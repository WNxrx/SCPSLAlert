using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>
    /// 导出前的一图流预览 / 纠错窗口。
    /// 左边看图，右边改事件（改名 / 删除）和角色名；改动只存注释，不改原始对局。
    /// </summary>
    internal sealed class WpfOneMapWindow : Window
    {
        private readonly Config _cfg;
        private MatchRecord _match;
        private MatchAnnotations _ann;
        private bool _light;

        private Image _image;
        private Image _normalImage;
        private StackPanel _eventList;
        private TextBlock _status;
        private TabControl _tabs;
        private OneMapOptions _opt;
        private bool _dirty;
        private readonly Dictionary<string, Border> _rows = new Dictionary<string, Border>();
        private string _selectedKey = "";
        private CheckBox _cbOneMap;
        private CheckBox _cbNormal;
        private CheckBox _cbSplit;
        private CheckBox _cbTxt;
        private VigilSelect _roundCombo;
        private CheckBox _cbSmooth;
        private CheckBox _cbLabels;
        private CheckBox _cbExtra;
        private VigilSelect _matchCombo;
        private VigilSelect _viewCombo;
        private bool _viewLatest = true;
        private Border _editHost;
        private MatchRecord _lastView;
        private List<MatchRecord> _recent;
        private bool _switchingMatch;
        private Popup _editPopup;
        private Func<MatchRecord> _liveProvider;
        private TextBlock _liveChip;

        private static readonly FontFamily UiFont = UiFonts.Body;

        public WpfOneMapWindow(Config cfg, MatchRecord match, bool light, Func<MatchRecord> liveProvider = null)
        {
            _liveProvider = liveProvider;
            _cfg = cfg ?? new Config();
            _match = match;
            _light = light;
            Title = "一图流  //  SCPSLAlert · A.R.C.T";
            Width = 1280;
            Height = 860;
            MinWidth = 980;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Bg();
            FontFamily = UiFont;
            Foreground = Ink();

            _ann = AskAnnotations();
            _recent = MatchStore.ListRecent(20);
            if (_recent.Count == 0 && _match != null) _recent.Add(_match);
            else if (_match != null)
            {
                bool found = false;
                for (int i = 0; i < _recent.Count; i++) if (_recent[i].Id == _match.Id) { found = true; break; }
                if (!found) _recent.Insert(0, _match);
            }

            Build();
            Render();
            if (_liveProvider != null && _liveChip != null)
            {
                try
                {
                    MatchRecord liveNow = _liveProvider();
                    bool same = liveNow != null && _match != null && liveNow.Id == _match.Id;
                    _liveChip.Text = same ? "● 进行中" : "○ 已结束";
                }
                catch { }
            }
        }

        private MatchAnnotations AskAnnotations()
        {
            if (_match == null || string.IsNullOrEmpty(_match.Id)) return new MatchAnnotations();
            string path = MatchAnnotations.PathFor(_match.Id);
            if (!File.Exists(path)) return MatchAnnotations.Load(_match.Id);

            MessageBoxResult r = MessageBox.Show(this,
                "这局有上次保存的纠正，是否应用？\n\n" + path,
                "一图流", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) return MatchAnnotations.Load(_match.Id);

            MatchAnnotations empty = new MatchAnnotations();
            empty.MatchId = _match.Id;
            return empty;
        }

        // ---------------- 配色 ----------------
        private Color BgColor() { return _light ? C(242, 245, 247) : C(7, 11, 16); }
        private Color PanelColor() { return _light ? C(255, 255, 255) : C(12, 18, 25); }
        private Color LineColor() { return _light ? C(214, 223, 229) : C(27, 43, 54); }
        private Color InkColor() { return _light ? C(10, 23, 32) : C(230, 244, 248); }
        private Color SubColor() { return _light ? C(74, 93, 108) : C(154, 176, 188); }
        private Color AccentColor() { return _light ? C(0, 132, 163) : C(56, 214, 255); }
        private Brush Bg() { return B(BgColor()); }
        private Brush Ink() { return B(InkColor()); }
        private Brush Sub() { return B(SubColor()); }
        private Brush Accent() { return B(AccentColor()); }
        private Brush Line() { return B(LineColor()); }
        private Brush Panel() { return B(PanelColor()); }

        // ---------------- 布局 ----------------
        private void Build()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            root.RowDefinitions.Add(new RowDefinition());
            Content = root;

            root.Children.Add(Header());

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            _tabs = new TabControl();
            _tabs.Background = Panel();
            _tabs.BorderBrush = Line();
            _tabs.Margin = new Thickness(10, 0, 0, 10);
            TabItem one = new TabItem();
            one.Header = "一图流";
            ScrollViewer sv = new ScrollViewer();
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _image = new Image();
            _image.Stretch = Stretch.None;
            _image.HorizontalAlignment = HorizontalAlignment.Left;
            _image.VerticalAlignment = VerticalAlignment.Top;
            _image.MouseLeftButtonDown += OnImageClick;
            sv.Content = _image;
            one.Content = sv;

            TabItem two = new TabItem();
            two.Header = "普通图";
            ScrollViewer sv2 = new ScrollViewer();
            sv2.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv2.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _normalImage = new Image();
            _normalImage.Stretch = Stretch.None;
            _normalImage.HorizontalAlignment = HorizontalAlignment.Left;
            _normalImage.VerticalAlignment = VerticalAlignment.Top;
            sv2.Content = _normalImage;
            two.Content = sv2;

            _tabs.Items.Add(one);
            _tabs.Items.Add(two);
            _tabs.SelectedIndex = _cfg.ExportDefaultTab == 0 ? 1 : 0;
            body.Children.Add(_tabs);

            body.Children.Add(SidePanel());
        }

        private UIElement Header()
        {
            Border bar = new Border();
            bar.Background = Panel();
            bar.BorderBrush = Line();
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = g;

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.Margin = new Thickness(14, 0, 0, 0);
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock title = new TextBlock();
            title.Text = "一图流  /  导出前纠正";
            title.FontSize = 14;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = Ink();
            title.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(title);

            _matchCombo = new VigilSelect(Panel(), Line(), Ink(), Accent(), 290);
            _matchCombo.Margin = new Thickness(14, 0, 0, 0);
            _matchCombo.VerticalAlignment = VerticalAlignment.Center;
            if (_recent != null)
            {
                for (int i = 0; i < _recent.Count; i++)
                {
                    MatchRecord rm = _recent[i];
                    _matchCombo.Items.Add(rm.Start.ToString("MM-dd HH:mm") + "  ·  " + rm.Rounds.Count + " 回合  ·  " + rm.Id);
                    if (_match != null && rm.Id == _match.Id) _matchCombo.SelectedIndex = i;
                }
            }
            _matchCombo.SelectionChanged += delegate
            {
                if (_switchingMatch) return;
                SwitchMatch(_matchCombo.SelectedIndex);
            };
            left.Children.Add(_matchCombo);

            _viewCombo = new VigilSelect(Panel(), Line(), Ink(), Accent(), 120);
            _viewCombo.Margin = new Thickness(8, 0, 0, 0);
            _viewCombo.VerticalAlignment = VerticalAlignment.Center;
            _viewCombo.Items.Add("仅最近回合");
            _viewCombo.Items.Add("全部回合");
            _viewCombo.SelectedIndex = 1;
            _viewCombo.SelectionChanged += delegate
            {
                _viewLatest = _viewCombo.SelectedIndex == 0;
                Render();
            };
            left.Children.Add(_viewCombo);

            _status = new TextBlock();
            _status.FontSize = 10.5;
            _status.Foreground = Sub();
            _status.Margin = new Thickness(14, 0, 0, 0);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Text = _match == null ? "没有对局" : ("对局 " + _match.Id + "  ·  " + _match.Rounds.Count + " 回合");
            left.Children.Add(_status);
            g.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 10, 0);
            if (_liveProvider != null)
            {
                _liveChip = new TextBlock();
                _liveChip.Text = "● 进行中";
                _liveChip.FontSize = 10;
                _liveChip.Foreground = Accent();
                _liveChip.VerticalAlignment = VerticalAlignment.Center;
                _liveChip.Margin = new Thickness(10, 0, 6, 0);
                right.Children.Add(_liveChip);
                right.Children.Add(Btn("刷新", delegate { RefreshLive(); }));
            }
            right.Children.Add(Btn(_light ? "深色预览" : "浅色预览", delegate
            {
                _light = !_light;
                Background = Bg();
                Foreground = Ink();
                Build();
                Render();
            }));
            right.Children.Add(Btn("重置纠正", delegate
            {
                _ann.DeletedEvents.Clear();
                _ann.EventLabels.Clear();
                _ann.RoleNames.Clear();
                _ann.HiddenScps.Clear();
                _ann.Save();
                RebuildSide();
                Render();
            }));
            right.Children.Add(Btn("导出…", delegate { Export(); }, true));
            gridRight(g, right);
            return bar;
        }

        private void gridRight(Grid g, UIElement e)
        {
            Grid.SetColumn(e, 1);
            g.Children.Add(e);
        }

        private UIElement SidePanel()
        {
            Border p = new Border();
            p.Background = Panel();
            p.BorderBrush = Line();
            p.BorderThickness = new Thickness(1, 0, 0, 0);
            p.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetColumn(p, 1);

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            p.Child = g;

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(12, 10, 12, 6);
            TextBlock h = new TextBlock();
            h.Text = "事件（可改名 / 删除）";
            h.FontSize = 11;
            h.Foreground = Accent();
            h.FontWeight = FontWeights.Bold;
            head.Children.Add(h);
            TextBlock tip = new TextBlock();
            tip.Text = "改动只存注释，不改原始对局";
            tip.FontSize = 9.5;
            tip.Foreground = Sub();
            tip.Margin = new Thickness(0, 3, 0, 0);
            head.Children.Add(tip);
            g.Children.Add(head);

            UIElement opt = ExportOptions();
            Grid.SetRow(opt, 1);
            g.Children.Add(opt);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _eventList = new StackPanel();
            _eventList.Margin = new Thickness(10, 0, 10, 10);
            sv.Content = _eventList;
            Grid.SetRow(sv, 2);
            g.Children.Add(sv);

            RebuildSide();
            return p;
        }

        private void RebuildEventList()
        {
            if (_eventList == null) return;
            _eventList.Children.Clear();
            if (_match == null) return;

            for (int r = 0; r < _match.Rounds.Count; r++)
            {
                MatchRound round = _match.Rounds[r];
                TextBlock rh = new TextBlock();
                rh.Text = "回合 " + round.Index;
                rh.FontSize = 10;
                rh.Foreground = Accent();
                rh.Margin = new Thickness(0, 8, 0, 4);
                _eventList.Children.Add(rh);

                for (int i = 0; i < round.Events.Count; i++)
                {
                    int ri = r, ei = i;
                    MatchEvent e = round.Events[i];
                    string key = round.Index + ":" + i;
                    if (_ann.DeletedEvents.Contains(key)) continue;

                    Grid row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                    TextBlock time = new TextBlock();
                    time.Text = (e.At - round.Start).TotalSeconds.ToString("0") + "s";
                    time.FontSize = 9;
                    time.Foreground = Sub();
                    time.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(time);

                    TextBox box = new TextBox();
                    box.Text = e.Label ?? "";
                    box.FontSize = 10.5;
                    box.Background = B(_light ? C(247, 249, 250) : C(16, 26, 36));
                    box.Foreground = Ink();
                    box.BorderBrush = Line();
                    box.Padding = new Thickness(4, 2, 4, 2);
                    box.Tag = key;
                    box.LostFocus += delegate
                    {
                        string k = (string)box.Tag;
                        if (string.Equals(box.Text, OriginalLabel(ri, ei), StringComparison.Ordinal))
                            _ann.EventLabels.Remove(k);
                        else
                            _ann.EventLabels[k] = box.Text;
                        _ann.Save();
                        _dirty = true;
                        Render();
                    };
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);

                    Button del = SmallButton("×");
                    del.Tag = key;
                    del.Click += delegate
                    {
                        _ann.DeletedEvents.Add((string)del.Tag);
                        _ann.Save();
                        _dirty = true;
                        RebuildSide();
                        Render();
                    };
                    Grid.SetColumn(del, 2);
                    row.Children.Add(del);

                    row.Margin = new Thickness(0, 0, 0, 2);
                    _eventList.Children.Add(row);
                }
            }
        }

        private void OnImageClick(object sender, MouseButtonEventArgs e)
        {
            if (_image == null || _image.Source == null || _match == null) return;
            try
            {
                Point p = e.GetPosition(_image);
                OneMapOptions opt = _opt ?? BuildOptions();
                MatchRecord src = _lastView ?? _match;
                OneMapRenderer.OneMapHit hit = OneMapRenderer.HitTest(src, opt, p.X, p.Y);
                if (hit.Round < 0) return;
                int roundNo = src.Rounds[hit.Round].Index;
                int origRound = -1;
                for (int i = 0; i < _match.Rounds.Count; i++)
                    if (_match.Rounds[i].Index == roundNo) { origRound = i; break; }
                if (origRound < 0) return;
                if (hit.Kind == "event")
                {
                    SelectRow(roundNo + ":" + hit.Index);
                    EditEvent(_match.Rounds[origRound], hit.Index);
                }
                else if (hit.Kind == "role")
                {
                    SelectRow("role:" + roundNo + ":" + hit.Index);
                    EditRole(_match.Rounds[origRound], hit.Index);
                }
                else if (hit.Kind == "scp") SelectRow("scp:" + roundNo + ":" + hit.Index);
            }
            catch { }
        }

        private void SelectRow(string key)
        {
            _selectedKey = key == null ? "" : key;
            foreach (KeyValuePair<string, Border> pair in _rows)
            {
                pair.Value.Background = pair.Key == _selectedKey
                    ? B(_light ? C(224, 240, 246) : C(22, 44, 56))
                    : Brushes.Transparent;
            }
            Border row;
            if (_rows.TryGetValue(_selectedKey, out row) && row != null)
                row.BringIntoView();
        }

        private TextBlock Section(string text)
        {
            TextBlock h = new TextBlock();
            h.Text = text;
            h.FontSize = 10;
            h.Foreground = Accent();
            h.Margin = new Thickness(0, 10, 0, 5);
            return h;
        }

        private UIElement ExportOptions()
        {
            StackPanel box = new StackPanel();
            box.Margin = new Thickness(12, 0, 12, 8);

            TextBlock h = new TextBlock();
            h.Text = "导出";
            h.FontSize = 10;
            h.Foreground = Accent();
            h.Margin = new Thickness(0, 4, 0, 4);
            box.Children.Add(h);

            _roundCombo = new VigilSelect(Panel(), Line(), Ink(), Accent(), 200);
            _roundCombo.Margin = new Thickness(0, 2, 0, 4);
            _roundCombo.Items.Add("全部回合");
            if (_match != null)
                for (int i = 0; i < _match.Rounds.Count; i++)
                    _roundCombo.Items.Add("回合 " + _match.Rounds[i].Index.ToString("00"));
            _roundCombo.SelectedIndex = 0;
            box.Children.Add(_roundCombo);

            _cbOneMap = Check("一图流", true);
            _cbNormal = Check("普通图", true);
            _cbSplit = Check("按对局分割", _cfg.ExportSplitByMatch);
            _cbTxt = Check("附带 txt 解读", _cfg.ExportTxtReadout);
            _cbExtra = Check("其他持续事件（净化/支援/发电机）", _cfg.OneMapExtraDurations);
            _cbExtra.Checked += delegate { Render(); };
            _cbExtra.Unchecked += delegate { Render(); };
            _cbLabels = Check("曲线数字标注", _cfg.OneMapLabels > 0);
            _cbLabels.Checked += delegate { Render(); };
            _cbLabels.Unchecked += delegate { Render(); };
            _cbSmooth = Check("FPS 平滑（默认原始）", _cfg.OneMapFpsSmooth);
            _cbSmooth.Checked += delegate { Render(); };
            _cbSmooth.Unchecked += delegate { Render(); };
            box.Children.Add(_cbOneMap);
            box.Children.Add(_cbNormal);
            box.Children.Add(_cbSplit);
            box.Children.Add(_cbTxt);
            box.Children.Add(_cbExtra);
            box.Children.Add(_cbLabels);
            box.Children.Add(_cbSmooth);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.Margin = new Thickness(0, 6, 0, 0);
            Button one = Btn("当前对局…", delegate { Export(); }, true);
            one.Margin = new Thickness(0, 0, 6, 0);
            buttons.Children.Add(one);
            buttons.Children.Add(Btn("最近 10 局", delegate { ExportRecent(); }));
            box.Children.Add(buttons);
            return box;
        }

        private CheckBox Check(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Content = text;
            c.IsChecked = value;
            c.FontSize = 10;
            c.Foreground = Ink();
            c.Margin = new Thickness(0, 1, 0, 1);
            return c;
        }

        private bool On(CheckBox c) { return c != null && c.IsChecked == true; }

        private OneMapExportRequest BuildRequest(bool split, string dir, string baseName)
        {
            OneMapExportRequest req = new OneMapExportRequest();
            req.OneMap = On(_cbOneMap);
            req.NormalChart = On(_cbNormal);
            req.Txt = On(_cbTxt);
            req.SplitByMatch = split;
            req.PartMaxPx = Math.Max(1000, _cfg.ExportPartMaxPx);
            req.Dir = dir;
            req.FileBase = baseName;
            return req;
        }

        /// <summary>导出时只保留下拉里选中的那一回合；0 = 全部。</summary>
        private MatchRecord LimitRounds(MatchRecord m)
        {
            if (m == null || _roundCombo == null || _roundCombo.SelectedIndex <= 0) return m;
            int pick = _roundCombo.SelectedIndex - 1;
            if (pick < 0 || pick >= m.Rounds.Count) return m;
            MatchRecord one = m.Clone();
            MatchRound keep = one.Rounds[pick];
            one.Rounds.Clear();
            one.Rounds.Add(keep);
            return one;
        }
        private void ExportRecent()
        {
            try
            {
                List<MatchRecord> list = MatchStore.ListRecent(10);
                if (list.Count == 0)
                {
                    MessageBox.Show(this, "还没有对局记录。", "一图流");
                    return;
                }
                string dir = OneMapExport.DefaultDir(_cfg.ExportDir);
                OneMapExportRequest req = BuildRequest(On(_cbSplit), dir, "");
                for (int i = 0; i < list.Count; i++) list[i] = LimitRounds(list[i]);
                OneMapOptions opt = BuildOptions();
                List<string> files = OneMapExport.ExportMany(list, opt,
                    OneMapRenderer.OneMapPalette.For(_light), req, 144);
                MessageBox.Show(this, "已导出 " + files.Count + " 个文件到：\n" + dir, "一图流");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "一图流");
            }
        }
        private void RefreshLive()
        {
            try
            {
                if (_liveProvider == null) return;
                MatchRecord live = _liveProvider();
                if (live == null)
                {
                    if (_status != null) _status.Text = "当前没有进行中的对局（显示的是已保存记录）";
                    if (_liveChip != null) _liveChip.Text = "○ 已结束";
                    return;
                }
                if (_dirty) { _ann.Save(); _dirty = false; }
                bool same = _match != null && _match.Id == live.Id;
                _match = live;
                if (_recent != null)
                {
                    if (same && _recent.Count > 0 && _recent[0].Id == live.Id) _recent[0] = live;
                    else _recent.Insert(0, live);
                }
                RebuildSide();
                Render();
                if (_status != null)
                    _status.Text = "对局 " + _match.Id + "  ·  " + _match.Rounds.Count + " 回合  ·  进行中";
                if (_liveChip != null) _liveChip.Text = "● 进行中";
            }
            catch { }
        }
        /// <summary>视图过滤：默认只画最近一回合，可切回全部。</summary>
        private MatchRecord ViewMatch(MatchRecord m)
        {
            if (m == null || !_viewLatest || m.Rounds.Count <= 1) return m;
            MatchRecord one = m.Clone();
            MatchRound keep = one.Rounds[one.Rounds.Count - 1];
            one.Rounds.Clear();
            one.Rounds.Add(keep);
            return one;
        }
        private void SwitchMatch(int index)
        {
            if (index < 0 || index >= _recent.Count || _recent[index] == null) return;
            if (_recent[index].Id == _match.Id) return;
            try
            {
                if (_dirty) { _ann.Save(); _dirty = false; }
                _switchingMatch = true;
                _match = _recent[index];
                _ann = MatchAnnotations.Load(_match.Id);
                _selectedKey = "";
                RebuildSide();
                Render();
                if (_status != null)
                    _status.Text = "对局 " + _match.Id + "  ·  " + _match.Rounds.Count + " 回合";
            }
            finally { _switchingMatch = false; }
        }

        private void EditEvent(MatchRound round, int index)
        {
            if (round == null || index < 0 || index >= round.Events.Count) return;
            MatchEvent e = round.Events[index];
            string key = round.Index + ":" + index;

            TextBox box = EditBox(e.Label);
            StackPanel panel = EditorPanel("事件  " + (e.At - round.Start).TotalSeconds.ToString("0") + "s", box);

            Button save = SmallBtn("保存", true);
            save.Click += delegate
            {
                string now = box.Text;
                if (string.Equals(now, e.Label ?? "", StringComparison.Ordinal)) _ann.EventLabels.Remove(key);
                else _ann.EventLabels[key] = now;
                Done();
            };
            Button del = SmallBtn("删除", false);
            del.Click += delegate
            {
                _ann.DeletedEvents.Add(key);
                Done();
            };
            Button cancel = SmallBtn("取消", false);
            cancel.Click += delegate { if (_editPopup != null) _editPopup.IsOpen = false; };

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.Margin = new Thickness(0, 8, 0, 0);
            buttons.Children.Add(save);
            buttons.Children.Add(del);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            ShowPopup(panel, delegate
            {
                _ann.Save();
                _dirty = false;
                RebuildSide();
                Render();
            });
        }

        private void EditRole(MatchRound round, int index)
        {
            if (round == null || index < 0 || index >= round.Roles.Count) return;
            MatchRoleSpan span = round.Roles[index];
            string key = "role:" + round.Index + ":" + index;

            TextBox box = EditBox(string.IsNullOrEmpty(span.Role) ? "未识别" : span.Role);
            StackPanel panel = EditorPanel("角色段  " + Math.Max(0, (span.Start - round.Start).TotalSeconds).ToString("0") + "s",
                box);

            Button save = SmallBtn("保存", true);
            save.Click += delegate
            {
                string now = box.Text;
                if (string.Equals(now, span.Role ?? "", StringComparison.Ordinal) || now == "未识别")
                    _ann.RoleNames.Remove(key);
                else
                    _ann.RoleNames[key] = now;
                Done();
            };
            Button cancel = SmallBtn("取消", false);
            cancel.Click += delegate { if (_editPopup != null) _editPopup.IsOpen = false; };

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.Margin = new Thickness(0, 8, 0, 0);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            ShowPopup(panel, delegate
            {
                _ann.Save();
                _dirty = false;
                RebuildSide();
                Render();
            });
        }

        private void Done()
        {
            if (_editPopup != null) _editPopup.IsOpen = false;
        }

        private void ShowPopup(UIElement child, Action after)
        {
            Border host = new Border();
            host.Background = B(_light ? C(248, 250, 251) : C(14, 22, 30));
            host.BorderBrush = Line();
            host.BorderThickness = new Thickness(1);
            host.CornerRadius = new CornerRadius(4);
            host.Padding = new Thickness(10);
            host.Child = child;

            Popup pop = new Popup();
            pop.Child = host;
            pop.PlacementTarget = _image;
            pop.Placement = PlacementMode.MousePoint;
            pop.StaysOpen = true;
            pop.AllowsTransparency = true;
            _editHost = host;
            MouseButtonEventHandler outside = null;
            outside = delegate(object s, MouseButtonEventArgs e)
            {
                if (_editHost != null && IsUnder(e.OriginalSource as DependencyObject, _editHost)) return;
                if (_editPopup != null) _editPopup.IsOpen = false;
            };
            PreviewMouseDown += outside;
            pop.Closed += delegate
            {
                PreviewMouseDown -= outside;
                _editHost = null;
                try { after(); } catch { }
            };
            _editPopup = pop;
            pop.IsOpen = true;
        }

        private TextBox EditBox(string text)
        {
            TextBox box = new TextBox();
            box.Text = text ?? "";
            box.Width = 220;
            box.FontSize = 11;
            box.Foreground = Ink();
            box.Background = B(_light ? C(255, 255, 255) : C(16, 26, 36));
            box.BorderBrush = Line();
            box.Padding = new Thickness(4, 2, 4, 2);
            return box;
        }

        private StackPanel EditorPanel(string title, TextBox box)
        {
            StackPanel panel = new StackPanel();
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 9.5;
            t.Foreground = Sub();
            t.Margin = new Thickness(0, 0, 0, 5);
            panel.Children.Add(t);
            panel.Children.Add(box);
            return panel;
        }

        private static bool IsUnder(DependencyObject source, DependencyObject ancestor)
        {
            DependencyObject node = source;
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }
        private Button SmallBtn(string text, bool primary)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 10;
            b.Height = 24;
            b.Padding = new Thickness(10, 0, 10, 0);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Foreground = primary ? B(Colors.White) : Ink();
            b.Background = primary ? Accent() : B(_light ? C(237, 242, 245) : C(22, 32, 42));
            b.BorderBrush = primary ? Accent() : Line();
            b.BorderThickness = new Thickness(1);
            b.Cursor = Cursors.Hand;
            return b;
        }
        private void RebuildSide()
        {
            if (_eventList == null) return;
            _eventList.Children.Clear();
            _rows.Clear();
            if (_match == null) return;

            for (int r = 0; r < _match.Rounds.Count; r++)
            {
                MatchRound round = _match.Rounds[r];
                _eventList.Children.Add(Section("回合 " + round.Index + " · 事件"));

                for (int i = 0; i < round.Events.Count; i++)
                {
                    int ri = r, ei = i;
                    MatchEvent e = round.Events[i];
                    string key = round.Index + ":" + i;
                    if (_ann.DeletedEvents.Contains(key)) continue;

                    Grid row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

                    TextBlock time = new TextBlock();
                    time.Text = Math.Max(0, (e.At - round.Start).TotalSeconds).ToString("0") + "s";
                    time.FontSize = 9;
                    time.Foreground = Sub();
                    time.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(time);

                    TextBox box = new TextBox();
                    box.Text = e.Label ?? "";
                    box.FontSize = 10.5;
                    box.Background = B(_light ? C(247, 249, 250) : C(16, 26, 36));
                    box.Foreground = Ink();
                    box.BorderBrush = Line();
                    box.Padding = new Thickness(4, 2, 4, 2);
                    box.Tag = key;
                    box.LostFocus += delegate
                    {
                        string k = (string)box.Tag;
                        if (string.Equals(box.Text, OriginalLabel(ri, ei), StringComparison.Ordinal))
                            _ann.EventLabels.Remove(k);
                        else
                            _ann.EventLabels[k] = box.Text;
                        _ann.Save();
                        _dirty = true;
                        Render();
                    };
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);

                    Button del = SmallButton("×");
                    del.Tag = key;
                    del.Click += delegate
                    {
                        _ann.DeletedEvents.Add((string)del.Tag);
                        _ann.Save();
                        _dirty = true;
                        RebuildSide();
                        Render();
                    };
                    Grid.SetColumn(del, 2);
                    row.Children.Add(del);

                    Border wrap = new Border();
                    wrap.Child = row;
                    wrap.Padding = new Thickness(2, 1, 2, 1);
                    wrap.Margin = new Thickness(0, 0, 0, 1);
                    wrap.CornerRadius = new CornerRadius(2);
                    wrap.MouseLeftButtonDown += delegate { SelectRow(key); };
                    _rows[key] = wrap;
                    _eventList.Children.Add(wrap);
                }

                _eventList.Children.Add(Section("回合 " + round.Index + " · 角色段"));
                for (int i = 0; i < round.Roles.Count; i++)
                {
                    int ri = r, si = i;
                    MatchRoleSpan span = round.Roles[i];
                    string key = "role:" + round.Index + ":" + i;

                    Grid row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

                    TextBlock time = new TextBlock();
                    time.Text = Math.Max(0, (span.Start - round.Start).TotalSeconds).ToString("0") + "s";
                    time.FontSize = 9;
                    time.Foreground = Sub();
                    time.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(time);

                    TextBox box = new TextBox();
                    box.Text = string.IsNullOrEmpty(span.Role) ? "（未识别）" : span.Role;
                    box.FontSize = 10.5;
                    box.Background = B(_light ? C(247, 249, 250) : C(16, 26, 36));
                    box.Foreground = Ink();
                    box.BorderBrush = Line();
                    box.Padding = new Thickness(4, 2, 4, 2);
                    box.Tag = round.Index + ":" + i;
                    box.LostFocus += delegate
                    {
                        string k = (string)box.Tag;
                        string original = OriginalRole(ri, si);
                        string now = box.Text;
                        if (string.Equals(now, original, StringComparison.Ordinal)
                            || now == "（未识别）")
                            _ann.RoleNames.Remove(k);
                        else
                            _ann.RoleNames[k] = now;
                        _ann.Save();
                        _dirty = true;
                        Render();
                    };
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);

                    TextBlock state = new TextBlock();
                    state.Text = span.State == "dead" ? "死亡" : "";
                    state.FontSize = 9;
                    state.Foreground = Sub();
                    state.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(state, 2);
                    row.Children.Add(state);

                    Border wrap = new Border();
                    wrap.Child = row;
                    wrap.Padding = new Thickness(2, 1, 2, 1);
                    wrap.Margin = new Thickness(0, 0, 0, 1);
                    wrap.CornerRadius = new CornerRadius(2);
                    wrap.MouseLeftButtonDown += delegate { SelectRow(key); };
                    _rows[key] = wrap;
                    _eventList.Children.Add(wrap);
                }
            }
            if (_rows.ContainsKey(_selectedKey)) SelectRow(_selectedKey);
        }

        private string OriginalRole(int roundIdx, int spanIdx)
        {
            if (_match == null || roundIdx >= _match.Rounds.Count) return "";
            MatchRound r = _match.Rounds[roundIdx];
            return spanIdx < r.Roles.Count ? (r.Roles[spanIdx].Role ?? "") : "";
        }

        private OneMapOptions BuildOptions()
        {
            OneMapOptions o = new OneMapOptions();
            o.Light = _light;
            o.Lttb = Math.Max(200, _cfg.OneMapLttb);
            o.Labels = On(_cbLabels) ? Math.Max(1, _cfg.OneMapLabels > 0 ? _cfg.OneMapLabels : 10) : 0;
            o.ShowHeart = _cfg.OneMapShowHeart;
            o.ExportAt = DateTime.Now;
            o.Version = AppInfo.Version;
            o.ShowPlayerName = _cfg.ExportShowPlayer;
            o.SmoothFps = On(_cbSmooth);
            o.ShowExtraDurations = On(_cbExtra);
            o.PlayerName = Environment.UserName;
            return o;
        }
        private string OriginalLabel(int roundIdx, int eventIdx)
        {
            if (_match == null || roundIdx >= _match.Rounds.Count) return "";
            MatchRound r = _match.Rounds[roundIdx];
            return eventIdx < r.Events.Count ? (r.Events[eventIdx].Label ?? "") : "";
        }

        // ---------------- 渲染 ----------------
        private void Render()
        {
            try
            {
                _opt = BuildOptions();

                OneMapRenderer.OneMapPalette pal = OneMapRenderer.OneMapPalette.For(_light);
                MatchRecord applied = _ann.Apply(_match);
                _lastView = ViewMatch(applied);
                BitmapSource bmp = OneMapRenderer.Render(_lastView, _opt, pal);
                if (_image != null)
                {
                    _image.Source = bmp;
                    _image.Width = bmp.PixelWidth;
                    _image.Height = bmp.PixelHeight;
                }

                if (_normalImage != null && applied != null)
                {
                    GamePerfSession s = OneMapExport.ToPerfSession(applied);
                    _normalImage.Source = GamePerformanceReport.Render(s,
                        OneMapExport.ToPerfPalette(pal), 144);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "预览失败：" + ex.Message, "一图流");
            }
        }

        private void Export()
        {
            try
            {
                MatchRecord applied = LimitRounds(_ann.Apply(_match));
                Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Title = "导出一图流";
                dlg.Filter = "PNG 图片 (*.png)|*.png";
                dlg.DefaultExt = ".png";
                string dir = OneMapExport.DefaultDir(_cfg.ExportDir);
                dlg.InitialDirectory = dir;
                dlg.FileName = OneMapExport.BaseName(applied) + ".png";
                if (dlg.ShowDialog(this) != true) return;

                OneMapExportRequest req = BuildRequest(false,
                    Path.GetDirectoryName(dlg.FileName),
                    Path.GetFileNameWithoutExtension(dlg.FileName));

                OneMapOptions opt = BuildOptions();

                List<string> files = OneMapExport.Export(applied, opt,
                    OneMapRenderer.OneMapPalette.For(_light), req, 144);
                MessageBox.Show(this, "已导出 " + files.Count + " 个文件：\n\n"
                    + string.Join("\n", files.ToArray()), "一图流");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "一图流");
            }
        }

        /// <summary>离屏自检用：拿到当前预览图。</summary>
        internal BitmapSource PreviewForTest()
        {
            return _image != null ? _image.Source as BitmapSource : null;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (_dirty) _ann.Save(); } catch { }
            base.OnClosed(e);
        }

        // ---------------- 小控件 ----------------
        private Button Btn(string text, Action click, bool primary = false)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 10.5;
            b.Height = 26;
            b.Padding = new Thickness(10, 0, 10, 0);
            b.Margin = new Thickness(6, 0, 0, 0);
            b.Foreground = primary ? B(Colors.White) : Ink();
            b.Background = primary ? Accent() : B(_light ? C(237, 242, 245) : C(16, 26, 36));
            b.BorderBrush = primary ? Accent() : Line();
            b.BorderThickness = new Thickness(1);
            b.Cursor = Cursors.Hand;
            b.Click += delegate { click(); };
            return b;
        }

        private Button SmallButton(string text)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 11;
            b.Width = 24;
            b.Height = 22;
            b.Padding = new Thickness(0);
            b.Foreground = B(_light ? C(150, 60, 50) : C(255, 140, 140));
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private static Color C(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        private static SolidColorBrush B(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
