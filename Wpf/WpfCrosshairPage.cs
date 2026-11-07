using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 「准星」页 + 二级编辑界面（准星工作台）。
    ///
    /// 结构：
    ///   · 准星页（一级）只放概览：当前准星预览、关键参数、以及「编辑准星…」按钮；
    ///   · 点编辑 → 打开工作台（二级），覆盖在内容区之上，左上角「← 返回」，
    ///     左边全部滑杆，右边实时预览，不再往「高级」页里堆。
    /// 本轮是原型：新参数留在内存，持久化与覆盖层实装下一步接。
    /// </summary>
    internal sealed partial class WpfVigilDataWindow
    {
        private Grid _chPageHost;
        private CrosshairModel _ch;
        private CrosshairPreview _chPrev;
        private CrosshairPreview _chPrevMini;
        private TextBlock _chFpsText;
        private TextBlock _chStateText;
        private TextBlock _chSummaryText;
        private Grid _chBenchPage;
        private bool _chBenchOpen;

        private VigilChips _cType, _cCurve, _cBg;
        private VigilSwatches _wMain, _wOutline;
        private WpfSlider _sLen, _sGap, _sThick, _sDot, _sRing, _sRot;
        private WpfSlider _sOuterLen, _sOuterThick, _sOuterGap;
        private WpfSlider _sOpacity, _sR, _sG, _sB;
        private WpfSlider _sOutOpacity, _sOutWidth;
        private WpfSlider _sFireAmt, _sFireDur, _sFireMax;
        private WpfSlider _sRingDur, _sRingRad, _sExpDur, _sExpAmt;
        private WpfSlider _sX, _sY;
        private WpfSlider _sZoom;
        private VigilToggle _tT, _tOuter, _tOutline, _tDyn, _tFire, _tHit, _tDpi, _tSnap, _tGrid, _tLimit;
        private DispatcherTimer _chFpsTimer;

        private static readonly Color[] ChPalette = new Color[]
        {
            Color.FromArgb(255, 0, 255, 128), Color.FromArgb(255, 0, 229, 255), Color.FromArgb(255, 255, 235, 59),
            Color.FromArgb(255, 255, 45, 149), Colors.White, Color.FromArgb(255, 255, 59, 48),
            Color.FromArgb(255, 179, 136, 255), Color.FromArgb(255, 255, 159, 10)
        };

        private static readonly Color[] ChOutlinePalette = new Color[]
        {
            Colors.Black, Colors.White, Color.FromArgb(255, 18, 26, 32),
            Color.FromArgb(255, 58, 18, 18), Color.FromArgb(255, 110, 110, 110)
        };

        // ================= 注册 =================
        // 一级入口仍然放在「高级」页里的准星卡上：那里有实时预览、总开关和「编辑准星」按钮。
        // 这里只负责准备数据模型和二级工作台的宿主。
        private void CreateCrosshairPage(Grid body)
        {
            _chPageHost = body;

            _ch = new CrosshairModel();
            _ch.Type = Math.Max(0, Math.Min(6, _cfg.CrosshairType));
            _ch.Length = _cfg.CrosshairLength;
            _ch.Gap = _cfg.CrosshairLineGap;
            _ch.Thickness = _cfg.CrosshairLineThickness;
            _ch.Opacity = _cfg.CrosshairOpacity;
            _ch.DotSize = _cfg.CrosshairDotSize;
            _ch.Rotation = _cfg.CrosshairRotation;
            _ch.RingRadius = _cfg.CrosshairRingRadiusF;
            _ch.RingEase = _cfg.CrosshairRingEase;
            _ch.TShape = _cfg.CrosshairTShape;
            _ch.OuterEnabled = _cfg.CrosshairOuterEnabled;
            _ch.OuterLength = _cfg.CrosshairOuterLength;
            _ch.OuterThickness = _cfg.CrosshairOuterThickness;
            _ch.OuterGap = _cfg.CrosshairOuterGap;
            _ch.OutlineEnabled = _cfg.CrosshairOutline;
            _ch.OutlineOpacity = _cfg.CrosshairOutlineOpacity;
            _ch.OutlineWidth = _cfg.CrosshairOutlineWidth;
            _ch.DynamicEnabled = _cfg.CrosshairDynamic;
            _ch.FireEnabled = _cfg.CrosshairFireSpread;
            _ch.FireAmount = _cfg.CrosshairFireAmount;
            _ch.FireDuration = _cfg.CrosshairFireDurationMs;
            _ch.FireCurve = _cfg.CrosshairFireCurve;
            _ch.FireMaxMul = _cfg.CrosshairFireMaxMul;
            _ch.HitFlash = _cfg.CrosshairHitFlash;
            _ch.DpiScale = _cfg.CrosshairDpiScale;
            _ch.SnapCenter = _cfg.CrosshairSnapCenter;
            _ch.RingDuration = _cfg.CrosshairRingDurationMs;
            _ch.RingRadiusMax = _cfg.CrosshairRingRadius;
            _ch.ExpandDuration = _cfg.CrosshairExpandDurationMs;
            _ch.ExpandAmount = _cfg.CrosshairExpandAmount;
            _ch.RingKey = KeyName(_cfg.CrosshairRingKey);
            _ch.ExpandKey = KeyName(_cfg.CrosshairExpandKey);
            _ch.OffsetX = _cfg.CrosshairOffsetX;
            _ch.OffsetY = _cfg.CrosshairOffsetY;
            System.Drawing.Color m = System.Drawing.Color.FromArgb(_cfg.CrosshairColor);
            _ch.Main = Color.FromArgb(255, m.R, m.G, m.B);
            System.Drawing.Color o = System.Drawing.Color.FromArgb(_cfg.CrosshairOutlineColor);
            _ch.OutlineColor = Color.FromArgb(255, o.R, o.G, o.B);

            _chFpsTimer = new DispatcherTimer();
            _chFpsTimer.Interval = TimeSpan.FromMilliseconds(400);
            _chFpsTimer.Tick += delegate { RefreshCrosshairLiveText(); };
            _chFpsTimer.Start();
        }

        private static string KeyName(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return "";
            string s = spec.Trim();
            int plus = s.LastIndexOf('+');
            return plus >= 0 ? s.Substring(plus + 1) : s;
        }

        private VigilChips _cRingEase;
        private TextBlock _ringKeyText, _expandKeyText;
        private Border _ringKeyFrame, _expandKeyFrame;
        private TextBlock _chKeyStatus;
        private string _chCaptureTarget;

        private VigilChips _cFps;
        private string _chPrevLabel = "跟随刷新";

        private string _chCfgSignature = "";

        private void RefreshCrosshairLiveText()
        {
            if (_chFpsText != null && _chPrev != null)
                _chFpsText.Text = (_chPrev.MeasuredFps <= 0 ? "--" : _chPrev.MeasuredFps.ToString("F0")) + " FPS / 目标 " + _chPrevLabel;
            if (_chSummaryText != null && _ch != null)
                _chSummaryText.Text = CrosshairSummaryLine();

            // 改动落盘：模型签名变了就写回 Config 并触发防抖保存
            if (_ch == null) return;
            string sig = ModelSignature();
            if (sig == _chCfgSignature) return;
            _chCfgSignature = sig;
            ApplyCrosshairModelToConfig();
            TouchSave();
        }

        private string ModelSignature()
        {
            return string.Concat(_ch.Type, ",", _ch.TShape, ",", _ch.Length.ToString("F1"), ",",
                _ch.Gap.ToString("F1"), ",", _ch.Thickness.ToString("F1"), ",", _ch.DotSize.ToString("F1"), ",",
                _ch.Rotation.ToString("F0"), ",", _ch.RingRadius.ToString("F0"), ",", _ch.RingEase, ",",
                _ch.OuterEnabled, ",", _ch.OuterLength.ToString("F1"), ",", _ch.OuterThickness.ToString("F1"), ",",
                _ch.OuterGap.ToString("F1"), ",", _ch.Opacity, ",", System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb(), ",",
                _ch.OutlineEnabled, ",", _ch.OutlineOpacity, ",", _ch.OutlineWidth.ToString("F2"), ",",
                System.Drawing.Color.FromArgb(255, _ch.OutlineColor.R, _ch.OutlineColor.G, _ch.OutlineColor.B).ToArgb(), ",", _ch.DynamicEnabled, ",", _ch.FireEnabled, ",",
                _ch.FireAmount.ToString("F1"), ",", _ch.FireDuration, ",", _ch.FireCurve, ",",
                _ch.FireMaxMul.ToString("F1"), ",", _ch.HitFlash, ",", _ch.DpiScale, ",", _ch.SnapCenter, ",",
                _ch.OffsetX.ToString("F0"), ",", _ch.OffsetY.ToString("F0"));
        }

        /// <summary>把工作台模型写回 Config（含旧字段镜像），老界面读到的值也一致。</summary>
        private void ApplyCrosshairModelToConfig()
        {
            _cfg.CrosshairType = _ch.Type;
            _cfg.CrosshairTShape = _ch.TShape;
            _cfg.CrosshairLength = (float)_ch.Length;
            _cfg.CrosshairLineGap = (float)_ch.Gap;
            _cfg.CrosshairLineThickness = (float)_ch.Thickness;
            _cfg.CrosshairDotSize = (float)_ch.DotSize;
            _cfg.CrosshairRotation = (float)_ch.Rotation;
            _cfg.CrosshairRingRadiusF = (float)_ch.RingRadius;
            _cfg.CrosshairRingEase = _ch.RingEase;
            _cfg.CrosshairOuterEnabled = _ch.OuterEnabled;
            _cfg.CrosshairOuterLength = (float)_ch.OuterLength;
            _cfg.CrosshairOuterThickness = (float)_ch.OuterThickness;
            _cfg.CrosshairOuterGap = (float)_ch.OuterGap;
            _cfg.CrosshairOpacity = _ch.Opacity;
            _cfg.CrosshairColor = System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb();
            _cfg.CrosshairOutline = _ch.OutlineEnabled;
            _cfg.CrosshairOutlineOpacity = _ch.OutlineOpacity;
            _cfg.CrosshairOutlineWidth = (float)_ch.OutlineWidth;
            _cfg.CrosshairOutlineColor = System.Drawing.Color.FromArgb(255, _ch.OutlineColor.R, _ch.OutlineColor.G, _ch.OutlineColor.B).ToArgb();
            _cfg.CrosshairDynamic = _ch.DynamicEnabled;
            _cfg.CrosshairFireSpread = _ch.FireEnabled;
            _cfg.CrosshairFireAmount = (float)_ch.FireAmount;
            _cfg.CrosshairFireDurationMs = _ch.FireDuration;
            _cfg.CrosshairFireCurve = _ch.FireCurve;
            _cfg.CrosshairFireMaxMul = (float)_ch.FireMaxMul;
            _cfg.CrosshairHitFlash = _ch.HitFlash;
            _cfg.CrosshairDpiScale = _ch.DpiScale;
            _cfg.CrosshairSnapCenter = _ch.SnapCenter;
            _cfg.CrosshairOffsetX = (int)Math.Round(_ch.OffsetX);
            _cfg.CrosshairOffsetY = (int)Math.Round(_ch.OffsetY);
            _cfg.SyncCrosshairLegacyMirrors();
        }

        private string CrosshairSummaryLine()
        {
            string type = _ch.Type >= 0 && _ch.Type < CrosshairModel.TypeNames.Length
                ? CrosshairModel.TypeNames[_ch.Type] : "?";
            return type + " · 长 " + _ch.Length.ToString("F0") + " · 隙 " + _ch.Gap.ToString("F0")
                + " · 粗 " + _ch.Thickness.ToString("F1") + " · α " + _ch.Opacity + "%"
                + (_ch.DynamicEnabled && _ch.FireEnabled ? " · 扩散 +" + _ch.FireAmount.ToString("F0") : " · 静态");
        }

        // ================= 高级页里的准星卡（一级入口） =================

        /// <summary>高级页卡片里的小预览：和二级工作台共用同一份模型。</summary>
        private UIElement CrosshairLiveThumb(double height)
        {
            // 【不要复用】WPF 一个 UIElement 只能有一个逻辑父级。二级页每次重建都会
            // 新建一个 Border 宿主；如果继续把字段里的旧 CrosshairPreview 塞进去，
            // 会抛“指定的元素已经是另一个元素的逻辑子元素”（2026-11-07 实机崩溃）。
            // 预览本身没有需要跨页保留的状态，模型在 _ch 上，所以每次新建最稳妥。
            DetachFromParent(_chPrevMini);
            _chPrevMini = new CrosshairPreview();
            _chPrevMini.Model = _ch;
            _chPrevMini.BackgroundMode = 0;
            _chPrevMini.Zoom = 1.05;
            _chPrevMini.Height = height;
            Border host = new Border();
            host.CornerRadius = new CornerRadius(4);
            host.ClipToBounds = true;
            host.BorderBrush = Line;
            host.BorderThickness = new Thickness(1);
            host.Background = PanelAlt;
            host.Margin = new Thickness(0, 6, 0, 8);
            host.Child = _chPrevMini;
            return host;
        }

        /// <summary>软件里的按钮风格：描边 + 图标 + 文字，悬停会亮底色。</summary>
        private Button OutlineButton(string glyph, string text, Action click, Brush tone)
        {
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = Icons;
            icon.FontSize = 11.5;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 7, 0);
            content.Children.Add(icon);
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontFamily = UiFonts.Body;
            label.FontSize = 11;
            label.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(label);

            VigilButtonKind kind = ReferenceEquals(tone, Danger) ? VigilButtonKind.Danger
                : ReferenceEquals(tone, Muted) ? VigilButtonKind.Ghost
                : ReferenceEquals(tone, Sub) ? VigilButtonKind.Secondary
                : VigilButtonKind.Primary;
            VigilButton b = new VigilButton(content, kind, ButtonPalette(tone), 30, 12);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.ToolTip = text;
            b.Click += delegate { if (click != null) click(); };
            return b;
        }

        /// <summary>共享的准星状态摘要行，卡片和工作台都用它。</summary>
        private TextBlock CrosshairSummaryText()
        {
            _chSummaryText = MonoText(CrosshairSummaryLine(), 9, Accent2);
            _chSummaryText.Margin = new Thickness(0, 0, 0, 6);
            return _chSummaryText;
        }
        private void InvalidateBoth()
        {
            if (_chPrev != null) _chPrev.InvalidateVisual();
            if (_chPrevMini != null) _chPrevMini.InvalidateVisual();
        }

        /// <summary>卡片与工作台共用：刷新预览与摘要文字。</summary>
        private void CrosshairInvalidate()
        {
            InvalidateBoth();
            RefreshCrosshairLiveText();
        }

        // ================= 二级界面：工作台 =================
        // 参照「总览 → 性能」的做法：左侧导航保持原位，只在内容区切换一页，
        // 页面左上角是圆角返回按钮，右上角显示 LIVE / 状态。
        private void OpenCrosshairWorkbench()
        {
            if (_chBenchPage == null) BuildCrosshairWorkbench();
            if (_chBenchPage == null) return;

            _chBenchOpen = true;
            HideAllForBench();
            // 进入二级工作台时撤销“跳转后 5 秒锁定”，否则描边会停在
            // 已经隐藏的高级页卡片位置上。
            ClearHover();
            _chBenchPage.Visibility = Visibility.Visible;
            _currentPage = 8;
            UpdateRailSelection(8);
            if (_chFpsTimer != null && !_chFpsTimer.IsEnabled) _chFpsTimer.Start();
            RefreshCrosshairLiveText();

            _chBenchPage.Opacity = 0;
            TranslateTransform slide = new TranslateTransform(0, 10);
            _chBenchPage.RenderTransform = slide;
            DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)));
            DoubleAnimation move = new DoubleAnimation(10, 0, new Duration(TimeSpan.FromMilliseconds(170)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _chBenchPage.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.YProperty, move);
            if (_chPrev != null) _chPrev.Focus();
        }

        private void CloseCrosshairWorkbench()
        {
            if (_chBenchPage == null) return;
            _chBenchOpen = false;
            _chBenchPage.Visibility = Visibility.Collapsed;
            HideAllForBench();
            ClearHover();
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Visible;
            _currentPage = 8;
            UpdateRailSelection(8);
            RefreshCrosshairLiveText();
            InvalidateBoth();
        }

        private void HideAllForBench()
        {
            _statusPage.Visibility = Visibility.Collapsed;
            if (_perfPage != null) _perfPage.Visibility = Visibility.Collapsed;
            _generalPage.Visibility = Visibility.Collapsed;
            _alertPage.Visibility = Visibility.Collapsed;
            _audioPage.Visibility = Visibility.Collapsed;
            _timePage.Visibility = Visibility.Collapsed;
            _logPage.Visibility = Visibility.Collapsed;
            _hotkeyPage.Visibility = Visibility.Collapsed;
            _toolsPage.Visibility = Visibility.Collapsed;
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Collapsed;
            if (_advancedPage != null) _advancedPage.Visibility = Visibility.Collapsed;
        }

        private void BuildCrosshairWorkbench()
        {
            if (_chPageHost == null) return;

            Grid page = new Grid();
            page.Margin = new Thickness(28, 18, 28, 18);
            page.Visibility = Visibility.Collapsed;
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(page, 1);
            page.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (HandleKeyCapture(e)) return;
                if (e.Key == Key.Escape) { CloseCrosshairWorkbench(); e.Handled = true; return; }
                if (_ch == null || _chPrev == null) return;
                // 在页面级拦截：否则焦点在按钮上时，空格会被 WPF 当成“点击按钮”吃掉
                if (string.Equals(e.Key.ToString(), _ch.RingKey, StringComparison.OrdinalIgnoreCase))
                {
                    _chPrev.TriggerRing();
                    e.Handled = true;
                    return;
                }
                if (string.Equals(e.Key.ToString(), _ch.ExpandKey, StringComparison.OrdinalIgnoreCase))
                {
                    _chPrev.TriggerExpand();
                    e.Handled = true;
                    return;
                }
            };

            page.Children.Add(CrosshairWorkbenchHeader());

            Grid main = new Grid();
            main.Margin = new Thickness(0, 4, 0, 0);
            main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(392) });
            Grid.SetRow(main, 1);
            page.Children.Add(main);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Padding = new Thickness(0, 0, 12, 0);
            StackPanel left = new StackPanel();
            left.Children.Add(BuildCrosshairShapeCard());
            left.Children.Add(BuildCrosshairColorCard());
            left.Children.Add(BuildCrosshairOutlineCard());
            left.Children.Add(BuildCrosshairFeedbackCard());
            left.Children.Add(BuildCrosshairKeysCard());
            left.Children.Add(BuildCrosshairLinesCard());
            left.Children.Add(BuildCrosshairPositionCard());
            scroll.Content = left;
            HookPageScroll(scroll);
            Grid.SetColumn(scroll, 0);
            main.Children.Add(scroll);

            StackPanel right = new StackPanel();
            right.Children.Add(BuildCrosshairPreviewCard());
            right.Children.Add(BuildCrosshairPresetCard());
            Grid.SetColumn(right, 2);
            main.Children.Add(right);

            _chBenchPage = page;
            _chPageHost.Children.Add(page);
        }

        /// <summary>工作台页头：返回按钮 + 大编号 + 标题，布局对齐「性能」二级页。</summary>
        private UIElement CrosshairWorkbenchHeader()
        {
            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 0, 12);
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border back = new Border();
            back.Width = 34;
            back.Height = 34;
            back.CornerRadius = new CornerRadius(4);
            back.BorderBrush = Line;
            back.BorderThickness = new Thickness(1);
            back.Background = Panel;
            back.Cursor = Cursors.Hand;
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.VerticalAlignment = VerticalAlignment.Top;
            back.Margin = new Thickness(0, 8, 0, 0);
            Button backButton = new Button();
            backButton.Background = Brushes.Transparent;
            backButton.BorderThickness = new Thickness(0);
            backButton.FocusVisualStyle = null;
            backButton.Cursor = Cursors.Hand;
            backButton.Template = FlatTemplate();
            TextBlock backIcon = new TextBlock();
            backIcon.Text = "\uE72B";
            backIcon.FontFamily = Icons;
            backIcon.FontSize = 15;
            backIcon.Foreground = Sub;
            backIcon.HorizontalAlignment = HorizontalAlignment.Center;
            backIcon.VerticalAlignment = VerticalAlignment.Center;
            backButton.Content = backIcon;
            backButton.MouseEnter += delegate { back.BorderBrush = Accent; backIcon.Foreground = Accent; back.Background = PanelAlt; };
            backButton.MouseLeave += delegate { back.BorderBrush = Line; backIcon.Foreground = Sub; back.Background = Panel; };
            backButton.Click += delegate { CloseCrosshairWorkbench(); };
            back.Child = backButton;
            head.Children.Add(back);

            OutlineText code = new OutlineText("08", 82, Accent, 1.1);
            code.HorizontalAlignment = HorizontalAlignment.Left;
            code.VerticalAlignment = VerticalAlignment.Top;
            code.Margin = new Thickness(0, -18, 0, 0);
            Grid.SetColumn(code, 1);
            head.Children.Add(code);

            StackPanel title = new StackPanel();
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 2, 0, 0);
            StackPanel kicker = new StackPanel();
            kicker.Orientation = Orientation.Horizontal;
            kicker.Children.Add(MonoText("CROSSHAIR WORKBENCH  /  SHAPE · COLOR · FEEDBACK · MOTION", 9, Sub));
            TextBlock backHint = MonoText("   ← 返回准星", 8.5, Muted);
            backHint.VerticalAlignment = VerticalAlignment.Center;
            kicker.Children.Add(backHint);
            title.Children.Add(kicker);
            TextBlock big = DisplayText("准星工作台", 40, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, -4, 0, 0);
            title.Children.Add(big);
            TextBlock desc = new TextBlock();
            desc.Text = "左侧全部滑杆，右侧实时预览 · 开火扩散、按键回弹与描边都能当场看效果";
            desc.FontSize = 11;
            desc.Foreground = Sub;
            title.Children.Add(desc);
            Grid.SetColumn(title, 2);
            head.Children.Add(title);

            StackPanel right = new StackPanel();
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock live = MonoText("LIVE  /  跟随刷新", 8.5, Muted);
            live.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(live);
            TextBlock state = MonoText("WORKBENCH", 19, Accent);
            state.FontWeight = FontWeights.Bold;
            state.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(state);
            TextBlock hint = MonoText("Esc 或返回图标退出", 8, Sub);
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(hint);
            Grid.SetColumn(right, 3);
            head.Children.Add(right);

            Border line = new Border();
            line.Height = 1;
            line.Background = Line;
            line.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumnSpan(line, 4);
            head.Children.Add(line);
            return head;
        }


        // ---------------- 工作台里的卡片 ----------------
        private WpfSlider ChSlider(double min, double max, double value, double step, int decimals,
                                   string unit, Action<double> apply)
        {
            WpfSlider s = new WpfSlider(Accent, Line, KnobBrush(), Ink, Sub, 208);
            s.Minimum = min;
            s.Maximum = max;
            s.Step = step;
            s.Decimals = decimals;
            s.Unit = unit;
            s.SetValueSilent(value);
            s.ValueChanged += delegate
            {
                apply(s.Value);
                InvalidateBoth();
                RefreshCrosshairLiveText();
                TouchSave();
            };
            return s;
        }

        private UIElement BuildCrosshairShapeCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("SHAPE  /  形态", "GEOMETRY", out body);

            _cType = new VigilChips(Accent, Line, Ink, Sub, Panel);
            _cType.SetItems(CrosshairModel.TypeNames, _ch.Type, delegate(int i)
            {
                _ch.Type = i;
                _cfg.CrosshairShape = Math.Max(0, Math.Min(11, i));
                InvalidateBoth();
                RefreshCrosshairLiveText();
                TouchSave();
            });
            body.Children.Add(GeneralRow("类型", "基础几何；内线+外线可叠两组线", _cType));

            _sLen = ChSlider(0, 80, _ch.Length, 0.5, 1, "", delegate(double v) { _ch.Length = v; _cfg.CrosshairSize = (int)Math.Round(v); });
            body.Children.Add(GeneralRow("长度", "每一根线的像素长度", _sLen));

            _sGap = ChSlider(-12, 60, _ch.Gap, 0.5, 1, "", delegate(double v) { _ch.Gap = v; _cfg.CrosshairGap = (int)Math.Round(Math.Max(0, v)); });
            body.Children.Add(GeneralRow("间隙", "允许负数：负值会把线拉向中心", _sGap));

            _sThick = ChSlider(0.5, 12, _ch.Thickness, 0.5, 1, "", delegate(double v) { _ch.Thickness = v; _cfg.CrosshairThickness = (int)Math.Round(v); });
            body.Children.Add(GeneralRow("粗细", "支持 0.5 步进", _sThick));

            _sDot = ChSlider(0, 16, _ch.DotSize, 0.5, 1, "", delegate(double v) { _ch.DotSize = v; _cfg.CrosshairDot = v > 0.01; });
            body.Children.Add(GeneralRow("中心点", "0 = 不显示；可与任何形态叠加", _sDot));

            _sRing = ChSlider(6, 90, _ch.RingRadius, 1, 0, "", delegate(double v) { _ch.RingRadius = v; });
            body.Children.Add(GeneralRow("圆环半径", "「圆环」类型使用", _sRing));

            _sRot = ChSlider(0, 90, _ch.Rotation, 1, 0, "°", delegate(double v) { _ch.Rotation = v; });
            body.Children.Add(GeneralRow("旋转", "整体旋转角度", _sRot));

            _tT = AdvToggle(_ch.TShape, delegate(bool v) { _ch.TShape = v; InvalidateBoth(); });
            body.Children.Add(GeneralRow("T 形", "去掉上方那一根线", _tT));
            return card;
        }

        private UIElement BuildCrosshairLinesCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("LINES  /  双线组", "INNER + OUTER", out body);

            _tOuter = AdvToggle(_ch.OuterEnabled, delegate(bool v) { _ch.OuterEnabled = v; InvalidateBoth(); });
            body.Children.Add(GeneralRow("启用外线组", "同 Valorant 的内外双线", _tOuter));

            _sOuterLen = ChSlider(0, 40, _ch.OuterLength, 0.5, 1, "", delegate(double v) { _ch.OuterLength = v; });
            body.Children.Add(GeneralRow("外线长度", "外圈那一组线的长度", _sOuterLen));

            _sOuterThick = ChSlider(0.5, 10, _ch.OuterThickness, 0.5, 1, "", delegate(double v) { _ch.OuterThickness = v; });
            body.Children.Add(GeneralRow("外线粗细", "可以和内线不同", _sOuterThick));

            _sOuterGap = ChSlider(0, 40, _ch.OuterGap, 0.5, 1, "", delegate(double v) { _ch.OuterGap = v; });
            body.Children.Add(GeneralRow("内外间距", "内外两组线之间的空隙", _sOuterGap));

            TextBlock note = new TextBlock();
            note.Text = "只在「内线+外线」类型下生效。";
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private UIElement BuildCrosshairColorCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("COLOR  /  颜色", "RGB", out body);

            _wMain = new VigilSwatches(Accent, Line);
            _wMain.SetItems(ChPalette, 0, delegate(int i)
            {
                _ch.Main = ChPalette[i];
                if (_sR != null) _sR.SetValueSilent(_ch.Main.R);
                if (_sG != null) _sG.SetValueSilent(_ch.Main.G);
                if (_sB != null) _sB.SetValueSilent(_ch.Main.B);
                _cfg.CrosshairColor = System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb();
                InvalidateBoth();
                TouchSave();
            });
            body.Children.Add(GeneralRow("预设色", "点一下直接套用", _wMain));

            _sR = ChSlider(0, 255, _ch.Main.R, 1, 0, "", delegate(double v)
            { _ch.Main = Color.FromArgb(_ch.Main.A, (byte)v, _ch.Main.G, _ch.Main.B); _cfg.CrosshairColor = System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb(); });
            body.Children.Add(GeneralRow("红", "", _sR));
            _sG = ChSlider(0, 255, _ch.Main.G, 1, 0, "", delegate(double v)
            { _ch.Main = Color.FromArgb(_ch.Main.A, _ch.Main.R, (byte)v, _ch.Main.B); _cfg.CrosshairColor = System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb(); });
            body.Children.Add(GeneralRow("绿", "", _sG));
            _sB = ChSlider(0, 255, _ch.Main.B, 1, 0, "", delegate(double v)
            { _ch.Main = Color.FromArgb(_ch.Main.A, _ch.Main.R, _ch.Main.G, (byte)v); _cfg.CrosshairColor = System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb(); });
            body.Children.Add(GeneralRow("蓝", "", _sB));

            _sOpacity = ChSlider(0, 100, _ch.Opacity, 1, 0, "%", delegate(double v) { _ch.Opacity = (int)v; _cfg.CrosshairOpacity = (int)v; });
            body.Children.Add(GeneralRow("不透明度", "本体透明度，与描边分开", _sOpacity));
            return card;
        }

        private UIElement BuildCrosshairOutlineCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("OUTLINE  /  描边", "CONTRAST", out body);

            _tOutline = AdvToggle(_ch.OutlineEnabled, delegate(bool v)
            { _ch.OutlineEnabled = v; _cfg.CrosshairOutline = v; InvalidateBoth(); });
            body.Children.Add(GeneralRow("启用描边", "亮背景下把准星压出来", _tOutline));

            _wOutline = new VigilSwatches(Accent, Line);
            _wOutline.SetItems(ChOutlinePalette, 0, delegate(int i)
            {
                _ch.OutlineColor = ChOutlinePalette[i];
                _cfg.CrosshairOutlineColor = System.Drawing.Color.FromArgb(255, _ch.OutlineColor.R, _ch.OutlineColor.G, _ch.OutlineColor.B).ToArgb();
                InvalidateBoth();
                TouchSave();
            });
            body.Children.Add(GeneralRow("描边色", "可单独于本体设置", _wOutline));

            _sOutOpacity = ChSlider(0, 100, _ch.OutlineOpacity, 1, 0, "%", delegate(double v) { _ch.OutlineOpacity = (int)v; });
            body.Children.Add(GeneralRow("描边不透明度", "设为 0 等同关闭", _sOutOpacity));

            _sOutWidth = ChSlider(0, 5, _ch.OutlineWidth, 0.25, 2, "", delegate(double v) { _ch.OutlineWidth = v; });
            body.Children.Add(GeneralRow("描边粗细", "每边扩展的像素", _sOutWidth));
            return card;
        }

        private UIElement BuildCrosshairFeedbackCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("FEEDBACK  /  动态反馈", "SPREAD", out body);

            _tDyn = AdvToggle(_ch.DynamicEnabled, delegate(bool v) { _ch.DynamicEnabled = v; InvalidateBoth(); });
            body.Children.Add(GeneralRow("动态总开关", "关掉就是纯静态准星", _tDyn));

            _tFire = AdvToggle(_ch.FireEnabled, delegate(bool v) { _ch.FireEnabled = v; InvalidateBoth(); });
            body.Children.Add(GeneralRow("左键按住扩散", "按下张开、松开按曲线收回", _tFire));

            _sFireAmt = ChSlider(0, 40, _ch.FireAmount, 0.5, 1, "", delegate(double v) { _ch.FireAmount = v; });
            body.Children.Add(GeneralRow("扩散幅度", "按住时最多张开的像素", _sFireAmt));

            _sFireDur = ChSlider(20, 600, _ch.FireDuration, 5, 0, "ms", delegate(double v) { _ch.FireDuration = (int)v; });
            body.Children.Add(GeneralRow("恢复时长", "松开后回到原位的时间", _sFireDur));

            _cCurve = new VigilChips(Accent, Line, Ink, Sub, Panel);
            _cCurve.SetItems(new string[] { "指数回落", "线性", "先撑后落" }, _ch.FireCurve, delegate(int i) { _ch.FireCurve = i; });
            body.Children.Add(GeneralRow("恢复曲线", "手感差异：指数最自然", _cCurve));

            _sFireMax = ChSlider(1, 4, _ch.FireMaxMul, 0.1, 1, "×", delegate(double v) { _ch.FireMaxMul = v; });
            body.Children.Add(GeneralRow("叠加上限", "连点时最多叠到幅度的几倍", _sFireMax));

            _tHit = AdvToggle(_ch.HitFlash, delegate(bool v) { _ch.HitFlash = v; });
            body.Children.Add(GeneralRow("命中闪光", "开火瞬间中心闪一圈", _tHit));
            return card;
        }

        private UIElement BuildCrosshairKeysCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("KEYS  /  按键反馈", "TRIGGER", out body);

            // 键位就地可改：点一下，再按新的键
            KeyBoxRef ringBox = KeyBox("缩圈键", _ch.RingKey, "ring");
            body.Children.Add(GeneralRow("缩圈键", "点一下，然后按新键；Esc 取消", ringBox.Frame));

            _sRingDur = ChSlider(200, 3000, _ch.RingDuration, 50, 0, "ms", delegate(double v) { _ch.RingDuration = (int)v; _cfg.CrosshairRingDurationMs = (int)v; });
            body.Children.Add(GeneralRow("缩圈时长", "从起始半径收到中心所用时间", _sRingDur));

            _sRingRad = ChSlider(8, 120, _ch.RingRadiusMax, 1, 0, "", delegate(double v) { _ch.RingRadiusMax = v; _cfg.CrosshairRingRadius = (int)v; });
            body.Children.Add(GeneralRow("缩圈半径", "缩圈起始半径", _sRingRad));

            _cRingEase = new VigilChips(Accent, Line, Ink, Sub, Panel);
            _cRingEase.SetItems(CrosshairEase.Names, _ch.RingEase, delegate(int i)
            {
                _ch.RingEase = i;
                _cfg.CrosshairRingEase = i;
                if (_chPrev != null) _chPrev.TriggerRing();
                CrosshairInvalidate();
            });
            body.Children.Add(GeneralRow("缩圈缓动", "先快后慢 / 先慢后快 / 平滑 / 回弹", _cRingEase));

            KeyBoxRef expandBox = KeyBox("回弹键", _ch.ExpandKey, "expand");
            body.Children.Add(GeneralRow("回弹键", "点一下，然后按新键；Esc 取消", expandBox.Frame));

            _sExpDur = ChSlider(60, 1500, _ch.ExpandDuration, 10, 0, "ms", delegate(double v) { _ch.ExpandDuration = (int)v; _cfg.CrosshairExpandDurationMs = (int)v; });
            body.Children.Add(GeneralRow("回弹时长", "先扩后收的总时长", _sExpDur));

            _sExpAmt = ChSlider(0, 40, _ch.ExpandAmount, 0.5, 1, "", delegate(double v) { _ch.ExpandAmount = v; _cfg.CrosshairExpandAmount = (int)Math.Round(v); });
            body.Children.Add(GeneralRow("回弹幅度", "回弹时额外张开的像素", _sExpAmt));

            _chKeyStatus = new TextBlock();
            _chKeyStatus.Text = "当前：缩圈 " + _ch.RingKey + " · 回弹 " + _ch.ExpandKey;
            _chKeyStatus.FontSize = 9;
            _chKeyStatus.Foreground = Accent2;
            _chKeyStatus.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(_chKeyStatus);

            TextBlock note = new TextBlock();
            note.Text = "组合键（Ctrl/Alt/Shift + 键）请到「热键」页设置；这里只录单键。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(note);
            return card;
        }

        private sealed class KeyBoxRef
        {
            public Border Frame;
            public TextBlock Text;
        }

        private KeyBoxRef KeyBox(string caption, string key, string target)
        {
            KeyBoxRef box = new KeyBoxRef();
            Border frame = new Border();
            frame.MinWidth = 96;
            frame.Height = 26;
            frame.CornerRadius = new CornerRadius(3);
            frame.BorderThickness = new Thickness(1);
            frame.BorderBrush = Line;
            frame.Background = PanelAlt;
            frame.Padding = new Thickness(10, 0, 10, 0);
            frame.Cursor = Cursors.Hand;
            TextBlock text = new TextBlock();
            text.Text = key;
            text.FontFamily = UiFonts.Mono;
            text.FontSize = 11;
            text.Foreground = Ink;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            frame.Child = text;
            frame.MouseLeftButtonUp += delegate
            {
                _chCaptureTarget = target;
                text.Text = "按任意键…";
                text.Foreground = Accent;
                frame.BorderBrush = Accent;
                frame.Focus();
            };
            frame.MouseEnter += delegate { if (_chCaptureTarget == null) frame.BorderBrush = Accent; };
            frame.MouseLeave += delegate { if (_chCaptureTarget == null) frame.BorderBrush = Line; };
            box.Frame = frame;
            box.Text = text;
            if (target == "ring") { _ringKeyText = text; _ringKeyFrame = frame; }
            else { _expandKeyText = text; _expandKeyFrame = frame; }
            return box;
        }

        /// <summary>按键录入：工作台页面级拦截，按下什么键就记什么键。</summary>
        private bool HandleKeyCapture(KeyEventArgs e)
        {
            if (_chCaptureTarget == null) return false;
            if (e.Key == Key.Escape) { EndKeyCapture(); e.Handled = true; return true; }
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftAlt ||
                e.Key == Key.RightAlt || e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin || e.Key == Key.System)
                return true;                      // 等真正的那个键

            string name = e.Key.ToString();
            if (_chCaptureTarget == "ring")
            {
                _ch.RingKey = name;
                _cfg.CrosshairRingKey = name;
            }
            else
            {
                _ch.ExpandKey = name;
                _cfg.CrosshairExpandKey = name;
            }
            EndKeyCapture();
            TouchSave();
            e.Handled = true;
            return true;
        }

        private void EndKeyCapture()
        {
            _chCaptureTarget = null;
            if (_ringKeyText != null)
            {
                _ringKeyText.Text = _ch.RingKey;
                _ringKeyText.Foreground = Ink;
            }
            if (_expandKeyText != null)
            {
                _expandKeyText.Text = _ch.ExpandKey;
                _expandKeyText.Foreground = Ink;
            }
            if (_ringKeyFrame != null) _ringKeyFrame.BorderBrush = Line;
            if (_expandKeyFrame != null) _expandKeyFrame.BorderBrush = Line;
            if (_chKeyStatus != null)
                _chKeyStatus.Text = "当前：缩圈 " + _ch.RingKey + " · 回弹 " + _ch.ExpandKey;
        }

        private UIElement BuildCrosshairPositionCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("POSITION  /  位置", "ALIGN", out body);

            _sX = ChSlider(-200, 200, _ch.OffsetX, 1, 0, "", delegate(double v) { _ch.OffsetX = v; _cfg.CrosshairOffsetX = (int)v; });
            body.Children.Add(GeneralRow("X 偏移", "相对窗口中心", _sX));
            _sY = ChSlider(-200, 200, _ch.OffsetY, 1, 0, "", delegate(double v) { _ch.OffsetY = v; _cfg.CrosshairOffsetY = (int)v; });
            body.Children.Add(GeneralRow("Y 偏移", "相对窗口中心", _sY));

            _tDpi = AdvToggle(_ch.DpiScale, delegate(bool v) { _ch.DpiScale = v; });
            body.Children.Add(GeneralRow("按 DPI 缩放", "不同缩放比例下保持观感一致", _tDpi));

            _tSnap = AdvToggle(_ch.SnapCenter, delegate(bool v) { _ch.SnapCenter = v; });
            body.Children.Add(GeneralRow("吸附窗口中心", "全屏 / 无边框都对齐", _tSnap));
            return card;
        }

        private UIElement BuildCrosshairPreviewCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("PREVIEW  /  实时预览", "LIVE", out body);

            _chPrev = new CrosshairPreview();
            _chPrev.Model = _ch;
            _chPrev.BackgroundMode = 0;
            _chPrev.Height = 268;
            Border host = new Border();
            host.CornerRadius = new CornerRadius(4);
            host.ClipToBounds = true;
            host.BorderBrush = Line;
            host.BorderThickness = new Thickness(1);
            host.Child = _chPrev;
            host.Margin = new Thickness(0, 8, 0, 10);
            body.Children.Add(host);

            _cBg = new VigilChips(Accent, Line, Ink, Sub, Panel);
            _cBg.SetItems(new string[] { "暗场景", "亮场景", "棋盘格" }, 0, delegate(int i)
            {
                _chPrev.BackgroundMode = i;
                _chPrev.InvalidateVisual();
            });
            body.Children.Add(GeneralRow("预览底板", "换底看可见度", _cBg));

            _sZoom = new WpfSlider(Accent, Line, KnobBrush(), Ink, Sub, 208);
            _sZoom.Minimum = 0.5; _sZoom.Maximum = 2; _sZoom.Step = 0.05; _sZoom.Decimals = 2; _sZoom.Unit = "×";
            _sZoom.SetValueSilent(1);
            _sZoom.ValueChanged += delegate { _chPrev.Zoom = _sZoom.Value; _chPrev.InvalidateVisual(); };
            body.Children.Add(GeneralRow("预览缩放", "放大看细节", _sZoom));

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            _tGrid = AdvToggle(true, delegate(bool v) { _chPrev.ShowGrid = v; _chPrev.InvalidateVisual(); });
            _tLimit = AdvToggle(true, delegate(bool v) { _chPrev.ShowLimit = v; _chPrev.InvalidateVisual(); });
            StackPanel a = new StackPanel();
            a.Orientation = Orientation.Horizontal;
            a.Margin = new Thickness(0, 0, 18, 0);
            a.Children.Add(_tGrid);
            TextBlock al = new TextBlock();
            al.Text = "对齐参考线";
            al.FontSize = 10.5;
            al.Foreground = Sub;
            al.Margin = new Thickness(8, 0, 0, 0);
            al.VerticalAlignment = VerticalAlignment.Center;
            a.Children.Add(al);
            StackPanel b = new StackPanel();
            b.Orientation = Orientation.Horizontal;
            b.Children.Add(_tLimit);
            TextBlock bl = new TextBlock();
            bl.Text = "扩散上限圈";
            bl.FontSize = 10.5;
            bl.Foreground = Sub;
            bl.Margin = new Thickness(8, 0, 0, 0);
            bl.VerticalAlignment = VerticalAlignment.Center;
            b.Children.Add(bl);
            row.Children.Add(a);
            row.Children.Add(b);
            body.Children.Add(GeneralRow("叠加参考", "看清边界与上限", row));

            _chFpsText = MonoText("-- FPS / 跟随刷新", 9, Accent2);
            body.Children.Add(GeneralRow("渲染帧率", "同步显示器刷新，不受 60 帧限制", _chFpsText));

            _cFps = new VigilChips(Accent, Line, Ink, Sub, Panel);
            _cFps.SetItems(new string[] { "跟随刷新", "120", "144", "240" }, 0, delegate(int i)
            {
                int target = i == 0 ? 0 : (i == 1 ? 120 : (i == 2 ? 144 : 240));
                _chPrev.TargetFps = target;
                _chPrevLabel = i == 0 ? "跟随刷新" : target + "Hz";
                RefreshCrosshairLiveText();
            });
            body.Children.Add(GeneralRow("渲染节奏", "240 = 内部步进，显示仍看刷新率", _cFps));

            _chStateText = new TextBlock();
            _chStateText.Text = "在预览里按住左键试扩散；按 " + _ch.RingKey + " 缩圈、" + _ch.ExpandKey + " 回弹。";
            _chStateText.FontSize = 9;
            _chStateText.Foreground = Muted;
            _chStateText.TextWrapping = TextWrapping.Wrap;
            _chStateText.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(_chStateText);
            return card;
        }

        private UIElement BuildCrosshairPresetCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("PRESETS  /  预设", "ONE CLICK", out body);

            TextBlock cap = new TextBlock();
            cap.Text = "从成熟方案起步，再逐项微调";
            cap.FontSize = 9.5;
            cap.Foreground = Muted;
            cap.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(cap);

            // 直接塞进卡片正文：WrapPanel 会按卡片宽度换行，不会顶出右边界
            VigilChips chips = new VigilChips(Accent, Line, Ink, Sub, Panel);
            chips.SetItems(new string[] { "CS2 经典静态", "Valorant 内线", "战术小点", "大十字", "厚描边" }, 0, delegate(int i) { ApplyCrosshairPreset(i); });
            chips.Margin = new Thickness(0, 7, 0, 4);
            body.Children.Add(chips);

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE8C8", "复制设置", delegate { CopyCrosshairCode(); }, Sub));
            actions.Children.Add(CommandButton("\uE777", "恢复默认", delegate { ApplyCrosshairPreset(0); }, Sub));
            body.Children.Add(actions);

            TextBlock note = new TextBlock();
            note.Text = "原型阶段：开火扩散、双线组、旋转、描边不透明度暂时只存在内存里，保存与覆盖层实装放到下一步。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 8.5;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }

        // ---------------- 预设与复制 ----------------
        private void ApplyCrosshairPreset(int id)
        {
            if (_ch == null) return;
            if (id == 1)
            {
                _ch.Type = 6; _ch.Length = 6; _ch.Gap = 3; _ch.Thickness = 2; _ch.DotSize = 0;
                _ch.OuterEnabled = true; _ch.OuterLength = 8; _ch.OuterGap = 6; _ch.OuterThickness = 2;
                _ch.Main = Color.FromArgb(255, 0, 229, 255); _ch.Opacity = 100; _ch.OutlineWidth = 2;
            }
            else if (id == 2)
            {
                _ch.Type = 2; _ch.Length = 0; _ch.Gap = 0; _ch.Thickness = 1; _ch.DotSize = 3.5;
                _ch.Main = Color.FromArgb(255, 255, 235, 59); _ch.Opacity = 100; _ch.OutlineWidth = 1.5;
            }
            else if (id == 3)
            {
                _ch.Type = 0; _ch.Length = 34; _ch.Gap = 10; _ch.Thickness = 3; _ch.DotSize = 2;
                _ch.Main = Colors.White; _ch.Opacity = 90; _ch.OutlineWidth = 3;
            }
            else if (id == 4)
            {
                _ch.Type = 0; _ch.Length = 14; _ch.Gap = 2; _ch.Thickness = 2.5; _ch.DotSize = 1;
                _ch.Main = Color.FromArgb(255, 255, 45, 149); _ch.Opacity = 100; _ch.OutlineWidth = 4;
            }
            else
            {
                _ch.Type = 0; _ch.Length = 16; _ch.Gap = -2; _ch.Thickness = 1.5; _ch.DotSize = 0;
                _ch.Main = Color.FromArgb(255, 0, 255, 128); _ch.Opacity = 92; _ch.OutlineWidth = 1.5;
            }

            if (_sLen != null) _sLen.SetValueSilent(_ch.Length);
            if (_sGap != null) _sGap.SetValueSilent(_ch.Gap);
            if (_sThick != null) _sThick.SetValueSilent(_ch.Thickness);
            if (_sDot != null) _sDot.SetValueSilent(_ch.DotSize);
            if (_sOuterLen != null) _sOuterLen.SetValueSilent(_ch.OuterLength);
            if (_sOuterGap != null) _sOuterGap.SetValueSilent(_ch.OuterGap);
            if (_sOuterThick != null) _sOuterThick.SetValueSilent(_ch.OuterThickness);
            if (_sOpacity != null) _sOpacity.SetValueSilent(_ch.Opacity);
            if (_sOutWidth != null) _sOutWidth.SetValueSilent(_ch.OutlineWidth);
            if (_sR != null) _sR.SetValueSilent(_ch.Main.R);
            if (_sG != null) _sG.SetValueSilent(_ch.Main.G);
            if (_sB != null) _sB.SetValueSilent(_ch.Main.B);
            if (_cType != null) _cType.Select(_ch.Type, true);
            InvalidateBoth();
            RefreshCrosshairLiveText();
            TouchSave();
        }

        private void CopyCrosshairCode()
        {
            if (_ch == null) return;
            string code = "[Crosshair]\r\nType=" + _ch.Type +
                          "\r\nLength=" + _ch.Length.ToString("F1") +
                          "\r\nGap=" + _ch.Gap.ToString("F1") +
                          "\r\nThickness=" + _ch.Thickness.ToString("F1") +
                          "\r\nDotSize=" + _ch.DotSize.ToString("F1") +
                          "\r\nColor=" + System.Drawing.Color.FromArgb(255, _ch.Main.R, _ch.Main.G, _ch.Main.B).ToArgb() +
                          "\r\nOpacity=" + _ch.Opacity +
                          "\r\nOutlineWidth=" + _ch.OutlineWidth.ToString("F2") +
                          "\r\nOutlineOpacity=" + _ch.OutlineOpacity +
                          "\r\nFireAmount=" + _ch.FireAmount.ToString("F1") +
                          "\r\nFireDurationMs=" + _ch.FireDuration +
                          "\r\nFireCurve=" + _ch.FireCurve;
            try { Clipboard.SetText(code); } catch { }
            if (_chStateText != null) _chStateText.Text = "已复制当前准星参数（可直接写进 config.ini 的 [Crosshair]）。";
        }
    }
}

















