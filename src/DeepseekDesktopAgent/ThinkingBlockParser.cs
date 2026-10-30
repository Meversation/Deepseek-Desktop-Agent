using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepseekDesktopAgent;

/// <summary>解析后的思考块。Summary 为“已深度思考（用时 N 秒）”这类标题，Body 为思考正文。</summary>
public sealed class ThinkingBlock
{
    public string Summary { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int? DurationSeconds { get; init; }
    public bool IsComplete { get; init; } = true;
    public bool IsEmpty => Summary.Length == 0 && Body.Length == 0;
}

/// <summary>
/// 把网页端思考块片段（HTML 或纯文本）规范化为可展示的文本。
/// 纯函数、无 WinForms 依赖；输入异常时返回空对象，绝不抛异常。
/// </summary>
public static class ThinkingBlockParser
{
    /// <summary>正文默认截断长度。</summary>
    public const int DefaultMaxChars = 20000;

    private static readonly Regex TagBreak = new(@"(?i)<\s*(br|/p|/div|/li|/h[1-6]|/tr)\s*/?\s*>", RegexOptions.Compiled);
    private static readonly Regex TagAny = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex DurationPattern = new(@"用时\s*(\d+)\s*(秒|分钟|分)", RegexOptions.Compiled);
    private static readonly Regex SummaryPattern = new(@"(已深度思考|思考中|已思考|深度思考)", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>解析 HTML 片段；摘要与用时从首个匹配行提取。</summary>
    public static ThinkingBlock Parse(string? html, int maxChars = DefaultMaxChars)
    {
        try
        {
            var text = NormalizeText(HtmlToText(html), maxChars);
            return Split(text);
        }
        catch
        {
            return new ThinkingBlock();
        }
    }

    /// <summary>解析已经是纯文本的内容；摘要、用时、完成状态可由调用方直接给出。</summary>
    public static ThinkingBlock ParseText(string? text, string? summary = null, int? durationSeconds = null,
        bool complete = true, int maxChars = DefaultMaxChars)
    {
        try
        {
            var normalized = NormalizeText(text ?? string.Empty, maxChars);
            var block = Split(normalized);
            var hasSummary = !string.IsNullOrWhiteSpace(summary);
            var finalSummary = hasSummary ? summary!.Trim() : block.Summary;
            var body = hasSummary ? normalized : block.Body;
            return new ThinkingBlock
            {
                Summary = finalSummary,
                Body = body,
                DurationSeconds = durationSeconds ?? ParseDuration(finalSummary.Length > 0 ? finalSummary : normalized),
                IsComplete = complete
            };
        }
        catch
        {
            return new ThinkingBlock { IsComplete = complete };
        }
    }

    /// <summary>把 HTML 片段转成纯文本：块级标签换行、去标签、解实体。</summary>
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var withBreaks = TagBreak.Replace(html!, "\n");
        var stripped = TagAny.Replace(withBreaks, string.Empty);
        return WebUtility.HtmlDecode(stripped);
    }

    private static string NormalizeText(string text, int maxChars)
    {
        var value = text.Replace("\r\n", "\n").Replace('\r', '\n');
        value = BlankLines.Replace(value, "\n\n").Trim();

        var limit = maxChars <= 0 ? DefaultMaxChars : maxChars;
        if (value.Length <= limit) return value;

        // 按行边界截断，避免切在半个字/半行上。
        var cut = value.LastIndexOf('\n', Math.Max(0, limit - 1));
        if (cut < limit / 2) cut = limit;
        return value[..cut].TrimEnd() + "\n（已截断）";
    }

    private static ThinkingBlock Split(string text)
    {
        if (text.Length == 0) return new ThinkingBlock();

        var lines = text.Split('\n');
        var summary = string.Empty;
        var bodyStart = 0;
        for (var i = 0; i < Math.Min(lines.Length, 3); i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (!SummaryPattern.IsMatch(line)) break;
            summary = line;
            bodyStart = i + 1;
            break;
        }

        var body = string.Join('\n', lines.Skip(bodyStart)).Trim();
        var duration = ParseDuration(summary.Length > 0 ? summary : text);
        var complete = !text.Contains("思考中", StringComparison.Ordinal) &&
                       !text.Contains("正在思考", StringComparison.Ordinal);
        return new ThinkingBlock
        {
            Summary = summary,
            Body = body,
            DurationSeconds = duration,
            IsComplete = complete
        };
    }

    private static int? ParseDuration(string text)
    {
        var match = DurationPattern.Match(text);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var value)) return null;
        return match.Groups[2].Value == "秒" ? value : value * 60;
    }

    /// <summary>供界面展示的一行摘要，没有摘要时按完成状态给兜底文案。</summary>
    public static string FormatSummary(ThinkingBlock block)
    {
        if (block == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(block.Summary)) return block.Summary;
        if (!block.IsComplete) return "思考中…";
        return block.DurationSeconds is { } seconds ? $"已深度思考（用时 {seconds} 秒）" : "已深度思考";
    }
}

internal static class ThinkingBlockParserSelfTest
{
    public static string[] Run()
    {
        var failures = new List<string>();

        var expanded = ThinkingBlockParser.Parse(
            "<div class=\"think\">已深度思考（用时 18 秒）<br>先读代码，<br>再改滚动条。</div>");
        if (expanded.Summary != "已深度思考（用时 18 秒）") failures.Add("摘要解析失败: " + expanded.Summary);
        if (!expanded.Body.Contains("先读代码") || !expanded.Body.Contains("再改滚动条")) failures.Add("正文换行解析失败");
        if (expanded.DurationSeconds != 18) failures.Add($"用时解析失败: {expanded.DurationSeconds}");
        if (!expanded.IsComplete) failures.Add("完成态判定失败");

        var folded = ThinkingBlockParser.Parse("<p>已思考</p>");
        if (folded.Summary != "已思考") failures.Add("折叠态摘要失败");
        if (folded.DurationSeconds != null) failures.Add("无用时不应给出秒数");

        var running = ThinkingBlockParser.Parse("思考中\n正在分析页面结构");
        if (running.IsComplete) failures.Add("思考中应判定为未完成");

        var empty = ThinkingBlockParser.Parse(null);
        if (!empty.IsEmpty) failures.Add("空输入应返回空对象");

        var plain = ThinkingBlockParser.ParseText("先分析\n再实现", "已深度思考（用时 2 分钟）");
        if (plain.Body != "先分析\n再实现") failures.Add("纯文本正文被污染: " + plain.Body);
        if (plain.DurationSeconds != 120) failures.Add($"分钟换算失败: {plain.DurationSeconds}");

        var entity = ThinkingBlockParser.Parse("<div>a &amp; b &lt;c&gt;</div>");
        if (!entity.Body.Contains("a & b <c>")) failures.Add("HTML 实体解码失败: " + entity.Body);

        var longText = new StringBuilder();
        for (var i = 0; i < 5000; i++) longText.Append("第").Append(i).Append("行内容\n");
        var truncated = ThinkingBlockParser.Parse(longText.ToString(), 500);
        if (truncated.Body.Length > 600 || !truncated.Body.EndsWith("（已截断）")) failures.Add("超长截断失败");

        return failures.ToArray();
    }
}
