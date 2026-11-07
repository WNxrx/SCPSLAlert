using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SCPSLAlert
{
    internal sealed class WpfLaunchGuardWindow : Window
    {
        private readonly List<LaunchHit> _hits;
        private readonly Action<List<LaunchHit>> _proceed;
        private readonly Action _cancel;

        public WpfLaunchGuardWindow(List<LaunchHit> hits,
            Action<List<LaunchHit>> proceed, Action cancel)
        {
            _hits = hits;
            _proceed = proceed;
            _cancel = cancel;

            WpfScroll.Apply(this, !ModernTheme.IsDark);
            Title = "启动前检查";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 720;
            WindowStyle = System.Windows.WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = WpfTheme.UiFont;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            Build();
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Cancel();
            };
        }

        private void Build()
        {
            Border root = new Border();
            root.Margin = new Thickness(18);
            root.Background = WpfTheme.PageBg;
            root.BorderBrush = WpfTheme.Danger;
            root.BorderThickness = new Thickness(1);
            root.Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.35,
                Color = Colors.Black
            };
            root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = root;

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(26, 20, 26, 20);
            root.Child = body;

            TextBlock eyebrow = new TextBlock();
            eyebrow.Text = "LAUNCH GUARD  /  启动前检查";
            eyebrow.FontFamily = UiFonts.Mono;
            eyebrow.FontSize = 9.5;
            eyebrow.Foreground = WpfTheme.Danger;
            body.Children.Add(eyebrow);

            TextBlock title = new TextBlock();
            title.Text = "发现 " + _hits.Count + " 个可疑程序";
            title.FontSize = 26;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = WpfTheme.Text;
            title.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(title);

            TextBlock desc = new TextBlock();
            desc.Text = "SL-AC 会检查这些工具，可能在启动时弹窗或直接拒绝进入。";
            desc.FontSize = 11;
            desc.Foreground = WpfTheme.SubText;
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(desc);

            VigilCard card = new VigilCard();
            card.Fill = WpfTheme.MutedBg;
            card.Stroke = WpfTheme.Border;
            card.Cut = 10;
            card.Padding = new Thickness(14, 10, 14, 10);
            card.Margin = new Thickness(0, 14, 0, 0);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.MaxHeight = 300;
            StackPanel list = new StackPanel();
            for (int i = 0; i < _hits.Count; i++) list.Children.Add(HitRow(_hits[i]));
            scroll.Content = list;
            card.Child = scroll;
            body.Children.Add(card);

            TextBlock hint = new TextBlock();
            hint.Text = "结束进程后如果仍然被 SL-AC 拦截，说明它可能还有驱动或服务残留。";
            hint.FontSize = 9;
            hint.Foreground = WpfTheme.SubText;
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(hint);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 16, 0, 0);

            WpfButton kill = new WpfButton("结束并启动", true);
            kill.Margin = new Thickness(0, 0, 8, 0);
            kill.Click += delegate
            {
                Close();
                if (_proceed != null) _proceed(_hits);
            };
            actions.Children.Add(kill);

            WpfButton ignore = new WpfButton("仍然启动", false);
            ignore.Margin = new Thickness(0, 0, 8, 0);
            ignore.Click += delegate
            {
                Close();
                if (_proceed != null) _proceed(new List<LaunchHit>());
            };
            actions.Children.Add(ignore);

            WpfButton cancel = new WpfButton("取消", false);
            cancel.Click += delegate { Cancel(); };
            actions.Children.Add(cancel);

            body.Children.Add(actions);
        }

        private UIElement HitRow(LaunchHit hit)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 8);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel();
            StackPanel nameRow = new StackPanel();
            nameRow.Orientation = Orientation.Horizontal;
            TextBlock name = new TextBlock();
            name.Text = hit.Name;
            name.FontSize = 11.5;
            name.FontWeight = FontWeights.Bold;
            name.Foreground = WpfTheme.Text;
            nameRow.Children.Add(name);
            TextBlock pid = new TextBlock();
            pid.Text = hit.Service ? "  服务" : "  PID " + hit.Pid;
            pid.FontFamily = UiFonts.Mono;
            pid.FontSize = 9;
            pid.Foreground = WpfTheme.SubText;
            pid.VerticalAlignment = VerticalAlignment.Center;
            nameRow.Children.Add(pid);
            text.Children.Add(nameRow);

            TextBlock reason = new TextBlock();
            reason.Text = hit.Reason;
            reason.FontSize = 9.5;
            reason.Foreground = WpfTheme.Danger;
            text.Children.Add(reason);

            if (!string.IsNullOrEmpty(hit.Path))
            {
                TextBlock path = new TextBlock();
                path.Text = hit.Path;
                path.FontSize = 8.5;
                path.Foreground = WpfTheme.SubText;
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(path);
            }
            g.Children.Add(text);
            return g;
        }

        private void Cancel()
        {
            Close();
            if (_cancel != null) _cancel();
        }
    }
}
