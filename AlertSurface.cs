using System;
using System.Drawing;

namespace SCPSLAlert
{
    /// <summary>
    /// 一条屏幕提示的“表面”。旧版用 WinForms 分层窗（OverlayForm），
    /// 新版 A.R.C.T 提示用 WPF 透明窗（WpfAlertOverlay）；AlertManager 只认这个接口，
    /// 所以两套主题可以随时切换而不用改倒计时 / 事件判定这些业务逻辑。
    /// </summary>
    internal interface IAlertSurface : IDisposable
    {
        /// <summary>拖动结束、坐标已改变时触发（AlertManager 拿它写回 config）。</summary>
        event EventHandler PositionChanged;

        bool IsPopped { get; }

        string CurrentText { get; }

        /// <summary>窗口当前所在位置（物理像素，和 config 里的 X/Y 同一坐标系）。</summary>
        Point RestLocation { get; }

        /// <summary>避让后的实际显示矩形（物理像素）。RestLocation 仍是用户配置的锚点。</summary>
        Rectangle DisplayBounds { get; }

        /// <summary>
        /// 只改显示位置，不写回配置。第 8 批防重叠管理者用；
        /// 用户拖动仍然走 SetRestLocation。
        /// </summary>
        void SetDisplayPosition(int x, int y);

        /// <summary>整块绘制表面的尺寸（物理像素，用来做顶部居中）。</summary>
        Size SurfaceSize { get; }

        bool EditMode { get; set; }

        bool Highlight { get; set; }

        void ApplyStyle(WindowStyle style, Config cfg);

        void SetRestLocation(int x, int y);

        void Nudge(int dx, int dy);

        void Pop(string text);

        /// <summary>倒计时读数（新版提示用；旧版忽略）。total ≤ 0 表示总量未知，只画扫描线。</summary>
        void SetTimer(double remainSeconds, double totalSeconds);

        void Retract();

        void HideNow();

        void ReassertExStyle();

        void Close();
    }
}
