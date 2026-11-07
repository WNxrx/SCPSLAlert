using System;
using System.Runtime.InteropServices;

namespace SCPSLAlert
{
    /// <summary>
    /// 需要用到的 Win32 API。只用于设置窗口样式、绘制分层窗口、全局热键和托盘图标，
    /// 不涉及任何游戏进程的内存读写或注入。
    /// </summary>
    internal static class NativeMethods
    {
        #region 窗口样式

        public const int GWL_EXSTYLE = -20;

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        #endregion

        #region 分层窗口（逐像素透明：圆角半透明背景 + 平滑淡入淡出）

        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        // ---- 供悬浮面板用的 DIB 画布（每帧推屏不再 GetHbitmap 拷贝整张图）----
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi,
            uint usage, out IntPtr bits, IntPtr hSection, uint offset);

        public const int BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool GetDeviceGammaRamp(IntPtr hDC, [Out] ushort[] ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool SetDeviceGammaRamp(IntPtr hDC, ushort[] ramp);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        // ---- 整窗缩略采样：一次 StretchBlt 把游戏客户区缩到 192×108，
        //      比 CopyFromScreen 整屏再缩放便宜一个数量级（亮度采样要每 150ms 跑一次）。----
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, int srcWidth, int srcHeight, int rop);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern int SetStretchBltMode(IntPtr hdc, int mode);

        public const int SRCCOPY = 0x00CC0020;
        public const int HALFTONE = 4;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        #endregion

        #region 图标 / 热键

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const int WM_HOTKEY = 0x0312;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        /// <summary>查某个虚拟键当前是否按下（用于“按住显示全部”这种需要检测松开的热键）。</summary>
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;   // Alt

        #endregion

        #region 焦点管理（悬浮面板要能收键盘，关掉后要把焦点还给游戏）

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>窗口是否已经"无响应"（Alt+F4 按在卡死的游戏上时用来区分）。</summary>
        [DllImport("user32.dll")]
        public static extern bool IsHungAppWindow(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        public const int HSHELL_REDRAW = 0x0006;

        [DllImport("user32.dll")]
        public static extern bool SetFocus(IntPtr hWnd);

        /// <summary>不改变 Z 序、不激活窗口，只把键盘焦点给过去并带回前台。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr hWnd);

        /// <summary>把窗口提到顶层但不抢焦点（SWP_NOACTIVATE）。</summary>
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, int uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;

        /// <summary>把某个窗口设为前台。失败时退回 AttachThreadInput 的办法。</summary>
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        /// <summary>
        /// 强行把键盘焦点交给 hWnd。
        /// Windows 不允许后台进程随便抢前台，所以要先把本线程挂到当前前台窗口的
        /// 输入队列上，设完焦点再摘掉 —— 这是让悬浮面板能收到按键的标准做法。
        /// </summary>
        public static void ForceFocus(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            IntPtr fore = GetForegroundWindow();
            uint myTid = GetCurrentThreadId();
            uint foreTid = fore == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fore, IntPtr.Zero);

            bool attached = false;
            if (foreTid != 0 && foreTid != myTid)
                attached = AttachThreadInput(myTid, foreTid, true);
            try
            {
                SetForegroundWindow(hWnd);
                SetActiveWindow(hWnd);
                SetFocus(hWnd);
            }
            finally
            {
                if (attached) AttachThreadInput(myTid, foreTid, false);
            }
        }

        #endregion

        #region 让本程序的窗口"对屏幕截图不可见"

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private static bool _captureExcludeLogged;
        private static bool _captureHideOffLogged;

        /// <summary>
        /// 要不要把本程序的窗口设成"截图/录制里不出现"。
        /// 由配置 `[General] HideFromCapture` 决定（设置窗口里那个勾），
        /// 每次应用配置时同步过来。默认开（防止自己被自己的 OCR 认进去）。
        /// </summary>
        public static bool CaptureHideEnabled = true;

        private const uint WDA_NONE = 0x00000000;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        /// <summary>
        /// 把本程序自己所有已经存在的窗口恢复成"可以被截图/录制"。
        ///
        /// 【为什么需要】窗口那个"对截图不可见"标志是在窗口创建时设的；
        /// 操作者在设置里把这个勾去掉之后，已经弹出来的提示窗还带着旧标志，
        /// 于是"改了没生效"（那条系统提示还在、录屏还是录不到）。
        /// 这里遍历本进程的顶层窗口挨个清掉，改完立刻见效。
        /// ⚠ 只动 pid 等于自己的窗口 —— 绝不能去碰别的程序（那是别人的东西）。
        /// </summary>
        public static int ShowAllInCapture()
        {
            int n = 0;
            try
            {
                uint myPid = GetCurrentProcessId();
                EnumWindows(delegate(IntPtr h, IntPtr p)
                {
                    try
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != myPid) return true;          // 只动自己的窗口
                        if (SetWindowDisplayAffinity(h, WDA_NONE)) n++;
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Logger.Err("OCR", "恢复窗口录制可见性失败", ex); }
            return n;
        }

        private const uint WDA_MONITOR = 0x00000001;              // 截屏里显示为全黑（Win7+）
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;   // 截屏里完全不出现（Win10 2004+）

        /// <summary>
        /// 让某个窗口"用户看得见、但屏幕截图里没有"。
        ///
        /// 【为什么需要】OCR 是直接截屏的。本程序自己的提示窗、面板、左下角那块
        /// OCR 捕获提示，只要压在识别区域上就会被自己截进去 ——
        /// 于是出现"OCR 把自己的字认成游戏播报"这种自我循环。
        ///
        /// WDA_EXCLUDEFROMCAPTURE 就是 Windows 为此提供的开关：窗口照常显示给用户，
        /// 但 CopyFromScreen / BitBlt 截到的画面里它不出现，露出底下的真实画面（游戏）。
        ///
        /// 老系统（Win10 2004 之前）不支持这个标志，退回 WDA_MONITOR ——
        /// 那会让窗口在截图里变成纯黑，对 OCR 同样无害（没有亮像素可认）。
        /// </summary>
        public static bool HideFromCapture(IntPtr hwnd, string what)
        {
            if (hwnd == IntPtr.Zero) return false;

            // 【可关掉】操作者要求：这个"对录制不可见"会让 Windows 弹
            // 「受保护的应用程序阻止桌面录制」，还会让 OBS 之类的录屏软件
            // 录不到本程序的窗口。设置里「常规 → 屏幕 OCR 播报识别 →
            // 本程序的提示窗在截图/录制里隐藏」可以关掉它（config.ini 的
            // [General] HideFromCapture=0）。关掉之后本程序的窗口就能被录进去，
            // 代价是它压在识别区域上时可能被自己的 OCR 认进去。
            if (!CaptureHideEnabled)
            {
                if (!_captureHideOffLogged)
                {
                    _captureHideOffLogged = true;
                    Logger.On("OCR", "按设置不在截图/录屏里隐藏本程序的窗口（" + what
                        + "）—— 录屏能录到了，也没有「受保护的应用程序阻止录制」那个提示；"
                        + "如果发现程序把自己的提示字认成播报，就把那个勾重新打开");
                }
                return false;
            }

            try
            {
                if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
                {
                    if (!_captureExcludeLogged)
                    {
                        _captureExcludeLogged = true;
                        Logger.On("OCR", "「" + what + "」已设为『截图里不出现』，"
                            + "不会再被自己识别进去（其它同类窗口同样处理）");
                    }
                    return true;
                }

                int err1 = Marshal.GetLastWin32Error();
                if (SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                {
                    Logger.On("OCR", "「" + what + "」在截图里会显示为黑块（这台系统的版本"
                        + "不支持『完全不出现』，但同样不会被认进去）");
                    return true;
                }

                Logger.On("OCR", "「" + what + "」没法从截图里排除（err=" + err1
                    + "）—— 它压在识别区域上时可能被自己认进去");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Err("OCR", "设置窗口截图排除失败：" + what, ex);
                return false;
            }
        }

        #endregion

        #region 托盘图标：消息过滤放行 + 资源管理器重启后重建

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message,
            uint action, IntPtr pChangeFilterStruct);

        private const uint MSGFLT_ALLOW = 1;

        /// <summary>资源管理器重启后会广播这条消息，收到就该把托盘图标重新加回去。</summary>
        public static readonly int WM_TASKBARCREATED =
            unchecked((int)RegisterWindowMessage("TaskbarCreated"));

        /// <summary>拖放 / 资源管理器与本程序之间传数据用的消息。</summary>
        public const int WM_COPYDATA = 0x004A;

        /// <summary>
        /// 放行一条消息，让它在"高权限 ↔ 低权限进程"之间也能送达。
        ///
        /// 【为什么必须要】托盘图标归 explorer.exe 管，而它是普通权限的。
        /// 本程序现在是以管理员身份运行的（为了和游戏权限一致，见 app.manifest），
        /// UIPI 会把高权限进程发往低权限窗口的某些消息直接挡掉 ——
        /// 表现就是"托盘图标创建不出来"。TaskbarCreated 就是典型的一条
        /// （资源管理器重启后的重建通知）。
        /// </summary>
        public static bool AllowMessage(IntPtr hWnd, int message)
        {
            if (hWnd == IntPtr.Zero || message == 0) return false;
            try
            {
                bool ok = ChangeWindowMessageFilterEx(hWnd, (uint)message, MSGFLT_ALLOW, IntPtr.Zero);
                Logger.On("托盘", "放行消息 0x" + message.ToString("X4")
                    + "（" + (message == WM_TASKBARCREATED ? "TaskbarCreated" : "WM_COPYDATA") + "）→ "
                    + (ok ? "成功" : "失败 err=" + Marshal.GetLastWin32Error()));
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Err("托盘", "放行消息失败", ex);
                return false;
            }
        }

        #endregion

        #region 打开文件 / 定位文件（不动文件关联，也不新起 explorer 进程）

        private const int ASSOCF_NONE = 0;
        private const int ASSOCSTR_EXECUTABLE = 2;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern uint AssocQueryStringW(int flags, int str, string pszAssoc,
            string pszExtra, System.Text.StringBuilder pszOut, ref uint pcchOut);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
            out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl,
            IntPtr apidl, uint dwFlags);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern void ILFree(IntPtr pidl);

        /// <summary>
        /// 这个扩展名有没有关联程序（比如 .txt 有记事本，.log 很多机器上就没有）。
        /// 只用来排查问题：实测 .log 常常返回 0x80070483（没有关联）。
        /// </summary>
        public static bool HasFileAssociation(string ext)
        {
            try
            {
                if (string.IsNullOrEmpty(ext)) return false;
                if (ext[0] != '.') ext = "." + ext;

                uint len = 1024;
                System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
                uint hr = AssocQueryStringW(ASSOCF_NONE, ASSOCSTR_EXECUTABLE, ext, null, sb, ref len);
                return hr == 0 && sb.Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 在资源管理器里打开文件所在目录并选中它。
        ///
        /// 用的是 Shell 的 SHOpenFolderAndSelectItems：它把这件事交给已经在跑的
        /// 资源管理器去做，不会另外起一个 explorer.exe 进程
        /// （另起进程在有些环境下会报 0xc0000142 直接起不来）。
        /// </summary>
        public static bool RevealInExplorer(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;

            IntPtr pidl = IntPtr.Zero;
            try
            {
                uint attrs;
                int hr = SHParseDisplayName(file, IntPtr.Zero, out pidl, 0, out attrs);
                if (hr != 0 || pidl == IntPtr.Zero) return false;

                // cidl = 0 时，pidlFolder 传"要选中的那一项"即可 —— 资源管理器会
                // 打开它所在的目录并把它选上。
                hr = SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
                return hr == 0;
            }
            catch { return false; }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    try { ILFree(pidl); } catch { }
                }
            }
        }

        /// <summary>
        /// 打开文件所在目录并尽量把它选中。
        /// 三级兜底：Shell API（不新起进程）→ explorer.exe 开目录 → 返回 false 让调用方把路径告诉用户。
        /// </summary>
        public static bool OpenContainingFolder(string file)
        {
            try
            {
                if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file)
                    && RevealInExplorer(file)) return true;
            }
            catch { }

            try
            {
                string dir = string.IsNullOrEmpty(file) ? null : System.IO.Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir)) return false;
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                return true;
            }
            catch { return false; }
        }

        #endregion
    }
}
