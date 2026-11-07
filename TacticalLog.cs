using System;
using System.Collections.Generic;

namespace SCPSLAlert
{
    /// <summary>
    /// 战术面板的数据与算法（一局一份）。
    ///
    /// 【影响力 / 令牌】—— 按 机制\刷新.txt 实现，见 TicketModel。
    ///     · 影响力按**目标**累加（击杀 SCP +15、发电机 +3、逃离 +5 等）；
    ///     · 影响力到 40/80/150/200 各发一个重生令牌；
    ///     · 每回合最多 3 个额外令牌，**两方共享**；每方开局自带 1 个。
    ///     旧版本用的是凭经验编的"血量 ÷ 200 + 击杀 × 15"，与官方机制不符，
    ///     已整个换掉。
    ///
    /// 【阵营刷新】—— 也按文档改过：
    ///     基准 300 秒；**每次重生后重置**，且重生方每个玩家 +10 秒；
    ///     目标会减少计时（击杀 SCP −10s、发电机 −10s、科学家逃离 −30s…）；
    ///     没有令牌时倒到 30 秒就停住，直到拿到令牌。
    ///
    /// 【大波 / 小波】—— 文档 1.2.4：
    ///     主波次后 2 分钟内拿到 10 影响力 → 可以获得小波，且每个主波次只能拿一个；
    ///     若主波后没能按时拿到小波，**敌对阵营**会得到一次小波。
    /// </summary>
    internal class TacticalLog
    {
        // ---- 可调参数 ----
        public int WaveInterval = 300;        // 两次重生波之间的基准间隔（秒，文档默认 5 分钟）
        public int PerPlayerSeconds = 10;     // 重生方每重生一个玩家，计时器额外 +10 秒
        public int NoTokenLockSeconds = 30;   // 没有令牌时倒计时停在这里（文档 1.2.1）
        public int BigWaveWindow = 120;       // 主波次后判定小波的时间窗（秒，文档 2 分钟）
        public int SmallWaveThreshold = 10;   // 窗口内拿够多少**影响力**就能给自己小波
        public int MiniWaveDelay = 150;       // 小波最早能在主波后多久出现（秒，文档 2 分 30 秒；config 里可调）
        public int MiniWaveMinDelay = 120;    // 被"死亡提前"压到的最低下限（文档：不能低于 2 分钟）

        // ---- 旧字段保留，只为兼容 config.ini 里的老键；已不参与计算 ----
        public int KillSeconds = 10;
        public int KillTickets = 15;
        public int DamagePerTicket = 200;

        /// <summary>影响力 / 令牌模型（文档实现）。</summary>
        public readonly TicketModel Tickets = new TicketModel();

        public const string MTF = "MTF";
        public const string CHAOS = "Chaos";
        public const string NONE = "";

        /// <summary>一个 SCP 本回合的记录。</summary>
        internal class ScpRecord
        {
            public string Code = "";
            public int TotalHp;        // 总血量（用户可改，0 = 用默认）
            public int CurrentHp = -1; // 当前血量（-1 = 没填，按满血算）
            public string Faction = NONE;
            public bool Killed;        // 是否被标记为已击杀
            public DateTime ChangedAt = DateTime.MinValue;

            /// <summary>本回合场上有这个 SCP（用户在面板上标）。</summary>
            public bool Present;

            /// <summary>
            /// 已经按"打掉 10% 最大生命"发过几次影响力。
            ///
            /// 文档 1.2.3 写的是"攻击一个可玩 SCP 超过最大生命值的 10%
            /// （当 SCP 死亡时不适用）"—— 字面上有两种读法：
            ///   · 每次伤害事件都看这一下够不够 10%（那几乎不可能触发）；
            ///   · 累计打掉的伤害每满 10% 算一次。
            /// 采用后者（用户确认）。用这个计数保证**只增不减**：
            /// 血量被误读回涨时不会重复发。
            /// </summary>
            public int PaidDamageMarks;

            /// <summary>
            /// 本回合累计**掉过**多少血（只增不减）。
            ///
            /// 【为什么不能直接用 "总血 − 当前血"】部分服务器有 SCP 回血机制
            /// （操作者确认）：血量回上去以后再打，那部分伤害**照样算影响力**。
            /// 如果按"总血 − 当前血"算，回血会把已经打掉的伤害抹掉，
            /// 再打一遍就发不出影响力了（这是实测踩到的坑）。
            ///
            /// 所以这里单独维护一个**只增不减**的累计量：
            ///   · 掉血时把它加上这一跳的差值；
            ///   · 回血时**不动它**（回血不该让累计量倒退，
            ///     但也不该提前把"以后要打的血"算进去 —— 那就等于
            ///     把回血当成"已经打过了"，下次真打就不发影响力了）。
            /// 血量统一走 <see cref="SetCurrentHp"/> 更新，保证这个量一直是对的。
            /// </summary>
            public int DamageCredit;

            /// <summary>
            /// 更新当前血量，并维护累计掉血量。
            /// **所有改血量的地方都要走这里**（OCR / 面板 / 清空）。
            /// </summary>
            public void SetCurrentHp(int hp)
            {
                if (hp < 0) { CurrentHp = hp; return; }

                // 第一次读到（或从"没填"变成有值）：当前这一跳就算掉血
                if (CurrentHp < 0)
                {
                    int first = TotalHp > 0 && hp < TotalHp ? TotalHp - hp : 0;
                    if (first > 0) DamageCredit += first;
                }
                else if (hp < CurrentHp)
                {
                    DamageCredit += CurrentHp - hp;      // 掉血：累加
                }
                // hp > CurrentHp = 回血：累计量不动（不倒退，也不提前算）

                CurrentHp = hp;
            }

            public bool HasDamage
            {
                get { return CurrentHp >= 0 && TotalHp > 0 && CurrentHp < TotalHp; }
            }

            /// <summary>
            /// 这一回合累计被打掉多少血（含回血之后重新打出来的部分）。
            /// 掉血判定用这个，不要用 <see cref="Damage"/>。
            /// </summary>
            public int AccumulatedDamage
            {
                get
                {
                    // 已阵亡：至少要把"死前剩的血"算进去（死亡那一跳本身算击杀目标）
                    if (Killed)
                    {
                        int atDeath = TotalHp - (CurrentHp >= 0 ? CurrentHp : 0);
                        return atDeath > DamageCredit ? atDeath : DamageCredit;
                    }
                    return DamageCredit;
                }
            }

            public int Damage
            {
                get
                {
                    if (TotalHp <= 0) return 0;

                    // 已阵亡：剩余血量【全部】算损失 —— 死亡票要连血量票一起给。
                    //   2500 总血、没填当前血量、已阵亡 → 损失 2500
                    //   2500 总血、当前 500、已阵亡      → 损失 2000
                    if (Killed)
                    {
                        int cur = CurrentHp >= 0 ? CurrentHp : 0;
                        return TotalHp > cur ? TotalHp - cur : 0;
                    }

                    if (!HasDamage) return 0;
                    return TotalHp - CurrentHp;
                }
            }

            /// <summary>这一条是否有内容（用于“清除改动”）。</summary>
            public bool IsTouched
            {
                get { return CurrentHp >= 0 || Killed || Faction != NONE; }
            }

            public void Clear()
            {
                CurrentHp = -1;
                Faction = NONE;
                Killed = false;
                Present = false;
                ChangedAt = DateTime.MinValue;
                PaidDamageMarks = 0;
            }
        }

        private readonly List<ScpRecord> _records = new List<ScpRecord>();

        /// <summary>常见 SCP 与默认总血量（服务器设置不同，面板里可以改）。</summary>
        public static readonly string[] ScpCodes = new string[]
        { "049", "096", "106", "173", "939", "079", "3114" };

        public static readonly int[] ScpDefaultHp = new int[]
        { 2500, 3000, 3000, 3000, 2500, 700, 1200 };

        public TacticalLog()
        {
            for (int i = 0; i < ScpCodes.Length; i++)
            {
                ScpRecord r = new ScpRecord();
                r.Code = ScpCodes[i];
                r.TotalHp = ScpDefaultHp[i];
                _records.Add(r);
            }
        }

        public List<ScpRecord> Records
        {
            get { return _records; }
        }

        public ScpRecord Find(string code)
        {
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Code == code) return _records[i];
            return null;
        }

        /// <summary>
        /// 换新一局：全部清空。
        /// 影响力 / 令牌也是"一局一份"，所以模型一起重置
        /// （文档：每回合各团队默认 1 个令牌、最多再拿 3 个）。
        /// </summary>
        public void ResetRound()
        {
            for (int i = 0; i < _records.Count; i++) _records[i].Clear();
            HasBigWave = false;
            BigWaveAt = DateTime.MinValue;
            BigWaveFaction = NONE;
            WaveHistory.Clear();   // 新回合，刷新顺序重新记
            PendingKills.Clear();  // 新回合，待归属的阵亡也清掉
            LastWavePlayers = 0;
            _miniWaveDecided = false;
            WaveAnchor = DateTime.MinValue;
            WaveCalibrated = false;
            LastBigWaveAt = DateTime.MinValue;
            _smallWaveFaction = null;
            _smallWaveAt = DateTime.MinValue;
            RoundStartedAt = DateTime.MinValue;
            Tickets.Reset();       // 影响力 / 令牌归零
            // 发电机状态也属于“本回合”，换局要一起清
            ResetGenerators();
        }

        #region 结算

        public int TotalDamage
        {
            get
            {
                int s = 0;
                for (int i = 0; i < _records.Count; i++) s += _records[i].Damage;
                return s;
            }
        }

        public int TotalKills
        {
            get
            {
                int s = 0;
                for (int i = 0; i < _records.Count; i++) if (_records[i].Killed) s++;
                return s;
            }
        }

        /// <summary>
        /// 总影响力（两方合计）。
        /// 旧版本这里是"伤害 ÷ 200 + 击杀 × 15"，与官方机制不符，已换成文档实现。
        /// </summary>
        public int TotalTickets
        {
            get { return Tickets.InfluenceOf(MTF) + Tickets.InfluenceOf(CHAOS); }
        }

        /// <summary>这一方累计拿到的**影响力**（文档里决定令牌的就是它）。</summary>
        public int TicketsOf(string faction)
        {
            return Tickets.InfluenceOf(faction);
        }

        /// <summary>
        /// 按当前的伤害情况补发"打掉 SCP 10% 最大生命"的影响力。
        ///
        /// 文档 1.2.3：打掉一个可玩 SCP 超过最大生命 10% → −1s 计时 +2 影响力，
        /// 但**该 SCP 死亡时这一条不适用**（它改为拿"击杀 SCP"的 −10s +15）。
        ///
        /// 所以这里按"累计"来数：每满 10% 算一次，而死亡那一跳里跨过的
        /// 最后一段不单独发（那一段算击杀）。用 <see cref="ScpRecord.PaidDamageMarks"/>
        /// 记住已经发过几次，保证只增不减 —— 血量被误读回涨时不会重复发。
        ///
        /// 返回这次补发了多少个 10%（0 = 没有新的）。
        /// </summary>
        public int AwardDamageMarks(string code, DateTime now)
        {
            ScpRecord r = Find(code);
            if (r == null) return 0;
            if (r.TotalHp <= 0) return 0;
            if (r.CurrentHp < 0) return 0;           // 血量没填，没法算

            int band = r.TotalHp / 10;
            if (band <= 0) return 0;

            // 累计打掉的伤害（含回血补回的部分，见 ScpRecord.AccumulatedDamage）。
            // 已阵亡时 Damage 会按"死之前"的伤害算，所以这里也自然是对的。
            int totalMark = r.AccumulatedDamage / band;

            // 死亡那一跳跨过的这一段不单独发（算击杀目标）
            int payable;
            if (r.Killed)
            {
                payable = r.AccumulatedDamage / band;
                if (payable > totalMark) payable = totalMark;
            }
            else payable = totalMark;

            if (payable <= r.PaidDamageMarks) return 0;

            int add = payable - r.PaidDamageMarks;

            // 按"当时场上有哪几方刷新了援军"分摊 —— 这是操作者定的规则：
            // 只有一方在场就全算它；大波之后另一方刷了小波就平均分。
            Dictionary<string, double> w = InfluenceWeights(now);

            // 没有波次信息时退回"这个 SCP 记的阵营"（面板上手动标的，也算一种依据）
            if (w.Count == 0)
            {
                if (r.Faction == NONE)
                {
                    // 什么依据都没有（既没刷过波、也没标阵营）→ 现在不敢归账。
                    //
                    // 【关键】这里**不能**把 PaidDamageMarks 往前推！
                    // 以前是先推计数再判断，结果"算不出来归属"的那几段被
                    // 永久标记成"已发过"，用户后面补上阵营也拿不到了 ——
                    // 等于悄悄吞掉影响力。现在改成只有真的发出去了才推计数。
                    return 0;
                }
                w[r.Faction] = 1.0;
            }

            int from = r.PaidDamageMarks;    // 这一段是从第几段开始发的
            for (int i = 0; i < add; i++)
            {
                string note;
                Tickets.RecordSplit(TicketModel.Objective.DamageScp10, w,
                    Has079, now, "SCP-" + code + " 累计 " + ((from + i + 1) * 10) + "%",
                    out note);
                if (note.Length > 0) _lastSplitNote = "SCP-" + code + " " + note;
            }
            r.PaidDamageMarks = payable;
            return add;
        }

        /// <summary>最近一次掉血分票的说明（面板/日志用）。</summary>
        private string _lastSplitNote = "";

        #region 下一步会不会有重生（预测）

        /// <summary>
        /// 某一方"下一步还有得重生"的概率（0..1）。
        ///
        /// 【为什么要这个】操作者说：玩家实际盯的是**重生次数**，
        /// 不是影响力。所以除了已经发生的事实，还要给一个"接下来还会不会有"的预测，
        /// 而且数字要写得大、看得清。
        ///
        /// 预测由几件事合成（按影响力大小判断，因为令牌是按影响力发的）：
        ///   ① 手里还有令牌 → 下一波一定有它，概率 1；
        ///   ② 影响力已经过了下一个阈值 → 令牌马上到手，概率 1；
        ///   ③ 影响力还差一点，但按现在攒的速度**能在下一波到来之前攒够** → 概率高；
        ///   ④ 否则随着"离下一波越近还没够"而迅速变小。
        ///
        /// 【怎么估"攒的速度"】用本回合至今的影响力 / 已进行时间，
        /// 也就是平均每秒攒多少影响力；再乘"离下一波还有多少秒"，
        /// 得到"这一波之前预计还能攒多少"。简单、可解释，不会乱跳。
        /// </summary>
        public double PredictRespawnChance(DateTime now, string faction)
        {
            if (faction != MTF && faction != CHAOS) return 0;

            if (Tickets.HasUnknownHistory(faction))
                return Tickets.UnknownHistoryRespawnChance(faction);

            // ① 手里还有令牌：下一波就是它的
            if (Tickets.TokensOf(faction) > 0) return 1.0;

            // 共享的额外令牌已经用完 → 只能靠继续攒影响力拿新令牌
            int need = Tickets.ToNextToken(faction);
            int have = Tickets.InfluenceOf(faction);

            // -1 = 已经没有下一个令牌可拿了（四个阈值全过 / 共享额度用完）
            if (need < 0) return 0.0;

            // ② 已经够了（只是还没到波次或还没结算）
            if (need == 0) return 1.0;

            // 下一波还有多久（它锁在 30 秒时说明"没令牌、在等令牌"）
            double left = WaveRemaining(now, faction);
            if (left <= 0) return 1.0;                   // 到点了，就差结算

            // ③ 按平均速度估"这一波之前还能攒多少"
            double elapsed = RoundElapsedNow();
            double perSec = (elapsed >= 5) ? (have / elapsed) : 0;

            // 攒得不够快 → 后面那条按"还差多少 / 还能攒多少"平滑衰减
            double expectGain = perSec * left;
            double ratio = (need > 0) ? (expectGain / need) : 1.0;

            double p;
            if (ratio >= 1.0) p = 0.9;                   // 够得着，但没到手，不写满
            else p = ratio * 0.85;                       // 越不够越小

            // 额外令牌还有剩（两方共享额度）→ 整体更乐观一点
            if (Tickets.BonusTokensLeft > 0) p += 0.05;

            if (p > 0.96) p = 0.96;
            if (p < 0.02) p = 0.02;
            return p;
        }

        /// <summary>
        /// "全人类（MTF + 混沌合起来）下一步还有得重生"的概率。
        ///
        /// 【为什么单独一条】操作者要求：总览页底下再加一条**全人类**的有票概率，
        /// 宽度要等于 MTF + 混沌两栏加起来（因为它统计的是所有人）。
        /// 两边任意一方有得重生，观察者就还有盼头，所以按"至少一方有"来合：
        ///     P = 1 − (1−P_mtf) × (1−P_chaos)
        /// 这也是唯一在数学上说得通的合并方式（两者不是互斥事件）。
        /// </summary>
        public double PredictAnyRespawnChance(DateTime now)
        {
            double a = PredictRespawnChance(now, MTF);
            double b = PredictRespawnChance(now, CHAOS);
            double p = 1.0 - (1.0 - a) * (1.0 - b);
            if (p > 0.99) p = 0.99;
            return p;
        }

        /// <summary>某方当前至少还有一张可用大票的概率，不把未来影响力收入算作当前存量。</summary>
        public double CurrentTokenChance(string faction)
        {
            if (faction != MTF && faction != CHAOS) return 0;
            if (Tickets.HasUnknownHistory(faction))
                return Tickets.UnknownHistoryRespawnChance(faction);
            return Tickets.TokensOf(faction) > 0 ? 1.0 : 0.0;
        }

        /// <summary>人类当前至少还有一张可用大票的概率。</summary>
        public double AnyCurrentTokenChance()
        {
            double a = CurrentTokenChance(MTF);
            double b = CurrentTokenChance(CHAOS);
            return 1.0 - (1.0 - a) * (1.0 - b);
        }

        /// <summary>把概率写成"87%"这种给面板用。</summary>
        public static string PercentText(double p)
        {
            if (p < 0) p = 0;
            if (p > 1) p = 1;
            return ((int)Math.Round(p * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "%";
        }

        public static string PercentTextOneDecimal(double p)
        {
            if (p < 0) p = 0;
            if (p > 1) p = 1;
            return p.ToString("0.0%", System.Globalization.CultureInfo.InvariantCulture);
        }

        #endregion

        public string LastSplitNote
        {
            get { return _lastSplitNote; }
        }

        /// <summary>
        /// 面板（或别处）改了总血量之后调一次。
        ///
        /// 总血量是分母，改了之后"已经发过几段"的含义会变：
        /// 比如上限从 3000 改成 4500，每段就从 300 变成 450，
        /// 之前发的 4 段其实只相当于 2.7 段。这里把已发段数按新的分母**重算**，
        /// 但**不会倒退**（免得以后同一段又被发一次）。
        /// </summary>
        public void OnTotalHpChanged(ScpRecord r)
        {
            if (r == null || r.TotalHp <= 0) return;

            int band = r.TotalHp / 10;
            if (band <= 0) return;

            int payable = r.AccumulatedDamage / band;
            if (payable > r.PaidDamageMarks) r.PaidDamageMarks = payable;
        }

        /// <summary>这一方还能重生几个令牌。</summary>
        public int TokensOf(string faction)
        {
            return Tickets.TokensOf(faction);
        }

        /// <summary>两方都没有令牌了（文档 1.2.5：这时候会启动 O5 核弹）。</summary>
        public bool BothOutOfTokens
        {
            get { return Tickets.BothOutOfTokens; }
        }

        public int DamageOf(string faction)
        {
            int s = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Faction == faction) s += _records[i].Damage;
            return s;
        }

        public int KillsOf(string faction)
        {
            int s = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Faction == faction && _records[i].Killed) s++;
            return s;
        }

        /// <summary>有多少条记录指定了阵营（没指定就没法判断小波归谁）。</summary>
        public bool HasFactionData
        {
            get
            {
                for (int i = 0; i < _records.Count; i++)
                    if (_records[i].IsTouched && _records[i].Faction != NONE) return true;
                return false;
            }
        }

        /// <summary>距离下一个令牌还差多少影响力（-1 = 本回合拿不到了）。</summary>
        public int FloatToNextTicket
        {
            get { return Tickets.ToNextToken(MTF); }
        }

        #endregion

        #region 阵营刷新

        /// <summary>
        /// 本波重生方**每个玩家**给计时器加的秒数（文档：重生后计时重置，
        /// 重生方每重生一个玩家额外 +10 秒）。
        /// 由面板填写"这波刷了多少人"，或者外部推算。
        /// </summary>
        public int LastWavePlayers;

        /// <summary>
        /// 大波的典型人数（操作者实测约 16-17 人）。OCR 数不出人数，
        /// 所以没读到具体值时按它估 —— 它直接决定"下一次重生 +N×10 秒"。
        /// </summary>
        public int BigWavePlayers = 16;

        /// <summary>小波的典型人数（操作者实测约 6 人）。</summary>
        public int SmallWavePlayers = 6;

        /// <summary>
        /// 小波从**播报**到真正进入设施的延迟（秒，操作者实测约 60 秒）。
        /// 游戏的重生计时是在人真进场那刻重置的，所以小波的计时锚点要往后挪这么多。
        /// </summary>
        public double SmallWaveEntryDelaySeconds = 60;

        /// <summary>
        /// 换算后的实际刷新间隔：基准 + 上一波重生人数 × 10。
        ///
        /// 文档里还有一堆"目标减计时"（击杀 SCP −10s、发电机 −10s、科学家逃离 −30s…），
        /// 那些是**事件性**的，不能像旧代码那样用"总击杀数 × 10"一刀切 ——
        /// 它们按发生时间即时生效，由 <see cref="TargetSecondsCut"/> 单独扣。
        /// </summary>
        public int EffectiveInterval
        {
            get
            {
                int v = WaveInterval + LastWavePlayers * PerPlayerSeconds;
                if (v < 60) v = 60;
                return v;
            }
        }

        /// <summary>
        /// 本波开始后、因完成目标而减少的计时秒数（击杀 SCP / 发电机 / 逃离…）。
        /// 取某一方在 [WaveAnchor, now] 这段时间里减掉的秒数。
        /// </summary>
        public int TargetSecondsCut(string faction, DateTime now)
        {
            if (WaveAnchor == DateTime.MinValue) return 0;
            double span = (now - WaveAnchor).TotalSeconds;
            if (span <= 0) return 0;
            return Tickets.SecondsCutInWindow(faction, WaveAnchor, span);
        }

        /// <summary>
        /// 上一波的时间点。
        /// 构造时就设成“现在”，所以哪怕用户什么都不填、也不校准，
        /// 刷新计时也会按固定节奏自己往前走。
        /// </summary>
        public DateTime WaveAnchor = DateTime.Now;

        /// <summary>是否明确校准过（按过 F11，或检测到回合开始）。</summary>
        public bool WaveCalibrated;

        /// <summary>没校准过也照样在走，所以这里只用来决定显示上要不要标“未校准”。</summary>
        public bool HasWaveAnchor
        {
            get { return WaveCalibrated; }
        }

        /// <summary>开局前不显示刷新倒计时；中途加入时首条已确认刷新才启动估算。</summary>
        public bool WaveClockRunning
        {
            get { return WaveCalibrated; }
        }

        /// <summary>
        /// 下一波还剩多少秒。
        /// 整局连续滚动：过了一波就自动进下一波 ——
        /// 哪怕用户什么都没填、也没按过校准，它也会按固定节奏自己往前走。
        /// </summary>
        /// <summary>
        /// 预计刷新时间的修正量（秒）。实测比推算值**晚**约 15 秒，
        /// 所以给倒计时加上这么多；可以在 config.ini 里改。
        /// </summary>
        public int WaveOffsetSeconds = 15;

        /// <summary>
        /// 下一波还剩多少秒 —— 按文档的规则算：
        ///
        ///   剩余 = 基准间隔（+ 上一波重生人数 × 10）
        ///          − 本波开始后因完成目标减掉的秒数
        ///          − 已经过去的时间
        ///
        /// 【为什么不再用"取余滚动"】旧代码假设"刷新永远按固定间隔循环"，
        /// 但文档里写得很清楚：**一旦有一方重生，两方的计时器都重置为默认值**，
        /// 而且目标会随时把计时往前拨（击杀 SCP −10s、发电机 −10s…）。
        /// 所以这是一个"从上一波开始算的截止时刻"，不是固定节奏。
        /// 锚点就是上一波的时间（MarkWave 会把它设成"刚刚"）。
        ///
        /// 没有令牌时（文档 1.2.1）：计时器变灰、倒到 30 秒就停住，
        /// 直到拿到令牌才继续 —— 这里就返回"停在 30 秒"。
        /// </summary>
        public double WaveRemaining(DateTime now)
        {
            return WaveRemaining(now, MTF);
        }

        /// <summary>
        /// 当前"场上有哪几方、各占多少权重"——用来给掉血分票。
        ///
        /// 【背景（操作者说明）】影响力 / 重生令牌 / SCP 血量这些**只有死了才看得到**；
        /// 活着的时候屏幕上没有精确数据，只能**推算**。死了就按读到的数直接记录
        /// （准确度 100%，见 TicketModel.CalibrateInfluence）。所以这里只负责"活着的时候"。
        ///
        /// 规则（按操作者给的思路实现，幅度是我定的）：
        ///   ① 只有一方有援军 → 全算它；
        ///   ② 两方都在场 → 从对半开始，按三条依据偏置（各 ±0.1~0.2，最后夹到 0.2~0.8）：
        ///        B. **小波归属**（操作者的建议）：小波给了大波那一方 → 说明它在窗口内
        ///           打够了 10 点影响力 → 它正在推进 → 偏向它；小波给了对方 → 偏向对方。
        ///        D. **距上次刷新**：哪一方刚刷完援军（<2 分钟），那批人正在打 → 偏向它；
        ///           超过 5 分钟没刷新 → 偏向另一边。
        ///        C. **影响力涨速**：最近 60 秒谁的影响力涨得多（用的是校准后的权威值）
        ///           → 偏 0.1；两边都没数据就不参与。
        ///   ③ 还没有任何刷新记录 → 空表（不给任何一方，宁可漏也不要错记）。
        /// </summary>
        public Dictionary<string, double> InfluenceWeights(DateTime now)
        {
            Dictionary<string, double> w = new Dictionary<string, double>();
            if (!HasBigWave || BigWaveFaction == NONE) return w;

            // ① 大波之后，对方刷过小波吗？（只认被采纳过的小波，见 SmallWaveAccepted）
            bool twoSides = _smallWaveFaction != null && _smallWaveFaction != NONE
                && _smallWaveFaction != BigWaveFaction
                && _smallWaveAt != DateTime.MinValue
                && now >= _smallWaveAt;

            if (!twoSides)
            {
                w[BigWaveFaction] = 1.0;      // 只有一方在场 → 全算它
                return w;
            }

            // ② 两方都在场：从对半开始偏
            string big = BigWaveFaction;
            string other = Opposite(big);
            double shareBig = 0.5;

            // B. 小波归属（±0.20）
            if (_smallWaveFaction == BigWaveFaction) shareBig += 0.20;   // 打够票续自己 → 它在打
            else shareBig -= 0.20;                                        // 没打够 → 对方在打

            // D. 距上次刷新（每一方各自 ±0.10）
            shareBig += SpawnRecencyBonus(big, now) * 0.10;
            shareBig -= SpawnRecencyBonus(other, now) * 0.10;

            // C. 影响力涨速（60 秒窗口，±0.10；两边都没数据就不动）
            double gBig = Tickets.InfluenceInWindow(big, now, 60);
            double gOther = Tickets.InfluenceInWindow(other, now, 60);
            if (gBig != gOther)
            {
                if (gBig > gOther) shareBig += 0.10;
                else shareBig -= 0.10;
            }

            // 夹取：不允许一边独占（留出"分错了也好纠正"的余地）
            if (shareBig < 0.20) shareBig = 0.20;
            if (shareBig > 0.80) shareBig = 0.80;

            w[big] = shareBig;
            w[other] = 1.0 - shareBig;
            return w;
        }

        /// <summary>
        /// "这一方刚刷过援军吗"的加成：刚刷过（&lt;2 分钟）= +1，很久没刷（&gt;5 分钟）= −1，
        /// 中间或没有记录 = 0。给 InfluenceWeights 用。
        /// </summary>
        private int SpawnRecencyBonus(string faction, DateTime now)
        {
            DateTime last = DateTime.MinValue;
            for (int i = WaveHistory.Count - 1; i >= 0; i--)
            {
                if (WaveHistory[i].Faction != faction) continue;
                last = WaveHistory[i].At;
                break;
            }
            if (last == DateTime.MinValue) return 0;      // 这一方本局还没刷过 → 不偏

            double since = (now - last).TotalSeconds;
            if (since < 120) return 1;
            if (since > 300) return -1;
            return 0;
        }

        /// <summary>被采纳的那次小波发生在什么时候（MinValue = 没有）。</summary>
        private DateTime _smallWaveAt = DateTime.MinValue;

        /// <summary>
        /// 一次"小波播报"能不能被采纳。
        ///
        /// 【为什么需要这条】OCR 有时会把同一段刷新读成大波，紧接着又读成小波
        /// （重复识别 / 误判）。操作者给的经验是：
        ///   **刚认定大波之后的一段时间内，报小波极可能是误报，一律不算。**
        /// 所以这里做一道冷却：距上次认定大波不足 <see cref="SmallWaveGuardSeconds"/>
        /// 秒时，小波播报只重置计时、不改判。
        /// </summary>
        public int SmallWaveGuardSeconds = 60;

        /// <summary>上一次认定大波的时刻（判定小波冷却用）。</summary>
        public DateTime LastBigWaveAt = DateTime.MinValue;

        /// <summary>这次小波播报该不该采纳（冷却期内不采纳）。</summary>
        public bool SmallWaveAccepted(DateTime now)
        {
            if (LastBigWaveAt == DateTime.MinValue) return true;   // 还没大波，小波就是第一波
            double since = (now - LastBigWaveAt).TotalSeconds;
            if (since < 0) return false;
            return since >= SmallWaveGuardSeconds;
        }

        /// <summary>某一方的下一波还剩多少秒（两方令牌情况可能不同）。</summary>
        public double WaveRemaining(DateTime now, string faction)
        {
            int interval = EffectiveInterval;
            if (interval <= 0) return 0;
            if (WaveAnchor == DateTime.MinValue) return interval;

            double elapsed = (now - WaveAnchor).TotalSeconds;
            if (elapsed <= 0) return interval;

            // 目标把计时往前拨了多少
            int cut = TargetSecondsCut(faction, now);

            double remain = interval - cut - elapsed + WaveOffsetSeconds;

            // 文档 1.2.1：没有令牌时计时器"变灰，并倒数直到达到 30 秒然后停止"。
            // 注意是**倒到 30 秒才停**，不是立刻显示 30 —— 所以：
            //   · 还有令牌 → 正常倒，最低到 0；
            //   · 没令牌   → 如果按正常算会掉到 30 秒以下，就停在 30 秒。
            if (Tickets.TokensOf(faction) <= 0 && remain < NoTokenLockSeconds)
                return NoTokenLockSeconds;

            if (remain < 0) remain = 0;
            return remain;
        }

        /// <summary>
        /// 这一方现在是不是"没有令牌、计时锁在 30 秒"的状态（面板要用灰字显示）。
        /// </summary>
        public bool WaveLockedNoToken(string faction)
        {
            return Tickets.TokensOf(faction) <= 0;
        }

        /// <summary>从锚点算起已经刷过多少波了（按实际记录数算，比旧算法可靠）。</summary>
        public int WavesElapsed(DateTime now)
        {
            int n = 0;
            for (int i = 0; i < WaveHistory.Count; i++) if (WaveHistory[i].At <= now) n++;
            return n;
        }

        /// <summary>校准“刚刚刷了一波大波”，计时从现在重新起算。</summary>
        public void MarkBigWave(DateTime now, string faction)
        {
            MarkBigWave(now, faction, "manual");
        }

        /// <summary>
        /// 同上，但记下来源（"ocr" = 识别到播报，其它 = 手动 / 快捷键）。
        /// 需求八附：面板要列出"本回合的刷新顺序"，所以每一次都要留痕。
        /// </summary>
        public void MarkBigWave(DateTime now, string faction, string source)
        {
            MarkWave(now, faction, false, source);
        }

        /// <summary>
        /// 校准"刚刚刷了一波"（带大小波区分）。
        ///
        /// isSmall = true 表示这是**小波**（播报里说"增援部队"）：
        ///   · 计时锚点照样重置（下一次刷新从这一刻算起）；
        ///   · 但**不覆盖**"本回合大波"的信息 —— 那份信息是判定小波阵营的依据，
        ///     被小波冲掉之后就判不出来了。
        /// 历史记录里会如实写成"小波"。
        ///
        /// 文档：**任何一方重生，两方计时器都重置为默认值**
        /// —— 所以这里只管把锚点设成"刚刚"，两方共用这个锚点。
        /// 同时按"重生方"扣掉一个令牌，并把"这波刷了多少人"记下来
        /// （每多一个玩家，下一次计时 +10 秒）。
        /// </summary>
        public bool MarkWave(DateTime now, string faction, bool isSmall, string source)
        {
            return MarkWave(now, faction, isSmall, source, 0);
        }

        /// <summary>同上，并指定这一波重生方刷了多少人。返回 true = 这一波被采纳了。</summary>
        public bool MarkWave(DateTime now, string faction, bool isSmall, string source, int players)
        {
            string f = (faction == CHAOS) ? CHAOS : MTF;

            // 小波播报的防误报冷却：刚认定过大波就不采信"紧接着的小波"
            //（OCR 常把同一段刷新重复读一次，读成大波又读成小波）。
            //
            // 【为什么现在连计时也不重置了】以前这里"照样重置计时"，理由是
            // "万一真是小波，按游戏规则也该重置"。但实测（日志）是：
            // 大波 22:38:08 → 22:38:13 和 22:38:22 又各读到一次"增援部队"，
            // 两次都被判误报、却把计时锚点又推了两次，于是后面所有刷新推算
            // 整体偏晚 —— 操作者看到的就是"说了有小波，后面的波次估计都不对"。
            // 既然是误报，就整条忽略：不采纳、不重置、不提示、不记账。
            if (isSmall && !SmallWaveAccepted(now))
            {
                Logger.On("战术", "小波播报距上次认定大波不足 "
                    + SmallWaveGuardSeconds + " 秒 → 判定为误报，整条忽略（不采纳、不重置计时）");
                return false;
            }

            // ---- 锚点与人数（操作者给的两条实测）----
            //  ① 小波从**播报到真正进入设施**还有约 1 分钟 ——
            //     游戏的重生计时是在"人真进场"那一刻重置的，所以小波的锚点要往后挪，
            //     否则我们算出来的下一波会比游戏早 1 分钟。
            //  ② 人数只能估：大波约 16-17 人、小波约 6 人（OCR 数不出人数）。
            //     这个人数直接决定"下一次重生 +N×10 秒"，所以给个典型值比当 0 强得多。
            int usePlayers = players > 0
                ? players
                : (isSmall ? SmallWavePlayers : BigWavePlayers);
            double delay = isSmall ? SmallWaveEntryDelaySeconds : 0;

            WaveAnchor = now.AddSeconds(delay);
            WaveCalibrated = true;
            LastWavePlayers = usePlayers;
            if (players <= 0)
                Logger.On("战术", "这一波没读到人数 → 按典型值估 "
                    + (isSmall ? "小波" : "大波") + " " + usePlayers + " 人（下次重生 +"
                    + (usePlayers * PerPlayerSeconds) + " 秒）"
                    + (delay > 0 ? "；小波进场还要约 " + (int)delay + " 秒，锚点已后移" : ""));

            // 文档 1.2.1：**主**重生波发生时，重生的那个团队消耗一个令牌。
            // 【操作者纠正】小波（迷你波）**不消耗**重生令牌 —— 它是不花令牌的额外一波。
            // 以前不分大小波都扣一个，于是"小波刷完面板上的令牌比游戏少 1"，
            // 还会把亡者协议（两方令牌用尽）提前触发。
            if (!isSmall) Tickets.RecordWaveSpend(f, now);

            if (!isSmall)
            {
                HasBigWave = true;
                BigWaveAt = now;
                BigWaveFaction = f;
                _smallWaveFaction = null;   // 重新判定
                _smallWaveAt = DateTime.MinValue;
                _miniWaveDecided = false;
            }
            else
            {
                _smallWaveFaction = f;      // 被采纳的小波：记下来，供分票用
                _smallWaveAt = now;
            }

            // 【操作者要求】不管大波小波，采纳之后都进入静默期：
            // 接下来 SmallWaveGuardSeconds 秒内再读到"刷新播报"一律当重复读的同一段。
            // （以前只有大波会重置这个起点，于是小波之后紧接着的重复读数会被当成新的一波。）
            LastBigWaveAt = now;

            WaveRecord rec = new WaveRecord();
            rec.At = now;
            rec.Faction = f;
            rec.IsSmall = isSmall;
            rec.Source = source == null ? "" : source;
            rec.Players = players < 0 ? 0 : players;
            WaveHistory.Add(rec);
            if (WaveHistory.Count > 12) WaveHistory.RemoveAt(0);
            return true;
        }

        /// <summary>切换最近一条波次大小，并同步未来刷新锚点与令牌计算。</summary>
        public bool ReclassifyLatestWave()
        {
            return WaveHistory.Count > 0 && ToggleWaveType(WaveHistory.Count - 1);
        }

        /// <summary>切换指定波次大小，并从完整历史重算波次状态。</summary>
        public bool ToggleWaveType(int index)
        {
            if (index < 0 || index >= WaveHistory.Count) return false;
            WaveRecord rec = WaveHistory[index];
            if (rec.IsSmall) Tickets.RecordWaveSpend(rec.Faction, rec.At);
            else Tickets.RefundTokenAt(rec.Faction, rec.At);
            rec.IsSmall = !rec.IsSmall;
            RebuildWaveState();
            return true;
        }

        /// <summary>切换指定波次阵营；若为大波，令牌消耗也转移到新阵营。</summary>
        public bool ToggleWaveFaction(int index)
        {
            if (index < 0 || index >= WaveHistory.Count) return false;
            WaveRecord rec = WaveHistory[index];
            string next = rec.Faction == CHAOS ? MTF : CHAOS;
            if (!rec.IsSmall)
            {
                Tickets.RefundTokenAt(rec.Faction, rec.At);
                Tickets.RecordWaveSpend(next, rec.At);
            }
            rec.Faction = next;
            RebuildWaveState();
            return true;
        }

        /// <summary>删除指定波次，并同步撤销大波令牌消耗和波次锚点。</summary>
        public bool RemoveWave(int index)
        {
            if (index < 0 || index >= WaveHistory.Count) return false;
            WaveRecord rec = WaveHistory[index];
            if (!rec.IsSmall) Tickets.RefundTokenAt(rec.Faction, rec.At);
            WaveHistory.RemoveAt(index);
            RebuildWaveState();
            return true;
        }

        private void RebuildWaveState()
        {
            HasBigWave = false;
            BigWaveAt = DateTime.MinValue;
            BigWaveFaction = NONE;
            _smallWaveFaction = null;
            _smallWaveAt = DateTime.MinValue;
            _miniWaveDecided = false;
            LastBigWaveAt = DateTime.MinValue;

            if (WaveHistory.Count == 0)
            {
                WaveCalibrated = false;
                LastWavePlayers = 0;
                WaveAnchor = DateTime.MinValue;
                return;
            }

            for (int i = 0; i < WaveHistory.Count; i++)
            {
                WaveRecord rec = WaveHistory[i];
                LastBigWaveAt = rec.At;
                if (rec.IsSmall) { _smallWaveFaction = rec.Faction; _smallWaveAt = rec.At; }
                else
                {
                    HasBigWave = true;
                    BigWaveAt = rec.At;
                    BigWaveFaction = rec.Faction;
                    _smallWaveFaction = null;
                    _smallWaveAt = DateTime.MinValue;
                }
            }

            WaveRecord latest = WaveHistory[WaveHistory.Count - 1];
            WaveAnchor = latest.At.AddSeconds(latest.IsSmall ? SmallWaveEntryDelaySeconds : 0);
            WaveCalibrated = true;
            LastWavePlayers = latest.Players > 0 ? latest.Players
                : (latest.IsSmall ? SmallWavePlayers : BigWavePlayers);
        }

        /// <summary>
        /// 按**时间**推测这一波是大波还是小波。
        ///
        /// 【为什么需要】混沌的大小波播报是**一模一样**的，认关键词分不出来。
        /// 但游戏里的约定是固定的：**大波之后一定有小波**（主波那方 2 分钟内打够
        /// 10 影响力就给自己，没打够就给敌方）—— 所以"大波之后的下一波"就是小波。
        ///
        /// 【操作者定稿的口径】
        ///   · 大波刚刷完的 60 秒：调用方当成"同一段被重复读到"，整条忽略；
        ///   · 静默期之后**识别到的第一波**就是小波 —— 不再卡"主波后 2 分钟窗口"
        ///     （实测小波常常 2~4 分钟才落下来，过了那个窗口就会被当成大波，
        ///      计时锚点跟着错，操作者看到的就是"小波刷了但提示还按大波算"）；
        ///   · 小波记下来之后，下一波自然回到大波（MiniWaveNotYetRecorded 变 false）。
        ///
        /// 不限阵营：小波可能是大波那一方，也可能是对方；阵营由 DecideSmallWave 另算。
        /// </summary>
        public bool GuessIsSmallWave(DateTime now)
        {
            return NextWaveIsSmall(now, 0);
        }

        /// <summary>本回合刷过的大波记录（需求八附）。</summary>
        public class WaveRecord
        {
            public DateTime At;
            public string Faction = NONE;
            public bool IsSmall;
            public string Source = "";      // "ocr" / "manual"
            public int Players;             // 这一波重生方刷了多少人（文档：每人 +10s）
        }

        // ============================================================
        // 待归属的阵亡（操作者要求）
        //
        // 认得出"某个 SCP 死了"、但认不出是哪一方杀的时（播报里没有击杀者、
        // 或者击杀者既不是 MTF/混沌也说不出阵营），不猜、也不漏 ——
        // 挂一条待归属，在面板「总览」页上让用户点一下定给哪一方。
        // 完全没识别到死亡（连"谁死了"都不知道）时不会挂任何东西（那时候没得判断）。
        // ============================================================

        public class PendingKill
        {
            public string Code = "";
            public DateTime At = DateTime.MinValue;
        }

        /// <summary>等用户定夺归属的阵亡（新回合清空）。</summary>
        public readonly List<PendingKill> PendingKills = new List<PendingKill>();

        /// <summary>挂一条待归属（同一个 SCP 只挂一条）。</summary>
        public void AddPendingKill(string code, DateTime now)
        {
            if (string.IsNullOrEmpty(code)) return;
            for (int i = 0; i < PendingKills.Count; i++)
                if (PendingKills[i].Code == code) return;

            PendingKill p = new PendingKill();
            p.Code = code;
            p.At = now;
            PendingKills.Add(p);
            Logger.On("战术", "SCP-" + code + " 已阵亡但认不出击杀方 → 在面板「总览」页挂一条待归属");
        }

        /// <summary>
        /// 把待归属的那一条定给某一方，并按文档记一笔（+15 影响力 / −10s 计时）。
        /// 返回 false = 没有这一条待归属。
        ///
        /// 【记账用的是"阵亡那一刻"的时间】不是点按钮的时间 ——
        /// 操作者可能过一会儿才回来判断归属，用点击时间会把这一笔挪到错误的
        /// 时间点上（影响力时间线、刷新窗口、小波判定都是按时间算的）。
        /// 参数 now 只用于日志。
        /// </summary>
        public bool ResolvePendingKill(string code, string faction, DateTime now, bool has079)
        {
            for (int i = 0; i < PendingKills.Count; i++)
            {
                if (PendingKills[i].Code != code) continue;
                DateTime at = PendingKills[i].At;
                if (at == DateTime.MinValue) at = now;
                PendingKills.RemoveAt(i);

                if (faction != MTF && faction != CHAOS) return true;   // 只是撤掉，不记账
                Tickets.RecordAt(TicketModel.Objective.KillScp, faction, has079, at,
                    "SCP-" + code + "（手动归属）");
                ScpRecord rec = Find(code);
                if (rec != null && rec.Faction == NONE) rec.Faction = faction;
                Logger.On("战术", "SCP-" + code + " 的阵亡已归属给 " + FactionName(faction)
                    + " → +" + TicketModel.Info(TicketModel.Objective.KillScp).Influence
                    + " 影响力 / 计时 −10 秒（按阵亡时刻 " + at.ToString("HH:mm:ss")
                    + " 记账，你是在 " + now.ToString("HH:mm:ss") + " 点的）");
                return true;
            }
            return false;
        }

        /// <summary>本回合的刷新顺序。新回合会被清空。</summary>
        public readonly List<WaveRecord> WaveHistory = new List<WaveRecord>();

        /// <summary>
        /// 推测后续刷新（需求八附）。按文档 1.2.4 的规则给**推算**，不是保证：
        ///   · 主波后 2 分钟内主波方拿够 10 影响力 → 小波给主波方；
        ///   · 没拿够 → 小波给敌方；
        ///   · 每个主波次只有一个小波；再往后回到常规主波。
        /// </summary>
        public string PredictNextWaves(DateTime now)
        {
            if (!HasBigWave) return "还没看到任何刷新增援的播报 —— 识别到『某某已进入设施』就会开始推测";

            int interval = EffectiveInterval;
            double remain = WaveRemaining(now);
            double sinceBig = (now - BigWaveAt).TotalSeconds;

            string cur = FactionName(BigWaveFaction);
            string other = FactionName(Opposite(BigWaveFaction));

            // 这一波小波会给谁
            int earned = Tickets.InfluenceInWindow(BigWaveFaction, BigWaveAt, BigWaveWindow);
            string miniOwner = FactionName(earned >= SmallWaveThreshold
                ? BigWaveFaction : Opposite(BigWaveFaction));
            bool miniNotYet = MiniWaveNotYetRecorded;

            string s;
            if (miniNotYet && sinceBig <= MiniWaveDueAfterBigWave + MiniWaveLateGrace)
            {
                // 时间按"**真正进场**"算：播报延迟 + 进场延迟（操作者实测约 1 分钟）
                s = "推测下一次：" + miniOwner + " 小波"
                  + "（主波后 " + (earned >= SmallWaveThreshold ? "已够" : "不够")
                  + " " + SmallWaveThreshold + " 影响力，当前 " + earned + "）"
                  + "，约 " + TimeEstimator.Clock(MiniWaveDueAfterBigWave - sinceBig, true) + " 后"
                  + "；再下一次：" + cur + " 大波（约 " + TimeEstimator.Clock(remain, true) + " 后）";
            }
            else
            {
                s = "推测下一次：" + other + " 大波（约 " + TimeEstimator.Clock(remain, true) + " 后）"
                  + "；再下一次：" + cur + " 大波（约 "
                  + TimeEstimator.Clock(remain + interval, true) + " 后）";
            }

            // 没有令牌的一方，计时会锁在 30 秒
            if (WaveLockedNoToken(MTF) || WaveLockedNoToken(CHAOS))
                s += "\r\n（"
                   + (WaveLockedNoToken(MTF) ? "MTF" : "")
                   + (WaveLockedNoToken(MTF) && WaveLockedNoToken(CHAOS) ? "、" : "")
                   + (WaveLockedNoToken(CHAOS) ? "混沌" : "")
                   + " 没有令牌 → 计时锁在 " + NoTokenLockSeconds + " 秒，直到拿到令牌）";
            return s;
        }

        /// <summary>刷新顺序的一行摘要（给面板列表用）。</summary>
        public string WaveHistoryText(DateTime now)
        {
            if (WaveHistory.Count == 0) return "（本回合还没记录到刷新增援）";

            string s = "";
            for (int i = 0; i < WaveHistory.Count; i++)
            {
                WaveRecord r = WaveHistory[i];
                string when = r.At.ToString("HH:mm:ss");
                s += (i + 1) + ". " + FactionName(r.Faction) + (r.IsSmall ? " 小波  " : " 大波  ") + when
                   + (r.Source == "ocr" ? "（识别）" : "（手动）");
                if (i < WaveHistory.Count - 1) s += "\r\n";
            }
            return s;
        }

        public bool HasBigWave;
        public DateTime BigWaveAt = DateTime.MinValue;
        public string BigWaveFaction = NONE;

        private string _smallWaveFaction;

        /// <summary>本局刚开始时调用，让计时器有个起点。</summary>
        public void BeginRound(DateTime now)
        {
            ResetRound();
            WaveAnchor = now;
            WaveCalibrated = true;
            RoundStartedAt = now;
            _smallWaveFaction = null;
        }

        /// <summary>
        /// 中途加入时启用刷新估计，但保留已累计的本局战术数据。
        /// 【操作者要求】中途加入时**不**按"从 0 开始"推算波次：
        /// 第一波刷新被记录之前，刷新计时保持等待状态；等真的刷了一波，
        /// MarkWave 会把锚点设到那一波，之后才按软件逻辑走。
        /// </summary>
        public void BeginMidRoundEstimate(DateTime now)
        {
            RoundStartedAt = now;
            if (WaveHistory.Count == 0)
            {
                WaveAnchor = DateTime.MinValue;
                WaveCalibrated = false;
                LastWavePlayers = 0;
            }
        }

        /// <summary>本回合开始时刻（面板上显示"回合时间"用）。</summary>
        public DateTime RoundStartedAt = DateTime.MinValue;

        /// <summary>本回合已经进行了多少秒（没开始过就是 0）。</summary>
        public double RoundElapsedNow()
        {
            if (RoundStartedAt == DateTime.MinValue) return 0;
            double s = (DateTime.Now - RoundStartedAt).TotalSeconds;
            return s < 0 ? 0 : s;
        }

        /// <summary>
        /// 小波会刷给哪一方 —— 文档 1.2.4：
        ///   主波次后**两分钟内**，主波那一方拿到 **10 影响力** → 小波给它自己；
        ///   没拿到 → **敌对阵营**得到一次小波。
        ///   （文档原文："若主要重生波次刷新后未能在规定时间内得到相应的
        ///     迷你波次支援，则敌对阵营会得到一次迷你波次"）
        /// 返回 "" 表示还判断不了。
        /// </summary>
        public string SmallWaveFaction(DateTime now)
        {
            if (!HasBigWave) return NONE;
            if (now < BigWaveAt) return NONE;

            // 已经判定过就沿用
            if (_smallWaveFaction != null) return _smallWaveFaction;

            // 还在 2 分钟窗口内，等窗口结束再判
            if ((now - BigWaveAt).TotalSeconds < BigWaveWindow) return NONE;

            return DecideSmallWave(now);
        }

        /// <summary>窗口结束时的判定（也会在大波后 2 分钟整由外部调用一次）。</summary>
        public string DecideSmallWave(DateTime now)
        {
            if (!HasBigWave) return NONE;
            if (_smallWaveFaction != null) return _smallWaveFaction;

            // 大波那一方在这 2 分钟里拿到多少**影响力**（不是旧代码的"票"）
            int earned = Tickets.InfluenceInWindow(BigWaveFaction, BigWaveAt, BigWaveWindow);

            // 拿够 10 点 → 给自己；没拿够 → 给敌方
            _smallWaveFaction = (earned >= SmallWaveThreshold) ? BigWaveFaction : Opposite(BigWaveFaction);
            return _smallWaveFaction;
        }

        /// <summary>
        /// 主波次后窗口内，某一方拿到了多少影响力（小波条件用）。
        /// anyFaction = true 时算两方合计（还没填阵营时的兜底）。
        /// </summary>
        public int TicketsInBigWaveWindow(double windowSeconds, bool anyFaction)
        {
            if (!HasBigWave) return 0;
            if (!anyFaction) return Tickets.InfluenceInWindow(BigWaveFaction, BigWaveAt, windowSeconds);
            return Tickets.InfluenceInWindow(MTF, BigWaveAt, windowSeconds)
                 + Tickets.InfluenceInWindow(CHAOS, BigWaveAt, windowSeconds);
        }

        /// <summary>大波后 2 分钟窗口是否已经过完。</summary>
        public bool SmallWaveDecidable(DateTime now)
        {
            return HasBigWave && (now - BigWaveAt).TotalSeconds >= BigWaveWindow;
        }

        private bool _miniWaveDecided;

        /// <summary>
        /// 小波最晚该什么时候来（文档 1.2.4）：
        ///   默认主波后 150 秒（2 分 30 秒）；
        ///   每个"在小波发生前死亡"的玩家把这个时间减 5 秒，但**不能低于 2 分钟**。
        /// deadBeforeMiniWave = 面板上填的"小波前死了几个人"。
        /// </summary>
        public int MiniWaveDelaySeconds(int deadBeforeMiniWave)
        {
            if (deadBeforeMiniWave < 0) deadBeforeMiniWave = 0;
            int v = MiniWaveDelay - deadBeforeMiniWave * 5;
            if (v < MiniWaveMinDelay) v = MiniWaveMinDelay;
            return v;
        }

        /// <summary>
        /// 小波是不是"已经错过了"（主波后到点了还没见到小波）→ 该轮到敌方了。
        /// </summary>
        public bool MiniWaveMissed(DateTime now, int deadBeforeMiniWave)
        {
            if (!HasBigWave) return false;
            double since = (now - BigWaveAt).TotalSeconds;
            return since >= MiniWaveDelaySeconds(deadBeforeMiniWave);
        }

        /// <summary>
        /// 小波比"最晚时间"还能再晚多少（秒）仍算"它在路上"。
        /// 操作者实测：小波有时比推算晚约 40 秒才刷出来（它可能先给敌方、拖一会儿）。
        /// 这个宽限只影响"即将刷新大波 / 小波"那条**标签**和面板的推测文案。
        /// </summary>
        public int MiniWaveLateGrace = 90;

        /// <summary>
        /// 下一次刷新是不是**小波**（"即将刷新大波 / 小波"那条提示用）。
        ///
        /// 【踩过的坑（操作者报的 bug）】以前判据是 BigWaveWindow（主波后 2 分钟），
        /// 而小波按文档**最早也要主波后 2 分 30 秒**才出现 —— 那个窗口永远赶不上
        /// 小波真正到来的时刻，于是不管下一波是小波，提示都写"即将刷新大波"。
        /// 现在按小波自己的时间模型判：主波之后、这一波小波还没被记过、
        /// 而且还没超过"小波最晚该来 + 宽限"。
        /// </summary>
        public bool NextWaveIsSmall(DateTime now, int deadBeforeMiniWave)
        {
            if (!HasBigWave || !MiniWaveNotYetRecorded) return false;
            if (now < BigWaveAt) return false;
            double since = (now - BigWaveAt).TotalSeconds;
            return since <= MiniWaveDueAfterBigWave + MiniWaveLateGrace;
        }

        /// <summary>
        /// 大波之后，小波**预计真正进场**的时刻（秒）：播报延迟（`MiniWaveDelay`）
        /// + 进场延迟（`SmallWaveEntryDelaySeconds`，操作者实测约 1 分钟）。
        ///
        /// 【为什么要加上进场延迟】操作者报："从你提示的『即将刷新小波』到真正刷新小波
        /// 还有 1 分钟，估计得太长了，提示往后延一点。" 那一分钟正是"播报 → 人真进场"
        /// 的间隔（和 MarkWave 里小波锚点后移 60 秒是同一件事），所以倒计时/提示要按
        /// **进场时刻**算，而不是按播报时刻。
        /// </summary>
        public double MiniWaveDueAfterBigWave
        {
            get { return MiniWaveDelaySeconds(0) + SmallWaveEntryDelaySeconds; }
        }

        /// <summary>
        /// 待刷的那一波小波还有多少秒**真正进场**（没有待刷的就返回 -1）。
        /// 给"支援刷新"那条倒计时用：认出过刷新播报之后，它应该盯着**下一波真实刷新**
        ///（小波或大波），而不是"回合已进行多少秒"那个固定估算。
        /// </summary>
        public double MiniWaveRemaining(DateTime now)
        {
            if (!NextWaveIsSmall(now, 0)) return -1;
            return MiniWaveDueAfterBigWave - (now - BigWaveAt).TotalSeconds;
        }

        /// <summary>小波还没被记过（每个主波次只能拿一个小波）。</summary>
        public bool MiniWaveNotYetRecorded
        {
            get
            {
                for (int i = WaveHistory.Count - 1; i >= 0; i--)
                {
                    if (WaveHistory[i].At < BigWaveAt) break;
                    if (WaveHistory[i].IsSmall) return false;
                }
                return true;
            }
        }

        public static string Opposite(string faction)
        {
            if (faction == MTF) return CHAOS;
            if (faction == CHAOS) return MTF;
            return NONE;
        }

        public static string FactionName(string f)
        {
            if (f == MTF) return "MTF";
            if (f == CHAOS) return "Chaos";
            return "未指定";
        }

        #endregion

        #region SCP-079 发电机

        /// <summary>
        /// SCP-079 没有血量，它的“进度”体现在三台发电机上。
        ///
        /// 收容流程：拉下三台发电机的拉杆 → 各自启动 → 三台都启动后
        /// 去 079 收容室按按钮 → 超负荷重收容。
        ///
        /// 关键点：
        ///   · 三台各自独立（可能被不同阵营拉起来），所以要分别记阵营和进度
        ///   · 完全启动官方是 125 秒，但打开面板操作也要时间，所以默认按 120 秒算
        ///   · 启动中的发电机可以被别人再次拉杆关掉（进度缓慢回退），
        ///     一旦完全启动就无法再关闭 —— 所以“已启动”是不可逆的
        /// </summary>
        internal class Generator
        {
            public string Faction = NONE;   // 是哪一方拉起来的
            public bool Running;            // 正在启动中
            public bool Done;               // 已经算"完全启动"（倒计时到点，或播报确认）

            /// <summary>
            /// 游戏播报确认过（"第 N 台应急发电站已被激活"）。
            ///
            /// 【为什么要跟 Done 分开】倒计时走完只是**本地估计**：
            /// 手填的秒数填错了，估计就会偏。播报才是权威 ——
            /// 只有播报确认过的才算"真的启动完成"，影响力也只由它来记。
            /// </summary>
            public bool Confirmed;

            /// <summary>这一台的 +3 影响力已经记过了（同一台只记一次）。</summary>
            public bool Rewarded;

            public DateTime StartedAt = DateTime.MinValue;
            public int Duration = 120;      // 这次启动打算跑多少秒（默认 120）

            /// <summary>
            /// 起算用的剩余秒数（-1 = 没填过，按 Duration 推算）。
            ///
            /// 【语义】这是"**起算点**"，不是固定值：填了 90 就是从 90 开始往下减
            /// （以前这里被当成固定值，秒数永远停在 90、也永远到不了点）。
            /// </summary>
            public int ManualRemaining = -1;

            public bool Touched
            {
                get { return Running || Done || Faction != NONE; }
            }

            public void Clear()
            {
                Faction = NONE;
                Running = false;
                Done = false;
                Confirmed = false;
                Rewarded = false;
                StartedAt = DateTime.MinValue;
                ManualRemaining = -1;
            }

            /// <summary>还剩多少秒（0 = 已经到点）。</summary>
            public double Remaining(DateTime now)
            {
                if (Done) return 0;
                if (!Running) return ManualRemaining > 0 ? ManualRemaining : 0;

                double used = StartedAt == DateTime.MinValue
                    ? 0 : (now - StartedAt).TotalSeconds;

                // 填过/读到过秒数：以它为起算点，随时间往下减
                if (ManualRemaining >= 0)
                {
                    double left = ManualRemaining - used;
                    return left < 0 ? 0 : left;
                }

                // 只有"启动中"三个字、没读到秒数：按满速跑
                double d = Duration - used;
                return d < 0 ? 0 : d;
            }
        }

        /// <summary>三台发电机，固定 3 台。</summary>
        public const int GeneratorCount = 3;

        public readonly Generator[] Generators = NewGenerators();

        private static Generator[] NewGenerators()
        {
            Generator[] g = new Generator[GeneratorCount];
            for (int i = 0; i < g.Length; i++) g[i] = new Generator();
            return g;
        }

        /// <summary>本回合是否有 SCP-079（用户在面板上标）。</summary>
        public bool Has079
        {
            get
            {
                ScpRecord r = Find("079");
                return r != null && r.Present;
            }
        }

        /// <summary>已经完全启动的发电机台数。</summary>
        public int GeneratorsActive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Generators.Length; i++)
                    if (Generators[i].Done) n++;
                return n;
            }
        }

        /// <summary>正在启动中的台数。</summary>
        public int GeneratorsRunning
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Generators.Length; i++)
                    if (Generators[i].Running && !Generators[i].Done) n++;
                return n;
            }
        }

        /// <summary>
        /// 三台都启动了 → 可以去收容室按按钮了。
        /// </summary>
        public bool AllGeneratorsReady
        {
            get { return GeneratorsActive >= GeneratorCount; }
        }

        /// <summary>
        /// 某台发电机是不是“快启动了”（默认最后 20 秒），用来提醒玩家。
        /// </summary>
        public const int GenSoonSeconds = 20;

        /// <summary>任意一台进入最后 20 秒 → 提示。</summary>
        public bool AnyGeneratorSoon(DateTime now)
        {
            for (int i = 0; i < Generators.Length; i++)
            {
                Generator g = Generators[i];
                if (!g.Running || g.Done) continue;
                double left = g.Remaining(now);
                if (left > 0 && left <= GenSoonSeconds) return true;
            }
            return false;
        }

        /// <summary>
        /// 需要高亮提醒的情况：已经有 2 台完全启动，
        /// 最后 1 台在面板上填了时间，且进入最后 20 秒。
        /// 这时候 079 马上要被重收容了，得醒目一点。
        /// </summary>
        public bool FinalGeneratorUrgent(DateTime now)
        {
            if (GeneratorsActive != GeneratorCount - 1) return false;
            for (int i = 0; i < Generators.Length; i++)
            {
                Generator g = Generators[i];
                if (g.Done || !g.Running) continue;
                double left = g.Remaining(now);
                if (left > 0 && left <= GenSoonSeconds) return true;
            }
            return false;
        }

        /// <summary>把三台发电机全部重置。</summary>
        public void ResetGenerators()
        {
            for (int i = 0; i < Generators.Length; i++) Generators[i].Clear();
        }

        /// <summary>
        /// 推进计时：倒计时到点的先标成"时间到"。
        ///
        /// 【口径：谁能算"完全启动"，一切以播报为准】
        ///   · 倒计时走完（手填的秒数 / 读到的秒数 / 满速）→ 只是**本地估计**：
        ///     Done = true，但 Confirmed 还是 false，而且**不记影响力**
        ///     （手填的秒数填错了也不会把账记歪）；
        ///   · 游戏播报"第 N 台已被激活" → <see cref="ConfirmGenerator"/>，
        ///     立刻算完成（不管倒计时走到哪），影响力由它来记。
        /// </summary>
        public void TickGenerators(DateTime now)
        {
            for (int i = 0; i < Generators.Length; i++)
            {
                Generator g = Generators[i];
                if (!g.Running || g.Done) continue;
                if (g.Remaining(now) <= 0)
                {
                    g.Done = true;          // 估计完成；等播报来确认
                    g.Running = false;
                    g.ManualRemaining = -1;
                }
            }
        }

        /// <summary>
        /// 游戏播报确认某一台"已被激活" → 立刻算完全启动，并按文档记一次
        /// +3 影响力（−10 秒要本回合有 079，RecordAt 里判）。
        ///
        /// 【和别的来源的优先级】播报 > 倒计时。倒计时已经把它标成 Done 了，
        /// 播报来了照样要确认 + 记账（"一切以播报为准"）。
        /// 同一台只会记一次账；阵营认不出来时不记（可用登记页的格子手动补）。
        /// 返回一句说明，给日志和提示用。
        /// </summary>
        public string ConfirmGenerator(int index, DateTime now, string source)
        {
            if (index < 0 || index >= Generators.Length) return "";
            Generator g = Generators[index];

            bool first = !g.Confirmed;
            g.Done = true;
            g.Confirmed = true;
            g.Running = false;
            g.ManualRemaining = -1;

            if (g.Rewarded) return first ? "（已完全启动；这一台的影响力之前记过了）" : "";
            if (g.Faction != MTF && g.Faction != CHAOS)
                return "（已完全启动；认不出是哪一方拉的，影响力没记"
                     + " —— 要用的话到「登记」页点一下那个格子）";

            g.Rewarded = true;
            int got = Tickets.RecordAt(TicketModel.Objective.ActivateGenerator,
                g.Faction, Has079, now, "发电机 " + (index + 1) + " 已被激活（" + source + "）");
            return "（已完全启动：" + FactionName(g.Faction) + " +" + got + " 影响力"
                 + (Has079 ? "，刷新计时 −10 秒" : "，本回合无 079 故不减计时") + "）";
        }

        #endregion
    }
}
