using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>准星的全部可调参数（预览与后续覆盖层共用同一份定义）。</summary>
    internal sealed class CrosshairModel
    {
        public int Type = 0;             // 0 十字 1 圆环 2 纯点 3 菱形 4 方框 5 三角 6 内线+外线
        public bool TShape;
        public double Length = 16;
        public double Gap = 5;
        public double Thickness = 2;
        public double DotSize;
        public double RingRadius = 26;
        public double Rotation;

        public bool OuterEnabled = true;
        public double OuterLength = 6;
        public double OuterThickness = 2;
        public double OuterGap = 6;

        public Color Main = Color.FromArgb(255, 0, 255, 128);
        public int Opacity = 92;

        public bool OutlineEnabled = true;
        public Color OutlineColor = Colors.Black;
        public int OutlineOpacity = 100;
        public double OutlineWidth = 2;

        public bool DynamicEnabled = true;
        public bool FireEnabled = true;
        public bool HitFlash = true;
        public double FireAmount = 14;
        public int FireDuration = 120;
        public int FireCurve = 0;
        public double FireMaxMul = 2;

        public string RingKey = "E";
        public int RingEase = 3;
        public int RingDuration = 900;
        public double RingRadiusMax = 34;
        public string ExpandKey = "Space";
        public int ExpandDuration = 220;
        public double ExpandAmount = 10;

        public double OffsetX;
        public double OffsetY;
        public bool DpiScale = true;
        public bool SnapCenter = true;

        public static readonly string[] TypeNames = { "十字", "圆环", "纯点", "菱形", "方框", "三角", "内线+外线" };

        public Color MainWithAlpha { get { return WithAlpha(Main, Opacity); } }
        public Color OutlineWithAlpha { get { return WithAlpha(OutlineColor, OutlineOpacity); } }

        public static Color WithAlpha(Color c, int percent)
        {
            int a = (int)Math.Round(percent * 2.55);
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return Color.FromArgb((byte)a, c.R, c.G, c.B);
        }
    }

    /// <summary>
    /// 实时准星预览。
    ///
    /// 帧率：直接挂 CompositionTarget.Rendering —— 每个显示器刷新周期回调一次，
    /// 144Hz 屏幕就是 144 帧，不会被 60 帧的 DispatcherTimer 卡住。
    /// 动画全部用「时间戳闭式解」算扩散/缩圈，不靠累加，所以不会随帧率漂移。
    /// </summary>
    internal sealed class CrosshairPreview : FrameworkElement
    {
        public CrosshairModel Model;
        public int BackgroundMode;          // 0 暗 1 亮 2 棋盘
        public double Zoom = 1;
        public bool ShowGrid = true;
        public bool ShowLimit = true;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _holding;
        private double _holdStart;
        private double _spreadAtRelease;
        private double _releaseAt = -100;
        private readonly System.Collections.Generic.List<double> _rings = new System.Collections.Generic.List<double>();
        private double _expandAt = -100;
        private double _hitAt = -100;

        private int _frames;
        private double _fpsAt = -1;
        private double _fps;
        private bool _hooked;

        public double MeasuredFps { get { return _fps; } }

        public CrosshairPreview()
        {
            Focusable = true;
            ClipToBounds = true;
            Loaded += delegate { RebuildClock(); };
            Unloaded += delegate { Hook(false); };
            FocusableChanged += delegate { };
        }

        private DispatcherTimer _fast;
        private int _targetFps;

        /// <summary>
        /// 渲染节奏：0 = 跟随显示器刷新（CompositionTarget）；
        /// &gt;0 = 用高精度计时器按该频率推进（120 / 144 / 240）。
        /// 动画是时间闭式解，所以频率越高越顺，且不会有累加误差。
        /// </summary>
        public int TargetFps
        {
            get { return _targetFps; }
            set
            {
                if (_targetFps == value) return;
                _targetFps = value;
                if (IsLoaded) RebuildClock();
            }
        }

        private void RebuildClock()
        {
            if (_fast != null)
            {
                _fast.Stop();
                _fast = null;
            }
            Hook(false);
            if (_targetFps <= 0)
            {
                Hook(true);
                return;
            }
            _fast = new DispatcherTimer(DispatcherPriority.Render);
            _fast.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, 1000.0 / _targetFps));
            _fast.Tick += delegate { InvalidateVisual(); };
            _fast.Start();
        }

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;
            if (on) CompositionTarget.Rendering += OnRendering;
            else CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            InvalidateVisual();
        }

        // ---------------- 动画状态（闭式解） ----------------
        private double Now { get { return _clock.Elapsed.TotalSeconds; } }

        private double CurrentSpread()
        {
            CrosshairModel m = Model;
            if (m == null || !m.DynamicEnabled || !m.FireEnabled) return 0;
            double cap = m.FireAmount * Math.Max(1, m.FireMaxMul);
            double now = Now;
            if (_holding)
            {
                double t = now - _holdStart;
                double ramp = Math.Min(1.0, t / 0.055);          // 55ms 撑满
                double v = m.FireAmount * (1 - Math.Pow(1 - ramp, 3));
                return Math.Min(cap, v);
            }
            double dur = Math.Max(0.02, m.FireDuration / 1000.0);
            double since = now - _releaseAt;
            if (since < 0 || since >= dur) return 0;
            double u = since / dur;
            double s = _spreadAtRelease;
            if (m.FireCurve == 1) return s * (1 - u);
            if (m.FireCurve == 2)
            {
                if (u < 0.35) return s;
                double v = (u - 0.35) / 0.65;
                return s * (1 - v * v);
            }
            return s * Math.Exp(-3.0 * u);
        }

        /// <summary>当前还活着的圈（0..1 进度），每按一次就多一个。</summary>
        private double[] ActiveRings()
        {
            CrosshairModel m = Model;
            if (m == null || !m.DynamicEnabled) return null;
            double dur = Math.Max(0.06, m.RingDuration / 1000.0);
            double now = Now;
            System.Collections.Generic.List<double> keep = new System.Collections.Generic.List<double>();
            lock (_rings)
            {
                for (int i = _rings.Count - 1; i >= 0; i--)
                {
                    double t = (now - _rings[i]) / dur;
                    if (t >= 1) { _rings.RemoveAt(i); continue; }
                    keep.Add(t);
                }
            }
            return keep.Count == 0 ? null : keep.ToArray();
        }

        private double ExpandPulse()
        {
            CrosshairModel m = Model;
            if (m == null || !m.DynamicEnabled) return 0;
            double t = (Now - _expandAt) / Math.Max(0.06, m.ExpandDuration / 1000.0);
            if (t < 0 || t >= 1) return 0;
            return Math.Sin(Math.PI * t) * m.ExpandAmount;
        }

        private double HitAlpha()
        {
            CrosshairModel m = Model;
            if (m == null || !m.HitFlash) return 0;
            double t = (Now - _hitAt) / 0.18;
            return t >= 0 && t < 1 ? (1 - t) * 0.75 : 0;
        }

        // ---------------- 绘制 ----------------
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 8 || h < 8) return;

            // FPS 统计
            _frames++;
            double now = Now;
            if (_fpsAt < 0) _fpsAt = now;
            else if (now - _fpsAt >= 0.5)
            {
                _fps = _frames / (now - _fpsAt);
                _frames = 0;
                _fpsAt = now;
            }

            PaintBackdrop(dc, w, h);
            CrosshairModel m = Model;
            if (m == null) return;

            double cx = Math.Round(w / 2 + m.OffsetX);
            double cy = Math.Round(h / 2 + m.OffsetY);

            if (ShowGrid)
            {
                Pen g1 = new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 1);
                g1.DashStyle = DashStyles.Dot;
                dc.DrawLine(g1, new Point(0, cy), new Point(w, cy));
                dc.DrawLine(g1, new Point(cx, 0), new Point(cx, h));
            }

            double spread = CurrentSpread();
            if (ShowLimit && m.DynamicEnabled && m.FireEnabled && m.FireAmount > 0)
            {
                double r = (m.Gap + m.Length + m.FireAmount * Math.Max(1, m.FireMaxMul)) * Zoom;
                Pen lim = new Pen(new SolidColorBrush(Color.FromArgb(52, 67, 207, 187)), 1);
                lim.DashStyle = DashStyles.Dash;
                dc.DrawEllipse(null, lim, new Point(cx, cy), r, r);
            }

            DrawReticle(dc, m, cx, cy, Zoom, spread);
            DrawRings(dc, m, cx, cy, Zoom);

            double hit = HitAlpha();
            if (hit > 0)
            {
                Pen p = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * hit), 67, 207, 187)), 2);
                dc.DrawEllipse(null, p, new Point(cx, cy), 10, 10);
            }

            PaintHud(dc, w, h, spread);
        }

        private void PaintBackdrop(DrawingContext dc, double w, double h)
        {
            if (BackgroundMode == 1)
            {
                LinearGradientBrush g = new LinearGradientBrush(
                    Color.FromArgb(255, 246, 248, 243), Color.FromArgb(255, 200, 210, 202), 45);
                dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
            }
            else if (BackgroundMode == 2)
            {
                double cell = 20;
                SolidColorBrush a = new SolidColorBrush(Color.FromArgb(255, 58, 62, 66));
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb(255, 41, 45, 48));
                a.Freeze(); b.Freeze();
                for (int y = 0; y * cell < h; y++)
                    for (int x = 0; x * cell < w; x++)
                        dc.DrawRectangle(((x + y) % 2 == 0) ? a : b, null,
                            new Rect(x * cell, y * cell, cell, cell));
            }
            else
            {
                LinearGradientBrush g = new LinearGradientBrush(
                    Color.FromArgb(255, 38, 44, 48), Color.FromArgb(255, 16, 19, 22), 90);
                dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
            }

            Pen border = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
            dc.DrawRectangle(null, border, new Rect(0.5, 0.5, w - 1, h - 1));
        }

        private void DrawReticle(DrawingContext dc, CrosshairModel m, double cx, double cy,
                                 double k, double spread)
        {
            Color mainC = CrosshairModel.WithAlpha(m.Main, m.Opacity);
            Color outlC = CrosshairModel.WithAlpha(m.OutlineColor, m.OutlineOpacity);
            SolidColorBrush main = new SolidColorBrush(mainC);
            SolidColorBrush outl = new SolidColorBrush(outlC);
            main.Freeze(); outl.Freeze();

            double th = Math.Max(0.8, m.Thickness * k);
            double len = Math.Max(0, m.Length * k);
            double gap = Math.Max(0.5, (m.Gap + spread) * k);
            double ow = m.OutlineWidth * k;
            bool outline = m.OutlineEnabled && m.OutlineOpacity > 0 && m.OutlineWidth > 0;

            if (m.Type == 1)
            {
                double r = (m.RingRadius + spread) * k;
                if (r > 1)
                {
                    if (outline) dc.DrawEllipse(null, new Pen(outl, th + ow * 2), new Point(cx, cy), r, r);
                    dc.DrawEllipse(null, new Pen(main, th), new Point(cx, cy), r, r);
                }
            }
            else if (m.Type == 6)
            {
                DrawArms(dc, m, cx, cy, gap, len, th, main, outl, outline, ow, true);
                if (m.OuterEnabled && m.OuterLength > 0)
                {
                    double ogap = gap + len + m.OuterGap * k;
                    DrawArms(dc, m, cx, cy, ogap, m.OuterLength * k,
                        Math.Max(0.8, m.OuterThickness * k), main, outl, outline, ow, true);
                }
            }
            else if (m.Type == 3) DrawPoly(dc, m, cx, cy, len + gap, 4, main, mainC, outl, outline, ow, -Math.PI / 2);
            else if (m.Type == 4) DrawPoly(dc, m, cx, cy, len + gap, 4, main, mainC, outl, outline, ow, Math.PI / 4);
            else if (m.Type == 5) DrawPoly(dc, m, cx, cy, len + gap, 3, main, mainC, outl, outline, ow, -Math.PI / 2);
            else if (m.Type != 2) DrawArms(dc, m, cx, cy, gap, len, th, main, outl, outline, ow, !m.TShape);

            if (m.DotSize > 0.01)
            {
                double d = m.DotSize * k;
                dc.DrawEllipse(main, null, new Point(cx, cy), d / 2, d / 2);
            }

        }

        /// <summary>每一圈独立收拢：后按的圈叠在前面，旧圈继续走自己的进度。</summary>
        private void DrawRings(DrawingContext dc, CrosshairModel m, double cx, double cy, double k)
        {
            double[] ts = ActiveRings();
            if (ts == null || m.RingRadiusMax <= 0) return;
            Color mainC = CrosshairModel.WithAlpha(m.Main, m.Opacity);
            double th = Math.Max(1.0, m.Thickness * k);
            for (int i = 0; i < ts.Length; i++)
            {
                double t = ts[i];
                double e = CrosshairEase.Ring(t, m.RingEase);
                double r = m.RingRadiusMax * k * (1 - e);
                byte a = (byte)(mainC.A * Math.Pow(1 - t, 1.25));
                if (a <= 4 || r <= 1) continue;
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(a, mainC.R, mainC.G, mainC.B)),
                    th), new Point(cx, cy), r, r);
            }
        }

        private void DrawArms(DrawingContext dc, CrosshairModel m, double cx, double cy,
                              double gap, double len, double th, Brush main, Brush outl, bool outline,
                              double ow, bool withTop)
        {
            if (len <= 0.2) return;
            double inner = gap, outer = gap + len;
            if (outer <= inner + 0.2) return;

            dc.PushTransform(new RotateTransform(m.Rotation, cx, cy));
            Line(dc, cx + inner, cy, cx + outer, cy, th, main, outl, outline, ow);
            Line(dc, cx, cy + inner, cx, cy + outer, th, main, outl, outline, ow);
            Line(dc, cx - inner, cy, cx - outer, cy, th, main, outl, outline, ow);
            if (withTop) Line(dc, cx, cy - inner, cx, cy - outer, th, main, outl, outline, ow);
            dc.Pop();
        }

        private static void Line(DrawingContext dc, double x1, double y1, double x2, double y2,
                                 double th, Brush main, Brush outl, bool outline, double ow)
        {
            if (outline)
            {
                Pen p = new Pen(outl, th + ow * 2);
                p.StartLineCap = PenLineCap.Round;
                p.EndLineCap = PenLineCap.Round;
                dc.DrawLine(p, new Point(x1, y1), new Point(x2, y2));
            }
            Pen mp = new Pen(main, th);
            mp.StartLineCap = PenLineCap.Round;
            mp.EndLineCap = PenLineCap.Round;
            dc.DrawLine(mp, new Point(x1, y1), new Point(x2, y2));
        }

        private void DrawPoly(DrawingContext dc, CrosshairModel m, double cx, double cy, double r, int sides,
                              Brush fill, Color mainC, Brush outl, bool outline, double ow, double start)
        {
            if (r <= 0.5) return;
            Point[] pts = new Point[sides];
            double rot = start + m.Rotation * Math.PI / 180.0;
            for (int i = 0; i < sides; i++)
            {
                double a = rot + i * 2 * Math.PI / sides;
                pts[i] = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            }
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], false, true);
                for (int i = 1; i < sides; i++) ctx.LineTo(pts[i], true, true);
            }
            geo.Freeze();
            if (outline)
            {
                Pen op = new Pen(outl, ow * 2 + 1.4);
                op.LineJoin = PenLineJoin.Round;
                dc.DrawGeometry(null, op, geo);
            }
            Pen p = new Pen(fill, 1.6);
            p.LineJoin = PenLineJoin.Round;
            dc.DrawGeometry(null, p, geo);
        }

        private void PaintHud(DrawingContext dc, double w, double h, double spread)
        {
            Typeface mono = new Typeface(UiFonts.Mono, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            Typeface ui = new Typeface(UiFonts.Body, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            SolidColorBrush plate = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            plate.Freeze();

            // FPS
            string fps = _fps <= 0 ? "--" : _fps.ToString("F0");
            FormattedText ft = new FormattedText(fps + " FPS", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                mono, 10, new SolidColorBrush(Color.FromArgb(235, 120, 230, 205)), dpi);
            Rect fr = new Rect(w - ft.Width - 22, 10, ft.Width + 12, ft.Height + 8);
            dc.DrawRoundedRectangle(plate, null, fr, 3, 3);
            dc.DrawText(ft, new Point(fr.X + 6, fr.Y + 4));

            // 状态行
            string state = _holding
                ? "按住中 · 扩散 " + spread.ToString("F1") + " px"
                : "在画面里按住左键试扩散 · E 缩圈 · Space 回弹";
            double maxWidth = Math.Max(40, w - 150);
            FormattedText st = new FormattedText(state, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                ui, 11, new SolidColorBrush(Color.FromArgb(230, 225, 238, 234)), dpi);
            st.MaxTextWidth = maxWidth;
            Rect sr = new Rect(10, h - st.Height - 14, st.Width + 16, st.Height + 8);
            dc.DrawRoundedRectangle(plate, null, sr, 3, 3);
            dc.DrawText(st, new Point(sr.X + 8, sr.Y + 4));
        }

        // ---------------- 输入 ----------------
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            _holding = true;
            _holdStart = Now;
            _hitAt = Now;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_holding) return;
            _spreadAtRelease = CurrentSpread();
            _releaseAt = Now;
            _holding = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Model == null) return;
            if (string.Equals(e.Key.ToString(), Model.RingKey, StringComparison.OrdinalIgnoreCase))
            {
                TriggerRing();
                e.Handled = true;
            }
            else if (string.Equals(e.Key.ToString(), Model.ExpandKey, StringComparison.OrdinalIgnoreCase))
            {
                _expandAt = Now;
                e.Handled = true;
            }
            else if (e.Key == Key.Space && Model.ExpandKey != "Space")
            {
                _expandAt = Now;
                e.Handled = true;
            }
        }

        public void TriggerRing() { lock (_rings) { _rings.Add(Now); if (_rings.Count > 10) _rings.RemoveAt(0); } }
        public void TriggerExpand() { _expandAt = Now; }
        public void TriggerHit() { _hitAt = Now; }
    }
}





