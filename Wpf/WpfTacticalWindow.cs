using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SCPSLAlert
{
    internal sealed partial class WpfTacticalWindow : Window
    {
        public event Action<string> ScpKilled;
        public Action<string, double> TimeCalibrate;
        public Func<string> TimeCalibrateState;
        public Action StopSpecialCountdowns;

        public TacticalLog Log { get { return _log; } }

        private readonly Config _cfg;
        private readonly TacticalLog _log;
        private readonly Action _onSaved;
        private readonly Func<VigilSnapshot> _snapshot;
        private readonly Action _recreate;
        private bool _transparentMode;

        private bool _light;
        private TacPalette _p;
        private Brush _bgB, _panelB, _panelAltB, _lineB, _inkB, _subB, _mutedB;
        private Brush _accentB, _accent2B, _dangerB, _warnB, _goodB, _mtfB, _chaosB;

        private Grid _root;
        private Grid _pageHost;
        private readonly ScrollViewer[] _pageScroll = new ScrollViewer[4];

        private readonly List<Border> _railItems = new List<Border>();
        private readonly List<TextBlock> _railIcons = new List<TextBlock>();
        private readonly List<TextBlock> _railLabels = new List<TextBlock>();
        private readonly List<Border> _railBars = new List<Border>();

        private TextBlock _headKicker, _headTitle, _headDesc, _headRight1, _headRight2;
        private TextBlock _ticker, _footerLeft, _footerRight;
        private Border _headerPulse;
        private Border _headSweep;
        private Grid _headHost;
        private TacGhostText _headGhost;

        private Grid _bodyShell;
        private Grid _entrance;
        private Border _entSeam;
        private TranslateTransform _seamShift;
        private DispatcherTimer _regionTimer;
        private Border _entEdgeTop, _entEdgeBottom;
        private Grid _entBrackets;
        private readonly List<Path> _entCorners = new List<Path>();
        private RectangleGeometry _rootClip;
        private bool _entranceOn;
        private bool _entranceClosing;
        private double _targetOpacity = 1;
        private readonly double[] _cornerFromX = new double[4];
        private readonly double[] _cornerFromY = new double[4];
        private readonly List<TacNumber> _numbers = new List<TacNumber>();

        private int _page;
        private DispatcherTimer _tick;
        private DispatcherTimer _saveTimer;
        private bool _syncing;
        private bool _shellBuilt;
        private readonly bool _animated;

        private string _bigWaveSide = TacticalLog.MTF;

        private IntPtr _prevForeground = IntPtr.Zero;

        private string _ticketSummary = "";
        private readonly HashSet<string> _curDirty = new HashSet<string>();
        private List<string> _layout = new List<string>();
        private bool _editMode;
        private StackPanel _overviewPanel;
        private string _layoutBackup = "";
        private string _dragId;
        private double _dragStartY;
        private Border _dragWrap;

        private static readonly string[] PageKickers =
        {
            "01 / RESPAWN", "02 / BATTLEFIELD", "03 / OBJECTIVES", "04 / CALIBRATION"
        };

        private static readonly string[] PageTitles =
        {
            "战术总览", "战况", "目标登记", "时间校准"
        };

        private static readonly string[] PageDescs =
        {
            "先看还能不能重生，再看下一波什么时候到；其余数据都在下面。",
            "每一只 SCP 的血量、阵营、阵亡与在场；发电机与刷新顺序也在这里。",
            "点一下 = 登记一次。影响力按目标累加，不是按血量。",
            "中途加入或计时偏了，填一项就校准；只影响时间，不改战术数据。"
        };

        public WpfTacticalWindow(Config cfg, TacticalLog log, Action onSaved, Func<VigilSnapshot> snapshot)
            : this(cfg, log, onSaved, snapshot, null) { }

        public WpfTacticalWindow(Config cfg, TacticalLog log, Action onSaved, Func<VigilSnapshot> snapshot,
            Action recreate)
        {
            _cfg = cfg;
            _log = log;
            _onSaved = onSaved;
            _snapshot = snapshot;
            _recreate = recreate;
            _animated = !UiAnim.Off;
            _transparentMode = cfg != null && cfg.TacticalOpacity < 100;

            _light = cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : cfg.UiThemeMode == 0;
            BuildPalette();

            Title = "SCPSLAlert // TACTICAL";
            Rect wa = SystemParameters.WorkArea;
            Width = Math.Min(1160, Math.Max(900, wa.Width - 40));
            Height = Math.Min(760, Math.Max(560, wa.Height - 40));
            WindowStyle = System.Windows.WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = _transparentMode;
            Background = _transparentMode ? Brushes.Transparent : _bgB;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = TacUi.Ui;
            Foreground = _inkB;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);

            BuildShell();
            _layout = TacWidgets.Parse(_cfg == null ? TacWidgets.Default : _cfg.TacticalWidgets);
            ApplyAppearance();
            BuildPages();
            SeedInputs();
            RefreshAll();

            _tick = new DispatcherTimer(DispatcherPriority.Render);
            _tick.Interval = TimeSpan.FromMilliseconds(250);
            _tick.Tick += delegate
            {
                if (!IsVisible) return;
                RefreshAll();
            };
            _tick.Start();

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveNow();
            };

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr h = new WindowInteropHelper(this).Handle;
                    int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE,
                        ex | NativeMethods.WS_EX_TOOLWINDOW);
                    NativeMethods.HideFromCapture(h, "战术面板");
                }
                catch (Exception e) { Logger.Err("战术", "设置新版战术面板窗口样式失败", e); }
            };

            PreviewKeyDown += OnWindowKeyDown;
            ModernTheme.Changed += OnThemeChanged;
            ThemeTokens.Changed += OnPaletteChanged;
            Closed += delegate
            {
                try { StopRegionSync(); ClearWindowRegion(); } catch { }
                ModernTheme.Changed -= OnThemeChanged;
                ThemeTokens.Changed -= OnPaletteChanged;
                try { if (_tick != null) _tick.Stop(); } catch { }
                try { if (_saveTimer != null) _saveTimer.Stop(); } catch { }
                try { SaveNow(); } catch { }
            };
        }


        private void BuildPalette()
        {
            _p = _light ? TacPalette.Light() : TacPalette.Dark();
            _bgB = TacColor.Solid(_p.Bg);
            _panelB = TacColor.Solid(_p.Panel);
            _panelAltB = TacColor.Solid(_p.PanelAlt);
            _lineB = TacColor.Solid(_p.Line);
            _inkB = TacColor.Solid(_p.Ink);
            _subB = TacColor.Solid(_p.Sub);
            _mutedB = TacColor.Solid(_p.Muted);
            _accentB = TacColor.Solid(_p.Accent);
            _accent2B = TacColor.Solid(_p.Accent2);
            _dangerB = TacColor.Solid(_p.Danger);
            _warnB = TacColor.Solid(_p.Warn);
            _goodB = TacColor.Solid(_p.Good);
            _mtfB = TacColor.Solid(_p.Mtf);
            _chaosB = TacColor.Solid(_p.Chaos);
        }

        private void OnThemeChanged()
        {
            if (_cfg.UiThemeMode >= 0) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                bool light = !ModernTheme.IsDark;
                if (light == _light) return;
                Rebuild(light);
            }));
        }

        private void OnPaletteChanged()
        {
            if (!IsLoaded) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (IsLoaded) Rebuild(_light);
            }));
        }

        private void Rebuild(bool light)
        {
            try
            {
                SaveNow();
                StopEntrance();
                _light = light;
                BuildPalette();
                Background = _bgB;
                Foreground = _inkB;
                BuildShell();
                BuildPages();
                SeedInputs();
                RefreshAll();
                GoPage(_page, false);
            }
            catch (Exception ex) { Logger.Err("战术", "重建新版战术面板失败", ex); }
        }


        private void BuildShell()
        {
            _shellBuilt = true;
            _numbers.Clear();
            _root = new Grid();
            _root.Background = _bgB;
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            _root.RowDefinitions.Add(new RowDefinition());
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            Content = _root;

            TacBackdrop backdrop = new TacBackdrop(_p);
            Grid.SetRowSpan(backdrop, 4);
            _root.Children.Add(backdrop);

            if (!UiAnim.Off)
            {
                TacOrbit orbit = new TacOrbit(_p);
                Grid.SetRowSpan(orbit, 4);
                _root.Children.Add(orbit);
            }

            if (!UiAnim.Off)
            {
                TacScanlines scan = new TacScanlines(_p);
                Grid.SetRowSpan(scan, 4);
                _root.Children.Add(scan);
            }

            _root.Children.Add(BuildTopBar());

            Border topLine = new Border();
            topLine.Background = _lineB;
            Grid.SetRow(topLine, 1);
            _root.Children.Add(topLine);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 2);
            _root.Children.Add(body);
            _bodyShell = body;

            body.Children.Add(BuildRail());

            Border railLine = new Border();
            railLine.Background = _lineB;
            Grid.SetColumn(railLine, 1);
            body.Children.Add(railLine);

            Grid content = new Grid();
            content.Margin = new Thickness(24, 12, 24, 10);
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            content.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(content, 2);
            body.Children.Add(content);

            content.Children.Add(BuildPageHead());

            _pageHost = new Grid();
            Grid.SetRow(_pageHost, 1);
            content.Children.Add(_pageHost);

            WpfScroll.Apply(this, _light);

            _root.Children.Add(BuildFooter());
            BuildEntrance();
        }

        private void BuildEntrance()
        {
            _entrance = new Grid();
            _entrance.IsHitTestVisible = false;
            _entrance.Visibility = Visibility.Collapsed;
            System.Windows.Controls.Panel.SetZIndex(_entrance, 80);
            Grid.SetRowSpan(_entrance, 4);
            _root.Children.Add(_entrance);

            _entSeam = new Border();
            _entSeam.Height = 3;
            _entSeam.VerticalAlignment = VerticalAlignment.Top;
            LinearGradientBrush seam = new LinearGradientBrush();
            seam.StartPoint = new Point(0, 0.5);
            seam.EndPoint = new Point(1, 0.5);
            seam.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            seam.GradientStops.Add(new GradientStop(TacColor.Alpha(_p.Accent, 40).Color, 0.12));
            seam.GradientStops.Add(new GradientStop(_p.Accent, 0.5));
            seam.GradientStops.Add(new GradientStop(TacColor.Alpha(_p.Accent, 40).Color, 0.88));
            seam.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _entSeam.Background = seam;
            _entSeam.Opacity = 0;
            _seamShift = new TranslateTransform(0, 0);
            _entSeam.RenderTransform = _seamShift;
            _entrance.Children.Add(_entSeam);

            _entEdgeTop = MakeEdge();
            _entEdgeBottom = MakeEdge();
            _entrance.Children.Add(_entEdgeTop);
            _entrance.Children.Add(_entEdgeBottom);

            _entBrackets = new Grid();
            _entBrackets.Opacity = 0;
            _entrance.Children.Add(_entBrackets);

            _entCorners.Clear();
            AddCorner("M0,26 L0,0 L26,0", HorizontalAlignment.Left, VerticalAlignment.Top, new TranslateTransform(-22, -22));
            AddCorner("M0,0 L26,0 L26,26", HorizontalAlignment.Right, VerticalAlignment.Top, new TranslateTransform(22, -22));
            AddCorner("M26,0 L26,26 L0,26", HorizontalAlignment.Right, VerticalAlignment.Bottom, new TranslateTransform(22, 22));
            AddCorner("M26,26 L0,26 L0,0", HorizontalAlignment.Left, VerticalAlignment.Bottom, new TranslateTransform(-22, 22));
        }

        private Border MakeEdge()
        {
            Border edge = new Border();
            edge.Height = 1.5;
            edge.VerticalAlignment = VerticalAlignment.Top;
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0.5);
            brush.EndPoint = new Point(1, 0.5);
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            brush.GradientStops.Add(new GradientStop(TacColor.Alpha(_p.Accent, 120).Color, 0.2));
            brush.GradientStops.Add(new GradientStop(_p.Accent, 0.5));
            brush.GradientStops.Add(new GradientStop(TacColor.Alpha(_p.Accent, 120).Color, 0.8));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            edge.Background = brush;
            edge.Opacity = 0;
            edge.IsHitTestVisible = false;
            edge.RenderTransform = new TranslateTransform();
            return edge;
        }

        private void AddCorner(string data, HorizontalAlignment h, VerticalAlignment v, TranslateTransform offset)
        {
            Path path = new Path();
            path.Data = Geometry.Parse(data);
            path.Stroke = _accentB;
            path.StrokeThickness = 2;
            path.HorizontalAlignment = h;
            path.VerticalAlignment = v;
            path.Margin = new Thickness(12);
            path.RenderTransform = offset;
            int i = _entCorners.Count;
            _cornerFromX[i] = offset.X;
            _cornerFromY[i] = offset.Y;
            _entBrackets.Children.Add(path);
            _entCorners.Add(path);
        }

        private TacNumber Num(string text, double size, Brush color, bool display)
        {
            TacNumber n = TacUi.Number(text, size, color, display);
            _numbers.Add(n);
            return n;
        }

        private void ResetNumbers()
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                try { _numbers[i].ResetToZero(); }
                catch { }
            }
        }

        private UIElement BuildTopBar()
        {
            Border bar = new Border();
            bar.Background = _light ? _panelB : TacColor.Solid(Color.FromRgb(3, 6, 8));
            bar.BorderBrush = _lineB;
            bar.BorderThickness = new Thickness(0);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource is DependencyObject && InsideButton(e.OriginalSource as DependencyObject)) return;
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = g;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(16, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            TextBlock vig = TacUi.Display("A.R.C.T", 18, _accentB);
            vig.FontWeight = FontWeights.Bold;
            brand.Children.Add(vig);
            TextBlock tag = TacUi.Mono("  //  TACTICAL", 8.5, _mutedB);
            tag.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(tag);
            TextBlock sep = TacUi.Mono("      ", 8.5, _mutedB);
            brand.Children.Add(sep);
            TacDot dot = new TacDot(_goodB, true);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(4, 0, 5, 0);
            brand.Children.Add(dot);
            TextBlock live = TacUi.Mono("LIVE", 8, _subB);
            live.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(live);
            g.Children.Add(brand);

            _ticker = TacUi.Mono("", 8.5, _subB);
            _ticker.VerticalAlignment = VerticalAlignment.Center;
            _ticker.HorizontalAlignment = HorizontalAlignment.Center;
            _ticker.TextAlignment = TextAlignment.Center;
            _ticker.TextTrimming = TextTrimming.CharacterEllipsis;
            _ticker.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(_ticker, 1);
            g.Children.Add(_ticker);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 8, 0);

            Border themeChip = new Border();
            themeChip.CornerRadius = new CornerRadius(3);
            themeChip.BorderThickness = new Thickness(1);
            themeChip.BorderBrush = _lineB;
            themeChip.Background = _panelAltB;
            themeChip.Height = 22;
            themeChip.Padding = new Thickness(8, 0, 8, 0);
            themeChip.Margin = new Thickness(0, 0, 6, 0);
            themeChip.VerticalAlignment = VerticalAlignment.Center;
            themeChip.Child = TacUi.Mono(ThemeText(), 7.5, _accent2B);
            right.Children.Add(themeChip);

            TacButton themeBtn = new TacButton(_light ? "\uE708" : "\uE706", TacKind.Quiet, _p, 24, 8);
            themeBtn.FontFamily = TacUi.Icons;
            themeBtn.Label.FontFamily = TacUi.Icons;
            themeBtn.Label.FontSize = 10;
            themeBtn.ToolTip = "切换深色 / 浅色（跟随系统 / 手动）";
            themeBtn.Click += delegate { CycleTheme(); };
            themeBtn.Margin = new Thickness(0, 0, 4, 0);
            right.Children.Add(themeBtn);

            TacButton close = new TacButton("\uE8BB", TacKind.Quiet, _p, 24, 8);
            close.Label.FontFamily = TacUi.Icons;
            close.Label.FontSize = 10;
            close.ToolTip = "收起面板（Esc）";
            close.Click += delegate { HidePanel(); };
            right.Children.Add(close);

            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            _headerPulse = new Border();
            _headerPulse.Height = 2;
            _headerPulse.Background = _accentB;
            _headerPulse.Opacity = 0;
            _headerPulse.VerticalAlignment = VerticalAlignment.Bottom;
            _headerPulse.IsHitTestVisible = false;
            Grid.SetColumnSpan(_headerPulse, 3);
            g.Children.Add(_headerPulse);
            return bar;
        }

        private string ThemeText()
        {
            if (_cfg.UiThemeMode < 0) return _light ? "LIGHT / AUTO" : "DARK / AUTO";
            return _light ? "LIGHT / MANUAL" : "DARK / MANUAL";
        }

        private void CycleTheme()
        {
            try
            {
                _cfg.UiThemeMode = _cfg.UiThemeMode < 0 ? 0 : _cfg.UiThemeMode == 0 ? 1 : -1;
                SaveNow();
                ModernTheme.SetMode(_cfg.UiThemeMode);
                bool light = _cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : _cfg.UiThemeMode == 0;
                Rebuild(light);
            }
            catch (Exception ex) { Logger.Err("战术", "切换战术面板主题失败", ex); }
        }

        private static bool InsideButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is Button) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private UIElement BuildRail()
        {
            Border rail = new Border();
            rail.Background = _light ? _panelB : TacColor.Solid(Color.FromRgb(4, 7, 9));
            rail.BorderThickness = new Thickness(0);
            rail.ClipToBounds = true;
            Grid.SetColumn(rail, 0);

            StackPanel list = new StackPanel();
            list.Margin = new Thickness(0, 14, 0, 0);

            string[] glyphs = { "\uE80F", "\uE765", "\uE8A5", "\uE823" };
            string[] labels = { "总览", "战况", "目标", "校准" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                int index = i;
                Border item = new Border();
                item.Height = 58;
                item.Background = Brushes.Transparent;
                item.Cursor = Cursors.Hand;
                item.ToolTip = labels[i];
                item.Margin = new Thickness(0, 0, 0, 2);

                Grid cell = new Grid();
                Border bar = new Border();
                bar.Width = 2;
                bar.Height = 24;
                bar.Background = _accentB;
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                bar.VerticalAlignment = VerticalAlignment.Center;
                bar.Opacity = index == 0 ? 1 : 0;
                cell.Children.Add(bar);

                TextBlock icon = new TextBlock();
                icon.Text = glyphs[i];
                icon.FontFamily = TacUi.Icons;
                icon.FontSize = 15;
                icon.Foreground = index == 0 ? _accentB : _mutedB;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 15, 0, 0);
                cell.Children.Add(icon);

                TextBlock label = TacUi.T(labels[i], 9, index == 0 ? _accentB : _subB);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Bottom;
                label.Margin = new Thickness(0, 0, 0, 9);
                cell.Children.Add(label);

                item.Child = cell;
                item.MouseEnter += delegate
                {
                    if (_page == index) return;
                    item.Background = _panelAltB;
                    icon.Foreground = _inkB;
                    label.Foreground = _inkB;
                };
                item.MouseLeave += delegate
                {
                    if (_page == index) return;
                    item.Background = Brushes.Transparent;
                    icon.Foreground = _mutedB;
                    label.Foreground = _subB;
                };
                item.MouseLeftButtonUp += delegate { GoPage(index); };
                list.Children.Add(item);

                _railItems.Add(item);
                _railIcons.Add(icon);
                _railLabels.Add(label);
                _railBars.Add(bar);
            }

            rail.Child = list;
            return rail;
        }

        private UIElement BuildPageHead()
        {
            Grid head = new Grid();
            _headHost = head;
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _headGhost = new TacGhostText("01", 68, TacColor.Alpha(_p.Accent, _p.IsLight ? 70 : 90), 1.0);
            _headGhost.HorizontalAlignment = HorizontalAlignment.Left;
            _headGhost.VerticalAlignment = VerticalAlignment.Top;
            _headGhost.Margin = new Thickness(-4, -8, 0, 0);
            head.Children.Add(_headGhost);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 7, 0, 0);
            _headKicker = TacUi.Mono(PageKickers[0], 8.5, _accentB);
            title.Children.Add(_headKicker);
            _headTitle = TacUi.Display(PageTitles[0], 25, _inkB);
            _headTitle.FontWeight = FontWeights.Bold;
            _headTitle.Margin = new Thickness(0, -1, 0, 1);
            title.Children.Add(_headTitle);
            _headDesc = TacUi.T(PageDescs[0], 10.5, _subB);
            _headDesc.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Children.Add(_headDesc);
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            _headRight1 = TacUi.Mono("", 8.5, _mutedB);
            _headRight1.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(_headRight1);
            _headRight2 = TacUi.Display("", 19, _inkB);
            _headRight2.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(_headRight2);
            Grid.SetColumn(right, 2);
            head.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = _lineB;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 3);
            head.Children.Add(line);

            _headSweep = new Border();
            _headSweep.Height = 2;
            _headSweep.Width = 240;
            _headSweep.HorizontalAlignment = HorizontalAlignment.Left;
            _headSweep.VerticalAlignment = VerticalAlignment.Bottom;
            LinearGradientBrush sweep = new LinearGradientBrush();
            sweep.StartPoint = new Point(0, 0.5);
            sweep.EndPoint = new Point(1, 0.5);
            sweep.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            sweep.GradientStops.Add(new GradientStop(_p.Accent, 0.65));
            sweep.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _headSweep.Background = sweep;
            _headSweep.Opacity = 0;
            _headSweep.IsHitTestVisible = false;
            Grid.SetColumnSpan(_headSweep, 3);
            head.Children.Add(_headSweep);
            return head;
        }

        private UIElement BuildFooter()
        {
            Border footer = new Border();
            footer.Background = _light ? _panelB : TacColor.Solid(Color.FromRgb(4, 7, 9));
            footer.BorderThickness = new Thickness(0);
            Grid.SetRow(footer, 3);

            Grid g = new Grid();
            g.Margin = new Thickness(14, 0, 14, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _footerLeft = TacUi.Mono("", 7.5, _mutedB);
            _footerLeft.VerticalAlignment = VerticalAlignment.Center;
            _footerLeft.HorizontalAlignment = HorizontalAlignment.Left;
            _footerLeft.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(_footerLeft);

            _footerRight = TacUi.Mono("A.R.C.T // TACTICAL     ·     MODEL LIVE     ·     AUTO SAVE", 7.5, _mutedB);
            _footerRight.VerticalAlignment = VerticalAlignment.Center;
            _footerRight.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_footerRight, 1);
            g.Children.Add(_footerRight);

            footer.Child = g;
            return footer;
        }


        private void GoPage(int index)
        {
            GoPage(index, _animated);
        }

        private void GoPage(int index, bool animate)
        {
            if (index < 0 || index > 3) return;
            bool changed = index != _page;
            _page = index;

            for (int i = 0; i < 4; i++)
            {
                if (_pageScroll[i] == null) continue;
                _pageScroll[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
            for (int i = 0; i < _railItems.Count; i++)
            {
                bool on = i == index;
                _railBars[i].Opacity = on ? 1 : 0;
                _railIcons[i].Foreground = on ? _accentB : _mutedB;
                _railLabels[i].Foreground = on ? _accentB : _subB;
                _railItems[i].Background = Brushes.Transparent;
            }

            if (_headKicker != null) _headKicker.Text = PageKickers[index];
            if (_headTitle != null) _headTitle.Text = PageTitles[index];
            if (_headDesc != null) _headDesc.Text = PageDescs[index];
            if (_headGhost != null) _headGhost.Text = (index + 1).ToString("00");

            if (changed && animate)
            {
                if (_pageScroll[index] != null)
                {
                    ScrollViewer sv = _pageScroll[index];
                    sv.Opacity = 0.25;
                    DoubleAnimation fade = new DoubleAnimation(0.25, 1,
                        new Duration(TimeSpan.FromMilliseconds(150)));
                    fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    TacAnim.Fix(fade);
                    sv.BeginAnimation(UIElement.OpacityProperty, fade);
                }
                PlayHeadSweep();
            }

            UpdateHead(DateTime.Now);
        }

        private void PlayHeadSweep()
        {
            if (_headSweep == null || !_animated || UiAnim.Off) return;
            TranslateTransform shift = _headSweep.RenderTransform as TranslateTransform;
            if (shift == null)
            {
                shift = new TranslateTransform();
                _headSweep.RenderTransform = shift;
            }
            double width = _headHost == null ? 900 : Math.Max(400, _headHost.ActualWidth);
            _headSweep.Opacity = 1;
            shift.X = -240;
            DoubleAnimation move = new DoubleAnimation(-240, width,
                new Duration(TimeSpan.FromMilliseconds(320)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(move);
            shift.BeginAnimation(TranslateTransform.XProperty, move);
            DoubleAnimation outFade = new DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(260)));
            outFade.BeginTime = TimeSpan.FromMilliseconds(180);
            outFade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(outFade);
            _headSweep.BeginAnimation(UIElement.OpacityProperty, outFade);
        }

        private void PlayEntrance()
        {
            if (_root == null) return;
            if (!_animated || UiAnim.Off)
            {
                StopEntrance();
                return;
            }
            if (_entrance == null) return;
            StopEntrance();
            UpdateLayout();

            double w = ActualWidth > 20 ? ActualWidth : Width;
            double h = ActualHeight > 20 ? ActualHeight : Height;
            if (w < 20 || h < 20) return;

            int style = _cfg == null ? 0 : _cfg.TacticalOpenStyle;
            if (style == 2)
            {
                PlayFadeEntrance();
                return;
            }

            _entranceOn = true;
            _entrance.Visibility = Visibility.Visible;
            _entSeam.Opacity = 0.95;
            _entBrackets.Opacity = 0;
            SetBaseOpacity();

            Rect slit = style == 1 ? new Rect(0, 0, w, 2) : new Rect(0, h / 2 - 1, w, 2);
            _rootClip = new RectangleGeometry(slit);
            _root.Clip = _rootClip;
            ApplyWindowRegion(slit);
            StartRegionSync();

            RectAnimation grow = new RectAnimation(slit, new Rect(0, 0, w, h),
                new Duration(TimeSpan.FromMilliseconds(400)));
            grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(grow);
            grow.Completed += delegate { FinishEntrance(); };
            _rootClip.BeginAnimation(RectangleGeometry.RectProperty, grow);

            DoubleAnimation topFade = new DoubleAnimation(0.95, 0,
                new Duration(TimeSpan.FromMilliseconds(200)));
            topFade.BeginTime = TimeSpan.FromMilliseconds(230);
            TacAnim.Fix(topFade);
            _entSeam.BeginAnimation(UIElement.OpacityProperty, topFade);
            if (_entEdgeTop != null)
            {
                _entEdgeTop.Opacity = 0.95;
                DoubleAnimation bottomFade = new DoubleAnimation(0.95, 0,
                    new Duration(TimeSpan.FromMilliseconds(200)));
                bottomFade.BeginTime = TimeSpan.FromMilliseconds(230);
                TacAnim.Fix(bottomFade);
                _entEdgeTop.BeginAnimation(UIElement.OpacityProperty, bottomFade);
            }

            PlayBrackets();
        }

        private void PlayFadeEntrance()
        {
            _entranceOn = true;
            _entrance.Visibility = Visibility.Visible;
            _entSeam.Opacity = 0;
            _entBrackets.Opacity = 0;
            if (_entEdgeTop != null) _entEdgeTop.Opacity = 0;

            UIElement target = _transparentMode && _root != null ? (UIElement)_root : this;
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 0;
            DoubleAnimation fade = new DoubleAnimation(0, _targetOpacity,
                new Duration(TimeSpan.FromMilliseconds(170)));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            TacAnim.Fix(fade);
            fade.Completed += delegate { FinishEntrance(); };
            target.BeginAnimation(UIElement.OpacityProperty, fade);

            PlayBrackets();
        }

        private void StartRegionSync()
        {
            StopRegionSync();
            _regionTimer = new DispatcherTimer(DispatcherPriority.Render);
            _regionTimer.Interval = TimeSpan.FromMilliseconds(8);
            _regionTimer.Tick += delegate { SyncRegionFromClip(); };
            _regionTimer.Start();
        }

        private void StopRegionSync()
        {
            if (_regionTimer != null)
            {
                try { _regionTimer.Stop(); } catch { }
                _regionTimer = null;
            }
        }

        private void SyncRegionFromClip()
        {
            if (!_entranceOn || _rootClip == null) return;
            Rect r = _rootClip.Rect;
            if (r.Height < 1) r.Height = 1;
            ApplyWindowRegion(r);
            if (_seamShift != null) _seamShift.Y = Math.Max(0, r.Top);
            if (_entEdgeTop != null)
            {
                TranslateTransform t = _entEdgeTop.RenderTransform as TranslateTransform;
                if (t != null) t.Y = Math.Max(0, r.Top + r.Height - 3);
            }
            if (_entEdgeBottom != null) _entEdgeBottom.Opacity = 0;
        }

        private void ApplyWindowRegion(Rect dip)
        {
            try
            {
                PresentationSource src = PresentationSource.FromVisual(this);
                if (src == null || src.CompositionTarget == null) return;
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                Matrix m = src.CompositionTarget.TransformToDevice;
                int l = (int)Math.Floor(dip.Left * m.M11);
                int t = (int)Math.Floor(dip.Top * m.M22);
                int r = (int)Math.Ceiling((dip.Left + dip.Width) * m.M11);
                int b = (int)Math.Ceiling((dip.Top + dip.Height) * m.M22);
                if (r <= l) r = l + 1;
                if (b <= t) b = t + 1;
                IntPtr rgn = CreateRectRgn(l, t, r, b);
                if (SetWindowRgn(hwnd, rgn, true) == 0) DeleteObject(rgn);
            }
            catch { }
        }

        private void ClearWindowRegion()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) SetWindowRgn(hwnd, IntPtr.Zero, true);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void PlayBrackets()
        {
            if (_entBrackets == null) return;
            _entBrackets.Opacity = 0;
            DoubleAnimationUsingKeyFrames fade = new DoubleAnimationUsingKeyFrames();
            fade.Duration = new Duration(TimeSpan.FromMilliseconds(840));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            _entBrackets.BeginAnimation(UIElement.OpacityProperty, fade);
            for (int i = 0; i < _entCorners.Count; i++)
            {
                TranslateTransform t = _entCorners[i].RenderTransform as TranslateTransform;
                if (t == null) continue;
                t.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(_cornerFromX[i], 0,
                        new Duration(TimeSpan.FromMilliseconds(260)))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                t.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(_cornerFromY[i], 0,
                        new Duration(TimeSpan.FromMilliseconds(260)))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        }

        private void FinishEntrance()
        {
            if (_root == null) return;
            StopRegionSync();
            ClearWindowRegion();
            _root.Clip = null;
            _rootClip = null;
            _entranceOn = false;
            SetBaseOpacity();
            StaggerIn(_pageScroll[_page]);
            DispatcherTimer cleanup = new DispatcherTimer();
            cleanup.Interval = TimeSpan.FromMilliseconds(320);
            cleanup.Tick += delegate
            {
                cleanup.Stop();
                if (_entrance != null && !_entranceOn) _entrance.Visibility = Visibility.Collapsed;
                if (_entBrackets != null)
                {
                    for (int i = 0; i < _entCorners.Count; i++)
                    {
                        TranslateTransform t = _entCorners[i].RenderTransform as TranslateTransform;
                        if (t == null) continue;
                        t.BeginAnimation(TranslateTransform.XProperty, null);
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.X = _cornerFromX[i];
                        t.Y = _cornerFromY[i];
                    }
                }
            };
            cleanup.Start();
        }

        private void StaggerIn(ScrollViewer sv)
        {
            if (!_animated || UiAnim.Off || sv == null) return;
            StackPanel panel = sv.Content as StackPanel;
            if (panel == null) return;
            int n = Math.Min(5, panel.Children.Count);
            for (int i = 0; i < n; i++)
            {
                UIElement e = panel.Children[i];
                e.Opacity = 0.55;
                TranslateTransform t = new TranslateTransform(0, 6);
                e.RenderTransform = t;
                e.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.55, 1, new Duration(TimeSpan.FromMilliseconds(200)))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 30),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                t.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(6, 0, new Duration(TimeSpan.FromMilliseconds(220)))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 30),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        }

        private void PlayExit()
        {
            if (_root == null || !_animated || UiAnim.Off)
            {
                Hide();
                return;
            }
            StopEntrance();
            UpdateLayout();
            double w = ActualWidth > 20 ? ActualWidth : Width;
            double h = ActualHeight > 20 ? ActualHeight : Height;

            int style = _cfg == null ? 0 : _cfg.TacticalOpenStyle;
            if (style == 2)
            {
                UIElement target = _transparentMode && _root != null ? (UIElement)_root : this;
                double from = _transparentMode ? _targetOpacity : 1;
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Opacity = from;
                DoubleAnimation fade = new DoubleAnimation(from, 0,
                    new Duration(TimeSpan.FromMilliseconds(150)));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                TacAnim.Fix(fade);
                fade.Completed += delegate
                {
                    SetBaseOpacity();
                    Hide();
                };
                target.BeginAnimation(UIElement.OpacityProperty, fade);
                return;
            }

            _entranceOn = true;
            _entranceClosing = true;
            _entrance.Visibility = Visibility.Visible;
            _entSeam.Opacity = 0.95;
            if (_entEdgeTop != null)
            {
                _entEdgeTop.Opacity = 0.95;
                if (_entEdgeTop.RenderTransform is TranslateTransform)
                    ((TranslateTransform)_entEdgeTop.RenderTransform).Y = h - 3;
            }
            if (_seamShift != null) _seamShift.Y = style == 1 ? 0 : 0;

            Rect slit = style == 1 ? new Rect(0, 0, w, 2) : new Rect(0, h / 2 - 1, w, 2);
            _rootClip = new RectangleGeometry(new Rect(0, 0, w, h));
            _root.Clip = _rootClip;
            StartRegionSync();

            RectAnimation close = new RectAnimation(new Rect(0, 0, w, h), slit,
                new Duration(TimeSpan.FromMilliseconds(200)));
            close.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            TacAnim.Fix(close);
            close.Completed += delegate
            {
                StopRegionSync();
                ClearWindowRegion();
                _root.Clip = null;
                _rootClip = null;
                _entranceOn = false;
                _entranceClosing = false;
                if (_entrance != null) _entrance.Visibility = Visibility.Collapsed;
                Hide();
            };
            _rootClip.BeginAnimation(RectangleGeometry.RectProperty, close);
        }

        private void StopEntrance()
        {
            _entranceOn = false;
            _entranceClosing = false;
            StopRegionSync();
            ClearWindowRegion();
            SetBaseOpacity();
            if (_rootClip != null)
            {
                _rootClip.BeginAnimation(RectangleGeometry.RectProperty, null);
                _rootClip = null;
            }
            if (_root != null) _root.Clip = null;
            if (_bodyShell != null)
            {
                _bodyShell.BeginAnimation(UIElement.OpacityProperty, null);
                _bodyShell.Opacity = 1;
            }
            if (_entrance != null) _entrance.Visibility = Visibility.Collapsed;
            if (_entEdgeTop != null) _entEdgeTop.Opacity = 0;
            if (_entEdgeBottom != null) _entEdgeBottom.Opacity = 0;
        }


        private void ApplyAppearance()
        {
            int op = _cfg == null ? 100 : _cfg.TacticalOpacity;
            if (op < 40) op = 40;
            if (op > 100) op = 100;
            _targetOpacity = op / 100.0;
            bool needTransparent = op < 100;
            if (needTransparent != _transparentMode)
            {
                if (_recreate != null)
                {
                    Dispatcher.BeginInvoke(new Action(_recreate));
                    return;
                }
                _transparentMode = needTransparent;
            }
            SetBaseOpacity();
        }

        private void SetBaseOpacity()
        {
            if (_root != null) _root.BeginAnimation(UIElement.OpacityProperty, null);
            BeginAnimation(UIElement.OpacityProperty, null);
            if (_transparentMode)
            {
                Opacity = 1;
                if (_root != null) _root.Opacity = _targetOpacity;
            }
            else
            {
                if (_root != null) _root.Opacity = 1;
                Opacity = 1;
            }
        }

        private void RebuildPages()
        {
            BuildPages();
            SeedInputs();
            RefreshAll();
            GoPage(_page, false);
        }

        public void ApplyTacticalConfig()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ApplyTacticalConfig));
                return;
            }
            ApplyAppearance();
            List<string> parsed = TacWidgets.Parse(_cfg == null ? TacWidgets.Default : _cfg.TacticalWidgets);
            bool changed = parsed.Count != _layout.Count;
            if (!changed)
            {
                for (int i = 0; i < parsed.Count; i++)
                {
                    if (parsed[i] != _layout[i]) { changed = true; break; }
                }
            }
            if (!changed) return;
            _layout = parsed;
            RebuildPages();
        }

        public void OpenEditor()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OpenEditor));
                return;
            }
            _layoutBackup = TacWidgets.Serialize(_layout);
            _editMode = true;
            if (!IsVisible)
            {
                BuildPages();
                GoPage(0, false);
                OpenPanel();
                return;
            }
            BuildPages();
            GoPage(0);
            RefreshAll();
        }

        public void CloseEditor()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CloseEditor));
                return;
            }
            _editMode = false;
            SaveLayout();
            SaveNow();
            BuildPages();
            GoPage(0);
            RefreshAll();
        }

        public void CancelEditor()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CancelEditor));
                return;
            }
            _layout = TacWidgets.Parse(_layoutBackup);
            _editMode = false;
            SaveLayout();
            BuildPages();
            GoPage(0);
            RefreshAll();
        }

        public void ResetLayout()
        {
            _layout = TacWidgets.Parse(TacWidgets.Default);
            SaveLayout();
            BuildPages();
            GoPage(0);
            RefreshAll();
        }

        private void SaveLayout()
        {
            if (_cfg != null) _cfg.TacticalWidgets = TacWidgets.Serialize(_layout);
            ScheduleSave();
        }

        public bool IsOpenOrOpening
        {
            get { return IsVisible; }
        }

        public bool IsCalibrationTab
        {
            get { return _page == 3; }
        }

        public void GoToPage(int index)
        {
            GoPage(index);
            RefreshAll();
        }

        public void OpenPanel()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OpenPanel));
                return;
            }
            try
            {
                if (_cfg != null && _recreate != null
                    && ((_cfg.TacticalOpacity < 100) != _transparentMode))
                {
                    Dispatcher.BeginInvoke(new Action(_recreate));
                    return;
                }
                ApplyTacticalConfig();
                _prevForeground = NativeMethods.GetForegroundWindow();
                SeedInputsIfNeeded();
                GoPage(_page, false);
                if (IsVisible)
                {
                    if (_entranceClosing) StopEntrance();
                    RefreshAll();
                    Activate();
                    return;
                }

                Show();
                Topmost = true;
                Activate();
                try
                {
                    IntPtr h = new WindowInteropHelper(this).Handle;
                    NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                    NativeMethods.ForceFocus(h);
                }
                catch { }

                ResetNumbers();
                RefreshAll();
                PlayEntrance();
                PulseDataChanged();
            }
            catch (Exception ex) { Logger.Err("战术", "打开新版战术面板失败", ex); }
        }

        public void OpenOverviewPanel()
        {
            GoPage(0);
            OpenPanel();
        }

        public void OpenCalibrationTab()
        {
            GoPage(3);
            OpenPanel();
        }

        public void HidePanel()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HidePanel));
                return;
            }
            try
            {
                if (_editMode)
                {
                    _editMode = false;
                    SaveLayout();
                }
                CommitCurInputs(true);
                ConfirmGenerators();
                SaveNow();
                if (!IsVisible) return;

                if (_prevForeground != IntPtr.Zero)
                {
                    try { NativeMethods.ForceFocus(_prevForeground); } catch { }
                    _prevForeground = IntPtr.Zero;
                }

                PlayExit();
            }
            catch (Exception ex) { Logger.Err("战术", "收起新版战术面板失败", ex); }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_editMode) CloseEditor();
                else HidePanel();
                return;
            }
            if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is Button) return;
                e.Handled = true;
                ConfirmGenerators();
                SaveNow();
                HidePanel();
                return;
            }
            if (e.Key == Key.Tab)
            {
                return;
            }
        }


        public void RefreshFromModel()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshFromModel));
                return;
            }
            RefreshAll();
            if (IsVisible) PulseDataChanged();
        }

        public void SetTicketSummary(string text)
        {
            _ticketSummary = text == null ? "" : text;
            RefreshFromModel();
        }

        public void SetWaveInfo(string order, string predict)
        {
            RefreshFromModel();
        }

        public void PulseDataChanged()
        {
            if (_headerPulse == null) return;
            try
            {
                if (!_animated)
                {
                    _headerPulse.Opacity = 0;
                    return;
                }
                _headerPulse.Opacity = 1;
                DoubleAnimation fade = new DoubleAnimation(1, 0,
                    new Duration(TimeSpan.FromMilliseconds(520)));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                TacAnim.Fix(fade);
                _headerPulse.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch { }
        }


        private void RefreshAll()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshAll));
                return;
            }
            if (_syncing || !_shellBuilt) return;
            _syncing = true;
            try
            {
                DateTime now = DateTime.Now;
                CommitCurInputs(false);
                ApplyModelToInputs();
                UpdateHead(now);
                UpdateTicker(now);
                UpdateOverview(now);
                UpdateRoster(now);
                UpdateObjectives(now);
                UpdateCalibrate(now);
                UpdateFooter();
            }
            catch (Exception ex)
            {
                Logger.Err("战术", "刷新新版战术面板失败", ex);
            }
            finally { _syncing = false; }
        }

        private void UpdateHead(DateTime now)
        {
            if (_headRight1 == null) return;
            string r1;
            string r2;
            BuildHeadStatus(now, out r1, out r2);
            _headRight1.Text = r1;
            _headRight2.Text = r2;
        }

        private void BuildHeadStatus(DateTime now, out string line1, out string line2)
        {
            VigilSnapshot snap = null;
            try { if (_snapshot != null) snap = _snapshot(); } catch { }
            string game = snap != null && snap.GameRunning ? "GAME RUNNING" : "GAME OFFLINE";
            if (_page == 0)
            {
                line1 = "REFRESH 0.25 s     ·     " + game + "     ·     " + _log.WaveHistory.Count + " WAVES";
                line2 = TimeEstimator.Clock(_log.RoundElapsedNow(), false);
                return;
            }
            if (_page == 1)
            {
                int present = 0;
                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    TacticalLog.ScpRecord rec = _log.Find(TacticalLog.ScpCodes[i]);
                    if (rec != null && rec.Present) present++;
                }
                line1 = "GEN " + _log.GeneratorsActive + " / " + TacticalLog.GeneratorCount
                    + "     ·     KILLED " + _log.TotalKills + "     ·     " + game;
                line2 = present + " / " + TacticalLog.ScpCodes.Length;
                return;
            }
            if (_page == 2)
            {
                line1 = "BONUS " + _log.Tickets.BonusTokensLeft + " / " + _log.Tickets.MaxBonusTokens
                    + "     ·     MTF " + _log.TicketsOf(TacticalLog.MTF)
                    + "     ·     CHAOS " + _log.TicketsOf(TacticalLog.CHAOS);
                line2 = _log.TotalTickets.ToString();
                return;
            }
            line1 = "CALIBRATION     ·     " + game + "     ·     "
                + (_log.HasWaveAnchor ? "ANCHORED" : "UNANCHORED");
            line2 = TimeEstimator.Clock(_log.RoundElapsedNow(), false);
        }

        private void UpdateTicker(DateTime now)
        {
            if (_ticker == null) return;
            string next = _log.WaveClockRunning
                ? TimeEstimator.Clock(Math.Max(0, _log.WaveRemaining(now, TacticalLog.MTF)), true)
                : "--:--";
            string game;
            VigilSnapshot snap = null;
            try { if (_snapshot != null) snap = _snapshot(); } catch { }
            if (snap == null) game = "OFFLINE";
            else if (snap.SelfDead) game = "DEAD";
            else if (snap.GameRunning) game = "RUNNING";
            else game = "OFFLINE";

            string role = snap != null && !string.IsNullOrEmpty(snap.Role) ? Trim(snap.Role, 18) : "—";
            _ticker.Text = "STATE " + game + "   |   ROLE " + role + "   |   MTF " + TokText(TacticalLog.MTF)
                + "   CHAOS " + TokText(TacticalLog.CHAOS) + "   |   NEXT WAVE " + next
                + "   |   SCP ALIVE " + (TacticalLog.ScpCodes.Length - _log.TotalKills)
                + " / " + TacticalLog.ScpCodes.Length;
        }

        private string TokText(string faction)
        {
            if (_log.Tickets.HasUnknownHistory(faction)) return "?";
            return _log.TokensOf(faction).ToString();
        }

        private void UpdateFooter()
        {
            if (_footerLeft == null) return;
            _footerLeft.Text = "ENTER 确认并收起     ·     ESC 收起     ·     F10 开关面板     ·     TAB 移动焦点";
        }


        private static double Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double v;
            if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static int IntNum(string s)
        {
            double d = Num(s);
            if (d < 0) return 0;
            if (d > int.MaxValue) return int.MaxValue;
            return (int)d;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string one = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return one.Length <= max ? one : one.Substring(0, max) + "…";
        }

        private void SeedInputs()
        {
            _syncing = true;
            _curDirty.Clear();
            try
            {
                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    string code = TacticalLog.ScpCodes[i];
                    if (code == "079") continue;
                    ScpRowUi row;
                    if (!_scpRows.TryGetValue(code, out row)) continue;
                    TacticalLog.ScpRecord rec = _log.Find(code);
                    if (rec == null) continue;

                    string cfgHp = _cfg == null ? "" : _cfg.GetTactical("hp_" + code);
                    int v;
                    if (int.TryParse(cfgHp, out v) && v > 0)
                    {
                        rec.TotalHp = v;
                    }
                    else if (rec.TotalHp <= 0)
                    {
                        rec.TotalHp = DefaultHp(code);
                    }
                    row.Total.Text = rec.TotalHp.ToString();
                    row.Cur.Text = rec.CurrentHp >= 0 ? rec.CurrentHp.ToString() : "";

                    if (!_presenceSeeded && !rec.Present)
                        rec.Present = _cfg != null && _cfg.GetTactical("pre_" + code) == "1";
                }

                TacticalLog.ScpRecord r79 = _log.Find("079");
                if (r79 != null && !_presenceSeeded && !r79.Present)
                    r79.Present = _cfg != null && _cfg.GetTactical("pre_079") == "1";
                _presenceSeeded = true;

                for (int g = 0; g < TacticalLog.GeneratorCount; g++)
                {
                    TacGenUi row = _genRows[g];
                    if (row == null) continue;
                    TacticalLog.Generator gen = _log.Generators[g];
                    string k = (g + 1).ToString();
                    string cfgBase = _cfg == null ? "" : _cfg.GetTactical("genbase_" + k);
                    int bv;
                    if (int.TryParse(cfgBase, out bv) && bv > 0) gen.Duration = bv;
                    row.Base.Text = (gen.Duration > 0 ? gen.Duration : 120).ToString();
                    if (gen.Done) row.Remaining.Text = "0";
                    else if (gen.Running) row.Remaining.Text = ((int)Math.Ceiling(gen.Remaining(DateTime.Now))).ToString();
                    else row.Remaining.Text = (_cfg == null ? "" : _cfg.GetTactical("gen_" + k));
                }
            }
            finally { _syncing = false; }
        }

        private bool _seeded;
        private bool _presenceSeeded;
        private void SeedInputsIfNeeded()
        {
            if (_seeded) return;
            _seeded = true;
            try { SeedInputs(); } catch { }
        }

        private static int DefaultHp(string code)
        {
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                if (TacticalLog.ScpCodes[i] == code) return TacticalLog.ScpDefaultHp[i];
            return 3000;
        }

        private void ApplyModelToInputs()
        {
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;
                ScpRowUi row;
                if (!_scpRows.TryGetValue(code, out row)) continue;
                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec == null) continue;

                if (row.Total.Box != null && !row.Total.Box.IsKeyboardFocusWithin)
                {
                    string want = rec.TotalHp > 0 ? rec.TotalHp.ToString() : row.Total.Text;
                    if (row.Total.Text != want) row.Total.Text = want;
                }
                if (row.Cur.Box != null && !row.Cur.Box.IsKeyboardFocusWithin)
                {
                    string want = rec.CurrentHp >= 0 ? rec.CurrentHp.ToString() : "";
                    if (row.Cur.Text != want) row.Cur.Text = want;
                }
            }

            DateTime now = DateTime.Now;
            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                TacGenUi row = _genRows[g];
                if (row == null) continue;
                TacticalLog.Generator gen = _log.Generators[g];
                if (row.Remaining.Box != null && !row.Remaining.Box.IsKeyboardFocusWithin
                    && !_genPending[g])
                {
                    string want;
                    if (gen.Done) want = "0";
                    else if (gen.Running) want = ((int)Math.Ceiling(gen.Remaining(now))).ToString();
                    else want = "";
                    if (row.Remaining.Text != want) row.Remaining.Text = want;
                }
            }
        }

        private void OnTotalHpEdited(string code, string raw)
        {
            if (_syncing) return;
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec == null) return;
            int v = IntNum(raw);
            if (rec.TotalHp == v) return;
            rec.TotalHp = v;
            if (v > 0) _log.OnTotalHpChanged(rec);
            if (_cfg != null) _cfg.SetTactical("hp_" + code, v > 0 ? v.ToString() : "");
            ScheduleSave();
            RefreshAll();
        }

        private void OnCurrentHpEdited(string code, string raw)
        {
            if (_syncing) return;
            _curDirty.Add(code);
            ScheduleSave();
        }

        private void CommitCurInputs(bool force)
        {
            if (_curDirty.Count == 0) return;
            List<string> done = new List<string>();
            foreach (string code in _curDirty)
            {
                ScpRowUi row;
                if (!_scpRows.TryGetValue(code, out row)) { done.Add(code); continue; }
                if (!force && row.Cur.Box != null && row.Cur.Box.IsKeyboardFocusWithin) continue;

                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec != null)
                {
                    string raw = row.Cur.Text.Trim();
                    if (raw.Length == 0) rec.SetCurrentHp(-1);
                    else rec.SetCurrentHp(IntNum(raw));
                    if (_cfg != null) _cfg.SetTactical("cur_" + code, raw);
                }
                done.Add(code);
            }
            for (int i = 0; i < done.Count; i++) _curDirty.Remove(done[i]);
        }

        private void OnGenBaseEdited(int index, string raw)
        {
            if (_syncing) return;
            double d = Num(raw);
            if (d <= 0) d = 120;
            _log.Generators[index].Duration = (int)d;
            if (_cfg != null) _cfg.SetTactical("genbase_" + (index + 1), ((int)d).ToString());
            ScheduleSave();
        }

        private void OnGenRemainingEdited(int index)
        {
            if (_syncing) return;
            _genPending[index] = true;
            ScheduleSave();
        }

        private void ConfirmGenerators()
        {
            bool any = false;
            for (int i = 0; i < _genPending.Length; i++)
            {
                if (!_genPending[i]) continue;
                _genPending[i] = false;
                AdoptGeneratorInput(i);
                any = true;
            }
            if (any) { RefreshAll(); ScheduleSave(); }
        }

        private void AdoptGeneratorInput(int index)
        {
            if (index < 0 || index >= TacticalLog.GeneratorCount) return;
            TacGenUi row = _genRows[index];
            if (row == null) return;
            TacticalLog.Generator gen = _log.Generators[index];
            if (gen.Done) return;

            double baseSec = Num(row.Base.Text);
            if (baseSec <= 0) baseSec = 120;
            gen.Duration = (int)baseSec;

            string raw = row.Remaining.Text;
            if (string.IsNullOrEmpty(raw))
            {
                gen.Running = false;
                gen.ManualRemaining = -1;
                return;
            }
            int want = IntNum(raw);
            gen.Running = true;
            gen.StartedAt = DateTime.Now;
            gen.ManualRemaining = want;
            if (gen.Faction == TacticalLog.NONE) gen.Faction = _bigWaveSide;
        }

        private void ScheduleSave()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            if (_cfg == null) return;
            try
            {
                DateTime now = DateTime.Now;
                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    string code = TacticalLog.ScpCodes[i];
                    if (code == "079") continue;
                    ScpRowUi row;
                    if (!_scpRows.TryGetValue(code, out row)) continue;
                    if (row.Total.Box != null)
                        _cfg.SetTactical("hp_" + code, row.Total.Text.Trim());
                    if (row.Cur.Box != null)
                        _cfg.SetTactical("cur_" + code, row.Cur.Text.Trim());
                }
                for (int g = 0; g < TacticalLog.GeneratorCount; g++)
                {
                    TacGenUi row = _genRows[g];
                    if (row == null) continue;
                    string k = (g + 1).ToString();
                    _cfg.SetTactical("genbase_" + k, row.Base.Text.Trim());
                    TacticalLog.Generator gen = _log.Generators[g];
                    _cfg.SetTactical("gen_" + k,
                        gen.Done ? "0" : (gen.Running ? ((int)Math.Ceiling(gen.Remaining(now))).ToString() : ""));
                }
                for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                {
                    TacticalLog.ScpRecord r = _log.Find(TacticalLog.ScpCodes[i]);
                    if (r == null) continue;
                    _cfg.SetTactical("pre_" + TacticalLog.ScpCodes[i], r.Present ? "1" : "");
                }
                if (_onSaved != null) _onSaved();
            }
            catch (Exception ex) { Logger.Err("战术", "新版战术面板保存失败", ex); }
        }


        public void MarkBigWaveNow()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(MarkBigWaveNow));
                return;
            }
            try
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
                RefreshAll();
                ScheduleSave();
                PulseDataChanged();
            }
            catch (Exception ex) { Logger.Err("战术", "新版战术面板记录刷新失败", ex); }
        }

        private void ResetRound()
        {
            try
            {
                _log.ResetRound();
                _bigWaveSide = TacticalLog.MTF;
                _manualCounts.Clear();
                _objOrder.Clear();
                _curDirty.Clear();
                _syncing = true;
                try
                {
                    for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
                    {
                        ScpRowUi row;
                        if (_scpRows.TryGetValue(TacticalLog.ScpCodes[i], out row) && row.Cur.Box != null)
                            row.Cur.Text = "";
                    }
                }
                finally { _syncing = false; }
                RefreshAll();
                ScheduleSave();
                Logger.On("战术", "新版战术面板：清空本局数据");
            }
            catch (Exception ex) { Logger.Err("战术", "清空本局失败", ex); }
        }
    }
}
