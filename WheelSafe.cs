using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 滚轮安全控件。
    ///
    /// 【为什么需要】
    /// WinForms 里 ComboBox / NumericUpDown / TrackBar 都会**吃掉滚轮**：
    /// 鼠标划过它们时滚一下，值就被改了。设置在可滚动页面里时这非常坑 ——
    /// 用户只是想滚页面，结果"识别语言"从 eng+chi_sim 悄悄变成了 chi_sim、
    /// "区域高度"从 400 变成了 361，而且改完立刻自动存盘了。
    ///
    /// 【为什么拦 WndProc 而不是 OnMouseWheel】
    /// ComboBox 是在自己的 WndProc 里就处理掉 WM_MOUSEWHEEL 的，
    /// 只重写 OnMouseWheel 根本拦不住（实测仍然会被改）。必须在消息进来
    /// 的第一时间截住它。
    ///
    /// 规则：**没有焦点时不吃滚轮**，把滚轮原样转给最近的滚动容器，
    /// 让页面正常滚动；有焦点（用户真的点进去了）才允许用它调值。
    /// </summary>
    internal static class WheelHelper
    {
        public const int WM_MOUSEWHEEL = 0x020A;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>把滚轮消息转给最近的、真正能滚的祖先容器。</summary>
        public static void Forward(Control self, IntPtr wParam, IntPtr lParam)
        {
            Control target = null;
            Control p = self == null ? null : self.Parent;

            while (p != null)
            {
                ScrollableControl sc = p as ScrollableControl;
                if (sc != null && sc.AutoScroll) { target = p; break; }
                p = p.Parent;
            }
            if (target == null && self != null) target = self.Parent;
            if (target == null) return;

            try { SendMessage(target.Handle, WM_MOUSEWHEEL, wParam, lParam); }
            catch { }
        }

        /// <summary>共用的 WndProc 处理：返回 true 表示已经拦下并转发掉了。</summary>
        public static bool HandleWheel(Control self, ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            if (self.Focused) return false;          // 有焦点就让它正常调值

            Forward(self, m.WParam, m.LParam);
            return true;
        }
    }

    /// <summary>不吃滚轮的下拉框。</summary>
    internal class WheelSafeComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            if (WheelHelper.HandleWheel(this, ref m)) return;
            base.WndProc(ref m);
        }
    }

    /// <summary>不吃滚轮的数字框。</summary>
    internal class WheelSafeNumeric : NumericUpDown
    {
        protected override void WndProc(ref Message m)
        {
            if (WheelHelper.HandleWheel(this, ref m)) return;
            base.WndProc(ref m);
        }
    }

    /// <summary>不吃滚轮的滑块。</summary>
    internal class WheelSafeTrackBar : TrackBar
    {
        protected override void WndProc(ref Message m)
        {
            if (WheelHelper.HandleWheel(this, ref m)) return;
            base.WndProc(ref m);
        }
    }
}
