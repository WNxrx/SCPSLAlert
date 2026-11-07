using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class BackupEntry
    {
        public string Path;
        public DateTime Time;
        public long Bytes;

        public string Name
        {
            get { return System.IO.Path.GetFileName(Path); }
        }

        public string SizeText
        {
            get { return CrashPaths.Size(Bytes); }
        }

        public string TimeText
        {
            get { return Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
        }
    }

    internal static class ConfigBackup
    {
        private const int Keep = 20;
        private const long MaxBytes = 20L * 1024 * 1024;
        private static string _lastHash = "";

        public static string Dir
        {
            get { return CrashPaths.Dir(CrashPaths.Backup); }
        }

        public static void EnsureBaseline(string configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
                if (List().Count > 0) return;
                Snapshot(configPath, "baseline");
            }
            catch { }
        }

        public static void BeforeSave(string configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
                string text = File.ReadAllText(configPath, Encoding.UTF8);
                string hash = Hash(text);
                if (hash == _lastHash) return;
                List<BackupEntry> existing = List();
                if (existing.Count > 0)
                {
                    try
                    {
                        if (Hash(File.ReadAllText(existing[0].Path, Encoding.UTF8)) == hash)
                        {
                            _lastHash = hash;
                            return;
                        }
                    }
                    catch { }
                }
                string made = Snapshot(configPath, "auto");
                if (made.Length > 0) _lastHash = hash;
                Prune();
            }
            catch { }
        }

        public static string Snapshot(string configPath, string tag)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return "";
                CrashPaths.Dir(Dir);
                string baseName = "config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string target = System.IO.Path.Combine(Dir, baseName + ".ini");
                int n = 2;
                while (File.Exists(target))
                {
                    target = System.IO.Path.Combine(Dir, baseName + "-" + n + ".ini");
                    n++;
                }
                string copied = CrashPaths.Copy(configPath, Dir, System.IO.Path.GetFileName(target));
                return copied;
            }
            catch { return ""; }
        }

        public static List<BackupEntry> List()
        {
            List<BackupEntry> list = new List<BackupEntry>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                string[] files = Directory.GetFiles(Dir, "config-*.ini");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        BackupEntry e = new BackupEntry();
                        e.Path = fi.FullName;
                        e.Bytes = fi.Length;
                        e.Time = ParseTime(fi.Name, fi.LastWriteTime);
                        list.Add(e);
                    }
                    catch { }
                }
                list.Sort(delegate(BackupEntry a, BackupEntry b) { return b.Time.CompareTo(a.Time); });
            }
            catch { }
            return list;
        }

        private static DateTime ParseTime(string name, DateTime fallback)
        {
            try
            {
                string s = name.Replace("config-", "").Replace(".ini", "");
                int dash = s.IndexOf('-');
                if (dash < 0) return fallback;
                string date = s.Substring(0, dash);
                string rest = s.Substring(dash + 1);
                int dash2 = rest.IndexOf('-');
                if (dash2 > 0) rest = rest.Substring(0, dash2);
                if (rest.Length > 6) rest = rest.Substring(0, 6);
                DateTime t;
                if (DateTime.TryParseExact(date + rest, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out t)) return t;
            }
            catch { }
            return fallback;
        }

        public static bool Restore(string backupPath, string configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath)) return false;
                Snapshot(configPath, "before-restore");
                string text = File.ReadAllText(backupPath, Encoding.UTF8);
                WriteAtomic(configPath, text);
                _lastHash = Hash(text);
                return true;
            }
            catch { return false; }
        }

        public static void Prune()
        {
            try
            {
                List<BackupEntry> list = List();
                for (int i = Keep; i < list.Count; i++)
                {
                    try { File.Delete(list[i].Path); } catch { }
                }
                long total = CrashPaths.TotalSize(Dir);
                if (total <= MaxBytes) return;
                list = List();
                for (int i = list.Count - 1; i >= 0 && total > MaxBytes; i--)
                {
                    try { File.Delete(list[i].Path); total -= list[i].Bytes; } catch { }
                }
            }
            catch { }
        }

        public static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(true));
            if (File.Exists(path))
            {
                string old = path + ".old";
                try
                {
                    File.Replace(tmp, path, old);
                    try { File.Delete(old); } catch { }
                    return;
                }
                catch { }
                try
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                    return;
                }
                catch
                {
                    File.WriteAllText(path, content, new UTF8Encoding(true));
                    try { File.Delete(tmp); } catch { }
                    return;
                }
            }
            File.Move(tmp, path);
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] data = Encoding.UTF8.GetBytes(text ?? "");
                byte[] hash = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
