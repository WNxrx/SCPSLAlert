using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 快捷键输入框：点一下，直接按组合键就填进去了。
    /// 不用再手打 "Ctrl+Alt+F8" 这种字符串。
    ///
    ///   · 点一下 → 提示“请按下组合键…”
    ///   · 按下组合键（可以带 Ctrl / Alt / Shift）→ 立刻填好并保存
    ///   · 只按修饰键不算数，会继续等真正的那个键
    ///   · Backspace / Delete → 清空
    ///   · Esc → 放弃这次输入，恢复原来的
    /// </summary>
    internal class HotkeyBox : TextBox
    {
        private HotkeySpec _spec = new HotkeySpec();
        private HotkeySpec _pending;          // 按下了但还没点确认的组合键
        private bool _capturing;
        private bool _silent;

        /// <summary>值真的生效了（用来触发自动保存）。</summary>
        public event Action Changed;

        /// <summary>点了确认按钮。</summary>
        public event Action Confirmed;

        /// <summary>按下了新的组合键、正在等确认（用来把确认按钮点亮）。</summary>
        public event Action PendingSet;

        /// <summary>确认之前的回调：返回 false 表示不生效（例如用户在冲突对话框里选了取消）。</summary>
        public Func<string, bool> BeforeConfirm;

        // 有几个框正在录按键。只要有框在录，就把全局热键暂停 ——
        // 否则用户想给 A 功能设 F10，F10 会直接把 B 功能调出来。
        private static int _capturingCount;
        private bool _iSuspended;

        public HotkeyBox()
        {
            ReadOnly = true;              // 不让直接打字
            ShortcutsEnabled = false;     // 屏蔽 Ctrl+C/V 之类
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            ForeColor = UiKit.TextColor;
            BorderStyle = BorderStyle.FixedSingle;
            Font = UiKit.Font;
            Height = 26;
            TextAlign = HorizontalAlignment.Left;
            ShowSpec();
        }

        /// <summary>当前生效的组合键文本（存 config.ini 用）。</summary>
        public string Value
        {
            get { return (_spec != null && _spec.IsValid) ? _spec.ToString() : ""; }
            set
            {
                _spec = HotkeySpec.Parse(value);
                _pending = null;
                _capturing = false;
                ShowSpec();
            }
        }

        /// <summary>有没有按下了但还没确认的组合键。</summary>
        public bool HasPending
        {
            get { return _pending != null && _pending.IsValid; }
        }

        /// <summary>开始录按键：把全局热键暂停，免得按键顺手把别的功能调出来。</summary>
        private void SuspendHotkeys()
        {
            if (_iSuspended) return;
            _iSuspended = true;
            _capturingCount++;
            KeyboardHook.Suspended = true;
        }

        /// <summary>停止录制：恢复全局热键。</summary>
        private void ResumeHotkeys()
        {
            if (!_iSuspended) return;
            _iSuspended = false;
            _capturingCount--;
            if (_capturingCount <= 0)
            {
                _capturingCount = 0;
                KeyboardHook.Suspended = false;
            }
        }

        /// <summary>确认这次按下 —— 只有点了确认按钮才真正生效。</summary>
        public void Confirm()
        {
            // 先无条件恢复热键：万一走到这里时没有待确认值（比如按键被吞了），
            // 也不能让全局热键一直挂着。
            ResumeHotkeys();

            if (!HasPending)
            {
                ShowSpec();
                return;
            }

            if (BeforeConfirm != null)
            {
                try { if (!BeforeConfirm(_pending.ToString())) { ShowSpec(); return; } }
                catch { }
            }

            _spec = _pending;
            _pending = null;
            ShowSpec();
            if (Changed != null) Changed();
            if (Confirmed != null) Confirmed();
        }

        /// <summary>放弃这次按下，恢复成原来生效的那个。</summary>
        public void CancelPending()
        {
            ResumeHotkeys();
            _pending = null;
            ShowSpec();
        }

        /// <summary>
        /// 兜底：把所有框的挂起状态清掉、恢复全局热键。
        /// 设置窗口隐藏 / 关闭时调一下，避免因为异常路径把热键永久挂起。
        /// </summary>
        public static void ReleaseAll()
        {
            _capturingCount = 0;
            KeyboardHook.Suspended = false;
        }

        /// <summary>把还没确认的值显示出来（带个标记，看得出没生效）。</summary>
        private void ShowPending()
        {
            _silent = true;
            try
            {
                Text = _pending.ToString() + "  ← 点「确认」生效";
                ForeColor = Color.FromArgb(200, 120, 20);
            }
            finally { _silent = false; }
            if (PendingSet != null) PendingSet();
        }

        private void ShowSpec()
        {
            _silent = true;
            try
            {
                bool ok = _spec != null && _spec.IsValid;
                Text = ok ? _spec.ToString() : "（未设置）";
                ForeColor = ok ? UiKit.TextColor : UiKit.SubColor;
            }
            finally { _silent = false; }
            try { SelectionStart = Text.Length; SelectionLength = 0; } catch { }
        }

        private void BeginCapture()
        {
            if (HasPending) { ShowPending(); return; }
            _capturing = true;
            SuspendHotkeys();
            _silent = true;
            try
            {
                Text = "请按下组合键…";
                ForeColor = UiKit.Accent;
            }
            finally { _silent = false; }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            BeginCapture();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            BeginCapture();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            // 没确认就离开：把这次按下留着（用户可能要去点确认按钮），
            // 但要把全局热键恢复回来，不然整个程序的热键就哑了。
            _capturing = false;
            ResumeHotkeys();
            if (HasPending) ShowPending();
            else ShowSpec();
            base.OnLostFocus(e);
        }

        /// <summary>只按了修饰键 —— 不算一个完整的快捷键，继续等。</summary>
        private static bool IsModifierKey(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu
                || k == Keys.LControlKey || k == Keys.RControlKey
                || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.LMenu || k == Keys.RMenu
                || k == Keys.LWin || k == Keys.RWin
                || k == Keys.Control || k == Keys.Shift || k == Keys.Alt;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_capturing)
            {
                // 没在录制时，按什么键都重新进入录制
                if (!IsModifierKey(e.KeyCode) || true) BeginCapture();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                _capturing = false;
                ShowSpec();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                // 清空也走“待确认”，避免误删
                _pending = new HotkeySpec();
                _capturing = false;
                ShowPending();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }

            if (IsModifierKey(e.KeyCode))
            {
                // 继续等真正的那个键
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }

            HotkeySpec h = new HotkeySpec();
            h.Ctrl = e.Control;
            h.Alt = e.Alt;
            h.Shift = e.Shift;
            h.Vk = e.KeyValue;

            if (!h.IsValid)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }

            // 只记下来，不立刻生效 —— 要用户点旁边的「确认」
            _pending = h;
            _capturing = false;
            ShowPending();

            e.Handled = true; e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // 录制期间不要真的往框里输入字符
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void Fire()
        {
            if (_silent) return;
            if (Changed != null) Changed();
        }

        /// <summary>这个组合键是不是“光秃秃一个字母/数字”（容易被游戏用到，提醒一下）。</summary>
        public bool IsBareLetter
        {
            get
            {
                if (_spec == null || !_spec.IsValid) return false;
                if (_spec.Ctrl || _spec.Alt || _spec.Shift || _spec.Win) return false;
                return _spec.Vk < 0x70;   // 不是 F1-F12
            }
        }
    }
}
