using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 用户自备音频（操作者要求）：
    ///   · 程序目录下建一个「音频」文件夹，用户往里丢音频文件（wav / mp3 / m4a / wma…）；
    ///   · 全局可以设一段"任何提示弹出都放"的音频；
    ///   · 每种提示可以单独指定自己的音频；
    ///   · 倒计时类提示还可以按"剩多少秒"放不同的音频（例如净化剩 60 秒放 A、剩 30 秒放 B）；
    ///   · **不叠音**：放新的之前先把上一段停掉（操作者要求）；
    ///   · 全程无感 —— 不弹别的软件、不占焦点。
    ///
    /// 播放用 Windows 自带的 MCI（winmm.dll）：
    ///   · 不需要任何第三方库，也不需要 WPF；
    ///   · 天然支持"停掉上一段"和音量；
    ///   · 认不出的格式（比如 ogg/flac）会记一行日志然后跳过，绝不影响主流程。
    /// </summary>
    internal static class AudioLibrary
    {
        /// <summary>默认的音频文件夹名（程序目录下）。</summary>
        public const string DefaultDirName = "音频";

        /// <summary>片段引用前缀：提示里的 AudioFile 写 "@名字" 就是引用一个自定义片段。</summary>
        public const string SegmentPrefix = "@";

        /// <summary>一条自定义片段：从某个音频里裁出来的一截（不改动原文件）。</summary>
        public class Segment
        {
            public string Name = "";
            public string File = "";
            public double From;      // 秒
            public double To;        // 秒（0 = 到结尾）
            public double FadeIn;    // 渐入秒数（0 = 不渐入）
            public double FadeOut;   // 渐出秒数（0 = 不渐出）

            public string Display
            {
                get
                {
                    return SegmentPrefix + Name + "（" + File + " "
                        + From.ToString("0.#") + "s~"
                        + (To > 0 ? To.ToString("0.#") + "s" : "结尾") + "）";
                }
            }
        }

        /// <summary>解析 config 里的 AudioSegments（`名字=文件|起|止`，分号分隔）。</summary>
        public static List<Segment> ParseSegments(string spec)
        {
            List<Segment> list = new List<Segment>();
            if (string.IsNullOrEmpty(spec)) return list;
            try
            {
                string[] parts = spec.Split(new char[] { ';', '；', '\n', '\r' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = p.Substring(0, eq).Trim();
                    string body = p.Substring(eq + 1).Trim();
                    string[] f = body.Split('|');
                    if (f.Length < 2) continue;

                    Segment s = new Segment();
                    s.Name = name;
                    s.File = f[0].Trim();
                    double.TryParse(f[1].Trim(), out s.From);
                    if (f.Length >= 3) double.TryParse(f[2].Trim(), out s.To);
                    // 兼容老配置：第 4/5 个字段是渐入 / 渐出秒数（没有就 0）
                    if (f.Length >= 4) double.TryParse(f[3].Trim(), out s.FadeIn);
                    if (f.Length >= 5) double.TryParse(f[4].Trim(), out s.FadeOut);
                    if (s.FadeIn < 0) s.FadeIn = 0;
                    if (s.FadeOut < 0) s.FadeOut = 0;
                    if (s.Name.Length == 0 || s.File.Length == 0) continue;
                    if (s.From < 0) s.From = 0;
                    if (s.To > 0 && s.To <= s.From) continue;
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }

        /// <summary>把片段列表写回 config 字符串。</summary>
        public static string JoinSegments(List<Segment> list)
        {
            StringBuilder sb = new StringBuilder();
            if (list == null) return "";
            for (int i = 0; i < list.Count; i++)
            {
                Segment s = list[i];
                if (i > 0) sb.Append(';');
                sb.Append(s.Name).Append('=').Append(s.File).Append('|')
                  .Append(s.From.ToString("0.##")).Append('|').Append(s.To.ToString("0.##"));
                // 渐入 / 渐出：只有非 0 才写，保持老配置文件干净
                if (s.FadeIn > 0 || s.FadeOut > 0)
                    sb.Append('|').Append(s.FadeIn.ToString("0.##"))
                      .Append('|').Append(s.FadeOut.ToString("0.##"));
            }
            return sb.ToString();
        }

        public static Segment FindSegment(string spec, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith(SegmentPrefix)) name = name.Substring(SegmentPrefix.Length);
            List<Segment> list = ParseSegments(spec);
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        /// <summary>解析音量表（`键=0~100`，分号分隔），取某个键的音量（没有 = 100）。</summary>
        public static int VolumeOf(string spec, string key)
        {
            if (string.IsNullOrEmpty(spec) || string.IsNullOrEmpty(key)) return 100;
            try
            {
                string[] parts = spec.Split(new char[] { ';', '；', '\n', '\r' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.LastIndexOf('=');
                    if (eq <= 0) continue;
                    if (!string.Equals(p.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
                    int v;
                    if (int.TryParse(p.Substring(eq + 1).Trim(), out v))
                        return v < 0 ? 0 : (v > 100 ? 100 : v);
                }
            }
            catch { }
            return 100;
        }

        /// <summary>设置/覆盖音量表里的一个键。</summary>
        public static string SetVolume(string spec, string key, int volume)
        {
            if (string.IsNullOrEmpty(key)) return spec;
            if (volume > 100) volume = 100;
            if (volume < 0) volume = 0;

            StringBuilder sb = new StringBuilder();
            bool done = false;
            if (!string.IsNullOrEmpty(spec))
            {
                string[] parts = spec.Split(new char[] { ';', '；', '\n', '\r' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.LastIndexOf('=');
                    if (eq <= 0) continue;
                    string k = p.Substring(0, eq).Trim();
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (sb.Length > 0) sb.Append(';');
                        sb.Append(k).Append('=').Append(volume);
                        done = true;
                        continue;
                    }
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(p);
                }
            }
            if (!done)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(key).Append('=').Append(volume);
            }
            return sb.ToString();
        }

        /// <summary>允许的扩展名（MCI 在这些上基本都能放）。</summary>
        private static readonly string[] Exts =
        {
            ".wav", ".mp3", ".wma", ".m4a", ".aac", ".mid", ".midi"
        };

        /// <summary>这个扩展名能不能当音频导入（拖放导入时用）。</summary>
        public static bool IsSupportedExt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < Exts.Length; i++) if (ext == Exts[i]) return true;
            return false;
        }

        /// <summary>
        /// 改/加一个"到点"（或分台）音频：`at=文件` 这种表里的第 at 项。
        /// 倒计时类提示用它按秒数放音频；发电机类提示用同一张表按"第几台"放音频
        /// （操作者要求：这些"有时间轴 / 有进度"的事件要能分别设）。
        /// </summary>
        public static string SetPoint(string spec, int at, string file)
        {
            if (at <= 0 || string.IsNullOrEmpty(file)) return spec;

            StringBuilder sb = new StringBuilder();
            bool done = false;
            if (!string.IsNullOrEmpty(spec))
            {
                string[] parts = spec.Split(new char[] { ',', '，', ';', '；' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    int v;
                    if (int.TryParse(p.Substring(0, eq).Trim(), out v) && v == at)
                    {
                        if (sb.Length > 0) sb.Append(',');
                        sb.Append(at).Append('=').Append(file);
                        done = true;
                        continue;
                    }
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(p);
                }
            }
            if (!done)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(at).Append('=').Append(file);
            }
            return sb.ToString();
        }

        /// <summary>删掉一个"到点"。</summary>
        public static string RemovePoint(string spec, int at)
        {
            if (string.IsNullOrEmpty(spec) || at <= 0) return spec;
            StringBuilder sb = new StringBuilder();
            string[] parts = spec.Split(new char[] { ',', '，', ';', '；' });
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                int v;
                if (int.TryParse(p.Substring(0, eq).Trim(), out v) && v == at) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(p);
            }
            return sb.ToString();
        }

        /// <summary>把 `60=a.mp3,30=b.wav` 读成 (秒数, 文件名) 列表（按秒数从大到小）。</summary>
        public static List<KeyValuePair<int, string>> PointPairs(string spec)
        {
            List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrEmpty(spec)) return list;
            try
            {
                string[] parts = spec.Split(new char[] { ',', '，', ';', '；' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    int v;
                    if (!int.TryParse(p.Substring(0, eq).Trim(), out v) || v <= 0) continue;
                    list.Add(new KeyValuePair<int, string>(v, p.Substring(eq + 1).Trim()));
                }
            }
            catch { }
            list.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b)
            {
                return b.Key.CompareTo(a.Key);
            });
            return list;
        }

        private static string _dir;

        /// <summary>总开关（设置里那个"启用提示音"）。</summary>
        public static bool Master = true;

        /// <summary>当前正在播放的别名（null = 没在放）。</summary>
        private static string _current;

        /// <summary>最近一次播放失败的原因（给日志 / 设置窗口看）。</summary>
        public static string LastError = "";

        /// <summary>初始化：定位音频文件夹（不存在就建一个空的，方便用户往里丢东西）。</summary>
        public static string ResolveDir(string configured)
        {
            try
            {
                string dir;
                if (!string.IsNullOrEmpty(configured))
                {
                    dir = Path.IsPathRooted(configured)
                        ? configured
                        : Path.Combine(AppDir(), configured);
                }
                else dir = Path.Combine(AppDir(), DefaultDirName);

                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                _dir = dir;
                return dir;
            }
            catch (Exception ex)
            {
                LastError = "定位音频文件夹失败：" + ex.Message;
                return null;
            }
        }

        private static string AppDir()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string d = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return Environment.CurrentDirectory;
        }

        /// <summary>
        /// 取当前音频文件夹；没定位过就自己定位一次。
        /// 【为什么要这个】_dir 只由 ResolveDir 赋值。谁要是没先调它就用 List()/Play()，
        /// 会静默拿到"空文件夹"（设置窗口里列表是空的，看着像"程序没认到我的音频"）。
        /// </summary>
        private static string DirFor()
        {
            if (string.IsNullOrEmpty(_dir)) ResolveDir(null);
            return _dir;
        }

        /// <summary>列出音频文件夹里所有可用的音频（给设置界面 / 日志用）。</summary>
        public static string[] List()
        {
            List<string> list = new List<string>();
            try
            {
                string d = DirFor();
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) return list.ToArray();
                foreach (string f in Directory.GetFiles(d))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    for (int i = 0; i < Exts.Length; i++)
                        if (ext == Exts[i]) { list.Add(Path.GetFileName(f)); break; }
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return list.ToArray();
        }

        /// <summary>
        /// 播放一个音频文件。name 可以是文件名（带扩展名），也可以是绝对路径。
        /// 空名字 / 文件不存在 / 播放失败都只记日志，不影响任何主流程。
        /// </summary>
        public static bool Play(string name, string why)
        {
            return Play(name, why, 100, 0, 0);
        }

        /// <summary>
        /// 打开一个 MCI 设备。返回成功的别名；失败返回 null 并给出最后一次的错误。
        ///
        /// 【为什么要试三次】有些机器上 MCI 的 MPEG 驱动会返回
        /// 277「初始化 MCI 时发生问题」——常见于**上一次设备刚关掉、驱动还在收尾**，
        /// 或者驱动只允许单实例（这种要加 shareable）。所以：
        ///   ① 原样打开；
        ///   ② 等 150ms 再试，并加 shareable（允许并行实例）；
        ///   ③ 等 400ms 再试，连 type 都不指定（让 MCI 按扩展名自己挑设备）。
        /// 每次失败都写日志，日志里能看出到底是哪一步、错在哪。
        /// </summary>
        private static string OpenMci(string path, out string lastError)
        {
            string type = MciType(path);
            string[] tails =
            {
                "type " + type + " alias ",
                "type " + type + " alias ",
                "alias "
            };
            string[] suffixes = { "", " shareable", "" };
            int[] waits = { 0, 150, 400 };
            string[] names = { "原样", "shareable", "不指定 type" };

            lastError = "";
            for (int i = 0; i < tails.Length; i++)
            {
                if (waits[i] > 0) System.Threading.Thread.Sleep(waits[i]);

                string alias = "scpsl_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string err = Mci("open \"" + path + "\" " + tails[i] + alias + suffixes[i]);
                if (err == null) return alias;

                lastError = err;
                Logger.On("音频", "MCI 打开失败（第 " + (i + 1) + " 次·" + names[i] + "）：" + err
                    + "　文件 " + Path.GetFileName(path));
                Mci("close " + alias);
            }

            // ---- 第 4 次：把这个文件前面的 ID3v2 标签剥掉再试 ----
            // 实测：同一个文件夹里 5 个 mp3 都能放，只有「标签 400KB」那个放不了，
            // 报的就是 277「初始化 MCI 时发生问题」；音频数据（MPEG1 L3 128k/44.1k）
            // 和其它文件完全一样。所以问题在大标签，不在音频 —— 剥掉它就能开。
            string stripped = StripId3v2(path);
            if (stripped != null)
            {
                string alias = "scpsl_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string err = Mci("open \"" + stripped + "\" type " + MciType(stripped) + " alias " + alias);
                if (err == null)
                {
                    Logger.On("音频", "剥掉 ID3 标签后打开成功：" + Path.GetFileName(path)
                        + "（临时副本 " + stripped + "）");
                    return alias;
                }
                lastError = err;
                Logger.On("音频", "MCI 打开失败（第 4 次·剥掉 ID3 标签）：" + err
                    + "　文件 " + Path.GetFileName(path));
                Mci("close " + alias);
            }

            return null;
        }

        /// <summary>
        /// 把文件开头的 ID3v2 标签剥掉、写成一个临时副本，返回副本路径；
        /// 没有标签 / 标签不大 / 出错了就返回 null（调用方继续用原文件）。
        /// 音频数据一个字节都不动，副本按 路径+大小+修改时间 缓存，不会反复复制。
        /// </summary>
        private static string StripId3v2(string path)
        {
            try
            {
                byte[] head = new byte[10];
                long len;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    len = fs.Length;
                    if (len < 4096) return null;
                    if (fs.Read(head, 0, 10) < 10) return null;
                }

                if (head[0] != 'I' || head[1] != 'D' || head[2] != '3') return null;
                if (head[3] < 2 || head[3] > 4) return null;

                int tag = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14)
                        | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
                tag += 10;
                if (tag < Id3StripMinBytes) return null;      // 标签不大，MCI 自己能跳过去
                if (tag + 1024 >= len) return null;           // 剥完就没剩什么了，别动

                string dir = Path.Combine(Path.GetTempPath(), "SCPSLAlert_audio");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string key = path + "|" + len + "|" + File.GetLastWriteTimeUtc(path).Ticks;
                string dest = Path.Combine(dir, Fnv1a(key) + ".mp3");
                if (File.Exists(dest) && new FileInfo(dest).Length == len - tag) return dest;

                using (FileStream src = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    src.Seek(tag, SeekOrigin.Begin);
                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                }
                Logger.On("音频", "已生成剥掉 ID3 标签的临时副本（原标签 " + (tag / 1024)
                    + " KB）：" + Path.GetFileName(path));
                CleanOldCopies(dir);
                return dest;
            }
            catch { return null; }
        }

        /// <summary>ID3v2 标签大到这个程度就值得剥（实测 400KB 的放不了、90KB 以下都能放）。</summary>
        private const int Id3StripMinBytes = 64 * 1024;

        /// <summary>清掉 30 天前的旧副本，免得临时目录越堆越多。</summary>
        private static void CleanOldCopies(string dir)
        {
            try
            {
                DateTime limit = DateTime.UtcNow.AddDays(-30);
                foreach (string f in Directory.GetFiles(dir, "*.mp3"))
                {
                    try { if (File.GetLastWriteTimeUtc(f) < limit) File.Delete(f); }
                    catch { }
                }
            }
            catch { }
        }

        private static string Fnv1a(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619;
                }
                return h.ToString("x8");
            }
        }

        /// <summary>
        /// 【推荐入口】按"音频条目"播放：name 可以是文件名，也可以是 `@片段名`。
        /// 音量 / 裁剪（片段）/ 片段渐入渐出都从 config 里那份表里取，
        /// 所以任何调用点都不用自己算。fadeInSec / fadeOutSec 是调用方追加的
        /// 渐入渐出（比如触发点自己设的），和片段自带的取较大者。
        /// </summary>
        public static bool PlayKey(string key, string why)
        {
            return PlayKey(key, why, 0, 0);
        }

        public static bool PlayKey(string key, string why, double fadeInSec, double fadeOutSec)
        {
            if (string.IsNullOrEmpty(key)) return false;

            int vol = VolumeOf(VolumesSpec, key);
            if (key.StartsWith(SegmentPrefix))
            {
                Segment s = FindSegment(SegmentsSpec, key);
                if (s == null)
                {
                    LastError = "找不到音频片段：" + key;
                    Logger.On("音频", LastError + "（" + why + "）");
                    return false;
                }
                // 片段的起止用**毫秒**传给 MCI（以前按秒取整，裁出来的头尾会差半秒）
                int fromMs = (int)Math.Round(s.From * 1000);
                int toMs = s.To > 0 ? (int)Math.Round(s.To * 1000) : 0;
                double fi = Math.Max(s.FadeIn, fadeInSec);
                double fo = Math.Max(s.FadeOut, fadeOutSec);
                return PlayMs(s.File, why + "／片段 " + s.Name, vol, fromMs, toMs, fi, fo);
            }
            return PlayMs(key, why, vol, 0, 0, fadeInSec, fadeOutSec);
        }

        /// <summary>config 里的片段表 / 音量表（由界面层同步进来）。</summary>
        public static string SegmentsSpec = "";
        public static string VolumesSpec = "";

        /// <summary>把 config 里的音频设置同步进来（片段表 + 音量表）。</summary>
        public static void ApplyConfig(Config cfg)
        {
            if (cfg == null) return;
            SegmentsSpec = cfg.AudioSegments == null ? "" : cfg.AudioSegments;
            VolumesSpec = cfg.AudioVolumes == null ? "" : cfg.AudioVolumes;
        }

        /// <summary>
        /// 播放一个音频文件，并可以控制**音量**和**裁剪范围**（操作者要求）。
        ///   · volume：0~100，默认 100（0 = 静音，等于不放）；
        ///   · fromSec / toSec：从第几秒放到第几秒（0 = 从头 / 到结尾）——
        ///     这就是"裁剪"，用 MCI 的 `play from ... to ...` 实现，**不改动原文件**，
        ///     所以 mp3 / m4a 都不用重新编码。
        /// 空名字 / 文件不存在 / 播放失败都只记日志，不影响任何主流程。
        /// </summary>
        public static bool Play(string name, string why, int volume, int fromSec, int toSec)
        {
            return PlayMs(name, why, volume, fromSec * 1000, toSec * 1000, 0, 0);
        }

        /// <summary>毫秒精度的播放入口（裁剪片段的起止是毫秒；渐入渐出秒数可带小数）。</summary>
        public static bool PlayMs(string name, string why, int volume, int fromMs, int toMs,
            double fadeInSec, double fadeOutSec)
        {
            if (!Master) return false;
            if (string.IsNullOrEmpty(name)) return false;

            if (volume <= 0) return false;          // 音量 0 就是不响，不用开 MCI
            if (volume > 100) volume = 100;
            if (fromMs < 0) fromMs = 0;
            if (toMs > 0 && toMs <= fromMs) toMs = fromMs + 500;
            if (fadeInSec < 0) fadeInSec = 0;
            if (fadeOutSec < 0) fadeOutSec = 0;

            try
            {
                string path = Path.IsPathRooted(name)
                    ? name
                    : Path.Combine(DirFor() ?? AppDir(), name);
                if (!File.Exists(path))
                {
                    LastError = "找不到音频文件：" + path;
                    Logger.On("音频", LastError + "（" + why + "）");
                    return false;
                }

                // 【不叠音】先把上一段停掉（操作者要求）
                StopCurrent();

                string openErr;
                string alias = OpenMci(path, out openErr);
                if (alias == null)
                {
                    LastError = "MCI 打不开（试了 3 种方式）：" + openErr;
                    Logger.On("音频", LastError + "（" + why + "，文件 " + Path.GetFileName(path) + "）");
                    return false;
                }

                // ---- 渐入：先静音开播，再用定时器把音量升到目标 ----
                // （MCI setaudio 的音量范围是 0~1000）
                if (fadeInSec > 0)
                {
                    Mci("setaudio " + alias + " volume to 0");
                    StartFade(alias, 0, volume, fadeInSec, false);
                }
                else if (volume < 100)
                {
                    Mci("setaudio " + alias + " volume to " + (volume * 10));
                }

                // ---- 渐出：提前 fadeOutSec 秒开始把音量降下去，降到 0 再停 ----
                if (fadeOutSec > 0)
                {
                    int playLenMs = toMs;
                    if (playLenMs <= 0)
                    {
                        double total = DurationSeconds(path);
                        if (total > 0) playLenMs = (int)Math.Round(total * 1000);
                    }
                    if (playLenMs > fadeOutSec * 1000 + 200)
                    {
                        int atMs = playLenMs - (int)Math.Round(fadeOutSec * 1000);
                        if (atMs < fromMs + 200) atMs = fromMs + 200;
                        StartFade(alias, volume, 0, fadeOutSec, true, fromMs + atMs);
                    }
                }

                // 播放（带裁剪范围）
                string err;
                if (fromMs > 0 || toMs > 0)
                {
                    string range = "";
                    if (fromMs > 0) range += " from " + fromMs;
                    if (toMs > 0) range += " to " + toMs;
                    err = Mci("play " + alias + range);
                }
                else
                {
                    err = Mci("play " + alias);
                }

                if (err != null)
                {
                    StopFade();
                    Mci("close " + alias);
                    LastError = "MCI 播放失败：" + err;
                    Logger.On("音频", LastError + "（" + why + "，文件 " + Path.GetFileName(path) + "）");
                    return false;
                }

                _current = alias;
                LastError = "";
                Logger.On("音频", "播放 " + Path.GetFileName(path)
                    + "（" + why
                    + (volume < 100 ? "，音量 " + volume + "%" : "")
                    + (fadeInSec > 0 ? "，渐入 " + fadeInSec.ToString("0.#") + "s" : "")
                    + (fadeOutSec > 0 ? "，渐出 " + fadeOutSec.ToString("0.#") + "s" : "")
                    + (fromMs > 0 || toMs > 0
                        ? "，裁剪 " + (fromMs / 1000.0).ToString("0.##") + "s~"
                          + (toMs > 0 ? (toMs / 1000.0).ToString("0.##") + "s" : "结尾") : "")
                    + "）");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Logger.Err("音频", "播放音频出错（忽略）", ex);
                return false;
            }
        }

        /// <summary>
        /// 问一个音频的**总时长**（秒）。0 = 问不出来。
        ///
        /// 【为什么必须有这个】裁剪编辑器要靠它把"鼠标的横坐标"换算成秒。
        /// 以前只对 wav 有长度（解析出来的），mp3 那些总时长是 0 ——
        /// 于是拖动换算不出秒数，选出来永远是 0.00~0.0x（这就是操作者报的 bug）。
        /// 现在：wav 自己解析；其它格式问 MCI 的 `status &lt;alias&gt; length`。
        /// </summary>
        public static double DurationSeconds(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;

#if NLAYER
            // mp3：自己解码读时长，比 MCI 可靠（大 ID3 标签的文件 MCI 根本打不开）
            try
            {
                string e0 = Path.GetExtension(path).ToLowerInvariant();
                if (e0 == ".mp3" || e0 == ".mp2" || e0 == ".mp1")
                {
                    using (NLayer.MpegFile mp3 = new NLayer.MpegFile(path))
                    {
                        double d = mp3.Duration.TotalSeconds;
                        if (d > 0) return d;
                    }
                }
            }
            catch { }
#endif

            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".wav")
                {
                    double sec; string why;
                    if (WaveformPeaks(path, 8, out sec, out why) != null && sec > 0) return sec;
                }
            }
            catch { }

            string alias = "scpsl_q" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string err = Mci("open \"" + path + "\" type " + MciType(path) + " alias " + alias);
            if (err != null)
            {
                // 大 ID3v2 标签的文件 MCI 打不开（详见 StripId3v2），剥掉标签再问一次
                string stripped = StripId3v2(path);
                if (stripped == null) return 0;
                alias = "scpsl_q" + Guid.NewGuid().ToString("N").Substring(0, 6);
                err = Mci("open \"" + stripped + "\" type " + MciType(stripped) + " alias " + alias);
                if (err != null) return 0;
            }
            try
            {
                StringBuilder sb = new StringBuilder(64);
                int rc = mciSendString("status " + alias + " length", sb, sb.Capacity, IntPtr.Zero);
                if (rc != 0) return 0;
                int ms;
                if (int.TryParse(sb.ToString().Trim(), out ms) && ms > 0) return ms / 1000.0;
            }
            catch { }
            finally { Mci("close " + alias); }
            return 0;
        }

        /// <summary>
        /// 当前播放位置（秒）；没在播 / 问不到就返回 0。
        /// 编辑窗口那根"播放到哪儿了"的线靠它。
        /// </summary>
        public static double CurrentPositionSeconds()
        {
            if (string.IsNullOrEmpty(_current)) return 0;
            try
            {
                StringBuilder sb = new StringBuilder(64);
                int rc = mciSendString("status " + _current + " position", sb, sb.Capacity, IntPtr.Zero);
                if (rc != 0) return 0;
                int ms;
                if (int.TryParse(sb.ToString().Trim(), out ms)) return ms / 1000.0;
            }
            catch { }
            return 0;
        }

        // ============================================================
        // 淡入 / 淡出（渐起渐落）
        //
        // 用一个 System.Windows.Forms.Timer（主线程消息泵里跑，MCI 调用没有线程
        // 竞争），每 50ms 把 setaudio volume 往目标推一步。
        //   · 渐入：开播时 0 → 目标音量；
        //   · 渐出：延时 delayMs 后 目标音量 → 0，降到头自动 StopCurrent()。
        // 换曲 / 手动停都会先 StopFade()，不会留一个野定时器在改音量。
        // ============================================================

        private static Timer _fadeTimer;
        private static string _fadeAlias;
        private static int _fadeFrom;           // 起始音量（0~100）
        private static int _fadeTo;             // 目标音量（0~100）
        private static double _fadeSeconds;     // 整个渐变时长
        private static DateTime _fadeStart;     // 渐变开始时刻
        private static int _fadeDelayMs;        // 渐变前的延时（渐出用）
        private static bool _fadeStopAtEnd;     // 渐变到头要不要顺手停掉

        /// <summary>启动一个渐变。delayMs 毫秒后才真正开始（渐出 = 提前量）。</summary>
        private static void StartFade(string alias, int fromVol, int toVol,
            double seconds, bool stopAtEnd, int delayMs = 0)
        {
            StopFade();
            _fadeAlias = alias;
            _fadeFrom = fromVol;
            _fadeTo = toVol;
            _fadeSeconds = seconds <= 0.05 ? 0.05 : seconds;
            _fadeStart = DateTime.Now.AddMilliseconds(delayMs);
            _fadeDelayMs = delayMs;
            _fadeStopAtEnd = stopAtEnd;

            if (_fadeTimer == null)
            {
                _fadeTimer = new Timer();
                _fadeTimer.Interval = 50;
                _fadeTimer.Tick += delegate { FadeTick(); };
            }
            _fadeTimer.Start();
        }

        private static void FadeTick()
        {
            try
            {
                if (string.IsNullOrEmpty(_fadeAlias)) { StopFade(); return; }
                double elapsed = (DateTime.Now - _fadeStart).TotalSeconds;
                if (elapsed < 0) return;                        // 还在延时里

                double k = elapsed / _fadeSeconds;
                if (k >= 1)
                {
                    int endVol = _fadeTo;
                    StopFade();                                  // 先摘定时器
                    if (_current == _fadeAlias || _fadeStopAtEnd)
                    {
                        if (endVol <= 0) { StopCurrent(); return; }
                        Mci("setaudio " + _fadeAlias + " volume to " + (endVol * 10));
                    }
                    return;
                }

                int vol = (int)Math.Round(_fadeFrom + (_fadeTo - _fadeFrom) * k);
                Mci("setaudio " + _fadeAlias + " volume to " + (vol * 10));
            }
            catch { StopFade(); }
        }

        /// <summary>摘掉渐变定时器（不碰音频本身）。</summary>
        private static void StopFade()
        {
            if (_fadeTimer != null) { try { _fadeTimer.Stop(); } catch { } }
            _fadeAlias = null;
        }

        /// <summary>
        /// 停掉正在播的音频，先在 fadeOutSec 秒内把音量降到底再停
        /// （停止触发器的"渐弱"）。fadeOutSec <= 0 就是立刻停。
        /// </summary>
        public static void StopWithFade(double fadeOutSec)
        {
            if (string.IsNullOrEmpty(_current)) return;
            if (fadeOutSec <= 0) { StopCurrent(); return; }

            string alias = _current;
            int curVol = 100;
            try
            {
                StringBuilder sb = new StringBuilder(64);
                if (mciSendString("status " + alias + " volume", sb, sb.Capacity, IntPtr.Zero) == 0)
                {
                    int v;
                    if (int.TryParse(sb.ToString().Trim(), out v)) curVol = v / 10;
                }
            }
            catch { }
            StartFade(alias, curVol <= 0 ? 100 : curVol, 0, fadeOutSec, true, 0);
        }

        /// <summary>
        /// 停掉正在播放的那一段。
        ///
        /// 【关闭必须确认成功】以前 stop / close 都不看返回值，close 失败时照样把
        /// _current 清空 —— 那个 MCI 设备就一直开着（泄漏）。泄漏几个之后新的 open
        /// 就会开始报 277「初始化 MCI 时发生问题」。现在 close 失败会重试几次并记日志。
        /// </summary>
        public static void StopCurrent()
        {
            StopFade();
            string alias = _current;
            if (string.IsNullOrEmpty(alias)) return;
            _current = null;

            try
            {
                Mci("stop " + alias);
                for (int i = 0; i < 3; i++)
                {
                    if (Mci("close " + alias) == null) return;
                    System.Threading.Thread.Sleep(60);
                }
                Logger.On("音频", "MCI 设备关不掉（可能泄漏了一个设备）：" + alias);
            }
            catch { }
        }

        /// <summary>按扩展名给 MCI 指定设备类型；认不出来就交给它自己猜。</summary>
        private static string MciType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav") return "waveaudio";
            if (ext == ".mid" || ext == ".midi") return "sequencer";
            return "mpegvideo";     // mp3 / wma / m4a 走这个
        }

        /// <summary>调一次 MCI。返回 null = 成功，否则是错误文本。</summary>
        private static string Mci(string cmd)
        {
            try
            {
                int rc = mciSendString(cmd, null, 0, IntPtr.Zero);
                if (rc == 0) return null;

                StringBuilder sb = new StringBuilder(256);
                mciGetErrorString(rc, sb, sb.Capacity);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder ret,
            int retLen, IntPtr hwndCallback);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciGetErrorString(int err, StringBuilder buf, int len);

        // ============================================================
        // 波形（给"音频裁剪"编辑窗口画图用）
        //
        // 【为什么只做 WAV】画波形需要**采样点**。程序播放走的是 Windows 自带的
        // MCI，拿不到解码后的 PCM；wav 是明文 PCM，自己解析最省事、也不用引入
        // 任何第三方库。mp3 / m4a 这些就在编辑窗口里退回"时间轴"模式
        //（照样能拖选、能试听，只是没有波形）。
        // ============================================================

        /// <summary>
        /// 读音频的波形包络：把整段音频分成 buckets 个桶，每桶取 |采样| 的峰值（0~1）。
        /// wav 直接解析采样点；mp3 用自带的纯 C# 解码器（NLayer）解成 PCM 再算。
        /// 失败（格式不支持 / 读不出来）返回 null，并给出说明。
        /// </summary>
        public static float[] WaveformPeaks(string path, int buckets, out double seconds, out string why)
        {
            seconds = 0;
            why = "";
            if (buckets < 8) buckets = 8;

#if NLAYER
            try
            {
                string e0 = Path.GetExtension(path).ToLowerInvariant();
                if (e0 == ".mp3" || e0 == ".mp2" || e0 == ".mp1")
                {
                    float[] cached = PeaksFromCache(path, buckets);
                    if (cached != null) { seconds = CachedSeconds(path); return cached; }

                    float[] p = Mp3Peaks(path, buckets, out seconds, out why);
                    if (p != null) { SavePeaksCache(path, buckets, seconds, p); return p; }
                }
            }
            catch { }
#endif

            try
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < 44 || all[0] != 'R' || all[1] != 'I' || all[2] != 'F' || all[3] != 'F')
                { why = "不是 WAV（没有 RIFF 头）"; return null; }

                int fmt = -1, data = -1, dataLen = 0;
                int channels = 1, bits = 16, format = 1, rate = 44100;
                int pos = 12;
                while (pos + 8 <= all.Length)
                {
                    string id = "" + (char)all[pos] + (char)all[pos + 1] + (char)all[pos + 2] + (char)all[pos + 3];
                    int size = BitConverter.ToInt32(all, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt " && body + 16 <= all.Length)
                    {
                        format = BitConverter.ToInt16(all, body);
                        channels = BitConverter.ToInt16(all, body + 2);
                        rate = BitConverter.ToInt32(all, body + 4);
                        bits = BitConverter.ToInt16(all, body + 14);
                        fmt = body;
                    }
                    else if (id == "data")
                    {
                        data = body;
                        dataLen = Math.Min(size, all.Length - body);
                        if (dataLen > 0) break;
                    }
                    pos = body + size + (size & 1);
                }

                if (fmt < 0 || data < 0 || dataLen <= 0) { why = "WAV 里找不到 fmt/data 块"; return null; }
                if (format != 1 && format != 3) { why = "WAV 是压缩格式（format=" + format + "），画不了波形"; return null; }
                if (channels < 1) channels = 1;

                int bytesPerSample = bits / 8;
                if (bytesPerSample <= 0) { why = "位深读不出来"; return null; }
                long frames = dataLen / (bytesPerSample * channels);
                if (frames <= 0) { why = "没有采样数据"; return null; }
                seconds = frames / (double)(rate > 0 ? rate : 44100);

                float[] peaks = new float[buckets];
                long per = Math.Max(1, frames / buckets);
                long f = 0;
                for (int b = 0; b < buckets && f < frames; b++)
                {
                    float max = 0;
                    long end = Math.Min(frames, f + per);
                    // 桶内再抽样，避免几百万个采样点算太久（画面上看不出来差别）
                    long step = Math.Max(1, (end - f) / 800);
                    for (long i = f; i < end; i += step)
                    {
                        int off = data + (int)(i * bytesPerSample * channels);
                        for (int c = 0; c < channels; c++)
                        {
                            int o = off + c * bytesPerSample;
                            if (o + bytesPerSample > all.Length) break;
                            float v;
                            if (format == 3) v = Math.Abs(BitConverter.ToSingle(all, o));
                            else if (bits == 8) v = Math.Abs((all[o] - 128) / 128f);
                            else if (bits == 16) v = Math.Abs(BitConverter.ToInt16(all, o) / 32768f);
                            else if (bits == 24)
                            {
                                int s = all[o] | (all[o + 1] << 8) | (all[o + 2] << 16);
                                if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000);
                                v = Math.Abs(s / 8388608f);
                            }
                            else v = Math.Abs(BitConverter.ToInt32(all, o) / 2147483648f);
                            if (v > max) max = v;
                        }
                    }
                    peaks[b] = max > 1 ? 1 : max;
                    f = end;
                }
                return peaks;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return null;
            }
        }

#if NLAYER
        // ============================================================
        // mp3 的波形：用自带的纯 C# 解码器（NLayer）解成 PCM，再按桶取峰值。
        //   · MCI / MediaPlayer 都拿不到 PCM，这条路才拿得到真正的音量；
        //   · 解码一首 3 分钟的歌约 0.6~1 秒，所以编辑器里丢后台线程算；
        //   · 峰值夹到 0~1（解码出来的浮点会超过 1，不夹会画到框外）。
        // ============================================================

        private static float[] Mp3Peaks(string path, int buckets, out double seconds, out string why)
        {
            seconds = 0;
            why = "";
            try
            {
                using (NLayer.MpegFile mp3 = new NLayer.MpegFile(path))
                {
                    double total = mp3.Duration.TotalSeconds;
                    if (total <= 0) { why = "mp3 读不出时长"; return null; }
                    seconds = total;

                    int rate = mp3.SampleRate > 0 ? mp3.SampleRate : 44100;
                    int ch = mp3.Channels > 0 ? mp3.Channels : 2;
                    long totalSamples = (long)(total * rate * ch);
                    if (totalSamples <= 0) totalSamples = 1;

                    float[] peaks = new float[buckets];
                    float[] buf = new float[16384];
                    long done = 0;
                    int n;
                    while ((n = mp3.ReadSamples(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            float v = buf[i];
                            if (v < 0) v = -v;
                            int b = (int)((done + i) * buckets / totalSamples);
                            if (b < 0) b = 0;
                            if (b >= buckets) b = buckets - 1;
                            if (v > peaks[b]) peaks[b] = v;
                        }
                        done += n;
                    }
                    for (int i = 0; i < buckets; i++) if (peaks[i] > 1f) peaks[i] = 1f;
                    return peaks;
                }
            }
            catch (Exception ex)
            {
                why = "mp3 解码失败：" + ex.Message;
                return null;
            }
        }

        // 峰值缓存：同一个文件换个桶数/重开窗口就不用再解一遍（最多留 4 份）
        private class PeakCacheEntry
        {
            public string Key = "";
            public int Buckets;
            public double Seconds;
            public float[] Peaks;
            public DateTime At;
        }

        private static readonly List<PeakCacheEntry> _peakCache = new List<PeakCacheEntry>();

        private static string CacheKey(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                return path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return path; }
        }

        private static float[] PeaksFromCache(string path, int buckets)
        {
            string key = CacheKey(path);
            for (int i = 0; i < _peakCache.Count; i++)
                if (_peakCache[i].Key == key && _peakCache[i].Buckets == buckets)
                    return _peakCache[i].Peaks;
            return null;
        }

        private static double CachedSeconds(string path)
        {
            string key = CacheKey(path);
            for (int i = 0; i < _peakCache.Count; i++)
                if (_peakCache[i].Key == key) return _peakCache[i].Seconds;
            return 0;
        }

        private static void SavePeaksCache(string path, int buckets, double seconds, float[] peaks)
        {
            try
            {
                PeakCacheEntry e = new PeakCacheEntry();
                e.Key = CacheKey(path);
                e.Buckets = buckets;
                e.Seconds = seconds;
                e.Peaks = peaks;
                e.At = DateTime.Now;
                _peakCache.Add(e);
                while (_peakCache.Count > 4) _peakCache.RemoveAt(0);
            }
            catch { }
        }
#endif

        /// <summary>
        /// 解析"到点放音频"表：<c>"60=一分钟.mp3, 30=紧张.mp3"</c> → 阈值数组（60, 30）。
        /// 解析不出来的条目直接跳过（绝不抛异常）。
        /// </summary>
        public static int[] ParsePoints(string spec)
        {
            List<int> list = new List<int>();
            if (string.IsNullOrEmpty(spec)) return list.ToArray();

            try
            {
                string[] parts = spec.Split(new char[] { ',', '，', ';', '；' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    int at;
                    if (int.TryParse(p.Substring(0, eq).Trim(), out at) && at > 0) list.Add(at);
                }
            }
            catch { }
            return list.ToArray();
        }

        /// <summary>取某个阈值对应的音频文件名（找不到返回 null）。</summary>
        public static string PointFile(string spec, int at)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            try
            {
                string[] parts = spec.Split(new char[] { ',', '，', ';', '；' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 0) continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    int v;
                    if (!int.TryParse(p.Substring(0, eq).Trim(), out v) || v != at) continue;
                    string f = p.Substring(eq + 1).Trim();
                    return f.Length > 0 ? f : null;
                }
            }
            catch { }
            return null;
        }

        // ============================================================
        // 一键导出片段（操作者要求）
        //
        // 原则：**保持原格式、不重新编码**。
        //   · wav —— 解析 RIFF，把 data 块里 [from,to] 的采样原样复制成新 wav；
        //   · mp3 —— 按 MP3 帧边界切（帧头解析出帧长，从头到尾拷对应帧），
        //            码率/采样率原样保留；
        //   · 其它（wma/m4a/mid…）—— 切不了就整文件复制一份，并在结果里说明。
        // ============================================================

        /// <summary>
        /// 把片段表里的所有片段导出到 destDir。返回 (成功数, 说明文本)。
        /// </summary>
        public static Tuple<int, string> ExportAllSegments(string spec, string destDir)
        {
            List<Segment> list = ParseSegments(spec);
            if (list.Count == 0) return Tuple.Create(0, "没有任何已保存的片段可导出。");
            try { if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir); }
            catch (Exception ex) { return Tuple.Create(0, "建不了导出文件夹：" + ex.Message); }

            int ok = 0;
            StringBuilder notes = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                string why;
                if (ExportOne(list[i], destDir, out why)) ok++;
                else if (why.Length > 0) notes.Append("· ").Append(why).Append("\r\n");
            }
            string msg = "成功导出 " + ok + " / " + list.Count + " 个片段到：\r\n" + destDir;
            if (notes.Length > 0) msg += "\r\n\r\n以下片段导出的是完整文件（该格式切不了）：\r\n" + notes;
            return Tuple.Create(ok, msg);
        }

        private static bool ExportOne(Segment s, string destDir, out string why)
        {
            why = "";
            try
            {
                string path = Path.IsPathRooted(s.File)
                    ? s.File
                    : Path.Combine(DirFor() ?? AppDir(), s.File);
                if (!File.Exists(path)) { why = s.Name + "：找不到音频文件 " + s.File; return false; }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                string baseName = SafeName(s.Name);
                if (baseName.Length == 0) baseName = "片段" + s.From.ToString("0");
                string dest = UniquePath(Path.Combine(destDir, baseName + ext));

                if (ext == ".wav")
                {
                    if (WavSlice(path, s.From, s.To, dest)) return true;
                    // 压缩 wav 切不了 → 整文件复制
                    File.Copy(path, dest, true);
                    why = s.Name + "：是压缩 wav，导出了完整文件";
                    return false;
                }
                if (ext == ".mp3")
                {
                    if (Mp3Slice(path, s.From, s.To, dest)) return true;
                    File.Copy(path, dest, true);
                    why = s.Name + "：mp3 帧解析失败，导出了完整文件";
                    return false;
                }

                File.Copy(path, dest, true);
                why = s.Name + "：格式（" + ext + "）切不了，导出了完整文件";
                return false;
            }
            catch (Exception ex)
            {
                why = s.Name + "：" + ex.Message;
                return false;
            }
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                bool isBad = false;
                for (int j = 0; j < bad.Length; j++) if (bad[j] == name[i]) { isBad = true; break; }
                sb.Append(isBad ? '_' : name[i]);
            }
            return sb.ToString().Trim();
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 999; i++)
            {
                string p = Path.Combine(dir, name + "(" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
            return path;
        }

        /// <summary>把 wav 的 [fromSec,toSec] 切成新文件（PCM/float 明文才行）。</summary>
        private static bool WavSlice(string path, double fromSec, double toSec, string dest)
        {
            try
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < 44 || all[0] != 'R' || all[1] != 'I' || all[2] != 'F' || all[3] != 'F')
                    return false;

                int fmt = -1, data = -1, dataLen = 0;
                int channels = 1, bits = 16, format = 1, rate = 44100, align = 2;
                int pos = 12;
                while (pos + 8 <= all.Length)
                {
                    string id = "" + (char)all[pos] + (char)all[pos + 1] + (char)all[pos + 2] + (char)all[pos + 3];
                    int size = BitConverter.ToInt32(all, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt " && body + 16 <= all.Length)
                    {
                        format = BitConverter.ToInt16(all, body);
                        channels = BitConverter.ToInt16(all, body + 2);
                        rate = BitConverter.ToInt32(all, body + 4);
                        bits = BitConverter.ToInt16(all, body + 14);
                        align = BitConverter.ToInt16(all, body + 12);
                        fmt = body;
                    }
                    else if (id == "data")
                    {
                        data = body;
                        dataLen = Math.Min(size, all.Length - body);
                        if (dataLen > 0) break;
                    }
                    pos = body + size + (size & 1);
                }
                if (fmt < 0 || data < 0 || dataLen <= 0) return false;
                if (format != 1 && format != 3) return false;
                if (align <= 0) align = Math.Max(1, bits / 8 * channels);

                double total = dataLen / (double)align / (rate > 0 ? rate : 44100);
                double from = Math.Max(0, fromSec);
                double to = (toSec > 0 && toSec < total) ? toSec : total;
                if (to - from < 0.02) return false;

                int start = data + (int)Math.Round(from * rate) * align;
                int end = data + (int)Math.Round(to * rate) * align;
                if (start < data) start = data;
                if (end > data + dataLen) end = data + dataLen;
                if (end - start < align) return false;

                int bodyLen = end - start;
                int alignPad = bodyLen & 1;

                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                using (BinaryWriter w = new BinaryWriter(fs))
                {
                    w.Write((byte)'R'); w.Write((byte)'I'); w.Write((byte)'F'); w.Write((byte)'F');
                    w.Write(36 + bodyLen + alignPad);
                    w.Write((byte)'W'); w.Write((byte)'A'); w.Write((byte)'V'); w.Write((byte)'E');
                    w.Write((byte)'f'); w.Write((byte)'m'); w.Write((byte)'t'); w.Write((byte)' ');
                    w.Write(16);
                    w.Write((short)format);
                    w.Write((short)channels);
                    w.Write(rate);
                    w.Write(rate * align);          // 字节率
                    w.Write((short)align);
                    w.Write((short)bits);
                    w.Write((byte)'d'); w.Write((byte)'a'); w.Write((byte)'t'); w.Write((byte)'a');
                    w.Write(bodyLen);
                    w.Write(all, start, bodyLen);
                    if (alignPad == 1) w.Write((byte)0);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>把 mp3 的 [fromSec,toSec] 按帧边界切成新文件（不重编码）。</summary>
        private static bool Mp3Slice(string path, double fromSec, double toSec, string dest)
        {
            try
            {
                byte[] all = File.ReadAllBytes(path);
                int n = all.Length;

                // 跳过 ID3v2 头
                int pos = 0;
                if (n > 10 && all[0] == 'I' && all[1] == 'D' && all[2] == '3')
                {
                    int sz = ((all[6] & 0x7F) << 21) | ((all[7] & 0x7F) << 14)
                           | ((all[8] & 0x7F) << 7) | (all[9] & 0x7F);
                    pos = 10 + sz;
                }

                int[][] rates =
                {
                    new int[] { 44100, 48000, 32000 },   // MPEG1
                    new int[] { 22050, 24000, 16000 },   // MPEG2
                    new int[] { 11025, 12000, 8000 }     // MPEG2.5
                };
                int[] bitrateV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
                int[] bitrateV2L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

                // 扫一遍记每帧起点/帧长，顺便累计总时长
                List<int> frameStarts = new List<int>(n / 400 + 16);
                List<int> frameLens = new List<int>(n / 400 + 16);
                double totalSec = 0;

                int p = pos;
                while (p + 4 <= n)
                {
                    if (all[p] != 0xFF || (all[p + 1] & 0xE0) != 0xE0) { p++; continue; }

                    int verBits = (all[p + 1] >> 3) & 3;    // 0=2.5, 2=2, 3=1
                    if (verBits == 1) { p++; continue; }
                    int layerBits = (all[p + 1] >> 1) & 3;  // 1 = Layer3
                    if (layerBits != 1) { p++; continue; }
                    int brIdx = (all[p + 2] >> 4) & 15;
                    int srIdx = (all[p + 2] >> 2) & 3;
                    if (brIdx == 0 || brIdx == 15 || srIdx == 3) { p++; continue; }

                    int vi = verBits == 3 ? 0 : (verBits == 2 ? 1 : 2);
                    int rate = rates[vi][srIdx];
                    int bitrate = (vi == 0 ? bitrateV1L3 : bitrateV2L3)[brIdx] * 1000;
                    int pad = (all[p + 2] >> 1) & 1;
                    int frameLen = vi == 0
                        ? 144 * bitrate / rate + pad
                        : 72 * bitrate / rate + pad;
                    if (frameLen < 8) { p++; continue; }
                    if (p + frameLen > n) frameLen = n - p;

                    frameStarts.Add(p);
                    frameLens.Add(frameLen);
                    double samples = vi == 0 ? 1152 : 576;
                    totalSec += samples / rate;
                    p += frameLen;
                }

                if (frameStarts.Count == 0 || totalSec <= 0) return false;

                double from = Math.Max(0, fromSec);
                double to = (toSec > 0 && toSec < totalSec) ? toSec : totalSec;
                if (to - from < 0.02) return false;

                int totalFrames = frameStarts.Count;
                int f0 = (int)Math.Floor(from / totalSec * totalFrames);
                int f1 = (int)Math.Ceiling(to / totalSec * totalFrames);
                if (f0 >= totalFrames) return false;
                if (f1 > totalFrames) f1 = totalFrames;
                if (f1 - f0 < 1) return false;

                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    if (pos > 0) fs.Write(all, 0, Math.Min(pos, n));   // 保留 ID3 标签
                    for (int i = f0; i < f1; i++)
                        fs.Write(all, frameStarts[i], frameLens[i]);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
