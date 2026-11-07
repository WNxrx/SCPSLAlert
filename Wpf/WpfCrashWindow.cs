using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SCPSLAlert
{
    internal sealed class WpfCrashWindow : Window
    {
        private readonly CrashReport _report;
        private readonly Action _restart;
        private readonly Action<CrashReport, bool> _export;
        private readonly Action<CrashReport> _openLog;
        private Border _root;
        private bool _light;

        public WpfCrashWindow(CrashReport report, Action restart,
            Action<CrashReport, bool> export, Action<CrashReport> openLog)
        {
            WpfScroll.Apply(this, _light);
            _report = report;
            _restart = restart;
            _export = export;
            _openLog = openLog;
            _light = !ModernTheme.IsDark;

            Title = report.Kind == "游戏"
                ? (report.ConfirmedCrash ? "游戏崩溃" : "游戏异常退出")
                : "程序崩溃";
            Width = 780;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 760;
            WindowStyle = System.Windows.WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            ShowActivated = true;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = WpfTheme.UiFont;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            Build();
            WpfTheme.Changed += OnThemeChanged;
            Closed += delegate { WpfTheme.Changed -= OnThemeChanged; };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        private void OnThemeChanged()
        {
            _light = !ModernTheme.IsDark;
            Build();
        }

        private void Build()
        {
            _root = new Border();
            _root.Margin = new Thickness(18);
            _root.Background = WpfTheme.PageBg;
            _root.BorderBrush = _report.Kind == "游戏"
                ? (_report.ConfirmedCrash ? WpfTheme.Danger : WpfTheme.Accent)
                : WpfTheme.Accent;
            _root.BorderThickness = new Thickness(1);
            _root.Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.35,
                Color = Colors.Black
            };
            _root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = _root;

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(28, 22, 28, 22);
            _root.Child = body;

            body.Children.Add(BuildHeader());

            TextBlock title = new TextBlock();
            title.Text = _report.Title;
            title.FontSize = 27;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = WpfTheme.Text;
            title.TextWrapping = TextWrapping.Wrap;
            title.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(title);

            TextBlock sub = new TextBlock();
            sub.Text = _report.Summary;
            sub.FontSize = 11.5;
            sub.Foreground = WpfTheme.SubText;
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(sub);

            body.Children.Add(BuildMeta());

            if (_report.Causes.Count > 0) body.Children.Add(BuildCauses());
            if (_report.Suggestions.Count > 0) body.Children.Add(BuildSuggestions());

            body.Children.Add(BuildFooter());
        }

        private UIElement BuildHeader()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Margin = new Thickness(0, 0, 0, 14);

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            Border dot = new Border();
            dot.Width = 8;
            dot.Height = 8;
            dot.CornerRadius = new CornerRadius(4);
            dot.Background = _report.Kind == "游戏"
                ? (_report.ConfirmedCrash ? WpfTheme.Danger : WpfTheme.Accent)
                : WpfTheme.Accent;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(dot);
            TextBlock eyebrow = new TextBlock();
            eyebrow.Text = (_report.Kind == "游戏"
                ? (_report.ConfirmedCrash ? "GAME CRASH" : "GAME EXIT")
                : "APP CRASH") + "  /  " + _report.Time.ToString("yyyy-MM-dd HH:mm:ss");
            eyebrow.FontFamily = UiFonts.Mono;
            eyebrow.FontSize = 9.5;
            eyebrow.Foreground = _report.Kind == "游戏"
                ? (_report.ConfirmedCrash ? WpfTheme.Danger : WpfTheme.Accent)
                : WpfTheme.Accent;
            eyebrow.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(eyebrow);
            g.Children.Add(left);

            Border close = new Border();
            close.Width = 30;
            close.Height = 30;
            close.Cursor = Cursors.Hand;
            close.Background = Brushes.Transparent;
            TextBlock x = new TextBlock();
            x.Text = "\uE711";
            x.FontFamily = WpfTheme.IconFont;
            x.FontSize = 12;
            x.Foreground = WpfTheme.SubText;
            x.HorizontalAlignment = HorizontalAlignment.Center;
            x.VerticalAlignment = VerticalAlignment.Center;
            close.Child = x;
            close.MouseEnter += delegate { x.Foreground = WpfTheme.Text; };
            close.MouseLeave += delegate { x.Foreground = WpfTheme.SubText; };
            close.MouseLeftButtonDown += delegate { Close(); };
            Grid.SetColumn(close, 1);
            g.Children.Add(close);
            return g;
        }

        private UIElement BuildMeta()
        {
            VigilCard card = new VigilCard();
            card.Fill = WpfTheme.MutedBg;
            card.Stroke = WpfTheme.Border;
            card.Cut = 10;
            card.Padding = new Thickness(16, 12, 16, 12);
            card.Margin = new Thickness(0, 16, 0, 0);

            StackPanel stack = new StackPanel();
            for (int i = 0; i < _report.Meta.Count; i++)
            {
                string line = _report.Meta[i] ?? "";
                TextBlock t = new TextBlock();
                t.Text = line;
                t.FontFamily = UiFonts.Mono;
                t.FontSize = 11;
                t.Foreground = i == 0 ? WpfTheme.Text : WpfTheme.SubText;
                t.LineHeight = 20;
                t.TextWrapping = TextWrapping.Wrap;
                stack.Children.Add(t);
            }
            card.Child = stack;
            return card;
        }

        private UIElement BuildCauses()
        {
            StackPanel wrap = new StackPanel();
            wrap.Margin = new Thickness(0, 16, 0, 0);

            TextBlock label = new TextBlock();
            label.Text = "可能的原因";
            label.FontSize = 10;
            label.FontFamily = UiFonts.Mono;
            label.Foreground = WpfTheme.SubText;
            label.Margin = new Thickness(0, 0, 0, 8);
            wrap.Children.Add(label);

            for (int i = 0; i < _report.Causes.Count; i++)
            {
                CrashCause c = _report.Causes[i];
                wrap.Children.Add(CauseRow(c.Level, c.Title, c.Detail));
            }
            return wrap;
        }

        private UIElement CauseRow(string level, string title, string detail)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 7);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            Border chip = new Border();
            chip.CornerRadius = new CornerRadius(3);
            chip.Padding = new Thickness(0, 2, 0, 2);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.VerticalAlignment = VerticalAlignment.Top;
            chip.Margin = new Thickness(0, 2, 0, 0);
            SolidColorBrush tone = level == "高" ? new SolidColorBrush(Color.FromRgb(198, 76, 70))
                : level == "低" ? new SolidColorBrush(Color.FromRgb(120, 132, 140))
                : new SolidColorBrush(Color.FromRgb(190, 140, 60));
            chip.Background = new SolidColorBrush(Color.FromArgb(38, tone.Color.R, tone.Color.G, tone.Color.B));
            TextBlock chipText = new TextBlock();
            chipText.Text = level;
            chipText.FontSize = 9.5;
            chipText.Foreground = tone;
            chipText.HorizontalAlignment = HorizontalAlignment.Center;
            chipText.Margin = new Thickness(8, 0, 8, 0);
            chip.Child = chipText;
            g.Children.Add(chip);

            StackPanel text = new StackPanel();
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 11.5;
            t.Foreground = WpfTheme.Text;
            t.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(t);
            if (!string.IsNullOrEmpty(detail))
            {
                TextBlock d = new TextBlock();
                d.Text = detail;
                d.FontSize = 9.5;
                d.Foreground = WpfTheme.SubText;
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(d);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            return g;
        }

        private UIElement BuildSuggestions()
        {
            StackPanel wrap = new StackPanel();
            wrap.Margin = new Thickness(0, 10, 0, 0);

            for (int i = 0; i < _report.Suggestions.Count; i++)
            {
                TextBlock t = new TextBlock();
                t.Text = (i + 1) + ".  " + _report.Suggestions[i];
                t.FontSize = 10.5;
                t.Foreground = WpfTheme.SubText;
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 0, 0, 3);
                wrap.Children.Add(t);
            }
            return wrap;
        }

        private UIElement BuildFooter()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 20, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;

            WpfButton primary = new WpfButton(_report.Kind == "游戏" ? "重启游戏" : "重启程序", true);
            primary.Margin = new Thickness(0, 0, 8, 0);
            primary.Click += delegate { Close(); if (_restart != null) _restart(); };
            left.Children.Add(primary);

            WpfButton zip = new WpfButton("导出 ZIP", false);
            zip.Margin = new Thickness(0, 0, 8, 0);
            zip.Click += delegate { if (_export != null) _export(_report, true); };
            left.Children.Add(zip);

            WpfButton folder = new WpfButton("导出文件夹", false);
            folder.Margin = new Thickness(0, 0, 8, 0);
            folder.Click += delegate { if (_export != null) _export(_report, false); };
            left.Children.Add(folder);

            WpfButton log = new WpfButton("查看日志", false);
            log.Margin = new Thickness(0, 0, 8, 0);
            log.Click += delegate { if (_openLog != null) _openLog(_report); };
            left.Children.Add(log);

            WpfButton close = new WpfButton("关闭", false);
            close.Click += delegate { Close(); };
            left.Children.Add(close);

            right.Children.Add(left);
            Grid.SetColumn(right, 0);
            g.Children.Add(right);
            return g;
        }
    }
}

