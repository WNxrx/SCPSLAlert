using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 中途加入的时间校准提示：右上角小卡片，不抢焦点、不挡画面。
    /// 只有提示在的时候才临时接管 F 键与 Esc；关闭后立刻还给游戏。
    /// </summary>
    internal static class CalibPrompt
    {
        private static Window _win;
        private static TextBlock _title, _sub, _foot, _keyA, _keyB;
        private static DispatcherTimer _timer;
        private static int _left;
        private static int _token;
        private static Action<int> _done;
        private static string _nameA = "F7", _nameB = "";

        public static bool Active { get { return _win != null; } }

        /// <summary>done: 0=第一项, 1=第二项, -1=取消(Esc), -2=超时未选。</summary>
        public static void Show(string title, string sub, string keyA, string keyB, int seconds,
            Action<int> done)
        {
            CloseNow();
            if (done == null) return;
            _done = done;
            _nameA = keyA ?? "F7";
            _nameB = keyB ?? "";
            _left = Math.Max(1, seconds);
            _token++;
            int token = _token;

            Border card = new Border();
            card.Background = new SolidColorBrush(Color.FromArgb(235, 12, 20, 30));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 214, 255));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(12, 10, 12, 10);
            card.Width = 268;

            StackPanel body = new StackPanel();
            _title = new TextBlock();
            _title.Text = title;
            _title.FontSize = 11.5;
            _title.FontWeight = FontWeights.SemiBold;
            _title.Foreground = new SolidColorBrush(Color.FromRgb(235, 245, 250));
            _title.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(_title);

            _sub = new TextBlock();
            _sub.Text = sub;
            _sub.FontSize = 10;
            _sub.Margin = new Thickness(0, 4, 0, 0);
            _sub.Foreground = new SolidColorBrush(Color.FromRgb(150, 175, 190));
            _sub.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(_sub);

            if (!string.IsNullOrEmpty(_nameA))
            {
                StackPanel keys = new StackPanel();
                keys.Orientation = Orientation.Horizontal;
                keys.Margin = new Thickness(0, 8, 0, 0);
                _keyA = KeyBox(_nameA);
                keys.Children.Add(_keyA);
                if (!string.IsNullOrEmpty(_nameB))
                {
                    _keyB = KeyBox(_nameB);
                    _keyB.Margin = new Thickness(6, 0, 0, 0);
                    keys.Children.Add(_keyB);
                }
                body.Children.Add(keys);
            }

            _foot = new TextBlock();
            _foot.FontSize = 9.5;
            _foot.Margin = new Thickness(0, 8, 0, 0);
            _foot.Foreground = new SolidColorBrush(Color.FromRgb(120, 145, 160));
            body.Children.Add(_foot);
            card.Child = body;

            _win = new Window();
            _win.WindowStyle = System.Windows.WindowStyle.None;
            _win.AllowsTransparency = true;
            _win.Background = Brushes.Transparent;
            _win.ShowInTaskbar = false;
            _win.Topmost = true;
            _win.ResizeMode = System.Windows.ResizeMode.NoResize;
            _win.ShowActivated = false;
            _win.Focusable = false;
            _win.IsHitTestVisible = false;
            _win.SizeToContent = SizeToContent.WidthAndHeight;
            _win.Content = card;
            _win.Left = SystemParameters.WorkArea.Right - card.Width - 26;
            _win.Top = SystemParameters.WorkArea.Top + 118;
            _win.Show();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += delegate
            {
                if (token != _token) return;
                _left--;
                if (_left > 0) UpdateFoot();
                else if (string.IsNullOrEmpty(_nameA)) Finish(0);   // 没有按键的提示：到点自动校准
                else Finish(-2);                                    // 要玩家选的提示：超时不算校准
            };
            UpdateFoot();
            _timer.Start();
        }

        private static TextBlock KeyBox(string name)
        {
            Border box = new Border();
            box.Background = new SolidColorBrush(Color.FromRgb(22, 40, 54));
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 214, 255));
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(4);
            box.Padding = new Thickness(8, 3, 8, 3);
            TextBlock t = new TextBlock();
            t.Text = name;
            t.FontSize = 10.5;
            t.FontFamily = UiFonts.Mono;
            t.Foreground = new SolidColorBrush(Color.FromRgb(210, 240, 250));
            box.Child = t;
            // 用 TextBlock 包一层，方便后面整体隐藏；这里直接返回 label
            TextBlock host = new TextBlock();
            host.Text = name;
            host.FontSize = 10.5;
            host.FontFamily = UiFonts.Mono;
            host.Foreground = new SolidColorBrush(Color.FromRgb(210, 240, 250));
            host.Background = new SolidColorBrush(Color.FromRgb(22, 40, 54));
            host.Padding = new Thickness(8, 3, 8, 3);
            host.Margin = new Thickness(0);
            return host;
        }

        private static void UpdateFoot()
        {
            if (_foot == null) return;
            string keys = string.IsNullOrEmpty(_nameA) ? "" : (_nameA + (string.IsNullOrEmpty(_nameB) ? "" : " / " + _nameB) + "  ");
            _foot.Text = keys + _left + " 秒后校准 · Esc 取消";
        }

        /// <summary>钩子线程调用：只做字符串比较和投递，不能做重活。</summary>
        public static bool HandleKey(string combo)
        {
            if (_win == null || string.IsNullOrEmpty(combo)) return false;
            string key = combo;
            int plus = key.LastIndexOf('+');
            if (plus >= 0 && plus < key.Length - 1) key = key.Substring(plus + 1);

            int code;
            if (string.Equals(key, "Esc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Escape", StringComparison.OrdinalIgnoreCase)) code = -1;
            else if (string.Equals(key, _nameA, StringComparison.OrdinalIgnoreCase)) code = 0;
            else if (!string.IsNullOrEmpty(_nameB) && string.Equals(key, _nameB, StringComparison.OrdinalIgnoreCase)) code = 1;
            else return false;

            Dispatcher d = _win.Dispatcher;
            if (d == null) return true;
            d.BeginInvoke(new Action(delegate { Finish(code); }));
            return true;
        }

        private static void Finish(int code)
        {
            if (_win == null) return;
            _token++;
            if (_timer != null) { _timer.Stop(); _timer = null; }

            if (code == -1)
            {
                if (_foot != null) _foot.Text = "已取消";
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromSeconds(1);
                t.Tick += delegate
                {
                    t.Stop();
                    CloseNow();
                    if (_done != null) { Action<int> d = _done; _done = null; d(-1); }
                };
                _timer = t;
                t.Start();
                return;
            }

            CloseNow();
            if (_done != null) { Action<int> d = _done; _done = null; d(code); }
        }

        private static void CloseNow()
        {
            if (_timer != null) { _timer.Stop(); _timer = null; }
            if (_win != null)
            {
                try { _win.Close(); } catch { }
                _win = null;
            }
            _title = _sub = _foot = _keyA = _keyB = null;
        }
    }
}
