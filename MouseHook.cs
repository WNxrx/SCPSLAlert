using System;
using System.Runtime.InteropServices;

namespace SCPSLAlert
{
    /// <summary>
    /// 鼠标低级钩子（WH_MOUSE_LL），只旁听左键按下/松开，
    /// 不吞消息、不改变任何行为 —— 游戏照常收到点击。
    /// 用来驱动准星的「按住扩散 / 松开回落」。
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int ptX;
            public int ptY;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        /// <summary>左键按下 / 松开。参数是鼠标键 0=左 1=右 2=中。</summary>
        public event Action<int, bool> ButtonChanged;

        public bool Installed { get { return _hook != IntPtr.Zero; } }
        public string FailReason = "";

        public MouseHook()
        {
            _proc = Callback;
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                IntPtr mod = GetModuleHandle(null);
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, mod, 0);
                if (_hook == IntPtr.Zero)
                {
                    FailReason = "SetWindowsHookEx(WH_MOUSE_LL) 失败，错误码 " + Marshal.GetLastWin32Error();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                FailReason = ex.Message;
                return false;
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    int button = -1;
                    bool down = false;
                    switch (msg)
                    {
                        case WM_LBUTTONDOWN: button = 0; down = true; break;
                        case WM_LBUTTONUP: button = 0; down = false; break;
                        case WM_RBUTTONDOWN: button = 1; down = true; break;
                        case WM_RBUTTONUP: button = 1; down = false; break;
                        case WM_MBUTTONDOWN: button = 2; down = true; break;
                        case WM_MBUTTONUP: button = 2; down = false; break;
                    }
                    if (button >= 0)
                    {
                        Action<int, bool> h = ButtonChanged;
                        if (h != null)
                        {
                            try { h(button, down); }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hook); }
                catch { }
                _hook = IntPtr.Zero;
            }
        }
    }
}
