using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 悬浮面板的基类。
    ///
    /// 和游戏里的倒计时提示用的是同一套渲染方式：
    ///   · UpdateLayeredWindow 逐像素透明 → 深色半透明 + 圆角 + 同样的字体
    ///   · 无边框、不进任务栏、TopMost
    ///
    /// 和普通提示的区别只有一个：**焦点**。
    /// 普通提示是鼠标穿透的（WS_EX_TRANSPARENT），永远不接收输入；
    /// 面板在呼出时要把这个位去掉、并且抢到键盘焦点，才能收键盘；
    /// 关闭时必须立刻加回去、并把焦点还给游戏，否则游戏会按键失灵。
    /// </summary>
    internal abstract class OverlayPanel : Form
    {
        /// <summary>面板四周的外发光留白。</summary>
        protected const int Pad = 18;

        protected class Row
        {
            public string Label = "";
            public string Value = "";
            public bool IsInput;
            public bool IsButton;
            public bool IsHeader;
            public bool IsNote;
            /// <summary>只读的结果行：左边标签，右边数值。</summary>
            public bool IsReadout;
            public int Width;
            public string Key = "";

            // 同一行里的第二个输入框（战术面板的“总血量 / 当前血量”）
            public string Label2 = "";
            public string Value2 = "";
            public bool HasSecond;
            public int Width2;
            public string Key2 = "";

            // 行内可点击的小格子（阵营 / 击杀 这类开关）
            public string Label3 = "";
            public string Key3 = "";
            public int Width3;
            public bool HasThird;

            public string Label4 = "";
            public string Key4 = "";
            public int Width4;
            public bool HasFourth;

            // 第五个小格子（“在场”标记）
            public string Label5 = "";
            public string Key5 = "";
            public int Width5;
            public bool HasFifth;

            /// <summary>小格子的高亮色（阵营格用）。</summary>
            public Color Cell3Color = TextColor;
            public Color Cell4Color = TextColor;
            public Color Cell5Color = TextColor;

            /// <summary>两个输入框左边的小字说明（默认「总」「当前」）。</summary>
            public string Small1 = "总";
            public string Small2 = "当前";

            /// <summary>分栏符：从这一行开始换到右边一列。</summary>
            public bool IsColumnBreak;

            /// <summary>
            /// 大卡片行（战术面板第一页那种"阵营大牌"）：
            /// 画一个带主色的圆角块，里面是阵营名 + 一个超大数字。
            /// Label 当阵营名、Value 当那个大数字，CardColor 当主色。
            /// </summary>
            public bool IsCard;

            /// <summary>
            /// 卡片右上角画一个**齿轮**（操作者要求：发电机那块的设置入口）。
            /// 齿轮本身是独立可点的（用 <see cref="GearKey"/>），整张卡片也可以点。
            /// </summary>
            public bool ShowGear;
            public string GearKey = "";

            /// <summary>
            /// 卡片底部画一条进度条（BarMax &gt; 0 时才画，BarValue/BarMax 决定长度）。
            /// 发电机那张卡就用它显示"几台 / 共几台"。
            /// </summary>
            public int BarValue = -1;
            public int BarMax = 0;

            /// <summary>卡片主色 / 强调色（卡片行、彩色按钮都用它）。</summary>
            public Color CardColor = TextColor;

            /// <summary>分区标题行（带底色色带，用来分隔大块内容）。</summary>
            public bool IsSection;

            /// <summary>
            /// 这一行要**横跨整块面板**（所有列加起来那么宽）。
            ///
            /// 【用途】总览页底下那条"全人类有票概率" —— 它统计的是
            /// MTF 和混沌两边合起来的情况，所以宽度也要是两边加起来，
            /// 视觉上才对应得上（操作者专门要求的）。
            /// </summary>
            public bool IsWide;

            /// <summary>卡片右侧的附加说明（例如 "49 / 80"）。</summary>
            public string CardSub = "";

            /// <summary>按钮用主色填充（默认是灰色）。</summary>
            public bool ColoredButton;

            /// <summary>一行里横排多个按钮（记录目标用）。</summary>
            public bool IsButtonStrip;
            public bool IsHidden;
            public readonly List<ButtonSpec> Buttons = new List<ButtonSpec>();

            /// <summary>横排按钮上显示的次数（"击杀SCP ×3"），按按钮顺序对应。</summary>
            public int[] StripCounts;
        }

        private struct Hit
        {
            public int X, Y, W, H;
            public string Key;
            public int Field;   // 0 / 1 = 输入格，2 / 3 = 行内小格子，-1 = 按钮
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly Timer _caretTimer;
        private readonly Timer _animTimer;

        // ---- 开合动画 ----
        private float _anim = 1f;      // 0 = 完全收起，1 = 完全展开
        private bool _animIn;
        private bool _hiding;

        /// <summary>展开 / 收起动画时长（毫秒）。</summary>
        protected int AnimInMs = 170;
        protected int AnimOutMs = 130;

        /// <summary>收起时向下滑多少像素（展开时从上方滑入）。</summary>
        protected int AnimSlide = 16;

        private bool _clickThrough = true;
        private bool _caretOn = true;
        private int _focusRow = -1;
        private int _focusField;
        private int _caretPos;

        // ---- 全息展开动画（只有战术面板 UseHoloIntro = true 时启用）----
        /// <summary>这套动画只在战术面板上开：两根发光竖线 + 面板横向展开。</summary>
        protected bool UseHoloIntro;

        private HoloLineForm _holoTop, _holoBottom;
        private HoloLineForm _decorFlash;      // 数据变动时，左边装饰线闪一下（故意的）
        private Timer _decorFlashTimer;
        private int _decorFlashStart;
        private int _holoTickPending;
        private System.Threading.Thread _holoThread;
        private System.Threading.AutoResetEvent _holoWake;
        private volatile bool _holoThreadStop;
        private volatile int _holoState;          // 0=关 1=展开中 2=收起中 3=已展开（左边留装饰线）
        private double _holoProgress;    // 0 = 全关，1 = 全开
        private double _holoFrom;
        private double _holoDuration;
        private int _holoStart;
        private Rectangle _holoBox;      // 面板卡片的屏幕矩形（不含外面那圈透明边）
        private double _holoPulse;
        private int _revealW = -1;       // >0 = 只显示画布中间这么宽（-1 = 全宽）
        private float _revealAlpha = 1f;
        private bool _holoHiRes;         // 这一轮动画是不是已经进过高精度计时
        private bool _holoSurfaceStale = true;   // 画布要不要重画（内容 / 装饰线变化时）
        private int _holoLastClip = -1;  // 上一帧推送时的裁切宽度（没变就不重推）
        private float _holoLastAlpha = -1f;
        private int _holoFrameNo;
        private int _holoFrames;         // 本轮动画实际画了多少帧（结束写日志，方便查帧率）
        private bool _holoDecor;         // 展开完成后：左边那根线画进面板里（不再用独立窗口）

        // ---- 推屏缓存（极高档 120fps 的关键）----
        // 以前每帧都 Bitmap.GetHbitmap + CreateCompatibleDC + DeleteObject：
        // 一张 1000×800 的图每帧拷一遍，光 GDI 对象就够把帧率拖死。
        // 现在只有"画布重画过 / 尺寸变了"才重建，其余帧直接复用同一份位图。
        private int _surfaceVersion;
        private IntPtr _pushHBitmap = IntPtr.Zero;
        private IntPtr _pushMemDc = IntPtr.Zero;
        private IntPtr _pushOldBitmap = IntPtr.Zero;
        private int _pushW, _pushH, _pushVersion = -1;

        // DIB 画布：_surfaceBuf 直接长在这块内存上，推屏时把 _dibDc 交给 ULW，
        // 不用每帧 GetHbitmap 再拷一遍（切页 / 展开动画的高帧率全靠它）。
        private IntPtr _dibSection = IntPtr.Zero;
        private IntPtr _dibDc = IntPtr.Zero;
        private IntPtr _dibBits = IntPtr.Zero;
        private int _dibW, _dibH;
        private static readonly Color HoloColor = Color.FromArgb(150, 235, 255);

        /// <summary>正在播全息展开 / 收起（动画期间不要重画面板内容、也不刷新实时数据）。</summary>
        public bool IsHoloAnimating
        {
            get { return UseHoloIntro && (_holoState == 1 || _holoState == 2); }
        }

        /// <summary>面板是"开着 / 正在开"（热键切换用：展开到一半再按会反向收起）。</summary>
        public bool IsOpenOrOpening
        {
            get
            {
                if (UseHoloIntro && !UiAnim.Off)
                    return _holoState == 1 || _holoState == 3;
                return Visible && Interactive;
            }
        }

        /// <summary>正在播收起动画。</summary>
        public bool IsClosingHolo
        {
            get { return UseHoloIntro && _holoState == 2; }
        }

        private IntPtr _previousForeground = IntPtr.Zero;

        /// <summary>面板当前是否处于“接收输入”状态（未穿透）。</summary>
        public bool Interactive
        {
            get { return !_clickThrough; }
        }

        public event Action Submitted;
        public event Action Cancelled;

        protected OverlayPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _caretTimer = new Timer();
            _caretTimer.Interval = 530;
            _caretTimer.Tick += delegate { _caretOn = !_caretOn; Redraw(); };

            _animTimer = new Timer();
            _animTimer.Interval = 16;
            _animTimer.Tick += OnAnimTick;
        }

        /// <summary>展开 / 收起动画。</summary>
        private void OnAnimTick(object sender, EventArgs e)
        {
            float step = _animIn
                ? (float)_animTimer.Interval / Math.Max(40f, AnimInMs)
                : -(float)_animTimer.Interval / Math.Max(40f, AnimOutMs);

            _anim += step;

            if (_anim >= 1f)
            {
                _anim = 1f;
                _animTimer.Stop();
            }
            else if (_anim <= 0f)
            {
                _anim = 0f;
                _animTimer.Stop();
                if (_hiding)
                {
                    _hiding = false;
                    if (Visible) Hide();
                    return;
                }
            }

            Redraw();
        }

        /// <summary>缓动：ease-out cubic。</summary>
        private static float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }   // 面板需要被激活才能收键盘
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
                // 刻意不加 WS_EX_NOACTIVATE（面板要能拿焦点），
                // 也不加 WS_EX_TRANSPARENT（那个位由 UpdateExStyle 按状态控制）
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateExStyle();

            // 面板压在识别区域上时不能被 OCR 自己截进去
            NativeMethods.HideFromCapture(Handle, "悬浮面板");
        }

        /// <summary>
        /// 鼠标穿透开关。
        /// true  = 加上 WS_EX_TRANSPARENT，点击穿过面板
        /// false = 去掉，面板能接收鼠标点击
        /// </summary>
        private void UpdateExStyle()
        {
            if (!IsHandleCreated) return;
            int ex = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
            int want = ex | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
            want &= ~NativeMethods.WS_EX_NOACTIVATE;
            if (_clickThrough) want |= NativeMethods.WS_EX_TRANSPARENT;
            else want &= ~NativeMethods.WS_EX_TRANSPARENT;
            if (want != ex)
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, want);
        }

        public void ReassertExStyle()
        {
            UpdateExStyle();
        }

        #region 行定义

        protected Row AddHeader(string text)
        {
            Row r = new Row(); r.Label = text; r.IsHeader = true; _rows.Add(r); return r;
        }

        protected Row AddNote(string text)
        {
            Row r = new Row(); r.Label = text; r.IsNote = true; _rows.Add(r); return r;
        }

        protected Row AddInput(string label, string key, string value, int width)
        {
            Row r = new Row();
            r.Label = label; r.Value = value ?? ""; r.IsInput = true;
            r.Key = key; r.Width = width;
            _rows.Add(r);
            return r;
        }

        /// <summary>一行里放两个输入框。</summary>
        protected Row AddInput2(string label, string label2,
                                string key, string value, int width,
                                string key2, string value2, int width2)
        {
            Row r = new Row();
            r.Label = label; r.Key = key; r.Value = value ?? ""; r.Width = width;
            r.HasSecond = true;
            r.Label2 = label2; r.Key2 = key2; r.Value2 = value2 ?? ""; r.Width2 = width2;
            r.IsInput = true;
            _rows.Add(r);
            return r;
        }

        /// <summary>两个输入框 + 两个行内可点小格子（战术面板用）。</summary>
        protected Row AddInput4(string label,
                                string key, string value, int width,
                                string key2, string value2, int width2,
                                string label3, string key3, int width3,
                                string label4, string key4, int width4)
        {
            Row r = AddInput2(label, "", key, value, width, key2, value2, width2);
            r.Label2 = "";
            r.HasThird = true; r.Label3 = label3 ?? ""; r.Key3 = key3; r.Width3 = width3;
            r.HasFourth = true; r.Label4 = label4 ?? ""; r.Key4 = key4; r.Width4 = width4;
            return r;
        }

        /// <summary>两个输入框 + 三个行内可点小格子（战术面板：阵营 / 击杀 / 在场）。</summary>
        protected Row AddInput5(string label,
                                string key, string value, int width,
                                string key2, string value2, int width2,
                                string label3, string key3, int width3,
                                string label4, string key4, int width4,
                                string label5, string key5, int width5)
        {
            Row r = AddInput4(label, key, value, width, key2, value2, width2,
                              label3, key3, width3, label4, key4, width4);
            r.HasFifth = true; r.Label5 = label5 ?? ""; r.Key5 = key5; r.Width5 = width5;
            return r;
        }

        /// <summary>换到下一列（横向分栏，不用一路往下堆）。</summary>
        protected void AddColumnBreak()
        {
            Row r = new Row();
            r.IsColumnBreak = true;
            _rows.Add(r);
        }

        /// <summary>一行只有一个行内可点小格子（大波阵营这种）。</summary>
        protected Row AddCellRow(string label, string key, string text, int width)
        {
            Row r = new Row();
            r.Label = label;
            r.IsInput = false;
            r.HasThird = true;
            r.Key3 = key;
            r.Label3 = text ?? "";
            r.Width3 = width;
            _rows.Add(r);
            return r;
        }

        /// <summary>一行带**两个**行内可点小格子（待归属阵亡的 MTF / 混沌）。</summary>
        protected Row AddCellRow2(string label,
                                  string key3, string text3, int width3,
                                  string key4, string text4, int width4)
        {
            Row r = AddCellRow(label, key3, text3, width3);
            r.HasFourth = true;
            r.Label4 = "";
            r.Key4 = key4;
            r.Label4 = text4 ?? "";
            r.Width4 = width4;
            return r;
        }

        /// <summary>改行内小格子的显示文字（和颜色）。</summary>
        protected void SetCell(string key, string text, Color color)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.HasThird && r.Key3 == key) { r.Label3 = text; r.Cell3Color = color; return; }
                if (r.HasFourth && r.Key4 == key) { r.Label4 = text; r.Cell4Color = color; return; }
                if (r.HasFifth && r.Key5 == key) { r.Label5 = text; r.Cell5Color = color; return; }
            }
        }

        protected Row AddButton(string label, string key)
        {
            Row r = new Row(); r.Label = label; r.IsButton = true; r.Key = key;
            _rows.Add(r); return r;
        }

        /// <summary>只读结果行（左边标题，右边数值）。extra 版本不带 Key 时用索引。</summary>
        protected Row AddReadout(string label, string key)
        {
            Row r = new Row();
            r.Label = label; r.IsReadout = true; r.Key = key;
            _rows.Add(r); return r;
        }

        /// <summary>
        /// 分区标题：一条带底色的色带 + 左侧主色块，比普通小标题醒目得多。
        /// 用来把面板切成"一眼能分清"的几块（像阵营牌那样的分区感）。
        /// </summary>
        protected Row AddSection(string title, Color color)
        {
            Row r = new Row();
            r.IsSection = true;
            r.Label = title ?? "";
            r.CardColor = color;
            _rows.Add(r);
            return r;
        }

        /// <summary>
        /// 大卡片行：主色圆角块 + 阵营名 + 超大数字（战术面板第一页那种阵营牌）。
        /// </summary>
        protected Row AddCard(string label, string value, string sub, Color color)
        {
            Row r = new Row();
            r.IsCard = true;
            r.Label = label ?? "";
            r.Value = value ?? "";
            r.CardSub = sub ?? "";
            r.CardColor = color;
            _rows.Add(r);
            return r;
        }

        /// <summary>
        /// 横跨整块面板的大牌（总览页底下的"全人类有票概率"用）。
        ///
        /// 【为什么要整块宽】这一条统计的是 MTF + 混沌**两边合起来**的情况，
        /// 所以宽度也要等于两边之和，视觉上才对得上（操作者要求）。
        /// 用法：把它单独放在一列的末尾（那一列只有它一个 Wide 行时才会拉满）。
        /// </summary>
        protected Row AddWideCard(string label, string value, string sub, Color color)
        {
            Row r = AddCard(label, value, sub, color);
            r.IsWide = true;
            return r;
        }

        /// <summary>彩色按钮（记录目标用：颜色区分是哪一方的目标）。</summary>
        protected Row AddColorButton(string label, string key, Color color)
        {
            Row r = AddButton(label, key);
            r.ColoredButton = true;
            r.CardColor = color;
            return r;
        }

        /// <summary>
        /// 一行里横排若干个彩色按钮（记录目标用，省纵向空间）。
        /// items 是 label / key / ARGB 的三元组，例如
        ///     AddButtonStrip("击杀SCP", "obj_kill", unchecked((int)0xFF6D9FF7))
        /// </summary>
        protected Row AddButtonStrip(params object[] items)
        {
            Row r = new Row();
            r.IsButtonStrip = true;
            for (int i = 0; i + 2 < items.Length; i += 3)
            {
                ButtonSpec b = new ButtonSpec();
                b.Label = items[i] == null ? "" : items[i].ToString();
                b.Key = items[i + 1] == null ? "" : items[i + 1].ToString();
                int argb = unchecked((int)0xFFFFFFFF);
                try { argb = Convert.ToInt32(items[i + 2]); } catch { }
                b.Color = Color.FromArgb(argb);
                r.Buttons.Add(b);
            }
            _rows.Add(r);
            return r;
        }

        /// <summary>隐藏或显示动态创建的按钮行。</summary>
        protected void SetRowHidden(string key, bool hidden)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.Key == key || r.Key2 == key || r.Key3 == key || r.Key4 == key || r.Key5 == key)
                { r.IsHidden = hidden; return; }
                for (int b = 0; b < r.Buttons.Count; b++)
                    if (r.Buttons[b].Key == key) { r.IsHidden = hidden; return; }
            }
        }

        /// <summary>更新动态按钮行中的标签和颜色。</summary>
        protected void SetButtonStripItem(string key, string label, Color color)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                for (int b = 0; b < r.Buttons.Count; b++)
                    if (r.Buttons[b].Key == key)
                    { r.Buttons[b].Label = label ?? ""; r.Buttons[b].Color = color; return; }
            }
        }

        /// <summary>横排按钮里的一个按钮。</summary>
        protected class ButtonSpec
        {
            public string Label = "";
            public string Key = "";
            public Color Color = TextColor;
        }

        /// <summary>把 Row 里的 Key 设成"整张卡点一下去哪里"（配合 ShowGear 用）。</summary>
        protected Row SetCardKey(Row r, string key)
        {
            if (r != null) r.Key = key == null ? "" : key;
            return r;
        }

        /// <summary>
        /// 画一个简单的**齿轮**：一圈 + 8 个齿 + 中心点。
        /// 用线条画（不是填充）—— 这样不用去猜卡片底色，任何卡上都不会出现色块。
        /// </summary>
        private static void DrawGear(Graphics g, Rectangle box, Color color)
        {
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            float rOut = Math.Min(box.Width, box.Height) / 2f;
            float rRing = rOut * 0.62f;
            float rHole = rOut * 0.20f;

            using (Pen p = new Pen(color, 2f))
            {
                g.DrawEllipse(p, cx - rRing, cy - rRing, rRing * 2, rRing * 2);
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * 2 * i / 8.0;
                    float x1 = (float)(cx + Math.Cos(a) * rRing);
                    float y1 = (float)(cy + Math.Sin(a) * rRing);
                    float x2 = (float)(cx + Math.Cos(a) * rOut);
                    float y2 = (float)(cy + Math.Sin(a) * rOut);
                    g.DrawLine(p, x1, y1, x2, y2);
                }
                using (SolidBrush b = new SolidBrush(color))
                    g.FillEllipse(b, cx - rHole, cy - rHole, rHole * 2, rHole * 2);
            }
        }

        protected void ClearRows()
        {
            _rows.Clear();
            _hits.Clear();
            _focusRow = -1;
            _focusField = 0;
            _caretPos = 0;
        }

        #endregion

        #region 分页页签

        /// <summary>
        /// 页签名字。非空时面板顶部会画一条页签栏，点页签切页。
        ///
        /// 切页时基类会 ClearRows() 再调一次 BuildRows()，
        /// 所以子类只要在 BuildRows 里按 <see cref="CurrentTab"/> 决定画哪一页就行。
        /// </summary>
        protected string[] Tabs;

        /// <summary>当前页签下标（从 0 开始）。</summary>
        protected int CurrentTab;

        /// <summary>切页之后调一次，让子类把该页的数据填上。</summary>
        protected virtual void OnTabChanged() { }

        /// <summary>
        /// 切页**之前**调一次，让子类把当前页输入框里的内容收进模型。
        ///
        /// 【为什么必须有】输入框的值只活在当前页的 Row 里，切页会 ClearRows，
        /// 没先收走就等于用户刚填的东西被悄悄丢掉。
        /// </summary>
        protected virtual void FlushInputs() { }

        /// <summary>切到某一页（越界会被忽略）。</summary>
        protected void GoTab(int i)
        {
            if (Tabs == null || Tabs.Length == 0) return;
            if (i < 0 || i >= Tabs.Length) return;
            if (i == CurrentTab) return;

            int oldTab = CurrentTab;

            // 先给"旧页"拍一张快照（要在 ClearRows 之前）
            Bitmap from = null;
            if (CanTabCarousel() && Visible)
            {
                try
                {
                    BuildSurface();
                    from = (Bitmap)_surfaceBuf.Clone();
                }
                catch { from = null; }
            }

            try { FlushInputs(); } catch { }     // 先把当前页的数据收走
            CurrentTab = i;
            ClearRows();
            BuildRows();
            OnTabChanged();

            int diff = Math.Abs(i - oldTab);
            Logger.On("面板", "切页 " + (Tabs[oldTab] ?? "") + " → " + (Tabs[i] ?? "")
                + "（跨 " + diff + " 页）");

            // 【稳定版】立即切页 + 方向感知的内容滑入；框架（标题/页签/左边条/提示）不动。
            // 之前"框架常驻 + 内容带轮播"那套在真机上反复出现"点页签有反馈但页不切/卡住"，
            // 先退回这一版，保证切页 100% 生效。复杂的轮播以后单独做开关再加。
            if (from != null) { try { from.Dispose(); } catch { } }
            StartTabAnim(i > oldTab ? 1 : -1);        }

        // ============================================================
        // 切页"轮播"动画
        //
        // 操作者要求：切到右边的页 → 整个内容往左滑（新页从右边进来）；
        // 中间隔着几页的话，滑的过程中也要能看到它们过去；
        // 滑完之后左边的装饰线闪一下。
        // ============================================================

        private Bitmap _tabFrom, _tabTo, _tabFrame;
        private readonly List<Bitmap> _tabMids = new List<Bitmap>();
        private int _tabDir;
        private int _tabSteps;
        private double _tabStart;
        private double _tabDuration;
        private volatile bool _tabCarousel;
        private int _tabTickPending;
        private int _tabFrameCount;
        private int _lastLayeredUpdateError;
        private Timer _tabGuard;         // 兜底：动画该结束还没结束（异常 / 丢帧）就强制收尾
        private Timer _tabCarTimer;      // 切页轮播的定时器（普通定拍，最高约 60fps）

        /// <summary>轮播动画的时长：一页 300ms，中间每多一页 +90ms（上限 700ms）。</summary>
        private static double TabCarouselMs(int steps)
        {
            double ms = 300 + (steps - 1) * 90.0;
            return ms > 700 ? 700 : ms;
        }

        private bool CanTabCarousel()
        {
            return TabAnimMs > 0 && UiAnim.Quality > 0 && _surfaceBuf != null;
        }

        /// <summary>正在播切页轮播动画（这期间不要重画面板，动画自己接管）。</summary>
        public bool IsTabAnimating
        {
            get { return _tabCarousel; }
        }

        private bool StartTabCarousel(Bitmap from, Bitmap to, int oldTab, int newTab, int diff)
        {
            StopTabCarousel(true);

            int dir = newTab > oldTab ? 1 : -1;   // 新页在右 → 内容往左走
            Rectangle cr = _contentRect;
            if (cr.Width < 8 || cr.Height < 8)
            {
                from.Dispose();
                to.Dispose();
                return false;
            }

            try
            {
                // 中间那些页：占位卡片（不建行、不动模型）
                List<Bitmap> mids = new List<Bitmap>();
                for (int k = 1; k < diff; k++)
                {
                    int idx = oldTab + dir * k;
                    string name = (idx >= 0 && idx < Tabs.Length) ? Tabs[idx] : "";
                    mids.Add(RenderTabPlaceholder(name, from.Size));
                }

                _tabFrom = from;
                _tabTo = to;
                _tabFrame = RenderFrameOnly();     // 框架快照（内容区透明），每帧盖在最上面
                _tabMids.Clear();
                _tabMids.AddRange(mids);
                _tabDir = dir;
                _tabSteps = diff;
                _tabDuration = TabCarouselMs(diff);
                _tabStart = Environment.TickCount;
                _tabFrameCount = 0;
                _tabAnim = 1f;
                _animDx = 0;
                _revealW = -1;
                _revealAlpha = 1f;
                _tabCarousel = true;

                // 用普通 WinForms 定时器驱动（最高约 60fps）。
                // 以前用"高精度线程 + 120fps"驱动切页，会把 UI 线程占满，
                // 真机上反而表现为"动画卡住、整体点不动"；这里封顶 60fps。
                if (_tabCarTimer == null)
                {
                    _tabCarTimer = new Timer();
                    _tabCarTimer.Tick += OnTabCarTick;
                }
                _tabCarTimer.Interval = Math.Max(16, UiAnim.IntervalMs);
                _tabCarTimer.Start();

                if (_tabGuard == null)
                {
                    _tabGuard = new Timer();
                    _tabGuard.Interval = 200;
                    _tabGuard.Tick += delegate
                    {
                        if (!_tabCarousel) { _tabGuard.Stop(); return; }
                        if (Environment.TickCount - _tabStart > _tabDuration + 800)
                        {
                            Logger.On("面板", "切页动画超时没收尾 → 强制结束（避免卡住）");
                            try { FinishTabCarousel(); } catch { }
                        }
                    };
                }
                _tabGuard.Start();

                OnTabCarTick(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Err("面板", "切页动画启动失败 → 直接切页", ex);
                try { StopTabCarousel(true); } catch { }
                try
                {
                    BuildSurface();
                    PushToScreen(_surfaceBuf);
                }
                catch { }
                return false;
            }
        }

        private void OnTabCarTick(object sender, EventArgs e)
        {
            if (!_tabCarousel) return;
            _tabFrameCount++;
            UiAnim.Ticks++;
            long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                TickTabCarousel();
            }
            catch (Exception ex)
            {
                Logger.Err("面板", "切页动画出错 → 直接收尾", ex);
                try { FinishTabCarousel(); } catch { }
            }
            UiAnim.TabWorkMs += (long)Math.Round(
                (System.Diagnostics.Stopwatch.GetTimestamp() - __t0) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency);
            UiAnim.TabWorkFrames++;
        }

        private void TickTabCarousel()
        {
            double t = UiAnim.Clamp01((Environment.TickCount - _tabStart) / _tabDuration);
            double eased = UiAnim.EaseInOutQuad(t);

            int w = _surfaceBuf.Width;
            int h = _surfaceBuf.Height;
            Rectangle cr = _contentRect;

            using (Graphics g = Graphics.FromImage(_surfaceBuf))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                // ① 动的内容：只画内容区，逐页按进度贴（框架本身不动）
                g.SetClip(cr);
                // 每一页的滑动距离应等于实际内容区宽度；整个画布还包含框架边距。
                double travel = eased * _tabSteps * _tabDir * cr.Width;
                for (int step = 0; step <= _tabSteps; step++)
                {
                    Bitmap page;
                    if (step == 0) page = _tabFrom;
                    else if (step == _tabSteps) page = _tabTo;
                    else
                    {
                        int mi = step - 1;
                        if (mi < 0 || mi >= _tabMids.Count) continue;
                        page = _tabMids[mi];
                    }

                    int x = (int)Math.Round(step * _tabDir * cr.Width - travel);
                    if (x + cr.Right <= cr.Left || x + cr.Left >= cr.Right) continue;
                    g.DrawImage(page,
                        new Rectangle(x + cr.X, cr.Y, cr.Width, cr.Height),
                        cr, GraphicsUnit.Pixel);
                }
                g.ResetClip();

                // ② 静止的框架：上/下横带 + 左右竖条盖上去（内容区已经被抠透明）
                if (_tabFrame != null)
                {
                    DrawFramePart(g, _tabFrame, new Rectangle(0, 0, w, cr.Top));
                    DrawFramePart(g, _tabFrame, new Rectangle(0, cr.Bottom, w, h - cr.Bottom));
                    if (cr.Left > 0)
                        DrawFramePart(g, _tabFrame, new Rectangle(0, cr.Top, cr.Left, cr.Height));
                    if (cr.Right < w)
                        DrawFramePart(g, _tabFrame, new Rectangle(cr.Right, cr.Top, w - cr.Right, cr.Height));
                }
            }

            _surfaceVersion++;      // 组合出来的这一帧是新内容，推屏缓存要重建
            PushToScreen(_surfaceBuf);

            if (t >= 1.0) FinishTabCarousel();
        }

        private static void DrawFramePart(Graphics g, Bitmap frame, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            g.DrawImage(frame, r, r, GraphicsUnit.Pixel);
        }

        private void FinishTabCarousel()
        {
            StopTabCarousel(true);
            try
            {
                BuildSurface();              // 换成"活"的新页
                PushToScreen(_surfaceBuf);
                Logger.On("面板", "切页动画结束（帧数 " + _tabFrameCount + "）→ 当前页「"
                    + (Tabs != null && CurrentTab >= 0 && CurrentTab < Tabs.Length ? Tabs[CurrentTab] : "") + "」");
            }
            catch (Exception ex)
            {
                Logger.Err("面板", "切页动画收尾失败", ex);
            }
            // 滑完：左边装饰线闪一下（"换页了/更新了"的提示）
            try { FlashDecorLine(); } catch { }
        }

        private void StopTabCarousel(bool disposePages)
        {
            _tabCarousel = false;
            System.Threading.Interlocked.Exchange(ref _tabTickPending, 0);
            if (_tabGuard != null) _tabGuard.Stop();
            if (_tabCarTimer != null) _tabCarTimer.Stop();
            LeaveHoloHighRes();
            if (disposePages)
            {
                if (_tabFrom != null) { try { _tabFrom.Dispose(); } catch { } _tabFrom = null; }
                if (_tabTo != null) { try { _tabTo.Dispose(); } catch { } _tabTo = null; }
                if (_tabFrame != null) { try { _tabFrame.Dispose(); } catch { } _tabFrame = null; }
                for (int i = 0; i < _tabMids.Count; i++) { try { _tabMids[i].Dispose(); } catch { } }
                _tabMids.Clear();
            }
        }

        /// <summary>中间页的"路过"卡片：深色圆角 + 页名 + 一点装饰（不建行、不动模型）。</summary>
        private Bitmap RenderTabPlaceholder(string name, Size sz)
        {
            Bitmap bmp = new Bitmap(sz.Width, sz.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                int titleH = string.IsNullOrEmpty(PanelTitle) ? 0 : 36;
                int tabH = TabBarHeight;
                int boxH = sz.Height - Pad * 2;
                Rectangle box = new Rectangle(Pad, Pad, sz.Width - Pad * 2, boxH);

                using (GraphicsPath gp = AlertRenderer.RoundedRect(box, 16))
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(246, BoxColor)))
                        g.FillPath(sb, gp);
                    using (Pen p = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
                }
                using (SolidBrush sb = new SolidBrush(AccentColor))
                    g.FillRectangle(sb, box.X, box.Y + 1, 4, box.Height - 2);

                // 页名放中间，低调一点
                using (Font big = new Font(TextFont.FontFamily, 26f, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(220, TextColor)))
                        g.DrawString(name ?? "", big, sb, box, sf);
                }
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    using (SolidBrush sb = new SolidBrush(DimColor))
                        g.DrawString("…", SmallFont, sb,
                            new RectangleF(box.X, box.Y + box.Height * 0.60f, box.Width, 30), sf);
                }
            }
            return bmp;
        }

        // ---- 切页动画 ----
        private float _tabAnim = 1f;
        private Timer _tabAnimTimer;

        /// <summary>切页动画时长（毫秒）。0 = 关掉动画。</summary>
        protected int TabAnimMs = 200;

        /// <summary>切页动画的进度（0 = 刚开始，1 = 完成）。绘制时用。</summary>
        protected float TabAnimProgress
        {
            get { return _tabAnim >= 1f ? 1f : _tabAnim; }
        }

        /// <summary>切页滑入方向：+1 = 新页在右（内容从右边滑进来），-1 = 在左。</summary>
        private int _tabSlideDir = 1;

        private void StartTabAnim(int dir)
        {
            _tabSlideDir = dir >= 0 ? 1 : -1;
            if (TabAnimMs <= 0 || UiAnim.Off) { _tabAnim = 1f; Redraw(); return; }
            if (_tabAnimTimer == null)
            {
                _tabAnimTimer = new Timer();
                _tabAnimTimer.Interval = Math.Max(8, UiAnim.IntervalMs);
                _tabAnimTimer.Tick += delegate
                {
                    _tabAnim += (float)(_tabAnimTimer.Interval / Math.Max(60.0, (double)TabAnimMs));
                    if (_tabAnim >= 1f)
                    {
                        _tabAnim = 1f;
                        _tabAnimTimer.Stop();
                        // 滑完：左边装饰线闪一下（提示"换页了"）
                        try { FlashDecorLine(); } catch { }
                    }
                    Redraw();
                };
            }
            _tabAnim = 0f;
            _tabAnimTimer.Interval = Math.Max(8, UiAnim.IntervalMs);
            _tabAnimTimer.Start();
            Redraw();
        }

        /// <summary>页签栏高度（没有页签就是 0）。</summary>
        private int TabBarHeight
        {
            get { return (Tabs != null && Tabs.Length > 0) ? 38 : 0; }
        }

        /// <summary>画页签栏，并把可点区域记进 _hits（Key 用 "tab:N"）。</summary>
        private void DrawTabs(Graphics g, int boxX, int y, int innerW)
        {
            if (Tabs == null || Tabs.Length == 0) return;

            int x = boxX + 18;
            for (int i = 0; i < Tabs.Length; i++)
            {
                string name = Tabs[i] == null ? "" : Tabs[i];
                SizeF sz = g.MeasureString(name, TextFont);
                int w = (int)sz.Width + 26;
                Rectangle r = new Rectangle(x, y, w, 28);

                bool on = (i == CurrentTab);
                using (GraphicsPath gp = AlertRenderer.RoundedRect(r, 7))
                {
                    using (SolidBrush sb = new SolidBrush(on
                        ? Color.FromArgb(210, AccentColor)
                        : Color.FromArgb(90, 60, 70, 88)))
                        g.FillPath(sb, gp);
                    using (Pen p = new Pen(Color.FromArgb(on ? 140 : 60, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
                }
                using (SolidBrush sb = new SolidBrush(on ? Color.White : DimColor))
                    g.DrawString(name, TextFont, sb, x + 13, y + 4);

                Hit h = new Hit();
                h.X = r.X; h.Y = r.Y; h.W = r.Width; h.H = r.Height;
                h.Key = "tab:" + i; h.Field = -1;
                _hits.Add(h);

                x += w + 8;
            }
        }

        /// <summary>页签点击（在普通按钮之前处理）。返回 true = 已经吃掉了这次点击。</summary>
        private bool HandleTabClick(MouseEventArgs e)
        {
            int idx;
            if (!TryGetTabClickIndex(e, out idx)) return false;
            Logger.On("面板", "命中页签 " + idx
                + "（当前 " + CurrentTab + "）");
            GoTab(idx);
            return true;
        }

        /// <summary>只解析页签点击，供切页动画期间先完成过渡再处理新目标。</summary>
        private bool TryGetTabClickIndex(MouseEventArgs e, out int index)
        {
            index = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                Hit h = _hits[i];
                if (h.Field != -1 || h.Key == null || !h.Key.StartsWith("tab:")) continue;
                if (e.X < h.X || e.X > h.X + h.W || e.Y < h.Y || e.Y > h.Y + h.H) continue;
                return int.TryParse(h.Key.Substring(4), out index);
            }
            return false;
        }

        #endregion

        #region 行定义（续）

        /// <summary>把最后那条说明行改成别的文字（预警之类）。</summary>
        protected void SetLastNote(string text)
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (_rows[i].IsNote) { _rows[i].Label = text; return; }
        }

        /// <summary>直接按引用改写一条说明行（不怕布局重排）。</summary>
        protected void SetNote(Row r, string text)
        {
            if (r == null) return;
            r.Label = text;
        }

        /// <summary>按“第几条说明行”改写文字（从 1 开始数）。</summary>
        protected void SetNoteAt(int index, string text)
        {
            int seen = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsNote) continue;
                seen++;
                if (seen == index) { _rows[i].Label = text; return; }
            }
        }

        /// <summary>把最后一行（不管什么类型）的文字改掉，用于实时预览。</summary>
        protected void SetLastRowLabel(string text)
        {
            if (_rows.Count == 0) return;
            _rows[_rows.Count - 1].Label = text;
        }

        /// <summary>行数（子类做布局判断用）。</summary>
        protected int RowCount
        {
            get { return _rows.Count; }
        }

        protected string GetValue(string key)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsInput) continue;
                if (_rows[i].Key == key) return _rows[i].Value;
                if (_rows[i].HasSecond && _rows[i].Key2 == key) return _rows[i].Value2;
            }
            return "";
        }

        /// <summary>
        /// 这一页上到底有没有这个输入格。
        ///
        /// 【为什么必须有】<see cref="GetValue"/> 对"不存在的格子"和"格子被清空"
        /// 都返回空串，分不清这两种情况。而面板的行是**按页懒建**的：
        /// 只按 GetValue 的结果去改模型的话，切到别的页就等于"把那一页没有的
        /// 数据全清空了"。所有"从输入框写回模型"的地方都要先用它问一句。
        /// </summary>
        protected bool HasInput(string key)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsInput) continue;
                if (_rows[i].Key == key) return true;
                if (_rows[i].HasSecond && _rows[i].Key2 == key) return true;
            }
            return false;
        }

        protected void SetValue(string key, string value)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsInput) continue;
                if (_rows[i].Key == key) { _rows[i].Value = value ?? ""; return; }
                if (_rows[i].HasSecond && _rows[i].Key2 == key) { _rows[i].Value2 = value ?? ""; return; }
            }
        }

        /// <summary>设置只读结果行的显示值。</summary>
        protected void SetReadout(string key, string text)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].IsReadout && _rows[i].Key == key) { _rows[i].Value = text ?? ""; return; }
        }

        /// <summary>
        /// 当前获得焦点的输入框的 key（没有就返回空串）。
        /// 子类用它判断"用户正在编辑哪一格" —— 比如发电机在跑的时候，
        /// 面板会定时把实时剩余秒数刷回输入框，但绝不能抢用户正在打字的那一格。
        /// </summary>
        protected string FocusedKey
        {
            get
            {
                if (_focusRow < 0 || _focusRow >= _rows.Count) return "";
                Row r = _rows[_focusRow];
                if (!r.IsInput) return "";
                if (_focusField == 1 && r.HasSecond) return r.Key2;
                return r.Key;
            }
        }

        /// <summary>这一格是不是正被用户编辑。</summary>
        protected bool HasFocus(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return string.Equals(FocusedKey, key, StringComparison.Ordinal);
        }

        /// <summary>取当前获得焦点的输入框的值（提交时用）。</summary>
        protected string FocusedValue        {
            get
            {
                if (_focusRow < 0 || _focusRow >= _rows.Count) return "";
                Row r = _rows[_focusRow];
                if (!r.IsInput) return "";
                return _focusField == 1 && r.HasSecond ? r.Value2 : r.Value;
            }
        }

        #endregion

        #region 焦点移动

        private void MoveFocus(int dir)
        {
            // 把“行 + 格”展平成一个线性序列再前后走
            List<int[]> order = new List<int[]>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsInput) continue;
                order.Add(new int[] { i, 0 });
                if (_rows[i].HasSecond) order.Add(new int[] { i, 1 });
            }
            if (order.Count == 0) return;

            int cur = -1;
            for (int k = 0; k < order.Count; k++)
                if (order[k][0] == _focusRow && order[k][1] == _focusField) { cur = k; break; }

            int next = cur + dir;
            if (next < 0) next = order.Count - 1;
            if (next >= order.Count) next = 0;

            _focusRow = order[next][0];
            _focusField = order[next][1];
            _caretPos = CurrentValue.Length;
            _caretOn = true;
            Redraw();
        }

        protected void FocusFirstInput()
        {
            _focusRow = -1;
            _focusField = 0;
            MoveFocus(1);
        }

        private string CurrentValue
        {
            get
            {
                if (_focusRow < 0 || _focusRow >= _rows.Count) return "";
                Row r = _rows[_focusRow];
                if (!r.IsInput) return "";
                return _focusField == 1 && r.HasSecond ? r.Value2 : r.Value;
            }
            set
            {
                if (_focusRow < 0 || _focusRow >= _rows.Count) return;
                Row r = _rows[_focusRow];
                if (!r.IsInput) return;
                if (_focusField == 1 && r.HasSecond) r.Value2 = value;
                else r.Value = value;
            }
        }

        #endregion

        #region 显示 / 隐藏（焦点管理）

        /// <summary>呼出面板：解除穿透、拿到键盘焦点。</summary>
        public void ShowPanel()
        {
            if (_rows.Count == 0) BuildRows();

            // 窗口标题：方便无障碍工具 / 任务视图识别，也便于排查问题
            Text = PanelTitle;
            Name = PanelTitle;

            // 【全息展开】战术面板专用（两根发光竖线 + 横向展开，1.5~2 秒）
            if (UseHoloIntro && !UiAnim.Off)
            {
                // 之前是「动画效果=关」直接显示出来的：现在改成要动画，
                // 就当成"已经全开"，别再补播一遍展开（否则会闪一下）。
                if (_holoState == 0 && Visible)
                {
                    _holoState = 3;
                    _holoProgress = 1;
                }
                StartHoloOpen();
                return;
            }

            if (!Visible) Show();
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

            // 关键顺序：先解除穿透，再抢焦点。
            // 反过来的话，抢焦点那一瞬间窗口还是穿透的，点击会漏进游戏。
            _clickThrough = false;
            UpdateExStyle();

            // 「动画效果 = 关」：不播动画，直接出来（但焦点 / 输入照常）
            if (UiAnim.Off)
            {
                _animTimer.Stop();
                _anim = 1f;
                _animIn = true;
                Redraw();
                NativeMethods.ForceFocus(Handle);
                FocusFirstInput();
                _caretTimer.Start();
                return;
            }

            // 开合动画：淡入 + 从上方滑下来
            _hiding = false;
            _anim = 0f;
            _animIn = true;
            _animTimer.Interval = UiAnim.IntervalMs;
            _animTimer.Start();
            Redraw();

            // 面板刚显示完，WinForms 会照 CreateParams 再套一次样式，这里补一刀
            try { BeginInvoke((MethodInvoker)delegate { UpdateExStyle(); }); } catch { }

            NativeMethods.ForceFocus(Handle);

            // 【补一次焦点】有的游戏（或输入法）会在我们抢到焦点之后又把前台抢回去，
            // 表现就是"面板开着，但敲键盘没反应"。过 120ms 再确认一次：
            // 面板还开着、还没被收起、前台不是它 → 再夺一次焦点。
            try
            {
                Timer refocus = new Timer();
                refocus.Interval = 120;
                refocus.Tick += delegate
                {
                    refocus.Stop();
                    refocus.Dispose();
                    if (!Visible || _clickThrough) return;
                    if (NativeMethods.GetForegroundWindow() != Handle)
                        NativeMethods.ForceFocus(Handle);
                };
                refocus.Start();
            }
            catch { }

            FocusFirstInput();
            _caretTimer.Start();
        }

        /// <summary>
        /// 收起面板：立刻恢复鼠标穿透、把焦点还给原来的窗口，然后播收起动画。
        /// 焦点是马上还的，不等动画 —— 免得游戏有一小段时间收不到按键。
        /// </summary>
        public void HidePanel()
        {
            _caretTimer.Stop();

            IntPtr prev = _previousForeground;

            _clickThrough = true;
            UpdateExStyle();

            // 焦点先交回去，游戏才不会按键失灵（全息动画也一样：动画不抢焦点）
            if (prev != IntPtr.Zero && prev != Handle)
                NativeMethods.ForceFocus(prev);
            _previousForeground = IntPtr.Zero;

            // 【全息收起】从当前进度反向播；放到一半按热键也能接着反向，不会打架
            if (UseHoloIntro && !UiAnim.Off && _holoState != 0)
            {
                StartHoloClose();
                return;
            }

            // 「动画效果 = 关」：直接收掉
            if (UiAnim.Off)
            {
                _animTimer.Stop();
                _anim = 0f;
                _hiding = false;
                CleanHolo();               // 万一之前是全息动画开着，把线和状态一起清掉
                if (Visible) Hide();
                return;
            }

            // 再播收起动画，动画放完才真正 Hide
            _hiding = true;
            _animIn = false;
            _animTimer.Interval = UiAnim.IntervalMs;
            _animTimer.Start();
            Redraw();
        }

        // ============================================================
        // 全息展开 / 收起（战术面板）
        // ============================================================

        /// <summary>把面板画布的实际尺寸量出来（不显示窗口）。</summary>
        private Size MeasureSurface()
        {
            Bitmap b = BuildSurface();
            return b.Size;
        }

        private void EnsureHoloTimer()
        {
            if (_holoThread != null && _holoThread.IsAlive) return;
            _holoThreadStop = false;
            if (_holoWake == null) _holoWake = new System.Threading.AutoResetEvent(false);
            _holoThread = new System.Threading.Thread(HoloLoop);
            _holoThread.IsBackground = true;
            _holoThread.Name = "SCPSLAlert 面板动画";
            _holoThread.Start();
        }

        /// <summary>
        /// 动画循环（独立线程 + Stopwatch 定拍）。
        ///
        /// 【为什么不用 Timer】实测：WinForms Timer / System.Threading.Timer 在 8ms 间隔下
        /// 都只能跑 ~64fps（它们自己会合并成 ~15.6ms），而 `timeBeginPeriod(1)` + Sleep(1)
        /// 的循环能稳定 125fps。极高档要 120fps 只能这样自己定拍，再把每帧
        /// BeginInvoke 回 UI 线程去推图（用 pending 合并，UI 跟不上时不堆积）。
        /// </summary>
        private void HoloLoop()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            long next = 0;
            while (!_holoThreadStop && !IsDisposed)
            {
                bool holoOn = _holoState == 1 || _holoState == 2;
                bool tabOn = _tabCarousel;
                if (!holoOn && !tabOn)
                {
                    _holoWake.WaitOne(200);
                    sw.Restart();
                    next = 0;
                    continue;
                }

                long now = sw.ElapsedMilliseconds;
                if (now >= next)
                {
                    next = now + Math.Max(4, UiAnim.IntervalMs);
                    if (holoOn) PostHoloTick();
                    else PostTabTick();
                }
                System.Threading.Thread.Sleep(1);
            }
        }

        private void StartHoloTimer()
        {
            EnsureHoloTimer();
            if (_holoWake != null) _holoWake.Set();
        }

        private void StopHoloTimer()
        {
            System.Threading.Interlocked.Exchange(ref _holoTickPending, 0);
        }

        /// <summary>把一帧投递到 UI 线程；上一帧还没处理完就跳过（不排队）。</summary>
        private void PostHoloTick()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (System.Threading.Interlocked.Exchange(ref _holoTickPending, 1) != 0) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    System.Threading.Interlocked.Exchange(ref _holoTickPending, 0);
                    try { OnHoloTick(this, EventArgs.Empty); } catch { }
                });
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref _holoTickPending, 0);
            }
        }

        /// <summary>切页轮播的一帧（同样走动画线程 + BeginInvoke）。</summary>
        private void PostTabTick()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (System.Threading.Interlocked.Exchange(ref _tabTickPending, 1) != 0) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    System.Threading.Interlocked.Exchange(ref _tabTickPending, 0);
                    try { OnTabCarTick(this, EventArgs.Empty); } catch { }
                });
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref _tabTickPending, 0);
            }
        }

        /// <summary>把全息动画的状态和两根线全部清掉（不播动画）。</summary>
        private void CleanHolo()
        {
            StopHoloTimer();
            LeaveHoloHighRes();
            if (_decorFlashTimer != null) _decorFlashTimer.Stop();
            if (_decorFlash != null) _decorFlash.HideLine();
            _holoState = 0;
            _holoProgress = 0;
            if (_holoTop != null) _holoTop.HideLine();
            if (_holoBottom != null) _holoBottom.HideLine();
            _holoDecor = false;
            _holoSurfaceStale = true;
            _holoLastClip = -1;
            _holoLastAlpha = -1f;
            _revealW = -1;
            _revealAlpha = 1f;
        }

        private void EnterHoloHighRes()
        {
            if (_holoHiRes) return;
            _holoHiRes = true;
            UiAnim.EnterHighRes();
        }

        private void LeaveHoloHighRes()
        {
            if (!_holoHiRes) return;
            _holoHiRes = false;
            UiAnim.LeaveHighRes();
        }

        /// <summary>开始（或从当前进度反向继续）全息展开。</summary>
        private void StartHoloOpen()
        {
            // 已经全开：不重播动画，只确保能输入
            if (_holoState == 3)
            {
                _clickThrough = false;
                UpdateExStyle();
                NativeMethods.ForceFocus(Handle);
                FocusFirstInput();
                _caretTimer.Start();
                return;
            }

            if (_holoTop == null || _holoTop.IsDisposed)
            {
                _holoTop = new HoloLineForm(UiAnim.Glow, UiAnim.ExtraFx);
                _holoBottom = new HoloLineForm(UiAnim.Glow, UiAnim.ExtraFx);
            }

            Size sz = MeasureSurface();   // 这里已经把画布画好了（只画一次，动画帧不再重画）
            Point loc = PanelLocation(sz.Width - Pad * 2, sz.Height - Pad * 2);
            _holoBox = new Rectangle(loc.X + Pad, loc.Y + Pad,
                Math.Max(40, sz.Width - Pad * 2), Math.Max(40, sz.Height - Pad * 2));

            // 正在收起 / 展开中再按一次 → 从当前进度接着走（不会跳回 0）
            if (_holoState == 1 || _holoState == 2)
                _holoFrom = _holoProgress;
            else
                _holoFrom = 0;

            _holoDuration = Math.Max(0.20, UiAnim.OpenSeconds * (1.0 - _holoFrom));
            _holoStart = Environment.TickCount;
            _holoState = 1;
            _anim = 1f;
            _animTimer.Stop();
            StopTabCarousel(true);         // 切页动画还没播完的话，直接收掉（内容已经切好了）
            _clickThrough = true;          // 动画期间不接收鼠标 / 不抢游戏焦点（保持原状）
            UpdateExStyle();
            _holoDecor = false;
            _holoSurfaceStale = false;     // MeasureSurface 刚画过，别在动画第一帧再重画（会卡一下）
            _holoLastClip = -1;
            _holoLastAlpha = -1f;
            _holoFrames = 0;

            IntPtr h = Handle;             // 先把句柄建出来（面板窗口先不显示，纵向阶段用不到它）
            ApplyHoloFrame();

            EnterHoloHighRes();
            EnsureHoloTimer();
            StartHoloTimer();
        }

        /// <summary>开始（或从当前进度反向继续）全息收起。</summary>
        private void StartHoloClose()
        {
            if (_holoState == 0) return;

            // 关的时候先把"画在面板里的装饰线"去掉（独立窗口负责这一段动画）
            _holoDecor = false;
            _holoSurfaceStale = true;
            StopTabCarousel(true);
            _holoLastClip = -1;
            _holoLastAlpha = -1f;
            _holoFrames = 0;
            if (_decorFlashTimer != null) _decorFlashTimer.Stop();
            if (_decorFlash != null) _decorFlash.HideLine();

            _holoFrom = _holoProgress;
            _holoDuration = Math.Max(0.22, UiAnim.CloseSeconds * Math.Max(0.30, _holoFrom));
            _holoStart = Environment.TickCount;
            _holoState = 2;
            _clickThrough = true;
            UpdateExStyle();
            EnterHoloHighRes();
            EnsureHoloTimer();
            StartHoloTimer();
            ApplyHoloFrame();
        }

        private void OnHoloTick(object sender, EventArgs e)
        {
            if (_holoState != 1 && _holoState != 2) { StopHoloTimer(); return; }
            UiAnim.Ticks++;
            _holoFrames++;

            double elapsed = (Environment.TickCount - _holoStart) / 1000.0;
            double t = _holoDuration <= 0.001 ? 1.0 : UiAnim.Clamp01(elapsed / _holoDuration);

            if (_holoState == 1)
            {
                _holoProgress = UiAnim.Lerp(_holoFrom, 1.0, t);
                ApplyHoloFrame();
                if (t >= 1.0) FinishHoloOpen();
            }
            else
            {
                _holoProgress = UiAnim.Lerp(_holoFrom, 0.0, t);
                ApplyHoloFrame();
                if (t >= 1.0) FinishHoloClose();
            }
        }

        private void ApplyHoloFrame()
        {
            HoloFrame f = HoloIntro.Compute(_holoProgress, _holoBox,
                Screen.PrimaryScreen.Bounds, UiAnim.Quality);

            // 画布裁剪：中间露出 ClipWidth 那么宽（0 = 全宽）
            _revealW = (f.ClipWidth <= 0) ? -1 : Math.Max(4, f.ClipWidth + Pad * 2);
            _revealAlpha = f.PanelAlpha;

            if (f.PanelVisible && f.PanelAlpha > 0.01f)
            {
                // 面板只在这一段出现；画布只在动画开始时画一次，逐帧只改裁切 / 透明度。
                // 而且只有"裁切 / 透明度真的变了"才推一帧：收尾阶段面板已经全宽静止，
                // 再每帧推一张 1600×850 的透明图画纯属浪费（极高档掉帧的主因）。
                _holoFrameNo++;
                int clip = f.ClipWidth;
                bool need = _holoSurfaceStale || _surfaceBuf == null
                         || clip != _holoLastClip
                         || Math.Abs(f.PanelAlpha - _holoLastAlpha) > 0.01f
                         || !Visible;

                if (_holoSurfaceStale || _surfaceBuf == null)
                {
                    BuildSurface();
                    _holoSurfaceStale = false;
                }
                if (!Visible)
                {
                    Show();
                    // 面板窗口刚显示出来，可能把自己盖到线上面 —— 把两根线重新提上来一次
                    if (_holoTop != null) _holoTop.RaiseTopMost();
                    if (_holoBottom != null) _holoBottom.RaiseTopMost();
                }
                // 极高档：面板图一秒推 100+ 次没意义（每张 ~5MB），隔帧推一次；
                // 线的位置仍然每帧更新 —— 看起来是线在流畅动、面板宽度跟上。
                bool force = f.ClipWidth == 0 && _holoLastClip > 0;
                bool skip = UiAnim.Quality >= 4 && !force && (_holoFrameNo & 1) == 1;
                if (need && !skip)
                {
                    PushToScreen(_surfaceBuf);
                    _holoLastClip = clip;
                    _holoLastAlpha = f.PanelAlpha;
                }
            }
            else
            {
                // 纵向拉伸阶段面板还没出现：干脆别显示窗口，省下一整张图的合成
                if (Visible) Hide();
                _holoLastClip = -1;
                _holoLastAlpha = -1f;
            }

            _holoPulse += 0.075;
            double pulse = Math.Sin(_holoPulse) * 0.5 + 0.5;

            if (f.ShowTop) _holoTop.ShowLine(f.TopLine, HoloColor, f.TopAlpha, pulse);
            else _holoTop.HideLine();

            if (f.ShowBottom) _holoBottom.ShowLine(f.BottomLine, HoloColor, f.BottomAlpha, pulse);
            else _holoBottom.HideLine();
        }

        private void FinishHoloOpen()
        {
            StopHoloTimer();
            double secs = Math.Max(0.001, (Environment.TickCount - _holoStart) / 1000.0);
            Logger.On("动画", "战术面板展开完成：目标 " + (int)Math.Round(_holoDuration * 1000)
                + "ms，实际画了 " + _holoFrames + " 帧（≈" + (int)Math.Round(_holoFrames / secs)
                + " fps，档位 " + UiAnim.Quality + "）");
            LeaveHoloHighRes();
            _holoState = 3;
            _holoProgress = 1.0;
            _revealW = -1;
            _revealAlpha = 1f;

            // 左边的装饰线改成画进面板里（独立窗口在内容刷新时会闪），右边收掉
            if (_holoTop != null) _holoTop.HideLine();
            if (_holoBottom != null) _holoBottom.HideLine();
            _holoDecor = true;
            BuildSurface();
            if (!Visible) Show();
            PushToScreen(_surfaceBuf);

            // 到这里才允许交互 / 拿一次键盘焦点（动画期间不抢游戏焦点）
            _clickThrough = false;
            UpdateExStyle();
            NativeMethods.ForceFocus(Handle);
            FocusFirstInput();
            _caretTimer.Start();
        }

        /// <summary>
        /// 数据有变动 → 左边那根装饰线闪两下（操作者要求：让用户一眼看到"有更新"）。
        /// 这是**故意**的闪烁：平时那根线是静止的画在面板里的，只在数据变动时叠一层更亮的光。
        /// </summary>
        public void FlashDecorLine()
        {
            if (!UseHoloIntro || _holoState != 3) return;
            if (_tabCarousel) return;      // 切页滑动结束时才闪，别在滑动中途抢戏
            if (_holoBox.Width <= 0 || _holoBox.Height <= 0) return;

            if (_decorFlash == null || _decorFlash.IsDisposed)
                _decorFlash = new HoloLineForm(true, UiAnim.ExtraFx);
            if (_decorFlashTimer == null)
            {
                _decorFlashTimer = new Timer();
                _decorFlashTimer.Interval = 30;
                _decorFlashTimer.Tick += OnDecorFlashTick;
            }
            _decorFlashStart = Environment.TickCount;
            _decorFlashTimer.Interval = Math.Max(16, UiAnim.IntervalMs);
            _decorFlashTimer.Start();
            OnDecorFlashTick(null, EventArgs.Empty);
        }

        private void OnDecorFlashTick(object sender, EventArgs e)
        {
            double t = (Environment.TickCount - _decorFlashStart) / 620.0;   // 620ms 两下
            if (t >= 1.0 || _holoState != 3)
            {
                _decorFlashTimer.Stop();
                if (_decorFlash != null) _decorFlash.HideLine();
                return;
            }

            // 两下脉冲：|sin(2π·2t)|，越到后面越弱，收得干脆
            double pulse = Math.Abs(Math.Sin(t * Math.PI * 2.0 * 2.0));
            float alpha = (float)(0.30 + 0.70 * pulse * (1.0 - t * 0.30));

            // 闪的时候光晕更宽（比常驻那根线宽 18px），"明显一点"
            int w = HoloIntro.LineWidth(UiAnim.Quality) + 18;
            _decorFlash.ShowLine(new Rectangle(_holoBox.Left - w / 2, _holoBox.Top, w, _holoBox.Height),
                HoloColor, alpha, pulse);
            _decorFlash.RaiseTopMost();
        }

        private void FinishHoloClose()
        {            StopHoloTimer();
            double secs = Math.Max(0.001, (Environment.TickCount - _holoStart) / 1000.0);
            Logger.On("动画", "战术面板收起完成：目标 " + (int)Math.Round(_holoDuration * 1000)
                + "ms，实际画了 " + _holoFrames + " 帧（≈" + (int)Math.Round(_holoFrames / secs)
                + " fps，档位 " + UiAnim.Quality + "）");
            LeaveHoloHighRes();
            _holoState = 0;
            _holoProgress = 0;
            if (_holoTop != null) _holoTop.HideLine();
            if (_holoBottom != null) _holoBottom.HideLine();
            _holoDecor = false;
            _holoSurfaceStale = true;
            if (Visible) Hide();
            _revealW = -1;
            _revealAlpha = 1f;
        }

        /// <summary>记住呼出前的窗口，关掉时好把焦点还回去。</summary>
        protected void RememberForeground()
        {
            IntPtr f = NativeMethods.GetForegroundWindow();
            _previousForeground = (f == Handle) ? IntPtr.Zero : f;
        }

        #endregion

        #region 键盘

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                if (Cancelled != null) Cancelled();
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                if (Submitted != null) Submitted();
                return;
            }
            if (e.KeyCode == Keys.Tab)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                MoveFocus(e.Shift ? -1 : 1);
                return;
            }
            if (e.KeyCode == Keys.Up) { e.Handled = true; e.SuppressKeyPress = true; MoveFocus(-1); return; }
            if (e.KeyCode == Keys.Down) { e.Handled = true; e.SuppressKeyPress = true; MoveFocus(1); return; }

            string cur = CurrentValue;
            if (e.KeyCode == Keys.Back)
            {
                if (_caretPos > 0 && cur.Length > 0)
                {
                    CurrentValue = cur.Remove(Math.Min(_caretPos, cur.Length) - 1, 1);
                    _caretPos--;
                    AfterEdit();
                }
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                if (_caretPos < cur.Length)
                {
                    CurrentValue = cur.Remove(_caretPos, 1);
                    AfterEdit();
                }
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Left) { if (_caretPos > 0) _caretPos--; e.Handled = true; e.SuppressKeyPress = true; Redraw(); return; }
            if (e.KeyCode == Keys.Right) { if (_caretPos < cur.Length) _caretPos++; e.Handled = true; e.SuppressKeyPress = true; Redraw(); return; }
            if (e.KeyCode == Keys.Home) { _caretPos = 0; e.Handled = true; e.SuppressKeyPress = true; Redraw(); return; }
            if (e.KeyCode == Keys.End) { _caretPos = cur.Length; e.Handled = true; e.SuppressKeyPress = true; Redraw(); return; }

            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // 【保险】没有焦点在输入框上时，别把按键丢掉 —— 自动落到第一个输入格。
            // （以前一旦因为某种原因丢了焦点，面板就"输不进字"了，
            //   用户只能关掉重开；现在至少敲一下就自己找回焦点。）
            if ((_focusRow < 0 || _focusRow >= _rows.Count || !_rows[_focusRow].IsInput)
                && !char.IsControl(e.KeyChar))
            {
                FocusFirstInput();
            }

            if (_focusRow >= 0 && _focusRow < _rows.Count && _rows[_focusRow].IsInput
                && !char.IsControl(e.KeyChar))
            {
                string cur = CurrentValue;
                if (cur.Length < 12)
                {
                    CurrentValue = cur.Insert(Math.Min(_caretPos, cur.Length), e.KeyChar.ToString());
                    _caretPos++;
                    AfterEdit();
                }
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        private void AfterEdit()
        {
            _caretOn = true;
            OnEdited();
            Redraw();
        }

        /// <summary>子类覆盖：输入变化时更新派生显示（例如实时算票数）。</summary>
        protected virtual void OnEdited() { }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 切页滑动过程中坐标一直在动，别让点击落到"正在滑走的那一页"上。
            // 但万一动画没收尾（卡住 / 丢帧），到点就直接收尾，别让面板永远点不动。
            if (_tabCarousel)
            {
                int requestedTab;
                if (TryGetTabClickIndex(e, out requestedTab))
                {
                    // 标签栏位置在过渡中保持不变；先把当前过渡收尾，再执行用户刚点的目标页。
                    try { FinishTabCarousel(); } catch { }
                    GoTab(requestedTab);
                    return;
                }
                if (Environment.TickCount - _tabStart > _tabDuration + 300)
                {
                    try { FinishTabCarousel(); } catch { }
                }
                else
                {
                    return;
                }
            }

            // 页签先处理：它和普通按钮都在 _hits 里，但语义不同
            if (HandleTabClick(e)) return;

            for (int i = 0; i < _hits.Count; i++)
            {
                Hit h = _hits[i];
                if (e.X < h.X || e.X > h.X + h.W || e.Y < h.Y || e.Y > h.Y + h.H) continue;
                if (h.Field < 0) { OnButton(h.Key); return; }
                if (h.Field >= 2) { OnCell(h.Key); return; }   // 行内小格子

                for (int k = 0; k < _rows.Count; k++)
                {
                    Row r = _rows[k];
                    if (!r.IsInput) continue;
                    if (h.Field == 0 && r.Key == h.Key) { _focusRow = k; _focusField = 0; }
                    else if (h.Field == 1 && r.HasSecond && r.Key2 == h.Key) { _focusRow = k; _focusField = 1; }
                }
                _caretPos = CurrentValue.Length;
                _caretOn = true;
                Redraw();
                return;
            }
            base.OnMouseDown(e);
        }

        /// <summary>子类覆盖：面板上的按钮被点了。</summary>
        protected virtual void OnButton(string key) { }

        /// <summary>子类覆盖：行内小格子被点了（阵营循环 / 击杀开关）。</summary>
        protected virtual void OnCell(string key) { }

        #endregion

        #region 绘制

        protected abstract void BuildRows();
        protected abstract string PanelTitle { get; }

        protected virtual string PanelHint
        {
            get { return "回车 = 确认    ESC = 关闭    Tab = 切换输入框"; }
        }

        protected virtual Point PanelLocation(int width, int height)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Left + (wa.Width - width) / 2,
                             wa.Top + (wa.Height - height) / 2);
        }

        private Font _titleFont, _textFont, _smallFont;

        protected Font TitleFont { get { return _titleFont ?? (_titleFont = MakeFont(12.5f, true)); } }
        protected Font TextFont { get { return _textFont ?? (_textFont = MakeFont(10f, false)); } }
        protected Font SmallFont { get { return _smallFont ?? (_smallFont = MakeFont(9f, false)); } }

        private static Font MakeFont(float size, bool bold)
        {
            string face = OverlayForm.ResolveFontFamily("微软雅黑");
            try { return new Font(face, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            catch { return SystemFonts.DefaultFont; }
        }

        // 配色与游戏内提示框保持一致
        protected static readonly Color TextColor = Color.FromArgb(235, 238, 244);
        protected static readonly Color DimColor = Color.FromArgb(140, 148, 162);
        protected static readonly Color AccentColor = Color.FromArgb(90, 190, 255);
        protected static readonly Color WarnColor = Color.FromArgb(255, 170, 60);
        protected static readonly Color GoodColor = Color.FromArgb(120, 230, 140);
        protected static readonly Color BadColor = Color.FromArgb(255, 100, 100);
        protected static readonly Color BoxColor = Color.FromArgb(26, 30, 38);
        protected static readonly Color FieldColor = Color.FromArgb(44, 50, 62);

        private const int RowH = 32;
        private const int FieldH = 26;
        private const int CardRowH = 96;

        /// <summary>卡片主色 / 强调色（战术面板的阵营牌用）。</summary>
        protected Color CardColor = Color.FromArgb(109, 159, 247);

        /// <summary>内容区宽度，子类可以调。</summary>
        protected virtual int ContentWidth { get { return 520; } }

        /// <summary>
        /// 锁定内容区高度（&gt;0 时生效）：切页时面板不再忽大忽小。
        /// 内容比它矮就留白，比它高就被截（子类自己保证放得下）。
        /// </summary>
        protected int FixedContentHeight;

        /// <summary>切页动画的横向偏移（绘制内容时叠加）。</summary>
        private int _animDx;

        /// <summary>只画"框架"（标题 / 页签 / 左边条 / 底部提示），不画页面内容 —— 切页滑动时框架常驻。</summary>
        private bool _frameOnly;

        /// <summary>页面内容区（画布坐标）：切页滑动只在这个矩形里动，框架不动。</summary>
        private Rectangle _contentRect;

        /// <summary>
        /// 复用的画布。
        /// 【为什么要复用】以前每次 Redraw 都 new 一张 900×800 的位图：
        /// 面板展开 / 切页动画是 16ms 一帧，一秒就是几十张 3MB 的位图 ——
        /// 全是垃圾对象，Gen0 GC 一抖，游戏里就能感觉出卡一下。
        /// 现在只有尺寸变了才重建，平时一直用这一张。
        /// </summary>
        private Bitmap _surfaceBuf;

        protected void Redraw()
        {
            if (!IsHandleCreated) return;
            // 切页轮播 / 全息动画期间由动画自己推帧，别在这里重画（会互相踩）
            if (_tabCarousel) return;
            PushToScreen(BuildSurface());
        }

        /// <summary>
        /// 只把面板画成一张图、不推到屏幕上。
        /// 给离线测试用：没有交互桌面时也能检查排版长什么样。
        /// 返回的是**拷贝**：正式绘制复用的那张缓冲不能被测试 Dispose 掉。
        /// </summary>
        internal Bitmap RenderForTest()
        {
            // 注意：BuildSurface 返回的是复用的那张缓冲，**千万别 Dispose 它**
            // （以前这里写成了 using，把正式绘制用的画布也一起销毁了）。
            Bitmap b = BuildSurface();
            return (Bitmap)b.Clone();
        }

        /// <summary>
        /// 只画框架（标题 / 页签 / 左边条 / 底部提示）的快照，页面内容留空。
        /// 切页轮播时把它当静止底图：框架常驻，只有内容区在滑。
        /// </summary>
        private Bitmap RenderFrameOnly()
        {
            _frameOnly = true;
            try
            {
                BuildSurface();
                Bitmap snap = (Bitmap)_surfaceBuf.Clone();
                // 把内容区抠成透明：轮播时先画内容、再把框架盖上去，
                // 框架的标题 / 页签 / 左边条压住边缘，内容区不会被框架底色盖掉。
                using (Graphics g = Graphics.FromImage(snap))
                {
                    g.SetClip(_contentRect);
                    g.Clear(Color.Transparent);
                    g.ResetClip();
                }
                return snap;
            }
            finally
            {
                _frameOnly = false;
                _surfaceVersion++;      // 下一帧正式内容还要重画
            }
        }

        /// <summary>
        /// 一条说明行会占多高（考虑换行）。
        /// 说明行现在会在列宽内自动折行，所以高度不能再按固定 22 算，
        /// 否则长说明会盖住下面的行。
        /// </summary>
        private int NoteHeight(string text, int colW)
        {
            if (string.IsNullOrEmpty(text)) return 24;

            // 粗略估算每行能放多少：SmallFont 里一个汉字约 12px、西文约 7px。
            // 高度计算发生在建位图之前、拿不到 Graphics，估算够用
            // （宁可多留一点也不要压行）。
            int total = 0;
            for (int i = 0; i < text.Length; i++)
                total += (text[i] >= 0x2E80) ? 12 : 7;

            int lines = (colW > 0) ? (total / colW) + 1 : 1;
            if (lines < 1) lines = 1;
            if (lines > 12) lines = 12;      // 兜底，别把面板撑爆
            return lines * 18 + 6;
        }

        private Bitmap BuildSurface()
        {
            int titleH = string.IsNullOrEmpty(PanelTitle) ? 0 : 36;
            int hintH = 30;
            int tabH = TabBarHeight;

            // ---- 按分栏符把行切成若干列 ----
            List<int> colStart = new List<int>();
            colStart.Add(0);
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].IsColumnBreak) colStart.Add(i + 1);

            int colCount = colStart.Count;
            int colW = ContentWidth;
            int colGap = 24;

            // 各列分别算高度，整体取最高的那一列
            int tallest = 0;
            for (int c = 0; c < colCount; c++)
            {
                int from = colStart[c];
                int to = (c + 1 < colCount) ? colStart[c + 1] - 1 : _rows.Count;
                int h = 0;
                for (int i = from; i < to; i++)
                {
                    Row r = _rows[i];
                    if (r.IsHidden) continue;
                    if (r.IsCard) h += CardRowH;
                    else if (r.IsSection) h += 34;
                    else if (r.IsHeader) h += 28;
                    else if (r.IsNote) h += NoteHeight(r.Label, colW);
                    else if (r.IsButtonStrip) h += 36;
                    else h += RowH;
                }
                if (h > tallest) tallest = h;
            }

            int contentH = tallest;
            // 锁高度：切页时窗口不跳
            if (FixedContentHeight > 0) contentH = FixedContentHeight;

            // 【安全网】内容比锁定高度还高时，至少按内容来 ——
            // 否则多出来的部分会**画到面板框外面**（操作者报的"总览右下角超出界面"）。
            if (contentH < tallest)
            {
                contentH = tallest;
                // 顺手写一行日志：以后哪一页内容超了，翻日志就知道是它（而不是靠肉眼看出框）
                Logger.On("面板", PanelTitle + " 的内容比锁定高度高（" + tallest + " > "
                    + FixedContentHeight + "）→ 这次按内容撑开，避免画到框外");
            }

            // 但也不能超出屏幕可用高度（超了照样会顶出去），
            // 到这一步就只能让最下面那点内容被裁掉，至少框是完整在屏幕里的。
            try
            {
                int chrome = titleH + tabH + hintH + 18 + Pad * 2;
                int maxContent = Screen.PrimaryScreen.WorkingArea.Height - chrome - 8;
                if (maxContent > 200 && contentH > maxContent) contentH = maxContent;
            }
            catch { }

            int contentW = colW * colCount + colGap * (colCount - 1);
            int boxW = contentW + 32;
            int boxH = titleH + tabH + contentH + hintH + 18;
            int surfW = boxW + Pad * 2;
            int surfH = boxH + Pad * 2;

            // 内容区（画布坐标）：页签下面到提示行上面那一块。
            // 切页轮播时只有这一块在动，标题 / 页签 / 左边条 / 底部提示都是静止的。
            int contentTop = Pad + 10 + titleH + tabH;
            _contentRect = new Rectangle(Pad + 6, contentTop,
                contentW + 24, Math.Max(1, contentH) + 6);

            if (_surfaceBuf == null || _surfaceBuf.Width != surfW || _surfaceBuf.Height != surfH)
            {
                if (_surfaceBuf != null) _surfaceBuf.Dispose();
                _surfaceBuf = CreateDibSurface(surfW, surfH);
            }
            Bitmap bmp = _surfaceBuf;
            _surfaceVersion++;      // 内容变了：推屏缓存要重建一次
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                Rectangle box = new Rectangle(Pad, Pad, boxW, boxH);
                int radius = 16;

                // 平面风格：实色底 + 1px 细边，不要发光、不要玻璃渐变。
                // 和「通知开关面板」是同一套观感：简单、一眼看懂。
                // 透明度留一点（246）——几乎不透明，保证字看得清，
                // 又不至于完全挡住游戏画面。
                using (GraphicsPath gp = AlertRenderer.RoundedRect(box, radius))
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(246, BoxColor)))
                        g.FillPath(sb, gp);
                    using (Pen p = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                        g.DrawPath(p, gp);
                }

                // 标题下面一条分隔线
                using (Pen p = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
                    g.DrawLine(p, box.X + 16, box.Y + 46, box.Right - 16, box.Y + 46);

                // 左侧一条强调色，和通知开关面板一样
                using (SolidBrush sb = new SolidBrush(AccentColor))
                    g.FillRectangle(sb, box.X, box.Y + 1, 4, box.Height - 2);

                // 全息展开完成后：左边那根发光竖线**直接画进面板**。
                // 以前它是独立的分层窗口，面板内容一刷新（OCR 认到东西 / 发电机走秒）
                // 就会在合成时闪一下；画进画布里就完全不会闪了。
                if (_holoDecor)
                {
                    int cx = box.X + 3;
                    for (int i = -10; i <= 10; i++)
                    {
                        int a = (int)Math.Round(70 * Math.Exp(-(i * i) / 40.0));
                        if (a <= 0) continue;
                        using (Pen p = new Pen(Color.FromArgb(a, HoloColor), 1f))
                            g.DrawLine(p, cx + i, box.Y + 4, cx + i, box.Bottom - 4);
                    }
                    using (Pen p = new Pen(Color.FromArgb(235, 245, 255, 255), 2f))
                        g.DrawLine(p, cx, box.Y + 4, cx, box.Bottom - 4);
                }

                int x = Pad + 18;
                int y = Pad + 10;
                int innerW = colW;

                if (!string.IsNullOrEmpty(PanelTitle))
                {
                    using (SolidBrush sb = new SolidBrush(TextColor))
                        g.DrawString(PanelTitle, TitleFont, sb, x, y);
                    y += 36;
                }

                int baseY = y;
                _hits.Clear();

                // ---- 页签栏（有页签时画在标题下面）----
                if (TabBarHeight > 0)
                {
                    DrawTabs(g, box.X, y, boxW - 36);
                    y += TabBarHeight;
                    baseY = y;              // 行从页签下面开始画
                }

                // ---- 切页动画：内容整体轻微右移滑入 ----
                // 只平移内容（不改字号），这样文字始终清晰；
                // 动画结束 anim==1 时偏移为 0，就是正常绘制。
                float anim = TabAnimProgress;
                _animDx = (anim < 1f)
                    ? (int)Math.Round((1f - UiAnim.EaseOutCubic(anim)) * 140f * _tabSlideDir)
                    : 0;

                // 只在切页滑动（有横向偏移）时把内容裁在内容区里 ——
                // 否则滑动中的行会压到左边的装饰条 / 页签 / 面板边框上（"穿模"）。
                bool clipAnim = _animDx != 0;
                if (clipAnim) g.SetClip(_contentRect);

                // 逐列绘制：每列自己的 x / y / 可用宽度
                // （_frameOnly = 只画框架时整段跳过 —— 切页滑动用它当静止底图）
                for (int c = 0; c < colCount && !_frameOnly; c++)
                {
                    int from = colStart[c];
                    int to = (c + 1 < colCount) ? colStart[c + 1] - 1 : _rows.Count;

                    x = Pad + 18 + c * (colW + colGap) + _animDx;
                    y = baseY;
                    innerW = colW;

                    // 本列是不是只有一条"整块宽"的行？是的话把它拉满整个面板宽度
                    bool wideColumn = (to - from) == 1
                        && _rows.Count > from && _rows[from].IsWide;
                    if (wideColumn)
                        innerW = colW * colCount + colGap * (colCount - 1);

                for (int i = from; i < to; i++)
                {
                    Row r = _rows[i];
                    if (r.IsHidden) continue;

                    if (r.IsSection)
                    {
                        // 一条带底色的色带 + 左侧主色块，用来把页面切成几块
                        int hgt = 34;
                        Rectangle band = new Rectangle(x - 6, y + 2, innerW + 12, hgt - 6);
                        using (GraphicsPath gp = AlertRenderer.RoundedRect(band, 6))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(60, r.CardColor)))
                            g.FillPath(sb, gp);
                        using (SolidBrush sb = new SolidBrush(r.CardColor))
                            g.FillRectangle(sb, band.X, band.Y + 2, 4, band.Height - 4);
                        using (SolidBrush sb = new SolidBrush(TextColor))
                            g.DrawString(r.Label, TextFont, sb, band.X + 12, band.Y + 4);
                        y += 34;
                        continue;
                    }

                    // ---- 大卡片行（阵营牌：主色圆角块 + 超大数字）----
                    if (r.IsCard)
                    {
                        Rectangle card = new Rectangle(x, y + 2, innerW, CardRowH - 8);
                        using (GraphicsPath gp = AlertRenderer.RoundedRect(card, 10))
                        {
                            using (SolidBrush sb = new SolidBrush(
                                Color.FromArgb(46, r.CardColor)))
                                g.FillPath(sb, gp);
                            using (Pen p = new Pen(Color.FromArgb(190, r.CardColor), 2f))
                                g.DrawPath(p, gp);
                        }

                        // 左侧一条主色竖条
                        using (SolidBrush sb = new SolidBrush(r.CardColor))
                            g.FillRectangle(sb, card.X, card.Y + 2, 5, card.Height - 4);

                        // 阵营名（小字，主色）
                        using (SolidBrush sb = new SolidBrush(r.CardColor))
                            g.DrawString(r.Label ?? "", TextFont, sb, card.X + 18, card.Y + 9);

                        // 超大数字（令牌数）
                        using (Font big = MakeFont(30f, true))
                        using (SolidBrush sb = new SolidBrush(r.CardColor))
                            g.DrawString(r.Value ?? "", big, sb, card.X + 16, card.Y + 28);

                        // 右侧附加说明（影响力 / 下一波）
                        // 【有齿轮时往左让位】齿轮画在右上角，说明文字压上去会叠在一起。
                        if (!string.IsNullOrEmpty(r.CardSub))
                        {
                            using (SolidBrush sb = new SolidBrush(TextColor))
                            {
                                float subW = g.MeasureString(r.CardSub, TextFont).Width;
                                float subX = card.Right - subW - 18 - (r.ShowGear ? 34 : 0);
                                g.DrawString(r.CardSub, TextFont, sb, subX, card.Y + 12);
                            }
                        }

                        // ---- 底部进度条（发电机那张卡：几台 / 共几台）----
                        if (r.BarMax > 0)
                        {
                            int barH = 6;
                            Rectangle track = new Rectangle(card.X + 18, card.Bottom - barH - 12,
                                card.Width - 36, barH);
                            using (GraphicsPath gp2 = AlertRenderer.RoundedRect(track, 3))
                            using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(46, r.CardColor)))
                                g.FillPath(sb2, gp2);

                            int v = r.BarValue < 0 ? 0 : (r.BarValue > r.BarMax ? r.BarMax : r.BarValue);
                            if (v > 0)
                            {
                                int fw = (int)Math.Round(track.Width * (v / (double)r.BarMax));
                                if (fw < barH) fw = barH;
                                Rectangle fill = new Rectangle(track.X, track.Y, fw, track.Height);
                                using (GraphicsPath gp2 = AlertRenderer.RoundedRect(fill, 3))
                                using (SolidBrush sb2 = new SolidBrush(r.CardColor))
                                    g.FillPath(sb2, gp2);
                            }
                        }

                        // ---- 右上角齿轮（点它 = 打开这一块的设置/详情）----
                        if (r.ShowGear && !string.IsNullOrEmpty(r.GearKey))
                        {
                            int gs = 24;
                            Rectangle gear = new Rectangle(card.Right - gs - 14, card.Y + 10, gs, gs);
                            DrawGear(g, gear, r.CardColor);

                            Hit hg = new Hit();
                            hg.X = gear.X - 3; hg.Y = gear.Y - 3;
                            hg.W = gear.Width + 6; hg.H = gear.Height + 6;
                            hg.Key = r.GearKey; hg.Field = -1;
                            _hits.Add(hg);
                        }

                        // 整张卡片也点得动（和齿轮同一个去处）
                        if (!string.IsNullOrEmpty(r.Key))
                        {
                            Hit hc = new Hit();
                            hc.X = card.X; hc.Y = card.Y; hc.W = card.Width; hc.H = card.Height;
                            hc.Key = r.Key; hc.Field = -1;
                            _hits.Add(hc);
                        }

                        y += CardRowH;
                        continue;
                    }

                    if (r.IsHeader)
                    {
                        using (SolidBrush sb = new SolidBrush(AccentColor))
                            g.DrawString(r.Label, TextFont, sb, x, y + 6);
                        y += 28;
                        continue;
                    }
                    if (r.IsNote)
                    {
                        // 说明行要能换行：有些说明很长（影响力时间线那种），
                        // 一行画到底会顶出面板右边缘。
                        using (SolidBrush sb = new SolidBrush(
                            r.Label.StartsWith("！") || r.Label.StartsWith("⚠") ? WarnColor : DimColor))
                        {
                            RectangleF area = new RectangleF(x, y + 4, innerW, 200f);
                            SizeF need = g.MeasureString(r.Label, SmallFont,
                                new SizeF(innerW, 1000f));
                            g.DrawString(r.Label, SmallFont, sb, area);
                            y += (int)Math.Ceiling(need.Height) + 6;
                        }
                        continue;
                    }
                    if (r.IsButtonStrip)
                    {
                        int bx = x;
                        for (int bi = 0; bi < r.Buttons.Count; bi++)
                        {
                            ButtonSpec spec = r.Buttons[bi];
                            string cap = spec.Label + (r.StripCounts != null && bi < r.StripCounts.Length
                                && r.StripCounts[bi] > 0 ? "  ×" + r.StripCounts[bi] : "");

                            SizeF ssz = g.MeasureString(cap, TextFont);
                            int bw2 = (int)ssz.Width + 24;
                            if (bx + bw2 > x + innerW && bx > x) { bx = x; y += 30; }   // 放不下就换行

                            Rectangle bb = new Rectangle(bx, y + 2, bw2, 26);
                            using (GraphicsPath gp = AlertRenderer.RoundedRect(bb, 6))
                            {
                                using (SolidBrush sb = new SolidBrush(Color.FromArgb(52, spec.Color)))
                                    g.FillPath(sb, gp);
                                using (Pen p = new Pen(Color.FromArgb(200, spec.Color), 1f))
                                    g.DrawPath(p, gp);
                            }
                            using (SolidBrush sb = new SolidBrush(spec.Color))
                                g.DrawString(cap, TextFont, sb, bx + 12, y + 6);

                            Hit hb = new Hit();
                            hb.X = bb.X; hb.Y = bb.Y; hb.W = bb.Width; hb.H = bb.Height;
                            hb.Key = spec.Key; hb.Field = -1;
                            _hits.Add(hb);

                            bx += bw2 + 6;
                        }
                        y += 36;
                        continue;
                    }

                    if (r.IsButton)
                    {
                        SizeF sz = g.MeasureString(r.Label, TextFont);
                        int bw = (int)sz.Width + 28;
                        Rectangle b = new Rectangle(x, y + 2, bw, 26);
                        using (GraphicsPath gp = AlertRenderer.RoundedRect(b, 6))
                        {
                            // 彩色按钮（记录目标用）：
                            // 主色淡底 + 主色描边 + 主色字，一眼能分辨是哪一方的目标。
                            Color fill = r.ColoredButton
                                ? Color.FromArgb(52, r.CardColor)
                                : Color.FromArgb(150, 60, 70, 88);
                            Color edge = r.ColoredButton
                                ? Color.FromArgb(200, r.CardColor)
                                : Color.FromArgb(90, 255, 255, 255);
                            using (SolidBrush sb = new SolidBrush(fill))
                                g.FillPath(sb, gp);
                            using (Pen p = new Pen(edge, 1f))
                                g.DrawPath(p, gp);
                        }
                        using (SolidBrush sb = new SolidBrush(
                            r.ColoredButton ? r.CardColor : TextColor))
                            g.DrawString(r.Label, TextFont, sb, x + 14, y + 6);

                        Hit hb = new Hit();
                        hb.X = b.X; hb.Y = b.Y; hb.W = b.Width; hb.H = b.Height;
                        hb.Key = r.Key; hb.Field = -1;
                        _hits.Add(hb);
                        y += RowH;
                        continue;
                    }
                    if (r.IsReadout)
                    {
                        // 注意：TitleFont 是缓存字段，这里绝对不能包 using
                        // （using 会把它 Dispose 掉，后面的行再用就崩）
                        string val = r.Value ?? "";

                        // 值太长时用正文号字，并且保证不和左边的标签撞上
                        //（否则会看到"距离下一个令牌"和值叠在一起）。
                        Font vf = TitleFont;
                        SizeF sz = g.MeasureString(val, vf);
                        SizeF lsz = g.MeasureString(r.Label, TextFont);

                        if (sz.Width + lsz.Width + 24 > innerW)
                        {
                            vf = TextFont;
                            sz = g.MeasureString(val, vf);

                            // 还是挤不下就截断（宁可截断也不要压字）
                            if (sz.Width + lsz.Width + 24 > innerW)
                            {
                                int maxW = innerW - (int)lsz.Width - 24;
                                if (maxW < 40) maxW = 40;
                                while (val.Length > 3 && g.MeasureString(val + "…", vf).Width > maxW)
                                    val = val.Substring(0, val.Length - 1);
                                if (val.Length < (r.Value ?? "").Length) val += "…";
                                sz = g.MeasureString(val, vf);
                            }
                        }

                        using (SolidBrush sb = new SolidBrush(TextColor))
                            g.DrawString(r.Label, TextFont, sb, x, y + 6);
                        using (SolidBrush sb2 = new SolidBrush(ValueColor(r.Key)))
                            g.DrawString(val, vf, sb2, x + innerW - sz.Width, y + 3);
                        y += RowH;
                        continue;
                    }

                    // ---- 只有一个行内小格子的行 ----
                    if (!r.IsInput && r.HasThird)
                    {
                        using (SolidBrush sb = new SolidBrush(TextColor))
                            g.DrawString(r.Label, TextFont, sb, x, y + 6);
                        int w3 = r.Width3 > 0 ? r.Width3 : 64;
                        DrawCell(g, r.Key3, r.Label3, r.Cell3Color,
                                 new Rectangle(x + innerW - w3, y + 3, w3, FieldH - 2), 2);
                        y += RowH;
                        continue;
                    }

                    // ---- 输入行 ----
                    if (r.HasSecond)
                    {
                        // 「SCP-049」 [总血量] [当前血量] [阵营] [击杀]
                        using (SolidBrush sb = new SolidBrush(TextColor))
                            g.DrawString(r.Label, TextFont, sb, x, y + 6);

                        int w1 = r.Width > 0 ? r.Width : 80;
                        int w2 = r.Width2 > 0 ? r.Width2 : 80;
                        int w3 = r.HasThird ? (r.Width3 > 0 ? r.Width3 : 64) : 0;
                        int w4 = r.HasFourth ? (r.Width4 > 0 ? r.Width4 : 56) : 0;
                        int w5 = r.HasFifth ? (r.Width5 > 0 ? r.Width5 : 48) : 0;
                        // 两个输入框之间留宽一点，好放「当前」这个说明字
                        int gapMid = 32;
                        int gap = 6;

                        int x5 = x + innerW - w5;
                        int x4 = x5 - (r.HasFifth ? gap : 0) - w4;
                        int x3 = x4 - (r.HasFourth ? gap : 0) - w3;
                        int x2 = x3 - (r.HasThird ? gap : 0) - w2;
                        int x1 = x2 - gapMid - w1;

                        DrawField(g, i, r.Key, r.Value, new Rectangle(x1, y + 2, w1, FieldH), 0);
                        DrawField(g, i, r.Key2, r.Value2, new Rectangle(x2, y + 2, w2, FieldH), 1);

                        if (r.HasThird)
                            DrawCell(g, r.Key3, r.Label3, r.Cell3Color,
                                     new Rectangle(x3, y + 3, w3, FieldH - 2), 2);
                        if (r.HasFourth)
                            DrawCell(g, r.Key4, r.Label4, r.Cell4Color,
                                     new Rectangle(x4, y + 3, w4, FieldH - 2), 3);
                        if (r.HasFifth)
                            DrawCell(g, r.Key5, r.Label5, r.Cell5Color,
                                     new Rectangle(x5, y + 3, w5, FieldH - 2), 4);

                        // 两个框左边的小字说明（各自按真实宽度右对齐，不会压到框上）
                        using (SolidBrush sb = new SolidBrush(DimColor))
                        {
                            string t1 = r.Small1 == null ? "" : r.Small1;
                            string t2 = r.Small2 == null ? "" : r.Small2;
                            if (t1.Length > 0)
                            {
                                SizeF s1 = g.MeasureString(t1, SmallFont);
                                g.DrawString(t1, SmallFont, sb, x1 - s1.Width - 5, y + 9);
                            }
                            if (t2.Length > 0)
                            {
                                SizeF s2 = g.MeasureString(t2, SmallFont);
                                g.DrawString(t2, SmallFont, sb, x2 - s2.Width - 6, y + 9);
                            }
                        }
                    }
                    else
                    {
                        using (SolidBrush sb = new SolidBrush(TextColor))
                            g.DrawString(r.Label, TextFont, sb, x, y + 6);

                        int fw = r.Width > 0 ? r.Width : 120;
                        DrawField(g, i, r.Key, r.Value,
                            new Rectangle(x + innerW - fw, y + 2, fw, FieldH), 0);
                    }
                    y += RowH;
                }

                }   // 结束这一列

                if (clipAnim) g.ResetClip();

                using (SolidBrush sb = new SolidBrush(DimColor))
                    g.DrawString(PanelHint, SmallFont, sb, Pad + 18, Pad + boxH - hintH + 4);
            }

            return bmp;
        }

        private static Color ValueColor(string key)
        {
            if (key == "mtf") return GoodColor;
            if (key == "chaos") return WarnColor;
            if (key == "total") return AccentColor;
            return TextColor;
        }

        private void DrawField(Graphics g, int rowIndex, string key, string value,
                               Rectangle f, int field)
        {
            bool focused = (rowIndex == _focusRow && field == _focusField);

            // 平面风格：实色块 + 1px 边，聚焦时换成强调色
            using (GraphicsPath gp = AlertRenderer.RoundedRect(f, 6))
            {
                using (SolidBrush sb = new SolidBrush(FieldColor))
                    g.FillPath(sb, gp);
                using (Pen p = new Pen(focused ? AccentColor : Color.FromArgb(56, 255, 255, 255),
                                       focused ? 2f : 1f))
                    g.DrawPath(p, gp);
            }

            if (!string.IsNullOrEmpty(value))
                using (SolidBrush sb = new SolidBrush(TextColor))
                    g.DrawString(value, TextFont, sb, f.X + 7, f.Y + 4);

            if (focused && _caretOn)
            {
                string before = value == null ? "" : value.Substring(0, Math.Min(_caretPos, value.Length));
                SizeF bw = g.MeasureString(before, TextFont);
                float cx = f.X + 7 + bw.Width;
                using (Pen p = new Pen(TextColor, 1f))
                    g.DrawLine(p, cx, f.Y + 5, cx, f.Y + FieldH - 6);
            }

            Hit h = new Hit();
            h.X = f.X; h.Y = f.Y; h.W = f.Width; h.H = f.Height;
            h.Key = key; h.Field = field;
            _hits.Add(h);
        }

        /// <summary>行内可点的小格子（阵营 / 击杀 这类开关）。</summary>
        private void DrawCell(Graphics g, string key, string text, Color textColor,
                              Rectangle f, int field)
        {
            using (GraphicsPath gp = AlertRenderer.RoundedRect(f, 5))
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(90, 56, 64, 78)))
                    g.FillPath(sb, gp);
                using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                    g.DrawPath(p, gp);
            }

            string t = text ?? "";
            SizeF sz = g.MeasureString(t, SmallFont);
            using (SolidBrush sb = new SolidBrush(textColor))
                g.DrawString(t, SmallFont, sb,
                    f.X + (f.Width - sz.Width) / 2f,
                    f.Y + (f.Height - sz.Height) / 2f);

            Hit h = new Hit();
            h.X = f.X; h.Y = f.Y; h.W = f.Width; h.H = f.Height;
            h.Key = key; h.Field = field;
            _hits.Add(h);
        }

        /// <summary>
        /// 把画布推到屏幕上。
        /// 【性能】位图 / 内存 DC 复用：只有 `_surfaceVersion` 变了（画布重画过）
        /// 或尺寸变了才重建。动画帧（只改裁切 / 透明度 / 位置）不再重建 GDI 对象。
        /// </summary>
        /// <summary>建一块和面板同尺寸的 DIB 画布（32bpp 预乘 alpha），可直接交给 ULW。</summary>
        private Bitmap CreateDibSurface(int w, int h)
        {
            ReleaseDibSurface();
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                _dibDc = NativeMethods.CreateCompatibleDC(screenDc);
                NativeMethods.BITMAPINFO bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h;      // 负数 = 自上而下存储
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;

                IntPtr bits;
                _dibSection = NativeMethods.CreateDIBSection(screenDc, ref bmi,
                    NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
                if (_dibSection != IntPtr.Zero && bits != IntPtr.Zero)
                {
                    _dibBits = bits;
                    NativeMethods.SelectObject(_dibDc, _dibSection);
                    _dibW = w;
                    _dibH = h;
                    return new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                }
            }
            catch { }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            ReleaseDibSurface();
            return new Bitmap(w, h, PixelFormat.Format32bppArgb);   // 兜底：还是用老路
        }

        private void ReleaseDibSurface()
        {
            _dibBits = IntPtr.Zero;
            if (_dibDc != IntPtr.Zero) { try { NativeMethods.DeleteDC(_dibDc); } catch { } _dibDc = IntPtr.Zero; }
            if (_dibSection != IntPtr.Zero) { try { NativeMethods.DeleteObject(_dibSection); } catch { } _dibSection = IntPtr.Zero; }
            _dibW = _dibH = 0;
        }

        private void PushToScreen(Bitmap surface)
        {
            Point loc = PanelLocation(surface.Width - Pad * 2, surface.Height - Pad * 2);

            // 开合动画：透明度 + 轻微位移
            float eased = Ease(_anim);
            loc.Y += (int)Math.Round((1f - eased) * AnimSlide);

            // 全息展开时只露出画布中间一条（宽度跟着两根线走）
            int fullW = surface.Width;
            int clipW = fullW;
            int srcX = 0;
            if (_revealW > 0 && _revealW < fullW)
            {
                clipW = Math.Max(1, _revealW);
                srcX = (fullW - clipW) / 2;
            }

            int alpha = (int)Math.Round(eased * _revealAlpha * 255f);
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;

            // 有 DIB 画布就直接用它当源 DC（不用每帧 GetHbitmap 拷整张图）；
            // 没有（极老系统 / 创建失败）才退回老的缓存位图。
            IntPtr srcDc;
            if (_dibDc != IntPtr.Zero && _dibW == surface.Width && _dibH == surface.Height)
            {
                srcDc = _dibDc;
            }
            else
            {
                EnsurePushCache(surface);
                srcDc = _pushMemDc;
            }

            NativeMethods.SIZE size = new NativeMethods.SIZE();
            size.cx = clipW;
            size.cy = surface.Height;

            NativeMethods.POINT src = new NativeMethods.POINT();
            NativeMethods.POINT dst = new NativeMethods.POINT();
            src.X = srcX;
            dst.X = loc.X + srcX;
            dst.Y = loc.Y;

            NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
            blend.BlendOp = NativeMethods.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = (byte)alpha;
            blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            bool updated;
            try
            {
                updated = NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    srcDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (updated)
            {
                System.Threading.Interlocked.Exchange(ref _lastLayeredUpdateError, 0);
            }
            else
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (error != System.Threading.Interlocked.Exchange(ref _lastLayeredUpdateError, error))
                    Logger.On("面板", "分层窗口画面提交失败，Win32 错误 " + error);
            }

            // 装饰线现在是画进画布的；闪线窗口如果正在显示，保证它压在面板上面
            if (_holoState == 3 && _decorFlash != null && _decorFlash.Visible)
                _decorFlash.RaiseTopMost();

            UpdateExStyle();
        }

        private void EnsurePushCache(Bitmap surface)
        {
            if (_pushMemDc != IntPtr.Zero
                && _pushW == surface.Width && _pushH == surface.Height
                && _pushVersion == _surfaceVersion)
                return;

            ReleasePushCache();

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                _pushMemDc = NativeMethods.CreateCompatibleDC(screenDc);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
            _pushHBitmap = surface.GetHbitmap(Color.FromArgb(0));
            _pushOldBitmap = NativeMethods.SelectObject(_pushMemDc, _pushHBitmap);
            _pushW = surface.Width;
            _pushH = surface.Height;
            _pushVersion = _surfaceVersion;
        }

        private void ReleasePushCache()
        {
            try
            {
                if (_pushMemDc != IntPtr.Zero)
                {
                    if (_pushHBitmap != IntPtr.Zero)
                    {
                        NativeMethods.SelectObject(_pushMemDc, _pushOldBitmap);
                        NativeMethods.DeleteObject(_pushHBitmap);
                    }
                    NativeMethods.DeleteDC(_pushMemDc);
                }
            }
            catch { }
            _pushHBitmap = IntPtr.Zero;
            _pushMemDc = IntPtr.Zero;
            _pushOldBitmap = IntPtr.Zero;
            _pushW = _pushH = 0;
            _pushVersion = -1;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _caretTimer.Stop();
                StopTabCarousel(true);
                _holoThreadStop = true;
                if (_holoWake != null) { try { _holoWake.Set(); } catch { } }
                if (_holoThread != null) { try { _holoThread.Join(300); } catch { } _holoThread = null; }
                if (_holoWake != null) { try { _holoWake.Dispose(); } catch { } _holoWake = null; }
                if (_decorFlashTimer != null) { try { _decorFlashTimer.Stop(); _decorFlashTimer.Dispose(); } catch { } _decorFlashTimer = null; }
                if (_decorFlash != null) { try { _decorFlash.Dispose(); } catch { } _decorFlash = null; }
                if (_holoTop != null) { try { _holoTop.Dispose(); } catch { } _holoTop = null; }
                if (_holoBottom != null) { try { _holoBottom.Dispose(); } catch { } _holoBottom = null; }
                LeaveHoloHighRes();
                ReleasePushCache();
                if (_surfaceBuf != null) { try { _surfaceBuf.Dispose(); } catch { } _surfaceBuf = null; }
                ReleaseDibSurface();
                if (_titleFont != null) _titleFont.Dispose();
                if (_textFont != null) _textFont.Dispose();
                if (_smallFont != null) _smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
