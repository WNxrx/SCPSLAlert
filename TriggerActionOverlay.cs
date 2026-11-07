using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 7 批：触发点的图片 / 文字动作显示。
    ///
    /// 每个触发点卡片一个"通道"：同一个卡片再触发时更新自己的窗口内容，不新建；
    /// 停止条件命中时按卡片整体收起。位置用 0..1，基准 = 目标显示器。
    /// </summary>
    internal static class TriggerActionOverlay
    {
        private sealed class Entry
        {
            public Window Window;
            public DispatcherTimer GifTimer;
            public List<BitmapFrame> Frames;
            public int FrameIndex;
        }

        private static readonly Dictionary<string, List<Entry>> Live = new Dictionary<string, List<Entry>>();

        public static void Show(string cardKey, TriggerAction a, Config cfg)
        {
            if (a == null) return;
            if (a.Kind != "image" && a.Kind != "text") return;
            try
            {
                HideCard(cardKey);
                Entry e = a.Kind == "image" ? BuildImage(a) : BuildText(a);
                if (e == null) return;
                List<Entry> list;
                if (!Live.TryGetValue(cardKey, out list))
                {
                    list = new List<Entry>();
                    Live[cardKey] = list;
                }
                list.Add(e);
                e.Window.Show();
                try { NativeMethods.HideFromCapture(new System.Windows.Interop.WindowInteropHelper(e.Window).Handle, "触发点动作"); } catch { }
            }
            catch (Exception ex) { Logger.Err("触发点", "显示图片/文字动作失败", ex); }
        }

        public static void HideCard(string cardKey)
        {
            List<Entry> list;
            if (!Live.TryGetValue(cardKey, out list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    if (list[i].GifTimer != null) list[i].GifTimer.Stop();
                    list[i].Window.Close();
                }
                catch { }
            }
            Live.Remove(cardKey);
        }

        public static void HideAll()
        {
            List<string> keys = new List<string>(Live.Keys);
            for (int i = 0; i < keys.Count; i++) HideCard(keys[i]);
        }

        private static Window BaseWindow(TriggerAction a, bool fullScreen)
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
            Window owner = null;
            try { owner = Application.Current != null ? Application.Current.MainWindow : null; } catch { }
            if (owner != null && owner != w) w.Owner = owner;
            return w;
        }

        private static void Place(Window w, TriggerAction a)
        {
            System.Drawing.Rectangle basis;
            IntPtr game = ScreenGeometry.FindGameWindow();
            if (game != IntPtr.Zero) basis = BrightnessSampler.ClientBounds(game);
            else basis = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            if (basis.Width <= 0) basis = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.UpdateLayout();
            double width = double.IsNaN(w.Width) || w.Width <= 0 ? w.ActualWidth : w.Width;
            double height = double.IsNaN(w.Height) || w.Height <= 0 ? w.ActualHeight : w.Height;

            double x = basis.Left + basis.Width * Math.Max(0, Math.Min(1, a.XPercent)) - width / 2;
            double y = basis.Top + basis.Height * Math.Max(0, Math.Min(1, a.YPercent)) - height / 2;
            w.Left = x;
            w.Top = y;
        }

        private static Entry BuildImage(TriggerAction a)
        {
            if (string.IsNullOrEmpty(a.Image) || !File.Exists(a.Image))
            {
                Logger.On("触发点", "图片动作找不到文件：" + a.Image);
                return null;
            }
            double scale = Math.Max(0.05, Math.Min(4.0, a.Scale <= 0 ? 1 : a.Scale));
            Entry e = new Entry();
            e.Window = BaseWindow(a, false);

            Image img = new Image();
            img.Stretch = Stretch.Uniform;
            img.Opacity = Math.Max(0.05, Math.Min(1.0, a.Opacity <= 0 ? 1 : a.Opacity));

            string ext = Path.GetExtension(a.Image);
            if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                GifBitmapDecoder dec = new GifBitmapDecoder(new Uri(a.Image, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                e.Frames = new List<BitmapFrame>();
                for (int i = 0; i < dec.Frames.Count; i++) e.Frames.Add(dec.Frames[i]);
                if (e.Frames.Count > 0)
                {
                    img.Source = e.Frames[0];
                    int wPix = e.Frames[0].PixelWidth;
                    int hPix = e.Frames[0].PixelHeight;
                    img.Width = wPix * scale;
                    img.Height = hPix * scale;
                    if (e.Frames.Count > 1)
                    {
                        e.GifTimer = new DispatcherTimer();
                        e.GifTimer.Interval = TimeSpan.FromMilliseconds(80);   // 上限 30fps 的保守值
                        e.GifTimer.Tick += delegate
                        {
                            e.FrameIndex = (e.FrameIndex + 1) % e.Frames.Count;
                            img.Source = e.Frames[e.FrameIndex];
                        };
                        e.GifTimer.Start();
                    }
                }
            }
            else
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(a.Image, UriKind.Absolute);
                bmp.EndInit();
                img.Source = bmp;
                img.Width = bmp.PixelWidth * scale;
                img.Height = bmp.PixelHeight * scale;
            }

            e.Window.Content = img;
            e.Window.Width = img.Width + 8;
            e.Window.Height = img.Height + 8;
            Place(e.Window, a);
            return e;
        }

        private static Entry BuildText(TriggerAction a)
        {
            Entry e = new Entry();
            e.Window = BaseWindow(a, false);
            string text = a.Text ?? "";
            if (text.Length == 0) return null;

            double size = Math.Max(8, Math.Min(120, a.FontSize <= 0 ? 32 : a.FontSize));
            FontFamily family = string.IsNullOrEmpty(a.Font)
                ? new FontFamily("Microsoft YaHei UI") : new FontFamily(a.Font);
            Color c1 = ParseColor(a.Color1, Colors.White);
            Color c2 = ParseColor(a.Color2, Color.FromRgb(0x43, 0xCF, 0xBB));

            FrameworkElement content;
            if (a.Effect == "marquee")
            {
                content = BuildMarquee(text, family, size, c1, c2, a);
            }
            else if (a.Effect == "char")
            {
                content = BuildPerChar(text, family, size, c1, c2, false);
            }
            else if (a.Effect == "rainbow")
            {
                content = BuildPerChar(text, family, size, c1, c2, true);
            }
            else
            {
                TextBlock tb = new TextBlock();
                tb.Text = text;
                tb.FontFamily = family;
                tb.FontSize = size;
                tb.FontWeight = FontWeights.Bold;
                if (a.Effect == "gradient")
                {
                    LinearGradientBrush brush = new LinearGradientBrush(c1, c2, 0);
                    tb.Foreground = brush;
                }
                else tb.Foreground = new SolidColorBrush(c1);
                content = a.Outline ? WithOutline(tb, size) : tb;
            }

            content.Opacity = Math.Max(0.05, Math.Min(1.0, a.Opacity <= 0 ? 1 : a.Opacity));
            e.Window.Content = content;
            e.Window.SizeToContent = SizeToContent.WidthAndHeight;
            e.Window.UpdateLayout();
            Place(e.Window, a);
            return e;
        }

        private static FrameworkElement BuildPerChar(string text, FontFamily family, double size,
            Color c1, Color c2, bool rainbow)
        {
            TextBlock tb = new TextBlock();
            tb.FontFamily = family;
            tb.FontSize = size;
            tb.FontWeight = FontWeights.Bold;
            for (int i = 0; i < text.Length; i++)
            {
                Run run = new Run(text[i].ToString());
                Color c;
                if (rainbow)
                {
                    double h = (i * 360.0 / Math.Max(1, text.Length)) % 360;
                    c = HsvToColor(h, 0.72, 1.0);
                }
                else c = i % 2 == 0 ? c1 : c2;
                run.Foreground = new SolidColorBrush(c);
                tb.Inlines.Add(run);
            }
            return WithOutline(tb, size);
        }

        private static FrameworkElement BuildMarquee(string text, FontFamily family, double size,
            Color c1, Color c2, TriggerAction a)
        {
            Canvas canvas = new Canvas();
            double h = size * 1.6;
            canvas.Height = h;
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontFamily = family;
            tb.FontSize = size;
            tb.FontWeight = FontWeights.Bold;
            tb.Foreground = new SolidColorBrush(c1);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = tb.DesiredSize.Width;
            canvas.Width = Math.Max(320, textWidth + 80);

            TranslateTransform tr = new TranslateTransform();
            tb.RenderTransform = tr;
            Canvas.SetTop(tb, 0);
            if (a.MarqueeDir == 0) Canvas.SetLeft(tb, canvas.Width);
            else Canvas.SetLeft(tb, -textWidth);

            double seconds = Math.Max(1.0, canvas.Width / Math.Max(0.1, a.MarqueeSpeed) / 120.0);
            DoubleAnimation anim = new DoubleAnimation();
            anim.From = a.MarqueeDir == 0 ? canvas.Width : -textWidth;
            anim.To = a.MarqueeDir == 0 ? -textWidth : canvas.Width;
            anim.Duration = new Duration(TimeSpan.FromSeconds(seconds));
            anim.RepeatBehavior = RepeatBehavior.Forever;
            tr.BeginAnimation(TranslateTransform.XProperty, anim);
            canvas.Children.Add(tb);
            return canvas;
        }

        /// <summary>1px 反差描边：四个方向各偏移 1px 的黑色副本 + 原文字。</summary>
        private static FrameworkElement WithOutline(TextBlock source, double size)
        {
            Grid g = new Grid();
            double[] dx = { -1, 1, 0, 0 };
            double[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                TextBlock t = new TextBlock();
                t.Text = source.Text;
                t.FontFamily = source.FontFamily;
                t.FontSize = source.FontSize;
                t.FontWeight = source.FontWeight;
                t.Foreground = Brushes.Black;
                t.Opacity = 0.85;
                t.RenderTransform = new TranslateTransform(dx[i], dy[i]);
                g.Children.Add(t);
            }
            g.Children.Add(source);
            return g;
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch { return fallback; }
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            System.Drawing.Color c = FactionTable.HsvToRgb(h, s, v);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
    }
}
