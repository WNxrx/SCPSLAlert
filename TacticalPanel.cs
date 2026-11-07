using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 悬浮式战术面板（默认 F10）。
    ///
    /// 每个 SCP 一格：填总血量 / 当前血量，点「阵营」循环切换 MTF → Chaos → 未指定，
    /// 点「击杀」标记这个 SCP 已经阵亡。
    /// 下面实时结算票数、阵营刷新时间、大波小波预警。
    ///
    /// 票数 = 累计已损失血量 ÷ 200 + 击杀数 × 15
    /// 每次击杀让阵营刷新时间 -10 秒。
    ///
    /// 安全：所有输入都按“非数字 = 0”处理，永远不会因为乱输入崩掉。
    /// </summary>
    internal class TacticalPanel : OverlayPanel
    {
        private readonly Config _cfg;
        private readonly Action _onSaved;
        private readonly TacticalLog _log;

        /// <summary>“刚刷了一波大波”的阵营，用面板上的格子切换。</summary>
        private string _bigWaveSide = TacticalLog.MTF;

        /// <summary>「记录」页那一行"最近登记的目标"。切页时会重建，所以可能为 null。</summary>
        private Row _recentNote;

        /// <summary>「记录」页的影响力时间线。</summary>
        private Row _timelineNote;

        // ---- 总览页的预测（下一步还有没有重生）----
        private Row _ovAnyCard;      // "全人类" 大牌（百分比）
        private Row _ovProbNote;     // 预测依据说明

        /// <summary>改动后自动保存（防抖，避免每敲一个字就写盘）。</summary>
        private readonly Timer _saveTimer;

        /// <summary>标记某个 SCP 阵亡时对外发通知，让“SCP 阵亡提示”真的有内容。</summary>
        public event Action<string> ScpKilled;

        /// <summary>
        /// 时间校准请求（由 Program 接到 AlertManager / TimeEstimator）。
        /// kind = "round" / "support" / "decon" / "deadman"，value = 秒。
        /// </summary>
        public Action<string, double> TimeCalibrate;

        /// <summary>
        /// 「校准」页上"当前估算"那一段文字（由 Program 接到 AlertManager）。
        /// 每次 Recalc 都会取一次，所以显示的是实时值。
        /// </summary>
        public Func<string> TimeCalibrateState;

        /// <summary>停止核弹与亡者协议两个特殊倒计时。</summary>
        public Action StopSpecialCountdowns;

        private Row _battleLatestWaveNote;
        private const int BattleWaveRows = 12;

        public TacticalPanel(Config cfg, TacticalLog log, Action onSaved)
        {
            _cfg = cfg;
            _log = log;
            _onSaved = onSaved;

            // 改完就自动存，不用等回车 / 关面板
            _saveTimer = new Timer();
            _saveTimer.Interval = 800;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); Save(); };

            // 回车 = 确认：这时候才把"用户填好的发电机剩余秒数"真正起算
            Submitted += delegate { ConfirmGenerators(); Save(); HidePanel(); };
            Cancelled += delegate { Save(); HidePanel(); };

            // 面板开着的时候定时走表。
            // 【为什么需要】以前面板只在"你改了某一格"时才重算，所以发电机的
            // 倒计时在界面上根本不走 —— 你只能看到"启动中"三个字，看不到秒数，
            // 关掉再打开又变回当初填的数字。现在每 500ms 刷一次，
            // 秒数会实时往下走，关掉时保存的也是真实剩余。
            _liveTimer = new Timer();
            _liveTimer.Interval = 500;
            _liveTimer.Tick += OnLiveTick;
            _liveTimer.Start();

            // 操作者要求：战术面板用"两根发光竖线"的全息展开 / 收起动画
            // （动画质量 / 帧率由设置里的「动画效果」决定；关掉就瞬间出现）。
            UseHoloIntro = true;

            // 分页：一页只看一类事，别全堆在一起
            // 「校准」页 = 原来那个独立的回合时间校准悬浮窗，现在直接并进面板
            Tabs = new string[] { "总览", "登记", "战况", "发电机", "记录", "校准", "说明" };
            CurrentTab = 0;

            // 锁定面板高度：切页时窗口不再忽大忽小。
            // 按"最高那一页"（说明页，内容约 640px）取；加上标题 / 页签 /
            // 提示行后总高约 800px，1080p 放得下。
            // 700 = 六页里最高的那页（「说明」页）也放得下的值：
            // 锁同一个高度，切页时面板不会跳大小；总览页两列各约 620 / 700。
            FixedContentHeight = 700;
        }

        /// <summary>
        /// 切页之前先把当前页输入框里的东西收进模型并存盘。
        /// 不收的话，用户在第 3 页填的血量会在切页时被丢掉。
        /// </summary>
        protected override void FlushInputs()
        {
            try
            {
                Recalc();
                Save();
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "切页前保存失败", ex);
            }
        }

        /// <summary>切页之后把新页的数据填上。</summary>
        protected override void OnTabChanged()
        {
            try { Recalc(); } catch { }
        }

        /// <summary>
        /// 页签标题保持干净（不放动态数字 —— 令牌数已经在总览页的大牌上了）。
        /// 这个方法留个钩子，将来要在页签上带状态时改这里。
        /// </summary>
        private void RefreshTabTitles()
        {
            if (Tabs == null || Tabs.Length < 6) return;
            Tabs[0] = "总览";
        }

        /// <summary>上一次重绘是在第几秒（同一个 500ms 周期里不重复重画）。</summary>
        private int _lastLiveSecond = -1;

        /// <summary>面板可见时的定时刷新（只在可见时才干活）。</summary>
        private void OnLiveTick(object sender, EventArgs e)
        {
            if (!Visible) return;
            // 全息展开 / 收起 / 切页滑动期间不要重画内容（动画帧自己接管绘制）
            if (IsHoloAnimating || IsTabAnimating) return;
            try
            {
                Recalc();
                // 面板上会变的只有"秒数 / 倒计时"这类，一秒最多变一次；
                // 定时器是 500ms 一拍，同一秒里那次重画纯属浪费 ——
                // 以前每拍都整张 900×800 重画 + UpdateLayeredWindow，
                // 游戏里表现为面板打开时帧率掉一点。
                int sec = DateTime.Now.Second;
                if (sec == _lastLiveSecond) return;
                _lastLiveSecond = sec;
                if (Visible) Redraw();
            }
            catch { }
        }

        /// <summary>
        /// 回车确认：把标了"待确认"的发电机真正起算 —— 你填的秒数从这一刻开始往下减。
        /// （用户要求：填完秒数按回车，倒计时才开始走）
        /// </summary>
        private void ConfirmGenerators()
        {
            bool any = false;
            for (int g = 0; g < _genPending.Length; g++)
            {
                if (!_genPending[g]) continue;
                _genPending[g] = false;
                AdoptGeneratorInput(g);
                any = true;
            }
            if (any) Recalc();
        }

        /// <summary>
        /// 把「剩余」格里的秒数当成起算点落进模型：从现在开始按这个数往下减。
        /// 把格子清空 = 不启动（把拉杆拉回去）。
        /// </summary>
        private void AdoptGeneratorInput(int g)
        {
            string k = (g + 1).ToString();
            TacticalLog.Generator gen = _log.Generators[g];
            if (gen.Done) return;      // 已经算完成的不用再起算

            double baseSec = Num(GetValue("genbase_" + k));
            if (baseSec <= 0) baseSec = 120;
            gen.Duration = (int)baseSec;

            string raw = GetValue("gen_" + k);
            if (string.IsNullOrEmpty(raw))
            {
                gen.Running = false;
                gen.ManualRemaining = -1;
                return;
            }

            int want = (int)Num(raw);
            if (want < 0) want = 0;
            gen.Running = true;
            gen.StartedAt = DateTime.Now;
            gen.ManualRemaining = want;      // 起算点（不是固定值）
            if (gen.Faction == TacticalLog.NONE) gen.Faction = TacticalLog.MTF;
        }

        /// <summary>哪些发电机改过数值、还在等回车确认。</summary>
        private readonly bool[] _genPending = new bool[TacticalLog.GeneratorCount];

        private Timer _liveTimer;

        /// <summary>安排一次自动保存（防抖）。</summary>
        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        protected override string PanelTitle
        {
            get { return "战术面板"; }
        }

        protected override string PanelHint
        {
            get
            {
                string closeKey = KeyName(_cfg.HotkeyTactical, "F10");
                return "点上方页签切换页面　·　回车 / ESC = 保存并关闭（" + closeKey + "）"
                     + "　·　Tab = 下一格　·　登记页点格子就是登记一次";
            }
        }

        /// <summary>把一个快捷键配置转成可读名字，空的话用兜底值。</summary>
        private static string KeyName(string cfg, string fallback)
        {
            if (string.IsNullOrEmpty(cfg)) return fallback;
            return cfg.Trim();
        }

        protected override int ContentWidth
        {
            // 1080p 放得下：两列 760 → 面板宽约 1576+边距，每列都够放一张阵营牌。
            get { return 760; }
        }

        public TacticalLog Log
        {
            get { return _log; }
        }

        private string Loaded(string key, string fallback)
        {
            string v = _cfg == null ? null : _cfg.GetTactical(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        // ============================================================
        // 建页时的初始值：模型优先，config 只是兜底
        //
        // 【为什么要这样】config 是"上次面板关掉时存下来的"，
        // 而模型是 OCR 一直在写的那一份。如果建页时只读 config，
        // 那么"面板没被唤出来"期间识别到的数据就要等下一次 Config 同步
        // 才看得到，甚至会被 config 里的旧值盖回去
        // （面板一建行、Recalc 一写回，OCR 刚读到的血量就没了）。
        // 让格子一开始就显示模型里的值，"没唤出面板"就彻底不影响数据。
        // ============================================================

        /// <summary>总血量格子的初始值：模型里的上限 → config → 目录默认值。</summary>
        private string SeedTotalHp(string code, int defaultHp)
        {
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec != null && rec.TotalHp > 0) return rec.TotalHp.ToString();
            return Loaded("hp_" + code, defaultHp.ToString());
        }

        /// <summary>当期血量格子的初始值：模型里的最新读数 → config。</summary>
        private string SeedCurrentHp(string code)
        {
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec != null && rec.CurrentHp >= 0) return rec.CurrentHp.ToString();
            return Loaded("cur_" + code, "");
        }

        /// <summary>发电机"剩余"格子的初始值：正在跑就给模型里的实时剩余 → config。</summary>
        private string SeedGenRemaining(TacticalLog.Generator gen, string key)
        {
            if (gen == null) return Loaded(key, "");
            if (gen.Done) return "0";
            if (gen.Running)
            {
                double left = gen.Remaining(DateTime.Now);
                if (left < 0) left = 0;
                return ((int)Math.Ceiling(left)).ToString();
            }
            return Loaded(key, "");
        }

        private static double Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double v;
            if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return 0;
        }

        protected override void BuildRows()
        {
            ClearRows();

            // 先把"在场"标记读回模型（几页都要用到这个状态）
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string c = TacticalLog.ScpCodes[i];
                TacticalLog.ScpRecord rr = _log.Find(c);
                if (rr != null && rr.ChangedAt == DateTime.MinValue)
                    rr.Present = Loaded("pre_" + c, "") == "1";
            }

            switch (CurrentTab)
            {
                case 0: BuildOverview(); break;
                case 1: BuildObjectives(); break;
                case 2: BuildBattlefield(); break;
                case 3: BuildGenerators(); break;
                case 4: BuildRecord(); break;
                case 5: BuildCalibrate(); break;
                default: BuildHelp(); break;
            }

            Recalc();
        }

        // ============================================================
        // 配色：直接照游戏 HUD 那两条
        //   MTF = 蓝 RGB(109,159,247)，混沌 = 绿 RGB(96,143,56)，危险 = 红
        // ============================================================

        private static readonly Color MtfColor = Color.FromArgb(109, 159, 247);
        private static readonly Color ChaosColor = Color.FromArgb(150, 196, 96);
        private static readonly Color BadColor = Color.FromArgb(255, 110, 110);
        private static readonly Color WarnColor2 = Color.FromArgb(255, 190, 90);

        // ============================================================
        // 第 1 页 · 总览
        //
        // 一打开就要能看清"两边各有多少重生令牌、影响力到哪了、下一波多久"，
        // 所以把游戏 HUD 那两条搬进来：左边 MTF 蓝牌、右边混沌绿牌，
        // 令牌数是超大数字，影响力用 "49 / 80" 这种和游戏一致的写法。
        // ============================================================

        private Row _ovMtfCard, _ovChaosCard;
        private Row _ovGenCard, _ovGenState;
        private Row _ovNextWave;
        private Row _ovMtfInf, _ovMtfWave, _ovMtfRt;
        private Row _ovChaosInf, _ovChaosWave, _ovChaosRt;
        private Row _ovShare, _ovTick, _ovKill, _ovLast, _ovLastWave;

        private void BuildOverview()
        {
            // ---- 左列：MTF + 预测 ----
            // 预测放在左列：它比"刷新操作"重要，而且能把左列填得跟右列差不多高，
            // 免得总览页一边满一边空。
            AddSection("重生令牌 · 影响力", MtfColor);
            _ovMtfCard = AddCard("MTF", "0", "", MtfColor);
            _ovMtfInf = AddReadout("影响力", "ov_mtf_inf");
            _ovMtfWave = AddReadout("下一波刷新", "ov_mtf_wave");
            _ovMtfRt = AddReadout("距下一个令牌", "ov_mtf_next");

            // ---- 预测：下一步还会不会有重生 ----
            // 操作者说玩家真正关心的是"重生次数"而不是影响力，
            // 所以把预测做成醒目的大数字，占满总览页下面那块空位。
            AddSection("当前至少还有一张大票的概率", WarnColor2);
            _ovAnyCard = AddCard("全人类（MTF + 混沌）当前有大票", "—", "", WarnColor2);
            _ovProbNote = AddNote("");

            // ---- 下一波刷新（预计）：操作者要求总览页上就能看到"预计小波刷新时间" ----
            // 口径和"支援刷新"那条倒计时完全一致：主波之后按
            // MiniWaveDelay(150) + 进场延迟(60) 算出小波真正进场的时刻；
            // 小波已经刷过了就退回显示下一波大波。
            // 【只加一节一行】总览页左列的高度已经贴着 FixedContentHeight(700) 了，
            // 再加说明文字会把右下角顶出面板（`_panelsmoke2` 的高度一致性会当场失败）。
            // 【名字避开歧义】上面两方那两行"下一波刷新"说的是各自的重生波倒计时，
            // 这一节说的是"下一波刷新具体是小波还是大波、什么时候来"，所以标题写"小波刷新"。
            AddSection("小波刷新（预计）", WarnColor2);
            _ovNextWave = AddReadout("下一波", "ov_next_wave");

            // ---- 左列下半：SCP-079 发电机（操作者要求：一个框 + 一个齿轮）----
            // 【为什么放左列】原来它在右列，结果右列比左列高出 100 多像素，
            // 右下角顶出面板、左下角却空着。挪过来两列就齐了。
            AddSection("SCP-079 · 发电机", AccentColor);
            _ovGenCard = AddCard("SCP-079 · 发电机", "0 / 3", "", AccentColor);
            _ovGenCard.BarMax = TacticalLog.GeneratorCount;
            _ovGenCard.ShowGear = true;
            _ovGenCard.GearKey = "ov_gen_gear";
            SetCardKey(_ovGenCard, "ov_gen_gear");
            _ovGenState = AddNote("");
            // 【两行并一行】总览左列的高度贴着 FixedContentHeight(700)，
            // 齿轮那句提示并进动态状态那行一起刷（见 Refresh 里的 SetNote）。

            // ---- 右列：混沌 + 本局概况 + 操作 ----
            AddColumnBreak();
            AddSection("重生令牌 · 影响力", MtfColor);
            _ovChaosCard = AddCard("混沌分裂者", "0", "", ChaosColor);
            _ovChaosInf = AddReadout("影响力", "ov_chaos_inf");
            _ovChaosWave = AddReadout("下一波刷新", "ov_chaos_wave");
            _ovChaosRt = AddReadout("距下一个令牌", "ov_chaos_next");

            AddSection("本局概况", AccentColor);
            _ovTick = AddReadout("回合时间", "ov_clock");
            _ovKill = AddReadout("击杀 / 已损失血量", "ov_killhp");
            // 【刚刷了什么】操作者要求：刚刷完容易忘是哪一方、大波还是小波，
            // 所以在总览页留一行档（完整顺序在「记录」页）。
            _ovLastWave = AddReadout("刚刚刷新", "ov_lastwave");
            _ovShare = AddNote("");
            _ovLast = AddNote("");

            AddSection("刷新操作", WarnColor2);
            AddButtonStrip("刚刷了一波（自动判大小波）", "ov_wave", unchecked((int)0xFFFFD166));
            AddButtonStrip("清空本局数据", "ov_reset", unchecked((int)0xFFFF6E6E));
            AddNote("点「刚刷了一波」= 顺着上一波记：上一波是大波 → 这次记小波；上一波是小波 → 这次记大波。两边计时照样重置。");

            // ---- 待归属的阵亡（操作者要求）----
            // 认得出"哪个 SCP 死了"、但认不出是哪一方杀的 → 挂在这里，
            // 点一下 [MTF] / [混沌] 就按文档记 +15 影响力 / 计时 −10 秒。
            // 【只列 3 条】这是唯一会"越来越长"的一块，列太多会把右下角顶出面板。
            AddSection("待归属的阵亡（点一下定给哪一方）", BadColor);
            if (_log.PendingKills.Count == 0)
            {
                AddNote("（暂无）认不出击杀方的阵亡会出现在这里");
            }
            else
            {
                int shown = 0;
                for (int i = 0; i < _log.PendingKills.Count && shown < 3; i++, shown++)
                {
                    string code = _log.PendingKills[i].Code;
                    AddCellRow2("SCP-" + code + " 已阵亡", 
                        "pend_" + code + "_" + TacticalLog.MTF, "记给 MTF", 96,
                        "pend_" + code + "_" + TacticalLog.CHAOS, "记给混沌", 100);
                }
                if (_log.PendingKills.Count > shown)
                    AddNote("（还有 " + (_log.PendingKills.Count - shown) + " 条，先处理上面的）");
            }
        }

        // ============================================================
        // 第 2 页 · 登记
        //
        // 目标表做成"点一下 +1"的格子：左边是自动记账的（击杀 SCP、发电机），
        // 右边是手动登记的（道具、逃离、武装人员）。点一下就能记，不用填表。
        // ============================================================

        private void BuildObjectives()
        {
            // 只分两列：三列会宽到 2396px，1080p 屏幕放不下。
            AddSection("MTF 目标（点一下 = 登记一次）", MtfColor);
            AddCellRow("击杀可玩 SCP", "obj_mtf_kill", "点此登记", 76);
            AddCellRow("发电机成功启动", "obj_mtf_gen", "点此登记", 76);
            AddCellRow("科学家未被捆绑逃离", "obj_mtf_sci", "点此登记", 76);
            AddCellRow("被捆绑的 D 级逃离", "obj_mtf_dclass", "点此登记", 76);

            AddSection("通用目标（默认记给 MTF）", WarnColor2);
            AddCellRow("击杀敌对武装人员", "obj_armed", "点此登记", 76);
            AddCellRow("使用消耗性 SCP 物品", "obj_cons", "点此登记", 76);
            AddCellRow("首次使用独特 SCP 物品", "obj_uniq", "点此登记", 76);

            AddColumnBreak();

            AddSection("混沌目标（点一下 = 登记一次）", ChaosColor);
            AddCellRow("击杀可玩 SCP", "obj_chaos_kill", "点此登记", 76);
            AddCellRow("发电机成功启动", "obj_chaos_gen", "点此登记", 76);
            AddCellRow("平民逃离转为混沌", "obj_chaos_civ", "点此登记", 76);

            AddSection("怎么用", AccentColor);
            AddNote("· 点一下 = 登记一次（格子显示「×次数」）；点错到「记录」页撤销。");
            AddNote("· 击杀 SCP / 发电机认得出播报会自动记账，手动点不会重复。");
            AddNote("· 通用目标默认记给 MTF；那一下是混沌打出来的请用混沌格子。");
            AddNote("影响力：击杀 SCP +15　发电机 +3　独特物品 +2　消耗物品 +1");
            AddNote("武装人员 +1　累计 10% 生命 +2　科学家 / D 级 / 平民逃离 +5（详见「说明」页）");
        }

        // ============================================================
        // 第 3 页 · 战况
        // ============================================================

        private void BuildBattlefield()
        {
            AddSection("SCP 血量与阵营（总血量按你服务器改）", BadColor);

            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;   // 079 没有血量，在「发电机」页

                TacticalLog.ScpRecord rec = _log.Find(code);
                int defHp = rec != null && rec.TotalHp > 0 ? rec.TotalHp : TacticalLog.ScpDefaultHp[i];

                AddInput5("SCP-" + code,
                    "hp_" + code, SeedTotalHp(code, defHp), 58,
                    "cur_" + code, SeedCurrentHp(code), 58,
                    "", "fac_" + code, 54,
                    "", "kill_" + code, 48,
                    "", "pre_" + code, 44);
            }

            AddNote("点「阵营」循环 MTF / 混沌 / 未指定；「击杀」标阵亡；「在场」标本回合有没有它。");
            AddButton("清空全部改动", "clearall");
            AddSection("本局已刷新波次（点彩色波次切换大小波）", WarnColor2);
            for (int i = 0; i < BattleWaveRows; i++)
            {
                AddButtonStrip("", "wave_type_" + i, unchecked((int)0xFF6D9FF7),
                    "换阵营", "wave_faction_" + i, unchecked((int)0xFFD3DAE8),
                    "删除", "wave_delete_" + i, unchecked((int)0xFFFF6E6E));
                SetRowHidden("wave_type_" + i, true);
            }

            AddColumnBreak();

            AddSection("结算", AccentColor);
            AddReadout("已损失血量", "lost");
            AddReadout("击杀数", "kills");
            AddReadout("总影响力", "total");
            AddReadout("MTF 影响力", "mtf");
            AddReadout("混沌 影响力", "chaos");
            AddReadout("MTF 令牌", "tok_mtf");
            AddReadout("混沌 令牌", "tok_chaos");
            AddReadout("共享额外令牌", "tok_share");
            AddReadout("距下一个令牌", "nexttoken");

            AddSection("刷新", WarnColor2);
            _battleLatestWaveNote = AddNote("");
            AddButton("切换最近波次为大波 / 小波", "wave_reclassify");
            AddReadout("MTF 下一波", "wave_mtf");
            AddReadout("混沌 下一波", "wave_chaos");
            AddReadout("实际刷新间隔", "interval");
            AddButton("停止核弹 / 亡者协议", "stop_special");
        }

        // ============================================================
        // 第 4 页 · 发电机
        // ============================================================

        private void BuildGenerators()
        {
            AddSection("SCP-079 · 发电机", AccentColor);
            AddCellRow("本回合有 079（点格子切换）", "has079", "", 84);
            AddNote("079 没有血量；拉满三台发电机后去 079 收容室按按钮。没有 079 时发电机不减刷新计时。");

            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                string k = (g + 1).ToString();
                Row gr = AddInput4("发电机 " + k,
                    "gen_" + k, SeedGenRemaining(_log.Generators[g], "gen_" + k), 70,
                    "genbase_" + k, Loaded("genbase_" + k, "120"), 60,
                    "", "genfac_" + k, 54,
                    "", "genrun_" + k, 52);
                gr.Small1 = "剩余";
                gr.Small2 = "满速";
            }
            AddReadout("发电机状态", "genstate");
            AddNote("「剩余秒数」填了就开始倒计时；「已启动」以游戏播报为准 —— 播报一响直接算完成，记这一方 +3 影响力。");
            AddNote("「满速」是这台打算跑多少秒，默认 120（官方 125，留 5 秒操作）。");

            AddColumnBreak();
            AddSection("场地与阵营", WarnColor2);
            AddCellRow("刚刷大波的是哪一方（点格子切换）", "bigside", "", 84);
            _waveNote1 = AddNote("");
            _waveNote2 = AddNote("");
        }

        // ============================================================
        // 第 5 页 · 记录
        // ============================================================

        private void BuildRecord()
        {
            AddSection("刷新增援顺序（本回合，自动记录）", WarnColor2);
            _waveOrderNote = AddNote("");
            _wavePredictNote = AddNote("");

            AddSection("屏幕上方票数（死亡时自动读取）", MtfColor);
            _ticketNote = AddNote(TicketText());

            AddColumnBreak();

            AddSection("最近登记的目标", ChaosColor);
            _recentNote = AddNote("");
            AddButton("撤销最后一次登记", "undo_obj");
            AddNote("撤销只影响影响力 / 令牌，不会动 SCP 血量那些数据。");

            AddSection("影响力时间线", AccentColor);
            _timelineNote = AddNote("");
        }

        // ============================================================
        // 第 6 页 · 校准
        //
        // 【操作者要求】原来那个独立的"回合时间校准"悬浮窗（F7）不好用
        // （输入进不去），直接并进战术面板这一页：填一项、点一下按钮就校准。
        // ============================================================

        private Row _calMsg;      // 校准结果 / 错误提示
        private Row _calState;    // 当前估算（实时）

        private void BuildCalibrate()
        {
            AddSection("时间校准", AccentColor);
            AddNote("中途加入、或者计时偏了，就在这里填一次。填一项就够，不用全填。");
            AddNote("填完点下面的按钮；回车 / ESC 是保存并关面板，不会执行校准。");

            AddInput("当前回合已进行（分.秒）", "cal_round", "", 170);
            AddInput("支援还剩（秒）", "cal_sup", "", 110);
            AddInput("净化还剩（秒）", "cal_dec", "", 110);
            AddInput("终焉协议还剩（秒）", "cal_dm", "", 110);

            AddButton("按上面填的校准", "cal_apply");
            _calMsg = AddNote("");

            AddColumnBreak();

            AddSection("当前估算", MtfColor);
            _calState = AddNote("");

            AddSection("怎么填", WarnColor2);
            AddNote("· 回合已进行：照 HUD 打就行 —— 11.22 = 11 分 22 秒；也能填 6:10 / 370（秒）。");
            AddNote("· 支援 / 净化 / 终焉：填屏幕上那条倒计时还剩多少秒。");
            AddNote("· 单项留空不影响其它项；填了多项时按 回合 → 支援 → 净化 → 终焉 的顺序只取第一项。");
            AddNote("· 终焉填 0 = 直接归零（会触发核弹那一步），一般别填 0。");
            AddNote("· 校准只影响计时，不改战术数据；校准完这一页会显示新的估算值。");
        }

        /// <summary>「校准」页的按钮：把填的那一项交给 Program 去校准。</summary>
        private void ApplyCalibration()
        {
            string round = SafeTrim(GetValue("cal_round"));
            string sup = SafeTrim(GetValue("cal_sup"));
            string dec = SafeTrim(GetValue("cal_dec"));
            string dm = SafeTrim(GetValue("cal_dm"));

            double v;
            if (round.Length > 0)
            {
                if (!TimeInput.TryMinutes(round, out v))
                { SetCalMsg("「当前回合已进行」读不懂：填 11.22（=11分22秒）/ 6:10 / 370 都行"); return; }
                SetCalMsg(DoCalibrate("round", v)); ClearCalInputs(); return;
            }
            if (sup.Length > 0)
            {
                if (!TimeInput.TrySeconds(sup, out v))
                { SetCalMsg("「支援还剩」读不懂：填秒数，比如 95"); return; }
                SetCalMsg(DoCalibrate("support", v)); ClearCalInputs(); return;
            }
            if (dec.Length > 0)
            {
                if (!TimeInput.TrySeconds(dec, out v))
                { SetCalMsg("「净化还剩」读不懂：填秒数，比如 320"); return; }
                SetCalMsg(DoCalibrate("decon", v)); ClearCalInputs(); return;
            }
            if (dm.Length > 0)
            {
                if (!TimeInput.TrySeconds(dm, out v))
                { SetCalMsg("「终焉协议还剩」读不懂：填秒数，比如 120"); return; }
                SetCalMsg(DoCalibrate("deadman", v)); ClearCalInputs(); return;
            }

            SetCalMsg("四个框都是空的：填一项再点校准");
        }

        private string DoCalibrate(string kind, double sec)
        {
            if (TimeCalibrate == null) return "这个版本没接上校准入口（内部错误）";
            try { TimeCalibrate(kind, sec); }
            catch (Exception ex)
            {
                Logger.Err("校准", "面板校准失败", ex);
                return "校准失败：" + ex.Message;
            }
            string name = kind == "round" ? "回合已进行"
                        : kind == "support" ? "支援剩余"
                        : kind == "decon" ? "净化剩余" : "终焉协议剩余";
            Logger.On("校准", "战术面板「校准」页：" + name + " = " + (int)Math.Round(sec) + " 秒");
            return "已校准：" + name + " " + (int)Math.Round(sec) + " 秒（看右边「当前估算」）";
        }

        private void ClearCalInputs()
        {
            SetValue("cal_round", "");
            SetValue("cal_sup", "");
            SetValue("cal_dec", "");
            SetValue("cal_dm", "");
        }

        private void SetCalMsg(string text)
        {
            if (_calMsg != null) SetNote(_calMsg, text == null ? "" : text);
            Redraw();
        }

        private static string SafeTrim(string s)
        {
            return s == null ? "" : s.Trim();
        }

        /// <summary>
        /// 当前是不是停在「校准」页（F7 再按一次就收起面板用）。
        /// </summary>
        public bool IsCalibrationTab
        {
            get
            {
                return Tabs != null && CurrentTab >= 0 && CurrentTab < Tabs.Length
                    && Tabs[CurrentTab] == "校准";
            }
        }

        /// <summary>
        /// 直接打开「校准」页（F7 / 托盘菜单 / 进服自动校准都走这里）。
        /// 不再有单独的悬浮窗。
        /// </summary>
        public void OpenCalibrationTab()
        {
            int idx = 5;
            for (int i = 0; Tabs != null && i < Tabs.Length; i++)
                if (Tabs[i] == "校准") { idx = i; break; }
            try { if (CurrentTab != idx) GoTab(idx); } catch { }
            ShowPanel();
        }

        // ============================================================
        // 第 7 页 · 说明
        // ============================================================

        private void BuildHelp()
        {
            AddSection("这套面板在算什么", AccentColor);
            AddNote("全部按官方机制（见根目录 机制\\刷新.txt 与 机制\\核弹.txt）：");

            AddSection("影响力（不是按血量算的）", WarnColor2);
            AddNote("击杀可玩 SCP（不含 049-2）   计时 −10s   影响力 +15");
            AddNote("打掉 SCP 最大生命 10% 以上   计时 −1s    影响力 +2");
            AddNote("首次使用独特 SCP 物品        计时 −5s    影响力 +2");
            AddNote("使用消耗性 SCP 物品          计时 −2s    影响力 +1");
            AddNote("击杀敌对武装人员             计时 −2s    影响力 +1");
            AddNote("发电机成功启动               计时 −10s   影响力 +3（无 079 不减计时）");
            AddNote("MTF：科学家未捆绑逃离        计时 −30s   影响力 +5");
            AddNote("MTF：被捆绑 D 级逃离         计时 −10s   影响力 +5");
            AddNote("混沌：平民逃离转混沌         计时 −20s   影响力 +5");

            AddColumnBreak();

            AddSection("重生令牌", ChaosColor);
            AddNote("· 每方开局自带 1 个，重生一次消耗 1 个。");
            AddNote("· 影响力到 40 / 80 / 150 / 200 各得 1 个。");
            AddNote("· 每回合最多 3 个额外令牌，两方共享（所以最多 5 波）。");
            AddNote("· 没有令牌时刷新计时倒到 30 秒就停住，拿到令牌才继续。");

            AddSection("刷新计时", MtfColor);
            AddNote("· 基准 5 分钟；任一方重生 → 两边都重置。");
            AddNote("· 重生方每重生一个玩家，计时额外 +10 秒。");
            AddNote("· 上面那些目标会按发生时间把计时往前拨。");

            AddSection("小波", WarnColor2);
            AddNote("· 主波后 2 分钟内拿到 10 影响力 → 小波给自己。");
            AddNote("· 没拿到 → 小波给敌对阵营。");
            AddNote("· 最早在主波后 2 分 30 秒；小波前每死一人提前 5 秒，不低于 2 分钟。");
            AddNote("· 每个主波次只有一个小波；刚认定大波后 " + _log.SmallWaveGuardSeconds
                  + " 秒内的「小波播报」判为误报（OCR 常把同一段读两次）。");

            AddSection("掉血怎么算票", BadColor);
            AddNote("· SCP 血量读的是画面中偏右那块「HP:当前/上限」清单（白字）。");
            AddNote("  紫色是休谟护盾、绿色是 AP，都不参与影响力计算；那块字很小，");
            AddNote("  程序会自动丢掉「当前值超过上限」这类明显错误读数。");
            AddNote("· 打掉上限 10% → 计时 −1s / 影响力 +2，按累计每满 10% 记一次；");
            AddNote("  该 SCP 阵亡时这一条不适用（改拿击杀 SCP 的 −10s / +15）。");
            AddNote("· 归给谁看当时的援军：只有一方 → 全算它；");
            AddNote("  大波之后另一方刷了小波 → 两方平均分；同一方小波 → 仍算它。");

            AddSection("核弹 / 亡者协议", BadColor);
            AddNote("· 最后 10 秒无法取消（Detonation Inevitable）。");
            AddNote("· 两方令牌用尽 → 2 分 30 秒亡者协议（拿到令牌会重置、小波后退 40 秒）；");
            AddNote("  归零则核弹启动且不可取消，同时把血量低于上限 25% 的 SCP 抬到 25%。");
            AddNote("· 核弹引爆后，下一次重生波强制 4 分钟冷却。");
        }

        /// <summary>死亡时识别到的票数行；由 Program 在数据变化后调用刷新。</summary>
        private Row _ticketNote;
        private Row _waveOrderNote;
        private Row _wavePredictNote;

        /// <summary>刷新顺序 + 推测（需求八附）。由 Program 在数据变化后调用。</summary>
        public void SetWaveInfo(string order, string predict)
        {
            try
            {
                SetNote(_waveOrderNote, order);
                SetNote(_wavePredictNote, predict);
                PulseDataChanged();
            }
            catch { }
        }

        /// <summary>
        /// 按模型现状把「刷新顺序 / 后续推测」两行填上。
        ///
        /// 【为什么要面板自己填】这两行的行对象是切到「记录」页时才建的，
        /// 而 OCR 认出刷新的那一刻用户可能停在任意一页（多数时候面板根本没开）。
        /// 只靠外层推送的话，那一波就永远显示不出来。
        /// 这里直接用面板手里的 <see cref="_log"/> 生成和
        /// AlertManager.WaveOrderText / WavePredictText 完全一样的文字，
        /// 所以两条路径（推送 / 自己填）不会打架。
        /// 顺带一个好处：「后续推测」是跟着时间变的，定时刷新会让它一直是最新的。
        /// </summary>
        private void FillWaveInfo()
        {
            if (_waveOrderNote == null && _wavePredictNote == null) return;   // 不在记录页
            try
            {
                DateTime now = DateTime.Now;
                if (_waveOrderNote != null) SetNote(_waveOrderNote, _log.WaveHistoryText(now));
                if (_wavePredictNote != null)
                    SetNote(_wavePredictNote, "推测：" + _log.PredictNextWaves(now));
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "刷新刷新顺序两行失败", ex);
            }
        }

        public void SetTicketSummary(string text)
        {
            try
            {
                if (_ticketNote == null) return;
                SetNote(_ticketNote, string.IsNullOrEmpty(text) ? TicketEmptyHint : text);
                PulseDataChanged();
            }
            catch { }
        }

        /// <summary>
        /// 数据有变动 → 左边那根装饰线闪两下。
        /// 操作者要求：让用户一眼看到"面板数据更新了"（OCR 认到东西 / 票数刷新都会触发）。
        /// </summary>
        public void PulseDataChanged()
        {
            try { FlashDecorLine(); } catch { }
        }

        /// <summary>票数那一行还没有数据时的兜底提示。</summary>
        private const string TicketEmptyHint = "（还没读到：玩家死亡时会自动识别上方那 4 个数字）";

        /// <summary>
        /// 票数那一行的文字，面板自己从数据里拼。
        ///
        /// 【为什么不能只等别人推】这一行也是按页懒建的：死亡那一刻面板要是
        /// 停在别的页、或者根本没唤出来，SetTicketSummary 就没地方写；
        /// 等你切到「记录」页，页面刚建出来还是空的。
        /// 票数就存在 config 里（tok_/inf_ 那几个键），所以面板直接读它就行 ——
        /// 切页、关掉再打开都不会丢。
        /// </summary>
        private string TicketText()
        {
            string t = _cfg == null ? "" : _cfg.TicketSummaryText();
            return string.IsNullOrEmpty(t) ? TicketEmptyHint : t;
        }

        /// <summary>把界面上的值同步进模型，然后刷新所有显示。</summary>
        private void Recalc()
        {
            // 【关键】"输入框 → 模型"这一段只处理当前这一页真的显示着的格子。
            //
            // 面板的行是按页懒建的（GetValue 对不存在的格子返回空串），
            // 以前这里不看有没有格子就直接写回模型，于是切页 / 定时刷新都会
            // 把另外几页的数据从模型里抹掉：
            //   · 不在战况页 → 所有 SCP 的"当期血量"被清成 -1、"上限"被清成 0；
            //   · 不在发电机页 → 正在启动的发电机被当成"没在跑"。
            // 而模型偏偏是整局一直权威的那一份：OCR 读到的东西一旦被抹掉，
            // 下一次读数会被当成"第一次读到"重新起算累计掉血，
            // 结果是影响力记多记少都说不准。
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec == null) continue;

                if (code != "079")
                {
                    if (HasInput("hp_" + code))
                    {
                        double total = Num(GetValue("hp_" + code));
                        int newTotal = total > 0 ? (int)total : 0;
                        bool totalChanged = rec.TotalHp != newTotal;
                        rec.TotalHp = newTotal;

                        // 分母（总血量）变了 → 已发过的段数含义变了，重算一次
                        if (totalChanged) _log.OnTotalHpChanged(rec);
                    }

                    if (HasInput("cur_" + code))
                    {
                        string curRaw = GetValue("cur_" + code);
                        // 清空输入框 = 重新开始算这个 SCP：走 SetCurrentHp(-1) 把状态清掉
                        if (string.IsNullOrEmpty(curRaw)) rec.SetCurrentHp(-1);
                        else rec.SetCurrentHp((int)Num(curRaw));
                    }
                }
            }

            SyncGenerators();

            DateTime now = DateTime.Now;

            SetReadout("lost", _log.TotalDamage.ToString());
            SetReadout("kills", _log.TotalKills.ToString());
            // 这里读的是影响力（按文档的目标表累加），不是旧版"血量÷200+击杀×15"
            SetReadout("total", _log.TotalTickets.ToString());
            SetReadout("mtf", _log.TicketsOf(TacticalLog.MTF).ToString());
            SetReadout("chaos", _log.TicketsOf(TacticalLog.CHAOS).ToString());

            int mtfTok = _log.TokensOf(TacticalLog.MTF);
            int chaosTok = _log.TokensOf(TacticalLog.CHAOS);

            SetReadout("tok_mtf", _log.Tickets.HasUnknownHistory(TacticalLog.MTF) ? "未知" : mtfTok.ToString());
            SetReadout("tok_chaos", _log.Tickets.HasUnknownHistory(TacticalLog.CHAOS) ? "未知" : chaosTok.ToString());
            SetReadout("tok_share", _log.Tickets.BonusTokensLeft + " / "
                + _log.Tickets.MaxBonusTokens + "（两方共享）");
            // 这一行会被拆成两段显示太长，所以只留"还差多少"
            SetReadout("nexttoken", "MTF " + ShortNextToken(TacticalLog.MTF)
                + "　｜　混沌 " + ShortNextToken(TacticalLog.CHAOS));

            // ---- 总览页（不在这一页时这些字段不存在，SetReadout 会安静跳过）----
            RefreshTabTitles();
            SetCard(_ovMtfCard, _log.Tickets.HasUnknownHistory(TacticalLog.MTF) ? "?" : mtfTok.ToString());
            SetCard(_ovChaosCard, _log.Tickets.HasUnknownHistory(TacticalLog.CHAOS) ? "?" : chaosTok.ToString());

            // 发电机那张卡：几台 / 共几台 + 进度条 + 状态小字
            if (_ovGenCard != null)
            {
                _ovGenCard.BarValue = _log.GeneratorsActive;
                _ovGenCard.CardSub = _log.Has079 ? "有 079" : "无 079";
                SetCard(_ovGenCard, _log.GeneratorsActive + " / " + TacticalLog.GeneratorCount);
            }
            if (_ovGenState != null) SetNote(_ovGenState,
                GenOverviewText() + "　·　点右上角齿轮（或这张卡）去「发电机」页");

            // 下一波刷新（预计小波时间）
            SetReadout("ov_next_wave", NextWaveEtaText());
            UpdateLatestWaveNote();
            UpdateBattleWaveRows(now);


            int mtfInf = _log.TicketsOf(TacticalLog.MTF);
            int chaosInf = _log.TicketsOf(TacticalLog.CHAOS);
            SetReadout("ov_mtf_inf", InfluenceText(TacticalLog.MTF, mtfInf));
            SetReadout("ov_chaos_inf", InfluenceText(TacticalLog.CHAOS, chaosInf));
            SetReadout("ov_mtf_next", ShortNextToken(TacticalLog.MTF));
            SetReadout("ov_chaos_next", ShortNextToken(TacticalLog.CHAOS));

            double mw = _log.WaveRemaining(now, TacticalLog.MTF);
            double cw = _log.WaveRemaining(now, TacticalLog.CHAOS);
            string waitingForRound = "等待回合开始";
            SetReadout("ov_mtf_wave", !_log.WaveClockRunning ? waitingForRound
                : TimeEstimator.Clock(mw, true)
                    + (_log.WaveLockedNoToken(TacticalLog.MTF) ? "（无令牌·锁30秒）" : "")
                    + (_log.HasWaveAnchor ? "" : "（未校准）"));
            SetReadout("ov_chaos_wave", !_log.WaveClockRunning ? waitingForRound
                : TimeEstimator.Clock(cw, true)
                    + (_log.WaveLockedNoToken(TacticalLog.CHAOS) ? "（无令牌·锁30秒）" : "")
                    + (_log.HasWaveAnchor ? "" : "（未校准）"));

            if (_ovShare != null)
                SetNote(_ovShare, "共享额外令牌：还能拿 " + _log.Tickets.BonusTokensLeft
                    + " / " + _log.Tickets.MaxBonusTokens + " 个　·　"
                    + (_log.BothOutOfTokens ? "两方都没令牌了（亡者协议会启动）" : "还有令牌可重生"));

            SetReadout("ov_clock", TimeEstimator.Clock(_log.RoundElapsedNow(), false)
                + (_log.HasWaveAnchor ? "" : "（未校准）"));
            SetReadout("ov_killhp", _log.TotalKills + " 个 SCP ／ 已掉 "
                + _log.TotalDamage + " 血");

            // 【刚刚刷新】最近一条刷新记录（哪一方 / 大波还是小波 / 几点）
            if (_log.WaveHistory.Count > 0)
            {
                TacticalLog.WaveRecord wr = _log.WaveHistory[_log.WaveHistory.Count - 1];
                string from = wr.Source == "ocr" ? "识别" : "手动";
                SetReadout("ov_lastwave", TacticalLog.FactionName(wr.Faction)
                    + (wr.IsSmall ? " 小波  " : " 大波  ") + wr.At.ToString("HH:mm:ss")
                    + "（" + from + "）");
            }
            else SetReadout("ov_lastwave", "（本回合还没记录到刷新）");
            if (_ovLast != null) SetNote(_ovLast, _log.Tickets.LastEntryText);

            // ---- 预测：下一步还会不会有重生 ----
            // 玩家真正关心的是"还有几次重生"，所以把概率写成大数字。
            double pMtf = _log.CurrentTokenChance(TacticalLog.MTF);
            double pChaos = _log.CurrentTokenChance(TacticalLog.CHAOS);
            double pAny = _log.AnyCurrentTokenChance();

            _ovMtfCard.CardSub = "有票概率 " + TacticalLog.PercentTextOneDecimal(pMtf);
            _ovChaosCard.CardSub = "有票概率 " + TacticalLog.PercentTextOneDecimal(pChaos);

            // 大牌：全人类合起来的概率
            SetCard(_ovAnyCard, TacticalLog.PercentTextOneDecimal(pAny));

            if (_ovProbNote != null)
                SetNote(_ovProbNote, PredictWhy(now, pMtf, pChaos));

            // ---- 登记页：每行显示"记过几次" ----
            RefreshObjectiveCounts();

            // 刷新计时一直在自己往前走，没校准过只是加个标注
            SetReadout("wave", !_log.WaveClockRunning ? waitingForRound
                : TimeEstimator.Clock(_log.WaveRemaining(now), true)
                    + (_log.HasWaveAnchor ? "" : "（未校准）")
                    + (_log.WaveLockedNoToken(TacticalLog.MTF) ? "　MTF 无令牌·锁30秒" : ""));
            SetReadout("wave_mtf", !_log.WaveClockRunning ? waitingForRound
                : TimeEstimator.Clock(mw, true)
                    + (_log.WaveLockedNoToken(TacticalLog.MTF) ? "（锁 30 秒）" : ""));
            SetReadout("wave_chaos", !_log.WaveClockRunning ? waitingForRound
                : TimeEstimator.Clock(cw, true)
                    + (_log.WaveLockedNoToken(TacticalLog.CHAOS) ? "（锁 30 秒）" : ""));
            SetReadout("interval", _log.EffectiveInterval + " 秒"
                + (_log.LastWavePlayers > 0 ? "（含重生 " + _log.LastWavePlayers + " 人 +"
                   + (_log.LastWavePlayers * _log.PerPlayerSeconds) + "s）" : ""));

            if (_recentNote != null) SetNote(_recentNote, _log.Tickets.LastEntryText);
            if (_timelineNote != null) SetNote(_timelineNote, BuildTimeline());

            // ---- 校准页：右下角那一段"当前估算"每次刷新都取最新的 ----
            if (_calState != null)
                SetNote(_calState, TimeCalibrateState != null ? TimeCalibrateState() : "");

            // ---- 记录页：票数 / 刷新顺序 / 后续推测 ----
            // 这几行都是"按页懒建"的，数据却一直活在模型 / config 里，
            // 所以每次 Recalc（切页 / 定时 / 模型变化都会走到）都按数据现状填一遍。
            // 票数只在真的读到过时才覆盖（不然会把兜底提示也冲掉）。
            if (_ticketNote != null && _cfg != null)
            {
                string tk = _cfg.TicketSummaryText();
                if (!string.IsNullOrEmpty(tk)) SetNote(_ticketNote, tk);
            }
            FillWaveInfo();

            // ---- SCP 在场标记 ----
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;
                TacticalLog.ScpRecord rec = _log.Find(code);
                bool has = rec != null && rec.Present;
                SetCell("pre_" + code, has ? "在场" : "—",
                        has ? Color.FromArgb(150, 235, 170) : Color.FromArgb(120, 126, 138));
            }

            TacticalLog.ScpRecord r079 = _log.Find("079");
            bool has079 = r079 != null && r079.Present;
            SetCell("has079", has079 ? "有" : "没有",
                    has079 ? Color.FromArgb(150, 235, 170) : Color.FromArgb(120, 126, 138));

            // ---- 发电机 ----
            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                string k = (g + 1).ToString();
                TacticalLog.Generator gen = _log.Generators[g];

                SetCell("genfac_" + k, TacticalLog.FactionName(gen.Faction),
                        gen.Faction == TacticalLog.NONE
                            ? Color.FromArgb(120, 126, 138)
                            : Color.FromArgb(150, 210, 255));

                if (gen.Done)
                {
                    // 播报确认过的才算"真的启动完成"；只是倒计时走完的写清楚，
                    // 免得把本地估计当成游戏确认（口径：一切以播报为准）。
                    if (gen.Confirmed)
                        SetCell("genrun_" + k, "已启动", Color.FromArgb(150, 235, 170));
                    else
                        SetCell("genrun_" + k, "时间到·等播报", Color.FromArgb(255, 190, 90));
                    SetValue("gen_" + k, "0");
                }
                else if (gen.Running)
                {
                    double left = gen.Remaining(DateTime.Now);
                    if (left < 0) left = 0;
                    int leftSec = (int)Math.Ceiling(left);

                    // 快启动的时候把状态格子弄成醒目的橙色
                    bool soon = left > 0 && left <= TacticalLog.GenSoonSeconds;
                    SetCell("genrun_" + k,
                            (soon ? "快好了 " : "启动中 ") + leftSec + "s",
                            soon ? Color.FromArgb(255, 190, 90) : Color.FromArgb(150, 210, 255));

                    // 把"剩余"那一格也刷成实时值：
                    //   ① 面板上就能看到秒数在走（以前只有"启动中"三个字，看不到倒计时）；
                    //   ② 关掉面板时 Save() 存的是真实剩余，
                    //      下次打开不会又变回 120（以前存的是你当初填的那个数字）。
                    // 用户正在这一格打字时不要抢（见 HasFocus）。
                    if (!HasFocus("gen_" + k))
                        SetValue("gen_" + k, leftSec.ToString());
                }
                else
                {
                    SetCell("genrun_" + k, "未启动", Color.FromArgb(120, 126, 138));
                }
            }

            // 状态行：把"倒计时估的"和"播报确认的"分开写，一眼能看出还差几台没确认
            int estOnly = 0;
            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                TacticalLog.Generator gg = _log.Generators[g];
                if (gg.Done && !gg.Confirmed) estOnly++;
            }

            string st = _log.GeneratorsActive + " / " + TacticalLog.GeneratorCount + " 台已启动";
            if (estOnly > 0)
                st += "（其中 " + estOnly + " 台只是倒计时到了，等播报确认）";
            if (_log.GeneratorsActive > 0 && _log.GeneratorsActive < TacticalLog.GeneratorCount
                && _log.GeneratorsRunning > 0)
                st += "，还有 " + _log.GeneratorsRunning + " 台在启动中";
            SetReadout("genstate", st);

            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec == null) continue;

                Color fc = rec.Faction == TacticalLog.MTF ? GoodColor
                         : rec.Faction == TacticalLog.CHAOS ? WarnColor
                         : DimColor;
                SetCell("fac_" + code, TacticalLog.FactionName(rec.Faction), fc);
                SetCell("kill_" + code, rec.Killed ? "已阵亡" : "存活",
                        rec.Killed ? BadColor : DimColor);
            }

            SetCell("bigside", TacticalLog.FactionName(_bigWaveSide),
                    _bigWaveSide == TacticalLog.MTF ? GoodColor : WarnColor);

            // 直接写引用，不用下标 —— 布局重排也不会写错行
            if (_waveNote1 != null) _waveNote1.Label = PredictSmallWaveText(now);
            if (_waveNote2 != null) _waveNote2.Label = _log.Tickets.Summary();
        }

        /// <summary>
        /// "距离下一个令牌还差多少影响力"。按文档，阈值是 40/80/150/200，
        /// 拿满四个（或两方共享的 3 个额外额度用完）就没有下一个了。
        /// </summary>
        private string NextTokenText(string faction)
        {
            string name = faction == TacticalLog.MTF ? "MTF" : "混沌";
            int need = _log.Tickets.ToNextToken(faction);
            if (need < 0) return name + "：本回合令牌已拿满";

            int th = _log.Tickets.NextTokenThreshold(faction);
            return name + "：还差 " + need + " 影响力（到 " + th + " 再得 1 个令牌）";
        }

        /// <summary>
        /// 影响力时间线：最近几条登记过的目标，带时间。
        /// 一眼能看出"这两分钟里谁在拿影响力"（小波判定就看这个）。
        /// </summary>
        private string BuildTimeline()
        {
            List<TicketModel.LogEntry> list = _log.Tickets.Entries;
            if (list.Count == 0) return "（本回合还没有登记过目标）";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int from = list.Count - 6;
            if (from < 0) from = 0;
            for (int i = from; i < list.Count; i++)
            {
                TicketModel.LogEntry e = list[i];
                sb.Append(e.At.ToString("HH:mm:ss")).Append("  ")
                  .Append(TicketModel.FactionName(e.Faction)).Append("  ")
                  .Append(e.What);
                if (e.Influence > 0) sb.Append("  +").Append(e.Influence);
                if (e.Seconds > 0) sb.Append("  −").Append(e.Seconds).Append("s");
                sb.Append("　");
            }
            return sb.ToString();
        }

        /// <summary>总览页那行"距下一个令牌"要短，不然一行放不下。</summary>
        private string ShortNextToken(string faction)
        {
            int need = _log.Tickets.ToNextToken(faction);
            if (need < 0) return "已拿满";
            return "还差 " + need + " 影响力";
        }

        /// <summary>
        /// 和游戏 HUD 一样的写法："49 / 80" —— 当前影响力 / 下一个令牌阈值。
        /// 拿满时只显示影响力。
        /// </summary>
        private string InfluenceText(string faction, int inf)
        {
            int th = _log.Tickets.NextTokenThreshold(faction);
            if (th < 0) return inf + "（已拿满）";
            return inf + " / " + th;
        }

        /// <summary>把卡片右侧的说明换掉（卡片行不在当前页时安静跳过）。</summary>
        /// <summary>
        /// 预测数字的依据说明（放在概率卡片下面）。
        ///
        /// 只写"为什么是这个数"，让用户能自己判断可不可信 ——
        /// 一个没有依据的百分比是不敢信的。
        /// </summary>
        private string PredictWhy(DateTime now, double pMtf, double pChaos)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append("当前存量按影响力奖励和已记录主波估算；中途加入时历史消耗未知，按可能存量给概率。");
            sb.Append(" HUD 大票只在死亡时校准，之后继续按新影响力奖励与主波更新。");

            return sb.ToString();
        }

        private void SetCard(Row r, string bigValue)
        {
            if (r == null) return;
            r.Value = bigValue ?? "";
        }

        // ============================================================
        // 登记页：把每种目标点过几次显示在格子上
        // ============================================================

        /// <summary>目标格子的 key → (目标类型, 阵营)。阵营为空 = 两边都算。</summary>
        private struct ObjSpec
        {
            public TicketModel.Objective Kind;
            public string Faction;
            public ObjSpec(TicketModel.Objective k, string f) { Kind = k; Faction = f; }
        }

        /// <summary>每个格子对应哪个目标。</summary>
        private static readonly Dictionary<string, ObjSpec> ObjCells =
            new Dictionary<string, ObjSpec>();

        static TacticalPanel()
        {
            ObjCells["obj_mtf_kill"] = new ObjSpec(TicketModel.Objective.KillScp, TacticalLog.MTF);
            ObjCells["obj_mtf_gen"] = new ObjSpec(TicketModel.Objective.ActivateGenerator, TacticalLog.MTF);
            ObjCells["obj_mtf_sci"] = new ObjSpec(TicketModel.Objective.MtfScientistEscape, TacticalLog.MTF);
            ObjCells["obj_mtf_dclass"] = new ObjSpec(TicketModel.Objective.MtfBondedDEscape, TacticalLog.MTF);

            ObjCells["obj_chaos_kill"] = new ObjSpec(TicketModel.Objective.KillScp, TacticalLog.CHAOS);
            ObjCells["obj_chaos_gen"] = new ObjSpec(TicketModel.Objective.ActivateGenerator, TacticalLog.CHAOS);
            ObjCells["obj_chaos_civ"] = new ObjSpec(TicketModel.Objective.ChaosCivilianEscape, TacticalLog.CHAOS);

            ObjCells["obj_armed"] = new ObjSpec(TicketModel.Objective.KillArmed, "");
            ObjCells["obj_cons"] = new ObjSpec(TicketModel.Objective.ConsumableScpItem, "");
            ObjCells["obj_uniq"] = new ObjSpec(TicketModel.Objective.UniqueScpItem, "");
            ObjCells["obj_dmg10"] = new ObjSpec(TicketModel.Objective.DamageScp10, "");
        }

        /// <summary>手动登记过的次数（key → 次数）。只用来显示，真实数据在影响力里。</summary>
        private readonly Dictionary<string, int> _manualCounts = new Dictionary<string, int>();

        /// <summary>把格子的显示刷新成"记过几次"。</summary>
        private void RefreshObjectiveCounts()
        {
            foreach (KeyValuePair<string, ObjSpec> kv in ObjCells)
            {
                int n;
                _manualCounts.TryGetValue(kv.Key, out n);
                Color c = n > 0 ? ObjectiveColor(kv.Value.Kind) : Color.FromArgb(120, 126, 138);
                SetCell(kv.Key, n > 0 ? ("×" + n) : "点此登记", c);
            }
        }

        /// <summary>按目标性质给个颜色：专属目标染阵营色，通用的是黄色。</summary>
        private static Color ObjectiveColor(TicketModel.Objective kind)
        {
            if (kind == TicketModel.Objective.MtfScientistEscape
                || kind == TicketModel.Objective.MtfBondedDEscape) return MtfColor;
            if (kind == TicketModel.Objective.ChaosCivilianEscape) return ChaosColor;
            if (kind == TicketModel.Objective.KillScp) return BadColor;
            return WarnColor2;
        }

        /// <summary>点格子 = 登记一次这个目标。</summary>
        private void RecordObjective(string key)
        {
            ObjSpec spec;
            if (!ObjCells.TryGetValue(key, out spec)) return;

            // 通用目标（阵营为空）默认算 MTF —— 影响力是分阵营记的，
            // 总得归给一边；真要知道是哪边打的，用专属格子。
            string faction = string.IsNullOrEmpty(spec.Faction) ? TacticalLog.MTF : spec.Faction;

            int gained = _log.Tickets.Record(spec.Kind, faction, _log.Has079,
                TicketModel.Info(spec.Kind).Name);
            int n;
            _manualCounts.TryGetValue(key, out n);
            _manualCounts[key] = n + 1;

            Logger.On("战术", "手动登记：" + TicketModel.FactionName(faction) + " "
                + TicketModel.Info(spec.Kind).Name + " → 影响力 +" + gained);

            Recalc();
            RefreshFromModelSafe();
            Redraw();
            Save();
        }

        /// <summary>撤销最后一次登记（只回退影响力 / 令牌，不动别的数据）。</summary>
        private void UndoLastObjective()
        {
            if (!_log.Tickets.UndoLast()) return;
            // 次数也退回一格
            foreach (KeyValuePair<string, ObjSpec> kv in ObjCells)
            {
                int n;
                if (!_manualCounts.TryGetValue(kv.Key, out n) || n <= 0) continue;
                _manualCounts[kv.Key] = n - 1;
                break;
            }
            Recalc();
            Redraw();
            Save();
        }

        /// <summary>Recalc + 重绘的安全包装（OCR 那边偶尔会并发调）。</summary>
        private void RefreshFromModelSafe()
        {
            try { RefreshFromModel(); } catch { }
        }

        /// <summary>大波/小波那两行说明的引用（布局可能变，所以不用下标）。</summary>
        private Row _waveNote1, _waveNote2;


        private string PredictSmallWaveText(DateTime now)
        {
            if (!_log.HasBigWave)
                return "还没有校准过大波 —— 看到大波刷出来时按一下 "
                     + KeyName(_cfg.HotkeyBigWave, "F11") + " 记录一次";

            double elapsed = (now - _log.BigWaveAt).TotalSeconds;
            if (elapsed < _log.BigWaveWindow)
            {
                double left = _log.BigWaveWindow - elapsed;
                return "大波已过 " + TimeEstimator.Clock(elapsed, false)
                     + "，再过 " + TimeEstimator.Clock(left, true) + " 判定小波归属";
            }

            string f = _log.DecideSmallWave(now);
            if (f == TacticalLog.NONE)
                return "小波：没填血量或阵营，判断不了是哪一方";
            return "小波将刷：" + TacticalLog.FactionName(f) + " 方";
        }

        /// <summary>把三台发电机的界面值同步进模型，并推进计时。</summary>
        private void SyncGenerators()
        {
            DateTime now = DateTime.Now;
            _log.TickGenerators(now);   // 模型自己走表（倒计时到点 → "时间到·等播报"）

            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                string k = (g + 1).ToString();
                TacticalLog.Generator gen = _log.Generators[g];

                // 不在发电机页 → 这一页没有发电机的格子，
                // 绝不能拿"空值"去改模型（那会把正在启动的发电机停掉）。
                // 模型那边由 AlertManager 的定时器持续推进，不依赖面板。
                if (!HasInput("gen_" + k)) continue;

                if (gen.Done) continue;   // 已经算完成的，不动它

                double baseSec = Num(GetValue("genbase_" + k));
                if (baseSec <= 0) baseSec = 120;
                gen.Duration = (int)baseSec;

                // 【为什么不再"拿格子里的值去和模型对比"】
                // 格子里显示的就是模型自己写上去的实时剩余。拿它对比，
                // 就变成一轮一轮把倒计时重新起算 —— 秒数永远走不到 0。
                // 起算点只在两个时机落定：
                //   ① 用户按了回车（ConfirmGenerators）；
                //   ② 用户改完直接点到别处去了（这里补一刀，别让改动白填）。
                if (_genPending[g])
                {
                    if (HasFocus("gen_" + k)) continue;    // 还在输入，别抢
                    _genPending[g] = false;
                    AdoptGeneratorInput(g);
                    continue;
                }

                // 用户把「剩余」清空了 = 把拉杆拉回去
                if (gen.Running && string.IsNullOrEmpty(GetValue("gen_" + k)))
                {
                    gen.Running = false;
                    gen.ManualRemaining = -1;
                }
            }

            _log.TickGenerators(now);
        }

        protected override void OnEdited()
        {
            // 用户正在改发电机剩余秒数 → 先记下来，等回车确认再起算。
            // （以前是敲第一个数字就开始跑，数字还没填完就先启动了一次）
            string fk = FocusedKey;
            if (fk != null && fk.StartsWith("gen_"))
            {
                int idx;
                if (int.TryParse(fk.Substring(4), out idx)
                    && idx >= 1 && idx <= _genPending.Length)
                    _genPending[idx - 1] = true;
            }

            Recalc();
            ScheduleSave();   // 改完自动存，不用等回车
        }

        protected override void OnCell(string key)
        {
            if (key.StartsWith("fac_"))
            {
                TacticalLog.ScpRecord rec = _log.Find(key.Substring(4));
                if (rec == null) return;
                rec.Faction = NextFaction(rec.Faction);
                if (rec.IsTouched && rec.ChangedAt == DateTime.MinValue) rec.ChangedAt = DateTime.Now;
            }
            else if (key.StartsWith("kill_"))
            {
                TacticalLog.ScpRecord rec = _log.Find(key.Substring(5));
                if (rec == null) return;
                rec.Killed = !rec.Killed;
                if (rec.Killed)
                {
                    rec.ChangedAt = DateTime.Now;
                    if (rec.Faction == TacticalLog.NONE) rec.Faction = _bigWaveSide;
                    if (ScpKilled != null) ScpKilled(rec.Code);
                }
                else rec.ChangedAt = DateTime.MinValue;
            }
            else if (key.StartsWith("pre_"))
            {
                TacticalLog.ScpRecord rec = _log.Find(key.Substring(4));
                if (rec == null) return;
                rec.Present = !rec.Present;
            }
            else if (key.StartsWith("genfac_"))
            {
                int gi = GenIndex(key.Substring(7));
                if (gi >= 0) _log.Generators[gi].Faction = NextFaction(_log.Generators[gi].Faction);
            }
            else if (key.StartsWith("genrun_"))
            {
                int gi = GenIndex(key.Substring(7));
                if (gi >= 0)
                {
                    TacticalLog.Generator gen = _log.Generators[gi];
                    if (!gen.Done)
                    {
                        if (gen.Running)
                        {
                            // 再点一次 = 把拉杆拉回去，停止启动
                            gen.Running = false;
                            gen.ManualRemaining = -1;
                            SetValue("gen_" + (gi + 1), "");
                        }
                        else
                        {
                            double d = Num(GetValue("genbase_" + (gi + 1)));
                            if (d <= 0) d = 120;
                            gen.Running = true;
                            gen.StartedAt = DateTime.Now;
                            gen.Duration = (int)d;
                            gen.ManualRemaining = (int)d;
                            if (gen.Faction == TacticalLog.NONE) gen.Faction = _bigWaveSide;
                            SetValue("gen_" + (gi + 1), ((int)d).ToString());
                        }
                    }
                }
            }
            else if (key == "has079")
            {
                TacticalLog.ScpRecord r79 = _log.Find("079");
                if (r79 != null) r79.Present = !r79.Present;
            }
            else if (key == "bigside")
            {
                _bigWaveSide = (_bigWaveSide == TacticalLog.MTF) ? TacticalLog.CHAOS : TacticalLog.MTF;
            }
            else if (key != null && key.StartsWith("pend_"))
            {
                // 「总览」页：点一下把某条待归属的阵亡定给一方
                // key 形如 pend_096_MTF / pend_096_CHAOS
                int cut = key.LastIndexOf('_');
                if (cut > 5)
                {
                    string code = key.Substring(5, cut - 5);
                    string fac = key.Substring(cut + 1);
                    if (fac != TacticalLog.MTF && fac != TacticalLog.CHAOS) fac = null;
                    if (fac != null)
                    {
                        // 记账成功后「记录」页的"最近登记的目标 / 影响力时间线"会自己出现这一笔
                        //（ResolvePendingKill → RecordAt → 时间线），这里不用另做反馈。
                        _log.ResolvePendingKill(code, fac, DateTime.Now, _log.Has079);
                    }
                }
            }

            // ---- 「登记」页：点一下格子 = 登记一次这个目标 ----
            if (key != null && key.StartsWith("obj_"))
            {
                RecordObjective(key);
                return;
            }

            Recalc();
            Redraw();
            ScheduleSave();   // 改完自动存
        }

        /// <summary>
        /// 总览页「下一波刷新（预计）」那一行的值（操作者要求：总览页也要能看到预计小波刷新时间）。
        ///
        /// 口径和「支援刷新」那条倒计时完全一致（别的地方改了这里也要跟着改）：
        ///   · 大波之后小波还没记过 → 小波，时间 = MiniWaveDelay(150) + 进场延迟(60) − 已过秒数；
        ///   · 小波已经刷过 / 还没认出大波 → 退回显示下一波大波（两方里更早的那个）。
        /// </summary>
        private string NextWaveEtaText()
        {
            try
            {
                if (!_log.WaveClockRunning) return "等待回合开始";
                DateTime now = DateTime.Now;
                if (!_log.HasBigWave) return "还没认出大波（认出后才好估）";

                double mini = _log.MiniWaveRemaining(now);
                if (mini > 0)
                {
                    string f = _log.DecideSmallWave(now);
                    string who = (f == TacticalLog.NONE || f == null) ? "" : TacticalLog.FactionName(f);
                    return (who.Length > 0 ? who + " 小波" : "小波")
                         + " 约 " + TimeEstimator.Clock(mini, true) + " 后";
                }

                double mtf = _log.WaveRemaining(now, TacticalLog.MTF);
                double chaos = _log.WaveRemaining(now, TacticalLog.CHAOS);
                double big = (mtf > 0 && chaos > 0) ? Math.Min(mtf, chaos) : Math.Max(mtf, chaos);
                if (big <= 0) return "大波 随时可能（等刷新播报）";
                return "大波 约 " + TimeEstimator.Clock(big, true) + " 后";
            }
            catch { return "—"; }
        }

        /// <summary>总览页上发电机那张卡的小字状态（几台在跑 / 还差几台）。</summary>
        private string GenOverviewText()
        {
            if (!_log.Has079) return "本回合没有 079 → 发电机不减刷新计时";
            if (_log.AllGeneratorsReady) return "三台都启动了 —— 去 079 收容室按按钮";
            if (_log.GeneratorsRunning > 0)
                return _log.GeneratorsActive + " 台已启动，还有 " + _log.GeneratorsRunning + " 台在启动中";
            if (_log.GeneratorsActive > 0)
                return _log.GeneratorsActive + " 台已启动，还差 " + (TacticalLog.GeneratorCount - _log.GeneratorsActive) + " 台";
            return "一台都还没启动（点上面的齿轮去「发电机」页填/校准）";
        }

        /// <summary>"genfac_2" 这类键里取出下标（0 起）。</summary>
        private static int GenIndex(string s)        {
            int n;
            if (!int.TryParse(s, out n)) return -1;
            n -= 1;
            return (n >= 0 && n < TacticalLog.GeneratorCount) ? n : -1;
        }

        private static string NextFaction(string f)
        {
            if (f == TacticalLog.NONE) return TacticalLog.MTF;
            if (f == TacticalLog.MTF) return TacticalLog.CHAOS;
            return TacticalLog.NONE;
        }

        protected override void OnButton(string key)
        {
            int waveIndex;
            if (TryParseWaveButton(key, "wave_type_", out waveIndex))
            {
                if (_log.ToggleWaveType(waveIndex))
                {
                    Logger.On("战术", "手动切换第 " + (waveIndex + 1) + " 条波次大小；已更新令牌与后续波次计算");
                    Recalc(); Redraw(); Save();
                }
                return;
            }
            if (TryParseWaveButton(key, "wave_delete_", out waveIndex))
            {
                if (_log.RemoveWave(waveIndex))
                {
                    Logger.On("战术", "手动删除第 " + (waveIndex + 1) + " 条波次记录；已更新令牌与后续波次计算");
                    Recalc(); Redraw(); Save();
                }
                return;
            }
            if (TryParseWaveButton(key, "wave_faction_", out waveIndex))
            {
                if (_log.ToggleWaveFaction(waveIndex))
                {
                    Logger.On("战术", "手动切换第 " + (waveIndex + 1) + " 条波次阵营；已更新令牌与后续波次计算");
                    Recalc(); Redraw(); Save();
                }
                return;
            }
            switch (key)
            {
                case "clearall":
                    _log.ResetRound();
                    for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                        SetValue("cur_" + TacticalLog.ScpCodes[i], "");
                    _bigWaveSide = TacticalLog.MTF;
                    _manualCounts.Clear();
                    Recalc();
                    Redraw();
                    return;

                // ---- 校准页 ----
                case "cal_apply":
                    ApplyCalibration();
                    return;

                // ---- 总览页 ----
                case "ov_wave":
                    MarkBigWaveNow();
                    return;

                // 发电机那张卡上的齿轮（或点整张卡）→ 跳到「发电机」页
                case "ov_gen_gear":
                    GoTab(3);
                    Redraw();
                    return;

                case "ov_reset":
                    _log.ResetRound();
                    _manualCounts.Clear();
                    SetTicketSummary("");
                    Recalc();
                    Redraw();
                    Save();
                    return;

                case "wave_reclassify":
                    if (_log.ReclassifyLatestWave())
                    {
                        Logger.On("战术", "手动切换最近一波类型；已更新令牌消耗与后续波次计算");
                        Recalc();
                        Redraw();
                        Save();
                    }
                    return;

                case "stop_special":
                    if (StopSpecialCountdowns != null) StopSpecialCountdowns();
                    return;

                // ---- 记录页 ----
                case "undo_obj":
                    UndoLastObjective();
                    return;
            }

            // 登记页那些格子的 key 也可能从按钮通道过来（不同版本）
            if (key != null && key.StartsWith("obj_"))
            {
                RecordObjective(key);
                return;
            }
        }

        /// <summary>
        /// 按快捷键 / 总览按钮记录“刚刷了一波”，并按上一波顺延大小波：
        ///   · 上一波是大波、这一波小波还没记过 → 记小波（阵营按影响力判定，判不出才用面板选的那一方）；
        ///   · 上一波是小波 / 小波已经记过 / 已经超过小波窗口 → 记大波。
        /// 这样不用再手动区分，点同一个按钮就能“大波 → 小波 → 大波”顺着记下去。
        /// </summary>
        public void MarkBigWaveNow()
        {
            DateTime now = DateTime.Now;
            bool small = _log.GuessIsSmallWave(now);
            string faction = _bigWaveSide;
            if (small)
            {
                string decided = _log.DecideSmallWave(now);
                if (decided == TacticalLog.MTF || decided == TacticalLog.CHAOS) faction = decided;
            }

            if (!_log.MarkWave(now, faction, small, "manual"))
            {
                Logger.On("战术", "手动刷新校准被忽略：距上一波太近，按重复读数处理");
                return;
            }

            Logger.On("战术", "手动校准" + (small ? "小波：" : "大波：")
                + TacticalLog.FactionName(faction) + "（按上一次刷新顺延）");
            Recalc();
            if (Visible) Redraw();
            Save();
        }

        /// <summary>
        /// 外部（屏幕 OCR）改了战术数据之后，把界面刷一遍。
        ///
        /// 【防死循环】这里只做单向同步：模型 → 控件 → 重绘。
        /// SetValue / SetCell / SetReadout 都只是改字段，不会触发 OnEdited，
        /// 所以不可能和用户编辑互相递归；再加一个重入标志兜底。
        ///
        /// 【为什么要把模型刷回输入框】
        /// Recalc() 是“把输入框的值读进模型”的，不先把模型写回输入框的话，
        /// 紧接着的 Recalc 会用旧值把 OCR 刚填好的数据覆盖掉。
        /// 发电机的剩余秒数也一样：SyncGenerators 把“和当前值差 &gt; 1”当成
        /// 用户改过而重新起算，所以必须把剩余秒数一起刷回去。
        /// </summary>
        private bool _refreshingFromModel;

        public void RefreshFromModel()
        {
            if (_refreshingFromModel) return;
            _refreshingFromModel = true;
            try
            {
                if (RowCount == 0) return;   // 面板还没建起来，模型就是权威数据

                DateTime now = DateTime.Now;

                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    string code = TacticalLog.ScpCodes[i];
                    TacticalLog.ScpRecord rec = _log.Find(code);
                    if (rec == null) continue;

                    // 【重要】正在被用户编辑的那一格不要写！
                    // 以前 OCR 每次刷新（1.5 秒一次）都会把输入框整个覆盖成模型值，
                    // 结果刚敲下的数字立刻被清掉 —— 表现就是"0 输入不了"（0 是最先被清掉的）。
                    if (!HasFocus("hp_" + code) && rec.TotalHp > 0)
                        SetValue("hp_" + code, rec.TotalHp.ToString());
                    if (!HasFocus("cur_" + code))
                        SetValue("cur_" + code, rec.CurrentHp >= 0 ? rec.CurrentHp.ToString() : "");
                }

                for (int g = 0; g < TacticalLog.GeneratorCount; g++)
                {
                    TacticalLog.Generator gen = _log.Generators[g];
                    string k = (g + 1).ToString();
                    if (HasFocus("gen_" + k)) continue;   // 同上：别抢用户正在输入的那一格
                    if (gen.Done) SetValue("gen_" + k, "0");
                    else if (gen.Running) SetValue("gen_" + k, ((int)gen.Remaining(now)).ToString());
                }

                // OCR 认出来的刷新型别跟着模型走，这样之后手动按 F11 也不会标错方
                if (_log.HasBigWave && _log.BigWaveFaction != TacticalLog.NONE)
                    _bigWaveSide = _log.BigWaveFaction;

                Recalc();
                if (Visible && !IsHoloAnimating && !IsTabAnimating) Redraw();
                // 数据有变动（OCR 认到东西 / 票数刷新）→ 左边装饰线闪一下提示用户
                if (Visible && !IsTabAnimating) PulseDataChanged();
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "OCR 刷新战术面板失败", ex);
            }
            finally
            {
                _refreshingFromModel = false;
            }
        }

        /// <summary>把面板上的数字写回配置（回车 / ESC 关闭时调用）。</summary>
        private void Save()
        {
            if (_cfg == null) return;

            // 【同一个坑的另一半】这里以前也是无脑写：切页时 FlushInputs 会调它，
            // 而 GetValue 对"这一页没有的格子"返回空串，SetTactical 收到空串就删键。
            // 于是 OCR 刚写进 config 的 cur_<编号> / gen_<编号>，会因为你切了一下页
            // 就被从 config 里删掉（重启后全没了），而 config 正是这些值的落盘处。
            // 只写"这一页真的有这一格"的值。
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;
                if (HasInput("hp_" + code)) _cfg.SetTactical("hp_" + code, GetValue("hp_" + code));
                if (HasInput("cur_" + code)) _cfg.SetTactical("cur_" + code, GetValue("cur_" + code));
            }
            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                string k = (g + 1).ToString();
                if (HasInput("gen_" + k)) _cfg.SetTactical("gen_" + k, GetValue("gen_" + k));
                if (HasInput("genbase_" + k)) _cfg.SetTactical("genbase_" + k, GetValue("genbase_" + k));
            }
            // 在场标记也存一下，程序重启后不用重新标
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                TacticalLog.ScpRecord r = _log.Find(TacticalLog.ScpCodes[i]);
                if (r == null) continue;
                _cfg.SetTactical("pre_" + TacticalLog.ScpCodes[i], r.Present ? "1" : "");
            }
            if (_onSaved != null) _onSaved();
        }

        public void OpenPanel()
        {
            RememberForeground();
            ClearRows();
            BuildRows();
            Recalc();
            ShowPanel();
        }

        public void OpenOverviewPanel()
        {
            if (CurrentTab != 0) GoTab(0);
            OpenPanel();
        }

        private void UpdateLatestWaveNote()
        {
            if (_battleLatestWaveNote == null) return;
            if (_log.WaveHistory.Count == 0)
            {
                SetNote(_battleLatestWaveNote, "最近记录：暂无波次");
                return;
            }
            TacticalLog.WaveRecord wave = _log.WaveHistory[_log.WaveHistory.Count - 1];
            SetNote(_battleLatestWaveNote, "最近记录：" + TacticalLog.FactionName(wave.Faction)
                + (wave.IsSmall ? " 小波 " : " 大波 ") + wave.At.ToString("HH:mm:ss")
                + "（下方可编辑完整波次记录）");
        }

        private static bool TryParseWaveButton(string key, string prefix, out int index)
        {
            index = -1;
            return key != null && key.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(key.Substring(prefix.Length), out index);
        }

        private void UpdateBattleWaveRows(DateTime now)
        {
            int count = _log.WaveHistory.Count;
            int first = Math.Max(0, count - BattleWaveRows);
            for (int slot = 0; slot < BattleWaveRows; slot++)
            {
                string key = "wave_type_" + slot;
                int index = first + slot;
                if (slot >= count)
                {
                    SetRowHidden(key, true);
                    continue;
                }
                TacticalLog.WaveRecord wave = _log.WaveHistory[index];
                string label = TacticalLog.FactionName(wave.Faction) + "  "
                    + (wave.IsSmall ? "小波" : "大波") + "  " + WaveAgeText(now - wave.At);
                Color color = wave.Faction == TacticalLog.CHAOS ? ChaosColor : MtfColor;
                SetButtonStripItem(key, label, color);
                // 数据结构最多保留 12 条；slot 和索引一致。
                SetRowHidden(key, false);
            }
        }

        private static string WaveAgeText(TimeSpan age)
        {
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age.TotalHours >= 1)
                return ((int)age.TotalHours) + "小时" + age.Minutes + "分前";
            if (age.TotalMinutes >= 1)
                return ((int)age.TotalMinutes) + "分" + age.Seconds.ToString("00") + "秒前";
            return Math.Max(0, (int)age.TotalSeconds) + "秒前";
        }
    }
}
