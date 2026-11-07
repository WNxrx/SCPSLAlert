using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal sealed class TextPromptForm : Form
    {
        public string Value;

        public static string Ask(IWin32Window owner, string title, string message, string initial)
        {
            using (TextPromptForm f = new TextPromptForm(title, message, initial))
            {
                DialogResult r = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                return r == DialogResult.OK ? f.Value : null;
            }
        }

        private TextPromptForm(string title, string message, string initial)
        {
            Text = title;
            Font = UiKit.Font;
            BackColor = UiKit.PageBg;
            ForeColor = UiKit.TextColor;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 196);

            Label head = new Label();
            head.Text = title;
            head.Font = UiKit.FontTitle;
            head.ForeColor = UiKit.TextColor;
            head.AutoSize = true;
            head.Location = new Point(22, 18);
            Controls.Add(head);

            Label body = new Label();
            body.Text = message;
            body.Font = UiKit.Font;
            body.ForeColor = UiKit.SubColor;
            body.AutoSize = false;
            body.Size = new Size(420, 22);
            body.Location = new Point(24, 54);
            Controls.Add(body);

            TextBox box = UiKit.Text(420);
            box.Text = initial ?? "";
            box.Location = new Point(24, 86);
            box.Height = 28;
            Controls.Add(box);

            Button cancel = UiKit.Button("取消", 96);
            cancel.Location = new Point(246, 140);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Button ok = UiKit.PrimaryButton("确定", 96);
            ok.Location = new Point(352, 140);
            ok.Click += delegate { Value = box.Text; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { box.Focus(); box.SelectAll(); };

            ModernDialogSkin.Apply(this);
        }
    }
}
