using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>点 × 之后用户选了什么。</summary>
    internal enum CloseChoice
    {
        /// <summary>最小化到托盘（程序继续在后台跑）。</summary>
        Minimize = 0,
        /// <summary>直接退出程序。</summary>
        Exit = 1,
        /// <summary>取消：什么都不做，窗口留着。</summary>
        Cancel = 2
    }

    /// <summary>
    /// 第一次点 × 时问一次"要最小化还是退出"。
    ///
    /// 用自定义窗口而不是 MessageBox，是因为要放三个按钮 + 一个"记住选择"。
    /// 选择会被存进 config.ini 的 CloseAction，之后就不再问了；
    /// 想改随时去托盘右键菜单的「关闭按钮的行为」。
    /// </summary>
    internal class CloseConfirmForm : Form
    {
        public CloseChoice Choice = CloseChoice.Cancel;

        /// <summary>用户有没有勾"记住我的选择"。</summary>
        public bool Remember = true;

        private CheckBox _chkRemember;

        public CloseConfirmForm(bool canMinimize)
        {
            Text = "SCPSLAlert · 关闭确认";
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            ForeColor = UiKit.TextColor;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(452, 208);

            BuildUi(canMinimize);
        }

        private void BuildUi(bool canMinimize)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(18, 16, 18, 14);
            root.BackColor = UiKit.PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "要最小化到托盘，还是退出程序？";
            title.Font = UiKit.FontTitle;
            title.ForeColor = UiKit.TextColor;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(title, 0, 0);

            Label body = new Label();
            body.Font = UiKit.Font;
            body.ForeColor = UiKit.SubColor;
            body.Dock = DockStyle.Fill;
            body.TextAlign = ContentAlignment.TopLeft;
            body.Text = "最小化：程序继续在后台跑（倒计时、热键、屏幕识别都照常），"
                      + "只是窗口收进托盘；\r\n"
                      + "退出：彻底关掉程序，之后要重新双击 exe 才会再运行。";
            root.Controls.Add(body, 0, 1);

            _chkRemember = UiKit.Check("记住我的选择（以后不再问，可在托盘右键菜单里改）", 400);
            _chkRemember.Checked = true;
            _chkRemember.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            root.Controls.Add(_chkRemember, 0, 2);

            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 4;
            bar.RowCount = 1;
            bar.Margin = new Padding(0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            if (canMinimize)
            {
                Button min = UiKit.PrimaryButton("最小化到托盘", 110);
                min.Dock = DockStyle.Fill;
                min.Margin = new Padding(6, 4, 0, 4);
                min.Click += delegate { Finish(CloseChoice.Minimize); };
                bar.Controls.Add(min, 1, 0);
            }

            Button exit = UiKit.Button("退出程序", 110);
            exit.Dock = DockStyle.Fill;
            exit.Margin = new Padding(6, 4, 0, 4);
            exit.Click += delegate { Finish(CloseChoice.Exit); };
            bar.Controls.Add(exit, 2, 0);

            Button cancel = UiKit.Button("取消", 90);
            cancel.Dock = DockStyle.Fill;
            cancel.Margin = new Padding(6, 4, 0, 4);
            cancel.Click += delegate { Finish(CloseChoice.Cancel); };
            bar.Controls.Add(cancel, 3, 0);

            root.Controls.Add(bar, 0, 3);

            CancelButton = cancel;

            ModernDialogSkin.Apply(this);
        }

        private void Finish(CloseChoice c)
        {
            Choice = c;
            Remember = _chkRemember.Checked;
            DialogResult = (c == CloseChoice.Cancel) ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }
    }
}
