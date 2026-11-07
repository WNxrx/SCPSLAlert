using System;
using System.Text.RegularExpressions;

namespace SCPSLAlert
{
    internal enum LogEventKind
    {
        None,
        /// <summary>进入设施场景 = 准备阶段开始（真正的回合可能还没开始）。</summary>
        RoundPreparing,
        RoundStarted,
        RoundFinished,
        PlayerJoined,
        WarheadCountdown,
        WarheadCancelled,
        WarheadDetonated,
        DecontaminationStarted,
        ScpDeath,
        /// <summary>自己死了（客户端日志里才有，带死因）。</summary>
        LocalDeath,
        /// <summary>某个 SCP 掉线 / 补位（客户端广播里能看到）。</summary>
        ScpDisconnected
    }

    internal class LogEvent
    {
        public LogEventKind Kind = LogEventKind.None;
        public string Scp = "";
        public string Killer = "";
        public string Reason = "";
        public string Player = "";
        public string Message = "";

        /// <summary>附带秒数（目前用于"服务器开局长倒计时还剩几秒"）。</summary>
        public int Seconds = -1;
    }

    /// <summary>
    /// SCP:SL 服务端日志解析（不依赖界面，方便单独测试）。
    ///
    /// 真实日志行形如：
    ///   2026-07-16 12:16:13.726 +08:00 | Game Event          | Game logic     | Round has been started.
    ///   2026-07-16 12:28:46.920 +08:00 | Game Event          | Warhead        | Countdown started.
    ///   2026-07-16 12:31:05.120 +08:00 | Kill                | Class change   | Player_A (00000000000000000@steam), playing as SCP-079, has died. Specific death reason: SCP-207.
    ///   2026-07-16 12:35:42.480 +08:00 | Kill                | Class change   | Player_B (ID_Dummy), playing as SCP-096, has been killed by Player_A (00000000000000000@steam) playing as: 九尾狐 指挥官. ...
    /// </summary>
    internal static class LogParser
    {
        private static readonly Regex ScpNameRegex =
            new Regex(@"SCP\s*-\s*\d+(?:\s*-\s*\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>SCP:SL 会在角色名里插入 Unity 富文本颜色标签，先剥掉。</summary>
        private static readonly Regex RichTextRegex =
            new Regex(@"<[^<>]{1,64}>", RegexOptions.Compiled);

        /// <summary>
        /// 有些服务器广播里用的是方括号写法（[color=red]SCP-079[/color]已掉线），
        /// 不剥掉的话 “SCP-079” 和 “已掉线” 中间会隔着标签，匹配不上。
        /// </summary>
        private static readonly Regex SquareTagRegex = new Regex(
            @"\[/?(?:color|b|i|u|s|size|sub|sup)(?:=[^\]]*)?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>从 “…, playing as &lt;角色&gt;, has …” 中取阵亡者身份。</summary>
        private static readonly Regex RoleRegex =
            new Regex(@"playing as\s+(?<role>.+?)\s*,\s*has\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>“has been killed by 玩家名 (steamid) playing as: …” 中取击杀者名字。</summary>
        private static readonly Regex KillerRegex =
            new Regex(@"has been killed by\s+(?<killer>.+?)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>取死因原文。</summary>
        private static readonly Regex ReasonRegex =
            new Regex(@"Specific death reason:\s*(?<reason>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>“Nickname of &lt;steamid&gt; is now &lt;昵称&gt;.” —— 玩家连进来并拿到昵称。</summary>
        private static readonly Regex JoinNickRegex =
            new Regex(@"^Nickname of\s+\S+\s+is now\s+(?<name>.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>“&lt;steamid&gt;@steam authenticated from endpoint ...” —— 玩家通过验证连进来（拿不到昵称时的兜底）。</summary>
        private static readonly Regex JoinAuthRegex =
            new Regex(@"^(?<id>\S+@steam)\s+authenticated from endpoint", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ============================================================
        // 客户端 Player.log（%USERPROFILE%\AppData\LocalLow\Northwood\SCPSL\Player.log）
        //
        // 加入别人的服务器时，本地只有这一种日志。实测能稳定拿到的行：
        //   SceneLoaded 2. Facility                  → 进入设施场景（= 这一局开始了）
        //   SceneLoaded 1. NewMainMenu               → 回到主菜单（= 这局结束）
        //   [FROM SERVER] Hi 名字! You have been authenticated on this server.
        //   [FROM SERVER] You died. Reason: ...      → 自己阵亡
        //   [BROADCAST FROM SERVER] 回合结束，...     → 回合结束广播
        //   [BROADCAST FROM SERVER] ...SCP-079已掉线... / SCP-079补位成功
        //   [BROADCAST FROM SERVER] 大厅倒计时: [color=yellow]60秒[/color]
        // ============================================================

        /// <summary>客户端连上服务器并完成验证。</summary>
        private static readonly Regex ClientJoinRegex = new Regex(
            @"^\[FROM SERVER\]\s*Hi\s+(?<name>.+?)\s*!\s*You have been authenticated",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>自己阵亡，后面跟着死因。</summary>
        private static readonly Regex ClientDeathRegex = new Regex(
            @"^\[FROM SERVER\]\s*You died\.\s*Reason:\s*(?<reason>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Unity 场景切换。</summary>
        private static readonly Regex SceneRegex = new Regex(
            @"^SceneLoaded\s+\d+\.\s*(?<scene>[A-Za-z0-9_]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 服务器正在重开这一局（客户端会重新加载场景 / 重新认证）。
        ///
        /// 【操作者说明】他这台服"重启回合"其实就是**重进服务器**：会重新弹
        /// 「进入服务器」的提示、日志里重新出现 `Hi 名字! You have been authenticated`。
        /// 那种情况已经由 PlayerJoined / SceneLoaded 覆盖；这条正则留着做**第二路保险**
        ///（有些服只写这一行、不重新认证）。
        /// </summary>
        private static readonly Regex RoundRestartRegex = new Regex(
            @"full round restart|round restart|restarting the round|is restarting",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>服务器广播行，先把它剥出来。</summary>
        private static readonly Regex BroadcastRegex = new Regex(
            @"^\[BROADCAST FROM SERVER\]\s*(?<text>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 服务器自己的"开局长倒计时"（很多服有，形如「大厅倒计时: 15秒」倒数到 0秒）。
        ///
        /// 【为什么值得专门认】客户端日志里本来没有一个可靠的"回合正式开始"标志，
        /// 只能靠"进图 + 等几秒"去猜（RoundStartDelay）。而这个倒计时是服务器自己
        /// 明确在数"还有几秒开局"，数到 0 的那一刻就是**回合真正开始**。
        /// 实测这台服：15…1、0秒，紧接着就是玩家交互/击杀，可见 0 秒即开局。
        /// </summary>
        private static readonly Regex CountdownRegex = new Regex(
            @"(?<label>大厅|开局|回合|round|lobby)[^\d]{0,6}(?<sec>\d{1,3})\s*秒",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>广播里的 “SCP-079已掉线” / “SCP-079补位成功”。</summary>
        private static readonly Regex ScpDropRegex = new Regex(
            @"(?<scp>SCP\s*-\s*\d+)\s*(?:已掉线|掉线|补位成功|已补位|disconnected)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>服务端日志行：以日期开头并带 “ | ” 分隔。</summary>
        private static bool LooksLikeServerLog(string line)
        {
            if (line.Length < 20) return false;
            if (line.IndexOf('|') < 0) return false;
            // 形如 2026-07-16 12:31:10.563 +08:00 | ...
            return char.IsDigit(line[0]) && line[4] == '-' && line[7] == '-';
        }

        /// <summary>
        /// “回合正式开始”的关键字（来自 config.ini 的 RoundStartKeys，逗号分隔）。
        ///
        /// 为什么需要：客户端 Player.log 里其实**没有**一个可靠的“回合正式开始”
        /// 标志。`SceneLoaded 2. Facility` 只是把设施场景加载出来 ——
        /// 那时候往往还在准备阶段（可以跑动，但回合没开始）。
        /// 各服的“正式开始”提示都不一样（有的是广播“回合开始”，有的是别的），
        /// 所以做成可配置：命中任意一条就认为回合正式开始。
        /// 找不到标志时，还有一个“准备时长”兜底（见 Config.RoundStartDelay）。
        /// </summary>
        public static string[] StartKeys = new string[0];

        private static bool IsRoundStartLine(string line)
        {
            if (StartKeys == null || StartKeys.Length == 0) return false;
            for (int i = 0; i < StartKeys.Length; i++)
            {
                string k = StartKeys[i];
                if (string.IsNullOrEmpty(k)) continue;
                if (line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>解析客户端 Player.log 的一行；不是事件就返回 None。</summary>
        private static LogEvent ParseClientLog(string line)
        {
            LogEvent ev = new LogEvent();
            ev.Message = line;

            // ---- 服务器正在重开这一局（第二路保险：有的服只写这一行）----
            if (RoundRestartRegex.IsMatch(line))
            {
                ev.Kind = LogEventKind.RoundFinished;
                ev.Reason = "回合重启";
                return ev;
            }

            // ---- 场景切换：准备阶段 / 回到主菜单 ----
            Match sm = SceneRegex.Match(line);
            if (sm.Success)
            {
                string scene = sm.Groups["scene"].Value;
                if (string.Equals(scene, "Facility", StringComparison.OrdinalIgnoreCase))
                {
                    // 进入设施 = 准备阶段开始；真正的回合开始看关键字 / 准备时长
                    ev.Kind = LogEventKind.RoundPreparing;
                    return ev;
                }
                if (string.Equals(scene, "NewMainMenu", StringComparison.OrdinalIgnoreCase))
                {
                    ev.Kind = LogEventKind.RoundFinished;
                    return ev;
                }
                return ev;
            }

            // ---- “回合正式开始”关键字（各服不一样，可配置）----
            if (IsRoundStartLine(line))
            {
                ev.Kind = LogEventKind.RoundStarted;
                return ev;
            }

            // ---- 连上服务器 ----
            Match jm = ClientJoinRegex.Match(line);
            if (jm.Success)
            {
                ev.Kind = LogEventKind.PlayerJoined;
                ev.Player = CleanName(jm.Groups["name"].Value);
                return ev;
            }

            // ---- 自己阵亡 ----
            Match dm = ClientDeathRegex.Match(line);
            if (dm.Success)
            {
                ev.Kind = LogEventKind.LocalDeath;
                ev.Reason = StripUnityTags(dm.Groups["reason"].Value).Trim().TrimEnd('.', '。');
                return ev;
            }

            // ---- 服务器广播 ----
            Match bm = BroadcastRegex.Match(line);
            if (bm.Success)
            {
                string text = StripUnityTags(bm.Groups["text"].Value).Trim();

                if (text.IndexOf("回合结束", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("Round finished", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ev.Kind = LogEventKind.RoundFinished;
                    return ev;
                }

                // ---- 服务器自己的开局长倒计时：数到 0 就是回合正式开始 ----
                Match cm = CountdownRegex.Match(text);
                if (cm.Success)
                {
                    int left;
                    if (int.TryParse(cm.Groups["sec"].Value, out left) && left <= 300)
                    {
                        if (left > 0)
                        {
                            ev.Kind = LogEventKind.RoundPreparing;   // 准备中（不用再猜时长）
                            ev.Seconds = left;
                            return ev;
                        }
                        ev.Kind = LogEventKind.RoundStarted;         // 0 秒 = 开局
                        return ev;
                    }
                }

                Match drop = ScpDropRegex.Match(text);
                if (drop.Success)
                {
                    ev.Kind = LogEventKind.ScpDisconnected;
                    ev.Scp = ExtractScpName(drop.Groups["scp"].Value);
                    ev.Reason = text;
                    return ev;
                }
            }

            return ev;
        }

        /// <summary>把 &lt;color=...&gt; 和 [color=...] 这类标签统统去掉。</summary>
        private static string StripUnityTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = RichTextRegex.Replace(s, "");
            t = SquareTagRegex.Replace(t, "");
            return t.Trim();
        }

        /// <summary>去掉昵称里的富文本颜色标签和首尾空白。</summary>
        private static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = RichTextRegex.Replace(s, "").Trim();
            t = t.TrimEnd('.', '。');
            if (t.Length > 32) t = t.Substring(0, 32) + "…";
            return t;
        }

        public static LogEvent Parse(string line)
        {
            LogEvent ev = new LogEvent();
            if (string.IsNullOrEmpty(line)) return ev;

            // 两种日志混在一起进来，先按格式分流：
            //   · 服务端 Round*.txt —— “日期 | 类别 | 子类别 | 消息”
            //   · 客户端 Player.log —— Unity 的原样输出
            if (!LooksLikeServerLog(line)) return ParseClientLog(line);

            // 时间戳 | 类别 | 子类别 | 消息（消息本身可能还含有 |，例如 Round finished! ... | Chaos: 1）
            string[] parts = line.Split('|');
            string sub = parts.Length >= 4 ? parts[2].Trim() : "";
            string msg = parts.Length >= 4
                ? string.Join(" | ", parts, 3, parts.Length - 3).Trim()
                : line.Trim();
            ev.Message = msg;

            // 1) 回合开始 / 结束
            if (Has(msg, "Round has been started"))
            {
                ev.Kind = LogEventKind.RoundStarted;
                return ev;
            }
            if (Has(msg, "Round finished") || Has(msg, "Round has been ended") || Has(msg, "Round has ended"))
            {
                ev.Kind = LogEventKind.RoundFinished;
                return ev;
            }

            // 1.5) 有玩家连进服务器
            //      "Nickname of 00000000000000000@steam is now Player_B."
            //      "00000000000000000@steam authenticated from endpoint (null). Player ID assigned: 30. ..."
            Match jm = JoinNickRegex.Match(msg);
            if (jm.Success)
            {
                ev.Kind = LogEventKind.PlayerJoined;
                ev.Player = CleanName(jm.Groups["name"].Value);
                return ev;
            }
            jm = JoinAuthRegex.Match(msg);
            if (jm.Success)
            {
                // 紧接着一定会有一行 “Nickname of ... is now 昵称.”，
                // 那一行才拿得到真正的名字。这里不再另外报一次，
                // 否则同一次进服会弹两条提示，而且第一条只有一串 steamid。
                return ev;
            }

            // 2) 核弹：只认 Warhead 子类别，避免远程管理（Remote Admin）日志误触发
            if (string.Equals(sub, "Warhead", StringComparison.OrdinalIgnoreCase))
            {
                if (Has(msg, "Countdown started"))
                {
                    ev.Kind = LogEventKind.WarheadCountdown;
                    return ev;
                }
                if (Has(msg, "Detonation cancelled"))
                {
                    ev.Kind = LogEventKind.WarheadCancelled;
                    return ev;
                }
                if (Has(msg, "Warhead detonated"))
                {
                    ev.Kind = LogEventKind.WarheadDetonated;
                    return ev;
                }
            }

            // 3) 轻收容净化
            if (Has(msg, "Decontamination started") || Has(msg, "Decontamination has begun"))
            {
                ev.Kind = LogEventKind.DecontaminationStarted;
                return ev;
            }

            // 4) SCP 阵亡
            if (Has(msg, "Specific death reason") && Has(msg, "playing as"))
            {
                ParseDeath(msg, ev);
                return ev;
            }

            return ev;
        }

        /// <summary>
        /// 只有“阵亡者本人的角色”是 SCP 时才算 SCP 阵亡。
        /// 死因里出现 SCP 名字（例如 “Died to SCP-1344”“SCP-207”）不算。
        /// </summary>
        private static void ParseDeath(string msg, LogEvent ev)
        {
            string clean = RichTextRegex.Replace(msg, "");

            Match rm = RoleRegex.Match(clean);
            if (!rm.Success) return;
            string role = rm.Groups["role"].Value.Trim();

            int idx = role.IndexOf("SCP-", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            ev.Kind = LogEventKind.ScpDeath;
            ev.Scp = ExtractScpName(role.Substring(idx));
            ev.Killer = ExtractKiller(clean);
            ev.Reason = ExtractReason(clean);
        }

        private static string ExtractScpName(string s)
        {
            Match m = ScpNameRegex.Match(s);
            if (m.Success) return m.Value.Replace(" ", "");
            return s.Trim();
        }

        private static string ExtractKiller(string clean)
        {
            Match m = KillerRegex.Match(clean);
            if (m.Success)
            {
                string k = m.Groups["killer"].Value.Trim().Trim('.', '。', ',', '，', ' ');
                if (k.Length > 0) return k;
            }
            return "未知";
        }

        private static string ExtractReason(string clean)
        {
            Match m = ReasonRegex.Match(clean);
            if (m.Success) return m.Groups["reason"].Value.Trim().TrimEnd('.', '。');
            return "";
        }

        private static bool Has(string s, string sub)
        {
            return s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
