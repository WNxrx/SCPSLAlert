using System;
using System.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 记住"用户刚刚按了 Alt+F4"：前台窗口属于哪个进程、按下的时间、
    /// 以及按下那一刻窗口是不是已经无响应（IsHungAppWindow）。
    ///
    /// 为什么还要看"按下时是否已经卡死"：
    ///   游戏崩溃/卡死之后，玩家第一反应往往也是按 Alt+F4 想关掉它。
    ///   如果只认"按过 Alt+F4"，这种退出就会被当成正常关闭，正好漏报。
    ///   所以按下时窗口已经无响应、或者按键之后很久才退出、或者系统在
    ///   按键之前就记过 AppHang，这次 Alt+F4 都不算"正常关闭"，
    ///   仍然按卡死/非正常退出记录。
    ///
    /// 这里只做很轻的判断，真正的匹配在游戏退出以后做（见 WasAltF4）。
    /// </summary>
    internal static class GameCloseTracker
    {
        private const int VK_F4 = 0x73;
        private const int VK_MENU = 0x12;

        private static int _pid;
        private static long _ticks;
        private static int _hung;

        /// <summary>键盘钩子每收到一个非修饰键按下时调用；不是 Alt+F4 就立刻返回。</summary>
        public static void NoteKey(int vk, bool alt)
        {
            if (vk != VK_F4) return;
            try
            {
                // 钩子里的 Alt 状态来自它自己维护的按下/抬起，
                // 极端情况（程序启动前就按住了 Alt）会漏，再问一次系统。
                if (!alt)
                {
                    short state = NativeMethods.GetAsyncKeyState(VK_MENU);
                    if ((state & 0x8000) == 0) return;
                }

                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero) return;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(foreground, out pid);
                if (pid == 0 || pid > int.MaxValue) return;

                // 按下这一刻窗口是否已经卡死：卡死游戏上的 Alt+F4 不能洗白退出。
                bool hung = false;
                try { hung = NativeMethods.IsHungAppWindow(foreground); } catch { }

                Interlocked.Exchange(ref _hung, hung ? 1 : 0);
                Interlocked.Exchange(ref _pid, (int)pid);
                Interlocked.Exchange(ref _ticks, DateTime.UtcNow.Ticks);
            }
            catch { }
        }

        /// <summary>
        /// 这次游戏退出之前，用户是不是刚对它的窗口按过 Alt+F4。
        /// hungAtPress 表示按下时窗口已经无响应；pressedAt 是按键的本地时间。
        /// </summary>
        public static bool WasAltF4(int gamePid, DateTime exitAt, int maxAgeMs,
            out bool hungAtPress, out DateTime pressedAt)
        {
            hungAtPress = false;
            pressedAt = DateTime.MinValue;
            if (gamePid <= 0 || maxAgeMs <= 0) return false;
            try
            {
                if (Interlocked.CompareExchange(ref _pid, 0, 0) != gamePid) return false;

                long ticks = Interlocked.Read(ref _ticks);
                if (ticks <= 0) return false;

                DateTime at = new DateTime(ticks, DateTimeKind.Utc);
                double age = (exitAt.ToUniversalTime() - at).TotalMilliseconds;
                if (age < 0 || age > maxAgeMs) return false;

                pressedAt = at.ToLocalTime();
                hungAtPress = Interlocked.CompareExchange(ref _hung, 0, 0) != 0;
                return true;
            }
            catch { return false; }
        }

        /// <summary>处理完一次退出后清掉，避免影响下一次判断。</summary>
        public static void Clear()
        {
            Interlocked.Exchange(ref _pid, 0);
            Interlocked.Exchange(ref _ticks, 0);
            Interlocked.Exchange(ref _hung, 0);
        }
    }
}
