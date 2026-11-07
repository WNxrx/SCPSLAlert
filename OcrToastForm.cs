using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// OCR 捕获提示：屏幕左下角的一小块，告诉用户"这一下识别到了什么、做了什么"。
    ///
    /// 目的不是提醒游戏事件（那是各种提示窗口的活），而是让用户**确认 OCR 在正常工作**：
    /// 看到字被认出来了、也知道程序据此做了什么操作。
    ///
    /// 要求就是"别碍事"：
    ///   · 鼠标完全穿透，绝不挡住游戏操作；
    ///   · 不抢焦点（点它也不会把游戏切出去）；
    ///   · 字小、底色淡，几秒后自己消失。
    /// </summary>
    internal class OcrToastForm : Form
    {
        private static readonly Font TitleFont =
            new Font("微软雅黑", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font TextFont =
            new Font("微软雅黑", 8f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font ActionFont =
            new Font("微软雅黑", 8f, FontStyle.Bold, GraphicsUnit.Point);

        private string _text = "";
        private string _action = "";
        private readonly Timer _hideTimer;

        public OcrToastForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 24, 32);
            Opacity = 0.82;
            DoubleBuffered = true;
            Size = new Size(460, 54);

            _hideTimer = new Timer();
            _hideTimer.Interval = 4500;
            _hideTimer.Tick += delegate { _hideTimer.Stop(); Hide(); };
        }

        /// <summary>鼠标穿透 + 不抢焦点：靠扩展样式实现。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT  鼠标穿透
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE   点了也不激活
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW   不出现在 Alt+Tab
                return cp;
            }
        }

        /// <summary>
        /// 【关键】让它自己在屏幕截图里消失。
        ///
        /// 它就贴在屏幕左下角 —— 正好压在默认的 OCR 识别区域上。
        /// 不排除的话，程序会把自己写的"OCR 识别 / 动作：…"这些字认一遍，
        /// 形成自我循环。设了之后用户照样看得见，截图里没有它。
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.HideFromCapture(Handle, "OCR 捕获提示");
        }

        /// <summary>
        /// 显示一次捕获结果。
        /// text 为空表示"只更新动作那一行"（同一次识别里先报文本、后报动作）。
        /// </summary>
        public void ShowToast(string text, string action)
        {
            if (text != null && text.Length > 0)
            {
                _text = Clip(text, 46);

                // 【踩过的坑】新的一次识别，动作一律以这次传进来的为准。
                // 以前只在 action == null 时才清空，但调用方传的是 ""（不是 null），
                // 于是旧的"认不出击杀者，已忽略"会一直挂在屏幕上 ——
                // 后面哪怕识别到完全无关的内容，下面那行也还显示这句。
                _action = action == null ? "" : action;
            }
            else if (!string.IsNullOrEmpty(action))
            {
                _action = action;   // 只更新动作那一行（text 传空）
            }

            Reposition();
            if (!Visible) Show();
            BringToFront();
            Redraw();

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private static string Clip(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.IndexOf("  ") >= 0) s = s.Replace("  ", " ");
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        /// <summary>摆在左下角（主屏工作区），避开任务栏。</summary>
        private void Reposition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + 12, wa.Bottom - Height - 12);
        }

        private void Redraw()
        {
            using (Graphics g = Graphics.FromImage(_buf))
            {
                g.Clear(Color.FromArgb(20, 24, 32));

                using (SolidBrush accent = new SolidBrush(Color.FromArgb(90, 190, 255)))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 168, 182)))
                    g.DrawString("OCR 识别", TitleFont, b, 12f, 6f);

                using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 232, 240)))
                    g.DrawString(_text, TextFont, b, 70f, 6f);

                string act = string.IsNullOrEmpty(_action) ? "（没有命中任何规则）" : _action;
                Color ac = string.IsNullOrEmpty(_action)
                    ? Color.FromArgb(120, 126, 138)
                    : Color.FromArgb(150, 235, 170);
                using (SolidBrush b = new SolidBrush(ac))
                    g.DrawString("动作：" + act, ActionFont, b, 12f, 28f);
            }
            Invalidate();
        }

        private readonly Bitmap _buf = new Bitmap(460, 54);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(_buf, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideTimer.Stop();
                _hideTimer.Dispose();
                _buf.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
