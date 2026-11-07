using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SCPSLAlert;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 8 批追加：高自由度漂浮文字。
    ///
    /// 一句文字拆成逐字 TextBlock，每个字可以有独立颜色、抖动幅度/速度/相位、
    /// 缩放和旋转；整句还有一层整体抖动范围。窗口无框、置顶、点击穿透，
    /// 不参与普通提示避让。
    /// </summary>
    internal static class FloatingTextOverlay
    {
        private const double Margin = 100;

        private sealed class Entry
        {
            public Window Window;
            public Canvas Canvas;
            public string Key;
            public List<TextBlock> Blocks = new List<TextBlock>();
            public List<TriggerCharStyle> Styles = new List<TriggerCharStyle>();
            public List<System.Windows.Point> Base = new List<System.Windows.Point>();
            public TriggerAction Action;
            public DispatcherTimer Timer;
            public DateTime Start;
            public bool Closing;
        }

        private static readonly Dictionary<string, Entry> Live =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static void Show(string key, TriggerAction a, Config cfg)
        {
            if (a == null || string.IsNullOrEmpty(a.Text)) return;
            try
            {
                HideCard(key);
                Entry e = Build(key, a, cfg);
                if (e == null) return;
                e.Key = key;
                Live[key] = e;
                e.Window.Show();
                try
                {
                    NativeMethods.HideFromCapture(
                        new System.Windows.Interop.WindowInteropHelper(e.Window).Handle, "漂浮文字");
                }
                catch { }
            }
            catch (Exception ex) { Logger.Err("漂浮文字", "显示失败", ex); }
        }

        public static void HideCard(string key)
        {
            Entry e;
            if (!Live.TryGetValue(key, out e)) return;
            Live.Remove(key);
            Close(e);
        }

        /// <summary>停止条件按卡片整体收起时，收起该卡片下所有漂浮文字动作。</summary>
        public static void HideCardPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            List<string> keys = new List<string>();
            foreach (KeyValuePair<string, Entry> p in Live)
                if (p.Key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(p.Key);
            for (int i = 0; i < keys.Count; i++) HideCard(keys[i]);
        }

        public static void HideAll()
        {
            List<string> keys = new List<string>(Live.Keys);
            for (int i = 0; i < keys.Count; i++) HideCard(keys[i]);
        }

        private static void Close(Entry e)
        {
            if (e == null) return;
            e.Closing = true;
            if (!string.IsNullOrEmpty(e.Key)) Live.Remove(e.Key);
            try { if (e.Timer != null) e.Timer.Stop(); } catch { }
            try { e.Window.Close(); } catch { }
        }

        private static Entry Build(string key, TriggerAction a, Config cfg)
        {
            string text = a.Text ?? "";
            if (text.Length == 0) return null;

            Entry e = new Entry();
            e.Action = a;
            e.Window = new Window();
            e.Window.WindowStyle = System.Windows.WindowStyle.None;
            e.Window.AllowsTransparency = true;
            e.Window.Background = Brushes.Transparent;
            e.Window.Topmost = true;
            e.Window.ShowInTaskbar = false;
            e.Window.ShowActivated = false;
            e.Window.ResizeMode = ResizeMode.NoResize;
            e.Window.Focusable = false;
            e.Window.IsHitTestVisible = false;

            string familyName = string.IsNullOrEmpty(a.Font) ? "Microsoft YaHei UI" : a.Font;
            FontFamily family = new FontFamily(familyName);
            double size = Math.Max(8, Math.Min(120, a.FontSize <= 0 ? 32 : a.FontSize));
            double opacity = Math.Max(0.05, Math.Min(1, a.Opacity <= 0 ? 1 : a.Opacity));

            e.Canvas = new Canvas();
            e.Canvas.Background = Brushes.Transparent;
            e.Canvas.IsHitTestVisible = false;
            e.Canvas.RenderTransform = new TranslateTransform(0, 0);

            double x = Margin;
            double maxH = size * 1.6;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                TextBlock tb = new TextBlock();
                tb.Text = ch == ' ' ? " " : ch.ToString();
                tb.FontFamily = family;
                tb.FontSize = size;
                tb.FontWeight = FontWeights.Bold;
                tb.Opacity = 0;
                if (a.Outline)
                {
                    System.Windows.Media.Effects.DropShadowEffect ds =
                        new System.Windows.Media.Effects.DropShadowEffect();
                    ds.ShadowDepth = 0;
                    ds.BlurRadius = 2;
                    ds.Color = Colors.Black;
                    ds.Opacity = 0.9;
                    tb.Effect = ds;
                }

                TriggerCharStyle style = a.StyleFor(i);
                Color c = ParseColor(string.IsNullOrEmpty(style.Color) ? a.Color1 : style.Color,
                    Colors.White);
                tb.Foreground = new SolidColorBrush(c);
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = Math.Max(1, tb.DesiredSize.Width);
                double h = Math.Max(1, tb.DesiredSize.Height);
                maxH = Math.Max(maxH, h);

                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, Margin);
                e.Canvas.Children.Add(tb);
                e.Blocks.Add(tb);
                e.Styles.Add(style);
                e.Base.Add(new System.Windows.Point(x, Margin));
                x += w;
            }

            double width = Math.Max(40, x - Margin + Margin * 2);
            double height = maxH + Margin * 2;
            e.Window.Width = width;
            e.Window.Height = height;
            e.Window.Content = e.Canvas;

            System.Drawing.Rectangle basis;
            IntPtr game = ScreenGeometry.FindGameWindow();
            if (game != IntPtr.Zero) basis = BrightnessSampler.ClientBounds(game);
            else basis = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            if (basis.Width <= 0) basis = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            double left = basis.Left + basis.Width * Clamp01(a.XPercent) - width / 2;
            double top = basis.Top + basis.Height * Clamp01(a.YPercent) - height / 2;
            e.Window.WindowStartupLocation = WindowStartupLocation.Manual;
            e.Window.Left = left;
            e.Window.Top = top;

            e.Start = DateTime.UtcNow;
            e.Timer = new DispatcherTimer(DispatcherPriority.Render);
            e.Timer.Interval = TimeSpan.FromMilliseconds(33);
            e.Timer.Tick += delegate { Tick(e, opacity); };
            e.Timer.Start();
            return e;
        }

        private static void Tick(Entry e, double opacity)
        {
            if (e == null || e.Closing) return;
            double elapsed = (DateTime.UtcNow - e.Start).TotalMilliseconds;
            double gx = Math.Sin(elapsed / 1000.0 * Math.PI * 2 * Math.Max(0.1, e.Action.FloatJitterSpeed)) * e.Action.FloatJitterX;
            double gy = Math.Cos(elapsed / 1000.0 * Math.PI * 2 * Math.Max(0.1, e.Action.FloatJitterSpeed) * 0.83) * e.Action.FloatJitterY;
            TranslateTransform tt = e.Canvas.RenderTransform as TranslateTransform;
            if (tt != null)
            {
                tt.X = gx + ScreenShake.OffsetX;
                tt.Y = gy + ScreenShake.OffsetY;
            }

            double life = e.Action.FloatLifeMs;
            double fade = 1.0;
            if (life > 0 && elapsed > life - 320)
                fade = Math.Max(0, (life - elapsed) / 320.0);
            if (life > 0 && elapsed >= life)
            {
                Close(e);
                return;
            }

            for (int i = 0; i < e.Blocks.Count; i++)
            {
                TextBlock tb = e.Blocks[i];
                TriggerCharStyle s = e.Styles[i];
                double speed = Math.Max(0.1, s.Speed);
                double phase = s.Phase < 0 ? i * 0.7 : s.Phase;
                double jx = Math.Sin(elapsed / 1000.0 * Math.PI * 2 * speed + phase) * s.JitterX;
                double jy = Math.Cos(elapsed / 1000.0 * Math.PI * 2 * speed * 1.21 + phase * 1.3) * s.JitterY;
                System.Windows.Point b = e.Base[i];
                Canvas.SetLeft(tb, b.X + jx);
                Canvas.SetTop(tb, b.Y + jy);

                double reveal = 1.0;
                if (e.Action.FloatReveal)
                {
                    double d = Math.Max(0, e.Action.FloatRevealDelayMs);
                    reveal = Clamp01((elapsed - i * d) / 160.0);
                }
                tb.Opacity = Math.Max(0, Math.Min(1, reveal * opacity * fade));

                if (Math.Abs(s.Scale - 1) > 0.001 || Math.Abs(s.Rotate) > 0.001)
                {
                    TransformGroup g = new TransformGroup();
                    g.Children.Add(new ScaleTransform(s.Scale, s.Scale));
                    g.Children.Add(new RotateTransform(s.Rotate));
                    tb.RenderTransform = g;
                    tb.RenderTransformOrigin = new Point(0.5, 0.5);
                }
            }
        }

        private static Color ParseColor(string s, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return fallback;
                return (Color)ColorConverter.ConvertFromString(s);
            }
            catch { return fallback; }
        }

        private static double Clamp01(double v)
        {
            if (double.IsNaN(v)) return 0.5;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }
    }

    /// <summary>
    /// 第 8 批追加：让普通提示和漂浮文字一起抖。
    /// 外部程序无法抖游戏画面，这里只抖本软件的提示表面/特效层。
    /// </summary>
    internal static class ScreenShake
    {
        public static bool Active { get; private set; }
        public static double OffsetX { get; private set; }
        public static double OffsetY { get; private set; }

        private static System.Windows.Forms.Timer _timer;
        private static DateTime _start;
        private static int _duration;
        private static int _amplitude;
        private static IAlertSurface[] _surfaces;
        private static List<System.Drawing.Point> _base;
        private static List<IAlertSurface> _live;

        public static void Begin(Config cfg, IAlertSurface[] surfaces, int amplitude, int durationMs)
        {
            if (Active) return;
            _surfaces = surfaces;
            _amplitude = Math.Max(0, Math.Min(24, amplitude));
            _duration = Math.Max(60, Math.Min(1200, durationMs));
            _live = new List<IAlertSurface>();
            _base = new List<System.Drawing.Point>();
            if (_surfaces != null)
            {
                for (int i = 0; i < _surfaces.Length; i++)
                {
                    try
                    {
                        if (_surfaces[i] != null && _surfaces[i].IsPopped)
                        {
                            System.Drawing.Point p = _surfaces[i].DisplayBounds.Location;
                            _live.Add(_surfaces[i]);
                            _base.Add(p);
                        }
                    }
                    catch { }
                }
            }
            _start = DateTime.UtcNow;
            Active = true;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 16;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            double t = (DateTime.UtcNow - _start).TotalMilliseconds / _duration;
            if (t < 0) t = 0;
            if (t >= 1)
            {
                End();
                return;
            }
            double env = 1 - t;
            double dx = Math.Sin(t * 42.0) * _amplitude * env;
            double dy = Math.Cos(t * 31.0) * _amplitude * env;
            OffsetX = dx;
            OffsetY = dy;
            for (int i = 0; i < _live.Count; i++)
            {
                try
                {
                    System.Drawing.Point b = _base[i];
                    _live[i].SetDisplayPosition((int)Math.Round(b.X + dx), (int)Math.Round(b.Y + dy));
                }
                catch { }
            }
        }

        private static void End()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            for (int i = 0; i < _live.Count; i++)
            {
                try
                {
                    System.Drawing.Point b = _base[i];
                    _live[i].SetDisplayPosition(b.X, b.Y);
                }
                catch { }
            }
            _live.Clear();
            _base.Clear();
            OffsetX = 0;
            OffsetY = 0;
            Active = false;
        }

        public static void Stop()
        {
            if (Active) End();
        }
    }
}
