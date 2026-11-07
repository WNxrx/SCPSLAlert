using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal static class CrashNotifier
    {
        private static Control _anchor;
        private static bool _showing;
        private static bool _hiddenForDialog;
        private static WpfCrashWindow _window;
        private static readonly object _sync = new object();

        public static Action Restart;
        public static Action<CrashReport, bool> Export;
        public static Action<CrashReport> OpenLog;

        public static void Attach(Control anchor)
        {
            _anchor = anchor;
        }

        public static void Show(CrashReport report)
        {
            if (report == null) return;
            lock (_sync)
            {
                if (_showing) return;
                _showing = true;
            }
            try
            {
                Action open = delegate
                {
                    try
                    {
                        WpfCrashWindow window = new WpfCrashWindow(report,
                            delegate { DoRestart(report); },
                            delegate(CrashReport r, bool zip) { DoExport(r, zip); },
                            delegate(CrashReport r) { DoOpenLog(r); });
                        _window = window;
                        window.Closed += delegate
                        {
                            _showing = false;
                            if (_window == window) _window = null;
                        };
                        window.Show();
                    }
                    catch
                    {
                        _showing = false;
                    }
                };
                if (_anchor != null && _anchor.InvokeRequired) _anchor.BeginInvoke(open);
                else open();
            }
            catch { _showing = false; }
        }

        private static void DoRestart(CrashReport report)
        {
            try
            {
                if (Restart != null) { Restart(); return; }
                if (report.Kind == "游戏")
                {
                    Process.Start(new ProcessStartInfo("steam://rungameid/700330") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：" + ex.Message, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void DoExport(CrashReport report, bool zip)
        {
            try
            {
                if (Export != null) { Export(report, zip); return; }
                if (string.IsNullOrEmpty(report.Dir) || !Directory.Exists(report.Dir))
                {
                    MessageBox.Show("这次没有留下现场文件。", "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (zip)
                {
                    using (SaveFileDialog dlg = new SaveFileDialog())
                    {
                        dlg.Filter = "ZIP 压缩包 (*.zip)|*.zip";
                        dlg.FileName = "SCPSLAlert-" + report.Kind + "-" + report.Time.ToString("yyyyMMdd-HHmmss") + ".zip";
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                        System.IO.Compression.ZipFile.CreateFromDirectory(report.Dir, dlg.FileName);
                        MessageBox.Show("已导出：\r\n" + dlg.FileName, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择导出到的文件夹";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    string target = Path.Combine(dlg.SelectedPath,
                        "SCPSLAlert-" + report.Kind + "-" + report.Time.ToString("yyyyMMdd-HHmmss"));
                    CopyDir(report.Dir, target);
                    MessageBox.Show("已导出到：\r\n" + target, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void DoOpenLog(CrashReport report)
        {
            try
            {
                if (OpenLog != null) { OpenLog(report); return; }
                string path = report.LogPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    path = Path.Combine(report.Dir, "summary.txt");
                if (!File.Exists(path))
                {
                    MessageBox.Show("没有可查看的记录文件。", "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                HidePopup();
                using (LogViewerForm form = new LogViewerForm(path)) form.ShowDialog();
                ShowPopup();
            }
            catch { }
        }

        public static void HidePopup()
        {
            try
            {
                if (_window == null || !_window.IsVisible) return;
                _hiddenForDialog = true;
                _window.Hide();
            }
            catch { }
        }

        public static void ShowPopup()
        {
            try
            {
                if (!_hiddenForDialog) return;
                _hiddenForDialog = false;
                if (_window == null) return;
                _window.Show();
                _window.Activate();
            }
            catch { }
        }

        private static void CopyDir(string source, string target)
        {
            Directory.CreateDirectory(target);
            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
                File.Copy(files[i], Path.Combine(target, Path.GetFileName(files[i])), true);
            string[] dirs = Directory.GetDirectories(source);
            for (int i = 0; i < dirs.Length; i++)
                CopyDir(dirs[i], Path.Combine(target, Path.GetFileName(dirs[i])));
        }
    }
}
