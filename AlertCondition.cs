using System;

namespace SCPSLAlert
{
    /// <summary>
    /// 条件显示：一条提示只有在满足条件时才弹出来，否则当作它不存在。
    ///
    /// 设计成“一个条件 + 一个数值”，够用又好懂。
    /// 条件分成两类：
    ///   · 和战术面板里的数据有关（票数、079、发电机）
    ///   · 和本回合进展有关（击杀数、已进行时间）
    ///
    /// 没填数据的时候，跟数据相关的条件一律当作“不满足”，这样不会误报。
    /// </summary>
    internal static class AlertCondition
    {
        public const int None = 0;
        public const int TicketAtLeast = 1;
        public const int TicketAtMost = 2;
        public const int KillAtLeast = 3;
        public const int Has079 = 4;
        public const int GenActiveAtLeast = 5;
        public const int RoundMinuteAtLeast = 6;
        public const int RoundMinuteAtMost = 7;
        public const int FactionTicketDiff = 8;   // 两方票数差 >= N

        public static readonly string[] Names =
        {
            "无条件（总是可以显示）",
            "总票数 ≥ N",
            "总票数 ≤ N",
            "击杀数 ≥ N",
            "本回合有 SCP-079",
            "079 发电机已启动 ≥ N 台",
            "回合已进行 ≥ N 分钟",
            "回合已进行 ≤ N 分钟",
            "两方票数差 ≥ N"
        };

        public static bool NeedsValue(int type)
        {
            return type != None && type != Has079;
        }

        public static string UnitOf(int type)
        {
            switch (type)
            {
                case TicketAtLeast:
                case TicketAtMost:
                case FactionTicketDiff:
                    return "票";
                case KillAtLeast:
                    return "个";
                case GenActiveAtLeast:
                    return "台";
                case RoundMinuteAtLeast:
                case RoundMinuteAtMost:
                    return "分钟";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 判断是否满足条件。
        /// log      —— 战术面板的数据（可能为 null）
        /// roundMin —— 本回合已进行多少分钟
        /// </summary>
        public static bool Satisfied(int type, int value, TacticalLog log, double roundMin)
        {
            switch (type)
            {
                case None:
                    return true;

                case TicketAtLeast:
                    return log != null && log.TotalTickets >= value;

                case TicketAtMost:
                    // 没数据时票数是 0，所以 ≤ N 天然成立
                    return log == null ? 0 <= value : log.TotalTickets <= value;

                case KillAtLeast:
                    return log != null && log.TotalKills >= value;

                case Has079:
                    return log != null && log.Has079;

                case GenActiveAtLeast:
                    return log != null && log.GeneratorsActive >= value;

                case RoundMinuteAtLeast:
                    return roundMin >= value;

                case RoundMinuteAtMost:
                    return roundMin <= value;

                case FactionTicketDiff:
                    if (log == null) return false;
                    return Math.Abs(log.TicketsOf(TacticalLog.MTF)
                                  - log.TicketsOf(TacticalLog.CHAOS)) >= value;

                default:
                    return true;
            }
        }

        /// <summary>给界面显示的一句话描述。</summary>
        public static string Describe(int type, int value)
        {
            if (type == None) return "";
            if (type == Has079) return "本回合有 079";
            if (type < 0 || type >= Names.Length) return "";
            string unit = UnitOf(type);
            string op = "≥";
            if (type == TicketAtMost || type == RoundMinuteAtMost) op = "≤";
            return Names[type] + "（" + op + " " + value + " " + unit + "）";
        }
    }
}
