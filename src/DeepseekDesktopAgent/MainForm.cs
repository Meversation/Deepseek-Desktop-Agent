using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed partial class MainForm : Form, IToolHost, IMessageFilter
{
    private AppConfig _cfg = ConfigService.Load();
    private readonly RuntimeStateStore _runtime = new();
    private readonly SessionStore _sessions = new();
    private Guid? _sessionId;
    private Guid? _turnId;
    private readonly AgentRuntimeState _runtimeState = new()
    {
        Pid = Environment.ProcessId,
        StartedAt = DateTimeOffset.Now
    };

    private readonly string _tempDir = AppPaths.CreateSessionTempDir();
    private readonly PowerShellRunner _ps;
    private readonly KeyboardCloseGuard _guard = new();
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 120 };

    private CdpClient? _cdp;
    private BrowserManager? _browser;
    private ToolExecutor? _tools;
    private CancellationTokenSource? _runCts;
    private MouseMarkerForm? _marker;
    private double _savedOpacity = 1.0;

    private bool _busy;
    private bool _shuttingDown;
    private bool _allowClose;
    private bool _recovering;
    private int _browserStarting;
    private int _userMessageCount;
    private int _toolCount;
    private int _currentRunIndex = -1;
    private string _lastSessionUrl = "";
    private readonly List<string> _attachedFiles = new();
    // Agent 运行期间用户提交的消息先入队，等本轮全部工作（含所有工具调用）结束后再依次发送。
    private readonly Queue<QueuedMessage> _queue = new();
    private readonly object _queueLock = new();
    private volatile bool _waitPageIdleAfterStop;

    private Label _lblWorkDir = null!;
    private Label _lblStatus = null!;
    private Label _lblAttach = null!;
    private Label _lblInputHint = null!;
    private Label _lblPsState = null!;
    private Label _lblOpacity = null!;
    private Label _lblInputPlaceholder = null!;
    private Label _lblDetailWorkDir = null!;
    private FlowLayoutPanel _detailFlow = null!;
    private TextBox _txtDetailPrompt = null!;
    private ScrollMask _detailPromptMask = null!;
    private LineScrollBar _detailPromptScroll = null!;
    private Button _btnChooseDir2 = null!;
    private Button _btnShowWeb = null!;
    private bool _webShown;
    private RoundedPanel _cardReply = null!;
    private RoundedPanel _cardInput = null!;
    private ThinkingIndicator _thinking = null!;
    private BufferedTableLayoutPanel _convoLayout = null!;
    private FlowLayoutPanel _settingsFlow = null!;
    private Button _btnChooseDir = null!;
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private Button _btnSend = null!;
    private Button _btnAttach = null!;
    private Button _btnClearAttach = null!;
    private Button _btnSwitch = null!;
    private Button _btnClearRuns = null!;
    private Button _btnOpenTemp = null!;
    private RadioButton _rbNew = null!;
    private RadioButton _rbOld = null!;
    private CheckBox _chkNight = null!;
    private CheckBox _chkAuto = null!;
    private CheckBox _chkDeepThink = null!;
    private CheckBox _chkEnterSend = null!;
    private ListBox _lstSessions = null!;
    private TextBox _txtSessionFilter = null!;
    private RichTextBox _sessionDetail = null!;
    private ScrollMask _detailMask = null!;
    private LineScrollBar _detailScroll = null!;
    private Label _lblSessionHead = null!;
    private TextBox _txtSessionInput = null!;
    private ScrollMask _sessionInputMask = null!;
    private LineScrollBar _sessionInputScroll = null!;
    private Button _btnSessionSend = null!;
    private ContextMenuStrip _menuSessions = null!;
    private Control _sessionsPage = null!;
    private Control[] _pages = Array.Empty<Control>();
    private bool _suppressSessionNav;
    private Button _btnNewChat = null!;
    private Button _btnOpenSettings = null!;
    private Button _btnOpenPs = null!;
    private Button _btnOpenDetail = null!;
    private readonly List<SessionTreeNode?> _sessionMap = new();
    // 与 _sessionMap 一一对应：非 null 表示该行是工作目录分组标题。
    private readonly List<string?> _sessionGroups = new();
    private readonly Font _sessionTitleFont = new("Microsoft YaHei UI", 9.5F);
    private readonly Font _sessionMetaFont = new("Microsoft YaHei UI", 8.25F);
    private readonly Font _sessionGroupFont = new("Microsoft YaHei UI", 8.75F, FontStyle.Bold);
    private TextBox _txtInput = null!;
    private ScrollMask _inputMask = null!;
    private LineScrollBar _inputScroll = null!;
    // 队列预览：折叠在输入框上方，点击 Header 切换展开/折叠。
    private Panel _queuePreviewPanel = null!;
    private Label _queuePreviewHeader = null!;
    private FlowLayoutPanel _queuePreviewList = null!;
    private bool _queuePreviewExpanded;
    private TextBox _txtPsCode = null!;
    private TextBox _txtPsOut = null!;
    private ScrollMask _psCodeMask = null!;
    private ScrollMask _psOutMask = null!;
    private LineScrollBar _psCodeScroll = null!;
    private LineScrollBar _psOutScroll = null!;
    private RichTextBox _txtReply = null!;
    private ScrollMask _replyMask = null!;
    private LineScrollBar _replyScroll = null!;
    private string _lastReplyRaw = "";
    private ListBox _lstRuns = null!;
    private NumberField _numDelayMin = null!;
    private NumberField _numDelayMax = null!;
    private bool _savingDelay;
    private LineSlider _trkOpacity = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _counterLabel = null!;
    private SplitContainer _split = null!;
    private SplitContainer _psSplit = null!;
    private SplitContainer _psRight = null!;
    private FadeOverlay _fadeOverlay = null!;
    private int _fadeStep;
    private bool _syncingDeepThink;
    private volatile bool _safeStopRequested;

    public MainForm()
    {
        _ps = new PowerShellRunner(_tempDir);
        _ps.OutputLine += line => ToolOutput(line);
        Application.AddMessageFilter(this);
        BuildUi();
        ApplyPalette();
        LoadConfigIntoUi();
        // 先落一条运行态：进程被强杀时，下次启动才能发现上一轮未正常结束。
        try { _runtime.Save(_runtimeState); } catch { }

        Load += MainForm_Load;
        Shown += (_, _) => ApplyInitialSplitLayout();
        FormClosing += MainForm_FormClosing;
        FormClosed += (_, _) => Cleanup();
        _watchdog.Tick += Watchdog_Tick;
        _scrollTimer.Tick += (_, _) =>
        {
            _replyScroll.RefreshScroll();
            _inputScroll.RefreshScroll();
            _psCodeScroll.RefreshScroll();
            _psOutScroll.RefreshScroll();
            _detailScroll.RefreshScroll();
            _sessionInputScroll.RefreshScroll();
            _detailPromptScroll.RefreshScroll();
            // 只读输出区即使获得焦点也保持无光标，避免点击后出现闪烁竖线。
            CaretHider.HideCaret(_txtReply.Handle);
            CaretHider.HideCaret(_txtPsCode.Handle);
            CaretHider.HideCaret(_txtPsOut.Handle);
            CaretHider.HideCaret(_sessionDetail.Handle);
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>让鼠标滚轮作用于光标下的文本框，而不是只作用于当前获得焦点的控件。</summary>
    public bool PreFilterMessage(ref Message m)
    {
        const int WmMouseWheel = 0x020A;
        if (m.Msg != WmMouseWheel) return false;

        var point = new NativePoint
        {
            X = (short)((long)m.LParam & 0xFFFF),
            Y = (short)(((long)m.LParam >> 16) & 0xFFFF)
        };
        var control = FromHandle(WindowFromPoint(point));
        var target = FindScrollTarget(control);

        if (target is LineScrollBar bar)
        {
            bar.HandleWheel(m.WParam, m.LParam);
            return true;
        }

        if (target is TextBoxBase box)
        {
            LineScrollBar.ScrollByWheel(box.Handle, m.WParam);
            return true;
        }

        return false;
    }

    /// <summary>找到鼠标所在区域的文本框，找不到时沿父级向上复用区域内的文本框。</summary>
    private Control? FindScrollTarget(Control? control)
    {
        while (control != null && control != this)
        {
            if (control is LineScrollBar || control is TextBoxBase) return control;
            if (control is ScrollMask mask) return mask.Target;

            foreach (Control child in control.Controls)
            {
                if (child is TextBoxBase box) return box;
            }

            control = control.Parent;
        }

        return null;
    }

    public string TempDir => _tempDir;
    public string WorkDir => _cfg.WorkDir;
    public bool IsAutoExecute => _chkAuto.Checked;
    public CancellationToken Token => _runCts?.Token ?? CancellationToken.None;

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("DeepseekDesktopAgent.icon.ico");
            if (stream != null) return new Icon(stream, SystemInformation.IconSize);
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        return null;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        ApplyInitialSplitLayout();

        _watchdog.Start();
        _scrollTimer.Start();

        // ①-I：先检查上次是否异常结束，再做联网更新检查，避免弹窗互相打断。
        try { ShowRecoveryIfNeeded(); } catch { }

        try
        {
            var latest = await UpdateChecker.FetchLatestVersionAsync();
            if (!IsDisposed && !string.IsNullOrWhiteSpace(latest) && latest != UpdateChecker.CurrentVersion)
            {
                var result = MessageBox.Show(this,
                    $"发现新版本：{latest}\r\n当前版本：{UpdateChecker.CurrentVersion}\r\n\r\n是否打开发布页？",
                    "Deepseek-Desktop-Agent 更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) UpdateChecker.OpenReleasesPage();
            }
        }
        catch { }
    }

    private static Control? FindFocused(Control root)
    {
        if (root.Focused) return root;
        foreach (Control child in root.Controls)
        {
            var found = FindFocused(child);
            if (found != null) return found;
        }
        return null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var modifiers = keyData & Keys.Modifiers;
        var key = keyData & Keys.KeyCode;
        var focused = FindFocused(this);

        if (modifiers == Keys.Control)
        {
            switch (key)
            {
                case Keys.A:
                    if (focused is TextBoxBase tbA) { tbA.SelectAll(); return true; }
                    break;
                case Keys.C:
                    if (focused is TextBoxBase tbC) { tbC.Copy(); return true; }
                    break;
                case Keys.X:
                    if (focused is TextBoxBase tbX) { tbX.Cut(); return true; }
                    break;
                case Keys.V:
                    if (focused is TextBoxBase tbV)
                    {
                        // 输入框里 Ctrl+V：剪贴板是图片时按附件处理，不往文本里塞字符。
                        if (tbV == _txtInput && TryPasteImage()) return true;
                        tbV.Paste();
                        return true;
                    }
                    break;
                case Keys.Z:
                    if (focused is TextBoxBase tbZ) { tbZ.Undo(); return true; }
                    break;
                case Keys.Y:
                    if (focused is RichTextBox rtbY) { rtbY.Redo(); return true; }
                    break;
                case Keys.Enter:
                    if (_chkEnterSend.Checked)
                    {
                        _ = SendUserAsync();
                        return true;
                    }
                    break;
            }
        }

        if (key == Keys.Escape && modifiers == Keys.Shift && _busy)
        {
            // ①-IV-A 安全停止：不打断正在执行的工具，只在当前工具完成后结束本轮。
            _safeStopRequested = true;
            SetStatus("已请求安全停止：当前工具执行完后结束本轮");
            return true;
        }

        if (key == Keys.Escape && modifiers == Keys.None && _busy)
        {
            StopCurrent();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void SetStatus(string text)
    {
        AppLog.Write(text);
        RunOnUi(() =>
        {
            _lblStatus.Text = text;
            _statusLabel.Text = text;
            if (_thinking.IsThinking) _thinking.SetStage(text);
        });
    }

    private void UpdateCounters()
    {
        RunOnUi(() => _counterLabel.Text = $"会话 {_userMessageCount} | 工具 {_toolCount}");
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
            return;
        }
        action();
    }

    private static Task InvokeOnUi(Control control, Action action)
    {
        if (!control.InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            control.BeginInvoke(new Action(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            }));
        }
        catch (Exception ex) { tcs.SetException(ex); }
        return tcs.Task;
    }

    private void ShowError(string title, Exception ex)
    {
        SetStatus(title + ": " + ex.Message);
        if (!IsDisposed)
        {
            RunOnUi(() => MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error));
        }
    }

    /// <summary>①-I：启动时检查上次是否异常结束，并按 ①-II 给出影响说明。</summary>
    private void ShowRecoveryIfNeeded()
    {
        IReadOnlyList<UncleanShutdownInfo> items;
        try { items = _runtime.ScanUnclean(); } catch { return; }
        if (items.Count == 0) return;

        var newest = items[0];
        var state = newest.State;
        if (state == null) return;

        var detail = RuntimeStateStore.BuildMessage(state);
        if (state.SessionId is { } sessionId) detail += "\n会话编号：" + sessionId;
        detail += "\n\n继续：把未执行的排队消息放回输入框，并沿用上次会话链接"
                + "\n只看记录：保留记录，不做任何恢复"
                + "\n新建会话：清除这条残留记录";

        var result = MessageBox.Show(this, detail, "上次运行未正常结束",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            if (!string.IsNullOrWhiteSpace(_cfg.LastSessionUrl)) _lastSessionUrl = _cfg.LastSessionUrl;
            if (state.PendingPrompts.Count > 0) _txtInput.Text = string.Join("\n", state.PendingPrompts);
            _runtime.Remove(state.Pid);
            SetStatus("已按上次异常记录准备恢复");
        }
        else if (result == DialogResult.Cancel)
        {
            _runtime.Remove(state.Pid);
            SetStatus("已清除上次异常记录");
        }
        else
        {
            SetStatus("已保留上次异常记录，可在诊断中查看");
        }

        // 更早的残留只清理，不再逐个弹窗。
        foreach (var item in items.Skip(1))
            if (item.State != null) _runtime.Remove(item.State.Pid);
    }

    /// <summary>正常退出：标记后删除本进程记录，保留 last.json 供下次对照。</summary>
    private void MarkCleanShutdown()
    {
        _runtimeState.CleanShutdown = true;
        _runtimeState.Phase = AgentPhase.Idle;
        _runtimeState.PendingPrompts = new List<string>();
        _runtime.Save(_runtimeState);
        _runtime.Remove(Environment.ProcessId);
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        if (_shuttingDown) return;
        _shuttingDown = true;
        _watchdog.Stop();
        _guard.Enabled = false;
        try { SaveDelay(); } catch { }
        try { MarkCleanShutdown(); } catch { }

        try { _runCts?.Cancel(); } catch { }
        _ps.KillCurrent();

        try
        {
            if (_cdp is { IsConnected: true })
            {
                var url = await ReadPersistedSessionUrlAsync();
                if (url.Length > 0)
                {
                    _lastSessionUrl = url;
                    SaveLastSession();
                }
            }
        }
        catch { }

        try
        {
            if (_browser != null)
                await _browser.CloseAsync();
        }
        catch { }

        try
        {
            if (_cdp != null) await _cdp.DisposeAsync();
        }
        catch { }

        Cleanup();
        _allowClose = true;
        BeginInvoke(new Action(Close));
    }

    private void Cleanup()
    {
        try { Application.RemoveMessageFilter(this); } catch { }
        try { _watchdog.Stop(); _watchdog.Dispose(); } catch { }
        try { _fadeTimer.Stop(); _fadeTimer.Dispose(); } catch { }
        try { _scrollTimer.Stop(); _scrollTimer.Dispose(); } catch { }
        try { _guard.Dispose(); } catch { }
        try { _ps.Dispose(); } catch { }
        try { _browser?.Dispose(); } catch { }
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

}