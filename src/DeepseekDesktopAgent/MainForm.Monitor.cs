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
            RefreshWebStateLabel();
            SetStatus("DeepSeek 网页已在独立窗口显示");
        }
        catch (Exception ex)
        {
            ShowError("显示 DeepSeek 网页失败", ex);
        }
    }
    private async void Watchdog_Tick(object? sender, EventArgs e)
    {
        RefreshWebStateLabel();
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
            // 新连接不可能已经停留在某个会话页面上，清掉“网页已就绪”标记，下次发送时按会话重新切换。
            _webSessionReady = null;
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
    /// <summary>详情页“DeepSeek 网页”状态：未启动 / 启动中 / 运行中（隐藏）/ 运行中（窗口已显示）/ 调试连接断开。</summary>
    private void RefreshWebStateLabel()
    {
        if (_lblWebState == null || _lblWebState.IsDisposed) return;

        var starting = Volatile.Read(ref _browserStarting) != 0 || (_browser != null && _browser.Starting);
        string text;
        Color color;

        if (_recovering)
        {
            text = "状态：浏览器恢复中…";
            color = ThemeManager.Current.Accent;
        }
        else if (starting)
        {
            text = "状态：浏览器启动中…";
            color = ThemeManager.Current.Accent;
        }
        else if (_browser == null || !_browser.IsAlive)
        {
            text = "状态：未启动（发送消息时自动启动）";
            color = ThemeManager.Current.SubtleText;
        }
        else if (_cdp == null || !_cdp.IsConnected)
        {
            text = "状态：运行中（隐藏），调试连接已断开";
            color = ThemeManager.Current.Text;
        }
        else if (_webShown)
        {
            text = "状态：运行中（窗口已显示）";
            color = ThemeManager.Current.Accent;
        }
        else
        {
            text = "状态：运行中（后台隐藏）";
            color = ThemeManager.Current.Accent;
        }

        if (text == _lastWebStateText && _lblWebState.ForeColor == color) return;
        _lastWebStateText = text;
        _lblWebState.Text = text;
        _lblWebState.ForeColor = color;
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
