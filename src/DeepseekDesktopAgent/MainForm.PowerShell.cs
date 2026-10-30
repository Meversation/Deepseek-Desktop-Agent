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
        _psCodeMask = new ScrollMask
        {
            Dock = DockStyle.Right,
            Width = SystemInformation.VerticalScrollBarWidth,
            Margin = Padding.Empty,
            Target = _txtPsCode
        };
        _psOutMask = new ScrollMask
        {
            Dock = DockStyle.Right,
            Width = SystemInformation.VerticalScrollBarWidth,
            Margin = Padding.Empty,
            Target = _txtPsOut
        };
        _psCodeScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _psOutScroll = new LineScrollBar { Dock = DockStyle.Right, Width = 12, Margin = Padding.Empty };
        _txtPsCode.HandleCreated += (_, _) => _psCodeScroll.Attach(_txtPsCode.Handle);
        _txtPsOut.HandleCreated += (_, _) => _psOutScroll.Attach(_txtPsOut.Handle);
        _txtPsOut.TextChanged += (_, _) => _psOutScroll.RefreshScroll();
        _psRight.Panel1.Padding = new Padding(0, 0, 0, 4);
        _psRight.Panel2.Padding = new Padding(0, 4, 0, 0);
        _psRight.Panel1.Controls.Add(FieldCard("命令 · 工具实际执行的脚本", WrapWithScroll(_txtPsCode, _psCodeMask, _psCodeScroll)));
        _psRight.Panel2.Controls.Add(FieldCard("输出 · stdout / stderr 回传", WrapWithScroll(_txtPsOut, _psOutMask, _psOutScroll)));
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
}
