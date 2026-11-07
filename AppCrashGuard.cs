using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal static class AppCrashGuard
    {
        private const string LockName = "run.lock";
        private const int MiniDumpNormal = 0;
        private const int MiniDumpWithUnloadedModules = 0x20;
        private const int MiniDumpWithThreadInfo = 0x1000;

        private static bool _installed;
        private static string _lockPath = "";
        private static NativeFilter _filter;
        private static readonly object _sync = new object();

        [StructLayout(LayoutKind.Sequential)]
        private struct MiniDumpExceptionInfo
        {
            public int ThreadId;
            public IntPtr ExceptionPointers;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientPointers;
        }

        private delegate IntPtr NativeFilter(IntPtr info);

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr process, int processId, IntPtr file,
            int dumpType, ref MiniDumpExceptionInfo exception, IntPtr userStream, IntPtr callback);

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr process, int processId, IntPtr file,
            int dumpType, IntPtr exception, IntPtr userStream, IntPtr callback);

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(NativeFilter filter);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            try { System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnTaskException; }
            catch { }
            try
            {
                _filter = OnNativeException;
                SetUnhandledExceptionFilter(_filter);
            }
            catch { }
        }

        public static void BeginRun()
        {
            try
            {
                _lockPath = Path.Combine(CrashPaths.Root, LockName);
                using (Process p = Process.GetCurrentProcess())
                {
                    string text = "pid=" + p.Id + Environment.NewLine
                        + "start=" + p.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine
                        + "at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    File.WriteAllText(_lockPath, text, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void EndRun()
        {
            try
            {
                if (_lockPath.Length > 0 && File.Exists(_lockPath)) File.Delete(_lockPath);
            }
            catch { }
        }

        public static CrashReport TakePreviousRun()
        {
            try
            {
                string path = Path.Combine(CrashPaths.Root, LockName);
                if (!File.Exists(path)) return null;
                string[] lines = File.ReadAllLines(path);
                int pid = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("pid=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.Substring(4), out pid);
                }
                bool alive = false;
                if (pid > 0)
                {
                    try
                    {
                        Process p = Process.GetProcessById(pid);
                        alive = p != null && !p.HasExited;
                        if (p != null) p.Dispose();
                    }
                    catch { alive = false; }
                }
                if (alive) return null;
                CrashReport report = LatestAppReport();
                if (report == null)
                {
                    string dir = CrashPaths.NewDir(CrashPaths.AppCrash, "exit");
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("SCPSLAlert 退出记录");
                    sb.AppendLine("时间：" + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine("状态：上次运行没有留下正常退出标记");
                    sb.AppendLine();
                    sb.AppendLine("没有找到转储或异常记录。");
                    sb.AppendLine("常见原因：用任务管理器结束、被系统关机/注销中断、");
                    sb.AppendLine("或者是程序在更新/重编译时被强制关闭。");
                    sb.AppendLine();
                    sb.AppendLine("启动标记：");
                    for (int i = 0; i < lines.Length; i++) sb.AppendLine(lines[i]);
                    string file = Path.Combine(dir, "report.txt");
                    File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));

                    report = new CrashReport();
                    report.Kind = "程序";
                    report.Title = "上次运行没有正常退出";
                    report.Time = File.GetLastWriteTime(path);
                    report.Summary = "没有找到异常记录或转储，进程可能是被强制结束的。";
                    report.Meta.Add("状态  上次运行未留下正常退出标记");
                    report.Meta.Add("时间  " + report.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                    report.Suggestions.Add("如果是你自己关掉的，点关闭忽略即可。");
                    report.Suggestions.Add("如果反复出现，打开日志查看最后一次操作。");
                    report.Dir = dir;
                    report.LogPath = file;
                }
                report.PreviousRun = true;
                CrashPaths.PruneFolders(CrashPaths.AppCrash, 20, 100L * 1024 * 1024);
                return report;
            }
            catch { return null; }
            finally
            {
                try
                {
                    string path = Path.Combine(CrashPaths.Root, LockName);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        public static CrashReport LatestAppReport()
        {
            try
            {
                List<string> dirs = CrashPaths.Folders(CrashPaths.AppCrash);
                for (int i = 0; i < dirs.Count; i++)
                {
                    string summary = Path.Combine(dirs[i], "summary.txt");
                    string report = Path.Combine(dirs[i], "report.txt");
                    string file = File.Exists(summary) ? summary : (File.Exists(report) ? report : "");
                    if (file.Length == 0) continue;
                    CrashReport r = new CrashReport();
                    r.Kind = "程序";
                    r.Dir = dirs[i];
                    r.DumpPath = Path.Combine(dirs[i], "app.dmp");
                    if (!File.Exists(r.DumpPath)) r.DumpPath = "";
                    r.Title = "SCPSLAlert 异常退出";
                    r.Time = Directory.GetLastWriteTime(dirs[i]);
                    r.Summary = FirstLine(File.ReadAllText(file));
                    r.Meta.Add("记录  " + Path.GetFileName(file));
                    r.Meta.Add("时间  " + r.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                    r.LogPath = file;
                    return r;
                }
            }
            catch { }
            return null;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0) return lines[i].Trim();
            }
            return "";
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Handle(e.Exception, "界面线程异常", false);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Handle(e.ExceptionObject as Exception, "未处理异常", e.IsTerminating);
        }

        private static void OnTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            try { Logger.Err("程序", "后台任务异常", e.Exception); } catch { }
        }

        private static void Handle(Exception ex, string title, bool terminating)
        {
            lock (_sync)
            {
                try
                {
                    string dir = CrashPaths.NewDir(CrashPaths.AppCrash, "app");
                    string dump = WriteDump(dir);
                    string text = BuildReportText(ex, title, dump);
                    File.WriteAllText(Path.Combine(dir, "report.txt"), text, new UTF8Encoding(false));

                    CrashReport report = new CrashReport();
                    report.Kind = "程序";
                    report.Title = "SCPSLAlert " + title;
                    report.Time = DateTime.Now;
                    report.Dir = dir;
                    report.DumpPath = dump;
                    report.LogPath = Path.Combine(dir, "report.txt");
                    if (ex != null)
                    {
                        report.Summary = ex.GetType().Name + "：" + ex.Message;
                        report.Meta.Add("异常  " + ex.GetType().Name);
                        report.Meta.Add("位置  " + (ex.TargetSite == null ? "未知" : ex.TargetSite.Name));
                    }
                    else
                    {
                        report.Summary = title;
                        report.Meta.Add("异常  未知（原生崩溃）");
                        report.Meta.Add("时间  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                    report.SaveText();
                    try { Logger.Err("程序", title, ex); } catch { }
                    CrashNotifier.Show(report);
                }
                catch { }
            }
            if (terminating)
            {
                try { Logger.Flush(800); } catch { }
            }
        }

        private static IntPtr OnNativeException(IntPtr info)
        {
            try
            {
                string dir = CrashPaths.NewDir(CrashPaths.AppCrash, "app-native");
                string dump = WriteNativeDump(dir, info);
                string text = "SCPSLAlert 原生崩溃" + Environment.NewLine
                    + "时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                    + "转储：" + (dump.Length == 0 ? "写入失败" : dump) + Environment.NewLine;
                File.WriteAllText(Path.Combine(dir, "report.txt"), text, new UTF8Encoding(false));
            }
            catch { }
            return IntPtr.Zero;
        }

        private static string WriteDump(string dir)
        {
            try
            {
                string path = Path.Combine(dir, "app.dmp");
                using (Process p = Process.GetCurrentProcess())
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    bool ok = MiniDumpWriteDump(p.Handle, p.Id, fs.SafeFileHandle.DangerousGetHandle(),
                        MiniDumpNormal | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (!ok)
                    {
                        MiniDumpExceptionInfo info = new MiniDumpExceptionInfo();
                        info.ThreadId = GetCurrentThreadId();
                        info.ExceptionPointers = IntPtr.Zero;
                        info.ClientPointers = false;
                        ok = MiniDumpWriteDump(p.Handle, p.Id, fs.SafeFileHandle.DangerousGetHandle(),
                            MiniDumpNormal | MiniDumpWithThreadInfo, ref info, IntPtr.Zero, IntPtr.Zero);
                    }
                    return ok ? path : "";
                }
            }
            catch { return ""; }
        }

        private static string WriteNativeDump(string dir, IntPtr exceptionPointers)
        {
            try
            {
                string path = Path.Combine(dir, "app.dmp");
                using (Process p = Process.GetCurrentProcess())
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    MiniDumpExceptionInfo info = new MiniDumpExceptionInfo();
                    info.ThreadId = GetCurrentThreadId();
                    info.ExceptionPointers = exceptionPointers;
                    info.ClientPointers = false;
                    bool ok = MiniDumpWriteDump(p.Handle, p.Id, fs.SafeFileHandle.DangerousGetHandle(),
                        MiniDumpNormal | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules,
                        ref info, IntPtr.Zero, IntPtr.Zero);
                    return ok ? path : "";
                }
            }
            catch { return ""; }
        }

        private static string BuildReportText(Exception ex, string title, string dump)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SCPSLAlert 异常报告");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("标题：" + title);
            sb.AppendLine("版本：" + Application.ProductVersion);
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine("运行时：" + Environment.Version);
            sb.AppendLine("进程：" + Process.GetCurrentProcess().Id);
            sb.AppendLine("转储：" + (dump.Length == 0 ? "写入失败" : dump));
            sb.AppendLine();
            if (ex != null)
            {
                sb.AppendLine(ex.ToString());
            }
            else
            {
                sb.AppendLine("原生崩溃，没有托管异常信息。");
            }
            sb.AppendLine();
            sb.AppendLine("已加载模块");
            try
            {
                Process p = Process.GetCurrentProcess();
                for (int i = 0; i < p.Modules.Count; i++)
                {
                    ProcessModule m = p.Modules[i];
                    sb.AppendLine(m.FileName + "  " + m.FileVersionInfo.FileVersion);
                    m.Dispose();
                }
                p.Dispose();
            }
            catch { }
            return sb.ToString();
        }
    }
}

