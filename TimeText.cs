using System;

namespace SCPSLAlert
{
    /// <summary>
    /// 需求八.1：带时间的提示统一格式。
    ///
    /// 模式：
    ///   0 = 自动（默认）：&lt;60 秒「x秒」；&lt;60 分「x分yy秒」；再往上「x小时y分」；
    ///   1 = 只要秒： 「x秒」；
    ///   2 = 分秒：   「x分yy秒」；
    ///   3 = 时分：   「x小时y分」。
    ///
    /// 优先级：单条提示（AlertConfig.TimeFormat，-1 = 跟随全局）> 全局（Config.TimeFormat）。
    /// 「条件类型」这一级本轮没有单独做，先留出接口（以后可按提示分组覆盖）。
    /// </summary>
    internal static class TimeText
    {
        public const int Auto = 0;
        public const int Seconds = 1;
        public const int MinSec = 2;
        public const int HourMin = 3;

        public static string Format(double seconds, int mode)
        {
            if (seconds < 0) seconds = 0;
            if (mode == Seconds) return Ceil(seconds) + "秒";
            if (mode == MinSec)
            {
                int total = Ceil(seconds);
                return (total / 60) + "分" + (total % 60).ToString("00") + "秒";
            }
            if (mode == HourMin)
            {
                int total = Ceil(seconds);
                return (total / 3600) + "小时" + ((total % 3600) / 60) + "分";
            }

            // 自动
            if (seconds < 60) return Ceil(seconds) + "秒";
            int t = Ceil(seconds);
            if (t < 3600) return (t / 60) + "分" + (t % 60).ToString("00") + "秒";
            return (t / 3600) + "小时" + ((t % 3600) / 60) + "分";
        }

        /// <summary>按“单提示 > 全局”解析出实际模式。</summary>
        public static int Resolve(Config cfg, AlertConfig alert)
        {
            if (alert != null && alert.TimeFormat >= 0 && alert.TimeFormat <= 3) return alert.TimeFormat;
            if (cfg != null && cfg.TimeFormat >= 0 && cfg.TimeFormat <= 3) return cfg.TimeFormat;
            return Auto;
        }

        public static string ForPrompt(Config cfg, AlertConfig alert, double seconds)
        {
            return Format(seconds, Resolve(cfg, alert));
        }

        private static int Ceil(double seconds)
        {
            if (seconds <= 0) return 0;
            return (int)Math.Ceiling(seconds);
        }
    }
}
