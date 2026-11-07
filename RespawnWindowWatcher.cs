using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// Observes Shell redraw notifications for SCP:SL windows. This is an indirect
    /// respawn signal; the manager combines it with OCR while the game is foreground.
    /// </summary>
    internal sealed class RespawnWindowWatcher : NativeWindow, IDisposable
    {
        private const string HookMessageName = "SHELLHOOK";
        private const int ForegroundPollMs = 100;
        private const int TransitionIgnoreMs = 900;

        private readonly Timer _timer;
        private readonly uint _shellMessage;
        private int _gamePid;
        private IntPtr _gameWindow;
        private bool _wasForeground;
        private DateTime _ignoreUntil = DateTime.MaxValue;
        private bool _registered;
        private bool _disposed;

        public event Action<bool, string> RedrawObserved;

        public RespawnWindowWatcher()
        {
            CreateHandle(new CreateParams());
            _shellMessage = NativeMethods.RegisterWindowMessage(HookMessageName);
            if (_shellMessage != 0)
            {
                _registered = NativeMethods.RegisterShellHookWindow(Handle);
                if (!_registered)
                    Logger.On("复活", "Shell Hook 注册失败，窗口重绘判定不可用");
            }
            else
            {
                Logger.On("复活", "SHELLHOOK 消息注册失败，窗口重绘判定不可用");
            }

            _timer = new Timer();
            _timer.Interval = ForegroundPollMs;
            _timer.Tick += OnPoll;
            _timer.Start();
            RefreshGameWindow(true);
        }

        public bool IsGameForeground
        {
            get
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                return IsGameWindow(foreground);
            }
        }

        private void OnPoll(object sender, EventArgs e)
        {
            RefreshGameWindow(false);
            bool foreground = IsGameForeground;
            if (foreground != _wasForeground)
            {
                _wasForeground = foreground;
                _ignoreUntil = DateTime.UtcNow.AddMilliseconds(TransitionIgnoreMs);
                Logger.On("复活", foreground
                    ? "游戏切到前台；忽略切换附近的窗口重绘"
                    : "游戏切到后台；忽略切换附近的窗口重绘");
            }
        }

        private void RefreshGameWindow(bool initial)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (IsGameWindow(foreground))
            {
                _gameWindow = foreground;
                _gamePid = GetPid(foreground);
            }
            else if (_gameWindow == IntPtr.Zero || !NativeMethods.IsWindow(_gameWindow)
                     || GetPid(_gameWindow) != _gamePid)
            {
                _gameWindow = FindGameWindow();
                _gamePid = _gameWindow == IntPtr.Zero ? 0 : GetPid(_gameWindow);
            }

            if (initial)
            {
                _wasForeground = IsGameForeground;
                // On startup don't treat an initial repaint as a transition/respawn.
                _ignoreUntil = DateTime.UtcNow.AddSeconds(2);
                Logger.On("复活", _registered
                    ? "Shell Hook 已就绪（等待游戏窗口重绘信号）"
                    : "Shell Hook 未就绪");
            }
        }

        private bool IsGameWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            int pid = GetPid(hwnd);
            if (pid == 0) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return p.ProcessName.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static int GetPid(IntPtr hwnd)
        {
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            return pid > int.MaxValue ? 0 : (int)pid;
        }

        private static IntPtr FindGameWindow()
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr state)
            {
                if (NativeMethods.IsWindowVisible(hwnd) && IsProcessGame(GetPid(hwnd)))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool IsProcessGame(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return p.ProcessName.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        protected override void WndProc(ref Message m)
        {
            if (_registered && m.Msg == _shellMessage
                && (m.WParam.ToInt32() & 0xFFFF) == NativeMethods.HSHELL_REDRAW)
            {
                IntPtr hwnd = m.LParam;
                if (IsGameWindow(hwnd))
                {
                    _gameWindow = hwnd;
                    _gamePid = GetPid(hwnd);
                    bool foreground = IsGameForeground;
                    if (DateTime.UtcNow >= _ignoreUntil)
                    {
                        Logger.On("复活", "收到游戏窗口 HSHELL_REDRAW（" + (foreground ? "前台" : "后台") + "）");
                        Action<bool, string> handler = RedrawObserved;
                        if (handler != null) handler(foreground, "游戏窗口 HSHELL_REDRAW");
                    }
                    else
                    {
                        Logger.On("复活", "忽略切换/启动抑制期内的游戏窗口重绘");
                    }
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnPoll;
                _timer.Dispose();
            }
            if (_registered)
            {
                try { NativeMethods.DeregisterShellHookWindow(Handle); } catch { }
                _registered = false;
            }
            try { DestroyHandle(); } catch { }
        }
    }
}
