using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 全局滚轮路由（操作者报"子容器无法滑动"的根治办法）。
    ///
    /// 【为什么会滑不动】WinForms 里 WM_MOUSEWHEEL 是发给**当前有焦点的窗口**的，
    /// 不是发给鼠标底下那个窗口。设置页里焦点通常在按钮/下拉框上，
    /// 鼠标停在 GroupBox、Label、TableLayoutPanel 这类"不处理滚轮"的子容器上时，
    /// 滚轮消息就落到了别处 —— 表现就是"页面有滚动条，但滚轮怎么滚都不动"。
    ///
    /// 【做法】装一个应用级消息过滤器，把滚轮改投给**鼠标底下那个能滚的控件**：
    ///   · 底下是列表 / 文本框（ListBox、CheckedListBox、TextBox…）→ 转给它自己滚
    ///     （这样即使它没有焦点，鼠标停在上面也能滚）；
    ///   · 底下是下拉框 / 数字框 / 滑块，而且**它正拿着焦点** → 不拦，
    ///     让用户真的用它调值（这是 WheelSafe 那一套的既定规则）；
    ///   · 其它情况（GroupBox、Label、面板…）→ 往上找最近的 AutoScroll 容器转给它。
    /// 这样"设置页、面板页、日志页、区域页…"全都跟着滚轮走，不用给每个控件单独写代码。
    /// </summary>
    internal class WheelRouter : IMessageFilter
    {
        public const int WM_MOUSEWHEEL = 0x020A;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        /// <summary>总共处理了多少次滚轮（给日志/排查用）。</summary>
        public static int Handled;

        /// <summary>
        /// 设置窗口注册的钩子：说明栏正显示、且鼠标停在带说明的控件上时，
        /// 返回说明栏 —— 滚轮就用来滚说明内容，而不是滚页面。
        /// </summary>
        public static Func<Control, Control> TipBarOverride;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;

            try
            {
                Point sp = Cursor.Position;
                POINT p = new POINT();
                p.X = sp.X;
                p.Y = sp.Y;

                IntPtr h = WindowFromPoint(p);
                if (h == IntPtr.Zero) return false;

                Control c = Control.FromChildHandle(h);
                if (c == null) c = Control.FromHandle(h);
                if (c == null) return false;

                Control target = TargetOf(c);
                if (target == null) return false;      // 交给默认逻辑

                SendMessage(target.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam);
                Handled++;
                return true;                            // 已经处理，别再让它跑到别处
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 决定滚轮该给谁：返回 null = 别拦（让有焦点的值控件自己处理）。
        /// 抽成 static 是为了能离线测试。
        /// </summary>
        public static Control TargetOf(Control c)
        {
            // 【说明栏优先】鼠标停在某个“带说明的控件”上时，滚轮先用来滚说明内容
            //（说明可能很长，窗口里没有别的地方能滚动它）。
            if (TipBarOverride != null)
            {
                try
                {
                    Control bar = TipBarOverride(c);
                    if (bar != null) return bar;
                }
                catch { }
            }

            Control p = c;
            while (p != null)
            {
                // 自己能滚的列表 / 文本：转给它（没焦点也能滚）
                if (IsSelfScrollable(p)) return p;

                // 值控件：只有"它正拿着焦点"时才让它改值，否则继续往上找滚动容器
                if (IsValueControl(p) && p.Focused) return null;

                // 能滚的容器（AutoScroll）：直接交给它。
                // 【为什么不看 VerticalScroll.Visible】那个值要等布局跑完才算出来，
                // 刚打开窗口/内容刚变长时可能还是 false —— 那时候滚轮就白滚一下。
                // 交给 AutoScroll 容器是安全的：没溢出时滚了也不动。
                ScrollableControl sc = p as ScrollableControl;
                if (sc != null && sc.AutoScroll) return sc;

                p = p.Parent;
            }
            return null;
        }

        private static bool IsSelfScrollable(Control c)
        {
            return c is ListBox || c is CheckedListBox || c is TextBoxBase
                || c is ListView || c is TreeView || c is RichTextBox
                || c is DataGridView;
        }

        private static bool IsValueControl(Control c)
        {
            return c is ComboBox || c is NumericUpDown || c is TrackBar;
        }
    }
}
