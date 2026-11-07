using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    #region 通用 INI 读写

    internal class IniSection
    {
        public string Name;
        public List<string> KeyOrder = new List<string>();
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IniSection(string name) { Name = name; }

        public void Set(string key, string value)
        {
            if (!Values.ContainsKey(key)) KeyOrder.Add(key);
            Values[key] = value;
        }

        public string Get(string key, string def)
        {
            string v;
            if (Values.TryGetValue(key, out v)) return v;
            return def;
        }
    }

    internal class IniFile
    {
        private readonly List<IniSection> _sections = new List<IniSection>();

        public IniSection Section(string name)
        {
            for (int i = 0; i < _sections.Count; i++)
                if (string.Equals(_sections[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _sections[i];
            IniSection s = new IniSection(name);
            _sections.Add(s);
            return s;
        }

        public string Get(string section, string key, string def)
        {
            for (int i = 0; i < _sections.Count; i++)
                if (string.Equals(_sections[i].Name, section, StringComparison.OrdinalIgnoreCase))
                    return _sections[i].Get(key, def);
            return def;
        }

        public void Set(string section, string key, string value) { Section(section).Set(key, value); }

        public static IniFile Load(string path)
        {
            IniFile ini = new IniFile();
            if (!File.Exists(path)) return ini;

            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { return ini; }

            IniSection cur = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int e = line.IndexOf(']');
                    if (e > 1) { cur = ini.Section(line.Substring(1, e - 1).Trim()); continue; }
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                cur.Set(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return ini;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; ============================================================");
            sb.AppendLine("; SCPSLAlert 配置文件");
            sb.AppendLine("; 所有外观都能在设置窗口里改，也可以直接改这里。");
            sb.AppendLine("; 颜色格式：R,G,B（例如 255,255,255）");
            sb.AppendLine("; 透明度 / 各种强度：0-100");
            sb.AppendLine("; 坐标：X/Y 是物理像素，XPercent/YPercent 是 0..1 归一化坐标（基准见注释）");
            sb.AppendLine("; ============================================================");
            sb.AppendLine();

            for (int i = 0; i < _sections.Count; i++)
            {
                IniSection s = _sections[i];
                sb.Append('[').Append(s.Name).AppendLine("]");
                for (int k = 0; k < s.KeyOrder.Count; k++)
                    sb.Append(s.KeyOrder[k]).Append('=').AppendLine(s.Values[s.KeyOrder[k]]);
                sb.AppendLine();
            }

            ConfigBackup.WriteAtomic(path, sb.ToString());
        }
    }

    #endregion


    #region 配置模型

    /// <summary>单个提示窗口的完整样式（每种提示一份，可以分别调）。</summary>
    internal class WindowStyle
    {
        public bool Enabled = true;
        public int X = 30;
        public int Y = 30;

        // ---- 坐标（第 3 批）----
        // 归一化 0..1，基准 = 目标显示器物理像素（左上角原点，X 向右、Y 向下）。
        // X/Y 仍保留为物理像素，给旧 WinForms 界面和回滚用；保存时两者同时写。
        public double XPercent = double.NaN;
        public double YPercent = double.NaN;
        public string Monitor = "";

        // 内部同步标记：上一次“X/Y 与归一化坐标一致”时的物理像素。
        // 旧界面拖动只会改 X/Y，保存时用它判断该以哪一边为准。
        public int LegacySyncedX = int.MinValue;
        public int LegacySyncedY = int.MinValue;

        // ---- 文字 ----
        public string FontFace = "微软雅黑";
        public float FontSize = 20f;
        public bool Bold = true;
        public Color TextColor = Color.White;
        public int Opacity = 100;

        // ---- 背景框 ----
        public Color BackColor = Color.FromArgb(10, 14, 20);
        public int BackOpacity = 62;
        public int CornerRadius = 12;

        // ---- 壁纸 ----
        public string Wallpaper = "";
        public WallpaperMode WallpaperMode = WallpaperMode.Stretch;
        public int WallpaperOpacity = 100;

        // ---- 玻璃质感 ----
        public bool Glass = false;
        public int GlassStrength = 55;
        public int GlassHighlight = 70;
        public int GlassNoise = 12;

        // ---- 动画 ----
        public AnimKind Anim = AnimKind.FromTop;
        public int AnimInMs = 220;
        public int AnimOutMs = 160;
        public int SlideDistance = 12;

        // ---- 小标识 ----
        public AlertIcon Icon = AlertIcon.None;
        public bool ShowIcon = true;

        public void CopyFrom(WindowStyle s)
        {
            Enabled = s.Enabled; X = s.X; Y = s.Y;
            XPercent = s.XPercent; YPercent = s.YPercent; Monitor = s.Monitor;
            LegacySyncedX = s.LegacySyncedX; LegacySyncedY = s.LegacySyncedY;
            FontFace = s.FontFace; FontSize = s.FontSize; Bold = s.Bold;
            TextColor = s.TextColor; Opacity = s.Opacity;
            BackColor = s.BackColor; BackOpacity = s.BackOpacity; CornerRadius = s.CornerRadius;
            Wallpaper = s.Wallpaper; WallpaperMode = s.WallpaperMode; WallpaperOpacity = s.WallpaperOpacity;
            Glass = s.Glass; GlassStrength = s.GlassStrength;
            GlassHighlight = s.GlassHighlight; GlassNoise = s.GlassNoise;
            Anim = s.Anim; AnimInMs = s.AnimInMs; AnimOutMs = s.AnimOutMs; SlideDistance = s.SlideDistance;
            Icon = s.Icon; ShowIcon = s.ShowIcon;
        }
    }

    /// <summary>一种提示的完整配置。</summary>
    internal class AlertConfig
    {
        public string Section = "";
        public string DisplayName = "";
        public string SampleText = "";
        public string SampleText2 = "";
        public string SampleText3 = "";

        /// <summary>倒计时类提示：有 Duration / EndText / 高亮阈值。</summary>
        public bool HasCountdown = false;

        /// <summary>常驻显示，不自动缩回（所有提示都可以开）。</summary>
        public bool Persistent = false;

        /// <summary>
        /// 这个倒计时受“时间估算”驱动（核弹 / 净化 / 支援）。
        /// 打开后：加入别人的服务器时也能靠估算显示倒计时，
        /// 并且可以在校准面板里按剩余时间直接校准。
        /// </summary>
        public bool Estimated = false;

        /// <summary>自动钉在屏幕正上方居中（回合时间用）。</summary>
        public bool PinTopCenter = false;

        /// <summary>
        /// 每分钟只露一小段，不一直挂在屏幕上
        /// （支援/阵营刷新这种长周期倒计时用）。
        /// </summary>
        public bool FlashPerMinute = false;

        /// <summary>每分钟露脸多少秒。</summary>
        public int FlashSeconds = 10;

        /// <summary>
        /// "隔多久露一段"的节拍（秒）。默认 60（= 每逢整分钟露一段，老行为）；
        /// 操作者要求支援刷新那条改成 30（没勾常驻就每 30 秒报一次）。
        /// `[提示段落] FlashPeriodSeconds` 可改。
        /// </summary>
        public int FlashPeriodSeconds = 60;

        /// <summary>最后多少秒持续显示（即将刷新）。</summary>
        public int SoonSeconds = 30;

        /// <summary>这条提示弹出时是否响一声。</summary>
        public bool SoundOn = false;

        /// <summary>
        /// 用哪个系统音：0=不响 1=提示 2=感叹 3=警告 4=疑问 5=默认
        /// 用系统自带音效，不依赖任何外部 wav 文件，保持绿色单文件。
        /// </summary>
        public int SoundKind = 1;

        /// <summary>条件显示：满足条件才弹（见 AlertCondition）。</summary>
        public int CondType = 0;
        public int CondValue = 0;

        /// <summary>响一声之后多少秒内不再重复响（避免连刷时吵）。</summary>
        public int SoundCooldown = 3;

        /// <summary>
        /// 这条提示弹出时播放的音频文件名（放在「音频」文件夹里；留空 = 用全局默认）。
        /// 例：九尾大波.mp3
        /// </summary>
        public string AudioFile = "";

        /// <summary>
        /// 倒计时类提示的"到点放音频"表，逗号分隔，形如：
        ///     60=还剩一分钟.mp3, 30=紧张.mp3, 10=最后警告.mp3
        /// 含义：剩的秒数降到这个值时放对应的音频（每个阈值只会放一次）。
        /// 留空 = 不放。
        /// </summary>
        public string AudioPoints = "";

        /// <summary>需求八.1：这条提示的时间格式；-1 = 跟随全局，0 自动，1 秒，2 分秒，3 时分。</summary>
        public int TimeFormat = -1;

        public WindowStyle Style = new WindowStyle();
        public string Format = "{0}";
        public int DurationSeconds = 60;
        public string EndText = "";
        public string AltText = "";

        public int[] PulseAt = new int[0];
        public int PulseSeconds = 3;
        public int HotBelow = 0;
    }

    internal class Config
    {
        /// <summary>提示音总开关（所有提示一起管）。</summary>
        public bool SoundEnabled = true;

        public string LogDirectory = "";
        public int PollIntervalMs = 500;
        public int GamePollMs = 2000;
        public int DisplaySeconds = 5;
        /// <summary>需求八.1：所有带时间的提示的默认格式（0 自动 / 1 秒 / 2 分秒 / 3 时分）。</summary>
        public int TimeFormat = 0;
        public bool GameBrightnessEnabled = false;
        public int GameBrightnessIntervalMs = 2000;
        public bool GameBrightnessCaptureMotionOnly = false;
        public bool GameBrightnessHoldEnabled = false;
        public int GameBrightnessHoldGamma = 80;
        public int GameBrightnessHoldSaturation = 107;
        public bool GameBrightnessForceGlobal = false;
        // 3–7 组“画面亮度:伽马指数”，格式如 35:0.82;80:0.78;...
        public string GameBrightnessProfile = "";
        public string GameBrightnessSaturationProfile = "";
        public string GameBrightnessPresets = "";

        /// <summary>
        /// 需求一.4：调伽马时自动补偿饱和度的增强量（0–0.50，默认 0.10）。
        /// 公式：现有补偿系数 0.35 ×（1 + 这个值）。
        /// </summary>
        public double GameBrightnessCompensationBoost = 0.10;

        /// <summary>当前选中的预设名（空 = 自定义调整）。</summary>
        public string GameBrightnessPresetCurrent = "";

        // 游戏辅助准星（默认关闭）
        public bool CrosshairEnabled = false;
        public int CrosshairShape = 0;
        public int CrosshairColor = unchecked((int)0xFFFFFF00);
        public int CrosshairOutlineColor = unchecked((int)0xFF000000);
        public int CrosshairSize = 16;
        public int CrosshairThickness = 2;
        public int CrosshairGap = 5;
        public int CrosshairOpacity = 90;
        public bool CrosshairOutline = true;
        public bool CrosshairDot = false;
        public int CrosshairOffsetX = 0;
        public int CrosshairOffsetY = 0;
        public string CrosshairRingKey = "E";
        public int CrosshairRingDurationMs = 1000;
        public int CrosshairRingRadius = 34;
        public string CrosshairExpandKey = "Space";
        public int CrosshairExpandDurationMs = 1000;
        public int CrosshairExpandAmount = 14;

        // ---- 新版准星工作台（旧键保留：老界面照旧写上面那组，保存时双向对齐）----
        public int CrosshairType = 0;              // 0 十字 1 圆环 2 纯点 3 菱形 4 方框 5 三角 6 内线+外线
        public float CrosshairLength = 16f;
        public float CrosshairLineGap = 5f;        // 允许负数：负值把线拉向中心
        public float CrosshairLineThickness = 2f;
        public float CrosshairDotSize = 0f;        // 0 = 不画中心点
        public float CrosshairRotation = 0f;
        public float CrosshairRingRadiusF = 26f;
        public int CrosshairRingEase = 3;          // 0 线性 1 先快后慢 2 先慢后快 3 平滑 4 回弹
        public bool CrosshairTShape = false;
        public bool CrosshairOuterEnabled = true;
        public float CrosshairOuterLength = 6f;
        public float CrosshairOuterThickness = 2f;
        public float CrosshairOuterGap = 6f;
        public int CrosshairOutlineOpacity = 100;
        public float CrosshairOutlineWidth = 2f;
        public bool CrosshairDynamic = true;
        public bool CrosshairFireSpread = true;
        public float CrosshairFireAmount = 14f;
        public int CrosshairFireDurationMs = 120;
        public int CrosshairFireCurve = 0;         // 0 指数 1 线性 2 先撑后落
        public float CrosshairFireMaxMul = 2f;
        public bool CrosshairHitFlash = true;
        public bool CrosshairDpiScale = true;
        public bool CrosshairSnapCenter = true;
        public int CrosshairRenderFps = 240;       // 覆盖层渲染线程目标帧率

        /// <summary>
        /// 动画效果档位：0=关 1=低 2=中 3=高（默认） 4=极高。
        /// 档位同时决定**帧率**（30/45/60/120fps）和**特效多少**（发光 / 收尾飞线 / 脉冲）。
        /// 见 UiAnim.cs；战术面板的全息展开动画时长也按档位在 1.5~1.9 秒之间。
        /// </summary>
        public int PanelAnimQuality = 3;
        public int OffsetX = 0;
        public int OffsetY = 0;

        /// <summary>第 8 批：普通提示防重叠总开关。</summary>
        public bool AlertAvoidEnabled = true;

        /// <summary>第 8 批：提示之间最小间隙（像素）。</summary>
        public int AlertAvoidGapPx = 8;

        /// <summary>第 8 批：工作区边界内边距（像素）。</summary>
        public int AlertAvoidMarginPx = 8;

        /// <summary>第 8 批：避让缓动时长（毫秒）；0 = 瞬移。</summary>
        public int AlertAvoidAnimMs = 180;

        // ---------------- 第 9 批：一图流 / 导出 ----------------
        /// <summary>导出目录；空 = 程序目录\导出。</summary>
        public string ExportDir = "";
        /// <summary>右上角数据来源水印。</summary>
        public bool ExportWatermark = true;
        /// <summary>水印里是否带玩家标识（本机用户名）。</summary>
        public bool ExportShowPlayer = false;
        /// <summary>按对局分割：1 = 每局一张；0 = 完整长图。</summary>
        public bool ExportSplitByMatch = true;
        /// <summary>导出对话框默认标签：1 = 一图流，0 = 普通图。</summary>
        public int ExportDefaultTab = 1;
        /// <summary>额外导出 txt 事件解读。</summary>
        public bool ExportTxtReadout = false;
        /// <summary>超过这个高度切成 _part01。</summary>
        public int ExportPartMaxPx = 10000;
        /// <summary>一图流是否画生理轨。</summary>
        public bool OneMapShowHeart = true;
        /// <summary>每条折线 LTTB 抽稀上限。</summary>
        public int OneMapLttb = 1000;
        /// <summary>每条曲线数字标注上限。</summary>
        public int OneMapLabels = 0;   // 默认不标数字，导出前可勾选
        /// <summary>多局纵向拼接间隔（像素）。</summary>
        public int OneMapBlockGap = 40;
        /// <summary>保留多少局原始对局记录。</summary>
        public int MatchKeep = 30;
        /// <summary>一图流 FPS 是否画平滑线（默认原始）。</summary>
        public bool OneMapFpsSmooth;
        /// <summary>一图流是否画其他持续事件（净化/支援/发电机），默认关。</summary>
        public bool OneMapExtraDurations;
        /// <summary>对局记录是否记心率。</summary>
        public bool MatchRecordHeart = true;
        /// <summary>第 10 批：新界面点击特效。</summary>
        public bool ClickFxEnabled = true;
        /// <summary>点击特效起始缺口（像素）。</summary>
        public int ClickFxGap = 84;
        /// <summary>进服后限时扫描 SCP 列表的秒数；0 = 进服不扫，仍等第一次死亡。</summary>
        public int ScpScanJoinSeconds = 30;
        /// <summary>第 10 批：旧快捷键（核弹取消后重启 / 核弹切换 / SCP 阵亡）已释放过一次。</summary>
        public bool HotkeyReleaseV1;

        /// <summary>
        /// 第 3 批坐标体系版本。旧配置没有这个键时，会把旧物理像素一次性折算成
        /// 0..1 归一化坐标；折算只读当前屏幕，不改用户原来的 X/Y。
        /// </summary>
        public int CoordV = 0;

        /// <summary>
        /// A.R.C.T 状态页 / 性能页显示哪些资源曲线。
        /// 逗号分隔：cpu,mem,gpu,net,disk,temp；空 = 全显示，none = 全隐藏。
        /// temp 只有在系统能读到 ACPI 温度时才会出现。
        /// </summary>
        public string StatusSeries = "";

        public string HotkeyAdjust = "Ctrl+Alt+F8";
        public string HotkeyToggleAll = "Ctrl+Alt+F9";
        public string HotkeySettings = "Ctrl+Alt+F10";
        public string HotkeyPanel = "Ctrl+Alt+F7";
        public string HotkeyHoldShowAll = "Ctrl+Alt+H";

        /// <summary>打开战术面板的「校准」页（时间校准；默认 F7）。</summary>
        public string HotkeyCalibrate = "F7";

        /// <summary>核弹：开始 90 秒。</summary>
        public string HotkeyWarhead = "F8";

        /// <summary>核弹：重置为 80 秒并继续（模拟取消重启）。</summary>
        public string HotkeyWarheadShift = "Shift+F8";

        /// <summary>核弹：强制 90 秒且无法取消（模拟亡者协议）。</summary>
        public string HotkeyWarheadCtrl = "Ctrl+F8";

        /// <summary>净化：从 705 秒开始倒计时。</summary>
        public string HotkeyDecon = "F9";

        /// <summary>打开悬浮式战术面板（票数估算）。</summary>
        public string HotkeyTactical = "F10";

        /// <summary>“SCP 已阵亡，注意支援刷新”提醒。</summary>
        public string HotkeyScpDead = "F6";

        /// <summary>刷新校准：记录“刚刷了一波”，按上一波顺延大波 / 小波，刷新计时重新起算。</summary>
        public string HotkeyBigWave = "F11";

        /// <summary>回合计时：手动暂停 / 继续。</summary>
        public string HotkeyClockPause = "F5";

        /// <summary>进入服务器时自动弹出校准窗口。</summary>
        public bool AutoCalibrateOnJoin = true;

        /// <summary>
        /// 净化倒计时的基础时长（需求五）。
        /// 收到"净化程序启动"播报之后从这里往下走：默认 705 秒 = 11 分 45 秒。
        /// </summary>
        public int DeconBaseSeconds = 705;

        /// <summary>
        /// 净化"播报时间 → 现实秒数"的映射（需求五.3）。
        ///
        /// 实测：C.A.S.S.I.E. 播报的时刻和现实剩余时间不是线性关系，
        /// 所以直接给一张对照表：左边是播报里说的（m = 分钟，s = 秒），
        /// 右边是现实该剩多少秒。表里没有的播报时间就用播报值本身。
        /// </summary>
        public string DeconAnnounceMap = "15m=652,10m=456,5m=258,1m=60,30s=30";

        /// <summary>净化播报同步的容差（秒）：相差不到这个数就不改，防跳字（需求五.5）。</summary>
        public int DeconSyncTolerance = 10;

        /// <summary>中途加入的时间校准提示总开关。</summary>
        public bool CalibPromptEnabled = true;
        /// <summary>数字播报（15/10/5/1 分钟）触发的校准提示。</summary>
        public bool CalibNumberPrompt = true;
        /// <summary>“开始清除/封锁”播报触发的校准提示。</summary>
        public bool CalibLockPrompt = true;
        /// <summary>校准提示停留秒数（到点自动校准）。</summary>
        public int CalibPromptSeconds = 5;

        /// <summary>核弹播报同步的容差（秒）（需求四.2）。OcrAuthoritative=1 时不用它。</summary>
        public int WarheadSyncTolerance = 5;

        /// <summary>
        /// Shift+F8「取消后重启」用的秒数（默认 80）。
        ///
        /// 【为什么要有这个键】以前这个 80 是写死在代码里的，
        /// 玩家想改成自己的服务器口径（比如 40 / 60）只能改源码。
        /// 现在它是 config.ini 的 `[Warhead] RestartSeconds`，设置里「时间」页也能直接填。
        /// </summary>
        public int WarheadRestartSeconds = 80;

        /// <summary>
        /// 「重生令牌」提示的默认位置（操作者要求：丢在屏幕下方）。
        /// 876/880 这一带在 1080p 上属于下半屏，又不会和屏幕底端的票数提示叠在一起。
        /// </summary>
        public const int TokenAlertPositionX = 620;
        public const int TokenAlertPositionY = 880;

        /// <summary>
        /// 重生概率提示的阈值（百分比）：人类整体"还能再重生一波"的概率
        /// 涨过这个数就弹一条（操作者要求）。默认 60。
        /// </summary>
        public int RespawnAlertPercent = 60;

        /// <summary>
        /// 音频文件夹（默认 = 程序目录下的「音频」）。用户可以往里丢 wav / mp3 等，
        /// 再在「提示外观」里选"哪条提示放哪段音频"（也能直接改 config.ini）。
        /// </summary>
        public string AudioDir = "音频";

        /// <summary>
        /// 默认音频：任何提示弹出时，如果那条没单独指定音频，就放这一段
        /// （留空 = 不放）。操作者要的"整体设置"就是这个。
        /// </summary>
        public string AudioDefault = "";

        /// <summary>
        /// 自定义音频片段（在「音频」页里：选中音频 → 编辑 → 拖一段 → 保存为片段）。
        /// 格式：`片段名=文件名|起始秒|结束秒`，多条用 `;` 或换行分隔。
        /// 这一段就是"从某个音频里裁出来的一截"，不改动原文件；
        /// 提示里引用它就写 `@片段名`（见 <see cref="AlertConfig.AudioFile"/>）。
        /// </summary>
        public string AudioSegments = "";

        /// <summary>
        /// 每段音频（或片段）自己的音量：`键=0~100`，多条用 `;` 分隔。
        /// 键 = 文件名（含扩展名）或 `@片段名`；没写的按 100 算。
        /// </summary>
        public string AudioVolumes = "";

        /// <summary>自定义音频触发点（一行一条，见 AudioTriggers.cs）。</summary>
        public string AudioTriggerSpec = "";

        /// <summary>解析出来的触发点列表（由 AudioTriggerSpec 解析，改完要重新解析）。</summary>
        public List<AudioTrigger> AudioTriggers = new List<AudioTrigger>();

        /// <summary>
        /// 核弹倒计时以 OCR 识别到的秒数为准（操作者要求）。
        /// 默认 1：读到"X 秒后自毁"就照它重新起算，不再"容差内不动"。
        /// 设 0 就退回老行为（按 WarheadSyncTolerance 防抖）。
        /// </summary>
        public bool WarheadOcrAuthoritative = true;

        /// <summary>
        /// 净化兜底：新回合 + OCR 一直没认出净化播报时，
        /// 按"回合已进行到 DeconAfterRound 秒"自动开始净化倒计时。
        ///
        /// 为什么要它：净化改成"必须识别到播报才启动"之后，
        /// 万一那段播报被 OCR 漏掉（画面被挡、字太暗、识别抖动），
        /// 净化倒计时就永远不会出现。有了兜底就退回按回合时间推算，
        /// 最多几秒误差，不会整局都没有。
        ///
        /// 中途加入（没检测到回合开始）不走这个兜底 —— 那种自己按 F9 手动起。
        /// </summary>
        public bool DeconFallback = true;

        /// <summary>
        /// 重生令牌阈值（需求二）：票数达到这些值时各弹一次提示。
        /// 逗号分隔，默认 30/80/150/200；"第几个令牌"就按这个顺序数。
        /// </summary>
        /// <summary>
        /// 日志里"回合开始"比游戏真实开始早几秒（实测约 5 秒）→ 起点往后挪这么多。
        /// </summary>
        public int RoundStartOffsetSeconds = 5;

        /// <summary>预计大波刷新时间的修正（秒，实测比推算晚约 15 秒）。</summary>
        public int WaveOffsetSeconds = 15;

        /// <summary>
        /// 大波基准间隔（秒）：两次重生波之间的基准时间。
        /// 以前写死在 TacticalLog 里（300），现在可以在「时间」页改 / 用日志校准。
        /// 预测时的实际间隔 = 它 + 上一波人数 × WavePerPlayerSeconds。
        /// </summary>
        public int WaveIntervalSeconds = 300;

        /// <summary>重生方每重生一个玩家，计时器额外加多少秒（默认 10，以前写死）。</summary>
        public int WavePerPlayerSeconds = 10;

        /// <summary>
        /// 重生令牌阈值：影响力达到这些数值时各得一个令牌。
        /// 官方（机制\刷新.txt）是 40 / 80 / 150 / 200 —— 老版本的默认值写成
        /// 30/80/150/200，第一个阈值就错了，这里改回官方值。
        /// </summary>
        public string TokenThresholds = "40,80,150,200";

        /// <summary>小波条件：大波刷新后这么多秒内打够 MiniWaveTickets 票（需求二）。</summary>
        public int MiniWaveWindowSeconds = 120;

        /// <summary>小波条件里"多少票"（需求二，默认 10）。</summary>
        public int MiniWaveTickets = 10;

        /// <summary>
        /// 小波最早在主波后多少秒出现（机制\刷新.txt 1.2.4：默认 2 分 30 秒 = 150）。
        /// 对不上自己服务器的节奏就改这里（实测偏晚的话往上加，比如 190）。
        /// 影响面：面板「推测下一次：小波 约 mm:ss 后」和「即将刷新大波 / 小波」那条标签。
        /// </summary>
        public int MiniWaveDelaySeconds = 150;

        /// <summary>
        /// 回合开始后多少秒净化倒计时开始（秒）—— 兜底用，也是"出生点不在轻收容"时唯一的依据。
        ///
        /// 【实测值（操作者日志 7 局统计）】回合开始后约 50 秒就会播
        /// 「轻收容区域的净化程序将在倒计时 15 分钟后进行」，也就是净化倒计时开始。
        /// 但出生点不在轻收容的时候那句字幕看不到/听不到，OCR 永远抓不到，
        /// 所以必须靠这个时间兜底（以前默认 600，太晚了）。
        ///
        /// 净化真正"封锁 / 开始清生物"实测在回合开始后约 710 秒 ≈ 50 + 705（基础时长）。
        /// </summary>
        public int DeconAfterRound = 50;

        /// <summary>回合开始后多久刷新支援（秒），用于估算。</summary>
        public int SupportAfterRound = 330;

        /// <summary>
        /// 分配角色时长（秒）。
        ///
        /// 客户端日志里 `SceneLoaded 2. Facility` 出现时，游戏其实是在分配角色：
        /// 玩家还要等几秒（实测常见 5~10 秒）才真正拿到角色、回合才算开始。
        /// 设成大于 0 的话，从这行日志开始先等这么久再开始正式计时
        /// （正式计时从 0 算）；期间只要日志里出现 RoundStartKeys 里的标志，
        /// 就立刻开始计时，不用等满。
        ///
        /// 0 = 老行为（进图就计时）。
        /// </summary>
        public int RoundStartDelay = 0;

        /// <summary>
        /// “回合正式开始”的关键字（逗号分隔）。命中任意一条就立刻开始计时。
        /// 各服提示不一样，所以做成可配置。
        /// </summary>
        public string RoundStartKeys = "回合开始,回合正式开始,准备结束,游戏正式开始,Round has been started";

        /// <summary>
        /// 认出"正式角色"时立刻开始计时（默认关）。
        ///
        /// 【为什么给这个开关】操作者发现：进图后玩家先是观察者，过几秒才变成
        /// 真正的游戏内角色 —— 那一刻有时正好就是回合真正开始。但各服节奏不同
        /// （他那台是"变角色"之后还要等大厅倒计时数完），所以默认不开，
        /// 由他按日志里的时间自己决定要不要开。
        /// </summary>
        public bool RoundStartOnRole = false;

        /// <summary>
        /// 【已停用，仅为兼容旧配置保留】旧版用它当亡者协议的"启动闸门"：
        /// 人类拿到令牌的概率高于等于这个百分比时先不起表。
        /// 2026-10-31 操作者口径纠正：2 分 30 秒计的是"最后那波小波 → 终焉"
        /// 的等待段，不能因为概率还高就推迟；拿到令牌会重置倒计时，本来就会处理。
        /// </summary>
        public int DeadManMinChancePercent = 60;

        /// <summary>
        /// 亡者协议（终焉）倒计时的总时长（秒）。默认 150 = 2 分 30 秒（文档口径）。
        ///
        /// 【为什么做成可配】各服不一样，而且操作者要求在「时间」页能改、
        /// 也能像核弹 / 净化那样按"还剩多少秒"校准一次（战术面板「校准」页）。
        /// </summary>
        public int DeadManSeconds = 150;

        /// <summary>
        /// 小波刷新之后还要过多少秒才起「终焉协议」倒计时（秒）。
        ///
        /// 【操作者口径】"刷小波后一段时间才有终焉" —— 这段时间要能校准，
        /// 和别的时间校准一样（F7 面板里填一次，或「时间」页里填）。
        ///
        /// 0 = 老行为（小波一记下来、两道闸一过就起表）。
        /// 默认先留 0：等操作者打完一把、拿到日志后，再按实测间隔改这个默认值。
        /// </summary>
        public int DeadManAfterMiniWaveSeconds = 0;

        /// <summary>
        /// 严格模式开关：前台要求静音连续 2 次未识别、且票数扫描未识别，窗口重绘作辅助；
        /// 后台 OCR 不可靠时，改用经过过滤的游戏窗口重绘判定。
        /// 关闭时沿用旧 OCR 规则：静音连续 2 次未识别或死亡界面连续 3 轮全空。
        ///
        /// `[General] RespawnRequireTicketsGone` 可改。
        /// </summary>
        public bool RespawnRequireTicketsGone = false;

        /// <summary>
        /// 本程序自己的窗口（提示窗 / 悬浮面板 / OCR 捕获提示）要不要
        /// 对截图和录屏不可见（Windows 的 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`）。
        ///
        ///   · 1（默认）：好处是不会被自己的 OCR 截进去（否则会出现
        ///     "把自己的提示字认成游戏播报"的自我循环）；
        ///     代价是 Windows 会弹「受保护的应用程序阻止桌面录制」，
        ///     而且 OBS 之类的录屏软件录不到那几块窗口。
        ///   · 0：录屏正常、没有那个系统提示；但提示窗压在识别区域上时
        ///     可能被自己认进去。
        ///
        /// 操作者要求"能关掉"，所以做成了「常规 → 屏幕 OCR 播报识别」里的一个勾
        ///（`[General] HideFromCapture`）。
        /// </summary>
        public bool HideFromCapture = true;

        /// <summary>
        /// 「三台发电机已就绪 —— 去 079 收容室按按钮」那条弹出来之后停多少秒。
        ///
        /// 【为什么要单独一个数】以前它用的是这条提示自己的 `DurationSeconds`（默认 120），
        /// 于是操作者看到它"一直黏在屏幕上"。现在默认 12 秒。
        /// `[General] GenAllReadySeconds` 可改。
        /// </summary>
        public int GenAllReadySeconds = 12;

        // ============================================================
        // 屏幕 OCR 播报识别（config.ini 里的 [Ocr] 段）
        //
        // 这块默认开着，但"开着"不等于"一直在跑"：只有屏幕左下角那一小块
        // 出现足够多的高亮（白 / 浅蓝）像素时才会真的调用 OCR 引擎，
        // 其余时间它都在睡觉。不想要的话在设置窗口里关掉，或者把
        // EnableOcr 改成 0。
        // ============================================================

        /// <summary>屏幕播报识别总开关。设成 0 时连 OCR 引擎都不会初始化。</summary>
        public bool EnableOcr = true;

        /// <summary>
        /// 只在进入服务器之后才开始扫描（默认 1 = 开）。
        ///
        /// 【操作者要求】OCR 要等**进入服务器**之后才开始运作：
        /// 桌面 / 主菜单 / 还没进服时完全不截图、不调用引擎；进服后自动开始，
        /// 离开服务器立刻停下。这样菜单文字不会再被认成游戏播报，也不白占 CPU。
        ///
        /// 中途加入（程序是在对局里才打开的）说明：那种情况下日志里"进服"那行
        /// 已经写过去了，`_inServer` 还是 false —— 要等下一次进服 / 进图 / 回合开始
        /// 的日志才会开始扫。想要"中途加入也马上扫"就把这个开关关掉。
        /// </summary>
        public bool OcrOnlyInServer = true;

        /// <summary>轮询间隔（毫秒）。默认 1500；调小会明显增加 CPU 占用。</summary>
        public int OcrPollInterval = 1500;

        /// <summary>识别语言，对应 tessdata 目录里的文件名。</summary>
        public string OcrLang = "eng+chi_sim";

        /// <summary>识别区域左上角 X（默认 0，也就是屏幕最左边）。</summary>
        public int OcrX = 0;

        /// <summary>识别区域下边距：实际 Y = 屏幕高度 - 这个值（默认 400）。</summary>
        public int OcrYOffset = 400;

        /// <summary>识别区域宽度（默认 1000，够宽才吃得下突发多行播报）。</summary>
        public int OcrWidth = 1000;

        /// <summary>识别区域高度（默认 400）。</summary>
        public int OcrHeight = 400;

        /// <summary>OCR 监控区域列表。第 1 组默认是左下角播报（定时轮询），
        /// 后面几组是自己加的按需区域（血量 / 发电机 / 通用）。</summary>
        public List<OcrRegion> OcrRegions = new List<OcrRegion>();

        // ============================================================
        // 第 4 批：阵营识别（替换屏幕中心的角色 OCR）
        // 只读取左下角血条最左 10% 的颜色，不截图整屏、不调 OCR 引擎。
        // ============================================================

        /// <summary>阵营识别总开关。关掉后"认出角色"只走日志里的旧信号。</summary>
        public bool FactionEnabled = true;

        /// <summary>一次识别最多采样多少秒（需求：默认 3 秒）。</summary>
        public int FactionMaxSampleSeconds = 3;

        /// <summary>连续多少帧颜色一致才出结果（需求：10 帧）。</summary>
        public int FactionConfirmFrames = 10;

        /// <summary>复活后延迟多少秒再开始认（需求：上限默认 3 秒）。</summary>
        public int FactionRespawnDelaySeconds = 3;

        /// <summary>加权欧氏距离的容差（0..1，越大越宽松）。超过就判"未知阵营"。</summary>
        public double FactionRgbTolerance = 0.55;

        /// <summary>阵营颜色映射表。默认值来自 阵营截图\ 的实测，可在 config.ini 里改。</summary>
        public List<FactionEntry> FactionMap = FactionTable.Defaults();

        // ============================================================
        // 第 6 批：截屏采集
        // 一次 21 张、先 7 张基础、按亮度分位挑 7 张展示、下次整体覆盖。
        // ============================================================

        /// <summary>基础张数（到这张时提示"基础的图片已采集完成"）。默认 7。</summary>
        public int CaptureMinCount = 7;

        /// <summary>上限张数（文件夹最多这么多张）。默认 21。</summary>
        public int CaptureMaxCount = 21;

        /// <summary>采集目录（相对程序目录或绝对路径）。默认「采集」。</summary>
        public string CaptureDir = "采集";

        /// <summary>
        /// 亮度参考图的“整体替换”目录。空 = 自动找游戏截图目录；
        /// 找不到时回落为软件动态生成的 7 张校色卡。旧配置没有此键时保持空。
        /// </summary>
        public string CaptureReferenceDir = "";

        /// <summary>
        /// 逐张替换的 7 个绝对路径，分号分隔；空位表示该档继续用自动/校色卡。
        /// 这是比“整体替换”更深一层的入口，只在用户主动改过时才有值。
        /// </summary>
        public string CaptureReferenceFiles = "";

        /// <summary>上一次采集到的总张数。</summary>
        public int CaptureCount = 0;

        /// <summary>上一次挑中的 7 张（按亮度升序后的序号，1 基，逗号分隔）。</summary>
        public string CaptureSelected = "";

        /// <summary>上一次采集完成的时间。</summary>
        public string CaptureSetAt = "";

        /// <summary>进度提示位置：0..1，基准 = 目标显示器。</summary>
        public double CaptureHudXPercent = double.NaN;
        public double CaptureHudYPercent = double.NaN;
        public string CaptureHudMonitor = "";

        /// <summary>开始 / 暂停采集的全局快捷键（空 = 不注册）。</summary>
        public string HotkeyCaptureStart = "";
        public string HotkeyCapturePause = "";

        /// <summary>程序自己的运行日志（SCPSLAlert.log），默认开。</summary>
        public bool EnableLog = true;

        /// <summary>
        /// 游戏崩溃监控：记录退出码、抓取日志与系统转储。
        /// 第 5 批起**新配置默认开**；旧配置里明确写着 0 的保持关（不覆盖用户选择）。
        /// </summary>
        public bool MonitorGameCrash = true;

        /// <summary>确认游戏崩溃或没有正常退出时弹窗说明；Alt+F4 / 控制台 quit 不弹，默认开。</summary>
        public bool CrashPopup = true;

        /// <summary>从软件启动游戏前先扫描可疑进程，默认开。</summary>
        public bool ScanBeforeLaunch = true;

        /// <summary>
        /// 第 5 批：游戏启动（进程出现）时，静默跑一次同一套可疑进程扫描。
        /// 复用 GameWatcher.Started 和 LaunchGuard，不新增轮询；同一启动只跑一次。
        /// </summary>
        public bool ScanOnGameStart = true;

        /// <summary>“立即扫描所有按需区域”的快捷键（F5 已经被回合计时暂停占用了）。</summary>
        public string HotkeyScanNow = "F2";

        /// <summary>停止当前所有计时并收起提示（有事件时会自动恢复）。</summary>
        public string HotkeyStopAll = "F4";

        /// <summary>立刻停掉正在播放的自备音频（操作者要求：快捷键停音频）。</summary>
        public string HotkeyStopAudio = "Ctrl+Alt+F12";

        // ============================================================
        // 第 5 批：快捷键"后台可用"名单
        //
        // 需求：每个快捷键一个开关。
        //   · 关（默认）= 只在游戏前台生效，并且把按键吞掉（防泄漏）；
        //   · 开 = 游戏在后台也生效，但不吞键，游戏照样收得到。
        // 存成一个逗号分隔的动作名列表，不新增 17 个配置键。
        // 动作名见 HotkeyForm.ActionName：edit / toggleAll / settings / ...
        // ============================================================

        /// <summary>后台也生效的快捷键动作名，逗号分隔。</summary>
        public string HotkeyBackground = "";

        /// <summary>这个动作的快捷键是否"后台可用"。</summary>
        public bool IsHotkeyBackground(string action)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(HotkeyBackground)) return false;
            string[] parts = HotkeyBackground.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), action, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>改一个动作的"后台可用"并写回列表。</summary>
        public void SetHotkeyBackground(string action, bool on)
        {
            if (string.IsNullOrEmpty(action)) return;
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(HotkeyBackground))
            {
                string[] parts = HotkeyBackground.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = parts[i].Trim();
                    if (s.Length == 0) continue;
                    if (string.Equals(s, action, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(s);
                }
            }
            if (on) list.Add(action);
            HotkeyBackground = string.Join(",", list.ToArray());
        }

        /// <summary>
        /// 点设置窗口右上角 × 时怎么办：
        ///   0 = 还没选过 → 弹窗问一次，并把选择记下来
        ///   1 = 最小化到托盘（程序继续在后台跑，只是窗口收起来）
        ///   2 = 直接退出程序
        ///
        /// 托盘图标上的右键菜单里随时可以改（"关闭按钮的行为"那一项）。
        /// </summary>
        public int CloseAction = 0;

        /// <summary>屏幕识别认到文字时，在屏幕左下角显示一小块"认到了什么"的提示。</summary>
        public bool OcrToast = true;

        /// <summary>
        /// 识别强度：0 = 保守，1 = 标准，2 = 激进（默认）。
        /// 越激进越容易从"被场景压暗 / 半透明叠出来"的播报文字里挑出字，
        /// 代价是可能多认进一些背景噪点。见 OcrAlertWatcher.StrengthParams。
        /// </summary>
        public int OcrStrength = 2;

        /// <summary>
        /// 语言模型精度：false = 快速版（tessdata\，约 2.5MB，默认），
        /// true = 最高精度版（tessdata_best\，约 13MB，更准但慢一些）。
        /// </summary>
        public bool OcrBestModel = false;

        /// <summary>
        /// 第一次没认好时，用原始灰度（反色 + 放大）再认一次。
        /// 默认关：识别强度开到「最大」时会自动启用（也可以在这里强制打开）。
        /// </summary>
        public bool OcrRetryRaw = false;

        /// <summary>灰度重试时的放大倍数（1~3，默认 2）。</summary>
        public int OcrScale = 2;

        /// <summary>
        /// 战术面板里那些输入框的内容（键 → 文本）。
        /// 存成字典比给每个 SCP 单开一个配置项干净，键名由面板自己定。
        /// </summary>
        private readonly Dictionary<string, string> _tactical =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string GetTactical(string key)
        {
            string v;
            return _tactical.TryGetValue(key, out v) ? v : "";
        }

        public void SetTactical(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) _tactical.Remove(key);
            else _tactical[key] = value;
        }

        /// <summary>
        /// 屏幕上那 4 个票数（死亡时 OCR 读到的）拼成一行摘要；一个都没读到就返回空串。
        ///
        /// 【为什么要放在 config 上】票数只存在这里（键 tok_MTF / inf_MTF / tok_混沌 /
        /// inf_混沌），战术面板和悬浮提示都要显示它。放在数据本身上，
        /// 两边就都能随时自己拼出来 —— 面板不必等别人推给它，
        /// 关掉再打开、切到别的页再切回来都不会丢。
        /// </summary>
        public string TicketSummaryText()
        {
            string mtfTok = GetTactical("tok_MTF");
            string mtfInf = GetTactical("inf_MTF");
            string chTok = GetTactical("tok_混沌");
            string chInf = GetTactical("inf_混沌");

            if (string.IsNullOrEmpty(mtfTok) && string.IsNullOrEmpty(mtfInf)
                && string.IsNullOrEmpty(chTok) && string.IsNullOrEmpty(chInf)) return "";

            string s = "重生令牌：MTF " + (string.IsNullOrEmpty(mtfTok) ? "?" : mtfTok);
            if (!string.IsNullOrEmpty(mtfInf)) s += "（" + mtfInf + "）";
            s += "　混沌 " + (string.IsNullOrEmpty(chTok) ? "?" : chTok);
            if (!string.IsNullOrEmpty(chInf)) s += "（" + chInf + "）";
            return s;
        }

        public AlertConfig RoundStart;
        public AlertConfig RoundTimer;
        public AlertConfig ServerJoin;
        public AlertConfig GameLaunch;
        public AlertConfig Warhead;
        public AlertConfig Decontamination;
        public AlertConfig ScpDeath;
        public AlertConfig SupportEstimate;

        /// <summary>SCP-079 发电机进度提醒。</summary>
        public AlertConfig GeneratorAlert;

        /// <summary>人类整体"还能不能再重生一波"的概率提示（操作者要求）。</summary>
        public AlertConfig RespawnAlert;

        /// <summary>发电机被激活（播报确认）时弹的那一条。</summary>
        public AlertConfig GenActiveAlert;

        /// <summary>亡者协议倒计时（两方令牌都用尽之后；活着也显示）。</summary>
        public AlertConfig DeadManAlert;

        /// <summary>按票数触发的"重生令牌"预警（需求二）。</summary>
        public AlertConfig TokenAlert;

        /// <summary>屏幕中心认出角色时的提示（需求六）。</summary>
        public AlertConfig RoleAlert;

        /// <summary>死亡界面识别到的票数 / 影响力提示（需求八）。</summary>
        public AlertConfig TicketAlert;

        /// <summary>识别到刷新增援播报时的提示（需求八附）。</summary>
        public AlertConfig WaveAlert;

        /// <summary>认不出击杀者时的被动提示（"SCP-XXX 已被击杀（击杀者未知）"）。</summary>
        public AlertConfig ScpKilled;

        /// <summary>启动时钉在屏幕顶端的那句"SCPSLAlert 已启动"。</summary>
        public AlertConfig StartupAlert;

        /// <summary>自己阵亡（日志里直接能读到死因）。</summary>
        public AlertConfig LocalDeathAlert;

        /// <summary>
        /// 自己重生提示；判定方式由 RespawnRequireTicketsGone 控制。
        /// </summary>
        public AlertConfig SelfRespawnAlert;

        /// <summary>启动演出：把所有提示弹一遍 → 故障退场 → 顶端显示"已启动"。默认开。</summary>
        public bool StartupShow = false;   // 默认关（操作者要求：启动时不要弹一遍所有提示）
        /// <summary>游戏退出时是否连带退出本程序。默认关：游戏退只弹提示，程序留在托盘。</summary>
        public bool ExitWithGame = false;
        public int MainUiStyle = 0;
        /// <summary>显示与画面总开关：关掉后伽马和准星一起停，但各自的原开关值保留。</summary>
        public bool DisplayAssistEnabled = true;
        /// <summary>识别总开关：关掉后 OCR 轮询、F2 手动扫描、阵营识别一起停。</summary>
        public bool RecognitionEnabled = true;
        /// <summary>强调色（#RRGGBB）。深色主题用 AccentHex，浅色主题用 LightAccentHex。</summary>
        public string AccentHex = ThemeTokens.DefaultAccent;
        public string LightAccentHex = "#006B84";
        // 深色色板（12 个）
        public string PageBgHex = "#070B10";
        public string PanelHex = "#0C1219";
        public string PanelAltHex = "#101A24";
        public string LineHex = "#1B2B36";
        public string TextHex = "#E6F4F8";
        public string SubTextHex = "#9AB0BC";
        public string MutedHex = "#5C7480";
        public string WarnHex = "#FFB03A";
        public string DangerHex = "#FF5C5C";
        public string SuccessHex = "#5BE38C";
        public string InfoHex = "#2F7BFF";
        // 浅色色板（12 个）
        public string LightPageBgHex = "#EBF1F7";
        public string LightPanelHex = "#FFFFFF";
        public string LightPanelAltHex = "#F2F6FA";
        public string LightLineHex = "#CCD8E3";
        public string LightTextHex = "#0A1720";
        public string LightSubTextHex = "#4A5D6C";
        public string LightMutedHex = "#8496A4";
        public string LightWarnHex = "#B26A00";
        public string LightDangerHex = "#C83232";
        public string LightSuccessHex = "#1F7A4D";
        public string LightInfoHex = "#0B63CE";
        public string UiFontFile = "";
        public string UiFontName = "";
        public string UiTitleFontFile = "";
        public string UiTitleFontName = "";
        public string UiMonoFontFile = "";
        public string UiMonoFontName = "";
        public int UiScale = 100;
        public int UiDensity = 1;
        public bool DynamicAccent = false;
        public int CardPreset = 0;
        public string CardTitle = "A.R.C.T";
        public string CardSubtitle = "SCPSL ALERT";
        public string CardLine1 = "";
        public string CardLine2 = "";
        // 名片扩展数据：让编辑器能配出 6 种预设各自需要的内容。
        public string CardTag = "ONLINE / 在线";
        public string CardPlayer = "Operator_7";
        public string CardPlayerId = "ID 00000000000000000";
        public string CardStat1Label = "总场次";
        public string CardStat1Value = "1,284";
        public string CardStat2Label = "存活率";
        public string CardStat2Value = "63%";
        public string CardStat3Label = "击杀";
        public string CardStat3Value = "412";
        public string CardAccentHex = "";
        public int CardTextScale = 100;
        public bool CardShowGrid = true;
        public bool CardShowHex = true;
        public bool CardShowFrame = true;
        public bool CardShowStats = true;
        public bool CardShowPlayer = true;
        public bool CardShowLive = true;
        public string PromptFontFile = "";
        public string PromptFontFamily = "";
        public int FontTitleSize = 32;
        public int FontBodySize = 14;
        public int FontDescSize = 12;
        public string UiFontFamily = "Microsoft YaHei UI";
        public string BackgroundImage = "";
        public int BackgroundOpacity = 12;
        public bool GlassWindow = false;
        public int GlassStrength = 55;
        public bool GlassCards = true;
        /// <summary>玻璃色调：0 = 按背景明暗自动，1 = 偏黑，2 = 偏白。</summary>
        public int GlassTintMode = 0;
        /// <summary>磨砂程度：0 = 通透，100 = 强毛玻璃。</summary>
        public int GlassFrost = 55;
        /// <summary>玻璃透明度：0 = 不透明，100 = 最透。</summary>
        public int GlassOpacity = 25;
        /// <summary>是否绘制玻璃光源（高光 / 亮边）。</summary>
        public bool GlassLightEnabled = true;
        /// <summary>光源亮度：0–100。</summary>
        public int GlassLightBrightness = 70;
        /// <summary>光源来源：0 左上，1 右上，2 上方，3 左侧，4 右侧。</summary>
        public int GlassLightSource = 0;
        public int BackgroundFill = 0;
        public int BackgroundDarken = 35;
        public string BackgroundMusic = "";
        public bool BackgroundMusicEnabled = true;
        public bool BackgroundMusicLoop = true;
        public int BackgroundMusicVolume = 45;
        public string ThemePack = "";
        public int ThemeSchema = 1;
        /// <summary>
        /// 仅本次进程有效：首次启动强制旧界面，但不写进 MainUiStyle。
        /// 这样“首次没切换就退出 → 下次进新界面”成立。
        /// </summary>
        [NonSerialized]
        public bool ForceLegacyThisRun;
        public int UiThemeMode = -1;

        /// <summary>本次实际生效的主界面：0 = 新界面，1 = 旧界面。</summary>
        public int UiStyle
        {
            get { return ForceLegacyThisRun ? 1 : (MainUiStyle == 0 ? 0 : 1); }
        }
        /// <summary>
        /// 屏幕提示的主题：-1 = 跟随主界面（新界面用新版 / 旧界面用旧版），
        /// 0 = 固定旧版经典提示，1 = 固定新版 A.R.C.T 提示。只影响提示，不影响主界面。
        /// </summary>
        public int PromptTheme = -1;

        /// <summary>实际生效的提示主题：0 = 经典，1 = A.R.C.T。跟随主界面时由 UiStyle 决定。</summary>
        public int EffectivePromptTheme
        {
            get
            {
                if (PromptTheme == 0) return 0;
                if (PromptTheme == 1) return 1;
                return UiStyle == 0 ? 1 : 0;
            }
        }

        /// <summary>识别总开关 + OCR 分开关都开时，OCR 才真正工作。</summary>
        public bool OcrAllowed
        {
            get { return RecognitionEnabled && EnableOcr; }
        }
        public bool UiAnimationsEnabled = true;
        public int UiAnimationDurationMs = 240;
        public int TacticalOpacity = 100;
        public int TacticalOpenStyle = 0;
        public string TacticalWidgets = "hero,actions,scp,pending,summary";

        // ---- 心率 / BLE ----
        public bool HeartRateBleEnabled = false;
        public string HeartRateBleAddress = "";
        public string HeartRateBleName = "";
        public bool HeartRateAlarmEnabled = true;
        public int HeartRateAlarmVolume = 25;
        public int HeartRateEcgSpeed = 55;
        public int HeartRateEcgAmplitude = 100;

        // ---- 心率屏幕提示（和游戏内提示分开：它只关心 BLE 心率）----
        public bool HeartPromptEnabled = false;
        /// <summary>0 = 完整卡片，1 = 无框，2 = 只显示数字。</summary>
        public int HeartPromptStyle = 0;
        public bool HeartPromptFrame = true;
        public bool HeartPromptLabel = true;
        public bool HeartPromptUnit = true;
        public bool HeartPromptPulse = true;
        public bool HeartPromptWave = true;
        public int HeartPromptWavePoints = 2;
        public int HeartPromptWaveAmp = 100;
        public int HeartPromptWaveWidth = 8;
        public int HeartPromptFontSize = 44;
        public int HeartPromptOpacity = 92;
        /// <summary>物理像素坐标；-1 = 自动放在主屏右上角。</summary>
        public int HeartPromptX = -1;
        public int HeartPromptY = -1;

        // 心率提示的归一化坐标（0..1，基准 = 目标显示器）。-1 的旧值仍表示“自动右上角”。
        public double HeartPromptXPercent = double.NaN;
        public double HeartPromptYPercent = double.NaN;
        public string HeartPromptMonitor = "";
        /// <summary>主色，格式 R,G,B。</summary>
        public string HeartPromptColor = "255,84,90";

        /// <summary>
        /// 启动演出里"所有提示保持正常状态"的时长（毫秒），然后才开始故障退场。
        /// 默认 1000（1 秒）。
        /// </summary>
        public int StartupHoldMs = 3000;

        public AlertConfig[] Alerts;

        public Config()
        {
            RoundStart = New("RoundStart", "回合开始", 30, Color.FromArgb(120, 230, 140), AlertIcon.Play);
            RoundStart.SampleText = "1";
            RoundStart.Format = "回合开始 · 第 {0} 回合";

            RoundTimer = New("RoundTimer", "回合时间", 86, Color.FromArgb(0, 255, 128), AlertIcon.Clock);
            RoundTimer.SampleText = "12:34";
            RoundTimer.Format = "本回合已进行 {0}";
            RoundTimer.Persistent = true;      // 默认常驻，像游戏里的顶部计时
            RoundTimer.PinTopCenter = true;    // 自动钉在屏幕正上方居中
            RoundTimer.Style.BackOpacity = 45; // 半透明小字
            RoundTimer.Style.Glass = false;
            RoundTimer.Style.FontSize = 15f;

            ServerJoin = New("ServerJoin", "进入服务器", 142, Color.FromArgb(130, 200, 255), AlertIcon.Arrow);
            ServerJoin.SampleText = "玩家名";
            ServerJoin.Format = "进入服务器：{0}";

            GameLaunch = New("GameLaunch", "游戏启动提示", 198, Color.FromArgb(200, 170, 255), AlertIcon.Screen);
            GameLaunch.Format = "SCP:SL 已启动";
            GameLaunch.AltText = "SCP:SL 已退出";

            Warhead = New("Warhead", "核弹倒计时", 254, Color.FromArgb(255, 90, 90), AlertIcon.Warning);
            Warhead.Style.FontSize = 26f;
            Warhead.SampleText = "01:30";
            Warhead.HasCountdown = true;
            // 核弹在客户端日志里根本看不到，只能靠 F8 / Shift+F8 / Ctrl+F8 手动起，
            // 所以它必须走“估算驱动”，否则按了快捷键什么都不显示。
            Warhead.Estimated = true;
            Warhead.Format = "核弹倒计时 {0}";
            Warhead.DurationSeconds = 90;
            Warhead.EndText = "核弹已引爆";
            Warhead.PulseAt = new int[] { 60, 30 };
            Warhead.PulseSeconds = 3;
            Warhead.HotBelow = 30;

            Decontamination = New("Decontamination", "净化倒计时", 316, Color.FromArgb(80, 200, 255), AlertIcon.Ring);
            Decontamination.SampleText = "11:45";
            Decontamination.HasCountdown = true;
            Decontamination.Estimated = true;
            Decontamination.Format = "净化倒计时 {0}";
            Decontamination.DurationSeconds = 600;
            Decontamination.EndText = "轻收容净化已开始";
            Decontamination.PulseAt = new int[] { 300, 60, 30 };
            Decontamination.PulseSeconds = 4;
            Decontamination.HotBelow = 60;

            ScpDeath = New("ScpDeath", "SCP 阵亡提示", 378, Color.FromArgb(255, 220, 80), AlertIcon.Diamond);
            ScpDeath.Style.FontSize = 18f;
            ScpDeath.SampleText = "SCP-173";
            ScpDeath.SampleText2 = "玩家名";
            ScpDeath.SampleText3 = "死因示例";
            // 击杀者放前面，读起来才是"谁杀了谁"（需求一）。
            // 【操作者要求】不要那句"支援可能即将刷新"了 —— SCP 死了就播报 SCP 死了，
            // 刷新的判断交给下面那条"重生概率"提示。
            ScpDeath.Format = "{1} 击杀了 {0}";

            SupportEstimate = New("SupportEstimate", "支援刷新", 434, Color.FromArgb(255, 160, 60), AlertIcon.Clock);
            SupportEstimate.SampleText = "01:00";
            SupportEstimate.HasCountdown = true;
            SupportEstimate.Estimated = true;
            SupportEstimate.Format = "支援刷新 {0}";
            SupportEstimate.DurationSeconds = 330;
            SupportEstimate.EndText = "支援已刷新";
            SupportEstimate.PulseAt = new int[] { 60, 30 };
            SupportEstimate.PulseSeconds = 4;
            SupportEstimate.HotBelow = 60;
            // 长周期倒计时：不一直挂在屏幕上，每隔一段时间露一小段，临近刷新才持续显示
            SupportEstimate.FlashPerMinute = true;
            SupportEstimate.FlashSeconds = 10;
            SupportEstimate.FlashPeriodSeconds = 30;   // 操作者要求：没勾常驻就每 30 秒报一次
            SupportEstimate.SoonSeconds = 30;
            SupportEstimate.Persistent = false;
            // 注：这条默认保持开着。以前我按"嫌吵"把它默认关掉了，那是过度解读 ——
            // 操作者只要求去掉「支援可能即将刷新」那句文案（见 ScpDeath），
            // 并没有要关掉这条倒计时提示。

            // ---- 重生概率提示（操作者要求）----
            // 人类整体"还能不能重生"的概率到阈值就弹一条（默认 60%），
            // 比"支援可能即将刷新"有用得多：它直接告诉你还有几波。
            RespawnAlert = New("RespawnAlert", "重生概率", 434, Color.FromArgb(255, 150, 150), AlertIcon.Warning);
            RespawnAlert.SampleText = "人类还有 72% 的机会再重生一波";
            RespawnAlert.Format = "{0}";
            RespawnAlert.DurationSeconds = 8;
            RespawnAlert.FlashPerMinute = false;

            GeneratorAlert = New("GeneratorAlert", "079 发电机", 490, Color.FromArgb(120, 220, 255), AlertIcon.Diamond);
            GeneratorAlert.SampleText = "发电机 2";
            GeneratorAlert.SampleText2 = "剩余 15 秒";
            GeneratorAlert.HasCountdown = true;
            GeneratorAlert.Format = "{0}";
            GeneratorAlert.DurationSeconds = 120;
            GeneratorAlert.EndText = "三台发电机已就绪";
            // 【操作者报：一直黏在屏幕上】三台就绪那条只弹一小段（以前用的是 DurationSeconds=120 秒）
            GeneratorAlert.EndText = "三台发电机已就绪";
            GeneratorAlert.HotBelow = TacticalLog.GenSoonSeconds;
            GeneratorAlert.PulseAt = new int[] { 60, 30, 20 };
            GeneratorAlert.PulseSeconds = 5;
            // 这个是随时可能发生的事件，不需要“每分钟露一段”
            GeneratorAlert.FlashPerMinute = false;

            // ---- 发电机被激活（播报确认）----
            // 操作者要求：模型认出"某一台被激活"之后，要像倒计时那样弹一条，
            // 让玩家当场知道（不然只有打开战术面板才看得到）。
            GenActiveAlert = New("GenActiveAlert", "发电机已激活", 850, Color.FromArgb(150, 235, 170), AlertIcon.Diamond);
            GenActiveAlert.SampleText = "发电机 2 已被激活（2/3）";
            GenActiveAlert.SampleText2 = "MTF +3 影响力";
            GenActiveAlert.Format = "{0}";
            GenActiveAlert.DurationSeconds = 8;
            // 一次性事件，不常驻、也不用"每分钟露一段"
            GenActiveAlert.FlashPerMinute = false;

            // ---- 亡者协议倒计时（两方令牌都用尽 → 2 分 30 秒后核弹）----
            // 操作者要求：放屏幕底端，活着也显示（这时候全人类都在等这一下）。
            DeadManAlert = New("DeadManAlert", "亡者协议", 990, Color.FromArgb(255, 120, 120), AlertIcon.Warning);
            DeadManAlert.Style.X = 620;
            DeadManAlert.Style.Y = 990;
            DeadManAlert.SampleText = "亡者协议 T-2:30";
            DeadManAlert.Format = "{0}";
            DeadManAlert.FlashPerMinute = false;

            // ---- 重生令牌阈值预警（需求二）----
            TokenAlert = New("TokenAlert", "重生令牌", TokenAlertPositionY, Color.FromArgb(255, 235, 130), AlertIcon.Diamond);
            // 操作者要求：默认丢在屏幕下方（不再放屏幕中上部的 y=550）
            TokenAlert.Style.X = TokenAlertPositionX;
            TokenAlert.SampleText = "累积 30 票";
            TokenAlert.SampleText2 = "人类即将获得第一个重生令牌";
            TokenAlert.Format = "{0}";
            TokenAlert.DurationSeconds = 8;
            TokenAlert.FlashPerMinute = false;

            // ---- 角色分配提示（需求六）----
            RoleAlert = New("RoleAlert", "角色分配", 610, Color.FromArgb(170, 230, 255), AlertIcon.Arrow);
            RoleAlert.SampleText = "你的角色：D 级人员";
            RoleAlert.Format = "你的角色：{0}";
            RoleAlert.DurationSeconds = 12;
            RoleAlert.FlashPerMinute = false;
            // 【不再常驻】实测踩到过：认出角色后那句「你的角色：SCP」一直挂在屏幕上
            // 不消失（观战界面误判时尤其碍眼）。现在到点自动缩回，
            // 想要常驻的话在「提示外观」里给这条打开 Persistent 就行。
            RoleAlert.Persistent = false;

            // ---- 死亡界面票数/影响力（需求八）----
            TicketAlert = New("TicketAlert", "票数 / 影响力", 670, Color.FromArgb(255, 210, 120), AlertIcon.Diamond);
            // 操作者要求：放在屏幕底端（死亡界面时一眼能看到，而不用盯着右上角）。
            // 1920×1080 下取底部居中偏左一点；拖过一次就不会再被这里的默认值覆盖。
            TicketAlert.Style.X = 620;
            TicketAlert.Style.Y = 980;
            TicketAlert.SampleText = "MTF 令牌 1（49/80）";
            TicketAlert.SampleText2 = "混沌 令牌 0（12/80）";
            TicketAlert.Format = "{0}";
            // 死亡界面期间靠"每轮重新确认"保持显示、复活立刻收起
            // （见 AlertManager.EndDeathScan）。所以这里不能用 Persistent ——
            // 现在活着也会隔几秒扫一次票数，Persistent 会让那一条一直赖在屏幕底下。
            TicketAlert.DurationSeconds = 10;
            TicketAlert.Persistent = false;
            TicketAlert.FlashPerMinute = false;

            // ---- 识别到"某某进入设施"的提示（需求八附）----
            WaveAlert = New("WaveAlert", "刷新增援", 730, Color.FromArgb(190, 255, 170), AlertIcon.Arrow);
            WaveAlert.SampleText = "识别到 MTF 进入设施（大波）";
            WaveAlert.Format = "{0}";
            WaveAlert.DurationSeconds = 8;
            WaveAlert.FlashPerMinute = false;

            // ---- 认不出击杀者时的被动提示（"SCP-XXX 已被击杀"）----
            ScpKilled = New("ScpKilled", "SCP 被击杀", 790, Color.FromArgb(255, 170, 170), AlertIcon.Warning);
            ScpKilled.SampleText = "SCP-096 已被击杀";
            ScpKilled.SampleText2 = "击杀者未知";
            ScpKilled.Format = "{0} 已被击杀（击杀者未知）";
            ScpKilled.DurationSeconds = 10;
            ScpKilled.FlashPerMinute = false;

            // ---- 自己阵亡 / 自己重生（操作者要求：这两件事也要能单独配音频）----
            // 死亡由客户端日志判定；复活规则由 RespawnRequireTicketsGone 控制：
            // 宽松模式用静音 / 死亡界面全空 OCR，严格模式以前台静音与票数 OCR 为主、
            // 游戏窗口重绘为辅助，后台用经过过滤的窗口重绘。
            LocalDeathAlert = New("LocalDeathAlert", "自己阵亡", 240,
                Color.FromArgb(255, 130, 130), AlertIcon.Warning);
            LocalDeathAlert.Style.X = 620;
            LocalDeathAlert.Style.Y = 300;
            LocalDeathAlert.SampleText = "你已阵亡";
            LocalDeathAlert.SampleText2 = "被 SCP-173 击杀";
            LocalDeathAlert.Format = "你已阵亡：{0}";
            LocalDeathAlert.DurationSeconds = 10;
            LocalDeathAlert.FlashPerMinute = false;

            SelfRespawnAlert = New("SelfRespawnAlert", "自己重生", 300,
                Color.FromArgb(140, 235, 160), AlertIcon.Arrow);
            SelfRespawnAlert.Style.X = 620;
            SelfRespawnAlert.Style.Y = 300;
            SelfRespawnAlert.SampleText = "你已重生";
            SelfRespawnAlert.SampleText2 = "右下角静音开关消失了";
            // 【不要括号里的依据】操作者看到"你已重生（右下角静音开关消失了）"觉得罗嗦，
            // 所以提示上只留"你已重生"；判定依据照样写进日志（[提示] 判定已经重生（依据：…））。
            SelfRespawnAlert.Format = "你已重生";
            SelfRespawnAlert.DurationSeconds = 10;
            SelfRespawnAlert.FlashPerMinute = false;

            // ---- 启动提示（钉在屏幕顶端"SCPSLAlert 已启动"）----
            StartupAlert = New("StartupAlert", "启动提示", 0, Color.FromArgb(150, 235, 255), AlertIcon.Play);
            StartupAlert.SampleText = "SCPSLAlert 已启动";
            StartupAlert.Format = "{0}";
            StartupAlert.DurationSeconds = 4;
            StartupAlert.PinTopCenter = true;
            StartupAlert.FlashPerMinute = false;

            Alerts = new AlertConfig[]
            {
                RoundStart, RoundTimer, ServerJoin, GameLaunch,
                Warhead, Decontamination, ScpDeath, SupportEstimate,
                GeneratorAlert, GenActiveAlert, TokenAlert, RoleAlert, TicketAlert, WaveAlert, ScpKilled,
                DeadManAlert, RespawnAlert,
                LocalDeathAlert, SelfRespawnAlert,
                StartupAlert
            };

            // 新配置也先放一组默认的播报区域，界面和 OCR 都有东西可用
            EnsureOcrRegions();
            // 全新配置的默认位置是按 1920×1080 设计值写的；先按设计基准折算，
            // 4K/2K 首次启动也能落在相对位置，而不是固定 980px。
            EnsureScreenCoordinates(true);
            EnsureOcrCoordinates(true);
        }

        /// <summary>
        /// 保证区域列表里至少有一组。
        /// 老配置（还没有 [OcrRegions] 段）会在这里用 [Ocr] 里那四个老字段
        /// 补出“左下角播报”，所以升级上来什么都不用改。
        /// </summary>
        public void EnsureOcrRegions()
        {
            if (OcrRegions == null) OcrRegions = new List<OcrRegion>();
            if (OcrRegions.Count > 0) return;   // 已有配置：一个都不动（老用户用右键菜单自己加角色框）

            OcrRegion r = OcrRegion.NewBroadcast();
            r.X = OcrX;
            r.Y = OcrYOffset;
            r.FromBottom = true;
            r.Width = OcrWidth;
            r.Height = OcrHeight;
            OcrRegions.Add(r);

            // 第 4 批：屏幕中心角色框已删除，全新配置不再创建它
            // （阵营识别不需要任何 OCR 区域）。

            // 以及死亡界面那 4 个票数/影响力框（需求八，坐标按 1920×1080 实测值）
            EnsureTicketRegions();
        }

        /// <summary>
        /// 补上死亡界面那 4 个票数/影响力框（需求八）。
        /// 只在"一个都没有"时加，所以用户删掉之后不会自己长回来；
        /// 想加回来用区域列表右键菜单里的「补 4 个票数框」。
        /// </summary>
        public void EnsureTicketRegions()
        {
            if (OcrRegions == null) OcrRegions = new List<OcrRegion>();

            int have = 0;
            for (int i = 0; i < OcrRegions.Count; i++)
                if (OcrRegions[i] != null && OcrRegions[i].Kind == OcrRegionKind.Ticket) have++;
            if (have > 0) return;

            // 坐标按用户提供的 1920×1080 截图量的：框略大于数字本身，
            // 多出来的边由票数识别那套颜色掩膜自己裁掉。
            OcrRegions.Add(OcrRegion.NewTicket("MTF 令牌", "mtf_tokens", 428, 0, 190, 84));
            OcrRegions.Add(OcrRegion.NewTicket("MTF 影响力", "mtf_influence", 576, 0, 190, 70));
            OcrRegions.Add(OcrRegion.NewTicket("混沌令牌", "chaos_tokens", 1411, 14, 100, 56));
            OcrRegions.Add(OcrRegion.NewTicket("混沌影响力", "chaos_influence", 1259, 14, 100, 50));
        }

        /// <summary>
        /// 一次性把"后来新加的几组框"补进老配置（用户希望默认配置里就有）。
        ///
        /// 用 [General] OcrBoxesV 记版本：
        ///   · 老配置（没这个键）→ 补一次，然后写上版本号；
        ///   · 之后用户自己删掉某个框，不会每次启动又被加回来。
        /// 补的是：屏幕中心角色框、死亡界面票数框、右下角静音开关、左侧 SCP 列表。
        /// </summary>
        public void MigrateOcrBoxesOnce()
        {
            if (OcrRegions == null) OcrRegions = new List<OcrRegion>();
            if (OcrBoxesV >= 1) return;

            int before = OcrRegions.Count;

            // 第 4 批：把老配置里已经存在的屏幕中心角色框删掉（这个功能整体下线了）。
            // 只删这一种，其它区域一个不动。
            for (int i = OcrRegions.Count - 1; i >= 0; i--)
            {
                if (OcrRegions[i] != null && OcrRegions[i].Kind == OcrRegionKind.Role)
                    OcrRegions.RemoveAt(i);
            }

            bool hasSilence = false, hasList = false, hasTicket = false;
            for (int i = 0; i < OcrRegions.Count; i++)
            {
                OcrRegion r = OcrRegions[i];
                if (r == null) continue;
                if (r.Kind == OcrRegionKind.Silence) hasSilence = true;
                else if (r.Kind == OcrRegionKind.ScpList) hasList = true;
                else if (r.Kind == OcrRegionKind.Ticket) hasTicket = true;
            }

            if (!hasTicket) EnsureTicketRegions();
            if (!hasSilence) OcrRegions.Add(OcrRegion.NewSilence());
            if (!hasList) OcrRegions.Add(OcrRegion.NewScpList());

            OcrBoxesV = 1;
            if (OcrRegions.Count != before)
                Logger.On("OCR", "已补上票数 / 静音开关 / 左侧 SCP 列表识别框，"
                    + "并删除已下线的屏幕中心角色框；位置不对就右键重新框选");
        }

        /// <summary>
        /// 补上"SCP 血量表"识别框（画面中偏右那块 HP:当前/上限）。
        ///
        /// 单独一个版本号：老配置（OcrBoxesV 已经是 1）也要能补上这块，
        /// 所以不能挂在 MigrateOcrBoxesOnce 里 —— 那个一旦跑过就不会再跑。
        /// 同样是"只补一次"：用户删掉之后不会每次启动又长回来。
        /// </summary>
        public void EnsureScpHpRegionOnce()
        {
            if (OcrRegions == null) OcrRegions = new List<OcrRegion>();
            if (ScpHpRegionV >= 1) return;

            bool have = false;
            for (int i = 0; i < OcrRegions.Count; i++)
                if (OcrRegions[i] != null && OcrRegions[i].Kind == OcrRegionKind.ScpHp) have = true;

            ScpHpRegionV = 1;
            if (have) return;

            OcrRegions.Add(OcrRegion.NewScpHp());
            Logger.On("OCR", "已补上「SCP 血量表」识别框（画面中偏右那块 HP:当前/上限）——"
                + "它用来读 SCP 血量、自动算掉血影响力；不同服务器位置不同，"
                + "到 设置 → OCR 区域列表 里选中它重新框选即可");
        }

        /// <summary>「SCP 血量表」识别框的补丁版本（只补一次）。</summary>
        public int ScpHpRegionV = 0;

        /// <summary>OCR 识别框的配置版本（用于"只补一次"的迁移）。</summary>
        public int OcrBoxesV = 0;

        /// <summary>
        /// 计时相关配置的迁移版本。
        /// 1 = 「分配角色时长」新语义 + 启动演出 3 秒/7 秒。
        /// </summary>
        public int TimingV = 0;

        /// <summary>
        /// 计时相关默认值迁移的最新版本号（每加一批就 +1）。
        /// 1：准备阶段时长 → 分配角色时长 / 启动演出时长；
        /// 2：净化兜底时间 600 → 50 秒（操作者日志实测：回合开始后约 50 秒净化倒计时开始）。
        /// </summary>
        public const int CurrentTimingV = 2;

        /// <summary>
        /// 提示默认值的迁移版本。
        /// 1 = 角色提示不再常驻 + 票数提示挪到屏幕底端。
        /// </summary>
        public int AlertDefaultsV = 0;

        /// <summary>
        /// 提示默认值迁移的最新版本号（每加一批迁移就 +1）。
        /// 测试直接引用它，这样以后加迁移不用再去改测试里的魔法数字。
        /// 1：角色不再常驻 / 票数挪到底端；2：票数不再常驻；3：启动演出默认关 + 令牌挪下方；
        /// 4：「自己重生」不带括号和判定依据。
        /// </summary>
        public const int CurrentAlertDefaultsV = 4;

        private static AlertConfig New(string section, string name, int y, Color color, AlertIcon icon)
        {
            AlertConfig a = new AlertConfig();
            a.Section = section;
            a.DisplayName = name;
            a.Style.X = 30;
            a.Style.Y = y;
            a.Style.TextColor = color;
            a.Style.Icon = icon;
            return a;
        }

        public AlertConfig BySection(string section)
        {
            for (int i = 0; i < Alerts.Length; i++)
                if (string.Equals(Alerts[i].Section, section, StringComparison.OrdinalIgnoreCase))
                    return Alerts[i];
            return null;
        }

        /// <summary>把另一份配置的值全部拷进来（保持本对象引用不变）。</summary>
        public void CopyFrom(Config src)
        {
            LogDirectory = src.LogDirectory;
            SoundEnabled = src.SoundEnabled;
            PollIntervalMs = src.PollIntervalMs;
            GamePollMs = src.GamePollMs;
            DisplaySeconds = src.DisplaySeconds;
            GameBrightnessEnabled = src.GameBrightnessEnabled;
            GameBrightnessIntervalMs = src.GameBrightnessIntervalMs;
            GameBrightnessCaptureMotionOnly = src.GameBrightnessCaptureMotionOnly;
            GameBrightnessHoldEnabled = src.GameBrightnessHoldEnabled;
            GameBrightnessHoldGamma = src.GameBrightnessHoldGamma;
            GameBrightnessHoldSaturation = src.GameBrightnessHoldSaturation;
            GameBrightnessForceGlobal = src.GameBrightnessForceGlobal;
            GameBrightnessProfile = src.GameBrightnessProfile;
            GameBrightnessSaturationProfile = src.GameBrightnessSaturationProfile;
            GameBrightnessPresets = src.GameBrightnessPresets;
            GameBrightnessCompensationBoost = src.GameBrightnessCompensationBoost;
            GameBrightnessPresetCurrent = src.GameBrightnessPresetCurrent;
            CrosshairEnabled = src.CrosshairEnabled; CrosshairShape = src.CrosshairShape;
            CrosshairColor = src.CrosshairColor; CrosshairOutlineColor = src.CrosshairOutlineColor;
            CrosshairSize = src.CrosshairSize; CrosshairThickness = src.CrosshairThickness;
            CrosshairGap = src.CrosshairGap; CrosshairOpacity = src.CrosshairOpacity;
            CrosshairOutline = src.CrosshairOutline; CrosshairDot = src.CrosshairDot;
            CrosshairOffsetX = src.CrosshairOffsetX; CrosshairOffsetY = src.CrosshairOffsetY;
            CrosshairRingKey = src.CrosshairRingKey; CrosshairRingDurationMs = src.CrosshairRingDurationMs;
            CrosshairRingRadius = src.CrosshairRingRadius; CrosshairExpandKey = src.CrosshairExpandKey;
            CrosshairExpandDurationMs = src.CrosshairExpandDurationMs; CrosshairExpandAmount = src.CrosshairExpandAmount;
            CrosshairType = src.CrosshairType; CrosshairLength = src.CrosshairLength;
            CrosshairLineGap = src.CrosshairLineGap; CrosshairLineThickness = src.CrosshairLineThickness;
            CrosshairDotSize = src.CrosshairDotSize; CrosshairRotation = src.CrosshairRotation;
            CrosshairRingRadiusF = src.CrosshairRingRadiusF; CrosshairRingEase = src.CrosshairRingEase; CrosshairTShape = src.CrosshairTShape;
            CrosshairOuterEnabled = src.CrosshairOuterEnabled; CrosshairOuterLength = src.CrosshairOuterLength;
            CrosshairOuterThickness = src.CrosshairOuterThickness; CrosshairOuterGap = src.CrosshairOuterGap;
            CrosshairOutlineOpacity = src.CrosshairOutlineOpacity; CrosshairOutlineWidth = src.CrosshairOutlineWidth;
            CrosshairDynamic = src.CrosshairDynamic; CrosshairFireSpread = src.CrosshairFireSpread;
            CrosshairFireAmount = src.CrosshairFireAmount; CrosshairFireDurationMs = src.CrosshairFireDurationMs;
            CrosshairFireCurve = src.CrosshairFireCurve; CrosshairFireMaxMul = src.CrosshairFireMaxMul;
            CrosshairHitFlash = src.CrosshairHitFlash; CrosshairDpiScale = src.CrosshairDpiScale;
            CrosshairSnapCenter = src.CrosshairSnapCenter; CrosshairRenderFps = src.CrosshairRenderFps;
            PanelAnimQuality = src.PanelAnimQuality;
            OffsetX = src.OffsetX; OffsetY = src.OffsetY;
            AlertAvoidEnabled = src.AlertAvoidEnabled;
            AlertAvoidGapPx = src.AlertAvoidGapPx;
            AlertAvoidMarginPx = src.AlertAvoidMarginPx;
            AlertAvoidAnimMs = src.AlertAvoidAnimMs;
            ExportDir = src.ExportDir;
            ExportWatermark = src.ExportWatermark;
            ExportShowPlayer = src.ExportShowPlayer;
            ExportSplitByMatch = src.ExportSplitByMatch;
            ExportDefaultTab = src.ExportDefaultTab;
            ExportTxtReadout = src.ExportTxtReadout;
            ExportPartMaxPx = src.ExportPartMaxPx;
            OneMapShowHeart = src.OneMapShowHeart;
            OneMapLttb = src.OneMapLttb;
            OneMapLabels = src.OneMapLabels;
            OneMapBlockGap = src.OneMapBlockGap;
            MatchKeep = src.MatchKeep;
            OneMapFpsSmooth = src.OneMapFpsSmooth;
            OneMapExtraDurations = src.OneMapExtraDurations;
            MatchRecordHeart = src.MatchRecordHeart;
            ClickFxEnabled = src.ClickFxEnabled;
            ClickFxGap = src.ClickFxGap;
            ScpScanJoinSeconds = src.ScpScanJoinSeconds;
            HotkeyReleaseV1 = src.HotkeyReleaseV1;
            CoordV = src.CoordV;
            StatusSeries = src.StatusSeries;
            HotkeyAdjust = src.HotkeyAdjust;
            HotkeyToggleAll = src.HotkeyToggleAll;
            HotkeySettings = src.HotkeySettings;
            HotkeyPanel = src.HotkeyPanel;
            HotkeyHoldShowAll = src.HotkeyHoldShowAll;
            HotkeyCalibrate = src.HotkeyCalibrate;
            HotkeyWarhead = src.HotkeyWarhead;
            HotkeyWarheadShift = src.HotkeyWarheadShift;
            HotkeyWarheadCtrl = src.HotkeyWarheadCtrl;
            HotkeyDecon = src.HotkeyDecon;
            HotkeyTactical = src.HotkeyTactical;
            HotkeyScpDead = src.HotkeyScpDead;
            HotkeyBigWave = src.HotkeyBigWave;
            HotkeyClockPause = src.HotkeyClockPause;
            HotkeyScanNow = src.HotkeyScanNow;
            HotkeyStopAll = src.HotkeyStopAll;
            HotkeyStopAudio = src.HotkeyStopAudio;
            HotkeyBackground = src.HotkeyBackground;
            ScanOnGameStart = src.ScanOnGameStart;
            CloseAction = src.CloseAction;
            OcrToast = src.OcrToast;
            OcrStrength = src.OcrStrength;
            OcrBestModel = src.OcrBestModel;
            OcrRetryRaw = src.OcrRetryRaw;
            OcrScale = src.OcrScale;
            EnableLog = src.EnableLog;
            AutoCalibrateOnJoin = src.AutoCalibrateOnJoin;
            DeconAfterRound = src.DeconAfterRound;
            TokenThresholds = src.TokenThresholds;
            RoundStartOffsetSeconds = src.RoundStartOffsetSeconds;
            WaveOffsetSeconds = src.WaveOffsetSeconds;
            WaveIntervalSeconds = src.WaveIntervalSeconds;
            WavePerPlayerSeconds = src.WavePerPlayerSeconds;
            StartupShow = src.StartupShow;
            ExitWithGame = src.ExitWithGame;
            MainUiStyle = src.MainUiStyle;
            DisplayAssistEnabled = src.DisplayAssistEnabled;
            RecognitionEnabled = src.RecognitionEnabled;
            AccentHex = src.AccentHex;
            LightAccentHex = src.LightAccentHex;
            PageBgHex = src.PageBgHex;
            PanelHex = src.PanelHex;
            PanelAltHex = src.PanelAltHex;
            LineHex = src.LineHex;
            TextHex = src.TextHex;
            SubTextHex = src.SubTextHex;
            MutedHex = src.MutedHex;
            WarnHex = src.WarnHex;
            DangerHex = src.DangerHex;
            SuccessHex = src.SuccessHex;
            InfoHex = src.InfoHex;
            LightPageBgHex = src.LightPageBgHex;
            LightPanelHex = src.LightPanelHex;
            LightPanelAltHex = src.LightPanelAltHex;
            LightLineHex = src.LightLineHex;
            LightTextHex = src.LightTextHex;
            LightSubTextHex = src.LightSubTextHex;
            LightMutedHex = src.LightMutedHex;
            LightWarnHex = src.LightWarnHex;
            LightDangerHex = src.LightDangerHex;
            LightSuccessHex = src.LightSuccessHex;
            LightInfoHex = src.LightInfoHex;
            UiFontFile = src.UiFontFile;
            UiFontName = src.UiFontName;
            UiTitleFontFile = src.UiTitleFontFile;
            UiTitleFontName = src.UiTitleFontName;
            UiMonoFontFile = src.UiMonoFontFile;
            UiMonoFontName = src.UiMonoFontName;
            UiScale = src.UiScale;
            UiDensity = src.UiDensity;
            DynamicAccent = src.DynamicAccent;
            CardPreset = src.CardPreset;
            CardTitle = src.CardTitle;
            CardSubtitle = src.CardSubtitle;
            CardLine1 = src.CardLine1;
            CardLine2 = src.CardLine2;
            CardTag = src.CardTag;
            CardPlayer = src.CardPlayer;
            CardPlayerId = src.CardPlayerId;
            CardStat1Label = src.CardStat1Label;
            CardStat1Value = src.CardStat1Value;
            CardStat2Label = src.CardStat2Label;
            CardStat2Value = src.CardStat2Value;
            CardStat3Label = src.CardStat3Label;
            CardStat3Value = src.CardStat3Value;
            CardAccentHex = src.CardAccentHex;
            CardTextScale = src.CardTextScale;
            CardShowGrid = src.CardShowGrid;
            CardShowHex = src.CardShowHex;
            CardShowFrame = src.CardShowFrame;
            CardShowStats = src.CardShowStats;
            CardShowPlayer = src.CardShowPlayer;
            CardShowLive = src.CardShowLive;
            PromptFontFile = src.PromptFontFile;
            PromptFontFamily = src.PromptFontFamily;
            FontTitleSize = src.FontTitleSize;
            FontBodySize = src.FontBodySize;
            FontDescSize = src.FontDescSize;
            UiFontFamily = src.UiFontFamily;
            BackgroundImage = src.BackgroundImage;
            BackgroundOpacity = src.BackgroundOpacity;
            GlassWindow = src.GlassWindow;
            GlassStrength = src.GlassStrength;
            GlassCards = src.GlassCards;
            GlassTintMode = src.GlassTintMode;
            GlassFrost = src.GlassFrost;
            GlassOpacity = src.GlassOpacity;
            GlassLightEnabled = src.GlassLightEnabled;
            GlassLightBrightness = src.GlassLightBrightness;
            GlassLightSource = src.GlassLightSource;
            BackgroundFill = src.BackgroundFill;
            BackgroundDarken = src.BackgroundDarken;
            BackgroundMusic = src.BackgroundMusic;
            BackgroundMusicEnabled = src.BackgroundMusicEnabled;
            BackgroundMusicLoop = src.BackgroundMusicLoop;
            BackgroundMusicVolume = src.BackgroundMusicVolume;
            ThemePack = src.ThemePack;
            ThemeSchema = src.ThemeSchema;
            UiThemeMode = src.UiThemeMode;
            PromptTheme = src.PromptTheme;
            UiAnimationsEnabled = src.UiAnimationsEnabled;
            UiAnimationDurationMs = src.UiAnimationDurationMs;
            TacticalOpacity = src.TacticalOpacity;
            TacticalOpenStyle = src.TacticalOpenStyle;
            TacticalWidgets = src.TacticalWidgets;
            HeartRateBleEnabled = src.HeartRateBleEnabled;
            HeartRateBleAddress = src.HeartRateBleAddress;
            HeartRateBleName = src.HeartRateBleName;
            HeartRateAlarmEnabled = src.HeartRateAlarmEnabled;
            HeartRateAlarmVolume = src.HeartRateAlarmVolume;
            HeartRateEcgSpeed = src.HeartRateEcgSpeed;
            HeartRateEcgAmplitude = src.HeartRateEcgAmplitude;
            HeartPromptEnabled = src.HeartPromptEnabled;
            HeartPromptStyle = src.HeartPromptStyle;
            HeartPromptFrame = src.HeartPromptFrame;
            HeartPromptLabel = src.HeartPromptLabel;
            HeartPromptUnit = src.HeartPromptUnit;
            HeartPromptPulse = src.HeartPromptPulse;
            HeartPromptWave = src.HeartPromptWave;
            HeartPromptWavePoints = src.HeartPromptWavePoints;
            HeartPromptWaveAmp = src.HeartPromptWaveAmp;
            HeartPromptWaveWidth = src.HeartPromptWaveWidth;
            HeartPromptFontSize = src.HeartPromptFontSize;
            HeartPromptOpacity = src.HeartPromptOpacity;
            HeartPromptX = src.HeartPromptX;
            HeartPromptY = src.HeartPromptY;
            HeartPromptXPercent = src.HeartPromptXPercent;
            HeartPromptYPercent = src.HeartPromptYPercent;
            HeartPromptMonitor = src.HeartPromptMonitor;
            HeartPromptColor = src.HeartPromptColor;
            OcrBoxesV = src.OcrBoxesV;
            ScpHpRegionV = src.ScpHpRegionV;
            TimingV = src.TimingV;
            AlertDefaultsV = src.AlertDefaultsV;
            StartupHoldMs = src.StartupHoldMs;
            MiniWaveWindowSeconds = src.MiniWaveWindowSeconds;
            MiniWaveTickets = src.MiniWaveTickets;
            MiniWaveDelaySeconds = src.MiniWaveDelaySeconds;
            DeconBaseSeconds = src.DeconBaseSeconds;
            DeconAnnounceMap = src.DeconAnnounceMap;
            WarheadRestartSeconds = src.WarheadRestartSeconds;
            RoundStartOnRole = src.RoundStartOnRole;
            DeadManMinChancePercent = src.DeadManMinChancePercent;
            RespawnRequireTicketsGone = src.RespawnRequireTicketsGone;
            HideFromCapture = src.HideFromCapture;
            GenAllReadySeconds = src.GenAllReadySeconds;
            RoundStartKeys = src.RoundStartKeys;
            RoundStartOnRole = src.RoundStartOnRole;
            WarheadOcrAuthoritative = src.WarheadOcrAuthoritative;
            RespawnAlertPercent = src.RespawnAlertPercent;
            AudioDir = src.AudioDir;
            AudioDefault = src.AudioDefault;
            AudioSegments = src.AudioSegments;
            AudioVolumes = src.AudioVolumes;
            AudioTriggerSpec = src.AudioTriggerSpec;
            DeconFallback = src.DeconFallback;
            SupportAfterRound = src.SupportAfterRound;
            RoundStartDelay = src.RoundStartDelay;
            RoundStartKeys = src.RoundStartKeys;
            EnableOcr = src.EnableOcr;
            OcrOnlyInServer = src.OcrOnlyInServer;
            OcrPollInterval = src.OcrPollInterval;
            OcrLang = src.OcrLang;
            OcrX = src.OcrX;
            OcrYOffset = src.OcrYOffset;
            OcrWidth = src.OcrWidth;
            OcrHeight = src.OcrHeight;

            // 区域列表是引用类型，必须逐个深拷贝，否则两份 Config 会共用同一个列表
            OcrRegions.Clear();
            for (int i = 0; i < src.OcrRegions.Count; i++)
                OcrRegions.Add(src.OcrRegions[i].Clone());

            FactionEnabled = src.FactionEnabled;
            FactionMaxSampleSeconds = src.FactionMaxSampleSeconds;
            FactionConfirmFrames = src.FactionConfirmFrames;
            FactionRespawnDelaySeconds = src.FactionRespawnDelaySeconds;
            FactionRgbTolerance = src.FactionRgbTolerance;
            FactionMap = FactionTable.Parse(FactionTable.Serialize(src.FactionMap));

            CaptureMinCount = src.CaptureMinCount;
            CaptureMaxCount = src.CaptureMaxCount;
            CaptureDir = src.CaptureDir;
            CaptureReferenceDir = src.CaptureReferenceDir;
            CaptureReferenceFiles = src.CaptureReferenceFiles;
            CaptureCount = src.CaptureCount;
            CaptureSelected = src.CaptureSelected;
            CaptureSetAt = src.CaptureSetAt;
            CaptureHudXPercent = src.CaptureHudXPercent;
            CaptureHudYPercent = src.CaptureHudYPercent;
            CaptureHudMonitor = src.CaptureHudMonitor;
            HotkeyCaptureStart = src.HotkeyCaptureStart;
            HotkeyCapturePause = src.HotkeyCapturePause;

            for (int i = 0; i < Alerts.Length && i < src.Alerts.Length; i++)
            {
                AlertConfig a = Alerts[i];
                AlertConfig b = src.Alerts[i];
                a.Persistent = b.Persistent;
                a.Estimated = b.Estimated;
                a.PinTopCenter = b.PinTopCenter;
                a.FlashPerMinute = b.FlashPerMinute;
                a.FlashSeconds = b.FlashSeconds;
                a.FlashPeriodSeconds = b.FlashPeriodSeconds;
                a.SoonSeconds = b.SoonSeconds;
            a.SoundOn = b.SoundOn;
            a.SoundKind = b.SoundKind;
            a.SoundCooldown = b.SoundCooldown;
            a.AudioFile = b.AudioFile;
            a.AudioPoints = b.AudioPoints;
            a.CondType = b.CondType;
            a.CondValue = b.CondValue;
                a.Format = b.Format;
                a.DurationSeconds = b.DurationSeconds;
                a.EndText = b.EndText;
                a.AltText = b.AltText;
                a.PulseAt = b.PulseAt;
                a.PulseSeconds = b.PulseSeconds;
                a.HotBelow = b.HotBelow;
                a.Style.CopyFrom(b.Style);
            }
        }

        #region 读

        /// <summary>
        /// 把旧版本 config.ini 里几个“改过就不好用”的值迁移到新语义。
        /// 只在旧值恰好等于老默认值时才动，用户自己调过的不会被覆盖。
        /// </summary>
        private static void MigrateOldDefaults(IniFile ini)
        {
            // 净化改成"播报触发"之后（需求五），基础时长回到实测的 705 秒 = 11 分 45 秒。
            // 上一版把它迁移成 600 是为了配合"回合推算"，现在不再用回合推算了，
            // 所以把 600 改回 705（只动恰好等于 600 的，用户自己改过的保留）。
            string decon = ini.Get("Decontamination", "Duration", "");
            if (decon == "600")
            {
                ini.Set("Decontamination", "Duration", "705");
                ini.Set("Decontamination", "PulseAt", "5:00, 1:00, 0:30");
            }

            // 旧版支援估算写死 60 秒（其实只是“支援可能已刷新”的停留），
            // 新版把它当成“回合开始后多久刷新支援”，默认 330。
            string sup = ini.Get("SupportEstimate", "Duration", "");
            if (sup == "60")
            {
                ini.Set("SupportEstimate", "Duration", "330");
                ini.Set("SupportEstimate", "PulseAt", "1:00, 0:30");
            }

            // 悬浮面板改版后，这两个功能从“Ctrl+Alt+组合键”换成了单键 F7 / F8。
            // 只在旧值恰好还是老默认值时才改，用户自己改过的保留。
            if (ini.Get("General", "HotkeyCalibrate", "") == "Ctrl+Alt+F11")
                ini.Set("General", "HotkeyCalibrate", "F7");
            if (ini.Get("General", "HotkeyWarhead", "") == "Ctrl+Alt+F12")
                ini.Set("General", "HotkeyWarhead", "F8");

            // 核弹改成由快捷键手动驱动后必须走“估算”，
            // 旧配置里写的是 0（那时还没有快捷键），这里补上。
            if (ini.Get("General", "HotkeyClockPause", "") == "F12")
                ini.Set("General", "HotkeyClockPause", "F5");

            if (ini.Get("Warhead", "Estimated", "") == "0")
                ini.Set("Warhead", "Estimated", "1");

            // 击杀提示的文案顺序改成"击杀者在前"（需求一：谁杀了谁）。
            // 只当用户没动过、还是老默认值时才换，自己改过的保留。
            string scpFmt = ini.Get("ScpDeath", "Format", "");
            if (scpFmt == "{0} 被 [{1}] 击杀，支援可能即将刷新")
                ini.Set("ScpDeath", "Format", "{1} 击杀了 {0}，支援可能即将刷新");
        }

        /// <summary>
        /// 第 3 批坐标迁移：旧配置只有物理像素 X/Y，没有 0..1 坐标。
        /// 这里按“当前所在显示器”折算一次，之后保存会同时写 X/Y 和 XPercent/YPercent。
        /// 只折算，不改旧键；旧 WinForms 界面继续读 X/Y。
        /// </summary>
        public void EnsureScreenCoordinates(bool designBasis)
        {
            Rectangle design = new Rectangle(0, 0, 1920, 1080);
            string designMonitor = ScreenGeometry.MonitorDevice(new Point(0, 0));

            if (Alerts != null)
            {
                for (int i = 0; i < Alerts.Length; i++)
                {
                    WindowStyle s = Alerts[i] == null ? null : Alerts[i].Style;
                    if (s == null) continue;
                    if (!IsNormalized(s.XPercent, s.YPercent))
                    {
                        Point p = new Point(s.X, s.Y);
                        Rectangle mon = designBasis ? design : ScreenGeometry.MonitorBounds(p);
                        NormalizedPoint n = ScreenGeometry.Normalize(p, mon);
                        s.XPercent = n.X;
                        s.YPercent = n.Y;
                        s.LegacySyncedX = s.X;
                        s.LegacySyncedY = s.Y;
                        if (string.IsNullOrEmpty(s.Monitor))
                            s.Monitor = designBasis ? designMonitor : ScreenGeometry.MonitorDevice(p);
                    }
                    else if (string.IsNullOrEmpty(s.Monitor))
                    {
                        s.Monitor = designBasis ? designMonitor : ScreenGeometry.MonitorDevice(new Point(s.X, s.Y));
                    }
                }
            }

            if (HeartPromptX >= 0 && HeartPromptY >= 0
                && !IsNormalized(HeartPromptXPercent, HeartPromptYPercent))
            {
                Point p = new Point(HeartPromptX, HeartPromptY);
                Rectangle mon = designBasis ? design : ScreenGeometry.MonitorBounds(p);
                NormalizedPoint n = ScreenGeometry.Normalize(p, mon);
                HeartPromptXPercent = n.X;
                HeartPromptYPercent = n.Y;
                if (string.IsNullOrEmpty(HeartPromptMonitor))
                    HeartPromptMonitor = designBasis ? designMonitor : ScreenGeometry.MonitorDevice(p);
            }

            if (CoordV < 1) CoordV = 1;
        }

        /// <summary>保存前把归一化坐标回写成当前物理像素，旧界面/回滚仍能读到正确位置。</summary>
        public void SyncLegacyScreenCoordinates()
        {
            if (Alerts != null)
            {
                for (int i = 0; i < Alerts.Length; i++)
                {
                    WindowStyle s = Alerts[i] == null ? null : Alerts[i].Style;
                    if (s == null || !IsNormalized(s.XPercent, s.YPercent)) continue;

                    // 旧界面拖动只会改 X/Y；如果它和上次同步值不同，就以 X/Y 为准反推归一化。
                    bool legacyChanged = s.LegacySyncedX != int.MinValue
                        && (s.X != s.LegacySyncedX || s.Y != s.LegacySyncedY);
                    if (legacyChanged)
                    {
                        Point legacy = new Point(s.X, s.Y);
                        Rectangle legacyMon = ScreenGeometry.MonitorBounds(legacy);
                        NormalizedPoint legacyNorm = ScreenGeometry.Normalize(legacy, legacyMon);
                        s.XPercent = legacyNorm.X;
                        s.YPercent = legacyNorm.Y;
                        s.Monitor = ScreenGeometry.MonitorDevice(legacy);
                        s.LegacySyncedX = s.X;
                        s.LegacySyncedY = s.Y;
                        continue;
                    }

                    Rectangle mon = ScreenGeometry.ResolveMonitor(s.Monitor, new Point(s.X, s.Y));
                    Point p = ScreenGeometry.Denormalize(new NormalizedPoint(s.XPercent, s.YPercent), mon);
                    s.X = p.X;
                    s.Y = p.Y;
                    s.LegacySyncedX = p.X;
                    s.LegacySyncedY = p.Y;
                    if (string.IsNullOrEmpty(s.Monitor)) s.Monitor = ScreenGeometry.MonitorDevice(p);
                }
            }

            if (HeartPromptX >= 0 && HeartPromptY >= 0
                && IsNormalized(HeartPromptXPercent, HeartPromptYPercent))
            {
                Rectangle mon = ScreenGeometry.ResolveMonitor(HeartPromptMonitor, new Point(HeartPromptX, HeartPromptY));
                Point p = ScreenGeometry.Denormalize(
                    new NormalizedPoint(HeartPromptXPercent, HeartPromptYPercent), mon);
                HeartPromptX = p.X;
                HeartPromptY = p.Y;
            }
        }

        private static bool IsNormalized(double x, double y)
        {
            return !double.IsNaN(x) && !double.IsNaN(y)
                && x >= 0.0 && x <= 1.0 && y >= 0.0 && y <= 1.0;
        }

        /// <summary>心率提示直接按物理像素改位置（设置页输入框、拖动）时同步归一化坐标。</summary>
        public void SetHeartPromptPixelPosition(int x, int y)
        {
            HeartPromptX = x;
            HeartPromptY = y;
            if (x < 0 || y < 0)
            {
                HeartPromptXPercent = double.NaN;
                HeartPromptYPercent = double.NaN;
                HeartPromptMonitor = "";
                return;
            }
            Point p = new Point(x, y);
            Rectangle mon = ScreenGeometry.MonitorBounds(p);
            NormalizedPoint n = ScreenGeometry.Normalize(p, mon);
            HeartPromptXPercent = n.X;
            HeartPromptYPercent = n.Y;
            HeartPromptMonitor = ScreenGeometry.MonitorDevice(p);
        }

        /// <summary>OCR 区域：旧配置按游戏客户区（没启动时按主屏）折算一次 0..1。</summary>
        public void EnsureOcrCoordinates(bool designBasis)
        {
            if (OcrRegions == null) return;
            Rectangle basis = designBasis
                ? new Rectangle(0, 0, 1920, 1080)
                : ScreenGeometry.GameClientOrPrimary();
            for (int i = 0; i < OcrRegions.Count; i++)
                if (OcrRegions[i] != null) OcrRegions[i].EnsureNormalized(basis);
        }

        /// <summary>保存前：旧界面改过 X/Y 的区域重新折算，其余把归一化回写成物理像素。</summary>
        public void SyncOcrCoordinates()
        {
            if (OcrRegions == null) return;
            Rectangle basis = ScreenGeometry.GameClientOrPrimary();
            for (int i = 0; i < OcrRegions.Count; i++)
            {
                OcrRegion r = OcrRegions[i];
                if (r == null) continue;
                if (!r.HasNormalized) r.EnsureNormalized(basis);
                r.SyncLegacy(basis);
            }
        }

        public static Config Load(string path)
        {
            IniFile ini = IniFile.Load(path);
            MigrateOldDefaults(ini);
            Config c = new Config();

            c.LogDirectory = ini.Get("General", "LogDirectory", c.LogDirectory);
            c.SoundEnabled = ReadBool(ini, "General", "SoundEnabled", c.SoundEnabled);
            c.PollIntervalMs = ReadInt(ini, "General", "PollIntervalMs", c.PollIntervalMs);
            c.GamePollMs = ReadInt(ini, "General", "GamePollMs", c.GamePollMs);
            c.DisplaySeconds = ReadInt(ini, "General", "DisplaySeconds", c.DisplaySeconds);
            c.TimeFormat = ClampI(ReadInt(ini, "General", "TimeFormat", c.TimeFormat), 0, 3);
            c.GameBrightnessEnabled = ReadBool(ini, "GameBrightness", "Enabled", c.GameBrightnessEnabled);
            c.GameBrightnessIntervalMs = ReadInt(ini, "GameBrightness", "IntervalMs", c.GameBrightnessIntervalMs);
            c.GameBrightnessCaptureMotionOnly = ReadBool(ini, "GameBrightness", "CaptureMotionOnly", c.GameBrightnessCaptureMotionOnly);
            c.GameBrightnessHoldEnabled = ReadBool(ini, "GameBrightness", "HoldEnabled", c.GameBrightnessHoldEnabled);
            c.GameBrightnessHoldGamma = ReadInt(ini, "GameBrightness", "HoldGamma", c.GameBrightnessHoldGamma);
            c.GameBrightnessHoldSaturation = ReadInt(ini, "GameBrightness", "HoldSaturation", c.GameBrightnessHoldSaturation);
            c.GameBrightnessForceGlobal = ReadBool(ini, "GameBrightness", "ForceGlobal", c.GameBrightnessForceGlobal);
            c.GameBrightnessProfile = ini.Get("GameBrightness", "Profile", c.GameBrightnessProfile);
            c.GameBrightnessSaturationProfile = ini.Get("GameBrightness", "SaturationProfile", c.GameBrightnessSaturationProfile);
            c.GameBrightnessPresets = ini.Get("GameBrightness", "Presets", c.GameBrightnessPresets);
            c.GameBrightnessCompensationBoost = ClampD(ReadDouble(ini, "GameBrightness", "CompensationBoost", c.GameBrightnessCompensationBoost), 0, 0.50);
            c.GameBrightnessPresetCurrent = ini.Get("GameBrightness", "PresetCurrent", c.GameBrightnessPresetCurrent);
            c.CrosshairEnabled = ReadBool(ini, "Crosshair", "Enabled", c.CrosshairEnabled);
            c.CrosshairShape = ReadInt(ini, "Crosshair", "Shape", c.CrosshairShape);
            c.CrosshairColor = ReadInt(ini, "Crosshair", "Color", c.CrosshairColor);
            c.CrosshairOutlineColor = ReadInt(ini, "Crosshair", "OutlineColor", c.CrosshairOutlineColor);
            c.CrosshairSize = ReadInt(ini, "Crosshair", "Size", c.CrosshairSize);
            c.CrosshairThickness = ReadInt(ini, "Crosshair", "Thickness", c.CrosshairThickness);
            c.CrosshairGap = ReadInt(ini, "Crosshair", "Gap", c.CrosshairGap);
            c.CrosshairOpacity = ReadInt(ini, "Crosshair", "Opacity", c.CrosshairOpacity);
            c.CrosshairOutline = ReadBool(ini, "Crosshair", "Outline", c.CrosshairOutline);
            c.CrosshairDot = ReadBool(ini, "Crosshair", "Dot", c.CrosshairDot);
            c.CrosshairOffsetX = ReadInt(ini, "Crosshair", "OffsetX", c.CrosshairOffsetX);
            c.CrosshairOffsetY = ReadInt(ini, "Crosshair", "OffsetY", c.CrosshairOffsetY);
            c.CrosshairRingKey = ini.Get("Crosshair", "RingKey", c.CrosshairRingKey);
            c.CrosshairRingDurationMs = ReadInt(ini, "Crosshair", "RingDurationMs", c.CrosshairRingDurationMs);
            c.CrosshairRingRadius = ReadInt(ini, "Crosshair", "RingRadius", c.CrosshairRingRadius);
            c.CrosshairExpandKey = ini.Get("Crosshair", "ExpandKey", c.CrosshairExpandKey);
            c.CrosshairExpandDurationMs = ReadInt(ini, "Crosshair", "ExpandDurationMs", c.CrosshairExpandDurationMs);
            c.CrosshairExpandAmount = ReadInt(ini, "Crosshair", "ExpandAmount", c.CrosshairExpandAmount);

            // 新版工作台参数；老配置没有 Type 键时会自动从旧字段迁移
            bool hasNewCrosshair = ini.Get("Crosshair", "Type", "").Trim().Length > 0;
            c.CrosshairType = ReadInt(ini, "Crosshair", "Type", c.CrosshairType);
            c.CrosshairLength = ReadFloat(ini, "Crosshair", "Length", c.CrosshairLength);
            c.CrosshairLineGap = ReadFloat(ini, "Crosshair", "LineGap", c.CrosshairLineGap);
            c.CrosshairLineThickness = ReadFloat(ini, "Crosshair", "LineThickness", c.CrosshairLineThickness);
            c.CrosshairDotSize = ReadFloat(ini, "Crosshair", "DotSize", c.CrosshairDotSize);
            c.CrosshairRotation = ReadFloat(ini, "Crosshair", "Rotation", c.CrosshairRotation);
            c.CrosshairRingRadiusF = ReadFloat(ini, "Crosshair", "RingRadiusF", c.CrosshairRingRadiusF);
            c.CrosshairRingEase = ReadInt(ini, "Crosshair", "RingEase", c.CrosshairRingEase);
            c.CrosshairTShape = ReadBool(ini, "Crosshair", "TShape", c.CrosshairTShape);
            c.CrosshairOuterEnabled = ReadBool(ini, "Crosshair", "OuterEnabled", c.CrosshairOuterEnabled);
            c.CrosshairOuterLength = ReadFloat(ini, "Crosshair", "OuterLength", c.CrosshairOuterLength);
            c.CrosshairOuterThickness = ReadFloat(ini, "Crosshair", "OuterThickness", c.CrosshairOuterThickness);
            c.CrosshairOuterGap = ReadFloat(ini, "Crosshair", "OuterGap", c.CrosshairOuterGap);
            c.CrosshairOutlineOpacity = ReadInt(ini, "Crosshair", "OutlineOpacity", c.CrosshairOutlineOpacity);
            c.CrosshairOutlineWidth = ReadFloat(ini, "Crosshair", "OutlineWidth", c.CrosshairOutlineWidth);
            c.CrosshairDynamic = ReadBool(ini, "Crosshair", "Dynamic", c.CrosshairDynamic);
            c.CrosshairFireSpread = ReadBool(ini, "Crosshair", "FireSpread", c.CrosshairFireSpread);
            c.CrosshairFireAmount = ReadFloat(ini, "Crosshair", "FireAmount", c.CrosshairFireAmount);
            c.CrosshairFireDurationMs = ReadInt(ini, "Crosshair", "FireDurationMs", c.CrosshairFireDurationMs);
            c.CrosshairFireCurve = ReadInt(ini, "Crosshair", "FireCurve", c.CrosshairFireCurve);
            c.CrosshairFireMaxMul = ReadFloat(ini, "Crosshair", "FireMaxMul", c.CrosshairFireMaxMul);
            c.CrosshairHitFlash = ReadBool(ini, "Crosshair", "HitFlash", c.CrosshairHitFlash);
            c.CrosshairDpiScale = ReadBool(ini, "Crosshair", "DpiScale", c.CrosshairDpiScale);
            c.CrosshairSnapCenter = ReadBool(ini, "Crosshair", "SnapCenter", c.CrosshairSnapCenter);
            c.CrosshairRenderFps = ReadInt(ini, "Crosshair", "RenderFps", c.CrosshairRenderFps);
            if (!hasNewCrosshair) c.MigrateCrosshairFromLegacy();
            c.OffsetX = ReadInt(ini, "General", "OffsetX", c.OffsetX);
            c.OffsetY = ReadInt(ini, "General", "OffsetY", c.OffsetY);
            c.AlertAvoidEnabled = ReadBool(ini, "General", "AlertAvoidEnabled", c.AlertAvoidEnabled);
            c.AlertAvoidGapPx = ClampI(ReadInt(ini, "General", "AlertAvoidGapPx", c.AlertAvoidGapPx), 0, 32);
            c.AlertAvoidMarginPx = ClampI(ReadInt(ini, "General", "AlertAvoidMarginPx", c.AlertAvoidMarginPx), 0, 64);
            c.AlertAvoidAnimMs = ClampI(ReadInt(ini, "General", "AlertAvoidAnimMs", c.AlertAvoidAnimMs), 0, 500);
            c.ExportDir = ini.Get("Export", "Dir", c.ExportDir);
            c.ExportWatermark = ReadBool(ini, "Export", "Watermark", c.ExportWatermark);
            c.ExportShowPlayer = ReadBool(ini, "Export", "ShowPlayer", c.ExportShowPlayer);
            c.ExportSplitByMatch = ReadBool(ini, "Export", "SplitByMatch", c.ExportSplitByMatch);
            c.ExportDefaultTab = ClampI(ReadInt(ini, "Export", "DefaultTab", c.ExportDefaultTab), 0, 1);
            c.ExportTxtReadout = ReadBool(ini, "Export", "Txt", c.ExportTxtReadout);
            c.ExportPartMaxPx = ClampI(ReadInt(ini, "Export", "PartMaxPx", c.ExportPartMaxPx), 3000, 20000);
            c.OneMapShowHeart = ReadBool(ini, "OneMap", "Heart", c.OneMapShowHeart);
            c.OneMapLttb = ClampI(ReadInt(ini, "OneMap", "Lttb", c.OneMapLttb), 200, 4000);
            c.OneMapLabels = ClampI(ReadInt(ini, "OneMap", "Labels", c.OneMapLabels), 0, 20);
            c.OneMapBlockGap = ClampI(ReadInt(ini, "OneMap", "BlockGap", c.OneMapBlockGap), 0, 120);
            c.MatchKeep = ClampI(ReadInt(ini, "Match", "Keep", c.MatchKeep), 5, 200);
            c.OneMapFpsSmooth = ReadBool(ini, "OneMap", "FpsSmooth", c.OneMapFpsSmooth);
            c.OneMapExtraDurations = ReadBool(ini, "OneMap", "ExtraDurations", c.OneMapExtraDurations);
            c.MatchRecordHeart = ReadBool(ini, "Match", "Heart", c.MatchRecordHeart);
            c.ClickFxEnabled = ReadBool(ini, "General", "ClickFx", c.ClickFxEnabled);
            c.ClickFxGap = ClampI(ReadInt(ini, "General", "ClickFxGap", c.ClickFxGap), 0, 240);
            c.ScpScanJoinSeconds = ClampI(ReadInt(ini, "General", "ScpScanJoinSeconds", c.ScpScanJoinSeconds), 0, 600);
            c.HotkeyReleaseV1 = ini.Get("General", "HotkeyReleaseV1", "") == "1";

            c.CoordV = ReadInt(ini, "General", "CoordV", c.CoordV);
            c.StatusSeries = ini.Get("General", "StatusSeries", c.StatusSeries);
            c.HotkeyAdjust = ini.Get("General", "HotkeyAdjust", c.HotkeyAdjust);
            c.HotkeyToggleAll = ini.Get("General", "HotkeyToggleAll", c.HotkeyToggleAll);
            c.HotkeySettings = ini.Get("General", "HotkeySettings", c.HotkeySettings);
            c.HotkeyPanel = ini.Get("General", "HotkeyPanel", c.HotkeyPanel);
            c.HotkeyHoldShowAll = ini.Get("General", "HotkeyHoldShowAll", c.HotkeyHoldShowAll);
            c.HotkeyCalibrate = ini.Get("General", "HotkeyCalibrate", c.HotkeyCalibrate);
            c.HotkeyWarhead = ini.Get("General", "HotkeyWarhead", c.HotkeyWarhead);
            c.HotkeyWarheadShift = ini.Get("General", "HotkeyWarheadShift", c.HotkeyWarheadShift);
            c.HotkeyWarheadCtrl = ini.Get("General", "HotkeyWarheadCtrl", c.HotkeyWarheadCtrl);
            c.HotkeyDecon = ini.Get("General", "HotkeyDecon", c.HotkeyDecon);
            c.HotkeyTactical = ini.Get("General", "HotkeyTactical", c.HotkeyTactical);
            c.HotkeyScpDead = ini.Get("General", "HotkeyScpDead", c.HotkeyScpDead);
            c.HotkeyBigWave = ini.Get("General", "HotkeyBigWave", c.HotkeyBigWave);
            c.HotkeyClockPause = ini.Get("General", "HotkeyClockPause", c.HotkeyClockPause);
            c.HotkeyScanNow = ini.Get("General", "HotkeyScanNow", c.HotkeyScanNow);
            c.HotkeyStopAll = ini.Get("General", "HotkeyStopAll", c.HotkeyStopAll);
            c.HotkeyStopAudio = ini.Get("General", "HotkeyStopAudio", c.HotkeyStopAudio);
            c.HotkeyBackground = ini.Get("General", "HotkeyBackground", c.HotkeyBackground);
            c.ScanOnGameStart = ReadBool(ini, "General", "ScanOnGameStart", c.ScanOnGameStart);

            // 第 5 批：崩溃监控默认值迁移。
            // 键存在 → 严格按用户存的（旧配置里是 0 就保持 0）；
            // 键不存在（老配置 / 全新配置）→ 用新默认值 = 开。
            string crashKey = ini.Get("General", "MonitorGameCrash", "");
            if (crashKey.Length == 0)
            {
                c.MonitorGameCrash = true;
                Logger.On("配置", "崩溃监控没有旧配置键 → 按新默认值开启（旧配置写明 0 的不会被改）");
            }
            else
            {
                c.MonitorGameCrash = crashKey.Trim() == "1" || crashKey.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            c.CloseAction = ReadInt(ini, "General", "CloseAction", c.CloseAction);
            c.OcrToast = ReadBool(ini, "General", "OcrToast", c.OcrToast);
            c.PanelAnimQuality = ReadInt(ini, "General", "PanelAnimQuality", c.PanelAnimQuality);
            c.OcrStrength = ReadInt(ini, "Ocr", "Strength", c.OcrStrength);
            c.OcrBestModel = ReadBool(ini, "Ocr", "BestModel", c.OcrBestModel);
            c.OcrRetryRaw = ReadBool(ini, "Ocr", "RetryRaw", c.OcrRetryRaw);
            c.OcrScale = ReadInt(ini, "Ocr", "Scale", c.OcrScale);
            c.EnableLog = ReadBool(ini, "General", "EnableLog", c.EnableLog);
            // 崩溃监控的默认值已在上面按"键是否存在"处理，这里不再重复读。
            c.CrashPopup = ReadBool(ini, "General", "CrashPopup", c.CrashPopup);
            c.ScanBeforeLaunch = ReadBool(ini, "General", "ScanBeforeLaunch", c.ScanBeforeLaunch);
            c.AutoCalibrateOnJoin = ReadBool(ini, "General", "AutoCalibrateOnJoin", c.AutoCalibrateOnJoin);
            c.DeconAfterRound = ReadInt(ini, "General", "DeconAfterRound", c.DeconAfterRound);
            c.TokenThresholds = ini.Get("General", "TokenThresholds", c.TokenThresholds);
            c.RoundStartOffsetSeconds = ReadInt(ini, "General", "RoundStartOffsetSeconds", c.RoundStartOffsetSeconds);
            c.WaveOffsetSeconds = ReadInt(ini, "General", "WaveOffsetSeconds", c.WaveOffsetSeconds);
            c.WaveIntervalSeconds = ReadInt(ini, "General", "WaveIntervalSeconds", c.WaveIntervalSeconds);
            c.WavePerPlayerSeconds = ReadInt(ini, "General", "WavePerPlayerSeconds", c.WavePerPlayerSeconds);
            c.StartupShow = ReadBool(ini, "General", "StartupShow", c.StartupShow);
            c.ExitWithGame = ReadBool(ini, "General", "ExitWithGame", c.ExitWithGame);
            c.MainUiStyle = ReadInt(ini, "Appearance", "MainUiStyle", c.MainUiStyle);
            // -1 是旧版的“选择界面”状态；现在缺失/损坏一律默认新界面。
            if (c.MainUiStyle < 0) c.MainUiStyle = 0;
            c.DisplayAssistEnabled = ReadBool(ini, "Appearance", "DisplayAssistEnabled", c.DisplayAssistEnabled);
            c.RecognitionEnabled = ReadBool(ini, "Appearance", "RecognitionEnabled", c.RecognitionEnabled);
            c.AccentHex = ThemeTokens.NormalizeHex(
                ini.Get("Appearance", "AccentHex", c.AccentHex), ThemeTokens.DefaultAccent);
            string lightAccentRaw = ini.Get("Appearance", "LightAccentHex", "");
            c.LightAccentHex = string.IsNullOrWhiteSpace(lightAccentRaw)
                ? ThemeTokens.ToHex(ThemeTokens.DeriveLightAccent(
                    ThemeTokens.ParseHex(c.AccentHex, ThemeTokens.ParseHex(ThemeTokens.DefaultAccent, Color.Cyan))))
                : ThemeTokens.NormalizeHex(lightAccentRaw, c.LightAccentHex);
            c.PageBgHex = ReadColor(ini, "PageBgHex", c.PageBgHex);
            c.PanelHex = ReadColor(ini, "PanelHex", c.PanelHex);
            c.PanelAltHex = ReadColor(ini, "PanelAltHex", c.PanelAltHex);
            c.LineHex = ReadColor(ini, "LineHex", c.LineHex);
            c.TextHex = ReadColor(ini, "TextHex", c.TextHex);
            c.SubTextHex = ReadColor(ini, "SubTextHex", c.SubTextHex);
            c.MutedHex = ReadColor(ini, "MutedHex", c.MutedHex);
            c.WarnHex = ReadColor(ini, "WarnHex", c.WarnHex);
            c.DangerHex = ReadColor(ini, "DangerHex", c.DangerHex);
            c.SuccessHex = ReadColor(ini, "SuccessHex", c.SuccessHex);
            c.InfoHex = ReadColor(ini, "InfoHex", c.InfoHex);
            c.LightPageBgHex = ReadColor(ini, "LightPageBgHex", c.LightPageBgHex);
            c.LightPanelHex = ReadColor(ini, "LightPanelHex", c.LightPanelHex);
            c.LightPanelAltHex = ReadColor(ini, "LightPanelAltHex", c.LightPanelAltHex);
            c.LightLineHex = ReadColor(ini, "LightLineHex", c.LightLineHex);
            c.LightTextHex = ReadColor(ini, "LightTextHex", c.LightTextHex);
            c.LightSubTextHex = ReadColor(ini, "LightSubTextHex", c.LightSubTextHex);
            c.LightMutedHex = ReadColor(ini, "LightMutedHex", c.LightMutedHex);
            c.LightWarnHex = ReadColor(ini, "LightWarnHex", c.LightWarnHex);
            c.LightDangerHex = ReadColor(ini, "LightDangerHex", c.LightDangerHex);
            c.LightSuccessHex = ReadColor(ini, "LightSuccessHex", c.LightSuccessHex);
            c.LightInfoHex = ReadColor(ini, "LightInfoHex", c.LightInfoHex);
            c.UiFontFile = ini.Get("Appearance", "UiFontFile", c.UiFontFile) ?? "";
            c.UiFontName = ini.Get("Appearance", "UiFontName", c.UiFontName) ?? "";
            c.UiTitleFontFile = ini.Get("Appearance", "UiTitleFontFile", c.UiTitleFontFile) ?? "";
            c.UiTitleFontName = ini.Get("Appearance", "UiTitleFontName", c.UiTitleFontName) ?? "";
            c.UiMonoFontFile = ini.Get("Appearance", "UiMonoFontFile", c.UiMonoFontFile) ?? "";
            c.UiMonoFontName = ini.Get("Appearance", "UiMonoFontName", c.UiMonoFontName) ?? "";
            c.UiScale = ClampI(ReadInt(ini, "Appearance", "UiScale", c.UiScale), 85, 125);
            c.UiDensity = ClampI(ReadInt(ini, "Appearance", "UiDensity", c.UiDensity), 0, 1);
            c.DynamicAccent = ReadBool(ini, "Appearance", "DynamicAccent", c.DynamicAccent);
            c.CardPreset = ClampI(ReadInt(ini, "Appearance", "CardPreset", c.CardPreset), 0, 5);
            c.CardTitle = ini.Get("Appearance", "CardTitle", c.CardTitle) ?? "";
            c.CardSubtitle = ini.Get("Appearance", "CardSubtitle", c.CardSubtitle) ?? "";
            c.CardLine1 = ini.Get("Appearance", "CardLine1", c.CardLine1) ?? "";
            c.CardLine2 = ini.Get("Appearance", "CardLine2", c.CardLine2) ?? "";
            c.CardTag = ini.Get("Appearance", "CardTag", c.CardTag) ?? "";
            c.CardPlayer = ini.Get("Appearance", "CardPlayer", c.CardPlayer) ?? "";
            c.CardPlayerId = ini.Get("Appearance", "CardPlayerId", c.CardPlayerId) ?? "";
            c.CardStat1Label = ini.Get("Appearance", "CardStat1Label", c.CardStat1Label) ?? "";
            c.CardStat1Value = ini.Get("Appearance", "CardStat1Value", c.CardStat1Value) ?? "";
            c.CardStat2Label = ini.Get("Appearance", "CardStat2Label", c.CardStat2Label) ?? "";
            c.CardStat2Value = ini.Get("Appearance", "CardStat2Value", c.CardStat2Value) ?? "";
            c.CardStat3Label = ini.Get("Appearance", "CardStat3Label", c.CardStat3Label) ?? "";
            c.CardStat3Value = ini.Get("Appearance", "CardStat3Value", c.CardStat3Value) ?? "";
            c.CardAccentHex = ini.Get("Appearance", "CardAccentHex", c.CardAccentHex) ?? "";
            c.CardTextScale = ClampI(ReadInt(ini, "Appearance", "CardTextScale", c.CardTextScale), 75, 145);
            c.CardShowGrid = ReadBool(ini, "Appearance", "CardShowGrid", c.CardShowGrid);
            c.CardShowHex = ReadBool(ini, "Appearance", "CardShowHex", c.CardShowHex);
            c.CardShowFrame = ReadBool(ini, "Appearance", "CardShowFrame", c.CardShowFrame);
            c.CardShowStats = ReadBool(ini, "Appearance", "CardShowStats", c.CardShowStats);
            c.CardShowPlayer = ReadBool(ini, "Appearance", "CardShowPlayer", c.CardShowPlayer);
            c.CardShowLive = ReadBool(ini, "Appearance", "CardShowLive", c.CardShowLive);
            c.PromptFontFile = ini.Get("Appearance", "PromptFontFile", c.PromptFontFile) ?? "";
            c.PromptFontFamily = ini.Get("Appearance", "PromptFontFamily", c.PromptFontFamily) ?? "";
            c.FontTitleSize = Math.Max(8, Math.Min(96, ReadInt(ini, "Appearance", "FontTitleSize", c.FontTitleSize)));
            c.FontBodySize = Math.Max(8, Math.Min(96, ReadInt(ini, "Appearance", "FontBodySize", c.FontBodySize)));
            c.FontDescSize = Math.Max(8, Math.Min(96, ReadInt(ini, "Appearance", "FontDescSize", c.FontDescSize)));
            c.UiFontFamily = ini.Get("Appearance", "UiFontFamily", c.UiFontFamily);
            c.BackgroundImage = ini.Get("Appearance", "BackgroundImage", c.BackgroundImage);
            c.BackgroundOpacity = Math.Max(0, Math.Min(100, ReadInt(ini, "Appearance", "BackgroundOpacity", c.BackgroundOpacity)));
            c.GlassWindow = ReadBool(ini, "Appearance", "GlassWindow", c.GlassWindow);
            c.GlassStrength = ClampI(ReadInt(ini, "Appearance", "GlassStrength", c.GlassStrength), 0, 100);
            c.GlassCards = ReadBool(ini, "Appearance", "GlassCards", c.GlassCards);
            c.GlassTintMode = ClampI(ReadInt(ini, "Appearance", "GlassTintMode", c.GlassTintMode), 0, 2);
            c.GlassFrost = ClampI(ReadInt(ini, "Appearance", "GlassFrost", c.GlassFrost), 0, 100);
            c.GlassOpacity = ClampI(ReadInt(ini, "Appearance", "GlassOpacity", c.GlassOpacity), 0, 100);
            c.GlassLightEnabled = ReadBool(ini, "Appearance", "GlassLightEnabled", c.GlassLightEnabled);
            c.GlassLightBrightness = ClampI(ReadInt(ini, "Appearance", "GlassLightBrightness", c.GlassLightBrightness), 0, 100);
            c.GlassLightSource = ClampI(ReadInt(ini, "Appearance", "GlassLightSource", c.GlassLightSource), 0, 4);
            c.BackgroundFill = ClampI(ReadInt(ini, "Appearance", "BackgroundFill", c.BackgroundFill), 0, 2);
            c.BackgroundDarken = ClampI(ReadInt(ini, "Appearance", "BackgroundDarken", c.BackgroundDarken), 0, 100);
            c.BackgroundMusic = ini.Get("Appearance", "BackgroundMusic", c.BackgroundMusic) ?? "";
            c.BackgroundMusicEnabled = ReadBool(ini, "Appearance", "BackgroundMusicEnabled", c.BackgroundMusicEnabled);
            c.BackgroundMusicLoop = ReadBool(ini, "Appearance", "BackgroundMusicLoop", c.BackgroundMusicLoop);
            c.BackgroundMusicVolume = ClampI(ReadInt(ini, "Appearance", "BackgroundMusicVolume", c.BackgroundMusicVolume), 0, 100);
            c.ThemePack = ini.Get("Appearance", "ThemePack", c.ThemePack);
            c.ThemeSchema = ReadInt(ini, "Appearance", "ThemeSchema", c.ThemeSchema);
            c.UiThemeMode = ReadInt(ini, "Appearance", "ThemeMode", c.UiThemeMode);
            c.PromptTheme = ReadInt(ini, "Appearance", "PromptTheme", c.PromptTheme);
            if (c.PromptTheme < -1) c.PromptTheme = -1;
            if (c.PromptTheme > 1) c.PromptTheme = 1;
            c.UiAnimationsEnabled = ReadBool(ini, "Appearance", "AnimationsEnabled", c.UiAnimationsEnabled);
            c.UiAnimationDurationMs = ReadInt(ini, "Appearance", "AnimationDurationMs", c.UiAnimationDurationMs);
            c.TacticalOpacity = ReadInt(ini, "Appearance", "TacticalOpacity", c.TacticalOpacity);
            c.TacticalOpenStyle = ReadInt(ini, "Appearance", "TacticalOpenStyle", c.TacticalOpenStyle);
            c.TacticalWidgets = ini.Get("Appearance", "TacticalWidgets", c.TacticalWidgets);
            c.HeartRateBleEnabled = false;   // 安全：开关不跨进程保留，每次启动默认关
            c.HeartRateBleAddress = ini.Get("HeartRate", "BleDeviceAddress", c.HeartRateBleAddress);
            c.HeartRateBleName = ini.Get("HeartRate", "BleDeviceName", c.HeartRateBleName);
            c.HeartRateAlarmEnabled = ReadBool(ini, "HeartRate", "AlarmEnabled", c.HeartRateAlarmEnabled);
            c.HeartRateAlarmVolume = ReadInt(ini, "HeartRate", "AlarmVolume", c.HeartRateAlarmVolume);
            c.HeartRateEcgSpeed = ReadInt(ini, "HeartRate", "EcgSpeed", c.HeartRateEcgSpeed);
            c.HeartRateEcgAmplitude = ReadInt(ini, "HeartRate", "EcgAmplitude", c.HeartRateEcgAmplitude);
            c.HeartPromptEnabled = ReadBool(ini, "HeartRate", "PromptEnabled", c.HeartPromptEnabled);
            c.HeartPromptStyle = ReadInt(ini, "HeartRate", "PromptStyle", c.HeartPromptStyle);
            if (c.HeartPromptStyle < 0) c.HeartPromptStyle = 0;
            if (c.HeartPromptStyle > 2) c.HeartPromptStyle = 2;
            c.HeartPromptFrame = ReadBool(ini, "HeartRate", "PromptFrame", c.HeartPromptFrame);
            c.HeartPromptLabel = ReadBool(ini, "HeartRate", "PromptLabel", c.HeartPromptLabel);
            c.HeartPromptUnit = ReadBool(ini, "HeartRate", "PromptUnit", c.HeartPromptUnit);
            c.HeartPromptPulse = ReadBool(ini, "HeartRate", "PromptPulse", c.HeartPromptPulse);
            c.HeartPromptWave = ReadBool(ini, "HeartRate", "PromptWave", c.HeartPromptWave);
            c.HeartPromptWavePoints = ReadInt(ini, "HeartRate", "PromptWavePoints", c.HeartPromptWavePoints);
            c.HeartPromptWaveAmp = ReadInt(ini, "HeartRate", "PromptWaveAmp", c.HeartPromptWaveAmp);
            c.HeartPromptWaveWidth = ReadInt(ini, "HeartRate", "PromptWaveWidth", c.HeartPromptWaveWidth);
            c.HeartPromptFontSize = ReadInt(ini, "HeartRate", "PromptFontSize", c.HeartPromptFontSize);
            c.HeartPromptOpacity = ReadInt(ini, "HeartRate", "PromptOpacity", c.HeartPromptOpacity);
            c.HeartPromptX = ReadInt(ini, "HeartRate", "PromptX", c.HeartPromptX);
            c.HeartPromptY = ReadInt(ini, "HeartRate", "PromptY", c.HeartPromptY);
            c.HeartPromptXPercent = ReadDouble(ini, "HeartRate", "PromptXPercent", c.HeartPromptXPercent);
            c.HeartPromptYPercent = ReadDouble(ini, "HeartRate", "PromptYPercent", c.HeartPromptYPercent);
            c.HeartPromptMonitor = ini.Get("HeartRate", "PromptMonitor", c.HeartPromptMonitor);
            c.HeartPromptColor = ini.Get("HeartRate", "PromptColor", c.HeartPromptColor);
            c.OcrBoxesV = ReadInt(ini, "General", "OcrBoxesV", c.OcrBoxesV);
            c.ScpHpRegionV = ReadInt(ini, "General", "ScpHpRegionV", c.ScpHpRegionV);
            c.StartupHoldMs = ReadInt(ini, "General", "StartupHoldMs", c.StartupHoldMs);
            c.MiniWaveWindowSeconds = ReadInt(ini, "General", "MiniWaveWindowSeconds", c.MiniWaveWindowSeconds);
            c.MiniWaveTickets = ReadInt(ini, "General", "MiniWaveTickets", c.MiniWaveTickets);
            c.MiniWaveDelaySeconds = ReadInt(ini, "General", "MiniWaveDelaySeconds", c.MiniWaveDelaySeconds);
            c.DeconBaseSeconds = ReadInt(ini, "Decontamination", "BaseSeconds", c.DeconBaseSeconds);
            c.DeconAnnounceMap = ini.Get("Decontamination", "AnnounceMap", c.DeconAnnounceMap);
            c.DeconSyncTolerance = ReadInt(ini, "Decontamination", "SyncTolerance", c.DeconSyncTolerance);
            c.CalibPromptEnabled = ReadBool(ini, "Decontamination", "CalibPrompt", c.CalibPromptEnabled);
            c.CalibNumberPrompt = ReadBool(ini, "Decontamination", "CalibNumberPrompt", c.CalibNumberPrompt);
            c.CalibLockPrompt = ReadBool(ini, "Decontamination", "CalibLockPrompt", c.CalibLockPrompt);
            c.CalibPromptSeconds = ClampI(ReadInt(ini, "Decontamination", "CalibPromptSeconds", c.CalibPromptSeconds), 2, 30);
            c.WarheadSyncTolerance = ReadInt(ini, "Warhead", "SyncTolerance", c.WarheadSyncTolerance);
            c.WarheadRestartSeconds = ReadInt(ini, "Warhead", "RestartSeconds", c.WarheadRestartSeconds);
            c.RoundStartKeys = ini.Get("General", "RoundStartKeys", c.RoundStartKeys);
            c.RoundStartOnRole = ReadBool(ini, "General", "RoundStartOnRole", c.RoundStartOnRole);
            c.DeadManMinChancePercent = ReadInt(ini, "General", "DeadManMinChancePercent", c.DeadManMinChancePercent);
            c.DeadManSeconds = ReadInt(ini, "General", "DeadManSeconds", c.DeadManSeconds);
            c.DeadManAfterMiniWaveSeconds = ReadInt(ini, "General", "DeadManAfterMiniWaveSeconds", c.DeadManAfterMiniWaveSeconds);
            c.RespawnRequireTicketsGone = ReadBool(ini, "General", "RespawnRequireTicketsGone", c.RespawnRequireTicketsGone);
            c.HideFromCapture = ReadBool(ini, "General", "HideFromCapture", c.HideFromCapture);
            c.GenAllReadySeconds = ReadInt(ini, "General", "GenAllReadySeconds", c.GenAllReadySeconds);
            c.RespawnAlertPercent = ReadInt(ini, "General", "RespawnAlertPercent", c.RespawnAlertPercent);
            c.AudioDir = ini.Get("General", "AudioDir", c.AudioDir);
            c.AudioDefault = ini.Get("General", "AudioDefault", c.AudioDefault);
            c.AudioSegments = ini.Get("General", "AudioSegments", c.AudioSegments);
            c.AudioVolumes = ini.Get("General", "AudioVolumes", c.AudioVolumes);
            c.AudioTriggerSpec = ini.Get("General", "AudioTriggerSpec", c.AudioTriggerSpec).Replace("\\n", "\n");
            c.DeconFallback = ReadBool(ini, "Decontamination", "Fallback", c.DeconFallback);
            c.SupportAfterRound = ReadInt(ini, "General", "SupportAfterRound", c.SupportAfterRound);
            c.RoundStartDelay = ReadInt(ini, "General", "RoundStartDelay", c.RoundStartDelay);
            c.RoundStartKeys = ini.Get("General", "RoundStartKeys", c.RoundStartKeys);

            // ---- 计时语义改过一次：按版本号迁移一次（老配置自愈）----
            //   · RoundStartDelay：老语义是"准备阶段时长"（老配置里动辄 60 秒），
            //     新语义是"分配角色时长"（进图后玩家等几秒拿到角色），量级完全不同；
            //   · StartupHoldMs：启动演出的"正常状态"从 1 秒改成 3 秒（故障退场固定 7 秒）。
            // 只在这份 ini 还没有版本号时迁移，之后用户自己改的值不会再被覆盖。
            c.TimingV = ReadInt(ini, "General", "TimingV", c.TimingV);
            if (c.TimingV < 1)
            {
                c.RoundStartDelay = 10;
                c.StartupHoldMs = 3000;
                c.TimingV = 1;
                Logger.On("配置", "计时相关配置已迁移到新语义："
                    + "「准备阶段时长」→「分配角色时长」(默认 10 秒)、"
                    + "启动演出正常状态 3 秒 + 故障 7 秒");
            }

            // 第二批计时默认值：
            //   净化"回合开始后多少秒开始"从 600 改成 50（操作者日志实测：回合开始后
            //   约 50 秒就播那句"净化程序将在倒计时 15 分钟后进行"；出生点不在轻收容时
            //   看不到那句字幕，只能靠这个时间兜底，600 太晚了）。
            // 同样只改"还等于老默认值"的那份配置，用户自己调过的不动。
            if (c.TimingV < 2)
            {
                if (c.DeconAfterRound == 600) c.DeconAfterRound = 50;
                c.TimingV = CurrentTimingV;
                Logger.On("配置", "净化兜底时间已按实测改成 50 秒"
                    + "（回合开始后约 50 秒净化倒计时开始；出生点不在轻收容时靠它兜底）");
            }

            c.EnableOcr = ReadBool(ini, "Ocr", "EnableOcr", c.EnableOcr);
            c.OcrOnlyInServer = ReadBool(ini, "Ocr", "OnlyInServer", c.OcrOnlyInServer);
            c.OcrPollInterval = ReadInt(ini, "Ocr", "OcrPollInterval", c.OcrPollInterval);
            c.OcrLang = ini.Get("Ocr", "OcrLang", c.OcrLang);
            c.OcrX = ReadInt(ini, "Ocr", "OcrX", c.OcrX);
            c.OcrYOffset = ReadInt(ini, "Ocr", "OcrYOffset", c.OcrYOffset);
            c.OcrWidth = ReadInt(ini, "Ocr", "OcrWidth", c.OcrWidth);
            c.OcrHeight = ReadInt(ini, "Ocr", "OcrHeight", c.OcrHeight);

            // ---- OCR 监控区域列表 ----
            // 老版本的 config.ini 没有这一段，读完之后 EnsureOcrRegions 会用
            // 上面 [Ocr] 里那四个老字段补出一组“左下角播报”，所以旧配置不用改。
            c.OcrRegions.Clear();
            int regionCount = ReadInt(ini, "OcrRegions", "Count", 0);
            for (int i = 0; i < regionCount; i++)
            {
                string p = "R" + Str(i) + "_";
                OcrRegion rg = new OcrRegion();
                rg.Name = ini.Get("OcrRegions", p + "Name", "");
                rg.X = ReadInt(ini, "OcrRegions", p + "X", rg.X);
                rg.Y = ReadInt(ini, "OcrRegions", p + "Y", rg.Y);
                rg.FromBottom = ReadBool(ini, "OcrRegions", p + "Bottom", rg.FromBottom);
                rg.Width = ReadInt(ini, "OcrRegions", p + "W", rg.Width);
                rg.Height = ReadInt(ini, "OcrRegions", p + "H", rg.Height);
                rg.XPercent = ReadDouble(ini, "OcrRegions", p + "XPct", rg.XPercent);
                rg.YPercent = ReadDouble(ini, "OcrRegions", p + "YPct", rg.YPercent);
                rg.WPercent = ReadDouble(ini, "OcrRegions", p + "WPct", rg.WPercent);
                rg.HPercent = ReadDouble(ini, "OcrRegions", p + "HPct", rg.HPercent);
                rg.Enabled = ReadBool(ini, "OcrRegions", p + "Enabled", rg.Enabled);
                rg.Poll = ReadBool(ini, "OcrRegions", p + "Poll", rg.Poll);
                rg.Kind = OcrRegion.ParseKind(ini.Get("OcrRegions", p + "Kind", "generic"));
                rg.Tag = ini.Get("OcrRegions", p + "Tag", "");
                // 第 4 批：屏幕中心角色 OCR 已整体删除。老配置里存着的 role 区域
                // 读出来直接丢掉（不写回、不扫描），其它区域一个不动。
                if (rg.Kind == OcrRegionKind.Role)
                {
                    Logger.On("OCR", "旧配置里的「" + rg.Name + "」是已删除的屏幕中心角色框，已忽略");
                    continue;
                }
                // 用户在界面上改坐标时常常忘了改"用途"，这里按名字兜一下
                if (rg.Kind == OcrRegionKind.Generic)
                {
                    OcrRegionKind guessed = OcrRegion.GuessKindFromName(rg.Name);
                    if (guessed != OcrRegionKind.Generic)
                    {
                        rg.Kind = guessed;
                        Logger.On("OCR", "区域「" + rg.Name + "」的用途是通用，但名字像「"
                            + OcrRegion.KindName(guessed) + "」→ 已自动按它处理");
                    }
                }
                c.OcrRegions.Add(rg);
            }
            c.EnsureOcrRegions();
            c.MigrateOcrBoxesOnce();   // 一次性把新加的识别框补进老配置
            c.EnsureScpHpRegionOnce(); // 再补一次"SCP 血量表"（单独版本号）
            c.EnsureOcrCoordinates(false);

            // ---- 第 4 批：阵营识别 ----
            c.FactionEnabled = ReadBool(ini, "Faction", "Enabled", c.FactionEnabled);
            c.FactionMaxSampleSeconds = ClampI(ReadInt(ini, "Faction", "MaxSampleSeconds",
                c.FactionMaxSampleSeconds), 1, 10);
            c.FactionConfirmFrames = ClampI(ReadInt(ini, "Faction", "ConfirmFrames",
                c.FactionConfirmFrames), 3, 30);
            c.FactionRespawnDelaySeconds = ClampI(ReadInt(ini, "Faction", "RespawnConfirmSeconds",
                c.FactionRespawnDelaySeconds), 0, 10);
            c.FactionRgbTolerance = ClampD(ReadDouble(ini, "Faction", "RgbTolerance",
                c.FactionRgbTolerance), 0.10, 0.95);
            c.FactionMap = FactionTable.Parse(ini.Get("Faction", "Map", ""));

            // ---- 第 6 批：截屏采集 ----
            c.CaptureMinCount = ClampI(ReadInt(ini, "Capture", "MinCount", c.CaptureMinCount), 1, 21);
            c.CaptureMaxCount = ClampI(ReadInt(ini, "Capture", "MaxCount", c.CaptureMaxCount), 7, 21);
            if (c.CaptureMinCount > c.CaptureMaxCount) c.CaptureMinCount = c.CaptureMaxCount;
            c.CaptureDir = ini.Get("Capture", "Dir", c.CaptureDir);
            c.CaptureReferenceDir = ini.Get("Capture", "ReferenceDir", c.CaptureReferenceDir);
            c.CaptureReferenceFiles = ini.Get("Capture", "ReferenceFiles", c.CaptureReferenceFiles);
            c.CaptureCount = ClampI(ReadInt(ini, "Capture", "Count", c.CaptureCount), 0, 21);
            c.CaptureSelected = ini.Get("Capture", "Selected", c.CaptureSelected);
            c.CaptureSetAt = ini.Get("Capture", "SetAt", c.CaptureSetAt);
            c.CaptureHudXPercent = ReadDouble(ini, "Capture", "HudXPct", c.CaptureHudXPercent);
            c.CaptureHudYPercent = ReadDouble(ini, "Capture", "HudYPct", c.CaptureHudYPercent);
            c.CaptureHudMonitor = ini.Get("Capture", "HudMonitor", c.CaptureHudMonitor);
            c.HotkeyCaptureStart = ini.Get("General", "HotkeyCaptureStart", c.HotkeyCaptureStart);
            c.HotkeyCapturePause = ini.Get("General", "HotkeyCapturePause", c.HotkeyCapturePause);

            for (int i = 0; i < c.Alerts.Length; i++)
                ReadAlert(ini, c.Alerts[i]);

            // ---- 提示默认值迁移（只做一次）----
            // 老配置里这几条是按旧默认值存着的，光改代码默认值影响不到它们：
            //   ① 角色提示以前 Persistent=1 →「你的角色：SCP」会一直挂在屏幕上不消失；
            //   ② 票数提示以前在屏幕右上角（1420,70），现在要放屏幕底端。
            // 票数那个位置只在"用户没拖过"（还是旧默认坐标）时才改，别覆盖人家调好的。
            c.AlertDefaultsV = ReadInt(ini, "General", "AlertDefaultsV", c.AlertDefaultsV);
            if (c.AlertDefaultsV < 1)
            {
                AlertConfig roleA = c.BySection("RoleAlert");
                if (roleA != null) roleA.Persistent = false;

                AlertConfig ticketA = c.BySection("TicketAlert");
                if (ticketA != null)
                {
                    bool untouched = ticketA.Style.X == 1420 && ticketA.Style.Y == 70;
                    if (untouched) { ticketA.Style.X = 620; ticketA.Style.Y = 980; }
                    ticketA.Persistent = false;   // 死亡时靠"重新确认"保持显示，复活就收
                }

                c.AlertDefaultsV = 1;
                Logger.On("配置", "提示默认值已迁移：角色提示不再常驻；"
                    + "票数提示挪到屏幕底端（死亡时显示、复活自动收起）");
            }

            // 第二批默认值（老配置同样要跟上）：
            //   ③ "支援刷新"那条估算倒计时默认关掉（操作者嫌吵，和阵亡/重生概率重复）
            //   ④ 票数提示不再 Persistent（现在活着也会隔几秒扫一次票数，
            //      常驻的话那一条会一直赖在屏幕底下）
            if (c.AlertDefaultsV < 2)
            {
                // 只做一件事：票数提示不再常驻（现在活着也会扫票数，常驻的话
                // 那一条会一直赖在屏幕底下）。「支援刷新」不动 —— 见上面 SupportEstimate 的注释。
                AlertConfig tk2 = c.BySection("TicketAlert");
                if (tk2 != null) tk2.Persistent = false;

                c.AlertDefaultsV = 2;
                Logger.On("配置", "提示默认值已迁移：票数提示不再常驻（死亡时显示、复活自动收起）");
            }

            // 第三批（操作者要求）：
            //   ⑤ 启动演出默认关（一开软件就把所有提示弹一遍太吵）
            //   ⑥ 「重生令牌」默认放到屏幕下方（以前在屏幕中上部 y=550）
            // 位置同样只在"用户没拖过"（还是旧默认坐标）时才改。
            if (c.AlertDefaultsV < 3)
            {
                c.StartupShow = false;

                AlertConfig tokA = c.BySection("TokenAlert");
                if (tokA != null && tokA.Style.X == 30 && tokA.Style.Y == 550)
                {
                    tokA.Style.X = TokenAlertPositionX;
                    tokA.Style.Y = TokenAlertPositionY;
                }

                c.AlertDefaultsV = 3;
                Logger.On("配置", "提示默认值已迁移：启动演出改为默认关闭；"
                    + "「重生令牌」默认挪到屏幕下方 (" + TokenAlertPositionX + ","
                    + TokenAlertPositionY + ")");
            }

            // 第四批（操作者要求）：
            //   ⑦ 「自己重生」原来默认格式是 `你已重生（{0}）`，{0} 是"判定依据"
            //      （右下角静音开关消失了…）—— 操作者说"括号和括号里的提示不要"。
            // 只改还留着老默认格式的那份配置，用户自己改过的格式不动。
            if (c.AlertDefaultsV < 4)
            {
                AlertConfig srA = c.BySection("SelfRespawnAlert");
                if (srA != null && (string.IsNullOrEmpty(srA.Format)
                    || srA.Format == "你已重生（{0}）"))
                {
                    srA.Format = "你已重生";
                    srA.SampleText = "你已重生";
                    srA.SampleText2 = "";
                }

                c.AlertDefaultsV = CurrentAlertDefaultsV;
                Logger.On("配置", "提示默认值已迁移：「自己重生」不再显示括号和判定依据"
                    + "（判定依据仍然写日志）");
            }

            // 战术面板：整段搬进字典（键名不限，面板自己决定有哪些格）
            IniSection tac = ini.Section("Tactical");
            for (int i = 0; i < tac.KeyOrder.Count; i++)
            {
                string k = tac.KeyOrder[i];
                if (k.StartsWith("Comment_", StringComparison.OrdinalIgnoreCase)) continue;
                c._tactical[k] = tac.Values[k];
            }

            c.EnsureScreenCoordinates(false);
            c.Clamp();
            return c;
        }

        private static void ReadAlert(IniFile ini, AlertConfig a)
        {
            string sec = a.Section;
            WindowStyle s = a.Style;

            s.Enabled = ReadBool(ini, sec, "Enabled", s.Enabled);
            s.X = ReadInt(ini, sec, "X", s.X);
            s.Y = ReadInt(ini, sec, "Y", s.Y);
            s.XPercent = ReadDouble(ini, sec, "XPercent", s.XPercent);
            s.YPercent = ReadDouble(ini, sec, "YPercent", s.YPercent);
            s.Monitor = ini.Get(sec, "Monitor", s.Monitor);

            s.FontFace = ini.Get(sec, "Font", s.FontFace);
            s.FontSize = ReadFloat(ini, sec, "FontSize", s.FontSize);
            s.Bold = ReadBool(ini, sec, "Bold", s.Bold);
            s.TextColor = ParseColor(ini.Get(sec, "Color", null), s.TextColor);
            s.Opacity = ReadInt(ini, sec, "Opacity", s.Opacity);

            s.BackColor = ParseColor(ini.Get(sec, "BackColor", null), s.BackColor);
            s.BackOpacity = ReadInt(ini, sec, "BackOpacity", s.BackOpacity);
            s.CornerRadius = ReadInt(ini, sec, "CornerRadius", s.CornerRadius);

            s.Wallpaper = ini.Get(sec, "Wallpaper", s.Wallpaper);
            s.WallpaperMode = ReadEnum(ini, sec, "WallpaperMode", s.WallpaperMode);
            s.WallpaperOpacity = ReadInt(ini, sec, "WallpaperOpacity", s.WallpaperOpacity);

            s.Glass = ReadBool(ini, sec, "Glass", s.Glass);
            s.GlassStrength = ReadInt(ini, sec, "GlassStrength", s.GlassStrength);
            s.GlassHighlight = ReadInt(ini, sec, "GlassHighlight", s.GlassHighlight);
            s.GlassNoise = ReadInt(ini, sec, "GlassNoise", s.GlassNoise);

            s.Anim = ReadEnum(ini, sec, "Anim", s.Anim);
            s.AnimInMs = ReadInt(ini, sec, "AnimInMs", s.AnimInMs);
            s.AnimOutMs = ReadInt(ini, sec, "AnimOutMs", s.AnimOutMs);
            s.SlideDistance = ReadInt(ini, sec, "SlideDistance", s.SlideDistance);

            s.ShowIcon = ReadBool(ini, sec, "ShowIcon", s.ShowIcon);

            a.Format = ini.Get(sec, "Format", a.Format);
            a.TimeFormat = ClampI(ReadInt(ini, sec, "TimeFormat", a.TimeFormat), -1, 3);
            a.Persistent = ReadBool(ini, sec, "Persistent", a.Persistent);
            a.AltText = ini.Get(sec, "AltText", a.AltText);
            a.Estimated = ReadBool(ini, sec, "Estimated", a.Estimated);
            a.PinTopCenter = ReadBool(ini, sec, "PinTopCenter", a.PinTopCenter);
            a.FlashPerMinute = ReadBool(ini, sec, "FlashPerMinute", a.FlashPerMinute);
            a.FlashSeconds = ReadInt(ini, sec, "FlashSeconds", a.FlashSeconds);
            a.FlashPeriodSeconds = ReadInt(ini, sec, "FlashPeriodSeconds", a.FlashPeriodSeconds);
            a.SoonSeconds = ReadInt(ini, sec, "SoonSeconds", a.SoonSeconds);
            a.SoundOn = ReadBool(ini, sec, "SoundOn", a.SoundOn);
            a.SoundKind = ReadInt(ini, sec, "SoundKind", a.SoundKind);
            a.SoundCooldown = ReadInt(ini, sec, "SoundCooldown", a.SoundCooldown);
            a.AudioFile = ini.Get(sec, "AudioFile", a.AudioFile);
            a.AudioPoints = ini.Get(sec, "AudioPoints", a.AudioPoints);
            a.CondType = ReadInt(ini, sec, "CondType", a.CondType);
            a.CondValue = ReadInt(ini, sec, "CondValue", a.CondValue);

            if (a.HasCountdown)
            {
                a.DurationSeconds = ReadInt(ini, sec, "Duration", a.DurationSeconds);
                a.EndText = ini.Get(sec, "EndText", a.EndText);
                a.PulseAt = ParseTimes(ini.Get(sec, "PulseAt", null), a.PulseAt);
                a.PulseSeconds = ReadInt(ini, sec, "PulseSeconds", a.PulseSeconds);
                a.HotBelow = ReadInt(ini, sec, "HotBelow", a.HotBelow);
            }
        }

        /// <summary>把 AudioTriggerSpec 重新解析成 AudioTriggers（界面改完 / 读配置后调）。</summary>
        public void RebuildAudioTriggers()
        {
            AudioTriggers = SCPSLAlert.AudioTriggerSpec.Parse(AudioTriggerSpec);
        }

        /// <summary>
        /// 把旧的「到点放音频」（提示自己的 AudioPoints=`60=a.mp3`）搬成触发点条件。
        ///
        /// 搬完就把那张表清空，所以这个方法是幂等的：跑过一次之后就没有可搬的了，
        /// 不需要额外的"已迁移"标记；也不会出现"新旧两套同时响"。
        /// 迁移结果就是普通触发点，用户可以在界面里改 / 删。
        /// </summary>
        public void MergeLegacyAudioPoints()
        {
            List<AudioTrigger> list = AudioTriggers;
            if (list == null) { list = new List<AudioTrigger>(); AudioTriggers = list; }

            int moved = 0;
            for (int i = 0; i < Alerts.Length; i++)
            {
                AlertConfig a = Alerts[i];
                if (a == null || string.IsNullOrEmpty(a.AudioPoints)) continue;

                List<KeyValuePair<int, string>> pts = AudioLibrary.PointPairs(a.AudioPoints);
                for (int k = 0; k < pts.Count; k++)
                {
                    int at = pts[k].Key;
                    string clip = pts[k].Value;
                    if (at <= 0 || string.IsNullOrEmpty(clip)) continue;

                    AudioTrigger t = new AudioTrigger();
                    t.Name = a.DisplayName + " 剩 " + at + " 秒";
                    t.Clip = clip;
                    t.CooldownSeconds = 0;
                    t.OncePerRound = true;

                    AudioCond c = new AudioCond();
                    c.Event = "alert.remain";
                    c.Arg = a.DisplayName;
                    c.Op = "<=";
                    c.Value = at;
                    t.Conds.Add(c);

                    list.Add(t);
                    moved++;
                }
                a.AudioPoints = "";
            }

            if (moved > 0)
            {
                AudioTriggerSpec = SCPSLAlert.AudioTriggerSpec.Join(list);
                Logger.On("音频", "旧的「到点放音频」已并入触发点，共 " + moved + " 条");
            }
        }

        private void Clamp()
        {
            GameBrightnessIntervalMs = ClampI(GameBrightnessIntervalMs, 500, 15000);
            GameBrightnessHoldGamma = ClampI(GameBrightnessHoldGamma, 20, 160);
            GameBrightnessHoldSaturation = ClampI(GameBrightnessHoldSaturation, 20, 160);
            CrosshairShape = ClampI(CrosshairShape, 0, 11);
            CrosshairSize = ClampI(CrosshairSize, 2, 100);
            CrosshairThickness = ClampI(CrosshairThickness, 1, 12);
            CrosshairGap = ClampI(CrosshairGap, 0, 50);
            CrosshairOpacity = ClampI(CrosshairOpacity, 10, 100);
            CrosshairOffsetX = ClampI(CrosshairOffsetX, -200, 200);
            CrosshairOffsetY = ClampI(CrosshairOffsetY, -200, 200);
            CrosshairRingDurationMs = ClampI(CrosshairRingDurationMs, 100, 5000);
            CrosshairRingRadius = ClampI(CrosshairRingRadius, 8, 150);
            CrosshairExpandDurationMs = ClampI(CrosshairExpandDurationMs, 100, 5000);
            CrosshairExpandAmount = ClampI(CrosshairExpandAmount, 1, 80);

            CrosshairType = ClampI(CrosshairType, 0, 6);
            CrosshairLength = ClampF(CrosshairLength, 0f, 80f);
            CrosshairLineGap = ClampF(CrosshairLineGap, -12f, 60f);
            CrosshairLineThickness = ClampF(CrosshairLineThickness, 0.5f, 12f);
            CrosshairDotSize = ClampF(CrosshairDotSize, 0f, 16f);
            CrosshairRotation = ClampF(CrosshairRotation, 0f, 90f);
            CrosshairRingRadiusF = ClampF(CrosshairRingRadiusF, 6f, 90f);
            CrosshairRingEase = ClampI(CrosshairRingEase, 0, 4);
            CrosshairOuterLength = ClampF(CrosshairOuterLength, 0f, 40f);
            CrosshairOuterThickness = ClampF(CrosshairOuterThickness, 0.5f, 10f);
            CrosshairOuterGap = ClampF(CrosshairOuterGap, 0f, 40f);
            CrosshairOutlineOpacity = ClampI(CrosshairOutlineOpacity, 0, 100);
            CrosshairOutlineWidth = ClampF(CrosshairOutlineWidth, 0f, 5f);
            CrosshairFireAmount = ClampF(CrosshairFireAmount, 0f, 40f);
            CrosshairFireDurationMs = ClampI(CrosshairFireDurationMs, 20, 600);
            CrosshairFireCurve = ClampI(CrosshairFireCurve, 0, 2);
            CrosshairFireMaxMul = ClampF(CrosshairFireMaxMul, 1f, 4f);
            CrosshairRenderFps = ClampI(CrosshairRenderFps, 60, 500);
            if (PollIntervalMs < 100) PollIntervalMs = 100;
            if (PollIntervalMs > 10000) PollIntervalMs = 10000;
            if (GamePollMs < 500) GamePollMs = 500;
            if (GamePollMs > 30000) GamePollMs = 30000;
            if (DisplaySeconds < 1) DisplaySeconds = 1;
            if (DisplaySeconds > 600) DisplaySeconds = 600;
            PanelAnimQuality = ClampI(PanelAnimQuality, 0, 4);
            if (OffsetX < -5000) OffsetX = -5000;
            if (OffsetX > 5000) OffsetX = 5000;
            if (OffsetY < -5000) OffsetY = -5000;
            if (OffsetY > 5000) OffsetY = 5000;
            AlertAvoidGapPx = ClampI(AlertAvoidGapPx, 0, 32);
            AlertAvoidMarginPx = ClampI(AlertAvoidMarginPx, 0, 64);
            AlertAvoidAnimMs = ClampI(AlertAvoidAnimMs, 0, 500);
            DeconAfterRound = ClampI(DeconAfterRound, 10, 36000);
            MiniWaveWindowSeconds = ClampI(MiniWaveWindowSeconds, 10, 600);
            WaveIntervalSeconds = ClampI(WaveIntervalSeconds, 30, 3600);
            WavePerPlayerSeconds = ClampI(WavePerPlayerSeconds, 0, 120);
            RoundStartOffsetSeconds = ClampI(RoundStartOffsetSeconds, 0, 60);
            WaveOffsetSeconds = ClampI(WaveOffsetSeconds, 0, 120);
            MiniWaveTickets = ClampI(MiniWaveTickets, 1, 1000);
            MiniWaveDelaySeconds = ClampI(MiniWaveDelaySeconds, 30, 900);
            if (string.IsNullOrEmpty(TokenThresholds)) TokenThresholds = "30,80,150,200";
            DeconBaseSeconds = ClampI(DeconBaseSeconds, 10, 36000);
            DeconSyncTolerance = ClampI(DeconSyncTolerance, 0, 120);
            WarheadSyncTolerance = ClampI(WarheadSyncTolerance, 0, 120);
            WarheadRestartSeconds = ClampI(WarheadRestartSeconds, 1, 36000);
            RespawnAlertPercent = ClampI(RespawnAlertPercent, 5, 100);
            DeadManMinChancePercent = ClampI(DeadManMinChancePercent, 0, 100);
            DeadManSeconds = ClampI(DeadManSeconds, 10, 600);
            DeadManAfterMiniWaveSeconds = ClampI(DeadManAfterMiniWaveSeconds, 0, 600);
            GenAllReadySeconds = ClampI(GenAllReadySeconds, 3, 600);

            // 自定义音频触发点：从字符串重建列表（界面改完也会调这个方法）
            RebuildAudioTriggers();
            MergeLegacyAudioPoints();
            if (DeconAnnounceMap == null) DeconAnnounceMap = "";
            SupportAfterRound = ClampI(SupportAfterRound, 10, 36000);
            RoundStartDelay = ClampI(RoundStartDelay, 0, 600);
            CloseAction = ClampI(CloseAction, 0, 2);
            if (RoundStartKeys == null) RoundStartKeys = "";
            if (RoundStartKeys.Length > 400) RoundStartKeys = RoundStartKeys.Substring(0, 400);

            // ---- OCR：间隔不能太密，区域不能越界 ----
            OcrPollInterval = ClampI(OcrPollInterval, 300, 30000);
            OcrStrength = ClampI(OcrStrength, 0, 3);
            OcrScale = ClampI(OcrScale, 1, 3);
            if (string.IsNullOrEmpty(OcrLang)) OcrLang = "eng+chi_sim";
            OcrX = ClampI(OcrX, -4000, 8000);
            OcrYOffset = ClampI(OcrYOffset, 0, 4000);
            OcrWidth = ClampI(OcrWidth, 100, 4000);
            OcrHeight = ClampI(OcrHeight, 50, 2000);

            // ---- OCR 区域列表：至少留一组；最多 12 组，免得一个人加出几百组把盘写满 ----
            EnsureOcrRegions();
            if (OcrRegions.Count > 12)
                OcrRegions.RemoveRange(12, OcrRegions.Count - 12);
            for (int i = 0; i < OcrRegions.Count; i++)
            {
                OcrRegion rg = OcrRegions[i];
                if (string.IsNullOrEmpty(rg.Name)) rg.Name = "区域 " + (i + 1);
                if (rg.Name.Length > 24) rg.Name = rg.Name.Substring(0, 24);
                rg.X = ClampI(rg.X, -4000, 8000);
                rg.Y = ClampI(rg.Y, 0, 8000);
                rg.Width = ClampI(rg.Width, 100, 4000);
                rg.Height = ClampI(rg.Height, 50, 2000);
            }

            for (int i = 0; i < Alerts.Length; i++) ClampStyle(Alerts[i]);
        }

        private static void ClampStyle(AlertConfig a)
        {
            WindowStyle s = a.Style;
            if (s.FontSize < 6f) s.FontSize = 6f;
            if (s.FontSize > 200f) s.FontSize = 200f;
            s.Opacity = ClampI(s.Opacity, 10, 100);
            s.BackOpacity = ClampI(s.BackOpacity, 0, 100);
            s.CornerRadius = ClampI(s.CornerRadius, 0, 60);
            s.WallpaperOpacity = ClampI(s.WallpaperOpacity, 0, 100);
            s.GlassStrength = ClampI(s.GlassStrength, 0, 100);
            s.GlassHighlight = ClampI(s.GlassHighlight, 0, 100);
            s.GlassNoise = ClampI(s.GlassNoise, 0, 100);
            s.AnimInMs = ClampI(s.AnimInMs, 0, 3000);
            s.AnimOutMs = ClampI(s.AnimOutMs, 0, 3000);
            s.SlideDistance = ClampI(s.SlideDistance, 0, 200);
            if (a.HasCountdown)
            {
                a.DurationSeconds = ClampI(a.DurationSeconds, 1, 36000);
                a.PulseSeconds = ClampI(a.PulseSeconds, 1, 120);
                a.FlashSeconds = ClampI(a.FlashSeconds, 1, 120);
                a.FlashPeriodSeconds = ClampI(a.FlashPeriodSeconds, 5, 600);
                a.SoonSeconds = ClampI(a.SoonSeconds, 0, 600);
            a.SoundKind = ClampI(a.SoundKind, 0, 5);
            a.SoundCooldown = ClampI(a.SoundCooldown, 0, 60);
            a.CondType = ClampI(a.CondType, 0, 8);
            a.CondValue = ClampI(a.CondValue, 0, 999999);
            }
        }

        private static float ClampF(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static int TypeFromShape(int shape)
        {
            switch (shape)
            {
                case 1: return 1;   // 圆环
                case 2: return 2;   // 中心点
                case 4: return 4;   // 方框
                case 11: return 3;  // 菱形
                case 9: return 5;   // 尖括号 → 三角
                case 3: case 5: case 6: case 7: case 8: case 10: return 0;
                default: return 0;
            }
        }

        private static int ShapeFromType(int type, bool tShape)
        {
            switch (type)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 11;
                case 4: return 4;
                case 5: return 9;
                case 6: return 0;
                default: return tShape ? 8 : 0;
            }
        }

        /// <summary>老配置第一次升级时，用旧字段把新字段填一遍。</summary>
        public void MigrateCrosshairFromLegacy()
        {
            CrosshairType = TypeFromShape(CrosshairShape);
            CrosshairLength = CrosshairSize;
            CrosshairLineGap = CrosshairGap;
            CrosshairLineThickness = CrosshairThickness;
            CrosshairDotSize = CrosshairDot ? 4f : 0f;
            CrosshairRingRadiusF = Math.Max(6f, CrosshairRingRadius);
            CrosshairOutlineOpacity = CrosshairOpacity;
            CrosshairOutlineWidth = CrosshairOutline ? 2f : 0f;
            CrosshairRotation = (CrosshairShape == 5 || CrosshairShape == 6) ? 45f : 0f;
            CrosshairTShape = CrosshairShape == 8;
            CrosshairOuterEnabled = true;
            CrosshairOuterLength = 6f;
            CrosshairOuterThickness = 2f;
            CrosshairOuterGap = 6f;
            CrosshairDynamic = true;
            CrosshairFireSpread = true;
            CrosshairFireAmount = 14f;
            CrosshairFireDurationMs = 120;
            CrosshairFireCurve = 0;
            CrosshairFireMaxMul = 2f;
            CrosshairHitFlash = true;
            CrosshairDpiScale = true;
            CrosshairSnapCenter = true;
            CrosshairRenderFps = 240;
        }

        /// <summary>旧版设置页改完准星后调用：把旧字段映射进新字段。</summary>
        public void SyncCrosshairFromLegacy()
        {
            CrosshairType = TypeFromShape(CrosshairShape);
            CrosshairLength = CrosshairSize;
            CrosshairLineGap = CrosshairGap;
            CrosshairLineThickness = CrosshairThickness;
            CrosshairDotSize = CrosshairDot ? 4f : 0f;
            CrosshairRingRadiusF = Math.Max(6f, CrosshairRingRadius);
            CrosshairOutlineOpacity = CrosshairOpacity;
            CrosshairOutlineWidth = CrosshairOutline ? CrosshairOutlineWidth : 0f;
            if (CrosshairShape == 5 || CrosshairShape == 6) CrosshairRotation = 45f;
            CrosshairTShape = CrosshairShape == 8;
        }

        /// <summary>新版工作台改完准星后调用：把新字段回写成旧字段，旧页面显示才一致。</summary>
        public void SyncCrosshairLegacyMirrors()
        {
            CrosshairShape = ShapeFromType(CrosshairType, CrosshairTShape);
            CrosshairSize = ClampI((int)Math.Round(CrosshairLength), 2, 100);
            CrosshairThickness = ClampI((int)Math.Round(CrosshairLineThickness), 1, 12);
            CrosshairGap = ClampI((int)Math.Round(Math.Max(0f, CrosshairLineGap)), 0, 50);
            CrosshairDot = CrosshairDotSize > 0.01f;
            CrosshairOpacity = ClampI(CrosshairOpacity, 10, 100);
            CrosshairRingRadius = ClampI((int)Math.Round(CrosshairRingRadiusF), 8, 150);
            CrosshairOutline = CrosshairOutlineWidth > 0.01f;
        }

        private static int ClampI(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static double ClampD(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        #endregion

        #region 写

        public void Save(string path)
        {
            try
            {
                ConfigBackup.BeforeSave(path);
                IniFile ini = new IniFile();

                ini.Set("GameBrightness", "Enabled", GameBrightnessEnabled ? "1" : "0");
                ini.Set("GameBrightness", "IntervalMs", Str(GameBrightnessIntervalMs));
                ini.Set("GameBrightness", "CaptureMotionOnly", GameBrightnessCaptureMotionOnly ? "1" : "0");
                ini.Set("GameBrightness", "HoldEnabled", GameBrightnessHoldEnabled ? "1" : "0");
                ini.Set("GameBrightness", "HoldGamma", Str(GameBrightnessHoldGamma));
                ini.Set("GameBrightness", "HoldSaturation", Str(GameBrightnessHoldSaturation));
                ini.Set("GameBrightness", "ForceGlobal", GameBrightnessForceGlobal ? "1" : "0");
                ini.Set("GameBrightness", "Profile", GameBrightnessProfile ?? "");
                ini.Set("GameBrightness", "SaturationProfile", GameBrightnessSaturationProfile ?? "");
                ini.Set("GameBrightness", "Presets", GameBrightnessPresets ?? "");
                ini.Set("GameBrightness", "CompensationBoost", GameBrightnessCompensationBoost.ToString("0.###", CultureInfo.InvariantCulture));
                ini.Set("GameBrightness", "Comment_CompensationBoost", "调伽马时自动补偿饱和度的增强量（0–0.50，默认 0.10）；公式 = 现有系数 0.35 ×（1 + 该值）");
                ini.Set("GameBrightness", "PresetCurrent", GameBrightnessPresetCurrent ?? "");
                ini.Set("Crosshair", "Enabled", CrosshairEnabled ? "1" : "0");
                ini.Set("Crosshair", "Shape", Str(CrosshairShape));
                ini.Set("Crosshair", "Color", Str(CrosshairColor));
                ini.Set("Crosshair", "OutlineColor", Str(CrosshairOutlineColor));
                ini.Set("Crosshair", "Size", Str(CrosshairSize));
                ini.Set("Crosshair", "Thickness", Str(CrosshairThickness));
                ini.Set("Crosshair", "Gap", Str(CrosshairGap));
                ini.Set("Crosshair", "Opacity", Str(CrosshairOpacity));
                ini.Set("Crosshair", "Outline", CrosshairOutline ? "1" : "0");
                ini.Set("Crosshair", "Dot", CrosshairDot ? "1" : "0");
                ini.Set("Crosshair", "OffsetX", Str(CrosshairOffsetX));
                ini.Set("Crosshair", "OffsetY", Str(CrosshairOffsetY));
                ini.Set("Crosshair", "RingKey", CrosshairRingKey ?? "E");
                ini.Set("Crosshair", "RingDurationMs", Str(CrosshairRingDurationMs));
                ini.Set("Crosshair", "RingRadius", Str(CrosshairRingRadius));
                ini.Set("Crosshair", "ExpandKey", CrosshairExpandKey ?? "Space");
                ini.Set("Crosshair", "ExpandDurationMs", Str(CrosshairExpandDurationMs));
                ini.Set("Crosshair", "ExpandAmount", Str(CrosshairExpandAmount));

                // 新版工作台参数
                ini.Set("Crosshair", "Type", Str(CrosshairType));
                ini.Set("Crosshair", "Length", Str(CrosshairLength));
                ini.Set("Crosshair", "LineGap", Str(CrosshairLineGap));
                ini.Set("Crosshair", "LineThickness", Str(CrosshairLineThickness));
                ini.Set("Crosshair", "DotSize", Str(CrosshairDotSize));
                ini.Set("Crosshair", "Rotation", Str(CrosshairRotation));
                ini.Set("Crosshair", "RingRadiusF", Str(CrosshairRingRadiusF));
                ini.Set("Crosshair", "RingEase", Str(CrosshairRingEase));
                ini.Set("Crosshair", "TShape", CrosshairTShape ? "1" : "0");
                ini.Set("Crosshair", "OuterEnabled", CrosshairOuterEnabled ? "1" : "0");
                ini.Set("Crosshair", "OuterLength", Str(CrosshairOuterLength));
                ini.Set("Crosshair", "OuterThickness", Str(CrosshairOuterThickness));
                ini.Set("Crosshair", "OuterGap", Str(CrosshairOuterGap));
                ini.Set("Crosshair", "OutlineOpacity", Str(CrosshairOutlineOpacity));
                ini.Set("Crosshair", "OutlineWidth", Str(CrosshairOutlineWidth));
                ini.Set("Crosshair", "Dynamic", CrosshairDynamic ? "1" : "0");
                ini.Set("Crosshair", "FireSpread", CrosshairFireSpread ? "1" : "0");
                ini.Set("Crosshair", "FireAmount", Str(CrosshairFireAmount));
                ini.Set("Crosshair", "FireDurationMs", Str(CrosshairFireDurationMs));
                ini.Set("Crosshair", "FireCurve", Str(CrosshairFireCurve));
                ini.Set("Crosshair", "FireMaxMul", Str(CrosshairFireMaxMul));
                ini.Set("Crosshair", "HitFlash", CrosshairHitFlash ? "1" : "0");
                ini.Set("Crosshair", "DpiScale", CrosshairDpiScale ? "1" : "0");
                ini.Set("Crosshair", "SnapCenter", CrosshairSnapCenter ? "1" : "0");
                ini.Set("Crosshair", "RenderFps", Str(CrosshairRenderFps));
                ini.Set("GameBrightness", "Comment", "仅游戏在前台时自动调节显示器伽马；切后台或退出时恢复");

                ini.Set("General", "SoundEnabled", SoundEnabled ? "1" : "0");
                ini.Set("General", "Comment_SoundEnabled", "提示音总开关；1 = 开。每种提示还能单独设自己的音效");

                ini.Set("General", "LogDirectory", LogDirectory);
                ini.Set("General", "Comment_LogDirectory",
                    "留空则自动定位 %APPDATA%\\SCP Secret Laboratory\\ServerLogs\\ 下最新的 Round*.txt");
                ini.Set("General", "PollIntervalMs", Str(PollIntervalMs));
                ini.Set("General", "GamePollMs", Str(GamePollMs));
                ini.Set("General", "DisplaySeconds", Str(DisplaySeconds));
                ini.Set("General", "TimeFormat", Str(TimeFormat));
                ini.Set("General", "Comment_TimeFormat", "所有带时间的提示的默认格式：0=自动(<60秒用x秒,<60分用x分yy秒,再往上x小时y分) 1=只显示秒 2=分秒 3=时分；单条提示可在提示详情里覆盖");
                ini.Set("General", "Comment_DisplaySeconds", "事件类提示弹出后显示多少秒自动缩回");
                ini.Set("General", "OffsetX", Str(OffsetX));
                ini.Set("General", "OffsetY", Str(OffsetY));
                ini.Set("General", "AlertAvoidEnabled", AlertAvoidEnabled ? "1" : "0");
                ini.Set("General", "Comment_AlertAvoid", "普通提示防重叠：1=开启，0=所有提示保持原位置；只影响带框提示");
                ini.Set("General", "AlertAvoidGapPx", Str(AlertAvoidGapPx));
                ini.Set("General", "Comment_AlertAvoidGapPx", "提示之间最小间隙（0–32 像素）");
                ini.Set("General", "AlertAvoidMarginPx", Str(AlertAvoidMarginPx));
                ini.Set("General", "Comment_AlertAvoidMarginPx", "工作区边界内边距（0–64 像素）");
                ini.Set("General", "AlertAvoidAnimMs", Str(AlertAvoidAnimMs));
                ini.Set("General", "Comment_AlertAvoidAnimMs", "避让缓动时长（0–500 毫秒；0=瞬移）");
                ini.Set("Export", "Dir", ExportDir ?? "");
                ini.Set("Export", "Comment_Dir", "导出目录；留空 = 程序目录\\导出");
                ini.Set("Export", "Watermark", ExportWatermark ? "1" : "0");
                ini.Set("Export", "Comment_Watermark", "右上角数据来源水印");
                ini.Set("Export", "ShowPlayer", ExportShowPlayer ? "1" : "0");
                ini.Set("Export", "Comment_ShowPlayer", "水印里是否显示玩家标识（默认关）");
                ini.Set("Export", "SplitByMatch", ExportSplitByMatch ? "1" : "0");
                ini.Set("Export", "Comment_SplitByMatch", "1=按对局分割；0=完整长图");
                ini.Set("Export", "DefaultTab", Str(ExportDefaultTab));
                ini.Set("Export", "Comment_DefaultTab", "导出对话框默认页：1=一图流，0=普通图");
                ini.Set("Export", "Txt", ExportTxtReadout ? "1" : "0");
                ini.Set("Export", "Comment_Txt", "额外导出 txt 事件解读");
                ini.Set("Export", "PartMaxPx", Str(ExportPartMaxPx));
                ini.Set("Export", "Comment_PartMaxPx", "一图流高度超过该值时切成 _part01（3000–20000）");
                ini.Set("OneMap", "Heart", OneMapShowHeart ? "1" : "0");
                ini.Set("OneMap", "Comment_Heart", "生理轨开关；没接设备时自动折叠");
                ini.Set("OneMap", "Lttb", Str(OneMapLttb));
                ini.Set("OneMap", "Comment_Lttb", "每条折线 LTTB 抽稀上限（200–4000）");
                ini.Set("OneMap", "Labels", Str(OneMapLabels));
                ini.Set("OneMap", "Comment_Labels", "每条曲线数字标注上限（0–20，必含最大/最小）");
                ini.Set("OneMap", "BlockGap", Str(OneMapBlockGap));
                ini.Set("OneMap", "Comment_BlockGap", "多局纵向拼接间隔（0–120 像素）");
                ini.Set("OneMap", "FpsSmooth", OneMapFpsSmooth ? "1" : "0");
                ini.Set("OneMap", "Comment_FpsSmooth", "FPS 是否画平滑线；0=原始（默认），1=原始淡色+平滑线");
                ini.Set("OneMap", "ExtraDurations", OneMapExtraDurations ? "1" : "0");
                ini.Set("OneMap", "Comment_ExtraDurations", "是否画净化/支援/发电机等持续事件（默认 0；终焉与核弹始终画）");
                ini.Set("Match", "Keep", Str(MatchKeep));
                ini.Set("Match", "Comment_Keep", "保留最近多少局原始记录（5–200）");
                ini.Set("Match", "Heart", MatchRecordHeart ? "1" : "0");
                ini.Set("Match", "Comment_Heart", "对局记录是否包含心率");
                ini.Set("General", "ClickFx", ClickFxEnabled ? "1" : "0");
                ini.Set("General", "Comment_ClickFx", "新界面点击特效（四条线汇聚）；受动效强度与休眠控制");
                ini.Set("General", "ClickFxGap", Str(ClickFxGap));
                ini.Set("General", "Comment_ClickFxGap", "点击特效起始缺口（0–240 像素）");
                ini.Set("General", "ScpScanJoinSeconds", Str(ScpScanJoinSeconds));
                ini.Set("General", "Comment_ScpScanJoinSeconds", "进服后限时识别左侧 SCP 列表的秒数（0–600）；0=进服不扫，仍等第一次死亡");
                ini.Set("General", "HotkeyReleaseV1", HotkeyReleaseV1 ? "1" : "0");
                ini.Set("General", "Comment_HotkeyReleaseV1", "1 = 已释放旧快捷键（核弹取消后重启 / 核弹切换 / SCP 阵亡）；手改对应键即可恢复");
                ini.Set("General", "CoordV", Str(CoordV));
                ini.Set("General", "Comment_CoordV",
                    "坐标体系版本。1 = 提示/心率使用 0..1 归一化坐标（基准=目标显示器），"
                    + "旧 X/Y 仍保留为物理像素；缺失时首次读取会按当前屏幕折算。");
                ini.Set("General", "StatusSeries", StatusSeries);
                ini.Set("General", "Comment_StatusSeries", "状态页显示哪些资源曲线：cpu,mem,gpu,net,disk,temp；空=全部，none=全隐藏");
                ini.Set("General", "Comment_Offset", "整体偏移：所有提示一起挪动的距离");
                ini.Set("General", "HotkeyAdjust", HotkeyAdjust);
                ini.Set("General", "HotkeyToggleAll", HotkeyToggleAll);
                ini.Set("General", "HotkeySettings", HotkeySettings);
                ini.Set("General", "HotkeyPanel", HotkeyPanel);
                ini.Set("General", "HotkeyHoldShowAll", HotkeyHoldShowAll);
                ini.Set("General", "HotkeyCalibrate", HotkeyCalibrate);
                ini.Set("General", "Comment_HotkeyCalibrate", "打开战术面板的「校准」页（时间校准；时间校准已并进战术面板，没有单独窗口了）");
                ini.Set("General", "HotkeyWarhead", HotkeyWarhead);
                ini.Set("General", "Comment_HotkeyWarhead", "核弹：从 90 秒开始");
                ini.Set("General", "HotkeyWarheadShift", HotkeyWarheadShift);
                ini.Set("General", "Comment_HotkeyWarheadShift", "核弹：重置为 80 秒并继续（模拟取消重启）");
                ini.Set("General", "HotkeyWarheadCtrl", HotkeyWarheadCtrl);
                ini.Set("General", "Comment_HotkeyWarheadCtrl", "核弹：强制 90 秒且无法取消（模拟亡者协议）");
                ini.Set("General", "HotkeyDecon", HotkeyDecon);
                ini.Set("General", "Comment_HotkeyDecon", "净化：从 705 秒开始倒计时");
                ini.Set("General", "HotkeyTactical", HotkeyTactical);
                ini.Set("General", "Comment_HotkeyTactical", "打开悬浮式战术面板（票数估算）");
                ini.Set("General", "HotkeyScpDead", HotkeyScpDead);
                ini.Set("General", "Comment_HotkeyScpDead", "SCP 已阵亡提醒");
                ini.Set("General", "HotkeyBigWave", HotkeyBigWave);
                ini.Set("General", "Comment_HotkeyBigWave", "刷新校准：记录刚刷了一波（自动判大波 / 小波）");
                ini.Set("General", "HotkeyClockPause", HotkeyClockPause);
                ini.Set("General", "Comment_HotkeyClockPause", "回合计时手动暂停 / 继续（默认 F5）");
                ini.Set("General", "HotkeyScanNow", HotkeyScanNow);
                ini.Set("General", "Comment_HotkeyScanNow",
                    "立即扫描所有“按需”OCR 区域（默认 F2；F5 已经被回合计时占用）");
                ini.Set("General", "HotkeyStopAll", HotkeyStopAll);
                ini.Set("General", "Comment_HotkeyStopAll",
                    "停止当前所有计时并收起提示（默认 F4）；之后只要再有事件就会自动恢复");
                ini.Set("General", "HotkeyStopAudio", HotkeyStopAudio);
                ini.Set("General", "Comment_HotkeyStopAudio",
                    "立刻停掉正在播放的自备音频（默认 Ctrl+Alt+F12）");
                ini.Set("General", "HotkeyBackground", HotkeyBackground);
                ini.Set("General", "Comment_HotkeyBackground",
                    "第 5 批：后台也生效的快捷键动作名（逗号分隔）："
                  + "edit / toggleAll / settings / panel / calibrate / warhead / decon / tactical / "
                  + "scannow / stopall / stopaudio / bigwave / clockpause。"
                  + "不在名单里 = 只在游戏前台生效并吞掉按键；在名单里 = 后台也生效但不吞键");
                ini.Set("General", "CloseAction", Str(CloseAction));
                ini.Set("General", "Comment_CloseAction",
                    "点设置窗口右上角 × 时怎么办：0=问一次（还没选过） 1=最小化到托盘 2=直接退出程序。"
                  + "第一次点 × 会弹窗让你选，托盘右键菜单里也能随时改");
                ini.Set("General", "PanelAnimQuality", Str(PanelAnimQuality));
                ini.Set("General", "Comment_PanelAnimQuality",
                    "动画效果：0=关（瞬间出现） 1=低(30fps) 2=中(45fps) 3=高(60fps，默认) 4=极高(120fps+额外光效)。"
                  + "战术面板的『全息展开』动画时长也按档位在 1.5~1.9 秒之间；档位越高越耗 CPU");
                ini.Set("General", "OcrToast", OcrToast ? "1" : "0");
                ini.Set("General", "Comment_OcrToast",
                    "屏幕识别认到文字时，在屏幕左下角显示一小块提示（显示认到了什么、做了什么）；1=开 0=关");
                ini.Set("General", "EnableLog", EnableLog ? "1" : "0");
                ini.Set("General", "Comment_EnableLog",
                    "程序自己的运行日志 SCPSLAlert.log；1 = 开（排障用），超过 5MB 自动清空");
                ini.Set("General", "MonitorGameCrash", MonitorGameCrash ? "1" : "0");
                ini.Set("General", "Comment_MonitorGameCrash",
                    "游戏崩溃监控；1 = 开。开启后游戏异常退出会保存退出码、Unity 日志和系统转储。"
                  + "第 5 批起全新配置默认 1；旧配置里写 0 的保持 0，不会被改");
                ini.Set("General", "CrashPopup", CrashPopup ? "1" : "0");
                ini.Set("General", "Comment_CrashPopup",
                    "确认游戏崩溃、或游戏没有正常退出（被结束 / 卡死）时弹窗说明；"
                  + "Alt+F4 和控制台 quit 不打扰；1 = 开 0 = 只记录不弹窗");
                ini.Set("General", "ScanBeforeLaunch", ScanBeforeLaunch ? "1" : "0");
                ini.Set("General", "Comment_ScanBeforeLaunch",
                    "从软件点启动游戏时，先扫描调试器、内存修改器等可疑进程；1 = 开 0 = 直接启动");
                ini.Set("General", "ScanOnGameStart", ScanOnGameStart ? "1" : "0");
                ini.Set("General", "Comment_ScanOnGameStart",
                    "第 5 批：游戏启动（进程出现）时静默跑一次同一套可疑进程扫描；"
                  + "复用现有 GameWatcher.Started，不新增轮询，一次启动只扫一次。1 = 开 0 = 关");
                ini.Set("General", "AutoCalibrateOnJoin", AutoCalibrateOnJoin ? "1" : "0");
                ini.Set("General", "Comment_AutoCalibrateOnJoin", "进入服务器时自动弹出校准窗口");
                ini.Set("Decontamination", "BaseSeconds", Str(DeconBaseSeconds));
                ini.Set("Decontamination", "Comment_BaseSeconds",
                    "净化倒计时的基础时长（秒）。识别到『净化程序启动』播报后从这里往下走；默认 705 秒 = 11 分 45 秒");
                ini.Set("Decontamination", "AnnounceMap", DeconAnnounceMap);
                ini.Set("Decontamination", "Comment_AnnounceMap",
                    "净化播报时间→现实秒数 的对照表（实测不是线性关系）：左边是播报里说的"
                  + "（m=分钟 s=秒），右边是现实该剩多少秒。表里没有的就直接用播报值。默认 15m=652,10m=456,5m=258,1m=60,30s=30");
                ini.Set("Decontamination", "Fallback", DeconFallback ? "1" : "0");
                ini.Set("Decontamination", "Comment_Fallback",
                    "净化兜底：新回合里 OCR 一直没认出净化播报时，按回合时间（DeconAfterRound）自动开始净化倒计时。1=开（默认） 0=关（没识别到就整局不显示净化）");
                ini.Set("Decontamination", "SyncTolerance", Str(DeconSyncTolerance));
                ini.Set("Decontamination", "CalibPrompt", CalibPromptEnabled ? "1" : "0");
                ini.Set("Decontamination", "CalibNumberPrompt", CalibNumberPrompt ? "1" : "0");
                ini.Set("Decontamination", "CalibLockPrompt", CalibLockPrompt ? "1" : "0");
                ini.Set("Decontamination", "CalibPromptSeconds", CalibPromptSeconds.ToString());
                ini.Set("Decontamination", "Comment_SyncTolerance",
                    "净化播报同步容差（秒）：和当前倒计时相差不到这个数就不改，防止识别抖动导致秒数乱跳。默认 10");
                ini.Set("General", "AudioDir", AudioDir);
                ini.Set("General", "Comment_AudioDir", "音频文件夹（默认 = 程序目录下的「音频」）；往里丢 wav/mp3 等文件，然后在各条提示里写文件名");
                ini.Set("General", "AudioDefault", AudioDefault);
                ini.Set("General", "AudioSegments", AudioSegments);
                ini.Set("General", "Comment_AudioSegments", "自定义音频片段：名字=文件名|起秒|止秒，多条用分号分隔（在【音频】页里点「编辑」拖一段保存生成）");
                ini.Set("General", "AudioVolumes", AudioVolumes);
                ini.Set("General", "AudioTriggerSpec", AudioTriggerSpec.Replace("\n", "\\n").Replace("\r", ""));
                ini.Set("General", "Comment_AudioTriggerSpec", "自定义音频触发点：一行一条，格式 名称|启用|音频|冷却秒|每回合一次|事件,阵营,编号,比较符,数值;第二条…（在【音频】页里点「新建触发点」生成）");
                ini.Set("General", "Comment_AudioVolumes", "每段音频/片段自己的音量：键=0~100，多条用分号分隔；键是文件名或 @片段名");
                ini.Set("General", "Comment_AudioDefault", "全局默认音频：任何提示弹出时，如果那条没单独指定音频就放它（留空 = 不放）");
                ini.Set("General", "RespawnAlertPercent", Str(RespawnAlertPercent));
                ini.Set("General", "Comment_RespawnAlertPercent", "重生概率提示阈值（%）：人类整体还能再重生一波的概率涨过它就弹一条。默认 60");
                ini.Set("General", "DeadManMinChancePercent", Str(DeadManMinChancePercent));
                ini.Set("General", "RespawnRequireTicketsGone", RespawnRequireTicketsGone ? "1" : "0");
                ini.Set("General", "HideFromCapture", HideFromCapture ? "1" : "0");
                ini.Set("General", "GenAllReadySeconds", Str(GenAllReadySeconds));
                ini.Set("General", "Comment_GenAllReadySeconds", "『三台发电机已就绪』那条停多少秒（默认 12；以前用的是提示自己的 Duration=120，所以一直黏在屏幕上）");
                ini.Set("General", "Comment_HideFromCapture", "1 = 本程序的提示窗在截图/录屏里隐藏（不会被自己的 OCR 认进去，但 Windows 会提示「受保护的应用程序阻止桌面录制」、录屏录不到这些窗口）；0 = 录屏正常（默认 1）");
                ini.Set("General", "Comment_RespawnRequireTicketsGone", "1 = 严格模式：前台以静音连续 2 次未识别 + 票数扫描未识别为主，游戏窗口重绘辅助；后台用过滤后的游戏窗口重绘判定；0 = 沿用旧 OCR 规则（默认）");
                ini.Set("General", "Comment_DeadManMinChancePercent",
                    "【已停用】旧版终焉启动闸门。现在不再按拿到令牌的概率推迟起表；"
                  + "保留这个键只是为了兼容旧配置，改它不会有任何效果");
                ini.Set("General", "DeadManSeconds", Str(DeadManSeconds));
                ini.Set("General", "Comment_DeadManSeconds",
                    "从『最后那波小波刷完』到终焉的等待时长（秒）。默认 150 = 2 分 30 秒；"
                  + "各服不一样就改这里，也可以在设置「时间」页里按「此刻还剩」校准");
                ini.Set("General", "DeadManAfterMiniWaveSeconds", Str(DeadManAfterMiniWaveSeconds));
                ini.Set("General", "Comment_DeadManAfterMiniWaveSeconds",
                    "最后那波小波刷新后额外等几秒再开始计时（秒）。0 = 小波一记下来就从"
                  + "小波刷新的时刻起表；起表时会按小波时刻回填，屏幕显示的是小波之后的剩余时间");
                ini.Set("Warhead", "SyncTolerance", Str(WarheadSyncTolerance));
                ini.Set("Warhead", "RestartSeconds", Str(WarheadRestartSeconds));
                ini.Set("Warhead", "Comment_RestartSeconds",
                    "Shift+F8「模拟取消后重启」用的秒数（默认 80）。以前写死在程序里，现在可以自己填");
                ini.Set("Warhead", "OcrAuthoritative", WarheadOcrAuthoritative ? "1" : "0");
                ini.Set("Warhead", "Comment_OcrAuthoritative",
                    "1 = 核弹倒计时以 OCR 识别到的「X 秒后自毁」为准（读到就按它重新起算，默认）；0 = 退回按 SyncTolerance 防抖");
                ini.Set("Warhead", "Comment_SyncTolerance",
                    "核弹播报同步容差（秒）：识别到的剩余时间和当前倒计时相差不到这个数就不改。默认 5");
                ini.Set("General", "StartupShow", StartupShow ? "1" : "0");
                ini.Set("General", "ExitWithGame", ExitWithGame ? "1" : "0");
                ini.Set("General", "Comment_ExitWithGame",
                    "游戏退出时是否连带退出本程序：0 = 不跟随（默认，留在托盘）；1 = 跟着退");
                ini.Set("Appearance", "MainUiStyle", Str(MainUiStyle));
                ini.Set("Appearance", "Comment_MainUiStyle",
                    "主界面：0 = 新界面（WPF，默认），1 = 旧界面（经典 WinForms）。"
                    + "首次启动强制旧界面只影响当次，不写进这里。");
                ini.Set("Appearance", "DisplayAssistEnabled", DisplayAssistEnabled ? "1" : "0");
                ini.Set("Appearance", "Comment_DisplayAssistEnabled",
                    "显示与画面总开关：0 = 伽马和准星一起停（各自原开关值保留）；1 = 按各自开关生效（默认）");
                ini.Set("Appearance", "RecognitionEnabled", RecognitionEnabled ? "1" : "0");
                ini.Set("Appearance", "Comment_RecognitionEnabled",
                    "识别总开关：0 = OCR 轮询、F2 手动扫描、阵营识别一起停；1 = 按各自开关生效（默认）");
                ini.Set("Appearance", "AccentHex", AccentHex ?? ThemeTokens.DefaultAccent);
                ini.Set("Appearance", "Comment_AccentHex",
                    "强调色（#RRGGBB）。缺失/非法回落到 #38D6FF；浅色主题会自动压深保证对比度。");
                ini.Set("Appearance", "LightAccentHex", LightAccentHex ?? "#006B84");
                ini.Set("Appearance", "PageBgHex", PageBgHex ?? "#070B10");
                ini.Set("Appearance", "PanelHex", PanelHex ?? "#0C1219");
                ini.Set("Appearance", "PanelAltHex", PanelAltHex ?? "#101A24");
                ini.Set("Appearance", "LineHex", LineHex ?? "#1B2B36");
                ini.Set("Appearance", "TextHex", TextHex ?? "#E6F4F8");
                ini.Set("Appearance", "SubTextHex", SubTextHex ?? "#9AB0BC");
                ini.Set("Appearance", "MutedHex", MutedHex ?? "#5C7480");
                ini.Set("Appearance", "WarnHex", WarnHex ?? "#FFB03A");
                ini.Set("Appearance", "DangerHex", DangerHex ?? "#FF5C5C");
                ini.Set("Appearance", "SuccessHex", SuccessHex ?? "#5BE38C");
                ini.Set("Appearance", "InfoHex", InfoHex ?? "#2F7BFF");
                ini.Set("Appearance", "LightPageBgHex", LightPageBgHex ?? "#EBF1F7");
                ini.Set("Appearance", "LightPanelHex", LightPanelHex ?? "#FFFFFF");
                ini.Set("Appearance", "LightPanelAltHex", LightPanelAltHex ?? "#F2F6FA");
                ini.Set("Appearance", "LightLineHex", LightLineHex ?? "#CCD8E3");
                ini.Set("Appearance", "LightTextHex", LightTextHex ?? "#0A1720");
                ini.Set("Appearance", "LightSubTextHex", LightSubTextHex ?? "#4A5D6C");
                ini.Set("Appearance", "LightMutedHex", LightMutedHex ?? "#8496A4");
                ini.Set("Appearance", "LightWarnHex", LightWarnHex ?? "#B26A00");
                ini.Set("Appearance", "LightDangerHex", LightDangerHex ?? "#C83232");
                ini.Set("Appearance", "LightSuccessHex", LightSuccessHex ?? "#1F7A4D");
                ini.Set("Appearance", "LightInfoHex", LightInfoHex ?? "#0B63CE");
            ini.Set("Appearance", "UiFontFile", UiFontFile ?? "");
            ini.Set("Appearance", "UiFontName", UiFontName ?? "");
            ini.Set("Appearance", "UiTitleFontFile", UiTitleFontFile ?? "");
            ini.Set("Appearance", "UiTitleFontName", UiTitleFontName ?? "");
            ini.Set("Appearance", "UiMonoFontFile", UiMonoFontFile ?? "");
            ini.Set("Appearance", "UiMonoFontName", UiMonoFontName ?? "");
            ini.Set("Appearance", "UiScale", Str(UiScale));
            ini.Set("Appearance", "UiDensity", Str(UiDensity));
            ini.Set("Appearance", "DynamicAccent", DynamicAccent ? "1" : "0");
            ini.Set("Appearance", "CardPreset", Str(CardPreset));
            ini.Set("Appearance", "CardTitle", CardTitle ?? "");
            ini.Set("Appearance", "CardSubtitle", CardSubtitle ?? "");
            ini.Set("Appearance", "CardLine1", CardLine1 ?? "");
            ini.Set("Appearance", "CardLine2", CardLine2 ?? "");
            ini.Set("Appearance", "CardTag", CardTag ?? "");
            ini.Set("Appearance", "CardPlayer", CardPlayer ?? "");
            ini.Set("Appearance", "CardPlayerId", CardPlayerId ?? "");
            ini.Set("Appearance", "CardStat1Label", CardStat1Label ?? "");
            ini.Set("Appearance", "CardStat1Value", CardStat1Value ?? "");
            ini.Set("Appearance", "CardStat2Label", CardStat2Label ?? "");
            ini.Set("Appearance", "CardStat2Value", CardStat2Value ?? "");
            ini.Set("Appearance", "CardStat3Label", CardStat3Label ?? "");
            ini.Set("Appearance", "CardStat3Value", CardStat3Value ?? "");
            ini.Set("Appearance", "CardAccentHex", CardAccentHex ?? "");
            ini.Set("Appearance", "CardTextScale", Str(CardTextScale));
            ini.Set("Appearance", "CardShowGrid", CardShowGrid ? "1" : "0");
            ini.Set("Appearance", "CardShowHex", CardShowHex ? "1" : "0");
            ini.Set("Appearance", "CardShowFrame", CardShowFrame ? "1" : "0");
            ini.Set("Appearance", "CardShowStats", CardShowStats ? "1" : "0");
            ini.Set("Appearance", "CardShowPlayer", CardShowPlayer ? "1" : "0");
            ini.Set("Appearance", "CardShowLive", CardShowLive ? "1" : "0");
            ini.Set("Appearance", "PromptFontFile", PromptFontFile ?? "");
            ini.Set("Appearance", "PromptFontFamily", PromptFontFamily ?? "");
                ini.Set("Appearance", "FontTitleSize", Str(FontTitleSize));
                ini.Set("Appearance", "FontBodySize", Str(FontBodySize));
                ini.Set("Appearance", "FontDescSize", Str(FontDescSize));
                ini.Set("Appearance", "UiFontFamily", UiFontFamily ?? "Microsoft YaHei UI");
                ini.Set("Appearance", "BackgroundImage", BackgroundImage ?? "");
                ini.Set("Appearance", "BackgroundOpacity", Str(BackgroundOpacity));
            ini.Set("Appearance", "GlassWindow", GlassWindow ? "1" : "0");
            ini.Set("Appearance", "GlassStrength", Str(GlassStrength));
            ini.Set("Appearance", "GlassCards", GlassCards ? "1" : "0");
            ini.Set("Appearance", "GlassTintMode", Str(GlassTintMode));
            ini.Set("Appearance", "GlassFrost", Str(GlassFrost));
            ini.Set("Appearance", "GlassOpacity", Str(GlassOpacity));
            ini.Set("Appearance", "GlassLightEnabled", GlassLightEnabled ? "1" : "0");
            ini.Set("Appearance", "GlassLightBrightness", Str(GlassLightBrightness));
            ini.Set("Appearance", "GlassLightSource", Str(GlassLightSource));
            ini.Set("Appearance", "BackgroundFill", Str(BackgroundFill));
            ini.Set("Appearance", "BackgroundDarken", Str(BackgroundDarken));
            ini.Set("Appearance", "BackgroundMusic", BackgroundMusic ?? "");
            ini.Set("Appearance", "BackgroundMusicEnabled", BackgroundMusicEnabled ? "1" : "0");
            ini.Set("Appearance", "BackgroundMusicLoop", BackgroundMusicLoop ? "1" : "0");
            ini.Set("Appearance", "BackgroundMusicVolume", Str(BackgroundMusicVolume));
                ini.Set("Appearance", "ThemePack", ThemePack ?? "");
                ini.Set("Appearance", "ThemeSchema", Str(ThemeSchema));
                ini.Set("Appearance", "ThemeMode", Str(UiThemeMode));
                ini.Set("Appearance", "PromptTheme", Str(PromptTheme));
                ini.Set("Appearance", "Comment_PromptTheme",
                    "屏幕提示主题：-1 = 跟随主界面（新界面默认新版 A.R.C.T 提示，旧界面默认经典提示），"
                    + "0 = 固定经典提示，1 = 固定新版 A.R.C.T 提示");
                ini.Set("Appearance", "AnimationsEnabled", UiAnimationsEnabled ? "1" : "0");
                ini.Set("Appearance", "AnimationDurationMs", Str(UiAnimationDurationMs));
                ini.Set("Appearance", "TacticalOpacity", Str(TacticalOpacity));
                ini.Set("Appearance", "TacticalOpenStyle", Str(TacticalOpenStyle));
                ini.Set("Appearance", "TacticalWidgets", TacticalWidgets == null ? "" : TacticalWidgets);
                ini.Set("HeartRate", "BleEnabled", "0");
                ini.Set("HeartRate", "BleDeviceAddress", HeartRateBleAddress == null ? "" : HeartRateBleAddress);
                ini.Set("HeartRate", "BleDeviceName", HeartRateBleName == null ? "" : HeartRateBleName);
                ini.Set("HeartRate", "AlarmEnabled", HeartRateAlarmEnabled ? "1" : "0");
                ini.Set("HeartRate", "AlarmVolume", Str(HeartRateAlarmVolume));
                ini.Set("HeartRate", "EcgSpeed", Str(HeartRateEcgSpeed));
                ini.Set("HeartRate", "EcgAmplitude", Str(HeartRateEcgAmplitude));
                ini.Set("HeartRate", "PromptEnabled", HeartPromptEnabled ? "1" : "0");
                ini.Set("HeartRate", "PromptStyle", Str(HeartPromptStyle));
                ini.Set("HeartRate", "PromptFrame", HeartPromptFrame ? "1" : "0");
                ini.Set("HeartRate", "PromptLabel", HeartPromptLabel ? "1" : "0");
                ini.Set("HeartRate", "PromptUnit", HeartPromptUnit ? "1" : "0");
                ini.Set("HeartRate", "PromptPulse", HeartPromptPulse ? "1" : "0");
                ini.Set("HeartRate", "PromptWave", HeartPromptWave ? "1" : "0");
                ini.Set("HeartRate", "PromptWavePoints", Str(HeartPromptWavePoints));
                ini.Set("HeartRate", "PromptWaveAmp", Str(HeartPromptWaveAmp));
                ini.Set("HeartRate", "PromptWaveWidth", Str(HeartPromptWaveWidth));
                ini.Set("HeartRate", "PromptFontSize", Str(HeartPromptFontSize));
                ini.Set("HeartRate", "PromptOpacity", Str(HeartPromptOpacity));
                ini.Set("HeartRate", "PromptX", Str(HeartPromptX));
                ini.Set("HeartRate", "PromptY", Str(HeartPromptY));
                ini.Set("HeartRate", "PromptXPercent", Str(HeartPromptXPercent));
                ini.Set("HeartRate", "PromptYPercent", Str(HeartPromptYPercent));
                ini.Set("HeartRate", "PromptMonitor", HeartPromptMonitor == null ? "" : HeartPromptMonitor);
                ini.Set("HeartRate", "Comment_PromptXYPercent",
                    "0..1 归一化坐标，基准=PromptMonitor 指定的显示器；-1 的 X/Y 仍表示自动右上角。");
                ini.Set("HeartRate", "PromptColor", HeartPromptColor == null ? "" : HeartPromptColor);
                ini.Set("HeartRate", "Comment_PromptStyle",
                    "心率屏幕提示样式：0 = 完整卡片，1 = 无框，2 = 只显示数字");
                ini.Set("HeartRate", "Comment_PromptX",
                    "心率提示位置（物理像素兜底）；-1 = 自动放主屏右上角。"
                  + "实际位置以 PromptXPercent/PromptYPercent（0..1，基准=PromptMonitor）为准。");
                ini.Set("HeartRate", "Comment_EcgSpeed", "心电图滚动速度（像素/秒），默认 55；越大越快");
                ini.Set("HeartRate", "Comment_EcgAmplitude", "心电图振幅百分比，默认 100；50~150");
                ini.Set("HeartRate", "Comment_BleEnabled", "此开关只在当前进程内有效：启动默认关闭，关闭软件后下次打开不会自动开启");
                ini.Set("HeartRate", "Comment_AlarmEnabled", "角色死亡时是否播放一声监护仪长音（1/0）");
                ini.Set("HeartRate", "Comment_AlarmVolume", "死亡报警音音量 0~100，默认 25");
                ini.Set("General", "Comment_StartupShow",
                    "启动演出：程序启动时把所有提示弹一遍、故障式退场（默认 0=关）。想开机看一遍就写 1");
                ini.Set("General", "RoundStartOffsetSeconds", Str(RoundStartOffsetSeconds));
                ini.Set("General", "Comment_RoundStartOffsetSeconds",
                    "修正：日志里『回合开始』比游戏真实开始早几秒（实测约 5 秒），把起点往后延这么多。默认 5");
                ini.Set("General", "WaveOffsetSeconds", Str(WaveOffsetSeconds));
                ini.Set("General", "Comment_WaveOffsetSeconds",
                    "修正：预计大波刷新时间比推算晚几秒（实测约 15 秒），倒计时加这么多。默认 15");
                ini.Set("General", "WaveIntervalSeconds", Str(WaveIntervalSeconds));
                ini.Set("General", "Comment_WaveIntervalSeconds",
                    "大波基准间隔（秒）：两次重生波之间的基准时间。实际间隔 = 它 + 上一波人数 × WavePerPlayerSeconds。默认 300（可在「时间」页用日志校准）");
                ini.Set("General", "WavePerPlayerSeconds", Str(WavePerPlayerSeconds));
                ini.Set("General", "Comment_WavePerPlayerSeconds",
                    "重生方每重生一个玩家，计时器额外加多少秒。默认 10");
                ini.Set("General", "OcrBoxesV", Str(OcrBoxesV));
                ini.Set("General", "Comment_OcrBoxesV",
                    "识别框配置版本。程序靠它判断『要不要把新加的默认框补进来』；补过一次就不再补（删掉的不会自己长回来）");
                ini.Set("General", "ScpHpRegionV", Str(ScpHpRegionV));
                ini.Set("General", "Comment_ScpHpRegionV",
                    "『SCP 血量表』识别框的独立补丁版本（单独一个版本号，这样老配置也能补上这块）");
                ini.Set("General", "TimingV", Str(TimingV));
                ini.Set("General", "Comment_TimingV",
                    "计时相关配置的迁移版本。1 = 「分配角色时长」新语义 + 启动演出 3 秒正常 / 7 秒故障（老配置第一次启动会自动迁移一次）");
                ini.Set("General", "AlertDefaultsV", Str(AlertDefaultsV));
                ini.Set("General", "Comment_AlertDefaultsV",
                    "提示默认值的迁移版本。1 = 角色提示不再常驻 + 票数提示挪到屏幕底端（老配置第一次启动会自动迁移一次）");
                ini.Set("General", "StartupHoldMs", Str(StartupHoldMs));
                ini.Set("General", "Comment_StartupHoldMs",
                    "启动演出：所有提示保持正常状态多少毫秒之后才开始故障退场。默认 3000（3 秒）；故障退场固定 7 秒，整段演出约 10 秒");
                ini.Set("General", "TokenThresholds", TokenThresholds);                ini.Set("General", "Comment_TokenThresholds",
                    "重生令牌阈值（需求二）：票数达到这些值时各弹一次提示，逗号分隔。默认 30,80,150,200（第一个/第二个/第三个/第四个令牌）");
                ini.Set("General", "MiniWaveWindowSeconds", Str(MiniWaveWindowSeconds));
                ini.Set("General", "Comment_MiniWaveWindowSeconds",
                    "小波条件：大波刷新后多少秒内打够 MiniWaveTickets 票就算打出小波。默认 120（2 分钟）");
                ini.Set("General", "MiniWaveTickets", Str(MiniWaveTickets));
                ini.Set("General", "Comment_MiniWaveTickets", "小波条件里需要多少票，默认 10");
                ini.Set("General", "MiniWaveDelaySeconds", Str(MiniWaveDelaySeconds));
                ini.Set("General", "Comment_MiniWaveDelaySeconds",
                    "小波最早在主波后多少秒出现（文档 1.2.4 默认 150 = 2 分 30 秒）。"
                  + "只影响面板的『小波 约 mm:ss 后』推测和『即将刷新大波/小波』那条标签；"
                  + "实测偏晚就往大改（比如 190）。");
                ini.Set("General", "DeconAfterRound", Str(DeconAfterRound));
                ini.Set("General", "RoundStartOnRole", RoundStartOnRole ? "1" : "0");
                ini.Set("General", "Comment_RoundStartOnRole", "1 = 认出正式角色（进图后先是观察者，几秒后变角色）时立刻开始回合计时；默认 0");
                ini.Set("General", "SupportAfterRound", Str(SupportAfterRound));
                ini.Set("General", "Comment_SupportAfterRound", "回合开始后多少秒刷新支援（用于估算，默认 330）");
                ini.Set("General", "RoundStartDelay", Str(RoundStartDelay));
                ini.Set("General", "Comment_RoundStartDelay",
                    "分配角色时长（秒）：日志里出现『进图』(SceneLoaded 2. Facility) 时游戏其实是在分配角色，"
                  + "玩家还要等几秒才拿到角色。填大于 0 就从那一刻起等这么久再开始正式计时；"
                  + "期间出现回合开始标志会立刻开始。0 = 进图就计时");
                ini.Set("General", "RoundStartKeys", RoundStartKeys);
                ini.Set("General", "Comment_RoundStartKeys",
                    "“回合正式开始”的关键字，逗号分隔；日志里出现任意一条就立刻开始计时，"
                  + "不用等分配角色时长走完。各服提示不一样，按自己服务器改");

                // ---- 屏幕 OCR 播报识别 ----
                ini.Set("Ocr", "EnableOcr", EnableOcr ? "1" : "0");
                ini.Set("Ocr", "OnlyInServer", OcrOnlyInServer ? "1" : "0");
                ini.Set("Ocr", "Comment_OnlyInServer",
                    "1 = 进入服务器之后才开始扫描（默认）：桌面 / 主菜单不截图、不调用引擎，"
                  + "进服后自动开始、离服立刻停下。0 = 一直扫");
                ini.Set("Ocr", "Comment_EnableOcr", "屏幕播报识别总开关；1 = 开，0 = 完全关闭（连 OCR 引擎都不加载）");
                ini.Set("Ocr", "OcrPollInterval", Str(OcrPollInterval));
                ini.Set("Ocr", "Comment_OcrPollInterval", "轮询间隔（毫秒，默认 1500）；只有画面出现高亮像素时才会真的调用 OCR");
                ini.Set("Ocr", "OcrLang", OcrLang);
                ini.Set("Ocr", "Comment_OcrLang", "识别语言，对应程序目录下 tessdata 里的文件名；初始化失败会自动降级成 eng");
                ini.Set("Ocr", "Strength", Str(OcrStrength));
                ini.Set("Ocr", "Comment_Strength",
                    "识别强度：0=保守 1=标准 2=激进（默认）。越激进越容易从被场景压暗的播报文字里挑出字，"
                  + "代价是可能多认进一些画面噪点；误报太多就调回 1 或 0");
                ini.Set("Ocr", "RetryRaw", OcrRetryRaw ? "1" : "0");
                ini.Set("Ocr", "Comment_RetryRaw",
                    "第一次没认好时，用原始灰度图（反色 + 放大）再认一次 —— 提高识别率，代价是失败时多花一次调用。"
                  + "默认 0=跟随识别强度（开到「最大」时自动启用）；填 1 则在任何强度下都启用");
                ini.Set("Ocr", "Scale", Str(OcrScale));
                ini.Set("Ocr", "Comment_Scale", "灰度重试的放大倍数 1~3（默认 2）。Tesseract 最喜欢的字高约 30px，放大对小字帮助明显");
                ini.Set("Ocr", "BestModel", OcrBestModel ? "1" : "0");
                ini.Set("Ocr", "Comment_BestModel",
                    "语言模型精度：0=快速版（tessdata，约 2.5MB，默认） 1=最高精度版（tessdata_best，约 13MB，更准但慢一些）。"
                  + "切换后程序会自动重建 OCR 引擎，不用重启");
                ini.Set("Ocr", "OcrX", Str(OcrX));
                ini.Set("Ocr", "Comment_OcrX", "识别区域左上角 X");
                ini.Set("Ocr", "OcrYOffset", Str(OcrYOffset));
                ini.Set("Ocr", "Comment_OcrYOffset", "识别区域下边距：实际 Y = 屏幕高度 - 这个值（默认 400）");
                ini.Set("Ocr", "OcrWidth", Str(OcrWidth));
                ini.Set("Ocr", "Comment_OcrWidth", "识别区域宽度（默认 1000，够宽才能应付突发多行播报）");
                ini.Set("Ocr", "OcrHeight", Str(OcrHeight));
                ini.Set("Ocr", "Comment_OcrHeight", "识别区域高度（默认 400）");

                // 下面这一组是“区域列表第 1 组”的镜像，只为兼容旧版本读法。
                // 真正的数据在 [OcrRegions] 里，改的时候改那边。
                SyncOcrCoordinates();
                if (OcrRegions != null && OcrRegions.Count > 0)
                {
                    OcrRegion first = OcrRegions[0];
                    ini.Set("Ocr", "OcrX", Str(first.X));
                    ini.Set("Ocr", "OcrYOffset", Str(first.Y));
                    ini.Set("Ocr", "OcrWidth", Str(first.Width));
                    ini.Set("Ocr", "OcrHeight", Str(first.Height));
                }

                // ---- OCR 监控区域列表（真正的数据在这里）----
                SyncOcrCoordinates();
                ini.Set("OcrRegions", "Comment",
                    "OCR 监控区域。第 1 组默认是左下角播报（Poll=1，按 OcrPollInterval 定时轮询）；"
                  + "其余各组的 Poll 都是 0，只在按“立即扫描”快捷键时截一次。");
                ini.Set("OcrRegions", "Comment_Kind",
                    "Kind：broadcast=播报 / health=血量 / generator=发电机 / generic=通用 / "
                  + "ticket=死亡界面的票数 / silence=静音开关 / scplist=左侧 SCP 列表 / scphp=SCP 血量表"
                  + "（屏幕中心角色框已于第 4 批删除，老配置里的 role 会被忽略）");
                ini.Set("OcrRegions", "Comment_Tag",
                    "Tag：细分用途，ticket 类型用得到 —— "
                  + "mtf_tokens=左侧大数字（MTF 可用令牌）/ mtf_influence=左侧小数字（MTF 影响力，如 49/80）/ "
                  + "chaos_tokens=右侧大数字 / chaos_influence=右侧小数字");
                ini.Set("OcrRegions", "Comment_Bottom",
                    "Bottom：1 = Y 是“距屏幕底部的边距”；0 = Y 是屏幕上的绝对纵坐标");
                ini.Set("OcrRegions", "Comment_Coord",
                    "XPct/YPct/WPct/HPct：0..1，基准=游戏客户区（左上角原点，X 向右、Y 向下）。"
                  + "X/Y/W/H 是同一区域的物理像素，给旧界面和回滚用；两者以百分比为准。");
                ini.Set("OcrRegions", "Count", Str(OcrRegions == null ? 0 : OcrRegions.Count));
                if (OcrRegions != null)
                {
                    for (int i = 0; i < OcrRegions.Count; i++)
                    {
                        OcrRegion rg = OcrRegions[i];
                        string p = "R" + Str(i) + "_";
                        ini.Set("OcrRegions", p + "Name", rg.Name);
                        ini.Set("OcrRegions", p + "X", Str(rg.X));
                        ini.Set("OcrRegions", p + "Y", Str(rg.Y));
                        ini.Set("OcrRegions", p + "Bottom", rg.FromBottom ? "1" : "0");
                        ini.Set("OcrRegions", p + "W", Str(rg.Width));
                        ini.Set("OcrRegions", p + "H", Str(rg.Height));
                        ini.Set("OcrRegions", p + "XPct", Str(rg.XPercent));
                        ini.Set("OcrRegions", p + "YPct", Str(rg.YPercent));
                        ini.Set("OcrRegions", p + "WPct", Str(rg.WPercent));
                        ini.Set("OcrRegions", p + "HPct", Str(rg.HPercent));
                        ini.Set("OcrRegions", p + "Enabled", rg.Enabled ? "1" : "0");
                        ini.Set("OcrRegions", p + "Poll", rg.Poll ? "1" : "0");
                        ini.Set("OcrRegions", p + "Kind", OcrRegion.KindKey(rg.Kind));
                        ini.Set("OcrRegions", p + "Tag", rg.Tag == null ? "" : rg.Tag);
                    }
                }

                SyncLegacyScreenCoordinates();

                // ---- 第 4 批：阵营识别 ----
                ini.Set("Faction", "Comment",
                    "阵营识别：开局角色分配完成 / 复活后，读左下角血条最左 10% 的颜色判定阵营。"
                  + "不截图整屏、不调用 OCR 引擎。");
                ini.Set("Faction", "Enabled", FactionEnabled ? "1" : "0");
                ini.Set("Faction", "MaxSampleSeconds", Str(FactionMaxSampleSeconds));
                ini.Set("Faction", "ConfirmFrames", Str(FactionConfirmFrames));
                ini.Set("Faction", "RespawnConfirmSeconds", Str(FactionRespawnDelaySeconds));
                ini.Set("Faction", "RgbTolerance", FactionRgbTolerance.ToString("0.###", CultureInfo.InvariantCulture));
                ini.Set("Faction", "Comment_Map",
                    "Map：名称|色相H|饱和S|明度V|色相容差|饱和容差|明度容差，分号分隔。"
                  + "默认值来自 阵营截图\\ 的 14 张实测（SCP 目前只有 173 的红色，其它 SCP 可能不同）。");
                ini.Set("Faction", "Map", FactionTable.Serialize(FactionMap));

                // ---- 第 6 批：截屏采集 ----
                ini.Set("Capture", "Comment",
                    "截屏采集：一次 21 张，先 7 张基础，按亮度分位挑 7 张展示，下次整体覆盖。"
                  + "采集目录里的 png 上限就是 MaxCount。");
                ini.Set("Capture", "MinCount", Str(CaptureMinCount));
                ini.Set("Capture", "Comment_MinCount",
                    "基础张数：采到这张时提示「基础的图片已采集完成」，之后继续采到 MaxCount");
                ini.Set("Capture", "MaxCount", Str(CaptureMaxCount));
                ini.Set("Capture", "Comment_MaxCount", "上限张数（7–21，默认 21）；文件夹最多保留这么多张");
                ini.Set("Capture", "Dir", CaptureDir);
                ini.Set("Capture", "Comment_Dir", "采集目录（相对程序目录，或绝对路径）");
                ini.Set("Capture", "ReferenceDir", CaptureReferenceDir);
                ini.Set("Capture", "Comment_ReferenceDir",
                    "亮度参考图整体替换目录；留空 = 自动找 Steam 游戏截图目录，找不到就用内置校色卡");
                ini.Set("Capture", "ReferenceFiles", CaptureReferenceFiles);
                ini.Set("Capture", "Comment_ReferenceFiles",
                    "逐张替换：7 个绝对路径，分号分隔；空位 = 继续用自动/校色卡");
                ini.Set("Capture", "Count", Str(CaptureCount));
                ini.Set("Capture", "Selected", CaptureSelected);
                ini.Set("Capture", "Comment_Selected",
                    "上一次挑中的 7 张：按亮度升序后的序号（1 基），逗号分隔");
                ini.Set("Capture", "SetAt", CaptureSetAt);
                ini.Set("Capture", "HudXPct", Str(CaptureHudXPercent));
                ini.Set("Capture", "HudYPct", Str(CaptureHudYPercent));
                ini.Set("Capture", "HudMonitor", CaptureHudMonitor);
                ini.Set("Capture", "Comment_Hud",
                    "进度提示位置：0..1，基准 = 目标显示器；留空 = 游戏窗口所在屏幕顶部居中");
                ini.Set("General", "HotkeyCaptureStart", HotkeyCaptureStart);
                ini.Set("General", "HotkeyCapturePause", HotkeyCapturePause);
                ini.Set("General", "Comment_HotkeyCapture", "开始 / 暂停截屏采集的全局快捷键（留空 = 不注册）");

                for (int i = 0; i < Alerts.Length; i++)
                    WriteAlert(ini, Alerts[i]);

                // 战术面板的输入内容
                ini.Set("Tactical", "Comment", "战术面板(F10)里填的 SCP 血量；留空表示满血无损失");
                List<string> tkeys = new List<string>(_tactical.Keys);
                tkeys.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < tkeys.Count; i++)
                    ini.Set("Tactical", tkeys[i], _tactical[tkeys[i]]);

                ini.Save(path);
            }
            catch
            {
                // 配置文件被占用或目录只读时忽略，不影响运行
            }
        }

        private static void WriteAlert(IniFile ini, AlertConfig a)
        {
            string sec = a.Section;
            WindowStyle s = a.Style;

            ini.Set(sec, "Enabled", s.Enabled ? "1" : "0");
            ini.Set(sec, "X", Str(s.X));
            ini.Set(sec, "Y", Str(s.Y));
            ini.Set(sec, "XPercent", Str(s.XPercent));
            ini.Set(sec, "YPercent", Str(s.YPercent));
            ini.Set(sec, "Comment_XYPercent",
                "0..1 归一化坐标，基准=Monitor 指定的显示器（左上角原点，X 向右、Y 向下）。"
                + "X/Y 是同一位置的物理像素，给旧界面和回滚用；两者以 XPercent/YPercent 为准。");
            ini.Set(sec, "Monitor", s.Monitor == null ? "" : s.Monitor);
            ini.Set(sec, "Comment_Monitor", "显示器设备名（如 \\\\.\\DISPLAY1）；空=按 X/Y 所在屏幕。");
            ini.Set(sec, "Persistent", a.Persistent ? "1" : "0");
            ini.Set(sec, "Comment_Persistent", "1 = 常驻显示，不自动缩回（所有提示都可以开）");
            ini.Set(sec, "PinTopCenter", a.PinTopCenter ? "1" : "0");
            ini.Set(sec, "Comment_PinTopCenter", "1 = 自动钉在屏幕正上方居中");
            ini.Set(sec, "FlashPerMinute", a.FlashPerMinute ? "1" : "0");
            ini.Set(sec, "Comment_FlashPerMinute", "1 = 每分钟只露一小段，不一直挂在屏幕上");
            ini.Set(sec, "FlashSeconds", Str(a.FlashSeconds));
            ini.Set(sec, "FlashPeriodSeconds", Str(a.FlashPeriodSeconds));
            ini.Set(sec, "Comment_FlashPeriodSeconds", "隔多久露一段（秒）：60 = 每逢整分钟露一段；支援刷新默认 30（每 30 秒报一次）");
            ini.Set(sec, "SoonSeconds", Str(a.SoonSeconds));
            ini.Set(sec, "Comment_SoonSeconds", "最后多少秒持续显示（即将刷新）");

            ini.Set(sec, "SoundOn", a.SoundOn ? "1" : "0");
            ini.Set(sec, "Comment_SoundOn", "1 = 这条提示弹出时响一声");
            ini.Set(sec, "SoundKind", Str(a.SoundKind));
            ini.Set(sec, "Comment_SoundKind", "0=不响 1=提示 2=感叹 3=警告 4=疑问 5=默认");
            ini.Set(sec, "SoundCooldown", Str(a.SoundCooldown));
            ini.Set(sec, "AudioFile", a.AudioFile);
            ini.Set(sec, "Comment_AudioFile", "这条提示弹出时播放的音频（放在「音频」文件夹里，写文件名；留空 = 用全局默认 AudioDefault）");
            ini.Set(sec, "AudioPoints", a.AudioPoints);
            ini.Set(sec, "Comment_AudioPoints", "倒计时到点放音频，形如 60=一分钟.mp3,30=紧张.mp3,10=最后警告.mp3（每个阈值只放一次；留空 = 不放）");
            ini.Set(sec, "Comment_SoundCooldown", "秒；这段时间内不重复响，避免连刷时太吵");

            ini.Set(sec, "CondType", Str(a.CondType));
            ini.Set(sec, "Comment_CondType", "条件显示，0=无条件；见使用说明里的条件列表");
            ini.Set(sec, "CondValue", Str(a.CondValue));
            ini.Set(sec, "Comment_CondValue", "配合 CondType 用的数值");
            if (a.HasCountdown)
            {
                ini.Set(sec, "Estimated", a.Estimated ? "1" : "0");
                ini.Set(sec, "Comment_Estimated", "1 = 由时间估算驱动；加入别人服务器时也能显示，并可在校准窗口里按剩余时间校准");
            }

            ini.Set(sec, "Font", s.FontFace);
            ini.Set(sec, "FontSize", Str(s.FontSize));
            ini.Set(sec, "Bold", s.Bold ? "1" : "0");
            ini.Set(sec, "Color", ColorToText(s.TextColor));
            ini.Set(sec, "Opacity", Str(s.Opacity));

            ini.Set(sec, "BackColor", ColorToText(s.BackColor));
            ini.Set(sec, "BackOpacity", Str(s.BackOpacity));
            ini.Set(sec, "CornerRadius", Str(s.CornerRadius));

            ini.Set(sec, "Wallpaper", s.Wallpaper);
            ini.Set(sec, "WallpaperMode", s.WallpaperMode.ToString());
            ini.Set(sec, "WallpaperOpacity", Str(s.WallpaperOpacity));

            ini.Set(sec, "Glass", s.Glass ? "1" : "0");
            ini.Set(sec, "GlassStrength", Str(s.GlassStrength));
            ini.Set(sec, "GlassHighlight", Str(s.GlassHighlight));
            ini.Set(sec, "GlassNoise", Str(s.GlassNoise));

            ini.Set(sec, "Anim", s.Anim.ToString());
            ini.Set(sec, "AnimInMs", Str(s.AnimInMs));
            ini.Set(sec, "AnimOutMs", Str(s.AnimOutMs));
            ini.Set(sec, "SlideDistance", Str(s.SlideDistance));

            ini.Set(sec, "ShowIcon", s.ShowIcon ? "1" : "0");

            ini.Set(sec, "Format", a.Format);
            ini.Set(sec, "TimeFormat", Str(a.TimeFormat));
            if (a.Section == "GameLaunch") ini.Set(sec, "AltText", a.AltText);
            if (a.HasCountdown)
            {
                ini.Set(sec, "Duration", Str(a.DurationSeconds));
                ini.Set(sec, "EndText", a.EndText);
                ini.Set(sec, "PulseAt", TimesToText(a.PulseAt));
                ini.Set(sec, "PulseSeconds", Str(a.PulseSeconds));
                ini.Set(sec, "HotBelow", Str(a.HotBelow));
            }
        }

        #endregion

        #region 工具

        private static string Str(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string Str(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string Str(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? ""
                : v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static int ReadInt(IniFile ini, string sec, string key, int def)
        {
            int v;
            if (int.TryParse(ini.Get(sec, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private static float ReadFloat(IniFile ini, string sec, string key, float def)
        {
            float v;
            if (float.TryParse(ini.Get(sec, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private static double ReadDouble(IniFile ini, string sec, string key, double def)
        {
            double v;
            if (double.TryParse(ini.Get(sec, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private static string ReadColor(IniFile ini, string key, string fallback)
        {
            return ThemeTokens.NormalizeHex(ini.Get("Appearance", key, fallback), fallback);
        }

        private static bool ReadBool(IniFile ini, string sec, string key, bool def)
        {
            string s = ini.Get(sec, key, "").Trim();
            if (s.Length == 0) return def;
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "off", StringComparison.OrdinalIgnoreCase))
                return false;
            return def;
        }

        private static T ReadEnum<T>(IniFile ini, string sec, string key, T def) where T : struct
        {
            string s = ini.Get(sec, key, "").Trim();
            if (s.Length == 0) return def;
            T v;
            if (Enum.TryParse<T>(s, true, out v)) return v;
            return def;
        }

        /// <summary>把 "15:00,10:00,05:00" 或 "900,600,300" 解析成秒。</summary>
        public static int[] ParseTimes(string text, int[] def)
        {
            if (text == null) return def == null ? new int[0] : def;
            text = text.Trim();
            if (text.Length == 0) return new int[0];

            List<int> list = new List<int>();
            string[] parts = text.Split(',', ';', '，', '；');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                int sec = ParseOneTime(p);
                if (sec > 0) list.Add(sec);
            }
            return list.ToArray();
        }

        private static int ParseOneTime(string p)
        {
            int colon = p.IndexOf(':');
            if (colon > 0)
            {
                int mm, ss;
                if (int.TryParse(p.Substring(0, colon).Trim(), out mm) &&
                    int.TryParse(p.Substring(colon + 1).Trim(), out ss))
                    return mm * 60 + ss;
                return 0;
            }
            int v;
            if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        public static string TimesToText(int[] times)
        {
            if (times == null || times.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < times.Length; i++)
            {
                if (i > 0) sb.Append(',');
                int t = times[i];
                if (t >= 60 && t % 60 == 0) sb.Append(t / 60).Append(":00");
                else if (t >= 60) sb.Append((t / 60).ToString("00")).Append(':').Append((t % 60).ToString("00"));
                else sb.Append(t);
            }
            return sb.ToString();
        }

        public static string ColorToText(Color c)
        {
            return c.R.ToString(CultureInfo.InvariantCulture) + "," +
                   c.G.ToString(CultureInfo.InvariantCulture) + "," +
                   c.B.ToString(CultureInfo.InvariantCulture);
        }

        public static Color ParseColor(string s, Color def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            string[] parts = s.Split(',');
            if (parts.Length >= 3)
            {
                int r, g, b;
                if (int.TryParse(parts[0].Trim(), out r) &&
                    int.TryParse(parts[1].Trim(), out g) &&
                    int.TryParse(parts[2].Trim(), out b))
                    return Color.FromArgb(Clamp255(r), Clamp255(g), Clamp255(b));
            }
            try
            {
                Color named = Color.FromName(s.Trim());
                if (named.A != 0 || named.R != 0 || named.G != 0 || named.B != 0) return named;
            }
            catch { }
            return def;
        }

        private static int Clamp255(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        #endregion
    }

    #endregion
}












