using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// SCP 扫描结果悬浮窗（操作者要求）：进入服务器后第一次死亡时开始扫描，
    /// **只在本局扫描到新的 SCP 时**弹一次（同一批反复扫到不再弹），
    /// 告诉用户"已添加：…"。样式跟其它悬浮提示一致：
    /// 深色圆角、无边框、不抢焦点、鼠标穿透、对截图不可见、几秒后自己消失。
    /// 放右侧是为了不压住左侧的 SCP 列表识别区。
    /// </summary>
    internal class ScpScanToast : Form
    {
        private static readonly Font TitleFont =
            new Font("微软雅黑", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font TextFont =
            new Font("微软雅黑", 9f, FontStyle.Regular, GraphicsUnit.Point);

        private string _text = "";
        private readonly Timer _hideTimer;
        private readonly Bitmap _buf = new Bitmap(320, 96);

        public ScpScanToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 24, 32);
            DoubleBuffered = true;
            Size = new Size(320, 96);

            _hideTimer = new Timer();
            _hideTimer.Interval = 6000;
            _hideTimer.Tick += delegate { _hideTimer.Stop(); Hide(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT  鼠标穿透
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE   点了也不激活
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW   不进 Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.HideFromCapture(Handle, "SCP 扫描悬浮窗");
        }

        /// <summary>
        /// 弹一次"已添加"。codes 是逗号分隔的**新**SCP 编号（"049,173"）；
        /// 空的就不弹（没认出东西时别打扰）。
        /// </summary>
        public void ShowCodes(string codes)
        {
            if (string.IsNullOrEmpty(codes)) return;

            _text = "已添加：" + codes.Replace(",", "、");
            Reposition();
            Redraw();
            if (!Visible) Show();
            BringToFront();

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void Reposition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Top + 90);
        }

        private void Redraw()
        {
            using (Graphics g = Graphics.FromImage(_buf))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(20, 24, 32));

                using (GraphicsPath gp = AlertRenderer.RoundedRect(new Rectangle(0, 0, _buf.Width - 1, _buf.Height - 1), 10))
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(240, 26, 30, 38)))
                        g.FillPath(sb, gp);
                    using (Pen p = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
                }
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(120, 230, 140)))
                    g.FillRectangle(accent, 2, 2, 3, _buf.Height - 4);

                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 160, 175)))
                    g.DrawString("SCP 扫描", TitleFont, b, 12f, 8f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 232, 240)))
                    g.DrawString(_text, TextFont, b, new RectangleF(12, 32, _buf.Width - 24, 52));
            }
            Invalidate();
        }

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
