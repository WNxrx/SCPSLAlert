using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SCPSLAlert
{

    internal struct TacPalette
    {
        public bool IsLight;
        public Color Bg;
        public Color Panel;
        public Color PanelAlt;
        public Color Line;
        public Color Ink;
        public Color Sub;
        public Color Muted;
        public Color Accent;
        public Color Accent2;
        public Color Danger;
        public Color Warn;
        public Color Good;
        public Color Mtf;
        public Color Chaos;

        private static Color C(System.Drawing.Color c)
        {
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        public static TacPalette Dark()
        {
            UiPalette t = ThemeTokens.Get(false);
            TacPalette p = new TacPalette();
            p.IsLight = false;
            p.Bg = C(t.PageBg);
            p.Panel = C(t.Panel);
            p.PanelAlt = C(t.PanelAlt);
            p.Line = C(t.Line);
            p.Ink = C(t.Text);
            p.Sub = C(t.Sub);
            p.Muted = C(t.Muted);
            p.Accent = C(ThemeTokens.For(false));
            p.Accent2 = C(t.Info);
            p.Danger = C(t.Danger);
            p.Warn = C(t.Warn);
            p.Good = C(t.Success);
            p.Mtf = Color.FromRgb(109, 159, 247);
            p.Chaos = Color.FromRgb(150, 196, 96);
            return p;
        }

        public static TacPalette Light()
        {
            UiPalette t = ThemeTokens.Get(true);
            TacPalette p = new TacPalette();
            p.IsLight = true;
            p.Bg = C(t.PageBg);
            p.Panel = C(t.Panel);
            p.PanelAlt = C(t.PanelAlt);
            p.Line = C(t.Line);
            p.Ink = C(t.Text);
            p.Sub = C(t.Sub);
            p.Muted = C(t.Muted);
            p.Accent = C(ThemeTokens.For(true));
            p.Accent2 = C(t.Info);
            p.Danger = C(t.Danger);
            p.Warn = C(t.Warn);
            p.Good = C(t.Success);
            p.Mtf = Color.FromRgb(36, 90, 186);
            p.Chaos = Color.FromRgb(70, 118, 40);
            return p;
        }
    }

    internal static class TacWidgets
    {
        public static readonly string[] Ids =
        {
            "hero", "actions", "scp", "pending", "summary", "gen", "waves", "timeline"
        };

        public const string Default = "hero,actions,scp,pending,summary";

        public static string Name(string id)
        {
            if (id == "hero") return "重生与下一波";
            if (id == "actions") return "操作按钮";
            if (id == "scp") return "场上 SCP";
            if (id == "pending") return "待归属阵亡";
            if (id == "summary") return "本局速览";
            if (id == "gen") return "SCP-079 发电机";
            if (id == "waves") return "本局刷新";
            if (id == "timeline") return "影响力时间线";
            return id;
        }

        public static string Desc(string id)
        {
            if (id == "hero") return "令牌、重生概率、影响力进度、下一波倒计时";
            if (id == "actions") return "刚刷了一波 / 校准 / 清空本局";
            if (id == "scp") return "每只 SCP 的存在状态与剩余血量";
            if (id == "pending") return "认不出击杀方的阵亡，手动归属";
            if (id == "summary") return "回合时间、击杀掉血、总影响力、最近刷新";
            if (id == "gen") return "三台发电机进度与阵营";
            if (id == "waves") return "本局刷新顺序，可改阵营 / 大小波";
            if (id == "timeline") return "最近登记过的目标与影响力变化";
            return "";
        }

        public static List<string> Parse(string text)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string id = parts[i].Trim();
                    if (id.Length == 0 || list.Contains(id)) continue;
                    if (Array.IndexOf(Ids, id) < 0) continue;
                    list.Add(id);
                }
            }
            if (list.Count == 0)
            {
                string[] def = Default.Split(',');
                for (int i = 0; i < def.Length; i++) list.Add(def[i]);
            }
            return list;
        }

        public static string Serialize(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return Default;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            return sb.ToString();
        }
    }

    internal static class TacAnim
    {
        public static int Fps
        {
            get
            {
                if (UiAnim.Quality >= 4) return 144;
                if (UiAnim.Quality >= 3) return 120;
                if (UiAnim.Quality == 2) return 90;
                return 60;
            }
        }

        public static void Fix(Timeline t)
        {
            if (t == null) return;
            try { Timeline.SetDesiredFrameRate(t, Fps); } catch { }
        }
    }

    internal static class TacColor
    {
        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, double t) { return Mix(c, Colors.White, t); }
        public static Color Darken(Color c, double t) { return Mix(c, Colors.Black, t); }

        public static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Alpha(Color c, int a)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb((byte)a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }

    internal static class TacUi
    {
        public static readonly FontFamily Ui = UiFonts.Body;
        public static readonly FontFamily DisplayFont = UiFonts.Title;
        public static readonly FontFamily MonoFont = UiFonts.Mono;
        public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        public static TextBlock T(string text, double size, Brush color)
        {
            TextBlock t = new TextBlock();
            t.Text = text == null ? "" : text;
            t.FontFamily = Ui;
            t.FontSize = size;
            t.Foreground = color;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static TextBlock T(string text, double size, Brush color, FontFamily family)
        {
            TextBlock t = T(text, size, color);
            t.FontFamily = family;
            return t;
        }

        public static TextBlock T(string text, double size, Brush color, FontFamily family, FontWeight weight)
        {
            TextBlock t = T(text, size, color, family);
            t.FontWeight = weight;
            return t;
        }

        public static TextBlock Mono(string text, double size, Brush color)
        {
            return T(text, size, color, MonoFont);
        }

        public static TextBlock Display(string text, double size, Brush color)
        {
            TextBlock t = T(text, size, color, DisplayFont);
            t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        public static TacNumber Number(string text, double size, Brush color, bool display)
        {
            TacNumber n = new TacNumber();
            n.Text = text == null ? "" : text;
            n.FontSize = size;
            n.Foreground = color;
            n.FontFamily = display ? DisplayFont : MonoFont;
            if (display) n.FontWeight = FontWeights.SemiBold;
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            return n;
        }

        public static Border Hair(Brush brush)
        {
            Border b = new Border();
            b.Height = 1;
            b.Background = brush;
            b.SnapsToDevicePixels = true;
            return b;
        }

        public static Border VHair(Brush brush)
        {
            Border b = new Border();
            b.Width = 1;
            b.Background = brush;
            b.SnapsToDevicePixels = true;
            return b;
        }

        public static StackPanel Stack(bool horizontal)
        {
            StackPanel s = new StackPanel();
            s.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
            return s;
        }

        public static Color ColorOf(Brush b)
        {
            SolidColorBrush s = b as SolidColorBrush;
            return s == null ? Colors.White : s.Color;
        }
    }

    internal sealed class TacGhostText : FrameworkElement
    {
        private string _text;
        private readonly double _size;
        private readonly Brush _brush;
        private readonly double _thickness;

        public TacGhostText(string text, double size, Brush brush, double thickness)
        {
            _text = text == null ? "" : text;
            _size = size;
            _brush = brush;
            _thickness = thickness;
            IsHitTestVisible = false;
        }

        public string Text
        {
            get { return _text; }
            set
            {
                if (_text == value) return;
                _text = value == null ? "" : value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(_text) || _size <= 0) return;
            try
            {
                FormattedText ft = new FormattedText(_text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Bahnschrift SemiCondensed"),
                    _size, Brushes.Black, 1.0);
                Geometry g = ft.BuildGeometry(new Point(0, 0));
                Pen pen = new Pen(_brush, _thickness);
                pen.LineJoin = PenLineJoin.Miter;
                dc.DrawGeometry(null, pen, g);
            }
            catch { }
        }
    }

    internal sealed class TacBackdrop : FrameworkElement
    {
        private readonly TacPalette _p;

        public TacBackdrop(TacPalette p)
        {
            _p = p;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            GeometryDrawing dot = new GeometryDrawing();
            dot.Brush = TacColor.Alpha(_p.Accent, _p.IsLight ? 26 : 22);
            dot.Geometry = new EllipseGeometry(new Point(1, 1), 1.1, 1.1);
            DrawingBrush grid = new DrawingBrush(dot);
            grid.Stretch = Stretch.None;
            grid.AlignmentX = AlignmentX.Left;
            grid.AlignmentY = AlignmentY.Top;
            grid.TileMode = TileMode.Tile;
            grid.Viewport = new Rect(0, 0, 26, 26);
            grid.ViewportUnits = BrushMappingMode.Absolute;
            dc.DrawRectangle(grid, null, new Rect(0, 0, w, h));

            try
            {
                FormattedText ghost = new FormattedText("TACTICAL", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Bahnschrift SemiCondensed"), 132,
                    Brushes.Black, 1.0);
                Geometry geo = ghost.BuildGeometry(new Point(w - 330, h - 196));
                dc.DrawGeometry(null, new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 16 : 18), 1.2), geo);
            }
            catch { }

            Pen corner = new Pen(TacColor.Alpha(_p.Ink, _p.IsLight ? 40 : 56), 1);
            dc.DrawLine(corner, new Point(14, 22), new Point(14, 14));
            dc.DrawLine(corner, new Point(14, 14), new Point(22, 14));
            dc.DrawLine(corner, new Point(w - 14, h - 22), new Point(w - 14, h - 14));
            dc.DrawLine(corner, new Point(w - 14, h - 14), new Point(w - 22, h - 14));
        }
    }

    internal sealed class TacOrbit : FrameworkElement
    {
        private readonly TacPalette _p;

        public TacOrbit(TacPalette p)
        {
            _p = p;
            IsHitTestVisible = false;
            RenderTransformOrigin = new Point(0.78, 0.28);
            CacheMode = new BitmapCache(1.0);
            if (!UiAnim.Off)
            {
                RotateTransform spin = new RotateTransform(0);
                RenderTransform = spin;
                DoubleAnimation a = new DoubleAnimation(0, 360,
                    new Duration(TimeSpan.FromSeconds(32)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                TacAnim.Fix(a);
                spin.BeginAnimation(RotateTransform.AngleProperty, a);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 40) return;
            double cx = w * 0.78;
            double cy = h * 0.28;

            Pen ring = new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 26 : 22), 0.8);
            ring.DashStyle = new DashStyle(new double[] { 2, 9 }, 0);
            for (int i = 0; i < 6; i++)
                dc.DrawEllipse(null, ring, new Point(cx, cy), 120 + i * 64, 120 + i * 64);

            Pen dot = new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 70 : 85), 2.2);
            for (int i = 0; i < 14; i++)
            {
                double a = i * Math.PI / 7 + 0.3;
                double r = 152 + (i % 3) * 64;
                dc.DrawEllipse(dot.Brush, null,
                    new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r), 1.8, 1.8);
            }
        }

        private static Geometry ArcGeometry(double cx, double cy, double r, double start, double sweep)
        {
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                int steps = 28;
                for (int i = 0; i <= steps; i++)
                {
                    double a = start + sweep * i / steps;
                    Point pt = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
                    if (i == 0) ctx.BeginFigure(pt, false, false);
                    else ctx.LineTo(pt, true, false);
                }
            }
            geo.Freeze();
            return geo;
        }
    }

    internal sealed class TacArc : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(TacArc),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty PulseProperty = DependencyProperty.Register(
            "Pulse", typeof(double), typeof(TacArc),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Track = Brushes.Gray;
        public Brush Fill = Brushes.White;
        public double Thickness = 3;
        public bool ShowTicks = true;

        private const double StartAngle = 135;
        private const double SweepAngle = 270;

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set
            {
                double v = Math.Max(0, Math.Min(1, value));
                BeginAnimation(ValueProperty, null);
                SetValue(ValueProperty, v);
            }
        }

        public void AnimateTo(double value, int ms)
        {
            double v = Math.Max(0, Math.Min(1, value));
            if (UiAnim.Off || ms <= 0)
            {
                Value = v;
                return;
            }
            double from = (double)GetValue(ValueProperty);
            if (Math.Abs(from - v) < 0.002) return;
            DoubleAnimation a = new DoubleAnimation(from, v, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(a);
            BeginAnimation(ValueProperty, a);
            if (Math.Abs(from - v) > 0.008)
            {
                BeginAnimation(PulseProperty, null);
                SetValue(PulseProperty, 1.0);
                DoubleAnimation pulse = new DoubleAnimation(1, 0,
                    new Duration(TimeSpan.FromMilliseconds(560)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                TacAnim.Fix(pulse);
                BeginAnimation(PulseProperty, pulse);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 20 || h < 20) return;
            double cx = w / 2;
            double cy = h / 2;
            double radius = Math.Min(w, h) / 2 - 8;
            if (radius < 8) return;
            double value = (double)GetValue(ValueProperty);

            if (ShowTicks)
            {
                Pen tick = new Pen(Tick, 1);
                for (int i = 0; i <= 45; i++)
                {
                    double deg = StartAngle + SweepAngle * i / 45.0;
                    double rad = deg * Math.PI / 180.0;
                    double len = (i % 5 == 0) ? 7 : 3.5;
                    dc.DrawLine(tick,
                        new Point(cx + Math.Cos(rad) * (radius + 3), cy + Math.Sin(rad) * (radius + 3)),
                        new Point(cx + Math.Cos(rad) * (radius + 3 + len), cy + Math.Sin(rad) * (radius + 3 + len)));
                }
            }

            DrawArc(dc, StartAngle, SweepAngle, Track, Thickness);
            if (value > 0.001)
            {
                DrawArc(dc, StartAngle, SweepAngle * value,
                    TacColor.Alpha(TacUi.ColorOf(Fill), 28), Thickness + 7);
                DrawArc(dc, StartAngle, SweepAngle * value, Fill, Thickness + 0.4);
                double endDeg = StartAngle + SweepAngle * value;
                double rad = endDeg * Math.PI / 180.0;
                Point end = new Point(cx + Math.Cos(rad) * radius, cy + Math.Sin(rad) * radius);
                dc.DrawEllipse(TacColor.Alpha(TacUi.ColorOf(Fill), 46), null, end, 7, 7);
                dc.DrawEllipse(Fill, null, end, 2.6, 2.6);
            }

            double pulse = (double)GetValue(PulseProperty);
            if (pulse > 0.01)
            {
                Pen halo = new Pen(TacColor.Alpha(TacUi.ColorOf(Fill), (int)(80 * pulse)), 1);
                dc.DrawEllipse(null, halo, new Point(cx, cy), radius + 10, radius + 10);
            }
        }

        private Brush _tickBrush = Brushes.Gray;
        public Brush Tick
        {
            get { return _tickBrush; }
            set { _tickBrush = value; }
        }

        private void DrawArc(DrawingContext dc, double startDeg, double sweepDeg, Brush brush, double thick)
        {
            if (sweepDeg <= 0.2) return;
            double w = ActualWidth;
            double h = ActualHeight;
            double cx = w / 2;
            double cy = h / 2;
            double radius = Math.Min(w, h) / 2 - 8;
            Pen pen = new Pen(brush, thick);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;

            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                int steps = Math.Max(2, (int)(sweepDeg / 2.5));
                for (int i = 0; i <= steps; i++)
                {
                    double deg = startDeg + sweepDeg * i / steps;
                    double rad = deg * Math.PI / 180.0;
                    Point pt = new Point(cx + Math.Cos(rad) * radius, cy + Math.Sin(rad) * radius);
                    if (i == 0) ctx.BeginFigure(pt, false, false);
                    else ctx.LineTo(pt, true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    internal sealed class TacMeter : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(TacMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GhostProperty = DependencyProperty.Register(
            "Ghost", typeof(double), typeof(TacMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Track = Brushes.Gray;
        public Brush Fill = Brushes.White;
        public Brush GhostFill = Brushes.Transparent;
        public double BarThickness = 4;

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set
            {
                double v = Math.Max(0, Math.Min(1, value));
                BeginAnimation(ValueProperty, null);
                SetValue(ValueProperty, v);
                SetValue(GhostProperty, v);
            }
        }

        public void AnimateTo(double value, int ms)
        {
            double v = Math.Max(0, Math.Min(1, value));
            if (UiAnim.Off || ms <= 0)
            {
                Value = v;
                return;
            }
            double from = (double)GetValue(ValueProperty);
            double oldGhost = (double)GetValue(GhostProperty);

            if (v < oldGhost - 0.004)
            {
                BeginAnimation(GhostProperty, null);
                SetValue(GhostProperty, oldGhost);
                DoubleAnimation ghost = new DoubleAnimation(oldGhost, v,
                    new Duration(TimeSpan.FromMilliseconds(ms + 420)));
                ghost.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                TacAnim.Fix(ghost);
                BeginAnimation(GhostProperty, ghost);
            }
            else if (v > oldGhost)
            {
                BeginAnimation(GhostProperty, null);
                SetValue(GhostProperty, v);
            }

            if (Math.Abs(from - v) < 0.002) return;
            DoubleAnimation a = new DoubleAnimation(from, v, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
            TacAnim.Fix(a);
            BeginAnimation(ValueProperty, a);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 2 || h < 1) return;
            double bar = Math.Min(BarThickness, h);
            double y = (h - bar) / 2;
            double value = (double)GetValue(ValueProperty);
            double ghost = (double)GetValue(GhostProperty);

            dc.DrawRectangle(Track, null, new Rect(0, y, w, bar));
            double gw = w * ghost;
            if (gw > value * w + 0.6 && GhostFill != null)
                dc.DrawRectangle(GhostFill, null, new Rect(w * value, y, gw - w * value, bar));
            double fw = w * value;
            if (fw > 0.6) dc.DrawRectangle(Fill, null, new Rect(0, y, fw, bar));
        }
    }

    internal enum TacKind
    {
        Primary,
        Ghost,
        Danger,
        Quiet
    }

    internal sealed class TacButton : Button
    {
        private readonly SolidColorBrush _fill = new SolidColorBrush();
        private readonly SolidColorBrush _stroke = new SolidColorBrush();
        private readonly SolidColorBrush _fore = new SolidColorBrush();
        private Color _fillBase, _fillHover, _fillPress;
        private Color _strokeBase, _strokeHover;
        private Color _foreBase, _foreHover;
        private bool _hover;
        private readonly int _ms = 110;
        private readonly bool _animated;

        public TextBlock Label;
        public readonly TacKind Kind;

        public TacButton(string text, TacKind kind, TacPalette p, double height)
            : this(text, kind, p, height, 12) { }

        public TacButton(string text, TacKind kind, TacPalette p, double height, double padX)
        {
            Kind = kind;
            _animated = !UiAnim.Off;
            Height = height;
            MinHeight = height;
            Padding = new Thickness(padX, 0, padX, 0);
            BorderThickness = new Thickness(1);
            Background = _fill;
            BorderBrush = _stroke;
            Foreground = _fore;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            FontFamily = TacUi.Ui;
            FontSize = 11.5;
            Template = BuildTemplate();

            if (kind == TacKind.Primary)
            {
                _fillBase = p.Accent;
                _fillHover = TacColor.Mix(p.Accent, p.IsLight ? Colors.White : Colors.Black, p.IsLight ? 0.18 : 0.10);
                _fillPress = TacColor.Darken(p.Accent, p.IsLight ? 0.08 : 0.16);
                _strokeBase = p.Accent;
                _strokeHover = TacColor.Lighten(p.Accent, 0.30);
                _foreBase = p.IsLight ? Colors.White : Color.FromRgb(6, 20, 26);
                _foreHover = _foreBase;
                FontWeight = FontWeights.SemiBold;
            }
            else if (kind == TacKind.Danger)
            {
                _fillBase = Colors.Transparent;
                _fillHover = TacColor.Alpha(p.Danger, p.IsLight ? 26 : 34).Color;
                _fillPress = TacColor.Alpha(p.Danger, p.IsLight ? 40 : 54).Color;
                _strokeBase = TacColor.Alpha(p.Danger, 150).Color;
                _strokeHover = p.Danger;
                _foreBase = p.Danger;
                _foreHover = p.Danger;
            }
            else if (kind == TacKind.Quiet)
            {
                _fillBase = Colors.Transparent;
                _fillHover = TacColor.Alpha(p.Ink, p.IsLight ? 16 : 22).Color;
                _fillPress = TacColor.Alpha(p.Ink, p.IsLight ? 28 : 34).Color;
                _strokeBase = Colors.Transparent;
                _strokeHover = TacColor.Alpha(p.Ink, 60).Color;
                _foreBase = p.Sub;
                _foreHover = p.Ink;
            }
            else
            {
                _fillBase = p.PanelAlt;
                _fillHover = TacColor.Mix(p.PanelAlt, p.Accent, p.IsLight ? 0.14 : 0.20);
                _fillPress = TacColor.Mix(p.PanelAlt, p.Accent, 0.30);
                _strokeBase = p.Line;
                _strokeHover = p.Accent;
                _foreBase = p.Ink;
                _foreHover = p.Ink;
            }

            Label = TacUi.T(text, 11.5, _fore, TacUi.Ui,
                kind == TacKind.Primary ? FontWeights.SemiBold : FontWeights.Normal);
            Content = Label;
            Apply(_fillBase, _strokeBase, _foreBase);
            IsEnabledChanged += delegate
            {
                Opacity = IsEnabled ? 1 : 0.42;
                Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
            };
        }

        private static ControlTemplate BuildTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty,
                new System.Windows.Data.Binding("HorizontalContentAlignment")
                {
                    RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
                });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty,
                new System.Windows.Data.Binding("VerticalContentAlignment")
                {
                    RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
                });
            border.AppendChild(presenter);
            ControlTemplate t = new ControlTemplate(typeof(Button));
            t.VisualTree = border;
            return t;
        }

        public void SetText(string text)
        {
            if (Label != null) Label.Text = text == null ? "" : text;
        }

        public void SetForegroundColor(Color c)
        {
            _foreBase = c;
            _foreHover = c;
            _fore.Color = c;
        }

        public void SetColors(Color fill, Color fillHover, Color stroke, Color fore)
        {
            _fillBase = fill;
            _fillHover = fillHover;
            _fillPress = fillHover;
            _strokeBase = stroke;
            _strokeHover = stroke;
            _foreBase = fore;
            _foreHover = fore;
            Apply(_fillBase, _strokeBase, _foreBase);
        }

        private void Apply(Color fill, Color stroke, Color fore)
        {
            _fill.Color = fill;
            _stroke.Color = stroke;
            _fore.Color = fore;
            if (Label != null) Label.Foreground = _fore;
        }

        private void Fade(Color fill, Color stroke, Color fore, int ms)
        {
            if (!_animated)
            {
                Apply(fill, stroke, fore);
                return;
            }
            ColorAnimation f = new ColorAnimation(fill, new Duration(TimeSpan.FromMilliseconds(ms)));
            f.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(f);
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, f);
            ColorAnimation s = new ColorAnimation(stroke, new Duration(TimeSpan.FromMilliseconds(ms)));
            TacAnim.Fix(s);
            _stroke.BeginAnimation(SolidColorBrush.ColorProperty, s);
            _fore.Color = fore;
            if (Label != null) Label.Foreground = _fore;
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            if (IsEnabled) Fade(_fillHover, _strokeHover, _foreHover, _ms);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            if (!IsEnabled) return;
            Fade(_fillBase, IsKeyboardFocused ? _strokeHover : _strokeBase, _foreBase, 150);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!IsEnabled) return;
            Fade(_fillPress, _strokeBase, _foreBase, 70);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsEnabled) return;
            Fade(_hover ? _fillHover : _fillBase, _hover ? _strokeHover : _strokeBase,
                _hover ? _foreHover : _foreBase, 130);
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            if (IsEnabled) Fade(_fillBase, _strokeHover, _foreBase, 80);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            if (IsEnabled) Fade(_fillBase, _strokeBase, _foreBase, 80);
        }
    }

    internal sealed class TacInput : Grid
    {
        public readonly TextBox Box;
        private readonly Color _lineBase;
        private readonly Color _lineFocus;

        public TacInput(TacPalette p, double width, double height, bool mono)
            : this(p, width, height, mono, "") { }

        public TacInput(TacPalette p, double width, double height, bool mono, string unit)
        {
            Width = width;
            Height = height;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _lineBase = p.Line;
            _lineFocus = p.Accent;

            Border box = new Border();
            box.Background = TacColor.Alpha(p.Ink, p.IsLight ? 10 : 14);
            box.BorderBrush = TacColor.Alpha(p.Line, p.IsLight ? 200 : 230);
            box.BorderThickness = new Thickness(0, 0, 0, 1);
            box.Padding = new Thickness(5, 0, string.IsNullOrEmpty(unit) ? 4 : 20, 0);
            Grid.SetColumnSpan(box, 2);
            Children.Add(box);

            Box = new TextBox();
            Box.BorderThickness = new Thickness(0);
            Box.Background = Brushes.Transparent;
            Box.Foreground = TacColor.Solid(p.Ink);
            Box.CaretBrush = TacColor.Solid(p.Accent);
            Box.SelectionBrush = TacColor.Alpha(p.Accent, 90);
            Box.FontFamily = mono ? TacUi.MonoFont : TacUi.Ui;
            Box.FontSize = mono ? 12.5 : 11.5;
            Box.Padding = new Thickness(0);
            Box.VerticalContentAlignment = VerticalAlignment.Center;
            Box.FocusVisualStyle = null;
            box.Child = Box;

            if (!string.IsNullOrEmpty(unit))
            {
                TextBlock u = TacUi.Mono(unit, 8.5, TacColor.Solid(p.Muted));
                u.VerticalAlignment = VerticalAlignment.Center;
                u.Margin = new Thickness(3, 0, 4, 0);
                u.IsHitTestVisible = false;
                Grid.SetColumn(u, 1);
                Children.Add(u);
            }

            Box.GotKeyboardFocus += delegate
            {
                box.BorderBrush = TacColor.Solid(_lineFocus);
                Box.SelectAll();
            };
            Box.LostKeyboardFocus += delegate
            {
                box.BorderBrush = TacColor.Solid(_lineBase);
            };
        }

        public string Text
        {
            get { return Box.Text; }
            set
            {
                if (Box.Text == value) return;
                Box.Text = value == null ? "" : value;
            }
        }
    }

    internal sealed class TacNumber : TextBlock
    {
        public static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
            "Display", typeof(double), typeof(TacNumber),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayChanged));

        private string _format = "0";
        private string _suffix = "";
        private bool _hasTarget;
        private double _targetValue;
        public bool Animate = true;

        private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            TacNumber n = (TacNumber)d;
            n.Text = n.Format((double)e.NewValue);
        }

        private string Format(double v)
        {
            string s = _format == "0"
                ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                : v.ToString(_format, CultureInfo.InvariantCulture);
            return s + _suffix;
        }

        public void SetValue(double value, string format, string suffix, int ms)
        {
            _format = format;
            _suffix = suffix == null ? "" : suffix;
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;

            string want = Format(value);
            if (Text == want)
            {
                _targetValue = value;
                _hasTarget = true;
                return;
            }
            if (_hasTarget && Math.Abs(_targetValue - value) < 0.0001) return;

            if (!Animate || UiAnim.Off || ms <= 0)
            {
                BeginAnimation(DisplayProperty, null);
                SetValue(DisplayProperty, value);
                _targetValue = value;
                _hasTarget = true;
                return;
            }

            double from = (double)GetValue(DisplayProperty);
            if (Math.Abs(from - value) < 0.05)
            {
                BeginAnimation(DisplayProperty, null);
                SetValue(DisplayProperty, value);
                _targetValue = value;
                _hasTarget = true;
                return;
            }
            DoubleAnimation a = new DoubleAnimation(from, value, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(a);
            BeginAnimation(DisplayProperty, a);
            _targetValue = value;
            _hasTarget = true;
        }

        public void SetText(string text, int pulseMs)
        {
            if (Text == text) return;
            Text = text == null ? "" : text;
            if (pulseMs > 0 && Animate && !UiAnim.Off)
            {
                DoubleAnimation pulse = new DoubleAnimation(0.35, 1,
                    new Duration(TimeSpan.FromMilliseconds(pulseMs)));
                TacAnim.Fix(pulse);
                BeginAnimation(OpacityProperty, pulse);
            }
        }

        public void Tick(int ms)
        {
            if (!Animate || UiAnim.Off || ms <= 0) return;
            TranslateTransform t = RenderTransform as TranslateTransform;
            if (t == null)
            {
                t = new TranslateTransform();
                RenderTransform = t;
            }
            DoubleAnimation move = new DoubleAnimation(2.4, 0,
                new Duration(TimeSpan.FromMilliseconds(ms)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(move);
            t.BeginAnimation(TranslateTransform.YProperty, move);
            DoubleAnimation fade = new DoubleAnimation(0.62, 1,
                new Duration(TimeSpan.FromMilliseconds(ms)));
            TacAnim.Fix(fade);
            BeginAnimation(OpacityProperty, fade);
        }

        public void ResetToZero()
        {
            BeginAnimation(DisplayProperty, null);
            BeginAnimation(OpacityProperty, null);
            _hasTarget = false;
            SetValue(DisplayProperty, 0.0);
        }
    }

    internal sealed class TacRadar : FrameworkElement
    {
        private readonly TacPalette _p;

        public TacRadar(TacPalette p)
        {
            _p = p;
            IsHitTestVisible = false;
            RenderTransformOrigin = new Point(0.5, 0.5);
            CacheMode = new BitmapCache(1.0);
            if (!UiAnim.Off)
            {
                RotateTransform spin = new RotateTransform(0);
                RenderTransform = spin;
                DoubleAnimation a = new DoubleAnimation(0, 360,
                    new Duration(TimeSpan.FromSeconds(24)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                TacAnim.Fix(a);
                spin.BeginAnimation(RotateTransform.AngleProperty, a);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 30 || h < 30) return;
            double cx = w / 2;
            double cy = h / 2;
            double r = Math.Min(w, h) / 2 - 6;

            Pen dashed = new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 40 : 46), 0.8);
            dashed.DashStyle = new DashStyle(new double[] { 2, 6 }, 0);
            dc.DrawEllipse(null, dashed, new Point(cx, cy), r * 0.55, r * 0.55);
            dc.DrawEllipse(null, dashed, new Point(cx, cy), r * 0.78, r * 0.78);
            dc.DrawEllipse(null, dashed, new Point(cx, cy), r * 0.97, r * 0.97);

            Pen tick = new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 34 : 40), 0.8);
            for (int i = 0; i < 48; i++)
            {
                double a = i * Math.PI / 24;
                double len = (i % 4 == 0) ? 5 : 2;
                dc.DrawLine(tick,
                    new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r),
                    new Point(cx + Math.Cos(a) * (r + len), cy + Math.Sin(a) * (r + len)));
            }

            double lead = -Math.PI / 2;
            for (int i = 0; i < 16; i++)
            {
                double a = lead - i * Math.PI / 180 * 1.7;
                int alpha = (int)(4 + (16 - i) * 2.2);
                Pen tail = new Pen(TacColor.Alpha(_p.Accent, alpha), 1.2);
                tail.StartLineCap = PenLineCap.Round;
                dc.DrawLine(tail, new Point(cx, cy),
                    new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
            }
            Pen leadPen = new Pen(TacColor.Alpha(_p.Accent, _p.IsLight ? 130 : 170), 1.2);
            dc.DrawLine(leadPen, new Point(cx, cy),
                new Point(cx + Math.Cos(lead) * r, cy + Math.Sin(lead) * r));
        }
    }

    internal sealed class TacScanlines : FrameworkElement
    {
        private readonly TacPalette _p;

        public TacScanlines(TacPalette p)
        {
            _p = p;
            IsHitTestVisible = false;
            CacheMode = new BitmapCache(1.0);
            if (!UiAnim.Off && UiAnim.Quality >= 3)
            {
                TranslateTransform drift = new TranslateTransform(0, 0);
                RenderTransform = drift;
                DoubleAnimation a = new DoubleAnimation(0, 3,
                    new Duration(TimeSpan.FromSeconds(6)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                Timeline.SetDesiredFrameRate(a, 12);
                drift.BeginAnimation(TranslateTransform.YProperty, a);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;
            Pen pen = new Pen(TacColor.Alpha(_p.Ink, _p.IsLight ? 8 : 10), 1);
            for (double y = 0; y < h; y += 3)
                dc.DrawLine(pen, new Point(0, y), new Point(w, y));
        }
    }

    internal sealed class TacDot : FrameworkElement
    {
        public Brush Fill = Brushes.White;
        public bool Pulse;

        public TacDot(Brush fill, bool pulse)
        {
            Fill = fill;
            Pulse = pulse;
            Width = 7;
            Height = 7;
            IsHitTestVisible = false;
            if (pulse && !UiAnim.Off)
            {
                DoubleAnimation a = new DoubleAnimation(1.0, 0.28, new Duration(TimeSpan.FromMilliseconds(900)));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                TacAnim.Fix(a);
                BeginAnimation(OpacityProperty, a);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawEllipse(Fill, null, new Point(ActualWidth / 2, ActualHeight / 2),
                Math.Min(ActualWidth, ActualHeight) / 2, Math.Min(ActualWidth, ActualHeight) / 2);
        }
    }
}
