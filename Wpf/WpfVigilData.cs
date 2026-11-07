using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace SCPSLAlert
{
    internal sealed partial class WpfVigilDataWindow : Window
    {
        private static readonly FontFamily Ui = UiFonts.Body;
        private static readonly FontFamily Display = UiFonts.Title;
        private static readonly FontFamily Mono = UiFonts.Mono;
        private static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private readonly Config _cfg;
        private bool _light;
        private readonly SystemTelemetry _tm;
        private readonly DispatcherTimer _poll;
        private readonly Dictionary<string, TextBlock> _tagged = new Dictionary<string, TextBlock>();
        private StackBar _memBar;
        private HistoryChart _history;
        private readonly Action _launch;
        private readonly Action _exit;
        private readonly Action _stop;
        private readonly Func<VigilSnapshot> _snapshot;
        private readonly Action _openSettings;
        private readonly Action _save;
        private readonly Action _calibrate;
        private readonly Action _hotkeysChanged;
        private readonly Action _ocrScan;
        private readonly Action _previewAlerts;
        private readonly Action _ocrReport;
        private readonly Action _restart;
        private readonly Action<int> _openLegacyPage;
        private readonly Action _switchLegacy;
        private readonly Action _ocrSettingsChanged;
        private readonly Action _previewOcr;
        private readonly Action _pickOcrRegion;
        private readonly Action _editOcrRegions;
        private readonly Action _ocrHelp;
        private readonly Action<bool> _openBrightness;
        private readonly Action _adjustPosition;
        private readonly Action<double> _calibrateDeadMan;
        private readonly Action _scanLogTimes;
        private readonly Action _editTactical;
        private readonly Action _applyTactical;
        private readonly Action _factionScan;
        private readonly Action _captureStart;
        private readonly Action _capturePause;
        private readonly Action _captureStop;
        private readonly Func<string> _captureDelete;
        private readonly Action _oneMap;
        private ClickFxLayer _clickFx;
        private TextBlock _tacticalData;
        // 左侧导航与 SelectPage 都用 0 基下标：0 状态、1 常规、2 提示、3 触发点、
        // 4 时间、5 热键、6 日志、7 工具、8 高级、9 外观。
        // 视觉顺序用 PageOrder 映射，这样插入“外观”不用重排日志/工具/高级的下标。
        private const int AdvancedPageIndex = 8;
        private const int AppearancePageIndex = 9;
        private static readonly int[] PageOrder = { 0, 1, 2, 3, 4, 5, 9, 6, 7, 8 };
        private ScrollViewer _appearancePage;
        private ScrollViewer _appearanceDetailPage;
        private ScrollViewer _cardDesignPage;
        private StackPanel _cardDesignBody;
        private bool _cardDesignOpen;
        private StackPanel _appearanceDetailBody;
        private bool _editLightPalette;
        private bool _appearanceDetailOpen;
        private ScrollViewer _advDetailPage;
        private StackPanel _advDetailBody;
        private ScrollViewer _advancedPage;
        private FrameworkElement _advOcrCard;
        private FrameworkElement _advTimeCard;
        private FrameworkElement _advPositionCard;
        private FrameworkElement _advBrightnessCard;
        private FrameworkElement _advCrosshairCard;
        private FrameworkElement _advAudioCard;
        private TextBlock _deadmanCalStatus;
        private TextBlock _advAudioStatus;
        private Grid _logTimeLayer;
        private StackPanel _logTimeList;
        private TextBlock _logTimeStatus;
        private bool _logTimeBusy;
        private Grid _statusPage;
        private Grid _body;
        private Grid _searchLayer;
        private TextBox _searchBox;
        private StackPanel _searchResults;
        private TextBlock _searchStatus;
        private bool _searchIndexBuilt;
        private readonly List<SettingHit> _searchIndex = new List<SettingHit>();
        private FrameworkElement _searchHitRow;
        private Brush _searchHitOldBg;
        private DispatcherTimer _searchHitTimer;

        private sealed class SettingMeta
        {
            public string Title;
            public string Desc;
        }

        private sealed class SettingHit
        {
            public string Title;
            public string Desc;
            public string PageName;
            public int Page;
            public FrameworkElement Row;
            public bool Heart;
        }
        private Grid _logChoiceLayer;
        private TextBlock _logChoiceStatus;
        private readonly List<TextBlock> _logChoiceSizes = new List<TextBlock>();
        private readonly List<string> _logChoicePaths = new List<string>();
        private ScrollViewer _perfPage;
        private bool _perfBuilt;
        private HistoryChart _perfChart;
        private FrameChart _frameChart;
        private readonly ResourceHistory _historyData = new ResourceHistory(60);
        private readonly List<double> _gameFpsHistory = new List<double>(64);
        private readonly GameFpsMonitor _gameFps = new GameFpsMonitor();
        private double _gameFpsAvg;
        private double _gameFpsLow;
        private double _gameFrameMs;
        private double _gameJitterMs;
        private string _gameFpsStatus = "未启动";
        private MatchRecord _match;
        private MatchEvent _deadmanEvent;
        private MatchEvent _warheadEvent;
        private DateTime _lastDeadmanEnd = DateTime.MinValue;
        private DateTime _lastMiniWaveAt = DateTime.MinValue;
        private bool _lastDeadmanActive;
        private bool _lastWarheadRunning;
        private string _lastMatchRole = "";
        private string _lastMatchState = "";
        private DateTime _matchRoundStart = DateTime.MinValue;
        private GamePerfSession _livePerf;
        private GamePerfSession _lastPerf;
        private DateTime _lastPerfSampleAt = DateTime.MinValue;
        private string _lastPerfSavedPath;
        private StackPanel _perfSaveRow;
        private TextBlock _perfSaveText;
        private Button _perfSaveButton;
        private bool _lastSelfDead;
        private bool _lastWarhead;
        private bool _lastDeadman;
        private DateTime _lastWaveAt = DateTime.MinValue;
        private GamePerfEvent _openWarheadEvent;
        private GamePerfEvent _openDeadmanEvent;
        private StackPanel _perfRecordsHost;
        private readonly List<Border> _insightDots = new List<Border>();
        private readonly List<TextBlock> _insightTexts = new List<TextBlock>();
        private readonly List<MeterBar> _marginBars = new List<MeterBar>();
        private readonly Dictionary<string, List<FrameworkElement>> _legendChips = new Dictionary<string, List<FrameworkElement>>();
        private StackPanel _tempLegend;
        private StackPanel _perfTempLegend;
        private UIElement _perfTempRow;
        private bool _showCpu = true;
        private bool _showMem = true;
        private bool _showGpu = true;
        private bool _showNet = true;
        private bool _showDisk = true;
        private bool _showTemp = true;
        private ScrollViewer _generalPage;
        private ScrollViewer _logPage;
        private ScrollViewer _alertPage;
        private ScrollViewer _audioPage;
        private FrameworkElement _audioDropTarget;
        private TextBlock _audioStatus;
        private ScrollViewer _timePage;
        private ScrollViewer _hotkeyPage;
        private ScrollViewer _toolsPage;
        private Action<string> _recordHotkey;
        private TextBlock _recordHotkeyText;
        private string _recordHotkeyOldText;
        private Border _recordHotkeyBorder;
        private TextBlock _timeRoleText;
        private TextBlock _timeOffsetText;
        private TextBlock _timeDeconText;
        private TextBlock _timeSupportText;
        private StackPanel _audioTriggerList;
        private string _audioDragEvent;
        private int _alertIndex;
        private StackPanel _alertDetail;
        private Border _alertPreviewBox;
        private TextBlock _alertPreviewText;
        private WpfPromptCard _alertPreviewNew;
        private WpfHeartCard _heartPreview;
        private readonly List<Border> _alertRows = new List<Border>();
        private readonly List<TextBlock> _alertDots = new List<TextBlock>();
        private TextBlock _logTail;
        private TextBlock _logStats;
        private TextBlock _backupStats;
        private StackPanel _backupList;
        private TextBlock _gameCrashStats;
        private VigilToggle _gameCrashToggle;
        private bool _dumpWarning;
        private Grid _crashListLayer;
        private readonly List<Border> _railItems = new List<Border>();
        private readonly List<TextBlock> _railIcons = new List<TextBlock>();
        private readonly List<Border> _railBars = new List<Border>();
        private readonly List<TextBlock> _railLabels = new List<TextBlock>();
        private readonly List<StackPanel> _railLabelGroups = new List<StackPanel>();
        private Border _rail;
        private bool _railExpanded;
        private readonly DispatcherTimer _saveTimer;
        private int _currentPage;
        private Canvas _hoverLayer;
        private HoverOutline _hoverFrame;
        private Border _flyBar;
        private Border _flyTrail;
        private readonly List<FrameworkElement> _hoverTargets = new List<FrameworkElement>();
        private FrameworkElement _hoverCurrent;
        private Rect _hoverRect;
        private bool _hoverSyncing;
        private bool _hoverAnimating;
        private DispatcherTimer _hoverSettle;
        private DispatcherTimer _hoverLeave;
        // 从“高级设置”提示条跳转后，描边先锁在目标卡片上 5 秒，
        // 让用户一眼看到“跳到的是哪一块”；时间到了再交还给鼠标跟随。
        private DispatcherTimer _hoverLockTimer;
        private FrameworkElement _hoverLockTarget;
        private Border _gameActionBorder;
        private TextBlock _gameActionIcon;
        private TextBlock _gameActionText;
        private SolidColorBrush _gameActionFill;
        private SolidColorBrush _gameActionFore;
        private Color _gameActionBase;
        private Color _gameActionFgColor;
        private bool _gameActionHover;
        private bool _gameActionPress;
        private bool _launchBusy;
        private DateTime _legacySwitchAt;
        private OrbitLayer _orbit;
        private MediaElement _bgVideo;
        private string _bgVideoPath = "";
        private System.Windows.Media.MediaPlayer _bgMusic;
        private string _bgMusicPath = "";
        private bool _entrancePlayed;
        private DispatcherTimer _revealPoll;

        private FrameworkElement _advHeartCard;
        private TextBlock _heartStatusText;
        private TextBlock _heartBpmText;
        private TextBlock _heartPreferredText;
        private StackPanel _heartDeviceList;
        private FrameworkElement _bleChip;
        private TextBlock _gameBpmText;
        private bool _heartSupported;
        private bool _heartProbeStarted;
        private BleHeartRateSource _heartSource;
        private VigilToggle _heartToggle;
        private DispatcherTimer _heartSafetyTimer;
        private int _heartNoConnectSeconds;
        private bool _heartAutoOff;
        private DispatcherTimer _heartPreviewPulse;
        private DateTime _heartPreviewLastTick = DateTime.Now;
        private const int HeartRateNoDeviceSeconds = 120;
        private readonly List<MiniEcg> _ecgControls = new List<MiniEcg>();
        private readonly Dictionary<ulong, BleHeartDevice> _heartDevices = new Dictionary<ulong, BleHeartDevice>();

        private Brush Bg;
        private Brush Panel;
        private Brush PanelAlt;
        private Brush Line;
        private Brush Ink;
        private Brush Sub;
        private Brush Muted;
        private Brush Accent;
        private Brush Accent2;
        private Brush Danger;
        private Brush SystemBar;
        private Brush ToolBar;
        private Brush GpuBrush;
        private Brush NetBrush;
        private Brush DiskBrush;

        public WpfVigilDataWindow(Config cfg, bool light, Action launch = null, Action exit = null, Action stop = null, Func<VigilSnapshot> snapshot = null, Action openSettings = null, Action save = null, Action calibrate = null, Action hotkeysChanged = null, Action ocrScan = null, Action previewAlerts = null, Action ocrReport = null, Action restart = null, Action<int> openLegacyPage = null, Action ocrSettingsChanged = null, Action previewOcr = null, Action pickOcrRegion = null, Action editOcrRegions = null, Action ocrHelp = null, Action<bool> openBrightness = null, Action adjustPosition = null, Action<double> calibrateDeadMan = null, Action scanLogTimes = null, Action editTactical = null, Action applyTactical = null, Action switchLegacy = null, Action factionScan = null, Action captureStart = null, Action capturePause = null, Action captureStop = null, Func<string> captureDelete = null, Action oneMap = null)
        {
            WpfScroll.Apply(this, light);
            _cfg = cfg;
            _cfg.UiThemeMode = Math.Max(-1, Math.Min(1, _cfg.UiThemeMode));
            ModernTheme.SetMode(_cfg.UiThemeMode);
            _light = _cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : _cfg.UiThemeMode == 0;
            _launch = launch;
            _exit = exit;
            _stop = stop;
            _snapshot = snapshot;
            _openSettings = openSettings;
            _save = save;
            _calibrate = calibrate;
            _hotkeysChanged = hotkeysChanged;
            _ocrScan = ocrScan;
            _previewAlerts = previewAlerts;
            _ocrReport = ocrReport;
            _restart = restart;
            _openLegacyPage = openLegacyPage;
            _switchLegacy = switchLegacy;
            _oneMap = oneMap;
            _ocrSettingsChanged = ocrSettingsChanged;
            _previewOcr = previewOcr;
            _pickOcrRegion = pickOcrRegion;
            _editOcrRegions = editOcrRegions;
            _ocrHelp = ocrHelp;
            _openBrightness = openBrightness;
            _adjustPosition = adjustPosition;
            _calibrateDeadMan = calibrateDeadMan;
            _scanLogTimes = scanLogTimes;
            _editTactical = editTactical;
            _applyTactical = applyTactical;
            _factionScan = factionScan;
            _captureStart = captureStart;
            _capturePause = capturePause;
            _captureStop = captureStop;
            _captureDelete = captureDelete;
            _tm = new SystemTelemetry();
            ModernTheme.Changed += OnAppThemeChanged;
            AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnClickFxDown), true);
            AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnClickFxUp), true);
            AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnClickFxMove), true);
            LoadSeriesSelection();
            BuildPalette();
            Title = "SCPSLAlert // A.R.C.T";
            // 高 DPI：窗口尺寸按当前工作区的 DIP 收敛，避免 150/200% 下
            // 1440×900 DIP 比屏幕还大；最小尺寸也跟着工作区走。
            double waW = 1440, waH = 900;
            try
            {
                waW = SystemParameters.WorkArea.Width;
                waH = SystemParameters.WorkArea.Height;
            }
            catch { }
            Width = Math.Min(1440, Math.Max(640, waW - 24));
            Height = Math.Min(900, Math.Max(480, waH - 24));
            MinWidth = Math.Min(1180, Width);
            MinHeight = Math.Min(760, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = System.Windows.WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = Bg;
            Foreground = Ink;
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(cfg.UiFontFamily)
                ? "Microsoft YaHei UI" : cfg.UiFontFamily);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);
            SourceInitialized += delegate { ApplyGlassNow(); };
            Loaded += delegate { ApplyGlassNow(); };
            Build();

            // 启动画面还在跑时，主窗口先不 Show()：不是透明，而是根本不显示。
            // 之前用 Opacity=0 时，深色窗口在部分机器上仍会露出一大块黑色。
            if (StartupSplash.IsAlive) StartRevealPoll();

            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnVigilPreviewMouseDown), true);
            CollectTags(this);
            ApplySeriesVisibility();
            UpdateTemperatureVisibility();
            for (int i = 0; i < 1; i++)
                _historyData.Add(_tm.CpuPercent, _tm.MemoryPercent, _tm.GpuPercent,
                    _tm.NetworkMbps, _tm.DiskUsedPercent, _tm.TemperatureCelsius);
            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveTimer.Tick += delegate { _saveTimer.Stop(); if (_save != null) _save(); };
            _poll = new DispatcherTimer();
            _poll.Interval = TimeSpan.FromSeconds(1);
            _poll.Tick += delegate
            {
                _tm.Refresh(1000);
                if (_currentPage == 6) UpdateLogTail();
                _historyData.Add(_tm.CpuPercent, _tm.MemoryPercent, _tm.GpuPercent,
                    _tm.NetworkMbps, _tm.DiskUsedPercent, _tm.TemperatureCelsius);
                if (_history != null)
                {
                    _history.DiskLabel = _tm.DiskLabel;
                    _history.InvalidateVisual();
                }
                if (_perfChart != null)
                {
                    _perfChart.DiskLabel = _tm.DiskLabel;
                    _perfChart.InvalidateVisual();
                }
                _gameFps.SetGameRunning(_tm.GameRunning, _tm.GamePid);
                GameFpsStats gf = _gameFps.Snapshot();
                _gameFpsAvg = gf.AverageFps;
                _gameFpsLow = gf.LowFps;
                _gameFrameMs = gf.FrameMs;
                _gameJitterMs = gf.JitterMs;
                _gameFpsStatus = !_tm.GameRunning ? "等待游戏启动" : gf.Status;
                SyncGameFpsHistory();
                VigilSnapshot perfSnap = _snapshot != null ? _snapshot() : null;
                TrackGamePerformance(gf, perfSnap);
                UpdateGamePerfNotice();
                if (_frameChart != null) _frameChart.InvalidateVisual();
                UpdateTelemetry();
                UpdatePerformance();
            };
            _poll.Start();
            _gameFps.StartWaitingForGame();

            // 设置页里的心率预览也跟着 BPM 跳动（没有真实数据时用 72 示例）。
            _heartPreviewPulse = new DispatcherTimer();
            _heartPreviewPulse.Interval = TimeSpan.FromMilliseconds(40);
            _heartPreviewPulse.Tick += delegate
            {
                if (_heartPreview == null || _alertIndex != _cfg.Alerts.Length) return;
                DateTime now = DateTime.Now;
                double dt = (now - _heartPreviewLastTick).TotalSeconds;
                _heartPreviewLastTick = now;
                if (dt <= 0) dt = 0.04;
                if (dt > 0.15) dt = 0.04;
                int bpm = HeartRateHub.Bpm;
                if (bpm <= 0) bpm = 72;
                _heartPreview.Advance(dt, bpm);
            };
            _heartPreviewPulse.Start();

            HeartMonitor.Died += OnHeartDied;
            HeartRateHub.BleConnectionChanged += OnBleConnectionChanged;
            HeartRateHub.BpmChanged += OnBpmChanged;
            StartHeartProbe();
            Closed += delegate
            {
                ModernTheme.Changed -= OnAppThemeChanged;
                HeartMonitor.Died -= OnHeartDied;
                HeartRateHub.BleConnectionChanged -= OnBleConnectionChanged;
                HeartRateHub.BpmChanged -= OnBpmChanged;
                StopHeartSafety();
                try { _gameFps.Dispose(); } catch { }
                if (_heartPreviewPulse != null) _heartPreviewPulse.Stop();
                if (_heartSource != null) { try { _heartSource.Dispose(); } catch { } _heartSource = null; }
                HeartAlarm.Stop();
                _poll.Stop();
                if (_hoverLockTimer != null) _hoverLockTimer.Stop();
                if (_hoverLeave != null) _hoverLeave.Stop();
                if (_hoverSettle != null) _hoverSettle.Stop();
            };
            UpdateTelemetry();
            UpdateGamePerfNotice();
        }

        private void StartHeartProbe()
        {
            if (_heartProbeStarted) return;
            _heartProbeStarted = true;
            try
            {
                BleHeartRateSource.Probe(delegate(bool ok)
                {
                    if (!Dispatcher.CheckAccess())
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background,
                            new Action(delegate { ApplyHeartSupport(ok); }));
                    }
                    else
                    {
                        ApplyHeartSupport(ok);
                    }
                });
            }
            catch
            {
                ApplyHeartSupport(false);
            }
        }

        private void ApplyHeartSupport(bool supported)
        {
            _heartSupported = supported;
            if (_advHeartCard != null)
                _advHeartCard.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
            if (supported && _cfg.HeartRateBleEnabled) TryStartHeartSource();
        }

        private void TryStartHeartSource()
        {
            if (!_heartSupported || !_cfg.HeartRateBleEnabled) return;
            EnsureHeartSource();
            try { _heartSource.Start(); } catch { }
            StartHeartSafety();
        }

        private void StartHeartSafety()
        {
            if (_heartSafetyTimer == null)
            {
                _heartSafetyTimer = new DispatcherTimer();
                _heartSafetyTimer.Interval = TimeSpan.FromSeconds(1);
                _heartSafetyTimer.Tick += delegate { HeartSafetyTick(); };
            }
            _heartNoConnectSeconds = 0;
            if (!_heartSafetyTimer.IsEnabled) _heartSafetyTimer.Start();
        }

        private void StopHeartSafety()
        {
            if (_heartSafetyTimer != null) _heartSafetyTimer.Stop();
            _heartNoConnectSeconds = 0;
        }

        private void HeartSafetyTick()
        {
            if (!_cfg.HeartRateBleEnabled)
            {
                StopHeartSafety();
                return;
            }

            if (HeartRateHub.BleConnected)
            {
                _heartNoConnectSeconds = 0;
                return;
            }

            _heartNoConnectSeconds++;
            if (_heartNoConnectSeconds < HeartRateNoDeviceSeconds) return;

            // 安全：开关一直开着却长时间连不上设备，自动关掉，避免后台一直扫描。
            StopHeartSafety();
            _cfg.HeartRateBleEnabled = false;
            TouchSave();
            if (_heartSource != null)
            {
                try { _heartSource.Stop(); } catch { }
            }
            _heartAutoOff = true;
            if (_heartToggle != null) _heartToggle.IsChecked = false;
            _heartAutoOff = false;
            if (_heartStatusText != null)
                _heartStatusText.Text = HeartRateNoDeviceSeconds + " 秒没有连接设备，已自动关闭蓝牙心率";
        }

        private void EnsureHeartSource()
        {
            if (_heartSource != null) return;
            ulong address = 0;
            ulong.TryParse(_cfg.HeartRateBleAddress, out address);
            _heartSource = new BleHeartRateSource(Dispatcher, address, _cfg.HeartRateBleName);
            _heartSource.DeviceFound += OnHeartDeviceFound;
            _heartSource.StatusChanged += OnHeartStatusChanged;
            _heartSource.Sample += OnHeartSample;
        }

        private void OnHeartDied(bool hadRealBpm)
        {
            if (!_cfg.HeartRateAlarmEnabled) return;
            try { HeartAlarm.Play(_cfg.HeartRateAlarmVolume); } catch { }
        }

        private void OnBleConnectionChanged(bool connected)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { OnBleConnectionChanged(connected); }));
                return;
            }
            if (_bleChip != null)
                _bleChip.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (connected)
            {
                _heartNoConnectSeconds = 0;
                string actual = HeartRateHub.BleDeviceName;
                if (!string.IsNullOrEmpty(actual) && actual != "心率设备"
                    && _cfg.HeartRateBleName != actual)
                {
                    // 广播里有时只有“心率设备”这种占位名；连上后设备真名才出来，
                    // 这里把“我的设备”同步成真名。
                    _cfg.HeartRateBleName = actual;
                    TouchSave();
                    UpdateHeartPreferredText();
                }
            }
            UpdateHeartStatusText();
            RebuildHeartDeviceList();
        }

        private void OnBpmChanged(int bpm)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { OnBpmChanged(bpm); }));
                return;
            }
            if (_heartBpmText != null) _heartBpmText.Text = bpm > 0 ? bpm + " BPM" : "-- BPM";
            if (_gameBpmText != null && HeartRateHub.BleConnected)
                _gameBpmText.Text = bpm > 0 ? bpm + " BPM" : "-- BPM";
        }

        private void OnHeartDeviceFound(BleHeartDevice device)
        {
            if (device == null) return;
            _heartDevices[device.Address] = device;
            RebuildHeartDeviceList();
        }

        private void OnHeartStatusChanged(string status)
        {
            UpdateHeartStatusText();
        }

        private void OnHeartSample(HeartRateSample sample)
        {
            if (_heartBpmText != null && sample != null)
                _heartBpmText.Text = sample.Bpm + " BPM";
        }

        private void UpdateHeartStatusText()
        {
            if (_heartStatusText == null) return;
            if (!_heartSupported)
            {
                _heartStatusText.Text = "蓝牙不可用";
                return;
            }
            if (!_cfg.HeartRateBleEnabled)
            {
                _heartStatusText.Text = "已关闭";
                return;
            }
            if (HeartRateHub.BleConnected)
            {
                _heartStatusText.Text = "已连接 " + HeartRateHub.BleDeviceName;
            }
            else if (_heartSource != null)
            {
                _heartStatusText.Text = _heartSource.Status;
                if (string.IsNullOrEmpty(_heartStatusText.Text)) _heartStatusText.Text = "正在搜索心率设备…";
            }
            else
            {
                _heartStatusText.Text = "正在搜索心率设备…";
            }
        }

        private void RefreshBleChip()
        {
            if (_bleChip == null) return;
            _bleChip.Visibility = HeartRateHub.BleConnected ? Visibility.Visible : Visibility.Collapsed;
        }


        /// <summary>
        /// 等启动画面把进度条跑到 100% 再显示主窗口。
        /// </summary>
        private void StartRevealPoll()
        {
            if (_revealPoll != null) return;
            _revealPoll = new DispatcherTimer(DispatcherPriority.Render);
            _revealPoll.Interval = TimeSpan.FromMilliseconds(50);
            _revealPoll.Tick += delegate
            {
                if (StartupSplash.IsRevealing || !StartupSplash.IsAlive)
                {
                    _revealPoll.Stop();
                    _revealPoll = null;
                    PlayEntrance();
                }
            };
            _revealPoll.Start();
        }

        public void PlayEntrance()
        {
            if (_entrancePlayed) return;
            _entrancePlayed = true;
            Opacity = 1;
            IsHitTestVisible = true;

            if (!IsVisible) Show();
            if (WindowState == System.Windows.WindowState.Minimized)
                WindowState = System.Windows.WindowState.Normal;
            Activate();
            TryStartHeartSource();
            try { Logger.On("启动", "进度条 100%：主界面显示"); } catch { }
        }

        private void CollectTags(DependencyObject root)
        {
            foreach (object item in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject child = item as DependencyObject;
                if (child == null) continue;
                TextBlock t = child as TextBlock;
                if (t != null && t.Tag is string && ((string)t.Tag).StartsWith("t:"))
                    _tagged[(string)t.Tag] = t;
                CollectTags(child);
            }
        }

        private void Tag(TextBlock t, string key)
        {
            t.Tag = "t:" + key;
        }

        private TextBlock TaggedText(string text, double size, Brush color, string key, bool mono)
        {
            TextBlock t = mono ? MonoText(text, size, color) : DisplayText(text, size, color);
            Tag(t, key);
            return t;
        }

        private void OnClickFxDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_clickFx == null || _cfg == null || !_cfg.ClickFxEnabled) return;
                if (e.ChangedButton != MouseButton.Left) return;
                _clickFx.Press(e.GetPosition(_clickFx), e.ClickCount >= 2);
            }
            catch { }
        }

        private void OnClickFxUp(object sender, MouseButtonEventArgs e)
        {
            try { if (_clickFx != null) _clickFx.Release(); } catch { }
        }

        private void OnClickFxMove(object sender, MouseEventArgs e)
        {
            try { if (_clickFx != null) _clickFx.Move(e.GetPosition(_clickFx)); } catch { }
        }
        private void UpdateTelemetry()
        {
            RefreshDynamicAccent();
            // 第 10 批：游戏前台、或软件不在前台时，常驻背景动效休眠（核心功能照常）
            try
            {
                bool sleep = _tm.GameForeground || !IsActive;
                if (sleep != SCPSLAlert.UiAnim.Sleep)
                {
                    SCPSLAlert.UiAnim.Sleep = sleep;
                    if (_orbit != null) _orbit.SetSleep(sleep);
                }
            }
            catch { }

            VigilSnapshot snap = _snapshot != null ? _snapshot() : null;
            if (_tacticalData != null) _tacticalData.Text = TacticalDataText();
            bool running = snap != null ? snap.GameRunning : _tm.GameRunning;
            bool dead = snap != null && snap.SelfDead;
            bool known = snap != null && (!string.IsNullOrEmpty(snap.Role) || !string.IsNullOrEmpty(snap.LastScan));
            HeartMonitor.Update(running, dead);
            if (_bleChip != null)
                _bleChip.Visibility = HeartRateHub.BleConnected ? Visibility.Visible : Visibility.Collapsed;
            if (_heartBpmText != null)
            {
                int liveBpm = HeartRateHub.Bpm;
                _heartBpmText.Text = liveBpm > 0 ? liveBpm + " BPM" : "-- BPM";
            }
            if (_gameBpmText != null)
            {
                bool ble = HeartRateHub.BleConnected;
                _gameBpmText.Visibility = ble ? Visibility.Visible : Visibility.Collapsed;
                if (ble)
                {
                    int liveBpm = HeartRateHub.Bpm;
                    _gameBpmText.Text = liveBpm > 0 ? liveBpm + " BPM" : "-- BPM";
                }
            }
            double usedGb = (double)(_tm.TotalMemory - _tm.AvailableMemory) / 1073741824.0;
            double totalGb = (double)_tm.TotalMemory / 1073741824.0;
            double gameGb = _tm.GameMemoryMb / 1024.0;
            SetText("clock", DateTime.Now.ToString("HH:mm:ss"));
            SetText("sw.mem", _tm.ToolMemoryMb.ToString("0"));
            SetText("sw.cpu", "CPU " + _tm.ToolCpuPercent.ToString("0.0") + "%");
            SetText("sw.threads", "线程 " + _tm.ToolThreads);
            SetText("sw.ocr", _cfg.EnableOcr ? "OCR READY" : "OCR OFF");
            SetText("sys.pct", _tm.MemoryPercent.ToString("0"));
            SetText("sys.mem", usedGb.ToString("0.0") + " / " + totalGb.ToString("0.0") + " GB");
            SetText("sys.cpu", "CPU " + _tm.CpuPercent.ToString("0") + "%");
            SetText("sys.proc", "进程 " + _tm.ProcessCount);
            SetText("sys.up", "运行 " + _tm.Uptime);
            SetText("game.state", !running ? "未启动" : (snap == null ? "运行中" : (dead ? "已死亡" : (known ? "存活" : "运行中"))));
            TextBlock stateText;
            if (_tagged.TryGetValue("t:game.state", out stateText))
                stateText.Foreground = !running ? Ink : (dead ? Danger : Accent);
            SetText("game.proc", _tm.GameRunning ? _tm.GamePid.ToString() : "--");
            SetText("game.mem", _tm.GameRunning ? _tm.MemoryText(_tm.GameMemoryMb) : "--");
            SetText("game.cpu", _tm.GameRunning ? _tm.GameCpuPercent.ToString("0.0") + "%" : "--");
            SetText("game.threads", _tm.GameRunning ? _tm.GameThreads.ToString() : "--");
            SetText("game.window", _tm.GameRunning ? (_tm.GameForeground ? "前台" : "后台") : "--");
            SetText("game.role", snap != null && !string.IsNullOrEmpty(snap.Role) ? snap.Role : "未识别");
            SetText("game.judge", !running ? "--" : (snap == null ? "待接入" : (dead ? "DEAD" : (known ? "ALIVE" : "待判定"))));
            if (snap != null && snap.RoundStart != DateTime.MinValue)
            {
                TimeSpan el = DateTime.Now - snap.RoundStart;
                if (el.TotalSeconds < 0) el = TimeSpan.Zero;
                SetText("game.round", "回合  " + ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00"));
            }
            else SetText("game.round", "回合  --");
            SetText("game.evidence", snap != null && !string.IsNullOrEmpty(snap.LastScan) ? Trim(snap.LastScan, 18) : (snap != null && !string.IsNullOrEmpty(snap.OcrInfo) ? Trim(snap.OcrInfo, 18) : "--"));
            SetText("mem.used", usedGb.ToString("0.0"));
            SetText("mem.total", " / " + totalGb.ToString("0.0") + " GB");
            SetText("mem.sys", _tm.MemoryText(Math.Max(0, usedGb - gameGb - _tm.ToolMemoryMb / 1024.0) * 1024.0));
            SetText("mem.game", _tm.MemoryText(_tm.GameMemoryMb));
            SetText("mem.tool", _tm.MemoryText(_tm.ToolMemoryMb));
            SetText("mem.free", _tm.MemoryText(Math.Max(0, totalGb - usedGb) * 1024.0));
            string gpuLabel = _tm.GpuAvailable ? _tm.GpuPercent.ToString("0") + "%" : "N/A";
            string netLabel = _tm.NetworkAvailable ? _tm.NetworkMbps.ToString("0.00") + " Mbps" : "N/A";
            string diskLabel = _tm.DiskLabel + "  " + _tm.DiskUsedPercent.ToString("0") + "%";
            string tempLabel = _tm.TemperatureAvailable ? _tm.TemperatureCelsius.ToString("0.0") + "°C" : "N/A";
            SetText("res.cpu", _tm.CpuPercent.ToString("0.0") + "%");
            SetText("res.mem", _tm.MemoryPercent.ToString("0.0") + "%");
            SetText("res.gpu", gpuLabel);
            SetText("res.net", netLabel);
            SetText("res.disk", diskLabel);
            SetText("res.temp", tempLabel);
            SetText("res.sample", "采样 " + _historyData.Count + " / " + _historyData.Capacity + " s  ·  悬停看数值  ·  点击图例开关曲线  ·  双击性能");
            SetText("pres.sample", "采样 " + _historyData.Count + " / " + _historyData.Capacity + " s");
            SetText("pres.cpu", _tm.CpuPercent.ToString("0.0") + "%");
            SetText("pres.mem", _tm.MemoryPercent.ToString("0.0") + "%");
            SetText("pres.gpu", gpuLabel);
            SetText("pres.net", netLabel);
            SetText("pres.disk", diskLabel);
            SetText("pres.temp", tempLabel);
            SetText("fact.gpu", gpuLabel);
            SetText("fact.net", netLabel);
            SetText("fact.disk", _tm.DiskLabel + "  " + _tm.DiskFreePercent.ToString("0") + "%");
            UpdateTemperatureVisibility();
            SetText("signal.judge", !running ? "WAITING" : (dead ? "DEAD" : (known ? "ALIVE" : "PENDING")));
            SetText("ticker", "SYS.CHECK  OK     |     POLL 1000 MS     |     MEMORY  LIVE     |     GAME  " + (running ? "RUNNING" : "OFFLINE") + "     |     JUDGE  " + (!running ? "WAITING" : (dead ? "DEAD" : (known ? "ALIVE" : "PENDING"))));
            UpdateGameAction();
            if (_memBar != null)
            {
                _memBar.SetValues(
                    Math.Max(0, usedGb - gameGb),
                    gameGb,
                    _tm.ToolMemoryMb / 1024.0,
                    Math.Max(0, totalGb - usedGb));
            }
        }

        private static readonly HashSet<string> RollKeys = new HashSet<string>
        {
            "mem.used", "mem.game", "sw.mem", "sys.pct", "game.mem", "game.cpu"
        };

        private void SetText(string key, string value)
        {
            TextBlock t;
            if (!_tagged.TryGetValue("t:" + key, out t) || t.Text == value) return;
            if (RollKeys.Contains(key))
            {
                Roll(t, value);
                return;
            }
            t.Text = value;
        }

        private void Roll(TextBlock t, string value)
        {
            TranslateTransform move = t.RenderTransform as TranslateTransform;
            if (move == null)
            {
                move = new TranslateTransform();
                t.RenderTransform = move;
            }
            DoubleAnimation outY = new DoubleAnimation(0, -9, new Duration(TimeSpan.FromMilliseconds(160)));
            outY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            DoubleAnimation outFade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160)));
            outFade.Completed += delegate
            {
                t.Text = value;
                t.BeginAnimation(UIElement.OpacityProperty, null);
                move.BeginAnimation(TranslateTransform.YProperty, null);
                t.Opacity = 0;
                move.Y = 9;
                DoubleAnimation inY = new DoubleAnimation(9, 0, new Duration(TimeSpan.FromMilliseconds(180)));
                inY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.YProperty, inY);
                t.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))));
            };
            move.BeginAnimation(TranslateTransform.YProperty, outY);
            t.BeginAnimation(UIElement.OpacityProperty, outFade);
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string one = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return one.Length <= max ? one : one.Substring(0, max) + "…";
        }

        public void SetLaunchBusy(bool busy)
        {
            _launchBusy = busy;
            UpdateGameAction();
        }

        private void UpdateGameAction()
        {
            if (_gameActionBorder == null || _gameActionText == null) return;
            Color accent = ColorOf(Accent);
            Color danger = ColorOf(Danger);
            Color panelAlt = ColorOf(PanelAlt);
            Color sub = ColorOf(Sub);
            Color line = ColorOf(Line);
            if (_launchBusy)
            {
                _gameActionText.Text = "正在扫描…";
                _gameActionIcon.Text = "\uE895";
                _gameActionBase = VigilButtonPalette.Mix(panelAlt, line, 0.35);
                _gameActionFgColor = sub;
            }
            else if (_tm.GameRunning)
            {
                _gameActionText.Text = "停止游戏";
                _gameActionIcon.Text = "\uE71A";
                _gameActionBase = danger;
                _gameActionFgColor = _light ? Colors.White : Color.FromRgb(8, 18, 24);
            }
            else
            {
                _gameActionText.Text = "启动游戏";
                _gameActionIcon.Text = "\uE768";
                _gameActionBase = accent;
                _gameActionFgColor = _light ? Colors.White : Color.FromRgb(8, 18, 24);
            }
            ApplyGameActionVisual();
        }

        private void ApplyGameActionVisual()
        {
            if (_gameActionBorder == null || _gameActionFill == null || _gameActionFore == null) return;
            Color bg = _gameActionBase;
            if (_gameActionPress) bg = VigilButtonPalette.Darken(bg, 0.16);
            else if (_gameActionHover) bg = VigilButtonPalette.Lighten(bg, 0.12);
            ColorAnimation fill = new ColorAnimation(bg, new Duration(TimeSpan.FromMilliseconds(110)));
            fill.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _gameActionFill.BeginAnimation(SolidColorBrush.ColorProperty, fill);
            _gameActionFore.Color = _gameActionFgColor;
            _gameActionBorder.BorderBrush = new SolidColorBrush(
                _gameActionPress ? VigilButtonPalette.Darken(_gameActionBase, 0.22) : _gameActionBase);
        }
        private static Color Alpha(int a, int r, int g, int b)
        {
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }

        private static SolidColorBrush Solid(System.Drawing.Color c)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush SolidAlpha(Color c, double alpha)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Max(0, Math.Min(255, (int)(255 * alpha))), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush SolidAlpha(System.Drawing.Color c, double alpha)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Max(0, Math.Min(255, (int)(255 * alpha))), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static bool IsVideoPath(string path)
        {
            string ext = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".wmv" || ext == ".avi" || ext == ".mov" || ext == ".mkv";
        }

        private UIElement CustomBackground()
        {
            string path = _cfg.BackgroundImage;
            Grid host = new Grid();
            double raw = Math.Max(0, Math.Min(100, _cfg.BackgroundOpacity));
            double op = raw <= 0 ? 0 : 0.35 + 0.65 * raw / 100.0;   // 0 仍表示隐藏；其余给一个可见下限
            Stretch stretch = _cfg.BackgroundFill == 1 ? Stretch.Uniform : Stretch.UniformToFill;

            if (IsVideoPath(path))
            {
                if (_bgVideo == null || !string.Equals(_bgVideoPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (_bgVideo != null)
                    {
                        try { _bgVideo.Stop(); _bgVideo.Close(); } catch { }
                        _bgVideo = null;
                    }
                    MediaElement v = new MediaElement();
                    v.LoadedBehavior = MediaState.Manual;
                    v.UnloadedBehavior = MediaState.Manual;
                    v.IsMuted = true;
                    v.Source = new Uri(path, UriKind.Absolute);
                    v.MediaEnded += delegate
                    {
                        try { v.Position = TimeSpan.Zero; v.Play(); } catch { }
                    };
                    v.MediaFailed += delegate { };
                    _bgVideo = v;
                    _bgVideoPath = path;
                    try { v.Play(); } catch { }
                }
                _bgVideo.Stretch = stretch;
                _bgVideo.Opacity = op;
                host.Children.Add(_bgVideo);
                return host;
            }

            if (_cfg.BackgroundFill == 2)
            {
                ImageBrush brush = new ImageBrush();
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                brush.ImageSource = bmp;
                brush.TileMode = TileMode.Tile;
                brush.ViewportUnits = BrushMappingMode.Absolute;
                brush.Viewport = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight);
                brush.Opacity = op;
                Rectangle tile = new Rectangle();
                tile.Fill = brush;
                host.Children.Add(tile);
                return host;
            }

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Image img = new Image();
            img.Source = image;
            img.Stretch = stretch;
            img.Opacity = op;
            host.Children.Add(img);
            return host;
        }

        private void EnsureBackgroundMusic()
        {
            try
            {
                string path = _cfg.BackgroundMusicEnabled ? (_cfg.BackgroundMusic ?? "").Trim() : "";
                if (path.Length == 0 || !File.Exists(path))
                {
                    if (_bgMusic != null) { _bgMusic.Stop(); _bgMusic.Close(); _bgMusic = null; _bgMusicPath = ""; }
                    return;
                }
                if (_bgMusic == null || !string.Equals(_bgMusicPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (_bgMusic != null) { _bgMusic.Stop(); _bgMusic.Close(); }
                    MediaPlayer p = new MediaPlayer();
                    p.Open(new Uri(path, UriKind.Absolute));
                    p.Volume = Math.Max(0, Math.Min(100, _cfg.BackgroundMusicVolume)) / 100.0;
                    p.MediaEnded += delegate
                    {
                        try
                        {
                            if (_cfg.BackgroundMusicLoop) { p.Position = TimeSpan.Zero; p.Play(); }
                        }
                        catch { }
                    };
                    _bgMusic = p;
                    _bgMusicPath = path;
                    p.Play();
                }
                else
                {
                    _bgMusic.Volume = Math.Max(0, Math.Min(100, _cfg.BackgroundMusicVolume)) / 100.0;
                }
            }
            catch { }
        }
        private void ApplyUiFont()
        {
            try
            {
                if (!string.IsNullOrEmpty(_cfg.UiFontFile) && File.Exists(_cfg.UiFontFile)
                    && !string.IsNullOrEmpty(_cfg.UiFontName))
                {
                    System.Windows.Media.Fonts.GetFontFamilies(new Uri(_cfg.UiFontFile));
                    FontFamily = new System.Windows.Media.FontFamily(_cfg.UiFontName);
                    return;
                }
            }
            catch { }
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        }
        private void ApplyGlassNow()
        {
            try
            {
                if (_cfg.GlassWindow)
                {
                    if (Content is Panel)
                        ((Panel)Content).Background = SolidAlpha(ColorOf(Bg), 0.55);
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                }
                else
                {
                    Background = Bg;
                }
            }
            catch { }
            try
            {
                Logger.On("外观", "毛玻璃 " + (_cfg.GlassWindow ? "开" : "关")
                    + "，系统 " + Environment.OSVersion.Version.Major + "." + Environment.OSVersion.Version.Minor
                    + " build " + Environment.OSVersion.Version.Build);
            }
            catch { }
            GlassEffect.Apply(this, _cfg.GlassWindow && !( !string.IsNullOrWhiteSpace(_cfg.BackgroundImage) && File.Exists(_cfg.BackgroundImage)), _cfg.GlassStrength, WpfCardGlass.LightTint);
        }
        /// <summary>界面缩放与密度：作用在每个页面容器上，滚动条仍然有效。</summary>
        private void ApplyPageScaleAndDensity()
        {
            double f = Math.Max(0.85, Math.Min(1.25, _cfg.UiScale / 100.0));
            Thickness pad = _cfg.UiDensity == 0
                ? new Thickness(18, 10, 18, 10)
                : new Thickness(28, 18, 28, 18);
            ScrollViewer[] pages = new ScrollViewer[]
            {
                _generalPage, _logPage, _alertPage, _audioPage, _timePage,
                _hotkeyPage, _toolsPage, _advancedPage, _appearancePage,
                _appearanceDetailPage, _advDetailPage
            };
            for (int i = 0; i < pages.Length; i++)
            {
                ScrollViewer sv = pages[i];
                if (sv == null) continue;
                sv.Padding = pad;
                if (sv.Content is FrameworkElement)
                    ((FrameworkElement)sv.Content).LayoutTransform = new ScaleTransform(f, f);
            }
            if (_statusPage != null && _cfg.UiDensity == 0)
                _statusPage.Margin = new Thickness(0);
        }

        /// <summary>动态强调色：按当前阵营微调主色（SCP 红 / 混沌绿），关掉就用主题色。</summary>
        private System.Drawing.Color DynamicAccentColor()
        {
            System.Drawing.Color baseColor = ThemeTokens.For(_light);
            if (!_cfg.DynamicAccent) return baseColor;
            string role = "";
            try
            {
                VigilSnapshot s = _snapshot != null ? _snapshot() : null;
                if (s != null) role = s.Role ?? "";
            }
            catch { }
            if (string.IsNullOrEmpty(role)) return baseColor;
            if (role.IndexOf("混沌", StringComparison.Ordinal) >= 0)
                return _light ? System.Drawing.Color.FromArgb(38, 122, 63) : System.Drawing.Color.FromArgb(123, 216, 143);
            if (role.IndexOf("SCP", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("079", StringComparison.Ordinal) >= 0)
                return _light ? System.Drawing.Color.FromArgb(178, 52, 46) : System.Drawing.Color.FromArgb(255, 118, 112);
            return baseColor;
        }

        private string _lastAccentRole = "";
        private void RefreshDynamicAccent()
        {
            if (!_cfg.DynamicAccent) return;
            string role = "";
            try
            {
                VigilSnapshot s = _snapshot != null ? _snapshot() : null;
                if (s != null) role = s.Role ?? "";
            }
            catch { }
            if (role == _lastAccentRole) return;
            _lastAccentRole = role;
            RebuildForThemeChange();
        }
        private void BuildPalette()
        {
            UiPalette p = ThemeTokens.Get(_light);
            Bg = Solid(p.PageBg);
            Panel = Solid(p.Panel);
            PanelAlt = Solid(p.PanelAlt);
            Line = Solid(p.Line);
            Ink = Solid(p.Text);
            Sub = Solid(p.Sub);
            Muted = Solid(p.Muted);
            Accent = Solid(DynamicAccentColor());
            Accent2 = Solid(p.Info);
            Danger = Solid(p.Danger);
            SystemBar = Solid(ThemeTokens.Blend(p.Line, p.Muted, 0.35f));
            ToolBar = Solid(p.Sub);
            GpuBrush = Solid(_light ? System.Drawing.Color.FromArgb(176, 103, 0) : System.Drawing.Color.FromArgb(255, 176, 58));
            NetBrush = Solid(_light ? System.Drawing.Color.FromArgb(118, 72, 196) : System.Drawing.Color.FromArgb(185, 140, 255));
            DiskBrush = Solid(_light ? System.Drawing.Color.FromArgb(35, 122, 74) : System.Drawing.Color.FromArgb(91, 227, 140));
            WpfCardGlass.Configure(_cfg, _light);
        }

        private void Build()
        {
            _hoverTargets.Clear();
            // 换主题会整棵界面重建，旧的描边锁定目标已经不在可视树上了。
            if (_hoverLockTimer != null) _hoverLockTimer.Stop();
            _hoverLockTarget = null;
            Grid root = new Grid();
            root.Background = Bg;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            Content = root;

            Grid bg = new Grid();
            bg.IsHitTestVisible = false;
            bool customBg = !string.IsNullOrWhiteSpace(_cfg.BackgroundImage) && File.Exists(_cfg.BackgroundImage);
            if (customBg)
            {
                UIElement layer = CustomBackground();
                if (layer != null) bg.Children.Add(layer);
                Rectangle dark = new Rectangle();
                dark.Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(Math.Max(0, Math.Min(100, _cfg.BackgroundDarken)) * 255 / 100), (byte)4, (byte)8, (byte)12));
                bg.Children.Add(dark);
            }
            else
            {
                UIElement backdrop = new DataBackdrop(_light, Accent, Line, Muted);
                // 窗口毛玻璃开启时，让背景层半透明，DWM 亚克力才有机会透出来。
                if (_cfg.GlassWindow) backdrop.Opacity = 0.42;
                bg.Children.Add(backdrop);
            }
            _orbit = new OrbitLayer(_light, Accent, Line);
            bg.Children.Add(_orbit);
            Grid.SetRowSpan(bg, 3);
            root.Children.Add(bg);

            if (_cfg.GlassWindow)
            {
                try { root.Background = SolidAlpha(ColorOf(Bg), 0.55); } catch { }
                GlassEffect.Apply(this, true, _cfg.GlassStrength, WpfCardGlass.LightTint);
            }
            else
            {
                GlassEffect.Apply(this, false, 0, WpfCardGlass.LightTint);
            }
            EnsureBackgroundMusic();
            ApplyPageScaleAndDensity();

            root.Children.Add(TopBar());

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            _body = body;
            body.Children.Add(Rail());

            Grid content = new Grid();
            content.Margin = new Thickness(28, 18, 28, 18);
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(214) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(124) });
            Grid.SetColumn(content, 1);
            body.Children.Add(content);
            _statusPage = content;
            _statusPage.Background = Brushes.Transparent;

            _generalPage = new ScrollViewer();
            _generalPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _generalPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _generalPage.Padding = new Thickness(28, 18, 28, 18);
            _generalPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_generalPage, 1);
            body.Children.Add(_generalPage);

            _logPage = new ScrollViewer();
            _logPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _logPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _logPage.Padding = new Thickness(28, 18, 28, 18);
            _logPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_logPage, 1);
            body.Children.Add(_logPage);

            _alertPage = new ScrollViewer();
            _alertPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _alertPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _alertPage.Padding = new Thickness(28, 18, 28, 18);
            _alertPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_alertPage, 1);
            body.Children.Add(_alertPage);

            _audioPage = new ScrollViewer();
            _audioPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _audioPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _audioPage.Padding = new Thickness(28, 18, 28, 18);
            _audioPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_audioPage, 1);
            body.Children.Add(_audioPage);

            _timePage = new ScrollViewer();
            _timePage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _timePage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _timePage.Padding = new Thickness(28, 18, 28, 18);
            _timePage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_timePage, 1);
            body.Children.Add(_timePage);

            _hotkeyPage = new ScrollViewer();
            _hotkeyPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _hotkeyPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _hotkeyPage.Padding = new Thickness(28, 18, 28, 18);
            _hotkeyPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_hotkeyPage, 1);
            body.Children.Add(_hotkeyPage);

            _toolsPage = new ScrollViewer();
            _toolsPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _toolsPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _toolsPage.Padding = new Thickness(28, 18, 28, 18);
            _toolsPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_toolsPage, 1);
            body.Children.Add(_toolsPage);

            _advancedPage = new ScrollViewer();
            _advancedPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _advancedPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _advancedPage.Padding = new Thickness(28, 18, 28, 18);
            _advancedPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_advancedPage, 1);
            body.Children.Add(_advancedPage);

            _appearancePage = new ScrollViewer();
            _appearancePage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _appearancePage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _appearancePage.Padding = new Thickness(28, 18, 28, 18);
            _appearancePage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_appearancePage, 1);
            body.Children.Add(_appearancePage);

            _appearanceDetailPage = new ScrollViewer();
            _appearanceDetailPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _appearanceDetailPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _appearanceDetailPage.Padding = new Thickness(28, 18, 28, 18);
            _appearanceDetailPage.Visibility = Visibility.Collapsed;
            _appearanceDetailBody = new StackPanel();
            _appearanceDetailPage.Content = _appearanceDetailBody;
            Grid.SetColumn(_appearanceDetailPage, 1);

            _cardDesignPage = new ScrollViewer();
            _cardDesignPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _cardDesignPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _cardDesignPage.Padding = new Thickness(28, 18, 28, 18);
            _cardDesignPage.Visibility = Visibility.Collapsed;
            _cardDesignBody = new StackPanel();
            _cardDesignPage.Content = _cardDesignBody;
            Grid.SetColumn(_cardDesignPage, 1);
            body.Children.Add(_cardDesignPage);
            body.Children.Add(_appearanceDetailPage);

            _advDetailPage = new ScrollViewer();
            _advDetailPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _advDetailPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _advDetailPage.Padding = new Thickness(28, 18, 28, 18);
            _advDetailPage.Visibility = Visibility.Collapsed;
            _advDetailBody = new StackPanel();
            _advDetailPage.Content = _advDetailBody;
            Grid.SetColumn(_advDetailPage, 1);
            body.Children.Add(_advDetailPage);

            CreateCrosshairPage(body);

            _perfPage = new ScrollViewer();
            _perfPage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _perfPage.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _perfPage.Padding = new Thickness(28, 18, 28, 18);
            _perfPage.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_perfPage, 1);
            body.Children.Add(_perfPage);
            HookPageScroll(_generalPage);
            HookPageScroll(_logPage);
            HookPageScroll(_alertPage);
            HookPageScroll(_audioPage);
            HookPageScroll(_timePage);
            HookPageScroll(_hotkeyPage);
            HookPageScroll(_toolsPage);
            HookPageScroll(_advancedPage);
            HookPageScroll(_appearancePage);
            HookPageScroll(_appearanceDetailPage);
            HookPageScroll(_advDetailPage);
            HookPageScroll(_perfPage);
            _statusPage.PreviewMouseLeftButtonDown += OnStatusPageMouseDown;

            content.Children.Add(Head());

            UIElement triad = StatusTop();
            Grid.SetRow(triad, 1);
            content.Children.Add(triad);

            UIElement mem = MemoryStrip();
            Grid.SetRow(mem, 2);
            content.Children.Add(mem);

            VigilCard history = PanelCard();
            history.Margin = new Thickness(0, 8, 0, 0);
            history.Child = HistoryPanel();
            Grid.SetRow(history, 3);
            content.Children.Add(history);

            UIElement bottom = BottomRow();
            Grid.SetRow(bottom, 4);
            content.Children.Add(bottom);

            root.Children.Add(BottomStrip());

            _hoverLayer = new Canvas();
            _hoverLayer.IsHitTestVisible = false;
            _hoverLayer.ClipToBounds = true;
            System.Windows.Controls.Panel.SetZIndex(_hoverLayer, 100);
            // 只覆盖右侧内容列，不要横跨左侧导航栏。
            // 之前 ColumnSpan=2 时，悬停描边会画到左面板上面，看起来像“包边盖住了导航”。
            Grid.SetColumn(_hoverLayer, 1);
            body.Children.Add(_hoverLayer);

            if (_clickFx != null && _clickFx.Parent is Panel) ((Panel)_clickFx.Parent).Children.Remove(_clickFx);
            _clickFx = new ClickFxLayer(ColorOf(Accent));
            _clickFx.GapSize = _cfg.ClickFxGap;
            System.Windows.Controls.Panel.SetZIndex(_clickFx, 400);
            Grid.SetRowSpan(_clickFx, 3);
            root.Children.Add(_clickFx);

            _hoverFrame = new HoverOutline(Accent);
            _hoverFrame.Opacity = 0;
            _hoverFrame.Visibility = Visibility.Collapsed;
            _hoverFrame.IsHitTestVisible = false;
            _hoverLayer.Children.Add(_hoverFrame);

            _flyBar = new Border();
            _flyBar.Width = 3;
            _flyBar.Height = 20;
            _flyBar.CornerRadius = new CornerRadius(1);
            _flyBar.Background = Accent;
            _flyBar.Opacity = 0;
            _flyBar.Visibility = Visibility.Collapsed;
            _flyBar.IsHitTestVisible = false;
            _hoverLayer.Children.Add(_flyBar);

            _flyTrail = new Border();
            _flyTrail.Height = 2;
            _flyTrail.CornerRadius = new CornerRadius(1);
            _flyTrail.Background = Accent;
            _flyTrail.Opacity = 0;
            _flyTrail.Visibility = Visibility.Collapsed;
            _flyTrail.IsHitTestVisible = false;
            System.Windows.Controls.Panel.SetZIndex(_flyTrail, 100);
            _hoverLayer.Children.Add(_flyTrail);

            _hoverLeave = new DispatcherTimer();
            _hoverLeave.Interval = TimeSpan.FromMilliseconds(180);
            _hoverLeave.Tick += delegate { _hoverLeave.Stop(); FadeHover(); };

            // 跨格子飞行结束后再把描边贴死在目标上；
            // LayoutUpdated 在飞行过程中不能插手，否则会把动画直接拉到终点。
            _hoverSettle = new DispatcherTimer();
            _hoverSettle.Interval = TimeSpan.FromMilliseconds(340);
            _hoverSettle.Tick += delegate
            {
                _hoverSettle.Stop();
                _hoverAnimating = false;
                if (_hoverCurrent != null && _hoverFrame != null
                    && _hoverFrame.Visibility == Visibility.Visible) SnapHover();
            };

            _hoverLockTimer = new DispatcherTimer();
            _hoverLockTimer.Interval = TimeSpan.FromSeconds(5);
            _hoverLockTimer.Tick += delegate
            {
                _hoverLockTimer.Stop();
                ReleaseHoverLock();
            };
            MouseMove += OnHoverMove;
            PreviewKeyDown += OnHotkeyKeyDown;
            SizeChanged += delegate { if (_hoverCurrent != null && !_hoverAnimating) SnapHover(); };
            LayoutUpdated += delegate { SyncHoverToTarget(); };

            AllowDrop = true;
            BuildSearchLayer();
        }

        private VigilCard PanelCard()
        {
            VigilCard c = new VigilCard();
            c.Fill = Panel;
            c.Stroke = Line;
            c.Cut = 10;
            c.Padding = new Thickness(18, 14, 18, 14);
            _hoverTargets.Add(c);
            return c;
        }

        private UIElement TopBar()
        {
            Border bar = new Border();
            bar.Background = _light ? Panel : Solid(3, 6, 8);
            bar.BorderBrush = Line;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (InsideButton(e.OriginalSource as DependencyObject)) return;
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = g;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(16, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            TextBlock v = DisplayText("A.R.C.T", 19, Accent);
            v.FontWeight = FontWeights.Bold;
            brand.Children.Add(v);
            g.Children.Add(brand);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 10, 0);
            right.Children.Add(CommandButton("\uE721", "搜索", delegate { ShowSearch(); }, Accent));
            right.Children.Add(LegacyButton("旧界面", delegate { if (_switchLegacy != null) _switchLegacy(); }));
            right.Children.Add(CommandButton(_light ? "\uE706" : "\uE708", "切换亮暗模式",
                delegate { CycleThemeMode(); }, Accent2));
            right.Children.Add(CommandButton("\uE7E8", "退出程序", delegate { if (_exit != null) _exit(); }, Danger));
            Border themeChip = new Border();
            themeChip.CornerRadius = new CornerRadius(4);
            themeChip.BorderThickness = new Thickness(1);
            themeChip.BorderBrush = Line;
            themeChip.Background = PanelAlt;
            themeChip.Height = 22;
            themeChip.Padding = new Thickness(9, 0, 9, 0);
            themeChip.Margin = new Thickness(2, 0, 4, 0);
            themeChip.VerticalAlignment = VerticalAlignment.Center;
            themeChip.Child = MonoText(ThemeStatusText(), 8, Accent2);
            right.Children.Add(themeChip);
            Border commandSep = new Border();
            commandSep.Width = 1;
            commandSep.Height = 20;
            commandSep.Background = Line;
            commandSep.Margin = new Thickness(2, 0, 6, 0);
            commandSep.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(commandSep);
            right.Children.Add(WinButton("\uE921", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(WinButton("\uE922", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            right.Children.Add(WinButton("\uE8BB", delegate { Close(); }, true));
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return bar;
        }

        private string ThemeStatusText()
        {
            if (_cfg.UiThemeMode < 0) return _light ? "LIGHT / AUTO" : "DARK / AUTO";
            return _light ? "LIGHT / MANUAL" : "DARK / MANUAL";
        }

        private void CycleThemeMode()
        {
            _cfg.UiThemeMode = _cfg.UiThemeMode < 0 ? 0 : _cfg.UiThemeMode == 0 ? 1 : -1;
            if (_save != null) _save();
            ModernTheme.SetMode(_cfg.UiThemeMode);
            _light = _cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : _cfg.UiThemeMode == 0;
            BuildPalette();
            Build();
            CollectTags(this);
            ApplySeriesVisibility();
            UpdateTemperatureVisibility();
            UpdateTelemetry();
            UpdatePerformance();
        }

        private void OnAppThemeChanged()
        {
            if (_appearanceRebuildQueued) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                int page = _currentPage;
                _light = _cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : _cfg.UiThemeMode == 0;
                BuildPalette();
                Build();
                CollectTags(this);
                ApplySeriesVisibility();
                UpdateTemperatureVisibility();
                UpdateTelemetry();
                UpdatePerformance();
                if (page >= 0 && page <= AppearancePageIndex) SelectPage(page);
                if (page == AppearancePageIndex && _appearanceDetailOpen) ShowAppearanceDetail();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 右上角的“旧界面”按钮。需求要求只有这一个按钮用白底黑字，
        /// 不被全局主题覆盖；同时锁 300ms，避免双击重复切换。
        /// </summary>
        private Button LegacyButton(string text, Action click)
        {
            Button b = new Button();
            b.Content = text;
            b.Height = 26;
            b.MinWidth = 64;
            b.Padding = new Thickness(10, 0, 10, 0);
            b.Background = Brushes.White;
            b.Foreground = Solid(26, 26, 26);
            b.BorderBrush = Solid(208, 208, 208);
            b.BorderThickness = new Thickness(1);
            b.FontFamily = Ui;
            b.FontSize = 11;
            b.Cursor = Cursors.Hand;
            b.Focusable = true;
            b.Margin = new Thickness(0, 0, 8, 0);
            b.ToolTip = "切换到旧界面（经典 WinForms）";

            ControlTemplate t = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            b.Template = t;

            b.MouseEnter += delegate { b.Background = Solid(248, 248, 248); };
            b.MouseLeave += delegate { b.Background = Brushes.White; };
            b.PreviewMouseLeftButtonDown += delegate { b.Background = Solid(237, 237, 237); };
            b.PreviewMouseLeftButtonUp += delegate { b.Background = Solid(248, 248, 248); };
            b.Click += delegate
            {
                if (DateTime.UtcNow < _legacySwitchAt) return;
                _legacySwitchAt = DateTime.UtcNow.AddMilliseconds(300);
                if (click != null) click();
            };
            return b;
        }

        private Button CommandButton(string glyph, string text, Action click, Brush accent)
        {
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = Icons;
            icon.FontSize = 11.5;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 7, 0);
            content.Children.Add(icon);
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontFamily = Ui;
            label.FontSize = 11;
            label.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(label);

            VigilButtonKind kind;
            if (ReferenceEquals(accent, Danger)) kind = VigilButtonKind.Danger;
            else if (ReferenceEquals(accent, Muted)) kind = VigilButtonKind.Ghost;
            else if (ReferenceEquals(accent, Sub)) kind = VigilButtonKind.Secondary;
            else kind = VigilButtonKind.Primary;

            VigilButton b = new VigilButton(content, kind, ButtonPalette(accent), 30, 12);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.ToolTip = text;
            b.Click += delegate { if (click != null) click(); };
            return b;
        }

        /// <summary>把当前主题的刷子转成一套按钮配色。</summary>
        private VigilButtonPalette ButtonPalette(Brush accent)
        {
            VigilButtonPalette p = new VigilButtonPalette();
            p.Accent = ColorOf(accent);
            p.Panel = ColorOf(Panel);
            p.PanelAlt = ColorOf(PanelAlt);
            p.Line = ColorOf(Line);
            p.Ink = ColorOf(Ink);
            p.Sub = ColorOf(Sub);
            p.Muted = ColorOf(Muted);
            p.Danger = ColorOf(Danger);
            p.Light = _light;
            return p;
        }

        private static Color ColorOf(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            return solid == null ? Colors.White : solid.Color;
        }
        private Border HeartButton(string text, Action click, bool primary)
        {
            return HeartButton(text, click, primary, 26, 10);
        }

        private Border HeartSmallButton(string text, Action click, bool primary)
        {
            return HeartButton(text, click, primary, 24, 9);
        }

        private Border HeartButton(string text, Action click, bool primary, double height, double pad)
        {
            Color accent = ColorOf(Accent);
            Color panel = ColorOf(Panel);
            Color panelAlt = ColorOf(PanelAlt);
            Color line = ColorOf(Line);
            Color ink = ColorOf(Ink);

            Color bgBase, bgHover, bgPress, bdBase, bdHover, bdPress, fg;
            if (primary)
            {
                bgBase = accent;
                bgHover = VigilButtonPalette.Lighten(accent, 0.14);
                bgPress = VigilButtonPalette.Darken(accent, 0.12);
                bdBase = accent;
                bdHover = VigilButtonPalette.Lighten(accent, 0.25);
                bdPress = VigilButtonPalette.Darken(accent, 0.15);
                fg = _light ? Colors.White : Color.FromRgb(6, 20, 26);
            }
            else
            {
                bgBase = panelAlt;
                bgHover = VigilButtonPalette.Mix(panelAlt, accent, 0.14);
                bgPress = VigilButtonPalette.Mix(panelAlt, accent, 0.24);
                bdBase = line;
                bdHover = VigilButtonPalette.Fade(accent, 190);
                bdPress = accent;
                fg = ink;
            }

            Border b = new Border();
            b.Height = height;
            b.Padding = new Thickness(pad, 0, pad, 0);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.CornerRadius = new CornerRadius(4);
            b.BorderThickness = new Thickness(1);
            b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontFamily = Ui;
            label.FontSize = height >= 30 ? 10.5 : 10;
            label.FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal;
            label.Foreground = new SolidColorBrush(fg);
            label.VerticalAlignment = VerticalAlignment.Center;
            b.Child = label;
            new VigilPressState(b, bgBase, bgHover, bgPress, bdBase, bdHover, bdPress);
            b.MouseLeftButtonUp += delegate { if (click != null) click(); };
            return b;
        }


        private Button IconButton(string glyph, Action click, Brush hover)
        {
            Button b = new Button();
            b.Width = 28;
            b.Height = 28;
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = FlatTemplate();
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = Icons;
            icon.FontSize = 11;
            icon.Foreground = Sub;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            b.Content = icon;
            b.MouseEnter += delegate { b.Background = PanelAlt; icon.Foreground = hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; icon.Foreground = Sub; b.RenderTransform = Transform.Identity; };
            b.PreviewMouseLeftButtonDown += delegate
            {
                b.Background = AccentSoftBrush();
                b.RenderTransform = new ScaleTransform(0.9, 0.9);
            };
            b.PreviewMouseLeftButtonUp += delegate
            {
                b.Background = PanelAlt;
                b.RenderTransform = Transform.Identity;
            };
            b.Click += delegate { click(); };
            return b;
        }
        private Button WinButton(string glyph, Action click, bool danger = false)
        {
            Button b = new Button();
            b.Content = glyph;
            b.FontFamily = Icons;
            b.FontSize = 11;
            b.Width = 38;
            b.Height = 30;
            b.Margin = new Thickness(3, 0, 0, 0);
            b.Background = Brushes.Transparent;
            b.Foreground = Sub;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = FlatTemplate();
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.MouseEnter += delegate
            {
                b.Background = danger
                    ? new SolidColorBrush(VigilButtonPalette.Mix(ColorOf(Panel), ColorOf(Danger), 0.22))
                    : PanelAlt;
                b.Foreground = danger ? Danger : Ink;
            };
            b.MouseLeave += delegate
            {
                b.Background = Brushes.Transparent;
                b.Foreground = Sub;
                b.RenderTransform = Transform.Identity;
            };
            b.PreviewMouseLeftButtonDown += delegate
            {
                b.Background = danger
                    ? new SolidColorBrush(VigilButtonPalette.Mix(ColorOf(Panel), ColorOf(Danger), 0.34))
                    : AccentSoftBrush();
                b.Foreground = danger ? Brushes.White : Accent;
                b.RenderTransform = new ScaleTransform(0.94, 0.94);
            };
            b.PreviewMouseLeftButtonUp += delegate
            {
                b.RenderTransform = Transform.Identity;
                b.Background = danger
                    ? new SolidColorBrush(VigilButtonPalette.Mix(ColorOf(Panel), ColorOf(Danger), 0.22))
                    : PanelAlt;
                b.Foreground = danger ? Danger : Ink;
            };
            b.Click += delegate { click(); };
            return b;
        }

        private static bool InsideButton(DependencyObject source)
        {
            DependencyObject node = source;
            while (node != null)
            {
                if (node is Button) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private static ControlTemplate FlatTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            ControlTemplate t = new ControlTemplate(typeof(Button));
            t.VisualTree = border;
            return t;
        }

        private UIElement Rail()
        {
            Border rail = new Border();
            rail.Background = _light ? Panel : Solid(4, 7, 9);
            rail.BorderBrush = Line;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            rail.Width = 66;
            rail.HorizontalAlignment = HorizontalAlignment.Left;
            rail.ClipToBounds = true;
            // 左导航展开后会盖住内容区；它的层级必须高于悬停描边层（100），
            // 这样描边飞行经过展开的导航栏时会被导航栏挡住，而不是画在它上面。
            System.Windows.Controls.Panel.SetZIndex(rail, 200);
            Grid.SetColumn(rail, 0);
            Grid.SetColumnSpan(rail, 2);
            _rail = rail;
            _railExpanded = false;
            rail.MouseEnter += delegate { ExpandRail(true); };
            rail.MouseLeave += delegate { ExpandRail(false); };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            rail.Child = grid;

            Grid wordHost = new Grid();
            wordHost.Width = 66;
            wordHost.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock word = DisplayText("A.R.C.T", 16, Accent);
            word.FontWeight = FontWeights.Bold;
            word.LayoutTransform = new RotateTransform(-90);
            word.HorizontalAlignment = HorizontalAlignment.Center;
            word.VerticalAlignment = VerticalAlignment.Center;
            wordHost.Children.Add(word);
            grid.Children.Add(wordHost);

            StackPanel icons = new StackPanel();
            icons.Margin = new Thickness(7, 4, 7, 0);
            string[] glyphs = { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE771", "\uE8A5", "\uE90F", "\uE7BA" };
            string[] labels = { "状态", "常规", "提示", "触发点", "时间", "热键", "外观", "日志", "工具", "高级" };
            _railItems.Clear();
            _railIcons.Clear();
            _railBars.Clear();
            _railLabels.Clear();
            _railLabelGroups.Clear();
            for (int i = 0; i < glyphs.Length; i++)
            {
                int index = PageOrder[i];
                Border item = new Border();
                item.Width = 194;
                item.Height = 48;
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Background = Brushes.Transparent;
                item.Cursor = Cursors.Hand;
                Grid cell = new Grid();
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                cell.ColumnDefinitions.Add(new ColumnDefinition());
                Border navBar = new Border();
                navBar.Width = 3;
                navBar.Height = 22;
                navBar.Background = Accent;
                navBar.HorizontalAlignment = HorizontalAlignment.Left;
                navBar.Opacity = index == 0 ? 1 : 0;
                cell.Children.Add(navBar);
                TextBlock icon = new TextBlock();
                icon.Text = glyphs[i];
                icon.FontFamily = Icons;
                icon.FontSize = 15;
                icon.Foreground = index == 0 ? Accent : Muted;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                cell.Children.Add(icon);

                StackPanel labelGroup = new StackPanel();
                labelGroup.Orientation = Orientation.Vertical;
                labelGroup.VerticalAlignment = VerticalAlignment.Center;
                labelGroup.Margin = new Thickness(6, 0, 0, 0);
                labelGroup.Opacity = 0;
                labelGroup.IsHitTestVisible = false;
                TranslateTransform slide = new TranslateTransform(-10, 0);
                labelGroup.RenderTransform = slide;
                TextBlock label = new TextBlock();
                label.Text = labels[i];
                label.FontFamily = Ui;
                label.FontSize = 11.5;
                label.Foreground = index == 0 ? Accent : Sub;
                label.VerticalAlignment = VerticalAlignment.Center;
                labelGroup.Children.Add(label);
                Grid.SetColumn(labelGroup, 1);
                cell.Children.Add(labelGroup);
                item.Child = cell;
                item.ToolTip = labels[i];
                item.MouseLeftButtonDown += delegate
                {
                    item.Background = AccentSoftBrush();
                    icon.Foreground = Accent;
                };
                item.MouseLeave += delegate
                {
                    if (_currentPage == index) return;
                    item.Background = Brushes.Transparent;
                    icon.Foreground = Muted;
                };
                item.MouseLeftButtonUp += delegate { SelectPage(index); };
                icons.Children.Add(item);
                _railItems.Add(item);
                _railIcons.Add(icon);
                _railBars.Add(navBar);
                _railLabels.Add(label);
                _railLabelGroups.Add(labelGroup);
            }
            // 高 DPI 小工作区（例如 1080p @200%）导航会超过可用高度；
            // 这里让导航项自己滚动，保留顶部品牌和底部内存读数固定。
            ScrollViewer iconScroll = new ScrollViewer();
            // 只在高度不足时出现细滚动条，提示还有导航项；正常 900/1080 高度不显示。
            iconScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            iconScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            iconScroll.CanContentScroll = true;
            iconScroll.Content = icons;
            Grid.SetRow(iconScroll, 1);
            grid.Children.Add(iconScroll);

            TextBlock sys = MonoText("MEM\n" + _tm.MemoryPercent.ToString("0") + "%", 7.5, Sub);
            sys.Width = 66;
            sys.HorizontalAlignment = HorizontalAlignment.Left;
            sys.VerticalAlignment = VerticalAlignment.Center;
            sys.TextAlignment = TextAlignment.Center;
            Grid.SetRow(sys, 2);
            grid.Children.Add(sys);
            return rail;
        }

        private void ExpandRail(bool expand)
        {
            if (_rail == null || _railExpanded == expand) return;
            _railExpanded = expand;
            double target = expand ? 208 : 66;
            double from = _rail.ActualWidth > 1 ? _rail.ActualWidth : target;
            DoubleAnimation width = new DoubleAnimation(from, target,
                new Duration(TimeSpan.FromMilliseconds(expand ? 190 : 150)));
            width.EasingFunction = new CubicEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn };
            width.FillBehavior = FillBehavior.HoldEnd;
            _rail.BeginAnimation(FrameworkElement.WidthProperty, width);

            for (int i = 0; i < _railLabelGroups.Count; i++)
            {
                StackPanel group = _railLabelGroups[i];
                DoubleAnimation fade = new DoubleAnimation(expand ? 1 : 0,
                    new Duration(TimeSpan.FromMilliseconds(expand ? 150 : 90)));
                fade.BeginTime = expand ? TimeSpan.FromMilliseconds(55) : TimeSpan.Zero;
                group.BeginAnimation(UIElement.OpacityProperty, fade);
                TranslateTransform slide = group.RenderTransform as TranslateTransform;
                if (slide != null)
                {
                    DoubleAnimation move = new DoubleAnimation(expand ? 0 : -10,
                        new Duration(TimeSpan.FromMilliseconds(expand ? 170 : 110)));
                    move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    slide.BeginAnimation(TranslateTransform.XProperty, move);
                }
            }
        }

        private UIElement Head()
        {
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 6, 0, 0);
            TextBlock big = DisplayText("状态", TitleSize, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 0, 0, 0);
            title.Children.Add(big);
            Grid.SetColumn(title, 0);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock time = DisplayText(DateTime.Now.ToString("HH:mm:ss"), 22, Ink);
            Tag(time, "clock");
            time.FontWeight = FontWeights.Bold;
            time.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(time);

            _perfSaveRow = new StackPanel();
            _perfSaveRow.Orientation = Orientation.Horizontal;
            _perfSaveRow.HorizontalAlignment = HorizontalAlignment.Right;
            _perfSaveRow.Margin = new Thickness(0, 3, 0, 0);
            _perfSaveRow.Visibility = Visibility.Collapsed;
            _perfSaveText = MonoText("已保存当前游戏性能曲线", 8, Accent2);
            _perfSaveText.VerticalAlignment = VerticalAlignment.Center;
            _perfSaveText.Margin = new Thickness(0, 0, 7, 0);
            _perfSaveRow.Children.Add(_perfSaveText);
            _perfSaveButton = CommandButton("\uE74E", "导出", delegate { ExportLastGamePerformance(); }, Accent2);
            _perfSaveButton.Height = 24;
            _perfSaveButton.Margin = new Thickness(0);
            _perfSaveButton.Padding = new Thickness(8, 0, 8, 0);
            _perfSaveRow.Children.Add(_perfSaveButton);
            right.Children.Add(_perfSaveRow);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 2);
            head.Children.Add(line);
            return head;
        }

        private Grid Kpis()
        {
            Grid g = new Grid();
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Kpi(0, "18.4", "GB", "MEMORY USED", "57%", Accent));
            g.Children.Add(Kpi(1, "18", "%", "TOTAL CPU", "8 cores", Accent2));
            g.Children.Add(Kpi(2, "03", "PROC", "TRACKED PROCESSES", "game / tool / os", Ink));
            g.Children.Add(Kpi(3, "120", "HZ", "RENDER RATE", "vsync", Ink));
            return g;
        }

        private UIElement Kpi(int col, string value, string unit, string label, string extra, Brush color)
        {
            Grid cell = new Grid();
            cell.Margin = new Thickness(0, 6, 0, 6);
            if (col > 0)
            {
                Border sep = new Border();
                sep.Width = 1;
                sep.Background = Line;
                sep.HorizontalAlignment = HorizontalAlignment.Left;
                cell.Children.Add(sep);
            }
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(16, 0, 0, 0);
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            TextBlock v = DisplayText(value, 34, color);
            v.FontWeight = FontWeights.Bold;
            row.Children.Add(v);
            TextBlock u = MonoText(" " + unit, 9, Muted);
            u.VerticalAlignment = VerticalAlignment.Bottom;
            u.Margin = new Thickness(0, 0, 0, 7);
            row.Children.Add(u);
            s.Children.Add(row);
            StackPanel meta = new StackPanel();
            meta.Orientation = Orientation.Horizontal;
            meta.Children.Add(MonoText(label, 7.5, Muted));
            TextBlock e = MonoText("   " + extra, 7.5, Sub);
            meta.Children.Add(e);
            s.Children.Add(meta);
            cell.Children.Add(s);
            Grid.SetColumn(cell, col);
            return cell;
        }

        private UIElement Memory()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("MEMORY COMPOSITION", 9, Accent));
            TextBlock total = MonoText("18.4 / 32.0 GB", 9, Sub);
            Grid.SetColumn(total, 1);
            head.Children.Add(total);
            s.Children.Add(head);

            Grid big = new Grid();
            big.Margin = new Thickness(0, 2, 0, 10);
            big.ColumnDefinitions.Add(new ColumnDefinition());
            big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock pct = DisplayText("57", 44, Ink);
            pct.FontWeight = FontWeights.Bold;
            big.Children.Add(pct);
            TextBlock rest = MonoText("%  USED", 10, Muted);
            rest.VerticalAlignment = VerticalAlignment.Bottom;
            rest.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetColumn(rest, 1);
            big.Children.Add(rest);
            s.Children.Add(big);

            StackBar bar = new StackBar(SystemBar, Accent, ToolBar, Line);
            bar.Height = 18;
            bar.Margin = new Thickness(0, 0, 0, 12);
            s.Children.Add(bar);

            Grid legend = new Grid();
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.Children.Add(Legend("系统其他", "9.2 GB", SystemBar));
            Grid.SetColumn(Legend("游戏 SCPSL", "6.8 GB", Accent), 1);
            Grid.SetColumn(Legend("本工具", "0.31 GB", ToolBar), 2);
            Grid.SetColumn(Legend("空闲", "13.6 GB", Muted), 3);
            s.Children.Add(legend);

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.Margin = new Thickness(0, 12, 0, 8);
            s.Children.Add(line);

            s.Children.Add(ProcBar("游戏 SCPSL.exe", 0.37, Accent, "6.8 GB / 21%"));
            s.Children.Add(ProcBar("本工具 SCPSLAlert.exe", 0.011, ToolBar, "0.31 GB / 1%"));
            s.Children.Add(ProcBar("系统与其他", 0.50, SystemBar, "9.2 GB / 29%"));
            return s;
        }

        private UIElement Legend(string name, string value, Brush color, string key = null)
        {
            StackPanel s = new StackPanel();
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            Border sw = new Border();
            sw.Width = 8;
            sw.Height = 8;
            sw.Background = color;
            sw.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(sw);
            TextBlock n = new TextBlock();
            n.Text = "  " + name;
            n.FontSize = 10;
            n.Foreground = Sub;
            row.Children.Add(n);
            s.Children.Add(row);
            TextBlock v = MonoText(value, 9, Ink);
            if (!string.IsNullOrEmpty(key)) Tag(v, key);
            v.Margin = new Thickness(16, 2, 0, 0);
            s.Children.Add(v);
            return s;
        }

        private UIElement ProcBar(string name, double ratio, Brush color, string value)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 10;
            n.Foreground = Sub;
            n.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(n);
            Border track = new Border();
            track.Height = 2;
            track.Background = Line;
            track.VerticalAlignment = VerticalAlignment.Center;
            track.Margin = new Thickness(0, 0, 12, 0);
            Border fill = new Border();
            fill.Width = 380 * ratio;
            fill.Background = color;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            track.Child = fill;
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            TextBlock v = MonoText(value, 8.5, Muted);
            v.VerticalAlignment = VerticalAlignment.Center;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 2);
            g.Children.Add(v);
            return g;
        }

        public void GoToPage(int index)
        {
            SelectPage(index);
        }

        private void OnHoverMove(object sender, MouseEventArgs e)
        {
            // 5 秒锁定期间鼠标移动不抢描边；时间到由 ReleaseHoverLock 统一交还。
            if (_hoverLockTarget != null) return;
            FrameworkElement target = FindHoverTarget();
            if (target == null)
            {
                _hoverCurrent = null;
                if (_hoverFrame != null && _hoverFrame.Visibility == Visibility.Visible && _hoverFrame.Opacity > 0.05)
                    _hoverLeave.Start();
                return;
            }
            if (_hoverLeave != null) _hoverLeave.Stop();
            if (_hoverFrame != null && _hoverFrame.Visibility == Visibility.Visible && _hoverFrame.Opacity > 0.05)
            {
                if (target != _hoverCurrent) MoveHover(target);
            }
            else
            {
                BeginHoverFly(target);
            }
            _hoverCurrent = target;
        }

        private FrameworkElement FindHoverTarget()
        {
            // 同时命中父子卡片时取面积最小的那个。
            // 以前按添加顺序返回，鼠标在内部小卡片上却可能选中外层大卡片，
            // 描边就会比实际卡片大一圈、甚至越出卡片可见边界。
            FrameworkElement best = null;
            double bestArea = double.MaxValue;
            for (int i = 0; i < _hoverTargets.Count; i++)
            {
                FrameworkElement t = _hoverTargets[i];
                if (t == null || !t.IsVisible || !t.IsMouseOver) continue;
                double area = Math.Max(0, t.ActualWidth) * Math.Max(0, t.ActualHeight);
                if (area < bestArea)
                {
                    best = t;
                    bestArea = area;
                }
            }
            return best;
        }

        private Rect HoverRect(FrameworkElement target)
        {
            Point p = target.TranslatePoint(new Point(0, 0), _hoverLayer);
            Rect r = new Rect(p.X, p.Y, target.ActualWidth, target.ActualHeight);
            r.Inflate(-1, -1);
            return r;
        }

        private void BeginHoverFly(FrameworkElement target)
        {
            SetHoverCut(target);
            RevealHover(HoverRect(target));
        }

        private void RevealHover(Rect rect)
        {
            _hoverAnimating = false;
            if (_hoverSettle != null) _hoverSettle.Stop();
            _hoverRect = rect;
            _hoverFrame.BeginAnimation(UIElement.OpacityProperty, null);
            _hoverFrame.BeginAnimation(Canvas.LeftProperty, null);
            _hoverFrame.BeginAnimation(Canvas.TopProperty, null);
            _hoverFrame.BeginAnimation(FrameworkElement.WidthProperty, null);
            _hoverFrame.BeginAnimation(FrameworkElement.HeightProperty, null);
            _hoverFrame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_hoverFrame, rect.Left);
            Canvas.SetTop(_hoverFrame, rect.Top);
            _hoverFrame.Width = rect.Width;
            _hoverFrame.Height = rect.Height;
            _hoverFrame.Opacity = 0;
            DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _hoverFrame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void MoveHover(FrameworkElement target)
        {
            // 保留跨格子的矩形飞行，但飞行层只存在于右侧内容列，
            // 不会再覆盖左侧导航栏，也不会飞出内容区域。
            Rect to = HoverRect(target);
            double fromLeft = Canvas.GetLeft(_hoverFrame);
            double fromTop = Canvas.GetTop(_hoverFrame);
            double fromWidth = _hoverFrame.Width;
            double fromHeight = _hoverFrame.Height;
            if (double.IsNaN(fromLeft) || double.IsNaN(fromTop)
                || double.IsNaN(fromWidth) || double.IsNaN(fromHeight))
            {
                fromLeft = _hoverRect.Left;
                fromTop = _hoverRect.Top;
                fromWidth = _hoverRect.Width;
                fromHeight = _hoverRect.Height;
            }

            _hoverRect = to;
            SetHoverCut(target);
            _hoverAnimating = true;
            if (_hoverSettle != null)
            {
                _hoverSettle.Stop();
                _hoverSettle.Start();
            }

            // 先把基值放到目标位置；动画只负责这段飞行过程，
            // 结束后 FillBehavior.Stop 会回到基值，再由 settle 定时器贴死。
            _hoverFrame.BeginAnimation(UIElement.OpacityProperty, null);
            _hoverFrame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_hoverFrame, to.Left);
            Canvas.SetTop(_hoverFrame, to.Top);
            _hoverFrame.Width = to.Width;
            _hoverFrame.Height = to.Height;
            _hoverFrame.Opacity = 1;

            int ms = 320;
            Anim(_hoverFrame, Canvas.LeftProperty, fromLeft, to.Left, ms);
            Anim(_hoverFrame, Canvas.TopProperty, fromTop, to.Top, ms);
            Anim(_hoverFrame, FrameworkElement.WidthProperty, fromWidth, to.Width, ms);
            Anim(_hoverFrame, FrameworkElement.HeightProperty, fromHeight, to.Height, ms);
        }

        private static void Anim(UIElement element, DependencyProperty property, double from, double to, int ms)
        {
            DoubleAnimation a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
            a.FillBehavior = FillBehavior.Stop;
            element.BeginAnimation(property, a);
        }

        /// <summary>
        /// 卡片位置会因为窗口缩放、滚动、数据刷新而移动。鼠标不动时也要把描边拉回卡片上，
        /// 否则会留下一个比卡片偏上/偏左的“旧框”。
        /// </summary>
        private void SyncHoverToTarget()
        {
            if (_hoverSyncing || _hoverAnimating) return;
            if (_hoverFrame == null || _hoverFrame.Visibility != Visibility.Visible) return;
            if (_hoverCurrent == null || !_hoverCurrent.IsVisible) return;

            Rect r;
            try { r = HoverRect(_hoverCurrent); }
            catch { return; }

            if (Math.Abs(r.Left - _hoverRect.Left) < 0.5
                && Math.Abs(r.Top - _hoverRect.Top) < 0.5
                && Math.Abs(r.Width - _hoverRect.Width) < 0.5
                && Math.Abs(r.Height - _hoverRect.Height) < 0.5)
                return;

            _hoverSyncing = true;
            try
            {
                _hoverRect = r;
                Canvas.SetLeft(_hoverFrame, r.Left);
                Canvas.SetTop(_hoverFrame, r.Top);
                _hoverFrame.Width = r.Width;
                _hoverFrame.Height = r.Height;
            }
            finally
            {
                _hoverSyncing = false;
            }
        }

        private void SetHoverCut(FrameworkElement target)
        {
            VigilCard card = target as VigilCard;
            _hoverFrame.Cut = card != null ? card.Cut : 0;
            ApplyHoverClip(target);
        }

        /// <summary>
        /// 卡片在 ScrollViewer 里时，描边只能画到可视区域底部。
        /// 之前描边层覆盖整个内容区，卡片下半截滚出视口后，描边仍会一直画到页脚，
        /// 看起来就是越过了粉色那条卡片底线。这里把悬停层裁到目标所在 ScrollViewer 的视口。
        /// </summary>
        private void ApplyHoverClip(FrameworkElement target)
        {
            if (_hoverLayer == null || _hoverFrame == null) return;

            ScrollViewer scroll = null;
            try
            {
                DependencyObject node = VisualTreeHelper.GetParent(target);
                while (node != null)
                {
                    ScrollViewer sv = node as ScrollViewer;
                    if (sv != null) { scroll = sv; break; }
                    node = VisualTreeHelper.GetParent(node);
                }
            }
            catch { }

            if (scroll == null)
            {
                _hoverLayer.Clip = null;
                return;
            }

            try
            {
                Point p = scroll.TranslatePoint(new Point(0, 0), _hoverLayer);
                Rect clip = new Rect(p.X, p.Y, Math.Max(0, scroll.ActualWidth), Math.Max(0, scroll.ActualHeight));
                Rect layer = new Rect(0, 0, Math.Max(0, _hoverLayer.ActualWidth), Math.Max(0, _hoverLayer.ActualHeight));
                clip.Intersect(layer);
                if (clip.IsEmpty || clip.Width <= 0 || clip.Height <= 0)
                {
                    _hoverLayer.Clip = new RectangleGeometry(new Rect(0, 0, 0, 0));
                    return;
                }
                RectangleGeometry geo = new RectangleGeometry(clip);
                geo.Freeze();
                _hoverLayer.Clip = geo;
            }
            catch
            {
                _hoverLayer.Clip = null;
            }
        }

        private void HookPageScroll(ScrollViewer page)
        {
            if (page == null) return;
            page.ScrollChanged += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshHoverAfterScroll));
            };
        }
        private void RefreshHoverAfterScroll()
        {
            if (_hoverFrame == null || _hoverFrame.Visibility != Visibility.Visible) return;
            if (_hoverAnimating) return;
            // 锁定期间滚动：描边必须继续跟着被锁定的卡片走，
            // 不能因为鼠标恰好压在别的卡片上而被抢走。
            if (_hoverLockTarget != null)
            {
                _hoverCurrent = _hoverLockTarget;
                if (_hoverLockTarget.IsVisible) SnapHover();
                return;
            }
            FrameworkElement target = FindHoverTarget();
            if (target != null) _hoverCurrent = target;
            if (_hoverCurrent != null) SnapHover();
        }
        private void SnapHover()
        {
            if (_hoverCurrent == null) return;
            Rect rect = HoverRect(_hoverCurrent);
            _hoverRect = rect;
            Canvas.SetLeft(_hoverFrame, rect.Left);
            Canvas.SetTop(_hoverFrame, rect.Top);
            _hoverFrame.Width = rect.Width;
            _hoverFrame.Height = rect.Height;
            SetHoverCut(_hoverCurrent);
        }

        private void FadeHover()
        {
            if (_hoverFrame == null || _hoverFrame.Visibility != Visibility.Visible) return;
            _hoverAnimating = false;
            if (_hoverSettle != null) _hoverSettle.Stop();
            if (_hoverLayer != null) _hoverLayer.Clip = null;
            DoubleAnimation fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(320)));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            fade.Completed += delegate
            {
                _hoverFrame.Visibility = Visibility.Collapsed;
                _hoverFrame.Opacity = 0;
                _hoverCurrent = null;
            };
            _hoverFrame.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// 把描边锁到某张卡片上 5 秒：跳转后先让用户看清目标卡片在哪，
        /// 之后自动交还给“鼠标指哪画哪”的原有逻辑。
        /// </summary>
        private void LockHoverTo(FrameworkElement target)
        {
            if (target == null || _hoverFrame == null || !target.IsVisible) return;
            if (_hoverLockTimer == null) return;
            _hoverLockTarget = target;
            _hoverCurrent = target;
            _hoverAnimating = false;
            if (_hoverSettle != null) _hoverSettle.Stop();
            if (_hoverLeave != null) _hoverLeave.Stop();
            BeginHoverFly(target);
            SnapHover();
            _hoverLockTimer.Stop();
            _hoverLockTimer.Start();
        }

        /// <summary>5 秒到：解除锁定，按鼠标当前实际位置恢复悬停描边。</summary>
        private void ReleaseHoverLock()
        {
            if (_hoverLockTimer != null) _hoverLockTimer.Stop();
            if (_hoverLockTarget == null) return;
            _hoverLockTarget = null;
            FrameworkElement target = FindHoverTarget();
            if (target == null)
            {
                // 鼠标已不在任何卡片上：和正常移出一样淡出。
                FadeHover();
                return;
            }
            if (_hoverFrame != null && _hoverFrame.Visibility == Visibility.Visible)
            {
                if (target != _hoverCurrent) MoveHover(target);
            }
            else
            {
                BeginHoverFly(target);
            }
            _hoverCurrent = target;
        }

        /// <summary>切页/重建时立刻撤销锁定，不留下一个指向旧页面的描边。</summary>
        private void StopHoverLock()
        {
            if (_hoverLockTimer != null) _hoverLockTimer.Stop();
            _hoverLockTarget = null;
        }

        private void SelectPage(int index)
        {
            if (_orbit != null) _orbit.Pulse();
            StopHoverLock();
            if (_hoverFrame != null)
            {
                _hoverCurrent = null;
                _hoverAnimating = false;
                if (_hoverSettle != null) _hoverSettle.Stop();
                if (_hoverLayer != null) _hoverLayer.Clip = null;
                _hoverFrame.Visibility = Visibility.Collapsed;
                _hoverFrame.Opacity = 0;
                _hoverFrame.BeginAnimation(UIElement.OpacityProperty, null);
            }
            if (_flyBar != null) _flyBar.Visibility = Visibility.Collapsed;
            if (_flyTrail != null) _flyTrail.Visibility = Visibility.Collapsed;
            if (index > AppearancePageIndex) return;
            _currentPage = index;
            if (index != AppearancePageIndex) _appearanceDetailOpen = false;
            _statusPage.Visibility = Visibility.Collapsed;
            if (_perfPage != null) _perfPage.Visibility = Visibility.Collapsed;
            _generalPage.Visibility = Visibility.Collapsed;
            _alertPage.Visibility = Visibility.Collapsed;
            _audioPage.Visibility = Visibility.Collapsed;
            _timePage.Visibility = Visibility.Collapsed;
            _logPage.Visibility = Visibility.Collapsed;
            _hotkeyPage.Visibility = Visibility.Collapsed;
            _toolsPage.Visibility = Visibility.Collapsed;
            if (_chBenchPage != null) _chBenchPage.Visibility = Visibility.Collapsed;
            _chBenchOpen = false;
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Collapsed;
            if (_appearancePage != null) _appearancePage.Visibility = Visibility.Collapsed;
            if (_appearanceDetailPage != null) _appearanceDetailPage.Visibility = Visibility.Collapsed;
            if (_cardDesignPage != null) _cardDesignPage.Visibility = Visibility.Collapsed;
            if (_advDetailPage != null) _advDetailPage.Visibility = Visibility.Collapsed;
            for (int i = 0; i < _railItems.Count; i++)
            {
                bool selected = i < PageOrder.Length && PageOrder[i] == index;
                _railBars[i].Opacity = selected ? 1 : 0;
                _railIcons[i].Foreground = selected ? Accent : Muted;
                if (i < _railLabels.Count) _railLabels[i].Foreground = selected ? Accent : Sub;
                _railItems[i].Background = selected ? PanelAlt : Brushes.Transparent;
            }
            if (index == 1)
            {
                if (_generalPage.Content == null) _generalPage.Content = BuildGeneralPage();
                _generalPage.Visibility = Visibility.Visible;
                _logPage.Visibility = Visibility.Collapsed;
                _alertPage.Visibility = Visibility.Collapsed;
                _audioPage.Visibility = Visibility.Collapsed;
                _statusPage.Visibility = Visibility.Collapsed;
            }
            else if (index == 2)
            {
                if (_alertPage.Content == null) _alertPage.Content = BuildAlertPage();
                _alertPage.Visibility = Visibility.Visible;
                _generalPage.Visibility = Visibility.Collapsed;
                _logPage.Visibility = Visibility.Collapsed;
                _audioPage.Visibility = Visibility.Collapsed;
                _statusPage.Visibility = Visibility.Collapsed;
            }
            else if (index == 3)
            {
                if (_audioPage.Content == null) _audioPage.Content = BuildAudioPage();
                _audioPage.Visibility = Visibility.Visible;
                _generalPage.Visibility = Visibility.Collapsed;
                _alertPage.Visibility = Visibility.Collapsed;
                _logPage.Visibility = Visibility.Collapsed;
                _statusPage.Visibility = Visibility.Collapsed;
            }
            else if (index == 4)
            {
                if (_timePage.Content == null) _timePage.Content = BuildTimePage();
                _timePage.Visibility = Visibility.Visible;
            }
            else if (index == 5)
            {
                if (_hotkeyPage.Content == null) _hotkeyPage.Content = BuildHotkeyPage();
                _hotkeyPage.Visibility = Visibility.Visible;
            }
            else if (index == 7)
            {
                if (_toolsPage.Content == null) _toolsPage.Content = BuildToolsPage();
                _toolsPage.Visibility = Visibility.Visible;
            }
            else if (index == AppearancePageIndex)
            {
                if (_appearancePage.Content == null) _appearancePage.Content = BuildAppearancePage();
                if (_cardDesignOpen && _cardDesignPage != null)                 {                     _appearancePage.Visibility = Visibility.Collapsed;                     _cardDesignPage.Visibility = Visibility.Visible;                 }                 else
                _appearancePage.Visibility = Visibility.Visible;
            }
            else if (index == AdvancedPageIndex)
            {
                if (_advancedPage.Content == null) _advancedPage.Content = BuildAdvancedPage();
                _advancedPage.Visibility = Visibility.Visible;
            }
            else if (index == 6)
            {
                if (_logPage.Content == null) _logPage.Content = BuildLogPage();
                _logPage.Visibility = Visibility.Visible;
                _generalPage.Visibility = Visibility.Collapsed;
                _alertPage.Visibility = Visibility.Collapsed;
                _audioPage.Visibility = Visibility.Collapsed;
                _statusPage.Visibility = Visibility.Collapsed;
                UpdateLogTail();
            }
            else
            {
                _generalPage.Visibility = Visibility.Collapsed;
                _logPage.Visibility = Visibility.Collapsed;
                _alertPage.Visibility = Visibility.Collapsed;
                _audioPage.Visibility = Visibility.Collapsed;
                _statusPage.Visibility = Visibility.Visible;
            }
        }

        private void OnStatusPageMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (InsideButton(source)) return;
            if (InsideLegend(source)) return;
            e.Handled = true;
            ShowPerformance();
        }

        /// <summary>
        /// WinForms + WPF 混合消息循环下，鼠标点 TextBox 有时不会自动拿到键盘焦点。
        /// 这里统一补一刀：点到文本框就显式激活窗口并聚焦，避免“看起来能点、但打不了字”。
        /// </summary>
        private void OnVigilPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && !(node is TextBox))
                node = VisualTreeHelper.GetParent(node);
            TextBox box = node as TextBox;
            if (box == null) return;
            try
            {
                if (!IsActive) Activate();
                if (!box.IsKeyboardFocusWithin)
                {
                    box.Focus();
                    Keyboard.Focus(box);
                }
            }
            catch { }
        }

        private static bool InsideLegend(DependencyObject source)
        {
            DependencyObject node = source;
            while (node != null)
            {
                FrameworkElement element = node as FrameworkElement;
                if (element != null && (element.Tag as string) == "legend") return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private void ShowPerformance()
        {
            if (_perfPage == null) return;
            if (!_perfBuilt)
            {
                _perfPage.Content = BuildPerformancePage();
                CollectTags(_perfPage);
                _perfBuilt = true;
                ApplySeriesVisibility();
                UpdateTemperatureVisibility();
            }
            if (_orbit != null) _orbit.Pulse();
            _statusPage.Visibility = Visibility.Collapsed;
            _generalPage.Visibility = Visibility.Collapsed;
            _logPage.Visibility = Visibility.Collapsed;
            _alertPage.Visibility = Visibility.Collapsed;
            _audioPage.Visibility = Visibility.Collapsed;
            _timePage.Visibility = Visibility.Collapsed;
            _hotkeyPage.Visibility = Visibility.Collapsed;
            _toolsPage.Visibility = Visibility.Collapsed;
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Collapsed;
            _perfPage.Visibility = Visibility.Visible;
            _perfPage.ScrollToTop();
            _currentPage = 9;
            UpdateRailSelection(0);
            ClearHover();
            UpdateTelemetry();
            UpdatePerformance();
            RefreshPerformanceRecords();
        }

        private void ShowStatus()
        {
            if (_perfPage != null) _perfPage.Visibility = Visibility.Collapsed;
            _generalPage.Visibility = Visibility.Collapsed;
            _logPage.Visibility = Visibility.Collapsed;
            _alertPage.Visibility = Visibility.Collapsed;
            _audioPage.Visibility = Visibility.Collapsed;
            _timePage.Visibility = Visibility.Collapsed;
            _hotkeyPage.Visibility = Visibility.Collapsed;
            _toolsPage.Visibility = Visibility.Collapsed;
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Collapsed;
            if (_appearancePage != null) _appearancePage.Visibility = Visibility.Collapsed;
            if (_appearanceDetailPage != null) _appearanceDetailPage.Visibility = Visibility.Collapsed;
            if (_advDetailPage != null) _advDetailPage.Visibility = Visibility.Collapsed;
            _statusPage.Visibility = Visibility.Visible;
            _currentPage = 0;
            UpdateRailSelection(0);
            ClearHover();
        }

        private void UpdateRailSelection(int index)
        {
            for (int i = 0; i < _railItems.Count; i++)
            {
                bool selected = i < PageOrder.Length && PageOrder[i] == index;
                _railBars[i].Opacity = selected ? 1 : 0;
                _railIcons[i].Foreground = selected ? Accent : Muted;
                if (i < _railLabels.Count) _railLabels[i].Foreground = selected ? Accent : Sub;
                _railItems[i].Background = selected ? PanelAlt : Brushes.Transparent;
            }
        }

        private void ClearHover()
        {
            if (_hoverFrame == null) return;
            StopHoverLock();
            _hoverCurrent = null;
            _hoverFrame.Visibility = Visibility.Collapsed;
            _hoverFrame.Opacity = 0;
            _hoverFrame.BeginAnimation(UIElement.OpacityProperty, null);
            if (_flyBar != null) _flyBar.Visibility = Visibility.Collapsed;
            if (_flyTrail != null) _flyTrail.Visibility = Visibility.Collapsed;
        }

        private void LoadSeriesSelection()
        {
            _showCpu = true;
            _showMem = true;
            _showGpu = true;
            _showNet = true;
            _showDisk = true;
            _showTemp = true;
            string raw = _cfg != null ? _cfg.StatusSeries : "";
            if (string.IsNullOrEmpty(raw)) return;
            raw = raw.Trim();
            if (raw.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                _showCpu = false;
                _showMem = false;
                _showGpu = false;
                _showNet = false;
                _showDisk = false;
                _showTemp = false;
                return;
            }
            _showCpu = false;
            _showMem = false;
            _showGpu = false;
            _showNet = false;
            _showDisk = false;
            _showTemp = false;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim().ToLowerInvariant();
                if (key == "cpu") _showCpu = true;
                else if (key == "mem") _showMem = true;
                else if (key == "gpu") _showGpu = true;
                else if (key == "net") _showNet = true;
                else if (key == "disk") _showDisk = true;
                else if (key == "temp") _showTemp = true;
            }
        }

        private bool SeriesOn(string key)
        {
            switch (key)
            {
                case "cpu": return _showCpu;
                case "mem": return _showMem;
                case "gpu": return _showGpu;
                case "net": return _showNet;
                case "disk": return _showDisk;
                case "temp": return _showTemp;
                default: return true;
            }
        }

        private void ToggleSeries(string key)
        {
            switch (key)
            {
                case "cpu": _showCpu = !_showCpu; break;
                case "mem": _showMem = !_showMem; break;
                case "gpu": _showGpu = !_showGpu; break;
                case "net": _showNet = !_showNet; break;
                case "disk": _showDisk = !_showDisk; break;
                case "temp": _showTemp = !_showTemp; break;
                default: return;
            }
            if (_cfg != null) _cfg.StatusSeries = SerializeSeries();
            ApplySeriesVisibility();
            UpdateTemperatureVisibility();
            TouchSave();
        }

        private string SerializeSeries()
        {
            List<string> parts = new List<string>();
            if (_showCpu) parts.Add("cpu");
            if (_showMem) parts.Add("mem");
            if (_showGpu) parts.Add("gpu");
            if (_showNet) parts.Add("net");
            if (_showDisk) parts.Add("disk");
            if (_showTemp) parts.Add("temp");
            if (parts.Count == 0) return "none";
            return string.Join(",", parts.ToArray());
        }

        private void ApplySeriesVisibility()
        {
            if (_history != null)
            {
                _history.ShowCpu = _showCpu;
                _history.ShowMem = _showMem;
                _history.ShowGpu = _showGpu;
                _history.ShowNet = _showNet;
                _history.ShowDisk = _showDisk;
                _history.ShowTemperature = _showTemp;
                _history.InvalidateVisual();
            }
            if (_perfChart != null)
            {
                _perfChart.ShowCpu = _showCpu;
                _perfChart.ShowMem = _showMem;
                _perfChart.ShowGpu = _showGpu;
                _perfChart.ShowNet = _showNet;
                _perfChart.ShowDisk = _showDisk;
                _perfChart.ShowTemperature = _showTemp;
                _perfChart.InvalidateVisual();
            }
            foreach (KeyValuePair<string, List<FrameworkElement>> pair in _legendChips)
            {
                double opacity = SeriesOn(pair.Key) ? 1.0 : 0.32;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    FrameworkElement chip = pair.Value[i];
                    if (chip != null) chip.Opacity = opacity;
                }
            }
        }

        private void UpdateTemperatureVisibility()
        {
            bool available = _tm.TemperatureAvailable;
            if (available) _historyData.FillTemperatureIfZero(_tm.TemperatureCelsius);
            if (_history != null) _history.TemperatureAvailable = available;
            if (_perfChart != null) _perfChart.TemperatureAvailable = available;
            Visibility visibility = available ? Visibility.Visible : Visibility.Collapsed;
            if (_tempLegend != null) _tempLegend.Visibility = visibility;
            if (_perfTempLegend != null) _perfTempLegend.Visibility = visibility;
            if (_perfTempRow != null) _perfTempRow.Visibility = visibility;
        }

        /// <summary>把 PresentMon 后台线程算好的 60 个/秒采样抄到曲线用的列表里。</summary>
        private void SyncGameFpsHistory()
        {
            double[] hist = _gameFps.HistorySnapshot();
            _gameFpsHistory.Clear();
            for (int i = 0; i < hist.Length; i++) _gameFpsHistory.Add(hist[i]);
        }

        /// <summary>
        /// 记录"这一局"的性能：进程出现开始，进程退出结束。
        /// 每秒一个点，最长保留 6 小时；退出时自动存一份 PNG，同时把数据留在内存里供再次导出。
        /// </summary>
        private void TrackGamePerformance(GameFpsStats gf, VigilSnapshot snap)
        {
            DateTime now = DateTime.Now;
            if (_tm.GameRunning && _livePerf == null)
            {
                StartMatchRecord(now, snap);
                _livePerf = new GamePerfSession();
                _livePerf.Start = now;
                _livePerf.Pid = _tm.GamePid;
                _livePerf.GameTitle = _tm.GameTitle;
                _lastPerfSampleAt = DateTime.MinValue;
                _lastSelfDead = false;
                _lastWarhead = false;
                _lastDeadman = false;
                _lastWaveAt = DateTime.MinValue;
                _openWarheadEvent = null;
                _openDeadmanEvent = null;
            }

            if (!_tm.GameRunning && _livePerf != null)
            {
                _livePerf.End = now;
                CloseDurationEvent(_livePerf, _openWarheadEvent, now);
                CloseDurationEvent(_livePerf, _openDeadmanEvent, now);
                _openWarheadEvent = null;
                _openDeadmanEvent = null;

                bool keep = _livePerf.EnteredServer && _livePerf.DurationSeconds > 180
                    && _livePerf.Samples.Count > 0;
                if (keep)
                {
                    _lastPerf = _livePerf;
                    _lastPerfSavedPath = SaveManagedPerformance(_lastPerf, true);
                    Logger.On("性能", "游戏结束：本局性能曲线已自动记录（" + _lastPerf.Samples.Count + " 个采样点，"
                        + ClockText(_lastPerf.DurationSeconds) + (_lastPerfSavedPath != null ? "，保存到 " + _lastPerfSavedPath : "，保存失败") + "）");
                }
                else
                {
                    _lastPerf = null;
                    _lastPerfSavedPath = null;
                    Logger.On("性能", "游戏结束：本局未自动记录（需要进入过服务器且游戏时长 > 3 分钟；本次 "
                        + ClockText(_livePerf.DurationSeconds) + (_livePerf.EnteredServer ? "，已进服" : "，未进服") + "）");
                }
                EndMatchRecord(now);
                _livePerf = null;
                UpdateGamePerfNotice();
                RefreshPerformanceRecords();
            }

            if (_tm.GameRunning && _livePerf != null)
            {
                if (snap != null)
                {
                    if (snap.InServer) _livePerf.EnteredServer = true;
                    IngestEvents(_livePerf, snap, now);
                }

                if (_lastPerfSampleAt == DateTime.MinValue || (now - _lastPerfSampleAt).TotalSeconds >= 0.9)
                {
                    GamePerfSample q = new GamePerfSample();
                    q.At = now;
                    q.HasFps = gf.HasData && gf.AverageFps > 0;
                    q.Fps = gf.AverageFps;
                    q.LowFps = gf.LowFps;
                    q.Cpu = _tm.CpuPercent;
                    q.GameCpu = _tm.GameCpuPercent;
                    q.Gpu = _tm.GpuPercent;
                    q.GameGpu = _tm.GameGpuPercent;
                    q.GpuAvailable = _tm.GpuAvailable;
                    q.MemPct = _tm.MemoryPercent;
                    q.GameMemMb = _tm.GameMemoryMb;
                    q.Disk = _tm.DiskBusyPercent;
                    q.Net = _tm.NetworkMbps;
                    q.Temp = _tm.TemperatureCelsius;
                    q.TempAvailable = _tm.TemperatureAvailable;
                    _livePerf.Samples.Add(q);
                    if (_livePerf.Samples.Count > 21600) _livePerf.Samples.RemoveAt(0);   // 6 小时
                    CrashTelemetry.Push(q);
                    SampleMatchRecord(snap, q, now);
                    _lastPerfSampleAt = now;
                }
            }
        }

        /// <summary>把这一秒快照里的游戏事件并进本局记录（波次 / 自己死亡 / 重生 / 核弹 / 终焉）。</summary>
        private void IngestEvents(GamePerfSession s, VigilSnapshot snap, DateTime now)
        {
            // ---- 刷新增援 ----
            for (int i = 0; i < snap.Waves.Count; i++)
            {
                VigilWave w = snap.Waves[i];
                if (w.At < s.Start) continue;
                string label = (w.IsSmall ? "小波 " : "大波 ") + TacticalLog.FactionName(w.Faction);
                bool found = false;
                for (int k = 0; k < s.Events.Count; k++)
                {
                    GamePerfEvent old = s.Events[k];
                    if (old.Kind == "wave" && old.At == w.At)
                    {
                        old.Label = label;      // 手动切换大小波后，报告里的标签也跟着改
                        found = true;
                        break;
                    }
                }
                if (found) continue;
                if (w.At <= _lastWaveAt) continue;
                GamePerfEvent e = new GamePerfEvent();
                e.At = w.At;
                e.Kind = "wave";
                e.Label = label;
                s.Events.Add(e);
                _lastWaveAt = w.At;
            }

            // ---- 自己死亡 / 重生 ----
            if (snap.SelfDead != _lastSelfDead)
            {
                GamePerfEvent e = new GamePerfEvent();
                e.At = now;
                e.Kind = snap.SelfDead ? "death" : "respawn";
                e.Label = snap.SelfDead ? "自己死亡" : "自己重生";
                s.Events.Add(e);
                _lastSelfDead = snap.SelfDead;
            }

            // ---- 核弹（持续段）----
            if (snap.WarheadRunning && !_lastWarhead)
            {
                GamePerfEvent e = new GamePerfEvent();
                double total = snap.WarheadTotal > 0 ? snap.WarheadTotal : 90;
                double passed = Math.Max(0, total - snap.WarheadRemaining);
                e.At = now.AddSeconds(-passed);
                if (e.At < s.Start) e.At = s.Start;
                e.Kind = "warhead";
                e.Label = "核弹启动";
                s.Events.Add(e);
                _openWarheadEvent = e;
            }
            if (!snap.WarheadRunning && _lastWarhead)
            {
                CloseDurationEvent(s, _openWarheadEvent, now);
                _openWarheadEvent = null;
            }
            _lastWarhead = snap.WarheadRunning;

            // ---- 亡者协议（持续段）----
            if (snap.DeadManActive && !_lastDeadman)
            {
                GamePerfEvent e = new GamePerfEvent();
                double total = snap.DeadManTotal > 0 ? snap.DeadManTotal : 150;
                double passed = Math.Max(0, total - snap.DeadManRemaining);
                e.At = now.AddSeconds(-passed);
                if (e.At < s.Start) e.At = s.Start;
                e.Kind = "deadman";
                e.Label = "终焉 / 亡者协议";
                s.Events.Add(e);
                _openDeadmanEvent = e;
            }
            if (!snap.DeadManActive && _lastDeadman)
            {
                CloseDurationEvent(s, _openDeadmanEvent, now);
                _openDeadmanEvent = null;
            }
            _lastDeadman = snap.DeadManActive;
        }

        private static void CloseDurationEvent(GamePerfSession s, GamePerfEvent e, DateTime now)
        {
            if (e == null) return;
            e.EndAt = now;
            if (e.EndAt < e.At) e.EndAt = e.At;
            double sec = (e.EndAt - e.At).TotalSeconds;
            if (e.Kind == "warhead") e.Label = "核弹 " + sec.ToString("0") + "s";
            else if (e.Kind == "deadman") e.Label = "终焉 " + sec.ToString("0") + "s";
        }

        // ============================================================
        //  一图流：对局记录（与性能曲线同一秒采样）
        // ============================================================
        /// <summary>当前是否正在记录这一局（给“一图流”窗口取实时数据）。</summary>
        public bool HasLiveMatch { get { return _match != null; } }

        /// <summary>取一份当前对局的快照；不在对局中返回 null。</summary>
        public MatchRecord LiveMatchSnapshot()
        {
            try { return _match != null ? _match.Clone() : null; }
            catch { return null; }
        }

        private void StartMatchRecord(DateTime now, VigilSnapshot snap)
        {
            try
            {
                _match = new MatchRecord();
                _match.Id = MatchStore.NewId(now);
                _match.Start = now;
                _match.Pid = _tm.GamePid;
                _match.GameTitle = _tm.GameTitle;
                _match.ExeVersion = AppInfo.Version;

                DateTime roundStart = snap != null ? snap.RoundStart : DateTime.MinValue;
                if (roundStart == DateTime.MinValue || roundStart > now || (now - roundStart).TotalMinutes > 30)
                    roundStart = now;

                MatchRound r = new MatchRound();
                r.Index = 1;
                r.Start = roundStart;
                _match.Rounds.Add(r);
                _matchRoundStart = roundStart;

                _deadmanEvent = null;
                _warheadEvent = null;
                _lastDeadmanEnd = DateTime.MinValue;
                _lastMiniWaveAt = DateTime.MinValue;
                _lastDeadmanActive = false;
                _lastWarheadRunning = false;
                _lastMatchRole = snap != null ? snap.Role : "";
                _lastMatchState = "alive";
                AddMatchRole(r, _lastMatchRole, _lastMatchState, now, "开局");
            }
            catch (Exception ex)
            {
                Logger.Err("一图流", "创建对局记录失败", ex);
                _match = null;
            }
        }

        private void EnsureMatchRound(VigilSnapshot snap, DateTime now)
        {
            if (_match == null || snap == null) return;
            DateTime roundStart = snap.RoundStart;
            if (roundStart == DateTime.MinValue) return;
            if (_matchRoundStart != DateTime.MinValue
                && Math.Abs((roundStart - _matchRoundStart).TotalSeconds) < 2) return;
            if (roundStart < _match.Start) return;

            _match.CloseRound(now);
            MatchRound r = new MatchRound();
            r.Index = _match.Rounds.Count + 1;
            r.Start = roundStart;
            _match.Rounds.Add(r);
            _matchRoundStart = roundStart;
            _lastMatchRole = snap.Role;
            _lastMatchState = snap.SelfDead ? "dead" : "alive";
            AddMatchRole(r, _lastMatchRole, _lastMatchState, roundStart, "回合开始");
        }

        private void SampleMatchRecord(VigilSnapshot snap, GamePerfSample q, DateTime now)
        {
            if (_match == null) return;
            EnsureMatchRound(snap, now);
            MatchRound r = _match.CurrentRound;
            if (r == null) return;

            MatchSample s = new MatchSample();
            s.At = now;
            s.Second = (int)Math.Max(0, (now - r.Start).TotalSeconds);
            s.HasFps = q.HasFps;
            s.Fps = q.Fps;
            s.Cpu = q.Cpu;
            s.Gpu = q.Gpu;
            s.HasTemp = q.TempAvailable;
            s.Temp = q.Temp;
            int bpm = 0;
            if (_cfg == null || _cfg.MatchRecordHeart)
            {
                try { bpm = HeartRateHub.Bpm; } catch { }
            }
            s.Bpm = bpm;

            string role = snap != null ? snap.Role : "";
            string state = snap != null && !string.IsNullOrEmpty(snap.SelfState)
                ? snap.SelfState
                : ((snap != null && snap.SelfDead) ? "dead" : "alive");
            s.Role = role;
            s.State = state;

            if (!string.Equals(role, _lastMatchRole, StringComparison.Ordinal)
                || !string.Equals(state, _lastMatchState, StringComparison.Ordinal))
            {
                string note = state == "dead" ? "阵亡 / 观战"
                    : (state != _lastMatchState ? "复活" : "角色切换");
                AddMatchRole(r, role, state, now, note);
                _lastMatchRole = role;
                _lastMatchState = state;
            }

            if (snap != null)
            {
                for (int i = 0; i < snap.Scps.Count; i++)
                {
                    VigilScpState v = snap.Scps[i];
                    if (string.IsNullOrEmpty(v.Code)) continue;
                    ScpPresence p = r.FindScp(v.Code);
                    if (p == null)
                    {
                        p = new ScpPresence();
                        p.Code = v.Code;
                        r.Scps.Add(p);
                    }
                    if (!p.Confirmed && (v.Present || v.Killed))
                    {
                        p.Confirmed = true;
                        p.Source = "scan";
                        p.FirstSeen = v.Present ? now
                            : (v.ChangedAt != DateTime.MinValue ? v.ChangedAt : now);
                    }
                    if (v.Killed && p.ContainedAt == DateTime.MinValue)
                        p.ContainedAt = v.ChangedAt != DateTime.MinValue ? v.ChangedAt : now;
                    if (role.IndexOf("079", StringComparison.Ordinal) >= 0) p.WasPlayerRole = true;
                }
            }

            // 持续事件：终焉（小波 → 激活）与核弹（激活 → 引爆），首尾相接
            if (snap != null)
            {
                for (int i = 0; i < snap.Waves.Count; i++)
                {
                    VigilWave w = snap.Waves[i];
                    if (!w.IsSmall) continue;
                    if (_lastMiniWaveAt != DateTime.MinValue && w.At <= _lastMiniWaveAt) continue;
                    _lastMiniWaveAt = w.At;
                    DateTime start = w.At < r.Start ? r.Start : w.At;
                    _deadmanEvent = new MatchEvent();
                    _deadmanEvent.At = start;
                    _deadmanEvent.Kind = "deadman";
                    _deadmanEvent.Label = "终焉 等待";
                    _deadmanEvent.Source = "wave";
                    r.Events.Add(_deadmanEvent);
                }

                if (_lastDeadmanActive && !snap.DeadManActive && _deadmanEvent != null)
                {
                    _deadmanEvent.EndAt = now;
                    double sec = Math.Max(1, (now - _deadmanEvent.At).TotalSeconds);
                    _deadmanEvent.Label = "终焉 " + sec.ToString("0") + "s";
                    _lastDeadmanEnd = now;
                    _deadmanEvent = null;
                }
                _lastDeadmanActive = snap.DeadManActive;

                if (!_lastWarheadRunning && snap.WarheadRunning)
                {
                    _warheadEvent = new MatchEvent();
                    _warheadEvent.At = now;
                    _warheadEvent.Kind = "warhead";
                    bool linked = _lastDeadmanEnd != DateTime.MinValue
                        && (now - _lastDeadmanEnd).TotalSeconds <= 30;
                    _warheadEvent.Label = linked ? "终焉核弹" : "核弹";
                    _warheadEvent.Source = "log";
                    r.Events.Add(_warheadEvent);
                }
                if (_lastWarheadRunning && !snap.WarheadRunning && _warheadEvent != null)
                {
                    _warheadEvent.EndAt = now;
                    double sec = Math.Max(1, (now - _warheadEvent.At).TotalSeconds);
                    _warheadEvent.Label += " " + sec.ToString("0") + "s";
                    _warheadEvent = null;
                }
                _lastWarheadRunning = snap.WarheadRunning;
            }
            r.Samples.Add(s);
            if (r.Samples.Count > 21600) r.Samples.RemoveAt(0);   // 6 小时
        }

        private static void AddMatchRole(MatchRound r, string role, string state, DateTime now, string note)
        {
            if (r == null) return;
            if (r.Roles.Count > 0)
            {
                MatchRoleSpan prev = r.Roles[r.Roles.Count - 1];
                if (prev.End == DateTime.MaxValue) prev.End = now;
            }
            MatchRoleSpan span = new MatchRoleSpan();
            span.Start = now;
            span.Role = role ?? "";
            span.State = string.IsNullOrEmpty(state) ? "alive" : state;
            span.Note = note ?? "";
            r.Roles.Add(span);
        }

        /// <summary>把性能记录里的事件按时间摊到各回合，一图流才能画出来。</summary>
        private void CopyPerfEventsToMatch()
        {
            if (_match == null || _livePerf == null || _livePerf.Events.Count == 0) return;
            for (int i = 0; i < _livePerf.Events.Count; i++)
            {
                GamePerfEvent ge = _livePerf.Events[i];
                MatchRound target = null;
                for (int r = 0; r < _match.Rounds.Count; r++)
                {
                    MatchRound round = _match.Rounds[r];
                    if (ge.At >= round.Start && (!round.HasEnd || ge.At <= round.End)) { target = round; break; }
                }
                if (target == null)
                {
                    for (int r = _match.Rounds.Count - 1; r >= 0; r--)
                        if (ge.At >= _match.Rounds[r].Start) { target = _match.Rounds[r]; break; }
                }
                if (target == null) continue;
                bool dup = false;
                for (int k = 0; k < target.Events.Count; k++)
                {
                    if (Math.Abs((target.Events[k].At - ge.At).TotalSeconds) < 1
                        && target.Events[k].Kind == ge.Kind) { dup = true; break; }
                }
                if (dup) continue;
                MatchEvent e = new MatchEvent();
                e.At = ge.At;
                e.Kind = ge.Kind;
                e.Label = ge.Label;
                e.Source = "log";
                e.EndAt = ge.EndAt;
                target.Events.Add(e);
            }
        }
        private void EndMatchRecord(DateTime now)
        {
            if (_match == null) return;
            try
            {
                _match.End = now;
                if (_deadmanEvent != null && !_deadmanEvent.HasEnd) _deadmanEvent.EndAt = now;
                if (_warheadEvent != null && !_warheadEvent.HasEnd) _warheadEvent.EndAt = now;
                _match.CloseRound(now);
                CopyPerfEventsToMatch();
                if (_match.TotalSamples >= 120)
                {
                    string dir = MatchStore.Save(_match);
                    if (dir != null)
                    {
                        MatchStore.Prune(_cfg != null ? Math.Max(5, _cfg.MatchKeep) : 30);
                        Logger.On("一图流", "对局已记录：" + dir + "（" + _match.TotalSamples + " 个采样点）");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Err("一图流", "保存对局失败", ex);
            }
            finally
            {
                _match = null;
                _matchRoundStart = DateTime.MinValue;
                _lastMatchRole = "";
                _lastMatchState = "";
            }
        }
        private string SaveManagedPerformance(GamePerfSession s, bool auto)
        {
            try
            {
                string dir = PerformanceReportDir();
                string prefix = auto ? "自动_" : "手动_";
                string stamp = s.Start.ToString("yyyyMMdd_HHmmss");
                string basePath = System.IO.Path.Combine(dir, prefix + stamp);
                int n = 2;
                while (File.Exists(basePath + ".png")) basePath = System.IO.Path.Combine(dir, prefix + stamp + "_" + (n++));
                GamePerformanceReport.Save(s, basePath + ".png", BuildReportPalette(), _tm.RefreshRate);
                File.WriteAllText(basePath + ".txt", GamePerformanceReport.RenderText(s, _tm.RefreshRate), Encoding.UTF8);
                WritePerfMeta(basePath + ".meta", s, auto);
                if (auto) PruneAutoPerformance(3);
                RefreshPerformanceRecords();
                return basePath + ".png";
            }
            catch (Exception ex)
            {
                Logger.Err("性能", "游戏性能曲线保存失败", ex);
                return null;
            }
        }

        private void WritePerfMeta(string path, GamePerfSession s, bool auto)
        {
            try
            {
                double avg = 0;
                int n = 0;
                for (int i = 0; i < s.Samples.Count; i++)
                    if (s.Samples[i].HasFps && s.Samples[i].Fps > 0) { avg += s.Samples[i].Fps; n++; }
                if (n > 0) avg /= n;
                File.WriteAllText(path,
                    "type=" + (auto ? "自动" : "手动") + "\r\n"
                    + "start=" + s.Start.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n"
                    + "end=" + (s.End == DateTime.MinValue ? "" : s.End.ToString("yyyy-MM-dd HH:mm:ss")) + "\r\n"
                    + "duration=" + s.DurationSeconds.ToString("0") + "\r\n"
                    + "avgfps=" + avg.ToString("0.0") + "\r\n"
                    + "samples=" + s.Samples.Count + "\r\n"
                    + "entered=" + (s.EnteredServer ? "1" : "0") + "\r\n",
                    Encoding.UTF8);
            }
            catch { }
        }

        private void PruneAutoPerformance(int keep)
        {
            try
            {
                string dir = PerformanceReportDir();
                string[] files = Directory.GetFiles(dir, "自动_*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int remove = files.Length - keep;
                for (int i = 0; i < remove; i++)
                {
                    string b = files[i].Substring(0, files[i].Length - 4);
                    try { File.Delete(files[i]); } catch { }
                    try { File.Delete(b + ".txt"); } catch { }
                    try { File.Delete(b + ".meta"); } catch { }
                }
            }
            catch { }
        }

        private static string ClockText(double seconds)
        {
            int s = (int)Math.Max(0, seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        private static string PerformanceReportDir()
        {
            try
            {
                string d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "性能报告");
                Directory.CreateDirectory(d);
                return d;
            }
            catch { }
            try
            {
                string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SCPSLAlert", "性能报告");
                Directory.CreateDirectory(d);
                return d;
            }
            catch { }
            return System.IO.Path.GetTempPath();
        }

        private PerfReportPalette BuildReportPalette()
        {
            PerfReportPalette p = new PerfReportPalette();
            p.Bg = Bg; p.Panel = Panel; p.PanelAlt = PanelAlt; p.Line = Line;
            p.Ink = Ink; p.Sub = Sub; p.Muted = Muted;
            p.Accent = Accent; p.Accent2 = Accent2; p.Danger = Danger;
            p.Gpu = GpuBrush; p.Net = NetBrush; p.Disk = DiskBrush;
            return p;
        }

        private void UpdateGamePerfNotice()
        {
            if (_perfSaveRow == null) return;
            bool show = _lastPerf != null && !_tm.GameRunning;
            _perfSaveRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show && _perfSaveText != null)
                _perfSaveText.Text = string.IsNullOrEmpty(_lastPerfSavedPath)
                    ? "当前游戏性能曲线已记录"
                    : "已保存当前游戏性能曲线";
        }

        private void ExportCurrentGamePerformance()
        {
            GamePerfSession s = _livePerf;
            if (s == null || s.Samples.Count == 0) s = _lastPerf;
            if (s == null || s.Samples.Count == 0)
            {
                _gameFpsStatus = "还没有可导出的游戏性能数据";
                UpdatePerformance();
                return;
            }
            ExportGamePerformance(s.Clone());
        }

        private void ExportLastGamePerformance()
        {
            if (_lastPerf == null || _lastPerf.Samples.Count == 0) return;
            ExportGamePerformance(_lastPerf.Clone());
        }

        private void ExportGamePerformance(GamePerfSession s)
        {
            try
            {
                Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Title = "导出游戏性能曲线";
                dlg.Filter = "PNG 图片 (*.png)|*.png";
                dlg.DefaultExt = ".png";
                dlg.FileName = "游戏性能_" + s.Start.ToString("yyyyMMdd_HHmmss") + ".png";
                try { dlg.InitialDirectory = PerformanceReportDir(); } catch { }
                if (dlg.ShowDialog(this) != true) return;
                GamePerformanceReport.Save(s, dlg.FileName, BuildReportPalette(), _tm.RefreshRate);
                try
                {
                    File.WriteAllText(System.IO.Path.ChangeExtension(dlg.FileName, ".txt"),
                        GamePerformanceReport.RenderText(s, _tm.RefreshRate), Encoding.UTF8);
                }
                catch { }
                // 手动导出的也留一份在软件记录里，永久保留，方便以后回来删。
                SaveManagedPerformance(s, false);
                _gameFpsStatus = "已导出游戏性能曲线";
                UpdatePerformance();
            }
            catch (Exception ex)
            {
                _gameFpsStatus = "导出失败：" + ex.Message;
                UpdatePerformance();
                Logger.Err("性能", "导出游戏性能曲线失败", ex);
            }
        }

        private UIElement BuildToolsPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("工具", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);
            page.Children.Add(AdvancedBanner("ocr", "OCR 开关、扫描时机、识别强度、语言模型与区域调试"));

            UniformGrid tiles = new UniformGrid();
            tiles.Columns = 4;

            tiles.Children.Add(ToolTile(2, "\uE9F9", "扫描报告", "查看上一次识别结果", delegate { if (_ocrReport != null) _ocrReport(); }));
            tiles.Children.Add(ToolTile(3, "\uE823", "战术校准", "对齐回合与倒计时", delegate { if (_calibrate != null) _calibrate(); }));
            tiles.Children.Add(ToolTile(4, "\uE7E7", "预览全部提示", "把所有提示弹一遍", delegate { if (_previewAlerts != null) _previewAlerts(); }));
            tiles.Children.Add(ToolTile(5, "\uE8A5", "打开日志", "选择完整 / OCR / 时间等日志", delegate { ShowLogChoice(); }));
            tiles.Children.Add(ToolTile(6, "\uE8D6", "打开音频目录", "放入 mp3 / wav 素材", delegate { OpenFolderSafe(AudioLibrary.ResolveDir(_cfg.AudioDir)); }));
            tiles.Children.Add(ToolTile(7, "\uE713", "打开配置目录", "config.ini 与备份", delegate { OpenFolderSafe(AppDomain.CurrentDomain.BaseDirectory); }));
            tiles.Children.Add(ToolTile(9, "\uE9D9", "一图流", "查看 / 导出对局图", delegate { if (_oneMap != null) _oneMap(); }));
            tiles.Children.Add(ToolTile(8, "\uE72C", "重启程序", "重新加载全部设置", delegate { if (_restart != null) _restart(); }));
            page.Children.Add(tiles);

            StackPanel body;
            VigilCard info = StyleCard("RUNTIME", out body);
            body.Children.Add(InfoLine("版本", AppInfo.Version));
            body.Children.Add(InfoLine("系统", Environment.OSVersion.VersionString));
            body.Children.Add(InfoLine("配置", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini")));
            body.Children.Add(InfoLine("音频", AudioLibrary.ResolveDir(_cfg.AudioDir)));
            page.Children.Add(info);
            return page;
        }

        private UIElement BuildAdvancedPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ViewportHeight")
            {
                Source = _advancedPage,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("高级", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);
            Grid.SetRow(head, 0);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            string displaySummary = _cfg.DisplayAssistEnabled
                ? "伽马 " + (_cfg.GameBrightnessHoldEnabled ? (_cfg.GameBrightnessHoldGamma / 100.0).ToString("0.00") : "跟随") +
                  "  ·  准星 " + (_cfg.CrosshairEnabled ? "开" : "关")
                : "已关闭";
            string recognitionSummary = _cfg.RecognitionEnabled
                ? "OCR " + (_cfg.EnableOcr ? "开" : "关") + "  ·  手动扫描 " + (_cfg.EnableOcr ? "可用" : "停")
                : "已关闭";
            string heartSummary = !_cfg.HeartRateBleEnabled
                ? "已关闭"
                : (HeartRateHub.BleConnected
                    ? "已连接  ·  " + HeartRateHub.Bpm + " BPM"
                    : "未连接");

            UIElement display = AdvancedEntryCard("DISPLAY  /  显示", "显示与画面", displaySummary,
                _cfg.DisplayAssistEnabled, delegate(bool v) { _cfg.DisplayAssistEnabled = v; },
                "display", Accent, new string[][]
                {
                    new[] { "伽马", _cfg.GameBrightnessHoldEnabled ? "固定 " + (_cfg.GameBrightnessHoldGamma / 100.0).ToString("0.00") : "跟随画面" },
                    new[] { "饱和度", (_cfg.GameBrightnessHoldSaturation / 100.0).ToString("0.00") },
                    new[] { "准星", _cfg.CrosshairEnabled ? "开" : "关" }
                });
            Grid.SetColumn(display, 0);
            grid.Children.Add(display);

            UIElement recognition = AdvancedEntryCard("RECOGNITION  /  识别", "识别", recognitionSummary,
                _cfg.RecognitionEnabled, delegate(bool v)
                {
                    _cfg.RecognitionEnabled = v;
                    if (_ocrSettingsChanged != null) _ocrSettingsChanged();
                }, "recognition", Accent2, new string[][]
                {
                    new[] { "阵营识别", _cfg.FactionEnabled ? "读血条颜色" : "关" },
                    new[] { "手动扫描", _cfg.EnableOcr ? "F2 可用" : "停" },
                    new[] { "播报 OCR", _cfg.EnableOcr ? "开" : "关" }
                });
            Grid.SetColumn(recognition, 2);
            grid.Children.Add(recognition);

            UIElement device = AdvancedEntryCard("DEVICE  /  设备", "设备", heartSummary,
                _cfg.HeartRateBleEnabled, delegate(bool v)
                {
                    _cfg.HeartRateBleEnabled = v;
                    try
                    {
                        if (v) TryStartHeartSource();
                        else if (_heartSource != null) _heartSource.Stop();
                    }
                    catch { }
                }, "device", SuccessBrush(), new string[][]
                {
                    new[] { "蓝牙", !_cfg.HeartRateBleEnabled ? "关" : (HeartRateHub.BleConnected ? "已连接" : "搜索中") },
                    new[] { "心率", HeartRateHub.Bpm > 0 ? HeartRateHub.Bpm + " BPM" : "-- BPM" },
                    new[] { "屏幕提示", _cfg.HeartPromptEnabled ? "开" : "关" }
                });
            Grid.SetColumn(device, 4);
            grid.Children.Add(device);

            Grid.SetRow(grid, 1);
            grid.VerticalAlignment = VerticalAlignment.Stretch;
            page.Children.Add(grid);
            return page;
        }

        private UIElement AdvancedEntryCard(string code, string title, string summary,
            bool on, Action<bool> toggle, string detailId, Brush tone, string[][] rows)
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 16, 18, 16);
            card.MinHeight = 300;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            StackPanel body = new StackPanel();
            body.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = DisplayText(title, 20, Ink);
            name.FontWeight = FontWeights.Bold;
            name.Margin = new Thickness(0, 2, 0, 4);
            body.Children.Add(name);
            TextBlock desc = new TextBlock();
            desc.Text = summary;
            desc.FontSize = 11.5;
            desc.Foreground = Sub;
            desc.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(desc);

            if (rows != null)
            {
                Border sep = new Border();
                sep.Height = 1;
                sep.Background = Line;
                sep.Margin = new Thickness(0, 12, 0, 4);
                body.Children.Add(sep);
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] == null || rows[i].Length < 2) continue;
                    body.Children.Add(AdvancedEntryRow(rows[i][0], rows[i][1]));
                }
            }

            Grid row = new Grid();
            row.Margin = new Thickness(0, 18, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel sw = new StackPanel();
            sw.Orientation = Orientation.Horizontal;
            sw.VerticalAlignment = VerticalAlignment.Center;
            VigilToggle toggleSwitch = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            toggleSwitch.IsChecked = on;
            toggleSwitch.CheckedChanged += delegate
            {
                if (toggle != null) toggle(toggleSwitch.IsChecked);
                TouchSave();
                if (_currentPage == AdvancedPageIndex)
                {
                    _advancedPage.Content = null;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_currentPage == AdvancedPageIndex) SelectPage(AdvancedPageIndex);
                    }), DispatcherPriority.Background);
                }
            };
            sw.Children.Add(toggleSwitch);
            TextBlock swLabel = new TextBlock();
            swLabel.Text = "总开关";
            swLabel.FontSize = 11.5;
            swLabel.Foreground = Sub;
            swLabel.VerticalAlignment = VerticalAlignment.Center;
            swLabel.Margin = new Thickness(10, 0, 0, 0);
            sw.Children.Add(swLabel);
            row.Children.Add(sw);
            body.Children.Add(row);
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (toggleSwitch.IsMouseOver) return;
                ShowAdvancedDetail(detailId);
            };

            Grid layer = new Grid();
            layer.Children.Add(new CardLineArt(detailId, ColorOf(tone), _light));
            layer.Children.Add(body);
            card.Child = layer;
            _hoverTargets.Add(card);
            return card;
        }

        private UIElement AdvancedEntryRow(string label, string value)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 6, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = Math.Max(8, Math.Min(96, _cfg.FontDescSize));
            l.Foreground = Sub;
            row.Children.Add(l);
            TextBlock v = new TextBlock();
            v.Text = value;
            v.FontSize = Math.Max(8, Math.Min(96, _cfg.FontDescSize));
            v.Foreground = Ink;
            v.FontFamily = Mono;
            Grid.SetColumn(v, 1);
            row.Children.Add(v);
            return row;
        }

        /// <summary>
        /// 二级页离场清理：在 ShowAdvancedDetail / ShowBrightnessPage 重建内容前调用。
        ///
        /// 复用同一个 UIElement 挂到新父级会抛 “指定的元素已经是另一个元素的逻辑子元素”；
        /// 同时旧曲线画布/预设下拉如果把 Changed / SelectionChanged 留着，
        /// 旧页面虽然看不见，事件仍会打到已经换掉的字段上。这里统一做两件事：
        ///   1. 把本页复用的元素从父级摘掉（Decorator / Panel / ContentControl 都覆盖）；
        ///   2. 解绑本页挂上去的事件、停掉 500ms 防抖计时器（有未落盘修改则先保存）。
        /// </summary>
        private void DetachAdvancedDetail()
        {
            try
            {
                if (_brightnessSaveTimer != null)
                {
                    if (_brightnessSaveTimer.IsEnabled && _curveCanvas != null
                        && _curvePoints != null && _curvePoints.Count >= 3)
                        SaveCurve(false);
                    _brightnessSaveTimer.Stop();
                    _brightnessSaveTimer = null;
                }
            }
            catch { }

            if (_curveCanvas != null)
            {
                _curveCanvas.Changed -= OnCurveChanged;
                DetachFromParent(_curveCanvas);
                _curveCanvas = null;
            }
            if (_presetCombo != null)
            {
                _presetCombo.SelectionChanged -= OnPresetChanged;
                DetachFromParent(_presetCombo);
                _presetCombo = null;
            }

            // 这两个是“准星卡/亮度卡复用同一个预览实例”时的重点对象。
            DetachFromParent(_chPrevMini);
            _chPrevMini = null;
            DetachFromParent(_chPrev);
            _chPrev = null;

            _curveTab = null;
            _captureTab = null;
            _curvePanel = null;
            _capturePanel = null;
            _curveStatus = null;
            _curvePointText = null;
            _curvePoints = new List<BrightnessPoint>();
        }

        /// <summary>把元素从当前逻辑/视觉父级摘掉；不依赖具体父级类型。</summary>
        private static void DetachFromParent(FrameworkElement element)
        {
            if (element == null) return;

            DependencyObject parent = null;
            try { parent = LogicalTreeHelper.GetParent(element); } catch { }
            if (parent == null)
            {
                try { parent = VisualTreeHelper.GetParent(element); } catch { }
            }
            if (parent == null) return;

            Panel panel = parent as Panel;
            if (panel != null)
            {
                panel.Children.Remove(element);
                return;
            }
            Decorator decorator = parent as Decorator;
            if (decorator != null)
            {
                if (ReferenceEquals(decorator.Child, element)) decorator.Child = null;
                return;
            }
            ContentControl content = parent as ContentControl;
            if (content != null)
            {
                if (ReferenceEquals(content.Content, element)) content.Content = null;
                return;
            }
            ContentPresenter presenter = parent as ContentPresenter;
            if (presenter != null && ReferenceEquals(presenter.Content, element))
                presenter.Content = null;
        }

        private void ShowAdvancedDetail(string id)
        {
            SelectPage(AdvancedPageIndex);
            if (_advDetailPage == null || _advDetailBody == null) return;
            DetachAdvancedDetail();
            _advancedPage.Visibility = Visibility.Collapsed;
            _advDetailBody.Children.Clear();

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 14);
            head.Children.Add(CommandButton("\uE72B", "返回高级", delegate { SelectPage(AdvancedPageIndex); }, Sub));
            string titleText = id == "display" ? "显示与画面" : (id == "recognition" ? "识别" : "设备");
            string codeText = id == "display" ? "DISPLAY" : (id == "recognition" ? "RECOGNITION" : "DEVICE");
            TextBlock code = MonoText(codeText + "  /  DETAIL", 9, Accent);
            code.Margin = new Thickness(0, 8, 0, 0);
            head.Children.Add(code);
            TextBlock title = DisplayText(titleText, 28, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            _advDetailBody.Children.Add(head);

            if (id == "display")
            {
                AddDetailCard(BuildBrightnessCard());
                AddDetailCard(BuildCrosshairCard());
            }
            else if (id == "recognition")
            {
                AddDetailCard(BuildOcrAdvancedCard());
                AddDetailCard(BuildFactionCard());
            }
            else
            {
                AddDetailCard(BuildHeartRateCard());
            }

            _advDetailPage.Visibility = Visibility.Visible;
            _currentPage = AdvancedPageIndex;
            UpdateRailSelection(AdvancedPageIndex);
        }

        private void AddDetailCard(UIElement card)
        {
            FrameworkElement fe = card as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, 0, 0, 12);
            _advDetailBody.Children.Add(card);
        }

        private Brush SuccessBrush()
        {
            return _light ? Solid(31, 122, 77) : Solid(91, 227, 140);
        }

        private UIElement AdvancedBanner(string section, string text)
        {
            Color accent = ColorOf(Accent);
            Color panelAlt = ColorOf(PanelAlt);

            Border outer = new Border();
            outer.CornerRadius = new CornerRadius(5);
            outer.BorderThickness = new Thickness(1);
            outer.Margin = new Thickness(0, 0, 0, 12);
            outer.MinHeight = 54;
            outer.Cursor = Cursors.Hand;
            outer.ToolTip = "前往对应设置";

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border stripe = new Border();
            stripe.Background = Accent;
            stripe.Width = 3;
            stripe.Opacity = 0.9;
            stripe.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(stripe, 0);
            g.Children.Add(stripe);

            Border iconBox = new Border();
            iconBox.Width = 28;
            iconBox.Height = 28;
            iconBox.CornerRadius = new CornerRadius(5);
            iconBox.Background = AccentSoftBrush();
            iconBox.BorderThickness = new Thickness(1);
            iconBox.BorderBrush = new SolidColorBrush(VigilButtonPalette.Fade(accent, 110));
            iconBox.VerticalAlignment = VerticalAlignment.Center;
            iconBox.Margin = new Thickness(14, 10, 12, 10);
            TextBlock icon = new TextBlock();
            icon.Text = "\uE713";
            icon.FontFamily = Icons;
            icon.FontSize = 12;
            icon.Foreground = Accent;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            iconBox.Child = icon;
            Grid.SetColumn(iconBox, 1);
            g.Children.Add(iconBox);

            StackPanel labels = new StackPanel();
            labels.VerticalAlignment = VerticalAlignment.Center;
            TextBlock title = MonoText("相关设置  /  SETTINGS", 9, Accent);
            title.FontWeight = FontWeights.Bold;
            labels.Children.Add(title);
            TextBlock desc = new TextBlock();
            desc.Text = text;
            desc.FontSize = 9.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 2, 0, 0);
            desc.TextWrapping = TextWrapping.Wrap;
            labels.Children.Add(desc);
            Grid.SetColumn(labels, 2);
            g.Children.Add(labels);

            Border cta = new Border();
            cta.CornerRadius = new CornerRadius(4);
            cta.BorderThickness = new Thickness(1);
            cta.BorderBrush = new SolidColorBrush(VigilButtonPalette.Fade(accent, 170));
            cta.Background = new SolidColorBrush(VigilButtonPalette.Fade(accent, 26));
            cta.Padding = new Thickness(11, 5, 11, 5);
            cta.VerticalAlignment = VerticalAlignment.Center;
            cta.Margin = new Thickness(14, 0, 14, 0);
            StackPanel ctaContent = new StackPanel();
            ctaContent.Orientation = Orientation.Horizontal;
            TextBlock go = new TextBlock();
            go.Text = "前往";
            go.FontFamily = Ui;
            go.FontSize = 10;
            go.FontWeight = FontWeights.Bold;
            go.Foreground = Accent;
            go.VerticalAlignment = VerticalAlignment.Center;
            ctaContent.Children.Add(go);
            TextBlock arrow = new TextBlock();
            arrow.Text = "  →";
            arrow.FontSize = 12;
            arrow.Foreground = Accent;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            ctaContent.Children.Add(arrow);
            cta.Child = ctaContent;
            Grid.SetColumn(cta, 3);
            g.Children.Add(cta);

            outer.Child = g;
            new VigilPressState(outer,
                VigilButtonPalette.Mix(panelAlt, accent, 0.05),
                VigilButtonPalette.Mix(panelAlt, accent, 0.13),
                VigilButtonPalette.Mix(panelAlt, accent, 0.21),
                VigilButtonPalette.Fade(accent, 115),
                accent,
                accent);
            outer.MouseLeftButtonUp += delegate { ShowAdvancedSection(section); };
            return outer;
        }

        private void ShowAdvancedSection(string section)
        {
            switch (section)
            {
                case "ocr": ShowAdvancedDetail("recognition"); break;
                case "heart": ShowAdvancedDetail("device"); break;
                case "brightness":
                case "crosshair":
                case "display": ShowAdvancedDetail("display"); break;
                case "time": SelectPage(4); break;
                case "audio": SelectPage(3); break;
                case "position": SelectPage(2); break;
                default: ShowAdvancedDetail("display"); break;
            }
        }

        private VigilCard AdvancedCard(string title, string code, out StackPanel body)
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 15);
            card.Margin = new Thickness(0, 0, 0, 12);
            body = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock h = MonoText(title, 9, Accent);
            h.FontWeight = FontWeights.Bold;
            head.Children.Add(h);
            TextBlock c = MonoText(code, 7.5, Muted);
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, 1);
            head.Children.Add(c);
            body.Children.Add(head);
            card.Child = body;
            _hoverTargets.Add(card);
            return card;
        }

        private VigilToggle AdvToggle(bool value, Action<bool> changed)
        {
            VigilToggle t = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            t.IsChecked = value;
            t.CheckedChanged += delegate { changed(t.IsChecked); };
            return t;
        }

        private VigilNumberField AdvNumber(int value, int min, int max, Action<int> changed)
        {
            VigilNumberField f = new VigilNumberField(_light, Line, Ink, Sub, 86);
            f.Minimum = min;
            f.Maximum = max;
            f.Value = value;
            f.ValueChanged += delegate { changed((int)f.Value); };
            return f;
        }

        private VigilSelect AdvSelect(List<string> items, int index, Action<int> changed)
        {
            VigilSelect s = new VigilSelect(PanelAlt, Line, Ink, Accent, 220);
            s.SetItems(items, index);
            s.SelectionChanged += delegate { changed(s.SelectedIndex); };
            return s;
        }

        private WrapPanel AdvActions()
        {
            WrapPanel w = new WrapPanel();
            w.Margin = new Thickness(0, 8, 0, 0);
            return w;
        }

        private void RunOrLegacy(Action action, int legacyPage)
        {
            if (action != null) action();
            else if (_openLegacyPage != null) _openLegacyPage(legacyPage);
        }

        private void ApplyOcrAdvanced()
        {
            if (_ocrSettingsChanged != null) _ocrSettingsChanged();
            TouchSave();
        }

        private UIElement BuildOcrAdvancedCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("OCR CONFIG  /  识别配置", "LIVE", out body);

            body.Children.Add(GeneralRow("屏幕播报识别", "总开关；关闭后不初始化 OCR", AdvToggle(_cfg.EnableOcr, delegate(bool v) { _cfg.EnableOcr = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("扫描时机", "只在进入服务器后开始截图", AdvToggle(_cfg.OcrOnlyInServer, delegate(bool v) { _cfg.OcrOnlyInServer = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("轮询间隔", "毫秒；越小越吃 CPU", AdvNumber(_cfg.OcrPollInterval, 300, 30000, delegate(int v) { _cfg.OcrPollInterval = v; ApplyOcrAdvanced(); })));

            List<string> strengths = new List<string>();
            strengths.Add("保守（只认很亮的白字）");
            strengths.Add("标准");
            strengths.Add("激进（推荐）");
            strengths.Add("最大（占用更高）");
            body.Children.Add(GeneralRow("识别强度", "暗字 / 半透明播报的取舍", AdvSelect(strengths, Math.Max(0, Math.Min(3, _cfg.OcrStrength)), delegate(int v) { _cfg.OcrStrength = v; ApplyOcrAdvanced(); })));

            List<string> models = new List<string>();
            models.Add("快速版（tessdata）");
            models.Add("最高精度（tessdata_best）");
            body.Children.Add(GeneralRow("语言模型", "最高精度更准但更慢", AdvSelect(models, _cfg.OcrBestModel ? 1 : 0, delegate(int v) { _cfg.OcrBestModel = v == 1; ApplyOcrAdvanced(); })));

            List<string> langs = new List<string>();
            langs.Add("eng+chi_sim");
            langs.Add("eng");
            langs.Add("chi_sim");
            if (!langs.Contains(_cfg.OcrLang)) langs.Add(_cfg.OcrLang);
            int langIndex = langs.IndexOf(_cfg.OcrLang);
            body.Children.Add(GeneralRow("识别语言", "对应 tessdata 语言包", AdvSelect(langs, langIndex < 0 ? 0 : langIndex, delegate(int v) { if (v >= 0 && v < langs.Count) { _cfg.OcrLang = langs[v]; ApplyOcrAdvanced(); } })));

            body.Children.Add(GeneralRow("录制隐藏", "本程序窗口不出现在截图 / 录屏", AdvToggle(_cfg.HideFromCapture, delegate(bool v) { _cfg.HideFromCapture = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("捕获提示", "识别到文字时在屏幕左下角显示小条", AdvToggle(_cfg.OcrToast, delegate(bool v) { _cfg.OcrToast = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("重生严格判定", "票数与静音连续未识别才判重生", AdvToggle(_cfg.RespawnRequireTicketsGone, delegate(bool v) { _cfg.RespawnRequireTicketsGone = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("灰度重试", "第一次认不出时反色放大再认一次", AdvToggle(_cfg.OcrRetryRaw, delegate(bool v) { _cfg.OcrRetryRaw = v; ApplyOcrAdvanced(); })));
            body.Children.Add(GeneralRow("灰度放大", "重试时的放大倍数", AdvNumber(_cfg.OcrScale, 1, 3, delegate(int v) { _cfg.OcrScale = v; ApplyOcrAdvanced(); })));

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE721", "立即扫描", delegate { if (_ocrScan != null) _ocrScan(); }, Accent));
            actions.Children.Add(CommandButton("\uE8A5", "扫描报告", delegate { if (_ocrReport != null) _ocrReport(); }, Sub));
            actions.Children.Add(CommandButton("\uE8A5", "预览识别", delegate { RunOrLegacy(_previewOcr, 1); }, Accent2));
            actions.Children.Add(CommandButton("\uE8A5", "框选播报区域", delegate { RunOrLegacy(_pickOcrRegion, 1); }, Sub));
            actions.Children.Add(CommandButton("\uE8A5", "监控区域列表", delegate { RunOrLegacy(_editOcrRegions, 1); }, Sub));
            actions.Children.Add(CommandButton("\uE8A5", "识别说明", delegate { RunOrLegacy(_ocrHelp, 1); }, Muted));
            actions.Children.Add(CommandButton("\uE713", "完整 OCR 设置", delegate { if (_openLegacyPage != null) _openLegacyPage(1); }, Muted));
            body.Children.Add(actions);

            TextBlock note = new TextBlock();
            note.Text = "改动会立即应用到 OCR 引擎。区域列表、预览识别与框选在识别工具里，结果会同步回状态页。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private UIElement BuildFactionCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("FACTION  /  阵营识别", "BAR", out body);

            TextBlock desc = new TextBlock();
            desc.Text = "开局角色分配完成、以及每次复活时，读左下角血条最左 10% 的颜色判定阵营；"
                + "连续 10 帧一致才出结果，最多 3 秒；连续 5 秒没有血条判 SCP-079。"
                + "不再截取屏幕中心的角色文字。";
            desc.TextWrapping = TextWrapping.Wrap;
            desc.FontSize = 11.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(desc);

            body.Children.Add(GeneralRow("识别开关", "关掉后不再读血条颜色", AdvToggle(_cfg.FactionEnabled, delegate(bool v) { _cfg.FactionEnabled = v; TouchSave(); })));
            body.Children.Add(GeneralRow("采样上限", "秒", AdvNumber(_cfg.FactionMaxSampleSeconds, 1, 10, delegate(int v) { _cfg.FactionMaxSampleSeconds = v; TouchSave(); })));
            body.Children.Add(GeneralRow("确认帧数", "连续多少帧一致才出结果", AdvNumber(_cfg.FactionConfirmFrames, 3, 30, delegate(int v) { _cfg.FactionConfirmFrames = v; TouchSave(); })));
            body.Children.Add(GeneralRow("复活延迟", "秒；等血条重新出现", AdvNumber(_cfg.FactionRespawnDelaySeconds, 0, 10, delegate(int v) { _cfg.FactionRespawnDelaySeconds = v; TouchSave(); })));
            body.Children.Add(GeneralRow("颜色容差", "0.10–0.95，越大越宽松", AdvNumber((int)Math.Round(_cfg.FactionRgbTolerance * 100), 10, 95, delegate(int v) { _cfg.FactionRgbTolerance = v / 100.0; TouchSave(); })));

            // 映射表：一行一个阵营，色块 = 判定用的基准色
            TextBlock mapTitle = MonoText("COLOR MAP  /  颜色映射", 9, Muted);
            mapTitle.Margin = new Thickness(0, 12, 0, 4);
            body.Children.Add(mapTitle);
            if (_cfg.FactionMap != null)
            {
                for (int i = 0; i < _cfg.FactionMap.Count; i++)
                {
                    FactionEntry e = _cfg.FactionMap[i];
                    if (e == null) continue;
                    body.Children.Add(FactionMapRow(e));
                }
            }

            VigilSnapshot snap = _snapshot != null ? _snapshot() : null;
            string role = snap != null && !string.IsNullOrEmpty(snap.Role) ? snap.Role : "未识别";
            string timeline = snap != null && !string.IsNullOrEmpty(snap.RoleTimeline) ? snap.RoleTimeline : "本局还没有记录";
            TextBlock current = new TextBlock();
            current.Text = "当前：" + role + "　｜　本局：" + timeline;
            current.TextWrapping = TextWrapping.Wrap;
            current.FontSize = 11.5;
            current.Foreground = Ink;
            current.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(current);

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE72C", "重新识别", delegate { if (_factionScan != null) _factionScan(); }, Accent));
            body.Children.Add(actions);

            TextBlock note = new TextBlock();
            note.Text = "颜色表默认值来自 阵营截图\\ 的 14 张实测；SCP 目前只有 173 的红色。"
                + "要改颜色请编辑 config.ini 的 [Faction] Map（名称|H|S|V|容差|…，分号分隔）。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        // ============================================================
        // 第 6 批：亮度校准 → 采集区（21 张池 + 7 张展示）
        // ============================================================

        private string CaptureFolder
        {
            get
            {
                string dir = string.IsNullOrEmpty(_cfg.CaptureDir) ? "采集" : _cfg.CaptureDir;
                if (!System.IO.Path.IsPathRooted(dir)) dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
                return dir;
            }
        }

        private void ShowCapturePage()
        {
            ShowBrightnessPage(1);
        }

        private UIElement BuildCaptureCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("CAPTURE  /  采集", "21 → 7", out body);

            int total = Math.Max(7, Math.Min(21, _cfg.CaptureMaxCount));
            int have = 0;
            try
            {
                string dir = CaptureFolder;
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.png")) have++;
            }
            catch { }

            TextBlock status = new TextBlock();
            status.Text = "目录：" + CaptureFolder + "\n"
                + "当前 " + have + " 张 / 上限 " + total + " 张"
                + (string.IsNullOrEmpty(_cfg.CaptureSetAt) ? "　（还没采集过）" : "　上次：" + _cfg.CaptureSetAt);
            status.TextWrapping = TextWrapping.Wrap;
            status.FontSize = 11.5;
            status.Foreground = Ink;
            status.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(status);

            body.Children.Add(GeneralRow("基础张数", "到这张时提示“基础的图片已采集完成”", AdvNumber(_cfg.CaptureMinCount, 1, 21, delegate(int v) { _cfg.CaptureMinCount = v; TouchSave(); })));
            body.Children.Add(GeneralRow("上限张数", "文件夹最多保留这么多张", AdvNumber(_cfg.CaptureMaxCount, 7, 21, delegate(int v) { _cfg.CaptureMaxCount = v; TouchSave(); })));
            body.Children.Add(GeneralRow("提示横向", "%（基准 = 目标显示器宽度）", AdvNumber(double.IsNaN(_cfg.CaptureHudXPercent) ? 50 : (int)Math.Round(_cfg.CaptureHudXPercent * 100), 0, 100, delegate(int v) { _cfg.CaptureHudXPercent = v / 100.0; TouchSave(); })));
            body.Children.Add(GeneralRow("提示纵向", "%（基准 = 目标显示器高度）", AdvNumber(double.IsNaN(_cfg.CaptureHudYPercent) ? 6 : (int)Math.Round(_cfg.CaptureHudYPercent * 100), 0, 100, delegate(int v) { _cfg.CaptureHudYPercent = v / 100.0; TouchSave(); })));

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE768", "开始采集", delegate { if (_captureStart != null) _captureStart(); }, Accent));
            actions.Children.Add(CommandButton("\uE769", "暂停", delegate { if (_capturePause != null) _capturePause(); }, Sub));
            actions.Children.Add(CommandButton("\uE768", "继续", delegate { if (_captureStart != null) _captureStart(); }, Sub));
            actions.Children.Add(CommandButton("\uE71A", "停止", delegate
            {
                if (System.Windows.MessageBox.Show("停止采集？已采到的图片会保存到本地，不会删除。",
                    "截屏采集", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                if (_captureStop != null) _captureStop();
            }, Sub));
            actions.Children.Add(CommandButton("\uE74D", "删除已采集图片", delegate
            {
                if (System.Windows.MessageBox.Show("删除采集目录里的全部 png？此操作不可撤销。",
                    "截屏采集", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                if (_captureDelete != null) _captureDelete();
                ShowCapturePage();
            }, Muted));
            body.Children.Add(actions);

            WrapPanel caps = new WrapPanel();
            caps.Margin = new Thickness(0, 10, 0, 0);
            AddKeycap(caps, "开始采集", "capturestart", delegate { return _cfg.HotkeyCaptureStart; }, delegate(string v) { _cfg.HotkeyCaptureStart = v; });
            AddKeycap(caps, "暂停 / 继续", "capturepause", delegate { return _cfg.HotkeyCapturePause; }, delegate(string v) { _cfg.HotkeyCapturePause = v; });
            body.Children.Add(caps);

            WrapPanel more = AdvActions();
            more.Children.Add(CommandButton("\uE8A5", "打开采集目录", delegate
            {
                try { Directory.CreateDirectory(CaptureFolder); Process.Start("explorer.exe", CaptureFolder); } catch { }
            }, Sub));
            more.Children.Add(CommandButton("\uE72C", "打开旧曲线编辑器", delegate { if (_openBrightness != null) _openBrightness(false); }, Sub));
            body.Children.Add(more);

            TextBlock note = new TextBlock();
            note.Text = "规则：一次 21 张覆盖上一轮（先写临时目录，采满再替换）；采集中死亡会暂停并等一次点击，"
                + "复活会自动暂停；暂停/继续各有独立快捷键；“后台可用”开关在热键页。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private UIElement BuildCaptureGalleryCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("SAMPLES  /  7 张亮度样本", "LUMA", out body);

            List<int> picks = CaptureSelectedPositions();
            string dir = CaptureFolder;
            if (picks.Count == 0 || !Directory.Exists(dir))
            {
                TextBlock empty = new TextBlock();
                empty.Text = "还没有采集结果。点上面的「开始采集」，采满 21 张后会自动挑出亮度分布最均匀的 7 张。下方是 7 档亮度参考，可先用来判断当前画面偏暗还是偏亮。";
                empty.TextWrapping = TextWrapping.Wrap;
                empty.FontSize = 11.5;
                empty.Foreground = Sub;
                body.Children.Add(empty);
                return card;
            }

            WrapPanel wrap = new WrapPanel();
            for (int i = 0; i < picks.Count; i++)
            {
                string file = System.IO.Path.Combine(dir, "capture" + picks[i].ToString("00") + ".png");
                wrap.Children.Add(CaptureThumb(file, picks[i], i + 1));
            }
            body.Children.Add(wrap);
            return card;
        }

        private List<int> CaptureSelectedPositions()
        {
            List<int> list = new List<int>();
            if (string.IsNullOrEmpty(_cfg.CaptureSelected)) return list;
            string[] parts = _cfg.CaptureSelected.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                    && n >= 1 && n <= 21 && !list.Contains(n))
                    list.Add(n);
            }
            return list;
        }

        private UIElement CaptureThumb(string path, int position, int order)
        {
            StackPanel box = new StackPanel();
            box.Width = 186;
            box.Margin = new Thickness(0, 0, 10, 10);

            Border frame = new Border();
            frame.Height = 104;
            frame.Background = PanelAlt;
            frame.BorderThickness = new Thickness(1);
            frame.BorderBrush = Line;
            frame.CornerRadius = new CornerRadius(4);
            frame.ClipToBounds = true;

            try
            {
                if (File.Exists(path))
                {
                    BitmapImage img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(path, UriKind.Absolute);
                    img.EndInit();
                    Image view = new Image();
                    view.Source = img;
                    view.Stretch = Stretch.UniformToFill;
                    frame.Child = view;
                }
                else
                {
                    TextBlock missing = new TextBlock();
                    missing.Text = "缺图";
                    missing.Foreground = Muted;
                    missing.HorizontalAlignment = HorizontalAlignment.Center;
                    missing.VerticalAlignment = VerticalAlignment.Center;
                    frame.Child = missing;
                }
            }
            catch { }
            box.Children.Add(frame);

            TextBlock label = new TextBlock();
            string luma = "—";
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(path))
                {
                    int w = Math.Min(192, bmp.Width);
                    luma = BrightnessSampler.Measure(bmp).ToString();
                }
            }
            catch { }
            label.Text = "#" + order + "　亮度 " + luma + "　capture" + position.ToString("00");
            label.FontFamily = Mono;
            label.FontSize = 9;
            label.Foreground = Sub;
            label.Margin = new Thickness(2, 4, 0, 0);
            box.Children.Add(label);
            return box;
        }

        private UIElement FactionMapRow(FactionEntry e)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 3);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border swatch = new Border();
            swatch.Width = 10;
            swatch.Height = 10;
            swatch.CornerRadius = new CornerRadius(2);
            swatch.VerticalAlignment = VerticalAlignment.Center;
            System.Drawing.Color dc = FactionTable.HsvToRgb(e.Hue, e.Sat, e.Val);
            swatch.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(dc.A, dc.R, dc.G, dc.B));
            Grid.SetColumn(swatch, 0);
            row.Children.Add(swatch);

            TextBlock name = new TextBlock();
            name.Text = e.Name;
            name.FontSize = Math.Max(8, Math.Min(96, _cfg.FontDescSize));
            name.Foreground = Ink;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            TextBlock hsv = new TextBlock();
            hsv.Text = "H" + e.Hue.ToString("0") + " S" + e.Sat.ToString("0.00") + " V" + e.Val.ToString("0.00");
            hsv.FontFamily = Mono;
            hsv.FontSize = 9.5;
            hsv.Foreground = Sub;
            hsv.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hsv, 2);
            row.Children.Add(hsv);
            return row;
        }

        private UIElement BuildTimeToolsCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("TIME TOOLS  /  时间工具", "EXTRA", out body);

            body.Children.Add(GeneralRow("核弹倒计时", "秒；F8 / Ctrl+F8", AdvNumber(_cfg.Warhead.DurationSeconds, 1, 36000, delegate(int v) { _cfg.Warhead.DurationSeconds = v; TouchSave(); })));
            body.Children.Add(GeneralRow("核弹取消重启", "秒；Shift+F8", AdvNumber(_cfg.WarheadRestartSeconds, 1, 36000, delegate(int v) { _cfg.WarheadRestartSeconds = v; TouchSave(); })));
            body.Children.Add(GeneralRow("三台发电机提示停留", "秒；全部就绪提示显示多久", AdvNumber(_cfg.GenAllReadySeconds, 1, 600, delegate(int v) { _cfg.GenAllReadySeconds = v; TouchSave(); })));
            TextBox deconMap = VigilInput(_cfg.DeconAnnounceMap, 220);
            deconMap.TextChanged += delegate { _cfg.DeconAnnounceMap = deconMap.Text ?? ""; TouchSave(); };
            body.Children.Add(GeneralRow("净化播报对照", "播报时间→现实秒数，如 15m=652", deconMap));
            body.Children.Add(GeneralRow("净化同步容差", "秒", AdvNumber(_cfg.DeconSyncTolerance, 0, 120, delegate(int v) { _cfg.DeconSyncTolerance = v; TouchSave(); })));
            body.Children.Add(GeneralRow("核弹同步容差", "秒", AdvNumber(_cfg.WarheadSyncTolerance, 0, 120, delegate(int v) { _cfg.WarheadSyncTolerance = v; TouchSave(); })));
            body.Children.Add(GeneralRow("核弹以 OCR 为准", "读到 X 秒就重新起算", AdvToggle(_cfg.WarheadOcrAuthoritative, delegate(bool v) { _cfg.WarheadOcrAuthoritative = v; TouchSave(); })));
            body.Children.Add(GeneralRow("净化兜底", "没识别到播报也按时间推算", AdvToggle(_cfg.DeconFallback, delegate(bool v) { _cfg.DeconFallback = v; TouchSave(); })));
            body.Children.Add(GeneralRow("校准提示", "中途加入时询问是否按轻收容时间校准", AdvToggle(_cfg.CalibPromptEnabled, delegate(bool v) { _cfg.CalibPromptEnabled = v; TouchSave(); })));
            body.Children.Add(GeneralRow("数字播报提示", "15/10/5/1 分钟", AdvToggle(_cfg.CalibNumberPrompt, delegate(bool v) { _cfg.CalibNumberPrompt = v; TouchSave(); })));
            body.Children.Add(GeneralRow("封锁播报提示", "开始清除所有生物", AdvToggle(_cfg.CalibLockPrompt, delegate(bool v) { _cfg.CalibLockPrompt = v; TouchSave(); })));
            body.Children.Add(GeneralRow("校准提示停留", "秒；到点自动校准", AdvNumber(_cfg.CalibPromptSeconds, 2, 30, delegate(int v) { _cfg.CalibPromptSeconds = v; TouchSave(); })));

            StackPanel calRow = new StackPanel();
            calRow.Orientation = Orientation.Horizontal;
            VigilNumberField remain = new VigilNumberField(_light, Line, Ink, Sub, 86);
            remain.Minimum = 0;
            remain.Maximum = 600;
            remain.Value = _cfg.DeadManSeconds;
            calRow.Children.Add(remain);
            Button cal = CommandButton("\uE823", "按剩余秒数校准", delegate
            {
                double sec = (double)remain.Value;
                if (_calibrateDeadMan != null)
                {
                    _calibrateDeadMan(sec);
                    if (_deadmanCalStatus != null) _deadmanCalStatus.Text = "已校准：终焉从还剩 " + (int)sec + " 秒开始走。";
                }
                else
                {
                    RunOrLegacy(null, 4);
                }
            }, Accent);
            cal.Margin = new Thickness(8, 0, 0, 0);
            calRow.Children.Add(cal);
            body.Children.Add(GeneralRow("终焉此刻还剩", "填当前剩余秒数，立即对齐", calRow));

            _deadmanCalStatus = new TextBlock();
            _deadmanCalStatus.FontSize = 8.5;
            _deadmanCalStatus.Foreground = Sub;
            _deadmanCalStatus.TextWrapping = TextWrapping.Wrap;
            _deadmanCalStatus.Margin = new Thickness(0, 2, 0, 4);
            body.Children.Add(_deadmanCalStatus);

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE8A7", "扫描游戏日志校准", delegate { ShowLogTimeCalibration(); }, Accent));
            actions.Children.Add(CommandButton("\uE823", "完整时间设置", delegate { if (_openLegacyPage != null) _openLegacyPage(4); }, Sub));
            actions.Children.Add(CommandButton("\uE8A7", "打开战术校准", delegate { if (_calibrate != null) _calibrate(); }, Sub));
            body.Children.Add(actions);

            TextBlock note = new TextBlock();
            note.Text = "日志扫描会读服务端 Round*.txt 和本程序时间日志，成对算出真实秒数；结果列表与一键填入在「时间」页。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private UIElement BuildPositionCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("ALERT POSITION  /  提示位置", "DRAG", out body);
            body.Children.Add(GeneralRow("调整提示位置", "进入拖动模式，逐条拖动每条提示", CommandButton("\uE8A7", "开始调整", delegate { RunOrLegacy(_adjustPosition, 2); }, Accent)));
            body.Children.Add(GeneralRow("全部预览", "把所有提示先弹一遍再拖", CommandButton("\uE768", "预览全部", delegate { if (_previewAlerts != null) _previewAlerts(); }, Sub)));
            TextBlock key = MonoText("Ctrl+Alt+F8  进入 / 退出调整模式", 9, Accent2);
            key.Margin = new Thickness(0, 8, 0, 2);
            body.Children.Add(key);
            TextBlock note = new TextBlock();
            note.Text = "调整模式会把提示恢复鼠标响应；拖完点操作条上的“完成调整”，位置自动保存。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            body.Children.Add(note);
            return card;
        }

        private UIElement BuildBrightnessCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("BRIGHTNESS  /  游戏亮度", "GAMMA", out body);

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel();
            TextBlock title = DisplayText("伽马 / 饱和度", 18, Ink);
            title.FontWeight = FontWeights.Bold;
            left.Children.Add(title);
            TextBlock desc = new TextBlock();
            desc.Text = (_cfg.DisplayAssistEnabled ? "已启用" : "已关闭")
                + "　·　" + (_cfg.GameBrightnessHoldEnabled
                    ? "固定 γ " + (_cfg.GameBrightnessHoldGamma / 100.0).ToString("0.00")
                    : "跟随画面亮度")
                + "　·　饱和度 " + (_cfg.GameBrightnessHoldSaturation / 100.0).ToString("0.00");
            desc.FontSize = 11;
            desc.Foreground = Sub;
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(desc);
            head.Children.Add(left);

            Button more = OutlineButton("\uE713", "高级设置", delegate { ShowBrightnessPage(0); }, Accent);
            more.Margin = new Thickness(0);
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(more, 1);
            head.Children.Add(more);
            body.Children.Add(head);

            Grid main = new Grid();
            main.Margin = new Thickness(0, 12, 0, 0);
            main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

            StackPanel values = new StackPanel();
            values.Children.Add(BrightnessValueLine("当前预设",
                string.IsNullOrEmpty(_cfg.GameBrightnessPresetCurrent) ? "自定义调整" : _cfg.GameBrightnessPresetCurrent, Accent));
            values.Children.Add(BrightnessValueLine("自动补色增强", (_cfg.GameBrightnessCompensationBoost * 100).ToString("0") + "%", Ink));
            values.Children.Add(BrightnessValueLine("检测间隔", _cfg.GameBrightnessIntervalMs + " ms", Sub));
            values.Children.Add(BrightnessValueLine("全场景覆盖", _cfg.GameBrightnessForceGlobal ? "开" : "关", Sub));
            values.Cursor = Cursors.Hand;
            values.MouseLeftButtonUp += delegate { ShowBrightnessPage(0); };
            TextBlock adjust = new TextBlock();
            adjust.Text = "点这里或右上「高级设置」可改：固定伽马 / 固定饱和度 / 全场景覆盖 / 检测间隔 / 补色增强";
            adjust.FontSize = 9;
            adjust.Foreground = Muted;
            adjust.TextWrapping = TextWrapping.Wrap;
            adjust.Margin = new Thickness(0, 6, 0, 0);
            values.Children.Add(adjust);
            main.Children.Add(values);

            Border preview = new Border();
            preview.BorderThickness = new Thickness(1);
            preview.BorderBrush = Line;
            preview.CornerRadius = new CornerRadius(4);
            preview.Height = 96;
            preview.Background = PanelAlt;
            BrightnessCurveThumb thumb = new BrightnessCurveThumb();
            thumb.GammaText = _cfg.GameBrightnessProfile;
            thumb.SatText = _cfg.GameBrightnessSaturationProfile;
            thumb.Margin = new Thickness(6);
            preview.Child = thumb;
            Grid.SetColumn(preview, 2);
            main.Children.Add(preview);
            body.Children.Add(main);

            Grid bottom = new Grid();
            bottom.Margin = new Thickness(0, 14, 0, 0);
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel sw = new StackPanel();
            sw.Orientation = Orientation.Horizontal;
            VigilToggle master = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            master.IsChecked = _cfg.DisplayAssistEnabled;
            master.CheckedChanged += delegate
            {
                _cfg.DisplayAssistEnabled = master.IsChecked;
                TouchSave();
                _advancedPage.Content = null;
            };
            sw.Children.Add(master);
            TextBlock swl = new TextBlock();
            swl.Text = "应用伽马 / 饱和度";
            swl.FontSize = 11.5;
            swl.Foreground = Sub;
            swl.VerticalAlignment = VerticalAlignment.Center;
            swl.Margin = new Thickness(10, 0, 0, 0);
            sw.Children.Add(swl);
            bottom.Children.Add(sw);

            ComboBox combo = new ComboBox();
            combo.Width = 240;
            combo.Height = 32;
            combo.FontSize = 12;
            combo.Foreground = Ink;
            combo.Background = PanelAlt;
            combo.BorderBrush = Line;
            foreach (GameBrightnessPreset p in GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets))
                combo.Items.Add(PresetRow(p.Name, p.Gamma, p.Saturation));
            combo.Items.Add(PresetRow("自定义调整", _cfg.GameBrightnessProfile, _cfg.GameBrightnessSaturationProfile));
            int sel = combo.Items.Count - 1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboBoxItem ci = combo.Items[i] as ComboBoxItem;
                if (ci != null && ci.Tag != null && ci.Tag.ToString() == _cfg.GameBrightnessPresetCurrent) { sel = i; break; }
            }
            bool loadingCombo = true;
            combo.SelectedIndex = sel;
            loadingCombo = false;
            combo.SelectionChanged += delegate
            {
                if (loadingCombo) return;
                ComboBoxItem ci = combo.SelectedItem as ComboBoxItem;
                string name = ci != null && ci.Tag != null ? ci.Tag.ToString() : "";
                ApplyBrightnessPreset(name);
            };
            Grid.SetColumn(combo, 1);
            bottom.Children.Add(combo);
            body.Children.Add(bottom);

            TextBlock note = new TextBlock();
            note.Text = "“高级设置”里有曲线调整区（拖点 / 预设 / 导入导出 / 500ms 自动保存）和采集区（采 21 张、挑 7 张）。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private StackPanel BrightnessValueLine(string label, string value, Brush valueBrush)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 6);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 10.5;
            l.Foreground = Sub;
            row.Children.Add(l);
            TextBlock v = new TextBlock();
            v.Text = value;
            v.FontSize = 11.5;
            v.Foreground = valueBrush;
            v.FontFamily = Mono;
            Grid.SetColumn(v, 1);
            row.Children.Add(v);
            StackPanel sp = new StackPanel();
            sp.Children.Add(row);
            return sp;
        }

        /// <summary>主卡片上的橙色预设下拉：选中即应用该预设的两条曲线。</summary>
        private void ApplyBrightnessPreset(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "自定义调整")
            {
                _cfg.GameBrightnessPresetCurrent = "";
                TouchSave();
                return;
            }
            GameBrightnessPreset preset = GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets)
                .Find(delegate(GameBrightnessPreset p) { return p.Name == name; });
            if (preset == null) return;
            if (GameBrightnessProfile.Count(preset.Gamma) < 3) return;
            _cfg.GameBrightnessProfile = preset.Gamma;
            _cfg.GameBrightnessSaturationProfile = preset.Saturation;
            _cfg.GameBrightnessPresetCurrent = name;
            TouchSave();
        }

        private UIElement BuildCrosshairCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("CROSSHAIR  /  准星", "OVERLAY", out body);

            // 一级入口：卡片里直接给实时预览，下面是总开关和「编辑准星」
            body.Children.Add(CrosshairLiveThumb(176));
            body.Children.Add(CrosshairSummaryText());

            body.Children.Add(GeneralRow("总开关", "仅游戏前台显示", AdvToggle(_cfg.CrosshairEnabled,
                delegate(bool v) { _cfg.CrosshairEnabled = v; TouchSave(); })));
            body.Children.Add(GeneralRow("动态反馈", "关掉就是纯静态准星", AdvToggle(_ch.DynamicEnabled,
                delegate(bool v) { _ch.DynamicEnabled = v; CrosshairInvalidate(); })));
            body.Children.Add(GeneralRow("左键扩散", "按住张开、松开按曲线收回", AdvToggle(_ch.FireEnabled,
                delegate(bool v) { _ch.FireEnabled = v; CrosshairInvalidate(); })));

            WrapPanel actions = AdvActions();
            actions.Children.Add(OutlineButton("\uE70F", "编辑准星", delegate { OpenCrosshairWorkbench(); }, Accent));
            actions.Children.Add(CommandButton("\uE823", "更多准星设置", delegate { if (_openLegacyPage != null) _openLegacyPage(7); }, Sub));
            body.Children.Add(actions);

            TextBlock note = new TextBlock();
            note.Text = "点「编辑准星」进工作台：形态、双线组、颜色、描边、开火扩散、按键回弹、位置偏移，右侧实时预览。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private UIElement CrosshairColorControl(bool outline)
        {
            StackPanel s = new StackPanel();
            s.Orientation = Orientation.Horizontal;
            s.Margin = new Thickness(0, 0, 10, 0);
            Border swatch = new Border();
            swatch.Width = 18;
            swatch.Height = 18;
            swatch.CornerRadius = new CornerRadius(3);
            swatch.BorderBrush = Line;
            swatch.BorderThickness = new Thickness(1);
            swatch.VerticalAlignment = VerticalAlignment.Center;
            System.Drawing.Color initial = System.Drawing.Color.FromArgb(
                outline ? _cfg.CrosshairOutlineColor : _cfg.CrosshairColor);
            swatch.Background = new SolidColorBrush(Color.FromArgb(initial.A, initial.R, initial.G, initial.B));
            s.Children.Add(swatch);
            Button pick = CommandButton("\uE8A5", outline ? "描边色" : "主色", delegate
            {
                using (System.Windows.Forms.ColorDialog d = new System.Windows.Forms.ColorDialog())
                {
                    int argb = outline ? _cfg.CrosshairOutlineColor : _cfg.CrosshairColor;
                    d.Color = System.Drawing.Color.FromArgb(argb);
                    d.FullOpen = true;
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    if (outline) _cfg.CrosshairOutlineColor = d.Color.ToArgb();
                    else _cfg.CrosshairColor = d.Color.ToArgb();
                    System.Drawing.Color c = d.Color;
                    swatch.Background = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                    TouchSave();
                }
            }, Sub);
            pick.Margin = new Thickness(4, 0, 0, 0);
            s.Children.Add(pick);
            return s;
        }

        private UIElement BuildHeartRateCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("HEART RATE  /  心率", "BLE", out body);
            card.Visibility = _heartSupported ? Visibility.Visible : Visibility.Collapsed;

            VigilToggle toggle = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            _heartToggle = toggle;
            toggle.IsChecked = _cfg.HeartRateBleEnabled;
            toggle.CheckedChanged += delegate
            {
                _cfg.HeartRateBleEnabled = toggle.IsChecked;
                TouchSave();
                if (toggle.IsChecked)
                {
                    EnsureHeartSource();
                    try { _heartSource.Start(); } catch { }
                    StartHeartSafety();
                }
                else
                {
                    if (_heartSource != null)
                    {
                        try { _heartSource.Stop(); } catch { }
                    }
                    StopHeartSafety();
                }
                if (!_heartAutoOff) UpdateHeartStatusText();
            };
            body.Children.Add(GeneralRow("蓝牙心率", "打开后自动搜索并连接", toggle));
            body.Children.Add(GeneralRow("波形速度", "像素/秒；越大越快", AdvNumber(
                _cfg.HeartRateEcgSpeed, 20, 140, delegate(int v)
                {
                    _cfg.HeartRateEcgSpeed = v;
                    ApplyEcgSettings();
                    TouchSave();
                })));
            body.Children.Add(GeneralRow("波形振幅", "百分比；随机波形始终保留", AdvNumber(
                _cfg.HeartRateEcgAmplitude, 50, 150, delegate(int v)
                {
                    _cfg.HeartRateEcgAmplitude = v;
                    ApplyEcgSettings();
                    TouchSave();
                })));

            Grid live = new Grid();
            live.Margin = new Thickness(0, 8, 0, 2);
            live.ColumnDefinitions.Add(new ColumnDefinition());
            live.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _heartStatusText = MonoText("--", 8.5, Sub);
            _heartStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
            live.Children.Add(_heartStatusText);
            _heartBpmText = MonoText("-- BPM", 15, Accent);
            _heartBpmText.FontWeight = FontWeights.Bold;
            _heartBpmText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_heartBpmText, 1);
            live.Children.Add(_heartBpmText);
            body.Children.Add(live);

            WrapPanel actions = AdvActions();
            actions.Children.Add(HeartButton("搜索手环", delegate
            {
                if (!_cfg.HeartRateBleEnabled)
                {
                    _cfg.HeartRateBleEnabled = true;
                    toggle.IsChecked = true;
                    TouchSave();
                }
                EnsureHeartSource();
                try { _heartSource.Start(); _heartSource.Scan(); } catch { }
            }, true));
            actions.Children.Add(HeartButton("解绑", delegate
            {
                _cfg.HeartRateBleAddress = "";
                _cfg.HeartRateBleName = "";
                TouchSave();
                if (_heartSource != null)
                {
                    try { _heartSource.ClearPreferred(); } catch { }
                }
                _heartDevices.Clear();
                UpdateHeartPreferredText();
                RebuildHeartDeviceList();
                if (_heartStatusText != null) _heartStatusText.Text = "已解绑，正在重新搜索…";
            }, false));
            body.Children.Add(actions);

            _heartPreferredText = MonoText("", 8, Sub);
            _heartPreferredText.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(_heartPreferredText);
            UpdateHeartPreferredText();

            _heartDeviceList = new StackPanel();
            _heartDeviceList.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(_heartDeviceList);
            RebuildHeartDeviceList();

            TextBlock note = new TextBlock();
            note.Text = "手环 设置 → Share HR / 心率广播 → 打开。打开开关后会自动搜索，默认连接第一个扫到的心率设备。\n"
                + "屏幕上的心率提示在 提示 → 心率提示：可选完整卡片 / 无框 / 纯数字，位置和颜色也能改。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);

            if (_heartSupported && _cfg.HeartRateBleEnabled)
            {
                EnsureHeartSource();
                try { _heartSource.Start(); } catch { }
                StartHeartSafety();
            }
            UpdateHeartStatusText();
            return card;
        }

        private void UpdateHeartPreferredText()
        {
            if (_heartPreferredText == null) return;
            string name = _cfg.HeartRateBleName;
            if (string.IsNullOrEmpty(name)) name = _cfg.HeartRateBleAddress;
            _heartPreferredText.Text = string.IsNullOrEmpty(name) ? "我的设备：未设置" : "我的设备：" + name;
        }

        private void RebuildHeartDeviceList()
        {
            if (_heartDeviceList == null) return;
            _heartDeviceList.Children.Clear();

            if (_heartDevices.Count == 0)
            {
                TextBlock empty = MonoText(_cfg.HeartRateBleEnabled ? "正在搜索心率设备…" : "打开开关后开始搜索", 8, Muted);
                empty.Margin = new Thickness(0, 2, 0, 2);
                _heartDeviceList.Children.Add(empty);
                return;
            }

            List<BleHeartDevice> list = new List<BleHeartDevice>(_heartDevices.Values);
            list.Sort(delegate(BleHeartDevice a, BleHeartDevice b) { return b.Rssi.CompareTo(a.Rssi); });
            for (int i = 0; i < list.Count; i++)
            {
                BleHeartDevice d = list[i];
                Grid row = new Grid();
                row.Margin = new Thickness(0, 3, 0, 3);
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel labels = new StackPanel();
                TextBlock name = MonoText(d.Name, 8.5, HeartRateHub.BleConnected && HeartRateHub.BleDeviceName == d.Name ? Accent : Ink);
                labels.Children.Add(name);
                TextBlock sub = MonoText(d.AddressText + "   " + d.Rssi + " dBm", 7, Muted);
                labels.Children.Add(sub);
                row.Children.Add(labels);

                Border connect = HeartSmallButton("连接", delegate
                {
                    EnsureHeartSource();
                    try { _heartSource.SetPreferred(d.Address, d.Name); _heartSource.Start(); } catch { }
                }, false);
                Grid.SetColumn(connect, 1);
                row.Children.Add(connect);

                Border mine = HeartSmallButton("设为我的设备", delegate
                {
                    string chosen = d.Name;
                    if (_heartSource != null && _heartSource.ConnectedAddress == d.Address
                        && !string.IsNullOrEmpty(HeartRateHub.BleDeviceName)
                        && HeartRateHub.BleDeviceName != "心率设备")
                    {
                        chosen = HeartRateHub.BleDeviceName;
                    }
                    _cfg.HeartRateBleAddress = d.Address.ToString();
                    _cfg.HeartRateBleName = chosen;
                    TouchSave();
                    EnsureHeartSource();
                    try { _heartSource.SetPreferred(d.Address, chosen); _heartSource.Start(); } catch { }
                    UpdateHeartPreferredText();
                    if (_heartStatusText != null) _heartStatusText.Text = "已设为我的设备：" + chosen;
                    RebuildHeartDeviceList();
                }, true);
                Grid.SetColumn(mine, 2);
                row.Children.Add(mine);

                _heartDeviceList.Children.Add(row);
            }
        }

        private UIElement BuildAudioToolsCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("AUDIO TOOLS  /  音频片段", "CLIPS", out body);
            int segments = 0;
            try { segments = AudioLibrary.ParseSegments(_cfg.AudioSegments).Count; } catch { }
            TextBlock count = MonoText("当前自定义片段 " + segments + " 个 · 音频条目 " + AudioLibrary.List().Length + " 个", 9, Sub);
            count.Margin = new Thickness(0, 6, 0, 4);
            body.Children.Add(count);

            List<string> audioKeys = new List<string>();
            string[] files = AudioLibrary.List();
            for (int i = 0; i < files.Length; i++) audioKeys.Add(files[i]);
            try
            {
                List<AudioLibrary.Segment> segs = AudioLibrary.ParseSegments(_cfg.AudioSegments);
                for (int i = 0; i < segs.Count; i++) audioKeys.Add(AudioLibrary.SegmentPrefix + segs[i].Name);
            }
            catch { }
            VigilSelect clip = new VigilSelect(PanelAlt, Line, Ink, Accent, 200);
            clip.SetItems(audioKeys, audioKeys.Count > 0 ? 0 : -1);
            VigilNumberField volume = new VigilNumberField(_light, Line, Ink, Sub, 74);
            volume.Minimum = 0;
            volume.Maximum = 100;
            if (audioKeys.Count > 0) volume.Value = AudioLibrary.VolumeOf(_cfg.AudioVolumes, audioKeys[0]);
            volume.ValueChanged += delegate
            {
                if (audioKeys.Count == 0) return;
                string key = clip.SelectedText;
                if (string.IsNullOrEmpty(key)) return;
                _cfg.AudioVolumes = AudioLibrary.SetVolume(_cfg.AudioVolumes, key, (int)volume.Value);
                AudioLibrary.ApplyConfig(_cfg);
                TouchSave();
            };
            clip.SelectionChanged += delegate
            {
                if (audioKeys.Count == 0 || string.IsNullOrEmpty(clip.SelectedText)) return;
                volume.Value = AudioLibrary.VolumeOf(_cfg.AudioVolumes, clip.SelectedText);
            };
            StackPanel level = new StackPanel();
            level.Orientation = Orientation.Horizontal;
            level.Children.Add(clip);
            volume.Margin = new Thickness(8, 0, 0, 0);
            level.Children.Add(volume);
            body.Children.Add(GeneralRow("片段音量", "单独设置 0~100", level));

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE768", "试听", delegate { if (audioKeys.Count > 0) AudioLibrary.PlayKey(clip.SelectedText, "音频高级页试听"); }, Sub));
            actions.Children.Add(CommandButton("\uE73E", "分配给全部提示", delegate
            {
                string key = clip.SelectedText;
                if (string.IsNullOrEmpty(key)) return;
                for (int i = 0; i < _cfg.Alerts.Length; i++) _cfg.Alerts[i].AudioFile = key;
                if (_advAudioStatus != null) _advAudioStatus.Text = "已把「" + key + "」分配给全部 " + _cfg.Alerts.Length + " 条提示。";
                TouchSave();
            }, Accent));
            actions.Children.Add(CommandButton("\uE711", "清空该音频分配", delegate
            {
                string key = clip.SelectedText;
                if (string.IsNullOrEmpty(key)) return;
                for (int i = 0; i < _cfg.Alerts.Length; i++)
                    if (string.Equals(_cfg.Alerts[i].AudioFile, key, StringComparison.OrdinalIgnoreCase)) _cfg.Alerts[i].AudioFile = "";
                if (_advAudioStatus != null) _advAudioStatus.Text = "已清空「" + key + "」的所有提示分配。";
                TouchSave();
            }, Sub));
            body.Children.Add(actions);

            WrapPanel actions2 = AdvActions();
            actions2.Children.Add(CommandButton("\uE767", "打开音频页", delegate { SelectPage(3); }, Accent));
            actions2.Children.Add(CommandButton("\uE8D6", "完整音频设置（裁剪 / 导出）", delegate { if (_openLegacyPage != null) _openLegacyPage(3); }, Sub));
            actions2.Children.Add(CommandButton("\uE8E5", "导出全部片段", delegate { ExportAudioSegments(); }, Sub));
            body.Children.Add(actions2);

            _advAudioStatus = new TextBlock();
            _advAudioStatus.FontSize = 8.5;
            _advAudioStatus.Foreground = Muted;
            _advAudioStatus.TextWrapping = TextWrapping.Wrap;
            _advAudioStatus.Margin = new Thickness(0, 4, 0, 2);
            body.Children.Add(_advAudioStatus);

            TextBlock note = new TextBlock();
            note.Text = "裁剪片段、导出单个片段、按音频设置音量，以及“全选 / 清空提示音分配”都在完整音频页；新音频页保留导入、试听和触发点。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            body.Children.Add(note);
            return card;
        }

        private void ExportAudioSegments()
        {
            try
            {
                using (System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    d.Description = "选择导出音频片段的目录";
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    Tuple<int, string> result = AudioLibrary.ExportAllSegments(_cfg.AudioSegments, d.SelectedPath);
                    if (_advAudioStatus != null)
                        _advAudioStatus.Text = "导出完成：" + result.Item1 + " 个片段" + (string.IsNullOrEmpty(result.Item2) ? "" : "；" + result.Item2);
                }
            }
            catch (Exception ex)
            {
                if (_advAudioStatus != null) _advAudioStatus.Text = "导出失败：" + ex.Message;
            }
        }

        private UIElement ToolTile(int index, string glyph, string title, string desc, Action click)
        {
            Color accent = ColorOf(Accent);
            Color panel = ColorOf(Panel);
            Color panelAlt = ColorOf(PanelAlt);
            Color line = ColorOf(Line);

            Border tile = new Border();
            tile.Height = 132;
            tile.Margin = new Thickness(6);
            tile.Padding = new Thickness(16, 14, 16, 13);
            tile.CornerRadius = new CornerRadius(6);
            tile.BorderThickness = new Thickness(1);
            tile.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            Border iconBox = new Border();
            iconBox.Width = 32;
            iconBox.Height = 32;
            iconBox.CornerRadius = new CornerRadius(5);
            iconBox.Background = AccentSoftBrush();
            iconBox.BorderThickness = new Thickness(1);
            iconBox.BorderBrush = new SolidColorBrush(VigilButtonPalette.Fade(accent, 90));
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = Icons;
            icon.FontSize = 14;
            icon.Foreground = Accent;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            iconBox.Child = icon;
            top.Children.Add(iconBox);
            TextBlock number = MonoText("0" + index, 8, Muted);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            number.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(number, 1);
            top.Children.Add(number);
            g.Children.Add(top);

            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 12.5;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Ink;
            t.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(t, 1);
            g.Children.Add(t);

            TextBlock d = new TextBlock();
            d.Text = desc;
            d.FontSize = 9.5;
            d.Foreground = Sub;
            d.TextWrapping = TextWrapping.Wrap;
            d.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetRow(d, 2);
            g.Children.Add(d);

            StackPanel foot = new StackPanel();
            foot.Orientation = Orientation.Horizontal;
            foot.Margin = new Thickness(0, 8, 0, 0);
            TextBlock open = MonoText("打开", 8.5, Accent);
            open.FontWeight = FontWeights.Bold;
            open.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(open);
            TextBlock arrow = new TextBlock();
            arrow.Text = "  →";
            arrow.FontSize = 11;
            arrow.Foreground = Accent;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(arrow);
            Grid.SetRow(foot, 3);
            g.Children.Add(foot);

            tile.Child = g;
            new VigilPressState(tile,
                panel, panelAlt, VigilButtonPalette.Mix(panelAlt, accent, 0.14),
                line, accent, accent);
            tile.MouseLeftButtonUp += delegate { if (click != null) click(); };
            _hoverTargets.Add(tile);
            return tile;
        }

        private UIElement InfoLine(string name, string value)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock n = MonoText(name, 9, Muted);
            g.Children.Add(n);
            TextBlock v = MonoText(value, 9, Sub);
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private void OpenFolderSafe(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void ShowLogChoice()
        {
            if (_logChoiceLayer != null && _body != null)
            {
                _body.Children.Remove(_logChoiceLayer);
                _logChoiceLayer = null;
            }
            if (_crashListLayer != null && _body != null)
            {
                _body.Children.Remove(_crashListLayer);
                _crashListLayer = null;
            }
            _logChoiceLayer = BuildLogChoiceLayer();
            Grid.SetColumnSpan(_logChoiceLayer, 2);
            System.Windows.Controls.Panel.SetZIndex(_logChoiceLayer, 300);
            if (_body != null) _body.Children.Add(_logChoiceLayer);
            RefreshLogChoiceSizes();
            if (_logChoiceStatus != null) _logChoiceStatus.Text = "选择要查看的日志；也可以先打开日志目录。";
            _logChoiceLayer.Visibility = Visibility.Visible;
        }

        private void ShowCrashList()
        {
            if (_crashListLayer != null && _body != null)
            {
                _body.Children.Remove(_crashListLayer);
                _crashListLayer = null;
            }
            _crashListLayer = BuildCrashListLayer();
            Grid.SetColumnSpan(_crashListLayer, 2);
            System.Windows.Controls.Panel.SetZIndex(_crashListLayer, 320);
            if (_body != null) _body.Children.Add(_crashListLayer);
        }

        private void HideCrashList()
        {
            if (_crashListLayer != null) _crashListLayer.Visibility = Visibility.Collapsed;
            if (_logChoiceLayer != null) _logChoiceLayer.Visibility = Visibility.Visible;
        }

        private void ClearCrashRecords()
        {
            int count = GameCrashWatcher.SessionCount();
            if (count == 0) return;
            System.Windows.MessageBoxResult answer = System.Windows.MessageBox.Show(
                "删除全部 " + count + " 条崩溃记录？\r\n\r\n转储、日志和说明都会一起删除，无法恢复。",
                "SCPSLAlert", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            GameCrashWatcher.DeleteAll();
            RefreshGameCrashCard();
            HideCrashList();
            ShowLogChoice();
        }

        private Grid BuildCrashListLayer()
        {
            Grid overlay = new Grid();
            overlay.Background = new SolidColorBrush(_light
                ? Color.FromArgb(150, 30, 42, 52)
                : Color.FromArgb(178, 0, 0, 0));
            overlay.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay) HideCrashList();
            };

            VigilCard dialog = new VigilCard();
            dialog.Fill = Panel;
            dialog.Stroke = Danger;
            dialog.Cut = 14;
            dialog.Padding = new Thickness(22, 18, 22, 18);
            dialog.Width = 680;
            dialog.MaxHeight = 640;
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
            overlay.Children.Add(dialog);

            StackPanel content = new StackPanel();
            List<string> sessions = GameCrashWatcher.Sessions();

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(MonoText("GAME CRASH LOGS  /  游戏崩溃日志", 9.5, Danger));
            TextBlock big = DisplayText("崩溃记录", 28, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -2, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "每条记录都带退出码、原因、建议和调用栈。";
            desc.FontSize = 9.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 2, 0, 0);
            title.Children.Add(desc);
            head.Children.Add(title);
            TextBlock count = MonoText(sessions.Count + " 条", 8, Muted);
            count.VerticalAlignment = VerticalAlignment.Top;
            count.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetColumn(count, 1);
            head.Children.Add(count);
            content.Children.Add(head);

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = Line;
            sep.Margin = new Thickness(0, 12, 0, 10);
            content.Children.Add(sep);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.MaxHeight = 400;
            scroll.Padding = new Thickness(0, 0, 6, 0);
            StackPanel list = new StackPanel();
            if (sessions.Count == 0)
            {
                TextBlock empty = MonoText("还没有记录。开启监控后，游戏异常退出会出现在这里。", 9, Muted);
                empty.Margin = new Thickness(0, 6, 0, 6);
                list.Children.Add(empty);
            }
            else
            {
                for (int i = 0; i < sessions.Count; i++)
                    list.Children.Add(CrashSessionRow(sessions[i]));
            }
            scroll.Content = list;
            content.Children.Add(scroll);

            Grid foot = new Grid();
            foot.Margin = new Thickness(0, 12, 0, 0);
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button back = CommandButton("\uE72B", "返回", delegate { HideCrashList(); }, Accent);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            foot.Children.Add(back);
            Button clear = CommandButton("\uE74D", "清空记录", delegate { ClearCrashRecords(); }, Danger);
            Grid.SetColumn(clear, 1);
            foot.Children.Add(clear);
            Button dir = CommandButton("\uE8A5", "打开目录", delegate { OpenFolderSafe(CrashPaths.Dir(CrashPaths.GameCrash)); }, Sub);
            Grid.SetColumn(dir, 2);
            foot.Children.Add(dir);
            Button close = CommandButton("\uE711", "关闭", delegate { HideLogChoice(); HideCrashList(); }, Muted);
            Grid.SetColumn(close, 3);
            foot.Children.Add(close);
            content.Children.Add(foot);

            dialog.Child = content;
            return overlay;
        }

        private UIElement CrashSessionRow(string dir)
        {
            string summaryPath = System.IO.Path.Combine(dir, "summary.txt");
            string when = "";
            string summary = "";
            try
            {
                when = Directory.GetLastWriteTime(dir).ToString("yyyy-MM-dd HH:mm");
                if (File.Exists(summaryPath))
                {
                    string[] lines = File.ReadAllLines(summaryPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("摘要：")) summary = lines[i].Substring(3).Trim();
                        else if (summary.Length == 0 && lines[i].StartsWith("标题：")) summary = lines[i].Substring(3).Trim();
                    }
                }
            }
            catch { }

            Border row = new Border();
            row.CornerRadius = new CornerRadius(4);
            row.BorderThickness = new Thickness(1);
            row.BorderBrush = Line;
            row.Background = PanelAlt;
            row.Padding = new Thickness(12, 9, 12, 9);
            row.Margin = new Thickness(0, 0, 0, 7);
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            TextBlock time = MonoText(when, 9, Sub);
            time.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(time);

            TextBlock text = new TextBlock();
            text.Text = summary.Length == 0 ? "（没有摘要）" : summary;
            text.FontSize = 10.5;
            text.Foreground = Ink;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            TextBlock open = new TextBlock();
            open.Text = "\uE8A7";
            open.FontFamily = Icons;
            open.FontSize = 10;
            open.Foreground = Muted;
            open.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(open, 2);
            g.Children.Add(open);

            TextBlock remove = new TextBlock();
            remove.Text = "\uE74D";
            remove.FontFamily = Icons;
            remove.FontSize = 10;
            remove.Foreground = Muted;
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Cursor = Cursors.Hand;
            remove.ToolTip = "删除这条记录";
            Grid.SetColumn(remove, 3);
            g.Children.Add(remove);
            remove.MouseEnter += delegate { remove.Foreground = Danger; };
            remove.MouseLeave += delegate { remove.Foreground = Muted; };
            remove.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                DeleteCrashSession(dir);
            };

            row.Child = g;
            row.MouseEnter += delegate { row.BorderBrush = Danger; row.Background = Panel; open.Foreground = Danger; };
            row.MouseLeave += delegate { row.BorderBrush = Line; row.Background = PanelAlt; open.Foreground = Muted; };
            row.MouseLeftButtonUp += delegate
            {
                HideLogChoice();
                HideCrashList();
                CrashNotifier.HidePopup();
                using (LogViewerForm form = new LogViewerForm(summaryPath)) form.ShowDialog();
                CrashNotifier.ShowPopup();
            };
            return row;
        }

        private void DeleteCrashSession(string dir)
        {
            string name = System.IO.Path.GetFileName(dir);
            System.Windows.MessageBoxResult answer = System.Windows.MessageBox.Show(
                "删除这条崩溃记录？\r\n\r\n" + name + "\r\n\r\n转储、日志和说明会一起删除。",
                "SCPSLAlert", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            GameCrashWatcher.DeleteSession(dir);
            RefreshGameCrashCard();
            HideCrashList();
            ShowCrashList();
        }

        private void HideLogChoice()
        {
            if (_logChoiceLayer != null) _logChoiceLayer.Visibility = Visibility.Collapsed;
        }

        private Grid BuildLogChoiceLayer()
        {
            Grid overlay = new Grid();
            overlay.Background = new SolidColorBrush(_light
                ? Color.FromArgb(150, 30, 42, 52)
                : Color.FromArgb(178, 0, 0, 0));
            overlay.Visibility = Visibility.Collapsed;
            overlay.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay) HideLogChoice();
            };

            VigilCard dialog = new VigilCard();
            dialog.Fill = Panel;
            dialog.Stroke = Accent;
            dialog.Cut = 14;
            dialog.Padding = new Thickness(22, 18, 22, 18);
            dialog.Width = 610;
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
            overlay.Children.Add(dialog);

            StackPanel content = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(MonoText("LOG ACCESS  /  日志中心", 9.5, Accent));
            TextBlock big = DisplayText("打开日志", 28, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -2, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "选择一种日志，使用系统默认程序打开。也可以直接跳到日志目录。";
            desc.FontSize = 9.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 2, 0, 0);
            title.Children.Add(desc);
            head.Children.Add(title);
            TextBlock typeCount = MonoText("6 TYPES", 8, Muted);
            typeCount.VerticalAlignment = VerticalAlignment.Top;
            typeCount.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetColumn(typeCount, 1);
            head.Children.Add(typeCount);
            content.Children.Add(head);

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = Line;
            sep.Margin = new Thickness(0, 12, 0, 10);
            content.Children.Add(sep);

            string root = AppDomain.CurrentDomain.BaseDirectory;
            string main = LogPath(Logger.Path, root, "SCPSLAlert.log");
            string ocr = LogPath(Logger.OcrPath, root, "SCPSLAlert.OCR.log");
            string ticket = LogPath(Logger.TicketPath, root, "SCPSLAlert.票数.log");
            string ops = LogPath(Logger.OperationsPath, root, "SCPSLAlert.程序操作.log");
            string time = LogPath(Logger.TimePath, root, "SCPSLAlert.时间.log");

            content.Children.Add(LogChoiceRow("全部日志", "SCPSLAlert.log", "完整记录：事件、判定与提示", main, Accent));
            content.Children.Add(LogChoiceRow("OCR 识别", "SCPSLAlert.OCR.log", "屏幕识别原文、区域与解析结果", ocr, Accent2));
            content.Children.Add(LogChoiceRow("票数识别", "SCPSLAlert.票数.log", "死亡界面票数 / 影响力识别记录", ticket, GpuBrush));
            content.Children.Add(LogChoiceRow("程序操作", "SCPSLAlert.程序操作.log", "启动、热键、设置变更与异常", ops, Sub));
            content.Children.Add(LogChoiceRow("时间与倒计时", "SCPSLAlert.时间.log", "回合、刷新、净化、核弹时间线", time, DiskBrush));

            int crashSessions = GameCrashWatcher.SessionCount();
            content.Children.Add(LogChoiceRow("游戏崩溃日志", crashSessions + " 条记录",
                "退出码、原因、建议与调用栈", "", WpfTheme.Danger, delegate { ShowCrashList(); }));

            _logChoiceStatus = new TextBlock();
            _logChoiceStatus.Text = "";
            _logChoiceStatus.FontSize = 8.5;
            _logChoiceStatus.Foreground = Muted;
            _logChoiceStatus.TextWrapping = TextWrapping.Wrap;
            _logChoiceStatus.Margin = new Thickness(1, 4, 0, 7);
            content.Children.Add(_logChoiceStatus);

            Grid foot = new Grid();
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock hint = MonoText("日志按实际写入情况生成；没有内容时显示 --", 8, Muted);
            hint.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(hint);
            Button dir = CommandButton("\uE8A5", "打开日志目录", delegate { OpenLogDirectory(); }, Accent);
            dir.ToolTip = "在资源管理器中定位日志文件";
            Grid.SetColumn(dir, 1);
            foot.Children.Add(dir);
            Button cancel = CommandButton("\uE711", "取消", delegate { HideLogChoice(); }, Muted);
            Grid.SetColumn(cancel, 2);
            foot.Children.Add(cancel);
            content.Children.Add(foot);

            dialog.Child = content;
            return overlay;
        }

        private static string LogPath(string configured, string root, string fallback)
        {
            if (!string.IsNullOrEmpty(configured)) return configured;
            return System.IO.Path.Combine(root, fallback);
        }

        private UIElement LogChoiceRow(string title, string fileName, string desc, string path, Brush color)
        {
            return LogChoiceRow(title, fileName, desc, path, color, null);
        }

        private UIElement LogChoiceRow(string title, string fileName, string desc, string path, Brush color, Action onClick)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(4);
            row.BorderThickness = new Thickness(1);
            row.BorderBrush = Line;
            row.Background = PanelAlt;
            row.Padding = new Thickness(12, 9, 12, 9);
            row.Margin = new Thickness(0, 0, 0, 7);
            row.Cursor = Cursors.Hand;
            row.ToolTip = path;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

            Border iconBox = new Border();
            iconBox.Width = 28;
            iconBox.Height = 28;
            iconBox.CornerRadius = new CornerRadius(4);
            iconBox.BorderBrush = color;
            iconBox.BorderThickness = new Thickness(1);
            iconBox.VerticalAlignment = VerticalAlignment.Center;
            TextBlock icon = new TextBlock();
            icon.Text = "\uE7C3";
            icon.FontFamily = Icons;
            icon.FontSize = 12;
            icon.Foreground = color;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            iconBox.Child = icon;
            g.Children.Add(iconBox);

            StackPanel text = new StackPanel();
            text.Margin = new Thickness(10, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            StackPanel nameRow = new StackPanel();
            nameRow.Orientation = Orientation.Horizontal;
            TextBlock name = new TextBlock();
            name.Text = title;
            name.FontSize = 10.5;
            name.Foreground = Ink;
            name.FontWeight = FontWeights.Bold;
            nameRow.Children.Add(name);
            TextBlock file = MonoText("  " + fileName, 8, Muted);
            file.VerticalAlignment = VerticalAlignment.Center;
            nameRow.Children.Add(file);
            text.Children.Add(nameRow);
            TextBlock d = new TextBlock();
            d.Text = desc;
            d.FontSize = 8.5;
            d.Foreground = Sub;
            d.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(d);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            TextBlock size = MonoText(FileSize(path), 8.5, Muted);
            size.VerticalAlignment = VerticalAlignment.Center;
            size.Margin = new Thickness(8, 0, 4, 0);
            _logChoiceSizes.Add(size);
            _logChoicePaths.Add(path);
            Grid.SetColumn(size, 2);
            g.Children.Add(size);

            TextBlock openIcon = new TextBlock();
            openIcon.Text = "\uE8A7";
            openIcon.FontFamily = Icons;
            openIcon.FontSize = 9;
            openIcon.Foreground = Muted;
            openIcon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(openIcon, 3);
            g.Children.Add(openIcon);

            row.Child = g;
            row.MouseEnter += delegate { row.BorderBrush = color; row.Background = Panel; openIcon.Foreground = color; };
            row.MouseLeave += delegate { row.BorderBrush = Line; row.Background = PanelAlt; openIcon.Foreground = Muted; };
            row.MouseLeftButtonUp += delegate
            {
                if (onClick != null) onClick();
                else OpenLogFile(path, title);
            };
            if (!File.Exists(path)) row.Opacity = 0.62;
            return row;
        }

        private void RefreshLogChoiceSizes()
        {
            for (int i = 0; i < _logChoiceSizes.Count && i < _logChoicePaths.Count; i++)
                _logChoiceSizes[i].Text = FileSize(_logChoicePaths[i]);
        }

        private void OpenLogFile(string path, string title)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (_logChoiceStatus != null)
                    _logChoiceStatus.Text = title + " 还没有生成。可以先打开日志目录，等产生内容后再来。";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Logger.On("日志", "打开日志：" + path);
                HideLogChoice();
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                    Logger.On("日志", "用记事本打开日志：" + path);
                    HideLogChoice();
                }
                catch (Exception ex)
                {
                    if (_logChoiceStatus != null) _logChoiceStatus.Text = "打开失败：" + ex.Message;
                }
            }
        }

        private void ShowLogTimeCalibration()
        {
            if (_logTimeLayer == null)
            {
                _logTimeLayer = BuildLogTimeLayer();
                Grid.SetColumnSpan(_logTimeLayer, 2);
                System.Windows.Controls.Panel.SetZIndex(_logTimeLayer, 310);
                if (_body != null) _body.Children.Add(_logTimeLayer);
            }
            HideLogChoice();
            _logTimeLayer.Visibility = Visibility.Visible;
            StartLogTimeScan();
        }

        private void HideLogTimeCalibration()
        {
            if (_logTimeLayer != null) _logTimeLayer.Visibility = Visibility.Collapsed;
        }

        private Grid BuildLogTimeLayer()
        {
            Grid overlay = new Grid();
            overlay.Background = new SolidColorBrush(_light
                ? Color.FromArgb(150, 30, 42, 52)
                : Color.FromArgb(178, 0, 0, 0));
            overlay.Visibility = Visibility.Collapsed;
            overlay.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay) HideLogTimeCalibration();
            };

            VigilCard dialog = new VigilCard();
            dialog.Fill = Panel;
            dialog.Stroke = Accent;
            dialog.Cut = 14;
            dialog.Padding = new Thickness(22, 18, 22, 18);
            dialog.Width = 780;
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
            overlay.Children.Add(dialog);

            StackPanel content = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(MonoText("LOG CALIBRATION  /  日志时间校准", 9.5, Accent));
            TextBlock big = DisplayText("扫描游戏日志", 28, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -2, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "读服务端 Round*.txt 与本程序时间日志，把事件的真实间隔算出来；直接校准项可一键填入。";
            desc.FontSize = 9.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 2, 0, 0);
            desc.TextWrapping = TextWrapping.Wrap;
            title.Children.Add(desc);
            head.Children.Add(title);
            Button close = IconButton("\uE711", delegate { HideLogTimeCalibration(); }, Danger);
            close.ToolTip = "关闭";
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            content.Children.Add(head);

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = Line;
            sep.Margin = new Thickness(0, 12, 0, 8);
            content.Children.Add(sep);

            _logTimeStatus = new TextBlock();
            _logTimeStatus.FontSize = 9;
            _logTimeStatus.Foreground = Sub;
            _logTimeStatus.TextWrapping = TextWrapping.Wrap;
            _logTimeStatus.Margin = new Thickness(0, 0, 0, 8);
            content.Children.Add(_logTimeStatus);

            ScrollViewer scroll = new ScrollViewer();
            scroll.Height = 360;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _logTimeList = new StackPanel();
            scroll.Content = _logTimeList;
            content.Children.Add(scroll);

            Grid foot = new Grid();
            foot.Margin = new Thickness(0, 10, 0, 0);
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button rescan = CommandButton("\uE72C", "重新扫描", delegate { StartLogTimeScan(); }, Accent);
            Grid.SetColumn(rescan, 1);
            foot.Children.Add(rescan);
            Button full = CommandButton("\uE713", "完整时间页", delegate { HideLogTimeCalibration(); if (_openLegacyPage != null) _openLegacyPage(4); }, Sub);
            Grid.SetColumn(full, 2);
            foot.Children.Add(full);
            content.Children.Add(foot);

            dialog.Child = content;
            return overlay;
        }

        private void StartLogTimeScan()
        {
            if (_logTimeBusy) return;
            _logTimeBusy = true;
            if (_logTimeStatus != null) _logTimeStatus.Text = "正在扫描游戏日志…";
            if (_logTimeList != null) _logTimeList.Children.Clear();
            int deconOffset = _cfg.DeconAfterRound;
            int perPlayer = _cfg.WavePerPlayerSeconds;
            Task.Run(delegate
            {
                LogTimeScanner.Report report = null;
                string error = null;
                try
                {
                    LogTimeScanner.ScanOptions opt = new LogTimeScanner.ScanOptions();
                    opt.DeconOffsetSeconds = deconOffset;
                    opt.BigWavePlayers = 16;
                    opt.PerPlayerSeconds = perPlayer;
                    opt.MiniEntryDelay = 60;
                    report = LogTimeScanner.Scan(opt);
                }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate { FillLogTimeReport(report, error); }));
            });
        }

        private void FillLogTimeReport(LogTimeScanner.Report report, string error)
        {
            _logTimeBusy = false;
            if (_logTimeList == null) return;
            _logTimeList.Children.Clear();
            if (error != null)
            {
                _logTimeStatus.Text = "扫描失败：" + error;
                return;
            }
            if (report == null || (report.Intervals.Count == 0 && report.Moments.Count == 0))
            {
                _logTimeStatus.Text = "没扫到带时间戳的事件。需要服务端日志（ServerLogs\\…\\Round*.txt）或本程序的时间日志。";
                return;
            }
            _logTimeStatus.Text = "实测间隔 " + report.Intervals.Count + " 条、单个时刻 " + report.Moments.Count
                + " 条。每条间隔都可以点“填入”写进对应时长（填过的行会变绿，关掉窗口后恢复）；"
                + "单个时刻只作参考。";

            int shown = 0;
            for (int i = 0; i < report.Intervals.Count && shown < 24; i++)
            {
                LogTimeScanner.Interval it = report.Intervals[i];
                string kind;
                string action;
                double seconds;
                bool canFill = TryGetLogFill(it, out kind, out action, out seconds);
                AddLogTimeRow(it.Label, "实测 " + ShowSeconds(it.Seconds) + " 秒"
                    + (string.IsNullOrEmpty(it.Note) ? "" : " · " + it.Note),
                    canFill ? action : "仅参考", canFill ? kind : null, seconds);
                shown++;
            }
            for (int i = 0; i < report.Moments.Count && shown < 30; i++)
            {
                LogTimeScanner.Moment m = report.Moments[i];
                AddLogTimeRow(m.KindName, m.Time.ToString("MM-dd HH:mm:ss") + " · 仅参考",
                    "仅参考", null, 0);
                shown++;
            }
        }

        /// <summary>
        /// 一条扫描结果能不能直接写进设置、写多少。
        ///
        /// 【为什么要做减法】日志量到的是“两波之间的实际间隔”，而设置里存的是
        /// “基准值”，软件预测时还会自己加“人数 × 每人加时”（小波还要加进场延迟）。
        /// 所以这里先把那部分减掉，写进去的才是设置该填的数。
        /// 所有“实测间隔”行都能填（以前只有 direct* 行能填，其余全标“仅参考”）。
        /// </summary>
        private bool TryGetLogFill(LogTimeScanner.Interval it, out string kind,
            out string action, out double seconds)
        {
            kind = null;
            action = null;
            seconds = it.Seconds;
            if (it == null) return false;

            switch (it.Kind)
            {
                // ---- 扫描器已经算好的直接校准项：原样填 ----
                case "directDecon":
                    kind = "directDecon"; action = "填入 净化时长"; break;
                case "directWarhead":
                    kind = "directWarhead"; action = "填入 核弹倒计时"; break;
                case "directSupport":
                    kind = "directSupport"; action = "填入 支援刷新"; break;
                case "directWaveBig":
                    kind = "directWaveBig"; action = "填入 大波间隔"; break;
                case "directWaveSmall":
                    kind = "directWaveSmall"; action = "填入 小波等待"; break;

                // ---- 实测间隔：换算成设置值 ----
                case "deconCountdown":
                    kind = "directDecon"; action = "填入 净化时长"; break;
                case "deconEnd":
                    // 「回合开始 → 开始杀人」是总时长，按设置的“回合后多久净化”劈开
                    seconds = it.Seconds - _cfg.DeconAfterRound;
                    kind = "directDecon"; action = "填入 净化时长"; break;
                case "warhead":
                    kind = "directWarhead"; action = "填入 核弹倒计时"; break;
                case "support":
                    kind = "directSupport"; action = "填入 支援刷新"; break;
                case "waveBigGap":
                    // 大波 → 大波：减掉“上一波大波人数 × 每人加时”
                    seconds = it.Seconds - 16 * _cfg.WavePerPlayerSeconds;
                    kind = "directWaveBig"; action = "填入 大波间隔"; break;
                case "waveAfterSmallGap":
                    // 小波 → 下一次大波：减掉小波的典型人数
                    seconds = it.Seconds - 6 * _cfg.WavePerPlayerSeconds;
                    kind = "directWaveBig"; action = "填入 大波间隔"; break;
                case "waveSmallGap":
                    // 大波 → 小波：减掉小波进场延迟
                    seconds = it.Seconds - 60;
                    kind = "directWaveSmall"; action = "填入 小波等待"; break;

                default:
                    return false;
            }

            // 和设置页的上下限保持一致，别把 0 / 负数写进去
            int min = kind == "directDecon" ? 10
                : kind == "directWarhead" ? 1
                : kind == "directSupport" ? 10
                : 30;
            if (seconds < min) seconds = min;
            return true;
        }

        private void AddLogTimeRow(string label, string detail, string action,
            string fillKind, double fillSeconds)
        {
            Border row = new Border();
            row.BorderBrush = Line;
            row.BorderThickness = new Thickness(1);
            row.CornerRadius = new CornerRadius(4);
            row.Background = PanelAlt;
            row.Padding = new Thickness(10, 7, 10, 7);
            row.Margin = new Thickness(0, 0, 0, 5);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new StackPanel();
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 9;
            l.Foreground = Ink;
            l.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(l);
            TextBlock d = MonoText(detail, 8.5, Muted);
            d.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(d);
            g.Children.Add(text);
            if (fillKind != null)
            {
                string kind = fillKind;
                double seconds = fillSeconds;
                bool filled = false;
                Button[] holder = new Button[1];
                holder[0] = CommandButton("\uE73E", action, delegate
                {
                    // 一次性的：同一条只填一次；关掉窗口重扫后又是新的一行
                    if (filled) return;
                    if (!ApplyLogInterval(kind, seconds)) return;
                    filled = true;
                    Button self = holder[0];
                    row.Background = new SolidColorBrush(_light
                        ? Color.FromArgb(72, 188, 236, 206)
                        : Color.FromArgb(60, 40, 150, 92));
                    row.BorderBrush = new SolidColorBrush(_light
                        ? Color.FromArgb(190, 64, 190, 116)
                        : Color.FromArgb(200, 84, 214, 138));
                    if (self != null)
                    {
                        self.IsHitTestVisible = false;
                        self.Foreground = new SolidColorBrush(_light
                            ? Color.FromArgb(255, 32, 132, 76)
                            : Color.FromArgb(255, 118, 232, 160));
                        SetCommandText(self, "已填入");
                    }
                    _logTimeStatus.Text = "已填入：" + action.Replace("填入 ", "")
                        + "（" + ShowSeconds(seconds) + " 秒），配置已保存。";
                    // 时间页如果已经建好，里面的数字还是旧的 —— 清掉缓存，下次显示时重建
                    if (_timePage != null)
                    {
                        _timePage.Content = null;
                        if (_timePage.Visibility == Visibility.Visible) SelectPage(4);
                    }
                }, Accent);
                Button fill = holder[0];
                Grid.SetColumn(fill, 1);
                g.Children.Add(fill);
            }
            else
            {
                TextBlock tag = MonoText(action, 8, Muted);
                tag.VerticalAlignment = VerticalAlignment.Center;
                tag.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(tag, 1);
                g.Children.Add(tag);
            }
            row.Child = g;
            _logTimeList.Children.Add(row);
        }

        /// <summary>把 CommandButton 里的文字换掉（图标保留）。</summary>
        private static void SetCommandText(Button button, string text)
        {
            StackPanel sp = button == null ? null : button.Content as StackPanel;
            if (sp == null || sp.Children.Count < 2) return;
            TextBlock label = sp.Children[1] as TextBlock;
            if (label != null) label.Text = text;
        }

        private static string ShowSeconds(double seconds)
        {
            return Math.Abs(seconds - Math.Round(seconds)) < 0.05
                ? ((int)Math.Round(seconds)).ToString() : seconds.ToString("0.0");
        }

        private bool ApplyLogInterval(string kind, double seconds)
        {
            int v = (int)Math.Round(seconds);
            if (v < 0) v = 0;
            switch (kind)
            {
                case "directDecon":
                    v = Math.Min(36000, Math.Max(10, v));
                    _cfg.DeconBaseSeconds = v;
                    break;
                case "directWarhead":
                    v = Math.Min(36000, Math.Max(1, v));
                    _cfg.Warhead.DurationSeconds = v;
                    break;
                case "directSupport":
                    v = Math.Min(36000, Math.Max(10, v));
                    _cfg.SupportAfterRound = v;
                    break;
                case "directWaveBig":
                    v = Math.Min(3600, Math.Max(30, v));
                    _cfg.WaveIntervalSeconds = v;
                    break;
                case "directWaveSmall":
                    v = Math.Min(900, Math.Max(30, v));
                    _cfg.MiniWaveDelaySeconds = v;
                    break;
                default: return false;
            }
            TouchSave();
            Logger.On("校准", "日志扫描填入 " + kind + " = " + v + " 秒");
            return true;
        }

        private UIElement BuildHotkeyPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("热键", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);

            string[] groups = { "PANEL / 界面", "TIME / 时间", "SCAN / AUDIO" };
            Grid grid = new Grid();
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            page.Children.Add(grid);
            for (int i = 0; i < 3; i++)
            {
                VigilCard card = new VigilCard();
                card.Fill = Panel;
                card.Stroke = Line;
                card.Cut = 10;
                card.Padding = new Thickness(14, 12, 14, 14);
                StackPanel body = new StackPanel();
                body.Children.Add(MonoText(groups[i], 9, Accent));
                StackPanel wrap = new StackPanel();
                wrap.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(wrap);
                if (i == 0)
                {
                    AddKeycap(wrap, "调整提示位置", "edit", delegate { return _cfg.HotkeyAdjust; }, delegate(string v) { _cfg.HotkeyAdjust = v; });
                    AddKeycap(wrap, "显示 / 隐藏全部", "toggleAll", delegate { return _cfg.HotkeyToggleAll; }, delegate(string v) { _cfg.HotkeyToggleAll = v; });
                    AddKeycap(wrap, "打开设置", "settings", delegate { return _cfg.HotkeySettings; }, delegate(string v) { _cfg.HotkeySettings = v; });
                    AddKeycap(wrap, "战术面板", "panel", delegate { return _cfg.HotkeyPanel; }, delegate(string v) { _cfg.HotkeyPanel = v; });
                    AddKeycap(wrap, "战术面板（备用）", "tactical", delegate { return _cfg.HotkeyTactical; }, delegate(string v) { _cfg.HotkeyTactical = v; });
                    AddKeycap(wrap, "按住显示全部", "", delegate { return _cfg.HotkeyHoldShowAll; }, delegate(string v) { _cfg.HotkeyHoldShowAll = v; });
                }
                else if (i == 1)
                {
                    AddKeycap(wrap, "时间校准", "calibrate", delegate { return _cfg.HotkeyCalibrate; }, delegate(string v) { _cfg.HotkeyCalibrate = v; });
                    AddKeycap(wrap, "核弹开始", "warhead", delegate { return _cfg.HotkeyWarhead; }, delegate(string v) { _cfg.HotkeyWarhead = v; });


                    AddKeycap(wrap, "净化开始", "decon", delegate { return _cfg.HotkeyDecon; }, delegate(string v) { _cfg.HotkeyDecon = v; });
                    AddKeycap(wrap, "刷新校准", "bigwave", delegate { return _cfg.HotkeyBigWave; }, delegate(string v) { _cfg.HotkeyBigWave = v; });
                    AddKeycap(wrap, "计时暂停", "clockpause", delegate { return _cfg.HotkeyClockPause; }, delegate(string v) { _cfg.HotkeyClockPause = v; });
                }
                else
                {
                    AddKeycap(wrap, "立即扫描", "scannow", delegate { return _cfg.HotkeyScanNow; }, delegate(string v) { _cfg.HotkeyScanNow = v; });
                    AddKeycap(wrap, "停止 / 恢复全部", "stopall", delegate { return _cfg.HotkeyStopAll; }, delegate(string v) { _cfg.HotkeyStopAll = v; });
                    AddKeycap(wrap, "停止音频", "stopaudio", delegate { return _cfg.HotkeyStopAudio; }, delegate(string v) { _cfg.HotkeyStopAudio = v; });

                }
                card.Child = body;
                _hoverTargets.Add(card);
                Grid.SetColumn(card, i * 2);
                grid.Children.Add(card);
            }


            return page;
        }

        private void AddKeycap(Panel host, string label, string action, Func<string> get, Action<string> set)
        {
            Border cap = new Border();
            cap.Margin = new Thickness(0, 0, 0, 6);
            cap.HorizontalAlignment = HorizontalAlignment.Stretch;
            cap.Tag = new SettingMeta { Title = label, Desc = "快捷键" };
            cap.Padding = new Thickness(12, 8, 12, 8);
            cap.CornerRadius = new CornerRadius(5);
            cap.BorderThickness = new Thickness(1);
            cap.BorderBrush = Line;
            cap.Background = PanelAlt;
            cap.Cursor = Cursors.Hand;

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel b = new StackPanel();
            TextBlock name = new TextBlock();
            name.Text = label;
            name.FontSize = 9;
            name.Foreground = Muted;
            b.Children.Add(name);
            TextBlock key = MonoText(get(), 15, Ink);
            key.FontWeight = FontWeights.Bold;
            key.Margin = new Thickness(0, 3, 0, 0);
            b.Children.Add(key);
            TextBlock hint = new TextBlock();
            hint.Text = "点击后按新键";
            hint.FontSize = 8;
            hint.Foreground = Muted;
            hint.Margin = new Thickness(0, 1, 0, 0);
            b.Children.Add(hint);
            row.Children.Add(b);

            Border toggleHost = null;
            if (!string.IsNullOrEmpty(action))
            {
                StackPanel right = new StackPanel();
                right.Orientation = Orientation.Horizontal;
                right.VerticalAlignment = VerticalAlignment.Center;

                TextBlock bgLabel = new TextBlock();
                bgLabel.Text = "后台";
                bgLabel.FontSize = 8.5;
                bgLabel.Foreground = Muted;
                bgLabel.VerticalAlignment = VerticalAlignment.Center;
                bgLabel.Margin = new Thickness(0, 0, 6, 0);
                bgLabel.ToolTip = "关 = 只在游戏前台生效并吞掉按键；开 = 游戏后台也生效，但按键照样传给游戏";
                right.Children.Add(bgLabel);

                VigilToggle bg = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
                bg.IsChecked = _cfg.IsHotkeyBackground(action);
                bg.CheckedChanged += delegate
                {
                    _cfg.SetHotkeyBackground(action, bg.IsChecked);
                    TouchSave();   // 后台开关在按键过滤时实时读取，不需要重新注册，避免弹“没有设置快捷键”
                };
                toggleHost = new Border();
                toggleHost.Child = bg;
                toggleHost.VerticalAlignment = VerticalAlignment.Center;
                right.Children.Add(toggleHost);

                Grid.SetColumn(right, 1);
                row.Children.Add(right);
            }

            cap.Child = row;
            cap.MouseEnter += delegate { if (toggleHost == null || !toggleHost.IsMouseOver) cap.BorderBrush = Accent; };
            cap.MouseLeave += delegate { if (_recordHotkeyBorder != cap) cap.BorderBrush = Line; };
            cap.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (toggleHost != null && IsUnder(e.OriginalSource as DependencyObject, toggleHost)) return;
                if (_recordHotkeyBorder != null && _recordHotkeyBorder != cap)
                {
                    _recordHotkeyBorder.BorderBrush = Line;
                    if (_recordHotkeyText != null && !string.IsNullOrEmpty(_recordHotkeyOldText))
                        _recordHotkeyText.Text = _recordHotkeyOldText;
                }
                _recordHotkey = set;
                _recordHotkeyText = key;
                _recordHotkeyOldText = get();
                _recordHotkeyBorder = cap;
                cap.BorderBrush = Accent;
                key.Text = "按下新键…";
                hint.Text = "Esc 取消";
            };
            host.Children.Add(cap);
        }

        private static bool IsUnder(DependencyObject source, DependencyObject ancestor)
        {
            DependencyObject node = source;
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            Key searchKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_searchLayer != null && _searchLayer.Visibility == Visibility.Visible
                && searchKey == Key.Escape)
            {
                HideSearch();
                e.Handled = true;
                return;
            }
            if (searchKey == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ShowSearch();
                e.Handled = true;
                return;
            }

            if (_logTimeLayer != null && _logTimeLayer.Visibility == Visibility.Visible)
            {
                Key timeKey = e.Key == Key.System ? e.SystemKey : e.Key;
                if (timeKey == Key.Escape)
                {
                    HideLogTimeCalibration();
                    e.Handled = true;
                    return;
                }
            }
            if (_logChoiceLayer != null && _logChoiceLayer.Visibility == Visibility.Visible)
            {
                Key closeKey = e.Key == Key.System ? e.SystemKey : e.Key;
                if (closeKey == Key.Escape)
                {
                    HideLogChoice();
                    e.Handled = true;
                    return;
                }
            }
            if (_recordHotkey == null) return;
            DependencyObject textNode = e.OriginalSource as DependencyObject;
            while (textNode != null && !(textNode is TextBox))
                textNode = VisualTreeHelper.GetParent(textNode);
            if (textNode is TextBox) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
                return;
            if (key == Key.Escape)
            {
                if (_recordHotkeyText != null && _recordHotkeyBorder != null)
                {
                    _recordHotkeyText.Text = string.IsNullOrEmpty(_recordHotkeyOldText) ? _recordHotkeyText.Text : _recordHotkeyOldText;
                    _recordHotkeyBorder.BorderBrush = Line;
                }
                _recordHotkey = null;
                e.Handled = true;
                return;
            }
            int vk = KeyInterop.VirtualKeyFromKey(key);
            ModifierKeys m = Keyboard.Modifiers;
            string text = HotkeySpec.Format(vk, (m & ModifierKeys.Control) != 0, (m & ModifierKeys.Alt) != 0,
                (m & ModifierKeys.Shift) != 0, (m & ModifierKeys.Windows) != 0);
            _recordHotkey(text);
            if (_recordHotkeyText != null) _recordHotkeyText.Text = text;
            if (_recordHotkeyBorder != null) _recordHotkeyBorder.BorderBrush = Line;
            _recordHotkey = null;
            if (_hotkeysChanged != null) _hotkeysChanged();
            TouchSave();
            e.Handled = true;
        }
        private UIElement BuildTimePage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("时间", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);
            page.Children.Add(AdvancedBanner("time", "核弹倒计时 / 取消重启、发电机提示停留与终焉即时校准"));

            VigilCard timeline = new VigilCard();
            timeline.Fill = Panel;
            timeline.Stroke = Line;
            timeline.Cut = 10;
            timeline.Padding = new Thickness(20, 14, 20, 14);
            timeline.Margin = new Thickness(0, 0, 0, 12);
            Grid tl = new Grid();
            tl.Height = 78;
            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.VerticalAlignment = VerticalAlignment.Top;
            line.Margin = new Thickness(0, 22, 0, 0);
            tl.Children.Add(line);
            UniformGrid nodes = new UniformGrid();
            nodes.Columns = 4;
            TextBlock t1, t2, t3, t4;
            nodes.Children.Add(TimeNode("角色分配", _cfg.RoundStartDelay + " s", out t1));
            nodes.Children.Add(TimeNode("回合开始", "+" + _cfg.RoundStartOffsetSeconds + " s", out t2));
            nodes.Children.Add(TimeNode("净化", _cfg.DeconAfterRound + " s", out t3));
            nodes.Children.Add(TimeNode("支援", _cfg.SupportAfterRound + " s", out t4));
            _timeRoleText = t1;
            _timeOffsetText = t2;
            _timeDeconText = t3;
            _timeSupportText = t4;
            tl.Children.Add(nodes);
            timeline.Child = tl;
            page.Children.Add(timeline);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel left = new StackPanel();
            StackPanel right = new StackPanel();
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            grid.Children.Add(left);
            grid.Children.Add(right);

            StackPanel rb;
            VigilCard round = StyleCard("ROUND", out rb);
            VigilNumberField delay = TimeNumber(_cfg.RoundStartDelay, 0, 600, "秒", delegate(int v) { _cfg.RoundStartDelay = v; });
            rb.Children.Add(GeneralRow("分配角色时长", "进图后等多久开始计时", delay));
            VigilNumberField offset = TimeNumber(_cfg.RoundStartOffsetSeconds, 0, 60, "秒", delegate(int v) { _cfg.RoundStartOffsetSeconds = v; });
            rb.Children.Add(GeneralRow("回合开始补偿", "日志时间比真实开始早多少", offset));
            VigilToggle auto = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            auto.IsChecked = _cfg.AutoCalibrateOnJoin;
            auto.CheckedChanged += delegate { _cfg.AutoCalibrateOnJoin = auto.IsChecked; TouchSave(); };
            rb.Children.Add(GeneralRow("进服自动校准", "加入服务器时弹出校准", auto));
            VigilToggle role = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            role.IsChecked = _cfg.RoundStartOnRole;
            role.CheckedChanged += delegate { _cfg.RoundStartOnRole = role.IsChecked; TouchSave(); };
            rb.Children.Add(GeneralRow("按角色开始计时", "认出正式角色就起表", role));
            TextBox roundKeys = VigilInput(_cfg.RoundStartKeys, 240);
            roundKeys.TextChanged += delegate { _cfg.RoundStartKeys = roundKeys.Text ?? ""; TouchSave(); };
            rb.Children.Add(GeneralRow("回合开始标志", "逗号分隔，命中就开始计时", roundKeys));
            left.Children.Add(round);

            StackPanel wb;
            VigilCard wave = StyleCard("WAVE", out wb);
            VigilNumberField wo = TimeNumber(_cfg.WaveOffsetSeconds, 0, 120, "秒", delegate(int v) { _cfg.WaveOffsetSeconds = v; });
            wb.Children.Add(GeneralRow("刷新波偏移", "推算时间修正", wo));
            VigilNumberField wi = TimeNumber(_cfg.WaveIntervalSeconds, 30, 3600, "秒", delegate(int v) { _cfg.WaveIntervalSeconds = v; });
            wb.Children.Add(GeneralRow("大波基准间隔", "两次重生波之间", wi));
            VigilNumberField wp = TimeNumber(_cfg.WavePerPlayerSeconds, 0, 120, "秒", delegate(int v) { _cfg.WavePerPlayerSeconds = v; });
            wb.Children.Add(GeneralRow("每人加时", "上一波每人加时", wp));
            VigilNumberField md = TimeNumber(_cfg.MiniWaveDelaySeconds, 30, 900, "秒", delegate(int v) { _cfg.MiniWaveDelaySeconds = v; });
            wb.Children.Add(GeneralRow("小波最早延迟", "大波后最早多久出小波", md));
            VigilNumberField mw = TimeNumber(_cfg.MiniWaveWindowSeconds, 10, 600, "秒", delegate(int v) { _cfg.MiniWaveWindowSeconds = v; });
            wb.Children.Add(GeneralRow("小波判定窗口", "窗口内票数达标才算", mw));
            VigilNumberField mt = TimeNumber(_cfg.MiniWaveTickets, 1, 1000, "票", delegate(int v) { _cfg.MiniWaveTickets = v; });
            wb.Children.Add(GeneralRow("小波票数", "窗口内需要多少票", mt));
            left.Children.Add(wave);

            StackPanel db;
            VigilCard decon = StyleCard("DECON / SUPPORT", out db);
            VigilNumberField da = TimeNumber(_cfg.DeconAfterRound, 10, 36000, "秒", delegate(int v) { _cfg.DeconAfterRound = v; });
            db.Children.Add(GeneralRow("回合后多久净化", "没识别到播报时的兜底", da));
            VigilNumberField sa = TimeNumber(_cfg.SupportAfterRound, 10, 36000, "秒", delegate(int v) { _cfg.SupportAfterRound = v; });
            db.Children.Add(GeneralRow("回合后多久支援", "刷新推算", sa));
            VigilNumberField dl = TimeNumber(_cfg.DeconBaseSeconds, 10, 36000, "秒", delegate(int v) { _cfg.DeconBaseSeconds = v; });
            db.Children.Add(GeneralRow("净化计时长度", "从起表开始倒计时", dl));
            right.Children.Add(decon);

            StackPanel mb;
            VigilCard dead = StyleCard("DEADMAN", out mb);
            VigilNumberField ds = TimeNumber(_cfg.DeadManSeconds, 10, 600, "秒", delegate(int v) { _cfg.DeadManSeconds = v; });
            mb.Children.Add(GeneralRow("终焉倒计时", "最后小波后等待", ds));
            VigilNumberField dg = TimeNumber(_cfg.DeadManAfterMiniWaveSeconds, 0, 600, "秒", delegate(int v) { _cfg.DeadManAfterMiniWaveSeconds = v; });
            mb.Children.Add(GeneralRow("小波刷新后额外等待", "从最后那波小波起算；0 = 立刻开始", dg));
            StackPanel dmCalRow = new StackPanel();
            dmCalRow.Orientation = Orientation.Horizontal;
            VigilNumberField dmRemain = new VigilNumberField(_light, Line, Ink, Sub, 86);
            dmRemain.Minimum = 0;
            dmRemain.Maximum = 600;
            dmRemain.Value = _cfg.DeadManSeconds;
            dmCalRow.Children.Add(dmRemain);
            TextBlock dmStatus = new TextBlock();
            dmStatus.FontSize = 8.5;
            dmStatus.Foreground = Muted;
            dmStatus.TextWrapping = TextWrapping.Wrap;
            dmStatus.Margin = new Thickness(0, 3, 0, 0);
            Button dmCal = CommandButton("\uE823", "按剩余秒数校准", delegate
            {
                double sec = (double)dmRemain.Value;
                if (_calibrateDeadMan != null)
                {
                    _calibrateDeadMan(sec);
                    dmStatus.Text = "已校准：终焉从还剩 " + (int)sec + " 秒开始走。";
                }
                else if (_openLegacyPage != null) _openLegacyPage(4);
            }, Accent);
            dmCal.Margin = new Thickness(8, 0, 0, 0);
            dmCalRow.Children.Add(dmCal);
            mb.Children.Add(GeneralRow("此刻还剩 / 校准", "填当前剩余秒数，立即对齐", dmCalRow));
            mb.Children.Add(dmStatus);
            right.Children.Add(dead);

            StackPanel wb2;
            VigilCard war = StyleCard("WARHEAD / GENERATOR", out wb2);
            VigilNumberField wd = TimeNumber(_cfg.Warhead.DurationSeconds, 1, 36000, "秒", delegate(int v) { _cfg.Warhead.DurationSeconds = v; });
            wb2.Children.Add(GeneralRow("核弹倒计时", "F8 / Ctrl+F8 使用", wd));
            VigilNumberField wr = TimeNumber(_cfg.WarheadRestartSeconds, 1, 36000, "秒", delegate(int v) { _cfg.WarheadRestartSeconds = v; });
            wb2.Children.Add(GeneralRow("核弹取消重启", "Shift+F8 使用", wr));
            VigilNumberField ga = TimeNumber(_cfg.GenAllReadySeconds, 1, 600, "秒", delegate(int v) { _cfg.GenAllReadySeconds = v; });
            wb2.Children.Add(GeneralRow("三台发电机提示停留", "全部就绪提示显示时长", ga));
            right.Children.Add(war);

            StackPanel cb;
            VigilCard cal = StyleCard("CALIBRATION", out cb);
            Button open = CommandButton("\uE8A7", "打开战术校准", delegate { if (_calibrate != null) _calibrate(); }, Accent);
            cb.Children.Add(GeneralRow("时间校准", "按 F7 也可打开", open));
            Button scan = CommandButton("\uE8A5", "扫描游戏日志校准", delegate { ShowLogTimeCalibration(); }, Sub);
            cb.Children.Add(GeneralRow("日志时间校准", "读服务端日志，实测间隔", scan));
            TextBlock note = new TextBlock();
            note.Text = "校准页会按日志和当前回合对齐倒计时；改完这里的时间，时间轴会立即更新。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 9.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            cb.Children.Add(note);
            right.Children.Add(cal);

            page.Children.Add(grid);
            FrameworkElement timeTools = BuildTimeToolsCard() as FrameworkElement;
            if (timeTools != null)
            {
                timeTools.Margin = new Thickness(0, 12, 0, 0);
                page.Children.Add(timeTools);
            }
            return page;
        }

        private UIElement TimeNode(string name, string value, out TextBlock valueText)
        {
            StackPanel s = new StackPanel();
            s.HorizontalAlignment = HorizontalAlignment.Left;
            Border dot = new Border();
            dot.Width = 9;
            dot.Height = 9;
            dot.CornerRadius = new CornerRadius(5);
            dot.Background = Accent;
            dot.Margin = new Thickness(0, 18, 0, 0);
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(dot);
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 10;
            n.Foreground = Sub;
            n.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(n);
            valueText = MonoText(value, 9, Ink);
            s.Children.Add(valueText);
            return s;
        }

        private VigilNumberField TimeNumber(int value, int min, int max, string unit, Action<int> apply)
        {
            VigilNumberField f = new VigilNumberField(_light, Line, Ink, Sub, 96);
            f.Minimum = min;
            f.Maximum = max;
            f.Unit = unit;
            f.Value = value;
            f.ValueChanged += delegate
            {
                int v = (int)f.Value;
                apply(v);
                UpdateTimeLine();
                TouchSave();
            };
            return f;
        }

        private void UpdateTimeLine()
        {
            if (_timeRoleText != null) _timeRoleText.Text = _cfg.RoundStartDelay + " s";
            if (_timeOffsetText != null) _timeOffsetText.Text = "+" + _cfg.RoundStartOffsetSeconds + " s";
            if (_timeDeconText != null) _timeDeconText.Text = _cfg.DeconAfterRound + " s";
            if (_timeSupportText != null) _timeSupportText.Text = _cfg.SupportAfterRound + " s";
        }
        private void ImportAudioByDialog()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "导入音频";
            dlg.Multiselect = true;
            dlg.Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.mid;*.midi|所有文件|*.*";
            bool? ok = dlg.ShowDialog(this);
            if (ok != true) return;
            ImportAudioFiles(dlg.FileNames);
        }
        private void ImportAudioFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;
            string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
            if (string.IsNullOrEmpty(dir))
            {
                AppendAudioStatus("定位不到「音频」文件夹。", Danger);
                return;
            }
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            int ok = 0, skipped = 0, failed = 0, dup = 0;
            string dupNames = "";
            for (int i = 0; i < files.Length; i++)
            {
                string src = files[i];
                try
                {
                    if (string.IsNullOrEmpty(src) || !File.Exists(src)) { skipped++; continue; }
                    string ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
                    if (!AudioLibrary.IsSupportedExt(ext)) { skipped++; continue; }
                    string baseDest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(src));
                    if (File.Exists(baseDest) && new FileInfo(src).Length == new FileInfo(baseDest).Length)
                    {
                        dup++;
                        dupNames += (dupNames.Length > 0 ? "、" : "") + System.IO.Path.GetFileName(src);
                        continue;
                    }
                    string dest = UniqueAudioPath(baseDest, src);
                    File.Copy(src, dest, false);
                    ok++;
                }
                catch { failed++; }
            }
            AudioLibrary.ResolveDir(_cfg.AudioDir);
            AudioLibrary.ApplyConfig(_cfg);
            if (_audioPage != null) _audioPage.Content = BuildAudioPage();
            if (dup > 0)
            {
                AppendAudioStatus("已有重复音频：" + dupNames, Solid(255, 176, 0));
                SelectPage(3);
            }
            else
            {
                AppendAudioStatus("已导入 " + ok + " 个音频" + (skipped > 0 ? "，跳过 " + skipped : "") + (failed > 0 ? "，失败 " + failed : ""), ok > 0 ? Accent : Danger);
            }
            TouchSave();
        }

        private static string UniqueAudioPath(string dest, string src)
        {
            if (!File.Exists(dest)) return dest;
            try
            {
                if (new FileInfo(src).Length == new FileInfo(dest).Length) return dest;
            }
            catch { }
            string dir = System.IO.Path.GetDirectoryName(dest);
            string name = System.IO.Path.GetFileNameWithoutExtension(dest);
            string ext = System.IO.Path.GetExtension(dest);
            for (int i = 2; i < 500; i++)
            {
                string candidate = System.IO.Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return dest;
        }

        private void AppendAudioStatus(string text, Brush color)
        {
            if (_audioStatus == null) return;
            _audioStatus.Text = text;
            _audioStatus.Foreground = color;
        }
        private UIElement BuildAudioPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("触发点", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);
            page.Children.Add(AdvancedBanner("audio", "音频片段裁剪、单独音量、提示音分配与导出"));

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            page.Children.Add(grid);

            StackPanel left = new StackPanel();
            left.Children.Add(BuildAudioGlobalCard());
            left.Children.Add(BuildAudioFileCard());
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            StackPanel center = new StackPanel();
            VigilCard triggers = new VigilCard();
            triggers.Fill = Panel;
            triggers.Stroke = Line;
            triggers.Cut = 10;
            triggers.Padding = new Thickness(18, 14, 18, 16);
            StackPanel tc = new StackPanel();
            Grid th = new Grid();
            th.ColumnDefinitions.Add(new ColumnDefinition());
            th.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            th.Children.Add(MonoText("TRIGGERS  /  " + _cfg.AudioTriggers.Count, 9, Accent));
            Button add = CommandButton("\uE710", "新建触发点", delegate { AddAudioTrigger(); }, Accent);
            Grid.SetColumn(add, 1);
            th.Children.Add(add);
            tc.Children.Add(th);
            _audioTriggerList = new StackPanel();
            _audioTriggerList.Margin = new Thickness(0, 8, 0, 0);
            tc.Children.Add(_audioTriggerList);
            triggers.Child = tc;
            _hoverTargets.Add(triggers);
            center.Children.Add(triggers);
            Grid.SetColumn(center, 2);
            grid.Children.Add(center);

            StackPanel right = new StackPanel();
            right.Children.Add(BuildAudioPaletteCard());
            Grid.SetColumn(right, 4);
            grid.Children.Add(right);

            RebuildAudioTriggers();
            FrameworkElement audioTools = BuildAudioToolsCard() as FrameworkElement;
            if (audioTools != null)
            {
                audioTools.Margin = new Thickness(0, 12, 0, 0);
                page.Children.Add(audioTools);
            }
            return page;
        }

        private UIElement BuildAudioGlobalCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 16);
            card.Margin = new Thickness(0, 0, 0, 12);
            StackPanel b = new StackPanel();
            b.Children.Add(MonoText("SOUND", 9, Accent));
            VigilToggle master = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            master.IsChecked = _cfg.SoundEnabled;
            master.CheckedChanged += delegate
            {
                _cfg.SoundEnabled = master.IsChecked;
                AudioLibrary.Master = master.IsChecked;
                if (!master.IsChecked) AudioLibrary.StopCurrent();
                AudioLibrary.ApplyConfig(_cfg);
                TouchSave();
            };
            b.Children.Add(GeneralRow("提示音总开关", "关闭后所有音频静音", master));

            string[] files = AudioLibrary.List();
            List<string> defItems = new List<string>();
            defItems.Add("(无)");
            for (int i = 0; i < files.Length; i++) defItems.Add(files[i]);
            int defIndex = 0;
            for (int i = 0; i < files.Length; i++) if (files[i] == _cfg.AudioDefault) defIndex = i + 1;
            VigilSelect def = new VigilSelect(PanelAlt, Line, Ink, Accent, 168);
            def.SetItems(defItems, defIndex);
            def.SelectionChanged += delegate
            {
                string pick = def.SelectedText;
                _cfg.AudioDefault = pick == "(无)" ? "" : pick;
                AudioLibrary.ApplyConfig(_cfg);
                TouchSave();
            };
            StackPanel defBlock = new StackPanel();
            defBlock.Margin = new Thickness(0, 8, 0, 0);
            TextBlock defLabel = new TextBlock();
            defLabel.Text = "全局默认音频";
            defLabel.FontSize = 10.5;
            defLabel.Foreground = Ink;
            defBlock.Children.Add(defLabel);
            TextBlock defDesc = new TextBlock();
            defDesc.Text = "没有单独指定音频的提示使用它";
            defDesc.FontSize = 8.5;
            defDesc.Foreground = Muted;
            defDesc.Margin = new Thickness(0, 1, 0, 0);
            defBlock.Children.Add(defDesc);
            def.Width = 200;
            def.Margin = new Thickness(0, 5, 0, 0);
            defBlock.Children.Add(def);
            b.Children.Add(defBlock);

            WrapPanel actions = new WrapPanel();
            actions.Margin = new Thickness(0, 6, 0, 0);
            actions.Children.Add(CommandButton("\uE8A7", "打开文件夹", delegate { OpenAudioFolder(); }, Accent));
            actions.Children.Add(CommandButton("\uE72C", "重新扫描", delegate { AudioLibrary.ResolveDir(_cfg.AudioDir); AudioLibrary.ApplyConfig(_cfg); TouchSave(); }, Sub));
            actions.Children.Add(CommandButton("\uE8E5", "选择文件导入", delegate { ImportAudioByDialog(); }, Accent));
            b.Children.Add(actions);

            TextBlock dropHint = new TextBlock();
            dropHint.Text = "用「选择文件导入」添加音频，支持多选";
            dropHint.TextWrapping = TextWrapping.Wrap;
            dropHint.FontSize = 9;
            dropHint.Foreground = Muted;
            dropHint.Margin = new Thickness(0, 8, 0, 0);
            b.Children.Add(dropHint);

            _audioStatus = new TextBlock();
            _audioStatus.FontSize = 9;
            _audioStatus.TextWrapping = TextWrapping.Wrap;
            _audioStatus.Foreground = Accent;
            _audioStatus.Margin = new Thickness(0, 4, 0, 0);
            b.Children.Add(_audioStatus);
            card.Child = b;
            _hoverTargets.Add(card);
            return card;
        }

        private UIElement BuildAudioFileCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 16);
            StackPanel b = new StackPanel();
            b.Children.Add(MonoText("FILES", 9, Accent));
            string[] files = AudioLibrary.List();
            TextBlock count = MonoText(files.Length + " 个音频文件", 9.5, Sub);
            count.Margin = new Thickness(0, 6, 0, 0);
            b.Children.Add(count);
            for (int i = 0; i < files.Length && i < 6; i++)
            {
                Border row = new Border();
                row.Height = 26;
                row.Cursor = Cursors.Hand;
                TextBlock name = MonoText(files[i], 9, Sub);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Child = name;
                string clip = files[i];
                row.MouseLeftButtonUp += delegate { if (AudioLibrary.PlayKey(clip, "音频页试听")) { } };
                b.Children.Add(row);
            }
            if (files.Length == 0) b.Children.Add(MonoText("(音频文件夹为空)", 9, Muted));
            card.Child = b;
            _hoverTargets.Add(card);
            return card;
        }

        private UIElement BuildAudioPaletteCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(14, 14, 14, 16);
            StackPanel b = new StackPanel();
            b.Children.Add(MonoText("CONDITIONS  /  拖到触发点", 8.5, Accent));
            string last = "";
            WrapPanel wrap = null;
            List<AudioEventInfo> all = AudioEvents.All;
            for (int i = 0; i < all.Count; i++)
            {
                AudioEventInfo ev = all[i];
                if (ev.Group != last)
                {
                    last = ev.Group;
                    TextBlock g = MonoText(ev.Group, 8, Muted);
                    g.Margin = new Thickness(0, 10, 0, 4);
                    b.Children.Add(g);
                    wrap = new WrapPanel();
                    wrap.Margin = new Thickness(0, 0, 0, 2);
                    b.Children.Add(wrap);
                }
                Border chip = AudioChip(ev);
                if (wrap != null) wrap.Children.Add(chip);
            }
            card.Child = b;
            _hoverTargets.Add(card);
            return card;
        }

        private Border AudioChip(AudioEventInfo ev)
        {
            Border chip = new Border();
            chip.CornerRadius = new CornerRadius(3);
            chip.BorderThickness = new Thickness(1);
            chip.BorderBrush = Line;
            chip.Background = Brushes.Transparent;
            chip.Padding = new Thickness(8, 5, 8, 5);
            chip.Margin = new Thickness(0, 0, 5, 5);
            chip.Cursor = Cursors.Hand;
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            TextBlock label = MonoText(ev.Name, 8.5, Sub);
            chip.Child = label;
            string id = ev.Id;
            chip.MouseEnter += delegate { chip.BorderBrush = Accent; label.Foreground = Accent; };
            chip.MouseLeave += delegate { chip.BorderBrush = Line; label.Foreground = Sub; chip.RenderTransform = Transform.Identity; };
            chip.PreviewMouseLeftButtonDown += delegate
            {
                _audioDragEvent = id;
                chip.RenderTransform = new ScaleTransform(0.95, 0.95);
                chip.Background = AccentSoftBrush();
            };
            chip.PreviewMouseLeftButtonUp += delegate
            {
                chip.RenderTransform = Transform.Identity;
                chip.Background = Brushes.Transparent;
            };
            chip.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_audioDragEvent != null && e.LeftButton == MouseButtonState.Pressed)
                {
                    chip.RenderTransform = Transform.Identity;
                    chip.Background = Brushes.Transparent;
                    DragDrop.DoDragDrop(chip, _audioDragEvent, DragDropEffects.Copy);
                    _audioDragEvent = null;
                }
            };
            return chip;
        }

        private void RebuildAudioTriggers()
        {
            if (_audioTriggerList == null) return;
            _audioTriggerList.Children.Clear();
            if (_cfg.AudioTriggers.Count == 0)
            {
                TextBlock empty = MonoText("还没有触发点。点「新建触发点」，再把右侧条件拖进去。", 9.5, Muted);
                empty.Margin = new Thickness(0, 12, 0, 0);
                _audioTriggerList.Children.Add(empty);
                return;
            }
            for (int i = 0; i < _cfg.AudioTriggers.Count; i++)
                _audioTriggerList.Children.Add(BuildAudioTriggerCard(_cfg.AudioTriggers[i], i));
        }

        private UIElement BuildAudioTriggerCard(AudioTrigger t, int index)
        {
            return BuildTriggerCardV2(t, index);
        }

        private void AddAudioCondition(AudioTrigger t, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            AudioCond c = new AudioCond();
            AudioEventInfo info = AudioEvents.Find(id);
            c.Event = id;
            c.Op = info != null && info.IsPulse ? "==" : (info != null && info.Unit == "秒" ? "<=" : ">=");
            c.Value = 1;
            t.Conds.Add(c);
            SaveAudioTriggers();
            RebuildAudioTriggers();
        }
        private UIElement BuildAudioCondRow(AudioTrigger t, AudioCond c, bool isStop)
        {
            AudioEventInfo ev = AudioEvents.Find(c.Event);
            string title = ev == null ? c.Event : ev.Name;
            Border row = new Border();
            row.BorderBrush = Line;
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(0, 6, 0, 7);
            StackPanel body = new StackPanel();

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = MonoText(title, 9, Sub);
            label.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(label);
            Button remove = IconButton("\uE711", delegate { if (isStop) t.StopConds.Remove(c); else t.Conds.Remove(c); SaveAudioTriggers(); RebuildAudioTriggers(); }, Danger);
            remove.ToolTip = "删除这个条件";
            Grid.SetColumn(remove, 1);
            head.Children.Add(remove);
            body.Children.Add(head);

            StackPanel controls = new StackPanel();
            controls.Orientation = Orientation.Horizontal;
            controls.Margin = new Thickness(0, 4, 0, 0);

            if (ev != null && ev.IsPulse)
            {
                TextBlock once = MonoText("事件发生时触发一次", 8.5, Accent);
                once.VerticalAlignment = VerticalAlignment.Center;
                controls.Children.Add(once);
            }
            else if (IsBooleanAudioEvent(ev))
            {
                VigilSelect state = new VigilSelect(PanelAlt, Line, Ink, Accent, 104);
                state.SetItems(new string[] { "成立", "不成立" }, c.Op == "!=" ? 1 : 0);
                state.SelectionChanged += delegate
                {
                    c.Op = state.SelectedIndex == 1 ? "!=" : "==";
                    c.Value = 1;
                    SaveAudioTriggers();
                };
                controls.Children.Add(state);
            }
            else
            {
                if (ev != null && ev.NeedsArg)
                    controls.Children.Add(AudioArgEditor(c, ev));
                if (ev != null && ev.NeedsFaction)
                {
                    TextBlock fl = MonoText("阵营", 8, Muted);
                    fl.VerticalAlignment = VerticalAlignment.Center;
                    fl.Margin = new Thickness(8, 0, 4, 0);
                    controls.Children.Add(fl);
                    VigilSelect faction = new VigilSelect(PanelAlt, Line, Ink, Accent, 88);
                    faction.SetItems(new string[] { "(任意)", "MTF", "Chaos" }, c.Faction == "MTF" ? 1 : (c.Faction == "Chaos" ? 2 : 0));
                    faction.SelectionChanged += delegate
                    {
                        c.Faction = faction.SelectedIndex == 1 ? "MTF" : (faction.SelectedIndex == 2 ? "Chaos" : "");
                        SaveAudioTriggers();
                    };
                    controls.Children.Add(faction);
                }
                string[] ops = AudioOpsFor(ev, c);
                VigilSelect op = new VigilSelect(PanelAlt, Line, Ink, Accent, 58);
                op.Margin = new Thickness(8, 0, 4, 0);
                op.SetItems(ops, OpIndex(ops, c.Op));
                op.SelectionChanged += delegate { c.Op = OpCode(op.SelectedText); SaveAudioTriggers(); };
                controls.Children.Add(op);
                VigilNumberField val = new VigilNumberField(_light, Line, Ink, Sub, 68);
                val.Minimum = -100000;
                val.Maximum = 100000;
                val.Value = (decimal)c.Value;
                val.ValueChanged += delegate { c.Value = (double)val.Value; SaveAudioTriggers(); };
                controls.Children.Add(val);
                if (ev != null && !string.IsNullOrEmpty(ev.Unit))
                {
                    TextBlock unit = MonoText(ev.Unit, 8, Muted);
                    unit.VerticalAlignment = VerticalAlignment.Center;
                    unit.Margin = new Thickness(4, 0, 0, 0);
                    controls.Children.Add(unit);
                }
            }
            body.Children.Add(controls);
            row.Child = body;
            return row;
        }

        private UIElement AudioArgEditor(AudioCond c, AudioEventInfo ev)
        {
            if (ev.Id == "alert.remain")
            {
                List<string> names = new List<string>();
                int index = 0;
                for (int i = 0; i < _cfg.Alerts.Length; i++)
                {
                    names.Add(_cfg.Alerts[i].DisplayName);
                    if (_cfg.Alerts[i].DisplayName == c.Arg) index = i;
                }
                VigilSelect pick = new VigilSelect(PanelAlt, Line, Ink, Accent, 150);
                pick.SetItems(names, index);
                pick.SelectionChanged += delegate { c.Arg = pick.SelectedText; SaveAudioTriggers(); };
                return pick;
            }
            if (ev.Id == "pulse.gen.activate")
            {
                VigilSelect pick = new VigilSelect(PanelAlt, Line, Ink, Accent, 88);
                int index = c.Arg == "2" ? 1 : (c.Arg == "3" ? 2 : 0);
                pick.SetItems(new string[] { "第 1 台", "第 2 台", "第 3 台" }, index);
                pick.SelectionChanged += delegate { c.Arg = (pick.SelectedIndex + 1).ToString(); SaveAudioTriggers(); };
                return pick;
            }
            TextBox arg = VigilInput(c.Arg, 78);
            arg.Margin = new Thickness(0, 0, 2, 0);
            arg.ToolTip = ev.ArgHint;
            arg.TextChanged += delegate { c.Arg = arg.Text; SaveAudioTriggers(); };
            return arg;
        }

        private static bool IsBooleanAudioEvent(AudioEventInfo ev)
        {
            if (ev == null) return false;
            switch (ev.Id)
            {
                case "round.active":
                case "inserver":
                case "alive":
                case "self.dead":
                case "warhead.active":
                case "deadman.active":
                case "decon.active":
                case "decon.locked":
                case "wave.mini.pending":
                case "wave.soon":
                case "has079":
                case "gen.allready":
                case "deadman.ocr":
                    return true;
            }
            return false;
        }

        private static string[] AudioOpsFor(AudioEventInfo ev, AudioCond c)
        {
            if (ev != null && ev.Unit == "秒") return new string[] { "≤", "≥", "=" };
            return new string[] { "≥", "≤", "=" };
        }

        private static int OpIndex(string[] ops, string op)
        {
            string symbol = OpSymbol(op);
            for (int i = 0; i < ops.Length; i++) if (ops[i] == symbol) return i;
            return 0;
        }

        private static string OpSymbol(string op)
        {
            if (op == "<=") return "≤";
            if (op == ">=") return "≥";
            if (op == "==") return "=";
            if (op == "!=") return "≠";
            return op;
        }

        private static string OpCode(string symbol)
        {
            if (symbol == "≤") return "<=";
            if (symbol == "≥") return ">=";
            if (symbol == "=") return "==";
            if (symbol == "≠") return "!=";
            return symbol;
        }
        private void AddAudioTrigger()
        {
            AudioTrigger t = new AudioTrigger();
            t.Name = "新触发点 " + (_cfg.AudioTriggers.Count + 1);
            string[] files = AudioLibrary.List();
            t.Clip = files.Length > 0 ? files[0] : "";
            _cfg.AudioTriggers.Add(t);
            SaveAudioTriggers();
            RebuildAudioTriggers();
        }

        private void SaveAudioTriggers()
        {
            _cfg.AudioTriggerSpec = AudioTriggerSpec.Join(_cfg.AudioTriggers);
            AudioLibrary.ApplyConfig(_cfg);
            TouchSave();
        }

        private void OpenAudioFolder()
        {
            try
            {
                string dir = AudioLibrary.ResolveDir(_cfg.AudioDir);
                if (Directory.Exists(dir))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private ComboBox VigilCombo()
        {
            ComboBox c = new ComboBox();
            c.Height = 27;
            c.FontFamily = Ui;
            c.FontSize = 10;
            c.Foreground = Ink;
            c.Background = PanelAlt;
            c.BorderBrush = Line;
            c.BorderThickness = new Thickness(1);
            c.Padding = new Thickness(4, 0, 4, 0);
            Style item = new Style(typeof(ComboBoxItem));
            item.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, PanelAlt));
            item.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Ink));
            item.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(6, 2, 6, 2)));
            c.ItemContainerStyle = item;
            return c;
        }

        private TextBox VigilInput(string value, double width)
        {
            TextBox t = new TextBox();
            if (width > 0) t.Width = width;
            t.Text = value;
            t.FontFamily = Ui;
            t.FontSize = 10.5;
            t.Foreground = Ink;
            t.Background = Brushes.Transparent;
            t.BorderBrush = Line;
            t.BorderThickness = new Thickness(0, 0, 0, 1);
            t.Padding = new Thickness(2, 2, 2, 2);
            t.CaretBrush = Accent;
            WpfTextInputFallback.Attach(t, false);
            t.PreviewMouseLeftButtonDown += delegate
            {
                try
                {
                    Window w = Window.GetWindow(t);
                    if (w != null && !w.IsActive) w.Activate();
                    if (!t.IsKeyboardFocusWithin) { t.Focus(); Keyboard.Focus(t); }
                }
                catch { }
            };
            return t;
        }
        private UIElement BuildHeartPromptDetail()
        {
            StackPanel body = new StackPanel();

            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 0, 10);
            StackPanel titles = new StackPanel();
            titles.Children.Add(MonoText("HEART RATE  /  心率提示", 9, Accent));
            TextBlock big = DisplayText("心率提示", 26, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 1, 0, 0);
            titles.Children.Add(big);
            head.Children.Add(titles);
            body.Children.Add(head);

            TextBlock note = new TextBlock();
            note.Text = "心率来自蓝牙接口，和游戏内事件无关。可以做成完整卡片、无框大字，或者只留一个数字。"
                + "位置 -1 = 自动放在主屏右上角。";
            note.FontSize = 10;
            note.Foreground = Muted;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(note);

            VigilToggle enabled = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            enabled.IsChecked = _cfg.HeartPromptEnabled;
            enabled.CheckedChanged += delegate
            {
                _cfg.HeartPromptEnabled = enabled.IsChecked;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("屏幕心率提示", "有 BPM 时显示；无信号自动收起", enabled));

            List<string> modes = new List<string>();
            modes.Add("完整卡片");
            modes.Add("无框大字");
            modes.Add("只显示数字");
            VigilSelect mode = new VigilSelect(PanelAlt, Line, Ink, Accent, 236);
            mode.SetItems(modes, Math.Max(0, Math.Min(2, _cfg.HeartPromptStyle)));
            body.Children.Add(GeneralRow("显示模式", "纯数字模式会忽略标签 / 单位 / 脉冲", mode));

            VigilToggle frame = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            frame.IsChecked = _cfg.HeartPromptFrame;
            VigilToggle label = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            label.IsChecked = _cfg.HeartPromptLabel;
            VigilToggle unit = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            unit.IsChecked = _cfg.HeartPromptUnit;
            VigilToggle pulse = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            pulse.IsChecked = _cfg.HeartPromptPulse;
            VigilToggle wave = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            wave.IsChecked = _cfg.HeartPromptWave;

            frame.CheckedChanged += delegate { _cfg.HeartPromptFrame = frame.IsChecked; TouchSave(); RefreshHeartPreview(); };
            label.CheckedChanged += delegate { _cfg.HeartPromptLabel = label.IsChecked; TouchSave(); RefreshHeartPreview(); };
            unit.CheckedChanged += delegate { _cfg.HeartPromptUnit = unit.IsChecked; TouchSave(); RefreshHeartPreview(); };
            pulse.CheckedChanged += delegate { _cfg.HeartPromptPulse = pulse.IsChecked; TouchSave(); RefreshHeartPreview(); };
            wave.CheckedChanged += delegate { _cfg.HeartPromptWave = wave.IsChecked; TouchSave(); RefreshHeartPreview(); };

            body.Children.Add(GeneralRow("外框", "完整卡片模式下生效", frame));
            body.Children.Add(GeneralRow("显示标签", "HEART RATE / 心率", label));
            body.Children.Add(GeneralRow("显示 BPM 单位", "数字后面的 BPM", unit));
            body.Children.Add(GeneralRow("心形脉冲", "心形图标随 BPM 跳动", pulse));
            body.Children.Add(GeneralRow("整条心电波", "整条波形从左到右随心跳突然张开", wave));

            VigilNumberField wavePoints = new VigilNumberField(_light, Line, Ink, Sub, 96);
            wavePoints.Minimum = 1;
            wavePoints.Maximum = 6;
            wavePoints.Value = Math.Max(1, Math.Min(6, _cfg.HeartPromptWavePoints));
            wavePoints.ValueChanged += delegate
            {
                _cfg.HeartPromptWavePoints = (int)wavePoints.Value;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("每次波点", "每次心跳随机挑几个点冒波", wavePoints));

            VigilNumberField waveAmp = new VigilNumberField(_light, Line, Ink, Sub, 96);
            waveAmp.Minimum = 10;
            waveAmp.Maximum = 180;
            waveAmp.Value = Math.Max(10, Math.Min(180, _cfg.HeartPromptWaveAmp));
            waveAmp.ValueChanged += delegate
            {
                _cfg.HeartPromptWaveAmp = (int)waveAmp.Value;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("波形振幅", "%；数值越大跳得越高", waveAmp));

            VigilNumberField waveWidth = new VigilNumberField(_light, Line, Ink, Sub, 96);
            waveWidth.Minimum = 2;
            waveWidth.Maximum = 24;
            waveWidth.Value = Math.Max(2, Math.Min(24, _cfg.HeartPromptWaveWidth));
            waveWidth.ValueChanged += delegate
            {
                _cfg.HeartPromptWaveWidth = (int)waveWidth.Value;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("波形宽度", "占整条线的百分比", waveWidth));

            VigilNumberField fontSize = new VigilNumberField(_light, Line, Ink, Sub, 96);
            fontSize.Minimum = 18;
            fontSize.Maximum = 140;
            fontSize.Value = Math.Max(18, Math.Min(140, _cfg.HeartPromptFontSize));
            fontSize.ValueChanged += delegate
            {
                _cfg.HeartPromptFontSize = (int)fontSize.Value;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("数字大小", "像素；越大提示越醒目", fontSize));

            VigilNumberField opacity = new VigilNumberField(_light, Line, Ink, Sub, 96);
            opacity.Minimum = 5;
            opacity.Maximum = 100;
            opacity.Value = Math.Max(5, Math.Min(100, _cfg.HeartPromptOpacity));
            opacity.ValueChanged += delegate
            {
                _cfg.HeartPromptOpacity = (int)opacity.Value;
                TouchSave();
                RefreshHeartPreview();
            };
            body.Children.Add(GeneralRow("不透明度", "%", opacity));

            body.Children.Add(GeneralRow("主色", "心跳数字 / 心形 / 边框", HeartColorSwatches()));
            body.Children.Add(GeneralRow("预览", "示例 72 BPM", HeartPreviewBox()));

            VigilNumberField px = new VigilNumberField(_light, Line, Ink, Sub, 96);
            px.Minimum = -1;
            px.Maximum = 8000;
            px.Value = Math.Max(-1, Math.Min(8000, _cfg.HeartPromptX));
            px.ValueChanged += delegate
            {
                _cfg.SetHeartPromptPixelPosition((int)px.Value, _cfg.HeartPromptY);
                TouchSave();
            };
            VigilNumberField py = new VigilNumberField(_light, Line, Ink, Sub, 96);
            py.Minimum = -1;
            py.Maximum = 8000;
            py.Value = Math.Max(-1, Math.Min(8000, _cfg.HeartPromptY));
            py.ValueChanged += delegate
            {
                _cfg.SetHeartPromptPixelPosition(_cfg.HeartPromptX, (int)py.Value);
                TouchSave();
            };
            body.Children.Add(GeneralRow("位置 X", "-1 = 自动右上角", px));
            body.Children.Add(GeneralRow("位置 Y", "-1 = 自动右上角", py));

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.Children.Add(CommandButton("\uE8A7", "一起调整位置",
                delegate { if (_adjustPosition != null) _adjustPosition(); }, Accent));
            body.Children.Add(actions);
            TextBlock adjustNote = new TextBlock();
            adjustNote.Text = "调整模式中心率提示也会出现（没有信号时用 72 占位），可直接拖动它。";
            adjustNote.FontSize = 9;
            adjustNote.Foreground = Muted;
            adjustNote.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(adjustNote);

            mode.SelectionChanged += delegate
            {
                _cfg.HeartPromptStyle = Math.Max(0, Math.Min(2, mode.SelectedIndex));
                TouchSave();
                RefreshHeartPreview();
            };
            RefreshHeartPreview();
            return body;
        }

        private StackPanel HeartColorSwatches()
        {
            StackPanel s = new StackPanel();
            s.Orientation = Orientation.Horizontal;
            string[] preset = { "255,84,90", "255,122,170", "255,176,58", "56,214,255", "198,255,61", "235,238,244" };
            for (int i = 0; i < preset.Length; i++)
            {
                string value = preset[i];
                Color c = ParseHeartColor(value);
                Border b = new Border();
                b.Width = 22;
                b.Height = 22;
                b.CornerRadius = new CornerRadius(3);
                b.Margin = new Thickness(0, 0, 6, 0);
                b.Background = new SolidColorBrush(c);
                b.BorderBrush = Line;
                b.BorderThickness = new Thickness(1);
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += delegate
                {
                    _cfg.HeartPromptColor = value;
                    TouchSave();
                    RefreshHeartPreview();
                };
                s.Children.Add(b);
            }
            return s;
        }

        private Border HeartPreviewBox()
        {
            Border box = new Border();
            box.Height = 170;
            box.Background = new SolidColorBrush(Color.FromRgb(7, 11, 10));
            box.BorderBrush = Line;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(8);
            _heartPreview = new WpfHeartCard();
            _heartPreview.IsHitTestVisible = false;
            _heartPreview.HorizontalAlignment = HorizontalAlignment.Center;
            _heartPreview.VerticalAlignment = VerticalAlignment.Center;
            box.Child = _heartPreview;
            return box;
        }

        private void RefreshHeartPreview()
        {
            int heartIndex = _cfg.Alerts.Length;
            if (heartIndex < _alertDots.Count)
                _alertDots[heartIndex].Foreground = _cfg.HeartPromptEnabled ? Accent : Muted;
            if (_heartPreview == null) return;
            _heartPreview.Bpm = 72;
            _heartPreview.Style = Math.Max(0, Math.Min(2, _cfg.HeartPromptStyle));
            _heartPreview.Frame = _cfg.HeartPromptFrame;
            _heartPreview.Label = _cfg.HeartPromptLabel;
            _heartPreview.Unit = _cfg.HeartPromptUnit;
            _heartPreview.Pulse = _cfg.HeartPromptPulse;
            _heartPreview.Wave = _cfg.HeartPromptWave;
            _heartPreview.WavePoints = _cfg.HeartPromptWavePoints;
            _heartPreview.WaveAmp = _cfg.HeartPromptWaveAmp;
            _heartPreview.WaveWidth = _cfg.HeartPromptWaveWidth;
            _heartPreview.FontSize = Math.Max(18, Math.Min(140, _cfg.HeartPromptFontSize));
            _heartPreview.Accent = ParseHeartColor(_cfg.HeartPromptColor);
            _heartPreview.Opacity = Math.Max(0.05, Math.Min(1.0, _cfg.HeartPromptOpacity / 100.0));
        }

        private static Color ParseHeartColor(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return Color.FromRgb(255, 84, 90);
                string[] p = text.Split(new char[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) return Color.FromRgb(255, 84, 90);
                return Color.FromRgb((byte)Math.Max(0, Math.Min(255, int.Parse(p[0]))),
                    (byte)Math.Max(0, Math.Min(255, int.Parse(p[1]))),
                    (byte)Math.Max(0, Math.Min(255, int.Parse(p[2]))));
            }
            catch { return Color.FromRgb(255, 84, 90); }
        }

        private UIElement BuildAlertPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("提示", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);

            // 提示主题：只换“屏幕上提示长什么样”，随时可切；默认跟随主界面。
            StackPanel themeBody;
            VigilCard themeCard = StyleCard("PROMPT THEME  /  提示主题", out themeBody);
            List<string> themeChoices = new List<string>();
            themeChoices.Add("跟随主界面（推荐）");
            themeChoices.Add("经典提示");
            themeChoices.Add("新版提示 · A.R.C.T");
            int themeIndex = _cfg.PromptTheme < 0 ? 0 : (_cfg.PromptTheme == 0 ? 1 : 2);
            VigilSelect themePick = new VigilSelect(PanelAlt, Line, Ink, Accent, 268);
            themePick.SetItems(themeChoices, themeIndex);
            TextBlock themeNote = new TextBlock();
            themeNote.FontSize = 10;
            themeNote.Foreground = Muted;
            themeNote.TextWrapping = TextWrapping.Wrap;
            themeNote.Margin = new Thickness(0, 6, 0, 0);
            Action updateThemeNote = delegate
            {
                themeNote.Text = _cfg.PromptTheme < 0
                    ? "跟随：新界面用 A.R.C.T 提示，旧界面用经典提示。切换主界面时会自动跟着换。"
                    : (_cfg.PromptTheme == 0
                        ? "固定经典提示：字体 / 颜色 / 背景 / 动画参数全部生效。"
                        : "固定 A.R.C.T 提示：近黑切角面板 + 语义信号色 + 等宽倒计时；下方旧样式参数不作用。");
            };
            updateThemeNote();
            themePick.SelectionChanged += delegate
            {
                _cfg.PromptTheme = themePick.SelectedIndex == 0 ? -1
                    : (themePick.SelectedIndex == 1 ? 0 : 1);
                updateThemeNote();
                TouchSave();
                RefreshAlertPreview();
            };
            themeBody.Children.Add(GeneralRow("提示主题", "只作用于屏幕提示，不影响主界面", themePick));
            themeBody.Children.Add(themeNote);
            StackPanel themeActions = new StackPanel();
            themeActions.Orientation = Orientation.Horizontal;
            themeActions.Margin = new Thickness(0, 8, 0, 0);
            themeActions.Children.Add(CommandButton("\uE768", "预览全部提示",
                delegate { if (_previewAlerts != null) _previewAlerts(); }, Accent));
            themeBody.Children.Add(themeActions);
            page.Children.Add(themeCard);

            // 第 8 批：普通提示防重叠（只影响带框提示；图片/无框文字不注册）
            StackPanel layoutBody;
            VigilCard layoutCard = StyleCard("ALERT LAYOUT  /  提示布局", out layoutBody);
            layoutBody.Children.Add(GeneralRow("防重叠",
                "只影响带框普通提示；图片和无框文字不参与", AdvToggle(_cfg.AlertAvoidEnabled,
                    delegate(bool v) { _cfg.AlertAvoidEnabled = v; TouchSave(); })));
            layoutBody.Children.Add(GeneralRow("最小间隙",
                "像素；默认 8", AdvNumber(_cfg.AlertAvoidGapPx, 0, 32,
                    delegate(int v) { _cfg.AlertAvoidGapPx = v; TouchSave(); })));
            layoutBody.Children.Add(GeneralRow("屏幕边距",
                "像素；默认 8，按工作区计算", AdvNumber(_cfg.AlertAvoidMarginPx, 0, 64,
                    delegate(int v) { _cfg.AlertAvoidMarginPx = v; TouchSave(); })));
            layoutBody.Children.Add(GeneralRow("缓动时长",
                "毫秒；0 = 瞬移，默认 180", AdvNumber(_cfg.AlertAvoidAnimMs, 0, 500,
                    delegate(int v) { _cfg.AlertAvoidAnimMs = v; TouchSave(); })));
            TextBlock layoutNote = new TextBlock();
            layoutNote.Text = "同屏、按实际矩形判断；向下放不下时整组改向上；上下都放不下时新提示优先并允许重叠，不删除窗口。";
            layoutNote.FontSize = 9.5;
            layoutNote.Foreground = Muted;
            layoutNote.TextWrapping = TextWrapping.Wrap;
            layoutNote.Margin = new Thickness(0, 6, 0, 0);
            layoutBody.Children.Add(layoutNote);
            page.Children.Add(layoutCard);

            page.Children.Add(AdvancedBanner("position", "逐条拖动提示位置、逐条音频分配与更完整的显示设置"));

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            page.Children.Add(grid);

            VigilCard listCard = new VigilCard();
            listCard.Fill = Panel;
            listCard.Stroke = Line;
            listCard.Cut = 10;
            listCard.Padding = new Thickness(12, 12, 12, 12);
            StackPanel list = new StackPanel();
            list.Children.Add(MonoText("ALERT LIST  /  " + (_cfg.Alerts.Length + 1), 9, Accent));
            _alertRows.Clear();
            _alertDots.Clear();
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                int index = i;
                AlertConfig alert = _cfg.Alerts[i];
                Border row = new Border();
                row.Height = 32;
                row.Margin = new Thickness(0, 2, 0, 2);
                row.Padding = new Thickness(8, 0, 8, 0);
                row.CornerRadius = new CornerRadius(3);
                row.Cursor = Cursors.Hand;
                TextBlock dot = MonoText("●", 8, alert.Style.Enabled ? Accent : Muted);
                dot.VerticalAlignment = VerticalAlignment.Center;
                row.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot } };
                TextBlock name = new TextBlock();
                name.Text = alert.DisplayName;
                name.FontSize = 11;
                name.Foreground = Sub;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(8, 0, 0, 0);
                ((StackPanel)row.Child).Children.Add(name);
                row.MouseLeftButtonUp += delegate { SelectAlert(index); };
                _alertRows.Add(row);
                _alertDots.Add(dot);
                list.Children.Add(row);
            }

            // 心率提示不是游戏事件，但和普通提示放在同一个列表里，
            // 选中后右边显示它自己的设置，位置也能在同一套流程里调。
            {
                int heartIndex = _cfg.Alerts.Length;
                Border row = new Border();
                row.Height = 32;
                row.Margin = new Thickness(0, 2, 0, 2);
                row.Padding = new Thickness(8, 0, 8, 0);
                row.CornerRadius = new CornerRadius(3);
                row.Cursor = Cursors.Hand;
                TextBlock dot = MonoText("♥", 9, _cfg.HeartPromptEnabled ? Accent : Muted);
                dot.VerticalAlignment = VerticalAlignment.Center;
                StackPanel rowBody = new StackPanel();
                rowBody.Orientation = Orientation.Horizontal;
                rowBody.Children.Add(dot);
                TextBlock name = new TextBlock();
                name.Text = "心率提示";
                name.FontSize = 11;
                name.Foreground = Sub;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(8, 0, 0, 0);
                rowBody.Children.Add(name);
                row.Child = rowBody;
                row.MouseLeftButtonUp += delegate { SelectAlert(heartIndex); };
                _alertRows.Add(row);
                _alertDots.Add(dot);
                list.Children.Add(row);
            }
            listCard.Child = list;
            Grid.SetColumn(listCard, 0);
            grid.Children.Add(listCard);

            _alertDetail = new StackPanel();
            _alertDetail.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(_alertDetail, 1);
            grid.Children.Add(_alertDetail);
            SelectAlert(0);
            FrameworkElement positionCard = BuildPositionCard() as FrameworkElement;
            if (positionCard != null)
            {
                positionCard.Margin = new Thickness(0, 12, 0, 0);
                page.Children.Add(positionCard);
            }
            return page;
        }

        private void SelectAlert(int index)
        {
            if (index < 0 || index > _cfg.Alerts.Length) return;
            _alertIndex = index;
            for (int i = 0; i < _alertRows.Count; i++)
            {
                bool on = i == index;
                _alertRows[i].Background = on ? PanelAlt : Brushes.Transparent;
                _alertRows[i].BorderBrush = on ? Accent : Brushes.Transparent;
                _alertRows[i].BorderThickness = new Thickness(on ? 1 : 0, 0, 0, 0);
            }
            if (_alertDetail == null) return;
            _alertDetail.Children.Clear();
            _alertDetail.Children.Add(index >= _cfg.Alerts.Length
                ? BuildHeartPromptDetail()
                : BuildAlertDetail());
        }

        private UIElement BuildAlertDetail()
        {
            AlertConfig alert = _cfg.Alerts[_alertIndex];
            WindowStyle style = alert.Style;
            StackPanel detail = new StackPanel();

            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 0, 12);
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel names = new StackPanel();
            names.Children.Add(MonoText("STYLE STUDIO  /  " + alert.Section, 9, Accent));
            TextBlock title = DisplayText(alert.DisplayName, 26, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 1, 0, 0);
            names.Children.Add(title);
            head.Children.Add(names);
            StackPanel switchHost = new StackPanel();
            switchHost.Orientation = Orientation.Horizontal;
            switchHost.VerticalAlignment = VerticalAlignment.Center;
            TextBlock switchLabel = new TextBlock();
            switchLabel.Text = "启用";
            switchLabel.FontSize = 10.5;
            switchLabel.Foreground = Sub;
            switchLabel.VerticalAlignment = VerticalAlignment.Center;
            switchLabel.Margin = new Thickness(0, 0, 10, 0);
            switchHost.Children.Add(switchLabel);
            VigilToggle enabled = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            enabled.IsChecked = style.Enabled;
            enabled.CheckedChanged += delegate
            {
                style.Enabled = enabled.IsChecked;
                if (_alertIndex < _alertDots.Count) _alertDots[_alertIndex].Foreground = style.Enabled ? Accent : Muted;
                TouchSave();
            };
            switchHost.Children.Add(enabled);
            Grid.SetColumn(switchHost, 1);
            head.Children.Add(switchHost);
            detail.Children.Add(head);

            StackPanel pv;
            VigilCard preview = StyleCard("LIVE PREVIEW  /  实时预览", out pv);
            _alertPreviewBox = new Border();
            _alertPreviewBox.Height = 198;
            _alertPreviewBox.BorderBrush = Line;
            _alertPreviewBox.BorderThickness = new Thickness(1);
            _alertPreviewBox.Padding = new Thickness(28, 16, 28, 16);
            _alertPreviewBox.Margin = new Thickness(0, 8, 0, 0);
            _alertPreviewText = new TextBlock();
            _alertPreviewText.FontFamily = Ui;
            _alertPreviewText.TextWrapping = TextWrapping.Wrap;
            _alertPreviewText.VerticalAlignment = VerticalAlignment.Center;
            _alertPreviewText.HorizontalAlignment = HorizontalAlignment.Center;
            _alertPreviewText.TextAlignment = TextAlignment.Center;
            _alertPreviewNew = new WpfPromptCard();
            _alertPreviewNew.IsHitTestVisible = false;
            _alertPreviewNew.HorizontalAlignment = HorizontalAlignment.Center;
            _alertPreviewNew.VerticalAlignment = VerticalAlignment.Center;
            _alertPreviewBox.Child = _alertPreviewText;
            pv.Children.Add(_alertPreviewBox);
            detail.Children.Add(preview);

            Grid columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel left = new StackPanel();
            StackPanel right = new StackPanel();
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);

            StackPanel b;
            VigilCard behavior = StyleCard("BEHAVIOR", out b);
            VigilToggle persistent = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            persistent.IsChecked = alert.Persistent;
            persistent.CheckedChanged += delegate { alert.Persistent = persistent.IsChecked; TouchSave(); };
            b.Children.Add(GeneralRow("常驻显示", "不自动缩回", persistent));
            VigilToggle sound = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            sound.IsChecked = alert.SoundOn;
            sound.CheckedChanged += delegate { alert.SoundOn = sound.IsChecked; TouchSave(); };
            b.Children.Add(GeneralRow("弹出时响一声", "使用提示音", sound));
            List<string> audioChoices = new List<string>();
            audioChoices.Add("(默认 / 无)");
            string[] audioFiles = AudioLibrary.List();
            for (int i = 0; i < audioFiles.Length; i++) audioChoices.Add(audioFiles[i]);
            try
            {
                List<AudioLibrary.Segment> segs = AudioLibrary.ParseSegments(_cfg.AudioSegments);
                for (int i = 0; i < segs.Count; i++) audioChoices.Add(AudioLibrary.SegmentPrefix + segs[i].Name);
            }
            catch { }
            int audioIndex = 0;
            for (int i = 1; i < audioChoices.Count; i++)
                if (string.Equals(audioChoices[i], alert.AudioFile, StringComparison.OrdinalIgnoreCase)) { audioIndex = i; break; }
            VigilSelect audioPick = new VigilSelect(PanelAlt, Line, Ink, Accent, 200);
            audioPick.SetItems(audioChoices, audioIndex);
            audioPick.SelectionChanged += delegate
            {
                alert.AudioFile = audioPick.SelectedIndex <= 0 ? "" : audioPick.SelectedText;
                TouchSave();
            };
            b.Children.Add(GeneralRow("指定音频", "留空使用全局默认", audioPick));
            left.Children.Add(behavior);

            StackPanel tx;
            VigilCard text = StyleCard("TEXT", out tx);
            VigilNumberField fontSize = new VigilNumberField(_light, Line, Ink, Sub, 86);
            fontSize.Minimum = 8;
            fontSize.Maximum = 120;
            fontSize.Value = (decimal)style.FontSize;
            fontSize.ValueChanged += delegate { style.FontSize = (float)fontSize.Value; RefreshAlertPreview(); TouchSave(); };
            tx.Children.Add(GeneralRow("字号", "像素", fontSize));
            VigilToggle bold = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            bold.IsChecked = style.Bold;
            bold.CheckedChanged += delegate { style.Bold = bold.IsChecked; RefreshAlertPreview(); TouchSave(); };
            tx.Children.Add(GeneralRow("粗体", "", bold));
            VigilNumberField opacity = new VigilNumberField(_light, Line, Ink, Sub, 86);
            opacity.Minimum = 0;
            opacity.Maximum = 100;
            opacity.Value = style.Opacity;
            opacity.ValueChanged += delegate { style.Opacity = (int)opacity.Value; RefreshAlertPreview(); TouchSave(); };
            tx.Children.Add(GeneralRow("文字不透明度", "%", opacity));
            tx.Children.Add(GeneralRow("文字颜色", "", TextSwatches(style)));
            left.Children.Add(text);

            StackPanel fr;
            VigilCard frame = StyleCard("FRAME", out fr);
            VigilNumberField backOpacity = new VigilNumberField(_light, Line, Ink, Sub, 86);
            backOpacity.Minimum = 0;
            backOpacity.Maximum = 100;
            backOpacity.Value = style.BackOpacity;
            backOpacity.ValueChanged += delegate { style.BackOpacity = (int)backOpacity.Value; RefreshAlertPreview(); TouchSave(); };
            fr.Children.Add(GeneralRow("背景不透明度", "%", backOpacity));
            VigilNumberField corner = new VigilNumberField(_light, Line, Ink, Sub, 86);
            corner.Minimum = 0;
            corner.Maximum = 60;
            corner.Value = style.CornerRadius;
            corner.ValueChanged += delegate { style.CornerRadius = (int)corner.Value; RefreshAlertPreview(); TouchSave(); };
            fr.Children.Add(GeneralRow("圆角", "像素", corner));
            fr.Children.Add(GeneralRow("背景颜色", "", BackSwatches(style)));
            right.Children.Add(frame);

            StackPanel mo;
            VigilCard motion = StyleCard("POSITION / MOTION", out mo);
            VigilNumberField px = new VigilNumberField(_light, Line, Ink, Sub, 86);
            px.Minimum = -4000;
            px.Maximum = 4000;
            px.Value = style.X;
            px.ValueChanged += delegate { style.X = (int)px.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("水平位置", "像素", px));
            VigilNumberField py = new VigilNumberField(_light, Line, Ink, Sub, 86);
            py.Minimum = -4000;
            py.Maximum = 4000;
            py.Value = style.Y;
            py.ValueChanged += delegate { style.Y = (int)py.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("垂直位置", "像素", py));
            VigilNumberField inMs = new VigilNumberField(_light, Line, Ink, Sub, 86);
            inMs.Minimum = 0;
            inMs.Maximum = 2000;
            inMs.Value = style.AnimInMs;
            inMs.ValueChanged += delegate { style.AnimInMs = (int)inMs.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("进入时长", "毫秒", inMs));
            VigilNumberField outMs = new VigilNumberField(_light, Line, Ink, Sub, 86);
            outMs.Minimum = 0;
            outMs.Maximum = 2000;
            outMs.Value = style.AnimOutMs;
            outMs.ValueChanged += delegate { style.AnimOutMs = (int)outMs.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("退出时长", "毫秒", outMs));
            VigilNumberField slide = new VigilNumberField(_light, Line, Ink, Sub, 86);
            slide.Minimum = 0;
            slide.Maximum = 200;
            slide.Value = style.SlideDistance;
            slide.ValueChanged += delegate { style.SlideDistance = (int)slide.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("滑动距离", "像素", slide));
            VigilNumberField duration = new VigilNumberField(_light, Line, Ink, Sub, 86);
            duration.Minimum = 1;
            duration.Maximum = 3600;
            duration.Value = alert.DurationSeconds;
            duration.ValueChanged += delegate { alert.DurationSeconds = (int)duration.Value; TouchSave(); };
            mo.Children.Add(GeneralRow("显示时长", "秒", duration));

            List<string> timeModeItems = new List<string>();
            timeModeItems.Add("跟随全局");
            timeModeItems.Add("自动");
            timeModeItems.Add("只显示秒");
            timeModeItems.Add("分 + 秒");
            timeModeItems.Add("时 + 分");
            VigilSelect alertTime = new VigilSelect(PanelAlt, Line, Ink, Accent, 130);
            alertTime.SetItems(timeModeItems, Math.Max(0, Math.Min(4, alert.TimeFormat + 1)));
            alertTime.SelectionChanged += delegate
            {
                alert.TimeFormat = alertTime.SelectedIndex - 1;
                TouchSave();
            };
            mo.Children.Add(GeneralRow("时间格式", "这条提示专用", alertTime));
            right.Children.Add(motion);

            columns.Children.Add(left);
            columns.Children.Add(right);
            detail.Children.Add(columns);
            RefreshAlertPreview();
            return detail;
        }
        private VigilCard StyleCard(string title, out StackPanel body)
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 14);
            card.Margin = new Thickness(0, 0, 0, 12);
            body = new StackPanel();
            body.Children.Add(MonoText(title, 9, Accent));
            card.Child = body;
            _hoverTargets.Add(card);
            return card;
        }

        private StackPanel TextSwatches(WindowStyle style)
        {
            return Swatches(delegate(System.Drawing.Color c)
            {
                style.TextColor = c;
                RefreshAlertPreview();
                TouchSave();
            }, true);
        }

        private StackPanel BackSwatches(WindowStyle style)
        {
            return Swatches(delegate(System.Drawing.Color c)
            {
                style.BackColor = c;
                RefreshAlertPreview();
                TouchSave();
            }, false);
        }

        private StackPanel Swatches(Action<System.Drawing.Color> apply, bool text)
        {
            StackPanel s = new StackPanel();
            s.Orientation = Orientation.Horizontal;
            System.Drawing.Color[] bright =
            {
                System.Drawing.Color.White,
                System.Drawing.Color.FromArgb(56, 214, 255),
                System.Drawing.Color.FromArgb(255, 176, 0),
                System.Drawing.Color.FromArgb(255, 92, 92),
                System.Drawing.Color.FromArgb(107, 227, 154),
                System.Drawing.Color.FromArgb(122, 162, 255)
            };
            System.Drawing.Color[] dark =
            {
                System.Drawing.Color.FromArgb(10, 14, 20),
                System.Drawing.Color.FromArgb(16, 27, 36),
                System.Drawing.Color.FromArgb(26, 36, 48),
                System.Drawing.Color.FromArgb(42, 16, 16),
                System.Drawing.Color.FromArgb(0, 26, 32),
                System.Drawing.Color.FromArgb(30, 30, 36)
            };
            System.Drawing.Color[] colors = text ? bright : dark;
            for (int i = 0; i < colors.Length; i++)
            {
                System.Drawing.Color c = colors[i];
                Border b = new Border();
                b.Width = 22;
                b.Height = 22;
                b.CornerRadius = new CornerRadius(3);
                b.Margin = new Thickness(0, 0, 6, 0);
                b.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                b.BorderBrush = Line;
                b.BorderThickness = new Thickness(1);
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += delegate { apply(c); };
                s.Children.Add(b);
            }
            return s;
        }

        private void RefreshAlertPreview()
        {
            if (_alertPreviewBox == null || _alertPreviewText == null) return;
            if (_alertIndex < 0 || _alertIndex >= _cfg.Alerts.Length) return;
            AlertConfig alert = _cfg.Alerts[_alertIndex];
            string sample = string.IsNullOrEmpty(alert.SampleText) ? alert.DisplayName : alert.SampleText;
            if (sample.Trim().Length <= 2) sample = alert.DisplayName + "   " + sample;

            if (_cfg.EffectivePromptTheme == 1)
            {
                if (_alertPreviewNew != null)
                {
                    _alertPreviewNew.SetContent(alert, sample, false);
                    // 新版提示是压在实际游戏画面上的 HUD；预览也给一块近黑“画面”，
                    // 否则在浅色页面上会像一张孤立的小黑卡。
                    _alertPreviewBox.Background = new SolidColorBrush(_light
                        ? System.Windows.Media.Color.FromRgb(18, 26, 24)
                        : System.Windows.Media.Color.FromRgb(6, 10, 8));
                    _alertPreviewBox.BorderThickness = new Thickness(1);
                    _alertPreviewBox.BorderBrush = Line;
                    _alertPreviewBox.CornerRadius = new CornerRadius(2);
                    _alertPreviewBox.Padding = new Thickness(8);
                    _alertPreviewBox.Child = _alertPreviewNew;
                }
                return;
            }

            WindowStyle s = alert.Style;
            byte alpha = (byte)Math.Max(0, Math.Min(255, s.BackOpacity * 255 / 100));
            _alertPreviewBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, s.BackColor.R, s.BackColor.G, s.BackColor.B));
            _alertPreviewBox.CornerRadius = new CornerRadius(s.CornerRadius);
            _alertPreviewBox.BorderThickness = new Thickness(1);
            _alertPreviewBox.BorderBrush = Line;
            _alertPreviewBox.Padding = new Thickness(28, 16, 28, 16);
            _alertPreviewBox.Child = _alertPreviewText;
            byte ta = (byte)Math.Max(0, Math.Min(255, s.Opacity * 255 / 100));
            _alertPreviewText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(ta, s.TextColor.R, s.TextColor.G, s.TextColor.B));
            _alertPreviewText.FontSize = Math.Max(10, Math.Min(46, s.FontSize));
            _alertPreviewText.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
            _alertPreviewText.Text = sample;
            try { _alertPreviewText.FontFamily = new FontFamily(s.FontFace); } catch { _alertPreviewText.FontFamily = Ui; }
        }
        private UIElement BuildLogPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("日志", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);

            VigilCard runtime = new VigilCard();
            runtime.Fill = Panel;
            runtime.Stroke = Line;
            runtime.Cut = 10;
            runtime.Padding = new Thickness(18, 14, 18, 16);
            runtime.Margin = new Thickness(0, 0, 0, 12);
            StackPanel r = new StackPanel();
            r.Children.Add(MonoText("RUNTIME LOG", 9, Accent));
            VigilToggle enabled = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            enabled.IsChecked = _cfg.EnableLog;
            enabled.CheckedChanged += delegate
            {
                _cfg.EnableLog = enabled.IsChecked;
                Logger.SetEnabled(enabled.IsChecked);
                TouchSave();
            };
            r.Children.Add(GeneralRow("记录运行日志", "排障用，超过 5MB 自动清理", enabled));
            TextBlock path = MonoText(string.IsNullOrEmpty(Logger.Path) ? "(尚未初始化)" : Logger.Path, 9, Sub);
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.Margin = new Thickness(0, 8, 0, 0);
            r.Children.Add(path);
            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.Children.Add(CommandButton("\uE8A7", "打开目录", delegate { OpenLogDirectory(); }, Accent));
            r.Children.Add(actions);
            runtime.Child = r;
            _hoverTargets.Add(runtime);
            page.Children.Add(runtime);

            page.Children.Add(BuildGameCrashCard());

            VigilCard files = new VigilCard();
            files.Fill = Panel;
            files.Stroke = Line;
            files.Cut = 10;
            files.Padding = new Thickness(18, 14, 18, 16);
            files.Margin = new Thickness(0, 0, 0, 12);
            StackPanel f = new StackPanel();
            f.Children.Add(MonoText("FILES", 9, Accent));
            Grid fileRow = new Grid();
            fileRow.Margin = new Thickness(0, 8, 0, 0);
            fileRow.ColumnDefinitions.Add(new ColumnDefinition());
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _logStats = MonoText("", 9.5, Sub);
            _logStats.LineHeight = 20;
            _logStats.VerticalAlignment = VerticalAlignment.Center;
            fileRow.Children.Add(_logStats);
            Button openLogs = CommandButton("\uE8A5", "打开日志", delegate { ShowLogChoice(); }, Accent);
            openLogs.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(openLogs, 1);
            fileRow.Children.Add(openLogs);
            f.Children.Add(fileRow);
            files.Child = f;
            _hoverTargets.Add(files);
            page.Children.Add(files);

            page.Children.Add(BuildBackupCard());

            VigilCard tailCard = new VigilCard();
            tailCard.Fill = Panel;
            tailCard.Stroke = Line;
            tailCard.Cut = 10;
            tailCard.Padding = new Thickness(18, 14, 18, 16);
            StackPanel t = new StackPanel();
            t.Children.Add(MonoText("TAIL  /  LAST 18 LINES", 9, Accent));
            Border box = new Border();
            box.Margin = new Thickness(0, 8, 0, 0);
            box.Background = PanelAlt;
            box.BorderBrush = Line;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(10, 8, 10, 8);
            _logTail = new TextBlock();
            _logTail.FontFamily = Mono;
            _logTail.FontSize = 9.5;
            _logTail.Foreground = Sub;
            _logTail.LineHeight = 15;
            _logTail.TextWrapping = TextWrapping.NoWrap;
            box.Child = _logTail;
            t.Children.Add(box);
            tailCard.Child = t;
            _hoverTargets.Add(tailCard);
            page.Children.Add(tailCard);
            UpdateLogTail();
            return page;
        }

        private UIElement BuildGameCrashCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 16);
            card.Margin = new Thickness(0, 0, 0, 12);
            StackPanel s = new StackPanel();
            s.Children.Add(MonoText("GAME CRASH", 9, Accent));

            VigilToggle toggle = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            toggle.IsChecked = _cfg.MonitorGameCrash;
            _gameCrashToggle = toggle;
            toggle.CheckedChanged += delegate
            {
                bool on = toggle.IsChecked;
                _cfg.MonitorGameCrash = on;
                GameCrashWatcher.Apply(_cfg);
                if (on) _dumpWarning = !GameDumpSettings.Enable();
                TouchSave();
                if (!on) AskDeleteCrashData();
                RefreshGameCrashCard();
            };
            s.Children.Add(GeneralRow("崩溃监控", "记录崩溃与非正常退出；Alt+F4 / quit 不打扰", toggle));

            _gameCrashStats = MonoText("", 9, Sub);
            _gameCrashStats.Margin = new Thickness(0, 8, 0, 0);
            _gameCrashStats.LineHeight = 18;
            s.Children.Add(_gameCrashStats);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.Margin = new Thickness(0, 10, 0, 0);
            actions.Children.Add(CommandButton("\uE8A7", "查看最近", delegate { OpenLatestGameCrash(); }, Accent));
            actions.Children.Add(CommandButton("\uE8A5", "打开目录", delegate { OpenFolderSafe(CrashPaths.Dir(CrashPaths.GameCrash)); }, Sub));
            actions.Children.Add(CommandButton("\uE713", "判断规则", delegate { OpenCrashRules(); }, Sub));
            s.Children.Add(actions);

            card.Child = s;
            _hoverTargets.Add(card);
            RefreshGameCrashCard();
            return card;
        }

        private void RefreshGameCrashCard()
        {
            if (_gameCrashStats == null) return;
            int sessions = GameCrashWatcher.SessionCount();
            if (sessions == 0)
            {
                _gameCrashStats.Text = _cfg.MonitorGameCrash ? "还没有记录" : "未开启";
                if (_dumpWarning) _gameCrashStats.Text += "\n转储目录需要管理员权限，系统转储可能抓不到";
                return;
            }
            long size = CrashPaths.TotalSize(CrashPaths.GameCrash);
            CrashReport last = GameCrashWatcher.LastReport();
            string text = "记录 " + sessions + " 次 · " + CrashPaths.Size(size);
            if (last != null && last.Summary.Length > 0) text += "\n最近：" + last.Summary;
            if (_dumpWarning) text += "\n转储目录需要管理员权限，系统转储可能抓不到";
            _gameCrashStats.Text = text;
        }

        private void OpenLatestGameCrash()
        {
            CrashReport last = GameCrashWatcher.LastReport();
            if (last == null || string.IsNullOrEmpty(last.LogPath) || !File.Exists(last.LogPath))
            {
                if (_gameCrashStats != null) _gameCrashStats.Text = "还没有可查看的记录";
                return;
            }
            CrashNotifier.HidePopup();
            using (LogViewerForm form = new LogViewerForm(last.LogPath)) form.ShowDialog();
            CrashNotifier.ShowPopup();
        }

        private void OpenCrashRules()
        {
            try
            {
                CrashRules.Ensure();
                string path = CrashRules.FilePath;
                if (!File.Exists(path))
                {
                    if (_gameCrashStats != null) _gameCrashStats.Text = "规则文件不可写";
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void AskDeleteCrashData()
        {
            long size = GameCrashWatcher.DataSize();
            if (size <= 0) return;
            System.Windows.MessageBoxResult answer = System.Windows.MessageBox.Show(
                "已记录 " + CrashPaths.Size(size) + " 的崩溃数据。\r\n\r\n"
                + "是：全部删除\r\n否：只删除转储（保留说明和日志）\r\n取消：保留",
                "SCPSLAlert", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
            if (answer == System.Windows.MessageBoxResult.Yes) GameCrashWatcher.DeleteAll();
            else if (answer == System.Windows.MessageBoxResult.No) GameCrashWatcher.DeleteDumps();
        }

        private UIElement BuildBackupCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(18, 14, 18, 16);
            card.Margin = new Thickness(0, 0, 0, 12);
            StackPanel s = new StackPanel();
            s.Children.Add(MonoText("CONFIG BACKUP", 9, Accent));
            _backupStats = MonoText("", 9, Sub);
            _backupStats.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(_backupStats);
            _backupList = new StackPanel();
            _backupList.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(_backupList);
            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.Margin = new Thickness(0, 10, 0, 0);
            actions.Children.Add(CommandButton("\uE74E", "立即备份", delegate { MakeBackup(); }, Accent));
            actions.Children.Add(CommandButton("\uE777", "恢复最近", delegate { RestoreLatestBackup(); }, Accent));
            actions.Children.Add(CommandButton("\uE8A5", "打开目录", delegate { OpenFolderSafe(CrashPaths.Dir(CrashPaths.Backup)); }, Sub));
            s.Children.Add(actions);
            card.Child = s;
            _hoverTargets.Add(card);
            RefreshBackupCard();
            return card;
        }

        private string ConfigPath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        }

        private void RefreshBackupCard()
        {
            if (_backupList == null) return;
            _backupList.Children.Clear();
            List<BackupEntry> list = ConfigBackup.List();
            if (_backupStats != null)
                _backupStats.Text = list.Count == 0
                    ? "还没有备份"
                    : "共 " + list.Count + " 份 · " + CrashPaths.Size(CrashPaths.TotalSize(CrashPaths.Backup));
            if (list.Count == 0)
            {
                TextBlock empty = MonoText("保存配置后会自动留一份", 9, Muted);
                _backupList.Children.Add(empty);
                return;
            }
            int show = Math.Min(4, list.Count);
            for (int i = 0; i < show; i++)
                _backupList.Children.Add(BackupRow(list[i]));
        }

        private UIElement BackupRow(BackupEntry entry)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock time = MonoText(entry.TimeText, 9, Sub);
            time.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(time);
            TextBlock size = MonoText(entry.SizeText, 9, Muted);
            size.VerticalAlignment = VerticalAlignment.Center;
            size.Margin = new Thickness(10, 0, 6, 0);
            Grid.SetColumn(size, 1);
            g.Children.Add(size);
            Button restore = CommandButton("\uE777", "恢复", delegate { RestoreBackup(entry.Path); }, Accent);
            Grid.SetColumn(restore, 2);
            g.Children.Add(restore);
            return g;
        }

        private void MakeBackup()
        {
            string made = ConfigBackup.Snapshot(ConfigPath(), "manual");
            RefreshBackupCard();
            if (_backupStats != null)
                _backupStats.Text = made.Length > 0
                    ? "已备份 " + System.IO.Path.GetFileName(made)
                    : "备份失败，检查程序目录是否可写";
        }

        private void RestoreLatestBackup()
        {
            List<BackupEntry> list = ConfigBackup.List();
            if (list.Count == 0)
            {
                if (_backupStats != null) _backupStats.Text = "还没有可恢复的备份";
                return;
            }
            RestoreBackup(list[0].Path);
        }

        private void RestoreBackup(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            System.Windows.MessageBoxResult answer = System.Windows.MessageBox.Show(
                "用这份备份覆盖当前 config.ini？\r\n\r\n" + name,
                "SCPSLAlert", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            if (!ConfigBackup.Restore(path, ConfigPath()))
            {
                if (_backupStats != null) _backupStats.Text = "恢复失败";
                return;
            }
            RefreshBackupCard();
            if (_restart == null) return;
            System.Windows.MessageBoxResult restart = System.Windows.MessageBox.Show(
                "已恢复，重启程序后生效。\r\n\r\n现在重启？",
                "SCPSLAlert", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (restart == System.Windows.MessageBoxResult.Yes) _restart();
        }

        private void OpenLogDirectory()
        {
            try
            {
                string path = Logger.Path;
                if (string.IsNullOrEmpty(path)) path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SCPSLAlert.log");
                string dir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
                if (!Directory.Exists(dir)) return;
                if (File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                if (_logChoiceStatus != null) _logChoiceStatus.Text = "已在资源管理器中打开日志位置。";
                Logger.On("日志", "打开日志目录：" + dir);
            }
            catch (Exception ex)
            {
                if (_logChoiceStatus != null) _logChoiceStatus.Text = "打不开资源管理器：" + ex.Message;
            }
        }

        private void UpdateLogTail()
        {
            if (_logTail == null) return;
            try
            {
                RefreshBackupCard();
                RefreshGameCrashCard();
                string main = Logger.Path;
                string time = Logger.TimePath;
                string ocr = Logger.OcrPath;
                _logStats.Text = "SCPSLAlert.log        " + FileSize(main) + "\n"
                    + "SCPSLAlert.时间.log   " + FileSize(time) + "\n"
                    + "SCPSLAlert.OCR.log    " + FileSize(ocr);
                string text = "";
                if (!string.IsNullOrEmpty(main) && File.Exists(main))
                {
                    string[] lines = File.ReadAllLines(main);
                    int start = Math.Max(0, lines.Length - 18);
                    for (int i = start; i < lines.Length; i++) text += lines[i] + "\n";
                }
                if (text.Length == 0) text = "(暂无日志)";
                _logTail.Text = text.TrimEnd();
            }
            catch { }
        }

        private static string FileSize(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "--";
                long len = new FileInfo(path).Length;
                if (len < 1024) return len + " B";
                if (len < 1048576) return (len / 1024.0).ToString("0.0") + " KB";
                return (len / 1048576.0).ToString("0.0") + " MB";
            }
            catch { return "--"; }
        }
        private UIElement BuildGeneralPage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 16);
            TextBlock title = DisplayText("常规", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);

            VigilCard alerts = new VigilCard();
            alerts.Fill = Panel;
            alerts.Stroke = Line;
            alerts.Cut = 10;
            alerts.Padding = new Thickness(18, 14, 18, 16);
            alerts.Margin = new Thickness(0, 0, 0, 12);
            StackPanel alertBody = new StackPanel();
            alertBody.Children.Add(MonoText("ALERT SWITCHES  /  " + _cfg.Alerts.Length, 9, Accent));
            UniformGrid grid = new UniformGrid();
            // 常规页的“开关组”统一 3 列；窗口变窄时由 Below 的响应式方法降到 2/1 列。
            grid.Columns = 3;
            grid.Margin = new Thickness(0, 8, 0, 0);
            MakeResponsiveColumns(grid, 300);
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                AlertConfig alert = _cfg.Alerts[i];
                Grid cell = new Grid();
                cell.Margin = new Thickness(0, 6, 14, 6);
                cell.ToolTip = "提示开关：" + alert.DisplayName;
                cell.Tag = new SettingMeta { Title = alert.DisplayName, Desc = "提示开关" };
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cell.ColumnDefinitions.Add(new ColumnDefinition());
                VigilToggle toggle = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
                toggle.IsChecked = alert.Style.Enabled;
                toggle.CheckedChanged += delegate
                {
                    alert.Style.Enabled = toggle.IsChecked;
                    TouchSave();
                };
                cell.Children.Add(toggle);
                TextBlock name = new TextBlock();
                name.Text = alert.DisplayName;
                name.FontSize = 11.5;
                name.Foreground = Ink;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(name, 1);
                cell.Children.Add(name);
                grid.Children.Add(cell);
            }
            alertBody.Children.Add(grid);
            alerts.Child = alertBody;
            _hoverTargets.Add(alerts);
            page.Children.Add(alerts);

            VigilCard behavior = new VigilCard();
            behavior.Fill = Panel;
            behavior.Stroke = Line;
            behavior.Cut = 10;
            behavior.Padding = new Thickness(18, 14, 18, 16);
            behavior.Margin = new Thickness(0, 0, 0, 12);
            StackPanel b = new StackPanel();
            b.Children.Add(MonoText("DISPLAY BEHAVIOR", 9, Accent));
            UniformGrid behaviorGrid = new UniformGrid();
            behaviorGrid.Columns = 2;
            behaviorGrid.Margin = new Thickness(0, 2, 0, 0);
            MakeResponsiveColumns(behaviorGrid, 380);
            VigilNumberField seconds = new VigilNumberField(_light, Line, Ink, Sub, 96);
            seconds.Minimum = 1;
            seconds.Maximum = 600;
            seconds.Unit = "秒";
            seconds.Value = _cfg.DisplaySeconds;
            seconds.ValueChanged += delegate { _cfg.DisplaySeconds = (int)seconds.Value; TouchSave(); };
            behaviorGrid.Children.Add(GeneralRow("事件提示显示", "秒后自动缩回", seconds));

            VigilToggle persistent = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            persistent.IsChecked = _cfg.RoundTimer.Persistent;
            persistent.CheckedChanged += delegate { _cfg.RoundTimer.Persistent = persistent.IsChecked; TouchSave(); };
            behaviorGrid.Children.Add(GeneralRow("回合时间常驻显示", "不自动缩回", persistent));

            List<string> timeModes = new List<string>();
            timeModes.Add("自动（推荐）");
            timeModes.Add("只显示秒");
            timeModes.Add("分 + 秒");
            timeModes.Add("时 + 分");
            VigilSelect timeFormat = new VigilSelect(PanelAlt, Line, Ink, Accent, 130);
            timeFormat.SetItems(timeModes, Math.Max(0, Math.Min(3, _cfg.TimeFormat)));
            timeFormat.SelectionChanged += delegate
            {
                _cfg.TimeFormat = Math.Max(0, Math.Min(3, timeFormat.SelectedIndex));
                TouchSave();
            };
            behaviorGrid.Children.Add(GeneralRow("时间格式", "全局默认；单条提示可覆盖", timeFormat));

            StackPanel seg = new StackPanel();
            seg.Orientation = Orientation.Horizontal;
            Border[] segs = new Border[SCPSLAlert.UiAnim.Names.Length];
            for (int i = 0; i < SCPSLAlert.UiAnim.Names.Length; i++)
            {
                int index = i;
                Border item = new Border();
                item.CornerRadius = new CornerRadius(4);
                item.BorderThickness = new Thickness(1);
                item.Height = 26;
                item.MinWidth = 40;
                item.Padding = new Thickness(10, 0, 10, 0);
                item.Margin = new Thickness(0, 0, 5, 0);
                item.Cursor = Cursors.Hand;
                item.RenderTransformOrigin = new Point(0.5, 0.5);
                TextBlock label = MonoText(i == 0 ? "OFF" : (i == 1 ? "30" : (i == 2 ? "45" : (i == 3 ? "60" : "120"))), 9.5, Sub);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                item.Child = label;
                segs[i] = item;
                item.MouseEnter += delegate
                {
                    if (_cfg.PanelAnimQuality == index) return;
                    item.BorderBrush = Accent;
                    item.Background = new SolidColorBrush(VigilButtonPalette.Mix(ColorOf(PanelAlt), ColorOf(Accent), 0.10));
                    label.Foreground = Ink;
                };
                item.MouseLeave += delegate
                {
                    bool on = _cfg.PanelAnimQuality == index;
                    item.BorderBrush = on ? Accent : Line;
                    item.Background = on ? AccentSoftBrush() : Brushes.Transparent;
                    label.Foreground = on ? Accent : Sub;
                    item.RenderTransform = Transform.Identity;
                };
                item.MouseLeftButtonDown += delegate
                {
                    item.RenderTransform = new ScaleTransform(0.94, 0.94);
                };
                item.MouseLeftButtonUp += delegate
                {
                    item.RenderTransform = Transform.Identity;
                    _cfg.PanelAnimQuality = index;
                    SCPSLAlert.UiAnim.Quality = index;
                    if (_orbit != null) _orbit.SetQuality(index);
                    for (int k = 0; k < segs.Length; k++)
                    {
                        bool on = k == index;
                        segs[k].BorderBrush = on ? Accent : Line;
                        segs[k].Background = on ? AccentSoftBrush() : Brushes.Transparent;
                        ((TextBlock)segs[k].Child).Foreground = on ? Accent : Sub;
                    }
                    TouchSave();
                };
                seg.Children.Add(item);
            }
            for (int i = 0; i < segs.Length; i++)
            {
                bool on = i == _cfg.PanelAnimQuality;
                segs[i].BorderBrush = on ? Accent : Line;
                segs[i].Background = on ? AccentSoftBrush() : Brushes.Transparent;
                ((TextBlock)segs[i].Child).Foreground = on ? Accent : Sub;
            }
            behaviorGrid.Children.Add(GeneralRow("动画效果", "档位决定帧率与光效", seg));


            VigilToggle startup = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            startup.IsChecked = _cfg.StartupShow;
            startup.CheckedChanged += delegate { _cfg.StartupShow = startup.IsChecked; TouchSave(); };
            behaviorGrid.Children.Add(GeneralRow("启动演出", "启动时播放全部提示展开", startup));

            VigilToggle deathAlarm = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            deathAlarm.IsChecked = _cfg.HeartRateAlarmEnabled;
            deathAlarm.CheckedChanged += delegate { _cfg.HeartRateAlarmEnabled = deathAlarm.IsChecked; TouchSave(); };
            behaviorGrid.Children.Add(GeneralRow("角色死亡报警音", "关闭后死亡不再播放监护仪长音", deathAlarm));

            VigilToggle quitWithGame = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            quitWithGame.IsChecked = _cfg.ExitWithGame;
            quitWithGame.CheckedChanged += delegate { _cfg.ExitWithGame = quitWithGame.IsChecked; TouchSave(); };
            behaviorGrid.Children.Add(GeneralRow("游戏退出时关闭本程序",
                "关掉后：游戏退出只弹提示，程序继续留在托盘", quitWithGame));
            b.Children.Add(behaviorGrid);
            behavior.Child = b;
            _hoverTargets.Add(behavior);
            page.Children.Add(behavior);

            VigilCard tactical = new VigilCard();
            tactical.Fill = Panel;
            tactical.Stroke = Line;
            tactical.Cut = 10;
            tactical.Padding = new Thickness(18, 14, 18, 16);
            tactical.Margin = new Thickness(0, 0, 0, 12);
            StackPanel tac = new StackPanel();
            tac.Children.Add(MonoText("TACTICAL PANEL  /  显示在游戏上的战术面板", 9, Accent));

            WpfSlider tacOpacity = new WpfSlider(Accent, TrackOffBrush(), KnobBrush(), Ink, Sub, 220);
            tacOpacity.Minimum = 40;
            tacOpacity.Maximum = 100;
            tacOpacity.Step = 1;
            tacOpacity.Unit = "%";
            tacOpacity.SetValueSilent(_cfg.TacticalOpacity);
            tacOpacity.ValueChanged += delegate
            {
                _cfg.TacticalOpacity = (int)tacOpacity.Value;
                TouchSave();
                if (_applyTactical != null) _applyTactical();
            };
            tac.Children.Add(GeneralRow("面板透明度", "100% 最流畅；调低可透出游戏", tacOpacity));

            StackPanel openSeg = new StackPanel();
            openSeg.Orientation = Orientation.Horizontal;
            string[] openNames = { "上下展开", "顶端展开", "淡入" };
            Border[] openSegs = new Border[openNames.Length];
            for (int i = 0; i < openNames.Length; i++)
            {
                int index = i;
                Border item = new Border();
                item.CornerRadius = new CornerRadius(4);
                item.BorderThickness = new Thickness(1);
                item.Height = 26;
                item.MinWidth = 54;
                item.Padding = new Thickness(10, 0, 10, 0);
                item.Margin = new Thickness(0, 0, 5, 0);
                item.Cursor = Cursors.Hand;
                TextBlock label = MonoText(openNames[i], 9.5, Sub);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                item.Child = label;
                openSegs[i] = item;
                item.MouseEnter += delegate
                {
                    if (_cfg.TacticalOpenStyle == index) return;
                    item.BorderBrush = Accent;
                    label.Foreground = Ink;
                };
                item.MouseLeave += delegate
                {
                    bool on = _cfg.TacticalOpenStyle == index;
                    item.BorderBrush = on ? Accent : Line;
                    item.Background = on ? AccentSoftBrush() : Brushes.Transparent;
                    label.Foreground = on ? Accent : Sub;
                };
                item.MouseLeftButtonUp += delegate
                {
                    _cfg.TacticalOpenStyle = index;
                    for (int k = 0; k < openSegs.Length; k++)
                    {
                        bool on = k == index;
                        openSegs[k].BorderBrush = on ? Accent : Line;
                        openSegs[k].Background = on ? AccentSoftBrush() : Brushes.Transparent;
                        ((TextBlock)openSegs[k].Child).Foreground = on ? Accent : Sub;
                    }
                    TouchSave();
                };
                openSeg.Children.Add(item);
            }
            for (int i = 0; i < openSegs.Length; i++)
            {
                bool on = i == _cfg.TacticalOpenStyle;
                openSegs[i].BorderBrush = on ? Accent : Line;
                openSegs[i].Background = on ? AccentSoftBrush() : Brushes.Transparent;
                ((TextBlock)openSegs[i].Child).Foreground = on ? Accent : Sub;
            }
            tac.Children.Add(GeneralRow("打开方式", "战术面板呼出时的入场", openSeg));

            Button editLayout = CommandButton("\uE70F", "编辑战术面板布局", delegate
            {
                if (_editTactical != null) _editTactical();
            }, Accent);
            tac.Children.Add(GeneralRow("显示项目与位置", "拖动把手排序，或添加发电机 / 刷新 / 时间线", editLayout));

            UniformGrid widgets = new UniformGrid();
            widgets.Columns = 3;
            widgets.Margin = new Thickness(0, 4, 0, 0);
            MakeResponsiveColumns(widgets, 280);
            string[] ids = TacWidgets.Ids;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                Grid cell = new Grid();
                cell.Margin = new Thickness(0, 5, 14, 5);
                cell.ToolTip = TacWidgets.Desc(id);
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cell.ColumnDefinitions.Add(new ColumnDefinition());
                VigilToggle tg = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
                tg.IsChecked = TacWidgets.Parse(_cfg.TacticalWidgets).Contains(id);
                tg.CheckedChanged += delegate
                {
                    List<string> cur = TacWidgets.Parse(_cfg.TacticalWidgets);
                    bool has = cur.Contains(id);
                    if (tg.IsChecked && !has) cur.Add(id);
                    if (!tg.IsChecked && has) cur.Remove(id);
                    _cfg.TacticalWidgets = TacWidgets.Serialize(cur);
                    TouchSave();
                    if (_applyTactical != null) _applyTactical();
                };
                cell.Children.Add(tg);
                StackPanel label = new StackPanel();
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Margin = new Thickness(10, 0, 0, 0);
                TextBlock n = new TextBlock();
                n.Text = TacWidgets.Name(id);
                n.FontSize = 11;
                n.Foreground = Ink;
                label.Children.Add(n);
                Grid.SetColumn(label, 1);
                cell.Children.Add(label);
                widgets.Children.Add(cell);
            }
            tac.Children.Add(widgets);

            TextBlock dataTitle = MonoText("CURRENT DATA  /  当前识别到的数据", 8.5, Sub);
            dataTitle.Margin = new Thickness(0, 10, 0, 3);
            tac.Children.Add(dataTitle);
            _tacticalData = MonoText(TacticalDataText(), 8.5, Sub);
            _tacticalData.TextWrapping = TextWrapping.Wrap;
            tac.Children.Add(_tacticalData);

            tactical.Child = tac;
            _hoverTargets.Add(tactical);
            page.Children.Add(tactical);

            VigilCard position = new VigilCard();
            position.Fill = Panel;
            position.Stroke = Line;
            position.Cut = 10;
            position.Padding = new Thickness(18, 14, 18, 16);
            StackPanel p = new StackPanel();
            p.Children.Add(MonoText("POSITION OFFSET", 9, Accent));
            VigilNumberField x = new VigilNumberField(_light, Line, Ink, Sub, 96);
            x.Minimum = -2000;
            x.Maximum = 2000;
            x.Unit = "px";
            x.Value = _cfg.OffsetX;
            x.ValueChanged += delegate { _cfg.OffsetX = (int)x.Value; TouchSave(); };
            p.Children.Add(GeneralRow("水平偏移", "像素", x));
            VigilNumberField y = new VigilNumberField(_light, Line, Ink, Sub, 96);
            y.Minimum = -2000;
            y.Maximum = 2000;
            y.Unit = "px";
            y.Value = _cfg.OffsetY;
            y.ValueChanged += delegate { _cfg.OffsetY = (int)y.Value; TouchSave(); };
            p.Children.Add(GeneralRow("垂直偏移", "像素", y));
            Button reset = CommandButton("\uE72C", "重置为 0", delegate
            {
                _cfg.OffsetX = 0;
                _cfg.OffsetY = 0;
                x.Value = 0;
                y.Value = 0;
                TouchSave();
            }, Sub);
            p.Children.Add(GeneralRow("恢复默认布局", "所有提示回到初始位置", reset));
            position.Child = p;
            _hoverTargets.Add(position);
            page.Children.Add(position);
            return page;
        }

        /// <summary>
        /// 开关组自适应列数：默认 3 列；宽度不够时降 2/1 列，
        /// 避免高 DPI 小工作区把开关文字挤成两行。
        /// </summary>
        private static void MakeResponsiveColumns(UniformGrid grid, double minCellWidth)
        {
            if (grid == null || minCellWidth <= 1) return;
            grid.SizeChanged += delegate
            {
                double w = grid.ActualWidth;
                if (w <= 1) return;
                int cols = (int)Math.Floor(w / minCellWidth);
                if (cols < 1) cols = 1;
                if (cols > 3) cols = 3;
                if (grid.Columns != cols) grid.Columns = cols;
            };
        }

        private UIElement GeneralRow(string title, string desc, FrameworkElement control)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 6, 0, 6);
            g.Tag = new SettingMeta { Title = title, Desc = desc };
            if (!string.IsNullOrEmpty(desc))
            {
                g.ToolTip = desc;
                ToolTipService.SetShowDuration(g, 20000);
            }
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            StackPanel text = new StackPanel();
            if (!string.IsNullOrEmpty(desc)) text.ToolTip = desc;
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = Math.Max(8, Math.Min(96, _cfg.FontBodySize));
            t.Foreground = Ink;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(t);
            g.Children.Add(text);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        #region 设置搜索

        private static readonly string[] SearchPageNames =
        {
            "状态", "常规", "提示", "触发点", "时间", "热键", "日志", "工具", "高级", "外观"
        };

        private void BuildSearchLayer()
        {
            if (_body == null) return;

            _searchIndexBuilt = false;
            _searchIndex.Clear();

            _searchLayer = new Grid();
            _searchLayer.Visibility = Visibility.Collapsed;
            _searchLayer.Background = new SolidColorBrush(Color.FromArgb(182, 0, 0, 0));
            _searchLayer.IsHitTestVisible = true;
            Grid.SetColumn(_searchLayer, 0);
            Grid.SetColumnSpan(_searchLayer, 2);
            System.Windows.Controls.Panel.SetZIndex(_searchLayer, 500);

            _searchLayer.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == _searchLayer) HideSearch();
            };
            _body.Children.Add(_searchLayer);

            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 12;
            card.Padding = new Thickness(22, 18, 22, 16);
            card.Width = 720;
            card.MaxHeight = 620;
            card.Margin = new Thickness(20);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;

            StackPanel stack = new StackPanel();

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel titles = new StackPanel();
            titles.Children.Add(MonoText("SEARCH  /  SETTINGS", 9, Accent));
            TextBlock title = DisplayText("搜索设置", 24, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 1, 0, 0);
            titles.Children.Add(title);
            head.Children.Add(titles);
            Button close = CommandButton("\uE711", "关闭", delegate { HideSearch(); }, Danger);
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            stack.Children.Add(head);

            TextBlock tip = new TextBlock();
            tip.Text = "输入关键词，搜索设置 / 开关 / 快捷键；Ctrl+F 随时打开，Esc 关闭。";
            tip.FontSize = 10;
            tip.Foreground = Muted;
            tip.Margin = new Thickness(0, 4, 0, 10);
            stack.Children.Add(tip);

            _searchBox = new TextBox();
            _searchBox.Height = 40;
            _searchBox.FontSize = 14;
            _searchBox.FontFamily = Ui;
            _searchBox.Foreground = Ink;
            _searchBox.Background = PanelAlt;
            _searchBox.CaretBrush = Accent;
            _searchBox.BorderBrush = Line;
            _searchBox.BorderThickness = new Thickness(1);
            _searchBox.Padding = new Thickness(11, 8, 11, 8);
            WpfTextInputFallback.Attach(_searchBox, false);
            _searchBox.TextChanged += delegate { FilterSearch(); };
            _searchBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    List<SettingHit> hits = MatchSettings();
                    if (hits.Count > 0) JumpToSearchHit(hits[0]);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    HideSearch();
                }
            };
            stack.Children.Add(_searchBox);

            _searchStatus = MonoText("输入关键词…", 9, Muted);
            _searchStatus.Margin = new Thickness(0, 9, 0, 6);
            stack.Children.Add(_searchStatus);

            _searchResults = new StackPanel();
            ScrollViewer scroll = new ScrollViewer();
            scroll.MaxHeight = 360;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Content = _searchResults;
            stack.Children.Add(scroll);

            card.Child = stack;
            _searchLayer.Children.Add(card);
        }

        private void ShowSearch()
        {
            if (_searchLayer == null) BuildSearchLayer();
            if (_searchLayer == null) return;
            _searchLayer.Visibility = Visibility.Visible;
            if (_searchBox != null)
            {
                _searchBox.Text = "";
                _searchBox.Focus();
                Keyboard.Focus(_searchBox);
            }
            if (_searchResults != null) _searchResults.Children.Clear();
            if (_searchStatus != null) _searchStatus.Text = "输入关键词…";
        }

        private void HideSearch()
        {
            if (_searchLayer != null) _searchLayer.Visibility = Visibility.Collapsed;
            if (_searchBox != null) _searchBox.Text = "";
            if (_searchResults != null) _searchResults.Children.Clear();
            if (_searchStatus != null) _searchStatus.Text = "输入关键词…";
            Focus();
        }

        private void FilterSearch()
        {
            if (_searchStatus == null || _searchResults == null) return;
            _searchResults.Children.Clear();

            string q = _searchBox == null ? "" : (_searchBox.Text ?? "").Trim();
            if (q.Length == 0)
            {
                _searchStatus.Text = "输入关键词…";
                return;
            }

            List<SettingHit> hits = MatchSettings();
            int show = Math.Min(40, hits.Count);
            for (int i = 0; i < show; i++)
                AddSearchResult(hits[i]);
            _searchStatus.Text = "找到 " + hits.Count + " 项"
                + (hits.Count > show ? "，显示前 " + show + " 项" : "");
        }

        private List<SettingHit> MatchSettings()
        {
            EnsureSearchIndex();
            string q = _searchBox == null ? "" : (_searchBox.Text ?? "").Trim();
            List<SettingHit> hits = new List<SettingHit>();
            if (q.Length == 0) return hits;

            string[] tokens = q.Split(new char[] { ' ', '\u3000', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < _searchIndex.Count; i++)
            {
                SettingHit h = _searchIndex[i];
                string all = (h.Title + " " + h.Desc + " " + h.PageName).ToLowerInvariant();
                bool ok = true;
                for (int k = 0; k < tokens.Length; k++)
                {
                    if (all.IndexOf(tokens[k].ToLowerInvariant(), StringComparison.Ordinal) < 0)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) hits.Add(h);
            }

            hits.Sort(delegate(SettingHit a, SettingHit b)
            {
                int at = a.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
                int bt = b.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
                if (at != bt) return at - bt;
                int ap = a.Row == null ? 1 : 0;
                int bp = b.Row == null ? 1 : 0;
                if (ap != bp) return ap - bp;
                return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
            });
            return hits;
        }

        private void AddSearchResult(SettingHit hit)
        {
            Border row = new Border();
            row.Background = PanelAlt;
            row.BorderBrush = Line;
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(10, 8, 10, 8);
            row.Margin = new Thickness(0, 0, 0, 2);
            row.Cursor = Cursors.Hand;

            StackPanel s = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = hit.Title;
            title.FontSize = 12;
            title.Foreground = Ink;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Children.Add(title);
            TextBlock meta = MonoText(hit.PageName + "  /  "
                + (hit.Row == null ? "页面" : (string.IsNullOrEmpty(hit.Desc) ? "设置" : hit.Desc)),
                8.5, Muted);
            meta.Margin = new Thickness(0, 2, 0, 0);
            meta.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Children.Add(meta);
            row.Child = s;

            row.MouseEnter += delegate { row.Background = Panel; };
            row.MouseLeave += delegate { row.Background = PanelAlt; };
            row.MouseLeftButtonUp += delegate { JumpToSearchHit(hit); };
            _searchResults.Children.Add(row);
        }

        private void JumpToSearchHit(SettingHit hit)
        {
            HideSearch();
            if (hit.Heart)
            {
                SelectPage(2);
                SelectAlert(_cfg.Alerts.Length);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        UpdateLayout();
                        FrameworkElement row = FindSettingRow(_alertDetail, hit.Title);
                        if (row != null)
                        {
                            row.BringIntoView();
                            HighlightSearchHit(row);
                        }
                    }
                    catch { }
                }), DispatcherPriority.Background);
                return;
            }
            SelectPage(hit.Page);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    UpdateLayout();
                    if (hit.Row != null)
                    {
                        hit.Row.BringIntoView();
                        HighlightSearchHit(hit.Row);
                    }
                    else
                    {
                        ScrollViewer sv = PageScroll(hit.Page);
                        if (sv != null) sv.ScrollToTop();
                    }
                }
                catch { }
            }), DispatcherPriority.Background);
        }

        private ScrollViewer PageScroll(int page)
        {
            if (page == 1) return _generalPage;
            if (page == 2) return _alertPage;
            if (page == 3) return _audioPage;
            if (page == 4) return _timePage;
            if (page == 5) return _hotkeyPage;
            if (page == 6) return _logPage;
            if (page == 7) return _toolsPage;
            if (page == 8) return _advancedPage;
            if (page == 9) return _appearancePage;
            return null;
        }

        private FrameworkElement FindSettingRow(DependencyObject root, string title)
        {
            if (root == null) return null;
            FrameworkElement fe = root as FrameworkElement;
            if (fe != null)
            {
                SettingMeta meta = fe.Tag as SettingMeta;
                if (meta != null && string.Equals(meta.Title, title, StringComparison.Ordinal))
                    return fe;
            }
            foreach (object item in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject child = item as DependencyObject;
                FrameworkElement hit = child == null ? null : FindSettingRow(child, title);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>搜索跳转后给目标行加一层短暂高亮，方便在长页里一眼看到。</summary>
        private void HighlightSearchHit(FrameworkElement row)
        {
            RestoreSearchHit();
            if (row == null) return;
            _searchHitRow = row;
            if (row is System.Windows.Controls.Panel)
            {
                _searchHitOldBg = ((System.Windows.Controls.Panel)row).Background;
                ((System.Windows.Controls.Panel)row).Background = AccentSoftBrush();
            }
            else if (row is Border)
            {
                _searchHitOldBg = ((Border)row).Background;
                ((Border)row).Background = AccentSoftBrush();
            }
            else
            {
                _searchHitRow = null;
                return;
            }

            if (_searchHitTimer == null)
            {
                _searchHitTimer = new DispatcherTimer();
                _searchHitTimer.Interval = TimeSpan.FromMilliseconds(1600);
                _searchHitTimer.Tick += delegate { _searchHitTimer.Stop(); RestoreSearchHit(); };
            }
            _searchHitTimer.Stop();
            _searchHitTimer.Start();
        }

        private void RestoreSearchHit()
        {
            if (_searchHitRow == null) return;
            if (_searchHitRow is System.Windows.Controls.Panel)
                ((System.Windows.Controls.Panel)_searchHitRow).Background = _searchHitOldBg;
            else if (_searchHitRow is Border) ((Border)_searchHitRow).Background = _searchHitOldBg;
            _searchHitRow = null;
            _searchHitOldBg = null;
        }

        private void EnsureSearchIndex()
        {
            if (_searchIndexBuilt) return;
            _searchIndexBuilt = true;
            _searchIndex.Clear();

            try { if (_generalPage != null && _generalPage.Content == null) _generalPage.Content = BuildGeneralPage(); } catch { }
            try { if (_alertPage != null && _alertPage.Content == null) _alertPage.Content = BuildAlertPage(); } catch { }
            try { if (_audioPage != null && _audioPage.Content == null) _audioPage.Content = BuildAudioPage(); } catch { }
            try { if (_timePage != null && _timePage.Content == null) _timePage.Content = BuildTimePage(); } catch { }
            try { if (_hotkeyPage != null && _hotkeyPage.Content == null) _hotkeyPage.Content = BuildHotkeyPage(); } catch { }
            try { if (_logPage != null && _logPage.Content == null) _logPage.Content = BuildLogPage(); } catch { }
            try { if (_toolsPage != null && _toolsPage.Content == null) _toolsPage.Content = BuildToolsPage(); } catch { }
            try { if (_advancedPage != null && _advancedPage.Content == null) _advancedPage.Content = BuildAdvancedPage(); } catch { }

            for (int i = 0; i < SearchPageNames.Length; i++)
            {
                if (i > 0)
                {
                    ScrollViewer sv = PageScroll(i);
                    if (sv != null && sv.Content != null)
                        CollectSettingHits(sv.Content as DependencyObject, i, SearchPageNames[i]);
                }
                _searchIndex.Add(new SettingHit
                {
                    Title = SearchPageNames[i] + " 页面",
                    Desc = "跳到该页顶部",
                    Page = i,
                    PageName = SearchPageNames[i],
                    Row = null
                });
            }
            AddHeartSearchHits();
        }

        private void AddHeartSearchHits()
        {
            string[,] rows = new string[,]
            {
                { "屏幕心率提示", "打开 / 关闭心率屏幕提示" },
                { "显示模式", "完整卡片 / 无框大字 / 只显示数字" },
                { "外框", "完整卡片模式下是否画背景和边框" },
                { "显示标签", "HEART RATE / 心率" },
                { "显示 BPM 单位", "数字后面的 BPM" },
                { "心形脉冲", "心形图标随 BPM 跳动" },
                { "整条心电波", "整条波形随心跳突然张开" },
                { "每次波点", "每次心跳随机挑几个点冒波" },
                { "波形振幅", "局部小波的振幅" },
                { "波形宽度", "局部小波的宽度" },
                { "数字大小", "心率数字字号" },
                { "不透明度", "心率提示整体透明度" },
                { "主色", "心率数字 / 心形 / 边框颜色" },
                { "位置 X", "心率提示水平位置，-1 = 自动右上角" },
                { "位置 Y", "心率提示垂直位置，-1 = 自动右上角" }
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                _searchIndex.Add(new SettingHit
                {
                    Title = rows[i, 0],
                    Desc = rows[i, 1],
                    Page = 2,
                    PageName = "提示",
                    Row = null,
                    Heart = true
                });
            }
        }

        private void CollectSettingHits(DependencyObject root, int page, string pageName)
        {
            if (root == null) return;
            FrameworkElement fe = root as FrameworkElement;
            if (fe != null)
            {
                SettingMeta meta = fe.Tag as SettingMeta;
                if (meta != null)
                {
                    _searchIndex.Add(new SettingHit
                    {
                        Title = meta.Title,
                        Desc = meta.Desc,
                        Page = page,
                        PageName = pageName,
                        Row = fe
                    });
                }
            }
            foreach (object item in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject child = item as DependencyObject;
                if (child != null) CollectSettingHits(child, page, pageName);
            }
        }

        #endregion

        private string TacticalDataText()
        {
            string s = "";
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                bool present = _cfg.GetTactical("pre_" + code) == "1";
                if (code == "079")
                {
                    if (present) s += "SCP-079 在场    ";
                    continue;
                }
                string cur = _cfg.GetTactical("cur_" + code);
                string hp = _cfg.GetTactical("hp_" + code);
                if (!present && string.IsNullOrEmpty(cur) && string.IsNullOrEmpty(hp)) continue;
                string total = string.IsNullOrEmpty(hp) ? TacticalLog.ScpDefaultHp[i].ToString() : hp;
                s += "SCP-" + code + " " + (string.IsNullOrEmpty(cur) ? "?" : cur)
                    + "/" + total + (present ? "" : "（不在场）") + "    ";
            }
            string mt = _cfg.GetTactical("tok_MTF");
            string ct = _cfg.GetTactical("tok_混沌");
            string mi = _cfg.GetTactical("inf_MTF");
            string ci = _cfg.GetTactical("inf_混沌");
            if (!string.IsNullOrEmpty(mt) || !string.IsNullOrEmpty(ct)
                || !string.IsNullOrEmpty(mi) || !string.IsNullOrEmpty(ci))
            {
                if (s.Length > 0) s += "\n";
                s += "令牌  MTF " + (string.IsNullOrEmpty(mt) ? "?" : mt)
                    + " / 混沌 " + (string.IsNullOrEmpty(ct) ? "?" : ct)
                    + "    影响力  " + (string.IsNullOrEmpty(mi) ? "?" : mi)
                    + " / " + (string.IsNullOrEmpty(ci) ? "?" : ci);
            }
            return s.Length == 0 ? "（还没有数据：进游戏后由识别或面板写入）" : s;
        }

        private Brush TrackOffBrush()
        {
            return _light ? Solid(190, 202, 212) : Solid(52, 74, 84);
        }

        private Brush KnobBrush()
        {
            return _light ? Brushes.White : Solid(235, 246, 250);
        }

        private Brush AccentSoftBrush()
        {
            System.Drawing.Color c = ThemeTokens.For(_light);
            return Solid(System.Drawing.Color.FromArgb(_light ? 26 : 40, c.R, c.G, c.B));
        }

        private void TouchSave()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        private UIElement StatusTop()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            g.Margin = new Thickness(0, 6, 0, 0);

            Grid left = new Grid();
            left.RowDefinitions.Add(new RowDefinition());
            left.RowDefinitions.Add(new RowDefinition());
            double usedGb = (double)(_tm.TotalMemory - _tm.AvailableMemory) / 1073741824.0;
            double totalGb = (double)_tm.TotalMemory / 1073741824.0;
            UIElement sw = CompactStatus("SOFTWARE  /  软件", "RUNNING", Accent,
                _tm.ToolMemoryMb.ToString("0"), "MB", "TOOL MEMORY",
                new string[] { "CPU " + _tm.ToolCpuPercent.ToString("0.0") + "%", "线程 " + _tm.ToolThreads, "热键 06", _cfg.EnableOcr ? "OCR READY" : "OCR OFF" },
                "sw");
            left.Children.Add(sw);
            UIElement sys = CompactStatus("SYSTEM  /  系统", "WINDOWS 11", Accent2,
                _tm.MemoryPercent.ToString("0"), "%", "MEMORY USED",
                new string[] { usedGb.ToString("0.0") + " / " + totalGb.ToString("0.0") + " GB", "CPU " + _tm.CpuPercent.ToString("0") + "%", "进程 " + _tm.ProcessCount, "运行 " + _tm.Uptime },
                "sys");
            Grid.SetRow(sys, 1);
            left.Children.Add(sys);
            g.Children.Add(left);

            UIElement game = GamePanel();
            Grid.SetColumn(game, 1);
            g.Children.Add(game);
            return g;
        }

        private UIElement CompactStatus(string title, string badge, Brush color, string value, string unit, string caption, string[] rows, string group)
        {
            Grid cell = new Grid();
            cell.Margin = new Thickness(0, 0, 16, 0);
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel label = new StackPanel();
            label.Orientation = Orientation.Horizontal;
            label.Children.Add(PulseDot(color));
            TextBlock t = MonoText(title, 8.5, Sub);
            t.Margin = new Thickness(6, 0, 0, 0);
            label.Children.Add(t);
            head.Children.Add(label);
            TextBlock b = MonoText(badge, 7.5, color);
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 1);
            head.Children.Add(b);
            cell.Children.Add(head);

            Grid body = new Grid();
            body.Margin = new Thickness(0, 4, 0, 0);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel big = new StackPanel();
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            TextBlock v = DisplayText(value, 30, Ink);
            Tag(v, group == "sw" ? "sw.mem" : "sys.pct");
            v.FontWeight = FontWeights.Bold;
            row.Children.Add(v);
            if (!string.IsNullOrEmpty(unit))
            {
                TextBlock u = MonoText(" " + unit, 8.5, Muted);
                u.VerticalAlignment = VerticalAlignment.Bottom;
                u.Margin = new Thickness(0, 0, 0, 5);
                row.Children.Add(u);
            }
            big.Children.Add(row);
            big.Children.Add(MonoText(caption, 7.5, Muted));
            body.Children.Add(big);
            UniformGrid kv = new UniformGrid();
            kv.Columns = 4;
            string[] keys = group == "sw"
                ? new string[] { "sw.cpu", "sw.threads", "sw.key", "sw.ocr" }
                : new string[] { "sys.mem", "sys.cpu", "sys.proc", "sys.up" };
            for (int i = 0; i < rows.Length; i++)
            {
                TextBlock item = MonoText(rows[i], 8, Sub);
                item.VerticalAlignment = VerticalAlignment.Center;
                if (i < keys.Length) Tag(item, keys[i]);
                kv.Children.Add(item);
            }
            Grid.SetColumn(kv, 1);
            body.Children.Add(kv);
            Grid.SetRow(body, 1);
            cell.Children.Add(body);

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = Line;
            sep.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(sep, 1);
            cell.Children.Add(sep);
            return cell;
        }

        private UIElement GamePanel()
        {
            VigilCard card = new VigilCard();
            card.Fill = _light ? Panel : PanelAlt;
            card.Stroke = Accent2;
            card.Cut = 12;
            card.Padding = new Thickness(18, 14, 18, 12);
            card.Margin = new Thickness(16, 0, 0, 0);

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel label = new StackPanel();
            label.Orientation = Orientation.Horizontal;
            label.Children.Add(PulseDot(Danger));
            TextBlock t = MonoText("GAME  /  游戏状态", 8.5, Sub);
            t.Margin = new Thickness(6, 0, 0, 0);
            label.Children.Add(t);
            head.Children.Add(label);
            TextBlock badge = MonoText(_tm.GameRunning ? "SCPSL.EXE / " + _tm.GamePid : "SCPSL.EXE / --", 7.5, Accent2);
            Grid.SetColumn(badge, 1);
            StackPanel rightHead = new StackPanel();
            rightHead.Orientation = Orientation.Horizontal;
            rightHead.VerticalAlignment = VerticalAlignment.Center;

            StackPanel ble = new StackPanel();
            ble.Orientation = Orientation.Horizontal;
            ble.VerticalAlignment = VerticalAlignment.Center;
            ble.Margin = new Thickness(0, 0, 10, 0);
            ble.Visibility = HeartRateHub.BleConnected ? Visibility.Visible : Visibility.Collapsed;
            Border green = new Border();
            green.Width = 5;
            green.Height = 5;
            green.CornerRadius = new CornerRadius(3);
            green.Background = Solid(58, 200, 120);
            green.VerticalAlignment = VerticalAlignment.Center;
            ble.Children.Add(green);
            TextBlock bleText = MonoText("已连接", 7.5, Solid(58, 200, 120));
            bleText.Margin = new Thickness(5, 0, 0, 0);
            bleText.VerticalAlignment = VerticalAlignment.Center;
            ble.Children.Add(bleText);
            _bleChip = ble;
            _gameBpmText = MonoText("-- BPM", 7.5, Accent2);
            _gameBpmText.Margin = new Thickness(0, 0, 10, 0);
            _gameBpmText.VerticalAlignment = VerticalAlignment.Center;
            _gameBpmText.Visibility = Visibility.Collapsed;
            rightHead.Children.Add(_gameBpmText);
            rightHead.Children.Add(ble);
            rightHead.Children.Add(badge);
            Grid.SetColumn(rightHead, 1);
            head.Children.Add(rightHead);
            g.Children.Add(head);

            Grid bigRow = new Grid();
            bigRow.Margin = new Thickness(0, 2, 0, 2);
            bigRow.ColumnDefinitions.Add(new ColumnDefinition());
            bigRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel big = new StackPanel();
            big.Orientation = Orientation.Horizontal;
            TextBlock v = DisplayText(_tm.GameRunning ? "运行中" : "未启动", 40, Ink);
            Tag(v, "game.state");
            v.FontWeight = FontWeights.Bold;
            big.Children.Add(v);
            TextBlock en = MonoText(_tm.GameRunning ? "  PROCESS / RUNNING" : "  PROCESS / OFFLINE", 9.5, Accent);
            en.VerticalAlignment = VerticalAlignment.Bottom;
            en.Margin = new Thickness(0, 0, 0, 7);
            big.Children.Add(en);
            bigRow.Children.Add(big);
            TextBlock round = MonoText("回合  --", 9, Sub);
            Tag(round, "game.round");
            round.VerticalAlignment = VerticalAlignment.Bottom;
            round.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetColumn(round, 1);
            bigRow.Children.Add(round);
            Grid.SetRow(bigRow, 1);
            g.Children.Add(bigRow);

            Grid rows = new Grid();
            rows.Margin = new Thickness(0, 4, 0, 2);
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 8; i++) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[][] items =
            {
                new[] { "PID", _tm.GameRunning ? _tm.GamePid.ToString() : "--" }, new[] { "内存", _tm.GameRunning ? _tm.MemoryText(_tm.GameMemoryMb) : "--" },
                new[] { "CPU", _tm.GameRunning ? _tm.GameCpuPercent.ToString("0.0") + "%" : "--" }, new[] { "线程", _tm.GameRunning ? _tm.GameThreads.ToString() : "--" },
                new[] { "窗口", _tm.GameRunning ? (_tm.GameForeground ? "前台" : "后台") : "--" }, new[] { "角色", "未识别" },
                new[] { "判定", "待接入" }, new[] { "最后证据", "--" }
            };
            string[] gameKeys = { "game.pid", "game.mem", "game.cpu", "game.threads", "game.window" };
            for (int i = 0; i < items.Length; i++)
            {
                StackPanel item = new StackPanel();
                item.Margin = new Thickness(0, 0, 12, 4);
                item.Children.Add(MonoText(items[i][0], 7.5, Muted));
                TextBlock val = MonoText(items[i][1], 9, Sub);
                if (i == 0) Tag(val, "game.proc");
                else if (i <= 4) Tag(val, gameKeys[i - 1]);
                else if (i == 5) Tag(val, "game.role");
                else if (i == 6) Tag(val, "game.judge");
                else if (i == 7) Tag(val, "game.evidence");
                item.Children.Add(val);
                Grid.SetColumn(item, i % 4);
                Grid.SetRow(item, i / 4);
                rows.Children.Add(item);
            }
            Grid.SetRow(rows, 2);
            g.Children.Add(rows);

            Grid bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            MiniEcg ecg = CreateEcg();
            ecg.ShowDeathLine = true;
            ecg.Height = 32;
            ecg.Margin = new Thickness(0, 2, 10, 0);
            bottom.Children.Add(ecg);
            UIElement action = GameActionButton();
            Grid.SetColumn(action, 1);
            bottom.Children.Add(action);
            Grid.SetRow(bottom, 3);
            g.Children.Add(bottom);

            card.Child = g;
            _hoverTargets.Add(card);
            return card;
        }

        private UIElement GameActionButton()
        {
            Border host = new Border();
            host.CornerRadius = new CornerRadius(4);
            host.BorderThickness = new Thickness(1);
            host.Width = 126;
            host.Height = 34;
            host.Margin = new Thickness(0, 0, 0, 0);
            host.HorizontalAlignment = HorizontalAlignment.Right;
            host.VerticalAlignment = VerticalAlignment.Center;
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            _gameActionFill = new SolidColorBrush();
            _gameActionFore = new SolidColorBrush();
            host.Background = _gameActionFill;
            _gameActionBorder = host;

            Button b = new Button();
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Padding = new Thickness(10, 0, 10, 0);
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = FlatTemplate();
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            TextBlock icon = new TextBlock();
            icon.Text = "\uE768";
            icon.FontFamily = Icons;
            icon.FontSize = 11;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 7, 0);
            icon.Foreground = _gameActionFore;
            _gameActionIcon = icon;
            content.Children.Add(icon);
            TextBlock label = new TextBlock();
            label.Text = "启动游戏";
            label.FontFamily = Ui;
            label.FontSize = 11;
            label.FontWeight = FontWeights.SemiBold;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Foreground = _gameActionFore;
            _gameActionText = label;
            content.Children.Add(label);
            b.Content = content;
            host.PreviewMouseLeftButtonDown += delegate
            {
                _gameActionPress = true;
                host.RenderTransform = new ScaleTransform(0.96, 0.96);
                ApplyGameActionVisual();
            };
            host.PreviewMouseLeftButtonUp += delegate
            {
                _gameActionPress = false;
                host.RenderTransform = Transform.Identity;
                ApplyGameActionVisual();
            };
            host.MouseEnter += delegate
            {
                _gameActionHover = true;
                ApplyGameActionVisual();
            };
            host.MouseLeave += delegate
            {
                _gameActionHover = false;
                _gameActionPress = false;
                host.RenderTransform = Transform.Identity;
                ApplyGameActionVisual();
            };
            b.Click += delegate
            {
                if (_tm.GameRunning)
                {
                    if (_stop != null) _stop();
                    else _tm.KillGame();
                    _tm.Refresh(0);
                    UpdateTelemetry();
                }
                else if (_launch != null)
                {
                    if (_launchBusy) return;
                    SetLaunchBusy(true);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { _launch(); }
                        catch { SetLaunchBusy(false); }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
            host.Child = b;
            UpdateGameAction();
            return host;
        }

        private UIElement SignalPanel()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("SIGNAL CHAIN", 9, Accent));
            TextBlock state = MonoText("4 / 4 LIVE", 8, Accent);
            Grid.SetColumn(state, 1);
            head.Children.Add(state);
            s.Children.Add(head);

            int enabled = 0;
            if (_cfg != null && _cfg.Alerts != null)
                for (int i = 0; i < _cfg.Alerts.Length; i++) if (_cfg.Alerts[i].Style.Enabled) enabled++;
            s.Children.Add(SignalRow("OCR", _cfg.EnableOcr ? "READY" : "OFF", _cfg.EnableOcr ? Accent : Muted, "signal.ocr"));
            s.Children.Add(SignalRow("LOG", _cfg.EnableLog ? "STREAMING" : "OFF", _cfg.EnableLog ? Sub : Muted, "signal.log"));
            s.Children.Add(SignalRow("JUDGE", _tm.GameRunning ? "RUNNING" : "WAITING", _tm.GameRunning ? Accent2 : Muted, "signal.judge"));
            s.Children.Add(SignalRow("ALERT", enabled + " ARMED", Sub, "signal.alert"));
            return s;
        }

        private UIElement SignalRow(string name, string value, Brush color, string key)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 6, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock n = MonoText(name, 8.5, Muted);
            g.Children.Add(n);
            TextBlock v = MonoText(value, 9, color);
            Tag(v, key);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private UIElement StatusTriad()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Margin = new Thickness(0, 8, 0, 0);

            UIElement software = StatusColumn("SOFTWARE  /  软件", "RUNNING", Accent,
                "312", "MB", "TOOL MEMORY",
                new string[][] { new[] { "CPU", "1.1%" }, new[] { "线程", "24" }, new[] { "热键", "06" }, new[] { "OCR", "READY" } });
            g.Children.Add(software);

            UIElement system = StatusColumn("SYSTEM  /  系统", "WINDOWS 11", Accent2,
                "57", "%", "MEMORY USED",
                new string[][] { new[] { "内存", "18.4 / 32.0 GB" }, new[] { "CPU", "18%" }, new[] { "进程", "193" }, new[] { "运行", "3d 04h" } });
            Grid.SetColumn(system, 1);
            g.Children.Add(system);

            UIElement game = StatusColumn("GAME  /  游戏", "SCPSL.EXE", Danger,
                "存活", "", "ALIVE / 86%",
                new string[][] { new[] { "PID", "4821" }, new[] { "内存", "6.8 GB" }, new[] { "CPU", "12.4%" }, new[] { "回合", "03:21" } });
            Grid.SetColumn(game, 2);
            g.Children.Add(game);
            return g;
        }

        private UIElement StatusColumn(string title, string badge, Brush color, string value, string unit, string caption, string[][] rows)
        {
            Grid cell = new Grid();
            cell.Margin = new Thickness(0, 0, 0, 0);
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(16, 0, 16, 0);
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel label = new StackPanel();
            label.Orientation = Orientation.Horizontal;
            label.Children.Add(PulseDot(color));
            TextBlock t = MonoText(title, 8.5, Sub);
            t.Margin = new Thickness(6, 0, 0, 0);
            label.Children.Add(t);
            head.Children.Add(label);
            TextBlock b = MonoText(badge, 7.5, color);
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 1);
            head.Children.Add(b);
            s.Children.Add(head);

            StackPanel big = new StackPanel();
            big.Orientation = Orientation.Horizontal;
            big.Margin = new Thickness(0, 4, 0, 0);
            TextBlock v = DisplayText(value, 38, Ink);
            v.FontWeight = FontWeights.Bold;
            big.Children.Add(v);
            if (!string.IsNullOrEmpty(unit))
            {
                TextBlock u = MonoText(" " + unit, 9, Muted);
                u.VerticalAlignment = VerticalAlignment.Bottom;
                u.Margin = new Thickness(0, 0, 0, 7);
                big.Children.Add(u);
            }
            s.Children.Add(big);
            s.Children.Add(MonoText(caption, 7.5, Muted));

            Grid kv = new Grid();
            kv.Margin = new Thickness(0, 8, 0, 0);
            kv.ColumnDefinitions.Add(new ColumnDefinition());
            kv.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows.Length; i++)
            {
                StackPanel item = new StackPanel();
                item.Margin = new Thickness(0, 0, 12, 4);
                TextBlock k = MonoText(rows[i][0], 7.5, Muted);
                TextBlock vv = MonoText(rows[i][1], 9.5, Sub);
                item.Children.Add(k);
                item.Children.Add(vv);
                Grid.SetColumn(item, i % 2);
                Grid.SetRow(item, i / 2);
                kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                kv.Children.Add(item);
            }
            s.Children.Add(kv);
            cell.Children.Add(s);

            Border sep = new Border();
            sep.Width = 1;
            sep.Background = Line;
            sep.HorizontalAlignment = HorizontalAlignment.Right;
            sep.Margin = new Thickness(0, 4, 0, 4);
            cell.Children.Add(sep);
            return cell;
        }

        private Border PulseDot(Brush color)
        {
            Border dot = new Border();
            dot.Width = 7;
            dot.Height = 7;
            dot.CornerRadius = new CornerRadius(4);
            dot.Background = color;
            dot.VerticalAlignment = VerticalAlignment.Center;
            DoubleAnimation pulse = new DoubleAnimation(0.35, 1.0, new Duration(TimeSpan.FromMilliseconds(1100)));
            pulse.AutoReverse = true;
            pulse.RepeatBehavior = RepeatBehavior.Forever;
            pulse.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            return dot;
        }

        private UIElement MemoryStrip()
        {
            Border host = new Border();
            host.BorderBrush = Line;
            host.BorderThickness = new Thickness(0, 1, 0, 1);
            host.Padding = new Thickness(0, 8, 0, 8);
            host.Margin = new Thickness(0, 6, 0, 0);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            host.Child = g;

            StackPanel left = new StackPanel();
            left.Margin = new Thickness(2, 0, 0, 0);
            left.Children.Add(MonoText("MEMORY", 8.5, Accent));
            StackPanel total = new StackPanel();
            total.Orientation = Orientation.Horizontal;
            double usedGbInit = (double)(_tm.TotalMemory - _tm.AvailableMemory) / 1073741824.0;
            double totalGbInit = (double)_tm.TotalMemory / 1073741824.0;
            TextBlock used = DisplayText(usedGbInit.ToString("0.0"), 22, Ink);
            Tag(used, "mem.used");
            used.FontWeight = FontWeights.Bold;
            total.Children.Add(used);
            TextBlock rest = MonoText(" / " + totalGbInit.ToString("0.0") + " GB", 8.5, Muted);
            Tag(rest, "mem.total");
            rest.VerticalAlignment = VerticalAlignment.Bottom;
            rest.Margin = new Thickness(0, 0, 0, 4);
            total.Children.Add(rest);
            left.Children.Add(total);
            g.Children.Add(left);

            Grid barHost = new Grid();
            barHost.ClipToBounds = true;
            barHost.VerticalAlignment = VerticalAlignment.Center;
            barHost.Margin = new Thickness(0, 0, 20, 0);
            StackBar bar = new StackBar(SystemBar, Accent, ToolBar, Line);
            bar.Height = 16;
            _memBar = bar;
            double usedGb = (double)(_tm.TotalMemory - _tm.AvailableMemory) / 1073741824.0;
            double totalGb = (double)_tm.TotalMemory / 1073741824.0;
            double gameGb = _tm.GameMemoryMb / 1024.0;
            bar.SetValues(Math.Max(0, usedGb - gameGb), gameGb, _tm.ToolMemoryMb / 1024.0, Math.Max(0, totalGb - usedGb));
            barHost.Children.Add(bar);
            Border shimmer = new Border();
            shimmer.Width = 90;
            shimmer.HorizontalAlignment = HorizontalAlignment.Left;
            LinearGradientBrush shine = new LinearGradientBrush();
            shine.StartPoint = new Point(0, 0);
            shine.EndPoint = new Point(1, 0);
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(52, 255, 255, 255), 0.5));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            shimmer.Background = shine;
            TranslateTransform slide = new TranslateTransform(-90, 0);
            shimmer.RenderTransform = slide;
            barHost.Children.Add(shimmer);
            DoubleAnimation move = new DoubleAnimation(-90, 900, new Duration(TimeSpan.FromSeconds(6)));
            move.RepeatBehavior = RepeatBehavior.Forever;
            move.BeginTime = TimeSpan.FromSeconds(2);
            slide.BeginAnimation(TranslateTransform.XProperty, move);
            Grid.SetColumn(barHost, 1);
            g.Children.Add(barHost);

            Grid legend = new Grid();
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            legend.ColumnDefinitions.Add(new ColumnDefinition());
            UIElement l0 = Legend("系统其他", _tm.MemoryText(Math.Max(0, usedGb - gameGb - _tm.ToolMemoryMb / 1024.0) * 1024.0), SystemBar, "mem.sys");
            UIElement l1 = Legend("游戏", _tm.MemoryText(_tm.GameMemoryMb), Accent, "mem.game");
            UIElement l2 = Legend("本工具", _tm.MemoryText(_tm.ToolMemoryMb), ToolBar, "mem.tool");
            UIElement l3 = Legend("空闲", _tm.MemoryText(Math.Max(0, totalGb - usedGb) * 1024.0), Muted, "mem.free");
            legend.Children.Add(l0);
            Grid.SetColumn(l1, 1);
            legend.Children.Add(l1);
            Grid.SetColumn(l2, 2);
            legend.Children.Add(l2);
            Grid.SetColumn(l3, 3);
            legend.Children.Add(l3);
            Grid.SetColumn(legend, 2);
            g.Children.Add(legend);
            return host;
        }

        private UIElement HistoryPanel()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            TextBlock h = MonoText("RESOURCE HISTORY  /  LAST 60 s", 10, Accent);
            h.FontWeight = FontWeights.Bold;
            title.Children.Add(h);
            title.Children.Add(MonoText("最近 60 秒 · 每秒采样 · 左轴 % / 温度 °C · 右轴网络 Mbps", 8.5, Muted));
            head.Children.Add(title);
            TextBlock hint = MonoText("采样 0 / 60 s  ·  悬停看数值  ·  点击图例开关曲线  ·  双击性能", 8.5, Sub);
            Tag(hint, "res.sample");
            hint.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(hint, 1);
            head.Children.Add(hint);
            g.Children.Add(head);

            WrapPanel legend = new WrapPanel();
            legend.Margin = new Thickness(0, 7, 0, 2);
            legend.Children.Add(LegendChip("CPU", Accent2, "res.cpu", "cpu"));
            legend.Children.Add(LegendChip("内存", Accent, "res.mem", "mem"));
            legend.Children.Add(LegendChip("GPU", GpuBrush, "res.gpu", "gpu"));
            legend.Children.Add(LegendChip("网络", NetBrush, "res.net", "net"));
            legend.Children.Add(LegendChip("游戏磁盘", DiskBrush, "res.disk", "disk"));
            _tempLegend = LegendChip("温度", Danger, "res.temp", "temp");
            legend.Children.Add(_tempLegend);
            Grid.SetRow(legend, 1);
            g.Children.Add(legend);

            _history = new HistoryChart(_light, Accent, Accent2, Line, Muted, Ink, Panel, Sub,
                GpuBrush, NetBrush, DiskBrush, Danger, _historyData);
            _history.DiskLabel = _tm.DiskLabel;
            _history.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(_history, 2);
            g.Children.Add(_history);
            return g;
        }

        private StackPanel LegendChip(string text, Brush color, string key, string seriesKey)
        {
            StackPanel s = new StackPanel();
            s.Orientation = Orientation.Horizontal;
            s.Margin = new Thickness(0, 0, 20, 4);
            s.VerticalAlignment = VerticalAlignment.Center;
            s.Cursor = Cursors.Hand;
            s.Tag = "legend";
            s.ToolTip = "点击显示 / 隐藏这条曲线";
            s.MouseLeftButtonUp += delegate { ToggleSeries(seriesKey); };
            Border dot = new Border();
            dot.Width = 13;
            dot.Height = 3;
            dot.CornerRadius = new CornerRadius(2);
            dot.Background = color;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 7, 0);
            s.Children.Add(dot);
            TextBlock t = MonoText(text, 9.5, Sub);
            t.VerticalAlignment = VerticalAlignment.Center;
            s.Children.Add(t);
            TextBlock v = MonoText("--", 10, Ink);
            Tag(v, key);
            v.FontWeight = FontWeights.Bold;
            v.VerticalAlignment = VerticalAlignment.Center;
            v.Margin = new Thickness(6, 0, 0, 0);
            s.Children.Add(v);
            List<FrameworkElement> list;
            if (!_legendChips.TryGetValue(seriesKey, out list))
            {
                list = new List<FrameworkElement>();
                _legendChips[seriesKey] = list;
            }
            list.Add(s);
            return s;
        }

        private UIElement SystemFacts()
        {
            Grid g = new Grid();
            for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels = { "GPU 3D", "NETWORK", "GAME DISK FREE", "DISPLAY", "PROCESSES" };
            string[] values =
            {
                _tm.GpuAvailable ? _tm.GpuPercent.ToString("0") + "%" : "N/A",
                _tm.NetworkAvailable ? _tm.NetworkMbps.ToString("0.00") + " Mbps" : "N/A",
                _tm.DiskLabel + " " + _tm.DiskFreePercent.ToString("0") + "%",
                _tm.RefreshRate + " HZ",
                _tm.ProcessCount.ToString()
            };
            Brush[] colors = { GpuBrush, NetBrush, DiskBrush, Ink, Ink };
            string[] keys = { "fact.gpu", "fact.net", "fact.disk", null, null };
            for (int i = 0; i < 5; i++)
            {
                StackPanel s = new StackPanel();
                s.Margin = new Thickness(i == 0 ? 0 : 16, 0, 0, 0);
                s.Children.Add(MonoText(labels[i], 7.5, Muted));
                TextBlock v = MonoText(values[i], 13, colors[i]);
                v.FontWeight = FontWeights.Bold;
                if (keys[i] != null) Tag(v, keys[i]);
                s.Children.Add(v);
                Grid.SetColumn(s, i);
                g.Children.Add(s);
            }
            return g;
        }

        private VigilCard PerfCard()
        {
            VigilCard c = PanelCard();
            c.Padding = new Thickness(18, 14, 18, 15);
            return c;
        }

        private UIElement BuildPerformancePage()
        {
            StackPanel page = new StackPanel();
            page.Margin = new Thickness(0, 0, 0, 12);

            page.Children.Add(PerformanceHeader());

            UniformGrid kpis = new UniformGrid();
            kpis.Columns = 4;
            kpis.Margin = new Thickness(-5, 0, -5, 10);
            kpis.Children.Add(PerfKpi("GAME AVG FPS", "游戏平均帧率", "perf.fps.avg", "FPS", Accent2, "PresentMon · 近 60 秒"));
            kpis.Children.Add(PerfKpi("1% LOW", "游戏低帧", "perf.fps.low", "FPS", GpuBrush, "PresentMon 帧时间 P99"));
            kpis.Children.Add(PerfKpi("FRAME TIME", "平均帧间隔", "perf.frame.ms", "MS", Accent, "抖动 " + "—"));
            kpis.Children.Add(PerfKpi("DISPLAY", "显示刷新率", "perf.refresh", "HZ", Ink, "帧率上限参考"));
            page.Children.Add(kpis);

            Grid main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(382) });

            StackPanel left = new StackPanel();
            left.Margin = new Thickness(0, 0, 10, 0);
            main.Children.Add(left);

            VigilCard frameCard = PerfCard();
            frameCard.Margin = new Thickness(0, 0, 0, 10);
            frameCard.Child = FramePanel();
            left.Children.Add(frameCard);

            VigilCard historyCard = PerfCard();
            historyCard.Margin = new Thickness(0, 0, 0, 10);
            historyCard.Child = PerfHistoryPanel();
            left.Children.Add(historyCard);

            VigilCard table = PerfCard();
            table.Child = PerfTable();
            left.Children.Add(table);

            VigilCard system = PerfCard();
            system.Margin = new Thickness(0, 10, 0, 0);
            system.Child = SystemDetailPanel();
            left.Children.Add(system);

            StackPanel right = new StackPanel();
            right.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(right, 1);
            main.Children.Add(right);

            VigilCard score = PerfCard();
            score.Margin = new Thickness(0, 0, 0, 10);
            score.Child = ScorePanel();
            right.Children.Add(score);

            VigilCard game = PerfCard();
            game.Margin = new Thickness(0, 0, 0, 10);
            game.Child = PerformanceGamePanel();
            right.Children.Add(game);

            VigilCard storage = PerfCard();
            storage.Margin = new Thickness(0, 0, 0, 10);
            storage.Child = StoragePanel();
            right.Children.Add(storage);

            VigilCard insights = PerfCard();
            insights.Child = InsightPanel();
            right.Children.Add(insights);

            VigilCard records = PerfCard();
            records.Margin = new Thickness(0, 10, 0, 0);
            records.Child = PerformanceRecordsPanel();
            right.Children.Add(records);

            page.Children.Add(main);
            return page;
        }

        private UIElement PerformanceHeader()
        {
            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 0, 12);
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border back = new Border();
            back.Width = 34;
            back.Height = 34;
            back.CornerRadius = new CornerRadius(4);
            back.BorderBrush = Line;
            back.BorderThickness = new Thickness(1);
            back.Background = Panel;
            back.Cursor = Cursors.Hand;
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.VerticalAlignment = VerticalAlignment.Top;
            back.Margin = new Thickness(0, 8, 0, 0);
            Button backButton = new Button();
            backButton.Background = Brushes.Transparent;
            backButton.BorderThickness = new Thickness(0);
            backButton.FocusVisualStyle = null;
            backButton.Cursor = Cursors.Hand;
            backButton.Template = FlatTemplate();
            TextBlock backIcon = new TextBlock();
            backIcon.Text = "\uE72B";
            backIcon.FontFamily = Icons;
            backIcon.FontSize = 15;
            backIcon.Foreground = Sub;
            backIcon.HorizontalAlignment = HorizontalAlignment.Center;
            backIcon.VerticalAlignment = VerticalAlignment.Center;
            backButton.Content = backIcon;
            backButton.MouseEnter += delegate { back.BorderBrush = Accent; backIcon.Foreground = Accent; back.Background = PanelAlt; };
            backButton.MouseLeave += delegate
            {
                back.BorderBrush = Line;
                backIcon.Foreground = Sub;
                back.Background = Panel;
                back.RenderTransform = Transform.Identity;
            };
            backButton.PreviewMouseLeftButtonDown += delegate
            {
                back.Background = AccentSoftBrush();
                backIcon.Foreground = Accent;
                back.RenderTransformOrigin = new Point(0.5, 0.5);
                back.RenderTransform = new ScaleTransform(0.92, 0.92);
            };
            backButton.PreviewMouseLeftButtonUp += delegate { back.RenderTransform = Transform.Identity; };
            backButton.Click += delegate { ShowStatus(); };
            back.Child = backButton;
            head.Children.Add(back);

            OutlineText code = new OutlineText("02", 82, Accent, 1.1);
            code.HorizontalAlignment = HorizontalAlignment.Left;
            code.VerticalAlignment = VerticalAlignment.Top;
            code.Margin = new Thickness(0, -18, 0, 0);
            Grid.SetColumn(code, 1);
            head.Children.Add(code);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 2, 0, 0);
            StackPanel kicker = new StackPanel();
            kicker.Orientation = Orientation.Horizontal;
            kicker.Children.Add(MonoText("PERFORMANCE  /  FRAME & RESOURCE DETAIL", 9, Sub));
            TextBlock backHint = MonoText("   ← 返回状态", 8.5, Muted);
            backHint.VerticalAlignment = VerticalAlignment.Center;
            kicker.Children.Add(backHint);
            title.Children.Add(kicker);
            TextBlock big = DisplayText("性能", 40, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -4, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "游戏帧率（PresentMon）、资源余量、进程细节与自动诊断 · 主框架保持原位";
            desc.FontSize = 11;
            desc.Foreground = Sub;
            title.Children.Add(desc);
            Grid.SetColumn(title, 2);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock live = MonoText("LIVE  /  1.0 s", 8.5, Muted);
            live.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(live);
            TextBlock state = MonoText("PERFORMANCE", 19, Accent);
            state.FontWeight = FontWeights.Bold;
            state.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(state);
            TextBlock hint = MonoText("双击状态页进入  ·  返回图标退出", 8, Sub);
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(hint);
            Button export = CommandButton("\uE74E", "导出游戏性能曲线", delegate { ExportCurrentGamePerformance(); }, Accent);
            export.HorizontalAlignment = HorizontalAlignment.Right;
            export.Margin = new Thickness(0, 5, 0, 0);
            right.Children.Add(export);
            Grid.SetColumn(right, 3);
            head.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 4);
            head.Children.Add(line);
            return head;
        }

        private VigilCard PerfKpi(string title, string caption, string key, string unit, Brush color, string foot)
        {
            VigilCard card = PerfCard();
            card.Margin = new Thickness(5, 0, 5, 0);
            StackPanel s = new StackPanel();
            s.Children.Add(MonoText(title, 8, Muted));
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 1, 0, 0);
            TextBlock value = DisplayText("--", 32, color);
            value.FontWeight = FontWeights.Bold;
            Tag(value, key);
            row.Children.Add(value);
            TextBlock u = MonoText("  " + unit, 8.5, Muted);
            u.VerticalAlignment = VerticalAlignment.Bottom;
            u.Margin = new Thickness(0, 0, 0, 6);
            row.Children.Add(u);
            s.Children.Add(row);
            s.Children.Add(MonoText(caption, 8.5, Sub));
            TextBlock f = MonoText(foot, 7.5, Muted);
            f.Margin = new Thickness(0, 2, 0, 0);
            Tag(f, key + ".foot");
            s.Children.Add(f);
            card.Child = s;
            return card;
        }

        private UIElement FramePanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("GAME FRAME TELEMETRY  /  游戏帧率", "PRESENTMON / SCPSL.EXE"));
            TextBlock note = new TextBlock();
            note.Text = "由 PresentMon 读取 SCPSL.exe 的呈现事件（不注入游戏进程）；检测到游戏进程就开始采集，需要管理员权限。";
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 2, 0, 4);
            s.Children.Add(note);
            _frameChart = new FrameChart(_gameFpsHistory, _tm.RefreshRate, Accent2, Line, Muted, Panel, Ink);
            _frameChart.Height = 148;
            s.Children.Add(_frameChart);
            return s;
        }

        private UIElement PerfHistoryPanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("RESOURCE HISTORY  /  资源历史", "采样 0 / 60 s", "pres.sample"));
            WrapPanel legend = new WrapPanel();
            legend.Margin = new Thickness(0, 5, 0, 2);
            legend.Children.Add(LegendChip("CPU", Accent2, "pres.cpu", "cpu"));
            legend.Children.Add(LegendChip("内存", Accent, "pres.mem", "mem"));
            legend.Children.Add(LegendChip("GPU", GpuBrush, "pres.gpu", "gpu"));
            legend.Children.Add(LegendChip("网络", NetBrush, "pres.net", "net"));
            legend.Children.Add(LegendChip("游戏磁盘", DiskBrush, "pres.disk", "disk"));
            _perfTempLegend = LegendChip("温度", Danger, "pres.temp", "temp");
            legend.Children.Add(_perfTempLegend);
            s.Children.Add(legend);
            _perfChart = new HistoryChart(_light, Accent, Accent2, Line, Muted, Ink, Panel, Sub,
                GpuBrush, NetBrush, DiskBrush, Danger, _historyData);
            _perfChart.DiskLabel = _tm.DiskLabel;
            _perfChart.TooltipFooter = "← 左上角返回图标回到状态页";
            _perfChart.Height = 238;
            _perfChart.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(_perfChart);
            return s;
        }

        private UIElement PerfTable()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("RESOURCE BREAKDOWN  /  资源统计", "AVG / PEAK 60 s"));
            Grid header = PerfColumns();
            header.Margin = new Thickness(0, 8, 0, 2);
            header.Children.Add(PerfCell("资源", Muted, 8, 0));
            header.Children.Add(PerfCell("当前", Muted, 8, 1));
            header.Children.Add(PerfCell("平均", Muted, 8, 2));
            header.Children.Add(PerfCell("峰值", Muted, 8, 3));
            header.Children.Add(PerfCell("状态", Muted, 8, 4));
            s.Children.Add(header);
            s.Children.Add(PerfTableRow("CPU", "cpu", Accent2));
            s.Children.Add(PerfTableRow("内存 / MEM", "mem", Accent));
            s.Children.Add(PerfTableRow("GPU", "gpu", GpuBrush));
            s.Children.Add(PerfTableRow("网络 / NET", "net", NetBrush));
            s.Children.Add(PerfTableRow("游戏磁盘", "disk", DiskBrush));
            _perfTempRow = PerfTableRow("温度 / TEMP", "temp", Danger);
            s.Children.Add(_perfTempRow);
            return s;
        }

        private Grid PerfColumns()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            return g;
        }

        private UIElement PerfCell(string text, Brush color, double size, int col)
        {
            TextBlock t = MonoText(text, size, color);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(t, col);
            return t;
        }

        private UIElement PerfTableRow(string name, string key, Brush color)
        {
            Grid g = PerfColumns();
            g.Margin = new Thickness(0, 5, 0, 5);
            TextBlock n = MonoText(name, 9, Sub);
            n.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(n);
            AddPerfCell(g, "perf.row." + key + ".cur", 1);
            AddPerfCell(g, "perf.row." + key + ".avg", 2);
            AddPerfCell(g, "perf.row." + key + ".peak", 3);
            TextBlock state = MonoText("--", 9, color);
            Tag(state, "perf.row." + key + ".state");
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 4);
            g.Children.Add(state);
            return g;
        }

        private void AddPerfCell(Grid g, string key, int col)
        {
            TextBlock t = MonoText("--", 9, Ink);
            Tag(t, key);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        private UIElement ScorePanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("PERFORMANCE SCORE  /  性能评分", "LIVE"));
            Grid hero = new Grid();
            hero.Margin = new Thickness(0, 4, 0, 2);
            hero.ColumnDefinitions.Add(new ColumnDefinition());
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel();
            StackPanel valueRow = new StackPanel();
            valueRow.Orientation = Orientation.Horizontal;
            TextBlock score = DisplayText("--", 50, Accent);
            score.FontWeight = FontWeights.Bold;
            Tag(score, "perf.score");
            valueRow.Children.Add(score);
            TextBlock max = MonoText(" / 100", 9, Muted);
            max.VerticalAlignment = VerticalAlignment.Bottom;
            max.Margin = new Thickness(0, 0, 0, 8);
            valueRow.Children.Add(max);
            left.Children.Add(valueRow);
            TextBlock verdict = MonoText("正在采样", 10, Sub);
            Tag(verdict, "perf.verdict");
            left.Children.Add(verdict);
            hero.Children.Add(left);

            Border gradeBox = new Border();
            gradeBox.Width = 52;
            gradeBox.Height = 52;
            gradeBox.CornerRadius = new CornerRadius(26);
            gradeBox.BorderBrush = Accent;
            gradeBox.BorderThickness = new Thickness(1);
            gradeBox.VerticalAlignment = VerticalAlignment.Center;
            TextBlock grade = DisplayText("--", 26, Accent);
            grade.FontWeight = FontWeights.Bold;
            grade.HorizontalAlignment = HorizontalAlignment.Center;
            grade.VerticalAlignment = VerticalAlignment.Center;
            Tag(grade, "perf.grade");
            gradeBox.Child = grade;
            Grid.SetColumn(gradeBox, 1);
            hero.Children.Add(gradeBox);
            s.Children.Add(hero);

            StackPanel meters = new StackPanel();
            meters.Margin = new Thickness(0, 4, 0, 0);
            meters.Children.Add(PerfMeterRow("CPU 余量", "perf.margin.cpu", Accent2, 0));
            meters.Children.Add(PerfMeterRow("GPU 余量", "perf.margin.gpu", GpuBrush, 1));
            meters.Children.Add(PerfMeterRow("内存余量", "perf.margin.mem", Accent, 2));
            meters.Children.Add(PerfMeterRow("磁盘余量", "perf.margin.disk", DiskBrush, 3));
            s.Children.Add(meters);

            TextBlock note = new TextBlock();
            note.Text = "评分为 CPU / GPU / 内存余量与磁盘繁忙度的加权结果。";
            note.FontSize = 8;
            note.Foreground = Muted;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(note);
            return s;
        }

        private UIElement PerfMeterRow(string label, string key, Brush color, int index)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 5, 0, 5);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            TextBlock l = MonoText(label, 8.5, Sub);
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            MeterBar bar = new MeterBar(color, Line);
            bar.Margin = new Thickness(0, 0, 10, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            while (_marginBars.Count <= index) _marginBars.Add(null);
            _marginBars[index] = bar;
            Grid.SetColumn(bar, 1);
            g.Children.Add(bar);
            TextBlock v = MonoText("--", 8.5, color);
            v.VerticalAlignment = VerticalAlignment.Center;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Tag(v, key);
            Grid.SetColumn(v, 2);
            g.Children.Add(v);
            return g;
        }

        private UIElement PerformanceRecordsPanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("GAME REPORTS  /  性能记录", "AUTO ≤ 3  ·  MANUAL 永久"));
            TextBlock note = new TextBlock();
            note.Text = "自动记录只保留最近 3 局（进入过服务器且游戏时长 > 3 分钟）；手动导出的记录永久保留，可在这里删除。";
            note.FontSize = 8;
            note.Foreground = Muted;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 2, 0, 5);
            s.Children.Add(note);

            _perfRecordsHost = new StackPanel();
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.MaxHeight = 240;
            sv.Content = _perfRecordsHost;
            s.Children.Add(sv);
            return s;
        }

        private void RefreshPerformanceRecords()
        {
            if (_perfRecordsHost == null) return;
            PruneAutoPerformance(3);
            _perfRecordsHost.Children.Clear();
            List<string> files = new List<string>();
            try
            {
                string dir = PerformanceReportDir();
                files.AddRange(Directory.GetFiles(dir, "自动_*.png"));
                files.AddRange(Directory.GetFiles(dir, "手动_*.png"));
            }
            catch { }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            files.Reverse();
            if (files.Count == 0)
            {
                TextBlock empty = MonoText("还没有记录。进入游戏并跑满 3 分钟后会自动保存。", 8.5, Muted);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.Margin = new Thickness(0, 4, 0, 4);
                _perfRecordsHost.Children.Add(empty);
                return;
            }
            for (int i = 0; i < files.Count; i++)
            {
                string png = files[i];
                string basePath = png.Substring(0, png.Length - 4);
                string type = System.IO.Path.GetFileName(png).StartsWith("自动_") ? "自动" : "手动";
                string start = "", duration = "0", avg = "--", samples = "0";
                try
                {
                    string[] meta = File.ReadAllLines(basePath + ".meta", Encoding.UTF8);
                    for (int k = 0; k < meta.Length; k++)
                    {
                        int eq = meta[k].IndexOf('=');
                        if (eq <= 0) continue;
                        string key = meta[k].Substring(0, eq);
                        string val = meta[k].Substring(eq + 1);
                        if (key == "start") start = val;
                        else if (key == "duration") duration = val;
                        else if (key == "avgfps") avg = val;
                        else if (key == "samples") samples = val;
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(start)) start = System.IO.Path.GetFileNameWithoutExtension(png);
                double durSec = 0;
                double.TryParse(duration, out durSec);

                Grid row = new Grid();
                row.Margin = new Thickness(0, 0, 0, 5);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

                TextBlock kind = MonoText(type, 7.5, type == "自动" ? Accent2 : Accent);
                kind.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(kind);

                StackPanel info = new StackPanel();
                info.Children.Add(MonoText(start, 8.5, Sub));
                info.Children.Add(MonoText(ClockText(durSec) + "  ·  " + avg + " FPS  ·  " + samples + " 点", 7.5, Muted));
                Grid.SetColumn(info, 1);
                row.Children.Add(info);

                TextBlock size = MonoText(FormatFileSize(png), 7.5, Muted);
                size.VerticalAlignment = VerticalAlignment.Center;
                size.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(size, 2);
                row.Children.Add(size);

                Button del = CommandButton("\uE74D", "删除", delegate { DeletePerformanceRecord(basePath); }, Danger);
                del.Height = 22;
                del.Padding = new Thickness(6, 0, 6, 0);
                del.Margin = new Thickness(0);
                del.FontSize = 9;
                Grid.SetColumn(del, 4);
                row.Children.Add(del);

                _perfRecordsHost.Children.Add(row);
            }
        }

        private void DeletePerformanceRecord(string basePath)
        {
            try
            {
                try { File.Delete(basePath + ".png"); } catch { }
                try { File.Delete(basePath + ".txt"); } catch { }
                try { File.Delete(basePath + ".meta"); } catch { }
                if (_lastPerfSavedPath != null
                    && string.Equals(_lastPerfSavedPath, basePath + ".png", StringComparison.OrdinalIgnoreCase))
                {
                    _lastPerfSavedPath = null;
                    UpdateGamePerfNotice();
                }
                RefreshPerformanceRecords();
                _gameFpsStatus = "已删除性能记录";
                UpdatePerformance();
            }
            catch (Exception ex)
            {
                Logger.Err("性能", "删除性能记录失败", ex);
                _gameFpsStatus = "删除失败：" + ex.Message;
                UpdatePerformance();
            }
        }

        private static string FormatFileSize(string path)
        {
            try
            {
                long len = new FileInfo(path).Length;
                if (len >= 1048576) return (len / 1048576.0).ToString("0.0") + " MB";
                return (len / 1024.0).ToString("0") + " KB";
            }
            catch { return "--"; }
        }

        private UIElement PerformanceGamePanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("GAME PROCESS  /  游戏进程", "LIVE / PID"));
            s.Children.Add(PerfRow("状态", "perf.game.state", "--", true));
            s.Children.Add(PerfRow("PID", "perf.game.pid", "--", false));
            s.Children.Add(PerfRow("CPU", "perf.game.cpu", "--", false));
            s.Children.Add(PerfRow("GPU", "perf.game.gpu", "--", false));
            s.Children.Add(PerfRow("内存", "perf.game.mem", "--", false));
            s.Children.Add(PerfRow("线程", "perf.game.threads", "--", false));
            s.Children.Add(PerfRow("窗口", "perf.game.window", "--", false));
            s.Children.Add(PerfRow("回合", "perf.game.round", "--", false));
            s.Children.Add(PerfRow("所在磁盘", "perf.game.disk", "--", false));
            return s;
        }

        private UIElement StoragePanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("STORAGE & NETWORK  /  存储与网络", "GAME DISK"));
            s.Children.Add(PerfRow("磁盘卷", "perf.disk.volume", "--", false));
            s.Children.Add(PerfRow("已用", "perf.disk.used", "--", false));
            s.Children.Add(PerfRow("读写", "perf.disk.io", "--", false));
            s.Children.Add(PerfRow("磁盘繁忙", "perf.disk.busy", "--", false));
            Border sep = new Border();
            sep.Height = 1;
            sep.Background = Line;
            sep.Margin = new Thickness(0, 7, 0, 7);
            s.Children.Add(sep);
            s.Children.Add(PerfRow("下行", "perf.net.down", "--", false));
            s.Children.Add(PerfRow("上行", "perf.net.up", "--", false));
            s.Children.Add(PerfRow("链路", "perf.net.link", "--", false));
            s.Children.Add(PerfRow("网络状态", "perf.net.state", "--", false));
            return s;
        }

        private UIElement SystemDetailPanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("SYSTEM DETAIL  /  系统详情", "RUNTIME"));
            s.Children.Add(PerfRow("操作系统", "perf.sys.os", "--", false));
            s.Children.Add(PerfRow("逻辑核心", "perf.sys.cores", "--", false));
            s.Children.Add(PerfRow("物理内存", "perf.sys.ram", "--", false));
            s.Children.Add(PerfRow("显示刷新", "perf.sys.display", "--", false));
            s.Children.Add(PerfRow("进程总数", "perf.sys.proc", "--", false));
            s.Children.Add(PerfRow("系统运行", "perf.sys.up", "--", false));
            s.Children.Add(PerfRow("GPU 计数器", "perf.sys.gpu", "--", false));
            s.Children.Add(PerfRow("网络链路", "perf.sys.net", "--", false));
            s.Children.Add(PerfRow("温度传感器", "perf.sys.temp", "--", false));
            return s;
        }

        private UIElement PerfRow(string label, string key, string initial, bool mono)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 3, 0, 3);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 9.5;
            l.Foreground = Sub;
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            TextBlock v = mono ? MonoText(initial, 9.5, Ink) : MonoText(initial, 9, Sub);
            Tag(v, key);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            v.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private UIElement InsightPanel()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(PerfHeader("DIAGNOSIS  /  自动诊断", "WINDOW 60 s"));
            _insightDots.Clear();
            _insightTexts.Clear();
            for (int i = 0; i < 5; i++)
            {
                Grid row = new Grid();
                row.Margin = new Thickness(0, 5, 0, 5);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                Border dot = new Border();
                dot.Width = 3;
                dot.Height = 22;
                dot.CornerRadius = new CornerRadius(2);
                dot.Background = Line;
                dot.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(dot);
                TextBlock text = new TextBlock();
                text.Text = "正在采集数据…";
                text.FontSize = 9;
                text.Foreground = Muted;
                text.TextWrapping = TextWrapping.Wrap;
                text.Margin = new Thickness(9, 0, 0, 0);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                _insightDots.Add(dot);
                _insightTexts.Add(text);
                s.Children.Add(row);
            }
            return s;
        }

        private UIElement PerfHeader(string title, string code, string codeKey = null)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock t = MonoText(title, 9, Accent);
            t.FontWeight = FontWeights.Bold;
            g.Children.Add(t);
            TextBlock c = MonoText(code, 7.5, Muted);
            c.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrEmpty(codeKey)) Tag(c, codeKey);
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
            return g;
        }

        private void SetPerfState(string key, string text, Brush color)
        {
            TextBlock t;
            if (!_tagged.TryGetValue("t:" + key, out t)) return;
            t.Text = text;
            t.Foreground = color;
        }

        private static string FormatMbps(double value)
        {
            if (value >= 100) return value.ToString("0");
            if (value >= 10) return value.ToString("0.0");
            return value.ToString("0.00");
        }

        private static string FormatPct(double value)
        {
            return value.ToString("0.0") + "%";
        }

        private void UpdatePerformance()
        {
            if (!_perfBuilt) return;
            double cpuAvg = ResourceHistory.Average(_historyData.Cpu);
            double cpuPeak = ResourceHistory.Peak(_historyData.Cpu);
            double memAvg = ResourceHistory.Average(_historyData.Memory);
            double memPeak = ResourceHistory.Peak(_historyData.Memory);
            double gpuAvg = ResourceHistory.Average(_historyData.Gpu);
            double gpuPeak = ResourceHistory.Peak(_historyData.Gpu);
            double netAvg = ResourceHistory.Average(_historyData.Network);
            double netPeak = ResourceHistory.Peak(_historyData.Network);
            double diskAvg = ResourceHistory.Average(_historyData.Disk);
            double diskPeak = ResourceHistory.Peak(_historyData.Disk);
            double tempAvg = ResourceHistory.Average(_historyData.Temperature);
            double tempPeak = ResourceHistory.Peak(_historyData.Temperature);

            SetText("perf.fps.avg", _gameFpsAvg > 0 ? _gameFpsAvg.ToString("0") : "--");
            SetText("perf.fps.low", _gameFpsLow > 0 ? _gameFpsLow.ToString("0") : "--");
            SetText("perf.frame.ms", _gameFrameMs > 0 ? _gameFrameMs.ToString("0.0") : "--");
            SetText("perf.refresh", _tm.RefreshRate.ToString());
            SetText("perf.fps.avg.foot", string.IsNullOrEmpty(_gameFpsStatus) ? "PresentMon" : _gameFpsStatus);
            SetText("perf.frame.ms.foot", "抖动 " + (_gameJitterMs > 0 ? _gameJitterMs.ToString("0.0") + " ms" : "—"));

            double cpuHead = 100 - cpuAvg;
            double gpuHead = _tm.GpuAvailable ? 100 - gpuAvg : 0;
            double memHead = 100 - memAvg;
            double diskHead = 100 - _tm.DiskBusyPercent;
            double score;
            if (_tm.GpuAvailable)
                score = cpuHead * 0.30 + gpuHead * 0.35 + memHead * 0.20 + diskHead * 0.15;
            else
                score = cpuHead * 0.48 + memHead * 0.35 + diskHead * 0.17;
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            string grade = score >= 90 ? "S" : score >= 80 ? "A" : score >= 70 ? "B" : score >= 60 ? "C" : "D";
            string verdict = score >= 90 ? "余量充裕 / 高帧率无忧" : score >= 80 ? "整体流畅" : score >= 70 ? "表现良好" : score >= 60 ? "存在压力" : "瓶颈明显";
            Brush verdictColor = score >= 80 ? Accent : score >= 65 ? Accent2 : score >= 50 ? GpuBrush : Danger;
            SetText("perf.score", score.ToString("0"));
            SetText("perf.grade", grade);
            SetPerfState("perf.verdict", verdict, verdictColor);
            TextBlock scoreText;
            if (_tagged.TryGetValue("t:perf.score", out scoreText)) scoreText.Foreground = verdictColor;
            TextBlock gradeText;
            if (_tagged.TryGetValue("t:perf.grade", out gradeText)) gradeText.Foreground = verdictColor;

            if (_marginBars.Count >= 4)
            {
                if (_marginBars[0] != null) _marginBars[0].Ratio = cpuHead / 100.0;
                if (_marginBars[1] != null) _marginBars[1].Ratio = _tm.GpuAvailable ? gpuHead / 100.0 : 0;
                if (_marginBars[2] != null) _marginBars[2].Ratio = memHead / 100.0;
                if (_marginBars[3] != null) _marginBars[3].Ratio = diskHead / 100.0;
            }
            SetText("perf.margin.cpu", cpuHead.ToString("0") + "%");
            SetText("perf.margin.gpu", _tm.GpuAvailable ? gpuHead.ToString("0") + "%" : "N/A");
            SetText("perf.margin.mem", memHead.ToString("0") + "%");
            SetText("perf.margin.disk", diskHead.ToString("0") + "%");

            SetText("perf.game.state", _tm.GameRunning ? "运行中" : "未启动");
            SetText("perf.game.pid", _tm.GameRunning ? _tm.GamePid.ToString() : "--");
            SetText("perf.game.cpu", _tm.GameRunning ? _tm.GameCpuPercent.ToString("0.0") + "%" : "--");
            SetText("perf.game.gpu", _tm.GameRunning && _tm.GpuAvailable ? _tm.GameGpuPercent.ToString("0.0") + "%" : "--");
            SetText("perf.game.mem", _tm.GameRunning ? _tm.MemoryText(_tm.GameMemoryMb) : "--");
            SetText("perf.game.threads", _tm.GameRunning ? _tm.GameThreads.ToString() : "--");
            SetText("perf.game.window", _tm.GameRunning ? (_tm.GameForeground ? "前台" : "后台") : "--");
            SetText("perf.game.disk", _tm.DiskLabel);
            VigilSnapshot snap = _snapshot != null ? _snapshot() : null;
            if (snap != null && snap.RoundStart != DateTime.MinValue)
            {
                TimeSpan el = DateTime.Now - snap.RoundStart;
                if (el.TotalSeconds < 0) el = TimeSpan.Zero;
                SetText("perf.game.round", ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00"));
            }
            else SetText("perf.game.round", "--");

            SetText("perf.disk.volume", _tm.DiskLabel + "  /  " + _tm.DiskRoot);
            SetText("perf.disk.used", _tm.DiskUsedPercent.ToString("0.0") + "%  ·  " + _tm.DiskFreeGb.ToString("0.0") + " GB 可用");
            SetText("perf.disk.io", _tm.DiskReadMBps.ToString("0.00") + " / " + _tm.DiskWriteMBps.ToString("0.00") + " MB/s");
            SetText("perf.disk.busy", _tm.DiskCounterAvailable ? _tm.DiskBusyPercent.ToString("0.0") + "%" : "N/A");
            SetText("perf.net.down", _tm.NetworkAvailable ? _tm.NetworkDownMbps.ToString("0.00") + " Mbps" : "N/A");
            SetText("perf.net.up", _tm.NetworkAvailable ? _tm.NetworkUpMbps.ToString("0.00") + " Mbps" : "N/A");
            SetText("perf.net.link", _tm.NetworkLinkMbps > 0 ? FormatMbps(_tm.NetworkLinkMbps) + " Mbps" : "N/A");
            SetText("perf.net.state", _tm.NetworkAvailable ? "在线" : "不可用");

            SetText("perf.sys.os", _tm.WindowsName);
            SetText("perf.sys.cores", Environment.ProcessorCount + " 逻辑核心");
            SetText("perf.sys.ram", (_tm.TotalMemory / 1073741824.0).ToString("0.0") + " GB");
            SetText("perf.sys.display", _tm.RefreshRate + " Hz");
            SetText("perf.sys.proc", _tm.ProcessCount + " 个");
            SetText("perf.sys.up", _tm.Uptime);
            SetText("perf.sys.gpu", _tm.GpuAvailable ? "可用  /  GPU Engine 3D" : "不可用");
            SetText("perf.sys.net", _tm.NetworkAvailable ? "在线  /  " + FormatMbps(_tm.NetworkLinkMbps) + " Mbps" : "不可用");
            SetText("perf.sys.temp", _tm.TemperatureAvailable ? "ACPI  /  " + _tm.TemperatureCelsius.ToString("0.0") + " °C" : "不可用");

            SetText("perf.row.cpu.cur", FormatPct(_tm.CpuPercent));
            SetText("perf.row.cpu.avg", FormatPct(cpuAvg));
            SetText("perf.row.cpu.peak", FormatPct(cpuPeak));
            SetText("perf.row.mem.cur", FormatPct(_tm.MemoryPercent));
            SetText("perf.row.mem.avg", FormatPct(memAvg));
            SetText("perf.row.mem.peak", FormatPct(memPeak));
            SetText("perf.row.gpu.cur", _tm.GpuAvailable ? FormatPct(_tm.GpuPercent) : "N/A");
            SetText("perf.row.gpu.avg", _tm.GpuAvailable ? FormatPct(gpuAvg) : "N/A");
            SetText("perf.row.gpu.peak", _tm.GpuAvailable ? FormatPct(gpuPeak) : "N/A");
            SetText("perf.row.net.cur", _tm.NetworkAvailable ? FormatMbps(_tm.NetworkMbps) + " Mbps" : "N/A");
            SetText("perf.row.net.avg", _tm.NetworkAvailable ? FormatMbps(netAvg) + " Mbps" : "N/A");
            SetText("perf.row.net.peak", _tm.NetworkAvailable ? FormatMbps(netPeak) + " Mbps" : "N/A");
            SetText("perf.row.disk.cur", _tm.DiskLabel + " " + FormatPct(_tm.DiskUsedPercent));
            SetText("perf.row.disk.avg", _tm.DiskLabel + " " + FormatPct(diskAvg));
            SetText("perf.row.disk.peak", _tm.DiskLabel + " " + FormatPct(diskPeak));
            SetText("perf.row.temp.cur", _tm.TemperatureAvailable ? _tm.TemperatureCelsius.ToString("0.0") + " °C" : "N/A");
            SetText("perf.row.temp.avg", _tm.TemperatureAvailable ? tempAvg.ToString("0.0") + " °C" : "N/A");
            SetText("perf.row.temp.peak", _tm.TemperatureAvailable ? tempPeak.ToString("0.0") + " °C" : "N/A");
            SetPerfState("perf.row.temp.state", _tm.TemperatureAvailable ? TempState(_tm.TemperatureCelsius) : "不可用",
                _tm.TemperatureAvailable ? StateColor(_tm.TemperatureCelsius, 70, 85) : Muted);

            SetPerfState("perf.row.cpu.state", CpuState(cpuAvg), StateColor(cpuAvg, 55, 80));
            SetPerfState("perf.row.mem.state", MemState(_tm.MemoryPercent), StateColor(_tm.MemoryPercent, 70, 88));
            SetPerfState("perf.row.gpu.state", _tm.GpuAvailable ? GpuState(gpuAvg) : "不可用", _tm.GpuAvailable ? StateColor(gpuAvg, 70, 88) : Muted);
            SetPerfState("perf.row.net.state", _tm.NetworkAvailable ? "在线" : "不可用", _tm.NetworkAvailable ? Accent : Muted);
            SetPerfState("perf.row.disk.state", DiskState(_tm.DiskUsedPercent), _tm.DiskUsedPercent >= 92 ? Danger : _tm.DiskUsedPercent >= 82 ? GpuBrush : Accent);

            UpdateInsights(cpuAvg, cpuPeak, gpuAvg, gpuPeak, netAvg, netPeak, score, grade, verdict);
        }

        private static string CpuState(double value)
        {
            if (value < 55) return "良好";
            if (value < 80) return "偏高";
            return "拥堵";
        }

        private static string MemState(double value)
        {
            if (value < 70) return "充足";
            if (value < 88) return "偏高";
            return "紧张";
        }

        private static string GpuState(double value)
        {
            if (value < 70) return "充足";
            if (value < 88) return "偏高";
            return "满载";
        }

        private static string DiskState(double value)
        {
            if (value < 82) return "空间充足";
            if (value < 92) return "空间偏低";
            return "空间告急";
        }

        private static string TempState(double value)
        {
            if (value < 70) return "正常";
            if (value < 85) return "偏高";
            return "过热";
        }

        private Brush StateColor(double value, double warn, double danger)
        {
            if (value >= danger) return Danger;
            if (value >= warn) return GpuBrush;
            return Accent;
        }

        private void UpdateInsights(double cpuAvg, double cpuPeak, double gpuAvg, double gpuPeak,
            double netAvg, double netPeak, double score, string grade, string verdict)
        {
            string[] lines = new string[5];
            Brush[] colors = new Brush[5];
            lines[0] = "综合 " + score.ToString("0") + " 分（" + grade + "） · " + verdict + "；CPU 平均 "
                + cpuAvg.ToString("0") + "%，峰值 " + cpuPeak.ToString("0") + "%"
                + (_tm.TemperatureAvailable ? "；温度 " + _tm.TemperatureCelsius.ToString("0.0") + " °C。" : "。");
            colors[0] = score >= 80 ? Accent : score >= 60 ? GpuBrush : Danger;

            if (_tm.GpuAvailable)
            {
                lines[1] = "GPU 平均 " + gpuAvg.ToString("0") + "%，峰值 " + gpuPeak.ToString("0")
                    + "%；" + (gpuAvg < 70 ? "显卡余量充足。" : gpuAvg < 88 ? "显卡压力偏高。" : "显卡接近满载，可能成为瓶颈。");
                colors[1] = gpuAvg < 70 ? Accent : gpuAvg < 88 ? GpuBrush : Danger;
            }
            else
            {
                lines[1] = "GPU 计数器不可用；请在 Windows 10/11 上以管理员身份运行以获取显卡占用。";
                colors[1] = Muted;
            }

            if (_tm.GameRunning)
            {
                lines[2] = "SCPSL 进程在线（PID " + _tm.GamePid + "），CPU " + _tm.GameCpuPercent.ToString("0.0")
                    + "%，内存 " + _tm.MemoryText(_tm.GameMemoryMb) + (_tm.GameForeground ? "，窗口在前台。" : "，窗口在后台。");
                colors[2] = Accent;
            }
            else
            {
                lines[2] = "未检测到 SCPSL 进程；启动游戏后此处会显示 PID、占用与回合数据。";
                colors[2] = Sub;
            }

            lines[3] = "游戏磁盘 " + _tm.DiskLabel + " 已用 " + _tm.DiskUsedPercent.ToString("0.0") + "%，剩余 "
                + _tm.DiskFreeGb.ToString("0.0") + " GB；当前繁忙 " + _tm.DiskBusyPercent.ToString("0.0")
                + "%，读 " + _tm.DiskReadMBps.ToString("0.00") + " / 写 " + _tm.DiskWriteMBps.ToString("0.00") + " MB/s。";
            colors[3] = _tm.DiskUsedPercent >= 92 ? Danger : _tm.DiskUsedPercent >= 82 ? GpuBrush : Accent;

            lines[4] = "网络 下行 " + _tm.NetworkDownMbps.ToString("0.00") + " / 上行 " + _tm.NetworkUpMbps.ToString("0.00")
                + " Mbps（60 s 峰值 " + netPeak.ToString("0.00") + "）；游戏帧率 " + (_gameFpsAvg > 0 ? _gameFpsAvg.ToString("0") : "--")
                + " FPS，1% low " + (_gameFpsLow > 0 ? _gameFpsLow.ToString("0") : "--") + " FPS"
                + (_gameFpsAvg > 0 && _gameFpsAvg < _tm.RefreshRate * 0.85 ? "，低于显示刷新率。" : "，渲染稳定。");
            colors[4] = _gameFpsAvg > 0 && _gameFpsAvg < _tm.RefreshRate * 0.85 ? GpuBrush : Sub;

            for (int i = 0; i < _insightTexts.Count && i < lines.Length; i++)
            {
                _insightTexts[i].Text = lines[i];
                _insightTexts[i].Foreground = colors[i];
                _insightDots[i].Background = colors[i];
            }
        }

        private UIElement BottomRow()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            g.Margin = new Thickness(0, 8, 0, 0);

            VigilCard tape = PanelCard();
            tape.Margin = new Thickness(0, 0, 10, 0);
            tape.Child = SystemFacts();
            g.Children.Add(tape);

            VigilCard signal = PanelCard();
            signal.Margin = new Thickness(10, 0, 0, 0);
            signal.Child = SignalPanel();
            Grid.SetColumn(signal, 1);
            g.Children.Add(signal);
            return g;
        }

        private UIElement EvidenceTape()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("EVIDENCE STREAM", 9, Accent));
            TextBlock last = MonoText("LAST 04", 8, Muted);
            Grid.SetColumn(last, 1);
            head.Children.Add(last);
            s.Children.Add(head);
            Grid items = new Grid();
            items.Margin = new Thickness(0, 6, 0, 0);
            for (int i = 0; i < 4; i++) items.ColumnDefinitions.Add(new ColumnDefinition());
            UIElement e0 = EvidenceItem("00:02", "OCR", "HUD 生命体征识别为存活", Accent);
            UIElement e1 = EvidenceItem("00:14", "LOG", "未检测到回合结束事件", Sub);
            UIElement e2 = EvidenceItem("00:21", "OCR", "票数扫描 37 / 40", Accent2);
            UIElement e3 = EvidenceItem("00:38", "SYS", "配置自动保存完成", Muted);
            items.Children.Add(e0);
            Grid.SetColumn(e1, 1);
            items.Children.Add(e1);
            Grid.SetColumn(e2, 2);
            items.Children.Add(e2);
            Grid.SetColumn(e3, 3);
            items.Children.Add(e3);
            s.Children.Add(items);
            return s;
        }

        private UIElement EvidenceItem(string time, string tag, string text, Brush color)
        {
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(0, 0, 12, 0);
            StackPanel top = new StackPanel();
            top.Orientation = Orientation.Horizontal;
            TextBlock t = MonoText(time, 13, color);
            t.FontWeight = FontWeights.Bold;
            top.Children.Add(t);
            TextBlock tg = MonoText("  " + tag, 7.5, Muted);
            tg.VerticalAlignment = VerticalAlignment.Bottom;
            top.Children.Add(tg);
            s.Children.Add(top);
            TextBlock tx = new TextBlock();
            tx.Text = text;
            tx.FontSize = 9.5;
            tx.Foreground = Sub;
            tx.TextWrapping = TextWrapping.Wrap;
            tx.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(tx);
            return s;
        }

        private MiniEcg CreateEcg()
        {
            MiniEcg ecg = new MiniEcg(_light, Accent, Line, Danger);
            ecg.PixelsPerSecond = _cfg.HeartRateEcgSpeed;
            ecg.AmplitudeScale = _cfg.HeartRateEcgAmplitude / 100.0;
            _ecgControls.Add(ecg);
            return ecg;
        }

        private void ApplyEcgSettings()
        {
            for (int i = 0; i < _ecgControls.Count; i++)
            {
                MiniEcg ecg = _ecgControls[i];
                if (ecg == null) continue;
                ecg.PixelsPerSecond = _cfg.HeartRateEcgSpeed;
                ecg.AmplitudeScale = _cfg.HeartRateEcgAmplitude / 100.0;
            }
        }

        private UIElement GameSignal()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("GAME SIGNAL", 9, Accent));
            TextBlock state = MonoText("ALIVE 86%", 8, Accent);
            Grid.SetColumn(state, 1);
            head.Children.Add(state);
            s.Children.Add(head);

            MiniEcg ecg = CreateEcg();
            ecg.Height = 42;
            ecg.Margin = new Thickness(0, 4, 0, 4);
            s.Children.Add(ecg);

            Grid indicators = new Grid();
            indicators.ColumnDefinitions.Add(new ColumnDefinition());
            indicators.ColumnDefinitions.Add(new ColumnDefinition());
            indicators.ColumnDefinitions.Add(new ColumnDefinition());
            indicators.Children.Add(Indicator("OCR", "READY", Accent));
            Grid.SetColumn(Indicator("LOG", "STREAM", Sub), 1);
            Grid.SetColumn(Indicator("JUDGE", "ALIVE", Accent2), 2);
            s.Children.Add(indicators);
            return s;
        }

        private UIElement Indicator(string name, string value, Brush color)
        {
            StackPanel s = new StackPanel();
            TextBlock n = MonoText(name, 7.5, Muted);
            TextBlock v = MonoText(value, 8.5, color);
            s.Children.Add(n);
            s.Children.Add(v);
            return s;
        }

        private UIElement Judge()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("GAME JUDGE", 9, Accent));
            TextBlock live = MonoText("LIVE / 86%", 8.5, Sub);
            Grid.SetColumn(live, 1);
            head.Children.Add(live);
            s.Children.Add(head);

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 2, 0, 0);
            TextBlock big = DisplayText("存活", 38, Ink);
            big.FontWeight = FontWeights.Bold;
            row.Children.Add(big);
            TextBlock en = MonoText("  ALIVE", 10, Accent);
            en.VerticalAlignment = VerticalAlignment.Bottom;
            en.Margin = new Thickness(0, 0, 0, 6);
            row.Children.Add(en);
            s.Children.Add(row);

            s.Children.Add(Row("游戏进程", "SCPSL.exe / 4821", Accent));
            s.Children.Add(Row("窗口", "前台 / 2560×1440", Sub));
            s.Children.Add(Row("回合", "进行中 03:21", Sub));
            s.Children.Add(Row("角色", "未识别", Muted));
            s.Children.Add(Row("最后证据", "OCR 00:02 前", Sub));

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.Margin = new Thickness(0, 8, 0, 6);
            s.Children.Add(line);

            Grid ecgRow = new Grid();
            ecgRow.ColumnDefinitions.Add(new ColumnDefinition());
            ecgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            MiniEcg ecg = CreateEcg();
            ecg.Height = 46;
            ecgRow.Children.Add(ecg);
            TextBlock sig = MonoText("ECG\nLIVE", 7.5, Muted);
            sig.VerticalAlignment = VerticalAlignment.Center;
            sig.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(sig, 1);
            ecgRow.Children.Add(sig);
            s.Children.Add(ecgRow);
            return s;
        }

        private UIElement Row(string name, string value, Brush color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 3, 0, 3);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 10;
            n.Foreground = Sub;
            g.Children.Add(n);
            TextBlock v = MonoText(value, 9, color);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private UIElement ProcessTable()
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(MonoText("PROCESSES  /  WINDOWS + GAME + TOOL", 9, Accent));
            TextBlock note = MonoText("CPU / MEMORY / THREADS", 8, Muted);
            Grid.SetColumn(note, 1);
            head.Children.Add(note);
            s.Children.Add(head);

            Grid columns = new Grid();
            columns.Margin = new Thickness(0, 10, 0, 4);
            string[] names = { "PROCESS", "PID", "CPU", "MEMORY", "THREADS", "STATE" };
            double[] widths = { 2.2, 70, 70, 220, 80, 100 };
            for (int i = 0; i < widths.Length; i++)
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], i == 0 ? GridUnitType.Star : GridUnitType.Pixel) });
            for (int i = 0; i < names.Length; i++)
            {
                TextBlock t = MonoText(names[i], 7.5, Muted);
                Grid.SetColumn(t, i);
                columns.Children.Add(t);
            }
            s.Children.Add(columns);
            s.Children.Add(ProcRow("SCPSL.exe", "游戏", "4821", "12.4%", 0.37, "6.8 GB", "96", "RUNNING", Accent));
            s.Children.Add(ProcRow("SCPSLAlert.exe", "本工具", "7736", "1.1%", 0.02, "312 MB", "24", "ACTIVE", ToolBar));
            s.Children.Add(ProcRow("explorer.exe", "系统", "3180", "0.3%", 0.01, "210 MB", "88", "IDLE", SystemBar));
            s.Children.Add(ProcRow("dwm.exe", "系统", "1204", "0.8%", 0.02, "176 MB", "18", "ACTIVE", SystemBar));
            s.Children.Add(ProcRow("System", "内核", "4", "0.1%", 0.01, "12 MB", "210", "SYSTEM", SystemBar));
            s.Children.Add(ProcRow("Steam.exe", "平台", "9120", "0.4%", 0.01, "420 MB", "58", "IDLE", SystemBar));
            s.Children.Add(ProcRow("svchost.exe", "系统服务", "1780", "0.2%", 0.01, "96 MB", "32", "IDLE", SystemBar));
            s.Children.Add(ProcRow("MsMpEng.exe", "安全", "3620", "0.6%", 0.02, "240 MB", "42", "ACTIVE", SystemBar));

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.Margin = new Thickness(0, 10, 0, 8);
            s.Children.Add(line);
            Grid summary = new Grid();
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.Children.Add(MonoText("DISK C:  42% FREE", 8, Muted));
            UIElement s2 = MonoText("MEM PRESSURE:  NORMAL", 8, Muted);
            Grid.SetColumn(s2, 1);
            summary.Children.Add(s2);
            UIElement s3 = MonoText("POLL:  1000 MS", 8, Muted);
            Grid.SetColumn(s3, 2);
            summary.Children.Add(s3);
            UIElement s4 = MonoText("SORT:  MEMORY DESC", 8, Muted);
            Grid.SetColumn(s4, 3);
            summary.Children.Add(s4);
            s.Children.Add(summary);
            return s;
        }

        private UIElement ProcRow(string name, string role, string pid, string cpu, double memRatio, string mem, string threads, string state, Brush color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 5, 0, 5);
            double[] widths = { 2.2, 70, 70, 220, 80, 100 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], i == 0 ? GridUnitType.Star : GridUnitType.Pixel) });

            StackPanel proc = new StackPanel();
            proc.Orientation = Orientation.Horizontal;
            Border dot = new Border();
            dot.Width = 6;
            dot.Height = 6;
            dot.Background = color;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 8, 0);
            proc.Children.Add(dot);
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 11;
            n.Foreground = Ink;
            n.VerticalAlignment = VerticalAlignment.Center;
            proc.Children.Add(n);
            TextBlock r = new TextBlock();
            r.Text = "  " + role;
            r.FontSize = 9;
            r.Foreground = Muted;
            r.VerticalAlignment = VerticalAlignment.Center;
            proc.Children.Add(r);
            g.Children.Add(proc);

            AddCell(g, 1, pid);
            AddCell(g, 2, cpu);

            Grid memCell = new Grid();
            memCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            memCell.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock mv = MonoText(mem, 9, Sub);
            mv.VerticalAlignment = VerticalAlignment.Center;
            memCell.Children.Add(mv);
            Border track = new Border();
            track.Height = 2;
            track.Background = Line;
            track.VerticalAlignment = VerticalAlignment.Center;
            track.Margin = new Thickness(0, 0, 10, 0);
            Border fill = new Border();
            fill.Width = 130 * memRatio;
            fill.Background = color;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            track.Child = fill;
            Grid.SetColumn(track, 1);
            memCell.Children.Add(track);
            Grid.SetColumn(memCell, 3);
            g.Children.Add(memCell);

            AddCell(g, 4, threads);
            AddCell(g, 5, state);
            return g;
        }

        private void AddCell(Grid g, int col, string text)
        {
            TextBlock t = MonoText(text, 9, Sub);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        private UIElement FooterStats()
        {
            Border host = new Border();
            host.BorderBrush = Line;
            host.BorderThickness = new Thickness(0, 1, 0, 0);
            host.Margin = new Thickness(0, 8, 0, 0);
            Grid g = new Grid();
            host.Child = g;
            string[] items = { "WINDOWS 11  26100", "UPTIME  3d 04h", _light ? "THEME  LIGHT" : "THEME  DARK", "DPI  100%", "OCR  READY", "LOG  1.8 MB", "HOTKEYS  06", "ALERTS  20" };
            for (int i = 0; i < items.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock t = MonoText(items[i], 8, i == 2 ? Accent : Sub);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Margin = new Thickness(12, 10, 0, 10);
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return host;
        }

        private UIElement BottomStrip()
        {
            Border b = new Border();
            b.Background = _light ? Panel : Solid(3, 6, 8);
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            Grid.SetRow(b, 2);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock right = MonoText(AppInfo.Version, 8, Muted);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            b.Child = g;
            return b;
        }

        private double TitleSize
        {
            get { return Math.Max(8, Math.Min(96, _cfg.FontTitleSize)); }
        }

        private TextBlock DisplayText(string text, double size, Brush color)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Display;
            t.FontSize = size <= 10.0 ? size + 1.0 : size;
            t.Foreground = color;
            return t;
        }

        private TextBlock MonoText(string text, double size, Brush color)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Mono;
            t.FontSize = size <= 10.0 ? size + 1.2 : size;
            t.Foreground = color;
            return t;
        }
    }

    internal sealed class VigilSelect : Grid
    {
        private readonly Border _field;
        private readonly TextBlock _text;
        private readonly Popup _popup;
        private readonly StackPanel _items;
        private readonly List<Border> _rows = new List<Border>();
        private readonly List<TextBlock> _rowTexts = new List<TextBlock>();
        private readonly TranslateTransform _shift = new TranslateTransform();
        private readonly Border _drop;
        private readonly Brush _bg;
        private readonly Brush _border;
        private readonly Brush _ink;
        private readonly Brush _accent;
        private readonly Brush _selectedBg;
        private int _selected = -1;
        public event EventHandler SelectionChanged;
        public readonly List<string> Items = new List<string>();

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                _selected = v;
                _text.Text = v >= 0 && v < Items.Count ? Items[v] : "";
                RefreshRows();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get { return _selected >= 0 && _selected < Items.Count ? Items[_selected] : ""; }
        }

        public VigilSelect(Brush bg, Brush border, Brush ink, Brush accent, int width)
        {
            _bg = bg; _border = border; _ink = ink; _accent = accent;
            // 选择框也走卡片玻璃参数；小控件不需要噪点，SurfaceBrush 已经去掉。
            Brush glassBg = WpfCardGlass.SurfaceBrush(bg);
            _selectedBg = glassBg;
            Width = width;
            Height = 28;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            _field = new Border();
            _field.CornerRadius = new CornerRadius(3);
            _field.BorderThickness = new Thickness(1);
            _field.BorderBrush = border;
            _field.Background = glassBg;
            _field.Cursor = Cursors.Hand;
            _text = new TextBlock();
            _text.FontFamily = WpfTheme.UiFont;
            _text.FontSize = 10;
            _text.Foreground = ink;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.Margin = new Thickness(7, 0, 0, 0);
            _text.TextTrimming = TextTrimming.CharacterEllipsis;
            _field.Child = _text;
            SetColumn(_field, 0);
            Children.Add(_field);

            Border arrowBg = new Border();
            arrowBg.Background = glassBg;
            arrowBg.BorderBrush = border;
            arrowBg.BorderThickness = new Thickness(0, 1, 1, 1);
            arrowBg.CornerRadius = new CornerRadius(0, 3, 3, 0);
            TextBlock arrow = new TextBlock();
            arrow.Text = "\uE70D";
            arrow.FontFamily = WpfTheme.IconFont;
            arrow.FontSize = 8;
            arrow.Foreground = ink;
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrowBg.Child = arrow;
            SetColumn(arrowBg, 1);
            Children.Add(arrowBg);

            _drop = new Border();
            _drop.Background = glassBg;
            _drop.BorderBrush = border;
            _drop.BorderThickness = new Thickness(1);
            _drop.CornerRadius = new CornerRadius(6);
            _drop.Padding = new Thickness(4);
            _drop.Margin = new Thickness(0, 4, 0, 0);
            _drop.MinWidth = width;
            _drop.MaxHeight = 240;
            _drop.RenderTransform = _shift;
            _drop.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.35
            };
            _items = new StackPanel();
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Content = _items;
            _drop.Child = scroll;

            _popup = new Popup();
            _popup.PlacementTarget = this;
            _popup.Placement = PlacementMode.Bottom;
            _popup.StaysOpen = false;
            _popup.AllowsTransparency = true;
            _popup.PopupAnimation = PopupAnimation.None;
            _popup.Child = _drop;

            _field.MouseLeftButtonUp += delegate { _popup.IsOpen = !_popup.IsOpen; };
            _popup.Opened += delegate
            {
                _drop.Opacity = 0;
                _shift.Y = -6;
                DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                _drop.BeginAnimation(UIElement.OpacityProperty, fade);
                DoubleAnimation slide = new DoubleAnimation(-6, 0, new Duration(TimeSpan.FromMilliseconds(170)));
                slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                _shift.BeginAnimation(TranslateTransform.YProperty, slide);
            };
            arrowBg.MouseLeftButtonUp += delegate { _popup.IsOpen = !_popup.IsOpen; };
        }

        private void RefreshRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i == _selected;
                _rows[i].Background = on ? _selectedBg : Brushes.Transparent;
                _rowTexts[i].Foreground = on ? _accent : _ink;
                _rowTexts[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        public void SetItems(IEnumerable<string> items, int selected)
        {
            Items.Clear();
            _items.Children.Clear();
            _rows.Clear();
            _rowTexts.Clear();
            foreach (string item in items)
            {
                string value = item;
                int index = Items.Count;
                Items.Add(value);
                Border row = new Border();
                row.Padding = new Thickness(8, 5, 8, 5);
                row.Background = Brushes.Transparent;
                row.Cursor = Cursors.Hand;
                TextBlock t = new TextBlock();
                t.Text = value;
                t.FontFamily = WpfTheme.UiFont;
                t.FontSize = 10;
                t.Foreground = _ink;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Child = t;
                row.MouseEnter += delegate { row.Background = _accent; t.Foreground = Brushes.White; };
                row.MouseLeave += delegate
                {
                    bool on = index == _selected;
                    row.Background = on ? _selectedBg : Brushes.Transparent;
                    t.Foreground = on ? _accent : _ink;
                };
                row.MouseLeftButtonUp += delegate
                {
                    _popup.IsOpen = false;
                    SelectedIndex = index;
                };
                _items.Children.Add(row);
                _rows.Add(row);
                _rowTexts.Add(t);
            }
            SelectedIndex = selected;
        }
    }
    internal sealed class VigilToggle : FrameworkElement
    {
        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
            "IsChecked", typeof(bool), typeof(VigilToggle),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCheckedChanged));

        private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
            "Phase", typeof(double), typeof(VigilToggle),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private readonly Brush _accent;
        private readonly Brush _off;
        private readonly Brush _knob;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }

        public VigilToggle(bool light, Brush accent, Brush off, Brush knob)
        {
            _accent = accent;
            _off = off;
            _knob = knob;
            Width = 44;
            Height = 24;
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += delegate { IsChecked = !IsChecked; };
            MouseEnter += delegate { _hover = true; InvalidateVisual(); };
            MouseLeave += delegate { _hover = false; InvalidateVisual(); };
        }

        private static void OnCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VigilToggle t = (VigilToggle)d;
            DoubleAnimation a = new DoubleAnimation((bool)e.NewValue ? 1.0 : 0.0, new Duration(TimeSpan.FromMilliseconds(170)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            t.BeginAnimation(PhaseProperty, a);
            if (t.CheckedChanged != null) t.CheckedChanged(t, EventArgs.Empty);
        }

        private double Phase { get { return (double)GetValue(PhaseProperty); } }

        protected override void OnRender(DrawingContext dc)
        {
            double w = Width;
            double h = Height;
            Brush track = IsChecked ? _accent : _off;
            if (_hover)
                track = IsChecked ? WpfTheme.Blend(_accent, Brushes.White, 0.16) : WpfTheme.Blend(_off, Brushes.White, 0.2);
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            double d = h - 4;
            double x = 2 + Phase * (w - d - 4);
            dc.DrawEllipse(_knob, null, new Point(x + d / 2, h / 2), d / 2, d / 2);
        }
    }

    internal sealed class VigilNumberField : Border
    {
        private readonly TextBox _box;
        private readonly TextBlock _unit;
        public decimal Minimum = 0;
        public decimal Maximum = 1000;
        public string Unit
        {
            get { return _unit.Text; }
            set
            {
                _unit.Text = value == null ? "" : value;
                _unit.Visibility = string.IsNullOrEmpty(_unit.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        public event EventHandler ValueChanged;

        public decimal Value
        {
            get
            {
                decimal v;
                if (!decimal.TryParse(_box.Text, out v)) return Minimum;
                if (v < Minimum) v = Minimum;
                if (v > Maximum) v = Maximum;
                return v;
            }
            set
            {
                decimal v = value;
                if (v < Minimum) v = Minimum;
                if (v > Maximum) v = Maximum;
                _box.Text = v.ToString("0.##");
            }
        }

        public VigilNumberField(bool light, Brush line, Brush ink, Brush sub, int width)
        {
            Width = width;
            Height = 30;
            CornerRadius = new CornerRadius(4);
            BorderBrush = line;
            BorderThickness = new Thickness(1);
            Background = WpfTheme.InputBg != null
                ? WpfCardGlass.SurfaceBrush(WpfTheme.InputBg)
                : Brushes.Transparent;
            Padding = new Thickness(8, 0, 8, 0);
            _box = new TextBox();
            _box.BorderThickness = new Thickness(0);
            _box.Background = Brushes.Transparent;
            _box.Foreground = ink;
            _box.CaretBrush = ink;
            _box.FontFamily = WpfTheme.UiFont;
            _box.FontSize = 11;
            _box.VerticalContentAlignment = VerticalAlignment.Center;
            _box.Padding = new Thickness(0);

            // 单位后缀：数字框右侧直接显示“秒/%/px”，不再另写灰色说明。
            Grid host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition());
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.Children.Add(_box);
            _unit = new TextBlock();
            _unit.FontFamily = WpfTheme.UiFont;
            _unit.FontSize = 9.5;
            _unit.Foreground = sub;
            _unit.VerticalAlignment = VerticalAlignment.Center;
            _unit.Margin = new Thickness(4, 0, 0, 0);
            _unit.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_unit, 1);
            host.Children.Add(_unit);
            Child = host;
            WpfTextInputFallback.Attach(_box, true);
            PreviewMouseLeftButtonDown += delegate
            {
                try
                {
                    Window w = Window.GetWindow(this);
                    if (w != null && !w.IsActive) w.Activate();
                    if (!_box.IsKeyboardFocusWithin) { _box.Focus(); Keyboard.Focus(_box); }
                }
                catch { }
            };
            _box.LostFocus += delegate { Normalize(); };
            _box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) Normalize();
            };
        }

        private void Normalize()
        {
            Value = Value;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// WinForms 主消息循环里，WPF 文本框有时收不到 WM_CHAR（退格能用、字符打不进去）。
    /// 这里在 PreviewKeyDown 里用 ToUnicode 补一次字符插入；只对 A.R.C.T 自己的输入框挂。
    /// </summary>
    internal static class WpfTextInputFallback
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags);

        public static void Attach(TextBox box, bool numericOnly)
        {
            if (box == null) return;
            box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                try
                {
                    if (box.IsReadOnly) return;
                    if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
                    Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                    int vk = KeyInterop.VirtualKeyFromKey(key);
                    if (vk <= 0) return;
                    byte[] state = new byte[256];
                    if (!GetKeyboardState(state)) return;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(8);
                    int n = ToUnicode((uint)vk, MapVirtualKey((uint)vk, 0), state, sb, sb.Capacity, 0);
                    if (n <= 0 || sb.Length == 0) return;
                    string text = sb.ToString();
                    if (numericOnly)
                    {
                        System.Text.StringBuilder filtered = new System.Text.StringBuilder();
                        for (int i = 0; i < text.Length; i++)
                            if ((text[i] >= '0' && text[i] <= '9') || text[i] == '.' || text[i] == '-')
                                filtered.Append(text[i]);
                        text = filtered.ToString();
                    }
                    else
                    {
                        System.Text.StringBuilder filtered = new System.Text.StringBuilder();
                        for (int i = 0; i < text.Length; i++)
                            if (text[i] >= ' ') filtered.Append(text[i]);
                        text = filtered.ToString();
                    }
                    if (text.Length == 0) return;
                    int start = box.SelectionStart;
                    int length = box.SelectionLength;
                    string current = box.Text ?? "";
                    if (start < 0) start = 0;
                    if (start > current.Length) start = current.Length;
                    if (length < 0) length = 0;
                    if (start + length > current.Length) length = current.Length - start;
                    box.Text = current.Substring(0, start) + text + current.Substring(start + length);
                    box.SelectionStart = start + text.Length;
                    box.SelectionLength = 0;
                    e.Handled = true;
                }
                catch { }
            };
        }
    }

    internal sealed class StackBar : FrameworkElement
    {
        private readonly Brush _system;
        private readonly Brush _game;
        private readonly Brush _tool;
        private readonly Brush _line;
        private double _systemRatio = 0.29;
        private double _gameRatio = 0.21;
        private double _toolRatio = 0.01;

        public StackBar(Brush system, Brush game, Brush tool, Brush line)
        {
            _system = system;
            _game = game;
            _tool = tool;
            _line = line;
            IsHitTestVisible = false;
        }

        public void SetValues(double systemGb, double gameGb, double toolGb, double freeGb)
        {
            double total = systemGb + gameGb + toolGb + freeGb;
            if (total <= 0) return;
            _systemRatio = systemGb / total;
            _gameRatio = gameGb / total;
            _toolRatio = toolGb / total;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            dc.DrawRectangle(_line, null, new Rect(0, 0, w, h));
            double system = w * _systemRatio;
            double game = w * _gameRatio;
            double tool = w * _toolRatio;
            dc.DrawRectangle(_system, null, new Rect(0, 0, system, h));
            dc.DrawRectangle(_game, null, new Rect(system + 2, 0, Math.Max(0, game - 2), h));
            dc.DrawRectangle(_tool, null, new Rect(system + game + 4, 0, Math.Max(1, tool), h));
        }
    }

    internal sealed class ResourceHistory
    {
        public readonly List<double> Cpu = new List<double>();
        public readonly List<double> Memory = new List<double>();
        public readonly List<double> Gpu = new List<double>();
        public readonly List<double> Network = new List<double>();
        public readonly List<double> Disk = new List<double>();
        public readonly List<double> Temperature = new List<double>();
        private readonly int _capacity;

        public ResourceHistory(int capacity)
        {
            _capacity = capacity > 2 ? capacity : 60;
        }

        public int Count { get { return Cpu.Count; } }
        public int Capacity { get { return _capacity; } }

        public void Add(double cpu, double memory, double gpu, double network, double disk, double temperature)
        {
            Cpu.Add(Safe(cpu));
            Memory.Add(Safe(memory));
            Gpu.Add(Safe(gpu));
            Network.Add(Safe(network));
            Disk.Add(Safe(disk));
            Temperature.Add(Safe(temperature));
            while (Cpu.Count > _capacity)
            {
                Cpu.RemoveAt(0);
                Memory.RemoveAt(0);
                Gpu.RemoveAt(0);
                Network.RemoveAt(0);
                Disk.RemoveAt(0);
                Temperature.RemoveAt(0);
            }
        }

        private static double Safe(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return value;
        }

        public void FillTemperatureIfZero(double value)
        {
            for (int i = 0; i < Temperature.Count; i++)
                if (Temperature[i] <= 0.01) Temperature[i] = Safe(value);
        }

        public static double Average(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        public static double Peak(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double max = 0;
            for (int i = 0; i < values.Count; i++) if (values[i] > max) max = values[i];
            return max;
        }
    }

    internal sealed class HistoryChart : FrameworkElement
    {
        private sealed class Series
        {
            public string Key;
            public string Name;
            public Brush Color;
            public List<double> Values;
            public bool Percent;
        }

        private readonly Brush _line;
        private readonly Brush _muted;
        private readonly Brush _ink;
        private readonly Brush _panel;
        private readonly Brush _sub;
        private readonly Brush _netBrush;
        private readonly ResourceHistory _data;
        private readonly List<Series> _series = new List<Series>();
        private Point _mouse;
        private bool _hover;
        private int _hoverIndex = -1;
        private double _netMax = 1;

        public string DiskLabel = "C:";
        public string TooltipFooter = "双击进入性能详情";
        public bool TemperatureAvailable;
        public bool ShowCpu = true;
        public bool ShowMem = true;
        public bool ShowGpu = true;
        public bool ShowNet = true;
        public bool ShowDisk = true;
        public bool ShowTemperature = true;

        public HistoryChart(bool light, Brush accent, Brush accent2, Brush line, Brush muted,
            Brush ink, Brush panel, Brush sub, Brush gpu, Brush net, Brush disk, Brush temp, ResourceHistory data)
        {
            _line = line;
            _muted = muted;
            _ink = ink;
            _panel = panel;
            _sub = sub;
            _netBrush = net;
            _data = data;
            Series cpu = new Series();
            cpu.Key = "cpu";
            cpu.Name = "CPU";
            cpu.Color = accent2;
            cpu.Values = data.Cpu;
            cpu.Percent = true;
            _series.Add(cpu);
            Series mem = new Series();
            mem.Key = "mem";
            mem.Name = "内存";
            mem.Color = accent;
            mem.Values = data.Memory;
            mem.Percent = true;
            _series.Add(mem);
            Series gpuSeries = new Series();
            gpuSeries.Key = "gpu";
            gpuSeries.Name = "GPU";
            gpuSeries.Color = gpu;
            gpuSeries.Values = data.Gpu;
            gpuSeries.Percent = true;
            _series.Add(gpuSeries);
            Series netSeries = new Series();
            netSeries.Key = "net";
            netSeries.Name = "网络";
            netSeries.Color = net;
            netSeries.Values = data.Network;
            netSeries.Percent = false;
            _series.Add(netSeries);
            Series diskSeries = new Series();
            diskSeries.Key = "disk";
            diskSeries.Name = "游戏磁盘";
            diskSeries.Color = disk;
            diskSeries.Values = data.Disk;
            diskSeries.Percent = true;
            _series.Add(diskSeries);
            Series tempSeries = new Series();
            tempSeries.Key = "temp";
            tempSeries.Name = "温度";
            tempSeries.Color = temp;
            tempSeries.Values = data.Temperature;
            tempSeries.Percent = true;
            _series.Add(tempSeries);

            IsHitTestVisible = true;
            Cursor = Cursors.Cross;
            MouseMove += OnChartMouseMove;
            MouseLeave += OnChartMouseLeave;
        }

        private bool IsSeriesVisible(Series series)
        {
            if (series == null) return false;
            switch (series.Key)
            {
                case "cpu": return ShowCpu;
                case "mem": return ShowMem;
                case "gpu": return ShowGpu;
                case "net": return ShowNet;
                case "disk": return ShowDisk;
                case "temp": return ShowTemperature && TemperatureAvailable;
                default: return true;
            }
        }

        private int VisibleSeriesCount()
        {
            int count = 0;
            for (int i = 0; i < _series.Count; i++) if (IsSeriesVisible(_series[i])) count++;
            return count;
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            _mouse = e.GetPosition(this);
            Rect plot = PlotRect();
            bool inside = plot.Contains(_mouse);
            int count = _data.Count;
            int index = -1;
            if (inside && count > 0)
            {
                double step = StepFor(plot);
                index = count - 1 - (int)Math.Round((plot.Right - _mouse.X) / step);
                if (index < 0) index = 0;
                if (index >= count) index = count - 1;
            }
            bool changed = _hover != inside || _hoverIndex != index;
            _hover = inside;
            _hoverIndex = index;
            if (changed) InvalidateVisual();
        }

        private void OnChartMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_hover) return;
            _hover = false;
            _hoverIndex = -1;
            InvalidateVisual();
        }

        private Rect PlotRect()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            double left = 43;
            double right = Math.Max(left + 12, w - 64);
            double top = 10;
            double bottom = Math.Max(top + 12, h - 21);
            return new Rect(left, top, right - left, bottom - top);
        }

        private double StepFor(Rect plot)
        {
            int capacity = _data.Capacity;
            return capacity > 1 ? plot.Width / (capacity - 1) : plot.Width;
        }

        private double XAt(Rect plot, int index)
        {
            return plot.Right - (_data.Count - 1 - index) * StepFor(plot);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 1 || h <= 1) return;
            Rect plot = PlotRect();
            if (plot.Width <= 8 || plot.Height <= 8) return;

            Pen grid = new Pen(_line, 0.65);
            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Top + plot.Height * i / 4.0;
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            for (int i = 0; i <= 6; i++)
            {
                double x = plot.Left + plot.Width * i / 6.0;
                dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }

            _netMax = NetworkAxisMax();
            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Top + plot.Height * i / 4.0;
                int percent = 100 - i * 25;
                string pct = percent == 100 ? "100%" : percent.ToString();
                DrawText(dc, pct, 8.2, _muted, new Point(2, y - 7), "Consolas");

                double netValue = _netMax * (4 - i) / 4.0;
                string netText = _netMax >= 10 ? netValue.ToString("0.0") : netValue.ToString("0.0");
                if (i == 0) netText += " Mbps";
                DrawText(dc, netText, 8.2, _netBrush, new Point(plot.Right + 8, y - 7), "Consolas");
            }
            LabelAxis(dc, plot);
            int visible = VisibleSeriesCount();
            if (visible == 0)
            {
                string hidden = "全部曲线已隐藏 · 点击图例恢复";
                double hw = MeasureText(hidden, 9, "Microsoft YaHei UI");
                DrawText(dc, hidden, 9, _muted, new Point(plot.Left + (plot.Width - hw) / 2, plot.Top + plot.Height / 2 - 6), "Microsoft YaHei UI");
            }
            else if (_data.Count < 3)
            {
                string hint = "正在采集历史数据…";
                double hw = MeasureText(hint, 9, "Microsoft YaHei UI");
                DrawText(dc, hint, 9, _muted, new Point(plot.Left + (plot.Width - hw) / 2, plot.Top + plot.Height / 2 - 6), "Microsoft YaHei UI");
            }

            dc.PushClip(new RectangleGeometry(plot));
            for (int i = 0; i < _series.Count; i++)
                if (IsSeriesVisible(_series[i])) DrawSeries(dc, _series[i], plot);
            dc.Pop();
            if (_hover && _hoverIndex >= 0 && visible > 0) DrawHover(dc, plot);
        }

        private void LabelAxis(DrawingContext dc, Rect plot)
        {
            string[] times = { "-60s", "-45s", "-30s", "-15s", "现在" };
            double[] ratios = { 0, 0.25, 0.5, 0.75, 1.0 };
            for (int i = 0; i < times.Length; i++)
            {
                string text = times[i];
                double width = MeasureText(text, 7.6, "Microsoft YaHei UI");
                double x = plot.Left + plot.Width * ratios[i] - width / 2;
                if (i == 0) x = plot.Left;
                if (i == times.Length - 1) x = plot.Right - width;
                DrawText(dc, text, 7.6, _muted, new Point(x, plot.Bottom + 5), "Microsoft YaHei UI");
            }
        }

        private void DrawSeries(DrawingContext dc, Series series, Rect plot)
        {
            List<double> values = series.Values;
            if (values.Count < 2) return;
            int count = values.Count;
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                for (int i = 0; i < count; i++)
                {
                    double x = XAt(plot, i);
                    double v = series.Percent ? Math.Max(0, Math.Min(100, values[i]))
                        : Math.Max(0, Math.Min(_netMax, values[i]));
                    double max = series.Percent ? 100.0 : _netMax;
                    double y = plot.Bottom - v / max * plot.Height;
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            SolidColorBrush color = series.Color as SolidColorBrush;
            if (color != null)
            {
                Pen glow = new Pen(new SolidColorBrush(Color.FromArgb(48, color.Color.R, color.Color.G, color.Color.B)), 4.2);
                dc.DrawGeometry(null, glow, geo);
            }
            Pen line = new Pen(series.Color, 1.9);
            line.LineJoin = PenLineJoin.Round;
            dc.DrawGeometry(null, line, geo);

            double lastX = plot.Left + plot.Width;
            double lastV = series.Percent ? Math.Max(0, Math.Min(100, values[count - 1]))
                : Math.Max(0, Math.Min(_netMax, values[count - 1]));
            double lastMax = series.Percent ? 100.0 : _netMax;
            double lastY = plot.Bottom - lastV / lastMax * plot.Height;
            dc.DrawEllipse(_panel, new Pen(series.Color, 1.4), new Point(lastX, lastY), 3.6, 3.6);
        }

        private void DrawHover(DrawingContext dc, Rect plot)
        {
            int count = _data.Count;
            if (count == 0) return;
            int index = Math.Max(0, Math.Min(count - 1, _hoverIndex));
            double x = XAt(plot, index);
            Pen guide = new Pen(_muted, 1);
            guide.DashStyle = new DashStyle(new double[] { 3, 4 }, 0);
            dc.DrawLine(guide, new Point(x, plot.Top), new Point(x, plot.Bottom));

            List<Series> visible = new List<Series>();
            for (int i = 0; i < _series.Count; i++)
                if (IsSeriesVisible(_series[i])) visible.Add(_series[i]);
            if (visible.Count == 0) return;

            for (int i = 0; i < visible.Count; i++)
            {
                Series series = visible[i];
                if (index >= series.Values.Count) continue;
                double v = series.Percent ? Math.Max(0, Math.Min(100, series.Values[index]))
                    : Math.Max(0, Math.Min(_netMax, series.Values[index]));
                double max = series.Percent ? 100.0 : _netMax;
                double y = plot.Bottom - v / max * plot.Height;
                dc.DrawEllipse(_panel, new Pen(series.Color, 2.0), new Point(x, y), 3.4, 3.4);
            }

            int ago = count - 1 - index;
            string caption = ago <= 0 ? "现在  /  NOW" : "-" + ago + " s";
            double boxWidth = 224;
            double rowHeight = 21;
            double boxHeight = 36 + visible.Count * rowHeight + 28;
            double bx = _mouse.X + 18;
            if (bx + boxWidth > ActualWidth - 4) bx = _mouse.X - boxWidth - 18;
            if (bx < 4) bx = 4;
            double by = _mouse.Y - boxHeight / 2;
            if (by + boxHeight > ActualHeight - 4) by = ActualHeight - boxHeight - 4;
            if (by < 4) by = 4;

            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(42, 0, 0, 0)), null,
                new Rect(bx + 3, by + 3, boxWidth, boxHeight), 6, 6);
            dc.DrawRoundedRectangle(_panel, new Pen(_line, 1), new Rect(bx, by, boxWidth, boxHeight), 6, 6);
            DrawText(dc, "资源快照  /  RESOURCE SNAPSHOT", 8, _muted, new Point(bx + 12, by + 9), "Microsoft YaHei UI");
            DrawText(dc, caption, 8.5, _sub, new Point(bx + boxWidth - 12 - MeasureText(caption, 8.5, "Consolas"), by + 9), "Consolas");

            for (int i = 0; i < visible.Count; i++)
            {
                Series series = visible[i];
                double y = by + 34 + i * rowHeight;
                dc.DrawRoundedRectangle(series.Color, null, new Rect(bx + 12, y + 6, 13, 3), 2, 2);
                DrawText(dc, series.Name, 9.2, _sub, new Point(bx + 31, y), "Microsoft YaHei UI");
                string value;
                if (series.Key == "temp")
                {
                    double v = index < series.Values.Count ? series.Values[index] : 0;
                    value = v.ToString("0.0") + " °C";
                }
                else if (series.Percent)
                {
                    double v = index < series.Values.Count ? series.Values[index] : 0;
                    value = v.ToString("0.0") + "%";
                }
                else
                {
                    double v = index < series.Values.Count ? series.Values[index] : 0;
                    value = v.ToString("0.00") + " Mbps";
                }
                if (series.Key == "disk")
                {
                    double v = index < series.Values.Count ? series.Values[index] : 0;
                    value = DiskLabel + "  " + v.ToString("0.0") + "%";
                }
                double vw = MeasureText(value, 9.5, "Consolas");
                DrawText(dc, value, 9.5, _ink, new Point(bx + boxWidth - 12 - vw, y), "Consolas");
            }

            double footY = by + boxHeight - 23;
            dc.DrawLine(new Pen(_line, 0.8), new Point(bx + 12, footY - 5), new Point(bx + boxWidth - 12, footY - 5));
            DrawText(dc, TooltipFooter, 8, _muted, new Point(bx + 12, footY), "Microsoft YaHei UI");
        }

        private double NetworkAxisMax()
        {
            double peak = ResourceHistory.Peak(_data.Network);
            return NiceMax(Math.Max(1.0, peak * 1.25));
        }

        private static double NiceMax(double value)
        {
            if (value <= 0) return 1;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double f = value / exp;
            double nice;
            if (f <= 1) nice = 1;
            else if (f <= 2) nice = 2;
            else if (f <= 5) nice = 5;
            else nice = 10;
            return nice * exp;
        }

        private double MeasureText(string text, double size, string font)
        {
            FormattedText ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(font), size, Brushes.Black, 1.0);
            return ft.Width;
        }

        private void DrawText(DrawingContext dc, string text, double size, Brush brush, Point at, string font)
        {
            FormattedText ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(font), size, brush, 1.0);
            dc.DrawText(ft, at);
        }
    }

    internal sealed class FrameChart : FrameworkElement
    {
        private readonly List<double> _values;
        private readonly Brush _accent;
        private readonly Brush _line;
        private readonly Brush _muted;
        private readonly Brush _panel;
        private readonly Brush _ink;
        private readonly int _target;

        public FrameChart(List<double> values, int target, Brush accent, Brush line, Brush muted, Brush panel, Brush ink)
        {
            _values = values;
            _target = target > 0 ? target : 60;
            _accent = accent;
            _line = line;
            _muted = muted;
            _panel = panel;
            _ink = ink;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 1 || h <= 1) return;
            Rect plot = new Rect(36, 8, Math.Max(10, w - 50), Math.Max(10, h - 26));
            double peak = _target;
            for (int i = 0; i < _values.Count; i++) if (_values[i] > peak) peak = _values[i];
            double max = Math.Ceiling(Math.Max(_target, peak) * 1.06);

            Pen grid = new Pen(_line, 0.65);
            for (int i = 0; i <= 3; i++)
            {
                double y = plot.Top + plot.Height * i / 3.0;
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
                double value = max * (3 - i) / 3.0;
                FormattedText ft = Text(value.ToString("0"), 7.6, _muted);
                dc.DrawText(ft, new Point(3, y - 6));
            }

            double targetY = plot.Bottom - _target / max * plot.Height;
            Pen targetPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 150, 160, 170)), 1);
            targetPen.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
            dc.DrawLine(targetPen, new Point(plot.Left, targetY), new Point(plot.Right, targetY));
            FormattedText targetText = Text("TARGET " + _target + " HZ", 7.6, _muted);
            dc.DrawText(targetText, new Point(plot.Right - targetText.Width, targetY - 12));

            if (_values.Count >= 2)
            {
                StreamGeometry line = new StreamGeometry();
                StreamGeometry area = new StreamGeometry();
                using (StreamGeometryContext ctx = line.Open())
                using (StreamGeometryContext ac = area.Open())
                {
                    for (int i = 0; i < _values.Count; i++)
                    {
                        double x = plot.Right - (_values.Count - 1 - i) * (plot.Width / 59.0);
                        double y = plot.Bottom - Math.Max(0, Math.Min(max, _values[i])) / max * plot.Height;
                        if (i == 0)
                        {
                            ctx.BeginFigure(new Point(x, y), false, false);
                            ac.BeginFigure(new Point(x, plot.Bottom), true, true);
                            ac.LineTo(new Point(x, y), true, false);
                        }
                        else
                        {
                            ctx.LineTo(new Point(x, y), true, false);
                            ac.LineTo(new Point(x, y), true, false);
                        }
                    }
                    ac.LineTo(new Point(plot.Right, plot.Bottom), true, false);
                }
                line.Freeze();
                area.Freeze();
                SolidColorBrush accent = _accent as SolidColorBrush;
                if (accent != null)
                {
                    LinearGradientBrush fill = new LinearGradientBrush();
                    fill.StartPoint = new Point(0, 0);
                    fill.EndPoint = new Point(0, 1);
                    fill.GradientStops.Add(new GradientStop(Color.FromArgb(72, accent.Color.R, accent.Color.G, accent.Color.B), 0));
                    fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.Color.R, accent.Color.G, accent.Color.B), 1));
                    dc.DrawGeometry(fill, null, area);
                }
                Pen pen = new Pen(_accent, 1.7);
                pen.LineJoin = PenLineJoin.Round;
                dc.DrawGeometry(null, pen, line);
                double lastY = plot.Bottom - Math.Max(0, Math.Min(max, _values[_values.Count - 1])) / max * plot.Height;
                dc.DrawEllipse(_panel, new Pen(_accent, 1.4), new Point(plot.Right, lastY), 3.2, 3.2);
            }

            FormattedText fpsLabel = Text("FPS", 7.6, _muted);
            dc.DrawText(fpsLabel, new Point(3, plot.Top - 2));
            FormattedText now = Text("最近 60 s", 7.6, _muted);
            dc.DrawText(now, new Point(plot.Right - now.Width, plot.Bottom + 5));
        }

        private static FormattedText Text(string text, double size, Brush brush)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Consolas"), size, brush, 1.0);
        }
    }

    internal sealed class MeterBar : FrameworkElement
    {
        private readonly Brush _fill;
        private readonly Brush _track;
        private double _ratio;

        public MeterBar(Brush fill, Brush track)
        {
            _fill = fill;
            _track = track;
            Height = 4;
            IsHitTestVisible = false;
        }

        public double Ratio
        {
            get { return _ratio; }
            set
            {
                double next = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(next - _ratio) < 0.001) return;
                _ratio = next;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(_track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (_ratio <= 0) return;
            dc.DrawRoundedRectangle(_fill, null, new Rect(0, 0, Math.Max(2, w * _ratio), h), h / 2, h / 2);
        }
    }

    internal sealed class MiniEcg : FrameworkElement
    {
        private const double SampleRate = 90.0;
        private const int Capacity = 2048;
        private double _pixelsPerSecond = 55.0;
        private double _amplitudeScale = 1.0;

        private readonly Brush _danger;
        private readonly Pen _linePen;
        private readonly Pen _wavePen;
        private readonly Pen _glowPen;
        private readonly Pen _deathPen;
        private readonly Pen _deathGlowPen;

        private readonly float[] _samples = new float[Capacity];
        private int _head;
        private int _count;
        private double _sampleAcc;
        private double _beatPhase;
        private int _beatIndex;
        private double _amp = 1;
        private double _rw = 0.006;
        private double _tamp = 0.2;
        private double _bpmSmooth = 72;
        private double _pulse;
        private bool _showDeathLine;
        private bool _hooked;
        private bool _wasDead;
        private DateTime _lastFrame = DateTime.MinValue;
        private DateTime _deathStarted = DateTime.MinValue;

        public MiniEcg(bool light, Brush accent, Brush line, Brush danger)
        {
            _danger = danger;
            IsHitTestVisible = false;

            _linePen = FreezePen(new SolidColorBrush(Color.FromArgb(light ? (byte)110 : (byte)95,
                ((SolidColorBrush)line).Color.R, ((SolidColorBrush)line).Color.G, ((SolidColorBrush)line).Color.B)), 0.8);
            _wavePen = FreezePen(accent, 1.35);
            _glowPen = FreezePen(accent, 4.0);
            _deathPen = FreezePen(danger, 1.8);
            _deathGlowPen = FreezePen(danger, 5.0);

            Loaded += delegate { Hook(); };
            Unloaded += delegate { Unhook(); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) Hook();
                else Unhook();
            };
        }

        public bool ShowDeathLine
        {
            get { return _showDeathLine; }
            set { _showDeathLine = value; InvalidateVisual(); }
        }

        public double PixelsPerSecond
        {
            get { return _pixelsPerSecond; }
            set
            {
                double v = value;
                if (v < 20) v = 20;
                if (v > 140) v = 140;
                _pixelsPerSecond = v;
                InvalidateVisual();
            }
        }

        public double AmplitudeScale
        {
            get { return _amplitudeScale; }
            set
            {
                double v = value;
                if (v < 0.3) v = 0.3;
                if (v > 2.0) v = 2.0;
                _amplitudeScale = v;
                InvalidateVisual();
            }
        }

        private static Pen FreezePen(Brush brush, double thickness)
        {
            Pen p = new Pen(brush, thickness);
            p.Freeze();
            return p;
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            _lastFrame = DateTime.UtcNow;
            if (_count == 0) Prefill();
            CompositionTarget.Rendering += OnFrame;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void Prefill()
        {
            _head = 0;
            _count = 0;
            _sampleAcc = 0;
            _beatPhase = 0;
            _beatIndex = 0;
            _bpmSmooth = 72;
            NewBeatProfile();
            for (int i = 0; i < 512; i++) AppendSample();
        }

        private void NewBeatProfile()
        {
            _amp = 0.86 + 0.26 * BeatRand(_beatIndex, 1);
            _rw = 0.0055 + 0.002 * BeatRand(_beatIndex, 2);
            _tamp = 0.16 + 0.10 * BeatRand(_beatIndex, 3);
        }

        private void AppendSample()
        {
            double y = WaveAt(_beatPhase);
            _samples[_head] = (float)y;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;

            double bpm = _bpmSmooth;
            if (bpm < 35) bpm = 35;
            if (bpm > 190) bpm = 190;
            _beatPhase += (60.0 / bpm) / SampleRate;
            if (_beatPhase >= 1.0)
            {
                _beatPhase -= 1.0;
                _beatIndex++;
                NewBeatProfile();
                _pulse = 1.0;
            }
        }

        private double WaveAt(double phase)
        {
            double v = 0;
            v += Gauss(phase, 0.11, 0.018) * 0.10;
            v += Gauss(phase, 0.225, 0.009) * -0.16;
            v += Gauss(phase, 0.247, _rw) * 1.0;
            v += Gauss(phase, 0.267, 0.011) * -0.26;
            v += Gauss(phase, 0.42, 0.042) * _tamp;
            v += Math.Sin(phase * Math.PI * 2) * 0.008;
            return v * _amp;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double dt = _lastFrame == DateTime.MinValue ? 0.016 : (now - _lastFrame).TotalSeconds;
            _lastFrame = now;
            if (dt <= 0 || dt > 0.1) dt = 0.016;

            bool dead = HeartMonitor.Dead;
            if (dead)
            {
                _pulse = 0;
                if (!_wasDead)
                {
                    _wasDead = true;
                    _deathStarted = now;
                }
            }
            else
            {
                if (_wasDead)
                {
                    _wasDead = false;
                    _deathStarted = DateTime.MinValue;
                    // 复活后从新的一段开始，不把死亡前的旧样本接上来。
                    _count = 0;
                    _head = 0;
                    _sampleAcc = 0;
                    _beatPhase = 0;
                    _beatIndex++;
                    NewBeatProfile();
                    Prefill();
                }

                int bpm = HeartRateHub.Bpm;
                if (bpm <= 0) bpm = 72;
                _bpmSmooth += (bpm - _bpmSmooth) * Math.Min(1.0, dt * 0.7);
                if (_bpmSmooth < 35) _bpmSmooth = 35;
                if (_bpmSmooth > 190) _bpmSmooth = 190;

                // 按固定采样率补齐样本：已经画出来的样本不会再被 BPM 变化重算。
                _sampleAcc += dt * SampleRate;
                int add = (int)_sampleAcc;
                _sampleAcc -= add;
                if (add > 8) add = 8;
                for (int i = 0; i < add; i++) AppendSample();

                _pulse = Math.Max(0, _pulse - dt * 3.2);
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 || h <= 2) return;
            double y0 = h / 2;

            dc.DrawLine(_linePen, new Point(0, y0), new Point(w, y0));

            if (HeartMonitor.Dead)
            {
                if (_showDeathLine)
                {
                    double elapsed = _deathStarted == DateTime.MinValue ? 0 : (DateTime.UtcNow - _deathStarted).TotalSeconds;

                    if (HeartMonitor.DeathHadRealBpm)
                    {
                        double fade = Math.Max(0, 1.0 - elapsed / 0.9);
                        if (fade > 0.01)
                        {
                            dc.PushOpacity(0.75 * fade);
                            DrawHistory(dc, w, h, y0);
                            dc.Pop();
                        }
                    }

                    double lineAlpha = Math.Min(1.0, elapsed / 0.28);
                    if (lineAlpha < 0.01) lineAlpha = 0.01;
                    dc.PushOpacity(lineAlpha);
                    dc.DrawLine(_deathGlowPen, new Point(0, y0), new Point(w, y0));
                    dc.DrawLine(_deathPen, new Point(0, y0), new Point(w, y0));
                    dc.DrawEllipse(_danger, null, new Point(w - 2, y0), 2.2, 2.2);
                    dc.Pop();
                }
                else
                {
                    double fade = Math.Max(0, 1.0 - (_deathStarted == DateTime.MinValue ? 0
                        : (DateTime.UtcNow - _deathStarted).TotalSeconds) / 0.9);
                    if (fade > 0.01)
                    {
                        dc.PushOpacity(0.18 * fade);
                        dc.DrawLine(_linePen, new Point(0, y0), new Point(w, y0));
                        dc.Pop();
                    }
                }
                return;
            }

            if (_count < 2) return;
            if (_pulse > 0.02)
            {
                dc.PushOpacity(0.10 + 0.22 * _pulse);
                DrawHistory(dc, w, h, y0, _glowPen);
                dc.Pop();
            }
            DrawHistory(dc, w, h, y0);
        }

        private void DrawHistory(DrawingContext dc, double w, double h, double y0)
        {
            DrawHistory(dc, w, h, y0, _wavePen);
        }

        private void DrawHistory(DrawingContext dc, double w, double h, double y0, Pen pen)
        {
            double pxPerSample = _pixelsPerSecond / SampleRate;
            int visible = Math.Min(_count, (int)Math.Ceiling(w / pxPerSample) + 1);
            if (visible < 2) return;

            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < visible; i++)
                {
                    int idx = (_head - visible + i + Capacity * 2) % Capacity;
                    double x = w - (visible - 1 - i) * pxPerSample;
                    if (x < 0) continue;
                    double y = y0 - _samples[idx] * h * 0.43 * _amplitudeScale;
                    if (!started)
                    {
                        ctx.BeginFigure(new Point(x, y), false, false);
                        started = true;
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, y), true, false);
                    }
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static double Gauss(double x, double center, double width)
        {
            if (width <= 0) return 0;
            double d = (x - center) / width;
            return Math.Exp(-0.5 * d * d);
        }

        private static double BeatRand(int beat, int salt)
        {
            unchecked
            {
                uint x = (uint)(beat * 374761393 + salt * 668265263);
                x = (x ^ (x >> 13)) * 1274126177u;
                x = x ^ (x >> 16);
                return (x & 0xFFFF) / 65535.0;
            }
        }
    }

    internal sealed class DataBackdrop : FrameworkElement
    {
        private readonly bool _light;
        private readonly Brush _accent;
        private readonly Brush _line;
        private readonly Brush _muted;

        public DataBackdrop(bool light, Brush accent, Brush line, Brush muted)
        {
            _light = light;
            _accent = accent;
            _line = line;
            _muted = muted;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            dc.DrawRectangle(_light ? Solid(235, 241, 247) : Solid(5, 8, 11), null, new Rect(0, 0, w, h));

            GeometryDrawing dot = new GeometryDrawing();
            dot.Brush = new SolidColorBrush(Alpha(_light ? 30 : 28, 90, 130, 150));
            dot.Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
            DrawingBrush grid = new DrawingBrush(dot);
            grid.TileMode = TileMode.Tile;
            grid.Viewport = new Rect(0, 0, 26, 26);
            grid.ViewportUnits = BrushMappingMode.Absolute;
            dc.DrawRectangle(grid, null, new Rect(0, 0, w, h));

            double cx = w * 0.78;
            double cy = h * 0.34;

            if (SCPSLAlert.UiAnim.Glow)
            {
                RadialGradientBrush glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(Alpha(_light ? 14 : 20, 0, 132, 163), 0.4));
                glow.GradientStops.Add(new GradientStop(Alpha(0, 0, 132, 163), 1));
                glow.Freeze();
                dc.DrawEllipse(glow, null, new Point(cx, cy), 420, 420);
            }

            int boost = SCPSLAlert.UiAnim.Quality >= 4 ? 22 : (SCPSLAlert.UiAnim.Quality >= 3 ? 10 : 0);
            Pen ring = new Pen(new SolidColorBrush(Alpha(Math.Min(255, (_light ? 26 : 24) + boost), 0, 132, 163)), 0.9);
            for (int i = 0; i < 7; i++)
                dc.DrawEllipse(null, ring, new Point(cx, cy), 110 + i * 58, 110 + i * 58);
            Pen radial = new Pen(new SolidColorBrush(Alpha(_light ? 18 : 18, 0, 132, 163)), 0.7);
            for (int i = 0; i < 20; i++)
            {
                double a = i * Math.PI / 10;
                dc.DrawLine(radial, new Point(cx + Math.Cos(a) * 110, cy + Math.Sin(a) * 110),
                    new Point(cx + Math.Cos(a) * 480, cy + Math.Sin(a) * 480));
            }

            Typeface tf = new Typeface("Bahnschrift SemiCondensed");
            FormattedText big = new FormattedText("A.R.C.T", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 120, Brushes.Black, 1.0);
            Geometry geo = big.BuildGeometry(new Point(w - 380, h - 230));
            Pen outline = new Pen(new SolidColorBrush(Alpha(_light ? 22 : 30, 0, 132, 163)), 1.4);
            dc.DrawGeometry(null, outline, geo);
        }

        private static Color Alpha(int a, int r, int g, int b)
        {
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }
    }
}










    internal sealed class HoverOutline : FrameworkElement
    {
        private readonly Brush _brush;
        public double Cut = 10;
        private readonly Pen _pen;

        public HoverOutline(Brush brush)
        {
            _brush = brush;
            _pen = new Pen(brush, 2.2);
            _pen.LineJoin = PenLineJoin.Miter;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 4 || h <= 4) return;
            double inset = 1.2;
            double right = w - inset;
            double bottom = h - inset;
            double c = Math.Max(0, Math.Min(Cut, Math.Min(w, h) / 3));
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(inset, inset), false, true);
                ctx.LineTo(new Point(right - c, inset), true, false);
                ctx.LineTo(new Point(right, inset + c), true, false);
                ctx.LineTo(new Point(right, bottom), true, false);
                ctx.LineTo(new Point(inset + c, bottom), true, false);
                ctx.LineTo(new Point(inset, bottom - c), true, false);
                ctx.LineTo(new Point(inset, inset), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, _pen, geo);
        }
    }
    internal sealed class OrbitLayer : FrameworkElement
    {
        private readonly bool _light;
        private readonly Brush _accent;
        private readonly Brush _line;
        private RotateTransform _rotate;

        private AnimationClock _clock;
        private bool _sleeping;

        public OrbitLayer(bool light, Brush accent, Brush line)
        {
            _light = light;
            _accent = accent;
            _line = line;
            IsHitTestVisible = false;
            Loaded += delegate
            {
                _rotate = new RotateTransform(0);
                RenderTransform = _rotate;
                RenderTransformOrigin = new Point(0.78, 0.34);
                if (SCPSLAlert.UiAnim.Off) return;
                double secs = SCPSLAlert.UiAnim.Quality >= 4 ? 90 : (SCPSLAlert.UiAnim.Quality == 3 ? 120 : (SCPSLAlert.UiAnim.Quality == 2 ? 160 : 220));
                DoubleAnimation spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(secs)));
                spin.RepeatBehavior = RepeatBehavior.Forever;
                _clock = spin.CreateClock();
                _rotate.ApplyAnimationClock(RotateTransform.AngleProperty, _clock);
                _clock.Controller.Begin();
            };
        }

        /// <summary>动效档位变化时立刻换转速（不用重建界面）。</summary>
        public void SetQuality(int q)
        {
            if (_rotate == null) return;
            if (_clock != null) { try { _clock.Controller.Stop(); } catch { } _clock = null; }
            try { _rotate.BeginAnimation(RotateTransform.AngleProperty, null); } catch { }
            _rotate.Angle = 0;
            if (SCPSLAlert.UiAnim.Off) return;
            double secs = q >= 4 ? 90 : (q == 3 ? 120 : (q == 2 ? 160 : 220));
            DoubleAnimation spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(secs)));
            spin.RepeatBehavior = RepeatBehavior.Forever;
            _clock = spin.CreateClock();
            _rotate.ApplyAnimationClock(RotateTransform.AngleProperty, _clock);
            _clock.Controller.Begin();
            if (_sleeping) _clock.Controller.Pause();
        }
        /// <summary>第 10 批：休眠 / 唤醒（游戏前台或软件不在前台时停转）。</summary>
        public void SetSleep(bool sleep)
        {
            _sleeping = sleep;
            if (_clock == null) return;
            if (sleep) _clock.Controller.Pause();
            else _clock.Controller.Resume();
        }

        public void Pulse()
        {
            DoubleAnimation pulse = new DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromMilliseconds(520)));
            pulse.AutoReverse = true;
            pulse.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, pulse);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 1 || h <= 1) return;
            double cx = w * 0.78;
            double cy = h * 0.34;

            // 第 10 批：雷达光晕（动效强度 >= 中 才画）
            if (SCPSLAlert.UiAnim.Glow)
            {
                RadialGradientBrush glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(_light ? 20 : 26), 0, 132, 163), 0.35));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 132, 163), 1));
                glow.Freeze();
                dc.DrawEllipse(glow, null, new Point(cx, cy),
                    (SCPSLAlert.UiAnim.Quality >= 4 ? 520 : 430), (SCPSLAlert.UiAnim.Quality >= 4 ? 520 : 430));
            }

            int boost = SCPSLAlert.UiAnim.Quality >= 4 ? 26 : (SCPSLAlert.UiAnim.Quality >= 3 ? 12 : 0);
            Pen thin = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, (_light ? 36 : 44) + boost), 0, 132, 163)), 0.8);
            thin.DashStyle = new DashStyle(new double[] { 2, 10 }, 0);
            for (int i = 0; i < 4; i++)
                dc.DrawEllipse(null, thin, new Point(cx, cy), 240 + i * 62, 240 + i * 62);
            Pen tick = new Pen(new SolidColorBrush(Color.FromArgb((byte)(_light ? 30 : 36), 0, 132, 163)), 0.7);
            for (int i = 0; i < 36; i++)
            {
                double a = i * Math.PI / 18;
                double r1 = 232;
                double r2 = i % 3 == 0 ? 470 : 300;
                dc.DrawLine(tick, new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1),
                    new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2));
            }
        }
    }



























































































































