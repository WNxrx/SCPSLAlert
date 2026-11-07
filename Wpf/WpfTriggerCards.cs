using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCPSLAlert
{
    /// <summary>
    /// 第 7 批：触发点三区卡片（条件 / 动作 / 停止）。
    /// 条件与停止都支持拖入、AND/OR 切换；动作支持音频 / 图片 / 文字混播。
    /// </summary>
    internal sealed partial class WpfVigilDataWindow
    {
        private UIElement BuildTriggerCardV2(AudioTrigger t, int index)
        {
            Border card = new Border();
            card.BorderBrush = Line;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(4);
            card.Padding = new Thickness(12, 10, 12, 12);
            card.Margin = new Thickness(0, 4, 0, 6);
            StackPanel root = new StackPanel();

            // ---- 卡片头 ----
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBox name = VigilInput(t.Name, 0);
            name.FontSize = 11.5;
            name.FontWeight = FontWeights.Bold;
            name.TextChanged += delegate { t.Name = name.Text; SaveAudioTriggers(); };
            head.Children.Add(name);
            VigilToggle on = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            on.IsChecked = t.Enabled;
            on.CheckedChanged += delegate { t.Enabled = on.IsChecked; SaveAudioTriggers(); };
            Grid.SetColumn(on, 1);
            head.Children.Add(on);
            Button del = IconButton("\uE711", delegate
            {
                TriggerActionOverlay.HideCard(index + ":" + t.Name);
                FloatingTextOverlay.HideCardPrefix(index + ":" + t.Name + ":float:");
                _cfg.AudioTriggers.Remove(t);
                SaveAudioTriggers();
                RebuildAudioTriggers();
            }, Danger);
            del.ToolTip = "删除触发点";
            Grid.SetColumn(del, 2);
            head.Children.Add(del);
            root.Children.Add(head);

            // ---- 三区 ----
            Grid zones = new Grid();
            zones.Margin = new Thickness(0, 10, 0, 0);
            zones.ColumnDefinitions.Add(new ColumnDefinition());
            zones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            zones.ColumnDefinitions.Add(new ColumnDefinition());
            zones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            zones.ColumnDefinitions.Add(new ColumnDefinition());

            zones.Children.Add(BuildCondZone(t));
            UIElement act = BuildActionZone(t, index);
            Grid.SetColumn(act, 2);
            zones.Children.Add(act);
            UIElement stop = BuildStopZone(t);
            Grid.SetColumn(stop, 4);
            zones.Children.Add(stop);

            root.Children.Add(zones);
            card.Child = root;
            return card;
        }

        private Border ZoneFrame(string title, string hint)
        {
            Border b = new Border();
            b.BorderBrush = Line;
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(3);
            b.Padding = new Thickness(8, 6, 8, 8);
            b.Background = Brushes.Transparent;
            StackPanel sp = new StackPanel();
            TextBlock h = MonoText(title, 8, Muted);
            sp.Children.Add(h);
            TextBlock hintText = MonoText(hint, 8, Muted);
            hintText.Margin = new Thickness(0, 1, 0, 4);
            sp.Children.Add(hintText);
            b.Child = sp;
            b.Tag = sp;
            return b;
        }

        private StackPanel ZoneBody(Border zone)
        {
            return zone.Tag as StackPanel;
        }

        private void WireZoneDrop(Border zone, Action<string> onDrop)
        {
            zone.AllowDrop = true;
            Brush normal = zone.BorderBrush;
            zone.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                zone.BorderBrush = Accent;
                zone.Background = AccentSoftBrush();
            };
            zone.DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            zone.DragLeave += delegate
            {
                zone.BorderBrush = normal;
                zone.Background = Brushes.Transparent;
            };
            zone.Drop += delegate(object s, DragEventArgs e)
            {
                string id = e.Data.GetData(DataFormats.StringFormat) as string;
                zone.BorderBrush = normal;
                zone.Background = Brushes.Transparent;
                e.Handled = true;
                if (!string.IsNullOrEmpty(id)) onDrop(id);
            };
        }

        private UIElement BuildCondZone(AudioTrigger t)
        {
            Border zone = ZoneFrame("条件", "把右侧事件拖进来；全部/任一满足");
            StackPanel body = ZoneBody(zone);

            StackPanel toggleRow = new StackPanel();
            toggleRow.Orientation = Orientation.Horizontal;
            toggleRow.Margin = new Thickness(0, 0, 0, 4);
            VigilSelect mode = new VigilSelect(PanelAlt, Line, Ink, Accent, 92);
            mode.SetItems(new string[] { "全部满足 (AND)", "任一满足 (OR)" }, t.MatchAny ? 1 : 0);
            mode.SelectionChanged += delegate { t.MatchAny = mode.SelectedIndex == 1; SaveAudioTriggers(); };
            toggleRow.Children.Add(mode);
            body.Children.Add(toggleRow);

            for (int i = 0; i < t.Conds.Count; i++)
                body.Children.Add(BuildAudioCondRow(t, t.Conds[i], false));

            TextBlock empty = MonoText(t.Conds.Count == 0 ? "还没有条件" : "", 8.5, Muted);
            body.Children.Add(empty);
            WireZoneDrop(zone, delegate(string id)
            {
                AddAudioCondition(t, id);
            });
            return zone;
        }

        private UIElement BuildStopZone(AudioTrigger t)
        {
            Border zone = ZoneFrame("停止", "命中就停掉本卡片所有动作");
            StackPanel body = ZoneBody(zone);

            StackPanel toggleRow = new StackPanel();
            toggleRow.Orientation = Orientation.Horizontal;
            toggleRow.Margin = new Thickness(0, 0, 0, 4);
            VigilSelect mode = new VigilSelect(PanelAlt, Line, Ink, Accent, 88);
            mode.SetItems(new string[] { "任一 (OR)", "全部 (AND)" }, t.StopMatchAny ? 0 : 1);
            mode.SelectionChanged += delegate { t.StopMatchAny = mode.SelectedIndex == 0; SaveAudioTriggers(); };
            toggleRow.Children.Add(mode);
            body.Children.Add(toggleRow);

            for (int i = 0; i < t.StopConds.Count; i++)
                body.Children.Add(BuildAudioCondRow(t, t.StopConds[i], true));

            TextBlock empty = MonoText(t.StopConds.Count == 0 ? "没有停止条件" : "", 8.5, Muted);
            body.Children.Add(empty);
            WireZoneDrop(zone, delegate(string id)
            {
                AddStopCondition(t, id);
            });
            return zone;
        }

        private void AddStopCondition(AudioTrigger t, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            AudioCond c = new AudioCond();
            AudioEventInfo info = AudioEvents.Find(id);
            c.Event = id;
            c.Op = info != null && info.IsPulse ? "==" : (info != null && info.Unit == "秒" ? "<=" : ">=");
            c.Value = 1;
            t.StopConds.Add(c);
            SaveAudioTriggers();
            RebuildAudioTriggers();
        }

        // ---------------- 动作区 ----------------

        private UIElement BuildActionZone(AudioTrigger t, int index)
        {
            Border zone = ZoneFrame("动作", "可多选：音频 / 图片 / 文字同时触发");
            StackPanel body = ZoneBody(zone);

            List<TriggerAction> acts = t.EffectiveActions();
            if (acts.Count == 0)
                body.Children.Add(MonoText("还没有动作", 8.5, Muted));
            for (int i = 0; i < acts.Count; i++)
            {
                body.Children.Add(BuildActionRow(t, acts[i]));
            }

            WrapPanel add = new WrapPanel();
            add.Margin = new Thickness(0, 4, 0, 0);
            add.Children.Add(CommandButton("\uE8D6", "音频", delegate { AddAction(t, "audio"); }, Sub));
            add.Children.Add(CommandButton("\uE8B9", "图片", delegate { AddAction(t, "image"); }, Sub));
            add.Children.Add(CommandButton("\uE8D2", "文字", delegate { AddAction(t, "text"); }, Sub));
            add.Children.Add(CommandButton("\uE8D2", "漂浮文字", delegate { AddAction(t, "float"); }, Sub));
            add.Children.Add(CommandButton("\uE945", "特效", delegate { AddAction(t, "effect"); }, Sub));
            body.Children.Add(add);
            return zone;
        }

        private void AddAction(AudioTrigger t, string kind)
        {
            TriggerAction a = new TriggerAction();
            a.Kind = kind;
            if (kind == "audio") a.Clip = t.Clip;
            if (kind == "text") { a.Text = " " ; a.FontSize = 32; }
            if (kind == "image") a.Scale = 1;
            if (kind == "float")
            {
                a.Text = "这是一句漂浮文字";
                a.FontSize = 36;
                a.FloatJitterX = 2;
                a.FloatJitterY = 7;
                a.FloatReveal = true;
                a.FloatRevealDelayMs = 45;
                a.FloatLifeMs = 6000;
            }
            if (kind == "effect")
            {
                a.EffectType = "shake";
                a.EffectAmplitude = 8;
                a.EffectDurationMs = 320;
                a.Effects.Add(new TriggerEffect { Type = "shake", Amplitude = 8, DurationMs = 320 });
            }
            t.Actions.Add(a);
            SaveAudioTriggers();
            RebuildAudioTriggers();
        }

        private UIElement BuildActionRow(AudioTrigger t, TriggerAction a)
        {
            Border row = new Border();
            row.BorderBrush = Line;
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(0, 4, 0, 6);
            StackPanel sp = new StackPanel();

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = MonoText(a.KindName() + "　" + a.Summary(), 8.5, Sub);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(label);
            Button remove = IconButton("\uE711", delegate
            {
                t.Actions.Remove(a);
                SaveAudioTriggers();
                RebuildAudioTriggers();
            }, Danger);
            remove.ToolTip = "删除这个动作";
            Grid.SetColumn(remove, 1);
            head.Children.Add(remove);
            sp.Children.Add(head);

            if (a.Kind == "audio")
            {
                List<string> clips = new List<string>();
                clips.Add("(默认)");
                string[] files = AudioLibrary.List();
                for (int i = 0; i < files.Length; i++) clips.Add(files[i]);
                int idx = 0;
                for (int i = 0; i < files.Length; i++) if (files[i] == a.Clip) idx = i + 1;
                VigilSelect clip = new VigilSelect(PanelAlt, Line, Ink, Accent, 190);
                clip.SetItems(clips, idx);
                clip.Margin = new Thickness(0, 4, 0, 0);
                clip.SelectionChanged += delegate { a.Clip = clip.SelectedText == "(默认)" ? "" : clip.SelectedText; SaveAudioTriggers(); };
                sp.Children.Add(clip);
            }
            else if (a.Kind == "image")
            {
                StackPanel imgRow = new StackPanel();
                imgRow.Orientation = Orientation.Horizontal;
                imgRow.Margin = new Thickness(0, 4, 0, 0);
                Button pick = CommandButton("\uE8B9", "选择 PNG / GIF", delegate
                {
                    string file = PickTriggerImage();
                    if (file == null) return;
                    a.Image = file;
                    SaveAudioTriggers();
                    RebuildAudioTriggers();
                }, Sub);
                imgRow.Children.Add(pick);
                TextBlock path = MonoText(string.IsNullOrEmpty(a.Image) ? "未选择" : Path.GetFileName(a.Image), 8, Muted);
                path.VerticalAlignment = VerticalAlignment.Center;
                path.Margin = new Thickness(6, 0, 0, 0);
                imgRow.Children.Add(path);
                sp.Children.Add(imgRow);
                sp.Children.Add(NumberLine("缩放 %", (int)Math.Round(a.Scale * 100), 5, 400, delegate(int v) { a.Scale = v / 100.0; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("不透明度 %", (int)Math.Round(a.Opacity * 100), 5, 100, delegate(int v) { a.Opacity = v / 100.0; SaveAudioTriggers(); }));
                sp.Children.Add(PositionLines(a));
            }
            else if (a.Kind == "float")
            {
                Button edit = CommandButton("\uE70F", "打开文字画布…", delegate
                {
                    FloatingTextEditorWindow dlg = new FloatingTextEditorWindow(a, _light);
                    dlg.Owner = this;
                    if (dlg.ShowDialog() == true)
                    {
                        SaveAudioTriggers();
                        RebuildAudioTriggers();
                    }
                }, Accent);
                edit.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(edit);
                sp.Children.Add(NumberLine("左右抖动", (int)Math.Round(a.FloatJitterX), 0, 40,
                    delegate(int v) { a.FloatJitterX = v; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("上下抖动", (int)Math.Round(a.FloatJitterY), 0, 40,
                    delegate(int v) { a.FloatJitterY = v; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("时长", a.FloatLifeMs, 0, 60000,
                    delegate(int v) { a.FloatLifeMs = v; SaveAudioTriggers(); }));
                sp.Children.Add(PositionLines(a));
            }
            else if (a.Kind == "effect")
            {
                if (a.Effects == null) a.Effects = new List<TriggerEffect>();
                if (a.Effects.Count == 0) a.Effects.Add(new TriggerEffect());
                for (int i = 0; i < a.Effects.Count; i++)
                    sp.Children.Add(BuildEffectRow(a, i));
                List<TriggerEffect> catalog = EffectPackStore.All();
                List<string> names = new List<string>();
                for (int i = 0; i < catalog.Count; i++) names.Add(catalog[i].Name());
                VigilSelect pick = new VigilSelect(PanelAlt, Line, Ink, Accent, 150);
                pick.SetItems(names, 0);
                StackPanel addRow = new StackPanel();
                addRow.Orientation = Orientation.Horizontal;
                addRow.Margin = new Thickness(0, 6, 0, 0);
                addRow.Children.Add(pick);
                Button add = CommandButton("\uE710", "添加", delegate
                {
                    int idx = pick.SelectedIndex;
                    if (idx < 0 || idx >= catalog.Count) return;
                    a.Effects.Add(catalog[idx].Clone());
                    SaveAudioTriggers();
                    RebuildAudioTriggers();
                }, Sub);
                add.Margin = new Thickness(6, 0, 0, 0);
                addRow.Children.Add(add);
                Button import = CommandButton("\uE8B5", "导入", delegate
                {
                    Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                    dlg.Filter = "特效包 (*.arctfx;*.zip)|*.arctfx;*.zip|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(this) != true) return;
                    ImportedEffect pack;
                    string err = EffectPackStore.Import(dlg.FileName, out pack);
                    if (!string.IsNullOrEmpty(err) || pack == null)
                    {
                        System.Windows.MessageBox.Show(err ?? "导入失败", "特效包",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    a.Effects.Add(pack.Effect.Clone());
                    SaveAudioTriggers();
                    RebuildAudioTriggers();
                }, Muted);
                import.Margin = new Thickness(6, 0, 0, 0);
                addRow.Children.Add(import);
                Button help = CommandButton("\uE946", "格式", delegate
                {
                    EffectPackHelpWindow w = new EffectPackHelpWindow();
                    w.Owner = this;
                    w.ShowDialog();
                }, Muted);
                help.Margin = new Thickness(6, 0, 0, 0);
                addRow.Children.Add(help);
                sp.Children.Add(addRow);
            }
            else
            {
                TextBox text = VigilInput(a.Text, 0);
                text.FontSize = 10;
                text.Margin = new Thickness(0, 4, 0, 0);
                text.TextChanged += delegate { a.Text = text.Text; SaveAudioTriggers(); };
                sp.Children.Add(text);

                List<string> effects = new List<string> { "纯色", "渐变", "单字", "彩虹", "跑马灯" };
                string[] keys = { "solid", "gradient", "char", "rainbow", "marquee" };
                int effectIndex = Math.Max(0, Array.IndexOf(keys, a.Effect));
                VigilSelect effect = new VigilSelect(PanelAlt, Line, Ink, Accent, 96);
                effect.SetItems(effects, effectIndex);
                effect.Margin = new Thickness(0, 4, 0, 0);
                effect.SelectionChanged += delegate
                {
                    a.Effect = keys[Math.Max(0, Math.Min(keys.Length - 1, effect.SelectedIndex))];
                    SaveAudioTriggers();
                };
                sp.Children.Add(effect);

                StackPanel colors = new StackPanel();
                colors.Orientation = Orientation.Horizontal;
                colors.Margin = new Thickness(0, 4, 0, 0);
                TextBox c1 = VigilInput(a.Color1, 78);
                c1.ToolTip = "主色（#RRGGBB）";
                c1.TextChanged += delegate { a.Color1 = c1.Text; SaveAudioTriggers(); };
                colors.Children.Add(c1);
                TextBox c2 = VigilInput(a.Color2, 78);
                c2.Margin = new Thickness(4, 0, 0, 0);
                c2.ToolTip = "副色（渐变 / 单字 / 跑马灯用）";
                c2.TextChanged += delegate { a.Color2 = c2.Text; SaveAudioTriggers(); };
                colors.Children.Add(c2);
                sp.Children.Add(colors);

                sp.Children.Add(NumberLine("字号", (int)Math.Round(a.FontSize), 8, 120, delegate(int v) { a.FontSize = v; SaveAudioTriggers(); }));

                CheckBox outline = new CheckBox();
                outline.Content = "1px 反差描边";
                outline.IsChecked = a.Outline;
                outline.Foreground = Sub;
                outline.FontSize = 9;
                outline.Margin = new Thickness(0, 4, 0, 0);
                outline.Checked += delegate { a.Outline = true; SaveAudioTriggers(); };
                outline.Unchecked += delegate { a.Outline = false; SaveAudioTriggers(); };
                sp.Children.Add(outline);
                sp.Children.Add(PositionLines(a));
            }

            row.Child = sp;
            return row;
        }

        private UIElement BuildEffectRow(TriggerAction owner, int index)
        {
            TriggerEffect e = owner.Effects[index];
            Border row = new Border();
            row.BorderBrush = Line;
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(0, 4, 0, 6);
            StackPanel sp = new StackPanel();
            row.Child = sp;

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            VigilSelect type = new VigilSelect(PanelAlt, Line, Ink, Accent, 112);
            type.SetItems(new string[] { "抖动", "闪屏", "礼炮", "边缘脉冲" }, TypeIndex(e.Type));
            type.SelectionChanged += delegate
            {
                e.Type = TypeKey(type.SelectedIndex);
                SaveAudioTriggers();
                RebuildAudioTriggers();
            };
            head.Children.Add(type);
            Button remove = IconButton("\uE711", delegate
            {
                owner.Effects.RemoveAt(index);
                SaveAudioTriggers();
                RebuildAudioTriggers();
            }, Danger);
            Grid.SetColumn(remove, 1);
            head.Children.Add(remove);
            sp.Children.Add(head);

            if (e.Type == "shake")
            {
                sp.Children.Add(NumberLine("幅度", e.Amplitude, 0, 24,
                    delegate(int v) { e.Amplitude = v; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("时长", e.DurationMs, 60, 1200,
                    delegate(int v) { e.DurationMs = v; SaveAudioTriggers(); }));
            }
            else if (e.Type == "confetti")
            {
                sp.Children.Add(NumberLine("数量", e.Count, 10, 120,
                    delegate(int v) { e.Count = v; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("时长", e.DurationMs, 400, 1800,
                    delegate(int v) { e.DurationMs = v; SaveAudioTriggers(); }));
                sp.Children.Add(EffectColorRow(e, true));
                sp.Children.Add(EffectColorRow(e, false));
            }
            else
            {
                sp.Children.Add(NumberLine("强度", (int)Math.Round(e.Intensity * 100), 3,
                    e.Type == "flash" ? 35 : 80,
                    delegate(int v) { e.Intensity = v / 100.0; SaveAudioTriggers(); }));
                sp.Children.Add(NumberLine("时长", e.DurationMs, 60, 1600,
                    delegate(int v) { e.DurationMs = v; SaveAudioTriggers(); }));
                sp.Children.Add(EffectColorRow(e, true));
            }
            return row;
        }

        private UIElement EffectColorRow(TriggerEffect e, bool first)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 4, 0, 0);
            TextBlock label = MonoText(first ? "主色" : "副色", 8, Muted);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 4, 0);
            row.Children.Add(label);
            TextBox box = VigilInput(first ? e.Color1 : e.Color2, 78);
            box.TextChanged += delegate
            {
                if (first) e.Color1 = box.Text; else e.Color2 = box.Text;
                SaveAudioTriggers();
            };
            row.Children.Add(box);
            return row;
        }

        private static int TypeIndex(string type)
        {
            if (type == "flash") return 1;
            if (type == "confetti") return 2;
            if (type == "pulse") return 3;
            return 0;
        }

        private static string TypeKey(int index)
        {
            if (index == 1) return "flash";
            if (index == 2) return "confetti";
            if (index == 3) return "pulse";
            return "shake";
        }

        private UIElement PositionLines(TriggerAction a)
        {
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Margin = new Thickness(0, 4, 0, 0);
            TextBlock l1 = MonoText("X%", 8, Muted);
            l1.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(l1);
            VigilNumberField x = new VigilNumberField(_light, Line, Ink, Sub, 54);
            x.Minimum = 0;
            x.Maximum = 100;
            x.Value = (decimal)Math.Round(a.XPercent * 100);
            x.ValueChanged += delegate { a.XPercent = (double)x.Value / 100.0; SaveAudioTriggers(); };
            sp.Children.Add(x);
            TextBlock l2 = MonoText("Y%", 8, Muted);
            l2.Margin = new Thickness(6, 0, 0, 0);
            l2.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(l2);
            VigilNumberField y = new VigilNumberField(_light, Line, Ink, Sub, 54);
            y.Minimum = 0;
            y.Maximum = 100;
            y.Value = (decimal)Math.Round(a.YPercent * 100);
            y.ValueChanged += delegate { a.YPercent = (double)y.Value / 100.0; SaveAudioTriggers(); };
            sp.Children.Add(y);
            return sp;
        }

        private UIElement NumberLine(string label, int value, int min, int max, Action<int> onChange)
        {
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Margin = new Thickness(0, 4, 0, 0);
            TextBlock l = MonoText(label, 8, Muted);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 4, 0);
            sp.Children.Add(l);
            VigilNumberField n = new VigilNumberField(_light, Line, Ink, Sub, 60);
            n.Minimum = min;
            n.Maximum = max;
            n.Value = value;
            n.ValueChanged += delegate { onChange((int)n.Value); };
            sp.Children.Add(n);
            return sp;
        }

        /// <summary>选图片：只收 PNG / GIF，≤10MB，GIF 平均帧率 ≤30。</summary>
        private string PickTriggerImage()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "图片 (*.png;*.gif)|*.png;*.gif";
            if (dlg.ShowDialog(this) != true) return null;
            try
            {
                FileInfo fi = new FileInfo(dlg.FileName);
                if (fi.Length > 10 * 1024 * 1024)
                {
                    System.Windows.MessageBox.Show("图片超过 10MB，已拒绝。", "触发点动作", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                string ext = Path.GetExtension(dlg.FileName);
                if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    GifBitmapDecoder dec = new GifBitmapDecoder(new Uri(dlg.FileName),
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    double total = 0;
                    for (int i = 0; i < dec.Frames.Count; i++)
                    {
                        double delay = 10;
                        if (dec.Frames[i].Metadata is BitmapMetadata)
                        {
                            object d = ((BitmapMetadata)dec.Frames[i].Metadata).GetQuery("/grctlext/Delay");
                            if (d is ushort) delay = (ushort)d;
                        }
                        total += delay / 100.0;
                    }
                    double fps = total <= 0 ? 0 : dec.Frames.Count / total;
                    if (fps > 30.5)
                    {
                        System.Windows.MessageBox.Show("GIF 帧率约 " + fps.ToString("0") + " fps，超过 30 fps 上限，已拒绝。",
                            "触发点动作", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }
                }
                return dlg.FileName;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("读取图片失败：" + ex.Message, "触发点动作", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }
    }
}
