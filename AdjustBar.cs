using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// “调整提示位置”模式下显示的操作条。
    ///
    /// 它是一个普通窗口（不穿透、可点击、可聚焦），所以：
    ///   · 屏幕上始终有一句“现在在调整模式”的提示，不会让人摸不着头脑；
    ///   · 即使鼠标拖不准，也可以用方向键 / 方向按钮逐像素微调；
    ///   · 随时能点“完成调整”退出。
    /// </summary>
    internal class AdjustBar : Form
    {
        private static readonly Font UiFont = new Font("微软雅黑", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font TitleFont = new Font("微软雅黑", 9.5f, FontStyle.Bold, GraphicsUnit.Point);

        private readonly AlertManager _mgr;
        private readonly HeartPromptOverlay _heart;
        private readonly Action _done;
        private readonly ComboBox _target;
        private readonly NumericUpDown _step;
        private readonly Label _pos;
        private readonly Label _title;
        private readonly Timer _poll;

        private bool _closing;

        public AdjustBar(AlertManager mgr, Action done)
            : this(mgr, done, null)
        {
        }

        public AdjustBar(AlertManager mgr, Action done, HeartPromptOverlay heart)
        {
            _mgr = mgr;
            _heart = heart;
            _done = done;

            Text = "调整提示位置";
            Font = UiFont;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            ClientSize = new Size(826, 92);
            BackColor = Color.FromArgb(30, 34, 42);
            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            Label title = new Label();
            title.Text = "调整模式：用鼠标把屏幕上的提示文字拖到想要的位置；也可以选中某个提示后用方向键微调。";
            title.Font = TitleFont;
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(14, 10);
            Controls.Add(title);
            _title = title;

            Label tip = new Label();
            tip.Text = "方向键 = 移动 1 步（按住 Shift = 10 倍），Esc = 完成";
            tip.ForeColor = Color.FromArgb(150, 158, 172);
            tip.AutoSize = true;
            tip.Location = new Point(14, 34);
            Controls.Add(tip);

            // 整体移动：所有提示一起挪，互相之间的相对位置保持不变
            CheckBox all = new CheckBox();
            all.Text = "整体移动全部提示";
            all.ForeColor = Color.FromArgb(200, 206, 216);
            all.Font = UiFont;
            all.BackColor = Color.Transparent;
            all.Location = new Point(14, 60);
            all.Size = new Size(150, 26);
            all.CheckedChanged += delegate
            {
                _mgr.GlobalMove = all.Checked;
                _title.Text = all.Checked
                    ? "整体移动：拖动任意一个提示，所有提示一起跟着走（相对位置不变）。"
                    : "调整模式：用鼠标把屏幕上的提示文字拖到想要的位置；也可以选中某个提示后用方向键微调。";
            };
            Controls.Add(all);

            Label l1 = MakeLabel("提示", 176, 66);
            Controls.Add(l1);

            _target = new ComboBox();
            _target.DropDownStyle = ComboBoxStyle.DropDownList;
            _target.Font = UiFont;
            _target.Location = new Point(212, 62);
            _target.Size = new Size(168, 25);
            for (int i = 0; i < mgr.AlertCount; i++)
            {
                string n = mgr.AlertName(i);
                if (!mgr.AlertEnabled(i)) n += "（已关闭）";
                _target.Items.Add(n);
            }
            if (_heart != null) _target.Items.Add("心率提示");
            if (_target.Items.Count > 0) _target.SelectedIndex = 0;
            _target.SelectedIndexChanged += delegate { RefreshPos(); };
            Controls.Add(_target);

            int bx = 394;
            Controls.Add(MakeArrow("◀", bx, 61, -1, 0));
            Controls.Add(MakeArrow("▲", bx + 38, 61, 0, -1));
            Controls.Add(MakeArrow("▼", bx + 76, 61, 0, 1));
            Controls.Add(MakeArrow("▶", bx + 114, 61, 1, 0));

            Controls.Add(MakeLabel("步进", 556, 66));
            _step = new NumericUpDown();
            _step.Minimum = 1;
            _step.Maximum = 200;
            _step.Value = 2;
            _step.Font = UiFont;
            _step.Location = new Point(592, 62);
            _step.Size = new Size(56, 25);
            Controls.Add(_step);
            Controls.Add(MakeLabel("像素", 492, 66));

            _pos = new Label();
            _pos.ForeColor = Color.FromArgb(120, 220, 160);
            _pos.AutoSize = true;
            _pos.Font = UiFont;
            _pos.Location = new Point(654, 66);
            Controls.Add(_pos);

            Button finish = new Button();
            finish.Text = "完成调整";
            finish.Font = UiFont;
            finish.FlatStyle = FlatStyle.Flat;
            finish.FlatAppearance.BorderSize = 0;
            finish.BackColor = Color.FromArgb(45, 108, 223);
            finish.ForeColor = Color.White;
            finish.Location = new Point(724, 56);
            finish.Size = new Size(88, 30);
            finish.Click += delegate { Finish(); };
            Controls.Add(finish);

            // 放到屏幕下方中间，尽量不压住提示文字
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 24);

            _poll = new Timer();
            _poll.Interval = 250;
            _poll.Tick += delegate { RefreshPos(); _mgr.ReassertEditStyles(); };
            _poll.Start();
        }

        private Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = Color.FromArgb(200, 206, 216);
            l.Font = UiFont;
            l.Location = new Point(x, y);
            return l;
        }

        private Button MakeArrow(string text, int x, int y, int dx, int dy)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = UiFont;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 98, 112);
            b.BackColor = Color.FromArgb(48, 54, 66);
            b.ForeColor = Color.White;
            b.UseVisualStyleBackColor = false;
            b.Location = new Point(x, y);
            b.Size = new Size(34, 28);
            b.Click += delegate { Nudge(dx, dy); };
            return b;
        }

        private int SelectedIndex
        {
            get { return _target.SelectedIndex < 0 ? 0 : _target.SelectedIndex; }
        }

        private bool HeartSelected
        {
            get { return _heart != null && SelectedIndex == _mgr.AlertCount; }
        }

        private void Nudge(int dx, int dy)
        {
            int step = (int)_step.Value;
            if (HeartSelected) _heart.Nudge(dx * step, dy * step);
            else _mgr.NudgeAlert(SelectedIndex, dx * step, dy * step);
            RefreshPos();
        }

        private void RefreshPos()
        {
            Point p = HeartSelected ? _heart.RestLocation : _mgr.GetAlertPosition(SelectedIndex);
            _pos.Text = "X:" + p.X + "  Y:" + p.Y;
        }

        private void Finish()
        {
            if (_closing) return;
            _closing = true;
            if (_done != null) _done();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: Nudge(-1, 0); e.Handled = true; break;
                case Keys.Right: Nudge(1, 0); e.Handled = true; break;
                case Keys.Up: Nudge(0, -1); e.Handled = true; break;
                case Keys.Down: Nudge(0, 1); e.Handled = true; break;
                case Keys.Escape: Finish(); e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 直接点标题栏的 × 也算“完成”，走同一条退出路径
            if (!_closing)
            {
                e.Cancel = true;
                Finish();
                return;
            }
            base.OnFormClosing(e);
        }

        public void AllowClose()
        {
            _closing = true;
            _poll.Stop();
        }
    }
}
