using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// A.R.C.T 风格的滑块：细轨道 + 青色已填充段 + 圆形滑块 + 右侧等宽数值。
    /// 支持点击跳转、拖动、键盘微调、Shift 精调。
    /// </summary>
    internal sealed class WpfSlider : FrameworkElement
    {
        public double Minimum = 0;
        public double Maximum = 100;
        public double Step = 1;
        public int Decimals = 0;
        public string Unit = "";

        private readonly Brush _accent;
        private readonly Brush _track;
        private readonly Brush _knob;
        private readonly Brush _text;
        private readonly Brush _sub;
        private double _value;
        private bool _drag;
        private bool _hover;
        private const double ValueWidth = 64;

        public event Action ValueChanged;

        public WpfSlider(Brush accent, Brush track, Brush knob, Brush text, Brush sub, double width)
        {
            _accent = accent; _track = track; _knob = knob; _text = text; _sub = sub;
            Width = width;
            Height = 24;
            Cursor = Cursors.Hand;
            Focusable = true;
        }

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Snap(value);
                if (Math.Abs(v - _value) < 1e-9) return;
                _value = v;
                InvalidateVisual();
                if (ValueChanged != null) ValueChanged();
            }
        }

        /// <summary>不触发回调地设置（预设套用、外部同步时用）。</summary>
        public void SetValueSilent(double v)
        {
            _value = Snap(v);
            InvalidateVisual();
        }

        private double Snap(double v)
        {
            if (v < Minimum) v = Minimum;
            if (v > Maximum) v = Maximum;
            if (Step > 0) v = Math.Round(v / Step) * Step;
            return v;
        }

        private double Ratio
        {
            get { return (Maximum - Minimum) <= 0 ? 0 : (_value - Minimum) / (Maximum - Minimum); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 4 || h <= 4) return;
            double trackW = Math.Max(10, w - ValueWidth);
            double cy = Math.Round(h / 2);

            // 轨道
            dc.DrawRoundedRectangle(_track, null, new Rect(0, cy - 2, trackW, 4), 2, 2);

            // 已填充段
            double fillW = Math.Round(trackW * Ratio);
            if (fillW > 1.5)
                dc.DrawRoundedRectangle(_accent, null, new Rect(0, cy - 2, fillW, 4), 2, 2);

            // 滑块
            double kx = Math.Round(trackW * Ratio);
            double r = _hover || _drag ? 7.0 : 5.6;
            if (_hover || _drag)
            {
                Brush glow = new SolidColorBrush(Color.FromArgb(42, ((SolidColorBrush)_accent).Color.R,
                    ((SolidColorBrush)_accent).Color.G, ((SolidColorBrush)_accent).Color.B));
                dc.DrawEllipse(glow, null, new Point(kx, cy), r + 4, r + 4);
            }
            dc.DrawEllipse(_knob, new Pen(_accent, 1.6), new Point(kx, cy), r, r);

            // 数值
            string text = _value.ToString("F" + Decimals, CultureInfo.InvariantCulture) + Unit;
            FormattedText ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(UiFonts.Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                10.5, IsFocused ? _accent : _text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            ft.TextAlignment = TextAlignment.Right;
            dc.DrawText(ft, new Point(w - 2, cy - ft.Height / 2));
        }

        private void SetFromX(double x)
        {
            double trackW = Math.Max(10, ActualWidth - ValueWidth);
            double r = x / trackW;
            if (r < 0) r = 0;
            if (r > 1) r = 1;
            double v = Minimum + (Maximum - Minimum) * r;
            if (Step > 0) v = Math.Round(v / Step) * Step;
            Value = v;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            _drag = true;
            CaptureMouse();
            SetFromX(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_hover) { _hover = true; InvalidateVisual(); }
            if (_drag) SetFromX(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drag)
            {
                _drag = false;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_drag) { _hover = false; InvalidateVisual(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            double step = shift ? Step * 0.2 : Step;
            if (e.Key == Key.Left) { Value = _value - step; e.Handled = true; }
            else if (e.Key == Key.Right) { Value = _value + step; e.Handled = true; }
            else if (e.Key == Key.Home) { Value = Minimum; e.Handled = true; }
            else if (e.Key == Key.End) { Value = Maximum; e.Handled = true; }
        }

        protected override void OnGotFocus(RoutedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
        protected override void OnLostFocus(RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
    }

    /// <summary>一排胶囊单选。</summary>
    internal sealed class VigilChips : WrapPanel
    {
        private string[] _labels = new string[0];
        private int _selected;
        private readonly Brush _accent, _line, _text, _sub, _panel;
        private Action<int> _changed;

        public VigilChips(Brush accent, Brush line, Brush text, Brush sub, Brush panel)
        {
            _accent = accent; _line = line; _text = text; _sub = sub; _panel = panel;
        }

        public int SelectedIndex { get { return _selected; } }

        public void SetItems(string[] labels, int selected, Action<int> changed)
        {
            _labels = labels;
            _selected = selected;
            _changed = changed;
            Rebuild();
        }

        public void Select(int index, bool silent)
        {
            if (index < 0 || index >= _labels.Length) return;
            _selected = index;
            Rebuild();
            if (!silent && _changed != null) _changed(index);
        }

        private void Rebuild()
        {
            Children.Clear();
            for (int i = 0; i < _labels.Length; i++)
            {
                int index = i;
                bool sel = i == _selected;
                Border b = new Border();
                b.CornerRadius = new CornerRadius(3);
                b.BorderThickness = new Thickness(1);
                b.BorderBrush = sel ? _accent : _line;
                b.Background = sel ? AccentSoftBrush() : Brushes.Transparent;
                b.Padding = new Thickness(9, 3, 9, 3);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.Cursor = Cursors.Hand;
                TextBlock t = new TextBlock();
                t.Text = _labels[i];
                t.FontSize = 10;
                t.FontFamily = UiFonts.Body;
                t.Foreground = sel ? _accent : _text;
                b.Child = t;
                b.MouseLeftButtonUp += delegate
                {
                    if (_selected == index) return;
                    _selected = index;
                    Rebuild();
                    if (_changed != null) _changed(index);
                };
                b.MouseEnter += delegate { if (!sel) b.BorderBrush = _accent; };
                b.MouseLeave += delegate { if (!sel) b.BorderBrush = _line; };
                Children.Add(b);
            }
        }

        private Brush AccentSoftBrush()
        {
            Color c = ((SolidColorBrush)_accent).Color;
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }

    /// <summary>一排色块单选。</summary>
    internal sealed class VigilSwatches : WrapPanel
    {
        private Color[] _colors;
        private int _selected;
        private readonly Brush _accent, _line;
        private Action<int> _changed;

        public VigilSwatches(Brush accent, Brush line)
        {
            _accent = accent; _line = line;
        }

        public void SetItems(Color[] colors, int selected, Action<int> changed)
        {
            _colors = colors;
            _selected = selected;
            _changed = changed;
            Rebuild();
        }

        public void Select(int index, bool silent)
        {
            if (_colors == null || index < 0 || index >= _colors.Length) return;
            _selected = index;
            Rebuild();
            if (!silent && _changed != null) _changed(index);
        }

        private void Rebuild()
        {
            Children.Clear();
            if (_colors == null) return;
            for (int i = 0; i < _colors.Length; i++)
            {
                int index = i;
                bool sel = i == _selected;
                Border ring = new Border();
                ring.Width = 26; ring.Height = 26;
                ring.CornerRadius = new CornerRadius(4);
                ring.BorderThickness = new Thickness(sel ? 2 : 1);
                ring.BorderBrush = sel ? _accent : _line;
                ring.Margin = new Thickness(0, 0, 7, 7);
                ring.Cursor = Cursors.Hand;
                Border fill = new Border();
                fill.CornerRadius = new CornerRadius(2);
                fill.Margin = new Thickness(2);
                SolidColorBrush b = new SolidColorBrush(_colors[i]);
                b.Freeze();
                fill.Background = b;
                ring.Child = fill;
                ring.MouseLeftButtonUp += delegate
                {
                    _selected = index;
                    Rebuild();
                    if (_changed != null) _changed(index);
                };
                Children.Add(ring);
            }
        }
    }
}
