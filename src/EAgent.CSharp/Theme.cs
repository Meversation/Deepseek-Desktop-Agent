using System.Drawing;

namespace EAgent;

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
            case TabPage page:
                page.BackColor = p.Surface;
                page.ForeColor = p.Text;
                break;
            case TabControl tabs:
                tabs.BackColor = p.Surface;
                tabs.ForeColor = p.Text;
                break;
            case SplitContainer split:
                split.BackColor = p.Border;
                split.Panel1.BackColor = p.Background;
                split.Panel2.BackColor = p.Background;
                break;
            case TextBoxBase text:
                text.BackColor = p.Input;
                text.ForeColor = p.Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox list:
                list.BackColor = p.Input;
                list.ForeColor = p.Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListView view:
                view.BackColor = p.Input;
                view.ForeColor = p.Text;
                view.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.BackColor = p.Input;
                combo.ForeColor = p.Text;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown number:
                number.BackColor = p.Input;
                number.ForeColor = p.Text;
                number.BorderStyle = BorderStyle.FixedSingle;
                break;
            case Button button:
                if (button.FlatStyle != FlatStyle.Flat) button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = button.Tag is string t && t.Contains("accent", StringComparison.Ordinal) ? p.Accent : p.Surface;
                button.ForeColor = button.Tag is string t2 && t2.Contains("accent", StringComparison.Ordinal) ? p.AccentText : p.Text;
                button.UseVisualStyleBackColor = false;
                break;
            case CheckBox check:
                check.BackColor = p.Surface;
                check.ForeColor = p.Text;
                break;
            case RadioButton radio:
                radio.BackColor = p.Surface;
                radio.ForeColor = p.Text;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = p.Text;
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

public class BufferedTabControl : TabControl
{
    public BufferedTabControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }
}
