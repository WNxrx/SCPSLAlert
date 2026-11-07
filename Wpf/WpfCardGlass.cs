using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>
    /// 卡片玻璃质感（纯 WPF 绘制，不依赖窗口亚克力）。
    ///
    /// 旧版“卡片半透明”只是保存了一个开关，VigilCard 仍然画不透明底色，
    /// 所以看起来永远没有玻璃。新版在这里统一决定：
    ///   · 卡片底色半透明，让背景图 / 点阵透进来；
    ///   · 叠一层斜向高光、顶部亮边和细噪点；
    ///   · 用户上传背景后，按背景平均明暗自动选择“偏黑”或“偏白”的玻璃，
    ///     避免白底上白玻璃、黑底上黑玻璃看不清。
    /// </summary>
    internal static class WpfCardGlass
    {
        public static bool Enabled = true;
        public static int Strength = 55;
        public static int Frost = 55;
        public static int Opacity = 25;
        public static bool LightEnabled = true;
        public static int LightBrightness = 70;
        public static int LightSource = 0;
        public static bool LightTint = true;
        public static bool AutoTint = true;
        public static double BackgroundLuma = 0;
        public static string LastError = "";

        private static string _cachedPath = "";
        private static long _cachedWrite;
        private static double _cachedLuma = -1;

        public static void Configure(Config cfg, bool lightTheme)
        {
            if (cfg == null) { Enabled = false; return; }
            Enabled = cfg.GlassCards;
            Strength = Math.Max(0, Math.Min(100, cfg.GlassStrength));
            Frost = Math.Max(0, Math.Min(100, cfg.GlassFrost));
            Opacity = Math.Max(0, Math.Min(100, cfg.GlassOpacity));
            LightEnabled = cfg.GlassLightEnabled;
            LightBrightness = Math.Max(0, Math.Min(100, cfg.GlassLightBrightness));
            LightSource = Math.Max(0, Math.Min(4, cfg.GlassLightSource));
            BackgroundLuma = EstimateLuma(cfg, lightTheme);

            int mode = Math.Max(0, Math.Min(2, cfg.GlassTintMode));
            if (mode == 1) { LightTint = false; AutoTint = false; }
            else if (mode == 2) { LightTint = true; AutoTint = false; }
            else { AutoTint = true; LightTint = BackgroundLuma < 0.52; }
        }

        public static string TintLabel
        {
            get
            {
                string value = LightTint ? "偏白" : "偏黑";
                return AutoTint ? "自动 · " + value : value;
            }
        }

        public static Color TintColor
        {
            get { return LightTint ? Color.FromRgb(245, 250, 255) : Color.FromRgb(4, 8, 14); }
        }

        /// <summary>估算主界面背景平均亮度（0=黑，1=白）。</summary>
        public static double EstimateLuma(Config cfg, bool lightTheme)
        {
            double pageLuma = lightTheme ? 0.88 : 0.045;
            string path = cfg != null ? (cfg.BackgroundImage ?? "").Trim() : "";
            if (path.Length == 0 || !File.Exists(path)) return pageLuma;

            double imageLuma;
            try { imageLuma = ImageLuma(path); }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return pageLuma;
            }

            double raw = Math.Max(0, Math.Min(100, cfg.BackgroundOpacity));
            double opacity = raw <= 0 ? 0 : 0.35 + 0.65 * raw / 100.0;
            double darken = Math.Max(0, Math.Min(100, cfg.BackgroundDarken)) / 100.0;

            // 先按不透明度叠到底色上，再叠加“暗化”黑色遮罩。
            double lum = pageLuma * (1 - opacity) + imageLuma * opacity;
            lum = lum * (1 - darken) + 0.018 * darken;
            return Math.Max(0, Math.Min(1, lum));
        }

        private static double ImageLuma(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".webm" || ext == ".wmv" || ext == ".avi" || ext == ".mov" || ext == ".mkv")
                return 0.34;   // 视频取不到帧，按偏暗处理；用户仍可手动选偏黑/偏白

            long write = 0;
            try { write = File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase)
                && write == _cachedWrite && _cachedLuma >= 0)
                return _cachedLuma;

            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 40;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            int w = Math.Max(1, bmp.PixelWidth);
            int h = Math.Max(1, bmp.PixelHeight);
            byte[] px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);

            double sum = 0;
            for (int i = 0; i < w * h; i++)
            {
                // Pbgra32 是预乘 alpha；背景图通常不透明，直接按 BGR 计亮度。
                double b = px[i * 4] / 255.0;
                double g = px[i * 4 + 1] / 255.0;
                double r = px[i * 4 + 2] / 255.0;
                sum += 0.2126 * r + 0.7152 * g + 0.0722 * b;
            }
            _cachedPath = path;
            _cachedWrite = write;
            _cachedLuma = sum / (w * h);
            return _cachedLuma;
        }

        /// <summary>
        /// 估算“按钮下面那层表面”的亮度：把卡片玻璃色按透明度叠到主界面背景上。
        /// 按钮据此选择白底或黑底，文字再用相反色。
        /// </summary>
        public static double SurfaceLuma(Color baseColor)
        {
            double baseLuma = Luma(baseColor);
            if (!Enabled || Strength <= 0) return baseLuma;

            double alpha = Math.Max(0, Math.Min(1, (255 - Math.Max(0, Math.Min(100, Opacity)) * 1.75) / 255.0));
            double frostT = Math.Max(0, Math.Min(100, Frost)) / 100.0;
            double mix = LightTint ? (0.05 + frostT * 0.13) : (0.28 + frostT * 0.16);
            Color glass = Mix(baseColor, TintColor, mix);
            double bg = Math.Max(0, Math.Min(1, BackgroundLuma));

            double r = (glass.R / 255.0) * alpha + bg * (1 - alpha);
            double g = (glass.G / 255.0) * alpha + bg * (1 - alpha);
            double b = (glass.B / 255.0) * alpha + bg * (1 - alpha);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double Luma(Color c)
        {
            return 0.2126 * c.R / 255.0 + 0.7152 * c.G / 255.0 + 0.0722 * c.B / 255.0;
        }

        public static Color ColorOf(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid != null) return solid.Color;
            return Color.FromRgb(18, 28, 38);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>VigilCard 的玻璃绘制；shape 已经按切角裁好。</summary>
        public static void Draw(DrawingContext dc, Geometry shape, Brush fill, Brush stroke, double w, double h)
        {
            int strength = Math.Max(0, Math.Min(100, Strength));
            int frost = Math.Max(0, Math.Min(100, Frost));
            int opacity = Math.Max(0, Math.Min(100, Opacity));
            Color baseColor = ColorOf(fill);
            Color tint = TintColor;

            // 透明度独立控制；0 = 不透明，100 = 最透（仍保留一点底色保证文字可读）。
            double alpha = 255 - opacity * 1.75;
            double frostT = frost / 100.0;
            double mix = LightTint ? (0.05 + frostT * 0.13) : (0.28 + frostT * 0.16);
            Color glass = Mix(baseColor, tint, mix);
            glass.A = (byte)Math.Max(90, Math.Min(255, alpha));

            dc.DrawGeometry(Solid(glass), null, shape);
            DrawOverlay(dc, shape, w, h);

            if (stroke != null)
            {
                Color line = ColorOf(stroke);
                line = Mix(line, tint, 0.35);
                line.A = (byte)Math.Min(255, (LightEnabled ? 60 : 45) + strength * 1.2);
                Pen frame = new Pen(Solid(line), 1);
                dc.DrawGeometry(null, frame, shape);
            }
        }

        /// <summary>磨砂 + 光源 + 噪点；名片和 VigilCard 共用同一套参数。</summary>
        public static void DrawOverlay(DrawingContext dc, Geometry shape, double w, double h)
        {
            int strength = Math.Max(0, Math.Min(100, Strength));
            int frost = Math.Max(0, Math.Min(100, Frost));
            Color tint = TintColor;

            // 磨砂：越强越像蒙了一层薄雾，同时颗粒更明显。
            if (frost > 0)
            {
                Color haze = Color.FromArgb((byte)(frost * 0.50), tint.R, tint.G, tint.B);
                dc.DrawGeometry(Solid(haze), null, shape);
            }

            // 光源：亮度 × 总强度，决定高光和亮边；关掉后只剩底色 + 磨砂。
            if (LightEnabled && LightBrightness > 0)
            {
                double light = (LightBrightness / 100.0) * (0.55 + strength / 150.0);
                if (light > 1.2) light = 1.2;
                Point start, end;
                bool top, left, right;
                LightPoints(LightSource, out start, out end, out top, out left, out right);

                byte sheenAlpha = (byte)Math.Min(110, 16 + light * 76);
                LinearGradientBrush sheen = new LinearGradientBrush(
                    Color.FromArgb(sheenAlpha, tint.R, tint.G, tint.B),
                    Color.FromArgb(0, tint.R, tint.G, tint.B),
                    start, end);
                sheen.Freeze();
                dc.DrawGeometry(sheen, null, shape);

                byte edgeAlpha = (byte)Math.Min(255, 40 + light * 190);
                Pen edgePen = new Pen(Solid(Color.FromArgb(edgeAlpha, 255, 255, 255)), 1.05);
                Pen sidePen = new Pen(Solid(Color.FromArgb((byte)(edgeAlpha * 0.55), 255, 255, 255)), 0.7);
                if (top)
                    dc.DrawLine(edgePen, new Point(2, 1.2), new Point(Math.Max(3, w - 2), 1.2));
                if (left)
                    dc.DrawLine(sidePen, new Point(1.2, Math.Min(8, h * 0.18)), new Point(1.2, Math.Min(h - 8, h * 0.72)));
                if (right)
                    dc.DrawLine(sidePen, new Point(w - 1.2, Math.Min(8, h * 0.18)), new Point(w - 1.2, Math.Min(h - 8, h * 0.72)));
            }

            DrawingBrush noise = NoiseBrush();
            if (noise != null && frost > 0)
            {
                dc.PushOpacity(Math.Min(0.18, 0.015 + frost * 0.0014 + strength * 0.0002));
                dc.DrawGeometry(noise, null, shape);
                dc.Pop();
            }
        }

        /// <summary>
        /// 给按钮 / 下拉框 / 数字输入框用的玻璃底刷。
        /// 和卡片同一套磨砂、透明度、光源参数，但去掉了噪点，避免小控件里显得脏。
        /// </summary>
        public static Brush SurfaceBrush(Brush baseFill)
        {
            if (!Enabled || Strength <= 0 || baseFill == null) return baseFill;

            int strength = Math.Max(0, Math.Min(100, Strength));
            int frost = Math.Max(0, Math.Min(100, Frost));
            int opacity = Math.Max(0, Math.Min(100, Opacity));
            Color baseColor = ColorOf(baseFill);
            Color tint = TintColor;

            // 尊重原底刷的透明度：输入框是实色，选中胶囊是 40/255 的强调色。
            double baseAlpha = baseColor.A / 255.0;
            if (baseAlpha <= 0.01) return baseFill;
            double alpha = 255 - opacity * 1.75;
            double frostT = frost / 100.0;
            // 小控件不做大幅提亮/压暗，否则浅色字会糊在浅色玻璃上。
            double mix = 0.035 + frostT * 0.055;
            Color glass = Mix(baseColor, tint, mix);
            glass.A = (byte)Math.Max(1, Math.Min(255, alpha * baseAlpha));

            DrawingGroup group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Solid(glass), null,
                new RectangleGeometry(new Rect(0, 0, 1, 1))));

            if (frost > 0)
            {
                Color haze = Color.FromArgb((byte)(frost * 0.22 * baseAlpha), tint.R, tint.G, tint.B);
                group.Children.Add(new GeometryDrawing(Solid(haze), null,
                    new RectangleGeometry(new Rect(0, 0, 1, 1))));
            }

            if (LightEnabled && LightBrightness > 0)
            {
                double light = (LightBrightness / 100.0) * (0.55 + strength / 150.0);
                if (light > 1.2) light = 1.2;
                Point start, end;
                bool top, left, right;
                LightPoints(LightSource, out start, out end, out top, out left, out right);

                byte sheenAlpha = (byte)Math.Max(1, Math.Min(64, (10 + light * 38) * baseAlpha));
                LinearGradientBrush sheen = new LinearGradientBrush(
                    Color.FromArgb(sheenAlpha, tint.R, tint.G, tint.B),
                    Color.FromArgb(0, tint.R, tint.G, tint.B),
                    start, end);
                group.Children.Add(new GeometryDrawing(sheen, null,
                    new RectangleGeometry(new Rect(0, 0, 1, 1))));
            }

            DrawingBrush brush = new DrawingBrush(group);
            brush.Stretch = Stretch.Fill;
            brush.Viewport = new Rect(0, 0, 1, 1);
            brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
            brush.TileMode = TileMode.None;
            brush.Freeze();
            return brush;
        }

        private static void LightPoints(int source, out Point start, out Point end,
            out bool top, out bool left, out bool right)
        {
            top = left = right = false;
            switch (source)
            {
                case 1: // 右上
                    start = new Point(1, 0); end = new Point(0, 1); top = true; right = true; break;
                case 2: // 上方
                    start = new Point(0.5, 0); end = new Point(0.5, 1); top = true; break;
                case 3: // 左侧
                    start = new Point(0, 0.5); end = new Point(1, 0.5); left = true; break;
                case 4: // 右侧
                    start = new Point(1, 0.5); end = new Point(0, 0.5); right = true; break;
                default: // 左上
                    start = new Point(0, 0); end = new Point(1, 1); top = true; left = true; break;
            }
        }

        private static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static DrawingBrush _noise;

        private static DrawingBrush NoiseBrush()
        {
            if (_noise != null) return _noise;
            DrawingGroup group = new DrawingGroup();
            Random rnd = new Random(20261107);
            for (int i = 0; i < 190; i++)
            {
                double x = rnd.NextDouble() * 64;
                double y = rnd.NextDouble() * 64;
                double size = rnd.NextDouble() < 0.82 ? 1 : 2;
                byte a = (byte)rnd.Next(10, 34);
                SolidColorBrush dot = new SolidColorBrush(Color.FromArgb(a, 255, 255, 255));
                dot.Freeze();
                group.Children.Add(new GeometryDrawing(dot, null,
                    new RectangleGeometry(new Rect(x, y, size, size))));
            }
            DrawingBrush brush = new DrawingBrush(group);
            brush.TileMode = TileMode.Tile;
            brush.Viewport = new Rect(0, 0, 64, 64);
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Stretch = Stretch.None;
            brush.Freeze();
            _noise = brush;
            return _noise;
        }
    }
}
