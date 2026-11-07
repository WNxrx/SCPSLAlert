using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IO = System.IO;

namespace SCPSLAlert
{
    /// <summary>一张亮度参考图：可能来自游戏截图，也可能是运行时生成的校色卡。</summary>
    internal sealed class BrightnessReferenceItem
    {
        public string Path = "";
        public ImageSource Image;
        public double Luma;
        public bool Generated;
        public string Caption = "";
    }

    /// <summary>7 档亮度参考的解析结果。</summary>
    internal sealed class BrightnessReferenceSet
    {
        public List<BrightnessReferenceItem> Items = new List<BrightnessReferenceItem>();
        public string SourceLabel = "";
        public string SourceDetail = "";
        public int CustomCount;
        public int InvalidCount;
    }

    /// <summary>
    /// 采集区“7 张亮度参考”的来源解析。
    ///
    /// 优先级：
    ///   1. 逐张替换（[Capture] ReferenceFiles，7 个绝对路径，空位跳过）；
    ///   2. 整体替换目录（[Capture] ReferenceDir）；
    ///   3. 自动找 Steam / 图片目录里的游戏截图；
    ///   4. 都没有时，运行时生成 7 张灰阶校色卡（不依赖外部文件）。
    ///
    /// 截图只取最近 48 张候选，按 480px 宽解码后算平均亮度，再取 7 个亮度分位。
    /// 结果缓存 30 秒，避免切【曲线调整区】/【采集区】时反复解码。
    /// </summary>
    internal static class BrightnessReferenceLibrary
    {
        public const int SlotCount = 7;
        private const int MaxCandidates = 48;
        private const int DecodeWidth = 480;

        private sealed class CacheEntry
        {
            public BrightnessReferenceSet Set;
            public DateTime At;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public static void InvalidateCache()
        {
            Cache.Clear();
        }

        public static string[] ReadSlots(Config cfg)
        {
            string[] slots = new string[SlotCount];
            string raw = cfg == null ? "" : (cfg.CaptureReferenceFiles ?? "");
            string[] parts = raw.Split(new char[] { ';' }, StringSplitOptions.None);
            for (int i = 0; i < SlotCount; i++)
                slots[i] = i < parts.Length ? (parts[i] ?? "").Trim() : "";
            return slots;
        }

        public static string JoinSlots(string[] slots)
        {
            string[] copy = new string[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                copy[i] = slots != null && i < slots.Length && slots[i] != null ? slots[i].Trim() : "";
            return string.Join(";", copy);
        }

        public static BrightnessReferenceSet Resolve(Config cfg)
        {
            string customDir = cfg == null ? "" : (cfg.CaptureReferenceDir ?? "").Trim();
            string[] slots = ReadSlots(cfg);
            List<string> autoDirs = customDir.Length > 0 ? new List<string>() : FindAutoDirs();

            string key = customDir + "\u0001" + (cfg == null ? "" : (cfg.CaptureReferenceFiles ?? ""))
                + "\u0001" + string.Join("|", autoDirs.ToArray());
            CacheEntry hit;
            if (Cache.TryGetValue(key, out hit) && (DateTime.UtcNow - hit.At).TotalSeconds < 30)
                return hit.Set;

            BrightnessReferenceSet set = new BrightnessReferenceSet();

            List<string> candidates = null;
            string baseLabel;
            string baseDetail;

            if (customDir.Length > 0)
            {
                candidates = ScanDirs(new string[] { customDir });
                if (candidates.Count >= SlotCount)
                {
                    baseLabel = "自定义目录";
                    baseDetail = "目录：" + customDir;
                }
                else
                {
                    baseLabel = "校色卡";
                    baseDetail = "自定义目录里可用图片不足 7 张（找到 " + candidates.Count
                        + " 张），已回退为运行时校色卡。";
                    candidates = null;
                }
            }
            else
            {
                candidates = ScanDirs(autoDirs);
                if (candidates.Count >= SlotCount)
                {
                    baseLabel = "自动截图";
                    baseDetail = "自动找到：" + string.Join("；", autoDirs.Take(2).ToArray());
                }
                else
                {
                    baseLabel = "校色卡";
                    baseDetail = "未找到足够 7 张的游戏截图，已生成 7 张运行时校色卡；"
                        + "可用「整体更换」指定截图目录。";
                    candidates = null;
                }
            }

            if (candidates != null)
            {
                set.Items = PickSeven(candidates);
                if (set.Items.Count < SlotCount)
                {
                    baseLabel = "校色卡";
                    baseDetail = "截图解码失败（可用 " + set.Items.Count + " 张），已回退为运行时校色卡。";
                    set.Items = GenerateCards();
                }
            }
            else
            {
                set.Items = GenerateCards();
            }

            // 逐张替换：只覆盖有效路径；失效路径保留原档并计数。
            for (int i = 0; i < SlotCount && i < set.Items.Count; i++)
            {
                string p = slots[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (!File.Exists(p))
                {
                    set.InvalidCount++;
                    continue;
                }
                BrightnessReferenceItem custom = LoadItem(p);
                if (custom == null)
                {
                    set.InvalidCount++;
                    continue;
                }
                custom.Caption = "REF " + (i + 1).ToString("00");
                set.Items[i] = custom;
                set.CustomCount++;
            }

            set.SourceLabel = baseLabel;
            set.SourceDetail = baseDetail;
            if (set.CustomCount > 0)
                set.SourceLabel += " + 逐张替换";
            if (set.InvalidCount > 0)
                set.SourceDetail += " 有 " + set.InvalidCount + " 个逐张路径失效，已保留原档。";

            Cache[key] = new CacheEntry { Set = set, At = DateTime.UtcNow };
            return set;
        }

        /// <summary>候选图片文件（按修改时间倒序，最多 48 张）。</summary>
        private static List<string> ScanDirs(IEnumerable<string> dirs)
        {
            List<FileInfo> files = new List<FileInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dirs == null) return new List<string>();

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    string[] all = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < all.Length; i++)
                    {
                        string ext = IO.Path.GetExtension(all[i]);
                        if (!IsImage(ext)) continue;
                        if (!seen.Add(all[i])) continue;
                        try { files.Add(new FileInfo(all[i])); } catch { }
                    }
                }
                catch { }
            }

            files.Sort(delegate(FileInfo a, FileInfo b)
            {
                int c = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                return c != 0 ? c : string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase);
            });

            List<string> result = new List<string>();
            for (int i = 0; i < files.Count && i < MaxCandidates; i++)
                result.Add(files[i].FullName);
            return result;
        }

        private static bool IsImage(string ext)
        {
            return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static List<BrightnessReferenceItem> PickSeven(List<string> paths)
        {
            List<BrightnessReferenceItem> scored = new List<BrightnessReferenceItem>();
            for (int i = 0; i < paths.Count; i++)
            {
                BrightnessReferenceItem item = LoadItem(paths[i]);
                if (item != null) scored.Add(item);
            }
            if (scored.Count < SlotCount) return scored;

            scored.Sort(delegate(BrightnessReferenceItem a, BrightnessReferenceItem b)
            {
                int c = a.Luma.CompareTo(b.Luma);
                return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });

            List<BrightnessReferenceItem> picked = new List<BrightnessReferenceItem>();
            int last = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                int idx = (int)Math.Round(i * (scored.Count - 1) / (double)(SlotCount - 1));
                if (idx <= last) idx = last + 1;
                if (idx >= scored.Count) idx = scored.Count - 1;
                last = idx;
                BrightnessReferenceItem item = scored[idx];
                item.Caption = "REF " + (i + 1).ToString("00");
                picked.Add(item);
            }
            return picked;
        }

        private static BrightnessReferenceItem LoadItem(string path)
        {
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = DecodeWidth;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                double luma = MeasureLuma(bmp);
                return new BrightnessReferenceItem
                {
                    Path = path,
                    Image = bmp,
                    Luma = luma,
                    Generated = false,
                    Caption = "REF"
                };
            }
            catch { return null; }
        }

        private static double MeasureLuma(BitmapSource source)
        {
            try
            {
                FormatConvertedBitmap bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth;
                int h = bgra.PixelHeight;
                if (w < 1 || h < 1) return 0;
                int stride = w * 4;
                byte[] pixels = new byte[stride * h];
                bgra.CopyPixels(pixels, stride, 0);

                double sum = 0;
                int n = 0;
                for (int y = 0; y < h; y += 4)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x += 4)
                    {
                        int p = row + x * 4;
                        sum += 0.2126 * pixels[p + 2] + 0.7152 * pixels[p + 1] + 0.0722 * pixels[p];
                        n++;
                    }
                }
                return n == 0 ? 0 : sum / n;
            }
            catch { return 0; }
        }

        private static List<BrightnessReferenceItem> GenerateCards()
        {
            double[] levels = { 0.08, 0.16, 0.26, 0.38, 0.52, 0.68, 0.86 };
            List<BrightnessReferenceItem> list = new List<BrightnessReferenceItem>();
            for (int i = 0; i < levels.Length; i++)
            {
                BrightnessReferenceItem item = MakeCalibrationCard(i, levels[i]);
                if (item != null) list.Add(item);
            }
            return list;
        }

        /// <summary>运行时画一张 480×270 的灰阶校色卡，不写盘、不依赖外部图片。</summary>
        private static BrightnessReferenceItem MakeCalibrationCard(int index, double level)
        {
            try
            {
                const int W = 480;
                const int H = 270;
                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    LinearGradientBrush bg = new LinearGradientBrush(
                        Color.FromRgb(7, 9, 10), Color.FromRgb(16, 20, 22), 90);
                    dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));

                    Pen hair = new Pen(new SolidColorBrush(Color.FromArgb(55, 120, 145, 150)), 1);
                    for (int x = 0; x < W; x += 40)
                        dc.DrawLine(hair, new Point(x, 0), new Point(x, 7));
                    for (int y = 0; y < H; y += 30)
                        dc.DrawLine(hair, new Point(0, y), new Point(7, y));

                    byte v = (byte)Math.Round(Math.Max(0, Math.Min(1, level)) * 255);
                    Brush patch = new SolidColorBrush(Color.FromRgb(v, v, v));
                    Rect patchRect = new Rect(150, 72, 180, 108);
                    dc.DrawRectangle(patch, null, patchRect);
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1),
                        new Rect(150.5, 72.5, 179, 107));

                    FormattedText code = Text("CAL " + (index + 1).ToString("00"),
                        "Consolas", 12, Color.FromRgb(210, 230, 225));
                    dc.DrawText(code, new Point(22, 18));

                    FormattedText value = Text(((int)Math.Round(level * 100)) + "%",
                        "Consolas", 20, Color.FromRgb(235, 245, 242));
                    dc.DrawText(value, new Point(22, 38));

                    LinearGradientBrush ramp = new LinearGradientBrush(Colors.Black, Colors.White, 0);
                    dc.DrawRectangle(ramp, null, new Rect(40, 222, 400, 10));
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1),
                        new Rect(40, 222, 400, 10));

                    FormattedText note = Text("LUMA REFERENCE  /  " + (index + 1) + " OF 7",
                        "Consolas", 8, Color.FromRgb(120, 150, 145));
                    dc.DrawText(note, new Point(40, 242));
                }

                RenderTargetBitmap rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();

                return new BrightnessReferenceItem
                {
                    Path = "",
                    Image = rtb,
                    Luma = level * 255.0,
                    Generated = true,
                    Caption = "CAL " + (index + 1).ToString("00")
                };
            }
            catch { return null; }
        }

        private static FormattedText Text(string text, string family, double size, Color color)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size, new SolidColorBrush(color), 1.0);
        }

        /// <summary>Steam / 图片目录里的游戏截图。只查常见位置，不扫全盘。</summary>
        public static List<string> FindAutoDirs()
        {
            List<string> dirs = new List<string>();
            string steam = ReadSteamPath();
            AddSteamScreenshots(steam, dirs);
            AddSteamScreenshots(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"), dirs);
            AddSteamScreenshots(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"), dirs);

            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            AddDir(dirs, System.IO.Path.Combine(pictures, "SCP Secret Laboratory"));
            AddDir(dirs, System.IO.Path.Combine(pictures, "SCP-SL"));
            AddDir(dirs, System.IO.Path.Combine(pictures, "Steam"));
            return dirs;
        }

        private static string ReadSteamPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key == null) return "";
                    object v = key.GetValue("SteamPath");
                    return v == null ? "" : v.ToString();
                }
            }
            catch { return ""; }
        }

        private static void AddSteamScreenshots(string steamRoot, List<string> dirs)
        {
            if (string.IsNullOrEmpty(steamRoot)) return;
            string userdata = System.IO.Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata)) return;
            try
            {
                string[] users = Directory.GetDirectories(userdata);
                for (int i = 0; i < users.Length; i++)
                {
                    string remote = System.IO.Path.Combine(users[i], "760", "remote", "700330");
                    AddDir(dirs, System.IO.Path.Combine(remote, "screenshots"));
                    AddDir(dirs, remote);
                }
            }
            catch { }
        }

        private static void AddDir(List<string> dirs, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            for (int i = 0; i < dirs.Count; i++)
                if (string.Equals(dirs[i], dir, StringComparison.OrdinalIgnoreCase)) return;
            dirs.Add(dir);
        }
    }

    internal sealed partial class WpfVigilDataWindow
    {
        /// <summary>采集区最下方的“7 档亮度参考”。采集完成后仍保留，供与本次采集对比。</summary>
        private UIElement BuildReferenceCard()
        {
            StackPanel body;
            VigilCard card = AdvancedCard("REFERENCE  /  亮度参考", "7 STEP", out body);

            BrightnessReferenceSet set = BrightnessReferenceLibrary.Resolve(_cfg);

            TextBlock desc = new TextBlock();
            desc.Text = set.SourceLabel;
            desc.FontSize = 10.5;
            desc.Foreground = Accent2;
            desc.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(desc);

            TextBlock detail = new TextBlock();
            detail.Text = set.SourceDetail;
            detail.FontSize = 9;
            detail.Foreground = Muted;
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(detail);

            WrapPanel actions = AdvActions();
            actions.Children.Add(CommandButton("\uE8A5", "整体更换…",
                delegate { ChooseReferenceFolder(); }, Sub));
            actions.Children.Add(CommandButton("\uE72C", "重新扫描",
                delegate
                {
                    BrightnessReferenceLibrary.InvalidateCache();
                    ShowBrightnessPage(1);
                }, Sub));
            actions.Children.Add(CommandButton("\uE7A7", "恢复自动",
                delegate { ResetReference(); }, Muted));
            body.Children.Add(actions);

            WrapPanel wrap = new WrapPanel();
            for (int i = 0; i < set.Items.Count; i++)
                wrap.Children.Add(ReferenceThumb(set.Items[i], i));
            body.Children.Add(wrap);

            // 逐张替换放在折叠区：普通用户只看到“整体更换”，需要精调再展开。
            Button advanced = CommandButton("\uE70F", "逐张替换（高级）", null, Muted);
            advanced.Margin = new Thickness(0, 8, 0, 0);
            StackPanel slots = new StackPanel();
            slots.Visibility = Visibility.Collapsed;
            slots.Margin = new Thickness(0, 6, 0, 0);
            advanced.Click += delegate
            {
                slots.Visibility = slots.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            };
            body.Children.Add(advanced);

            string[] paths = BrightnessReferenceLibrary.ReadSlots(_cfg);
            for (int i = 0; i < BrightnessReferenceLibrary.SlotCount; i++)
            {
                BrightnessReferenceItem current = i < set.Items.Count ? set.Items[i] : null;
                slots.Children.Add(ReferenceSlotRow(i, paths[i], current));
            }
            body.Children.Add(slots);
            return card;
        }

        private UIElement ReferenceThumb(BrightnessReferenceItem item, int index)
        {
            StackPanel box = new StackPanel();
            box.Width = 158;
            box.Margin = new Thickness(0, 0, 8, 10);

            Border frame = new Border();
            frame.Height = 89;
            frame.Background = PanelAlt;
            frame.BorderThickness = new Thickness(1);
            frame.BorderBrush = Line;
            frame.CornerRadius = new CornerRadius(4);
            frame.ClipToBounds = true;
            if (item != null && item.Image != null)
            {
                Image view = new Image();
                view.Source = item.Image;
                view.Stretch = Stretch.UniformToFill;
                frame.Child = view;
            }
            else
            {
                TextBlock missing = new TextBlock();
                missing.Text = "缺图";
                missing.Foreground = Muted;
                missing.HorizontalAlignment = HorizontalAlignment.Center;
                missing.VerticalAlignment = VerticalAlignment.Center;
                frame.Child = missing;
            }
            box.Children.Add(frame);

            TextBlock label = new TextBlock();
            label.FontFamily = Mono;
            label.FontSize = 8.5;
            label.Foreground = Sub;
            label.Margin = new Thickness(2, 3, 0, 0);
            string luma = item == null ? "—" : Math.Round(item.Luma / 255.0 * 100).ToString("0");
            label.Text = "REF " + (index + 1).ToString("00") + "　" + luma + "%　"
                + (item == null ? "缺图" : (item.Generated ? "校色卡" : "游戏截图"));
            box.Children.Add(label);

            TextBlock file = new TextBlock();
            file.FontSize = 7.5;
            file.Foreground = Muted;
            file.TextTrimming = TextTrimming.CharacterEllipsis;
            file.Text = item == null || item.Generated
                ? "运行时生成"
                : IO.Path.GetFileName(item.Path);
            file.ToolTip = item == null ? "" : item.Path;
            box.Children.Add(file);
            return box;
        }

        private UIElement ReferenceSlotRow(int index, string path, BrightnessReferenceItem current)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 3);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock code = MonoText("REF " + (index + 1).ToString("00"), 8.5, Accent2);
            code.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(code);

            TextBlock name = new TextBlock();
            name.FontSize = 9;
            name.Foreground = Sub;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                name.Text = IO.Path.GetFileName(path);
                name.ToolTip = path;
            }
            else
            {
                name.Text = current == null ? "自动 / 校色卡"
                    : (current.Generated ? "自动 · 校色卡" : "自动 · " + IO.Path.GetFileName(current.Path));
            }
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            int slot = index;
            Button pick = CommandButton("\uE8E5", "选择…", delegate { ChooseReferenceSlot(slot); }, Sub);
            pick.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(pick, 2);
            row.Children.Add(pick);

            Button clear = CommandButton("\uE711", "清除", delegate { ClearReferenceSlot(slot); }, Muted);
            clear.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(clear, 3);
            row.Children.Add(clear);
            return row;
        }

        private void ChooseReferenceFolder()
        {
            using (System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择游戏截图目录；软件会按亮度从里面挑 7 张作为参考。";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(_cfg.CaptureReferenceDir) && Directory.Exists(_cfg.CaptureReferenceDir))
                    dlg.SelectedPath = _cfg.CaptureReferenceDir;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                _cfg.CaptureReferenceDir = dlg.SelectedPath;
                _cfg.CaptureReferenceFiles = "";
                BrightnessReferenceLibrary.InvalidateCache();
                TouchSave();
                ShowBrightnessPage(1);
            }
        }

        private void ResetReference()
        {
            _cfg.CaptureReferenceDir = "";
            _cfg.CaptureReferenceFiles = "";
            BrightnessReferenceLibrary.InvalidateCache();
            TouchSave();
            ShowBrightnessPage(1);
        }

        private void ChooseReferenceSlot(int slot)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*";
            dlg.Title = "选择第 " + (slot + 1) + " 档亮度参考图";
            if (dlg.ShowDialog(this) != true) return;

            string[] slots = BrightnessReferenceLibrary.ReadSlots(_cfg);
            slots[slot] = dlg.FileName;
            _cfg.CaptureReferenceFiles = BrightnessReferenceLibrary.JoinSlots(slots);
            BrightnessReferenceLibrary.InvalidateCache();
            TouchSave();
            ShowBrightnessPage(1);
        }

        private void ClearReferenceSlot(int slot)
        {
            string[] slots = BrightnessReferenceLibrary.ReadSlots(_cfg);
            slots[slot] = "";
            _cfg.CaptureReferenceFiles = BrightnessReferenceLibrary.JoinSlots(slots);
            BrightnessReferenceLibrary.InvalidateCache();
            TouchSave();
            ShowBrightnessPage(1);
        }
    }
}
