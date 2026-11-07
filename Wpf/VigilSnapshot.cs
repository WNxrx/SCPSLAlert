using System;
using System.Collections.Generic;

namespace SCPSLAlert
{
    internal sealed class VigilWave
    {
        public DateTime At;
        public string Faction = "";
        public bool IsSmall;
    }

    internal sealed class VigilScpState
    {
        public string Code = "";
        public bool Present;
        public bool Killed;
        public DateTime ChangedAt = DateTime.MinValue;
    }

    internal sealed class VigilSnapshot
    {
        public bool GameRunning;
        public bool InServer;
        public bool SelfDead;
        public string Role = "";
        public string SelfState = "";
        public string RoleTimeline = "";
        public DateTime RoundStart = DateTime.MinValue;
        public string OcrInfo = "";
        public string LastScan = "";
        public bool WarheadRunning;
        public double WarheadRemaining;
        public double WarheadTotal = 90;
        public bool DeadManActive;
        public double DeadManRemaining;
        public double DeadManTotal = 150;
        public readonly List<VigilWave> Waves = new List<VigilWave>();
        public readonly List<VigilScpState> Scps = new List<VigilScpState>();
    }
}
