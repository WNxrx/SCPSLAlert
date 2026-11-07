using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SCPSLAlert
{
    /// <summary>
    /// 动画质量（设置里「动画效果」那一项）：关 / 低 / 中 / 高 / 极高。
    ///
    /// 【为什么要分档】悬浮面板是逐像素透明的分层窗口，动画 = 每帧重画 + 一次
    /// UpdateLayeredWindow。开太高吃 CPU（游戏里可能掉帧），开太低又不够顺滑，
    /// 所以让用户自己选：档位同时决定**帧率**和**特效多少**（发光 / 收尾飞线 / 脉冲）。
    /// </summary>
    internal static class UiAnim
    {
        /// <summary>0 = 关，1 = 低，2 = 中，3 = 高（默认），4 = 极高。</summary>
        public static int Quality = 3;

        public static readonly string[] Names =
        {
            "关（瞬间出现，不做动画）",
            "低（30 fps，细线无发光）",
            "中（45 fps，线条带发光）",
            "高（60 fps，默认）",
            "极高（120 fps，额外光效 / 脉冲）",
        };

        public static bool Off { get { return Quality <= 0; } }

        public static int Fps
        {
            get
            {
                if (Quality <= 0) return 0;
                if (Quality == 1) return 30;
                if (Quality == 2) return 45;
                if (Quality == 3) return 60;
                return 120;
            }
        }

        /// <summary>
        /// 动画定时器间隔。注意：Windows Forms 的 Timer 精度约 15.6ms，
        /// 120fps 需要同时把系统计时器精度调到 1ms（见 EnterHighRes/LeaveHighRes）。
        /// </summary>
        public static int IntervalMs
        {
            get
            {
                int ms = Fps <= 0 ? 33 : (int)Math.Round(1000.0 / Fps);
                if (ms < 4) ms = 4;
                return ms;
            }
        }

        /// <summary>第 10 批：游戏前台 / 软件不在前台时休眠常驻动效（核心功能不受影响）。</summary>
        public static bool Sleep;
        public static bool Glow { get { return Quality >= 2; } }
        public static bool ExtraFx { get { return Quality >= 4; } }

        /// <summary>动画帧计数（诊断 / 自测用：算出来的才是真帧率）。</summary>
        public static int Ticks;
        public static long TabWorkMs;      // 切页动画每帧实际耗时合计（诊断）
        public static int TabWorkFrames;

        /// <summary>展开动画总时长：严格落在 1.5 ~ 2.0 秒之间。</summary>
        public static double OpenSeconds
        {
            get
            {
                if (Quality <= 1) return 1.55;
                if (Quality == 2) return 1.65;
                if (Quality == 3) return 1.72;
                return 1.90;
            }
        }

        /// <summary>收起动画总时长（比展开短，干脆一点）。</summary>
        public static double CloseSeconds
        {
            get
            {
                if (Quality <= 1) return 0.85;
                if (Quality == 2) return 0.95;
                if (Quality == 3) return 1.00;
                return 1.10;
            }
        }

        // ---- 缓动函数（全部是纯数学，便于单测）----
        public static double Clamp01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static double EaseOutCubic(double t)
        {
            t = Clamp01(t); double u = 1 - t; return 1 - u * u * u;
        }

        public static double EaseInCubic(double t)
        {
            t = Clamp01(t); return t * t * t;
        }

        public static double EaseInOutQuad(double t)
        {
            t = Clamp01(t);
            return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        }

        // ---- 高精度计时（极高档才需要）----
        private static int _hiRes;   // 引用计数：多个窗口同时动画时只进出一次

        public static void EnterHighRes()
        {
            if (Quality < 4) return;
            if (_hiRes++ == 0) { try { timeBeginPeriod(1); } catch { } }
        }

        public static void LeaveHighRes()
        {
            if (_hiRes <= 0) { _hiRes = 0; return; }
            if (--_hiRes == 0) { try { timeEndPeriod(1); } catch { } }
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
    }

    /// <summary>全息展开动画某一帧的几何。</summary>
    internal struct HoloFrame
    {
        public double Progress;
        public bool PanelVisible;
        public int ClipWidth;         // 画布上要裁出来的宽度（0 = 还没出现）
        public float PanelAlpha;      // 0~1
        public bool ShowTop;
        public bool ShowBottom;
        public Rectangle TopLine;     // 屏幕坐标（细竖线）
        public Rectangle BottomLine;
        public float TopAlpha;
        public float BottomAlpha;
    }

    /// <summary>
    /// 战术面板的全息展开 / 收起动画：两帧纯数学。
    ///
    /// 进度 p = 0（全关）→ 1（全开），分三段：
    ///   ① 0.00 ~ 0.36  纵向拉伸：两根竖线从屏幕下方升到面板顶 / 底；
    ///   ② 0.36 ~ 0.84  横向分裂：两根线分别向左右滑开，面板在中间逐渐变宽；
    ///   ③ 0.84 ~ 1.00  收尾：右边的线加速飞出屏幕，左边那根留在面板左边缘当装饰。
    /// 收起就是这套逻辑倒着走（最后左线向下飞出屏幕）。
    ///
    /// 所有段内都套缓动函数；`Compute` 只做几何，不碰窗口，方便离线测。
    /// </summary>
    internal static class HoloIntro
    {
        public const double VerticalEnd = 0.36;
        public const double HorizontalEnd = 0.84;

        /// <summary>竖线在"未完全升高"阶段的长度（像素）。</summary>
        public static int TickHeight(int boxHeight)
        {
            int h = (int)Math.Round(boxHeight * 0.16);
            if (h < 28) h = 28;
            if (h > 90) h = 90;
            return h;
        }

        /// <summary>线窗口的宽度（含发光范围）。</summary>
        public static int LineWidth(int quality)
        {
            if (quality <= 1) return 3;
            if (quality == 2) return 16;
            if (quality == 3) return 24;
            return 34;
        }

        public static HoloFrame Compute(double p, Rectangle box, Rectangle screen, int quality)
        {
            p = UiAnim.Clamp01(p);
            HoloFrame f = new HoloFrame();
            f.Progress = p;

            int lw = LineWidth(quality);
            double cx = box.Left + box.Width / 2.0;
            int startY = screen.Bottom + 40;

            if (p <= VerticalEnd)
            {
                // ① 纵向拉伸：两根线拼成**一根**连续的竖线（上半截 + 下半截），
                //    整根线从屏幕下方升上来，长度从 0 长到面板高度。
                double e = UiAnim.EaseOutCubic(p / VerticalEnd);
                f.ShowTop = true;
                f.ShowBottom = true;
                f.TopAlpha = (float)UiAnim.Clamp01(e * 1.6);
                f.BottomAlpha = (float)UiAnim.Clamp01(e * 1.6);

                double lineH = e * box.Height;
                int topY = (int)Math.Round(UiAnim.Lerp(startY, box.Top, e));
                int halfH = (int)Math.Round(lineH / 2.0);
                if (halfH < 2) halfH = 2;

                int x = (int)Math.Round(cx) - lw / 2;
                // 上半截 + 下半截严丝合缝 → 看起来就是一根线
                f.TopLine = new Rectangle(x, topY, lw, halfH);
                f.BottomLine = new Rectangle(x, topY + halfH, lw, halfH);
                f.PanelVisible = false;
                f.ClipWidth = 0;
                f.PanelAlpha = 0f;
                return f;
            }

            if (p <= HorizontalEnd)
            {
                // ② 横向分裂：这一根线从中间断开 —— 上半截往左、下半截往右，
                //    同时各自长到面板高度，面板在两根线中间变宽。
                double q = (p - VerticalEnd) / (HorizontalEnd - VerticalEnd);
                double e = UiAnim.EaseInOutQuad(q);

                int leftX = (int)Math.Round(UiAnim.Lerp(cx, box.Left, e)) - lw / 2;
                int rightX = (int)Math.Round(UiAnim.Lerp(cx, box.Right, e)) - lw / 2;
                int halfH = (int)Math.Round(UiAnim.Lerp(box.Height / 2.0, box.Height, e));
                int topY = box.Top;
                int botY = (int)Math.Round(UiAnim.Lerp(box.Top + box.Height / 2.0, box.Top, e));

                f.ShowTop = true;
                f.ShowBottom = true;
                f.TopAlpha = 1f;
                f.BottomAlpha = 1f;
                f.TopLine = new Rectangle(leftX, topY, lw, halfH);
                f.BottomLine = new Rectangle(rightX, botY, lw, halfH);

                f.PanelVisible = q > 0.02;
                f.PanelAlpha = (float)UiAnim.Clamp01(q * 3.0);
                // 裁出来的宽度 = 两根线之间的距离
                double w = (UiAnim.Lerp(cx, box.Right, e) - UiAnim.Lerp(cx, box.Left, e));
                f.ClipWidth = (int)Math.Round(Math.Max(0, w));
                return f;
            }

            // ③ 收尾：右边那根加速飞出去；左边那根留在面板左边缘当装饰。
            double r = (p - HorizontalEnd) / (1 - HorizontalEnd);
            f.ShowTop = true;
            f.ShowBottom = quality >= 2;      // 低档不飞线，直接消失
            f.TopAlpha = 1f;
            f.TopLine = new Rectangle(box.Left - lw / 2, box.Top, lw, box.Height);

            if (f.ShowBottom)
            {
                double e = UiAnim.EaseInCubic(r);
                int x = (int)Math.Round(UiAnim.Lerp(box.Right, screen.Right + lw + 80, e)) - lw / 2;
                f.BottomLine = new Rectangle(x, box.Top, lw, box.Height);
                f.BottomAlpha = (float)(1 - UiAnim.EaseInOutQuad(UiAnim.Clamp01((r - 0.55) / 0.45)));
            }

            f.PanelVisible = true;
            f.PanelAlpha = 1f;
            f.ClipWidth = 0;   // 0 表示"不裁，整宽"
            return f;
        }
    }
}
