using System.Drawing;

namespace DeepseekDesktopAgent;

public sealed class SafetyForm : Form
{
    public bool Allowed { get; private set; }

    public SafetyForm(string code)
    {
        Text = "Deepseek-Desktop-Agent 安全确认";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(820, 560);
        BackColor = ThemeManager.Current.Surface;

        var title = new Label
        {
            Text = "AI 请求执行以下 PowerShell 命令，是否允许？",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 38,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            Padding = new Padding(14, 0, 14, 0)
        };

        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            Font = new Font("Consolas", 10F),
            Text = code,
            BorderStyle = BorderStyle.FixedSingle
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10)
        };

        var allow = new Button
        {
            Text = "允许执行",
            Width = 110,
            Height = 34,
            Tag = "accent",
            DialogResult = DialogResult.Yes
        };
        var deny = new Button
        {
            Text = "拒绝",
            Width = 96,
            Height = 34,
            DialogResult = DialogResult.No
        };
        allow.Click += (_, _) => { Allowed = true; };
        buttons.Controls.Add(allow);
        buttons.Controls.Add(deny);
        AcceptButton = allow;
        CancelButton = deny;

        Controls.Add(box);
        Controls.Add(buttons);
        Controls.Add(title);

        ThemeManager.Apply(this, ThemeManager.Current);
    }

    public static bool Confirm(string code)
    {
        using var form = new SafetyForm(code);
        return form.ShowDialog() == DialogResult.Yes && form.Allowed;
    }
}

public sealed class SessionPickerForm : Form
{
    private readonly CdpClient _cdp;
    private readonly ListView _list;
    private readonly TextBox _manualUrl;
    private readonly Label _hint;
    private readonly Button _refresh;

    public string SelectedUrl { get; private set; } = "";
    public bool CreateNew { get; private set; }

    public SessionPickerForm(CdpClient cdp, string currentUrl)
    {
        _cdp = cdp;
        Text = "选择工作会话";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(780, 540);
        MinimumSize = new Size(680, 460);

        var header = new Label
        {
            Text = "选择要继续的 DeepSeek 会话。选择后会直接进入该会话，不再新开对话。",
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 14, 0)
        };

        _hint = new Label
        {
            Text = currentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "当前页面: " + currentUrl : "",
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 14, 0),
            ForeColor = ThemeManager.Current.SubtleText
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        _list.Columns.Add("会话标题", 520);
        _list.Columns.Add("会话地址", 220);
        _list.DoubleClick += (_, _) => ConfirmSelection();

        var manualPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            ColumnCount = 2,
            Padding = new Padding(12, 8, 12, 6)
        };
        manualPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        manualPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        manualPanel.Controls.Add(new Label
        {
            Text = "或手动输入会话 URL",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, 0);
        _manualUrl = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "https://chat.deepseek.com/a/chat/s/..." };
        manualPanel.Controls.Add(_manualUrl, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8)
        };

        var ok = new Button { Text = "继续此会话", Width = 112, Height = 34, Tag = "accent" };
        var cancel = new Button { Text = "取消", Width = 88, Height = 34, DialogResult = DialogResult.Cancel };
        var create = new Button { Text = "新建对话", Width = 100, Height = 34 };
        _refresh = new Button { Text = "刷新列表", Width = 100, Height = 34 };

        ok.Click += (_, _) => ConfirmSelection();
        create.Click += (_, _) =>
        {
            CreateNew = true;
            SelectedUrl = "https://chat.deepseek.com";
            DialogResult = DialogResult.OK;
            Close();
        };
        _refresh.Click += async (_, _) => await LoadConversationsAsync();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(create);
        buttons.Controls.Add(_refresh);
        buttons.Controls.Add(cancel);
        CancelButton = cancel;

        Controls.Add(_list);
        Controls.Add(manualPanel);
        Controls.Add(_hint);
        Controls.Add(header);
        Controls.Add(buttons);

        ThemeManager.Apply(this, ThemeManager.Current);
        Shown += async (_, _) => await LoadConversationsAsync();
    }

    private async Task LoadConversationsAsync()
    {
        if (IsDisposed) return;
        _refresh.Enabled = false;
        _hint.Text = "正在读取会话列表...";
        _list.Items.Clear();
        try
        {
            var items = await _cdp.GetConversationsAsync();
            if (IsDisposed) return;
            if (items.Count == 0)
            {
                _hint.Text = "未找到会话。请先确认 DeepSeek 已登录并打开过对话，或手动输入会话 URL。";
                return;
            }
            foreach (var item in items)
            {
                var row = new ListViewItem(item.Title);
                row.SubItems.Add(item.Url);
                row.Tag = item.Url;
                _list.Items.Add(row);
            }
            _hint.Text = $"共 {items.Count} 个会话。";
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _hint.Text = "读取失败: " + ex.Message;
        }
        finally
        {
            if (!IsDisposed) _refresh.Enabled = true;
        }
    }

    private void ConfirmSelection()
    {
        var manual = _manualUrl.Text.Trim();
        if (manual.Length > 0)
        {
            if (!manual.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _hint.Text = "URL 必须以 http 开头。";
                return;
            }
            SelectedUrl = manual;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_list.SelectedItems.Count == 0)
        {
            _hint.Text = "请先选择一个会话，或点击“新建对话”。";
            return;
        }

        SelectedUrl = _list.SelectedItems[0].Tag as string ?? "";
        if (SelectedUrl.Length == 0)
        {
            _hint.Text = "该会话没有有效地址。";
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

public sealed class MouseMarkerForm : Form
{
    public MouseMarkerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Yellow;
        Opacity = 150.0 / 255.0;
        Size = new Size(20, 20);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowAtCursor()
    {
        var scale = Native.GetPrimaryScale();
        var size = Math.Max(12, (int)Math.Round(20 * scale));
        var (x, y) = Native.GetCursorPhysical();
        Bounds = new Rectangle(x - size / 2, y - size / 2, size, size);
        Show();
        BringToFront();
    }
}
