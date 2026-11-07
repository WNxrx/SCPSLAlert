using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SCPSLAlert
{
    /// <summary>
    /// Win10/11 的窗口毛玻璃（SetWindowCompositionAttribute 的亚克力）。
    /// 失败就静默退回普通窗口，不影响其他功能。
    /// </summary>
    internal static class GlassEffect
    {
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public static void Apply(Window window, bool enabled, int strength)
        {
            Apply(window, enabled, strength, true);
        }

        public static void Apply(Window window, bool enabled, int strength, bool lightTint)
        {
            if (window == null) return;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (enabled)
                {
                    int dark = lightTint ? 0 : 1;
                    DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                    int backdrop = 3;   // 3 = Acrylic, 2 = Mica
                    if (DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) == 0) return;
                }
                else
                {
                    int backdropOff = 1;
                    DwmSetWindowAttribute(hwnd, 38, ref backdropOff, sizeof(int));
                }

                AccentPolicy accent = new AccentPolicy();
                if (enabled)
                {
                    accent.AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    int a = (int)(Math.Max(0, Math.Min(100, strength)) * 2.2);
                    // GradientColor 是 ABGR：AABBGGRR。按背景明暗自动选偏黑 / 偏白。
                    if (lightTint)
                        accent.GradientColor = (a << 24) | (0xF4 << 16) | (0xF8 << 8) | 0xFA;
                    else
                        accent.GradientColor = (a << 24) | (0x0A << 16) | (0x0E << 8) | 0x06;
                }
                else
                {
                    accent.AccentState = 0;
                }
                accent.AccentFlags = 2;

                int size = Marshal.SizeOf(typeof(AccentPolicy));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    WindowCompositionAttributeData data = new WindowCompositionAttributeData();
                    data.Attribute = WCA_ACCENT_POLICY;
                    data.Data = ptr;
                    data.SizeOfData = size;
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { }
        }
    }
}
