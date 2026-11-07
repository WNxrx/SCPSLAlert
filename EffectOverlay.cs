using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 8 批追加：可单独添加的屏幕特效。
    /// 只作用于本软件覆盖层，不修改游戏画面；闪屏有强度/频率上限。
    /// </summary>
    internal static class ScreenEffects
    {
        private static DateTime _lastFlash = DateTime.MinValue;

        public static void Play(TriggerEffect e, Config cfg, IAlertSurface[] forms)
        {
            if (e == null) return;
            if (e.Type == "shake")
            {
                ScreenShake.Begin(cfg, forms, e.Amplitude, e.DurationMs);
                return;
            }
            if (e.Type == "flash")
            {
                // 不连续强闪：两次闪屏至少间隔 400ms。
                if ((DateTime.UtcNow - _lastFlash).TotalMilliseconds < 400) return;
                _lastFlash = DateTime.UtcNow;
                Flash(e);
                return;
            }
            if (e.Type == "pulse") { Pulse(e); return; }
            if (e.Type == "confetti") Confetti(e);
        }

        private static Window BaseWindow(System.Drawing.Rectangle basis)
        {
            Window w = new Window();
            w.WindowStyle = System.Windows.WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.Topmost = true;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.ResizeMode = ResizeMode.NoResize;
            w.Focusable = false;
            w.IsHitTestVisible = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = basis.Left;
            w.Top = basis.Top;
            w.Width = Math.Max(80, basis.Width);
            w.Height = Math.Max(80, basis.Height);
            return w;
        }

        private static System.Drawing.Rectangle Basis()
        {
            IntPtr game = ScreenGeometry.FindGameWindow();
            System.Drawing.Rectangle r = game != IntPtr.Zero
                ? BrightnessSampler.ClientBounds(game)
                : System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            if (r.Width <= 0) r = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            return r;
        }

        private static void HideFromCapture(Window w)
        {
            try
            {
                NativeMethods.HideFromCapture(
                    new System.Windows.Interop.WindowInteropHelper(w).Handle, "屏幕特效");
            }
            catch { }
        }

        private static void Flash(TriggerEffect e)
        {
            try
            {
                double intensity = Math.Max(0.03, Math.Min(0.35, e.Intensity));
                int duration = Math.Max(60, Math.Min(400, e.DurationMs));
                Window w = BaseWindow(Basis());
                w.Background = new SolidColorBrush(ParseColor(e.Color1, Colors.White));
                w.Opacity = 0;
                w.Show();
                HideFromCapture(w);
                DoubleAnimation anim = new DoubleAnimation(0, intensity,
                    new Duration(TimeSpan.FromMilliseconds(duration / 2.0)));
                anim.AutoReverse = true;
                w.BeginAnimation(UIElement.OpacityProperty, anim);
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(duration + 40);
                t.Tick += delegate { t.Stop(); try { w.Close(); } catch { } };
                t.Start();
            }
            catch (Exception ex) { Logger.Err("特效", "闪屏失败", ex); }
        }

        private static void Pulse(TriggerEffect e)
        {
            try
            {
                double intensity = Math.Max(0.05, Math.Min(0.8, e.Intensity));
                int duration = Math.Max(120, Math.Min(1600, e.DurationMs));
                Window w = BaseWindow(Basis());
                Border border = new Border();
                border.BorderThickness = new Thickness(14);
                border.BorderBrush = new SolidColorBrush(ParseColor(e.Color1, Color.FromRgb(67, 207, 187)));
                border.Background = Brushes.Transparent;
                border.Opacity = 0;
                w.Content = border;
                w.Show();
                HideFromCapture(w);
                DoubleAnimation anim = new DoubleAnimation(0, intensity,
                    new Duration(TimeSpan.FromMilliseconds(duration * 0.35)));
                anim.AutoReverse = true;
                border.BeginAnimation(UIElement.OpacityProperty, anim);
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(duration + 40);
                t.Tick += delegate { t.Stop(); try { w.Close(); } catch { } };
                t.Start();
            }
            catch (Exception ex) { Logger.Err("特效", "边缘脉冲失败", ex); }
        }

        private static void Confetti(TriggerEffect e)
        {
            try
            {
                int count = Math.Max(10, Math.Min(120, e.Count));
                int duration = Math.Max(400, Math.Min(1800, e.DurationMs));
                System.Drawing.Rectangle basis = Basis();
                Window w = BaseWindow(basis);
                Canvas canvas = new Canvas();
                canvas.Width = w.Width;
                canvas.Height = w.Height;
                w.Content = canvas;
                w.Show();
                HideFromCapture(w);

                Color c1 = ParseColor(e.Color1, Color.FromRgb(255, 255, 255));
                Color c2 = ParseColor(e.Color2, Color.FromRgb(67, 207, 187));
                Random rnd = new Random();
                List<Rectangle> parts = new List<Rectangle>();
                List<double> vx = new List<double>();
                List<double> vy = new List<double>();
                List<double> rot = new List<double>();
                for (int i = 0; i < count; i++)
                {
                    Rectangle r = new Rectangle();
                    r.Width = 4 + rnd.Next(0, 7);
                    r.Height = 8 + rnd.Next(0, 8);
                    r.Fill = new SolidColorBrush(i % 2 == 0 ? c1 : c2);
                    r.RenderTransformOrigin = new Point(0.5, 0.5);
                    Canvas.SetLeft(r, rnd.Next(0, Math.Max(1, (int)canvas.Width)));
                    Canvas.SetTop(r, -rnd.Next(0, Math.Max(1, (int)canvas.Height / 2)));
                    canvas.Children.Add(r);
                    parts.Add(r);
                    vx.Add((rnd.NextDouble() - 0.5) * 240);
                    vy.Add(180 + rnd.NextDouble() * 360);
                    rot.Add((rnd.NextDouble() - 0.5) * 720);
                }

                DateTime start = DateTime.UtcNow;
                DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Render);
                t.Interval = TimeSpan.FromMilliseconds(33);
                t.Tick += delegate
                {
                    double elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    double left = Math.Max(0, duration / 1000.0 - elapsed);
                    double opacity = Math.Min(1, left / 0.35);
                    for (int i = 0; i < parts.Count; i++)
                    {
                        Rectangle r = parts[i];
                        double x = Canvas.GetLeft(r) + vx[i] * 0.033;
                        double y = Canvas.GetTop(r) + vy[i] * 0.033;
                        Canvas.SetLeft(r, x);
                        Canvas.SetTop(r, y);
                        r.Opacity = opacity;
                        r.RenderTransform = new RotateTransform(rot[i] * elapsed);
                    }
                    if (elapsed >= duration / 1000.0)
                    {
                        t.Stop();
                        try { w.Close(); } catch { }
                    }
                };
                t.Start();
            }
            catch (Exception ex) { Logger.Err("特效", "礼炮失败", ex); }
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
    }
}
