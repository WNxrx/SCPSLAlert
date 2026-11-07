using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>查看程序日志前选择要打开的类别。</summary>
    internal sealed class LogChoiceForm : Form
    {
        public string SelectedPath = "";

        public LogChoiceForm(string[] names, string[] paths)
        {
            Text = "选择要查看的日志";
            Font = UiKit.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiKit.PageBg;
            ClientSize = new Size(430, 104 + names.Length * 48);
            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            Label title = new Label();
            title.Text = "你想查看哪一类日志？";
            title.Font = new Font(UiKit.Font.FontFamily, 11f, FontStyle.Bold);
            title.ForeColor = UiKit.TextColor;
            title.AutoSize = true;
            title.Location = new Point(20, 15);
            Controls.Add(title);

            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                Button button = i == 0 ? UiKit.PrimaryButton(names[i], 386) : UiKit.Button(names[i], 386);
                button.Location = new Point(20, 50 + i * 48);
                button.Click += delegate
                {
                    SelectedPath = paths[index];
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(button);
            }
        }
    }
}
