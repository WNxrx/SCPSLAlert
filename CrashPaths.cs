using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SCPSLAlert
{
    internal static class CrashPaths
    {
        public static string Root
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string Backup
        {
            get { return Path.Combine(Root, "备份"); }
        }

        public static string AppCrash
        {
            get { return Path.Combine(Root, "崩溃"); }
        }

        public static string GameCrash
        {
            get { return Path.Combine(Root, "游戏崩溃"); }
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        public static string Dir(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch { }
            return path;
        }

        public static string NewDir(string root, string prefix)
        {
            Dir(root);
            string name = prefix + "-" + Stamp();
            string path = Path.Combine(root, name);
            int n = 2;
            while (Directory.Exists(path))
            {
                path = Path.Combine(root, name + "-" + n);
                n++;
            }
            try { Directory.CreateDirectory(path); }
            catch { }
            return path;
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        public static long TotalSize(string root)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(root)) return 0;
                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { total += new FileInfo(files[i]).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        public static List<string> Folders(string root)
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(root)) return list;
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                {
                    try
                    {
                        DateTime t = Directory.GetLastWriteTime(dirs[i]);
                        list.Add(dirs[i]);
                    }
                    catch { }
                }
                list.Sort(delegate(string a, string b)
                {
                    DateTime ta = DateTime.MinValue, tb = DateTime.MinValue;
                    try { ta = Directory.GetLastWriteTime(a); } catch { }
                    try { tb = Directory.GetLastWriteTime(b); } catch { }
                    return tb.CompareTo(ta);
                });
            }
            catch { }
            return list;
        }

        public static void PruneFolders(string root, int keep, long maxBytes)
        {
            try
            {
                List<string> dirs = Folders(root);
                for (int i = keep; i < dirs.Count; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
                if (maxBytes > 0 && TotalSize(root) > maxBytes)
                {
                    dirs = Folders(root);
                    for (int i = dirs.Count - 1; i >= 0 && TotalSize(root) > maxBytes; i--)
                    {
                        try { Directory.Delete(dirs[i], true); } catch { }
                    }
                }
            }
            catch { }
        }

        public static string Copy(string source, string targetDir, string name)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return "";
            try
            {
                Dir(targetDir);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileName(source);
                string target = Path.Combine(targetDir, name);
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        using (FileStream src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (FileStream dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                                dst.Write(buffer, 0, read);
                        }
                        return target;
                    }
                    catch
                    {
                        Thread.Sleep(150);
                    }
                }
            }
            catch { }
            return "";
        }
    }
}
