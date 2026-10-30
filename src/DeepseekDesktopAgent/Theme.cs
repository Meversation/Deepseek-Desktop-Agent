using System.Drawing;
using System.Drawing.Drawing2D;

namespace DeepseekDesktopAgent;

public sealed class ThemePalette
{
    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color Input { get; init; }
    public Color Text { get; init; }
    public Color SubtleText { get; init; }
    public Color Accent { get; init; }
    public Color AccentText { get; init; }
    public Color Border { get; init; }
}

public static class ThemeManager
{
    public static readonly ThemePalette Light = new()
    {
        Background = Color.FromArgb(244, 246, 249),
        Surface = Color.FromArgb(255, 255, 255),
        Input = Color.FromArgb(255, 255, 255),
        Text = Color.FromArgb(28, 32, 38),
        SubtleText = Color.FromArgb(98, 106, 118),
        Accent = Color.FromArgb(37, 99, 235),
        AccentText = Color.White,
        Border = Color.FromArgb(218, 223, 230)
    };

    public static readonly ThemePalette Dark = new()
    {
        Background = Color.FromArgb(17, 22, 30),
        Surface = Color.FromArgb(26, 32, 43),
        Input = Color.FromArgb(32, 40, 53),
        Text = Color.FromArgb(230, 235, 242),
        SubtleText = Color.FromArgb(150, 160, 175),
        Accent = Color.FromArgb(96, 165, 250),
        AccentText = Color.FromArgb(12, 20, 32),
        Border = Color.FromArgb(49, 60, 76)
    };

    public static ThemePalette Current { get; private set; } = Light;

    public static void Apply(Control root, ThemePalette palette)
    {
        Current = palette;
        root.SuspendLayout();
        try { ApplyRecursive(root, palette); }
        finally { root.ResumeLayout(true); }
        root.Invalidate(true);
    }

    private static void ApplyRecursive(Control control, ThemePalette p)
    {
        if (control.Tag is string tag && tag.Contains("no-theme", StringComparison.Ordinal))
            return;

        switch (control)
        {
            case Form form:
                form.BackColor = p.Background;
                form.ForeColor = p.Text;
                break;
            case SplitContainer split:
                split.BackColor = p.Border;
                split.Panel1.BackColor = p.Background;
                split.Panel2.BackColor = p.Background;
                break;
            case TextBoxBase text:
                text.BackColor = p.Input;
                text.ForeColor = p.Text;
                break;
            case ListBox list:
                list.BackColor = p.Input;
                list.ForeColor = p.Text;
                break;
            case ListView view:
                view.BackColor = p.Input;
                view.ForeColor = p.Text;
                break;
            case ComboBox combo:
                combo.BackColor = p.Input;
                combo.ForeColor = p.Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown number:
                number.BackColor = p.Input;
                number.ForeColor = p.Text;
                break;
            case Button button:
                if (button.FlatStyle != FlatStyle.Flat) button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = button.Tag is string t && t.Contains("accent", StringComparison.Ordinal) ? p.Accent : p.Surface;
                button.ForeColor = button.Tag is string t2 && t2.Contains("accent", StringComparison.Ordinal) ? p.AccentText : p.Text;
                button.UseVisualStyleBackColor = false;
                break;
            case CheckBox check:
                check.BackColor = p.Background;
                check.ForeColor = p.Text;
                break;
            case RadioButton radio:
                // 系统视觉样式在浅色 Windows 主题下会忽略 ForeColor 画成黑字，暗色下几乎不可见。
                radio.FlatStyle = FlatStyle.Flat;
                radio.UseVisualStyleBackColor = false;
                radio.BackColor = p.Surface;
                radio.ForeColor = p.Text;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = label.Tag is string lt && lt.Contains("subtle", StringComparison.Ordinal)
                    ? p.SubtleText
                    : p.Text;
                break;
            case GroupBox group:
                group.BackColor = p.Surface;
                group.ForeColor = p.Text;
                break;
            case TableLayoutPanel table:
                table.BackColor = table.Tag is string tt && tt.Contains("card", StringComparison.Ordinal) ? p.Surface : p.Background;
                break;
            case Panel panel:
                panel.BackColor = panel.Tag is string pt && pt.Contains("card", StringComparison.Ordinal) ? p.Surface : p.Background;
                break;
            case TrackBar bar:
                bar.BackColor = p.Surface;
                break;
            case StatusStrip strip:
                strip.BackColor = p.Surface;
                strip.ForeColor = p.SubtleText;
                foreach (ToolStripItem item in strip.Items) item.ForeColor = p.SubtleText;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyRecursive(child, p);
    }
}

public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }
}

public class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }
}

/// <summary>自绘分段标签栏：完全由主题配色绘制，避免原生 TabControl 在暗色下露出系统浅色标签带。</summary>
public class SegmentedTabs : Control
{
    private const int TabWidth = 96;
    private const int Gap = 6;
    private readonly List<string> _titles = new();
    private int _selected;
    private int _hover = -1;

    public int SelectedIndex => _selected;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedTabs()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        Height = 44;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.5F);
    }

    public void AddTab(string title)
    {
        _titles.Add(title);
        Invalidate();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _titles.Count || index == _selected) return;
        _selected = index;
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle PillAt(int index) =>
        new(6 + index * (TabWidth + Gap), 7, TabWidth, Height - 14);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Background);

        for (var i = 0; i < _titles.Count; i++)
        {
            var pill = PillAt(i);
            if (i == _selected)
            {
                using var path = UiPaint.Round(pill, 9);
                using (var brush = new SolidBrush(p.Surface)) g.FillPath(brush, path);
                using var pen = new Pen(UiPaint.Blend(p.Border, p.Accent, 0.5));
                g.DrawPath(pen, path);
            }
            else if (i == _hover)
            {
                using var path = UiPaint.Round(pill, 9);
                using var brush = new SolidBrush(UiPaint.Blend(p.Background, p.Surface, 0.6));
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(g, _titles[i], Font, pill,
                i == _selected ? p.Text : p.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        using var divider = new Pen(p.Border);
        g.DrawLine(divider, 0, Height - 1, Width, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit >= 0) Select(hit);
        base.OnMouseDown(e);
    }

    private int HitTest(Point pt)
    {
        for (var i = 0; i < _titles.Count; i++)
            if (PillAt(i).Contains(pt)) return i;
        return -1;
    }
}

