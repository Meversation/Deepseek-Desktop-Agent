using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath =>
        Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_debug.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch { }
    }
}

public static class Diagnostics
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static int Run()
    {
        var sb = new StringBuilder();
        var cfg = ConfigService.Load();
        var port = cfg.BrowserPort;

        sb.AppendLine("Deepseek-Desktop-Agent 诊断报告");
        sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("版本: " + UpdateChecker.CurrentVersion);
        sb.AppendLine("系统: " + Environment.OSVersion + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
        sb.AppendLine("进程: " + Environment.ProcessId);
        sb.AppendLine();

        sb.AppendLine("[路径]");
        sb.AppendLine("配置: " + AppPaths.ConfigFile + (File.Exists(AppPaths.ConfigFile) ? " (存在)" : " (不存在)"));
        sb.AppendLine("浏览器配置目录: " + AppPaths.BrowserProfileDir + (Directory.Exists(AppPaths.BrowserProfileDir) ? " (存在)" : " (不存在)"));
        sb.AppendLine("日志: " + AppLog.FilePath);
        sb.AppendLine();

        sb.AppendLine("[配置]");
        sb.AppendLine("工作目录: " + cfg.WorkDir);
        sb.AppendLine("端口: " + port);
        sb.AppendLine("上次会话: " + (string.IsNullOrWhiteSpace(cfg.LastSessionUrl) ? "(空)" : cfg.LastSessionUrl));
        sb.AppendLine("夜间模式: " + cfg.NightMode + " / 自动执行: " + cfg.AutoExecute);
        sb.AppendLine();

        sb.AppendLine("[调试端口]");
        try
        {
            var version = Http.GetStringAsync($"http://127.0.0.1:{port}/json/version").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(version);
            sb.AppendLine("端口可用: 是");
            foreach (var name in new[] { "Browser", "webSocketDebuggerUrl", "User-Agent" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value))
                    sb.AppendLine("  " + name + ": " + value.GetString());
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("端口可用: 否 (" + ex.Message + ")");
        }
        sb.AppendLine();

        sb.AppendLine("[CDP 页面]");
        try
        {
            var json = Http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var index = 0;
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                var type = page.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue;
                index++;
                var url = page.TryGetProperty("url", out var u) ? u.GetString() : "";
                var title = page.TryGetProperty("title", out var ti) ? ti.GetString() : "";
                sb.AppendLine($"  #{index} {url}");
                sb.AppendLine($"     标题: {title}");
            }
            sb.AppendLine("  DeepSeek 页面数: " +
                doc.RootElement.EnumerateArray().Count(p =>
                    p.TryGetProperty("url", out var u) &&
                    (u.GetString() ?? "").Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            sb.AppendLine("  读取失败: " + ex.Message);
        }
        sb.AppendLine();

        sb.AppendLine("[浏览器进程]");
        foreach (var name in new[] { "msedge", "chrome" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero && process.Id == 0) continue;
                    sb.AppendLine($"  {name} pid={process.Id} 窗口={process.MainWindowHandle} 标题={process.MainWindowTitle}");
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        sb.AppendLine();

        sb.AppendLine("[顶层窗口]");
        var browser = new BrowserManager();
        foreach (var hWnd in Native.EnumTopLevelWindows())
        {
            var cls = Native.GetWindowClass(hWnd);
            if (!cls.Equals("Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) &&
                !cls.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Native.GetWindowRect(hWnd, out var rect)) continue;
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            var title = Native.GetWindowTitle(hWnd);
            sb.AppendLine($"  hwnd=0x{hWnd.ToInt64():X} pid={pid} 类={cls} 可见={Native.IsWindowVisible(hWnd)} 父=0x{Native.GetParent(hWnd).ToInt64():X}");
            sb.AppendLine($"     进程: {DescribeProcess((int)pid)}");
            sb.AppendLine($"     标题: {title}");
            sb.AppendLine($"     位置: {rect.Left},{rect.Top} {rect.Width}x{rect.Height} 缩放={Native.GetScaleForWindow(hWnd)}");
        }
        sb.AppendLine();

        var found = browser.FindDeepSeekWindow();
        sb.AppendLine("[定位结果]");
        sb.AppendLine("选择窗口: 0x" + found.ToInt64().ToString("X"));
        sb.AppendLine("说明: 0 表示未找到，非 0 表示将用于内嵌的窗口句柄");
        browser.Dispose();
        sb.AppendLine();

        sb.AppendLine("[日志尾部]");
        try
        {
            if (File.Exists(AppLog.FilePath))
            {
                var lines = File.ReadAllLines(AppLog.FilePath, Encoding.UTF8);
                foreach (var line in lines.Skip(Math.Max(0, lines.Length - 40)))
                    sb.AppendLine("  " + line);
            }
            else sb.AppendLine("  (无日志)");
        }
        catch (Exception ex)
        {
            sb.AppendLine("  读取失败: " + ex.Message);
        }

        var report = sb.ToString();
        var path = Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_diagnose.txt");
        try { File.WriteAllText(path, report, new UTF8Encoding(false)); } catch { }

        AppLog.Write("诊断报告已生成: " + path);
        try
        {
            AttachConsole(-1);
            Console.Out.WriteLine(report);
            Console.Out.WriteLine("报告文件: " + path);
        }
        catch { }

        return 0;
    }

    private static string DescribeProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName + " " + (p.MainModule?.FileName ?? "(无路径)");
        }
        catch { return "(无法读取)"; }
    }
}
