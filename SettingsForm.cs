using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 图形化设置窗口。
    ///
    /// 排版原则：整窗用 TableLayoutPanel 分区（页头 / 主体 / 页脚），
    /// 每个设置项都走 UiKit 的三列网格（标签 | 控件 | 单位），
    /// 所以文字和滑块永远不会重叠，窗口拉大拉小也不会错位。
    /// </summary>
    internal class SettingsForm : Form
    {
        private const int NavWidth = 156;
        private const int HeaderHeight = 66;
        private const int FooterHeight = 68;

        private readonly Config _cfg;
        private readonly AlertManager _mgr;
        private readonly Action _adjustAction;
        private readonly Action _exitAction;
        private readonly Action _launchGameAction;
        private readonly Action _saveAction;

        private SideNav _nav;
        private Panel _content;
        private Panel[] _pages;
        private Label _lblStatus;
        private Label _lblSaved;
        private UiSettingsPanel _uiSettings;
        private Timer _pageAnim;

        public Action RestartApplication;

        // ---- 常规 ----
        private CheckBox[] _chkEnabled;
        private NumericUpDown _numWarhead;
        private NumericUpDown _numDecon;
        private NumericUpDown _numSupport;
        private NumericUpDown _numDisplay;
        private CheckBox _chkPersistent;
        private ComboBox _cboAnimQuality;      // 动画效果档位（关/低/中/高/极高）
        private CheckBox _chkSound;
        private ComboBox _cboAudioDefault;
        private Button _btnAudioTest, _btnAudioOpen, _btnAudioReload;
        private Button _btnAudioStop, _btnAudioPlay, _btnAudioSetDef;
        private Button _btnAudioEdit, _btnAudioDelSeg;
        private Button _btnAudioExport;
        private NumericUpDown _numAudioVolume;
        private Label _lblAudioDir;
        private ListBox _lstAudio;
        private ListBox _lstTriggers;
        private Button _btnTrigToggle;

        /// <summary>列表每一行对应的"键"（文件名或 @片段名）—— 显示文字和键不是一回事。</summary>
        private readonly List<string> _audioKeys = new List<string>();
        private CheckedListBox _clAudioAlerts;      // 这段音频分配给哪些提示
        private Label _lblAudioAssign;
        private Button _btnAudioAll, _btnAudioNone;
        private bool _loadingAssign;                // 正在按配置刷勾选（别当成用户操作）
        private string _audioAssignFile = "";       // 当前在"文件列表"里选中的音频
        private NumericUpDown _numOffsetX;
        private NumericUpDown _numOffsetY;
        private NumericUpDown _numDeconAfter;
        private NumericUpDown _numSupportAfter;
        private CheckBox _chkAutoCalibrate;
        private CheckBox _chkStartupShow;
        private Label _lblHotkeyStatus;
        private NumericUpDown _numRoundDelay;
        private TextBox _txtRoundKeys;
        private CheckBox _chkRoundOnRole;

        // ---- 「时间」页 ----
        private CheckedListBox _lstTimeCands;   // 扫出来的日志候选
        private Label _lblTimeScan;
        private LogTimeScanner.Report _timeReport = new LogTimeScanner.Report();

        /// <summary>列表框每一行对应什么（间隔 / 单个时刻），勾选后按类型处理。</summary>
        private class TimeRow
        {
            public LogTimeScanner.Interval Interval;
            public LogTimeScanner.Moment Moment;
        }

        private readonly List<TimeRow> _timeRows = new List<TimeRow>();
        private NumericUpDown _numWarheadRestart; // 核弹取消重启秒数（Shift+F8）
        private NumericUpDown _numRoundOffset;  // 回合开始补偿（RoundStartOffsetSeconds）
        private NumericUpDown _numWaveOffset;   // 刷新波偏移（WaveOffsetSeconds）
        private NumericUpDown _numWaveInterval; // 大波基准间隔（WaveIntervalSeconds）
        private NumericUpDown _numWavePerPlayer;// 每人加时（WavePerPlayerSeconds）
        private NumericUpDown _numMiniDelay;    // 小波最早延迟（MiniWaveDelaySeconds）
        private NumericUpDown _numMiniWindow;   // 小波判定窗口（MiniWaveWindowSeconds）
        private NumericUpDown _numGenReady;     // 发电机全部就绪提示（GenAllReadySeconds）
        private TextBox _txtDeconMap;           // 净化播报时间对照表（AnnounceMap）
        private NumericUpDown _numDeadManSeconds;     // 终焉（亡者协议）倒计时总长
        private NumericUpDown _numDeadManMiniGap;     // 小波刷新后多久到终焉（可校准）
        private NumericUpDown _numDeadManRemain;      // 按"此刻还剩多少秒"校准终焉倒计时
        private Label _lblDeadManCal;                 // 校准结果

        // ---- 屏幕 OCR 播报识别 ----
        private CheckBox _chkOcr;
        private CheckBox _chkOcrOnlyInServer;   // 进入服务器后才开始扫描
        private CheckBox _chkOcrToast;
        private CheckBox _chkHideFromCapture;
        private CheckBox _chkRespawnStrict;
        private CheckBox _chkMiniPanel;         // 战术数据小面板开关
        private ComboBox _cboMiniBox4;          // 小面板第 4 框显示什么
        private Label _lblScanResult;

        /// <summary>点"立即扫描一次"时调用（Program 接到 OCR 模块）。</summary>
        public Action OcrScanNow;

        /// <summary>弹出"扫描全部"的完整结果窗口。</summary>
        public Action OcrShowReport;

        /// <summary>取完整扫描报告（多行）。</summary>
        public Func<string> OcrScanReport;

        /// <summary>取最近一次扫描的结果梗概。</summary>
        public Func<string> OcrScanSummary;
        private ComboBox _cboOcrStrength;
        private ComboBox _cboOcrModel;
        private NumericUpDown _numOcrInterval;
        private ComboBox _cboOcrLang;
        private Label _lblOcrRegionInfo;
        private Label _lblOcrStatus;

        // ---- 程序运行日志 ----
        private CheckBox _chkLog;

        // ---- 外观 ----
        private ComboBox _cboAlert;
        private StyleEditor _editor;
        private AlertPreview _preview;
        private AlertVisual _previewVisual;

        // ---- 快捷键 ----
        private HotkeyBox _txtHotkeyPanel;
        private HotkeyBox _txtHotkeyAdjust;
        private HotkeyBox _txtHotkeyToggleAll;
        private HotkeyBox _txtHotkeySettings;
        private HotkeyBox _txtHotkeyHoldShowAll;
        private HotkeyBox _txtHotkeyCalibrate;
        private HotkeyBox _txtHotkeyWarhead;
        private HotkeyBox _txtHotkeyDecon;
        private HotkeyBox _txtHotkeyTactical;
        private HotkeyBox _txtHotkeyBigWave;
        private HotkeyBox _txtHotkeyClockPause;
        private HotkeyBox _txtHotkeyScanNow;
        private HotkeyBox _txtHotkeyStopAll;
        private HotkeyBox _txtHotkeyStopAudio;

        // ---- 日志 ----
        private TextBox _txtLogDir;
        private CheckBox _chkGameBrightness;
        private CheckBox _chkBrightnessMotion;
        private CheckBox _chkBrightnessHold;
        private CheckBox _chkBrightnessForceGlobal;
        private NumericUpDown _numBrightnessInterval;
        private NumericUpDown _numBrightnessHoldGamma;
        private NumericUpDown _numBrightnessHoldSaturation;
        private Label _lblBrightnessProfile;
        private CheckBox _chkCrosshair, _chkCrosshairOutline, _chkCrosshairDot;
        private ComboBox _cboCrosshairShape;
        private NumericUpDown _numCrosshairSize, _numCrosshairThick, _numCrosshairGap, _numCrosshairOpacity;
        private NumericUpDown _numCrosshairX, _numCrosshairY, _numRingMs, _numRingRadius, _numExpandMs, _numExpandAmount;
        private TextBox _txtRingKey, _txtExpandKey;
        private Button _btnCrosshairColor, _btnCrosshairOutlineColor;

        /// <summary>打开游戏亮度采样与曲线校准窗口。</summary>
        public Action<bool> OpenGameBrightnessAssistant;

        private readonly Timer _saveTimer;
        private readonly Timer _statusTimer;
        private readonly Timer _savedTimer;
        private bool _loading;
        private DateTime _switchLockUntil;

        /// <summary>
        /// 界面是否已经从配置里读过一次（ReloadUi 跑过）。
        /// 在它变 true 之前，<see cref="Apply"/> 一律不动配置 ——
        /// 否则"构造界面时某个控件触发了 Apply"会把所有提示关掉（见 Apply 里的注释）。
        /// </summary>
        private bool _uiLoaded;
        private bool _reallyClose;
        public Action SwitchToModernUi;
        public Action SwitchToLegacyUi;

        protected SideNav NavControl { get { return _nav; } }
        protected Panel ContentPanel { get { return _content; } }
        protected Panel[] PagePanels { get { return _pages; } }
        protected Panel TipBarPanel { get { return _tipBar; } }
        protected Label SavedLabel { get { return _lblSaved; } }
        protected void RegisterSavedLabel(Label label) { _lblSaved = label; }
        protected void RunAdjustAction() { if (_adjustAction != null) _adjustAction(); }
        protected void RunExitAction() { if (_exitAction != null) _exitAction(); }
        protected void RunLaunchGameAction() { if (_launchGameAction != null) _launchGameAction(); }
        protected void RunSwitchToModernUi() { if (SwitchToModernUi != null) SwitchToModernUi(); }
        protected void RunSwitchToLegacyUi() { if (SwitchToLegacyUi != null) SwitchToLegacyUi(); }
        protected void PreviewAllAlerts() { if (_mgr != null) _mgr.PreviewAll(); }
        protected virtual int NavWidthValue { get { return NavWidth; } }
        protected virtual int HeaderHeightValue { get { return HeaderHeight; } }
        protected virtual int FooterHeightValue { get { return FooterHeight; } }
        protected virtual bool HorizontalNavigationValue { get { return false; } }
        protected virtual string[] NavigationLabelsValue { get { return null; } }
        protected virtual string[] NavigationIconsValue { get { return null; } }

        public SettingsForm(Config cfg, AlertManager mgr,
                            Action adjustAction, Action exitAction, Action saveAction,
                            Action launchGameAction)
        {
            _cfg = cfg;
            _mgr = mgr;
            _adjustAction = adjustAction;
            _exitAction = exitAction;
            _saveAction = saveAction;
            _launchGameAction = launchGameAction;
            ModernTheme.AnimationsEnabled = cfg.UiAnimationsEnabled;

            Text = "SCPSLAlert 设置";
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            FormBorderStyle = FormBorderStyle.Sizable;   // 可缩放，控件跟着自适应
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 720);
            MinimumSize = new Size(820, 620);
            // 高分屏（125% / 150% 缩放）下也按系统 DPI 自动缩放文字和按钮，
            // 拉大拉小窗口时 TableLayoutPanel 会自动重排，页面装不下会出滚动条
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            // 右上角保留"最小化"按钮（收进任务栏），它和 × 的"最小化到托盘"互不影响
            MinimizeBox = true;
            MaximizeBox = true;

            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            BuildRoot();

            _nav.SelectedIndex = 0;
            SetupDropImport();      // 「音频」页支持把文件直接拖进来导入

            _saveTimer = new Timer();
            _saveTimer.Interval = 700;
            _saveTimer.Tick += OnSaveTimer;

            _statusTimer = new Timer();
            _statusTimer.Interval = 1000;
            _statusTimer.Tick += OnStatusTimer;

            _savedTimer = new Timer();
            _savedTimer.Interval = 2000;
            _savedTimer.Tick += delegate
            {
                _savedTimer.Stop();
                if (_lblSaved != null)
                {
                    _lblSaved.Visible = false;
                    _lblSaved.Text = "";
                }
            };

            Load += delegate
            {
                ReloadUi();
                _statusTimer.Start();
            };
        }

        #region 整体骨架

        private void BuildRoot()
        {
            // 整窗只有这一个子控件：四行网格 = 页头 / 主体 / 底部说明栏 / 页脚
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.BackColor = UiKit.PageBg;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeightValue));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            // 底部说明栏：默认收起（高 0），鼠标停到带说明的控件上才展开
            _tipRowStyle = new RowStyle(SizeType.Absolute, 0);
            root.RowStyles.Add(_tipRowStyle);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeightValue));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildTipBar(), 0, 2);
            root.Controls.Add(BuildFooter(), 0, 3);

            Controls.Add(root);

            UiKit.TipShown += OnTipShown;
            UiKit.TipHidden += OnTipHideRequest;
            FormClosed += delegate
            {
                UiKit.TipShown -= OnTipShown;
                UiKit.TipHidden -= OnTipHideRequest;
                WheelRouter.TipBarOverride = null;
                _savedTimer.Stop();
                _savedTimer.Dispose();
            };
        }

        // ---- 底部说明栏 ----
        // 鼠标停到某一项上时，这里显示“简略说明 + 完整说明”，
        // 不再用 Windows 原生那个飘来飘去的黄框。
        // 完整说明太长时：简略那行固定在顶上，完整说明可以用滚轮滚（右侧有滚动指示）。
        private Panel _tipBar;
        private Label _tipShort, _tipFull;
        private RowStyle _tipRowStyle;
        private readonly Timer _tipHideTimer = new Timer();
        private int _tipScroll;             // 完整说明的滚动偏移
        private int _tipMaxScroll;          // 最大可滚距离（0 = 不用滚）
        private bool _tipShown;             // 说明栏是不是正显示（不能看 Visible：父窗口没显示时它是 false）
        private const int TipBarMin = 56;   // 至少这么高（只有简略说明时）
        private const int TipBarMax = 200;  // 最高这么高，再长就靠滚轮滚
        private const int TipPadX = 16;
        private const int TipPadY = 8;
        private const int TipShortH = 24;

        private Control BuildTipBar()
        {
            _tipBar = new Panel();
            _tipBar.Dock = DockStyle.Fill;
            _tipBar.Margin = new Padding(0);
            _tipBar.BackColor = Color.FromArgb(226, 235, 248);   // 浅蓝，和页脚区分开
            // 不用 Panel 自带的 AutoScroll：它和 TableLayoutPanel 一起用时
            // 滚动位置会被布局重置（实测滚不动），所以这里自己算偏移。
            _tipBar.AutoScroll = false;
            _tipBar.Visible = false;
            _tipBar.Paint += delegate(object s, PaintEventArgs e)
            {
                // 顶部一条分隔线，让说明栏和上面的页面分开
                using (Pen p = new Pen(UiKit.BorderColor, 1f))
                    e.Graphics.DrawLine(p, 0, 0, _tipBar.Width, 0);

                // 完整说明比可见区域长时，右侧画一条滚动指示（让人知道“这里能滚”）
                if (_tipMaxScroll > 0)
                {
                    int trackY = TipPadY + TipShortH;
                    int trackH = Math.Max(20, _tipBar.ClientSize.Height - trackY - 6);
                    int viewH = Math.Max(10, _tipBar.ClientSize.Height - trackY - TipPadY);
                    int thumbH = Math.Max(24, trackH * viewH / (viewH + _tipMaxScroll));
                    int thumbY = trackY + (trackH - thumbH) * _tipScroll / Math.Max(1, _tipMaxScroll);

                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(36, 110, 130, 160)))
                        e.Graphics.FillRectangle(sb, _tipBar.Width - 8, trackY, 4, trackH);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, 80, 115, 165)))
                        e.Graphics.FillRectangle(sb, _tipBar.Width - 8, thumbY, 4, thumbH);
                }
            };

            // 简略说明：粗体蓝色，一眼知道这一项是干什么的（固定在顶部，不跟着滚）
            _tipShort = new Label();
            _tipShort.Font = UiKit.FontBold;
            _tipShort.ForeColor = UiKit.Accent;
            _tipShort.AutoSize = false;
            _tipShort.Height = TipShortH;
            _tipShort.TextAlign = ContentAlignment.MiddleLeft;
            _tipShort.BackColor = Color.Transparent;

            // 完整说明：正文号字、深色（比简略说明大一点、颜色也不一样，一眼分得开）
            _tipFull = new Label();
            _tipFull.Font = UiKit.Font;
            _tipFull.ForeColor = Color.FromArgb(48, 56, 70);
            _tipFull.AutoSize = false;
            _tipFull.BackColor = Color.Transparent;

            _tipBar.Controls.Add(_tipFull);
            _tipBar.Controls.Add(_tipShort);

            _tipBar.MouseWheel += delegate(object s, MouseEventArgs e) { ScrollTipBar(e.Delta); };
            _tipShort.MouseWheel += delegate(object s, MouseEventArgs e) { ScrollTipBar(e.Delta); };
            _tipFull.MouseWheel += delegate(object s, MouseEventArgs e) { ScrollTipBar(e.Delta); };
            _tipBar.Resize += delegate
            {
                // 【踩过的坑】这里原来只看 _tipBar.Visible：收起说明栏时
                // 「行高先改 0 → 触发重新排版 → 面板 Resize」这一拍里 Visible 还是 true，
                // 于是 LayoutTipBar() 又把行高改回内容高度 —— 结果文字没了、框还留在窗口上。
                // 现在必须 _tipShown 为真（真的在显示说明）才允许重排。
                if (_tipShown && _tipBar.Visible) LayoutTipBar();
            };

            _tipHideTimer.Interval = 160;    // 稍稍延迟再收起，避免在控件之间移动时闪
            _tipHideTimer.Tick += delegate
            {
                _tipHideTimer.Stop();
                CollapseTipBar();
            };

            // 说明栏显示时，滚轮优先用来滚说明内容（见 WheelRouter）
            WheelRouter.TipBarOverride = delegate(Control under)
            {
                if (_tipBar == null || !_tipShown) return null;
                if (UiKit.LastTipSource == null) return null;
                return _tipBar;
            };

            return _tipBar;
        }

        private void OnTipShown(string shortText, string fullText)
        {
            if (_tipBar == null) return;
            _tipHideTimer.Stop();
            _tipShort.Text = shortText == null ? "" : shortText;
            _tipShort.Tag = shortText;                       // 原文留着（加提示后缀时不叠加）
            _tipFull.Text = string.IsNullOrEmpty(fullText) ? "" : fullText;
            _tipScroll = 0;
            _tipShown = true;
            _tipBar.Visible = true;
            LayoutTipBar();
        }

        /// <summary>
        /// 按完整说明的实际高度决定说明栏多高（最高 TipBarMax，超过就靠滚轮滚）。
        /// </summary>
        private void LayoutTipBar()
        {
            // 只在"真的要显示说明"时排版；收起过程中触发的 Resize 不许再把行高撑回去
            if (!_tipShown) return;
            int avail = Math.Max(160, _tipBar.ClientSize.Width - TipPadX * 2 - 14);
            Size need = TextRenderer.MeasureText(_tipFull.Text, _tipFull.Font,
                new Size(avail, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            _tipFull.Height = Math.Max(18, need.Height + 6);
            _tipFull.Width = avail;
            _tipShort.Width = avail;

            int content = TipShortH + _tipFull.Height + TipPadY * 2;
            float h = Math.Min(Math.Max(content, TipBarMin), TipBarMax);
            _tipRowStyle.Height = h;

            int viewH = Math.Max(10, (int)h - TipShortH - TipPadY - 6);
            _tipMaxScroll = Math.Max(0, _tipFull.Height - viewH);
            if (_tipScroll > _tipMaxScroll) _tipScroll = _tipMaxScroll;

            ApplyTipScroll();

            // 滚得动就明说一句，免得用户以为内容就这么多
            string brief = _tipShort.Tag as string ?? _tipShort.Text;
            _tipShort.Text = _tipMaxScroll > 0 ? brief + "　（滚轮可滚动完整说明 ↓）" : brief;
        }

        /// <summary>把简略说明钉在顶上，完整说明按当前偏移摆放。</summary>
        private void ApplyTipScroll()
        {
            _tipShort.Location = new Point(TipPadX, TipPadY);
            _tipFull.Location = new Point(TipPadX, TipPadY + TipShortH - _tipScroll);
        }

        /// <summary>滚轮滚说明内容（滚到顶/底就停住，不影响页面）。</summary>
        private void ScrollTipBar(int delta)
        {
            if (_tipBar == null || !_tipShown || _tipMaxScroll <= 0) return;
            _tipScroll -= Math.Sign(delta) * 48;
            if (_tipScroll < 0) _tipScroll = 0;
            if (_tipScroll > _tipMaxScroll) _tipScroll = _tipMaxScroll;
            ApplyTipScroll();
            _tipBar.Invalidate();
        }

        private void OnTipHideRequest()
        {
            if (_tipBar == null) return;
            _tipHideTimer.Stop();
            _tipHideTimer.Start();
        }

        private void CollapseTipBar()
        {
            if (_tipBar == null) return;
            _tipShown = false;
            // 先藏面板，再把行高压 0，最后强制排版一次 ——
            // 少了最后这一步，TableLayoutPanel 有时会留着上一次的高度，
            // 看起来就是"文字没了、但那个框还在"。
            _tipBar.Visible = false;
            _tipRowStyle.Height = 0;
            if (_tipBar.Parent != null) _tipBar.Parent.PerformLayout();
        }

        protected virtual Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            header.BackColor = UiKit.HeaderBg;

            Label title = new Label();
            title.Text = "SCPSLAlert 设置";
            title.Font = UiKit.FontTitle;
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(22, 12);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "每种提示都能单独调字体、颜色、背景、壁纸、玻璃质感和动画";
            sub.Font = UiKit.FontSmall;
            sub.ForeColor = Color.FromArgb(150, 158, 172);
            sub.AutoSize = true;
            sub.Location = new Point(24, 39);
            header.Controls.Add(sub);

            return header;
        }

        protected virtual Control BuildBody()
        {
            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0);
            body.BackColor = UiKit.PageBg;
            if (HorizontalNavigationValue)
            {
                body.ColumnCount = 1;
                body.RowCount = 2;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            }
            else
            {
                body.ColumnCount = 2;
                body.RowCount = 1;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavWidthValue));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            }

            _nav = new SideNav();
            _nav.Dock = DockStyle.Fill;
            _nav.Margin = new Padding(0);
            _nav.BackColor = UiKit.NavBg;
            _nav.Font = new Font("微软雅黑", 10f, FontStyle.Regular, GraphicsUnit.Point);
            _nav.Horizontal = HorizontalNavigationValue;
            _nav.ItemHeight = HorizontalNavigationValue ? 60 : 42;
            string[] navLabels = NavigationLabelsValue;
            if (navLabels == null)
                navLabels = new string[] { "开始使用", "常规", "外观", "音频", "时间", "快捷键", "日志", "辅助" };
            _nav.Items.AddRange(navLabels);
            string[] navIcons = NavigationIconsValue;
            if (navIcons != null) _nav.Icons.AddRange(navIcons);
            _nav.SelectedIndexChanged += OnNavChanged;
            if (HorizontalNavigationValue) body.Controls.Add(_nav, 0, 0);
            else body.Controls.Add(_nav, 0, 0);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(0);
            _content.BackColor = UiKit.PageBg;
            if (HorizontalNavigationValue) body.Controls.Add(_content, 0, 1);
            else body.Controls.Add(_content, 1, 0);

            BuildPages();
            return body;
        }

        protected virtual Control BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.BackColor = UiKit.FooterBg;

            // 左边一组常用操作，右边一组收尾按钮，用两个流式容器分开，
            // 窗口变宽变窄都不会互相挤压。
            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Left;
            left.AutoSize = true;
            left.FlowDirection = FlowDirection.LeftToRight;
            left.WrapContents = false;
            left.Padding = new Padding(16, 18, 0, 0);
            left.BackColor = Color.Transparent;

            Button adjust = UiKit.Button("调整提示位置…", 132);
            adjust.Click += delegate { if (_adjustAction != null) _adjustAction(); };
            left.Controls.Add(adjust);

            Button preview = UiKit.Button("预览效果", 92);
            preview.Click += delegate { _mgr.PreviewAll(); };
            left.Controls.Add(preview);

            Button defaults = UiKit.Button("恢复默认", 92);
            defaults.Click += delegate { RestoreDefaults(); };
            left.Controls.Add(defaults);

            footer.Controls.Add(left);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.AutoSize = true;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.Padding = new Padding(0, 18, 16, 0);
            right.BackColor = Color.Transparent;

            Button close = UiKit.PrimaryButton("保存", 108);
            close.Click += delegate { SaveNow(); };
            right.Controls.Add(close);

            Button exit = UiKit.Button("退出程序", 96);
            exit.Click += delegate { if (_exitAction != null) _exitAction(); };
            right.Controls.Add(exit);

            bool isModern = _cfg != null && _cfg.UiStyle == 0;
            // 旧界面这一颗按钮按需求固定白底黑字，不跟全局主题走。
            Button modern = new Button();
            modern.Text = isModern ? "返回旧版界面" : "进入新版本";
            modern.Width = 118;
            modern.Height = 32;
            modern.FlatStyle = FlatStyle.Flat;
            modern.FlatAppearance.BorderSize = 1;
            modern.FlatAppearance.BorderColor = Color.FromArgb(208, 208, 208);
            modern.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 248, 248);
            modern.FlatAppearance.MouseDownBackColor = Color.FromArgb(237, 237, 237);
            modern.BackColor = Color.White;
            modern.ForeColor = Color.FromArgb(26, 26, 26);
            modern.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            modern.Cursor = Cursors.Hand;
            modern.UseVisualStyleBackColor = false;
            modern.Click += delegate
            {
                // 300ms 锁：快速连点不会重复切界面 / 重复保存。
                if (DateTime.UtcNow < _switchLockUntil) return;
                _switchLockUntil = DateTime.UtcNow.AddMilliseconds(300);
                if (_cfg != null && _cfg.UiStyle == 0)
                {
                    RunSwitchToLegacyUi();
                    modern.Text = "进入新版本";
                }
                else
                {
                    RunSwitchToModernUi();
                }
            };
            right.Controls.Add(modern);

            Button launchGame = UiKit.Button("启动 SCP:SL", 112);
            launchGame.Click += delegate
            {
                if (_launchGameAction != null) _launchGameAction();
            };
            right.Controls.Add(launchGame);

            _lblSaved = new Label();
            _lblSaved.AutoSize = false;
            _lblSaved.Size = new Size(58, 34);
            _lblSaved.Margin = new Padding(8, 0, 4, 0);
            _lblSaved.TextAlign = ContentAlignment.MiddleCenter;
            _lblSaved.ForeColor = Color.FromArgb(72, 190, 126);
            _lblSaved.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            _lblSaved.Visible = false;
            right.Controls.Add(_lblSaved);

            footer.Controls.Add(right);
            return footer;
        }

        private void OnNavChanged(object sender, EventArgs e)
        {
            int i = _nav.SelectedIndex;
            for (int k = 0; k < _pages.Length; k++)
                _pages[k].Visible = (k == i);

            _audioPageActive = (i == 3);      // 拖放导入只在「音频」页生效
            if (!_audioPageActive) HideDropVeil();

            // “外观”页在隐藏状态下就已经载入过配置了。下拉框这类控件在句柄
            // 真正创建时可能把选中项重置成第一项，所以等页面显示出来之后，
            // 必须再按【配置里的值】重新载入一遍 —— 不能从控件里读，否则会把
            // 已经被重置的错值当成用户的设置确认下来。
            if (i == 2 && !_loading && _editor != null)
            {
                LoadSelectedAlert();
                _editor.PerformLayout();
                BeginInvoke((MethodInvoker)delegate { LoadSelectedAlert(); });
            }

            AnimatePageIn();
        }

        private void AnimatePageIn()
        {
            if (_content == null) return;
            if (_pageAnim != null) { _pageAnim.Stop(); _pageAnim.Dispose(); _pageAnim = null; }
            if (this is ModernSettingsForm)
            {
                _content.Padding = new Padding(0);
                return;
            }
            if (!_cfg.UiAnimationsEnabled)
            {
                _content.Padding = new Padding(0);
                return;
            }
            int duration = Math.Max(100, Math.Min(600, _cfg.UiAnimationDurationMs));
            int start = Environment.TickCount;
            _pageAnim = new Timer();
            _pageAnim.Interval = 15;
            _pageAnim.Tick += delegate
            {
                double t = (Environment.TickCount - start) / (double)duration;
                if (t >= 1)
                {
                    _pageAnim.Stop();
                    _pageAnim.Dispose();
                    _pageAnim = null;
                    _content.Padding = new Padding(0);
                    return;
                }
                int offset = (int)Math.Round(16 * (1 - UiAnim.EaseOutCubic(t)));
                _content.Padding = new Padding(offset, 0, 0, 0);
            };
            _pageAnim.Start();
        }

        // ============================================================
        // 拖放导入音频（操作者要求）
        //
        // 处在「音频」页时，把文件从资源管理器拖到窗口上：
        //   · 整个窗口盖一层半透明遮罩（看起来就是"糊了"），中间一行清晰的大字
        //     「拖到此处松手导入」；
        //   · 松手 → 把文件复制到「音频」文件夹（同名不同大小自动加 (2)），
        //     然后重扫列表。
        // ============================================================

        private bool _audioPageActive;
        private Panel _dropVeil;

        private void SetupDropImport()
        {
            AllowDrop = true;
            DragEnter += OnFormDragEnter;
            DragOver += OnFormDragOver;
            DragLeave += delegate { HideDropVeil(); };
            DragDrop += OnFormDrop;

            // 遮罩本身也要接受拖放，否则鼠标移到遮罩上就收不到 Drop 了
            _dropVeil = new Panel();
            _dropVeil.Dock = DockStyle.Fill;
            _dropVeil.Visible = false;
            _dropVeil.AllowDrop = true;
            _dropVeil.BackColor = Color.FromArgb(236, 246, 247, 250);
            _dropVeil.Paint += OnDropVeilPaint;
            _dropVeil.DragEnter += OnFormDragEnter;
            _dropVeil.DragOver += OnFormDragOver;
            _dropVeil.DragLeave += delegate { HideDropVeil(); };
            _dropVeil.DragDrop += OnFormDrop;
            Controls.Add(_dropVeil);
            _dropVeil.BringToFront();
        }

        private bool DropDataOk(DragEventArgs e)
        {
            if (!_audioPageActive) return false;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
            string[] files = null;
            try { files = (string[])e.Data.GetData(DataFormats.FileDrop); } catch { }
            return files != null && files.Length > 0;
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            bool ok = DropDataOk(e);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) ShowDropVeil();
        }

        private void OnFormDragOver(object sender, DragEventArgs e)
        {
            bool ok = DropDataOk(e);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void ShowDropVeil()
        {
            if (_dropVeil == null || _dropVeil.Visible) return;
            _dropVeil.Visible = true;
            _dropVeil.BringToFront();
            _dropVeil.Invalidate();
        }

        private void HideDropVeil()
        {
            if (_dropVeil == null || !_dropVeil.Visible) return;
            _dropVeil.Visible = false;
        }

        private void OnDropVeilPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle all = _dropVeil.ClientRectangle;

            // 一层面纱：看起来像"整体变模糊了"
            using (SolidBrush b = new SolidBrush(Color.FromArgb(214, 250, 251, 253)))
                g.FillRectangle(b, all);

            // 中间一个虚线大框
            Rectangle box = new Rectangle(all.X + 40, all.Y + 40,
                Math.Max(80, all.Width - 80), Math.Max(80, all.Height - 80));
            using (Pen p = new Pen(Color.FromArgb(200, 90, 140, 250), 3f))
            {
                p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawRectangle(p, box);
            }

            using (Font big = new Font("微软雅黑", 22f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font small = new Font("微软雅黑", 10f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush headline = new SolidBrush(Color.FromArgb(255, 40, 90, 200)))
            using (Brush sub = new SolidBrush(Color.FromArgb(255, 90, 100, 120)))
            {
                string t1 = "拖到此处松手导入";
                string t2 = "会把文件复制到「音频」文件夹（原文件不动），支持 wav / mp3 / wma / m4a / aac / mid";
                SizeF s1 = g.MeasureString(t1, big);
                SizeF s2 = g.MeasureString(t2, small);
                g.DrawString(t1, big, headline, all.X + (all.Width - s1.Width) / 2f, all.Y + all.Height / 2f - s1.Height);
                g.DrawString(t2, small, sub, all.X + (all.Width - s2.Width) / 2f, all.Y + all.Height / 2f + 16);
            }
        }

        private void OnFormDrop(object sender, DragEventArgs e)
        {
            HideDropVeil();
            if (!DropDataOk(e)) return;

            string[] files = null;
            try { files = (string[])e.Data.GetData(DataFormats.FileDrop); } catch { }
            if (files == null || files.Length == 0) return;

            string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show("定位不到「音频」文件夹。", "导入音频",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int ok = 0, skipped = 0;
            System.Text.StringBuilder failed = new System.Text.StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string src = files[i];
                    if (string.IsNullOrEmpty(src) || !System.IO.File.Exists(src)) { skipped++; continue; }

                    string ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
                    if (!AudioLibrary.IsSupportedExt(ext)) { skipped++; continue; }

                    string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(src));
                    dest = UniquePath(dest, src);
                    System.IO.File.Copy(src, dest, false);
                    ok++;
                    Logger.On("音频", "拖放导入：" + System.IO.Path.GetFileName(src) + " → " + dest);
                }
                catch (Exception ex)
                {
                    failed.Append(System.IO.Path.GetFileName(files[i])).Append("（").Append(ex.Message).Append("）\n");
                }
            }

            RefreshAudioFromDisk();

            string msg = "已导入 " + ok + " 个音频到「音频」文件夹";
            if (skipped > 0) msg += "；跳过 " + skipped + " 个（不支持的类型 / 不是文件）";
            if (failed.Length > 0) msg += "；失败：\n" + failed;
            BalloonStatus(msg.Replace("\n", " "));
            if (failed.Length > 0)
                MessageBox.Show(msg, "导入音频", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>目标文件已存在时：一样大小就直接用现有的，否则加 (2) (3)…</summary>
        private static string UniquePath(string dest, string src)
        {
            try
            {
                if (!System.IO.File.Exists(dest)) return dest;

                System.IO.FileInfo a = new System.IO.FileInfo(src);
                System.IO.FileInfo b = new System.IO.FileInfo(dest);
                if (a.Length == b.Length) return dest;     // 同一个文件再拖一次：不重复拷

                string dir = System.IO.Path.GetDirectoryName(dest);
                string name = System.IO.Path.GetFileNameWithoutExtension(dest);
                string ext = System.IO.Path.GetExtension(dest);
                for (int n = 2; n < 999; n++)
                {
                    string cand = System.IO.Path.Combine(dir, name + " (" + n + ")" + ext);
                    if (!System.IO.File.Exists(cand)) return cand;
                }
                return System.IO.Path.Combine(dir, name + "_" + DateTime.Now.ToString("HHmmss") + ext);
            }
            catch { return dest; }
        }

        /// <summary>重新读一遍音频文件夹 + 片段，把「音频」页刷成最新的（托盘菜单刷新也用它）。</summary>
        public void RefreshAudioFromDisk()
        {
            try
            {
                AudioLibrary.ResolveDir(_cfg.AudioDir);
                AudioLibrary.ApplyConfig(_cfg);
                ReloadAudioDefaults(null);
                ReloadAudioPageList();
                RefreshAudioAssign();
                RefreshTriggerList();
            }
            catch (Exception ex) { Logger.Err("音频", "刷新音频界面失败", ex); }
        }


        #endregion

        #region 页面

        private const string OcrLangCustom = "自定义语言…";
        private bool _ocrLangBusy;

        private void OnOcrLangChanged(object sender, EventArgs e)
        {
            if (_loading || _ocrLangBusy) return;
            if ((_cboOcrLang.SelectedItem as string) == OcrLangCustom)
            {
                string value = TextPromptForm.Ask(this, "识别语言",
                    "输入 tessdata 里的语言文件名，多个用 + 连接（例如 eng+chi_sim）", _cfg.OcrLang);
                if (string.IsNullOrEmpty(value))
                {
                    SelectOcrLang(_cfg.OcrLang);
                    return;
                }
                SelectOcrLang(value.Trim());
            }
            Apply();
        }

        private void SelectOcrLang(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "eng+chi_sim";
            int index = _cboOcrLang.Items.IndexOf(lang);
            if (index < 0)
            {
                _cboOcrLang.Items.Insert(0, lang);
                index = 0;
            }
            _ocrLangBusy = true;
            try { _cboOcrLang.SelectedIndex = index; }
            finally { _ocrLangBusy = false; }
        }

        private void BuildPages()
        {            _pages = new Panel[8];
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = UiKit.Page();
                _pages[i].Visible = false;
                _content.Controls.Add(_pages[i]);
            }

            BuildStartPage(_pages[0]);
            BuildGeneralPage(_pages[1]);
            BuildLookPage(_pages[2]);
            BuildAudioPage(_pages[3]);
            BuildTimePage(_pages[4]);
            BuildHotkeyPage(_pages[5]);
            BuildLogPage(_pages[6]);
            BuildAuxPage(_pages[7]);
        }

        private void BuildAuxPage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();
            page.Controls.Add(stack);
            TableLayoutPanel grid;
            GroupBox group = UiKit.Group("游戏亮度模式", 4, out grid);
            _chkGameBrightness = UiKit.NewCheckBox();
            _chkGameBrightness.Text = "游戏在前台时启用伽马调整";
            _chkGameBrightness.AutoSize = true;
            _chkGameBrightness.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "总开关", _chkGameBrightness, "默认关闭", false);
            _numBrightnessInterval = UiKit.NewNumeric();
            _numBrightnessInterval.Minimum = 500;
            _numBrightnessInterval.Maximum = 15000;
            _numBrightnessInterval.Increment = 500;
            _numBrightnessInterval.Width = 130;
            _numBrightnessInterval.Value = 2000;
            _numBrightnessInterval.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "检测间隔", _numBrightnessInterval, "毫秒", false);
            _chkBrightnessHold = UiKit.NewCheckBox();
            _chkBrightnessHold.Text = "保持固定伽马，不跟随画面亮度变化";
            _chkBrightnessHold.AutoSize = true;
            _chkBrightnessHold.CheckedChanged += delegate
            {
                if (_numBrightnessHoldGamma != null) _numBrightnessHoldGamma.Enabled = _chkBrightnessHold.Checked;
                if (_numBrightnessHoldSaturation != null) _numBrightnessHoldSaturation.Enabled = _chkBrightnessHold.Checked;
                if (!_loading) Apply();
            };
            UiKit.Row(grid, "固定模式", _chkBrightnessHold, "开启后使用下方固定值", false);
            _numBrightnessHoldGamma = UiKit.NewNumeric();
            _numBrightnessHoldGamma.DecimalPlaces = 2;
            _numBrightnessHoldGamma.Minimum = 0.20m;
            _numBrightnessHoldGamma.Maximum = 1.60m;
            _numBrightnessHoldGamma.Increment = 0.05m;
            _numBrightnessHoldGamma.Width = 130;
            _numBrightnessHoldGamma.Value = 0.80m;
            _numBrightnessHoldGamma.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "固定伽马", _numBrightnessHoldGamma, "指数（小于 1 提亮）", false);
            _numBrightnessHoldSaturation = UiKit.NewNumeric();
            _numBrightnessHoldSaturation.DecimalPlaces = 2;
            _numBrightnessHoldSaturation.Minimum = 0.20m;
            _numBrightnessHoldSaturation.Maximum = 1.60m;
            _numBrightnessHoldSaturation.Increment = 0.05m;
            _numBrightnessHoldSaturation.Width = 130;
            _numBrightnessHoldSaturation.Value = 1.07m;
            _numBrightnessHoldSaturation.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "固定饱和度", _numBrightnessHoldSaturation, "1.00 为原始色彩", false);
            _chkBrightnessForceGlobal = UiKit.NewCheckBox();
            _chkBrightnessForceGlobal.Text = "强制覆盖所有使用场景（切出游戏后仍保持伽马）";
            _chkBrightnessForceGlobal.AutoSize = true;
            _chkBrightnessForceGlobal.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "全场景覆盖", _chkBrightnessForceGlobal, "会影响该显示器上的其他窗口", false);
            UiKit.AddGroup(stack, group);

            GroupBox captureGroup = UiKit.Group("伽马样本采集", 5, out grid);
            _lblBrightnessProfile = UiKit.Label("尚未校准");
            UiKit.Row(grid, "映射曲线", _lblBrightnessProfile, "3–7 个亮度点", false);
            _chkBrightnessMotion = UiKit.NewCheckBox();
            _chkBrightnessMotion.Text = "画面变化明显时才采集高分辨率样本";
            _chkBrightnessMotion.AutoSize = true;
            _chkBrightnessMotion.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "自动采集", _chkBrightnessMotion, "在本页设置", false);
            Button calibrate = UiKit.Button("采集画面并调整…", 160);
            calibrate.Click += delegate { if (OpenGameBrightnessAssistant != null) OpenGameBrightnessAssistant(false); };
            UiKit.Row(grid, "手动采集", calibrate, "打开校准窗口", false);
            Button autoCapture = UiKit.Button("自动采集样本", 140);
            autoCapture.Click += delegate { if (OpenGameBrightnessAssistant != null) OpenGameBrightnessAssistant(true); };
            UiKit.Row(grid, "一键开始", autoCapture, "隐藏设置后自动采集", false);
            UiKit.Note(grid, "伽马与饱和度曲线共用亮度采样点；采样校准和手动曲线编辑都可调整两者，并可将样本截图作为参考。全场景覆盖时对整个虚拟桌面重新采样。饱和度通过显示器 RGB 曲线近似调整，1.00 为中性，建议小幅增加（约 1.05–1.15）以补偿提亮后的色彩变淡。");
            UiKit.AddGroup(stack, captureGroup);

            GroupBox cross = UiKit.Group("辅助准星", 12, out grid);
            _chkCrosshair = UiKit.NewCheckBox(); _chkCrosshair.Text = "游戏在前台时显示准星覆盖层"; _chkCrosshair.AutoSize = true;
            _chkCrosshair.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "总开关", _chkCrosshair, "仅游戏前台显示", false);
            _cboCrosshairShape = UiKit.NewComboBox(); _cboCrosshairShape.DropDownStyle = ComboBoxStyle.DropDownList; _cboCrosshairShape.Width = 180;
            _cboCrosshairShape.Items.AddRange(new object[] { "十字", "圆环", "中心点", "十字圆点", "方框", "斜十字", "X 形", "四角框", "横框", "尖括号", "双层方框", "菱形" });
            _cboCrosshairShape.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "样式", _cboCrosshairShape, "12 种基础样式", false);
            _numCrosshairSize = CrossNum(2, 100, 16); _numCrosshairSize.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "尺寸", _numCrosshairSize, "像素", false);
            _numCrosshairThick = CrossNum(1, 12, 2); _numCrosshairThick.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "线宽", _numCrosshairThick, "像素", false);
            _numCrosshairGap = CrossNum(0, 50, 5); _numCrosshairGap.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "中心间距", _numCrosshairGap, "像素", false);
            _numCrosshairOpacity = CrossNum(10, 100, 90); _numCrosshairOpacity.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "不透明度", _numCrosshairOpacity, "%", false);
            _btnCrosshairColor = UiKit.Button("选择颜色", 110); _btnCrosshairColor.Click += delegate { PickCrosshairColor(false); };
            UiKit.Row(grid, "准星颜色", _btnCrosshairColor, "支持透明度", false);
            _chkCrosshairOutline = UiKit.NewCheckBox(); _chkCrosshairOutline.Text = "添加描边"; _chkCrosshairOutline.AutoSize = true; _chkCrosshairOutline.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "描边", _chkCrosshairOutline, "复杂背景提高对比", false);
            _btnCrosshairOutlineColor = UiKit.Button("选择颜色", 110); _btnCrosshairOutlineColor.Click += delegate { PickCrosshairColor(true); };
            UiKit.Row(grid, "描边颜色", _btnCrosshairOutlineColor, "", false);
            _chkCrosshairDot = UiKit.NewCheckBox(); _chkCrosshairDot.Text = "显示中心点"; _chkCrosshairDot.AutoSize = true; _chkCrosshairDot.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "中心点", _chkCrosshairDot, "额外叠加", false);
            _numCrosshairX = CrossNum(-200, 200, 0); _numCrosshairX.ValueChanged += delegate { if (!_loading) Apply(); };
            _numCrosshairY = CrossNum(-200, 200, 0); _numCrosshairY.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "中心偏移 X / Y", Pair(_numCrosshairX, _numCrosshairY), "像素", false);
            _txtRingKey = CrossKeyBox("E"); _numRingMs = CrossNum(100, 5000, 1000); _numRingRadius = CrossNum(8, 150, 34);
            _txtRingKey.KeyDown += CrossKeyDown; _numRingMs.ValueChanged += delegate { if (!_loading) Apply(); }; _numRingRadius.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "缩圈动画", Pair(_txtRingKey, Pair(_numRingMs, _numRingRadius)), "按键 / 时长毫秒 / 半径", false);
            _txtExpandKey = CrossKeyBox("Space"); _numExpandMs = CrossNum(100, 5000, 1000); _numExpandAmount = CrossNum(1, 80, 14);
            _txtExpandKey.KeyDown += CrossKeyDown; _numExpandMs.ValueChanged += delegate { if (!_loading) Apply(); }; _numExpandAmount.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(grid, "扩张回弹", Pair(_txtExpandKey, Pair(_numExpandMs, _numExpandAmount)), "按键 / 时长毫秒 / 幅度", false);
            UiKit.Note(grid, "按键框获得焦点后直接按下新按键。缩圈从设定半径收至中心；扩张回弹在设定时长内先扩后缩。覆盖层不会拦截鼠标或键盘输入。可在 config.ini 的 [Crosshair] 中进一步保存和备份全部参数。");
            UiKit.AddGroup(stack, cross);
        }

        private static NumericUpDown CrossNum(int min, int max, int value)
        { NumericUpDown n = UiKit.NewNumeric(); n.Minimum = min; n.Maximum = max; n.Value = value; n.Width = 95; return n; }
        private static TextBox CrossKeyBox(string key)
        { TextBox t = new TextBox(); t.Text = key; t.ReadOnly = true; t.Width = 100; t.TabStop = true; t.Enter += delegate { KeyboardHook.Suspended = true; }; t.Leave += delegate { KeyboardHook.Suspended = false; }; return t; }
        private static Control Pair(Control a, Control b)
        { FlowLayoutPanel p = new FlowLayoutPanel(); p.AutoSize = true; p.WrapContents = false; p.Margin = new Padding(0); p.Controls.Add(a); p.Controls.Add(b); return p; }
        private void CrossKeyDown(object sender, KeyEventArgs e)
        {
            if (_loading) return;
            TextBox t = (TextBox)sender;
            int vk = (int)e.KeyCode;
            if (vk == 16 || vk == 17 || vk == 18 || vk == 91 || vk == 92) return;
            t.Text = HotkeySpec.Format(vk, e.Control, e.Alt, e.Shift, (Control.ModifierKeys & Keys.LWin) != 0 || (Control.ModifierKeys & Keys.RWin) != 0);
            e.SuppressKeyPress = true; e.Handled = true; Apply();
        }
        private void PickCrosshairColor(bool outline)
        {
            using (ColorDialog d = new ColorDialog())
            {
                int argb = outline ? _cfg.CrosshairOutlineColor : _cfg.CrosshairColor;
                d.Color = Color.FromArgb(argb); d.FullOpen = true;
                if (d.ShowDialog(this) == DialogResult.OK)
                { if (outline) _cfg.CrosshairOutlineColor = d.Color.ToArgb(); else _cfg.CrosshairColor = d.Color.ToArgb(); Apply(); UpdateCrosshairColorButtons(); }
            }
        }
        private void UpdateCrosshairColorButtons()
        {
            if (_btnCrosshairColor != null) _btnCrosshairColor.BackColor = Color.FromArgb(_cfg.CrosshairColor);
            if (_btnCrosshairOutlineColor != null) _btnCrosshairOutlineColor.BackColor = Color.FromArgb(_cfg.CrosshairOutlineColor);
        }

        /// <summary>
        /// 「音频」页（操作者要求：左边导航里单独开一项）。
        ///
        /// 这里管"全局"的东西：音频文件夹在哪、往哪儿丢文件、默认放哪一段、
        /// 文件夹里现在有哪些（可试听 / 直接设为默认）。
        /// 某一条提示单独用什么音频、倒计时到点放什么，仍然在「外观」页里设。
        /// </summary>
        private void BuildAudioPage(Panel page)
        {
            // 【别改这个 Dock】UiKit.Page() 已经 AutoScroll=true，UiKit.Stack() 是
            // Dock=Top + AutoSize —— 这一对才是"内容长了能滚动"的配方。
            // 之前这里画蛇添足写了 stack.Dock = Fill：子控件被强行撑成页面大小，
            // AutoScroll 永远不触发 → 操作者报的"音频界面无法往下滑"就是这个。
            TableLayoutPanel stack = UiKit.Stack();
            page.Controls.Add(stack);

            // ---- 文件夹 ----
            TableLayoutPanel gDir;
            GroupBox bDir = UiKit.Group("音频文件夹（把 wav / mp3 等文件丢进去就能用）", 4, out gDir);

            _lblAudioDir = UiKit.Label("");
            UiKit.Row(gDir, "文件夹", _lblAudioDir, "", true);

            _btnAudioOpen = UiKit.Button("打开音频文件夹", 140);
            _btnAudioOpen.Click += delegate { OpenAudioFolder(); };
            UiKit.Row(gDir, "", _btnAudioOpen, "丢完文件点下面的「重新扫描」", false, "丢完文件点「重新扫描」");

            _btnAudioReload = UiKit.Button("重新扫描", 110);
            _btnAudioReload.Click += delegate
            {
                ReloadAudioDefaults();
                ReloadAudioPageList();
                BalloonStatus("重新扫描完成：文件夹里现有 " + AudioLibrary.List().Length + " 个可用音频");
            };
            UiKit.Row(gDir, "", _btnAudioReload, "新建 / 删除文件之后点一下", false, "新建 / 删除文件后点一下");

            UiKit.Note(gDir, "支持的格式：wav / mp3 / wma / m4a / aac / mid。");
            UiKit.Note(gDir, "文件夹位置写在 config.ini 的 [General] AudioDir（默认就是程序目录下的「音频」）。");
            UiKit.AddGroup(stack, bDir);

            // ---- 默认音频（任何提示都用它，除非那条单独指定了）----
            TableLayoutPanel gDef;
            GroupBox bDef = UiKit.Group("默认音频（任何提示弹出时都放这一段）", 4, out gDef);

            _cboAudioDefault = UiKit.Combo(false, 300);
            _cboAudioDefault.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gDef, "用哪一段", _cboAudioDefault, "第一项 = 不放音频", false);

            _btnAudioTest = UiKit.Button("试听", 80);
            _btnAudioTest.Click += delegate { TestAudio(); };
            _btnAudioStop = UiKit.Button("停止播放", 100);
            _btnAudioStop.Click += delegate
            {
                AudioLibrary.StopCurrent();
                BalloonStatus("已停止播放");
            };
            UiKit.Row(gDef, "", _btnAudioTest, "", false);
            UiKit.Row(gDef, "", _btnAudioStop, "放新的也会自动停掉上一段（不会叠音）", false, "放新的会自动停掉上一段");

            UiKit.NoteButton(gDef, "单独设音频", "让某一条提示用自己的音频，而不是默认音频",
                "想给某一条提示单独设音频：到「外观」页选那条提示 →「自备音频」。\n" +
                "倒计时类提示还能设「到点放音频」（如 60=一分钟.mp3,30=紧张.mp3），同样在「外观」页。");
            UiKit.AddGroup(stack, bDef);

            // ---- 文件夹里的文件 + 自定义片段 ----
            TableLayoutPanel gList;
            GroupBox bList = UiKit.Group("音频条目（文件 + 你裁出来的片段）", 8, out gList);

            _lstAudio = UiKit.NewListBox();
            _lstAudio.Font = UiKit.Font;
            _lstAudio.BackColor = Color.White;
            _lstAudio.IntegralHeight = false;
            _lstAudio.DoubleClick += delegate { PlaySelectedAudio(); };
            _lstAudio.SelectedIndexChanged += delegate { OnAudioFileSelected(); };
            UiKit.Row(gList, "条目", ListHolder(_lstAudio, 170), "", true);

            _btnAudioPlay = UiKit.Button("试听选中", 110);
            _btnAudioPlay.Click += delegate { PlaySelectedAudio(); };
            _btnAudioSetDef = UiKit.Button("设为默认", 110);
            _btnAudioSetDef.Click += delegate { SetSelectedAsDefault(); };
            UiKit.Row(gList, "", _btnAudioPlay, "或直接双击那一条", false);
            UiKit.Row(gList, "", _btnAudioSetDef, "所有没单独指定的提示都用它", false);

            // ---- 选中条目 → 编辑（裁剪）/ 音量 ----
            _btnAudioEdit = UiKit.Button("编辑…（裁剪）", 130);
            _btnAudioEdit.Click += delegate { EditSelectedAudio(); };
            _btnAudioDelSeg = UiKit.Button("删除片段", 100);
            _btnAudioDelSeg.Click += delegate { DeleteSelectedSegment(); };
            _btnAudioExport = UiKit.Button("导出全部片段…", 130);
            _btnAudioExport.Click += delegate { ExportAllSegments(); };
            UiKit.Row(gList, "", _btnAudioEdit, "波形 + 片段条：点片段选中，拖空白 = 新片段", false, "拖空白 = 新建片段，点片段 = 选中");
            UiKit.Row(gList, "", _btnAudioDelSeg, "只删片段记录，不动原文件", false);
            UiKit.Row(gList, "", _btnAudioExport, "把所有裁好的片段按原格式导出到一个文件夹", false, "把裁好的片段导出到一个文件夹");

            _numAudioVolume = UiKit.Num(100, 0, 100, 92);
            _numAudioVolume.ValueChanged += delegate { OnAudioVolumeChanged(); };
            UiKit.Row(gList, "音量", _numAudioVolume, "%（只影响选中的这一段）", false);

            UiKit.NoteButton(gList, "裁剪说明", "把音频裁成片段，可以分配给任意提示",
                "编辑窗口里会把这个文件已有的片段都画出来：单击选中，重合处再点一下换选下面的。\n" +
                "保存出来的片段会出现在上面这个列表里（名字前面带 @），可以分配给任意提示。\n" +
                "片段不改动原文件（只记「文件 + 起止秒」），mp3 也不用重编码。");
            UiKit.AddGroup(stack, bList);

            // ---- 每段音频分配给它要触发的提示（操作者设想）----
            TableLayoutPanel gAs;
            GroupBox bAs = UiKit.Group("这段音频用在哪些提示上（勾选即分配）", 5, out gAs);

            _lblAudioAssign = UiKit.Label("（先在上面「文件夹里的音频」里点一段）");
            UiKit.Row(gAs, "这段音频", _lblAudioAssign, "", true);

            _clAudioAlerts = UiKit.NewCheckedListBox();
            _clAudioAlerts.Font = UiKit.Font;
            _clAudioAlerts.CheckOnClick = true;
            _clAudioAlerts.IntegralHeight = false;
            _clAudioAlerts.BackColor = Color.White;
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                _clAudioAlerts.Items.Add(_cfg.Alerts[i].DisplayName);
            _clAudioAlerts.ItemCheck += OnAudioAssignCheck;
            UiKit.Row(gAs, "应用到", ListHolder(_clAudioAlerts, 210), "", true);

            _btnAudioAll = UiKit.Button("全选", 80);
            _btnAudioAll.Click += delegate { SetAudioAssignAll(true); };
            _btnAudioNone = UiKit.Button("清空", 80);
            _btnAudioNone.Click += delegate { SetAudioAssignAll(false); };
            UiKit.Row(gAs, "", _btnAudioAll, "", false);
            UiKit.Row(gAs, "", _btnAudioNone, "取消这段音频对所有提示的分配", false, "= 取消它对所有提示的分配");

            UiKit.Note(gAs, "优先级：某一条单独指定的音频 > 上面的「默认音频」；两边都没有就只响系统音效。");
            UiKit.Note(gAs, "同一段音频可以勾给好几条提示；一条提示也只能有一个专属音频。");
            UiKit.AddGroup(stack, bAs);

            // ---- 自定义触发点（原来的「到点放音频」已经并进来了）----
            TableLayoutPanel gTr;
            GroupBox bTr = UiKit.Group("触发点（条件满足就播放 / 停止一段音频）", 5, out gTr);

            _lstTriggers = UiKit.NewListBox();
            _lstTriggers.Font = UiKit.Font;
            _lstTriggers.BackColor = Color.White;
            _lstTriggers.IntegralHeight = false;
            _lstTriggers.SelectedIndexChanged += delegate { SyncTriggerButtons(); };
            UiKit.Row(gTr, "现有触发点", ListHolder(_lstTriggers, 150), "", true);

            Button trigNew = UiKit.Button("新建触发点…", 130);
            trigNew.Click += delegate { EditTrigger(-1); };
            Button trigEdit = UiKit.Button("编辑选中的", 110);
            trigEdit.Click += delegate { EditTrigger(SelectedTrigger()); };
            _btnTrigToggle = UiKit.Button("启用 / 停用当前触发点", 170);
            _btnTrigToggle.Click += delegate { ToggleTrigger(); };
            Button trigDel = UiKit.Button("删除选中的", 110);
            trigDel.Click += delegate { DeleteTrigger(); };

            UiKit.Row(gTr, "", trigNew, "条件满足时：播放一段音频，或者停止正在播的音频", false, "条件满足时：放一段或停止播放");
            UiKit.Row(gTr, "", trigEdit, "", false);
            UiKit.Row(gTr, "", _btnTrigToggle, "列表里的【启用】/【停用】就是当前状态", false, "看列表里的【启用】/【停用】");
            UiKit.Row(gTr, "", trigDel, "", false);

            UiKit.NoteButton(gTr, "触发点说明", "满足条件时自动放一段音频，或停止正在播的音频",
                "倒计时到点放音频 = 条件「某条提示的倒计时到点」；原来的「到点放音频」设置会自动搬成这种触发点。\n" +
                "例：核弹剩余到点 60 秒 → 放一段；本局第几波 = 3 → 放一段；某个 SCP 刚刚阵亡（096）→ 放一段。\n" +
                "停止条件：新建触发点 → 选「停止正在播的音频」→ 条件写「净化刚刚开始计时」之类即可。");
            UiKit.AddGroup(stack, bTr);

            ReloadAudioDefaults(null);
            ReloadAudioPageList();
            RefreshAudioAssign();
            RefreshTriggerList();
        }

        // ============================================================
        // 自定义音频触发点：列表 / 新建 / 编辑 / 删除 / 开关
        // ============================================================

        /// <summary>当前选中的触发点下标（-1 = 没选）。</summary>
        private int SelectedTrigger()
        {
            if (_lstTriggers == null) return -1;
            return _lstTriggers.SelectedIndex;
        }

        private void RefreshTriggerList()
        {
            if (_lstTriggers == null) return;
            _cfg.RebuildAudioTriggers();
            List<AudioTrigger> list = _cfg.AudioTriggers;

            _lstTriggers.BeginUpdate();
            _lstTriggers.Items.Clear();
            if (list.Count == 0)
            {
                _lstTriggers.Items.Add("（还没有触发点：点下面的「新建触发点…」）");
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    AudioTrigger t = list[i];
                    _lstTriggers.Items.Add((t.Enabled ? "【启用】 " : "【停用】 ") + t.Name
                        + "  →  " + (string.IsNullOrEmpty(t.Clip) ? "（没选音频）" : t.Clip)
                        + "   {" + t.Conds.Count + " 个条件}");
                }
            }
            _lstTriggers.EndUpdate();

            SyncTriggerButtons();
        }

        /// <summary>保存触发点列表：写回配置字符串 + 存盘 + 让引擎重新解析。</summary>
        private void SaveTriggers()
        {
            _cfg.AudioTriggerSpec = AudioTriggerSpec.Join(_cfg.AudioTriggers);
            _cfg.RebuildAudioTriggers();
            ScheduleSave();
            _mgr.ApplyConfig();
            RefreshTriggerList();
        }

        /// <summary>倒计时类提示的显示名（触发点里"某条提示的倒计时到点"用）。</summary>
        private string[] CountdownAlertNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                if (_cfg.Alerts[i].HasCountdown) names.Add(_cfg.Alerts[i].DisplayName);
            return names.ToArray();
        }

        /// <summary>
        /// 触发点里能选的音频：文件夹里的文件 + 在软件里裁好但**没导出**的片段。
        /// 片段的显示文字带文件名和起止秒（跟「音频条目」列表一致），
        /// 但写进配置的是 @名字，所以显示文字和实际值要成对给。
        /// </summary>
        private List<AudioTriggerForm.ClipChoice> TriggerClipChoices()
        {
            List<AudioTriggerForm.ClipChoice> list = new List<AudioTriggerForm.ClipChoice>();

            string[] files = AudioLibrary.List();
            for (int i = 0; i < files.Length; i++)
                list.Add(new AudioTriggerForm.ClipChoice { Text = files[i], Key = files[i] });

            List<AudioLibrary.Segment> segs = AudioLibrary.ParseSegments(
                _cfg.AudioSegments == null ? "" : _cfg.AudioSegments);
            for (int i = 0; i < segs.Count; i++)
                list.Add(new AudioTriggerForm.ClipChoice
                {
                    Text = segs[i].Display,
                    Key = AudioLibrary.SegmentPrefix + segs[i].Name
                });

            return list;
        }

        private void EditTrigger(int index)
        {
            List<AudioTrigger> list = _cfg.AudioTriggers;
            AudioTrigger t = (index >= 0 && index < list.Count)
                ? list[index].Clone() : NewTriggerTemplate();

            using (AudioTriggerForm f = new AudioTriggerForm(t, TriggerClipChoices(),
                       CountdownAlertNames(), TacticalLog.ScpCodes))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                AudioTrigger result = f.Result;
                if (result == null) return;

                if (index >= 0 && index < list.Count) list[index] = result;
                else list.Add(result);
                SaveTriggers();
                Logger.On("音频", "触发点已保存：「" + result.Name + "」（" + result.Text()
                    + "）→ " + (result.IsStop ? "停止播放" : result.Clip));
            }
        }

        /// <summary>新建时的模板：给一个最常用、看得懂的例子。</summary>
        private static AudioTrigger NewTriggerTemplate()
        {
            AudioTrigger t = new AudioTrigger();
            t.Name = "新触发点";
            t.CooldownSeconds = 3;
            t.OncePerRound = true;
            AudioCond c = new AudioCond();
            c.Event = "pulse.round.start";    // 回合刚开始
            c.Op = "==";
            c.Value = 1;
            t.Conds.Add(c);
            return t;
        }

        private void DeleteTrigger()
        {
            int i = SelectedTrigger();
            List<AudioTrigger> list = _cfg.AudioTriggers;
            if (i < 0 || i >= list.Count)
            {
                BalloonStatus("先在列表里点一个触发点");
                return;
            }
            string name = list[i].Name;
            list.RemoveAt(i);
            SaveTriggers();
            BalloonStatus("已删除触发点：" + name);
        }

        /// <summary>按当前选中项，把"启用 / 停用"那颗按钮的文字对上。</summary>
        private void SyncTriggerButtons()
        {
            if (_btnTrigToggle == null) return;
            int i = SelectedTrigger();
            List<AudioTrigger> list = _cfg.AudioTriggers;
            if (i < 0 || i >= list.Count)
            {
                _btnTrigToggle.Text = "启用 / 停用当前触发点";
                return;
            }
            _btnTrigToggle.Text = list[i].Enabled ? "停用这个触发点" : "启用这个触发点";
        }

        /// <summary>启用 / 停用当前选中的触发点（操作者要求：用按钮，且状态要显眼）。</summary>
        private void ToggleTrigger()
        {
            int i = SelectedTrigger();
            List<AudioTrigger> list = _cfg.AudioTriggers;
            if (i < 0 || i >= list.Count)
            {
                BalloonStatus("先在列表里点一个触发点");
                return;
            }
            if (_loading) return;

            list[i].Enabled = !list[i].Enabled;
            string state = list[i].Enabled ? "启用" : "停用";
            string name = list[i].Name;
            SaveTriggers();
            BalloonStatus("已" + state + "触发点：" + name);
            Logger.On("音频", "触发点「" + name + "」已" + state);
        }

        // ---- 给控件套一个固定高度的容器 ----
        // 【为什么】表格行是 AutoSize 的，Dock=Fill 的控件如果直接放进去，
        // 行高会被压到控件的最小高度（列表就成了一条缝）。
        private static Panel ListHolder(Control inner, int height)
        {
            Panel holder = new Panel();
            holder.Dock = DockStyle.Fill;
            holder.Height = height;
            holder.MinimumSize = new Size(0, height);
            holder.BackColor = Color.Transparent;
            inner.Dock = DockStyle.Fill;
            holder.Controls.Add(inner);
            return holder;
        }

        /// <summary>
        /// 当前选中条目的"键"：文件名，或者 `@片段名`。
        /// 列表里显示的是给人看的文字（片段那行前面带 @），键从 _audioKeys 里按下标取。
        /// </summary>
        private string SelectedAudioKey()
        {
            try
            {
                if (_lstAudio == null || _lstAudio.SelectedIndex < 0) return "";
                if (_lstAudio.SelectedIndex < _audioKeys.Count) return _audioKeys[_lstAudio.SelectedIndex];
            }
            catch { }
            return "";
        }

        /// <summary>在文件列表里换了选中项 → 刷新音量框和下面"应用到哪些提示"的勾选。</summary>
        private void OnAudioFileSelected()
        {
            try
            {
                _audioAssignFile = SelectedAudioKey();
                RefreshAudioAssign();
                RefreshAudioVolume();
            }
            catch { }
        }

        /// <summary>把选中条目的音量显示到数字框里（不触发写盘）。</summary>
        private void RefreshAudioVolume()
        {
            if (_numAudioVolume == null) return;
            string key = SelectedAudioKey();
            bool wasLoading = _loading;        // 别把外层正在加载的状态弄丢
            _loading = true;
            try
            {
                _numAudioVolume.Enabled = key.Length > 0;
                _numAudioVolume.Value = key.Length > 0
                    ? AudioLibrary.VolumeOf(_cfg.AudioVolumes, key) : 100;
            }
            finally { _loading = wasLoading; }
        }

        /// <summary>改了音量 → 写进 config（按"这一段音频"存，不跟提示绑定）。</summary>
        private void OnAudioVolumeChanged()
        {
            if (_loading) return;
            string key = SelectedAudioKey();
            if (key.Length == 0) return;

            _cfg.AudioVolumes = AudioLibrary.SetVolume(_cfg.AudioVolumes, key, (int)_numAudioVolume.Value);
            AudioLibrary.ApplyConfig(_cfg);
            ScheduleSave();
            BalloonStatus("「" + key + "」音量 = " + (int)_numAudioVolume.Value + "%");
        }

        /// <summary>「编辑…」：打开裁剪编辑窗口，存出来的片段立刻进列表。</summary>
        private void EditSelectedAudio()
        {
            try
            {
                string key = SelectedAudioKey();
                if (key.Length == 0)
                {
                    BalloonStatus("先在列表里点一条音频（片段不能二次编辑，重新裁一个就行）");
                    return;
                }
                if (key.StartsWith(AudioLibrary.SegmentPrefix))
                {
                    BalloonStatus("片段不能二次编辑：对着原来的那个文件重新裁一段即可");
                    return;
                }

                string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
                string path = System.IO.Path.Combine(dir == null ? "" : dir, key);
                if (!System.IO.File.Exists(path))
                {
                    MessageBox.Show("找不到这个文件：\n" + path, "音频",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AudioClipEditor ed = new AudioClipEditor(key, path, 0);
                ed._segmentsSpec = _cfg.AudioSegments == null ? "" : _cfg.AudioSegments;
                ed._volumesSpec = _cfg.AudioVolumes == null ? "" : _cfg.AudioVolumes;
                DialogResult r = ed.ShowDialog(this);
                if (r != DialogResult.OK) return;

                _cfg.AudioSegments = ed._segmentsSpec;
                AudioLibrary.ApplyConfig(_cfg);
                ScheduleSave();
                ReloadAudioPageList();
                ReloadAudioDefaults(null);

                // 选中刚动过的那个片段（没有新建/改名就不跳）
                if (ed.SavedName.Length > 0)
                {
                    int idx = _audioKeys.IndexOf(AudioLibrary.SegmentPrefix + ed.SavedName);
                    if (idx >= 0) _lstAudio.SelectedIndex = idx;
                    BalloonStatus("片段「" + ed.SavedName + "」已保存 —— 现在可以勾给提示了");
                }
                else
                {
                    BalloonStatus("音频片段已更新");
                }
            }
            catch (Exception ex)
            {
                Logger.Err("音频", "打开裁剪编辑窗口失败", ex);
                MessageBox.Show("打开编辑窗口失败：" + ex.Message, "音频",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 一键导出全部片段（操作者要求）：选一个文件夹，把每个片段按原格式切出来存进去。
        /// wav 按采样复制、mp3 按帧切，都不重编码；切不了的格式导出完整文件并说明。
        /// </summary>
        private void ExportAllSegments()
        {
            try
            {
                if ((_cfg.AudioSegments ?? "").Trim().Length == 0)
                {
                    MessageBox.Show("还没有任何已保存的片段（先「编辑…」裁一段）。", "导出片段",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择把片段导出到哪个文件夹（会新建「片段导出」子文件夹）";
                    dlg.ShowNewFolderButton = true;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    string dest = System.IO.Path.Combine(dlg.SelectedPath, "片段导出");
                    Tuple<int, string> res = AudioLibrary.ExportAllSegments(_cfg.AudioSegments, dest);
                    Logger.On("音频", "导出片段：" + res.Item2.Replace("\r\n", "；"));
                    MessageBox.Show(res.Item2, "导出片段",
                        MessageBoxButtons.OK,
                        res.Item1 > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    if (res.Item1 > 0) BalloonStatus("已导出 " + res.Item1 + " 个片段");
                }
            }
            catch (Exception ex)
            {
                Logger.Err("音频", "导出片段失败", ex);
                MessageBox.Show("导出失败：" + ex.Message, "导出片段",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>删掉选中的片段（只删记录，不动原文件 / 已经分配它的提示照旧用不了它）。</summary>
        private void DeleteSelectedSegment()
        {
            try
            {
                string key = SelectedAudioKey();
                if (key.Length == 0 || !key.StartsWith(AudioLibrary.SegmentPrefix))
                {
                    BalloonStatus("先选中一个片段（名字带 @ 的那种）");
                    return;
                }
                string name = key.Substring(AudioLibrary.SegmentPrefix.Length);
                if (MessageBox.Show("删除片段「" + name + "」？\n（原文件不会被删；已经分配它的提示会变回「没指定」）",
                        "音频", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                List<AudioLibrary.Segment> list = AudioLibrary.ParseSegments(_cfg.AudioSegments);
                for (int i = 0; i < list.Count; i++)
                    if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) { list.RemoveAt(i); break; }
                _cfg.AudioSegments = AudioLibrary.JoinSegments(list);
                AudioLibrary.ApplyConfig(_cfg);
                ScheduleSave();
                ReloadAudioPageList();
                ReloadAudioDefaults(null);
                BalloonStatus("片段「" + name + "」已删除");
            }
            catch (Exception ex) { Logger.Err("音频", "删除片段失败", ex); }
        }

        /// <summary>把"这段音频分配给了哪些提示"按配置重新勾一遍。</summary>
        private void RefreshAudioAssign()
        {
            if (_clAudioAlerts == null) return;
            _loadingAssign = true;
            try
            {
                bool has = !string.IsNullOrEmpty(_audioAssignFile);
                if (_lblAudioAssign != null)
                    _lblAudioAssign.Text = has ? _audioAssignFile : "（先在上面「音频条目」里点一条）";
                _clAudioAlerts.Enabled = has;

                for (int i = 0; i < _clAudioAlerts.Items.Count && i < _cfg.Alerts.Length; i++)
                {
                    bool on = has && string.Equals(_cfg.Alerts[i].AudioFile, _audioAssignFile,
                        StringComparison.OrdinalIgnoreCase);
                    if (_clAudioAlerts.GetItemChecked(i) != on) _clAudioAlerts.SetItemChecked(i, on);
                }
            }
            catch { }
            finally { _loadingAssign = false; }
        }

        private void OnAudioAssignCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loadingAssign || string.IsNullOrEmpty(_audioAssignFile)) return;
            int i = e.Index;
            if (i < 0 || i >= _cfg.Alerts.Length) return;

            bool want = e.NewValue == CheckState.Checked;
            if (want) _cfg.Alerts[i].AudioFile = _audioAssignFile;
            else if (string.Equals(_cfg.Alerts[i].AudioFile, _audioAssignFile, StringComparison.OrdinalIgnoreCase))
                _cfg.Alerts[i].AudioFile = "";

            ScheduleSave();
            _mgr.ApplyConfig();
            Logger.On("音频", "「" + _audioAssignFile + "」" + (want ? "分配给了" : "取消分配给")
                + "提示「" + _cfg.Alerts[i].DisplayName + "」");
        }

        /// <summary>全选 / 清空：把这段音频分配给所有提示（或全部取消）。</summary>
        private void SetAudioAssignAll(bool on)
        {
            if (string.IsNullOrEmpty(_audioAssignFile))
            {
                BalloonStatus("先在「文件夹里的音频」里点一段音频");
                return;
            }
            _loadingAssign = true;
            try
            {
                for (int i = 0; i < _clAudioAlerts.Items.Count && i < _cfg.Alerts.Length; i++)
                {
                    if (on) _cfg.Alerts[i].AudioFile = _audioAssignFile;
                    else if (string.Equals(_cfg.Alerts[i].AudioFile, _audioAssignFile,
                        StringComparison.OrdinalIgnoreCase)) _cfg.Alerts[i].AudioFile = "";
                    _clAudioAlerts.SetItemChecked(i, on);
                }
            }
            finally { _loadingAssign = false; }

            ScheduleSave();
            _mgr.ApplyConfig();
            BalloonStatus(on ? ("「" + _audioAssignFile + "」已分配给全部 "
                + _clAudioAlerts.Items.Count + " 条提示")
                : ("已取消「" + _audioAssignFile + "」的全部分配"));
        }

        /// <summary>把音频文件夹里的文件 + 自定义片段填进「音频」页的列表。</summary>
        private void ReloadAudioPageList()
        {
            try
            {
                if (_lstAudio == null) return;
                string keep = SelectedAudioKey();

                _lstAudio.BeginUpdate();
                _lstAudio.Items.Clear();
                _audioKeys.Clear();

                string[] files = AudioLibrary.List();
                for (int i = 0; i < files.Length; i++)
                {
                    _audioKeys.Add(files[i]);
                    _lstAudio.Items.Add(files[i]);
                }

                List<AudioLibrary.Segment> segs = AudioLibrary.ParseSegments(_cfg.AudioSegments);
                for (int i = 0; i < segs.Count; i++)
                {
                    _audioKeys.Add(AudioLibrary.SegmentPrefix + segs[i].Name);
                    _lstAudio.Items.Add(segs[i].Display);
                }
                _lstAudio.EndUpdate();

                if (!string.IsNullOrEmpty(keep))
                {
                    int idx = _audioKeys.IndexOf(keep);
                    if (idx >= 0) _lstAudio.SelectedIndex = idx;
                }
                if (_lstAudio.SelectedIndex < 0 && _lstAudio.Items.Count > 0)
                    _lstAudio.SelectedIndex = 0;

                if (_lblAudioDir != null)
                {
                    string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
                    _lblAudioDir.Text = (dir == null ? "（定位失败）" : dir)
                        + "　——　" + files.Length + " 个音频、" + segs.Count + " 个片段";
                }
            }
            catch (Exception ex) { Logger.Err("音频", "刷新音频列表失败", ex); }
        }

        /// <summary>试听列表里选中的那一段（片段就播它那一截）。</summary>
        private void PlaySelectedAudio()
        {
            try
            {
                string key = SelectedAudioKey();
                if (key.Length == 0)
                {
                    BalloonStatus("先在列表里点一条音频");
                    return;
                }
                AudioLibrary.ApplyConfig(_cfg);
                if (!AudioLibrary.PlayKey(key, "设置里试听"))
                    MessageBox.Show("这段播不出来：" + AudioLibrary.LastError
                        + "\n\n已经试过 3 种打开方式（原因和每一次的返回都写在 SCPSLAlert.log 里，"
                        + "搜「MCI 打开失败」）。\n"
                        + "如果这个文件一直放不了，把它转成 wav 再放进「音频」文件夹 —— "
                        + "wav 走的是另一条更稳的驱动。",
                        "试听失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else BalloonStatus("正在播放：" + key);
            }
            catch (Exception ex) { Logger.Err("音频", "试听失败", ex); }
        }

        /// <summary>把列表里选中的那一段设成"默认音频"。</summary>
        private void SetSelectedAsDefault()
        {
            try
            {
                string key = SelectedAudioKey();
                if (key.Length == 0)
                {
                    BalloonStatus("先在列表里点一条音频");
                    return;
                }

                // 下拉框里已经有这一项就选中它（Apply 会写进配置）
                int idx = _cboAudioDefault.Items.IndexOf(key);
                if (idx < 0) { _cboAudioDefault.Items.Add(key); idx = _cboAudioDefault.Items.IndexOf(key); }
                _cboAudioDefault.SelectedIndex = idx;

                _cfg.AudioDefault = key;
                try { _config_SaveSafe(); } catch { }
                _mgr.ApplyConfig();
                BalloonStatus("默认音频已设为：" + key);
                Logger.On("音频", "默认音频改为：" + key);
            }
            catch (Exception ex) { Logger.Err("音频", "设为默认失败", ex); }
        }

        /// <summary>存盘（走外面给的动作，没有就直接写文件）。</summary>
        private void _config_SaveSafe()
        {
            if (_saveAction != null) _saveAction();
        }

        /// <summary>在页脚状态条上显示一句话（设置窗口底部那行）。</summary>
        private void BalloonStatus(string text)
        {
            try
            {
                if (_lblStatus != null) _lblStatus.Text = text;
                Logger.On("界面", text);
            }
            catch { }
        }

        /// <summary>新手引导页：第一次用按这个顺序做就行。</summary>
        private void BuildStartPage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();

            // 教程入口放在最显眼的位置，随时可以重看
            TableLayoutPanel g0;
            GroupBox b0 = UiKit.Group("先看一遍？", 2, out g0);
            Button tour = UiKit.PrimaryButton("开始新手教程", 150);
            tour.Click += delegate { StartTour(); };
            UiKit.Row(g0, "带你把整个界面走一遍", tour, "随时可按 Esc 退出", false);
            UiKit.Note(g0, "教程会用高亮框依次指出每个区域是干什么的，不想看就随时退出。");
            UiKit.AddGroup(stack, b0);

            TableLayoutPanel g;
            GroupBox b = UiKit.Group("第一次用，按这个顺序来就行", 6, out g);

            UiKit.Note(g, "① 先进游戏打一局，程序会自己开始计时（屏幕正上方出现“本回合已进行 03:21”）。");
            UiKit.Note(g, "② 按「回合时间校准」键，填一次当前回合打了多久。",
                "中途加入别人的服务器时，日志里没有准确起点，填这一次之后净化 / 支援的推算倒计时就都准了。");
            UiKit.Note(g, "③ 按「战术面板」键，把场上 SCP 的当前血量和阵营填一填。",
                "点「阵营」记是谁打的，点「击杀」标记阵亡 —— 票数和下一波刷新时间会自动算。");
            UiKit.Note(g, "④ 看到刷新真的出来了，按一下「刷新校准」键，把计时掰回到眼前这一刻；大波 / 小波会自动顺着上一波记。");
            UiKit.AddGroup(stack, b);

            TableLayoutPanel g2;
            GroupBox b2 = UiKit.Group("这些键平时最常按", 6, out g2);
            UiKit.Note(g2, "战术面板          填血量、看票数和刷新时间");
            UiKit.Note(g2, "回合时间校准      对准时间，中途进服必按");
            UiKit.Note(g2, "刷新校准          看到刷新就按一下（自动判大小波）");
            UiKit.Note(g2, "核弹 90 秒        核弹开始倒计时");
            UiKit.Note(g2, "净化 705 秒       净化开始倒计时");
            UiKit.Note(g2, "SCP 阵亡提醒      提醒队友注意支援刷新");
            UiKit.AddGroup(stack, b2);

            TableLayoutPanel g3;
            GroupBox b3 = UiKit.Group("快捷键怎么改（不用打字）", 3, out g3);
            UiKit.Note(g3, "到左边「快捷键」页，点一下输入框，直接按你想要的组合键即可。Backspace 清空，Esc 放弃。",
                "本程序只“看一眼”按键，不会独占 —— 游戏里照样能收到同一个键。");
            UiKit.AddGroup(stack, b3);

            TableLayoutPanel g4;
            GroupBox b4 = UiKit.Group("程序在做什么", 3, out g4);
            UiKit.Note(g4, "· 只读游戏的日志文件，不注入、不改内存、不模拟按键。");
            UiKit.Note(g4, "· 中途加入别人的服务器时，核弹 / 净化按“回合已经打了多久”推算。",
                "客户端日志里没有核弹 / 净化信息，所以这两项是推算的 —— 在「时间校准」里校准一次就准。");
            UiKit.Note(g4, "· 托盘图标右键有「时间校准…」，双击托盘图标可以回到这个设置窗口。");
            UiKit.AddGroup(stack, b4);

            page.Controls.Add(stack);
        }

        private void BuildGeneralPage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();

            // ---- 提示开关：两列复选，用网格排，不会挤在一起 ----
            TableLayoutPanel g1;
            GroupBox b1 = UiKit.Group("提示开关（只有对应事件真的发生时才弹出）", 4, out g1);
            _chkEnabled = new CheckBox[_cfg.Alerts.Length];
            TableLayoutPanel checks = new TableLayoutPanel();
            checks.Dock = DockStyle.Fill;
            checks.Margin = new Padding(0);
            checks.BackColor = Color.Transparent;
            checks.ColumnCount = 2;
            checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                _chkEnabled[i] = UiKit.Check(_cfg.Alerts[i].DisplayName, 200);
                _chkEnabled[i].CheckedChanged += delegate { if (!_loading) Apply(); };
                checks.Controls.Add(_chkEnabled[i], i % 2, i / 2);
            }

            // 【踩过的坑】这里以前写死 4 行 / 116px。提示条数涨到 18 条（要 9 行）之后，
            // 后 10 条复选框被切掉看不见也点不到 —— 用户能看到的只有前 8 条，
            // 看着就像"别的提示不见了"。现在按实际条数算行数和高度。
            int alertRows = (_cfg.Alerts.Length + 1) / 2;
            checks.RowCount = alertRows;
            for (int r = 0; r < alertRows; r++)
                checks.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / alertRows));
            // 把复选网格放进分组框的网格里，占满整行
            g1.RowCount = 1;
            g1.RowStyles.Clear();
            g1.RowStyles.Add(new RowStyle(SizeType.Absolute, alertRows * 26 + 8));
            g1.Controls.Add(checks, 0, 0);
            g1.SetColumnSpan(checks, 3);
            UiKit.ResizeGroup(g1);   // 按实际行数撑高，别让复选框被切掉
            UiKit.AddGroup(stack, b1);

            // 【已搬到「时间」页】倒计时时长 / 时间估算两组 —— 所有和时间有关的
            // 设置（含以前写死、只能改 config.ini 的那些）现在都在「时间」页里。

            // ---- 显示行为 ----
            TableLayoutPanel g3;
            GroupBox b3 = UiKit.Group("显示行为", 3, out g3);
            _numDisplay = UiKit.Num(5, 1, 600, 92);
            _numDisplay.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g3, "事件提示显示", _numDisplay, "秒后自动缩回", false);

            _chkPersistent = UiKit.Check("回合时间常驻显示（不自动缩回）", 320);
            _chkPersistent.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g3, "回合时间", _chkPersistent, "", false);

            _cboAnimQuality = UiKit.NewComboBox();
            _cboAnimQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboAnimQuality.Font = UiKit.Font;
            _cboAnimQuality.Width = 300;
            for (int i = 0; i < UiAnim.Names.Length; i++) _cboAnimQuality.Items.Add(UiAnim.Names[i]);
            _cboAnimQuality.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g3, "动画效果", _cboAnimQuality,
                "档位同时决定帧率和特效：关=瞬间出现，低=30fps，中=45fps，高=60fps，极高=120fps+额外光效。"
                + "战术面板的全息展开动画时长也按档位在 1.5~1.9 秒之间", false,
                "帧率 / 特效档位（越高越吃 CPU）");

            _chkStartupShow = UiKit.Check("启动时播放「全部提示展开」动画", 320);
            _chkStartupShow.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g3, "启动演出", _chkStartupShow, "默认关；开的话启动会弹一遍所有提示", false, "开启后程序启动会弹一遍全部提示");

            UiKit.Note(g3, "倒计时类提示在倒计时期间会一直显示，不受这一项影响。");
            UiKit.AddGroup(stack, b3);

            // ---- 整体位置 ----
            TableLayoutPanel g4;
            GroupBox b4 = UiKit.Group("整体位置（所有提示一起挪）", 3, out g4);
            _numOffsetX = UiKit.Num(0, -5000, 5000, 92);
            _numOffsetX.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g4, "水平偏移", _numOffsetX, "像素", false);

            _numOffsetY = UiKit.Num(0, -5000, 5000, 92);
            _numOffsetY.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g4, "垂直偏移", _numOffsetY, "像素", false);

            Button reset = UiKit.Button("重置所有提示位置", 176);
            reset.Click += delegate
            {
                if (MessageBox.Show(this, "把所有提示的位置恢复成默认布局？", "SCPSLAlert",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                _mgr.ResetPositions();
                _loading = true;
                _numOffsetX.Value = 0;
                _numOffsetY.Value = 0;
                _loading = false;
                ScheduleSave();
            };
            // 直接放进这一行，不额外套容器（套了反而可能整行不显示）
            UiKit.Row(g4, "恢复默认布局", reset, "位置回到初始值", false);
            UiKit.AddGroup(stack, b4);

            // ---- 提示音 ----
            // 音频文件 / 默认音频那一套已经单独搬到左边的「音频」页了，
            // 这里只留总开关（免得同一样东西两个地方都能改、容易打架）。
            TableLayoutPanel gS;
            GroupBox bS = UiKit.Group("提示音", 3, out gS);

            _chkSound = UiKit.Check("启用提示音", 320);
            _chkSound.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gS, "总开关", _chkSound, "关掉之后所有提示都不响（含自备音频）", false, "关掉后所有提示都不响");

            UiKit.Note(gS, "自备音频在左边的「音频」页；托盘图标右键菜单里也有「音频」子菜单。",
                "往「音频」文件夹丢 wav / mp3，就能选默认音频、试听。");
            UiKit.AddGroup(stack, bS);

            // ---- 屏幕 OCR 播报识别 ----
            // 默认开着，但"开着"不等于一直在跑：只有左下角那一条出现足够多的
            // 高亮像素时才会真的调用 OCR，其余时间这个模块是睡着的。
            TableLayoutPanel gO;
            GroupBox bO = UiKit.Group("屏幕 OCR 播报识别（认游戏里的播报文字，自动触发上面的提示）", 6, out gO);

            _chkOcr = UiKit.Check("启用屏幕播报识别", 320);
            _chkOcr.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "总开关", _chkOcr, "关掉之后不截图、不占 CPU", false, "关掉后完全不截图，不占 CPU");

            _chkOcrOnlyInServer = UiKit.Check("进入服务器后才开始扫描", 360);
            _chkOcrOnlyInServer.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "扫描时机", _chkOcrOnlyInServer,
                "截图 / 识别只在进入服务器之后才开始：桌面、主菜单、还没进服时完全不截图；"
                + "进服后自动开始，离开服务器立刻停。"
                + "关掉 = 一直扫（玩一半才开程序、想马上开始扫的就关掉它）", false,
                "进服后才开始扫描");

            _chkOcrToast = UiKit.Check("识别到文字时在屏幕左下角显示小提示", 360);
            _chkOcrToast.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "捕获提示", _chkOcrToast, "显示认到了什么、程序据此做了什么", false, "认到了什么就在游戏里显示一行");

            // ---- 本程序自己的窗口要不要"对截图/录制不可见"（操作者要求：能关掉）----
            // 这是 Windows 的 SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)：
            // 好处是本程序的提示窗/面板不会被自己的 OCR 截进去（否则会出现
            // "把自己的字认成游戏播报"的自我循环）；代价是 Windows 会弹
            // 「受保护的应用程序阻止桌面录制」，而且用 OBS 之类的录屏软件
            // 录不到那几块窗口。
            _chkHideFromCapture = UiKit.Check("本程序的提示窗在截图/录制里隐藏", 360);
            _chkHideFromCapture.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "屏幕录制", _chkHideFromCapture,
                "开着：Windows 会提示「受保护的应用程序阻止录制」，录屏录不到那些窗口；"
                + "关掉：录屏正常，但本程序自己的提示压在识别区上时可能被自己认进去", false,
                "开着录屏录不到本程序窗口");

            // 严格模式以前台静音连续漏识别 + 票数扫描漏识别为主，窗口重绘辅助；
            // 后台 OCR 不可靠时，由经过过滤的游戏窗口重绘判定。宽松模式保留旧 OCR 规则。
            _chkRespawnStrict = UiKit.Check("自己重生要严格判定", 360);
            _chkRespawnStrict.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "自己重生", _chkRespawnStrict,
                "开着：前台要求静音连续 2 次未识别、票数扫描也未识别（窗口重绘作辅助）；"
                + "后台按过滤后的游戏窗口重绘判定。关掉（默认）：沿用旧 OCR 规则，静音连续 2 次未识别"
                + "或死亡界面连续 3 轮全空就算", false,
                "严格模式：静音 + 票数为主，窗口重绘辅助；后台用窗口重绘");

            // "立即扫描一次"按钮：按需扫一遍所有区域，不影响自动轮询
            Button scanNow = UiKit.Button("立即扫描一次（不影响自动扫描）", 240);
            scanNow.Click += delegate
            {
                if (OcrScanNow != null) OcrScanNow();
                if (_lblScanResult != null) _lblScanResult.Text = "正在扫描…";
            };
            UiKit.Row(gO, "手动扫描", scanNow, "只扫这一次，自动轮询照常", false);

            _lblScanResult = UiKit.Label("");
            _lblScanResult.ForeColor = UiKit.SubColor;
            _lblScanResult.Font = UiKit.FontSmall;
            UiKit.Row(gO, "扫描结果", _lblScanResult, "", false);
            _cboOcrStrength = UiKit.Combo(false, 230);
            _cboOcrStrength.Items.Add("保守（只认很亮的白字）");
            _cboOcrStrength.Items.Add("标准");
            _cboOcrStrength.Items.Add("激进（推荐：暗一点的字也能认）");
            _cboOcrStrength.Items.Add("最大（最不容易漏：播报区不设冷却、加大超时，占用更高）");
            _cboOcrStrength.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "识别强度", _cboOcrStrength,
                "越激进越不容易漏，但可能多认进画面噪点；误报多就调回标准", false,
                "越激进越不易漏，也越可能误报");

            _cboOcrModel = UiKit.Combo(false, 230);
            _cboOcrModel.Items.Add("快速版（约 2.5MB，够快）");
            _cboOcrModel.Items.Add("最高精度版（约 13MB，更准、稍慢）");
            _cboOcrModel.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "语言模型", _cboOcrModel,
                "换模型后程序自动重建引擎，不用重启（最高精度版要放 tessdata_best 目录）", false,
                "换模型自动重建引擎，不用重启");

            _numOcrInterval = UiKit.Num(1500, 300, 30000, 92);
            _numOcrInterval.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gO, "轮询间隔", _numOcrInterval, "毫秒（默认 1500）", false);

            _cboOcrLang = UiKit.Combo(false, 220);
            _cboOcrLang.Items.Add("eng+chi_sim");
            _cboOcrLang.Items.Add("eng");
            _cboOcrLang.Items.Add("chi_sim");
            _cboOcrLang.Items.Add(OcrLangCustom);
            _cboOcrLang.SelectedIndexChanged += OnOcrLangChanged;
            UiKit.Row(gO, "识别语言", _cboOcrLang, "对应 tessdata 里的文件名", false, "用哪个语言包（tessdata 里的文件名）");

            Button regions = UiKit.Button("OCR 区域列表…", 130);
            regions.Click += delegate { EditOcrRegions(); };
            _lblOcrRegionInfo = UiKit.Row(gO, "监控区域", regions, "", false);

            Button pick = UiKit.Button("鼠标框选播报区域", 170);
            pick.Click += delegate { PickBroadcastRegion(); };
            UiKit.Row(gO, "框选", pick, "在屏幕上直接拖出播报位置（推荐）", false, "在屏幕上直接拖出播报位置");

            Button ocrTest = UiKit.Button("预览识别", 130);
            ocrTest.Click += delegate { PreviewOcr(); };
            UiKit.Row(gO, "试一下", ocrTest, "照第 1 组区域截一张，看看认得准不准", false, "截一张看看认得准不准");

            _lblOcrStatus = new Label();
            _lblOcrStatus.Font = UiKit.FontSmall;
            _lblOcrStatus.ForeColor = UiKit.SubColor;
            _lblOcrStatus.AutoSize = false;
            _lblOcrStatus.Width = 460;
            _lblOcrStatus.Height = 20;
            _lblOcrStatus.Anchor = AnchorStyles.Left;
            _lblOcrStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblOcrStatus.BackColor = Color.Transparent;
            _lblOcrStatus.Text = OcrEngine.Status;
            UiKit.Row(gO, "引擎状态", _lblOcrStatus, "", false);

            UiKit.NoteButton(gO, "识别与排障说明", "OCR 什么时候扫描、认错了或黑屏怎么办",
                "血量 / 发电机这类区域默认只在按“立即扫描”快捷键时截一次；左下角播报那条默认 1500 毫秒轮询一次。\n" +
                "识别强度选「最大」时不再强制 3 秒冷静、播报区也不再因“画面无高亮”被跳过 —— 轮询间隔设多快就扫多快（最小 300 毫秒，占用随之上升）。\n" +
                "其它强度档下，只有画面里出现足够多的高亮（白 / 浅蓝）像素时才会真的调用 OCR，其余时间它一直在睡觉。\n" +
                "识别到播报会走和 F6~F10 手动快捷键完全一样的通道；手动键一直保留，随时兜底。识别到的战况会自动填进战术面板，认错了也不影响手动用。\n" +
                "整屏压暗也看不到、或日志说“截图全黑”：游戏是独占全屏，请改成无边框窗口。\n" +
                "游戏以管理员身份运行时热键和截图会被 Windows 屏蔽 —— 托盘右键「以管理员身份重新启动」。");
            UiKit.AddGroup(stack, bO);

            // ---- 战术数据小面板（操作者要求：大面板下方 4 个小框）----
            TableLayoutPanel gMini;
            GroupBox bMini = UiKit.Group("战术数据小面板（战术面板正下方，4 个小框）", 3, out gMini);
            _chkMiniPanel = UiKit.Check("显示小面板（跟着战术面板一起开关）", 340);
            _chkMiniPanel.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gMini, "开关", _chkMiniPanel, "四个框：扫描到的 SCP / 重生令牌 / SCP 血量 / 自定义", false, "四个框：SCP / 令牌 / 血量 / 自定义");

            _cboMiniBox4 = UiKit.Combo(false, 200);
            _cboMiniBox4.Items.Add("击杀数");
            _cboMiniBox4.Items.Add("总影响力");
            _cboMiniBox4.Items.Add("发电机");
            _cboMiniBox4.Items.Add("已打掉血量");
            _cboMiniBox4.SelectedIndexChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gMini, "第4框显示", _cboMiniBox4, "前三个框固定：SCP、令牌、血量", false, "前三个框固定，只有第 4 框可选");

            UiKit.Note(gMini, "小面板的宽度和战术面板一样，数据实时同步（OCR 扫到什么这里就显示什么）。");
            UiKit.NoteButton(gMini, "扫描时机", "进服后第一次死亡时开始扫描 SCP 列表",
                "进入服务器后第一次死亡时开始扫描 SCP 列表，扫到的会弹悬浮窗；下次进服重新开始，点「立即扫描一次」也会开启。");
            UiKit.AddGroup(stack, bMini);

            page.Controls.Add(stack);
        }

        /// <summary>用鼠标在屏幕上框出播报区域，直接写回第 1 组区域。</summary>
        private void PickBroadcastRegion()
        {
            if (_cfg.OcrRegions == null || _cfg.OcrRegions.Count == 0)
            {
                MessageBox.Show(this, "还没有任何 OCR 区域。", "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OcrRegion r = _cfg.OcrRegions[0];

            try
            {
                Rectangle rc;
                using (RegionPickerForm picker = new RegionPickerForm(r.Name))
                {
                    Hide();
                    DialogResult dr;
                    try { dr = picker.ShowDialog(); }
                    finally { Show(); }

                    if (dr != DialogResult.OK) return;
                    rc = picker.SelectedRect;
                }

                if (rc.Width <= 0 || rc.Height <= 0) return;

                int screenH = Screen.PrimaryScreen.Bounds.Height;
                r.X = rc.X;
                r.Width = Math.Max(20, rc.Width);
                r.Height = Math.Max(20, rc.Height);

                // 保持原来的锚定方式：原来按"距底边"算的，就继续按距底边算，
                // 这样换分辨率区域不会跑偏。
                if (r.FromBottom) r.Y = Math.Max(0, screenH - rc.Bottom);
                else r.Y = rc.Y;
                r.ClearNormalized();

                SaveNow();
                if (OcrSettingsChanged != null) OcrSettingsChanged();
                ReloadUi();

                Logger.On("OCR", "框选了播报区域：" + r.Describe());
                MessageBox.Show(this, "播报区域已更新：\r\n\r\n" + r.Describe()
                    + "\r\n\r\n建议再点一下【预览识别】确认能认到字。",
                    "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "框选失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>打开 OCR 区域列表管理窗口；确定之后写回配置并通知 OCR 模块重新读。</summary>
        private void EditOcrRegions()
        {
            try
            {
                using (OcrRegionForm d = new OcrRegionForm(_cfg))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;

                    _cfg.OcrRegions = d.Result;
                    _cfg.EnsureOcrRegions();
                    SaveNow();                        // 立刻落盘，免得用户以为没生效
                    if (OcrSettingsChanged != null) OcrSettingsChanged();
                    ReloadUi();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "区域列表保存失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 在程序内部查看日志。
        /// 不依赖 .log 的文件关联，也不依赖资源管理器 —— 点一下一定能看到内容。
        /// </summary>
        private void ViewProgramLog(string path)
        {
            try
            {
                using (LogViewerForm d = new LogViewerForm(path))
                    d.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Logger.Err("日志", "打开日志查看器失败", ex);
                MessageBox.Show(this, "打开日志查看器失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ChooseProgramLog()
        {
            string root = Application.StartupPath;
            string[] names = new string[]
            {
                "全部日志（完整记录）",
                "OCR 识别",
                "票数识别",
                "程序操作",
                "时间与倒计时"
            };
            string[] paths = new string[]
            {
                string.IsNullOrEmpty(Logger.Path) ? System.IO.Path.Combine(root, "SCPSLAlert.log") : Logger.Path,
                string.IsNullOrEmpty(Logger.OcrPath) ? System.IO.Path.Combine(root, "SCPSLAlert.OCR.log") : Logger.OcrPath,
                string.IsNullOrEmpty(Logger.TicketPath) ? System.IO.Path.Combine(root, "SCPSLAlert.票数.log") : Logger.TicketPath,
                string.IsNullOrEmpty(Logger.OperationsPath) ? System.IO.Path.Combine(root, "SCPSLAlert.程序操作.log") : Logger.OperationsPath,
                string.IsNullOrEmpty(Logger.TimePath) ? System.IO.Path.Combine(root, "SCPSLAlert.时间.log") : Logger.TimePath
            };

            using (LogChoiceForm choice = new LogChoiceForm(names, paths))
            {
                if (choice.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(choice.SelectedPath))
                    ViewProgramLog(choice.SelectedPath);
            }
        }

        /// <summary>时间日志（只记回合/刷新/校准/倒计时那些行）。</summary>
        private void ViewTimeLog()
        {
            string p = Logger.TimePath;
            if (string.IsNullOrEmpty(p))
                p = System.IO.Path.Combine(Application.StartupPath, "SCPSLAlert.时间.log");
            ViewProgramLog(p);
        }

        /// <summary>在资源管理器里打开日志所在目录，并尽量选中日志文件。</summary>
        private void OpenLogFolder()
        {
            string p = Logger.Path;
            if (string.IsNullOrEmpty(p)) p = System.IO.Path.Combine(Application.StartupPath, "SCPSLAlert.log");

            if (NativeMethods.OpenContainingFolder(p))
            {
                Logger.On("日志", "已在资源管理器中打开日志位置：" + p);
                return;
            }

            // 连资源管理器都打不开（系统策略挡住）→ 至少把路径告诉用户
            Logger.On("日志", "打不开资源管理器，直接把路径告诉用户：" + p);
            MessageBox.Show(this,
                "打不开资源管理器（可能被系统策略挡住了）。\r\n\r\n日志文件就在这里：\r\n" + p
                + "\r\n\r\n也可以直接点左边的「查看日志」在程序里看。",
                "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>“监控区域”那一行的说明文字。</summary>
        private string OcrRegionSummary()
        {
            if (_cfg.OcrRegions == null || _cfg.OcrRegions.Count == 0) return "还没有区域";

            int on = 0, poll = 0;
            for (int i = 0; i < _cfg.OcrRegions.Count; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r == null || !r.Enabled) continue;
                on++;
                if (r.Poll) poll++;
            }
            return "共 " + _cfg.OcrRegions.Count + " 组（启用 " + on + "，定时轮询 " + poll + "）";
        }

        /// <summary>
        /// "预览识别"：立刻照当前区域截一张、识别一遍，把文字和诊断信息弹出来。
        /// 用来调识别区域最好使 —— 直接把框挪到播报真正出现的地方。
        /// </summary>
        private void PreviewOcr()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                int bright;
                string info;
                string text = OcrAlertWatcher.PreviewOnce(_cfg, out bright, out info);

                string body;
                if (text == null)
                {
                    body = info;
                }
                else
                {
                    text = text.Trim();
                    body = info + "\r\n\r\n识别到的文字：\r\n"
                         + (text.Length == 0 ? "（一片空白，说明这段画面里没有文字）" : text);
                }

                if (_lblOcrStatus != null) _lblOcrStatus.Text = OcrEngine.Status;
                MessageBox.Show(this, body, "屏幕播报识别 · 预览",
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

        // ============================================================
        // 「时间」页（操作者要求）：软件里所有和时间有关的设置集中在这里，
        // 而且全部可以手动输入；还能扫描游戏日志、把日志里关键事件的时间
        // 一键校准进来。
        // ============================================================
        private void BuildTimePage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();
            page.Controls.Add(stack);

            // ---- 日志时间校准 ----
            TableLayoutPanel gScan;
            GroupBox bScan = UiKit.Group("扫描游戏日志校准（读日志里的真实时间）", 5, out gScan);

            Button scan = UiKit.Button("扫描游戏日志…", 150);
            scan.Click += delegate { ScanLogTimes(); };
            UiKit.Row(gScan, "扫描", scan, "读服务端日志，把事件成对找出来（开始 → 结束）", false, "读服务端日志，事件成对找出");

            _lstTimeCands = UiKit.NewCheckedListBox();
            _lstTimeCands.Font = UiKit.Font;
            _lstTimeCands.BackColor = Color.White;
            _lstTimeCands.CheckOnClick = true;
            _lstTimeCands.IntegralHeight = false;
            UiKit.Row(gScan, "扫到的", ListHolder(_lstTimeCands, 200),
                "间隔行 = 填下面的时长；时刻行 = 校准此刻还剩多少", true, "勾要用的那几条");

            Button fill = UiKit.Button("把勾选的填进去", 160);
            fill.Click += delegate { ApplyCheckedCalibrations(); };
            UiKit.Row(gScan, "填入", fill, "间隔 → 写进下面的时长设置；时刻 → 校准此刻倒计时", false, "填时长 / 校准此刻");

            _lblTimeScan = UiKit.Label("");
            _lblTimeScan.ForeColor = UiKit.SubColor;
            _lblTimeScan.Font = UiKit.FontSmall;
            UiKit.Row(gScan, "说明", _lblTimeScan, "", true);
            UiKit.NoteButton(gScan, "时间校准说明", "间隔行按日志里两个时刻之差算出真实秒数",
                "扫描读两个来源：\n" +
                "· 服务端日志（ServerLogs\\…\\Round*.txt）：回合开始、净化「开始杀人」、核弹、支援刷新；\n" +
                "· 程序自己的日志（SCPSLAlert.log）：OCR 认到 CASSIE 那句「净化倒计时开始」。\n\n" +
                "· 「实测间隔」= 两个时刻之差，**现量**出来的秒数，没有任何写死的值；\n" +
                "· 勾上间隔行点填入 → 直接把实测秒数写进下面的时长设置；\n" +
                "· 净化优先用「倒计时开始 → 开始杀人」这对外部+内部日志配对，拿到就是**倒计时时长本身**；\n" +
                "  没有应用端记录时退回服务端那条「回合开始 → 开始杀人」，\n" +
                "  再按你填的「回合后多久净化」劈开（算式会写在那一行上）；\n" +
                "· 「单个时刻」行只用来校准「此刻还剩多少」，不写时长设置。");
            UiKit.AddGroup(stack, bScan);

            // ---- 倒计时时长（从「常规」页搬过来）----
            TableLayoutPanel g2;
            GroupBox b2 = UiKit.Group("倒计时时长", 3, out g2);
            _numWarhead = UiKit.Num(90, 1, 36000, 92);
            _numWarhead.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g2, "核弹倒计时", _numWarhead, "秒（F8 / Ctrl+F8 用；默认 90）", false, "F8 / Ctrl+F8 使用，默认 90");

            _numWarheadRestart = UiKit.Num(80, 1, 36000, 92);
            _numWarheadRestart.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g2, "核弹取消重启", _numWarheadRestart,
                "秒（Shift+F8 模拟「取消后重启」用；默认 80）", false, "Shift+F8 模拟取消后重启，默认 80");

            // 净化这条用的是 DeconBaseSeconds（真正驱动倒计时的那个值）；
            // 以前这里绑的是 [Decontamination] Duration，那个键其实没有代码读它，
            // 于是「改了净化倒计时但屏幕上没变」。现在两个键一起写，以 BaseSeconds 为准。
            _numDecon = UiKit.Num(705, 1, 36000, 92);
            _numDecon.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g2, "净化计时长度", _numDecon,
                "秒。F9 手动起表 / 识别到「净化程序启动」后从这里往下走（默认 705）", false,
                "F9 起表后从这里往下走，默认 705");

            _numSupport = UiKit.Num(330, 1, 36000, 92);
            _numSupport.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g2, "支援刷新", _numSupport, "秒（默认 330）", false);
            UiKit.AddGroup(stack, b2);

            // ---- 终焉协议（亡者协议）校准 ----
            // 操作者要求：终焉倒计时也要能像核弹 / 净化 / 支援那样**在「时间」页校准**一次；
            // 另外"小波刷新后多久到终焉"也要能自己填（机制不一定准，以实测为准）。
            TableLayoutPanel gDm;
            GroupBox bDm = UiKit.Group("终焉协议（亡者协议）校准", 4, out gDm);

            _numDeadManRemain = UiKit.Num(150, 0, 600, 92);
            UiKit.Row(gDm, "此刻还剩", _numDeadManRemain,
                "秒。填当前终焉倒计时的剩余秒数，按右边按钮就对齐（没在走就按这个秒数起表）", false,
                "按剩余秒数校准终焉倒计时");

            Button dmCal = UiKit.Button("按剩余秒数校准", 150);
            dmCal.Click += delegate
            {
                if (_mgr == null) return;
                double sec = (double)_numDeadManRemain.Value;
                _mgr.CalibrateDeadManRemaining(sec);
                if (_lblDeadManCal != null)
                    _lblDeadManCal.Text = "已校准：终焉协议从还剩 " + (int)sec + " 秒开始走"
                        + (sec <= 0 ? "（归零 → 核弹）" : "");
            };
            UiKit.Row(gDm, "校准", dmCal, "", false);

            _numDeadManSeconds = UiKit.Num(150, 10, 600, 92);
            _numDeadManSeconds.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gDm, "终焉倒计时", _numDeadManSeconds,
                "秒。从『最后那波小波刷完』到终焉的等待时长；默认 150 = 2 分 30 秒（各服不一样就改这里）", false,
                "最后小波后等待时长，默认 150 秒");

            _numDeadManMiniGap = UiKit.Num(0, 0, 600, 92);
            _numDeadManMiniGap.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gDm, "小波刷新后额外等待", _numDeadManMiniGap,
                "秒。最后那波小波刷完后额外等多久再起表。0 = 小波一记下来就从该时刻起算"
                + "（起点会回填到最后那波小波，屏幕显示的是这段等待的剩余时间）", false,
                "小波刷新后额外等待，0 = 立刻从该时刻起算");

            _lblDeadManCal = UiKit.Label("");
            _lblDeadManCal.ForeColor = UiKit.SubColor;
            _lblDeadManCal.Font = UiKit.FontSmall;
            UiKit.Row(gDm, "说明", _lblDeadManCal, "", true);
            UiKit.AddGroup(stack, bDm);

            // ---- 时间估算（从「常规」页搬过来）----
            TableLayoutPanel g5;
            GroupBox b5 = UiKit.Group("时间估算（加入别人的服务器时靠这个）", 4, out g5);

            _numDeconAfter = UiKit.Num(600, 10, 36000, 92);
            _numDeconAfter.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "回合后多久净化", _numDeconAfter, "秒（默认 600）", false);

            _numSupportAfter = UiKit.Num(330, 10, 36000, 92);
            _numSupportAfter.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "回合后多久支援", _numSupportAfter, "秒（默认 330）", false);

            _chkAutoCalibrate = UiKit.Check("进入服务器时自动弹出校准窗口", 320);
            _chkAutoCalibrate.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "自动校准", _chkAutoCalibrate, "", false);

            _numRoundDelay = UiKit.Num(0, 0, 600, 92);
            _numRoundDelay.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "分配角色时长", _numRoundDelay, "秒（0 = 进图就计时）", false, "0 = 进图就开始计时");

            _txtRoundKeys = UiKit.Text(300);
            _txtRoundKeys.TextChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "回合开始标志", _txtRoundKeys, "逗号分隔，命中就立刻开始计时", true, "逗号分隔，命中就开始计时");

            _chkRoundOnRole = UiKit.Check("认出正式角色时就立刻开始计时", 320);
            _chkRoundOnRole.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g5, "按角色计时", _chkRoundOnRole,
                "默认关；有些服\"变成角色\"就是回合开始，有些还要等大厅倒计时", false,
                "认出新角色就开始计时（有些服不准）");

            UiKit.Note(g5, "时间校准并进了战术面板的「校准」页（默认 F7 直接打开那一页）。");
            UiKit.Note(g5, "客户端的 Player.log 里没有核弹和净化信息，这两个只能按“回合已进行多久”推算。");
            UiKit.AddGroup(stack, b5);

            // ---- 其它时间参数（以前写死 / 只能改 config.ini，现在都能填）----
            TableLayoutPanel g6;
            GroupBox b6 = UiKit.Group("其它时间参数（以前写死的，现在都能手动输入）", 3, out g6);

            _numRoundOffset = UiKit.Num(5, 0, 60, 92);
            _numRoundOffset.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "回合开始补偿", _numRoundOffset,
                "秒。日志里的「回合开始」比游戏真实开始早这么多（默认 5）", false,
                "日志时间比真实开始早的秒数");

            _numWaveOffset = UiKit.Num(15, 0, 120, 92);
            _numWaveOffset.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "刷新波偏移", _numWaveOffset, "秒。大波刷新的推算时间修正（默认 15）", false, "大波刷新推算的修正值，默认 15");

            _numWaveInterval = UiKit.Num(300, 30, 3600, 92);
            _numWaveInterval.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "大波基准间隔", _numWaveInterval,
                "秒。两次重生波之间的基准时间；实际 = 它 + 人数×每人加时（默认 300）", false,
                "两次重生波之间的基准间隔，默认 300");

            _numWavePerPlayer = UiKit.Num(10, 0, 120, 92);
            _numWavePerPlayer.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "每人加时", _numWavePerPlayer,
                "秒。上一波重生几个人，下次计时就多这么多（默认 10）", false,
                "上一波几个人，下次计时就多这么多秒，默认 10");

            _numMiniDelay = UiKit.Num(150, 0, 600, 92);
            _numMiniDelay.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "小波最早延迟", _numMiniDelay, "秒。小波最早能在大波后多久出现（默认 150）", false, "小波最早出现的时间，默认 150");

            _numMiniWindow = UiKit.Num(120, 10, 600, 92);
            _numMiniWindow.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "小波判定窗口", _numMiniWindow, "秒。窗口内打够票数才有小波（默认 120）", false, "窗口内打够票数才有小波");

            _numGenReady = UiKit.Num(12, 0, 300, 92);
            _numGenReady.ValueChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "发电机就绪提示", _numGenReady, "秒。最后一台启动后提前这么多秒提醒（默认 12）", false, "最后一台启动后提前提醒，默认 12");

            _txtDeconMap = UiKit.Text(300);
            _txtDeconMap.TextChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g6, "净化播报对照", _txtDeconMap,
                "播报时间→现实秒数，如 15m=652,10m=456（表里没有就用播报值）", true,
                "播报时间换算成现实秒数，如 15m=652");

            UiKit.Note(g6, "改完立刻生效（会自动保存到 config.ini）。");
            UiKit.AddGroup(stack, b6);
        }

        /// <summary>
        /// 扫一遍游戏日志，把关键事件列出来让用户勾。
        /// 【为什么丢后台线程】要读好几个日志文件并逐行解析，同步跑会把设置窗口
        /// 卡住几秒（看起来像"点了没反应"）。这里先提示"正在扫描…"，扫完再回 UI 线程填列表。
        /// </summary>
        private bool _timeScanBusy;

        private void ScanLogTimes()
        {
            if (_timeScanBusy) return;
            _timeScanBusy = true;
            if (_lblTimeScan != null) _lblTimeScan.Text = "正在扫描游戏日志…";

            // 控件的值只能在 UI 线程读，先取出来再丢后台
            double deconOffset = _numDeconAfter == null ? 0 : (double)_numDeconAfter.Value;
            int wavePerPlayer = _numWavePerPlayer == null ? 10 : (int)_numWavePerPlayer.Value;

            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                LogTimeScanner.Report rep = null;
                string err = null;
                try
                {
                    LogTimeScanner.ScanOptions so = new LogTimeScanner.ScanOptions();
                    so.DeconOffsetSeconds = deconOffset;
                    so.BigWavePlayers = _mgr.Tactical != null ? _mgr.Tactical.BigWavePlayers : 16;
                    so.PerPlayerSeconds = wavePerPlayer;
                    so.MiniEntryDelay = _mgr.Tactical != null
                        ? (int)_mgr.Tactical.SmallWaveEntryDelaySeconds : 60;
                    rep = LogTimeScanner.Scan(so);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                    Logger.Err("校准", "扫描游戏日志失败", ex);
                }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate { FillTimeCandidates(rep, err); });
                }
                catch { }
            }));
        }

        /// <summary>扫描结果回到 UI 线程后填进列表框。</summary>
        private void FillTimeCandidates(LogTimeScanner.Report rep, string err)
        {
            _timeScanBusy = false;
            try
            {
                if (err != null)
                {
                    if (_lblTimeScan != null) _lblTimeScan.Text = "扫描失败：" + err;
                    return;
                }
                if (rep == null) rep = new LogTimeScanner.Report();
                _timeReport = rep;
                _timeRows.Clear();

                _lstTimeCands.BeginUpdate();
                _lstTimeCands.Items.Clear();

                // ---- 直接校准项（勾上就填对应设置）----
                int direct = 0;
                for (int i = 0; i < rep.Intervals.Count; i++)
                    if (rep.Intervals[i].Kind.StartsWith("direct")) direct++;
                if (direct > 0)
                {
                    _lstTimeCands.Items.Add("—— 直接校准：勾一项 → 就填对应的时长 ——");
                    _timeRows.Add(null);
                }
                // 固定顺序：净化 → 核弹 → 支援（不跟着时间排序乱跳）
                string[] order = { "directDecon", "directWarhead", "directSupport", "directWaveBig", "directWaveSmall" };
                for (int k = 0; k < order.Length; k++)
                {
                    for (int i = 0; i < rep.Intervals.Count; i++)
                    {
                        LogTimeScanner.Interval it = rep.Intervals[i];
                        if (it.Kind != order[k]) continue;
                        string target;
                        NumericUpDown num;
                        int val;
                        IntervalTarget(it, out num, out val, out target);
                        _lstTimeCands.Items.Add(it.Label + "　建议 " + Show(it.Seconds) + " 秒"
                            + (it.Samples.Length > 0 ? "（" + it.Samples + "）" : "")
                            + "　→ " + target);
                        TimeRow row = new TimeRow();
                        row.Interval = it;
                        _timeRows.Add(row);
                    }
                }

                // ---- 实测间隔（原始数据）----
                if (rep.Intervals.Count > direct)
                {
                    _lstTimeCands.Items.Add("—— 实测间隔（原始）：每一局的两个时刻之差 ——");
                    _timeRows.Add(null);
                }
                for (int i = 0; i < rep.Intervals.Count; i++)
                {
                    LogTimeScanner.Interval it = rep.Intervals[i];
                    if (it.Kind.StartsWith("direct")) continue;
                    string target;
                    NumericUpDown num;
                    int val;
                    IntervalTarget(it, out num, out val, out target);
                    _lstTimeCands.Items.Add(it.Label
                        + "　" + it.From.ToString("MM-dd HH:mm:ss") + " → " + it.To.ToString("HH:mm:ss")
                        + "　实测 " + Show(it.Seconds) + " 秒　→ " + target);
                    TimeRow row = new TimeRow();
                    row.Interval = it;
                    _timeRows.Add(row);
                }

                // ---- 单个时刻（校准"此刻还剩多少"）----
                if (rep.Moments.Count > 0)
                {
                    _lstTimeCands.Items.Add("—— 单个时刻：勾上 → 校准此刻还剩多少 ——");
                    _timeRows.Add(null);
                }
                DateTime now = DateTime.Now;
                for (int i = 0; i < rep.Moments.Count; i++)
                {
                    LogTimeScanner.Moment m = rep.Moments[i];
                    double age = (now - m.Time).TotalSeconds;
                    string ageText = age < 90 ? (int)age + " 秒前"
                        : age < 5400 ? ((int)(age / 60)) + " 分钟前"
                        : ((int)(age / 3600)) + " 小时前";
                    _lstTimeCands.Items.Add(m.KindName + "　"
                        + m.Time.ToString("MM-dd HH:mm:ss") + "（" + ageText + "）");
                    TimeRow row = new TimeRow();
                    row.Moment = m;
                    _timeRows.Add(row);
                }
                _lstTimeCands.EndUpdate();

                if (_lblTimeScan != null)
                    _lblTimeScan.Text = rep.Intervals.Count == 0 && rep.Moments.Count == 0
                        ? "没扫到带时间戳的事件 —— 需要服务端日志（ServerLogs\\…\\Round*.txt）。"
                        : "实测间隔 " + rep.Intervals.Count + " 条、单个时刻 " + rep.Moments.Count
                          + " 条。勾选后点「把勾选的填进去」。";
                Logger.On("校准", "扫描游戏日志：间隔 " + rep.Intervals.Count
                    + " 条、时刻 " + rep.Moments.Count + " 条");
            }
            catch (Exception ex)
            {
                Logger.Err("校准", "显示扫描结果失败", ex);
                if (_lblTimeScan != null) _lblTimeScan.Text = "扫描失败：" + ex.Message;
            }
        }

        private static string Show(double sec)
        {
            return (Math.Abs(sec - Math.Round(sec)) < 0.05)
                ? ((int)Math.Round(sec)).ToString() : sec.ToString("0.0");
        }

        /// <summary>
        /// 一条实测间隔该写进哪个设置、写多少。
        /// 净化只有「回合开始 → 清生物」这一个总时长（日志里没有 CASSIE 播报那行），
        /// 所以按用户当前填的「回合后多久净化」把总时长劈开，算式直接显示在列表行上。
        /// </summary>
        private bool IntervalTarget(LogTimeScanner.Interval it,
            out NumericUpDown num, out int value, out string desc)
        {
            num = null;
            value = 0;
            int sec = (int)Math.Round(it.Seconds);

            switch (it.Kind)
            {
                // ---- 直接校准项：算好的建议值，直接写进去 ----
                case "directDecon":
                    num = _numDecon;
                    value = sec;
                    desc = "净化计时长度 = " + sec;
                    return true;

                case "directWarhead":
                    num = _numWarhead;
                    value = sec;
                    desc = "核弹倒计时 = " + sec;
                    return true;

                case "directSupport":
                    num = _numSupportAfter;
                    value = sec;
                    desc = "回合后多久支援 = " + sec;
                    return true;

                case "directWaveBig":
                    num = _numWaveInterval;
                    value = sec;
                    desc = "大波基准间隔 = " + sec;
                    return true;

                case "directWaveSmall":
                    num = _numMiniDelay;
                    value = sec;
                    desc = "小波最早延迟 = " + sec;
                    return true;

                case "warhead":
                    num = _numWarhead;
                    value = sec;
                    desc = "核弹倒计时 = " + sec;
                    return true;

                case "support":
                    num = _numSupportAfter;
                    value = sec;
                    desc = "回合后多久支援 = " + sec;
                    return true;

                case "deconCountdown":
                    // 应用端「倒计时开始」+ 服务端「开始杀人」配对出来的真实倒计时时长，直接填
                    num = _numDecon;
                    value = sec;
                    desc = "净化计时长度 = " + sec + "（倒计时实测）";
                    return true;

                case "deconEnd":
                    {
                        // 只有服务端那条「回合开始 → 开始杀人」时，用你填的
                        // 「回合后多久净化」把它劈成倒计时时长（算式印在行上）
                        int off = _numDeconAfter == null ? 0 : (int)_numDeconAfter.Value;
                        int len = sec - off;
                        if (len < 1)
                        {
                            desc = "实测 " + sec + " 秒 比「回合后多久净化」(" + off + " 秒) 还短，没法自动填";
                            return false;
                        }
                        num = _numDecon;
                        value = len;
                        desc = "净化计时长度 = " + sec + " − " + off + " = " + len;
                        return true;
                    }

                case "warheadCancel":
                    desc = "倒计时被取消了（不算时长）";
                    return false;

                default:
                    desc = "只显示";
                    return false;
            }
        }

        /// <summary>把勾选的候选填进去：间隔 → 写时长设置；时刻 → 校准此刻倒计时。</summary>
        private void ApplyCheckedCalibrations()
        {
            if (_timeRows.Count == 0)
            {
                MessageBox.Show("先点「扫描游戏日志…」。", "时间校准",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int filled = 0, calibrated = 0, skipped = 0;
            for (int i = 0; i < _lstTimeCands.Items.Count && i < _timeRows.Count; i++)
            {
                if (!_lstTimeCands.GetItemChecked(i)) continue;
                TimeRow row = _timeRows[i];
                if (row == null) continue;                       // 分组标题行

                if (row.Interval != null)
                {
                    NumericUpDown num;
                    int val;
                    string desc;
                    if (!IntervalTarget(row.Interval, out num, out val, out desc) || num == null)
                    {
                        skipped++;
                        continue;
                    }
                    if (val < num.Minimum) val = (int)num.Minimum;
                    if (val > num.Maximum) val = (int)num.Maximum;
                    num.Value = val;                              // ValueChanged → Apply() 落配置
                    Logger.On("校准", "按日志填入：" + row.Interval.Label + " 实测 "
                        + Show(row.Interval.Seconds) + " 秒 → " + desc);
                    filled++;
                }
                else if (row.Moment != null)
                {
                    bool ok = false;
                    try { ok = _mgr.ApplyLogCalibration(row.Moment.Kind, row.Moment.Time); }
                    catch (Exception ex) { Logger.Err("校准", "填入校准失败", ex); }
                    if (ok) calibrated++;
                    else skipped++;
                }
            }

            if (_lblTimeScan != null)
                _lblTimeScan.Text = "已填 " + filled + " 个时长、校准此刻 " + calibrated
                    + " 项" + (skipped > 0 ? "，跳过 " + skipped + " 条（看 SCPSLAlert.log）" : "") + "。";
        }

        private void BuildLookPage(Panel page)
        {
            Panel uiHost = new Panel();
            uiHost.Dock = DockStyle.Top;
            uiHost.Height = 232;
            uiHost.Padding = new Padding(18, 8, 18, 6);
            uiHost.BackColor = UiKit.PageBg;
            _uiSettings = new UiSettingsPanel(_cfg,
                delegate { SaveNow(); },
                delegate { if (RestartApplication != null) RestartApplication(); });
            _uiSettings.Dock = DockStyle.Fill;
            uiHost.Controls.Add(_uiSettings);
            page.Controls.Add(uiHost);

            // 顶部一行：选择提示 + 套用到全部
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.Height = 46;
            top.ColumnCount = 4;
            top.BackColor = UiKit.PageBg;
            top.Padding = new Padding(18, 12, 18, 0);
            top.Margin = new Padding(0);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

            Label lab = UiKit.Label("要调哪一种提示");
            top.Controls.Add(lab, 0, 0);

            _cboAlert = UiKit.Combo(false, 200);
            _cboAlert.Dock = DockStyle.Fill;
            _cboAlert.Margin = new Padding(0, 2, 0, 0);
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                _cboAlert.Items.Add(_cfg.Alerts[i].DisplayName);
            _cboAlert.SelectedIndex = 0;
            _cboAlert.SelectedIndexChanged += delegate { if (!_loading) LoadSelectedAlert(); };
            top.Controls.Add(_cboAlert, 1, 0);

            Button applyAll = UiKit.Button("把当前样式套用到全部提示", 186);
            applyAll.Anchor = AnchorStyles.Right;
            applyAll.Margin = new Padding(0, 2, 0, 0);
            applyAll.Click += delegate
            {
                int i = SelectedAlert();
                if (MessageBox.Show(this,
                        "把“" + _cfg.Alerts[i].DisplayName + "”的外观套用到其他所有提示？\r\n" +
                        "（字体 / 颜色 / 背景 / 壁纸 / 玻璃 / 动画，位置和开关不受影响）",
                        "SCPSLAlert", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                ApplyVisualToAll(i);
                ReloadUi();
                Apply();
            };
            top.Controls.Add(applyAll, 3, 0);
            page.Controls.Add(top);

            // 预览
            Panel previewHost = new Panel();
            previewHost.Dock = DockStyle.Top;
            previewHost.Height = 96;
            previewHost.Padding = new Padding(18, 4, 18, 10);
            previewHost.BackColor = UiKit.PageBg;
            _preview = new AlertPreview();
            _preview.Dock = DockStyle.Fill;
            previewHost.Controls.Add(_preview);
            page.Controls.Add(previewHost);

            // 编辑器（自带滚动）
            _editor = new StyleEditor();
            _editor.Dock = DockStyle.Fill;
            _editor.Changed += delegate { if (!_loading) Apply(); };
            page.Controls.Add(_editor);
            _editor.BringToFront();

            // Dock=Fill 的编辑器要排在最后添加，这里再确认一次顺序
            page.Controls.SetChildIndex(_editor, 0);
            page.Controls.SetChildIndex(_preview.Parent, 1);
            page.Controls.SetChildIndex(top, 2);
            page.Controls.SetChildIndex(uiHost, 3);
        }

        private void BuildHotkeyPage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();

            // 排在前面的就是平时最常用的；顺序本身即引导，不加“重要”字样
            TableLayoutPanel g;
            GroupBox b = UiKit.Group("游戏内常用", 10, out g);

            AddHotkeyRow(g, "战术面板（票数与刷新）", out _txtHotkeyTactical, "打开面板");
            AddHotkeyRow(g, "回合时间校准", out _txtHotkeyCalibrate, "对准时间");
            AddHotkeyRow(g, "刷新校准", out _txtHotkeyBigWave, "看到刷新时按（自动判大小波）");
            AddHotkeyRow(g, "核弹 90 秒", out _txtHotkeyWarhead, "开始倒计时");
            AddHotkeyRow(g, "净化 705 秒", out _txtHotkeyDecon, "开始倒计时");
            AddHotkeyRow(g, "回合计时暂停 / 继续", out _txtHotkeyClockPause, "暂停时间");
            AddHotkeyRow(g, "立即扫描 OCR 区域", out _txtHotkeyScanNow, "血量 / 发电机等按需区域");
            AddHotkeyRow(g, "停止 / 恢复 全部计时", out _txtHotkeyStopAll, "按一次停，再按一次恢复");
            AddHotkeyRow(g, "停止正在播放的音频", out _txtHotkeyStopAudio, "只掐音频，不动计时和提示");

            UiKit.NoteButton(g, "热键改法", "怎么改键位、撞键了怎么办",
                "改法：点一下输入框，按下你想要的组合键，然后点旁边的「确认」才生效。只按 Ctrl / Alt / Shift 不算数，会继续等真正的那个键；Backspace 清空。\n" +
                "同一个组合被两个功能占用时，会弹窗让你选「替换」还是「互换」—— 选「替换」的那个功能会变成「（未设置）」，也就是不再有快捷键。");

            // 实时状态：按一下键就能当场看出"到底有没有送到程序"
            TableLayoutPanel gst;
            GroupBox bst = UiKit.Group("热键状态（按一下键，看看右边有没有反应）", 2, out gst);
            _lblHotkeyStatus = UiKit.Label("");
            _lblHotkeyStatus.ForeColor = UiKit.SubColor;
            _lblHotkeyStatus.Font = UiKit.FontSmall;
            UiKit.Row(gst, "当前", _lblHotkeyStatus, "", false);
            UiKit.Note(gst, "「最近收到」会随着你按键实时刷新：有反应 = 键送到了程序；一直不动 = 键没到程序。");
            UiKit.AddGroup(stack, bst);
            UiKit.AddGroup(stack, b);

            TableLayoutPanel g2;
            GroupBox b2 = UiKit.Group("其他", 5, out g2);

            AddHotkeyRow(g2, "通知开关面板", out _txtHotkeyPanel, "临时开关某一类提示");
            AddHotkeyRow(g2, "调整提示位置", out _txtHotkeyAdjust, "拖动摆放");
            AddHotkeyRow(g2, "显示 / 隐藏全部", out _txtHotkeyToggleAll, "一键全收 / 全放");
            AddHotkeyRow(g2, "按住显示全部通知", out _txtHotkeyHoldShowAll, "按住期间生效");
            AddHotkeyRow(g2, "打开设置窗口", out _txtHotkeySettings, "就是这个窗口");

            UiKit.AddGroup(stack, b2);

            TableLayoutPanel g3;
            GroupBox b3 = UiKit.Group("说明", 3, out g3);
            UiKit.Note(g3, "· 按键不会被独占，游戏和本程序都能收到同一个键。");
            UiKit.Note(g3, "· 如果同时给两个功能设了同一个组合，只有排在前面那个会生效。");
            UiKit.Note(g3, "· “按住显示全部通知”是按住期间临时无视已关闭 / 已隐藏状态。");
            UiKit.AddGroup(stack, b3);

            page.Controls.Add(stack);
        }

        /// <summary>
        /// 造一行“按键录入框 + 确认按钮”。
        /// 按下组合键后只是待确认状态，必须点旁边的「确认」才真正生效。
        /// </summary>
        private void AddHotkeyRow(TableLayoutPanel g, string label, out HotkeyBox box, string unit)
        {
            HotkeyBox b = new HotkeyBox();
            b.Width = 186;
            b.Margin = new Padding(0);

            Button ok = UiKit.Button("确认", 60);
            ok.Enabled = false;

            ok.Click += delegate { b.Confirm(); };

            // 按下新组合键时点亮按钮。
            // （之前这里挂在 Changed 上，而 Changed 只在“确认之后”才触发，
            //   所以按钮永远是灰的 —— 这就是点不动确认的原因）
            b.PendingSet += delegate
            {
                ok.Enabled = true;
                ok.Text = "确认";
            };

            // 确认前先查冲突，让用户自己决定怎么处理
            b.BeforeConfirm = delegate(string spec) { return ResolveConflict(b, spec); };

            b.Changed += delegate
            {
                ok.Enabled = false;
                if (!_loading) Apply();
            };

            _hotkeyBoxes.Add(b);
            _hotkeyNames.Add(label);

            UiKit.Row2(g, label, b, 192, ok, unit);
            box = b;
        }

        /// <summary>快捷键页上所有的录入框，用来查冲突。</summary>
        private readonly List<HotkeyBox> _hotkeyBoxes = new List<HotkeyBox>();
        private readonly List<string> _hotkeyNames = new List<string>();

        /// <summary>
        /// 同一个组合键被两个功能占了怎么办 —— 交给用户选，不闷头覆盖。
        /// 返回 false 表示这次别生效。
        /// </summary>
        private bool ResolveConflict(HotkeyBox box, string spec)
        {
            if (string.IsNullOrEmpty(spec)) return true;   // 清空不会冲突

            HotkeyBox other = null;
            string otherName = null;
            for (int i = 0; i < _hotkeyBoxes.Count; i++)
            {
                HotkeyBox hb = _hotkeyBoxes[i];
                if (hb == box) continue;
                if (string.Equals(hb.Value, spec, StringComparison.OrdinalIgnoreCase))
                {
                    other = hb;
                    otherName = _hotkeyNames[i];
                    break;
                }
            }
            if (other == null) return true;   // 没冲突，直接生效

            string oldMine = box.Value;

            using (ConflictDialog dlg = new ConflictDialog(spec, otherName, oldMine))
            {
                dlg.ShowDialog(this);
                if (dlg.Choice == ConflictChoice.Cancel) return false;

                if (dlg.Choice == ConflictChoice.Replace)
                {
                    other.Value = "";            // 原来那个功能改成“未设置”
                }
                else if (dlg.Choice == ConflictChoice.Swap)
                {
                    other.Value = oldMine;       // 两个功能互换
                }
            }
            return true;
        }

        /// <summary>造一个“按键录入”输入框，并接上自动保存。</summary>
        private HotkeyBox MakeHotkeyBox()
        {
            HotkeyBox box = new HotkeyBox();
            box.Width = 200;
            box.Anchor = AnchorStyles.Left;
            box.Margin = new Padding(0);
            box.Changed += delegate { if (!_loading) Apply(); };
            return box;
        }

        #region 新手教程

        private TourOverlay _tour;

        /// <summary>教程用：呼出 / 收起悬浮面板（由 Program 接上）。</summary>
        public Action ShowTacticalForTour;
        public Action HideTacticalForTour;
        public Action ShowCalibrateForTour;
        public Action HideCalibrateForTour;

        /// <summary>点“去改这个按键”时：先把演示的面板收掉，免得挡住快捷键页。</summary>
        private void HideTacticalForTourInternal()
        {
            try { if (HideTacticalForTour != null) HideTacticalForTour(); } catch { }
        }

        private void HideCalibrateForTourInternal()
        {
            try { if (HideCalibrateForTour != null) HideCalibrateForTour(); } catch { }
        }

        /// <summary>
        /// 教程用：把演示提示弹出来 / 收回去。
        /// 文字前面会加「示例」水印 —— 这是演示不是真实回合数据。
        /// </summary>
        private void DemoAlertsOn() { try { _mgr.PreviewAll(true); } catch { } }
        private void DemoAlertsOff() { try { _mgr.HideAllNow(); } catch { } }

        /// <summary>按板块顺序，把整个界面走一遍。</summary>
        private List<TourStep> BuildTour()
        {
            List<TourStep> list = new List<TourStep>();

            Action<int> go = delegate(int page) { _nav.SelectedIndex = page; };

            // ---------- 板块一：这是哪儿 ----------
            list.Add(new TourStep
            {
                Section = "总览",
                Title = "这是 SCPSLAlert 的设置窗口",
                Body = "它会在游戏里弹提示：回合时间、核弹倒计时、净化倒计时、SCP 阵亡提醒，"
                     + "还有一个算票数的战术面板。\n\n下面用高亮框把整个界面走一遍，随时按 Esc 可以退出。",
                Target = null
            });

            list.Add(new TourStep
            {
                Section = "总览",
                Title = "左边是分类",
                Body = "「开始使用」是这份说明；「常规」管提示开关；「外观」调提示样式，"
                     + "也能切换新版 / 旧版主界面和全局动画；"
                     + "「音频」管自备音频；「时间」放所有和时间有关的设置；"
                     + "「快捷键」改按键；「日志」是读取来源。\n\n点一下就能切换。",
                Target = _nav
            });

            list.Add(new TourStep
            {
                Section = "总览",
                Title = "底部是一排常用操作",
                Body = "「调整提示位置」用来把屏幕上的提示拖到你喜欢的地方；"
                     + "「预览效果」把所有提示用示例文字弹一遍看看长什么样。\n\n"
                     + "右边「保存并关闭」会保存设置并缩到托盘，程序继续在后台跑。",
                Target = null,
                OnEnter = delegate { go(0); }
            });

            // ---------- 板块二：开始使用 ----------
            list.Add(new TourStep
            {
                Section = "开始使用",
                Title = "第一次用，按这个顺序来",
                Body = "① 先进游戏打一局，程序会自己开始计时。\n"
                     + "② 按「回合时间校准」键填一次当前打了多久。\n"
                     + "③ 按「战术面板」键填 SCP 血量和阵营。\n"
                     + "④ 看到刷新出来按一下「刷新校准」键，大小波会自动顺延。",
                Target = null,
                OnEnter = delegate { go(0); }
            });

            // ---------- 板块三：常规 ----------
            list.Add(new TourStep
            {
                Section = "常规",
                Title = "这里是各种开关",
                Body = "八种提示可以单独开关。关掉的提示不会弹，但随时能用「通知开关面板」再打开。",
                Target = null,
                OnEnter = delegate { go(1); }
            });

            list.Add(new TourStep
            {
                Section = "常规",
                Title = "倒计时时长（在「时间」页）",
                Body = "核弹、净化、支援刷新的时间都能改 —— 这些现在统一放在左边「时间」页里。\n"
                     + "如果你常玩的服务器改过这些数值，在那里填成实际值，推算会更准。",
                Target = null,
                OnEnter = delegate { go(4); }
            });

            // ---------- 板块四：快捷键 ----------
            list.Add(new TourStep
            {
                Section = "快捷键",
                Title = "快捷键页 —— 改键不用打字",
                Body = "点一下右边的输入框，然后【直接按你想要的组合键】，它自己就填进去了。\n\n"
                     + "清空按 Backspace，放弃这次输入按 Esc。改完要重启程序才生效。",
                Target = null,
                OnEnter = delegate { go(5); }
            });

            list.Add(new TourStep
            {
                Section = "快捷键",
                Title = "常用的排在前面",
                Body = "上面那组是游戏里最常按的：战术面板、时间校准、刷新校准、核弹、净化、阵亡提醒。\n\n"
                     + "下面那组是偶尔才用的，比如调整位置、通知开关面板。",
                Target = null,
                OnEnter = delegate { go(5); }
            });

            list.Add(new TourStep
            {
                Section = "快捷键",
                Title = "小提醒：按键不会被独占",
                Body = "教程默认用的是 F5–F11。这些键被本程序占住之后，游戏里就收不到了。\n\n"
                     + "如果某个 F 键你在游戏里也要用，把它改成 Ctrl+Alt+ 组合就行。",
                Target = null,
                OnEnter = delegate { go(5); }
            });

            // ---------- 板块五：外观 ----------
            list.Add(new TourStep
            {
                Section = "外观",
                Title = "每一种提示都能单独调",
                Body = "先用上面的下拉框选一种提示，下面的改动只作用在它身上。\n\n"
                     + "调的时候上面的预览会实时跟着变。",
                Target = null,
                OnEnter = delegate { go(2); }
            });

            list.Add(new TourStep
            {
                Section = "外观",
                Title = "字体、颜色、背景、壁纸、动画",
                Body = "「显示方式」里可以设常驻不消失；\n"
                     + "「文字」「背景框与玻璃质感」「背景壁纸」「弹出动画」分别管一块。\n\n"
                     + "调好一种之后，可以用右上角「把当前样式套用到全部提示」一次复制过去。",
                Target = null,
                OnEnter = delegate { go(2); }
            });

            // ---------- 板块六：日志 ----------
            list.Add(new TourStep
            {
                Section = "日志",
                Title = "程序从哪里读数据",
                Body = "加入别人的服务器时读客户端日志，自己开服时读开服日志，两个都盯着。\n\n"
                     + "下面「当前状态」会显示实际在监控哪些文件。",
                Target = null,
                OnEnter = delegate { go(6); }
            });

            // ---------- 板块：游戏里是什么样（迷你卡片，不挡操作）----------
            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "先看看提示长什么样",
                Body = "接下来几步会真的把提示弹出来给你看，卡片缩到右下角，不会挡着你。\n\n"
                     + "（反正只是演示，不想看就点「下一步」。）",
                Target = null,
                OnEnter = delegate { go(0); }
            });

            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "这就是游戏里弹出来的提示",
                Body = "左边那几条就是真实的提示，位置、字体、颜色都和游戏里一样。\n"
                     + "为了和真实数据区分开，文字前面都带「示例」两个字。\n\n"
                     + "回合时间会钉在屏幕正上方；其他提示默认在左侧竖着排。\n"
                     + "想改位置：底部「调整提示位置」。想改样子：左边「外观」。",
                Mini = true,
                OnEnter = delegate { DemoAlertsOn(); },
                OnExit = delegate { DemoAlertsOff(); }
            });

            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "试试看：战术面板",
                Body = "已经帮你打开了战术面板（屏幕中间那个）。\n\n"
                     + "你可以直接上手点点看：点「阵营」切换 MTF / Chaos，点「击杀」标记阵亡，"
                     + "票数和下一波刷新时间会实时变。\n\n"
                     + "看完按 F10 关掉，或者直接点「下一步」—— 我会帮你收掉。",
                Mini = true,
                ExtraLabel = "去改这个按键",
                ExtraAction = delegate { HideTacticalForTourInternal(); go(5); },
                OnEnter = delegate { if (ShowTacticalForTour != null) ShowTacticalForTour(); },
                OnExit = delegate { if (HideTacticalForTour != null) HideTacticalForTour(); }
            });

            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "这个面板的数据从哪来",
                Body = "票数 = 已损失血量 ÷ 200 + 击杀数 × 15；每击杀 1 个 SCP，刷新时间 -10 秒。\n\n"
                     + "「总血量」可以按你服务器的实际数值改，改完自动保存。",
                Mini = true,
                OnEnter = delegate { if (ShowTacticalForTour != null) ShowTacticalForTour(); },
                OnExit = delegate { if (HideTacticalForTour != null) HideTacticalForTour(); }
            });

            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "试试看：战术面板的「校准」页",
                Body = "中途加入服务器、或者计时偏了，就在这里填一次。\n\n"
                     + "填一项就行，比如「3.20」表示这一局已经打了 3 分 20 秒，"
                     + "点「按上面填的校准」之后净化 / 支援的推算就都对准了。\n\n"
                     + "时间校准已经并进战术面板，不再有单独的悬浮窗；F7 会直接打开这一页。",
                Mini = true,
                ExtraLabel = "去改这个按键",
                ExtraAction = delegate { HideCalibrateForTourInternal(); go(5); },
                OnEnter = delegate { if (ShowCalibrateForTour != null) ShowCalibrateForTour(); },
                OnExit = delegate { if (HideCalibrateForTour != null) HideCalibrateForTour(); }
            });

            list.Add(new TourStep
            {
                Section = "游戏里是什么样",
                Title = "这两个面板在设置里对应哪里",
                Body = "提示样式（字体、颜色、背景、壁纸、动画）都在「外观」页，"
                     + "每一种提示可以单独调，右上角能一键套用到全部。"
                     + "同一个页面顶部还能切换新版 / 旧版主界面、开关全局动画。\n\n"
                     + "所有和时间有关的（倒计时时长、时间估算、日志校准）在「时间」页，"
                     + "提示开关在「常规」页。",
                Target = null,
                OnEnter = delegate { go(2); }
            });

            // ---------- 板块七：收尾 ----------
            list.Add(new TourStep
            {
                Section = "结束",
                Title = "好了，就这些",
                Body = "托盘图标右键里也有「时间校准…」，双击托盘图标可以随时回到这个窗口。\n\n"
                     + "想再看一遍教程，就到「开始使用」页点「开始新手教程」。",
                Target = null,
                OnEnter = delegate { go(0); }
            });

            return list;
        }

        /// <summary>开始（或重新开始）新手教程。</summary>
        public void StartTour()
        {
            try
            {
                if (_tour != null && !_tour.IsDisposed) return;

                _tour = new TourOverlay(BuildTour());
                _tour.Completed += delegate { _tour = null; };
                _tour.Exited += delegate { _tour = null; };
                _tour.StartTour();
            }
            catch
            {
                _tour = null;
            }
        }

        #endregion

        private void BuildLogPage(Panel page)
        {
            TableLayoutPanel stack = UiKit.Stack();

            TableLayoutPanel g;
            GroupBox b = UiKit.Group("日志文件", 7, out g);

            _txtLogDir = UiKit.Text(200);
            _txtLogDir.TextChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(g, "日志目录", _txtLogDir, "留空 = 自动定位", true);

            // 每个按钮各占一行，不嵌套容器
            Button browse = UiKit.Button("浏览目录…", 130);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "选择 SCP:SL 的 ServerLogs 目录（或它下面的 7777 子目录）";
                    if (d.ShowDialog(this) == DialogResult.OK) _txtLogDir.Text = d.SelectedPath;
                }
            };
            UiKit.Row(g, "手动指定", browse, "选一个文件夹", false);

            Button auto = UiKit.Button("自动定位", 130);
            auto.Click += delegate
            {
                _txtLogDir.Text = "";
                MessageBox.Show(this,
                    "已切换为自动定位。程序会同时盯住这两处：\r\n\r\n" +
                    "① 客户端日志（加入别人的服务器时用这个）\r\n" +
                    "%USERPROFILE%\\AppData\\LocalLow\\Northwood\\SCPSL\\Player.log\r\n\r\n" +
                    "② 开服日志（自己开服时用这个）\r\n" +
                    "%APPDATA%\\SCP Secret Laboratory\\ServerLogs\\*\\Round*.txt\r\n\r\n" +
                    "两边会同时监控，哪个先有事件就报哪个。",
                    "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            UiKit.Row(g, "恢复自动", auto, "清空上面的路径", false);

            _lblStatus = UiKit.Label("");
            _lblStatus.ForeColor = UiKit.Accent;
            UiKit.Row(g, "当前状态", _lblStatus, "", false);

            UiKit.Note(g, "自动定位会同时监控「客户端日志」和「开服日志」，两个都在就两个一起盯。");
            UiKit.Note(g, "加入别人的服务器时本地没有开服日志，一切以客户端 Player.log 为准。");
            UiKit.Note(g, "程序只读取日志文件，不注入游戏、不改内存、不模拟按键。");
            UiKit.Note(g, "启动时会跳过日志里已有的历史内容，只响应之后新产生的事件。");
            UiKit.AddGroup(stack, b);

            // ---- 程序自己的运行日志（和上面“游戏日志”不是一回事）----
            TableLayoutPanel gL;
            GroupBox bL = UiKit.Group("程序运行日志（排障用）", 3, out gL);

            _chkLog = UiKit.Check("启用调试日志", 260);
            _chkLog.CheckedChanged += delegate { if (!_loading) Apply(); };
            UiKit.Row(gL, "总开关", _chkLog, "默认开；关掉之后不再写盘", false, "关掉后不再写日志文件");

            Button openLog = UiKit.Button("查看日志", 110);
            openLog.Click += delegate { ChooseProgramLog(); };
            UiKit.Row(gL, "查看", openLog, "先选择日志类别，再打开对应内容", false);
            Button openLogDir = UiKit.Button("打开日志目录", 118);
            openLogDir.Click += delegate { OpenLogFolder(); };
            UiKit.Row(gL, "找日志", openLogDir, "在资源管理器里打开并选中", false);

            UiKit.Note(gL, "「查看日志」直接在程序里打开看，不依赖文件关联，点一下一定有内容。");
            UiKit.Note(gL, "日志分为完整记录、OCR 识别、票数识别、程序操作和时间与倒计时，方便定位问题。");
            UiKit.Note(gL, "原有完整日志和时间日志继续保留；分类日志从更新后开始记录。");
            UiKit.Note(gL, "日志文件超过上限时会自动删掉较旧的一半，避免一直占用硬盘。");
            UiKit.AddGroup(stack, bL);

            TableLayoutPanel g2;
            GroupBox b2 = UiKit.Group("其他快捷键", 3, out g2);
            UiKit.Note(g2, "· Ctrl+Alt+F7 打开通知开关面板（面板里可按数字键 1-8 快速开关某一类提示）");
            UiKit.Note(g2, "· Ctrl+Alt+F8 进入 / 退出“调整提示位置”模式");
            UiKit.Note(g2, "· Ctrl+Alt+H 按住不放：把被关掉或被隐藏的通知临时全部显示出来");
            UiKit.AddGroup(stack, b2);

            page.Controls.Add(stack);
        }

        #endregion

        #region 读写界面

        private int SelectedAlert()
        {
            int i = _cboAlert.SelectedIndex;
            if (i < 0 || i >= _cfg.Alerts.Length) i = 0;
            return i;
        }

        /// <summary>刷新整个界面（也用于托盘“重新加载配置”后）。</summary>
        public void ReloadUi()
        {
            _loading = true;
            try
            {
                if (_chkGameBrightness != null) _chkGameBrightness.Checked = _cfg.GameBrightnessEnabled;
                if (_chkCrosshair != null) _chkCrosshair.Checked = _cfg.CrosshairEnabled;
                if (_cboCrosshairShape != null) _cboCrosshairShape.SelectedIndex = Math.Max(0, Math.Min(_cboCrosshairShape.Items.Count - 1, _cfg.CrosshairShape));
                if (_chkCrosshairOutline != null) _chkCrosshairOutline.Checked = _cfg.CrosshairOutline;
                if (_chkCrosshairDot != null) _chkCrosshairDot.Checked = _cfg.CrosshairDot;
                if (_numCrosshairSize != null) _numCrosshairSize.Value = _cfg.CrosshairSize;
                if (_numCrosshairThick != null) _numCrosshairThick.Value = _cfg.CrosshairThickness;
                if (_numCrosshairGap != null) _numCrosshairGap.Value = _cfg.CrosshairGap;
                if (_numCrosshairOpacity != null) _numCrosshairOpacity.Value = _cfg.CrosshairOpacity;
                if (_numCrosshairX != null) _numCrosshairX.Value = _cfg.CrosshairOffsetX;
                if (_numCrosshairY != null) _numCrosshairY.Value = _cfg.CrosshairOffsetY;
                if (_txtRingKey != null) _txtRingKey.Text = _cfg.CrosshairRingKey;
                if (_numRingMs != null) _numRingMs.Value = _cfg.CrosshairRingDurationMs;
                if (_numRingRadius != null) _numRingRadius.Value = _cfg.CrosshairRingRadius;
                if (_txtExpandKey != null) _txtExpandKey.Text = _cfg.CrosshairExpandKey;
                if (_numExpandMs != null) _numExpandMs.Value = _cfg.CrosshairExpandDurationMs;
                if (_numExpandAmount != null) _numExpandAmount.Value = _cfg.CrosshairExpandAmount;
                UpdateCrosshairColorButtons();
                if (_chkBrightnessMotion != null) _chkBrightnessMotion.Checked = _cfg.GameBrightnessCaptureMotionOnly;
                if (_chkBrightnessHold != null) _chkBrightnessHold.Checked = _cfg.GameBrightnessHoldEnabled;
                if (_chkBrightnessForceGlobal != null) _chkBrightnessForceGlobal.Checked = _cfg.GameBrightnessForceGlobal;
                if (_numBrightnessHoldGamma != null)
                {
                    _numBrightnessHoldGamma.Value = Math.Max(_numBrightnessHoldGamma.Minimum,
                        Math.Min(_numBrightnessHoldGamma.Maximum, _cfg.GameBrightnessHoldGamma / 100m));
                    _numBrightnessHoldGamma.Enabled = _chkBrightnessHold != null && _chkBrightnessHold.Checked;
                }
                if (_numBrightnessHoldSaturation != null)
                {
                    _numBrightnessHoldSaturation.Value = Math.Max(_numBrightnessHoldSaturation.Minimum,
                        Math.Min(_numBrightnessHoldSaturation.Maximum, _cfg.GameBrightnessHoldSaturation / 100m));
                    _numBrightnessHoldSaturation.Enabled = _chkBrightnessHold != null && _chkBrightnessHold.Checked;
                }
                if (_numBrightnessInterval != null)
                    _numBrightnessInterval.Value = Math.Max(_numBrightnessInterval.Minimum,
                        Math.Min(_numBrightnessInterval.Maximum, _cfg.GameBrightnessIntervalMs));
                if (_lblBrightnessProfile != null)
                    _lblBrightnessProfile.Text = GameBrightnessProfile.Count(_cfg.GameBrightnessProfile) + " 个曲线节点 / " + GameBrightnessProfile.Count(_cfg.GameBrightnessSaturationProfile) + " 个饱和度节点";
                for (int i = 0; i < _chkEnabled.Length; i++)
                    _chkEnabled[i].Checked = _cfg.Alerts[i].Style.Enabled;

                _numWarhead.Value = ClampDec(_cfg.Warhead.DurationSeconds, _numWarhead);
                if (_numWarheadRestart != null)
                    _numWarheadRestart.Value = ClampDec(_cfg.WarheadRestartSeconds, _numWarheadRestart);
                // 净化计时长度取 DeconBaseSeconds（真正生效的那个键）
                _numDecon.Value = ClampDec(_cfg.DeconBaseSeconds, _numDecon);
                _numSupport.Value = ClampDec(_cfg.SupportEstimate.DurationSeconds, _numSupport);
                _numDisplay.Value = ClampDec(_cfg.DisplaySeconds, _numDisplay);
                _chkPersistent.Checked = _cfg.RoundTimer.Persistent;
                if (_cboAnimQuality != null)
                    _cboAnimQuality.SelectedIndex = Math.Max(0, Math.Min(UiAnim.Names.Length - 1, _cfg.PanelAnimQuality));
                _chkSound.Checked = _cfg.SoundEnabled;
                _numDeconAfter.Value = ClampDec(_cfg.DeconAfterRound, _numDeconAfter);
                if (_txtRoundKeys != null) _txtRoundKeys.Text = _cfg.RoundStartKeys;
                if (_chkRoundOnRole != null) _chkRoundOnRole.Checked = _cfg.RoundStartOnRole;
                _chkAutoCalibrate.Checked = _cfg.AutoCalibrateOnJoin;
                if (_chkStartupShow != null) _chkStartupShow.Checked = _cfg.StartupShow;
                if (_numRoundDelay != null) _numRoundDelay.Value = ClampDec(_cfg.RoundStartDelay, _numRoundDelay);
                if (_txtRoundKeys != null) _txtRoundKeys.Text = _cfg.RoundStartKeys;
                // 「时间」页新增的可调参数
                if (_numWarheadRestart != null) _numWarheadRestart.Value = ClampDec(_cfg.WarheadRestartSeconds, _numWarheadRestart);
                if (_numRoundOffset != null) _numRoundOffset.Value = ClampDec(_cfg.RoundStartOffsetSeconds, _numRoundOffset);
                if (_numWaveOffset != null) _numWaveOffset.Value = ClampDec(_cfg.WaveOffsetSeconds, _numWaveOffset);
                if (_numWaveInterval != null) _numWaveInterval.Value = ClampDec(_cfg.WaveIntervalSeconds, _numWaveInterval);
                if (_numWavePerPlayer != null) _numWavePerPlayer.Value = ClampDec(_cfg.WavePerPlayerSeconds, _numWavePerPlayer);
                if (_numMiniDelay != null) _numMiniDelay.Value = ClampDec(_cfg.MiniWaveDelaySeconds, _numMiniDelay);
                if (_numMiniWindow != null) _numMiniWindow.Value = ClampDec(_cfg.MiniWaveWindowSeconds, _numMiniWindow);
                if (_numDeadManSeconds != null) _numDeadManSeconds.Value = ClampDec(_cfg.DeadManSeconds, _numDeadManSeconds);
                if (_numDeadManMiniGap != null) _numDeadManMiniGap.Value = ClampDec(_cfg.DeadManAfterMiniWaveSeconds, _numDeadManMiniGap);
                // "此刻还剩"：终焉在走就填它当前的剩余，否则填总长（方便直接起表）
                if (_numDeadManRemain != null)
                {
                    double dmLeft = _mgr == null ? -1 : _mgr.DeadManRemaining;
                    _numDeadManRemain.Value = ClampDec(dmLeft > 0 ? dmLeft : _cfg.DeadManSeconds, _numDeadManRemain);
                }
                if (_numGenReady != null) _numGenReady.Value = ClampDec(_cfg.GenAllReadySeconds, _numGenReady);
                if (_txtDeconMap != null) _txtDeconMap.Text = _cfg.DeconAnnounceMap ?? "";
                _numOffsetX.Value = ClampDec(_cfg.OffsetX, _numOffsetX);
                _numOffsetY.Value = ClampDec(_cfg.OffsetY, _numOffsetY);

                _chkSound.Checked = _cfg.SoundEnabled;
                ReloadAudioDefaults(_cfg.AudioDefault);
                ReloadAudioPageList();
                _chkOcr.Checked = _cfg.EnableOcr;
                if (_chkOcrOnlyInServer != null) _chkOcrOnlyInServer.Checked = _cfg.OcrOnlyInServer;
                _chkOcrToast.Checked = _cfg.OcrToast;
                _chkMiniPanel.Checked = _cfg.GetTactical("mini_enabled") != "0";
                string m4 = _cfg.GetTactical("mini_box4");
                _cboMiniBox4.SelectedIndex =
                    m4 == "tickets" ? 1 : m4 == "gen" ? 2 : m4 == "damage" ? 3 : 0;
                _chkHideFromCapture.Checked = _cfg.HideFromCapture;
                _chkRespawnStrict.Checked = _cfg.RespawnRequireTicketsGone;
                _cboOcrStrength.SelectedIndex = Math.Max(0, Math.Min(3, _cfg.OcrStrength));
                _cboOcrModel.SelectedIndex = _cfg.OcrBestModel ? 1 : 0;
                _numOcrInterval.Value = ClampDec(_cfg.OcrPollInterval, _numOcrInterval);
                SelectOcrLang(_cfg.OcrLang);
                if (_lblOcrRegionInfo != null) _lblOcrRegionInfo.Text = OcrRegionSummary();
                if (_lblOcrStatus != null) _lblOcrStatus.Text = OcrEngine.Status;
                if (_chkLog != null) _chkLog.Checked = _cfg.EnableLog;
                if (_txtHotkeyScanNow != null) _txtHotkeyScanNow.Value = _cfg.HotkeyScanNow;
                if (_txtHotkeyStopAll != null) _txtHotkeyStopAll.Value = _cfg.HotkeyStopAll;
                if (_txtHotkeyStopAudio != null) _txtHotkeyStopAudio.Value = _cfg.HotkeyStopAudio;

                _txtLogDir.Text = _cfg.LogDirectory;

                _txtHotkeyPanel.Value = _cfg.HotkeyPanel;
                _txtHotkeyAdjust.Value = _cfg.HotkeyAdjust;
                _txtHotkeyToggleAll.Value = _cfg.HotkeyToggleAll;
                _txtHotkeySettings.Value = _cfg.HotkeySettings;
                _txtHotkeyHoldShowAll.Value = _cfg.HotkeyHoldShowAll;
                _txtHotkeyCalibrate.Value = _cfg.HotkeyCalibrate;
                _txtHotkeyWarhead.Value = _cfg.HotkeyWarhead;
                _txtHotkeyDecon.Value = _cfg.HotkeyDecon;
                _txtHotkeyTactical.Value = _cfg.HotkeyTactical;
                _txtHotkeyBigWave.Value = _cfg.HotkeyBigWave;
                _txtHotkeyClockPause.Value = _cfg.HotkeyClockPause;
            }
            finally
            {
                _loading = false;
            }

            if (_uiSettings != null) _uiSettings.Reload();
            LoadSelectedAlert();
            _uiLoaded = true;     // 从这一刻起，界面上的改动才允许写回配置
        }

        /// <summary>当前选中的全局默认音频（占位项 → 空串）。</summary>
        private string SelectedDefaultAudio()
        {
            try
            {
                string s = _cboAudioDefault == null ? null : _cboAudioDefault.SelectedItem as string;
                if (string.IsNullOrEmpty(s) || s.StartsWith("（")) return "";
                return s;
            }
            catch { return ""; }
        }

        /// <summary>把「音频」文件夹里的文件列进"默认音频"下拉框。</summary>
        private void ReloadAudioDefaults()
        {
            ReloadAudioDefaults(null);
        }

        private void ReloadAudioDefaults(string keep)
        {
            try
            {
                if (_cboAudioDefault == null) return;
                if (keep == null) keep = SelectedDefaultAudio();

                _cboAudioDefault.Items.Clear();
                _cboAudioDefault.Items.Add("（不响）");
                string[] files = AudioLibrary.List();
                for (int i = 0; i < files.Length; i++) _cboAudioDefault.Items.Add(files[i]);
                if (files.Length == 0)
                    _cboAudioDefault.Items.Add("（「音频」文件夹里还没有文件）");

                int idx = string.IsNullOrEmpty(keep) ? 0 : _cboAudioDefault.Items.IndexOf(keep);
                if (idx < 0)
                {
                    // 文件被删了也把设置留着，别悄悄丢掉
                    _cboAudioDefault.Items.Add(keep);
                    idx = _cboAudioDefault.Items.IndexOf(keep);
                }
                _cboAudioDefault.SelectedIndex = idx;
            }
            catch { }
        }

        /// <summary>试听当前选中的默认音频。</summary>
        private void TestAudio()
        {
            try
            {
                string f = SelectedDefaultAudio();
                if (string.IsNullOrEmpty(f))
                {
                    MessageBox.Show("先往「音频」文件夹里丢一个音频文件（wav / mp3 等），\n"
                        + "点「刷新列表」，再在这里选它。\n\n"
                        + "文件夹位置：程序目录下的「音频」——点「打开音频文件夹」直接打开。",
                        "试听", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!AudioLibrary.Play(f, "设置窗口里试听"))
                    MessageBox.Show("播不出这一段：" + AudioLibrary.LastError
                        + "\n\n详细原因在 SCPSLAlert.log（搜「MCI 打开失败」）。"
                        + "实在放不了就转成 wav 再试。",
                        "试听失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Logger.Err("音频", "试听失败", ex); }
        }

        /// <summary>打开音频文件夹（没有就建一个）。</summary>
        private void OpenAudioFolder()
        {
            try
            {
                string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
                if (string.IsNullOrEmpty(dir)) return;
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Logger.Err("音频", "打开音频文件夹失败", ex); }
        }

        private void LoadSelectedAlert()
        {
            _loading = true;
            try
            {
                if (_editor != null) { _editor.GlobalAudioDefault = _cfg.AudioDefault; _editor.SegmentsSpec = _cfg.AudioSegments; }
                _editor.Load(_cfg.Alerts[SelectedAlert()].Style, _cfg.Alerts[SelectedAlert()]);
            }
            finally
            {
                _loading = false;
            }
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            int i = SelectedAlert();
            AlertConfig a = _cfg.Alerts[i];
            string sample = SafeFormat(a.Format, a.SampleText, a.SampleText2, a.SampleText3);
            if (string.IsNullOrEmpty(sample)) sample = a.DisplayName;

            WindowStyle tmp = new WindowStyle();
            tmp.CopyFrom(a.Style);
            if (_previewVisual != null)
            {
                if (_previewVisual.Font != null) _previewVisual.Font.Dispose();
                if (_previewVisual.Wallpaper != null) _previewVisual.Wallpaper.Dispose();
            }
            _previewVisual = _editor.MakeVisual(tmp, a, a.Style.Icon, sample);
            _preview.SetVisual(_previewVisual, sample);
        }

        private static string SafeFormat(string fmt, string a, string b, string c)
        {
            if (string.IsNullOrEmpty(fmt)) return "";
            try { return string.Format(fmt, a, b, c); }
            catch { return fmt; }
        }

        /// <summary>把某个提示的外观复制到其它提示（不动位置和开关）。</summary>
        private void ApplyVisualToAll(int from)
        {
            WindowStyle src = _cfg.Alerts[from].Style;
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                if (i == from) continue;
                WindowStyle d = _cfg.Alerts[i].Style;
                d.FontFace = src.FontFace;
                d.FontSize = src.FontSize;
                d.Bold = src.Bold;
                d.TextColor = src.TextColor;
                d.Opacity = src.Opacity;
                d.BackColor = src.BackColor;
                d.BackOpacity = src.BackOpacity;
                d.CornerRadius = src.CornerRadius;
                d.Wallpaper = src.Wallpaper;
                d.WallpaperMode = src.WallpaperMode;
                d.WallpaperOpacity = src.WallpaperOpacity;
                d.Glass = src.Glass;
                d.GlassStrength = src.GlassStrength;
                d.GlassHighlight = src.GlassHighlight;
                d.GlassNoise = src.GlassNoise;
                d.Anim = src.Anim;
                d.AnimInMs = src.AnimInMs;
                d.AnimOutMs = src.AnimOutMs;
                d.SlideDistance = src.SlideDistance;
                d.ShowIcon = src.ShowIcon;
            }
        }

        private static decimal ClampDec(double v, NumericUpDown n)
        {
            decimal d = (decimal)v;
            if (d < n.Minimum) return n.Minimum;
            if (d > n.Maximum) return n.Maximum;
            return d;
        }

        #endregion

        #region 应用

        private void Apply()
        {
            if (_loading) return;

            // 【踩过的大坑】界面上还没从配置里读过一次（ReloadUi 没跑过）就不许写回！
            //
            // 复选框的默认状态是"未勾选"，而 Apply 会把复选框状态整个写进 config。
            // 只要有任何控件在"配置还没加载"之前触发了 Apply（比如构造界面时
            // 下拉框被填了内容 → SelectedIndexChanged → Apply），
            // 就会把 18 条提示全部写成 Enabled=false，0.7 秒后自动存盘 ——
            // 用户看到的就是"一进软件，所有提示都被关掉了"。
            if (!_uiLoaded) return;

            string hotkeysBefore = HotkeySnapshot();

            if (_chkGameBrightness != null) _cfg.GameBrightnessEnabled = _chkGameBrightness.Checked;
            if (_chkCrosshair != null) _cfg.CrosshairEnabled = _chkCrosshair.Checked;
            if (_cboCrosshairShape != null && _cboCrosshairShape.SelectedIndex >= 0) _cfg.CrosshairShape = _cboCrosshairShape.SelectedIndex;
            if (_chkCrosshairOutline != null) _cfg.CrosshairOutline = _chkCrosshairOutline.Checked;
            if (_chkCrosshairDot != null) _cfg.CrosshairDot = _chkCrosshairDot.Checked;
            if (_numCrosshairSize != null) _cfg.CrosshairSize = (int)_numCrosshairSize.Value;
            if (_numCrosshairThick != null) _cfg.CrosshairThickness = (int)_numCrosshairThick.Value;
            if (_numCrosshairGap != null) _cfg.CrosshairGap = (int)_numCrosshairGap.Value;
            if (_numCrosshairOpacity != null) _cfg.CrosshairOpacity = (int)_numCrosshairOpacity.Value;
            if (_numCrosshairX != null) _cfg.CrosshairOffsetX = (int)_numCrosshairX.Value;
            if (_numCrosshairY != null) _cfg.CrosshairOffsetY = (int)_numCrosshairY.Value;
            if (_txtRingKey != null) _cfg.CrosshairRingKey = _txtRingKey.Text;
            if (_numRingMs != null) _cfg.CrosshairRingDurationMs = (int)_numRingMs.Value;
            if (_numRingRadius != null) _cfg.CrosshairRingRadius = (int)_numRingRadius.Value;
            if (_txtExpandKey != null) _cfg.CrosshairExpandKey = _txtExpandKey.Text;
            if (_numExpandMs != null) _cfg.CrosshairExpandDurationMs = (int)_numExpandMs.Value;
            if (_numExpandAmount != null) _cfg.CrosshairExpandAmount = (int)_numExpandAmount.Value;
            // 旧页面改完准星后，把旧字段映射进新版字段，两套界面看到的是同一份设置
            _cfg.SyncCrosshairFromLegacy();
            if (_chkBrightnessMotion != null) _cfg.GameBrightnessCaptureMotionOnly = _chkBrightnessMotion.Checked;
            if (_chkBrightnessHold != null) _cfg.GameBrightnessHoldEnabled = _chkBrightnessHold.Checked;
            if (_chkBrightnessForceGlobal != null) _cfg.GameBrightnessForceGlobal = _chkBrightnessForceGlobal.Checked;
            if (_numBrightnessHoldGamma != null) _cfg.GameBrightnessHoldGamma = (int)Math.Round(_numBrightnessHoldGamma.Value * 100m);
            if (_numBrightnessHoldSaturation != null) _cfg.GameBrightnessHoldSaturation = (int)Math.Round(_numBrightnessHoldSaturation.Value * 100m);
            if (_numBrightnessInterval != null) _cfg.GameBrightnessIntervalMs = (int)_numBrightnessInterval.Value;

            for (int i = 0; i < _chkEnabled.Length; i++)
                _cfg.Alerts[i].Style.Enabled = _chkEnabled[i].Checked;

            _cfg.Warhead.DurationSeconds = (int)_numWarhead.Value;
            if (_numWarheadRestart != null) _cfg.WarheadRestartSeconds = (int)_numWarheadRestart.Value;
            // 净化：BaseSeconds 是真正驱动倒计时的那个键；Duration 一起写，
            // 免得 ini 里两个值不一致（以后要查也好看）。
            _cfg.DeconBaseSeconds = (int)_numDecon.Value;
            _cfg.Decontamination.DurationSeconds = (int)_numDecon.Value;
            _cfg.SupportEstimate.DurationSeconds = (int)_numSupport.Value;
            _cfg.DisplaySeconds = (int)_numDisplay.Value;
            _cfg.RoundTimer.Persistent = _chkPersistent.Checked;
            _cfg.PanelAnimQuality = _cboAnimQuality == null ? _cfg.PanelAnimQuality
                : Math.Max(0, Math.Min(UiAnim.Names.Length - 1, _cboAnimQuality.SelectedIndex));
            UiAnim.Quality = _cfg.PanelAnimQuality;     // 立刻生效，不用等存盘
            _cfg.SoundEnabled = _chkSound.Checked;
            _cfg.DeconAfterRound = (int)_numDeconAfter.Value;
            _cfg.RoundStartKeys = _txtRoundKeys.Text.Trim();
            _cfg.RoundStartOnRole = _chkRoundOnRole != null && _chkRoundOnRole.Checked;
            _cfg.AutoCalibrateOnJoin = _chkAutoCalibrate.Checked;
            if (_chkStartupShow != null) _cfg.StartupShow = _chkStartupShow.Checked;
            _cfg.RoundStartDelay = (int)_numRoundDelay.Value;
            _cfg.RoundStartKeys = _txtRoundKeys.Text.Trim();
            // 「时间」页新增的可调参数（以前写死 / 只能改 config.ini）
            if (_numRoundOffset != null) _cfg.RoundStartOffsetSeconds = (int)_numRoundOffset.Value;
            if (_numWaveOffset != null) _cfg.WaveOffsetSeconds = (int)_numWaveOffset.Value;
            if (_numWaveInterval != null) _cfg.WaveIntervalSeconds = (int)_numWaveInterval.Value;
            if (_numWavePerPlayer != null) _cfg.WavePerPlayerSeconds = (int)_numWavePerPlayer.Value;
            if (_numMiniDelay != null) _cfg.MiniWaveDelaySeconds = (int)_numMiniDelay.Value;
            if (_numMiniWindow != null) _cfg.MiniWaveWindowSeconds = (int)_numMiniWindow.Value;
            if (_numDeadManSeconds != null) _cfg.DeadManSeconds = (int)_numDeadManSeconds.Value;
            if (_numDeadManMiniGap != null) _cfg.DeadManAfterMiniWaveSeconds = (int)_numDeadManMiniGap.Value;
            if (_numGenReady != null) _cfg.GenAllReadySeconds = (int)_numGenReady.Value;
            if (_txtDeconMap != null) _cfg.DeconAnnounceMap = _txtDeconMap.Text.Trim();
            _cfg.OffsetX = (int)_numOffsetX.Value;
            _cfg.OffsetY = (int)_numOffsetY.Value;

            _cfg.EnableOcr = _chkOcr.Checked;
            _cfg.OcrOnlyInServer = _chkOcrOnlyInServer.Checked;
            _cfg.OcrToast = _chkOcrToast.Checked;
            _cfg.SetTactical("mini_enabled", _chkMiniPanel.Checked ? "1" : "0");
            _cfg.SetTactical("mini_box4",
                new string[] { "kills", "tickets", "gen", "damage" }[
                    Math.Max(0, Math.Min(3, _cboMiniBox4.SelectedIndex))]);
            if (_chkHideFromCapture != null) _cfg.HideFromCapture = _chkHideFromCapture.Checked;
            _cfg.RespawnRequireTicketsGone = _chkRespawnStrict.Checked;
            _cfg.OcrStrength = Math.Max(0, Math.Min(3, _cboOcrStrength.SelectedIndex));
            _cfg.OcrBestModel = (_cboOcrModel.SelectedIndex == 1);
            _cfg.OcrPollInterval = (int)_numOcrInterval.Value;
            _cfg.EnableLog = _chkLog.Checked;
            Logger.SetEnabled(_cfg.EnableLog);      // 勾掉之后立即停止写日志
            string lang = _cboOcrLang.Text.Trim();
            _cfg.OcrLang = lang.Length == 0 ? "eng+chi_sim" : lang;
            if (OcrSettingsChanged != null) OcrSettingsChanged();

            _cfg.LogDirectory = _txtLogDir.Text.Trim();

            _cfg.HotkeyPanel = _txtHotkeyPanel.Value;
            _cfg.HotkeyAdjust = _txtHotkeyAdjust.Value;
            _cfg.HotkeyToggleAll = _txtHotkeyToggleAll.Value;
            _cfg.HotkeySettings = _txtHotkeySettings.Value;
            _cfg.HotkeyHoldShowAll = _txtHotkeyHoldShowAll.Value;
            _cfg.HotkeyCalibrate = _txtHotkeyCalibrate.Value;
            _cfg.HotkeyWarhead = _txtHotkeyWarhead.Value;
            _cfg.HotkeyDecon = _txtHotkeyDecon.Value;
            _cfg.HotkeyTactical = _txtHotkeyTactical.Value;
            _cfg.HotkeyBigWave = _txtHotkeyBigWave.Value;
            _cfg.HotkeyClockPause = _txtHotkeyClockPause.Value;
            _cfg.HotkeyScanNow = _txtHotkeyScanNow.Value;
            _cfg.HotkeyStopAll = _txtHotkeyStopAll.Value;
            _cfg.HotkeyStopAudio = _txtHotkeyStopAudio.Value;

            _cfg.AudioDefault = SelectedDefaultAudio();
            _editor.GlobalAudioDefault = _cfg.AudioDefault;
            _editor.SegmentsSpec = _cfg.AudioSegments;
            _editor.Save(_cfg.Alerts[SelectedAlert()].Style, _cfg.Alerts[SelectedAlert()]);

            _mgr.ApplyConfig();
            RefreshPreview();
            ScheduleSave();

            // 快捷键改了 → 立刻重新注册，不用重启程序
            if (hotkeysBefore != HotkeySnapshot() && HotkeySettingsChanged != null)
                HotkeySettingsChanged();
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimer(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        public void SaveNow()
        {
            if (_saveAction != null) _saveAction();
            if (_lblSaved != null && !_reallyClose)
            {
                _lblSaved.Text = "已保存";
                _lblSaved.Visible = true;
                _savedTimer.Stop();
                _savedTimer.Start();
            }
        }

        /// <summary>快捷键改完立刻生效（不用重启）。</summary>
        public Action HotkeySettingsChanged;

        /// <summary>取一行热键状态文字（由 Program 接到 HotkeyForm 上）。</summary>
        public Func<string> HotkeyStatusProvider;

        /// <summary>把当前 config 里的全部快捷键拼成一个串，用来判断"这次改动动没动快捷键"。</summary>
        private string HotkeySnapshot()
        {
            return string.Join("|", new string[]
            {
                _cfg.HotkeyAdjust, _cfg.HotkeyToggleAll, _cfg.HotkeySettings, _cfg.HotkeyPanel,
                _cfg.HotkeyHoldShowAll, _cfg.HotkeyCalibrate, _cfg.HotkeyWarhead,
                _cfg.HotkeyDecon, _cfg.HotkeyTactical, _cfg.HotkeyBigWave,
                _cfg.HotkeyClockPause, _cfg.HotkeyScanNow, _cfg.HotkeyStopAll,
                _cfg.HotkeyStopAudio
            });
        }

        /// <summary>OCR 开关 / 间隔 / 区域改了之后通知主程序（由 Program 接上）。</summary>
        public Action OcrSettingsChanged;

        protected void RestoreDefaults()
        {
            if (MessageBox.Show(this, "确定要把所有设置恢复成默认值吗？", "SCPSLAlert",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            Config def = new Config();
            def.LogDirectory = _cfg.LogDirectory;
            _cfg.CopyFrom(def);
            ReloadUi();
            Apply();
            SaveNow();
        }

        private void OnStatusTimer(object sender, EventArgs e)
        {
            if (_lblStatus == null) return;
            string s = _mgr.StatusText;
            if (s != null && s.Length > 88) s = s.Substring(0, 85) + "…";
            _lblStatus.Text = s;

            // OCR 引擎是在后台异步初始化的，状态会变，顺手刷一下
            if (_lblOcrStatus != null && _lblOcrStatus.Text != OcrEngine.Status)
                _lblOcrStatus.Text = OcrEngine.Status;

            // 立即扫描的结果梗概
            if (_lblScanResult != null && OcrScanSummary != null)
            {
                string ss = OcrScanSummary();
                if (!string.IsNullOrEmpty(ss) && _lblScanResult.Text != ss)
                    _lblScanResult.Text = (ss.Length > 120 ? ss.Substring(0, 120) + "…（详细请点上面的按钮看结果窗口）" : ss);
            }

            // 热键状态（含"最近收到哪个键"）
            if (_lblHotkeyStatus != null && HotkeyStatusProvider != null)
            {
                string hs = HotkeyStatusProvider();
                if (hs != null && _lblHotkeyStatus.Text != hs) _lblHotkeyStatus.Text = hs;
            }
        }

        #endregion

        #region 显示 / 隐藏

        public void ShowSettings()
        {
            // 之前"最小化到托盘"时把任务栏按钮藏了，重新显示要还回来
            if (!ShowInTaskbar) ShowInTaskbar = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        public void ShowPage(int index)
        {
            ShowSettings();
            if (_nav == null || _pages == null) return;
            if (index < 0 || index >= _pages.Length) return;
            _nav.SelectedIndex = index;
        }

        // ---- 给新版 A.R.C.T 的“高级”页提供直达入口 --------------------------
        public void ShowOcrPreview()
        {
            ShowSettings();
            ShowPage(1);
            PreviewOcr();
        }

        public void ShowOcrRegionPicker()
        {
            ShowSettings();
            ShowPage(1);
            PickBroadcastRegion();
        }

        public void ShowOcrRegionsEditor()
        {
            ShowSettings();
            ShowPage(1);
            EditOcrRegions();
        }

        public void ShowOcrHelp()
        {
            ShowSettings();
            ShowPage(1);
            MessageBox.Show(this,
                "OCR 什么时候扫描：\n" +
                "· 左下角播报区域按「轮询间隔」定时识别；\n" +
                "· 血量 / 发电机 / SCP 列表等按需区域只在手动扫描或事件触发时截一次。\n\n" +
                "识别强度「最大」会取消 3 秒冷静，播报区也不因“画面无高亮”跳过；轮询间隔最小 300ms，占用会上升。\n\n" +
                "黑屏 / 认不到字：\n" +
                "· 游戏是独占全屏时截不到，请改成无边框窗口；\n" +
                "· 游戏以管理员运行时，本程序也要以管理员身份运行；\n" +
                "· 区域偏移后用「鼠标框选播报区域」重新框一次。",
                "OCR 识别与排障说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void ShowLogTimeScan()
        {
            ShowSettings();
            ShowPage(4);
            ScanLogTimes();
        }

        public Panel TakePage(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length) return null;
            Panel p = _pages[index];
            if (_content != null) _content.Controls.Remove(p);
            p.Dock = DockStyle.Fill;
            p.Margin = new Padding(0);
            p.Visible = true;
            return p;
        }

        public void RefreshAppearancePage()
        {
            if (_editor != null) LoadSelectedAlert();
            if (_uiSettings != null) _uiSettings.Reload();
        }


        /// <summary>
        /// 窗口藏起来 / 关掉时兜底恢复全局热键 ——
        /// 万一某个录入框还停在“等按键”状态，不能让热键一直哑着。
        /// </summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) HotkeyBox.ReleaseAll();
        }

        /// <summary>
        /// 窗口失去激活时，把"正在录入快捷键"的状态一起清掉。
        ///
        /// 【为什么必须有】录入框一旦进入"等你按键"状态，全局热键就是挂起的。
        /// 这时用户要是直接切回游戏，热键有可能一直挂着 ——
        /// 现象就是"所有快捷键都失效"。所以在失活的瞬间无条件释放。
        /// </summary>
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HotkeyBox.ReleaseAll();
        }

        /// <summary>
        /// 点右上角 × 时怎么办。
        ///
        /// 【第一次会问】还没选过（CloseAction = 0）就弹一次自定义对话框，
        /// 让用户选"最小化到托盘"还是"退出程序"，并记住选择。
        /// 之后按记住的选择直接执行，不再打扰。
        /// 想改：托盘右键菜单 →「关闭按钮的行为…」。
        ///
        /// MinimizeBox 现在是 true，所以右上角还有一个真正的"最小化"按钮，
        /// 那个走系统默认行为（收进任务栏），和 × 的行为互不影响。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveNow();

            // 不是用户点 ×（注销 / 关机 / 程序自己在退出）→ 一律放行
            if (_reallyClose || e.CloseReason != CloseReason.UserClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            int action = _cfg.CloseAction;
            bool remember = true;

            if (action != 1 && action != 2)
            {
                // 还没选过：问一次
                using (CloseConfirmForm dlg = new CloseConfirmForm(CanMinimizeToTray))
                {
                    dlg.ShowDialog(this);
                    if (dlg.Choice == CloseChoice.Cancel)
                    {
                        e.Cancel = true;      // 什么都不做，窗口留着
                        return;
                    }
                    action = (dlg.Choice == CloseChoice.Minimize) ? 1 : 2;
                    remember = dlg.Remember;
                }

                if (remember)
                {
                    _cfg.CloseAction = action;
                    SaveNow();
                }
            }

            if (action == 1 && CanMinimizeToTray)
            {
                // 最小化到托盘：只是把窗口收起来，程序照常在后台跑
                e.Cancel = true;
                HideToTray();
                return;
            }

            // 退出程序
            _reallyClose = true;
            if (_exitAction != null) { _exitAction(); return; }
            base.OnFormClosing(e);
        }

        /// <summary>托盘图标在不在（不在就别"最小化到托盘"了，否则用户再也找不回窗口）。</summary>
        public bool CanMinimizeToTray = true;

        /// <summary>最小化到托盘时对外通知一声（Program 拿它弹个气泡提示）。</summary>
        public Action MinimizedToTray;

        /// <summary>收进托盘。</summary>
        private void HideToTray()
        {
            try
            {
                HotkeyBox.ReleaseAll();     // 别让录入状态把热键一直挂着
                Hide();
                ShowInTaskbar = false;
                if (MinimizedToTray != null) MinimizedToTray();
            }
            catch { }
        }

        /// <summary>
        /// 告诉窗体“这次是真的要退出了”，之后再收到关闭请求就不再拦截。
        /// 由“退出程序”按钮 / 托盘菜单调用。
        /// </summary>
        public void PrepareExit()
        {
            _reallyClose = true;
            _saveTimer.Stop();
            _statusTimer.Stop();
            _savedTimer.Stop();
        }

        public void CloseForExit()
        {
            PrepareExit();
            Close();
        }

        #endregion
    }
}

