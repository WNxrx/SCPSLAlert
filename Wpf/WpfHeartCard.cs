using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 心率屏幕提示的卡片。和游戏内提示分开画：
    /// 它不绑事件，只显示 BPM；样式可以在设置里换成完整卡片 / 无框 / 纯数字。
    /// </summary>
    internal sealed class WpfHeartCard : FrameworkElement
    {
        private const double Outer = 3;
        private const double PadX = 14;
        private const double PadY = 8;
        private static readonly FontFamily UiFont =
            UiFonts.Body;
        private static readonly FontFamily MonoFont =
            UiFonts.Mono;
        private static readonly FontFamily DisplayFont =
            UiFonts.Title;

        private int _bpm = 72;
        private int _style = 0;
        private bool _frame = true;
        private bool _label = true;
        private bool _unit = true;
        private bool _pulse = true;
        private bool _wave = true;
        private bool _edit;
        private double _fontSize = 44;
        private Color _accent = Color.FromRgb(255, 84, 90);
        private double _beatPhase;
        private double _beatPulse;
        private double _beatPulseAge = 99;
        private double _pulseDuration = 0.5;
        private int _wavePoints = 2;
        private int _waveAmp = 100;
        private int _waveWidth = 8;
        private readonly List<Bump> _bumps = new List<Bump>();
        private readonly Random _rng = new Random();
        private Size _size = new Size(220, 92);

        private sealed class Bump
        {
            public double Center;
            public double Half;   // 归一化半宽，生成时已保证相邻不重叠
            public double Amp;
            public int Shape;
        }

        public WpfHeartCard()
        {
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
        }

        public int Bpm
        {
            get { return _bpm; }
            set { int v = Math.Max(0, Math.Min(250, value)); if (_bpm == v) return; _bpm = v; InvalidateVisual(); }
        }

        public int Style
        {
            get { return _style; }
            set { int v = Math.Max(0, Math.Min(2, value)); if (_style == v) return; _style = v; Refresh(); }
        }

        public bool Frame
        {
            get { return _frame; }
            set { if (_frame == value) return; _frame = value; Refresh(); }
        }

        public bool Label
        {
            get { return _label; }
            set { if (_label == value) return; _label = value; Refresh(); }
        }

        public bool Unit
        {
            get { return _unit; }
            set { if (_unit == value) return; _unit = value; Refresh(); }
        }

        public bool Pulse
        {
            get { return _pulse; }
            set { if (_pulse == value) return; _pulse = value; Refresh(); }
        }

        /// <summary>整条心电波；每次心跳随机挑几个点冒小波。</summary>
        public bool Wave
        {
            get { return _wave; }
            set { if (_wave == value) return; _wave = value; Refresh(); }
        }

        public int WavePoints
        {
            get { return _wavePoints; }
            set { int v = Math.Max(1, Math.Min(6, value)); if (_wavePoints == v) return; _wavePoints = v; InvalidateVisual(); }
        }

        public int WaveAmp
        {
            get { return _waveAmp; }
            set { int v = Math.Max(10, Math.Min(180, value)); if (_waveAmp == v) return; _waveAmp = v; InvalidateVisual(); }
        }

        public int WaveWidth
        {
            get { return _waveWidth; }
            set { int v = Math.Max(2, Math.Min(24, value)); if (_waveWidth == v) return; _waveWidth = v; InvalidateVisual(); }
        }

        public bool EditMode
        {
            get { return _edit; }
            set { if (_edit == value) return; _edit = value; InvalidateVisual(); }
        }

        public double FontSize
        {
            get { return _fontSize; }
            set { double v = Math.Max(18, Math.Min(140, value)); if (Math.Abs(_fontSize - v) < 0.1) return; _fontSize = v; Refresh(); }
        }

        public Color Accent
        {
            get { return _accent; }
            set { if (_accent == value) return; _accent = value; InvalidateVisual(); }
        }

        /// <summary>
        /// 推进心跳时钟。BPM 只影响间隔，不会重置当前相位，
        /// 所以改 BPM 不会让波形“立刻多跳一下”。
        /// </summary>
        public void Advance(double seconds, int bpm)
        {
            if (seconds <= 0) return;
            if (seconds > 1.0) seconds = 1.0;
            if (bpm < 30) bpm = 72;
            if (bpm > 220) bpm = 220;
            double interval = 60.0 / bpm;
            _beatPhase += seconds / interval;
            bool triggered = false;
            double sinceTrigger = seconds;
            if (_beatPhase >= 1.0)
            {
                double over = _beatPhase - Math.Floor(_beatPhase);
                sinceTrigger = over * interval;
                _beatPhase = over;
                _beatPulseAge = 0;
                GenerateBumps();
                triggered = true;
            }
            _pulseDuration = Math.Max(0.28, Math.Min(0.58, interval * 0.62));
            if (_beatPulseAge < 90)
            {
                _beatPulseAge += triggered ? sinceTrigger : seconds;
                double u = Math.Max(0, Math.Min(1, _beatPulseAge / _pulseDuration));
                _beatPulse = Math.Sin(Math.PI * u);
                if (_beatPulseAge >= _pulseDuration) _beatPulse = 0;
            }
            else
            {
                _beatPulse = 0;
            }
            if (triggered || _beatPulseAge < 90) InvalidateVisual();
        }

        /// <summary>每次心跳在互不重叠的槽位里随机选点，形状 / 振幅 / 宽度随机。</summary>
        private void GenerateBumps()
        {
            _bumps.Clear();
            int count = Math.Max(1, Math.Min(6, _wavePoints));
            double left = 0.07, right = 0.93;
            double slot = (right - left) / count;
            double wantedHalf = Math.Max(0.012, (_waveWidth / 100.0) * 0.5);
            for (int i = 0; i < count; i++)
            {
                double maxHalf = Math.Max(0.012, slot * 0.40);
                double half = Math.Min(maxHalf, wantedHalf * (0.75 + _rng.NextDouble() * 0.6));
                double minC = left + i * slot + half + 0.006;
                double maxC = left + (i + 1) * slot - half - 0.006;
                Bump b = new Bump();
                b.Half = half;
                b.Center = maxC > minC ? minC + _rng.NextDouble() * (maxC - minC)
                                       : left + (i + 0.5) * slot;
                b.Amp = 0.65 + _rng.NextDouble() * 0.85;
                b.Shape = _rng.Next(3);
                _bumps.Add(b);
            }
        }

        /// <summary>心跳是否处在“张开”窗口里，供设置页预览/测试判断。</summary>
        public bool IsBeating { get { return _beatPulse > 0.01; } }

        public Size CardSize { get { return _size; } }

        private void Refresh()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            string num = _bpm <= 0 ? "--" : _bpm.ToString(CultureInfo.InvariantCulture);
            FormattedText numberFt = Text(num, MonoFont, FontWeights.Bold, _fontSize, Brushes.White, dpi);
            FormattedText unitFt = Text(" BPM", MonoFont, FontWeights.Normal, Math.Max(9, _fontSize * 0.24), Brushes.White, dpi);
            FormattedText labelFt = Text("HEART RATE  /  心率", DisplayFont, FontWeights.SemiBold, 9, Brushes.White, dpi);

            double w = numberFt.WidthIncludingTrailingWhitespace + (_unit && _style != 2 ? unitFt.WidthIncludingTrailingWhitespace : 0);
            double h = Math.Max(numberFt.Height, _fontSize * 1.05);
            if (_style == 0)
            {
                w = Math.Max(196, w + 76);
                h = Math.Max(112, h + 44);
                if (_label) h += 13;
            }
            else if (_style == 1)
            {
                w = Math.Max(150, w + 24);
                h = Math.Max(76, h + 22);
                if (_label) h += 12;
            }
            else
            {
                w = Math.Max(70, w + 8);
                h = Math.Max(34, h + 4);
            }
            _size = new Size(Math.Ceiling(w + Outer * 2), Math.Ceiling(h + Outer * 2));
            return _size;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Clip = BuildShape(ActualWidth, ActualHeight, 9, Outer);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 10 || h < 10) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            bool framed = _style == 0 && _frame;
            Brush accent = Solid(_accent);
            Brush ink = Solid(Color.FromRgb(244, 238, 236));
            Brush muted = Solid(Color.FromRgb(150, 132, 132));

            if (framed)
            {
                Geometry shape = BuildShape(w, h, 9, Outer);
                LinearGradientBrush bg = new LinearGradientBrush(
                    Color.FromArgb(246, 16, 11, 13), Color.FromArgb(246, 7, 6, 7), 90);
                bg.Freeze();
                dc.DrawGeometry(bg, null, shape);
                for (double y = Outer; y < h - Outer; y += 4)
                    dc.DrawRectangle(Solid(Color.FromArgb(10, _accent.R, _accent.G, _accent.B)), null,
                        new Rect(Outer, y, w - Outer * 2, 1));
                dc.PushClip(shape);
                dc.DrawRectangle(accent, null, new Rect(Outer, Outer + 2, 3, h - Outer * 2 - 4));
                dc.Pop();
                Pen border = new Pen(Solid(Color.FromArgb(150, _accent.R, _accent.G, _accent.B)), 1);
                border.Freeze();
                dc.DrawGeometry(null, border, shape);
            }
            else
            {
                // 无框模式不画底：只留数字/标签本身。亮画面上靠主色和字号保证可读。
            }

            string num = _bpm <= 0 ? "--" : _bpm.ToString(CultureInfo.InvariantCulture);
            double numSize = _fontSize;
            double unitSize = Math.Max(9, _fontSize * 0.24);
            FormattedText numberFt = Text(num, MonoFont, FontWeights.Bold, numSize,
                _bpm > 0 ? accent : muted, dpi);
            FormattedText unitFt = Text("BPM", MonoFont, FontWeights.Normal, Math.Max(10, unitSize), muted, dpi);
            FormattedText labelFt = Text("HEART RATE  /  心率", DisplayFont, FontWeights.SemiBold, 9,
                muted, dpi);

            double numberH = numberFt.Height;
            double top = Outer + PadY;
            if (_style == 0)
            {
                if (_label)
                {
                    dc.DrawText(labelFt, new Point(Outer + PadX + 7, top));
                    top += 14;
                }
                if (_pulse)
                    DrawHeart(dc, new Point(w - Outer - PadX - 10, top + numberH / 2 + 2),
                        13 + BeatBurst() * 4.0, accent);
                double baseline = top + numberH;
                dc.DrawText(numberFt, new Point(Outer + PadX + 7, top));
                if (_unit)
                    dc.DrawText(unitFt, new Point(Outer + PadX + 9 + numberFt.WidthIncludingTrailingWhitespace,
                        baseline - unitFt.Height - 3));
                if (_wave)
                    DrawPulse(dc, new Rect(Outer + PadX + 7, baseline + 3,
                        w - Outer * 2 - PadX * 2 - 14, Math.Max(10, h - baseline - Outer - 9)), accent);
            }
            else if (_style == 1)
            {
                if (_label)
                {
                    dc.DrawText(labelFt, new Point(Outer + PadX, top));
                    top += 13;
                }
                dc.DrawText(numberFt, new Point(Outer + PadX, top));
                if (_unit)
                    dc.DrawText(unitFt, new Point(Outer + PadX + 2 + numberFt.WidthIncludingTrailingWhitespace,
                        top + numberH - unitFt.Height - 3));
                if (_pulse)
                    DrawHeart(dc, new Point(w - Outer - PadX - 8, top + numberH / 2 + 2),
                        10 + BeatBurst() * 3.0, accent);
                if (_wave)
                {
                    double wy = top + numberH + 4;
                    DrawPulse(dc, new Rect(Outer + PadX, wy, w - Outer * 2 - PadX * 2,
                        Math.Max(8, h - wy - Outer - 4)), accent);
                }
            }
            else
            {
                dc.DrawText(numberFt, new Point(Outer + 4, Outer + 2));
            }

            if (_edit)
            {
                Pen dash = new Pen(Solid(Color.FromRgb(56, 214, 255)), 1);
                dash.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
                dash.Freeze();
                dc.DrawRectangle(null, dash,
                    new Rect(Outer + 1, Outer + 1, w - Outer * 2 - 2, h - Outer * 2 - 2));
            }
        }

        private void DrawHeart(DrawingContext dc, Point c, double r, Brush brush)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y + r * 0.55), true, true);
                ctx.BezierTo(new Point(c.X - r * 1.2, c.Y - r * 0.15),
                    new Point(c.X - r * 0.58, c.Y - r * 1.15),
                    new Point(c.X, c.Y - r * 0.42), true, false);
                ctx.BezierTo(new Point(c.X + r * 0.58, c.Y - r * 1.15),
                    new Point(c.X + r * 1.2, c.Y - r * 0.15),
                    new Point(c.X, c.Y + r * 0.55), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(brush, null, g);
        }

        private void DrawPulse(DrawingContext dc, Rect r, Brush brush)
        {
            if (r.Width < 20 || r.Height < 3) return;
            double y = r.Y + r.Height * 0.55;
            double burst = _beatPulse;

            // 没有跳动时就是一条直线。
            if (burst <= 0.01 || _bumps.Count == 0)
            {
                Pen flat = new Pen(new SolidColorBrush(Color.FromArgb(165,
                    _accent.R, _accent.G, _accent.B)), 1.25);
                flat.Freeze();
                dc.DrawLine(flat, new Point(r.X, y), new Point(r.Right, y));
                return;
            }

            double ampBase = Math.Min(r.Height * 1.0,
                r.Height * 0.88 * burst * (_waveAmp / 100.0));
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                Point last = new Point(r.X, y);
                ctx.BeginFigure(last, false, false);
                for (int i = 0; i < _bumps.Count; i++)
                {
                    Bump b = _bumps[i];
                    double cx = r.X + b.Center * r.Width;
                    double half = Math.Max(4.0, b.Half * r.Width);
                    double x0 = Math.Max(r.X, cx - half);
                    double x1 = Math.Min(r.Right, cx + half);
                    if (x1 - x0 < 2) continue;

                    // 直线到波起点：这一段保留直线
                    ctx.LineTo(new Point(x0, y), true, false);
                    // 波内部：用局部心电形状替代直线
                    int steps = 30;
                    for (int s = 1; s <= steps; s++)
                    {
                        double t = (double)s / steps;
                        double hh = BumpShape(b.Shape, t) * b.Amp;
                        ctx.LineTo(new Point(x0 + (x1 - x0) * t, y - ampBase * hh), true, false);
                    }
                    ctx.LineTo(new Point(x1, y), true, false);
                }
                ctx.LineTo(new Point(r.Right, y), true, false);
            }
            g.Freeze();

            // 一条很淡的“影子线”跟着波形走，不再留一条穿波而过的背景直线。
            Pen shadow = new Pen(new SolidColorBrush(Color.FromArgb(38,
                _accent.R, _accent.G, _accent.B)), 4.0);
            shadow.StartLineCap = PenLineCap.Round;
            shadow.EndLineCap = PenLineCap.Round;
            shadow.Freeze();
            dc.DrawGeometry(null, shadow, g);

            Pen wavePen = new Pen(brush, 1.5);
            wavePen.Freeze();
            dc.DrawGeometry(null, wavePen, g);
        }

        /// <summary>局部心电复合波：P - QRS - T，不是三角/正弦包。</summary>
        private static double BumpShape(int shape, double t)
        {
            if (t <= 0 || t >= 1) return 0;
            if (shape == 0)
            {
                return 0.18 * G(t, 0.18, 0.055)
                     - 0.16 * G(t, 0.33, 0.024)
                     + 1.00 * G(t, 0.41, 0.019)
                     - 0.38 * G(t, 0.49, 0.025)
                     + 0.28 * G(t, 0.70, 0.075);
            }
            if (shape == 1)
            {
                return 0.14 * G(t, 0.16, 0.050)
                     - 0.12 * G(t, 0.31, 0.024)
                     + 0.82 * G(t, 0.39, 0.028)
                     - 0.28 * G(t, 0.47, 0.030)
                     + 0.44 * G(t, 0.73, 0.100);
            }
            return 0.22 * G(t, 0.20, 0.045)
                 - 0.20 * G(t, 0.34, 0.020)
                 + 1.14 * G(t, 0.42, 0.015)
                 - 0.46 * G(t, 0.50, 0.022)
                 + 0.22 * G(t, 0.67, 0.060);
        }

        private static double G(double t, double c, double s)
        {
            double d = (t - c) / s;
            return Math.Exp(-d * d);
        }

        private double BeatBurst()
        {
            return _beatPulse;
        }

        private static FormattedText Text(string s, FontFamily family, FontWeight weight,
            double size, Brush brush, double dpi)
        {
            return new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
                size, brush, dpi);
        }

        private static Geometry BuildShape(double w, double h, double cut, double inset)
        {
            if (w <= 0 || h <= 0) return Geometry.Empty;
            double l = inset, t = inset, r = w - inset, b = h - inset;
            if (r - l < cut * 2 || b - t < cut * 2) cut = Math.Min((r - l) / 2, (b - t) / 2);
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(l, t), true, true);
                c.LineTo(new Point(r - cut, t), true, false);
                c.LineTo(new Point(r, t + cut), true, false);
                c.LineTo(new Point(r, b), true, false);
                c.LineTo(new Point(l + cut, b), true, false);
                c.LineTo(new Point(l, b - cut), true, false);
            }
            g.Freeze();
            return g;
        }

        private static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
