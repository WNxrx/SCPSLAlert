using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// OCR 区域列表管理窗口。
    ///
    /// 一个列表 + 一组参数：第 1 组默认是“左下角播报”（定时轮询），
    /// 后面可以自己加“血量区域”“发电机区域”之类，加出来的默认都是
    /// “按需”——只在按“立即扫描”快捷键时截一次。
    ///
    /// 编辑的是一份副本，点确定才写回配置；点取消什么都不改。
    /// </summary>
    internal class OcrRegionForm : Form
    {
        private readonly Config _cfg;
        private readonly List<OcrRegion> _list = new List<OcrRegion>();

        /// <summary>框选后高亮显示几秒。</summary>
        private const int FocusSeconds = 4;

        private ListBox _box;
        private ContextMenuStrip _boxMenu;
        private TextBox _name;
        private NumericUpDown _numX;
        private NumericUpDown _numY;
        private NumericUpDown _numW;
        private NumericUpDown _numH;
        private CheckBox _chkBottom;
        private CheckBox _chkEnabled;
        private CheckBox _chkPoll;
        private CheckBox _chkPinSelected;
        private CheckBox _chkPersist;
        private ComboBox _cboKind;
        private Label _lblStatus;
        private Button _btnDelete;

        private bool _loading;

        public OcrRegionForm(Config cfg)
        {
            _cfg = cfg;
            for (int i = 0; i < cfg.OcrRegions.Count; i++)
                _list.Add(cfg.OcrRegions[i].Clone());

            Text = "OCR 区域列表";
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            ForeColor = UiKit.TextColor;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            // 【为什么要置顶】开了"持续显示"之后屏幕上会一直有一层透明遮罩画着框，
            // 用户接下来就要对着这些框调坐标 —— 这时候本窗口要是被压到后面，
            // 就没法一边看框一边改了。遮罩本身是纯描边、不挡鼠标，
            // 所以把本窗口置顶不会有任何副作用。
            TopMost = true;
            ClientSize = new Size(790, 540);
            MinimumSize = new Size(720, 480);

            BuildUi();
            ReloadList(0);

            // 打开就把现有区域画在屏幕上（几秒后自动消失）：
            // 光看坐标数字是没有空间感的，用户得知道框到底在哪。
            Shown += delegate { FlashRegions(null); };
        }

        /// <summary>点“确定”之后的区域列表（调用方写回配置并保存）。</summary>
        public List<OcrRegion> Result
        {
            get { return _list; }
        }

        #region 界面

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.Padding = new Padding(14, 12, 14, 12);
            root.BackColor = UiKit.PageBg;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            // ---------------- 左：区域列表 ----------------
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 3;
            left.BackColor = Color.Transparent;
            left.Margin = new Padding(0, 0, 12, 0);
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            Label lbTitle = new Label();
            lbTitle.Text = "区域列表（第 1 组默认是播报）";
            lbTitle.Font = UiKit.FontBold;
            lbTitle.ForeColor = UiKit.SubColor;
            lbTitle.Dock = DockStyle.Fill;
            lbTitle.TextAlign = ContentAlignment.MiddleLeft;
            left.Controls.Add(lbTitle, 0, 0);

            _box = new ListBox();
            _box.Font = UiKit.Font;
            _box.Dock = DockStyle.Fill;
            _box.IntegralHeight = false;
            _box.BorderStyle = BorderStyle.FixedSingle;
            _box.SelectedIndexChanged += delegate { OnSelectionChanged(); };
            left.Controls.Add(_box, 0, 1);

            // 右键菜单：框选这类"作用在选中区域上"的操作都收在这里，
            // 免得主界面上按钮太多、看不出哪个是改哪个区域的。
            _boxMenu = new ContextMenuStrip();
            _boxMenu.Font = UiKit.Font;

            ToolStripMenuItem mPick = new ToolStripMenuItem("鼠标重新框选这个区域…");
            mPick.Click += delegate { PickRegion(); };
            _boxMenu.Items.Add(mPick);

            ToolStripMenuItem mPreview = new ToolStripMenuItem("在屏幕上高亮显示这个区域");
            mPreview.Click += delegate { FlashSelected(); };
            _boxMenu.Items.Add(mPreview);

            _boxMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem mAdd = new ToolStripMenuItem("新增区域");
            mAdd.Click += delegate { AddRegion(); };
            _boxMenu.Items.Add(mAdd);

            // 第 4 批："补一个屏幕中心的角色框"已删除（角色改由阵营识别读血条颜色）。

            ToolStripMenuItem mSil = new ToolStripMenuItem("补右下角静音开关框（判断复活）");
            mSil.Click += delegate { Commit(); _list.Add(OcrRegion.NewSilence()); ReloadList(_list.Count - 1); };
            _boxMenu.Items.Add(mSil);

            ToolStripMenuItem mList = new ToolStripMenuItem("补左侧 SCP 列表框（标在场）");
            mList.Click += delegate { Commit(); _list.Add(OcrRegion.NewScpList()); ReloadList(_list.Count - 1); };
            _boxMenu.Items.Add(mList);

            ToolStripMenuItem mTicket = new ToolStripMenuItem("补 4 个票数框（死亡界面用）");
            mTicket.Click += delegate { AddTicketRegions(); };
            _boxMenu.Items.Add(mTicket);

            ToolStripMenuItem mDel = new ToolStripMenuItem("删除这个区域");
            mDel.Click += delegate { DeleteRegion(); };
            _boxMenu.Items.Add(mDel);

            _boxMenu.Opening += delegate
            {
                bool has = Selected != null;
                mPick.Enabled = has;
                mPreview.Enabled = has;
                mDel.Enabled = has;
            };

            // 在列表上点右键时，先把它指的那一行选中，再弹菜单
            _box.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                int idx = _box.IndexFromPoint(e.Location);
                if (idx >= 0 && idx < _box.Items.Count) _box.SelectedIndex = idx;
            };
            _box.ContextMenuStrip = _boxMenu;

            TableLayoutPanel leftBtns = new TableLayoutPanel();
            leftBtns.Dock = DockStyle.Fill;
            leftBtns.ColumnCount = 2;
            leftBtns.RowCount = 1;
            leftBtns.Margin = new Padding(0, 6, 0, 0);
            leftBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            leftBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            leftBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Button add = UiKit.Button("新增区域", 120);
            add.Dock = DockStyle.Fill;
            add.Click += delegate { AddRegion(); };
            leftBtns.Controls.Add(add, 0, 0);

            _btnDelete = UiKit.Button("删除选中", 120);
            _btnDelete.Dock = DockStyle.Fill;
            _btnDelete.Click += delegate { DeleteRegion(); };
            leftBtns.Controls.Add(_btnDelete, 1, 0);

            left.Controls.Add(leftBtns, 0, 2);
            root.Controls.Add(left, 0, 0);

            // ---------------- 右：参数 ----------------
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 2;
            right.BackColor = Color.Transparent;
            right.Margin = new Padding(0);
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            TableLayoutPanel stack = UiKit.Stack();
            stack.Padding = new Padding(0, 0, 0, 0);

            TableLayoutPanel g;
            GroupBox b = UiKit.Group("选中区域的参数", 9, out g);

            _name = UiKit.Text(200);
            _name.TextChanged += delegate { OnEdited(); };
            UiKit.Row(g, "名称", _name, "给自己看的，例如“血量区域”", false);

            _numX = UiKit.Num(0, 0, 8000, 84);
            _numX.ValueChanged += delegate { OnEdited(); };
            _numY = UiKit.Num(0, 0, 8000, 84);
            _numY.ValueChanged += delegate { OnEdited(); };
            UiKit.Row2(g, "左上角 X / Y", _numX, 96, _numY, "像素");

            _numW = UiKit.Num(400, 100, 4000, 84);
            _numW.ValueChanged += delegate { OnEdited(); };
            _numH = UiKit.Num(200, 20, 2000, 84);
            _numH.ValueChanged += delegate { OnEdited(); };
            UiKit.Row2(g, "宽 / 高", _numW, 96, _numH, "像素");

            Button pick = UiKit.Button("重新框选这个区域…", 190);
            pick.Click += delegate { PickRegion(); };
            UiKit.Row(g, "改区域位置", pick,
                "在屏幕上拖一个框，就把【当前选中的这个区域】换成新位置", false);

            _chkBottom = UiKit.Check("Y 是从屏幕底边算起的边距", 300);
            _chkBottom.CheckedChanged += delegate { OnEdited(); };
            UiKit.Row(g, "Y 的参照", _chkBottom, "播报区默认勾选", false);

            _cboKind = UiKit.Combo(false, 140);
            // 【顺序必须和 OcrRegionKind 的取值一一对应】
            // 这个下拉框的值是直接按索引映射到枚举的（见 Commit 里的
            // (OcrRegionKind)_cboKind.SelectedIndex），所以**新增一种用途时
            // 必须在这里也加一行、而且放在同样的位置**，否则用户一改别的参数
            // 就会把用途改成错的（踩过：加了 ScpHp 却没加这一行，
            // 结果血量表的用途会被写成前面的某一项）。
            _cboKind.Items.Add("播报");                                    // 0 Broadcast
            _cboKind.Items.Add("血量");                                    // 1 Health
            _cboKind.Items.Add("发电机");                                  // 2 Generator
            _cboKind.Items.Add("通用");                                    // 3 Generic
            _cboKind.Items.Add("角色（已删除，别选）");                    // 4 Role（占位，保持索引）
            _cboKind.Items.Add("票数（死亡界面上方）");                    // 5 Ticket
            _cboKind.Items.Add("静音开关（复活判断）");                    // 6 Silence
            _cboKind.Items.Add("SCP 列表（左侧，标在场）");                // 7 ScpList
            _cboKind.Items.Add("SCP 血量表（中偏右，HP:当前/上限）");      // 8 ScpHp
            _cboKind.SelectedIndexChanged += delegate { OnEdited(); };
            UiKit.Row(g, "用途", _cboKind, "决定用哪套识别规则", false);

            _chkPoll = UiKit.Check("定时轮询这个区域", 260);
            _chkPoll.CheckedChanged += delegate { OnEdited(); };
            UiKit.Row(g, "扫描方式", _chkPoll, "不勾 = 只在按“立即扫描”时扫一次", false);

            _chkEnabled = UiKit.Check("启用这个区域", 260);
            _chkEnabled.CheckedChanged += delegate { OnEdited(); };
            UiKit.Row(g, "启用", _chkEnabled, "", false);

            // ---- 把框画在屏幕上（让用户"看得见自己在框哪") ----
            //
            // 两个开关各管一件事，也可以一起用：
            //   · 「一直显示选中的框」：选中谁就一直画谁。调坐标 / 重新框选时
            //     这块框会跟着动，一眼能看出改对没有，不用来回闪。
            //   · 「持续显示区域位置」：不分选中谁，把所有启用的框都一直画着。
            //
            // 持久显示模式**不带那层黑色遮罩**（只画彩色描边）——
            // 整屏压暗会严重影响一边看游戏一边调框；一闪而过的模式才需要压暗。
            _chkPinSelected = UiKit.Check("一直显示选中的框", 240);
            _chkPinSelected.CheckedChanged += delegate { RefreshOverlay(); };
            UiKit.Row(g, "选中就显示", _chkPinSelected,
                "选中哪个区域就一直把它画在屏幕上，切走或关窗才消失", false);

            _chkPersist = UiKit.Check("持续显示区域位置", 240);
            _chkPersist.CheckedChanged += delegate { RefreshOverlay(); };
            UiKit.Row(g, "持续显示", _chkPersist,
                "把所有启用的框一直画在屏幕上，直到取消勾选或关闭本窗口", false);

            Button preview = UiKit.Button("预览识别这个区域", 170);
            preview.Click += delegate { Preview(); };
            UiKit.Row(g, "试一下", preview, "照当前参数截一张看看", false);

            _lblStatus = UiKit.Label("");
            _lblStatus.ForeColor = UiKit.SubColor;
            _lblStatus.Font = UiKit.FontSmall;
            UiKit.Row(g, "状态", _lblStatus, "", false);

            UiKit.Note(g, "推荐直接点【鼠标框选】：屏幕上拖一个框就填好了，比手填坐标准得多。");
            UiKit.Note(g, "框选填的是屏幕绝对坐标，所以「Y 的参照」会自动取消勾选。");
            UiKit.Note(g, "播报区建议只留一组并保持定时轮询；血量 / 发电机这类区域不要轮询，按需扫更省。");
            UiKit.AddGroup(stack, b);
            right.Controls.Add(stack, 0, 0);

            // ---------------- 底部按钮 ----------------
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 3;
            footer.RowCount = 1;
            footer.Margin = new Padding(0);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Button cancel = UiKit.Button("取消", 100);
            cancel.Dock = DockStyle.Fill;
            cancel.Margin = new Padding(6, 6, 0, 6);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(cancel, 2, 0);

            Button ok = UiKit.PrimaryButton("确定", 100);
            ok.Dock = DockStyle.Fill;
            ok.Margin = new Padding(6, 6, 0, 6);
            ok.Click += delegate { OnOk(); };
            footer.Controls.Add(ok, 1, 0);

            right.Controls.Add(footer, 0, 1);
            root.Controls.Add(right, 1, 0);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        #endregion

        #region 列表操作

        private void ReloadList(int select)
        {
            _loading = true;
            try
            {
                _box.Items.Clear();
                for (int i = 0; i < _list.Count; i++)
                    _box.Items.Add((i + 1) + ".  " + _list[i].Describe());
                if (_box.Items.Count > 0)
                {
                    if (select < 0) select = 0;
                    if (select >= _box.Items.Count) select = _box.Items.Count - 1;
                    _box.SelectedIndex = select;
                }
            }
            finally
            {
                _loading = false;
            }
            LoadSelected();
        }

        private OcrRegion Selected
        {
            get
            {
                int i = _box == null ? -1 : _box.SelectedIndex;
                return (i >= 0 && i < _list.Count) ? _list[i] : null;
            }
        }

        private void OnSelectionChanged()
        {
            if (_loading) return;
            LoadSelected();

            // 选中项变了 → 屏幕上高亮的框跟着换。
            // 这就是"选中某个框后就一直出现那个框，直到取消选中或关闭"。
            RefreshOverlay();
        }

        /// <summary>把选中区域的值刷新到右边控件上。</summary>
        private void LoadSelected()
        {
            OcrRegion r = Selected;
            _loading = true;
            try
            {
                bool has = r != null;
                if (_name != null) _name.Enabled = has;
                _numX.Enabled = _numY.Enabled = _numW.Enabled = _numH.Enabled = has;
                _chkBottom.Enabled = _chkEnabled.Enabled = _chkPoll.Enabled = has;
                _cboKind.Enabled = has;
                if (_btnDelete != null) _btnDelete.Enabled = _list.Count > 1;

                if (!has) return;

                _name.Text = r.Name;
                _numX.Value = Clamp(r.X, _numX);
                _numY.Value = Clamp(r.Y, _numY);
                _numW.Value = Clamp(r.Width, _numW);
                _numH.Value = Clamp(r.Height, _numH);
                _chkBottom.Checked = r.FromBottom;
                _chkEnabled.Checked = r.Enabled;
                _chkPoll.Checked = r.Poll;
                _cboKind.SelectedIndex = (int)r.Kind;
                _lblStatus.Text = r.Describe();
            }
            finally
            {
                _loading = false;
            }
        }

        private static decimal Clamp(int v, NumericUpDown n)
        {
            decimal d = v;
            if (d < n.Minimum) return n.Minimum;
            if (d > n.Maximum) return n.Maximum;
            return d;
        }

        /// <summary>把右边控件的值写回选中区域（每次切换前先提交，免得丢改动）。</summary>
        private void Commit()
        {
            OcrRegion r = Selected;
            if (r == null) return;

            r.Name = _name.Text.Trim();
            if (r.Name.Length == 0) r.Name = "区域";
            if (r.Name.Length > 24) r.Name = r.Name.Substring(0, 24);
            r.X = (int)_numX.Value;
            r.Y = (int)_numY.Value;
            r.Width = (int)_numW.Value;
            r.Height = (int)_numH.Value;
            // 旧界面改的是物理像素；清掉归一化，保存时按新值重新折算。
            r.ClearNormalized();
            r.FromBottom = _chkBottom.Checked;
            r.Enabled = _chkEnabled.Checked;
            r.Poll = _chkPoll.Checked;
            r.Kind = (OcrRegionKind)Math.Max(0,
                Math.Min(_cboKind.Items.Count - 1, _cboKind.SelectedIndex));
            // 第 4 批：屏幕中心角色框已删除。下拉框保留第 5 项占位（不改索引，
            // 否则票数/静音/SCP 列表的用途会整体错位），选中它一律按"通用"存。
            if (r.Kind == OcrRegionKind.Role) r.Kind = OcrRegionKind.Generic;
        }

        /// <summary>控件值变了：提交 + 刷新列表文字（不重建整张列表，避免闪烁）。</summary>
        private void OnEdited()
        {
            if (_loading) return;
            int sel = _box.SelectedIndex;
            Commit();
            _loading = true;
            try
            {
                if (sel >= 0 && sel < _box.Items.Count && sel < _list.Count)
                    _box.Items[sel] = (sel + 1) + ".  " + _list[sel].Describe();
                OcrRegion r = Selected;
                if (r != null) _lblStatus.Text = r.Describe();
            }
            finally
            {
                _loading = false;
            }

            // 坐标 / 名称 / 启用状态一改，屏幕上的框要跟着动，
            // 否则用户改了坐标却看不出改对没有（那就还得来回闪）。
            RefreshOverlay();
        }

        private void AddRegion()
        {
            int sel = _box.SelectedIndex;
            Commit();

            OcrRegion r = new OcrRegion();
            r.Name = "区域 " + (_list.Count + 1);
            r.X = 0;
            r.Y = 120;
            r.FromBottom = false;
            r.Width = 400;
            r.Height = 200;
            r.Enabled = true;
            r.Poll = false;                    // 加出来的默认按需扫，不轮询
            r.Kind = OcrRegionKind.Generic;
            _list.Add(r);

            ReloadList(_list.Count - 1);
            _name.Focus();
            _name.SelectAll();
        }

        /// <summary>补上死亡界面那 4 个票数 / 影响力框（需求八）。</summary>
        private void AddTicketRegions()
        {
            Commit();
            // 坐标按 1920×1080 实测：框略大于数字本身，多出来的边由票数识别
            // 那套颜色掩膜自己裁掉（框紧贴数字反而会让识别变差）。
            _list.Add(OcrRegion.NewTicket("MTF 令牌", "mtf_tokens", 428, 0, 190, 84));
            _list.Add(OcrRegion.NewTicket("MTF 影响力", "mtf_influence", 576, 0, 190, 70));
            _list.Add(OcrRegion.NewTicket("混沌令牌", "chaos_tokens", 1411, 14, 100, 56));
            _list.Add(OcrRegion.NewTicket("混沌影响力", "chaos_influence", 1259, 14, 100, 50));
            ReloadList(_list.Count - 4);
            MessageBox.Show(this,
                "已补上 4 个票数框（坐标按 1920×1080 的实测值）。"
              + "换分辨率的话，选中每一行再用【鼠标重新框选这个区域…】自己框一次就能对准。",
                "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DeleteRegion()
        {
            int sel = _box.SelectedIndex;
            if (sel < 0 || sel >= _list.Count) return;

            if (_list.Count <= 1)
            {
                MessageBox.Show(this, "至少要留一组区域。", "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, "删除「" + _list[sel].Name + "」？", "SCPSLAlert",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _list.RemoveAt(sel);
            ReloadList(sel >= _list.Count ? _list.Count - 1 : sel);
        }

        /// <summary>
        /// 在屏幕上把当前所有区域画出来给用户看，几秒后**遮罩和框一起**淡出消失。
        /// 打开区域列表时会自动调一次 —— 光看坐标数字是没有空间感的。
        ///
        /// 这里默认**带遮罩**：一闪而过的时候压暗画面能帮你看清每个框的走向。
        /// 不想压暗的场合（比如只看一个框）请用带 mask 参数的那个重载。
        /// </summary>
        public void FlashRegions(string focusName)
        {
            FlashRegions(focusName, true);
        }

        /// <summary>
        /// 当前有没有理由让框一直留在屏幕上。
        ///
        /// 「一直显示选中的框」= 有选中项就一直显示；
        /// 「持续显示区域位置」= 不分选中谁，全都要显示。
        /// 两个开关任意一个开了都算。
        /// </summary>
        private bool PersistWanted
        {
            get
            {
                if (_chkPersist != null && _chkPersist.Checked) return true;
                if (_chkPinSelected != null && _chkPinSelected.Checked && Selected != null) return true;
                return false;
            }
        }

        /// <summary>
        /// 按当前开关状态刷新屏幕上的框。
        ///
        /// 三种去向：
        ///   · 要持久显示 → 建/更新遮罩，切到持久模式（不带黑遮罩）；
        ///   · 本来就开着持久、现在不要了 → 收掉（这就是"取消勾选就消失"）；
        ///   · 本来就没开、现在也不要 → 什么都不做（不要去闪一下打扰用户）。
        ///
        /// focusName 是要不要额外高亮某一个；传 null 表示"按选中项自己算"。
        /// </summary>
        private void RefreshOverlay(string focusName)
        {
            if (_loading) return;
            try
            {
                if (PersistWanted)
                {
                    EnsureOverlay(focusName);
                    return;
                }

                if (_overlay != null && _overlay.Persistent) CloseOverlay();
            }
            catch (Exception ex)
            {
                Logger.Err("OCR", "刷新区域显示失败", ex);
            }
        }

        private void RefreshOverlay() { RefreshOverlay(null); }

        /// <summary>
        /// 保证屏幕上有一个"持久显示"的遮罩，并把最新的区域 / 高亮同步进去。
        ///
        /// 【为什么每次都重新 SetRegions】用户正在改坐标 —— 编号框一动、
        /// 拖框一完成，帧就要跟着动，否则用户看不出自己改对没有。
        /// SetRegions 内部会重设 Bounds 并 Invalidate，等于一次完整重画。
        /// </summary>
        private void EnsureOverlay(string focusName)
        {
            Commit();   // 把界面上正在改的坐标先落到模型里

            if (_flashTimer != null) { _flashTimer.Stop(); _flashTimer = null; }

            if (_overlay == null || _overlay.IsDisposed)
            {
                _overlay = new RegionOverlayForm();
                _overlay.Persistent = true;
                _overlay.Show();
            }
            _overlay.Persistent = true;

            OcrRegion sel = Selected;
            string focus = focusName;
            if (focus == null && sel != null && _chkPinSelected != null && _chkPinSelected.Checked)
                focus = sel.Name;

            _overlay.SetRegions(_list, focus);
            _overlay.BringToFront();
        }

        /// <summary>立刻收掉遮罩（窗口关掉 / 点确定时用，不留残影）。</summary>
        private void CloseOverlay()
        {
            if (_overlay == null) return;
            try { _overlay.Close(); } catch { }
            _overlay = null;
        }

        private RegionOverlayForm _overlay;
        private Timer _flashTimer;

        /// <summary>
        /// 只高亮当前选中的那一个区域，**不压暗屏幕**，几秒后自动消失。
        ///
        /// 【为什么不压暗】看单个框时整屏变暗很打扰（用户可能正对着游戏调框），
        /// 而且只高亮一个框本来也不需要靠压暗来突出。
        /// 所以这里显式要求走"不遮罩"那条路（见 RegionOverlayForm.MaskOverride）。
        /// </summary>
        private void FlashSelected()
        {
            OcrRegion r = Selected;
            if (r == null) return;
            FlashRegions(r.Name, false);
        }

        /// <summary>
        /// 在屏幕上显示区域。mask = false 时不加黑色遮罩（不降低屏幕亮度）。
        /// </summary>
        public void FlashRegions(string focusName, bool mask)
        {
            try
            {
                CloseOverlay();
                if (_flashTimer != null) { _flashTimer.Stop(); _flashTimer = null; }

                Commit();

                // 用户开了"持续显示"→ 别闪一下就走，直接进持久模式
                if (PersistWanted)
                {
                    EnsureOverlay(focusName);
                    return;
                }

                _overlay = new RegionOverlayForm();
                _overlay.MaskOverride = mask;
                _overlay.SetRegions(_list, focusName);
                _overlay.Show();
                _overlay.BringToFront();

                _flashTimer = new Timer();
                _flashTimer.Interval = FocusSeconds * 1000;
                _flashTimer.Tick += delegate
                {
                    _flashTimer.Stop();
                    _flashTimer = null;
                    _overlay.StartFadeOut();   // 淡出：框和遮罩一起走，不会留下黑罩子
                };
                _flashTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Err("OCR", "显示区域位置失败", ex);
            }
        }

        /// <summary>鼠标在屏幕上直接框一块，把坐标填进上面的数字框。</summary>
        private void PickRegion()
        {
            try
            {
                Commit();
                OcrRegion r = Selected;
                string hint = r == null ? "" : r.Name;

                Rectangle rc;
                using (RegionPickerForm picker = new RegionPickerForm(hint))
                {
                    // 把自己藏起来，免得挡住要框的地方
                    Hide();
                    DialogResult dr;
                    try { dr = picker.ShowDialog(); }
                    finally { Show(); }

                    if (dr != DialogResult.OK) return;
                    rc = picker.SelectedRect;
                }

                if (rc.Width <= 0 || rc.Height <= 0) return;

                _loading = true;
                try
                {
                    // 框出来的是屏幕绝对坐标，所以取消"从底边算起"
                    _chkBottom.Checked = false;
                    _numX.Value = Clamp(rc.X, _numX);
                    _numY.Value = Clamp(rc.Y, _numY);
                    _numW.Value = Clamp(rc.Width, _numW);
                    _numH.Value = Clamp(rc.Height, _numH);
                }
                finally { _loading = false; }

                OnEdited();     // 提交 + 刷新列表文字 + 状态
                Logger.On("OCR", "框选了区域「" + (r == null ? "" : r.Name) + "」：" + rc);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "框选失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Preview()
        {
            Commit();
            OcrRegion r = Selected;
            if (r == null) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                int bright;
                string info;
                string text = OcrAlertWatcher.PreviewRegion(_cfg, r, out bright, out info);

                string body = text == null
                    ? info
                    : info + "\r\n\r\n识别到的文字：\r\n"
                        + (text.Trim().Length == 0 ? "（一片空白）" : text.Trim());

                _lblStatus.Text = r.Describe();
                MessageBox.Show(this, body, "预览识别 · " + r.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "预览失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnOk()
        {
            Commit();

            bool hasPoll = false;
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].Poll && _list[i].Enabled) hasPoll = true;
                if (string.IsNullOrEmpty(_list[i].Name)) _list[i].Name = "区域 " + (i + 1);
            }

            if (!hasPoll)
            {
                if (MessageBox.Show(this,
                        "现在没有任何“定时轮询”的区域，播报也不会自动识别了。\r\n确定这样保存吗？",
                        "SCPSLAlert", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// 窗口一关就把屏幕上的框收掉 —— "持续显示"的承诺到此为止
        /// （用户要求：直到关闭或取消选中）。
        /// 用 OnFormClosed 而不是 OnFormClosing：保证窗口真的已经走了，
        /// 不会留下一个关不掉的残留遮罩。
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_flashTimer != null) { _flashTimer.Stop(); _flashTimer.Dispose(); _flashTimer = null; }
            CloseOverlay();
            base.OnFormClosed(e);
        }

        #endregion
    }
}
