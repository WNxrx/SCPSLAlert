using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal static class Program
    {
        private const string MutexName = "SCPSLAlert_SingleInstance_Mutex";
        private const string ShowEventName = "SCPSLAlert_ShowSettings_Event";

        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            // 离屏渲染设计稿：SCPSLAlert.exe --splash-render [输出目录]
            if (HasArg(args, "--splash-render"))
            {
                try { NativeMethods.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SplashDesignCapture.Run(GetArgValue(args, "--splash-render"));
                return;
            }

            // 设计预览：只打开启动画面，不初始化其它模块。
            //   SCPSLAlert.exe --splash-preview
            //   SCPSLAlert.exe --splash-preview-light
            //   SCPSLAlert.exe --splash-preview-dark
            bool preview = HasArg(args, "--splash-preview");
            bool previewLight = HasArg(args, "--splash-preview-light");
            bool previewDark = HasArg(args, "--splash-preview-dark");
            if (preview || previewLight || previewDark)
            {
                try { NativeMethods.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool? forceLight = null;
                if (previewLight) forceLight = true;
                else if (previewDark) forceLight = false;
                StartupSplash.Preview(forceLight);
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // 已经有实例在跑：通知它把设置窗口显示出来，然后自己安静退出。
                // 这里绝不做任何阻塞等待，避免"卡死主线程"。
                SignalExistingInstance();
                return;
            }

            // 立刻建好事件对象，后面的实例才能通知到我们
            EventWaitHandle showEvent = null;
            try { showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName); }
            catch { }

            try { NativeMethods.SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                try { Logger.Err("界面", "未处理的界面异常（已拦截，程序继续运行）", e.Exception); }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                try { Logger.Err("程序", "未处理的异常", e.ExceptionObject as Exception); }
                catch { }
            };
            AppCrashGuard.Install();
            Application.ApplicationExit += delegate { AppCrashGuard.EndRun(); SafeMode.CleanExit(); };
            try
            {
                AppDomain.CurrentDomain.ProcessExit += delegate { AppCrashGuard.EndRun(); };
            }
            catch { }

            // 滚轮路由：让滚轮跟着"鼠标底下的能滚控件"走。
            // 【为什么必须全局装】WinForms 把 WM_MOUSEWHEEL 发给**有焦点的窗口**，
            // 鼠标停在 GroupBox / Label 这类子容器上时，设置页有滚动条也滚不动
            // （操作者报的"子容器无法滑动"）。装一个消息过滤器全解决，不用逐个控件改。
            try { Application.AddMessageFilter(new WheelRouter()); } catch { }

            // 启动画面延后到读完 MainUiStyle 再决定：旧界面不创建启动动画窗口。
            StartupSplash.Status("正在启动核心…", 0.06, 0);

            AlertApplication app = null;
            try
            {
                app = new AlertApplication(showEvent);
            }
            catch (Exception ex)
            {
                StartupSplash.CloseNow();
                MessageBox.Show("启动失败：\r\n" + ex.ToString(), "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Application.Run(app);
            }
            finally
            {
                StartupSplash.CloseNow();
                try { app.Dispose(); } catch { }
                try { _mutex.ReleaseMutex(); } catch { }
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetArgValue(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                string v = args[i + 1];
                if (!v.StartsWith("-", StringComparison.Ordinal)) return v;
            }
            return null;
        }

        /// <summary>短暂重试后放弃；整个过程中的等待都很短，不会让已运行的实例卡住。</summary>
        private static void SignalExistingInstance()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (EventWaitHandle h = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        h.Set();
                        return;
                    }
                }
                catch
                {
                    Thread.Sleep(120);
                }
            }
        }
    }

    /// <summary>程序主体：设置窗口 + 托盘图标 + 日志监控。</summary>
    internal class AlertApplication : ApplicationContext
    {
        private readonly string _configPath;
        private readonly Config _config;

        /// <summary>本程序的消息窗口。托盘图标的重建、消息过滤放行都挂在它身上。</summary>
        private MessageWindow _msgWin;

        /// <summary>
        /// 一个隐藏的消息窗口，专门用来收 TaskbarCreated。
        ///
        /// 【为什么要有它】资源管理器重启后会向所有顶层窗口广播 TaskbarCreated，
        /// 收到这条消息才知道该把托盘图标重新加回去。ApplicationContext 本身
        /// 收到消息时并没有把消息转发给 WndProc，所以这里用一个真正的小窗口来收。
        /// </summary>
        private class MessageWindow : NativeWindow, IDisposable
        {
            public event Action TaskbarCreated;

            public MessageWindow()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_TASKBARCREATED)
                {
                    try { if (TaskbarCreated != null) TaskbarCreated(); }
                    catch { }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                try { DestroyHandle(); } catch { }
            }
        }

        /// <summary>本 ApplicationContext 的窗口句柄（消息过滤、TaskbarCreated 都要用）。</summary>
        private IntPtr _handle;
        private readonly AlertManager _manager;
        private readonly RespawnWindowWatcher _respawnWatcher;
        private readonly HotkeyForm _hotkeys;
        private HeartPromptOverlay _heartPrompt;

        // 第 6 批：截屏采集
        private ScreenCaptureCollector _capture;
        private CaptureHudForm _captureHud;
        private SettingsForm _settings;
        private WpfVigilDataWindow _vigilStatus;
        private System.Windows.Forms.Form _modernHost;
        private ModernStartView _modernView;
        private readonly GameBrightnessController _gameBrightness;
        private readonly CrosshairController _crosshair;
        private readonly ToolStripMenuItem _editItem;
        private AdjustBar _adjustBar;
        private bool _adjustReturnToVigil;

        // ============================================================
        // 托盘图标：三个字段故意做成 static，把它死死绑在进程级的存活域上。
        //
        // 教训（幽灵图标）：Icon.FromHandle() 只是给 HICON 套了个壳，
        // 图标数据仍然来自那张 Bitmap 产生的句柄。只要
        //   · Bitmap 被释放 / 被 GC 回收，或者
        //   · NotifyIcon 本身变成不可达对象被回收
        // 系统托盘里就会留下一个点不动、也消失不掉的“幽灵图标”。
        //
        // 所以这里：
        //   · 三个字段都是 static，进程结束前一直可达；
        //   · 除了退出程序，绝不调用 Dispose；
        //   · 由 Application.Run 的主消息循环负责保活，不要用局部变量接。
        // ============================================================
        /// <summary>
        /// 把托盘图标挂上去。
        ///
        /// 【为什么要单独一个方法 + 反复重试】托盘图标归 explorer.exe 管，
        /// 而这个程序是以管理员身份运行的，UIPI 有可能把注册消息挡掉，
        /// 或者 explorer 还没准备好 —— 表现就是"启动后托盘里什么都没有"。
        /// 所以这里：失败/没成功就继续重试，并且允许被"资源管理器重启"重置回未创建状态。
        /// </summary>
        private static void EnsureTrayIcon()
        {
            if (_trayIcon == null || _notifyIcon == null) return;
            if (_trayIconAdded) return;

            try
            {
                _notifyIcon.Visible = true;   // 显式打开，否则托盘区不会有图标
                _trayIconAdded = true;
                _trayAttempts = 0;
                Logger.On("托盘", "托盘图标已挂上（第 " + (_trayAttempts + 1) + " 次尝试成功）");
            }
            catch (Exception ex)
            {
                Logger.Err("托盘", "挂托盘图标失败", ex);
            }
        }

        /// <summary>资源管理器重启之后，把托盘图标重新加回去。</summary>
        private void ReAddTrayIcon()
        {
            Logger.On("托盘", "收到 TaskbarCreated（资源管理器重启过），正在重建托盘图标");
            _trayIconAdded = false;
            _trayAttempts = 0;

            // 重建 NotifyIcon 最稳：老对象的内部状态可能已经和新的 explorer 对不上了
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            catch { }

            try
            {
                _notifyIcon = new NotifyIcon();
                _notifyIcon.Icon = _trayIcon;
                _notifyIcon.Text = "SCPSLAlert";
                _notifyIcon.ContextMenuStrip = _trayMenu;
                _notifyIcon.DoubleClick += delegate { ShowSettings(); };
                EnsureTrayIcon();
            }
            catch (Exception ex)
            {
                Logger.Err("托盘", "重建托盘图标失败", ex);
            }
        }

        private static Bitmap _trayBitmap;
        private static Icon _trayIcon;
        private static NotifyIcon _notifyIcon;
        private static ContextMenuStrip _trayMenu;

        // ---- 音频菜单（主界面/托盘）----
        private ToolStripMenuItem _audioMenu;      // 「音频」子菜单
        private ToolStripMenuItem _audioNow;       // 显示当前默认音频
        private ToolStripMenuItem _audioOn;        // 提示音总开关（带勾）
        private ToolStripMenuItem _audioList;      // 「选默认音频」的子项列表
        private static bool _trayIconAdded;
        private static int _trayAttempts;
        private System.Windows.Forms.Timer _trayRetry;

        /// <summary>窗口图标共用同一份，避免多造 HICON。</summary>
        internal static Icon SharedIcon
        {
            get { return _trayIcon; }
        }

        private NotifyPanel _panel;
        private readonly KeyHoldWatcher _hold;
        private readonly OcrAlertWatcher _ocr;
        private readonly EventWaitHandle _showEvent;
        private RegisteredWaitHandle _showReg;
        private string StatusTextHolder;
        private bool _disposed;
        private readonly Stopwatch _bootWatch = Stopwatch.StartNew();
        private CrashReport _previousCrash;
        private Control _crashAnchor;

        public AlertApplication(EventWaitHandle showEvent)
        {
            StartupSplash.Status("正在载入配置与界面主题…", 0.14, 0);
            _configPath = Path.Combine(Application.StartupPath, "config.ini");
            // 先看看是不是第一次运行（config.ini 还不存在），决定要不要问“看不看教程”
            _firstRun = !File.Exists(_configPath);
            _config = Config.Load(_configPath);
            try { SafeMode.Ask(_config, _configPath); } catch { }
            try
            {
                UiFonts.Apply(_config);
                string pf = FontLibrary.FileFor(_config.PromptFontFamily);
                if (string.IsNullOrEmpty(pf)) pf = _config.PromptFontFile;
                OverlayForm.SetPromptFontFile(pf);
            }
            catch { }
            try
            {
                // 第 10 批：界面已隐藏这三个键，首次升级时释放按键并落盘；之后手改 config 不受影响
                if (!_config.HotkeyReleaseV1)
                {
                    _config.HotkeyWarheadShift = "";
                    _config.HotkeyWarheadCtrl = "";
                    _config.HotkeyScpDead = "";
                    _config.HotkeyReleaseV1 = true;
                    _config.Save(_configPath);
                    Logger.On("配置", "已释放旧快捷键：核弹取消后重启 / 核弹切换 / SCP 阵亡");
                }
            }
            catch { }
            // 首次启动强制旧界面；配置键只记“上次退出时在哪”，不承担首次标记。
            if (_firstRun)
            {
                _config.ForceLegacyThisRun = true;
                _config.MainUiStyle = 0;
            }
            if (_config.MainUiStyle < 0) _config.MainUiStyle = 0;
            ThemeTokens.Configure(_config);
            // 只有新界面才创建启动动画；旧界面直接进，不闪启动画面。
            if (_config.UiStyle == 0) StartupSplash.Begin();
            ConfigBackup.EnsureBaseline(_configPath);
            CrashRules.Ensure();
            _gameBrightness = new GameBrightnessController(_config);
            _crosshair = new CrosshairController(_config);
            _config.Save(_configPath);
            UiAnim.Quality = _config.PanelAnimQuality;   // 动画档位先按配置生效
            StartupSplash.Status("正在准备运行环境…", 0.24, 0);

            // 隐藏消息窗口：专门收"资源管理器重启了"的广播
            _msgWin = new MessageWindow();
            _msgWin.TaskbarCreated += delegate { ReAddTrayIcon(); };

            // 消息过滤要用窗口句柄：这里用隐藏消息窗口的句柄
            _handle = _msgWin.Handle;

            // ---------- 程序自己的运行日志（排障用）----------
            // 放在程序目录下；超过 5MB 会自动删掉最旧的一半
            Logger.Init(Path.Combine(Application.StartupPath, "SCPSLAlert.log"), _config.EnableLog);
            Logger.On("启动", "SCPSLAlert 启动；配置 " + _configPath);
            Logger.On("启动", "屏幕识别 " + (_config.OcrAllowed ? "开启" : "关闭")
                + "；OCR 区域 " + _config.OcrRegions.Count + " 组"
                + "；运行日志 " + (_config.EnableLog ? "开启" : "关闭"));
            Logger.On("启动", "本程序" + (IsElevated() ? "以管理员身份运行" : "没有以管理员身份运行")
                + "（游戏若是管理员运行，热键和屏幕截图都会被 Windows 屏蔽）");
            // 这一行同时进时间日志 —— 让那个文件一启动就有，用户能立刻看到它在工作
            Logger.On("回合", "时间日志已开启（只记回合 / 刷新 / 校准 / 倒计时这些行）："
                + Logger.TimePath);

            _manager = new AlertManager(_config, _configPath);
            _manager.Start();

            // 第 6 批：截屏采集 + 独立进度提示通道
            try
            {
                _capture = new ScreenCaptureCollector(_config);
                _captureHud = new CaptureHudForm(_config);
                _capture.Notice = delegate(string text, bool light)
                {
                    try
                    {
                        if (_captureHud == null) return;
                        if (_captureHud.InvokeRequired)
                            _captureHud.BeginInvoke((MethodInvoker)delegate { ShowCaptureNotice(text, light); });
                        else ShowCaptureNotice(text, light);
                    }
                    catch { }
                };
                _capture.Progress = delegate(int current, int total)
                {
                    try
                    {
                        if (_captureHud == null) return;
                        if (_captureHud.InvokeRequired)
                            _captureHud.BeginInvoke((MethodInvoker)delegate { ShowCaptureProgress(current, total); });
                        else ShowCaptureProgress(current, total);
                    }
                    catch { }
                };
                _capture.Completed = delegate(string dir)
                {
                    Logger.On("采集", "采集完成，目录：" + dir);
                };
                _capture.SaveRequested = delegate { try { _config.Save(_configPath); } catch { } };
                _manager.SelfDeath += delegate { if (_capture != null) _capture.OnSelfDeath(); };
                _manager.SelfRespawn += delegate { if (_capture != null) _capture.OnSelfRespawn(); };
            }
            catch (Exception ex)
            {
                Logger.Err("采集", "初始化截屏采集失败", ex);
            }

            // 心率屏幕提示：独立于游戏事件，只读 HeartRateHub。
            // 窗口先建好并隐藏，之后由设置和 BLE 数据决定显不显示。
            try
            {
                _heartPrompt = new HeartPromptOverlay(_config);
                _heartPrompt.PositionChanged += delegate
                {
                    try { _config.Save(_configPath); } catch { }
                };
            }
            catch (Exception ex)
            {
                Logger.Err("心率", "创建心率屏幕提示失败（不影响游戏提示）", ex);
            }
            StartupSplash.Status("正在启动后台监控…", 0.38, 1);

            _previousCrash = AppCrashGuard.TakePreviousRun();
            AppCrashGuard.BeginRun();
            _crashAnchor = new Control();
            _crashAnchor.CreateControl();
            CrashNotifier.Attach(_crashAnchor);
            GameCrashWatcher.ReportReady += delegate(CrashReport report) { CrashNotifier.Show(report); };
            GameCrashWatcher.Apply(_config);
            if (_config.MonitorGameCrash) GameDumpSettings.Enable();

            // 用系统 Shell Hook 观察游戏窗口重绘。前台需 OCR 候选共同确认，
            // 严格模式以前台静音/票数 OCR 为主、重绘为辅助；后台使用经过过滤的重绘信号。
            _respawnWatcher = new RespawnWindowWatcher();
            _respawnWatcher.RedrawObserved += delegate(bool foreground, string why)
            {
                _manager.OnGameWindowRedraw(foreground, why);
            };

            _hotkeys = new HotkeyForm();
            // 第 4 批：阵营识别在后台线程出结果，切回 UI 线程再动提示窗
            _manager.UiInvoke = delegate(Action work) { _hotkeys.RunOnUi(work); };
            _hotkeys.EditModePressed += delegate { ToggleEditMode(); };
            _hotkeys.ToggleAllPressed += delegate { _manager.ToggleVisibleAll(); };
            _hotkeys.OpenSettingsPressed += delegate { ShowSettings(); };
            _hotkeys.PanelPressed += delegate { ToggleNotifyPanel(); };

            // ---- 悬浮面板 ----
            // F7 时间校准：已经并进战术面板的「校准」页，不再有单独窗口
            _hotkeys.CalibratePressed += delegate { ToggleTimeCalibration(); };
            _hotkeys.TacticalPressed += delegate { ToggleTacticalPanel(); };

            // ---- 核弹 / 净化 / 阵亡 ----
            // 【时长全部从配置读】以前这几个秒数是写死的（90 / 80 / 705），
            // 现在都是 config.ini 里的值，设置窗口「时间」页可以手动改。
            _hotkeys.WarheadPressed += delegate { _manager.ToggleWarheadByHotkey(); };
            _hotkeys.DeconPressed += delegate
            { _manager.StartDeconByHotkey(_config.DeconBaseSeconds); };
            _hotkeys.BigWavePressed += delegate { MarkBigWave(); };
            _hotkeys.ClockPausePressed += delegate { ToggleRoundClock(); };
            _hotkeys.CaptureStartPressed += delegate { StartCaptureByHotkey(); };
            _hotkeys.CapturePausePressed += delegate { ToggleCapturePauseByHotkey(); };

            // 按住不放的热键：临时无视“已关闭”和“已隐藏”，把通知全显示出来
            _hold = new KeyHoldWatcher(HotkeySpec.Parse(_config.HotkeyHoldShowAll));
            _hold.Pressed += delegate { _manager.ForceShowAll = true; };
            _hold.Released += delegate { _manager.ForceShowAll = false; };
            _hold.Start();

            // ---------- 屏幕 OCR 播报识别 ----------
            // 复用现有的 AlertManager 和悬浮窗：识别到播报之后走的是和
            // F6~F10 手动快捷键完全一样的通道，所以显示效果天然一致。
            // 引擎在后台线程初始化，这里绝不阻塞启动。
            _ocr = new OcrAlertWatcher(_config, _manager,
                delegate(Action work) { _hotkeys.RunOnUi(work); });

            // 屏幕识别命中时，在屏幕左下角给一小块提示：
            // 让用户看得见"这一下认到了什么字、程序据此做了什么"。
            _ocr.OnCaptured = delegate(string text, string action)
            {
                try
                {
                    if (!_config.OcrToast) return;
                    if (_ocrToast == null) _ocrToast = new OcrToastForm();
                    _ocrToast.ShowToast(text, action);
                }
                catch (Exception ex) { Logger.Err("提示", "显示 OCR 捕获提示失败", ex); }
            };

            // 认出角色 → 立刻结束准备阶段、进入正式计时（需求六/七）
            _ocr.OnRoleRecognized = delegate(string role) { _manager.OnRoleRecognized(role); };
            _ocr.OnObserverSeen = delegate { _manager.NoteObserverState(); };
            _ocr.OnDeathScreenEmpty = delegate { _manager.OnDeathScreenEmpty(); };
            _ocr.OnTicketScan = delegate(bool saw) { _manager.OnTicketScan(saw); };

            // 准备阶段 ↔ 快速扫描频率（需求六.2/3）
            _manager.PrepPhaseChanged += delegate(bool on) { _ocr.SetPrepScan(on); };

            // 【进服后才开始扫描】进服 / 离服时重新算一次 OCR 的扫描时机：
            // 进服 → 定时器开起来；离服 → 立刻停（不再对着主菜单截图）。
            _manager.ServerStateChanged += delegate(bool on) { if (_ocr != null) _ocr.ApplyConfig(); };

            // 死亡界面票数（需求八）：死亡时开扫，复活/回合结束停
            _manager.DeathScanChanged += delegate(bool on) { _ocr.SetDeathScan(on); };
            // 右下角静音开关 → 判断复活；左侧列表 → 标记这一局的 SCP 在场/存活
            _ocr.OnSilenceScan = delegate(bool saw) { _manager.OnSilenceScanned(saw); };
            _ocr.OnScpListScan = delegate(string codes)
            {
                _manager.OnScpListScanned(codes);
                RefreshTacticalFromModel();
            };

            // SCP 扫描新逻辑：进服 + 第一次死亡 → 开始持续扫左侧 SCP 列表，
            // 扫到新内容弹右侧悬浮窗；离开服务器自动停。
            _manager.ScpScanWatchChanged += delegate(bool on)
            {
                _ocr.ScpListWatch = on;
                if (on) { try { _ocr.ScanScpListNow(); } catch { } }   // 武装后立刻先扫一次
            };
            _manager.ScpScanPopup += delegate(string codes) { ShowScpScanToast(codes); };

            _ocr.OnTicketRecognized = delegate(string tag, string val, bool authoritative)
            {
                _manager.OnTicketRecognized(tag, val, authoritative);
                SetTacticalTicketSummary(_manager.TicketSummary());
                SetTacticalWaveInfo(_manager.WaveOrderText(), _manager.WavePredictText());
            };

            _ocr.Start();
            StartupSplash.Status("正在唤醒屏幕识别引擎…", 0.60, 2);

            // 启动演出（用户要求）：全部弹窗 → 故障退场 → 顶端"SCPSLAlert 已启动"
            // 注意：必须等启动画面完全退场再演。否则那些大块提示面板会在
            // 启动画面后面/四周冒出来，看起来就是一大坨黑色窗口。
            System.Windows.Forms.Timer startupDelay = new System.Windows.Forms.Timer();
            startupDelay.Interval = 400;
            startupDelay.Tick += delegate
            {
                if (StartupSplash.IsAlive) return;
                startupDelay.Stop();
                startupDelay.Dispose();
                try { _manager.PlayStartupShow(); } catch (Exception ex) { Logger.Err("启动", "启动演出失败", ex); }

                // 【新版行为】A.R.C.T 主界面启动时始终弹出，即使游戏已经在跑。
                // 旧版仍然保持原来的“游戏运行就不糊到游戏脸上”行为。
                try
                {
                    if (_config.UiStyle == 0)
                    {
                        if (_vigilStatus != null)
                        {
                            if (!_vigilStatus.IsVisible) _vigilStatus.Show();
                            if (_vigilStatus.WindowState == System.Windows.WindowState.Minimized)
                                _vigilStatus.WindowState = System.Windows.WindowState.Normal;
                            _vigilStatus.Activate();
                            Logger.On("启动", "新版主界面：启动时保持显示（即使游戏正在运行）");
                        }
                    }
                    else if (_manager.GameRunning && _settings != null && _settings.Visible)
                    {
                        _settings.Hide();
                        Logger.On("启动", "启动时检测到游戏正在运行 → 不弹旧版主界面（已收进托盘；动画照常播放）");
                    }
                }
                catch (Exception ex) { Logger.Err("启动", "处理启动主界面失败", ex); }

                if (_previousCrash != null)
                {
                    CrashReport report = _previousCrash;
                    _previousCrash = null;
                    CrashNotifier.Show(report);
                }
            };
            startupDelay.Start();

            // 【这里以前有个"启动 8 秒后就开始计时"的兜底，已经删掉】
            // 它的本意是"玩到一半才开程序 → 至少让钟走起来"，但只要游戏进程在跑
            // 就会无条件起算 —— 于是坐在主菜单里开程序，钟也自己开始走，
            // 看起来就是"一点开软件就自动开始游戏计时"。
            // 现在改成**有证据才起算**：日志（进图 / 回合开始）、手动 F6 / F7、
            // 或者 OCR 真认出对局里才有的东西（角色 / 刷新增援 / 票数 / 战术数据…），
            // 见 AlertManager.NoteRoundEvidence。

            // “立即扫描”快捷键：只扫那些没开定时轮询的区域（血量 / 发电机 / 顶部状态）
            _hotkeys.ScanNowPressed += delegate { ToggleScanResultPanel(); };   // F2：开/关扫描结果面板（再按一次收起）

            // 停止 / 恢复全部计时：再次按下同一快捷键会从停止时的剩余时间继续。
            _hotkeys.StopAllPressed += delegate
            {
                bool wasStopped = _manager.AllStopped;
                _manager.ToggleAllTimers();
                Balloon(wasStopped ? "已恢复全部计时。" : "已停止全部计时；再次按下可从原剩余时间恢复。");
            };

            // “停止音频”：只把正在放的那段自备音频掐掉，别的什么都不动。
            _hotkeys.StopAudioPressed += delegate
            {
                AudioLibrary.StopCurrent();
                Logger.On("音频", "按快捷键停掉当前音频");
            };

            // ---------- OCR 自动填写战术数据 → 刷一次战术面板 ----------
            // 单向回调：AlertManager 只在数据真的变了之后发一次通知，
            // 面板那边的刷新不会反过来再改数据，所以不存在循环重绘。
            _manager.TacticalChanged += delegate
            {
                try
                {
                    RefreshTacticalFromModel();

                    // 【踩过的坑】刷新顺序 / 后续推测这两行以前**只在"读到票数"
                    // 时才填**（那个回调只在玩家死亡时触发）。于是 OCR 认出
                    // 阵营刷新之后，数据其实已经进了模型，但「记录」页那两行
                    // 一直是空的 —— 看起来就像"刷新没算进面板"。
                    // 它们是模型数据，任何一次模型变化都该跟着刷。
                    SetTacticalWaveInfo(_manager.WaveOrderText(), _manager.WavePredictText());
                }
                catch (Exception ex) { Logger.Err("战术", "刷新面板失败", ex); }
            };

            // ---------- 游戏退了 → 本程序也跟着退 ----------
            // 【为什么要隔 3 秒确认一下】GameWatcher 判断"游戏在不在"靠的是
            // 遍历系统进程，而拿进程列表偶尔会失败（权限 / 瞬时抖动），
            // 那种抖动会被当成"游戏退出了"。提示弹错一下无所谓，
            // 但拿它直接关掉程序就过分了 —— 所以过 3 秒再看一眼，
            // 确实还没起来才退（顺带也给"关掉游戏马上重开"留了余地）。
            // 是否“跟着游戏一起退”交给玩家在「常规」页决定（默认不跟随）。
            // _config 在这里还没赋值？—— 由下面的 _config 字段初始化保证先于本委托执行。
            _manager.GameExited += delegate
            {
                if (_config != null && _config.ExitWithGame) ConfirmGameExitAndQuit();
            };

            // ---------- 第 5 批：游戏启动 → 静默复用一次可疑进程扫描 ----------
            _manager.GameStarted += delegate { ScanOnGameStart(); };

            // ---------- 托盘图标 ----------
            StartupSplash.Status("正在准备托盘与主界面…", 0.74, 3);
            // 1) 先画图，结果存进静态字段 _trayBitmap（不进局部变量、不 Dispose）
            CreateTrayImage();

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("微软雅黑", 9f, FontStyle.Regular, GraphicsUnit.Point);

            ToolStripMenuItem head = new ToolStripMenuItem("SCPSLAlert");
            head.Enabled = false;
            menu.Items.Add(head);

            // ---- 状态区（灰的，点不动，只是"一眼看到现在什么状态"）----
            _stOcr = new ToolStripMenuItem("屏幕识别：");
            _stSound = new ToolStripMenuItem("提示音：");
            _stClock = new ToolStripMenuItem("回合计时：");
            _stAlerts = new ToolStripMenuItem("提示：");
            _stAdmin = new ToolStripMenuItem("权限：");
            ToolStripMenuItem[] stats = new ToolStripMenuItem[] { _stOcr, _stSound, _stClock, _stAlerts, _stAdmin };
            for (int i = 0; i < stats.Length; i++)
            {
                stats[i].Enabled = false;      // 只是显示，不给点
                menu.Items.Add(stats[i]);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem vigil = new ToolStripMenuItem("状态面板（A.R.C.T）");
            vigil.Click += delegate { ShowVigilStatus(); };
            menu.Items.Add(vigil);

            ToolStripMenuItem openSettings = new ToolStripMenuItem("打开设置");
            openSettings.Font = new Font(menu.Font, FontStyle.Bold);
            openSettings.Click += delegate { ShowSettings(); };
            menu.Items.Add(openSettings);

            _editItem = new ToolStripMenuItem("调整提示位置");
            _editItem.Click += delegate { ToggleEditMode(); };
            menu.Items.Add(_editItem);

            ToolStripMenuItem toggleAll = new ToolStripMenuItem("显示 / 隐藏全部提示");
            toggleAll.Click += delegate { _manager.ToggleVisibleAll(); };
            menu.Items.Add(toggleAll);

            ToolStripMenuItem stopAll = new ToolStripMenuItem("停止 / 恢复全部计时");
            stopAll.Click += delegate { _manager.ToggleAllTimers(); };
            menu.Items.Add(stopAll);

            // 中途加入（没检测到回合开始、OCR 也没认出播报）时手动起净化
            ToolStripMenuItem decon = new ToolStripMenuItem("净化倒计时（手动开始）");
            decon.Click += delegate
            {
                _manager.StartDeconByHotkey(_config.DeconBaseSeconds);
                Balloon("净化倒计时已手动开始（" + _config.DeconBaseSeconds + " 秒）");
            };
            menu.Items.Add(decon);

            // 关闭按钮的行为：点 × 是最小化还是退出，随时可以在这里改
            ToolStripMenuItem closeMenu = new ToolStripMenuItem("关闭按钮的行为");
            ToolStripMenuItem cMin = new ToolStripMenuItem("最小化到托盘");
            ToolStripMenuItem cExit = new ToolStripMenuItem("直接退出程序");
            ToolStripMenuItem cAsk = new ToolStripMenuItem("每次都问我");
            cMin.Click += delegate { SetCloseAction(1); };
            cExit.Click += delegate { SetCloseAction(2); };
            cAsk.Click += delegate { SetCloseAction(0); };
            closeMenu.DropDownItems.Add(cMin);
            closeMenu.DropDownItems.Add(cExit);
            closeMenu.DropDownItems.Add(new ToolStripSeparator());
            closeMenu.DropDownItems.Add(cAsk);
            _closeMenu = closeMenu;
            _closeMin = cMin;
            _closeExit = cExit;
            _closeAsk = cAsk;
            menu.Items.Add(closeMenu);

            ToolStripMenuItem panelItem = new ToolStripMenuItem("通知开关面板…");
            panelItem.Click += delegate { ToggleNotifyPanel(); };
            menu.Items.Add(panelItem);

            // ---------- 音频（操作者要求：主界面加一个新菜单）----------
            // 托盘菜单就是这个程序的"主界面"，所以音频相关的都在这里：
            // 打开文件夹、试听、停止、换默认音频、开关总开关。
            ToolStripMenuItem audioMenu = new ToolStripMenuItem("音频");
            _audioMenu = audioMenu;

            _audioNow = new ToolStripMenuItem("默认音频：");
            _audioNow.Enabled = false;                 // 只是显示现在用的是哪一段
            audioMenu.DropDownItems.Add(_audioNow);

            _audioOn = new ToolStripMenuItem("启用提示音");
            _audioOn.CheckOnClick = false;
            _audioOn.Click += delegate { ToggleSound(); };
            audioMenu.DropDownItems.Add(_audioOn);

            audioMenu.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem aTest = new ToolStripMenuItem("试听默认音频");
            aTest.Click += delegate { TestDefaultAudio(); };
            audioMenu.DropDownItems.Add(aTest);

            ToolStripMenuItem aStop = new ToolStripMenuItem("停止播放");
            aStop.Click += delegate
            {
                AudioLibrary.StopCurrent();
                Balloon("已停止播放音频");
            };
            audioMenu.DropDownItems.Add(aStop);

            ToolStripMenuItem aOpen = new ToolStripMenuItem("打开音频文件夹…");
            aOpen.Click += delegate { OpenAudioFolder(); };
            audioMenu.DropDownItems.Add(aOpen);

            ToolStripMenuItem aReload = new ToolStripMenuItem("重新扫描音频文件夹");
            aReload.Click += delegate
            {
                RebuildAudioList();
                // 【操作者要求】托盘里这个"刷新"也要把**设置窗口的音频页**一起刷新
                //（以前只重建了托盘自己的子菜单，设置窗口开着的话列表还是旧的）。
                RefreshSettingsAudioIfOpen();
                Balloon("音频重新扫描完成：文件夹里现有 " + AudioLibrary.List().Length
                    + " 个可用音频（片段见设置窗口的「音频」页）");
            };
            audioMenu.DropDownItems.Add(aReload);

            audioMenu.DropDownItems.Add(new ToolStripSeparator());

            _audioList = new ToolStripMenuItem("选默认音频");
            audioMenu.DropDownItems.Add(_audioList);
            BuildAudioListItems();

            menu.Items.Add(audioMenu);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem calibrate = new ToolStripMenuItem("时间校准（战术面板）…");
            calibrate.Click += delegate { ShowTimeCalibration(); };
            menu.Items.Add(calibrate);

            ToolStripMenuItem warhead = new ToolStripMenuItem("切换核弹倒计时");
            warhead.Click += delegate { _manager.ToggleWarheadByHotkey(); };
            menu.Items.Add(warhead);

            ToolStripMenuItem tour = new ToolStripMenuItem("新手教程");
            tour.Click += delegate { ShowTour(); };
            menu.Items.Add(tour);

            // 没提权时才给这一项：游戏若是管理员运行，非管理员的热键和截图
            // 都会被 Windows 的 UIPI 挡住（现象就是"热键全失效 + OCR 全黑"）。
            if (!IsElevated())
            {
                ToolStripMenuItem admin = new ToolStripMenuItem("以管理员身份重新启动");
                admin.Click += delegate { RelaunchAsAdmin(); };
                menu.Items.Add(admin);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem reload = new ToolStripMenuItem("重新加载配置");
            reload.Click += delegate { ReloadConfig(); };
            menu.Items.Add(reload);

            ToolStripMenuItem openCfg = new ToolStripMenuItem("打开 config.ini");
            openCfg.Click += delegate { OpenPath(_configPath, false); };
            menu.Items.Add(openCfg);

            ToolStripMenuItem openLog = new ToolStripMenuItem("打开日志目录");
            openLog.Click += delegate { OpenPath(LogRoot(), true); };
            menu.Items.Add(openLog);

            ToolStripMenuItem crashMenu = new ToolStripMenuItem("崩溃记录");
            ToolStripMenuItem appCrash = new ToolStripMenuItem("程序崩溃目录");
            appCrash.Click += delegate { OpenPath(CrashPaths.Dir(CrashPaths.AppCrash), true); };
            crashMenu.DropDownItems.Add(appCrash);
            ToolStripMenuItem gameCrash = new ToolStripMenuItem("游戏崩溃目录");
            gameCrash.Click += delegate { OpenPath(CrashPaths.Dir(CrashPaths.GameCrash), true); };
            crashMenu.DropDownItems.Add(gameCrash);
            ToolStripMenuItem backup = new ToolStripMenuItem("配置备份目录");
            backup.Click += delegate { OpenPath(CrashPaths.Dir(CrashPaths.Backup), true); };
            crashMenu.DropDownItems.Add(backup);
            menu.Items.Add(crashMenu);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出程序");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            // 2) 转成 Icon（同样是静态字段，永不 Dispose）
            _trayIcon = Icon.FromHandle(_trayBitmap.GetHicon());

            // 把菜单也存成静态字段：资源管理器重启后重建 NotifyIcon 时还要用它
            _trayMenu = menu;
            // 交给 NotifyIcon 自己弹菜单：它会正确处理前台窗口/焦点，
            // 避免手工 Show() 时“闪一下又消失、点几次才出来”。
            menu.Opening += delegate { RefreshTrayStatus(); };

            // 3) 挂到 NotifyIcon 上并显示；NotifyIcon 也放在静态字段里由主循环保活
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _trayIcon;
            _notifyIcon.Text = "SCPSLAlert";
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += delegate { ShowSettings(); };

            // 主题切换时同步换托盘图标（亮/暗两套）
            try { ModernTheme.Changed += delegate { RefreshTrayIcon(); }; } catch { }

            // 每次打开菜单时刷新：状态栏 + "关闭按钮的行为"的勾选
            menu.Opening += delegate
            {
                try
                {
                    _closeMin.Checked = (_config.CloseAction == 1);
                    _closeExit.Checked = (_config.CloseAction == 2);
                    _closeAsk.Checked = (_config.CloseAction != 1 && _config.CloseAction != 2);

                    RefreshTrayStatus();
                }
                catch { }
            };

            // 4) 放行 TaskbarCreated —— 本程序是管理员权限，explorer 是普通权限，
            //    不放行的话资源管理器重启后图标就再也不会回来了
            NativeMethods.AllowMessage(_handle, NativeMethods.WM_TASKBARCREATED);
            NativeMethods.AllowMessage(_msgWin.Handle, NativeMethods.WM_TASKBARCREATED);
            NativeMethods.AllowMessage(_msgWin.Handle, NativeMethods.WM_COPYDATA);

            // 5) 真正挂上去，并且定时重试 —— explorer 没准备好时第一次可能不成功
            EnsureTrayIcon();

            // 托盘图标没挂上就别允许"最小化到托盘"，否则窗口会再也找不回来
            if (_settings != null) _settings.CanMinimizeToTray = _trayIconAdded;

            _trayRetry = new System.Windows.Forms.Timer();
            _trayRetry.Interval = 2500;
            _trayRetry.Tick += delegate
            {
                if (_trayIconAdded) { _trayRetry.Stop(); return; }
                if (++_trayAttempts > 24)   // 一分钟还没成功就不再刷屏了
                {
                    _trayRetry.Stop();
                    Logger.On("托盘", "重试 24 次仍未成功；可以看使用说明里的"
                        + "「找不到托盘图标」一节（Win11 默认会收进溢出区）");
                    return;
                }
                EnsureTrayIcon();
            };
            _trayRetry.Start();

            // ---------- 设置窗口（启动时默认弹出）----------
            StartupSplash.Status("正在构建状态面板…", 0.86, 3);
            if (_config.UiStyle == 0)
            {
                ShowVigilStatus();
                if (_vigilStatus == null)
                {
                    _settings = CreateSettingsForm(false);
                    WireSettings(_settings);
                }
            }
            else
            {
                _settings = CreateSettingsForm(false);
                WireSettings(_settings);
            }
            _ocr._onScanExtra = delegate
            {
                string s = _manager.TicketSummary();
                string scp = _manager.PresentScpSummary();
                if (scp.Length > 0) s += (s.Length > 0 ? "　" : "") + scp;
                return s;
            };

            // ---------- 时间校准：已经并进战术面板的「校准」页 ----------
            _manager.CalibrateRequested += delegate { ShowTimeCalibration(); };

            // ---------- 托盘气泡上的日志状态 ----------
            System.Windows.Forms.Timer tipTimer = new System.Windows.Forms.Timer();
            tipTimer.Interval = 2000;
            tipTimer.Tick += delegate { UpdateTrayText(); };
            tipTimer.Start();

            // ---------- 第二实例请求显示设置窗口 ----------
            _showEvent = showEvent;
            if (_showEvent != null)
            {
                _showReg = ThreadPool.RegisterWaitForSingleObject(
                    _showEvent, OnShowSettingsSignal, null, -1, false);
            }

            // 按 config.ini 注册全局热键
            _hotkeys.RegisterAll(_config);
            if (_hotkeys.Failed.Count > 0)
            {
                StatusTextHolder = "有热键被占用：" + string.Join("、", _hotkeys.Failed.ToArray());
            }
            else if (_hotkeys.Duplicates.Count > 0)
            {
                // 撞了快捷键是"静默失效"，必须让用户看见
                StatusTextHolder = "快捷键冲突：" + string.Join("；", _hotkeys.Duplicates.ToArray());
            }

            // 诊断：把热键注册结果写到临时目录，方便排查“按键没反应”
            try
            {
                string rep = "识别方式：" + (_hotkeys.UsingHook
                        ? "低级键盘钩子（不占用按键，游戏也能收到）"
                        : "RegisterHotKey 回退方案")
                           + "\r\n注册成功 " + _hotkeys.RegisteredCount + " 个，失败 "
                           + _hotkeys.Failed.Count + " 个\r\n";
                if (!_hotkeys.UsingHook)
                    rep += "钩子安装失败原因：" + _hotkeys.HookFailReason + "\r\n";
                if (_hotkeys.Failed.Count > 0)
                    rep += "失败清单：" + string.Join("、", _hotkeys.Failed.ToArray()) + "\r\n";
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SCPSLAlert_hotkeys.txt"),
                    rep, new System.Text.UTF8Encoding(false));
            }
            catch { }

            LogHotkeyReport();

            ShowSettings();

            // 主界面已经显示。启动画面会按固定节奏走完四个阶段再淡出，
            // 每个阶段至少停留 850ms，进度条会在每个格子停一下。
            StartupSplash.HandOff();
            Logger.On("启动", "初始化完成，启动画面交接；本次初始化用时 "
                + _bootWatch.ElapsedMilliseconds + " ms");

            // 第一次运行：问一句要不要看教程，然后完全按用户的选择来
            if (_firstRun) AskAboutTour();
        }

        private bool _firstRun;

        /// <summary>
        /// 首次运行问一次“要不要看新手教程”。
        /// 只是问，不强制 —— 选“否”就永远不再自动问（想看得自己去「开始使用」页点）。
        /// </summary>
        private void AskAboutTour()
        {
            try
            {
                DialogResult r = MessageBox.Show(
                    "第一次使用，要不要看一遍新手教程？\n\n"
                  + "教程会用高亮框把整个界面走一遍，并真的把提示和面板弹出来让你试一下。\n"
                  + "随时可以按 Esc 或点「跳过」退出。\n\n"
                  + "（以后想再看，到「开始使用」页点「开始新手教程」，或用托盘菜单）",
                    "SCPSLAlert · 第一次使用",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (r == DialogResult.Yes) ShowTour();
            }
            catch { }
        }

        private void OnShowSettingsSignal(object state, bool timedOut)
        {
            // 回调在线程池线程上，转回 UI 线程执行
            if (_hotkeys != null)
                _hotkeys.RunOnUi(delegate { ShowSettings(); });
        }

        #region 托盘 / 设置

        /// <summary>
        /// 画托盘图标。结果放进静态字段 _trayBitmap —— 绝对不能是局部变量，
        /// 也绝对不能 Dispose：HICON 的数据就挂在这张位图上。
        /// </summary>
        private static void CreateTrayImage()
        {
            if (_trayBitmap != null) return;

            // 用启动界面徽标（亮/暗两套），不再画旧的 "SL" 占位图。
            _trayBitmap = AppMark.Render(!ModernTheme.DetectDark(), 32);
            // 注意：这里不 Dispose 位图，也不 DestroyIcon
        }

        /// <summary>主题切换后换托盘图标（亮/暗两套）。旧图标不 Dispose。</summary>
        private static void RefreshTrayIcon()
        {
            try
            {
                if (_notifyIcon == null) return;
                _trayIcon = AppMark.Tray(!ModernTheme.IsDark);
                _notifyIcon.Icon = _trayIcon;
                _notifyIcon.Text = "SCPSLAlert";
            }
            catch { }
        }

        private ToolStripMenuItem _closeMenu;
        private ToolStripMenuItem _closeMin;
        private ToolStripMenuItem _closeExit;
        private ToolStripMenuItem _closeAsk;

        /// <summary>改"点 × 的行为"（0 = 每次问，1 = 最小化到托盘，2 = 退出）。</summary>
        private void SetCloseAction(int action)
        {
            _config.CloseAction = action;
            try { _config.Save(_configPath); } catch { }
            Logger.On("界面", "关闭按钮行为改为：" + CloseActionName(action));
            Balloon("关闭按钮的行为已改为：" + CloseActionName(action));
        }

        // ============================================================
        // 音频菜单（托盘菜单里的「音频」子菜单）
        // ============================================================

        /// <summary>开关"提示音"总开关（含自备音频），立刻存盘并刷新托盘状态。</summary>
        private void ToggleSound()
        {
            _config.SoundEnabled = !_config.SoundEnabled;
            if (!_config.SoundEnabled) AudioLibrary.StopCurrent();
            try { _config.Save(_configPath); } catch { }
            _manager.ApplyConfig();
            RefreshTrayStatus();
            RefreshAudioMenu();
            Logger.On("音频", "提示音总开关：" + (_config.SoundEnabled ? "开" : "关"));
            Balloon(_config.SoundEnabled
                ? "提示音已打开（系统音效 + 自备音频）"
                : "提示音已关闭（所有提示都不响，含自备音频）");
        }

        /// <summary>试听当前的全局默认音频。</summary>
        private void TestDefaultAudio()
        {
            string f = _config.AudioDefault;
            if (string.IsNullOrEmpty(f))
            {
                Balloon("还没设默认音频。先在「音频」文件夹里丢一段音频，"
                    + "再到下面的「选默认音频」里点它。");
                return;
            }
            if (!AudioLibrary.PlayKey(f, "托盘菜单里试听"))
                Balloon("这段播不出来：" + AudioLibrary.LastError
                    + "（有些格式 Windows 放不了，换 wav / mp3 试试）");
            else Balloon("正在播放：" + f);
        }

        /// <summary>打开（必要时创建）音频文件夹。</summary>
        private void OpenAudioFolder()
        {
            try
            {
                string dir = AudioLibrary.ResolveDir(_config.AudioDir);
                if (string.IsNullOrEmpty(dir)) { Balloon("找不到音频文件夹"); return; }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                Logger.Err("音频", "打开音频文件夹失败", ex);
                Balloon("打不开音频文件夹：" + ex.Message);
            }
        }

        /// <summary>重新扫描音频文件夹，重建"选默认音频"的子项。</summary>
        private void RebuildAudioList()
        {
            try
            {
                AudioLibrary.ResolveDir(_config.AudioDir);
                AudioLibrary.ApplyConfig(_config);
                BuildAudioListItems();
                RefreshAudioMenu();
            }
            catch (Exception ex) { Logger.Err("音频", "重扫音频失败", ex); }
        }

        /// <summary>设置窗口开着的话，把它的「音频」页也刷成最新的。</summary>
        private void RefreshSettingsAudioIfOpen()
        {
            try
            {
                if (_settings != null && !_settings.IsDisposed && _settings.Visible)
                    _settings.RefreshAudioFromDisk();
            }
            catch (Exception ex) { Logger.Err("音频", "刷新设置窗口音频页失败", ex); }
        }

        /// <summary>把音频文件夹里的文件做成"选默认音频"的子项（点一下就设成默认并试听）。</summary>
        private void BuildAudioListItems()
        {
            if (_audioList == null) return;
            _audioList.DropDownItems.Clear();

            ToolStripMenuItem none = new ToolStripMenuItem("（不放音频）");
            none.Click += delegate { SetDefaultAudio(""); };
            _audioList.DropDownItems.Add(none);

            string[] files = AudioLibrary.List();
            if (files.Length == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem("（「音频」文件夹里还没有文件）");
                empty.Enabled = false;
                _audioList.DropDownItems.Add(empty);
                return;
            }

            _audioList.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < files.Length && i < 40; i++)
            {
                string name = files[i];
                ToolStripMenuItem it = new ToolStripMenuItem(name);
                it.Click += delegate { SetDefaultAudio(name); };
                _audioList.DropDownItems.Add(it);
            }
            if (files.Length > 40)
            {
                ToolStripMenuItem more = new ToolStripMenuItem("…（还有更多，见音频文件夹）");
                more.Enabled = false;
                _audioList.DropDownItems.Add(more);
            }
        }

        /// <summary>把某一段音频设成全局默认（点菜单里那一条时调用），马上试听一下。</summary>
        private void SetDefaultAudio(string file)
        {
            _config.AudioDefault = file;
            try { _config.Save(_configPath); } catch { }
            _manager.ApplyConfig();
            RefreshAudioMenu();
            Logger.On("音频", "默认音频改为：" + (string.IsNullOrEmpty(file) ? "（不放）" : file));
            if (string.IsNullOrEmpty(file)) Balloon("已取消默认音频");
            else if (AudioLibrary.PlayKey(file, "托盘菜单里试听")) Balloon("默认音频：" + file);
            else Balloon("设成默认了，但这段播不出来：" + AudioLibrary.LastError);
        }

        /// <summary>刷新音频菜单上的勾选 / 当前默认音频显示。</summary>
        private void RefreshAudioMenu()
        {
            try
            {
                if (_audioOn != null) _audioOn.Checked = _config.SoundEnabled;
                if (_audioNow != null)
                {
                    string f = _config.AudioDefault;
                    _audioNow.Text = "默认音频：" + (string.IsNullOrEmpty(f) ? "（没设）" : f);
                }
                if (_audioList != null)
                {
                    for (int i = 0; i < _audioList.DropDownItems.Count; i++)
                    {
                        ToolStripMenuItem it = _audioList.DropDownItems[i] as ToolStripMenuItem;
                        if (it == null) continue;
                        it.Checked = (it.Text == _config.AudioDefault && !string.IsNullOrEmpty(_config.AudioDefault));
                    }
                }
            }
            catch { }
        }

        private static string CloseActionName(int a)
        {
            if (a == 1) return "最小化到托盘";
            if (a == 2) return "直接退出程序";
            return "每次都问我";
        }

        /// <summary>托盘菜单顶部的状态栏条目（打开菜单时刷新文字）。</summary>
        private ToolStripMenuItem _stOcr;
        private ToolStripMenuItem _stSound;
        private ToolStripMenuItem _stClock;
        private ToolStripMenuItem _stAlerts;
        private ToolStripMenuItem _stAdmin;

        /// <summary>左下角"OCR 认到了什么"的小提示（懒创建，用一次就不销毁）。</summary>
        private OcrToastForm _ocrToast;

        /// <summary>
        /// 刷新托盘菜单顶部的状态显示。
        /// 这些条目是灰的（点不动），纯粹用来"一眼看到现在是什么状态"。
        /// </summary>
        private void ShowTrayMenu()
        {
            if (_trayMenu == null) return;
            try
            {
                if (_msgWin != null) NativeMethods.SetForegroundWindow(_msgWin.Handle);
                _trayMenu.Show(Cursor.Position);
            }
            catch (Exception ex)
            {
                Logger.Err("托盘", "打开托盘菜单失败", ex);
            }
        }
        private void RefreshTrayStatus()
        {
            if (_stOcr == null) return;

            // 屏幕识别
            string ocr = _config.OcrAllowed ? "开" : "关";
            if (_config.OcrAllowed)
            {
                ocr += "（" + OcrEngine.Status;
                if (OcrEngine.IsReady && _ocr != null) ocr += "｜" + _ocr.LastInfo;
                ocr += "）";
            }
            _stOcr.Text = "屏幕识别：" + ocr;

            // 提示音
            _stSound.Text = "提示音：" + (_config.SoundEnabled ? "开" : "关");

            // 回合计时 / 是否停过
            string clock;
            if (_manager.AllStopped) clock = "已停止（有新事件会自动恢复）";
            else clock = _manager.ClockPaused ? "已暂停" : "运行中";
            _stClock.Text = "回合计时：" + clock;

            // 有没有被整体隐藏 / 关掉了哪些提示
            int on = 0;
            for (int i = 0; i < _manager.AlertCount; i++) if (_manager.AlertEnabled(i)) on++;
            _stAlerts.Text = "提示：" + on + " / " + _manager.AlertCount + " 类开启"
                + (_manager.AllHidden ? "｜已隐藏" : "");

            // 权限（热键和截图能不能用的前提）
            _stAdmin.Text = "权限：" + (IsElevated() ? "管理员（正常）" : "普通（游戏若是管理员会收不到热键/截图）");

            // 音频菜单上的勾选 / "默认音频：xxx" 也跟着刷新一下
            RefreshAudioMenu();
        }

        private void ShowSettings()
        {
            if (_disposed) return;
            if (_modernHost != null && !_modernHost.IsDisposed) _modernHost.Hide();
            if (_config.UiStyle == 0)
            {
                if (_settings != null && _settings.Visible) _settings.Hide();
                ShowVigilStatus();
                return;
            }
            if (_vigilStatus != null) _vigilStatus.Hide();
            if (_settings != null) _settings.ShowSettings();
        }

        private void ShowInterfaceChooser()
        {
            if (_disposed) return;
            ShowModernWindow(true);
        }

        private void ShowModernShell()
        {
            ShowSettings();
        }

        private void ShowModernWindow(bool chooser)
        {
            if (!chooser)
            {
                ShowSettings();
                return;
            }
            try
            {
                if (_modernHost == null || _modernHost.IsDisposed)
                {
                    _modernHost = new System.Windows.Forms.Form();
                    _modernHost.Text = "SCPSLAlert · 选择界面";
                    _modernHost.ClientSize = new System.Drawing.Size(680, 490);
                    _modernHost.MinimumSize = new System.Drawing.Size(680, 490);
                    _modernHost.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                    _modernHost.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
                    _modernHost.MaximizeBox = false;
                    _modernHost.ShowInTaskbar = true;
                    _modernHost.FormClosed += delegate { if (!_disposed) ExitApp(); };
                    _modernView = new ModernStartView(delegate(int style)
                    {
                        _config.MainUiStyle = style;
                        _config.Save(_configPath);
                        bool restart = false;
                        using (RestartConfirmForm f = new RestartConfirmForm(style == 0 ? "新版" : "旧版"))
                            restart = f.ShowDialog(_modernHost) == DialogResult.OK && f.Accepted;
                        if (restart)
                        {
                            RestartApplication();
                            return;
                        }
                        _modernHost.Hide();
                        UseSettingsForm(style);
                    }, _config.UiAnimationsEnabled, _config.UiAnimationDurationMs);
                    _modernView.Dock = System.Windows.Forms.DockStyle.Fill;
                    _modernHost.Controls.Add(_modernView);
                }
                _modernHost.Text = "SCPSLAlert · 选择界面";
                if (!_modernHost.Visible) _modernHost.Show();
                _modernHost.Activate();
            }
            catch (Exception ex)
            {
                Logger.Err("界面", "界面选择窗口启动失败，回退到旧版", ex);
                _config.MainUiStyle = 1;
                try { _config.Save(_configPath); } catch { }
                ShowSettings();
            }
        }

        private void OpenClassicSettings()
        {
            try
            {
                if (_vigilStatus != null) _vigilStatus.Hide();
                if (_settings != null && !_settings.IsDisposed)
                {
                    _settings.ShowSettings();
                    return;
                }
                _settings = CreateSettingsForm(false);
                WireSettings(_settings);
                _settings.ShowSettings();
            }
            catch (Exception ex)
            {
                Logger.Err("界面", "打开经典设置失败", ex);
            }
        }

        private void OpenClassicSettingsPage(int page)
        {
            OpenClassicSettings();
            if (_settings != null && !_settings.IsDisposed) _settings.ShowPage(page);
        }

        private void WithLegacySettings(Action<SettingsForm> action)
        {
            if (action == null) return;
            OpenClassicSettings();
            if (_settings != null && !_settings.IsDisposed) action(_settings);
        }

        private void OpenVigilBrightness(bool autoCapture)
        {
            try
            {
                if (_vigilStatus != null) _vigilStatus.Hide();
                _gameBrightness.SetSuspended(true);
                GameBrightnessAssistantForm f = new GameBrightnessAssistantForm(_config,
                    delegate { _config.Save(_configPath); },
                    delegate
                    {
                        _gameBrightness.SetSuspended(false);
                        if (!_disposed && _vigilStatus != null)
                        {
                            _vigilStatus.Show();
                            _vigilStatus.Activate();
                        }
                    });
                if (autoCapture) f.Shown += delegate { f.BeginAutoCapture(); };
                f.Show();
                f.Activate();
                f.BringToFront();
            }
            catch (Exception ex)
            {
                Logger.Err("界面", "打开亮度校准失败", ex);
            }
        }

        private void BeginAdjustFromVigil()
        {
            _adjustReturnToVigil = true;
            if (_vigilStatus != null) _vigilStatus.Hide();
            BeginAdjustPosition();
        }
        private void OpenOneMap()
        {
            try
            {
                MatchRecord live = _vigilStatus != null ? _vigilStatus.LiveMatchSnapshot() : null;
                List<MatchRecord> list = MatchStore.ListRecent(20);
                if (live != null) list.Insert(0, live);
                if (list.Count == 0)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "还没有对局记录。先玩一局，游戏结束后会自动记录。",
                        "一图流", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                    return;
                }
                WpfOneMapWindow w = new WpfOneMapWindow(_config, list[0], !ModernTheme.IsDark,
                    delegate { return _vigilStatus != null ? _vigilStatus.LiveMatchSnapshot() : null; });
                if (_vigilStatus != null) w.Owner = _vigilStatus;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Err("一图流", "打开预览失败", ex);
            }
        }
        private void ShowVigilStatus()
        {
            if (_vigilStatus != null)
            {
                try
                {
                    // 启动画面还没跑完时，主窗口由 WpfVigilDataWindow 自己的
                    // 100% 轮询负责 Show，这里不能提前弹出来。
                    if (StartupSplash.IsAlive) return;
                    if (!_vigilStatus.IsVisible) _vigilStatus.Show();
                    if (_vigilStatus.WindowState == System.Windows.WindowState.Minimized)
                        _vigilStatus.WindowState = System.Windows.WindowState.Normal;
                    _vigilStatus.Activate();
                }
                catch (Exception ex)
                {
                    Logger.Err("界面", "重新显示 A.R.C.T 状态面板失败", ex);
                }
                return;
            }
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    System.Windows.Application wpf = new System.Windows.Application();
                    wpf.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }
                _vigilStatus = new WpfVigilDataWindow(_config, !ModernTheme.DetectDark(),
                    delegate { LaunchScpsl(); },
                    delegate { ExitApp(); },
                    delegate { KillGameProcess(); },
                    delegate { return BuildVigilSnapshot(); },
                    delegate { OpenClassicSettings(); },
                    delegate { _config.Save(_configPath); try { _manager.ApplyConfig(); } catch { } },
                    delegate { ShowTimeCalibration(); },
                    delegate { ReRegisterHotkeys(); },
                    delegate { try { _manager.ArmScpScan("手动全部扫描"); } catch { } _ocr.ScanOnDemand(true); },
                    delegate
                    {
                        _manager.PreviewAll();
                        try
                        {
                            if (_heartPrompt != null)
                                _heartPrompt.Preview(Math.Max(4, _config.DisplaySeconds), 78);
                        }
                        catch { }
                    },
                    delegate { ShowScanResultPanel(); },
                    delegate { RestartApplication(); },
                    delegate(int page) { OpenClassicSettingsPage(page); },
                    delegate { if (_ocr != null) _ocr.ApplyConfig(); },
                    delegate { WithLegacySettings(delegate(SettingsForm s) { s.ShowOcrPreview(); }); },
                    delegate { WithLegacySettings(delegate(SettingsForm s) { s.ShowOcrRegionPicker(); }); },
                    delegate { WithLegacySettings(delegate(SettingsForm s) { s.ShowOcrRegionsEditor(); }); },
                    delegate { WithLegacySettings(delegate(SettingsForm s) { s.ShowOcrHelp(); }); },
                    delegate(bool auto) { OpenVigilBrightness(auto); },
                    delegate { BeginAdjustFromVigil(); },
                    delegate(double sec) { ApplyTimeCalibration("deadman", sec); },
                    delegate { WithLegacySettings(delegate(SettingsForm s) { s.ShowLogTimeScan(); }); },
                    delegate { EnsureWpfTactical().OpenEditor(); },
                    delegate { if (_wpfTactical != null) _wpfTactical.ApplyTacticalConfig(); },
                    delegate
                    {
                        _config.ForceLegacyThisRun = false;
                        _config.MainUiStyle = 1;
                        try { _config.Save(_configPath); } catch { }
                        UseSettingsForm(1);
                    },
                    delegate { if (_manager != null) _manager.RequestFactionScan("手动重新识别"); },
                    delegate { CaptureStart(); },
                    delegate { CapturePause(); },
                    delegate { CaptureStop(); },
                    delegate { return CaptureDelete(); },
                    delegate { OpenOneMap(); });
                _vigilStatus.Closed += delegate { _vigilStatus = null; };
                // 启动阶段先不 Show，避免主窗口的黑底在启动画面后面露出；
                // 窗口内部的轮询会在进度条 100% 时调用 PlayEntrance() -> Show()。
                if (!StartupSplash.IsAlive) _vigilStatus.Show();
            }
            catch (Exception ex)
            {
                Logger.Err("界面", "A.R.C.T 状态面板打开失败", ex);
            }
        }

        private VigilSnapshot BuildVigilSnapshot()        {
            VigilSnapshot s = new VigilSnapshot();
            s.GameRunning = _manager != null && _manager.GameRunning;
            s.InServer = _manager != null && _manager.InServer;
            s.SelfDead = _manager != null && _manager.SelfDead;
            s.Role = _manager != null ? _manager.LastRole : "";
            try { s.RoleTimeline = _manager != null ? _manager.RoleTimeline.Summary(4) : ""; } catch { }
            try { s.SelfState = _manager != null ? _manager.RoleTimeline.CurrentState : ""; } catch { }
            s.RoundStart = _manager != null ? _manager.RoundStartedAt : DateTime.MinValue;
            s.OcrInfo = _ocr != null ? _ocr.LastInfo : "";
            s.LastScan = _ocr != null ? _ocr.LastScanSummary : "";
            if (_manager != null)
            {
                s.WarheadRunning = _manager.Estimator.WarheadRunning;
                s.WarheadRemaining = _manager.Estimator.WarheadRemaining;
                s.WarheadTotal = _manager.Estimator.WarheadSeconds;
                s.DeadManActive = _manager.DeadManActive;
                s.DeadManRemaining = _manager.DeadManRemaining;
                s.DeadManTotal = _manager.DeadManTotal;
                try
                {
                    List<TacticalLog.ScpRecord> recs = _manager.Tactical.Records;
                    for (int i = 0; i < recs.Count; i++)
                    {
                        TacticalLog.ScpRecord rec = recs[i];
                        if (!rec.Present && !rec.Killed) continue;
                        VigilScpState v = new VigilScpState();
                        v.Code = rec.Code;
                        v.Present = rec.Present;
                        v.Killed = rec.Killed;
                        v.ChangedAt = rec.ChangedAt;
                        s.Scps.Add(v);
                    }
                }
                catch { }

                List<TacticalLog.WaveRecord> wh = _manager.Tactical.WaveHistory;
                int from = Math.Max(0, wh.Count - 12);
                for (int i = from; i < wh.Count; i++)
                {
                    VigilWave w = new VigilWave();
                    w.At = wh[i].At;
                    w.Faction = wh[i].Faction;
                    w.IsSmall = wh[i].IsSmall;
                    s.Waves.Add(w);
                }
            }
            return s;
        }

        private void KillGameProcess()
        {
            try
            {
                Process[] all = Process.GetProcesses();
                for (int i = 0; i < all.Length; i++)
                {
                    try
                    {
                        string name = all[i].ProcessName;
                        if (name.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0)
                            all[i].Kill();
                    }
                    catch { }
                    finally { try { all[i].Dispose(); } catch { } }
                }
            }
            catch (Exception ex)
            {
                Logger.Err("界面", "结束游戏进程失败", ex);
            }
        }
        private SettingsForm CreateSettingsForm(bool modern)
        {
            Action adjust = delegate { BeginAdjustPosition(); };
            Action exit = delegate { ExitApp(); };
            Action save = delegate { _config.Save(_configPath); };
            Action launch = delegate { LaunchScpsl(); };
            // 【废案下线】modern 分支原来创建 ModernSettingsForm（横向导航那个新版设置）。
            // 那个界面已废弃：它的内容本来就是旧设置的子类，所以这里统一交给旧设置。
            // 参数保留只为不改动调用方；ModernSettingsForm.cs 仍留在工程里备查，不再被创建。
            ModernTheme.Active = modern;
            if (modern)
            {
                ModernTheme.SetMode(_config.UiThemeMode);
                ModernTheme.Start();
            }
            return new SettingsForm(_config, _manager, adjust, exit, save, launch);
        }

        private void WireSettings(SettingsForm s)
        {
            if (s == null) return;
            s.RestartApplication = RestartApplication;
            s.SwitchToModernUi = delegate
            {
                _config.ForceLegacyThisRun = false;
                _config.MainUiStyle = 0;
                try { _config.Save(_configPath); } catch { }
                UseSettingsForm(0);
            };
            s.SwitchToLegacyUi = delegate
            {
                _config.ForceLegacyThisRun = false;
                _config.MainUiStyle = 1;
                try { _config.Save(_configPath); } catch { }
                UseSettingsForm(1);
            };
            s.OpenGameBrightnessAssistant = delegate(bool autoCapture)
            {
                _gameBrightness.SetSuspended(true);
                s.Hide();
                GameBrightnessAssistantForm f = new GameBrightnessAssistantForm(_config,
                    delegate { _config.Save(_configPath); s.ReloadUi(); },
                    delegate { _gameBrightness.SetSuspended(false); if (!_disposed) { s.Show(); s.Activate(); } });
                if (autoCapture) f.Shown += delegate { f.BeginAutoCapture(); };
                f.Show();
            };
            s.ShowTacticalForTour = delegate { ShowTacticalPanel(); };
            s.HideTacticalForTour = delegate { HideTacticalPanel(); };
            s.ShowCalibrateForTour = delegate { ShowTimeCalibration(); };
            s.HideCalibrateForTour = delegate { HideTacticalPanel(); };
            s.HotkeySettingsChanged = delegate { ReRegisterHotkeys(); };
            s.HotkeyStatusProvider = delegate { return _hotkeys.StatusLine(); };
            s.OcrScanNow = delegate
            {
                try { _manager.ArmScpScan("手动全部扫描"); } catch { }
                _ocr.ScanOnDemand(true);
            };
            s.OcrScanSummary = delegate { return _ocr.LastScanSummary; };
            s.OcrScanReport = delegate { return _ocr.LastScanReport; };
            s.OcrShowReport = delegate { ShowScanResultPanel(); };
            s.CanMinimizeToTray = true;
            s.MinimizedToTray = delegate
            {
                Balloon("已最小化到托盘，程序继续在后台运行。\n双击托盘图标可以再打开设置窗口。");
            };
            s.OcrSettingsChanged = delegate { if (_ocr != null) _ocr.ApplyConfig(); };
        }

        private void UseSettingsForm(int style)
        {
            bool modern = style == 0;
            // 主界面换了：如果提示主题是「跟随主界面」，这里立刻把提示一起换掉；
            // 固定主题时这次调用只是无操作（SyncPromptTheme 会自己判断）。
            try { if (_manager != null) _manager.ApplyConfig(); } catch { }
            // 换界面时把另一套战术面板收掉：新界面只留 WPF 面板，旧界面只留原面板，
            // 不会出现"两套面板都能开、状态各显示一半"的情况。
            if (modern) CloseLegacyTacticalPanel();
            else CloseWpfTacticalPanel();
            if (modern)
            {
                if (_settings != null && !_settings.IsDisposed)
                {
                    try { _settings.PrepareExit(); _settings.Close(); _settings.Dispose(); } catch { }
                    _settings = null;
                }
                ShowVigilStatus();
                return;
            }
            if (_vigilStatus != null) _vigilStatus.Close();
            // 现在只会创建旧设置，这里不再区分 modern / 旧版
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.ShowSettings();
                return;
            }
            SettingsForm old = _settings;
            _settings = CreateSettingsForm(modern);
            WireSettings(_settings);
            _settings.ShowSettings();
            try
            {
                if (old != null && !old.IsDisposed)
                {
                    old.PrepareExit();
                    old.Close();
                    old.Dispose();
                }
            }
            catch { }
        }

        private void RestartApplication()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Application.StartupPath;
                if (IsElevated()) psi.Verb = "runas";
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                Logger.Err("启动", "重启程序失败", ex);
                MessageBox.Show("重启失败：\r\n" + ex.Message, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateTrayText()
        {
            if (_notifyIcon == null || _disposed) return;
            string s = StatusTextHolder;
            if (string.IsNullOrEmpty(s)) s = _manager.StatusText;
            if (s == null) s = "SCPSLAlert";
            if (s.Length > 63) s = s.Substring(0, 60) + "...";
            try { _notifyIcon.Text = s; }
            catch { }
        }

        private void Balloon(string text)
        {
            try { _notifyIcon.ShowBalloonTip(2000, "SCPSLAlert", text, ToolTipIcon.Info); }
            catch { }
        }

        private void ToggleEditMode()
        {
            SetEditMode(!_manager.EditMode);
        }

        private void BeginAdjustPosition()
        {
            SetEditMode(true);
        }

        private void SetEditMode(bool on)
        {
            _manager.SetEditMode(on);
            if (_heartPrompt != null) _heartPrompt.EditMode = on;
            _editItem.Checked = on;

            if (on)
            {
                if (_settings != null) _settings.Hide();
                ShowAdjustBar();
                Balloon("调整模式：拖动屏幕上的提示文字，或用操作条上的方向键微调，完成后点“完成调整”。");
            }
            else
            {
                CloseAdjustBar();
                Balloon("位置已保存，提示恢复鼠标穿透。");
                if (_adjustReturnToVigil)
                {
                    _adjustReturnToVigil = false;
                    if (!_disposed && _vigilStatus != null)
                    {
                        _vigilStatus.Show();
                        _vigilStatus.Activate();
                    }
                    else ShowSettings();
                }
                else ShowSettings();
            }
        }

        private void ShowAdjustBar()
        {
            CloseAdjustBar();
            try
            {
                _adjustBar = new AdjustBar(_manager, delegate { SetEditMode(false); }, _heartPrompt);
                _adjustBar.Show();
                _adjustBar.Activate();
            }
            catch
            {
                _adjustBar = null;
            }
        }

        /// <summary>通知开关面板：游戏里想临时关掉某一类提示时用。</summary>
        private void ToggleNotifyPanel()
        {
            if (_panel != null)
            {
                CloseNotifyPanel();
                return;
            }
            try
            {
                if (_settings != null && _settings.Visible) _settings.Hide();
                _panel = new NotifyPanel(_manager,
                    delegate { if (_settings != null) _settings.ReloadUi(); });
                _panel.Show();
                _panel.Activate();
            }
            catch
            {
                _panel = null;
            }
        }

        private void CloseNotifyPanel()
        {
            if (_panel == null) return;
            NotifyPanel p = _panel;
            _panel = null;
            try
            {
                p.AllowClose();
                p.Close();
                p.Dispose();
            }
            catch { }
            if (_settings != null) _settings.ReloadUi();
        }

        // ---- 悬浮面板（懒创建，复用同一份实例）----
        private ScanResultPanel _scanPanel;

        /// <summary>
        /// F2：**开 / 关**扫描结果面板。
        /// 第一次按 → 扫一次并弹出；再按一次 → 直接收起（不用去按 ESC）。
        /// </summary>
        private void ToggleScanResultPanel()
        {
            try
            {
                if (_scanPanel != null && !_scanPanel.IsDisposed
                    && _scanPanel.Visible && _scanPanel.Interactive)
                {
                    _scanPanel.HidePanel();
                    return;
                }
            }
            catch { }

            try { _ocr.ScanOnDemand(); } catch { }   // 打开时顺便扫一遍
            ShowScanResultPanel();
        }

        /// <summary>呼出"扫描结果"悬浮面板（样式和校准面板一致）。</summary>
        private void ShowScanResultPanel()
        {
            try
            {
                if (_scanPanel == null || _scanPanel.IsDisposed)
                    _scanPanel = new ScanResultPanel(delegate { return _ocr.LastScanReport; });
                _scanPanel.OpenPanel();
            }
            catch (Exception ex) { Logger.Err("扫描", "打开扫描结果面板失败", ex); }
        }
        private TacticalPanel _tacticalPanel;
        private TacticalMiniPanel _miniPanel;

        /// <summary>新版（A.R.C.T）界面的战术面板。只在 MainUiStyle = 0 时创建。</summary>
        private WpfTacticalWindow _wpfTactical;

        private bool NewUiActive
        {
            get { return _config != null && _config.UiStyle == 0; }
        }

        /// <summary>
        /// 数据变了 → 通知"当前那套战术面板"刷新。
        /// 新旧两套面板同时只存在一套：新界面用 WPF 面板，旧界面用原来的悬浮面板。
        /// </summary>
        private void RefreshTacticalFromModel()
        {
            try
            {
                if (NewUiActive)
                {
                    if (_wpfTactical != null) _wpfTactical.RefreshFromModel();
                    return;
                }
                if (_tacticalPanel != null && !_tacticalPanel.IsDisposed)
                    _tacticalPanel.RefreshFromModel();
            }
            catch (Exception ex) { Logger.Err("战术", "刷新战术面板失败", ex); }
        }

        private void SetTacticalTicketSummary(string text)
        {
            try
            {
                if (NewUiActive)
                {
                    if (_wpfTactical != null) _wpfTactical.SetTicketSummary(text);
                    return;
                }
                if (_tacticalPanel != null && !_tacticalPanel.IsDisposed)
                    _tacticalPanel.SetTicketSummary(text);
            }
            catch (Exception ex) { Logger.Err("战术", "刷新战术票数失败", ex); }
        }

        private void SetTacticalWaveInfo(string order, string predict)
        {
            try
            {
                if (NewUiActive)
                {
                    if (_wpfTactical != null) _wpfTactical.SetWaveInfo(order, predict);
                    return;
                }
                if (_tacticalPanel != null && !_tacticalPanel.IsDisposed)
                    _tacticalPanel.SetWaveInfo(order, predict);
            }
            catch (Exception ex) { Logger.Err("战术", "刷新战术波次失败", ex); }
        }

        /// <summary>SCP 扫描结果悬浮窗（懒创建，复用同一份实例）。</summary>
        private ScpScanToast _scpToast;

        private void ShowScpScanToast(string codes)
        {
            try
            {
                if (_scpToast == null || _scpToast.IsDisposed)
                    _scpToast = new ScpScanToast();
                _scpToast.ShowCodes(codes);
            }
            catch (Exception ex) { Logger.Err("扫描", "显示 SCP 扫描悬浮窗失败", ex); }
        }

        /// <summary>
        /// 呼出 / 收起 时间校准 —— 已经并进战术面板的「校准」页，不再有单独悬浮窗。
        /// </summary>
        private void ToggleTimeCalibration()
        {
            try
            {
                if (NewUiActive)
                {
                    WpfTacticalWindow np = EnsureWpfTactical();
                    if (np.IsOpenOrOpening && np.IsCalibrationTab) np.HidePanel();
                    else np.OpenCalibrationTab();
                    return;
                }
                TacticalPanel p = EnsureTacticalPanel();
                // 展开 / 收起动画期间按热键也要能反向（IsOpenOrOpening / IsClosing 都算"开着"）
                if (p.IsOpenOrOpening && p.IsCalibrationTab) p.HidePanel();
                else p.OpenCalibrationTab();
            }
            catch { }
        }

        /// <summary>直接打开战术面板的「校准」页（进服自动校准 / 教程都走这里）。</summary>
        private void ShowTimeCalibration()
        {
            try
            {
                if (NewUiActive) EnsureWpfTactical().OpenCalibrationTab();
                else EnsureTacticalPanel().OpenCalibrationTab();
            }
            catch { }
        }

        /// <summary>战术面板「校准」页提交的时间校准（统一在这里落到估算器 / AlertManager）。</summary>
        private void ApplyTimeCalibration(string kind, double seconds)
        {
            DateTime now = DateTime.Now;
            TimeEstimator est = _manager.Estimator;
            if (est == null) return;

            switch (kind)
            {
                case "round":
                    est.CalibrateRoundElapsed(now, seconds);
                    // 把回合标记成"已开始"，否则屏幕上方那一行还是不显示
                    _manager.NotifyRoundCalibrated();
                    break;
                case "support":
                    est.CalibrateSupportRemaining(now, seconds);
                    _manager.OnCalibrated();
                    break;
                case "decon":
                    est.CalibrateDeconRemaining(now, seconds);
                    _manager.OnCalibrated();
                    break;
                case "deadman":
                    _manager.CalibrateDeadManRemaining(seconds);
                    break;
            }
        }

        /// <summary>「校准」页右侧的"当前估算"文字。</summary>
        private string TimeCalibrationStateText()
        {
            TimeEstimator est = _manager.Estimator;
            if (est == null) return "";
            if (!est.Running)
                return "本局还没开始计时：填「当前回合已进行」再点校准就会开始。";

            string s = "回合已进行 " + TimeEstimator.Clock(est.RoundElapsed, false)
                     + (est.Calibrated ? "" : "（未校准，填一次就准）");
            s += "\r\n支援剩余 " + (est.SupportReady ? "已刷新" : TimeEstimator.Clock(est.SupportRemaining, true));
            s += "\r\n净化 " + (est.DeconDisabled && !est.DeconCountdownStarted ? "不计时（中途加入，等真实播报 / F9）"
                : !est.DeconAnnounced ? "还没开始（等净化播报 / F9）"
                : est.DeconStarted ? "已开始" : "剩余 " + TimeEstimator.Clock(est.DeconRemaining, true));
            s += "\r\n终焉协议 " + (_manager.DeadManActive
                ? "剩余 " + TimeEstimator.Clock(_manager.DeadManRemaining, true) : "没在走");
            s += "\r\n核弹 " + (est.WarheadRunning
                ? "剩余 " + TimeEstimator.Clock(est.WarheadRemaining, true) : "没在走");
            return s;
        }

        /// <summary>确保战术面板已创建（懒加载）。</summary>
        private TacticalPanel EnsureTacticalPanel()
        {
            if (_tacticalPanel == null || _tacticalPanel.IsDisposed)
            {
                _tacticalPanel = new TacticalPanel(_config, _manager.Tactical,
                    delegate { _config.Save(_configPath); });
                // 在面板里标记某个 SCP 阵亡时，真的弹一条阵亡提示
                _tacticalPanel.ScpKilled += delegate(string code)
                { _manager.ShowScpDiedByCode(code); };
                // 「校准」页：提交时间校准 / 读取当前估算
                _tacticalPanel.TimeCalibrate = ApplyTimeCalibration;
                _tacticalPanel.TimeCalibrateState = TimeCalibrationStateText;
                _tacticalPanel.StopSpecialCountdowns = _manager.StopSpecialCountdowns;
            }
            // 下方的小面板跟着一起建（它自己会跟随大面板显隐，设置里可关）
            if (_miniPanel == null || _miniPanel.IsDisposed)
            {
                _miniPanel = new TacticalMiniPanel(_config, _manager.Tactical);
                _miniPanel.Attach(_tacticalPanel);
            }
            return _tacticalPanel;
        }

        /// <summary>
        /// 确保新版（WPF）战术面板已创建。数据源和旧面板是同一个 TacticalLog，
        /// 落盘也是同一组 config 键，所以两套可以来回切。
        /// </summary>
        private WpfTacticalWindow EnsureWpfTactical()
        {
            if (_wpfTactical == null)
            {
                if (System.Windows.Application.Current == null)
                {
                    System.Windows.Application wpf = new System.Windows.Application();
                    wpf.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }
                _wpfTactical = new WpfTacticalWindow(_config, _manager.Tactical,
                    delegate { _config.Save(_configPath); },
                    delegate { return BuildVigilSnapshot(); },
                    delegate { RecreateTacticalPanel(); });
                _wpfTactical.ScpKilled += delegate(string code)
                { _manager.ShowScpDiedByCode(code); };
                _wpfTactical.TimeCalibrate = ApplyTimeCalibration;
                _wpfTactical.TimeCalibrateState = TimeCalibrationStateText;
                _wpfTactical.StopSpecialCountdowns = _manager.StopSpecialCountdowns;
                _wpfTactical.Closed += delegate { _wpfTactical = null; };
            }
            return _wpfTactical;
        }

        /// <summary>呼出 / 收起 悬浮式战术面板。</summary>
        private void ToggleTacticalPanel()
        {
            try
            {
                if (NewUiActive)
                {
                    WpfTacticalWindow np = EnsureWpfTactical();
                    if (np.IsOpenOrOpening) np.HidePanel();
                    else np.OpenOverviewPanel();
                    return;
                }
                TacticalPanel p = EnsureTacticalPanel();
                // 展开 / 收起动画期间按热键也要能反向：统一用 IsOpenOrOpening 判断
                if (p.IsOpenOrOpening) p.HidePanel();
                else p.OpenOverviewPanel();
            }
            catch (Exception ex) { Logger.Err("战术", "切换战术面板失败", ex); }
        }

        private void ShowTacticalPanel()
        {
            try
            {
                if (NewUiActive)
                {
                    EnsureWpfTactical().OpenPanel();
                    return;
                }
                EnsureTacticalPanel().OpenPanel();
            }
            catch (Exception ex) { Logger.Err("战术", "显示战术面板失败", ex); }
        }

        private void HideTacticalPanel()
        {
            try
            {
                if (NewUiActive)
                {
                    if (_wpfTactical != null) _wpfTactical.HidePanel();
                    return;
                }
                if (_tacticalPanel != null && _tacticalPanel.Visible) _tacticalPanel.HidePanel();
            }
            catch { }
        }

        /// <summary>托盘菜单 / 设置页都能起：先把设置窗口调出来，再走教程。</summary>
        private void ShowTour()
        {
            try
            {
                ShowSettings();
                if (_settings != null) _settings.StartTour();
            }
            catch { }
        }

        /// <summary>刷新校准：记录“刚刷了一波”，按上一波顺延大波 / 小波，刷新计时重新起算。</summary>
        private void MarkBigWave()
        {
            try
            {
                if (NewUiActive) EnsureWpfTactical().MarkBigWaveNow();
                else EnsureTacticalPanel().MarkBigWaveNow();
            }
            catch { }
        }

        /// <summary>回合计时：手动暂停 / 继续。</summary>
        private void ToggleRoundClock()
        {
            try { _manager.ToggleClockPaused(); } catch { }
        }

        private void ClosePanels()
        {
            CloseLegacyTacticalPanel();
            CloseWpfTacticalPanel();
            if (_scpToast != null)
            {
                ScpScanToast s = _scpToast;
                _scpToast = null;
                try { s.Hide(); s.Dispose(); } catch { }
            }
        }

        /// <summary>关掉旧版（WinForms）战术面板与它的小面板。</summary>
        private void CloseLegacyTacticalPanel()
        {
            if (_tacticalPanel != null)
            {
                TacticalPanel t = _tacticalPanel;
                _tacticalPanel = null;
                try { t.HidePanel(); t.Dispose(); } catch { }
            }
            if (_miniPanel != null)
            {
                TacticalMiniPanel m = _miniPanel;
                _miniPanel = null;
                try { m.Hide(); m.Dispose(); } catch { }
            }
        }

        /// <summary>关掉新版（WPF）战术面板。</summary>
        private void CloseWpfTacticalPanel()
        {
            if (_wpfTactical == null) return;
            WpfTacticalWindow t = _wpfTactical;
            _wpfTactical = null;
            try { t.HidePanel(); t.Close(); } catch { }
        }

        /// <summary>
        /// 透明度跨越 100% 时重建战术面板：100% 用不透明窗口（高刷），
        /// 低于 100% 用真正的透明窗口（能透出游戏）。
        /// </summary>
        private void RecreateTacticalPanel()
        {
            bool wasVisible = _wpfTactical != null && _wpfTactical.IsVisible;
            CloseWpfTacticalPanel();
            if (wasVisible) EnsureWpfTactical().OpenPanel();
        }

        private void CloseAdjustBar()
        {
            if (_adjustBar == null) return;
            AdjustBar bar = _adjustBar;
            _adjustBar = null;
            try
            {
                bar.AllowClose();
                bar.Close();
                bar.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// 重新注册全部热键（设置窗口里改了快捷键时调）。
        /// 注册完把结果报出来：冲突 / 被占用 / 没设置，都要让用户当场知道。
        /// </summary>
        private void ReRegisterHotkeys()
        {
            try
            {
                _hotkeys.RegisterAll(_config);
                LogHotkeyReport();

                string warn = "";
                if (_hotkeys.Duplicates.Count > 0)
                    warn = "快捷键冲突：" + string.Join("；", _hotkeys.Duplicates.ToArray());
                else if (_hotkeys.Failed.Count > 0)
                    warn = "这些快捷键被别的程序占用了：" + string.Join("、", _hotkeys.Failed.ToArray());
                else if (_hotkeys.Unset.Count > 0)
                    warn = "没有设置快捷键（不会生效）：" + string.Join("、", _hotkeys.Unset.ToArray());

                if (warn.Length > 0) Balloon(warn);
                else Balloon("快捷键已更新，共 " + _hotkeys.RegisteredCount + " 个，立刻生效。");
            }
            catch (Exception ex)
            {
                Logger.Err("热键", "重新注册热键失败", ex);
            }
        }

        /// <summary>把热键注册结果写进日志（启动时和每次改快捷键都会调）。</summary>
        private void LogHotkeyReport()
        {
            Logger.On("热键", "系统级注册 " + _hotkeys.RegCount + " 个（前台不是本程序也能用，"
                + "且按键会被本程序独占、不再传给游戏）；钩子兜底 " + _hotkeys.HookOnlyCount + " 个"
                + "；注册失败 " + _hotkeys.Failed.Count + " 个；键盘钩子"
                + (_hotkeys.UsingHook ? "已安装" : "未安装"));
            if (_hotkeys.HookOnlyCount > 0)
                Logger.On("热键", "只能靠钩子兜底的快捷键有 " + _hotkeys.HookOnlyCount
                    + " 个：前台是管理员程序时它们可能收不到（那是 Windows 的 UIPI 限制）");
            if (_hotkeys.Failed.Count > 0)
                Logger.On("热键", "注册失败清单：" + string.Join("、", _hotkeys.Failed.ToArray()));
            if (_hotkeys.Unset.Count > 0)
                Logger.On("热键", "没有设置、因此不会生效的快捷键："
                    + string.Join("、", _hotkeys.Unset.ToArray())
                    + "（到设置窗口「快捷键」页填上即可）");
            if (_hotkeys.Duplicates.Count > 0)
                Logger.On("热键", "快捷键冲突（只有前面那个生效）："
                    + string.Join("；", _hotkeys.Duplicates.ToArray()));
        }

        /// <summary>本进程是不是以管理员身份运行。</summary>
        private static bool IsElevated()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity id =
                       System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    System.Security.Principal.WindowsPrincipal p =
                        new System.Security.Principal.WindowsPrincipal(id);
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 以管理员身份重新启动自己。
        /// 游戏若是管理员运行，Windows 的 UIPI 会让非管理员进程**既收不到热键、
        /// 也截不到画面**（截图会全黑）—— 这时候只能两边都用管理员。
        /// </summary>
        private void RelaunchAsAdmin()
        {
            try
            {
                Logger.On("启动", "尝试以管理员身份重新启动");
                Logger.Flush(800);

                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Application.StartupPath;
                psi.Verb = "runas";        // 触发 UAC 提权
                System.Diagnostics.Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                Logger.Err("启动", "提权启动失败", ex);
                MessageBox.Show("以管理员身份启动失败（如果刚才是点了“否”，重来一次即可）：\r\n\r\n"
                    + ex.Message, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ReloadConfig()
        {
            try
            {
                Logger.On("配置", "重新加载 config.ini");
                Config fresh = Config.Load(_configPath);
                _config.CopyFrom(fresh);

                bool editing = _manager.EditMode;
                if (editing) _manager.SetEditMode(false);
                _manager.ApplyConfig();

                if (_ocr != null) _ocr.ApplyConfig();

                if (_settings != null) _settings.ReloadUi();
                Balloon("配置已重新加载。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("重新加载配置失败：\r\n" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string LogRoot()
        {
            if (!string.IsNullOrEmpty(_config.LogDirectory)) return _config.LogDirectory;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "SCP Secret Laboratory", "ServerLogs");
        }

        private void OpenPath(string path, bool isFolder)
        {
            try
            {
                if (isFolder ? !Directory.Exists(path) : !File.Exists(path))
                {
                    MessageBox.Show("找不到：\r\n" + path, "SCPSLAlert",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LaunchScpsl()
        {
            if (_config != null && !_config.ScanBeforeLaunch)
            {
                if (_vigilStatus != null) _vigilStatus.SetLaunchBusy(false);
                LaunchScpslCore();
                return;
            }

            if (_vigilStatus != null) _vigilStatus.SetLaunchBusy(true);
            LaunchGuard.ScanAsync(delegate(List<LaunchHit> hits)
            {
                Action done = delegate
                {
                    if (_vigilStatus != null) _vigilStatus.SetLaunchBusy(false);
                    if (hits == null || hits.Count == 0)
                    {
                        LaunchScpslCore();
                        return;
                    }
                    Logger.On("启动", "启动前扫描到 " + hits.Count + " 个可疑程序");
                    WpfLaunchGuardWindow window = new WpfLaunchGuardWindow(hits,
                        delegate(List<LaunchHit> kill)
                        {
                            if (kill != null && kill.Count > 0)
                            {
                                string failed = LaunchGuard.Kill(kill);
                                if (failed.Length > 0) Balloon("有进程结束失败：" + failed);
                                Logger.On("启动", "启动前结束可疑进程：" + kill.Count + " 个");
                            }
                            LaunchScpslCore();
                        },
                        delegate { });
                    window.Show();
                };
                try
                {
                    if (_crashAnchor != null && _crashAnchor.InvokeRequired) _crashAnchor.BeginInvoke(done);
                    else done();
                }
                catch { }
            });
        }

        /// <summary>
        /// 第 5 批：游戏启动时静默跑一次"已有的"可疑进程扫描。
        /// 复用 GameWatcher.Started + LaunchGuard，不新增轮询、不改扫描规则/展示/日志。
        /// 同一次启动只跑一次；扫描在后台线程，不阻塞主线程。
        /// </summary>
        private bool _gameStartScanBusy;

        // ---------------- 第 6 批：截屏采集 ----------------

        private void ShowCaptureNotice(string text, bool light)
        {
            try { if (_captureHud != null) _captureHud.ShowNotice(text, light); } catch { }
        }

        private void ShowCaptureProgress(int current, int total)
        {
            try { if (_captureHud != null) _captureHud.ShowProgress("当前已采集 " + current + "/" + total + " 张图片", current, total); } catch { }
        }

        /// <summary>开始（空闲时）或继续（暂停时）。</summary>
        private void CaptureStart()
        {
            if (_capture == null) return;
            if (_capture.State == CaptureState.Idle) _capture.Start();
            else _capture.Resume();
        }

        private void CapturePause()
        {
            if (_capture == null) return;
            if (_capture.State == CaptureState.Capturing) _capture.Pause("已暂停采集", true);
            else _capture.Resume();
        }

        private void CaptureStop()
        {
            if (_capture == null) return;
            _capture.Stop();
        }

        private string CaptureDelete()
        {
            if (_capture == null) return "采集模块未初始化";
            string r = _capture.DeleteAll();
            Logger.On("采集", r);
            return r;
        }

        private void StartCaptureByHotkey()
        {
            CaptureStart();
        }

        private void ToggleCapturePauseByHotkey()
        {
            CapturePause();
        }

        private void ScanOnGameStart()
        {
            if (_config == null || !_config.ScanOnGameStart) return;
            if (_gameStartScanBusy) return;
            _gameStartScanBusy = true;
            try
            {
                LaunchGuard.ScanAsync(delegate(List<LaunchHit> hits)
                {
                    Action done = delegate
                    {
                        _gameStartScanBusy = false;
                        if (hits == null || hits.Count == 0) return;
                        Logger.On("启动", "游戏启动时扫描到 " + hits.Count + " 个可疑程序");
                        WpfLaunchGuardWindow window = new WpfLaunchGuardWindow(hits,
                            delegate(List<LaunchHit> kill)
                            {
                                if (kill != null && kill.Count > 0)
                                {
                                    string failed = LaunchGuard.Kill(kill);
                                    if (failed.Length > 0) Balloon("有进程结束失败：" + failed);
                                    Logger.On("启动", "游戏启动时结束可疑进程：" + kill.Count + " 个");
                                }
                            },
                            delegate { });
                        window.Show();
                    };
                    try
                    {
                        if (_crashAnchor != null && _crashAnchor.InvokeRequired) _crashAnchor.BeginInvoke(done);
                        else done();
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                _gameStartScanBusy = false;
                Logger.Err("启动", "游戏启动扫描失败", ex);
            }
        }

        private void LaunchScpslCore()
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/700330")
                {
                    UseShellExecute = true
                });
                Balloon("已通过 Steam 启动 SCP:SL。");
                Logger.On("启动", "已调用 Steam 启动协议 steam://rungameid/700330");
            }
            catch (Exception ex)
            {
                Logger.Err("启动", "启动 SCP:SL 失败", ex);
                MessageBox.Show("启动 SCP:SL 失败：\r\n" + ex.Message
                    + "\r\n\r\n请确认已安装 Steam 并且 steam:// 协议可用。",
                    "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExitApp()
        {
            Logger.On("退出", "用户退出程序");
            Logger.Flush(1200);
            GameCrashWatcher.Stop();
            AppCrashGuard.EndRun();
            try { if (_capture != null) _capture.Dispose(); } catch { }
            try { if (_captureHud != null) { _captureHud.HideNow(); _captureHud.Dispose(); } } catch { }
            try { if (_settings != null) _settings.SaveNow(); } catch { }
            try { _manager.SaveConfig(); } catch { }

            // 退出时唯一的清理：先把图标从托盘撤掉，避免留下幽灵图标，
            // 然后让 Application 正常结束（不要用 ExitThread，它会绕过窗体关闭流程）。
            RemoveTrayIcon();
            try { if (_settings != null) _settings.PrepareExit(); } catch { }
            try { if (_vigilStatus != null) _vigilStatus.Close(); } catch { }
            Application.Exit();
        }

        /// <summary>游戏退出时的"确认中"计时器（null = 当前没在等确认）。</summary>
        private System.Windows.Forms.Timer _exitConfirm;

        /// <summary>
        /// 游戏退了 → 等"SCP:SL 已退出"那条提示放完，再看一眼确实没起来，然后跟着退。
        ///
        /// 【为什么不是立刻退】游戏退出本来就有一条提示（GameLaunch 的 AltText，
        /// 显示时长就是「事件提示显示时长」，默认 5 秒）。立刻退会把那条提示切掉，
        /// 变成"程序莫名其妙没了"。等它放完再退，顺便还把"进程列表抖动"
        /// 挡在外面（这段时间里会再确认一次游戏是不是真的没在跑）。
        /// 走的是和托盘菜单里"退出"完全一样的收尾（ExitApp），
        /// 所以配置照样会存盘、图标照样会好好撤掉。
        /// </summary>
        private void ConfirmGameExitAndQuit()
        {
            if (_exitConfirm != null) return;    // 已经在等确认了，别叠第二个

            int waitSec = (_config == null ? 5 : _config.DisplaySeconds) + 1;
            if (waitSec < 3) waitSec = 3;
            if (waitSec > 20) waitSec = 20;      // 别为了等一条提示卡太久

            _exitConfirm = new System.Windows.Forms.Timer();
            _exitConfirm.Interval = waitSec * 1000;
            _exitConfirm.Tick += delegate
            {
                _exitConfirm.Stop();
                _exitConfirm.Dispose();
                _exitConfirm = null;

                if (_manager.GameRunning) return;    // 又起来了（或刚才是抖动）→ 不退

                Logger.On("退出", "检测到 SCP:SL 已退出（" + waitSec + " 秒后确认仍不在）→ 本程序一起退出");
                ExitApp();
            };
            _exitConfirm.Start();
        }

        /// <summary>
        /// 把托盘图标撤下来。这是唯一允许碰 NotifyIcon 生命周期的地方。
        /// 位图和 Icon 本身仍然不 Dispose —— 交给进程退出时由系统回收。
        /// </summary>
        private static void RemoveTrayIcon()
        {
            if (_notifyIcon == null) return;
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }
            _notifyIcon = null;
            _trayIconAdded = false;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                if (_showReg != null)
                {
                    try { _showReg.Unregister(null); } catch { }
                    _showReg = null;
                }
                CloseAdjustBar();
                CloseNotifyPanel();
                ClosePanels();
                if (_trayRetry != null) { try { _trayRetry.Stop(); _trayRetry.Dispose(); } catch { } }
                if (_hold != null) _hold.Stop();
                if (_showEvent != null)
                {
                    try { _showEvent.Close(); } catch { }
                }
                RemoveTrayIcon();
                // 刻意不 Dispose _trayIcon / _trayBitmap：
                // 进程马上就结束了，销毁它们反而可能在退出瞬间留下幽灵图标。
                if (_settings != null)
                {
                    try { _settings.CloseForExit(); } catch { }
                    _settings.Dispose();
                }
                if (_modernView != null) { try { _modernView.Dispose(); } catch { } }
                if (_modernHost != null) { try { _modernHost.Dispose(); } catch { } }
                if (_gameBrightness != null) _gameBrightness.Dispose();
                if (_crosshair != null) _crosshair.Dispose();
                if (_hotkeys != null) _hotkeys.Dispose();
                if (_hold != null) _hold.Dispose();
                if (_respawnWatcher != null) _respawnWatcher.Dispose();
                if (_ocr != null) _ocr.Dispose();
                if (_heartPrompt != null) { try { _heartPrompt.Shutdown(); } catch { } _heartPrompt = null; }
                if (_manager != null) _manager.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}














