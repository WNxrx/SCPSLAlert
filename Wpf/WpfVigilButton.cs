using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SCPSLAlert
{
    internal enum VigilButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    /// <summary>
    /// 统一的按钮配色。窗口每次重建界面都会按当前主题重新取一份，
    /// 所以这里只存 Color，不缓存 Brush。
    /// </summary>
    internal struct VigilButtonPalette
    {
        public Color Accent;
        public Color Panel;
        public Color PanelAlt;
        public Color Line;
        public Color Ink;
        public Color Sub;
        public Color Muted;
        public Color Danger;
        public bool Light;

        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        public static Color Fade(Color c, byte alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        public static Color Lighten(Color c, double t)
        {
            return Mix(c, Colors.White, t);
        }

        public static Color Darken(Color c, double t)
        {
            return Mix(c, Colors.Black, t);
        }
    }

    /// <summary>
    /// A.R.C.T 主界面的按钮：比原来的“无边框小字”更大、对比更强，
    /// 悬停会亮边框、按下有缩放和底色反馈，键盘聚焦时有一圈强调色光。
    /// 仍然是 Button，所以原有的 Click / IsEnabled / Content 逻辑不受影响。
    /// </summary>
    internal sealed class VigilButton : Button
    {
        private readonly VigilButtonPalette _p;
        private readonly VigilButtonKind _kind;
        private readonly SolidColorBrush _bgBrush = new SolidColorBrush();
        private readonly SolidColorBrush _bdBrush = new SolidColorBrush();
        private readonly SolidColorBrush _fgBrush = new SolidColorBrush();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);

        private Color _bg, _bgHover, _bgPress;
        private Color _bd, _bdHover, _bdPress;
        private Color _fg, _fgHover, _fgPress;
        private bool _hover;

        public VigilButton(UIElement content, VigilButtonKind kind, VigilButtonPalette p, double height, double padX)
        {
            _p = p;
            _kind = kind;
            Content = content;
            Height = height;
            MinHeight = height;
            Padding = new Thickness(padX, 0, padX, 0);
            FontFamily = UiFonts.Body;
            FontSize = 11;
            FontWeight = kind == VigilButtonKind.Primary ? FontWeights.SemiBold : FontWeights.Normal;
            Background = _bgBrush;
            BorderBrush = _bdBrush;
            Foreground = _fgBrush;
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            RenderTransform = _scale;
            RenderTransformOrigin = new Point(0.5, 0.5);
            Template = BuildTemplate();

            ComputeColors();
            Apply(_bg, _bd, _fg, 0);
            IsEnabledChanged += delegate
            {
                Opacity = IsEnabled ? 1 : 0.42;
                Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
            };
        }

        private static ControlTemplate BuildTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            ControlTemplate t = new ControlTemplate(typeof(VigilButton));
            t.VisualTree = border;
            return t;
        }

        private void ComputeColors()
        {
            Color accent = _p.Accent;
            // 按钮下面的卡片/背景偏暗 → 浅灰底 + 深色字；偏亮 → 深灰底 + 浅色字。
            // 不直接上纯白/纯黑，避免和周围的玻璃卡片对比太硬。
            bool lightFace = WpfCardGlass.SurfaceLuma(_p.Panel) < 0.5;
            Color face = lightFace
                ? Color.FromArgb(235, 222, 231, 237)
                : Color.FromArgb(235, 45, 57, 66);
            Color ink = lightFace
                ? Color.FromRgb(12, 21, 28)
                : Color.FromRgb(238, 244, 248);
            Color neutralBorder = lightFace
                ? Color.FromRgb(126, 145, 158)
                : Color.FromRgb(142, 160, 172);

            switch (_kind)
            {
                case VigilButtonKind.Primary:
                    _bg = face;
                    _bgHover = VigilButtonPalette.Mix(face, accent, lightFace ? 0.10 : 0.20);
                    _bgPress = VigilButtonPalette.Mix(face, accent, lightFace ? 0.18 : 0.32);
                    _bd = VigilButtonPalette.Fade(accent, 200);
                    _bdHover = accent;
                    _bdPress = accent;
                    _fg = ink;
                    _fgHover = ink;
                    _fgPress = ink;
                    break;
                case VigilButtonKind.Danger:
                    _bg = face;
                    _bgHover = VigilButtonPalette.Mix(face, _p.Danger, lightFace ? 0.08 : 0.18);
                    _bgPress = VigilButtonPalette.Mix(face, _p.Danger, lightFace ? 0.16 : 0.28);
                    _bd = VigilButtonPalette.Fade(_p.Danger, 190);
                    _bdHover = _p.Danger;
                    _bdPress = _p.Danger;
                    _fg = _p.Danger;
                    _fgHover = _p.Danger;
                    _fgPress = _p.Danger;
                    break;
                case VigilButtonKind.Ghost:
                    _bg = face;
                    _bgHover = VigilButtonPalette.Mix(face, accent, lightFace ? 0.07 : 0.15);
                    _bgPress = VigilButtonPalette.Mix(face, accent, lightFace ? 0.13 : 0.24);
                    _bd = VigilButtonPalette.Fade(neutralBorder, (byte)(lightFace ? 190 : 170));
                    _bdHover = VigilButtonPalette.Fade(accent, 160);
                    _bdPress = accent;
                    _fg = ink;
                    _fgHover = ink;
                    _fgPress = ink;
                    break;
                default:
                    _bg = face;
                    _bgHover = VigilButtonPalette.Mix(face, accent, lightFace ? 0.09 : 0.17);
                    _bgPress = VigilButtonPalette.Mix(face, accent, lightFace ? 0.16 : 0.27);
                    _bd = VigilButtonPalette.Fade(neutralBorder, 190);
                    _bdHover = VigilButtonPalette.Fade(accent, 190);
                    _bdPress = accent;
                    _fg = ink;
                    _fgHover = ink;
                    _fgPress = ink;
                    break;
            }
        }

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            if (!IsPressed) Apply(_bgHover, _bdHover, _fgHover, 120);
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            if (!IsPressed) Apply(_bg, _bd, _fg, 160);
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (IsPressed)
            {
                Apply(_bgPress, _bdPress, _fgPress, 60);
                _scale.ScaleX = 0.965;
                _scale.ScaleY = 0.965;
            }
            else
            {
                Apply(_hover ? _bgHover : _bg, _hover ? _bdHover : _bd, _hover ? _fgHover : _fg, 130);
                _scale.ScaleX = 1;
                _scale.ScaleY = 1;
            }
        }

        protected override void OnGotKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            DropShadowEffect glow = new DropShadowEffect();
            glow.Color = _p.Accent;
            glow.BlurRadius = 12;
            glow.ShadowDepth = 0;
            glow.Opacity = 0.55;
            Effect = glow;
        }

        protected override void OnLostKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            Effect = null;
        }

        private void Apply(Color bg, Color bd, Color fg, int ms)
        {
            if (ms <= 0)
            {
                _bgBrush.Color = bg;
                _bdBrush.Color = bd;
                _fgBrush.Color = fg;
                return;
            }
            AnimateBrush(_bgBrush, bg, ms);
            AnimateBrush(_bdBrush, bd, ms);
            AnimateBrush(_fgBrush, fg, ms);
        }

        private static void AnimateBrush(SolidColorBrush brush, Color target, int ms)
        {
            ColorAnimation a = new ColorAnimation(target, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            a.FillBehavior = FillBehavior.HoldEnd;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }
    }

    /// <summary>
    /// 给用 Border 拼出来的可点击卡片（工具磁贴、心脏按钮、提示条等）
    /// 补上“按下”反馈：缩放 + 底色/边框过渡。
    /// </summary>
    internal sealed class VigilPressState
    {
        private readonly Border _frame;
        private readonly SolidColorBrush _bg = new SolidColorBrush();
        private readonly SolidColorBrush _bd = new SolidColorBrush();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly Color _bgBase, _bgHover, _bgPress;
        private readonly Color _bdBase, _bdHover, _bdPress;
        private bool _hover, _down;

        public VigilPressState(Border frame,
            Color bgBase, Color bgHover, Color bgPress,
            Color bdBase, Color bdHover, Color bdPress)
        {
            _frame = frame;
            _bgBase = bgBase;
            _bgHover = bgHover;
            _bgPress = bgPress;
            _bdBase = bdBase;
            _bdHover = bdHover;
            _bdPress = bdPress;
            _bg.Color = bgBase;
            _bd.Color = bdBase;
            frame.Background = _bg;
            frame.BorderBrush = _bd;
            frame.RenderTransform = _scale;
            frame.RenderTransformOrigin = new Point(0.5, 0.5);
            frame.MouseEnter += delegate { _hover = true; Refresh(); };
            frame.MouseLeave += delegate { _hover = false; _down = false; Refresh(); };
            frame.MouseLeftButtonDown += delegate { _down = true; Refresh(); };
            frame.MouseLeftButtonUp += delegate { _down = false; Refresh(); };
            frame.LostMouseCapture += delegate { _down = false; Refresh(); };
            frame.IsEnabledChanged += delegate { frame.Opacity = frame.IsEnabled ? 1 : 0.42; };
        }

        private void Refresh()
        {
            bool press = _down && _hover;
            Animate(_bg, press ? _bgPress : (_hover ? _bgHover : _bgBase), 100);
            Animate(_bd, press ? _bdPress : (_hover ? _bdHover : _bdBase), 100);
            _scale.ScaleX = press ? 0.975 : 1;
            _scale.ScaleY = press ? 0.975 : 1;
        }

        private static void Animate(SolidColorBrush brush, Color target, int ms)
        {
            ColorAnimation a = new ColorAnimation(target, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            a.FillBehavior = FillBehavior.HoldEnd;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }
    }
}
