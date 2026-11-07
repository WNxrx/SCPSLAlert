using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 一个提示样式的实时预览：用和屏幕上完全相同的 AlertRenderer 来画，
    /// 所以调什么这里就看到什么。
    /// </summary>
    internal class AlertPreview : Control
    {
        private AlertVisual _visual = new AlertVisual();
        private string _sample = "净化倒计时 11:45";
        private Bitmap _layer;

        public AlertPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(52, 56, 62);
        }

        public void SetVisual(AlertVisual v, string sample)
        {
            _visual = v;
            _sample = sample;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            // 先画一块伪“游戏画面”背景，方便看清半透明 / 壁纸 / 玻璃质感的实际效果
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 120, 132, 118)))
                g.FillRectangle(b, 0, 0, Width, Height);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 92, 104, 128)))
                g.FillRectangle(b, 0, Height / 2, Width, Height / 2);
            using (Pen p = new Pen(Color.FromArgb(255, 150, 160, 140), 2f))
            {
                g.DrawLine(p, 0, Height / 2, Width, Height / 2);
                g.DrawLine(p, Width / 3, 0, Width / 3, Height);
            }

            if (_visual == null) return;
            AlertVisual v = _visual;
            v.Text = _sample;

            AlertLayout L = AlertRenderer.Measure(v);

            // 提示本身画到一张独立的透明位图上（和屏幕上的做法一致），
            // 再贴到伪游戏画面上，这样半透明 / 玻璃才会正确叠加上去。
            if (_layer == null || _layer.Width != L.Surface.Width || _layer.Height != L.Surface.Height)
            {
                if (_layer != null) _layer.Dispose();
                _layer = new Bitmap(L.Surface.Width, L.Surface.Height,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            using (Graphics lg = Graphics.FromImage(_layer))
                AlertRenderer.Draw(lg, v, L, 1f);

            float scale = 1f;
            if (L.Surface.Width > Width - 8 || L.Surface.Height > Height - 8)
            {
                float sx = (float)(Width - 8) / L.Surface.Width;
                float sy = (float)(Height - 8) / L.Surface.Height;
                scale = Math.Min(sx, sy);
                if (scale > 1f) scale = 1f;
            }

            float w = L.Surface.Width * scale;
            float h = L.Surface.Height * scale;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_layer, (Width - w) / 2f, (Height - h) / 2f, w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _layer != null) { _layer.Dispose(); _layer = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 提示外观编辑器。全部用三列网格排（标签 | 控件 | 单位），
    /// 行高统一、间距一致，文字和滑块绝对不会重叠。
    /// </summary>
    internal class StyleEditor : Panel
    {
        public event Action Changed;

        private ComboBox _cboFont;
        private NumericUpDown _numFontSize;
        private CheckBox _chkBold;
        private Button _btnTextColor;
        private TrackBar _trkTextOpacity;
        private Label _lblTextOpacity;
        private CheckBox _chkShowIcon;

        private Button _btnBackColor;
        private TrackBar _trkBackOpacity;
        private Label _lblBackOpacity;
        private NumericUpDown _numCorner;
        private CheckBox _chkGlass;
        private TrackBar _trkGlassStrength;
        private Label _lblGlassStrength;
        private TrackBar _trkGlassHighlight;
        private Label _lblGlassHighlight;
        private TrackBar _trkGlassNoise;
        private Label _lblGlassNoise;

        private TextBox _txtWallpaper;
        private ComboBox _cboWallMode;
        private TrackBar _trkWallOpacity;
        private Label _lblWallOpacity;

        private ComboBox _cboAnim;
        private NumericUpDown _numAnimIn;
        private NumericUpDown _numAnimOut;
        private NumericUpDown _numSlide;

        private bool _loading;

        /// <summary>全局默认音频（试听时用；由 SettingsForm 在加载时塞进来）。</summary>
        public string GlobalAudioDefault = "";
        private string _globalAudioDefault { get { return GlobalAudioDefault; } }

        /// <summary>自定义片段的 config 串（由 SettingsForm 塞进来，用来列 @片段）。</summary>
        public string SegmentsSpec = "";

        private CheckBox _chkPersistent;
        private CheckBox _chkEstimated;

        // 提示音
        private CheckBox _chkSound;
        private ComboBox _cboSoundKind;
        private NumericUpDown _numSoundCd;

        // 用户自备音频（放在「音频」文件夹里）
        private ComboBox _cboAudio;
        private Button _btnAudioTest;
        private Button _btnAudioReload;
        private TextBox _txtAudioPoints;

        // 条件显示
        private ComboBox _cboCondType;
        private NumericUpDown _numCondValue;
        private Label _lblCondUnit;

        public StyleEditor()
        {
            AutoScroll = true;
            BackColor = UiKit.PageBg;
            Font = UiKit.Font;

            TableLayoutPanel gText, gBack, gWall, gAnim, gShow, gCond, gSound;

            TableLayoutPanel stack = UiKit.Stack();
            UiKit.AddGroup(stack, UiKit.Group("显示方式", 3, out gShow));
            BuildShow(gShow);
            UiKit.AddGroup(stack, UiKit.Group("条件显示（满足条件才弹）", 4, out gCond));
            BuildCondition(gCond);
            UiKit.AddGroup(stack, UiKit.Group("提示音 / 自备音频", 8, out gSound));
            BuildSound(gSound);
            UiKit.AddGroup(stack, UiKit.Group("文字", 5, out gText));
            BuildText(gText);
            UiKit.AddGroup(stack, UiKit.Group("背景框与玻璃质感", 8, out gBack));
            BuildBack(gBack);
            UiKit.AddGroup(stack, UiKit.Group("背景壁纸", 5, out gWall));
            BuildWallpaper(gWall);
            UiKit.AddGroup(stack, UiKit.Group("弹出动画", 4, out gAnim));
            BuildAnim(gAnim);

            Controls.Add(stack);
        }

        /// <summary>提示音：这条提示响不响、响哪个。</summary>
        private void BuildSound(TableLayoutPanel g)
        {
            _chkSound = UiKit.Check("弹出时响一声", 200);
            _chkSound.CheckedChanged += Fire;
            UiKit.Row(g, "提示音", _chkSound, "要总开关也开着才响", false);

            _cboSoundKind = UiKit.Combo(false, 200);
            for (int i = 0; i < AlertSound.Names.Length; i++)
                _cboSoundKind.Items.Add((i + 1) + ". " + AlertSound.Names[i]);
            _cboSoundKind.SelectedIndexChanged += Fire;
            UiKit.Row(g, "音效", _cboSoundKind, "用 Windows 自带音效", false);

            _numSoundCd = UiKit.Num(3, 0, 60, 92);
            _numSoundCd.ValueChanged += Fire;
            UiKit.Row(g, "最短间隔", _numSoundCd, "秒内不重复响", false);

            UiKit.Note(g, "上面这些用的是系统自带音效；下面可以改用你自己放在「音频」文件夹里的音频。");

            // ---- 用户自备音频（操作者要求：界面上能选）----
            _cboAudio = UiKit.Combo(false, 260);
            ReloadAudioList();
            _cboAudio.SelectedIndexChanged += Fire;
            UiKit.Row(g, "自备音频", _cboAudio, "选「音频」文件夹里的文件", false);

            _btnAudioTest = UiKit.Button("试听", 70);
            _btnAudioTest.Click += delegate
            {
                try
                {
                    string f = SelectedAudioFile();
                    if (string.IsNullOrEmpty(f)) f = _globalAudioDefault;
                    if (string.IsNullOrEmpty(f))
                    {
                        MessageBox.Show("没有可试听的音频：\n\n"
                            + "先把音频文件（wav / mp3 等）丢进程序目录下的「音频」文件夹，\n"
                            + "点「刷新列表」，然后在上面选它。\n\n"
                            + "（也可以在 config.ini 的 [General] AudioDefault 里设一个全局默认音频。）",
                            "音频", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    AudioLibrary.PlayKey(f, "设置窗口里试听");
                }
                catch (Exception ex) { Logger.Err("音频", "试听失败", ex); }
            };
            UiKit.Row(g, "", _btnAudioTest, "立刻放一遍（不叠音：会停掉上一段）", false);

            _btnAudioReload = UiKit.Button("刷新列表", 90);
            _btnAudioReload.Click += delegate { ReloadAudioList(); };
            UiKit.Row(g, "", _btnAudioReload, "往「音频」文件夹丢完文件后点一下", false);

            // 【操作者要求】音量 / 裁剪（片段）都搬到「音频」页里按"这一段音频"设，
            // 这里只负责"这条提示用哪一段"。
            UiKit.Note(g, "音量、裁剪（选中音频 → 编辑 → 拖一段存成片段）都在左边的「音频」页里设，"
                + "这里只管「这条提示用哪一段」（也可以写 @片段名）。");

            _txtAudioPoints = UiKit.Text(260);
            _txtAudioPoints.TextChanged += Fire;
            UiKit.Row(g, "到点放音频", _txtAudioPoints, "形如 60=一分钟.mp3,30=紧张.mp3", true);
            UiKit.Note(g, "「到点放音频」只对倒计时类提示有用：剩的秒数降到某个值时放对应音频，");
            UiKit.Note(g, "每个阈值每轮只放一次。音频文件夹位置和用法见程序目录下「音频\\说明.txt」。");
        }

        /// <summary>把「音频」文件夹里的文件 + 自定义片段列进下拉框（第一项 = 用全局默认）。</summary>
        public void ReloadAudioList()
        {
            try
            {
                string keep = _cboAudio == null ? null : (string)_cboAudio.SelectedItem;
                if (_cboAudio == null) return;

                _cboAudio.Items.Clear();
                _cboAudio.Items.Add("（用全局默认 / 不响）");
                string[] files = AudioLibrary.List();
                for (int i = 0; i < files.Length; i++) _cboAudio.Items.Add(files[i]);

                // 自定义片段（在「音频」页里裁出来的）也要能在这里选
                List<AudioLibrary.Segment> segs = AudioLibrary.ParseSegments(SegmentsSpec);
                for (int i = 0; i < segs.Count; i++)
                    _cboAudio.Items.Add(AudioLibrary.SegmentPrefix + segs[i].Name);

                if (files.Length == 0 && segs.Count == 0)
                    _cboAudio.Items.Add("（「音频」文件夹里还没有文件）");

                if (!string.IsNullOrEmpty(keep))
                {
                    int idx = _cboAudio.Items.IndexOf(keep);
                    if (idx >= 0) { _cboAudio.SelectedIndex = idx; return; }
                }
                _cboAudio.SelectedIndex = 0;
            }
            catch { }
        }

        /// <summary>当前选中的音频文件名（"（用全局默认）"或占位项 → 空串）。</summary>
        private string SelectedAudioFile()
        {
            try
            {
                if (_cboAudio == null) return "";
                string s = _cboAudio.SelectedItem as string;
                if (string.IsNullOrEmpty(s)) return "";
                if (s.StartsWith("（")) return "";
                return s;
            }
            catch { return ""; }
        }

        /// <summary>条件显示：满足条件才弹，否则当作这条提示不存在。</summary>
        private void BuildCondition(TableLayoutPanel g)
        {
            _cboCondType = UiKit.Combo(false, 260);
            for (int i = 0; i < AlertCondition.Names.Length; i++)
                _cboCondType.Items.Add(AlertCondition.Names[i]);
            _cboCondType.SelectedIndexChanged += delegate
            {
                UpdateCondUnit();
                Fire();
            };
            UiKit.Row(g, "条件", _cboCondType, "什么时候才显示", false);

            _numCondValue = UiKit.Num(0, 0, 999999, 92);
            _numCondValue.ValueChanged += Fire;
            _lblCondUnit = UiKit.Row(g, "数值", _numCondValue, "", false);

            UiKit.Note(g, "例子：支援预警设成「票数 ≥ 15」，就只在票数够了之后才弹。");
            UiKit.Note(g, "「本回合有 079」这类条件依赖战术面板里填的内容。");
            UpdateCondUnit();
        }

        private void UpdateCondUnit()
        {
            if (_lblCondUnit == null || _cboCondType == null) return;
            int t = _cboCondType.SelectedIndex;
            bool needs = AlertCondition.NeedsValue(t);
            _numCondValue.Enabled = needs;
            _lblCondUnit.Text = AlertCondition.UnitOf(t);
        }

        /// <summary>显示方式：常驻不消失 / 由时间估算驱动。</summary>
        private void BuildShow(TableLayoutPanel g)
        {
            _chkPersistent = UiKit.Check("常驻显示，不自动缩回", 260);
            _chkPersistent.CheckedChanged += Fire;
            UiKit.Row(g, "常驻", _chkPersistent, "一直留在屏幕上", false);

            _chkEstimated = UiKit.Check("由时间估算驱动（净化 / 核弹 / 支援）", 340);
            _chkEstimated.CheckedChanged += Fire;
            UiKit.Row(g, "估算", _chkEstimated, "客户端模式下靠校准推算", false);

            UiKit.Note(g, "只有倒计时类提示适合开“估算”；开了之后即使日志里没有信息也能显示倒计时。");
        }

        private void BuildText(TableLayoutPanel g)
        {
            // 字体用 DropDownList：它的显示文字直接来自列表项，
            // 不会出现“托管侧 Text 已经改了、原生编辑框还显示旧值”的同步问题。
            _cboFont = UiKit.Combo(false, 230);
            FillFonts();
            _cboFont.SelectedIndexChanged += Fire;

            _numFontSize = UiKit.Num(20, 6, 200, 68);
            _numFontSize.ValueChanged += Fire;
            _chkBold = UiKit.Check("加粗", 76);
            _chkBold.CheckedChanged += Fire;

            UiKit.Row(g, "字体", _cboFont, "", false);
            UiKit.Row2(g, "字号", _numFontSize, 70, _chkBold, "");

            _btnTextColor = UiKit.ColorSwatch(120);
            _btnTextColor.Click += delegate
            {
                Pick(_btnTextColor, delegate (Color c) { _btnTextColor.BackColor = c; Fire(); });
            };
            UiKit.Row(g, "文字颜色", _btnTextColor, "点色块选择", false);

            Panel s1 = UiKit.Slider(10, 100, out _trkTextOpacity, out _lblTextOpacity);
            _trkTextOpacity.ValueChanged += Fire;
            UiKit.Row(g, "整体不透明度", s1, "", true);

            _chkShowIcon = UiKit.Check("在文字左边画一个小图标", 240);
            _chkShowIcon.CheckedChanged += Fire;
            UiKit.Row(g, "小标识", _chkShowIcon, "", false);
        }

        private void BuildBack(TableLayoutPanel g)
        {
            _btnBackColor = UiKit.ColorSwatch(120);
            _btnBackColor.Click += delegate
            {
                Pick(_btnBackColor, delegate (Color c) { _btnBackColor.BackColor = c; Fire(); });
            };
            UiKit.Row(g, "背景颜色", _btnBackColor, "点色块选择", false);

            Panel s1 = UiKit.Slider(0, 100, out _trkBackOpacity, out _lblBackOpacity);
            _trkBackOpacity.ValueChanged += Fire;
            UiKit.Row(g, "背景不透明度", s1, "", true);

            _numCorner = UiKit.Num(12, 0, 60, 70);
            _numCorner.ValueChanged += Fire;
            UiKit.Row(g, "圆角大小", _numCorner, "像素，0 = 直角", false);

            _chkGlass = UiKit.Check("开启玻璃质感", 200);
            _chkGlass.CheckedChanged += Fire;
            UiKit.Row(g, "玻璃质感", _chkGlass, "", false);

            Panel s2 = UiKit.Slider(0, 100, out _trkGlassStrength, out _lblGlassStrength);
            _trkGlassStrength.ValueChanged += Fire;
            UiKit.Row(g, "光泽强度", s2, "上亮下暗的渐变", true);

            Panel s3 = UiKit.Slider(0, 100, out _trkGlassHighlight, out _lblGlassHighlight);
            _trkGlassHighlight.ValueChanged += Fire;
            UiKit.Row(g, "顶部亮边", s3, "玻璃感主要靠它", true);

            Panel s4 = UiKit.Slider(0, 100, out _trkGlassNoise, out _lblGlassNoise);
            _trkGlassNoise.ValueChanged += Fire;
            UiKit.Row(g, "磨砂颗粒", s4, "叠一层淡噪点", true);

            UiKit.Note(g, "提示：这三项都是 0-100，调到多少就有多少；全部为 0 就是纯色块。");
        }

        private void BuildWallpaper(TableLayoutPanel g)
        {
            // 图片路径单独占一行（和其它行完全一样的排法，保证显示得出来）
            _txtWallpaper = UiKit.Text(200);
            _txtWallpaper.TextChanged += Fire;
            UiKit.Row(g, "图片文件", _txtWallpaper, "png/jpg/bmp/gif", true);

            // 每个按钮各占一行：不嵌套任何容器，就不会出现整行显示不出来的问题
            Button pick = UiKit.Button("选择图片…", 130);
            pick.Click += delegate { BrowseWallpaper(); };
            UiKit.Row(g, "选择图片", pick, "从磁盘挑一张", false);

            Button clear = UiKit.Button("清除", 90);
            clear.Click += delegate { _txtWallpaper.Text = ""; Fire(); };
            UiKit.Row(g, "清除壁纸", clear, "改回纯色背景", false);

            _cboWallMode = UiKit.Combo(false, 130);
            _cboWallMode.Items.AddRange(new object[] { "拉伸铺满", "保持比例", "平铺", "居中" });
            _cboWallMode.SelectedIndexChanged += Fire;
            UiKit.Row(g, "铺法", _cboWallMode, "", false);

            Panel s1 = UiKit.Slider(0, 100, out _trkWallOpacity, out _lblWallOpacity);
            _trkWallOpacity.ValueChanged += Fire;
            UiKit.Row(g, "壁纸浓度", s1, "0 = 只用底色", true);
        }

        private void BuildAnim(TableLayoutPanel g)
        {
            _cboAnim = UiKit.Combo(false, 150);
            _cboAnim.Items.AddRange(new object[]
            {
                "无（直接出现）", "只淡入淡出", "从上方滑入", "从下方滑入",
                "从左侧滑入", "从右侧滑入", "由小放大", "由大缩小"
            });
            _cboAnim.SelectedIndexChanged += Fire;
            UiKit.Row(g, "方式", _cboAnim, "", false);

            _numAnimIn = UiKit.Num(220, 0, 3000, 76);
            _numAnimIn.ValueChanged += Fire;
            UiKit.Row(g, "入场时长", _numAnimIn, "毫秒", false);

            _numAnimOut = UiKit.Num(160, 0, 3000, 76);
            _numAnimOut.ValueChanged += Fire;
            UiKit.Row(g, "出场时长", _numAnimOut, "毫秒", false);

            _numSlide = UiKit.Num(12, 0, 200, 76);
            _numSlide.ValueChanged += Fire;
            UiKit.Row(g, "滑入距离", _numSlide, "像素（滑入类有效）", false);
        }

        private void FillFonts()
        {
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    FontFamily[] fams = col.Families;
                    System.Collections.Generic.List<string> names =
                        new System.Collections.Generic.List<string>();
                    for (int i = 0; i < fams.Length; i++) names.Add(fams[i].Name);
                    names.Sort(StringComparer.CurrentCultureIgnoreCase);
                    _cboFont.Items.AddRange(names.ToArray());
                }
            }
            catch { }
            // 别让“加到第一项就自动选中”留下一个默认值，选谁由 Load 决定
            _cboFont.SelectedIndex = -1;
        }

        private void Fire(object sender, EventArgs e) { Fire(); }

        private void Fire()
        {
            if (_loading) return;
            UpdatePercents();
            if (Changed != null) Changed();
        }

        private void UpdatePercents()
        {
            _lblTextOpacity.Text = _trkTextOpacity.Value + "%";
            _lblBackOpacity.Text = _trkBackOpacity.Value + "%";
            _lblGlassStrength.Text = _trkGlassStrength.Value.ToString();
            _lblGlassHighlight.Text = _trkGlassHighlight.Value.ToString();
            _lblGlassNoise.Text = _trkGlassNoise.Value.ToString();
            _lblWallOpacity.Text = _trkWallOpacity.Value.ToString();
        }

        private void Pick(Button owner, Action<Color> apply)
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = owner.BackColor;
                d.FullOpen = true;
                d.AnyColor = true;
                if (d.ShowDialog(this) == DialogResult.OK) apply(d.Color);
            }
        }

        private void BrowseWallpaper()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择一张图片当提示框壁纸";
                d.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _txtWallpaper.Text = d.FileName;
                    Fire();
                }
            }
        }

        #region 读 / 写

        public void Load(WindowStyle s, AlertConfig a)
        {
            _loading = true;
            try
            {
                if (a != null)
                {
                    _chkPersistent.Checked = a.Persistent;
                    _chkEstimated.Checked = a.Estimated;
                    // 只有倒计时类提示才谈得上“估算驱动”
                    _chkEstimated.Enabled = a.HasCountdown;
                    if (!a.HasCountdown) _chkEstimated.Checked = false;
                }
                // 字体：配置里的名字可能不在系统字体列表里（比如“微软雅黑”在某些系统上
                // 只会列成 Microsoft YaHei）。先把名字补进列表再按索引选中，
                // 这样既不会丢字体，也不会被下拉框的自动补全改写。
                string face = s.FontFace;
                if (string.IsNullOrEmpty(face)) face = "微软雅黑";
                if (!_cboFont.Items.Contains(face)) _cboFont.Items.Add(face);
                int fi = _cboFont.Items.IndexOf(face);
                if (fi >= 0) _cboFont.SelectedIndex = fi;
                else _cboFont.Text = face;

                _numFontSize.Value = ClampD(s.FontSize, _numFontSize);
                _chkBold.Checked = s.Bold;
                _btnTextColor.BackColor = s.TextColor;
                _trkTextOpacity.Value = ClampI(s.Opacity, _trkTextOpacity);
                _chkShowIcon.Checked = s.ShowIcon;

                _btnBackColor.BackColor = s.BackColor;
                _trkBackOpacity.Value = ClampI(s.BackOpacity, _trkBackOpacity);
                _numCorner.Value = ClampD(s.CornerRadius, _numCorner);
                _chkGlass.Checked = s.Glass;
                _trkGlassStrength.Value = ClampI(s.GlassStrength, _trkGlassStrength);
                _trkGlassHighlight.Value = ClampI(s.GlassHighlight, _trkGlassHighlight);
                _trkGlassNoise.Value = ClampI(s.GlassNoise, _trkGlassNoise);

                _txtWallpaper.Text = s.Wallpaper;
                _cboWallMode.SelectedIndex = ClampI((int)s.WallpaperMode, 0, 3);
                _trkWallOpacity.Value = ClampI(s.WallpaperOpacity, _trkWallOpacity);

                _cboAnim.SelectedIndex = ClampI((int)s.Anim, 0, 7);
                _numAnimIn.Value = ClampD(s.AnimInMs, _numAnimIn);
                _numAnimOut.Value = ClampD(s.AnimOutMs, _numAnimOut);
                _numSlide.Value = ClampD(s.SlideDistance, _numSlide);

                if (a != null)
                {
                    _chkSound.Checked = a.SoundOn;
                    _cboSoundKind.SelectedIndex = ClampI(a.SoundKind - 1, 0, AlertSound.Names.Length - 1);
                    _numSoundCd.Value = ClampD(a.SoundCooldown, _numSoundCd);

                    // 自备音频：先保证列表是最新的，再选中这条提示设的那个文件
                    ReloadAudioList();
                    if (!string.IsNullOrEmpty(a.AudioFile))
                    {
                        int ai = _cboAudio.Items.IndexOf(a.AudioFile);
                        if (ai < 0)
                        {
                            _cboAudio.Items.Add(a.AudioFile);   // 文件不在了也留着，别悄悄丢掉设置
                            ai = _cboAudio.Items.IndexOf(a.AudioFile);
                        }
                        _cboAudio.SelectedIndex = ai;
                    }
                    else _cboAudio.SelectedIndex = 0;
                    _txtAudioPoints.Text = a.AudioPoints;

                    _cboCondType.SelectedIndex = ClampI(a.CondType, 0, AlertCondition.Names.Length - 1);
                    _numCondValue.Value = ClampD(a.CondValue, _numCondValue);
                }
                UpdateCondUnit();
            }
            finally
            {
                _loading = false;
            }
            UpdatePercents();
        }

        public void Save(WindowStyle s, AlertConfig a)
        {
            if (a != null)
            {
                a.Persistent = _chkPersistent.Checked;
                a.Estimated = _chkEstimated.Enabled && _chkEstimated.Checked;

                a.SoundOn = _chkSound.Checked;
                a.SoundKind = Math.Max(0, _cboSoundKind.SelectedIndex) + 1;
                a.SoundCooldown = (int)_numSoundCd.Value;
                a.AudioFile = SelectedAudioFile();
                a.AudioPoints = _txtAudioPoints.Text == null ? "" : _txtAudioPoints.Text.Trim();

                a.CondType = Math.Max(0, _cboCondType.SelectedIndex);
                a.CondValue = (int)_numCondValue.Value;
            }

            s.FontFace = _cboFont.Text;
            s.FontSize = (float)_numFontSize.Value;
            s.Bold = _chkBold.Checked;
            s.TextColor = _btnTextColor.BackColor;
            s.Opacity = _trkTextOpacity.Value;
            s.ShowIcon = _chkShowIcon.Checked;

            s.BackColor = _btnBackColor.BackColor;
            s.BackOpacity = _trkBackOpacity.Value;
            s.CornerRadius = (int)_numCorner.Value;
            s.Glass = _chkGlass.Checked;
            s.GlassStrength = _trkGlassStrength.Value;
            s.GlassHighlight = _trkGlassHighlight.Value;
            s.GlassNoise = _trkGlassNoise.Value;

            s.Wallpaper = _txtWallpaper.Text.Trim();
            s.WallpaperMode = (WallpaperMode)Math.Max(0, _cboWallMode.SelectedIndex);
            s.WallpaperOpacity = _trkWallOpacity.Value;

            s.Anim = (AnimKind)Math.Max(0, _cboAnim.SelectedIndex);
            s.AnimInMs = (int)_numAnimIn.Value;
            s.AnimOutMs = (int)_numAnimOut.Value;
            s.SlideDistance = (int)_numSlide.Value;
        }

        /// <summary>按当前界面上的值生成一份用于预览的素材。</summary>
        public AlertVisual MakeVisual(WindowStyle s, AlertConfig a, AlertIcon icon, string sample)
        {
            Save(s, a);
            AlertVisual v = new AlertVisual();
            v.Text = sample;
            v.Font = new Font(OverlayForm.ResolveFontFamily(s.FontFace), s.FontSize,
                              s.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            v.TextColor = s.TextColor;
            v.TextOpacity = s.Opacity;
            v.BackColor = s.BackColor;
            v.BackOpacity = s.BackOpacity;
            v.CornerRadius = s.CornerRadius;
            v.Icon = icon;
            v.ShowIcon = s.ShowIcon;
            v.Wallpaper = AlertRenderer.LoadWallpaper(s.Wallpaper);
            v.WallpaperMode = s.WallpaperMode;
            v.WallpaperOpacity = s.WallpaperOpacity;
            v.Glass = s.Glass;
            v.GlassStrength = s.GlassStrength;
            v.GlassHighlight = s.GlassHighlight;
            v.GlassNoise = s.GlassNoise;
            return v;
        }

        private static decimal ClampD(double v, NumericUpDown n)
        {
            decimal d = (decimal)v;
            if (d < n.Minimum) return n.Minimum;
            if (d > n.Maximum) return n.Maximum;
            return d;
        }

        private static int ClampI(int v, TrackBar t)
        {
            if (v < t.Minimum) return t.Minimum;
            if (v > t.Maximum) return t.Maximum;
            return v;
        }

        private static int ClampI(int v, NumericUpDown n)
        {
            int lo = (int)n.Minimum, hi = (int)n.Maximum;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static int ClampI(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        #endregion
    }
}
