using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>
    /// 软件徽标（启动界面 SplashEmblemCore / SplashEmblemOrbit 的矢量语言）。
    /// 亮 / 暗两套，窗口图标和托盘图标都从这里取，换主题时跟着换。
    /// exe 文件图标在编译期固定（app.ico），运行期改不了。
    /// </summary>
    internal static class AppMark
    {
        private static readonly Dictionary<string, System.Drawing.Icon> Icons =
            new Dictionary<string, System.Drawing.Icon>();
        private static readonly List<Bitmap> KeepAlive = new List<Bitmap>();

        public static Color PlateTop(bool light) { return light ? C(255, 255, 255) : C(13, 20, 28); }
        public static Color PlateBottom(bool light) { return light ? C(237, 242, 245) : C(7, 11, 16); }

        public static Color AccentColor(bool light) { return light ? C(0, 132, 163) : C(56, 214, 255); }
        private static Color LineColor(bool light) { return light ? C(43, 74, 90) : C(120, 160, 180); }
        private static Color MutedColor(bool light) { return light ? C(143, 166, 180) : C(92, 116, 128); }

        /// <summary>画一张 size×size 的位图（32bpp ARGB，透明底 + 圆角底板）。</summary>
        public static Bitmap Render(bool light, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                double inset = size <= 24 ? 0.5 : 1.0;
                RectangleF plate = new RectangleF(
                    (float)inset, (float)inset, (float)(size - inset * 2), (float)(size - inset * 2));
                using (GraphicsPath path = Round(plate, (float)Math.Max(2.0, size * 0.16)))
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(0, size), PlateTop(light), PlateBottom(light)))
                    g.FillPath(fill, path);
                using (GraphicsPath path = Round(plate, (float)Math.Max(2.0, size * 0.16)))
                using (Pen edge = new Pen(light ? C(150, 170, 182) : C(34, 52, 66), (float)Math.Max(0.9, size / 96.0)))
                    g.DrawPath(edge, path);

                DrawEmblem(g, light, size);
            }
            return bmp;
        }

        private static void DrawEmblem(Graphics g, bool light, int size)
        {
            double cx = size / 2.0;
            double cy = size / 2.0;
            double pad = size <= 24 ? 1.2 : size * 0.09;
            double maxR = size / 2.0 - pad;
            double r = maxR * 0.82;
            Color accent = AccentColor(light);
            Color line = LineColor(light);
            Color muted = MutedColor(light);
            bool detailed = size >= 64;
            bool mid = size >= 32;

            // 外六边
            PointF[] hex = Hex(cx, cy, r, -Math.PI / 2);
            using (Pen p = new Pen(line, (float)Math.Max(1.0, size / 42.0)))
                g.DrawPolygon(p, hex);

            // 12 根刻度
            int ticks = detailed ? 12 : (mid ? 8 : 0);
            for (int i = 0; i < ticks; i++)
            {
                double a = i * Math.PI * 2 / ticks - Math.PI / 2;
                bool major = (i % 3) == 0;
                using (Pen p = new Pen(major ? accent : muted, (float)Math.Max(0.7, size / 64.0)))
                    g.DrawLine(p,
                        (float)(cx + Math.Cos(a) * r * 0.55), (float)(cy + Math.Sin(a) * r * 0.55),
                        (float)(cx + Math.Cos(a) * r * (major ? 0.66 : 0.60)),
                        (float)(cy + Math.Sin(a) * r * (major ? 0.66 : 0.60)));
            }

            // 菱形
            float d = (float)(r * (size <= 24 ? 0.44 : 0.30));
            PointF[] diamond = new PointF[]
            {
                new PointF((float)cx, (float)cy - d), new PointF((float)cx + d, (float)cy),
                new PointF((float)cx, (float)cy + d), new PointF((float)cx - d, (float)cy)
            };
            using (Pen p = new Pen(accent, (float)Math.Max(1.1, size / 40.0)))
                g.DrawPolygon(p, diamond);

            // 十字准星 + 核心点
            if (mid)
            {
                float a0 = (float)(r * 0.34), a1 = (float)(r * 0.52);
                using (Pen p = new Pen(accent, (float)Math.Max(1.0, size / 44.0)))
                {
                    g.DrawLine(p, (float)cx - a1, (float)cy, (float)cx - a0, (float)cy);
                    g.DrawLine(p, (float)cx + a0, (float)cy, (float)cx + a1, (float)cy);
                    g.DrawLine(p, (float)cx, (float)cy - a1, (float)cx, (float)cy - a0);
                    g.DrawLine(p, (float)cx, (float)cy + a0, (float)cx, (float)cy + a1);
                }
            }
            float dot = (float)Math.Max(1.3, size * 0.045);
            using (SolidBrush b = new SolidBrush(accent))
                g.FillEllipse(b, (float)cx - dot, (float)cy - dot, dot * 2, dot * 2);

            // 轨道 + 卫星
            if (mid)
            {
                float or = (float)(maxR - 1.5);
                using (Pen p = new Pen(accent, (float)Math.Max(0.7, size / 64.0)))
                {
                    p.DashStyle = DashStyle.Custom;
                    p.DashPattern = new float[] { 2, 6 };
                    g.DrawEllipse(p, (float)cx - or * 0.94f, (float)cy - or * 0.94f,
                        or * 1.88f, or * 1.88f);
                }
                float sr = (float)Math.Max(1.2, size * 0.045);
                using (SolidBrush b = new SolidBrush(accent))
                    g.FillEllipse(b, (float)(cx + or * 0.94) - sr, (float)cy - sr, sr * 2, sr * 2);
            }
        }

        private static PointF[] Hex(double cx, double cy, double r, double start)
        {
            PointF[] p = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = start + i * Math.PI / 3;
                p[i] = new PointF((float)(cx + Math.Cos(a) * r), (float)(cy + Math.Sin(a) * r));
            }
            return p;
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>托盘图标。缓存，永不 Dispose（HICON 挂在位图上）。</summary>
        public static System.Drawing.Icon Tray(bool light)
        {
            string key = light ? "light" : "dark";
            System.Drawing.Icon icon;
            if (Icons.TryGetValue(key, out icon)) return icon;
            Bitmap bmp = Render(light, 32);
            KeepAlive.Add(bmp);
            icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            Icons[key] = icon;
            return icon;
        }

        /// <summary>窗口图标（位图源），按主题取。</summary>
        public static BitmapSource Image(bool light, int size)
        {
            using (Bitmap bmp = Render(light, size))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        private static Color C(byte r, byte g, byte b)
        {
            return Color.FromArgb(255, r, g, b);
        }
    }
}
