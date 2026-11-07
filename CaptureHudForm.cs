using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 6 批：采集进度 / 轻提示的独立显示通道。
    ///
    /// 【为什么独立】需求七.6：采集进度、轻提示、暂停提示走独立通道，默认不参与
    /// 悬浮窗避让。视觉沿用提示卡的样式（深色卡片 + 底部强调色进度条 + 淡出），
    /// 但它不占用 AlertManager 的提示槽、也不受提示位置设置影响。
    /// </summary>
    internal sealed class CaptureHudForm : Form
    {
        private readonly Label _text = new Label();
        private readonly Panel _bar = new Panel();
        private readonly Panel _track = new Panel();
        private readonly Timer _fadeTimer = new Timer();
        private readonly Timer _hideTimer = new Timer();
        private readonly Config _cfg;
        private bool _light;
        private DateTime _shownAt = DateTime.MinValue;
        private int _opacityStep = 20;

        public CaptureHudForm(Config cfg)
        {
            _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(18, 21, 26);
            Opacity = 0;
            Width = 420;
            Height = 74;
            Padding = new Padding(14, 10, 14, 10);

            _text.AutoSize = false;
            _text.Dock = DockStyle.Fill;
            _text.ForeColor = Color.FromArgb(232, 236, 242);
            _text.Font = new Font("微软雅黑", 11f, FontStyle.Regular);
            _text.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_text);

            _track.Height = 3;
            _track.Dock = DockStyle.Bottom;
            _track.BackColor = Color.FromArgb(44, 50, 60);
            Controls.Add(_track);

            _bar.Height = 3;
            _bar.Width = 0;
            _bar.BackColor = Color.FromArgb(67, 207, 187);
            _bar.Dock = DockStyle.Left;
            _track.Controls.Add(_bar);

            _fadeTimer.Interval = 30;
            _fadeTimer.Tick += OnFadeTick;
            _hideTimer.Interval = 1500;
            _hideTimer.Tick += delegate { _hideTimer.Stop(); _fadeTimer.Start(); };

            // 不抢焦点、不挡鼠标
            try
            {
                int ex = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE,
                    ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        /// <summary>进度：当前 x / 上限 y。</summary>
        public void ShowProgress(string text, int current, int total)
        {
            _light = true;
            ShowAtDefault();   // 每次重新贴到目标位置（游戏窗口可能换了）
            _text.Text = text;
            _track.Visible = true;
            _bar.Width = total <= 0 ? 0 : Math.Max(0, Math.Min(_track.Width, (int)(_track.Width * (double)current / total)));
            _hideTimer.Stop();
            _fadeTimer.Stop();
            Opacity = 0.96;
            _shownAt = DateTime.Now;
            _hideTimer.Interval = 1500;
            _hideTimer.Start();
            Show();
        }

        /// <summary>普通提示；light = 1.5 秒淡出。</summary>
        public void ShowNotice(string text, bool light)
        {
            _light = light;
            ShowAtDefault();
            _text.Text = text;
            _track.Visible = false;
            Opacity = 0.96;
            _shownAt = DateTime.Now;
            Show();
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _hideTimer.Interval = light ? 1500 : 3500;
            _hideTimer.Start();
        }

        public void HideNow()
        {
            _hideTimer.Stop();
            _fadeTimer.Stop();
            Opacity = 0;
            Hide();
        }

        private void ShowAtDefault()
        {
            try
            {
                Rectangle basis;
                Config cfg = _cfg;
                IntPtr hwnd = ScreenGeometry.FindGameWindow();
                if (hwnd != IntPtr.Zero)
                    basis = BrightnessSampler.ClientBounds(hwnd);
                else
                    basis = Screen.PrimaryScreen.Bounds;
                if (basis.Width <= 0) basis = Screen.PrimaryScreen.Bounds;

                double xp = cfg != null && !double.IsNaN(cfg.CaptureHudXPercent) ? cfg.CaptureHudXPercent : 0.5;
                double yp = cfg != null && !double.IsNaN(cfg.CaptureHudYPercent) ? cfg.CaptureHudYPercent : 0.06;
                int x = basis.Left + (int)Math.Round(basis.Width * xp) - Width / 2;
                int y = basis.Top + (int)Math.Round(basis.Height * yp);
                Location = new Point(x, y);
                _bar.Width = 0;
                if (Width != 420) Width = 420;
            }
            catch { }
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            double left = (DateTime.Now - _shownAt).TotalMilliseconds;
            // 1.5 秒轻提示：前 1.2 秒不动，最后 0.3 秒淡出
            if (left < 1200 && _light) return;
            Opacity -= 0.08;
            if (Opacity <= 0.02)
            {
                _fadeTimer.Stop();
                Opacity = 0;
                Hide();
            }
        }
    }
}
