using System.Diagnostics;
using System.Net.Http;

namespace DeepseekDesktopAgent;

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

    private readonly SemaphoreSlim _startGate = new(1, 1);

    public bool Starting { get; private set; }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        await _startGate.WaitAsync(ct);
        Starting = true;
        try
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
                    HideUntilEmbedded();
                    Log?.Invoke("已复用 9222 端口上的浏览器窗口");
                    return;
                }

                Log?.Invoke("9222 端口已被占用，尝试在现有浏览器中打开 DeepSeek...");
                if (await OpenDeepSeekPageAsync(port, ct))
                {
                    var reuseDeadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < reuseDeadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        _hwnd = FindDeepSeekWindow();
                        if (_hwnd != IntPtr.Zero) break;
                        await Task.Delay(500, ct);
                    }
                    if (_hwnd != IntPtr.Zero)
                    {
                        HideUntilEmbedded();
                        Log?.Invoke("已复用 9222 端口上的浏览器窗口");
                        return;
                    }
                }

                throw new InvalidOperationException("调试端口 9222 已被其他程序占用，且无法在其中打开 DeepSeek 窗口。请关闭占用该端口的浏览器后重试。");
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
                "--window-position=-32000,-32000",
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

            HideUntilEmbedded();
            Log?.Invoke("浏览器窗口已就绪");
        }
        finally
        {
            Starting = false;
            _startGate.Release();
        }
    }

    private void CaptureFloatingRect()
    {
        if (_hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var rect) && !rect.IsEmpty &&
            rect.Left > -10000 && rect.Top > -10000)
        {
            _floating = rect;
            return;
        }

        if (_floating.IsEmpty)
            _floating = new Native.RECT { Left = 160, Top = 100, Right = 1360, Bottom = 1000 };
    }

    /// <summary>浏览器先离屏启动，找到窗口后立即隐藏，直到内嵌完成再显示，避免启动闪窗。</summary>
    private void HideUntilEmbedded()
    {
        if (_hwnd == IntPtr.Zero) return;
        CaptureFloatingRect();
        // 隐藏会清掉 WS_VISIBLE，先记录原始样式，还原窗口时才能恢复成普通独立窗口。
        if (_savedStyle == 0)
            _savedStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_STYLE).ToInt64();
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
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
        if (_savedStyle == 0)
            _savedStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_STYLE).ToInt64();
        _host = host;
        _topCrop = Math.Max(0, topCrop);
        var childStyle = (_savedStyle &
                          ~(Native.WS_THICKFRAME | Native.WS_CAPTION | Native.WS_SYSMENU |
                            Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX | Native.WS_POPUP)) | Native.WS_CHILD;
        Log?.Invoke($"内嵌目标窗口 0x{_hwnd.ToInt64():X}，原样式 0x{_savedStyle:X8}");
        Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, new IntPtr(childStyle));
        var styleErr = Native.GetLastError();
        Native.SetParent(_hwnd, host);
        var parentErr = Native.GetLastError();
        if (Native.GetParent(_hwnd) != host)
        {
            Log?.Invoke($"SetParent 失败，窗口样式错误码 {styleErr}，父窗口错误码 {parentErr}，当前父窗口 0x{Native.GetParent(_hwnd).ToInt64():X}");
            Native.SetParent(_hwnd, IntPtr.Zero);
            Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, new IntPtr(_savedStyle));
            throw new InvalidOperationException("内嵌 DeepSeek 窗口失败，窗口句柄可能已失效。");
        }
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

        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var width = Math.Min(Math.Max(900, _floating.Width), area.Width);
        var height = Math.Min(Math.Max(650, _floating.Height), area.Height);
        var left = _floating.Left;
        var top = _floating.Top;
        if (_floating.IsEmpty)
        {
            left = 160;
            top = 100;
        }

        var maxLeft = Math.Max(area.Left, area.Right - width);
        var maxTop = Math.Max(area.Top, area.Bottom - height);
        left = Math.Clamp(left, area.Left, maxLeft);
        top = Math.Clamp(top, area.Top, maxTop);

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

    /// <summary>
    /// 把浏览器显示为独立窗口（不内嵌）。默认隐藏的浏览器只有用户主动要求查看时才显示。
    /// </summary>
    public void ShowAsWindow()
    {
        if (!IsAlive) return;
        if (_embedded)
        {
            Undock();
            return;
        }

        CaptureFloatingRect();
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var width = Math.Min(Math.Max(900, _floating.Width), area.Width);
        var height = Math.Min(Math.Max(650, _floating.Height), area.Height);
        var left = _floating.IsEmpty ? 160 : Math.Clamp(_floating.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var top = _floating.IsEmpty ? 100 : Math.Clamp(_floating.Top, area.Top, Math.Max(area.Top, area.Bottom - height));

        Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height,
            Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.ForceFocus(_hwnd);
        Log?.Invoke("DeepSeek 网页已作为独立窗口显示");
    }

    public void Focus()
    {
        if (IsAlive) Native.ForceFocus(_hwnd);
    }

    private static async Task<bool> OpenDeepSeekPageAsync(int port, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{port}/json/new?https://chat.deepseek.com");
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public bool TryAdoptForegroundWindow()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (fg == _hwnd) return true;
        if (!IsBrowserWindow(fg)) return false;
        Native.GetWindowThreadProcessId(fg, out var pid);
        if ((int)pid == Environment.ProcessId) return false;

        _hwnd = fg;
        CaptureFloatingRect();
        Log?.Invoke("已锁定当前 DeepSeek 窗口");
        return true;
    }

    public bool TryAdoptByBounds(double left, double top, double width, double height)
    {
        if (width <= 0 || height <= 0) return false;

        IntPtr best = IntPtr.Zero;
        double bestScore = double.MaxValue;
        foreach (var hWnd in Native.EnumTopLevelWindows())
        {
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            if ((int)pid == Environment.ProcessId) continue;
            if (!IsBrowserProcess((int)pid)) continue;
            if (!IsBrowserClass(Native.GetWindowClass(hWnd))) continue;
            if (Native.GetParent(hWnd) != IntPtr.Zero) continue;
            if (!Native.GetWindowRect(hWnd, out var rect)) continue;

            var scale = Native.GetScaleForWindow(hWnd);
            if (scale <= 0) scale = 1;
            var dx = Math.Abs(rect.Left - left * scale);
            var dy = Math.Abs(rect.Top - top * scale);
            var dw = Math.Abs(rect.Width - width * scale);
            var dh = Math.Abs(rect.Height - height * scale);
            var tolerance = 24 * scale;
            if (dx > tolerance || dy > tolerance || dw > tolerance * 2 || dh > tolerance * 2) continue;

            var score = dx + dy + dw + dh;
            if (score >= bestScore) continue;
            best = hWnd;
            bestScore = score;
        }

        if (best == IntPtr.Zero) return false;
        if (best == _hwnd) return true;
        _hwnd = best;
        CaptureFloatingRect();
        Log?.Invoke("已按调试目标定位 DeepSeek 窗口");
        return true;
    }

    private static bool IsBrowserClass(string cls) =>
        cls.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
        cls.Equals("Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase);

    private static bool IsBrowserWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Native.IsWindowVisible(hWnd)) return false;
        if (!IsBrowserClass(Native.GetWindowClass(hWnd))) return false;
        if (Native.GetParent(hWnd) != IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hWnd, out var pid);
        if (!IsBrowserProcess((int)pid)) return false;
        var title = Native.GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase)) return false;
        if (title.Contains("新建标签页", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("New Tab", StringComparison.OrdinalIgnoreCase)) return false;
        return title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 只认真正的 Chrome/Edge 进程，排除 Trae/QQ 等同样使用 Chrome_WidgetWin_1 的 Electron 程序。
    /// </summary>
    private static bool IsBrowserProcess(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            var name = p.ProcessName.ToLowerInvariant();
            if (name != "chrome" && name != "msedge") return false;
            var file = Path.GetFileName(p.MainModule?.FileName ?? "").ToLowerInvariant();
            return file == "chrome.exe" || file == "msedge.exe";
        }
        catch { return false; }
    }

    /// <summary>
    /// 查找 DeepSeek 浏览器主窗口。优先本程序启动的浏览器进程、可见窗口、标题含 DeepSeek、面积较大的窗口。
    /// </summary>
    public IntPtr FindDeepSeekWindow()
    {
        var launcherId = 0;
        try
        {
            if (_launcher is { HasExited: false }) launcherId = _launcher.Id;
        }
        catch { }

        var best = SearchWindows(visibleOnly: true, launcherId);
        if (best != IntPtr.Zero) return best;
        return SearchWindows(visibleOnly: false, launcherId);
    }

    private IntPtr SearchWindows(bool visibleOnly, int launcherId)
    {
        IntPtr best = IntPtr.Zero;
        long bestScore = long.MinValue;

        foreach (var hWnd in Native.EnumTopLevelWindows())
        {
            if (visibleOnly && !Native.IsWindowVisible(hWnd)) continue;
            if (!IsBrowserClass(Native.GetWindowClass(hWnd))) continue;
            if (Native.GetParent(hWnd) != IntPtr.Zero) continue;

            Native.GetWindowThreadProcessId(hWnd, out var pid);
            if (!IsBrowserProcess((int)pid)) continue;
            if (!Native.GetWindowRect(hWnd, out var rect)) continue;

            var area = (long)Math.Max(0, rect.Width) * Math.Max(0, rect.Height);
            if (area <= 0) continue;

            var title = Native.GetWindowTitle(hWnd);
            if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase)) continue;

            long score = 0;
            if (launcherId != 0 && (int)pid == launcherId) score += 1_000_000;
            if (title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) score += 100_000;
            if (Native.IsWindowVisible(hWnd) && !string.IsNullOrWhiteSpace(title)) score += 10_000;
            score += Math.Min(area / 2000, 9_000);

            if (score > bestScore)
            {
                bestScore = score;
                best = hWnd;
            }
        }

        return best;
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
        _startGate.Dispose();
    }
}
