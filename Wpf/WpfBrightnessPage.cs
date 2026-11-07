using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCPSLAlert
{
    /// <summary>
    /// 需求一：伽马/饱和度重构（WPF 二级页）。
    ///
    /// 页面结构：标题旁横向切换栏【曲线调整区】【采集区】。
    ///   · 曲线调整区：双曲线画布（可拖点）、预设下拉（带微型曲线）、导入/导出、
    ///     自动补色强度、500ms 自动保存、无存档自动建「预设 1」。
    ///   · 采集区：复用第 6 批的 21 → 7 张采集卡与 7 张缩略图。
    ///
    /// 数据仍写回原来的 [GameBrightness] Profile / SaturationProfile / Presets 键，
    /// 旧 WinForms 亮度校准窗口读到的是同一份数据。
    /// </summary>
    internal sealed partial class WpfVigilDataWindow
    {
        private BrightnessCurveCanvas _curveCanvas;
        private VigilSelect _presetCombo;
        private readonly List<string> _presetNames = new List<string>();
        private TextBlock _curveStatus;
        private TextBlock _curvePointText;
        private Border _curveTab;
        private Border _captureTab;
        private StackPanel _curvePanel;
        private StackPanel _capturePanel;
        private DispatcherTimer _brightnessSaveTimer;
        private List<BrightnessPoint> _curvePoints = new List<BrightnessPoint>();
        private bool _curveLoading;

        private void ShowBrightnessPage(int tab)
        {
            SelectPage(AdvancedPageIndex);
            if (_advDetailPage == null || _advDetailBody == null) return;
            DetachAdvancedDetail();
            _advancedPage.Visibility = Visibility.Collapsed;
            _advDetailBody.Children.Clear();

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 12);

            // 两级返回并排：上级 = 显示与画面（二级），高级 = 高级一级页。
            // 先放“返回上级”（更常用），再放“返回高级”，并用面包屑把层级写清楚。
            StackPanel nav = new StackPanel();
            nav.Orientation = Orientation.Horizontal;
            Button up = CommandButton("\uE72B", "返回上级", delegate { ShowAdvancedDetail("display"); }, Accent);
            up.Margin = new Thickness(0, 0, 8, 0);
            nav.Children.Add(up);
            nav.Children.Add(CommandButton("\uE80F", "返回高级", delegate { SelectPage(AdvancedPageIndex); }, Sub));
            head.Children.Add(nav);

            TextBlock crumb = MonoText("高级  /  显示与画面  /  亮度校准", 8.5, Muted);
            crumb.Margin = new Thickness(0, 7, 0, 0);
            head.Children.Add(crumb);

            TextBlock code = MonoText("BRIGHTNESS  /  CALIBRATION", 9, Accent);
            code.Margin = new Thickness(0, 6, 0, 0);
            head.Children.Add(code);

            Grid titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition());
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = DisplayText("亮度校准", 28, Ink);
            title.FontWeight = FontWeights.Bold;
            titleRow.Children.Add(title);
            StackPanel tabs = new StackPanel();
            tabs.Orientation = Orientation.Horizontal;
            tabs.VerticalAlignment = VerticalAlignment.Bottom;
            _curveTab = BrightnessTab("曲线调整区", delegate { ShowBrightnessPage(0); });
            _captureTab = BrightnessTab("采集区", delegate { ShowBrightnessPage(1); });
            tabs.Children.Add(_curveTab);
            tabs.Children.Add(_captureTab);
            Grid.SetColumn(tabs, 1);
            titleRow.Children.Add(tabs);
            head.Children.Add(titleRow);
            _advDetailBody.Children.Add(head);

            _curvePanel = new StackPanel();
            _capturePanel = new StackPanel();
            _advDetailBody.Children.Add(_curvePanel);
            _advDetailBody.Children.Add(_capturePanel);

            if (tab == 0)
            {
                BuildCurvePanel();
                _curvePanel.Visibility = Visibility.Visible;
                _capturePanel.Visibility = Visibility.Collapsed;
                SetTabState(_curveTab, true);
                SetTabState(_captureTab, false);
            }
            else
            {
                AddPanelCard(_capturePanel, BuildCaptureCard());
                AddPanelCard(_capturePanel, BuildCaptureGalleryCard());
                AddPanelCard(_capturePanel, BuildReferenceCard());
                _curvePanel.Visibility = Visibility.Collapsed;
                _capturePanel.Visibility = Visibility.Visible;
                SetTabState(_curveTab, false);
                SetTabState(_captureTab, true);
            }

            _advDetailPage.Visibility = Visibility.Visible;
            _currentPage = AdvancedPageIndex;
            UpdateRailSelection(AdvancedPageIndex);
        }

        private Border BrightnessTab(string text, Action onClick)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(4);
            b.Padding = new Thickness(14, 7, 14, 7);
            b.Margin = new Thickness(8, 0, 0, 2);
            b.Cursor = Cursors.Hand;
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Line;
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Ink;
            b.Child = t;
            b.MouseLeftButtonUp += delegate { onClick(); };
            return b;
        }

        private void SetTabState(Border tab, bool on)
        {
            if (tab == null) return;
            tab.Background = on ? Accent : Brushes.Transparent;
            TextBlock t = tab.Child as TextBlock;
            if (t != null) t.Foreground = on ? new SolidColorBrush(Color.FromRgb(12, 16, 20)) : Sub;
        }

        // ---------------- 曲线调整区 ----------------

        /// <summary>基础显示行为：把旧窗口里的固定伽马 / 固定饱和度 / 全场景覆盖 / 检测间隔 / 补色增强搬回来。</summary>
        private UIElement BuildBrightnessBasicCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("BASIC  /  基础", "DISPLAY", out body);

            body.Children.Add(GeneralRow("固定伽马", "不跟随画面，按下面的固定值输出",
                AdvToggle(_cfg.GameBrightnessHoldEnabled, delegate(bool v)
                {
                    _cfg.GameBrightnessHoldEnabled = v;
                    TouchSave();
                })));

            TextBox gamma = VigilInput((_cfg.GameBrightnessHoldGamma / 100.0).ToString("0.00"), 90);
            gamma.LostFocus += delegate
            {
                double v;
                if (double.TryParse(gamma.Text, out v))
                {
                    v = Math.Max(0.20, Math.Min(1.60, v));
                    _cfg.GameBrightnessHoldGamma = (int)Math.Round(v * 100);
                    gamma.Text = v.ToString("0.00");
                    TouchSave();
                }
            };
            body.Children.Add(GeneralRow("固定 γ 值", "0.20–1.60；小于 1 提亮", gamma));

            TextBox sat = VigilInput((_cfg.GameBrightnessHoldSaturation / 100.0).ToString("0.00"), 90);
            sat.LostFocus += delegate
            {
                double v;
                if (double.TryParse(sat.Text, out v))
                {
                    v = Math.Max(0.20, Math.Min(1.60, v));
                    _cfg.GameBrightnessHoldSaturation = (int)Math.Round(v * 100);
                    sat.Text = v.ToString("0.00");
                    TouchSave();
                }
            };
            body.Children.Add(GeneralRow("固定饱和度", "1.00 为原始色彩", sat));

            body.Children.Add(GeneralRow("全场景覆盖", "切出游戏后仍保持伽马；会影响该显示器上的其他窗口",
                AdvToggle(_cfg.GameBrightnessForceGlobal, delegate(bool v)
                {
                    _cfg.GameBrightnessForceGlobal = v;
                    TouchSave();
                })));

            body.Children.Add(GeneralRow("检测间隔", "毫秒；越小越跟手，也越吃 CPU",
                AdvNumber(_cfg.GameBrightnessIntervalMs, 100, 5000, delegate(int v)
                {
                    _cfg.GameBrightnessIntervalMs = v;
                    TouchSave();
                })));

            body.Children.Add(GeneralRow("仅画面变化时采集", "画面不动就跳过，省 CPU",
                AdvToggle(_cfg.GameBrightnessCaptureMotionOnly, delegate(bool v)
                {
                    _cfg.GameBrightnessCaptureMotionOnly = v;
                    TouchSave();
                })));

            body.Children.Add(GeneralRow("自动补色增强", "%；拖伽马时自动补偿饱和度",
                AdvNumber((int)Math.Round(_cfg.GameBrightnessCompensationBoost * 100), 0, 50, delegate(int v)
                {
                    _cfg.GameBrightnessCompensationBoost = v / 100.0;
                    TouchSave();
                })));

            TextBlock note = new TextBlock();
            note.Text = "曲线、预设、导入导出在下面的「曲线调整区」；采集与 7 张亮度参考在「采集区」。";
            note.TextWrapping = TextWrapping.Wrap;
            note.FontSize = 9;
            note.Foreground = Muted;
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);
            return card;
        }
        private void BuildCurvePanel()
        {
            _curvePanel.Children.Clear();
            LoadCurvePoints();
            AddPanelCard(_curvePanel, BuildBrightnessBasicCard());

            StackPanel body;
            VigilCard card = AdvancedCard("CURVE  /  曲线", "GAMMA + SAT", out body);

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            _curveCanvas = new BrightnessCurveCanvas();
            _curveCanvas.Height = 330;
            _curveCanvas.Points = _curvePoints;
            _curveCanvas.CompensationBoost = _cfg.GameBrightnessCompensationBoost;
            _curveCanvas.Changed += OnCurveChanged;
            Border frame = new Border();
            frame.BorderThickness = new Thickness(1);
            frame.BorderBrush = Line;
            frame.CornerRadius = new CornerRadius(4);
            frame.ClipToBounds = true;
            frame.Child = _curveCanvas;
            top.Children.Add(frame);

            StackPanel side = new StackPanel();
            side.Margin = new Thickness(12, 0, 0, 0);
            TextBlock legendG = MonoText("γ  伽马曲线", 10, new SolidColorBrush(Color.FromRgb(0x3C, 0xD2, 0xF5)));
            TextBlock legendS = MonoText("S  饱和度曲线", 10, new SolidColorBrush(Color.FromRgb(0xFF, 0xAD, 0x4A)));
            side.Children.Add(legendG);
            side.Children.Add(legendS);

            _curvePointText = new TextBlock();
            _curvePointText.FontFamily = Mono;
            _curvePointText.FontSize = 11;
            _curvePointText.Foreground = Ink;
            _curvePointText.Margin = new Thickness(0, 12, 0, 0);
            _curvePointText.TextWrapping = TextWrapping.Wrap;
            side.Children.Add(_curvePointText);

            TextBlock hint = new TextBlock();
            hint.Text = "左键拖节点改数值；双击空白加点；右键删点（至少 3 个）。";
            hint.TextWrapping = TextWrapping.Wrap;
            hint.FontSize = 9.5;
            hint.Foreground = Muted;
            hint.Margin = new Thickness(0, 8, 0, 0);
            side.Children.Add(hint);

            side.Children.Add(Labeled("自动补色强度", AdvNumber(
                (int)Math.Round(_cfg.GameBrightnessCompensationBoost * 100), 0, 50,
                delegate(int v)
                {
                    _cfg.GameBrightnessCompensationBoost = v / 100.0;
                    if (_curveCanvas != null) _curveCanvas.CompensationBoost = _cfg.GameBrightnessCompensationBoost;
                    TouchSave();
                })));

            _curveStatus = new TextBlock();
            _curveStatus.FontSize = 9.5;
            _curveStatus.Foreground = Muted;
            _curveStatus.TextWrapping = TextWrapping.Wrap;
            _curveStatus.Margin = new Thickness(0, 10, 0, 0);
            _curveStatus.Text = "改动停止 500ms 后自动保存。";
            side.Children.Add(_curveStatus);

            Grid.SetColumn(side, 2);
            top.Children.Add(side);
            body.Children.Add(top);

            // 预设行
            StackPanel presetRow = new StackPanel();
            presetRow.Orientation = Orientation.Horizontal;
            presetRow.Margin = new Thickness(0, 12, 0, 0);
            _presetCombo = new VigilSelect(PanelAlt, Line, Ink, Accent, 260);
            _presetNames.Clear();
            _presetCombo.Items.Clear();
            foreach (GameBrightnessPreset p in GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets))
            {
                _presetNames.Add(p.Name);
                _presetCombo.Items.Add(p.Name + "  γ" + p.Gamma + " / S" + p.Saturation);
            }
            _presetNames.Add("自定义调整");
            _presetCombo.Items.Add("自定义调整");

            _curveLoading = true;
            int select = _presetCombo.Items.Count - 1;
            if (!string.IsNullOrEmpty(_cfg.GameBrightnessPresetCurrent))
            {
                for (int i = 0; i < _presetCombo.Items.Count; i++)
                {
                    if (_presetNames[i] == _cfg.GameBrightnessPresetCurrent) { select = i; break; }
                }
            }
            _presetCombo.SelectedIndex = select;
            _curveLoading = false;
            _presetCombo.SelectionChanged += OnPresetChanged;
            presetRow.Children.Add(_presetCombo);

            Button savePreset = CommandButton("\uE74E", "存为预设", delegate { SaveAsPreset(); }, Sub);
            savePreset.Margin = new Thickness(8, 0, 0, 0);
            presetRow.Children.Add(savePreset);
            Button delPreset = CommandButton("\uE74D", "删除预设", delegate { DeletePreset(); }, Muted);
            delPreset.Margin = new Thickness(8, 0, 0, 0);
            presetRow.Children.Add(delPreset);
            body.Children.Add(presetRow);

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE710", "导入…", delegate { ImportCurve(); }, Sub));
            actions.Children.Add(CommandButton("\uE792", "导出…", delegate { ExportCurve(); }, Sub));
            actions.Children.Add(CommandButton("\uE74E", "立即保存", delegate { SaveCurve(true); }, Accent));
            body.Children.Add(actions);

            AddPanelCard(_curvePanel, card);
        }

        private ComboBoxItem PresetRow(string name, string gamma, string sat)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Tag = name;
            item.Padding = new Thickness(4, 3, 6, 3);
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            BrightnessCurveThumb thumb = new BrightnessCurveThumb();
            thumb.Width = 64;
            thumb.Height = 22;
            thumb.Margin = new Thickness(0, 0, 8, 0);
            thumb.GammaText = gamma ?? "";
            thumb.SatText = sat ?? "";
            sp.Children.Add(thumb);
            TextBlock t = new TextBlock();
            t.Text = name;
            t.FontSize = 12;
            t.Foreground = Ink;
            t.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(t);
            item.Content = sp;
            return item;
        }

        private string PresetNameAt(int index)
        {
            if (index < 0 || index >= _presetNames.Count) return "";
            return _presetNames[index];
        }

        private string SelectedPresetName()
        {
            if (_presetCombo == null) return "";
            return PresetNameAt(_presetCombo.SelectedIndex);
        }

        private void SelectPresetByName(string name)
        {
            if (_presetCombo == null) return;
            for (int i = 0; i < _presetCombo.Items.Count; i++)
            {
                if (PresetNameAt(i) == name) { _presetCombo.SelectedIndex = i; return; }
            }
            _presetCombo.SelectedIndex = _presetCombo.Items.Count - 1;
        }

        private void AddPanelCard(StackPanel panel, UIElement card)
        {
            FrameworkElement fe = card as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(card);
        }

        private FrameworkElement Labeled(string label, FrameworkElement control)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 10, 0, 0);
            TextBlock t = new TextBlock();
            t.Text = label;
            t.FontSize = 10.5;
            t.Foreground = Sub;
            sp.Children.Add(t);
            sp.Children.Add(control);
            return sp;
        }

        private void LoadCurvePoints()
        {
            List<BrightnessPoint> g = GameBrightnessProfile.Parse(_cfg.GameBrightnessProfile);
            List<BrightnessPoint> s = GameBrightnessProfile.Parse(_cfg.GameBrightnessSaturationProfile);
            if (g.Count == 0)
            {
                // 需求一.2：没有存档就自动建「预设 1」
                g = DefaultCurvePoints();
                s = DefaultCurvePoints();
                _cfg.GameBrightnessProfile = GameBrightnessProfile.Serialize(g);
                _cfg.GameBrightnessSaturationProfile = GameBrightnessProfile.Serialize(s.Select(
                    delegate(BrightnessPoint p) { return new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation }; }));
                EnsurePreset("预设 1", g, s);
                _cfg.GameBrightnessPresetCurrent = "预设 1";
                TouchSave();
            }
            _curvePoints = new List<BrightnessPoint>();
            foreach (BrightnessPoint p in g)
            {
                BrightnessPoint sat = s.Find(delegate(BrightnessPoint q) { return q.Luma == p.Luma; });
                _curvePoints.Add(new BrightnessPoint
                {
                    Luma = p.Luma,
                    Gamma = p.Gamma,
                    Saturation = sat == null ? GameBrightnessProfile.AutoSaturation(p.Gamma, _cfg.GameBrightnessCompensationBoost) : sat.Gamma
                });
            }
        }

        private static List<BrightnessPoint> DefaultCurvePoints()
        {
            List<BrightnessPoint> list = new List<BrightnessPoint>();
            for (int i = 0; i < 5; i++)
                list.Add(new BrightnessPoint { Luma = i * 255 / 4, Gamma = 0.86, Saturation = 1.05 });
            return list;
        }

        private void EnsurePreset(string name, List<BrightnessPoint> g, List<BrightnessPoint> s)
        {
            GameBrightnessPresetStore.Put(_cfg, name,
                GameBrightnessProfile.Serialize(g),
                GameBrightnessProfile.Serialize(s.Select(delegate(BrightnessPoint p) { return new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation }; })));
        }

        private void OnCurveChanged()
        {
            if (_curveLoading) return;
            UpdatePointText();
            _curveStatus.Text = "正在保存…";
            if (_brightnessSaveTimer == null)
            {
                _brightnessSaveTimer = new DispatcherTimer();
                _brightnessSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
                _brightnessSaveTimer.Tick += delegate
                {
                    _brightnessSaveTimer.Stop();
                    SaveCurve(false);
                };
            }
            _brightnessSaveTimer.Stop();
            _brightnessSaveTimer.Start();
        }

        private void UpdatePointText()
        {
            if (_curveCanvas == null || _curvePointText == null) return;
            BrightnessPoint p = _curveCanvas.SelectedPoint();
            if (p == null)
            {
                _curvePointText.Text = "未选中节点\n共 " + _curveCanvas.Points.Count + " 个";
                return;
            }
            _curvePointText.Text = "亮度 " + p.Luma + "\nγ " + p.Gamma.ToString("0.00", CultureInfo.InvariantCulture)
                + "　饱和度 " + p.Saturation.ToString("0.00", CultureInfo.InvariantCulture)
                + (_cfg.GameBrightnessCompensationBoost > 0 ? "\n补色增强 " + (_cfg.GameBrightnessCompensationBoost * 100).ToString("0") + "%" : "");
        }

        private void SaveCurve(bool explicitSave)
        {
            if (_curvePoints == null || _curvePoints.Count < 3) return;
            List<BrightnessPoint> ordered = new List<BrightnessPoint>(_curvePoints);
            ordered.Sort(delegate(BrightnessPoint a, BrightnessPoint b) { return a.Luma.CompareTo(b.Luma); });
            _cfg.GameBrightnessProfile = GameBrightnessProfile.Serialize(ordered);
            _cfg.GameBrightnessSaturationProfile = GameBrightnessProfile.Serialize(ordered.Select(
                delegate(BrightnessPoint p) { return new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation }; }));

            // 需求一.2：编辑当前预设时自动写回该预设；自定义调整只写当前曲线
            string name = SelectedPresetName();
            if (name == "自定义调整") name = null;
            if (!string.IsNullOrEmpty(name))
            {
                EnsurePreset(name, ordered, ordered);
                _cfg.GameBrightnessPresetCurrent = name;
            }
            else
            {
                _cfg.GameBrightnessPresetCurrent = "";
            }
            TouchSave();
            _curveStatus.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss")
                + (explicitSave ? "（手动）" : "（自动）");
        }

        private void OnPresetChanged(object sender, EventArgs e)
        {
            if (_curveComboReady == false || _presetCombo == null) return;
            if (_curveLoading) return;
            string name = SelectedPresetName();
            if (name == "自定义调整") name = null;
            if (string.IsNullOrEmpty(name))
            {
                _cfg.GameBrightnessPresetCurrent = "";
                TouchSave();
                return;
            }
            GameBrightnessPreset preset = GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets)
                .Find(delegate(GameBrightnessPreset p) { return p.Name == name; });
            if (preset == null) return;
            List<BrightnessPoint> g = GameBrightnessProfile.Parse(preset.Gamma);
            List<BrightnessPoint> s = GameBrightnessProfile.Parse(preset.Saturation);
            if (g.Count < 3) return;
            _curveLoading = true;
            try
            {
                _curvePoints.Clear();
                foreach (BrightnessPoint p in g)
                {
                    BrightnessPoint sat = s.Find(delegate(BrightnessPoint q) { return q.Luma == p.Luma; });
                    _curvePoints.Add(new BrightnessPoint
                    {
                        Luma = p.Luma,
                        Gamma = p.Gamma,
                        Saturation = sat == null ? 1.07 : sat.Gamma
                    });
                }
                if (_curveCanvas != null)
                {
                    _curveCanvas.Points = _curvePoints;
                    _curveCanvas.SelectedLuma = -1;
                    _curveCanvas.InvalidateVisual();
                }
                _cfg.GameBrightnessPresetCurrent = name;
                SaveCurve(false);
            }
            finally { _curveLoading = false; }
        }

        private bool _curveComboReady = true;

        private void SaveAsPreset()
        {
            string name = "预设 " + (GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets).Count + 1).ToString();
            // 简单命名：不弹输入框，直接按序号；用户可在 config.ini 里改名
            EnsurePreset(name, _curvePoints, _curvePoints);
            _presetNames.Insert(Math.Max(0, _presetNames.Count - 1), name);
            _presetCombo.Items.Insert(Math.Max(0, _presetCombo.Items.Count - 1), name);
            SelectPresetByName(name);
            SaveCurve(true);
        }

        private void DeletePreset()
        {
            string name = SelectedPresetName();
            if (string.IsNullOrEmpty(name) || name == "自定义调整") return;

            List<GameBrightnessPreset> rows = GameBrightnessPresetStore.Parse(_cfg.GameBrightnessPresets);
            int idx = rows.FindIndex(delegate(GameBrightnessPreset p) { return p.Name == name; });
            if (idx >= 0) rows.RemoveAt(idx);
            _cfg.GameBrightnessPresets = GameBrightnessPresetStore.Serialize(rows);
            if (_cfg.GameBrightnessPresetCurrent == name) _cfg.GameBrightnessPresetCurrent = "";
            TouchSave();

            _presetNames.Clear();
            _presetCombo.Items.Clear();
            foreach (GameBrightnessPreset p in rows)
            {
                _presetNames.Add(p.Name);
                _presetCombo.Items.Add(p.Name + "  γ" + p.Gamma + " / S" + p.Saturation);
            }
            _presetNames.Add("自定义调整");
            _presetCombo.Items.Add("自定义调整");
            _presetCombo.SelectedIndex = _presetCombo.Items.Count - 1;
        }

        private void ExportCurve()
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "亮度曲线 (*.brightness)|*.brightness|文本 (*.txt)|*.txt";
            dlg.FileName = "亮度曲线-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("name=" + SelectedPresetName());
                sb.AppendLine("gamma=" + GameBrightnessProfile.Serialize(_curvePoints));
                List<BrightnessPoint> sat = new List<BrightnessPoint>();
                foreach (BrightnessPoint p in _curvePoints)
                    sat.Add(new BrightnessPoint { Luma = p.Luma, Gamma = p.Saturation });
                sb.AppendLine("saturation=" + GameBrightnessProfile.Serialize(sat));
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(false));
                _curveStatus.Text = "已导出：" + dlg.FileName;
            }
            catch (Exception ex) { _curveStatus.Text = "导出失败：" + ex.Message; }
        }

        private void ImportCurve()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "亮度曲线 (*.brightness;*.txt)|*.brightness;*.txt|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string[] lines = File.ReadAllLines(dlg.FileName);
                string gamma = null, sat = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("gamma=", StringComparison.OrdinalIgnoreCase)) gamma = line.Substring(6);
                    else if (line.StartsWith("saturation=", StringComparison.OrdinalIgnoreCase)) sat = line.Substring(11);
                }
                List<BrightnessPoint> g = GameBrightnessProfile.Parse(gamma);
                List<BrightnessPoint> s = GameBrightnessProfile.Parse(sat);
                if (g.Count < 3) { _curveStatus.Text = "导入失败：伽马曲线少于 3 个点"; return; }
                _curvePoints.Clear();
                foreach (BrightnessPoint p in g)
                {
                    BrightnessPoint q = s.Find(delegate(BrightnessPoint x) { return x.Luma == p.Luma; });
                    _curvePoints.Add(new BrightnessPoint
                    {
                        Luma = p.Luma,
                        Gamma = p.Gamma,
                        Saturation = q == null ? GameBrightnessProfile.AutoSaturation(p.Gamma, _cfg.GameBrightnessCompensationBoost) : q.Gamma
                    });
                }
                _curveCanvas.Points = _curvePoints;
                _curveCanvas.SelectedLuma = -1;
                _curveCanvas.InvalidateVisual();
                SaveCurve(true);
                _curveStatus.Text = "已导入 " + g.Count + " 个节点：" + dlg.FileName;
            }
            catch (Exception ex) { _curveStatus.Text = "导入失败：" + ex.Message; }
        }
    }
}
