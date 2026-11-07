using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SCPSLAlert
{
    internal sealed class WpfSettingsWindow : Window
    {
        private readonly Config _cfg;
        private readonly AlertManager _mgr;
        private readonly Action _save;
        private readonly Action _exit;
        private readonly Action _launch;
        private readonly Action _adjust;

        private readonly string[] _labels = { "总览", "常规", "提示", "音频", "时间", "热键", "日志", "工具" };
        private readonly string[] _icons = { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE8A5", "\uE90F" };
        private readonly string[] _captions =
        {
            "配置检查、常用入口与上手指南",
            "提示开关、显示行为与整体位置",
            "逐条配置提示的内容、样式与条件",
            "提示音、片段、触发点与分配",
            "回合、刷新与倒计时校准",
            "全部热键一览与修改",
            "运行记录与排查入口",
            "游戏亮度、准星与显示辅助"
        };

        private readonly DispatcherTimer _saveTimer;
        private int _page;
        private readonly List<Border> _navItems = new List<Border>();
        private readonly List<TextBlock> _navLabels = new List<TextBlock>();
        private readonly List<TextBlock> _navIcons = new List<TextBlock>();
        private readonly List<Border> _navBars = new List<Border>();
        private Grid _shell;
        private ScrollViewer _scroll;
        private StackPanel _pageHost;
        private TextBlock _pageTitle;
        private TextBlock _pageCaption;
        private TextBlock _themeChip;
        private Button _themeButton;

        public WpfSettingsWindow(Config cfg, AlertManager mgr, Action save, Action exit, Action launch, Action adjust)
        {
            WpfScroll.Apply(this);
            _cfg = cfg;
            _mgr = mgr;
            _save = save;
            _exit = exit;
            _launch = launch;
            _adjust = adjust;

            ModernTheme.SetMode(_cfg == null ? -1 : _cfg.UiThemeMode);

            WpfTheme.Start();
            Title = "SCPSLAlert 控制台";
            Width = 1280;
            Height = 860;
            MinWidth = 1080;
            MinHeight = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = System.Windows.WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = WpfTheme.PageBg;
            Foreground = WpfTheme.Text;
            FontFamily = WpfTheme.UiFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveTimer.Tick += delegate { _saveTimer.Stop(); if (_save != null) _save(); };

            WpfTheme.Changed += OnThemeChanged;
            Closed += delegate { WpfTheme.Changed -= OnThemeChanged; _saveTimer.Stop(); };

            BuildShell();
            SelectPage(0);
        }

        private void OnThemeChanged()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                int page = _page;
                BuildShell();
                SelectPage(page);
            }));
        }

        private void BuildShell()
        {
            Background = WpfTheme.PageBg;
            Foreground = WpfTheme.Text;
            _navItems.Clear();
            _navLabels.Clear();
            _navIcons.Clear();
            _navBars.Clear();

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
            Content = root;

            root.Children.Add(BuildHeader());

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            body.Children.Add(BuildNav());

            _scroll = new ScrollViewer();
            _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _scroll.Padding = new Thickness(30, 24, 30, 24);
            _scroll.Background = WpfTheme.PageBg;
            Grid.SetColumn(_scroll, 1);
            body.Children.Add(_scroll);

            _pageHost = new StackPanel();
            _pageHost.MaxWidth = 1060;
            _pageHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            _scroll.Content = _pageHost;

            _pageTitle = new TextBlock();
            _pageTitle.FontSize = 27;
            _pageTitle.FontWeight = FontWeights.Bold;
            _pageTitle.Foreground = WpfTheme.Text;
            _pageCaption = new TextBlock();
            _pageCaption.FontSize = 12;
            _pageCaption.Foreground = WpfTheme.SubText;
            _pageCaption.Margin = new Thickness(0, 4, 0, 0);
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(2, 0, 0, 18);
            head.Children.Add(_pageTitle);
            head.Children.Add(_pageCaption);
            _pageHost.Children.Add(head);

            root.Children.Add(BuildFooter());
        }

        private UIElement BuildHeader()
        {
            Border header = new Border();
            header.Background = WpfTheme.HeaderBg;
            header.BorderBrush = WpfTheme.Border;
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) ToggleMaximize();
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Grid.SetRow(header, 0);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = grid;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(24, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            Border mark = new Border();
            mark.Width = 38;
            mark.Height = 38;
            mark.CornerRadius = new CornerRadius(11);
            mark.Background = WpfTheme.Accent;
            TextBlock markText = new TextBlock();
            markText.Text = "S";
            markText.FontFamily = WpfTheme.BrandFont;
            markText.FontSize = 22;
            markText.FontWeight = FontWeights.Bold;
            markText.Foreground = Brushes.White;
            markText.HorizontalAlignment = HorizontalAlignment.Center;
            markText.VerticalAlignment = VerticalAlignment.Center;
            mark.Child = markText;
            brand.Children.Add(mark);
            StackPanel word = new StackPanel();
            word.Margin = new Thickness(12, 0, 0, 0);
            word.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = new TextBlock();
            name.Text = "SCPSLALERT";
            name.FontFamily = WpfTheme.BrandFont;
            name.FontSize = 15;
            name.FontWeight = FontWeights.Bold;
            name.Foreground = WpfTheme.Text;
            TextBlock sub = new TextBlock();
            sub.Text = "FIELD CONTROL  /  WPF";
            sub.FontFamily = WpfTheme.BrandFont;
            sub.FontSize = 8.5;
            sub.Foreground = WpfTheme.SubText;
            sub.Margin = new Thickness(0, 1, 0, 0);
            word.Children.Add(name);
            word.Children.Add(sub);
            brand.Children.Add(word);
            grid.Children.Add(brand);

            _themeButton = new Button();
            _themeButton.Content = ThemeButtonText();
            _themeButton.FontSize = 11.5;
            _themeButton.FontFamily = WpfTheme.UiFont;
            _themeButton.Foreground = WpfTheme.Text;
            _themeButton.Background = WpfTheme.MutedBg;
            _themeButton.BorderBrush = WpfTheme.Border;
            _themeButton.BorderThickness = new Thickness(1);
            _themeButton.Padding = new Thickness(12, 7, 12, 7);
            _themeButton.Margin = new Thickness(0, 0, 150, 0);
            _themeButton.VerticalAlignment = VerticalAlignment.Center;
            _themeButton.HorizontalAlignment = HorizontalAlignment.Right;
            _themeButton.Cursor = Cursors.Hand;
            _themeButton.ToolTip = "点击切换：跟随系统 → 浅色 → 深色";
            _themeButton.Click += delegate { CycleTheme(); };
            Grid.SetColumn(_themeButton, 2);
            grid.Children.Add(_themeButton);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.VerticalAlignment = VerticalAlignment.Center;
            buttons.Margin = new Thickness(0, 0, 10, 0);
            buttons.Children.Add(WindowButton("\uE921", delegate { WindowState = WindowState.Minimized; }));
            buttons.Children.Add(WindowButton("\uE922", ToggleMaximize));
            buttons.Children.Add(WindowButton("\uE8BB", delegate { Close(); }));
            Grid.SetColumn(buttons, 2);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(buttons);

            return header;
        }

        private string ThemeButtonText()
        {
            int mode = _cfg == null ? -1 : _cfg.UiThemeMode;
            return mode == 0 ? "☼  浅色" : mode == 1 ? "☾  深色"
                : (WpfTheme.IsDark ? "☾  深色 · 跟随系统" : "☼  浅色 · 跟随系统");
        }

        private void CycleTheme()
        {
            int mode = _cfg == null ? -1 : _cfg.UiThemeMode;
            mode = mode < 0 ? 0 : mode == 0 ? 1 : -1;
            if (_cfg != null) _cfg.UiThemeMode = mode;
            if (_save != null) _save();
            ModernTheme.SetMode(mode);
            if (_themeButton != null) _themeButton.Content = ThemeButtonText();
        }

        private Button WindowButton(string glyph, Action click)
        {
            Button b = new Button();
            b.Content = glyph;
            b.FontFamily = WpfTheme.IconFont;
            b.FontSize = 10;
            b.Width = 42;
            b.Height = 34;
            b.Margin = new Thickness(2, 0, 2, 0);
            b.Background = Brushes.Transparent;
            b.Foreground = WpfTheme.SubText;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = WindowButtonTemplate();
            b.MouseEnter += delegate { b.Background = WpfTheme.NavHover; b.Foreground = WpfTheme.Text; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; b.Foreground = WpfTheme.SubText; };
            b.Click += delegate { click(); };
            return b;
        }

        private static ControlTemplate WindowButtonTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
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

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private UIElement BuildNav()
        {
            Border rail = new Border();
            rail.Background = WpfTheme.NavBg;
            rail.BorderBrush = WpfTheme.Border;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(rail, 0);

            StackPanel stack = new StackPanel();
            stack.Margin = new Thickness(10, 14, 10, 10);
            rail.Child = stack;

            for (int i = 0; i < _labels.Length; i++)
            {
                int index = i;
                Border item = new Border();
                item.CornerRadius = new CornerRadius(10);
                item.Height = 46;
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Background = Brushes.Transparent;
                item.Cursor = Cursors.Hand;

                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition());

                Border bar = new Border();
                bar.CornerRadius = new CornerRadius(2);
                bar.Width = 4;
                bar.Height = 20;
                bar.VerticalAlignment = VerticalAlignment.Center;
                bar.Background = WpfTheme.Accent;
                bar.Opacity = 0;
                row.Children.Add(bar);

                TextBlock icon = new TextBlock();
                icon.Text = _icons[i];
                icon.FontFamily = WpfTheme.IconFont;
                icon.FontSize = 15;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Foreground = WpfTheme.SubText;
                Grid.SetColumn(icon, 1);
                row.Children.Add(icon);

                TextBlock label = new TextBlock();
                label.Text = _labels[i];
                label.FontSize = 13.5;
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Foreground = WpfTheme.SubText;
                Grid.SetColumn(label, 2);
                row.Children.Add(label);

                item.Child = row;
                item.MouseLeftButtonUp += delegate { SelectPage(index); };
                item.MouseEnter += delegate { if (index != _page) AnimateBackground(item, WpfTheme.NavHover); };
                item.MouseLeave += delegate { if (index != _page) AnimateBackground(item, Brushes.Transparent); };
                stack.Children.Add(item);
                _navItems.Add(item);
                _navLabels.Add(label);
                _navIcons.Add(icon);
                _navBars.Add(bar);
            }
            return rail;
        }

        private static void AnimateBackground(Border b, Brush target)
        {
            SolidColorBrush from = b.Background as SolidColorBrush;
            Color start = from == null ? Colors.Transparent : from.Color;
            Color end = ((SolidColorBrush)target).Color;
            SolidColorBrush brush = new SolidColorBrush(start);
            b.Background = brush;
            ColorAnimation a = new ColorAnimation(end, new Duration(TimeSpan.FromMilliseconds(150)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        private UIElement BuildFooter()
        {
            Border footer = new Border();
            footer.Background = WpfTheme.FooterBg;
            footer.BorderBrush = WpfTheme.Border;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            Grid.SetRow(footer, 2);

            Grid grid = new Grid();
            grid.Margin = new Thickness(20, 0, 20, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.Child = grid;

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(FooterButton("预览提示", false, delegate { if (_mgr != null) _mgr.PreviewAll(); }));
            left.Children.Add(FooterButton("调整提示位置", false, delegate { if (_adjust != null) _adjust(); }));
            left.Children.Add(FooterButton("恢复默认", false, delegate { }));
            grid.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(FooterButton("退出程序", false, delegate { if (_exit != null) _exit(); }));
            right.Children.Add(FooterButton("启动游戏", false, delegate { if (_launch != null) _launch(); }));
            right.Children.Add(FooterButton("保存设置", true, delegate { if (_save != null) _save(); }));
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            return footer;
        }

        private WpfButton FooterButton(string text, bool primary, Action click)
        {
            WpfButton b = new WpfButton(text, primary);
            b.Margin = new Thickness(8, 0, 0, 0);
            b.Click += delegate { click(); };
            return b;
        }

        public void GoToPage(int index)
        {
            SelectPage(index);
        }

        private void SelectPage(int index)
        {
            _page = index;
            for (int i = 0; i < _navItems.Count; i++)
            {
                bool selected = i == index;
                _navItems[i].Background = selected ? WpfTheme.NavSelected : Brushes.Transparent;
                _navBars[i].Opacity = selected ? 1 : 0;
                _navLabels[i].Foreground = selected ? WpfTheme.Accent : WpfTheme.SubText;
                _navLabels[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                _navIcons[i].Foreground = selected ? WpfTheme.Accent : WpfTheme.SubText;
            }
            _pageTitle.Text = _labels[index];
            _pageCaption.Text = _captions[index];

            while (_pageHost.Children.Count > 1) _pageHost.Children.RemoveAt(1);
            UIElement page = BuildPage(index);
            page.Opacity = 0;
            TranslateTransform slide = new TranslateTransform(0, 10);
            page.RenderTransform = slide;
            _pageHost.Children.Add(page);
            DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            page.BeginAnimation(UIElement.OpacityProperty, fade);
            DoubleAnimation move = new DoubleAnimation(10, 0, new Duration(TimeSpan.FromMilliseconds(220)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.YProperty, move);
            _scroll.ScrollToTop();
        }

        private UIElement BuildPage(int index)
        {
            switch (index)
            {
                case 0: return BuildOverviewPage();
                case 1: return BuildGeneralPage();
                default: return BuildPendingPage(index);
            }
        }

        private UIElement BuildOverviewPage()
        {
            StackPanel stack = new StackPanel();
            int enabled = 0;
            for (int i = 0; i < _cfg.Alerts.Length; i++) if (_cfg.Alerts[i].Style.Enabled) enabled++;

            Border hero = Card(null);
            Grid heroGrid = new Grid();
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition());
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heroText = new StackPanel();
            StackPanel chip = new StackPanel();
            chip.Orientation = Orientation.Horizontal;
            Border dot = new Border();
            dot.Width = 8;
            dot.Height = 8;
            dot.CornerRadius = new CornerRadius(4);
            dot.Background = WpfTheme.Accent;
            dot.VerticalAlignment = VerticalAlignment.Center;
            chip.Children.Add(dot);
            TextBlock chipText = new TextBlock();
            chipText.Text = "控制台就绪";
            chipText.FontSize = 11;
            chipText.Foreground = WpfTheme.Accent;
            chipText.Margin = new Thickness(7, 0, 0, 0);
            chip.Children.Add(chipText);
            heroText.Children.Add(chip);
            TextBlock title = new TextBlock();
            title.Text = "所有监控逻辑已在后台运行";
            title.FontSize = 21;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = WpfTheme.Text;
            title.Margin = new Thickness(0, 8, 0, 0);
            TextBlock desc = new TextBlock();
            desc.Text = "提示、OCR、回合计时与热键共用同一套内核。这里只负责把常用操作和状态集中到一起。";
            desc.FontSize = 12.5;
            desc.Foreground = WpfTheme.SubText;
            desc.TextWrapping = TextWrapping.Wrap;
            desc.MaxWidth = 620;
            desc.HorizontalAlignment = HorizontalAlignment.Left;
            desc.Margin = new Thickness(0, 8, 0, 0);
            heroText.Children.Add(title);
            heroText.Children.Add(desc);
            heroGrid.Children.Add(heroText);

            StackPanel stats = new StackPanel();
            stats.VerticalAlignment = VerticalAlignment.Center;
            stats.Margin = new Thickness(24, 0, 4, 0);
            stats.Children.Add(Stat(enabled.ToString(), "已启用提示"));
            stats.Children.Add(Stat(_cfg.EnableOcr ? "已开启" : "未开启", "屏幕识别"));
            Grid.SetColumn(stats, 1);
            heroGrid.Children.Add(stats);
            hero.Child = heroGrid;
            stack.Children.Add(hero);

            ExpanderCard actions = new ExpanderCard("常用操作");
            WrapPanel quick = new WrapPanel();
            quick.Children.Add(Quick("预览全部提示", delegate { if (_mgr != null) _mgr.PreviewAll(); }));
            quick.Children.Add(Quick("调整提示位置", delegate { if (_adjust != null) _adjust(); }));
            quick.Children.Add(Quick("立即保存", delegate { if (_save != null) _save(); }));
            quick.Children.Add(Quick("退出程序", delegate { if (_exit != null) _exit(); }));
            actions.Content = quick;
            stack.Children.Add(actions);

            ExpanderCard guide = new ExpanderCard("第一次用，按这个顺序");
            StackPanel list = new StackPanel();
            list.Children.Add(GuideRow("1", "先进游戏打一局，程序会自动开始计时。"));
            list.Children.Add(GuideRow("2", "按「回合时间校准」键，填一次当前回合已经打了多久。"));
            list.Children.Add(GuideRow("3", "按「战术面板」键，把场上 SCP 的血量和阵营填一遍。"));
            list.Children.Add(GuideRow("4", "看到刷新时，按一下「刷新校准」键，把计时拉回当前这一刻；大小波会自动顺着上一波记。"));
            guide.Content = list;
            stack.Children.Add(guide);

            ExpanderCard hot = new ExpanderCard("常用热键");
            StackPanel keys = new StackPanel();
            keys.Children.Add(KeyRow("战术面板", _cfg.HotkeyTactical));
            keys.Children.Add(KeyRow("回合时间校准", _cfg.HotkeyCalibrate));
            keys.Children.Add(KeyRow("刷新校准", _cfg.HotkeyBigWave));
            keys.Children.Add(KeyRow("核弹 90 秒", _cfg.HotkeyWarhead));
            keys.Children.Add(KeyRow("净化 705 秒", _cfg.HotkeyDecon));
            hot.Content = keys;
            stack.Children.Add(hot);
            return stack;
        }

        private UIElement BuildGeneralPage()
        {
            StackPanel stack = new StackPanel();
            ExpanderCard alerts = new ExpanderCard("提示开关");
            UniformGrid grid = new UniformGrid();
            grid.Columns = 2;
            for (int i = 0; i < _cfg.Alerts.Length; i++)
                grid.Children.Add(AlertToggle(_cfg.Alerts[i]));
            alerts.Content = grid;
            stack.Children.Add(alerts);

            ExpanderCard display = new ExpanderCard("显示行为");
            StackPanel rows = new StackPanel();
            WpfNumberField seconds = new WpfNumberField(110);
            seconds.Minimum = 1;
            seconds.Maximum = 600;
            seconds.Value = _cfg.DisplaySeconds;
            seconds.ValueChanged += delegate
            {
                _cfg.DisplaySeconds = (int)seconds.Value;
                TouchSave();
            };
            rows.Children.Add(SettingRow("事件提示显示", "秒后自动缩回", seconds));

            WpfSwitch persistent = new WpfSwitch();
            persistent.IsChecked = _cfg.RoundTimer.Persistent;
            persistent.CheckedChanged += delegate
            {
                _cfg.RoundTimer.Persistent = persistent.IsChecked;
                TouchSave();
            };
            rows.Children.Add(SettingRow("回合时间常驻显示", "不自动缩回，像游戏内顶部计时", persistent));

            WpfSelect quality = new WpfSelect(220);
            for (int i = 0; i < UiAnim.Names.Length; i++) quality.Items.Add(UiAnim.Names[i]);
            quality.SelectedIndex = Math.Max(0, Math.Min(UiAnim.Names.Length - 1, _cfg.PanelAnimQuality));
            quality.SelectionChanged += delegate
            {
                _cfg.PanelAnimQuality = quality.SelectedIndex;
                UiAnim.Quality = _cfg.PanelAnimQuality;
                TouchSave();
            };
            rows.Children.Add(SettingRow("动画效果", "档位同时决定帧率与额外光效", quality));

            WpfSwitch startup = new WpfSwitch();
            startup.IsChecked = _cfg.StartupShow;
            startup.CheckedChanged += delegate
            {
                _cfg.StartupShow = startup.IsChecked;
                TouchSave();
            };
            rows.Children.Add(SettingRow("启动演出", "启动时播放一次全部提示展开动画", startup));
            display.Content = rows;
            stack.Children.Add(display);

            ExpanderCard position = new ExpanderCard("整体位置");
            StackPanel pos = new StackPanel();
            WpfNumberField x = new WpfNumberField(110);
            x.Minimum = -2000;
            x.Maximum = 2000;
            x.Value = _cfg.OffsetX;
            x.ValueChanged += delegate { _cfg.OffsetX = (int)x.Value; TouchSave(); };
            pos.Children.Add(SettingRow("水平偏移", "像素，可正可负", x));

            WpfNumberField y = new WpfNumberField(110);
            y.Minimum = -2000;
            y.Maximum = 2000;
            y.Value = _cfg.OffsetY;
            y.ValueChanged += delegate { _cfg.OffsetY = (int)y.Value; TouchSave(); };
            pos.Children.Add(SettingRow("垂直偏移", "像素，可正可负", y));

            WpfButton reset = new WpfButton("重置为 0", false);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Click += delegate
            {
                _cfg.OffsetX = 0;
                _cfg.OffsetY = 0;
                x.Value = 0;
                y.Value = 0;
                TouchSave();
            };
            pos.Children.Add(SettingRow("恢复默认布局", "把所有提示位置回到初始值", reset));
            position.Content = pos;
            stack.Children.Add(position);
            return stack;
        }

        private UIElement BuildPendingPage(int index)
        {
            StackPanel stack = new StackPanel();
            Border card = Card(null);
            StackPanel inner = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = _labels[index] + " 正在迁移到 WPF";
            title.FontSize = 19;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = WpfTheme.Text;
            TextBlock desc = new TextBlock();
            desc.Text = "这一页的控件数量较多，正在逐页迁移。当前版本仍然可以切回旧版设置修改这一页，迁移完成后这里会换成新的卡片布局。";
            desc.FontSize = 12.5;
            desc.Foreground = WpfTheme.SubText;
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 8, 0, 0);
            inner.Children.Add(title);
            inner.Children.Add(desc);
            card.Child = inner;
            stack.Children.Add(card);
            return stack;
        }

        private Border Card(UIElement child)
        {
            Border b = new Border();
            b.Background = WpfTheme.CardBg;
            b.BorderBrush = WpfTheme.CardBorder;
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(14);
            b.Padding = new Thickness(22, 18, 22, 18);
            b.Margin = new Thickness(0, 0, 0, 14);
            b.Child = child;
            return b;
        }

        private UIElement Stat(string value, string label)
        {
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(0, 4, 0, 4);
            TextBlock v = new TextBlock();
            v.Text = value;
            v.FontSize = 20;
            v.FontWeight = FontWeights.Bold;
            v.Foreground = WpfTheme.Accent;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 11;
            l.Foreground = WpfTheme.SubText;
            l.HorizontalAlignment = HorizontalAlignment.Right;
            s.Children.Add(v);
            s.Children.Add(l);
            return s;
        }

        private UIElement Quick(string text, Action click)
        {
            WpfButton b = new WpfButton(text, false);
            b.Margin = new Thickness(0, 0, 10, 8);
            b.Click += delegate { click(); };
            return b;
        }

        private UIElement GuideRow(string index, string text)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Margin = new Thickness(0, 6, 0, 6);
            Border dot = new Border();
            dot.Width = 22;
            dot.Height = 22;
            dot.CornerRadius = new CornerRadius(11);
            dot.Background = WpfTheme.AccentSoft;
            TextBlock n = new TextBlock();
            n.Text = index;
            n.FontSize = 11;
            n.FontWeight = FontWeights.Bold;
            n.Foreground = WpfTheme.Accent;
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.VerticalAlignment = VerticalAlignment.Center;
            dot.Child = n;
            g.Children.Add(dot);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12.5;
            t.Foreground = WpfTheme.Text;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        private UIElement KeyRow(string name, string key)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Margin = new Thickness(0, 7, 0, 7);
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 12.5;
            n.Foreground = WpfTheme.SubText;
            g.Children.Add(n);
            Border chip = new Border();
            chip.Background = WpfTheme.MutedBg;
            chip.BorderBrush = WpfTheme.Border;
            chip.BorderThickness = new Thickness(1);
            chip.CornerRadius = new CornerRadius(6);
            chip.Padding = new Thickness(8, 3, 8, 3);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock k = new TextBlock();
            k.Text = string.IsNullOrEmpty(key) ? "未设置" : key;
            k.FontFamily = WpfTheme.BrandFont;
            k.FontSize = 11.5;
            k.Foreground = WpfTheme.Text;
            chip.Child = k;
            Grid.SetColumn(chip, 1);
            g.Children.Add(chip);
            return g;
        }

        private UIElement AlertToggle(AlertConfig alert)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Margin = new Thickness(0, 7, 18, 7);
            WpfSwitch sw = new WpfSwitch();
            sw.IsChecked = alert.Style.Enabled;
            sw.CheckedChanged += delegate
            {
                alert.Style.Enabled = sw.IsChecked;
                TouchSave();
            };
            g.Children.Add(sw);
            TextBlock label = new TextBlock();
            label.Text = alert.DisplayName;
            label.FontSize = 12.5;
            label.Foreground = WpfTheme.Text;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            return g;
        }

        private UIElement SettingRow(string title, string desc, FrameworkElement control)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(8);
            row.Padding = new Thickness(8, 7, 8, 7);
            row.Margin = new Thickness(-8, 1, -8, 1);
            SolidColorBrush hover = new SolidColorBrush(Colors.Transparent);
            row.Background = hover;
            row.MouseEnter += delegate { AnimateBrush(hover, ((SolidColorBrush)WpfTheme.MutedBg).Color); };
            row.MouseLeave += delegate { AnimateBrush(hover, Colors.Transparent); };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 13;
            t.Foreground = WpfTheme.Text;
            TextBlock d = new TextBlock();
            d.Text = desc;
            d.FontSize = 11.5;
            d.Foreground = WpfTheme.SubText;
            d.Margin = new Thickness(0, 2, 0, 0);
            d.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(t);
            if (!string.IsNullOrEmpty(desc)) text.Children.Add(d);
            g.Children.Add(text);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(18, 0, 0, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            row.Child = g;
            return row;
        }

        private static void AnimateBrush(SolidColorBrush brush, Color target)
        {
            ColorAnimation a = new ColorAnimation(target, new Duration(TimeSpan.FromMilliseconds(140)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        private void TouchSave()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }
}
