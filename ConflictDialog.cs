using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    internal enum ConflictChoice
    {
        Cancel,
        Replace,
        Swap
    }

    /// <summary>
    /// 快捷键冲突时弹的小窗口。
    ///
    /// 用户把某个键设成已经被别的功能占用的组合时，不替他做决定，
    /// 而是把三种处理方式摆出来让他自己选。
    /// </summary>
    internal class ConflictDialog : Form
    {
        public ConflictChoice Choice = ConflictChoice.Cancel;

        public ConflictDialog(string key, string otherName, string myOldKey)
        {
            Text = "快捷键冲突";
            Font = UiKit.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 214);
            BackColor = UiKit.PageBg;
            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            Label title = new Label();
            title.Text = "这个组合键已经被占用了";
            title.Font = new Font(UiKit.Font.FontFamily, 11f, FontStyle.Bold);
            title.ForeColor = UiKit.TextColor;
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            Controls.Add(title);

            Label msg = new Label();
            msg.Text = "【" + key + "】现在用于「" + otherName + "」。\n\n请选择怎么处理：";
            msg.ForeColor = UiKit.TextColor;
            msg.Font = UiKit.Font;
            msg.AutoSize = false;
            msg.Size = new Size(394, 52);
            msg.Location = new Point(20, 46);
            Controls.Add(msg);

            int y = 104;

            Button replace = UiKit.PrimaryButton("替换（把原来那个清空）", 194);
            replace.Location = new Point(18, y);
            replace.Click += delegate { Choice = ConflictChoice.Replace; Close(); };
            Controls.Add(replace);

            Button swap = UiKit.Button("交换两者", 194);
            swap.Location = new Point(220, y);
            swap.Enabled = !string.IsNullOrEmpty(myOldKey);
            if (swap.Enabled)
                swap.Click += delegate { Choice = ConflictChoice.Swap; Close(); };
            Controls.Add(swap);

            Label hint = new Label();
            hint.Text = string.IsNullOrEmpty(myOldKey)
                ? "「交换」需要这一行原本就有按键。"
                : "「交换」＝ 原来是【" + myOldKey + "】，两个功能把键对调。";
            hint.ForeColor = UiKit.SubColor;
            hint.Font = UiKit.Font;
            hint.AutoSize = true;
            hint.Location = new Point(20, y + 40);
            Controls.Add(hint);

            Button cancel = UiKit.Button("取消（不更改）", 140);
            cancel.Location = new Point(274, y + 70);
            cancel.Click += delegate { Choice = ConflictChoice.Cancel; Close(); };
            Controls.Add(cancel);
        }
    }
}
