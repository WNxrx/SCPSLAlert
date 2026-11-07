using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 8 批追加：漂浮文字画布编辑器。
    ///
    /// - 文字逐字渲染；单击某个字选中，按住左键拖框可多选；
    /// - 右侧修改选中字的颜色、左右/上下抖动、速度、缩放、旋转；
    /// - 整句抖动、逐字显现和显示时长也在右侧；
    /// - 取消不会改动原动作。
    /// </summary>
    internal sealed class FloatingTextEditorWindow : Window
    {
        private readonly TriggerAction _target;
        private readonly TriggerAction _work;
        private readonly Canvas _canvas;
        private readonly TextBox _text;
        private readonly Rectangle _marquee;
        private readonly StackPanel _charProps;
        private readonly TextBlock _selectionLabel;
        private readonly List<TextBlock> _blocks = new List<TextBlock>();
        private readonly List<Rect> _bounds = new List<Rect>();
        private readonly HashSet<int> _selected = new HashSet<int>();
        private bool _dragging;
        private Point _dragStart;

        public FloatingTextEditorWindow(TriggerAction action, bool light)
        {
            _target = action;
            _work = action.Clone();

            Title = "漂浮文字画布";
            Width = 1020;
            Height = 660;
            MinWidth = 820;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(11, 13, 14));
            Foreground = new SolidColorBrush(Color.FromRgb(230, 227, 218));
            FontFamily = UiFonts.Body;

            Grid root = new Grid();
            root.Margin = new Thickness(16);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

            StackPanel top = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "漂浮文字画布";
            title.FontSize = 20;
            title.FontWeight = FontWeights.Bold;
            top.Children.Add(title);
            TextBlock tip = new TextBlock();
            tip.Text = "单击选字，拖框多选。";
            tip.FontSize = 10;
            tip.Foreground = new SolidColorBrush(Color.FromRgb(150, 160, 158));
            tip.Margin = new Thickness(0, 2, 0, 8);
            top.Children.Add(tip);
            _text = new TextBox();
            _text.Text = _work.Text ?? "";
            _text.FontSize = 13;
            _text.Padding = new Thickness(8, 6, 8, 6);
            _text.Background = new SolidColorBrush(Color.FromRgb(20, 25, 28));
            _text.Foreground = Foreground;
            _text.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
            _text.TextChanged += delegate
            {
                _work.Text = _text.Text ?? "";
                BuildChars();
            };
            top.Children.Add(_text);
            Grid.SetColumnSpan(top, 3);
            root.Children.Add(top);

            Border frame = new Border();
            frame.Margin = new Thickness(0, 12, 0, 0);
            frame.Background = new SolidColorBrush(Color.FromRgb(14, 18, 21));
            frame.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(4);
            frame.ClipToBounds = true;
            _canvas = new Canvas();
            _canvas.Background = Brushes.Transparent;
            _canvas.ClipToBounds = true;
            frame.Child = _canvas;
            Grid.SetRow(frame, 1);
            root.Children.Add(frame);

            _marquee = new Rectangle();
            _marquee.Stroke = new SolidColorBrush(Color.FromRgb(67, 207, 187));
            _marquee.StrokeThickness = 1;
            _marquee.Fill = new SolidColorBrush(Color.FromArgb(35, 67, 207, 187));
            _marquee.Visibility = Visibility.Collapsed;
            _marquee.IsHitTestVisible = false;
            _canvas.Children.Add(_marquee);

            _canvas.PreviewMouseLeftButtonDown += OnCanvasDown;
            _canvas.PreviewMouseMove += OnCanvasMove;
            _canvas.PreviewMouseLeftButtonUp += OnCanvasUp;

            _selectionLabel = new TextBlock();
            _selectionLabel.FontSize = 11;
            _selectionLabel.Foreground = new SolidColorBrush(Color.FromRgb(67, 207, 187));
            _selectionLabel.Margin = new Thickness(0, 12, 0, 4);
            _selectionLabel.Text = "未选中";

            _charProps = new StackPanel();
            _charProps.Margin = new Thickness(0, 0, 0, 0);
            _charProps.Children.Add(_selectionLabel);

            ScrollViewer right = new ScrollViewer();
            right.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            right.Content = _charProps;
            Grid.SetRow(right, 1);
            Grid.SetColumn(right, 2);
            right.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(right);

            StackPanel bottom = new StackPanel();
            bottom.Orientation = Orientation.Horizontal;
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            Button cancel = new Button();
            cancel.Content = "取消";
            cancel.Width = 80;
            cancel.Height = 30;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += delegate { DialogResult = false; };
            bottom.Children.Add(cancel);
            Button save = new Button();
            save.Content = "保存";
            save.Width = 88;
            save.Height = 30;
            save.Background = new SolidColorBrush(Color.FromRgb(67, 207, 187));
            save.Foreground = new SolidColorBrush(Color.FromRgb(12, 16, 20));
            save.BorderThickness = new Thickness(0);
            save.Click += delegate { Save(); };
            bottom.Children.Add(save);
            Grid.SetRow(bottom, 2);
            Grid.SetColumnSpan(bottom, 3);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(bottom);

            Content = root;
            BuildChars();
            BuildProperties();
        }

        private void BuildChars()
        {
            HashSet<int> keep = new HashSet<int>(_selected);
            _canvas.Children.Clear();
            _canvas.Children.Add(_marquee);
            _blocks.Clear();
            _bounds.Clear();
            _selected.Clear();

            string text = _work.Text ?? "";
            double size = Math.Max(8, Math.Min(120, _work.FontSize <= 0 ? 32 : _work.FontSize));
            FontFamily family = new FontFamily(string.IsNullOrEmpty(_work.Font)
                ? "Microsoft YaHei UI" : _work.Font);
            double x = 36;
            double y = 36;
            for (int i = 0; i < text.Length; i++)
            {
                TriggerCharStyle s = _work.StyleFor(i);
                TextBlock tb = new TextBlock();
                tb.Text = text[i] == ' ' ? " " : text[i].ToString();
                tb.FontFamily = family;
                tb.FontSize = size;
                tb.FontWeight = FontWeights.Bold;
                tb.Foreground = new SolidColorBrush(ParseColor(
                    string.IsNullOrEmpty(s.Color) ? _work.Color1 : s.Color, Colors.White));
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = Math.Max(1, tb.DesiredSize.Width);
                double h = Math.Max(1, tb.DesiredSize.Height);
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
                _canvas.Children.Add(tb);
                _blocks.Add(tb);
                _bounds.Add(new Rect(x, y, w, h));
                x += w;
            }
            _canvas.Width = Math.Max(300, x + 36);
            _canvas.Height = 260;
            foreach (int i in keep)
                if (i >= 0 && i < _blocks.Count) _selected.Add(i);
            UpdateSelectionVisual();
        }

        private void BuildProperties()
        {
            _charProps.Children.Clear();
            _charProps.Children.Add(_selectionLabel);

            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 2, 0, 4);
            string[] colors = { "#FFFFFF", "#43CFBB", "#FF4D4D", "#FFAD4A", "#6EE7FF", "#FF6BD6", "#C6FF3D", "#A78BFA" };
            for (int i = 0; i < colors.Length; i++)
            {
                string hex = colors[i];
                Border sw = new Border();
                sw.Width = 22;
                sw.Height = 22;
                sw.Margin = new Thickness(0, 0, 4, 4);
                sw.CornerRadius = new CornerRadius(2);
                sw.Background = new SolidColorBrush(ParseColor(hex, Colors.White));
                sw.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
                sw.BorderThickness = new Thickness(1);
                sw.Cursor = Cursors.Hand;
                sw.ToolTip = hex;
                sw.MouseLeftButtonUp += delegate { ApplyColor(hex); };
                palette.Children.Add(sw);
            }
            _charProps.Children.Add(Label("颜色", 9));
            _charProps.Children.Add(palette);

            TextBox hexBox = new TextBox();
            hexBox.Width = 100;
            hexBox.Text = "#FFFFFF";
            hexBox.Background = new SolidColorBrush(Color.FromRgb(20, 25, 28));
            hexBox.Foreground = Foreground;
            hexBox.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
            Button applyHex = new Button();
            applyHex.Content = "应用";
            applyHex.Width = 52;
            applyHex.Height = 24;
            applyHex.Margin = new Thickness(4, 0, 0, 0);
            applyHex.Click += delegate { ApplyColor(hexBox.Text); };
            StackPanel hexRow = new StackPanel();
            hexRow.Orientation = Orientation.Horizontal;
            hexRow.Margin = new Thickness(0, 4, 0, 6);
            hexRow.Children.Add(hexBox);
            hexRow.Children.Add(applyHex);
            _charProps.Children.Add(hexRow);

            TriggerCharStyle first = FirstSelectedStyle();
            _charProps.Children.Add(Label("左右抖动", 9));
            _charProps.Children.Add(Number(first == null ? 2 : first.JitterX, 0, 40, delegate(double v)
            {
                ApplyToSelected(delegate(TriggerCharStyle s) { s.JitterX = v; });
            }));
            _charProps.Children.Add(Label("上下抖动", 9));
            _charProps.Children.Add(Number(first == null ? 5 : first.JitterY, 0, 40, delegate(double v)
            {
                ApplyToSelected(delegate(TriggerCharStyle s) { s.JitterY = v; });
            }));
            _charProps.Children.Add(Label("速度", 9));
            _charProps.Children.Add(Number(first == null ? 1 : first.Speed, 0.1, 4, delegate(double v)
            {
                ApplyToSelected(delegate(TriggerCharStyle s) { s.Speed = v; });
            }));
            _charProps.Children.Add(Label("缩放", 9));
            _charProps.Children.Add(Number(first == null ? 100 : first.Scale * 100, 20, 300, delegate(double v)
            {
                ApplyToSelected(delegate(TriggerCharStyle s) { s.Scale = v / 100.0; });
            }));
            _charProps.Children.Add(Label("旋转", 9));
            _charProps.Children.Add(Number(first == null ? 0 : first.Rotate, -180, 180, delegate(double v)
            {
                ApplyToSelected(delegate(TriggerCharStyle s) { s.Rotate = v; });
            }));

            _charProps.Children.Add(Label("左右", 9));
            _charProps.Children.Add(Number(_work.FloatJitterX, 0, 40, delegate(double v)
            { _work.FloatJitterX = v; }));
            _charProps.Children.Add(Label("上下", 9));
            _charProps.Children.Add(Number(_work.FloatJitterY, 0, 40, delegate(double v)
            { _work.FloatJitterY = v; }));
            _charProps.Children.Add(Label("速度", 9));
            _charProps.Children.Add(Number(_work.FloatJitterSpeed, 0.1, 4, delegate(double v)
            { _work.FloatJitterSpeed = v; }));

            CheckBox reveal = new CheckBox();
            reveal.Content = "逐字显现";
            reveal.IsChecked = _work.FloatReveal;
            reveal.Foreground = Foreground;
            reveal.Margin = new Thickness(0, 8, 0, 2);
            reveal.Checked += delegate { _work.FloatReveal = true; };
            reveal.Unchecked += delegate { _work.FloatReveal = false; };
            _charProps.Children.Add(reveal);
            _charProps.Children.Add(Label("间隔", 9));
            _charProps.Children.Add(Number(_work.FloatRevealDelayMs, 0, 1000, delegate(double v)
            { _work.FloatRevealDelayMs = (int)v; }));
            _charProps.Children.Add(Label("时长", 9));
            _charProps.Children.Add(Number(_work.FloatLifeMs, 0, 60000, delegate(double v)
            { _work.FloatLifeMs = (int)v; }));

            Button applyAll = new Button();
            applyAll.Content = "应用到全部";
            applyAll.Margin = new Thickness(0, 8, 0, 0);
            applyAll.Height = 28;
            applyAll.Click += delegate
            {
                if (_blocks.Count == 0) return;
                TriggerCharStyle src = FirstSelectedStyle();
                if (src == null) return;
                for (int i = 0; i < _blocks.Count; i++)
                {
                    TriggerCharStyle s = _work.StyleFor(i);
                    s.JitterX = src.JitterX; s.JitterY = src.JitterY;
                    s.Speed = src.Speed; s.Scale = src.Scale; s.Rotate = src.Rotate;
                }
                BuildChars();
            };
            _charProps.Children.Add(applyAll);
            UpdateSelectionLabel();
        }

        private FrameworkElement Number(double value, double min, double max, Action<double> changed)
        {
            TextBox box = new TextBox();
            box.Width = 82;
            box.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
            box.Background = new SolidColorBrush(Color.FromRgb(20, 25, 28));
            box.Foreground = Foreground;
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
            box.Margin = new Thickness(0, 1, 0, 4);
            box.LostFocus += delegate
            {
                double v;
                if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    if (v < min) v = min;
                    if (v > max) v = max;
                    changed(v);
                }
            };
            return box;
        }

        private TextBlock Label(string text, double size)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.Foreground = new SolidColorBrush(Color.FromRgb(150, 160, 158));
            t.Margin = new Thickness(0, 7, 0, 1);
            return t;
        }

        private TriggerCharStyle FirstSelectedStyle()
        {
            foreach (int i in _selected) return _work.StyleFor(i);
            return null;
        }

        private void ApplyColor(string hex)
        {
            Color c = ParseColor(hex, Colors.White);
            string normalized = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            ApplyToSelected(delegate(TriggerCharStyle s) { s.Color = normalized; });
        }

        private void ApplyToSelected(Action<TriggerCharStyle> apply)
        {
            if (_selected.Count == 0) return;
            foreach (int i in _selected) apply(_work.StyleFor(i));
            BuildChars();
        }

        private void OnCanvasDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(_canvas);
            _dragStart = p;
            _dragging = true;
            _marquee.Visibility = Visibility.Collapsed;
            _canvas.CaptureMouse();
            int hit = HitChar(p);
            if (hit >= 0)
            {
                _selected.Clear();
                _selected.Add(hit);
                UpdateSelectionVisual();
                e.Handled = true;
            }
            else
            {
                _selected.Clear();
                UpdateSelectionVisual();
            }
        }

        private void OnCanvasMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = e.GetPosition(_canvas);
            double x = Math.Min(_dragStart.X, p.X);
            double y = Math.Min(_dragStart.Y, p.Y);
            double w = Math.Abs(p.X - _dragStart.X);
            double h = Math.Abs(p.Y - _dragStart.Y);
            if (w < 3 && h < 3) return;
            Canvas.SetLeft(_marquee, x);
            Canvas.SetTop(_marquee, y);
            _marquee.Width = w;
            _marquee.Height = h;
            _marquee.Visibility = Visibility.Visible;
            _selected.Clear();
            Rect box = new Rect(x, y, w, h);
            for (int i = 0; i < _bounds.Count; i++)
                if (box.IntersectsWith(_bounds[i])) _selected.Add(i);
            UpdateSelectionVisual();
        }

        private void OnCanvasUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            _marquee.Visibility = Visibility.Collapsed;
            _canvas.ReleaseMouseCapture();
            UpdateSelectionLabel();
        }

        private int HitChar(Point p)
        {
            for (int i = 0; i < _bounds.Count; i++)
                if (_bounds[i].Contains(p)) return i;
            return -1;
        }

        private void UpdateSelectionVisual()
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                _blocks[i].Background = _selected.Contains(i)
                    ? new SolidColorBrush(Color.FromArgb(90, 67, 207, 187))
                    : Brushes.Transparent;
            }
            UpdateSelectionLabel();
        }

        private void UpdateSelectionLabel()
        {
            if (_selectionLabel == null) return;
            _selectionLabel.Text = _selected.Count == 0
                ? "未选中"
                : "已选中 " + _selected.Count + " 个字";
        }

        private void Save()
        {
            _target.Text = _work.Text ?? "";
            _target.Font = _work.Font;
            _target.FontSize = _work.FontSize;
            _target.Color1 = _work.Color1;
            _target.Outline = _work.Outline;
            _target.XPercent = _work.XPercent;
            _target.YPercent = _work.YPercent;
            _target.Opacity = _work.Opacity;
            _target.FloatJitterX = _work.FloatJitterX;
            _target.FloatJitterY = _work.FloatJitterY;
            _target.FloatJitterSpeed = _work.FloatJitterSpeed;
            _target.FloatReveal = _work.FloatReveal;
            _target.FloatRevealDelayMs = _work.FloatRevealDelayMs;
            _target.FloatLifeMs = _work.FloatLifeMs;
            _target.CharStyles = new List<TriggerCharStyle>();
            for (int i = 0; i < _work.CharStyles.Count; i++)
                if (_work.CharStyles[i] != null) _target.CharStyles.Add(_work.CharStyles[i].Clone());
            DialogResult = true;
        }

        private static Color ParseColor(string s, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return fallback;
                return (Color)ColorConverter.ConvertFromString(s);
            }
            catch { return fallback; }
        }
    }
}
