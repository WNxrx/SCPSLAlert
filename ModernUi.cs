using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal sealed class SideNav : Control
    {
        public readonly List<string> Items = new List<string>();
        public readonly List<string> Icons = new List<string>();
        public event EventHandler SelectedIndexChanged;

        private int _itemHeight = 42;
        private int _selectedIndex = -1;
        private float _indicatorY;
        private int _hover = -1;
        private int _pressed = -1;
        private readonly AnimTimer _anim;
        private readonly AnimTimer _hoverAnim;
        private readonly Font _iconFont;
        private readonly List<float> _fades = new List<float>();
        private bool _dark;
        private bool _horizontal;

        private int NavigationTop
        {
            get { return ModernTheme.IsModernHost(this) ? 104 : 0; }
        }

        public SideNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = UiKit.NavBg;
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            _iconFont = new Font("Segoe MDL2 Assets", 12.5f, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
            _anim = new AnimTimer();
            _anim.Tick += delegate { StepAnimation(); };
            _hoverAnim = new AnimTimer();
            _hoverAnim.Tick += delegate { StepHover(); };
        }

        public int ItemHeight
        {
            get { return _itemHeight; }
            set { _itemHeight = Math.Max(24, value); Invalidate(); }
        }

        public bool Horizontal
        {
            get { return _horizontal; }
            set { _horizontal = value; Invalidate(); }
        }

        public bool Dark
        {
            get { return _dark; }
            set { _dark = value; BackColor = value ? Color.FromArgb(20, 27, 39) : UiKit.NavBg; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                if (v == _selectedIndex) return;
                _selectedIndex = v;
                _anim.Start();
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_horizontal && _selectedIndex >= 0 && Items.Count > 0)
                _indicatorY = _selectedIndex * (Width / (float)Items.Count);
            Invalidate();
        }

        private void StepAnimation()
        {
            float target = _horizontal
                ? (_selectedIndex < 0 ? 0 : _selectedIndex * (Width / (float)Math.Max(1, Items.Count)))
                : (_selectedIndex < 0 ? NavigationTop : NavigationTop + _selectedIndex * _itemHeight);
            float delta = target - _indicatorY;
            if (Math.Abs(delta) < 0.6f)
            {
                _indicatorY = target;
                _anim.Stop();
            }
            else
            {
                _indicatorY += delta * 0.32f;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            UiColors p = ModernTheme.For(this);
            bool themed = ModernTheme.IsModernHost(this);
            Color normal = themed ? p.NavText : (_dark ? Color.FromArgb(196, 206, 221) : UiKit.TextColor);
            Color selectedFill = themed ? p.NavSelected : (_dark ? Color.FromArgb(38, 62, 104) : Color.FromArgb(226, 236, 253));
            Color hoverFill = themed ? p.NavHover : (_dark ? Color.FromArgb(35, 46, 63) : Color.FromArgb(238, 242, 248));
            Color pressFill = themed ? ModernTheme.Mix(p.NavHover, p.Accent, 0.14f) : (_dark ? Color.FromArgb(44, 58, 80) : Color.FromArgb(219, 228, 241));
            Color accent = themed ? p.Accent : UiKit.Accent;

            if (_horizontal)
            {
                PaintHorizontal(g, p, normal, selectedFill, hoverFill, pressFill, accent);
                return;
            }

            if (themed)
            {
                using (SolidBrush tile = new SolidBrush(p.AccentSoft))
                using (GraphicsPath path = Rounded(new Rectangle(20, 22, 42, 42), 12)) g.FillPath(tile, path);
                using (Font logo = new Font("Segoe UI", 21f, FontStyle.Bold))
                using (SolidBrush ink = new SolidBrush(p.Accent))
                {
                    StringFormat centered = new StringFormat();
                    centered.Alignment = StringAlignment.Center;
                    centered.LineAlignment = StringAlignment.Center;
                    g.DrawString("S", logo, ink, new RectangleF(20, 19, 42, 42), centered);
                    centered.Dispose();
                }
                using (Font brand = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (SolidBrush ink = new SolidBrush(p.Text))
                    g.DrawString("SCPSLAlert", brand, ink, 72, 25);
                using (Font micro = new Font("Segoe UI", 7f, FontStyle.Bold))
                using (SolidBrush ink = new SolidBrush(p.SubText))
                    g.DrawString("FIELD CONTROL  /  V2", micro, ink, 73, 46);
                using (Pen line = new Pen(p.Border)) g.DrawLine(line, 18, 82, Width - 18, 82);
                using (Font section = new Font("Segoe UI", 7f, FontStyle.Bold))
                using (SolidBrush ink = new SolidBrush(p.SubText))
                    g.DrawString("CONFIGURATION", section, ink, 24, 88);
            }

            EnsureFades();
            for (int i = 0; i < Items.Count; i++)
            {
                int rowTop = NavigationTop + i * _itemHeight;
                Rectangle row = new Rectangle(0, rowTop, Width, _itemHeight);
                if (i == _pressed)
                {
                    using (SolidBrush b = new SolidBrush(pressFill))
                    using (GraphicsPath path = Rounded(new Rectangle(8, row.Y + 4, Width - 16, _itemHeight - 8), 10))
                        g.FillPath(b, path);
                }
                else if (i < _fades.Count && _fades[i] > 0.01f)
                {
                    int alpha = (int)Math.Round(255 * Math.Min(1f, _fades[i]));
                    using (SolidBrush b = new SolidBrush(ModernTheme.Alpha(hoverFill, alpha)))
                    using (GraphicsPath path = Rounded(new Rectangle(8, row.Y + 4, Width - 16, _itemHeight - 8), 10))
                        g.FillPath(b, path);
                }
            }

            if (_selectedIndex >= 0 && _selectedIndex < Items.Count)
            {
                int y = (int)Math.Round(_indicatorY);
                Rectangle fillRect = new Rectangle(8, y + 4, Width - 16, _itemHeight - 8);
                using (SolidBrush b = new SolidBrush(selectedFill))
                using (GraphicsPath path = Rounded(fillRect, 10))
                    g.FillPath(b, path);
                using (SolidBrush b = new SolidBrush(accent))
                using (GraphicsPath path = Rounded(new Rectangle(8, y + 12, 4, _itemHeight - 24), 2))
                    g.FillPath(b, path);
            }

            StringFormat sf = new StringFormat();
            sf.LineAlignment = StringAlignment.Center;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            bool aligned = Icons.Count > 0;
            for (int i = 0; i < Items.Count; i++)
            {
                bool sel = i == _selectedIndex;
                Color c = sel ? accent : normal;
                int rowY = NavigationTop + i * _itemHeight;
                using (Font f = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(c))
                {
                    int shift = (i == _pressed) ? 1 : 0;
                    Rectangle text = new Rectangle(aligned ? 56 : 24, rowY + shift, Width - (aligned ? 66 : 34), _itemHeight);
                    g.DrawString(Items[i], f, b, text, sf);
                }
                if (aligned && i < Icons.Count && !string.IsNullOrEmpty(Icons[i]))
                {
                    using (SolidBrush b = new SolidBrush(sel ? accent : normal))
                    {
                        StringFormat isf = new StringFormat();
                        isf.LineAlignment = StringAlignment.Center;
                        isf.Alignment = StringAlignment.Center;
                        Rectangle iconRect = new Rectangle(22, rowY + 1, 26, _itemHeight);
                        g.DrawString(Icons[i], _iconFont, b, iconRect, isf);
                        isf.Dispose();
                    }
                }
            }
            sf.Dispose();
        }

        private void PaintHorizontal(Graphics g, UiColors p, Color normal, Color selectedFill,
                                     Color hoverFill, Color pressFill, Color accent)
        {
            EnsureFades();
            int count = Math.Max(1, Items.Count);
            float cellWidth = Width / (float)count;
            for (int i = 0; i < Items.Count; i++)
            {
                int left = (int)Math.Round(i * cellWidth);
                int right = (int)Math.Round((i + 1) * cellWidth);
                Rectangle cell = new Rectangle(left, 0, Math.Max(1, right - left), Height);
                Rectangle chip = new Rectangle(left + 5, 6, Math.Max(1, cell.Width - 10), Height - 12);
                Color fill = i == _pressed ? pressFill : (i == _selectedIndex ? selectedFill : Color.Transparent);
                if (i != _pressed && i != _selectedIndex && i < _fades.Count && _fades[i] > 0.01f)
                    fill = ModernTheme.Alpha(hoverFill, (int)Math.Round(255 * Math.Min(1f, _fades[i])));
                if (fill.A > 0)
                {
                    using (SolidBrush b = new SolidBrush(fill))
                    using (GraphicsPath path = Rounded(chip, 10)) g.FillPath(b, path);
                }
                bool selected = i == _selectedIndex;
                Color ink = selected ? accent : normal;
                int shift = i == _pressed ? 1 : 0;
                if (i < Icons.Count && !string.IsNullOrEmpty(Icons[i]))
                {
                    Rectangle icon = new Rectangle(cell.Left + 12, cell.Top + shift, 22, cell.Height);
                    TextRenderer.DrawText(g, Icons[i], _iconFont, icon, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    Rectangle text = new Rectangle(cell.Left + 37, cell.Top + shift, Math.Max(0, cell.Width - 42), cell.Height);
                    TextRenderer.DrawText(g, Items[i], Font, text, ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                else
                {
                    TextRenderer.DrawText(g, Items[i], Font, cell, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            if (_selectedIndex >= 0 && _selectedIndex < Items.Count)
            {
                int x = (int)Math.Round(_indicatorY);
                int left = x + 22;
                int width = Math.Max(8, (int)Math.Round(cellWidth) - 44);
                using (SolidBrush b = new SolidBrush(accent))
                using (GraphicsPath path = Rounded(new Rectangle(left, Height - 3, width, 3), 1))
                    g.FillPath(b, path);
            }
            using (Pen pen = new Pen(p.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        private void EnsureFades()
        {
            while (_fades.Count < Items.Count) _fades.Add(0f);
            while (_fades.Count > Items.Count) _fades.RemoveAt(_fades.Count - 1);
        }

        private void StepHover()
        {
            EnsureFades();
            bool busy = false;
            for (int i = 0; i < _fades.Count; i++)
            {
                float target = i == _hover ? 1f : 0f;
                float v = _fades[i];
                if (Math.Abs(target - v) > 0.03f)
                {
                    v += (target - v) * 0.3f;
                    busy = true;
                }
                else v = target;
                _fades[i] = v;
            }
            if (!busy) _hoverAnim.Stop();
            Invalidate();
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

        private int IndexAt(Point p)
        {
            if (p.Y < 0) return -1;
            if (_horizontal)
            {
                if (Items.Count == 0 || p.X < 0) return -1;
                int index = (int)(p.X / Math.Max(1f, Width / (float)Items.Count));
                return index >= 0 && index < Items.Count ? index : -1;
            }
            int y = p.Y - NavigationTop;
            if (y < 0) return -1;
            int i = y / _itemHeight;
            return i >= 0 && i < Items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != _hover)
            {
                _hover = i;
                EnsureFades();
                _hoverAnim.Start();
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            _pressed = -1;
            EnsureFades();
            _hoverAnim.Start();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _pressed = IndexAt(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = IndexAt(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0 && i == _pressed) SelectedIndex = i;
            _pressed = -1;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_horizontal && e.KeyCode == Keys.Right) SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
            else if (_horizontal && e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selectedIndex - 1);
            else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
            else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, _selectedIndex - 1);
            else return;
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Dispose();
                _hoverAnim.Dispose();
                _iconFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        public event EventHandler CheckedChanged;

        private bool _checked;
        private float _pos;
        private readonly AnimTimer _anim;
        private bool _hover;
        private bool _pressed;

        public string OnContent = "已开启";
        public string OffContent = "已关闭";

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Size = new Size(300, 34);
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _anim = new AnimTimer();
            _anim.Tick += delegate { Step(); };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _anim.Start();
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        private void Step()
        {
            float target = _checked ? 1f : 0f;
            float delta = target - _pos;
            if (Math.Abs(delta) < 0.02f)
            {
                _pos = target;
                _anim.Stop();
            }
            else
            {
                _pos += delta * 0.28f;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            UiColors p = ModernTheme.For(this);
            Color track = _checked ? p.Accent : p.TrackOff;
            if (_pressed) track = _checked ? ModernTheme.Mix(p.Accent, Color.Black, 0.12f)
                                           : ModernTheme.Mix(p.TrackOff, Color.Black, 0.10f);
            Rectangle tr = new Rectangle(2, (Height - 24) / 2, 46, 24);
            using (SolidBrush b = new SolidBrush(track))
            using (GraphicsPath path = Round(tr, 12))
                g.FillPath(b, path);

            int knobX = 4 + (int)Math.Round(_pos * 22f);
            Rectangle knob = new Rectangle(knobX, (Height - 20) / 2, 20, 20);
            using (SolidBrush b = new SolidBrush(p.Knob))
                g.FillEllipse(b, knob);
            using (Pen pen = new Pen(Color.FromArgb(30, 0, 0, 0)))
                g.DrawEllipse(pen, knob);

            string text = _checked ? OnContent : OffContent;
            Color fore = _checked ? p.Accent : (_hover ? p.Text : p.SubText);
            using (Font f = new Font(Font, _checked ? FontStyle.Bold : FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(fore))
                g.DrawString(text, f, b, 58, (Height - Font.Height) / 2f + 1);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
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

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool inside = ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (e.Button == MouseButtons.Left && inside) Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class UiSettingsPanel : Panel
    {
        private readonly Config _cfg;
        private readonly Action _saveNow;
        private readonly Action _restart;
        private readonly ComboBox _style;
        private readonly ToggleSwitch _toggle;
        private readonly NumericUpDown _duration;
        private readonly Panel _durationRow;
        private readonly Label _state;
        private readonly Timer _saveTimer;
        private readonly AnimTimer _rowTimer;
        private bool _loading;
        private int _rowTarget;
        private int _rowCurrent;

        public UiSettingsPanel(Config cfg, Action saveNow, Action restart)
        {
            _cfg = cfg;
            _saveNow = saveNow;
            _restart = restart;
            BackColor = Color.White;
            Height = 226;
            Padding = new Padding(0);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Label title = new Label();
            title.Text = "软件界面";
            title.Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = UiKit.TextColor;
            title.AutoSize = true;
            title.Location = new Point(20, 14);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "选择主界面风格，并控制整个软件的过渡动画。提示的样式在下方单独调整。";
            sub.Font = UiKit.FontSmall;
            sub.ForeColor = UiKit.SubColor;
            sub.AutoSize = true;
            sub.Location = new Point(20, 44);
            Controls.Add(sub);

            Label styleLabel = new Label();
            styleLabel.Text = "主界面风格";
            styleLabel.Font = UiKit.Font;
            styleLabel.ForeColor = UiKit.TextColor;
            styleLabel.AutoSize = true;
            styleLabel.Location = new Point(20, 78);
            Controls.Add(styleLabel);

            _style = UiKit.NewComboBox();
            _style.Font = UiKit.Font;
            _style.DropDownStyle = ComboBoxStyle.DropDownList;
            _style.Width = 180;
            _style.Location = new Point(150, 74);
            _style.Items.AddRange(new object[] { "新版", "旧版" });
            Controls.Add(_style);

            _state = new Label();
            _state.Text = "切换后需重启程序生效";
            _state.Font = UiKit.FontSmall;
            _state.ForeColor = UiKit.SubColor;
            _state.AutoSize = true;
            _state.Location = new Point(344, 79);
            Controls.Add(_state);

            Label animLabel = new Label();
            animLabel.Text = "全局动画";
            animLabel.Font = UiKit.Font;
            animLabel.ForeColor = UiKit.TextColor;
            animLabel.AutoSize = true;
            animLabel.Location = new Point(20, 120);
            Controls.Add(animLabel);

            _toggle = new ToggleSwitch();
            _toggle.OnContent = "平滑过渡（推荐）";
            _toggle.OffContent = "关闭（即时切换）";
            _toggle.Location = new Point(150, 114);
            _toggle.Size = new Size(300, 34);
            Controls.Add(_toggle);

            _durationRow = new Panel();
            _durationRow.Location = new Point(12, 154);
            _durationRow.Size = new Size(548, 36);
            _durationRow.BackColor = Color.White;
            _durationRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_durationRow);
            Resize += delegate { _durationRow.Width = Math.Max(200, Width - 24); };

            Label durLabel = new Label();
            durLabel.Text = "动画时长";
            durLabel.Font = UiKit.Font;
            durLabel.ForeColor = UiKit.TextColor;
            durLabel.AutoSize = true;
            durLabel.Location = new Point(20, 8);
            _durationRow.Controls.Add(durLabel);

            _duration = new NumericUpDown();
            _duration.Font = UiKit.Font;
            _duration.Minimum = 100;
            _duration.Maximum = 600;
            _duration.Increment = 20;
            _duration.Width = 90;
            _duration.Location = new Point(150, 4);
            _durationRow.Controls.Add(_duration);

            Label durUnit = new Label();
            durUnit.Text = "毫秒（100 - 600）";
            durUnit.Font = UiKit.FontSmall;
            durUnit.ForeColor = UiKit.SubColor;
            durUnit.AutoSize = true;
            durUnit.Location = new Point(248, 8);
            _durationRow.Controls.Add(durUnit);

            Label tip = new Label();
            tip.Text = "新版界面使用圆角卡片、侧栏高亮滑动和页面过渡；旧版保留熟悉布局，随时可回退。";
            tip.Font = UiKit.FontSmall;
            tip.ForeColor = UiKit.SubColor;
            tip.AutoSize = true;
            tip.Location = new Point(20, 198);
            Controls.Add(tip);

            _saveTimer = new Timer();
            _saveTimer.Interval = 600;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); Save(); };

            _rowTimer = new AnimTimer();
            _rowTimer.Tick += delegate { StepRow(); };

            _rowCurrent = 36;
            _rowTarget = 36;

            _style.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                int want = _style.SelectedIndex == 0 ? 0 : 1;
                if (_cfg.MainUiStyle == want) return;
                _cfg.MainUiStyle = want;
                Save();
                _state.Text = "已保存为" + (want == 0 ? "新版" : "旧版") + "主界面";
                if (_restart != null)
                {
                    using (RestartConfirmForm f = new RestartConfirmForm(want == 0 ? "新版" : "旧版"))
                    {
                        Form owner = FindForm();
                        DialogResult r = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                        if (r == DialogResult.OK && f.Accepted) _restart();
                        else _state.Text = "已保存，重启程序后生效";
                    }
                }
            };
            _toggle.CheckedChanged += delegate
            {
                if (_loading) return;
                _cfg.UiAnimationsEnabled = _toggle.Checked;
                ModernTheme.AnimationsEnabled = _toggle.Checked;
                SetDurationExpanded(_toggle.Checked, true);
                Save();
            };
            _duration.ValueChanged += delegate
            {
                if (_loading) return;
                _cfg.UiAnimationDurationMs = (int)_duration.Value;
                _saveTimer.Stop();
                _saveTimer.Start();
            };
        }

        public void Reload()
        {
            _loading = true;
            try
            {
                _style.SelectedIndex = _cfg.MainUiStyle == 0 ? 0 : 1;
                _toggle.Checked = _cfg.UiAnimationsEnabled;
                int ms = Math.Max(100, Math.Min(600, _cfg.UiAnimationDurationMs));
                _duration.Value = ms;
                _state.Text = "切换后需重启程序生效";
                SetDurationExpanded(_cfg.UiAnimationsEnabled, false);
            }
            finally
            {
                _loading = false;
            }
        }

        private void SetDurationExpanded(bool expanded, bool animate)
        {
            _rowTarget = expanded ? 36 : 0;
            if (!animate)
            {
                _rowCurrent = _rowTarget;
                _durationRow.Height = _rowTarget;
                _durationRow.Visible = _rowTarget > 0;
                return;
            }
            _durationRow.Visible = true;
            _rowTimer.Start();
        }

        private void StepRow()
        {
            int delta = _rowTarget - _rowCurrent;
            if (Math.Abs(delta) <= 2)
            {
                _rowCurrent = _rowTarget;
                _rowTimer.Stop();
                _durationRow.Visible = _rowTarget > 0;
            }
            else
            {
                _rowCurrent += (int)Math.Round(delta * 0.3);
            }
            _durationRow.Height = Math.Max(0, _rowCurrent);
        }

        private void Save()
        {
            if (_saveNow != null) _saveNow();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiColors p = ModernTheme.For(this);
            using (Pen pen = new Pen(p.CardBorder))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(p.Accent))
                e.Graphics.FillRectangle(b, 0, 16, 4, Math.Max(1, Height - 32));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _saveTimer.Dispose();
                _rowTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class RestartConfirmForm : Form
    {
        public bool Accepted;

        public RestartConfirmForm(string styleName)
        {
            Text = "SCPSLAlert · 切换主界面";
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            ForeColor = UiKit.TextColor;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(438, 188);

            Label title = new Label();
            title.Text = "已切换为" + styleName + "主界面";
            title.Font = UiKit.FontTitle;
            title.ForeColor = UiKit.TextColor;
            title.AutoSize = true;
            title.Location = new Point(22, 20);
            Controls.Add(title);

            Label body = new Label();
            body.Text = "新的主界面会在程序重启后生效。\r\n现在立即重启程序吗？";
            body.Font = UiKit.Font;
            body.ForeColor = UiKit.SubColor;
            body.AutoSize = false;
            body.Size = new Size(390, 50);
            body.Location = new Point(24, 58);
            Controls.Add(body);

            Button later = UiKit.Button("稍后", 104);
            later.Location = new Point(210, 132);
            later.Click += delegate { Accepted = false; DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(later);

            Button now = UiKit.PrimaryButton("立即重启", 104);
            now.Location = new Point(322, 132);
            now.Click += delegate { Accepted = true; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(now);

            AcceptButton = now;
            CancelButton = later;
            ModernDialogSkin.Apply(this);
        }
    }
}
