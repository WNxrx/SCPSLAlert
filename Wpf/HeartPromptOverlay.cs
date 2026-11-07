using System;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GPoint = System.Drawing.Point;

namespace SCPSLAlert
{
    /// <summary>
    /// 心率屏幕提示：只订阅 HeartRateHub，不掺和游戏事件。
    /// 样式（完整卡片 / 无框 / 纯数字、外框、标签、单位、脉冲、字号、颜色、位置）
    /// 全在 设置 → 提示 → 心率提示 里调，运行中改完半秒内生效。
    /// </summary>
    internal sealed class HeartPromptOverlay : Window
    {
        private readonly Config _cfg;
        private readonly WpfHeartCard _card;
        private readonly System.Windows.Forms.Timer _poll;
        private readonly System.Windows.Forms.Timer _pulse;
        private int _bpm;
        private DateTime _lastPulseAt = DateTime.Now;
        private bool _visibleWanted;
        private bool _sourceReady;
        private bool _editMode;
        private bool _dragging;
        private int _dragDx, _dragDy;

        public event Action PositionChanged;

        public HeartPromptOverlay(Config cfg)
        {
            _cfg = cfg;
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

            _card = new WpfHeartCard();
            _card.IsHitTestVisible = false;
            _card.MouseLeftButtonDown += OnCardMouseDown;
            _card.MouseMove += OnCardMouseMove;
            _card.MouseLeftButtonUp += OnCardMouseUp;
            Content = _card;
            Opacity = 1;

            HeartRateHub.BpmChanged += OnBpmChanged;
            HeartRateHub.BleConnectionChanged += OnBleConnectionChanged;

            SourceInitialized += delegate
            {
                ApplyExStyle();
                TryHideFromCapture();
                _sourceReady = true;
            };
            Loaded += delegate { ApplyExStyle(); PositionWindow(); TryHideFromCapture(); };
            DpiChanged += delegate
            {
                MeasureCard();
                PositionWindow();
            };

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 500;
            _poll.Tick += delegate { PollConfig(); };
            _poll.Start();

            _pulse = new System.Windows.Forms.Timer();
            _pulse.Interval = 40;
            _pulse.Tick += delegate { TickPulse(); };

            _bpm = HeartRateHub.Bpm;
            _card.Bpm = _bpm;
            ApplyConfig(true);
        }

        private double DpiX
        {
            get { try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1.0; } }
        }

        private double DpiY
        {
            get { try { return VisualTreeHelper.GetDpi(this).DpiScaleY; } catch { return 1.0; } }
        }

        private DateTime _previewUntil = DateTime.MinValue;

        /// <summary>“预览全部”时让心率提示也出现几秒（没有真实心率时用占位值）。</summary>
        public bool Preview(int seconds, int bpm)
        {
            try
            {
                _previewUntil = DateTime.UtcNow.AddSeconds(Math.Max(2, seconds));
                _bpm = bpm > 0 ? bpm : 72;
                _card.Bpm = _bpm;
                ApplyConfig(true);
                return true;
            }
            catch { return false; }
        }

        private void PollConfig()
        {
            if (_previewUntil != DateTime.MinValue)
            {
                if (DateTime.UtcNow < _previewUntil) { ApplyConfig(false); return; }
                _previewUntil = DateTime.MinValue;
                _bpm = HeartRateHub.Bpm;
                _card.Bpm = _bpm;
                ApplyConfig(false);
                return;
            }
            int live = HeartRateHub.Bpm;
            if (live != _bpm)
            {
                _bpm = live;
                _card.Bpm = live;
            }
            ApplyConfig(false);
        }

        private void OnBpmChanged(int bpm)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(delegate { OnBpmChanged(bpm); })); } catch { }
                return;
            }
            _bpm = Math.Max(0, bpm);
            _card.Bpm = _bpm;
            ApplyConfig(false);
        }

        /// <summary>
        /// 调整模式：心率提示也进入可拖动状态。
        /// 没有真实 BPM 时用 72 做占位，方便先摆位置。
        /// </summary>
        public bool EditMode
        {
            get { return _editMode; }
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                IsHitTestVisible = value;
                _card.IsHitTestVisible = value;
                _card.EditMode = value;
                ApplyExStyle();
                if (value)
                {
                    if (!IsVisible) Show();
                    if (_bpm <= 0) _card.Bpm = 72;
                }
                ApplyConfig(true);
                if (value && (_cfg.HeartPromptX < 0 || _cfg.HeartPromptY < 0))
                {
                    System.Drawing.Point p = ActualLocationPx();
                    StoreHeartPosition(p.X, p.Y);
                }
                if (!value && PositionChanged != null) PositionChanged();
            }
        }

        public System.Drawing.Point RestLocation
        {
            get
            {
                try
                {
                    return new System.Drawing.Point(_cfg.HeartPromptX, _cfg.HeartPromptY);
                }
                catch { return new System.Drawing.Point(0, 0); }
            }
        }

        public void Nudge(int dx, int dy)
        {
            try
            {
                System.Drawing.Point p = ActualLocationPx();
                StoreHeartPosition(p.X + dx, p.Y + dy);
                PositionWindow();
            }
            catch { }
        }

        private void OnCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_editMode || e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            _dragging = true;
            System.Drawing.Point cur = System.Windows.Forms.Cursor.Position;
            System.Drawing.Point rest = ActualLocationPx();
            _dragDx = cur.X - rest.X;
            _dragDy = cur.Y - rest.Y;
            _card.CaptureMouse();
            e.Handled = true;
        }

        private void OnCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging) return;
            System.Drawing.Point cur = System.Windows.Forms.Cursor.Position;
            StoreHeartPosition(cur.X - _dragDx, cur.Y - _dragDy);
            PositionWindow();
        }

        private void OnCardMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _card.ReleaseMouseCapture();
            if (PositionChanged != null) PositionChanged();
        }

        private System.Drawing.Point ActualLocationPx()
        {
            try
            {
                System.Drawing.Rectangle r = WpfScreen.GetBounds(this);
                if (!r.IsEmpty) return new System.Drawing.Point(r.Left, r.Top);
            }
            catch { }
            try
            {
                return new System.Drawing.Point(
                    (int)Math.Round(Left * DpiX),
                    (int)Math.Round(Top * DpiY));
            }
            catch { return new System.Drawing.Point(0, 0); }
        }

        /// <summary>把物理位置折回 0..1，并同步旧 X/Y 供旧界面使用。</summary>
        private void StoreHeartPosition(int x, int y)
        {
            _cfg.SetHeartPromptPixelPosition(x, y);
        }

        private void OnBleConnectionChanged(bool connected)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(delegate { OnBleConnectionChanged(connected); })); } catch { }
                return;
            }
            if (!connected)
            {
                _bpm = 0;
                _card.Bpm = 0;
                ApplyConfig(false);
            }
        }

        private void ApplyConfig(bool force)
        {
            if (_cfg == null) return;

            int style = Math.Max(0, Math.Min(2, _cfg.HeartPromptStyle));
            bool frame = _cfg.HeartPromptFrame;
            bool label = _cfg.HeartPromptLabel;
            bool unit = _cfg.HeartPromptUnit;
            bool pulse = _cfg.HeartPromptPulse;
            bool wave = _cfg.HeartPromptWave;
            double fontSize = Math.Max(18, Math.Min(140, _cfg.HeartPromptFontSize));
            Color accent = ParseColor(_cfg.HeartPromptColor, Color.FromRgb(255, 84, 90));

            _card.Style = style;
            _card.Frame = frame;
            _card.Label = label;
            _card.Unit = unit;
            _card.Pulse = pulse;
            _card.Wave = wave;
            _card.WavePoints = _cfg.HeartPromptWavePoints;
            _card.WaveAmp = _cfg.HeartPromptWaveAmp;
            _card.WaveWidth = _cfg.HeartPromptWaveWidth;
            _card.FontSize = fontSize;
            _card.Accent = accent;
            _card.Opacity = Math.Max(0.05, Math.Min(1.0, _cfg.HeartPromptOpacity / 100.0));

            bool want = _editMode || _previewUntil != DateTime.MinValue || (_cfg.HeartPromptEnabled && _bpm > 0);
            _visibleWanted = want;

            if (!want)
            {
                if (IsVisible) Hide();
                _pulse.Stop();
                return;
            }

            MeasureCard();
            PositionWindow();
            if (!IsVisible) Show();
            _card.Bpm = _bpm > 0 || !_editMode ? _bpm : 72;
            ApplyExStyle();
            TryHideFromCapture();
            if (!_pulse.Enabled) _pulse.Start();
            TickPulse();
        }

        private void MeasureCard()
        {
            _card.InvalidateMeasure();
            _card.Measure(new Size(1600, 400));
            Size want = _card.DesiredSize;
            if (want.Width <= 0) want.Width = 180;
            if (want.Height <= 0) want.Height = 80;
            Width = want.Width;
            Height = want.Height;
            _card.Arrange(new Rect(0, 0, want.Width, want.Height));
        }

        private void PositionWindow()
        {
            double sx = DpiX <= 0 ? 1.0 : DpiX;
            double sy = DpiY <= 0 ? 1.0 : DpiY;
            int px;
            int py;
            if (_cfg.HeartPromptX < 0 || _cfg.HeartPromptY < 0)
            {
                try
                {
                    System.Drawing.Rectangle wa = ScreenGeometry.PrimaryWorkArea;
                    int w = (int)Math.Round(Width * sx);
                    int h = (int)Math.Round(Height * sy);
                    px = Math.Max(wa.Left, wa.Right - w - 24);
                    py = Math.Max(wa.Top, wa.Top + 24);
                }
                catch
                {
                    px = 24;
                    py = 24;
                }
            }
            else if (!double.IsNaN(_cfg.HeartPromptXPercent) && !double.IsNaN(_cfg.HeartPromptYPercent)
                && _cfg.HeartPromptXPercent >= 0 && _cfg.HeartPromptXPercent <= 1
                && _cfg.HeartPromptYPercent >= 0 && _cfg.HeartPromptYPercent <= 1)
            {
                System.Drawing.Rectangle mon = ScreenGeometry.ResolveMonitor(
                    _cfg.HeartPromptMonitor,
                    new System.Drawing.Point(_cfg.HeartPromptX, _cfg.HeartPromptY));
                System.Drawing.Point p = ScreenGeometry.Denormalize(
                    new NormalizedPoint(_cfg.HeartPromptXPercent, _cfg.HeartPromptYPercent), mon);
                px = p.X;
                py = p.Y;
            }
            else
            {
                px = _cfg.HeartPromptX;
                py = _cfg.HeartPromptY;
            }

            if (!WpfScreen.Move(this, px, py))
            {
                Left = px / sx;
                Top = py / sy;
            }
        }

        private void TickPulse()
        {
            if (_bpm <= 0 && !_editMode) return;
            DateTime now = DateTime.Now;
            double dt = (now - _lastPulseAt).TotalSeconds;
            _lastPulseAt = now;
            if (dt <= 0) dt = 0.04;
            if (dt > 0.15) dt = 0.04;
            _card.Advance(dt, _bpm > 0 ? _bpm : 72);
        }

        private void ApplyExStyle()
        {
            if (!_sourceReady) return;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int want = ex | NativeMethods.WS_EX_LAYERED
                              | NativeMethods.WS_EX_NOACTIVATE
                              | NativeMethods.WS_EX_TOOLWINDOW;
                if (_editMode) want &= ~NativeMethods.WS_EX_TRANSPARENT;
                else want |= NativeMethods.WS_EX_TRANSPARENT;
                if (want != ex) NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, want);
            }
            catch { }
        }

        private void TryHideFromCapture()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) NativeMethods.HideFromCapture(hwnd, "心率提示");
            }
            catch { }
        }

        private static Color ParseColor(string text, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return fallback;
                string[] p = text.Split(new char[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) return fallback;
                int r = int.Parse(p[0].Trim(), CultureInfo.InvariantCulture);
                int g = int.Parse(p[1].Trim(), CultureInfo.InvariantCulture);
                int b = int.Parse(p[2].Trim(), CultureInfo.InvariantCulture);
                return Color.FromRgb((byte)Math.Max(0, Math.Min(255, r)),
                    (byte)Math.Max(0, Math.Min(255, g)),
                    (byte)Math.Max(0, Math.Min(255, b)));
            }
            catch { return fallback; }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _poll.Stop(); _pulse.Stop(); } catch { }
            try { HeartRateHub.BpmChanged -= OnBpmChanged; } catch { }
            try { HeartRateHub.BleConnectionChanged -= OnBleConnectionChanged; } catch { }
            base.OnClosed(e);
        }

        public void Shutdown()
        {
            try { _poll.Stop(); } catch { }
            try { _pulse.Stop(); } catch { }
            try { HeartRateHub.BpmChanged -= OnBpmChanged; } catch { }
            try { HeartRateHub.BleConnectionChanged -= OnBleConnectionChanged; } catch { }
            try { if (IsVisible) Close(); } catch { }
        }
    }
}
