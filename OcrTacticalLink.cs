using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SCPSLAlert
{
    /// <summary>一条“想对战术数据做的改动”。先算出来，再统一在 UI 线程上套用。</summary>
    internal class TacticalUpdate
    {
        public enum Kind
        {
            /// <summary>把某个 SCP 标成“本回合在场”。</summary>
            SetPresent,
            /// <summary>标记阵亡（可带击杀阵营）。</summary>
            MarkKilled,
            /// <summary>填血量。</summary>
            SetHp,
            /// <summary>校准阵营刷新（大波 / 小波）。</summary>
            SetWave,
            /// <summary>发电机状态。</summary>
            SetGenerator,
            /// <summary>
            /// 认到了"轻收容还剩多少秒"。
            /// B 存剩余秒数；套用时按它校准整个回合时间基准，
            /// 于是支援刷新 / 核弹 / 回合时钟会一起对齐。
            /// </summary>
            SetDeconRemaining
        }

        public Kind What;
        public string Code = "";
        public string Faction = TacticalLog.NONE;

        public int A;               // 血量当前值 / 发电机下标
        public int B;               // 血量上限 / 发电机剩余秒数
        public bool Flag;           // 发电机：是否已完全启动
        public bool Off;            // 发电机：是否被关掉

        /// <summary>给日志用的一句话。</summary>
        public string Text = "";
    }

    /// <summary>
    /// OCR 识别结果 → 战术面板数据 的联动。
    ///
    /// 分成两段，目的是绝不出现“后台线程改数据、UI 线程同时在读”的竞态，
    /// 也绝不出现界面重绘风暴：
    ///
    ///   Evaluate()  纯文本计算，只读静态数据，可以放心在后台线程跑；
    ///   Apply()     只在 UI 线程上改模型 + 写回 config，改完发一次通知。
    ///
    /// 复用的是现有的 TacticalLog 模型和 Config 里的战术数据（面板本来就用它们），
    /// 所以 OCR 关掉 / 认错了，战术面板照样能完全独立地手动用。
    ///
    /// 容错：SCP 编号一律拿 TacticalLog.ScpCodes 里那几种去对，
    /// 所以 "SCP-173" / "SCP 173" / "SCP-1 73" / "5CP173" 都能认成 173。
    /// </summary>
    internal class OcrTacticalLink
    {
        private readonly Config _cfg;
        private readonly AlertManager _mgr;

        // ---- SCP 编号：允许 SCP / 5CP / SGP，中间可以有各种分隔 ----
        private static readonly Regex ReScpHead = new Regex(
            // 加 cp / cр：OCR 常常把开头的 s 漏掉（"SCP-173" → "CP-173"）
            "(?:scp|5cp|sgp|scps|scb|cp|сc|ср)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---- 阵亡 / 被收容。故意不用 "contain"：Containment Zone 会误判 ----
        // 中文也不能用光秃秃的 "收容"（"轻收容" 会误判），必须带前后文。
        //
        // 【模糊匹配】OCR 会在汉字之间插空格，中间还可能夹着主语/副词，
        // 例如 "SCP-696 已 被 科学 家 重新 收容"：
        // 去掉空格后是 "已被科学家重新收容"，里面**找不到连续的"已被收容"**。
        // 所以这里用"被动标记 + 中间允许插字 + 收容"的写法。
        //
        // 【踩过的坑】中间那段以前写的是 `[^0-9]{0,12}`（不许出现数字），
        // 结果 MTF 部队名（QUEBEC-15 / CHARLIE-18 / 0SCAR-69）全都带数字，
        // "SCP-106 已被 QUEBEC-15 成功收容"整条匹配不上 —— 战术模型连"阵亡"
        // 都没记，自然也没有 +15 影响力（日志实锤：只有播报提示弹出来）。
        // 现在允许中间出现数字（长度照旧限制，避免把整句吞进来）。
        private static readonly Regex ReDeath = new Regex(
            "terminat|contained|recontain|killed|eliminated|neutraliz|execut|destroy|dead|"
          + "阵亡|消灭|击毙|击杀|死亡|已死|"
            // 被动 +（最多 12 个任意字符）+（可选"重新"）+ 收容。
            // 否定后顾：『目前还有 3 个 SCP 项目**等待被**重新收容』是提示，不是死亡。
          + "(?<!等待)(?<!还要等待)(?<!還要等待)(?:已被|已|被).{0,12}?(?:重新)?(?:收容|contain)|"
            // "重新收容"本身就是很强的信号（"轻收容"前面不会出现"重新"），
            // 但"等待被重新收容"这种提示语同样要排除。
          + "(?<!等待)(?<!还要等待)(?<!還要等待)重新.{0,6}?收容",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // ---- 阵营 ----
        // 注意要包含"机动特遣**部队**"这种写法（"特遣队"匹配不到它，
        // 中间隔着"部"字；实测播报就是"机动特遣部队（）代号（）已进入设施"）。
        private static readonly Regex ReMtf = new Regex(
            "mtf|ntf|ninetailed|epsilon|九[尾属]狐|九尾|九属|机动特遣队|特遣队|特遣部队|机动特遣",
            RegexOptions.CultureInvariant);

        private static readonly Regex ReChaos = new Regex(
            "chaos|混沌|叛乱|反叛|叛军",
            RegexOptions.CultureInvariant);

        // ---- 刷新增援 ----
        private static readonly Regex ReEnter = new Regex(
            "hasentered|entered|entering|arriv|deploy|spawn|detect|spott|found|"
          + "进入|进入设施|抵达|到达|入场|进场|已到|侦测到|檢測到|检测到|监测到|发现|發現|出现",
            RegexOptions.CultureInvariant);

        // ---- 混沌小波的说法："刷了 N 名武装单位" ----
        private static readonly Regex ReArmedUnit = new Regex(
            "武装单位|武装人员|armed",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---- 小波：播报里说"增援部队"（增援 = 小规模）。
        //      "九尾狐增援部队已进入设施" → MTF 小波
        private static readonly Regex ReReinforce = new Regex(
            "增援部队|增援|reinforcement",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---- 发电机：generator2 / gen 2 / 发电机2 / 发电机 二 / 应急发电站 ----
        // 注意关键词要包含"发电站"：C.A.S.S.I.E. 中文播报用的是"应急发电站"。
        private static readonly Regex ReGen = new Regex(
            "(?:emergency\\s*)?(?:generator|gen|gcn|发电机|發電機|发电站|發電站)"
          + "[\\s\\-_#:]*([123]|[一二三])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "A 分之 B 的应急发电站已被激活" —— 中文分数的写法：A 是分母，B 是分子，
        // 也就是 **B/A = 已经启动了几台 / 一共几台**（实测原文都是"3 分之 1/2/3"）。
        // 分子前那一位允许漏读（OCR 只认出"分之 2"时，2 就是已启动台数）。
        private static readonly Regex ReGenFraction = new Regex(
            "([0-9]{1,2}|[一二三])?\\s*分\\s*之?\\s*([0-9]{1,2}|[一二三])"
          + "[^0-9]{0,10}?(?:应急|應急)?\\s*(?:发电机|發電機|发电站|發電站|generator)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "所有的应急发电站已被成功激活" —— 三台全开的那条收尾播报（实测原文）。
        private static readonly Regex ReGenAll = new Regex(
            "(?:所有|全部|三台|3\\s*台|all)\\s*(?:的)?\\s*(?:应急|應急)?\\s*"
          + "(?:发电机|發電機|发电站|發電站|generators?|generator)"
          + "[^0-9]{0,12}(?:激活|啟動|启动|activat|online|ready|成功)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ---- 轻收容 / 净化的"剩余时间" ----
        //
        // 【为什么要专门认这个】以前只认"净化开始了"这一个事件，于是：
        // 认到轻收容剩余时间 → 只改了净化那一项，支援刷新 / 核弹 / 回合时钟
        // 全都还按老基准走，越错越多。
        // 其实"净化还剩多少"是一条**全局锚点**：它唯一确定了本回合已进行多久，
        // 所以拿它去校准回合基准，支援刷新 / 核弹 / 回合时间会一起对齐。
        //
        // 【两种写法都要认】
        //   · 原始文本里冒号还在："轻收容剩余时间 03:20"
        //   · Normalize 之后冒号没了： "轻收容剩余时间0320"
        // 另外中文 OCR 经常把字之间插上空格（"轻 收 容"），
        // 所以关键词也要允许中间有空格。
        private const string DeconKw =
            "(?:轻\\s*收\\s*容|輕\\s*收\\s*容|净\\s*化|淨\\s*化|decontam|decon)";

        /// <summary>带冒号的 mm:ss（在原始文本上匹配）。</summary>
        private static readonly Regex ReDeconClock = new Regex(
            DeconKw + "[^0-9]{0,20}(\\d{1,2})\\s*[:：]\\s*(\\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>明确写了"秒 / s / sec"的秒数。</summary>
        private static readonly Regex ReDeconSeconds = new Regex(
            DeconKw + "[^0-9]{0,20}(\\d{2,4})\\s*(?:秒|s\\b|sec)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>冒号被 Normalize 吃掉之后的 4 位 mmss（"0320" = 3 分 20 秒）。</summary>
        private static readonly Regex ReDeconMmss = new Regex(
            DeconKw + "[^0-9]{0,20}(\\d{2})(\\d{2})(?!\\d)",
            RegexOptions.CultureInvariant);

        public OcrTacticalLink(Config cfg, AlertManager mgr)
        {
            _cfg = cfg;
            _mgr = mgr;
        }

        private TacticalLog Log
        {
            // _mgr 允许为 null（设置窗口预览时只想跑 Evaluate，不碰模型）
            get { return _mgr == null ? null : _mgr.Tactical; }
        }

        /// <summary>去空格后的文本里找出所有已知 SCP 编号（给"左侧列表"用）。</summary>
        public static List<string> FindAllCodes(string flat)
        {
            return FindCodes(flat);
        }

        public static bool IsKnownCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                if (TacticalLog.ScpCodes[i] == code) return true;
            return false;
        }

        /// <summary>
        /// 从数字串里挑出已知的 SCP 编号。
        /// "1732400" → 173（先试 4 位再试 3 位，全部拿 ScpCodes 对）；
        /// "1 73" 这种被 OCR 拆开的，外面已经先把空格去掉了，所以到这里是 "173"。
        ///
        /// 【模糊匹配】OCR 经常把数字认错（0↔6、1↔7、5↔6、8↔3、9↔4…）。
        /// 比如 SCP-096 被认成 "696" —— 严格比对会直接丢掉整条播报。
        /// 所以精确匹配失败时，再试一次"只差一位、而且错的那位是易混字符"的编号。
        /// 只有**唯一命中**时才采纳（同时像两个就宁可不要，免得认错对象）。
        /// </summary>
        public static string MatchKnownCode(string digitRun)
        {
            if (string.IsNullOrEmpty(digitRun)) return null;
            if (digitRun.Length >= 4 && IsKnownCode(digitRun.Substring(0, 4))) return digitRun.Substring(0, 4);
            if (digitRun.Length >= 3 && IsKnownCode(digitRun.Substring(0, 3))) return digitRun.Substring(0, 3);

            // ---- 两位数也收（操作者要求）----
            // OCR 经常把 "SCP-049" 读成 "SCP-49" / "649"（前导 0 丢了）。
            // 只收**前导零被打头**的那几个编号（049 → 49、096 → 96、079 → 79）：
            // 它们的两位写法唯一，认错不了；而 "106" 的 "06"、"3114" 的 "14"
            // 本身就像普通数字，放进来只会误判。
            if (digitRun.Length == 2)
                return ZeroLeadTwoDigit(digitRun);

            // ---- 精确匹配失败 → 试模糊 ----
            string cand = digitRun.Length >= 3 ? digitRun.Substring(0, 3) : digitRun;
            if (cand.Length != 3) return null;

            string found = null;
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string known = TacticalLog.ScpCodes[i];
                if (known.Length != cand.Length) continue;
                if (!Confusable(known, cand)) continue;

                if (found != null) return null;   // 不止一个像 → 不敢猜
                found = known;
            }
            return found;
        }

        /// <summary>
        /// 两位数字串唯一对应哪个"前导零"编号（049 / 096 / 079）。
        /// 找不到或不止一个就返回 null。
        /// </summary>
        private static string ZeroLeadTwoDigit(string two)
        {
            string found = null;
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string k = TacticalLog.ScpCodes[i];
                if (k.Length != 3 || k[0] != '0') continue;
                if (k.Substring(1) != two) continue;
                if (found != null) return null;
                found = k;
            }
            return found;
        }

        /// <summary>
        /// 两个等长编号是不是"只差一位、而且那一位是 OCR 易混字符"。
        /// 易混对：0/6/8/9、1/7/4、3/8/5、2/7、5/6。
        /// </summary>
        private static bool Confusable(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                diff++;
                if (diff > 1) return false;
                if (!ConfusableDigit(a[i], b[i])) return false;
            }
            return diff == 1;
        }

        private static bool ConfusableDigit(char x, char y)
        {
            // 把易混的一组写成字符串，两个字符落在同一组里就算易混
            string[] groups = new string[]
            {
                "0689", "174", "38", "385", "27", "56", "2", "0"
            };
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i].IndexOf(x) >= 0 && groups[i].IndexOf(y) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 纯计算：一段识别文本里有哪些可以自动填写的东西。
        /// 不碰任何共享状态，后台线程随便调。
        /// </summary>
        public List<TacticalUpdate> Evaluate(OcrRegion region, string rawText)
        {
            List<TacticalUpdate> list = new List<TacticalUpdate>();
            if (rawText == null || rawText.Trim().Length == 0) return list;

            // flat：去掉所有空格和标点，专门用来找关键词和编号
            // （"SCP-1 73" 在这里会变成 "scp173"，正好和 173 对上）
            string flat = OcrAlertWatcher.Normalize(rawText);
            if (flat.Length == 0) return list;

            // 播报类规则（核弹/净化/阵亡/刷新增援）不只看"播报区"：
            // 那些文字可能出现在任何地方，所以除了几个有专用用途的区域
            // （角色 / 票数 / 静音框）之外，全都跑一遍。
            bool isBroadcast = region == null
                            || (region.Kind != OcrRegionKind.Role
                                && region.Kind != OcrRegionKind.Ticket
                                && region.Kind != OcrRegionKind.Silence);
            // 【踩过的坑】发电机播报是出现在"左下角播报"里的，
            // 而以前只在"发电机/通用"区域上解析 —— 用户配置里根本没有发电机区域，
            // 于是发电机永远认不出来。现在播报区也跑发电机解析。
            // HP 同理：左侧 SCP 列表里本来就写着 "SCP-106 350/350"。
            bool allowHp = region == null || region.Kind == OcrRegionKind.Health
                        || region.Kind == OcrRegionKind.Generic
                        || region.Kind == OcrRegionKind.ScpList;
            bool allowGen = region == null || region.Kind == OcrRegionKind.Generator
                         || region.Kind == OcrRegionKind.Generic
                         || region.Kind == OcrRegionKind.Broadcast;

            // ---- 0) SCP 血量表（专用格式 "HP:当前/上限 HS:护盾"）----
            // 这一块用专门的正则，比通用规则准；解析结果同样是 SetHp，
            // 后面会走"掉血 → 累计 10% → 按波次分摊影响力"那条路。
            if (region != null && region.Kind == OcrRegionKind.ScpHp)
            {
                list.AddRange(DetectScpHpBlock(rawText));
                return list;      // 这块只有血量，不再套其它规则
            }

            // ---- 1) 阵营刷新增援（大波 / 小波）----
            // 播报区和通用区都试；血量区不算，免得面板上写着 MTF 就误判
            if (isBroadcast || (region != null && region.Kind == OcrRegionKind.Generic))
            {
                bool smallWave;
                string wave = DetectWave(flat, out smallWave);
                if (wave != null)
                {
                    TacticalUpdate u = new TacticalUpdate();
                    u.What = TacticalUpdate.Kind.SetWave;
                    u.Faction = wave;
                    u.Flag = smallWave;   // 播报里说"增援部队" → 小波
                    list.Add(u);
                }
            }

            // ---- 2) SCP 阵亡 / 被收容 ----
            bool death = ReDeath.IsMatch(flat);
            string killedBy = DetectFaction(flat);

            // 【为什么还要 TryParseKill】提示那条路（OcrAlertWatcher → TryParseKill）
            // 会按操作者定的规则把阵营推出来：部队编号（QUEBEC-15 / Epsilon-11）→ MTF、
            // 科学家 → MTF、D 级 → 混沌、其它玩家名 → 混沌。
            // 而 DetectFaction 只认字面上的 MTF / 九尾狐 / 混沌 —— 两边口径不一致，
            // 于是提示里写着"QUEBEC-15（MTF）击杀了 SCP-106"，战术模型却认不出阵营，
            // 只挂一条"待归属"，+15 影响力要人手点一下才有。
            // 现在阵亡这条也走同一套推断，两边对得上。
            if (death)
            {
                KillInfo ki;
                if (TryParseKill(rawText, out ki) && ki.Faction != TacticalLog.NONE)
                    killedBy = ki.Faction;
            }

            foreach (string code in FindCodes(flat))
            {
                if (death)
                {
                    TacticalUpdate u = new TacticalUpdate();
                    u.What = TacticalUpdate.Kind.MarkKilled;
                    u.Code = code;
                    u.Faction = killedBy;
                    list.Add(u);
                }
                else
                {
                    TacticalUpdate u = new TacticalUpdate();
                    u.What = TacticalUpdate.Kind.SetPresent;
                    u.Code = code;
                    list.Add(u);
                }
            }

            // ---- 3) 轻收容 / 净化的剩余时间 → 校准全局时间基准 ----
            // 这一条会让支援刷新、核弹、回合时钟一起对齐，不只是净化那一项
            double deconLeft;
            if (TryDetectDeconRemaining(rawText, flat, out deconLeft))
            {
                TacticalUpdate u = new TacticalUpdate();
                u.What = TacticalUpdate.Kind.SetDeconRemaining;
                u.B = (int)Math.Round(deconLeft);
                list.Add(u);
            }

            // ---- 4) 血量（按需区域才做）----
            if (allowHp) list.AddRange(DetectHp(rawText));

            // ---- 5) 发电机（按需区域才做）----
            if (allowGen) list.AddRange(DetectGenerators(flat));

            return list;
        }

        /// <summary>
        /// 从文字里读出"净化还剩多少秒"。按可靠程度依次尝试三种写法。
        /// 时间必须在合理范围内，否则宁可不要 ——
        /// 误读一个数字会把整个回合基准带偏，比不校准更糟。
        /// </summary>
        private bool TryDetectDeconRemaining(string raw, string flat, out double seconds)
        {
            seconds = 0;
            Match m;
            int a, b;

            // ① 原始文本里带冒号：最可靠
            m = ReDeconClock.Match(raw == null ? "" : raw);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out a)
                && int.TryParse(m.Groups[2].Value, out b)
                && b < 60)
            {
                seconds = a * 60 + b;
                return Plausible(seconds);
            }

            // ② 明确写了秒 / s / sec
            if (MatchSeconds(ReDeconSeconds, raw, out seconds)) return true;
            if (MatchSeconds(ReDeconSeconds, flat, out seconds)) return true;

            // ③ 冒号被 Normalize 吃掉，只剩 4 位数字（"0320" = 3 分 20 秒）
            m = ReDeconMmss.Match(flat == null ? "" : flat);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out a)
                && int.TryParse(m.Groups[2].Value, out b)
                && b < 60)
            {
                seconds = a * 60 + b;
                return Plausible(seconds);
            }

            return false;
        }

        private static bool MatchSeconds(Regex re, string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;
            Match m = re.Match(text);
            if (!m.Success) return false;

            int sec;
            if (!int.TryParse(m.Groups[1].Value, out sec)) return false;
            seconds = sec;
            return Plausible(seconds);
        }

        private static bool Plausible(double seconds)
        {
            if (seconds < 3) return false;
            // 上界放宽到 30 分钟：只要不是明显误读（比如把别处的编号读成秒数）就收下。
            // 真正的上界是"一局的净化总时长"，但那是实例字段，这里拿不到。
            if (seconds > 30 * 60) return false;
            return true;
        }

        /// <summary>把 Evaluate 算出来的改动套到战术数据上（必须在 UI 线程调用）。</summary>
        public string Apply(List<TacticalUpdate> updates)
        {
            if (updates == null || updates.Count == 0) return "";
            TacticalLog log = Log;
            if (log == null) return "";

            SyncRoundState(log);      // 换局了就清掉按回合的状态

            List<string> done = new List<string>();
            DateTime now = DateTime.Now;
            bool changed = false;

            for (int i = 0; i < updates.Count; i++)
            {
                TacticalUpdate u = updates[i];
                switch (u.What)
                {
                    case TacticalUpdate.Kind.SetPresent:
                        changed |= SetPresent(u.Code, done);
                        break;

                    case TacticalUpdate.Kind.MarkKilled:
                    {
                        TacticalLog.ScpRecord rec = log.Find(u.Code);
                        if (rec == null) break;
                        changed |= SetPresent(u.Code, done);

                        if (rec.Killed)
                        {
                            // 已经标过阵亡：只在阵营还是空的时候补一下
                            if (u.Faction != TacticalLog.NONE && rec.Faction == TacticalLog.NONE)
                            {
                                rec.Faction = u.Faction;
                                // 认出来了就把"待归属"那条撤掉（已经不用人来判断了）
                                if (log.ResolvePendingKill(u.Code, u.Faction, now, log.Has079))
                                    done.Add("SCP-" + u.Code + " 已按播报归属（待归属那条已撤）");
                                changed = true;
                                done.Add("SCP-" + u.Code + " 补记击杀方 " + TacticalLog.FactionName(u.Faction));
                            }
                            break;
                        }

                        rec.Killed = true;
                        rec.ChangedAt = now;
                        if (u.Faction != TacticalLog.NONE) rec.Faction = u.Faction;
                        // 写回 config：面板重建（重新打开）和程序重启都是从这里读状态的。
                        // 不写的话下次开面板会看成"存活"。
                        _cfg.SetTactical("killed_" + u.Code, "1");
                        if (u.Faction != TacticalLog.NONE)
                            _cfg.SetTactical("killfac_" + u.Code, u.Faction);
                        changed = true;
                        done.Add("SCP-" + u.Code + " 已阵亡"
                            + (u.Faction == TacticalLog.NONE ? "" : "（" + TacticalLog.FactionName(u.Faction) + "）"));

                        // ---- 按文档记目标 ----
                        // 先补发"打掉 10% 最大生命"的累计影响力（文档说死亡时不适用，
                        // 所以 AwardDamageMarks 会自动把最后那一段留给击杀）。
                        int marks = log.AwardDamageMarks(u.Code, now);
                        if (marks > 0)
                            done.Add("（累计打掉 " + (marks * 10) + "% 生命 +"
                                + (marks * TicketModel.Info(TicketModel.Objective.DamageScp10).Influence)
                                + " 影响力）");

                        // 击杀可玩 SCP = +15 影响力 −10s 计时
                        // 只有认出是哪一方杀的时候才记账（认不出阵营就没法归给谁）。
                        if (u.Faction != TacticalLog.NONE)
                        {
                            log.Tickets.RecordAt(TicketModel.Objective.KillScp, u.Faction,
                                log.Has079, now, "SCP-" + u.Code);
                            done.Add("（" + TacticalLog.FactionName(u.Faction)
                                + " +" + TicketModel.Info(TicketModel.Objective.KillScp).Influence
                                + " 影响力）");
                        }
                        else
                        {
                            // 认得出"哪个 SCP 死了"、但认不出是哪一方杀的 →
                            // 挂一条"待归属"，让用户在面板第一页点一下定夺
                            //（操作者要求：这是很重要的决策，交给人来判断）。
                            log.AddPendingKill(u.Code, now);
                            done.Add("（认不出击杀方 → 已在面板「总览」页挂一条待归属，等你点）");
                        }
                        break;
                    }

                    case TacticalUpdate.Kind.SetHp:
                    {
                        TacticalLog.ScpRecord rec = log.Find(u.Code);
                        if (rec == null) break;
                        changed |= SetPresent(u.Code, done);

                        if (rec.CurrentHp == u.A && (u.B <= 0 || rec.TotalHp == u.B)) break;

                        // ---- 血量涨了：可能是回血，也可能是误读 ----
                        // 以前这里一律当误读丢掉（老注释写"游戏里没有治疗"）。
                        // 但操作者确认：**部分服务器有 SCP 回血机制**，
                        // 而且血量回上来以后再打，那部分伤害照样算影响力。
                        //
                        // 两种情况要分开：
                        //   · 面板总血量被改过 → 血量基准变了，不算回血；
                        //   · 确实是回血 → 累计掉血量**不动**（既不倒退，
                        //     也不提前算）。以后真打出来的伤害会重新累加。
                        // 误读（2400 读成 2800）也会被当成回血，
                        // 但那只影响一个 SCP 的血量读数，下次读到正确值就正过来了，
                        // 不会像"丢掉"那样把真实回血漏掉 —— 宁可偏向接受回血。
                        if (rec.CurrentHp > 0 && u.A > rec.CurrentHp)
                        {
                            bool totalChanged = u.B > 0 && rec.TotalHp > 0 && rec.TotalHp != u.B;

                            if (!totalChanged)
                            {
                                done.Add("SCP-" + u.Code + " 回血 " + (u.A - rec.CurrentHp)
                                    + " → 累计掉血仍记 " + rec.AccumulatedDamage
                                    + "（回血后再打照样算影响力）");
                            }
                            else
                            {
                                done.Add("SCP-" + u.Code + " 读到 " + u.A + "，但总血量同时从 "
                                    + rec.TotalHp + " 改成 " + u.B + " → 认为是改配置，不算回血");
                            }
                        }

                        // ---- 血量掉得很多：多读一次确认（操作者的建议）----
                        //
                        // 【为什么不能只看"掉了多少"】操作者提醒：**有的武器 DPS 上千**，
                        // 一次轮询（几秒）内把 SCP 打掉大半管血是完全正常的。
                        // 所以"掉得多 = 读错"这个判据根本站不住脚 ——
                        // 按它拦，高 DPS 交火时的影响力会被大量漏掉。
                        //
                        // 【真正的判据是"持续性"】误读（2400 读成 400）下一帧就恢复；
                        // 真掉血则会停在那儿、或者继续往下掉。所以：
                        //   · 一次掉超过上限 25% → 先挂起，**这次不采纳**；
                        //   · 后面某次读到"在第一次附近或更低"（不需要相等，
                        //     DPS 一直在掉本来就不会相等，容差给到上限的 25%）
                        //     → 判定为真，采纳那次的读数。
                        // 这样高 DPS 顶多晚一个轮询周期记账，不会漏；
                        // 而一次性误读因为下一帧就跳回去了，永远不会被确认。
                        if (rec.CurrentHp > 0 && u.A < rec.CurrentHp && rec.TotalHp > 0
                            && (rec.CurrentHp - u.A) * 100 > rec.TotalHp * HpDropSuspectPercent)
                        {
                            if (!WaitBigDropConfirm(u.Code, u.A, rec.TotalHp))
                            {
                                done.Add("SCP-" + u.Code + " 一次掉到 " + u.A + "（超过上限 "
                                    + HpDropSuspectPercent + "%）→ 先等下一次读数确认，本次不记账");
                                break;
                            }
                            done.Add("SCP-" + u.Code + " 大幅掉血 " + u.A + " 已获下一次读数确认");
                        }

                        // 走 SetCurrentHp：它负责维护"累计掉血量"，
                        // 所以回血之后再打出来的伤害照样能跨过新的 10% 发影响力。
                        // 【踩过的坑】以前这里是直接赋值 + 单独记一笔"回血"，
                        // 结果回血会被当成"以后要打的血已经打过了"，
                        // 再打一遍一点影响力都不发。现在统一走这个入口就不会错。
                        rec.SetCurrentHp(u.A);

                        // 总血量（分母）：**优先用玩家在面板上填的值**（操作者要求）。
                        //
                        // 判断依据是 config 里那条 hp_<编号>。注意两件事：
                        //   · 面板会给每个 SCP 填好默认值（catalog 里的标准血量），
                        //     那种"默认值"和用户手填的没法区分 —— 都当作用户设定，
                        //     这样 OCR 读歪（4500 → 5500）时不会把用户的设置冲掉；
                        //   · 如果 config 里根本没有这条（用户用的是老配置、
                        //     或者还没开过面板），就用 OCR 读到的上限兜底。
                        // 面板在用户改过之后也会把值写回 config，所以"用户说了算"
                        // 这条在任何时候都成立。
                        bool panelHasTotal = false;
                        {
                            string panelTotal = _cfg.GetTactical("hp_" + u.Code);
                            int pt;
                            if (int.TryParse(panelTotal, out pt) && pt > 0) panelHasTotal = true;
                        }

                        if (!panelHasTotal && u.B > 0 && rec.TotalHp != u.B)
                        {
                            rec.TotalHp = u.B;
                            log.OnTotalHpChanged(rec);
                        }
                        changed = true;

                        // ---- 累计伤害到 10% 就补发一次影响力（文档 1.2.3）----
                        int hpMarks = log.AwardDamageMarks(u.Code, now);
                        if (hpMarks > 0)
                            done.Add("（SCP-" + u.Code + " 累计掉 "
                                + (hpMarks * 10) + "% 生命 → +"
                                + (hpMarks * TicketModel.Info(TicketModel.Objective.DamageScp10).Influence)
                                + " 影响力）");
                        // 写回 config：战术面板重建时是从 config 读这些值的，
                        // 不写回去的话一开面板就会被旧值覆盖。
                        // 注意**不写回 hp_（上限）** —— 上限以面板填的为准，
                        // OCR 读到的不该覆盖用户的设置。
                        _cfg.SetTactical("cur_" + u.Code, u.A.ToString(CultureInfo.InvariantCulture));
                        done.Add("SCP-" + u.Code + " 血量 " + u.A
                            + (u.B > 0 ? "/" + u.B : ""));
                        break;
                    }

                    case TacticalUpdate.Kind.SetDeconRemaining:
                    {
                        // 用"净化还剩多少"反推本回合已经进行了多久 ——
                        // 这一步同时校准了净化 / 支援刷新 / 核弹 / 回合时钟，
                        // 而不是只改净化那一项。
                        if (_mgr == null || _mgr.Estimator == null) break;
                        _mgr.Estimator.CalibrateDeconRemaining(now, u.B);
                        changed = true;
                        done.Add("轻收容剩余 " + TimeEstimator.Clock(u.B, true)
                            + " → 已按它校准回合时间（支援刷新 / 核弹 / 回合时钟一起对齐）");
                        break;
                    }

                    case TacticalUpdate.Kind.SetWave:
                    {
                        if (u.Faction == TacticalLog.NONE) break;

                        // ---- 60 秒静默期（操作者要求）----
                        // 刚认定大波后 60 秒内，屏幕上再出现"刷新播报"一定是**同一段被重复读到**
                        // （游戏里最快也要 2 分钟以上才可能再刷一波），
                        // 所以这期间**大小波一律不认**：不提示、不进顺序、不重置计时。
                        // 实测：大波 22:38:08 之后 13 秒 / 22 秒又各读到一次"增援部队"，
                        // 提示里就蹦出"小波"，后面的刷新推算也跟着偏。
                        //
                        // 【唯一例外】亡者协议正在走的时候，游戏**确实会**再刷一波小波，
                        // 而规则是"小波把协议进度后退 40 秒"（见 AlertManager.CheckDeadManSwitch）。
                        // 那种小波必须**记进刷新顺序**才能被认出来，所以这时照常记，
                        // 只是不弹那条"小波"提示（免得又让你以为识别错了）。
                        double sinceBig = log.HasBigWave
                            ? (now - log.LastBigWaveAt).TotalSeconds : double.MaxValue;
                        bool deadManMiniWave = u.Flag && _mgr != null && _mgr.DeadManActive;
                        if (sinceBig < log.SmallWaveGuardSeconds && !deadManMiniWave)
                        {
                            done.Add("（距上次大波只有 " + (int)sinceBig
                                + " 秒 → 判为重复读到的同一条，整条忽略）");
                            break;
                        }

                        // 和面板按 F11 走的是同一个入口，行为完全一致
                        // 大小波判定：
                        //   ① 播报里明说"增援部队" → 小波（MTF 那边是这种写法）；
                        //   ② 否则按**时间**推：大波之后 2 分钟窗口内的那一波就是小波。
                        // ② 是必须的：混沌的大小波播报一模一样，只能靠时间分。
                        // 注意②不限阵营 —— 小波可能是大波那一方（打够票就续自己）。
                        bool small = u.Flag || log.GuessIsSmallWave(now);

                        // 需求八附：记下是识别到的（含大小波）。
                        // 【返回值很关键】被判为"刚刷大波后的小波误报"时返回 false ——
                        // 那种情况整条忽略：不弹提示、不刷面板、也不改计时锚点。
                        // 以前这里不管三七二十一都提示"识别到 XX 进入设施（小波）"，
                        // 还顺手把计时又推了一次，后面的刷新推算就全偏了。
                        if (!log.MarkWave(now, u.Faction, small, "ocr"))
                        {
                            done.Add("（同一段刷新被重复读到，判为小波误报 → 整条忽略）");
                            break;
                        }

                        changed = true;
                        done.Add((small ? "小波" : "大波") + "刷新：" + TacticalLog.FactionName(u.Faction)
                            + "，刷新计时已校准到现在");

                        // 需求八附：给用户一条提示（识别到谁进来了 + 后续推测）
                        // 亡者协议期间那种"静默期里的小波"只记顺序、不弹提示（见上面的注释）。
                        if (_mgr != null && !(deadManMiniWave && sinceBig < log.SmallWaveGuardSeconds))
                            _mgr.OnWaveRecognized(u.Faction, small);
                        break;
                    }

                    case TacticalUpdate.Kind.SetGenerator:
                    {
                        if (u.A < 0 || u.A >= TacticalLog.GeneratorCount) break;
                        TacticalLog.Generator gen = log.Generators[u.A];

                        string k = (u.A + 1).ToString(CultureInfo.InvariantCulture);

                        // ---- 播报说"已被激活"：一切以播报为准 ----
                        // 放在 `if (gen.Done) break;` **前面**：
                        // 手填的倒计时可能已经先把它标成"时间到"了，但那是本地估计，
                        // 播报才是权威 —— 播报来了照样要确认，并且由它来记这一笔影响力。
                        if (u.Flag)
                        {
                            if (u.Faction != TacticalLog.NONE) gen.Faction = u.Faction;
                            _cfg.SetTactical("gen_" + k, "0");
                            string note = log.ConfirmGenerator(u.A, now, "播报");
                            done.Add("发电机 " + k + " 已完全启动" + note);
                            changed = true;
                            break;
                        }

                        if (gen.Done) break;     // 已经算完成的，不再接受"启动中/未启动"这类更新

                        if (u.Off)
                        {
                            gen.Running = false;
                            gen.ManualRemaining = -1;
                            _cfg.SetTactical("gen_" + k, "");
                            done.Add("发电机 " + k + " 未启动");
                        }
                        else
                        {
                            // 【踩过的坑】"启动中"这条播报会**每轮 OCR 都重复报到**。
                            // 以前每报一次就把 StartedAt 重设成"现在"，于是
                            // "按满速 120 秒跑完"永远跑不到头 —— 每轮轮询都从头开始，
                            // 发电机再也不会自己到点（模型的时间被轮询绑架了）。
                            //
                            // 规则：这一台已经按满速在跑、这次又没读到秒数 → 锚点不动；
                            //       读到了具体秒数 → 拿屏幕上的数字重新对齐
                            //       （那种情况时钟本来就是屏幕，读到多少对齐到多少）。
                            bool keepAnchor = gen.Running && gen.ManualRemaining < 0 && u.B < 0;
                            if (!keepAnchor)
                            {
                                gen.Running = true;
                                gen.StartedAt = now;
                                gen.ManualRemaining = u.B >= 0 ? u.B : -1;
                            }
                            if (gen.Duration <= 0) gen.Duration = 120;
                            if (u.Faction != TacticalLog.NONE) gen.Faction = u.Faction;

                            // 写回 config 的是**模型算出来的实时剩余**，
                            // 不是"光写了启动中"那种情况下的 -1（那会让面板显示成 -1）。
                            _cfg.SetTactical("gen_" + k, ((int)gen.Remaining(now))
                                .ToString(CultureInfo.InvariantCulture));
                            done.Add("发电机 " + k + " 启动中，剩余 "
                                + (int)gen.Remaining(now) + " 秒");
                        }
                        changed = true;
                        break;
                    }
                }
            }

            // 没有改动就不发通知（避免无意义的重绘）。
            // 但"被忽略"的理由还是要返回给调用方去记日志 ——
            // 比如"血量回涨判定为误读"，用户需要知道程序看到了却没采纳。
            if (!changed)
                return done.Count > 0 ? string.Join("；", done.ToArray()) : "";

            // 只发一次通知：界面层（Program）接过去刷一次战术面板。
            // 面板那边的刷新是单向的（模型 → 控件 → 重绘），不会再回到这里。
            if (_mgr != null) _mgr.NotifyTacticalChanged();

            return string.Join("；", done.ToArray());
        }

        private bool SetPresent(string code, List<string> done)
        {
            TacticalLog log = Log;
            if (log == null) return false;
            TacticalLog.ScpRecord rec = log.Find(code);
            if (rec == null || rec.Present) return false;

            rec.Present = true;
            _cfg.SetTactical("pre_" + code, "1");
            if (done != null) done.Add("SCP-" + code + " 标记在场");
            return true;
        }

        /// <summary>这一段里出现的是哪一方刷新增援（判断不了就返回 NONE）。</summary>
        private static string DetectWave(string flat)
        {
            bool small;
            return DetectWave(flat, out small);
        }

        /// <summary>
        /// 判断这段文字是不是"刷新增援"，以及是**大波**还是**小波**。
        ///
        /// 播报的几种真实写法（实测）：
        ///   · "机动特遣部队（）代号（）已进入设施"          → MTF **大波**
        ///   · "九尾狐增援部队已进入设施"                    → MTF **小波**（关键词"增援部队"）
        ///   · "全体人员请注意。在 A 大门处侦测到 6 名混沌分裂者。" → 混沌 **大波**
        ///   · "Chaos Insurgency has entered the facility"   → 混沌 大波
        ///
        /// 【为什么要放宽】以前只认 entered/进入 这类动词，
        /// "**侦测到** 6 名混沌分裂者"这种最常见的混沌播报完全认不出来。
        /// 现在把"发现 / 侦测 / 检测 / detected"都收进来了。
        /// </summary>
        private static string DetectWave(string flat, out bool isSmallWave)
        {
            isSmallWave = false;

            // 小波：播报里说"增援部队"（增援 = 小规模）。
            // 注意要在"进入设施"之前判断，因为两种写法的动词是一样的。
            if (ReReinforce.IsMatch(flat)) isSmallWave = true;

            if (!ReEnter.IsMatch(flat) && !ReReinforce.IsMatch(flat))
            {
                // 没写"进入/侦测到"，但如果同时出现了阵营 + 大门/设施，
                // 那基本就是在说刷新增援（放宽一点，别漏）
                bool place = flat.IndexOf("大门") >= 0 || flat.IndexOf("設施") >= 0
                          || flat.IndexOf("设施") >= 0 || flat.IndexOf("facility") >= 0;
                if (!place) return null;
            }

            bool mtf = ReMtf.IsMatch(flat);
            bool chaos = ReChaos.IsMatch(flat);
            if (mtf && !chaos) return TacticalLog.MTF;
            if (chaos && !mtf) return TacticalLog.CHAOS;

            // 混沌小波的播报里没写"混沌"，而是说"刷了 N 名武装单位" ——
            // 按用户实测：只有武装单位、没有别的阵营词时就算混沌。
            if (!mtf && !chaos && ReArmedUnit.IsMatch(flat)) return TacticalLog.CHAOS;

            return null;    // 两边都提到了（比如"MTF 击退了 Chaos"），不猜
        }

        private static string DetectFaction(string flat)
        {
            bool mtf = ReMtf.IsMatch(flat);
            bool chaos = ReChaos.IsMatch(flat);
            if (mtf && !chaos) return TacticalLog.MTF;
            if (chaos && !mtf) return TacticalLog.CHAOS;
            return TacticalLog.NONE;
        }

        // ============================================================
        // 击杀播报解析（需求一）
        //
        // 需求：只有明确认出"谁杀的"才允许弹提示；
        //       认不出击杀者就整条忽略，不许弹"【未知】击杀了 SCP-XXX"。
        //       击杀者可以是玩家名，也可以是阵营名（MTF / 九尾狐 / Chaos / 混沌…）。
        // ============================================================

        /// <summary>一次击杀播报解析出来的信息。</summary>
        public class KillInfo
        {
            /// <summary>被击杀的 SCP 编号（如 "173"）。</summary>
            public string Code = "";
            /// <summary>击杀者显示名（玩家名或阵营名）。</summary>
            public string Killer = "";
            /// <summary>击杀者阵营；认不出来就是 NONE。</summary>
            public string Faction = TacticalLog.NONE;
            /// <summary>
            /// 有没有认出"谁杀的"。
            /// false 时 Killer 是"未知"，调用方应当换一条**被动语态**的提示
            /// （"SCP-XXX 已被击杀"），而不是"未知 击杀了 SCP-XXX"。
            /// </summary>
            public bool HasKiller;
        }

        // 击杀动词前面的那一段就是击杀者。故意限制长度和字符集，
        // 免得把整句话都吞进去。
        private static readonly Regex ReKillerBefore = new Regex(
            "([A-Za-z0-9_\\-\\u4e00-\\u9fa5]{1,20})\\s*"
          + "(?:击杀了|已击杀|杀死了|杀掉了|击杀了|击杀|杀死|killed|terminated|eliminated|neutralized|executed)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 被动语态：killed by XXX / 被 XXX 击杀 / 被 XXX（中间可能夹字）成功收容
        private static readonly Regex ReKillerAfter = new Regex(
            "(?:killed by|killedby|eliminated by|by)\\s*([A-Za-z0-9_\\-\\u4e00-\\u9fa5]{1,20})"
          + "|被\\s*([A-Za-z0-9_\\-\\u4e00-\\u9fa5]{1,20})[^0-9]{0,6}(?:击杀|杀死|消灭|收容)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 解析"某玩家击杀了 SCP-XXX"这类播报。
        /// 返回 false 表示**认不出击杀者** —— 调用方应当直接忽略这条播报。
        /// </summary>
        public static bool TryParseKill(string rawText, out KillInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(rawText)) return false;

            string flat = OcrAlertWatcher.Normalize(rawText);
            if (flat.Length == 0) return false;

            List<string> codes = FindCodes(flat);
            if (codes.Count == 0) return false;

            // 击杀者：**先找被动语态**（"killed by X" / "被 X 击杀"）——
            // 这种写法最明确；主动语态容易出现 "has been terminated" 里
            // 把 been 当成名字的乌龙。
            string killer = "";
            Match m = ReKillerAfter.Match(rawText);
            if (m.Success)
                killer = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;

            if (CleanKiller(killer).Length == 0)
            {
                killer = "";
                m = ReKillerBefore.Match(rawText);
                if (m.Success) killer = m.Groups[1].Value;
            }

            // 击杀者里其实写的是阵营名（"MTF 击杀了 SCP-173"）也算认出来了
            string faction = DetectFaction(killer.Length > 0 ? killer : "");

            killer = CleanKiller(killer);

            // 整句里如果还有别的阵营词，也可以给阵营补个值
            if (faction == TacticalLog.NONE) faction = DetectFaction(flat);

            // ---- 阵营推断（用户给的规则）----
            //   有"部队编号"（GOLF-688 / Epsilon-11 这种字母+数字）→ 九尾（MTF）
            //   科学家 / 研究员                → 属于九尾那边
            //   D 级人员 / Class-D             → 属于混沌那边
            //   其它（就是个普通玩家名）        → 混沌
            if (faction == TacticalLog.NONE)
                faction = GuessFaction(flat, killer);

            info = new KillInfo();
            info.Code = codes[0];
            info.HasKiller = killer.Length > 0 || faction != TacticalLog.NONE;

            // 认不出人名但能推出阵营 → 用阵营名当击杀者显示
            if (killer.Length == 0 && faction != TacticalLog.NONE)
                killer = TacticalLog.FactionName(faction);

            // 连阵营都推不出来 → 击杀者写"未知"，
            // 调用方会换一条**被动语态**的提示（"SCP-XXX 已被击杀"），
            // 而不是"未知 击杀了 SCP-XXX"那种读起来别扭的话。
            if (killer.Length == 0) killer = "未知";

            info.Killer = killer;
            info.Faction = faction;
            return true;
        }

        // 部队编号：字母（≥2）+ 可选分隔 + 数字，例如 GOLF-688 / Epsilon-11 / NTF-2
        private static readonly Regex ReUnitCode = new Regex(
            "[A-Za-z]{2,}[\\s\\-_]?\\d{1,4}",
            RegexOptions.CultureInvariant);

        private static readonly Regex ReScientist = new Regex(
            "科学家|科學家|研究员|研究員|scientist|research",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ReDClass = new Regex(
            "d\\s*[-－]?\\s*级|d\\s*[-－]?\\s*class|class\\s*[-－]?\\s*d|d级人员|d 级人员",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 推不出阵营时按用户的规则猜一个（需求变更）：
        /// 有部队编号 → 九尾；科学家 → 九尾；D 级 → 混沌；其它 → 混沌。
        /// 都没有（比如"已被收容"这种没主语的说法）→ 返回 NONE，交给"未知"那条提示。
        /// </summary>
        private static string GuessFaction(string flat, string killer)
        {
            string hay = (killer == null ? "" : killer) + "|" + (flat == null ? "" : flat);

            if (ReScientist.IsMatch(hay)) return TacticalLog.MTF;
            if (ReDClass.IsMatch(hay)) return TacticalLog.CHAOS;

            // 有部队编号就是九尾。
            // 注意必须先把 "scp049" 这类编号从文本里去掉 ——
            // 否则它本身长得就像"字母+数字"，会把每一条播报都判成九尾。
            string rest = StripScpHead(hay);
            if (ReUnitCode.IsMatch(rest)) return TacticalLog.MTF;

            // 有人名（清过之后还有内容）→ 混沌
            if (!string.IsNullOrEmpty(killer)) return TacticalLog.CHAOS;

            return TacticalLog.NONE;
        }

        /// <summary>把 "scp173 / 5cp-096 / scb 3114" 这些 SCP 标识从文本里抹掉。</summary>
        private static string StripScpHead(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Regex.Replace(s, "(?:scp|5cp|sgp|scps|scb)[\\s\\-_]*\\d{0,4}",
                " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>把抓到的"击杀者"洗一下：去掉明显是句子成分的词。</summary>
        private static string CleanKiller(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();

            // 这些明显不是名字，是句子的组成部分
            string[] junk = new string[]
            {
                "scp", "5cp", "sgp", "the", "has", "have", "had", "been", "being", "is", "was",
                "were", "are", "be", "by", "a", "an", "and", "or", "of", "in", "on", "at", "to",
                "unit", "units", "player", "someone", "unknown", "just", "now", "then",
                "被", "已", "被已", "玩家", "有人", "某", "未知", "未知的", "不明", "被击", "击"
            };
            for (int i = 0; i < junk.Length; i++)
            {
                if (string.Equals(s, junk[i], StringComparison.OrdinalIgnoreCase)) return "";
            }
            if (s.Length < 2) return "";
            return s;
        }

        /// <summary>去空格后的文本里找所有已知的 SCP 编号（去重）。</summary>
        private static List<string> FindCodes(string flat)
        {
            List<string> found = new List<string>();
            foreach (Match m in ReScpHead.Matches(flat))
            {
                int start = m.Index + m.Length;
                if (start >= flat.Length) continue;

                // 往后取一段数字（最多 8 位够用了）
                int end = start;
                while (end < flat.Length && end - start < 8 && flat[end] >= '0' && flat[end] <= '9') end++;
                if (end == start) continue;

                string code = MatchKnownCode(flat.Substring(start, end - start));
                if (code == null) continue;
                if (!found.Contains(code)) found.Add(code);
            }
            return found;
        }

        /// <summary>
        /// 血量："SCP-173 2400/3000"、"SCP 173 HP: 2400"、"SCP-1 73 2400"、
        /// 以及 HUD 上常见的省略写法 "173  2400/3000"。
        /// 编号允许被空格拆开，所以这里是按“已知编号”现拼正则去原文本里找，
        /// 不依赖去掉空格后的串 —— 那样数字会粘在一起分不开。
        /// 所有已知编号都会试一遍（不是只认某一个）。
        /// </summary>
        private static List<TacticalUpdate> DetectHp(string rawText)
        {
            List<TacticalUpdate> list = new List<TacticalUpdate>();
            string text = rawText.ToLowerInvariant();

            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;      // 079 没有血量

                // "173" → "1\s*7\s*3"，中间允许 OCR 插进来的空格
                StringBuilder sb = new StringBuilder();
                for (int d = 0; d < code.Length; d++)
                {
                    if (d > 0) sb.Append("\\s*");
                    sb.Append(code[d]);
                }

                // 前缀 "SCP" 是可以省的：HUD 上经常只写 "173  2400/3000"。
                // 前面加 (?<![0-9]) 是为了别在 "1106 3000" 里认出一个 "106"。
                Regex re = new Regex(
                    "(?<![0-9])(?:(?:scp|5cp|sgp)\\s*[-_#:]*\\s*)?" + sb.ToString()
                    + "[^0-9]{0,20}?([0-9]{2,6})(?:\\s*[/|]\\s*([0-9]{2,6}))?",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                Match m = re.Match(text);
                if (!m.Success) continue;

                int cur;
                if (!int.TryParse(m.Groups[1].Value, out cur)) continue;

                int total = 0;
                if (m.Groups[2].Success) int.TryParse(m.Groups[2].Value, out total);

                // 合理性检查：太少的多半是百分比、柱状图刻度之类，不要
                if (cur < 100 || cur > 100000) continue;
                if (total > 0 && (total < cur || total > 100000)) total = 0;

                TacticalUpdate u = new TacticalUpdate();
                u.What = TacticalUpdate.Kind.SetHp;
                u.Code = code;
                u.A = cur;
                u.B = total;
                list.Add(u);
            }
            return list;
        }

        /// <summary>
        /// "一次掉很多"的怀疑门槛：单次读数掉掉超过上限这个百分比就先挂起。
        ///
        /// 【为什么是 25%】操作者提醒有的武器 **DPS 上千**，一次轮询掉掉大半管血
        /// 是正常的 —— 所以这个门槛**不能定高**，定高了就会把真实伤害当误读拦掉。
        /// 它的作用不是"判真假"，只是"挑出值得复核的那几次"；
        /// 真假由下面的持续性判据决定。定在 25% 能覆盖掉绝大多数普通交火，
        /// 只对真正的大跳做一次复核。
        /// </summary>
        private const int HpDropSuspectPercent = 25;

        /// <summary>
        /// 挂起的"待确认大幅掉血"：编号 → 第一次读到的值。
        /// 下一次读到"在它附近或更低"就确认。
        /// </summary>
        private readonly Dictionary<string, int> _bigDropPending =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 判定这次"大幅掉血"读数能不能采纳。返回 false = 先挂起，等下一次读数。
        ///
        /// 【判据】不是"两次读得一样"，而是"这次不比上次高"：
        ///   · 真掉血（尤其高 DPS）每次读数都在往下走，本来就不可能相等，
        ///     所以容差给到**上限的 25%**，并且允许"更低"；
        ///   · 一次性误读下一帧就跳回高位，那时 `read > pending` 成立 →
        ///     不确认，而且会把 pending 更新成新的高位读数（重新开始等）。
        /// </summary>
        private bool WaitBigDropConfirm(string code, int read, int totalHp)
        {
            int pending;
            if (_bigDropPending.TryGetValue(code, out pending))
            {
                int tol = totalHp / 4;                 // 容差 = 上限的 25%
                if (tol < 50) tol = 50;

                // 在第一次读数附近 → 确认
                if (read <= pending + tol)
                {
                    _bigDropPending.Remove(code);
                    return true;
                }
            }

            // 第一次（或读数比挂起值高很多，说明上次是误读）→ 重新挂起
            _bigDropPending[code] = read;
            return false;
        }

        /// <summary>这个 SCP 有没有挂着的"待确认大幅掉血"。</summary>
        private bool HasPendingBigDrop(string code)
        {
            return _bigDropPending.ContainsKey(code);
        }

        /// <summary>
        /// 每次 Apply 开头检查一下"回合是不是换了"。
        ///
        /// 【为什么这样做】OcrTacticalLink 是本类内部创建的，没法从
        /// AlertManager 那边直接调到；但它每收到一条识别结果都会走 Apply。
        /// 这里比对 TacticalLog.RoundStartedAt（每局开始会被重置），
        /// 发现变了就清掉"按回合"的状态 —— 不用再往外面加接线。
        /// </summary>
        private DateTime _stateRoundAt = DateTime.MinValue;

        private void SyncRoundState(TacticalLog log)
        {
            if (log == null) return;
            if (log.RoundStartedAt == _stateRoundAt) return;

            _stateRoundAt = log.RoundStartedAt;
            _hpFirstLocked.Clear();      // 血量位置每回合重新比较
            _bigDropPending.Clear();     // 挂起的大幅掉血读数过期
        }

        /// <summary>
        /// 新一局开始时调用：清掉"本回合血量位置判定"和挂起的"大幅掉血"读数。
        ///
        /// 这两份状态都是**按回合**定的：
        ///   · 血量位置（HP 是第一个数还是 HS 是第一个数）每回合重新比较一次，
        ///     因为不同服务器/不同局的显示可能不一样；
        ///   · 挂起的大幅掉血读数过期就没意义了。
        /// 不清的话上一局的判定会带到下一局，血量可能一直认定错位置。
        /// </summary>
        public void OnNewRound()
        {
            _hpFirstLocked.Clear();
            _bigDropPending.Clear();
        }

        /// <summary>
        /// SCP 血量表：从 "HP:当前/上限 HS:护盾" 这种格式里读血量。
        ///
        /// 【这个格式从哪来】画面中偏右那块清单，实测长这样（白字，但只有 ~11px 高）：
        ///     [SCP-173]HP:4405.871/4500 HS:0        所在区域:重收
        ///     [SCP-939]HP:1235.96/2500  HS:527.11   所在区域:重收
        ///     [SCP-3114]HP:2266.853/2300 HS:350     所在区域:重收
        ///     [SCP-049]HP:1681.272/2700 HS:389.9724 所在区域:重收
        ///
        /// 【为什么要做容错】字太小，实测 OCR 会稳定地把
        ///   "SCP" 读成 "SOP"/"SecP"、"173" 读成 "178"、"4500" 读成 "s500"、
        ///   ":" 读成 "." 或 "!"、斜杠直接漏掉。
        /// 所以正则放宽到能吃下这些常见错法，编号再用"最接近的已知编号"兜住。
        ///
        /// 【怎么区分 HP 和 HS】HP 是**第一个**数、HS 是第二个（HS: 后面）。
        /// 操作者的经验是"血量一般比护盾多，刚开局一定这样"，所以额外做一道保险：
        /// 每回合第一次读到某一行时比较两个数，**大的那个当血量**并把结论锁死
        /// 整个回合 —— 之后就一直盯那个位置，不再重新判断。
        /// </summary>
        private static readonly Regex ReScpHp = new Regex(
            "h\\s*[pvb]\\s*[:：.!！]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// "HP" 之后的两个数字：第一个是当前血量、第二个是上限。
        /// 中间允许任意非数字字符（斜杠有没有都行 —— 实测 OCR 经常把斜杠漏掉）。
        /// </summary>
        private static readonly Regex ReScpHpPair = new Regex(
            "h\\s*[pvb]\\s*[:：.!！]?\\s*[^0-9]{0,4}([0-9oils]{1,6})(?:\\.[0-9]+)?"
          + "[^0-9]{1,6}([0-9oils]{1,6})(?:\\.[0-9]+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 把 OCR 常见误认的字符换回数字：
        /// s/S→5、o/O→0、i/l/I→1、B→8、Z→2、G→6。
        /// </summary>
        private static string FixDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case 's': case 'S': sb.Append('5'); break;
                    case 'o': case 'O': sb.Append('0'); break;
                    case 'i': case 'I': case 'l': case 'L': sb.Append('1'); break;
                    case 'B': sb.Append('8'); break;
                    case 'Z': sb.Append('2'); break;
                    case 'G': sb.Append('6'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 从 "HP:1255.96/2500" 里取"当前血量 / 上限"。
        ///
        /// 【为什么要先把斜杠前面整段切出来】实测 OCR 读 "1255.96/2500" 时，
        /// 如果只按"第一个数字"抓，会把小数点后的 "255.96" 当成第一个数，
        /// 血量就变成 255 了。所以这里先在斜杠处切开：
        /// 斜杠前那一段里取第一个数字并丢掉小数（"1255.96" → 1255），
        /// 斜杠后再取第一个数字当上限。
        /// 斜杠整个漏掉时（"2266.8537239EHSS350"）退化成
        /// "先抓一段数字当血量、再抓后面那段当上限"。
        /// </summary>
        private static bool TryExtractTwoNumbers(string line, out int cur, out int max)
        {
            cur = 0; max = 0;
            if (string.IsNullOrEmpty(line)) return false;

            Match head = Regex.Match(line, "h\\s*[pvb]\\s*[:：.!！]?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!head.Success) return false;

            int start = head.Index + head.Length;
            if (start >= line.Length) return false;
            string rest = line.Substring(start);

            // 斜杠优先（半角/全角/反斜杠/竖线都算）
            int bar = -1;
            for (int i = 0; i < rest.Length; i++)
            {
                char c = rest[i];
                if (c == '/' || c == '\\' || c == '|') { bar = i; break; }
            }

            string left, right;
            if (bar >= 0)
            {
                left = rest.Substring(0, bar);
                right = rest.Substring(bar + 1);
            }
            else
            {
                // 没有斜杠：第一段数字当血量，剩下的交给上限
                int i = 0;
                while (i < rest.Length && !IsDigitish(rest[i])) i++;
                int numStart = i;
                while (i < rest.Length && (IsDigitish(rest[i]) || rest[i] == '.')) i++;
                left = rest.Substring(numStart, i - numStart);
                right = i < rest.Length ? rest.Substring(i) : "";
            }

            if (!int.TryParse(FixDigits(FirstNumber(left)), out cur)) return false;
            if (!int.TryParse(FixDigits(FirstNumber(right)), out max)) return false;

            // ---- 把 OCR 用空格切开的数字补回来 ----
            // 实测 "1 255.96" 其实是 "1255.96"：斜杠前那一段被空格切成两截。
            // 用"按空格切再拼前两段"的办法判断，而不是靠数字字符中间断没断
            //（"255.96" 本身是连续的，靠字符断点看不出来）。
            if (cur < 100)
            {
                string[] toks = left.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length >= 2)
                {
                    int head2, tail2;
                    if (int.TryParse(FixDigits(FirstNumber(toks[0])), out head2)
                        && int.TryParse(FixDigits(FirstNumber(toks[1])), out tail2))
                    {
                        string joined = head2.ToString() + tail2.ToString();
                        int merged;
                        if (joined.Length <= 4 && int.TryParse(joined, out merged)) cur = merged;
                    }
                }
            }

            // ---- 合理性检查：把明显的误读挡掉 ----
            // 【为什么需要】这块字只有 ~11px，OCR 经常把数字糊在一起
            // （"2300" 会糊成 "55350" 这种离谱值）。
            // 垃圾值放进去会让血量自己乱跳、影响力跟着乱算，所以宁可丢掉这一行
            // —— 下一轮还会再读一次，掉血是持续过程，偶尔丢一帧没关系。
            //
            // 【为什么上限卡在 20000】"2300" 会糊成 "55350"（明显不合理，挡掉）；
            // 但 "4500" 实测会被读成 "5500"（只错第一位，量级还正常）——
            // 这种没法从数值上判断对错，只能留着。
            // 掉血判定只依赖**血量相对于上限的比例**，上限错一位会让阈值偏一点，
            // 但不会让逻辑崩掉；所以这里只挡离谱的，不追求数值完全准。
            if (max < 50 || max > 20000) return false;
            if (cur < 0 || cur > 99999) return false;
            if (cur > max) return false;                 // 当前血量不可能超过上限

            return true;
        }

        private static bool IsDigitish(char c)
        {
            return (c >= '0' && c <= '9') || c == 'o' || c == 'O' || c == 's' || c == 'S'
                || c == 'i' || c == 'I' || c == 'l' || c == 'L' || c == 'B'
                || c == 'Z' || c == 'G';
        }

        /// <summary>取一段文字里"第一个数字"（丢掉小数部分）。</summary>
        private static string FirstNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = 0;
            while (i < s.Length && !IsDigitish(s[i])) i++;
            int j = i;
            while (j < s.Length && (IsDigitish(s[j]) || s[j] == '.')) j++;

            string seg = s.Substring(i, j - i);
            int dot = seg.IndexOf('.');
            if (dot >= 0) seg = seg.Substring(0, dot);
            return seg;
        }

        /// <summary>
        /// 每一行（SCP 编号）本回合认定的"血量在哪一边"：
        /// true = 斜杠前那个数是血量（正常情况），false = 用 HS 那个数当血量。
        /// </summary>
        private readonly Dictionary<string, bool> _hpFirstLocked =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>把 SCP 血量表的一行解析成 (编号, 当前血量, 血量上限)。</summary>
        internal static bool TryParseScpHpLine(string raw, out string code,
            out int cur, out int max)
        {
            code = null; cur = 0; max = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            // 编号：优先用文本里的 SCP-xxx；实在没有就用整段文字去模糊匹配已知编号
            code = FindCodeNear(raw);
            if (code == null) return false;

            if (!TryExtractTwoNumbers(raw, out cur, out max)) return false;
            return true;
        }

        /// <summary>
        /// 在文字里找一个 SCP 编号（"SCP-173" / "sop-178" / 光秃秃的 "173"）。
        /// 认出来的编号要跟已知编号对上 —— "178" 不在表里时会找最接近的
        /// （178 → 173，这是实测里最常见的一种误读）。
        /// </summary>
        private static string FindCodeNear(string raw)
        {
            Match m = Regex.Match(raw, "(?:scp|5cp|sgp|sop|secp|sup)\\s*[-_#:.]*\\s*([0-9]{2,4})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
            {
                string hit = OcrTacticalLink.MatchKnownCode(m.Groups[1].Value);
                if (hit != null) return hit;
            }

            // 没有 "SCP" 前缀时，扫一遍已知编号（要求不是更长数字的一部分）
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string c = TacticalLog.ScpCodes[i];
                if (Regex.IsMatch(raw, "(?<![0-9])" + c + "(?![0-9])",
                    RegexOptions.CultureInvariant))
                    return c;
            }
            return null;
        }

        /// <summary>血量表整块文字 → 每个 SCP 一条 SetHp。</summary>
        private List<TacticalUpdate> DetectScpHpBlock(string raw)
        {
            List<TacticalUpdate> list = new List<TacticalUpdate>();

            // 按行拆（血量表一行一个 SCP）；没有换行符号时退化成整段扫
            string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) lines = new string[] { raw };

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("hp", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string code;
                int cur, max;
                if (!TryParseScpHpLine(lines[i], out code, out cur, out max)) continue;

                // ---- 本回合第一次读到这个编号 → 定"血量在哪一边"并锁死 ----
                bool lockedFirst;
                if (!_hpFirstLocked.TryGetValue(code, out lockedFirst))
                {
                    // 操作者的经验：血量一般比护盾多，刚开局一定这样。
                    // 所以第一次比较两个数，大的那个当血量。
                    lockedFirst = IsHpBiggerThanShield(lines[i]);
                    _hpFirstLocked[code] = lockedFirst;
                    Logger.On("血量", "本回合锁定 SCP-" + code + " 的血量位置："
                        + (lockedFirst ? "HP 那个数（斜杠前）" : "用 HS 那个数")
                        + "（依据：两个数值比较，大的算血量）");
                }

                if (!lockedFirst) continue;   // 这一行判定是"护盾比血多"，本回合不信它

                TacticalUpdate u = new TacticalUpdate();
                u.What = TacticalUpdate.Kind.SetHp;
                u.Code = code;
                u.A = cur;
                u.B = max;
                list.Add(u);
            }
            return list;
        }

        /// <summary>
        /// HP 那个数是不是比 HS 大（比不出来时按"是"处理，这是常态）。
        ///
        /// 操作者的经验：**血量一般比护盾多，刚开局一定这样**。
        /// 所以每回合第一次读到某个 SCP 时用这条定"哪个数是血量"，然后锁死整回合。
        /// </summary>
        private static bool IsHpBiggerThanShield(string line)
        {
            if (string.IsNullOrEmpty(line)) return true;

            int hp, max;
            if (!TryExtractTwoNumbers(line, out hp, out max)) return true;   // 读不出来 → 按常态

            // 护盾：HP 后面那段 "HS:数字" 里的数字
            Match hs = Regex.Match(line, "h\\s*s\\s*[:：.!！]?\\s*([0-9]{1,6})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!hs.Success) return true;                                    // 没读到 HS → 按常态

            int shield;
            if (!int.TryParse(hs.Groups[1].Value, out shield)) return true;

            // 血量一般比护盾多（用读到的 HP 那个数跟护盾比）
            return hp >= shield;
        }

        /// <summary>发电机：从“发电机2启动中 / GEN 3 剩余 90 / 发电机 一 已启动”里认状态。</summary>
        private static List<TacticalUpdate> DetectGenerators(string flat)
        {
            List<TacticalUpdate> list = new List<TacticalUpdate>();

            // ---- 形式一：数字在关键词**前面** ----
            // C.A.S.S.I.E. 的中文播报，关键词是"**发电站**"（不是发电机），
            // 动词是"已被激活"。
            //
            // 【口径（操作者确认）】"3 分之 3"不是"第 3 台"，而是**已经启动了三台**
            // —— 中文分数 A 分之 B = B/A，分子才是"已激活的台数"。
            // 以前把它当"第几台"，于是只认出"3 分之 3"这一条时面板只写了 1/3
            // （日志实锤：原文"3 分之 3 的应急发电站已被激活"→ 只标了发电机 3）。
            // 现在按"已启动 N 台"处理：前 N 台一起确认，中间漏读播报也不影响总数。
            // 谁激活的不知道也没关系 —— 需求是"至少把激活状态记下来"，
            // 阵营留空（面板会显示"未指定"）。
            foreach (Match m in ReGenFraction.Matches(flat))
            {
                int count = FractionCount(GenNumber(m.Groups[1].Value),
                                         GenNumber(m.Groups[2].Value));
                if (count < 0) continue;

                for (int i = 0; i < count && i < TacticalLog.GeneratorCount; i++)
                    list.Add(MakeGenActive(i));
            }

            // ---- 形式一附：收尾播报"所有的应急发电站已被成功激活" → 三台全确认 ----
            if (ReGenAll.IsMatch(flat))
            {
                for (int i = 0; i < TacticalLog.GeneratorCount; i++)
                    list.Add(MakeGenActive(i));
            }

            // ---- 形式二：关键词后面跟数字（"发电机 2 剩余 90"）----
            foreach (Match m in ReGen.Matches(flat))
            {
                int idx = GenIndex(m.Groups[1].Value);
                if (idx < 0) continue;

                TacticalUpdate u = MakeGenUpdate(idx, Tail(flat, m.Index + m.Length));
                if (u != null) list.Add(u);
            }
            return list;
        }

        /// <summary>造一条"这一台已经（被播报确认）完全启动"的改动。</summary>
        private static TacticalUpdate MakeGenActive(int idx)
        {
            TacticalUpdate u = new TacticalUpdate();
            u.What = TacticalUpdate.Kind.SetGenerator;
            u.A = idx;
            u.Flag = true;
            return u;
        }

        /// <summary>取关键词后面的一小段，用来判断"启动中 / 已启动 / 关了 / 还剩多少秒"。</summary>
        private static string Tail(string flat, int from)
        {
            int len = Math.Min(16, flat.Length - from);
            return len > 0 ? flat.Substring(from, len) : "";
        }

        /// <summary>把"关键词后面那一小段文字"翻译成一条发电机改动；认不出来返回 null。</summary>
        private static TacticalUpdate MakeGenUpdate(int idx, string tail)
        {
            TacticalUpdate u = new TacticalUpdate();
            u.What = TacticalUpdate.Kind.SetGenerator;
            u.A = idx;

            if (tail.IndexOf("done") >= 0 || tail.IndexOf("ready") >= 0
                || tail.IndexOf("online") >= 0 || tail.IndexOf("complete") >= 0
                || tail.IndexOf("activat") >= 0
                || tail.IndexOf("已启动") >= 0 || tail.IndexOf("就绪") >= 0
                || tail.IndexOf("完成") >= 0 || tail.IndexOf("激活") >= 0
                || tail.IndexOf("已开启") >= 0)
            {
                u.Flag = true;
                return u;
            }

            if (tail.IndexOf("off") >= 0 || tail.IndexOf("idle") >= 0
                || tail.IndexOf("未启动") >= 0 || tail.IndexOf("关闭") >= 0)
            {
                u.Off = true;
                return u;
            }

            // 剩下的情况：把后面的数字当成"剩余秒数"
            Match num = Regex.Match(tail, "([0-9]{1,4})");
            if (num.Success)
            {
                int sec;
                if (int.TryParse(num.Groups[1].Value, out sec) && sec >= 0 && sec <= 600)
                {
                    u.B = sec;
                    return u;
                }
                return null;      // 数字不合理 → 这条丢掉
            }

            // 光写了"启动中"没写秒数：按默认满速开始跑
            u.B = -1;
            return u;
        }

        /// <summary>
        /// "A 分之 B 的应急发电站已被激活"里**已经启动了几台**。
        ///
        /// 中文分数 A 分之 B = B/A：A 是分母（总台数 3），B 是分子（已启动台数），
        /// 所以正常情况下取**后**一个数：
        ///   · 3 分之 1 → 1 台
        ///   · 3 分之 2 → 2 台
        ///   · 3 分之 3 → 3 台（三台全开，不是"第 3 台"）
        /// 语序反过来的老写法（"1 分之 3"，操作者早期报过一次）取前一个数（1 台）；
        /// 分子漏读（"分之 2"）时后一个数就是台数。
        /// 两个数都不是 3（OCR 把 3 认成了 8 之类）时不猜，直接丢掉 ——
        /// 认错总数比不认更糟。
        /// </summary>
        private static int FractionCount(int a, int b)
        {
            int total = TacticalLog.GeneratorCount;

            if (a < 0) return (b >= 1 && b <= total) ? b : -1;      // 分母被漏读
            if (a == total && b >= 1 && b <= total) return b;       // 正常写法：B/3
            if (b == total && a >= 1 && a <= total) return a;       // 语序反过来
            return -1;
        }

        /// <summary>把"1/2/3/一/二/三"读成台号（1 起数；认不出返回 -1）。</summary>
        private static int GenNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            if (s == "1" || s == "一") return 1;
            if (s == "2" || s == "二") return 2;
            if (s == "3" || s == "三") return 3;
            int n;
            if (int.TryParse(s, out n) && n >= 1 && n <= TacticalLog.GeneratorCount) return n;
            return -1;
        }

        private static int GenIndex(string s)
        {
            if (s == "1" || s == "一") return 0;
            if (s == "2" || s == "二") return 1;
            if (s == "3" || s == "三") return 2;
            return -1;
        }
    }
}
