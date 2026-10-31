using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed partial class MainForm
{
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

    /// <summary>遮住文本框自带的原生滚动条：不调用 ShowScrollBar，避免滚动信息被重置。</summary>
    private sealed class ScrollMask : Control
    {
        /// <summary>滚轮落在遮罩上时，转发给这个文本框。</summary>
        public Control? Target;

        public ScrollMask()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            Cursor = Cursors.Hand;
            BackColor = ThemeManager.Current.Input;
        }

        /// <summary>覆盖在文本框右侧，正好遮住原生滚动条所在的区域。</summary>
        public void PlaceOver(Control input, Control bar)
        {
            Left = Math.Max(0, bar.Left - Width);
            Top = input.Top;
            Height = input.Height;
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
    /// <summary>自绘数字输入框：去掉原生微调按钮，右侧上下箭头与整体配色一致。</summary>
    private sealed class NumberField : Control
    {
        private const int ArrowWidth = 22;
        private readonly TextBox _edit;
        private int _value;
        private bool _writing;
        private bool _hotUp;
        private bool _hotDown;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum { get; set; } = 600;
        public event EventHandler? ValueChanged;

        public NumberField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            _edit = new TextBox
            {
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            Controls.Add(_edit);
            _edit.TextChanged += (_, _) => ReadText();
            _edit.KeyDown += OnEditKeyDown;
            _edit.Enter += (_, _) => Invalidate();
            _edit.Leave += (_, _) =>
            {
                Commit();
                Invalidate();
            };
            SetValue(0, false);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set => SetValue(value, true);
        }

        /// <summary>把当前输入内容规范化并同步到 Value，失焦或关闭窗口前调用。</summary>
        public void Commit()
        {
            if (!int.TryParse(_edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                parsed = _value;
            SetValue(parsed, true);
        }

        private bool InRange(int value) => value >= Minimum && value <= Maximum;

        private void ReadText()
        {
            if (_writing) return;
            if (!int.TryParse(_edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return;
            if (parsed == _value || !InRange(parsed)) return;
            _value = parsed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetValue(int value, bool raise)
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            var changed = clamped != _value;
            _value = clamped;
            // 先同步输入框文字，再通知外部；否则外部读到的仍是旧文字（会把刚设的值覆盖回去）。
            WriteText();
            if (changed && raise) ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void WriteText()
        {
            var text = _value.ToString(CultureInfo.InvariantCulture);
            if (_edit.Text == text) return;
            _writing = true;
            _edit.Text = text;
            _edit.SelectionStart = _edit.TextLength;
            _writing = false;
        }

        private void Step(int delta) => SetValue(_value + delta, true);

        private void OnEditKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                    Step(1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    Step(-1);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    Commit();
                    _edit.SelectAll();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    WriteText();
                    e.Handled = true;
                    break;
            }
        }

        private Rectangle UpRect => new(Width - ArrowWidth, 2, ArrowWidth - 2, (Height - 4) / 2);
        private Rectangle DownRect => new(Width - ArrowWidth, Height / 2, ArrowWidth - 2, (Height - 4) / 2);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var textHeight = _edit.PreferredHeight;
            _edit.SetBounds(8, Math.Max(1, (Height - textHeight) / 2), Math.Max(10, Width - ArrowWidth - 10), textHeight);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var up = UpRect.Contains(e.Location);
            var down = DownRect.Contains(e.Location);
            Cursor = up || down ? Cursors.Hand : Cursors.IBeam;
            if (up == _hotUp && down == _hotDown) return;
            _hotUp = up;
            _hotDown = down;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_hotUp && !_hotDown) return;
            _hotUp = _hotDown = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (UpRect.Contains(e.Location)) Step(1);
            else if (DownRect.Contains(e.Location)) Step(-1);
            else _edit.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var theme = ThemeManager.Current;
            _edit.BackColor = theme.Input;
            _edit.ForeColor = theme.Text;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UiPaint.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8);
            using (var fill = new SolidBrush(theme.Input))
                e.Graphics.FillPath(fill, path);
            using (var border = new Pen(_edit.Focused ? theme.Accent : theme.Border, 1f))
                e.Graphics.DrawPath(border, path);

            var midX = Width - ArrowWidth / 2f;
            DrawChevron(e.Graphics, midX, Height * 0.32f, true, _hotUp ? theme.Accent : theme.SubtleText);
            DrawChevron(e.Graphics, midX, Height * 0.68f, false, _hotDown ? theme.Accent : theme.SubtleText);
        }

        private static void DrawChevron(Graphics g, float x, float y, bool up, Color color)
        {
            using var pen = new Pen(color, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            // up 画 ∧、down 画 ∨：dy 取正让左右端点下移、顶点上移。
            var dy = up ? 2.5f : -2.5f;
            g.DrawLines(pen, new[]
            {
                new PointF(x - 4f, y + dy),
                new PointF(x, y - dy),
                new PointF(x + 4f, y + dy)
            });
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
            if (delta == 0) return;

            // 到达上/下限后不再发送滚动消息，避免原生控件来回修正导致抖动。
            var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = SifAll };
            if (!GetScrollInfo(hwnd, SbVert, ref info)) return;
            var bottom = info.Max - (int)info.Page + 1;
            var up = delta > 0;
            if (up ? info.Pos <= info.Min : info.Pos >= bottom) return;

            var notches = Math.Max(1, Math.Abs(delta) / 120);
            var command = up ? 0 : 1; // SB_LINEUP / SB_LINEDOWN
            var steps = Math.Min(notches * 3, up ? info.Pos - info.Min : bottom - info.Pos);
            for (var i = 0; i < steps; i++)
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
        public QueuedMessage(string text, List<string> files, Guid? sessionId, string workDir)
        {
            Id = Guid.NewGuid();
            Text = text;
            Files = files;
            SessionId = sessionId;
            WorkDir = workDir ?? string.Empty;
        }

        public Guid Id { get; }

        public string Text { get; set; }
        public List<string> Files { get; }
        /// <summary>该消息归属的本地会话；null 表示发送时要新建会话。</summary>
        public Guid? SessionId { get; }
        public string WorkDir { get; }
    }
}

/// <summary>自绘单选按钮：系统视觉样式会忽略 ForeColor，暗色背景下文字几乎全黑不可见。</summary>
public sealed class ThemedRadio : RadioButton
{
    public ThemedRadio()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        using (var bg = new SolidBrush(Parent?.BackColor ?? p.Background))
            g.FillRectangle(bg, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const int diameter = 14;
        var circle = new Rectangle(2, (Height - diameter) / 2, diameter, diameter);
        using (var pen = new Pen(Checked ? p.Accent : p.Border, 1.6f))
            g.DrawEllipse(pen, circle);
        if (Checked)
        {
            using var dot = new SolidBrush(p.Accent);
            g.FillEllipse(dot, Rectangle.Inflate(circle, -4, -4));
        }

        var textRect = new Rectangle(diameter + 8, 0, Math.Max(0, Width - diameter - 8), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
