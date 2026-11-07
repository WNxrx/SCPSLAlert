using System;
using System.Collections.Generic;

namespace SCPSLAlert
{
    /// <summary>一图流的一秒采样。性能轨 + 生理轨共用一条。</summary>
    internal sealed class MatchSample
    {
        public DateTime At;
        public int Second;          // 距回合开始
        public bool HasFps;
        public double Fps;
        public double Cpu;
        public double Gpu;
        public bool HasTemp;
        public double Temp;
        public int Bpm;             // 0 = 没设备 / 没信号
        public string Role = "";
        public string State = "alive";
    }

    /// <summary>时间轴上的一个事件标记。Source 记来源，便于导出 txt 解读。</summary>
    internal sealed class MatchEvent
    {
        public DateTime At;
        public string Kind = "";     // wave / warhead / deadman / decon / gen / death / respawn / role / scp / round
        public string Label = "";
        public string Source = "";   // log / ocr / manual / hotkey / faction
        public DateTime EndAt = DateTime.MinValue;
        public bool HasEnd { get { return EndAt != DateTime.MinValue && EndAt > At; } }
    }

    /// <summary>本回合某个 SCP 的在场情况。FirstSeen 是第一次被确认的时间。</summary>
    internal sealed class ScpPresence
    {
        public string Code = "";
        public DateTime FirstSeen = DateTime.MinValue;
        public bool Confirmed;                 // 扫描/播报确认在场
        public DateTime ContainedAt = DateTime.MinValue;  // 被收容 / 阵亡
        public string Source = "";             // scan / log / manual
        public bool WasPlayerRole;             // 本机这一局扮演过它
    }

    /// <summary>回合内的角色时间段（从 RoleSegment 拷贝一份，避免数据层依赖 UI/识别模块）。</summary>
    internal sealed class MatchRoleSpan
    {
        public DateTime Start;
        public DateTime End = DateTime.MaxValue;
        public string Role = "";
        public string State = "alive";
        public string Note = "";

        public double Seconds(DateTime now)
        {
            DateTime e = End == DateTime.MaxValue ? now : End;
            return Math.Max(0, (e - Start).TotalSeconds);
        }
    }

    /// <summary>一回合的数据。</summary>
    internal sealed class MatchRound
    {
        public int Index;
        public DateTime Start;
        public DateTime End = DateTime.MinValue;
        public readonly List<MatchRoleSpan> Roles = new List<MatchRoleSpan>();
        public readonly List<ScpPresence> Scps = new List<ScpPresence>();
        public readonly List<MatchEvent> Events = new List<MatchEvent>();
        public readonly List<MatchSample> Samples = new List<MatchSample>(4096);

        public bool HasEnd { get { return End != DateTime.MinValue; } }

        public DateTime EffectiveEnd
        {
            get
            {
                if (HasEnd) return End;
                if (Samples.Count > 0) return Samples[Samples.Count - 1].At;
                return Start;
            }
        }

        public double Seconds { get { return Math.Max(0, (EffectiveEnd - Start).TotalSeconds); } }

        public ScpPresence FindScp(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            for (int i = 0; i < Scps.Count; i++)
                if (string.Equals(Scps[i].Code, code, StringComparison.OrdinalIgnoreCase)) return Scps[i];
            return null;
        }
    }

    /// <summary>一局（一次游戏进程）。对局 ID = 开始时间 + 序号，用作注释文件的键。</summary>
    internal sealed class MatchRecord
    {
        public const int Schema = 1;

        public string Id = "";
        public int Version = Schema;
        public DateTime Start = DateTime.Now;
        public DateTime End = DateTime.MinValue;
        public string GameTitle = "";
        public int Pid;
        public string ExeVersion = AppInfo.Version;
        public readonly List<MatchRound> Rounds = new List<MatchRound>();

        public DateTime EffectiveEnd
        {
            get
            {
                if (End != DateTime.MinValue) return End;
                if (Rounds.Count > 0) return Rounds[Rounds.Count - 1].EffectiveEnd;
                return Start;
            }
        }

        public double Seconds { get { return Math.Max(0, (EffectiveEnd - Start).TotalSeconds); } }

        public MatchRound CurrentRound
        {
            get { return Rounds.Count > 0 ? Rounds[Rounds.Count - 1] : null; }
        }

        public MatchRecord Clone()
        {
            MatchRecord c = new MatchRecord();
            c.Id = Id; c.Version = Version; c.Start = Start; c.End = End;
            c.GameTitle = GameTitle; c.Pid = Pid; c.ExeVersion = ExeVersion;
            for (int i = 0; i < Rounds.Count; i++)
            {
                MatchRound r = Rounds[i];
                MatchRound d = new MatchRound();
                d.Index = r.Index; d.Start = r.Start; d.End = r.End;
                for (int k = 0; k < r.Roles.Count; k++)
                {
                    MatchRoleSpan s = r.Roles[k];
                    MatchRoleSpan e = new MatchRoleSpan();
                    e.Start = s.Start; e.End = s.End; e.Role = s.Role; e.State = s.State; e.Note = s.Note;
                    d.Roles.Add(e);
                }
                for (int k = 0; k < r.Scps.Count; k++)
                {
                    ScpPresence s = r.Scps[k];
                    ScpPresence e = new ScpPresence();
                    e.Code = s.Code; e.FirstSeen = s.FirstSeen; e.Confirmed = s.Confirmed;
                    e.ContainedAt = s.ContainedAt; e.Source = s.Source; e.WasPlayerRole = s.WasPlayerRole;
                    d.Scps.Add(e);
                }
                for (int k = 0; k < r.Events.Count; k++)
                {
                    MatchEvent s = r.Events[k];
                    MatchEvent e = new MatchEvent();
                    e.At = s.At; e.Kind = s.Kind; e.Label = s.Label; e.Source = s.Source; e.EndAt = s.EndAt;
                    d.Events.Add(e);
                }
                for (int k = 0; k < r.Samples.Count; k++) d.Samples.Add(r.Samples[k]);
                c.Rounds.Add(d);
            }
            return c;
        }
        public MatchRound EnsureRound(DateTime at, int index)
        {
            MatchRound r = CurrentRound;
            if (r != null && !r.HasEnd) return r;
            r = new MatchRound();
            r.Index = index;
            r.Start = at;
            Rounds.Add(r);
            return r;
        }

        public void CloseRound(DateTime at)
        {
            MatchRound r = CurrentRound;
            if (r != null && !r.HasEnd)
            {
                r.End = at < r.Start ? r.Start : at;
            }
        }

        public int TotalSamples
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Rounds.Count; i++) n += Rounds[i].Samples.Count;
                return n;
            }
        }
    }

    /// <summary>
    /// 079 存活区间的推导。
    /// 用户口径：
    ///   · 不是本机扮演 079 时，在时间轴上画它“存活”的电弧；
    ///   · 玩家死后扫 SCP 列表扫到 079 → 默认**从回合开始**一直存在，所以区间起点 = 回合开始；
    ///   · 玩家没死、但播报 079 被收容 → 说明开局到收容前它一直在，区间也是从回合开始；
    ///   · 收容时间之后区间结束。
    /// </summary>
    internal static class Scp079Window
    {
        public static bool Has079(MatchRound round)
        {
            return Find(round) != null;
        }

        public static ScpPresence Find(MatchRound round)
        {
            if (round == null) return null;
            for (int i = 0; i < round.Scps.Count; i++)
            {
                string c = round.Scps[i].Code ?? "";
                if (c.IndexOf("079", StringComparison.Ordinal) >= 0) return round.Scps[i];
            }
            return null;
        }

        /// <summary>
        /// 取 079 存活区间。返回 false = 这局没有 079 / 没有证据。
        /// </summary>
        public static bool TryGet(MatchRound round, out DateTime start, out DateTime end, out bool fromScan)
        {
            start = DateTime.MinValue;
            end = DateTime.MinValue;
            fromScan = false;

            ScpPresence p = Find(round);
            if (p == null) return false;

            // 只要确认在场（扫描 / 播报 / 手动），一律从回合开始画。
            start = round.Start;
            end = p.ContainedAt != DateTime.MinValue ? p.ContainedAt : round.EffectiveEnd;
            if (end < start) end = start;
            fromScan = string.Equals(p.Source, "scan", StringComparison.OrdinalIgnoreCase)
                || p.FirstSeen != DateTime.MinValue;
            return true;
        }
    }
}
