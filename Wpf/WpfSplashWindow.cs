using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 启动画面（A.R.C.T 风格）。
    ///
    /// 【为什么单独开一个 UI 线程】
    /// 主线程要同步做完配置、热键、OCR、托盘、主窗口等初始化，这段时间里
    /// 如果启动画面也挂在主线程上，动画会被整段初始化卡住，看起来就是
    /// “窗口开了但死住不动”。把启动画面放到独立的 STA 线程，主线程继续
    /// 干活，画面这边照常 60fps 动；初始化完成后再让画面淡出、主窗口淡入。
    /// </summary>
    internal static class StartupSplash
    {
        private static readonly object Gate = new object();

        private static Thread _thread;
        private static WpfSplashWindow _window;
        private static Dispatcher _dispatcher;

        private static bool _started;
        private static bool _exitRequested;
        private static bool _preview;
        private static volatile bool _revealing;

        private static string _pendingStatus;
        private static double _pendingProgress = -1;
        private static int _pendingStage = -1;

        /// <summary>
        /// 启动画面是否已经跑到 100% / READY。主窗口用这个信号决定什么时候淡入，
        /// 避免进度条才走到一半，主界面就已经在周围露出来了。
        /// </summary>
        public static bool IsRevealing
        {
            get { return _revealing; }
        }

        internal static void NotifyRevealStarted()
        {
            _revealing = true;
        }

        /// <summary>启动画面是否还活着（主窗口交接、退出时用）。</summary>
        public static bool IsAlive
        {
            get
            {
                lock (Gate) { return _window != null; }
            }
        }

        /// <summary>正常启动：在后台线程打开启动画面，立即返回。</summary>
        public static void Begin()
        {
            Start(null, true, false);
        }

        /// <summary>设计预览：只打开启动画面，跑一段演示进度，按 Esc 或 30 秒后关闭。</summary>
        public static void Preview(bool? forceLight)
        {
            Start(forceLight, true, true);
        }

        private static void Start(bool? forceLight, bool animations, bool preview)
        {
            lock (Gate)
            {
                if (_started) return;
                _started = true;
                _preview = preview;
                _revealing = false;

                bool light = ResolveLight(forceLight);
                bool animate = animations && ReadAnimationsEnabled();

                _thread = new Thread(delegate() { SplashThread(light, animate, preview); });
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.IsBackground = !preview;
                _thread.Name = "SCPSLAlertSplash";
                _thread.Start();
            }

            // 预览模式阻塞到窗口关闭，方便双击 exe --splash-preview 直接看。
            if (preview)
            {
                try { _thread.Join(); }
                catch { }
            }
        }

        private static void SplashThread(bool light, bool animations, bool preview)
        {
            try
            {
                SplashThreadCore(light, animations, preview);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "SCPSLAlert_splash_error.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
                }
                catch { }
            }
        }

        private static void SplashThreadCore(bool light, bool animations, bool preview)
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            WpfSplashWindow window = new WpfSplashWindow(light, animations, preview);
            window.Closed += delegate
            {
                lock (Gate)
                {
                    _window = null;
                    _dispatcher = null;
                }
                try { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                catch { }
            };

            string text;
            double progress;
            int stage;
            lock (Gate)
            {
                _window = window;
                _dispatcher = dispatcher;
                text = _pendingStatus;
                progress = _pendingProgress;
                stage = _pendingStage;
            }

            try { window.Show(); }
            catch { return; }

            if (text != null || progress >= 0 || stage >= 0)
            {
                try { window.SetStatus(text, progress, stage); }
                catch { }
            }

            try { Dispatcher.Run(); }
            catch { }
        }

        /// <summary>更新启动进度；必须允许从任意线程调用。</summary>
        public static void Status(string text, double progress, int stage)
        {
            WpfSplashWindow window;
            lock (Gate)
            {
                _pendingStatus = text;
                if (progress >= 0) _pendingProgress = progress;
                if (stage >= 0) _pendingStage = stage;
                window = _window;
                if (window == null) return;
            }

            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    try { window.SetStatus(text, progress, stage); }
                    catch { }
                }));
            }
            catch { }
        }

        /// <summary>
        /// 后台初始化已经完成。启动画面仍会把四个阶段按固定节奏走完，
        /// 再显示 READY / 100% 并淡出，避免“一打开就只剩最后一个格子”。
        /// </summary>
        public static void HandOff()
        {
            WpfSplashWindow window;
            lock (Gate)
            {
                if (_exitRequested) return;
                _exitRequested = true;
                window = _window;
            }
            if (window == null) return;

            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    try { window.MarkBootComplete(); }
                    catch { }
                }));
            }
            catch { }
        }

        /// <summary>启动失败 / 进程退出时立即收掉，避免盖住错误框。</summary>
        public static void CloseNow()
        {
            WpfSplashWindow window;
            lock (Gate) { window = _window; }
            if (window == null) return;

            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                {
                    try { window.CloseImmediate(); }
                    catch { }
                }));
            }
            catch { }
        }

        // ------------------------------------------------------------
        // 主题 / 动画开关：在 Config.Load 之前先轻量读一下 config.ini，
        // 这样启动画面一出来就是用户最终看到的亮 / 暗主题，不会先闪错颜色。
        // ------------------------------------------------------------

        private static bool ResolveLight(bool? forceLight)
        {
            if (forceLight.HasValue) return forceLight.Value;

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (File.Exists(path))
                {
                    bool inAppearance = false;
                    string[] lines = File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string s = lines[i].Trim();
                        if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("#")) continue;
                        if (s.StartsWith("["))
                        {
                            inAppearance = s.Equals("[Appearance]", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        if (!inAppearance) continue;

                        int eq = s.IndexOf('=');
                        if (eq <= 0) continue;
                        if (!s.Substring(0, eq).Trim().Equals("ThemeMode", StringComparison.OrdinalIgnoreCase))
                            continue;

                        int mode;
                        if (int.TryParse(s.Substring(eq + 1).Trim(), out mode))
                        {
                            if (mode == 0) return true;
                            if (mode == 1) return false;
                        }
                        break;
                    }
                }
            }
            catch { }

            try { return !ModernTheme.DetectDark(); }
            catch { return false; }
        }

        private static bool ReadAnimationsEnabled()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(path)) return true;
                bool inAppearance = false;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string s = lines[i].Trim();
                    if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("#")) continue;
                    if (s.StartsWith("["))
                    {
                        inAppearance = s.Equals("[Appearance]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inAppearance) continue;

                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!s.Substring(0, eq).Trim().Equals("AnimationsEnabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string v = s.Substring(eq + 1).Trim();
                    return !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
            return true;
        }
    }

    /// <summary>A.R.C.T 风格启动画面。只负责画和动，生命周期由 StartupSplash 管。</summary>
    internal sealed class WpfSplashWindow : Window
    {
        private const int MinVisibleMs = 1150;
        private const double CardCut = 16;
        private const double EmblemSize = 150;

        private static readonly FontFamily Ui = UiFonts.Body;
        private static readonly FontFamily Display =
            UiFonts.Title;
        private static readonly FontFamily Mono = UiFonts.Mono;
        private static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private readonly bool _light;
        private readonly bool _animations;
        private readonly bool _preview;

        private Color _bgColor, _panelColor, _panelAltColor, _lineColor, _inkColor;
        private Color _subColor, _mutedColor, _accentColor, _accent2Color;

        private SolidColorBrush _bg, _panel, _panelAlt, _line, _ink, _sub, _muted, _accent, _accent2;

        private Grid _root;
        private Grid _card;
        private SplashFrame _frame;
        private SplashField _field;
        private SplashOrbits _orbits;
        private SplashParticles _particles;
        private SplashEmblemCore _emblemCore;

        private Grid _topRail;
        private Grid _hero;
        private StackPanel _emblemHost;
        private UIElement _overline;
        private StackPanel _heroText;
        private UIElement _subLine;
        private UIElement _tagline;
        private Grid _bottom;

        private SplashProgress _progress;
        private TextBlock _statusText;
        private TextBlock _percentText;
        private readonly Border[] _stageBars = new Border[4];
        private readonly TextBlock[] _stageTexts = new TextBlock[4];
        private Border _activeBar;
        private int _lastPercent = -1;

        private DateTime _shownAt = DateTime.MinValue;
        private bool _exitScheduled;
        private bool _exiting;
        private DispatcherTimer _previewClose;

        // 启动阶段节奏：每个格子至少停 StageDwellMs，避免初始化太快时一打开就到最后。
        private const int StageDwellMs = 850;
        private const int ReadyHoldMs = 280;
        private DispatcherTimer _stageTimer;
        private int _stage = -1;
        private bool _bootDone;
        private bool _finishing;
        private readonly string[] _stageLiveText = new string[4];

        private Stopwatch _clock = new Stopwatch();

        public WpfSplashWindow(bool light, bool animations, bool preview)
        {
            _light = light;
            _animations = animations;
            _preview = preview;

            BuildPalette();

            Title = "SCPSLAlert // A.R.C.T";
            Width = 820;
            Height = 480;
            WindowStyle = System.Windows.WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = preview;
            Focusable = preview;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Build();

            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) RequestExit(0);
            };
        }

        // ============================================================
        // 色板：和 WpfVigilDataWindow.BuildPalette 完全一致，
        // 主界面的深色 / 浅色是什么颜色，启动画面就是什么颜色。
        // ============================================================
        private void BuildPalette()
        {
            if (_light)
            {
                _bgColor = Color.FromRgb(235, 241, 247);
                _panelColor = Color.FromRgb(255, 255, 255);
                _panelAltColor = Color.FromRgb(242, 246, 250);
                _lineColor = Color.FromRgb(204, 216, 227);
                _inkColor = Color.FromRgb(10, 23, 32);
                _subColor = Color.FromRgb(74, 93, 108);
                _mutedColor = Color.FromRgb(132, 150, 164);
                _accentColor = Color.FromRgb(0, 132, 163);
                _accent2Color = Color.FromRgb(11, 99, 206);
            }
            else
            {
                _bgColor = Color.FromRgb(7, 11, 16);
                _panelColor = Color.FromRgb(12, 18, 25);
                _panelAltColor = Color.FromRgb(16, 26, 36);
                _lineColor = Color.FromRgb(27, 43, 54);
                _inkColor = Color.FromRgb(230, 244, 248);
                _subColor = Color.FromRgb(154, 176, 188);
                _mutedColor = Color.FromRgb(92, 116, 128);
                _accentColor = Color.FromRgb(56, 214, 255);
                _accent2Color = Color.FromRgb(47, 123, 255);
            }

            _bg = Solid(_bgColor);
            _panel = Solid(_panelColor);
            _panelAlt = Solid(_panelAltColor);
            _line = Solid(_lineColor);
            _ink = Solid(_inkColor);
            _sub = Solid(_subColor);
            _muted = Solid(_mutedColor);
            _accent = Solid(_accentColor);
            _accent2 = Solid(_accent2Color);
        }

        private static SolidColorBrush Solid(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static SolidColorBrush Alpha(Color c, byte a)
        {
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        // ============================================================
        // 布局
        // ============================================================
        private void Build()
        {
            _root = new Grid();
            _root.Margin = new Thickness(30);
            Content = _root;

            _frame = new SplashFrame(_light, _bgColor, _panelAltColor, _lineColor, _accentColor, _accent2Color, _mutedColor);
            _root.Children.Add(_frame);

            _card = new Grid();
            _card.Background = Brushes.Transparent;
            _card.ClipToBounds = true;
            _card.SizeChanged += delegate
            {
                _card.Clip = CutGeometry(Math.Max(1, _card.ActualWidth), Math.Max(1, _card.ActualHeight), CardCut);
            };

            _field = new SplashField(_light, _bgColor, _lineColor, _inkColor, _accentColor, _accent2Color, _mutedColor);
            _field.Opacity = 0;
            _card.Children.Add(_field);

            _orbits = new SplashOrbits(_light, _lineColor, _accentColor, _accent2Color);
            _card.Children.Add(_orbits);

            _particles = new SplashParticles(_accentColor, _accent2Color);
            _card.Children.Add(_particles);

            _card.Children.Add(BuildForeground());
            _root.Children.Add(_card);
        }

        private static Geometry CutGeometry(double w, double h, double cut)
        {
            double c = Math.Max(0, Math.Min(cut, Math.Min(w, h) / 3.0));
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w - c, 0), true, false);
                ctx.LineTo(new Point(w, c), true, false);
                ctx.LineTo(new Point(w, h), true, false);
                ctx.LineTo(new Point(c, h), true, false);
                ctx.LineTo(new Point(0, h - c), true, false);
            }
            geo.Freeze();
            return geo;
        }

        private UIElement BuildForeground()
        {
            Grid fg = new Grid();
            fg.Margin = new Thickness(34, 24, 34, 20);
            fg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            fg.RowDefinitions.Add(new RowDefinition());
            fg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(126) });

            _topRail = BuildTopRail();
            fg.Children.Add(_topRail);

            _hero = BuildHero();
            Grid.SetRow(_hero, 1);
            fg.Children.Add(_hero);

            _bottom = BuildBottom();
            Grid.SetRow(_bottom, 2);
            fg.Children.Add(_bottom);

            return fg;
        }

        private Grid BuildTopRail()
        {
            Grid rail = new Grid();
            rail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rail.ColumnDefinitions.Add(new ColumnDefinition());
            rail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;

            Border mark = new Border();
            mark.Width = 7;
            mark.Height = 7;
            mark.Background = _accent;
            mark.Margin = new Thickness(0, 0, 9, 0);
            mark.RenderTransform = new RotateTransform(45);
            mark.RenderTransformOrigin = new Point(0.5, 0.5);
            left.Children.Add(mark);

            TextBlock vigil = MonoText("A.R.C.T", 9.5, _accent);
            vigil.FontWeight = FontWeights.Bold;
            left.Children.Add(vigil);

            TextBlock sys = MonoText("  //  TACTICAL ALERT SYSTEM", 8, _muted);
            sys.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(sys);
            rail.Children.Add(left);

            TextBlock center = MonoText("BOOT SEQUENCE  /  04 STAGES", 8, _muted);
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(center, 1);
            rail.Children.Add(center);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(MonoText(_light ? "LIGHT" : "DARK", 8, _accent2));
            right.Children.Add(MonoText("  /  SECURE BOOT  /  2026.10", 8, _muted));
            Grid.SetColumn(right, 2);
            rail.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = _line;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 3);
            rail.Children.Add(line);

            rail.Opacity = 0;
            return rail;
        }

        private Grid BuildHero()
        {
            Grid hero = new Grid();
            hero.VerticalAlignment = VerticalAlignment.Center;
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
            hero.ColumnDefinitions.Add(new ColumnDefinition());

            _emblemHost = new StackPanel();
            _emblemHost.HorizontalAlignment = HorizontalAlignment.Center;
            _emblemHost.VerticalAlignment = VerticalAlignment.Center;
            _emblemHost.Opacity = 0;

            Grid emblem = new Grid();
            emblem.Width = EmblemSize;
            emblem.Height = EmblemSize;
            _emblemCore = new SplashEmblemCore(_light, _lineColor, _accentColor, _accent2Color, _mutedColor);
            emblem.Children.Add(_emblemCore);
            emblem.Children.Add(new SplashEmblemOrbit(_light, _lineColor, _accentColor, _accent2Color, EmblemSize));
            _emblemHost.Children.Add(emblem);

            TextBlock cap = MonoText("A.R.C.T  /  CORE 01", 7.5, _muted);
            cap.HorizontalAlignment = HorizontalAlignment.Center;
            cap.Margin = new Thickness(0, 2, 0, 0);
            _emblemHost.Children.Add(cap);
            hero.Children.Add(_emblemHost);

            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(26, 0, 0, 0);

            StackPanel overline = new StackPanel();
            overline.Orientation = Orientation.Horizontal;
            overline.Opacity = 0;
            Border overMark = new Border();
            overMark.Width = 16;
            overMark.Height = 2;
            overMark.Background = _accent;
            overMark.VerticalAlignment = VerticalAlignment.Center;
            overMark.Margin = new Thickness(0, 0, 8, 0);
            overline.Children.Add(overMark);
            overline.Children.Add(MonoText("TACTICAL ALERT SYSTEM", 8.5, _accent));
            TextBlock overSlash = MonoText("   //   " + AppInfo.Brand + " " + AppInfo.Version, 8.5, _muted);
            overSlash.VerticalAlignment = VerticalAlignment.Center;
            overline.Children.Add(overSlash);
            text.Children.Add(overline);
            _overline = overline;

            StackPanel title = new StackPanel();
            title.Orientation = Orientation.Horizontal;
            title.Opacity = 0;
            title.Margin = new Thickness(0, 4, 0, 0);

            TextBlock brandA = DisplayText("SCPSL", 60, _ink);
            brandA.FontWeight = FontWeights.Bold;
            title.Children.Add(brandA);
            TextBlock brandB = DisplayText("Alert", 60, _accent);
            brandB.FontWeight = FontWeights.Bold;
            title.Children.Add(brandB);
            text.Children.Add(title);
            _heroText = title;

            Border underline = new Border();
            underline.Width = 92;
            underline.Height = 2;
            underline.HorizontalAlignment = HorizontalAlignment.Left;
            LinearGradientBrush ug = new LinearGradientBrush(_accent2Color, _accentColor, 0);
            ug.Freeze();
            underline.Background = ug;
            underline.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(underline);

            StackPanel sub = new StackPanel();
            sub.Orientation = Orientation.Horizontal;
            sub.Margin = new Thickness(0, 12, 0, 0);
            sub.Opacity = 0;
            TextBlock cn = new TextBlock();
            cn.Text = "战术提醒终端";
            cn.FontFamily = Ui;
            cn.FontSize = 20;
            cn.FontWeight = FontWeights.SemiBold;
            cn.Foreground = _ink;
            sub.Children.Add(cn);
            TextBlock subEn = new TextBlock();
            subEn.Text = "   实时态势  ·  自动预警  ·  战术面板";
            subEn.FontFamily = Ui;
            subEn.FontSize = 10.5;
            subEn.Foreground = _sub;
            subEn.VerticalAlignment = VerticalAlignment.Bottom;
            subEn.Margin = new Thickness(0, 0, 0, 3);
            sub.Children.Add(subEn);
            text.Children.Add(sub);
            _subLine = sub;

            StackPanel tag = new StackPanel();
            tag.Orientation = Orientation.Horizontal;
            tag.Margin = new Thickness(0, 12, 0, 0);
            tag.Opacity = 0;
            tag.Children.Add(MonoText("READ THE FIELD", 8, _muted));
            tag.Children.Add(MonoText("  /  BEFORE IT MOVES  /  STAY AHEAD", 8, _muted));
            text.Children.Add(tag);
            _tagline = tag;

            Grid.SetColumn(text, 1);
            hero.Children.Add(text);

            return hero;
        }

        private Grid BuildBottom()
        {
            Grid bottom = new Grid();
            bottom.Opacity = 0;
            bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

            // ---- 状态行 ----
            Grid statusRow = new Grid();
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition());
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel boot = new StackPanel();
            boot.Orientation = Orientation.Horizontal;
            boot.VerticalAlignment = VerticalAlignment.Center;
            Border dot = new Border();
            dot.Width = 5;
            dot.Height = 5;
            dot.Background = _accent;
            dot.Margin = new Thickness(0, 0, 8, 0);
            dot.RenderTransform = new RotateTransform(45);
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            boot.Children.Add(dot);
            boot.Children.Add(MonoText("BOOT  /  03", 8, _accent));
            statusRow.Children.Add(boot);

            _statusText = new TextBlock();
            _statusText.Text = "正在启动核心…";
            _statusText.FontFamily = Ui;
            _statusText.FontSize = 11;
            _statusText.Foreground = _sub;
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            _statusText.Margin = new Thickness(20, 0, 0, 0);
            _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_statusText, 1);
            statusRow.Children.Add(_statusText);

            StackPanel percent = new StackPanel();
            percent.Orientation = Orientation.Horizontal;
            percent.VerticalAlignment = VerticalAlignment.Center;
            percent.HorizontalAlignment = HorizontalAlignment.Right;
            _percentText = DisplayText("0", 25, _accent);
            _percentText.FontWeight = FontWeights.Bold;
            _percentText.Width = 38;
            _percentText.TextAlignment = TextAlignment.Right;
            percent.Children.Add(_percentText);
            TextBlock pctSign = MonoText("%", 8.5, _muted);
            pctSign.VerticalAlignment = VerticalAlignment.Bottom;
            pctSign.Margin = new Thickness(1, 0, 0, 4);
            percent.Children.Add(pctSign);
            Grid.SetColumn(percent, 2);
            statusRow.Children.Add(percent);

            bottom.Children.Add(statusRow);

            // ---- 进度线 ----
            _progress = new SplashProgress(_light, _lineColor, _accentColor, _accent2Color);
            _progress.Height = 12;
            _progress.Margin = new Thickness(0, 2, 0, 0);
            _progress.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_progress, 1);
            bottom.Children.Add(_progress);

            // ---- 四个阶段 ----
            Grid stages = new Grid();
            for (int i = 0; i < 4; i++)
                stages.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels = new string[] { "01  配置 / CONFIG", "02  监控 / MONITOR", "03  识别 / OCR", "04  界面 / INTERFACE" };
            for (int i = 0; i < 4; i++)
            {
                StackPanel stage = new StackPanel();
                stage.Margin = new Thickness(0, 0, i < 3 ? 14 : 0, 0);
                Border bar = new Border();
                bar.Height = 2;
                bar.Background = _line;
                bar.Margin = new Thickness(0, 0, 0, 5);
                stage.Children.Add(bar);
                _stageBars[i] = bar;

                TextBlock label = MonoText(labels[i], 7.5, _muted);
                stage.Children.Add(label);
                _stageTexts[i] = label;

                Grid.SetColumn(stage, i);
                stages.Children.Add(stage);
            }
            Grid.SetRow(stages, 2);
            bottom.Children.Add(stages);

            // ---- 底部微行 ----
            Grid micro = new Grid();
            TextBlock left = MonoText("SCPSLALERT.EXE  /  BOOT CHANNEL 01  /  " + (_light ? "LIGHT" : "DARK"), 7, _muted);
            left.VerticalAlignment = VerticalAlignment.Center;
            micro.Children.Add(left);
            TextBlock right = MonoText("BUILD 2026.10  /  WPF  /  A.R.C.T", 7, _muted);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            micro.Children.Add(right);
            Grid.SetRow(micro, 3);
            bottom.Children.Add(micro);

            return bottom;
        }

        // ============================================================
        // 生命周期 / 动画
        // ============================================================
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _shownAt = DateTime.UtcNow;
            _clock.Start();
            CompositionTarget.Rendering += OnFrame;

            BeginIntro();

            if (_preview) StartPreview();
            else StartStagePacing();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            if (_stageTimer != null) { _stageTimer.Stop(); _stageTimer = null; }
            if (_fillTimer != null) { _fillTimer.Stop(); _fillTimer = null; }
            if (_previewClose != null) { _previewClose.Stop(); _previewClose = null; }
            _clock.Stop();
        }

        private void BeginIntro()
        {
            if (!_animations)
            {
                SetAllVisible();
                return;
            }

            FadeIn(_frame, 0, 320, 0, 1, 0);
            FadeIn(_field, 0, 900, 0, 1, 0);
            FadeIn(_topRail, 0, 300, -6, 1, 0);
            FadeIn(_emblemHost, 80, 760, 0, 0.86, -18);
            FadeIn(_overline, 190, 480, 8, 1, 0);
            FadeIn(_heroText, 250, 700, 24, 1, 0);
            FadeIn(_subLine, 380, 500, 10, 1, 0);
            FadeIn(_tagline, 460, 500, 8, 1, 0);
            FadeIn(_bottom, 560, 540, 12, 1, 0);
        }

        private void SetAllVisible()
        {
            _frame.Opacity = 1;
            _field.Opacity = 1;
            _topRail.Opacity = 1;
            _emblemHost.Opacity = 1;
            _overline.Opacity = 1;
            _heroText.Opacity = 1;
            _subLine.Opacity = 1;
            _tagline.Opacity = 1;
            _bottom.Opacity = 1;
            _progress.Opacity = 1;
        }

        private void FadeIn(UIElement element, int delayMs, int durationMs, double fromY, double fromScale, double fromRotate)
        {
            if (element == null) return;
            if (!_animations) { element.Opacity = 1; return; }

            element.Opacity = 0;
            TransformGroup group = new TransformGroup();
            ScaleTransform scale = new ScaleTransform(fromScale, fromScale);
            RotateTransform rotate = new RotateTransform(fromRotate);
            TranslateTransform move = new TranslateTransform(0, fromY);
            group.Children.Add(scale);
            group.Children.Add(rotate);
            group.Children.Add(move);
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            TimeSpan delay = TimeSpan.FromMilliseconds(delayMs);
            Duration duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation fade = new DoubleAnimation(0, 1, duration);
            fade.BeginTime = delay;
            fade.EasingFunction = ease;
            element.BeginAnimation(UIElement.OpacityProperty, fade);

            if (fromY != 0)
            {
                DoubleAnimation y = new DoubleAnimation(fromY, 0, duration);
                y.BeginTime = delay;
                y.EasingFunction = ease;
                move.BeginAnimation(TranslateTransform.YProperty, y);
            }
            if (fromScale != 1)
            {
                DoubleAnimation sx = new DoubleAnimation(fromScale, 1, duration);
                sx.BeginTime = delay;
                sx.EasingFunction = ease;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                DoubleAnimation sy = new DoubleAnimation(fromScale, 1, duration);
                sy.BeginTime = delay;
                sy.EasingFunction = ease;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            }
            if (fromRotate != 0)
            {
                DoubleAnimation ra = new DoubleAnimation(fromRotate, 0, duration);
                ra.BeginTime = delay;
                ra.EasingFunction = ease;
                rotate.BeginAnimation(RotateTransform.AngleProperty, ra);
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double dt = _clock.Elapsed.TotalSeconds;
            _clock.Restart();
            if (dt <= 0 || dt > 0.1) dt = 0.016;

            if (_progress != null)
            {
                _progress.Tick(dt);

                int pct = (int)Math.Round(_progress.Display * 100);
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                if (pct != _lastPercent && _percentText != null)
                {
                    _lastPercent = pct;
                    _percentText.Text = pct.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (_particles != null) _particles.Tick(dt);
        }

        // ============================================================
        // 外部推进：只记录真实状态文字，进度与阶段由启动画面自己按节奏走。
        // 这样即使后台 0.9 秒就初始化完，四个格子也会依次亮满再进入主界面。
        // ============================================================
        public void SetStatus(string text, double progress, int stage)
        {
            if (stage >= 0 && stage < 4 && !string.IsNullOrEmpty(text))
                _stageLiveText[stage] = text;

            if (stage >= 0 && stage == _stage && !_finishing && !string.IsNullOrEmpty(text))
                AnimateStatusText(text);
        }

        private void AnimateStatusText(string text)
        {
            if (_statusText == null || text == _statusText.Text) return;
            _statusText.Text = text;
            if (!_animations) return;

            _statusText.BeginAnimation(UIElement.OpacityProperty, null);
            _statusText.Opacity = 0;
            TranslateTransform move = _statusText.RenderTransform as TranslateTransform;
            if (move == null)
            {
                move = new TranslateTransform(0, 4);
                _statusText.RenderTransform = move;
            }
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.Y = 4;

            DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _statusText.BeginAnimation(UIElement.OpacityProperty, fade);
            DoubleAnimation rise = new DoubleAnimation(4, 0, new Duration(TimeSpan.FromMilliseconds(240)));
            rise.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            move.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        private void SetStage(int stage)
        {
            int s = stage;
            if (s < 0) s = 0;
            if (s > 3) s = 3;
            if (s == _stage) return;
            _stage = s;

            if (_activeBar != null)
            {
                _activeBar.BeginAnimation(UIElement.OpacityProperty, null);
                _activeBar.Opacity = 1;
                _activeBar = null;
            }

            for (int i = 0; i < 4; i++)
            {
                if (_stageBars[i] == null) continue;
                if (i < s)
                {
                    _stageBars[i].Background = _accent2;
                    _stageTexts[i].Foreground = _sub;
                }
                else if (i == s)
                {
                    _stageBars[i].Background = _accent;
                    _stageTexts[i].Foreground = _accent;
                    _activeBar = _stageBars[i];
                }
                else
                {
                    _stageBars[i].Background = _line;
                    _stageTexts[i].Foreground = _muted;
                }
            }

            if (_activeBar != null && _animations)
            {
                DoubleAnimation pulse = new DoubleAnimation(0.35, 1.0, new Duration(TimeSpan.FromMilliseconds(900)));
                pulse.AutoReverse = true;
                pulse.RepeatBehavior = RepeatBehavior.Forever;
                pulse.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                _activeBar.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
        }

        public void RequestExit(int delayMs)
        {
            if (_exitScheduled) return;
            _exitScheduled = true;

            if (_progress != null) _progress.Target = 1.0;

            double elapsed = _shownAt == DateTime.MinValue ? 0 : (DateTime.UtcNow - _shownAt).TotalMilliseconds;
            int wait = Math.Max(delayMs, (int)(MinVisibleMs - elapsed));
            if (wait < 0) wait = 0;

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(wait);
            timer.Tick += delegate
            {
                timer.Stop();
                BeginFadeOut();
            };
            timer.Start();
        }

        // ============================================================
        // 固定节奏的四阶段启动：每个格子至少亮 StageDwellMs。
        // 实际初始化可能 0.9 秒就结束了，但画面仍会把 01→02→03→04 走完，
        // 进度条分别停在 25% / 50% / 75% / 96%，再进入 READY。
        // ============================================================
        private void StartStagePacing()
        {
            ApplyStage(0);

            _stageTimer = new DispatcherTimer();
            _stageTimer.Interval = TimeSpan.FromMilliseconds(StageDwellMs);
            _stageTimer.Tick += delegate
            {
                if (_finishing) return;
                int next = _stage + 1;
                if (next > 3)
                {
                    _stageTimer.Stop();
                    MaybeFinish();
                    return;
                }
                ApplyStage(next);
                if (next >= 3)
                {
                    _stageTimer.Stop();
                    MaybeFinish();
                }
            };
            _stageTimer.Start();
        }

        private void ApplyStage(int stage)
        {
            if (stage < 0) stage = 0;
            if (stage > 3) stage = 3;

            SetStage(stage);

            string text = _stageLiveText[stage];
            if (string.IsNullOrEmpty(text)) text = DefaultStageText(stage);
            AnimateStatusText(text);

            double target = stage == 0 ? 0.25
                : stage == 1 ? 0.50
                : stage == 2 ? 0.75
                : 0.96;
            if (_progress != null) _progress.Target = Math.Max(_progress.Target, target);
        }

        private static string DefaultStageText(int stage)
        {
            switch (stage)
            {
                case 0: return "正在载入配置与界面主题…";
                case 1: return "正在启动后台监控…";
                case 2: return "正在唤醒屏幕识别引擎…";
                default: return "正在构建状态面板…";
            }
        }

        /// <summary>后台初始化完成：走完最后一个格子后进入 READY。</summary>
        public void MarkBootComplete()
        {
            _bootDone = true;
            MaybeFinish();
        }

        private DispatcherTimer _fillTimer;

        /// <summary>
        /// 前三个格子固定停留；到第四个格子时如果后台已经初始化完，
        /// 进度条直接滑到 100% 再让主界面出来。
        /// 如果还没初始化完，就停在第四格等真实完成，完成后再滑到 100%。
        /// </summary>
        private void MaybeFinish()
        {
            if (_finishing || !_bootDone) return;
            if (_stage < 3) return;

            _finishing = true;
            if (_stageTimer != null) { _stageTimer.Stop(); _stageTimer = null; }
            AnimateStatusText("正在完成最后一步…");
            if (_progress != null) _progress.Target = 1.0;

            if (!_animations)
            {
                FinishFill();
                return;
            }

            DateTime started = DateTime.UtcNow;
            _fillTimer = new DispatcherTimer(DispatcherPriority.Render);
            _fillTimer.Interval = TimeSpan.FromMilliseconds(30);
            _fillTimer.Tick += delegate
            {
                double p = _progress != null ? _progress.Display : 1.0;
                double ms = (DateTime.UtcNow - started).TotalMilliseconds;
                if (p >= 0.993 || ms > 1200)
                {
                    _fillTimer.Stop();
                    _fillTimer = null;
                    FinishFill();
                }
            };
            _fillTimer.Start();
        }

        private void FinishFill()
        {
            if (_progress != null)
            {
                _progress.Target = 1.0;
                _progress.Display = 1.0;
                _progress.InvalidateVisual();
            }
            if (_percentText != null) _percentText.Text = "100";

            AnimateStatusText("READY  /  正在进入主界面…");
            // 条真的到 100% 了，才让主窗口显示/显示出来。
            StartupSplash.NotifyRevealStarted();

            DispatcherTimer hold = new DispatcherTimer();
            hold.Interval = TimeSpan.FromMilliseconds(ReadyHoldMs);
            hold.Tick += delegate
            {
                hold.Stop();
                BeginFadeOut();
            };
            hold.Start();
        }

        private void BeginFadeOut()
        {
            if (_exiting) return;
            _exiting = true;

            if (_progress != null) _progress.Target = 1.0;

            if (!_animations)
            {
                try { Close(); }
                catch { }
                return;
            }

            if (_card != null)
            {
                _card.RenderTransformOrigin = new Point(0.5, 0.5);
                ScaleTransform scale = _card.RenderTransform as ScaleTransform;
                if (scale == null)
                {
                    scale = new ScaleTransform(1, 1);
                    _card.RenderTransform = scale;
                }
                DoubleAnimation grow = new DoubleAnimation(1.0, 1.018, new Duration(TimeSpan.FromMilliseconds(520)));
                grow.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }

            DoubleAnimation fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(460)));
            fade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            fade.Completed += delegate
            {
                try { Close(); }
                catch { }
            };
            BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public void CloseImmediate()
        {
            _exiting = true;
            try { Close(); }
            catch { }
        }

        private void StartPreview()
        {
            // 预览模式：走同一套四阶段节奏，但不会收到 MarkBootComplete，
            // 所以会停在 96% / 04 界面，按 Esc 或 30 秒后关闭。
            StartStagePacing();

            _previewClose = new DispatcherTimer();
            _previewClose.Interval = TimeSpan.FromSeconds(30);
            _previewClose.Tick += delegate
            {
                _previewClose.Stop();
                RequestExit(0);
            };
            _previewClose.Start();
        }

        // ---- 小的文本工厂 ----
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

    /// <summary>
    /// 设计稿离屏渲染：不依赖可见桌面，直接把启动画面渲染成 PNG。
    /// 用法：SCPSLAlert.exe --splash-render [输出目录]
    /// </summary>
    internal static class SplashDesignCapture
    {
        public static void Run(string outputDir)
        {
            try { RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly; }
            catch { }

            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_preview");
            Directory.CreateDirectory(outputDir);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            RenderTheme(false, outputDir, delegate
            {
                RenderTheme(true, outputDir, delegate
                {
                    try { app.Shutdown(); }
                    catch { }
                });
            });

            app.Run();
        }

        private static void RenderTheme(bool light, string dir, Action done)
        {
            WpfSplashWindow window = new WpfSplashWindow(light, true, false);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            window.Show();

            string name = light ? "light" : "dark";

            Later(900, delegate
            {
                try { window.SetStatus("正在唤醒屏幕识别引擎…", 0.62, 2); }
                catch { }
            });

            Later(700, delegate { Capture(window, Path.Combine(dir, "splash-" + name + "-intro.png")); });
            Later(2200, delegate { Capture(window, Path.Combine(dir, "splash-" + name + ".png")); });
            Later(2500, delegate
            {
                try { window.Close(); }
                catch { }
                if (done != null) done();
            });
        }

        private static void Capture(Window window, string path)
        {
            try
            {
                window.UpdateLayout();
                int width = (int)Math.Max(1, Math.Ceiling(window.ActualWidth));
                int height = (int)Math.Max(1, Math.Ceiling(window.ActualHeight));
                RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(window);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (FileStream fs = File.Create(path)) enc.Save(fs);

                // 同时保存一张白底 / 深底预览，方便直接看透明阴影的效果。
                SaveFlat(bmp, path, Color.FromRgb(255, 255, 255));
                SaveFlat(bmp, path, Color.FromRgb(16, 19, 23));
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "SCPSLAlert_splash_error.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " capture: " + ex + "\r\n\r\n");
                }
                catch { }
            }
        }

        private static void SaveFlat(RenderTargetBitmap source, string path, Color back)
        {
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(back), null,
                    new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }
            RenderTargetBitmap flat = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight,
                96, 96, PixelFormats.Pbgra32);
            flat.Render(visual);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(flat));

            string suffix = back.R > 200 ? "-white.png" : "-dark.png";
            string file = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + suffix);
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }

        private static void Later(int ms, Action action)
        {
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ms);
            timer.Tick += delegate
            {
                timer.Stop();
                try { action(); }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "SCPSLAlert_splash_error.txt"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " later: " + ex + "\r\n\r\n");
                    }
                    catch { }
                }
            };
            timer.Start();
        }
    }

    // ================================================================
    // 背景 / 几何元素
    // ================================================================

    /// <summary>切角卡片底板 + 细边框 + 投影。几何形状和 A.R.C.T 卡片一致。</summary>
    internal sealed class SplashFrame : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _bg, _panelAlt, _line, _accent, _accent2, _muted;

        public SplashFrame(bool light, Color bg, Color panelAlt, Color line, Color accent, Color accent2, Color muted)
        {
            _light = light;
            _bg = bg;
            _panelAlt = panelAlt;
            _line = line;
            _accent = accent;
            _accent2 = accent2;
            _muted = muted;
            IsHitTestVisible = false;

            BitmapCache cache = new BitmapCache();
            cache.RenderAtScale = 1.0;
            cache.SnapsToDevicePixels = true;
            CacheMode = cache;

            DropShadowEffect shadow = new DropShadowEffect();
            shadow.Color = Colors.Black;
            shadow.BlurRadius = _light ? 24 : 32;
            shadow.ShadowDepth = _light ? 7 : 10;
            shadow.Direction = 270;
            shadow.Opacity = _light ? 0.18 : 0.56;
            Effect = shadow;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            double cut = Math.Max(0, Math.Min(16, Math.Min(w, h) / 3.0));
            Geometry geo = WpfSplashWindowCut(w, h, cut);

            LinearGradientBrush fill = new LinearGradientBrush();
            fill.StartPoint = new Point(0, 0);
            fill.EndPoint = new Point(0.7, 1);
            fill.GradientStops.Add(new GradientStop(_bg, 0));
            fill.GradientStops.Add(new GradientStop(Blend(_bg, _panelAlt, 0.55), 0.55));
            fill.GradientStops.Add(new GradientStop(_bg, 1));
            fill.Freeze();

            Pen border = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)220 : (byte)255, _line.R, _line.G, _line.B)), 1);
            border.Freeze();
            dc.DrawGeometry(fill, border, geo);

            // 顶部 / 底部强调线：让卡片一打开就有“终端”识别度
            Pen topAccent = new Pen(new SolidColorBrush(Color.FromArgb(230, _accent.R, _accent.G, _accent.B)), 2);
            topAccent.Freeze();
            dc.DrawLine(topAccent, new Point(1, 1), new Point(96, 1));

            Pen bottomAccent = new Pen(new SolidColorBrush(Color.FromArgb(210, _accent2.R, _accent2.G, _accent2.B)), 2);
            bottomAccent.Freeze();
            dc.DrawLine(bottomAccent, new Point(w - 62, h - 1), new Point(w - 1, h - 1));

            // 四角刻度
            Pen tick = new Pen(new SolidColorBrush(Color.FromArgb(150, _muted.R, _muted.G, _muted.B)), 1);
            tick.Freeze();
            dc.DrawLine(tick, new Point(14, 12), new Point(34, 12));
            dc.DrawLine(tick, new Point(12, 14), new Point(12, 34));
            dc.DrawLine(tick, new Point(w - 14, h - 12), new Point(w - 34, h - 12));
            dc.DrawLine(tick, new Point(w - 12, h - 14), new Point(w - 12, h - 34));

            Pen accentTick = new Pen(new SolidColorBrush(Color.FromArgb(220, _accent.R, _accent.G, _accent.B)), 1.2);
            accentTick.Freeze();
            dc.DrawLine(accentTick, new Point(w - 14, 12), new Point(w - 34, 12));
            dc.DrawLine(accentTick, new Point(w - 12, 14), new Point(w - 12, 34));
        }

        private static Geometry WpfSplashWindowCut(double w, double h, double cut)
        {
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w - cut, 0), true, false);
                ctx.LineTo(new Point(w, cut), true, false);
                ctx.LineTo(new Point(w, h), true, false);
                ctx.LineTo(new Point(cut, h), true, false);
                ctx.LineTo(new Point(0, h - cut), true, false);
            }
            geo.Freeze();
            return geo;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>点阵、雷达环、斜线、巨大的 A.R.C.T 轮廓 —— 全部沿用主界面背景语言。</summary>
    internal sealed class SplashField : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _bg, _line, _ink, _accent, _accent2, _muted;
        private readonly DrawingBrush _dots;
        private readonly Brush _glow;

        public SplashField(bool light, Color bg, Color line, Color ink, Color accent, Color accent2, Color muted)
        {
            _light = light;
            _bg = bg;
            _line = line;
            _ink = ink;
            _accent = accent;
            _accent2 = accent2;
            _muted = muted;
            IsHitTestVisible = false;

            BitmapCache cache = new BitmapCache();
            cache.RenderAtScale = 1.0;
            CacheMode = cache;

            GeometryDrawing dot = new GeometryDrawing();
            dot.Brush = new SolidColorBrush(Color.FromArgb(_light ? (byte)32 : (byte)30, 90, 130, 150));
            dot.Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
            DrawingBrush grid = new DrawingBrush(dot);
            grid.TileMode = TileMode.Tile;
            grid.Viewport = new Rect(0, 0, 26, 26);
            grid.ViewportUnits = BrushMappingMode.Absolute;
            grid.Freeze();
            _dots = grid;

            RadialGradientBrush glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(_light ? (byte)26 : (byte)34, accent.R, accent.G, accent.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
            glow.Freeze();
            _glow = glow;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            LinearGradientBrush gloss = new LinearGradientBrush();
            gloss.StartPoint = new Point(0, 0);
            gloss.EndPoint = new Point(0, 1);
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(_light ? (byte)70 : (byte)38, 255, 255, 255), 0));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
            gloss.Freeze();
            dc.DrawRectangle(gloss, null, new Rect(0, 0, w, h * 0.52));

            // 点阵
            dc.DrawRectangle(_dots, null, new Rect(0, 0, w, h));

            // 徽标后方的柔光
            dc.DrawEllipse(_glow, null, new Point(w * 0.245, h * 0.50), 190, 190);

            // 右侧同心雷达环
            double cx = w * 0.84;
            double cy = h * 0.27;
            Pen ring = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)30 : (byte)26, _accent.R, _accent.G, _accent.B)), 0.8);
            ring.Freeze();
            for (int i = 0; i < 7; i++)
                dc.DrawEllipse(null, ring, new Point(cx, cy), 90 + i * 50, 90 + i * 50);

            Pen radial = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)20 : (byte)18, _accent.R, _accent.G, _accent.B)), 0.7);
            radial.Freeze();
            for (int i = 0; i < 24; i++)
            {
                double a = i * Math.PI / 12;
                dc.DrawLine(radial,
                    new Point(cx + Math.Cos(a) * 92, cy + Math.Sin(a) * 92),
                    new Point(cx + Math.Cos(a) * 430, cy + Math.Sin(a) * 430));
            }

            // 左上斜线
            Pen diag = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)16 : (byte)24, _accent2.R, _accent2.G, _accent2.B)), 0.8);
            diag.Freeze();
            for (int i = 0; i < 5; i++)
                dc.DrawLine(diag, new Point(-20, 24 + i * 20), new Point(130 + i * 22, -6));

            // 巨大的 A.R.C.T 轮辐字（和主界面 DataBackdrop 同一个手法）
            FormattedText ft = new FormattedText("A.R.C.T", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Bahnschrift SemiCondensed"), 110,
                Brushes.Black, 1.0);
            Geometry big = ft.BuildGeometry(new Point(w - 340, h - 178));
            Pen outline = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)26 : (byte)20, _accent.R, _accent.G, _accent.B)), 1.2);
            outline.Freeze();
            dc.DrawGeometry(null, outline, big);

            // 一条很细的水平基准线
            Pen baseline = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)34 : (byte)30, _line.R, _line.G, _line.B)), 0.8);
            baseline.Freeze();
            dc.DrawLine(baseline, new Point(34, h - 24), new Point(w - 34, h - 24));
        }
    }

    /// <summary>缓慢旋转的虚线轨道 —— 给画面持续的生命感。</summary>
    internal sealed class SplashOrbits : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _line, _accent, _accent2;

        public SplashOrbits(bool light, Color line, Color accent, Color accent2)
        {
            _light = light;
            _line = line;
            _accent = accent;
            _accent2 = accent2;
            IsHitTestVisible = false;

            BitmapCache cache = new BitmapCache();
            cache.RenderAtScale = 1.0;
            CacheMode = cache;

            RenderTransformOrigin = new Point(0.84, 0.27);
            RotateTransform rotate = new RotateTransform(0);
            RenderTransform = rotate;
            DoubleAnimation spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(180)));
            spin.RepeatBehavior = RepeatBehavior.Forever;
            Timeline.SetDesiredFrameRate(spin, 30);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;
            double cx = w * 0.84;
            double cy = h * 0.27;

            Pen dashed = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)46 : (byte)54, _accent.R, _accent.G, _accent.B)), 0.9);
            dashed.DashStyle = new DashStyle(new double[] { 2, 9 }, 0);
            dashed.Freeze();
            for (int i = 0; i < 4; i++)
                dc.DrawEllipse(null, dashed, new Point(cx, cy), 210 + i * 58, 210 + i * 58);

            Pen tick = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)36 : (byte)44, _accent2.R, _accent2.G, _accent2.B)), 0.8);
            tick.Freeze();
            for (int i = 0; i < 36; i++)
            {
                double a = i * Math.PI / 18;
                double r1 = 214;
                double r2 = i % 3 == 0 ? 420 : 286;
                dc.DrawLine(tick,
                    new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1),
                    new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2));
            }
        }
    }

    /// <summary>右上角缓慢上浮的微粒。</summary>
    internal sealed class SplashParticles : FrameworkElement
    {
        private struct Particle
        {
            public double X;
            public double Y;
            public double Speed;
            public double Size;
            public double Phase;
            public Brush Brush;
        }

        private readonly Particle[] _dots;
        private double _time;

        public SplashParticles(Color accent, Color accent2)
        {
            IsHitTestVisible = false;
            _dots = new Particle[18];
            Random r = new Random(20261024);
            for (int i = 0; i < _dots.Length; i++)
            {
                Particle p = new Particle();
                p.X = 0.04 + r.NextDouble() * 0.92;
                p.Y = r.NextDouble();
                p.Speed = 0.006 + r.NextDouble() * 0.018;
                p.Size = 0.7 + r.NextDouble() * 1.2;
                p.Phase = r.NextDouble() * Math.PI * 2;
                int alpha = 20 + r.Next(46);
                Color c = i % 3 == 0 ? accent2 : accent;
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb((byte)alpha, c.R, c.G, c.B));
                b.Freeze();
                p.Brush = b;
                _dots[i] = p;
            }
        }

        public void Tick(double dt)
        {
            _time += dt;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            for (int i = 0; i < _dots.Length; i++)
            {
                Particle p = _dots[i];
                double y = (p.Y - _time * p.Speed) % 1.0;
                if (y < 0) y += 1.0;
                double x = p.X + Math.Sin(_time * 0.22 + p.Phase) * 0.007;
                double alphaScale = 0.55 + 0.45 * Math.Sin(_time * 0.7 + p.Phase);
                dc.PushOpacity(Math.Max(0.15, alphaScale));
                dc.DrawEllipse(p.Brush, null, new Point(x * w, y * h), p.Size, p.Size);
                dc.Pop();
            }
        }
    }

    /// <summary>中心六边形徽标：几何核心，静止但会呼吸。</summary>
    internal sealed class SplashEmblemCore : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _line, _accent, _accent2, _muted;

        public SplashEmblemCore(bool light, Color line, Color accent, Color accent2, Color muted)
        {
            _light = light;
            _line = line;
            _accent = accent;
            _accent2 = accent2;
            _muted = muted;
            IsHitTestVisible = false;

            BitmapCache cache = new BitmapCache();
            cache.RenderAtScale = 1.0;
            CacheMode = cache;

            DoubleAnimation breathe = new DoubleAnimation(0.82, 1.0, new Duration(TimeSpan.FromSeconds(2.8)));
            breathe.AutoReverse = true;
            breathe.RepeatBehavior = RepeatBehavior.Forever;
            breathe.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            Timeline.SetDesiredFrameRate(breathe, 30);
            BeginAnimation(UIElement.OpacityProperty, breathe);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            double cx = w / 2;
            double cy = h / 2;
            double maxR = Math.Min(w, h) / 2 - 6;
            if (maxR < 10) return;

            // 背后柔光
            RadialGradientBrush glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(_light ? (byte)30 : (byte)42, _accent.R, _accent.G, _accent.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, _accent.R, _accent.G, _accent.B), 1));
            glow.Freeze();
            dc.DrawEllipse(glow, null, new Point(cx, cy), maxR * 1.12, maxR * 1.12);

            double r = maxR * 0.82;
            Point[] hex = Hex(cx, cy, r, -Math.PI / 2);
            Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)190 : (byte)210, _line.R, _line.G, _line.B)), 1.2);
            pen.Freeze();
            StreamGeometry hexGeo = Polygon(hex, true);
            dc.DrawGeometry(null, pen, hexGeo);

            // 内层六边形（旋转 30°）
            Point[] hex2 = Hex(cx, cy, r * 0.76, 0);
            Pen dashed = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)80 : (byte)110, _muted.R, _muted.G, _muted.B)), 0.8);
            dashed.DashStyle = new DashStyle(new double[] { 2, 4 }, 0);
            dashed.Freeze();
            dc.DrawGeometry(null, dashed, Polygon(hex2, true));

            // 刻度圆
            Pen tick = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)110 : (byte)140, _muted.R, _muted.G, _muted.B)), 0.7);
            tick.Freeze();
            for (int i = 0; i < 36; i++)
            {
                double a = i * Math.PI / 18;
                double r1 = r * 0.55;
                double r2 = i % 6 == 0 ? r * 0.66 : r * 0.60;
                Pen p = i % 6 == 0
                    ? new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)200 : (byte)230, _accent.R, _accent.G, _accent.B)), 1.1)
                    : tick;
                dc.DrawLine(p, new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1),
                    new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2));
            }

            // 中心菱形 + 十字准星
            Pen accentPen = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)210 : (byte)240, _accent.R, _accent.G, _accent.B)), 1.1);
            accentPen.Freeze();
            Point[] diamond = new Point[]
            {
                new Point(cx, cy - r * 0.30),
                new Point(cx + r * 0.30, cy),
                new Point(cx, cy + r * 0.30),
                new Point(cx - r * 0.30, cy)
            };
            dc.DrawGeometry(null, accentPen, Polygon(diamond, true));

            dc.DrawLine(accentPen, new Point(cx - r * 0.52, cy), new Point(cx - r * 0.34, cy));
            dc.DrawLine(accentPen, new Point(cx + r * 0.34, cy), new Point(cx + r * 0.52, cy));
            dc.DrawLine(accentPen, new Point(cx, cy - r * 0.52), new Point(cx, cy - r * 0.34));
            dc.DrawLine(accentPen, new Point(cx, cy + r * 0.34), new Point(cx, cy + r * 0.52));

            SolidColorBrush core = new SolidColorBrush(Color.FromArgb(255, _accent.R, _accent.G, _accent.B));
            core.Freeze();
            dc.DrawEllipse(core, null, new Point(cx, cy), 3.4, 3.4);

            Pen coreRing = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)140 : (byte)170, _accent.R, _accent.G, _accent.B)), 0.8);
            coreRing.Freeze();
            dc.DrawEllipse(null, coreRing, new Point(cx, cy), 9, 9);

            // 四角小刻度
            Pen corner = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)110 : (byte)140, _muted.R, _muted.G, _muted.B)), 0.8);
            corner.Freeze();
            double[] angles = new double[] { 0.35, Math.PI - 0.35, Math.PI + 0.35, Math.PI * 2 - 0.35 };
            for (int i = 0; i < angles.Length; i++)
            {
                double a = angles[i];
                dc.DrawLine(corner,
                    new Point(cx + Math.Cos(a) * (maxR - 2), cy + Math.Sin(a) * (maxR - 2)),
                    new Point(cx + Math.Cos(a) * (maxR - 14), cy + Math.Sin(a) * (maxR - 14)));
            }
        }

        private static Point[] Hex(double cx, double cy, double r, double start)
        {
            Point[] p = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                double a = start + i * Math.PI / 3;
                p[i] = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            }
            return p;
        }

        private static StreamGeometry Polygon(Point[] points, bool closed)
        {
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(points[0], false, closed);
                for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
            }
            geo.Freeze();
            return geo;
        }
    }

    /// <summary>外层虚线轨道 + 卫星点，持续旋转。</summary>
    internal sealed class SplashEmblemOrbit : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _line, _accent, _accent2;
        private readonly double _size;

        public SplashEmblemOrbit(bool light, Color line, Color accent, Color accent2, double size)
        {
            _light = light;
            _line = line;
            _accent = accent;
            _accent2 = accent2;
            _size = size;
            IsHitTestVisible = false;

            BitmapCache cache = new BitmapCache();
            cache.RenderAtScale = 1.0;
            CacheMode = cache;

            RenderTransformOrigin = new Point(0.5, 0.5);

            RotateTransform rotate = new RotateTransform(0);
            RenderTransform = rotate;
            DoubleAnimation spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(22)));
            spin.RepeatBehavior = RepeatBehavior.Forever;
            Timeline.SetDesiredFrameRate(spin, 30);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 10 || h < 10) return;
            double cx = w / 2;
            double cy = h / 2;
            double r = Math.Min(w, h) / 2 - 4;

            Pen dashed = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)150 : (byte)190, _accent.R, _accent.G, _accent.B)), 1.0);
            dashed.DashStyle = new DashStyle(new double[] { 2, 6 }, 0);
            dashed.Freeze();
            dc.DrawEllipse(null, dashed, new Point(cx, cy), r * 0.94, r * 0.94);

            Pen faint = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)40 : (byte)60, _line.R, _line.G, _line.B)), 0.8);
            faint.Freeze();
            dc.DrawEllipse(null, faint, new Point(cx, cy), r * 0.995, r * 0.995);

            // 卫星点 + 尾迹
            SolidColorBrush accent = new SolidColorBrush(Color.FromArgb(255, _accent.R, _accent.G, _accent.B));
            accent.Freeze();
            dc.DrawEllipse(accent, null, new Point(cx + r * 0.94, cy), 3.0, 3.0);

            Pen trail = new Pen(new SolidColorBrush(Color.FromArgb(_light ? (byte)120 : (byte)160, _accent.R, _accent.G, _accent.B)), 1.1);
            trail.Freeze();
            StreamGeometry arc = new StreamGeometry();
            using (StreamGeometryContext ctx = arc.Open())
            {
                double ar = r * 0.94;
                ctx.BeginFigure(new Point(cx + ar, cy), false, false);
                for (int i = 1; i <= 10; i++)
                {
                    double a = -0.55 * i / 10.0;
                    ctx.LineTo(new Point(cx + ar * Math.Cos(a), cy + ar * Math.Sin(a)), true, false);
                }
            }
            arc.Freeze();
            dc.DrawGeometry(null, trail, arc);

            // 180° 的配重小方块
            SolidColorBrush second = new SolidColorBrush(Color.FromArgb(_light ? (byte)210 : (byte)230, _accent2.R, _accent2.G, _accent2.B));
            second.Freeze();
            Point opp = new Point(cx - r * 0.94, cy);
            dc.PushTransform(new RotateTransform(45, opp.X, opp.Y));
            dc.DrawRectangle(second, null, new Rect(opp.X - 2.2, opp.Y - 2.2, 4.4, 4.4));
            dc.Pop();
        }
    }

    /// <summary>底部进度线：渐变填充 + 流光 + 进度点 + 刻度。</summary>
    internal sealed class SplashProgress : FrameworkElement
    {
        private readonly bool _light;
        private readonly Color _lineColor, _accentColor, _accent2Color;
        private readonly Brush _trackBrush, _fillBrush, _dotBrush, _glowBrush;
        private readonly Pen _trackPen, _tickPen, _fillPen, _glowPen;

        public double Target;
        public double Display;
        private double _phase;

        public SplashProgress(bool light, Color line, Color accent, Color accent2)
        {
            _light = light;
            _lineColor = line;
            _accentColor = accent;
            _accent2Color = accent2;
            IsHitTestVisible = false;

            _trackBrush = Frozen(Color.FromArgb(light ? (byte)150 : (byte)135, line.R, line.G, line.B));
            _trackPen = new Pen(_trackBrush, 1.0);
            _tickPen = new Pen(_trackBrush, 0.7);
            _fillBrush = FrozenLinear(accent2, accent);
            _fillPen = new Pen(_fillBrush, 2.1);
            _dotBrush = Frozen(accent);
            _glowBrush = Frozen(Color.FromArgb(70, accent.R, accent.G, accent.B));
            _glowPen = new Pen(_glowBrush, 6);
            _trackPen.Freeze();
            _tickPen.Freeze();
            _fillPen.Freeze();
            _glowPen.Freeze();
        }

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush FrozenLinear(Color a, Color b)
        {
            LinearGradientBrush g = new LinearGradientBrush(a, b, 0);
            g.Freeze();
            return g;
        }

        public void Tick(double dt)
        {
            double gap = Target - Display;
            if (gap > 0.0001)
            {
                // 有目标时平滑追；同时保证永远有一点点前进，不会“卡住”
                Display += Math.Max(gap * dt * 4.2, dt * 0.055);
                if (Display > Target) Display = Target;
            }
            else
            {
                Display += (Target - Display) * Math.Min(1, dt * 8);
            }
            if (Display < 0) Display = 0;
            if (Display > 1) Display = 1;

            if (Display > 0.72) _phase += dt * (1.0 + (Display - 0.72) * 2.2);
            else _phase += dt;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;
            double y = h * 0.5;

            dc.DrawLine(_trackPen, new Point(0, y), new Point(w, y));

            for (int i = 0; i <= 20; i++)
            {
                double x = w * i / 20.0;
                double len = i % 5 == 0 ? 4.5 : 2.5;
                dc.DrawLine(_tickPen, new Point(x, y - len), new Point(x, y + len));
            }

            double fill = w * Display;
            if (fill > 0.5)
            {
                dc.DrawLine(_glowPen, new Point(0, y), new Point(fill, y));
                dc.DrawLine(_fillPen, new Point(0, y), new Point(fill, y));

                // 流光：一段透明的亮带在已完成区间里来回扫
                double shimmerW = Math.Max(40, w * 0.16);
                double x = ((_phase * 0.22) % 1.45 - 0.2) * w;
                if (x < fill)
                {
                    LinearGradientBrush shine = new LinearGradientBrush();
                    shine.StartPoint = new Point(0, 0);
                    shine.EndPoint = new Point(1, 0);
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(_light ? (byte)70 : (byte)90, 255, 255, 255), 0.5));
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                    shine.Freeze();

                    dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, fill), h)));
                    dc.DrawRectangle(shine, null, new Rect(x, y - 2.5, shimmerW, 5));
                    dc.Pop();
                }

                // 进度点
                dc.DrawEllipse(_glowBrush, null, new Point(fill, y), 7, 7);
                dc.DrawEllipse(_dotBrush, null, new Point(fill, y), 2.6, 2.6);
            }
        }
    }
}
