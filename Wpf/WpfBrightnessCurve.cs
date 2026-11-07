using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 伽马 / 饱和度双曲线画布（WPF）。替代旧 WinForms 的 BrightnessCurveGraph。
    ///
    /// 交互：
    ///   · 左键按住节点上下拖 = 改该点数值（伽马曲线 / 饱和度曲线各自独立）；
    ///   · 左键双击空白 = 在该亮度处加点；
    ///   · 右键点节点 = 删点（至少保留 3 个）；
    ///   · 拖伽马时若开了自动补色，饱和度按新的补偿公式跟着走。
    /// </summary>
    internal sealed class BrightnessCurveCanvas : FrameworkElement
    {
        public List<BrightnessPoint> Points = new List<BrightnessPoint>();
        public bool Editable = true;
        public bool ShowSaturation = true;
        public bool AutoSaturation = true;
        public double CompensationBoost = 0.10;
        public int SelectedLuma = -1;

        public event Action Changed;

        private BrightnessPoint _drag;
        private bool _dragSat;
        private const double PadLeft = 42;
        private const double PadRight = 12;
        private const double PadTop = 12;
        private const double PadBottom = 24;

        private static readonly Color GammaColor = Color.FromRgb(0x3C, 0xD2, 0xF5);
        private static readonly Color SatColor = Color.FromRgb(0xFF, 0xAD, 0x4A);
        private static readonly Color GridColor = Color.FromRgb(0x2A, 0x30, 0x38);
        private static readonly Color FaintColor = Color.FromRgb(0x22, 0x28, 0x30);
        private static readonly Color TextColor = Color.FromRgb(0x8A, 0x94, 0xA3);

        public BrightnessCurveCanvas()
        {
            Focusable = false;
            ClipToBounds = true;
        }

        private Rect Plot
        {
            get
            {
                double w = RenderSize.Width - PadLeft - PadRight;
                double h = RenderSize.Height - PadTop - PadBottom;
                if (w < 20) w = 20;
                if (h < 20) h = 20;
                return new Rect(PadLeft, PadTop, w, h);
            }
        }

        private double X(int luma) { Rect r = Plot; return r.Left + r.Width * Math.Max(0, Math.Min(255, luma)) / 255.0; }
        private double Y(double v) { Rect r = Plot; v = Math.Max(0.2, Math.Min(1.6, v)); return r.Bottom - r.Height * (v - 0.2) / 1.4; }
        private int LumaAt(double x) { Rect r = Plot; return (int)Math.Round(Math.Max(0, Math.Min(1, (x - r.Left) / r.Width)) * 255); }
        private double ValueAt(double y) { Rect r = Plot; return 0.2 + Math.Max(0, Math.Min(1, (r.Bottom - y) / r.Height)) * 1.4; }

        public double ValueFor(bool sat)
        {
            List<BrightnessPoint> ordered = Ordered();
            return sat ? ordered[0].Saturation : ordered[0].Gamma;
        }

        private List<BrightnessPoint> Ordered()
        {
            List<BrightnessPoint> list = new List<BrightnessPoint>(Points);
            list.Sort(delegate(BrightnessPoint a, BrightnessPoint b) { return a.Luma.CompareTo(b.Luma); });
            if (list.Count == 0) list.Add(new BrightnessPoint { Luma = 0, Gamma = 0.80, Saturation = 1.07 });
            return list;
        }

        private double Interp(int luma, bool sat)
        {
            List<BrightnessPoint> p = Ordered();
            if (luma <= p[0].Luma) return sat ? p[0].Saturation : p[0].Gamma;
            if (luma >= p[p.Count - 1].Luma) return sat ? p[p.Count - 1].Saturation : p[p.Count - 1].Gamma;
            for (int i = 0; i < p.Count - 1; i++)
            {
                if (luma < p[i + 1].Luma)
                {
                    double t = (double)(luma - p[i].Luma) / Math.Max(1, p[i + 1].Luma - p[i].Luma);
                    t = t * t * (3 - 2 * t);
                    double a = sat ? p[i].Saturation : p[i].Gamma;
                    double b = sat ? p[i + 1].Saturation : p[i + 1].Gamma;
                    return a + (b - a) * t;
                }
            }
            return sat ? p[p.Count - 1].Saturation : p[p.Count - 1].Gamma;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Rect r = Plot;
            // 背景
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)), null, r);

            // 网格：横向 0.2/0.6/1.0/1.4，纵向每 32 亮度
            Pen grid = new Pen(new SolidColorBrush(GridColor), 1);
            Pen faint = new Pen(new SolidColorBrush(FaintColor), 1);
            for (int i = 0; i <= 4; i++)
            {
                double v = 0.2 + i * 0.35;
                double y = Y(v);
                dc.DrawLine(faint, new Point(r.Left, y), new Point(r.Right, y));
                FormattedText ft = Label(v.ToString("0.0", CultureInfo.InvariantCulture));
                dc.DrawText(ft, new Point(4, y - ft.Height / 2));
            }
            for (int l = 0; l <= 255; l += 32)
            {
                double x = X(l);
                dc.DrawLine(l % 64 == 0 ? faint : faint, new Point(x, r.Top), new Point(x, r.Bottom));
                if (l % 64 == 0)
                {
                    FormattedText ft = Label(l.ToString());
                    dc.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 4));
                }
            }

            // 曲线
            DrawCurve(dc, r, false, GammaColor);
            if (ShowSaturation) DrawCurve(dc, r, true, SatColor);

            // 节点
            List<BrightnessPoint> ordered = Ordered();
            for (int i = 0; i < ordered.Count; i++)
            {
                BrightnessPoint p = ordered[i];
                DrawNode(dc, p.Luma, p.Gamma, GammaColor, p.Luma == SelectedLuma);
                if (ShowSaturation) DrawNode(dc, p.Luma, p.Saturation, SatColor, p.Luma == SelectedLuma);
            }
        }

        private FormattedText Label(string text)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 9, new SolidColorBrush(TextColor));
        }

        private void DrawCurve(DrawingContext dc, Rect r, bool sat, Color color)
        {
            List<BrightnessPoint> ordered = Ordered();
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                bool first = true;
                for (int x = 0; x <= 255; x += 2)
                {
                    double v = Interp(x, sat);
                    Point pt = new Point(X(x), Y(v));
                    if (first) { ctx.BeginFigure(pt, false, false); first = false; }
                    else ctx.LineTo(pt, true, false);
                }
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.8), geo);
        }

        private void DrawNode(DrawingContext dc, int luma, double value, Color color, bool selected)
        {
            Point pt = new Point(X(luma), Y(value));
            Brush fill = new SolidColorBrush(selected ? Colors.White : color);
            dc.DrawEllipse(fill, new Pen(new SolidColorBrush(Color.FromRgb(10, 12, 15)), 1), pt, selected ? 5 : 3.5, selected ? 5 : 3.5);
        }

        private BrightnessPoint HitTest(Point pos, out bool sat)
        {
            sat = false;
            double best = 10;
            BrightnessPoint found = null;
            foreach (BrightnessPoint p in Ordered())
            {
                if (Math.Abs(X(p.Luma) - pos.X) <= best && Math.Abs(Y(p.Gamma) - pos.Y) <= best)
                {
                    best = Math.Abs(Y(p.Gamma) - pos.Y);
                    found = p; sat = false;
                }
                if (ShowSaturation && Math.Abs(X(p.Luma) - pos.X) <= best && Math.Abs(Y(p.Saturation) - pos.Y) <= best)
                {
                    best = Math.Abs(Y(p.Saturation) - pos.Y);
                    found = p; sat = true;
                }
            }
            return found;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!Editable) return;
            Point pos = e.GetPosition(this);
            bool sat;
            BrightnessPoint hit = HitTest(pos, out sat);
            if (hit != null)
            {
                _drag = hit; _dragSat = sat;
                SelectedLuma = hit.Luma;
                CaptureMouse();
                InvalidateVisual();
                return;
            }
            // 双击空白 = 加点
            if (e.ClickCount == 2)
            {
                int luma = LumaAt(pos.X);
                if (Points.Exists(delegate(BrightnessPoint p) { return Math.Abs(p.Luma - luma) <= 2; })) return;
                Points.Add(new BrightnessPoint
                {
                    Luma = luma,
                    Gamma = Interp(luma, false),
                    Saturation = Interp(luma, true)
                });
                SelectedLuma = luma;
                if (Changed != null) Changed();
                InvalidateVisual();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag == null) return;
            Point pos = e.GetPosition(this);
            double v = ValueAt(pos.Y);
            if (_dragSat) _drag.Saturation = Math.Max(0.90, Math.Min(1.25, v));
            else
            {
                _drag.Gamma = Math.Max(0.20, Math.Min(1.60, v));
                if (AutoSaturation)
                    _drag.Saturation = GameBrightnessProfile.AutoSaturation(_drag.Gamma, CompensationBoost);
            }
            if (Changed != null) Changed();
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drag != null)
            {
                _drag = null;
                ReleaseMouseCapture();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (!Editable) return;
            bool sat;
            BrightnessPoint hit = HitTest(e.GetPosition(this), out sat);
            if (hit == null) return;
            if (Points.Count <= 3) return;
            Points.Remove(hit);
            SelectedLuma = -1;
            if (Changed != null) Changed();
            InvalidateVisual();
        }

        public void AddPoint(int luma)
        {
            luma = Math.Max(0, Math.Min(255, luma));
            if (Points.Exists(delegate(BrightnessPoint p) { return Math.Abs(p.Luma - luma) <= 2; })) return;
            Points.Add(new BrightnessPoint { Luma = luma, Gamma = Interp(luma, false), Saturation = Interp(luma, true) });
            SelectedLuma = luma;
            if (Changed != null) Changed();
            InvalidateVisual();
        }

        public BrightnessPoint SelectedPoint()
        {
            foreach (BrightnessPoint p in Points) if (p.Luma == SelectedLuma) return p;
            return null;
        }
    }

    /// <summary>预设下拉里的微型曲线（只读，两像素线，不画节点）。</summary>
    internal sealed class BrightnessCurveThumb : FrameworkElement
    {
        public static readonly DependencyProperty GammaTextProperty =
            DependencyProperty.Register("GammaText", typeof(string), typeof(BrightnessCurveThumb),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SatTextProperty =
            DependencyProperty.Register("SatText", typeof(string), typeof(BrightnessCurveThumb),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string GammaText
        {
            get { return (string)GetValue(GammaTextProperty); }
            set { SetValue(GammaTextProperty, value); }
        }

        public string SatText
        {
            get { return (string)GetValue(SatTextProperty); }
            set { SetValue(SatTextProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = RenderSize.Width, h = RenderSize.Height;
            if (w < 4 || h < 4) return;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x17, 0x1B, 0x21)), null, new Rect(0, 0, w, h));
            Draw(dc, GameBrightnessProfile.Parse(GammaText), Color.FromRgb(0x3C, 0xD2, 0xF5), w, h, false);
            Draw(dc, GameBrightnessProfile.Parse(SatText), Color.FromRgb(0xFF, 0xAD, 0x4A), w, h, true);
        }

        private static void Draw(DrawingContext dc, List<BrightnessPoint> pts, Color color, double w, double h, bool sat)
        {
            if (pts == null || pts.Count == 0) return;
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                bool first = true;
                for (int i = 0; i <= 32; i++)
                {
                    int luma = i * 255 / 32;
                    double v = Evaluate(pts, luma, sat);
                    double x = 1 + (w - 2) * luma / 255.0;
                    double y = h - 2 - (h - 4) * (Math.Max(0.2, Math.Min(1.6, v)) - 0.2) / 1.4;
                    if (first) { ctx.BeginFigure(new Point(x, y), false, false); first = false; }
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.2), geo);
        }

        private static double Evaluate(List<BrightnessPoint> p, int luma, bool sat)
        {
            if (luma <= p[0].Luma) return sat ? p[0].Saturation : p[0].Gamma;
            if (luma >= p[p.Count - 1].Luma) return sat ? p[p.Count - 1].Saturation : p[p.Count - 1].Gamma;
            for (int i = 0; i < p.Count - 1; i++)
            {
                if (luma < p[i + 1].Luma)
                {
                    double t = (double)(luma - p[i].Luma) / Math.Max(1, p[i + 1].Luma - p[i].Luma);
                    t = t * t * (3 - 2 * t);
                    double a = sat ? p[i].Saturation : p[i].Gamma;
                    double b = sat ? p[i + 1].Saturation : p[i + 1].Gamma;
                    return a + (b - a) * t;
                }
            }
            return sat ? p[p.Count - 1].Saturation : p[p.Count - 1].Gamma;
        }
    }

    /// <summary>预设下拉项：名称 + 两条曲线（供 DataTemplate 绑定）。</summary>
    internal sealed class BrightnessPresetItem
    {
        public string Name;
        public string Gamma;
        public string Sat;
        public override string ToString() { return Name; }
    }
}