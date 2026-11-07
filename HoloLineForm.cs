using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 全息动画用的一根"发光竖线"窗口。
    ///
    /// 特点（按操作者要求）：
    ///   · 无边框、透明背景、逐像素透明（UpdateLayeredWindow）；
    ///   · **不抢焦点**：WS_EX_NOACTIVATE + WS_EX_TRANSPARENT + ShowWithoutActivation；
    ///   · 对截图不可见（和别的提示窗一样），不会被自己的 OCR 认进去；
    ///   · 发光 = 中间一条亮芯 + 左右两侧的横向渐隐；极高档再多一层外圈光。
    /// 面板负责算位置，这里只负责"把这一帧画出来"。
    ///
    /// 性能：画布复用（高度变化只是少画一点），不每帧 new 位图 ——
    /// 极高档 120fps 时这点分配很关键。
    /// </summary>
    internal sealed class HoloLineForm : Form
    {
        private readonly bool _glow;
        private readonly bool _extra;
        private Bitmap _buf;

        private static readonly Color CoreColor = Color.FromArgb(245, 255, 255);

        public HoloLineForm(bool glow, bool extra)
        {
            _glow = glow;
            _extra = extra;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TRANSPARENT
                            | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.HideFromCapture(Handle, "全息动画线");
        }

        /// <summary>画一根竖线并推到屏幕上（rect 是屏幕坐标）。</summary>
        public void ShowLine(Rectangle screenRect, Color color, float alpha, double pulse)
        {
            if (screenRect.Width < 1 || screenRect.Height < 1 || alpha <= 0.02f)
            {
                HideLine();
                return;
            }

            EnsureBuffer(screenRect.Width, screenRect.Height);
            Render(color, alpha, pulse, screenRect.Height);

            if (!Visible)
            {
                Show();
                RaiseTopMost();     // 只在第一次显示时抢一次置顶，之后别再每帧 SetWindowPos（会闪）
            }
            Push(screenRect);
        }

        /// <summary>把线窗口重新提到最前（面板内容刷新时不会被盖住闪一下）。</summary>
        public void RaiseTopMost()
        {
            if (!IsHandleCreated) return;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        }

        public void HideLine()
        {
            if (Visible) Hide();
        }

        /// <summary>
        /// 复用一块"够大"的画布：每帧线的高度都在变，但不需要每帧重新 new 位图。
        /// 画的时候只画上面 h 像素，推屏时 ULW 的 size 也只取 h。
        /// </summary>
        private void EnsureBuffer(int w, int h)
        {
            if (_buf != null && _buf.Width == w && _buf.Height >= h) return;
            int newH = (_buf != null && _buf.Width == w) ? Math.Max(h, _buf.Height) : h;
            if (_buf != null) _buf.Dispose();
            _buf = new Bitmap(w, newH, PixelFormat.Format32bppArgb);
        }

        private void Render(Color color, float alpha, double pulse, int height)
        {
            using (Graphics g = Graphics.FromImage(_buf))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.Clear(Color.Transparent);

                int w = _buf.Width;
                int h = Math.Min(height, _buf.Height);
                int cx = w / 2;

                // 极高档：整体亮度做一点点呼吸（pulse 0~1）
                float breath = _extra ? (float)(0.86 + 0.14 * pulse) : 1f;
                int a0 = (int)Math.Round(alpha * breath * 255);
                if (a0 < 0) a0 = 0;
                if (a0 > 255) a0 = 255;

                // ---- 发光：横向渐隐 ----
                if (_glow)
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(2, w), 1), Color.Transparent, Color.Transparent, 0f))
                    {
                        ColorBlend cb = new ColorBlend(5);
                        cb.Colors = new Color[]
                        {
                            Color.FromArgb(0, color),
                            Color.FromArgb((int)(a0 * 0.28), color),
                            Color.FromArgb((int)(a0 * 0.85), color),
                            Color.FromArgb((int)(a0 * 0.28), color),
                            Color.FromArgb(0, color),
                        };
                        cb.Positions = new float[] { 0f, 0.30f, 0.5f, 0.70f, 1f };
                        br.InterpolationColors = cb;
                        g.FillRectangle(br, 0, 0, w, h);
                    }
                }

                // ---- 亮芯：两个像素宽（低档 1 像素），上下两端稍淡 ----
                int coreW = _glow ? 2 : 1;
                Rectangle core = new Rectangle(cx - coreW / 2, 0, coreW, h);
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, coreW), Math.Max(2, h)),
                    Color.Transparent, Color.Transparent, 90f))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[]
                    {
                        Color.FromArgb((int)(a0 * 0.35), CoreColor),
                        Color.FromArgb(a0, CoreColor),
                        Color.FromArgb((int)(a0 * 0.35), CoreColor),
                    };
                    cb.Positions = new float[] { 0f, 0.5f, 1f };
                    br.InterpolationColors = cb;
                    g.FillRectangle(br, core);
                }

                // ---- 极高档：再叠一层更细的外圈光 ----
                if (_extra)
                {
                    using (Pen p = new Pen(Color.FromArgb((int)(a0 * 0.22), color), 1f))
                    {
                        int d = Math.Max(3, w / 6);
                        g.DrawLine(p, cx - d, 2, cx - d, h - 2);
                        g.DrawLine(p, cx + d, 2, cx + d, h - 2);
                    }
                }
            }
        }

        private void Push(Rectangle screenRect)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = _buf.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE();
                size.cx = _buf.Width;
                size.cy = Math.Min(screenRect.Height, _buf.Height);

                NativeMethods.POINT src = new NativeMethods.POINT();
                NativeMethods.POINT dst = new NativeMethods.POINT();
                dst.X = screenRect.X;
                dst.Y = screenRect.Y;

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    NativeMethods.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_buf != null) { try { _buf.Dispose(); } catch { } _buf = null; }
            }
            base.Dispose(disposing);
        }
    }
}
