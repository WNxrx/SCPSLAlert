using System;
using System.Collections.Generic;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>
    /// 崩溃前的一段遥测环形缓冲（1 秒一个点，约 10 分钟）。
    /// 主界面每秒采样时顺手塞进来；游戏崩溃后由 GameCrashWatcher 取出来做对比。
    /// </summary>
    internal static class CrashTelemetry
    {
        public struct Snap
        {
            public DateTime At;
            public bool HasFps;
            public double Fps;
            public double Cpu;
            public double Gpu;
            public double MemPct;
            public bool HasTemp;
            public double Temp;
            public double Disk;
            public double Net;
        }

        private const int Keep = 600;
        private static readonly List<Snap> Ring = new List<Snap>(Keep + 8);
        private static readonly object Gate = new object();

        public static void Push(GamePerfSample q)
        {
            if (q == null) return;
            Snap s = new Snap();
            s.At = q.At;
            s.HasFps = q.HasFps;
            s.Fps = q.Fps;
            s.Cpu = q.Cpu;
            s.Gpu = q.Gpu;
            s.MemPct = q.MemPct;
            s.HasTemp = q.TempAvailable;
            s.Temp = q.Temp;
            s.Disk = q.Disk;
            s.Net = q.Net;
            lock (Gate)
            {
                Ring.Add(s);
                if (Ring.Count > Keep) Ring.RemoveRange(0, Ring.Count - Keep);
            }
        }

        public static string Report()
        {
            List<Snap> list;
            lock (Gate) list = new List<Snap>(Ring);
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("==== 崩溃前后遥测 ====");
            if (list.Count == 0)
            {
                sb.AppendLine("（没有采样：游戏可能在开始记录前就退出了）");
                return sb.ToString();
            }

            Snap last = list[list.Count - 1];
            sb.AppendLine("崩溃点   : " + last.At.ToString("HH:mm:ss") + "  " + Point(last));

            Snap avg10 = Average(list, 10);
            Snap avg60 = Average(list, 60);
            Snap avg300 = Average(list, 300);
            sb.AppendLine("前 10 秒 : " + Point(avg10));
            sb.AppendLine("前 60 秒 : " + Point(avg60));
            sb.AppendLine("前 5 分钟: " + Point(avg300));
            sb.AppendLine("变化     : CPU " + Delta(last.Cpu, avg60.Cpu, "pp")
                + " / GPU " + Delta(last.Gpu, avg60.Gpu, "pp")
                + " / 内存 " + Delta(last.MemPct, avg60.MemPct, "pp")
                + " / FPS " + Delta(last.HasFps ? last.Fps : 0, avg60.HasFps ? avg60.Fps : 0, "")
                + (last.HasTemp ? " / 温度 " + Delta(last.Temp, avg60.Temp, "°C") : ""));
            sb.AppendLine();

            sb.AppendLine("判断：");
            int marks = 0;
            if (last.MemPct >= 90) { sb.AppendLine("· 崩溃时内存占用 " + last.MemPct.ToString("0") + "%，已接近上限"); marks++; }
            if (last.Cpu >= 90) { sb.AppendLine("· 崩溃时 CPU " + last.Cpu.ToString("0") + "%，处于满载"); marks++; }
            if (last.Gpu >= 90) { sb.AppendLine("· 崩溃时 GPU " + last.Gpu.ToString("0") + "%，处于满载"); marks++; }
            if (last.HasTemp && last.Temp >= 85) { sb.AppendLine("· 崩溃时温度 " + last.Temp.ToString("0") + "°C，偏高"); marks++; }
            if (last.HasFps && avg60.HasFps && avg60.Fps > 10 && last.Fps < avg60.Fps * 0.6)
            {
                sb.AppendLine("· 崩溃前帧率从 " + avg60.Fps.ToString("0") + " 掉到 " + last.Fps.ToString("0")
                    + "（降幅 " + ((1 - last.Fps / avg60.Fps) * 100).ToString("0") + "%）");
                marks++;
            }
            if (avg60.MemPct - avg300.MemPct >= 8)
            {
                sb.AppendLine("· 最近 1 分钟内存比 5 分钟均值高 "
                    + (avg60.MemPct - avg300.MemPct).ToString("0") + " 个百分点，可能有内存增长");
                marks++;
            }
            if (marks == 0) sb.AppendLine("· 崩溃点没有明显资源尖峰；更像是游戏自身异常（看转储 / 日志）");

            return sb.ToString();
        }

        private static string Point(Snap s)
        {
            string fps = s.HasFps ? s.Fps.ToString("0") + " FPS" : "FPS --";
            string temp = s.HasTemp ? s.Temp.ToString("0") + "°C" : "温度 --";
            return fps + "  CPU " + s.Cpu.ToString("0") + "%  GPU " + s.Gpu.ToString("0")
                + "%  内存 " + s.MemPct.ToString("0") + "%  " + temp;
        }

        private static string Delta(double now, double before, string unit)
        {
            double d = now - before;
            if (Math.Abs(d) < 0.5) return "持平";
            return (d > 0 ? "+" : "") + d.ToString("0") + unit;
        }

        private static Snap Average(List<Snap> list, int seconds)
        {
            DateTime cut = list[list.Count - 1].At.AddSeconds(-seconds);
            int n = 0;
            Snap sum = new Snap();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].At < cut) break;
                Snap s = list[i];
                sum.Cpu += s.Cpu; sum.Gpu += s.Gpu; sum.MemPct += s.MemPct;
                sum.Disk += s.Disk; sum.Net += s.Net;
                sum.Fps += s.HasFps ? s.Fps : 0;
                sum.Temp += s.HasTemp ? s.Temp : 0;
                if (s.HasFps) sum.HasFps = true;
                if (s.HasTemp) sum.HasTemp = true;
                n++;
            }
            if (n == 0) return new Snap();
            sum.Cpu /= n; sum.Gpu /= n; sum.MemPct /= n; sum.Disk /= n; sum.Net /= n;
            sum.Fps /= n; sum.Temp /= n;
            return sum;
        }
    }
}
