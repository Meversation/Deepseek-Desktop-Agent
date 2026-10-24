using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeepseekDesktopAgent;

public sealed class AppConfig
{
    public string WorkDir { get; set; } = "";
    public bool AutoExecute { get; set; }
    public bool NightMode { get; set; }
    public int DelayMinSeconds { get; set; } = 3;
    public int DelayMaxSeconds { get; set; } = 20;
    public int OpacityPercent { get; set; } = 100;
    public bool EnterToSend { get; set; } = true;
    public string LastSessionUrl { get; set; } = "";
    public int BrowserPort { get; set; } = 9222;
}

public static class AppPaths
{
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deepseek-Desktop-Agent");

    public static string ConfigFile => Path.Combine(AppDataDir, "config.json");

    public static string BrowserProfileDir =>
        Path.Combine(Path.GetTempPath(), "deepseek_debug_profile");

    public static string CreateSessionTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_" + Environment.ProcessId + "_" + Random.Shared.Next(1000, 9999));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg != null) return Normalize(cfg);
            }
        }
        catch { }
        return Normalize(new AppConfig());
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
            var cfg = Normalize(config);
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(cfg, JsonOptions), new UTF8Encoding(false));
        }
        catch { }
    }

    private static AppConfig Normalize(AppConfig cfg)
    {
        if (cfg.DelayMinSeconds < 0) cfg.DelayMinSeconds = 0;
        if (cfg.DelayMaxSeconds < cfg.DelayMinSeconds) cfg.DelayMaxSeconds = cfg.DelayMinSeconds;
        if (cfg.OpacityPercent < 30) cfg.OpacityPercent = 30;
        if (cfg.OpacityPercent > 100) cfg.OpacityPercent = 100;
        if (cfg.BrowserPort <= 0 || cfg.BrowserPort > 65535) cfg.BrowserPort = 9222;
        return cfg;
    }
}

public static class Prompt
{
    public static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeepseekDesktopAgent.system_prompt.txt");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    public static string Build(string workDir, string tempDir)
    {
        return Load()
            .Replace("{{WORKDIR}}", workDir, StringComparison.Ordinal)
            .Replace("{{TEMP_WORKDIR}}", tempDir, StringComparison.Ordinal);
    }

    public static string EnsureSkills(string tempDir)
    {
        var target = Path.Combine(tempDir, "skill_all.zip");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeepseekDesktopAgent.skills.zip");
        if (stream != null)
        {
            using var file = File.Create(target);
            stream.CopyTo(file);
        }
        return target;
    }
}

public enum ToolKind
{
    PowerShell,
    Screenshot,
    UploadFile,
    Mouse,
    Keyboard,
    HttpGetFile
}

public sealed class ToolCall
{
    public ToolKind Kind { get; init; }
    public string Raw { get; init; } = "";
    public string Payload { get; init; } = "";
    public List<string> Paths { get; init; } = new();
    public List<int> Numbers { get; init; } = new();
    public bool FullScreen { get; init; }
    public IntPtr WindowHandle { get; set; }
    public string Url { get; init; } = "";
}

public sealed class ToolResult
{
    public string Message { get; init; } = "";
    public List<string> Files { get; init; } = new();
    public bool Executed { get; init; } = true;
}

public sealed class ConversationItem
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

public static class ToolProtocol
{
    public const string PowerShellHead = "# DeepseekDesktopAgent_ps_识别头_完全保留本行注释";
    public const string PowerShellTail = "# DeepseekDesktopAgent_ps_识别尾_完全保留本行注释";
    public const string ScreenshotHead = "# DeepseekDesktopAgent_getjpg_识别头_完全保留本行注释";
    public const string ScreenshotTail = "# DeepseekDesktopAgent_getjpg_识别尾_完全保留本行注释";
    public const string UploadHead = "# DeepseekDesktopAgent_getfile_识别头_完全保留本行注释";
    public const string UploadTail = "# DeepseekDesktopAgent_getfile_识别尾_完全保留本行注释";
    public const string MouseHead = "# DeepseekDesktopAgent_mouse_simulate_识别头_完全保留本行注释";
    public const string MouseTail = "# DeepseekDesktopAgent_mouse_simulate_识别尾_完全保留本行注释";
    public const string KeyboardHead = "# DeepseekDesktopAgent_keyboard_识别头_完全保留本行注释";
    public const string KeyboardTail = "# DeepseekDesktopAgent_keyboard_识别尾_完全保留本行注释";
    public const string HttpHead = "# DeepseekDesktopAgent_httpgetfile_识别头_完全保留本行注释";
    public const string HttpTail = "# DeepseekDesktopAgent_httpgetfile_识别尾_完全保留本行注释";
    public const string NoToolMarker = "//[工具][无需工具]//";

    public static bool HasMarkers(string text)
    {
        return text.Contains("_识别头_完全保留本行注释", StringComparison.Ordinal) ||
               text.Contains("_识别尾_完全保留本行注释", StringComparison.Ordinal);
    }

    public static ToolCall? Parse(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;

        var ps = ExtractBetween(reply, PowerShellHead, PowerShellTail)?.Trim('\r', '\n');
        if (!string.IsNullOrWhiteSpace(ps))
        {
            return new ToolCall { Kind = ToolKind.PowerShell, Raw = reply, Payload = ps, Numbers = new List<int>() };
        }

        var jpg = ExtractBetween(reply, ScreenshotHead, ScreenshotTail);
        if (jpg != null)
        {
            var value = ExtractBetween(jpg, "agent_getjpg_1_star(", ")_1_getjpg_end");
            if (value != null)
            {
                value = value.Trim();
                if (value == "屏幕" || value.Length == 0)
                    return new ToolCall { Kind = ToolKind.Screenshot, Raw = reply, Payload = jpg, FullScreen = true };
                if (long.TryParse(value, out var handle))
                    return new ToolCall { Kind = ToolKind.Screenshot, Raw = reply, Payload = jpg, WindowHandle = new IntPtr(handle) };
            }
            return new ToolCall { Kind = ToolKind.Screenshot, Raw = reply, Payload = jpg, FullScreen = true };
        }

        var files = ExtractBetween(reply, UploadHead, UploadTail);
        if (files != null)
        {
            var inner = ExtractBetween(files, "agent_getfile_head(", ")agent_getfile_end");
            var paths = ParsePathList(inner ?? files);
            return new ToolCall { Kind = ToolKind.UploadFile, Raw = reply, Payload = files, Paths = paths };
        }

        var mouse = ExtractBetween(reply, MouseHead, MouseTail);
        if (mouse != null)
        {
            var mcall = new ToolCall { Kind = ToolKind.Mouse, Raw = reply, Payload = mouse };
            for (var i = 1; i <= 3; i++)
            {
                var raw = ExtractBetween(mouse, $"agent_mouse_simulate_{i}_start(", $")_agent_mouse_simulate_{i}_end")?.Trim() ?? "";
                mcall.Numbers.Add(int.TryParse(raw, out var v) ? v : 0);
            }
            return mcall;
        }

        var keyboard = ExtractBetween(reply, KeyboardHead, KeyboardTail);
        if (keyboard != null)
        {
            var kcall = new ToolCall { Kind = ToolKind.Keyboard, Raw = reply, Payload = keyboard };
            for (var i = 1; i <= 3; i++)
            {
                var raw = ExtractBetween(keyboard, $"agent_keyboard_simulate_{i}_start(", $")_agent_keyboard_simulate_{i}_end")?.Trim() ?? "";
                kcall.Numbers.Add(int.TryParse(raw, out var v) ? v : 0);
            }
            var hRaw = ExtractBetween(keyboard, "agent_keyboard_simulate_4_start(", ")_agent_keyboard_simulate_4_end")?.Trim() ?? "";
            if (int.TryParse(hRaw, out var handle) && handle > 0)
                kcall.WindowHandle = new IntPtr(handle);
            return kcall;
        }

        var http = ExtractBetween(reply, HttpHead, HttpTail);
        if (http != null)
        {
            var url = ExtractBetween(http, "httpgetfile_1_star(", ")_httpgetfile_1_end")?.Trim() ?? "";
            return new ToolCall { Kind = ToolKind.HttpGetFile, Raw = reply, Payload = http, Url = url };
        }

        return null;
    }

    public static string? ExtractBetween(string text, string head, string tail)
    {
        var start = text.IndexOf(head, StringComparison.Ordinal);
        if (start < 0) return null;
        start += head.Length;
        var end = text.IndexOf(tail, start, StringComparison.Ordinal);
        if (end < 0) return null;
        return text.Substring(start, end - start);
    }

    public static List<string> ParsePathList(string raw)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(raw, "\"([^\"]+)\""))
        {
            var v = m.Groups[1].Value.Trim();
            if (v.Length > 0) list.Add(v);
        }
        if (list.Count == 0)
        {
            foreach (var part in raw.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var v = part.Trim().Trim('"', '\'');
                if (v.Length > 0) list.Add(v);
            }
        }
        return list;
    }

    private static List<int> ParseNumbers(string text, string pattern)
    {
        var result = new List<int>();
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
        {
            var value = m.Groups[1].Value.Trim();
            if (int.TryParse(value, out var n)) result.Add(n);
            else result.Add(0);
        }
        return result;
    }
}
