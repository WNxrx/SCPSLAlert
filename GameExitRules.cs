using System;

namespace SCPSLAlert
{
    /// <summary>游戏这次退出的性质。</summary>
    internal enum GameExitKind
    {
        /// <summary>用户主动关闭（Alt+F4 / 控制台 quit / 退出码 0）。</summary>
        Normal,
        /// <summary>没有崩溃证据，但也不是用户正常关闭（被结束、卡死、启动失败……）。</summary>
        Abnormal,
        /// <summary>有系统异常码 / WER / 转储 / 崩溃日志。</summary>
        Crash
    }

    /// <summary>
    /// 判断"游戏这次退出算不算崩溃、算不算正常退出"。
    ///
    /// 早先只靠退出码和日志里的 GOODBYE：
    ///   · SCP:SL 正常退出时日志里没有 GOODBYE（那是控制台 quit 才有的）；
    ///   · 正常退出、卡死后被杀、被任务管理器结束，退出码都可能是 0 或 -1。
    /// 所以退出码本身不足以判断。
    ///
    /// 现在的判据（从上到下）：
    ///   1. 有崩溃证据（NTSTATUS 异常码 / WER / 转储 / Unity 崩溃日志 / AppHang）
    ///      → 崩溃或非正常退出。这一条优先于 Alt+F4：玩家在崩溃之后
    ///      按 Alt+F4 想关掉它，也盖不掉已经留下的证据。
    ///   2. 日志里有控制台 quit 的收尾 → 正常。
    ///   3. Alt+F4 且按下时窗口有响应、按键后及时退出、按键前没有卡死记录
    ///      → 正常。Alt+F4 只是"用户想关游戏"的旁证；按下时已经卡死、
    ///      按完迟迟不退出、系统在按键前就记过 AppHang，都不算正常关闭。
    ///   4. 退出码 0 且没有卡死记录 → 正常（窗口 X、菜单退出、Steam 停止）。
    ///   5. 其余（典型是 -1）→ 没有崩溃证据的非正常退出，仍然记录并弹窗，
    ///      只是会明确写"没有崩溃证据"，不冒充崩溃。
    /// 这样既不会把 Alt+F4 报成崩溃，也尽量不放过真正异常的退出。
    /// </summary>
    internal static class GameExitRules
    {
        /// <summary>
        /// Alt+F4 按下之后，多久之内退出才算"这次按键真的把游戏关掉了"。
        /// 卡死/崩溃时按 Alt+F4，进程往往要等外部强制结束后才退出，会超出这个窗口。
        /// </summary>
        public const int AltF4HonorMs = 12000;

        /// <summary>日志里有没有控制台 quit 的收尾（不是判断正常退出的必要条件）。</summary>
        public static bool IsCleanLog(string text)
        {
            return Contains(text, ">>> quit") || Contains(text, "GOODBYE");
        }

        /// <summary>
        /// 退出码本身是不是一个系统级异常码。
        /// 0 / -1 不是；0xC000013A（关窗口/控制台中断）、0x40010004（调试器终止）
        /// 也不是崩溃；其余最高位为 1 的 NTSTATUS 才算。
        /// </summary>
        public static bool IsCrashExitCode(int exitCode)
        {
            uint code = unchecked((uint)exitCode);
            if (code == 0 || code == 0xFFFFFFFF) return false;
            if (code == 0xC000013A) return false;   // STATUS_CONTROL_C_EXIT：用户关闭
            if (code == 0x40010004) return false;   // DBG_TERMINATE_PROCESS：调试器结束
            if (code == 0x40010005) return false;   // DBG_CONTROL_C
            return code >= 0x80000000;
        }

        /// <summary>Unity / 游戏崩溃处理器留在 Player.log 里的强特征。</summary>
        public static bool LooksLikeCrashLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (Contains(text, "Crash!!!")) return true;
            if (Contains(text, "OUTPUTTING STACK TRACE")) return true;
            if (Contains(text, "Obtained ") && Contains(text, " stack frames")) return true;
            if (Contains(text, "Receiving unhandled NULL exception")) return true;
            if (Contains(text, "caused an Access Violation")) return true;
            if (Contains(text, "This application has crashed")) return true;
            return false;
        }

        /// <summary>有没有"确定是崩溃"的证据。日志里有正常收尾时一律不算。</summary>
        public static bool HasCrashEvidence(int exitCode, bool werFound, bool dumpFound, string logText)
        {
            if (IsCleanLog(logText)) return false;
            if (werFound || dumpFound) return true;
            if (IsCrashExitCode(exitCode)) return true;
            return LooksLikeCrashLog(logText);
        }

        /// <summary>
        /// Alt+F4 能不能算"用户正常关闭"。
        /// 以下任一情况都不算：
        ///   · 按下时窗口已经无响应（玩家是在关一个卡死的游戏）；
        ///   · 按下后超过 AltF4HonorMs 才退出（按键并没有关掉它，多半是之后被强制结束）；
        ///   · 系统在按键之前就记录了卡死（游戏先卡死，玩家才按的 Alt+F4）。
        /// </summary>
        public static bool AltF4Counts(bool altF4, bool hungAtPress, DateTime pressedAt,
            DateTime exitAt, DateTime hangAt)
        {
            if (!altF4 || pressedAt == DateTime.MinValue) return false;
            if (hungAtPress) return false;

            double age = (exitAt - pressedAt).TotalMilliseconds;
            if (age < 0 || age > AltF4HonorMs) return false;

            if (hangAt != DateTime.MinValue && hangAt < pressedAt.AddSeconds(-1)) return false;
            return true;
        }

        /// <summary>
        /// 综合判断这次退出。altF4 由 GameCloseTracker 在退出前捕获；
        /// hang 表示系统事件日志里存在 AppHang 之类的卡死记录。
        /// </summary>
        public static GameExitKind Classify(int exitCode, bool werFound, bool dumpFound,
            bool altF4, bool hang, string logText)
        {
            // 控制台 quit 的收尾最明确：崩溃不可能先写 GOODBYE。
            if (IsCleanLog(logText)) return GameExitKind.Normal;

            // 崩溃证据优先：即使退出码看起来"正常"也按崩溃处理。
            if (HasCrashEvidence(exitCode, werFound, dumpFound, logText)) return GameExitKind.Crash;

            // 用户刚按过 Alt+F4：这是主动关闭。即使系统顺手记了一条
            // "关闭时短暂无响应"，也按正常退出处理（见最终说明里的取舍）。
            if (altF4) return GameExitKind.Normal;

            // 有卡死记录：不是崩溃，但也不是正常关闭，单独报"卡死"。
            if (hang) return GameExitKind.Abnormal;

            // 退出码 0：窗口 X / 菜单退出 / Steam 停止 / 关闭过程中被系统收尾。
            // 真正的崩溃不会以 0 退出；有证据的情况上面已经拦走了。
            if (unchecked((uint)exitCode) == 0) return GameExitKind.Normal;

            return GameExitKind.Abnormal;
        }

        private static bool Contains(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return false;
            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
