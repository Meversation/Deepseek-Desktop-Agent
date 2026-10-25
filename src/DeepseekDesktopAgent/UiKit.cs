using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace DeepseekDesktopAgent;

/// <summary>圆角面板的填充来源，取值随主题实时变化。</summary>
public enum PanelFill
{
    Surface,
    Input,
    Background
}

/// <summary>圆角、混色等自定义绘制共用的基础方法。</summary>
public static class UiPaint
{
    /// <summary>把原生输入控件包进圆角容器，得到统一的圆角输入框与聚焦描边。</summary>
    public static RoundedPanel WrapField(Control input, int padX = 8, int padY = 4, int radius = 9)
    {
        var panel = new RoundedPanel
        {
            Radius = radius,
            FillRole = PanelFill.Input,
            Padding = new Padding(padX, padY, padX, padY),
            Margin = input.Margin
        };

        // 容器必须继承输入控件原来的停靠方式，否则放进 SplitContainer / 表格后会缩成一小块。
        if (input.Dock == DockStyle.None)
        {
            panel.Width = input.Width + padX * 2;
            panel.Height = input.Height + padY * 2;
            panel.Anchor = input.Anchor;
        }
        else
        {
            panel.Dock = input.Dock;
        }

        switch (input)
        {
            case TextBoxBase text: text.BorderStyle = BorderStyle.None; break;
            case ListBox list: list.BorderStyle = BorderStyle.None; break;
            case NumericUpDown num: num.BorderStyle = BorderStyle.None; break;
        }

        input.BackColor = ThemeManager.Current.Input;
        input.Dock = DockStyle.Fill;
        input.Margin = Padding.Empty;
        input.Enter += (_, _) => { panel.Highlight = true; panel.Invalidate(); };
        input.Leave += (_, _) => { panel.Highlight = false; panel.Invalidate(); };
        panel.Controls.Add(input);
        return panel;
    }

    public static GraphicsPath Round(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || rect.Width <= 2 || rect.Height <= 2)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = Math.Min(Math.Min(radius * 2, rect.Width), rect.Height);
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }
}

/// <summary>圆角卡片面板：填充与描边由主题实时计算，避免方角破坏整体视觉。</summary>
public class RoundedPanel : Panel
{
    [System.ComponentModel.DefaultValue(12)]
    public int Radius { get; set; } = 12;

    [System.ComponentModel.DefaultValue(true)]
    public bool DrawBorder { get; set; } = true;

    [System.ComponentModel.DefaultValue(PanelFill.Surface)]
    public PanelFill FillRole { get; set; } = PanelFill.Surface;

    [System.ComponentModel.DefaultValue(false)]
    public bool Highlight { get; set; }

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 完全自绘，避免默认背景在圆角处留下方角。
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Background);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiPaint.Round(rect, Radius);
        var fill = FillRole switch
        {
            PanelFill.Input => p.Input,
            PanelFill.Background => p.Background,
            _ => p.Surface
        };
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (DrawBorder)
        {
            using var pen = new Pen(Highlight ? p.Accent : p.Border);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>自绘圆角按钮：悬停、按下、禁用三态都有明确反馈。</summary>
public class AccentButton : Button
{
    private bool _hover;
    private bool _press;
    [System.ComponentModel.DefaultValue(false)]
    public bool Accent { get; set; }

    /// <summary>急停等危险操作使用的红色样式。</summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool Danger { get; set; }

    [System.ComponentModel.DefaultValue(8)]
    public int Radius { get; set; } = 8;

    public AccentButton()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _press = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _press = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _press = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Background);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiPaint.Round(rect, Radius);

        Color fill, border, fore;
        if (!Enabled)
        {
            fill = UiPaint.Blend(p.Surface, p.Background, 0.45);
            border = p.Border;
            fore = p.SubtleText;
        }
        else if (Danger)
        {
            var red = Color.FromArgb(214, 48, 48);
            fill = _press
                ? UiPaint.Blend(red, Color.Black, 0.18)
                : _hover ? UiPaint.Blend(red, Color.White, 0.14) : red;
            border = fill;
            fore = Color.White;
        }
        else if (Accent)
        {
            fill = _press
                ? UiPaint.Blend(p.Accent, Color.Black, 0.18)
                : _hover ? UiPaint.Blend(p.Accent, Color.White, 0.14) : p.Accent;
            border = fill;
            fore = p.AccentText;
        }
        else
        {
            fill = _press
                ? UiPaint.Blend(p.Surface, Color.Black, 0.12)
                : _hover ? UiPaint.Blend(p.Surface, p.Accent, 0.12) : p.Surface;
            border = _hover ? p.Accent : p.Border;
            fore = p.Text;
        }

        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(border)) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, rect, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>“模型正在思考”提示条：跳动的指示点、实时阶段文案、耗时与流光扫过。</summary>
public class ThinkingIndicator : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = new();
    private float _phase;
    private string _stage = "";
    private bool _active;
    private Font? _boldFont;

    public ThinkingIndicator()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        Height = 34;
        Visible = false;
        _timer.Tick += (_, _) =>
        {
            _phase += 0.09f;
            Invalidate();
        };
    }

    public bool IsThinking => _active;

    public void StartThinking(string stage)
    {
        _stage = stage;
        if (!_active)
        {
            _active = true;
            _clock.Restart();
            _timer.Start();
        }
        Visible = true;
        Invalidate();
    }

    public void SetStage(string stage)
    {
        _stage = stage;
        Invalidate();
    }

    public void StopThinking()
    {
        _active = false;
        _timer.Stop();
        _clock.Stop();
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Background);
        if (!_active) return;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiPaint.Round(rect, 9);
        using (var brush = new SolidBrush(UiPaint.Blend(p.Surface, p.Accent, 0.10)))
            g.FillPath(brush, path);
        using (var pen = new Pen(UiPaint.Blend(p.Border, p.Accent, 0.35)))
            g.DrawPath(pen, path);

        // 流光：一条柔和的强调色带从左向右循环扫过。
        var state = g.Save();
        g.SetClip(path);
        var bandWidth = Math.Max(160, Width / 4);
        var x = (float)((_clock.Elapsed.TotalSeconds * 220) % (Width + bandWidth)) - bandWidth;
        using (var shimmer = new LinearGradientBrush(
            new RectangleF(x, 0, bandWidth, Height),
            Color.FromArgb(0, p.Accent),
            Color.FromArgb(0, p.Accent),
            LinearGradientMode.Horizontal))
        {
            var blend = new ColorBlend(3)
            {
                Colors = new[] { Color.FromArgb(0, p.Accent), Color.FromArgb(28, p.Accent), Color.FromArgb(0, p.Accent) },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            shimmer.InterpolationColors = blend;
            g.FillRectangle(shimmer, x, 0, bandWidth, Height);
        }
        g.Restore(state);

        // 三个跳动指示点
        for (var i = 0; i < 3; i++)
        {
            var bounce = (float)Math.Sin(_phase * 2 + i * 0.85) * 2.4f;
            var alpha = (int)(150 + 105 * (0.5 + 0.5 * Math.Sin(_phase * 2 + i * 0.85)));
            using var dot = new SolidBrush(Color.FromArgb(alpha, p.Accent));
            g.FillEllipse(dot, 15 + i * 10, Height / 2f - 3 + bounce, 6, 6);
        }

        var text = "DeepSeek 正在思考";
        var bold = _boldFont ??= new Font(Font, FontStyle.Bold);
        var titleWidth = TextRenderer.MeasureText(text, bold).Width;
        TextRenderer.DrawText(g, text, bold, new Rectangle(52, 0, titleWidth + 4, Height), p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        if (!string.IsNullOrWhiteSpace(_stage))
        {
            var stageRect = new Rectangle(52 + titleWidth + 10, 0, Math.Max(0, Width - 160 - titleWidth), Height);
            TextRenderer.DrawText(g, "· " + _stage, Font, stageRect, p.SubtleText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        var elapsed = _clock.Elapsed.TotalSeconds;
        var timeText = elapsed < 60 ? $"{elapsed:0}s" : $"{(int)elapsed / 60}m{(int)elapsed % 60}s";
        TextRenderer.DrawText(g, timeText, Font, new Rectangle(Width - 60, 0, 46, Height), p.SubtleText,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _boldFont?.Dispose();
        _boldFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _boldFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
