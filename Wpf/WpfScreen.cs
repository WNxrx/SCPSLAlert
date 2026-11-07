using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SCPSLAlert
{
    /// <summary>
    /// WPF 窗口的物理像素定位。
    ///
    /// 为什么不用 Window.Left / Top：PerMonitorV2 下 WPF 的 Left/Top 是 DIP，
    /// 且不同显示器的 DIP 基准不同；跨屏、负坐标、125/150/200% 会互相打架。
    /// 这里统一走 SetWindowPos，传入的是物理像素，和 ScreenGeometry、config 完全一致。
    /// </summary>
    internal static class WpfScreen
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoZOrder = 0x0004;

        /// <summary>把窗口左上角移动到物理像素 (x,y)，不改变尺寸、不抢焦点。</summary>
        public static bool Move(Window window, int x, int y)
        {
            if (window == null) return false;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // 句柄还没创建：先按 DIP 放，SourceInitialized 后会再走一次物理定位。
                double scale = 1.0;
                try { scale = ScreenGeometry.DpiScale(IntPtr.Zero); }
                catch { }
                window.Left = x / Math.Max(0.5, scale);
                window.Top = y / Math.Max(0.5, scale);
                return false;
            }
            try
            {
                return SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0,
                    SwpNoSize | SwpNoActivate | SwpNoZOrder);
            }
            catch { return false; }
        }

        /// <summary>读窗口物理像素位置；读不到时返回 Empty。</summary>
        public static Rectangle GetBounds(Window window)
        {
            if (window == null) return Rectangle.Empty;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return Rectangle.Empty;
            try
            {
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return Rectangle.Empty;
                return new Rectangle(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
            }
            catch { return Rectangle.Empty; }
        }
    }
}
