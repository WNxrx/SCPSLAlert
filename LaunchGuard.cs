using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SCPSLAlert
{
    internal sealed class LaunchHit
    {
        public int Pid;
        public string Name = "";
        public string Path = "";
        public string Reason = "";
        public bool Service;
    }

    internal static class LaunchGuard
    {
        private sealed class Rule
        {
            public string Pattern;
            public string Reason;
        }

        private static readonly Rule[] Rules = new Rule[]
        {
            RuleOf("cheatengine", "内存修改工具"),
            RuleOf("cheat engine", "内存修改工具"),
            RuleOf("ida64", "反汇编 / 逆向工具"),
            RuleOf("idaq64", "反汇编 / 逆向工具"),
            RuleOf("ida", "反汇编 / 逆向工具"),
            RuleOf("x64dbg", "调试器"),
            RuleOf("x32dbg", "调试器"),
            RuleOf("ollydbg", "调试器"),
            RuleOf("windbg", "调试器"),
            RuleOf("dnspy", "反编译工具"),
            RuleOf("ilspy", "反编译工具"),
            RuleOf("ghidra", "逆向工具"),
            RuleOf("radare2", "逆向工具"),
            RuleOf("scylla", "逆向工具"),
            RuleOf("lordpe", "逆向工具"),
            RuleOf("reclass", "内存查看工具"),
            RuleOf("processhacker", "进程 / 内核查看工具"),
            RuleOf("systeminformer", "进程 / 内核查看工具"),
            RuleOf("procexp", "进程查看工具"),
            RuleOf("procmon", "进程监视工具"),
            RuleOf("injector", "注入器"),
            RuleOf("artmoney", "内存修改工具"),
            RuleOf("wemod", "训练器"),
            RuleOf("frida", "注入 / 调试框架"),
            RuleOf("memedit", "内存修改工具"),
            RuleOf("memoryhacker", "内存修改工具")
        };

        private static Rule RuleOf(string pattern, string reason)
        {
            Rule rule = new Rule();
            rule.Pattern = pattern;
            rule.Reason = reason;
            return rule;
        }

        public static void ScanAsync(Action<List<LaunchHit>> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LaunchHit> hits = null;
                try { hits = Scan(); }
                catch { hits = new List<LaunchHit>(); }
                if (done != null)
                {
                    try { done(hits); } catch { }
                }
            });
        }

        public static List<LaunchHit> Scan()
        {
            List<LaunchHit> hits = new List<LaunchHit>();
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                for (int i = 0; i < all.Length; i++)
                {
                    string name;
                    try { name = all[i].ProcessName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;

                    string lower = name.ToLowerInvariant();
                    Rule hit = Match(lower);
                    if (hit == null) continue;

                    LaunchHit item = new LaunchHit();
                    item.Pid = all[i].Id;
                    item.Name = name + ".exe";
                    item.Reason = hit.Reason;
                    try { item.Path = all[i].MainModule.FileName; }
                    catch { item.Path = ""; }
                    hits.Add(item);
                }
            }
            catch { }
            finally
            {
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        try { all[i].Dispose(); } catch { }
                    }
                }
            }

            ScanServices(hits);

            return hits;
        }

        private static void ScanServices(List<LaunchHit> hits)
        {
            string[] patterns = new string[] { "kprocesshacker", "processhacker" };
            try
            {
                using (RegistryKey services = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services"))
                {
                    if (services == null) return;
                    string[] names = services.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string lower = names[i].ToLowerInvariant();
                        bool match = false;
                        for (int k = 0; k < patterns.Length; k++)
                        {
                            if (lower.IndexOf(patterns[k], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                match = true;
                                break;
                            }
                        }
                        if (!match) continue;

                        LaunchHit service = new LaunchHit();
                        service.Name = names[i];
                        service.Reason = "内核驱动服务（需要手动删除）";
                        service.Service = true;
                        hits.Add(service);
                    }
                }
            }
            catch { }
        }

        private static Rule Match(string processName)
        {
            for (int i = 0; i < Rules.Length; i++)
            {
                if (processName.IndexOf(Rules[i].Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Rules[i];
            }
            return null;
        }

        public static string Kill(List<LaunchHit> hits)
        {
            StringBuilder failed = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].Service) continue;
                try
                {
                    Process p = Process.GetProcessById(hits[i].Pid);
                    p.Kill();
                    p.WaitForExit(3000);
                    p.Dispose();
                }
                catch
                {
                    if (failed.Length > 0) failed.Append("、");
                    failed.Append(hits[i].Name);
                }
            }
            return failed.ToString();
        }
    }
}
