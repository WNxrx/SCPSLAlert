using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SCPSLAlert
{
    internal sealed class ImportedEffect
    {
        public string Name;
        public string File;
        public TriggerEffect Effect;
    }

    /// <summary>
    /// 外部特效包：.arctfx 文本（或含 .arctfx/effect.ini 的 .zip）。
    /// 只能参数化内置渲染器（抖动/闪屏/礼炮/边缘脉冲），不执行外部代码。
    /// </summary>
    internal static class EffectPackStore
    {
        private static readonly string[] Types = { "shake", "flash", "confetti", "pulse" };

        public static string Dir
        {
            get
            {
                string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "特效包");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static List<TriggerEffect> Builtins()
        {
            List<TriggerEffect> list = new List<TriggerEffect>();
            list.Add(new TriggerEffect { Type = "shake", Amplitude = 8, DurationMs = 320 });
            list.Add(new TriggerEffect { Type = "flash", Intensity = 0.22, DurationMs = 180, Color1 = "#FFFFFF" });
            list.Add(new TriggerEffect { Type = "confetti", Count = 60, DurationMs = 1200, Color1 = "#FF4D4D", Color2 = "#43CFBB" });
            list.Add(new TriggerEffect { Type = "pulse", Intensity = 0.45, DurationMs = 600, Color1 = "#43CFBB" });
            return list;
        }

        public static List<TriggerEffect> All()
        {
            List<TriggerEffect> list = Builtins();
            List<ImportedEffect> packs = List();
            for (int i = 0; i < packs.Count; i++)
                if (packs[i] != null && packs[i].Effect != null) list.Add(packs[i].Effect);
            return list;
        }

        public static List<ImportedEffect> List()
        {
            List<ImportedEffect> list = new List<ImportedEffect>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                string[] files = Directory.GetFiles(Dir, "*.arctfx", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    ImportedEffect e = ParseFile(files[i]);
                    if (e != null) list.Add(e);
                }
            }
            catch { }
            return list;
        }

        public static string Import(string path, out ImportedEffect imported)
        {
            imported = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "文件不存在";
            string text;
            try
            {
                string ext = Path.GetExtension(path);
                if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
                    text = ReadFromZip(path);
                else
                    text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) { return "读取失败：" + ex.Message; }
            if (string.IsNullOrEmpty(text)) return "包里没有可用内容";

            ImportedEffect e = ParseText(text, path);
            if (e == null) return "格式不正确或类型/参数不合法";
            try
            {
                string safe = SafeName(e.Name) + ".arctfx";
                string target = Path.Combine(Dir, safe);
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(Dir, SafeName(e.Name) + "-" + n + ".arctfx");
                    n++;
                }
                File.WriteAllText(target, Normalize(text), Encoding.UTF8);
                e.File = target;
                imported = e;
                return null;
            }
            catch (Exception ex) { return "写入失败：" + ex.Message; }
        }

        private static string ReadFromZip(string path)
        {
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry pick = null;
                for (int i = 0; i < zip.Entries.Count; i++)
                {
                    string n = zip.Entries[i].FullName.ToLowerInvariant();
                    if (n.EndsWith(".arctfx") || n.EndsWith("effect.ini"))
                    {
                        pick = zip.Entries[i];
                        break;
                    }
                }
                if (pick == null) return null;
                using (StreamReader r = new StreamReader(pick.Open(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        private static ImportedEffect ParseFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return ParseText(text, path);
            }
            catch { return null; }
        }

        private static ImportedEffect ParseText(string text, string path)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string name = "";
            TriggerEffect e = new TriggerEffect();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")
                    || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "name") name = val;
                else if (key == "type") e.Type = val.ToLowerInvariant();
                else if (key == "amplitude") e.Amplitude = Int(val, e.Amplitude);
                else if (key == "duration" || key == "durationms") e.DurationMs = Int(val, e.DurationMs);
                else if (key == "intensity") e.Intensity = Double(val, e.Intensity);
                else if (key == "count") e.Count = Int(val, e.Count);
                else if (key == "color1") e.Color1 = val;
                else if (key == "color2") e.Color2 = val;
            }
            if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(path);
            if (Array.IndexOf(Types, e.Type) < 0) return null;
            e.Label = name;
            e.Amplitude = Math.Max(0, Math.Min(24, e.Amplitude));
            e.DurationMs = Math.Max(60, Math.Min(1800, e.DurationMs));
            e.Intensity = Math.Max(0.03, Math.Min(0.8, e.Intensity));
            e.Count = Math.Max(10, Math.Min(120, e.Count));
            ImportedEffect pack = new ImportedEffect();
            pack.Name = name;
            pack.File = path;
            pack.Effect = e;
            return pack;
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim() + "\n";
        }

        private static string SafeName(string name)
        {
            string s = string.IsNullOrEmpty(name) ? "特效" : name;
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++) s = s.Replace(bad[i], '_');
            s = s.Trim();
            if (s.Length == 0) s = "特效";
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        private static int Int(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static double Double(string s, double def)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }
    }
}
