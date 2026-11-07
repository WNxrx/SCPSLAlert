using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GPoint = System.Drawing.Point;
using GSize = System.Drawing.Size;

namespace SCPSLAlert
{
    /// <summary>
    /// 新版 A.R.C.T 提示窗口（WPF）。
    ///
    /// 和旧版 OverlayForm 一样是“逐像素透明 + 鼠标穿透 + 不进 Alt+Tab”的悬浮提示，
    /// 但视觉完全由 WpfPromptCard 重画：近黑切角面板、信号色竖线、等宽读数、扫描线。
    /// 坐标仍使用物理像素（与 config.ini 的 X/Y、旧版提示、屏幕分辨率同一坐标系），
    /// 窗口内部再换算成 WPF 的 DIP。
    /// </summary>
    internal sealed class WpfAlertOverlay : Window, IAlertSurface
    {
        private enum SurfaceState { Hidden, Showing, Visible, Hiding }

        private readonly Config _cfg;
        private readonly AlertConfig _alert;
        private readonly WpfPromptCard _card;
        private WindowStyle _style = new WindowStyle();
        private bool _styleInitialized;
        private GPoint _restPx;
        private GPoint _displayPx;
        private SurfaceState _state = SurfaceState.Hidden;
        private string _text = "";
        private bool _highlight;
        private bool _editMode;
        private bool _clickThrough = true;
        private bool _dragging;
        private int _dragDx, _dragDy;
        private readonly DispatcherTimer _phaseTimer;
        private bool _phaseRunning;
        private readonly System.Windows.Forms.Timer _hoverTimer;
        private double _hoverAlpha = 1.0;

        // 入场 / 退场动画：用 WinForms 计时器，宿主是 WinForms 消息循环时也能稳定推进。
        private readonly System.Windows.Forms.Timer _animTimer;
        private DateTime _animStart;
        private int _animDuration;
        private bool _animExiting;
        private double _exitFrom = 1;
        private int _exitFromOffsetY;
        private int _animOffsetY;

        public event EventHandler PositionChanged;

        public WpfAlertOverlay(Config cfg, AlertConfig alert)
        {
            _cfg = cfg;
            _alert = alert;

            Title = alert == null ? "SCPSLAlert 提示" : alert.DisplayName;
            WindowStyle = System.Windows.WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            _card = new WpfPromptCard();
            _card.Alert = alert;
            // 平时不做 WPF 命中测试，保证点击穿透；悬停检测改为轮询鼠标位置，
            // 这样鼠标压到提示上时可以让整卡变淡，但不会挡住游戏点击。
            _card.IsHitTestVisible = false;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            _card.MouseMove += OnCardMouseMove;
            _card.MouseLeftButtonUp += OnCardMouseUp;
            Content = _card;
            Opacity = 1;

            _restPx = AlertRestPoint();
            _displayPx = _restPx;
            ApplyRestToWindow();

            _phaseTimer = new DispatcherTimer(DispatcherPriority.Render);
            _phaseTimer.Interval = TimeSpan.FromMilliseconds(33);
            _phaseTimer.Tick += delegate
            {
                if (_state == SurfaceState.Hidden) { StopPhase(); return; }
                _card.Phase = (Environment.TickCount % 3200) / 3200.0;
            };

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 15;
            _animTimer.Tick += OnAnimTick;

            _hoverTimer = new System.Windows.Forms.Timer();
            _hoverTimer.Interval = 33;
            _hoverTimer.Tick += delegate { UpdateHoverFade(); };

            // 挂上去之后保持在截图 / 录屏之外（和旧提示同一个开关）
            SourceInitialized += delegate
            {
                ApplyExStyle();
                TryHideFromCapture();
            };
            Loaded += delegate { ApplyExStyle(); ApplyRestToWindow(); TryHideFromCapture(); };
            DpiChanged += delegate
            {
                // 跨屏/改缩放后，卡片尺寸按新 DPI 重算，再用物理像素重新钉回原位。
                MeasureCard();
                ApplyRestToWindow();
            };
        }

        #region 尺寸 / 位置

        private double DpiX
        {
            get { try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1.0; } }
        }

        private double DpiY
        {
            get { try { return VisualTreeHelper.GetDpi(this).DpiScaleY; } catch { return 1.0; } }
        }

        private void MeasureCard()
        {
            _card.InvalidateMeasure();
            _card.Measure(new Size(2000, 600));
            Size want = _card.DesiredSize;
            if (want.Width <= 0) want.Width = 300;
            if (want.Height <= 0) want.Height = 80;
            Width = want.Width;
            Height = want.Height;
            _card.Arrange(new Rect(0, 0, want.Width, want.Height));
        }

        /// <summary>
        /// 从归一化坐标算出提示左上角的物理像素位置。
        /// 基准是提示所在显示器；旧 X/Y 只作为没有归一化值时的兜底。
        /// </summary>
        private GPoint AlertRestPoint()
        {
            if (_alert == null || _alert.Style == null)
                return new GPoint(30 + (_cfg == null ? 0 : _cfg.OffsetX),
                                  30 + (_cfg == null ? 0 : _cfg.OffsetY));

            WindowStyle s = _styleInitialized ? _style : _alert.Style;
            int ox = _cfg == null ? 0 : _cfg.OffsetX;
            int oy = _cfg == null ? 0 : _cfg.OffsetY;
            if (!double.IsNaN(s.XPercent) && !double.IsNaN(s.YPercent)
                && s.XPercent >= 0 && s.XPercent <= 1 && s.YPercent >= 0 && s.YPercent <= 1)
            {
                System.Drawing.Rectangle mon = ScreenGeometry.ResolveMonitor(s.Monitor, new GPoint(s.X, s.Y));
                GPoint p = ScreenGeometry.Denormalize(new NormalizedPoint(s.XPercent, s.YPercent), mon);
                return new GPoint(p.X + ox, p.Y + oy);
            }
            return new GPoint(s.X + ox, s.Y + oy);
        }

        /// <summary>拖动/微调后把物理位置折回 0..1，并同步旧 X/Y 供旧界面使用。</summary>
        private void StorePhysicalPosition(int x, int y)
        {
            if (_alert != null && _alert.Style != null)
            {
                WindowStyle s = _alert.Style;
                int ox = _cfg == null ? 0 : _cfg.OffsetX;
                int oy = _cfg == null ? 0 : _cfg.OffsetY;
                GPoint basePoint = new GPoint(x - ox, y - oy);
                System.Drawing.Rectangle mon = ScreenGeometry.MonitorBounds(basePoint);
                NormalizedPoint n = ScreenGeometry.Normalize(basePoint, mon);
                s.XPercent = n.X;
                s.YPercent = n.Y;
                s.Monitor = ScreenGeometry.MonitorDevice(basePoint);
                s.X = basePoint.X;
                s.Y = basePoint.Y;
                s.LegacySyncedX = s.X;
                s.LegacySyncedY = s.Y;
                _style.XPercent = s.XPercent;
                _style.YPercent = s.YPercent;
                _style.Monitor = s.Monitor;
                _style.X = s.X;
                _style.Y = s.Y;
                _style.LegacySyncedX = s.LegacySyncedX;
                _style.LegacySyncedY = s.LegacySyncedY;
            }
        }

        private void ApplyRestToWindow()
        {
            int y = _displayPx.Y + _animOffsetY;
            if (!WpfScreen.Move(this, _displayPx.X, y))
            {
                // 句柄还没建好时的兜底：SourceInitialized / Loaded 会再走一次物理定位。
                double sx = DpiX <= 0 ? 1.0 : DpiX;
                double sy = DpiY <= 0 ? 1.0 : DpiY;
                Left = _displayPx.X / sx;
                Top = y / sy;
            }
        }

        public GPoint RestLocation { get { return _restPx; } }

        public GPoint DisplayLocation { get { return _displayPx; } }

        public System.Drawing.Rectangle DisplayBounds
        {
            get
            {
                GSize size = SurfaceSize;
                return new System.Drawing.Rectangle(_displayPx.X, _displayPx.Y,
                    Math.Max(0, size.Width), Math.Max(0, size.Height));
            }
        }

        public void SetDisplayPosition(int x, int y)
        {
            _displayPx = new GPoint(x, y);
            ApplyRestToWindow();
        }

        public GSize SurfaceSize
        {
            get
            {
                MeasureCard();
                double sx = DpiX <= 0 ? 1.0 : DpiX;
                double sy = DpiY <= 0 ? 1.0 : DpiY;
                return new GSize((int)Math.Round(Width * sx), (int)Math.Round(Height * sy));
            }
        }

        public void SetRestLocation(int x, int y)
        {
            _restPx = new GPoint(x, y);
            _displayPx = _restPx;
            StorePhysicalPosition(x, y);
            ApplyRestToWindow();
        }

        public void Nudge(int dx, int dy)
        {
            _restPx = new GPoint(_restPx.X + dx, _restPx.Y + dy);
            _displayPx = _restPx;
            StorePhysicalPosition(_restPx.X, _restPx.Y);
            ApplyRestToWindow();
        }

        #endregion

        #region 公开状态

        public bool IsPopped { get { return _state != SurfaceState.Hidden; } }

        public string CurrentText { get { return _text; } }

        public bool Highlight
        {
            get { return _highlight; }
            set
            {
                if (_highlight == value) return;
                _highlight = value;
                _card.Highlight = value;
                if (_state != SurfaceState.Hidden) StartPhase();
            }
        }

        public bool EditMode
        {
            get { return _editMode; }
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                IsHitTestVisible = value;
                _card.IsHitTestVisible = value;   // 只有调整模式才允许鼠标碰到它
                _card.EditMode = value;
                _clickThrough = !value;
                ApplyExStyle();
                if (value)
                {
                    StopAnim();
                    _state = SurfaceState.Visible;
                    _hoverAlpha = 1;
                    _card.Opacity = 1;
                    _card.Entrance = 1;
                    _animOffsetY = 0;
                    if (!IsVisible) Show();
                    MeasureCard();
                    ApplyRestToWindow();
                }
                ApplyExStyle();
            }
        }

        public void ApplyStyle(WindowStyle style, Config cfg)
        {
            _style.CopyFrom(style);
            _styleInitialized = true;
            _restPx = AlertRestPoint();
            _displayPx = _restPx;
            ApplyRestToWindow();
        }

        public void ReassertExStyle()
        {
            ApplyExStyle();
        }

        /// <summary>
        /// 鼠标压到提示上 → 整卡淡到 25%，让开底下的画面；移开再回到 100%。
        /// 用轮询而不是 WPF MouseEnter：提示本身必须保持点击穿透，
        /// 不能为了“悬停”去抢游戏的鼠标事件。
        /// </summary>
        private void UpdateHoverFade()
        {
            if (_state == SurfaceState.Hidden) return;

            double target = 1.0;
            if (!_editMode && IsCursorOverCard()) target = 0.25;

            if (AnimationsOff)
            {
                _hoverAlpha = target;
            }
            else
            {
                _hoverAlpha += (target - _hoverAlpha) * 0.38;
                if (Math.Abs(target - _hoverAlpha) < 0.01) _hoverAlpha = target;
            }
            _card.Opacity = _hoverAlpha;
        }

        private bool IsCursorOverCard()
        {
            try
            {
                if (Width <= 1 || Height <= 1) return false;
                System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
                double left = _displayPx.X;
                double top = _displayPx.Y + _animOffsetY;
                double right = left + Width * DpiX;
                double bottom = top + Height * DpiY;
                return p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom;
            }
            catch { return false; }
        }

        public void HideNow()
        {
            StopAnim();
            StopPhase();
            _state = SurfaceState.Hidden;
            _card.Entrance = 0;
            _animOffsetY = 0;
            _hoverAlpha = 1;
            _card.Opacity = 1;
            if (IsVisible) Hide();
        }

        #endregion

        #region 弹出 / 缩回

        public void Pop(string text)
        {
            string t = text == null ? "" : text;
            bool same = t == _text;
            _text = t;

            // 正在退场时又来了新内容：取消退场，重新入场。
            if (_state == SurfaceState.Hiding)
            {
                StopAnim();
                _state = SurfaceState.Hidden;
            }
            if (same && _state != SurfaceState.Hidden) return;

            _card.Text = t;
            MeasureCard();
            ApplyRestToWindow();

            if (_state == SurfaceState.Visible || _state == SurfaceState.Showing)
            {
                if (!same)
                {
                    // 已经挂在屏幕上：只换内容，不重播进场动画
                    _card.Opacity = _hoverAlpha;
                    _card.Entrance = 1;
                    _animOffsetY = 0;
                    ApplyRestToWindow();
                }
                StartPhase();
                return;
            }

            _state = SurfaceState.Showing;
            _hoverAlpha = 1;
            _card.Opacity = 1;
            _card.Entrance = 0;
            _animOffsetY = 10;
            ApplyRestToWindow();
            if (!IsVisible) Show();
            ApplyRestToWindow();
            TryHideFromCapture();
            StartPhase();
            StartEntrance();
        }

        public void SetTimer(double remainSeconds, double totalSeconds)
        {
            _card.SetTimer(remainSeconds, totalSeconds);
        }

        public void Retract()
        {
            if (_state == SurfaceState.Hidden || _state == SurfaceState.Hiding) return;

            if (AnimationsOff || _style.Anim == AnimKind.None || _style.AnimOutMs <= 0)
            {
                HideNow();
                return;
            }

            StopAnim();
            StopPhase();
            _animExiting = true;
            _exitFrom = _card.Entrance;
            _exitFromOffsetY = _animOffsetY;
            _animStart = DateTime.Now;
            _animDuration = 150;
            _state = SurfaceState.Hiding;
            _animTimer.Start();
        }

        private void StartEntrance()
        {
            StopAnim();
            if (AnimationsOff || _style.Anim == AnimKind.None || _style.AnimInMs <= 0)
            {
                _card.Entrance = 1;
                _animOffsetY = 0;
                ApplyRestToWindow();
                _state = SurfaceState.Visible;
                return;
            }

            int baseMs = _style.AnimInMs > 0 ? _style.AnimInMs : 240;
            _animDuration = Math.Max(240, Math.Min(440, baseMs + 80));
            _animStart = DateTime.Now;
            _animExiting = false;
            _animTimer.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _animStart).TotalMilliseconds;
            double t = _animDuration <= 0 ? 1 : elapsed / _animDuration;
            if (t < 0) t = 0;

            if (_animExiting)
            {
                double p = EaseInCubic(Math.Min(1, t));
                _card.Entrance = _exitFrom * (1 - p);
                _animOffsetY = _exitFromOffsetY + (int)Math.Round(p * 7);
                ApplyRestToWindow();
                if (t >= 1) HideNow();
                return;
            }

            double eased = EaseOutCubic(Math.Min(1, t));
            _card.Entrance = Math.Min(1, t);
            _animOffsetY = (int)Math.Round((1 - eased) * 10);
            ApplyRestToWindow();
            if (t >= 1)
            {
                _animTimer.Stop();
                _card.Entrance = 1;
                _animOffsetY = 0;
                ApplyRestToWindow();
                _state = SurfaceState.Visible;
            }
        }

        private void StopAnim()
        {
            _animTimer.Stop();
            _animExiting = false;
        }

        private static double EaseOutCubic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t;
            return 1 - u * u * u;
        }

        private static double EaseInCubic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * t;
        }

        private bool AnimationsOff
        {
            get
            {
                if (UiAnim.Off) return true;
                if (_cfg != null && !_cfg.UiAnimationsEnabled) return true;
                return false;
            }
        }

        private void StartPhase()
        {
            if (_phaseRunning) return;
            _phaseRunning = true;
            _card.Phase = (Environment.TickCount % 3200) / 3200.0;
            _phaseTimer.Start();
            _hoverTimer.Start();
        }

        private void StopPhase()
        {
            _phaseRunning = false;
            _phaseTimer.Stop();
            _hoverTimer.Stop();
        }

        #endregion

        #region 穿透 / 截图隐藏

        private void ApplyExStyle()
        {
            if (!IsSourceInitialized) return;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int want = ex | NativeMethods.WS_EX_LAYERED
                              | NativeMethods.WS_EX_NOACTIVATE
                              | NativeMethods.WS_EX_TOOLWINDOW;
                if (_clickThrough) want |= NativeMethods.WS_EX_TRANSPARENT;
                else want &= ~NativeMethods.WS_EX_TRANSPARENT;
                if (want != ex) NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, want);
            }
            catch { }
        }

        private bool IsSourceInitialized
        {
            get { try { return new WindowInteropHelper(this).Handle != IntPtr.Zero; } catch { return false; } }
        }

        private void TryHideFromCapture()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) NativeMethods.HideFromCapture(hwnd, "提示窗");
            }
            catch { }
        }

        #endregion

        #region 拖动（只在调整模式）

        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_editMode || e.ChangedButton != MouseButton.Left) return;
            _dragging = true;
            System.Drawing.Point cur = System.Windows.Forms.Cursor.Position;
            _dragDx = (int)Math.Round((double)(cur.X - _restPx.X));
            _dragDy = (int)Math.Round((double)(cur.Y - _restPx.Y));
            _card.CaptureMouse();
            e.Handled = true;
        }

        private void OnCardMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            System.Drawing.Point cur = System.Windows.Forms.Cursor.Position;
            _restPx = new GPoint(cur.X - _dragDx, cur.Y - _dragDy);
            _displayPx = _restPx;
            StorePhysicalPosition(_restPx.X, _restPx.Y);
            ApplyRestToWindow();
        }

        private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _card.ReleaseMouseCapture();
            if (PositionChanged != null) PositionChanged(this, EventArgs.Empty);
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            StopAnim();
            StopPhase();
            base.OnClosed(e);
        }

        public void Dispose()
        {
            StopAnim();
            StopPhase();
            _animTimer.Stop();
            _phaseTimer.Stop();
            try { if (IsVisible) Close(); } catch { }
        }
    }
}
