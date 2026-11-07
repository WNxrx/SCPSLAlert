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

namespace SCPSLAlert
{
    internal sealed class WpfSwitch : FrameworkElement
    {
        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
            "IsChecked", typeof(bool), typeof(WpfSwitch),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCheckedChanged));

        private static readonly DependencyProperty ThumbProperty = DependencyProperty.Register(
            "Thumb", typeof(double), typeof(WpfSwitch),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private bool _hover;

        public event EventHandler CheckedChanged;

        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }

        public WpfSwitch()
        {
            Width = 46;
            Height = 26;
            Cursor = Cursors.Hand;
            Focusable = true;
            SnapsToDevicePixels = true;
            WpfTheme.Changed += OnThemeChanged;
            MouseEnter += delegate { _hover = true; InvalidateVisual(); };
            MouseLeave += delegate { _hover = false; InvalidateVisual(); };
            MouseLeftButtonUp += delegate { Toggle(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Space || e.Key == Key.Enter)
                {
                    Toggle();
                    e.Handled = true;
                }
            };
        }

        private static void OnCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WpfSwitch sw = (WpfSwitch)d;
            bool value = (bool)e.NewValue;
            DoubleAnimation anim = new DoubleAnimation(value ? 1.0 : 0.0,
                new Duration(TimeSpan.FromMilliseconds(180)));
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            sw.BeginAnimation(ThumbProperty, anim);
            if (sw.CheckedChanged != null) sw.CheckedChanged(sw, EventArgs.Empty);
        }

        private void Toggle()
        {
            IsChecked = !IsChecked;
        }

        private void OnThemeChanged()
        {
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double h = Height;
            double w = Width;
            double radius = h / 2;
            Brush track = IsChecked ? WpfTheme.Accent : WpfTheme.TrackOff;
            if (_hover)
                track = IsChecked ? WpfTheme.Blend(WpfTheme.Accent, Brushes.White, 0.12) : WpfTheme.Blend(WpfTheme.TrackOff, Brushes.White, 0.18);
            if (IsFocused)
            {
                double glow = 3;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(38, 0, 133, 126)), null,
                    new Rect(-glow, -glow, w + glow * 2, h + glow * 2), radius + glow, radius + glow);
            }
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), radius, radius);
            double thumb = h - 4;
            double pos = (double)GetValue(ThumbProperty);
            double x = 2 + pos * (w - thumb - 4);
            dc.DrawEllipse(WpfTheme.Knob, null, new Point(x + thumb / 2, h / 2), thumb / 2, thumb / 2);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            InvalidateVisual();
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            InvalidateVisual();
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            InvalidateVisual();
        }
    }

    internal sealed class WpfButton : Button
    {
        private readonly SolidColorBrush _fill;
        private readonly Color _baseColor;
        private readonly bool _primary;

        public WpfButton(string text, bool primary)
        {
            _primary = primary;
            Content = text;
            Height = 34;
            Padding = new Thickness(16, 0, 16, 0);
            FontFamily = WpfTheme.UiFont;
            FontSize = 13;
            Cursor = Cursors.Hand;
            BorderThickness = new Thickness(0);
            FocusVisualStyle = null;
            _baseColor = primary ? ToColor(WpfTheme.Accent) : ToColor(WpfTheme.CardBg);
            _fill = new SolidColorBrush(_baseColor);
            Background = _fill;
            Foreground = primary ? Brushes.White : WpfTheme.Text;
            Template = BuildTemplate();
            WpfTheme.Changed += OnThemeChanged;
            ApplyTheme();
        }

        private static Color ToColor(Brush b)
        {
            SolidColorBrush s = b as SolidColorBrush;
            return s == null ? Colors.White : s.Color;
        }

        private static ControlTemplate BuildTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            ControlTemplate t = new ControlTemplate(typeof(Button));
            t.VisualTree = border;
            return t;
        }

        private void OnThemeChanged()
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (_primary)
            {
                _fill.Color = ToColor(WpfTheme.Accent);
                Foreground = Brushes.White;
                BorderBrush = WpfTheme.Accent;
            }
            else
            {
                _fill.Color = ToColor(WpfTheme.CardBg);
                Foreground = WpfTheme.Text;
                BorderBrush = WpfTheme.InputBorder;
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Color target = _primary
                ? Lighten(ToColor(WpfTheme.Accent), 0.16)
                : ToColor(WpfTheme.AccentSoft);
            Animate(target, 130);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Animate(_baseColor, 160);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Animate(Lighten(_baseColor, _primary ? 0.05 : 0.08), 80);
            RenderTransform = new ScaleTransform(0.985, 0.985);
            RenderTransformOrigin = new Point(0.5, 0.5);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            RenderTransform = Transform.Identity;
            Animate(_baseColor, 120);
        }

        private void Animate(Color target, int ms)
        {
            ColorAnimation a = new ColorAnimation(target, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        private static Color Lighten(Color c, double t)
        {
            return Color.FromRgb(
                (byte)(c.R + (255 - c.R) * t),
                (byte)(c.G + (255 - c.G) * t),
                (byte)(c.B + (255 - c.B) * t));
        }
    }

    internal sealed class WpfField : Border
    {
        public readonly TextBox Box;

        public WpfField()
        {
            CornerRadius = new CornerRadius(9);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(11, 0, 11, 0);
            Height = 34;
            Box = new TextBox();
            Box.BorderThickness = new Thickness(0);
            Box.Background = Brushes.Transparent;
            Box.FontFamily = WpfTheme.UiFont;
            Box.FontSize = 13;
            Box.VerticalContentAlignment = VerticalAlignment.Center;
            Box.Padding = new Thickness(0);
            Box.CaretBrush = WpfTheme.Accent;
            Child = Box;
            WpfTheme.Changed += OnThemeChanged;
            ApplyTheme();
            Box.GotFocus += delegate { BorderBrush = WpfTheme.Accent; };
            Box.LostFocus += delegate { BorderBrush = WpfTheme.InputBorder; };
        }

        public string Text
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        private void OnThemeChanged()
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            Background = WpfTheme.InputBg;
            BorderBrush = WpfTheme.InputBorder;
            Box.Foreground = WpfTheme.Text;
            Box.CaretBrush = WpfTheme.Accent;
        }
    }

    internal sealed class WpfNumberField : Grid
    {
        private readonly WpfField _field;
        private readonly TextBlock _up;
        private readonly TextBlock _down;
        public decimal Minimum = 0;
        public decimal Maximum = 100;
        public decimal Increment = 1;
        public event EventHandler ValueChanged;

        public decimal Value
        {
            get
            {
                decimal v;
                if (!decimal.TryParse(_field.Text, out v)) return Minimum;
                if (v < Minimum) v = Minimum;
                if (v > Maximum) v = Maximum;
                return v;
            }
            set
            {
                decimal v = value;
                if (v < Minimum) v = Minimum;
                if (v > Maximum) v = Maximum;
                _field.Text = v.ToString("0.##");
            }
        }

        public WpfNumberField(int width)
        {
            Width = width;
            Height = 34;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            _field = new WpfField();
            SetColumn(_field, 0);
            Children.Add(_field);
            Grid step = new Grid();
            step.RowDefinitions.Add(new RowDefinition());
            step.RowDefinitions.Add(new RowDefinition());
            _up = CreateArrow(true);
            _down = CreateArrow(false);
            Border upHit = new Border();
            upHit.Background = Brushes.Transparent;
            upHit.Child = _up;
            Border downHit = new Border();
            downHit.Background = Brushes.Transparent;
            downHit.Child = _down;
            upHit.MouseLeftButtonUp += delegate { Change(Increment); };
            downHit.MouseLeftButtonUp += delegate { Change(-Increment); };
            SetRow(upHit, 0);
            SetRow(downHit, 1);
            step.Children.Add(upHit);
            step.Children.Add(downHit);
            Border host = new Border();
            host.Background = WpfTheme.InputBg;
            host.BorderBrush = WpfTheme.InputBorder;
            host.BorderThickness = new Thickness(0, 1, 1, 1);
            host.CornerRadius = new CornerRadius(0, 9, 9, 0);
            host.Margin = new Thickness(-1, 0, 0, 0);
            host.Child = step;
            SetColumn(host, 1);
            Children.Add(host);
            WpfTheme.Changed += OnThemeChanged;
            _field.Box.LostFocus += delegate { Normalize(); };
            _field.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) Normalize();
            };
            WpfTheme.Changed += delegate
            {
                host.Background = WpfTheme.InputBg;
                host.BorderBrush = WpfTheme.InputBorder;
            };
        }

        private static TextBlock CreateArrow(bool up)
        {
            TextBlock t = new TextBlock();
            t.Text = up ? "\uE70E" : "\uE70D";
            t.FontFamily = WpfTheme.IconFont;
            t.FontSize = 9;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Cursor = Cursors.Hand;
            t.Foreground = WpfTheme.SubText;
            return t;
        }

        private void OnThemeChanged()
        {
            _up.Foreground = WpfTheme.SubText;
            _down.Foreground = WpfTheme.SubText;
        }

        private void Change(decimal delta)
        {
            Value = Value + delta;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        private void Normalize()
        {
            Value = Value;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    internal sealed class WpfSelect : Grid
    {
        private readonly Border _field;
        private readonly TextBlock _text;
        private readonly Popup _popup;
        private readonly Border _drop;
        private readonly StackPanel _items;
        private readonly ScrollViewer _scroll;
        private bool _open;

        public readonly List<string> Items = new List<string>();
        public event EventHandler SelectionChanged;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                _selected = v;
                _text.Text = v >= 0 && v < Items.Count ? Items[v] : "";
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        private int _selected = -1;

        public WpfSelect(int width)
        {
            Width = width;
            Height = 34;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            _field = new Border();
            _field.CornerRadius = new CornerRadius(9);
            _field.BorderThickness = new Thickness(1);
            _field.Background = WpfTheme.InputBg;
            _field.BorderBrush = WpfTheme.InputBorder;
            _field.Cursor = Cursors.Hand;
            _text = new TextBlock();
            _text.FontFamily = WpfTheme.UiFont;
            _text.FontSize = 13;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.Margin = new Thickness(11, 0, 0, 0);
            _text.TextTrimming = TextTrimming.CharacterEllipsis;
            _field.Child = _text;
            SetColumn(_field, 0);
            Children.Add(_field);

            Grid arrowHost = new Grid();
            arrowHost.Margin = new Thickness(-1, 0, 0, 0);
            Border arrowBg = new Border();
            arrowBg.Background = WpfTheme.InputBg;
            arrowBg.BorderBrush = WpfTheme.InputBorder;
            arrowBg.BorderThickness = new Thickness(0, 1, 1, 1);
            arrowBg.CornerRadius = new CornerRadius(0, 9, 9, 0);
            TextBlock arrow = new TextBlock();
            arrow.Text = "\uE70D";
            arrow.FontFamily = WpfTheme.IconFont;
            arrow.FontSize = 10;
            arrow.Foreground = WpfTheme.SubText;
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrowBg.Child = arrow;
            arrowHost.Children.Add(arrowBg);
            SetColumn(arrowHost, 1);
            Children.Add(arrowHost);

            _items = new StackPanel();
            _scroll = new ScrollViewer();
            _scroll.Content = _items;
            _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroll.MaxHeight = 270;
            _drop = new Border();
            _drop.CornerRadius = new CornerRadius(10);
            _drop.Background = WpfTheme.CardBg;
            _drop.BorderBrush = WpfTheme.CardBorder;
            _drop.BorderThickness = new Thickness(1);
            _drop.Padding = new Thickness(4);
            _drop.Child = _scroll;
            _drop.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.22, Color = Colors.Black };
            _popup = new Popup();
            _popup.AllowsTransparency = true;
            _popup.StaysOpen = false;
            _popup.Placement = PlacementMode.Bottom;
            _popup.PlacementTarget = this;
            _popup.VerticalOffset = 4;
            _popup.Child = _drop;
            _popup.Closed += delegate { _open = false; };
            MouseLeftButtonUp += delegate { ToggleDrop(); };
            WpfTheme.Changed += OnThemeChanged;
            ApplyTheme();
        }

        private void ToggleDrop()
        {
            if (_open) { _popup.IsOpen = false; _open = false; return; }
            BuildItems();
            _drop.Width = Math.Max(ActualWidth, 140);
            _popup.IsOpen = true;
            _open = true;
        }

        private void BuildItems()
        {
            _items.Children.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                Border row = new Border();
                row.CornerRadius = new CornerRadius(7);
                row.Padding = new Thickness(10, 7, 10, 7);
                row.Margin = new Thickness(0, 1, 0, 1);
                row.Background = i == _selected ? WpfTheme.AccentSoft : Brushes.Transparent;
                TextBlock label = new TextBlock();
                label.Text = Items[i];
                label.FontFamily = WpfTheme.UiFont;
                label.FontSize = 13;
                label.Foreground = i == _selected ? WpfTheme.Accent : WpfTheme.Text;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Child = label;
                row.Cursor = Cursors.Hand;
                row.MouseEnter += delegate { if (index != _selected) row.Background = WpfTheme.NavHover; };
                row.MouseLeave += delegate { if (index != _selected) row.Background = Brushes.Transparent; };
                row.MouseLeftButtonUp += delegate
                {
                    SelectedIndex = index;
                    _popup.IsOpen = false;
                    _open = false;
                };
                _items.Children.Add(row);
            }
        }

        private void ApplyTheme()
        {
            _field.Background = WpfTheme.InputBg;
            _field.BorderBrush = WpfTheme.InputBorder;
            _text.Foreground = WpfTheme.Text;
            _drop.Background = WpfTheme.CardBg;
            _drop.BorderBrush = WpfTheme.CardBorder;
        }

        private void OnThemeChanged()
        {
            ApplyTheme();
        }
    }

    internal sealed class ExpanderCard : ContentControl
    {
        private Border _card;
        private Border _bodyHost;
        private ContentPresenter _presenter;
        private TextBlock _title;
        private Border _header;
        private Border _bar;
        private Path _chevron;
        private bool _expanded = true;
        private bool _animating;
        private bool _hover;

        public bool IsExpanded { get { return _expanded; } }

        public ExpanderCard(string title)
        {
            Focusable = false;
            Template = BuildTemplate();
            Loaded += delegate { _title.Text = Title; if (_expanded) _bodyHost.MaxHeight = double.PositiveInfinity; };
            WpfTheme.Changed += OnThemeChanged;
            Title = title;
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            "Title", typeof(string), typeof(ExpanderCard), new PropertyMetadata(""));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); if (_title != null) _title.Text = value; }
        }

        private static ControlTemplate BuildTemplate()
        {
            FrameworkElementFactory card = new FrameworkElementFactory(typeof(Border));
            card.Name = "Card";
            card.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
            card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            card.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 14));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

            FrameworkElementFactory header = new FrameworkElementFactory(typeof(Border));
            header.Name = "Header";
            header.SetValue(Border.HeightProperty, 48.0);
            header.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            header.SetValue(Border.CursorProperty, Cursors.Hand);
            header.SetValue(Border.ClipToBoundsProperty, true);
            FrameworkElementFactory headerDock = new FrameworkElementFactory(typeof(DockPanel));
            headerDock.SetValue(DockPanel.LastChildFillProperty, true);

            FrameworkElementFactory bar = new FrameworkElementFactory(typeof(Border));
            bar.Name = "Bar";
            bar.SetValue(Border.WidthProperty, 5.0);
            bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            bar.SetValue(Border.MarginProperty, new Thickness(0, 12, 0, 12));
            bar.SetValue(Border.BackgroundProperty, WpfTheme.Accent);
            bar.SetValue(DockPanel.DockProperty, Dock.Left);
            headerDock.AppendChild(bar);

            FrameworkElementFactory chevronHost = new FrameworkElementFactory(typeof(Grid));
            chevronHost.SetValue(FrameworkElement.WidthProperty, 44.0);
            chevronHost.SetValue(DockPanel.DockProperty, Dock.Right);
            FrameworkElementFactory chevron = new FrameworkElementFactory(typeof(Path));
            chevron.Name = "Chevron";
            chevron.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 5 5 L 10 0"));
            chevron.SetValue(Path.StrokeThicknessProperty, 1.6);
            chevron.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            chevron.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            chevron.SetValue(Path.StrokeLineJoinProperty, PenLineJoin.Round);
            chevron.SetValue(Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chevron.SetValue(Path.VerticalAlignmentProperty, VerticalAlignment.Center);
            chevron.SetValue(Path.RenderTransformProperty, new RotateTransform(0, 5, 2.5));
            chevron.SetValue(Path.RenderTransformOriginProperty, new Point(0.5, 0.5));
            chevronHost.AppendChild(chevron);
            headerDock.AppendChild(chevronHost);

            FrameworkElementFactory title = new FrameworkElementFactory(typeof(TextBlock));
            title.Name = "Title";
            title.SetValue(TextBlock.FontFamilyProperty, WpfTheme.UiFont);
            title.SetValue(TextBlock.FontSizeProperty, 14.5);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            title.SetValue(TextBlock.MarginProperty, new Thickness(16, 0, 0, 0));
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            headerDock.AppendChild(title);

            header.AppendChild(headerDock);
            stack.AppendChild(header);

            FrameworkElementFactory bodyHost = new FrameworkElementFactory(typeof(Border));
            bodyHost.Name = "BodyHost";
            bodyHost.SetValue(Border.ClipToBoundsProperty, true);
            bodyHost.SetValue(Border.PaddingProperty, new Thickness(20, 0, 20, 18));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.Name = "Presenter";
            presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            bodyHost.AppendChild(presenter);
            stack.AppendChild(bodyHost);

            card.AppendChild(stack);
            ControlTemplate t = new ControlTemplate(typeof(ExpanderCard));
            t.VisualTree = card;
            return t;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _card = GetTemplateChild("Card") as Border;
            _header = GetTemplateChild("Header") as Border;
            _bodyHost = GetTemplateChild("BodyHost") as Border;
            _presenter = GetTemplateChild("Presenter") as ContentPresenter;
            _title = GetTemplateChild("Title") as TextBlock;
            _bar = GetTemplateChild("Bar") as Border;
            _chevron = GetTemplateChild("Chevron") as Path;
            if (_header != null)
            {
                _header.MouseLeftButtonUp += delegate { Toggle(); };
                _header.MouseEnter += delegate { _hover = true; ApplyTheme(); };
                _header.MouseLeave += delegate { _hover = false; ApplyTheme(); };
            }
            ApplyTheme();
        }

        private void Toggle()
        {
            _expanded = !_expanded;
            Animate();
        }

        private void Animate()
        {
            if (_bodyHost == null) return;
            double from = _expanded ? 0 : _bodyHost.ActualHeight;
            double to;
            if (_expanded)
            {
                _bodyHost.MaxHeight = double.PositiveInfinity;
                _bodyHost.Measure(new Size(Math.Max(0, ActualWidth - 40), double.PositiveInfinity));
                to = _bodyHost.DesiredSize.Height;
            }
            else
            {
                to = 0;
            }
            DoubleAnimation heightAnim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(230)));
            heightAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            heightAnim.Completed += delegate
            {
                _animating = false;
                _bodyHost.MaxHeight = _expanded ? double.PositiveInfinity : 0;
            };
            _animating = true;
            _bodyHost.BeginAnimation(Border.MaxHeightProperty, null);
            _bodyHost.MaxHeight = double.PositiveInfinity;
            _bodyHost.BeginAnimation(Border.MaxHeightProperty, heightAnim);
            if (_chevron != null)
            {
                RotateTransform rot = _chevron.RenderTransform as RotateTransform;
                if (rot == null) { rot = new RotateTransform(0, 5, 2.5); _chevron.RenderTransform = rot; }
                DoubleAnimation angle = new DoubleAnimation(_expanded ? 0 : -90, new Duration(TimeSpan.FromMilliseconds(230)));
                angle.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                rot.BeginAnimation(RotateTransform.AngleProperty, angle);
            }
        }

        private void ApplyTheme()
        {
            if (_card != null)
            {
                _card.Background = WpfTheme.CardBg;
                _card.BorderBrush = _hover ? WpfTheme.Accent : WpfTheme.CardBorder;
            }
            if (_title != null) _title.Foreground = WpfTheme.Text;
            if (_bar != null) _bar.Background = WpfTheme.Accent;
            if (_chevron != null) _chevron.Stroke = _hover ? WpfTheme.Accent : WpfTheme.SubText;
        }

        private void OnThemeChanged()
        {
            ApplyTheme();
        }
    }
}
