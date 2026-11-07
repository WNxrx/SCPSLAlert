using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SCPSLAlert
{
    // ============================================================
    // 自定义音频触发点（操作者要求：音效系统要更丰富的自由度）
    //
    // 一条触发点 = 名称 + **一组条件（全部满足才算，AND）** + 一个音频条目。
    // 条件从「软件能读到的事件」里选，可以带阵营 / 编号 / 数值比较，
    // 所以"079 第 2 台发电机启动""核弹开启前 60 秒""终焉归零"这种组合都能表达 ——
    // 以前那种"分台音频"就是这里的一个普通条件，不用再单独做一套。
    //
    // 存储：config.ini 的 [AudioTriggers] 段，一行一条（管道分隔），
    //      这样加字段也不会把老配置读崩（缺的字段按默认值）。
    // ============================================================

    /// <summary>一条条件：某事件（带阵营/编号）与一个数值的比较。</summary>
    public class AudioCond
    {
        /// <summary>事件 id，见 <see cref="AudioEvents"/>。</summary>
        public string Event = "";

        /// <summary>阵营："" = 任意，或 MTF / Chaos。</summary>
        public string Faction = "";

        /// <summary>附加参数：SCP 编号（"079"）、第几台（"1"）等；不需要就留空。</summary>
        public string Arg = "";

        /// <summary>比较符：&lt;= &gt;= == != &gt; &lt;（脉冲类事件只会是 1/0）。</summary>
        public string Op = ">=";

        public double Value = 1;

        /// <summary>
        /// 中文比较符（操作者要求：比 &gt;= / == 直观）。
        /// 下标和 <see cref="AudioEvents.Ops"/> 一一对应。
        /// </summary>
        public static readonly string[] OpNames =
        {
            "小于或等于", "大于或等于", "等于", "不等于", "大于", "小于"
        };

        /// <summary>比较符 → 中文名（认不出就原样返回）。</summary>
        public static string OpLabel(string op)
        {
            if (string.IsNullOrEmpty(op)) return op;
            string[] ops = AudioEvents.Ops;
            for (int i = 0; i < ops.Length; i++)
                if (ops[i] == op) return OpNames[i];
            return op;
        }

        /// <summary>中文名 → 比较符（认不出就原样返回）。</summary>
        public static string OpFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return ">=";
            for (int i = 0; i < OpNames.Length; i++)
                if (OpNames[i] == label) return AudioEvents.Ops[i];
            // 兼容直接给符号的情况
            for (int i = 0; i < AudioEvents.Ops.Length; i++)
                if (AudioEvents.Ops[i] == label) return label;
            return ">=";
        }

        public bool Eval(double actual)
        {
            switch (Op)
            {
                case "<=": return actual <= Value;
                case ">=": return actual >= Value;
                case "==": return Math.Abs(actual - Value) < 0.0001;
                case "!=": return Math.Abs(actual - Value) >= 0.0001;
                case ">": return actual > Value;
                case "<": return actual < Value;
                default: return actual >= Value;
            }
        }

        public AudioCond Clone()
        {
            return new AudioCond
            {
                Event = Event, Faction = Faction, Arg = Arg, Op = Op, Value = Value
            };
        }

        /// <summary>给界面/日志看的一行文字，例如「核弹剩余 &lt;= 60 秒」。</summary>
        public string Text()
        {
            AudioEventInfo info = AudioEvents.Find(Event);
            string name = info == null ? Event : info.Name;
            string tail = "";
            if (!string.IsNullOrEmpty(Faction)) tail += " · " + FactionName(Faction);
            if (!string.IsNullOrEmpty(Arg)) tail += " · " + Arg;
            string unit = info == null ? "" : info.Unit;
            // 秒数类（倒计时）条件说人话：「核弹倒计时 剩余降到 60 秒」
            if (unit == "秒")
            {
                string baseName = name.Replace("（秒）", "");
                string sub = tail;
                if (Event == "alert.remain" && !string.IsNullOrEmpty(Arg))
                {
                    baseName = Arg;          // 用具体那条提示当主语，比通用事件名好读
                    sub = "";
                }
                string phrase;
                switch (Op)
                {
                    case "<=": phrase = "剩余降到"; break;
                    case "<": phrase = "剩余低于"; break;
                    case ">=": phrase = "剩余高于"; break;
                    case ">": phrase = "剩余超过"; break;
                    case "==": phrase = "剩余正好"; break;
                    default: phrase = "剩余不等于"; break;
                }
                return baseName + sub + " " + phrase + " " + Num(Value) + " 秒";
            }
            return name + tail + " " + OpLabel(Op) + " " + Num(Value) + (unit.Length > 0 ? " " + unit : "");
        }

        public static string FactionName(string f)
        {
            if (f == TacticalLog.MTF) return "MTF";
            if (f == TacticalLog.CHAOS) return "混沌";
            return string.IsNullOrEmpty(f) ? "任意" : f;
        }

        private static string Num(double v)
        {
            return (Math.Abs(v - Math.Round(v)) < 0.0001)
                ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 第 7 批：触发点的一个动作（音频 / 图片 / 文字，可同时配多个）。
    /// 位置用 0..1 归一化坐标，基准 = 目标显示器（和提示窗一致）。
    /// </summary>
    /// <summary>第 8 批追加：漂浮文字的逐字样式。</summary>
    public class TriggerCharStyle
    {
        public int Index;
        public string Color = "";          // 空 = 动作主色
        public double JitterX = 2.0;       // 左右抖动上限（px）
        public double JitterY = 5.0;       // 上下抖动上限（px，默认大于左右）
        public double Speed = 1.0;         // 每字抖动速度倍率
        public double Phase = -1;          // <0 = 按序号自动错开
        public double Scale = 1.0;
        public double Rotate = 0;

        public TriggerCharStyle Clone()
        {
            return new TriggerCharStyle
            {
                Index = Index, Color = Color,
                JitterX = JitterX, JitterY = JitterY,
                Speed = Speed, Phase = Phase, Scale = Scale, Rotate = Rotate
            };
        }
    }

    /// <summary>第 8 批追加：一个可单独添加的特效。</summary>
    public class TriggerEffect
    {
        public string Type = "shake";      // shake / flash / confetti / pulse
        public string Label = "";          // 导入特效包的显示名；空 = 用内置名
        public int Amplitude = 8;          // shake
        public int DurationMs = 320;
        public double Intensity = 0.22;    // flash / pulse
        public int Count = 60;             // confetti
        public string Color1 = "#FFFFFF";
        public string Color2 = "#43CFBB";

        public TriggerEffect Clone()
        {
            return new TriggerEffect
            {
                Type = Type, Label = Label,
                Amplitude = Amplitude, DurationMs = DurationMs,
                Intensity = Intensity, Count = Count, Color1 = Color1, Color2 = Color2
            };
        }

        public string Name()
        {
            if (!string.IsNullOrEmpty(Label)) return Label;
            if (Type == "flash") return "闪屏";
            if (Type == "confetti") return "礼炮";
            if (Type == "pulse") return "边缘脉冲";
            return "抖动";
        }
    }

    public class TriggerAction
    {
        public string Kind = "audio";        // audio / image / text / float / effect
        public string Clip = "";             // Kind=audio：音频条目
        public string Image = "";            // Kind=image：PNG / GIF 路径
        public string Text = "";             // Kind=text / float：显示文字
        public string Effect = "solid";      // solid / gradient / char / rainbow / marquee
        public string Color1 = "#FFFFFF";
        public string Color2 = "#43CFBB";
        public bool Outline = true;          // 纯文本默认 1px 反差描边
        public double XPercent = 0.5;        // 0..1，基准=目标显示器
        public double YPercent = 0.5;
        public double Opacity = 1.0;
        public double Scale = 1.0;
        public int MarqueeDir = 0;           // 0 = 左移，1 = 右移
        public double MarqueeSpeed = 1.0;    // 倍速
        public string Font = "";
        public double FontSize = 32;

        // ---- 第 8 批追加：漂浮文字 ----
        public double FloatJitterX = 2.0;    // 整句左右抖动上限
        public double FloatJitterY = 6.0;    // 整句上下抖动上限（默认大于左右）
        public double FloatJitterSpeed = 1.0;
        public bool FloatReveal = true;      // 逐字显现
        public int FloatRevealDelayMs = 40;
        public int FloatLifeMs = 6000;       // 0 = 一直显示到停止条件
        public List<TriggerCharStyle> CharStyles = new List<TriggerCharStyle>();

        // ---- 第 8 批追加：特效 ----
        public string EffectType = "shake";  // 老字段：保留兼容
        public int EffectAmplitude = 8;
        public int EffectDurationMs = 320;
        public List<TriggerEffect> Effects = new List<TriggerEffect>();

        public List<TriggerEffect> EffectiveEffects()
        {
            if (Effects != null && Effects.Count > 0) return Effects;
            List<TriggerEffect> list = new List<TriggerEffect>();
            list.Add(new TriggerEffect
            {
                Type = string.IsNullOrEmpty(EffectType) ? "shake" : EffectType,
                Amplitude = EffectAmplitude,
                DurationMs = EffectDurationMs,
                Intensity = 0.22,
                Count = 60,
                Color1 = "#FFFFFF",
                Color2 = "#43CFBB"
            });
            return list;
        }

        public TriggerAction Clone()
        {
            TriggerAction a = new TriggerAction();
            a.Kind = Kind; a.Clip = Clip; a.Image = Image; a.Text = Text;
            a.Effect = Effect; a.Color1 = Color1; a.Color2 = Color2; a.Outline = Outline;
            a.XPercent = XPercent; a.YPercent = YPercent;
            a.Opacity = Opacity; a.Scale = Scale;
            a.MarqueeDir = MarqueeDir; a.MarqueeSpeed = MarqueeSpeed;
            a.Font = Font; a.FontSize = FontSize;
            a.FloatJitterX = FloatJitterX; a.FloatJitterY = FloatJitterY;
            a.FloatJitterSpeed = FloatJitterSpeed; a.FloatReveal = FloatReveal;
            a.FloatRevealDelayMs = FloatRevealDelayMs; a.FloatLifeMs = FloatLifeMs;
            a.EffectType = EffectType; a.EffectAmplitude = EffectAmplitude;
            a.EffectDurationMs = EffectDurationMs;
            if (Effects != null)
                for (int i = 0; i < Effects.Count; i++)
                    if (Effects[i] != null) a.Effects.Add(Effects[i].Clone());
            if (CharStyles != null)
                for (int i = 0; i < CharStyles.Count; i++)
                    if (CharStyles[i] != null) a.CharStyles.Add(CharStyles[i].Clone());
            return a;
        }

        public TriggerCharStyle StyleFor(int index)
        {
            if (CharStyles == null) CharStyles = new List<TriggerCharStyle>();
            for (int i = 0; i < CharStyles.Count; i++)
                if (CharStyles[i] != null && CharStyles[i].Index == index) return CharStyles[i];
            TriggerCharStyle s = new TriggerCharStyle { Index = index };
            s.Phase = index * 0.7;
            CharStyles.Add(s);
            return s;
        }

        public string KindName()
        {
            if (Kind == "image") return "图片";
            if (Kind == "text") return "文字";
            if (Kind == "float") return "漂浮文字";
            if (Kind == "effect") return "特效";
            return "音频";
        }

        public string Summary()
        {
            if (Kind == "image") return "图片 " + System.IO.Path.GetFileName(Image);
            if (Kind == "text") return "文字「" + (Text ?? "") + "」";
            if (Kind == "float") return "漂浮「" + (Text ?? "") + "」";
            if (Kind == "effect")
            {
                List<TriggerEffect> fx = EffectiveEffects();
                string s = "";
                for (int i = 0; i < fx.Count; i++)
                    s += (i > 0 ? "+" : "") + fx[i].Name();
                return "特效 " + s;
            }
            return "音频 " + (string.IsNullOrEmpty(Clip) ? "(默认)" : Clip);
        }

        /// <summary>制表符分隔的单行（外层再 Base64，避免和配置分隔符打架）。</summary>
        public string ToLine()
        {
            return string.Join("\t", new string[]
            {
                Kind ?? "", Clip ?? "", Image ?? "", (Text ?? "").Replace("\t", " ").Replace("\n", " "),
                Effect ?? "", Color1 ?? "", Color2 ?? "", Outline ? "1" : "0",
                XPercent.ToString("0.####", CultureInfo.InvariantCulture),
                YPercent.ToString("0.####", CultureInfo.InvariantCulture),
                Opacity.ToString("0.###", CultureInfo.InvariantCulture),
                Scale.ToString("0.###", CultureInfo.InvariantCulture),
                MarqueeDir.ToString(),
                MarqueeSpeed.ToString("0.##", CultureInfo.InvariantCulture),
                Font ?? "",
                FontSize.ToString("0.#", CultureInfo.InvariantCulture),
                FloatJitterX.ToString("0.##", CultureInfo.InvariantCulture),
                FloatJitterY.ToString("0.##", CultureInfo.InvariantCulture),
                FloatJitterSpeed.ToString("0.##", CultureInfo.InvariantCulture),
                FloatReveal ? "1" : "0",
                FloatRevealDelayMs.ToString(),
                FloatLifeMs.ToString(),
                EncodeCharStyles(CharStyles),
                EffectType ?? "shake",
                EffectAmplitude.ToString(),
                EffectDurationMs.ToString(),
                EncodeEffects(Effects)
            });
        }

        public static TriggerAction FromLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] f = line.Split('\t');
            TriggerAction a = new TriggerAction();
            if (f.Length > 0) a.Kind = string.IsNullOrEmpty(f[0]) ? "audio" : f[0];
            if (f.Length > 1) a.Clip = f[1];
            if (f.Length > 2) a.Image = f[2];
            if (f.Length > 3) a.Text = f[3];
            if (f.Length > 4) a.Effect = string.IsNullOrEmpty(f[4]) ? "solid" : f[4];
            if (f.Length > 5) a.Color1 = f[5];
            if (f.Length > 6) a.Color2 = f[6];
            if (f.Length > 7) a.Outline = f[7] != "0";
            a.XPercent = Num(f, 8, 0.5);
            a.YPercent = Num(f, 9, 0.5);
            a.Opacity = Num(f, 10, 1.0);
            a.Scale = Num(f, 11, 1.0);
            int dir;
            if (f.Length > 12 && int.TryParse(f[12], out dir)) a.MarqueeDir = dir;
            a.MarqueeSpeed = Num(f, 13, 1.0);
            if (f.Length > 14) a.Font = f[14];
            a.FontSize = Num(f, 15, 32);
            a.FloatJitterX = Num(f, 16, 2.0);
            a.FloatJitterY = Num(f, 17, 6.0);
            a.FloatJitterSpeed = Num(f, 18, 1.0);
            if (f.Length > 19) a.FloatReveal = f[19] != "0";
            a.FloatRevealDelayMs = (int)Math.Max(0, Num(f, 20, 40));
            a.FloatLifeMs = (int)Math.Max(0, Num(f, 21, 6000));
            if (f.Length > 22) a.CharStyles = DecodeCharStyles(f[22]);
            if (f.Length > 23 && !string.IsNullOrEmpty(f[23])) a.EffectType = f[23];
            a.EffectAmplitude = (int)Math.Max(0, Num(f, 24, 8));
            a.EffectDurationMs = (int)Math.Max(0, Num(f, 25, 320));
            if (f.Length > 26) a.Effects = DecodeEffects(f[26]);
            return a;
        }

        private static string EncodeEffects(List<TriggerEffect> effects)
        {
            if (effects == null || effects.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < effects.Count; i++)
            {
                TriggerEffect e = effects[i];
                if (e == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append((e.Type ?? "shake").Replace("\t", " ")).Append('\t')
                  .Append(e.Amplitude).Append('\t')
                  .Append(e.DurationMs).Append('\t')
                  .Append(e.Intensity.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Count).Append('\t')
                  .Append((e.Color1 ?? "").Replace("\t", " ")).Append('\t')
                  .Append((e.Color2 ?? "").Replace("\t", " ")).Append('\t')
                  .Append((e.Label ?? "").Replace("\t", " "));
            }
            return sb.Length == 0 ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static List<TriggerEffect> DecodeEffects(string encoded)
        {
            List<TriggerEffect> list = new List<TriggerEffect>();
            if (string.IsNullOrEmpty(encoded)) return list;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 1) continue;
                    TriggerEffect e = new TriggerEffect();
                    if (p.Length > 0 && p[0].Trim().Length > 0) e.Type = p[0].Trim();
                    if (p.Length > 1) int.TryParse(p[1], out e.Amplitude);
                    if (p.Length > 2) int.TryParse(p[2], out e.DurationMs);
                    e.Intensity = ParseD(p, 3, 0.22);
                    if (p.Length > 4) int.TryParse(p[4], out e.Count);
                    if (p.Length > 5) e.Color1 = p[5];
                    if (p.Length > 6) e.Color2 = p[6];
                    if (p.Length > 7) e.Label = p[7];
                    list.Add(e);
                }
            }
            catch { }
            return list;
        }

        private static string EncodeCharStyles(List<TriggerCharStyle> styles)
        {
            if (styles == null || styles.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < styles.Count; i++)
            {
                TriggerCharStyle s = styles[i];
                if (s == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(s.Index).Append('\t')
                  .Append((s.Color ?? "").Replace("\t", " ")).Append('\t')
                  .Append(s.JitterX.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(s.JitterY.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(s.Speed.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(s.Phase.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(s.Scale.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(s.Rotate.ToString("0.##", CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static List<TriggerCharStyle> DecodeCharStyles(string encoded)
        {
            List<TriggerCharStyle> list = new List<TriggerCharStyle>();
            if (string.IsNullOrEmpty(encoded)) return list;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 1) continue;
                    TriggerCharStyle s = new TriggerCharStyle();
                    int idx;
                    if (int.TryParse(p[0], out idx)) s.Index = idx;
                    if (p.Length > 1) s.Color = p[1];
                    s.JitterX = ParseD(p, 2, 2);
                    s.JitterY = ParseD(p, 3, 5);
                    s.Speed = ParseD(p, 4, 1);
                    s.Phase = ParseD(p, 5, idx * 0.7);
                    s.Scale = ParseD(p, 6, 1);
                    s.Rotate = ParseD(p, 7, 0);
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }

        private static double ParseD(string[] f, int i, double def)
        {
            double v;
            if (f.Length > i && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private static double Num(string[] f, int i, double def)
        {
            double v;
            if (f.Length > i && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }
    }

    /// <summary>一条自定义触发点。</summary>
    public class AudioTrigger
    {
        public string Name = "新触发点";
        public bool Enabled = true;
        public string Clip = "";            // 音频条目（文件名 / @片段名）
        public int CooldownSeconds = 3;     // 连续触发的最小间隔
        public bool OncePerRound = true;    // 一局只响一次 / 每次满足都响

        /// <summary>
        /// 规则模式（操作者要求）：false = 全部条件满足才触发（而且），
        /// true = 任意一条条件满足就触发（或者）。
        /// </summary>
        public bool MatchAny = false;

        /// <summary>
        /// 触发后的动作：false = 播放音频（默认）；
        /// true = **停止**正在播的音频（停止条件，操作者要求）。
        /// </summary>
        public bool IsStop = false;

        /// <summary>播放/停止时的渐入秒数（0 = 不渐入）。</summary>
        public double FadeIn = 0;

        /// <summary>播放/停止时的渐出秒数（0 = 不渐出，停就立刻停）。</summary>
        public double FadeOut = 0;

        public List<AudioCond> Conds = new List<AudioCond>();

        // ---- 第 7 批：三区卡片 ----
        /// <summary>动作区：音频 / 图片 / 文字可以同时配多个。</summary>
        public List<TriggerAction> Actions = new List<TriggerAction>();

        /// <summary>停止区：这些条件命中时停掉本卡片的所有动作。</summary>
        public List<AudioCond> StopConds = new List<AudioCond>();

        /// <summary>停止条件之间是"任一满足"（默认，需求 7.1）还是"全部满足"。</summary>
        public bool StopMatchAny = true;

        /// <summary>实际要执行的动作；老配置只有 Clip 时自动当成一个音频动作。</summary>
        public List<TriggerAction> EffectiveActions()
        {
            List<TriggerAction> list = new List<TriggerAction>();
            for (int i = 0; i < Actions.Count; i++)
                if (Actions[i] != null) list.Add(Actions[i]);
            if (list.Count == 0 && !string.IsNullOrEmpty(Clip))
            {
                TriggerAction a = new TriggerAction();
                a.Kind = "audio";
                a.Clip = Clip;
                list.Add(a);
            }
            return list;
        }

        /// <summary>停止条件是否命中（默认 OR）。</summary>
        public bool StopTrue(Func<string, string, string, double?> get)
        {
            if (StopConds.Count == 0) return false;
            if (StopMatchAny)
            {
                for (int i = 0; i < StopConds.Count; i++)
                {
                    AudioCond c = StopConds[i];
                    double? v = get(c.Event, c.Faction, c.Arg);
                    if (v.HasValue && c.Eval(v.Value)) return true;
                }
                return false;
            }
            for (int i = 0; i < StopConds.Count; i++)
            {
                AudioCond c = StopConds[i];
                double? v = get(c.Event, c.Faction, c.Arg);
                if (!v.HasValue) return false;
                if (!c.Eval(v.Value)) return false;
            }
            return true;
        }

        public AudioTrigger Clone()
        {
            AudioTrigger t = new AudioTrigger();
            t.Name = Name; t.Enabled = Enabled; t.Clip = Clip;
            t.CooldownSeconds = CooldownSeconds; t.OncePerRound = OncePerRound;
            t.MatchAny = MatchAny; t.IsStop = IsStop;
            t.FadeIn = FadeIn; t.FadeOut = FadeOut;
            for (int i = 0; i < Conds.Count; i++) t.Conds.Add(Conds[i].Clone());
            for (int i = 0; i < Actions.Count; i++) if (Actions[i] != null) t.Actions.Add(Actions[i].Clone());
            for (int i = 0; i < StopConds.Count; i++) t.StopConds.Add(StopConds[i].Clone());
            t.StopMatchAny = StopMatchAny;
            return t;
        }

        /// <summary>
        /// 条件是否满足（按 MatchAny 走"全部 / 任一"）。
        /// 取不到值的事件一律当"不满足"；没有条件永远不触发。
        /// </summary>
        public bool AllTrue(Func<string, string, string, double?> get)
        {
            if (Conds.Count == 0) return false;

            if (MatchAny)
            {
                for (int i = 0; i < Conds.Count; i++)
                {
                    AudioCond c = Conds[i];
                    double? v = get(c.Event, c.Faction, c.Arg);
                    if (v.HasValue && c.Eval(v.Value)) return true;
                }
                return false;
            }

            for (int i = 0; i < Conds.Count; i++)
            {
                AudioCond c = Conds[i];
                double? v = get(c.Event, c.Faction, c.Arg);
                if (!v.HasValue) return false;
                if (!c.Eval(v.Value)) return false;
            }
            return true;
        }

        public string Text()
        {
            if (Conds.Count == 0 && StopConds.Count == 0) return "（没有条件，不会触发）";
            StringBuilder sb = new StringBuilder();
            if (IsStop) sb.Append("【停止】");
            if (Conds.Count > 0)
            {
                sb.Append("（").Append(MatchAny ? "任一满足" : "全部满足").Append("）");
                for (int i = 0; i < Conds.Count; i++)
                {
                    if (i > 0) sb.Append(MatchAny ? "　或　" : "　且　");
                    sb.Append(Conds[i].Text());
                }
            }
            List<TriggerAction> acts = EffectiveActions();
            if (acts.Count > 0)
            {
                sb.Append("　→　");
                for (int i = 0; i < acts.Count; i++)
                {
                    if (i > 0) sb.Append(" + ");
                    sb.Append(acts[i].Summary());
                }
            }
            if (StopConds.Count > 0)
            {
                sb.Append("　停止：");
                for (int i = 0; i < StopConds.Count; i++)
                {
                    if (i > 0) sb.Append(StopMatchAny ? " 或 " : " 且 ");
                    sb.Append(StopConds[i].Text());
                }
            }
            if (FadeIn > 0) sb.Append("　渐入").Append(FadeIn.ToString("0.#")).Append("s");
            if (FadeOut > 0) sb.Append("　渐出").Append(FadeOut.ToString("0.#")).Append("s");
            return sb.ToString();
        }

        // ---- 序列化：`名称|启用|音频|冷却|每回合一次|条件;条件|规则|动作|渐入|渐出` ----
        private const char FSep = '|';
        private const char CSep = ';';
        private const char VSep = ',';

        public string ToSpec()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Clean(Name)).Append(FSep)
              .Append(Enabled ? "1" : "0").Append(FSep)
              .Append(Clean(Clip)).Append(FSep)
              .Append(CooldownSeconds).Append(FSep)
              .Append(OncePerRound ? "1" : "0").Append(FSep);
            sb.Append(EncodeConds(Conds));
            // 后 4 个字段是新增的：老配置没有就按默认值（全部满足 / 播放 / 不渐变）
            sb.Append(FSep).Append(MatchAny ? "any" : "all");
            sb.Append(FSep).Append(IsStop ? "stop" : "play");
            sb.Append(FSep).Append(FadeIn.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(FSep).Append(FadeOut.ToString("0.##", CultureInfo.InvariantCulture));
            // 第 7 批：动作区（Base64）+ 停止区条件 + 停止区 AND/OR
            sb.Append(FSep).Append(EncodeActions());
            sb.Append(FSep).Append(EncodeConds(StopConds));
            sb.Append(FSep).Append(StopMatchAny ? "any" : "all");
            return sb.ToString();
        }

        private string EncodeActions()
        {
            if (Actions == null || Actions.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i] == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Actions[i].ToLine());
            }
            if (sb.Length == 0) return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static List<TriggerAction> DecodeActions(string s)
        {
            List<TriggerAction> list = new List<TriggerAction>();
            if (string.IsNullOrEmpty(s)) return list;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(s));
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    TriggerAction a = TriggerAction.FromLine(lines[i]);
                    if (a != null) list.Add(a);
                }
            }
            catch { }
            return list;
        }

        internal static string EncodeConds(List<AudioCond> conds)
        {
            if (conds == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < conds.Count; i++)
            {
                if (conds[i] == null) continue;
                if (sb.Length > 0) sb.Append(CSep);
                AudioCond c = conds[i];
                sb.Append(Clean(c.Event)).Append(VSep)
                  .Append(Clean(c.Faction)).Append(VSep)
                  .Append(Clean(c.Arg)).Append(VSep)
                  .Append(Clean(c.Op)).Append(VSep)
                  .Append(c.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        internal static void ParseConds(string s, List<AudioCond> target)
        {
            if (string.IsNullOrEmpty(s) || target == null) return;
            string[] cs = s.Split(CSep);
            for (int i = 0; i < cs.Length; i++)
            {
                string[] v = cs[i].Split(VSep);
                if (v.Length < 1 || v[0].Trim().Length == 0) continue;
                AudioCond c = new AudioCond();
                c.Event = v[0].Trim();
                if (v.Length > 1) c.Faction = v[1].Trim();
                if (v.Length > 2) c.Arg = v[2].Trim();
                if (v.Length > 3 && v[3].Trim().Length > 0) c.Op = v[3].Trim();
                double val;
                if (v.Length > 4 && double.TryParse(v[4].Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out val)) c.Value = val;
                target.Add(c);
            }
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(FSep, ' ').Replace(CSep, ' ').Replace(VSep, ' ').Trim();
        }

        public static AudioTrigger FromSpec(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            string[] f = spec.Split(FSep);
            if (f.Length < 1) return null;

            AudioTrigger t = new AudioTrigger();
            t.Name = f.Length > 0 && f[0].Trim().Length > 0 ? f[0].Trim() : "触发点";
            t.Enabled = f.Length <= 1 || f[1].Trim() != "0";
            t.Clip = f.Length > 2 ? f[2].Trim() : "";
            int cd;
            t.CooldownSeconds = (f.Length > 3 && int.TryParse(f[3].Trim(), out cd)) ? cd : 3;
            t.OncePerRound = f.Length <= 4 || f[4].Trim() != "0";
            if (f.Length > 6 && f[6].Trim() == "any") t.MatchAny = true;
            if (f.Length > 7 && f[7].Trim() == "stop") t.IsStop = true;
            double fv;
            if (f.Length > 8 && double.TryParse(f[8].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out fv) && fv > 0) t.FadeIn = fv;
            if (f.Length > 9 && double.TryParse(f[9].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out fv) && fv > 0) t.FadeOut = fv;

            if (f.Length > 5)
            {
                string condText = f[5].Trim();
                // 老格式：条件字段本身用 ; 和 , 分隔
                if (condText.Length > 0) ParseConds(condText, t.Conds);
            }

            // 第 7 批字段（老配置没有 → 留空，按老语义跑）
            if (f.Length > 10 && f[10].Trim().Length > 0)
                t.Actions = DecodeActions(f[10].Trim());
            if (f.Length > 11 && f[11].Trim().Length > 0)
                ParseConds(f[11].Trim(), t.StopConds);
            if (f.Length > 12 && f[12].Trim() == "all") t.StopMatchAny = false;
            return t;
        }
    }

    /// <summary>多行文本 ⇄ 触发点列表（config.ini 里一行一条）。</summary>
    public static class AudioTriggerSpec
    {
        public static string Join(List<AudioTrigger> list)
        {
            if (list == null || list.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(list[i].ToSpec());
            }
            return sb.ToString();
        }

        public static List<AudioTrigger> Parse(string spec)
        {
            List<AudioTrigger> list = new List<AudioTrigger>();
            if (string.IsNullOrEmpty(spec)) return list;
            string[] lines = spec.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                AudioTrigger t = AudioTrigger.FromSpec(lines[i]);
                if (t != null) list.Add(t);
            }
            return list;
        }
    }

    /// <summary>一个可以当条件用的事件（给界面下拉框、也给日志用）。</summary>
    public class AudioEventInfo
    {
        public string Id = "";
        public string Name = "";
        public string Unit = "";          // 数值单位（"秒"/""…）
        public bool NeedsFaction;
        public bool NeedsArg;
        public string ArgHint = "";       // 参数提示（"SCP 编号"/"第几台"）
        public bool IsPulse;              // 一次性事件（命中后短暂为 1）
        public string Group = "";         // 界面分组
    }

    /// <summary>
    /// 软件能读到、可以当条件用的事件清单（操作者要的"所有事件都加到条件里"）。
    /// 真正取值在 <see cref="AlertManager.EvalAudioEvent"/> 里，这里只描述元数据。
    /// </summary>
    public static class AudioEvents
    {
        private static List<AudioEventInfo> _all;

        public static List<AudioEventInfo> All
        {
            get { if (_all == null) _all = Build(); return _all; }
        }

        public static AudioEventInfo Find(string id)
        {
            List<AudioEventInfo> all = All;
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase)) return all[i];
            return null;
        }

        public static string[] Ops = new string[] { "<=", ">=", "==", "!=", ">", "<" };

        private static void Add(List<AudioEventInfo> l, string id, string name, string group,
            string unit, bool faction, bool arg, string argHint, bool pulse)
        {
            AudioEventInfo e = new AudioEventInfo();
            e.Id = id; e.Name = name; e.Group = group; e.Unit = unit;
            e.NeedsFaction = faction; e.NeedsArg = arg; e.ArgHint = argHint; e.IsPulse = pulse;
            l.Add(e);
        }

        private static List<AudioEventInfo> Build()
        {
            List<AudioEventInfo> l = new List<AudioEventInfo>();

            // ---- 状态类（1 = 成立 / 有数字）----
            Add(l, "round.active", "正在对局中", "状态", "", false, false, "", false);
            Add(l, "inserver", "在服务器里", "状态", "", false, false, "", false);
            Add(l, "alive", "自己活着", "状态", "", false, false, "", false);
            Add(l, "round.elapsed", "回合已进行（秒）", "状态", "秒", false, false, "", false);
            Add(l, "self.dead", "自己处于死亡界面", "状态", "", false, false, "", false);

            // ---- 倒计时到点（原「到点放音频」的写法，现在就是一个条件）----
            Add(l, "alert.remain", "某条提示的倒计时剩余（秒）", "倒计时", "秒", false, true, "提示名", false);

            // ---- 核弹 / 终焉 ----
            Add(l, "warhead.active", "核弹倒计时在走", "核弹", "", false, false, "", false);
            Add(l, "warhead.remain", "核弹剩余（秒）", "核弹", "秒", false, false, "", false);
            Add(l, "deadman.active", "亡者协议在走", "终焉", "", false, false, "", false);
            Add(l, "deadman.remain", "亡者协议剩余（秒）", "终焉", "秒", false, false, "", false);
            Add(l, "pulse.warhead.start", "核弹刚刚开启", "核弹", "", false, false, "", true);
            Add(l, "pulse.warhead.detonated", "核弹刚刚引爆", "核弹", "", false, false, "", true);
            Add(l, "pulse.deadman.start", "终焉（亡者协议）刚刚开启", "终焉", "", false, false, "", true);
            Add(l, "pulse.deadman.zero", "终焉倒计时归零（核弹另计，不联动）", "终焉", "", false, false, "", true);
            Add(l, "deadman.ocr", "屏幕上认到过终焉协议", "终焉", "", false, false, "", false);
            Add(l, "pulse.deadman.ocr", "刚刚认到终焉协议", "终焉", "", false, false, "", true);

            // ---- 净化 ----
            Add(l, "decon.active", "净化倒计时已起表", "净化", "", false, false, "", false);
            Add(l, "decon.remain", "净化剩余（秒）", "净化", "秒", false, false, "", false);
            Add(l, "decon.locked", "净化已归零锁死", "净化", "", false, false, "", false);
            Add(l, "pulse.decon.start", "净化刚刚开始计时", "净化", "", false, false, "", true);
            Add(l, "pulse.decon.purge", "净化刚刚开始清生物", "净化", "", false, false, "", true);

            // ---- 刷新 ----
            Add(l, "wave.count", "本局第几波", "刷新", "波", false, false, "", false);
            Add(l, "wave.next.remain", "距下一波刷新（秒）", "刷新", "秒", false, false, "", false);            Add(l, "wave.mini.pending", "下一波是小波", "刷新", "", false, false, "", false);
            Add(l, "wave.mini.remain", "小波预计剩余（秒）", "刷新", "秒", false, false, "", false);
            Add(l, "wave.soon", "即将刷新（最后 30 秒）", "刷新", "", false, false, "", false);
            Add(l, "pulse.wave.big", "刚刚刷新大波", "刷新", "", true, false, "", true);
            Add(l, "pulse.wave.mini", "刚刚刷新小波", "刷新", "", true, false, "", true);

            // ---- 发电机 / 079 ----
            Add(l, "has079", "本回合有 SCP-079", "发电机", "", false, false, "", false);
            Add(l, "gen.active", "已启动发电机台数", "发电机", "台", false, false, "", false);
            Add(l, "gen.allready", "三台发电机已就绪", "发电机", "", false, false, "", false);
            Add(l, "gen.soon", "第几台快启动了（0=没有）", "发电机", "", false, false, "", false);
            Add(l, "gen.soon.remain", "快启动那台还剩（秒）", "发电机", "秒", false, false, "", false);
            Add(l, "pulse.gen.activate", "某一台发电机刚刚激活", "发电机", "", false, true, "第几台", true);
            Add(l, "pulse.gen.allready", "三台刚刚全部就绪", "发电机", "", false, false, "", true);

            // ---- 票数 / 令牌 / 影响力 ----
            Add(l, "tokens", "重生令牌数", "票数", "个", true, false, "", false);
            Add(l, "influence", "影响力", "票数", "", true, false, "", false);
            Add(l, "influence.next", "距下一个令牌阈值还差", "票数", "", true, false, "", false);
            Add(l, "pulse.token.gain", "刚刚拿到一个重生令牌", "票数", "", true, false, "", true);

            // ---- SCP ----
            Add(l, "scp.alive", "某个 SCP 还活着", "SCP", "", false, true, "SCP 编号", false);
            Add(l, "scp.present", "某个 SCP 本回合在场", "SCP", "", false, true, "SCP 编号", false);
            Add(l, "scp.dead", "某个 SCP 已阵亡", "SCP", "", false, true, "SCP 编号", false);
            Add(l, "pulse.scp.killed", "某个 SCP 刚刚阵亡", "SCP", "", false, true, "SCP 编号", true);

            // ---- 自己 ----
            Add(l, "pulse.self.death", "自己刚刚阵亡", "自己", "", false, false, "", true);
            Add(l, "pulse.self.respawn", "自己刚刚重生", "自己", "", false, false, "", true);

            // ---- 回合 / 服务器 / 游戏 ----
            Add(l, "pulse.round.start", "回合刚刚开始", "回合", "", false, false, "", true);
            Add(l, "pulse.round.end", "回合刚刚结束", "回合", "", false, false, "", true);
            Add(l, "pulse.server.join", "刚刚进入服务器", "回合", "", false, false, "", true);
            Add(l, "pulse.server.leave", "刚刚离开服务器", "回合", "", false, false, "", true);
            Add(l, "pulse.game.start", "游戏刚刚启动", "回合", "", false, false, "", true);
            Add(l, "pulse.game.exit", "游戏刚刚退出", "回合", "", false, false, "", true);

            return l;
        }
    }
}
