using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal sealed class ModernStartView : UserControl, IDisposable
    {
        private readonly Panel _card;
        private readonly Label _brand;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _mode;
        private readonly Label _footer;
        private readonly ModernChoiceButton _modern;
        private readonly ModernChoiceButton _classic;
        private int _slide;

        public ModernStartView(Action<int> choose, bool animations, int durationMs)
        {
            DoubleBuffered = true;
            BackColor = ModernTheme.P.PageBg;
            _card = new Panel { Size = new Size(640, 420), BackColor = ModernTheme.P.CardBg, Padding = new Padding(30), Anchor = AnchorStyles.None };
            _card.Paint += PaintCard;
            Controls.Add(_card);

            _brand = new Label { Text = "SCPSLAlert   /   FIELD CONTROL", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = ModernTheme.P.Accent, AutoSize = true, Location = new Point(36, 28) };
            _card.Controls.Add(_brand);

            _title = new Label { Text = "选择主界面", Font = new Font("Microsoft YaHei UI", 25, FontStyle.Bold), AutoSize = true, Location = new Point(34, 78) };
            _card.Controls.Add(_title);

            _subtitle = new Label { Text = "选择适合你的控制台外观；之后可以在设置中随时切换。", Font = new Font("Microsoft YaHei UI", 10), ForeColor = ModernTheme.P.SubText, AutoSize = true, Location = new Point(36, 132) };
            _card.Controls.Add(_subtitle);

            _mode = new Label { Text = "跟随 Windows 外观", Font = new Font("Microsoft YaHei UI", 9), ForeColor = ModernTheme.P.SubText, AutoSize = true, Location = new Point(36, 177) };
            _card.Controls.Add(_mode);

            _modern = new ModernChoiceButton("◈", "新版控制台", "冷青主题、动态导航与细致控件样式", delegate { choose(0); });
            _modern.Location = new Point(34, 208);
            _classic = new ModernChoiceButton("▤", "经典界面", "保持熟悉的传统设置窗口", delegate { choose(1); });
            _classic.Location = new Point(330, 208);
            _card.Controls.Add(_modern);
            _card.Controls.Add(_classic);

            _footer = new Label { Text = "两种界面共用监控、OCR、日志、快捷键与提示设置。", Font = new Font("Microsoft YaHei UI", 8.5f), ForeColor = ModernTheme.P.SubText, AutoSize = true, Location = new Point(36, 373) };
            _card.Controls.Add(_footer);

            Resize += delegate { CenterCard(); };
            CenterCard();
            ModernTheme.Start();
            ModernTheme.Changed += OnThemeChanged;
            ApplyTheme();

            if (animations)
            {
                _slide = 12;
                Timer intro = new Timer { Interval = Math.Max(8, durationMs / 24) };
                int ticks = Math.Max(1, durationMs / Math.Max(8, durationMs / 24));
                int step = Math.Max(1, (int)Math.Ceiling(12.0 / ticks));
                intro.Tick += delegate
                {
                    _slide -= step;
                    if (_slide <= 0) { _slide = 0; intro.Stop(); intro.Dispose(); }
                    CenterCard();
                    Invalidate();
                };
                intro.Start();
            }
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            ApplyTheme();
            Invalidate(true);
        }

        private void ApplyTheme()
        {
            UiColors p = ModernTheme.P;
            bool dark = ModernTheme.IsDark;
            BackColor = p.PageBg;
            _card.BackColor = p.CardBg;
            _brand.ForeColor = p.Accent;
            _title.ForeColor = p.Text;
            _subtitle.ForeColor = p.SubText;
            _mode.ForeColor = p.SubText;
            _footer.ForeColor = p.SubText;
            _mode.Text = dark ? "深色模式 · 跟随 Windows 外观" : "浅色模式 · 跟随 Windows 外观";
            _modern.SetTheme(dark);
            _classic.SetTheme(dark);
            _card.Invalidate();
        }

        private void CenterCard()
        {
            if (_card == null) return;
            _card.Left = Math.Max(0, (ClientSize.Width - _card.Width) / 2);
            _card.Top = Math.Max(0, (ClientSize.Height - _card.Height) / 2 + _slide);
        }

        private void PaintCard(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = _card.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            using (Pen pen = new Pen(ModernTheme.P.CardBorder))
            using (GraphicsPath path = Rounded(r, 20)) e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public new void Dispose()
        {
            ModernTheme.Changed -= OnThemeChanged;
        }

        private sealed class ModernChoiceButton : Control
        {
            private readonly string _icon, _title, _description;
            private readonly Action _click;
            private bool _hover, _pressed, _dark;
            private readonly AnimTimer _anim;
            private float _hoverPhase, _pressPhase;

            public ModernChoiceButton(string icon, string title, string description, Action click)
            {
                _icon = icon;
                _title = title;
                _description = description;
                _click = click;
                Size = new Size(276, 142);
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                _anim = new AnimTimer();
                _anim.Tick += delegate { Step(); };
                MouseEnter += delegate { _hover = true; _anim.Start(); Invalidate(); };
                MouseLeave += delegate { _hover = false; _pressed = false; _anim.Start(); Invalidate(); };
                MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; _anim.Start(); Invalidate(); } };
                MouseUp += delegate(object s, MouseEventArgs e) { bool inside = ClientRectangle.Contains(e.Location); _pressed = false; _anim.Start(); Invalidate(); if (inside && _click != null) _click(); };
            }

            private void Step()
            {
                bool busy = false;
                float target = _hover ? 1f : 0f;
                float press = _pressed ? 1f : 0f;
                if (Math.Abs(_hoverPhase - target) > 0.02f) { _hoverPhase += (target - _hoverPhase) * 0.3f; busy = true; }
                else _hoverPhase = target;
                if (Math.Abs(_pressPhase - press) > 0.02f) { _pressPhase += (press - _pressPhase) * 0.35f; busy = true; }
                else _pressPhase = press;
                if (!busy) _anim.Stop();
                Invalidate();
            }

            public void SetTheme(bool dark) { _dark = dark; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                UiColors p = ModernTheme.P;
                Color fill = p.MutedBg;
                fill = ModernTheme.Mix(fill, p.Accent, 0.07f * _hoverPhase);
                fill = ModernTheme.Mix(fill, p.Accent, 0.10f * _pressPhase);
                Rectangle r = ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                using (SolidBrush b = new SolidBrush(fill)) using (GraphicsPath path = Rounded(r, 14)) e.Graphics.FillPath(b, path);
                Color border = _hoverPhase > 0.05f ? ModernTheme.Mix(p.CardBorder, p.Accent, _hoverPhase) : p.CardBorder;
                using (Pen pen = new Pen(border)) using (GraphicsPath path = Rounded(r, 14)) e.Graphics.DrawPath(pen, path);
                using (Font f = new Font("Segoe UI Symbol", 21, FontStyle.Regular)) using (SolidBrush b = new SolidBrush(p.Accent)) e.Graphics.DrawString(_icon, f, b, 17, 13);
                using (Font f = new Font("Microsoft YaHei UI", 11, FontStyle.Bold)) using (SolidBrush b = new SolidBrush(p.Text)) e.Graphics.DrawString(_title, f, b, 18, 57);
                using (Font f = new Font("Microsoft YaHei UI", 8.5f)) using (SolidBrush b = new SolidBrush(p.SubText)) e.Graphics.DrawString(_description, f, b, new RectangleF(18, 86, Width - 34, 43));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _anim.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
