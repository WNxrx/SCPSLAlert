using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace SCPSLAlert
{
    internal sealed class WpfVigilHeroWindow : Window
    {
        private static readonly FontFamily Ui = UiFonts.Body;
        private static readonly FontFamily Display = UiFonts.Title;
        private static readonly FontFamily Mono = UiFonts.Mono;
        private static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private readonly Config _cfg;
        private readonly bool _light;

        private Brush Bg;
        private Brush Panel;
        private Brush Line;
        private Brush Ink;
        private Brush Sub;
        private Brush Muted;
        private Brush Accent;
        private Brush Accent2;
        private Brush Danger;

        public WpfVigilHeroWindow(Config cfg, bool light)
        {
            WpfScroll.Apply(this, light);
            _cfg = cfg;
            _light = light;
            BuildPalette();
            Title = "SCPSLAlert // A.R.C.T";
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

            Build();
        }

        private void BuildPalette()
        {
            if (_light)
            {
                Bg = Solid(240, 237, 228);
                Panel = Solid(247, 244, 235);
                Line = Solid(22, 32, 29);
                Ink = Solid(14, 20, 18);
                Sub = Solid(72, 84, 79);
                Muted = Solid(136, 146, 139);
                Accent = Solid(0, 122, 138);
                Accent2 = Solid(192, 80, 28);
                Danger = Solid(170, 40, 32);
            }
            else
            {
                Bg = Solid(6, 9, 10);
                Panel = Solid(10, 15, 16);
                Line = Solid(32, 50, 52);
                Ink = Solid(233, 246, 247);
                Sub = Solid(140, 166, 170);
                Muted = Solid(78, 102, 106);
                Accent = Solid(91, 240, 255);
                Accent2 = Solid(255, 180, 84);
                Danger = Solid(255, 91, 91);
            }
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

        private void Build()
        {
            Grid root = new Grid();
            root.Background = Bg;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            Content = root;

            Grid backdrop = new Grid();
            backdrop.IsHitTestVisible = false;
            backdrop.Children.Add(new EcgField(_light, Accent, Line, Muted, false));
            Grid.SetRowSpan(backdrop, 3);
            root.Children.Add(backdrop);

            root.Children.Add(TopBar());

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            body.Children.Add(Rail());

            Grid content = new Grid();
            content.Margin = new Thickness(34, 20, 34, 20);
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            Grid.SetColumn(content, 1);
            body.Children.Add(content);

            content.Children.Add(Head());

            Grid hero = new Grid();
            Grid.SetRow(hero, 1);
            content.Children.Add(hero);
            hero.Children.Add(HeroInstrument());
            hero.Children.Add(HeroOverlay());

            content.Children.Add(MetricsRow());

            content.Children.Add(EvidenceTape());

            root.Children.Add(BottomStrip());
        }

        private UIElement TopBar()
        {
            Border bar = new Border();
            bar.Background = _light ? Panel : Solid(3, 5, 6);
            bar.BorderBrush = Line;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
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
            TextBlock tag = MonoText("  //  VITAL SIGN TERMINAL", 8.5, Muted);
            tag.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(tag);
            g.Children.Add(brand);

            TextBlock ticker = MonoText("SYS.CHECK  OK     |     GAME  RUNNING     |     OCR  READY     |     JUDGE  ALIVE 86%     |     FRAME  120HZ", 8.5, Sub);
            ticker.VerticalAlignment = VerticalAlignment.Center;
            ticker.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(ticker, 1);
            g.Children.Add(ticker);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(MonoText(_light ? "LIGHT / PAPER" : "DARK / INK", 8.5, Accent2));
            right.Children.Add(WinButton("\uE921", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(WinButton("\uE922", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            right.Children.Add(WinButton("\uE8BB", delegate { Close(); }));
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return bar;
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
            b.MouseEnter += delegate { b.Background = Panel; b.Foreground = Ink; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; b.Foreground = Sub; };
            b.Click += delegate { click(); };
            return b;
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
            rail.Background = _light ? Panel : Solid(4, 7, 8);
            rail.BorderBrush = Line;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(rail, 0);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            rail.Child = grid;

            TextBlock word = DisplayText("A.R.C.T", 24, Accent);
            word.FontWeight = FontWeights.Bold;
            word.LayoutTransform = new RotateTransform(-90);
            word.HorizontalAlignment = HorizontalAlignment.Center;
            word.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(word);

            StackPanel icons = new StackPanel();
            icons.Margin = new Thickness(8, 8, 8, 0);
            string[] glyphs = { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE8A5", "\uE90F" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                Border item = new Border();
                item.Width = 56;
                item.Height = 50;
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Background = Brushes.Transparent;
                item.Cursor = Cursors.Hand;
                Grid cell = new Grid();
                if (i == 0)
                {
                    Border bar = new Border();
                    bar.Width = 3;
                    bar.Height = 24;
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

            TextBlock sys = MonoText("04\n05", 8, Sub);
            sys.HorizontalAlignment = HorizontalAlignment.Center;
            sys.VerticalAlignment = VerticalAlignment.Center;
            sys.TextAlignment = TextAlignment.Center;
            Grid.SetRow(sys, 2);
            grid.Children.Add(sys);
            return rail;
        }

        private UIElement Head()
        {
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            OutlineText code = new OutlineText("01", 108, Accent, 1.3);
            code.HorizontalAlignment = HorizontalAlignment.Left;
            code.VerticalAlignment = VerticalAlignment.Top;
            code.Margin = new Thickness(-4, -26, 0, 0);
            head.Children.Add(code);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 14, 0, 0);
            title.Children.Add(MonoText("STATUS  /  VITAL SIGNS", 9, Sub));
            TextBlock big = DisplayText("状态", 58, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -6, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "软件、系统与游戏的实时判定";
            desc.FontSize = 11.5;
            desc.Foreground = Sub;
            title.Children.Add(desc);
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock cap = MonoText("TERMINAL LINK", 8, Muted);
            cap.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(cap);
            TextBlock stable = DisplayText("STABLE", 24, Accent);
            stable.FontWeight = FontWeights.Bold;
            stable.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(stable);
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

        private UIElement HeroInstrument()
        {
            EcgField field = new EcgField(_light, Accent, Line, Muted, true);
            field.Margin = new Thickness(0, 0, 0, 0);
            return field;
        }

        private UIElement HeroOverlay()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(22, 18, 22, 18);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

            StackPanel left = new StackPanel();
            left.VerticalAlignment = VerticalAlignment.Top;
            TextBlock tag = MonoText("JUDGE  /  GAME STATE", 9, Muted);
            tag.Margin = new Thickness(0, 0, 0, 2);
            left.Children.Add(tag);
            TextBlock big = DisplayText("存活", 76, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 0, 0, -6);
            left.Children.Add(big);
            TextBlock en = MonoText("ALIVE  /  CONFIDENCE 86%", 10, Accent);
            left.Children.Add(en);
            WrapPanel chips = new WrapPanel();
            chips.Margin = new Thickness(0, 14, 0, 0);
            chips.Children.Add(Chip("游戏 运行中", Accent));
            chips.Children.Add(Chip("窗口 前台", Sub));
            chips.Children.Add(Chip("回合 03:21", Sub));
            chips.Children.Add(Chip("角色 未识别", Muted));
            left.Children.Add(chips);
            g.Children.Add(left);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            right.Children.Add(MonoText("SIGNAL", 8, Muted));
            TextBlock conf = DisplayText("86", 54, Accent2);
            conf.FontWeight = FontWeights.Bold;
            conf.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(conf);
            TextBlock conf2 = MonoText("CONFIDENCE / 100", 8, Muted);
            conf2.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(conf2);
            TextBlock ev = MonoText("LAST EVIDENCE  00:02", 8, Sub);
            ev.HorizontalAlignment = HorizontalAlignment.Right;
            ev.Margin = new Thickness(0, 10, 0, 0);
            right.Children.Add(ev);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            return g;
        }

        private Border Chip(string text, Brush color)
        {
            Border b = new Border();
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(8, 3, 8, 3);
            b.Margin = new Thickness(0, 0, 6, 0);
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            Ellipse dot = new Ellipse();
            dot.Width = 4;
            dot.Height = 4;
            dot.Fill = color;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(dot);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 9.5;
            t.Foreground = Sub;
            row.Children.Add(t);
            b.Child = row;
            return b;
        }

        private UIElement MetricsRow()
        {
            Border host = new Border();
            host.BorderBrush = Line;
            host.BorderThickness = new Thickness(0, 1, 0, 1);
            Grid.SetRow(host, 2);
            Grid g = new Grid();
            for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            host.Child = g;
            string[][] data =
            {
                new string[] { "05", "MODULES" },
                new string[] { "20", "ALERTS" },
                new string[] { "120", "FRAME RATE" },
                new string[] { "1.8", "LOG / MB" },
                new string[] { "06", "HOTKEYS" }
            };
            for (int i = 0; i < data.Length; i++)
            {
                Grid cell = new Grid();
                cell.Margin = new Thickness(14, 8, 14, 8);
                StackPanel s = new StackPanel();
                TextBlock num = DisplayText(data[i][0], 30, i == 2 ? Accent2 : Ink);
                num.FontWeight = FontWeights.Bold;
                s.Children.Add(num);
                s.Children.Add(MonoText(data[i][1], 7.5, Muted));
                cell.Children.Add(s);
                if (i > 0)
                {
                    Border sep = new Border();
                    sep.Width = 1;
                    sep.Background = Line;
                    sep.HorizontalAlignment = HorizontalAlignment.Left;
                    cell.Children.Add(sep);
                }
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
            }
            return host;
        }

        private UIElement EvidenceTape()
        {
            Grid host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            host.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(host, 3);
            host.Margin = new Thickness(0, 10, 0, 0);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Children.Add(MonoText("EVIDENCE", 9, Accent));
            TextBlock tape = DisplayText("证据带", 22, Ink);
            tape.FontWeight = FontWeights.Bold;
            title.Children.Add(tape);
            title.Children.Add(MonoText("LAST 04 EVENTS", 8, Muted));
            host.Children.Add(title);

            Border line = new Border();
            line.BorderBrush = Line;
            line.BorderThickness = new Thickness(0, 1, 0, 0);
            line.VerticalAlignment = VerticalAlignment.Top;
            line.Margin = new Thickness(0, 32, 0, 0);
            Grid.SetColumn(line, 1);
            host.Children.Add(line);

            UniformGrid cards = new UniformGrid();
            cards.Columns = 4;
            cards.Margin = new Thickness(0, 44, 0, 0);
            cards.Children.Add(EvidenceItem("00:02", "OCR", "HUD 生命体征识别为存活", Accent));
            cards.Children.Add(EvidenceItem("00:14", "LOG", "未检测到回合结束事件", Sub));
            cards.Children.Add(EvidenceItem("00:21", "OCR", "票数扫描 37 / 40", Accent2));
            cards.Children.Add(EvidenceItem("00:38", "SYS", "配置自动保存完成", Muted));
            Grid.SetColumn(cards, 1);
            host.Children.Add(cards);
            return host;
        }

        private UIElement EvidenceItem(string time, string tag, string text, Brush color)
        {
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(0, 0, 14, 0);
            StackPanel top = new StackPanel();
            top.Orientation = Orientation.Horizontal;
            TextBlock t = DisplayText(time, 22, color);
            t.FontWeight = FontWeights.Bold;
            top.Children.Add(t);
            TextBlock tg = MonoText("  " + tag, 8, Muted);
            tg.VerticalAlignment = VerticalAlignment.Bottom;
            top.Children.Add(tg);
            s.Children.Add(top);
            TextBlock tx = new TextBlock();
            tx.Text = text;
            tx.FontSize = 10;
            tx.Foreground = Sub;
            tx.TextWrapping = TextWrapping.Wrap;
            tx.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(tx);
            return s;
        }

        private UIElement BottomStrip()
        {
            Border b = new Border();
            b.Background = _light ? Panel : Solid(3, 5, 6);
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            Grid.SetRow(b, 2);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock left = MonoText("A.R.C.T  /  VITAL SIGN TERMINAL   ·   SIGNAL CYAN   ·   GPU COMPOSITED   ·   STAGGER 40MS", 8.5, Sub);
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(16, 0, 0, 0);
            g.Children.Add(left);
            TextBlock right = MonoText("WPF  /  VSYNC  /  2026.10", 8, Muted);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            b.Child = g;
            return b;
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

    internal sealed class EcgField : FrameworkElement
    {
        private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
            "Phase", typeof(double), typeof(EcgField),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private readonly bool _light;
        private readonly Brush _accent;
        private readonly Brush _line;
        private readonly Brush _muted;
        private readonly bool _hero;

        public EcgField(bool light, Brush accent, Brush line, Brush muted, bool hero)
        {
            _light = light;
            _accent = accent;
            _line = line;
            _muted = muted;
            _hero = hero;
            IsHitTestVisible = false;
            Loaded += delegate
            {
                DoubleAnimation a = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(4.5)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                BeginAnimation(PhaseProperty, a);
            };
        }

        private double Phase
        {
            get { return (double)GetValue(PhaseProperty); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            Brush bg = _light ? Solid(240, 237, 228) : Solid(6, 9, 10);
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            Pen grid = new Pen(new SolidColorBrush(Alpha(_light ? 40 : 32, 120, 150, 150)), 0.7);
            for (double x = 0; x < w; x += 28) dc.DrawLine(grid, new Point(x, 0), new Point(x, h));
            for (double y = 0; y < h; y += 28) dc.DrawLine(grid, new Point(0, y), new Point(w, y));

            double cx = w * 0.82;
            double cy = h * 0.38;
            Pen ring = new Pen(new SolidColorBrush(Alpha(_light ? 46 : 40, 91, 240, 255)), 0.9);
            for (int i = 0; i < 8; i++)
                dc.DrawEllipse(null, ring, new Point(cx, cy), 80 + i * 46, 80 + i * 46);
            Pen radial = new Pen(new SolidColorBrush(Alpha(_light ? 30 : 28, 91, 240, 255)), 0.8);
            for (int i = 0; i < 24; i++)
            {
                double ang = i * Math.PI / 12;
                dc.DrawLine(radial, new Point(cx + Math.Cos(ang) * 90, cy + Math.Sin(ang) * 90),
                    new Point(cx + Math.Cos(ang) * 440, cy + Math.Sin(ang) * 440));
            }

            if (_hero)
            {
                DrawWave(dc, w, h);
                DrawScan(dc, w, h);
            }

            Pen edge = new Pen(_line, 1);
            dc.DrawLine(edge, new Point(0, h - 0.5), new Point(w, h - 0.5));
        }

        private void DrawWave(DrawingContext dc, double w, double h)
        {
            double baseY = h * 0.66;
            double amp = Math.Min(70, h * 0.28);
            double offset = Phase * 240;
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                bool started = false;
                for (double x = 0; x <= w; x += 2)
                {
                    double t = x + offset;
                    double y = baseY - Pulse(t) * amp - Noise(t) * 2.2;
                    if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            Pen glow = new Pen(new SolidColorBrush(Alpha(_light ? 30 : 45, 91, 240, 255)), 5);
            Pen line = new Pen(_accent, 1.7);
            dc.DrawGeometry(null, glow, geo);
            dc.DrawGeometry(null, line, geo);

            Pen baseline = new Pen(new SolidColorBrush(Alpha(_light ? 60 : 40, 91, 240, 255)), 0.7);
            baseline.DashStyle = new DashStyle(new double[] { 1, 6 }, 0);
            dc.DrawLine(baseline, new Point(0, baseY), new Point(w, baseY));
        }

        private void DrawScan(DrawingContext dc, double w, double h)
        {
            double x = Phase * w;
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 0);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 91, 240, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 91, 240, 255), 0.7));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 91, 240, 255), 1));
            dc.DrawRectangle(brush, null, new Rect(Math.Max(0, x - 60), 0, 60, h));
            Pen head = new Pen(new SolidColorBrush(Alpha(_light ? 120 : 160, 91, 240, 255)), 0.8);
            dc.DrawLine(head, new Point(x, 0), new Point(x, h));
        }

        private static double Pulse(double t)
        {
            double p = t % 240;
            if (p > 104 && p < 116) return Math.Sin((p - 104) / 12 * Math.PI) * 0.62;
            if (p > 116 && p < 124) return -0.28 * Math.Sin((p - 116) / 8 * Math.PI);
            if (p > 124 && p < 140) return Math.Sin((p - 124) / 16 * Math.PI) * 0.9;
            if (p > 140 && p < 152) return -0.18 * Math.Sin((p - 140) / 12 * Math.PI);
            return 0;
        }

        private static double Noise(double t)
        {
            return Math.Sin(t * 0.11) * 0.5 + Math.Sin(t * 0.037) * 0.4;
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


