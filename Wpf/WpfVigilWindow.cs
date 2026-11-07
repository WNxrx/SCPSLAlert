using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace SCPSLAlert
{
    internal sealed class WpfVigilWindow : Window
    {
        private static readonly FontFamily Ui = UiFonts.Body;
        private static readonly FontFamily Display = UiFonts.Title;
        private static readonly FontFamily Mono = UiFonts.Mono;
        private static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private readonly Config _cfg;
        private readonly bool _light;

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
        private Brush Glow;

        public WpfVigilWindow(Config cfg, bool light)
        {
            WpfScroll.Apply(this, light);
            _cfg = cfg;
            _light = light;
            BuildPalette();
            Title = "SCPSLAlert // A.R.C.T";
            try { Icon = AppMark.Image(light, 64); } catch { }
            Width = 1440;
            Height = 900;
            MinWidth = 1180;
            MinHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = System.Windows.WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = Bg;
            Foreground = Ink;
            FontFamily = Ui;
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

            // 亮/暗两套图标：跟随主题切换（任务栏 / Alt+Tab 用窗口图标）
            try
            {
                ModernTheme.Changed += delegate
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            try { Icon = AppMark.Image(!ModernTheme.IsDark, 64); } catch { }
                        }));
                    }
                    catch { }
                };
            }
            catch { }

            Build();
        }

        private void BuildPalette()
        {
            if (_light)
            {
                Bg = Solid(238, 235, 224);
                Panel = Solid(247, 244, 234);
                PanelAlt = Solid(232, 228, 215);
                Line = Solid(27, 36, 32);
                Ink = Solid(18, 24, 21);
                Sub = Solid(74, 86, 80);
                Muted = Solid(138, 147, 140);
                Accent = Solid(11, 107, 74);
                Accent2 = Solid(194, 78, 18);
                Danger = Solid(179, 38, 30);
                Glow = Solid(0, 0, 0);
            }
            else
            {
                Bg = Solid(5, 8, 7);
                Panel = Solid(10, 18, 16);
                PanelAlt = Solid(14, 25, 22);
                Line = Solid(30, 51, 45);
                Ink = Solid(232, 245, 239);
                Sub = Solid(143, 174, 166);
                Muted = Solid(82, 110, 103);
                Accent = Solid(198, 255, 61);
                Accent2 = Solid(255, 138, 42);
                Danger = Solid(255, 77, 77);
                Glow = Solid(198, 255, 61);
            }
        }

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }

        private void Build()
        {
            Grid root = new Grid();
            root.Background = Bg;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            Content = root;

            root.Children.Add(Backdrop());
            root.Children.Add(TickerBar());

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            body.Children.Add(Rail());

            Grid content = new Grid();
            content.Margin = new Thickness(34, 22, 34, 22);
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(128) });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            Grid.SetColumn(content, 1);
            body.Children.Add(content);

            content.Children.Add(PageHead());

            Grid main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.Margin = new Thickness(0, 0, 0, 14);
            Grid.SetRow(main, 1);
            content.Children.Add(main);

            VigilCard vitals = Card();
            vitals.Margin = new Thickness(0, 0, 14, 0);
            vitals.VerticalAlignment = VerticalAlignment.Top;
            vitals.Child = Vitals();
            main.Children.Add(vitals);

            VigilCard game = Card();
            game.BorderBrush = Accent2;
            game.Margin = new Thickness(14, 0, 0, 0);
            game.VerticalAlignment = VerticalAlignment.Top;
            game.Child = GameState();
            Grid.SetColumn(game, 1);
            main.Children.Add(game);

            VigilCard evidence = Card();
            evidence.Margin = new Thickness(0, 14, 0, 0);
            evidence.Child = Evidence();
            Grid.SetRow(evidence, 1);
            Grid.SetColumnSpan(evidence, 2);
            main.Children.Add(evidence);

            VigilCard chain = Card();
            chain.Child = Chain();
            Grid.SetRow(chain, 2);
            content.Children.Add(chain);

            root.Children.Add(BottomStrip());
        }

        private UIElement Backdrop()
        {
            Grid layer = new Grid();
            layer.IsHitTestVisible = false;
            layer.Children.Add(new VigilField(_light, Accent, Line, Muted));
            Grid.SetRowSpan(layer, 3);
            return layer;
        }

        private UIElement TickerBar()
        {
            Border bar = new Border();
            bar.Background = _light ? PanelAlt : Solid(3, 6, 5);
            bar.BorderBrush = Line;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = g;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(16, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            TextBlock v = DisplayText("A.R.C.T", 18, Accent);
            v.FontWeight = FontWeights.Bold;
            brand.Children.Add(v);
            TextBlock slash = MonoText("  //  TACTICAL ALERT TERMINAL", 8.5, Muted);
            slash.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(slash);
            g.Children.Add(brand);

            TextBlock ticker = MonoText("SYS.CHECK  OK     |     OCR  STANDBY     |     HOTKEYS  06     |     CONFIG  SAVED     |     FRAME  120HZ", 8.5, Sub);
            ticker.VerticalAlignment = VerticalAlignment.Center;
            ticker.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(ticker, 1);
            g.Children.Add(ticker);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(MonoText(_light ? "LIGHT" : "DARK", 8.5, Accent));
            right.Children.Add(MonoText("  /  WIN 11  /  2026.10", 8.5, Muted));
            right.Children.Add(WinButton("\uE921", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(WinButton("\uE922", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            right.Children.Add(WinButton("\uE8BB", delegate { Close(); }));
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return bar;
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            DependencyObject node = source;
            while (node != null)
            {
                if (node is Button) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private Button WinButton(string glyph, Action click)
        {
            Button b = new Button();
            b.Content = glyph;
            b.FontFamily = Icons;
            b.FontSize = 9;
            b.Width = 34;
            b.Height = 28;
            b.Margin = new Thickness(2, 0, 0, 0);
            b.Background = Brushes.Transparent;
            b.Foreground = Sub;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = FlatTemplate();
            b.MouseEnter += delegate { b.Background = PanelAlt; b.Foreground = Ink; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; b.Foreground = Sub; };
            b.Click += delegate { click(); };
            return b;
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
            rail.Background = _light ? PanelAlt : Solid(4, 7, 6);
            rail.BorderBrush = Line;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(rail, 0);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            rail.Child = grid;

            TextBlock word = DisplayText("A.R.C.T", 26, Accent);
            word.FontWeight = FontWeights.Bold;
            word.LayoutTransform = new RotateTransform(-90);
            word.HorizontalAlignment = HorizontalAlignment.Center;
            word.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(word, 0);
            grid.Children.Add(word);

            StackPanel icons = new StackPanel();
            icons.Margin = new Thickness(10, 6, 10, 0);
            string[] glyphs = { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE8A5", "\uE90F" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                Border item = new Border();
                item.Width = 56;
                item.Height = 48;
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Cursor = Cursors.Hand;
                item.Background = Brushes.Transparent;
                Grid cell = new Grid();
                if (i == 0)
                {
                    Border bar = new Border();
                    bar.Width = 3;
                    bar.Height = 22;
                    bar.Background = Accent;
                    bar.HorizontalAlignment = HorizontalAlignment.Left;
                    cell.Children.Add(bar);
                }
                TextBlock icon = new TextBlock();
                icon.Text = glyphs[i];
                icon.FontFamily = Icons;
                icon.FontSize = 16;
                icon.Foreground = i == 0 ? Accent : Muted;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                cell.Children.Add(icon);
                item.Child = cell;
                icons.Children.Add(item);
            }
            Grid.SetRow(icons, 1);
            grid.Children.Add(icons);

            TextBlock sys = MonoText("SYS\n04/05", 8, Sub);
            sys.HorizontalAlignment = HorizontalAlignment.Center;
            sys.VerticalAlignment = VerticalAlignment.Center;
            sys.TextAlignment = TextAlignment.Center;
            Grid.SetRow(sys, 2);
            grid.Children.Add(sys);
            return rail;
        }

        private UIElement PageHead()
        {
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            OutlineText code = new OutlineText("01", 98, Accent, 1.2);
            code.HorizontalAlignment = HorizontalAlignment.Left;
            code.VerticalAlignment = VerticalAlignment.Top;
            code.Margin = new Thickness(-6, -18, 0, 0);
            head.Children.Add(code);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 12, 0, 0);
            TextBlock small = MonoText("STATUS  /  SYSTEM SNAPSHOT", 9, Sub);
            title.Children.Add(small);
            TextBlock big = DisplayText("状态", 42, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -2, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "软件、系统与游戏的实时判定";
            desc.FontSize = 11.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 2, 0, 0);
            title.Children.Add(desc);
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock cap = MonoText("TERMINAL LINK", 8, Muted);
            cap.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(cap);
            TextBlock link = MonoText("STABLE", 22, Accent);
            link.FontWeight = FontWeights.Bold;
            link.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(link);
            TextBlock time = MonoText(DateTime.Now.ToString("HH:mm:ss"), 10, Sub);
            time.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(time);
            Grid.SetColumn(right, 2);
            head.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 3);
            head.Children.Add(line);
            return head;
        }

        private VigilCard Card()
        {
            VigilCard c = new VigilCard();
            c.Fill = Panel;
            c.Stroke = Line;
            c.Cut = 16;
            c.Padding = new Thickness(20, 16, 20, 18);
            return c;
        }

        private UIElement Vitals()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Header("SYSTEM VITALS", "VGL / 01"));
            Grid metrics = new Grid();
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.Margin = new Thickness(0, 10, 0, 14);
            metrics.Children.Add(Metric("05", "MODULES ONLINE", Accent));
            StackPanel second = Metric("20", "ALERTS ARMED", Ink);
            Grid.SetColumn(second, 1);
            metrics.Children.Add(second);
            StackPanel third = Metric("120", "FRAME RATE", Accent2);
            Grid.SetColumn(third, 2);
            metrics.Children.Add(third);
            s.Children.Add(metrics);

            s.Children.Add(VitalRow("MAIN PROCESS", "RUNNING", Accent, 1.0));
            s.Children.Add(VitalRow("CONFIG STORE", "SAVED / 23:58", Accent, 0.86));
            s.Children.Add(VitalRow("OCR ENGINE", _cfg.EnableOcr ? "READY" : "STANDBY", _cfg.EnableOcr ? Accent : Accent2, _cfg.EnableOcr ? 0.78 : 0.34));
            s.Children.Add(VitalRow("LOG WRITER", "STREAMING", Accent, 0.92));
            s.Children.Add(VitalRow("HOTKEY HOOK", "06 REGISTERED", Accent, 0.7));
            s.Children.Add(VitalRow("WINDOWS THEME", _light ? "LIGHT / PAPER" : "DARK / TERMINAL", Accent, 0.62));
            s.Children.Add(VitalRow("DPI SCALE", "100%", Sub, 0.5));
            s.Children.Add(VitalRow("UPTIME", "03:21:12", Sub, 0.66));
            s.Children.Add(VitalRow("LOG STREAM", "1.8 MB / 5 MB", Sub, 0.36));
            s.Children.Add(VitalRow("FRAME TIMER", "1 MS / 120 HZ", Accent2, 0.8));
            return s;
        }

        private UIElement GameState()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Header("GAME STATE", "JUDGE / LIVE"));

            Grid hero = new Grid();
            hero.Margin = new Thickness(0, 8, 0, 8);
            hero.ColumnDefinitions.Add(new ColumnDefinition());
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel();
            TextBlock big = DisplayText("存活", 54, Accent);
            big.FontWeight = FontWeights.Bold;
            left.Children.Add(big);
            TextBlock en = MonoText("ALIVE  /  CONFIDENCE 86%", 9, Sub);
            left.Children.Add(en);
            hero.Children.Add(left);

            Border glyph = new Border();
            glyph.Width = 66;
            glyph.Height = 66;
            glyph.BorderBrush = Accent;
            glyph.BorderThickness = new Thickness(1);
            glyph.CornerRadius = new CornerRadius(33);
            TextBlock icon = new TextBlock();
            icon.Text = "\uE73E";
            icon.FontFamily = Icons;
            icon.FontSize = 24;
            icon.Foreground = Accent;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            glyph.Child = icon;
            Grid.SetColumn(glyph, 1);
            hero.Children.Add(glyph);
            s.Children.Add(hero);

            s.Children.Add(GameRow("游戏进程", "运行中", Accent));
            s.Children.Add(GameRow("窗口状态", "前台", Sub));
            s.Children.Add(GameRow("回合", "进行中 03:21", Sub));
            s.Children.Add(GameRow("角色", "未识别", Muted));
            s.Children.Add(GameRow("阵营", "未识别", Muted));
            s.Children.Add(GameRow("服务器", "未连接", Muted));
            s.Children.Add(GameRow("判定依据", "HUD / OCR 00:02 前", Sub));
            s.Children.Add(GameRow("上次证据", "00:02 前", Sub));

            Border bar = new Border();
            bar.Height = 4;
            bar.Margin = new Thickness(0, 14, 0, 0);
            bar.Background = Line;
            Border fill = new Border();
            fill.Width = 280;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Background = Accent;
            bar.Child = fill;
            s.Children.Add(bar);
            TextBlock note = MonoText("SIGNAL CONFIDENCE  /  86", 8, Muted);
            note.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(note);
            return s;
        }

        private UIElement Evidence()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Header("EVIDENCE STREAM", "LAST 04"));
            s.Children.Add(EvidenceRow("00:02", "OCR", "HUD 生命体征识别为存活", Accent));
            s.Children.Add(EvidenceRow("00:14", "LOG", "未检测到回合结束事件", Sub));
            s.Children.Add(EvidenceRow("00:21", "OCR", "票数扫描 37 / 40", Accent2));
            s.Children.Add(EvidenceRow("00:38", "SYS", "配置自动保存完成", Muted));
            return s;
        }

        private UIElement EvidenceRow(string time, string tag, string text, Brush color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock t = MonoText(time, 8.5, Muted);
            g.Children.Add(t);
            TextBlock tg = MonoText(tag, 8.5, color);
            Grid.SetColumn(tg, 1);
            g.Children.Add(tg);
            TextBlock tx = MonoText(text, 9, Sub);
            Grid.SetColumn(tx, 2);
            g.Children.Add(tx);
            return g;
        }

        private UIElement Chain()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel title = new StackPanel();
            title.Children.Add(Header("SIGNAL CHAIN", "FLOW / 04"));
            title.Children.Add(MonoText("LIVE PIPELINE", 8, Muted));
            g.Children.Add(title);

            Grid nodes = new Grid();
            nodes.ColumnDefinitions.Add(new ColumnDefinition());
            nodes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            nodes.ColumnDefinitions.Add(new ColumnDefinition());
            nodes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            nodes.ColumnDefinitions.Add(new ColumnDefinition());
            nodes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            nodes.ColumnDefinitions.Add(new ColumnDefinition());
            nodes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            nodes.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(nodes, 1);
            g.Children.Add(nodes);

            string[] names = { "GAME", "LOG", "OCR", "JUDGE", "ALERT" };
            string[] states = { "LIVE", "STREAM", "READY", "READY", "ARMED" };
            for (int i = 0; i < 5; i++)
            {
                UIElement node = Node(names[i], states[i], i == 4 ? Accent2 : Accent);
                Grid.SetColumn(node, i * 2);
                nodes.Children.Add(node);
                if (i < 4)
                {
                    Border connector = new Border();
                    connector.Height = 1;
                    connector.Background = Line;
                    connector.VerticalAlignment = VerticalAlignment.Center;
                    connector.Margin = new Thickness(6, 0, 6, 0);
                    Grid.SetColumn(connector, i * 2 + 1);
                    nodes.Children.Add(connector);
                }
            }
            return g;
        }

        private UIElement Node(string name, string state, Brush color)
        {
            StackPanel s = new StackPanel();
            s.HorizontalAlignment = HorizontalAlignment.Center;
            s.VerticalAlignment = VerticalAlignment.Center;
            Border dot = new Border();
            dot.Width = 10;
            dot.Height = 10;
            dot.CornerRadius = new CornerRadius(5);
            dot.BorderBrush = color;
            dot.BorderThickness = new Thickness(1);
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock inner = new TextBlock();
            inner.Text = ".";
            s.Children.Add(dot);
            TextBlock n = MonoText(name, 9, color);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.Margin = new Thickness(0, 5, 0, 0);
            s.Children.Add(n);
            TextBlock st = MonoText(state, 7.5, Muted);
            st.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(st);
            return s;
        }

        private UIElement BottomStrip()
        {
            Border b = new Border();
            b.Background = _light ? PanelAlt : Solid(3, 6, 5);
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            Grid.SetRow(b, 2);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock left = MonoText("RECENT EVIDENCE   //   OCR: HUD NOMINAL   ·   LOG: NO ROUND EVENT   ·   JUDGE: ALIVE (86%)", 8.5, Sub);
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(16, 0, 0, 0);
            g.Children.Add(left);
            TextBlock right = MonoText(AppInfo.Brand + " " + AppInfo.Version + "  /  WPF  /  GPU COMPOSITED", 8, Muted);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            b.Child = g;
            return b;
        }

        private UIElement Header(string title, string code)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock t = MonoText(title, 9, Accent);
            t.FontWeight = FontWeights.Bold;
            g.Children.Add(t);
            TextBlock c = MonoText(code, 8, Muted);
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
            return g;
        }

        private StackPanel Metric(string value, string label, Brush color)
        {
            StackPanel s = new StackPanel();
            TextBlock v = DisplayText(value, 34, color);
            v.FontWeight = FontWeights.Bold;
            s.Children.Add(v);
            TextBlock l = MonoText(label, 7.5, Muted);
            s.Children.Add(l);
            return s;
        }

        private UIElement VitalRow(string name, string value, Brush color, double level)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 5, 0, 5);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            TextBlock n = MonoText(name, 9, Sub);
            n.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(n);
            Border track = new Border();
            track.Height = 3;
            track.Background = Line;
            track.VerticalAlignment = VerticalAlignment.Center;
            track.Margin = new Thickness(0, 0, 16, 0);
            Border fill = new Border();
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = 300 * level;
            fill.Background = color;
            track.Child = fill;
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            TextBlock v = MonoText(value, 8.5, color);
            v.VerticalAlignment = VerticalAlignment.Center;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 2);
            g.Children.Add(v);
            return g;
        }

        private UIElement GameRow(string name, string value, Brush color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock n = new TextBlock();
            n.Text = name;
            n.FontSize = 10.5;
            n.Foreground = Sub;
            g.Children.Add(n);
            TextBlock v = MonoText(value, 9, color);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private TextBlock DisplayText(string text, double size, Brush color)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Display;
            t.FontSize = size;
            t.Foreground = color;
            return t;
        }

        private TextBlock MonoText(string text, double size, Brush color)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Mono;
            t.FontSize = size;
            t.Foreground = color;
            return t;
        }
    }

    internal sealed class VigilCard : Border
    {
        public Brush Fill;
        public Brush Stroke;
        public double Cut = 16;
        private Geometry _shape;

        public VigilCard()
        {
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            SnapsToDevicePixels = true;
        }

        private Geometry BuildShape()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return Geometry.Empty;
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w - Cut, 0), true, false);
                ctx.LineTo(new Point(w, Cut), true, false);
                ctx.LineTo(new Point(w, h), true, false);
                ctx.LineTo(new Point(Cut, h), true, false);
                ctx.LineTo(new Point(0, h - Cut), true, false);
            }
            geo.Freeze();
            return geo;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_shape == null) _shape = BuildShape();
            if (_shape == Geometry.Empty) return;
            if (WpfCardGlass.Enabled && WpfCardGlass.Strength > 0)
            {
                WpfCardGlass.Draw(dc, _shape, Fill, Stroke, ActualWidth, ActualHeight);
                return;
            }
            dc.DrawGeometry(Fill, new Pen(Stroke, 1), _shape);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _shape = BuildShape();
            Clip = _shape;
            InvalidateVisual();
        }
    }

    internal sealed class OutlineText : FrameworkElement
    {
        private readonly string _text;
        private readonly double _size;
        private readonly Brush _brush;
        private readonly double _thickness;

        public OutlineText(string text, double size, Brush brush, double thickness)
        {
            _text = text;
            _size = size;
            _brush = brush;
            _thickness = thickness;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            FormattedText ft = new FormattedText(_text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Bahnschrift SemiCondensed"), _size, Brushes.Black, 1.0);
            Geometry g = ft.BuildGeometry(new Point(0, 0));
            Pen pen = new Pen(_brush, _thickness);
            pen.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    internal sealed class VigilField : FrameworkElement
    {
        private readonly bool _light;
        private readonly Brush _accent;
        private readonly Brush _line;
        private readonly Brush _muted;

        public VigilField(bool light, Brush accent, Brush line, Brush muted)
        {
            _light = light;
            _accent = accent;
            _line = line;
            _muted = muted;
            IsHitTestVisible = false;
            Loaded += delegate { Animate(); };
        }

        private void Animate()
        {
            DoubleAnimation pulse = new DoubleAnimation(0.82, 1.0, new Duration(TimeSpan.FromSeconds(4.5)));
            pulse.AutoReverse = true;
            pulse.RepeatBehavior = RepeatBehavior.Forever;
            pulse.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            BeginAnimation(OpacityProperty, pulse);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            Brush baseFill = _light ? Solid(238, 235, 224) : Solid(5, 8, 7);
            dc.DrawRectangle(baseFill, null, new Rect(0, 0, w, h));

            GeometryDrawing dot = new GeometryDrawing();
            dot.Brush = new SolidColorBrush(Alpha(_light ? 26 : 30, 100, 130, 120));
            dot.Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
            DrawingBrush grid = new DrawingBrush(dot);
            grid.TileMode = TileMode.Tile;
            grid.Viewport = new Rect(0, 0, 28, 28);
            grid.ViewportUnits = BrushMappingMode.Absolute;
            dc.DrawRectangle(grid, null, new Rect(0, 0, w, h));

            double cx = w * 0.76;
            double cy = h * 0.42;
            Pen ring = new Pen(new SolidColorBrush(Alpha(_light ? 40 : 44, 120, 160, 140)), 1);
            for (int i = 0; i < 7; i++)
                dc.DrawEllipse(null, ring, new Point(cx, cy), 90 + i * 52, 90 + i * 52);

            Pen dashed = new Pen(new SolidColorBrush(Alpha(_light ? 50 : 70, 198, 255, 61)), 1.2);
            dashed.DashStyle = new DashStyle(new double[] { 2, 7 }, 0);
            dc.DrawEllipse(null, dashed, new Point(cx, cy), 330, 330);
            dc.DrawEllipse(null, dashed, new Point(cx, cy), 410, 410);

            Pen radial = new Pen(new SolidColorBrush(Alpha(_light ? 26 : 32, 120, 160, 140)), 1);
            for (int i = 0; i < 24; i++)
            {
                double angle = i * Math.PI / 12;
                double r1 = 92;
                double r2 = i % 3 == 0 ? 430 : 230;
                dc.DrawLine(radial,
                    new Point(cx + Math.Cos(angle) * r1, cy + Math.Sin(angle) * r1),
                    new Point(cx + Math.Cos(angle) * r2, cy + Math.Sin(angle) * r2));
            }

            Pen diag = new Pen(new SolidColorBrush(Alpha(_light ? 20 : 26, 120, 160, 140)), 1);
            for (int i = -2; i < 6; i++)
                dc.DrawLine(diag, new Point(0, h * 0.2 + i * 80), new Point(w * 0.55, h * 0.2 + i * 80 - 160));

            Typeface tf = new Typeface("Bahnschrift SemiCondensed");
            FormattedText big = new FormattedText("01", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 300, Brushes.Black, 1.0);
            Geometry bg = big.BuildGeometry(new Point(w - 330, h - 360));
            Pen outline = new Pen(new SolidColorBrush(Alpha(_light ? 28 : 34, 198, 255, 61)), 1.4);
            dc.DrawGeometry(null, outline, bg);

            dc.DrawLine(new Pen(_line, 1), new Point(24, 18), new Point(24, h - 18));
            dc.DrawLine(new Pen(_line, 1), new Point(24, h - 18), new Point(w - 24, h - 18));
        }

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }

        private static Color Alpha(int a, int r, int g, int b)
        {
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }
    }
}


