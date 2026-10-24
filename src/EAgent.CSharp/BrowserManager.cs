using System.Diagnostics;
using System.Net.Http;

namespace EAgent;

public sealed class BrowserManager : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private Process? _launcher;
    private IntPtr _hwnd;
    private IntPtr _host;
    private Native.RECT _floating;
    private long _savedStyle;
    private bool _embedded;
    private int _topCrop;

    public int Port { get; private set; } = 9222;
    public bool ShuttingDown { get; private set; }
    public IntPtr Hwnd => _hwnd;
    public bool IsEmbedded => _embedded;
    public bool IsAlive => _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);
    public event Action<string>? Log;

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        Port = port;
        ShuttingDown = false;
        _embedded = false;
        _host = IntPtr.Zero;
        _hwnd = IntPtr.Zero;
        _savedStyle = 0;
        _topCrop = 0;

        if (await IsPortReadyAsync(port, ct))
        {
            var existing = FindDeepSeekWindow();
            if (existing != IntPtr.Zero)
            {
                _hwnd = existing;
                CaptureFloatingRect();
                Log?.Invoke("已复用 9222 端口上的浏览器窗口");
                return;
            }
        }

        var exe = FindBrowserExe();
        if (exe == null)
            throw new InvalidOperationException("未找到 Chrome 或 Edge，请先安装浏览器。");

        Directory.CreateDirectory(AppPaths.BrowserProfileDir);
        var args = string.Join(' ', new[]
        {
            $"--remote-debugging-port={port}",
            "--remote-allow-origins=*",
            $"--user-data-dir=\"{AppPaths.BrowserProfileDir}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-popup-blocking",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding",
            "--disable-features=CalculateNativeWinOcclusion",
            "--disable-infobars",
            "--disable-blink-features=AutomationControlled",
            "--test-type",
            "--lang=zh-CN",
            "--window-size=1200,900",
            "--window-position=120,60",
            "--app=https://chat.deepseek.com"
        });

        Log?.Invoke("正在启动浏览器: " + Path.GetFileName(exe));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            Arguments = args
        };
        _launcher = Process.Start(psi) ?? throw new InvalidOperationException("无法启动浏览器进程。");

        if (!await WaitPortAsync(port, 30000, ct))
            throw new TimeoutException("浏览器调试端口 9222 未在 30 秒内就绪。");

        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            _hwnd = FindDeepSeekWindow();
            if (_hwnd != IntPtr.Zero) break;
            await Task.Delay(500, ct);
        }

        if (_hwnd == IntPtr.Zero)
            throw new TimeoutException("未能找到 DeepSeek 浏览器窗口。");

        CaptureFloatingRect();
        Log?.Invoke("浏览器窗口已就绪");
    }

    private void CaptureFloatingRect()
    {
        if (_hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var rect) && !rect.IsEmpty)
            _floating = rect;
    }

    public void Embed(IntPtr host, int topCrop)
    {
        if (!IsAlive) throw new InvalidOperationException("DeepSeek 窗口不存在。");
        if (host == IntPtr.Zero) throw new InvalidOperationException("内嵌容器句柄无效。");

        if (_embedded)
        {
            _topCrop = Math.Max(0, topCrop);
            UpdateLayout();
            return;
        }

        CaptureFloatingRect();
        _savedStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_STYLE).ToInt64();
        _host = host;
        _topCrop = Math.Max(0, topCrop);
        Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, new IntPtr(_savedStyle & ~Native.WS_THICKFRAME));
        Native.SetParent(_hwnd, host);
        _embedded = true;
        UpdateLayout();
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.ForceFocus(_hwnd);
        Log?.Invoke("DeepSeek 窗口已内嵌");
    }

    public void UpdateLayout()
    {
        if (!_embedded || !IsAlive || _host == IntPtr.Zero) return;
        if (!Native.GetClientRect(_host, out var client)) return;
        var width = Math.Max(1, client.Width);
        var height = Math.Max(1, client.Height);
        Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, -_topCrop, width, height + _topCrop,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    public void Undock()
    {
        if (!_embedded || !IsAlive) return;

        var width = Math.Max(900, _floating.Width);
        var height = Math.Max(650, _floating.Height);
        var left = _floating.Left;
        var top = _floating.Top;
        if (_floating.IsEmpty)
        {
            left = 160;
            top = 100;
        }

        Native.SetParent(_hwnd, IntPtr.Zero);
        _embedded = false;
        _host = IntPtr.Zero;
        if (_savedStyle != 0)
            Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, new IntPtr(_savedStyle));
        Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height,
            Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.ForceFocus(_hwnd);
        Log?.Invoke("DeepSeek 窗口已还原为独立窗口");
    }

    public void Focus()
    {
        if (IsAlive) Native.ForceFocus(_hwnd);
    }

    public IntPtr FindDeepSeekWindow()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;

        foreach (var hWnd in Native.EnumTopLevelWindows())
        {
            if (!Native.IsWindowVisible(hWnd)) continue;
            var cls = Native.GetWindowClass(hWnd);
            if (!string.Equals(cls, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase)) continue;

            var title = Native.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase)) continue;

            var isDeepSeek = title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase);
            Native.GetWindowRect(hWnd, out var rect);
            var area = (long)Math.Max(0, rect.Width) * Math.Max(0, rect.Height);

            if (isDeepSeek && area > bestArea)
            {
                best = hWnd;
                bestArea = area;
            }
        }

        if (best != IntPtr.Zero) return best;

        foreach (var hWnd in Native.EnumTopLevelWindows())
        {
            if (!Native.IsWindowVisible(hWnd)) continue;
            if (!string.Equals(Native.GetWindowClass(hWnd), "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase)) continue;
            var title = Native.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase)) continue;
            if (title.Contains("新建标签页", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("New Tab", StringComparison.OrdinalIgnoreCase)) continue;

            Native.GetWindowThreadProcessId(hWnd, out var pid);
            var name = GetProcessName((int)pid);
            if (name != "chrome" && name != "msedge") continue;

            Native.GetWindowRect(hWnd, out var rect);
            var area = (long)Math.Max(0, rect.Width) * Math.Max(0, rect.Height);
            if (area > bestArea)
            {
                best = hWnd;
                bestArea = area;
            }
        }

        return best;
    }

    private static string GetProcessName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName.ToLowerInvariant(); }
        catch { return ""; }
    }

    private static string? FindBrowserExe()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> IsPortReadyAsync(int port, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync($"http://127.0.0.1:{port}/json/version", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitPortAsync(int port, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsPortReadyAsync(port, ct)) return true;
            await Task.Delay(300, ct);
        }
        return false;
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        ShuttingDown = true;
        try { await CdpClient.CloseBrowserAsync(Port, ct); } catch { }
        await Task.Delay(800, ct);

        try
        {
            if (IsAlive)
            {
                Native.GetWindowThreadProcessId(_hwnd, out var pid);
                if (pid != 0)
                {
                    using var p = Process.GetProcessById((int)pid);
                    p.Kill(entireProcessTree: true);
                }
            }
        }
        catch { }

        try
        {
            if (_launcher != null && !_launcher.HasExited)
                _launcher.Kill(entireProcessTree: true);
        }
        catch { }

        _hwnd = IntPtr.Zero;
        _embedded = false;
    }

    public void Dispose()
    {
        _launcher?.Dispose();
    }
}
