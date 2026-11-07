using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SCPSLAlert
{
    internal sealed partial class WpfTacticalWindow
    {
        private sealed class ScpRowUi
        {
            public string Code = "";
            public TacInput Total;
            public TacInput Cur;
            public TacMeter Bar;
            public TacNumber Hp;
            public TacButton Faction;
            public TacButton Kill;
            public TacButton Present;
        }

        private sealed class ScpChipUi
        {
            public string Code = "";
            public Border Host;
            public TextBlock CodeText;
            public TacNumber HpText;
            public TextBlock StateText;
            public TacMeter Bar;
        }

        private sealed class TacGenUi
        {
            public TacInput Remaining;
            public TacInput Base;
            public TacMeter Bar;
            public TextBlock State;
            public TacButton Faction;
            public TacButton Run;
        }

        private sealed class WaveRowUi
        {
            public Border Host;
            public TacButton Faction;
            public TacButton Kind;
            public TextBlock Age;
            public TacButton Delete;
        }

        private readonly Dictionary<string, ScpRowUi> _scpRows = new Dictionary<string, ScpRowUi>();
        private readonly Dictionary<string, ScpChipUi> _scpChips = new Dictionary<string, ScpChipUi>();
        private readonly TacGenUi[] _genRows = new TacGenUi[TacticalLog.GeneratorCount];
        private readonly bool[] _genPending = new bool[TacticalLog.GeneratorCount];
        private readonly List<WaveRowUi> _waveRows = new List<WaveRowUi>();
        private int _waveFirst;

        private TacArc _ovArc;
        private TacNumber _ovPercent;
        private TacNumber _ovMtfTok, _ovChaosTok;
        private TacNumber _ovMtfProb, _ovChaosProb;
        private TacNumber _ovMtfInf, _ovChaosInf;
        private TacMeter _ovMtfMeter, _ovChaosMeter;
        private TacNumber _ovNextWave, _ovRound;
        private TextBlock _ovNextWaveLine, _ovSmallWave;
        private TextBlock _ovShared;
        private TacNumber _ovSharedSummary, _ovKills, _ovTotalInf, _ovTickets;
        private TacNumber _ovLastWave, _ovRoundSummary;
        private StackPanel _pendingList;
        private Border _pendingCard;
        private int _pendingCount = -1;
        private string _pendingSig = "";

        private TacButton _has079;
        private TacButton _waveKindToggle;
        private TextBlock _genStatus;

        private readonly Dictionary<string, TacNumber> _objCounts = new Dictionary<string, TacNumber>();
        private readonly Dictionary<string, int> _manualCounts = new Dictionary<string, int>();
        private readonly List<string> _objOrder = new List<string>();
        private TextBlock _recentObj;
        private StackPanel _timeline;
        private int _timelineSig = -1;

        private TacInput _calRound, _calSup, _calDec, _calDm;
        private TextBlock _calMsg;
        private StackPanel _calStateList;
        private string _calStateSig = "";

        private VigilCard Card(string kicker, string title, string right, out StackPanel body)
        {
            VigilCard card = new VigilCard();
            card.Fill = _panelB;
            card.Stroke = _lineB;
            card.Cut = 12;
            card.Padding = new Thickness(16, 12, 16, 14);
            card.Margin = new Thickness(0, 0, 0, 10);

            StackPanel stack = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Children.Add(TacUi.Mono(kicker, 8, _accentB));
            TextBlock t = TacUi.Display(title, 15, _inkB);
            t.Margin = new Thickness(0, -1, 0, 0);
            left.Children.Add(t);
            head.Children.Add(left);

            if (!string.IsNullOrEmpty(right))
            {
                TextBlock r = TacUi.Mono(right, 8, _mutedB);
                r.VerticalAlignment = VerticalAlignment.Bottom;
                r.HorizontalAlignment = HorizontalAlignment.Right;
                r.Margin = new Thickness(12, 0, 0, 2);
                r.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(r, 1);
                head.Children.Add(r);
            }
            stack.Children.Add(head);

            Border hair = TacUi.Hair(_lineB);
            hair.Margin = new Thickness(0, 9, 0, 10);
            stack.Children.Add(hair);

            body = new StackPanel();
            stack.Children.Add(body);
            card.Child = stack;
            return card;
        }

        private TextBlock Kicker(string text, Brush color)
        {
            TextBlock t = TacUi.Mono(text, 8, color);
            t.Margin = new Thickness(0, 0, 0, 4);
            return t;
        }

        private UIElement StatCell(string label, out TacNumber value, int col, int row)
        {
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(0, 1, 10, 2);
            s.Children.Add(TacUi.Mono(label, 8, _mutedB));
            value = Num("", 12.5, _inkB, false);
            value.Margin = new Thickness(0, 1, 0, 0);
            s.Children.Add(value);
            Grid.SetColumn(s, col);
            Grid.SetRow(s, row);
            return s;
        }

        private Grid TwoColumns(double leftStar, double rightStar, double gap)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(leftStar, GridUnitType.Star)
            });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            g.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(rightStar, GridUnitType.Star)
            });
            return g;
        }

        private static void PutColumn(UIElement e, int col)
        {
            Grid.SetColumn(e, col);
        }

        private ScrollViewer Page(StackPanel content)
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Padding = new Thickness(0, 2, 8, 6);
            sv.Content = content;
            return sv;
        }

        private void BuildPages()
        {
            _scpRows.Clear();
            _scpChips.Clear();
            for (int i = 0; i < _genRows.Length; i++) _genRows[i] = null;
            _waveRows.Clear();
            _waveFirst = 0;
            _timeline = null;
            _timelineSig = -1;
            _calStateSig = "";
            _objCounts.Clear();
            _pendingCount = -1;
            _pendingSig = "";

            if (_pageHost == null) return;
            _pageHost.Children.Clear();
            for (int i = 0; i < 4; i++) _pageScroll[i] = null;

            _pageScroll[0] = Page(BuildOverviewPage());
            _pageScroll[1] = Page(BuildRosterPage());
            _pageScroll[2] = Page(BuildObjectivesPage());
            _pageScroll[3] = Page(BuildCalibratePage());
            for (int i = 0; i < 4; i++)
            {
                _pageScroll[i].Visibility = i == _page ? Visibility.Visible : Visibility.Collapsed;
                _pageHost.Children.Add(_pageScroll[i]);
            }
        }

        private StackPanel BuildOverviewPage()
        {
            StackPanel root = new StackPanel();
            _overviewPanel = root;
            if (_editMode) root.Children.Add(BuildEditorBar());
            for (int i = 0; i < _layout.Count; i++)
            {
                string id = _layout[i];
                UIElement w = BuildWidget(id);
                if (w == null) continue;
                root.Children.Add(_editMode ? EditWrap(w, id) : w);
            }
            if (_editMode && _layout.Count == 0)
                root.Children.Add(TacUi.T("当前没有显示任何模块：用上面的「添加」放回来。", 10, _mutedB));
            return root;
        }

        private bool OnOverview(string id)
        {
            return _layout != null && _layout.Contains(id);
        }

        private UIElement BuildWidget(string id)
        {
            if (id == "hero") return BuildHero();
            if (id == "actions") return BuildActionStrip();
            if (id == "scp") return BuildScpStrip();
            if (id == "pending") return BuildPendingCard();
            if (id == "summary") return BuildSummaryStrip();
            if (id == "gen") return BuildGeneratorCard();
            if (id == "waves") return BuildWaveCard();
            if (id == "timeline") return BuildTimelineCard();
            return null;
        }

        private UIElement BuildEditorBar()
        {
            Border bar = new Border();
            bar.Background = _panelAltB;
            bar.BorderBrush = _accentB;
            bar.BorderThickness = new Thickness(1);
            bar.CornerRadius = new CornerRadius(3);
            bar.Padding = new Thickness(12, 9, 12, 9);
            bar.Margin = new Thickness(0, 0, 0, 10);

            StackPanel stack = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel texts = new StackPanel();
            texts.Children.Add(TacUi.Mono("LAYOUT EDITOR / 布局编辑", 9, _accentB));
            texts.Children.Add(TacUi.T("拖动左侧把手调整顺序；× 移除；点「添加」把模块放进来。", 9, _subB));
            head.Children.Add(texts);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.VerticalAlignment = VerticalAlignment.Center;
            TacButton done = new TacButton("完成", TacKind.Primary, _p, 26, 14);
            done.Click += delegate { CloseEditor(); };
            btns.Children.Add(done);
            TacButton cancel = new TacButton("取消", TacKind.Ghost, _p, 26, 10);
            cancel.Margin = new Thickness(6, 0, 0, 0);
            cancel.Click += delegate { CancelEditor(); };
            btns.Children.Add(cancel);
            TacButton reset = new TacButton("恢复默认", TacKind.Quiet, _p, 26, 10);
            reset.Margin = new Thickness(6, 0, 0, 0);
            reset.Click += delegate { ResetLayout(); };
            btns.Children.Add(reset);
            Grid.SetColumn(btns, 1);
            head.Children.Add(btns);
            stack.Children.Add(head);

            WrapPanel add = new WrapPanel();
            add.Margin = new Thickness(0, 8, 0, 0);
            for (int i = 0; i < TacWidgets.Ids.Length; i++)
            {
                string id = TacWidgets.Ids[i];
                if (_layout.Contains(id)) continue;
                string wid = id;
                TacButton b = new TacButton("+ " + TacWidgets.Name(id), TacKind.Ghost, _p, 24, 9);
                b.Margin = new Thickness(0, 0, 6, 4);
                b.Click += delegate { AddWidget(wid); };
                add.Children.Add(b);
            }
            if (add.Children.Count == 0)
                add.Children.Add(TacUi.Mono("全部模块都已显示", 8, _mutedB));
            stack.Children.Add(add);

            bar.Child = stack;
            return bar;
        }

        private UIElement EditWrap(UIElement content, string id)
        {
            Border wrap = new Border();
            wrap.Tag = id;
            wrap.BorderBrush = TacColor.Alpha(_p.Accent, 110);
            wrap.BorderThickness = new Thickness(1);
            wrap.CornerRadius = new CornerRadius(3);
            wrap.Margin = new Thickness(0, 0, 0, 6);
            wrap.Background = TacColor.Alpha(_p.Accent, _p.IsLight ? 6 : 8);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            Border grip = new Border();
            grip.Background = TacColor.Alpha(_p.Accent, 26);
            grip.Cursor = Cursors.SizeAll;
            StackPanel gripText = new StackPanel();
            gripText.VerticalAlignment = VerticalAlignment.Center;
            gripText.HorizontalAlignment = HorizontalAlignment.Center;
            gripText.Children.Add(TacUi.Mono("≡", 12, _accentB));
            gripText.Children.Add(TacUi.Mono("拖", 7, _mutedB));
            grip.Child = gripText;
            g.Children.Add(grip);

            content.IsHitTestVisible = false;
            Grid.SetColumn(content, 1);
            g.Children.Add(content);

            TacButton del = new TacButton("×", TacKind.Quiet, _p, 22, 6);
            del.VerticalAlignment = VerticalAlignment.Top;
            del.HorizontalAlignment = HorizontalAlignment.Right;
            del.Margin = new Thickness(2, 2, 2, 0);
            del.Click += delegate { RemoveWidget(id); };
            Grid.SetColumn(del, 2);
            g.Children.Add(del);

            wrap.Child = g;

            grip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                _dragId = id;
                _dragStartY = e.GetPosition(_overviewPanel).Y;
                _dragWrap = wrap;
                wrap.Opacity = 0.55;
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_dragWrap != wrap) return;
                double y = e.GetPosition(_overviewPanel).Y;
                wrap.RenderTransform = new TranslateTransform(0, y - _dragStartY);
            };
            grip.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (_dragWrap != wrap) return;
                double y = e.GetPosition(_overviewPanel).Y;
                grip.ReleaseMouseCapture();
                wrap.RenderTransform = null;
                wrap.Opacity = 1;
                _dragWrap = null;
                string dragId = _dragId;
                _dragId = null;
                MoveWidget(dragId, DropIndex(y, wrap));
                e.Handled = true;
            };
            return wrap;
        }

        private int DropIndex(double y, Border self)
        {
            if (_overviewPanel == null) return _layout.Count;
            for (int i = 0; i < _overviewPanel.Children.Count; i++)
            {
                FrameworkElement c = _overviewPanel.Children[i] as FrameworkElement;
                if (c == null || c == self) continue;
                string id = c.Tag as string;
                if (id == null) continue;
                double top = c.TranslatePoint(new Point(0, 0), _overviewPanel).Y;
                if (y < top + c.ActualHeight / 2) return Math.Max(0, _layout.IndexOf(id));
            }
            return _layout.Count;
        }

        private void MoveWidget(string id, int target)
        {
            int from = _layout.IndexOf(id);
            if (from < 0) return;
            _layout.RemoveAt(from);
            if (target > from) target--;
            if (target < 0) target = 0;
            if (target > _layout.Count) target = _layout.Count;
            if (target == from)
            {
                _layout.Insert(from, id);
                return;
            }
            _layout.Insert(target, id);
            SaveLayout();
            RebuildPages();
        }

        private void AddWidget(string id)
        {
            if (_layout.Contains(id)) return;
            _layout.Add(id);
            SaveLayout();
            RebuildPages();
        }

        private void RemoveWidget(string id)
        {
            _layout.Remove(id);
            SaveLayout();
            RebuildPages();
        }

        private VigilCard BuildHero()
        {
            VigilCard card = new VigilCard();
            card.Fill = _panelB;
            card.Stroke = _lineB;
            card.Cut = 14;
            card.Padding = new Thickness(18, 14, 18, 14);
            card.Margin = new Thickness(0, 0, 0, 10);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(316) });

            Grid arcHost = new Grid();
            arcHost.Width = 160;
            arcHost.Height = 160;
            arcHost.HorizontalAlignment = HorizontalAlignment.Left;
            if (!UiAnim.Off)
            {
                TacRadar radar = new TacRadar(_p);
                radar.Width = 186;
                radar.Height = 186;
                radar.HorizontalAlignment = HorizontalAlignment.Center;
                radar.VerticalAlignment = VerticalAlignment.Center;
                radar.Opacity = 0.85;
                arcHost.Children.Add(radar);
            }
            _ovArc = new TacArc();
            _ovArc.Track = TacColor.Alpha(_p.Ink, _p.IsLight ? 26 : 32);
            _ovArc.Fill = _accentB;
            _ovArc.Tick = TacColor.Alpha(_p.Ink, _p.IsLight ? 34 : 40);
            _ovArc.Thickness = 3.5;
            arcHost.Children.Add(_ovArc);

            StackPanel arcText = new StackPanel();
            arcText.VerticalAlignment = VerticalAlignment.Center;
            arcText.HorizontalAlignment = HorizontalAlignment.Center;
            _ovPercent = Num("—", 40, _accentB, true);
            _ovPercent.FontWeight = FontWeights.Bold;
            _ovPercent.HorizontalAlignment = HorizontalAlignment.Center;
            arcText.Children.Add(_ovPercent);
            TextBlock note = TacUi.Mono("还有重生", 8, _mutedB);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            arcText.Children.Add(note);
            arcHost.Children.Add(arcText);
            g.Children.Add(arcHost);

            Border v1 = TacUi.VHair(_lineB);
            v1.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetColumn(v1, 1);
            g.Children.Add(v1);

            StackPanel mid = new StackPanel();
            mid.Margin = new Thickness(18, 4, 18, 0);
            mid.Children.Add(Kicker("重生余量", _accentB));

            Grid sides = new Grid();
            sides.Margin = new Thickness(0, 2, 0, 0);
            sides.ColumnDefinitions.Add(new ColumnDefinition());
            sides.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            sides.ColumnDefinitions.Add(new ColumnDefinition());
            UIElement mtfPool = FactionPool(TacticalLog.MTF, out _ovMtfTok, out _ovMtfProb,
                out _ovMtfInf, out _ovMtfMeter);
            sides.Children.Add(mtfPool);
            UIElement chaosPool = FactionPool(TacticalLog.CHAOS, out _ovChaosTok, out _ovChaosProb,
                out _ovChaosInf, out _ovChaosMeter);
            Grid.SetColumn(chaosPool, 2);
            sides.Children.Add(chaosPool);
            mid.Children.Add(sides);

            _ovNextWaveLine = TacUi.Mono("", 8.5, _subB);
            _ovNextWaveLine.Margin = new Thickness(0, 10, 0, 0);
            mid.Children.Add(_ovNextWaveLine);
            _ovSmallWave = TacUi.T("", 10, _warnB);
            _ovSmallWave.Margin = new Thickness(0, 2, 0, 0);
            _ovSmallWave.TextWrapping = TextWrapping.Wrap;
            mid.Children.Add(_ovSmallWave);
            Grid.SetColumn(mid, 2);
            g.Children.Add(mid);

            Border v2 = TacUi.VHair(_lineB);
            v2.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetColumn(v2, 3);
            g.Children.Add(v2);

            StackPanel right = new StackPanel();
            right.Margin = new Thickness(18, 4, 0, 0);
            right.Children.Add(Kicker("下一波", _accentB));
            _ovNextWave = Num("--:--", 40, _inkB, false);
            _ovNextWave.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(_ovNextWave);
            _ovRound = Num("", 8.5, _mutedB, false);
            _ovRound.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(_ovRound);

            Border hr = TacUi.Hair(_lineB);
            hr.Margin = new Thickness(0, 12, 0, 8);
            right.Children.Add(hr);

            right.Children.Add(TacUi.T("两边合计还能重生", 9.5, _subB));
            TextBlock hint = TacUi.T("", 9, _mutedB);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 3, 0, 0);
            _ovShared = hint;
            right.Children.Add(hint);

            Grid.SetColumn(right, 4);
            g.Children.Add(right);

            card.Child = g;
            return card;
        }

        private UIElement FactionPool(string faction, out TacNumber tokenText,
            out TacNumber probText, out TacNumber infText, out TacMeter meter)
        {
            Brush color = faction == TacticalLog.MTF ? _mtfB : _chaosB;

            StackPanel s = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = TacUi.Mono(faction == TacticalLog.MTF ? "MTF" : "混沌", 9, color);
            name.FontWeight = FontWeights.SemiBold;
            head.Children.Add(name);
            probText = Num("", 8, _mutedB, false);
            probText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(probText, 1);
            head.Children.Add(probText);
            s.Children.Add(head);

            StackPanel big = new StackPanel();
            big.Orientation = Orientation.Horizontal;
            big.Margin = new Thickness(0, -3, 0, 0);
            tokenText = Num("0", 36, color, true);
            tokenText.FontWeight = FontWeights.Bold;
            big.Children.Add(tokenText);
            TextBlock unit = TacUi.T(" 张", 10, _subB);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(0, 0, 0, 8);
            big.Children.Add(unit);
            s.Children.Add(big);

            meter = new TacMeter();
            meter.Height = 8;
            meter.BarThickness = 5;
            meter.Track = TacColor.Alpha(_p.Ink, _p.IsLight ? 26 : 32);
            meter.Fill = color;
            meter.GhostFill = TacColor.Alpha(_p.Warn, _p.IsLight ? 70 : 90);
            meter.Margin = new Thickness(0, 2, 0, 4);
            s.Children.Add(meter);

            infText = Num("", 8.5, _subB, false);
            s.Children.Add(infText);
            return s;
        }

        private UIElement BuildActionStrip()
        {
            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 0, 0, 10);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TacButton wave = new TacButton("刚刷了一波  (F11)", TacKind.Primary, _p, 34, 16);
            wave.Margin = new Thickness(0, 0, 8, 0);
            wave.Click += delegate { MarkBigWaveNow(); };
            actions.Children.Add(wave);

            // 【中途加入】第一波软件默认是大波，但游戏可能刷的是小波。
            // 这个按钮直接切换"最近这一波"的大小，改的是全局 TacticalLog，
            // 波次锚点 / 下一波倒计时 / 令牌扣减都会跟着重算。
            _waveKindToggle = new TacButton("刚这波：未记录", TacKind.Quiet, _p, 34, 12);
            _waveKindToggle.Margin = new Thickness(0, 0, 8, 0);
            _waveKindToggle.ToolTip = "切换最近一波是大波还是小波；波次计时和令牌会全局重算";
            _waveKindToggle.Click += delegate { ToggleLatestWaveType(); };
            Grid.SetColumn(_waveKindToggle, 1);
            actions.Children.Add(_waveKindToggle);

            TacButton cal = new TacButton("校准时间", TacKind.Ghost, _p, 34, 14);
            cal.Click += delegate { GoPage(3); };
            Grid.SetColumn(cal, 2);
            actions.Children.Add(cal);

            TacButton reset = new TacButton("清空本局", TacKind.Danger, _p, 34, 14);
            reset.Click += delegate { ResetRound(); };
            Grid.SetColumn(reset, 4);
            actions.Children.Add(reset);
            return actions;
        }

        private VigilCard BuildScpStrip()
        {
            StackPanel body;
            VigilCard card = Card("战况", "场上 SCP", "点卡片看详情", out body);
            card.Padding = new Thickness(16, 10, 16, 10);

            // 本回合 079 的确认挪到这里：和“场上 SCP”放在同一张卡里，
            // 看 SCP 阵容时顺手就能确认这一局有没有 079。
            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 0, 6);
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _has079 = new TacButton(_log.Has079 ? "本回合有 079" : "本回合无 079",
                TacKind.Ghost, _p, 28, 10);
            _has079.HorizontalAlignment = HorizontalAlignment.Right;
            _has079.Click += delegate { ToggleFound079(); };
            Grid.SetColumn(_has079, 1);
            head.Children.Add(_has079);
            body.Children.Add(head);

            Grid wrap = new Grid();
            wrap.Margin = new Thickness(0, -2, 0, 0);
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;
                int col = wrap.ColumnDefinitions.Count;
                wrap.ColumnDefinitions.Add(new ColumnDefinition());
                ScpChipUi chip = new ScpChipUi();
                chip.Code = code;

                Border host = new Border();
                host.Height = 46;
                host.Margin = new Thickness(0, 0, 8, 4);
                host.Background = _panelAltB;
                host.BorderThickness = new Thickness(1);
                host.BorderBrush = _lineB;
                host.Cursor = Cursors.Hand;
                host.ClipToBounds = true;
                Grid.SetColumn(host, col);

                Grid cg = new Grid();
                cg.Margin = new Thickness(9, 5, 9, 5);
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                cg.ColumnDefinitions.Add(new ColumnDefinition());

                StackPanel nameCol = new StackPanel();
                chip.CodeText = TacUi.Display("SCP-" + code, 10.5, _inkB);
                chip.CodeText.FontWeight = FontWeights.Bold;
                chip.CodeText.Width = 56;
                chip.CodeText.TextTrimming = TextTrimming.CharacterEllipsis;
                nameCol.Children.Add(chip.CodeText);
                chip.StateText = TacUi.Mono("", 7, _mutedB);
                chip.StateText.Width = 56;
                chip.StateText.TextTrimming = TextTrimming.CharacterEllipsis;
                nameCol.Children.Add(chip.StateText);
                cg.Children.Add(nameCol);

                StackPanel stat = new StackPanel();
                stat.VerticalAlignment = VerticalAlignment.Center;
                chip.Bar = new TacMeter();
                chip.Bar.Height = 8;
                chip.Bar.BarThickness = 3;
                chip.Bar.Track = TacColor.Alpha(_p.Ink, _p.IsLight ? 24 : 30);
                chip.Bar.Fill = _goodB;
                stat.Children.Add(chip.Bar);
                chip.HpText = Num("", 8, _subB, false);
                chip.HpText.Margin = new Thickness(0, 2, 0, 0);
                stat.Children.Add(chip.HpText);
                Grid.SetColumn(stat, 1);
                cg.Children.Add(stat);

                host.Child = cg;
                host.MouseEnter += delegate { host.BorderBrush = _accentB; };
                host.MouseLeave += delegate { host.BorderBrush = _lineB; };
                host.MouseLeftButtonUp += delegate { GoPage(1); };

                wrap.Children.Add(host);
                chip.Host = host;
                _scpChips[code] = chip;
            }
            body.Children.Add(wrap);
            return card;
        }

        private VigilCard BuildPendingCard()
        {
            VigilCard card = new VigilCard();
            card.Fill = _panelB;
            card.Stroke = _warnB;
            card.Cut = 12;
            card.Padding = new Thickness(14, 9, 14, 9);
            card.Margin = new Thickness(0, 0, 0, 10);
            _pendingCard = card;
            _pendingCard.Visibility = Visibility.Collapsed;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel label = new StackPanel();
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Children.Add(TacUi.T("待归属的阵亡", 10.5, _inkB));
            g.Children.Add(label);

            _pendingList = new StackPanel();
            _pendingList.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_pendingList, 1);
            g.Children.Add(_pendingList);

            card.Child = g;
            return card;
        }

        private UIElement BuildSummaryStrip()
        {
            VigilCard card = new VigilCard();
            card.Fill = _panelB;
            card.Stroke = _lineB;
            card.Cut = 12;
            card.Padding = new Thickness(16, 10, 16, 10);

            StackPanel stack = new StackPanel();

            Grid stats = new Grid();
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.Children.Add(StatCell("回合时间", out _ovRoundSummary, 0, 0));
            stats.Children.Add(StatCell("击杀 / 掉血", out _ovKills, 1, 0));
            stats.Children.Add(StatCell("总影响力", out _ovTotalInf, 2, 0));
            stats.Children.Add(StatCell("额外令牌（共享）", out _ovSharedSummary, 3, 0));
            stack.Children.Add(stats);

            Border hair = TacUi.Hair(_lineB);
            hair.Margin = new Thickness(0, 6, 0, 7);
            stack.Children.Add(hair);

            _ovLastWave = Num("", 10.5, _inkB, false);
            _ovLastWave.FontFamily = TacUi.Ui;
            stack.Children.Add(_ovLastWave);

            _ovTickets = Num("", 8, _mutedB, false);
            _ovTickets.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(_ovTickets);

            card.Child = stack;
            return card;
        }

        private StackPanel BuildRosterPage()
        {
            StackPanel root = new StackPanel();

            StackPanel body;
            VigilCard card = Card("血量", "SCP 状态", "点「阵营」循环切换", out body);
            body.Children.Add(ScpHeader());
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                if (code == "079") continue;
                body.Children.Add(BuildScpRow(code));
            }
            TextBlock hint = TacUi.T("总血量按服务器填；清空「当前」= 这只 SCP 重新算。",
                9, _mutedB);
            hint.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(hint);
            root.Children.Add(card);

            if (!OnOverview("gen")) root.Children.Add(BuildGeneratorCard());
            if (!OnOverview("waves")) root.Children.Add(BuildWaveCard());
            return root;
        }

        private Grid ScpHeader()
        {
            Grid g = new Grid();
            g.Height = 18;
            g.Margin = new Thickness(0, 2, 0, 2);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });

            string[] heads = { "SCP", "总血量", "当前", "剩余", "阵营", "状态", "在场" };
            for (int i = 0; i < heads.Length; i++)
            {
                TextBlock t = TacUi.Mono(heads[i], 7.5, _mutedB);
                t.VerticalAlignment = VerticalAlignment.Center;
                if (i >= 4) t.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return g;
        }

        private UIElement BuildScpRow(string code)
        {
            ScpRowUi row = new ScpRowUi();
            row.Code = code;

            StackPanel host = new StackPanel();
            Grid g = new Grid();
            g.Height = 44;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });

            TextBlock name = TacUi.Display("SCP-" + code, 13, _inkB);
            name.FontWeight = FontWeights.Bold;
            name.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(name);

            row.Total = new TacInput(_p, 76, 26, true);
            row.Total.VerticalAlignment = VerticalAlignment.Center;
            row.Total.Box.TextChanged += delegate { OnTotalHpEdited(code, row.Total.Text); };
            Grid.SetColumn(row.Total, 1);
            g.Children.Add(row.Total);

            row.Cur = new TacInput(_p, 76, 26, true);
            row.Cur.VerticalAlignment = VerticalAlignment.Center;
            row.Cur.Box.TextChanged += delegate { OnCurrentHpEdited(code, row.Cur.Text); };
            row.Cur.Box.LostKeyboardFocus += delegate { ScheduleSave(); };
            Grid.SetColumn(row.Cur, 2);
            g.Children.Add(row.Cur);

            Grid hpCell = new Grid();
            hpCell.Margin = new Thickness(8, 0, 12, 0);
            hpCell.VerticalAlignment = VerticalAlignment.Center;
            hpCell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            hpCell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.Bar = new TacMeter();
            row.Bar.Height = 10;
            row.Bar.BarThickness = 3;
            row.Bar.Track = TacColor.Alpha(_p.Ink, _p.IsLight ? 24 : 30);
            row.Bar.Fill = _goodB;
            row.Bar.VerticalAlignment = VerticalAlignment.Center;
            hpCell.Children.Add(row.Bar);
            row.Hp = Num("", 8.5, _subB, false);
            Grid.SetRow(row.Hp, 1);
            hpCell.Children.Add(row.Hp);
            Grid.SetColumn(hpCell, 3);
            g.Children.Add(hpCell);

            row.Faction = new TacButton("—", TacKind.Ghost, _p, 26, 8);
            row.Faction.Margin = new Thickness(2, 0, 6, 0);
            row.Faction.Click += delegate { CycleScpFaction(code); };
            Grid.SetColumn(row.Faction, 4);
            g.Children.Add(row.Faction);

            row.Kill = new TacButton("存活", TacKind.Quiet, _p, 26, 8);
            row.Kill.Margin = new Thickness(0, 0, 4, 0);
            row.Kill.Click += delegate { ToggleScpKilled(code); };
            Grid.SetColumn(row.Kill, 5);
            g.Children.Add(row.Kill);

            row.Present = new TacButton("—", TacKind.Quiet, _p, 26, 6);
            row.Present.Click += delegate { ToggleScpPresent(code); };
            Grid.SetColumn(row.Present, 6);
            g.Children.Add(row.Present);

            host.Children.Add(g);
            host.Children.Add(TacUi.Hair(TacColor.Alpha(_p.Line, _p.IsLight ? 150 : 190)));
            _scpRows[code] = row;
            return host;
        }

        private VigilCard BuildGeneratorCard()
        {
            StackPanel body;
            VigilCard card = Card("SCP-079", "发电机", "填「剩余」后回车开始倒计时", out body);

            _genStatus = TacUi.T("", 9.5, _subB);
            _genStatus.Margin = new Thickness(0, 0, 0, 6);
            _genStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(_genStatus);

            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                body.Children.Add(BuildGeneratorRow(g));
            }
            body.Children.Add(TacUi.T("「满速」是这台打算跑多少秒，默认 120；完成以游戏播报为准。",
                9, _mutedB));
            return card;
        }

        private UIElement BuildGeneratorRow(int index)
        {
            TacGenUi row = new TacGenUi();
            StackPanel host = new StackPanel();

            Grid g = new Grid();
            g.Height = 42;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });

            TextBlock name = TacUi.T("发电机 " + (index + 1), 11.5, _inkB);
            name.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(name);

            row.Remaining = new TacInput(_p, 76, 26, true, "s");
            row.Remaining.VerticalAlignment = VerticalAlignment.Center;
            row.Remaining.Box.TextChanged += delegate { OnGenRemainingEdited(index); };
            row.Remaining.Box.LostKeyboardFocus += delegate
            {
                if (_genPending[index]) ConfirmGenerators();
            };
            row.Remaining.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    ConfirmGenerators();
                }
            };
            Grid.SetColumn(row.Remaining, 1);
            g.Children.Add(row.Remaining);

            row.Base = new TacInput(_p, 76, 26, true, "s");
            row.Base.VerticalAlignment = VerticalAlignment.Center;
            row.Base.Box.TextChanged += delegate { OnGenBaseEdited(index, row.Base.Text); };
            Grid.SetColumn(row.Base, 2);
            g.Children.Add(row.Base);

            Grid mid = new Grid();
            mid.Margin = new Thickness(8, 0, 12, 0);
            mid.VerticalAlignment = VerticalAlignment.Center;
            mid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            mid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.Bar = new TacMeter();
            row.Bar.Height = 10;
            row.Bar.BarThickness = 3;
            row.Bar.Track = TacColor.Alpha(_p.Ink, _p.IsLight ? 24 : 30);
            row.Bar.Fill = _accentB;
            mid.Children.Add(row.Bar);
            row.State = TacUi.Mono("", 8.5, _subB);
            Grid.SetRow(row.State, 1);
            mid.Children.Add(row.State);
            Grid.SetColumn(mid, 3);
            g.Children.Add(mid);

            row.Faction = new TacButton("—", TacKind.Ghost, _p, 26, 8);
            row.Faction.Margin = new Thickness(2, 0, 6, 0);
            row.Faction.Click += delegate { CycleGenFaction(index); };
            Grid.SetColumn(row.Faction, 4);
            g.Children.Add(row.Faction);

            row.Run = new TacButton("启动", TacKind.Quiet, _p, 26, 10);
            row.Run.Click += delegate { ToggleGenerator(index); };
            Grid.SetColumn(row.Run, 5);
            g.Children.Add(row.Run);

            host.Children.Add(g);
            host.Children.Add(TacUi.Hair(TacColor.Alpha(_p.Line, _p.IsLight ? 150 : 190)));
            _genRows[index] = row;
            return host;
        }

        private VigilCard BuildWaveCard()
        {
            StackPanel body;
            VigilCard card = Card("刷新顺序", "本局刷新", "点阵营 / 大小波可以改", out body);

            for (int i = 0; i < 5; i++)
            {
                WaveRowUi row = new WaveRowUi();
                Grid g = new Grid();
                g.Height = 30;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                TextBlock idx = TacUi.Mono((i + 1).ToString("00"), 8, _mutedB);
                idx.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(idx);

                row.Faction = new TacButton("—", TacKind.Quiet, _p, 24, 8);
                row.Faction.Click += delegate { CycleWaveFaction(GetWaveSlot(row)); };
                Grid.SetColumn(row.Faction, 1);
                g.Children.Add(row.Faction);

                row.Kind = new TacButton("—", TacKind.Quiet, _p, 24, 8);
                row.Kind.Click += delegate { ToggleWaveKind(GetWaveSlot(row)); };
                Grid.SetColumn(row.Kind, 2);
                g.Children.Add(row.Kind);

                row.Age = TacUi.Mono("", 8.5, _subB);
                row.Age.VerticalAlignment = VerticalAlignment.Center;
                row.Age.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(row.Age, 3);
                g.Children.Add(row.Age);

                row.Delete = new TacButton("×", TacKind.Quiet, _p, 24, 8);
                row.Delete.Click += delegate { RemoveWave(GetWaveSlot(row)); };
                Grid.SetColumn(row.Delete, 4);
                g.Children.Add(row.Delete);

                row.Host = new Border();
                row.Host.Child = g;
                row.Host.Visibility = Visibility.Collapsed;
                body.Children.Add(row.Host);
                _waveRows.Add(row);
            }

            TacButton reclass = new TacButton("切换最近一波类型", TacKind.Quiet, _p, 26, 10);
            reclass.Margin = new Thickness(0, 6, 0, 0);
            reclass.HorizontalAlignment = HorizontalAlignment.Left;
            reclass.Click += delegate { ToggleLatestWaveType(); };
            body.Children.Add(reclass);
            return card;
        }

        private int GetWaveSlot(WaveRowUi row)
        {
            for (int i = 0; i < _waveRows.Count; i++)
            {
                if (_waveRows[i] == row) return i;
            }
            return -1;
        }

        private StackPanel BuildObjectivesPage()
        {
            StackPanel root = new StackPanel();

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            top.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel mtfBody;
            VigilCard mtf = Card("MTF", "MTF 目标", "点一下登记一次", out mtfBody);
            AddObjective(mtfBody, "mtf_kill", "击杀可玩 SCP", TicketModel.Objective.KillScp, TacticalLog.MTF);
            AddObjective(mtfBody, "mtf_gen", "发电机成功启动", TicketModel.Objective.ActivateGenerator, TacticalLog.MTF);
            AddObjective(mtfBody, "mtf_sci", "科学家未被捆绑逃离", TicketModel.Objective.MtfScientistEscape, TacticalLog.MTF);
            AddObjective(mtfBody, "mtf_dclass", "被捆绑的 D 级逃离", TicketModel.Objective.MtfBondedDEscape, TacticalLog.MTF);
            PutColumn(mtf, 0);
            top.Children.Add(mtf);

            StackPanel anyBody;
            VigilCard any = Card("通用", "通用目标", "默认记给 MTF", out anyBody);
            AddObjective(anyBody, "armed", "击杀敌对武装人员", TicketModel.Objective.KillArmed, TacticalLog.MTF);
            AddObjective(anyBody, "cons", "使用消耗性 SCP 物品", TicketModel.Objective.ConsumableScpItem, TacticalLog.MTF);
            AddObjective(anyBody, "uniq", "首次使用独特 SCP 物品", TicketModel.Objective.UniqueScpItem, TacticalLog.MTF);
            PutColumn(any, 2);
            top.Children.Add(any);

            StackPanel chaosBody;
            VigilCard chaos = Card("混沌", "混沌目标", "点一下登记一次", out chaosBody);
            AddObjective(chaosBody, "chaos_kill", "击杀可玩 SCP", TicketModel.Objective.KillScp, TacticalLog.CHAOS);
            AddObjective(chaosBody, "chaos_gen", "发电机成功启动", TicketModel.Objective.ActivateGenerator, TacticalLog.CHAOS);
            AddObjective(chaosBody, "chaos_civ", "平民逃离转为混沌", TicketModel.Objective.ChaosCivilianEscape, TacticalLog.CHAOS);
            PutColumn(chaos, 4);
            top.Children.Add(chaos);

            root.Children.Add(top);
            if (!OnOverview("timeline")) root.Children.Add(BuildTimelineCard());
            return root;
        }

        private VigilCard BuildTimelineCard()
        {
            StackPanel body;
            VigilCard card = Card("影响力", "时间线", "点错了可以撤销", out body);

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _recentObj = TacUi.T("", 9.5, _subB);
            _recentObj.TextTrimming = TextTrimming.CharacterEllipsis;
            _recentObj.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_recentObj);

            TacButton undo = new TacButton("撤销最后一次", TacKind.Ghost, _p, 26, 10);
            undo.Click += delegate { UndoLastObjective(); };
            Grid.SetColumn(undo, 1);
            head.Children.Add(undo);
            body.Children.Add(head);

            Border hair = TacUi.Hair(_lineB);
            hair.Margin = new Thickness(0, 8, 0, 4);
            body.Children.Add(hair);

            _timeline = new StackPanel();
            body.Children.Add(_timeline);
            return card;
        }

        private void AddObjective(StackPanel body, string key, string name,
            TicketModel.Objective kind, string faction)
        {
            TicketModel.Reward r = TicketModel.Info(kind);

            TacButton btn = new TacButton("", TacKind.Ghost, _p, 56, 12);
            btn.Margin = new Thickness(0, 0, 0, 6);
            btn.HorizontalContentAlignment = HorizontalAlignment.Stretch;

            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            TextBlock t = TacUi.T(name, 11.5, _inkB);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(t);
            TextBlock reward = TacUi.Mono("+" + r.Influence + " 影响力    −" + r.Seconds + "s",
                8, ObjectiveColor(kind));
            reward.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(reward);
            content.Children.Add(text);

            TacNumber count = Num("+", 17, _mutedB, true);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 1);
            content.Children.Add(count);

            btn.Content = content;
            btn.Label = null;
            btn.Click += delegate { RecordObjective(key, kind, faction); };

            body.Children.Add(btn);
            _objCounts[key] = count;
        }

        private Brush ObjectiveColor(TicketModel.Objective kind)
        {
            if (kind == TicketModel.Objective.MtfScientistEscape
                || kind == TicketModel.Objective.MtfBondedDEscape) return _mtfB;
            if (kind == TicketModel.Objective.ChaosCivilianEscape) return _chaosB;
            if (kind == TicketModel.Objective.KillScp) return _dangerB;
            return _warnB;
        }

        private StackPanel BuildCalibratePage()
        {
            StackPanel root = new StackPanel();
            Grid two = TwoColumns(1.1, 1, 10);

            StackPanel body;
            VigilCard form = Card("校准", "填一项就校准", "只影响时间", out body);
            body.Children.Add(TacUi.T("中途加入或计时偏了，填一项就够。", 9.5, _subB));

            body.Children.Add(BuildCalRow("当前回合已进行", "11.22 = 11 分 22 秒", out _calRound));
            body.Children.Add(BuildCalRow("支援还剩", "秒", out _calSup));
            body.Children.Add(BuildCalRow("净化还剩", "秒", out _calDec));
            body.Children.Add(BuildCalRow("终焉协议还剩", "秒，填 0 = 归零", out _calDm));

            TacButton apply = new TacButton("按上面填的校准", TacKind.Primary, _p, 32, 16);
            apply.HorizontalAlignment = HorizontalAlignment.Left;
            apply.Margin = new Thickness(0, 8, 0, 0);
            apply.Click += delegate { ApplyCalibration(); };
            body.Children.Add(apply);

            _calMsg = TacUi.T("", 9.5, _warnB);
            _calMsg.TextWrapping = TextWrapping.Wrap;
            _calMsg.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(_calMsg);
            PutColumn(form, 0);
            two.Children.Add(form);

            StackPanel stateBody;
            VigilCard state = Card("估算", "当前估算", "实时", out stateBody);
            _calStateList = new StackPanel();
            stateBody.Children.Add(_calStateList);

            TacButton stop = new TacButton("停止核弹 / 亡者协议", TacKind.Ghost, _p, 30, 12);
            stop.Margin = new Thickness(0, 10, 0, 0);
            stop.HorizontalAlignment = HorizontalAlignment.Left;
            stop.Click += delegate
            {
                try { if (StopSpecialCountdowns != null) StopSpecialCountdowns(); }
                catch (Exception ex) { Logger.Err("战术", "停止特殊倒计时失败", ex); }
            };
            stateBody.Children.Add(stop);
            PutColumn(state, 2);
            two.Children.Add(state);

            root.Children.Add(two);
            return root;
        }

        private UIElement BuildCalRow(string label, string hint, out TacInput input)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 7, 0, 7);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            StackPanel text = new StackPanel();
            text.Children.Add(TacUi.T(label, 10.5, _inkB));
            text.Children.Add(TacUi.Mono(hint, 7.5, _mutedB));
            g.Children.Add(text);

            input = new TacInput(_p, 140, 28, true);
            input.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(input, 1);
            g.Children.Add(input);
            return g;
        }

        private void UpdateOverview(DateTime now)
        {
            if (_ovPercent != null)
            {
                double pAny = _log.AnyCurrentTokenChance();
                double pMtf = _log.CurrentTokenChance(TacticalLog.MTF);
                double pChaos = _log.CurrentTokenChance(TacticalLog.CHAOS);

                _ovArc.AnimateTo(pAny, 420);
                _ovPercent.SetValue(pAny * 100, "0.0", "%", 420);
                _ovPercent.Foreground = pAny >= 0.6 ? _accentB : pAny >= 0.25 ? _warnB : _dangerB;

                if (_log.Tickets.HasUnknownHistory(TacticalLog.MTF)) _ovMtfTok.SetText("?", 0);
                else _ovMtfTok.SetValue(_log.TokensOf(TacticalLog.MTF), "0", "", 300);
                if (_log.Tickets.HasUnknownHistory(TacticalLog.CHAOS)) _ovChaosTok.SetText("?", 0);
                else _ovChaosTok.SetValue(_log.TokensOf(TacticalLog.CHAOS), "0", "", 300);

                _ovMtfProb.SetValue(pMtf * 100, "0.0", "%", 360);
                _ovChaosProb.SetValue(pChaos * 100, "0.0", "%", 360);

                UpdatePool(_ovMtfMeter, _ovMtfInf, TacticalLog.MTF);
                UpdatePool(_ovChaosMeter, _ovChaosInf, TacticalLog.CHAOS);

                bool unknown = _log.Tickets.HasUnknownHistory(TacticalLog.MTF)
                    || _log.Tickets.HasUnknownHistory(TacticalLog.CHAOS);
                if (unknown)
                {
                    _ovShared.Text = "历史消耗未知，只能给概率";
                    if (_ovSharedSummary != null) _ovSharedSummary.SetText("未知", 0);
                }
                else
                {
                    int total = _log.TokensOf(TacticalLog.MTF) + _log.TokensOf(TacticalLog.CHAOS);
                    _ovShared.Text = "合计 " + total + " 张"
                        + (_log.BothOutOfTokens ? "，两边都没令牌了" : "");
                    if (_ovSharedSummary != null)
                        _ovSharedSummary.SetText(_log.Tickets.BonusTokensLeft + " / "
                            + _log.Tickets.MaxBonusTokens, 160);
                }

                double mtfLeft = _log.WaveClockRunning ? Math.Max(0, _log.WaveRemaining(now, TacticalLog.MTF)) : 0;
                double chaosLeft = _log.WaveClockRunning ? Math.Max(0, _log.WaveRemaining(now, TacticalLog.CHAOS)) : 0;
                if (_log.WaveClockRunning)
                {
                    double next = mtfLeft > 0 && chaosLeft > 0 ? Math.Min(mtfLeft, chaosLeft)
                        : Math.Max(mtfLeft, chaosLeft);
                    _ovNextWave.SetText(TimeEstimator.Clock(next, true), 110);
                    _ovNextWave.Foreground = _inkB;
                    _ovNextWaveLine.Text = "MTF " + TimeEstimator.Clock(mtfLeft, true)
                        + "　混沌 " + TimeEstimator.Clock(chaosLeft, true)
                        + (_log.HasWaveAnchor ? "" : "（未校准）");
                }
                else
                {
                    _ovNextWave.SetText("--:--", 0);
                    _ovNextWave.Foreground = _mutedB;
                    _ovNextWaveLine.Text = "等待第一波刷新";
                }

                _ovSmallWave.Text = NextWaveEtaText(now);
                _ovRound.SetText("计时 " + TimeEstimator.Clock(_log.RoundElapsedNow(), false)
                    + (_log.HasWaveAnchor ? "" : "（未校准）")
                    + "　·　已记录 " + _log.WaveHistory.Count + " 波", 0);
            }

            if (_ovKills != null)
            {
                _ovKills.SetText(_log.TotalKills + " / " + _log.TotalDamage, 180);
                _ovTotalInf.SetValue(_log.TotalTickets, "0", "", 320);
                _ovRoundSummary.SetText(TimeEstimator.Clock(_log.RoundElapsedNow(), false)
                    + (_log.HasWaveAnchor ? "" : "（未校准）"), 0);

                string ticket = !string.IsNullOrEmpty(_ticketSummary) ? _ticketSummary
                    : (_cfg == null ? "" : _cfg.TicketSummaryText());
                _ovTickets.SetText(string.IsNullOrEmpty(ticket) ? "" : ticket, 0);
            }

            if (_ovLastWave != null)
            {
                if (_log.WaveHistory.Count > 0)
                {
                    TacticalLog.WaveRecord wr = _log.WaveHistory[_log.WaveHistory.Count - 1];
                    _ovLastWave.SetText("最近刷新：" + TacticalLog.FactionName(wr.Faction)
                        + (wr.IsSmall ? " 小波" : " 大波") + "　" + wr.At.ToString("HH:mm:ss")
                        + "（" + (wr.Source == "ocr" ? "识别" : "手动") + "）", 180);
                }
                else
                {
                    _ovLastWave.SetText("最近刷新：还没有记录", 0);
                }
            }

            UpdateScpChips(now);
            UpdatePending(now);
        }

        private void UpdatePool(TacMeter meter, TacNumber inf, string faction)
        {
            int influence = _log.TicketsOf(faction);
            int threshold = _log.Tickets.NextTokenThreshold(faction);
            if (threshold > 0)
            {
                double prev = 0;
                int[] th = _log.Tickets.TokenThresholds;
                for (int i = 0; i < th.Length; i++)
                {
                    if (th[i] < threshold) prev = th[i];
                }
                double span = threshold - prev;
                double v = span <= 0 ? 1 : (influence - prev) / span;
                meter.AnimateTo(v, 260);
                inf.SetText(influence + " / " + threshold + "　还差 " + (threshold - influence), 140);
            }
            else
            {
                meter.AnimateTo(1, 260);
                inf.SetText(influence + "　已拿满", 140);
            }
        }

        private string NextWaveEtaText(DateTime now)
        {
            try
            {
                if (!_log.WaveClockRunning) return "下一波：等待第一波刷新";
                if (!_log.HasBigWave) return "下一波：还没认出大波";

                double mini = _log.MiniWaveRemaining(now);
                if (mini > 0)
                {
                    string f = _log.DecideSmallWave(now);
                    string who = (f == TacticalLog.NONE || f == null) ? "" : TacticalLog.FactionName(f) + " ";
                    return "下一波是 " + who + "小波，" + TimeEstimator.Clock(mini, true) + " 后";
                }
                double mtf = _log.WaveRemaining(now, TacticalLog.MTF);
                double chaos = _log.WaveRemaining(now, TacticalLog.CHAOS);
                double big = (mtf > 0 && chaos > 0) ? Math.Min(mtf, chaos) : Math.Max(mtf, chaos);
                if (big <= 0) return "下一波：大波随时可能";
                return "下一波是大波，" + TimeEstimator.Clock(big, true) + " 后";
            }
            catch { return ""; }
        }

        private void UpdateScpChips(DateTime now)
        {
            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                ScpChipUi chip;
                if (!_scpChips.TryGetValue(code, out chip)) continue;
                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec == null) continue;

                int total = rec.TotalHp > 0 ? rec.TotalHp : DefaultHp(code);
                int cur = rec.CurrentHp >= 0 ? rec.CurrentHp : total;
                double ratio = total <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)cur / total));

                chip.Bar.AnimateTo(rec.Killed ? 0 : ratio, 220);
                chip.Bar.Fill = rec.Killed ? _dangerB
                    : rec.CurrentHp < 0 ? _mutedB
                    : ratio <= 0.25 ? _dangerB
                    : ratio <= 0.55 ? _warnB : _goodB;

                if (rec.Killed)
                {
                    chip.HpText.SetText("已阵亡", 160);
                    chip.StateText.Text = "阵亡";
                    chip.StateText.Foreground = _dangerB;
                    chip.CodeText.Foreground = _mutedB;
                }
                else
                {
                    chip.HpText.SetText(rec.CurrentHp >= 0
                        ? cur + " / " + total : "上限 " + total, 120);
                    string fac = rec.Faction == TacticalLog.MTF ? "MTF"
                        : rec.Faction == TacticalLog.CHAOS ? "混沌" : "";
                    chip.StateText.Text = rec.Present
                        ? (fac.Length > 0 ? fac + " · 在场" : "在场")
                        : (fac.Length > 0 ? fac : "—");
                    chip.StateText.Foreground = rec.Present ? _goodB
                        : rec.Faction == TacticalLog.MTF ? _mtfB
                        : rec.Faction == TacticalLog.CHAOS ? _chaosB : _mutedB;
                    chip.CodeText.Foreground = _inkB;
                }
            }
        }

        private void UpdatePending(DateTime now)
        {
            if (_pendingCard == null) return;
            int count = _log.PendingKills.Count;
            StringBuilder sig = new StringBuilder();
            for (int i = 0; i < count; i++) sig.Append(_log.PendingKills[i].Code).Append('|');
            string signature = sig.ToString();

            _pendingCard.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (count == 0) return;

            if (count == _pendingCount && signature == _pendingSig) return;
            _pendingCount = count;
            _pendingSig = signature;

            _pendingList.Children.Clear();
            for (int i = 0; i < count && i < 3; i++)
            {
                string code = _log.PendingKills[i].Code;
                Grid g = new Grid();
                g.Margin = new Thickness(0, 1, 0, 1);
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition());

                TextBlock label = TacUi.T("SCP-" + code + " 已阵亡", 10, _inkB);
                label.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(label);

                TacButton mtf = new TacButton("记给 MTF", TacKind.Ghost, _p, 24, 8);
                mtf.Margin = new Thickness(0, 0, 6, 0);
                mtf.Click += MakePendingHandler(code, TacticalLog.MTF);
                Grid.SetColumn(mtf, 1);
                g.Children.Add(mtf);

                TacButton chaos = new TacButton("记给混沌", TacKind.Ghost, _p, 24, 8);
                chaos.Click += MakePendingHandler(code, TacticalLog.CHAOS);
                Grid.SetColumn(chaos, 2);
                g.Children.Add(chaos);

                _pendingList.Children.Add(g);
            }
        }

        private RoutedEventHandler MakePendingHandler(string code, string faction)
        {
            return delegate
            {
                try
                {
                    _log.ResolvePendingKill(code, faction, DateTime.Now, _log.Has079);
                    RefreshAll();
                    ScheduleSave();
                }
                catch (Exception ex) { Logger.Err("战术", "处理待归属阵亡失败", ex); }
            };
        }

        private void UpdateRoster(DateTime now)
        {
            if (_scpRows.Count == 0) return;

            if (_log.HasBigWave && _log.BigWaveFaction != TacticalLog.NONE)
                _bigWaveSide = _log.BigWaveFaction;

            for (int i = 0; i < TacticalLog.ScpCodes.Length; i++)
            {
                string code = TacticalLog.ScpCodes[i];
                ScpRowUi row;
                if (!_scpRows.TryGetValue(code, out row)) continue;
                TacticalLog.ScpRecord rec = _log.Find(code);
                if (rec == null) continue;

                int total = rec.TotalHp > 0 ? rec.TotalHp : DefaultHp(code);
                int cur = rec.CurrentHp >= 0 ? rec.CurrentHp : total;
                double ratio = total <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)cur / total));

                row.Bar.AnimateTo(rec.Killed ? 0 : ratio, 220);
                row.Bar.Fill = rec.Killed ? _dangerB
                    : rec.CurrentHp < 0 ? _mutedB
                    : ratio <= 0.25 ? _dangerB
                    : ratio <= 0.55 ? _warnB : _goodB;

                if (rec.Killed)
                {
                    row.Hp.SetText("已阵亡（上限 " + total + "）", 160);
                    row.Hp.Foreground = _dangerB;
                }
                else if (rec.CurrentHp >= 0)
                {
                    row.Hp.SetText("剩 " + cur + " / " + total + "　" + (int)Math.Round(ratio * 100) + "%", 140);
                    row.Hp.Foreground = ratio <= 0.25 ? _dangerB : _subB;
                }
                else
                {
                    row.Hp.SetText("上限 " + total, 0);
                    row.Hp.Foreground = _mutedB;
                }

                ApplyFactionChip(row.Faction, rec.Faction);
                row.Kill.SetText(rec.Killed ? "已阵亡" : "存活");
                row.Kill.SetForegroundColor(rec.Killed ? _p.Danger : _p.Muted);
                row.Present.SetText(rec.Present ? "在场" : "—");
                row.Present.SetForegroundColor(rec.Present ? _p.Good : _p.Muted);
            }

            UpdateGenerators(now);
            UpdateWaveRows(now);
        }

        private void ApplyFactionChip(TacButton b, string faction)
        {
            if (faction == TacticalLog.MTF)
            {
                b.SetColors(TacColor.Alpha(_p.Mtf, _p.IsLight ? 26 : 34).Color,
                    TacColor.Alpha(_p.Mtf, _p.IsLight ? 44 : 56).Color,
                    TacColor.Alpha(_p.Mtf, 200).Color, _p.Mtf);
                b.SetText("MTF");
            }
            else if (faction == TacticalLog.CHAOS)
            {
                b.SetColors(TacColor.Alpha(_p.Chaos, _p.IsLight ? 26 : 34).Color,
                    TacColor.Alpha(_p.Chaos, _p.IsLight ? 44 : 56).Color,
                    TacColor.Alpha(_p.Chaos, 200).Color, _p.Chaos);
                b.SetText("混沌");
            }
            else
            {
                b.SetColors(Colors.Transparent, _p.PanelAlt,
                    TacColor.Alpha(_p.Ink, 60).Color, _p.Muted);
                b.SetText("—");
            }
        }

        private void UpdateGenerators(DateTime now)
        {
            if (_genRows[0] == null) return;

            for (int g = 0; g < TacticalLog.GeneratorCount; g++)
            {
                TacGenUi row = _genRows[g];
                TacticalLog.Generator gen = _log.Generators[g];
                if (row == null) continue;

                ApplyFactionChip(row.Faction, gen.Faction);

                if (gen.Done)
                {
                    row.Bar.AnimateTo(1, 260);
                    row.Bar.Fill = gen.Confirmed ? _goodB : _warnB;
                    row.State.Text = gen.Confirmed ? "已启动" : "时间到，等播报";
                    row.State.Foreground = gen.Confirmed ? _goodB : _warnB;
                    row.Run.SetText(gen.Confirmed ? "已启动" : "等播报");
                    row.Run.SetForegroundColor(gen.Confirmed ? _p.Good : _p.Warn);
                    row.Run.IsEnabled = !gen.Confirmed;
                }
                else if (gen.Running)
                {
                    double left = Math.Max(0, gen.Remaining(now));
                    int dur = gen.Duration > 0 ? gen.Duration : 120;
                    row.Bar.AnimateTo(Math.Max(0, Math.Min(1, left / dur)), 240);
                    bool soon = left > 0 && left <= TacticalLog.GenSoonSeconds;
                    row.Bar.Fill = soon ? _warnB : _accentB;
                    row.State.Text = (soon ? "快好了 " : "启动中 ") + (int)Math.Ceiling(left) + "s";
                    row.State.Foreground = soon ? _warnB : _accentB;
                    row.Run.SetText("停止");
                    row.Run.SetForegroundColor(_p.Sub);
                    row.Run.IsEnabled = true;
                }
                else
                {
                    row.Bar.AnimateTo(0, 260);
                    row.State.Text = "未启动";
                    row.State.Foreground = _mutedB;
                    row.Run.SetText("启动");
                    row.Run.SetForegroundColor(_p.Sub);
                    row.Run.IsEnabled = true;
                }
            }

            bool has = _log.Has079;
            if (_has079 != null)
            {
                _has079.SetColors(has ? TacColor.Alpha(_p.Good, _p.IsLight ? 24 : 32).Color
                        : _p.PanelAlt,
                    has ? TacColor.Alpha(_p.Good, _p.IsLight ? 42 : 54).Color : _p.PanelAlt,
                    has ? _p.Good : _p.Line, has ? _p.Good : _p.Muted);
                _has079.SetText(has ? "本回合有 079" : "本回合无 079");
            }

            string st = _log.GeneratorsActive + " / " + TacticalLog.GeneratorCount + " 台已启动";
            if (_log.GeneratorsRunning > 0) st += "，" + _log.GeneratorsRunning + " 台在启动中";
            if (_log.AllGeneratorsReady) st += "，去 079 收容室按按钮";
            if (_genStatus != null)
            {
                _genStatus.Text = st;
                _genStatus.Foreground = _log.AllGeneratorsReady ? _goodB : _subB;
            }
        }

        private void UpdateObjectives(DateTime now)
        {
            if (_objCounts.Count == 0) return;
            foreach (KeyValuePair<string, TacNumber> kv in _objCounts)
            {
                int n;
                _manualCounts.TryGetValue(kv.Key, out n);
                kv.Value.SetText(n > 0 ? "×" + n : "+", 180);
                kv.Value.Foreground = n > 0 ? _accentB : _mutedB;
            }
            if (_recentObj != null)
            {
                _recentObj.Text = "总影响力 " + _log.TotalTickets
                    + "　·　" + _log.Tickets.LastEntryText;
            }
            UpdateTimeline();
        }

        private void UpdateTimeline()
        {
            if (_timeline == null) return;
            System.Collections.Generic.List<TicketModel.LogEntry> entries = _log.Tickets.Entries;
            int sig = entries.Count * 31 + (entries.Count > 0 ? entries[entries.Count - 1].At.GetHashCode() : 0);
            if (sig == _timelineSig) return;
            _timelineSig = sig;

            _timeline.Children.Clear();
            if (entries.Count == 0)
            {
                _timeline.Children.Add(TacUi.T("本回合还没有登记过目标", 9.5, _mutedB));
                return;
            }

            int from = Math.Max(0, entries.Count - 6);
            for (int i = entries.Count - 1; i >= from; i--)
            {
                TicketModel.LogEntry e = entries[i];
                Grid g = new Grid();
                g.Height = 26;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

                TextBlock time = TacUi.Mono(e.At.ToString("HH:mm:ss"), 8.5, _mutedB);
                time.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(time);

                TextBlock fac = TacUi.Mono(TicketModel.FactionName(e.Faction), 8.5,
                    e.Faction == TacticalLog.CHAOS ? _chaosB : _mtfB);
                fac.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(fac, 1);
                g.Children.Add(fac);

                TextBlock what = TacUi.T(e.What, 10, _inkB);
                what.VerticalAlignment = VerticalAlignment.Center;
                what.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(what, 2);
                g.Children.Add(what);

                StackPanel gain = new StackPanel();
                gain.Orientation = Orientation.Horizontal;
                gain.HorizontalAlignment = HorizontalAlignment.Right;
                gain.VerticalAlignment = VerticalAlignment.Center;
                if (e.Influence > 0) gain.Children.Add(TacUi.Mono("+" + e.Influence, 9, _accentB));
                if (e.Seconds > 0) gain.Children.Add(TacUi.Mono("  −" + e.Seconds + "s", 9, _warnB));
                Grid.SetColumn(gain, 3);
                g.Children.Add(gain);

                _timeline.Children.Add(g);
                _timeline.Children.Add(TacUi.Hair(TacColor.Alpha(_p.Line, 120)));
            }
        }

        private void UpdateWaveRows(DateTime now)
        {
            if (_waveRows.Count == 0) return;
            int count = _log.WaveHistory.Count;
            _waveFirst = Math.Max(0, count - _waveRows.Count);
            for (int slot = 0; slot < _waveRows.Count; slot++)
            {
                WaveRowUi row = _waveRows[slot];
                int index = _waveFirst + slot;
                if (index >= count)
                {
                    row.Host.Visibility = Visibility.Collapsed;
                    continue;
                }
                row.Host.Visibility = Visibility.Visible;
                TacticalLog.WaveRecord wave = _log.WaveHistory[index];
                row.Faction.SetText(TacticalLog.FactionName(wave.Faction));
                row.Faction.SetForegroundColor(wave.Faction == TacticalLog.CHAOS ? _p.Chaos : _p.Mtf);
                row.Kind.SetText(wave.IsSmall ? "小波" : "大波");
                row.Kind.SetForegroundColor(wave.IsSmall ? _p.Warn : _p.Ink);
                row.Age.Text = wave.At.ToString("HH:mm:ss") + "   "
                    + (wave.Source == "ocr" ? "识别" : "手动");
            }
            if (_waveKindToggle != null)
            {
                if (count > 0)
                {
                    TacticalLog.WaveRecord last = _log.WaveHistory[count - 1];
                    _waveKindToggle.SetText("刚这波：" + (last.IsSmall ? "小波" : "大波") + "  (点击切换)");
                    _waveKindToggle.SetForegroundColor(last.IsSmall ? _p.Warn : _p.Ink);
                }
                else
                {
                    _waveKindToggle.SetText("刚这波：未记录");
                    _waveKindToggle.SetForegroundColor(_p.Muted);
                }
            }
        }

        private void UpdateCalibrate(DateTime now)
        {
            if (_calStateList == null) return;
            string state = "";
            try { if (TimeCalibrateState != null) state = TimeCalibrateState(); } catch { }
            if (state == null) state = "";
            if (state.Trim().Length == 0) state = "（当前估算不可用）";
            if (state == _calStateSig) return;
            _calStateSig = state;

            _calStateList.Children.Clear();
            string[] lines = state.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                Grid g = new Grid();
                g.Margin = new Thickness(0, 2, 0, 2);
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                Border dot = new Border();
                dot.Width = 3;
                dot.Height = 3;
                dot.Background = i == 0 ? _accentB : _lineB;
                dot.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(dot);
                TextBlock t = TacUi.Mono(line, 9.5, i == 0 ? _inkB : _subB);
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                _calStateList.Children.Add(g);
            }
        }

        private void CycleScpFaction(string code)
        {
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec == null) return;
            rec.Faction = NextFaction(rec.Faction);
            if (rec.IsTouched && rec.ChangedAt == DateTime.MinValue) rec.ChangedAt = DateTime.Now;
            RefreshAll();
            ScheduleSave();
        }

        private void ToggleScpKilled(string code)
        {
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec == null) return;
            rec.Killed = !rec.Killed;
            if (rec.Killed)
            {
                rec.ChangedAt = DateTime.Now;
                if (rec.Faction == TacticalLog.NONE) rec.Faction = _bigWaveSide;
                if (ScpKilled != null)
                {
                    try { ScpKilled(rec.Code); } catch { }
                }
                Logger.On("战术", "手动标记 SCP-" + code + " 阵亡（记给 "
                    + TacticalLog.FactionName(rec.Faction) + "）");
            }
            else
            {
                rec.ChangedAt = DateTime.MinValue;
            }
            RefreshAll();
            ScheduleSave();
        }

        private void ToggleScpPresent(string code)
        {
            TacticalLog.ScpRecord rec = _log.Find(code);
            if (rec == null) return;
            rec.Present = !rec.Present;
            RefreshAll();
            ScheduleSave();
        }

        private void ToggleFound079()
        {
            TacticalLog.ScpRecord rec = _log.Find("079");
            if (rec == null) return;
            rec.Present = !rec.Present;
            RefreshAll();
            ScheduleSave();
        }

        private void CycleGenFaction(int index)
        {
            TacticalLog.Generator gen = _log.Generators[index];
            gen.Faction = NextFaction(gen.Faction);
            RefreshAll();
            ScheduleSave();
        }

        private void ToggleGenerator(int index)
        {
            TacticalLog.Generator gen = _log.Generators[index];
            if (gen.Done) return;

            if (gen.Running)
            {
                gen.Running = false;
                gen.ManualRemaining = -1;
                if (_genRows[index] != null) _genRows[index].Remaining.Text = "";
            }
            else
            {
                double d = Num(_genRows[index].Base.Text);
                if (d <= 0) d = 120;
                gen.Running = true;
                gen.StartedAt = DateTime.Now;
                gen.Duration = (int)d;
                gen.ManualRemaining = (int)d;
                if (gen.Faction == TacticalLog.NONE) gen.Faction = _bigWaveSide;
                if (_genRows[index] != null) _genRows[index].Remaining.Text = ((int)d).ToString();
            }
            _genPending[index] = false;
            RefreshAll();
            ScheduleSave();
        }

        private string NextFaction(string f)
        {
            if (f == TacticalLog.NONE) return TacticalLog.MTF;
            if (f == TacticalLog.MTF) return TacticalLog.CHAOS;
            return TacticalLog.NONE;
        }

        private void ToggleLatestWaveType()
        {
            try
            {
                if (_log.ReclassifyLatestWave())
                {
                    Logger.On("战术", "手动切换最近一波类型");
                    RefreshAll();
                    ScheduleSave();
                }
            }
            catch (Exception ex) { Logger.Err("战术", "切换最近一波类型失败", ex); }
        }

        private void ToggleWaveKind(int slot)
        {
            int index = _waveFirst + slot;
            if (slot < 0 || index < 0 || index >= _log.WaveHistory.Count) return;
            if (_log.ToggleWaveType(index))
            {
                RefreshAll();
                ScheduleSave();
            }
        }

        private void CycleWaveFaction(int slot)
        {
            int index = _waveFirst + slot;
            if (slot < 0 || index < 0 || index >= _log.WaveHistory.Count) return;
            if (_log.ToggleWaveFaction(index))
            {
                RefreshAll();
                ScheduleSave();
            }
        }

        private void RemoveWave(int slot)
        {
            int index = _waveFirst + slot;
            if (slot < 0 || index < 0 || index >= _log.WaveHistory.Count) return;
            if (_log.RemoveWave(index))
            {
                RefreshAll();
                ScheduleSave();
            }
        }

        private void RecordObjective(string key, TicketModel.Objective kind, string faction)
        {
            try
            {
                int gained = _log.Tickets.Record(kind, faction, _log.Has079,
                    TicketModel.Info(kind).Name);
                int n;
                _manualCounts.TryGetValue(key, out n);
                _manualCounts[key] = n + 1;
                _objOrder.Add(key);

                Logger.On("战术", "手动登记：" + TicketModel.FactionName(faction) + " "
                    + TicketModel.Info(kind).Name + " → 影响力 +" + gained);
                RefreshAll();
                ScheduleSave();
            }
            catch (Exception ex) { Logger.Err("战术", "登记目标失败", ex); }
        }

        private void UndoLastObjective()
        {
            try
            {
                if (!_log.Tickets.UndoLast()) return;
                if (_objOrder.Count > 0)
                {
                    string key = _objOrder[_objOrder.Count - 1];
                    _objOrder.RemoveAt(_objOrder.Count - 1);
                    int n;
                    if (_manualCounts.TryGetValue(key, out n) && n > 0) _manualCounts[key] = n - 1;
                }
                RefreshAll();
                ScheduleSave();
            }
            catch (Exception ex) { Logger.Err("战术", "撤销登记失败", ex); }
        }

        private void ApplyCalibration()
        {
            try
            {
                string round = _calRound.Text.Trim();
                string sup = _calSup.Text.Trim();
                string dec = _calDec.Text.Trim();
                string dm = _calDm.Text.Trim();
                double v;

                if (round.Length > 0)
                {
                    if (!TimeInput.TryMinutes(round, out v))
                    { SetCalMsg("回合时间读不懂：填 11.22 或 6:10 或 370"); return; }
                    SetCalMsg(DoCalibrate("round", v));
                    ClearCalInputs();
                    return;
                }
                if (sup.Length > 0)
                {
                    if (!TimeInput.TrySeconds(sup, out v))
                    { SetCalMsg("支援剩余填秒数，比如 95"); return; }
                    SetCalMsg(DoCalibrate("support", v));
                    ClearCalInputs();
                    return;
                }
                if (dec.Length > 0)
                {
                    if (!TimeInput.TrySeconds(dec, out v))
                    { SetCalMsg("净化剩余填秒数，比如 320"); return; }
                    SetCalMsg(DoCalibrate("decon", v));
                    ClearCalInputs();
                    return;
                }
                if (dm.Length > 0)
                {
                    if (!TimeInput.TrySeconds(dm, out v))
                    { SetCalMsg("终焉协议填秒数，比如 120"); return; }
                    SetCalMsg(DoCalibrate("deadman", v));
                    ClearCalInputs();
                    return;
                }
                SetCalMsg("四个框都是空的：填一项");
            }
            catch (Exception ex) { Logger.Err("战术", "校准失败", ex); SetCalMsg("校准失败：" + ex.Message); }
        }

        private string DoCalibrate(string kind, double sec)
        {
            if (TimeCalibrate == null) return "没有接上校准入口";
            try { TimeCalibrate(kind, sec); }
            catch (Exception ex)
            {
                Logger.Err("校准", "面板校准失败", ex);
                return "校准失败：" + ex.Message;
            }
            string name = kind == "round" ? "回合已进行"
                : kind == "support" ? "支援剩余"
                : kind == "decon" ? "净化剩余" : "终焉协议剩余";
            Logger.On("校准", "新版战术面板：" + name + " = " + (int)Math.Round(sec) + " 秒");
            RefreshAll();
            return "已校准：" + name + " " + (int)Math.Round(sec) + " 秒";
        }

        private void SetCalMsg(string text)
        {
            if (_calMsg == null) return;
            _calMsg.Text = text == null ? "" : text;
            _calMsg.Foreground = text != null && text.StartsWith("已校准") ? _goodB : _warnB;
        }

        private void ClearCalInputs()
        {
            _calRound.Text = "";
            _calSup.Text = "";
            _calDec.Text = "";
            _calDm.Text = "";
        }
    }
}
