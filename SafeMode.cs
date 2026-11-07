using System;
using System.IO;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 自定义主题安全模式：上次启动异常结束（崩溃 / 被强杀）后，下次启动弹一次窗，
    /// 询问是否禁用自定义背景（视频/图片）、音乐、毛玻璃、自定义字体启动。
    /// 正常退出会清掉标记，所以不会每次启动都弹。
    /// </summary>
    internal static class SafeMode
    {
        private static readonly string FlagDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCPSLAlert");
        private static readonly string FlagPath = Path.Combine(FlagDir, "start.flag");
        private static readonly string BackupPath =
            Path.Combine(Application.StartupPath, "config.safe-backup.ini");

        public static bool Active { get; private set; }
        private static System.Threading.Timer _timer;

        /// <summary>启动时调用：上次没正常退出就弹一次安全模式询问。</summary>
        public static bool Ask(Config cfg, string configPath)
        {
            bool crashed = false;
            try { crashed = File.Exists(FlagPath); } catch { }
            BeginRun();
            if (!crashed || cfg == null) return false;

            DialogResult r = DialogResult.No;
            try
            {
                r = MessageBox.Show(
                    "上次启动没有正常结束，可能是自定义背景 / 视频 / 字体导致的。\n\n"
                    + "要用“安全模式”启动吗？\n"
                    + "安全模式会临时禁用：自定义背景（含视频）、背景音乐、毛玻璃、自定义字体、界面缩放与动态强调色。\n"
                    + "你的配置不会被删除，正常退出后自动恢复。",
                    "A.R.C.T 安全模式",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            }
            catch { }

            if (r != DialogResult.Yes) return false;

            Active = true;
            try { File.Copy(configPath, BackupPath, true); } catch { }

            cfg.BackgroundImage = "";
            cfg.BackgroundMusic = "";
            cfg.BackgroundMusicEnabled = false;
            cfg.GlassWindow = false;
            cfg.GlassCards = true;
            cfg.DynamicAccent = false;
            cfg.UiScale = 100;
            cfg.UiDensity = 1;
            cfg.UiFontFile = ""; cfg.UiFontName = "";
            cfg.UiTitleFontFile = ""; cfg.UiTitleFontName = "";
            cfg.UiMonoFontFile = ""; cfg.UiMonoFontName = "";
            cfg.PromptFontFile = ""; cfg.PromptFontFamily = "";
            return true;
        }

        /// <summary>启动时写标记；12 秒后仍活着才算这次启动成功。</summary>
        public static void BeginRun()
        {
            try
            {
                if (!Directory.Exists(FlagDir)) Directory.CreateDirectory(FlagDir);
                File.WriteAllText(FlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { }

            try
            {
                if (_timer != null) _timer.Dispose();
                _timer = new System.Threading.Timer(delegate
                {
                    try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
                }, null, 12000, System.Threading.Timeout.Infinite);
            }
            catch { }
        }

        /// <summary>正常退出：删标记；安全模式则把用户原配置恢复回去。</summary>
        public static void CleanExit()
        {
            try { if (_timer != null) { _timer.Dispose(); _timer = null; } } catch { }
            try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
            if (!Active) return;
            try
            {
                string configPath = Path.Combine(Application.StartupPath, "config.ini");
                if (File.Exists(BackupPath))
                {
                    File.Copy(BackupPath, configPath, true);
                    File.Delete(BackupPath);
                }
            }
            catch { }
        }
    }
}
