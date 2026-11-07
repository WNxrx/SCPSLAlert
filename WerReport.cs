using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class WerReport
    {
        public string Folder = "";
        public string FilePath = "";
        public string Module = "";
        public string Code = "";
        public string Offset = "";
        public readonly List<string> Modules = new List<string>();

        public static WerReport FindLatest(DateTime notBefore)
        {
            try
            {
                string[] roots = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "Windows", "WER", "Temp")
                };

                WerReport best = null;
                DateTime bestTime = DateTime.MinValue;
                for (int r = 0; r < roots.Length; r++)
                {
                    string[] dirs;
                    try
                    {
                        if (!Directory.Exists(roots[r])) continue;
                        dirs = Directory.GetDirectories(roots[r], "AppCrash_SCPSL.exe*");
                    }
                    catch { continue; }

                    for (int i = 0; i < dirs.Length; i++)
                    {
                        DateTime time;
                        try { time = Directory.GetLastWriteTime(dirs[i]); }
                        catch { continue; }
                        // 只认这次运行开始之后产生的报告，避免把上一局崩溃的
                        // 报告带进"刚退出"的判定里；留 2 秒给文件时间误差。
                        if (time < notBefore.AddSeconds(-2)) continue;
                        if (time <= bestTime) continue;
                        string file = Path.Combine(dirs[i], "Report.wer");
                        if (!File.Exists(file)) continue;
                        WerReport parsed = Parse(file);
                        if (parsed == null) continue;
                        parsed.Folder = dirs[i];
                        best = parsed;
                        bestTime = time;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        public static WerReport Parse(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string[] lines = File.ReadAllLines(path, Encoding.Unicode);
                WerReport report = new WerReport();
                report.FilePath = path;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("LoadedModule[", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string module = line.Substring(eq + 1).Trim();
                            if (module.Length > 0 && !report.Modules.Contains(module))
                                report.Modules.Add(module);
                        }
                        continue;
                    }
                    if (line.StartsWith("Sig[3].Value=", StringComparison.OrdinalIgnoreCase))
                        report.Module = line.Substring(line.IndexOf('=') + 1).Trim();
                    else if (line.StartsWith("Sig[6].Value=", StringComparison.OrdinalIgnoreCase))
                        report.Offset = line.Substring(line.IndexOf('=') + 1).Trim();
                    else if (line.StartsWith("Sig[7].Value=", StringComparison.OrdinalIgnoreCase))
                        report.Code = line.Substring(line.IndexOf('=') + 1).Trim();
                }
                return report;
            }
            catch { return null; }
        }
    }
}
