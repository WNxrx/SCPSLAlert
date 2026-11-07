using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 低级键盘钩子（WH_KEYBOARD_LL）。
    ///
    /// 为什么不用 RegisterHotKey：
    ///   · 某个组合被别的程序占了就注册失败，而且不报错，用户只感觉“按了没反应”；
    ///   · 前台窗口若是管理员权限，非管理员进程的热键收不到（UIPI）；
    ///   · 注册成功后会把那个键从游戏手里“抢走”。
    ///
    /// 钩子的好处：
    ///   · 不占用按键 —— 我们只“看一眼”，按键照样传给游戏；
    ///   · 别的程序抢占了也能收到；
    ///   · 不会因为注册失败而整个功能失灵。
    ///
    /// 说明：如果前台窗口以管理员身份运行，非管理员进程的钩子同样收不到按键
    /// （这是 Windows 的 UIPI 限制，任何用户态方案都绕不过）。这种情况需要
    /// 本程序也用管理员身份运行。
    /// </summary>
    internal class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

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
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // 必须保住这个委托的引用，否则被 GC 回收之后回调会指向野地址直接崩
        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        // 修饰键状态自己维护，比在回调里问 GetAsyncKeyState 可靠
        private bool _ctrl, _alt, _shift, _win;

        /// <summary>收到一个“完整”的组合键（带了非修饰键）。参数：虚拟键码、Ctrl、Alt、Shift、Win。</summary>
        public event Action<int, bool, bool, bool, bool> Combo;

        /// <summary>
        /// 第 5 批：按键过滤 / 拦截。
        ///
        /// 参数是格式化后的组合键文本（和 config.ini 里写的一样）。
        /// 返回值：
        ///   · -1 = 不是我们的键：不拦，交给游戏；
        ///   ·  0 = 是我们的键，但"后台可用 = 开"：触发功能，**照样传给游戏**；
        ///   ·  1 = 是我们的键，且游戏在前台：触发功能并**吞掉按键**（防泄漏）。
        ///
        /// 必须在回调里立刻返回，这里绝不能做重活。
        /// </summary>
        public Func<string, int> Filter;

        /// <summary>
        /// 临时挂起。
        /// 设置窗口正在录快捷键时必须挂起 —— 否则用户想给 A 功能设 F10，
        /// 按下去的一瞬间 F10 就把 B 功能调出来了。
        ///
        /// 【为什么带自动过期】
        /// 它是个静态标志：只要有一次"进了录入框但没能正常退出"，
        /// 它就会一直 true —— 现象就是**所有快捷键永久失效**，
        /// 只能重启程序。所以这里改成带截止时间：最长挂起 1 分钟，
        /// 之后自动失效，最坏情况也能自己恢复。
        /// </summary>
        private const int SuspendMs = 60000;
        private static int _suspendUntil;

        public static bool Suspended
        {
            get
            {
                int until = _suspendUntil;
                if (until == 0) return false;
                // 用 TickCount 差值判断，天然处理回绕
                return unchecked(Environment.TickCount - until) < 0;
            }
            set
            {
                _suspendUntil = value ? unchecked(Environment.TickCount + SuspendMs) : 0;
            }
        }

        public bool Installed
        {
            get { return _hook != IntPtr.Zero; }
        }

        /// <summary>安装失败时的原因（给界面显示用）。</summary>
        public string FailReason = "";

        public KeyboardHook()
        {
            _proc = Callback;
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                IntPtr mod = GetModuleHandle(null);
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, mod, 0);
                if (_hook == IntPtr.Zero)
                {
                    FailReason = "SetWindowsHookEx 失败，错误码 " + Marshal.GetLastWin32Error();
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

        /// <summary>
        /// 重新挂一次钩子（先摘再挂）。
        ///
        /// 为什么需要：低级键盘钩子一旦在回调里超时，Windows 会**静默地**
        /// 把它摘掉，而且不会给任何通知 —— 于是所有快捷键就永久失效了。
        /// 所以外层要定期调一下这个，把钩子重新挂上，最坏情况也能自愈。
        /// </summary>
        public bool Rearm()
        {
            try
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
            }
            catch { }
            return Install();
        }

        private static bool IsModifier(int vk)
        {
            return vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU
                || vk == VK_LWIN || vk == VK_RWIN
                || vk == 0xA0 || vk == 0xA1   // L/R Shift
                || vk == 0xA2 || vk == 0xA3   // L/R Control
                || vk == 0xA4 || vk == 0xA5;  // L/R Menu
        }

        private void UpdateModifier(int vk, bool down)
        {
            switch (vk)
            {
                case VK_SHIFT:
                case 0xA0: case 0xA1:
                    _shift = down; break;
                case VK_CONTROL:
                case 0xA2: case 0xA3:
                    _ctrl = down; break;
                case VK_MENU:
                case 0xA4: case 0xA5:
                    _alt = down; break;
                case VK_LWIN: case VK_RWIN:
                    _win = down; break;
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // 挂起期间：只看一眼修饰键状态，不触发任何动作。
            // 这样设置窗口录按键时，按下某个键不会顺手把那个功能调出来。
            if (Suspended)
            {
                try
                {
                    if (nCode >= 0)
                    {
                        KBDLLHOOKSTRUCT sd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam, typeof(KBDLLHOOKSTRUCT));
                        int svk = (int)sd.vkCode;
                        int smsg = wParam.ToInt32();
                        if (smsg == WM_KEYDOWN || smsg == WM_SYSKEYDOWN) UpdateModifier(svk, true);
                        else if (smsg == WM_KEYUP || smsg == WM_SYSKEYUP) UpdateModifier(svk, false);
                    }
                }
                catch { }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            try
            {
                if (nCode >= 0)
                {
                    KBDLLHOOKSTRUCT d = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(KBDLLHOOKSTRUCT));
                    int vk = (int)d.vkCode;
                    int msg = wParam.ToInt32();
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    if (down || up)
                    {
                        if (IsModifier(vk))
                        {
                            UpdateModifier(vk, down);
                        }
                        else if (down)
                        {
                            // 给崩溃判定用：记下"用户是不是刚对前台窗口按了 Alt+F4"。
                            // 只在 F4 上做一点判断，别的键立刻返回。
                            try { GameCloseTracker.NoteKey(vk, _alt); } catch { }

                            // 真正的那个键按下了：把当前修饰键状态一起报上去。
                            // 注意：这里【故意不吞掉按键】，游戏照样能收到。
                            Action<int, bool, bool, bool, bool> h = Combo;
                            if (h != null)
                            {
                                try { h(vk, _ctrl, _alt, _shift, _win); }
                                catch { }
                            }

                            // 第 5 批：问一下这个键要不要吞掉（只有"配置里的热键 +
                            // 游戏在前台"才吞，其它一律放行，避免影响打字/游戏操作）。
                            try
                            {
                                if (Filter != null)
                                {
                                    string combo = HotkeySpec.Format(vk, _ctrl, _alt, _shift, _win);
                                    if (combo != null && Filter(combo) > 0)
                                        return (IntPtr)1;
                                }
                            }
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
                try { UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
        }
    }
}
