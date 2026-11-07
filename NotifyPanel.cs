using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 通知开关面板：游戏里想临时关掉某一类提示时用。
    ///
    /// 设计目标就是“游戏里也好按”：
    ///   · 一个大格子对应一种提示，点一下就是开/关，没有二级菜单；
    ///   · 每个格子左边带序号，直接按键盘 1-8 也能切，不用瞄准；
    ///   · Esc 或点“关闭”收起。
    ///
    /// 外观和悬浮面板保持一致：深色半透明底、圆角卡片、强调色。
    /// 打开 / 收起都带淡入淡出动画。
    /// </summary>
    internal class NotifyPanel : Form
    {
        private static readonly Font UiFont = new Font("微软雅黑", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font TitleFont = new Font("微软雅黑", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font HintFont = new Font("微软雅黑", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

        // 和 OverlayPanel 用同一套配色
        private static readonly Color BoxColor = Color.FromArgb(26, 30, 38);
        private static readonly Color onBack = Color.FromArgb(34, 76, 54);
        private static readonly Color onText = Color.FromArgb(150, 245, 180);
        private static readonly Color offBack = Color.FromArgb(44, 50, 62);
        private static readonly Color offText = Color.FromArgb(150, 156, 168);
        private static readonly Color accent = Color.FromArgb(90, 190, 255);

        private readonly AlertManager _mgr;
        private readonly Button[] _tiles;
        private readonly Action _onChanged;
        private bool _closing;

        private readonly Timer _animTimer;
        private bool _fadingIn;

        public NotifyPanel(AlertManager mgr, Action onChanged)
        {
            _mgr = mgr;
            _onChanged = onChanged;

            Text = "通知开关";
            Font = UiFont;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = BoxColor;
            ClientSize = new Size(576, 316);
            DoubleBuffered = true;
            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            // 标题
            Label title = new Label();
            title.Text = "通知开关";
            title.Font = TitleFont;
            title.ForeColor = Color.FromArgb(235, 238, 244);
            title.AutoSize = true;
            title.Location = new Point(24, 14);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "点格子切换开 / 关，或直接按键盘数字键。Esc 收起。";
            sub.Font = HintFont;
            sub.ForeColor = Color.FromArgb(140, 148, 162);
            sub.AutoSize = true;
            sub.Location = new Point(26, 38);
            Controls.Add(sub);

            // 顶部一条分隔线（自绘在 OnPaint 里）

            int count = mgr.AlertCount;
            _tiles = new Button[count];

            // 【别写死行列】以前是写死的 2 列 × 4 行 = 8 格，后来提示类型加到 9 个，
            // 第 9 个就被排到 x=562（面板只有 576 宽）—— 跑到面板外面去了，
            // 既点不到也看不到文字。现在按数量算行列，并且同步把面板撑高。
            int cols = count <= 10 ? 2 : 3;
            int rows = (count + cols - 1) / cols;
            if (rows < 1) rows = 1;

            const int padX = 22;
            const int gapX = 12;
            const int rowH = 42;
            const int gapY = 8;

            int colW = (576 - padX * 2 - gapX * (cols - 1)) / cols;
            int gridBottom = 62 + rows * (rowH + gapY) - gapY;
            ClientSize = new Size(576, gridBottom + 58);   // 底部留出按钮和说明的位置

            for (int i = 0; i < count; i++)
            {
                int col = i / rows;    // 按列填：一列排满再排下一列
                int row = i % rows;
                Button b = new Button();
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.Font = UiFont;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Location = new Point(padX + col * (colW + gapX), 62 + row * (rowH + gapY));
                b.Size = new Size(colW, rowH);
                b.Tag = i;
                b.Cursor = Cursors.Hand;
                b.UseVisualStyleBackColor = false;
                b.Click += delegate(object s, EventArgs e)
                {
                    int idx = (int)((Button)s).Tag;
                    Toggle(idx);
                };
                // 圆角卡片：直接切一个圆角 Region 出来，不用自绘
                b.Region = new Region(Rounded(new Rectangle(0, 0, b.Width, b.Height), 8));
                _tiles[i] = b;
                Controls.Add(b);
            }

            Button close = new Button();
            close.Text = "关闭";
            close.Font = UiFont;
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Color.FromArgb(80, 88, 102);
            close.BackColor = Color.FromArgb(48, 54, 66);
            close.ForeColor = Color.FromArgb(235, 238, 244);
            close.UseVisualStyleBackColor = false;
            close.Size = new Size(84, 30);
            close.Location = new Point(ClientSize.Width - 106, ClientSize.Height - 46);
            close.Cursor = Cursors.Hand;
            close.Region = new Region(Rounded(new Rectangle(0, 0, close.Width, close.Height), 7));
            close.Click += delegate { Finish(); };
            Controls.Add(close);

            Label hint = new Label();
            hint.Text = "按住“显示全部”热键时，这里关掉的提示也会临时显示出来。";
            hint.ForeColor = Color.FromArgb(130, 138, 152);
            hint.Font = HintFont;
            hint.AutoSize = true;
            hint.Location = new Point(24, ClientSize.Height - 38);
            Controls.Add(hint);

            Refresh2();

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 90);

            // 淡入动画
            Opacity = 0;
            _fadingIn = true;
            _animTimer = new Timer();
            _animTimer.Interval = 16;
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            double step = 0.10;
            if (_fadingIn)
            {
                Opacity += step;
                if (Opacity >= 1) { Opacity = 1; _animTimer.Stop(); }
            }
            else
            {
                Opacity -= step;
                if (Opacity <= 0)
                {
                    Opacity = 0;
                    _animTimer.Stop();
                    _closing = true;
                    Close();
                }
            }
        }

        /// <summary>顶部一道强调色，和悬浮面板的观感统一。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
                e.Graphics.DrawLine(p, 22, 56, ClientSize.Width - 22, 56);
            using (SolidBrush b = new SolidBrush(accent))
                e.Graphics.FillRectangle(b, 0, 0, 4, ClientSize.Height);

            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private void Toggle(int i)
        {
            _mgr.SetAlertEnabled(i, !_mgr.AlertEnabled(i));
            Refresh2();
            if (_onChanged != null) _onChanged();
        }

        private void Refresh2()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                bool on = _mgr.AlertEnabled(i);
                _tiles[i].BackColor = on ? onBack : offBack;
                _tiles[i].ForeColor = on ? onText : offText;
                _tiles[i].Text = "  " + (i + 1) + "    " + _mgr.AlertName(i) + "        " + (on ? "开" : "关");
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Finish();
                e.Handled = true;
            }
            else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                int idx = e.KeyCode - Keys.D1;
                if (idx < _tiles.Length) { Toggle(idx); e.Handled = true; }
            }
            else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)
            {
                int idx = e.KeyCode - Keys.NumPad1;
                if (idx < _tiles.Length) { Toggle(idx); e.Handled = true; }
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing)
            {
                e.Cancel = true;
                Finish();
                return;
            }
            base.OnFormClosing(e);
        }

        /// <summary>收起：先播淡出，动画放完再真正 Close。</summary>
        private void Finish()
        {
            if (_closing) return;
            if (_onChanged != null) _onChanged();

            _fadingIn = false;
            _animTimer.Start();
        }

        public void AllowClose()
        {
            _closing = true;
            _animTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimTick;
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
