using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>
    /// 对局落盘：一局一个文件夹，meta.ini 存回合/角色/SCP/事件，samples.csv 存每秒采样。
    /// 纯文本，能手改；不依赖 JSON 库。
    /// </summary>
    internal static class MatchStore
    {
        public const string FolderName = "对局记录";

        public static string Root()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName);
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static string NewId(DateTime at)
        {
            return at.ToString("yyyyMMdd-HHmmss");
        }

        public static string Save(MatchRecord m)
        {
            if (m == null) return null;
            try
            {
                if (string.IsNullOrEmpty(m.Id)) m.Id = NewId(m.Start);
                string dir = Path.Combine(Root(), m.Id);
                Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder(8192);
                sb.AppendLine("[Match]");
                sb.AppendLine("schema=" + MatchRecord.Schema);
                sb.AppendLine("id=" + m.Id);
                sb.AppendLine("start=" + Fmt(m.Start));
                sb.AppendLine("end=" + (m.End == DateTime.MinValue ? "" : Fmt(m.End)));
                sb.AppendLine("game=" + Clean(m.GameTitle));
                sb.AppendLine("pid=" + m.Pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("version=" + Clean(m.ExeVersion));

                for (int r = 0; r < m.Rounds.Count; r++)
                {
                    MatchRound round = m.Rounds[r];
                    sb.AppendLine();
                    sb.AppendLine("[Round " + round.Index + "]");
                    sb.AppendLine("start=" + Fmt(round.Start));
                    sb.AppendLine("end=" + (round.HasEnd ? Fmt(round.End) : ""));

                    for (int i = 0; i < round.Roles.Count; i++)
                    {
                        MatchRoleSpan s = round.Roles[i];
                        sb.AppendLine("role=" + Fmt(s.Start) + "|" +
                            (s.End == DateTime.MaxValue ? "" : Fmt(s.End)) + "|" +
                            Clean(s.Role) + "|" + Clean(s.State) + "|" + Clean(s.Note));
                    }
                    for (int i = 0; i < round.Scps.Count; i++)
                    {
                        ScpPresence p = round.Scps[i];
                        sb.AppendLine("scp=" + Clean(p.Code) + "|" +
                            (p.FirstSeen == DateTime.MinValue ? "" : Fmt(p.FirstSeen)) + "|" +
                            (p.ContainedAt == DateTime.MinValue ? "" : Fmt(p.ContainedAt)) + "|" +
                            Clean(p.Source) + "|" + (p.WasPlayerRole ? "1" : "0") + "|" +
                            (p.Confirmed ? "1" : "0"));
                    }
                    for (int i = 0; i < round.Events.Count; i++)
                    {
                        MatchEvent e = round.Events[i];
                        sb.AppendLine("event=" + Fmt(e.At) + "|" + Clean(e.Kind) + "|" +
                            Clean(e.Label) + "|" + Clean(e.Source) + "|" +
                            (e.HasEnd ? Fmt(e.EndAt) : ""));
                    }
                }

                File.WriteAllText(Path.Combine(dir, "meta.ini"), sb.ToString(), new UTF8Encoding(false));
                WriteSamples(m, Path.Combine(dir, "samples.csv"));
                return dir;
            }
            catch { return null; }
        }

        private static void WriteSamples(MatchRecord m, string path)
        {
            StringBuilder sb = new StringBuilder(65536);
            sb.AppendLine("round,second,fps,cpu,gpu,temp,bpm,role,state");
            CultureInfo inv = CultureInfo.InvariantCulture;
            for (int r = 0; r < m.Rounds.Count; r++)
            {
                MatchRound round = m.Rounds[r];
                for (int i = 0; i < round.Samples.Count; i++)
                {
                    MatchSample s = round.Samples[i];
                    sb.Append(round.Index.ToString(inv)).Append(',');
                    sb.Append(s.Second.ToString(inv)).Append(',');
                    sb.Append(s.HasFps ? s.Fps.ToString("0.0", inv) : "").Append(',');
                    sb.Append(s.Cpu.ToString("0.0", inv)).Append(',');
                    sb.Append(s.Gpu.ToString("0.0", inv)).Append(',');
                    sb.Append(s.HasTemp ? s.Temp.ToString("0.0", inv) : "").Append(',');
                    sb.Append(s.Bpm > 0 ? s.Bpm.ToString(inv) : "").Append(',');
                    sb.Append(Clean(s.Role)).Append(',');
                    sb.Append(Clean(s.State));
                    sb.AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static MatchRecord Load(string idOrDir)
        {
            try
            {
                string dir = idOrDir;
                if (!Directory.Exists(dir)) dir = Path.Combine(Root(), idOrDir);
                if (!Directory.Exists(dir)) return null;
                string meta = Path.Combine(dir, "meta.ini");
                if (!File.Exists(meta)) return null;

                MatchRecord m = new MatchRecord();
                MatchRound current = null;
                string[] lines = File.ReadAllLines(meta);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '[')
                    {
                        if (line.StartsWith("[Round ", StringComparison.OrdinalIgnoreCase))
                        {
                            int idx = Int(line.Substring(7).TrimEnd(']'), m.Rounds.Count + 1);
                            current = new MatchRound();
                            current.Index = idx;
                            m.Rounds.Add(current);
                        }
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (current == null)
                    {
                        if (key == "id") m.Id = val;
                        else if (key == "start") m.Start = ParseDate(val);
                        else if (key == "end") m.End = ParseDate(val);
                        else if (key == "game") m.GameTitle = val;
                        else if (key == "pid") m.Pid = Int(val, 0);
                        else if (key == "version") m.ExeVersion = val;
                        continue;
                    }

                    if (key == "start") current.Start = ParseDate(val);
                    else if (key == "end") { DateTime e; if (DateTime.TryParse(val, out e)) current.End = e; }
                    else if (key == "role") ParseRole(current, val);
                    else if (key == "scp") ParseScp(current, val);
                    else if (key == "event") ParseEvent(current, val);
                }

                // 没有 meta 里的采样就补第二遍 CSV
                LoadSamples(m, Path.Combine(dir, "samples.csv"));
                return m;
            }
            catch { return null; }
        }

        private static void ParseRole(MatchRound round, string val)
        {
            string[] p = val.Split('|');
            if (p.Length < 3) return;
            MatchRoleSpan s = new MatchRoleSpan();
            s.Start = ParseDate(p[0]);
            s.End = string.IsNullOrEmpty(p[1]) ? DateTime.MaxValue : ParseDate(p[1]);
            s.Role = p[2];
            s.State = p.Length > 3 && p[3].Length > 0 ? p[3] : "alive";
            s.Note = p.Length > 4 ? p[4] : "";
            round.Roles.Add(s);
        }

        private static void ParseScp(MatchRound round, string val)
        {
            string[] p = val.Split('|');
            if (p.Length < 1 || p[0].Length == 0) return;
            ScpPresence s = new ScpPresence();
            s.Code = p[0];
            s.FirstSeen = p.Length > 1 ? ParseDate(p[1]) : DateTime.MinValue;
            s.ContainedAt = p.Length > 2 ? ParseDate(p[2]) : DateTime.MinValue;
            s.Source = p.Length > 3 ? p[3] : "";
            s.WasPlayerRole = p.Length > 4 && p[4] == "1";
            s.Confirmed = p.Length > 5 && p[5] == "1";
            if (s.Confirmed && s.FirstSeen == DateTime.MinValue) s.FirstSeen = round.Start;
            round.Scps.Add(s);
        }

        private static void ParseEvent(MatchRound round, string val)
        {
            string[] p = val.Split('|');
            if (p.Length < 3) return;
            MatchEvent e = new MatchEvent();
            e.At = ParseDate(p[0]);
            e.Kind = p[1];
            e.Label = p[2];
            e.Source = p.Length > 3 ? p[3] : "";
            if (p.Length > 4 && p[4].Length > 0) e.EndAt = ParseDate(p[4]);
            round.Events.Add(e);
        }

        private static void LoadSamples(MatchRecord m, string path)
        {
            if (!File.Exists(path)) return;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] p = lines[i].Split(',');
                if (p.Length < 9) continue;
                int idx = Int(p[0], -1);
                MatchRound round = null;
                for (int r = 0; r < m.Rounds.Count; r++) if (m.Rounds[r].Index == idx) { round = m.Rounds[r]; break; }
                if (round == null) continue;

                MatchSample s = new MatchSample();
                s.Second = Int(p[1], 0);
                s.At = round.Start.AddSeconds(s.Second);
                s.HasFps = p[2].Length > 0;
                if (s.HasFps) s.Fps = Dbl(p[2]);
                s.Cpu = Dbl(p[3]);
                s.Gpu = Dbl(p[4]);
                s.HasTemp = p[5].Length > 0;
                if (s.HasTemp) s.Temp = Dbl(p[5]);
                s.Bpm = Int(p[6], 0);
                s.Role = p[7];
                s.State = p[8];
                round.Samples.Add(s);
            }
        }

        /// <summary>列出最近的对局（按开始时间倒序）。</summary>
        public static List<MatchRecord> ListRecent(int max)
        {
            List<MatchRecord> list = new List<MatchRecord>();
            try
            {
                string root = Root();
                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (int i = dirs.Length - 1; i >= 0 && list.Count < max; i--)
                {
                    MatchRecord m = Load(dirs[i]);
                    if (m != null && m.Rounds.Count > 0) list.Add(m);
                }
            }
            catch { }
            return list;
        }

        /// <summary>按保留份数删最旧的（只删带 meta.ini 的对局目录）。</summary>
        public static void Prune(int keep)
        {
            try
            {
                string root = Root();
                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                int remove = dirs.Length - keep;
                for (int i = 0; i < remove; i++)
                {
                    if (!File.Exists(Path.Combine(dirs[i], "meta.ini"))) continue;
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
        }

        private static string Fmt(DateTime t) { return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

        private static DateTime ParseDate(string s)
        {
            DateTime t;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            return DateTime.MinValue;
        }

        private static int Int(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static double Dbl(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
