using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed class MainForm : Form, IToolHost
{
    private AppConfig _cfg = ConfigService.Load();
    private readonly string _tempDir = AppPaths.CreateSessionTempDir();
    private readonly PowerShellRunner _ps;
    private readonly KeyboardCloseGuard _guard = new();
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 700 };

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
    private int _userMessageCount;
    private int _toolCount;
    private int _currentRunIndex = -1;
    private string _lastSessionUrl = "";
    private readonly List<string> _attachedFiles = new();

    private Label _lblWorkDir = null!;
    private Label _lblStatus = null!;
    private Label _lblAttach = null!;
    private Label _lblPsState = null!;
    private Label _lblBrowserHint = null!;
    private Label _lblOpacity = null!;
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
    private TextBox _txtPsCode = null!;
    private TextBox _txtPsOut = null!;
    private RichTextBox _txtReply = null!;
    private ListBox _lstRuns = null!;
    private TabControl _tabs = null!;
    private BufferedPanel _browserHost = null!;
    private NumericUpDown _numDelayMin = null!;
    private NumericUpDown _numDelayMax = null!;
    private TrackBar _trkOpacity = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _counterLabel = null!;
    private SplitContainer _split = null!;
    private SplitContainer _psSplit = null!;
    private SplitContainer _psRight = null!;

    public MainForm()
    {
        _ps = new PowerShellRunner(_tempDir);
        _ps.OutputLine += line => ToolOutput(line);
        BuildUi();
        ApplyPalette();
        LoadConfigIntoUi();

        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
        FormClosed += (_, _) => Cleanup();
        _watchdog.Tick += Watchdog_Tick;
        _layoutTimer.Tick += (_, _) => _browser?.UpdateLayout();
    }

    public string TempDir => _tempDir;
    public string WorkDir => _cfg.WorkDir;
    public bool IsAutoExecute => _chkAuto.Checked;
    public CancellationToken Token => _runCts?.Token ?? CancellationToken.None;

    private void BuildUi()
    {
        Text = "Deepseek-Desktop-Agent";
        ClientSize = new Size(1220, 780);
        MinimumSize = new Size(1020, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var topBar = new BufferedPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(8, 6, 8, 4),
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
            Width = 250,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _rbNew = new RadioButton { Text = "新对话", Checked = true, AutoSize = true, Height = 32, Margin = new Padding(10, 6, 2, 0) };
        _rbOld = new RadioButton { Text = "旧对话", AutoSize = true, Height = 32, Margin = new Padding(2, 6, 8, 0) };
        _btnStart = MakeButton("开始 / 继续", 106, "accent");
        _btnStop = MakeButton("停止", 74);
        _btnStop.Enabled = false;
        _chkNight = new CheckBox { Text = "夜间模式", AutoSize = true, Height = 32, Margin = new Padding(8, 6, 4, 0) };
        _chkAuto = new CheckBox { Text = "自动执行", AutoSize = true, Height = 32, Margin = new Padding(4, 6, 8, 0) };
        _lblStatus = new Label
        {
            Text = "请选择工作目录后开始",
            AutoSize = false,
            Width = 260,
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

        _tabs = new BufferedTabControl { Dock = DockStyle.Fill };

        var convoTab = new TabPage("对话");
        var psTab = new TabPage("PowerShell");
        var settingsTab = new TabPage("设置");
        _tabs.TabPages.Add(convoTab);
        _tabs.TabPages.Add(psTab);
        _tabs.TabPages.Add(settingsTab);

        BuildConversationTab(convoTab);
        BuildPowerShellTab(psTab);
        BuildSettingsTab(settingsTab);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6
        };
        _split.Panel1.Controls.Add(_tabs);

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
        _rbOld.CheckedChanged += (_, _) => { if (_rbOld.Checked) SetStatus("旧对话：开始时会弹出会话选择"); };
        _chkNight.CheckedChanged += (_, _) => { _cfg.NightMode = _chkNight.Checked; ConfigService.Save(_cfg); ApplyPalette(); };
        _chkAuto.CheckedChanged += (_, _) => { _cfg.AutoExecute = _chkAuto.Checked; ConfigService.Save(_cfg); };
        _chkEnterSend.CheckedChanged += (_, _) => { _cfg.EnterToSend = _chkEnterSend.Checked; ConfigService.Save(_cfg); };
        _btnDock2.Click += (_, _) => ToggleDock();
        _btnSwitch.Click += async (_, _) => await SwitchSessionAsync();
        _browserHost.Resize += (_, _) => _browser?.UpdateLayout();
        _browserHost.MouseDown += (_, _) => _browser?.Focus();
    }

    private void BuildConversationTab(TabPage page)
    {
        page.Padding = new Padding(4);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        _txtReply = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        _lblAttach = new Label
        {
            Text = "未选择附件",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _txtInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
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

        layout.Controls.Add(_txtReply, 0, 0);
        layout.Controls.Add(_lblAttach, 0, 1);
        layout.Controls.Add(_txtInput, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        page.Controls.Add(layout);
    }

    private void BuildPowerShellTab(TabPage page)
    {
        page.Padding = new Padding(4);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _lblPsState = new Label
        {
            Text = "状态：空闲",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _psSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 5
        };
        _lstRuns = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
        _psSplit.Panel1.Controls.Add(_lstRuns);

        _psRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 5
        };
        _txtPsCode = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5F)
        };
        _txtPsOut = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5F)
        };
        _psRight.Panel1.Controls.Add(_txtPsCode);
        _psRight.Panel2.Controls.Add(_txtPsOut);
        _psSplit.Panel2.Controls.Add(_psRight);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _btnClearRuns = MakeButton("清空记录", 92);
        _btnOpenTemp = MakeButton("打开临时目录", 112);
        var openConfig = MakeButton("打开配置目录", 112);
        _btnClearRuns.Click += (_, _) => ClearRunLog();
        _btnOpenTemp.Click += (_, _) => OpenFolder(_tempDir);
        openConfig.Click += (_, _) => OpenFolder(AppPaths.AppDataDir);
        buttons.Controls.AddRange(new Control[] { _btnClearRuns, _btnOpenTemp, openConfig });

        layout.Controls.Add(_lblPsState, 0, 0);
        layout.Controls.Add(_psSplit, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        page.Controls.Add(layout);
    }

    private void BuildSettingsTab(TabPage page)
    {
        page.Padding = new Padding(10);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        flow.Controls.Add(SectionLabel("防封控延时（自动执行开启时生效）"));
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.Add(new Label { Text = "最小秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
        _numDelayMin = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 80, Height = 28 };
        delayRow.Controls.Add(_numDelayMin);
        delayRow.Controls.Add(new Label { Text = "最大秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(18, 8, 6, 0) });
        _numDelayMax = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 80, Height = 28 };
        delayRow.Controls.Add(_numDelayMax);
        flow.Controls.Add(delayRow);

        flow.Controls.Add(SectionLabel("窗口透明度"));
        var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _trkOpacity = new TrackBar { Minimum = 30, Maximum = 100, TickFrequency = 5, Width = 300, Height = 40 };
        _lblOpacity = new Label { Text = "100%", AutoSize = false, Width = 60, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 8, 0, 0) };
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
        _trkOpacity.Scroll += (_, _) =>
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

    private static Button MakeButton(string text, int width, string? tag = null) => new()
    {
        Text = text,
        Width = width,
        Height = 32,
        Tag = tag,
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
        UpdateCounters();
    }

    private void ApplyPalette()
    {
        ThemeManager.Apply(this, _chkNight.Checked ? ThemeManager.Dark : ThemeManager.Light);
        _lblBrowserHint.ForeColor = ThemeManager.Current.SubtleText;
        _statusLabel.ForeColor = ThemeManager.Current.SubtleText;
        _counterLabel.ForeColor = ThemeManager.Current.SubtleText;
        _lblWorkDir.ForeColor = ThemeManager.Current.Text;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            _psSplit.SplitterDistance = 210;
            _psRight.SplitterDistance = 260;
            _split.SplitterDistance = Math.Max(420, _split.Width - 620);
        }
        catch { }

        _watchdog.Start();
        _layoutTimer.Start();

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

    private async Task ChooseDirClickedAsync()
    {
        if (_busy) return;
        if (!await ChooseWorkDirAsync()) return;
        try
        {
            await EnsureBrowserAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(_txtInput.Text) || !_txtInput.Text.Contains("# System Prompt", StringComparison.Ordinal))
                _txtInput.Text = Prompt.Build(_cfg.WorkDir, _tempDir);
            _tabs.SelectedIndex = 0;
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
            _cdp = new CdpClient();

        if (!_cdp.IsConnected)
        {
            SetStatus("正在连接 DeepSeek 调试页面...");
            await _cdp.ConnectAsync(_cfg.BrowserPort, ct);
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
            else
            {
                await _cdp.NavigateAsync("https://chat.deepseek.com");
                message = "system.txt";
            }

            _lastSessionUrl = await _cdp.GetCurrentUrlAsync();
            SaveLastSession();
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
        if (_busy) return;
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

                reply = await SendAndReceiveAsync(result.Message, result.Files, cts.Token);
                ShowReply(reply);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("已停止");
            ShowReply("[已停止] 当前任务被用户终止。");
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
                _lastSessionUrl = await _cdp.GetCurrentUrlAsync(ct);
                SaveLastSession();
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
        RunOnUi(() =>
        {
            _txtReply.Text = reply;
            _txtReply.SelectionStart = _txtReply.TextLength;
            _txtReply.ScrollToCaret();
        });
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

    private void StopCurrent()
    {
        if (_runCts == null) return;
        SetStatus("正在停止...");
        try { _runCts.Cancel(); } catch { }
        _ps.KillCurrent();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RunOnUi(() =>
        {
            _btnStart.Enabled = !busy;
            _btnSend.Enabled = !busy;
            _btnChooseDir.Enabled = !busy;
            _btnStop.Enabled = busy;
            _btnSwitch.Enabled = !busy;
            _rbNew.Enabled = !busy;
            _rbOld.Enabled = !busy;
        });
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

    private void SaveLastSession()
    {
        if (string.IsNullOrWhiteSpace(_lastSessionUrl)) return;
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
                    if (_chkEnterSend.Checked && !_busy)
                    {
                        _ = SendUserAsync();
                        return true;
                    }
                    break;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void SetStatus(string text)
    {
        RunOnUi(() =>
        {
            _lblStatus.Text = text;
            _statusLabel.Text = text;
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
            _lblPsState.ForeColor = ThemeManager.Current.Text;
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
        try { _watchdog.Stop(); _watchdog.Dispose(); } catch { }
        try { _layoutTimer.Stop(); _layoutTimer.Dispose(); } catch { }
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
