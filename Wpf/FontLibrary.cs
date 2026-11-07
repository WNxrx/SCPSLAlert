using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 字体库：Fonts\ 文件夹里的 .ttf/.otf 统一管理，各字体分类从这里选，不再各自上传。
    /// </summary>
    internal static class FontLibrary
    {
        public class Entry
        {
            public string Name;
            public string File;
        }

        public static string Dir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"); }
        }

        private static List<Entry> _cache;

        public static List<Entry> Entries()
        {
            if (_cache != null) return _cache;
            List<Entry> list = new List<Entry>();
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                foreach (string file in Directory.GetFiles(Dir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".ttf" && ext != ".otf") continue;
                    try
                    {
                        foreach (FontFamily f in Fonts.GetFontFamilies(new Uri(file)))
                        {
                            Entry e = new Entry();
                            e.Name = f.Source;
                            e.File = file;
                            list.Add(e);
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            list.Sort(delegate(Entry a, Entry b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            _cache = list;
            return list;
        }

        public static void Refresh()
        {
            _cache = null;
        }

        /// <summary>把选中的字体文件复制进 Fonts\ 并刷新缓存；返回复制了几个。</summary>
        public static int Import(string[] files)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        string name = Path.GetFileName(files[i]);
                        string dest = Path.Combine(Dir, name);
                        if (!string.Equals(Path.GetFullPath(files[i]), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                            File.Copy(files[i], dest, true);
                        n++;
                    }
                    catch { }
                }
                Refresh();
            }
            catch { }
            return n;
        }

        /// <summary>按字体族名找文件；找不到返回空。</summary>
        public static string FileFor(string family)
        {
            if (string.IsNullOrEmpty(family)) return "";
            List<Entry> list = Entries();
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, family, StringComparison.OrdinalIgnoreCase)) return list[i].File;
            return "";
        }
    }
}
