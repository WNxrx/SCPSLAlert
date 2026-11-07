using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SCPSLAlert
{
    internal sealed partial class WpfVigilDataWindow
    {
        // “外观”页：2A 先给主题模式、强调色和实时预览；
        // “更多设置”二级页（完整色板/背景/字号/主题包）在 2B 接进来。
        private bool _appearanceRebuildQueued;
        private bool _cardDesignBuilding;

        private UIElement BuildAppearancePage()
        {
            StackPanel page = new StackPanel();
            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 14);
            TextBlock title = DisplayText("外观", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(title);
            page.Children.Add(head);

            // ---- 第一行：主题（左，宽） + 动效 / 背景（右，窄） ----
            Grid row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });

            StackPanel themeBody;
            VigilCard theme = AppearanceCard("THEME  /  主题", out themeBody);
            theme.Margin = new Thickness(0, 0, 10, 10);
            string[] modeNames = { "跟随系统", "深色", "浅色" };
            int modeIndex = _cfg.UiThemeMode < 0 ? 0 : (_cfg.UiThemeMode == 0 ? 2 : 1);
            themeBody.Children.Add(GeneralRow("主题模式", "跟随系统时自动切深 / 浅色",
                AppearanceSegment(modeNames, modeIndex, delegate(int i)
                {
                    _cfg.UiThemeMode = i == 0 ? -1 : (i == 1 ? 1 : 0);
                    ModernTheme.SetMode(_cfg.UiThemeMode);
                    TouchSave();
                    RebuildForThemeChange();
                })));

            StackPanel accentRow = new StackPanel();
            accentRow.Orientation = Orientation.Horizontal;
            accentRow.VerticalAlignment = VerticalAlignment.Center;
            string current = ThemeTokens.NormalizeHex(_cfg.AccentHex, ThemeTokens.DefaultAccent);
            for (int i = 0; i < ThemeTokens.PresetHex.Length; i++)
            {
                string hex = ThemeTokens.PresetHex[i];
                bool on = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                Border swatch = new Border();
                swatch.Width = 22;
                swatch.Height = 22;
                swatch.CornerRadius = new CornerRadius(3);
                swatch.Background = Solid(ThemeTokens.ParseHex(hex, ThemeTokens.Get(_light).Accent));
                swatch.BorderThickness = new Thickness(on ? 2 : 1);
                swatch.BorderBrush = on ? Ink : Line;
                swatch.Margin = new Thickness(0, 0, 8, 0);
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = ThemeTokens.PresetNames[i] + "  " + hex;
                swatch.MouseLeftButtonUp += delegate { ApplyAccentHex(hex); };
                accentRow.Children.Add(swatch);
            }
            Button custom = CommandButton("\uE790", "自定义…", delegate
            {
                using (System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.Color = System.Drawing.Color.FromArgb(
                        ThemeTokens.For(_light).R, ThemeTokens.For(_light).G, ThemeTokens.For(_light).B);
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        ApplyAccentHex(ThemeTokens.ToHex(System.Drawing.Color.FromArgb(
                            dlg.Color.R, dlg.Color.G, dlg.Color.B)));
                    }
                }
            }, Sub);
            custom.Margin = new Thickness(2, 0, 0, 0);
            accentRow.Children.Add(custom);
            accentRow.Children.Add(MonoText("  " + current, 9, Sub));
            themeBody.Children.Add(GeneralRow("强调色", "预设色块或自定义 HEX；浅色主题自动压深", accentRow));

            StackPanel presets = new StackPanel();
            presets.Orientation = Orientation.Horizontal;
            string[] presetNames = { "深空", "琥珀", "终端", "纸白" };
            string[] presetHex = { "#38D6FF", "#E8A33D", "#5BE38C", "#2E6BE6" };
            int[] presetModes = { 1, 1, 1, 0 };
            for (int pi = 0; pi < presetNames.Length; pi++)
            {
                int idx = pi;
                Button chip = CommandButton("\uE790", presetNames[pi], delegate
                {
                    _cfg.UiThemeMode = presetModes[idx];
                    _cfg.AccentHex = presetHex[idx];
                    ModernTheme.SetMode(_cfg.UiThemeMode);
                    TouchSave();
                    RebuildForThemeChange();
                }, Sub);
                chip.Margin = new Thickness(0, 0, 6, 0);
                presets.Children.Add(chip);
            }
            themeBody.Children.Add(GeneralRow("主题预设", "一键切换配色与明暗", presets));
            themeBody.Children.Add(AppearancePreview());
            Button more = CommandButton("\uE713", "更多设置", delegate { ShowAppearanceDetail(); }, Accent);
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.Margin = new Thickness(0, 10, 0, 0);
            themeBody.Children.Add(more);
            themeBody.Children.Add(ResetButton("恢复主题默认", 1, false));
            themeBody.Children.Add(ResetButton("全部恢复默认", 0, true));
            Grid.SetColumn(theme, 0);
            row1.Children.Add(theme);

            StackPanel side = new StackPanel();
            StackPanel motionBody;
            VigilCard motion = AppearanceCard("MOTION  /  动效", out motionBody);
            motion.Margin = new Thickness(0, 0, 0, 10);
            int motionIndex = _cfg.PanelAnimQuality <= 0 ? 0 : (_cfg.PanelAnimQuality <= 2 ? 1 : 2);
            motionBody.Children.Add(GeneralRow("动效档位", "界面过渡与卡片动画",
                AppearanceSegment(new string[] { "关", "低", "高" }, motionIndex, delegate(int i)
                {
                    _cfg.UiAnimationsEnabled = i > 0;
                    _cfg.PanelAnimQuality = i == 0 ? 0 : (i == 1 ? 2 : 4);
                    UiAnim.Quality = _cfg.PanelAnimQuality;
                    TouchSave();
                })));
            motionBody.Children.Add(GeneralRow("点击特效", "四条线汇聚；双击旋转 45°",
                AdvToggle(_cfg.ClickFxEnabled, delegate(bool v) { _cfg.ClickFxEnabled = v; TouchSave(); })));
            motionBody.Children.Add(GeneralRow("起始缺口", "像素；线段离点击中心的距离",
                AdvNumber(_cfg.ClickFxGap, 0, 240, delegate(int v)
                {
                    _cfg.ClickFxGap = v;
                    if (_clickFx != null) _clickFx.GapSize = v;
                    TouchSave();
                })));
            motionBody.Children.Add(GeneralRow("窗口毛玻璃", "系统亚克力；自定义背景会盖住它，卡片玻璃仍生效", AdvToggle(_cfg.GlassWindow, delegate(bool v) { _cfg.GlassWindow = v; TouchSave(); RebuildForThemeChange(); })));
            motionBody.Children.Add(GeneralRow("卡片玻璃", "让背景透进卡片，并叠加磨砂 / 光源 / 细噪点", AdvToggle(_cfg.GlassCards, delegate(bool v) { _cfg.GlassCards = v; TouchSave(); RebuildForThemeChange(); })));
            motionBody.Children.Add(GeneralRow("玻璃强度", "总强度；影响高光、亮边、噪点和边框", AdvNumber(_cfg.GlassStrength, 0, 100, delegate(int v) { _cfg.GlassStrength = v; TouchSave(); RebuildForThemeChange(); })));
            motionBody.Children.Add(GeneralRow("磨砂程度", "0–100；越大越像毛玻璃，颗粒也越明显", AdvNumber(_cfg.GlassFrost, 0, 100, delegate(int v) { _cfg.GlassFrost = v; TouchSave(); RebuildForThemeChange(); })));
            motionBody.Children.Add(GeneralRow("玻璃透明度", "0 = 不透明，100 = 最透", AdvNumber(_cfg.GlassOpacity, 0, 100, delegate(int v) { _cfg.GlassOpacity = v; TouchSave(); RebuildForThemeChange(); })));
            motionBody.Children.Add(GeneralRow("显示光源", "关掉后只保留底色、磨砂和颗粒", AdvToggle(_cfg.GlassLightEnabled, delegate(bool v)
            {
                _cfg.GlassLightEnabled = v;
                TouchSave();
                RebuildForThemeChange();
            })));
            VigilNumberField lightBright = AdvNumber(_cfg.GlassLightBrightness, 0, 100, delegate(int v)
            {
                _cfg.GlassLightBrightness = v;
                TouchSave();
                RebuildForThemeChange();
            });
            lightBright.IsEnabled = _cfg.GlassLightEnabled;
            motionBody.Children.Add(GeneralRow("光源亮度", "0–100；高光 / 亮边强度", lightBright));
            VigilSelect lightSource = AdvSelect(new List<string> { "左上", "右上", "上方", "左侧", "右侧" },
                Math.Max(0, Math.Min(4, _cfg.GlassLightSource)), delegate(int v)
                {
                    _cfg.GlassLightSource = Math.Max(0, Math.Min(4, v));
                    TouchSave();
                    RebuildForThemeChange();
                });
            lightSource.IsEnabled = _cfg.GlassLightEnabled;
            motionBody.Children.Add(GeneralRow("光源来源", "高光和亮边从哪个方向来", lightSource));
            TextBlock tintNow = MonoText("当前：" + WpfCardGlass.TintLabel, 9, Sub);
            tintNow.Margin = new Thickness(0, 2, 0, 0);
            tintNow.ToolTip = "自动模式会读取背景图平均亮度：暗背景用偏白玻璃，亮背景用偏黑玻璃。";
            motionBody.Children.Add(GeneralRow("玻璃色调", "用户上传背景后自动在偏黑 / 偏白之间选择",
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        AppearanceSegment(new string[] { "自动", "偏黑", "偏白" }, _cfg.GlassTintMode, delegate(int v)
                        {
                            _cfg.GlassTintMode = Math.Max(0, Math.Min(2, v));
                            TouchSave();
                            RebuildForThemeChange();
                        }),
                        tintNow
                    }
                }));
            side.Children.Add(motion);
            motionBody.Children.Add(ResetButton("恢复动效默认", 2, false));

            StackPanel bgBody;
            VigilCard background = AppearanceCard("BACKDROP  /  背景", out bgBody);
            Grid bgRow = new Grid();
            bgRow.ColumnDefinitions.Add(new ColumnDefinition());
            bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock bgPath = new TextBlock();
            bgPath.Text = string.IsNullOrEmpty(_cfg.BackgroundImage) ? "未选择" : Path.GetFileName(_cfg.BackgroundImage);
            bgPath.FontSize = 11;
            bgPath.Foreground = Sub;
            bgPath.VerticalAlignment = VerticalAlignment.Center;
            bgPath.TextTrimming = TextTrimming.CharacterEllipsis;
            bgRow.Children.Add(bgPath);
            StackPanel bgBtns = new StackPanel();
            bgBtns.Orientation = Orientation.Horizontal;
            bgBtns.Children.Add(CommandButton("\uE8A5", "选择", delegate { PickBackground(); }, Accent));
            bgBtns.Children.Add(CommandButton("\uE790", "自动取色", delegate { AutoAccentFromBackground(); }, Sub));
            Grid.SetColumn(bgBtns, 1);
            bgRow.Children.Add(bgBtns);
            bgBody.Children.Add(GeneralRow("背景图", "铺在主界面后面的简笔画 / 图片", bgRow));
            VigilNumberField opacity = new VigilNumberField(_light, Line, Ink, Sub, 86);
            opacity.Minimum = 0;
            opacity.Maximum = 100;
            opacity.Value = _cfg.BackgroundOpacity;
            opacity.ValueChanged += delegate
            {
                _cfg.BackgroundOpacity = (int)opacity.Value;
                TouchSave();
            };
            bgBody.Children.Add(GeneralRow("不透明度", "0–100", opacity));
            background.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(side, 1);
            bgBody.Children.Add(GeneralRow("填充方式", "填充 / 适应 / 平铺",
                AppearanceSegment(new string[] { "填充", "适应", "平铺" }, _cfg.BackgroundFill, delegate(int idx)
                {
                    _cfg.BackgroundFill = idx;
                    TouchSave();
                    RebuildForThemeChange();
                })));
            bgBody.Children.Add(GeneralRow("暗化", "0–100；压住背景不抢文字",
                AdvNumber(_cfg.BackgroundDarken, 0, 100, delegate(int v) { _cfg.BackgroundDarken = v; TouchSave(); RebuildForThemeChange(); })));

            Grid musicRow = new Grid();
            musicRow.ColumnDefinitions.Add(new ColumnDefinition());
            musicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock musicPath = new TextBlock();
            musicPath.Text = string.IsNullOrEmpty(_cfg.BackgroundMusic) ? "未选择" : Path.GetFileName(_cfg.BackgroundMusic);
            musicPath.FontSize = 11;
            musicPath.Foreground = Sub;
            musicPath.VerticalAlignment = VerticalAlignment.Center;
            musicPath.TextTrimming = TextTrimming.CharacterEllipsis;
            musicRow.Children.Add(musicPath);
            StackPanel musicBtns = new StackPanel();
            musicBtns.Orientation = Orientation.Horizontal;
            musicBtns.Children.Add(CommandButton("\uE8A5", "选择", delegate { PickMusic(); }, Accent));
            musicBtns.Children.Add(CommandButton("\uE74D", "清除", delegate
            {
                _cfg.BackgroundMusic = "";
                TouchSave();
                RebuildForThemeChange();
            }, Sub));
            Grid.SetColumn(musicBtns, 1);
            musicRow.Children.Add(musicBtns);
            bgBody.Children.Add(GeneralRow("背景音乐", "图片 / 视频背景都能配", musicRow));
            bgBody.Children.Add(GeneralRow("循环播放", "关 = 软件启动只播一次",
                AdvToggle(_cfg.BackgroundMusicLoop, delegate(bool v) { _cfg.BackgroundMusicLoop = v; TouchSave(); })));
            bgBody.Children.Add(GeneralRow("音量", "0–100",
                AdvNumber(_cfg.BackgroundMusicVolume, 0, 100, delegate(int v) { _cfg.BackgroundMusicVolume = v; TouchSave(); RebuildForThemeChange(); })));
            bgBody.Children.Add(ResetButton("恢复背景默认", 3, false));
            row1.Children.Add(side);
            background.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(background, 2);
            row1.Children.Add(background);

            // ---- 第二行：提示外观（左） + 字号（右） ----
            Grid row2 = new Grid();
            row2.Margin = new Thickness(0, 2, 0, 0);
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel promptBody;
            VigilCard prompt = AppearanceCard("PROMPT  /  提示外观", out promptBody);
            prompt.Margin = new Thickness(0, 0, 10, 0);
            promptBody.Children.Add(GeneralRow("提示主题与背景",
                "与提示页共用同一份数据，不在这里重复保存",
                CommandButton("\uE8A5", "去提示页", delegate { SelectPage(2); }, Accent)));
            promptBody.Children.Add(GeneralRow("字体", "标题 / 设置名 / 说明",
                MonoText(_cfg.FontTitleSize + " / " + _cfg.FontBodySize + " / " + _cfg.FontDescSize + " px", 10, Sub)));

            StackPanel folderRow = new StackPanel();
            folderRow.Orientation = Orientation.Horizontal;
            folderRow.Children.Add(CommandButton("\uE8B5", "导入字体", delegate { ImportFonts(); }, Accent));
            folderRow.Children.Add(CommandButton("\uE838", "打开文件夹", delegate
            {
                try
                {
                    if (!Directory.Exists(FontLibrary.Dir)) Directory.CreateDirectory(FontLibrary.Dir);
                    System.Diagnostics.Process.Start("explorer.exe", FontLibrary.Dir);
                }
                catch { }
            }, Sub));
            promptBody.Children.Add(GeneralRow("字体文件夹", "放进 Fonts\\ 后，下面四类直接选", folderRow));

            promptBody.Children.Add(GeneralRow("提示字体", "屏幕提示；立即生效", FontCombo(_cfg.PromptFontFamily, 0)));
            promptBody.Children.Add(GeneralRow("正文字体", "设置名 / 说明；重启后生效", FontCombo(_cfg.UiFontName, 1)));
            promptBody.Children.Add(GeneralRow("标题字体", "页面标题 / 大数字；重启后生效", FontCombo(_cfg.UiTitleFontName, 2)));
            promptBody.Children.Add(GeneralRow("等宽字体", "数字 / 快捷键 / 日志；重启后生效", FontCombo(_cfg.UiMonoFontName, 3)));
            promptBody.Children.Add(ResetButton("恢复字体默认", 4, false));
            Grid.SetColumn(prompt, 0);
            row2.Children.Add(prompt);

            StackPanel fontBody;
            VigilCard font = AppearanceCard("TYPE  /  字号", out fontBody);
            Grid fontGrid = new Grid();
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.Children.Add(FontSizeField("标题", _cfg.FontTitleSize, delegate(int v) { _cfg.FontTitleSize = v; }, 0));
            fontGrid.Children.Add(FontSizeField("设置名", _cfg.FontBodySize, delegate(int v) { _cfg.FontBodySize = v; }, 1));
            fontGrid.Children.Add(FontSizeField("说明", _cfg.FontDescSize, delegate(int v) { _cfg.FontDescSize = v; }, 2));
            fontBody.Children.Add(fontGrid);
            StackPanel fontRow = new StackPanel();
            fontRow.Orientation = Orientation.Horizontal;
            string curFont = string.IsNullOrEmpty(_cfg.UiFontName) ? "默认（微软雅黑 UI）" : _cfg.UiFontName;
            fontRow.Children.Add(MonoText(curFont, 10, Sub));
            fontRow.Children.Add(CommandButton("\uE8A5", "上传字体", delegate { PickUiFont(0); }, Accent));
            fontRow.Children.Add(CommandButton("\uE711", "默认", delegate
            {
                _cfg.UiFontFile = "";
                _cfg.UiFontName = "";
                TouchSave();
                RebuildForThemeChange();
            }, Sub));
            fontBody.Children.Add(GeneralRow("界面缩放", "85–125%", AdvNumber(_cfg.UiScale, 85, 125, delegate(int v) { _cfg.UiScale = v; TouchSave(); RebuildForThemeChange(); })));
            fontBody.Children.Add(GeneralRow("密度", "紧凑会收紧页面留白",
                AppearanceSegment(new string[] { "紧凑", "舒适" }, _cfg.UiDensity, delegate(int v) { _cfg.UiDensity = v; TouchSave(); RebuildForThemeChange(); })));
            fontBody.Children.Add(GeneralRow("动态强调色", "按阵营微调主色（SCP 红 / 混沌绿）",
                AdvToggle(_cfg.DynamicAccent, delegate(bool v) { _cfg.DynamicAccent = v; TouchSave(); RebuildForThemeChange(); })));
            Grid.SetColumn(font, 1);
            row2.Children.Add(font);

            // ---- 两列排布：左（主题+提示字体） 右（动效+背景+字号） ----
            row1.Children.Clear();
            row2.Children.Clear();
            side.Children.Clear();
            Grid layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            StackPanel leftCol = new StackPanel();
            theme.Margin = new Thickness(0, 0, 10, 10);
            prompt.Margin = new Thickness(0, 0, 10, 0);
            leftCol.Children.Add(theme);
            leftCol.Children.Add(prompt);
            StackPanel rightCol = new StackPanel();
            motion.Margin = new Thickness(0);
            background.Margin = new Thickness(0, 0, 0, 10);
            font.Margin = new Thickness(0);
            rightCol.Children.Add(motion);
            rightCol.Children.Add(background);
            rightCol.Children.Add(font);
            Grid.SetColumn(leftCol, 0);
            Grid.SetColumn(rightCol, 1);
            layout.Children.Add(leftCol);
            layout.Children.Add(rightCol);
            page.Children.Add(layout);

            // ---- 底部：简笔画装饰条 ----
            VigilCard art = new VigilCard();
            art.Fill = Panel;
            art.Stroke = Line;
            art.Cut = 10;
            art.Padding = new Thickness(0);
            art.Margin = new Thickness(0, 10, 0, 0);
            art.Child = AppearanceArt();
            _hoverTargets.Add(art);
            page.Children.Add(art);
            return page;
        }

        private Button ResetButton(string label, int scope, bool confirm)
        {
            Button b = CommandButton("\uE777", label, delegate { ResetAppearance(scope, confirm); }, Sub);
            b.Margin = new Thickness(0, 8, 0, 0);
            b.HorizontalAlignment = HorizontalAlignment.Right;
            return b;
        }

        /// <summary>scope: 0=全部 1=主题 2=动效/玻璃 3=背景/音乐 4=字体。</summary>
        private void ResetAppearance(int scope, bool confirm)
        {
            if (confirm && MessageBox.Show("恢复外观默认设置？\n（不会删除 Fonts / Themes 里的文件）",
                    "外观", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            if (scope == 0 || scope == 1)
            {
                _cfg.UiThemeMode = -1;
                _cfg.AccentHex = ThemeTokens.DefaultAccent;
                _cfg.PageBgHex = _cfg.PanelHex = _cfg.PanelAltHex = _cfg.LineHex = "";
                _cfg.TextHex = _cfg.SubTextHex = _cfg.MutedHex = "";
                _cfg.WarnHex = _cfg.DangerHex = _cfg.SuccessHex = _cfg.InfoHex = "";
                _cfg.LightPageBgHex = _cfg.LightPanelHex = _cfg.LightPanelAltHex = _cfg.LightLineHex = "";
                _cfg.LightTextHex = _cfg.LightSubTextHex = _cfg.LightMutedHex = "";
                _cfg.LightAccentHex = _cfg.LightWarnHex = _cfg.LightDangerHex = "";
                _cfg.LightSuccessHex = _cfg.LightInfoHex = "";
            }
            if (scope == 0 || scope == 2)
            {
                _cfg.ClickFxEnabled = true;
                _cfg.ClickFxGap = 84;
                _cfg.GlassWindow = false;
                _cfg.GlassStrength = 55;
                _cfg.GlassCards = true;
                _cfg.GlassTintMode = 0;
                _cfg.GlassFrost = 55;
                _cfg.GlassOpacity = 25;
                _cfg.GlassLightEnabled = true;
                _cfg.GlassLightBrightness = 70;
                _cfg.GlassLightSource = 0;
                _cfg.PanelAnimQuality = 3;
                _cfg.UiAnimationsEnabled = true;
                _cfg.UiScale = 100;
                _cfg.UiDensity = 1;
                _cfg.DynamicAccent = false;
            }
            if (scope == 0 || scope == 3)
            {
                _cfg.BackgroundImage = "";
                _cfg.BackgroundOpacity = 12;
                _cfg.BackgroundFill = 0;
                _cfg.BackgroundDarken = 35;
                _cfg.BackgroundMusic = "";
                _cfg.BackgroundMusicEnabled = true;
                _cfg.BackgroundMusicLoop = true;
                _cfg.BackgroundMusicVolume = 45;
            }
            if (scope == 0 || scope == 4)
            {
                _cfg.UiFontFile = ""; _cfg.UiFontName = "";
                _cfg.UiTitleFontFile = ""; _cfg.UiTitleFontName = "";
                _cfg.UiMonoFontFile = ""; _cfg.UiMonoFontName = "";
                _cfg.PromptFontFile = ""; _cfg.PromptFontFamily = "";
                _cfg.FontTitleSize = 32;
                _cfg.FontBodySize = 14;
                _cfg.FontDescSize = 12;
            }

            ThemeTokens.Configure(_cfg);
            try { UiFonts.Apply(_cfg); } catch { }
            try { OverlayForm.SetPromptFontFile(""); } catch { }
            TouchSave();
            MessageBox.Show("已恢复默认。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
            RebuildForThemeChange();
        }

        private VigilSelect FontCombo(string current, int kind)
        {
            VigilSelect cb = new VigilSelect(PanelAlt, Line, Ink, Accent, 190);
            cb.Items.Add("默认（系统字体）");
            List<FontLibrary.Entry> list = FontLibrary.Entries();
            int sel = 0;
            for (int i = 0; i < list.Count; i++)
            {
                cb.Items.Add(list[i].Name);
                if (string.Equals(list[i].Name, current, StringComparison.OrdinalIgnoreCase)) sel = i + 1;
            }
            cb.SelectedIndex = sel;
            cb.SelectionChanged += delegate
            {
                if (cb.SelectedIndex < 0) return;
                string name = cb.SelectedIndex == 0 ? "" : cb.Items[cb.SelectedIndex];
                string file = FontLibrary.FileFor(name);
                if (kind == 0)
                {
                    _cfg.PromptFontFamily = name;
                    _cfg.PromptFontFile = file;
                    OverlayForm.SetPromptFontFile(file);
                    TouchSave();
                    return;
                }
                if (kind == 1) { _cfg.UiFontName = name; _cfg.UiFontFile = file; }
                else if (kind == 2) { _cfg.UiTitleFontName = name; _cfg.UiTitleFontFile = file; }
                else { _cfg.UiMonoFontName = name; _cfg.UiMonoFontFile = file; }
                TouchSave();
                MessageBox.Show("字体已选择，重启软件后生效。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            return cb;
        }

        private void ImportFonts()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "字体|*.ttf;*.otf";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                int n = FontLibrary.Import(dlg.FileNames);
                MessageBox.Show("已导入 " + n + " 个字体文件到 Fonts\\。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                RebuildForThemeChange();
            }
        }

        private VigilCard AppearanceCard(string tag, out StackPanel body)
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(20, 15, 20, 17);
            body = new StackPanel();
            body.Children.Add(MonoText(tag, 9, Accent));
            card.Child = body;
            _hoverTargets.Add(card);
            return card;
        }

        /// <summary>简笔画装饰条：网格 + 六边形 / 菱形线稿 + 少量取景标记，不参与交互。</summary>
        private UIElement AppearanceArt()
        {
            Grid host = new Grid();
            host.Height = 200;
            host.Children.Add(new ShareCardCanvas(Accent, Line, Sub, PanelAlt, Ink, _cfg));
            host.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    try { ShowCardDesign(); }
                    catch (Exception ex) { MessageBox.Show("打开名片编辑器失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning); }
                }
            };

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Top;
            actions.Margin = new Thickness(0, 14, 16, 0);
            actions.Children.Add(CommandButton("\uE70F", "编辑名片", delegate { ShowCardDesign(); }, Accent));
            actions.Children.Add(CommandButton("\uE74E", "导出 PNG", delegate { SaveCardPng(); }, Accent));
            actions.Children.Add(CommandButton("\uE896", "导出主题", delegate { ExportThemePack(); }, Sub));
            actions.Children.Add(CommandButton("\uE8B5", "导入主题", delegate { ImportThemePack(); }, Sub));
            host.Children.Add(actions);
            return host;
        }

        private void ShowCardDesign()
        {
            _cardDesignOpen = true;
            BuildCardDesign();
            SelectPage(AppearancePageIndex);
        }

        private void BuildCardDesign()
        {
            if (_cardDesignBody == null) return;
            PurgeCardDesignHoverTargets();
            _cardDesignBody.Children.Clear();
            _cardDesignBuilding = true;

            StackPanel head = new StackPanel();
            head.Orientation = Orientation.Horizontal;
            head.Margin = new Thickness(0, 0, 0, 14);
            head.Children.Add(CommandButton("\uE72B", "返回外观", delegate { CloseCardDesign(); }, Sub));
            TextBlock title = DisplayText("名片设计", 26, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(14, 0, 0, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            _cardDesignBody.Children.Add(head);
            _cardDesignBody.Children.Add(MonoText("CARD  /  分享名片 · 所有改动即时预览，保存后主界面同步", 9, Accent));

            ShareCardCanvas preview = new ShareCardCanvas(Accent, Line, Sub, PanelAlt, Ink, _cfg);
            preview.Width = 1306;
            preview.Height = 196;
            Viewbox previewScale = new Viewbox();
            previewScale.Stretch = Stretch.Uniform;
            previewScale.Height = 205;
            previewScale.Margin = new Thickness(0, 8, 0, 12);
            previewScale.Child = preview;
            Border previewBox = new Border();
            previewBox.BorderBrush = Line;
            previewBox.BorderThickness = new Thickness(1);
            previewBox.CornerRadius = new CornerRadius(6);
            previewBox.ClipToBounds = true;
            previewBox.Child = previewScale;
            _cardDesignBody.Children.Add(previewBox);
            Action refresh = delegate { try { preview.InvalidateVisual(); } catch { } };

            // ---- 预设 ----
            StackPanel presetBody;
            VigilCard presetCard = AppearanceSection("PRESET  /  预设", out presetBody);
            WrapPanel presets = new WrapPanel();
            presets.Margin = new Thickness(0, 0, 0, 4);
            for (int i = 0; i < ShareCardCanvas.PresetNames.Length; i++)
            {
                int idx = i;
                bool on = _cfg.CardPreset == i;
                Button b = CommandButton("\uE790", ShareCardCanvas.PresetNames[i], delegate
                {
                    _cfg.CardPreset = idx;
                    TouchSave();
                    BuildCardDesign();
                }, on ? Accent : Sub);
                b.Margin = new Thickness(0, 0, 8, 8);
                presets.Children.Add(b);
            }
            presetBody.Children.Add(presets);
            _cardDesignBody.Children.Add(presetCard);

            // ---- 内容 ----
            StackPanel contentBody;
            VigilCard contentCard = AppearanceSection("CONTENT  /  内容", out contentBody);
            contentBody.Children.Add(GeneralRow("标题", "大号字；不同预设位置会自动适配",
                CardField(_cfg.CardTitle, delegate(string v) { _cfg.CardTitle = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("副标题", "标题下面一行",
                CardField(_cfg.CardSubtitle, delegate(string v) { _cfg.CardSubtitle = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("状态标签", "ONLINE / 在线、KETER、LEVEL 等",
                CardField(_cfg.CardTag, delegate(string v) { _cfg.CardTag = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("玩家名", "终端 / 干员证 / 信号脉冲使用",
                CardField(_cfg.CardPlayer, delegate(string v) { _cfg.CardPlayer = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("玩家 ID", "玩家名下面的一行小字",
                CardField(_cfg.CardPlayerId, delegate(string v) { _cfg.CardPlayerId = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("数据 1", "左：标签，右：数值",
                CardStatRow(_cfg.CardStat1Label, _cfg.CardStat1Value,
                    delegate(string v) { _cfg.CardStat1Label = v; }, delegate(string v) { _cfg.CardStat1Value = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("数据 2", "第二组数据",
                CardStatRow(_cfg.CardStat2Label, _cfg.CardStat2Value,
                    delegate(string v) { _cfg.CardStat2Label = v; }, delegate(string v) { _cfg.CardStat2Value = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("数据 3", "第三组数据",
                CardStatRow(_cfg.CardStat3Label, _cfg.CardStat3Value,
                    delegate(string v) { _cfg.CardStat3Label = v; }, delegate(string v) { _cfg.CardStat3Value = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("小字 1", "左下角第一行；留空隐藏",
                CardField(_cfg.CardLine1, delegate(string v) { _cfg.CardLine1 = v; }, refresh)));
            contentBody.Children.Add(GeneralRow("小字 2", "左下角第二行；留空隐藏",
                CardField(_cfg.CardLine2, delegate(string v) { _cfg.CardLine2 = v; }, refresh)));
            _cardDesignBody.Children.Add(contentCard);

            // ---- 样式 ----
            StackPanel styleBody;
            VigilCard styleCard = AppearanceSection("STYLE  /  样式", out styleBody);
            styleBody.Children.Add(GeneralRow("主色", "强调线 / 数字 / 边框；留空跟随主题",
                CardAccentPicker(refresh)));
            styleBody.Children.Add(GeneralRow("文字缩放", "75–145%；只影响名片文字",
                AdvNumber(_cfg.CardTextScale, 75, 145, delegate(int v)
                {
                    _cfg.CardTextScale = v;
                    TouchSave();
                    refresh();
                })));
            styleBody.Children.Add(GeneralRow("显示网格", "终端 / 收容 / 战术预设的背景网格",
                CardToggle(_cfg.CardShowGrid, delegate(bool v) { _cfg.CardShowGrid = v; }, refresh)));
            styleBody.Children.Add(GeneralRow("显示六边形", "装饰线稿；关闭后更干净",
                CardToggle(_cfg.CardShowHex, delegate(bool v) { _cfg.CardShowHex = v; }, refresh)));
            styleBody.Children.Add(GeneralRow("显示边框", "外框 / 分隔线 / 条形码等装饰",
                CardToggle(_cfg.CardShowFrame, delegate(bool v) { _cfg.CardShowFrame = v; }, refresh)));
            styleBody.Children.Add(GeneralRow("显示数据", "三组数据；关掉后只保留标题和玩家",
                CardToggle(_cfg.CardShowStats, delegate(bool v) { _cfg.CardShowStats = v; }, refresh)));
            styleBody.Children.Add(GeneralRow("显示玩家", "玩家名 / ID / 胶囊标签",
                CardToggle(_cfg.CardShowPlayer, delegate(bool v) { _cfg.CardShowPlayer = v; }, refresh)));
            styleBody.Children.Add(GeneralRow("显示实时数据", "会话时长 / 对局数 / LIVE 状态",
                CardToggle(_cfg.CardShowLive, delegate(bool v) { _cfg.CardShowLive = v; }, refresh)));
            _cardDesignBody.Children.Add(styleCard);

            _cardDesignBuilding = false;

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 14, 0, 0);
            actions.Children.Add(CommandButton("\uE74E", "导出 PNG", delegate { SaveCardPng(); }, Sub));
            actions.Children.Add(CommandButton("\uE777", "恢复默认", delegate
            {
                ResetCardDefaults();
                BuildCardDesign();
            }, Sub));
            actions.Children.Add(CommandButton("\uE73E", "保存并返回", delegate { CloseCardDesign(); }, Accent));
            _cardDesignBody.Children.Add(actions);
        }

        /// <summary>名片编辑页反复重建时，把旧卡片从悬停描边列表里摘掉，避免越点越卡。</summary>
        private void PurgeCardDesignHoverTargets()
        {
            if (_cardDesignBody == null) return;
            for (int i = _hoverTargets.Count - 1; i >= 0; i--)
            {
                DependencyObject d = _hoverTargets[i];
                while (d != null && d != _cardDesignBody)
                    d = VisualTreeHelper.GetParent(d);
                if (d == _cardDesignBody) _hoverTargets.RemoveAt(i);
            }
        }

        private TextBox CardField(string value, Action<string> set, Action refresh)
        {
            TextBox box = VigilInput(value ?? "", 230);
            box.TextChanged += delegate
            {
                if (_cardDesignBuilding) return;
                set(box.Text ?? "");
                TouchSave();
                refresh();
            };
            return box;
        }

        private FrameworkElement CardStatRow(string label, string value, Action<string> setLabel, Action<string> setValue, Action refresh)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            TextBox labelBox = CardField(label, setLabel, refresh);
            labelBox.Width = 104;
            labelBox.Margin = new Thickness(0, 0, 8, 0);
            TextBox valueBox = CardField(value, setValue, refresh);
            valueBox.Width = 118;
            row.Children.Add(labelBox);
            row.Children.Add(valueBox);
            return row;
        }

        private VigilToggle CardToggle(bool value, Action<bool> set, Action refresh)
        {
            VigilToggle toggle = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            toggle.IsChecked = value;
            toggle.CheckedChanged += delegate
            {
                if (_cardDesignBuilding) return;
                set(toggle.IsChecked);
                TouchSave();
                refresh();
            };
            return toggle;
        }

        private FrameworkElement CardAccentPicker(Action refresh)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            string[] hexes = { "#38D6FF", "#FF5C5C", "#E8A33D", "#5BE38C", "#F4F8FF" };
            for (int i = 0; i < hexes.Length; i++)
            {
                string hex = hexes[i];
                Border swatch = new Border();
                swatch.Width = 22;
                swatch.Height = 22;
                swatch.CornerRadius = new CornerRadius(3);
                swatch.Margin = new Thickness(0, 0, 8, 0);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = Line;
                swatch.Background = Solid(ThemeTokens.ParseHex(hex, ThemeTokens.Get(_light).Accent));
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = hex;
                swatch.MouseLeftButtonUp += delegate
                {
                    _cfg.CardAccentHex = hex;
                    TouchSave();
                    refresh();
                };
                row.Children.Add(swatch);
            }
            Button custom = CommandButton("\uE790", "自定义…", delegate
            {
                using (System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog())
                {
                    dlg.FullOpen = true;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _cfg.CardAccentHex = ThemeTokens.ToHex(System.Drawing.Color.FromArgb(
                            dlg.Color.R, dlg.Color.G, dlg.Color.B));
                        TouchSave();
                        refresh();
                    }
                }
            }, Sub);
            custom.Margin = new Thickness(2, 0, 4, 0);
            row.Children.Add(custom);
            row.Children.Add(CommandButton("\uE711", "跟随主题", delegate
            {
                _cfg.CardAccentHex = "";
                TouchSave();
                refresh();
            }, Sub));
            return row;
        }

        private void ResetCardDefaults()
        {
            _cfg.CardPreset = 0;
            _cfg.CardTitle = "A.R.C.T";
            _cfg.CardSubtitle = "SCPSL ALERT";
            _cfg.CardTag = "ONLINE / 在线";
            _cfg.CardPlayer = "Operator_7";
            _cfg.CardPlayerId = "ID 00000000000000000";
            _cfg.CardStat1Label = "总场次";
            _cfg.CardStat1Value = "1,284";
            _cfg.CardStat2Label = "存活率";
            _cfg.CardStat2Value = "63%";
            _cfg.CardStat3Label = "击杀";
            _cfg.CardStat3Value = "412";
            _cfg.CardAccentHex = "";
            _cfg.CardTextScale = 100;
            _cfg.CardShowGrid = true;
            _cfg.CardShowHex = true;
            _cfg.CardShowFrame = true;
            _cfg.CardShowStats = true;
            _cfg.CardShowPlayer = true;
            _cfg.CardShowLive = true;
            _cfg.CardLine1 = "";
            _cfg.CardLine2 = "";
            TouchSave();
        }

        /// <summary>保存名片并重建主界面，避免“点了保存但主界面那张没换”。</summary>
        private void CloseCardDesign()
        {
            TouchSave();
            _cardDesignOpen = false;
            RebuildForThemeChange();
        }

        /// <summary>保留旧入口名；双击名片和新版“编辑名片”都走同一套完整编辑器。</summary>
        private void ShowCardEditor()
        {
            ShowCardDesign();
        }

        private void SaveCardPng()
        {
            using (System.Windows.Forms.SaveFileDialog dlg = new System.Windows.Forms.SaveFileDialog())
            {
                dlg.Filter = "PNG 图片|*.png";
                dlg.FileName = "A.R.C.T-card.png";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    ShareCardCanvas card = new ShareCardCanvas(Accent, Line, Sub, PanelAlt, Ink, _cfg);
                    card.SavePng(dlg.FileName, 1600, 240);
                    MessageBox.Show("名片 PNG 已导出。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private FrameworkElement AppearanceSegment(string[] names, int selected, Action<int> pick)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                bool on = i == selected;
                Border item = new Border();
                item.CornerRadius = new CornerRadius(4);
                item.BorderThickness = new Thickness(1);
                item.BorderBrush = on ? Accent : Line;
                item.Background = on ? WpfCardGlass.SurfaceBrush(AccentSoftBrush()) : Brushes.Transparent;
                item.Height = 30;
                item.Padding = new Thickness(12, 0, 12, 0);
                item.Margin = new Thickness(0, 0, 6, 0);
                item.Cursor = Cursors.Hand;
                TextBlock label = new TextBlock();
                label.Text = names[i];
                label.FontFamily = Ui;
                label.FontSize = 11.5;
                label.Foreground = on ? Accent : Sub;
                label.VerticalAlignment = VerticalAlignment.Center;
                item.Child = label;
                item.MouseEnter += delegate { if (!on) { item.BorderBrush = Accent; label.Foreground = Ink; } };
                item.MouseLeave += delegate { if (!on) { item.BorderBrush = Line; label.Foreground = Sub; } };
                item.MouseLeftButtonUp += delegate { if (pick != null) pick(index); };
                row.Children.Add(item);
            }
            return row;
        }

        private UIElement AppearancePreview()
        {
            Border box = new Border();
            box.Background = PanelAlt;
            box.BorderBrush = Line;
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(6);
            box.Padding = new Thickness(16, 14, 16, 14);
            box.Margin = new Thickness(0, 10, 0, 0);
            box.IsHitTestVisible = false;

            StackPanel stack = new StackPanel();
            TextBlock big = DisplayText("样式预览", 24, Ink);
            big.FontWeight = FontWeights.Bold;
            big.Margin = new Thickness(0, 6, 0, 2);
            stack.Children.Add(big);
            stack.Children.Add(MonoText("PREVIEW  /  外观示意，不可点击", 9, Muted));
            TextBlock sample = MonoText("示例文字    ·    示例数字", 9, Sub);
            sample.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(sample);

            StackPanel line = new StackPanel();
            line.Orientation = Orientation.Horizontal;
            line.Margin = new Thickness(0, 12, 0, 0);
            Border primary = new Border();
            primary.Background = Accent;
            primary.CornerRadius = new CornerRadius(4);
            primary.Padding = new Thickness(14, 7, 14, 7);
            TextBlock ptext = new TextBlock();
            ptext.Text = "主按钮";
            ptext.FontSize = 11;
            ptext.FontWeight = FontWeights.Bold;
            ptext.Foreground = OnAccentText();
            primary.Child = ptext;
            line.Children.Add(primary);

            Border selected = new Border();
            selected.Background = AccentSoftBrush();
            selected.BorderBrush = Accent;
            selected.BorderThickness = new Thickness(1);
            selected.CornerRadius = new CornerRadius(4);
            selected.Padding = new Thickness(12, 7, 12, 7);
            selected.Margin = new Thickness(10, 0, 0, 0);
            TextBlock stext = new TextBlock();
            stext.Text = "选中项";
            stext.FontSize = 11;
            stext.Foreground = Accent;
            selected.Child = stext;
            line.Children.Add(selected);

            VigilToggle toggle = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            toggle.IsChecked = true;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(14, 0, 0, 0);
            line.Children.Add(toggle);
            stack.Children.Add(line);
            box.Child = stack;
            return box;
        }

        private Brush OnAccentText()
        {
            System.Drawing.Color accent = ThemeTokens.For(_light);
            double darkContrast = ThemeTokens.Contrast(accent, System.Drawing.Color.FromArgb(6, 16, 22));
            double lightContrast = ThemeTokens.Contrast(accent, System.Drawing.Color.White);
            return darkContrast >= lightContrast ? Solid(6, 16, 22) : Brushes.White;
        }

        private void ApplyAccentHex(string hex)
        {
            string v = ThemeTokens.NormalizeHex(hex, ThemeTokens.DefaultAccent);
            if (_light) _cfg.LightAccentHex = v;
            else _cfg.AccentHex = v;
            ThemeTokens.Configure(_cfg);
            TouchSave();
        }

        private void RebuildForThemeChange()
        {
            if (_appearanceRebuildQueued) return;
            _appearanceRebuildQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _appearanceRebuildQueued = false;
                int page = _currentPage;
                _light = _cfg.UiThemeMode < 0 ? !ModernTheme.IsDark : _cfg.UiThemeMode == 0;
                BuildPalette();
                Build();
                CollectTags(this);
                ApplySeriesVisibility();
                UpdateTemperatureVisibility();
                UpdateTelemetry();
                UpdatePerformance();
                SelectPage(page >= 0 && page <= AppearancePageIndex ? page : AppearancePageIndex);
                if (_appearanceDetailOpen) ShowAppearanceDetail();
            }), DispatcherPriority.Background);
        }

        private void ShowAppearanceDetail()
        {
            _appearanceDetailOpen = true;
            SelectPage(AppearancePageIndex);
            if (_appearancePage == null || _appearanceDetailPage == null || _appearanceDetailBody == null) return;
            _appearancePage.Visibility = Visibility.Collapsed;
            _appearanceDetailBody.Children.Clear();

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(0, 0, 0, 14);
            head.Children.Add(CommandButton("\uE72B", "返回外观",
                delegate { _appearanceDetailOpen = false; SelectPage(AppearancePageIndex); }, Sub));
            TextBlock title = DisplayText("主题设置", TitleSize, Ink);
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 8, 0, 0);
            head.Children.Add(title);
            _appearanceDetailBody.Children.Add(head);

            StackPanel paletteBody;
            VigilCard palette = AppearanceSection("色板", out paletteBody);
            StackPanel modeRow = new StackPanel();
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Margin = new Thickness(0, 0, 0, 10);
            modeRow.Children.Add(AppearanceSegment(new string[] { "深色", "浅色" },
                _editLightPalette ? 1 : 0, delegate(int i)
                {
                    _editLightPalette = i == 1;
                    ShowAppearanceDetail();
                }));
            paletteBody.Children.Add(modeRow);

            string[] names = { "背景", "面板", "浮层", "边框", "主文字", "次文字", "弱文字", "强调", "警告", "危险", "成功", "信息" };
            UniformGrid colors = new UniformGrid();
            colors.Columns = 2;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                colors.Children.Add(ColorRow(names[i], GetThemeColor(index),
                    delegate(string hex) { SetThemeColor(index, hex); }));
            }
            paletteBody.Children.Add(colors);
            _appearanceDetailBody.Children.Add(palette);

            StackPanel bgBody;
            VigilCard background = AppearanceSection("背景", out bgBody);
            Grid bgRow = new Grid();
            bgRow.ColumnDefinitions.Add(new ColumnDefinition());
            bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock bgPath = new TextBlock();
            bgPath.Text = string.IsNullOrEmpty(_cfg.BackgroundImage) ? "未选择" : Path.GetFileName(_cfg.BackgroundImage);
            bgPath.FontSize = 12;
            bgPath.Foreground = Sub;
            bgPath.VerticalAlignment = VerticalAlignment.Center;
            bgPath.TextTrimming = TextTrimming.CharacterEllipsis;
            bgRow.Children.Add(bgPath);
            StackPanel bgButtons = new StackPanel();
            bgButtons.Orientation = Orientation.Horizontal;
            bgButtons.Children.Add(CommandButton("\uE8A5", "选择", delegate { PickBackground(); }, Accent));
            bgButtons.Children.Add(CommandButton("\uE74D", "清除", delegate
            {
                _cfg.BackgroundImage = "";
                TouchSave();
                RebuildForThemeChange();
            }, Sub));
            Grid.SetColumn(bgButtons, 1);
            bgRow.Children.Add(bgButtons);
            bgBody.Children.Add(AppearanceLabel("背景图"));
            bgBody.Children.Add(bgRow);

            VigilNumberField opacity = new VigilNumberField(_light, Line, Ink, Sub, 86);
            opacity.Minimum = 0;
            opacity.Maximum = 100;
            opacity.Value = _cfg.BackgroundOpacity;
            opacity.ValueChanged += delegate
            {
                _cfg.BackgroundOpacity = (int)opacity.Value;
                TouchSave();
            };
            bgBody.Children.Add(AppearanceLabel("不透明度"));
            bgBody.Children.Add(opacity);
            _appearanceDetailBody.Children.Add(background);

            StackPanel fontBody;
            VigilCard font = AppearanceSection("字号", out fontBody);
            Grid fontGrid = new Grid();
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fontGrid.Children.Add(FontSizeField("标题", _cfg.FontTitleSize, delegate(int v) { _cfg.FontTitleSize = v; }, 0));
            fontGrid.Children.Add(FontSizeField("设置名", _cfg.FontBodySize, delegate(int v) { _cfg.FontBodySize = v; }, 1));
            fontGrid.Children.Add(FontSizeField("说明", _cfg.FontDescSize, delegate(int v) { _cfg.FontDescSize = v; }, 2));
            fontBody.Children.Add(fontGrid);
            StackPanel fontRow = new StackPanel();
            fontRow.Orientation = Orientation.Horizontal;
            string curFont = string.IsNullOrEmpty(_cfg.UiFontName) ? "默认（微软雅黑 UI）" : _cfg.UiFontName;
            fontRow.Children.Add(MonoText(curFont, 10, Sub));
            fontRow.Children.Add(CommandButton("\uE8A5", "上传字体", delegate { PickUiFont(0); }, Accent));
            fontRow.Children.Add(CommandButton("\uE711", "默认", delegate
            {
                _cfg.UiFontFile = "";
                _cfg.UiFontName = "";
                TouchSave();
                RebuildForThemeChange();
            }, Sub));
            fontBody.Children.Add(GeneralRow("正文字体", "设置名 / 说明；重启后生效", fontRow));
            fontBody.Children.Add(FontPickRow("标题字体", _cfg.UiTitleFontName, 1));
            fontBody.Children.Add(FontPickRow("等宽字体", _cfg.UiMonoFontName, 2));
            fontBody.Children.Add(GeneralRow("界面缩放", "85–125%", AdvNumber(_cfg.UiScale, 85, 125, delegate(int v) { _cfg.UiScale = v; TouchSave(); RebuildForThemeChange(); })));
            fontBody.Children.Add(GeneralRow("密度", "紧凑会收紧页面留白",
                AppearanceSegment(new string[] { "紧凑", "舒适" }, _cfg.UiDensity, delegate(int v) { _cfg.UiDensity = v; TouchSave(); RebuildForThemeChange(); })));
            fontBody.Children.Add(GeneralRow("动态强调色", "按阵营微调主色（SCP 红 / 混沌绿）",
                AdvToggle(_cfg.DynamicAccent, delegate(bool v) { _cfg.DynamicAccent = v; TouchSave(); RebuildForThemeChange(); })));
            _appearanceDetailBody.Children.Add(font);

            StackPanel motionBody;
            VigilCard motion = AppearanceSection("动效", out motionBody);
            int motionIndex = _cfg.PanelAnimQuality <= 0 ? 0 : (_cfg.PanelAnimQuality <= 2 ? 1 : 2);
            motionBody.Children.Add(AppearanceSegment(new string[] { "关", "低", "高" }, motionIndex,
                delegate(int i)
                {
                    _cfg.UiAnimationsEnabled = i > 0;
                    _cfg.PanelAnimQuality = i == 0 ? 0 : (i == 1 ? 2 : 4);
                    UiAnim.Quality = _cfg.PanelAnimQuality;
                    TouchSave();
                }));
            VigilToggle clickFx = new VigilToggle(_light, Accent, TrackOffBrush(), KnobBrush());
            clickFx.IsChecked = _cfg.ClickFxEnabled;
            clickFx.CheckedChanged += delegate { _cfg.ClickFxEnabled = clickFx.IsChecked; TouchSave(); };
            motionBody.Children.Add(GeneralRow("点击特效", "点击时四条线汇聚；双击旋转 45°", clickFx));
            _appearanceDetailBody.Children.Add(motion);

            Button apply = CommandButton("\uE73E", "应用", delegate { RebuildForThemeChange(); }, Accent);
            apply.HorizontalAlignment = HorizontalAlignment.Right;
            apply.Margin = new Thickness(0, 4, 0, 0);
            _appearanceDetailBody.Children.Add(apply);

            StackPanel packRow = new StackPanel();
            packRow.Orientation = Orientation.Horizontal;
            packRow.HorizontalAlignment = HorizontalAlignment.Right;
            packRow.Margin = new Thickness(0, 8, 0, 0);
            packRow.Children.Add(CommandButton("\uE896", "导出主题", delegate { ExportThemePack(); }, Sub));
            packRow.Children.Add(CommandButton("\uE8B5", "导入主题", delegate { ImportThemePack(); }, Sub));
            _appearanceDetailBody.Children.Add(packRow);

            _appearanceDetailPage.Visibility = Visibility.Visible;
            _currentPage = AppearancePageIndex;
            UpdateRailSelection(AppearancePageIndex);
        }

        private VigilCard AppearanceSection(string title, out StackPanel body)
        {
            VigilCard card = new VigilCard();
            card.Fill = Panel;
            card.Stroke = Line;
            card.Cut = 10;
            card.Padding = new Thickness(20, 16, 20, 18);
            card.Margin = new Thickness(0, 0, 0, 12);
            body = new StackPanel();
            TextBlock t = DisplayText(title, 18, Ink);
            t.FontWeight = FontWeights.Bold;
            t.Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(t);
            card.Child = body;
            _hoverTargets.Add(card);
            return card;
        }

        private TextBlock AppearanceLabel(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = Math.Max(8, Math.Min(96, _cfg.FontDescSize));
            t.Foreground = Sub;
            t.Margin = new Thickness(0, 8, 0, 4);
            return t;
        }

        private UIElement ColorRow(string name, string hex, Action<string> set)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 4, 16, 4);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border swatch = new Border();
            swatch.Width = 16;
            swatch.Height = 16;
            swatch.CornerRadius = new CornerRadius(3);
            swatch.BorderThickness = new Thickness(1);
            swatch.BorderBrush = Line;
            swatch.Background = Solid(ThemeTokens.ParseHex(hex, ThemeTokens.Get(_light).Accent));
            swatch.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(swatch);
            TextBlock label = new TextBlock();
            label.Text = name;
            label.FontSize = 12;
            label.Foreground = Ink;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            TextBlock value = new TextBlock();
            value.Text = hex;
            value.FontSize = 11;
            value.FontFamily = Mono;
            value.Foreground = Sub;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            Button pick = CommandButton("\uE790", "拾色", delegate
            {
                using (System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.Color = ThemeTokens.ParseHex(hex, ThemeTokens.Get(_light).Accent);
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        set(ThemeTokens.ToHex(System.Drawing.Color.FromArgb(
                            dlg.Color.R, dlg.Color.G, dlg.Color.B)));
                    }
                }
            }, Sub);
            pick.Margin = new Thickness(0);
            Grid.SetColumn(pick, 3);
            row.Children.Add(pick);
            return row;
        }

        private string GetThemeColor(int index)
        {
            string[] dark = { _cfg.PageBgHex, _cfg.PanelHex, _cfg.PanelAltHex, _cfg.LineHex,
                _cfg.TextHex, _cfg.SubTextHex, _cfg.MutedHex, _cfg.AccentHex,
                _cfg.WarnHex, _cfg.DangerHex, _cfg.SuccessHex, _cfg.InfoHex };
            string[] light = { _cfg.LightPageBgHex, _cfg.LightPanelHex, _cfg.LightPanelAltHex, _cfg.LightLineHex,
                _cfg.LightTextHex, _cfg.LightSubTextHex, _cfg.LightMutedHex, _cfg.LightAccentHex,
                _cfg.LightWarnHex, _cfg.LightDangerHex, _cfg.LightSuccessHex, _cfg.LightInfoHex };
            string[] a = _editLightPalette ? light : dark;
            return index >= 0 && index < a.Length ? a[index] : ThemeTokens.DefaultAccent;
        }

        private void SetThemeColor(int index, string hex)
        {
            string v = ThemeTokens.NormalizeHex(hex, ThemeTokens.DefaultAccent);
            if (_editLightPalette)
            {
                switch (index)
                {
                    case 0: _cfg.LightPageBgHex = v; break;
                    case 1: _cfg.LightPanelHex = v; break;
                    case 2: _cfg.LightPanelAltHex = v; break;
                    case 3: _cfg.LightLineHex = v; break;
                    case 4: _cfg.LightTextHex = v; break;
                    case 5: _cfg.LightSubTextHex = v; break;
                    case 6: _cfg.LightMutedHex = v; break;
                    case 7: _cfg.LightAccentHex = v; break;
                    case 8: _cfg.LightWarnHex = v; break;
                    case 9: _cfg.LightDangerHex = v; break;
                    case 10: _cfg.LightSuccessHex = v; break;
                    case 11: _cfg.LightInfoHex = v; break;
                }
            }
            else
            {
                switch (index)
                {
                    case 0: _cfg.PageBgHex = v; break;
                    case 1: _cfg.PanelHex = v; break;
                    case 2: _cfg.PanelAltHex = v; break;
                    case 3: _cfg.LineHex = v; break;
                    case 4: _cfg.TextHex = v; break;
                    case 5: _cfg.SubTextHex = v; break;
                    case 6: _cfg.MutedHex = v; break;
                    case 7: _cfg.AccentHex = v; break;
                    case 8: _cfg.WarnHex = v; break;
                    case 9: _cfg.DangerHex = v; break;
                    case 10: _cfg.SuccessHex = v; break;
                    case 11: _cfg.InfoHex = v; break;
                }
            }
            ThemeTokens.Configure(_cfg);
            TouchSave();
        }

        private void PickBackground()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "图片或视频|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.webm;*.wmv;*.avi;*.mov;*.mkv|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|视频|*.mp4;*.webm;*.wmv;*.avi;*.mov;*.mkv";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _cfg.BackgroundImage = dlg.FileName;
                TouchSave();
                RebuildForThemeChange();
            }
        }



        private void PickPromptFont()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "字体|*.ttf;*.otf";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string ext = Path.GetExtension(dlg.FileName);
                    string dest = Path.Combine(dir, "prompt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ext);
                    File.Copy(dlg.FileName, dest, true);
                    string family = "";
                    foreach (FontFamily f in System.Windows.Media.Fonts.GetFontFamilies(new Uri(dest)))
                    {
                        family = f.Source;
                        break;
                    }
                    _cfg.PromptFontFile = dest;
                    _cfg.PromptFontFamily = string.IsNullOrEmpty(family) ? Path.GetFileNameWithoutExtension(dest) : family;
                    OverlayForm.SetPromptFontFile(dest);
                    TouchSave();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("提示字体加载失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        private UIElement FontPickRow(string label, string current, int kind)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Children.Add(MonoText(string.IsNullOrEmpty(current) ? "默认" : current, 10, Sub));
            row.Children.Add(CommandButton("\uE8A5", "上传", delegate { PickUiFont(kind); }, Accent));
            row.Children.Add(CommandButton("\uE711", "默认", delegate
            {
                if (kind == 1) { _cfg.UiTitleFontFile = ""; _cfg.UiTitleFontName = ""; }
                else if (kind == 2) { _cfg.UiMonoFontFile = ""; _cfg.UiMonoFontName = ""; }
                TouchSave();
                MessageBox.Show("已恢复默认，重启软件后生效。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
            }, Sub));
            return GeneralRow(label, "只影响这一类文字；重启后生效", row);
        }

        private void PickUiFont(int kind)
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "字体|*.ttf;*.otf";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string ext = Path.GetExtension(dlg.FileName);
                    string prefix = kind == 1 ? "title-" : (kind == 2 ? "mono-" : "body-");
                    string dest = Path.Combine(dir, prefix + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ext);
                    File.Copy(dlg.FileName, dest, true);
                    string family = "";
                    foreach (FontFamily f in System.Windows.Media.Fonts.GetFontFamilies(new Uri(dest)))
                    {
                        family = f.Source;
                        break;
                    }
                    if (string.IsNullOrEmpty(family))
                    {
                        MessageBox.Show("这个字体读不出字体族名。", "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (kind == 1) { _cfg.UiTitleFontFile = dest; _cfg.UiTitleFontName = family; }
                    else if (kind == 2) { _cfg.UiMonoFontFile = dest; _cfg.UiMonoFontName = family; }
                    else { _cfg.UiFontFile = dest; _cfg.UiFontName = family; }
                    TouchSave();
                    MessageBox.Show("字体已保存，重启软件后生效。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("字体加载失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        private void AutoAccentFromBackground()
        {
            string path = _cfg.BackgroundImage;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("先选一张图片背景。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".webm" || ext == ".wmv" || ext == ".avi" || ext == ".mov" || ext == ".mkv")
            {
                MessageBox.Show("视频背景暂不支持自动取色，先用图片背景。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 48;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                int w = bmp.PixelWidth, h = bmp.PixelHeight;
                byte[] px = new byte[w * h * 4];
                bmp.CopyPixels(px, w * 4, 0);
                double sr = 0, sg = 0, sb = 0, wt = 0, ar = 0, ag = 0, ab = 0;
                for (int i = 0; i < w * h; i++)
                {
                    double b = px[i * 4] / 255.0, g = px[i * 4 + 1] / 255.0, r = px[i * 4 + 2] / 255.0;
                    ar += r; ag += g; ab += b;
                    double max = Math.Max(r, Math.Max(g, b));
                    double min = Math.Min(r, Math.Min(g, b));
                    if (max < 0.10 || max - min < 0.03) continue;
                    double sat = (max - min) / max;
                    double wgt = max * sat;
                    sr += r * wgt; sg += g * wgt; sb += b * wgt; wt += wgt;
                }
                if (wt <= 0) { sr = ar; sg = ag; sb = ab; wt = w * h; }
                double rr = sr / wt, gg = sg / wt, bb = sb / wt;

                double mx = Math.Max(rr, Math.Max(gg, bb));
                double mn = Math.Min(rr, Math.Min(gg, bb));
                double hue = 0;
                if (mx - mn > 0.0001)
                {
                    if (mx == rr) hue = 60 * (((gg - bb) / (mx - mn)) % 6);
                    else if (mx == gg) hue = 60 * (((bb - rr) / (mx - mn)) + 2);
                    else hue = 60 * (((rr - gg) / (mx - mn)) + 4);
                }
                if (hue < 0) hue += 360;
                double satOut = Math.Min(0.95, Math.Max(0.55, mx - mn == 0 ? 0.6 : (mx - mn) / mx));
                double valOut = Math.Min(0.95, Math.Max(0.72, mx));

                double c = valOut * satOut;
                double x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
                double m = valOut - c;
                double r2 = 0, g2 = 0, b2 = 0;
                int seg = (int)(hue / 60) % 6;
                if (seg == 0) { r2 = c; g2 = x; }
                else if (seg == 1) { r2 = x; g2 = c; }
                else if (seg == 2) { g2 = c; b2 = x; }
                else if (seg == 3) { g2 = x; b2 = c; }
                else if (seg == 4) { r2 = x; b2 = c; }
                else { r2 = c; b2 = x; }

                System.Drawing.Color color = System.Drawing.Color.FromArgb(
                    (int)Math.Round((r2 + m) * 255), (int)Math.Round((g2 + m) * 255), (int)Math.Round((b2 + m) * 255));
                ApplyAccentHex(ThemeTokens.ToHex(color));
                MessageBox.Show("已按背景主色设置强调色：" + ThemeTokens.ToHex(color), "外观", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("取色失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PickMusic()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "音频|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg;*.opus";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _cfg.BackgroundMusic = dlg.FileName;
                _cfg.BackgroundMusicEnabled = true;
                TouchSave();
                RebuildForThemeChange();
            }
        }

        private UIElement FontSizeField(string label, int value, Action<int> set, int column)
        {
            StackPanel box = new StackPanel();
            box.Margin = new Thickness(0, 0, 12, 0);
            TextBlock t = new TextBlock();
            t.Text = label;
            t.FontSize = 12;
            t.Foreground = Sub;
            t.Margin = new Thickness(0, 0, 0, 4);
            box.Children.Add(t);
            VigilNumberField f = new VigilNumberField(_light, Line, Ink, Sub, 86);
            f.Minimum = 8;
            f.Maximum = 96;
            f.Value = value;
            f.ValueChanged += delegate
            {
                set((int)f.Value);
                TouchSave();
            };
            box.Children.Add(f);
            Grid.SetColumn(box, column);
            return box;
        }

        private static readonly string[] ThemePackKeys =
        {
            "PageBgHex", "PanelHex", "PanelAltHex", "LineHex", "TextHex", "SubTextHex", "MutedHex",
            "AccentHex", "WarnHex", "DangerHex", "SuccessHex", "InfoHex",
            "LightPageBgHex", "LightPanelHex", "LightPanelAltHex", "LightLineHex", "LightTextHex",
            "LightSubTextHex", "LightMutedHex", "LightAccentHex", "LightWarnHex", "LightDangerHex",
            "LightSuccessHex", "LightInfoHex",
            "FontTitleSize", "FontBodySize", "FontDescSize",
            "BackgroundOpacity", "PanelAnimQuality", "AnimationsEnabled", "ThemeSchema",
            "GlassWindow", "GlassStrength", "GlassCards", "GlassTintMode",
            "GlassFrost", "GlassOpacity", "GlassLightEnabled", "GlassLightBrightness", "GlassLightSource",
            "BackgroundFill", "BackgroundDarken", "UiFontName",
            "UiThemeMode", "ClickFxEnabled", "ClickFxGap", "BackgroundMusicEnabled", "BackgroundMusicLoop", "BackgroundMusicVolume"
        };

        private string ThemeValue(string key)
        {
            switch (key)
            {
                case "PageBgHex": return _cfg.PageBgHex;
                case "PanelHex": return _cfg.PanelHex;
                case "PanelAltHex": return _cfg.PanelAltHex;
                case "LineHex": return _cfg.LineHex;
                case "TextHex": return _cfg.TextHex;
                case "SubTextHex": return _cfg.SubTextHex;
                case "MutedHex": return _cfg.MutedHex;
                case "AccentHex": return _cfg.AccentHex;
                case "WarnHex": return _cfg.WarnHex;
                case "DangerHex": return _cfg.DangerHex;
                case "SuccessHex": return _cfg.SuccessHex;
                case "InfoHex": return _cfg.InfoHex;
                case "LightPageBgHex": return _cfg.LightPageBgHex;
                case "LightPanelHex": return _cfg.LightPanelHex;
                case "LightPanelAltHex": return _cfg.LightPanelAltHex;
                case "LightLineHex": return _cfg.LightLineHex;
                case "LightTextHex": return _cfg.LightTextHex;
                case "LightSubTextHex": return _cfg.LightSubTextHex;
                case "LightMutedHex": return _cfg.LightMutedHex;
                case "LightAccentHex": return _cfg.LightAccentHex;
                case "LightWarnHex": return _cfg.LightWarnHex;
                case "LightDangerHex": return _cfg.LightDangerHex;
                case "LightSuccessHex": return _cfg.LightSuccessHex;
                case "LightInfoHex": return _cfg.LightInfoHex;
                case "FontTitleSize": return _cfg.FontTitleSize.ToString();
                case "FontBodySize": return _cfg.FontBodySize.ToString();
                case "FontDescSize": return _cfg.FontDescSize.ToString();
                case "BackgroundOpacity": return _cfg.BackgroundOpacity.ToString();
                case "PanelAnimQuality": return _cfg.PanelAnimQuality.ToString();
                case "AnimationsEnabled": return _cfg.UiAnimationsEnabled ? "1" : "0";
                case "GlassWindow": return _cfg.GlassWindow ? "1" : "0";
                case "GlassStrength": return _cfg.GlassStrength.ToString();
                case "GlassCards": return _cfg.GlassCards ? "1" : "0";
                case "GlassTintMode": return _cfg.GlassTintMode.ToString();
                case "GlassFrost": return _cfg.GlassFrost.ToString();
                case "GlassOpacity": return _cfg.GlassOpacity.ToString();
                case "GlassLightEnabled": return _cfg.GlassLightEnabled ? "1" : "0";
                case "GlassLightBrightness": return _cfg.GlassLightBrightness.ToString();
                case "GlassLightSource": return _cfg.GlassLightSource.ToString();
                case "BackgroundFill": return _cfg.BackgroundFill.ToString();
                case "BackgroundDarken": return _cfg.BackgroundDarken.ToString();
                case "UiThemeMode": return _cfg.UiThemeMode.ToString();
                case "ClickFxEnabled": return _cfg.ClickFxEnabled ? "1" : "0";
                case "ClickFxGap": return _cfg.ClickFxGap.ToString();
                case "BackgroundMusicEnabled": return _cfg.BackgroundMusicEnabled ? "1" : "0";
                case "BackgroundMusicLoop": return _cfg.BackgroundMusicLoop ? "1" : "0";
                case "BackgroundMusicVolume": return _cfg.BackgroundMusicVolume.ToString();
                case "UiFontName": return _cfg.UiFontName ?? "";
                case "ThemeSchema": return "2";
                default: return "";
            }
        }

        private void ApplyThemeValue(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            int n;
            switch (key)
            {
                case "PageBgHex": _cfg.PageBgHex = ThemeTokens.NormalizeHex(value, _cfg.PageBgHex); break;
                case "PanelHex": _cfg.PanelHex = ThemeTokens.NormalizeHex(value, _cfg.PanelHex); break;
                case "PanelAltHex": _cfg.PanelAltHex = ThemeTokens.NormalizeHex(value, _cfg.PanelAltHex); break;
                case "LineHex": _cfg.LineHex = ThemeTokens.NormalizeHex(value, _cfg.LineHex); break;
                case "TextHex": _cfg.TextHex = ThemeTokens.NormalizeHex(value, _cfg.TextHex); break;
                case "SubTextHex": _cfg.SubTextHex = ThemeTokens.NormalizeHex(value, _cfg.SubTextHex); break;
                case "MutedHex": _cfg.MutedHex = ThemeTokens.NormalizeHex(value, _cfg.MutedHex); break;
                case "AccentHex": _cfg.AccentHex = ThemeTokens.NormalizeHex(value, _cfg.AccentHex); break;
                case "WarnHex": _cfg.WarnHex = ThemeTokens.NormalizeHex(value, _cfg.WarnHex); break;
                case "DangerHex": _cfg.DangerHex = ThemeTokens.NormalizeHex(value, _cfg.DangerHex); break;
                case "SuccessHex": _cfg.SuccessHex = ThemeTokens.NormalizeHex(value, _cfg.SuccessHex); break;
                case "InfoHex": _cfg.InfoHex = ThemeTokens.NormalizeHex(value, _cfg.InfoHex); break;
                case "LightPageBgHex": _cfg.LightPageBgHex = ThemeTokens.NormalizeHex(value, _cfg.LightPageBgHex); break;
                case "LightPanelHex": _cfg.LightPanelHex = ThemeTokens.NormalizeHex(value, _cfg.LightPanelHex); break;
                case "LightPanelAltHex": _cfg.LightPanelAltHex = ThemeTokens.NormalizeHex(value, _cfg.LightPanelAltHex); break;
                case "LightLineHex": _cfg.LightLineHex = ThemeTokens.NormalizeHex(value, _cfg.LightLineHex); break;
                case "LightTextHex": _cfg.LightTextHex = ThemeTokens.NormalizeHex(value, _cfg.LightTextHex); break;
                case "LightSubTextHex": _cfg.LightSubTextHex = ThemeTokens.NormalizeHex(value, _cfg.LightSubTextHex); break;
                case "LightMutedHex": _cfg.LightMutedHex = ThemeTokens.NormalizeHex(value, _cfg.LightMutedHex); break;
                case "LightAccentHex": _cfg.LightAccentHex = ThemeTokens.NormalizeHex(value, _cfg.LightAccentHex); break;
                case "LightWarnHex": _cfg.LightWarnHex = ThemeTokens.NormalizeHex(value, _cfg.LightWarnHex); break;
                case "LightDangerHex": _cfg.LightDangerHex = ThemeTokens.NormalizeHex(value, _cfg.LightDangerHex); break;
                case "LightSuccessHex": _cfg.LightSuccessHex = ThemeTokens.NormalizeHex(value, _cfg.LightSuccessHex); break;
                case "LightInfoHex": _cfg.LightInfoHex = ThemeTokens.NormalizeHex(value, _cfg.LightInfoHex); break;
                case "FontTitleSize":
                    if (int.TryParse(value, out n)) _cfg.FontTitleSize = Math.Max(8, Math.Min(96, n));
                    break;
                case "FontBodySize":
                    if (int.TryParse(value, out n)) _cfg.FontBodySize = Math.Max(8, Math.Min(96, n));
                    break;
                case "FontDescSize":
                    if (int.TryParse(value, out n)) _cfg.FontDescSize = Math.Max(8, Math.Min(96, n));
                    break;
                case "BackgroundOpacity":
                    if (int.TryParse(value, out n)) _cfg.BackgroundOpacity = Math.Max(0, Math.Min(100, n));
                    break;
                case "PanelAnimQuality":
                    if (int.TryParse(value, out n)) _cfg.PanelAnimQuality = Math.Max(0, Math.Min(4, n));
                    break;
                case "AnimationsEnabled":
                    _cfg.UiAnimationsEnabled = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "GlassWindow":
                    _cfg.GlassWindow = value == "1";
                    break;
                case "GlassStrength":
                    if (int.TryParse(value, out n)) _cfg.GlassStrength = Math.Max(0, Math.Min(100, n));
                    break;
                case "GlassCards":
                    _cfg.GlassCards = value == "1";
                    break;
                case "GlassTintMode":
                    if (int.TryParse(value, out n)) _cfg.GlassTintMode = Math.Max(0, Math.Min(2, n));
                    break;
                case "GlassFrost":
                    if (int.TryParse(value, out n)) _cfg.GlassFrost = Math.Max(0, Math.Min(100, n));
                    break;
                case "GlassOpacity":
                    if (int.TryParse(value, out n)) _cfg.GlassOpacity = Math.Max(0, Math.Min(100, n));
                    break;
                case "GlassLightEnabled":
                    _cfg.GlassLightEnabled = value == "1";
                    break;
                case "GlassLightBrightness":
                    if (int.TryParse(value, out n)) _cfg.GlassLightBrightness = Math.Max(0, Math.Min(100, n));
                    break;
                case "GlassLightSource":
                    if (int.TryParse(value, out n)) _cfg.GlassLightSource = Math.Max(0, Math.Min(4, n));
                    break;
                case "BackgroundFill":
                    if (int.TryParse(value, out n)) _cfg.BackgroundFill = Math.Max(0, Math.Min(2, n));
                    break;
                case "BackgroundDarken":
                    if (int.TryParse(value, out n)) _cfg.BackgroundDarken = Math.Max(0, Math.Min(100, n));
                    break;
                case "UiThemeMode":
                    if (int.TryParse(value, out n)) _cfg.UiThemeMode = Math.Max(-1, Math.Min(1, n));
                    break;
                case "ClickFxEnabled":
                    _cfg.ClickFxEnabled = value == "1";
                    break;
                case "ClickFxGap":
                    if (int.TryParse(value, out n)) _cfg.ClickFxGap = Math.Max(0, Math.Min(240, n));
                    break;
                case "BackgroundMusicEnabled":
                    _cfg.BackgroundMusicEnabled = value == "1";
                    break;
                case "BackgroundMusicLoop":
                    _cfg.BackgroundMusicLoop = value == "1";
                    break;
                case "BackgroundMusicVolume":
                    if (int.TryParse(value, out n)) _cfg.BackgroundMusicVolume = Math.Max(0, Math.Min(100, n));
                    break;
            }
        }

        private void ExportThemePack()
        {
            using (System.Windows.Forms.SaveFileDialog dlg = new System.Windows.Forms.SaveFileDialog())
            {
                dlg.Filter = "主题包|*.zip";
                dlg.FileName = "A.R.C.T-theme.zip";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                    using (FileStream fs = File.Create(dlg.FileName))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        ZipArchiveEntry ini = zip.CreateEntry("theme.ini");
                        using (StreamWriter w = new StreamWriter(ini.Open(), new System.Text.UTF8Encoding(false)))
                        {
                            w.WriteLine("[Theme]");
                            for (int i = 0; i < ThemePackKeys.Length; i++)
                                w.WriteLine(ThemePackKeys[i] + "=" + ThemeValue(ThemePackKeys[i]));
                        }
                        if (!string.IsNullOrEmpty(_cfg.BackgroundImage) && File.Exists(_cfg.BackgroundImage))
                        {
                            string ext = Path.GetExtension(_cfg.BackgroundImage);
                            if (string.IsNullOrEmpty(ext)) ext = ".png";
                            ZipArchiveEntry img = zip.CreateEntry("background" + ext);
                            using (Stream dst = img.Open())
                            using (FileStream src = File.OpenRead(_cfg.BackgroundImage))
                                src.CopyTo(dst);
                        }
                        if (!string.IsNullOrEmpty(_cfg.BackgroundMusic) && File.Exists(_cfg.BackgroundMusic))
                        {
                            string mext = Path.GetExtension(_cfg.BackgroundMusic);
                            if (string.IsNullOrEmpty(mext)) mext = ".mp3";
                            ZipArchiveEntry mus = zip.CreateEntry("music" + mext);
                            using (Stream md = mus.Open())
                            using (FileStream ms = File.OpenRead(_cfg.BackgroundMusic))
                                ms.CopyTo(md);
                        }
                    }
                    MessageBox.Show("已导出主题包。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ImportThemePack()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Filter = "主题包|*.zip";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    using (ZipArchive zip = ZipFile.OpenRead(dlg.FileName))
                    {
                        ZipArchiveEntry ini = null;
                        ZipArchiveEntry bg = null;
                        ZipArchiveEntry mus = null;
                        ZipArchiveEntry fontEntry = null;
                        for (int i = 0; i < zip.Entries.Count; i++)
                        {
                            ZipArchiveEntry e = zip.Entries[i];
                            if (string.Equals(e.FullName, "theme.ini", StringComparison.OrdinalIgnoreCase)) ini = e;
                            else if (e.FullName.StartsWith("background.", StringComparison.OrdinalIgnoreCase)) bg = e;
                            else if (e.FullName.StartsWith("music.", StringComparison.OrdinalIgnoreCase)) mus = e;
                            else if (e.FullName.StartsWith("font.", StringComparison.OrdinalIgnoreCase)) fontEntry = e;
                        }
                        if (ini == null)
                        {
                            MessageBox.Show("主题包里没有 theme.ini。", "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        Dictionary<string, string> values = new Dictionary<string, string>();
                        using (StreamReader r = new StreamReader(ini.Open(), System.Text.Encoding.UTF8))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                                int eq = line.IndexOf('=');
                                if (eq <= 0) continue;
                                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                            }
                        }
                        string schema;
                        if (!values.TryGetValue("ThemeSchema", out schema) || (schema != "1" && schema != "2"))
                        {
                            MessageBox.Show("主题包版本不支持。", "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                        if (File.Exists(configPath))
                        {
                            string backup = configPath + ".bak-theme-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                            File.Copy(configPath, backup, true);
                        }
                        for (int i = 0; i < ThemePackKeys.Length; i++)
                        {
                            string v;
                            if (values.TryGetValue(ThemePackKeys[i], out v)) ApplyThemeValue(ThemePackKeys[i], v);
                        }
                        if (bg != null)
                        {
                            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            string ext = Path.GetExtension(bg.FullName);
                            if (string.IsNullOrEmpty(ext)) ext = ".png";
                            string path = Path.Combine(dir, "theme-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ext);
                            using (Stream src = bg.Open())
                            using (FileStream dst = File.Create(path))
                                src.CopyTo(dst);
                            _cfg.BackgroundImage = path;
                        }
                        ThemeTokens.Configure(_cfg);
                        TouchSave();
                    }
                    MessageBox.Show("主题包已导入。", "外观", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导入失败：" + ex.Message, "外观", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
    /// <summary>外观页底部的简笔画装饰：网格、六边形容器、菱形核心和取景刻度。</summary>
    internal sealed class AppearanceArtCanvas : FrameworkElement
    {
        private readonly Brush _accent, _line, _sub, _panel;

        public AppearanceArtCanvas(Brush accent, Brush line, Brush sub, Brush panel)
        {
            _accent = accent;
            _line = line;
            _sub = sub;
            _panel = panel;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 2 || h <= 2) return;
            dc.DrawRectangle(_panel, null, new Rect(0, 0, w, h));

            Pen grid = new Pen(_line, 0.55);
            for (double x = 0; x < w; x += 26) dc.DrawLine(grid, new Point(x, 0), new Point(x, h));
            for (double y = 0; y < h; y += 26) dc.DrawLine(grid, new Point(0, y), new Point(w, y));

            Point c = new Point(w * 0.72, h * 0.5);
            double r = Math.Min(w, h) * 0.31;
            Pen soft = new Pen(_line, 1.0);
            Pen acc = new Pen(_accent, 1.4);

            // 外围六边形
            StreamGeometry hex = new StreamGeometry();
            using (StreamGeometryContext ctx = hex.Open())
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 3 * i - Math.PI / 6;
                    Point p = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
                    if (i == 0) ctx.BeginFigure(p, false, true);
                    else ctx.LineTo(p, true, false);
                }
            }
            hex.Freeze();
            dc.DrawGeometry(null, soft, hex);

            // 内层菱形 + 圆
            Point top = new Point(c.X, c.Y - r * 0.62);
            Point right = new Point(c.X + r * 0.62, c.Y);
            Point bottom = new Point(c.X, c.Y + r * 0.62);
            Point left = new Point(c.X - r * 0.62, c.Y);
            dc.DrawLine(acc, top, right);
            dc.DrawLine(acc, right, bottom);
            dc.DrawLine(acc, bottom, left);
            dc.DrawLine(acc, left, top);
            dc.DrawEllipse(null, new Pen(_line, 0.8), c, r * 0.82, r * 0.82);

            // 四向刻度
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 2 * i;
                Point inner = new Point(c.X + Math.Cos(a) * r * 1.04, c.Y + Math.Sin(a) * r * 1.04);
                Point outer = new Point(c.X + Math.Cos(a) * r * 1.16, c.Y + Math.Sin(a) * r * 1.16);
                dc.DrawLine(soft, inner, outer);
            }

            // 从左边引出的两条结构线
            dc.DrawLine(soft, new Point(w * 0.06, c.Y - r * 0.55), new Point(c.X - r * 0.95, c.Y - r * 0.55));
            dc.DrawLine(soft, new Point(w * 0.06, c.Y + r * 0.55), new Point(c.X - r * 0.95, c.Y + r * 0.55));
            dc.DrawLine(soft, new Point(w * 0.06, c.Y - r * 0.55), new Point(w * 0.06, c.Y + r * 0.55));

        }

    }
}
