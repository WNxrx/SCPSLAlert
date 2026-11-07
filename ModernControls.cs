using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal static class ModernDraw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Rectangle r, int radius, Color c)
        {
            if (c.A == 0) return;
            using (SolidBrush b = new SolidBrush(c))
            using (GraphicsPath p = Round(r, radius))
                g.FillPath(b, p);
        }

        public static void Outline(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            if (c.A == 0) return;
            using (Pen p = new Pen(c, width))
            using (GraphicsPath path = Round(r, radius))
                g.DrawPath(p, path);
        }

        public static void Chevron(Graphics g, Point center, float degrees, int size, Color color)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(degrees);
            using (Pen p = new Pen(color, 1.7f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new PointF[]
                {
                    new PointF(-size * 0.5f, -size * 0.22f),
                    new PointF(0f, size * 0.3f),
                    new PointF(size * 0.5f, -size * 0.22f)
                });
            }
            g.Restore(state);
        }

        public static Color TextOn(Color background)
        {
            return ModernTheme.Luma(background) < 0.58 ? Color.White : ModernTheme.P.Text;
        }
    }

    internal sealed class AnimTimer : Timer
    {
        public AnimTimer()
        {
            Interval = ModernTheme.FrameInterval;
        }

        public new void Start()
        {
            Interval = ModernTheme.FrameInterval;
            base.Start();
        }

        public new void Stop()
        {
            base.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Stop();
            base.Dispose(disposing);
        }
    }

    internal sealed class CapsuleCheckBox : CheckBox
    {
        private readonly AnimTimer _anim;
        private float _pos;
        private float _hover;
        private float _press;
        private float _animFrom;
        private int _animStart;

        public CapsuleCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Font = UiKit.Font;
            Cursor = Cursors.Hand;
            _anim = new AnimTimer();
            _anim.Tick += delegate { Step(); };
            ModernTheme.Changed += OnThemeChanged;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text == null ? "" : Text, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            return new Size(52 + text.Width + 2, Math.Max(30, text.Height + 8));
        }

        private void OnThemeChanged()
        {
            ApplySurface();
            Invalidate();
        }

        private void ApplySurface()
        {
            Color back = ModernTheme.ContentBack(this);
            if (BackColor != back) BackColor = back;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            ApplySurface();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) ApplySurface();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            if (!UiAnimationsEnabled)
            {
                _pos = Checked ? 1f : 0f;
                Invalidate();
                return;
            }
            _animFrom = _pos;
            _animStart = Environment.TickCount;
            _anim.Start();
            Invalidate();
        }

        private bool UiAnimationsEnabled
        {
            get { return ModernTheme.AnimationsEnabled; }
        }

        private void Step()
        {
            float target = Checked ? 1f : 0f;
            float t = (Environment.TickCount - _animStart) / 170f;
            if (t >= 1f)
            {
                _pos = target;
                _anim.Stop();
            }
            else
            {
                _pos = _animFrom + (target - _animFrom) * (float)UiAnim.EaseOutCubic(t);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            UiColors p = ModernTheme.For(this);

            int trackH = Math.Min(22, Math.Max(16, Height - 6));
            int trackW = 42;
            int radius = trackH / 2;
            Rectangle track = new Rectangle(0, (Height - trackH) / 2, trackW, trackH);

            Color on = p.Accent;
            Color off = p.TrackOff;
            Color fill = ModernTheme.Mix(off, on, _pos);
            if (_hover > 0f) fill = ModernTheme.Mix(fill, p.Accent, 0.10f * _hover);
            if (_press > 0f) fill = ModernTheme.Mix(fill, Color.Black, 0.10f * _press);
            if (!Enabled) fill = ModernTheme.Mix(fill, p.CardBg, 0.55f);

            if (_hover > 0f || Focused)
            {
                using (SolidBrush glow = new SolidBrush(ModernTheme.Alpha(p.Accent, (int)(26 * _hover))))
                using (GraphicsPath gp = ModernDraw.Round(new Rectangle(track.X - 3, track.Y - 3, track.Width + 6, track.Height + 6), radius + 3))
                    g.FillPath(glow, gp);
            }

            using (SolidBrush b = new SolidBrush(fill))
            using (GraphicsPath path = ModernDraw.Round(track, radius))
                g.FillPath(b, path);

            int knobD = trackH - 4;
            int left = 2;
            int span = trackW - knobD - 4;
            int knobX = track.X + left + (int)Math.Round(span * _pos);
            int knobY = track.Y + 2;
            int grow = (int)Math.Round(1.5 * _hover + 1.0 * _press);
            Rectangle knob = new Rectangle(knobX - grow, knobY - grow, knobD + grow * 2, knobD + grow * 2);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
                g.FillEllipse(shadow, new Rectangle(knob.X + 1, knob.Y + 1, knob.Width, knob.Height));
            using (SolidBrush b = new SolidBrush(Enabled ? p.Knob : ModernTheme.Mix(p.Knob, p.TrackOff, 0.4f)))
                g.FillEllipse(b, knob);

            if (Focused && ShowFocusCues)
            {
                ModernDraw.Outline(g, new Rectangle(0, 0, Width - 1, Height - 1), 8,
                    ModernTheme.Alpha(p.Accent, 140), 1f);
            }

            if (!string.IsNullOrEmpty(Text))
            {
                Color fore = Enabled ? p.Text : p.SubText;
                Rectangle textRect = new Rectangle(trackW + 10, 0, Math.Max(0, Width - trackW - 10), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = 1f;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = 0f;
            _press = 0f;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _press = 1f;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _press = 0f;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Dispose();
                ModernTheme.Changed -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernButton : Button
    {
        private readonly AnimTimer _anim;
        private float _hover;
        private float _press;
        private float _hoverFrom;
        private float _pressFrom;
        private int _hoverStart;
        private int _pressStart;
        private float _hoverTarget;
        private float _pressTarget;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            ForeColor = Color.Black;
            Font = UiKit.Font;
            Cursor = Cursors.Hand;
            Height = 30;
            _anim = new AnimTimer();
            _anim.Tick += delegate { Step(); };
            ModernTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            Invalidate();
        }

        private void Step()
        {
            float ht = (Environment.TickCount - _hoverStart) / 120f;
            if (ht >= 1f) _hover = _hoverTarget;
            else _hover = _hoverFrom + (_hoverTarget - _hoverFrom) * (float)UiAnim.EaseOutCubic(ht);

            float pt = (Environment.TickCount - _pressStart) / 90f;
            if (pt >= 1f) _press = _pressTarget;
            else _press = _pressFrom + (_pressTarget - _pressFrom) * (float)UiAnim.EaseOutCubic(pt);

            if (ht >= 1f && pt >= 1f) _anim.Stop();
            Invalidate();
        }

        private void AnimateTo(float hoverTarget, float pressTarget)
        {
            if (!ModernTheme.AnimationsEnabled)
            {
                _hover = hoverTarget;
                _press = pressTarget;
                _hoverTarget = hoverTarget;
                _pressTarget = pressTarget;
                Invalidate();
                return;
            }
            _hoverFrom = _hover;
            _pressFrom = _press;
            _hoverTarget = hoverTarget;
            _pressTarget = pressTarget;
            _hoverStart = Environment.TickCount;
            _pressStart = Environment.TickCount;
            _anim.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            UiColors p = ModernTheme.For(this);
            g.Clear(ModernTheme.ContentBack(this));

            Color baseColor = BackColor;
            if (baseColor.A == 0 || baseColor == Color.Transparent) baseColor = p.CardBg;
            if (ModernTheme.IsNear(baseColor, UiKit.Accent, 8)) baseColor = p.Accent;
            else if (ModernTheme.Luma(baseColor) > 0.86) baseColor = p.CardBg;

            Color fill = baseColor;
            Color accent = p.Accent;
            if (ModernTheme.Luma(baseColor) < 0.58)
            {
                fill = ModernTheme.Mix(fill, Color.White, 0.12f * _hover);
                fill = ModernTheme.Mix(fill, Color.Black, 0.12f * _press);
            }
            else
            {
                fill = ModernTheme.Mix(fill, accent, 0.10f * _hover);
                fill = ModernTheme.Mix(fill, accent, 0.20f * _press);
            }
            if (!Enabled) fill = ModernTheme.Mix(fill, p.CardBg, 0.6f);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(6, Math.Min(9, Height / 3));
            using (SolidBrush b = new SolidBrush(fill))
            using (GraphicsPath path = ModernDraw.Round(r, radius))
                g.FillPath(b, path);

            bool lightFill = ModernTheme.Luma(fill) > 0.62;
            Color border = Focused ? p.Accent : (lightFill ? p.InputBorder : ModernTheme.Alpha(Color.White, 30));
            using (Pen pen = new Pen(border, Focused ? 1.6f : 1f))
            using (GraphicsPath path = ModernDraw.Round(r, radius))
                g.DrawPath(pen, path);

            Color fore = ForeColor;
            if (fore == Color.Black || fore.A == 0 || ModernTheme.Luma(fore) < 0.2)
                fore = lightFill ? p.Text : Color.White;
            if (!Enabled) fore = p.SubText;

            Rectangle textRect = new Rectangle(6, (int)Math.Round(_press * 1.2f), Width - 12, Height - 1);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            AnimateTo(1f, _pressTarget);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            AnimateTo(0f, 0f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) AnimateTo(_hoverTarget, 1f);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            AnimateTo(_hoverTarget, 0f);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Dispose();
                ModernTheme.Changed -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernExpander : GroupBox
    {
        public const int HeaderH = 46;
        public const int CollapsedH = 46;

        private readonly AnimTimer _anim;
        private bool _expanded = true;
        private bool _animating;
        private float _phase = 1f;
        private float _chev = 1f;
        private float _animFrom = 1f;
        private int _animStart;
        private int _fullHeight = 120;
        private bool _hoverHeader;
        private bool _pressHeader;
        private readonly Font _headerFont;

        public ModernExpander()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = ModernTheme.P.PageBg;
            Font = UiKit.Font;
            _headerFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
            Padding = new Padding(16, HeaderH + 8, 16, 12);
            _anim = new AnimTimer();
            _anim.Tick += delegate { Step(); };
            ModernTheme.Changed += OnThemeChanged;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            BackColor = ModernTheme.For(this).PageBg;
        }

        public bool Expanded
        {
            get { return _expanded; }
        }

        public void SetExpanded(bool expanded, bool animate)
        {
            if (_expanded == expanded && animate) return;
            _expanded = expanded;
            Control content = ContentControl();
            if (content != null) content.Visible = true;
            if (!animate)
            {
                _phase = expanded ? 1f : 0f;
                _chev = _phase;
                _animating = true;
                Height = Math.Max(CollapsedH, expanded ? _fullHeight : CollapsedH);
                _animating = false;
                if (content != null) content.Visible = expanded;
                Invalidate();
                return;
            }
            _animFrom = _phase;
            _animStart = Environment.TickCount;
            _anim.Start();
        }

        private Control ContentControl()
        {
            return Controls.Count > 0 ? Controls[0] : null;
        }

        public override Rectangle DisplayRectangle
        {
            get
            {
                int top = (int)Math.Round(HeaderH + 6 * _phase);
                int bottom = (int)Math.Round(12 * _phase);
                int left = 16;
                int width = Width - left - 16;
                int height = Height - top - bottom;
                if (width < 0) width = 0;
                if (height < 0) height = 0;
                return new Rectangle(left, top, width, height);
            }
        }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (!_animating && _expanded && _phase >= 0.999f) _fullHeight = Math.Max(CollapsedH, height);
            base.SetBoundsCore(x, y, width, height, specified);
        }

        private Rectangle HeaderRect()
        {
            return new Rectangle(0, 0, Width, HeaderH);
        }

        private void Toggle()
        {
            _expanded = !_expanded;
            if (!ModernTheme.AnimationsEnabled)
            {
                SetExpanded(_expanded, false);
                return;
            }
            Control content = ContentControl();
            if (content != null) content.Visible = true;
            _animFrom = _phase;
            _animStart = Environment.TickCount;
            _anim.Start();
            Invalidate();
        }

        private void Step()
        {
            float target = _expanded ? 1f : 0f;
            float t = (Environment.TickCount - _animStart) / 220f;
            if (t >= 1f)
            {
                _phase = target;
                _chev = target;
                _anim.Stop();
                Control content = ContentControl();
                if (content != null && !_expanded) content.Visible = false;
            }
            else
            {
                _phase = _animFrom + (target - _animFrom) * (float)UiAnim.EaseOutCubic(t);
                _chev = _phase;
            }

            int full = Math.Max(CollapsedH, _fullHeight);
            int h = CollapsedH + (int)Math.Round((full - CollapsedH) * UiAnim.EaseOutCubic(_phase));
            _animating = true;
            Height = Math.Max(CollapsedH, h);
            _animating = false;
            if (Parent != null) Parent.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            UiColors p = ModernTheme.For(this);
            g.Clear(BackColor);

            Rectangle card = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (SolidBrush b = new SolidBrush(p.CardBg))
            using (GraphicsPath path = ModernDraw.Round(card, 14))
                g.FillPath(b, path);

            using (Pen pen = new Pen(_hoverHeader ? p.Accent : p.CardBorder))
            using (GraphicsPath path = ModernDraw.Round(card, 14))
                g.DrawPath(pen, path);

            int barH = Math.Max(10, Height - 24);
            Rectangle bar = new Rectangle(1, 12, 4, barH);
            using (SolidBrush b = new SolidBrush(p.Accent))
            using (GraphicsPath path = ModernDraw.Round(bar, 2))
                g.FillPath(b, path);

            Rectangle titleRect = new Rectangle(18, 0, Math.Max(0, Width - 74), HeaderH);
            TextRenderer.DrawText(g, Text, _headerFont, titleRect, p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Point center = new Point(Width - 26, HeaderH / 2);
            if (_hoverHeader)
            {
                using (SolidBrush b = new SolidBrush(p.MutedBg))
                    g.FillEllipse(b, new Rectangle(center.X - 13, center.Y - 13, 26, 26));
            }
            ModernDraw.Chevron(g, center, (_chev - 1f) * 90f, 10, _hoverHeader ? p.Accent : p.SubText);

            if (_phase > 0.6f)
            {
                int alpha = (int)(255 * Math.Min(1f, (_phase - 0.6f) / 0.4f));
                using (Pen pen = new Pen(ModernTheme.Alpha(p.Border, alpha)))
                    g.DrawLine(pen, 18, HeaderH - 2, Width - 18, HeaderH - 2);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = HeaderRect().Contains(e.Location);
            if (over != _hoverHeader)
            {
                _hoverHeader = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverHeader = false;
            _pressHeader = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && HeaderRect().Contains(e.Location))
            {
                _pressHeader = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool hit = _pressHeader && e.Button == MouseButtons.Left && HeaderRect().Contains(e.Location);
            _pressHeader = false;
            if (hit) Toggle();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                Toggle();
                e.Handled = true;
            }
        }

        private void OnThemeChanged()
        {
            BackColor = ModernTheme.For(this).PageBg;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Dispose();
                _headerFont.Dispose();
                ModernTheme.Changed -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernComboBox : WheelSafeComboBox
    {
        private const int WmPaint = 0x000F;

        public ModernComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            ItemHeight = 24;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            ModernTheme.Changed += OnThemeChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { BeginInvoke(new MethodInvoker(ConfigureForStyle)); }
            catch { }
        }

        private void ConfigureForStyle()
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList)
            {
                if (DrawMode != DrawMode.OwnerDrawFixed) DrawMode = DrawMode.OwnerDrawFixed;
                if (FlatStyle != FlatStyle.Flat) FlatStyle = FlatStyle.Flat;
            }
            else
            {
                if (DrawMode != DrawMode.Normal) DrawMode = DrawMode.Normal;
                if (FlatStyle != FlatStyle.Standard) FlatStyle = FlatStyle.Standard;
            }
            UiColors p = ModernTheme.For(this);
            if (BackColor != p.InputBg) BackColor = p.InputBg;
            if (ForeColor != p.Text) ForeColor = p.Text;
        }

        private void OnThemeChanged()
        {
            if (DropDownStyle != ComboBoxStyle.DropDownList)
            {
                UiColors p = ModernTheme.For(this);
                BackColor = p.InputBg;
                ForeColor = p.Text;
            }
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            UiColors p = ModernTheme.For(this);
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color fill = selected ? p.AccentSoft : p.InputBg;
            using (SolidBrush b = new SolidBrush(fill))
                g.FillRectangle(b, e.Bounds);
            string text = e.Index < Items.Count ? GetItemText(Items[e.Index]) : "";
            Rectangle r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(g, text, Font, r, selected ? p.Accent : p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WmPaint || !IsHandleCreated) return;
            if (DropDownStyle != ComboBoxStyle.DropDownList) return;
            PaintFace();
        }

        private void PaintFace()
        {
            try
            {
                UiColors p = ModernTheme.For(this);
                using (Graphics g = Graphics.FromHwnd(Handle))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(ModernTheme.ContentBack(this));
                    Color bg = Enabled ? p.InputBg : ModernTheme.Mix(p.InputBg, p.CardBg, 0.5f);
                    Color fg = Enabled ? p.Text : p.SubText;
                    Color border = Focused ? p.Accent : p.InputBorder;
                    int bw = 24;
                    Rectangle field = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                    ModernDraw.Fill(g, field, 8, bg);
                    ModernDraw.Outline(g, field, 8, border, Focused ? 1.6f : 1f);

                    Point center = new Point(field.Right - bw / 2 - 2, field.Height / 2);
                    ModernDraw.Chevron(g, center, DroppedDown ? 180f : 0f, 9, Enabled ? p.SubText : p.SubText);

                    string text = SelectedIndex >= 0 && SelectedIndex < Items.Count
                        ? GetItemText(Items[SelectedIndex]) : "";
                    Rectangle tr = new Rectangle(10, 1, Math.Max(0, field.Width - bw - 10), Math.Max(1, field.Height - 2));
                    TextRenderer.DrawText(g, text, Font, tr, fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ModernTheme.Changed -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernPageIntro : Control
    {
        private readonly string _title;
        private readonly string _subtitle;
        private readonly Font _titleFont;

        public ModernPageIntro(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            _titleFont = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Point);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = UiKit.FontSmall;
            BackColor = ModernTheme.P.PageBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiColors p = ModernTheme.For(this);
            g.Clear(ModernTheme.ContentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle mark = new Rectangle(2, 12, 5, 26);
            ModernDraw.Fill(g, mark, 2, p.Accent);
            Rectangle titleRect = new Rectangle(18, 4, Math.Max(0, Width - 24), 36);
            TextRenderer.DrawText(g, _title, _titleFont, titleRect, p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            Rectangle subRect = new Rectangle(20, 38, Math.Max(0, Width - 26), 22);
            TextRenderer.DrawText(g, _subtitle, Font, subRect, p.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            BackColor = ModernTheme.ContentBack(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernNumeric : WheelSafeNumeric
    {
        private readonly Stepper _stepper;

        public ModernNumeric()
        {
            BorderStyle = BorderStyle.FixedSingle;
            Font = UiKit.Font;
            TextAlign = HorizontalAlignment.Left;
            _stepper = new Stepper(this);
            Resize += delegate { LayoutStepper(); };
            ModernTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            UiColors p = ModernTheme.For(this);
            BackColor = p.InputBg;
            ForeColor = p.Text;
            _stepper.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Controls.Count > 0) Controls[0].Visible = false;
            if (_stepper.Parent == null) Controls.Add(_stepper);
            _stepper.BringToFront();
            LayoutStepper();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _stepper.Enabled = Enabled;
            _stepper.Invalidate();
        }

        private void LayoutStepper()
        {
            if (_stepper.Parent == null) return;
            int h = Math.Max(1, Height - 2);
            _stepper.SetBounds(Math.Max(0, Width - 24), 1, 24, h);
        }

        internal void StepValue(int direction)
        {
            try
            {
                Focus();
                if (direction > 0) UpButton();
                else DownButton();
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ModernTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        private sealed class Stepper : Control
        {
            private readonly ModernNumeric _owner;
            private int _hover = -1;
            private int _pressed = -1;

            public Stepper(ModernNumeric owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            private int ZoneAt(Point p)
            {
                return p.Y < Height / 2 ? 0 : 1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                UiColors p = ModernTheme.For(this);
                Color back = Enabled ? p.InputBg : ModernTheme.Mix(p.InputBg, p.CardBg, 0.5f);
                using (SolidBrush b = new SolidBrush(back))
                    g.FillRectangle(b, ClientRectangle);
                using (Pen pen = new Pen(p.InputBorder))
                    g.DrawLine(pen, 0, 2, 0, Height - 3);

                for (int i = 0; i < 2; i++)
                {
                    Rectangle r = new Rectangle(1, i * (Height / 2) + 1, Width - 2, Height / 2 - 1);
                    if (i == _pressed) ModernDraw.Fill(g, r, 4, p.AccentSoft);
                    else if (i == _hover) ModernDraw.Fill(g, r, 4, p.MutedBg);
                    Point c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2 + 1);
                    Color ink = Enabled ? (i == _hover ? p.Accent : p.SubText) : p.SubText;
                    ModernDraw.Chevron(g, c, i == 0 ? 180f : 0f, 7, ink);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int z = ZoneAt(e.Location);
                if (z != _hover)
                {
                    _hover = z;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = -1;
                _pressed = -1;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                _pressed = ZoneAt(e.Location);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int z = ZoneAt(e.Location);
                bool hit = _pressed == z;
                _pressed = -1;
                Invalidate();
                if (hit && Enabled) _owner.StepValue(z == 0 ? 1 : -1);
            }
        }
    }

    internal sealed class ModernListBox : ListBox
    {
        private int _hover = -1;

        public ModernListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            ItemHeight = 26;
            BackColor = ModernTheme.For(this).InputBg;
            ForeColor = ModernTheme.For(this).Text;
            ModernTheme.Changed += OnThemeChanged;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            OnThemeChanged();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            OnThemeChanged();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) OnThemeChanged();
        }

        private void OnThemeChanged()
        {
            UiColors p = ModernTheme.For(this);
            BackColor = p.InputBg;
            ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            UiColors p = ModernTheme.For(this);
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Rectangle r = new Rectangle(0, e.Bounds.Y, Width, e.Bounds.Height);
            Color fill = selected ? p.AccentSoft : (e.Index == _hover ? p.MutedBg : p.InputBg);
            using (SolidBrush b = new SolidBrush(fill))
                g.FillRectangle(b, r);
            if (selected)
            {
                using (SolidBrush b = new SolidBrush(p.Accent))
                    g.FillRectangle(b, new Rectangle(0, r.Y + 3, 3, Math.Max(1, r.Height - 6)));
            }
            string text = GetItemText(Items[e.Index]);
            Rectangle textRect = new Rectangle(12, r.Y, Math.Max(0, r.Width - 20), r.Height);
            TextRenderer.DrawText(g, text, Font, textRect, selected ? p.Accent : p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexFromPoint(e.Location);
            if (i != _hover)
            {
                _hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ModernTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernCheckedListBox : CheckedListBox
    {
        private int _hover = -1;

        public ModernCheckedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            ItemHeight = 26;
            BackColor = ModernTheme.For(this).InputBg;
            ForeColor = ModernTheme.For(this).Text;
            ModernTheme.Changed += OnThemeChanged;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            OnThemeChanged();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            OnThemeChanged();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) OnThemeChanged();
        }

        private void OnThemeChanged()
        {
            UiColors p = ModernTheme.For(this);
            BackColor = p.InputBg;
            ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            UiColors p = ModernTheme.For(this);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Rectangle r = new Rectangle(0, e.Bounds.Y, Width, e.Bounds.Height);
            Color fill = selected ? p.AccentSoft : (e.Index == _hover ? p.MutedBg : p.InputBg);
            using (SolidBrush b = new SolidBrush(fill))
                g.FillRectangle(b, r);

            bool isChecked = false;
            try { isChecked = GetItemChecked(e.Index); }
            catch { }
            Rectangle box = new Rectangle(10, r.Y + (r.Height - 15) / 2, 15, 15);
            using (SolidBrush b = new SolidBrush(isChecked ? p.Accent : p.InputBg))
            using (GraphicsPath path = ModernDraw.Round(box, 4))
                g.FillPath(b, path);
            using (Pen pen = new Pen(isChecked ? p.Accent : p.InputBorder, 1.2f))
            using (GraphicsPath path = ModernDraw.Round(box, 4))
                g.DrawPath(pen, path);
            if (isChecked)
            {
                using (Pen pen = new Pen(Color.White, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(box.X + 3.5f, box.Y + 8f),
                        new PointF(box.X + 6.5f, box.Y + 11f),
                        new PointF(box.X + 11.5f, box.Y + 4.5f)
                    });
                }
            }

            string text = GetItemText(Items[e.Index]);
            Rectangle textRect = new Rectangle(34, r.Y, Math.Max(0, r.Width - 42), r.Height);
            TextRenderer.DrawText(g, text, Font, textRect, selected ? p.Accent : p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexFromPoint(e.Location);
            if (i != _hover)
            {
                _hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ModernTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
