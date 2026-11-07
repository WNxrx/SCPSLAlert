using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>
    /// 一图流的“纠错注释”。不改原始对局数据，只记差异：
    /// 删掉哪些事件、改了哪些标签/角色名、隐藏哪些 SCP。
    /// 按 对局ID + 版本 另存：对局记录\注释\&lt;id&gt;.v1.txt
    /// </summary>
    internal sealed class MatchAnnotations
    {
        public const int Schema = 1;

        public string MatchId = "";
        public readonly HashSet<string> DeletedEvents = new HashSet<string>();
        public readonly Dictionary<string, string> EventLabels = new Dictionary<string, string>();
        public readonly Dictionary<string, string> RoleNames = new Dictionary<string, string>();
        public readonly HashSet<string> HiddenScps = new HashSet<string>();

        public bool IsEmpty
        {
            get
            {
                return DeletedEvents.Count == 0 && EventLabels.Count == 0
                    && RoleNames.Count == 0 && HiddenScps.Count == 0;
            }
        }

        public static string Dir()
        {
            string d = Path.Combine(MatchStore.Root(), "注释");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        public static string PathFor(string id)
        {
            return Path.Combine(Dir(), (id ?? "unknown") + ".v1.txt");
        }

        public static MatchAnnotations Load(string id)
        {
            MatchAnnotations a = new MatchAnnotations();
            a.MatchId = id ?? "";
            try
            {
                string path = PathFor(id);
                if (!File.Exists(path)) return a;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "event.delete") a.DeletedEvents.Add(v);
                    else if (k == "event.label") SetPair(a.EventLabels, v);
                    else if (k == "role.name") SetPair(a.RoleNames, v);
                    else if (k == "scp.hide") a.HiddenScps.Add(v);
                }
            }
            catch { }
            return a;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# SCPSLAlert one-map annotations v" + Schema);
                sb.AppendLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (string k in DeletedEvents) sb.AppendLine("event.delete=" + k);
                foreach (KeyValuePair<string, string> p in EventLabels)
                    sb.AppendLine("event.label=" + p.Key + "=" + Clean(p.Value));
                foreach (KeyValuePair<string, string> p in RoleNames)
                    sb.AppendLine("role.name=" + p.Key + "=" + Clean(p.Value));
                foreach (string k in HiddenScps) sb.AppendLine("scp.hide=" + k);
                File.WriteAllText(PathFor(MatchId), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>把注释应用到一份克隆上，原始 MatchRecord 不动。</summary>
        public MatchRecord Apply(MatchRecord src)
        {
            if (src == null) return null;
            MatchRecord m = src.Clone();
            for (int r = 0; r < m.Rounds.Count; r++)
            {
                MatchRound round = m.Rounds[r];
                string rk = round.Index.ToString();

                List<MatchEvent> keep = new List<MatchEvent>();
                for (int i = 0; i < round.Events.Count; i++)
                {
                    string key = rk + ":" + i;
                    if (DeletedEvents.Contains(key)) continue;
                    string label;
                    if (EventLabels.TryGetValue(key, out label) && label != null) round.Events[i].Label = label;
                    keep.Add(round.Events[i]);
                }
                round.Events.Clear();
                round.Events.AddRange(keep);

                for (int i = 0; i < round.Roles.Count; i++)
                {
                    string name;
                    if (RoleNames.TryGetValue(rk + ":" + i, out name) && name != null)
                        round.Roles[i].Role = name;
                }

                List<ScpPresence> scps = new List<ScpPresence>();
                for (int i = 0; i < round.Scps.Count; i++)
                {
                    if (HiddenScps.Contains(rk + ":" + round.Scps[i].Code)) continue;
                    scps.Add(round.Scps[i]);
                }
                round.Scps.Clear();
                round.Scps.AddRange(scps);
            }
            return m;
        }

        private static void SetPair(Dictionary<string, string> map, string v)
        {
            int eq = v.IndexOf('=');
            if (eq <= 0) return;
            map[v.Substring(0, eq)] = v.Substring(eq + 1);
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
