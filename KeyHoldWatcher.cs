using System;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// “按住不放才生效”的热键。
    /// RegisterHotKey 只能在按下时通知一次，拿不到“松开”，所以这里用
    /// GetAsyncKeyState 轮询主键和修饰键的状态，检测按下 / 松开两个沿。
    /// 只读按键状态，不拦截、不吞键，游戏照常收到按键。
    /// </summary>
    internal class KeyHoldWatcher : IDisposable
    {
        private readonly Timer _timer;
        private HotkeySpec _spec;
        private bool _down;
        private bool _disposed;

        public event Action Pressed;
        public event Action Released;

        public KeyHoldWatcher(HotkeySpec spec)
        {
            _spec = spec;
            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += OnTick;
        }

        public HotkeySpec Spec
        {
            get { return _spec; }
            set
            {
                _spec = value;
                if (_down)
                {
                    _down = false;
                    if (Released != null) Released();
                }
            }
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            if (_down)
            {
                _down = false;
                if (Released != null) Released();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool now = IsDown();
            if (now == _down) return;
            _down = now;
            if (now)
            {
                if (Pressed != null) Pressed();
            }
            else
            {
                if (Released != null) Released();
            }
        }

        private bool IsDown()
        {
            if (_spec == null || !_spec.IsValid) return false;
            if (!Down(_spec.Vk)) return false;
            if (_spec.Ctrl != Down(NativeMethods.VK_CONTROL)) return false;
            if (_spec.Alt != Down(NativeMethods.VK_MENU)) return false;
            if (_spec.Shift != Down(NativeMethods.VK_SHIFT)) return false;
            return true;
        }

        private static bool Down(int vk)
        {
            try { return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
    }
}
