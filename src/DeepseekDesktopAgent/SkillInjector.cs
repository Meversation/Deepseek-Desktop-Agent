using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepseekDesktopAgent;

/// <summary>一个可用技能：名称、摘要、命中关键词与正文。</summary>
public sealed class SkillDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public string Content { get; init; } = string.Empty;
    public string EntryPath { get; init; } = string.Empty;
}

/// <summary>注入结果：拼好的提示词片段、命中的技能名、字符数。</summary>
public sealed record InjectionResult(string Text, IReadOnlyList<string> UsedSkills, int TotalChars)
{
    public static readonly InjectionResult Empty = new(string.Empty, Array.Empty<string>(), 0);
}

/// <summary>
/// 会话级技能注入器：从 skill_all.zip 或已解压目录读取 SKILL.md，
/// 按本轮提示词的关键词命中挑选相关技能，把正文拼进提示词。
/// 只用 BCL，无 WinForms 依赖，任何 IO/压缩异常都不会抛出。
/// </summary>
public sealed class SkillInjector
{
    private static readonly Regex WordPattern = new(@"[A-Za-z][A-Za-z0-9_\-\.]{1,}", RegexOptions.Compiled);
    private static readonly char[] KeywordSeparators = { ',', '，', '、', ';', '；', '|', ' ' };

    private readonly string _source;
    private List<SkillDescriptor> _skills = new();
    private bool _loaded;

    public SkillInjector(string? skillSource)
    {
        _source = skillSource?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<SessionLoadError> LastErrors { get; private set; } = Array.Empty<SessionLoadError>();

    public IReadOnlyList<SkillDescriptor> Skills
    {
        get
        {
            EnsureLoaded();
            return _skills;
        }
    }

    /// <summary>重新扫描技能来源；失败时保留上一次结果并记录错误。</summary>
    public void Reload()
    {
        var errors = new List<SessionLoadError>();
        var list = new List<SkillDescriptor>();
        try
        {
            if (_source.Length == 0) { }
            else if (Directory.Exists(_source))
            {
                foreach (var file in Directory.EnumerateFiles(_source, "SKILL.md", SearchOption.AllDirectories))
                    AddFromFile(file, list, errors);
            }
            else if (File.Exists(_source))
            {
                AddFromZip(_source, list, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(_source, ex.Message));
        }

        _skills = list;
        _loaded = true;
        LastErrors = errors;
    }

    /// <summary>
    /// 按提示词挑选技能并拼接；没有命中返回空结果，超预算按行边界截断。
    /// </summary>
    public InjectionResult Build(string? userPrompt, int maxChars = 6000)
    {
        try
        {
            EnsureLoaded();
            var prompt = userPrompt ?? string.Empty;
            if (prompt.Length == 0 || maxChars <= 0 || _skills.Count == 0) return InjectionResult.Empty;

            var scored = new List<(SkillDescriptor Skill, int Score)>();
            foreach (var skill in _skills)
            {
                var score = Score(skill, prompt);
                if (score > 0) scored.Add((skill, score));
            }
            if (scored.Count == 0) return InjectionResult.Empty;

            scored.Sort(static (a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0
                    ? byScore
                    : string.Compare(a.Skill.Name, b.Skill.Name, StringComparison.OrdinalIgnoreCase);
            });

            var builder = new StringBuilder();
            var used = new List<string>();
            builder.Append("## 相关技能\n");
            foreach (var (skill, _) in scored)
            {
                var section = BuildSection(skill);
                if (builder.Length + section.Length <= maxChars)
                {
                    builder.Append(section);
                    used.Add(skill.Name);
                    continue;
                }

                var remain = maxChars - builder.Length;
                if (remain > 40)
                {
                    builder.Append(Truncate(section, remain));
                    used.Add(skill.Name);
                }
                break;
            }

            var text = builder.ToString();
            if (used.Count == 0) return InjectionResult.Empty;
            if (text.Length > maxChars) text = text[..maxChars];
            return new InjectionResult(text, used, text.Length);
        }
        catch (Exception ex)
        {
            LastErrors = LastErrors.Append(new SessionLoadError(_source, ex.Message)).ToArray();
            return InjectionResult.Empty;
        }
    }

    private void EnsureLoaded()
    {
        if (!_loaded) Reload();
    }

    private static int Score(SkillDescriptor skill, string prompt)
    {
        var hits = 0;
        foreach (var keyword in skill.Keywords)
        {
            if (keyword.Length < 2) continue;
            if (prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase)) hits++;
        }
        return hits;
    }

    private static string BuildSection(SkillDescriptor skill)
    {
        var builder = new StringBuilder();
        builder.Append("### ").Append(skill.Name).Append('\n');
        if (skill.Summary.Length > 0) builder.Append(skill.Summary).Append('\n');
        builder.Append(skill.Content.Trim()).Append("\n\n");
        return builder.ToString();
    }

    private static string Truncate(string text, int limit)
    {
        if (text.Length <= limit) return text;
        var cut = text.LastIndexOf('\n', Math.Max(0, limit - 1));
        if (cut < limit / 2) cut = limit;
        return text[..cut].TrimEnd() + "\n（已截断）\n";
    }

    private static void AddFromFile(string path, List<SkillDescriptor> list, List<SessionLoadError> errors)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            list.Add(Parse(path, text));
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(path, ex.Message));
        }
    }

    private static void AddFromZip(string path, List<SkillDescriptor> list, List<SessionLoadError> errors)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                list.Add(Parse(entry.FullName, reader.ReadToEnd()));
            }
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(path, ex.Message));
        }
    }

    private static SkillDescriptor Parse(string path, string text)
    {
        var name = string.Empty;
        var summary = string.Empty;
        var keywords = new List<string>();
        var body = text.Trim();

        // 解析可选的 front-matter。
        if (body.StartsWith("---", StringComparison.Ordinal))
        {
            var end = body.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var header = body[3..end];
                body = body[(end + 4)..].Trim();
                foreach (var line in header.Split('\n'))
                {
                    var pair = line.Split(':', 2);
                    if (pair.Length != 2) continue;
                    var key = pair[0].Trim().ToLowerInvariant();
                    var value = pair[1].Trim();
                    switch (key)
                    {
                        case "name": name = value; break;
                        case "description":
                        case "summary": summary = value; break;
                        case "keywords":
                            keywords.AddRange(value.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries));
                            break;
                    }
                }
            }
        }

        var lines = body.Split('\n');
        if (name.Length == 0)
        {
            foreach (var line in lines)
            {
                if (!line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                name = line.Trim().TrimStart('#').Trim();
                break;
            }
        }
        if (name.Length == 0) name = Path.GetFileName(Path.GetDirectoryName(path)) ?? "未命名技能";
        if (summary.Length == 0)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                summary = trimmed;
                break;
            }
        }

        if (keywords.Count == 0)
        {
            foreach (var word in WordPattern.Matches(name + " " + summary).Select(m => m.Value))
                keywords.Add(word);
        }

        var distinct = keywords
            .Where(k => k.Length >= 2)
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SkillDescriptor
        {
            Name = name.Trim(),
            Summary = summary.Trim(),
            Keywords = distinct,
            Content = body.Trim(),
            EntryPath = path
        };
    }
}

internal static class SkillInjectorSelfTest
{
    public static string[] Run(string root)
    {
        var failures = new List<string>();
        var testRoot = Path.Combine(root, "skills_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(testRoot);
            var zipPath = Path.Combine(testRoot, "skill_all.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "ps/SKILL.md",
                    "---\nname: PowerShell 脚本\ndescription: 编写与调试 PowerShell\nkeywords: PowerShell, 注册表, 脚本\n---\n# PowerShell\n永远显式输出 OK。\n");
                WriteEntry(archive, "markdown/SKILL.md",
                    "---\nname: Markdown 渲染\ndescription: 表格与转义\nkeywords: Markdown, 表格, RTF\n---\n# Markdown\n表格需要 trowd。\n");
                WriteEntry(archive, "empty/SKILL.md", "没有标题的说明\n正文");
            }

            var injector = new SkillInjector(zipPath);
            if (injector.Skills.Count != 3) failures.Add($"zip 技能数应为 3，实际 {injector.Skills.Count}");

            var hit = injector.Build("帮我写个 PowerShell 脚本导出注册表", 6000);
            if (hit.UsedSkills.Count == 0) failures.Add("命中关键词时未选中技能");
            else if (hit.UsedSkills[0] != "PowerShell 脚本") failures.Add("排序错误: " + string.Join(",", hit.UsedSkills));
            if (!hit.Text.Contains("## 相关技能")) failures.Add("缺少小节标题");
            if (hit.TotalChars != hit.Text.Length) failures.Add("字符数统计不一致");

            var miss = injector.Build("今天天气怎么样");
            if (miss.Text.Length != 0) failures.Add("无命中时应返回空文本");

            var tight = injector.Build("PowerShell 脚本 注册表", 80);
            if (tight.Text.Length > 80) failures.Add($"预算截断失效: {tight.Text.Length}");

            var again = injector.Build("帮我写个 PowerShell 脚本导出注册表", 6000);
            if (again.Text != hit.Text) failures.Add("两次结果不一致");

            var dirSource = Path.Combine(testRoot, "dir");
            Directory.CreateDirectory(Path.Combine(dirSource, "tool-a"));
            Directory.CreateDirectory(Path.Combine(dirSource, "tool-b"));
            File.WriteAllText(Path.Combine(dirSource, "tool-a", "SKILL.md"),
                "---\nname: Tool A\ndescription: 甲工具\nkeywords: alpha\n---\n甲正文", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dirSource, "tool-b", "SKILL.md"),
                "---\nname: Tool B\ndescription: 乙工具\nkeywords: beta\n---\n乙正文", new UTF8Encoding(false));

            var dirInjector = new SkillInjector(dirSource);
            if (dirInjector.Skills.Count != 2) failures.Add($"目录来源技能数应为 2，实际 {dirInjector.Skills.Count}");
            var dirHit = dirInjector.Build("请用 alpha 方式处理", 6000);
            if (dirHit.UsedSkills.Count != 1 || dirHit.UsedSkills[0] != "Tool A") failures.Add("目录来源命中错误");

            var missing = new SkillInjector(Path.Combine(testRoot, "nope.zip"));
            var empty = missing.Build("PowerShell", 6000);
            if (empty.Text.Length != 0) failures.Add("不存在来源应返回空结果");

            File.WriteAllText(Path.Combine(testRoot, "broken.zip"), "not a zip", new UTF8Encoding(false));
            var broken = new SkillInjector(Path.Combine(testRoot, "broken.zip"));
            if (broken.Skills.Count != 0) failures.Add("损坏 zip 不应加载出技能");
            if (broken.LastErrors.Count == 0) failures.Add("损坏 zip 未记录错误");

            if (new SkillInjector(zipPath).Build("", 6000).Text.Length != 0) failures.Add("空提示词应返回空结果");
            if (new SkillInjector(zipPath).Build("PowerShell", 0).Text.Length != 0) failures.Add("非法预算应返回空结果");
        }
        catch (Exception ex)
        {
            failures.Add("技能注入自检异常: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
        }
        return failures.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
