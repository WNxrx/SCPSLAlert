using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 新建 / 编辑一条触发点。
    /// 左边条件 → 右边动作：条件满足时「播放一段音频」或「停止正在播的音频」。
    /// </summary>
    public class AudioTriggerForm : Form
    {
        private class Preset
        {
            public string Label = "";
            public string Event = "";
            public string Op = "<=";
            public string ArgMode = "";         // "" / scp / alert
            public double Value = 60;
            public bool NeedsValue = true;
        }

        private readonly AudioTrigger _t;

        /// <summary>音频候选：Text = 下拉框里显示的文字，Key = 真正写进配置的值。</summary>
        public class ClipChoice
        {
            public string Text = "";
            public string Key = "";
        }

        private readonly List<ClipChoice> _clipChoices;
        private readonly string[] _alertNames;
        private readonly string[] _scpCodes;
        private readonly List<Preset> _presets = new List<Preset>();

        private TextBox _txtName;
        private CheckBox _chkEnabled;
        private RadioButton _radPlay, _radStop;

        private ComboBox _cboMatch;
        private ComboBox _cboPreset, _cboPresetArg;
        private NumericUpDown _numPresetValue;
        private Label _lblPresetUnit, _lblPresetHint;

        private ComboBox _cboEvent, _cboFaction, _cboOp;
        private TextBox _txtArg;
        private NumericUpDown _numValue;
        private Label _lblArgHint;
        private ListBox _lstConds;

        private GroupBox _grpAudio;
        private ComboBox _cboClip;
        private NumericUpDown _numFadeIn, _numFadeOut;

        private NumericUpDown _numCooldown;
        private CheckBox _chkOnce;

        public AudioTrigger Result { get; private set; }

        public AudioTriggerForm(AudioTrigger t, string[] clips)
            : this(t, Plain(clips), null, null) { }

        public AudioTriggerForm(AudioTrigger t, string[] clips,
            string[] countdownAlerts, string[] scpCodes)
            : this(t, Plain(clips), countdownAlerts, scpCodes) { }

        /// <summary>字符串数组的兼容入口：显示文字和实际值一样。</summary>
        private static List<ClipChoice> Plain(string[] clips)
        {
            List<ClipChoice> list = new List<ClipChoice>();
            if (clips == null) return list;
            for (int i = 0; i < clips.Length; i++)
                list.Add(new ClipChoice { Text = clips[i], Key = clips[i] });
            return list;
        }

        /// <summary>
        /// clips 里既有「音频」文件夹里的文件，也有在软件里裁好但**没导出**的片段
        /// （@名字）—— 两者显示文字不同、写进配置的值也不同，所以要成对传。
        /// </summary>
        public AudioTriggerForm(AudioTrigger t, List<ClipChoice> clips,
            string[] countdownAlerts, string[] scpCodes)
        {
            _t = t ?? new AudioTrigger();
            _clipChoices = clips ?? new List<ClipChoice>();
            _alertNames = countdownAlerts ?? new string[0];
            _scpCodes = scpCodes == null || scpCodes.Length == 0
                ? TacticalLog.ScpCodes : scpCodes;

            Text = "触发点 —— " + _t.Name;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 672);
            BackColor = UiKit.PageBg;
            Font = UiKit.Font;
            try { if (AlertApplication.SharedIcon != null) Icon = AlertApplication.SharedIcon; }
            catch { }

            BuildUi();
            LoadFromModel();
        }

        private void BuildUi()
        {
            Controls.Add(Title("名称", 16, 20, 50));
            _txtName = UiKit.Text(200);
            _txtName.Location = new Point(70, 16);
            Controls.Add(_txtName);

            _chkEnabled = UiKit.Check("启用这条触发点", 140);
            _chkEnabled.Location = new Point(286, 18);
            Controls.Add(_chkEnabled);

            Controls.Add(Title("满足条件时", 16, 62, 90));
            _radPlay = new RadioButton();
            _radPlay.Text = "播放一段音频";
            _radPlay.Location = new Point(110, 60);
            _radPlay.AutoSize = true;
            _radPlay.Checked = true;
            _radPlay.ForeColor = UiKit.TextColor;
            _radPlay.CheckedChanged += delegate { SyncAction(); };
            Controls.Add(_radPlay);

            _radStop = new RadioButton();
            _radStop.Text = "停止正在播的音频";
            _radStop.Location = new Point(250, 60);
            _radStop.AutoSize = true;
            _radStop.ForeColor = UiKit.TextColor;
            _radStop.CheckedChanged += delegate { SyncAction(); };
            Controls.Add(_radStop);

            // ---- 条件 ----
            GroupBox gC = new GroupBox();
            gC.Text = "条件";
            gC.Font = UiKit.Font;
            gC.ForeColor = UiKit.TextColor;
            gC.BackColor = Color.White;
            gC.Location = new Point(16, 92);
            gC.Size = new Size(688, 320);
            Controls.Add(gC);

            Label lMatch = Title("触发方式", 14, 26, 70);
            gC.Controls.Add(lMatch);
            _cboMatch = UiKit.Combo(false, 130);
            _cboMatch.Location = new Point(86, 24);
            _cboMatch.Items.AddRange(new object[] { "全部条件满足", "任一条件满足" });
            _cboMatch.SelectedIndex = 0;
            gC.Controls.Add(_cboMatch);

            Label lQuick = Title("常用（一键添加）", 14, 62, 116);
            gC.Controls.Add(lQuick);
            _cboPreset = UiKit.Combo(false, 230);
            _cboPreset.Location = new Point(130, 60);
            _cboPreset.SelectedIndexChanged += delegate { SyncPreset(); };
            gC.Controls.Add(_cboPreset);

            _cboPresetArg = UiKit.Combo(false, 130);
            _cboPresetArg.Location = new Point(368, 60);
            _cboPresetArg.Visible = false;
            gC.Controls.Add(_cboPresetArg);

            _numPresetValue = UiKit.Num(60, 0, 100000, 78);
            _numPresetValue.Location = new Point(506, 60);
            gC.Controls.Add(_numPresetValue);

            _lblPresetUnit = Title("秒", 590, 62, 20);
            gC.Controls.Add(_lblPresetUnit);

            Button addQuick = UiKit.Button("加入", 70);
            addQuick.Location = new Point(612, 59);
            addQuick.Click += delegate { AddPreset(); };
            gC.Controls.Add(addQuick);

            _lblPresetHint = Title("", 130, 90);
            _lblPresetHint.ForeColor = UiKit.SubColor;
            _lblPresetHint.Font = UiKit.FontSmall;
            _lblPresetHint.Width = 540;
            gC.Controls.Add(_lblPresetHint);

            Label lManual = Title("或者手动选事件", 14, 116, 116);
            gC.Controls.Add(lManual);

            _cboEvent = UiKit.Combo(false, 250);
            _cboEvent.Location = new Point(14, 140);
            _cboEvent.SelectedIndexChanged += delegate { SyncEventFields(); };
            gC.Controls.Add(_cboEvent);

            _cboFaction = UiKit.Combo(false, 80);
            _cboFaction.Location = new Point(272, 140);
            _cboFaction.Items.AddRange(new object[] { "任意", "MTF", "混沌" });
            _cboFaction.SelectedIndex = 0;
            gC.Controls.Add(_cboFaction);

            _txtArg = UiKit.Text(80);
            _txtArg.Location = new Point(360, 140);
            gC.Controls.Add(_txtArg);

            _cboOp = UiKit.Combo(false, 110);
            _cboOp.Location = new Point(448, 140);
            _cboOp.Items.AddRange(AudioCond.OpNames);
            _cboOp.SelectedIndex = 1;
            gC.Controls.Add(_cboOp);

            _numValue = UiKit.Num(1, -100000, 100000, 76);
            _numValue.Location = new Point(566, 140);
            gC.Controls.Add(_numValue);

            Button addCond = UiKit.Button("加入", 70);
            addCond.Location = new Point(612, 170);
            addCond.Click += delegate { AddCond(); };
            gC.Controls.Add(addCond);

            _lblArgHint = Title("", 272, 168);
            _lblArgHint.ForeColor = UiKit.SubColor;
            _lblArgHint.Font = UiKit.FontSmall;
            _lblArgHint.Width = 330;
            gC.Controls.Add(_lblArgHint);

            _lstConds = new ListBox();
            _lstConds.Font = UiKit.Font;
            _lstConds.BackColor = Color.White;
            _lstConds.Location = new Point(14, 196);
            _lstConds.Size = new Size(540, 108);
            gC.Controls.Add(_lstConds);

            Button del = UiKit.Button("删除选中", 96);
            del.Location = new Point(564, 196);
            del.Click += delegate
            {
                int i = _lstConds.SelectedIndex;
                if (i >= 0 && i < _t.Conds.Count) { _t.Conds.RemoveAt(i); RefreshConds(); }
            };
            gC.Controls.Add(del);

            Button clr = UiKit.Button("清空", 96);
            clr.Location = new Point(564, 232);
            clr.Click += delegate
            {
                if (_t.Conds.Count == 0) return;
                _t.Conds.Clear();
                RefreshConds();
            };
            gC.Controls.Add(clr);

            // ---- 音频 ----
            _grpAudio = new GroupBox();
            _grpAudio.Text = "播放的音频（停止动作不用选）";
            _grpAudio.Font = UiKit.Font;
            _grpAudio.ForeColor = UiKit.TextColor;
            _grpAudio.BackColor = Color.White;
            _grpAudio.Location = new Point(16, 422);
            _grpAudio.Size = new Size(688, 104);
            Controls.Add(_grpAudio);

            Label lClip = Title("用哪一段", 14, 26, 70);
            _grpAudio.Controls.Add(lClip);
            _cboClip = UiKit.Combo(false, 300);
            _cboClip.Location = new Point(86, 24);
            for (int i = 0; i < _clipChoices.Count; i++) _cboClip.Items.Add(_clipChoices[i].Text);
            _grpAudio.Controls.Add(_cboClip);

            Label lClipHint = Title("列表里「@名字」就是你在「音频」页裁好的片段（不用导出）", 86, 84, 520);
            lClipHint.ForeColor = UiKit.SubColor;
            lClipHint.Font = UiKit.FontSmall;
            _grpAudio.Controls.Add(lClipHint);

            Button test = UiKit.Button("试听", 70);
            test.Location = new Point(396, 23);
            test.Click += delegate { TestClip(); };
            _grpAudio.Controls.Add(test);

            Label lFade = Title("渐入 / 渐出", 14, 62, 94);
            _grpAudio.Controls.Add(lFade);
            _numFadeIn = UiKit.Num(0, 0, 30, 70);
            _numFadeIn.DecimalPlaces = 1;
            _numFadeIn.Increment = 0.5m;
            _numFadeIn.Location = new Point(110, 60);
            _grpAudio.Controls.Add(_numFadeIn);
            Label lF1 = Title("秒 /", 186, 62, 36);
            _grpAudio.Controls.Add(lF1);
            _numFadeOut = UiKit.Num(0, 0, 30, 70);
            _numFadeOut.DecimalPlaces = 1;
            _numFadeOut.Increment = 0.5m;
            _numFadeOut.Location = new Point(226, 60);
            _grpAudio.Controls.Add(_numFadeOut);
            Label lF2 = Title("秒（0 = 不渐变）", 302, 62, 120);
            _grpAudio.Controls.Add(lF2);

            // ---- 节奏 ----
            GroupBox gT = new GroupBox();
            gT.Text = "触发节奏";
            gT.Font = UiKit.Font;
            gT.ForeColor = UiKit.TextColor;
            gT.BackColor = Color.White;
            gT.Location = new Point(16, 536);
            gT.Size = new Size(688, 70);
            Controls.Add(gT);

            Label lCd = Title("最小间隔", 14, 28, 70);
            gT.Controls.Add(lCd);
            _numCooldown = UiKit.Num(3, 0, 600, 70);
            _numCooldown.Location = new Point(86, 26);
            gT.Controls.Add(_numCooldown);
            Label lCd2 = Title("秒", 162, 28, 30);
            gT.Controls.Add(lCd2);

            _chkOnce = UiKit.Check("一局只执行一次", 150);
            _chkOnce.Location = new Point(206, 28);
            gT.Controls.Add(_chkOnce);

            Button ok = UiKit.PrimaryButton("确定", 96);
            ok.Location = new Point(506, 622);
            ok.Click += delegate { OnOk(); };
            Controls.Add(ok);

            Button cancel = UiKit.Button("取消", 96);
            cancel.Location = new Point(608, 622);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            BuildPresets();
            FillEvents();
        }

        private static Label Title(string text, int x, int y, int w = 92)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Location = new Point(x, y + 4);
            l.Size = new Size(w, 22);
            l.ForeColor = UiKit.TextColor;
            l.BackColor = Color.Transparent;
            return l;
        }

        private void BuildPresets()
        {
            AddPresetDef("核弹剩余到点", "warhead.remain", "<=", "", 60, true);
            AddPresetDef("净化剩余到点", "decon.remain", "<=", "", 60, true);
            AddPresetDef("终焉（亡者协议）剩余到点", "deadman.remain", "<=", "", 60, true);
            AddPresetDef("某条提示的倒计时到点", "alert.remain", "<=", "alert", 60, true);
            AddPresetDef("下一波刷新剩余到点", "wave.next.remain", "<=", "", 60, true);
            AddPresetDef("本局第几波", "wave.count", "==", "", 3, true);
            AddPresetDef("已启动发电机达到", "gen.active", ">=", "", 1, true);
            AddPresetDef("某个 SCP 刚刚阵亡", "pulse.scp.killed", "==", "scp", 1, false);
            AddPresetDef("自己刚刚阵亡", "pulse.self.death", "==", "", 1, false);
            AddPresetDef("自己刚刚重生", "pulse.self.respawn", "==", "", 1, false);
            AddPresetDef("刚刚进入服务器", "pulse.server.join", "==", "", 1, false);
            AddPresetDef("刚刚离开服务器", "pulse.server.leave", "==", "", 1, false);
            AddPresetDef("回合刚刚开始", "pulse.round.start", "==", "", 1, false);
            AddPresetDef("回合刚刚结束", "pulse.round.end", "==", "", 1, false);
            AddPresetDef("核弹刚刚开启", "pulse.warhead.start", "==", "", 1, false);
            AddPresetDef("核弹刚刚引爆", "pulse.warhead.detonated", "==", "", 1, false);
            AddPresetDef("净化刚刚开始计时", "pulse.decon.start", "==", "", 1, false);
            AddPresetDef("三台发电机刚刚就绪", "pulse.gen.allready", "==", "", 1, false);

            for (int i = 0; i < _presets.Count; i++) _cboPreset.Items.Add(_presets[i].Label);
            _cboPreset.SelectedIndex = 0;
        }

        private void AddPresetDef(string label, string ev, string op,
            string argMode, double value, bool needsValue)
        {
            Preset p = new Preset();
            p.Label = label;
            p.Event = ev;
            p.Op = op;
            p.ArgMode = argMode;
            p.Value = value;
            p.NeedsValue = needsValue;
            _presets.Add(p);
        }

        private Preset CurrentPreset()
        {
            int i = _cboPreset.SelectedIndex;
            return (i >= 0 && i < _presets.Count) ? _presets[i] : null;
        }

        private void SyncPreset()
        {
            Preset p = CurrentPreset();
            if (p == null) return;

            _cboPresetArg.Items.Clear();
            _cboPresetArg.Visible = p.ArgMode.Length > 0;

            if (p.ArgMode == "alert")
            {
                for (int i = 0; i < _alertNames.Length; i++) _cboPresetArg.Items.Add(_alertNames[i]);
                if (_cboPresetArg.Items.Count > 0) _cboPresetArg.SelectedIndex = 0;
            }
            else if (p.ArgMode == "scp")
            {
                for (int i = 0; i < _scpCodes.Length; i++) _cboPresetArg.Items.Add(_scpCodes[i]);
                if (_cboPresetArg.Items.Count > 0) _cboPresetArg.SelectedIndex = 0;
            }

            _numPresetValue.Enabled = p.NeedsValue;
            _numPresetValue.Value = (decimal)Math.Max(0, Math.Min(100000, p.Value));

            AudioEventInfo info = AudioEvents.Find(p.Event);
            bool seconds = info != null && info.Unit == "秒";
            _lblPresetUnit.Visible = seconds;

            string hint = "条件：" + p.Label;
            if (p.ArgMode == "scp") hint += " —— 选具体编号";
            else if (p.ArgMode == "alert") hint += " —— 先选哪条提示";
            _lblPresetHint.Text = hint;
        }

        private void FillEvents()
        {
            List<AudioEventInfo> all = AudioEvents.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0 && all[i - 1].Group != all[i].Group)
                    _cboEvent.Items.Add("—— " + all[i].Group + " ——");
                _cboEvent.Items.Add(all[i].Name + "（" + all[i].Id + "）");
            }
            for (int i = 0; i < _cboEvent.Items.Count; i++)
                if (!_cboEvent.Items[i].ToString().StartsWith("——"))
                {
                    _cboEvent.SelectedIndex = i;
                    break;
                }
        }

        private AudioEventInfo SelectedEvent()
        {
            if (_cboEvent.SelectedIndex < 0) return null;
            string s = _cboEvent.Items[_cboEvent.SelectedIndex].ToString();
            if (s.StartsWith("——")) return null;
            int a = s.LastIndexOf('（'), b = s.LastIndexOf('）');
            if (a < 0 || b <= a) return null;
            return AudioEvents.Find(s.Substring(a + 1, b - a - 1));
        }

        private void SyncEventFields()
        {
            if (_cboFaction == null || _txtArg == null || _lblArgHint == null
                || _cboOp == null || _numValue == null) return;

            AudioEventInfo e = SelectedEvent();
            bool has = e != null;
            _cboFaction.Enabled = has && e.NeedsFaction;
            _txtArg.Enabled = has && e.NeedsArg;
            _lblArgHint.Text = has && e.NeedsArg ? e.ArgHint : "";
            if (has && e.IsPulse)
            {
                _cboOp.SelectedItem = "等于";
                _numValue.Value = 1;
            }
        }

        private void SyncAction()
        {
            if (_grpAudio == null) return;
            bool play = _radPlay.Checked;
            _grpAudio.Enabled = play;
            _cboClip.Enabled = play;
        }

        private void AddPreset()
        {
            Preset p = CurrentPreset();
            if (p == null) return;

            AudioCond c = new AudioCond();
            c.Event = p.Event;
            c.Op = p.Op;
            c.Value = p.NeedsValue ? (double)_numPresetValue.Value : 1;

            if (p.ArgMode == "scp")
            {
                if (_cboPresetArg.SelectedIndex < 0)
                {
                    MessageBox.Show(this, "先选一个 SCP 编号。", "触发点");
                    return;
                }
                c.Arg = _cboPresetArg.SelectedItem.ToString();
            }
            else if (p.ArgMode == "alert")
            {
                if (_cboPresetArg.SelectedIndex < 0)
                {
                    MessageBox.Show(this, "先选一条提示（设置里要有倒计时类提示）。", "触发点");
                    return;
                }
                c.Arg = _cboPresetArg.SelectedItem.ToString();
            }

            _t.Conds.Add(c);
            RefreshConds();
        }

        private void AddCond()
        {
            AudioEventInfo e = SelectedEvent();
            if (e == null)
            {
                MessageBox.Show(this, "先选一个事件。", "触发点");
                return;
            }

            AudioCond c = new AudioCond();
            c.Event = e.Id;
            if (e.NeedsFaction && _cboFaction.SelectedIndex > 0)
                c.Faction = _cboFaction.SelectedIndex == 1 ? TacticalLog.MTF : TacticalLog.CHAOS;
            if (e.NeedsArg) c.Arg = _txtArg.Text.Trim();
            c.Op = _cboOp.SelectedItem == null
                ? ">=" : AudioCond.OpFromLabel(_cboOp.SelectedItem.ToString());
            c.Value = (double)_numValue.Value;
            _t.Conds.Add(c);
            RefreshConds();
        }

        private void RefreshConds()
        {
            _lstConds.BeginUpdate();
            _lstConds.Items.Clear();
            for (int i = 0; i < _t.Conds.Count; i++)
                _lstConds.Items.Add((i + 1) + ". " + _t.Conds[i].Text());
            _lstConds.EndUpdate();
        }

        private void LoadFromModel()
        {
            _txtName.Text = _t.Name;
            _chkEnabled.Checked = _t.Enabled;
            _radStop.Checked = _t.IsStop;
            _radPlay.Checked = !_t.IsStop;
            _cboMatch.SelectedIndex = _t.MatchAny ? 1 : 0;
            ShowClip(_t.Clip);            _numFadeIn.Value = (decimal)Math.Max(0, Math.Min(30, _t.FadeIn));
            _numFadeOut.Value = (decimal)Math.Max(0, Math.Min(30, _t.FadeOut));
            _numCooldown.Value = Math.Max(0, Math.Min(600, _t.CooldownSeconds));
            _chkOnce.Checked = _t.OncePerRound;
            RefreshConds();
            SyncEventFields();
            SyncPreset();
            SyncAction();
        }

        /// <summary>下拉框里显示的文字 → 真正写进配置的值（手打的文件名原样返回）。</summary>
        private string KeyOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Trim();
            for (int i = 0; i < _clipChoices.Count; i++)
                if (string.Equals(_clipChoices[i].Text, s, StringComparison.OrdinalIgnoreCase))
                    return _clipChoices[i].Key;
            return s;
        }

        /// <summary>配置里的值 → 下拉框里对应的那一项（找不到就原样显示）。</summary>
        private void ShowClip(string key)
        {
            if (string.IsNullOrEmpty(key)) { _cboClip.SelectedIndex = -1; return; }
            for (int i = 0; i < _clipChoices.Count; i++)
                if (string.Equals(_clipChoices[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    _cboClip.SelectedIndex = i;
                    return;
                }
            _cboClip.Items.Add(key);
            _cboClip.SelectedIndex = _cboClip.Items.Count - 1;
        }

        private void TestClip()
        {
            string clip = KeyOf(_cboClip.Text);
            if (clip.Length == 0)
            {
                MessageBox.Show(this, "先选一段音频。", "试听");
                return;
            }
            if (!AudioLibrary.PlayKey(clip, "触发点试听"))
                MessageBox.Show(this, "这段播不出来：" + AudioLibrary.LastError
                    + "\n\n详细原因见 SCPSLAlert.log（搜「MCI 打开失败」）。"
                    + "实在放不了就把它转成 wav 再放进「音频」文件夹。", "试听");
        }

        private void OnOk()
        {
            if ((_txtName.Text ?? "").Trim().Length == 0)
            {
                MessageBox.Show(this, "给触发点起个名字。", "触发点");
                return;
            }
            if (_t.Conds.Count == 0)
            {
                MessageBox.Show(this, "至少要加一条条件。", "触发点");
                return;
            }
            bool stop = _radStop.Checked;
            string clipKey = KeyOf(_cboClip.Text);
            if (!stop && clipKey.Length == 0)
            {
                MessageBox.Show(this, "选一段要播放的音频。", "触发点");
                return;
            }

            AudioTrigger r = _t.Clone();
            r.Name = _txtName.Text.Trim();
            r.Clip = stop ? "" : clipKey;
            r.CooldownSeconds = (int)_numCooldown.Value;
            r.OncePerRound = _chkOnce.Checked;
            r.Enabled = _chkEnabled.Checked;
            r.MatchAny = _cboMatch.SelectedIndex == 1;
            r.IsStop = stop;
            r.FadeIn = (double)_numFadeIn.Value;
            r.FadeOut = (double)_numFadeOut.Value;
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
