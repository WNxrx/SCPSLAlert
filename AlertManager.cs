using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 把日志 / 进程探测的结果映射到各个提示窗口：
    /// 弹出（带动画）→ 停留 → 自动缩回。
    ///
    /// 重要：所有提示都只在“对应事件真的发生”时才弹出来，没有事件时屏幕上不会有东西。
    /// 倒计时类提示在倒计时期间一直显示；接近结束时按配置高亮提醒。
    /// </summary>
    internal class AlertManager : IDisposable
    {
        /// <summary>一个倒计时的运行状态。</summary>
        private class Countdown
        {
            public bool Counting;
            public DateTime End;
            public bool Ended;
            public DateTime EndUntil;
            public DateTime PulseStart = DateTime.MinValue;
            public DateTime PulseUntil = DateTime.MinValue;
            public double PausedRemaining;
            public bool Paused;
            public double Total;          // 本轮起表时的总秒数（新版提示画进度条用）
            public readonly List<int> Fired = new List<int>();

            /// <summary>已经放过音频的阈值（和 Fired 分开，两者可以用不同的秒数）。</summary>
            public readonly List<int> AudioFired = new List<int>();

            /// <summary>开始一轮新的倒计时。</summary>
            public void Begin(DateTime now, int seconds)
            {
                Counting = true;
                Ended = false;
                End = now.AddSeconds(seconds);
                Total = seconds;
                PulseStart = DateTime.MinValue;
                PulseUntil = DateTime.MinValue;
                Paused = false;
                PausedRemaining = 0;
                Fired.Clear();
                AudioFired.Clear();
            }

            public void Stop()
            {
                Counting = false;
                Ended = false;
                PulseUntil = DateTime.MinValue;
                Paused = false;
                PausedRemaining = 0;
            }

            public void Pause(DateTime now)
            {
                if (Paused) return;
                PausedRemaining = Counting ? Math.Max(0, (End - now).TotalSeconds) : 0;
                Paused = true;
            }

            public void Resume(DateTime now)
            {
                if (!Paused) return;
                if (Counting) End = now.AddSeconds(PausedRemaining);
                Paused = false;
                PausedRemaining = 0;
            }
        }

        private Config _cfg;
        private readonly string _configPath;
        private LogMonitor _monitor;
        private readonly Timer _tick;
        private IAlertSurface[] _forms;
        private FloatingLayoutManager _layout;
        private int _surfaceTheme = int.MinValue;
        private readonly Countdown[] _cd;
        private readonly GameWatcher _game;

        /// <summary>本局的时间估算表（净化 / 支援 / 核弹都靠它）。</summary>
        private readonly TimeEstimator _est = new TimeEstimator();

        /// <summary>本局的战术记录（票数 / 阵营刷新 / 大波小波）。</summary>
        private readonly TacticalLog _tactical = new TacticalLog();

        // ---- 第 4 批：阵营识别 + 角色时间段（替换屏幕中心角色 OCR）----
        private FactionRecognizer _faction;
        private readonly RoleTimeline _roleTimeline = new RoleTimeline();

        /// <summary>本局角色时间段（UI 和以后的一图流都读这里）。</summary>
        public RoleTimeline RoleTimeline
        {
            get { return _roleTimeline; }
        }

        /// <summary>手动/自动再认一次阵营（界面上的“重新识别”按钮）。</summary>
        public void RequestFactionScan(string reason)
        {
            if (_faction == null) return;
            ShowRoleBusy();
            _faction.Begin(string.IsNullOrEmpty(reason) ? "手动重新识别" : reason, 0, 0);
        }

        /// <summary>
        /// 第 4 批：把后台线程的识别结果切回 UI 线程。
        /// 阵营识别跑在 Timer 线程上，而 `OnRoleRecognized` 会碰提示窗，
        /// 不切线程会直接跨线程访问控件。Program 启动时接到热键窗的 RunOnUi。
        /// </summary>
        public Action<Action> UiInvoke;

        public TacticalLog Tactical
        {
            get { return _tactical; }
        }

        /// <summary>
        /// 战术数据被外部（屏幕 OCR 自动填写 / 校准）改动后触发。
        /// 界面层接上去刷一次战术面板即可 —— 面板的刷新是单向的
        /// （模型 → 控件 → 重绘），不会反过来再改数据，所以不会死循环。
        /// </summary>
        public event Action TacticalChanged;

        /// <summary>通知界面：战术数据变了，需要刷一次面板。</summary>
        public void NotifyTacticalChanged()
        {
            NotifyTacticalChanged(true);
        }

        /// <summary>
        /// 战术数据变了 → 刷面板（可选地"把这也当成对局证据"）。
        ///
        /// 【为什么要这个开关】`ClearAllRoundData` 也会调它来刷面板；
        /// 那条路径刚刚把 `_roundActive` 置 false（回合结束），如果还按
        /// "屏幕上认到了战术数据"去判中途加入，就会立刻把新回合开起来、
        /// 顺便把 `_inServer` 又置回 true —— 那样"回合之间不弹提示"就白做了。
        /// </summary>
        public void NotifyTacticalChanged(bool roundEvidence)
        {
            // OCR 认出了"只有对局里才会出现"的战术数据 → 这本身就是"这一局在打"的证据。
            // 放在最前面：面板刷新不受它影响。
            if (roundEvidence) NoteRoundEvidence("屏幕上的战术数据");

            if (TacticalChanged != null) TacticalChanged();
        }

        /// <summary>回合计时是否被手动暂停。</summary>
        private bool _clockPaused;

        public bool ClockPaused
        {
            get { return _clockPaused; }
        }

        /// <summary>手动暂停 / 继续回合计时（暂停期间所有倒计时都停住）。</summary>
        public void ToggleClockPaused()
        {
            // 如果之前是按了"停止全部计时"，这个键应当也能把状态拉回来，
            // 否则用户会看到"按了没反应"。
            if (_allStopped)
            {
                ResumeAllTimers();
                return;
            }

            _clockPaused = !_clockPaused;
            _est.Paused = _clockPaused;
            StatusText = _clockPaused ? "回合计时已暂停" : "回合计时已继续";
            ApplyAll();
        }

        /// <summary>
        /// 手动"回合开始"（默认快捷键 F6）。
        ///
        /// 【为什么需要】日志里那行"回合开始"有时候偏早/偏晚，
        /// 或者用户中途才开程序。按一下就把回合计时校准到此刻 ——
        /// 之后所有跟着回合走的倒计时（支援刷新、净化…）都以此为基准。
        /// </summary>
        public void MarkRoundStartNow()
        {
            ResumeFromStop();
            SetPreparing(false);
            _roundActive = true;
            _midJoinRound = false;         // 用户把此刻当成回合开头 → 正常显示"本回合已进行"

            DateTime now = DateTime.Now;
            _roundStart = now;
            _lastRoundSignalAt = now;
            _est.BeginRound(now, 0);
            _est.ResetWarhead();

            // 用"SCP 阵亡"那条提示的位置显示一句反馈（用户要求把那条改成回合开始校准）
            _scpText = "回合开始：已手动校准到现在";
            _scpShowing = true;
            _scpUntil = now.AddSeconds(6);

            StatusText = "回合开始已手动校准";
            Logger.On("回合", "手动校准回合开始 → 计时从此刻 0 秒开始");
            ApplyAll();
        }

        /// <summary>
        /// 用户在校准面板（战术面板「校准」页）里提交了"回合已进行多少" → 把回合标记成已开始，
        /// 让回合计时 / 支援 / 净化这些立刻显示出来。
        /// （以前只校准了估算器，回合没被标记，所以"提交了但画面上没变化"。）
        /// </summary>
        public void NotifyRoundCalibrated()
        {
            ResumeFromStop();
            SetPreparing(false);
            _roundActive = true;
            // 用户校准过 → 计时是真的回合时间了，显示回「本回合已进行 mm:ss」
            _midJoinRound = false;
            if (_roundStart == DateTime.MinValue) _roundStart = DateTime.Now;
            _lastRoundSignalAt = DateTime.Now;
            ApplyAll();
        }

        /// <summary>在战术面板里标记某个 SCP 阵亡时，弹一条真的阵亡提示。</summary>
        public void ShowScpDiedByCode(string code)
        {
            ResumeFromStop();
            DateTime now = DateTime.Now;
            _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                new object[] { "SCP-" + code, "战术面板", "已阵亡" });
            _scpShowing = true;
            _scpUntil = now.AddSeconds(Math.Max(4, _cfg.DisplaySeconds));
            ApplyAll();
        }

        /// <summary>
        /// OCR 认到 SCP 阵亡时弹提示（需求一）。
        ///
        /// 【规则】只有明确认出击杀者（玩家名或阵营名）才弹；
        /// 认不出来就整条忽略 —— 宁可少一条提示，也不要弹"【未知】击杀了…"
        /// 这种没有信息量、还会误导判断的东西。
        ///
        /// 击杀者属于某阵营时，顺便把这个阵营写进"支援可能即将刷新"那一句里，
        /// 于是原来是"未知"的位置会显示成 MTF / 混沌。
        ///
        /// 返回 true 表示弹了提示；false 表示按规则忽略了。
        /// </summary>
        public bool ShowScpKilledByOcr(string code, string killer, string faction)
        {
            if (string.IsNullOrEmpty(code)) return false;

            bool hasKiller = !string.IsNullOrEmpty(killer)
                          && !string.Equals(killer, "未知", StringComparison.Ordinal);
            bool hasFaction = faction != null && faction != TacticalLog.NONE;

            // 需求一.2：认不出击杀者 → 直接忽略，不弹
            if (!hasKiller && !hasFaction) return false;

            ResumeFromStop();
            DateTime now = DateTime.Now;

            string who = hasKiller ? killer : TacticalLog.FactionName(faction);
            if (hasFaction && hasKiller
                && who.IndexOf(TacticalLog.FactionName(faction), StringComparison.Ordinal) < 0)
            {
                // "某玩家（MTF）"这种，把阵营也带上，信息更全
                who = who + "（" + TacticalLog.FactionName(faction) + "）";
            }

            _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                new object[] { "SCP-" + code, who, "已阵亡" });
            _scpShowing = true;
            _scpUntil = now.AddSeconds(Math.Max(4, _cfg.DisplaySeconds));
            // 自定义触发点用：「某个 SCP 刚刚阵亡」（编号精确匹配）
            NotePulse("pulse.scp.killed", null, code);

            // 支援刷新估算（不精确，仅提醒）
            _cd[I_Support].Begin(now, _cfg.Alerts[I_Support].DurationSeconds);
            ApplyAll();
            return true;
        }

        /// <summary>进入服务器时请求弹出校准窗口。</summary>
        public event Action CalibrateRequested;

        /// <summary>
        /// 检测到 SCP:SL 退出了（进程消失）。
        /// Program 接这个事件决定"游戏退了本程序也退"。
        /// </summary>
        public event Action GameExited;

        /// <summary>第 5 批：游戏启动（进程出现）时通知外层跑一次可疑进程扫描。</summary>
        public event Action GameStarted;

        /// <summary>第 6 批：自己阵亡 / 复活（采集模块用）。</summary>
        public event Action SelfDeath;
        public event Action SelfRespawn;

        public TimeEstimator Estimator
        {
            get { return _est; }
        }

        private readonly int I_RoundStart, I_RoundTimer, I_ServerJoin, I_GameLaunch;
        private readonly int I_Warhead, I_Decon, I_Scp, I_Support, I_Gen, I_Token, I_Role, I_Ticket, I_Wave, I_ScpKilled, I_Startup;
        private readonly int I_LocalDeath, I_SelfRespawn;
        private readonly int I_GenActive;
        private readonly int I_DeadMan;
        private readonly int I_Respawn;

        private bool _editMode;
        private bool _allHidden;
        private bool _allStopped;
        private DateTime _allStoppedAt = DateTime.MinValue;
        private bool _estWasRunningAtStop;
        private bool _globalMove;
        private bool _forceShowAll;
        private bool _disposed;

        private Point[] _lastPos;

        /// <summary>托盘提示文字。</summary>
        public string StatusText = "正在启动…";

        // ---- 回合开始 ----
        private int _roundCounter;
        private bool _roundStartShowing;
        private DateTime _roundStartUntil;
        private string _roundStartText = "";

        // ---- 回合时间 ----
        private bool _roundActive;
        private DateTime _roundStart;
        private DateTime _roundPopUntil;

        // ---- 准备阶段（进图之后、回合正式开始之前）----
        private bool _roundPreparing;
        private DateTime _roundStartAt = DateTime.MinValue;

        /// <summary>最近一次"回合相关信号"（进图 / 回合开始）的时刻 —— 用来判断"是不是刚开局"。</summary>
        private DateTime _lastRoundSignalAt = DateTime.MinValue;

        // ============================================================
        // 分配角色（需求六 / 七）
        //
        // 日志里那行"进图"（SceneLoaded 2. Facility）出现时，游戏其实是在分配角色：
        // 玩家还得等几秒（常见 5~10 秒）才真正拿到角色、回合才算开始。
        // 所以这一段的处理是：
        //   · 回合计时先不开始，显示「等待分配角色」；
        //   · 屏幕中心那块"角色"区域用最快频率（100ms）扫，认出角色就弹提示；
        //   · 按配置的「分配角色时长」（RoundStartDelay）顺延：
        //     进图时刻 + 这个时长 = 正式计时起点（从 0 开始算）。
        //     期间只要日志里出现"回合开始标志"，就立刻开始计时（不用等满）。
        // 设 0 = 老行为：进图就开始计时。
        // ============================================================

        /// <summary>
        /// 统一改"分配角色中"这个状态，并在变化时通知 OCR 切换扫描频率。
        ///
        /// 【为什么要收成一个入口】以前是到处直接写 `_roundPreparing = ...`，
        /// 通知只在其中一处发。现在"进图 → 分配角色"这段会把屏幕中心角色区
        /// 改成 100ms 快速扫，要是哪条路径只清了标记、没发通知，
        /// 快速扫就会一直开着（一直高频 OCR）—— 收成一个口就不会漏。
        /// </summary>
        private void SetPreparing(bool on)
        {
            if (_roundPreparing == on) return;
            _roundPreparing = on;
            if (PrepPhaseChanged != null) PrepPhaseChanged(on);
        }

        /// <summary>分配角色期间的提示文字（需求七.1）。</summary>
        public const string WaitingRoleText = "等待分配角色";

        /// <summary>分配角色开始/结束的通知（让 OCR 那边切换扫描频率）。</summary>
        public event Action<bool> PrepPhaseChanged;

        /// <summary>是否处于"分配角色"这一段（回合还没正式开始）。</summary>
        public bool Preparing
        {
            get { return _roundPreparing; }
        }

        /// <summary>
        /// OCR 在屏幕中心认出了角色（需求六.3）。
        /// 立刻结束准备阶段、进入正式计时，并弹一条角色提示。
        /// </summary>
        /// <summary>阵营识别开始时弹“识别中”，复用角色提示的样式。</summary>
        public void ShowRoleBusy()
        {
            try
            {
                if (_cfg == null || !_cfg.Alerts[I_Role].Style.Enabled) return;
                _roleText = SafeFormat(_cfg.Alerts[I_Role].Format, new object[] { "识别中…" });
                _roleShowing = true;
                _roleUntil = DateTime.Now.AddSeconds(Math.Max(2, _cfg.Alerts[I_Role].DurationSeconds));
                ApplyAll();
            }
            catch { }
        }
        public void OnRoleRecognized(string role)
        {
            if (string.IsNullOrEmpty(role)) return;
            _lastRole = role;

            DateTime now = DateTime.Now;
            // 认出角色 = 屏幕上出现了"只有进了局才有"的东西 → 中途加入也算得出来
            NoteRoundEvidence("你的角色");

            // ---- 认出"正式角色"时立刻开始计时（可选，默认关）----
            // 【操作者的观察】进图后玩家先是观察者，过几秒才变成真正的游戏内角色。
            // 有的服"变角色"那一刻就是回合真正开始 —— 那种服开这个开关最准。
            // ⚠ 不能无条件用它：他那台服"变角色"之后还要等大厅倒计时数完，
            // 无条件按它计时会偏早一分钟。所以：
            //   · 默认关（config.ini → [General] RoundStartOnRole=1 才生效）；
            //   · 即使打开，只要"这次开局已经由服务器倒计时定下来了"就不抢跑。
            if (_cfg.RoundStartOnRole && _roundPreparing && !_roundStartFromCountdown)
            {
                SetPreparing(false);
                Logger.On("回合", "认出正式角色（" + role + "）→ 按它开始计时"
                    + "（[General] RoundStartOnRole=1 时这样）"
                    + (_sawObserverThisRound ? "；本轮见过观察者 ✓（是真的『观察者→角色』转换）"
                                             : "；本轮没读到观察者（不确定是不是转换）"));
                DoRoundStart(now, "认出正式角色：" + role);
            }
            else if (_roundPreparing)
            {
                // 不管有没有开这个开关，都把"变角色的时刻"记下来 ——
                // 操作者拿它和"倒计时数到 0"的时间对一下，就知道该不该开。
                double sinceScene = _lastRoundSignalAt == DateTime.MinValue
                    ? -1 : (now - _lastRoundSignalAt).TotalSeconds;
                Logger.On("回合", "还在准备阶段时认出了正式角色（" + role + "）"
                    + (sinceScene >= 0 ? "，距上一次回合信号 " + (int)sinceScene + " 秒" : "")
                    + (_sawObserverThisRound ? "，见过观察者 ✓" : "")
                    + "；本次开局" + (_roundStartFromCountdown ? "已由服务器倒计时定下" : "还是固定时长估算")
                    + " → 要不要按它计时见 [General] RoundStartOnRole");
            }

            // 角色提示（不在准备阶段也提示，方便中途加入的人知道自己是啥）
            _roleText = SafeFormat(_cfg.Alerts[I_Role].Format, new object[] { role });
            _roleShowing = true;
            _roleUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Role].DurationSeconds));

            // 【计时口径】认出角色本身不会打断/提前已经存在的计时：
            //   ① 已经在准备阶段（等配置的准备时长）→ NoteRoundEvidence 直接返回，不动它；
            //   ② 日志已经让计时开始了 → 同上，不动它；
            //   ③ 只有"什么都没检测到、但屏幕上已经有角色了"（玩到一半才开程序）
            //      → 才按"中途加入"起算。
            Logger.On("回合", "认出角色：" + role
                + "（只弹提示；如果本来还没开始计时，才按中途加入起算）");
            ApplyAll();
        }

        private string _roleText = "";
        private string _lastRole = "";
        private bool _roleShowing;
        private DateTime _roleUntil;

        // ============================================================
        // 死亡界面的票数 / 影响力（需求八）
        //
        // 玩家一死就开扫；确认复活或离开服务器时停止。
        // ============================================================

        /// <summary>死亡扫描开 / 关（让 OCR 那边启用那 4 个框）。</summary>
        public event Action<bool> DeathScanChanged;

        private bool _deathScanOn;

        private string _ticketText = "";
        private bool _ticketShowing;
        /// <summary>这一轮死亡扫描到底认到东西没有（用来给"没识别到"的提示）。</summary>
        private bool _ticketGotSomething;
        private DateTime _ticketUntil;


        /// <summary>
        /// 「分配角色时长」之外再多等的几秒：留给服务器那条「大厅倒计时」。
        /// 很多服在"进图"之后十来秒才吐第一条倒计时，一到点就计时的话那条永远赶不上。
        /// </summary>
        public const int RoundStartGraceSeconds = 5;

        /// <summary>
        /// 这台服务器报过「大厅倒计时」之后，下一局"进图"最多等这么久再兜底开始计时。
        /// 倒计时一旦出现就会立刻接管（比这个兜底早），所以只是"别抢跑"。
        /// </summary>
        public const int LobbyCountdownWaitSeconds = 90;

        /// <summary>这台服务器会不会自己报「大厅倒计时」（认过一次就记住）。</summary>
        private bool _sawLobbyCountdown;

        /// <summary>当前这次"准备中"的开关时刻是不是已经由服务器倒计时定下来了。</summary>
        private bool _roundStartFromCountdown;

        /// <summary>
        /// 本局是不是"中途加入"才开始的（玩一半才开程序 / 中途进服）。
        ///
        /// 【为什么要这个标记】中局加入时，日志里那句"回合开始"早就写过去了，
        /// 程序只能从"此刻"开始数 —— 屏幕上要是照样写「本回合已进行 03:21」，
        /// 会让人误以为这就是真实回合时间（其实是软件自己数了 3 分 21 秒）。
        /// 所以操作者要求：中途加入时改成显示「当前已计时 Ns」，
        /// 只有**正常从开局计时**或者**用户校准过**之后，才回到「本回合已进行 mm:ss」。
        /// </summary>
        private bool _midJoinRound;
        private int _calibPromptsLeft = 2;
        private bool _calibResolved;

        /// <summary>本次开局有没有在角色框上读到"观察者"（用来确认"观察者→正式角色"转换）。</summary>
        private bool _sawObserverThisRound;

        /// <summary>
        /// 屏幕角色框显示"观察者"（进图后先是观察者，几秒后才变正式角色）。
        ///
        /// 只做两件事：① 记下"本轮见过观察者"，这样之后认出正式角色时能确认
        /// 那是转换而不是中途加入；② 在日志里留一行时间锚点，方便和
        /// "服务器开局倒计时数到 0" 对比（操作者就是拿这两行判断该不该开
        /// [General] RoundStartOnRole）。
        /// 状态变了才记，避免每轮轮询都刷一行。
        /// </summary>
        /// <summary>
        /// 把"本局"的数据全部清空：模型 + config 里那些面板直接读的键。
        ///
        /// 【什么时候调】操作者要求：重启回合 / 进入服务器 / 离开当前服务器 时都清一次。
        /// 触发点分别是
        ///   · 日志里"回合结束"广播 / "Server is performing a full round restart" /
        ///     `SceneLoaded 1. NewMainMenu`（回主菜单 = 退服）→ `RoundFinished`
        ///   · "Hi 名字! You have been authenticated"（进服）→ `PlayerJoined`
        ///   · `SceneLoaded 2. Facility`（进图）→ `RoundPreparing`
        ///
        /// ⚠ 保留 `hp_&lt;编号&gt;`（SCP 总血量，是服务器参数）和 `genbase_&lt;n&gt;`
        ///（发电机满速，是设置）—— 这两个不是"本局数据"，清了每次都要重填。
        /// </summary>
        public void ClearAllRoundData(string why)
        {
            try
            {
                // 【别把同一件事连清好几次】日志监控在某些机器上会把
                // `SceneLoaded 2. Facility` 那一行重复吐出来（实测 1 秒一次、连吐 6 次），
                // 于是这里每秒钟清一遍、日志刷屏。10 秒内的重复清空直接跳过。
                DateTime now = DateTime.Now;
                if (_lastClearAt != DateTime.MinValue
                    && (now - _lastClearAt).TotalSeconds < 10)
                    return;
                _lastClearAt = now;

                _tactical.ResetRound();
                ResetTokenState();
                ResetEstimateVisuals();

                // ⚠ 【不要在这里动 _roundActive】—— 这里也会被"进图"调用，
                // 而进图在一局中途重连 / 日志重读时会再来一次；把 _roundActive
                // 打成 false 会让 ApplyAll 里那道"回合没开始就全部收起"的闸
                // 把所有对局内提示都收掉，而且要等到下一局开始才恢复
                //（操作者报的"改完以后回合开始的计时好像不会开始了"就是这个）。
                // 回合真的结束/换服时，由 RoundFinished / PlayerJoined 那两处显式置 false。
                _wasBothOut = false;
                _deadManAt = DateTime.MinValue;
                _deadManOcrAt = DateTime.MinValue;
                _deadManBlockedLogged = false;
                _deadManCompleted = false;
                _deadManDoneUntil = DateTime.MinValue;
                _deadManNotBothOutSince = DateTime.MinValue;
                _deadManUntil = DateTime.MinValue;
                _midJoinRound = false;
                _deadManText = "";

                // config 里"本局"的键也一起清（战术面板直接读它们）
                ClearRoundConfigKeys();

                Logger.On("回合", "已清空本局全部数据（" + why + "）");
                NotifyTacticalChanged(false);   // 只刷面板，别当成"对局证据"
            }
            catch (Exception ex) { Logger.Err("回合", "清空本局数据失败", ex); }
        }

        /// <summary>上一次清空本局数据的时刻（防重复清空的节流用）。</summary>
        private DateTime _lastClearAt = DateTime.MinValue;

        /// <summary>
        /// 只清 config 里那些"本局"的键（战术面板直接读它们）。
        /// 分配角色结束、正式开始计时（<see cref="DoRoundStart"/>）时也要清一次 ——
        /// 否则上一局的发电机剩余时间 / 影响力读数会跟着挂到新一局上。
        /// 保留 `hp_&lt;编号&gt;`（SCP 总血量）与 `genbase_&lt;n&gt;`（发电机满速）：
        /// 那是服务器参数 / 设置，不是本局数据。
        /// </summary>
        private void ClearRoundConfigKeys()
        {
            _cfg.SetTactical("tok_MTF", "");
            _cfg.SetTactical("tok_混沌", "");
            _cfg.SetTactical("inf_MTF", "");
            _cfg.SetTactical("inf_混沌", "");
            _cfg.SetTactical("has079", "");
            for (int g = 1; g <= TacticalLog.GeneratorCount; g++)
            {
                _cfg.SetTactical("gen_" + g, "");
                _cfg.SetTactical("genfac_" + g, "");
                _cfg.SetTactical("genrun_" + g, "");
            }
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                _cfg.SetTactical("cur_" + code, "");
                _cfg.SetTactical("fac_" + code, "");
                _cfg.SetTactical("kill_" + code, "");
                _cfg.SetTactical("pre_" + code, "");
                _cfg.SetTactical("killed_" + code, "");
                _cfg.SetTactical("killfac_" + code, "");
            }
        }

        public void NoteObserverState()        {
            DateTime now = DateTime.Now;
            if (_sawObserverThisRound) return;
            _sawObserverThisRound = true;

            double sinceScene = _lastRoundSignalAt == DateTime.MinValue
                ? -1 : (now - _lastRoundSignalAt).TotalSeconds;
            Logger.On("回合", "屏幕角色框 = 观察者（准备阶段）"
                + (sinceScene >= 0 ? "，距上一次回合信号 " + (int)sinceScene + " 秒" : "")
                + "；等它变成正式角色时就是「观察者→角色」转换");
        }

        private void BeginDeathScan(DateTime now)
        {
            _ticketGotSomething = false;
            // 【新的一次死亡，判复活的三个计数器都归零】
            // _lastTicketOkAt = MinValue 表示"这次死亡期间还没读到过票数"，
            // 如果整场都没读到票数（区域没对准），
            // 那就只能靠"连续 3 轮全空"那一路判复活，不会因为票数时间戳陈旧而立即误判。
            _lastTicketOkAt = DateTime.MinValue;
            _deathEmptyRounds = 0;
            _silenceMisses = 0;
            _strictTicketMisses = 0;
            _lastSilenceMissAt = DateTime.MinValue;
            _lastTicketMissAt = DateTime.MinValue;

            // 死亡立刻给一条反馈：让用户知道"程序看到了、正在读" ——
            // 以前死亡后什么都没弹，用户根本不知道是没识别到还是没做这件事。
            _ticketText = "死亡：正在读取上方票数 / 重生次数 / 左侧 SCP 列表…";
            _ticketShowing = true;
            _ticketUntil = now.AddSeconds(Math.Max(6, _cfg.Alerts[I_Ticket].DurationSeconds));
            if (_deathScanOn) return;

            _deathScanOn = true;
            if (DeathScanChanged != null) DeathScanChanged(true);
        }

        private void EndDeathScan(string why)
        {
            if (!_deathScanOn) return;

            // 整段死亡期间什么都没读到 → 明确告诉用户"没识别到"，
            // 而不是默默什么都不显示（这样至少知道要去检查区域）。
            if (!_ticketGotSomething)
            {
                _ticketText = "没识别到票数 / 重生次数 / SCP 列表（检查那几块区域是否对准，"
                    + "或到 设置 → OCR 区域列表 里用【鼠标重新框选】调一下）";
                _ticketShowing = true;
                _ticketUntil = DateTime.Now.AddSeconds(Math.Max(6, _cfg.Alerts[I_Ticket].DurationSeconds));
                Logger.On("票数", "这一轮死亡扫描什么都没认到（" + why + "）→ 已提示用户检查区域");
            }
            _deathScanOn = false;
            if (DeathScanChanged != null) DeathScanChanged(false);

            // 【复活就把票数提示收起来】操作者要求：票数那块放屏幕底下，
            // 但要"一般活了就隐藏"—— 活着的时候不该有一条票数糊在屏幕底部。
            // 这里不看成 _ticketUntil：只要死亡扫描结束（复活 / 回合结束 / 离服），
            // 就把这条收掉。
            if (_ticketShowing)
            {
                _ticketShowing = false;
                Logger.On("票数", "死亡界面结束（" + why + "）→ 收起票数提示");
            }

            Logger.On("票数", "停止扫描上方票数框：" + why);
        }

        /// <summary>
        /// OCR 读出了票数 / 影响力（需求八）。
        /// 弹一条提示，并把数值写进战术数据里（键名 tok_/inf_），
        /// 战术面板会用 TicketSummary() 把这两行显示出来。
        /// </summary>
        /// <param name="authoritative">
        /// 是不是"死亡界面的权威读数"。活着时只读影响力，令牌数只在死亡期间校准。
        /// 防误读靠的是另外三道闸：读数必须是干净数字、令牌不能超过
        /// <see cref="MaxTokensOnScreen"/>、同一个值要连续读到几次。
        /// </param>
        public void OnTicketRecognized(string tag, string value)
        {
            OnTicketRecognized(tag, value, true);
        }

        public void OnTicketRecognized(string tag, string value, bool authoritative)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(value)) return;

            DateTime now = DateTime.Now;
            string side = tag.StartsWith("mtf", StringComparison.OrdinalIgnoreCase) ? "MTF"
                        : tag.StartsWith("chaos", StringComparison.OrdinalIgnoreCase) ? "混沌"
                        : tag;
            bool isTokens = tag.EndsWith("_tokens", StringComparison.OrdinalIgnoreCase);

            // 活着时令牌区域不可读，也不应覆盖事件估算；只有死亡界面的 HUD
            // 令牌读数才是可用的准确校准值。影响力（小票）仍然常开读取。
            if (isTokens && !_deathScanOn) return;

            // ---- 显示名 ≠ 模型里的阵营名（这里踩过一个大坑）----
            // 屏幕上的侧名是"MTF / 混沌"（中文），而模型里的阵营常量是
            // TacticalLog.MTF / TacticalLog.CHAOS（"MTF" / "Chaos"，英文）。
            // 以前把显示名直接丢给 CalibrateTokens / CalibrateInfluence，
            // 而 TicketModel.Of("混沌") 比不过 "Chaos"、落到默认分支 = MTF ——
            // 于是混沌那一路的令牌 / 影响力全被写到了 MTF 头上：
            //   混沌令牌读 0 → MTF 也变 0 → 亡者协议反复"启动 / 重置"；
            //   面板上两边的令牌数字永远和屏幕上对不上（影响力因为"只升不降"
            //   看起来像是对了，其实是把混沌的读数加到了 MTF 上）。
            // 现在分成两个变量：side 只管显示和 config 键名，faction 才是模型口径。
            string faction = tag.StartsWith("mtf", StringComparison.OrdinalIgnoreCase) ? TacticalLog.MTF
                           : tag.StartsWith("chaos", StringComparison.OrdinalIgnoreCase) ? TacticalLog.CHAOS
                           : null;
            if (faction == null)
            {
                Logger.On("票数", "认不出这个框是哪一方的（tag=" + tag + "），忽略：" + value);
                return;
            }

            // ---- 先过"这个读数可不可能"这一关 ----
            //
            // 【为什么要放在"连续几次"前面】以前是先攒够次数、最后才判数字干不干净，
            // 于是一个根本不可能的读数会被当成"确定值"反复显示：
            // 日志实锤 —— MTF 令牌框连着 6 轮都读成 7（一局最多 1 + 3 个额外令牌），
            // 提示上就一直挂着那个 7，看起来像"读到一个错数就钉死了，也不再重读"。
            // 现在不可能的读数当场丢掉，而且不进计数，下一轮照样重新读。
            int num;
            if (!TryParseScreenNumber(value, out num))
            {
                Logger.On("票数", region_side_hint(side, isTokens) + value
                    + " → 不是干净数字，当作误读丢掉（会继续重读）");
                return;
            }
            if (isTokens && num > MaxTokensOnScreen)
            {
                Logger.On("票数", region_side_hint(side, isTokens) + value
                    + " → 令牌不可能超过 " + MaxTokensOnScreen
                    + " 个，当作误读丢掉（会继续重读）");
                return;
            }

            // ---- 【判复活用】读到了一个干净数字 = 死亡界面还在 ----
            // 操作者口径："必须令牌和静音那 4 个选项都识别不到才算重生"。
            // 所以这里记下"最近一次真的读到了票数"的时刻，用来拦住
            // "静音开关抖两下就报重生"（他遇到过：明明还能看到重生令牌，却提示已重生）。
            if (_deathScanOn)
            {
                _lastTicketOkAt = now;
                _deathEmptyRounds = 0;      // 这一轮不是"什么都没读到"
            }

            // ---- 多次识别取"确定值" ----
            // 死一次可能只有一两帧能看清，所以同一个框认出同样的值 2 次才算数；
            // 这样显示出来的是"确定值"，不会一会 49 一会 4g。
            //
            // 【踩过的坑】这里以前直接写 _ticketLast[tag]，而字典一开始是空的 ——
            // 第一次识别任何一个框都会抛 KeyNotFoundException，
            // 于是整条票数链路从第一帧就断了：config 里永远写不进 tok_ / inf_，
            // 面板「记录」页那一行永远是空的（看起来像"功能没写完"）。
            int seen = 0;
            _ticketHits.TryGetValue(tag, out seen);

            string last;
            if (!_ticketLast.TryGetValue(tag, out last))
            {
                _ticketLast[tag] = value;
                seen = 1;                       // 第一次看到这个框
            }
            else if (value == last)
            {
                seen++;                         // 连续读到同样的值
            }
            else
            {
                _ticketLast[tag] = value;
                seen = 1;                       // 变了 → 重新数
            }
            _ticketHits[tag] = seen;

            // 令牌（重生次数）比影响力更"要紧"：它直接决定亡者协议 / 核弹会不会启动，
            // 读歪一次就会把判断带偏，所以要求连续 3 次一样才采纳（操作者要求）。
            int need = isTokens ? 3 : 2;
            if (seen < need)
            {
                Logger.On("票数", region_side_hint(side, isTokens) + value + "（第 " + seen
                    + " 次，要连续 " + need + " 次一样才采纳）");
                return;
            }

            // 影响力活着时持续扫描；令牌只来自死亡界面的 HUD 读数。
            if (!authoritative && !isTokens)
                Logger.On("票数", region_side_hint(side, isTokens) + value
                    + "（存活时读到的影响力，确认后写进战术数据）");

            // 写进战术数据（面板会显示出来）
            _cfg.SetTactical((isTokens ? "tok_" : "inf_") + side, value);

            // ---- 同步进战术模型：面板和亡者协议判断用的就是模型里这份 ----
            // 操作者要求"面板和其他所有数据同步"：
            //   · 影响力只升不降（一局内只会涨，读到更低的当 OCR 读歪，忽略）；
            //   · 令牌按屏幕上的值反推（重生一次照样 −1，下次再对齐）。
            // 能走到这里说明这个值已经连续读到一样（见上面的确认逻辑），
            // 而且是个干净、可能范围内的数字（见函数开头那道闸）。
            // 先登记屏幕证据：若这是中途加入的第一份数据，要先把历史令牌消耗标成未知，
            // 避免影响力校准时把从阈值 40 起的奖励当成这局刚拿到的令牌。
            NoteRoundEvidence(isTokens ? "死亡界面的令牌" : "影响力读数");

            // ---- 影响力右边的 MAX（游戏自己写的，不是我们猜的）----
            // 实测原文："@ 168/MAX" / "63/MAX G"。它表示这一方本回合已经拿满，
            // 不会再有新令牌 → 也不再会有新的大波。只置位、不在读丢时清除，
            // 否则 OCR 偶尔把 MAX 认成 "N" 就会把确认过的状态洗回去；新一局 Reset() 清。
            bool changedMax = !isTokens && TicketValueSaysMax(value)
                && _tactical.Tickets.MarkInfluenceMaxed(faction);

            bool changedModel = isTokens
                ? _tactical.Tickets.CalibrateTokens(faction, num)
                : _tactical.Tickets.CalibrateInfluence(faction, num);
            if (changedMax)
            {
                Logger.On("票数", side + " 影响力读数 = MAX → 本回合不会再有新令牌 / 新大波");
            }
            if (changedModel || changedMax)
            {
                Logger.On("票数", "已同步进战术模型：" + side
                    + (isTokens ? " 令牌 → " : " 影响力 → ") + num
                    + (changedMax ? "（MAX）" : "")
                    + "（面板 / 亡者协议都用这份数据）");
                NotifyTacticalChanged();
            }

            ShowTicketPrompt(side, isTokens, value);
            Logger.On("票数", "识别到 " + side + (isTokens ? " 可用令牌 " : " 影响力 ") + value
                + "（已写入战术数据 " + (isTokens ? "tok_" : "inf_") + side + "）");
        }

        /// <summary>弹一下"票数 / 影响力"那条提示（模型那边不写）。</summary>
        private void ShowTicketPrompt(string side, bool isTokens, string value)
        {
            DateTime now = DateTime.Now;
            string line = side + (isTokens ? " 可用令牌 " : " 影响力 ") + value;
            string all = TicketSummary();
            _ticketText = SafeFormat(_cfg.Alerts[I_Ticket].Format,
                new object[] { all.Length > 0 ? all : line });
            _ticketGotSomething = true;
            _ticketShowing = true;
            _ticketUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Ticket].DurationSeconds));
            ApplyAll();
        }

        /// <summary>取战术数据，取不到就是空串（GetTactical 只接受一个参数）。</summary>
        private string GetTacticalOr(string key)
        {
            try { string v = _cfg.GetTactical(key); return v == null ? "" : v; }
            catch { return ""; }
        }

        /// <summary>
        /// 屏幕上最多可能显示几个重生令牌。
        /// 文档：每方开局自带 1 个，每回合最多再拿 3 个（两方共享）→ 最多 4 个。
        /// 留一个余量取 5；读到 6 以上一定是 OCR 认歪了（实测会读出 7、9 这种）。
        /// </summary>
        private const int MaxTokensOnScreen = 5;

        /// <summary>
        /// 把屏幕上那一格的值读成整数。
        /// 影响力那格游戏里写的是「49/80」这种（下一阈值），OCR 还可能读成「15/」，
        /// 拿满时右边是 MAX（如「168/MAX」，MAX 由 TicketValueSaysMax 另判）。
        /// 所以这里只取开头那串数字；一上来就不是数字的（"MAX"、"?"）直接不要。
        /// </summary>
        private static bool TryParseScreenNumber(string v, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(v)) return false;

            int i = 0;
            while (i < v.Length && v[i] >= '0' && v[i] <= '9') i++;
            if (i == 0 || i > 3) return false;                 // 一上来就没数字 / 太长 → 不要
            if (!int.TryParse(v.Substring(0, i), out n)) return false;
            return n >= 0 && n <= 250;                          // 影响力阈值最高 200，令牌最多 5
        }

        /// <summary>
        /// 影响力那一格右边是不是游戏写的 MAX（"168/MAX"）。
        /// 只有影响力走这个判断；令牌框里的 MAX 一律不算。
        /// </summary>
        private static bool TicketValueSaysMax(string v)
        {
            return !string.IsNullOrEmpty(v)
                && v.IndexOf("MAX", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string _waveText = "";
        private bool _waveShowing;
        private DateTime _waveUntil;

        /// <summary>
        /// 识别到"某某进入设施"（需求八附）：
        /// 弹一条提示，把识别到的阵营 + 推测的后续刷新一起告诉用户。
        /// </summary>
        public void OnWaveRecognized(string faction)
        {
            OnWaveRecognized(faction, false);
        }

        /// <summary>带大/小波的版本（播报里说"增援部队"就是小波）。</summary>
        public void OnWaveRecognized(string faction, bool isSmallWave)
        {
            DateTime now = DateTime.Now;
            string who = TacticalLog.FactionName(faction);

            // 刷新增援的播报只会在对局里出现 → 也是"中途加入"的证据
            NoteRoundEvidence("刷新增援的播报");

            string text = "识别到 " + who + " 进入设施（" + (isSmallWave ? "小波 / 增援部队" : "大波") + "）";
            _waveText = SafeFormat(_cfg.Alerts[I_Wave].Format, new object[] { text });
            _waveShowing = true;
            _waveUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Wave].DurationSeconds));
            Logger.On("波次", text + "；" + _tactical.PredictNextWaves(now));
            ApplyAll();
        }

        private string _scpKilledText = "";
        private bool _scpKilledShowing;
        private DateTime _scpKilledUntil;

        // ============================================================
        // 启动演出（用户要求）
        //
        //   1. 把所有提示全部弹出来（一眼看到自己配置的样式和位置）；
        //   2. 用"故障"的方式退场：文字被乱码快速替换 + 高亮闪烁；
        //   3. 最后在屏幕顶端显示「SCPSLAlert 已启动」，几秒后消失。
        //
        // 全程只改文字和高亮，不动窗口位置 —— 位置是用户拖好的，
        // 一旦被程序改动就可能触发"位置已变更"，把 config 里的坐标写乱。
        // ============================================================

        private System.Windows.Forms.Timer _startupTimer;
        private int _startupStep;
        private bool _startupShowing;

        private string _startupText = "";
        private bool _startupShown;
        private DateTime _startupUntil;

        // 正常状态保持多久由 config 决定（默认 3 秒）；这里是兜底值。
        private const int StartupHoldMsDefault = 3000;

        // 故障退场：35 帧 × 200ms = 7 秒。整段演出 ≈ 3 秒正常 + 7 秒故障 = 10 秒。
        // 【为什么帧间隔不是几十毫秒】以前 45ms 一帧、十帧就完了，
        // 看起来只是"抖一下"；现在要的是"逐步被吃掉"的过程，
        // 每帧约 1/5 秒，噪声比例和"被吃掉"的范围都随进度稳定推进。
        private const int StartupGlitchFrames = 35;
        private const int StartupGlitchMs = 200;

        /// <summary>播放启动演出（Program 在启动后调用）。</summary>
        public void PlayStartupShow()
        {
            if (_disposed) return;
            if (!_cfg.StartupShow) return;
            if (_startupShowing) return;

            _startupShowing = true;
            _startupStep = 0;

            // 1) 全部弹出来（只弹用户没关掉的那些 —— 关掉的提示不该在启动时闪一下）
            for (int i = 0; i < _cfg.Alerts.Length && i < _forms.Length; i++)
            {
                if (i == I_Startup) continue;          // 启动提示最后单独显示
                AlertConfig a = _cfg.Alerts[i];
                if (a == null || _forms[i] == null) continue;
                if (a.Style == null || !a.Style.Enabled) continue;
                _forms[i].Pop(SafeFormat(a.Format, new object[]
                {
                    a.SampleText, a.SampleText2, a.SampleText3
                }));
            }
            Logger.On("启动", "播放启动演出：先把所有提示弹一遍，再做故障退场");

            _startupTimer = new System.Windows.Forms.Timer();
            int hold = _cfg.StartupHoldMs > 0 ? _cfg.StartupHoldMs : StartupHoldMsDefault;
            _startupTimer.Interval = hold;
            Logger.On("启动", "所有提示会保持正常状态 " + hold + "ms，然后故障退场");
            _startupTimer.Tick += OnStartupTick;
            _startupTimer.Start();
        }

        private void OnStartupTick(object sender, EventArgs e)
        {
            try
            {
                _startupStep++;

                // 2) 故障退场
                if (_startupStep <= StartupGlitchFrames + 1)
                {
                    if (_startupStep == 1)
                    {
                        _startupTimer.Interval = StartupGlitchMs;
                        Logger.On("启动", "故障退场（" + StartupGlitchFrames + " 帧）");
                    }

                    bool last = _startupStep > StartupGlitchFrames;
                    for (int i = 0; i < _cfg.Alerts.Length && i < _forms.Length; i++)
                    {
                        if (i == I_Startup) continue;
                        AlertConfig a = _cfg.Alerts[i];
                        if (a == null || _forms[i] == null) continue;
                        if (a.Style == null || !a.Style.Enabled) continue;

                        if (last)
                        {
                            _forms[i].Highlight = false;
                            _forms[i].Retract();
                            continue;
                        }

                        // 故障进度：第 1 帧约 0.1，最后一帧 1.0（烂透）
                        double progress = (double)_startupStep / StartupGlitchFrames;
                        _forms[i].Pop(GlitchText(SafeFormat(a.Format, new object[]
                        {
                            a.SampleText, a.SampleText2, a.SampleText3
                        }), progress));
                        _forms[i].Highlight = (_startupStep % 2) == 0;
                    }
                    return;
                }

                // 3) 顶端"SCPSLAlert 已启动"
                _startupTimer.Stop();
                _startupTimer.Dispose();
                _startupTimer = null;
                _startupShowing = false;

                // 【关键】收尾时把所有提示硬收回（HideNow：不走动画、立刻 Hidden）。
                // 踩过的坑：以前这里只靠最后一帧的 Retract()（带动画）——
                // 只要那条提示的动画没跑完 / 状态有残留，演出结束后它就会赖在屏幕上，
                // 操作者看到的就是"重生令牌在演出结束也不收回"。
                // 现在：先全部硬收回，再让 ApplyAll 把"确实该显示"的重新推出来
                // （启动提示 + 演出期间真的发生的事件），一条都不许多留。
                for (int i = 0; i < _forms.Length; i++)
                {
                    try
                    {
                        if (_forms[i] == null) continue;
                        _forms[i].Highlight = false;
                        _forms[i].HideNow();
                    }
                    catch { }
                }

                _startupText = SafeFormat(_cfg.Alerts[I_Startup].Format,
                    new object[] { "SCPSLAlert 已启动" });
                _startupShown = true;
                _startupUntil = DateTime.Now.AddSeconds(
                    Math.Max(2, _cfg.Alerts[I_Startup].DurationSeconds));
                ApplyAll();
                Logger.On("启动", "启动演出结束，已在屏幕顶端提示「SCPSLAlert 已启动」");
            }
            catch (Exception ex)
            {
                Logger.Err("启动", "启动演出出错（忽略）", ex);
                try
                {
                    if (_startupTimer != null)
                    {
                        _startupTimer.Stop();
                        _startupTimer.Dispose();
                        _startupTimer = null;
                    }
                }
                catch { }
                _startupShowing = false;
            }
        }

        /// <summary>
        /// 把一句话"打碎"成故障文字。progress = 0（完好）→ 1（烂透）。
        ///
        /// 【为什么要进度】以前每帧都是"固定 28% 的字符随机换成噪点"，
        /// 十帧看起来一模一样地在抖，像"坏了但一直没坏完"。
        /// 现在故障是逐步吃掉的：
        ///   · 前段：只有零星噪点，字还认得出；
        ///   · 中段：噪点比例越来越大；
        ///   · 后段：从右往左整段塌成 █▓ 色块，最后一帧一个字都不剩。
        /// </summary>
        private static string GlitchText(string s, double progress)
        {
            if (string.IsNullOrEmpty(s)) return "█▓▒░";

            char[] noise = new char[] { '█', '▓', '▒', '░', '#', '@', '%', '&', '*', '?', '!', '╳', '▚' };
            char[] buf = s.ToCharArray();
            int n = buf.Length;

            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            // "被吃掉"的比例：平方一下 → 前半段几乎看不出来，后段突然塌下来
            double eaten = progress * progress * 1.15;
            int from = n - (int)Math.Round(n * eaten);   // 这个下标之后的字符全变色块
            if (from < 0) from = 0;

            // 没被吃掉的字符里，噪点比例也随进度变大（6% → 66%）
            int chance = 6 + (int)(progress * 60);

            for (int i = 0; i < n; i++)
            {
                if (i >= from)
                {
                    buf[i] = noise[_rnd.Next(2)];            // █ / ▓：整段塌成色块
                    continue;
                }
                if (_rnd.Next(100) < chance) buf[i] = noise[_rnd.Next(noise.Length)];
            }
            return new string(buf);
        }

        private static readonly Random _rnd = new Random();

        /// <summary>
        /// 认不出击杀者时的被动提示：
        /// 用"SCP-XXX 已被击杀（击杀者未知）"这种说法，
        /// 而不是"未知 击杀了 SCP-XXX"（那句话读起来很别扭，也没有信息量）。
        /// </summary>
        public bool ShowScpKilledUnknownByOcr(string code, string killerOrUnknown)
        {
            if (string.IsNullOrEmpty(code)) return false;
            ResumeFromStop();
            DateTime now = DateTime.Now;
            _scpKilledText = SafeFormat(_cfg.Alerts[I_ScpKilled].Format,
                new object[] { "SCP-" + code });
            _scpKilledShowing = true;
            _scpKilledUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_ScpKilled].DurationSeconds));
            NotePulse("pulse.scp.killed", null, code);   // 自定义触发点用
            Logger.On("播报", "弹出被击杀提示（击杀者未知）：SCP-" + code);
            ApplyAll();
            return true;
        }

        /// <summary>本回合刷新顺序（战术面板用，需求八附）。</summary>
        public string WaveOrderText()
        {
            try { return _tactical.WaveHistoryText(DateTime.Now); }
            catch { return ""; }
        }

        /// <summary>后续刷新推测（战术面板用，需求八附）。</summary>
        public string WavePredictText()
        {
            try { return "推测：" + _tactical.PredictNextWaves(DateTime.Now); }
            catch { return ""; }
        }

        // 静音开关连续两次认不出来 → 产生 OCR 复活候选；最终由窗口重绘协调器确认
        private int _silenceMisses;

        // ---- 自己阵亡 / 自己重生（操作者要求：这两件事也要能配音频）----

        /// <summary>这一局自己死过没有（没死过就不谈"重生"）。</summary>
        private bool _selfDead;

        /// <summary>这次死亡的"重生"已经报过了没有（一次死亡只报一次）。</summary>
        private bool _selfRespawned;

        private DateTime _lastRespawnWindowRedraw = DateTime.MinValue;
        private DateTime _lastGameWindowRedraw = DateTime.MinValue;
        private DateTime _selfDeathAt = DateTime.MinValue;
        private DateTime _deathPairFirstRedraw = DateTime.MinValue;
        private bool _deathTailPending;
        private DateTime _lastSilenceMissAt = DateTime.MinValue;
        private DateTime _lastTicketMissAt = DateTime.MinValue;
        private int _strictTicketMisses;
        private static readonly TimeSpan StrictRespawnSignalWindow = TimeSpan.FromSeconds(15);

        private string _selfDeathText = "";
        private bool _selfDeathShowing;
        private DateTime _selfDeathUntil = DateTime.MinValue;

        private string _selfRespawnText = "";
        private bool _selfRespawnShowing;
        private DateTime _selfRespawnUntil = DateTime.MinValue;

        /// <summary>宽松模式下死亡界面连续多少轮"一个数字/开关都没读到"。</summary>
        private int _deathEmptyRounds;

        /// <summary>死亡期间"最近一次真的读到了票数"的时刻（判复活的关键闸门）。</summary>
        private DateTime _lastTicketOkAt = DateTime.MinValue;

        /// <summary>
        /// 票数框多久没读到才算"看不见了"（秒）。
        /// 静音开关消失那一路要等它过了才作数 —— 见 <see cref="OnSilenceScanned"/>。
        /// </summary>
        public const double DeathTicketStaleSeconds = 15;

        /// <summary>已确认的复活入口：结束死亡扫描、更新状态并显示复活提示。</summary>
        public void NoteAliveAgain(string why)
        {
            if (!_selfDead || _selfRespawned) return;

            _selfRespawned = true;
            NotePulse("pulse.self.respawn");
            _isDeadNow = false;   // 判定了复活
            DateTime now = DateTime.Now;

            // 第 4 批：复活 → 时间段切回 alive（时间记在复活这一刻），
            // 延迟 N 秒后再认阵营（等血条重新出现）。
            try
            {
                _roleTimeline.SetState("alive", now, true);   // 新角色未知，先清空，等识别结果
                if (_faction != null)
                    ShowRoleBusy();
                    _faction.Begin("复活", Math.Max(0, _cfg.FactionRespawnDelaySeconds) * 1000, 0);
                if (SelfRespawn != null) SelfRespawn();
            }
            catch (Exception ex) { Logger.Err("阵营", "复活识别启动失败", ex); }

            try
            {
                _selfRespawnText = SafeFormat(_cfg.Alerts[I_SelfRespawn].Format, new object[] { why });
                _selfRespawnShowing = true;
                _selfRespawnUntil = now.AddSeconds(_cfg.DisplaySeconds);
                Logger.On("提示", "判定已经重生（依据：" + why + "）→ 弹「"
                    + _cfg.Alerts[I_SelfRespawn].DisplayName + "」");
            }
            catch (Exception ex) { Logger.Err("提示", "重生提示失败", ex); }

            ApplyAll();
        }

        /// <summary>窗口 Shell Hook 的游戏窗口重绘信号；后台模式下可单独确认复活。</summary>
        public void OnGameWindowRedraw(bool gameForeground, string why)
        {
            DateTime now = DateTime.Now;
            _lastGameWindowRedraw = now;
            if (!_selfDead || _selfRespawned) return;

            if (_deathTailPending)
            {
                double afterDeath = (now - _selfDeathAt).TotalSeconds;
                double fromFirst = _deathPairFirstRedraw == DateTime.MinValue
                    ? -1 : (now - _deathPairFirstRedraw).TotalSeconds;
                bool tail = _deathPairFirstRedraw != DateTime.MinValue
                    ? fromFirst >= 0.28 && fromFirst <= 0.75
                    : afterDeath >= 0.20 && afterDeath <= 0.70;
                _deathTailPending = false; // 每次死亡最多过滤一跳
                if (tail)
                {
                    Logger.On("复活", "过滤死亡对应的尾部重绘（距死亡 "
                        + afterDeath.ToString("0.00") + " 秒）");
                    return;
                }
            }

            _lastRespawnWindowRedraw = now;

            // 非严格模式保留旧的 OCR 判定口径；严格模式才启用新的窗口信号。
            if (!_cfg.RespawnRequireTicketsGone) return;

            if (!gameForeground)
            {
                Logger.On("复活", "后台游戏窗口重绘作为严格模式复活信号" + (string.IsNullOrEmpty(why) ? "" : "（" + why + "）"));
                ConfirmSelfRespawn("后台窗口重绘");
                return;
            }

            Logger.On("复活", "前台窗口重绘已记录为辅助信号；严格模式以静音和票数 OCR 为主");
            TryStrictRespawn();
        }

        /// <summary>宽松模式沿用原 OCR 判定；严格模式由两项 OCR 主信号共同决定。</summary>
        private void OnRespawnOcrCandidate(string why)
        {
            if (!_selfDead || _selfRespawned) return;
            if (_cfg.RespawnRequireTicketsGone)
            {
                Logger.On("复活", "严格模式忽略单项 OCR 复活候选（" + why + "）；等待静音和票数两项信号");
                return;
            }

            EndDeathScan("宽松模式 OCR 判定复活：" + why);
            NoteAliveAgain(why);
        }

        /// <summary>死亡期间票数框的一轮扫描结果（只在确实配置了票数区域时回调）。</summary>
        public void OnTicketScan(bool sawTicket)
        {
            if (!_deathScanOn) return;
            if (sawTicket)
            {
                _strictTicketMisses = 0;
                _lastTicketMissAt = DateTime.MinValue;
                _deathEmptyRounds = 0;
                return;
            }

            _strictTicketMisses++;
            _lastTicketMissAt = DateTime.Now;
            TryStrictRespawn();
        }

        private void TryStrictRespawn()
        {
            if (!_cfg.RespawnRequireTicketsGone || !_selfDead || _selfRespawned) return;
            if (_silenceMisses < 2 || _strictTicketMisses < 1) return;

            DateTime now = DateTime.Now;
            if (_lastSilenceMissAt == DateTime.MinValue || _lastTicketMissAt == DateTime.MinValue)
                return;
            if (now - _lastSilenceMissAt > StrictRespawnSignalWindow
                || now - _lastTicketMissAt > StrictRespawnSignalWindow
                || Math.Abs((_lastSilenceMissAt - _lastTicketMissAt).TotalSeconds)
                    > StrictRespawnSignalWindow.TotalSeconds)
            {
                Logger.On("复活", "严格模式的静音/票数漏识别时间相隔过久，继续等待新一轮扫描");
                return;
            }

            string redraw = _lastRespawnWindowRedraw != DateTime.MinValue
                && now - _lastRespawnWindowRedraw <= StrictRespawnSignalWindow
                ? "；同时收到窗口重绘辅助信号" : "；当前没有窗口重绘辅助信号";
            Logger.On("复活", "严格模式主条件成立：静音连续 " + _silenceMisses
                + " 次未识别、票数扫描未识别到数字" + redraw);
            ConfirmSelfRespawn("静音连续未识别 + 票数未识别");
        }

        private void ConfirmSelfRespawn(string why)
        {
            if (!_selfDead || _selfRespawned) return;
            EndDeathScan("确认复活：" + why);
            NoteAliveAgain(why);
        }

        /// <summary>死亡界面这一轮什么都没读到（票数框 + 静音开关都空）→ 可能已经复活。</summary>
        public void OnDeathScreenEmpty()
        {
            if (!_deathScanOn) return;

            // 严格模式用独立的“静音未识别 + 票数未识别”两路记录，
            // 不把同一轮总空当成第三条判据。
            if (_cfg.RespawnRequireTicketsGone) return;

            _deathEmptyRounds++;
            if (_deathEmptyRounds < 3) return;      // 连续 3 轮都空才算（一轮两次扫描）

            Logger.On("票数", "死亡界面连续 " + _deathEmptyRounds
                + " 轮什么都没读到（票数框 + 静音那 4 个选项都空）→ 宽松模式判定复活");
            _deathEmptyRounds = 0;
            OnRespawnOcrCandidate("重生令牌和静音选项都读不到了");
        }

        /// <summary>OCR 扫右下角静音开关的结果（true = 还看得见 = 还没复活）。</summary>
        public void OnSilenceScanned(bool sawSilence)
        {
            if (!_deathScanOn) return;
            // 右下角那个静音开关只有死亡界面才有 → 它还在，说明这一局在打
            if (sawSilence) NoteRoundEvidence("死亡界面的静音开关");
            if (sawSilence)
            {
                _silenceMisses = 0;
                _lastSilenceMissAt = DateTime.MinValue;
                _deathEmptyRounds = 0;
                return;
            }

            DateTime now = DateTime.Now;
            if (_lastSilenceMissAt == DateTime.MinValue
                || now - _lastSilenceMissAt > StrictRespawnSignalWindow)
                _silenceMisses = 0;
            _silenceMisses++;
            _lastSilenceMissAt = now;

            if (_cfg.RespawnRequireTicketsGone)
            {
                TryStrictRespawn();
                return;
            }
            if (_silenceMisses < 2) return;

            Logger.On("票数", "静音开关连续 " + _silenceMisses
                + " 次都没认出来 → 宽松模式判定复活");
            _silenceMisses = 0;
            OnRespawnOcrCandidate("右下角静音开关消失了");
        }

        /// <summary>
        /// OCR 扫左侧列表的结果（逗号分隔的编号）。
        /// 列表里有的 SCP = 这一局在场、还活着 → 标进战术面板的"存活"。
        /// 列表里没有的不动（不能因为没列出来就判死）。
        /// </summary>
        /// <summary>本局已经"添加"过的 SCP 编号（悬浮窗只报**新出现的**那些）。</summary>
        private readonly List<string> _scpScanAdded = new List<string>();

        /// <summary>挑出这一轮新扫到的 SCP 编号（没有新的就返回空串）。</summary>
        private string TakeNewScpCodes(string codes)
        {
            string fresh = "";
            string[] arr = codes.Split(',');
            for (int i = 0; i < arr.Length; i++)
            {
                string code = arr[i].Trim();
                if (code.Length == 0) continue;
                if (_scpScanAdded.Contains(code)) continue;
                _scpScanAdded.Add(code);
                fresh += (fresh.Length > 0 ? "," : "") + code;
            }
            return fresh;
        }

        public void OnScpListScanned(string codes)
        {
            if (string.IsNullOrEmpty(codes)) return;

            // 悬浮窗（操作者要求）：**只在本局扫描到新的 SCP 时**弹一次，
            // 内容是"已添加：…"；同一批 SCP 被反复扫到（每 ~2.5 秒一轮）不再弹。
            string fresh = TakeNewScpCodes(codes);
            if (_scpScanArmed && fresh.Length > 0 && ScpScanPopup != null)
            {
                try { ScpScanPopup(fresh); } catch { }
            }

            string[] arr = codes.Split(',');
            int n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                string code = arr[i].Trim();
                if (code.Length == 0) continue;
                TacticalLog.ScpRecord rec = _tactical.Find(code);
                if (rec == null) continue;

                rec.Present = true;
                if (rec.Killed)
                {
                    // 列表里还活着 → 撤销之前那条"阵亡"（可能是误判）
                    rec.Killed = false;
                    rec.ChangedAt = DateTime.MinValue;
                    _cfg.SetTactical("killed_" + code, "");
                }
                _cfg.SetTactical("pre_" + code, "1");
                n++;
            }

            if (n == 0) return;
            Logger.On("战术", "左侧列表认出 " + n + " 个 SCP → 已在战术面板标为在场/存活：" + codes);
            NotifyTacticalChanged();
        }

        private static string region_side_hint(string side, bool isTokens)
        {
            return side + (isTokens ? " 可用令牌 " : " 影响力 ") + "识别到 " ;
        }

        private readonly Dictionary<string, string> _ticketLast = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _ticketHits = new Dictionary<string, int>();

        /// <summary>这一局"在场"的 SCP 一行摘要（给立即扫描的梗概用）。</summary>
        public string PresentScpSummary()
        {
            try
            {
                string s = "";
                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    TacticalLog.ScpRecord r = _tactical.Find(TacticalLog.ScpCodes[i]);
                    if (r == null || !r.Present) continue;
                    s += (s.Length > 0 ? "、" : "") + "SCP-" + r.Code + (r.Killed ? "(已阵亡)" : "");
                }
                return s.Length > 0 ? "在场：" + s : "";
            }
            catch { return ""; }
        }

        /// <summary>票数识别的结果行（战术面板用）。文字由 config 里那几个票数键拼出来。</summary>
        public string TicketSummary()
        {
            return _cfg == null ? "" : _cfg.TicketSummaryText();
        }


        // ---- 进入服务器 ----
        private bool _joinShowing;
        private DateTime _joinUntil;
        private string _joinText = "";

        // ---- 游戏启动 / 退出 ----
        private bool _gameShowing;
        private DateTime _gameUntil;
        private string _gameText = "";

        // ---- SCP 阵亡 ----
        private bool _scpShowing;
        private DateTime _scpUntil;
        private string _scpText = "";

        public AlertManager(Config cfg, string configPath)
        {
            _cfg = cfg;
            _configPath = configPath;
            SyncTacticalSettings();     // 小波那几个值：config → 模型

            _surfaceTheme = _cfg.EffectivePromptTheme;
            _forms = new IAlertSurface[_cfg.Alerts.Length];
            _cd = new Countdown[_cfg.Alerts.Length];
            _alertSlot = new int[_cfg.Alerts.Length];
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                _forms[i] = CreateSurface(i);
                _cd[i] = new Countdown();
            }

            I_RoundStart = IndexOf("RoundStart");
            I_RoundTimer = IndexOf("RoundTimer");
            I_ServerJoin = IndexOf("ServerJoin");
            I_GameLaunch = IndexOf("GameLaunch");
            I_Warhead = IndexOf("Warhead");
            I_Decon = IndexOf("Decontamination");
            I_Scp = IndexOf("ScpDeath");
            I_Support = IndexOf("SupportEstimate");
            I_Gen = IndexOf("GeneratorAlert");
            I_GenActive = IndexOf("GenActiveAlert");
            I_DeadMan = IndexOf("DeadManAlert");
            I_Respawn = IndexOf("RespawnAlert");
            I_Token = IndexOf("TokenAlert");
            I_Role = IndexOf("RoleAlert");
            I_Ticket = IndexOf("TicketAlert");
            I_Wave = IndexOf("WaveAlert");
            I_ScpKilled = IndexOf("ScpKilled");
            I_Startup = IndexOf("StartupAlert");
            I_LocalDeath = IndexOf("LocalDeathAlert");
            I_SelfRespawn = IndexOf("SelfRespawnAlert");

            for (int i = 0; i < _forms.Length; i++)
            {
                int captured = i;
                _forms[i].PositionChanged += delegate { HandleMoved(captured); };
            }

            _lastPos = new Point[_forms.Length];
            _layout = new FloatingLayoutManager(_cfg, _forms);

            _monitor = CreateMonitor();

            _game = new GameWatcher(_cfg.GamePollMs);
            _game.Started += OnGameStarted;
            _game.Exited += OnGameExited;

            _tick = new Timer();
            _tick.Interval = 200;
            _tick.Tick += OnTick;

            // ---- 第 4 批：阵营识别（读血条颜色，不再截角色框跑 OCR）----
            _faction = new FactionRecognizer(_cfg);
            _faction.Recognized += OnFactionRecognized;
        }

        /// <summary>阵营识别出结果（后台线程）→ 记时间段、复用原有"认出角色"提示与计时。</summary>
        private void OnFactionRecognized(FactionResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.Name)) return;
            Action work = delegate
            {
                try
                {
                    DateTime now = DateTime.Now;
                    _roleTimeline.SetRole(r.Name, now);
                    OnRoleRecognized(r.Name);
                }
                catch (Exception ex) { Logger.Err("阵营", "处理识别结果失败", ex); }
            };
            try
            {
                if (UiInvoke != null) UiInvoke(work);
                else work();
            }
            catch (Exception ex) { Logger.Err("阵营", "切线程失败", ex); }
        }

        private int IndexOf(string section)
        {
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                if (string.Equals(_cfg.Alerts[i].Section, section, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        private LogMonitor CreateMonitor()
        {
            LogMonitor m = new LogMonitor(_cfg.LogDirectory, _cfg.PollIntervalMs);
            m.LineReceived += OnLine;
            m.StatusChanged += OnStatus;
            return m;
        }

        /// <summary>一种提示当前的显示内容与是否高亮（给校准窗口 / 面板读）。</summary>
        public string CurrentText(int i)
        {
            return "";
        }

        /// <summary>把估算的时长同步进估算器（配置改了之后调用）。</summary>
        private void SyncEstimator()
        {
            // 【净化两条时间线要分清】
            //   DeconAfterRound  = 回合开始后多少秒净化倒计时开始（实测 ~50 秒）；
            //   DeconBaseSeconds = 那个倒计时本身多长（705 秒）。
            // 没有识别到任何播报时，屏幕上那条倒计时应该指向真正清生物的时刻，
            // 也就是两者的和（≈755 秒，实测"已被封锁"出现在回合开始后 ~710 秒）。
            // 以前这里只写 DeconAfterRound，于是"按回合算"的那条会提前十几分钟归零，
            // 显示成「轻收容净化已开始」而没有时间。
            _est.DeconSeconds = _cfg.DeconAfterRound + _cfg.DeconBaseSeconds;
            _est.StartOffsetSeconds = _cfg.RoundStartOffsetSeconds;
            _tactical.WaveOffsetSeconds = _cfg.WaveOffsetSeconds;
            _est.SupportSeconds = _cfg.SupportAfterRound;
            _est.WarheadSeconds = _cfg.Alerts[I_Warhead].DurationSeconds;
        }

        /// <summary>
        /// 把配置里"小波"相关的几个值推进战术模型。
        ///
        /// 【为什么要有这一步】模型里那三个字段（BigWaveWindow / SmallWaveThreshold /
        /// MiniWaveDelay）以前跟 config 里的 MiniWaveWindowSeconds / MiniWaveTickets
        /// 各写各的（两边默认值一样，所以看不出问题），改 ini 只影响一半代码。
        /// 现在统一从 config 推过去，ini 里改一次就两边都生效；
        /// MiniWaveDelaySeconds 是新加的可调项（默认 150 = 文档的 2 分 30 秒）。
        /// </summary>
        private void SyncTacticalSettings()
        {
            _tactical.BigWaveWindow = _cfg.MiniWaveWindowSeconds;
            _tactical.SmallWaveThreshold = _cfg.MiniWaveTickets;
            _tactical.MiniWaveDelay = _cfg.MiniWaveDelaySeconds;

            // 大波间隔 / 每人加时：以前写死在 TacticalLog 里，现在从配置推
            _tactical.WaveInterval = _cfg.WaveIntervalSeconds;
            _tactical.PerPlayerSeconds = _cfg.WavePerPlayerSeconds;
            _tactical.WaveOffsetSeconds = _cfg.WaveOffsetSeconds;

            // 音频片段 / 音量表也同步进来（播放时 PlayKey 要查它）
            AudioLibrary.ApplyConfig(_cfg);
        }

        /// <summary>手动切换核弹倒计时（F8）。</summary>
        public void ToggleWarheadByHotkey()
        {
            ResumeFromStop();
            _est.ToggleWarhead(DateTime.Now);
            _cd[I_Warhead].Stop();      // 交给估算器驱动
            ApplyAll();
        }

        /// <summary>核弹重置为指定秒数并继续（Shift+F8，模拟取消重启）。</summary>
        public void RestartWarheadByHotkey(int seconds)
        {
            ResumeFromStop();
            _est.RestartWarhead(DateTime.Now, seconds);
            _cd[I_Warhead].Stop();
            ApplyAll();
        }

        /// <summary>强制核弹重新起算并锁定（Ctrl+F8，模拟亡者协议，不可取消）。</summary>
        public void ForceWarheadByHotkey(int seconds)
        {
            ResumeFromStop();
            _est.ForceWarhead(DateTime.Now, seconds);
            _cd[I_Warhead].Stop();
            ApplyAll();
        }

        /// <summary>净化倒计时从指定秒数开始（F9）：老老实实从 705 秒往下走。</summary>
        public void StartDeconByHotkey(int seconds)
        {
            ResumeFromStop();
            _est.ForceDecon(DateTime.Now, seconds);
            ApplyAll();
        }

        /// <summary>
        /// OCR 认出"净化程序启动"（需求五）。
        /// 从现在开始走基础时长（默认 705 秒），并顺手用"回合里净化开始的时刻"
        /// 校准一次回合时间 —— 净化的开始时刻在回合时间轴上是固定的，
        /// 所以它能顺带把支援刷新 / 核弹 / 回合时钟一起对齐。
        /// </summary>
        public void AnnounceDeconByOcr(int baseSeconds)
        {
            ResumeFromStop();
            DateTime now = DateTime.Now;

            // 【改回"回合开始那一次"】普通净化播报不再起算倒计时 ——
            // 净化现在跟着回合时间走，OCR 不再参与校准（实测这条太不稳）。
            // 这里只把"已经开始净化"这件事记下来。
            // 真正会动倒计时的只有一条：识别到"开始清除所有生物" → 归零并锁死，
            // 走的是 LockDeconPurgeByOcr。
            _est.MarkDeconAnnouncedOnly();
            Logger.On("播报", "识别到净化相关播报 —— 净化倒计时跟着回合时间走，不单独起算");
            ApplyAll();
        }

        /// <summary>
        /// OCR 认出"开始清除所有生物"（区域真的开始锁了、清生物了）：
        /// 净化倒计时归零并锁死，之后不再接受任何修改。
        /// </summary>
        public void LockDeconPurgeByOcr()
        {
            ResumeFromStop();
            if (_est.DeconLocked) return;

            _est.LockDeconPurge();
            NotePulse("pulse.decon.purge");
            Logger.On("播报", "识别到『开始清除所有生物』→ 轻收容净化已开始，倒计时归零并锁住（不再更改）");
            ApplyAll();
        }

        /// <summary>
        /// OCR 认出"净化开始计时"的那句播报（操作者口径：
        /// 「所有生物必须立即转移」/「所有人员应该立即转移」= 开始计时的标志，不是结束）。
        ///
        /// 从这一刻起按 `seconds`（默认基础时长 705 秒）往下走，直到
        /// 「现在开始清除所有生物」把它归零锁死。
        /// 已经起过表 / 已经锁死就不再动 —— 同一段播报 OCR 常读两遍。
        /// </summary>
        public void StartDeconCountdownByOcr(int seconds)
        {
            ResumeFromStop();
            if (_est.DeconLocked) return;
            if (_est.DeconCountdownStarted) return;      // 已经起过表，别重置

            if (seconds <= 0) seconds = _cfg.DeconBaseSeconds;
            _est.AnnounceDecon(DateTime.Now, seconds);

            // 【操作者要求】开始计时这一刻要弹一条通知（像正常的倒计时提示那样）。
            // 以前只有"每分钟露一段"的策略，所以开始计时时屏幕上什么都没出现。
            // 这里给这条提示开一个"刚起表，先完整显示 DisplaySeconds 秒"的窗口；
            // 之后按用户配置走：常驻就留着，不常驻就回到每分钟露一段。
            _estPopUntil[I_Decon] = DateTime.Now.AddSeconds(
                Math.Max(6, _cfg.DisplaySeconds));

            NotePulse("pulse.decon.start");
            Logger.On("播报", "识别到『净化开始计时』→ 净化倒计时从 " + seconds
                + " 秒开始走（清除生物那句会把它归零）");
            ApplyAll();
        }

        /// <summary>
        /// OCR 按播报里的时间同步净化倒计时 —— 已经废弃。
        /// 净化改回"跟着回合时间走"之后不再需要它，保留只是为了不改调用点。
        /// </summary>
        public bool SyncDeconByOcr(int seconds)
        {
            return false;
        }

        /// <summary>
        /// OCR 按播报里的数字同步核弹倒计时（需求四）。
        ///
        /// 【口径：以识别到的秒数为准】操作者要求"识别到 X 秒后自毁"就直接采信，
        /// 所以默认用 tolerance = 0 → 每次读到都以它重新起算（不再"容差内不动"）。
        /// 想把容差调回来的话，config.ini 里 [Warhead] OcrAuthoritative=0。
        /// 没识别到核弹播报时这个方法根本不会被调用，
        /// 所以手动 F8 / Shift+F8 / Ctrl+F8 的逻辑完全不受影响。
        /// </summary>
        public bool SyncWarheadByOcr(int seconds)
        {
            DateTime now = DateTime.Now;
            double tol = _cfg.WarheadOcrAuthoritative ? 0 : _cfg.WarheadSyncTolerance;
            if (_est.SyncWarheadRemaining(now, seconds, tol))
            {
                ResumeFromStop();
                Logger.On("播报", "核弹倒计时按播报校准为 " + seconds + " 秒"
                    + (_cfg.WarheadOcrAuthoritative ? "（以播报为准）" : "（容差 " + tol + " 秒）"));
                ApplyAll();
                return true;
            }
            // 容差内不动：也记一笔，方便核对"为什么秒数没跳"
            Logger.On("播报", "核弹播报 " + seconds + " 秒与当前倒计时相差不到 "
                + tol + " 秒 → 容差内，不改");
            return false;
        }

        /// <summary>
        /// OCR 读到「引爆程序已被取消」：停掉核弹倒计时，并保留剩余秒数。
        ///
        /// 和日志里的 WarheadCancelled 走同一套口径：
        ///   · 已进入最后 10 秒（Detonation Inevitable）时取消无效，忽略；
        ///   · 正常取消只停表，剩余时间留着 —— 之后游戏播「核弹重新启动，
        ///     倒计时 N 秒」时会按播报的 N 秒重新起算。
        /// </summary>
        public bool CancelWarheadByOcr()
        {
            if (!_est.WarheadRunning)
            {
                Logger.On("播报", "读到『引爆程序已被取消』，但核弹本来就没在走 → 忽略");
                return false;
            }
            if (WarheadInevitable)
            {
                Logger.On("播报", "读到核弹取消播报，但已进入最后 10 秒（Detonation Inevitable）→ 忽略");
                return false;
            }

            DateTime now = DateTime.Now;
            double remain = _est.WarheadRemaining;
            _cd[I_Warhead].Stop();
            _est.ToggleWarhead(now);   // 停表并保留剩余时间
            Logger.On("播报", "读到『引爆程序已被取消』→ 核弹倒计时已停，保留剩余 "
                + (int)remain + " 秒；之后再读到重启播报会重新起算");
            ApplyAll();
            return true;
        }

        /// <summary>手动弹一条“SCP 已阵亡，注意支援刷新”（F6）。</summary>
        public void ShowScpDeadByHotkey()
        {
            ResumeFromStop();
            DateTime now = DateTime.Now;
            _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                new object[] { "SCP", "未知", "已阵亡" });
            _scpShowing = true;
            _scpUntil = now.AddSeconds(Math.Max(4, _cfg.DisplaySeconds));
            _cd[I_Support].Begin(now, _cfg.Alerts[I_Support].DurationSeconds);
            ApplyAll();
        }

        /// <summary>校准之后立刻刷新显示。</summary>
        public void OnCalibrated()
        {
            ApplyAll();
        }

        /// <summary>
        /// 把某个提示钉到屏幕正上方居中（回合时间用）。
        /// 先让它按当前文字量一次，拿到真实宽高再居中，否则会偏。
        /// </summary>
        private void PinTopCenter(int idx)
        {
            AlertConfig a = _cfg.Alerts[idx];
            if (!a.PinTopCenter) return;

            IAlertSurface f = _forms[idx];
            f.Pop(SafeFormat(a.Format, new object[] { "00:00" }));   // 先撑出尺寸
            Size sz = f.SurfaceSize;
            if (sz.Width <= 0 || sz.Height <= 0) return;

            Screen sc = Screen.PrimaryScreen;
            Rectangle wa = sc.WorkingArea;
            int x = wa.Left + (wa.Width - sz.Width) / 2;
            int y = wa.Top + 8;      // 离顶部留一点点

            f.SetRestLocation(x, y);
            _lastPos[idx] = new Point(x, y);
        }

        public void Start()
        {
            ApplyAllStyles();
            SyncEstimator();
            SyncLogKeys();

            // 用户自备音频：定位文件夹 + 把现有音频列一遍（都写进日志，方便排查）
            try
            {
                string adir = AudioLibrary.ResolveDir(_cfg.AudioDir);
                AudioLibrary.Master = _cfg.SoundEnabled;
                string[] clips = AudioLibrary.List();
                Logger.On("音频", "音频文件夹：" + (adir == null ? "(无法定位)" : adir)
                    + "；可用音频 " + clips.Length + " 个"
                    + (clips.Length > 0
                        ? "：" + string.Join("、", clips)
                        : "（往里丢 wav/mp3 等文件，再到 config.ini 的 [General] AudioDefault "
                          + "或各条提示的 AudioFile 里写文件名）"));
            }
            catch (Exception ex) { Logger.Err("音频", "初始化音频失败（忽略）", ex); }

            _monitorDir = _cfg.LogDirectory;
            _monitorPoll = _cfg.PollIntervalMs;
            _monitor.Start();
            _game.Start();
            if (_game.IsRunning)
            {
                bool inFacility;
                if (_monitor.TryGetCurrentClientFacility(out inFacility) && inFacility)
                {
                    EnterServer("启动时从 Player.log 确认仍在 Facility 场景");
                    StatusText = "监控中：启动时确认游戏已在设施场景";
                }
                else
                {
                    Logger.On("启动", "检测到游戏进程正在运行，但客户端日志没有确认当前处于 Facility 场景；暂不把主菜单当作对局");
                }
            }
            _tick.Start();
            ApplyAll();
        }

        /// <summary>把“回合正式开始”的关键字喂给日志解析器。</summary>
        private void SyncLogKeys()
        {
            try
            {
                string raw = _cfg.RoundStartKeys;
                if (string.IsNullOrEmpty(raw)) { LogParser.StartKeys = new string[0]; return; }

                string[] parts = raw.Split(new char[] { ',', '，', ';', '；', '|' });
                System.Collections.Generic.List<string> list =
                    new System.Collections.Generic.List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = parts[i].Trim();
                    if (s.Length > 0) list.Add(s);
                }
                LogParser.StartKeys = list.ToArray();
            }
            catch { LogParser.StartKeys = new string[0]; }
        }

        #region 位置

        private void HandleMoved(int index)
        {
            Point cur = _forms[index].RestLocation;
            int dx = cur.X - _lastPos[index].X;
            int dy = cur.Y - _lastPos[index].Y;
            if (dx == 0 && dy == 0) return;

            if (_globalMove) MoveAllBy(dx, dy);
            else _lastPos[index] = cur;

            SavePositions();
        }

        private void MoveAllBy(int dx, int dy)
        {
            for (int i = 0; i < _forms.Length; i++)
            {
                Point p = _forms[i].RestLocation;
                Point np = new Point(p.X + dx, p.Y + dy);
                _forms[i].SetRestLocation(np.X, np.Y);
                _lastPos[i] = np;
            }
        }

        /// <summary>把各窗口当前位置写回配置（减去整体偏移）。</summary>
        private void SavePositions()
        {
            for (int i = 0; i < _forms.Length; i++)
            {
                _cfg.Alerts[i].Style.X = _forms[i].RestLocation.X - _cfg.OffsetX;
                _cfg.Alerts[i].Style.Y = _forms[i].RestLocation.Y - _cfg.OffsetY;
                _lastPos[i] = _forms[i].RestLocation;
            }
            _cfg.Save(_configPath);
        }

        public void SaveConfig()
        {
            SavePositions();
        }

        private void ApplyAllStyles()
        {
            for (int i = 0; i < _forms.Length; i++)
            {
                _forms[i].ApplyStyle(_cfg.Alerts[i].Style, _cfg);
                _lastPos[i] = _forms[i].RestLocation;
            }
        }

        /// <summary>
        /// 按当前设置造一条提示。新版走 WPF 的 A.R.C.T 卡片，旧版走原来的 OverlayForm；
        /// 业务逻辑（什么时候弹、弹什么字、倒计时）完全共用。
        /// </summary>
        private IAlertSurface CreateSurface(int i)
        {
            if (_cfg.EffectivePromptTheme == 1)
                return new WpfAlertOverlay(_cfg, _cfg.Alerts[i]);
            return new OverlayForm(_cfg.Alerts[i].DisplayName);
        }

        /// <summary>
        /// 提示主题变了：把旧窗口的位置写回配置，销毁旧表面，按新主题重建。
        /// 位置、开关、倒计时都不丢，只是“画法”换了。
        /// </summary>
        private void SyncPromptTheme()
        {
            int want = _cfg.EffectivePromptTheme;
            if (want == _surfaceTheme) return;

            try
            {
                SavePositions();       // 先把旧窗口当前位置写回 cfg.Alerts[i].Style
            }
            catch { }

            IAlertSurface[] old = _forms;
            _forms = new IAlertSurface[_cfg.Alerts.Length];
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                _forms[i] = CreateSurface(i);

            if (old != null)
            {
                for (int i = 0; i < old.Length; i++)
                {
                    try { old[i].HideNow(); } catch { }
                    try { old[i].Close(); } catch { }
                    try { old[i].Dispose(); } catch { }
                }
            }

            for (int i = 0; i < _forms.Length; i++)
            {
                int captured = i;
                _forms[i].PositionChanged += delegate { HandleMoved(captured); };
            }
            _lastPos = new Point[_forms.Length];
            if (_layout != null) _layout.SetSurfaces(_forms);
            _surfaceTheme = want;
            Logger.On("提示", "提示主题已切换为" + (want == 1 ? "新版 A.R.C.T" : "旧版经典")
                + "（" + _forms.Length + " 条提示已重建）");
        }

        /// <summary>设置窗口改完配置后调用：立即生效。</summary>
        public void ApplyConfig()
        {
            SyncPromptTheme();
            ApplyAllStyles();
            SyncEstimator();
            SyncTacticalSettings();
            SyncLogKeys();

            // 本程序的窗口要不要"对截图/录屏不可见"（防止自己被自己的 OCR 认进去）。
            // 关掉之后录屏正常、也不会有「受保护的应用程序阻止录制」那个系统提示。
            bool wasHide = NativeMethods.CaptureHideEnabled;
            NativeMethods.CaptureHideEnabled = _cfg.HideFromCapture;
            if (wasHide && !_cfg.HideFromCapture)
            {
                // 从"隐藏"改成"不隐藏"：把已经弹出来的那些窗口上的旧标志清掉，
                // 否则操作者会觉得"勾掉了没生效"（系统那条提示还在、录屏还是录不到）。
                int n = NativeMethods.ShowAllInCapture();
                Logger.On("OCR", "已按设置恢复窗口的录制可见性（清了 " + n + " 个窗口）");
            }

            // 只有日志目录 / 轮询间隔真的变了才重建监视器，
            // 否则每次调样式都会丢掉读取位置。
            bool needRebuild = _monitor == null
                || !string.Equals(_monitorDir, _cfg.LogDirectory, StringComparison.OrdinalIgnoreCase)
                || _monitorPoll != _cfg.PollIntervalMs;

            if (needRebuild)
            {
                if (_monitor != null)
                {
                    _monitor.LineReceived -= OnLine;
                    _monitor.StatusChanged -= OnStatus;
                    _monitor.Dispose();
                }
                _monitor = CreateMonitor();
                _monitorDir = _cfg.LogDirectory;
                _monitorPoll = _cfg.PollIntervalMs;
                _monitor.Start();
            }

            if (_editMode) SetEditMode(true);
            else ApplyAll();
        }

        private string _monitorDir;
        private int _monitorPoll = -1;

        #endregion

        #region 事件来源

        private void OnGameStarted()
        {
            _gameShowing = true;
            NotePulse("pulse.game.start");
            _gameUntil = DateTime.Now.AddSeconds(_cfg.DisplaySeconds);
            _gameText = SafeFormat(_cfg.Alerts[I_GameLaunch].Format, new object[] { "" });
            StatusText = "监控中：游戏正在运行";

            // 第 5 批：同一次启动只发一次（GameWatcher 本身只在状态跳变时触发）
            if (GameStarted != null) { try { GameStarted(); } catch { } }
        }

        private void OnGameExited()
        {
            // 游戏退了 = 肯定不在服务器里了（操作者要求：不在服务器就不弹那些计时）
            LeaveServer("游戏退出");
            NotePulse("pulse.game.exit");
            _roundActive = false;
            SetPreparing(false);

            _gameShowing = true;
            _gameUntil = DateTime.Now.AddSeconds(_cfg.DisplaySeconds);
            string alt = _cfg.Alerts[I_GameLaunch].AltText;
            if (string.IsNullOrEmpty(alt)) alt = _cfg.Alerts[I_GameLaunch].Format;
            _gameText = SafeFormat(alt, new object[] { "" });
            StatusText = "游戏已退出";

            // 通知外层：游戏退了（Program 用它决定要不要一起退）
            if (GameExited != null) GameExited();
        }

        private void OnLine(string line)
        {
            LogEvent ev = LogParser.Parse(line);
            if (ev.Kind == LogEventKind.None) return;

            // 有新事件就解除"停止全部计时"状态，让提示重新出来
            ResumeFromStop();

            DateTime now = DateTime.Now;

            switch (ev.Kind)
            {
                case LogEventKind.RoundPreparing:
                    {
                        // 【操作者要求】进图 = 新的一局 → 先把上一局的数据清干净
                        // （重连、换服都会走到这里；清完再按倒计时/兜底开始计时）
                        EnterServer("进图");
                        ClearAllRoundData("进图（SceneLoaded 2. Facility）");

                        // 【最准的一路】服务器自己在数"还有几秒开局"
                        // （很多服有，形如「大厅倒计时: 15秒」一路数到 0秒）。
                        // 那就老实等它数到 0（0 秒那条会作为 RoundStarted 进来），
                        // 不要再用固定时长去猜 —— 猜出来的开局时刻必然偏早或偏晚。
                        if (ev.Seconds > 0)
                        {
                            // 【踩过的坑】以前"已经在准备中"就整条忽略，于是：
                            //   进图后第 9 秒来了「大厅倒计时 53 秒」→ 被忽略 →
                            //   第 10 秒照样按「分配角色时长」开始计时，
                            //   比真正开局早了将近一分钟（日志实锤）。
                            // 现在只要倒计时指向的时刻比现有估算更晚，就按它校准；
                            // 只有它更早（几乎不会）才不动。
                            _sawLobbyCountdown = true;
                            DateTime byCountdown = now.AddSeconds(ev.Seconds + 2);
                            if (!_roundPreparing)
                            {
                                SetPreparing(true);
                                _roundStartAt = byCountdown;
                                _roundStartFromCountdown = true;
                                StatusText = "开局倒计时 " + ev.Seconds + " 秒";
                                Logger.On("回合", "服务器开局倒计时 " + ev.Seconds
                                    + " 秒 → 等它数到 0 再开始正式计时");
                            }
                            else if (!_roundStartFromCountdown || byCountdown > _roundStartAt)
                            {
                                _roundStartAt = byCountdown;
                                _roundStartFromCountdown = true;
                                StatusText = "开局倒计时 " + ev.Seconds + " 秒";
                                Logger.On("回合", "服务器开局倒计时 " + ev.Seconds
                                    + " 秒比原来的估算晚 → 按倒计时校准开局时刻（原来的估算作废）");
                            }
                            break;
                        }

                        // 这台服务器没有开局倒计时 → 退回"进图 + 分配角色时长"的老办法。
                        // （这行"进图"出现时其实是在分配角色，玩家还要等几秒才拿到角色。）
                        // 期间只要命中"回合开始标志"，RoundStarted 那条会立刻接管。
                        int delay = _cfg.RoundStartDelay;
                        _lastRoundSignalAt = now;
                        if (delay <= 0)
                        {
                            DoRoundStart(now, "进入设施场景（没设分配角色时长 → 进图就计时）");
                        }
                        else
                        {
                            // 【为什么要多给几秒】很多服在"进图"之后十来秒才吐第一条
                            // 「大厅倒计时」。如果一到点就按固定时长开始计时，
                            // 那条倒计时就永远赶不上（日志实锤：进图后 11 秒才来倒计时，
                            // 而这里第 10 秒就开始计时了 → 整局时间早了一分钟）。
                            // 多等这几秒，"倒计时"那一路就能接手，而且不会重复开始两次。
                            //
                            // 【这一台服认过一次倒计时之后】下一局干脆等满
                            // LobbyCountdownWaitSeconds 秒（倒计时一旦出现就会立刻接管），
                            // 免得"进图就开始计时"再来一次。
                            int wait = delay + RoundStartGraceSeconds;
                            if (_sawLobbyCountdown && wait < LobbyCountdownWaitSeconds)
                                wait = LobbyCountdownWaitSeconds;

                            _roundStartFromCountdown = false;
                            _roundStartAt = now.AddSeconds(wait);
                            SetPreparing(true);
                            StatusText = "分配角色中…" + delay + " 秒后开始计时";
                            Logger.On("回合", "进图 = 开始分配角色：等 " + wait + " 秒再开始计时"
                                + "（其中 " + delay + " 秒是「分配角色时长」，其余留给服务器开局倒计时；"
                                + "这期间出现回合开始标志 / 开局倒计时会立刻接管）");
                        }
                        break;
                    }

                case LogEventKind.RoundStarted:
                    {
                        _lastRoundSignalAt = now;   // 刚开局 → 不弹校准窗口
                        bool wasPreparing = _roundPreparing;
                        SetPreparing(false);
                        DoRoundStart(now, ev.Seconds == 0
                            ? "服务器开局倒计时数到 0"
                            : (wasPreparing ? "识别到“回合正式开始”标志" : "日志事件"));
                        break;
                    }

                case LogEventKind.RoundFinished:
                    EndDeathScan("回合结束");
                    _roundActive = false;
                    SetPreparing(false);
                    _est.Stop();
                    // 【操作者要求】回合结束 / 离开服务器 / 回合重启 → 本局数据全部清空
                    ClearAllRoundData("回合结束或离开服务器");
                    NotePulse("pulse.round.end");
                    LeaveServer("回合结束 / 回主菜单");
                    break;

                case LogEventKind.PlayerJoined:
                    // 【操作者要求】进入服务器（"Hi 名字! You have been authenticated"）
                    // → 上一局留下来的数据全部清空，否则面板上还挂着别的服的数据。
                    // 换服/重开一局时回合状态也要归零（新服还没开局）；
                    // 这里显式置 false，而不是让 ClearAllRoundData 去动它 ——
                    // 那个方法还会被"进图"调用，一局中途不能被打断。
                    _roundActive = false;
                    SetPreparing(false);
                    EnterServer("进服：" + ev.Player);
                    ClearAllRoundData("进入服务器（" + ev.Player + "）");
                    // SCP 扫描：进服先限时扫一段，超时自动停；之后第一次死亡再常驻
                    if (_cfg.ScpScanJoinSeconds > 0) ArmScpScan("进入服务器（限时 " + _cfg.ScpScanJoinSeconds + " 秒）", _cfg.ScpScanJoinSeconds); else DisarmScpScan("进入服务器（等第一次死亡再开始）");
                    NotePulse("pulse.server.join");

                    _joinText = SafeFormat(_cfg.Alerts[I_ServerJoin].Format, new object[] { ev.Player });
                    _joinShowing = true;
                    _joinUntil = now.AddSeconds(_cfg.DisplaySeconds);

                    // 中途加入时本地时间不准，弹校准窗口让用户填一次。
                    // 【但刚开局不弹】日志里刚刚出现过"回合开始"的话，计时已经很准，
                    // 再弹校准窗口只会打扰人。
                    // 用"最近一次回合信号（进图 / 回合开始）"来判断，而不是 _roundActive：
                    // 因为"加入服务器"往往比"回合开始"日志更早到，那时 _roundActive 还是 false，
                    // 用 _roundActive 判断就会照样弹出来。
                    bool justStarted = _lastRoundSignalAt != DateTime.MinValue
                                    && (now - _lastRoundSignalAt).TotalSeconds < 150;
                    if (_cfg.AutoCalibrateOnJoin && CalibrateRequested != null && !justStarted)
                    {
                        try { CalibrateRequested(); } catch { }
                    }
                    else if (justStarted)
                    {
                        Logger.On("回合", "刚开局（回合开始不到 2 分钟）→ 不弹回合时间校准窗口");
                    }
                    break;

                case LogEventKind.WarheadCountdown:
                    // 服务端日志能直接看到核弹开始，这里就用准确值
                    _cd[I_Warhead].Stop();
                    _est.StartWarhead(now, _cfg.Alerts[I_Warhead].DurationSeconds);
                    break;

                case LogEventKind.WarheadCancelled:
                    // 取消：保留剩余时间，下次启动从这里继续。
                    // 文档：最后 10 秒不可能关掉（屏幕会显示 Detonation Inevitable），
                    // 所以那一段里的取消请求直接忽略。
                    _cd[I_Warhead].Stop();
                    if (WarheadInevitable)
                        Logger.On("核弹", "已进入最后 10 秒（Detonation Inevitable）→ 忽略取消");
                    else
                        _est.ToggleWarhead(now);
                    break;

                case LogEventKind.WarheadDetonated:
                    {
                        _est.StopWarhead();
                        _est.ResetWarhead();
                        // 文档：引爆后下一次重生波强制 4 分钟冷却
                        OnWarheadDetonated();
                        Countdown st = _cd[I_Warhead];
                        st.Counting = false;
                        st.Ended = true;
                        st.EndUntil = now.AddSeconds(_cfg.DisplaySeconds);
                        st.PulseUntil = DateTime.MinValue;
                        break;
                    }

                case LogEventKind.DecontaminationStarted:
                    // 服务端日志能直接看到净化开始：把估算的起点对齐过去
                    _est.CalibrateRoundElapsed(now, _est.DeconSeconds);
                    break;

                case LogEventKind.ScpDeath:
                    _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                        new object[] { ev.Scp, ev.Killer, ev.Reason });
                    _scpShowing = true;
                    _scpUntil = now.AddSeconds(_cfg.DisplaySeconds);

                    // 支援刷新估算（不精确，仅提醒）
                    _cd[I_Support].Begin(now, _cfg.Alerts[I_Support].DurationSeconds);
                    break;

                // 客户端日志里能看到“自己死了”，带上死因
                case LogEventKind.LocalDeath:
                    BeginDeathScan(now);   // 需求八：死亡界面有票数，开始扫

                    // SCP 扫描新逻辑：进服后的第一次死亡 → 开始持续扫描左侧 SCP 列表
                    if (_inServer) ArmScpScan("进服后的第一次死亡");

                    // 【自己阵亡】这条单独弹（操作者要求：死亡时要能配一段自己的音频）
                    _selfDead = true;
                    _selfDeathAt = now;
                    // 第 4 批：死亡 = 角色时间段切到 dead，并停止正在跑的阵营识别
                    //（死亡界面没有血条，继续扫只会把全黑当成 079）
                    try
                    {
                        _roleTimeline.SetState("dead", now);
                        if (_faction != null) _faction.Stop();
                        if (SelfDeath != null) SelfDeath();
                    }
                    catch (Exception ex) { Logger.Err("阵营", "记录死亡时间段失败", ex); }
                    _deathPairFirstRedraw = _lastGameWindowRedraw != DateTime.MinValue
                        && (now - _lastGameWindowRedraw).TotalSeconds <= 0.45
                        ? _lastGameWindowRedraw : DateTime.MinValue;
                    _deathTailPending = true;
                    _lastRespawnWindowRedraw = DateTime.MinValue;
                    NotePulse("pulse.self.death");
                    _isDeadNow = true;
                    _selfRespawned = false;
                    _selfDeathText = SafeFormat(_cfg.Alerts[I_LocalDeath].Format,
                        new object[] { ev.Reason });
                    _selfDeathShowing = true;
                    _selfDeathUntil = now.AddSeconds(_cfg.DisplaySeconds);
                    Logger.On("提示", "自己阵亡（" + ev.Reason + "）→ 弹「" + _cfg.Alerts[I_LocalDeath].DisplayName
                        + "」，并开始判复活（前台 OCR + 游戏窗口重绘；后台窗口重绘）");

                    _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                        new object[] { "自己", "自己", ev.Reason });
                    _scpShowing = true;
                    _scpUntil = now.AddSeconds(_cfg.DisplaySeconds);
                    _cd[I_Support].Begin(now, _cfg.Alerts[I_Support].DurationSeconds);
                    break;

                // 客户端广播里能看到“SCP-XXX 已掉线 / 补位成功”
                case LogEventKind.ScpDisconnected:
                    _scpText = SafeFormat(_cfg.Alerts[I_Scp].Format,
                        new object[] { ev.Scp, "掉线", ev.Reason });
                    _scpShowing = true;
                    _scpUntil = now.AddSeconds(_cfg.DisplaySeconds);
                    _cd[I_Support].Begin(now, _cfg.Alerts[I_Support].DurationSeconds);
                    break;
            }
        }

        private void OnStatus(string s)
        {
            StatusText = s;
        }

        #endregion

        #region 刷新显示

        /// <summary>
        /// 回合正式开始：从这里开始计时。
        ///
        /// 触发它的可能是
        ///   · 进入设施场景（没设准备时长时，保持老行为）；
        ///   · 准备时长走完；
        ///   · 日志里出现了 RoundStartKeys 里的“回合正式开始”标志。
        /// why 会写进日志，方便用户确认到底是被哪个条件触发的。
        /// </summary>
        private void DoRoundStart(DateTime now, string why)
        {
            DoRoundStart(now, why, 0);
        }

        /// <summary>
        /// 正式开始计时。
        /// initialElapsed：一开始就认为"本回合已经进行了多少秒"——
        /// 准备阶段超时那条路会传配置的准备时间（需求七.3），正常是 0。
        /// </summary>
        private void DoRoundStart(DateTime now, string why, double initialElapsed)
        {
            if (initialElapsed < 0) initialElapsed = 0;

            _sawObserverThisRound = false;     // 新回合重新数"见过观察者没有"
            _roundStartFromCountdown = false;
            _midJoinRound = false;               // 正常开局（含倒计时校准）→ 显示回合时间
            _calibPromptsLeft = 2;
            _calibResolved = false;
            _isDeadNow = false;                // 新回合开始 → 活着（支援刷新可以弹了）
            _selfDead = false;                 // 回合开始角色已分配，旧回合的死亡标记不能留着
            _deadManOcrAt = DateTime.MinValue;   // 新回合：终焉协议的屏幕识别也归零
            _deadManCompleted = false;
            _deadManDoneUntil = DateTime.MinValue;
            _deadManNotBothOutSince = DateTime.MinValue;

            NotePulse("pulse.round.start");
            ResetAudioTriggers();   // 新回合：一局一次的触发点重新武装
            _roundCounter++;
            _roundStartText = SafeFormat(_cfg.Alerts[I_RoundStart].Format, new object[] { _roundCounter });
            _roundStartShowing = true;
            _roundStartUntil = now.AddSeconds(_cfg.DisplaySeconds);

            _roundActive = true;
            _roundStart = now;
            _roundPopUntil = now.AddSeconds(_cfg.DisplaySeconds);

            // 第 4 批：新一局 → 角色时间段从 0 开始，并立刻认一次阵营
            //（"角色分配完成"就是这一刻；识别最多 3 秒，认不出就记未知）
            try
            {
                _roleTimeline.BeginRound(_roundCounter, now);
                if (_faction != null) { ShowRoleBusy(); _faction.Begin("角色分配完成", 0, 0); }
            }
            catch (Exception ex) { Logger.Err("阵营", "开局识别启动失败", ex); }

            // 新回合，清掉上一局的倒计时
            _cd[I_Warhead].Stop();
            _cd[I_Decon].Stop();
            _cd[I_Support].Stop();
            _scpShowing = false;

            // 时间估算：正常从 0 开始；超时进来的话带上估算出来的已进行时间
            SyncEstimator();
            _est.BeginRound(now, (int)Math.Round(initialElapsed));
            _est.ResetWarhead();
            ResetEstimateVisuals();

            // 战术记录也跟着换新一局
            _tactical.BeginRound(now);
            // 新一局的票数从 0 开始，令牌阈值要能重新触发（需求二）
            ResetTokenState();
            // 【操作者要求】分配角色结束、正式开始计时这一刻，上一局的残留也要清
            //（config 里那些面板直接读的键；模型上面 BeginRound 已经清了）
            ClearRoundConfigKeys();
            // 提示音的“最短间隔”状态也重置，新一局第一声要能响
            AlertSound.Reset();

            // 顶部居中的“本回合已进行 00:00”
            PinTopCenter(I_RoundTimer);

            Logger.On("回合", "第 " + _roundCounter + " 回合开始计时（触发原因：" + why + "）"
                + (initialElapsed > 0 ? "，起始已进行 " + (int)initialElapsed + " 秒" : ""));
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_scpScanArmed && _scpScanUntil != DateTime.MinValue && DateTime.Now >= _scpScanUntil)
                DisarmScpScan("限时扫描到点（" + _scpScanUntilWhy + "）");
            //
            // 【为什么是从 0 算】这段时长的含义就是"进图之后还要多久玩家才拿到角色"，
            // 所以时间一到、回合就算正式开始，计时从 0 起。
            // （以前这里会按一个"准备时间"往回估一个时刻，等于把钟拨到往前 30 秒；
            //   那个估算和「分配角色时长」是同一个意思、会互相打架，已经撤掉。）
            if (_roundPreparing && DateTime.Now >= _roundStartAt)
            {
                DateTime now = DateTime.Now;
                SetPreparing(false);

                // 【日志要说实话】这一条以前一律写"分配角色时长结束"，于是
                // 操作者一直以为"是靠固定时长猜的"。其实多数时候 _roundStartAt
                // 已经被服务器倒计时校准过了 —— 那两件事走的是同一个到点检查。
                if (_roundStartFromCountdown)
                {
                    Logger.On("回合", "开局时刻到（本次是按服务器开局倒计时校准的）→ 从现在开始正式计时");
                    DoRoundStart(now, "服务器开局倒计时校准的时刻");
                }
                else
                {
                    Logger.On("回合", "分配角色时长结束（进图后 " + _cfg.RoundStartDelay
                        + " 秒 + 宽限内既没等到回合开始标志，也没等到开局倒计时）→ 从现在开始正式计时");
                    DoRoundStart(now, "分配角色时长结束");
                }
            }
            CheckTokenThresholds(DateTime.Now);
            CheckDeconFallback(DateTime.Now);
            CheckDeadManSwitch(DateTime.Now);
            CheckGeneratorActivations(DateTime.Now);
            CheckRespawnChance(DateTime.Now);

            // 发电机到点自动标"已完全启动"。
            //
            // 【为什么要放在这里】这一步以前只有战术面板内部那个定时器会做，
            // 等于"你不把面板唤出来，发电机永远不会到点"——
            // 面板是模型的一个视图，模型的推进不该依赖视图有没有显示。
            // 放在这里之后，面板开不开、停在哪一页都不影响模型。
            // （TickGenerators 只翻标志位，不发影响力，重复调用没有副作用。）
            _tactical.TickGenerators(DateTime.Now);

            ApplyAll();
        }

        // ============================================================
        // O5 核弹 / 亡者协议（机制\核弹.txt 最后一段）
        //
        // 文档原文：
        //   "A 2.5 minute countdown will begin when both teams run out respawns.
        //    If another respawn is gained during this countdown then it will reset.
        //    Mini-waves will only set the progress back by 40 seconds.
        //    If the timer reaches zero the Dead Man's Switch will activate and
        //    start the warhead countdown. This process can not be stopped once
        //    activated. Upon activation, all SCPs will have their health restored
        //    to 25% of their max HP if below that amount."
        //
        // 也就是：
        //   ① 两方重生令牌都用尽 → 起一个 2 分 30 秒 的亡者协议倒计时；
        //   ② 期间任何一方拿到新令牌 → 整个倒计时重置；
        //   ③ 小波只会把进度往后退 40 秒（不是重置）；
        //   ④ 软件只显示亡者协议倒计时；归零时结束倒计时，不操作核弹计时。
        // ============================================================

        /// <summary>亡者协议倒计时的总时长（秒，文档 2.5 分钟）。</summary>
        public const int DeadManSeconds = TicketModel.DeadManSeconds;

        /// <summary>当前配置的终焉总时长（0 或没配 → 文档默认 150）。</summary>
        private double DeadManTotalSeconds
        {
            get { return _cfg != null && _cfg.DeadManSeconds > 0 ? _cfg.DeadManSeconds : DeadManSeconds; }
        }

        /// <summary>小波把亡者协议进度往回退的秒数（文档 40 秒）。</summary>
        public const int DeadManMiniWaveSetback = TicketModel.DeadManMiniWaveSetback;

        /// <summary>核弹引爆后，下一次重生波被强制推迟到这个冷却（秒，文档 4 分钟）。</summary>
        public const int PostDetonationWaveCooldown = TicketModel.PostDetonationWaveCooldown;

        /// <summary>亡者协议倒计时开始时刻（MinValue = 没在走）。</summary>
        private DateTime _deadManAt = DateTime.MinValue;

        /// <summary>本局的终焉倒计时已经自然结束；令牌仍耗尽时不要自动重新起表。</summary>
        private bool _deadManCompleted;

        /// <summary>两方都没令牌的状态是不是刚刚从"有"变成"没有"。</summary>
        private bool _wasBothOut;

        /// <summary>亡者协议开始后已经看到过几条刷新记录（用来发现新出现的小波）。</summary>
        private int _deadManWaveSeen;

        /// <summary>"因为还欠小波 / 概率还高，所以暂不启动亡者协议"这条日志只写一次。</summary>
        private bool _deadManBlockedLogged;

        /// <summary>亡者协议倒计时还剩多少秒；-1 = 没在走。</summary>
        public double DeadManRemaining
        {
            get
            {
                if (_deadManAt == DateTime.MinValue) return -1;
                double r = DeadManTotalSeconds - (DateTime.Now - _deadManAt).TotalSeconds;
                return r < 0 ? 0 : r;
            }
        }

        /// <summary>亡者协议是不是正在进行。</summary>
        public bool DeadManActive
        {
            get { return _deadManAt != DateTime.MinValue; }
        }

        /// <summary>本局亡者协议的总时长（秒），给性能报告画事件带用。</summary>
        public double DeadManTotal
        {
            get { return DeadManTotalSeconds; }
        }

        /// <summary>由战术面板停止核弹和亡者协议倒计时。</summary>
        public void StopSpecialCountdowns()
        {
            bool warheadWasRunning = _est.WarheadRunning;
            bool deadManWasRunning = DeadManActive;
            _est.CancelWarhead();
            _cd[I_Warhead].Stop();
            _wasWarheadAudio = false;
            _deadManAt = DateTime.MinValue;
            _deadManOcrAt = DateTime.MinValue;
            _deadManDoneUntil = DateTime.MinValue;
            _deadManNotBothOutSince = DateTime.MinValue;
            _deadManText = "";
            _deadManUntil = DateTime.MinValue;
            if (deadManWasRunning || (_tactical != null && _tactical.BothOutOfTokens))
            {
                _deadManCompleted = true;
                _wasBothOut = true;
            }
            _deadManBlockedLogged = false;
            Logger.On("计时", "面板停止特殊计时：核弹" + (warheadWasRunning ? "已停止" : "未运行")
                + "，亡者协议" + (deadManWasRunning ? "已停止" : "未运行"));
            ApplyAll();
        }

        /// <summary>屏幕上认到过"终焉协议"播报（给自定义触发点当条件）。</summary>
        private DateTime _deadManOcrAt = DateTime.MinValue;

        /// <summary>
        /// OCR 认到"终焉协议 / 亡者协议"的播报。
        ///
        /// 【2026-10-31 口径纠正】这句播报出现时，游戏里的 2 分 30 秒等待已经走完、
        /// 终焉已经激活（紧接着就是核弹倒计时）。所以这里**不能**从这一刻重新起
        /// 150 秒 —— 那会显示成"终焉开始了才开始计时"。正确做法是：
        ///   · 等待表在走 → 立即归零并结束；
        ///   · 等待表没在走（票数/小波没识别到）→ 只记录播报，不凭空起表。
        /// 播报本身仍保留给音频条件：「屏幕上认到过终焉协议」/「刚刚认到终焉协议」。
        /// </summary>
        /// <summary>当前是不是中途加入（时间未校准）。</summary>
        public bool IsMidJoinRound { get { return _midJoinRound; } }

        /// <summary>
        /// OCR 读到轻收容播报里的分钟数（15/10/5/1）。
        /// 只在中途加入时弹校准提示；正常开局一律不弹。
        /// </summary>
        public void NoteDeconCalibNumberByOcr(int minutes, DateTime readAt)
        {
            if (!_midJoinRound || _cfg == null || !_cfg.CalibPromptEnabled || !_cfg.CalibNumberPrompt) return;
            if (_calibResolved || _calibPromptsLeft <= 0 || CalibPrompt.Active) return;
            if (minutes <= 0) return;

            int a = minutes;
            bool exact = a == 15 || a == 10 || a == 5 || a == 1;
            if (exact)
            {
                StartCalibPrompt("识别到轻收容剩余 " + a + " 分钟",
                    "软件将在 " + _cfg.CalibPromptSeconds + " 秒后按这个时间校准",
                    "", "", a, 0, readAt);
                return;
            }

            int hi, lo;
            if (a > 15) { hi = 15; lo = 10; }
            else if (a < 1) { hi = 1; lo = 1; }
            else if (a > 10) { hi = 15; lo = 10; }
            else if (a > 5) { hi = 10; lo = 5; }
            else { hi = 5; lo = 1; }

            string keyA = BaseCalibKey();
            string keyB = NextFKey(keyA);
            StartCalibPrompt("识别到轻收容剩余 " + a + " 分钟（不确定）",
                keyA + " 用 " + hi + " 分钟 / " + keyB + " 用 " + lo + " 分钟校准",
                keyA, keyB, hi, lo, readAt);
        }

        /// <summary>OCR 读到“开始清除所有生物 / 开始封锁” → 按净化起点校准。</summary>
        public void NoteDeconCalibLockByOcr(DateTime readAt)
        {
            if (!_midJoinRound || _cfg == null || !_cfg.CalibPromptEnabled || !_cfg.CalibLockPrompt) return;
            if (_calibResolved || _calibPromptsLeft <= 0 || CalibPrompt.Active) return;
            StartCalibPrompt("识别到轻收容开始封锁",
                "软件将在 " + _cfg.CalibPromptSeconds + " 秒后校准回合时间",
                "", "", 0, 0, readAt);
        }

        private string BaseCalibKey()
        {
            string k = "F7";
            try { if (_cfg != null && !string.IsNullOrEmpty(_cfg.HotkeyCalibrate)) k = _cfg.HotkeyCalibrate; } catch { }
            int p = k.LastIndexOf('+');
            if (p >= 0 && p < k.Length - 1) k = k.Substring(p + 1);
            return k;
        }

        private static string NextFKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 2) return "";
            if (key[0] != 'F' && key[0] != 'f') return "";
            int n;
            if (!int.TryParse(key.Substring(1), out n) || n < 1 || n >= 12) return "";
            return "F" + (n + 1);
        }

        private void StartCalibPrompt(string title, string sub, string keyA, string keyB,
            int firstMinutes, int secondMinutes, DateTime readAt)
        {
            _calibPromptsLeft--;
            int seconds = _cfg.CalibPromptSeconds;
            bool hasKeys = !string.IsNullOrEmpty(keyA);
            Logger.On("校准", "弹出校准提示：" + title + "（" + seconds + " 秒，剩余次数 " + _calibPromptsLeft + "）");
            CalibPrompt.Show(title, sub, keyA, keyB, seconds, delegate(int r)
            {
                if (r == 0)
                {
                    ApplyDeconCalib(firstMinutes, readAt);
                    _calibResolved = true;
                }
                else if (r == 1)
                {
                    ApplyDeconCalib(secondMinutes, readAt);
                    _calibResolved = true;
                }
                else if (r == -1)
                {
                    _calibResolved = true;   // 玩家明确取消：本局不再弹
                    Logger.On("校准", "玩家取消了校准提示，本局不再弹");
                }
                else
                {
                    Logger.On("校准", "校准提示超时未选，不校准；下次识别还会再弹（最多 2 次）");
                }
            });
        }

        private void ApplyDeconCalib(int minutes, DateTime readAt)
        {
            int remain = minutes > 0 ? DeconRemainForMinutes(minutes) : 0;
            _est.CalibrateDeconRemaining(readAt, remain);
            Logger.On("校准", minutes > 0
                ? "按轻收容 " + minutes + " 分钟校准（现实剩余 " + remain + " 秒），以读到的那一刻为准"
                : "按轻收容开始封锁校准（剩余 0 秒），以读到的那一刻为准");
            ApplyAll();
        }

        private int DeconRemainForMinutes(int minutes)
        {
            try
            {
                string table = _cfg.DeconAnnounceMap;
                if (!string.IsNullOrEmpty(table))
                {
                    string[] parts = table.Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string one = parts[i].Trim();
                        int eq = one.IndexOf('=');
                        if (eq <= 0) continue;
                        string left = one.Substring(0, eq).Trim().ToLowerInvariant();
                        string digits = left.TrimEnd('m', 's', '分', '秒');
                        int key;
                        if (!int.TryParse(digits, out key)) continue;
                        bool isMin = left.EndsWith("m") || left.EndsWith("分");
                        int keySeconds = isMin ? key * 60 : key;
                        if (keySeconds != minutes * 60) continue;
                        int v;
                        if (int.TryParse(one.Substring(eq + 1).Trim(), out v) && v >= 0) return v;
                    }
                }
            }
            catch { }
            return minutes * 60;
        }

        public void NoteDeadManBroadcastByOcr(string text)
        {
            ResumeFromStop();
            DateTime now = DateTime.Now;
            _deadManOcrAt = now;
            NotePulse("pulse.deadman.ocr");

            bool wasRunning = DeadManActive;
            bool bothOut = _tactical != null && _tactical.BothOutOfTokens;
            _deadManAt = DateTime.MinValue;
            _deadManText = "";
            _deadManUntil = DateTime.MinValue;
            // 真终焉激活时两边令牌一定都是 0；如果 OCR 认到"终焉"但令牌并不是
            // 双双为 0，先不当成本局结束（单帧误识别不能把这一局的终焉表封死）。
            _deadManCompleted = wasRunning || bothOut;
            _deadManDoneUntil = now.AddSeconds(DeadManDoneShowSeconds);
            NotePulse("pulse.deadman.zero");
            Logger.On("播报", "屏幕上认到『终焉协议』→ "
                + (wasRunning ? "等待倒计时立即归零" : "等待倒计时没在走，只记录播报")
                + "；终焉已启动，只结束终焉表，不启动、不调整核弹计时（核弹由自己的播报单独起表）"
                + "。原文：" + text);
            ApplyAll();
        }

        /// <summary>屏幕上认到过终焉协议吗（1/0）。</summary>
        private double DeadManOcrValue()
        {
            return _deadManOcrAt == DateTime.MinValue ? 0 : 1;
        }

        /// <summary>核弹引爆后强制刷新的冷却到什么时候。</summary>
        private DateTime _waveCooldownUntil = DateTime.MinValue;

        private void CheckDeadManSwitch(DateTime now)
        {
            if (!_roundActive || _tactical == null) return;

            // ---- ⓪ 读取当前令牌状态；终焉倒计时独立于核弹计时 ----
            bool bothOut = _tactical.BothOutOfTokens;

            // ---- ① 两方都没令牌 → 起倒计时 ----
            //
            // 【口径（2026-10-31 操作者纠正）】2 分 30 秒计的是"最后那波小波刷完 →
            // 终焉真正启动"中间那段等待，不是终焉启动之后。所以：
            //   · 等大波之后那波小波刷出来（小波不消耗令牌，必须先过去）；
            //   · 小波一出来就按**小波刷新的时刻**起表（不是等到检测到"两方都是 0"
            //     才起 —— 票数读得晚，那样会把整段等待吞掉）；
            //   · 不再用"人类还有概率拿到令牌"来推迟起表：文档的规则是拿到令牌
            //     就重置倒计时，不是等概率降下来才起。
            if (bothOut && _deadManAt == DateTime.MinValue && !_deadManCompleted)
            {
                bool miniPending = _tactical.NextWaveIsSmall(now, 0);
                double miniWait = MiniWaveToDeadManWait(now);
                double chance = _tactical.PredictAnyRespawnChance(now);

                if (miniPending || miniWait > 0)
                {
                    if (!_deadManBlockedLogged)
                    {
                        _deadManBlockedLogged = true;
                        Logger.On("终焉协议", "两方令牌都用尽了，但"
                            + (miniPending ? "大波之后的那波小波还没来" : "")
                            + (miniPending && miniWait > 0 ? "、" : "")
                            + (miniWait > 0
                                ? "小波刷新后还要 " + (int)Math.Ceiling(miniWait) + " 秒"
                                  + "（小波后 " + _cfg.DeadManAfterMiniWaveSeconds + " 秒）"
                                : "")
                            + " → 暂不启动亡者协议（当前拿到令牌概率 "
                            + TacticalLog.PercentText(chance) + "，拿到会重置，不阻塞起表）");
                    }
                }
                else
                {
                    _deadManBlockedLogged = false;
                    // 票数读数常常比小波晚几十秒：把起点定在"最后那波小波"，
                    // 这样屏幕上显示的就是操作者要的中间等待段。
                    DateTime anchor = LastMiniWaveAt();
                    DateTime? startAt = now;
                    if (anchor != DateTime.MinValue && anchor >= _tactical.BigWaveAt)
                    {
                        double sinceAnchor = (now - anchor).TotalSeconds;
                        DateTime earliest = anchor.AddSeconds(Math.Max(0, _cfg.DeadManAfterMiniWaveSeconds));
                        if (sinceAnchor >= DeadManTotalSeconds)
                        {
                            // 小波已经过去比整段等待还久：说明等待段早该结束了，
                            // 再起一条 150 秒只会骗人，等屏幕上的终焉播报即可。
                            startAt = null;
                        }
                        else if (earliest <= now)
                        {
                            startAt = earliest;
                        }
                    }

                    if (startAt.HasValue)
                    {
                        _deadManAt = startAt.Value;
                        _deadManWaveSeen = _tactical.WaveHistory.Count;
                        Logger.On("终焉协议", "两方重生令牌都已用尽，最后那波小波已过 → 亡者协议倒计时从 "
                            + startAt.Value.ToString("HH:mm:ss") + " 起算（" + DeadManTotalSeconds + " 秒，"
                            + "剩余约 " + (int)DeadManRemaining + " 秒；当前拿到令牌概率 "
                            + TacticalLog.PercentText(chance) + "，拿到会重置）");
                    }
                    else
                    {
                        Logger.On("终焉协议", "两方重生令牌都已用尽，但最后那波小波已经过去超过 "
                            + (int)DeadManTotalSeconds + " 秒 → 不再补起等待表，等屏幕终焉播报");
                    }
                }
            }
            // ---- ② 期间拿到新令牌 → 整个倒计时重置 ----
            else if (!bothOut && _wasBothOut)
            {
                if (_deadManAt != DateTime.MinValue)
                    Logger.On("终焉协议", "有阵营重新拿到令牌 → 亡者协议倒计时重置");
                _deadManAt = DateTime.MinValue;
                _deadManOcrAt = DateTime.MinValue;
                _deadManCompleted = false;
                _deadManDoneUntil = DateTime.MinValue;
            }

            if (!bothOut) _deadManBlockedLogged = false;
            _wasBothOut = bothOut;

            // ---- ③ 误识别保护：表在走，但两方令牌并不是都 0 ----
            // 用来兜住"单次误识别先起了一张表，后面读数掰回来但表还挂着"的情况：
            // 连续 4 秒都确认不了"两方令牌都是 0"，就把这张表撤掉。
            if (_deadManAt != DateTime.MinValue && !bothOut)
            {
                if (_deadManNotBothOutSince == DateTime.MinValue) _deadManNotBothOutSince = now;
                else if ((now - _deadManNotBothOutSince).TotalSeconds >= 4)
                {
                    Logger.On("终焉协议", "终焉表在走，但两方令牌已经不再是都 0（持续 4 秒）"
                        + " → 判定为误识别/已恢复，撤掉这张表");
                    _deadManAt = DateTime.MinValue;
                    _deadManOcrAt = DateTime.MinValue;
                    _deadManDoneUntil = DateTime.MinValue;
                    _deadManCompleted = false;
                    _deadManNotBothOutSince = DateTime.MinValue;
                    _wasBothOut = false;
                }
            }
            else _deadManNotBothOutSince = DateTime.MinValue;

            if (_deadManAt == DateTime.MinValue) return;

            // 文档："Mini-waves will only set the progress back by 40 seconds."
            // 也就是亡者协议期间刷了一次小波，倒计时往回退 40 秒（不是整个重置）。
            int waveCount = _tactical.WaveHistory.Count;
            if (waveCount != _deadManWaveSeen)
            {
                // 只在"多出来的是小波"时后退
                bool sawSmall = false;
                for (int i = _deadManWaveSeen; i < waveCount; i++)
                {
                    if (i >= 0 && i < _tactical.WaveHistory.Count && _tactical.WaveHistory[i].IsSmall)
                    { sawSmall = true; break; }
                }
                _deadManWaveSeen = waveCount;
                if (sawSmall)
                {
                    _deadManAt = _deadManAt.AddSeconds(DeadManMiniWaveSetback);
                    Logger.On("终焉协议", "亡者协议期间出现小波 → 进度后退 "
                        + DeadManMiniWaveSetback + " 秒（还剩 "
                        + (int)DeadManRemaining + " 秒）");
                }
            }

            double left = DeadManRemaining;
            if (left > 0)
            {
                // 倒计时期间持续提示（复用核弹那个窗口，免得再多一个悬浮窗）
                _deadManText = "亡者协议倒计时 " + TimeText.ForPrompt(_cfg, _cfg.Alerts[I_DeadMan], left);
                _deadManUntil = now.AddSeconds(2);
                return;
            }

            // ---- ④ 归零：只结束终焉自己的表 ----
            // 操作者要求（2026-10-31）：终焉和核弹是两条独立的计时，
            // 终焉归零绝不能顺手把核弹表起起来；核弹只由它自己的播报 / 快捷键驱动。
            _deadManAt = DateTime.MinValue;
            _deadManOcrAt = DateTime.MinValue;
            _deadManText = "";
            _deadManCompleted = true;
            _deadManDoneUntil = now.AddSeconds(DeadManDoneShowSeconds);
            NotePulse("pulse.deadman.zero");
            Logger.On("终焉协议", "亡者协议倒计时结束 → 终焉已启动；只结束终焉表，"
                + "不启动、不调整核弹计时（核弹由自己的播报单独起表）");
        }

        /// <summary>
        /// 「小波刷新后到终焉」还要等多少秒（0 = 不用等）。
        /// 操作者口径："刷小波后一段时间才有终焉" —— 这段时间可校准。
        /// </summary>
        private double MiniWaveToDeadManWait(DateTime now)
        {
            if (_cfg.DeadManAfterMiniWaveSeconds <= 0) return 0;
            DateTime anchor = LastMiniWaveAt();
            if (anchor == DateTime.MinValue) return 0;
            double since = (now - anchor).TotalSeconds;
            double wait = _cfg.DeadManAfterMiniWaveSeconds - since;
            return wait > 0 ? wait : 0;
        }

        /// <summary>
        /// 最近一次"小波刷新"的时刻（没有小波记录 → MinValue）。
        /// </summary>
        private DateTime LastMiniWaveAt()
        {
            if (_tactical == null) return DateTime.MinValue;
            for (int i = _tactical.WaveHistory.Count - 1; i >= 0; i--)
            {
                if (_tactical.WaveHistory[i].IsSmall) return _tactical.WaveHistory[i].At;
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// 校准「终焉协议还剩多少秒」：按用户填的剩余时间重新对齐，没在走就直接起表。
        /// 和支援 / 净化那几条"还剩多少秒"的校准是同一个口径 ——
        /// 入口在战术面板的「校准」页（时间校准已经并进战术面板）。
        /// </summary>
        public void CalibrateDeadManRemaining(double remainSeconds)
        {
            if (remainSeconds < 0) remainSeconds = 0;
            double total = _cfg.DeadManSeconds > 0 ? _cfg.DeadManSeconds : DeadManSeconds;
            if (remainSeconds > total) remainSeconds = total;

            ResumeFromStop();
            DateTime now = DateTime.Now;
            _deadManAt = now.AddSeconds(-(total - remainSeconds));
            _deadManCompleted = false;
            _deadManDoneUntil = DateTime.MinValue;
            _deadManNotBothOutSince = DateTime.MinValue;
            _deadManWaveSeen = _tactical == null ? 0 : _tactical.WaveHistory.Count;
            _deadManBlockedLogged = false;
            _wasBothOut = _tactical != null && _tactical.BothOutOfTokens;
            Logger.On("终焉协议", "手动校准：终焉协议倒计时按剩余 " + (int)remainSeconds
                + " 秒对齐（总长 " + (int)total + " 秒）");
            ApplyAll();
        }

        private string _deadManText = "";
        private DateTime _deadManUntil = DateTime.MinValue;

        /// <summary>
        /// 终焉归零后，亡者协议那条提示继续显示"终焉协议已启动"到什么时候。
        /// 它只影响亡者协议自己的提示 —— 核弹计时永远由核弹播报 / 快捷键单独驱动，
        /// 终焉归零不会去启动或修改核弹表。
        /// </summary>
        private DateTime _deadManDoneUntil = DateTime.MinValue;
        private const double DeadManDoneShowSeconds = 8;

        /// <summary>
        /// 终焉表在走、但战术模型里"两方令牌都是 0"已经不再成立多久了。
        /// 用来撤掉因为单次误识别/误读数起出来的假表（后面掰回来就清掉）。
        /// </summary>
        private DateTime _deadManNotBothOutSince = DateTime.MinValue;

        /// <summary>
        /// 核弹是不是已经进入"引爆不可逆"的阶段。
        /// 文档：最后 10 秒无法关闭，两个终端的屏幕会闪 "Detonation Inevitable"。
        /// </summary>
        public bool WarheadInevitable
        {
            get
            {
                if (!_est.WarheadRunning) return false;
                if (_est.WarheadLocked) return true;
                return _est.WarheadRemaining <= TicketModel.InevitableSeconds;
            }
        }

        /// <summary>
        /// 核弹引爆后的收尾（文档）：
        ///   "Upon detonation, the next respawn wave will be forced to a four minute
        ///    cooldown, regardless of their timer before the detonation."
        /// </summary>
        public void OnWarheadDetonated()
        {
            _waveCooldownUntil = DateTime.Now.AddSeconds(PostDetonationWaveCooldown);
            Logger.On("核弹", "核弹已引爆 → 下一次重生波强制推迟到 "
                + PostDetonationWaveCooldown + " 秒冷却之后");
        }

        /// <summary>核弹引爆后强制刷新冷却剩余秒数（0 = 没有冷却）。</summary>
        public double WaveCooldownRemaining
        {
            get
            {
                if (_waveCooldownUntil == DateTime.MinValue) return 0;
                double r = (_waveCooldownUntil - DateTime.Now).TotalSeconds;
                return r < 0 ? 0 : r;
            }
        }

        // ============================================================
        // 净化兜底
        //
        // 净化是"必须识别到播报才启动"的。万一那段播报被 OCR 漏掉
        // （画面被挡、字太暗、识别抖动），这一局就永远看不到净化倒计时。
        //
        // 兜底规则（只在检测到新回合时生效）：
        //   回合正式计时开始后一直没等到播报，那么当"回合已进行到
        //   DeconAfterRound 秒"时自己开始 —— 等价于退回老版本按回合
        //   时间推算的行为，顶多几秒误差，不会整局都没有。
        //
        // 中途加入（没检测到回合开始）不走这里：那种情况按 F9，
        // 或用托盘菜单里的「净化倒计时（手动开始）」。
        // ============================================================
        private void CheckDeconFallback(DateTime now)
        {
            if (!_cfg.DeconFallback) return;
            if (!_roundActive) return;                 // 没检测到回合开始 → 不猜
            if (_est.DeconDisabled) return;            // 中途加入：不推算轻收容
            if (_est.DeconAnnounced) return;           // 已经认出来了（或手动起过）

            double elapsed = _est.RoundElapsed;
            double due = _cfg.DeconAfterRound;
            if (elapsed < due) return;

            // 已经过了净化该开始的时刻 → 按"晚了多少"把倒计时起点往后挪
            double remain = _cfg.DeconBaseSeconds - (elapsed - due);
            if (remain < 0) remain = 0;

            _est.AnnounceDecon(now, remain <= 0 ? 0.001 : remain);
            Logger.On("回合", "兜底触发：回合已进行 " + (int)elapsed + " 秒，"
                + "一直没识别到净化播报 → 按回合时间自动开始净化倒计时（剩 "
                + (int)remain + " 秒）。可用 config.ini 的 [Decontamination] Fallback=0 关掉");
            ApplyAll();
        }

        // ============================================================
        // 重生令牌阈值预警（需求二）
        //
        // 票数是玩家手填的 SCP 血量算出来的（伤害 ÷ 200 + 击杀 × 15）。
        // 每当票数越过 config.ini 里那串阈值中的一个，就弹一次提示：
        //     累积30票，人类即将获得第一个重生令牌
        // "第几个"按阈值在列表里的顺序数，所以阈值可以随便加/改。
        //
        // 另外还有一个"小波"条件：大波刷新后的窗口内打够 N 票
        //     → 2分钟内打出10票，可召唤迷你波（小波）
        //
        // 用"越过"而不是"大于等于"，是为了只弹一次；
        // 新回合会清空记录，所以下一局还能再弹。
        // ============================================================

        private bool _miniWaveFired;

        private void ResetTokenState()
        {
            _miniWaveFired = false;
            _tokenEarned.Clear();
        }

        /// <summary>解析 config 里的阈值串（"40,80,150,200"），顺手排序、去重。</summary>
        private int[] TokenList()
        {
            string raw = _cfg.TokenThresholds;
            if (raw == null) raw = "";
            string[] parts = raw.Split(',');
            List<int> vals = new List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), out v) && v > 0 && !vals.Contains(v)) vals.Add(v);
            }
            // 官方阈值（机制\刷新.txt 1.2.2）；老版本这里是 30，第一个就写错了
            if (vals.Count == 0) { vals.Add(40); vals.Add(80); vals.Add(150); vals.Add(200); }
            vals.Sort();
            return vals.ToArray();
        }

        /// <summary>
        /// 检查令牌阈值 —— 现在看的是影响力（文档 1.2.2），不是旧版的"票数"。
        ///
        /// 每个阵营单独算：影响力跨过 40/80/150/200 时，
        /// 该阵营获得一个重生令牌。每回合最多 3 个额外令牌，而且两方共享这个上限。
        /// </summary>
        private void CheckTokenThresholds(DateTime now)
        {
            if (!_roundActive) return;

            TacticalLog log = _tactical;
            if (log == null) return;

            // 两个阵营各自检查一次
            CheckOneFaction(log, TacticalLog.MTF);
            CheckOneFaction(log, TacticalLog.CHAOS);

            // ---- 小波：主波后 2 分钟内拿到 10 影响力 ----
            // （文档 1.2.4；判定本身在 TacticalLog 里，这里只负责提示）
            if (!_miniWaveFired && log.HasBigWave)
            {
                double since = (now - log.BigWaveAt).TotalSeconds;
                if (since >= 0 && since <= _cfg.MiniWaveWindowSeconds)
                {
                    int mine = log.TicketsInBigWaveWindow(_cfg.MiniWaveWindowSeconds, false);
                    if (mine >= _cfg.MiniWaveTickets)
                    {
                        _miniWaveFired = true;
                        string text = _cfg.MiniWaveWindowSeconds / 60 + "分钟内拿到"
                                    + _cfg.MiniWaveTickets + "影响力，可召唤迷你波（小波）";
                        _tokenText = SafeFormat(_cfg.Alerts[I_Token].Format, new object[] { text });
                        _tokenShowing = true;
                        _tokenUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Token].DurationSeconds));
                        Logger.On("战术", "小波条件满足 → " + text);
                    }
                }
            }

        }

        /// <summary>某一个阵营的令牌阈值提示。</summary>
        private void CheckOneFaction(TacticalLog log, string faction)
        {
            int inf = log.TicketsOf(faction);
            string name = faction == TacticalLog.MTF ? "MTF" : "混沌";

            // 已经提示过的阈值个数；用影响力反推，不用单独存数组
            int earned = 0;
            int[] th = TokenList();
            for (int i = 0; i < th.Length; i++) if (inf >= th[i]) earned++;

            int was;
            if (!_tokenEarned.TryGetValue(faction, out was)) was = -1;

            // 第一次统计不算"越过"，免得开面板时把旧数据全弹一遍
            if (was < 0) { _tokenEarned[faction] = earned; return; }
            if (earned <= was) { _tokenEarned[faction] = earned; return; }

            _tokenEarned[faction] = earned;

            int got = earned - was;
            int bonus = log.Tickets.BonusOf(faction);
            _tokenText = SafeFormat(_cfg.Alerts[I_Token].Format, new object[]
            {
                name + " 影响力 " + inf + " → 获得 " + got + " 个重生令牌"
                + "（现有 " + log.TokensOf(faction) + " 个；本回合额外令牌 "
                + bonus + "/" + log.Tickets.MaxBonusTokens + "）"
            });
            _tokenShowing = true;
            _tokenUntil = DateTime.Now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Token].DurationSeconds));
            Logger.On("战术", name + " 影响力达到 " + inf + " → 获得 " + got + " 个重生令牌");
        }

        /// <summary>每个阵营已经跨过几个阈值（第一次统计时初始化）。</summary>
        private readonly Dictionary<string, int> _tokenEarned = new Dictionary<string, int>();

        /// <summary>两方都没令牌的提示只弹一次。</summary>

        private string _tokenText = "";
        private bool _tokenShowing;
        private DateTime _tokenUntil;

        /// <summary>手动触发一次令牌预警（给测试 / 排查用）。</summary>
        public void FireTokenAlert(string text)
        {
            _tokenText = SafeFormat(_cfg.Alerts[I_Token].Format, new object[] { text });
            _tokenShowing = true;
            _tokenUntil = DateTime.Now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Token].DurationSeconds));
            ApplyAll();
        }

        private static void Drive(IAlertSurface form, bool show, string text)
        {
            if (show) form.Pop(text);
            else form.Retract();
        }

        /// <summary>
        /// 该提示当前是否允许显示。
        ///   · “按住显示全部”会临时无视关闭状态；
        ///   · 条件显示（票数 / 079 / 击杀数…）不满足时也当作不显示，
        ///     但“按住显示全部”同样可以强行看到它 —— 方便排查条件设错了。
        /// </summary>
        private bool On(AlertConfig a)
        {
            if (_forceShowAll) return true;
            if (!a.Style.Enabled) return false;
            if (a.CondType != AlertCondition.None) return ConditionOk(a);
            return true;
        }

        /// <summary>判断这条提示的条件是否满足。</summary>
        private bool ConditionOk(AlertConfig a)
        {
            double roundMin = _est.Running ? _est.RoundElapsed / 60.0 : 0;
            return AlertCondition.Satisfied(a.CondType, a.CondValue, _tactical, roundMin);
        }

        /// <summary>
        /// 把对局内的提示全部收起来（回合结束之后、下一局开始之前用）。
        ///
        /// 【哪些不收】回合开始 / 进入服务器 / 游戏启动退出 / 启动横幅 ——
        /// 这几条是"程序与服务器状态"的提示，和本局在不在进行无关。
        /// 回合时间**收在这里之前**就单独判了（没进服务器 / 准备阶段都不显示），
        /// 所以它不在这个数组里。
        /// 【为什么要它】操作者报："回合结束后、对局开始前不该弹出任何提示"，
        /// 而当时支援刷新常驻挂着不走（见 <see cref="ApplyAll"/> 里那道闸的注释）。
        /// </summary>
        private void RetractRoundAlerts()
        {
            int[] roundOnes = new int[]
            {
                I_Warhead, I_Decon, I_Support, I_Scp, I_ScpKilled, I_Token, I_Role,
                I_Ticket, I_Wave, I_Respawn, I_DeadMan, I_GenActive, I_Gen,
                I_LocalDeath, I_SelfRespawn
            };
            for (int i = 0; i < roundOnes.Length; i++)
            {
                int idx = roundOnes[i];
                if (idx < 0 || idx >= _forms.Length) continue;
                try
                {
                    _forms[idx].Highlight = false;
                    Drive(_forms[idx], false, "");
                }
                catch { }
            }
            _genActiveShowing = false;
            _tokenShowing = false;
            _respawnShowing = false;
            _scpShowing = false;
            _scpKilledShowing = false;
            _roleShowing = false;
            _ticketShowing = false;
            _waveShowing = false;
            _selfDeathShowing = false;
            _selfRespawnShowing = false;
        }

        // ============================================================
        // 【自定义音频触发点】引擎（操作者要求：音效系统要更丰富的自由度）
        //
        // 一条触发点 = 名称 + 一组条件（全部满足才触发）+ 音频条目。
        // 每拍（200ms）算一次：条件从"不满足"变成"满足"的那一刻响一次，
        // 之后按 CooldownSeconds 冷却；勾了"一局只响一次"就这一局不再响第二次。
        //
        // 事件取值在 EvalAudioEvent 里；一次性事件（"刚刚阵亡""第 3 波刷新"）
        // 用脉冲实现：事件发生那一刻起 NotePulse 记一个短暂的 1，
        // 条件里写 `脉冲事件 == 1` 就能抓到（配合 wave.count 之类还能表达"第 N 次"）。
        // ============================================================

        /// <summary>脉冲（一次性事件）在什么时候之前算"刚刚发生"。</summary>
        private readonly Dictionary<string, DateTime> _pulseUntil = new Dictionary<string, DateTime>();

        // 状态跳变检测用的"上一拍"快照
        private bool _wasWarheadAudio;
        private bool _wasDeadManAudio;
        private bool _wasAllReadyAudio;
        private int _wasWaveCountAudio;
        private readonly int[] _wasTokensAudio = new int[2];

        /// <summary>每条触发点上一拍的判定结果（用来做"从无到有"的边沿触发）。</summary>
        private bool[] _trigWas = new bool[0];

        /// <summary>每条触发点上次响的时刻。</summary>
        private DateTime[] _trigLast = new DateTime[0];

        /// <summary>每条触发点"这一局已经响过"的回合号（0 = 没响过）。</summary>
        private int[] _trigFiredRound = new int[0];   // 值 = 响过的那一局的 _roundCounter，-1 表示还没响
        private bool[] _trigStopWas = new bool[0];      // 第 7 批：停止区条件上一帧是否成立

        /// <summary>记一次"刚刚发生"。faction / arg 可以为空，用来区分对象。</summary>
        private void NotePulse(string id, string faction, string arg)
        {
            if (string.IsNullOrEmpty(id)) return;
            string key = PulseKey(id, faction, arg);
            _pulseUntil[key] = DateTime.Now.AddSeconds(PulseHoldSeconds);
        }

        private void NotePulse(string id)
        {
            NotePulse(id, null, null);
        }

        /// <summary>脉冲保持多少秒算"刚刚"（够触发点判定就行，别太长）。</summary>
        private const double PulseHoldSeconds = 4;

        private static string PulseKey(string id, string faction, string arg)
        {
            string k = id;
            if (!string.IsNullOrEmpty(faction)) k += "|" + faction;
            if (!string.IsNullOrEmpty(arg)) k += "#" + arg;
            return k;
        }

        /// <summary>
        /// 取某个脉冲现在的值（1 = 刚刚发生）。
        /// 条件里指定了阵营/编号就精确匹配；没指定则"任何对象刚发生"都算。
        /// </summary>
        private double PulseValue(string id, string faction, string arg)
        {
            DateTime now = DateTime.Now;

            if (!string.IsNullOrEmpty(faction) || !string.IsNullOrEmpty(arg))
            {
                string key = PulseKey(id, faction, arg);
                DateTime until;
                if (_pulseUntil.TryGetValue(key, out until) && now < until) return 1;

                // 条件只写了阵营（没写编号）时，同一阵营的任何编号都算
                if (!string.IsNullOrEmpty(faction) && string.IsNullOrEmpty(arg))
                {
                    string pre = id + "|" + faction;
                    foreach (KeyValuePair<string, DateTime> kv in _pulseUntil)
                        if (kv.Key.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && now < kv.Value)
                            return 1;
                }
                return 0;
            }

            // 没指定对象 → 只要这个事件有任何一个还没过期就算
            DateTime u2;
            if (_pulseUntil.TryGetValue(id, out u2) && now < u2) return 1;
            foreach (KeyValuePair<string, DateTime> kv in _pulseUntil)
            {
                if (now >= kv.Value) continue;
                if (kv.Key.StartsWith(id, StringComparison.OrdinalIgnoreCase)) return 1;
            }
            return 0;
        }

        /// <summary>把"MTF / 混沌 / 空"归一成模型里的阵营名。</summary>
        private static string NormFaction(string f)
        {
            if (string.IsNullOrEmpty(f)) return null;
            if (string.Equals(f, "MTF", StringComparison.OrdinalIgnoreCase)) return TacticalLog.MTF;
            if (f == "混沌" || f == "混沌分裂者"
                || string.Equals(f, "Chaos", StringComparison.OrdinalIgnoreCase)) return TacticalLog.CHAOS;
            return f;
        }

        /// <summary>
        /// 某个事件现在的数值（触发点条件就用它比较）。
        /// 返回 null = 这个事件现在没法取值（条件判为不满足）。
        /// </summary>
        public double? EvalAudioEvent(string id, string faction, string arg)
        {
            if (string.IsNullOrEmpty(id)) return null;
            DateTime now = DateTime.Now;

            // 脉冲类：id 以 pulse. 开头
            if (id.StartsWith("pulse.", StringComparison.OrdinalIgnoreCase))
                return PulseValue(id, faction, arg);

            switch (id.ToLowerInvariant())
            {
                case "round.active": return _roundActive ? 1 : 0;
                case "inserver": return _inServer ? 1 : 0;
                case "alive": return (_inServer && !_isDeadNow) ? 1 : 0;
                case "self.dead": return _isDeadNow ? 1 : 0;
                case "round.elapsed":
                    if (_est.Running) return _est.RoundElapsed;
                    return _roundActive ? (now - _roundStart).TotalSeconds : 0;

                case "warhead.active": return _est.WarheadRunning ? 1 : 0;
                case "warhead.remain": return _est.WarheadRunning ? _est.WarheadRemaining : 0;
                case "alert.remain":
                    {
                        int ai = FindAlertByName(arg);
                        if (ai < 0) return null;
                        return AlertRemainingSeconds(ai, now);
                    }
                case "deadman.active": return DeadManActive ? 1 : 0;
                case "deadman.ocr": return DeadManOcrValue();
                case "deadman.remain":
                    {
                        double r = DeadManRemaining;
                        return r < 0 ? 0 : r;
                    }

                case "decon.active": return _est.DeconDisabled && !_est.DeconCountdownStarted ? 0 : (_est.DeconCountdownStarted ? 1 : 0);
                case "decon.remain": return _est.DeconDisabled && !_est.DeconCountdownStarted ? _est.DeconSeconds : _est.DeconRemaining;
                case "decon.locked": return _est.DeconLocked ? 1 : 0;

                case "wave.count": return _tactical.WaveHistory.Count;
                case "wave.next.remain":
                    {
                        double mtf = _tactical.WaveRemaining(now, TacticalLog.MTF);
                        double chaos = _tactical.WaveRemaining(now, TacticalLog.CHAOS);
                        return (mtf <= 0) ? chaos : (chaos <= 0 ? mtf : Math.Min(mtf, chaos));
                    }
                case "wave.mini.pending": return _tactical.NextWaveIsSmall(now, 0) ? 1 : 0;
                case "wave.mini.remain":
                    {
                        double m = _tactical.MiniWaveRemaining(now);
                        return m < 0 ? 0 : m;
                    }
                case "wave.soon":
                    {
                        double left = _tactical.NextWaveIsSmall(now, 0)
                            ? Math.Max(0, _tactical.MiniWaveRemaining(now))
                            : _tactical.WaveRemaining(now, TacticalLog.MTF);
                        return left > 0 && left <= 30 ? 1 : 0;
                    }

                case "has079": return _tactical.Has079 ? 1 : 0;
                case "gen.active": return _tactical.GeneratorsActive;
                case "gen.allready": return _tactical.AllGeneratorsReady ? 1 : 0;
                case "gen.soon":
                    {
                        int idx = -1; double left = double.MaxValue;
                        GenSoon(now, out idx, out left);
                        return idx < 0 ? 0 : idx + 1;
                    }
                case "gen.soon.remain":
                    {
                        int idx = -1; double left = double.MaxValue;
                        GenSoon(now, out idx, out left);
                        return idx < 0 ? 0 : Math.Max(0, left);
                    }

                case "tokens":
                    return _tactical.Tickets.TokensOf(NormFaction(faction) ?? TacticalLog.MTF);
                case "influence":
                    return _tactical.TicketsOf(NormFaction(faction) ?? TacticalLog.MTF);
                case "influence.next":
                    {
                        string f = NormFaction(faction) ?? TacticalLog.MTF;
                        int need = _tactical.Tickets.ToNextToken(f);
                        if (need < 0) return 999;      // 没有下一个令牌了
                        int have = _tactical.TicketsOf(f);
                        return need > have ? need - have : 0;
                    }

                case "scp.alive":
                case "scp.dead":
                case "scp.present":
                    {
                        if (string.IsNullOrEmpty(arg)) return null;
                        TacticalLog.ScpRecord rec = _tactical.Find(arg.Trim());
                        if (rec == null) return 0;
                        if (id.Equals("scp.alive", StringComparison.OrdinalIgnoreCase))
                            return (rec.Present && !rec.Killed) ? 1 : 0;
                        if (id.Equals("scp.present", StringComparison.OrdinalIgnoreCase))
                            return rec.Present ? 1 : 0;
                        return rec.Killed ? 1 : 0;
                    }
            }
            return null;
        }

        /// <summary>按显示名 / Section 找一条提示（触发点条件「某条提示的倒计时剩余」用）。</summary>
        private int FindAlertByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            string want = name.Trim();
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                AlertConfig a = _cfg.Alerts[i];
                if (string.Equals(a.DisplayName, want, StringComparison.OrdinalIgnoreCase)) return i;
                if (string.Equals(a.Section, want, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>某条提示当前还剩多少秒（没在倒计时 = 0）。</summary>
        private double AlertRemainingSeconds(int i, DateTime now)
        {
            if (i < 0 || i >= _cfg.Alerts.Length) return 0;
            AlertConfig a = _cfg.Alerts[i];

            if (a.Estimated)
            {
                if (i == I_Warhead) return _est.WarheadRunning ? _est.WarheadRemaining : 0;
                if (i == I_Decon) return _est.DeconRemaining;
                if (i == I_Support) return Math.Max(0, _est.SupportRemaining);
                return 0;
            }

            Countdown st = _cd[i];
            if (st == null || !st.Counting) return 0;
            double r = (st.End - now).TotalSeconds;
            return r < 0 ? 0 : r;
        }

        /// <summary>正在启动、且快好了的那一台（给 gen.soon 用）。</summary>
        private void GenSoon(DateTime now, out int idx, out double left)        {
            idx = -1;
            left = double.MaxValue;
            for (int i = 0; i < TacticalLog.GeneratorCount; i++)
            {
                TacticalLog.Generator g = _tactical.Generators[i];
                if (g.Done || !g.Running) continue;
                double l = g.Remaining(now);
                if (l > 0 && l <= TacticalLog.GenSoonSeconds && l < left) { left = l; idx = i; }
            }
        }

        /// <summary>每拍检查所有自定义触发点（在 ApplyAll 里调）。</summary>
        private void CheckAudioTriggers(DateTime now)
        {
            // ---- 先根据"状态跳变"补一次性脉冲 ----
            // （散落在各处的事件用 NotePulse 直接记；这几条是"看状态变化"更靠谱的）
            bool warheadNow = _est.WarheadRunning;
            if (warheadNow && !_wasWarheadAudio)
                NotePulse("pulse.warhead.start");
            if (_wasWarheadAudio && !warheadNow && _est.WarheadRemaining <= 0.5)
                NotePulse("pulse.warhead.detonated");
            _wasWarheadAudio = warheadNow;

            bool dmNow = DeadManActive;
            if (dmNow && !_wasDeadManAudio) NotePulse("pulse.deadman.start");
            _wasDeadManAudio = dmNow;

            if (_tactical.AllGeneratorsReady && !_wasAllReadyAudio) NotePulse("pulse.gen.allready");
            _wasAllReadyAudio = _tactical.AllGeneratorsReady;

            // 发电机第 N 台：跟 ApplyGenerators 共用的 _genActiveSeen 看跳变
            for (int i = 0; i < TacticalLog.GeneratorCount && i < _genActiveSeen.Length; i++)
            {
                bool c = _tactical.Generators[i].Confirmed;
                if (c && !_genActiveSeen[i]) NotePulse("pulse.gen.activate", null, (i + 1).ToString());
            }

            // 刷新：波次一多就是刚刷了一波（大波/小波 + 阵营）
            int waves = _tactical.WaveHistory.Count;
            if (waves > _wasWaveCountAudio && waves > 0)
            {
                TacticalLog.WaveRecord r = _tactical.WaveHistory[waves - 1];
                NotePulse(r.IsSmall ? "pulse.wave.mini" : "pulse.wave.big", r.Faction, null);
            }
            _wasWaveCountAudio = waves;

            // 重生令牌：哪一方多了一个
            for (int s = 0; s < 2; s++)
            {
                string f = (s == 0) ? TacticalLog.MTF : TacticalLog.CHAOS;
                int tok = _tactical.Tickets.TokensOf(f);
                if (tok > _wasTokensAudio[s]) NotePulse("pulse.token.gain", f, null);
                _wasTokensAudio[s] = tok;
            }

            List<AudioTrigger> list = _cfg.AudioTriggers;
            if (list == null || list.Count == 0) return;

            // 列表长度变了（用户刚改过配置）→ 重建并行状态数组
            if (_trigWas.Length != list.Count)
            {
                _trigWas = new bool[list.Count];
                _trigStopWas = new bool[list.Count];
                _trigLast = new DateTime[list.Count];
                _trigFiredRound = new int[list.Count];
                for (int z = 0; z < _trigFiredRound.Length; z++) _trigFiredRound[z] = -1;
            }

            for (int i = 0; i < list.Count; i++)
            {
                AudioTrigger t = list[i];
                if (t == null) { _trigWas[i] = false; _trigStopWas[i] = false; continue; }
                string cardKey = i + ":" + t.Name;

                // ---- 停止区（第 7 批）：命中就停掉本卡片的所有动作 ----
                bool stopHit = t.Enabled && (t.StopConds.Count > 0
                    ? t.StopTrue(EvalAudioEvent)
                    : (t.IsStop && t.Conds.Count > 0 && t.AllTrue(EvalAudioEvent)));
                bool stopRising = stopHit && !_trigStopWas[i];
                _trigStopWas[i] = stopHit;

                if (stopRising)
                {
                    try
                    {
                        TriggerActionOverlay.HideCard(cardKey);
                        FloatingTextOverlay.HideCardPrefix(cardKey + ":float:");
                        AudioLibrary.StopWithFade(t.FadeOut);
                        Logger.On("触发点", "「" + t.Name + "」停止条件命中 → 停掉本卡片全部动作"
                            + (t.FadeOut > 0 ? "（渐出 " + t.FadeOut.ToString("0.#") + "s）" : ""));
                    }
                    catch (Exception ex) { Logger.Err("触发点", "执行停止条件失败", ex); }
                }

                // ---- 动作区：主条件成立才执行 ----
                List<TriggerAction> acts = t.EffectiveActions();
                bool on = t.Enabled && !t.IsStop && acts.Count > 0
                          && t.Conds.Count > 0
                          && t.AllTrue(EvalAudioEvent);
                bool rising = on && !_trigWas[i];
                _trigWas[i] = on;
                if (!rising) continue;

                // 一局只响一次？
                if (t.OncePerRound && _trigFiredRound[i] == _roundCounter) continue;

                // 冷却
                if (t.CooldownSeconds > 0 && _trigLast[i] != DateTime.MinValue
                    && (now - _trigLast[i]).TotalSeconds < t.CooldownSeconds) continue;

                _trigLast[i] = now;
                _trigFiredRound[i] = _roundCounter;
                try
                {
                    for (int a = 0; a < acts.Count; a++)
                    {
                        TriggerAction act = acts[a];
                        if (act == null) continue;
                        if (act.Kind == "image" || act.Kind == "text")
                        {
                            TriggerActionOverlay.Show(cardKey, act, _cfg);
                        }
                        else if (act.Kind == "float")
                        {
                            FloatingTextOverlay.Show(cardKey + ":float:" + a, act, _cfg);
                        }
                        else if (act.Kind == "effect")
                        {
                            List<TriggerEffect> effects = act.EffectiveEffects();
                            for (int fx = 0; fx < effects.Count; fx++)
                                ScreenEffects.Play(effects[fx], _cfg, _forms);
                        }
                        else
                        {
                            AudioLibrary.PlayKey(act.Clip, "自定义触发点「" + t.Name + "」",
                                t.FadeIn, t.FadeOut);
                        }
                    }
                    Logger.On("触发点", "「" + t.Name + "」命中（" + t.Text() + "）→ 执行 "
                        + acts.Count + " 个动作");
                }
                catch (Exception ex) { Logger.Err("触发点", "执行自定义触发点失败", ex); }
            }
        }

        /// <summary>换局/清空时把触发点状态一起重置（一局一次的口径跟着新回合走）。</summary>
        private void ResetAudioTriggers()
        {
            for (int i = 0; i < _trigWas.Length; i++)
            {
                _trigWas[i] = false;
                _trigFiredRound[i] = -1;   // -1 = 这一局还没响过（_roundCounter 从 0 开始）
            }
            _pulseUntil.Clear();
            try { TriggerActionOverlay.HideAll(); } catch { }
            try { FloatingTextOverlay.HideAll(); } catch { }
        }

        private void ApplyAll()
        {
            ApplyAllCore();
            RelayoutAlerts(false);
        }

        /// <summary>第 8 批：避让只改显示位置；不写 config、不改变业务状态。</summary>
        private void RelayoutAlerts(bool immediate)
        {
            if (_layout == null) return;
            // 屏幕抖动期间由 ScreenShake 直接移动表面，避让不要抢位置。
            if (ScreenShake.Active) return;
            if (_editMode || _startupShowing || DateTime.Now < _previewUntil) return;
            try { _layout.Relayout(immediate); }
            catch (Exception ex) { Logger.Err("提示", "防重叠布局失败（忽略）", ex); }
        }

        private void ApplyAllCore()
        {
            // 设置里那个总开关，每次刷新都同步一次（改了立刻生效）
            AlertSound.Master = _cfg.SoundEnabled;
            AudioLibrary.Master = _cfg.SoundEnabled;
            UiAnim.Quality = _cfg.PanelAnimQuality;   // 动画效果档位（帧率 + 特效）

            DateTime now = DateTime.Now;

            if (_editMode) return;
            if (now < _previewUntil) return;

            // 【启动演出期间不要按真实状态驱动提示】
            // 演出自己会把所有提示 Pop 出来、再逐帧故障、最后 Retract。
            // 但主定时器每 200ms 也会跑一次 ApplyAll，它按"真实状态"判断：
            // 这时候一个事件都没有，于是刚 Pop 出来的提示立刻被 Retract ——
            // 表现就是"先闪一下 → 全没了 → 过一会儿才出来故障"。
            // 演出期间整段交给演出自己管，结束时会再调一次 ApplyAll 收拾干净。
            if (_startupShowing) return;

            if ((_allHidden || _allStopped) && !_forceShowAll)
            {
                for (int i = 0; i < _forms.Length; i++) _forms[i].Retract();
                return;
            }

            // 【操作者要求】没进服务器（在桌面 / 主菜单 / 还没加载进服）时，
            // 那些"计时类"的提示一条都不许挂出来 —— 回合时间也不例外。
            // 这里算一次、下面各分支都用它；手动打开战术面板不受影响（那是独立窗口）。
            bool alertsAllowed = AlertsAllowed;

            // ---- 回合开始 ----
            AlertConfig c0 = _cfg.Alerts[I_RoundStart];

            if (On(c0) && _roundStartShowing && (now < _roundStartUntil || c0.Persistent))
                Drive(_forms[I_RoundStart], true, _roundStartText);
            else
            {
                _roundStartShowing = false;
                Drive(_forms[I_RoundStart], false, "");
            }

            // ---- 回合时间 ----
            AlertConfig c1 = _cfg.Alerts[I_RoundTimer];
            bool showRound = false;
            string roundText = "";
            if (On(c1) && _roundPreparing && alertsAllowed)
            {
                // 需求七.1：准备阶段不显示正式时间，而是明说在等角色分配
                roundText = WaitingRoleText;
                showRound = true;
            }
            else if (On(c1) && alertsAllowed && (_roundActive || _est.Running))
            {
                double elapsed = _est.Running ? _est.RoundElapsed : (now - _roundStart).TotalSeconds;
                // 中途加入（不是从开局开始数的）→ 只说"当前已计时 Ns"，
                // 别装成真实回合时间；用户校准过 / 正常开局就回到原来的样子。
                if (_midJoinRound && !_est.Calibrated)
                    roundText = "当前已计时 " + (int)elapsed + "s";
                else
                    roundText = SafeFormat(c1.Format, new object[] { TimeEstimator.Clock(elapsed, false) });
                // Persistent = 常驻不缩回
                showRound = c1.Persistent || _forceShowAll || now < _roundPopUntil;
            }
            Drive(_forms[I_RoundTimer], showRound, roundText);

            // ---- 进入服务器 ----
            AlertConfig c2 = _cfg.Alerts[I_ServerJoin];
            if (On(c2) && _joinShowing && (now < _joinUntil || c2.Persistent))
                Drive(_forms[I_ServerJoin], true, _joinText);
            else
            {
                _joinShowing = false;
                Drive(_forms[I_ServerJoin], false, "");
            }

            // ---- 游戏启动 / 退出 ----
            AlertConfig c3 = _cfg.Alerts[I_GameLaunch];
            if (On(c3) && _gameShowing && (now < _gameUntil || c3.Persistent))
                Drive(_forms[I_GameLaunch], true, _gameText);
            else
            {
                _gameShowing = false;
                Drive(_forms[I_GameLaunch], false, "");
            }

            // ---- 启动提示（钉在屏幕顶端）----
            // 【注意】它必须排在下面那道"回合没开始就全部收起"的闸前面：
            // 启动横幅是程序自己的提示，和回合状态无关。
            AlertConfig su = _cfg.Alerts[I_Startup];
            if (On(su) && _startupShown && now < _startupUntil)
                Drive(_forms[I_Startup], true, _startupText);
            else
            {
                _startupShown = false;
                Drive(_forms[I_Startup], false, "");
            }

            // ============================================================
            // 【操作者要求】回合结束之后、下一局正式开始之前（含"分配角色"那段），
            // 一条对局内的提示都不许挂在屏幕上。
            //
            // ⚠ 这里的条件只看状态，不主动改状态：
            //   · `_roundActive` 只在"回合结束 / 回主菜单 / 换服"时被置 false；
            //   · `_roundPreparing`（分配角色）期间也算"还没开局"。
            // 曾经写成在 ClearAllRoundData 里把 `_roundActive` 打成 false ——
            // 而"进图"也会调它（一局中途重连、日志重读都会再来一遍），
            // 结果整局的对局内提示全被收掉，操作者看到的就是
            // "回合开始的计时好像不会开始了"。别再把状态改回来放这儿。
            // ============================================================
            if (!_roundActive || _roundPreparing || !alertsAllowed)
            {
                RetractRoundAlerts();
                if (_deadManAt != DateTime.MinValue)
                {
                    _deadManAt = DateTime.MinValue;      // 对局都结束了，协议也不再走
                    _wasBothOut = false;
                }
                return;
            }

            // ---- 三个倒计时 ----
            ApplyCountdown(I_Warhead, now);
            ApplyCountdown(I_Decon, now);
            // ---- SCP 阵亡 ----
            AlertConfig scp = _cfg.Alerts[I_Scp];
            if (On(scp) && _scpShowing && (now < _scpUntil || scp.Persistent))
                Drive(_forms[I_Scp], true, _scpText);
            else
            {
                _scpShowing = false;
                Drive(_forms[I_Scp], false, "");
            }

            ApplyCountdown(I_Support, now);

            // ---- 重生令牌阈值预警（需求二）----
            // 亡者协议由红色专用提示显示，不复用令牌提示窗口。
            // 【只在游戏运行时弹】操作者要求：游戏没开（在桌面/主菜单挂着）时
            // 重生令牌提示不许弹出来 —— 数据本来就该只认游戏里的屏幕，
            // 进程都不在还弹提示只会吓人。亡者协议倒计时也一样压住。
            AlertConfig tok = _cfg.Alerts[I_Token];
            bool gameUp = _game != null && _game.IsRunning;
            if (gameUp && On(tok) && _tokenShowing && (now < _tokenUntil || tok.Persistent))
                Drive(_forms[I_Token], true, _tokenText);
            else
            {
                _tokenShowing = false;
                Drive(_forms[I_Token], false, "");
            }

            // ---- 角色分配提示（需求六）----
            AlertConfig role = _cfg.Alerts[I_Role];
            if (On(role) && _roleShowing && (now < _roleUntil || role.Persistent))
                Drive(_forms[I_Role], true, _roleText);
            else
            {
                _roleShowing = false;
                Drive(_forms[I_Role], false, "");
            }

            // ---- 启动提示已在闸门之前处理（见上面的注释）----

            // ---- 认不出击杀者的"SCP 被击杀"（被动语态）----
            AlertConfig sk = _cfg.Alerts[I_ScpKilled];
            if (On(sk) && _scpKilledShowing && (now < _scpKilledUntil || sk.Persistent))
                Drive(_forms[I_ScpKilled], true, _scpKilledText);
            else
            {
                _scpKilledShowing = false;
                Drive(_forms[I_ScpKilled], false, "");
            }

            // ---- 死亡界面的票数 / 影响力（需求八）----
            AlertConfig tk = _cfg.Alerts[I_Ticket];
            if (On(tk) && _ticketShowing && (now < _ticketUntil || tk.Persistent))
                Drive(_forms[I_Ticket], true, _ticketText);
            else
            {
                _ticketShowing = false;
                Drive(_forms[I_Ticket], false, "");
            }

            // ---- 识别到刷新增援的提示（需求八附）----
            AlertConfig wv = _cfg.Alerts[I_Wave];
            if (On(wv) && _waveShowing && (now < _waveUntil || wv.Persistent))
                Drive(_forms[I_Wave], true, _waveText);
            else
            {
                _waveShowing = false;
                Drive(_forms[I_Wave], false, "");
            }

            ApplyGenerators(now);

            // ---- 重生概率提示（操作者要求）----
            AlertConfig rp = _cfg.Alerts[I_Respawn];
            if (On(rp) && _respawnShowing && (now < _respawnUntil || rp.Persistent))
                Drive(_forms[I_Respawn], true, _respawnText);
            else
            {
                _respawnShowing = false;
                Drive(_forms[I_Respawn], false, "");
            }

            // ---- 亡者协议倒计时（两方令牌都用尽时单独显示）----
            // 操作者要求：放屏幕底端、活着也显示。
            // 核弹是**另一条**独立的提示：终焉归零只把这一条切成"已启动"，
            // 不会去点亮 / 修改核弹那条。
            AlertConfig dm = _cfg.Alerts[I_DeadMan];
            if (On(dm) && DeadManActive && DeadManRemaining > 0)
            {
                double leftDm = DeadManRemaining;
                string dmText = "亡者协议 T-" + TimeText.ForPrompt(_cfg, _cfg.Alerts[I_DeadMan], leftDm);
                _forms[I_DeadMan].SetTimer(leftDm, _cfg.DeadManSeconds);
                Drive(_forms[I_DeadMan], true, SafeFormat(dm.Format, new object[] { dmText }));
                _forms[I_DeadMan].Highlight = leftDm <= 30;   // 最后 30 秒高亮
            }
            else if (On(dm) && now < _deadManDoneUntil)
            {
                // 归零后单独留 8 秒"终焉协议已启动"，和核弹倒计时分开显示。
                _forms[I_DeadMan].SetTimer(0, _cfg.DeadManSeconds);
                Drive(_forms[I_DeadMan], true, "终焉协议已启动（核弹另计）");
                _forms[I_DeadMan].Highlight = true;
            }
            else
            {
                _forms[I_DeadMan].SetTimer(0, 0);
                Drive(_forms[I_DeadMan], false, "");
                _forms[I_DeadMan].Highlight = false;
            }

            // ---- 发电机被激活的提醒（一次性弹出）----
            AlertConfig ga = _cfg.Alerts[I_GenActive];
            if (On(ga) && _genActiveShowing && (now < _genActiveUntil || ga.Persistent))
                Drive(_forms[I_GenActive], true, _genActiveText);
            else
            {
                _genActiveShowing = false;
                Drive(_forms[I_GenActive], false, "");
            }

            // ---- 自己阵亡 / 自己重生（操作者要求：这两件事也要能配音频）----
            AlertConfig ld = _cfg.Alerts[I_LocalDeath];
            if (On(ld) && _selfDeathShowing && (now < _selfDeathUntil || ld.Persistent))
                Drive(_forms[I_LocalDeath], true, _selfDeathText);
            else
            {
                _selfDeathShowing = false;
                Drive(_forms[I_LocalDeath], false, "");
            }

            AlertConfig sr = _cfg.Alerts[I_SelfRespawn];
            if (On(sr) && _selfRespawnShowing && (now < _selfRespawnUntil || sr.Persistent))
                Drive(_forms[I_SelfRespawn], true, _selfRespawnText);
            else
            {
                _selfRespawnShowing = false;
                Drive(_forms[I_SelfRespawn], false, "");
            }

            // ---- 提示音：在“从没显示 -> 刚显示”的那一刻响 ----
            PlayRisingSounds();

            // ---- 自定义音频触发点（操作者要求）----
            CheckAudioTriggers(now);
        }

        // ============================================================
        // 发电机被激活 → 弹一条提醒
        //
        // 【为什么是"盯着模型看变化"，而不是在 OCR 那条路上直接弹】
        // "某一台被确认"这件事可能来自播报识别，也可能来自以后加的手动确认；
        // 盯模型状态最省事：谁确认的都跑不掉，而且播报重复报几次也不会重复弹。
        // 判定用的是 Generator.Confirmed（播报确认），不是 Done ——
        // 倒计时走完只是本地估计，不该拿它来喊"已激活"。
        // ============================================================

        /// <summary>上一拍各台的确认状态，用来发现"刚刚被确认"。</summary>
        private readonly bool[] _genActiveSeen = new bool[TacticalLog.GeneratorCount];
        private string _genActiveText = "";
        private bool _genActiveShowing;
        private DateTime _genActiveUntil;

        // ============================================================
        // 重生概率提示（操作者要求）
        //
        // "人类整体还能不能再刷一波"的概率（PredictAnyRespawnChance）越过阈值
        // （默认 60%）就弹一条，让玩家知道该不该继续囤票、还有几波。
        // 只在跨过阈值往上的那一刻弹一次；掉下去之后又涨上来才会再弹，
        // 免得一直挂在屏幕上刷。
        // ============================================================

        private string _respawnText = "";
        private bool _respawnShowing;
        private DateTime _respawnUntil;
        private bool _respawnWasHigh;

        private void CheckRespawnChance(DateTime now)
        {
            if (!_roundActive || _tactical == null) return;

            int threshold = _cfg.RespawnAlertPercent;
            double p = _tactical.PredictAnyRespawnChance(now);
            bool high = p * 100.0 >= threshold;

            if (high && !_respawnWasHigh)
            {
                _respawnText = "人类还有 " + TacticalLog.PercentText(p)
                    + " 的机会再重生一波（令牌 / 影响力）";
                _respawnShowing = true;
                _respawnUntil = now.AddSeconds(Math.Max(4, _cfg.Alerts[I_Respawn].DurationSeconds));
                Logger.On("回合", "重生概率达到 " + TacticalLog.PercentText(p)
                    + "（阈值 " + threshold + "%）→ 弹出提示");
            }
            else if (!high && _respawnWasHigh)
            {
                Logger.On("回合", "重生概率回落到 " + TacticalLog.PercentText(p) + "（低于阈值）");
            }
            _respawnWasHigh = high;
        }

        private void CheckGeneratorActivations(DateTime now)
        {
            for (int i = 0; i < TacticalLog.GeneratorCount; i++)
            {
                TacticalLog.Generator g = _tactical.Generators[i];
                bool on = g != null && g.Confirmed;
                if (on == _genActiveSeen[i]) continue;
                _genActiveSeen[i] = on;
                if (!on) continue;          // 换局重置了，不弹

                string who = TacticalLog.FactionName(g.Faction);
                string text = "发电机 " + (i + 1) + " 已被激活（"
                    + _tactical.GeneratorsActive + "/" + TacticalLog.GeneratorCount + "）";
                if (who != "未指定") text += "  ·  " + who;

                AlertConfig a = _cfg.Alerts[I_GenActive];
                // （分台音频已由「自定义触发点」取代：条件 脉冲.gen.activate + 第几台）
                _genActiveText = SafeFormat(a.Format, new object[] { text });
                _genActiveShowing = true;
                _genActiveUntil = now.AddSeconds(Math.Max(4, a.DurationSeconds));
                Logger.On("发电机", text + " → 已弹出提醒");
            }
        }

        /// <summary>
        /// SCP-079 发电机提醒。
        ///
        /// 两种情况要提示：
        ///   · 有发电机进入最后 20 秒（快启动了，079 会收到警告）
        ///   · 已经启动 2 台、最后 1 台也进入最后 20 秒 —— 这时候马上就要重收容，
        ///     所以要高亮，让玩家别错过按按钮的时机
        /// </summary>
        /// <summary>
        /// SCP-079 这一局是不是已经死了。
        /// 死了之后"去 079 收容室按按钮"那条就没意义了 —— 必须收起来，
        /// 不然它会一直挂在屏幕上（操作者报的"一直黏着"）。
        /// </summary>
        private bool Scp079Dead()
        {
            try
            {
                TacticalLog.ScpRecord r = _tactical.Find("079");
                return r != null && r.Killed;
            }
            catch { return false; }
        }

        private void ApplyGenerators(DateTime now)
        {            AlertConfig a = _cfg.Alerts[I_Gen];
            IAlertSurface form = _forms[I_Gen];

            if (!On(a) || !_tactical.Has079 || Scp079Dead())
            {
                Drive(form, false, "");
                form.Highlight = false;
                return;
            }

            // 找出台数最多的一台“正在启动且快好了”
            int soonIdx = -1;
            double soonLeft = double.MaxValue;
            for (int i = 0; i < TacticalLog.GeneratorCount; i++)
            {
                TacticalLog.Generator g = _tactical.Generators[i];
                if (g.Done || !g.Running) continue;
                double left = g.Remaining(now);
                if (left > 0 && left <= TacticalLog.GenSoonSeconds && left < soonLeft)
                {
                    soonLeft = left;
                    soonIdx = i;
                }
            }

            // 三台都好了 → 提示去按按钮
            // 【操作者要求】只弹一会儿，不要一直挂在屏幕上
            //（以前这里每个 tick 都无条件 Drive，于是那条"三台发电机已就绪"永远赖着）。
            if (_tactical.AllGeneratorsReady)
            {
                // 【操作者报：这条一直黏在屏幕上】
                // 以前这里用的是这条提示自己的 DurationSeconds（默认 120 秒！），
                // 于是"三台已就绪 —— 去 079 收容室按按钮"会在屏幕上挂两分钟。
                // 现在用专门的一小段（`[GeneratorAlert] AllReadySeconds`，默认 12 秒）。
                int showSec = _cfg.GenAllReadySeconds;
                if (showSec < 3) showSec = 3;

                if (_genAllReadyUntil == DateTime.MinValue)
                {
                    _genAllReadyUntil = now.AddSeconds(showSec);
                    Logger.On("发电机", "三台发电机已就绪 → 弹出『去 079 收容室按按钮』，"
                        + showSec + " 秒后自动收起");
                }

                if (now < _genAllReadyUntil)
                {
                    form.Highlight = true;
                    Drive(form, true, SafeFormat(a.Format,
                        new object[] { "三台发电机已就绪 —— 去 079 收容室按按钮" }));
                }
                else
                {
                    form.Highlight = false;
                    Drive(form, false, "");
                }
                return;
            }
            _genAllReadyUntil = DateTime.MinValue;   // 不再"全部就绪" → 下次重新计时

            if (soonIdx < 0)
            {
                Drive(form, false, "");
                form.Highlight = false;
                return;
            }

            // 最后 1 台 + 已有 2 台 → 高亮（这就是“前 20 秒高亮提醒”）
            bool urgent = _tactical.FinalGeneratorUrgent(now);
            form.Highlight = urgent || now < _genPulseUntil;

            string who = TacticalLog.FactionName(_tactical.Generators[soonIdx].Faction);
            string text = "发电机 " + (soonIdx + 1) + " 快启动了（还剩 "
                        + TimeEstimator.Clock(soonLeft, true) + "）";
            if (who != "未指定") text += "  · " + who;
            if (urgent) text = "⚠ " + text + "  —— 马上可重收容";

            // （分台音频已由「自定义触发点」取代）
            Drive(form, true, text);
        }

        private DateTime _genPulseUntil = DateTime.MinValue;

        /// <summary>
        /// 每条提示"这一次弹出来该用哪个分台/分进度音频"（0 = 没有）。
        /// 发电机那两条用它：激活的是第几台 / 快启动的是第几台，
        /// 由 <see cref="CheckGeneratorActivations"/> 和 <see cref="ApplyGenerators"/> 写好，
        /// <see cref="PlayRisingSounds"/> 在弹出来那一刻取用。
        /// </summary>
        private readonly int[] _alertSlot;

        /// <summary>"三台发电机已就绪"那条提示该收到什么时候（MinValue = 还没弹）。</summary>
        private DateTime _genAllReadyUntil = DateTime.MinValue;

        /// <summary>
        /// 逐条比较这一帧和上一帧的显示状态，只在刚弹出来的那一瞬间响一声。
        /// 这样不管是倒计时开始、回合开始还是 SCP 阵亡，都能统一带上音效。
        ///
        /// 【顺便把"哪个提示显示了/收起了"写进日志】操作者要求：
        /// 以后凡是屏幕上出现过的提示都要能在日志里查到（排查"刚才那条是什么"时很关键）。
        /// 只在状态跳变时记，不跟着每 200ms 的重绘刷屏。
        /// </summary>
        private void PlayRisingSounds()
        {
            for (int i = 0; i < _forms.Length && i < _wasShowing.Length; i++)
            {
                bool showing = false;
                try { showing = _forms[i].IsPopped; } catch { }

                if (showing && !_wasShowing[i])
                {
                    AlertConfig a = _cfg.Alerts[i];
                    if (a.SoundOn) AlertSound.Play(a.SoundKind, i, a.SoundCooldown);

                    // 用户自备音频：优先级
                    //   ① 这一条提示的"分台 / 分进度"音频（发电机那种"第几台"）——
                    //      驱动方在弹出来之前把槽位写进 _alertSlot；
                    //   ② 这条提示自己的 AudioFile（可以是文件，也可以是 @片段名）；
                    //   ③ 全局默认 AudioDefault。
                    // 音量 / 裁剪 / 片段都在「音频」页里按"这一段音频"设（PlayKey 自己查）。
                    int slot = (i < _alertSlot.Length) ? _alertSlot[i] : 0;
                    string slotClip = slot > 0 ? AudioLibrary.PointFile(a.AudioPoints, slot) : null;
                    if (i < _alertSlot.Length) _alertSlot[i] = 0;   // 用完就清，别影响下一波

                    string clip = !string.IsNullOrEmpty(slotClip) ? slotClip
                                : (!string.IsNullOrEmpty(a.AudioFile) ? a.AudioFile : _cfg.AudioDefault);
                    if (!string.IsNullOrEmpty(clip))
                        AudioLibrary.PlayKey(clip, "提示「" + a.DisplayName + "」"
                            + (slotClip != null ? "（第 " + slot + " 台）" : ""));
                    Logger.On("提示", "显示「" + a.DisplayName + "」：" + FormTextOf(i)
                        + (slotClip != null ? "（用的是第 " + slot + " 台的分台音频）" : ""));
                }
                else if (!showing && _wasShowing[i])
                {
                    Logger.On("提示", "收起「" + _cfg.Alerts[i].DisplayName + "」");
                }
                _wasShowing[i] = showing;
            }
        }

        /// <summary>读某条提示当前显示的文字（只给日志用）。</summary>
        private string FormTextOf(int i)
        {
            try
            {
                string s = _forms[i].CurrentText;
                if (string.IsNullOrEmpty(s)) return "";
                s = s.Replace("\r", " ").Replace("\n", " ");
                return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
            }
            catch { return ""; }
        }

        // 够用就行；以前写死 16 正好等于当时的提示条数，
        // 再加一种提示就会漏掉最后一条的音效（数组长度不匹配就直接跳过）。
        private readonly bool[] _wasShowing = new bool[64];

        /// <summary>
        /// 倒计时类提示的统一处理。
        ///
        /// 两种驱动方式：
        ///   · 日志驱动（自己开服）：日志里明确写了“核弹开始 / 净化开始”时，
        ///     用精确值塞进估算器，之后一样由估算器走。
        ///   · 估算驱动（加入别人的服务器）：靠“回合已进行多久”推算。
        ///
        /// 显示规则：
        ///   · 倒计时期间常驻
        ///   · Persistent = 1 时即使归零 / 归零文本过期也不缩回
        ///   · 命中 PulseAt 阈值闪一下，低于 HotBelow 持续高亮
        /// </summary>
        private void ApplyCountdown(int idx, DateTime now)
        {
            AlertConfig a = _cfg.Alerts[idx];
            Countdown st = _cd[idx];
            IAlertSurface form = _forms[idx];

            // 估算器驱动这一项：剩余时间直接从估算表取
            if (a.Estimated)
            {
                if (!_est.Running)
                {
                    form.Highlight = false;
                    Drive(form, false, "");
                    return;
                }
                ApplyEstimated(idx, a, form, now);
                return;
            }

            // ---- 下面是没有开估算的原有逻辑（完全靠日志事件） ----
            if (st.Counting && now >= st.End)
            {
                st.Counting = false;
                st.Ended = true;
                st.EndUntil = now.AddSeconds(_cfg.DisplaySeconds);
                st.PulseUntil = DateTime.MinValue;
            }

            double remain = (st.End - now).TotalSeconds;

            // 触发脉冲阈值（每个阈值每轮只触发一次）
            if (st.Counting && a.PulseAt != null)
            {
                for (int k = 0; k < a.PulseAt.Length; k++)
                {
                    int t = a.PulseAt[k];
                    if (t <= 0) continue;
                    if (remain <= t && remain > 0 && !st.Fired.Contains(t))
                    {
                        st.Fired.Add(t);
                        st.PulseStart = now;
                        st.PulseUntil = now.AddSeconds(a.PulseSeconds);
                    }
                }
            }

            // ---- 倒计时到点放音频（操作者要求）----
            // 写法：AudioPoints=60=一分钟.mp3,30=紧张.mp3,10=最后警告.mp3
            // 每个阈值每轮只放一次；命中的时候把上一段停掉（不叠音）。
            if (st.Counting && !string.IsNullOrEmpty(a.AudioPoints))
            {
                int[] pts = AudioLibrary.ParsePoints(a.AudioPoints);
                for (int k = 0; k < pts.Length; k++)
                {
                    int t = pts[k];
                    if (t <= 0 || st.AudioFired.Contains(t)) continue;
                    if (remain > t || remain <= 0) continue;

                    st.AudioFired.Add(t);
                    string clip = AudioLibrary.PointFile(a.AudioPoints, t);
                    if (!string.IsNullOrEmpty(clip))
                        AudioLibrary.PlayKey(clip, a.DisplayName + " 倒计时剩 " + t + " 秒");
                }
            }

            // 计算高亮
            bool highlight = false;
            if (st.Counting)
            {
                if (a.HotBelow > 0 && remain > 0 && remain <= a.HotBelow)
                    highlight = true;                                   // 持续高亮
                else if (now < st.PulseUntil)
                    highlight = ((int)((now - st.PulseStart).TotalMilliseconds / 320) % 2) == 0;  // 闪一下
            }
            form.Highlight = highlight;

            if (!On(a)) { form.SetTimer(0, 0); Drive(form, false, ""); }
            else if (st.Counting)
            {
                form.SetTimer(Math.Max(0, (st.End - now).TotalSeconds), st.Total);
                Drive(form, true, SafeFormat(a.Format, new object[] { ToClock(st.End - now, true) }));
            }
            else if (st.Ended && (now < st.EndUntil || a.Persistent))
            {
                form.SetTimer(0, st.Total);
                Drive(form, true, a.EndText);
            }
            else
            {
                st.Ended = false;
                st.Total = 0;
                form.Highlight = false;
                form.SetTimer(0, 0);
                Drive(form, false, "");
            }
        }

        /// <summary>用估算器驱动的倒计时显示。</summary>
        private void ApplyEstimated(int idx, AlertConfig a, IAlertSurface form, DateTime now)
        {
            double remain;
            bool started;
            double timerTotal = 0;

            if (idx == I_Warhead)
            {
                timerTotal = _est.WarheadSeconds;
                remain = _est.WarheadRemaining;
                started = _est.WarheadRunning;
                if (!started)
                {
                    // 核弹没在走：显示“已取消，重启将从 xx 继续”这种状态就不必了，
                    // 直接把窗口收起来，免得一直挂在屏幕上。
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                    return;
                }
            }
            else if (idx == I_Decon)
            {
                // 中途加入且还没认到真实净化播报 → 这条不计时、不显示。
                if (_est.DeconDisabled && !_est.DeconCountdownStarted && !_est.DeconLocked)
                {
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                    return;
                }
                timerTotal = _est.DeconSeconds;
                // 净化从"净化程序将在倒计时 N 分钟后进行"那句开始计时（操作者口径）。
                // 计时起表之后才显示这条提示：
                //   · 起过表（认到播报 / F9 / 清除生物锁死）→ 立刻显示（见 _estPopUntil）；
                //   · 还没起表 → 只有"快归零了"（默认最后 60 秒）才显示，
                //     免得常驻配置下从回合一开始就挂着一条实际没在走的净化倒计时。
                remain = _est.DeconRemaining;
                bool deconAnchored = _est.DeconCountdownStarted || _est.DeconLocked;
                if (!deconAnchored && remain > DeconFallbackShowSeconds)
                {
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                    return;
                }
                started = true;
            }
            else
            {
                remain = _est.SupportRemaining;
                started = true;

                // 【操作者要求】支援刷新只在活着的时候弹：
                // 死了要么在等复活、要么在看别人打，这条挂在那儿没意义。
                if (_isDeadNow)
                {
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                    return;
                }

                // 【操作者报的 bug：大波刚刷完就弹"即将刷新小波"】
                // 以前这条倒计时算的是"回合已进行多少秒 vs 配置的支援时长"，
                // 和真实的刷新节奏毫无关系 —— 它的"最后 30 秒"随时可能撞上
                // 大波刚刷完的那一刻，于是冒出一条"即将刷新小波"（时间也是错的）。
                // 现在只要认出过刷新播报（HasBigWave），就改成盯下一波真实刷新：
                //   · 待刷小波还没记过、而且它比下一次大波更早 → 盯小波（主波后 2 分 30 秒）
                //   · 否则 → 盯下一次大波（按刷新间隔算）
                // 这样"即将刷新小波"只会在小波真的要来时出现。
                if (_tactical.HasBigWave)
                {
                    double mini = _tactical.MiniWaveRemaining(now);
                    double big = _tactical.WaveRemaining(now);
                    if (mini >= 0 && (big <= 0 || mini < big)) remain = mini;
                    else if (big > 0) remain = big;
                    else remain = 0;
                }
            }

            // 新版提示的底部读数条：核弹 / 净化有可靠总时长，支援刷新只有剩余秒数，
            // 所以后者传 0（保持扫描线，不假装知道进度）。
            form.SetTimer(Math.Max(0, remain), timerTotal);

            // 阈值闪烁（用一个全局槽位就够，同一时刻一般只有一个在闪）
            if (a.PulseAt != null && remain > 0)            {
                for (int k = 0; k < a.PulseAt.Length; k++)
                {
                    int t = a.PulseAt[k];
                    if (t <= 0) continue;
                    if (remain <= t && remain > t - 1.0 && !_estFired.Contains(idx + ":" + t))
                    {
                        _estFired.Add(idx + ":" + t);
                        _estPulse[idx] = now.AddSeconds(a.PulseSeconds);
                    }
                }
            }

            bool highlight = false;
            if (remain > 0)
            {
                if (a.HotBelow > 0 && remain <= a.HotBelow) highlight = true;
                else if (now < _estPulse[idx])
                    highlight = ((int)((now - _estPulse[idx]).TotalMilliseconds / 320) % 2) == 0;
            }
            form.Highlight = highlight;

            if (!On(a)) { form.SetTimer(0, 0); Drive(form, false, ""); return; }

            // ---- 长周期倒计时的显示策略：不常显，每隔 FlashPeriodSeconds 秒露一段，
            //      临近（SoonSeconds 以内）才持续显示 ----
            //
            // 刚"起表"那一刻例外（_estPopUntil）：先完整显示一段。
            // 【以前的两个毛病】
            //   ① 节拍写死"每分钟"，操作者要求支援刷新改成每 30 秒一次；
            //   ② 多了一个 `_tactical.HasWaveAnchor` 条件 —— 还没认出任何刷新播报时
            //      直接跳过这里，掉到下面 "remain > 0 就一直显示"，
            //      于是"没勾常驻却一直挂着"。现在没有锚点只是换成普通倒计时文字。
            if (a.FlashPerMinute && !a.Persistent && remain > a.SoonSeconds
                && now >= _estPopUntil[idx])
            {
                double period = a.FlashPeriodSeconds > 0 ? a.FlashPeriodSeconds : 60.0;
                double into = remain - Math.Floor(remain / period) * period;
                double sinceBoundary = period - into;
                bool visible = sinceBoundary < a.FlashSeconds;

                if (visible)
                {
                    // 有刷新锚点时显示的是"大波/小波"的预计内容；
                    // 没有锚点就老老实实显示这条提示自己的倒计时文字。
                    string txt = _tactical.HasWaveAnchor
                        ? WaveText(a, remain, now)
                        : SafeFormat(a.Format, new object[] { TimeText.ForPrompt(_cfg, a, remain) });
                    Drive(form, true, txt);
                }
                else
                {
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                }
                return;
            }

            // 还没到 / 归零后
            if (remain > 0)
            {
                // 即将刷新（最后 SoonSeconds 秒）：换成“即将刷新大波/小波”
                if (a.FlashPerMinute && remain <= a.SoonSeconds)
                {
                    form.Highlight = true;
                    Drive(form, true, ComingText(now));
                    return;
                }
                // 【"约"要贴着时间，不能加在整句话前面】
                // 以前是 prefix + SafeFormat(整句)，未校准时会变成
                //     "约 净化倒计时 10:00"
                // 读起来像"约净化"这个动作。正确写法是把标记并进时间参数：
                //     "净化倒计时 约 10:00"
                // {0} 对这些倒计时提示来说就是时间，所以并进 {0} 即可。
                string clock = (_est.Calibrated ? "" : "约 ")
                             + TimeText.ForPrompt(_cfg, a, remain);
                Drive(form, true, SafeFormat(a.Format, new object[] { clock }));
            }
            else
            {
                // 已经归零：显示结束语，Persistent 时一直留着
                string end = string.IsNullOrEmpty(a.EndText) ? SafeFormat(a.Format, new object[] { "00:00" }) : a.EndText;
                if (a.Persistent) Drive(form, true, end);
                else if (now < _estEndUntil[idx])
                {
                    _estEndUntil[idx] = now.AddSeconds(_cfg.DisplaySeconds);
                    Drive(form, true, end);
                }
                else
                {
                    form.Highlight = false;
                    form.SetTimer(0, 0);
                    Drive(form, false, "");
                }
            }
        }

        private readonly DateTime[] _estPulse = new DateTime[16];
        private readonly DateTime[] _estEndUntil = new DateTime[16];

        /// <summary>
        /// 每条倒计时"刚起表，先完整显示到什么时候"（默认 MinValue = 不用这个窗口）。
        /// 净化开始计时那一刻用它弹一次通知（操作者要求：开始计时要有提示，
        /// 而且"常驻"也要等到开始计时之后才挂上去）。
        /// </summary>
        private readonly DateTime[] _estPopUntil = new DateTime[16];

        /// <summary>净化还没起表时，只有快归零了才显示（秒）。</summary>
        private const double DeconFallbackShowSeconds = 60;
        private readonly System.Collections.Generic.List<string> _estFired =
            new System.Collections.Generic.List<string>();

        /// <summary>每分钟露脸时显示的内容：大波还是小波、归哪一方。</summary>
        private string WaveText(AlertConfig a, double remain, DateTime now)
        {
            string kind = WaveKind(now);
            string who = WaveSide(now);
            string tail = who.Length > 0 ? "（" + who + "）" : "";
            return kind + tail + "  " + TimeText.ForPrompt(_cfg, a, remain);
        }

        /// <summary>即将刷新的通知：即将刷新大波 / 小波。</summary>
        private string ComingText(DateTime now)
        {
            string kind = WaveKind(now);
            string who = WaveSide(now);
            return who.Length > 0 ? "即将刷新" + kind + " —— " + who : "即将刷新" + kind;
        }

        /// <summary>现在轮到的是大波还是小波。</summary>
        private string WaveKind(DateTime now)
        {
            // 【操作者报的 bug】以前这里卡的是 BigWaveWindow（主波后 2 分钟整），
            // 而小波按文档最早也要主波后 2 分 30 秒才出现 —— 那个窗口永远赶不上
            // 小波真正到来的时刻，于是不管下一波是小波，都显示"即将刷新大波"。
            // 现在问模型：主波之后、小波还没被记过、还在"小波该来"的时间范围内 → 小波。
            // 具体口径（含 90 秒宽限）见 TacticalLog.NextWaveIsSmall。
            return _tactical.NextWaveIsSmall(now, 0) ? "小波" : "大波";
        }

        /// <summary>
        /// 这一波归哪一方。
        ///   小波：按 2 分钟 / 10 票规则判定；没填数据就返回 ""（不指定阵营）。
        ///   大波：没填数据也说不出阵营，同样返回 ""。
        /// </summary>
        private string WaveSide(DateTime now)
        {
            if (!_tactical.HasFactionData) return "";
            if (WaveKind(now) == "小波")
            {
                string f = _tactical.DecideSmallWave(now);
                return f == TacticalLog.NONE ? "" : TacticalLog.FactionName(f);
            }
            // 大波：谁票多就归谁
            int m = _tactical.TicketsOf(TacticalLog.MTF);
            int c = _tactical.TicketsOf(TacticalLog.CHAOS);
            if (m == c) return "";
            return TacticalLog.FactionName(m > c ? TacticalLog.MTF : TacticalLog.CHAOS);
        }

        /// <summary>换新一局时把估算相关的闪烁状态清掉。</summary>
        private void ResetEstimateVisuals()
        {
            _estFired.Clear();
            for (int i = 0; i < _estPulse.Length; i++)
            {
                _estPulse[i] = DateTime.MinValue;
                _estEndUntil[i] = DateTime.MinValue;
            }
        }

        /// <summary>倒计时向上取整，已用时间向下取整，显示为 mm:ss。</summary>
        private static string ToClock(TimeSpan t, bool ceil)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            double total = ceil ? Math.Ceiling(t.TotalSeconds) : Math.Floor(t.TotalSeconds);
            int sec = (int)total;
            return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
        }

        private static string SafeFormat(string fmt, object[] args)
        {
            if (string.IsNullOrEmpty(fmt)) return "";
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        #endregion

        #region 状态与开关

        public bool EditMode
        {
            get { return _editMode; }
        }

        public bool AllHidden
        {
            get { return _allHidden; }
        }

        public bool GlobalMove
        {
            get { return _globalMove; }
            set { _globalMove = value; }
        }

        /// <summary>按住“显示全部”热键期间为 true：忽略关闭状态和隐藏状态。</summary>
        public bool ForceShowAll
        {
            get { return _forceShowAll; }
            set
            {
                if (_forceShowAll == value) return;
                _forceShowAll = value;
                ApplyAll();
            }
        }

        public int AlertCount
        {
            get { return _cfg.Alerts.Length; }
        }

        public string AlertName(int i)
        {
            return _cfg.Alerts[i].DisplayName;
        }

        public bool AlertEnabled(int i)
        {
            return _cfg.Alerts[i].Style.Enabled;
        }

        /// <summary>快速开关某个提示（通知面板用），立即可见并写回配置。</summary>
        public void SetAlertEnabled(int i, bool on)
        {
            if (_cfg.Alerts[i].Style.Enabled == on) return;
            _cfg.Alerts[i].Style.Enabled = on;
            if (!on)
            {
                _forms[i].Highlight = false;
                _forms[i].Retract();
            }
            _cfg.Save(_configPath);
            ApplyAll();
        }

        public Point GetAlertPosition(int i)
        {
            return _forms[i].RestLocation;
        }

        /// <summary>按像素微调；整体移动模式下所有提示一起动。</summary>
        public void NudgeAlert(int i, int dx, int dy)
        {
            if (_globalMove) MoveAllBy(dx, dy);
            else
            {
                Point p = _forms[i].RestLocation;
                _forms[i].SetRestLocation(p.X + dx, p.Y + dy);
                _lastPos[i] = _forms[i].RestLocation;
            }
            SavePositions();
        }

        /// <summary>把所有提示恢复成默认布局，并清零整体偏移。</summary>
        public void ResetPositions()
        {
            Config def = new Config();
            for (int i = 0; i < _cfg.Alerts.Length && i < def.Alerts.Length; i++)
            {
                _cfg.Alerts[i].Style.X = def.Alerts[i].Style.X;
                _cfg.Alerts[i].Style.Y = def.Alerts[i].Style.Y;
            }
            _cfg.OffsetX = 0;
            _cfg.OffsetY = 0;
            ApplyAllStyles();
            SavePositions();
        }

        /// <summary>调整模式下定期确认“不穿透”，防止样式被意外改回去。</summary>
        public void ReassertEditStyles()
        {
            if (!_editMode) return;
            for (int i = 0; i < _forms.Length; i++)
                _forms[i].ReassertExStyle();
        }

        public void SetEditMode(bool on)
        {
            _editMode = on;
            if (_layout != null) _layout.Suspended = on;
            if (on)
            {
                // 进入调整模式时收回避让偏移，按用户配置的锚点显示；否则用户拖到的
                // 位置和 config 里的位置会对不上。
                for (int i = 0; i < _forms.Length; i++)
                {
                    try
                    {
                        Point p = _forms[i].RestLocation;
                        _forms[i].SetRestLocation(p.X, p.Y);
                    }
                    catch { }
                }
            }
            for (int i = 0; i < _forms.Length; i++)
            {
                AlertConfig a = _cfg.Alerts[i];
                if (on && a.Style.Enabled)
                {
                    _forms[i].EditMode = true;
                    _forms[i].Pop(SampleFor(a));
                }
                else
                {
                    _forms[i].EditMode = false;
                }
            }

            if (!on)
            {
                _allHidden = false;
                _globalMove = false;
                SavePositions();
                ApplyAll();
            }
        }

        private static string SampleFor(AlertConfig a)
        {
            return SafeFormat(a.Format, new object[] { a.SampleText, a.SampleText2, a.SampleText3 });
        }

        /// <summary>演示模式：给弹出来的示例文字加个水印，免得被当成真的回合数据。</summary>
        private bool _demo;

        /// <summary>演示用的示例文字（带水印）。</summary>
        private static string DemoSampleFor(AlertConfig a)
        {
            return "【示例】" + SampleFor(a);
        }

        private DateTime _previewUntil = DateTime.MinValue;

        /// <summary>
        /// 把所有提示弹出来给用户看效果，几秒后自动恢复真实状态。
        /// demo = true 时给文字加「示例」水印（新手教程里用，
        /// 免得用户把演示内容当成真实回合数据）。
        /// </summary>
        public void PreviewAll()
        {
            PreviewAll(false);
        }

        public void PreviewAll(bool demo)
        {
            _demo = demo;
            for (int i = 0; i < _forms.Length; i++)
            {
                if (_cfg.Alerts[i].Style.Enabled)
                    _forms[i].Pop(demo ? DemoSampleFor(_cfg.Alerts[i]) : SampleFor(_cfg.Alerts[i]));
            }
            _previewUntil = DateTime.Now.AddSeconds(Math.Max(3, _cfg.DisplaySeconds));
        }

        /// <summary>把演示用的提示立刻收回（教程离开那一步时用）。</summary>
        public void HideAllNow()
        {
            _previewUntil = DateTime.MinValue;
            _demo = false;
            for (int i = 0; i < _forms.Length; i++)
            {
                try { _forms[i].HideNow(); } catch { }
            }
        }

        public void ToggleVisibleAll()
        {
            _allHidden = !_allHidden;
            if (_allHidden)
                for (int i = 0; i < _forms.Length; i++) _forms[i].Retract();
            else
                ApplyAll();
        }

        /// <summary>
        /// 停止当前所有计时，并把所有提示收起来。
        ///
        /// 和"显示/隐藏全部"的区别：那个只是藏起来，倒计时还在后台走；
        /// 这个是真的把这一局的倒计时和回合时钟全部停掉，屏幕上干干净净。
        ///
        /// 不会关掉任何功能：之后只要再有事件发生 —— 包括 OCR 识别到核弹 /
        /// 净化 / SCP 阵亡 —— 就会自动恢复显示并从那一刻重新计时。
        /// </summary>
        public void StopAllTimers()
        {
            if (_allStopped) return;
            try
            {
                DateTime now = DateTime.Now;
                _allStoppedAt = now;
                _estWasRunningAtStop = _est.Running;
                // 保存普通提示倒计时剩余时间，恢复时从原位置接着走。
                for (int i = 0; i < _cd.Length; i++) { try { _cd[i].Pause(now); } catch { } }

                // 各类"正在显示"的事件标记
                _roundStartShowing = false;
                _joinShowing = false;
                _gameShowing = false;
                _scpShowing = false;

                // 回合时钟 + 准备阶段
                _est.Paused = true;
                _est.Stop();
                _allStopped = true;

                for (int i = 0; i < _forms.Length; i++)
                {
                    try { _forms[i].Retract(); } catch { }
                }

                StatusText = "已停止全部计时（再按一次恢复）";
                Logger.On("计时", "手动停止全部计时并收起全部提示");
            }
            catch (Exception ex)
            {
                Logger.Err("计时", "停止全部计时失败", ex);
            }
        }

        /// <summary>
        /// 停止 / 恢复 全部计时的切换。
        ///
        /// 之前只能靠“有新事件”被动恢复，用户按同一个键没法恢复回来 ——
        /// 现在再按一下就直接恢复，不必等事件。
        /// </summary>
        public void ToggleAllTimers()
        {
            if (_allStopped) ResumeAllTimers();
            else StopAllTimers();
        }

        /// <summary>恢复到“正在计时”的状态（按键触发，不需要等到有新事件）。</summary>
        public void ResumeAllTimers()
        {
            if (!_allStopped) return;
            _allStopped = false;

            // 【关键】"停止全部计时"把估算器也停了（_est.Stop），
            // 以前这里只清标记、不把估算器重新跑起来 ——
            // 结果就是再按一次也恢复不了（回合时钟一直不显示）。
            // 现在接着停住时的已进行时间继续走。
            DateTime now = DateTime.Now;
            if (_allStoppedAt != DateTime.MinValue && _deadManAt != DateTime.MinValue)
                _deadManAt = _deadManAt.Add(now - _allStoppedAt);
            _allStoppedAt = DateTime.MinValue;
            if (_estWasRunningAtStop) _est.ResumeStopped(now);
            _est.Paused = _clockPaused;
            for (int i = 0; i < _cd.Length; i++) { try { _cd[i].Resume(now); } catch { } }
            _estWasRunningAtStop = false;

            // 回合本身还在（只是被按住了），把状态一并还原，
            // 否则回合时间那一行还是不会显示。
            StatusText = "已恢复计时";
            Logger.On("计时", "手动恢复全部计时（回合计时从 "
                + (int)_est.RoundElapsed + " 秒接着走）");
            ApplyAll();
        }

        /// <summary>
        /// OCR 认到"只有对局里才会出现"的东西 → 判定为"中途加入"，从此刻开始计时。
        ///
        /// 【为什么要这一条】玩到一半才开程序时，日志里那句"进图 / 回合开始"
        /// 早就写过去了（程序启动只读新增内容，不会翻旧账），
        /// 所以只能靠屏幕上的证据判断"这一局已经在打了"。
        ///
        /// 【为什么不是"启动 N 秒后就开始计时"】那样在主菜单里开程序也会开始计时：
        /// 人还没进局，钟已经在走 —— 用户看到的就是"一点开软件就自己开始计时"。
        /// 现在只有真认出东西才起算，而且照样是"未校准"的（提示带『约』，F7 可校准）。
        /// </summary>
        public void NoteRoundEvidence(string what)
        {
            if (_disposed) return;
            if (!_game.IsRunning) return;                  // 游戏没开，屏幕上认到的不算数
            if (string.IsNullOrEmpty(what)) what = "屏幕内容";

            // 【操作者报的 bug：玩一半才打开程序 → 所有提示都不弹】
            // 原因是"已经进入服务器"这个标记只有进服 / 进图两条路会置上，
            // 而这两种日志在程序启动之前就写完了（日志监视器只读新行）——
            // 于是 `_inServer` 一直是 false，`AlertsAllowed` 把整局的提示全挡住。
            // 屏幕上认到"只有对局里才会有"的东西（票数、SCP 列表、角色、播报）
            // 本身就证明"我在服务器里、这一局在打"，所以这里也把标记补上。
            EnterServer("屏幕上认到对局数据（" + what + "）");

            if (_roundActive || _roundPreparing) return;   // 已经在计时了，不用再管

            DateTime now = DateTime.Now;
            _roundActive = true;
            _midJoinRound = true;              // 中途加入：回合时间改成"当前已计时 Ns"
            _tactical.BeginMidRoundEstimate(now);
            _tactical.Tickets.MarkMidRoundHistoryUnknown();
            _roundStart = now;
            _est.BeginMidRound(now);
            StatusText = "已开始计时（中途加入，可校准）";
            Logger.On("回合", "认出「" + what + "」→ 判定为中途加入，从此刻开始计时"
                + "（未校准，提示会标『约』；按 F7 可以填一次真实回合时间）");
            ApplyAll();
        }

        /// <summary>游戏进程是不是在跑（供"中途加入"判断用）。</summary>
        public bool SelfDead
        {
            get { return _selfDead; }
        }

        public string LastRole
        {
            get { return _lastRole; }
        }

        public DateTime RoundStartedAt
        {
            get { return _roundStart; }
        }
        public bool GameRunning
        {
            get { return _game != null && _game.IsRunning; }
        }

        /// <summary>
        /// 是不是已经在服务器里（进服 / 进图，或屏幕上认到对局数据）。
        /// 屏幕扫描的「进服后才开始扫」开关要用它。
        /// </summary>
        public bool InServer
        {
            get { return _inServer; }
        }

        /// <summary>本局是不是已经进入计时状态。</summary>
        public bool RoundActive
        {
            get { return _roundActive; }
        }

        /// <summary>当前是不是处于"已停止全部计时"状态。</summary>
        public bool AllStopped
        {
            get { return _allStopped; }
        }

        // ============================================================
        // 【操作者要求】"所有计时器都应当在进入服务器之后再打开"：
        //   · 支援刷新还要额外满足"活着"（死了不弹）；
        //   · 重生令牌 / 亡者协议等不在服务器里一律不弹。
        //
        // 进服（Hi … authenticated）和进图（SceneLoaded 2. Facility）都会把
        // _inServer 置 true；回合结束 / 回主菜单 / 退服 / 游戏退出置 false。
        // 死亡状态 _isDeadNow 由"自己阵亡"置 true，复活或新回合清掉。
        // ============================================================

        /// <summary>是不是已经在服务器里了。</summary>
        private bool _inServer;

        /// <summary>现在是不是死亡状态（死亡界面还在）。</summary>
        private bool _isDeadNow;

        /// <summary>进服 / 进图。</summary>
        private void EnterServer(string why)
        {
            if (_inServer) return;
            _inServer = true;
            _calibPromptsLeft = 2;   // 每次进服刷新校准提示次数
            _calibResolved = false;
            Logger.On("回合", "已进入服务器（" + why + "）→ 开始允许对局提示");
            // OCR 的「进服后才开始扫描」靠这条事件开始工作
            if (ServerStateChanged != null) { try { ServerStateChanged(true); } catch { } }
        }

        /// <summary>回合结束 / 回主菜单 / 退服 / 游戏退出。</summary>
        private void LeaveServer(string why)
        {
            if (!_inServer && !_isDeadNow) return;
            EndDeathScan("离开服务器：" + why);
            _inServer = false;
            _isDeadNow = false;
            // 离开服务器 → SCP 列表监视也停（下次进服再等第一次死亡重新开始）
            DisarmScpScan("离开服务器（" + why + "）");
            NotePulse("pulse.server.leave");
            Logger.On("回合", "已离开服务器（" + why + "）→ 对局提示全部停下");
            if (ServerStateChanged != null) { try { ServerStateChanged(false); } catch { } }
        }

        // ============================================================
        // SCP 列表扫描的新逻辑（操作者要求）：
        //   · 进入服务器后先限时扫一段（默认 30 秒，可配）；超时自动停；
        //   · 扫描到的内容弹悬浮窗展示，并持续扫描（每 ~2.5 秒一次）；
        //   · 第一次死亡后改为常驻，直到下一次进入服务器，
        //     或者用户点了「全部扫描」按钮（也会武装扫描）。
        // ============================================================

        private bool _scpScanArmed;
        private DateTime _scpScanUntil = DateTime.MinValue;
        private string _scpScanUntilWhy = "";

        /// <summary>SCP 列表监视开关变化（true = 开始持续扫）→ Program 接到 OCR。</summary>
        public event Action<bool> ScpScanWatchChanged;

        /// <summary>
        /// 进服 / 离服状态变化（true = 已经进入服务器）。
        /// 【谁在用】OCR 的「进入服务器后才开始扫描」——Program 接到这个事件后
        /// 让 OcrAlertWatcher 重新算一次扫描时机（进服开始扫、离服立刻停）。
        /// </summary>
        public event Action<bool> ServerStateChanged;

        /// <summary>扫描到新的 SCP 列表内容（armed 时才发）→ Program 弹悬浮窗。</summary>
        public event Action<string> ScpScanPopup;

        /// <summary>当前是否在持续扫描 SCP 列表。</summary>
        public bool ScpScanArmed { get { return _scpScanArmed; } }

        /// <summary>武装扫描（开始持续扫左侧 SCP 列表）。</summary>
        public void ArmScpScan(string why)
        {
            ArmScpScan(why, 0);
        }

        /// <summary>武装扫描；seconds &gt; 0 时到点自动停（进服限时）。</summary>
        public void ArmScpScan(string why, int seconds)
        {
            _scpScanUntil = seconds > 0 ? DateTime.Now.AddSeconds(seconds) : DateTime.MinValue;
            _scpScanUntilWhy = why ?? "";
            if (_scpScanArmed)
            {
                Logger.On("扫描", "SCP 列表扫描保持开启（" + why + "，" + (seconds > 0 ? "限时 " + seconds + " 秒" : "不限时") + "）");
                return;
            }
            _scpScanArmed = true;
            // 清掉"这一局已经添加过"的记忆：新一局哪怕 SCP 组合和上一局一样，
            // 武装后的第一次识别也要把它们报出来（否则用户会以为扫描没工作）。
            _scpScanAdded.Clear();
            Logger.On("扫描", "SCP 列表扫描已开启（" + why + "）");
            if (ScpScanWatchChanged != null) { try { ScpScanWatchChanged(true); } catch { } }
        }

        /// <summary>解除扫描。</summary>
        private void DisarmScpScan(string why)
        {
            if (!_scpScanArmed) return;
            _scpScanArmed = false;
            _scpScanUntil = DateTime.MinValue;
            _scpScanUntilWhy = "";
            Logger.On("扫描", "SCP 列表扫描已停止（" + why + "）");
            if (ScpScanWatchChanged != null) { try { ScpScanWatchChanged(false); } catch { } }
        }

        // ============================================================
        // 日志时间校准（「时间」页的一键校准）
        //
        // 用户在设置里扫出日志事件、挑一条填进来：这里负责把那个过去时刻
        // 换算成当前校准需要的数据（还剩多少秒 / 回合已进行多少秒）。
        // 返回是否真的改了。
        // ============================================================

        public bool ApplyLogCalibration(string kind, DateTime logTime)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            DateTime now = DateTime.Now;
            if (logTime > now.AddMinutes(1)) return false;      // 未来 time（时区错）不认
            double passed = (now - logTime).TotalSeconds;
            if (passed < 0) passed = 0;

            try
            {
                switch (kind)
                {
                    case "round":
                    {
                        // 日志那行比真实开局早 StartOffsetSeconds 秒 → 补回去
                        double elapsed = passed + _est.StartOffsetSeconds;
                        _est.CalibrateRoundElapsed(now, elapsed);
                        Logger.On("校准", "按日志回合开始（" + logTime.ToString("HH:mm:ss")
                            + "）→ 回合已进行 " + (int)elapsed + " 秒");
                        NotifyRoundCalibrated();
                        return true;
                    }
                    case "decon":
                    {
                        double remain = _est.DeconSeconds - passed;
                        if (remain <= 1)
                        {
                            Logger.On("校准", "日志里的净化早就走完了（还剩 " + (int)remain + " 秒）→ 不改");
                            return false;
                        }
                        _est.SyncDeconRemaining(now, remain, 0);
                        Logger.On("校准", "按日志净化开始（" + logTime.ToString("HH:mm:ss")
                            + "）→ 净化剩余 " + (int)remain + " 秒");
                        return true;
                    }
                    case "warhead":
                    {
                        double remain = _est.WarheadSeconds - passed;
                        if (remain <= 1)
                        {
                            Logger.On("校准", "日志里的核弹倒计时已经走完了 → 不改");
                            return false;
                        }
                        _est.SyncWarheadRemaining(now, remain, 0);
                        Logger.On("校准", "按日志核弹开始（" + logTime.ToString("HH:mm:ss")
                            + "）→ 核弹剩余 " + (int)remain + " 秒");
                        return true;
                    }
                    case "detonated":
                    {
                        _est.StopWarhead();
                        Logger.On("校准", "按日志核弹引爆（" + logTime.ToString("HH:mm:ss") + "）→ 核弹计时结束");
                        return true;
                    }
                    case "roundend":
                    {
                        _est.Stop();
                        Logger.On("校准", "按日志回合结束（" + logTime.ToString("HH:mm:ss") + "）→ 计时停止");
                        return true;
                    }
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Err("校准", "应用日志校准失败", ex);
                return false;
            }
        }

        /// <summary>现在允许显示"对局内"的提示吗（游戏在跑 + 在服务器里）。</summary>
        private bool AlertsAllowed
        {
            get { return ForceAlertsAllowed || (_game != null && _game.IsRunning && _inServer); }
        }

        /// <summary>
        /// 【测试用】强制允许对局提示：绕过"游戏在跑 + 在服务器里"这两道检查。
        /// 生产流程永远不会去设它（回归测试里没有真游戏进程，只能这样模拟）。
        /// </summary>
        public bool ForceAlertsAllowed { get; set; }

        /// <summary>
        /// 有新事件来了 → 解除"已停止"状态，让提示重新出来。
        /// 快捷键手动触发（含 OCR 自动触发）都会走这里。
        /// </summary>
        private void ResumeFromStop()
        {
            if (!_allStopped) return;
            ResumeAllTimers();
            Logger.On("计时", "检测到新事件，已从“停止全部计时”恢复");
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_tick != null)
            {
                _tick.Stop();
                _tick.Tick -= OnTick;
                _tick.Dispose();
            }
            if (_game != null)
            {
                _game.Started -= OnGameStarted;
                _game.Exited -= OnGameExited;
                _game.Dispose();
            }
            if (_monitor != null)
            {
                _monitor.LineReceived -= OnLine;
                _monitor.StatusChanged -= OnStatus;
                _monitor.Dispose();
            }
            if (_faction != null)
            {
                _faction.Recognized -= OnFactionRecognized;
                _faction.Dispose();
                _faction = null;
            }
            if (_layout != null)
            {
                _layout.Dispose();
                _layout = null;
            }
            ScreenShake.Stop();
            try { FloatingTextOverlay.HideAll(); } catch { }
            try { _roleTimeline.Dispose(); } catch { }
            if (_forms != null)
            {
                for (int i = 0; i < _forms.Length; i++)
                {
                    try { _forms[i].HideNow(); _forms[i].Close(); _forms[i].Dispose(); }
                    catch { }
                }
            }
        }
    }
}

