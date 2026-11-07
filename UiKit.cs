using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 设置界面的统一外观与排版工具。
    ///
    /// 关键点：所有设置项都用 TableLayoutPanel 排成
    /// 【标签列 | 控件列 | 单位列】三列网格，行高统一，
    /// 这样文字和控件永远不会叠在一起，窗口缩放也不会错位。
    /// </summary>
    internal static class UiKit
    {
        // ---- 配色（清爽的浅色主题 + 深色页头/导航） ----
        public static readonly Color PageBg = Color.FromArgb(244, 246, 249);
        public static readonly Color CardBg = Color.White;
        public static readonly Color HeaderBg = Color.FromArgb(30, 36, 48);
        public static readonly Color NavBg = Color.FromArgb(233, 237, 243);
        public static readonly Color FooterBg = Color.FromArgb(236, 239, 244);
        public static Color Accent { get { return ThemeTokens.For(true); } }
        public static Color AccentHover { get { return ThemeTokens.Lighten(Accent, 0.08f); } }
        public static readonly Color TextColor = Color.FromArgb(34, 38, 46);
        public static readonly Color SubColor = Color.FromArgb(120, 128, 142);
        public static readonly Color BorderColor = Color.FromArgb(214, 219, 228);
        public static readonly Color MutedBg = Color.FromArgb(247, 249, 252);

        // ---- 字体 ----
        public static readonly Font Font = new Font("微软雅黑", 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontBold = new Font("微软雅黑", 9f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontSmall = new Font("微软雅黑", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontTitle = new Font("微软雅黑", 12.5f, FontStyle.Bold, GraphicsUnit.Point);

        // ---- 尺寸 ----
        public const int RowH = 34;      // 每个设置项占的高度
        public const int LabelW = 128;   // 标签列宽
        public const int UnitW = 136;    // 单位 / 说明列宽（要放得下“秒（705 = 11:45）”）
        private const int ModernRowH = 42;

        #region 基础控件

        public static Color PageSurface
        {
            get { return PageBg; }
        }

        public static Color CardSurface
        {
            get { return CardBg; }
        }

        public static Color TextInk
        {
            get { return TextColor; }
        }

        public static Color SubInk
        {
            get { return SubColor; }
        }

        public static Color LineInk
        {
            get { return BorderColor; }
        }

        public static CheckBox NewCheckBox()
        {
            return ModernTheme.Active ? (CheckBox)new CapsuleCheckBox() : new CheckBox();
        }

        public static ListBox NewListBox()
        {
            return ModernTheme.Active ? (ListBox)new ModernListBox() : new ListBox();
        }

        public static CheckedListBox NewCheckedListBox()
        {
            return ModernTheme.Active ? (CheckedListBox)new ModernCheckedListBox() : new CheckedListBox();
        }

        public static ComboBox NewComboBox()
        {
            return ModernTheme.Active ? (ComboBox)new ModernComboBox() : new ComboBox();
        }

        public static NumericUpDown NewNumeric()
        {
            return ModernTheme.Active ? (NumericUpDown)new ModernNumeric() : new WheelSafeNumeric();
        }

        public static Label Label(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Font;
            l.ForeColor = TextInk;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0, 0, 8, 0);
            return l;
        }

        public static Label Unit(string text)
        {
            Label l = Label(text);
            l.ForeColor = SubInk;
            l.Font = FontSmall;
            l.Margin = new Padding(6, 0, 0, 0);
            return l;
        }

        public static Label Section(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontBold;
            l.ForeColor = SubInk;
            l.AutoSize = false;
            l.Height = 22;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        public static NumericUpDown Num(decimal v, decimal min, decimal max, int width)
        {
            // WheelSafeNumeric：滚页面时不会把数字改掉（详见 WheelSafe.cs）
            NumericUpDown n = NewNumeric();
            n.Font = Font;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Minimum = min;
            n.Maximum = max;
            if (v < min) v = min;
            if (v > max) v = max;
            n.Value = v;
            n.Width = width;
            n.Anchor = AnchorStyles.Left;
            n.Margin = new Padding(0);
            n.Height = 25;
            return n;
        }

        public static CheckBox Check(string text, int width)
        {
            CheckBox c = NewCheckBox();
            c.Text = text;
            c.Font = Font;
            c.ForeColor = TextInk;
            c.AutoSize = false;
            c.Width = width;
            c.Height = ModernTheme.Active ? 30 : 24;
            c.Anchor = AnchorStyles.Left;
            c.Margin = new Padding(0);
            c.BackColor = Color.Transparent;
            return c;
        }

        public static ComboBox Combo(bool editable, int width)
        {
            // WheelSafeComboBox：滚页面时不会把选中项改掉（详见 WheelSafe.cs）
            ComboBox c = ModernTheme.Active ? (ComboBox)new ModernComboBox() : new WheelSafeComboBox();
            c.Font = ModernTheme.Active ? ModernTheme.InputFont : Font;
            c.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Anchor = AnchorStyles.Left;
            c.Margin = new Padding(0);
            // 刻意不开 AutoComplete：它会在设置选中项之后把文字改写成列表里的
            // 第一个匹配项（字体这种长列表会把用户选的字体偷偷换掉）。
            // 下拉框自带的“输入首字母跳转”已经够用了。
            return c;
        }

        public static TextBox Text(int width)
        {
            TextBox t = new TextBox();
            t.Font = Font;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Width = width;
            t.Anchor = AnchorStyles.Left;
            t.Margin = new Padding(0);
            return t;
        }

        public static Button Button(string text, int width)
        {
            Button b = ModernTheme.Active ? (Button)new ModernButton() : new Button();
            b.Text = text;
            b.Font = Font;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = LineInk;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 238, 248);
            b.BackColor = CardSurface;
            b.ForeColor = TextInk;
            b.UseVisualStyleBackColor = false;
            b.Width = width;
            b.Height = 30;
            b.Anchor = AnchorStyles.Left;
            b.Margin = new Padding(0);
            return b;
        }

        public static Button PrimaryButton(string text, int width)
        {
            Button b = Button(text, width);
            b.BackColor = ModernTheme.Active ? ModernTheme.P.Accent : Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            return b;
        }

        public static Button ColorSwatch(int width)
        {
            Button b = ModernTheme.Active ? (Button)new ModernButton() : new Button();
            b.Font = Font;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(150, 156, 166);
            b.UseVisualStyleBackColor = false;
            b.BackColor = Color.White;
            b.Width = width;
            b.Height = 26;
            b.Anchor = AnchorStyles.Left;
            b.Margin = new Padding(0);
            return b;
        }

        /// <summary>滑块 + 右边的数值，放在一格控件位里，永远不会和标签打架。</summary>
        public static Panel Slider(int min, int max, out TrackBar track, out Label value)
        {
            Panel host = new Panel();
            host.Height = 30;
            host.Dock = DockStyle.Fill;
            host.Margin = new Padding(0);
            host.BackColor = Color.Transparent;

            value = new Label();
            value.Font = FontSmall;
            value.ForeColor = SubColor;
            value.AutoSize = false;
            value.Width = 46;
            value.Dock = DockStyle.Right;
            value.TextAlign = ContentAlignment.MiddleRight;
            host.Controls.Add(value);

            track = new WheelSafeTrackBar();   // 同样不吃滚轮
            track.Minimum = min;
            track.Maximum = max;
            track.TickFrequency = Math.Max(1, (max - min) / 10);
            track.SmallChange = 1;
            track.Dock = DockStyle.Fill;
            track.Height = 30;
            track.Margin = new Padding(0);
            host.Controls.Add(track);
            track.BringToFront();

            return host;
        }

        #endregion

        #region 网格排版

        /// <summary>新建一张三列网格：标签 | 控件 | 单位。</summary>
        public static TableLayoutPanel Grid()
        {
            TableLayoutPanel t = new BufferedTable();
            t.Dock = DockStyle.Fill;
            t.BackColor = ModernTheme.Active ? Color.White : Color.Transparent;
            t.ColumnCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ModernTheme.Active ? 142 : LabelW));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ModernTheme.Active ? 112 : UnitW));
            t.Padding = ModernTheme.Active ? new Padding(18, 12, 18, 14) : new Padding(14, 6, 14, 8);
            t.Margin = new Padding(0);
            t.AutoSize = false;
            return t;
        }

        /// <summary>往网格里加一行。stretch = 控件是否横向拉满（滑块用 true）。</summary>
        /// <summary>
        /// 加一行“标签 + 控件 + 说明”。返回那一列说明 Label，方便之后改文字。
        ///
        /// shortTip：这一行说明的“简略版”（自己写的一句话，让用户一眼知道这是什么）。
        /// 给了它、或者说明太长，第三列就只放一个「?」按钮，悬停看简略 + 完整。
        /// </summary>
        public static Label Row(TableLayoutPanel t, string label, Control c, string unit, bool stretch)
        {
            return Row(t, label, c, unit, stretch, null);
        }

        public static Label Row(TableLayoutPanel t, string label, Control c, string unit,
                                bool stretch, string shortTip)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;

            // 【行高不能一律用 RowH】像音频页的 ListHolder（固定 170/210px 的列表容器）
            // 比 RowH 高一大截：行样式写死 RowH 的话，列表会溢出这一行、压到下面的行上，
            // 兄弟控件在 z 序里把它挡住 —— 表现就是"列表点不到、选不了、勾不上了"。
            // 所以行高取 max(普通行高, 控件声明的最小高度)，让它在这行里装得下。
            int rh = ModernTheme.Active ? ModernRowH : RowH;
            if (c != null && c.MinimumSize.Height > rh)
                rh = c.MinimumSize.Height + 6;      // +6 是上下 Margin（各 3px）的余量
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, rh));

            t.Controls.Add(Label(label), 0, r);

            c.Margin = new Padding(0, 3, 0, 3);
            if (stretch)
            {
                c.Dock = DockStyle.Fill;
                c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            }
            else
            {
                c.Anchor = AnchorStyles.Left;
            }
            t.Controls.Add(c, 1, r);

            Label u = Unit(unit);
            if (!string.IsNullOrEmpty(unit))
            {
                // 第三列说明：短的、一句话说得完的照原样显示；
                // 长说明（或者作者另给了 shortTip）界面上一律不放文字，
                // 只留一个小「?」按钮，悬停看“简略 + 完整”。
                string shown = ShortUnit(unit);
                bool asButton = !string.IsNullOrEmpty(shortTip) || shown != unit;
                if (!asButton)
                {
                    u.Text = unit;
                    t.Controls.Add(u, 2, r);
                }
                else
                {
                    // 只挂在这个「?」注释按钮上 —— 功能控件（勾选框 / 下拉框 / 功能按钮）
                    // 自己悬停不弹注释，免得用户想操作时被说明栏打断。
                    string brief = string.IsNullOrEmpty(shortTip) ? shown : shortTip;
                    Button q = InfoButton();
                    Tip(q, brief, unit);
                    t.Controls.Add(q, 2, r);
                }
            }
            else
            {
                t.Controls.Add(u, 2, r);
            }

            ResizeGroup(t);
            return u;
        }

        /// <summary>第三列的小「?」说明按钮（悬停看说明，不用点）。</summary>
        public static Button InfoButton()
        {
            Button b = Button("?", 24);
            b.Font = FontSmall;
            b.Height = 22;
            b.FlatAppearance.BorderColor = LineInk;
            b.ForeColor = SubInk;
            b.BackColor = CardSurface;
            b.Anchor = AnchorStyles.Left;
            b.Margin = new Padding(6, 3, 0, 3);
            b.TabStop = false;
            return b;
        }

        /// <summary>加一行“两个控件并排”的（比如 字号 + 加粗）。</summary>
        public static void Row2(TableLayoutPanel t, string label, Control a, int aWidth, Control b, string unit)
        {
            TableLayoutPanel inner = new TableLayoutPanel();
            inner.Dock = DockStyle.Fill;
            inner.Margin = new Padding(0);
            inner.BackColor = Color.Transparent;
            inner.ColumnCount = 2;
            inner.RowCount = 1;                        // 必须显式给 1 行，否则子控件不布局
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, aWidth));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            a.Dock = DockStyle.Left;
            a.Margin = new Padding(0, 3, 0, 3);
            b.Dock = DockStyle.Left;
            b.Margin = new Padding(10, 3, 0, 3);
            inner.Controls.Add(a, 0, 0);
            inner.Controls.Add(b, 1, 0);

            Row(t, label, inner, unit, true);
        }

        /// <summary>加一行整行都是文字说明的（浅灰小字）。</summary>
        public static void Note(TableLayoutPanel t, string text)
        {
            Note(t, text, null);
        }

        // ---- 悬停说明（不用 Windows 原生 tooltip）----
        // 鼠标停到带说明的控件上时发 TipShown 事件，由设置窗口底部的“说明栏”显示；
        // 没有窗口订阅时（其它小窗口）才退回原生 ToolTip。
        public static event Action<string, string> TipShown;    // (简略说明, 完整说明)
        public static event Action TipHidden;

        /// <summary>鼠标当前停着的、带说明的控件（滚轮路由用它判断“滚轮该滚说明栏”）。</summary>
        public static Control LastTipSource;

        private static ToolTip _tip;   // 兜底用：没有订阅者时才创建

        /// <summary>给任意控件挂悬停说明：简略 + 完整（在订阅了 TipShown 的窗口里显示）。</summary>
        public static void Tip(Control c, string shortText, string fullText)
        {
            if (c == null || string.IsNullOrEmpty(shortText)) return;
            c.Cursor = Cursors.Help;
            c.MouseEnter += delegate
            {
                LastTipSource = c;
                if (TipShown != null) TipShown(shortText, fullText);
                else
                {
                    if (_tip == null)
                    {
                        _tip = new ToolTip();
                        _tip.AutoPopDelay = 15000;
                        _tip.InitialDelay = 450;
                        _tip.ReshowDelay = 120;
                        _tip.ShowAlways = true;
                    }
                    string s = shortText;
                    if (!string.IsNullOrEmpty(fullText)) s += "\n\n" + fullText;
                    _tip.SetToolTip(c, s);
                }
            };
            c.MouseLeave += delegate
            {
                if (LastTipSource == c) LastTipSource = null;
                if (TipHidden != null) TipHidden();
            };
        }

        /// <summary>
        /// 第三列说明的“简略版”：只留第一句（遇到换行 / 句号 / 分号就切），
        /// 超过 14 个字再截断。完整原文悬停时显示在窗口底部说明栏里。
        /// </summary>
        private static string ShortUnit(string s)
        {
            int cut = s.IndexOfAny(new char[] { '\n', '。', '；' });
            string first = cut > 0 ? s.Substring(0, cut) : s;
            bool trimmed = cut > 0;
            if (first.Length > 14)
            {
                first = first.Substring(0, 14);
                trimmed = true;
            }
            if (trimmed) first += "…";
            return first;
        }

        /// <summary>
        /// 加一行说明按钮：界面上**不放长文字**，只放一个小按钮（title），
        /// 悬停时在窗口底部说明栏显示 shortText + fullText。玩家想看就看。
        /// </summary>
        public static void NoteButton(TableLayoutPanel t, string title, string shortText, string fullText)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            Button b = ModernTheme.Active ? (Button)new ModernButton() : new Button();
            b.Text = title;
            b.Font = FontSmall;
            b.Width = 20 + title.Length * 14;
            b.Height = 24;
            b.Margin = new Padding(0, 2, 0, 2);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = ModernTheme.Active
                ? ModernTheme.Alpha(ModernTheme.P.Border, 90)
                : Color.FromArgb(70, 255, 255, 255);
            b.ForeColor = SubInk;
            b.BackColor = Color.Transparent;
            Tip(b, shortText, fullText);

            t.Controls.Add(b, 0, r);
            t.SetColumnSpan(b, 3);

            ResizeGroup(t);
        }

        /// <summary>
        /// 加一行说明文字：界面上只放 shortText（精简后的），
        /// fullText 不为空时悬停在这行字上会弹出完整说明。
        /// </summary>
        public static void Note(TableLayoutPanel t, string shortText, string fullText)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            Label l = new Label();
            l.Text = shortText;
            l.Font = FontSmall;
            l.ForeColor = SubInk;
            l.AutoSize = false;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0, 4, 0, 0);
            if (!string.IsNullOrEmpty(fullText))
            {
                l.Cursor = Cursors.Help;
                Tip(l, shortText, fullText);
            }

            t.Controls.Add(l, 0, r);
            t.SetColumnSpan(l, 3);

            ResizeGroup(t);
        }

        /// <summary>
        /// 按网格里实际有多少行，把外层的分组框撑到够高。
        /// 这样加行 / 删行都不用再去改别处的数字，也不会出现
        /// 最后一行被分组框边缘切掉、下拉框点不到的情况。
        /// </summary>
        public static void ResizeGroup(TableLayoutPanel t)
        {
            GroupBox g = t.Tag as GroupBox;
            if (g == null) return;

            int content = 0;
            for (int i = 0; i < t.RowStyles.Count; i++)
                content += (int)t.RowStyles[i].Height;

            if (ModernTheme.Active)
            {
                g.Height = ModernExpander.HeaderH + content + 34;
                return;
            }

            // 标题栏 + 上下内边距 + 一点余量（换 DPI / 换字体也不会被切掉）
            g.Height = 26 + content + 44;
        }

        /// <summary>
        /// 分组框。高度不用手动算：每次 Row / Note 加行时会自动把它撑高
        /// （rows 只是个起始估值，后面会被实际行数覆盖）。
        /// </summary>
        public static GroupBox Group(string title, int rows, out TableLayoutPanel grid)
        {
            GroupBox g = ModernTheme.Active ? (GroupBox)new ModernExpander() : new GroupBox();
            g.Text = title;
            g.Font = Font;
            g.ForeColor = TextInk;
            g.BackColor = ModernTheme.Active ? ModernTheme.P.PageBg : CardBg;
            g.Dock = DockStyle.Fill;
            g.Margin = new Padding(0, 0, 0, 14);
            g.Padding = ModernTheme.Active
                ? new Padding(16, ModernExpander.HeaderH + 8, 16, 12)
                : new Padding(10, 6, 10, 10);

            grid = Grid();
            grid.Tag = g;              // 让 Row / Note 知道该撑哪个分组框
            g.Controls.Add(grid);

            g.Height = ModernTheme.Active
                ? ModernExpander.CollapsedH + 20
                : 26 + Math.Max(1, rows) * RowH + 44;
            return g;
        }

        /// <summary>装分组框的页面（可纵向滚动，缩放不错位）。</summary>
        public static Panel Page()
        {
            Panel p = new BufferedPanel();
            p.Dock = DockStyle.Fill;
            p.BackColor = PageSurface;
            p.AutoScroll = true;
            p.Padding = new Padding(16, 14, 16, 14);
            return p;
        }

        /// <summary>把分组框垂直堆到一个页面上（用网格，随窗口缩放自动对齐）。</summary>
        public static TableLayoutPanel Stack()
        {
            TableLayoutPanel t = new BufferedStack();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 1;
            t.BackColor = PageSurface;
            t.Padding = new Padding(18, 16, 18, 16);
            t.Margin = new Padding(0);
            return t;
        }

        public static void AddGroup(TableLayoutPanel stack, Control group)
        {
            int r = stack.RowCount;
            stack.RowCount = r + 1;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            group.Dock = DockStyle.Top;
            group.Margin = new Padding(0, 0, 0, 14);
            stack.Controls.Add(group, 0, r);
        }

        #endregion
    }

    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
        }
    }

    internal sealed class BufferedTable : TableLayoutPanel
    {
        public BufferedTable()
        {
            DoubleBuffered = true;
        }
    }

    internal sealed class BufferedStack : TableLayoutPanel
    {
        public BufferedStack()
        {
            DoubleBuffered = true;
        }
    }
}
