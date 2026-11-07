// ===================================================================
//  【废案 / RETIRED】2026-10-24 标记
//  这是早期"新版设置"：顶部横向导航的 WinForms 窗口。
//  已确认由 WPF 的 A.R.C.T 控制台（Wpf/WpfVigilData.cs）取代。
//  · 运行时不再被创建：Program.CreateSettingsForm 现在一律返回 SettingsForm；
//  · 本文件仍参与编译（避免连带改动），但没有任何入口会打开它；
//  · 确认要彻底删除时：删除本文件，并确认 Program.cs 中无 ModernSettingsForm 引用。
//  注意：Wpf/ 目录下的 WPF 界面是现行界面，不是废案，不要动。
// ===================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal sealed class ModernSettingsForm : SettingsForm
    {
        private const int WmSettingChange = 0x001A;
        private const int WmThemeChanged = 0x031A;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        private Panel _header;
        private Panel _footer;
        private FlowLayoutPanel _footerLeft;
        private FlowLayoutPanel _footerRight;
        private Label _footerSaved;
        private Label _title;
        private PillLabel _badge;
        private Label _sub;
        private Label _hint;
        private readonly Dictionary<Control, Color> _origBack = new Dictionary<Control, Color>();
        private readonly Dictionary<Control, Color> _origFore = new Dictionary<Control, Color>();
        private readonly Timer _themeDebounce = new Timer();
        private bool _theming;

        static ModernSettingsForm()
        {
            ModernTheme.Active = true;
            ModernTheme.Start();
        }

        public ModernSettingsForm(Config cfg, AlertManager mgr,
                                  Action adjustAction, Action exitAction, Action saveAction,
                                  Action launchGameAction)
            : base(cfg, mgr, adjustAction, exitAction, saveAction, launchGameAction)
        {
            Text = "SCPSLAlert · 控制台界面";
            BackColor = ModernTheme.P.PageBg;
            ClientSize = new Size(1240, 860);
            MinimumSize = new Size(1040, 760);

            if (NavControl != null)
            {
                NavControl.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
                NavControl.Invalidate();
            }

            _themeDebounce.Interval = 90;
            _themeDebounce.Tick += delegate
            {
                _themeDebounce.Stop();
                ApplyTheme();
            };

            ModernTheme.Changed += OnSystemThemeChanged;
            FormClosed += delegate
            {
                ModernTheme.Changed -= OnSystemThemeChanged;
                _themeDebounce.Stop();
                _themeDebounce.Dispose();
            };

            ApplyTheme();
            InsertPageIntros();
            HookDynamic(this);
        }

        protected override bool HorizontalNavigationValue { get { return true; } }
        protected override int HeaderHeightValue { get { return 88; } }
        protected override int FooterHeightValue { get { return 76; } }
        protected override string[] NavigationLabelsValue
        {
            get { return new string[] { "总览", "常规", "提示", "音频", "时间", "热键", "日志", "工具" }; }
        }
        protected override string[] NavigationIconsValue
        {
            get { return new string[] { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE8A5", "\uE90F" }; }
        }

        private void InsertPageIntros()
        {
            Panel[] pages = PagePanels;
            if (pages == null) return;
            string[,] intro = new string[,]
            {
                { "总览", "从这里开始：配置检查、常用入口与上手指南" },
                { "常规", "提示开关、显示行为与所有提示的整体位置" },
                { "提示", "逐条配置提示的内容、样式、触发条件与动画" },
                { "音频", "管理提示音、片段、触发点与提示分配" },
                { "时间", "回合、刷新与各类倒计时的校准" },
                { "热键", "全部热键一览；点输入框后直接按新组合键" },
                { "日志", "运行记录与排查入口" },
                { "工具", "游戏亮度、准星与显示辅助" },
            };
            for (int i = 0; i < pages.Length && i < 8; i++)
            {
                if (pages[i] == null || i == 2) continue;
                TableLayoutPanel stack = FindStack(pages[i]);
                if (stack == null) continue;
                stack.SuspendLayout();
                Control[] existing = new Control[stack.Controls.Count];
                for (int c = 0; c < existing.Length; c++) existing[c] = stack.Controls[c];
                int nextRow = stack.RowCount;
                stack.RowCount = nextRow + 1;
                stack.RowStyles.Clear();
                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
                for (int r = 0; r < nextRow; r++)
                    stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                for (int c = 0; c < existing.Length; c++)
                    stack.SetRow(existing[c], stack.GetRow(existing[c]) + 1);
                ModernPageIntro head = new ModernPageIntro(intro[i, 0], intro[i, 1]);
                head.Dock = DockStyle.Fill;
                head.Margin = new Padding(0, 0, 0, 6);
                stack.Controls.Add(head, 0, 0);
                stack.ResumeLayout();
            }
        }

        private static TableLayoutPanel FindStack(Control parent)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                TableLayoutPanel t = parent.Controls[i] as TableLayoutPanel;
                if (t != null && t.Dock == DockStyle.Top) return t;
                TableLayoutPanel deep = FindStack(parent.Controls[i]);
                if (deep != null) return deep;
            }
            return null;
        }

        protected override int NavWidthValue { get { return 190; } }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmSettingChange || m.Msg == WmThemeChanged)
            {
                if (m.Msg == WmSettingChange && m.WParam == IntPtr.Zero) return;
                _themeDebounce.Stop();
                _themeDebounce.Start();
            }
        }

        private void OnSystemThemeChanged()
        {
            if (IsDisposed || Disposing) return;
            if (!_themeDebounce.Enabled)
            {
                _themeDebounce.Stop();
                _themeDebounce.Start();
            }
        }

        protected override Control BuildHeader()
        {
            UiColors p = ModernTheme.P;
            _header = new Panel();
            _header.Dock = DockStyle.Fill;
            _header.Margin = new Padding(0);
            _header.BackColor = p.HeaderBg;
            _header.Paint += delegate(object s, PaintEventArgs e)
            {
                UiColors c = ModernTheme.P;
                using (Pen pen = new Pen(c.Border))
                    e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
                using (SolidBrush b = new SolidBrush(c.Accent))
                    e.Graphics.FillRectangle(b, 0, _header.Height - 3, _header.Width, 3);
            };

            _title = new Label();
            _title.Text = "SCPSL ALERT";
            _title.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
            _title.ForeColor = p.Text;
            _title.AutoSize = true;
            _title.Location = new Point(26, 11);
            _header.Controls.Add(_title);

            _badge = new PillLabel();
            _badge.Text = "FIELD OPS";
            _badge.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            _badge.Size = new Size(82, 22);
            _badge.Location = new Point(180, 17);
            _header.Controls.Add(_badge);

            _sub = new Label();
            _sub.Text = "CONTROL DESK     /     提示监控 · OCR 识别 · 回合校准";
            _sub.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            _sub.ForeColor = p.HeaderSub;
            _sub.AutoSize = true;
            _sub.Location = new Point(28, 49);
            _header.Controls.Add(_sub);

            _hint = new Label();
            _hint.Text = "AUTO-SAVE  /  READY";
            _hint.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _hint.ForeColor = p.HeaderSub;
            _hint.AutoSize = false;
            _hint.TextAlign = ContentAlignment.MiddleRight;
            _hint.Size = new Size(220, 28);
            _hint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _hint.Location = new Point(_header.Width - _hint.Width - 28, 29);
            _header.Controls.Add(_hint);
            _header.Resize += delegate { _hint.Left = Math.Max(320, _header.Width - _hint.Width - 28); };

            return _header;
        }

        protected override Control BuildFooter()
        {
            _footer = new Panel();
            _footer.Dock = DockStyle.Fill;
            _footer.Margin = new Padding(0);
            _footer.BackColor = ModernTheme.P.FooterBg;
            _footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(ModernTheme.P.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
            };

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Left;
            left.AutoSize = true;
            left.WrapContents = false;
            left.Padding = new Padding(22, 18, 0, 0);
            left.BackColor = ModernTheme.P.FooterBg;
            Button preview = UiKit.Button("预览提示", 104);
            preview.Margin = new Padding(0, 0, 8, 0);
            preview.Click += delegate { PreviewAllAlerts(); };
            left.Controls.Add(preview);
            Button adjust = UiKit.Button("调整提示位置", 124);
            adjust.Margin = new Padding(0, 0, 8, 0);
            adjust.Click += delegate { RunAdjustAction(); };
            left.Controls.Add(adjust);
            Button defaults = UiKit.Button("恢复默认", 96);
            defaults.Margin = new Padding(0, 0, 8, 0);
            defaults.Click += delegate { RestoreDefaults(); };
            left.Controls.Add(defaults);
            _footerLeft = left;
            _footer.Controls.Add(left);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.AutoSize = true;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.Padding = new Padding(0, 18, 22, 0);
            right.BackColor = ModernTheme.P.FooterBg;
            Button save = UiKit.PrimaryButton("保存设置", 112);
            save.Margin = new Padding(8, 0, 0, 0);
            save.Click += delegate { SaveNow(); };
            right.Controls.Add(save);
            Button launch = UiKit.Button("启动游戏", 104);
            launch.Margin = new Padding(8, 0, 0, 0);
            launch.Click += delegate { RunLaunchGameAction(); };
            right.Controls.Add(launch);
            Button exit = UiKit.Button("退出程序", 96);
            exit.Margin = new Padding(8, 0, 0, 0);
            exit.Click += delegate { RunExitAction(); };
            right.Controls.Add(exit);
            Button modern = UiKit.PrimaryButton("进入新版本", 118);
            modern.Margin = new Padding(8, 0, 0, 0);
            modern.Click += delegate { RunSwitchToModernUi(); };
            right.Controls.Add(modern);
            Label saved = new Label();
            saved.AutoSize = false;
            saved.Size = new Size(58, 34);
            saved.Margin = new Padding(10, 0, 4, 0);
            saved.TextAlign = ContentAlignment.MiddleCenter;
            saved.ForeColor = ModernTheme.P.Accent;
            saved.BackColor = ModernTheme.P.FooterBg;
            saved.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            saved.Visible = false;
            right.Controls.Add(saved);
            _footerSaved = saved;
            _footerRight = right;
            RegisterSavedLabel(saved);
            _footer.Controls.Add(right);

            EventHandler repaintFooter = delegate
            {
                if (_footer != null && _footer.IsHandleCreated) _footer.Invalidate(true);
            };
            left.SizeChanged += repaintFooter;
            left.LocationChanged += repaintFooter;
            right.SizeChanged += repaintFooter;
            right.LocationChanged += repaintFooter;
            return _footer;
        }

        private void ApplyTheme()
        {
            if (_theming || _header == null) return;
            _theming = true;
            try
            {
                UiColors p = ModernTheme.P;
                BackColor = p.PageBg;
                ForeColor = p.Text;

                _header.BackColor = p.HeaderBg;
                _title.ForeColor = p.Accent;
                _badge.BackColor = p.AccentSoft;
                _badge.ForeColor = p.Accent;
                _sub.ForeColor = p.HeaderSub;
                _hint.ForeColor = p.HeaderSub;
                _hint.Text = ModernTheme.IsDark ? "AUTO-SAVE  /  DARK MODE" : "AUTO-SAVE  /  LIGHT MODE";
                _header.Invalidate();

                if (_footer != null)
                {
                    _footer.BackColor = p.FooterBg;
                    _footer.Invalidate(true);
                }
                if (_footerLeft != null)
                {
                    _footerLeft.BackColor = p.FooterBg;
                    _footerLeft.Invalidate(true);
                }
                if (_footerRight != null)
                {
                    _footerRight.BackColor = p.FooterBg;
                    _footerRight.Invalidate(true);
                }
                if (_footerSaved != null)
                {
                    _footerSaved.BackColor = p.FooterBg;
                    _footerSaved.ForeColor = p.Accent;
                }

                ThemeChildren(this, true);

                if (NavControl != null)
                {
                    NavControl.BackColor = p.NavBg;
                    NavControl.Invalidate();
                }
                if (ContentPanel != null) ContentPanel.BackColor = p.PageBg;
                if (TipBarPanel != null)
                {
                    TipBarPanel.BackColor = p.MutedBg;
                    TipBarPanel.Invalidate();
                }

                ApplyChrome();
                Invalidate(true);
            }
            finally
            {
                _theming = false;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyChrome();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyChrome();
        }

        private void ApplyChrome()
        {
            if (!IsHandleCreated) return;
            ApplyDarkTitleBar(this, ModernTheme.IsDark);
            ThemeScrollbars(this);
        }

        private static void ApplyDarkTitleBar(Form f, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref value, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref value, 4);
            }
            catch
            {
            }
        }

        private static void ThemeScrollbars(Control root)
        {
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control c = root.Controls[i];
                bool scrollable = (c is ScrollableControl && ((ScrollableControl)c).AutoScroll)
                                  || c is ListBox || c is CheckedListBox;
                if (scrollable && c.IsHandleCreated)
                {
                    try { SetWindowTheme(c.Handle, ModernTheme.IsDark ? "DarkMode_Explorer" : "", ""); }
                    catch { }
                }
                ThemeScrollbars(c);
            }
        }

        private void ThemeChildren(Control parent, bool top)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c == _header || c == _footer) continue;
                ThemeOne(c);
                ThemeChildren(c, false);
            }
        }

        private void ThemeOne(Control c)
        {
            if (c is ModernButton || c is CapsuleCheckBox) return;
            if (c is ModernExpander)
            {
                c.BackColor = ModernTheme.P.PageBg;
                return;
            }
            if (c is ModernListBox || c is ModernCheckedListBox) return;

            if (c is Label)
            {
                Color labelBack = ModernTheme.ContentBack(c);
                if (c.BackColor != labelBack) c.BackColor = labelBack;
                Color labelOriginal;
                if (!_origFore.TryGetValue(c, out labelOriginal))
                {
                    labelOriginal = c.ForeColor;
                    _origFore[c] = labelOriginal;
                }
                Color labelFore = ModernTheme.ForeFor(labelOriginal, ModernTheme.P.PageBg);
                if (c.ForeColor != labelFore) c.ForeColor = labelFore;
                return;
            }

            Color ob;
            if (!_origBack.TryGetValue(c, out ob))
            {
                ob = c.BackColor;
                _origBack[c] = ob;
            }
            Color of;
            if (!_origFore.TryGetValue(c, out of))
            {
                of = c.ForeColor;
                _origFore[c] = of;
            }

            if (ob != Color.Transparent)
            {
                Color nb = ModernTheme.SurfaceFor(ob);
                if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox || c is CheckedListBox)
                    nb = ModernTheme.P.InputBg;
                if (c.BackColor != nb) c.BackColor = nb;
            }

            Color nf = ModernTheme.ForeFor(of, ob);
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox || c is CheckedListBox)
                nf = ModernTheme.P.Text;
            if (c.ForeColor != nf) c.ForeColor = nf;

            if (c is Label && (c.ForeColor == Color.White)) c.ForeColor = ModernTheme.P.Text;
        }

        private void HookDynamic(Control root)
        {
            root.ControlAdded -= OnChildAdded;
            root.ControlAdded += OnChildAdded;
            for (int i = 0; i < root.Controls.Count; i++)
                HookDynamic(root.Controls[i]);
        }

        private void OnChildAdded(object sender, ControlEventArgs e)
        {
            if (e.Control == null || _theming) return;
            _theming = true;
            try
            {
                ThemeOne(e.Control);
                ThemeChildren(e.Control, false);
            }
            finally
            {
                _theming = false;
            }
            HookDynamic(e.Control);
        }
    }

    internal sealed class PillLabel : Control
    {
        public PillLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            ModernTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ModernTheme.ContentBack(this));
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(ModernTheme.P.AccentSoft))
            using (GraphicsPath path = ModernDraw.Round(r, Height / 2))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, Font, r, ModernTheme.P.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ModernTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}

