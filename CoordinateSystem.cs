using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 归一化坐标：0..1，左上角为原点，X 向右、Y 向下。
    ///
    /// 基准由调用方给出，存 config 时一律附带基准说明：
    ///   · 提示 / 心率 / 图片 / 悬浮窗：目标显示器物理像素（Monitor）；
    ///   · 准星：游戏客户区中心 + 物理像素偏移（GameClient）；
    ///   · OCR / 截屏：游戏客户区物理像素（GameClient）。
    ///
    /// 不存 0..100：0..1 的 double 在 1080p 下仍有约 0.002px 的精度，
    /// 而 1% 在 1080p 已经是 19.2px，无法满足“点击与视觉误差 ≤1 物理像素”。
    /// </summary>
    internal struct NormalizedPoint
    {
        public double X;
        public double Y;

        public NormalizedPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public bool IsValid
        {
            get
            {
                return !double.IsNaN(X) && !double.IsNaN(Y)
                    && !double.IsInfinity(X) && !double.IsInfinity(Y)
                    && X >= 0.0 && X <= 1.0 && Y >= 0.0 && Y <= 1.0;
            }
        }
    }

    /// <summary>
    /// 全软件唯一的屏幕坐标入口。
    ///
    /// 进程启用 PerMonitorV2 后，Win32 / WinForms / WPF 拿到的屏幕坐标都是物理像素；
    /// 这里只做三件事：找基准矩形、0..1 与物理像素互转、找游戏客户区。
    /// 所有“百分比”都必须写明基准，不允许在页面里各自猜。
    /// </summary>
    internal static class ScreenGeometry
    {
        private static readonly object GameLock = new object();
        private static IntPtr _gameHwnd = IntPtr.Zero;
        private static DateTime _gameHwndAt = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public static Rectangle VirtualDesktop
        {
            get
            {
                try { return SystemInformation.VirtualScreen; }
                catch { return new Rectangle(0, 0, 1920, 1080); }
            }
        }

        public static Rectangle PrimaryBounds
        {
            get
            {
                try
                {
                    Screen s = Screen.PrimaryScreen;
                    return s == null ? new Rectangle(0, 0, 1920, 1080) : s.Bounds;
                }
                catch { return new Rectangle(0, 0, 1920, 1080); }
            }
        }

        public static Rectangle PrimaryWorkArea
        {
            get
            {
                try
                {
                    Screen s = Screen.PrimaryScreen;
                    return s == null ? PrimaryBounds : s.WorkingArea;
                }
                catch { return PrimaryBounds; }
            }
        }

        public static Rectangle MonitorBounds(Point physicalPoint)
        {
            try
            {
                Screen s = Screen.FromPoint(physicalPoint);
                return s == null ? PrimaryBounds : s.Bounds;
            }
            catch { return PrimaryBounds; }
        }

        public static Rectangle MonitorWorkArea(Point physicalPoint)
        {
            try
            {
                Screen s = Screen.FromPoint(physicalPoint);
                return s == null ? PrimaryWorkArea : s.WorkingArea;
            }
            catch { return PrimaryWorkArea; }
        }

        public static string MonitorDevice(Point physicalPoint)
        {
            try
            {
                Screen s = Screen.FromPoint(physicalPoint);
                return s == null ? "" : s.DeviceName;
            }
            catch { return ""; }
        }

        public static Rectangle MonitorByDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return Rectangle.Empty;
            try
            {
                Screen[] all = Screen.AllScreens;
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                        return all[i].Bounds;
                }
            }
            catch { }
            return Rectangle.Empty;
        }

        /// <summary>
        /// 游戏客户区（不含标题栏/边框）的屏幕物理像素矩形。
        /// 游戏没启动或窗口最小化时返回 Empty，由调用方决定回落到主屏还是虚拟桌面。
        /// </summary>
        public static Rectangle GameClientBounds()
        {
            IntPtr hwnd = FindGameWindow();
            if (hwnd == IntPtr.Zero) return Rectangle.Empty;
            try
            {
                RECT cr;
                if (!GetClientRect(hwnd, out cr)) return Rectangle.Empty;
                int w = cr.Right - cr.Left;
                int h = cr.Bottom - cr.Top;
                if (w <= 0 || h <= 0) return Rectangle.Empty;
                POINT origin = new POINT();
                if (!ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;
                return new Rectangle(origin.X, origin.Y, w, h);
            }
            catch { return Rectangle.Empty; }
        }

        public static Rectangle GameClientOrPrimary()
        {
            Rectangle r = GameClientBounds();
            return r.IsEmpty ? PrimaryBounds : r;
        }

        /// <summary>
        /// 找 SCPSL 主窗口。优先前台窗口，其次按进程名找；
        /// 1 秒缓存，避免 OCR 每个区域都枚举一遍进程。
        /// </summary>
        public static IntPtr FindGameWindow()
        {
            lock (GameLock)
            {
                DateTime now = DateTime.UtcNow;
                if (_gameHwnd != IntPtr.Zero && (now - _gameHwndAt).TotalMilliseconds < 1000)
                {
                    try { if (IsWindow(_gameHwnd)) return _gameHwnd; } catch { }
                }

                IntPtr fg = IntPtr.Zero;
                try { fg = GetForegroundWindow(); } catch { }
                if (IsGameWindow(fg))
                {
                    _gameHwnd = fg;
                    _gameHwndAt = now;
                    return _gameHwnd;
                }

                string[] names = { "SCPSL", "SCP Secret Laboratory" };
                for (int n = 0; n < names.Length; n++)
                {
                    try
                    {
                        Process[] all = Process.GetProcessesByName(names[n]);
                        for (int i = 0; i < all.Length; i++)
                        {
                            Process p = all[i];
                            try
                            {
                                IntPtr h = p.MainWindowHandle;
                                if (h != IntPtr.Zero && IsWindow(h))
                                {
                                    _gameHwnd = h;
                                    _gameHwndAt = now;
                                    return h;
                                }
                            }
                            catch { }
                            finally { try { p.Dispose(); } catch { } }
                        }
                    }
                    catch { }
                }

                _gameHwnd = IntPtr.Zero;
                _gameHwndAt = now;
                return IntPtr.Zero;
            }
        }

        public static bool IsGameWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;
                using (Process p = Process.GetProcessById((int)pid))
                {
                    string name = p.ProcessName ?? "";
                    if (name.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)) return false;
                    return name.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        /// <summary>窗口所在显示器的 DPI 缩放（1.0 = 100%）。</summary>
        public static double DpiScale(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi >= 48) return dpi / 96.0;
                }
                catch { }
                try
                {
                    using (Graphics g = Graphics.FromHwnd(hwnd))
                        if (g != null && g.DpiX > 0) return g.DpiX / 96.0;
                }
                catch { }
            }
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    if (g != null && g.DpiX > 0) return g.DpiX / 96.0;
            }
            catch { }
            return 1.0;
        }

        /// <summary>某个物理点所在显示器的 DPI（准星跨屏时取目标屏，而不是主屏）。</summary>
        public static double DpiScaleForPoint(Point physicalPoint)
        {
            try
            {
                POINT p = new POINT();
                p.X = physicalPoint.X;
                p.Y = physicalPoint.Y;
                IntPtr mon = MonitorFromPoint(p, MonitorDefaultToNearest);
                if (mon != IntPtr.Zero)
                {
                    uint dx, dy;
                    if (GetDpiForMonitor(mon, MdtEffectiveDpi, out dx, out dy) == 0 && dx >= 48)
                        return dx / 96.0;
                }
            }
            catch { }
            return DpiScale(IntPtr.Zero);
        }

        public static NormalizedPoint Normalize(Point physicalPoint, Rectangle basis)
        {
            if (basis.Width <= 0 || basis.Height <= 0) return new NormalizedPoint(0, 0);
            double x = (physicalPoint.X - basis.Left) / (double)basis.Width;
            double y = (physicalPoint.Y - basis.Top) / (double)basis.Height;
            return new NormalizedPoint(Clamp01(x), Clamp01(y));
        }

        public static Point Denormalize(NormalizedPoint normalized, Rectangle basis)
        {
            if (basis.Width <= 0 || basis.Height <= 0) return new Point(0, 0);
            int x = (int)Math.Round(basis.Left + Clamp01(normalized.X) * basis.Width);
            int y = (int)Math.Round(basis.Top + Clamp01(normalized.Y) * basis.Height);
            return new Point(x, y);
        }

        public static Rectangle DenormalizeRect(NormalizedPoint topLeft, double width, double height, Rectangle basis)
        {
            if (basis.Width <= 0 || basis.Height <= 0) return Rectangle.Empty;
            int x = (int)Math.Round(basis.Left + Clamp01(topLeft.X) * basis.Width);
            int y = (int)Math.Round(basis.Top + Clamp01(topLeft.Y) * basis.Height);
            int w = Math.Max(1, (int)Math.Round(Clamp01(width) * basis.Width));
            int h = Math.Max(1, (int)Math.Round(Clamp01(height) * basis.Height));
            return new Rectangle(x, y, w, h);
        }

        /// <summary>把矩形约束在虚拟桌面内；副屏负坐标同样按虚拟桌面左上角计算。</summary>
        public static Rectangle ClampToVirtual(Rectangle r)
        {
            Rectangle v = VirtualDesktop;
            if (r.Width <= 0 || r.Height <= 0 || v.Width <= 0 || v.Height <= 0) return r;
            int x = Math.Max(v.Left, Math.Min(r.Left, v.Right - 1));
            int y = Math.Max(v.Top, Math.Min(r.Top, v.Bottom - 1));
            int right = Math.Max(x + 1, Math.Min(r.Right, v.Right));
            int bottom = Math.Max(y + 1, Math.Min(r.Bottom, v.Bottom));
            return new Rectangle(x, y, right - x, bottom - y);
        }

        public static double Clamp01(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        public static Rectangle ResolveMonitor(string deviceName, Point fallbackPhysicalPoint)
        {
            Rectangle r = MonitorByDevice(deviceName);
            if (!r.IsEmpty) return r;
            return MonitorBounds(fallbackPhysicalPoint);
        }
    }
}
