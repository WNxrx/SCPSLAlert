using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class CrashCause
    {
        public string Level = "中";
        public string Title = "";
        public string Detail = "";
    }

    internal sealed class CrashReport
    {
        public string Kind = "程序";
        public string Title = "";
        public string Summary = "";
        public DateTime Time = DateTime.Now;
        public List<string> Meta = new List<string>();
        public List<CrashCause> Causes = new List<CrashCause>();
        public List<string> Suggestions = new List<string>();
        public string Dir = "";
        public string DumpPath = "";
        public string LogPath = "";
        public string StackText = "";
        public bool PreviousRun;

        /// <summary>是否拿到了确定的崩溃证据。false = "没有正常退出，但也不是崩溃"。</summary>
        public bool ConfirmedCrash = true;

        public string Signature
        {
            get
            {
                if (Meta.Count == 0) return "";
                if (Meta.Count == 1) return Meta[0];
                return Meta[0] + " · " + Meta[1];
            }
        }

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SCPSLAlert " + Kind + "异常报告");
            sb.AppendLine("时间：" + Time.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("标题：" + Title);
            if (Summary.Length > 0) sb.AppendLine("摘要：" + Summary);
            if (!ConfirmedCrash) sb.AppendLine("判定：没有崩溃证据（非正常退出）");
            sb.AppendLine();
            for (int i = 0; i < Meta.Count; i++) sb.AppendLine(Meta[i]);
            if (Causes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("可能的原因");
                for (int i = 0; i < Causes.Count; i++)
                    sb.AppendLine("[" + Causes[i].Level + "] " + Causes[i].Title
                        + (Causes[i].Detail.Length > 0 ? " —— " + Causes[i].Detail : ""));
            }
            if (Suggestions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("建议");
                for (int i = 0; i < Suggestions.Count; i++)
                    sb.AppendLine((i + 1) + ". " + Suggestions[i]);
            }
            if (StackText.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("调用栈（栈扫描，近似）");
                sb.AppendLine(StackText.TrimEnd());
            }
            if (Dir.Length > 0) sb.AppendLine();
            if (Dir.Length > 0) sb.AppendLine("现场目录：" + Dir);
            if (DumpPath.Length > 0) sb.AppendLine("转储文件：" + DumpPath);
            return sb.ToString();
        }

        public string SaveText()
        {
            try
            {
                if (Dir.Length == 0) return "";
                CrashPaths.Dir(Dir);
                string path = Path.Combine(Dir, "summary.txt");
                File.WriteAllText(path, ToText(), new UTF8Encoding(false));
                return path;
            }
            catch { return ""; }
        }
    }
}
