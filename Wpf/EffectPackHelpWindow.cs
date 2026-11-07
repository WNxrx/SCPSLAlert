using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 外部特效包格式说明。只展示/复制模板，不执行外部代码。
    /// </summary>
    internal sealed class EffectPackHelpWindow : Window
    {
        public const string TemplateText =
@"; SCPSLAlert 外部特效包
; 保存为 UTF-8 的 .arctfx；也可以放一个 .arctfx 或 effect.ini 进 .zip
; 必填：name、type
; type 只能是：shake（抖动）、flash（闪屏）、confetti（礼炮）、pulse（边缘脉冲）
; 参数超出范围会自动夹紧；type 不认识会拒绝导入

name=黄金礼炮
type=confetti
count=88
duration=1200
color1=#FFD700
color2=#FFA500

; ---- 如何实现各类特效 ----
; 抖动：普通提示和漂浮文字一起抖，只抖软件表面，不能抖游戏画面
; name=我的抖动
; type=shake
; amplitude=8        ; 0-24 像素
; duration=320       ; 60-1200 毫秒

; 闪屏：全屏颜色一闪，实际强度最高 0.35，连续两次至少间隔 400ms
; name=我的闪屏
; type=flash
; intensity=0.22     ; 0.03-0.8，实际再受 0.35 上限
; duration=180       ; 60-400 毫秒
; color1=#FFFFFF

; 礼炮：粒子从屏幕上方落下
; name=我的礼炮
; type=confetti
; count=60           ; 10-120 个
; duration=1200      ; 400-1800 毫秒
; color1=#FF4D4D
; color2=#43CFBB

; 边缘脉冲：屏幕四周亮起一条色边
; name=我的脉冲
; type=pulse
; intensity=0.45     ; 0.05-0.8
; duration=600       ; 120-1600 毫秒
; color1=#43CFBB

; 注意：外部包只能用这四种内置渲染器做参数化组合，不能执行代码，
; 也不能凭空增加新的渲染类型。";

        public EffectPackHelpWindow()
        {
            Title = "特效包格式";
            Width = 760;
            Height = 640;
            MinWidth = 620;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(11, 13, 14));
            Foreground = new SolidColorBrush(Color.FromRgb(230, 227, 218));
            FontFamily = UiFonts.Body;

            Grid root = new Grid();
            root.Margin = new Thickness(16);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel head = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "特效包格式";
            title.FontSize = 20;
            title.FontWeight = FontWeights.Bold;
            head.Children.Add(title);
            TextBlock brief = new TextBlock();
            brief.Text = ".arctfx 键值文本；name 与 type 必填。type：抖动 / 闪屏 / 礼炮 / 边缘脉冲。";
            brief.FontSize = 10.5;
            brief.Foreground = new SolidColorBrush(Color.FromRgb(150, 160, 158));
            brief.TextWrapping = TextWrapping.Wrap;
            brief.Margin = new Thickness(0, 4, 0, 10);
            head.Children.Add(brief);
            root.Children.Add(head);

            TextBox body = new TextBox();
            body.Text = TemplateText;
            body.IsReadOnly = true;
            body.AcceptsReturn = true;
            body.TextWrapping = TextWrapping.NoWrap;
            body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            body.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            body.FontFamily = UiFonts.Mono;
            body.FontSize = 11;
            body.Background = new SolidColorBrush(Color.FromRgb(15, 19, 22));
            body.Foreground = Foreground;
            body.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 60));
            body.Padding = new Thickness(10);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            StackPanel bottom = new StackPanel();
            bottom.Orientation = Orientation.Horizontal;
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 10, 0, 0);
            TextBlock status = new TextBlock();
            status.FontSize = 10;
            status.Foreground = new SolidColorBrush(Color.FromRgb(67, 207, 187));
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Margin = new Thickness(0, 0, 10, 0);
            bottom.Children.Add(status);

            Button copy = new Button();
            copy.Content = "复制完整结构";
            copy.Width = 112;
            copy.Height = 30;
            copy.Background = new SolidColorBrush(Color.FromRgb(67, 207, 187));
            copy.Foreground = new SolidColorBrush(Color.FromRgb(12, 16, 20));
            copy.BorderThickness = new Thickness(0);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(TemplateText);
                    status.Text = "已复制";
                }
                catch { status.Text = "复制失败"; }
            };
            bottom.Children.Add(copy);

            Button close = new Button();
            close.Content = "关闭";
            close.Width = 72;
            close.Height = 30;
            close.Margin = new Thickness(8, 0, 0, 0);
            close.Click += delegate { Close(); };
            bottom.Children.Add(close);

            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);
            Content = root;
        }
    }
}
