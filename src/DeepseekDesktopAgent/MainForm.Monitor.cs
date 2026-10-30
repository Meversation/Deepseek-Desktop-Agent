using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed partial class MainForm
{
    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _browserStarting, 1) == 1)
        {
            while (Volatile.Read(ref _browserStarting) != 0)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(200, ct);
            }
            return;
        }

        try
        {
            if (_browser == null)
            {
                _browser = new BrowserManager();
                _browser.Log += SetStatus;
                _guard.GetBrowserWindow = () => _browser?.Hwnd ?? IntPtr.Zero;
                _guard.Install();
                _tools = new ToolExecutor(_ps, this);
            }

            if (!_browser.IsAlive)
            {
                SetStatus("正在启动 DeepSeek 浏览器...");
                await _browser.StartAsync(_cfg.BrowserPort, ct);
            }

            if (_cdp == null)
            {
                _cdp = new CdpClient();
                _cdp.Log += SetStatus;
            }

            if (!_cdp.IsConnected)
            {
                SetStatus("正在连接 DeepSeek 调试页面...");
                await _cdp.ConnectAsync(_cfg.BrowserPort, ct);
            }
            await SyncDeepThinkStateAsync();

            // 浏览器默认隐藏在本程序后台运行，只有用户点“显示网页会话”时才作为独立窗口出现。
            if (_browser.Hwnd == IntPtr.Zero)
            {
                var bounds = await _cdp.GetWindowBoundsAsync(ct);
                var adopted = bounds is { } b && _browser.TryAdoptByBounds(b.Left, b.Top, b.Width, b.Height);
                if (!adopted) _browser.TryAdoptForegroundWindow();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _browserStarting, 0);
        }
    }

    /// <summary>用户手动点击“显示网页会话”：启动/复用浏览器后，把 DeepSeek 页面显示为独立窗口。</summary>
    private async Task ShowWebSessionAsync()
    {
        try
        {
            SetStatus("正在准备 DeepSeek 网页...");
            await EnsureBrowserAsync(CancellationToken.None);
            if (_browser == null || !_browser.IsAlive)
            {
                MessageBox.Show(this, "DeepSeek 浏览器尚未就绪，请稍后重试。", "Deepseek-Desktop-Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _browser.ShowAsWindow();
            _webShown = true;
            SetStatus("DeepSeek 网页已在独立窗口显示");
        }
        catch (Exception ex)
        {
            ShowError("显示 DeepSeek 网页失败", ex);
        }
    }
    private async void Watchdog_Tick(object? sender, EventArgs e)
    {
        if (_shuttingDown || _browser == null || _recovering) return;
        if (Volatile.Read(ref _browserStarting) != 0 || _browser.Starting) return;
        if (_browser.IsAlive) return;

        _recovering = true;
        try
        {
            if (_runCts is { IsCancellationRequested: false })
            {
                _runCts.Cancel();
                _ps.KillCurrent();
            }

            SetStatus("DeepSeek 窗口被关闭，正在恢复...");
            await _browser.StartAsync(_cfg.BrowserPort, CancellationToken.None);

            if (_cdp != null)
            {
                try { await _cdp.DisposeAsync(); } catch { }
            }
            _cdp = new CdpClient();
            await _cdp.ConnectAsync(_cfg.BrowserPort, CancellationToken.None);
            await _cdp.NavigateAsync(string.IsNullOrWhiteSpace(_lastSessionUrl) ? "https://chat.deepseek.com" : _lastSessionUrl);
            var bounds = await _cdp.GetWindowBoundsAsync(CancellationToken.None);
            var adopted = bounds is { } b && _browser.TryAdoptByBounds(b.Left, b.Top, b.Width, b.Height);
            if (!adopted && _browser.Hwnd == IntPtr.Zero)
                _browser.TryAdoptForegroundWindow();

            // 用户之前主动显示过网页窗口，恢复后继续保持为独立窗口；否则保持隐藏。
            if (_webShown) _browser.ShowAsWindow();

            SetStatus("DeepSeek 窗口已恢复");
        }
        catch (Exception ex)
        {
            SetStatus("恢复 DeepSeek 失败: " + ex.Message);
        }
        finally
        {
            _recovering = false;
        }
    }
    public async Task PrepareCaptureAsync()
    {
        await InvokeOnUi(this, () =>
        {
            _savedOpacity = Opacity;
            _marker = new MouseMarkerForm();
            _marker.ShowAtCursor();
            Visible = false;
        });
        await Task.Delay(400);
    }
    public async Task FinishCaptureAsync()
    {
        await InvokeOnUi(this, () =>
        {
            _marker?.Close();
            _marker?.Dispose();
            _marker = null;
            Visible = true;
            Opacity = _savedOpacity;
            Activate();
        });
    }
}
