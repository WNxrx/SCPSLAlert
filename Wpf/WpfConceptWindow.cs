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

namespace SCPSLAlert
{
    internal sealed class WpfConceptWindow : Window
    {
        private static readonly Brush Bg = Frozen(7, 14, 13);
        private static readonly Brush Panel = Frozen(12, 23, 21);
        private static readonly Brush PanelAlt = Frozen(16, 31, 28);
        private static readonly Brush Line = Frozen(28, 51, 46);
        private static readonly Brush Ink = Frozen(220, 239, 234);
        private static readonly Brush Sub = Frozen(127, 158, 151);
        private static readonly Brush Muted = Frozen(74, 103, 97);
        private static readonly Brush Teal = Frozen(53, 224, 193);
        private static readonly Brush TealSoft = Frozen(24, 68, 61);
        private static readonly Brush Amber = Frozen(232, 180, 92);
        private static readonly Brush Red = Frozen(224, 108, 108);

        private static readonly FontFamily Ui = UiFonts.Body;
        private static readonly FontFamily Mono = UiFonts.Mono;
        private static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        private readonly Config _cfg;
        private readonly string[] _labels = { "总览", "常规", "提示", "音频", "时间", "热键", "日志", "工具" };
        private readonly string[] _icons = { "\uE80F", "\uE713", "\uE790", "\uE767", "\uE823", "\uE765", "\uE8A5", "\uE90F" };
        private readonly string[] _codes = { "OVR", "GEN", "TME", "AUD", "CLK", "KEY", "LOG", "UTL" };

        private readonly List<Border> _nav = new List<Border>();
        private readonly List<TextBlock> _navIcons = new List<TextBlock>();
        private Grid _host;
        private TextBlock _crumb;
        private int _page;
        private int _selectedAlert;

        public WpfConceptWindow(Config cfg)
        {
            WpfScroll.Apply(this);
            _cfg = cfg;
            Title = "SCPSLAlert // Control";
            Width = 1360;
            Height = 900;
            MinWidth = 1100;
            MinHeight = 740;
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

            BuildShell();
            SelectPage(0);
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void BuildShell()
        {
            Grid root = new Grid();
            root.Background = Bg;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            root.RowDefinitions.Add(new RowDefinition());
            Content = root;

            root.Children.Add(Backdrop());
            root.Children.Add(TopBar());
            UIElement bgLayer = root.Children[0];
            Grid.SetRowSpan(bgLayer, 2);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            body.Children.Add(Rail());

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            scroll.Padding = new Thickness(26, 18, 26, 26);
            Grid.SetColumn(scroll, 1);
            body.Children.Add(scroll);

            _host = new Grid();
            _host.MaxWidth = 1220;
            _host.HorizontalAlignment = HorizontalAlignment.Left;
            _host.VerticalAlignment = VerticalAlignment.Top;
            scroll.Content = _host;
            scroll.SizeChanged += delegate
            {
                _host.Width = Math.Min(1220, Math.Max(920, scroll.ActualWidth - 72));
            };
        }

        private UIElement Backdrop()
        {
            Grid layer = new Grid();
            layer.IsHitTestVisible = false;
            Rectangle baseRect = new Rectangle();
            baseRect.Fill = Bg;
            layer.Children.Add(baseRect);

            Rectangle glow = new Rectangle();
            RadialGradientBrush grad = new RadialGradientBrush();
            grad.Center = new Point(0.28, 0.1);
            grad.GradientOrigin = new Point(0.28, 0.1);
            grad.RadiusX = 0.85;
            grad.RadiusY = 0.85;
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(58, 31, 130, 113), 0));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 7, 14, 13), 1));
            glow.Fill = grad;
            layer.Children.Add(glow);

            GeometryDrawing dot = new GeometryDrawing();
            dot.Brush = new SolidColorBrush(Color.FromArgb(26, 96, 160, 148));
            dot.Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1));
            DrawingBrush grid = new DrawingBrush(dot);
            grid.TileMode = TileMode.Tile;
            grid.Viewport = new Rect(0, 0, 26, 26);
            grid.ViewportUnits = BrushMappingMode.Absolute;
            Rectangle dots = new Rectangle();
            dots.Fill = grid;
            layer.Children.Add(dots);

            LinearGradientBrush fade = new LinearGradientBrush();
            fade.StartPoint = new Point(0, 0);
            fade.EndPoint = new Point(0, 1);
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 7, 14, 13), 0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(90, 7, 14, 13), 1));
            Rectangle bottom = new Rectangle();
            bottom.Fill = fade;
            layer.Children.Add(bottom);
            return layer;
        }

        private UIElement TopBar()
        {
            Border bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(235, 9, 18, 16));
            bar.BorderBrush = Line;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(18, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            Border sigil = new Border();
            sigil.Width = 30;
            sigil.Height = 30;
            sigil.BorderBrush = Teal;
            sigil.BorderThickness = new Thickness(1);
            sigil.CornerRadius = new CornerRadius(3);
            sigil.Background = new SolidColorBrush(Color.FromArgb(30, 53, 224, 193));
            TextBlock sigilText = MonoText("S", 16, Teal);
            sigilText.FontWeight = FontWeights.Bold;
            sigilText.HorizontalAlignment = HorizontalAlignment.Center;
            sigilText.VerticalAlignment = VerticalAlignment.Center;
            sigil.Child = sigilText;
            brand.Children.Add(sigil);
            StackPanel names = new StackPanel();
            names.Margin = new Thickness(10, 0, 0, 0);
            names.VerticalAlignment = VerticalAlignment.Center;
            names.Children.Add(MonoText("SCPSL // CONTROL", 12, Ink));
            TextBlock tag = MonoText("TACTICAL OPERATIONS", 8, Muted);
            tag.Margin = new Thickness(0, 2, 0, 0);
            names.Children.Add(tag);
            brand.Children.Add(names);
            grid.Children.Add(brand);

            StackPanel status = new StackPanel();
            status.Orientation = Orientation.Horizontal;
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Children.Add(StatusChip("GAME OFFLINE", Amber));
            status.Children.Add(StatusChip("OCR STANDBY", Teal));
            status.Children.Add(StatusChip(_cfg.Alerts.Length + " ALERTS", Sub));
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 10, 0);
            Border command = new Border();
            command.Width = 150;
            command.Height = 28;
            command.CornerRadius = new CornerRadius(4);
            command.BorderBrush = Line;
            command.BorderThickness = new Thickness(1);
            command.Background = new SolidColorBrush(Color.FromArgb(120, 18, 34, 31));
            command.Margin = new Thickness(0, 0, 12, 0);
            Grid cmd = new Grid();
            TextBlock cmdText = MonoText("SEARCH  /  COMMAND", 9, Muted);
            cmdText.Margin = new Thickness(10, 0, 0, 0);
            cmdText.VerticalAlignment = VerticalAlignment.Center;
            cmd.Children.Add(cmdText);
            TextBlock cmdKey = MonoText("CTRL K", 9, Teal);
            cmdKey.HorizontalAlignment = HorizontalAlignment.Right;
            cmdKey.VerticalAlignment = VerticalAlignment.Center;
            cmdKey.Margin = new Thickness(0, 0, 10, 0);
            cmd.Children.Add(cmdKey);
            command.Child = cmd;
            right.Children.Add(command);
            right.Children.Add(WindowButton("\uE921", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(WindowButton("\uE922", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            right.Children.Add(WindowButton("\uE8BB", delegate { Close(); }));
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
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

        private Border StatusChip(string text, Brush color)
        {
            Border chip = new Border();
            chip.CornerRadius = new CornerRadius(3);
            chip.BorderBrush = Line;
            chip.BorderThickness = new Thickness(1);
            chip.Background = new SolidColorBrush(Color.FromArgb(80, 16, 31, 28));
            chip.Padding = new Thickness(8, 3, 8, 3);
            chip.Margin = new Thickness(4, 0, 4, 0);
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            Ellipse dot = new Ellipse();
            dot.Width = 5;
            dot.Height = 5;
            dot.Fill = color;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(dot);
            row.Children.Add(MonoText(text, 9, Sub));
            chip.Child = row;
            return chip;
        }

        private Button WindowButton(string glyph, Action click)
        {
            Button b = new Button();
            b.Content = glyph;
            b.FontFamily = Icons;
            b.FontSize = 10;
            b.Width = 38;
            b.Height = 30;
            b.Background = Brushes.Transparent;
            b.Foreground = Sub;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            b.Template = FlatButtonTemplate();
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(120, 26, 50, 45)); b.Foreground = Ink; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; b.Foreground = Sub; };
            b.Click += delegate { click(); };
            return b;
        }

        private static ControlTemplate FlatButtonTemplate()
        {            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
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
            rail.Background = new SolidColorBrush(Color.FromArgb(150, 9, 18, 16));
            rail.BorderBrush = Line;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(rail, 0);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rail.Child = grid;

            StackPanel items = new StackPanel();
            items.Margin = new Thickness(8, 14, 8, 0);

            for (int i = 0; i < _labels.Length; i++)
            {
                int index = i;
                Border item = new Border();
                item.Width = 52;
                item.Height = 46;
                item.CornerRadius = new CornerRadius(4);
                item.Margin = new Thickness(0, 2, 0, 2);
                item.Background = Brushes.Transparent;
                item.Cursor = Cursors.Hand;
                item.ToolTip = _labels[i];

                Grid cell = new Grid();
                Border bar = new Border();
                bar.Width = 3;
                bar.Height = 20;
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                bar.VerticalAlignment = VerticalAlignment.Center;
                bar.Background = Teal;
                bar.Opacity = 0;
                bar.CornerRadius = new CornerRadius(1);
                cell.Children.Add(bar);
                TextBlock icon = new TextBlock();
                icon.Text = _icons[i];
                icon.FontFamily = Icons;
                icon.FontSize = 17;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Foreground = Sub;
                cell.Children.Add(icon);
                item.Child = cell;

                item.MouseLeftButtonUp += delegate { SelectPage(index); };
                item.MouseEnter += delegate
                {
                    if (index != _page) item.Background = new SolidColorBrush(Color.FromArgb(70, 26, 50, 45));
                };
                item.MouseLeave += delegate
                {
                    if (index != _page) item.Background = Brushes.Transparent;
                };
                items.Children.Add(item);
                _nav.Add(item);
                _navIcons.Add(icon);
            }
            grid.Children.Add(items);

            StackPanel bottom = new StackPanel();
            bottom.Margin = new Thickness(8, 0, 8, 14);
            bottom.Children.Add(RailIcon("\uE897"));
            bottom.Children.Add(RailIcon("\uE7E8"));
            Grid.SetRow(bottom, 1);
            grid.Children.Add(bottom);
            return rail;
        }

        private Border RailIcon(string glyph)
        {
            Border b = new Border();
            b.Width = 52;
            b.Height = 42;
            b.CornerRadius = new CornerRadius(4);
            b.Margin = new Thickness(0, 2, 0, 2);
            TextBlock t = new TextBlock();
            t.Text = glyph;
            t.FontFamily = Icons;
            t.FontSize = 15;
            t.Foreground = Muted;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            b.Cursor = Cursors.Hand;
            return b;
        }

        public void ShowPage(int index)
        {
            SelectPage(index);
        }

        private void SelectPage(int index)
        {
            _page = index;
            for (int i = 0; i < _nav.Count; i++)
            {
                bool selected = i == index;
                _nav[i].Background = selected ? new SolidColorBrush(Color.FromArgb(90, 28, 74, 66)) : Brushes.Transparent;
                Border bar = ((Grid)_nav[i].Child).Children[0] as Border;
                if (bar != null) bar.Opacity = selected ? 1 : 0;
                _navIcons[i].Foreground = selected ? Teal : Sub;
            }
            _host.Children.Clear();
            UIElement page = index == 0 ? Overview() : index == 1 ? General() : Placeholder(index);
            page.Opacity = 0;
            TranslateTransform slide = new TranslateTransform(0, 14);
            page.RenderTransform = slide;
            _host.Children.Add(page);
            DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)));
            page.BeginAnimation(UIElement.OpacityProperty, fade);
            DoubleAnimation move = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(260)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.YProperty, move);
        }

        private UIElement PageHead(string code, string title, string caption)
        {
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Margin = new Thickness(0, 0, 0, 16);
            StackPanel left = new StackPanel();
            TextBlock tag = MonoText(code + "  /  " + Spread(title), 9, Teal);
            left.Children.Add(tag);
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 26;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = Ink;
            t.Margin = new Thickness(0, 5, 0, 0);
            left.Children.Add(t);
            TextBlock c = new TextBlock();
            c.Text = caption;
            c.FontSize = 11.5;
            c.Foreground = Sub;
            c.Margin = new Thickness(0, 5, 0, 0);
            left.Children.Add(c);
            head.Children.Add(left);
            TextBlock live = MonoText("LIVE FEED   //   " + DateTime.Now.ToString("HH:mm:ss"), 9, Muted);
            live.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(live, 1);
            head.Children.Add(live);
            return head;
        }

        private static string Spread(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return string.Join(" ", text.ToCharArray());
        }

        private UIElement Overview()
        {
            StackPanel page = new StackPanel();
            page.Children.Add(PageHead("01", "总览", "运行状态、关键指标与快速入口"));

            Grid bento = new Grid();
            bento.ColumnDefinitions.Add(new ColumnDefinition());
            bento.ColumnDefinitions.Add(new ColumnDefinition());
            bento.ColumnDefinitions.Add(new ColumnDefinition());
            bento.ColumnDefinitions.Add(new ColumnDefinition());
            bento.RowDefinitions.Add(new RowDefinition { Height = new GridLength(176) });
            bento.RowDefinitions.Add(new RowDefinition { Height = new GridLength(176) });
            bento.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            page.Children.Add(bento);

            HudCard hero = Card(0, 0, 3, 1);
            hero.Margin = Gap(0, 0, 8, 8);
            hero.Child = HeroBody();
            bento.Children.Add(hero);

            HudCard alerts = Card(3, 0, 1, 1);
            alerts.Margin = Gap(0, 0, 0, 8);
            alerts.Child = AlertsBody();
            bento.Children.Add(alerts);

            HudCard timer = Card(3, 1, 1, 1);
            timer.Margin = Gap(0, 0, 0, 8);
            timer.Child = TimerBody();
            bento.Children.Add(timer);

            HudCard quick = Card(0, 1, 2, 1);
            quick.Margin = Gap(0, 0, 8, 8);
            quick.Child = QuickBody();
            bento.Children.Add(quick);

            HudCard keys = Card(2, 1, 1, 1);
            keys.Margin = Gap(0, 0, 8, 8);
            keys.Child = KeysBody();
            bento.Children.Add(keys);

            HudCard matrix = Card(0, 2, 3, 1);
            matrix.Margin = Gap(0, 0, 8, 0);
            matrix.Child = MatrixBody();
            bento.Children.Add(matrix);

            HudCard log = Card(3, 2, 1, 1);
            log.Child = LogBody();
            bento.Children.Add(log);

            page.Children.Add(StatusStrip());
            Stagger(page);
            return page;
        }

        private UIElement StatusStrip()
        {
            Border strip = new Border();
            strip.Height = 30;
            strip.Margin = new Thickness(0, 14, 0, 0);
            strip.BorderBrush = Line;
            strip.BorderThickness = new Thickness(0, 1, 0, 0);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock left = MonoText("SCPSL // MONITOR CORE", 8, Muted);
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(2, 0, 0, 0);
            g.Children.Add(left);
            TextBlock mid = MonoText("FRAME 120 HZ  /  VSYNC", 8, Teal);
            mid.VerticalAlignment = VerticalAlignment.Center;
            mid.Margin = new Thickness(0, 0, 24, 0);
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);
            TextBlock right = MonoText("BUILD 2026.10  //  WPF", 8, Muted);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            strip.Child = g;
            return strip;
        }

        private HudCard Card(int col, int row, int span, int rows)
        {
            HudCard card = new HudCard();
            card.Background = Panel;
            card.BorderBrush = Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(18, 15, 18, 15);
            Grid.SetColumn(card, col);
            Grid.SetRow(card, row);
            Grid.SetColumnSpan(card, span);
            Grid.SetRowSpan(card, rows);
            return card;
        }

        private static Thickness Gap(double l, double t, double r, double b)
        {
            return new Thickness(l, t, r, b);
        }

        private UIElement HeroBody()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            StackPanel text = new StackPanel();
            text.Children.Add(Section("SYSTEM STATUS"));
            TextBlock title = new TextBlock();
            title.Text = "监控内核运行中";
            title.FontSize = 24;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = Ink;
            title.Margin = new Thickness(0, 6, 0, 0);
            text.Children.Add(title);
            TextBlock desc = new TextBlock();
            desc.Text = "所有监控、OCR、回合计时与热键共用同一套内核。";
            desc.FontSize = 11.5;
            desc.Foreground = Sub;
            desc.Margin = new Thickness(0, 6, 0, 12);
            text.Children.Add(desc);

            WrapPanel chips = new WrapPanel();
            int enabled = 0;
            for (int i = 0; i < _cfg.Alerts.Length; i++) if (_cfg.Alerts[i].Style.Enabled) enabled++;
            chips.Children.Add(MiniChip("游戏未运行", Amber));
            chips.Children.Add(MiniChip(_cfg.EnableOcr ? "OCR 在线" : "OCR 待命", Teal));
            chips.Children.Add(MiniChip(enabled + " / " + _cfg.Alerts.Length + " 提示", Sub));
            text.Children.Add(chips);
            g.Children.Add(text);

            Canvas ring = new Canvas();
            ring.Width = 150;
            ring.Height = 130;
            ring.HorizontalAlignment = HorizontalAlignment.Right;
            ring.VerticalAlignment = VerticalAlignment.Center;
            Ellipse halo = new Ellipse();
            halo.Width = 104;
            halo.Height = 104;
            halo.Stroke = new SolidColorBrush(Color.FromArgb(55, 53, 224, 193));
            halo.StrokeThickness = 1;
            Canvas.SetLeft(halo, 23);
            Canvas.SetTop(halo, 13);
            ring.Children.Add(halo);

            Ellipse pulse = new Ellipse();
            pulse.Width = 104;
            pulse.Height = 104;
            pulse.Stroke = new SolidColorBrush(Color.FromArgb(120, 53, 224, 193));
            pulse.StrokeThickness = 1;
            pulse.RenderTransformOrigin = new Point(0.5, 0.5);
            Canvas.SetLeft(pulse, 23);
            Canvas.SetTop(pulse, 13);
            ring.Children.Add(pulse);
            ScaleTransform scale = new ScaleTransform(1, 1);
            pulse.RenderTransform = scale;
            DoubleAnimation grow = new DoubleAnimation(1, 1.55, new Duration(TimeSpan.FromMilliseconds(1800)));
            grow.RepeatBehavior = RepeatBehavior.Forever;
            grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            DoubleAnimation dim = new DoubleAnimation(0.65, 0, new Duration(TimeSpan.FromMilliseconds(1800)));
            dim.RepeatBehavior = RepeatBehavior.Forever;
            pulse.BeginAnimation(UIElement.OpacityProperty, dim);

            Ellipse core = new Ellipse();
            core.Width = 46;
            core.Height = 46;
            core.Fill = new SolidColorBrush(Color.FromArgb(38, 53, 224, 193));
            core.Stroke = Teal;
            core.StrokeThickness = 1.2;
            Canvas.SetLeft(core, 52);
            Canvas.SetTop(core, 42);
            ring.Children.Add(core);
            TextBlock live = MonoText("LIVE", 9, Teal);
            live.HorizontalAlignment = HorizontalAlignment.Center;
            Canvas.SetLeft(live, 65);
            Canvas.SetTop(live, 57);
            ring.Children.Add(live);
            Grid.SetColumn(ring, 1);
            g.Children.Add(ring);
            return g;
        }

        private UIElement AlertsBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("ALERT MATRIX"));
            int enabled = 0;
            for (int i = 0; i < _cfg.Alerts.Length; i++) if (_cfg.Alerts[i].Style.Enabled) enabled++;
            TextBlock big = MonoText(enabled.ToString("00"), 46, Teal);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(big);
            s.Children.Add(MonoText("OF " + _cfg.Alerts.Length.ToString("00") + " ENABLED", 9, Muted));

            UniformGrid dots = new UniformGrid();
            dots.Columns = 10;
            dots.Rows = 2;
            dots.Margin = new Thickness(0, 10, 0, 0);
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                Border d = new Border();
                d.Width = 9;
                d.Height = 9;
                d.Margin = new Thickness(0, 0, 4, 4);
                d.CornerRadius = new CornerRadius(1);
                d.Background = _cfg.Alerts[i].Style.Enabled ? Teal : new SolidColorBrush(Color.FromArgb(70, 74, 103, 97));
                dots.Children.Add(d);
            }
            s.Children.Add(dots);
            return s;
        }

        private UIElement TimerBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("ROUND TIMER"));
            TextBlock big = MonoText("03:21", 40, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 0, 0, 0);
            s.Children.Add(big);
            s.Children.Add(MonoText("PAUSED  /  WAITING FOR ROUND", 8, Muted));
            Border track = new Border();
            track.Height = 3;
            track.Margin = new Thickness(0, 12, 0, 0);
            track.Background = new SolidColorBrush(Color.FromArgb(80, 28, 51, 46));
            track.CornerRadius = new CornerRadius(1);
            Grid stack = new Grid();
            Border fill = new Border();
            fill.Width = 120;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Background = Teal;
            fill.CornerRadius = new CornerRadius(1);
            stack.Children.Add(fill);
            track.Child = stack;
            s.Children.Add(track);
            return s;
        }

        private UIElement QuickBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("QUICK ACTIONS"));
            UniformGrid grid = new UniformGrid();
            grid.Columns = 2;
            grid.Margin = new Thickness(0, 10, 0, 0);
            grid.Children.Add(ActionTile("\uE8B8", "预览全部提示"));
            grid.Children.Add(ActionTile("\uE740", "调整提示位置"));
            grid.Children.Add(ActionTile("\uE8A5", "打开运行日志"));
            grid.Children.Add(ActionTile("\uE74E", "立即保存配置"));
            s.Children.Add(grid);
            return s;
        }

        private UIElement ActionTile(string glyph, string text)
        {
            Border b = new Border();
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Height = 44;
            b.CornerRadius = new CornerRadius(4);
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.Background = new SolidColorBrush(Color.FromArgb(100, 20, 38, 34));
            b.Cursor = Cursors.Hand;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = Icons;
            icon.FontSize = 13;
            icon.Foreground = Teal;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 11.5;
            t.Foreground = Ink;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            b.Child = g;
            b.MouseEnter += delegate { b.BorderBrush = Teal; };
            b.MouseLeave += delegate { b.BorderBrush = Line; };
            return b;
        }

        private UIElement KeysBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("KEY BINDINGS"));
            s.Children.Add(KeyLine("TACTICAL", _cfg.HotkeyTactical));
            s.Children.Add(KeyLine("CALIBRATE", _cfg.HotkeyCalibrate));
            s.Children.Add(KeyLine("BIG WAVE", _cfg.HotkeyBigWave));
            return s;
        }

        private UIElement KeyLine(string label, string key)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 8, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock l = MonoText(label, 9, Sub);
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            Border cap = new Border();
            cap.Background = new SolidColorBrush(Color.FromArgb(160, 22, 42, 38));
            cap.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 53, 224, 193));
            cap.BorderThickness = new Thickness(1);
            cap.CornerRadius = new CornerRadius(3);
            cap.Padding = new Thickness(8, 3, 8, 3);
            TextBlock k = MonoText(string.IsNullOrEmpty(key) ? "--" : key, 10, Teal);
            cap.Child = k;
            Grid.SetColumn(cap, 1);
            g.Children.Add(cap);
            return g;
        }

        private UIElement MatrixBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("ACTIVE MODULES"));
            WrapPanel wrap = new WrapPanel();
            wrap.Margin = new Thickness(0, 8, 0, 0);
            for (int i = 0; i < _cfg.Alerts.Length && i < 12; i++)
            {
                Border cell = new Border();
                cell.Width = 168;
                cell.Height = 28;
                cell.Margin = new Thickness(0, 0, 8, 6);
                cell.CornerRadius = new CornerRadius(3);
                cell.Background = new SolidColorBrush(Color.FromArgb(80, 18, 34, 31));
                cell.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 53, 224, 193));
                cell.BorderThickness = new Thickness(0, 0, 0, 1);
                StackPanel row = new StackPanel();
                row.Orientation = Orientation.Horizontal;
                Ellipse d = new Ellipse();
                d.Width = 4;
                d.Height = 4;
                d.Fill = _cfg.Alerts[i].Style.Enabled ? Teal : Muted;
                d.Margin = new Thickness(9, 0, 7, 0);
                d.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(d);
                TextBlock t = new TextBlock();
                t.Text = _cfg.Alerts[i].DisplayName;
                t.FontSize = 10.5;
                t.Foreground = Sub;
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                cell.Child = row;
                wrap.Children.Add(cell);
            }
            s.Children.Add(wrap);
            return s;
        }

        private UIElement LogBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("RECENT ACTIVITY"));
            s.Children.Add(LogLine("23:58:12", "配置已保存", Teal));
            s.Children.Add(LogLine("23:57:40", "OCR 引擎就绪", Sub));
            s.Children.Add(LogLine("23:56:02", "回合开始识别", Sub));
            return s;
        }

        private UIElement LogLine(string time, string text, Brush color)
        {
            StackPanel s = new StackPanel();
            s.Orientation = Orientation.Horizontal;
            s.Margin = new Thickness(0, 7, 0, 0);
            s.Children.Add(MonoText(time, 8.5, Muted));
            TextBlock t = MonoText(text, 9, color);
            t.Margin = new Thickness(10, 0, 0, 0);
            s.Children.Add(t);
            return s;
        }

        private UIElement General()
        {
            StackPanel page = new StackPanel();
            page.Children.Add(PageHead("02", "常规", "警报矩阵与参数检查器"));

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            page.Children.Add(body);

            HudCard matrix = new HudCard();
            matrix.Background = Panel;
            matrix.BorderBrush = Line;
            matrix.BorderThickness = new Thickness(1);
            matrix.CornerRadius = new CornerRadius(6);
            matrix.Padding = new Thickness(16);
            matrix.Margin = new Thickness(0, 0, 10, 0);
            StackPanel m = new StackPanel();
            m.Children.Add(Section("ALERT MATRIX  /  SELECT A MODULE"));
            UniformGrid grid = new UniformGrid();
            grid.Columns = 4;
            grid.Margin = new Thickness(0, 12, 0, 0);
            for (int i = 0; i < _cfg.Alerts.Length; i++)
            {
                int index = i;
                grid.Children.Add(AlertTile(index));
            }
            m.Children.Add(grid);
            matrix.Child = m;
            body.Children.Add(matrix);

            HudCard inspector = new HudCard();
            inspector.Background = PanelAlt;
            inspector.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 53, 224, 193));
            inspector.BorderThickness = new Thickness(1);
            inspector.CornerRadius = new CornerRadius(6);
            inspector.Padding = new Thickness(18, 16, 18, 16);
            inspector.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(inspector, 1);
            inspector.Child = InspectorBody();
            body.Children.Add(inspector);

            HudCard display = new HudCard();
            display.Background = Panel;
            display.BorderBrush = Line;
            display.BorderThickness = new Thickness(1);
            display.CornerRadius = new CornerRadius(6);
            display.Padding = new Thickness(18, 14, 18, 16);
            display.Margin = new Thickness(0, 14, 0, 0);
            display.Child = DisplayBody();
            page.Children.Add(display);

            page.Children.Add(StatusStrip());
            Stagger(page);
            return page;
        }

        private UIElement AlertTile(int index)
        {
            AlertConfig a = _cfg.Alerts[index];
            bool selected = index == _selectedAlert;
            Border b = new Border();
            b.Height = 68;
            b.Margin = new Thickness(0, 0, 8, 8);
            b.CornerRadius = new CornerRadius(4);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = selected ? Teal : Line;
            b.Background = selected ? new SolidColorBrush(Color.FromArgb(70, 24, 68, 61)) : new SolidColorBrush(Color.FromArgb(100, 16, 31, 28));
            b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += delegate { _selectedAlert = index; SelectPage(1); };

            Grid g = new Grid();
            g.Margin = new Thickness(10, 8, 10, 8);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock code = MonoText("A-" + (index + 1).ToString("00"), 8, selected ? Teal : Muted);
            g.Children.Add(code);
            Border state = new Border();
            state.Width = 26;
            state.Height = 14;
            state.CornerRadius = new CornerRadius(7);
            state.Background = a.Style.Enabled ? Teal : new SolidColorBrush(Color.FromArgb(90, 74, 103, 97));
            Ellipse knob = new Ellipse();
            knob.Width = 10;
            knob.Height = 10;
            knob.Fill = Bg;
            knob.HorizontalAlignment = a.Style.Enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            knob.Margin = new Thickness(2, 0, 2, 0);
            state.Child = knob;
            Grid.SetColumn(state, 1);
            g.Children.Add(state);

            TextBlock name = new TextBlock();
            name.Text = a.DisplayName;
            name.FontSize = 11;
            name.Foreground = selected ? Ink : Sub;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetRow(name, 1);
            g.Children.Add(name);
            b.Child = g;
            return b;
        }

        private UIElement InspectorBody()
        {
            AlertConfig a = _cfg.Alerts[_selectedAlert];
            StackPanel s = new StackPanel();
            s.Children.Add(Section("INSPECTOR"));
            TextBlock code = MonoText("A-" + (_selectedAlert + 1).ToString("00") + "  /  MODULE", 8, Teal);
            code.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(code);
            TextBlock name = new TextBlock();
            name.Text = a.DisplayName;
            name.FontSize = 19;
            name.FontWeight = FontWeights.Bold;
            name.Foreground = Ink;
            name.Margin = new Thickness(0, 4, 0, 12);
            s.Children.Add(name);

            s.Children.Add(FieldLabel("状态"));
            s.Children.Add(InspectorRow("启用", a.Style.Enabled ? "ON" : "OFF", a.Style.Enabled ? Teal : Muted));
            s.Children.Add(FieldLabel("显示时长"));
            s.Children.Add(InspectorRow("自动缩回", a.Persistent ? "常驻" : "5 秒", Ink));
            s.Children.Add(FieldLabel("动画"));
            s.Children.Add(InspectorRow("弹入 / 淡出", "240 ms", Ink));

            Border preview = new Border();
            preview.Height = 54;
            preview.Margin = new Thickness(0, 14, 0, 0);
            preview.CornerRadius = new CornerRadius(4);
            preview.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 53, 224, 193));
            preview.BorderThickness = new Thickness(1);
            preview.Background = new SolidColorBrush(Color.FromArgb(50, 53, 224, 193));
            TextBlock p = new TextBlock();
            p.Text = a.DisplayName;
            p.FontSize = 13;
            p.FontWeight = FontWeights.SemiBold;
            p.Foreground = Teal;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center;
            preview.Child = p;
            s.Children.Add(preview);
            return s;
        }

        private UIElement FieldLabel(string text)
        {
            TextBlock t = MonoText(text, 8.5, Muted);
            t.Margin = new Thickness(0, 8, 0, 0);
            return t;
        }

        private UIElement InspectorRow(string label, string value, Brush color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 11;
            l.Foreground = Sub;
            g.Children.Add(l);
            TextBlock v = MonoText(value, 10, color);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private UIElement DisplayBody()
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Section("DISPLAY BEHAVIOR"));
            Grid row = new Grid();
            row.Margin = new Thickness(0, 10, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock l = new TextBlock();
            l.Text = "动画质量";
            l.FontSize = 11.5;
            l.Foreground = Sub;
            l.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(l);
            StackPanel seg = new StackPanel();
            seg.Orientation = Orientation.Horizontal;
            seg.Margin = new Thickness(0, 0, 0, 0);
            for (int i = 0; i < UiAnim.Names.Length; i++)
            {
                Border b = new Border();
                b.Padding = new Thickness(10, 5, 10, 5);
                b.Margin = new Thickness(0, 0, 4, 0);
                b.CornerRadius = new CornerRadius(3);
                b.BorderThickness = new Thickness(1);
                b.BorderBrush = i == _cfg.PanelAnimQuality ? Teal : Line;
                b.Background = i == _cfg.PanelAnimQuality ? new SolidColorBrush(Color.FromArgb(70, 24, 68, 61)) : Brushes.Transparent;
                TextBlock t = MonoText(i == 3 ? "60" : i == 4 ? "120" : i == 0 ? "OFF" : i == 1 ? "30" : "45", 9,
                    i == _cfg.PanelAnimQuality ? Teal : Muted);
                b.Child = t;
                Grid.SetColumn(seg, 1);
                seg.Children.Add(b);
            }
            row.Children.Add(seg);
            Grid.SetColumn(seg, 1);
            TextBlock note = MonoText("FRAME RATE FOLLOWS DISPLAY", 8, Muted);
            note.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(note, 2);
            row.Children.Add(note);
            s.Children.Add(row);
            return s;
        }

        private UIElement Placeholder(int index)
        {
            StackPanel page = new StackPanel();
            page.Children.Add(PageHead(("0" + (index + 1)), _labels[index], _captions(index)));
            HudCard card = new HudCard();
            card.Background = Panel;
            card.BorderBrush = Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(22, 20, 22, 22);
            StackPanel s = new StackPanel();
            s.Children.Add(Section("MODULE UNDER CONSTRUCTION"));
            TextBlock t = new TextBlock();
            t.Text = _labels[index] + " 将换成专用编辑器";
            t.FontSize = 20;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = Ink;
            t.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(t);
            TextBlock d = new TextBlock();
            d.Text = "不是长列表，而是按这一页的内容做成矩阵、工作室、时间轴、键盘或终端流。";
            d.FontSize = 11.5;
            d.Foreground = Sub;
            d.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(d);
            card.Child = s;
            page.Children.Add(card);
            return page;
        }

        private string _captions(int index)
        {
            string[] c = { "", "警报矩阵与参数检查器", "样式工作室", "波形库", "时间轴", "虚拟键盘", "终端流", "校准台" };
            return index >= 0 && index < c.Length ? c[index] : "";
        }

        private void Stagger(UIElement root)
        {
            if (!(root is Panel)) return;
            Panel panel = (Panel)root;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                UIElement child = panel.Children[i];
                child.Opacity = 0;
                TranslateTransform t = new TranslateTransform(0, 12);
                child.RenderTransform = t;
                DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260)));
                fade.BeginTime = TimeSpan.FromMilliseconds(30 * i);
                child.BeginAnimation(UIElement.OpacityProperty, fade);
                DoubleAnimation move = new DoubleAnimation(12, 0, new Duration(TimeSpan.FromMilliseconds(320)));
                move.BeginTime = TimeSpan.FromMilliseconds(30 * i);
                move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.YProperty, move);
            }
        }

        private TextBlock Section(string text)
        {
            TextBlock t = MonoText(text, 8.5, Teal);
            t.FontWeight = FontWeights.Bold;
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

        private Border MiniChip(string text, Brush color)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(3);
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.Background = new SolidColorBrush(Color.FromArgb(60, 18, 34, 31));
            b.Padding = new Thickness(7, 3, 7, 3);
            b.Margin = new Thickness(0, 0, 6, 0);
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            Ellipse d = new Ellipse();
            d.Width = 4;
            d.Height = 4;
            d.Fill = color;
            d.VerticalAlignment = VerticalAlignment.Center;
            d.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(d);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 9.5;
            t.Foreground = Sub;
            row.Children.Add(t);
            b.Child = row;
            return b;
        }
    }

    internal sealed class HudCard : Border
    {
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(90, 53, 224, 193)), 1);
            pen.Freeze();
            double L = 10;
            double inset = 5;
            double w = ActualWidth;
            double h = ActualHeight;
            dc.DrawLine(pen, new Point(inset, inset + L), new Point(inset, inset));
            dc.DrawLine(pen, new Point(inset, inset), new Point(inset + L, inset));
            dc.DrawLine(pen, new Point(w - inset - L, inset), new Point(w - inset, inset));
            dc.DrawLine(pen, new Point(w - inset, inset), new Point(w - inset, inset + L));
            dc.DrawLine(pen, new Point(w - inset, h - inset - L), new Point(w - inset, h - inset));
            dc.DrawLine(pen, new Point(w - inset, h - inset), new Point(w - inset - L, h - inset));
            dc.DrawLine(pen, new Point(inset + L, h - inset), new Point(inset, h - inset));
            dc.DrawLine(pen, new Point(inset, h - inset), new Point(inset, h - inset - L));
        }
    }
}

