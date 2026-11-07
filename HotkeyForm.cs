using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 一个永不显示的窗口，用来注册全局热键，并充当“把后台线程的请求转回 UI 线程”的宿主。
    /// 热键内容全部来自 config.ini，可自行修改。
    ///
    /// 注意：RegisterHotKey 要求修饰键完全匹配。
    /// 所以 F8 / Shift+F8 / Ctrl+F8 是三条独立的热键，必须分别注册，
    /// 不能指望注册一个 F8 就能收到带 Shift 的版本。
    /// </summary>
    internal class HotkeyForm : Form
    {
        private const int IdBase = 0xA100;

        private readonly Dictionary<int, string> _idToAction = new Dictionary<int, string>();
        private IntPtr _handle;
        private int _nextId = IdBase;

        public event EventHandler EditModePressed;
        public event EventHandler ToggleAllPressed;
        public event EventHandler OpenSettingsPressed;
        public event EventHandler PanelPressed;
        public event EventHandler CalibratePressed;

        /// <summary>核弹：开始 90 秒。</summary>
        public event EventHandler WarheadPressed;

        /// <summary>净化：从 705 秒开始。</summary>
        public event EventHandler DeconPressed;


        /// <summary>战术面板。</summary>
        public event EventHandler TacticalPressed;

        /// <summary>立即扫描所有“按需”OCR 区域（血量 / 发电机等）。</summary>
        public event EventHandler ScanNowPressed;

        /// <summary>停止当前所有计时并收起提示（之后有新事件会自动恢复）。</summary>
        public event EventHandler StopAllPressed;

        /// <summary>立刻停掉正在播放的自备音频（操作者要求）。</summary>
        public event EventHandler StopAudioPressed;
        public event EventHandler BigWavePressed;
        public event EventHandler ClockPausePressed;

        /// <summary>第 6 批：开始 / 暂停截屏采集。</summary>
        public event EventHandler CaptureStartPressed;
        public event EventHandler CapturePausePressed;

        /// <summary>注册失败的快捷键（设置窗口里可以提示用户）。</summary>
        public readonly List<string> Failed = new List<string>();

        /// <summary>压根没设置的快捷键（config.ini 里是空的，自然也就没注册）。</summary>
        public readonly List<string> Unset = new List<string>();

        /// <summary>
        /// 冲突的快捷键：两个功能被设成了同一个组合。
        /// 这种情况只有先注册的那个能用，另一个是**静默失效**的 ——
        /// 以前只写在说明里，用户根本不知道自己撞了，必须显式报出来。
        /// </summary>
        public readonly List<string> Duplicates = new List<string>();

        /// <summary>注册成功的条数。</summary>
        public int RegisteredCount
        {
            get { return _idToAction.Count + _comboToAction.Count; }
        }

        /// <summary>用系统级 RegisterHotKey 注册成功的个数（前台不是本程序也能用，且会独占按键）。</summary>
        public int RegCount
        {
            get { return _idToAction.Count; }
        }

        /// <summary>系统级注册失败、只能靠键盘钩子兜底的个数。</summary>
        public int HookOnlyCount
        {
            get { return _hookOnly.Count; }
        }

        /// <summary>
        /// 最近一次真的收到并触发的动作。
        /// 设置窗口会实时显示它 —— 用户按一下某个键就能当场看出
        /// "键到底有没有送到程序"，不用翻日志、也不用别人帮忙测。
        /// </summary>
        public string LastAction = "";
        public DateTime LastActionAt = DateTime.MinValue;

        /// <summary>给设置窗口看的一行状态。</summary>
        public string StatusLine()
        {
            string s = "系统级注册 " + _idToAction.Count
                     + "　钩子兜底 " + _hookOnly.Count
                     + "　未设置 " + Unset.Count
                     + "　冲突 " + Duplicates.Count
                     + "　被占用 " + Failed.Count;
            s += LastActionAt == DateTime.MinValue
                ? "　　最近收到：（还没有，按一下某个键试试）"
                : "　　最近收到：" + LastActionAt.ToString("HH:mm:ss") + " " + LastAction;
            return s;
        }

        /// <summary>当前用的是钩子还是 RegisterHotKey（给界面显示）。</summary>
        public bool UsingHook;

        /// <summary>钩子安装失败的说明。</summary>
        public string HookFailReason = "";

        // 钩子方案：组合键文本 -> 动作
        private readonly Dictionary<string, string> _comboToAction =
            new Dictionary<string, string>();

        /// <summary>已经用系统级 RegisterHotKey 注册成功的组合（钩子要避开它们，免得触发两次）。</summary>
        private readonly HashSet<string> _regCombos = new HashSet<string>();

        /// <summary>系统级注册成功的组合 → 动作（钩子兜底补触发时要用）。</summary>
        private readonly Dictionary<string, string> _regAction =
            new Dictionary<string, string>();

        /// <summary>因为系统级注册失败、只能靠钩子兜底的组合。</summary>
        private readonly HashSet<string> _hookOnly = new HashSet<string>();

        /// <summary>这次注册里用过的组合（用来查重复绑定）。</summary>
        private readonly Dictionary<string, string> _seenCombos =
            new Dictionary<string, string>();

        private KeyboardHook _hook;

        /// <summary>最近一次注册用的配置（看门狗重挂时要用）。</summary>
        private Config _cfg;

        public HotkeyForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Minimized;
            Location = new Point(-4000, -4000);
            Size = new Size(1, 1);

            _handle = Handle; // 强制创建句柄

            // 待定补触发用的计时器（平时不跑，钩子遇到"已注册"的键时才启动）
            _pendingTimer = new Timer();
            _pendingTimer.Interval = 60;
            _pendingTimer.Tick += OnPendingTick;

            // 看门狗：低级键盘钩子被系统静默摘掉之后能自己长回来
            _watchdog = new Timer();
            _watchdog.Interval = 90000;
            _watchdog.Tick += OnWatchdog;
            _watchdog.Start();
        }

        private readonly Timer _watchdog;

        /// <summary>
        /// 定期确认键盘钩子还活着。
        ///
        /// 低级钩子的回调一旦超时，Windows 会不留痕迹地把它摘掉，
        /// 这时候所有快捷键都会失效。每 90 秒重挂一次，最坏情况也能在
        /// 一两分钟内自动恢复，不用重启程序。
        /// </summary>
        private void OnWatchdog(object sender, EventArgs e)
        {
            if (_cfg == null) return;

            // ① 钩子可能在回调超时后被系统悄悄摘掉，重新挂一次
            bool hookOk = true;
            if (UsingHook)
            {
                hookOk = _hook != null && _hook.Rearm();
                if (!hookOk)
                    Logger.On("热键", "键盘钩子重挂失败（" + (_hook == null ? "钩子为空" : _hook.FailReason) + "）");
            }

            // ② 之前"系统级注册失败、只能靠钩子兜底"的键，再试一次 ——
            //    占用它的那个程序可能已经退出了。
            //    钩子本身挂了的话，整体重来一遍（会重新装钩子）。
            if (_hookOnly.Count > 0 || !hookOk)
            {
                Logger.On("热键", "看门狗重新注册全部热键（钩子兜底 " + _hookOnly.Count + " 个）");
                RegisterAll(_cfg);
            }
        }

        /// <summary>按配置注册全部热键。</summary>
        public void RegisterAll(Config cfg)
        {
            _cfg = cfg;
            UnregisterAll();

            // 先试键盘钩子；装不上才退回 RegisterHotKey
            TryInstallHook();

            Register(cfg.HotkeyAdjust, "edit");
            Register(cfg.HotkeyToggleAll, "toggleAll");
            Register(cfg.HotkeySettings, "settings");
            Register(cfg.HotkeyPanel, "panel");

            Register(cfg.HotkeyCalibrate, "calibrate");
            Register(cfg.HotkeyWarhead, "warhead");
            Register(cfg.HotkeyDecon, "decon");
            Register(cfg.HotkeyTactical, "tactical");
            Register(cfg.HotkeyScanNow, "scannow");
            Register(cfg.HotkeyStopAll, "stopall");
            Register(cfg.HotkeyStopAudio, "stopaudio");
            Register(cfg.HotkeyBigWave, "bigwave");
            Register(cfg.HotkeyClockPause, "clockpause");
            Register(cfg.HotkeyCaptureStart, "capturestart");
            Register(cfg.HotkeyCapturePause, "capturepause");
        }

        private void Register(string text, string action)
        {
            HotkeySpec spec = HotkeySpec.Parse(text);
            if (spec == null || !spec.IsValid)
            {
                // 空值 = 用户把这个功能留空了（Backspace 会清空），
                // 单独记一笔，启动时写进日志 —— 免得用户以为"快捷键全失效"。
                if (string.IsNullOrEmpty(text)) Unset.Add(ActionName(action) + "(" + action + ")");
                return;
            }

            string combo = spec.ToString();

            // 同一个组合被两个功能占用：只有前面那个生效，记下来告诉用户
            string owner;
            if (_seenCombos.TryGetValue(combo, out owner))
            {
                Duplicates.Add(combo + "（" + owner + " / " + ActionName(action) + "）");
                Logger.On("热键", "快捷键冲突：" + combo + " 同时给了「"
                    + owner + "」和「" + ActionName(action) + "」，只有前者生效");
                return;
            }
            _seenCombos[combo] = ActionName(action);

            // ============================================================
            // 第 5 批：装了键盘钩子之后，**全部热键都走钩子**，不再用 RegisterHotKey。
            //
            // 原因：RegisterHotKey 会在系统层把按键"抢走"，游戏永远收不到；
            // 而需求要的是"后台可用=开 → 后台生效但不拦截"，这只有钩子能做到
            // （钩子回调里返回 1 才吞键、返回 0 就放行）。
            //
            // 只有钩子装不上（UIPI / 被安全软件挡）时才退回 RegisterHotKey ——
            // 那条路无法"不拦截"，属于已知限制，界面会写出来。
            // ============================================================
            if (UsingHook)
            {
                _comboToAction[combo] = action;
                return;
            }

            // ---- 退路：系统级 RegisterHotKey（会独占按键）----
            int id = _nextId++;
            if (NativeMethods.RegisterHotKey(_handle, id, spec.RegisterModifiers, spec.Vk))
            {
                _idToAction[id] = action;
                _regCombos.Add(combo);
                _regAction[combo] = action;
                return;
            }

            Failed.Add(combo + "（" + ActionName(action) + "）");
        }

        /// <summary>
        /// 建立钩子。成功就用钩子，失败再退回 RegisterHotKey。
        /// </summary>
        private bool TryInstallHook()
        {
            // 排查用：设了环境变量 SCPSLALERT_NO_HOOK=1 就强制走 RegisterHotKey 那条路，
            // 怀疑钩子有问题时可以拿来对照验证。
            try
            {
                if (Environment.GetEnvironmentVariable("SCPSLALERT_NO_HOOK") == "1")
                {
                    HookFailReason = "环境变量 SCPSLALERT_NO_HOOK=1，强制使用 RegisterHotKey";
                    UsingHook = false;
                    return false;
                }
            }
            catch { }

            try
            {
                if (_hook != null) { _hook.Dispose(); _hook = null; }
                _hook = new KeyboardHook();
                _hook.Combo += OnCombo;
                _hook.Filter = HookFilter;
                if (_hook.Install())
                {
                    UsingHook = true;
                    return true;
                }
                HookFailReason = _hook.FailReason;
                _hook.Combo -= OnCombo;
                _hook.Dispose();
                _hook = null;
            }
            catch (Exception ex)
            {
                HookFailReason = ex.Message;
            }
            UsingHook = false;
            return false;
        }

        /// <summary>第 5 批：游戏窗口是不是前台。</summary>
        private static bool GameForeground()
        {
            try
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                return fg != IntPtr.Zero && ScreenGeometry.IsGameWindow(fg);
            }
            catch { return false; }
        }

        /// <summary>第 5 批：本程序的窗口是不是前台（在设置里按键也要能用）。</summary>
        private bool OurForeground()
        {
            try
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                return fg != IntPtr.Zero && fg == Handle;
            }
            catch { return false; }
        }

        private bool BackgroundAllowed(string action)
        {
            return _cfg != null && _cfg.IsHotkeyBackground(action);
        }

        /// <summary>这个功能现在该不该触发。</summary>
        private bool ShouldFire(string action)
        {
            return GameForeground() || OurForeground() || BackgroundAllowed(action);
        }

        /// <summary>
        /// 第 5 批：钩子的拦截判断。
        /// 返回 -1 放行、0 触发但不吞键、1 触发并吞键（只有游戏前台才吞）。
        /// </summary>
        private int HookFilter(string combo)
        {
            string action;
            if (!_comboToAction.TryGetValue(combo, out action)) return -1;
            if (KeyboardHook.Suspended) return -1;

            bool game = GameForeground();
            if (!game && !OurForeground() && !BackgroundAllowed(action)) return -1;
            return game ? 1 : 0;
        }

        /// <summary>钩子收到一个组合键：查表并触发。</summary>
        private void OnCombo(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            // 把按下的一组合成和 config 里一样的文本，再查表
            string key = HotkeySpec.Format(vk, ctrl, alt, shift, win);
            if (key == null) return;

            // ---- 已经用系统级 RegisterHotKey 注册过的组合 ----
            //
            // 正常情况下系统会紧接着投递 WM_HOTKEY，我们什么都不用做。
            // 但实测发现：某些状态下（比如程序窗口被最小化）系统并不会投递
            // WM_HOTKEY，于是"两条路一起哑了"——用户看到的就是"快捷键全失效"。
            //
            // 所以这里先记成"待定"，等一小会儿：
            //   · WM_HOTKEY 来了  → 取消待定（由它触发，按键仍然被独占）；
            //   · 一直没来        → 我们补触发一次，保证快捷键永远有反应。
            if (_regCombos.Contains(key))
            {
                string regAction;
                if (!_regAction.TryGetValue(key, out regAction)) return;

                _pendingCombo = key;
                _pendingAction = regAction;
                _pendingDeadline = DateTime.Now.AddMilliseconds(180);
                if (_pendingTimer != null && !_pendingTimer.Enabled) _pendingTimer.Start();
                return;
            }

            // ---- 只能靠钩子兜底的组合：直接触发 ----
            string action;
            if (!_comboToAction.TryGetValue(key, out action)) return;

            // 第 5 批：游戏不在前台、且这个功能没开"后台可用" → 不触发。
            // （钩子那边已经放行按键，所以这里只是不响应，不会影响游戏。）
            if (!ShouldFire(action)) return;

            // ============================================================
            // 【关键】这个回调必须立刻返回。
            //
            // Windows 对低级键盘钩子（WH_KEYBOARD_LL）的回调有硬性超时
            // （LowLevelHooksTimeout，默认 300ms）。一旦超时，系统会**静默
            // 把钩子摘掉** —— 用户看到的就是"所有快捷键突然全部失效"。
            //
            // 所以这里绝不做任何实事（不写盘、不建窗口、不读配置），
            // 只把动作丢进消息队列，真正的活儿交给消息循环去做。
            // ============================================================
            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate { Fire(action); });
                    return;
                }
            }
            catch { }
            Fire(action);   // 消息队列还没建好（极端情况）才直接执行
        }

        private string _pendingCombo;
        private string _pendingAction;
        private DateTime _pendingDeadline = DateTime.MinValue;
        private Timer _pendingTimer;

        /// <summary>最近一次"系统级热键真的送到了"的时间，用来避免钩子补触发造成重复。</summary>
        private DateTime _lastHotkeyAt = DateTime.MinValue;

        /// <summary>待定超时检查：WM_HOTKEY 一直没来就自己补触发。</summary>
        private void OnPendingTick(object sender, EventArgs e)
        {
            if (_pendingAction == null)
            {
                if (_pendingTimer != null) _pendingTimer.Stop();
                return;
            }
            if (DateTime.Now < _pendingDeadline) return;

            // 刚才已经有系统级热键触发过了（说明只是钩子回调来得晚），
            // 这一次就不补了，否则一次按键会触发两遍。
            if ((DateTime.Now - _lastHotkeyAt).TotalMilliseconds < 400)
            {
                _pendingCombo = null;
                _pendingAction = null;
                if (_pendingTimer != null) _pendingTimer.Stop();
                return;
            }

            string action = _pendingAction;
            _pendingCombo = null;
            _pendingAction = null;
            if (_pendingTimer != null) _pendingTimer.Stop();

            Logger.On("热键", "系统级热键没有投递 WM_HOTKEY，已由键盘钩子补触发");
            Fire(action);
        }

        private static string ActionName(string action)
        {
            if (action == "edit") return "调整位置";
            if (action == "toggleAll") return "显示/隐藏全部";
            if (action == "settings") return "打开设置";
            if (action == "panel") return "通知开关面板";
            if (action == "calibrate") return "回合时间校准";
            if (action == "warhead") return "核弹 90 秒";
            if (action == "decon") return "净化倒计时";
            if (action == "tactical") return "战术面板";
            if (action == "scannow") return "立即扫描 OCR 区域";
            if (action == "stopall") return "停止 / 恢复全部计时";
            if (action == "bigwave") return "刷新校准";
            if (action == "clockpause") return "回合计时暂停";
            if (action == "capturestart") return "开始截屏采集";
            if (action == "capturepause") return "暂停 / 继续截屏采集";
            return action;
        }

        private void UnregisterAll()
        {
            foreach (KeyValuePair<int, string> kv in _idToAction)
            {
                try { NativeMethods.UnregisterHotKey(_handle, kv.Key); }
                catch { }
            }
            _idToAction.Clear();
            _comboToAction.Clear();
            _regCombos.Clear();
            _regAction.Clear();
            _hookOnly.Clear();
            _seenCombos.Clear();
            _nextId = IdBase;
            Failed.Clear();
            Unset.Clear();
            Duplicates.Clear();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false); // 永远不显示
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                string action;
                if (_idToAction.TryGetValue(id, out action))
                {
                    // 系统级热键到了 → 取消钩子那边的"待定补触发"，避免触发两次
                    _pendingCombo = null;
                    _pendingAction = null;
                    if (_pendingTimer != null) _pendingTimer.Stop();

                    _lastHotkeyAt = DateTime.Now;
                    Fire(action);
                }
            }
            base.WndProc(ref m);
        }

        private void Fire(string action)
        {
            // 设置窗口正在录快捷键时全部按住 —— 包括 RegisterHotKey 那条路。
            // 否则用户想给 A 功能设某个键，按下的一瞬间就把 B 功能调出来了。
            // （这个挂起状态带 1 分钟自动过期，见 KeyboardHook.Suspended）
            if (KeyboardHook.Suspended) return;

            // 第 5 批：后台可用 = 关时，只有游戏/本程序在前台才响应。
            if (!ShouldFire(action)) return;

            // 记下来给设置窗口实时显示：这样"按了没反应"时分得清是
            // "键根本没送到程序" 还是 "送到了但功能没生效"。
            LastAction = ActionName(action);
            LastActionAt = DateTime.Now;

            // 排障用：把每次热键都记进日志（日志关掉时它自己就什么都不做）
            Logger.On("热键", "按下 " + ActionName(action) + "（" + action + "）");

            switch (action)
            {
                case "edit": if (EditModePressed != null) EditModePressed(this, EventArgs.Empty); break;
                case "toggleAll": if (ToggleAllPressed != null) ToggleAllPressed(this, EventArgs.Empty); break;
                case "settings": if (OpenSettingsPressed != null) OpenSettingsPressed(this, EventArgs.Empty); break;
                case "panel": if (PanelPressed != null) PanelPressed(this, EventArgs.Empty); break;
                case "calibrate": if (CalibratePressed != null) CalibratePressed(this, EventArgs.Empty); break;
                case "warhead": if (WarheadPressed != null) WarheadPressed(this, EventArgs.Empty); break;
                case "decon": if (DeconPressed != null) DeconPressed(this, EventArgs.Empty); break;
                case "tactical": if (TacticalPressed != null) TacticalPressed(this, EventArgs.Empty); break;
                case "scannow": if (ScanNowPressed != null) ScanNowPressed(this, EventArgs.Empty); break;
                case "stopall": if (StopAllPressed != null) StopAllPressed(this, EventArgs.Empty); break;
                case "stopaudio": if (StopAudioPressed != null) StopAudioPressed(this, EventArgs.Empty); break;
                case "bigwave": if (BigWavePressed != null) BigWavePressed(this, EventArgs.Empty); break;
                case "clockpause": if (ClockPausePressed != null) ClockPausePressed(this, EventArgs.Empty); break;
                case "capturestart": if (CaptureStartPressed != null) CaptureStartPressed(this, EventArgs.Empty); break;
                case "capturepause": if (CapturePausePressed != null) CapturePausePressed(this, EventArgs.Empty); break;
            }
        }

        /// <summary>从任意线程安全地在 UI 线程上执行一段代码。</summary>
        public void RunOnUi(Action action)
        {
            try
            {
                if (!IsHandleCreated) { action(); return; }
                BeginInvoke((MethodInvoker)delegate { action(); });
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _handle = Handle;      // 句柄万一被重建，这里跟着更新，注册才不会打到废句柄上
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterAll();
            if (_watchdog != null)
            {
                try { _watchdog.Stop(); _watchdog.Tick -= OnWatchdog; _watchdog.Dispose(); } catch { }
            }
            if (_hook != null)
            {
                try { _hook.Combo -= OnCombo; _hook.Dispose(); } catch { }
                _hook = null;
            }
            base.OnHandleDestroyed(e);
        }
    }
}
