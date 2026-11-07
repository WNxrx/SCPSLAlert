using System;

namespace SCPSLAlert
{
    /// <summary>
    /// 一局游戏的时间估算表。
    ///
    /// 为什么要“估算”：
    ///   加入别人的服务器时，客户端 Player.log 里没有任何倒计时信息
    ///   （核弹、净化游戏都只播 CASSIE 语音，不写日志）。
    ///   所以只能拿“这一局已经过了多久”去推。
    ///
    /// 核心就是一个基准点：_anchorWall（换算成墙钟的“回合 0 秒”时刻）。
    ///   估算时刻 = _anchorWall + 该事件的固定秒数
    /// 只要校准一次基准点，核弹 / 净化 / 支援三个估算就全都跟着准了。
    ///
    /// 支持三种校准方式（都换算成同一个基准点）：
    ///   · 输入“当前回合已进行时间”
    ///   · 输入“支援刷新还剩多少秒” → 反推回合已进行时间
    ///   · 直接指定某个事件还剩多少秒（核弹快捷键用）
    /// </summary>
    internal class TimeEstimator
    {
        /// <summary>vanilla 默认：回合开始后多久轻收容净化开始。</summary>
        public const int DeconAfterRound = 600;

        /// <summary>vanilla 默认：回合开始后多久刷新 MTF 支援。</summary>
        public const int SupportAfterRound = 330;

        private bool _running;

        /// <summary>换算成墙钟的“回合 0 秒”时刻。</summary>
        private DateTime _anchorWall = DateTime.MinValue;

        /// <summary>记录基准点当时的 TickCount，用来算单调递增的经过时间。</summary>
        private int _anchorTick;

        /// <summary>本局是否校准过（没校准过就只能靠日志事件粗略起点）。</summary>
        private bool _calibrated;

        // ---- 事件时长（秒），都可以在设置里改 ----
        public int DeconSeconds = DeconAfterRound;
        public int SupportSeconds = SupportAfterRound;
        public int WarheadSeconds = 90;

        /// <summary>正在估算中（回合已开始）。</summary>
        public bool Running
        {
            get { return _running; }
        }

        /// <summary>是否校准过。没校准过的时候提示文字里会标“约”。</summary>
        public bool Calibrated
        {
            get { return _calibrated; }
        }

        /// <summary>当前估算的“回合已进行秒数”。</summary>
        /// <summary>
        /// 回合开始时间的修正量（秒）。
        /// 日志里那行"回合开始"比游戏真实开始**早**几秒（实测约 5 秒），
        /// 所以把起点往后挪这么多，显示的时间才是游戏里的时间。
        /// </summary>
        public int StartOffsetSeconds = 5;

        public double RoundElapsed
        {
            get
            {
                if (!_running) return 0;
                // 用单调时钟推进，系统时间被改动时不会跳变。
                // _anchorTick 存的是 Environment.TickCount（毫秒）。
                double s = (NowTick - _anchorTick) / 1000.0 - StartOffsetSeconds;
                return s < 0 ? 0 : s;
            }
        }

        // ---- 手动暂停：暂停期间所有倒计时一起冻结 ----
        private bool _paused;
        private int _pausedAtTick;
        private int _pausedAccumMs;

        public bool Paused
        {
            get { return _paused; }
            set
            {
                if (_paused == value) return;
                _paused = value;
                if (value) _pausedAtTick = Environment.TickCount;
                else _pausedAccumMs += Environment.TickCount - _pausedAtTick;
            }
        }

        /// <summary>扣掉暂停时长之后的单调刻度。</summary>
        private int NowTick
        {
            get { return Environment.TickCount - _pausedAccumMs; }
        }

        /// <summary>本回合的起点（墙钟），给支持窗口显示用。</summary>
        public DateTime RoundStartWall
        {
            get { return _anchorWall; }
        }

        /// <summary>用单调时钟推进估算，避免系统时间被改动时跳变。</summary>
        private void Rebase(DateTime wall, int tick)
        {
            _anchorWall = wall;
            _anchorTick = tick;
        }

        /// <summary>回合开始。日志里看到回合开始事件时调用。</summary>
        public void BeginRound(DateTime wall, int roundElapsedSeconds)
        {
            if (roundElapsedSeconds < 0) roundElapsedSeconds = 0;
            _running = true;
            _calibrated = false;
            _deconDisabled = false;    // 正常开局：净化照常按回合时间推算
            _deconForced = false;      // 新回合，清掉 F9 的手动倒计时
            _deconAnchored = false;    // 也回到"跟着回合时间走"

            // 【新回合必须把"锁死"和"收到过播报"也清掉】
            // 操作的实测：上一局（或被 OCR 误读的一句"开始清除所有生物"）把净化锁成 0 之后，
            // 新一局里那句「净化程序将在倒计时 15 分钟后进行」会被 `if (_deconLocked) return;`
            // 挡掉 —— 表现就是"识别到了但通知不弹；强制弹出时只显示『轻收容净化已开始』、
            // 没有时间"。这两个状态是**一局一份**的，换局必须归零。
            _deconLocked = false;
            _deconAnnounced = false;
            _deconRemainAtAnchor = 0;
            _deconAnchorTick = 0;

            // 把“基准点”挪到 roundElapsedSeconds 秒之前，这样 RoundElapsed 就是真实值
            _anchorWall = wall.AddSeconds(-roundElapsedSeconds);
            _anchorTick = NowTick - roundElapsedSeconds * 1000;
        }

        private bool _deconDisabled;

        /// <summary>中途加入时是否停用了轻收容的推算倒计时（认到真实播报 / F9 会解除）。</summary>
        public bool DeconDisabled
        {
            get { return _deconDisabled; }
        }

        /// <summary>
        /// 中途加入：只起回合时钟，**不推算轻收容倒计时**。
        /// 屏幕上没看到净化播报之前，不显示、也不按回合时间猜净化；
        /// 真认到净化播报 / F9 手动起表时，会重新启用并锚定。
        /// </summary>
        public void BeginMidRound(DateTime wall)
        {
            _running = true;
            _calibrated = false;
            _deconDisabled = true;
            _deconForced = false;
            _deconAnchored = false;
            _deconLocked = false;
            _deconAnnounced = false;
            _deconRemainAtAnchor = 0;
            _deconAnchorTick = 0;
            _anchorWall = wall;
            _anchorTick = NowTick;
        }

        public void Stop()
        {
            // 记下停住的那一刻已经进行了多久 —— 恢复时要接着走，
            // 不能从 0 重新开始（那是"停止全部计时"的切换键再次按下时的行为）。
            _stoppedElapsed = RoundElapsed;
            _running = false;
            _calibrated = false;
        }

        private double _stoppedElapsed;

        /// <summary>
        /// 从"停止"状态接着走（"停止全部计时"再按一次时调用）。
        /// 已经停过就沿用停止那一刻的已进行时间；没停过就什么都不做。
        /// </summary>
        public void ResumeStopped(DateTime wall)
        {
            if (_running) return;

            double keep = _stoppedElapsed;
            if (keep < 0) keep = 0;

            _running = true;
            _anchorWall = wall.AddSeconds(-keep);
            _anchorTick = NowTick - (int)(keep * 1000.0);
        }

        // ============================================================
        // 校准
        // ============================================================

        /// <summary>按“当前回合已进行多少秒”校准。</summary>
        public void CalibrateRoundElapsed(DateTime wall, double elapsedSeconds)
        {
            if (elapsedSeconds < 0) elapsedSeconds = 0;
            _running = true;
            _calibrated = true;
            // 关键：把 tick 基准往前挪 elapsedSeconds*1000 毫秒，
            // 这样 RoundElapsed 立刻就是 elapsedSeconds，之后继续单调递增。
            _anchorWall = wall.AddSeconds(-elapsedSeconds);
            _anchorTick = NowTick - (int)(elapsedSeconds * 1000.0);
        }

        /// <summary>按“支援刷新还剩多少秒”校准。</summary>
        public void CalibrateSupportRemaining(DateTime wall, double remainSeconds)
        {
            double elapsed = SupportSeconds - remainSeconds;
            CalibrateRoundElapsed(wall, elapsed);
        }

        /// <summary>按“净化还剩多少秒”校准。</summary>
        public void CalibrateDeconRemaining(DateTime wall, double remainSeconds)
        {
            // 用校准过的回合基准去推算，就不再需要 F9 那个手动倒计时了
            _deconDisabled = false;
            _deconForced = false;
            _deconAnchored = false;
            double elapsed = DeconSeconds - remainSeconds;
            CalibrateRoundElapsed(wall, elapsed);
        }

        /// <summary>按“核弹还剩多少秒”校准（核弹快捷键用）。</summary>
        public void CalibrateWarheadRemaining(DateTime wall, double remainSeconds)
        {
            // 这个就是 StartWarhead 的别名，保留名字是为了读起来清楚
            StartWarhead(wall, remainSeconds);
        }

        // ============================================================
        // 各事件的剩余时间
        // ============================================================

        /// <summary>
        /// 净化还剩多少秒。
        ///
        /// 【两种口径】
        ///   ① 默认：净化跟着**回合时间**走（回合开始 = 净化倒计时开始），
        ///      OCR 不参与校准 —— 实测 OCR 认净化这条太不稳；
        ///   ② 一旦"净化开始计时"那句播报被认出（"所有生物必须立即转移" /
        ///      F9 手动），就改成 **锚点 + 时长** 驱动：从那一刻按基础时长往下走。
        ///      这是操作者要的口径："那句话是开始计时的标志"。
        ///
        /// 唯一的例外是**锁死**：识别到"开始清除所有生物"（区域真的锁了）→ 归零并锁住。
        ///
        /// ⚠ 以前 `_deconRemainAtAnchor / _deconAnchorTick / _deconForced` 只写不读，
        /// 于是 F9 手动起的净化倒计时**根本没生效**（屏幕上还是按回合时间算的）。
        /// 现在这两个字段真的参与计算了。
        /// </summary>
        public double DeconRemaining
        {
            get
            {
                if (_deconLocked) return 0;
                if (_deconDisabled && !_deconAnchored && !_deconForced) return DeconSeconds;

                if (_deconAnchored)
                {
                    double passed = (NowTick - _deconAnchorTick) / 1000.0;
                    double r = _deconRemainAtAnchor - passed;
                    return r < 0 ? 0 : r;
                }

                double q = DeconSeconds - RoundElapsed;
                return q < 0 ? 0 : q;
            }
        }

        /// <summary>倒计时是不是由"开始计时的锚点 + 时长"驱动（否则跟着回合时间走）。</summary>
        private bool _deconAnchored;

        /// <summary>净化倒计时是不是已经起过表（播报"净化开始计时"或 F9）。</summary>
        public bool DeconCountdownStarted
        {
            get { return _deconAnchored; }
        }

        // 锁定标记：识别到"开始清除所有生物"之后就固定 0 秒
        private bool _deconLocked;
        private double _deconRemainAtAnchor;
        private int _deconAnchorTick;

        /// <summary>净化是不是已经"锁死"（区域开始清除生物了）。</summary>
        public bool DeconLocked
        {
            get { return _deconLocked; }
        }

        /// <summary>
        /// 识别到"开始清除所有生物"→ 净化归零并**锁住**（后续不再接受任何修改）。
        /// 语义：轻收容已经过了很多分钟、开始锁区域清生物了。
        /// </summary>
        /// <summary>只记下"已经收到净化播报"，不改变剩余（净化跟着回合走）。</summary>
        public void MarkDeconAnnouncedOnly()
        {
            _deconDisabled = false;     // 真在屏幕上看到了净化播报 → 恢复计时
            _deconAnnounced = true;
        }

        public void LockDeconPurge()
        {
            _deconDisabled = false;
            _deconLocked = true;
            _deconAnnounced = true;
            _deconRemainAtAnchor = 0;
            _deconAnchorTick = NowTick;
        }

        /// <summary>有没有收到过净化播报（或手动起过）。</summary>
        private bool _deconAnnounced;

        public bool DeconAnnounced
        {
            get { return _deconAnnounced; }
        }

        /// <summary>是否由 F9 手动起的净化倒计时。</summary>
        public bool DeconForced
        {
            get { return _deconForced; }
        }

        private bool _deconForced;

        /// <summary>
        /// 收到净化播报：从指定秒数开始走倒计时（需求五）。
        /// seconds &lt;= 0 时用配置的基础时长（默认 705 秒 = 11 分 45 秒）。
        /// </summary>
        public void AnnounceDecon(DateTime wall, double seconds)
        {
            if (_deconLocked) return;   // 已经锁死了，谁都不许改
            if (seconds <= 0) seconds = DeconSeconds;
            _deconDisabled = false;
            _deconAnnounced = true;
            _deconForced = false;
            _deconAnchored = true;      // 从这一刻起按"锚点 + 时长"走
            _deconRemainAtAnchor = seconds;
            _deconAnchorTick = NowTick;
        }

        /// <summary>
        /// 按播报里的时间同步净化倒计时（需求五.3/5）。
        ///
        /// 容差：和当前剩余相差不到 tolerance 秒就不动 ——
        /// 识别本身有抖动，照单全收会让屏幕上的秒数来回跳。
        ///
        /// 返回 true 表示真的改了。
        /// </summary>
        public bool SyncDeconRemaining(DateTime wall, double seconds, double tolerance)
        {
            if (seconds <= 0) return false;

            if (!_deconAnnounced)
            {
                // 还没开始过：这条播报就当它是开始
                AnnounceDecon(wall, seconds);
                return true;
            }

            if (Math.Abs(DeconRemaining - seconds) < tolerance) return false;

            _deconRemainAtAnchor = seconds;
            _deconAnchorTick = NowTick;
            return true;
        }

        /// <summary>
        /// 手动从指定秒数开始净化倒计时（F9）。
        /// 这个不受回合推算影响，就是老老实实从 705 秒往下走。
        /// </summary>
        public void ForceDecon(DateTime wall, double seconds)
        {
            EnsureRunning(wall);
            if (seconds <= 0) seconds = DeconSeconds;
            _deconDisabled = false;
            _deconAnnounced = true;
            _deconForced = true;
            _deconAnchored = true;      // F9 起的表也真的参与计算了（以前只写不读）
            _deconRemainAtAnchor = seconds;
            _deconAnchorTick = NowTick;
        }

        public void ClearForcedDecon()
        {
            _deconForced = false;
            _deconAnchored = false;
            _deconRemainAtAnchor = 0;
            _deconAnnounced = false;
        }

        /// <summary>支援刷新还剩多少秒。</summary>
        public double SupportRemaining
        {
            get { return SupportSeconds - RoundElapsed; }
        }

        /// <summary>
        /// 净化"已经开始"（播报过、且已经走到 0）。
        /// 没收到播报时一律 false —— 不能因为"未知"就当成已开始。
        /// </summary>
        public bool DeconStarted
        {
            // 锁死(清除生物) 或 按回合时间走到 0 都算"已开始"
            get { return _deconLocked || DeconRemaining <= 0; }
        }

        public bool SupportReady
        {
            get { return RoundElapsed >= SupportSeconds; }
        }

        // ============================================================
        // 核弹：可以按快捷键开关，取消后重启会“从剩余时间继续”
        // ============================================================

        private bool _warheadRunning;
        private bool _warheadCalibrated;
        private DateTime _warheadAnchorWall = DateTime.MinValue;
        private double _warheadRemainAtAnchor;
        private int _warheadAnchorTick;

        /// <summary>核弹倒计时是否在走。</summary>
        public bool WarheadRunning
        {
            get { return _warheadRunning; }
        }

        public bool WarheadCalibrated
        {
            get { return _warheadCalibrated; }
        }

        /// <summary>核弹还剩多少秒。</summary>
        public double WarheadRemaining
        {
            get
            {
                if (!_warheadRunning) return 0;
                double passed = (NowTick - _warheadAnchorTick) / 1000.0;
                double r = _warheadRemainAtAnchor - passed;
                return r < 0 ? 0 : r;
            }
        }

        /// <summary>
        /// 切换核弹倒计时。
        ///   开启时：如果之前取消过、留下了剩余时间，就从那个剩余时间接着走
        ///           （游戏就是这个规则），否则用默认时长。
        ///   锁定中（亡者协议）：不允许取消，按了没反应。
        /// </summary>
        public void ToggleWarhead(DateTime wall)
        {
            if (_warheadRunning)
            {
                if (_warheadLocked) return;   // 亡者协议期间不能取消

                // 取消：把当前剩余时间记下来，下次启动从这里继续
                _warheadRemainAtAnchor = WarheadRemaining;
                _warheadRunning = false;
                _warheadCalibrated = true;
                _warheadAnchorWall = wall;
                _warheadAnchorTick = NowTick;
            }
            else
            {
                EnsureRunning(wall);   // 还没检测到回合也要能显示
                double start = _warheadRemainAtAnchor;
                if (start <= 0) start = WarheadSeconds;
                _warheadRunning = true;
                _warheadAnchorWall = wall;
                _warheadRemainAtAnchor = start;
                _warheadAnchorTick = NowTick;
            }
        }

        /// <summary>
        /// 按 OCR 播报里的数字同步核弹倒计时（需求四）。
        ///
        /// 容差：和当前剩余相差不到 tolerance 秒就不动 ——
        /// 识别本身有抖动，照单全收会让屏幕上的秒数来回跳。
        ///
        /// 只影响"当前倒计时"，**不碰手动快捷键的逻辑**：
        /// 没识别到核弹播报时这里根本不会被调用，
        /// 手动 F8 / Shift+F8 / Ctrl+F8 该怎样还怎样。
        ///
        /// 返回 true 表示真的改了。
        /// </summary>
        public bool SyncWarheadRemaining(DateTime wall, double seconds, double tolerance)
        {
            if (seconds <= 0) return false;

            if (_warheadRunning)
            {
                double cur = WarheadRemaining;
                if (Math.Abs(cur - seconds) < tolerance) return false;
            }

            StartWarhead(wall, seconds);
            _warheadCalibrated = true;
            return true;
        }

        private bool _warheadLocked;

        /// <summary>核弹是否处于“不可取消”状态（亡者协议）。</summary>
        public bool WarheadLocked
        {
            get { return _warheadLocked; }
        }

        /// <summary>强制重新起算并锁定（Ctrl+F8，模拟亡者协议）。</summary>
        public void ForceWarhead(DateTime wall, double seconds)
        {
            StartWarhead(wall, seconds);
            _warheadLocked = true;
        }

        /// <summary>按指定秒数重启并解锁（Shift+F8，模拟“取消后重启”）。</summary>
        public void RestartWarhead(DateTime wall, double seconds)
        {
            StartWarhead(wall, seconds);
            _warheadLocked = false;
        }

        /// <summary>
        /// 按快捷键操作核弹时，如果还没有检测到回合开始，
        /// 就把“现在”当作回合起点 —— 玩家既然在按核弹键，说明这一局正在进行。
        /// 否则倒计时会被 ApplyEstimated 里的 _running 判断挡掉，什么都不显示。
        /// </summary>
        private void EnsureRunning(DateTime wall)
        {
            if (_running) return;
            _running = true;
            _calibrated = false;
            _anchorWall = wall;
            _anchorTick = NowTick;
        }

        /// <summary>直接开始核弹倒计时（日志里看到“Countdown started”时用）。</summary>
        public void StartWarhead(DateTime wall, double seconds)
        {
            EnsureRunning(wall);
            if (seconds <= 0) seconds = WarheadSeconds;
            _warheadRunning = true;
            _warheadCalibrated = true;
            _warheadAnchorWall = wall;
            _warheadRemainAtAnchor = seconds;
            _warheadAnchorTick = NowTick;
        }

        public void StopWarhead()
        {
            _warheadRunning = false;
        }

        /// <summary>
        /// 停掉核弹倒计时并**解除"不可取消"锁**，同时清掉残留剩余时间 ——
        /// 下一次再起就是从默认时长开始，而不是从被停下的那一段继续。
        ///
        /// 【谁在用】终焉协议触发的核弹：操作者口径是"爆炸前读到新的支援刷新就停表"。
        /// 普通的取消（ToggleWarhead）要保留剩余时间以便"取消后重启"继续，
        /// 两者的语义不一样，所以单独开一个方法，别混用。
        /// </summary>
        public void CancelWarhead()
        {
            _warheadRunning = false;
            _warheadLocked = false;
            _warheadCalibrated = false;
            _warheadRemainAtAnchor = 0;
            _warheadAnchorWall = DateTime.MinValue;
        }

        /// <summary>引爆后清零，下一局重新来。</summary>
        public void ResetWarhead()
        {
            _warheadRunning = false;
            _warheadCalibrated = false;
            _warheadLocked = false;
            _warheadRemainAtAnchor = 0;
            _warheadAnchorWall = DateTime.MinValue;
        }

        /// <summary>取消后残留的剩余时间（用于界面提示“重启将从 xx 继续”）。</summary>
        public double WarheadHeldSeconds
        {
            get { return _warheadRemainAtAnchor; }
        }

        // ============================================================
        // 显示
        // ============================================================

        /// <summary>秒数 → mm:ss。</summary>
        public static string Clock(double seconds, bool ceil)
        {
            if (seconds < 0) seconds = 0;
            double total = ceil ? Math.Ceiling(seconds) : Math.Floor(seconds);
            int sec = (int)total;
            if (sec > 99 * 60 + 59) sec = 99 * 60 + 59;
            return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
        }

        /// <summary>一句话摘要，给设置窗口和校准窗口显示当前估算状态。</summary>
        public string Describe()
        {
            if (!_running) return "本局还没开始（等日志里出现回合开始，或手动校准）";
            string mark = _calibrated ? "" : "约 ";
            string s = "回合已进行 " + mark + Clock(RoundElapsed, false);
            // 需求五：净化没收到播报之前是"未知"，不要显示成 00:00
            if (_deconDisabled && !_deconAnchored) s += "\r\n净化：不计时（中途加入，等真实播报 / F9）";
            else if (!_deconAnnounced) s += "\r\n净化：还没开始（等识别到净化播报）";
            else s += "\r\n净化剩余 " + mark + (DeconStarted ? "已开始" : Clock(DeconRemaining, true));
            s += "\r\n支援剩余 " + mark + (SupportReady ? "已刷新" : Clock(SupportRemaining, true));
            if (_warheadRunning) s += "\r\n核弹剩余 " + Clock(WarheadRemaining, true);
            else if (_warheadRemainAtAnchor > 0) s += "\r\n核弹已取消，重启将从 " + Clock(_warheadRemainAtAnchor, true) + " 继续";
            else s += "\r\n核弹未启动";
            if (!_calibrated) s += "\r\n（未校准，建议按校准键填一次当前回合时间）";
            return s;
        }
    }
}
