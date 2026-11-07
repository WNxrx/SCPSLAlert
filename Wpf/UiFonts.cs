using System;
using System.IO;
using System.Windows.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 全局字体分类。启动时按配置加载；运行时改字体需重启软件生效。
    ///   Body  = 正文 / 设置名 / 说明
    ///   Title = 标题 / 大数字
    ///   Mono  = 数字、代码、快捷键、日志
    /// </summary>
    internal static class UiFonts
    {
        public static FontFamily Body = new FontFamily("Microsoft YaHei UI");
        public static FontFamily Title = new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");
        public static FontFamily Mono = new FontFamily("Consolas");

        public static void Apply(Config cfg)
        {
            Body = Load(cfg.UiFontFile, cfg.UiFontName, "Microsoft YaHei UI");
            Title = Load(cfg.UiTitleFontFile, cfg.UiTitleFontName, "Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");
            Mono = Load(cfg.UiMonoFontFile, cfg.UiMonoFontName, "Consolas");
        }

        private static FontFamily Load(string file, string name, string fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(name))
                {
                    string path = (!string.IsNullOrEmpty(file) && File.Exists(file)) ? file : FontLibrary.FileFor(name);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        Fonts.GetFontFamilies(new Uri(path));
                        return new FontFamily(new Uri(path), "./#" + name);
                    }
                }
            }
            catch { }
            return new FontFamily(fallback);
        }
    }
}
