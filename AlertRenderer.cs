using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace SCPSLAlert
{
    /// <summary>提示框里那张“壁纸”的铺法。</summary>
    internal enum WallpaperMode
    {
        Stretch,   // 拉伸铺满
        Fit,       // 保持比例缩放，留边
        Tile,      // 平铺
        Center     // 原始大小居中
    }

    /// <summary>弹出 / 缩回的动画方式。</summary>
    internal enum AnimKind
    {
        None,        // 直接出现 / 直接消失
        Fade,        // 只淡入淡出
        FromTop,     // 从上方滑入
        FromBottom,  // 从下方滑入
        FromLeft,    // 从左侧滑入
        FromRight,   // 从右侧滑入
        ZoomIn,      // 由小放大
        ZoomOut      // 由大缩小
    }

    /// <summary>画一个提示所需的全部素材（提示窗口和设置页预览共用同一份）。</summary>
    internal class AlertVisual
    {
        public string Text = "";

        public Font Font;
        public Color TextColor = Color.White;
        public int TextOpacity = 100;

        public Color BackColor = Color.FromArgb(10, 14, 20);
        public int BackOpacity = 62;
        public int CornerRadius = 12;

        public Image Wallpaper;
        public WallpaperMode WallpaperMode = WallpaperMode.Stretch;
        public int WallpaperOpacity = 100;

        // 玻璃 / 质感（都是模拟出来的：一层光泽渐变 + 顶部亮边 + 磨砂噪点）
        public bool Glass;
        public int GlassStrength = 55;
        public int GlassHighlight = 70;
        public int GlassNoise = 12;

        public AlertIcon Icon = AlertIcon.None;
        public bool ShowIcon = true;

        public bool Highlight;
    }

    /// <summary>一次排版的结果：Measure 算一次，Draw 复用。</summary>
    internal struct AlertLayout
    {
        public Size Surface;
        public Size TextSize;
        public int IconSize;
        public int IconW;
        public Rectangle Box;
    }

    /// <summary>
    /// 提示的真实画法。提示窗口和设置页里的预览都调这里，
    /// 保证“预览看到什么样，屏幕上就是什么样”。
    /// </summary>
    internal static class AlertRenderer
    {
        public const int Glow = 5;    // 外发光 / 阴影宽度（也是窗口四周留白）
        public const int PadH = 16;   // 文字到背景框边缘的水平距离
        public const int PadV = 9;    // 文字到背景框边缘的垂直距离

        private static readonly Bitmap MeasureBitmap = new Bitmap(1, 1);
        private static Bitmap _noise;

        public static AlertLayout Measure(AlertVisual v)
        {
            AlertLayout L = new AlertLayout();

            string t = string.IsNullOrEmpty(v.Text) ? " " : v.Text;
            Font f = v.Font != null ? v.Font : SystemFonts.DefaultFont;

            using (Graphics g = Graphics.FromImage(MeasureBitmap))
                L.TextSize = Size.Ceiling(g.MeasureString(t, f));

            if (v.ShowIcon && v.Icon != AlertIcon.None && !string.IsNullOrEmpty(v.Text))
            {
                L.IconSize = (int)Math.Round(f.Height * 0.92);
                if (L.IconSize < 9) L.IconSize = 9;
                if (L.IconSize > 64) L.IconSize = 64;
                L.IconW = L.IconSize + 9;
            }

            int boxW = L.IconW + L.TextSize.Width + PadH * 2;
            int boxH = L.TextSize.Height + PadV * 2;
            int w = boxW + Glow * 2;
            int h = boxH + Glow * 2;
            if (w < 16) w = 16;
            if (h < 16) h = 16;

            L.Surface = new Size(w, h);
            L.Box = new Rectangle(Glow, Glow, boxW, boxH);
            return L;
        }

        public static void Draw(Graphics g, AlertVisual v, AlertLayout L, float scale)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // 缩放类动画：以窗口中心为原点整体缩放
            if (scale != 1f && scale > 0.01f)
            {
                g.TranslateTransform(L.Surface.Width / 2f, L.Surface.Height / 2f);
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-L.Surface.Width / 2f, -L.Surface.Height / 2f);
            }

            Rectangle box = L.Box;
            int radius = Clamp(v.CornerRadius, 0, Math.Min(box.Width, box.Height) / 2);

            // ---- 高亮时整体反色成“亮底深字”的警示牌 ----
            Color fillColor = v.BackColor;
            int fillAlpha = v.BackOpacity;
            Color drawColor = v.TextColor;
            Color borderColor = Color.FromArgb(72, 255, 255, 255);
            float borderWidth = 1f;

            if (v.Highlight)
            {
                fillColor = v.TextColor;
                fillAlpha = 92;
                drawColor = Color.FromArgb(16, 18, 22);
                borderColor = v.TextColor;
                borderWidth = 2f;
            }

            // ---- 外圈柔和发光 ----
            int glowBase = v.Highlight ? 20 : 8;
            int glowStep = v.Highlight ? 12 : 5;
            for (int i = Glow; i >= 1; i--)
            {
                int a = glowBase + (Glow - i) * glowStep;
                Color gc = v.Highlight ? v.TextColor : Color.Black;
                using (GraphicsPath gp = RoundedRect(Rectangle.Inflate(box, i, i), radius + i))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(Math.Min(200, a), gc)))
                    g.FillPath(sb, gp);
            }

            int backA = fillAlpha;
            if (v.Highlight) backA = Math.Min(100, fillAlpha + 4);

            using (GraphicsPath gp = RoundedRect(box, radius))
            {
                // 底色
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(Alpha(backA), fillColor)))
                    g.FillPath(sb, gp);

                GraphicsState st = g.Save();
                g.SetClip(gp);

                // 壁纸（在底色之上）
                if (v.Wallpaper != null && v.WallpaperOpacity > 0)
                {
                    int wpA = Alpha(backA) * Clamp(v.WallpaperOpacity, 0, 100) / 100;
                    DrawWallpaper(g, box, v.Wallpaper, v.WallpaperMode, wpA);
                    if (v.WallpaperOpacity < 100)
                    {
                        using (SolidBrush sb = new SolidBrush(
                            Color.FromArgb(Alpha(backA) * (100 - v.WallpaperOpacity) / 100, fillColor)))
                            g.FillPath(sb, gp);
                    }
                }

                // 玻璃质感
                if (v.Glass && !v.Highlight) DrawGlass(g, box, v);

                g.Restore(st);

                // 边框
                using (Pen pen = new Pen(Color.FromArgb(v.Highlight ? 240 : 72, borderColor), borderWidth))
                    g.DrawPath(pen, gp);
            }

            // ---- 小标识 ----
            if (L.IconW > 0)
            {
                int iy = Glow + PadV + (L.TextSize.Height - L.IconSize) / 2;
                AlertIcons.Draw(g, new Rectangle(Glow + PadH, iy, L.IconSize, L.IconSize), v.Icon, drawColor);
            }

            // ---- 文字 ----
            string t = string.IsNullOrEmpty(v.Text) ? " " : v.Text;
            Font f = v.Font != null ? v.Font : SystemFonts.DefaultFont;
            float tx = Glow + PadH + L.IconW;
            float ty = Glow + PadV;

            using (StringFormat sf = new StringFormat(StringFormatFlags.NoClip | StringFormatFlags.NoWrap))
            using (Brush shadow = new SolidBrush(v.Highlight
                        ? Color.FromArgb(90, 255, 255, 255)
                        : Color.FromArgb(200, 0, 0, 0)))
            using (Brush fore = new SolidBrush(drawColor))
            {
                g.DrawString(t, f, shadow, tx + 1f, ty + 1f, sf);
                g.DrawString(t, f, fore, tx, ty, sf);
            }
        }

        #region 玻璃质感

        /// <summary>
        /// 给外部（悬浮面板）用的玻璃质感。
        /// 提示框内部走 DrawGlass，这里额外把绘制裁进圆角路径里，
        /// 免得矩形渐变把圆角糊成直角。
        /// </summary>
        public static void DrawGlassFor(Graphics g, GraphicsPath clip, Rectangle box, AlertVisual v)
        {
            GraphicsState st = g.Save();
            try
            {
                g.SetClip(clip, CombineMode.Intersect);
                DrawGlass(g, box, v);
            }
            finally
            {
                g.Restore(st);
            }
        }

        private static void DrawGlass(Graphics g, Rectangle box, AlertVisual v)
        {
            int strength = Clamp(v.GlassStrength, 0, 100);

            // 1) 上亮下暗的光泽渐变，模拟玻璃反射
            if (strength > 0)
            {
                int top = strength * 90 / 100;
                int bottom = strength * 70 / 100;
                using (LinearGradientBrush lb = new LinearGradientBrush(
                           new Rectangle(box.X, box.Y - 1, box.Width, box.Height + 2),
                           Color.FromArgb(top, 255, 255, 255),
                           Color.FromArgb(bottom, 0, 0, 0),
                           LinearGradientMode.Vertical))
                {
                    ColorBlend blend = new ColorBlend(4);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(top, 255, 255, 255),
                        Color.FromArgb(Math.Max(1, top / 6), 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(bottom, 0, 0, 0)
                    };
                    blend.Positions = new float[] { 0f, 0.18f, 0.55f, 1f };
                    lb.InterpolationColors = blend;
                    g.FillRectangle(lb, box);
                }
            }

            // 2) 磨砂噪点
            int noise = Clamp(v.GlassNoise, 0, 100);
            if (noise > 0)
            {
                Image n = Noise();
                float k = noise / 100f * 0.5f;
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix00 = 1f; cm.Matrix11 = 1f; cm.Matrix22 = 1f;
                cm.Matrix33 = k; cm.Matrix44 = 1f;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    for (int y = box.Top; y < box.Bottom; y += n.Height)
                        for (int x = box.Left; x < box.Right; x += n.Width)
                            g.DrawImage(n, new Rectangle(x, y, n.Width, n.Height),
                                0, 0, n.Width, n.Height, GraphicsUnit.Pixel, ia);
                }
            }

            // 3) 顶部一道亮边，玻璃感的关键
            int hl = Clamp(v.GlassHighlight, 0, 100);
            if (hl > 0)
            {
                int h = Math.Max(1, box.Height / 26);
                Rectangle r = new Rectangle(box.X + 2, box.Y + 1, Math.Max(1, box.Width - 4), h);
                using (LinearGradientBrush lb = new LinearGradientBrush(
                           r, Color.FromArgb(hl * 255 / 100, 255, 255, 255),
                           Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(hl * 255 / 100, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    lb.InterpolationColors = blend;
                    g.FillRectangle(lb, r);
                }
            }
        }

        /// <summary>生成一张小的噪点图，之后一直复用。</summary>
        private static Bitmap Noise()
        {
            if (_noise != null) return _noise;
            const int N = 48;
            Bitmap b = new Bitmap(N, N, PixelFormat.Format32bppArgb);
            Random rnd = new Random(20240701);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int v = rnd.Next(0, 256);
                    int a = rnd.Next(0, 256);
                    b.SetPixel(x, y, Color.FromArgb(a, v, v, v));
                }
            }
            _noise = b;
            return b;
        }

        #endregion

        #region 壁纸

        private static void DrawWallpaper(Graphics g, Rectangle box, Image img, WallpaperMode mode, int alpha)
        {
            if (alpha <= 0 || img == null || img.Width <= 0 || img.Height <= 0) return;
            alpha = Clamp(alpha, 0, 255);
            if (alpha >= 254)
            {
                DrawRaw(g, box, img, mode, null);
                return;
            }

            float k = alpha / 255f;
            ColorMatrix cm = new ColorMatrix();
            cm.Matrix00 = 1f; cm.Matrix11 = 1f; cm.Matrix22 = 1f; cm.Matrix33 = k; cm.Matrix44 = 1f;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                DrawRaw(g, box, img, mode, ia);
            }
        }

        private static void DrawRaw(Graphics g, Rectangle box, Image img, WallpaperMode mode, ImageAttributes ia)
        {
            switch (mode)
            {
                case WallpaperMode.Fit:
                    {
                        float s = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
                        int w = Math.Max(1, (int)Math.Round(img.Width * s));
                        int h = Math.Max(1, (int)Math.Round(img.Height * s));
                        DrawImage(g, img, new Rectangle(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h), ia);
                        break;
                    }
                case WallpaperMode.Center:
                    DrawImage(g, img, new Rectangle(box.Left + (box.Width - img.Width) / 2,
                        box.Top + (box.Height - img.Height) / 2, img.Width, img.Height), ia);
                    break;
                case WallpaperMode.Tile:
                    for (int y = box.Top; y < box.Bottom; y += img.Height)
                        for (int x = box.Left; x < box.Right; x += img.Width)
                            DrawImage(g, img, new Rectangle(x, y, img.Width, img.Height), ia);
                    break;
                default:
                    DrawImage(g, img, box, ia);
                    break;
            }
        }

        private static void DrawImage(Graphics g, Image img, Rectangle r, ImageAttributes ia)
        {
            if (ia == null) g.DrawImage(img, r);
            else g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
        }

        /// <summary>读一张图片当壁纸。先读进内存再克隆，不会把文件一直占住。</summary>
        public static Image LoadWallpaper(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string full = path;
                if (!Path.IsPathRooted(full))
                    full = Path.Combine(System.Windows.Forms.Application.StartupPath, path);
                if (!File.Exists(full)) return null;

                using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image src = Image.FromStream(fs))
                {
                    Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        #endregion

        /// <summary>圆角矩形路径。</summary>
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static int Alpha(int percent)
        {
            return Clamp((int)Math.Round(percent * 255.0 / 100.0), 0, 255);
        }

        public static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
