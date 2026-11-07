using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 屏幕 OCR 播报识别 + 多区域按需扫描。
    ///
    /// 两类区域，行为完全不同：
    ///   · Poll = true 的区域（默认只有“左下角播报”）按 OcrPollInterval 定时轮询；
    ///   · 其它区域一次都不轮询，只在按“立即扫描”快捷键时各截一次。
    ///
    /// 每一轮扫描的流程（每一步都是为了“不影响游戏帧率”）：
    ///   1. 截一小块画面，顺手把它二值化成黑白图；
    ///   2. 数高亮（白 / 浅蓝）像素，少于 200 个直接返回，连 OCR 引擎都不碰；
    ///   3. 真的够亮才交给 Tesseract，500ms 硬超时，超时就丢；
    ///   4. 调过一次就强制冷静 3000ms；同一段文字挂着也不会重复处理；
    ///   5. 识别结果分两路走：
    ///        · 播报关键词（核弹 / 净化 / 阵亡）→ 现有的 AlertManager + 悬浮窗；
    ///        · 战术数据（谁被谁打死了、哪一方刷新增援、血量、发电机）→ 自动填写战术面板。
    ///
    /// OCR 关掉或认错了，战术面板照样能完全独立地手动用。
    /// </summary>
    internal class OcrAlertWatcher : IDisposable
    {
        /// <summary>高亮像素少于这个数就完全不调用 OCR。</summary>
        public const int BrightPixelThreshold = 200;

        /// <summary>一次成功识别之后的强制冷静期（毫秒）。</summary>
        public const int CooldownMs = 3000;

        /// <summary>
        /// 当前强度下真正生效的冷静期。
        ///
        /// 【口径（操作者要求）】开到「最大」时不再强制冷静 —— 节奏完全交给
        /// 设置里的"轮询间隔"（最小 300 毫秒）：他要的是"宁可多扫也别漏播报"，
        /// 而且明确说了这点占用对他不算什么。其它档位保持原来的 3000 毫秒，
        /// 免得默认配置下白白多跑 OCR。
        /// </summary>
        private static int CooldownForStrength(int s)
        {
            return s >= FineMaskStrength ? 0 : CooldownMs;
        }

        /// <summary>同一段文字在这段时间内不重复处理（毫秒）。</summary>
        public const int SameTextWindowMs = 60000;

        // ---- 播报关键词（识别出的文本先转小写、去掉所有非字母数字汉字，再丢进来）----
        // 允许错别字：把容易认错的字母写成字符组，中间再放开 0~3 个任意字符，
        // 这样 warhead / warhcad / warhad / warh 都能认出来。
        //
        // ⚠ 【操作者给的原话】核弹那条播报里根本没有"核弹"两个字，它是
        //    「本设施的地下区域将在倒计时: 98 秒后自毁」—— 所以只认 warhead/核/弹
        //    的话整条都认不出来（提示当然不会弹）。这里把"自毁"也收进来。
        //
        // ⚠ 另外 "alpha" 会把 MTF 的部队代号 ALPHA-67 / ALPHA-87 也算成核弹，
        //    于是"SCP-049 已被 ALPHA-87 成功收容"整条被当成"提到核弹"吞掉 ——
        //    击杀既没提示也没记账（日志实锤）。所以 alpha / 阿尔法 后面紧跟编号
        //    （-67 这种小队代号）时不算核弹。
        private static readonly Regex ReNuke = new Regex(
            "w[ao4]rh[a-z0-9]{0,3}|nu[ck]l[ea]ar|"
          + "a[l1][pf]h[ao](?!\\s*[-–—－]?\\s*\\d)|"
          + "阿尔法(?!\\s*[-–—－]?\\s*\\d)|核|弹|自毁|自爆",
            RegexOptions.CultureInvariant);

        // 净化相关的关键词。"封锁"也算 —— 实测"轻收容区域已封锁"就是净化开始的播报。
        private static readonly Regex ReDecon = new Regex(
            "d[e3]c[o0]nt?a?m|d[e3]c[o0]n|净化|轻收容|輕收容|封锁|封鎖|lockdown",
            RegexOptions.CultureInvariant);

        private static readonly Regex ReScp = new Regex(
            "scp|5cp", RegexOptions.CultureInvariant);

        // ---- 需求四：核弹播报里的秒数 ----
        // "T-80 seconds" / "T minus 90" / "80秒" / "还剩 80 秒"
        private static readonly Regex ReNukeSeconds = new Regex(
            "t\\s*[-–—]?\\s*(?:minus\\s*)?(\\d{1,3})"
          + "|(\\d{1,3})\\s*(?:秒|s\\b|seconds?|sec\\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 兜底：OCR 有时把 80 认成 8O，或者把「秒」整个漏掉。
        // 只在已经确认是核弹播报、正常规则又没读到秒数时用。
        private static readonly Regex ReNukeSecondsLoose = new Regex(
            "(?:倒计时|倒數|自毁|自毀|引爆)[^0-9oils]{0,8}([0-9oils]{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 只有"明确的引爆倒计时"才算核弹播报。
        //
        // 【实测踩过的坑】以前只要出现 warhead 就算，于是 C.A.S.S.I.E. 那些
        // 「CHECKPOINT WARHEAD」「TESLA WARHEAD」之类的播报（只是提到核弹/地点）
        // 每读到一次就把倒计时按配置时长重新起算一遍 —— 屏幕上就是
        // 秒数反复跳回 90 秒。现在的规则（操作者要求）：
        //   必须同时出现"自毁/引爆/爆炸/倒计时/detonat/T-minus"这类倒计时字样，
        //   而且必须读到具体秒数，才认。
        private static readonly Regex ReNukeCountdown = new Regex(
            "自毁|引爆|爆炸|倒计时|倒數|detonat|detonate|destroy|explod|t\\s*[-–—]?\\s*minus|tminus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 核弹取消播报：「引爆程序已被取消」。
        // 这句在游戏里出现时核弹已经停了，但句子里**没有秒数**。
        // 如果不单独认，它会掉进“提到核弹但没读到秒数”的分支 —— 只加快扫描 10 秒，
        // 屏幕上的倒计时却继续往下走，最后还会自己报“核弹已引爆”，完全反了。
        private static readonly Regex ReNukeCancel = new Regex(
            "引爆程序已被取消|引爆程序已取消|引爆程序被取消|"
          + "自毁程序已被取消|自毁程序已取消|"
          + "核弹[^。]{0,8}(?:已被|已经|被)?取消|取消[^。]{0,6}(?:引爆|自毁|核弹)|"
          + "detonation[^.]{0,20}cancelled|detonation[^.]{0,20}canceled|"
          + "warhead[^.]{0,20}cancelled|warhead[^.]{0,20}canceled",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 「核弹重新启动，倒计时 90 秒」。取消后重启和第一次启用都走这条。
        private static readonly Regex ReNukeRestart = new Regex(
            "重新启动|重新啟動|重启|重啟|重新开始|重新開始|restart|reactivat",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "开始清除所有生物" = 轻收容已经过了很多分钟、真的开始锁区域清生物了。
        // 识别到它就把净化倒计时归零并锁死（后续不再更改）。
        //
        // 【为什么要加"生物…清除"这条】实测这句话的语序不止一种
        //（"开始清除所有生物" / "轻收容区域内的所有生物将被清除"）。
        // ⚠ 但不能把"所有生物必须立即转移，以避免被清除"这种预警也算进来：
        // 那句里"生物"到"清除"中间隔着 10 个字，所以这里卡死 8 字以内 ——
        // 预警句够不着，"将被清除"够得着。
        //
        // 【操作者补充】开始净化时还有一句「所有人员应该立即转移」，也算开始。
        //
        // ⚠ 【操作者纠正】「所有生物必须立即转移」/「所有人员应该立即转移」是
        // 开始计时的标志（净化倒计时从这一刻起算），不是结束 ——
        // 结束（归零并锁死）只有"开始清除所有生物"那一类。所以这两句分开处理：
        // 归零看 ReDeconPurge，开始计时看 ReDeconStart。
        private static readonly Regex ReDeconPurge = new Regex(
            "清除所有生物|清除生物|清理所有生物|清理生物|生物清除|"
          + "开始清除[^，。；,;]{0,6}(?:生物|人员|玩家)|"
          + "开始清理[^，。；,;]{0,6}(?:生物|人员|玩家)|"
          + "清除程序|清理程序|purge|terminate all|"
          + "(?:所有|全部)?生物[^，。；,;]{0,8}清除",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 净化相关播报的整段长度上限。真播报只有十几二十个字；
        /// 超过它的一律当成"把一整段长文字误读成播报"（聊天记录、说明文字…）而忽略。
        /// </summary>
        private const int DeconTextMaxLen = 70;

        /// <summary>
        /// 终焉协议（亡者协议）的播报（操作者要求：识别"终"就差不多，用于音频条件）。
        ///
        /// 游戏里那句大概是「终焉协议已启动 / O5 议会已授权终焉协议」，
        /// OCR 经常只认到"终"。所以这里：
        ///   · 认 终焉 / 亡者协议 / 死手 / dead man / deadman；
        ///   · 也认"终"后面跟着 协议 / 启动 / 开始 / 授权 的写法（丢掉"焉"也能中）。
        /// ⚠ 不认光秃秃一个"终"（"最终""终止"会把音频和倒计时带偏）。
        /// </summary>
        private static readonly Regex ReDeadMan = new Regex(
            "终焉|亡者协议|死手|dead\\s*man|deadman|"
          + "(?<![最临端])终(?![端止])[焉末]?[^，。；,;]{0,4}(?:协议|启动|开始|授权|执行)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>这段文字像不像"终焉协议"的播报（给测试和日志用）。</summary>
        public static bool LooksLikeDeadManBroadcast(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string flat = Normalize(raw);
            if (flat.Length == 0) return false;
            return ReDeadMan.IsMatch(flat);
        }

        /// <summary>
        /// 本程序自己的亡者协议悬浮窗文字。如果截图排除失败（或用户关了
        /// HideFromCapture），OCR 会把自己的「亡者协议 T-02:30」读回来，
        /// 看起来就像终焉播报反复出现 —— 必须排除，否则误识别会自我维持。
        /// </summary>
        private static readonly Regex ReOwnDeadManOverlay = new Regex(
            "亡者协议t\\d|亡者协议倒计时|核弹另计",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 这段文字像不像"开始清除生物"（净化归零）。
        ///
        /// 给测试用的公开入口：长度闸 + 关键词两条一起判。
        /// 只认"开始清除/清理 + 生物/人员/玩家"这种紧凑写法，
        /// 不会因为一段长文里凑巧出现"开始清除"就误判
        ///（操作者日志实锤：`也 在 开始 清除 所 a ez 我 觉得 可 以 给 7B,…` 误触发了归零，
        /// 把那一局的净化倒计时锁死，通知里只剩"轻收容净化已开始"没有时间）。
        /// </summary>
        public static bool LooksLikeDeconPurge(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string flat = Normalize(raw);
            if (flat.Length == 0 || flat.Length > DeconTextMaxLen) return false;
            return ReDeconPurge.IsMatch(flat);
        }

        /// <summary>
        /// "净化开始计时"的播报：「所有生物必须立即转移」/「所有人员应该立即转移」。
        /// 认出它就从这一刻按基础时长开始走倒计时（操作者口径）。
        /// </summary>
        private static readonly Regex ReDeconStart = new Regex(
            "(?:所有)?(?:生物|人员|玩家)[^，。；,;]{0,10}(?:必须|应该|需要|请)[^，。；,;]{0,6}"
          + "(?:立即|马上|尽快|迅速)?[^，。；,;]{0,4}转移",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---- 需求五：净化播报里的时间 ----
        // "15分钟" / "15 minutes" / "03:20" / "200秒"
        private static readonly Regex ReDeconClock = new Regex(
            "(?:轻\\s*收\\s*容|輕\\s*收\\s*容|净\\s*化|淨\\s*化|decontam|decon)"
          + "[^0-9]{0,20}(\\d{1,2})\\s*[:：]\\s*(\\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ReDeconMinutes = new Regex(
            "(?:轻\\s*收\\s*容|輕\\s*收\\s*容|净\\s*化|淨\\s*化|decontam|decon)"
          + "[^0-9]{0,20}(\\d{1,2})\\s*(?:分钟|分|minutes?|mins?|m\\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ReDeconPlainSeconds = new Regex(
            "(?:轻\\s*收\\s*容|輕\\s*收\\s*容|净\\s*化|淨\\s*化|decontam|decon)"
          + "[^0-9]{0,20}(\\d{2,4})\\s*(?:秒|s\\b|seconds?|sec\\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 什么算"SCP 死了"。故意把击杀动词都收进来：
        // 以前的列表只有 contain/terminat/阵亡/收容，
        // 于是 "某某 击杀了 SCP-173" 和 "SCP-173 killed by MTF" 这类播报
        // 根本进不了这条规则 —— 中文服最常见的击杀播报直接漏掉了。
        // （就算误进这条规则也没关系：下面还要求认出击杀者，认不出会被忽略。）
        private static readonly Regex ReScpDead = new Regex(
            "contain|terminat|eliminat|neutraliz|execut|kill|dead|"
          + "阵亡|收容|击杀|杀死|击毙|消灭|死亡|已死",
            RegexOptions.CultureInvariant);

        private readonly Config _cfg;
        private readonly AlertManager _mgr;
        private readonly Action<Action> _runOnUi;
        private readonly OcrTacticalLink _link;
        private readonly System.Threading.Timer _timer;
        private readonly object _gate = new object();

        /// <summary>按尺寸缓存的截图缓冲。多区域之间来回切也不用每次 new 位图。</summary>
        private readonly Dictionary<int, Buf> _bufs = new Dictionary<int, Buf>();

        private int _busy;
        private DateTime _cooldownUntil = DateTime.MinValue;
        private string _lastText = "";
        private DateTime _lastTextAt = DateTime.MinValue;
        private bool _started;
        private bool _disposed;

        // ---- 终焉播报的"多次确认" ----
        // 单帧 OCR 误识别（聊天、界面文字里凑巧出现"终焉/终末…协议"）不许直接触发；
        // 必须在短时间内连续看到多次才认。真实 CASSIE 那句会重复显示好几秒。
        private DateTime _deadManCandidateAt = DateTime.MinValue;
        private DateTime _deadManFiredAt = DateTime.MinValue;
        private int _deadManCandidateHits;
        private const int DeadManConfirmHits = 3;
        private const double DeadManConfirmWindowSeconds = 12.0;

        /// <summary>诊断信息，给设置窗口看。</summary>
        public string LastInfo = "还没开始扫描";

        /// <summary>
        /// 最近一次"立即扫描"的结果梗概（设置窗口上的按钮用它显示）。
        /// 没有任何结果时是"扫描失败：…"，方便用户判断是区域没对准还是别的问题。
        /// </summary>
        public string LastScanSummary = "";

        /// <summary>最近一次"立即扫描"的完整报告（每块区域一行 + 没扫出来的清单）。</summary>
        public string LastScanReport = "";

        /// <summary>一块可以反复用的截图缓冲。</summary>
        /// <summary>最近一次截图的原始画面（浅字深底），灰度重试时要用。</summary>
        private Bitmap _lastShot;

        private class Buf
        {
            public Bitmap Shot;
            public Bitmap Bin;
            public Graphics G;

            public void Release()
            {
                if (G != null) { try { G.Dispose(); } catch { } G = null; }
                if (Shot != null) { try { Shot.Dispose(); } catch { } Shot = null; }
                if (Bin != null) { try { Bin.Dispose(); } catch { } Bin = null; }
            }
        }

        /// <summary>一次区域扫描的结局，主要给日志用。</summary>
        private class ScanResult
        {
            public bool DidOcr;
            public bool SilenceScanned;
            public bool SawSilence;
            public string Text = "";
            public string Note = "";
        }

        public OcrAlertWatcher(Config cfg, AlertManager mgr, Action<Action> runOnUi)
        {
            _cfg = cfg;
            _mgr = mgr;
            _runOnUi = runOnUi;
            _link = new OcrTacticalLink(cfg, mgr);
            _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// 语言文件目录：程序目录下的 tessdata（快速版）或 tessdata_best（最高精度版）。
        /// 可用环境变量 SCPSLALERT_TESSDATA 覆盖，方便排查。
        /// </summary>
        public static string TessdataPath()
        {
            return TessdataPath(false);
        }

        public static string TessdataPath(bool best)
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("SCPSLALERT_TESSDATA");
                if (!string.IsNullOrEmpty(env)) return env;
            }
            catch { }
            return System.IO.Path.Combine(Application.StartupPath, best ? "tessdata_best" : "tessdata");
        }

        /// <summary>按当前配置把目录 / 强度 / 模型精度同步给引擎。</summary>
        private void SyncEngineSettings()
        {
            bool best = _cfg.OcrBestModel;

            // 最高精度版目录不存在（用户没放文件）就悄悄退回快速版，别把识别搞挂
            string dir = TessdataPath(best);
            if (best && !System.IO.Directory.Exists(dir))
            {
                Logger.On("OCR", "没找到 tessdata_best 目录，仍用快速版（把最高精度版的 "
                    + "chi_sim.traineddata 放进 tessdata_best\\ 就能生效）");
                best = false;
                dir = TessdataPath(false);
            }

            Strength = _cfg.OcrStrength;

            // 识别超时按强度给：最高精度模型 + 整块播报区本来就慢，
            // 500ms 会把"其实能认出来"的那几帧全丢掉（还连带重建引擎）。
            OcrEngine.TimeoutMs = _cfg.OcrStrength >= FineMaskStrength ? 2000 : 800;

            // 语言 / 精度变了：tesseract 的引擎建好之后改不了语言，只能整个重建。
            // 判据用 ReadyLang（Reset 时会被清成空串），所以"还没建过引擎"时
            // 两个条件都是 false —— 启动阶段不会白白重置一次。
            bool langChanged = !string.IsNullOrEmpty(OcrEngine.ReadyLang)
                            && OcrEngine.ReadyLang != _cfg.OcrLang;

            if (langChanged) OcrEngine.Reset();
            OcrEngine.BestModel = best;
            _engineDir = dir;
        }

        private string _engineDir;

        /// <summary>按配置启动（EnableOcr = false 时彻底不启动，连引擎都不初始化）。</summary>
        public void Start()
        {
            if (_disposed) return;
            _started = true;
            ApplyConfig();
        }

        /// <summary>配置改了之后重新读一遍（开关 / 间隔 / 区域列表）。</summary>
        public void ApplyConfig()
        {
            if (_disposed) return;

            SyncEngineSettings();

            if (!_started || !_cfg.OcrAllowed)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                LastInfo = _cfg.OcrAllowed ? "已停止" : "已关闭（可在设置里开启）";
                return;
            }

            // 【操作者要求】OCR 进入服务器后才开始运作：
            // 桌面 / 主菜单 / 还没进服时完全不截图、不调引擎（定时器直接停），
            // 进服事件（AlertManager.ServerStateChanged）来了 Program 会再调一次这里。
            if (_cfg.OcrOnlyInServer && !_mgr.InServer)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                LastInfo = "等待进入服务器（已开启「进入服务器后才开始扫描」）";
                return;
            }

            int polled = PolledRegions().Count;
            if (polled == 0)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                LastInfo = "没有需要定时轮询的区域（只按“立即扫描”工作）";
            }
            else
            {
                int ms = ClampInterval(_cfg.OcrPollInterval);
                _timer.Change(ms, ms);
                LastInfo = "定时轮询中，间隔 " + ms + " 毫秒，共 " + polled + " 个区域";
            }

            // 引擎在后台预热；初始化很慢，这里绝不阻塞。
            if (OcrEngine.IsReady) LastInfo += "；引擎就绪（" + OcrEngine.Status + "）";
            else
            {
                LastInfo += "；引擎初始化中…";
                OcrEngine.EnsureReady(_engineDir != null ? _engineDir : TessdataPath(), _cfg.OcrLang);
            }
        }

        // ============================================================
        // 分阶段扫描（需求六）
        //
        // 准备阶段（等待分配角色）时，游戏还没正式开始，掉帧无所谓，
        // 所以把间隔压到 100ms 并只扫"角色"那一块，第一时间认出角色；
        // 认出角色（或 90 秒超时）之后立刻降回正常间隔。
        //
        // 为什么要限制成"只扫角色区"：100ms 一轮如果连播报区一起扫，
        // OCR 引擎（500ms 超时 + 引擎锁）根本跟不上，反而拖慢整条链路。
        // ============================================================

        /// <summary>角色快速扫描用的间隔（毫秒）。第 4 批后不再使用，保留常量兼容旧测试。</summary>
        public const int FastScanMs = 100;

        /// <summary>第 4 批：屏幕中心角色 OCR 已删除，准备阶段不再切快速扫描。</summary>
        public bool PrepFastScan
        {
            get { return false; }
        }

        /// <summary>第 4 批：空实现，保留调用点避免旧代码炸（角色改由阵营识别负责）。</summary>
        public void SetPrepScan(bool on)
        {
            // 屏幕中心角色框已删除：准备阶段不再需要 100ms 快速扫。
        }

        /// <summary>所有"角色"类型的区域。第 4 批已删除该功能，恒返回空表。</summary>
        private List<OcrRegion> RoleRegions()
        {
            return new List<OcrRegion>();
        }

        // ============================================================
        // 核弹"已启用"之后的一小段监视（操作者要求）
        //
        // 核弹有两段播报：
        //   第一段只说"核弹已启用"（不带时间）→ 不弹窗，但把播报区扫快一点；
        //   第二段给出倒计时时间 → 按它倒计时，监视结束。
        // 监视最多 10 秒，而且只加快"播报类"区域（整屏拉高 OCR 引擎跟不上）。
        // ============================================================

        /// <summary>核弹监视期间的扫描间隔（毫秒）。</summary>
        public const int NukeWatchMs = 400;

        /// <summary>核弹监视最多持续多少秒。</summary>
        public const int NukeWatchSeconds = 10;

        private volatile bool _nukeWatch;
        private DateTime _nukeWatchUntil = DateTime.MinValue;

        public bool NukeWatch
        {
            get { return _nukeWatch; }
        }

        /// <summary>核弹已启用 → 最多盯 10 秒，快点抓第二段给的时间。</summary>
        public void StartNukeWatch()
        {
            if (_disposed) return;
            if (_nukeWatch) return;                     // 已经在盯了，别无限续期
            _nukeWatch = true;
            _nukeWatchUntil = DateTime.Now.AddSeconds(NukeWatchSeconds);

            if (!_started || !_cfg.OcrAllowed) return;
            _timer.Change(NukeWatchMs, NukeWatchMs);
            Logger.On("OCR", "核弹已启用 → 播报区改用 " + NukeWatchMs + "ms 扫描，最多盯 "
                + NukeWatchSeconds + " 秒（等第二段给出倒计时时间）");
        }

        /// <summary>结束核弹监视（拿到时间了 / 盯满 10 秒）。</summary>
        public void StopNukeWatch(string why)
        {
            if (!_nukeWatch) return;
            _nukeWatch = false;
            Logger.On("OCR", "核弹监视结束（" + why + "）→ 扫描频率降回正常");

            if (!_started || !_cfg.OcrAllowed) return;
            int ms = ClampInterval(_cfg.OcrPollInterval);
            if (PolledRegions().Count == 0) _timer.Change(Timeout.Infinite, Timeout.Infinite);
            else _timer.Change(ms, ms);
        }

        /// <summary>"播报类"区域（核弹播报出现在左下角那块）。</summary>
        private List<OcrRegion> BroadcastRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            for (int i = 0; i < _cfg.OcrRegions.Count && i < 12; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r == null || !r.Enabled) continue;
                if (r.Kind == OcrRegionKind.Broadcast || r.Kind == OcrRegionKind.Generic)
                    list.Add(r);
            }
            return list;
        }

        /// <summary>第 4 批：屏幕中心角色 OCR 已删除，这两个事件保留签名但不再触发。</summary>
        public Action<string> OnRoleRecognized;

        /// <summary>第 4 批：不再触发（观察者状态改由日志事件提供）。</summary>
        public Action OnObserverSeen;

        // ============================================================
        // 死亡界面的票数 / 影响力扫描（需求八）
        //
        // 玩家一死，游戏会显示结算界面，上方左右各一组数字：
        //   左侧 = MTF：大数字是可用重生令牌，小数字是影响力 / 下一阈值（如 49/80）
        //   右侧 = 混沌：同上
        // 这组框平时不扫，只在死亡期间启用；确认复活或回合结束就停。
        // ============================================================

        private volatile bool _deathScan;

        public bool DeathScan
        {
            get { return _deathScan; }
        }

        /// <summary>玩家死了 → 开始扫票数；复活 / 回合结束 → 停。</summary>
        public void SetDeathScan(bool on)
        {
            if (_disposed) return;
            if (_deathScan == on) return;
            _deathScan = on;
            Logger.On("OCR", on ? "玩家死亡 → 开始识别上方票数 / 影响力（4 个框）"
                                : "死亡扫描结束（复活或回合结束）→ 停掉票数框");
        }

        /// <summary>
        /// 活着时只按间隔扫描影响力（小票）；令牌（大票）区域仅在死亡期间扫描。
        /// </summary>
        public int AliveTicketSeconds = 6;
        private DateTime _aliveTicketAt = DateTime.MinValue;

        public bool AliveTicketDue
        {
            get
            {
                if (_deathScan) return false;      // 死了有专门那套
                if ((DateTime.Now - _aliveTicketAt).TotalSeconds < AliveTicketSeconds) return false;
                _aliveTicketAt = DateTime.Now;
                return true;
            }
        }

        /// <summary>票数区域。死亡时读全部；存活时只读影响力区域。</summary>
        private List<OcrRegion> TicketRegions(bool includeTokens)
        {
            List<OcrRegion> list = new List<OcrRegion>();
            for (int i = 0; i < _cfg.OcrRegions.Count && i < 12; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r != null && r.Enabled && r.Kind == OcrRegionKind.Ticket
                    && (includeTokens || r.Tag == null
                        || !r.Tag.EndsWith("_tokens", StringComparison.OrdinalIgnoreCase))) list.Add(r);
            }
            return list;
        }

        /// <summary>
        /// 这些区域是"彩色/亮字压深底"，不能用给白底黑字设计的
        /// 白像素预检，也不该只走一条亮度阈值老路。
        /// </summary>
        private static bool IsColorishRegion(OcrRegion r)
        {
            if (r == null) return false;
            return r.Kind == OcrRegionKind.Ticket
                || r.Kind == OcrRegionKind.Silence
                || r.Kind == OcrRegionKind.ScpHp;   // SCP 血量表：服务器配色各不相同
        }

        /// <summary>所有启用中的区域（"扫描全部"用）。</summary>
        private List<OcrRegion> AllEnabledRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            for (int i = 0; i < _cfg.OcrRegions.Count && i < 12; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r != null && r.Enabled) list.Add(r);
            }
            return list;
        }

        /// <summary>死亡期间要扫的所有框：票数 / 静音开关 / 左侧 SCP 列表。</summary>
        private List<OcrRegion> DeathScanRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            for (int i = 0; i < _cfg.OcrRegions.Count && i < 12; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r == null || !r.Enabled) continue;
                if (r.Kind == OcrRegionKind.Ticket || r.Kind == OcrRegionKind.Silence
                    || r.Kind == OcrRegionKind.ScpList) list.Add(r);
            }
            return list;
        }

        /// <summary>给"立即扫描"的梗概补充面板数据（票数 / SCP 在场），由 Program 接。</summary>
        public Func<string> _onScanExtra;

        /// <summary>静音开关扫描结果（true = 还看得见静音按钮 = 还没复活）。</summary>
        public Action<bool> OnSilenceScan;

        /// <summary>死亡界面票数框这一轮有没有识别到有效数字。</summary>
        public Action<bool> OnTicketScan;

        /// <summary>
        /// 死亡界面这一轮什么都没读到（票数框 + 静音开关全空）→ 通知外面。
        /// 复活之后那块界面就消失了，OCR 一个数字都读不出来；这条信号
        /// 以前是完全丢掉的（`flat.Length == 0` 直接 return），
        /// 于是"死亡扫描"无法获得完整空白帧信号。
        /// </summary>
        public Action OnDeathScreenEmpty;

        /// <summary>这一轮死亡界面扫描有没有读到任何东西。</summary>
        private bool _deathRoundHit;
        private bool _deathTicketRoundHit;

        /// <summary>连续多少次截到的画面是"全黑"（判"录制被挡住"用）。</summary>
        private int _blackShots;

        /// <summary>左侧 SCP 列表里认出来的编号（逗号分隔）。</summary>
        public Action<string> OnScpListScan;

        // ---- SCP 列表持续监视（操作者要求）----
        // 进服后第一次死亡 / 手动"全部扫描"时由 AlertManager 打开；
        // 打开期间每次轮询都会去扫左侧 SCP 列表，直到下次进服重新武装。
        private volatile bool _scpListWatch;
        private DateTime _scpListNext = DateTime.MinValue;

        /// <summary>持续扫描左侧 SCP 列表（true = 开）。</summary>
        public bool ScpListWatch
        {
            get { return _scpListWatch; }
            set { _scpListWatch = value; if (value) _scpListNext = DateTime.MinValue; }
        }

        /// <summary>配置里启用的 SCP 列表区域。</summary>
        private List<OcrRegion> ScpListRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            if (_cfg.OcrRegions == null) return list;
            for (int i = 0; i < _cfg.OcrRegions.Count; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r != null && r.Enabled && r.Kind == OcrRegionKind.ScpList) list.Add(r);
            }
            return list;
        }

        /// <summary>立刻扫一次 SCP 列表（不等下一轮轮询）。</summary>
        public void ScanScpListNow()
        {
            List<OcrRegion> list = ScpListRegions();
            if (list.Count == 0)
            {
                Logger.On("扫描", "没有启用「左侧 SCP 列表」区域，SCP 列表扫描没有可扫的地方（到 设置 → OCR 区域列表 里加一个）");
                return;
            }
            if (_disposed || !_cfg.OcrAllowed) return;

            // 【丢后台线程】识别一次要截图 + 跑 OCR（几百毫秒），
            // 这个方法是从 UI 线程（AlertManager 判定首次死亡 / 设置页按钮）调过来的，
            // 同步跑会把主界面和游戏提示卡一下 —— 和「立即扫描」一样走 Task.Run，
            // 并且共用一个 _busy 互锁，绝不和定时轮询叠在一起跑。
            Task.Run(new Action(delegate
            {
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
                try
                {
                    OcrEngine.EnsureReady(_engineDir != null ? _engineDir : TessdataPath(), _cfg.OcrLang);
                    if (!OcrEngine.IsReady) return;
                    ScanRegion(list[0], false, false, true);
                }
                catch (Exception ex) { Logger.Err("扫描", "立即扫描 SCP 列表失败", ex); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            }));
        }

        /// <summary>票数变化时通知外面（Program 接到 AlertManager，弹提示 + 写面板）。</summary>
        /// <summary>
        /// 票数/影响力识别到值：(tag, 文本, 是不是"死亡界面的权威读数")。
        /// 【第三个参数现在只用来记日志】活着的时候也会隔几秒扫一趟那几个框
        /// （屏幕上方那条重生条本来就一直在），以前那种"活着的读数绝不写进模型"
        /// 已经按操作者要求去掉了 —— 他一直是常开扫描的，屏幕上读到了面板就该跟着动。
        /// 防误读改由 AlertManager 那边的三道闸把关：干净数字、
        /// 令牌不可能超过 MaxTokensOnScreen、同一个值要连续几次一样。
        /// </summary>
        public Action<string, string, bool> OnTicketRecognized;   // (tag, 文本, 权威?)

        /// <summary>
        /// 把票数框识别出来的原始文字洗成能看的数字串。
        /// 大数字只可能是 0-9 一位；小数字是 "49/80" 这种，也可能是 "49"。
        /// </summary>
        /// <summary>
        /// 激进的"亮字识别"（票数 / 影响力用）。
        ///
        /// 【为什么需要】默认那条路是给"白底黑字"设计的：先按亮度硬阈值二值化。
        /// 但 HUD 上的票数是亮的彩色数字（蓝色 / 绿色）压在深色底上，
        /// 按"黑的才是字"的思路处理，数字会整片被当成背景丢掉 —— 这就是认不出来的原因。
        ///
        /// 【现在的做法】先走"颜色掩膜"这条新路（见 TicketMaskText）：
        /// 按色相把蓝字 / 绿字抠出来，归一化大小之后用"整行"模式识别，
        /// 又快又准；实在认不出数字，再退回下面这套老的亮度阈值大扫。
        /// </summary>
        private string AggressiveBrightText(OcrRegion region, Bitmap shot, out float bestConf)
        {
            bestConf = 0f;

            // 只有"最大"档才跑那套精细的颜色掩膜（见 UseFineTicketMask 的说明）。
            // 低档位保持原来的行为不变：亮度阈值大扫。
            if (!UseFineTicketMask) return LegacyBrightText(shot, out bestConf);

            string colorText = TicketMaskText(region, shot, out bestConf);
            if (IsGoodTicketText(region, colorText, bestConf)) return colorText;

            float legacyConf;
            string legacy = LegacyBrightText(shot, out legacyConf);
            if (string.IsNullOrEmpty(colorText))
            {
                bestConf = legacyConf;
                return legacy;
            }
            if (string.IsNullOrEmpty(legacy)) return colorText;

            // 两条路都认出了点东西：比"最好的一段"谁更像票数。
            // 首选路（色相掩膜 + Otsu）带一点偏向：它跟老办法打平时优先用它 ——
            // 老办法按亮度抠，容易把左边的高亮块一起认进来（"<3 37/40" 这种）。
            int a = TicketShapeScore(region, colorText, bestConf, TicketLaneBonus);
            int b = TicketShapeScore(region, legacy, legacyConf);
            if (a >= b) return colorText;
            bestConf = legacyConf;
            return legacy;
        }

        /// <summary>
        /// SCP 血量表专用：几条"让字看清楚"的路各认一遍，
        /// 返回"能解析出最多条 SCP 血量"的那次原文。
        ///
        /// 【为什么不能只用一条路】操作者说得很清楚：不同服务器配色不一样，
        ///   让文字看清楚最重要，怎么认清楚怎么来。
        /// 实测这块字只有约 11px、还带服务器自己的色调（绿底白字 / 粉字 / 黄字），
        /// 单一路径都不稳。所以这里把几种办法都试一遍：
        ///   ① 原图（白字深底，Tesseract 自己算 Otsu 往往最稳）
        ///   ② 灰度反色 + 放大 2 倍（浅字深底最有效的一条）
        ///   ③ 颜色掩膜（把亮且有颜色倾向的字抠出来，压掉服务器的背景色偏）
        /// 都用"整块文字"模式（psm 6），因为这是一张清单。
        ///
        /// 【评分标准】不看长度、不看置信度，只看 有几行能解析成
        /// "某个 SCP 的血量"（用 OcrTacticalLink.TryParseScpHpLine 判定）。
        /// 一块都没解析出来时，退化成"原文里出现了几个 SCP 编号"当分数。
        /// </summary>
        private string PickBestHpText(OcrRegion region, Bitmap shot, string first, out float bestConf)
        {
            bestConf = 0f;
            string best = first;
            int bestScore = HpTextScore(first);
            if (shot == null || IsAllHpParsed(first)) return best;   // 已经够好，别多花时间

            // ---- ② 灰度反色 + 放大 2 倍 ----
            Bitmap gray = null, up = null;
            try
            {
                gray = GrayInvert(shot);
                up = ScaleUp(gray, 2);
                float c;
                string t = OcrEngine.RecognizePage(up, 6, out c);
                int s = HpTextScore(t);
                if (s > bestScore)
                {
                    bestScore = s; best = t; bestConf = c;
                    Logger.On("OCR", region.Name + "：灰度反色+2倍 认出的更多（" + s + " 条）");
                }
            }
            catch (Exception ex) { Logger.Err("OCR", "血量表灰度路失败（忽略）", ex); }
            finally
            {
                if (up != null && up != gray) up.Dispose();
                if (gray != null) gray.Dispose();
            }

            // ---- ③ 颜色掩膜：把"亮且有颜色倾向"的字抠出来 ----
            // 这一步是为了适配"不同服务器配色不一样"：即使底色被染成绿/粉，
            // 文字本身通常更亮、更饱和，掩膜能把背景压掉。
            Bitmap masked = null, mup = null;
            try
            {
                masked = HpColorMask(shot);
                if (masked != null)
                {
                    mup = ScaleUp(masked, 2);
                    float c;
                    string t = OcrEngine.RecognizePage(mup, 6, out c);
                    int s = HpTextScore(t);
                    if (s > bestScore)
                    {
                        bestScore = s; best = t; bestConf = c;
                        Logger.On("OCR", region.Name + "：颜色掩膜 认出的更多（" + s + " 条）");
                    }
                }
            }
            catch (Exception ex) { Logger.Err("OCR", "血量表掩膜路失败（忽略）", ex); }
            finally
            {
                if (mup != null) mup.Dispose();
                if (masked != null) masked.Dispose();
            }

            return best;
        }

        /// <summary>一条路认出的原文里，能解析出几条 SCP 血量（越多越好）。</summary>
        private static int HpTextScore(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string[] lines = text.Split(new char[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            int ok = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string code; int cur, max;
                if (OcrTacticalLink.TryParseScpHpLine(lines[i], out code, out cur, out max))
                    ok++;
            }
            if (ok > 0) return ok;

            // 一行都没解析出来 → 退化成"出现了几个 SCP 编号"，当作字认得更像
            int codes = 0;
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                if (text.IndexOf(TacticalLog.ScpCodes[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    codes++;
            return -codes;
        }

        private static bool IsAllHpParsed(string text)
        {
            return HpTextScore(text) >= 4;      // 一般最多 5 行，认到 4 条就够用
        }

        /// <summary>灰度反色（浅字深底 → 深字浅底），保留抗锯齿、不砍阈值。</summary>
        private static Bitmap GrayInvert(Bitmap src)
        {
            Bitmap d = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Color p = src.GetPixel(x, y);
                    int g = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                    int v = 255 - g;
                    d.SetPixel(x, y, Color.FromArgb(v, v, v));
                }
            return d;
        }

        /// <summary>放大若干倍（给只有 ~11px 的小字用）。</summary>
        private static Bitmap ScaleUp(Bitmap src, int scale)
        {
            if (scale <= 1) return src;
            Bitmap d = new Bitmap(src.Width * scale, src.Height * scale,
                PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(d))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, d.Width, d.Height));
            }
            return d;
        }

        /// <summary>
        /// SCP 血量表的颜色掩膜：把"亮、而且明显比周围更亮"的像素当字，
        /// 其余当背景 → 黑字白底。
        ///
        /// 跟票数那套掩膜的区别：票数是按特定色相（蓝/绿）抠的，
        /// 而这块各服配色不一样，不能赌某一种颜色。所以这里用
        /// "亮度 + 局部对比"来抠：文字总是比紧邻的背景亮，
        /// 用 3×3 的模糊当背景估计，像素减背景超过阈值就算字。
        /// 这样绿底白字、粉字、黄字都能对付。
        /// </summary>
        private static Bitmap HpColorMask(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            if (w < 4 || h < 4) return null;

            // 亮度图
            byte[] lum = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color p = src.GetPixel(x, y);
                    lum[y * w + x] = (byte)((p.R * 299 + p.G * 587 + p.B * 114) / 1000);
                }

            // 背景估计：3×3 均值
            Bitmap d = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            sum += lum[yy * w + xx]; n++;
                        }
                    int bg = n > 0 ? sum / n : 0;
                    int v = lum[y * w + x];

                    // 比周围亮 18 以上 → 当字（深字白底）
                    byte outv = (byte)((v - bg) >= 18 ? 0 : 255);
                    d.SetPixel(x, y, Color.FromArgb(outv, outv, outv));
                }
            return d;
        }

        /// <summary>
        /// 从识别原文里挑出"最像票数的那一段"，给它打个分（0 = 完全不像）。
        ///
        /// 掩膜做得再干净，边上也难免混进一两个高亮块认出来的符号
        /// （实测 MTF 令牌的原文是 "0 Q) &gt;"、影响力的是 "&lt;3 37/40"），
        /// 所以判分要看连续的一段，而且允许"少认 / 多认一位"这种小差错：
        ///   · 令牌框：单个数字最好（"+ 500"），"08"（Tesseract 给 0 补了个 8）
        ///     这种也留着当候选，但分数低一点；
        ///   · 影响力框：正好 "数字/数字" 最好；"37"、"12140" 这种残的排后面。
        /// 这样"干净掩膜认出 08（置信度 0.73）"就压得过
        /// "脏掩膜认出 0(Q)*（置信度 0.01）"。
        /// </summary>
        private static int TicketShapeScore(OcrRegion region, string raw, float conf)
        {
            return TicketShapeScore(region, raw, conf, 0);
        }

        /// <summary>
        /// 同上，extra 是"这条候选来自哪条路"的额外加分。
        /// 票数识别的首选路（色相掩膜 + Otsu）认出来的结果给一点偏向，
        /// 这样它跟兜底的老办法打平时不会被老办法抢走（老办法容易带上
        /// 左边的高亮块，像 "&lt;3 37/40" 那样）。
        /// 但偏向是有上限的：形状明显更差的结果照样赢不了。
        /// </summary>
        private static int TicketShapeScore(OcrRegion region, string raw, float conf, int extra)
        {
            string run = BestTicketRun(raw);
            if (run.Length == 0) return 0;

            int digits = 0, slash = 0;
            for (int i = 0; i < run.Length; i++)
            {
                if (run[i] >= '0' && run[i] <= '9') digits++;
                else slash++;
            }
            if (digits == 0) return 0;

            int bonus = 0;
            bool exact;
            if (TicketIsToken(region))
            {
                exact = digits == 1 && slash == 0;
                bonus = (3 - digits) * TicketPartialBonus / 2;
            }
            else
            {
                exact = slash > 0 && digits >= 1;
                int d = digits >= 2 ? digits : 2;      // 只认出一位的，按"少一位"算
                bonus = (4 - d) * TicketPartialBonus / 2;
            }
            if (bonus < 0) bonus = 0;
            if (exact) bonus += TicketShapeBonus;

            // 令牌框里冒出斜杠一定是认错了（"0/1/2" 这种），压一压
            if (TicketIsToken(region) && slash > 0) bonus -= TicketSlashPenalty;

            return digits * 1000 + (slash > 0 ? 100 : 0) + bonus + extra + (int)(conf * 100);
        }

        /// <summary>把原文按"票数字符"切段，返回分值最高的那一段。</summary>
        private static string BestTicketRun(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string best = "";
            int bestScore = -1;
            int i = 0;
            while (i < raw.Length)
            {
                if (!IsTicketChar(raw[i])) { i++; continue; }
                int j = i;
                while (j < raw.Length && IsTicketChar(raw[j])) j++;
                string run = raw.Substring(i, j - i);
                int score = TicketRunScore(run);
                if (score > bestScore) { bestScore = score; best = run; }
                i = j;
            }
            return best;
        }

        /// <summary>不看置信度的那部分分值：数字越多越好，斜杠是加分项。</summary>
        private static int TicketRunScore(string run)
        {
            int digits = 0, slash = 0;
            for (int i = 0; i < run.Length; i++)
            {
                if (run[i] >= '0' && run[i] <= '9') digits++;
                else slash++;
            }
            return digits * 100 + (slash > 0 ? 50 : 0) + run.Length;
        }

        private static bool IsTicketChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '/' || c == '／' || c == '\\' || c == '|';
        }

        /// <summary>"正好长成票数样"的加分 / "少认或多认一位"的扣分步长。</summary>
        private const int TicketShapeBonus = 500;
        private const int TicketPartialBonus = 200;
        private const int TicketSlashPenalty = 150;

        /// <summary>首选路（色相掩膜 + Otsu）的加分：平手时它优先于兜底的老办法。</summary>
        private const int TicketLaneBonus = 50;

        /// <summary>首选路最多调用几次识别：两种颜色 × 两个变体。</summary>
        private const int TicketLaneCalls = 4;

        /// <summary>
        /// 这一段"长得像不像"票数。
        /// 注意：run 是 BestTicketRun 切出来的，里面只会有数字和斜杠。
        ///   · 令牌框：正好一位数字才叫"像"（"08" 这种多认一位的不算）；
        ///   · 影响力框：要"数字/数字"（如 "49/80"）才叫"像" ——
        ///     只认出 "37"、或者斜杠被认丢变成 "12140"，都只能当候选，
        ///     还要再试别的颜色 / 变体，别拿它当最终结果。
        /// </summary>
        private static bool TicketShapeOk(OcrRegion region, string run)
        {
            if (string.IsNullOrEmpty(run)) return false;
            int digits = 0;
            bool slash = false;
            for (int i = 0; i < run.Length; i++)
            {
                if (run[i] >= '0' && run[i] <= '9') digits++;
                else slash = true;
            }
            if (digits == 0) return false;

            if (TicketIsToken(region)) return digits == 1 && !slash;
            return slash && digits >= 2;
        }

        /// <summary>老的"亮的部分当字"大扫（4 个阈值 × 放大倍数，取数字最多的一次）。</summary>
        private static string LegacyBrightText(Bitmap shot, out float bestConf)
        {
            bestConf = 0f;
            string best = null;
            int bestScore = -1;

            // 策略表：mode 0=亮度 1=最大通道 2=饱和度；scale 放大倍数；threshold 阈值
            // 顺序按"最可能先成功"排：彩色数字 → 最大通道 + 中间阈值最稳。
            int[] mode = new int[]    { 1,  2,  0,  1,  0,  2,  1,  0 };
            int[] th   = new int[]    { 90, 45, 80, 140, 150, 70, 60, 40 };
            double[] scale = new double[] { 3, 3, 3, 3, 4, 4, 4, 5 };

            int attempts = 0;
            for (int k = 0; k < mode.Length; k++)
            {
                if (attempts >= 8) break;                 // 最多 8 次；死亡时不怕慢，但别无限拖
                attempts++;

                Bitmap big = null, bw = null;
                try
                {
                    Rectangle rc = new Rectangle(0, 0, shot.Width, shot.Height);
                    int w = (int)(shot.Width * scale[k]);
                    int h = (int)(shot.Height * scale[k]);
                    if (w < 1 || h < 1 || (long)w * h > 3000000L) continue;

                    big = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(big))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(shot, new Rectangle(0, 0, w, h), rc, GraphicsUnit.Pixel);
                    }

                    bw = BrightToBlackOnWhiteMode(big, th[k], mode[k]);
                    if (bw == null) continue;

                    float c;
                    string txt = OcrEngine.RecognizePage(bw, 7, out c);   // 7 = 当成一行
                    string digits = OnlyDigits(txt);
                    int score = digits.Length * 1000 + (int)(c * 100);
                    if (digits.Length > 0 && score > bestScore)
                    {
                        bestScore = score;
                        best = txt;
                        bestConf = c;

                        // 早停：已经认到"长得像票数/影响力"的结果就不再试别的了
                        // （影响力是 "72/80" 这种带斜杠的；令牌是 1~2 位数字）
                        string dig2 = digits;
                        if (dig2.Length >= 2) break;
                    }
                }
                catch { }
                finally
                {
                    if (bw != null) { try { bw.Dispose(); } catch { } }
                    if (big != null) { try { big.Dispose(); } catch { } }
                }
            }
            return best;
        }

        // ============================================================
        // 票数 / 影响力：颜色掩膜识别
        //
        // 【为什么单独做一套】HUD 上这两组数字是纯色字：
        //   MTF  = 蓝色 RGB(109,159,247)
        //   混沌 = 绿色 RGB(96,143,56)
        // 压在深橄榄色的半透明底上。按亮度硬阈值的做法会把深底也算成字，
        // 把数字糊掉；按"亮的部分当字"又会把旁边的发电机进度条、阵营图标
        // 一起抠进来，Tesseract 就在这堆东西里乱猜（实测会把 "1" 认成 "7"）。
        //
        // 这里改用色相：蓝色通道比其它两个通道高出一大截的像素才是字，
        // 绿色通道同理。这样：
        //   · 深色底（三通道都低）不会进来；
        //   · 灰白字不会进来（不会把图标、进度条一起抠）；
        //   · 蓝色字和绿色字还能分开认，互不干扰。
        //
        // 抠完再做两件事，才交给引擎：
        //   ① 令牌框（只有一个大数字）先做连通域筛选，只留"高的字块"，
        //      把旁边的进度条 / 高亮块 / 图标碎片剔掉；
        //   ② 把字块裁剪出来，按比例缩放到字高约 34px —— Tesseract 在
        //      这个字高下最准；字太小（影响力只有 12px）和太大（令牌有
        //      90px）都会直接返回空页。
        //
        // 全部用 LockBits + 手写指针/一维数组，一张几百像素的图 1~2ms 就完，
        // 只在票数框上跑（死亡时才扫），完全不影响左下角播报的识别。
        // ============================================================

        /// <summary>颜色掩膜归一化后的目标字高（像素）。</summary>
        private const int TicketTargetGlyphHeight = 34;

        /// <summary>归一化后四周留的白边（像素）：不留边时 Tesseract 会把置信度算成 0。</summary>
        private const int TicketMaskPadding = 8;

        /// <summary>
        /// 票数 / 影响力专用：色相掩膜 → 连通域筛选 → 归一化字高，
        /// 再用"当成一行"的方式识别。认不出返回 null。
        /// </summary>
        private static string TicketMaskText(OcrRegion region, Bitmap shot, out float bestConf)
        {
            bestConf = 0f;
            if (shot == null) return null;

            // 颜色提示：按 tag 先猜（mtf→蓝、chaos→绿），认不出再试另一种。
            // 猜错也不怕，另一种颜色会兜住。
            int hint = TicketColorHint(region);
            int[] order = hint == 1 ? new int[] { 1, 2 } : new int[] { 2, 1 };

            string best = null;
            int bestScore = -1;
            int calls = 0;

            // 调用预算：每块票数框最多 4 次识别，按 Tag 猜的颜色先试、猜错才换另一种。
            // 每种颜色两个变体，顺序按"通常最好"排：
            //   ① Otsu + 3×3 平滑 + 连通域筛选（主力）
            //   ② ①再补回抗锯齿边缘（Otsu 有时会把笔画切细，补一下就够粗了）
            for (int o = 0; o < order.Length && calls < TicketLaneCalls; o++)
            {
                int hue = order[o];                 // 2 = 蓝，1 = 绿

                for (int v = 0; v < 2 && calls < TicketLaneCalls; v++)
                {
                    bool[] ink = TicketColorMaskEx(shot, hue, v == 1);
                    if (CountInk(ink) < 20) continue;

                    ink = TicketDigitComponents(ink, shot.Width, shot.Height);
                    if (ink == null || CountInk(ink) < 20) continue;

                    Bitmap bmp = TicketMaskToBitmap(ink, shot.Width, shot.Height);
                    if (bmp == null) continue;

                    try
                    {
                        float c;
                        calls++;
                        string txt = OcrEngine.RecognizePage(bmp, 7, out c);   // 7 = 当成一行
                        if (txt == null) continue;

                        // 打分看"最像票数的那一段"，不是看有几个数字 ——
                        // 掩膜边上混进来的高亮块会认成 "Q)" 之类，不能让它占便宜。
                        // 首选路的给一点偏向（见 TicketShapeScore 的 extra 参数）。
                        int score = TicketShapeScore(region, txt, c, TicketLaneBonus);
                        if (score > 0 && score > bestScore)
                        {
                            bestScore = score;
                            best = txt;
                            bestConf = c;
                        }
                        // 早停：这一段已经"长得完全像票数"了（令牌=一位数字，
                        // 影响力=数字/数字），不用再试别的颜色 / 别的变体
                        if (score >= 1000 + TicketShapeBonus) return best;
                    }
                    catch { }
                    finally { try { bmp.Dispose(); } catch { } }
                }
            }
            return best;
        }

        /// <summary>票数框里"数字 / 斜杠"的个数，用来判断这次认得够不够全。</summary>
        private static int TicketTokenCount(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
                if ((s[i] >= '0' && s[i] <= '9') || s[i] == '/' || s[i] == '／'
                    || s[i] == '\\' || s[i] == '|') n++;
            return n;
        }

        /// <summary>认出来的东西够不够好（够好就不必再退回老办法大扫一遍）。</summary>
        private static bool IsGoodTicketText(OcrRegion region, string text, float conf)
        {
            if (TicketShapeScore(region, text, conf) <= 0) return false;
            string run = BestTicketRun(text);
            if (!TicketShapeOk(region, run)) return false;
            // 影响力是 "72/80"，至少要两位数字才算认全；令牌一位就够
            return TicketIsToken(region) || TicketTokenCount(run) >= 2;
        }

        /// <summary>这个票数框是大数字（令牌）还是小数字（影响力）。</summary>
        private static bool TicketIsToken(OcrRegion region)
        {
            string tag = region == null || region.Tag == null ? "" : region.Tag;
            return tag.EndsWith("_tokens", StringComparison.OrdinalIgnoreCase)
                || tag.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>按 tag 猜颜色：2 = 蓝（MTF），1 = 绿（混沌），0 = 不知道。</summary>
        private static int TicketColorHint(OcrRegion region)
        {
            string tag = region == null || region.Tag == null
                ? "" : region.Tag.ToLowerInvariant();
            if (tag.IndexOf("chaos") >= 0 || tag.IndexOf("混沌") >= 0) return 1;
            if (tag.IndexOf("mtf") >= 0 || tag.IndexOf("ntf") >= 0) return 2;
            return 0;
        }

        private static int CountInk(bool[] ink)
        {
            if (ink == null) return 0;
            int n = 0;
            for (int i = 0; i < ink.Length; i++) if (ink[i]) n++;
            return n;
        }

        /// <summary>
        /// 色相掩膜：hue = 2 抠蓝字、hue = 1 抠绿字。
        ///
        /// 【为什么要"按色相"而不是按亮度】票数是纯色字压在深色半透明底上，
        /// 旁边还有同色的发电机进度条、阵营图标。按亮度抠会把它们全带进来。
        /// 判据用"目标通道比另外两个通道高出多少"（色差）：深色底（三通道都低）
        /// 和灰白图标（三通道接近）的色差都接近 0，天然被排除；蓝字绿字还能
        /// 分开认，互不干扰。
        ///
        /// 【色差上限因颜色而异】蓝字绿字的"色差上限"差很多：
        ///   MTF  蓝字 RGB(109,159,247)：色差最高 88
        ///   混沌 绿字 RGB(96,143,56)  ：色差最高只有 47
        /// 写死一个阈值（比如 42）对蓝字只是削掉边缘，对绿字却正好卡在尖峰上，
        /// 只剩笔画最中间一条 —— "5/40" 就是这么被认成 "12140" 的。
        ///
        /// 【怎么定阈值：先平滑再 Otsu】游戏画面一直在变，逐像素相减出来的色差
        /// 噪声很大（三个通道的噪声都会进到差值里）。所以这里：
        ///   ① 3×3 均值平滑色差 —— 噪声是独立的会互相抵消，而笔画有好几像素
        ///      宽，平滑之后还在。实测这一步对"背景一直在变"帮助最大；
        ///   ② 在平滑后的直方图上跑 Otsu（大津法），自动找"字 / 背景"两类之间
        ///      的谷当阈值 —— 不用任何写死的常数，亮度整体涨落也不影响。
        /// 用带背景扰动（整体明暗 0.7~1.35 倍、加噪声）的对照测试跑过：
        /// 这套组合比原来"峰值 × 65%"的胜率高 3 倍。
        /// </summary>
        private static bool[] TicketColorMask(Bitmap shot, int hue)
        {
            return TicketColorMaskEx(shot, hue, false);
        }

        /// <summary>
        /// 同上，extraEdge=true 时再做一次"滞后阈值"：Otsu 只圈走字的实心核心，
        /// 这一步把与核心相连的抗锯齿边缘也收回掩膜里，笔画更完整、更好认。
        /// （Otsu 偶尔会把边缘切掉导致字偏细，这时第二个变体会赢。）
        /// </summary>
        private static bool[] TicketColorMaskEx(Bitmap shot, int hue, bool extraEdge)
        {
            int w = shot.Width, h = shot.Height;
            bool[] ink = new bool[w * h];
            if (w < 2 || h < 2) return ink;

            int[] sm;
            int[] bins;
            int visible;
            Measure(shot, hue, out sm, out bins, out visible);
            if (visible < 8) return ink;

            int thr = Otsu(bins, visible);
            if (thr < 16) thr = 16;
            if (thr > 70) thr = 70;

            for (int i = 0; i < sm.Length; i++)
                if (sm[i] >= thr) ink[i] = true;

            if (!extraEdge) return ink;

            // 滞后：把与实心核心相连、色差又够明显的边缘补回来
            int low = thr * 55 / 100;
            if (low < 12) low = 12;
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (ink[i] || sm[i] < low) continue;
                        bool near = false;
                        for (int dy = -1; dy <= 1 && !near; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= w) continue;
                                if (ink[yy * w + xx]) { near = true; break; }
                            }
                        }
                        if (near) { ink[i] = true; changed = true; }
                    }
                if (!changed) break;
            }
            return ink;
        }

        /// <summary>
        /// 量一次色差并做 3×3 平滑，同时把平滑后的直方图统计好。
        /// 一次遍历就够：每个像素的平滑值直接累加进直方图。
        /// sm[i] 是平滑后的色差（-1 表示这个像素太暗、不参与）。
        /// </summary>
        private static void Measure(Bitmap shot, int hue, out int[] sm, out int[] bins, out int visible)
        {
            const int minChannel = 70;      // 太暗的像素（没真的显示出来 / 纯噪声）不参与
            int w = shot.Width, h = shot.Height;

            int[] raw = new int[w * h];
            sm = new int[w * h];
            bins = new int[256];
            visible = 0;

            System.Drawing.Imaging.BitmapData sd = null;
            try
            {
                sd = shot.LockBits(new Rectangle(0, 0, w, h),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = (byte*)sd.Scan0 + y * sd.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            int b = row[x * 4 + 0];
                            int g = row[x * 4 + 1];
                            int r = row[x * 4 + 2];

                            int c, other;
                            if (hue == 2) { c = b; other = r > g ? r : g; }
                            else { c = g; other = r > b ? r : b; }

                            raw[y * w + x] = c < minChannel ? -1 : (c - other);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { if (sd != null) shot.UnlockBits(sd); } catch { }
            }

            // 3×3 均值平滑（只在"够亮"的像素之间平均，免得把暗背景拉进来）
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (raw[i] < 0) { sm[i] = -1; continue; }

                    int sum = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            int v = raw[yy * w + xx];
                            if (v < 0) continue;
                            sum += v; n++;
                        }
                    }
                    int avg = n == 0 ? 0 : sum / n;
                    sm[i] = avg;
                    if (avg < 0) avg = 0;
                    if (avg > 255) avg = 255;
                    bins[avg]++;
                    visible++;
                }
        }

        /// <summary>
        /// Otsu（大津法）：在直方图上找一个阈值，让"字 / 背景"两类的类间方差最大。
        /// 不依赖任何写死的色差常数 —— 画面整体明暗变化也不会影响它。
        /// </summary>
        private static int Otsu(int[] bins, int visible)
        {
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += (double)i * bins[i];

            double sumB = 0;
            long wB = 0;
            double best = -1;
            int bestT = 0;

            for (int t = 0; t < 256; t++)
            {
                wB += bins[t];
                if (wB == 0) continue;
                long wF = visible - wB;
                if (wF == 0) break;

                sumB += (double)t * bins[t];
                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double between = (double)wB * wF * (mB - mF) * (mB - mF);
                if (between > best) { best = between; bestT = t; }
            }
            return bestT;
        }

        /// <summary>
        /// 只留下"像数字"的连通域，把高亮块 / 进度条 / 图标碎片剔掉。
        ///
        /// 做法：先找所有连通域，取"面积较大的那些"的高度中位数当字号；
        /// 只保留高度在 0.65~1.6 倍字号之间、宽度不超过 1.4 倍字号的域；
        /// 再按 x 间距聚簇，取含最大字块的那一簇（这样 "37/40" 会整组留下，
        /// 而紧挨着的发电机进度条、右下角的高亮块会被分到别的簇里丢掉）。
        /// </summary>
        private static bool[] TicketDigitComponents(bool[] ink, int w, int h)
        {
            List<int[]> comps = TicketComponents(ink, w, h, 12);
            if (comps.Count == 0) return null;

            // 字号：用"面积 >= 25"的域的高度中位数；全都很小就用全体
            List<int[]> big = new List<int[]>();
            for (int i = 0; i < comps.Count; i++) if (comps[i][4] >= 25) big.Add(comps[i]);
            if (big.Count == 0) big = comps;

            List<int> hs = new List<int>();
            for (int i = 0; i < big.Count; i++) hs.Add(big[i][3]);
            hs.Sort();
            int med = hs[hs.Count / 2];

            int lo = (int)(med * 0.65);
            int hi = (int)(med * 1.6);

            List<int[]> cand = new List<int[]>();
            for (int i = 0; i < comps.Count; i++)
            {
                int cw = comps[i][2], ch = comps[i][3];
                if (ch < lo || ch > hi) continue;
                if (cw > med * 1.4) continue;
                cand.Add(comps[i]);
            }
            if (cand.Count == 0) return null;

            cand.Sort(delegate (int[] a, int[] b) { return a[0] - b[0]; });

            int gap = med * 6 / 5;
            if (gap < 6) gap = 6;

            List<List<int[]>> clusters = new List<List<int[]>>();
            List<int[]> cur = new List<int[]>();
            int right = int.MinValue;
            for (int i = 0; i < cand.Count; i++)
            {
                int[] c = cand[i];
                if (cur.Count > 0 && c[0] - right > gap)
                {
                    clusters.Add(cur);
                    cur = new List<int[]>();
                }
                cur.Add(c);
                if (c[0] + c[2] > right) right = c[0] + c[2];
            }
            if (cur.Count > 0) clusters.Add(cur);

            // 取含"最大字块"的那一簇
            int biggest = -1, bestSet = 0;
            for (int i = 0; i < clusters.Count; i++)
                for (int j = 0; j < clusters[i].Count; j++)
                    if (clusters[i][j][4] > biggest) { biggest = clusters[i][j][4]; bestSet = i; }

            bool[] keep = new bool[w * h];
            List<int[]> chosen = clusters[bestSet];
            for (int i = 0; i < chosen.Count; i++)
            {
                int[] c = chosen[i];
                for (int y = c[1]; y < c[1] + c[3]; y++)
                    for (int x = c[0]; x < c[0] + c[2]; x++)
                        if (ink[y * w + x]) keep[y * w + x] = true;
            }
            return keep;
        }

        /// <summary>八连通域标记，返回 {x, y, w, h, 面积}。面积小于 minArea 的丢掉。</summary>
        private static List<int[]> TicketComponents(bool[] m, int w, int h, int minArea)
        {
            List<int[]> list = new List<int[]>();
            int[] lab = new int[w * h];
            int[] stack = new int[w * h];
            int cur = 0;

            for (int i = 0; i < w * h; i++)
            {
                if (!m[i] || lab[i] != 0) continue;
                cur++;
                int sp = 0;
                stack[sp++] = i;
                lab[i] = cur;

                int minX = w, minY = h, maxX = -1, maxY = -1, n = 0;
                while (sp > 0)
                {
                    int p = stack[--sp];
                    int x = p % w, y = p / w;
                    n++;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            int q = yy * w + xx;
                            if (!m[q] || lab[q] != 0) continue;
                            lab[q] = cur;
                            stack[sp++] = q;
                        }
                    }
                }
                if (n >= minArea) list.Add(new int[] { minX, minY, maxX - minX + 1, maxY - minY + 1, n });
            }
            return list;
        }

        /// <summary>
        /// 把掩膜裁剪到实际内容，再等比放大到字高约 34px，做成黑字白底的位图。
        /// 缩放要干净：先 1:1 拷到只含内容的临时图，再一次性画成目标大小，
        /// 免得把框外的垃圾像素一起插值进来。
        /// </summary>
        private static unsafe Bitmap TicketMaskToBitmap(bool[] ink, int w, int h)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (!ink[row + x]) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return null;

            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;

            double sc = (double)TicketTargetGlyphHeight / bh;
            if (sc < 1.5) sc = 1.5;
            if (sc > 8.0) sc = 8.0;
            int iw = (int)Math.Round(bw * sc);
            int ih = (int)Math.Round(bh * sc);
            if (iw < 8) iw = 8;
            if (ih < 8) ih = 8;

            // 四周留一圈白：字紧贴图边时 Tesseract 会认字但把置信度算成 0
            // （实测 MTF 令牌的 "0" 就是这样）；留边之后置信度从 0.00 回到 0.6~0.9。
            int nw = iw + TicketMaskPadding * 2;
            int nh = ih + TicketMaskPadding * 2;
            if ((long)nw * nh > 4000000L) return null;

            Bitmap small = null;
            Bitmap dst = null;
            try
            {
                small = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                System.Drawing.Imaging.BitmapData d1 = null;
                try
                {
                    d1 = small.LockBits(new Rectangle(0, 0, bw, bh),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    for (int y = 0; y < bh; y++)
                    {
                        byte* row = (byte*)d1.Scan0 + y * d1.Stride;
                        for (int x = 0; x < bw; x++)
                        {
                            byte v = ink[(y + minY) * w + (x + minX)] ? (byte)0 : (byte)255;
                            row[x * 3 + 0] = v;
                            row[x * 3 + 1] = v;
                            row[x * 3 + 2] = v;
                        }
                    }
                }
                finally { try { if (d1 != null) small.UnlockBits(d1); } catch { } }

                dst = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(TicketMaskPadding, TicketMaskPadding, iw, ih),
                        new Rectangle(0, 0, bw, bh), GraphicsUnit.Pixel);
                }
                return dst;
            }
            catch
            {
                if (dst != null) { try { dst.Dispose(); } catch { } }
                return null;
            }
            finally
            {
                if (small != null) { try { small.Dispose(); } catch { } }
            }
        }

        /// <summary>
        /// 图像处理核心：把"亮的彩色字 + 深色底"抠成"黑字白底"。
        ///
        /// 【为什么要这么多花样】HUD 上的票数是蓝色/绿色数字，压在半透明深色底上：
        ///   · 按亮度算，蓝字的亮度其实不高（蓝的 0.114 权重最小）→ 容易漏；
        ///   · 所以额外用 max(R,G,B)（对彩色字最亮）和 饱和度（彩色 vs 灰底）两条路；
        ///   · 抠出来之后再 3×3 膨胀一次，把抗锯齿造成的细笔画补粗，Tesseract 才认得出。
        /// mode: 0 = 亮度，1 = 最大通道，2 = 饱和度
        /// </summary>
        private static Bitmap BrightToBlackOnWhiteMode(Bitmap src, int threshold, int mode)
        {
            Bitmap dst = new Bitmap(src.Width, src.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            System.Drawing.Imaging.BitmapData sd = null, dd = null;
            try
            {
                sd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                dd = dst.LockBits(new Rectangle(0, 0, dst.Width, dst.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                int w = src.Width, h = src.Height;
                bool[] ink = new bool[w * h];

                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* srow = (byte*)sd.Scan0 + y * sd.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            int b = srow[x * 4 + 0];
                            int g = srow[x * 4 + 1];
                            int r = srow[x * 4 + 2];

                            bool on;
                            if (mode == 1)
                            {
                                int mx = r > g ? r : g;
                                if (b > mx) mx = b;
                                on = mx > threshold;
                            }
                            else if (mode == 2)
                            {
                                int mx = r > g ? r : g;
                                if (b > mx) mx = b;
                                int mn = r < g ? r : g;
                                if (b < mn) mn = b;
                                int chroma = mx - mn;          // 彩色度
                                on = chroma >= threshold && mx >= 70;   // 有颜色且不太暗
                            }
                            else
                            {
                                on = (r * 299 + g * 587 + b * 114) / 1000 > threshold;
                            }
                            ink[y * w + x] = on;
                        }
                    }
                }

                // 3×3 膨胀：周围有一个是字就把它也算成字（补粗笔画）
                bool[] dil = new bool[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        bool on = false;
                        for (int dy = -1; dy <= 1 && !on; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= w) continue;
                                if (ink[yy * w + xx]) { on = true; break; }
                            }
                        }
                        dil[y * w + x] = on;
                    }
                }

                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* drow = (byte*)dd.Scan0 + y * dd.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte v = dil[y * w + x] ? (byte)0 : (byte)255;
                            drow[x * 3 + 0] = v;
                            drow[x * 3 + 1] = v;
                            drow[x * 3 + 2] = v;
                        }
                    }
                }
            }
            catch
            {
                try { dst.Dispose(); } catch { }
                return null;
            }
            finally
            {
                try { if (sd != null) src.UnlockBits(sd); } catch { }
                try { if (dd != null) dst.UnlockBits(dd); } catch { }
            }
            return dst;
        }

        private static Bitmap BrightToBlackOnWhite(Bitmap src, int threshold)
        {
            return BrightToBlackOnWhiteMode(src, threshold, 0);
        }
        /// <summary>只留数字（给激进识别打分用）。</summary>
        private static string OnlyDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '0' && s[i] <= '9') sb.Append(s[i]);
            return sb.ToString();
        }

        /// <summary>
        /// 把票数框识别出来的原始文字洗成能看的数字串。
        /// 大数字（令牌）只可能是 0-9 一位；小数字（影响力）是 "49/80" 这种。
        ///
        /// 【为什么先切"最像票数的那一段"】掩膜再干净，边上也常混进一两个高亮块
        /// 认出来的符号。实测 MTF 影响力有一次认出 "&lt;3 37/40"：如果把原文里所有
        /// 数字字符都串起来，会得到 "337/40"（错了）；而按"最像票数的一段"切，
        /// 正好切出 "37/40"（对的）。所以这里复用 TicketShapeScore 那套分段逻辑。
        /// </summary>
        public static string ParseTicketValue(string tag, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            bool tokens = tag != null && tag.EndsWith("_tokens", StringComparison.OrdinalIgnoreCase);

            // 先按"最像票数的一段"取（切不出来就退回整段原文）
            string run = BestTicketRun(raw);
            string source = run.Length > 0 ? run : raw;

            if (tokens)
            {
                // 大数字就一位：用去噪后的文本取第一个数字
                string flat = Normalize(source);
                for (int i = 0; i < flat.Length; i++)
                    if (flat[i] >= '0' && flat[i] <= '9') return flat[i].ToString();
                return null;
            }

            // 影响力（"49/80"）：必须保留斜杠 ——
            // 全干掉的话 "49/80" 会变成 "4980"，分不清是"49 比 80"还是"4980"。
            string digits = "";
            for (int i = 0; i < source.Length; i++)
            {
                char ch = source[i];
                if (ch >= '0' && ch <= '9') { digits += ch; continue; }
                if (ch == '/' || ch == '\\' || ch == '／' || ch == '|') { digits += '/'; continue; }
            }
            if (digits.Length == 0) return null;
            if (digits.Length > 12) digits = digits.Substring(0, 12);

            // 【MAX 不是"没读到右边"】游戏在"本回合这方不会再有新令牌"时，
            // 影响力那格写的是 "168/MAX" / "63/MAX"。以前这里只保留数字和斜杠，
            // MAX 被洗掉，下游只看到 "168/"，完全不知道已经拿满。
            // 现在把 MAX 原样保留（只限影响力；令牌框不会走这里）。
            if (TicketSaysMax(raw))
            {
                if (!digits.EndsWith("/", StringComparison.Ordinal)) digits += "/";
                digits += "MAX";
            }
            return digits;
        }

        /// <summary>
        /// 影响力框的右边是不是游戏写的 MAX。
        /// 必须在斜杠**后面**找到 MAX —— 防止把左边数字旁边的杂字当成 MAX。
        /// 允许 OCR 在 MAX 后面继续粘杂字（实测有 "MAX G" / "MAXK"）。
        /// </summary>
        private static bool TicketSaysMax(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            int slash = -1;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '/' || c == '／' || c == '\\' || c == '|') { slash = i; break; }
            }
            if (slash < 0 || slash + 1 >= raw.Length) return false;
            return raw.IndexOf("MAX", slash + 1, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- 角色关键词 ----
        // 只认"我是谁"，所以比播报规则严格得多：必须出现明确的身份词。
        //
        // 【实测踩过的坑】角色框开得比较大时，观战/等待界面也会被扫进来：
        // 原文「正在等待玩家 20 已连接 @ 选择不扮演 SCP」里有个 SCP，
        // 于是弹了「你的角色：SCP」（人还在观战）。
        // 所以除了身份词，还要求这段文字不像界面提示、并且够短（角色卡就几个字）。
        private static readonly Regex ReRoleScp = new Regex(
            "scp|特殊收容", RegexOptions.CultureInvariant);

        /// <summary>带编号的 SCP（SCP-173 / SCP 096）—— 最可靠的角色写法。</summary>
        private static readonly Regex ReRoleScpNumbered = new Regex(
            "(?:scp|5cp)\\s*[-－—_:：]?\\s*\\d{2,4}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 明显是"界面提示"而不是"角色卡"的文字：等待玩家 / 已连接 / 选择 / 扮演 /
        /// 观察者 / 加载中… 命中就不认角色。
        /// </summary>
        private static readonly Regex ReRoleUiNoise = new Regex(
            "等待|已连接|正在|选择|扮演|观察|观战|spectat|waiting|connecting|loading|has joined|joined",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>角色卡最多这么长（Normalize 之后）。再长基本是别的东西。</summary>
        private const int RoleTextMaxLen = 24;

        private static readonly Regex ReRoleDClass = new Regex(
            "d\\s*[-－]?\\s*级|d\\s*[-－]?\\s*class|class\\s*[-－]?\\s*d|d级人员|d 级人员",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ReRoleMtf = new Regex(
            "mtf|ntf|九尾|机动特遣|特遣队|epsilon|ninetailed",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ReRoleChaos = new Regex(
            "chaos|混沌|叛乱|反叛|叛军",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ReRoleScientist = new Regex(
            "科学家|scientist|研究员|research",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 从角色区域认出来的文字里判断"我是什么角色"。
        /// 返回给用户看的名字；认不出来返回 null。
        /// </summary>
        public static string ParseRole(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string flat = Normalize(raw);
            if (flat.Length == 0) return null;

            // ① 界面提示（等待玩家 / 已连接 / 选择不扮演…）不是角色卡 —— 直接不认
            if (ReRoleUiNoise.IsMatch(flat)) return null;

            // ② 带编号的 SCP 最可靠（"SCP-173"），长度不限
            if (ReRoleScpNumbered.IsMatch(flat)) return "SCP";

            // ③ 其余身份词都要求整段够短 ——
            //    真正的角色卡就几个字；一长串里蹦出个"九尾狐"多半是玩家列表 / 击杀播报。
            if (flat.Length > RoleTextMaxLen) return null;

            // 顺序有讲究：先认"SCP"，再认混沌 / MTF / D 级 / 科学家
            if (ReRoleScp.IsMatch(flat)) return "SCP";
            if (ReRoleChaos.IsMatch(flat)) return "混沌分裂者";
            if (ReRoleMtf.IsMatch(flat)) return "MTF（九尾狐）";
            if (ReRoleDClass.IsMatch(flat)) return "D 级人员";
            if (ReRoleScientist.IsMatch(flat)) return "科学家";
            return null;
        }

        /// <summary>
        /// 角色框里出现这些词 = "现在还是观察者"（进图后先观察者，几秒后才变正式角色；
        /// 死了之后也是观察者）。
        /// </summary>
        private static readonly Regex ReObserver = new Regex(
            "观察者|觀察者|观战|觀戰|旁观|旁觀|spectat|observer",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 角色框这段文字是不是"观察者"状态。
        ///
        /// 【为什么单独做一条】`ParseRole` 把"观察者/观战"当界面杂音直接丢掉
        /// （那是为了防止观战界面被认成角色）—— 于是"观察者 → 正式角色"这个
        /// 转换在日志里看不见。操作者要的正是这个时刻，所以这里专门认一下，
        /// 但不把它当角色（只用来记状态 + 日志）。
        /// </summary>
        public static bool IsObserverText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string flat = Normalize(raw);
            if (flat.Length == 0) return false;
            if (flat.Length > RoleTextMaxLen) return false;   // 整段太长 → 多半是别的界面
            return ReObserver.IsMatch(flat);
        }

        /// <summary>
        /// 核弹时间归到整十秒（核弹播报都是 90 / 80 / 70 / 120 这样的整十）。
        ///
        /// 【操作者口径】两位数取前一位 —— 实测原文是
        /// 「本设施的地下区域将在倒计时: 98 秒后自毁」，而游戏里那一下是 90 秒，
        /// 所以 98 要变成 90（往下截到十位），不能四舍五入成 100。
        /// 全部向下取整到整十（32 → 30、117 → 110），避免 OCR 多读几秒后向上抬高计时。
        /// 至少给 10 秒。
        /// </summary>
        private static int SnapToTen(int sec)
        {
            if (sec <= 0) return sec;
            int snapped = (sec / 10) * 10;
            if (snapped < 10) snapped = 10;
            return snapped;
        }

        private static int ClampInterval(int ms)
        {
            if (ms < 300) ms = 300;
            if (ms > 30000) ms = 30000;            return ms;
        }

        /// <summary>需要定时轮询的区域。</summary>
        private List<OcrRegion> PolledRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            if (_cfg.OcrRegions == null) return list;
            for (int i = 0; i < _cfg.OcrRegions.Count; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r != null && r.Enabled && r.Poll) list.Add(r);
            }
            return list;
        }

        /// <summary>只在按“立即扫描”时才扫的区域。</summary>
        private List<OcrRegion> OnDemandRegions()
        {
            List<OcrRegion> list = new List<OcrRegion>();
            if (_cfg.OcrRegions == null) return list;
            for (int i = 0; i < _cfg.OcrRegions.Count; i++)
            {
                OcrRegion r = _cfg.OcrRegions[i];
                if (r != null && r.Enabled && !r.Poll) list.Add(r);
            }
            return list;
        }

        private void OnTick(object state)
        {
            if (_disposed) return;
            // 上一轮（或者手动扫描）还没跑完就直接跳过这一轮，绝不排队堆积
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try { Poll(); }
            catch (Exception ex) { Logger.Err("OCR", "定时轮询异常", ex); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void Poll()
        {
            if (!_cfg.OcrAllowed) return;

            // 【操作者要求】进服后才开始运作：定时器已经停了，这里再兜一道，
            // 免得"进服事件晚到 / 刚离服"的那一拍还去截一张图。
            if (_cfg.OcrOnlyInServer && !_mgr.InServer) return;

            // ---- 第 4 批：准备阶段的"屏幕中心角色"快速扫已删除 ----
            // 角色改由 FactionRecognizer 读血条颜色判定，这里不再走 OCR。

            if (DateTime.UtcNow < _cooldownUntil) return;

            // ---- 核弹监视：先扫播报区（不等冷却），拿到时间就停 ----
            if (_nukeWatch)
            {
                if (DateTime.Now > _nukeWatchUntil) StopNukeWatch("盯满 " + NukeWatchSeconds + " 秒没抓到时间");
                else if (OcrEngine.IsReady)
                {
                    List<OcrRegion> bc = BroadcastRegions();
                    for (int i = 0; i < bc.Count; i++)
                    {
                        ScanResult rb = ScanRegion(bc[i], false, false, true);
                        if (rb.DidOcr) break;
                        if (_disposed) return;
                    }
                }
            }

            List<OcrRegion> polled = PolledRegions();
            // 死亡期间还要扫：票数框 + 右下角静音开关（判断复活）+ 左侧 SCP 列表
            List<OcrRegion> tickets = _deathScan ? DeathScanRegions() : null;
            // 活着时只间隔扫描影响力；令牌区域只在死亡扫描时加入。
            bool aliveTicket = tickets == null && AliveTicketDue;
            if (aliveTicket) tickets = TicketRegions(false);
            // SCP 列表持续监视（进服+首次死亡 / 手动全部扫描后打开）
            List<OcrRegion> scpWatch = _scpListWatch ? ScpListRegions() : null;
            bool scpDue = scpWatch != null && scpWatch.Count > 0
                && DateTime.UtcNow >= _scpListNext;
            if (polled.Count == 0 && (tickets == null || tickets.Count == 0)
                && !scpDue) return;

            if (!OcrEngine.IsReady)
            {
                OcrEngine.EnsureReady(_engineDir != null ? _engineDir : TessdataPath(), _cfg.OcrLang);
                LastInfo = "引擎初始化中…（" + OcrEngine.Status + "）";
                return;
            }

            // ---- 死亡期间：先把 4 个票数框扫一遍 ----
            // 这一步故意不等冷却（fastMode）：死亡界面是静止的，
            // 4 个框一轮扫完正好赶上界面还在。
            if (tickets != null && tickets.Count > 0)
            {
                // 死亡界面：多扫几遍（一帧看不准，要取"确定值"）
                // 活着：只扫一遍就够（省 OCR；而且值一般不变，慢慢攒够确认次数）
                int passes = _deathScan ? 2 : 1;
                _deathRoundHit = false;      // 这一轮有没有读到东西（判复活用）
                _deathTicketRoundHit = false;
                bool hasTicketRegion = false;
                bool ticketOcrCompleted = false;
                bool silenceOcrCompleted = false;
                bool sawSilence = false;
                for (int i = 0; i < tickets.Count; i++)
                    if (IsTicketRegion(tickets[i])) { hasTicketRegion = true; break; }
                for (int pass = 0; pass < passes; pass++)
                {
                    for (int i = 0; i < tickets.Count; i++)
                    {
                        ScanResult rt = ScanRegion(tickets[i], false, false, true);
                        if (IsTicketRegion(tickets[i]) && rt.DidOcr) ticketOcrCompleted = true;
                        if (tickets[i].Kind == OcrRegionKind.Silence && rt.SilenceScanned)
                        {
                            silenceOcrCompleted = true;
                            if (rt.SawSilence) sawSilence = true;
                        }
                        if (_disposed) return;
                    }
                }
                if (_deathScan) _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownForStrength(Strength));

                // 独立报告票数扫描结果。严格判定会把它和静音连续漏识别、窗口重绘
                // 分别记时，避免几个 OCR 区域的扫描顺序影响最终结果。
                if (_deathScan && hasTicketRegion && ticketOcrCompleted && OnTicketScan != null)
                {
                    bool sawTicket = _deathTicketRoundHit;
                    if (_runOnUi != null) _runOnUi(delegate { try { OnTicketScan(sawTicket); } catch { } });
                    else { try { OnTicketScan(sawTicket); } catch { } }
                }

                // 两遍识别合并成一轮结果，避免同一张画面被计为两次静音漏识别。
                if (_deathScan && silenceOcrCompleted && OnSilenceScan != null)
                {
                    bool saw = sawSilence;
                    if (_runOnUi != null) _runOnUi(delegate { try { OnSilenceScan(saw); } catch { } });
                    else { try { OnSilenceScan(saw); } catch { } }
                }

                // 【判复活：第二路信号】死亡界面这一轮票数框 + 静音开关全都空
                // （复活之后那块界面就没了，OCR 什么都读不出来，以前这条信号是丢掉的）。
                if (_deathScan && !_deathRoundHit && OnDeathScreenEmpty != null)
                {
                    if (_runOnUi != null) _runOnUi(delegate { try { OnDeathScreenEmpty(); } catch { } });
                    else { try { OnDeathScreenEmpty(); } catch { } }
                }
            }

            // 一轮最多真正调用一次引擎（调完就进冷却期了）
            for (int i = 0; i < polled.Count; i++)
            {
                ScanResult r = ScanRegion(polled[i], true, true);
                if (r.DidOcr) break;
            }

            // ---- SCP 列表持续监视：隔 ~2.5 秒扫一次左侧列表 ----
            // （走到这里说明这一轮还没调过引擎或者引擎还有余量；
            //   用独立节流时间，不跟票数框 / 轮询区抢）
            if (scpDue && !_disposed)
            {
                ScanRegion(scpWatch[0], false, false, true);
                _scpListNext = DateTime.UtcNow.AddMilliseconds(2500);
            }
        }

        /// <summary>
        /// “立即扫描”快捷键：把所有“按需”区域各截一次、识别一次。
        /// 识别在后台线程跑（阻塞 UI 会掉帧），数据套用转回 UI 线程。
        /// </summary>
        public void ScanOnDemand()
        {
            ScanOnDemand(false);
        }

        /// <summary>
        /// includePolled = true 时连"定时轮询"的区域（左下角播报）也一起扫一遍 ——
        /// 设置窗口里的「立即扫描一次」按钮用这个，才算真的"扫描全部"；
        /// 它只是额外扫一次，不影响自动轮询的节奏。
        /// </summary>
        public void ScanOnDemand(bool includePolled)
        {
            if (_disposed) return;
            _quietScan = true;   // 手动扫描：安静模式（只识别、只显示结果）

            if (!_cfg.OcrAllowed)
            {
                Logger.On("扫描", "屏幕识别已关闭，忽略“立即扫描”");
                LastInfo = "屏幕识别已关闭";
                LastScanSummary = "屏幕识别已关闭（到设置里打开总开关）";
                return;
            }

            List<OcrRegion> list = includePolled ? AllEnabledRegions() : OnDemandRegions();
            if (list.Count == 0)
            {
                Logger.On("扫描", "没有可扫的区域");
                LastInfo = "没有可扫的区域";
                LastScanSummary = "没有可扫的区域（到 设置 → OCR 区域列表 里加/启用）";
                return;
            }

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                Logger.On("扫描", "上一次识别还没跑完，本次跳过");
                LastInfo = "上一次还在跑，本次跳过";
                LastScanSummary = "上一次扫描还没跑完，本次跳过 —— 等 1~2 秒再按一次";
                return;
            }

            Task.Run(new Action(delegate
            {
                try
                {
                    Logger.On("扫描", "立即扫描开始，共 " + list.Count + " 个按需区域");
                    LastInfo = "正在扫描 " + list.Count + " 个区域…";

                    OcrEngine.EnsureReady(_engineDir != null ? _engineDir : TessdataPath(), _cfg.OcrLang);
                    if (!OcrEngine.IsReady)
                    {
                        Logger.On("扫描", "OCR 引擎还没就绪（" + OcrEngine.Status + "），稍等一下再按");
                        LastInfo = "引擎还没就绪：" + OcrEngine.Status;
                        return;
                    }

                    int ocr = 0;
                    int got = 0;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    System.Text.StringBuilder sbAll = new System.Text.StringBuilder();
                    System.Text.StringBuilder sbMiss = new System.Text.StringBuilder();
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (_disposed) break;
                        ScanResult r = ScanRegion(list[i], false, false);
                        if (r.DidOcr) ocr++;
                        string note = r.Note.Length == 0 ? "无结果" : r.Note;
                        if (r.Text != null && r.Text.Trim().Length > 0 && got < 6)
                        {
                            got++;
                            sb.Append("【" + list[i].Name + "】" + note + "　");
                        }
                        Logger.On("扫描", "  " + list[i].Describe() + " → " + note);

                        string rec = (r.Text == null ? "" : r.Text.Trim());
                        sbAll.AppendLine("【" + (i + 1) + ". " + list[i].Name + "】" + list[i].KindName()
                            + "  位置 " + list[i].Describe());
                        if (rec.Length > 0)
                        {
                            if (rec.Length > 160) rec = rec.Substring(0, 160) + "…";
                            sbAll.AppendLine("    认出：" + rec);
                            sbAll.AppendLine("    处理：" + note);
                        }
                        else
                        {
                            sbAll.AppendLine("    ✗ 没认出来：" + note);
                            sbMiss.AppendLine("· " + list[i].Name + "（" + list[i].KindName() + "，"
                                + list[i].Describe() + "）→ " + note);
                        }
                    }

                    // 顺手把"票数 / SCP"这类关键信息也汇总进来
                    string extra = "";
                    try
                    {
                        string tk = _onScanExtra != null ? _onScanExtra() : "";
                        if (!string.IsNullOrEmpty(tk)) extra = tk;
                    }
                    catch { }

                    // 完整报告：上半部分每块区域的结果，下半部分没扫出来的
                    LastScanReport = "扫描全部 —— 共 " + list.Count + " 块区域，实际调用 OCR " + ocr + " 次"
                        + "，认出文字 " + got + " 块" + Environment.NewLine
                        + new string('=', 60) + Environment.NewLine
                        + sbAll.ToString() + Environment.NewLine
                        + new string('=', 60) + Environment.NewLine
                        + (sbMiss.Length == 0 ? "没有『没认出来』的区域 —— 全部区域都认到东西了。"
                            : ("没扫出来的内容：" + Environment.NewLine + sbMiss.ToString()))
                        + (extra.Length > 0 ? Environment.NewLine + "面板数据：" + extra : "");

                    if (got == 0)
                        LastScanSummary = "扫描失败：这一轮没有任何区域认出文字。"
                            + "（检查那几块区域是否对准；也可以到日志页看每块的跳过原因）"
                            + (extra.Length > 0 ? "　" + extra : "");
                    else
                        LastScanSummary = "扫描完成，认出 " + got + " 块：" + sb.ToString()
                            + (extra.Length > 0 ? "　" + extra : "");

                    Logger.On("扫描", "立即扫描结束：共 " + list.Count + " 个区域，实际调用 OCR " + ocr + " 次");
                    _quietScan = false;   // 安静模式结束
                    LastInfo = "扫描完成：" + list.Count + " 个区域，调用 OCR " + ocr + " 次";

                    // 结果也丢到左下角那块提示上：设置窗口没在最前面时也能立刻看到
                    string show = LastScanSummary;
                    if (show != null && show.Length > 90) show = show.Substring(0, 90) + "…";
                    ShowToast("手动扫描完成（" + list.Count + " 个区域）", show);
                }
                catch (Exception ex)
                {
                    Logger.Err("扫描", "立即扫描失败", ex);
                    LastInfo = "扫描失败：" + ex.Message;
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            }));
        }

        private static IntPtr _fgHwnd;
        private static bool _fgIsGame;
        private static DateTime _fgCheckedAt = DateTime.MinValue;

        /// <summary>
        /// 当前前台窗口是不是 SCP:SL。
        ///
        /// 【为什么播报区必须卡这个】操作者 alt-tab 出去聊天 / 看浏览器时，
        /// 左下角播报区截到的是桌面文字 —— 实测 OCR 把聊天记录里的
        /// 「终焉 / 亡者协议」读成播报，直接误触发终焉表。游戏不在前台时，
        /// 屏幕上的 CASSIE 字幕本来也不可能在播，跳过最安全。
        /// </summary>
        private static bool IsGameForeground()
        {
            IntPtr h = NativeMethods.GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            DateTime now = DateTime.UtcNow;
            if (h == _fgHwnd && (now - _fgCheckedAt).TotalMilliseconds < 500) return _fgIsGame;

            bool result = false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(h, out pid);
            if (pid != 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        string n = p.ProcessName ?? "";
                        result = !n.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)
                            && (n.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
                catch { result = false; }
            }
            _fgHwnd = h; _fgIsGame = result; _fgCheckedAt = now;
            return result;
        }

        /// <summary>
        /// 扫一个区域：截图 → 白像素预检 → OCR → 分发结果。
        /// allowAlerts：识别到播报关键词时要不要弹悬浮窗（只有播报区要）。
        /// dedup：同一段文字短时间内不重复处理。
        /// fastMode：准备阶段的角色快速扫描 —— 不等冷却，认到角色就立刻回报。
        /// </summary>
        private ScanResult ScanRegion(OcrRegion region, bool allowAlerts, bool dedup)
        {
            return ScanRegion(region, allowAlerts, dedup, false);
        }

        private ScanResult ScanRegion(OcrRegion region, bool allowAlerts, bool dedup, bool fastMode)
        {
            ScanResult res = new ScanResult();
            if (region == null || !region.Enabled) return res;

            Rectangle rc = region.Rect();
            if (rc.Width <= 0 || rc.Height <= 0) { res.Note = "区域无效"; return res; }

            int bright;
            int nonBlack;
            Bitmap bin = Capture(rc, out bright, out nonBlack);
            if (bin == null) { res.Note = "截图失败（游戏可能在全屏独占）"; return res; }

            // ---- 【整张截图全黑 = 录制被挡住了】----
            // 操作者遇到的情况：Windows 弹「受保护的应用程序阻止桌面录制」，
            // 其实就是屏幕上那个应用（DRM / 防录制）不让别人截它的画面 ——
            // CopyFromScreen 拿到的会是全黑，OCR 再准也认不出任何字。
            // 这里连续几次全黑就明确写一行日志，免得让人以为"程序坏了"。
            if (nonBlack == 0)
            {
                _blackShots++;
                if (_blackShots == 6)
                {
                    Logger.On("OCR", "连续 6 次截到的画面全是黑的 —— 很可能是受保护的窗口"
                        + "（Windows 提示「受保护的应用程序阻止桌面录制」）挡住了录制，"
                        + "截图只会返回全黑，OCR 自然一个都认不出。"
                        + "处理办法：把那个应用关掉 / 挪走；游戏改成「无边框窗口」；"
                        + "游戏和本程序的权限保持一致（都用管理员或都不用）。"
                        + "也可能只是独占全屏。");
                }
            }
            else _blackShots = 0;

            // ---- 白像素预检：不够亮就到此为止，OCR 引擎连碰都不碰 ----
            bool loose, veryLoose;
            int brightThreshold;
            StrengthParams(Strength, out loose, out veryLoose, out brightThreshold);

            // 【踩过的坑】票数 / 影响力 / 静音那几块是"彩色数字压深底"，
            // 白像素预检会把它们当成"画面没高亮"整片跳过 —— 票数永远认不出来。
            // 所以这几种区域不套预检（直接进去用激进方式认）。
            int useThreshold = brightThreshold;
            if (IsColorishRegion(region))
                useThreshold = 0;

            // 「最大」档：定时轮询的区域（左下角播报）也不套"无高亮就跳过"那道预检。
            // 【为什么】操作者要求：宁可多扫也别漏。字幕淡出 / 半透明那几帧经常
            // 高亮像素不到 40，于是那一句播报整条丢掉（日志里"画面无高亮，跳过"
            // 之后紧跟着就是播报）；他不介意这点占用。节奏由轮询间隔 +
            // CooldownForStrength 决定，低档位照旧靠预检省 OCR。
            if (Strength >= FineMaskStrength && region.Poll)
                useThreshold = 0;

            if (bright < useThreshold)
            {
                res.Note = "画面无高亮，跳过（高亮像素 " + bright + " < " + useThreshold + "）";
                if (nonBlack == 0)
                    res.Note += "；整张截图全黑 —— 游戏八成是独占全屏（改成“无边框窗口”），"
                              + "或者游戏以管理员运行而本程序不是（试试也用管理员运行），"
                              + "也可能是屏幕上有受保护的窗口挡住了桌面录制（见日志里的说明）";
                LogSkip(region, res.Note);
                return res;
            }

            float conf;
            string text = OcrEngine.Recognize(bin, out conf);

            // ---- 票数 / 影响力：默认那条路是"白底黑字"，而 HUD 上是亮的彩色数字 + 深色底，
            //      二值化会把它们当背景丢掉 —— 所以这里换一套激进的办法：
            //      按亮度把"亮的部分"当字、反色成黑字白底，多个阈值 × 多个放大倍数各认一遍，
            //      取认出的数字最多的那次结果。 ----
            if (IsTicketRegion(region))
            {
                float aconf;
                string aggr = AggressiveBrightText(region, _lastShot, out aconf);
                if (!string.IsNullOrEmpty(aggr) && aggr.Trim().Length > 0)
                {
                    if (text == null || aggr.Length >= text.Trim().Length)
                    {
                        res.Note = "激进识别：" + aggr.Trim();
                        text = aggr;
                        conf = aconf;
                    }
                }
            }

            res.DidOcr = true;
            // 调过引擎就进冷静期，不管有没有认到东西。
            // 准备阶段的角色快速扫描（fastMode）不进冷却 —— 否则 100ms 的轮询
            // 会被 3000ms 的冷却挡住，等于白设。
            // 「最大」档的冷静期是 0（见 CooldownForStrength）：那时节奏
            // 完全由"轮询间隔"决定。
            _cooldownUntil = fastMode ? DateTime.UtcNow
                                      : DateTime.UtcNow.AddMilliseconds(CooldownForStrength(Strength));

            // ---- 第一次没认好 → 用"原始灰度 + 反色 + 放大"再认一次 ----
            // 二值化那道硬阈值会把字的边缘切掉，Tesseract 只看到残缺字形；
            // 给它没被切过的灰度图（让它自己算 Otsu），识别率通常会好一截。
            // 只在结果确实很差时才多花这一次调用，正常情况不多花时间。
            // 灰度重试：识别强度开到「最大」时自动启用；也可以用它自己的开关强制打开。
            if ((Strength >= 3 || _cfg.OcrRetryRaw) && LooksWeak(text, conf))
            {
                Bitmap raw = null;
                try
                {
                    // 同一线程紧接着截图，缓冲还没被复用，直接用即可
                    raw = _lastShot == null ? null : RawForOcr(_lastShot, _cfg.OcrScale);
                    if (raw != null)
                    {
                        float conf2;
                        string text2 = OcrEngine.Recognize(raw, out conf2);
                        if (text2 != null && Normalize(text2).Length > Normalize(text).Length)
                        {
                            Logger.On("OCR", region.Name + "：二值化只认出 "
                                + OneLine(text) + "（" + conf.ToString("0.00") + "）→ "
                                + "改用灰度放大图重认：" + OneLine(text2)
                                + "（" + conf2.ToString("0.00") + "）");
                            text = text2;
                            conf = conf2;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Err("OCR", "灰度重试失败（忽略，用第一次的结果）", ex);
                }
                finally { if (raw != null) raw.Dispose(); }
            }

            // ---- SCP 血量表：多条路各认一遍，取"解析出最多条血量"的那次 ----
            //
            // 【为什么不能只用一条路】操作者说得很清楚：不同服务器配色不一样，
            //   让文字看清楚最重要，怎么认清楚怎么来。
            // 实测这块字只有 ~11px、还带服务器自己的色调，单一路径都不稳。
            // 所以这里把几种"让文字看清楚"的办法都试一遍，谁解析出的 SCP 行多就用谁：
            //   ① 原图（白字深底，Tesseract 自己算 Otsu 往往最稳）
            //   ② 灰度反色 + 放大 2 倍（浅字深底最有效的一条）
            //   ③ 颜色掩膜（把亮且有颜色倾向的字抠出来，压掉服务器的背景色偏）
            // 都用"整块文字"模式（psm 6），因为这是一张清单。
            if (region.Kind == OcrRegionKind.ScpHp)
            {
                float hconf;
                string best = PickBestHpText(region, _lastShot, text, out hconf);
                if (!string.IsNullOrEmpty(best) && best != text)
                {
                    text = best;
                    conf = hconf;
                    res.Note = "血量表多路识别：" + OneLine(text);
                }
            }

            if (text == null)
            {
                res.Note = "识别超时（>" + OcrEngine.TimeoutMs + "ms）或出错，本次丢弃";
                Logger.On("OCR", region.Name + "：识别超时或出错，本次丢弃");
                return res;
            }

            string flat = Normalize(text);
            res.Text = text;
            res.Note = flat.Length == 0 ? "没认出一个字" : "识别到：" + OneLine(text);

            Logger.On("OCR", region.Name + " [" + rc.X + "," + rc.Y + " " + rc.Width + "×" + rc.Height + "]"
                + " 置信度 " + conf.ToString("0.00", CultureInfo.InvariantCulture)
                + " 原文：" + OneLine(text));

            if (flat.Length == 0)
            {
                // 空白也是一次有效的“没识别到”观察，不能像以前那样直接丢掉。
                if (region.Kind == OcrRegionKind.Silence) res.SilenceScanned = true;
                return res;
            }

            // ---- 票数区：只做"读出数字"这一件事（需求八）----
            if (IsTicketRegion(region))
            {
                string val = ParseTicketValue(region.Tag, text);
                if (val == null)
                {
                    res.Note = "票数区认到文字但没读出数字：" + OneLine(text);
                    return res;
                }

                res.Note = "票数 " + region.Tag + " = " + val;
                _deathRoundHit = true;      // 这一轮读到了票数（判复活用）
                _deathTicketRoundHit = true;
                Logger.On("票数", region.Name + "（" + region.Tag + "）= " + val
                    + "（原文：" + OneLine(text) + "）");
                if (OnTicketRecognized != null)
                {
                    string tag = region.Tag;
                    string v = val;
                    // 【别再静默吞异常】这里以前是 `catch { }`。票数那条链路曾经
                    // 因为一个 KeyNotFoundException 整天都在挂，却一行日志都没有
                    // —— 表现只是"面板上那行一直是空的"，完全看不出哪里坏了。
                    if (_runOnUi != null)
                        _runOnUi(delegate
                        {
                            try { OnTicketRecognized(tag, v, _deathScan); }
                            catch (Exception ex) { Logger.Err("票数", "写入战术数据失败", ex); }
                        });
                    else
                    {
                        try { OnTicketRecognized(tag, v, _deathScan); }
                        catch (Exception ex) { Logger.Err("票数", "写入战术数据失败", ex); }
                    }
                }
                return res;
            }

            // ---- 静音开关：只判断"还看不看得见"（三个都认不出 = 复活了）----
            if (region.Kind == OcrRegionKind.Silence)
            {
                // "静音存活玩家 / 静音观察者 / 静音音效" 或 "Toggle reinforcement interface"
                // 只要认出任意一个，就说明死亡界面还在 → 还没复活。
                bool saw = flat.IndexOf("静音") >= 0 || flat.IndexOf("mute") >= 0
                        || flat.IndexOf("toggle") >= 0 || flat.IndexOf("reinforcement") >= 0
                        || flat.IndexOf("增强") >= 0;
                res.Note = saw ? "还看得见静音开关" : "没认出静音开关（可能已经复活）";
                res.SilenceScanned = true;
                res.SawSilence = saw;
                if (saw) _deathRoundHit = true;     // 静音开关还在 = 死亡界面还在（判复活用）
                return res;
            }

            // ---- 左侧 SCP 列表：认出这局有哪些 SCP ----
            if (region.Kind == OcrRegionKind.ScpList)
            {
                List<string> codes = OcrTacticalLink.FindAllCodes(flat);
                res.Note = codes.Count == 0
                    ? "左侧列表里没认出 SCP 编号" : ("认出 SCP：" + string.Join("、", codes.ToArray()));
                if (codes.Count > 0 && OnScpListScan != null)
                {
                    string joined = string.Join(",", codes.ToArray());
                    if (_runOnUi != null) _runOnUi(delegate { try { OnScpListScan(joined); } catch { } });
                    else { try { OnScpListScan(joined); } catch { } }
                }
                return res;
            }

            // ---- 第 4 批：屏幕中心角色区已删除，不再有"我是谁"的 OCR 分支 ----

            // 游戏不在前台：左下角播报区截到的是聊天/浏览器/桌面文字，
            // 绝不能当成 CASSIE 播报（实测会把聊天里的"终焉"读成终焉协议）。
            if (allowAlerts && region.Kind == OcrRegionKind.Broadcast && !IsGameForeground())
            {
                res.Note = "游戏不在前台 → 跳过播报识别（防止读到聊天/浏览器文字）";
                return res;
            }

            // ---- 终焉播报要连续确认：单帧误识别不触发，也不被 dedup 吃掉 ----
            DateTime now = DateTime.UtcNow;
            bool deadManFrame = allowAlerts && region.Kind == OcrRegionKind.Broadcast
                && flat.Length > 0 && ReDeadMan.IsMatch(flat) && !ReOwnDeadManOverlay.IsMatch(flat);
            if (deadManFrame)
            {
                if ((now - _deadManCandidateAt).TotalSeconds > DeadManConfirmWindowSeconds)
                    _deadManCandidateHits = 0;
                // 同一帧可能被多条扫描路径读到：300ms 内只算一次
                if ((now - _deadManCandidateAt).TotalMilliseconds >= 300)
                    _deadManCandidateHits++;
                _deadManCandidateAt = now;
            }
            else if ((now - _deadManCandidateAt).TotalSeconds > 3)
            {
                // 字幕消失/换成别的话超过 3 秒 → 候选中断，重新数
                _deadManCandidateHits = 0;
            }

            // ---- 同一段文字挂在屏幕上时不要每轮都重来一遍 ----
            // 终焉候选要绕过 dedup，否则同一句干净字幕第二帧就被 60 秒去重挡掉，
            // 永远凑不够确认次数。
            if (dedup && !deadManFrame && flat == _lastText && (now - _lastTextAt).TotalMilliseconds < SameTextWindowMs)
            {
                _lastTextAt = now;
                res.Note = "与上一段相同，跳过重复处理";
                return res;
            }
            _lastText = flat;
            _lastTextAt = now;

            // ---- 第一路：自动填写 / 校准战术数据（所有区域都走这里）----
            ApplyTactical(region, text);

            // ---- 第二路：播报关键词 → 现有的悬浮窗（只有播报区才做）----
            if (allowAlerts && region.Kind == OcrRegionKind.Broadcast)
                DispatchAlerts(flat, text, conf);

            return res;
        }

        /// <summary>
        /// 这个区域是不是"票数 / 影响力"框。
        ///
        /// 【为什么看 tag 也要算】默认配置里的 4 个票数框，用途写的是 generic、
        /// 只有 Tag 是 mtf_tokens / chaos_influence 这些。老代码只认 Kind=Ticket，
        /// 结果这 4 个框整个走的是"通用识别"，票数专用那套（不套白像素预检、
        /// 用颜色掩膜认数字）一条都没生效 —— 这就是票数一直认不准的根子。
        /// 名字里带"令牌 / 影响力 / 票数"的，加载配置时会被自动改成 Ticket；
        /// 这里再兜一层 Tag，用户手工写 Tag 也照样生效。
        /// </summary>
        private static bool IsTicketRegion(OcrRegion region)
        {
            if (region == null) return false;
            if (region.Kind == OcrRegionKind.Ticket) return true;
            string tag = region.Tag == null ? "" : region.Tag.ToLowerInvariant();
            return tag.IndexOf("_tokens") >= 0 || tag.IndexOf("_influence") >= 0
                || tag.IndexOf("token") >= 0 || tag.IndexOf("influence") >= 0;
        }

        /// <summary>把识别结果里的战术信息（阵亡 / 刷新增援 / 血量 / 发电机）落到战术面板上。</summary>
        private void ApplyTactical(OcrRegion region, string text)
        {
            List<TacticalUpdate> ups;
            try
            {
                ups = _link.Evaluate(region, text);
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "解析识别结果失败", ex);
                return;
            }
            if (ups == null || ups.Count == 0) return;

            // 模型改动必须回到 UI 线程做：那边同时还有用户在点面板
            if (_runOnUi != null) _runOnUi(delegate { ApplyTacticalOnUi(region, ups); });
            else ApplyTacticalOnUi(region, ups);
        }

        private void ApplyTacticalOnUi(OcrRegion region, List<TacticalUpdate> ups)
        {
            try
            {
                string summary = _link.Apply(ups);
                if (_quietScan) { summary = ""; }   // 手动扫描不写面板数据
                if (!string.IsNullOrEmpty(summary))
                {
                    Logger.On("战术", "按 " + region.Name + " 的识别结果自动填写：" + summary);
                    // 让左下角那块提示的"动作"行显示战术结果 ——
                    // 否则"发电机已标记"这种明明生效了，提示却写"没匹配任何规则"。
                    _lastTacticalSummaryAt = DateTime.UtcNow;
                    ShowToast(null, "战术：" + summary);
                }
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "套用识别结果失败", ex);
            }
        }

        /// <summary>
        /// 记录“这一轮为什么没识别”。
        /// 必须节流：定时轮询 1.5 秒一轮，照直写会把日志刷爆。
        /// 只在原因变了、或者距上次记录超过 30 秒时才写一条。
        /// </summary>
        private string _lastSkipNote = "";
        private DateTime _lastSkipAt = DateTime.MinValue;

        private void LogSkip(OcrRegion region, string note)
        {
            DateTime now = DateTime.UtcNow;
            if (note == _lastSkipNote && (now - _lastSkipAt).TotalSeconds < 30) return;
            _lastSkipNote = note;
            _lastSkipAt = now;
            LastInfo = region.Name + "：" + note;
            Logger.On("OCR", region.Name + "：" + note);
        }

        /// <summary>
        /// 从核弹播报里读秒数（需求四）："T-80 seconds" / "80秒" / "T minus 90"。
        /// 只在这个分支里调用（已经确认是核弹播报），所以宽松一点没关系。
        /// </summary>
        private static bool TryReadNukeSeconds(string raw, out int seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            Match m = ReNukeSeconds.Match(raw);
            if (!m.Success)
            {
                // 兜底：只认「倒计时 / 引爆 / 自毁」后面紧跟着的那组字符，
                // 避免把整段里别处的数字当成核弹秒数。
                Match loose = ReNukeSecondsLoose.Match(raw);
                if (!loose.Success || !Regex.IsMatch(loose.Groups[1].Value, "[0-9]")) return false;
                int lv;
                if (!int.TryParse(FixOcrDigits(loose.Groups[1].Value), out lv)) return false;
                if (lv <= 0 || lv > 600) return false;
                seconds = lv;
                return true;
            }

            int v;
            if (!int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out v))
                return false;

            if (v <= 0 || v > 600) return false;   // 明显不是倒计时
            seconds = v;
            return true;
        }

        /// <summary>这条播报里到底有没有可用的倒计时秒数（不关心具体值）。</summary>
        private static bool HasNukeSeconds(string raw)
        {
            int seconds;
            return TryReadNukeSeconds(raw, out seconds);
        }

        /// <summary>OCR 常见数字误认：8O → 80、l5 → 15、S0 → 50。</summary>
        private static string FixOcrDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == 'o' || c == 'O') sb.Append('0');
                else if (c == 'i' || c == 'I' || c == 'l' || c == 'L') sb.Append('1');
                else if (c == 's' || c == 'S') sb.Append('5');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 从净化播报里读出"播报说的是多少秒"（需求五）：
        /// "15分钟" / "15 minutes" / "03:20" / "200秒"。
        /// 读出的是播报值，还要过 MapDeconAnnounce 才是现实秒数。
        ///
        /// ⚠ 【现在不再用它算了】操作者定稿：轻收容净化从"净化程序将在倒计时 N 分钟后进行"
        /// 那句开始计时，而那个 N 只是播报值（OCR 还常认错），所以一律按
        /// `DeconBaseSeconds` 起表。这两个方法留着是为了别的服 / 以后要按表映射时能直接用，
        /// 当前主流程不调用它们。
        /// </summary>
        private static bool TryReadDeconSeconds(string raw, out int announced)
        {
            announced = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            Match m;

            // ① mm:ss
            m = ReDeconClock.Match(raw);
            if (m.Success)
            {
                int a, b;
                if (int.TryParse(m.Groups[1].Value, out a)
                    && int.TryParse(m.Groups[2].Value, out b) && b < 60)
                {
                    announced = a * 60 + b;
                    return announced > 0;
                }
            }

            // ② "15分钟" / "15 minutes" / "15 min"
            m = ReDeconMinutes.Match(raw);
            if (m.Success)
            {
                int a;
                if (int.TryParse(m.Groups[1].Value, out a) && a > 0 && a <= 60)
                {
                    announced = a * 60;
                    return true;
                }
            }

            // ③ "200秒"
            m = ReDeconPlainSeconds.Match(raw);
            if (m.Success)
            {
                int a;
                if (int.TryParse(m.Groups[1].Value, out a) && a > 0 && a <= 3600)
                {
                    announced = a;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 播报时间 → 现实秒数（需求五.3）。
        /// 先查配置里的对照表（format: "15m=652,10m=456,30s=30"），
        /// 表里没有就原样使用播报值。
        /// </summary>
        private bool MapDeconAnnounce(int announced, out int mapped)
        {
            mapped = announced;
            string table = _cfg.DeconAnnounceMap;
            if (string.IsNullOrEmpty(table)) return announced > 0;

            string[] parts = table.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string one = parts[i].Trim();
                if (one.Length == 0) continue;

                int eq = one.IndexOf('=');
                if (eq <= 0 || eq >= one.Length - 1) continue;

                string left = one.Substring(0, eq).Trim().ToLowerInvariant();
                string right = one.Substring(eq + 1).Trim();

                string digits = left.TrimEnd('m', 's', '分', '秒');
                bool isMinutes = left.EndsWith("m") || left.EndsWith("分");
                bool isSeconds = left.EndsWith("s") || left.EndsWith("秒");

                int key;
                if (!int.TryParse(digits, out key) || key <= 0) continue;
                int keySeconds = isMinutes ? key * 60 : key;

                // 没写单位的，两个都试
                if (!isSeconds && !isMinutes)
                {
                    if (key == announced) { int v; if (int.TryParse(right, out v) && v > 0) { mapped = v; return true; } }
                    if (key * 60 == announced) { int v; if (int.TryParse(right, out v) && v > 0) { mapped = v; return true; } }
                    continue;
                }

                if (keySeconds != announced) continue;

                int real;
                if (int.TryParse(right, out real) && real > 0)
                {
                    mapped = real;
                    return true;
                }
            }

            return announced > 0;
        }

        /// <summary>把识别结果映射成现有的 AlertManager 调用（在 UI 线程上执行）。</summary>
        private void DispatchAlerts(string norm, string raw, float conf)
        {
            // 手动扫描（安静模式）只记录识别结果，不弹任何真提示
            if (_quietScan) return;

            Action act = null;
            string what = null;

            // ---- 终焉协议（亡者协议）的播报：认到就以屏幕为准 ----
            // 操作者要求："识别『终』就差不多了，用于音频的条件" ——
            // 所以关键词放宽到 终焉 / 亡者协议 / 死手 / dead man，另外
            // "终" 后面跟 协议 / 启动 / 开始 / 已授权 也算（OCR 常把"焉"丢掉）。
            // 认到之后：① 亡者协议倒计时直接起表（比"两方令牌都用尽"推断更准）；
            //          ② 给自定义触发点一个状态/脉冲条件。
            if (act == null && ReDeadMan.IsMatch(norm) && !ReOwnDeadManOverlay.IsMatch(norm))
            {
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - _deadManFiredAt).TotalSeconds < 30)
                {
                    LastInfo = "终焉播报已经确认过，忽略同一段的后续变体：" + OneLine(raw);
                    return;
                }
                if (_deadManCandidateHits < DeadManConfirmHits)
                {
                    LastInfo = "终焉播报候选（" + _deadManCandidateHits + "/" + DeadManConfirmHits
                        + " 次）—— 先不触发，防单帧误识别";
                    Logger.On("播报", "终焉播报只读到 " + _deadManCandidateHits + "/" + DeadManConfirmHits
                        + " 次，先不触发（防单帧误识别）：" + OneLine(raw));
                    return;
                }
                _deadManFiredAt = nowUtc;
                _deadManCandidateHits = 0;
                act = delegate { _mgr.NoteDeadManBroadcastByOcr(OneLine(raw)); };
                what = "终焉协议（亡者协议）播报（已连续确认 " + DeadManConfirmHits + " 次）";
            }

            // 同一段文字里可能同时留着旧的「取消」和新的「重新启动，倒计时 N 秒」：
            // 有重启字样 + 秒数时以新的倒计时为准，不能因为看到“取消”就把新表停掉。
            bool nukeCancel = ReNukeCancel.IsMatch(norm);
            bool nukeRestartWithSeconds = ReNukeRestart.IsMatch(norm) && HasNukeSeconds(raw);
            if (act == null && nukeCancel && !nukeRestartWithSeconds)
            {
                // 「引爆程序已被取消」：游戏里核弹已经停了，这里必须跟着停表。
                // 不要求秒数 —— 这句本来就没有秒数。
                StopNukeWatch("读到核弹取消播报");
                act = delegate { _mgr.CancelWarheadByOcr(); };
                what = "核弹引爆已取消（按播报停表，保留剩余秒数）";
            }
            else if (ReNuke.IsMatch(norm))
            {
                // 操作者要求：以识别到的"X 秒后自毁"为准。
                // 必须同时满足"倒计时字样"+"具体秒数"，否则什么都不做
                // （以前没秒数就按配置时长重新起算，会被 CHECKPOINT/TESLA 那类
                //   播报反复重置 —— 见 ReNukeCountdown 的注释）。
                int announced = 0;
                bool hasCountdownWord = ReNukeCountdown.IsMatch(norm);
                bool hasSeconds = TryReadNukeSeconds(raw, out announced);

                if (hasCountdownWord && hasSeconds)
                {
                    // 核弹都是整十秒（90/80/120…），OCR 差个几秒很正常 →
                    // 就近归到十位上，比"照读"稳得多。
                    int sec2 = SnapToTen(announced);
                    StopNukeWatch("已经读到倒计时时间 " + announced + " 秒");
                    act = delegate { _mgr.SyncWarheadByOcr(sec2); };
                    what = "核弹倒计时（按播报 " + announced + " 秒 → 取整 " + sec2 + " 秒）";
                }
                else
                {
                    // 第一段："核弹已启用"（还没给时间）→ 不弹窗，只把播报区扫快一点，
                    // 最多盯 10 秒等第二段；那种只提到核弹的杂播报也跟着一起被忽略掉。
                    //
                    // 【踩过的坑】这里以前是 `else if` 链条的第一段，于是"提到核弹"
                    // 就把后面所有规则都挡掉了 —— MTF 部队代号里带 ALPHA 的击杀播报
                    //（"SCP-049 已被 ALPHA-87 成功收容"）整条被吞，击杀既没提示也没记账。
                    // 现在只是"顺手开始盯核弹"，下面的阵亡 / 净化规则照常判。
                    StartNukeWatch();
                    Logger.On("播报", "提到核弹但"
                        + (hasSeconds ? "没写「自毁/引爆」这类字样" : "没读到秒数")
                        + " → 不弹窗，改为加快扫描 10 秒等第二段：" + OneLine(raw));
                }
            }

            // 【三段净化规则都要"整段够短"这道闸】
            // 操作者机器上经常把一整段长文字（聊天记录 / 我们的对话 / 说明文字）
            // 当成播报读进来，那种文本里凑巧出现"开始清除""净化"就会误判 ——
            // 日志实锤：`也 在 开始 清除 所 a ez 我 觉得 可 以 给 7B,…` 触发了"归零并锁住"，
            // 于是那一局（以及后面的局）那句真正的净化播报被 `if (_deconLocked) return;`
            // 顶掉，通知里只剩"轻收容净化已开始"、没有时间。
            // 真正的净化播报都很短（十几二十个字），所以这里卡一个长度上限。
            bool deconShort = norm.Length <= DeconTextMaxLen;

            // 与 LooksLikeDeconPurge 同一套判定：长度闸 + 关键词
            if (act == null && deconShort && ReDeconPurge.IsMatch(norm))
            {
                // 真的开始清生物了 → 净化归零并锁死
                act = delegate { _mgr.NoteDeconCalibLockByOcr(DateTime.Now); _mgr.LockDeconPurgeByOcr(); };
                what = "轻收容净化已开始（倒计时归零并锁住）";
            }
            else if (act == null && deconShort && ReDeconStart.IsMatch(norm))
            {
                // 【操作者口径】"所有生物必须立即转移 / 所有人员应该立即转移"
                // 是净化开始计时的标志（不是结束）：从这一刻按基础时长往下走，
                // 之后"现在开始清除所有生物"会把倒计时归零并锁死。
                int sec = _cfg.DeconBaseSeconds;
                act = delegate { _mgr.StartDeconCountdownByOcr(sec); };
                what = "净化开始计时（倒计时从 " + sec + " 秒起算）";
            }
            else if (act == null && deconShort && ReDecon.IsMatch(norm))
            {
                // 【操作者口径（定稿）】轻收容净化不是回合一开始就计时的 ——
                // 它从 C.A.S.S.I.E 这句开始：
                //     「轻收容区域的净化程序将在倒计时 15 分钟后进行」
                // 所以这里：
                //   · 只看关键词（净化 / 轻收容 / 封锁…），认到就开始计时；
                //   · 不理会播报里的那个数字（OCR 认不准，而且它不是现实时间），
                //     一律按「净化基础时长」`DeconBaseSeconds`（默认 705 秒）起表；
                //   · 起过表之后不再重置（见 AlertManager.StartDeconCountdownByOcr）。
                // 之后「现在开始清除所有生物」那句会把倒计时归零并锁死。
                int sec = _cfg.DeconBaseSeconds;
                act = delegate { _mgr.StartDeconCountdownByOcr(sec); };
                what = "轻收容净化开始计时（关键词命中，从 " + sec + " 秒起算，忽略播报里的分钟数）";
            }
            else if (act == null && ReScp.IsMatch(norm) && ReScpDead.IsMatch(norm))
            {
                // 需求一：只有明确认出击杀者（玩家名或阵营名）才弹提示。
                // 认不出来就整条忽略 —— 不要弹"【未知】击杀了 SCP-XXX"。
                OcrTacticalLink.KillInfo ki;
                if (!OcrTacticalLink.TryParseKill(raw, out ki))
                {
                    LastInfo = "认出阵亡播报，但连 SCP 编号都没认出来 → 忽略：" + OneLine(raw);
                    Logger.On("播报", "忽略：连编号都没认出来（" + OneLine(raw) + "）");
                    ShowToast(OneLine(raw), "认不出是哪个 SCP，已忽略");
                    return;
                }

                OcrTacticalLink.KillInfo info = ki;
                if (info.HasKiller)
                {
                    // 认出了谁杀的（玩家名 / 部队编号 / 阵营）
                    act = delegate
                    {
                        _mgr.ShowScpKilledByOcr(info.Code, info.Killer, info.Faction);
                    };
                    what = "SCP 阵亡提示（" + info.Killer + "）";
                }
                else
                {
                    // 认不出击杀者 → 换被动语态的提示（"SCP-XXX 已被击杀（击杀者未知）"）
                    act = delegate
                    {
                        _mgr.ShowScpKilledUnknownByOcr(info.Code, info.Killer);
                    };
                    what = "SCP 被击杀（击杀者未知）";
                }
            }

            string oneLine = OneLine(raw);
            if (oneLine.Length > 60) oneLine = oneLine.Substring(0, 60) + "…";

            if (act == null)
            {
                LastInfo = "认出文字但没命中播报关键词：" + oneLine;
                // 战术那边刚刚填过东西的话，别把它的结果覆盖成"没命中"（发电机就是这样）
                if ((DateTime.UtcNow - _lastTacticalSummaryAt).TotalSeconds > 3)
                    ShowToast(oneLine, "");     // 也要让用户看见"确实认到字了"
                return;
            }

            LastInfo = "命中 " + what + "（置信度 " + conf.ToString("0.00", CultureInfo.InvariantCulture) + "）：" + oneLine;
            Logger.On("播报", "触发 " + what + "（置信度 "
                + conf.ToString("0.00", CultureInfo.InvariantCulture) + "）：" + oneLine);
            ShowToast(oneLine, what);
            if (_runOnUi != null) _runOnUi(act);
        }

        /// <summary>最近一次战术自动填写的时间：刚写过就别再用"没匹配任何规则"覆盖提示。</summary>
        private DateTime _lastTacticalSummaryAt = DateTime.MinValue;

        /// <summary>
        /// "手动扫描"期间为 true：只做识别、只显示结果，
        /// 不触发那些真正的提示（核弹 / 净化 / 刷新增援 / 击杀…）。
        /// 用户点"立即扫描"只是想看看认得准不准，不想被一堆提示糊一脸。
        /// </summary>
        private bool _quietScan;

        /// <summary>
        /// 左下角那块"识别到什么、做了什么"的小提示。
        /// 由 Program 接到 OcrToastForm 上；回调会切到 UI 线程执行。
        /// </summary>
        public Action<string, string> OnCaptured;

        private void ShowToast(string text, string action)
        {
            if (OnCaptured == null) return;
            if (_runOnUi == null) { try { OnCaptured(text, action); } catch { } return; }
            _runOnUi(delegate { try { OnCaptured(text, action); } catch { } });
        }

        /// <summary>识别区域：默认 X=0、Y=屏幕高度-400、宽 1000、高 400（屏幕左下角那一条）。</summary>
        public static Rectangle Region(Config cfg)
        {
            if (cfg == null || cfg.OcrRegions == null || cfg.OcrRegions.Count == 0)
                return new Rectangle(0, 0, 0, 0);
            return cfg.OcrRegions[0].Rect();
        }

        private Buf EnsureBuffer(int w, int h)
        {
            int key = (w << 16) | (h & 0xFFFF);
            Buf b;
            if (_bufs.TryGetValue(key, out b)) return b;

            // 尺寸太多说明配置很乱，清一遍再从零建，免得一直占着内存
            if (_bufs.Count > 8)
            {
                foreach (KeyValuePair<int, Buf> kv in _bufs) kv.Value.Release();
                _bufs.Clear();
            }

            b = new Buf();
            b.Shot = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            b.Bin = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            b.G = Graphics.FromImage(b.Shot);
            _bufs[key] = b;
            return b;
        }

        private void ReleaseBuffers()
        {
            lock (_gate)
            {
                foreach (KeyValuePair<int, Buf> kv in _bufs) kv.Value.Release();
                _bufs.Clear();
            }
        }

        private Bitmap Capture(Rectangle rc, out int bright, out int nonBlack)
        {
            bright = 0;
            nonBlack = 0;
            Buf b;
            lock (_gate) { b = EnsureBuffer(rc.Width, rc.Height); }

            try
            {
                b.G.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(rc.Width, rc.Height),
                    CopyPixelOperation.SourceCopy);
            }
            catch { return null; }

            _lastShot = b.Shot;
            bright = Binarize(b.Shot, b.Bin, out nonBlack);
            return b.Bin;
        }

        /// <summary>
        /// 把原始截图（浅色字 + 深色背景）转成"深色字 + 浅色底"的灰度图，并放大。
        ///
        /// 为什么要它：二值化用的是我手写的固定阈值，字被场景压暗、或者
        /// 半透明叠得厉害时，阈值一划就把字的边缘切掉，Tesseract 收到的
        /// 就是残缺的字形。给它没被切过的灰度图，让它自己的 Otsu
        /// 去决定阈值，往往能多认出几个字。
        ///
        /// 放大也有用：Tesseract 最喜欢的字高大约 30px，游戏在 1080p 下的
        /// 播报字经常只有十几像素，放大 2 倍之后识别率明显好一些。
        /// </summary>
        private static Bitmap RawForOcr(Bitmap shot, int scale)
        {
            if (shot == null) return null;
            if (scale < 1) scale = 1;

            // 别把图放得太大：500ms 的超时很容易被大图吃掉
            // （注意：编译器是 C# 5，不能用 3_000_000 这种数字分隔符）
            long area = (long)shot.Width * shot.Height * scale * scale;
            while (scale > 1 && area > 3000000L)
            {
                scale--;
                area = (long)shot.Width * shot.Height * scale * scale;
            }

            int w = shot.Width * scale;
            int h = shot.Height * scale;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // 先反色（浅字深底 → 深字浅底），再画到放大后的画布上
                ImageAttributes ia = new ImageAttributes();
                ia.SetColorMatrix(new ColorMatrix(new float[][]
                {
                    new float[] { -1,  0,  0, 0, 0 },
                    new float[] {  0, -1,  0, 0, 0 },
                    new float[] {  0,  0, -1, 0, 0 },
                    new float[] {  0,  0,  0, 1, 0 },
                    new float[] {  1,  1,  1, 0, 1 }   // 反色后的偏移（-v + 255）
                }));
                g.DrawImage(shot, new Rectangle(0, 0, w, h),
                    0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel, ia);
                ia.Dispose();
            }
            return dst;
        }

        /// <summary>
        /// 第一次识别结果够不够好。不够就值得用灰度图再试一次。
        /// "够好"= 认出了内容，而且置信度不算太低。
        /// </summary>
        private static bool LooksWeak(string text, float conf)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string flat = Normalize(text);
            if (flat.Length == 0) return true;
            if (flat.Length <= 2) return true;          // 只认出一两个字，多半是残片
            if (conf < 0.35f) return true;              // 置信度太低
            return false;
        }

        /// <summary>
        /// 一趟扫完：数高亮像素 + 直接把画面压成“黑字白底”。
        /// 用 LockBits + 内存指针，400K 像素大约 1~2ms，不会掉帧。
        /// 顺带二值化的好处是 OCR 只看得到文字，背景画面全被抹平。
        ///
        /// nonBlack = 非纯黑像素的个数。一个都没有说明整张截图全黑 ——
        /// 那是被系统挡了（独占全屏，或者游戏以管理员运行而本程序不是），
        /// 必须能分辨出来，否则用户只会看到“没反应”。
        /// </summary>
        /// <summary>各强度档的取值：是否放宽颜色判定、白像素预检门槛。</summary>
        private static void StrengthParams(int s, out bool loose, out bool veryLoose, out int brightThreshold)
        {
            if (s <= 0) { loose = false; veryLoose = false; brightThreshold = 320; return; }   // 保守
            if (s == 1) { loose = true; veryLoose = false; brightThreshold = 200; return; }    // 标准
            if (s == 2) { loose = true; veryLoose = true; brightThreshold = 90; return; }      // 激进（默认）

            // 最大：颜色判定跟激进一样，但白像素预检几乎不挡，
            // 而且会启用"灰度图重试"（见 ScanRegion）—— 最慢、最不容易漏。
            loose = true; veryLoose = true; brightThreshold = 40;
        }

        /// <summary>识别强度（0/1/2/3）。静态：Binarize 是静态的，预览和正式扫描共用一份。</summary>
        public static int Strength = 2;

        /// <summary>用"色相掩膜 + 平滑 + Otsu"这套精细算法的最低强度档（3 = 最大）。</summary>
        public const int FineMaskStrength = 3;

        /// <summary>
        /// 精细那套（色相掩膜 + 3×3 平滑 + Otsu）要不要用。
        ///
        /// 【为什么只在"最大"档开】那套要按色相量一遍整块区域的色差、再做平滑和
        /// 直方图，比原来的"亮度硬阈值扫 8 次"精细但更费 CPU。默认的"激进"档
        /// 是给日常跑图用的，帧率优先；用户主动选到"最大"就是明确表态
        /// "宁可慢一点也要认准"，这时候才整套打开。
        /// 低档位下票数框走原来的亮度阈值老办法（LegacyBrightText）。
        /// </summary>
        public static bool UseFineTicketMask
        {
            get { return Strength >= FineMaskStrength; }
        }

        private static unsafe int Binarize(Bitmap src, Bitmap dst, out int nonBlack)
        {
            bool loose, veryLoose;
            int ignoredThreshold;
            StrengthParams(Strength, out loose, out veryLoose, out ignoredThreshold);

            int w = src.Width;
            int h = src.Height;
            int bright = 0;
            int nonBlackCount = 0;

            BitmapData sd = null;
            BitmapData dd = null;
            try
            {
                sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                for (int y = 0; y < h; y++)
                {
                    byte* sp = (byte*)sd.Scan0 + y * sd.Stride;
                    byte* dp = (byte*)dd.Scan0 + y * dd.Stride;

                    for (int x = 0; x < w; x++)
                    {
                        int b = sp[0];
                        int g = sp[1];
                        int r = sp[2];
                        sp += 4;

                        if ((r | g | b) != 0) nonBlackCount++;

                        // 高亮 = 白字 / 偏浅蓝的播报底色。
                        // loose / veryLoose 是识别强度放出来的余量：
                        //   · loose     —— 亮度门槛整体下压，能兜住被场景压暗的字
                        //   · veryLoose —— 再放宽一档，连带色的浅字也算上
                        bool hot =
                            // 灰白字（三个通道都亮，且彼此接近）
                            (r > (loose ? 190 : 220) && g > (loose ? 190 : 220) && b > (loose ? 190 : 220))
                            // 浅蓝底色 / 蓝色标题字
                         || (r > (veryLoose ? 80 : 110) && g > (veryLoose ? 130 : 170) && b > (veryLoose ? 155 : 195))
                            // 激进档再补一条：整体够亮且偏冷色，兜住半透明叠出来的字
                         || (veryLoose && r > 150 && g > 160 && b > 170);

                        if (hot)
                        {
                            bright++;
                            dp[0] = 0; dp[1] = 0; dp[2] = 0;        // 文字 → 黑
                        }
                        else
                        {
                            dp[0] = 255; dp[1] = 255; dp[2] = 255;  // 背景 → 白
                        }
                        dp += 3;
                    }
                }
            }
            finally
            {
                if (sd != null) { try { src.UnlockBits(sd); } catch { } }
                if (dd != null) { try { dst.UnlockBits(dd); } catch { } }
            }

            nonBlack = nonBlackCount;
            return bright;
        }

        /// <summary>只要高亮像素数（预览 / 测试用）。</summary>
        private static int Binarize(Bitmap src, Bitmap dst)
        {
            int ignored;
            return Binarize(src, dst, out ignored);
        }

        /// <summary>转小写并去掉所有非字母数字汉字，顺便把 OCR 认出来的空格噪声抹掉。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToLowerInvariant(s[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c >= '\u4e00' && c <= '\u9fff'))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>把多行识别结果压成一行，给日志和提示用。</summary>
        public static string OneLine(string s)
        {
            if (s == null) return "";
            string t = s.Replace("\r", " ").Replace("\n", " ");
            while (t.IndexOf("  ") >= 0) t = t.Replace("  ", " ");
            return t.Trim();
        }

        /// <summary>设置窗口里的“预览识别”：立刻照这个区域截一张、识别一遍。</summary>
        public static string PreviewRegion(Config cfg, OcrRegion region, out int bright, out string info)
        {
            bright = 0;
            info = "";
            if (region == null) { info = "没有这个区域。"; return null; }

            // 预览要跟正式扫描用同一档算法：这里自己同步一次识别强度，
            // 免得用户刚在设置里把强度改成「最大」、还没保存生效就来预览，
            // 看到的是旧档位的结果。
            Strength = cfg == null ? Strength : cfg.OcrStrength;

            Rectangle rc = region.Rect();
            if (rc.Width <= 0 || rc.Height <= 0) { info = "识别区域无效。"; return null; }

            Bitmap shot = null;
            Bitmap bin = null;
            Graphics g = null;
            try
            {
                shot = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb);
                bin = new Bitmap(rc.Width, rc.Height, PixelFormat.Format24bppRgb);
                g = Graphics.FromImage(shot);
                g.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(rc.Width, rc.Height),
                    CopyPixelOperation.SourceCopy);

                bright = Binarize(shot, bin);

                // 票数 / 影响力框：白像素预检会把"彩色数字压深底"整片挡掉，
                // 所以这里跟正式扫描一样跳过预检、直接上颜色掩膜，让预览看到的
                // 就是真正的识别结果（用户才能判断框得准不准）。
                bool ticket = IsTicketRegion(region);

                if (!ticket && bright < BrightPixelThreshold)
                {
                    info = "区域 " + region.Name + "：" + rc.Width + "×" + rc.Height + "，高亮像素只有 " + bright
                         + " 个（阈值 " + BrightPixelThreshold + "）。\n"
                         + "实际运行时这一步就直接返回了，不会调用 OCR。";
                    return null;
                }

                OcrEngine.EnsureReady(TessdataPath(cfg.OcrBestModel), cfg.OcrLang);
                if (!OcrEngine.IsReady)
                {
                    info = "引擎还在初始化：" + OcrEngine.Status + "\n稍等一两秒再点一次。";
                    return null;
                }

                float conf;
                string text;
                if (ticket)
                {
                    string how;
                    float aconf;
                    if (UseFineTicketMask)
                    {
                        // 最大档：跟正式扫描一样走颜色掩膜，认不出再退回老办法
                        text = TicketMaskText(region, shot, out aconf);
                        how = "颜色掩膜（色相 + 平滑 + Otsu）";
                        if (!IsGoodTicketText(region, text, aconf))
                        {
                            float lconf;
                            string legacy = LegacyBrightText(shot, out lconf);
                            if (IsGoodTicketText(region, legacy, lconf)) { text = legacy; aconf = lconf; }
                        }
                    }
                    else
                    {
                        // 低档位：跟正式扫描一样用原来的亮度阈值老办法
                        text = LegacyBrightText(shot, out aconf);
                        how = "亮度阈值（低档位用；换成「最大」会用颜色掩膜）";
                    }
                    conf = aconf;
                    info = "区域 " + region.Name + "：" + rc.Width + "×" + rc.Height
                         + "（票数框：" + how + "；不套高亮像素预检，实测高亮 "
                         + bright + " 个）"
                         + "\n平均置信度 " + conf.ToString("0.00", CultureInfo.InvariantCulture)
                         + "\n引擎：" + OcrEngine.Status;
                    Logger.On("预览", region.Name + " 预览识别（票数）：" + OneLine(text));
                    return text;
                }

                text = OcrEngine.Recognize(bin, out conf);
                if (text == null)
                {
                    info = "识别超时（>" + OcrEngine.TimeoutMs + "ms）或出错。\n引擎状态：" + OcrEngine.Status;
                    return null;
                }

                Logger.On("预览", region.Name + " 预览识别：" + OneLine(text));

                info = "区域 " + region.Name + "：" + rc.Width + "×" + rc.Height
                     + "，高亮像素 " + bright
                     + "，平均置信度 " + conf.ToString("0.00", CultureInfo.InvariantCulture)
                     + "\n引擎：" + OcrEngine.Status;

                // 顺手算一下这一段能识别出哪些战术数据（只是预览，不改任何数据）
                try
                {
                    int n = new OcrTacticalLink(cfg, null).Evaluate(region, text).Count;
                    if (n > 0)
                        info += "\n这一段能识别出 " + n + " 条战术数据（按“立即扫描”时才会真正填进去）";
                }
                catch { }

                return text;
            }
            catch (Exception ex)
            {
                info = "预览失败：" + ex.Message;
                return null;
            }
            finally
            {
                if (g != null) { try { g.Dispose(); } catch { } }
                if (bin != null) { try { bin.Dispose(); } catch { } }
                if (shot != null) { try { shot.Dispose(); } catch { } }
            }
        }

        /// <summary>兼容旧调用：预览第 1 组（播报）区域。</summary>
        public static string PreviewOnce(Config cfg, out int bright, out string info)
        {
            if (cfg == null || cfg.OcrRegions == null || cfg.OcrRegions.Count == 0)
            {
                bright = 0;
                info = "没有配置任何 OCR 区域。";
                return null;
            }
            return PreviewRegion(cfg, cfg.OcrRegions[0], out bright, out info);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { _timer.Dispose(); } catch { }
            ReleaseBuffers();
        }
    }
}
