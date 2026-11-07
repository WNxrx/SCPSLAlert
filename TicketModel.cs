using System;
using System.Collections.Generic;

namespace SCPSLAlert
{
    /// <summary>
    /// 影响力 / 重生令牌模型 —— 完全按 机制\刷新.txt 实现。
    ///
    /// 【为什么要有这个类】
    ///   旧代码里的"票数"是**凭经验编的**：
    ///       总票数 = 累计已损失血量 ÷ 200（向下取整）+ 击杀数 × 15
    ///   官方的机制根本不是这样。文档里写得很清楚：
    ///     · 影响力（influence）是**按目标累加**的，每个目标给固定的影响力；
    ///     · 影响力达到阈值才发一个重生令牌，阈值是 **40 / 80 / 150 / 200**；
    ///     · 每回合最多拿 3 个额外令牌，而且这个上限是**两方共享**的
    ///       （所以默认一回合最多 5 次重生波）；
    ///     · 每个团队【默认各有 1 个】令牌，重生一次消耗一个；
    ///     · 没有令牌时刷新计时器变灰、倒到 30 秒就停住，直到拿到令牌才继续。
    ///
    /// 【目标表（文档 1.2.3）】
    ///   行动                                  计时器   影响力
    ///   击杀一个可玩 SCP（不含 049-2）          −10s     +15
    ///   打掉某个 SCP 最大生命的 10% 以上        −1s      +2     （该 SCP 死亡时不另算）
    ///   首次使用某个"独特"SCP 物品              −5s      +2
    ///   使用一个"消耗性"SCP 物品                −2s      +1
    ///   击杀一名敌对武装人员                    −2s      +1     （持武器的平民也算）
    ///   发电机成功启动                          −10s     +3     （本回合没有 SCP-079 时不给计时奖励）
    ///   ── MTF 专属 ──
    ///   科学家未被捆绑逃离设施                  −30s     +5
    ///   被捆绑的 D 级人员逃离设施               −10s     +5
    ///   ── 混沌专属 ──
    ///   平民单位逃离后变成混沌分裂者            −20s     +5
    ///                                                          （未被捆绑的 D 级 / 已捆绑的科学家）
    ///
    ///   消耗性 SCP 物品：反物质可乐、SCP-207、SCP-500、SCP-1853、SCP-2176
    ///   独特   SCP 物品：SCP-018、SCP-244、SCP-268、SCP-1344、SCP-1576
    ///   （在 SCP-914 里造出来的物品不算，必须是自然生成的）
    /// </summary>
    internal class TicketModel
    {
        public const string MTF = "MTF";
        public const string CHAOS = "Chaos";

        // ---------------- 目标类型 ----------------

        /// <summary>文档里列出的每一种目标。</summary>
        public enum Objective
        {
            KillScp,            // 击杀可玩 SCP            −10s  +15
            DamageScp10,        // 打掉 SCP 10% 最大生命   −1s   +2
            UniqueScpItem,      // 首次使用独特 SCP 物品    −5s   +2
            ConsumableScpItem,  // 使用消耗性 SCP 物品      −2s   +1
            KillArmed,          // 击杀敌对武装人员         −2s   +1
            ActivateGenerator,  // 发电机成功启动           −10s  +3（无 079 时不给计时）
            MtfScientistEscape, // [MTF] 科学家未捆绑逃离    −30s  +5
            MtfBondedDEscape,   // [MTF] 被捆绑 D 级逃离     −10s  +5
            ChaosCivilianEscape,// [混沌] 平民逃离转混沌     −20s  +5
        }

        /// <summary>一个目标的固定数值。</summary>
        public struct Reward
        {
            public int Seconds;     // 计时器减少多少秒
            public int Influence;   // 给多少影响力
            public bool MtfOnly;
            public bool ChaosOnly;
            public string Name;

            public Reward(int sec, int inf, string name, bool mtf, bool chaos)
            {
                Seconds = sec; Influence = inf; Name = name;
                MtfOnly = mtf; ChaosOnly = chaos;
            }
        }

        /// <summary>目标表：文档 1.2.3 的完整对照。</summary>
        private static readonly Dictionary<Objective, Reward> Table =
            new Dictionary<Objective, Reward>();

        static TicketModel()
        {
            Table[Objective.KillScp] = new Reward(10, 15, "击杀 SCP", false, false);
            Table[Objective.DamageScp10] = new Reward(1, 2, "打掉 SCP 10% 生命", false, false);
            Table[Objective.UniqueScpItem] = new Reward(5, 2, "首次使用独特 SCP 物品", false, false);
            Table[Objective.ConsumableScpItem] = new Reward(2, 1, "使用消耗性 SCP 物品", false, false);
            Table[Objective.KillArmed] = new Reward(2, 1, "击杀敌对武装人员", false, false);
            Table[Objective.ActivateGenerator] = new Reward(10, 3, "发电机成功启动", false, false);
            Table[Objective.MtfScientistEscape] = new Reward(30, 5, "科学家未被捆绑逃离", true, false);
            Table[Objective.MtfBondedDEscape] = new Reward(10, 5, "被捆绑 D 级逃离", true, false);
            Table[Objective.ChaosCivilianEscape] = new Reward(20, 5, "平民逃离转为混沌", false, true);
        }

        public static Reward Info(Objective o)
        {
            Reward r;
            return Table.TryGetValue(o, out r) ? r : new Reward(0, 0, "未知", false, false);
        }

        /// <summary>这个目标对某一方是否适用。</summary>
        public static bool AppliesTo(Objective o, string faction)
        {
            Reward r = Info(o);
            if (r.MtfOnly) return faction == MTF;
            if (r.ChaosOnly) return faction == CHAOS;
            return true;
        }

        /// <summary>文档里的四种消耗性 SCP 物品。</summary>
        public static readonly string[] ConsumableItems =
            new string[] { "反物质可乐", "SCP-207", "SCP-500", "SCP-1853", "SCP-2176" };

        /// <summary>文档里的五种独特 SCP 物品。</summary>
        public static readonly string[] UniqueItems =
            new string[] { "SCP-018", "SCP-244", "SCP-268", "SCP-1344", "SCP-1576" };

        // ---------------- 参数（都可在 config 里改） ----------------

        /// <summary>升到"第 N 个额外令牌"所需的影响力（文档 1.2.2）。</summary>
        public int[] TokenThresholds = new int[] { 40, 80, 150, 200 };

        /// <summary>每个团队开局自带的令牌数（文档：默认各自 1 个）。</summary>
        public int StartTokens = 1;

        /// <summary>每回合最多能拿几个额外令牌（文档：3 个，且两方共享）。</summary>
        public int MaxBonusTokens = 3;

        // ---------------- 核弹相关的时间常数（机制\核弹.txt 最后一段） ----------------

        /// <summary>
        /// 两方重生令牌都用尽后，亡者协议（Dead Man's Switch）的倒计时时长。
        /// 文档："A 2.5 minute countdown will begin when both teams run out respawns."
        /// </summary>
        public const int DeadManSeconds = 150;

        /// <summary>
        /// 小波只会把亡者协议的进度往后退这么多秒（不是整个重置）。
        /// 文档："Mini-waves will only set the progress back by 40 seconds."
        /// </summary>
        public const int DeadManMiniWaveSetback = 40;

        /// <summary>
        /// 核弹引爆后，下一次重生波被强制推迟到这个冷却。
        /// 文档："Upon detonation, the next respawn wave will be forced to a four
        /// minute cooldown, regardless of their timer before the detonation."
        /// </summary>
        public const int PostDetonationWaveCooldown = 240;

        /// <summary>核弹倒计时进入"无法关闭"的剩余秒数（文档：最后 10 秒）。</summary>
        public const int InevitableSeconds = 10;

        // ---------------- 一方的状态 ----------------

        internal class Side
        {
            public string Faction = MTF;
            public int Influence;               // 累计影响力（只增不减）
            public int Spent;                   // 已经用掉几个令牌（重生了几波）

            /// <summary>
            /// 最近一次死亡 HUD 读到的可用令牌数（-1 = 还没读到）。之后用新增奖励和主波
            /// 相对校准时的差额推进；重生后停止令牌 OCR 仍可继续估算。
            /// </summary>
            public int ScreenTokens = -1;
            public int ScreenBonusAnchor;
            public int ScreenSpentAnchor;
            // 中途加入时历史令牌消耗未知，令牌数只能用概率表达。
            public bool HistoryUnknown;

            /// <summary>
            /// 屏幕影响力右边写的是 MAX：本回合这一方不会再有新令牌。
            /// 只由 OCR 的 "168/MAX" 置位，新一局 Reset() 清掉。
            /// </summary>
            public bool InfluenceMaxed;

            /// <summary>本回合已经拿到的"额外令牌"个数（不含开局自带的那个）。</summary>
            public int Bonus { get; set; }

            /// <summary>按影响力，本回合总共该拿到几个额外令牌（不受共享上限影响）。</summary>
            public int EarnedByInfluence(int[] th)
            {
                int n = 0;
                for (int i = 0; i < th.Length; i++) if (Influence >= th[i]) n++;
                return n;
            }

            /// <summary>可用令牌：有屏幕读数就以它为准，否则 = 开局自带 + 额外令牌 − 已用掉。</summary>
            public int Available(int start, int bonus)
            {
                if (ScreenTokens >= 0)
                {
                    int calibrated = ScreenTokens + (Bonus - ScreenBonusAnchor)
                        - (Spent - ScreenSpentAnchor);
                    return calibrated < 0 ? 0 : calibrated;
                }
                int v = start + bonus - Spent;
                return v < 0 ? 0 : v;
            }
        }

        private Side _mtf = new Side();
        private Side _chaos = new Side();

        public TicketModel()
        {
            Reset();
        }

        public void Reset()
        {
            _mtf = new Side(); _mtf.Faction = MTF;
            _chaos = new Side(); _chaos.Faction = CHAOS;
            _sharedBonus = 0;
            _log.Clear();
        }

        /// <summary>中途加入：当前影响力可校准，但历史令牌消耗未知。</summary>
        public void MarkMidRoundHistoryUnknown()
        {
            _mtf.HistoryUnknown = true;
            _chaos.HistoryUnknown = true;
        }

        private Side Of(string faction)
        {
            return faction == CHAOS ? _chaos : _mtf;
        }

        // ---------------- 共享上限 ----------------

        /// <summary>两方合计已经发出去的额外令牌数（文档：上限两方共享）。</summary>
        private int _sharedBonus;

        /// <summary>共享额度还剩几个。</summary>
        public int BonusTokensLeft
        {
            get { int v = MaxBonusTokens - _sharedBonus; return v < 0 ? 0 : v; }
        }

        /// <summary>
        /// 重新按影响力结算"该发多少额外令牌"，并吃共享额度。
        /// 每次记目标之后都要调一次 —— 影响力只增，所以令牌只多不少。
        /// </summary>
        private void Settle()
        {
            SettleSide(_mtf);
            SettleSide(_chaos);
        }

        private void SettleSide(Side s)
        {
            if (s.InfluenceMaxed) return;          // 游戏已经写 MAX，不再发新令牌

            int want = s.EarnedByInfluence(TokenThresholds);
            while (s.Bonus < want)
            {
                if (BonusTokensLeft <= 0) break;   // 两方共享的上限用完了
                s.Bonus++;
                _sharedBonus++;
            }
        }

        // ---------------- 记目标 ----------------

        /// <summary>记一次目标。返回这次拿到的影响力（0 表示这个目标对那一方不适用）。</summary>
        public int Record(Objective o, string faction, bool scp079Present)
        {
            return RecordAt(o, faction, scp079Present, DateTime.Now, null);
        }

        /// <summary>
        /// 同上，但由调用方指定发生时刻和附加说明。
        ///
        /// 【为什么允许指定时刻】小波判定要问"主波之后这 2 分钟里拿到了多少影响力"，
        /// 那是个**时间窗口**查询。生产代码用"现在"，但测试必须能造出
        /// "20 秒前拿了 5 点、5 分钟后拿了 10 点"这种时间线，否则窗口逻辑没法验证。
        /// </summary>
        public int RecordAt(Objective o, string faction, bool scp079Present,
            DateTime at, string detail)
        {
            if (faction != MTF && faction != CHAOS) return 0;
            if (!AppliesTo(o, faction)) return 0;

            Reward r = Info(o);
            int influence = r.Influence;
            int seconds = r.Seconds;

            // 文档：发电机"如果回合内没有 SCP-079 则不给予计时奖励"
            //（影响力照给）
            if (o == Objective.ActivateGenerator && !scp079Present) seconds = 0;

            Side s = Of(faction);
            s.Influence += influence;
            Settle();

            AddLog(at, faction, r.Name, influence, seconds, detail);
            return influence;
        }

        /// <summary>记一次目标，并顺带记下"这次击杀的是哪个 SCP / 什么名字"。</summary>
        public int Record(Objective o, string faction, bool scp079Present, string detail)
        {
            return RecordAt(o, faction, scp079Present, DateTime.Now, detail);
        }

        /// <summary>
        /// 按权重把一次目标的影响力分摊给若干方（权重之和为 1）。
        ///
        /// 【为什么需要】SCP 掉血时，我们并不真的知道是谁在打 —— 只能按
        /// "当时场上有哪几方刷新的援军"来推断。操作者定的规则是：
        ///   · 只有一方在场 → 全算它；
        ///   · 大波之后另一方又刷了小波（两方都在）→ **平均分**；
        ///   · 小波和大波是同一方 → 其实只有一方，全算它。
        /// 用"权重"表达最直接：weights[f] 就是那一方该拿的比例。
        /// </summary>
        public int RecordSplit(Objective o, Dictionary<string, double> weights,
            bool scp079Present, DateTime at, string detail, out string splitNote)
        {
            splitNote = "";
            if (weights == null || weights.Count == 0) return 0;

            Reward r = Info(o);

            // 先把有效的（比例 > 0、而且这个目标对它适用）挑出来
            List<KeyValuePair<string, double>> use = new List<KeyValuePair<string, double>>();
            double sum = 0;
            foreach (KeyValuePair<string, double> kv in weights)
            {
                if (kv.Value <= 0) continue;
                if (kv.Key != MTF && kv.Key != CHAOS) continue;
                if (!AppliesTo(o, kv.Key)) continue;
                use.Add(kv);
                sum += kv.Value;
            }
            if (use.Count == 0) return 0;

            // 先算每方该拿多少（技能/影响力那部分四舍五入，余数补到最后一方）
            int[] inf = new int[use.Count];
            int[] sec = new int[use.Count];
            int given = 0;
            for (int i = 0; i < use.Count; i++)
            {
                double w = use[i].Value / sum;
                if (i == use.Count - 1) inf[i] = r.Influence - given;
                else inf[i] = (int)Math.Round(r.Influence * w);
                if (inf[i] < 0) inf[i] = 0;
                given += inf[i];

                // 发电机那条：没有 079 时不给计时奖励（影响力照给）
                bool giveSec = (o != Objective.ActivateGenerator) || scp079Present;
                sec[i] = giveSec ? (int)Math.Round(r.Seconds * w) : 0;
            }

            int totalInf = 0;
            for (int i = 0; i < use.Count; i++)
            {
                string f = use[i].Key;
                double w = use[i].Value / sum;

                if (inf[i] > 0)
                {
                    Side s = Of(f);
                    s.Influence += inf[i];
                    totalInf += inf[i];
                }

                if (inf[i] > 0 || sec[i] > 0)
                    AddLog(at, f, r.Name + (use.Count > 1 ? "（分摊）" : ""),
                        inf[i], sec[i],
                        (detail == null ? "" : detail + " ") + "权重" + (int)Math.Round(w * 100) + "%");
            }

            Settle();

            if (use.Count > 1)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < use.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(FactionName(use[i].Key));
                }
                splitNote = "按 " + sb + " 平均分（共 +" + totalInf + " 影响力）";
            }
            return totalInf;
        }

        /// <summary>
        /// 某方用掉一个令牌（发生了一次重生波）。返回 false = 没令牌可用。
        /// 同时记一笔已用次数；有 HUD 基线时按基线之后的主波数递减。
        /// </summary>
        public bool SpendToken(string faction)
        {
            Side s = Of(faction);
            if (s.Available(StartTokens, s.Bonus) <= 0 && !s.HistoryUnknown) return false;

            s.Spent++;
            AddLog(faction, "重生波（消耗令牌）", 0, 0);
            return true;
        }

        /// <summary>最近一次大波被纠正为小波时，撤销对应的令牌消耗。</summary>
        public void RefundToken(string faction)
        {
            Side s = Of(faction);
            if (s.Spent > 0) s.Spent--;
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                LogEntry e = _log[i];
                if (e.Faction == faction && e.What == "重生波（消耗令牌）")
                { _log.RemoveAt(i); break; }
            }
        }

        /// <summary>记录一场确认发生的大波；即使旧估算已到 0，也保留这次实际消耗。</summary>
        public void RecordWaveSpend(string faction, DateTime at)
        {
            Side s = Of(faction);
            s.Spent++;
            AddLog(at, faction, "重生波（消耗令牌）", 0, 0, null);
        }

        /// <summary>撤销指定时间的大波令牌消耗，避免编辑旧波时撤销错账。</summary>
        public void RefundTokenAt(string faction, DateTime at)
        {
            Side s = Of(faction);
            if (s.Spent > 0) s.Spent--;
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                LogEntry e = _log[i];
                if (e.Faction == faction && e.What == "重生波（消耗令牌）" && e.At == at)
                { _log.RemoveAt(i); break; }
            }
        }

        /// <summary>某方实际重生了几波（= 花了几个令牌）。</summary>
        public int WavesOf(string faction)
        {
            return Of(faction).Spent;
        }

        /// <summary>
        /// 用屏幕上读到的影响力**校准**（操作者要求：面板和游戏数据同步）。
        ///
        /// 规则：**只升不降** —— 影响力在一局里只会涨，所以"读到的比模型高"才采纳；
        /// 低的一律忽略（多半是 OCR 读歪了，比如把 149 读成 14）。
        /// 调用方负责"要连续读到几次一样才放进来"那层确认（见 AlertManager.OnTicketRecognized）。
        /// 返回 true 表示真的改了。
        /// </summary>
        public bool CalibrateInfluence(string faction, int screenInfluence)
        {
            Side s = Of(faction);
            if (screenInfluence <= s.Influence) return false;

            int before = s.Influence;
            s.Influence = screenInfluence;
            Settle();      // 涨了可能够到阈值 → 按规则补发额外令牌
            AddLog(faction, "按屏幕票数校准影响力（" + before + " → " + screenInfluence + "）", 0, 0);
            return true;
        }

        /// <summary>
        /// 屏幕上这一方的影响力右边写的是 MAX：本回合不会再有新令牌。
        /// 这是游戏给的权威信号，比"按阈值推算"更可靠（共享额度用光时，
        /// 影响力还很低，游戏也会直接写 MAX，比如 63/MAX）。
        /// 只置位、不清除 —— OCR 偶尔把 MAX 读丢不能把已确认的状态洗回去；
        /// 新一局 Reset() 会清。返回 true = 这次真的改了。
        /// </summary>
        public bool MarkInfluenceMaxed(string faction)
        {
            Side s = Of(faction);
            if (s.InfluenceMaxed) return false;
            s.InfluenceMaxed = true;
            AddLog(faction, "屏幕影响力 = MAX（本回合不再有新令牌）", 0, 0);
            return true;
        }

        /// <summary>
        /// 用屏幕上读到的"还能重生几次"校准这一方（操作者要求：面板和游戏数据同步）。
        ///
        /// 直接记成屏幕读数（ScreenTokens），之后 TokensOf 就以它为准；
        /// 发生重生波时 SpendToken 会在它上面 **−1**（操作者要求）；
        /// 下次再读到屏幕上的值还会重新对齐。
        /// 返回 true 表示真的改了。
        /// </summary>
        public bool CalibrateTokens(string faction, int screenTokens)
        {
            if (screenTokens < 0) return false;
            Side s = Of(faction);

            int cur = s.Available(StartTokens, s.Bonus);
            if (cur == screenTokens && s.ScreenTokens == screenTokens) return false;

            s.ScreenTokens = screenTokens;
            s.ScreenBonusAnchor = s.Bonus;
            s.ScreenSpentAnchor = s.Spent;
            s.HistoryUnknown = false;
            AddLog(faction, "按屏幕票数校准令牌（" + cur + " → " + screenTokens + "）", 0, 0);
            return true;
        }

        /// <summary>
        /// 撤销最后一次"记目标"（影响力回退、令牌重算）。
        /// 只回退目标，不动"重生波消耗令牌"那种记录 —— 那个是事实，不能撤。
        /// 返回 false = 没有可撤的。
        /// </summary>
        public bool UndoLast()
        {
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                LogEntry e = _log[i];
                if (e.Influence <= 0) continue;      // 只撤有影响力的目标
                Side s = Of(e.Faction);
                s.Influence -= e.Influence;
                if (s.Influence < 0) s.Influence = 0;
                _log.RemoveAt(i);
                RebuildBonus();
                return true;
            }
            return false;
        }

        /// <summary>撤销之后按当前影响力把"额外令牌"重新算一遍（可能变少）。</summary>
        private void RebuildBonus()
        {
            _mtf.Bonus = 0;
            _chaos.Bonus = 0;
            _sharedBonus = 0;
            Settle();
        }

        // ---------------- 查询 ----------------

        public int InfluenceOf(string faction)
        {
            return Of(faction).Influence;
        }

        /// <summary>某方当前还能重生几个令牌。</summary>
        public int TokensOf(string faction)
        {
            Side s = Of(faction);
            return s.Available(StartTokens, s.Bonus);
        }

        public bool HasUnknownHistory(string faction)
        {
            return Of(faction).HistoryUnknown && Of(faction).ScreenTokens < 0;
        }

        public double UnknownHistoryRespawnChance(string faction)
        {
            Side s = Of(faction);
            int supply = StartTokens + s.Bonus;
            // 已观察到的加入后主波会排除“历史已消耗更多令牌”的不可能状态。
            int remainingBeforeUnknownSpend = Math.Max(0, supply - s.Spent);
            return remainingBeforeUnknownSpend <= 0
                ? 0
                : (double)remainingBeforeUnknownSpend / (remainingBeforeUnknownSpend + 1);
        }

        /// <summary>某方本回合拿到的额外令牌数。</summary>
        public int BonusOf(string faction)
        {
            return Of(faction).Bonus;
        }

        /// <summary>
        /// 距离下一个令牌还差多少影响力。
        /// 已经拿满（或共享额度用完）时返回 -1。
        /// </summary>
        public int ToNextToken(string faction)
        {
            Side s = Of(faction);
            if (s.InfluenceMaxed) return -1;                    // 游戏自己写的 MAX
            int have = s.EarnedByInfluence(TokenThresholds);
            if (have >= TokenThresholds.Length) return -1;      // 四个阈值全过了
            if (BonusTokensLeft <= 0) return -1;                // 共享额度没了，谁也拿不到新令牌

            int need = TokenThresholds[have] - s.Influence;
            return need < 0 ? 0 : need;
        }

        /// <summary>下一个令牌需要的影响力总数值（没有下一个时返回 -1）。</summary>
        public int NextTokenThreshold(string faction)
        {
            Side s = Of(faction);
            if (s.InfluenceMaxed) return -1;                    // 游戏自己写的 MAX
            if (BonusTokensLeft <= 0) return -1;                // 共享额度没了
            int have = s.EarnedByInfluence(TokenThresholds);
            if (have >= TokenThresholds.Length) return -1;
            return TokenThresholds[have];
        }

        /// <summary>两方都没有令牌了（文档 1.2.5：这时候会启动 O5 核弹）。</summary>
        public bool BothOutOfTokens
        {
            get { return TokensOf(MTF) <= 0 && TokensOf(CHAOS) <= 0; }
        }

        // ---------------- 时间线（小波判定用） ----------------

        internal class LogEntry
        {
            public DateTime At;
            public string Faction = MTF;
            public string What = "";
            public string Detail = "";
            public int Influence;
            public int Seconds;
        }

        private readonly List<LogEntry> _log = new List<LogEntry>();

        public List<LogEntry> Entries
        {
            get { return _log; }
        }

        private void AddLog(string faction, string what, int influence, int seconds)
        {
            AddLog(DateTime.Now, faction, what, influence, seconds, null);
        }

        private void AddLog(DateTime at, string faction, string what, int influence,
            int seconds, string detail)
        {
            LogEntry e = new LogEntry();
            e.At = at;
            e.Faction = faction;
            e.What = what;
            e.Detail = detail == null ? "" : detail;
            e.Influence = influence;
            e.Seconds = seconds;
            int atIndex = _log.Count;
            while (atIndex > 0 && _log[atIndex - 1].At > e.At) atIndex--;
            _log.Insert(atIndex, e);
            if (_log.Count > 400) _log.RemoveAt(0);
        }

        /// <summary>
        /// 某个时间窗口内，该阵营通过目标拿到的**影响力**（文档 1.2.4 的小波判定）。
        /// 窗口是 [from, from+seconds]。
        /// </summary>
        public int InfluenceInWindow(string faction, DateTime from, double seconds)
        {
            int sum = 0;
            for (int i = 0; i < _log.Count; i++)
            {
                LogEntry e = _log[i];
                if (e.Faction != faction) continue;
                double d = (e.At - from).TotalSeconds;
                if (d < 0 || d > seconds) continue;
                sum += e.Influence;
            }
            return sum;
        }

        /// <summary>
        /// 时间窗口内该阵营减少的计时器秒数（用来算"距离下一波还差多少"）。
        /// </summary>
        public int SecondsCutInWindow(string faction, DateTime from, double seconds)
        {
            int sum = 0;
            for (int i = 0; i < _log.Count; i++)
            {
                LogEntry e = _log[i];
                if (e.Faction != faction) continue;
                double d = (e.At - from).TotalSeconds;
                if (d < 0 || d > seconds) continue;
                sum += e.Seconds;
            }
            return sum;
        }

        /// <summary>最近一条目标的描述（给面板/日志看）。</summary>
        public string LastEntryText
        {
            get
            {
                if (_log.Count == 0) return "（本回合还没有记录到任何目标）";
                LogEntry e = _log[_log.Count - 1];
                string s = FactionName(e.Faction) + "：" + e.What;
                if (!string.IsNullOrEmpty(e.Detail)) s += "（" + e.Detail + "）";
                if (e.Influence > 0) s += "  +" + e.Influence + " 影响力";
                if (e.Seconds > 0) s += "  −" + e.Seconds + "s";
                return s;
            }
        }

        public static string FactionName(string f)
        {
            if (f == MTF) return "MTF";
            if (f == CHAOS) return "混沌";
            return "未指定";
        }

        /// <summary>给面板用的一行总览。</summary>
        public string Summary()
        {
            return "MTF 影响力 " + InfluenceOf(MTF) + "（令牌 " + TokensOf(MTF) + "）"
                 + "　混沌 影响力 " + InfluenceOf(CHAOS) + "（令牌 " + TokensOf(CHAOS) + "）"
                 + "　额外令牌还剩 " + BonusTokensLeft + "/" + MaxBonusTokens + " 个（两方共享）";
        }
    }
}
