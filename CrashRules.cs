using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class CrashRule
    {
        public string Pattern = "";
        public string Level = "中";
        public string Title = "";
        public string Advice = "";
        public string Url = "";

        public bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text) || Pattern.Length == 0) return false;
            return text.IndexOf(Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class CrashRules
    {
        public static readonly List<CrashRule> Modules = new List<CrashRule>();
        public static readonly List<CrashRule> Logs = new List<CrashRule>();
        public static readonly List<string> Whitelist = new List<string>();
        private static bool _loaded;

        public static string FilePath
        {
            get { return Path.Combine(CrashPaths.Root, "crash-rules.ini"); }
        }

        public static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) WriteDefaults();
                if (File.Exists(FilePath)) Load();
                else ParseLines(DefaultsText().Replace("\r", "").Split('\n'));
            }
            catch { }
        }

        public static void Reload()
        {
            Modules.Clear();
            Logs.Clear();
            Whitelist.Clear();
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) WriteDefaults();
                if (File.Exists(FilePath)) Load();
                else ParseLines(DefaultsText().Replace("\r", "").Split('\n'));
            }
            catch { }
        }

        public static bool IsWhitelisted(string path)
        {
            if (Whitelist.Count == 0) return false;
            for (int i = 0; i < Whitelist.Count; i++)
            {
                if (path.IndexOf(Whitelist[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static CrashRule MatchModule(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            for (int i = 0; i < Modules.Count; i++)
            {
                if (Modules[i].Matches(path)) return Modules[i];
            }
            return null;
        }

        private static readonly string[] IgnoredLogRules = new string[]
        {
            "kGfxThreadingModeSplitJobs",
            // Player.log 的模块加载列表里一定有 SL-AC.dll；单看 "SL-AC"
            // 会把所有崩溃都误判成反作弊。反作弊崩溃由故障模块/调用栈判断。
            "SL-AC"
        };

        public static List<CrashRule> MatchLogs(string text)
        {
            List<CrashRule> hits = new List<CrashRule>();
            if (string.IsNullOrEmpty(text)) return hits;
            for (int i = 0; i < Logs.Count; i++)
            {
                if (IsIgnored(Logs[i].Pattern)) continue;
                if (Logs[i].Matches(text) && !hits.Contains(Logs[i])) hits.Add(Logs[i]);
            }
            return hits;
        }

        private static bool IsIgnored(string pattern)
        {
            for (int i = 0; i < IgnoredLogRules.Length; i++)
            {
                if (string.Equals(pattern, IgnoredLogRules[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void Load()
        {
            ParseLines(File.ReadAllLines(FilePath, Encoding.UTF8));
        }

        private static void ParseLines(string[] lines)
        {
            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    section = end > 1 ? line.Substring(1, end - 1).Trim() : "";
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (section.Equals("Whitelist", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Length > 0) Whitelist.Add(key);
                    continue;
                }
                CrashRule rule = ParseRule(key, value);
                if (rule == null) continue;
                if (section.Equals("Modules", StringComparison.OrdinalIgnoreCase)) Modules.Add(rule);
                else if (section.Equals("Logs", StringComparison.OrdinalIgnoreCase)) Logs.Add(rule);
            }
        }

        private static CrashRule ParseRule(string key, string value)
        {
            if (key.Length == 0) return null;
            string[] parts = value.Split('|');
            CrashRule rule = new CrashRule();
            rule.Pattern = key;
            if (parts.Length > 0 && parts[0].Trim().Length > 0) rule.Level = parts[0].Trim();
            if (parts.Length > 1) rule.Title = parts[1].Trim();
            if (parts.Length > 2) rule.Advice = parts[2].Trim();
            if (parts.Length > 3) rule.Url = parts[3].Trim();
            if (rule.Title.Length == 0) rule.Title = key;
            return rule;
        }

        private static void WriteDefaults()
        {
            File.WriteAllText(FilePath, DefaultsText(), new UTF8Encoding(true));
        }

        private static string DefaultsText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; SCPSLAlert 崩溃判断规则");
            sb.AppendLine("; 格式：匹配关键字 = 置信度|标题|建议|链接(可选)");
            sb.AppendLine("; 关键字不区分大小写，模块按文件名匹配，日志按整段文本匹配");
            sb.AppendLine("; 改完立即生效，不需要重新编译程序");
            sb.AppendLine();
            sb.AppendLine("[Whitelist]");
            sb.AppendLine("SCP Secret Laboratory");
            sb.AppendLine("GameAssembly");
            sb.AppendLine("UnityPlayer");
            sb.AppendLine("SL-AC");
            sb.AppendLine("steam_api64");
            sb.AppendLine("steamclient");
            sb.AppendLine("discord-rpc");
            sb.AppendLine("GpuDriver");
            sb.AppendLine("libcaress");
            sb.AppendLine("lib_burst");
            sb.AppendLine("librnnoise");
            sb.AppendLine("libopus");
            sb.AppendLine("baselib");
            sb.AppendLine();
            sb.AppendLine("[Modules]");
            sb.AppendLine("RTSSHooks=高|RivaTuner / RTSS 覆盖层|关闭 RivaTuner 与 MSI Afterburner 后重新启动游戏");
            sb.AppendLine("DiscordHook=中|Discord 活动状态|在 Discord 设置里关闭“游戏活动状态”，或退出 Discord");
            sb.AppendLine("Nahimic=中|Nahimic 音效增强|关闭 Nahimic 音效或卸载该驱动后重试");
            sb.AppendLine("Overwolf=中|Overwolf 覆盖层|退出 Overwolf 及其插件后重试");
            sb.AppendLine("AsusGameFirst=中|华硕 GameFirst 网络优化|关闭 Armoury Crate 的 GameFirst 功能");
            sb.AppendLine("AsusOSD=中|华硕 OSD|关闭 Armoury Crate 的屏幕显示功能");
            sb.AppendLine("Razer=低|雷蛇 Synapse|退出 Synapse 后重试");
            sb.AppendLine("LGHUB=低|罗技 G HUB|退出 G HUB 后重试");
            sb.AppendLine("ReShade=中|ReShade 画质注入|移除 ReShade 注入后重试");
            sb.AppendLine("SpecialK=中|Special K 注入|移除 Special K 后重试");
            sb.AppendLine("graphics-hook64=中|OBS 游戏捕获注入|关闭 OBS 的游戏捕获再启动游戏");
            sb.AppendLine("wallpaper64=低|Wallpaper Engine|退出 Wallpaper Engine 后重试");
            sb.AppendLine("Voicemod=中|Voicemod 语音驱动|退出 Voicemod 后重试");
            sb.AppendLine("Bandicam=低|Bandicam 录屏|关闭录屏后重试");
            sb.AppendLine();
            sb.AppendLine("[Logs]");
            sb.AppendLine("VirtualAlloc remapping failed=高|内存分配失败|关闭占用内存的后台程序，检查虚拟内存设置");
            sb.AppendLine("DXGI_ERROR_DEVICE_REMOVED=中|显卡设备丢失|更新显卡驱动，关闭超频");
            sb.AppendLine("device removed=中|显卡设备丢失|更新显卡驱动，关闭超频");
            sb.AppendLine("integrity couldn't be verified=高|游戏文件校验失败|在 Steam 里验证游戏文件完整性");
            sb.AppendLine("out of memory=中|内存不足|关闭后台程序，检查虚拟内存");
            sb.AppendLine("; 不要只写 SL-AC：Player.log 的模块列表里一定有 SL-AC.dll，会误报。");
            sb.AppendLine("; 反作弊崩溃由故障模块/调用栈判断；要加就写具体报错原文。");
            return sb.ToString();
        }
    }
}
