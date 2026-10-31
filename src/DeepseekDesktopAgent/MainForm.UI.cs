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

        var sideGrid = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var brand = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brandTitle = new Label
        {
            Text = "DeepSeek Agent",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            Margin = Padding.Empty
        };
        var brandSub = new Label
        {
            Text = "本机执行 · 技能优先加载",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Tag = "subtle",
            Margin = Padding.Empty
        };
        _btnNewChat = MakeButton("新建对话", 76, "accent");
        _btnNewChat.Dock = DockStyle.None;
        _btnNewChat.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnNewChat.Margin = new Padding(0, 6, 0, 0);
        brand.Controls.Add(brandTitle, 0, 0);
        brand.Controls.Add(brandSub, 0, 1);
        brand.Controls.Add(_btnNewChat, 1, 0);

        // 技能入口：压缩成一行，避免和会话列表抢空间。
        var skillCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            FillRole = PanelFill.Input,
            Padding = new Padding(10, 0, 6, 0),
            Margin = new Padding(0, 1, 0, 1)
        };
        var skillGrid = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        // 卡片内部容器的底色必须和卡片一致，否则会盖住卡片填充、只留下边缘一条，看着像孤立方框。
        skillGrid.BackColor = ThemeManager.Current.Input;
        _inputFill.Add(skillGrid);
        skillGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        skillGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        var skillTitle = new Label
        {
            Text = "技能 · 优先加载",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Tag = "subtle",
            Margin = Padding.Empty
        };
        var btnSkills = MakeButton("技能目录", 74);
        btnSkills.Dock = DockStyle.Fill;
        btnSkills.Margin = new Padding(2, 3, 0, 3);
        skillGrid.Controls.Add(skillTitle, 0, 0);
        skillGrid.Controls.Add(btnSkills, 1, 0);
        skillCard.Controls.Add(skillGrid);

        _txtSessionFilter = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            PlaceholderText = "搜索会话",
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var searchCard = UiPaint.WrapField(_txtSessionFilter, 10, 7, 9);
        searchCard.Margin = new Padding(0, 2, 0, 6);
        _txtSessionFilter.TextChanged += (_, _) => RefreshSessions();

        // 会话列表：两行行高（标题 / 时间·轮次），按工作目录分组。
        _lstSessions = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = _sessionTitleFont,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            Margin = new Padding(0, 0, 0, 4)
        };
        _lstSessions.SelectedIndexChanged += (_, _) =>
        {
            var node = SelectedSessionNode();
            if (!_suppressSessionNav && node != null)
            {
                LoadSessionToChatArea(node);
            }
        };
        _lstSessions.DrawItem += DrawSessionRow;
        // 右键菜单：重命名 / 删除；右键先选中该行，避免误操作别的会话。
        _menuSessions = new ContextMenuStrip();
        _menuSessions.Items.Add("重命名会话", null, (_, _) => RenameSelectedSession());
        _menuSessions.Items.Add("删除会话", null, (_, _) => DeleteSelectedSession());
        _lstSessions.ContextMenuStrip = _menuSessions;
        _lstSessions.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var idx = _lstSessions.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < _sessionMap.Count && _sessionMap[idx] != null)
                _lstSessions.SelectedIndex = idx;
        };
        _lstSessions.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2) { e.SuppressKeyPress = true; RenameSelectedSession(); }
            else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; DeleteSelectedSession(); }
        };

        _lblStatus = new Label
        {
            Text = "请选择工作目录后开始",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(2, 0, 2, 0)
        };

        var sideBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0)
        };
        _btnOpenDetail = MakeButton("详情", 64);
        _btnOpenSettings = MakeButton("设置", 64);
        _btnOpenPs = MakeButton("PowerShell", 96);
        sideBottom.Controls.AddRange(new Control[] { _btnOpenDetail, _btnOpenSettings, _btnOpenPs });

        sideGrid.Controls.Add(brand, 0, 0);
        sideGrid.Controls.Add(skillCard, 0, 1);
        sideGrid.Controls.Add(searchCard, 0, 2);
        sideGrid.Controls.Add(_lstSessions, 0, 3);
        sideGrid.Controls.Add(_lblStatus, 0, 4);
        sideGrid.Controls.Add(sideBottom, 0, 5);

        var sidebar = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 12, 10, 12),
            Tag = "card"
        };
        sidebar.Controls.Add(sideGrid);
        // 主区域页面容器：对话 / PowerShell / 设置 / 会话回溯。



        // 主区域页面容器：对话 / PowerShell / 设置 / 会话回溯。
        var mainHost = new BufferedPanel { Dock = DockStyle.Fill };
        var convoPage = new Panel { Dock = DockStyle.Fill, Visible = true };
        var psPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var settingsPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var sessionsPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var detailPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        _sessionsPage = sessionsPage;

        BuildConversationTab(convoPage);
        BuildPowerShellTab(psPage);
        BuildSettingsTab(settingsPage);
        BuildSessionsTab(sessionsPage);
        BuildDetailTab(detailPage);
        mainHost.Controls.Add(convoPage);
        mainHost.Controls.Add(psPage);
        mainHost.Controls.Add(settingsPage);
        mainHost.Controls.Add(sessionsPage);
        mainHost.Controls.Add(detailPage);

        _fadeOverlay = new FadeOverlay();
        mainHost.Controls.Add(_fadeOverlay);
        _fadeTimer.Tick += (_, _) => AdvanceTabFade();

        _pages = new Control[] { convoPage, psPage, settingsPage, sessionsPage, detailPage };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6
        };
        _split.Panel1.Controls.Add(sidebar);
        _split.Panel2.Controls.Add(mainHost);

        _statusStrip = new StatusStrip { SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("就绪");
        _counterLabel = new ToolStripStatusLabel("会话 0 | 工具 0") { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_counterLabel);

        Controls.Add(_split);
        Controls.Add(_statusStrip);

        _btnNewChat.Click += (_, _) =>
        {
            // 新建会话：只留空白对话区，不显示系统提示词、附件或任何旧内容。
            _rbNew.Checked = true;
            _sessionId = null;
            _turnId = null;
            _lastReplyRaw = "";
            _txtInput.Clear();
            ClearAttachments();
            RenderReply();
            ShowMainPage(0);
            _txtInput.Focus();
            SetStatus("新对话：描述任务后按 Ctrl+Enter 发送");
        };
        _btnOpenSettings.Click += (_, _) => ShowMainPage(2);
        _btnOpenPs.Click += (_, _) => ShowMainPage(1);
        _btnOpenDetail.Click += (_, _) => ShowMainPage(4);
        btnSkills.Click += (_, _) => OpenFolder(_tempDir);
        _btnChooseDir.Click += async (_, _) => await ChooseDirClickedAsync();
        _btnChooseDir2.Click += async (_, _) => await ChooseDirClickedAsync();
        _btnShowWeb.Click += async (_, _) => await ShowWebSessionAsync();
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
        _chkDeepThink.CheckedChanged += (_, _) => { _ = ToggleDeepThinkAsync(); };
        _chkEnterSend.CheckedChanged += (_, _) => { _cfg.EnterToSend = _chkEnterSend.Checked; ConfigService.Save(_cfg); };
        _btnSwitch.Click += async (_, _) => await SwitchSessionAsync();
    }
    private void BuildConversationTab(Panel page)
    {
        // 主区域只放和 AI 的对话内容与输入；工作目录、系统提示词、附件、网页会话统一收进“详情”页。
        page.Padding = new Padding(26, 14, 26, 14);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 0 回复 / 空态标题
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // 1 思考提示
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // 2 消息队列
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));  // 3 输入（含底部工具行）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // 4 输入提示
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 5 选择文件夹
        _convoLayout = layout;

        // 回复区与空态标题共用同一块区域：标题在整块空白区域里上下居中，像 Trae 那样。
        var replyHost = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 6)
        };
        _lblHeroTitle = new Label
        {
            Text = "Build With DeepSeek",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold)
        };
        replyHost.Controls.Add(_lblHeroTitle);

        _cardReply = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 12, 10),
            Margin = Padding.Empty,
            Radius = 14,
            FillRole = PanelFill.Input
        };
        replyHost.Controls.Add(_cardReply);
        _cardReply.BringToFront();

        _txtReply = new NoCaretRichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.ForcedVertical,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        _replyMask = new ScrollMask
        {
            Width = SystemInformation.VerticalScrollBarWidth,
            Target = _txtReply
        };
        _replyScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _cardReply.Controls.Add(_txtReply);
        _cardReply.Controls.Add(_replyScroll);
        _cardReply.Controls.Add(_replyMask);
        _replyMask.BringToFront();
        _cardReply.Resize += (_, _) => _replyMask.PlaceOver(_txtReply, _replyScroll);
        _txtReply.HandleCreated += (_, _) => _replyScroll.Attach(_txtReply.Handle);
        _txtReply.TextChanged += (_, _) => _replyScroll.RefreshScroll();

        _thinking = new ThinkingIndicator { Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 6) };

        // 队列预览：折叠在输入框上方，Header 点击切换。默认高度 0，有消息时按需展开。
        _queuePreviewPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(2, 0, 2, 4),
            Radius = 10,
            FillRole = PanelFill.Input,
            Visible = false
        };
        _queuePreviewHeader = new Label
        {
            Text = "消息队列",
            Dock = DockStyle.Top,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Bold)
        };
        _queuePreviewHeader.Click += (_, _) => ToggleQueuePreview();
        _queuePreviewList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Visible = false,
            BackColor = ThemeManager.Current.Input
        };
        _inputFill.Add(_queuePreviewList);
        _queuePreviewPanel.Controls.Add(_queuePreviewList);
        _queuePreviewPanel.Controls.Add(_queuePreviewHeader);

        _cardInput = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 8, 6),
            Margin = new Padding(2, 0, 2, 0),
            Radius = 12,
            FillRole = PanelFill.Input
        };
        // 输入卡片：上方是文本区，下方一行工具（附件 + 模型名 + 发送/急停），像 Trae 的输入框。
        var inputGrid = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        inputGrid.BackColor = ThemeManager.Current.Input;
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var inputHost = new BufferedPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        inputHost.BackColor = ThemeManager.Current.Input;
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
            Left = 2,
            Top = 2,
            Visible = false,
            Cursor = Cursors.IBeam,
            Font = new Font("Microsoft YaHei UI", 8.25F)
        };
        _lblInputPlaceholder.MouseDown += (_, _) => { _lblInputPlaceholder.Visible = false; _txtInput.Focus(); };
        _inputMask = new ScrollMask
        {
            Width = SystemInformation.VerticalScrollBarWidth,
            Target = _txtInput
        };
        _inputScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        inputHost.Controls.Add(_txtInput);
        inputHost.Controls.Add(_lblInputPlaceholder);
        inputHost.Controls.Add(_inputScroll);
        inputHost.Controls.Add(_inputMask);
        _inputMask.BringToFront();
        inputHost.Resize += (_, _) => _inputMask.PlaceOver(_txtInput, _inputScroll);

        var toolbar = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        toolbar.BackColor = ThemeManager.Current.Input;
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        var btnPlus = MakeButton("+", 28);
        btnPlus.Dock = DockStyle.Fill;
        btnPlus.Margin = new Padding(0, 0, 4, 0);
        btnPlus.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        btnPlus.Click += (_, _) => AttachFiles();
        _btnSend = MakeButton("↑", 40, "accent");
        _btnSend.Dock = DockStyle.Fill;
        _btnSend.Margin = Padding.Empty;
        _btnSend.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _btnStop = MakeButton("■", 40);
        _btnStop.Dock = DockStyle.Fill;
        _btnStop.Margin = new Padding(0, 0, 4, 0);
        _btnStop.Font = new Font("Microsoft YaHei UI", 9F);
        ((AccentButton)_btnStop).Danger = true;
        _btnStop.Visible = false;
        toolbar.Controls.Add(btnPlus, 0, 0);
        toolbar.Controls.Add(_btnStop, 2, 0);
        toolbar.Controls.Add(_btnSend, 3, 0);

        inputGrid.Controls.Add(inputHost, 0, 0);
        inputGrid.Controls.Add(toolbar, 0, 1);
        _cardInput.Controls.Add(inputGrid);
        _inputFill.Add(inputGrid);
        _inputFill.Add(inputHost);
        _inputFill.Add(toolbar);
        _txtInput.HandleCreated += (_, _) => _inputScroll.Attach(_txtInput.Handle);
        _txtInput.TextChanged += (_, _) =>
        {
            UpdateInputPlaceholder();
            _inputScroll.RefreshScroll();
        };
        _txtInput.EnabledChanged += (_, _) => UpdateInputPlaceholder();
        _txtInput.Enter += (_, _) =>
        {
            _cardInput.Highlight = true;
            _cardInput.Invalidate();
            _lblInputPlaceholder.Visible = false;
            UpdateInputPlaceholder();
        };
        _txtInput.Leave += (_, _) =>
        {
            _cardInput.Highlight = false;
            _cardInput.Invalidate();
            UpdateInputPlaceholder();
        };

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

        // 选择文件夹：紧贴输入框下方，只保留入口与当前路径。
        var workRow = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(2, 4, 2, 2)
        };
        workRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        workRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _btnChooseDir = MakeButton("📁  选择项目  ▾", 105, "subtle");
        _btnChooseDir.TextAlign = ContentAlignment.MiddleCenter;
        _btnChooseDir.Dock = DockStyle.Fill;
        _btnChooseDir.Margin = new Padding(0, 0, 8, 0);
        // Tag 已在 MakeButton 设置
        _lblWorkDir = new Label
        {
            Text = "未选择工作目录",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(2, 0, 0, 0)
        };
        workRow.Controls.Add(_btnChooseDir, 0, 0);
        workRow.Controls.Add(_lblWorkDir, 1, 0);

        void SendClicked(object? s, EventArgs e) => _ = SendUserAsync();
        _btnSend.Click += SendClicked;

        layout.Controls.Add(replyHost, 0, 0);
        layout.Controls.Add(_thinking, 0, 1);
        layout.Controls.Add(_queuePreviewPanel, 0, 2);
        layout.Controls.Add(_cardInput, 0, 3);
        layout.Controls.Add(_lblInputHint, 0, 4);
        layout.Controls.Add(workRow, 0, 5);
        page.Controls.Add(layout);
    }

    /// <summary>详情页：系统提示词、工作目录路径、附件、DeepSeek 网页与深度思考集中在这里，
    /// 对话页只保留“和 AI 的对话内容”。</summary>
    private void BuildDetailTab(Panel page)
    {
        page.Padding = new Padding(20, 10, 20, 12);
        // 固定高度的小节 + 中间自适应高度的提示词区，内容始终一屏放下，不出现原生滚动条。
        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 0 工作目录
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // 1 路径
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // 2 按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 3 系统提示词
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 4 提示词正文
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 5 附件
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // 6 附件按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 7 DeepSeek 网页
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // 8 网页按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 9 运行状态
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // 10 网页说明
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 11 深度思考

        var workTitle = SectionLabel("工作目录");
        workTitle.Dock = DockStyle.Fill;
        layout.Controls.Add(workTitle, 0, 0);

        _lblDetailWorkDir = new Label
        {
            Text = "未选择工作目录",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(2, 0, 0, 4)
        };
        layout.Controls.Add(_lblDetailWorkDir, 0, 1);

        var workRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0, 0, 0, 2)
        };
        _btnChooseDir2 = MakeButton("选择文件夹", 92);
        var backBtn = MakeButton("返回对话", 92);
        backBtn.Click += (_, _) => ShowMainPage(0);
        workRow.Controls.AddRange(new Control[] { _btnChooseDir2, backBtn });
        layout.Controls.Add(workRow, 0, 2);

        var promptTitle = SectionLabel("系统提示词");
        promptTitle.Dock = DockStyle.Fill;
        layout.Controls.Add(promptTitle, 0, 3);

        var promptCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 12,
            FillRole = PanelFill.Input,
            Padding = new Padding(12, 8, 8, 8),
            Margin = new Padding(0, 2, 0, 6)
        };
        _txtDetailPrompt = new NoCaretTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        _detailPromptMask = new ScrollMask { Width = SystemInformation.VerticalScrollBarWidth, Target = _txtDetailPrompt };
        _detailPromptScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        promptCard.Controls.Add(_txtDetailPrompt);
        promptCard.Controls.Add(_detailPromptScroll);
        promptCard.Controls.Add(_detailPromptMask);
        _detailPromptMask.BringToFront();
        promptCard.Resize += (_, _) => _detailPromptMask.PlaceOver(_txtDetailPrompt, _detailPromptScroll);
        _txtDetailPrompt.HandleCreated += (_, _) => _detailPromptScroll.Attach(_txtDetailPrompt.Handle);
        _txtDetailPrompt.TextChanged += (_, _) => _detailPromptScroll.RefreshScroll();
        layout.Controls.Add(promptCard, 0, 4);

        var attachTitle = SectionLabel("附件");
        attachTitle.Dock = DockStyle.Fill;
        layout.Controls.Add(attachTitle, 0, 5);

        _btnAttach = MakeButton("添加附件", 88);
        _btnClearAttach = MakeButton("清除附件", 88);
        _btnAttach.Click += (_, _) => AttachFiles();
        _btnClearAttach.Click += (_, _) => ClearAttachments();
        _lblAttach = new Label
        {
            Text = "未选择附件",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(8, 0, 0, 0)
        };
        var attachRow = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 2)
        };
        attachRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        attachRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        attachRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _btnAttach.Dock = DockStyle.Fill;
        _btnAttach.Margin = new Padding(0, 0, 6, 0);
        _btnClearAttach.Dock = DockStyle.Fill;
        _btnClearAttach.Margin = new Padding(0, 0, 6, 0);
        attachRow.Controls.Add(_btnAttach, 0, 0);
        attachRow.Controls.Add(_btnClearAttach, 1, 0);
        attachRow.Controls.Add(_lblAttach, 2, 0);
        layout.Controls.Add(attachRow, 0, 6);

        var webTitle = SectionLabel("DeepSeek 网页");
        webTitle.Dock = DockStyle.Fill;
        layout.Controls.Add(webTitle, 0, 7);

        _btnShowWeb = MakeButton("显示网页会话", 128, "accent");
        _btnSwitch = MakeButton("切换网页会话", 120);
        _btnShowWeb.Dock = DockStyle.Fill;
        _btnShowWeb.Margin = new Padding(0, 0, 6, 0);
        _btnSwitch.Dock = DockStyle.Fill;
        _btnSwitch.Margin = new Padding(0, 0, 6, 0);
        var webRow = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 2)
        };
        webRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        webRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        webRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        webRow.Controls.Add(_btnShowWeb, 0, 0);
        webRow.Controls.Add(_btnSwitch, 1, 0);
        layout.Controls.Add(webRow, 0, 8);

        _lblWebState = new Label
        {
            Text = "状态：未启动",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(2, 0, 0, 2)
        };
        layout.Controls.Add(_lblWebState, 0, 9);

        layout.Controls.Add(new Label
        {
            Text = "网页默认隐藏，只有点击“显示网页会话”才会以独立窗口出现；被关闭后会自动恢复。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor = ThemeManager.Current.SubtleText,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            Margin = new Padding(2, 0, 0, 2)
        }, 0, 10);

        _chkDeepThink = new CheckBox
        {
            Text = "开启 DeepSeek 深度思考（需已连接网页）",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2)
        };
        layout.Controls.Add(_chkDeepThink, 0, 11);

        page.Controls.Add(layout);

        // 工作目录只有一处真实来源（对话页输入框下方），详情页只做只读展示。
        _lblWorkDir.TextChanged += (_, _) => _lblDetailWorkDir.Text = _lblWorkDir.Text;
        _lblDetailWorkDir.Text = _lblWorkDir.Text;
    }

    private void UpdateInputPlaceholder()
    {
        // 获得焦点后隐藏提示文字，让光标正常显示；失去焦点且为空时恢复提示。
        var show = _txtInput.TextLength == 0 && !_txtInput.Focused;
        _lblInputPlaceholder.Text = _busy
            ? "Agent 运行中：此处输入会排队，本轮全部结束后自动发送"
            : "在此输入任务，Ctrl+Enter 发送…";
        _lblInputPlaceholder.Visible = show;
        if (show) _lblInputPlaceholder.BringToFront();
    }
    /// <summary>思考提示条显示时占用一行，隐藏时行高归零。</summary>
    private void ShowThinking(bool on)
    {
        if (_convoLayout.RowStyles.Count > 1)
            _convoLayout.RowStyles[1].Height = on ? 36 : 0;
        if (on)
        {
            ShowHeroTitle(false);
            _thinking.StartThinking(_lblStatus.Text);
        }
        else _thinking.StopThinking();
    }

    /// <summary>空态标题：新建会话时在空白区域正中显示 Build With DeepSeek，有内容后隐藏。</summary>
    private void ShowHeroTitle(bool on)
    {
        _lblHeroTitle.Visible = on;
        _cardReply.Visible = !on;
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
    private static Panel WrapWithScroll(Control input, ScrollMask mask, LineScrollBar bar)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = ThemeManager.Current.Input
        };
        input.Dock = DockStyle.Fill;
        mask.Dock = DockStyle.None;
        bar.Dock = DockStyle.Right;
        host.Controls.Add(input);
        host.Controls.Add(bar);
        host.Controls.Add(mask);
        mask.BringToFront();
        host.Resize += (_, _) => mask.PlaceOver(input, bar);
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

        // 低频开关集中到设置页：主页只保留高频操作。
        flow.Controls.Add(SectionLabel("外观"));
        _chkNight = new CheckBox { Text = "夜间模式（深色 + 蓝色主题，全界面统一）", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        flow.Controls.Add(_chkNight);

        flow.Controls.Add(SectionLabel("会话模式"));
        var modeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        _rbNew = new ThemedRadio { Text = "新对话", Checked = true, AutoSize = false, Width = 92, Height = 30, Margin = new Padding(0, 2, 8, 0) };
        _rbOld = new ThemedRadio { Text = "旧对话（续接上次网页会话）", AutoSize = false, Width = 214, Height = 30, Margin = new Padding(0, 2, 0, 0) };
        modeRow.Controls.AddRange(new Control[] { _rbNew, _rbOld });
        flow.Controls.Add(modeRow);

        flow.Controls.Add(SectionLabel("快捷键"));
        _chkEnterSend = new CheckBox { Text = "Ctrl+Enter 发送（关闭时请用“发送”按钮）", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        flow.Controls.Add(_chkEnterSend);

        flow.Controls.Add(SectionLabel("防封控延时（自动执行开启时生效）"));
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.Add(new Label { Text = "最小秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
        _numDelayMin = new NumberField { Minimum = 0, Maximum = 600, Width = 84, Height = 30, Margin = new Padding(0, 6, 6, 0) };
        delayRow.Controls.Add(_numDelayMin);
        delayRow.Controls.Add(new Label { Text = "最大秒数", AutoSize = true, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(18, 8, 6, 0) });
        _numDelayMax = new NumberField { Minimum = 0, Maximum = 600, Width = 84, Height = 30, Margin = new Padding(0, 6, 0, 0) };
        delayRow.Controls.Add(_numDelayMax);
        flow.Controls.Add(delayRow);

        flow.Controls.Add(SectionLabel("窗口透明度"));
        var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _trkOpacity = new LineSlider { Minimum = 30, Maximum = 100, Width = 300, Height = 28, Margin = new Padding(0, 2, 0, 0) };
        _lblOpacity = new Label { Text = "100%", AutoSize = false, Width = 60, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 2, 0, 0) };
        opacityRow.Controls.Add(_trkOpacity);
        opacityRow.Controls.Add(_lblOpacity);
        flow.Controls.Add(opacityRow);

        flow.Controls.Add(SectionLabel("执行方式"));
        _chkAuto = new CheckBox { Text = "自动执行 PowerShell（关闭时每次弹窗确认）", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        flow.Controls.Add(_chkAuto);
        flow.Controls.Add(new Label
        {
            Text = "PowerShell 始终在后台执行，不显示命令窗口；执行记录显示在 PowerShell 面板。",
            AutoSize = false,
            Width = 720,
            Height = 44,
            ForeColor = ThemeManager.Current.SubtleText
        });

        flow.Controls.Add(SectionLabel("技能注入"));
        flow.Controls.Add(new Label
        {
            Text = "发送用户消息时，按关键词从内置 skill_all.zip 中挑选相关技能正文，追加到该条消息末尾。",
            AutoSize = false,
            Width = 720,
            Height = 44,
            ForeColor = ThemeManager.Current.SubtleText
        });
        var skillRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var openSkills = MakeButton("打开技能目录", 108);
        openSkills.Click += (_, _) => OpenFolder(_tempDir);
        skillRow.Controls.Add(openSkills);
        flow.Controls.Add(skillRow);

        flow.Controls.Add(SectionLabel("快捷操作"));
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var openReleases = MakeButton("打开发布页", 100);
        var reset = MakeButton("重置配置", 92);
        openReleases.Click += (_, _) => UpdateChecker.OpenReleasesPage();
        reset.Click += (_, _) => ResetConfig();
        links.Controls.Add(openReleases);
        links.Controls.Add(reset);
        var unlockCaptcha = MakeButton("解除验证码锁定", 132);
        unlockCaptcha.Click += (_, _) => UnlockCaptchaLock();
        links.Controls.Add(unlockCaptcha);
        flow.Controls.Add(links);

        flow.Controls.Add(SectionLabel("临时目录"));
        var tempDirRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var openTemp = MakeButton("打开目录", 92);
        var cleanTemp = MakeButton("清理临时目录", 132);
        openTemp.Click += (_, _) => OpenFolder(_tempDir);
        cleanTemp.Click += (_, _) => CleanTempDirs();
        tempDirRow.Controls.Add(openTemp);
        tempDirRow.Controls.Add(cleanTemp);
        flow.Controls.Add(tempDirRow);
        flow.Controls.Add(new Label
        {
            Text = "临时目录固定为 %TEMP%\\Deepseek-Desktop-Agent，跨启动复用。清理会删除该前缀下的所有残留目录，当前会话正在使用的目录会保留。",
            AutoSize = false,
            Width = 720,
            Height = 44,
            ForeColor = ThemeManager.Current.SubtleText
        });

        flow.Controls.Add(SectionLabel("关于"));
        flow.Controls.Add(new Label
        {
            Text = "Deepseek-Desktop-Agent · C# 重构版\n" +
                   "DeepSeek 网页版 + CDP 自动化。工具协议与原版保持一致。\n" +
                   "配置目录：" + AppPaths.AppDataDir + "\n" +
                   "临时目录（跨启动复用）：" + _tempDir,
            AutoSize = false,
            Width = 760,
            Height = 96
        });

        page.Controls.Add(flow);

        // 设置页的说明文字原本写死 720/760 宽，窗口一窄就会撑出横向滚动条；
        // 统一按可视宽度收缩，并禁用横向滚动。
        bool fitting = false;
        void FitSettingsWidths()
        {
            if (fitting) return;
            fitting = true;
            try
            {
                var w = Math.Max(240, flow.ClientSize.Width - 24);
                foreach (Control c in flow.Controls)
                    if (c is Label lb && !lb.AutoSize) lb.Width = w;
            }
            finally { fitting = false; }
        }
        flow.ClientSizeChanged += (_, _) => FitSettingsWidths();
        flow.HorizontalScroll.Enabled = false;
        FitSettingsWidths();

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
    /// <summary>设置页“清理临时目录”入口：二次确认后清理 %TEMP% 下所有残留目录，跳过当前会话正在使用的目录。</summary>
    private void CleanTempDirs()
    {
        var answer = MessageBox.Show(
            this,
            "确定要清理临时目录吗？\r\n\r\n" +
            "将删除 %TEMP% 下所有以「" + AppPaths.SessionTempPrefix + "」开头的目录，\r\n" +
            "当前会话正在使用的目录会保留：\r\n" + _tempDir + "\r\n\r\n" +
            "此操作不可恢复。",
            "清理临时目录",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var (removed, failed, bytes) = AppPaths.CleanSessionTempDirs(_tempDir);
        var sizeText = bytes >= 1024L * 1024 ? (bytes / 1024.0 / 1024.0).ToString("F2") + " MB"
                     : bytes >= 1024 ? (bytes / 1024.0).ToString("F1") + " KB"
                     : bytes + " 字节";
        var msg = "清理完成。\r\n删除目录：" + removed + " 个\r\n释放空间：" + sizeText;
        if (failed > 0) msg += "\r\n失败（可能被占用）：" + failed + " 个";
        MessageBox.Show(this, msg, "清理临时目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UnlockCaptchaLock()
    {
        if (_txtInput.Enabled) { MessageBox.Show(this, "当前输入框未被锁定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var res = MessageBox.Show(this, "确定要解除输入锁定吗？\r\n请确保您已在网页中手动完成了验证码。\r\n如果继续被风控检测到自动化行为，账号极大概率会被永久封禁！", "封号风险警告", MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);
        if (res == DialogResult.Yes)
        {
            _txtInput.Enabled = true;
            _btnSend.Enabled = true;
            SetStatus("输入框已解锁，请谨慎操作。");
        }
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
                RefreshSystemPrompt();
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
            SetStatus("已记住上次会话 " + id + "，点击开始继续；要换网页会话请点“切换网页会话”");
        }
        UpdateCounters();
    }
    private void ApplyPalette()
    {
        ThemeManager.Apply(this, _chkNight.Checked ? ThemeManager.Dark : ThemeManager.Light);
        _lblInputHint.ForeColor = ThemeManager.Current.SubtleText;
        _lblHeroTitle.ForeColor = ThemeManager.Current.Text;
        _lblDetailWorkDir.ForeColor = ThemeManager.Current.Text;
        _statusLabel.ForeColor = ThemeManager.Current.SubtleText;
        _counterLabel.ForeColor = ThemeManager.Current.SubtleText;
        _lblWorkDir.ForeColor = ThemeManager.Current.Text;
        _lblStatus.ForeColor = ThemeManager.Current.SubtleText;
        _lstSessions.BackColor = ThemeManager.Current.Surface;
        _lstSessions.ForeColor = ThemeManager.Current.Text;

        // 会话卡片背景与输入框同色，靠圆角与描边区分层次。
        _txtReply.BackColor = ThemeManager.Current.Input;
        _txtReply.ForeColor = ThemeManager.Current.Text;
        _lblInputPlaceholder.ForeColor = ThemeManager.Current.SubtleText;
        // 输入框在 Agent 运行期间始终可用，保持正常底色，避免看起来像被禁用。
        _txtInput.BackColor = ThemeManager.Current.Input;
        _lblInputPlaceholder.BackColor = _txtInput.BackColor;
        _txtDetailPrompt.BackColor = ThemeManager.Current.Input;
        _txtDetailPrompt.ForeColor = ThemeManager.Current.Text;
        _detailPromptMask.BackColor = ThemeManager.Current.Input;
        _detailPromptScroll.BackColor = ThemeManager.Current.Input;
        _detailPromptScroll.Invalidate();
        _cardReply.Invalidate();
        _cardInput.Invalidate();
        _thinking.Invalidate();
        _trkOpacity.Invalidate();
        _replyScroll.BackColor = ThemeManager.Current.Input;
        _replyScroll.Invalidate();
        _replyMask.BackColor = ThemeManager.Current.Input;
        _inputScroll.BackColor = ThemeManager.Current.Input;
        _inputScroll.Invalidate();
        _inputMask.BackColor = ThemeManager.Current.Input;
        _psCodeScroll.BackColor = ThemeManager.Current.Input;
        _psCodeScroll.Invalidate();
        _psCodeMask.BackColor = ThemeManager.Current.Input;
        _psOutScroll.BackColor = ThemeManager.Current.Input;
        _psOutScroll.Invalidate();
        _psOutMask.BackColor = ThemeManager.Current.Input;
        _detailScroll.BackColor = ThemeManager.Current.Input;
        _detailScroll.Invalidate();
        _detailMask.BackColor = ThemeManager.Current.Input;
        _txtSessionInput.BackColor = ThemeManager.Current.Input;
        _txtSessionInput.ForeColor = ThemeManager.Current.Text;
        _lblSessionInputPlaceholder.ForeColor = ThemeManager.Current.SubtleText;
        _lblSessionInputPlaceholder.BackColor = _txtSessionInput.BackColor;
        _sessionInputMask.BackColor = ThemeManager.Current.Input;
        _sessionInputScroll.BackColor = ThemeManager.Current.Input;
        _sessionInputScroll.Invalidate();
        // 输入卡片内部的容器必须跟着填充色走，否则会露出深色方块。
        foreach (var ctl in _inputFill) ctl.BackColor = ThemeManager.Current.Input;
        _menuSessions.BackColor = ThemeManager.Current.Surface;
        _menuSessions.ForeColor = ThemeManager.Current.Text;
        RefreshWebStateLabel();
        UpdateAttachLabel();
        UpdateInputPlaceholder();
        RenderReply();
        // 会话回溯页的 RTF 带显式颜色，换主题后需要重渲染一次。
        if (_sessionDetail != null && !_sessionDetail.IsDisposed) ShowSessionDetail();
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
                // 左栏放技能入口与会话列表，宽度固定在 272–320 之间，右侧主区域占满剩余空间。
                var target = Math.Clamp(_split.Width * 22 / 100, 272, 320);
                if (Math.Abs(_split.SplitterDistance - target) > 2) _split.SplitterDistance = target;
            }
        }
        catch { }

        try { _psSplit.SplitterDistance = 210; } catch { }
        try { _psRight.SplitterDistance = 260; } catch { }
    }
    /// <summary>切换主区域页面：0 对话 / 1 PowerShell / 2 设置 / 3 会话回溯 / 4 详情。</summary>
    private void ShowMainPage(int index)
    {
        if (_pages.Length == 0 || index < 0 || index >= _pages.Length) return;
        SwitchTabPage(_pages, index);
    }

    /// <summary>
    /// 切换主区域页面：先用不透明遮罩盖住内容区，页面切换与布局都在遮罩后完成，
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
        if (pages[index] == _sessionsPage)
        {
            ShowSessionDetail();
        }

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

    // ==================== ③-B 会话树 ====================

    private void BuildSessionsTab(Panel page)
    {
        // 会话回溯页：列表在左栏，这里只显示完整对话内容（只读、可复制，可回溯）。

        page.Padding = new Padding(20, 12, 20, 12);
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

        var head = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

        _lblSessionHead = new Label
        {
            Text = "选择左侧会话查看内容",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            Margin = Padding.Empty
        };
        _sessionDetail = new NoCaretRichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.ForcedVertical,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Margin = Padding.Empty
        };

        var back = MakeButton("返回对话", 92);
        back.Dock = DockStyle.Fill;
        back.Margin = new Padding(4, 5, 0, 5);
        back.Click += (_, _) => ShowMainPage(0);
        var rename = MakeButton("重命名", 74);
        rename.Dock = DockStyle.Fill;
        rename.Margin = new Padding(0, 5, 4, 5);
        rename.Click += (_, _) => RenameSelectedSession();
        var remove = MakeButton("删除", 74);
        remove.Dock = DockStyle.Fill;
        remove.Margin = new Padding(0, 5, 4, 5);
        ((AccentButton)remove).Danger = true;
        remove.Click += (_, _) => DeleteSelectedSession();
        head.Controls.Add(_lblSessionHead, 0, 0);
        head.Controls.Add(rename, 1, 0);
        head.Controls.Add(remove, 2, 0);
        head.Controls.Add(back, 3, 0);

        var detailCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 12,
            FillRole = PanelFill.Input,
            Padding = new Padding(14, 10, 10, 10),
            Margin = Padding.Empty
        };
        _detailMask = new ScrollMask { Width = SystemInformation.VerticalScrollBarWidth, Target = _sessionDetail };
        _detailScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        detailCard.Controls.Add(_sessionDetail);
        detailCard.Controls.Add(_detailScroll);
        detailCard.Controls.Add(_detailMask);
        _detailMask.BringToFront();
        detailCard.Resize += (_, _) => _detailMask.PlaceOver(_sessionDetail, _detailScroll);
        _sessionDetail.HandleCreated += (_, _) => _detailScroll.Attach(_sessionDetail.Handle);
        _sessionDetail.TextChanged += (_, _) => _detailScroll.RefreshScroll();
        _sessionDetail.Resize += (_, _) => _detailMask.PlaceOver(_sessionDetail, _detailScroll);

        // 底部输入卡片：与对话页一致，上方文本区 + 下方工具行（+ / ↑），不再用巨大按钮。
        var inputCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 12,
            FillRole = PanelFill.Input,
            Padding = new Padding(12, 8, 8, 6),
            Margin = new Padding(0, 8, 0, 0)
        };
        var inputGrid = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        inputGrid.BackColor = ThemeManager.Current.Input;
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _txtSessionInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "继续该会话的任务，Ctrl+Enter 发送…",
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Margin = Padding.Empty
        };
        _lblSessionInputPlaceholder = new Label
        {
            Text = "继续该会话的任务，Ctrl+Enter 发送…",
            AutoSize = false,
            Width = 480,
            Height = 22,
            Left = 2,
            Top = 2,
            Visible = false,
            Font = new Font("Microsoft YaHei UI", 8.25F)
        };
        _sessionInputMask = new ScrollMask { Width = SystemInformation.VerticalScrollBarWidth, Target = _txtSessionInput };
        _sessionInputScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        // 文本框与自绘滚动条放在同一个容器里，避免系统原生滚动条露出来。
        var inputHost = new BufferedPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        inputHost.BackColor = ThemeManager.Current.Input;
        inputHost.Controls.Add(_txtSessionInput);
        inputHost.Controls.Add(_lblSessionInputPlaceholder);
        inputHost.Controls.Add(_sessionInputScroll);
        inputHost.Controls.Add(_sessionInputMask);
        _sessionInputMask.BringToFront();
        inputHost.Resize += (_, _) => _sessionInputMask.PlaceOver(_txtSessionInput, _sessionInputScroll);

        var toolbar = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        toolbar.BackColor = ThemeManager.Current.Input;
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        var btnPlus = MakeButton("+", 28);
        btnPlus.Dock = DockStyle.Fill;
        btnPlus.Margin = new Padding(0, 0, 4, 0);
        btnPlus.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        btnPlus.Click += (_, _) => AttachFiles();
        _btnSessionSend = MakeButton("↑", 40, "accent");
        _btnSessionSend.Dock = DockStyle.Fill;
        _btnSessionSend.Margin = Padding.Empty;
        _btnSessionSend.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        toolbar.Controls.Add(btnPlus, 0, 0);
        toolbar.Controls.Add(_btnSessionSend, 2, 0);

        inputGrid.Controls.Add(inputHost, 0, 0);
        inputGrid.Controls.Add(toolbar, 0, 1);
        _inputFill.Add(inputGrid);
        _inputFill.Add(inputHost);
        _inputFill.Add(toolbar);
        _txtSessionInput.HandleCreated += (_, _) => _sessionInputScroll.Attach(_txtSessionInput.Handle);
        _txtSessionInput.TextChanged += (_, _) => _sessionInputScroll.RefreshScroll();
        _txtSessionInput.Enter += (_, _) => { _lblSessionInputPlaceholder.Visible = false; };
        _txtSessionInput.Leave += (_, _) =>
        {
            _lblSessionInputPlaceholder.Visible = _txtSessionInput.TextLength == 0;
            if (_lblSessionInputPlaceholder.Visible) _lblSessionInputPlaceholder.BringToFront();
        };
        _btnSessionSend.Click += (_, _) => TryContinueSession();
        inputCard.Controls.Add(inputGrid);

        layout.Controls.Add(head, 0, 0);
        layout.Controls.Add(detailCard, 0, 1);
        layout.Controls.Add(inputCard, 0, 2);
        page.Controls.Add(layout);
        RefreshSessions();
    }

    private SessionTreeNode? SelectedSessionNode()
    {
        var index = _lstSessions.SelectedIndex;
        return index >= 0 && index < _sessionMap.Count ? _sessionMap[index] : null;
    }

    /// <summary>重命名当前选中的会话；名称留空即恢复“未命名会话”。</summary>
    private void RenameSelectedSession()
    {
        var node = SelectedSessionNode();
        if (node == null)
        {
            SetStatus("请先选择一个会话");
            return;
        }

        using var dlg = new Form
        {
            Text = "重命名会话",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 124),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = ThemeManager.Current.Background,
            ForeColor = ThemeManager.Current.Text,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var tip = new Label
        {
            Text = "会话名称（留空则回到“未命名会话”）",
            Location = new Point(16, 14),
            AutoSize = true,
            ForeColor = ThemeManager.Current.SubtleText
        };
        var box = new TextBox
        {
            Text = node.Title ?? string.Empty,
            Location = new Point(16, 40),
            Width = 388,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeManager.Current.Input,
            ForeColor = ThemeManager.Current.Text,
            PlaceholderText = node.DisplayTitle
        };
        var ok = MakeButton("保存", 88, "accent");
        ok.Location = new Point(232, 80);
        ok.DialogResult = DialogResult.OK;
        var cancel = MakeButton("取消", 88);
        cancel.Location = new Point(332, 80);
        cancel.DialogResult = DialogResult.Cancel;
        dlg.Controls.AddRange(new Control[] { tip, box, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var name = box.Text.Trim();
        if (!_sessions.UpdateTitle(node.SessionId, name.Length == 0 ? null : name))
        {
            SetStatus("重命名失败：会话文件可能已被移除");
            return;
        }
        RefreshSessions();
        ShowSessionDetail();
        SetStatus("已重命名：" + (name.Length == 0 ? "未命名会话" : name));
    }

    /// <summary>删除当前选中会话（不可恢复），同时清理正在使用的会话指针。</summary>
    private void DeleteSelectedSession()
    {
        if (_busy)
        {
            SetStatus("任务运行中，暂时不能删除会话");
            return;
        }
        var node = SelectedSessionNode();
        if (node == null)
        {
            SetStatus("请先选择一个会话");
            return;
        }
        var answer = MessageBox.Show(this,
            "删除会话“" + node.DisplayTitle + "”？\r\n工作目录：" + node.WorkDir +
            "\r\n共 " + node.TurnCount + " 轮，删除后无法恢复。",
            "删除会话", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;

        if (!_sessions.DeleteSession(node.SessionId))
        {
            SetStatus("删除失败：会话文件可能已被占用");
            return;
        }
        if (_sessionId == node.SessionId)
        {
            _sessionId = null;
            _turnId = null;
        }
        RefreshSessions();
        if (SelectedSessionNode() == null)
        {
            // 会话已删光：回到 “Build With DeepSeek” 空态，不在回溯页停着。
            _rbNew.Checked = true;
            _lastReplyRaw = "";
            _txtReply.Clear();
            _txtInput.Clear();
            ClearAttachments();
            RenderReply();
            ShowMainPage(0);
            _txtInput.Focus();
            SetStatus("会话已全部删除，输入消息会自动创建新会话");
        }
        else
        {
            ShowSessionDetail();
            SetStatus("已删除会话：" + node.DisplayTitle);
        }
    }

    /// <summary>重新构建会话树并绑定到列表；筛选为空时展示全部。</summary>
    private void RefreshSessions()
    {
        if (_lstSessions == null || _lstSessions.IsDisposed) return;
        try
        {
            var keep = SelectedSessionNode()?.SessionId;
            var tree = new SessionTreeService(_sessions).Build(_txtSessionFilter.Text);
            var items = new List<string>();
            var map = new List<SessionTreeNode?>();
            var groups = new List<string?>();
            foreach (var group in tree.Groups)
            {
                var folder = Path.GetFileName(group.WorkDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folder)) folder = group.WorkDir;
                items.Add(folder);
                map.Add(null);
                groups.Add($"{folder}    {group.SessionCount} 个会话");
                foreach (var session in group.Sessions)
                {
                    items.Add(session.DisplayTitle);
                    map.Add(session);
                    groups.Add(null);
                }
            }
            if (items.Count == 0)
            {
                items.Add("暂无会话记录");
                map.Add(null);
                groups.Add(null);
            }

            _suppressSessionNav = true;
            _lstSessions.BeginUpdate();
            _lstSessions.Items.Clear();
            _lstSessions.Items.AddRange(items.ToArray());
            _sessionMap.Clear();
            _sessionMap.AddRange(map);
            _sessionGroups.Clear();
            _sessionGroups.AddRange(groups);
            // 刷新后尽量保持原来选中的会话，避免每次切换标签页都要重新点。
            var restore = keep == null ? -1 : map.FindIndex(n => n != null && n.SessionId == keep);
            // 没有可恢复的选中项时默认选中最新的会话，右侧直接显示最近一次对话。
            if (restore < 0) restore = map.FindIndex(n => n != null);
            if (restore >= 0) _lstSessions.SelectedIndex = restore;
            _lstSessions.EndUpdate();
            _suppressSessionNav = false;
        }
        catch (Exception ex)
        {
            SetStatus("读取会话失败：" + ex.Message);
            _suppressSessionNav = false;
        }
    }

    /// <summary>自绘会话列表：分组一行小字，会话行两行（标题 / 时间·轮次），选中时左侧亮条。</summary>
    private void DrawSessionRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _sessionMap.Count || e.Index >= _sessionGroups.Count) return;

        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                     TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var node = _sessionMap[e.Index];
        var header = _sessionGroups[e.Index];

        using (var background = new SolidBrush(theme.Surface)) g.FillRectangle(background, e.Bounds);

        if (header != null)
        {
            TextRenderer.DrawText(g, "▾  " + header, _sessionGroupFont,
                Rectangle.Inflate(e.Bounds, -10, 0), theme.SubtleText, line);
            using var pen = new Pen(theme.Border);
            g.DrawLine(pen, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
            return;
        }

        if (node == null)
        {
            TextRenderer.DrawText(g, "暂无会话记录", _sessionMetaFont,
                Rectangle.Inflate(e.Bounds, -10, 0), theme.SubtleText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        if (selected)
        {
            using var fill = new SolidBrush(theme.Input);
            g.FillRectangle(fill, e.Bounds);
            using var accent = new SolidBrush(theme.Accent);
            g.FillRectangle(accent, new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 7, 3, e.Bounds.Height - 14));
        }

        var textLeft = e.Bounds.Left + 14;
        var textWidth = Math.Max(10, e.Bounds.Width - 22);
        var titleRect = new Rectangle(textLeft, e.Bounds.Top + 3, textWidth, 21);
        TextRenderer.DrawText(g, node.DisplayTitle, _sessionTitleFont, titleRect,
            selected ? theme.Accent : theme.Text, line);

        var metaRect = new Rectangle(textLeft, e.Bounds.Top + 21, textWidth, 17);
        var meta = node.UpdatedText + " · " + node.TurnCount.ToString() + " 轮";
        TextRenderer.DrawText(g, meta, _sessionMetaFont, metaRect, theme.SubtleText, line);
    }

    /// <summary>把选中会话的完整对话渲染到右侧面板，供回溯查看。</summary>
    /// <summary>
    /// 将历史会话记录直接加载到右侧主聊天区（Trae 风格），不再覆盖跳转页面。
    /// </summary>
    private void LoadSessionToChatArea(SessionTreeNode node)
    {
        ShowMainPage(0);
        _sessionId = node.SessionId;
        _turnId = null;

        // 该会话正在输出：直接显示它的实时内容，不再重复加载历史。
        if (_liveSessionId == node.SessionId)
        {
            _lastReplyRaw = _liveTranscript;
            RenderReply();
            ScrollReplyToEnd();
        }
        else
        {
            var record = _sessions.LoadSession(node.SessionId);
            if (record == null || record.Turns.Count == 0)
            {
                _txtInput.Clear();
                _lastReplyRaw = "";
                RenderReply();
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("**历史会话回顾** - " + node.DisplayTitle).AppendLine();
                foreach (var turn in record.Turns)
                {
                    if (!string.IsNullOrWhiteSpace(turn.Prompt))
                    {
                        sb.AppendLine("### 👤 你");
                        sb.AppendLine(turn.Prompt.Trim());
                        sb.AppendLine();
                    }
                    if (!string.IsNullOrWhiteSpace(turn.Reply))
                    {
                        sb.AppendLine("### 🤖 DeepSeek");
                        sb.AppendLine(turn.Reply.Trim());
                        sb.AppendLine();
                    }
                }

                _lastReplyRaw = sb.ToString();
                RenderReply();
                ScrollReplyToEnd();
            }
        }

        // 任务运行中浏览别的会话只做只读查看，不能改运行中任务的工作目录。
        if (!_busy && !string.IsNullOrWhiteSpace(node.WorkDir) && node.WorkDir != _cfg.WorkDir)
        {
            _cfg.WorkDir = node.WorkDir;
            ConfigService.Save(_cfg);
            _lblWorkDir.Text = node.WorkDir;
        }

        _txtInput.Clear();
        ClearAttachments();
        _txtInput.Focus();
        SetStatus("已加载历史会话：" + node.DisplayTitle);
    }
    private void ShowSessionDetail()
    {
        if (_sessionDetail == null || _sessionDetail.IsDisposed) return;

        var node = SelectedSessionNode();
        if (node == null)
        {
            _lblSessionHead.Text = "选择左侧会话查看内容";
            _sessionDetail.Clear();
            return;
        }

        var record = _sessions.LoadSession(node.SessionId);
        if (record == null)
        {
            _lblSessionHead.Text = node.DisplayTitle;
            _sessionDetail.Text = "会话记录文件已不存在。";
            return;
        }

        _lblSessionHead.Text = $"{node.DisplayTitle}    {node.WorkDir}    {record.Turns.Count} 轮 · 更新 {record.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        _sessionDetail.Rtf = MarkdownRenderer.ToRtf(BuildSessionMarkdown(node, record), ThemeManager.Current);
        _sessionDetail.SelectionStart = 0;
        _sessionDetail.ScrollToCaret();
        _detailScroll.RefreshScroll();
        CaretHider.HideCaret(_sessionDetail.Handle);
    }

    private static string BuildSessionMarkdown(SessionTreeNode node, SessionRecord record)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(node.DisplayTitle);
        sb.Append("工作目录：").AppendLine(node.WorkDir);
        sb.Append("创建：").Append(record.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
          .Append("    更新：").Append(record.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
          .Append("    轮次：").AppendLine(record.Turns.Count.ToString());
        sb.AppendLine();

        for (var i = 0; i < record.Turns.Count; i++)
        {
            var turn = record.Turns[i];
            sb.Append("## 第 ").Append(i + 1).Append(" 轮 · ")
              .Append(turn.StartedAt.ToLocalTime().ToString("MM-dd HH:mm"))
              .Append(" · ").AppendLine(DescribeTurn(turn));
            sb.AppendLine("**用户**");
            sb.AppendLine(turn.Prompt);
            sb.AppendLine();
            sb.AppendLine("**助手**");
            sb.AppendLine(string.IsNullOrWhiteSpace(turn.Reply) ? "（无回复）" : turn.Reply);
            if (turn.Files.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("附件：");
                foreach (var file in turn.Files) sb.Append("- `").Append(file).AppendLine("`");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        if (record.Turns.Count == 0) sb.AppendLine("（暂无轮次记录）");
        return sb.ToString();
    }

    private static string DescribeTurn(TurnRecord turn) => turn.Status switch
    {
        TurnStatus.Running => "进行中",
        TurnStatus.Completed => "完成",
        TurnStatus.Stopped => turn.StopMode == StopMode.Safe ? "安全停止" : "急停",
        TurnStatus.Failed => "失败",
        _ => turn.Status.ToString()
    };
}
