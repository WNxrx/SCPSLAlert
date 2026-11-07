using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal enum OverlayState
    {
        Hidden,
        Showing,
        Visible,
        Hiding
    }

    /// <summary>
    /// 屏幕提示窗口。
    ///
    /// 用 UpdateLayeredWindow 做逐像素透明，所以可以同时做到：
    ///   · 窗口四周完全透明（alpha = 0 的地方连鼠标点击都会穿透）
    ///   · 圆角背景框 + 壁纸 + 玻璃质感 + 外发光
    ///   · 各种淡入 / 滑入 / 缩放动画，时长可以逐条提示单独设
    ///
    /// 具体怎么画全部交给 AlertRenderer，设置窗口里的预览用的是同一份代码。
    /// </summary>
    internal class OverlayForm : Form, IAlertSurface
    {
        // ---- 样式（来自配置） ----
        private WindowStyle _style = new WindowStyle();
        private int _offsetX, _offsetY;
        private Image _wallpaper;
        private string _wallpaperPath = "";

        // ---- 运行时 ----
        private Font _font;
        private Bitmap _surface;
        private AlertLayout _layout;
        private string _text = "";
        private Point _rest = new Point(30, 30);
        private Point _display = new Point(30, 30);
        private OverlayState _state = OverlayState.Hidden;
        private float _anim;
        private readonly Timer _animTimer;

        private bool _editMode;
        private bool _clickThrough = true;
        private bool _dragging;
        private Point _dragOffset;
        private bool _highlight;

        /// <summary>拖动结束（坐标已改变）时触发，用于写回 config.ini。</summary>
        public event EventHandler PositionChanged;

        public OverlayForm(string title)
        {
            Text = title;
            Name = title;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _font = BuildFont(_style.FontFace, _style.FontSize, _style.Bold);

            _animTimer = new Timer();
            _animTimer.Interval = 20;
            _animTimer.Tick += OnAnimTick;
        }

        #region 窗口样式

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TRANSPARENT
                            | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // 内容完全由 UpdateLayeredWindow 提供，WinForms 自己不用画
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateExStyle();

            // 提示窗压在识别区域上时，绝不能被 OCR 自己截进去（否则会自我循环）
            NativeMethods.HideFromCapture(Handle, "提示窗");
        }

        /// <summary>
        /// 应用 / 撤销鼠标穿透。
        ///
        /// 坑：WinForms 在窗口显示流程结束时还会照 CreateParams 再套用一次扩展样式，
        /// 而 CreateParams 里带着 WS_EX_TRANSPARENT，于是 OnHandleCreated 里刚清掉的位
        /// 又被加回来了。所以这里除了“有变化才写”，还要在显示完之后再确认一次。
        /// </summary>
        private void UpdateExStyle()
        {
            if (!IsHandleCreated) return;
            int ex = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
            int want = ex | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
            if (_clickThrough) want |= NativeMethods.WS_EX_TRANSPARENT;
            else want &= ~NativeMethods.WS_EX_TRANSPARENT;
            if (want != ex)
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, want);
        }

        /// <summary>调整模式下定期确认“不穿透”，防止样式被意外改回去。</summary>
        public void ReassertExStyle()
        {
            UpdateExStyle();
        }

        #endregion

        #region 公开状态

        public bool IsPopped
        {
            get { return _state != OverlayState.Hidden; }
        }

        /// <summary>当前显示的文字（日志里记"显示了什么"用；只读）。</summary>
        public string CurrentText
        {
            get { return _text; }
        }

        public Point RestLocation
        {
            get { return _rest; }
        }

        /// <summary>避让后的实际显示位置；RestLocation 仍是用户配置的锚点。</summary>
        public Rectangle DisplayBounds
        {
            get
            {
                Size sz = SurfaceSize;
                return new Rectangle(_display.X, _display.Y, sz.Width, sz.Height);
            }
        }

        public void SetDisplayPosition(int x, int y)
        {
            _display = new Point(x, y);
            if (IsHandleCreated && _state != OverlayState.Hidden) Redraw();
        }

        /// <summary>当前绘制表面的尺寸（要居中的时候用）。</summary>
        public Size SurfaceSize
        {
            get { return _surface == null ? new Size(0, 0) : _surface.Size; }
        }

        public void SetRestLocation(int x, int y)
        {
            _rest = new Point(x, y);
            _display = _rest;
            if (IsHandleCreated && _state != OverlayState.Hidden) Redraw();
        }

        public bool EditMode
        {
            get { return _editMode; }
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                _clickThrough = !value;
                UpdateExStyle();
                if (value)
                {
                    _state = OverlayState.Visible;
                    _anim = 1f;
                    _animTimer.Stop();
                    if (!Visible) Show();
                }
                Redraw();

                // 窗口刚做完显示流程，这时再确认一次扩展样式
                UpdateExStyle();
                if (IsHandleCreated)
                {
                    try { BeginInvoke((MethodInvoker)delegate { UpdateExStyle(); }); }
                    catch { }
                }
            }
        }

        public bool Highlight
        {
            get { return _highlight; }
            set
            {
                if (_highlight == value) return;
                _highlight = value;
                if (IsHandleCreated && _state != OverlayState.Hidden) Redraw();
            }
        }

        /// <summary>把配置里的样式套到窗口上。</summary>
        public void ApplyStyle(WindowStyle style, Config cfg)
        {
            _style.CopyFrom(style);
            _offsetX = cfg.OffsetX;
            _offsetY = cfg.OffsetY;

            Font old = _font;
            _font = BuildFont(_style.FontFace, _style.FontSize, _style.Bold);
            if (old != null && old != SystemFonts.DefaultFont) old.Dispose();

            // 壁纸路径变了才重新读文件
            if (!string.Equals(_wallpaperPath, _style.Wallpaper, StringComparison.OrdinalIgnoreCase))
            {
                _wallpaperPath = _style.Wallpaper;
                if (_wallpaper != null) { _wallpaper.Dispose(); _wallpaper = null; }
                _wallpaper = AlertRenderer.LoadWallpaper(_wallpaperPath);
            }

            _rest = new Point(style.X + _offsetX, style.Y + _offsetY);
            _display = _rest;
            if (IsHandleCreated && _state != OverlayState.Hidden) Redraw();
        }

        /// <summary>按像素整体挪动（调整模式里用方向键 / 按钮微调）。</summary>
        public void Nudge(int dx, int dy)
        {
            _rest = new Point(_rest.X + dx, _rest.Y + dy);
            _display = _rest;
            if (IsHandleCreated && _state != OverlayState.Hidden) Redraw();
        }

        #endregion

        #region 弹出 / 缩回

        /// <summary>弹出（或就地更新）文字。已经在屏幕上时只更新内容，不重播动画。</summary>
        public void Pop(string text)
        {            string t = text == null ? "" : text;
            bool same = (t == _text);
            _text = t;

            if (_state == OverlayState.Visible || _state == OverlayState.Showing)
            {
                // 【别白重画】AlertManager 每 200ms 会把所有正在显示的提示再 Pop 一次。
                // 文字没变、也没在播动画时，重画一遍纯属浪费（UpdateLayeredWindow
                // 是真正的内存拷贝 + 合成，游戏里会掉一点帧）。这里只在
                // "文字真的变了 / 还在播动画" 时才重画。
                if (!same) Redraw();
                return;
            }

            IntPtr handle = Handle; // 先建好句柄，避免显示瞬间空白

            if (AnimatesIn && !UiAnim.Off)
            {
                _state = OverlayState.Showing;
                _anim = 0f;
                _animTimer.Interval = UiAnim.IntervalMs;
                Redraw();
                if (!Visible) Show();
                _animTimer.Start();
            }
            else
            {
                _state = OverlayState.Visible;
                _anim = 1f;
                Redraw();
                if (!Visible) Show();
            }
        }

        /// <summary>旧版提示没有独立读数条，忽略倒计时进度。</summary>
        public void SetTimer(double remainSeconds, double totalSeconds) { }

        /// <summary>缩回并隐藏。</summary>
        public void Retract()
        {
            if (_state == OverlayState.Hidden || _state == OverlayState.Hiding) return;

            if (AnimatesOut && !UiAnim.Off)
            {
                _state = OverlayState.Hiding;
                _animTimer.Interval = UiAnim.IntervalMs;
                _animTimer.Start();
            }
            else
            {
                _state = OverlayState.Hidden;
                _anim = 0f;
                _animTimer.Stop();
                if (Visible) Hide();
            }
        }

        /// <summary>立刻隐藏，不播动画（退出时用）。</summary>
        public void HideNow()
        {
            _animTimer.Stop();
            _state = OverlayState.Hidden;
            _anim = 0f;
            if (Visible) Hide();
        }

        private bool AnimatesIn
        {
            get { return _style.Anim != AnimKind.None && _style.AnimInMs > 0; }
        }

        private bool AnimatesOut
        {
            get { return _style.Anim != AnimKind.None && _style.AnimOutMs > 0; }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (_state == OverlayState.Showing)
            {
                float step = (float)_animTimer.Interval / Math.Max(30f, (float)_style.AnimInMs);
                _anim += step;
                if (_anim >= 1f)
                {
                    _anim = 1f;
                    _state = OverlayState.Visible;
                    _animTimer.Stop();
                }
            }
            else if (_state == OverlayState.Hiding)
            {
                float step = (float)_animTimer.Interval / Math.Max(30f, (float)_style.AnimOutMs);
                _anim -= step;
                if (_anim <= 0f)
                {
                    _anim = 0f;
                    _state = OverlayState.Hidden;
                    _animTimer.Stop();
                    Redraw();
                    if (Visible) Hide();
                    return;
                }
            }
            else
            {
                _animTimer.Stop();
                return;
            }

            Redraw();
        }

        private static float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float inv = 1f - t;
            return 1f - inv * inv * inv; // ease-out cubic
        }

        #endregion

        #region 绘制

        private AlertVisual BuildVisual()
        {
            AlertVisual v = new AlertVisual();
            v.Text = _text;
            v.Font = _font;
            v.TextColor = _style.TextColor;
            v.TextOpacity = _style.Opacity;
            v.BackColor = _style.BackColor;
            v.BackOpacity = _style.BackOpacity;
            v.CornerRadius = _style.CornerRadius;
            v.Icon = _style.Icon;
            v.ShowIcon = _style.ShowIcon;
            v.Wallpaper = _wallpaper;
            v.WallpaperMode = _style.WallpaperMode;
            v.WallpaperOpacity = _style.WallpaperOpacity;
            v.Glass = _style.Glass;
            v.GlassStrength = _style.GlassStrength;
            v.GlassHighlight = _style.GlassHighlight;
            v.GlassNoise = _style.GlassNoise;
            v.Highlight = _highlight;
            return v;
        }

        /// <summary>当前动画进度下的缩放系数。</summary>
        private float CurrentScale(float eased)
        {
            switch (_style.Anim)
            {
                case AnimKind.ZoomIn: return 0.55f + 0.45f * eased;
                case AnimKind.ZoomOut: return 1.45f - 0.45f * eased;
                default: return 1f;
            }
        }

        /// <summary>当前动画进度下的位置偏移。</summary>
        private void CurrentOffset(float eased, out int dx, out int dy)
        {
            dx = 0; dy = 0;
            if (_style.Anim == AnimKind.None) return;
            int d = _style.SlideDistance;
            if (d <= 0) return;
            float k = 1f - eased;
            switch (_style.Anim)
            {
                case AnimKind.FromTop: dy = -(int)Math.Round(k * d); break;
                case AnimKind.FromBottom: dy = (int)Math.Round(k * d); break;
                case AnimKind.FromLeft: dx = -(int)Math.Round(k * d); break;
                case AnimKind.FromRight: dx = (int)Math.Round(k * d); break;
            }
        }

        private void Redraw()
        {
            if (!IsHandleCreated) return;
            BuildSurface();
            PushToScreen();
        }

        private void BuildSurface()
        {
            AlertVisual v = BuildVisual();
            _layout = AlertRenderer.Measure(v);

            Size want = _layout.Surface;
            if (_surface == null || _surface.Width != want.Width || _surface.Height != want.Height)
            {
                if (_surface != null) _surface.Dispose();
                _surface = new Bitmap(want.Width, want.Height, PixelFormat.Format32bppArgb);
            }

            float eased = Ease(_anim);
            using (Graphics g = Graphics.FromImage(_surface))
                AlertRenderer.Draw(g, v, _layout, CurrentScale(eased));

            if (_editMode)
            {
                using (Graphics g = Graphics.FromImage(_surface))
                using (Pen p = new Pen(Color.FromArgb(235, 0, 200, 255), 1f))
                {
                    p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    Rectangle b = _layout.Box;
                    g.DrawRectangle(p, b.X, b.Y, b.Width - 1, b.Height - 1);
                }
            }
        }

        private void PushToScreen()
        {
            if (_surface == null || !IsHandleCreated) return;

            float eased = Ease(_anim);
            int dx, dy;
            CurrentOffset(eased, out dx, out dy);

            float global = eased * AlertRenderer.Clamp(_style.Opacity, 0, 100) / 100f;
            int sca = (int)Math.Round(global * 255f);
            if (sca < 0) sca = 0;
            if (sca > 255) sca = 255;

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = _surface.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE();
                size.cx = _surface.Width;
                size.cy = _surface.Height;

                NativeMethods.POINT src = new NativeMethods.POINT();
                NativeMethods.POINT dst = new NativeMethods.POINT();
                dst.X = _display.X + dx;
                dst.Y = _display.Y + dy;

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = (byte)sca;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    NativeMethods.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            // 自愈：万一扩展样式又被系统 / WinForms 改回去，这里立刻纠正
            UpdateExStyle();
        }

        #endregion

        #region 字体

        private static System.Drawing.Text.PrivateFontCollection _promptFonts;
        private static FontFamily _promptFamily;

        /// <summary>提示专用字体文件；空路径 = 跟随每条提示自己的字体。</summary>
        internal static void SetPromptFontFile(string path)
        {
            try
            {
                _promptFamily = null;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
                _promptFonts = new System.Drawing.Text.PrivateFontCollection();
                _promptFonts.AddFontFile(path);
                if (_promptFonts.Families.Length > 0) _promptFamily = _promptFonts.Families[0];
            }
            catch { _promptFamily = null; }
        }

        private static Font BuildFont(string face, float size, bool bold)
        {
            if (_promptFamily != null)
            {
                try { return new Font(_promptFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); } catch { }
            }
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
            string resolved = ResolveFontFamily(face);
            try { return new Font(resolved, size, style, GraphicsUnit.Point); }
            catch { }
            try { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
            catch { }
            return SystemFonts.DefaultFont;
        }

        public static string ResolveFontFamily(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                try { using (FontFamily ff = new FontFamily(name)) { return ff.Name; } }
                catch { }
            }
            string[] candidates = new string[] { "微软雅黑", "Microsoft YaHei", "Segoe UI", "宋体", "SimSun", "Arial" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try { using (FontFamily ff = new FontFamily(candidates[i])) { return ff.Name; } }
                catch { }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        #endregion

        #region 拖动

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_editMode && e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(e.X, e.Y);
                Cursor = Cursors.SizeAll;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point cur = Cursor.Position;
                SetRestLocation(cur.X - _dragOffset.X, cur.Y - _dragOffset.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Cursor = Cursors.Default;
                if (PositionChanged != null) PositionChanged(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimTick;
                _animTimer.Dispose();
                if (_font != null && _font != SystemFonts.DefaultFont) _font.Dispose();
                if (_surface != null) { _surface.Dispose(); _surface = null; }
                if (_wallpaper != null) { _wallpaper.Dispose(); _wallpaper = null; }
            }
            base.Dispose(disposing);
        }
    }
}
