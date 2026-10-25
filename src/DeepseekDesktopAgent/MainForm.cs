using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed class MainForm : Form, IToolHost, IMessageFilter
{
    private AppConfig _cfg = ConfigService.Load();
    private readonly string _tempDir = AppPaths.CreateSessionTempDir();
    private readonly PowerShellRunner _ps;
    private readonly KeyboardCloseGuard _guard = new();
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 700 };
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
    private Label _lblBrowserHint = null!;
    private Label _lblOpacity = null!;
    private Label _lblChatState = null!;
    private Label _lblReplyEmpty = null!;
    private Label _lblInputPlaceholder = null!;
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
    private Button _btnDock2 = null!;
    private Button _btnSwitch = null!;
    private Button _btnClearRuns = null!;
    private Button _btnOpenTemp = null!;
    private RadioButton _rbNew = null!;
    private RadioButton _rbOld = null!;
    private CheckBox _chkNight = null!;
    private CheckBox _chkAuto = null!;
    private CheckBox _chkEnterSend = null!;
    private TextBox _txtInput = null!;
    private LineScrollBar _inputScroll = null!;
    private TextBox _txtPsCode = null!;
    private TextBox _txtPsOut = null!;
    private LineScrollBar _psCodeScroll = null!;
    private LineScrollBar _psOutScroll = null!;
    private RichTextBox _txtReply = null!;
    private LineScrollBar _replyScroll = null!;
    private string _lastReplyRaw = "";
    private ListBox _lstRuns = null!;
    private SegmentedTabs _tabs = null!;
    private BufferedPanel _browserHost = null!;
    private NumericUpDown _numDelayMin = null!;
    private NumericUpDown _numDelayMax = null!;
    private LineSlider _trkOpacity = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _counterLabel = null!;
    private SplitContainer _split = null!;
    private SplitContainer _psSplit = null!;
    private SplitContainer _psRight = null!;
    private FadeOverlay _fadeOverlay = null!;
    private int _fadeStep;

    public MainForm()
    {
        _ps = new PowerShellRunner(_tempDir);
        _ps.OutputLine += line => ToolOutput(line);
        Application.AddMessageFilter(this);
        BuildUi();
        ApplyPalette();
        LoadConfigIntoUi();

        Load += MainForm_Load;
        Shown += (_, _) => ApplyInitialSplitLayout();
        FormClosing += MainForm_FormClosing;
        FormClosed += (_, _) => Cleanup();
        _watchdog.Tick += Watchdog_Tick;
        _layoutTimer.Tick += (_, _) => _browser?.UpdateLayout();
        _scrollTimer.Tick += (_, _) =>
        {
            _replyScroll.RefreshScroll();
            _inputScroll.RefreshScroll();
            _psCodeScroll.RefreshScroll();
            _psOutScroll.RefreshScroll();
            // 只读输出区即使获得焦点也保持无光标，避免点击后出现闪烁竖线。
            CaretHider.HideCaret(_txtReply.Handle);
            CaretHider.HideCaret(_txtPsCode.Handle);
            CaretHider.HideCaret(_txtPsOut.Handle);
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

    private void BuildUi()
    {
        var appIcon = LoadAppIcon();
        if (appIcon != null) Icon = appIcon;
        Text = "Deepseek-Desktop-Agent";
        var __wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        ClientSize = new Size(__wa.Width, __wa.Height);
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(__wa.Left, __wa.Top);
        WindowState = FormWindowState.Maximized;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var topBar = new BufferedPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(10, 7, 10, 5),
            Tag = "card"
        };
        var topFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        _btnChooseDir = MakeButton("选择工作目录", 112, "accent");
        _lblWorkDir = new Label
        {
            Text = "未选择工作目录",
            AutoSize = false,
            Width = 360,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _rbNew = new RadioButton { Text = "新对话", Checked = true, AutoSize = true, Height = 32, Margin = new Padding(10, 6, 2, 0) };
        _rbOld = new RadioButton { Text = "旧对话", AutoSize = true, Height = 32, Margin = new Padding(2, 6, 8, 0) };
        _btnStart = MakeButton("开始 / 继续", 106, "accent");
        _btnStop = MakeButton("急停", 74);
        ((AccentButton)_btnStop).Danger = true;
        _btnStop.Enabled = false;
        _chkNight = new CheckBox { Text = "夜间模式", AutoSize = true, Height = 32, Margin = new Padding(8, 6, 4, 0) };
        _chkAuto = new CheckBox { Text = "自动执行", AutoSize = true, Height = 32, Margin = new Padding(4, 6, 8, 0) };
        _lblStatus = new Label
        {
            Text = "请选择工作目录后开始",
            AutoSize = false,
            Width = 380,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(8, 4, 0, 0)
        };

        topFlow.Controls.AddRange(new Control[]
        {
            _btnChooseDir, _lblWorkDir, _rbNew, _rbOld, _btnStart, _btnStop, _chkNight, _chkAuto, _lblStatus
        });
        topBar.Controls.Add(topFlow);

        // 自绘标签栏 + 内容面板：原生 TabControl 在暗色主题下会露出浅色标签带。
        var tabRoot = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        tabRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        tabRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabs = new SegmentedTabs { Dock = DockStyle.Fill };
        _tabs.AddTab("对话");
        _tabs.AddTab("PowerShell");
        _tabs.AddTab("设置");

        var tabHost = new BufferedPanel { Dock = DockStyle.Fill };
        var convoPage = new Panel { Dock = DockStyle.Fill, Visible = true };
        var psPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var settingsPage = new Panel { Dock = DockStyle.Fill, Visible = false };

        BuildConversationTab(convoPage);
        BuildPowerShellTab(psPage);
        BuildSettingsTab(settingsPage);
        tabHost.Controls.Add(convoPage);
        tabHost.Controls.Add(psPage);
        tabHost.Controls.Add(settingsPage);

        _fadeOverlay = new FadeOverlay();
        tabHost.Controls.Add(_fadeOverlay);
        _fadeTimer.Tick += (_, _) => AdvanceTabFade();

        var pages = new Control[] { convoPage, psPage, settingsPage };
        _tabs.SelectedIndexChanged += (_, _) => SwitchTabPage(pages, _tabs.SelectedIndex);

        tabRoot.Controls.Add(_tabs, 0, 0);
        tabRoot.Controls.Add(tabHost, 0, 1);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6
        };
        _split.Panel1.Controls.Add(tabRoot);

        var browserPanel = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Tag = "card"
        };
        browserPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        browserPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var browserBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(6, 3, 6, 0)
        };
        var browserTitle = new Label
        {
            Text = "DeepSeek 页面",
            AutoSize = false,
            Width = 120,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _btnDock2 = MakeButton("还原窗口", 96);
        _btnSwitch = MakeButton("切换会话", 96);
        browserBar.Controls.AddRange(new Control[] { browserTitle, _btnDock2, _btnSwitch });

        _browserHost = new BufferedPanel { Dock = DockStyle.Fill };
        _lblBrowserHint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "尚未启动 DeepSeek 浏览器\r\n选择工作目录后会自动启动并内嵌到此处",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ThemeManager.Current.SubtleText
        };
        _browserHost.Controls.Add(_lblBrowserHint);

        browserPanel.Controls.Add(browserBar, 0, 0);
        browserPanel.Controls.Add(_browserHost, 0, 1);
        _split.Panel2.Controls.Add(browserPanel);

        _statusStrip = new StatusStrip { SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("就绪");
        _counterLabel = new ToolStripStatusLabel("会话 0 | 工具 0") { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_counterLabel);

        Controls.Add(_split);
        Controls.Add(topBar);
        Controls.Add(_statusStrip);

        _btnChooseDir.Click += async (_, _) => await ChooseDirClickedAsync();
        _btnStart.Click += async (_, _) => await StartFlowAsync();
        _btnStop.Click += (_, _) => StopCurrent();
        _rbOld.CheckedChanged += (_, _) =>
        {
            if (!_rbOld.Checked) return;
            SetStatus(IsSessionUrl(_lastSessionUrl)
                ? "旧对话：开始时会自动续接上次会话"
                : "旧对话：开始时会弹出会话选择");
        };
        _chkNight.CheckedChanged += (_, _) => { _cfg.NightMode = _chkNight.Checked; ConfigService.Save(_cfg); ApplyPalette(); };
        _chkAuto.CheckedChanged += (_, _) => { _cfg.AutoExecute = _chkAuto.Checked; ConfigService.Save(_cfg); };
        _chkEnterSend.CheckedChanged += (_, _) => { _cfg.EnterToSend = _chkEnterSend.Checked; ConfigService.Save(_cfg); };
        _btnDock2.Click += (_, _) => ToggleDock();
        _btnSwitch.Click += async (_, _) => await SwitchSessionAsync();
        _browserHost.Resize += (_, _) => _browser?.UpdateLayout();
        _browserHost.MouseDown += (_, _) => _browser?.Focus();
    }

    private void BuildConversationTab(Panel page)
    {
        page.Padding = new Padding(8, 6, 8, 6);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _convoLayout = layout;

        var header = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(2, 0, 2, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        var title = new Label
        {
            Text = "会话",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "回复以 Markdown 渲染 · 工具在本机执行",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            AutoEllipsis = true
        };
        _lblChatState = new Label
        {
            Text = "● 空闲",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 8.25F)
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 1, 0);
        header.Controls.Add(_lblChatState, 2, 0);

        _cardReply = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 12, 10),
            Margin = new Padding(2, 0, 2, 6),
            Radius = 14,
            FillRole = PanelFill.Input
        };
        _txtReply = new NoCaretRichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        _lblReplyEmpty = new Label
        {
            Dock = DockStyle.Fill,
            Text = "会话回复会显示在这里\r\n输入任务后按 Ctrl+Enter 发送",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        _replyScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _cardReply.Controls.Add(_txtReply);
        _cardReply.Controls.Add(_lblReplyEmpty);
        _cardReply.Controls.Add(_replyScroll);
        _txtReply.HandleCreated += (_, _) =>
        {
            LineScrollBar.HideNativeBar(_txtReply.Handle, true);
            _replyScroll.Attach(_txtReply.Handle);
        };
        _txtReply.TextChanged += (_, _) => _replyScroll.RefreshScroll();

        _thinking = new ThinkingIndicator { Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 6) };

        _lblAttach = new Label
        {
            Text = "未选择附件",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(4, 0, 0, 0)
        };

        _cardInput = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 10, 6),
            Margin = new Padding(2, 0, 2, 0),
            Radius = 12,
            FillRole = PanelFill.Input
        };
        _txtInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        _lblInputPlaceholder = new Label
        {
            Text = "在此输入任务，Ctrl+Enter 发送…",
            AutoSize = false,
            Width = 480,
            Height = 22,
            Left = 16,
            Top = 10,
            Visible = false,
            Font = new Font("Microsoft YaHei UI", 8.25F)
        };
        _inputScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _cardInput.Controls.Add(_txtInput);
        _cardInput.Controls.Add(_lblInputPlaceholder);
        _cardInput.Controls.Add(_inputScroll);
        _txtInput.HandleCreated += (_, _) =>
        {
            LineScrollBar.HideNativeBar(_txtInput.Handle, true);
            _inputScroll.Attach(_txtInput.Handle);
        };
        _txtInput.TextChanged += (_, _) =>
        {
            UpdateInputPlaceholder();
            _inputScroll.RefreshScroll();
        };
        _txtInput.EnabledChanged += (_, _) => UpdateInputPlaceholder();
        _txtInput.Enter += (_, _) => { _cardInput.Highlight = true; _cardInput.Invalidate(); };
        _txtInput.Leave += (_, _) => { _cardInput.Highlight = false; _cardInput.Invalidate(); };

        _lblInputHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Visible = false,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Text = "",
            Margin = new Padding(6, 0, 0, 0)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(2, 4, 0, 0)
        };
        _btnSend = MakeButton("发送 (Ctrl+Enter)", 132, "accent");
        _btnAttach = MakeButton("添加附件", 92);
        _btnClearAttach = MakeButton("清除附件", 92);
        _chkEnterSend = new CheckBox { Text = "启用 Ctrl+Enter", Checked = true, AutoSize = true, Height = 32, Margin = new Padding(8, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _btnSend, _btnAttach, _btnClearAttach, _chkEnterSend });

        void SendClicked(object? s, EventArgs e) => _ = SendUserAsync();
        _btnSend.Click += SendClicked;
        _btnAttach.Click += (_, _) => AttachFiles();
        _btnClearAttach.Click += (_, _) => ClearAttachments();

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_cardReply, 0, 1);
        layout.Controls.Add(_thinking, 0, 2);
        layout.Controls.Add(_lblAttach, 0, 3);
        layout.Controls.Add(_cardInput, 0, 4);
        layout.Controls.Add(_lblInputHint, 0, 5);
        layout.Controls.Add(buttons, 0, 6);
        page.Controls.Add(layout);
    }

    private void UpdateInputPlaceholder()
    {
        var show = _txtInput.TextLength == 0;
        _lblInputPlaceholder.Text = _busy
            ? "Agent 运行中：此处输入会排队，本轮全部结束后自动发送"
            : "在此输入任务，Ctrl+Enter 发送…";
        _lblInputPlaceholder.Visible = show;
        if (show) _lblInputPlaceholder.BringToFront();
    }

    /// <summary>思考提示条显示时占用一行，隐藏时行高归零。</summary>
    private void ShowThinking(bool on)
    {
        if (_convoLayout.RowStyles.Count > 2)
            _convoLayout.RowStyles[2].Height = on ? 36 : 0;
        if (on)
        {
            _thinking.StartThinking(_lblStatus.Text);
            _lblChatState.Text = "● 正在思考";
            _lblChatState.ForeColor = ThemeManager.Current.Accent;
        }
        else
        {
            _thinking.StopThinking();
            _lblChatState.Text = "● 空闲";
            _lblChatState.ForeColor = ThemeManager.Current.SubtleText;
        }
    }

    private void BuildPowerShellTab(Panel page)
    {
        page.Padding = new Padding(8, 6, 8, 6);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        // 页头：说明这一页在看什么，避免只剩几块空白的文本区。
        var header = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(2, 0, 2, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.Controls.Add(new Label
        {
            Text = "PowerShell",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = "每次工具调用都会在这里留下运行记录、实际命令与输出",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            AutoEllipsis = true
        }, 1, 0);
        _lblPsState = new Label
        {
            Text = "状态：空闲",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Tag = "subtle"
        };
        header.Controls.Add(_lblPsState, 2, 0);

        _psSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 8
        };
        _lstRuns = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _psSplit.Panel1.Padding = new Padding(0, 0, 6, 0);
        _psSplit.Panel1.Controls.Add(FieldCard("运行记录", _lstRuns));

        _psRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8
        };
        _txtPsCode = new NoCaretTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F)
        };
        _txtPsOut = new NoCaretTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F)
        };
        _psCodeScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _psOutScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _txtPsCode.HandleCreated += (_, _) =>
        {
            LineScrollBar.HideNativeBar(_txtPsCode.Handle, true);
            _psCodeScroll.Attach(_txtPsCode.Handle);
        };
        _txtPsOut.HandleCreated += (_, _) =>
        {
            LineScrollBar.HideNativeBar(_txtPsOut.Handle, true);
            _psOutScroll.Attach(_txtPsOut.Handle);
        };
        _txtPsOut.TextChanged += (_, _) => _psOutScroll.RefreshScroll();
        _psRight.Panel1.Padding = new Padding(0, 0, 0, 4);
        _psRight.Panel2.Padding = new Padding(0, 4, 0, 0);
        _psRight.Panel1.Controls.Add(FieldCard("命令 · 工具实际执行的脚本", WrapWithScroll(_txtPsCode, _psCodeScroll)));
        _psRight.Panel2.Controls.Add(FieldCard("输出 · stdout / stderr 回传", WrapWithScroll(_txtPsOut, _psOutScroll)));
        _psSplit.Panel2.Controls.Add(_psRight);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _btnClearRuns = MakeButton("清空记录", 92);
        _btnOpenTemp = MakeButton("打开临时目录", 112);
        var openConfig = MakeButton("打开配置目录", 112);
        _btnClearRuns.Click += (_, _) => ClearRunLog();
        _btnOpenTemp.Click += (_, _) => OpenFolder(_tempDir);
        openConfig.Click += (_, _) => OpenFolder(AppPaths.AppDataDir);
        buttons.Controls.AddRange(new Control[] { _btnClearRuns, _btnOpenTemp, openConfig });

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_psSplit, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        page.Controls.Add(layout);
    }

    /// <summary>带小标题的圆角字段卡片，让每块区域一眼看懂用途。</summary>
    private static Control FieldCard(string caption, Control input)
    {
        var card = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(6, 0, 0, 2),
            Tag = "subtle"
        }, 0, 0);
        card.Controls.Add(UiPaint.WrapField(input, 8, 6, 10), 0, 1);
        return card;
    }

    /// <summary>把文本框和自绘滚动条放进同一个容器，供字段卡片统一包裹。</summary>
    private static Panel WrapWithScroll(Control input, LineScrollBar bar)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = ThemeManager.Current.Input
        };
        input.Dock = DockStyle.Fill;
        host.Controls.Add(input);
        host.Controls.Add(bar);
        return host;
    }

    private void BuildSettingsTab(Panel page)
    {
        page.Padding = new Padding(10);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        _settingsFlow = flow;

        flow.Controls.Add(SectionLabel("防封控延时（自动执行开启时生效）"));
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.Add(new Label { Text = "最小秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
        _numDelayMin = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 80, Height = 28 };
        var minField = UiPaint.WrapField(_numDelayMin, 8, 3, 8);
        minField.Margin = new Padding(0, 6, 6, 0);
        delayRow.Controls.Add(minField);
        delayRow.Controls.Add(new Label { Text = "最大秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(18, 8, 6, 0) });
        _numDelayMax = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 80, Height = 28 };
        var maxField = UiPaint.WrapField(_numDelayMax, 8, 3, 8);
        maxField.Margin = new Padding(0, 6, 0, 0);
        delayRow.Controls.Add(maxField);
        flow.Controls.Add(delayRow);

        flow.Controls.Add(SectionLabel("窗口透明度"));
        var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _trkOpacity = new LineSlider { Minimum = 30, Maximum = 100, Width = 300, Height = 28, Margin = new Padding(0, 2, 0, 0) };
        _lblOpacity = new Label { Text = "100%", AutoSize = false, Width = 60, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 2, 0, 0) };
        opacityRow.Controls.Add(_trkOpacity);
        opacityRow.Controls.Add(_lblOpacity);
        flow.Controls.Add(opacityRow);

        flow.Controls.Add(SectionLabel("执行方式"));
        _chkAuto.Text = "自动执行 PowerShell（关闭时每次弹窗确认）";
        _chkAuto.AutoSize = true;
        flow.Controls.Add(_chkAuto);
        flow.Controls.Add(new Label
        {
            Text = "PowerShell 始终在后台执行，不显示命令窗口；执行指令与实时输出显示在 PowerShell 选项卡。",
            AutoSize = false,
            Width = 720,
            Height = 44,
            ForeColor = ThemeManager.Current.SubtleText
        });

        flow.Controls.Add(SectionLabel("快捷操作"));
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var openReleases = MakeButton("打开发布页", 100);
        var reset = MakeButton("重置配置", 92);
        openReleases.Click += (_, _) => UpdateChecker.OpenReleasesPage();
        reset.Click += (_, _) => ResetConfig();
        links.Controls.Add(openReleases);
        links.Controls.Add(reset);
        flow.Controls.Add(links);

        flow.Controls.Add(SectionLabel("关于"));
        flow.Controls.Add(new Label
        {
            Text = "Deepseek-Desktop-Agent · C# 重构版\n" +
                   "DeepSeek 网页版 + CDP 自动化。工具协议与原版保持一致。\n" +
                   "配置目录：" + AppPaths.AppDataDir + "\n" +
                   "临时目录（每次启动清空）：" + _tempDir,
            AutoSize = false,
            Width = 760,
            Height = 96
        });

        page.Controls.Add(flow);

        _numDelayMin.ValueChanged += (_, _) => SaveDelay();
        _numDelayMax.ValueChanged += (_, _) => SaveDelay();
        _trkOpacity.ValueChanged += (_, _) =>
        {
            _cfg.OpacityPercent = _trkOpacity.Value;
            Opacity = _trkOpacity.Value / 100.0;
            _lblOpacity.Text = _trkOpacity.Value + "%";
            ConfigService.Save(_cfg);
        };
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 720,
        Height = 30,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft,
        Margin = new Padding(0, 12, 0, 4)
    };

    private static Button MakeButton(string text, int width, string? tag = null) => new AccentButton
    {
        Text = text,
        Width = width,
        Height = 32,
        Tag = tag,
        Accent = tag == "accent",
        Margin = new Padding(0, 2, 6, 0)
    };

    private void LoadConfigIntoUi()
    {
        _chkNight.Checked = _cfg.NightMode;
        _chkAuto.Checked = _cfg.AutoExecute;
        _chkEnterSend.Checked = _cfg.EnterToSend;
        _numDelayMin.Value = Math.Clamp(_cfg.DelayMinSeconds, 0, 600);
        _numDelayMax.Value = Math.Clamp(_cfg.DelayMaxSeconds, 0, 600);
        _trkOpacity.Value = Math.Clamp(_cfg.OpacityPercent, 30, 100);
        _lblOpacity.Text = _trkOpacity.Value + "%";
        Opacity = _trkOpacity.Value / 100.0;

        if (!string.IsNullOrWhiteSpace(_cfg.WorkDir))
        {
            _lblWorkDir.Text = _cfg.WorkDir;
            if (Directory.Exists(_cfg.WorkDir))
            {
                _txtInput.Text = Prompt.Build(_cfg.WorkDir, _tempDir);
                SetStatus("已载入上次的工作目录，点击开始即可连接 DeepSeek");
            }
            else
            {
                SetStatus("上次的工作目录不存在，请重新选择");
            }
        }

        _lastSessionUrl = _cfg.LastSessionUrl;
        if (IsSessionUrl(_lastSessionUrl))
        {
            _rbOld.Checked = true;
            var id = _lastSessionUrl[(_lastSessionUrl.IndexOf("/a/chat/s/", StringComparison.OrdinalIgnoreCase) + 10)..].Trim('/');
            if (id.Length > 8) id = id[..8];
            SetStatus("已记住上次会话 " + id + "，点击开始继续；要换会话请点“切换会话”");
        }
        UpdateCounters();
    }

    private void ApplyPalette()
    {
        ThemeManager.Apply(this, _chkNight.Checked ? ThemeManager.Dark : ThemeManager.Light);
        _lblBrowserHint.ForeColor = ThemeManager.Current.SubtleText;
        _lblInputHint.ForeColor = ThemeManager.Current.SubtleText;
        _statusLabel.ForeColor = ThemeManager.Current.SubtleText;
        _counterLabel.ForeColor = ThemeManager.Current.SubtleText;
        _lblWorkDir.ForeColor = ThemeManager.Current.Text;

        // 会话卡片背景与输入框同色，靠圆角与描边区分层次。
        _txtReply.BackColor = ThemeManager.Current.Input;
        _txtReply.ForeColor = ThemeManager.Current.Text;
        _lblReplyEmpty.BackColor = ThemeManager.Current.Input;
        _lblReplyEmpty.ForeColor = ThemeManager.Current.SubtleText;
        _lblInputPlaceholder.ForeColor = ThemeManager.Current.SubtleText;
        // 输入框在 Agent 运行期间始终可用，保持正常底色，避免看起来像被禁用。
        _txtInput.BackColor = ThemeManager.Current.Input;
        _lblInputPlaceholder.BackColor = _txtInput.BackColor;
        _lblChatState.ForeColor = _busy ? ThemeManager.Current.Accent : ThemeManager.Current.SubtleText;
        _cardReply.Invalidate();
        _cardInput.Invalidate();
        _thinking.Invalidate();
        _trkOpacity.Invalidate();
        _replyScroll.BackColor = ThemeManager.Current.Input;
        _replyScroll.Invalidate();
        _inputScroll.BackColor = ThemeManager.Current.Input;
        _inputScroll.Invalidate();
        _psCodeScroll.BackColor = ThemeManager.Current.Input;
        _psCodeScroll.Invalidate();
        _psOutScroll.BackColor = ThemeManager.Current.Input;
        _psOutScroll.Invalidate();
        UpdateAttachLabel();
        UpdateInputPlaceholder();
        RenderReply();
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        ApplyInitialSplitLayout();

        _watchdog.Start();
        _layoutTimer.Start();
        _scrollTimer.Start();

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

    /// <summary>
    /// 设置启动时的分栏比例。PowerShell 页初始隐藏，布局完成前设置其分栏距离会抛异常，
    /// 因此三处分栏分开处理，避免左侧主面板被跳过而停在默认的 50 像素宽。
    /// </summary>
    private void ApplyInitialSplitLayout()
    {
        try
        {
            if (_split.Width > 0)
            {
                // 左侧留给会话区，右侧浏览器至少保留 360 像素；左侧最小 560 像素。
                var target = Math.Clamp(_split.Width - 620, 560, Math.Max(560, _split.Width - 360));
                if (_split.SplitterDistance < target) _split.SplitterDistance = target;
            }
        }
        catch { }

        try { _psSplit.SplitterDistance = 210; } catch { }
        try { _psRight.SplitterDistance = 260; } catch { }
    }

    /// <summary>
    /// 切换标签页：先用不透明遮罩盖住内容区，页面切换与布局都在遮罩后完成，
    /// 再抓取新页面快照做淡入，避免切换瞬间的闪动和抽搐。
    /// </summary>
    private void SwitchTabPage(Control[] pages, int index)
    {
        _fadeTimer.Stop();
        _fadeOverlay.HideOverlay();
        _fadeOverlay.ShowFrame(null, 0f);

        for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
        // 切回设置页时复位滚动位置，避免上次聚焦导致的内容偏移。
        if (index == 2) _settingsFlow.AutoScrollPosition = new Point(0, 0);

        var page = pages[index];
        Bitmap? frame = null;
        try
        {
            page.Parent?.PerformLayout();
            page.PerformLayout();
            if (page.Width > 0 && page.Height > 0)
            {
                frame = new Bitmap(page.Width, page.Height);
                page.DrawToBitmap(frame, new Rectangle(0, 0, page.Width, page.Height));
            }
        }
        catch
        {
            frame?.Dispose();
            frame = null;
        }

        if (frame == null)
        {
            _fadeOverlay.HideOverlay();
            return;
        }

        _fadeOverlay.ShowFrame(frame, 0f);
        _fadeStep = 0;
        _fadeTimer.Start();
    }

    private void AdvanceTabFade()
    {
        const int frames = 12;
        _fadeStep++;
        var t = Math.Clamp(_fadeStep / (float)frames, 0f, 1f);
        _fadeOverlay.SetAlpha(t * (2 - t)); // 缓出曲线，收尾更柔和
        if (_fadeStep >= frames)
        {
            _fadeTimer.Stop();
            _fadeOverlay.HideOverlay();
        }
    }

    private async Task ChooseDirClickedAsync()
    {
        if (_busy) return;
        if (!await ChooseWorkDirAsync()) return;
        try
        {
            await EnsureBrowserAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(_txtInput.Text) || !_txtInput.Text.Contains("# System Prompt", StringComparison.Ordinal))
                _txtInput.Text = Prompt.Build(_cfg.WorkDir, _tempDir);
            _tabs.Select(0);
            SetStatus("工作目录已选择，点击“开始 / 继续”提交系统提示词");
        }
        catch (Exception ex)
        {
            ShowError("启动 DeepSeek 浏览器失败", ex);
        }
    }

    private async Task<bool> ChooseWorkDirAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.WorkDir) && Directory.Exists(_cfg.WorkDir))
        {
            var result = MessageBox.Show(this,
                "你想继续这个工作目录吗？\r\n" + _cfg.WorkDir,
                "继续工作", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes)
            {
                _lblWorkDir.Text = _cfg.WorkDir;
                return true;
            }
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Deepseek-Desktop-Agent 的工作目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_cfg.WorkDir) ? _cfg.WorkDir : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        _cfg.WorkDir = dialog.SelectedPath;
        ConfigService.Save(_cfg);
        _lblWorkDir.Text = _cfg.WorkDir;
        _txtInput.Text = Prompt.Build(_cfg.WorkDir, _tempDir);
        Prompt.EnsureSkills(_tempDir);
        SetStatus("工作目录：" + _cfg.WorkDir);
        return true;
    }

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

            if (!_browser.IsEmbedded)
            {
                var bounds = await _cdp.GetWindowBoundsAsync(ct);
                var adopted = bounds is { } b && _browser.TryAdoptByBounds(b.Left, b.Top, b.Width, b.Height);
                if (!adopted && _browser.Hwnd == IntPtr.Zero)
                    _browser.TryAdoptForegroundWindow();
            }

            if (!_browser.IsEmbedded)
            {
                var crop = await MeasureTopCropAsync(ct);
                _browser.Embed(_browserHost.Handle, crop);
                _lblBrowserHint.Visible = false;
            }

            _btnDock2.Text = _browser.IsEmbedded ? "还原窗口" : "内嵌窗口";
            _lblBrowserHint.Visible = !_browser.IsEmbedded;
        }
        finally
        {
            Interlocked.Exchange(ref _browserStarting, 0);
        }
    }

    private async Task<int> MeasureTopCropAsync(CancellationToken ct)
    {
        try
        {
            if (_cdp != null)
            {
                var raw = await _cdp.EvaluateAsync(
                    "JSON.stringify({o:window.outerHeight,i:window.innerHeight,d:window.devicePixelRatio})", 6000, ct);
                using var doc = JsonDocument.Parse(raw);
                var outer = doc.RootElement.GetProperty("o").GetDouble();
                var inner = doc.RootElement.GetProperty("i").GetDouble();
                var dpr = doc.RootElement.GetProperty("d").GetDouble();
                var crop = (outer - inner) * dpr;
                if (crop > 4 && crop < 140) return (int)Math.Round(crop);
            }
        }
        catch { }
        return (int)Math.Round(32 * Native.GetScaleForWindow(_browserHost.Handle));
    }

    private async Task StartFlowAsync()
    {
        if (_busy) return;

        if (string.IsNullOrWhiteSpace(_cfg.WorkDir) || !Directory.Exists(_cfg.WorkDir))
        {
            if (!await ChooseWorkDirAsync()) return;
        }

        try
        {
            SetStatus("正在准备会话...");
            await EnsureBrowserAsync(CancellationToken.None);
            if (_cdp == null) throw new InvalidOperationException("DeepSeek 调试连接未建立。");

            Prompt.EnsureSkills(_tempDir);
            if (string.IsNullOrWhiteSpace(_txtInput.Text) || !_txtInput.Text.Contains("# System Prompt", StringComparison.Ordinal))
                _txtInput.Text = Prompt.Build(_cfg.WorkDir, _tempDir);

            var systemFile = WriteSystemFile(_txtInput.Text);
            var files = new List<string> { systemFile };
            string message;

            if (_rbOld.Checked)
            {
                if (IsSessionUrl(_lastSessionUrl))
                {
                    SetStatus("正在打开上次会话...");
                    await _cdp.NavigateAsync(_lastSessionUrl);
                    var reopened = await _cdp.GetCurrentUrlAsync();
                    if (IsSessionUrl(reopened))
                    {
                        message = ContinuationMessage();
                    }
                    else
                    {
                        SetStatus("上次会话已失效，请重新选择");
                        using var picker = new SessionPickerForm(_cdp, reopened);
                        if (picker.ShowDialog(this) != DialogResult.OK) return;
                        if (picker.CreateNew)
                        {
                            await _cdp.NavigateAsync("https://chat.deepseek.com");
                            message = "system.txt";
                        }
                        else
                        {
                            await _cdp.NavigateAsync(picker.SelectedUrl);
                            message = ContinuationMessage();
                        }
                    }
                }
                else
                {
                    var current = await _cdp.GetCurrentUrlAsync();
                    using var picker = new SessionPickerForm(_cdp, current);
                    if (picker.ShowDialog(this) != DialogResult.OK) return;

                    if (picker.CreateNew)
                    {
                        await _cdp.NavigateAsync("https://chat.deepseek.com");
                        message = "system.txt";
                    }
                    else
                    {
                        await _cdp.NavigateAsync(picker.SelectedUrl);
                        message = ContinuationMessage();
                    }
                }
            }
            else
            {
                await _cdp.NavigateAsync("https://chat.deepseek.com");
                message = "system.txt";
            }

            var persistedUrl = await ReadPersistedSessionUrlAsync();
            if (persistedUrl.Length > 0)
            {
                _lastSessionUrl = persistedUrl;
                SaveLastSession();
            }
            _rbNew.Enabled = false;
            _rbOld.Enabled = false;
            _txtInput.Text = message;
            _txtInput.Clear();
            _userMessageCount++;
            UpdateCounters();
            await RunConversationAsync(message, files);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            ShowError("启动会话失败", ex);
        }
        finally
        {
            _rbNew.Enabled = true;
            _rbOld.Enabled = true;
        }
    }

    /// <summary>
    /// 只返回“已经真正落盘”的会话地址：URL 中的会话 id 必须已出现在 DeepSeek 侧边栏列表里。
    /// 新建对话时浏览器会短暂使用临时 id，直接保存会导致下次打开提示“该对话不存在”。
    /// </summary>
    private async Task<string> ReadPersistedSessionUrlAsync(CancellationToken ct = default)
    {
        if (_cdp == null) return "";
        try
        {
            var url = await _cdp.EvaluateAsync(
                "(() => { const h = location.href; const m = h.match(/\\/a\\/chat\\/s\\/([0-9a-fA-F-]{20,})/); " +
                "if (!m) return ''; const hit = Array.from(document.querySelectorAll('a[href*=\"/a/chat/s/\"]'))" +
                ".some(a => (a.getAttribute('href') || '').includes(m[1])); return hit ? h : ''; })()", 5000, ct);
            return IsSessionUrl(url) ? url.Trim() : "";
        }
        catch { return ""; }
    }

    private string ContinuationMessage()
    {
        return "[工具][继续对话] 1.Agent 炸了，现已重新连接，并重新提交系统提示词。2.原临时文件夹已被清空，现临时文件夹为" +
               _tempDir + "。3.请询问用户是否继续，你才能调用工具。";
    }

    private string WriteSystemFile(string promptText)
    {
        var path = Path.Combine(_tempDir, "system.txt");
        File.WriteAllText(path, promptText, new UTF8Encoding(true));
        return path;
    }

    private async Task SendUserAsync()
    {
        // Agent 运行期间输入框保持可用：消息只入队，等本轮全部工作结束后再发送。
        if (_busy)
        {
            var queuedText = _txtInput.Text;
            var queuedFiles = _attachedFiles.ToList();
            if (string.IsNullOrWhiteSpace(queuedText) && queuedFiles.Count == 0) return;

            lock (_queueLock) _queue.Enqueue(new QueuedMessage(queuedText, queuedFiles));
            _txtInput.Clear();
            ClearAttachments();
            _userMessageCount++;
            UpdateCounters();
            SetStatus($"已加入消息队列（共 {QueueCount} 条），本轮全部结束后自动发送");
            UpdateQueueHint();
            UpdateInputPlaceholder();
            return;
        }

        if (_browser == null || _cdp == null || !_browser.IsAlive)
        {
            MessageBox.Show(this, "请先选择工作目录并启动 DeepSeek 浏览器。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var text = _txtInput.Text;
        var files = _attachedFiles.ToList();
        if (string.IsNullOrWhiteSpace(text) && files.Count == 0) return;

        _txtInput.Clear();
        ClearAttachments();
        _userMessageCount++;
        UpdateCounters();
        await RunConversationAsync(text, files);
    }

    private async Task RunConversationAsync(string message, List<string> files)
    {
        if (_cdp == null)
        {
            ShowError("发送失败", new InvalidOperationException("DeepSeek 调试连接未建立。"));
            return;
        }

        using var cts = new CancellationTokenSource();
        _runCts = cts;
        SetBusy(true);
        try
        {
            // 刚急停过：等网页里那段回复真正生成结束再发消息，否则网页仍在生成会导致发送失败。
            if (_waitPageIdleAfterStop)
            {
                _waitPageIdleAfterStop = false;
                SetStatus("等待 DeepSeek 结束当前回复...");
                await _cdp.WaitForReplyIdleAsync(120000, cts.Token);
            }

            var reply = await SendAndReceiveAsync(message, files, cts.Token);
            ShowReply(reply);

            for (var hop = 0; hop < 60; hop++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var call = ToolProtocol.Parse(reply);
                if (call == null)
                {
                    if (ToolProtocol.HasMarkers(reply))
                    {
                        var hint = "!![工具][错误]!!\n" +
                                   "检测到关键词【_识别头_完全保留本行注释】，你可能想调用工具，但是工具无法识别代码。" +
                                   "如果确实要调用工具：请按格式重新输出一次；如果没有调用工具却收到此信息：请忽略。\n//[END]//";
                        reply = await SendAndReceiveAsync(hint, new List<string>(), cts.Token);
                        ShowReply(reply);
                        continue;
                    }
                    break;
                }

                _toolCount++;
                UpdateCounters();
                SetStatus("正在执行工具：" + call.Kind);

                ToolResult result;
                try
                {
                    result = await (_tools ?? throw new InvalidOperationException("工具执行器未初始化。")).ExecuteAsync(call);
                }
                catch (OperationCanceledException) { throw; }

                SetStatus("正在等待 DeepSeek 回复...");
                reply = await SendAndReceiveAsync(result.Message, result.Files, cts.Token);
                ShowReply(reply);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("已急停");
            ShowReply("[已急停] 当前任务被用户终止，队列中的消息将在停止完成后发送。");
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // CDP 调用在取消时抛出的是 CdpException，同样按急停处理。
            SetStatus("已急停");
            ShowReply("[已急停] 当前任务被用户终止，队列中的消息将在停止完成后发送。");
        }
        catch (Exception ex)
        {
            SetStatus("运行失败: " + ex.Message);
            ShowReply("!![运行错误]!!\n" + ex.Message);
            MessageBox.Show(this, ex.Message, "Deepseek-Desktop-Agent 运行错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (_runCts == cts) _runCts = null;
            SetBusy(false);
            // 只有整轮全部结束（DeepSeek 已回复且所有工具调用完成）后才发送队列里的下一条消息。
            StartNextQueuedMessage();
        }
    }

    private async Task<string> SendAndReceiveAsync(string text, IReadOnlyList<string> files, CancellationToken ct)
    {
        if (_cdp == null) throw new InvalidOperationException("CDP 未连接。");

        if (_chkAuto.Checked)
        {
            var min = (int)_numDelayMin.Value;
            var max = Math.Max(min, (int)_numDelayMax.Value);
            var delay = min == max ? min : Random.Shared.Next(min, max + 1);
            for (var i = delay; i > 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                SetStatus($"防封控延时 {i} 秒...");
                await Task.Delay(1000, ct);
            }
        }

        SetStatus("正在发送并等待 DeepSeek 回复...");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var reply = await _cdp.SendMessageAsync(text, files, 180000, ct);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                var persisted = await ReadPersistedSessionUrlAsync(ct);
                if (persisted.Length > 0)
                {
                    _lastSessionUrl = persisted;
                    SaveLastSession();
                }
                SetStatus("已收到回复");
                return reply;
            }
            SetStatus("未取回回复，正在重试...");
            await Task.Delay(1500, ct);
        }

        throw new CdpException("未取回 AI 回复。请检查 DeepSeek 页面是否正常、是否已登录。");
    }

    private void ShowReply(string reply)
    {
        _lastReplyRaw = reply;
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_last_reply.md"),
                reply, new UTF8Encoding(false));
        }
        catch { }
        RunOnUi(() =>
        {
            RenderReply();
            _txtReply.SelectionStart = _txtReply.TextLength;
            _txtReply.ScrollToCaret();
        });
    }

    private void RenderReply()
    {
        _lblReplyEmpty.Visible = string.IsNullOrEmpty(_lastReplyRaw);
        if (_lblReplyEmpty.Visible) _lblReplyEmpty.BringToFront();
        if (string.IsNullOrEmpty(_lastReplyRaw))
        {
            _txtReply.Clear();
            return;
        }

        try
        {
            _txtReply.Rtf = MarkdownRenderer.ToRtf(_lastReplyRaw, ThemeManager.Current);
        }
        catch (Exception ex)
        {
            AppLog.Write("Markdown 渲染失败: " + ex.Message);
            _txtReply.Text = _lastReplyRaw;
        }
    }

    private async Task SwitchSessionAsync()
    {
        if (_busy)
        {
            SetStatus("请先停止当前任务，再切换会话");
            return;
        }
        if (_cdp == null || _browser == null || !_browser.IsAlive)
        {
            MessageBox.Show(this, "请先启动 DeepSeek 浏览器。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var current = await _cdp.GetCurrentUrlAsync();
            using var picker = new SessionPickerForm(_cdp, current);
            if (picker.ShowDialog(this) != DialogResult.OK) return;

            if (picker.CreateNew)
            {
                await _cdp.NavigateAsync("https://chat.deepseek.com");
                _rbNew.Checked = true;
                var systemFile = WriteSystemFile(Prompt.Build(_cfg.WorkDir, _tempDir));
                _userMessageCount++;
                UpdateCounters();
                await RunConversationAsync("system.txt", new List<string> { systemFile });
            }
            else
            {
                await _cdp.NavigateAsync(picker.SelectedUrl);
                _rbOld.Checked = true;
                _lastSessionUrl = picker.SelectedUrl;
                SaveLastSession();
                var systemFile = WriteSystemFile(Prompt.Build(_cfg.WorkDir, _tempDir));
                _userMessageCount++;
                UpdateCounters();
                await RunConversationAsync(ContinuationMessage(), new List<string> { systemFile });
            }
        }
        catch (Exception ex)
        {
            ShowError("切换会话失败", ex);
        }
    }

    private async void ToggleDock()
    {
        if (_browser == null || !_browser.IsAlive)
        {
            MessageBox.Show(this, "DeepSeek 浏览器尚未启动。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (_browser.IsEmbedded)
            {
                _browser.Undock();
                _lblBrowserHint.Visible = true;
                _lblBrowserHint.Text = "DeepSeek 已作为独立窗口显示\r\n点击“内嵌窗口”可放回此处";
                _lblBrowserHint.BringToFront();
            }
            else
            {
                var crop = await MeasureTopCropAsync(CancellationToken.None);
                _browser.Embed(_browserHost.Handle, crop);
                _lblBrowserHint.Visible = false;
            }
            _btnDock2.Text = _browser.IsEmbedded ? "还原窗口" : "内嵌窗口";
        }
        catch (Exception ex)
        {
            ShowError("切换窗口模式失败", ex);
        }
    }

    private async void Watchdog_Tick(object? sender, EventArgs e)
    {
        if (_shuttingDown || _browser == null || _recovering) return;
        if (Volatile.Read(ref _browserStarting) != 0 || _browser.Starting) return;
        if (_browser.IsAlive)
        {
            if (_browser.IsEmbedded) _browser.UpdateLayout();
            return;
        }

        _recovering = true;
        try
        {
            var wasEmbedded = _browser.IsEmbedded;
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

            if (wasEmbedded)
            {
                var crop = await MeasureTopCropAsync(CancellationToken.None);
                _browser.Embed(_browserHost.Handle, crop);
                _lblBrowserHint.Visible = false;
            }

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

    /// <summary>急停：立刻打断本轮 Agent 运行（含正在执行的工具与等待回复）。</summary>
    private void StopCurrent()
    {
        if (_runCts == null) return;
        SetStatus("正在急停...");
        _waitPageIdleAfterStop = true;
        try { _runCts.Cancel(); } catch { }
        _ps.KillCurrent();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RunOnUi(() =>
        {
            _btnStart.Enabled = !busy;
            _btnChooseDir.Enabled = !busy;
            _btnStop.Enabled = busy;
            _btnSwitch.Enabled = !busy;
            _rbNew.Enabled = !busy;
            _rbOld.Enabled = !busy;
            ShowThinking(busy);
            UpdateInputPlaceholder();
            ApplyQueueHint();
        });
    }

    private int QueueCount
    {
        get { lock (_queueLock) return _queue.Count; }
    }

    /// <summary>把队列里的下一条消息作为新的一轮发送。</summary>
    private void StartNextQueuedMessage()
    {
        if (_shuttingDown) return;

        QueuedMessage? next;
        lock (_queueLock)
        {
            next = _queue.Count > 0 ? _queue.Dequeue() : null;
        }
        ApplyQueueHint();
        if (next == null) return;

        SetStatus("正在发送队列中的消息...");
        _ = RunConversationAsync(next.Text, next.Files);
    }

    private void UpdateQueueHint() => RunOnUi(ApplyQueueHint);

    private void ApplyQueueHint()
    {
        var count = QueueCount;
        string hint;
        if (count > 0)
            hint = $"已排队 {count} 条消息，Agent 结束本轮全部工作后按顺序自动发送";
        else if (_busy)
            hint = "Agent 正在运行：现在输入的消息会排队，本轮全部结束后发送 · Esc 急停";
        else
            hint = "";

        _lblInputHint.Text = hint;
        var show = hint.Length > 0;
        _lblInputHint.Visible = show;
        if (_lblInputHint.Parent is TableLayoutPanel table && table.RowStyles.Count > 5)
            table.RowStyles[5].Height = show ? 22 : 0;
    }

    private void AttachFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要附加的文件" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames)
        {
            if (!_attachedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                _attachedFiles.Add(file);
        }
        UpdateAttachLabel();
    }

    private void ClearAttachments()
    {
        _attachedFiles.Clear();
        UpdateAttachLabel();
    }

    private void UpdateAttachLabel()
    {
        _lblAttach.Text = _attachedFiles.Count == 0
            ? "未选择附件"
            : $"已附加 {_attachedFiles.Count} 个文件：" + string.Join("; ", _attachedFiles);
        _lblAttach.ForeColor = _attachedFiles.Count == 0 ? ThemeManager.Current.SubtleText : ThemeManager.Current.Accent;
    }

    private void SaveDelay()
    {
        _cfg.DelayMinSeconds = (int)_numDelayMin.Value;
        _cfg.DelayMaxSeconds = Math.Max((int)_numDelayMin.Value, (int)_numDelayMax.Value);
        ConfigService.Save(_cfg);
    }

    /// <summary>DeepSeek 会话页地址，形如 https://chat.deepseek.com/a/chat/s/&lt;id&gt;。</summary>
    private static bool IsSessionUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("/a/chat/s/", StringComparison.OrdinalIgnoreCase);

    private void SaveLastSession()
    {
        if (!IsSessionUrl(_lastSessionUrl)) return;
        _cfg.LastSessionUrl = _lastSessionUrl;
        ConfigService.Save(_cfg);
    }

    private void ResetConfig()
    {
        if (MessageBox.Show(this, "确定要重置所有配置吗？", "Deepseek-Desktop-Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _cfg = new AppConfig();
        ConfigService.Save(_cfg);
        _chkNight.Checked = false;
        _chkAuto.Checked = false;
        _chkEnterSend.Checked = true;
        _numDelayMin.Value = _cfg.DelayMinSeconds;
        _numDelayMax.Value = _cfg.DelayMaxSeconds;
        _trkOpacity.Value = _cfg.OpacityPercent;
        _lblWorkDir.Text = "未选择工作目录";
        _txtInput.Clear();
        ApplyPalette();
        SetStatus("配置已重置");
    }

    private void ClearRunLog()
    {
        _lstRuns.Items.Clear();
        _txtPsCode.Clear();
        _txtPsOut.Clear();
        _currentRunIndex = -1;
        _lblPsState.Text = "状态：空闲";
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("打开目录失败", ex);
        }
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
                    if (focused is TextBoxBase tbV) { tbV.Paste(); return true; }
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

    public Task<bool> ConfirmPowerShellAsync(string code)
    {
        if (!InvokeRequired)
            return Task.FromResult(SafetyForm.Confirm(code));

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(() =>
            {
                try { tcs.SetResult(SafetyForm.Confirm(code)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }));
        }
        catch (Exception ex) { tcs.SetException(ex); }
        return tcs.Task;
    }

    public void BeginTool(string name, string detail)
    {
        RunOnUi(() =>
        {
            _lblPsState.Text = "状态：正在执行 " + name;
            _lblPsState.ForeColor = ThemeManager.Current.Accent;
            _txtPsCode.Text = detail;
            _txtPsOut.Clear();
            _currentRunIndex = _lstRuns.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + name + "  (运行中)");
            _lstRuns.SelectedIndex = _currentRunIndex;
        });
    }

    public void ToolOutput(string line)
    {
        RunOnUi(() =>
        {
            if (_txtPsOut.TextLength > 200_000) return;
            _txtPsOut.AppendText(line + Environment.NewLine);
        });
    }

    public void EndTool(string name, string result, int? exitCode)
    {
        RunOnUi(() =>
        {
            _lblPsState.Text = exitCode.HasValue ? $"状态：空闲（上次退出码 {exitCode}）" : "状态：空闲";
            _lblPsState.ForeColor = ThemeManager.Current.SubtleText;
            if (_currentRunIndex >= 0 && _currentRunIndex < _lstRuns.Items.Count)
            {
                var suffix = exitCode.HasValue ? $"  退出码 {exitCode}" : "";
                _lstRuns.Items[_currentRunIndex] = DateTime.Now.ToString("HH:mm:ss") + "  " + name + suffix;
            }
            if (!string.IsNullOrWhiteSpace(result) && _txtPsOut.TextLength < 200_000)
                _txtPsOut.AppendText(Environment.NewLine + "[结束] " + TrimForLog(result, 400) + Environment.NewLine);
        });
    }

    private static string TrimForLog(string text, int max)
    {
        var oneLine = text.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max) + "...";
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

    private void ShowError(string title, Exception ex)
    {
        SetStatus(title + ": " + ex.Message);
        if (!IsDisposed)
        {
            RunOnUi(() => MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error));
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        if (_shuttingDown) return;
        _shuttingDown = true;
        _watchdog.Stop();
        _layoutTimer.Stop();
        _guard.Enabled = false;

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
        try { _layoutTimer.Stop(); _layoutTimer.Dispose(); } catch { }
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

    private static class CaretHider
    {
        [DllImport("user32.dll")]
        public static extern bool HideCaret(IntPtr hwnd);
    }

    /// <summary>只读文本框：点击、选择、滚动都正常，但不显示闪烁光标。</summary>
    private sealed class NoCaretTextBox : TextBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case 0x0007:
                case 0x0100:
                case 0x0101:
                case 0x0200:
                case 0x0201:
                case 0x0202:
                case 0x0203:
                case 0x020A:
                case 0x0114:
                case 0x0115:
                    CaretHider.HideCaret(Handle);
                    break;
            }
        }
    }

    /// <summary>只读富文本框：保留选择与复制能力，但不显示闪烁光标。</summary>
    private sealed class NoCaretRichTextBox : RichTextBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case 0x0007:
                case 0x0100:
                case 0x0101:
                case 0x0200:
                case 0x0201:
                case 0x0202:
                case 0x0203:
                case 0x020A:
                case 0x0114:
                case 0x0115:
                    CaretHider.HideCaret(Handle);
                    break;
            }
        }
    }

    /// <summary>
    /// 纵向滚动条：半透明长线作轨道，滑块是一段按内容比例变化长度的短线。
    /// 作为独立控件放在文本区右侧，点击或拖动它不会让文本框获得焦点，因此不出现光标。
    /// </summary>
    private sealed class LineScrollBar : Control
    {
        private const int SbVert = 1;
        private const int WmVScroll = 0x0115;
        private const int SbPageUp = 2;
        private const int SbPageDown = 3;
        private const int SbThumbPosition = 4;
        private const int PadY = 9;
        private const uint SifAll = 0x17;

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public uint Size;
            public uint Mask;
            public int Min;
            public int Max;
            public uint Page;
            public int Pos;
            public int TrackPos;
        }

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

        [DllImport("user32.dll")]
        private static extern bool HideCaret(IntPtr hwnd);

        private IntPtr _target;
        private int _min;
        private int _max;
        private int _pos;
        private uint _page;
        private bool _dragging;
        private bool _hover;
        private int _grabOffset;

        public LineScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            TabStop = false;
            Cursor = Cursors.Hand;
            BackColor = ThemeManager.Current.Input;
        }

        /// <summary>隐藏文本框自带的 Windows 滚动条，但保留滚动能力。</summary>
        public static void HideNativeBar(IntPtr handle, bool vertical)
        {
            if (handle == IntPtr.Zero) return;
            ShowScrollBar(handle, vertical ? SbVert : 0, false);
        }

        public void Attach(IntPtr target)
        {
            _target = target;
            RefreshScroll();
        }

        private bool HasOverflow =>
            _target != IntPtr.Zero && _page > 0 && _max > _min && _max - _min > (int)_page;

        private float ThumbLength
        {
            get
            {
                var track = Math.Max(1, Height - PadY * 2);
                var visible = _page / (float)Math.Max(1, _max - _min);
                return Math.Min(Math.Max(track * visible, 18f), track);
            }
        }

        private float ThumbTop
        {
            get
            {
                var track = Math.Max(1, Height - PadY * 2);
                var span = Math.Max(1, _max - _min - (int)_page);
                var ratio = Math.Clamp((_pos - _min) / (float)span, 0f, 1f);
                return PadY + ratio * (track - ThumbLength);
            }
        }

        public void RefreshScroll()
        {
            if (_target == IntPtr.Zero) return;

            var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = SifAll };
            if (!GetScrollInfo(_target, SbVert, ref info))
            {
                if (_max != 0)
                {
                    _max = 0;
                    _pos = 0;
                    _page = 0;
                    Invalidate();
                }
                return;
            }

            if (info.Min == _min && info.Max == _max && info.Pos == _pos && info.Page == _page) return;

            _min = info.Min;
            _max = info.Max;
            _pos = info.Pos;
            _page = info.Page;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!HasOverflow) return;

            var theme = ThemeManager.Current;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var x = Width / 2f;
            var top = (float)PadY;
            var bottom = Height - PadY;

            // 白色半透明长线打底，浅色主题下自动换成深色半透明，保证可见。
            var light = theme.Background.GetBrightness() > 0.5f;
            var trackColor = light ? Color.FromArgb(50, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255);
            using (var track = new Pen(trackColor, 3f))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, x, top, x, bottom);
            }

            // 滑块是一小段线，长度随可视区域与总内容的比例变化。
            using var thumb = new Pen(theme.Accent, _dragging || _hover ? 5f : 4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(thumb, x, ThumbTop, x, ThumbTop + ThumbLength);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !HasOverflow)
            {
                base.OnMouseDown(e);
                return;
            }

            var top = ThumbTop;
            var bottom = top + ThumbLength;
            if (e.Y >= top - 4 && e.Y <= bottom + 4)
            {
                _dragging = true;
                _grabOffset = e.Y - (int)top;
            }
            else
            {
                SendMessage(_target, WmVScroll, (IntPtr)(e.Y > bottom ? SbPageDown : SbPageUp), IntPtr.Zero);
                HideCaret(_target);
            }

            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) DragTo(e.Y);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                RefreshScroll();
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        private void DragTo(int y)
        {
            var track = Math.Max(1, Height - PadY * 2);
            var thumb = ThumbLength;
            var top = Math.Clamp(y - _grabOffset, PadY, PadY + track - thumb);
            var ratio = (top - PadY) / Math.Max(1f, track - thumb);
            var span = Math.Max(0, _max - _min - (int)_page);
            var pos = _min + (int)Math.Round(ratio * span);

            SendMessage(_target, WmVScroll, (IntPtr)((pos << 16) | SbThumbPosition), IntPtr.Zero);
            HideCaret(_target);
            _pos = pos;
            Invalidate();
        }

        /// <summary>把鼠标滚轮转发给当前滚动条绑定的文本框。</summary>
        public void HandleWheel(IntPtr wParam, IntPtr lParam)
        {
            if (_target == IntPtr.Zero) return;
            ScrollByWheel(_target, wParam);
        }

        /// <summary>原生文本框不会处理 WM_MOUSEWHEEL，这里按行发送 WM_VSCROLL。</summary>
        public static void ScrollByWheel(IntPtr hwnd, IntPtr wParam)
        {
            if (hwnd == IntPtr.Zero) return;
            var delta = (short)((long)wParam >> 16);
            var notches = Math.Max(1, Math.Abs(delta) / 120);
            var command = delta > 0 ? 0 : 1; // SB_LINEUP / SB_LINEDOWN
            for (var i = 0; i < notches * 3; i++)
            {
                SendMessage(hwnd, WmVScroll, (IntPtr)command, IntPtr.Zero);
            }
            HideCaret(hwnd);
        }
    }

    /// <summary>细线 + 圆点样式的滑杆，替代系统 TrackBar，避免原生控件在布局时闪动。</summary>
    private sealed class LineSlider : Control
    {
        private const int Pad = 12;
        private int _min = 30;
        private int _max = 100;
        private int _value = 100;
        private bool _dragging;
        private bool _hover;

        public LineSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Height = 28;
            Cursor = Cursors.Hand;
        }

        [System.ComponentModel.DefaultValue(30)]
        public int Minimum
        {
            get => _min;
            set { _min = value; _value = Math.Clamp(_value, _min, _max); Invalidate(); }
        }

        [System.ComponentModel.DefaultValue(100)]
        public int Maximum
        {
            get => _max;
            set { _max = Math.Max(value, _min); _value = Math.Clamp(_value, _min, _max); Invalidate(); }
        }

        [System.ComponentModel.DefaultValue(100)]
        public int Value
        {
            get => _value;
            set
            {
                var v = Math.Clamp(value, _min, _max);
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        private float Ratio => (_value - _min) / (float)Math.Max(1, _max - _min);

        private float ThumbX => Pad + Ratio * (Width - Pad * 2);

        protected override void OnPaint(PaintEventArgs e)
        {
            var theme = ThemeManager.Current;
            e.Graphics.Clear(theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var cy = Height / 2f;
            var left = Pad;
            var right = Width - Pad;
            var x = ThumbX;

            using (var track = new Pen(UiPaint.Blend(theme.Border, theme.Background, 0.25), 4f))
            using (var filled = new Pen(theme.Accent, 4f))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                filled.StartCap = filled.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, left, cy, right, cy);
                e.Graphics.DrawLine(filled, left, cy, x, cy);
            }

            var radius = _dragging || _hover ? 7f : 6f;
            using var knob = new SolidBrush(theme.Accent);
            e.Graphics.FillEllipse(knob, x - radius, cy - radius, radius * 2, radius * 2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                _dragging = true;
                SetFromMouse(e.X);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) SetFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var step = e.Shift ? 10 : 1;
            switch (e.KeyCode)
            {
                case Keys.Left: Value -= step; e.Handled = true; break;
                case Keys.Right: Value += step; e.Handled = true; break;
                case Keys.Home: Value = _min; e.Handled = true; break;
                case Keys.End: Value = _max; e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        private void SetFromMouse(int x)
        {
            var ratio = (x - Pad) / (float)Math.Max(1, Width - Pad * 2);
            Value = (int)Math.Round(_min + Math.Clamp(ratio, 0f, 1f) * (_max - _min));
        }
    }

    /// <summary>标签页淡入用的快照遮罩：不透明绘制背景，再按透明度叠加新的页面快照。</summary>
    private sealed class FadeOverlay : Control
    {
        private Bitmap? _frame;
        private float _alpha;

        public FadeOverlay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            Visible = false;
        }

        public void ShowFrame(Bitmap? frame, float alpha)
        {
            _frame = frame;
            _alpha = Math.Clamp(alpha, 0f, 1f);
            Visible = true;
            BringToFront();
            Update();
        }

        public void SetAlpha(float alpha)
        {
            _alpha = Math.Clamp(alpha, 0f, 1f);
            Invalidate();
        }

        public void HideOverlay()
        {
            Visible = false;
            _frame?.Dispose();
            _frame = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ThemeManager.Current.Background);
            if (_frame == null || _alpha <= 0f) return;

            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = _alpha });
            e.Graphics.DrawImage(_frame,
                new Rectangle(0, 0, Width, Height),
                0, 0, _frame.Width, _frame.Height,
                GraphicsUnit.Pixel, attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _frame?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Agent 运行期间通过输入框提交、等待本轮结束后发送的消息。</summary>
    private sealed class QueuedMessage
    {
        public QueuedMessage(string text, List<string> files)
        {
            Text = text;
            Files = files;
        }

        public string Text { get; }
        public List<string> Files { get; }
    }
}
