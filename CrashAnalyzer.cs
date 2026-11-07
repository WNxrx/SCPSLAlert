using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    internal static class CrashAnalyzer
    {
        public static CrashReport Analyze(int exitCode, string module, string code, string offset,
            string[] logTail, TimeSpan duration, string dir)
        {
            return Analyze(exitCode, module, code, offset, logTail, duration, dir, null, null, null);
        }

        public static CrashReport Analyze(int exitCode, string module, string code, string offset,
            string[] logTail, TimeSpan duration, string dir, List<string> loadedModules)
        {
            return Analyze(exitCode, module, code, offset, logTail, duration, dir, loadedModules, null, null);
        }

        public static CrashReport Analyze(int exitCode, string module, string code, string offset,
            string[] logTail, TimeSpan duration, string dir, List<string> loadedModules,
            List<string> stackModules, List<string> stackLines)
        {
            CrashReport r = new CrashReport();
            r.Kind = "游戏";
            r.Dir = dir;
            r.Time = DateTime.Now;
            r.Title = "游戏异常退出";
            r.Summary = "SCP: Secret Laboratory 结束运行，退出码 " + Hex(exitCode) + "。";

            if (!string.IsNullOrEmpty(code))
                r.Meta.Add("异常  " + code);
            else
                r.Meta.Add("退出码  " + Hex(exitCode) + " (" + exitCode + ")");
            // WER 有时拿不到故障模块（module 为空），但转储/调用栈里已经有真实帧。
            // 先取调用栈最内层的非系统模块，后面的分支才能按真实故障模块判断。
            string faultModule = module;
            bool moduleFromStack = false;
            if ((string.IsNullOrEmpty(faultModule) || IsSystem(faultModule))
                && stackModules != null && stackModules.Count > 0)
            {
                string fromStack = FirstMeaningful(stackModules);
                if (!string.IsNullOrEmpty(fromStack)
                    && !string.Equals(fromStack, faultModule, StringComparison.OrdinalIgnoreCase))
                {
                    faultModule = fromStack;
                    moduleFromStack = true;
                }
            }

            if (moduleFromStack)
                r.Meta.Add("模块  " + faultModule + "（来自调用栈）");
            else if (!string.IsNullOrEmpty(module))
                r.Meta.Add("模块  " + module + (string.IsNullOrEmpty(offset) ? "" : " +" + offset));
            else
                r.Meta.Add("模块  没有系统崩溃记录");
            r.Meta.Add("运行  " + HumanDuration(duration));
            r.Meta.Add("日志  " + (HasLog(dir) ? "Player.log 已保存" : "没有可用的 Unity 日志"));

            uint value = unchecked((uint)exitCode);
            if (!string.IsNullOrEmpty(code) && code.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    value = uint.Parse(code.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                catch { }
            }

            string text = Join(logTail);

            // 不能拿整段日志找 "SL-AC"：Player.log 的模块加载列表本来就包含
            // SL-AC.dll，那样每次都会误报成反作弊崩溃。只认真正的故障模块。
            bool slac = Contains(faultModule, "SL-AC");
            bool virtualAlloc = Contains(text, "VirtualAlloc") || Contains(text, "remapping failed");
            bool device = Contains(text, "device removed") || Contains(text, "DXGI_ERROR");
            bool memory = Contains(text, "out of memory") || Contains(text, "OutOfMemory");

            if (value == 0xC0000409)
            {
                Add(r, "高", "进程被系统快速终止（BEX64）",
                    "0xC0000409 不经过游戏异常处理，常见于反作弊、第三方注入或原生栈损坏。");
                if (slac)
                    Add(r, "中", "反作弊模块参与了这次退出", "故障模块是 SL-AC。");
                r.Suggestions.Add("关闭 Steam 游戏内覆盖、Discord 活动状态和录屏工具后重试");
                r.Suggestions.Add("在 Steam 里验证游戏文件完整性");
                r.Suggestions.Add("仍然复现就导出报告，附上时间和异常签名");
            }
            else if (value == 0xC0000005)
            {
                if (Contains(faultModule, "UnityPlayer"))
                {
                    Add(r, "高", "Unity 引擎访问了非法内存", "故障模块是 UnityPlayer.dll。");
                    r.Suggestions.Add("更新或清洁安装显卡驱动");
                    r.Suggestions.Add("关闭第三方覆盖层和超频工具");
                }
                else if (Contains(faultModule, "GameAssembly"))
                {
                    Add(r, "高", "游戏逻辑层崩溃", "故障模块是 GameAssembly.dll（IL2CPP）。");
                    r.Suggestions.Add("验证游戏文件完整性");
                    r.Suggestions.Add("清理 AppData\\Roaming\\SCP Secret Laboratory 下的缓存后重试");
                }
                else if (slac)
                {
                    Add(r, "高", "反作弊模块访问冲突", "故障模块是 SL-AC.dll。");
                    r.Suggestions.Add("检查网络与 DNS，尝试用 --doh 启动参数");
                    r.Suggestions.Add("确认系统时间和时区正确");
                }
                else
                {
                    Add(r, "中", "访问冲突", "有模块读写了非法地址，具体模块见上方。");
                    r.Suggestions.Add("关闭覆盖层与后台注入类软件后重试");
                }
            }
            else if (value == 0xC000001D)
            {
                Add(r, "高", "CPU 不支持游戏需要的指令", "非法指令，通常是 AVX 一类指令集缺失。");
                r.Suggestions.Add("确认 CPU 支持 AVX2；老平台无法运行新版本");
            }
            else if (value == 0xC0000135 || value == 0xC0000139)
            {
                Add(r, "高", "缺少运行库或 DLL", "加载阶段找不到依赖。");
                r.Suggestions.Add("安装最新的 Visual C++ 运行库");
                r.Suggestions.Add("验证游戏文件完整性");
            }
            // 退出码 0 / -1 本身没有诊断价值：SCP:SL 正常退出就是 -1，正常退出时
            // Player.log 也常常没有 ">>> quit" / "GOODBYE" 收尾。所以这里不再凭退出码
            // 推断"被外部结束"或"游戏主动退出"——那种结论以前会把普通退出报成崩溃。
            // 是否崩溃已经在 GameCrashWatcher 里用 WER / 转储 / 异常码 / 崩溃日志判定过。

            if (virtualAlloc)
                Add(r, "中", "内存分配失败", "日志里出现 VirtualAlloc remapping failed，地址空间或内存不足。");
            if (device)
                Add(r, "中", "显卡设备丢失", "日志里出现 device removed 一类错误。");
            if (memory)
                Add(r, "中", "内存不足", "日志里出现内存分配失败。");

            if (r.Causes.Count == 0)
            {
                Add(r, "低", "没有匹配到已知原因", "只能确认进程异常结束，需要转储才能进一步定位。");
                r.Suggestions.Add("如果反复出现，导出报告并附上最近一次的操作");
            }

            if (virtualAlloc || memory)
                r.Suggestions.Add("关闭占用内存的后台程序，检查虚拟内存设置");
            if (device)
                r.Suggestions.Add("更新显卡驱动，或把游戏切到无边框窗口模式");

            ApplyRules(r, module, text, loadedModules);
            ApplyStack(r, stackModules, stackLines);

            SortCauses(r);
            if (r.Causes.Count > 3) r.Causes.RemoveRange(3, r.Causes.Count - 3);
            r.Summary = r.Causes[0].Title + "（" + Hex(exitCode) + "）";
            return r;
        }

        private static void ApplyRules(CrashReport r, string faultModule, string logText, List<string> loadedModules)
        {
            try
            {
                CrashRules.Ensure();

                if (!string.IsNullOrEmpty(faultModule))
                {
                    CrashRule hit = CrashRules.MatchModule(faultModule);
                    if (hit != null) AddRule(r, hit, true);
                }

                if (loadedModules != null)
                {
                    for (int i = 0; i < loadedModules.Count; i++)
                    {
                        string path = loadedModules[i];
                        if (string.IsNullOrEmpty(path)) continue;
                        if (CrashRules.IsWhitelisted(path)) continue;
                        CrashRule rule = CrashRules.MatchModule(path);
                        if (rule == null) continue;
                        AddRule(r, rule, false);
                    }
                }

                List<CrashRule> logs = CrashRules.MatchLogs(logText);
                for (int i = 0; i < logs.Count; i++) AddRule(r, logs[i], false);
            }
            catch { }
        }

        private static void ApplyStack(CrashReport r, List<string> stackModules, List<string> stackLines)
        {
            try
            {
                if (stackLines != null && stackLines.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < stackLines.Count && i < 16; i++)
                        sb.AppendLine("#" + i + "  " + stackLines[i]);
                    r.StackText = sb.ToString();
                    r.Meta.Add("栈  " + stackLines.Count + " 帧（近似）");
                }

                if (stackModules == null || stackModules.Count == 0) return;

                string inner = FirstMeaningful(stackModules);
                if (inner.Length == 0) return;

                if (Contains(inner, "SL-AC"))
                {
                    Add(r, "高", "反作弊模块在调用栈上", "调用栈最内层落在 SL-AC，先检查网络与反作弊连接。");
                    r.Suggestions.Insert(0, "检查网络与 DNS，尝试用 --doh 启动参数");
                    return;
                }

                if (IsRender(inner))
                {
                    Add(r, "中", "崩溃发生在渲染路径", "调用栈最内层落在 " + inner + "，通常是显卡驱动或覆盖层。");
                    r.Suggestions.Insert(0, "更新显卡驱动，或把游戏改成无边框窗口模式");
                    return;
                }

                if (Contains(inner, "GameAssembly"))
                {
                    Add(r, "中", "崩溃发生在游戏逻辑", "调用栈最内层落在 GameAssembly，多为游戏自身或模组问题。");
                    r.Suggestions.Insert(0, "验证游戏文件完整性，移除第三方模组后重试");
                    return;
                }

                CrashRule rule = CrashRules.MatchModule(inner);
                if (rule != null)
                {
                    AddRule(r, rule, true);
                    return;
                }

                Add(r, "低", "崩溃位置：" + inner, "调用栈最内层落在 " + inner + "。");
            }
            catch { }
        }

        private static string FirstMeaningful(List<string> modules)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                string name = modules[i];
                if (string.IsNullOrEmpty(name)) continue;
                if (IsSystem(name)) continue;
                return name;
            }
            return "";
        }

        private static bool IsSystem(string name)
        {
            string[] system = new string[]
            {
                "ntdll", "kernel32", "kernelbase", "win32u", "user32", "gdi32",
                "combase", "sechost", "msvcrt", "ucrtbase", "rpcrt4", "ole32"
            };
            for (int i = 0; i < system.Length; i++)
            {
                if (string.Equals(name, system[i] + ".dll", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsRender(string name)
        {
            string[] render = new string[]
            {
                "UnityPlayer", "d3d11", "dxgi", "nvlddmkm", "nvoglv", "atidxx",
                "amdvlk", "vulkan", "GpuDriver", "d3d12"
            };
            for (int i = 0; i < render.Length; i++)
            {
                if (Contains(name, render[i])) return true;
            }
            return false;
        }

        private static void AddRule(CrashReport r, CrashRule rule, bool faulting)
        {
            if (rule == null) return;
            for (int i = 0; i < r.Causes.Count; i++)
            {
                if (string.Equals(r.Causes[i].Title, rule.Title, StringComparison.Ordinal)) return;
            }
            CrashCause c = new CrashCause();
            c.Level = faulting ? "高" : rule.Level;
            c.Title = rule.Title;
            c.Detail = rule.Advice;
            if (faulting) c.Detail = "崩溃就发生在这个模块。" + rule.Advice;
            r.Causes.Add(c);

            if (rule.Advice.Length > 0 && !r.Suggestions.Contains(rule.Advice))
                r.Suggestions.Insert(0, rule.Advice);
            if (rule.Url.Length > 0 && !r.Suggestions.Contains(rule.Url))
                r.Suggestions.Add(rule.Url);
        }

        private static void SortCauses(CrashReport r)
        {
            r.Causes.Sort(delegate(CrashCause a, CrashCause b)
            {
                return Rank(a.Level).CompareTo(Rank(b.Level));
            });
        }

        private static int Rank(string level)
        {
            if (level == "高") return 0;
            if (level == "中") return 1;
            return 2;
        }


        private static void Add(CrashReport r, string level, string title, string detail)
        {
            CrashCause c = new CrashCause();
            c.Level = level;
            c.Title = title;
            c.Detail = detail;
            r.Causes.Add(c);
        }

        private static bool HasLog(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Player.log")); }
            catch { return false; }
        }

        private static bool Contains(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return false;
            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Join(string[] lines)
        {
            if (lines == null || lines.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++) sb.AppendLine(lines[i]);
            return sb.ToString();
        }

        public static string Hex(int value)
        {
            return "0x" + ((uint)value).ToString("X8", CultureInfo.InvariantCulture);
        }

        public static string HumanDuration(TimeSpan span)
        {
            if (span.TotalSeconds < 1) return "不到 1 秒";
            if (span.TotalMinutes < 1) return ((int)span.TotalSeconds) + " 秒";
            if (span.TotalHours < 1) return ((int)span.TotalMinutes) + " 分 " + span.Seconds + " 秒";
            return ((int)span.TotalHours) + " 小时 " + span.Minutes + " 分";
        }
    }
}
