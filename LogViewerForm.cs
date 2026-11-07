using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 程序运行日志查看器。
    ///
    /// 为什么要在程序内部看，而不是丢给系统打开：
    ///   · .log 这个扩展名很多机器上**根本没有关联程序**
    ///     （实测 AssocQueryString 返回 0x80070483 = 没有关联），
    ///     "用默认程序打开"只会弹一个选择框或者干脆没反应；
    ///   · 交给资源管理器定位也可能被系统策略挡住。
    /// 所以这里直接在程序里把日志读出来显示，点一下**必定**能看到内容，
    /// 要看原文件再去点"打开所在目录"。
    /// </summary>
    internal class LogViewerForm : Form
    {
        /// <summary>只读最后这么多字节，日志再大打开也是一瞬间。</summary>
        private const int MaxTailBytes = 256 * 1024;

        private readonly string _path;
        private TextBox _box;
        private Label _info;

        public LogViewerForm(string path)
        {
            _path = path;

            Text = "日志 · " + System.IO.Path.GetFileNameWithoutExtension(path ?? "");
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            ForeColor = UiKit.TextColor;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(640, 380);

            BuildUi();
            Reload();
        }

        /// <summary>当前显示出来的日志文本（自动化测试用）。</summary>
        public string LogText
        {
            get { return _box == null ? "" : _box.Text; }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(12, 10, 12, 10);
            root.BackColor = UiKit.PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root);

            _info = new Label();
            _info.Font = UiKit.FontSmall;
            _info.ForeColor = UiKit.SubColor;
            _info.Dock = DockStyle.Fill;
            _info.TextAlign = ContentAlignment.MiddleLeft;
            _info.Text = _path;
            root.Controls.Add(_info, 0, 0);

            _box = new TextBox();
            _box.Multiline = true;
            _box.ReadOnly = true;
            _box.ScrollBars = ScrollBars.Both;
            _box.WordWrap = false;
            _box.Dock = DockStyle.Fill;
            _box.BackColor = Color.White;
            _box.ForeColor = UiKit.TextColor;
            _box.Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _box.BorderStyle = BorderStyle.FixedSingle;
            root.Controls.Add(_box, 0, 1);

            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 5;
            bar.RowCount = 1;
            bar.Margin = new Padding(0, 6, 0, 0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Button refresh = UiKit.Button("刷新", 88);
            refresh.Dock = DockStyle.Fill;
            refresh.Margin = new Padding(0, 0, 8, 0);
            refresh.Click += delegate { Reload(); };
            bar.Controls.Add(refresh, 0, 0);

            Button folder = UiKit.Button("打开所在目录", 132);
            folder.Dock = DockStyle.Fill;
            folder.Margin = new Padding(0, 0, 8, 0);
            folder.Click += delegate { OpenFolder(); };
            bar.Controls.Add(folder, 1, 0);

            Button copy = UiKit.Button("复制全部", 88);
            copy.Dock = DockStyle.Fill;
            copy.Margin = new Padding(0, 0, 8, 0);
            copy.Click += delegate { CopyAll(); };
            bar.Controls.Add(copy, 2, 0);

            Button close = UiKit.PrimaryButton("关闭", 88);
            close.Dock = DockStyle.Fill;
            close.Margin = new Padding(8, 0, 0, 0);
            close.Click += delegate { Close(); };
            bar.Controls.Add(close, 4, 0);

            root.Controls.Add(bar, 0, 2);

            CancelButton = close;
        }

        /// <summary>把日志（最后一段）读进来显示。</summary>
        public void Reload()
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
                {
                    _box.Text = "（还没有日志文件。日志开关关掉时不会写，或者刚被清空过。）";
                    _info.Text = _path;
                    return;
                }

                long len;
                string text;
                using (FileStream fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    len = fs.Length;
                    long start = len > MaxTailBytes ? len - MaxTailBytes : 0;
                    fs.Seek(start, SeekOrigin.Begin);

                    byte[] buf = new byte[len - start];
                    int read = fs.Read(buf, 0, buf.Length);
                    text = new UTF8Encoding(false).GetString(buf, 0, read);
                    if (start > 0)
                        text = "……（日志较长，这里只显示最后 " + (MaxTailBytes / 1024) + " KB）\r\n\r\n" + text;
                }

                _box.Text = text;
                _box.SelectionStart = _box.TextLength;
                _box.ScrollToCaret();

                _info.Text = _path + "    " + (len / 1024) + " KB";
            }
            catch (Exception ex)
            {
                _box.Text = "读取日志失败：" + ex.Message;
            }
        }

        private void CopyAll()
        {
            try
            {
                if (_box.TextLength == 0) return;
                Clipboard.SetText(_box.Text);
                MessageBox.Show(this, "已复制全部内容到剪贴板。", "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：" + ex.Message, "SCPSLAlert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFolder()
        {
            if (NativeMethods.OpenContainingFolder(_path)) return;

            MessageBox.Show(this,
                "打不开资源管理器（可能被系统策略挡住了）。\r\n\r\n日志文件就在这里：\r\n" + _path,
                "SCPSLAlert", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
