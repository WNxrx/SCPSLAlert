using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// "扫描全部"的结果窗口 —— 做成**和校准时的那种悬浮面板一样的样式**
    /// （深色无边框、自己画的标题栏、ESC 关闭），而不是普通带标题栏的窗口。
    ///
    /// 内容每 700ms 从外面的 <c>provider</c> 重取一次：扫描是异步的，
    /// 面板先显示"正在扫描…"，跑完自动变成结果。
    /// </summary>
    internal class ScanResultPanel : OverlayPanel
    {
        private readonly Func<string> _provider;
        private readonly Timer _timer;
        private string _report = "";

        public ScanResultPanel(Func<string> provider)
        {
            _provider = provider;
            Cancelled += delegate { HidePanel(); };

            _timer = new Timer();
            _timer.Interval = 700;
            _timer.Tick += delegate { RefreshReport(); };
        }

        protected override string PanelTitle
        {
            get { return "扫描结果（扫描全部区域）"; }
        }

        protected override string PanelHint
        {
            get { return "ESC = 关闭    （扫描是异步的，跑完这里会自动刷新）"; }
        }

        protected override void BuildRows()
        {
            ClearRows();

            if (string.IsNullOrEmpty(_report))
            {
                AddNote("正在扫描…");
                return;
            }

            string[] lines = _report.Replace("\r\n", "\n").Split('\n');
            int shown = 0;
            for (int i = 0; i < lines.Length && shown < 34; i++)
            {
                string s = lines[i];
                if (s.Length > 120) s = s.Substring(0, 120) + "…";
                AddNote(s);
                shown++;
            }
            if (lines.Length > shown) AddNote("…（还有 " + (lines.Length - shown) + " 行，完整内容见日志）");
        }

        private void RefreshReport()
        {
            if (!Visible) return;
            try
            {
                string s = _provider == null ? "" : _provider();
                if (string.IsNullOrEmpty(s) || s == _report) return;
                _report = s;
                BuildRows();
                Redraw();
            }
            catch { }
        }

        /// <summary>呼出面板（先记住前台窗口，关闭时把焦点还给游戏）。</summary>
        public void OpenPanel()
        {
            try
            {
                _report = _provider == null ? "" : _provider();
            }
            catch { _report = ""; }

            RememberForeground();
            ClearRows();
            BuildRows();
            ShowPanel();
            if (!_timer.Enabled) _timer.Start();
        }

        public new void HidePanel()
        {
            try { _timer.Stop(); } catch { }
            base.HidePanel();
        }
    }
}
