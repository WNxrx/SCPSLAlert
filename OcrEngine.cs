using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
#if TESSERACT
using Tesseract;
#endif

namespace SCPSLAlert
{
    /// <summary>
    /// Tesseract 引擎的全局单例持有者。
    ///
    /// 设计要点（都是为了不影响游戏帧率）：
    ///   · 引擎只在后台线程初始化一次，之后全程复用，绝不每次识别都 new；
    ///   · Process() 用 Task 包起来硬等 500ms，超时就丢弃本次结果，
    ///     并把卡住的引擎隔离掉（不能让 GC 回收它，否则 native 句柄会崩）；
    ///   · 初始化失败（缺语言文件 / 缺 VC++ 运行库）时先降级成纯英文，
    ///     再失败就让 IsReady 保持 false，整个 OCR 模块安静地不工作，
    ///     绝不会影响倒计时、热键这些主功能。
    ///
    /// 编译时没有 libs\Tesseract.dll 就把 TESSERACT 宏关掉，
    /// 这个类依然能编译，只是 IsReady 恒为 false。
    /// </summary>
    internal static class OcrEngine
    {
        /// <summary>
        /// 单次识别的硬超时（毫秒）：超时直接丢弃本次识别，并把卡住的引擎隔离重建。
        ///
        /// 【为什么不是固定值了】以前写死 500ms。但「最大」档用的是最高精度模型
        /// （tessdata_best\chi_sim），一整块播报区（737×279、中英混排）经常要
        /// 600~1500 毫秒 —— 于是日志里反复出现"识别超时 → 丢弃 → 重建引擎"：
        /// 既把那一句播报丢了，又要白等一次引擎重建。现在由
        /// OcrAlertWatcher.SyncEngineSettings 按识别强度设置（最大 2000、其它 800）。
        /// </summary>
        public static int TimeoutMs = 800;

        /// <summary>保护 _engine / _status 这些短字段，临界区里绝不做重活。</summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// Process 不是线程安全的，轮询线程和设置窗口的"预览识别"必须排队。
        /// 单独一把锁，免得锁着 _lock 去跑 native。
        /// </summary>
        private static readonly object _recogLock = new object();

        /// <summary>用 object 装引擎：没定义 TESSERACT 时这里永远是 null。</summary>
        private static object _engine;
        private static bool _initializing;
        private static string _status = "未初始化";
        private static string _lang = "";

#if TESSERACT
        /// <summary>超时被抛弃的引擎：留住引用，等它自己跑完再释放。</summary>
        private static readonly System.Collections.Generic.List<object> _quarantine =
            new System.Collections.Generic.List<object>();
#endif

        /// <summary>当前引擎用的语言（没建好就是 null）。用来判断语言变了要不要重建。</summary>
        public static string ReadyLang
        {
            get { lock (_lock) { return _lang; } }
        }

        public static bool IsReady
        {
            get { lock (_lock) { return _engine != null; } }
        }

        public static string Status
        {
            get { lock (_lock) { return _status; } }
        }

        /// <summary>实际生效的语言（降级之后会变成 eng）。</summary>
        public static string Language
        {
            get { lock (_lock) { return _lang; } }
        }

        /// <summary>
        /// 确保引擎已经（或正在）初始化。可以随便调，重复调用是空操作。
        /// 永远不阻塞调用线程 —— 初始化可能要几百毫秒。
        /// </summary>
        public static void EnsureReady(string tessdataDir, string lang)
        {
            bool go;
            lock (_lock)
            {
                if (_engine != null || _initializing) return;
                _initializing = true;
                go = true;
            }
            if (!go) return;

            Task.Run(new Action(delegate
            {
                try { Initialize(tessdataDir, lang); }
                catch (Exception ex)
                {
                    lock (_lock) { _status = "初始化异常：" + ex.Message; }
                    Logger.Err("OCR", "引擎初始化异常", ex);
                }
                finally { lock (_lock) { _initializing = false; } }
            }));
        }

        /// <summary>
        /// 丢掉当前引擎，让下次 EnsureReady 用新设置重建。
        /// 切换语言 / 精度模型时用 —— tesseract 的引擎一旦建好就改不了语言。
        /// </summary>
        public static void Reset()
        {
            object old = null;
            lock (_lock)
            {
                old = _engine;
                _engine = null;
                _lang = "";             // 也清掉：否则下次判断"语言变了没"会一直为真
                _initializing = false;
                _status = "正在重建…";
#if TESSERACT
                _quarantine.Clear();
#endif
            }
#if TESSERACT
            try { if (old != null) ((TesseractEngine)old).Dispose(); } catch { }
#endif
            Logger.On("OCR", "引擎已重置，下次扫描会用新的语言 / 精度模型重建");
        }

        /// <summary>真正的初始化。会阻塞，只应该在后台线程里调。</summary>
        public static void Initialize(string tessdataDir, string lang)
        {
            string report = null;
            lock (_lock)
            {
                if (_engine != null) return;
                if (string.IsNullOrEmpty(lang)) lang = "eng+chi_sim";

                // 用户在界面里选的"语言模型"。这里只是记下来打日志用；
                // 真正决定用哪份 .traineddata 的是 tessdata 目录里的文件名
                // （见 OcrModelFile 的说明）。
                _model = ModelFileOf(lang);

#if TESSERACT
                string firstError = null;

                // 第一选择：配置里的语言。EngineMode.Default 就是 tesseract 的
                // --oem 3（OEM_DEFAULT，LSTM + 兼容层），和命令行参数是同一个东西。
                try
                {
                    _engine = new TesseractEngine(tessdataDir, lang, EngineMode.Default);
                    _lang = lang;
                    _status = "已就绪（" + lang + "）";
                    report = "引擎就绪：" + _status + "；语言文件目录 " + tessdataDir;
                }
                catch (Exception ex)
                {
                    firstError = ex.Message;
                }

                // 兜底：多语言起不来（缺 chi_sim.traineddata 之类）就降级纯英文。
                if (_engine == null && !string.Equals(lang, "eng", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _engine = new TesseractEngine(tessdataDir, "eng", EngineMode.Default);
                        _lang = "eng";
                        _status = "已降级为 eng（" + lang + " 初始化失败：" + firstError + "）";
                        report = "引擎降级：" + _status;
                    }
                    catch (Exception ex2)
                    {
                        _status = "初始化失败：" + ex2.Message;
                        report = "引擎初始化失败（已降级 eng 仍失败）：" + ex2.Message;
                    }
                }
                else if (_engine == null)
                {
                    _status = "初始化失败：" + firstError;
                    report = "引擎初始化失败：" + firstError;
                }

                if (_engine != null)
                {
                    try { ((TesseractEngine)_engine).DefaultPageSegMode = PageSegMode.Auto; }
                    catch { }
                }
#else
                _lang = lang;
                _status = "不可用（编译时没有引用 Tesseract.dll）";
                report = "OCR " + _status;
#endif
            }

            // 日志放在锁外面写，免得写盘把别的线程堵住
            if (report != null) Logger.On("OCR", report);
        }

        /// <summary>
        /// 语言模型精度：false = 快速版（tessdata\chi_sim.traineddata，约 2.5MB），
        /// true = 最高精度版（tessdata_best\chi_sim.traineddata，约 13MB）。
        ///
        /// 【怎么切换的】tesseract 只认文件名叫 "chi_sim.traineddata" 的那一份，
        /// 所以两份模型分别放在两个目录里，切换就是换**语言文件目录**：
        ///     tessdata\       快速版（默认，日常够用、快）
        ///     tessdata_best\  最高精度版（更准，但慢一些）
        /// 两份目录里都必须同时有 eng.traineddata 和 chi_sim.traineddata。
        ///
        /// 初值 true：和"程序启动时先假定用高精度版"一致，这样把配置里的实际值
        /// 同步进来时不会被误判成"用户换了模型"，避免启动阶段白白重建一次引擎。
        /// </summary>
        public static bool BestModel = true;

        private static string _model = "快速版";

        /// <summary>当前用的是哪份模型（给界面 / 日志显示）。</summary>
        public static string ModelName
        {
            get { return BestModel ? "最高精度版" : "快速版"; }
        }

        private static string ModelFileOf(string lang)
        {
            return BestModel ? "最高精度版" : "快速版";
        }

        /// <summary>
        /// 识别一张图。超时 / 出错都返回 null，调用方直接丢弃本次结果即可。
        /// 会阻塞最多 TimeoutMs 毫秒。
        /// </summary>
        public static string Recognize(Bitmap bmp, out float confidence)
        {
            return RecognizePage(bmp, 0, out confidence);
        }

        /// <summary>
        /// 指定页面分割方式识别。psm=0 用引擎默认（Auto，给普通播报用）；
        /// psm=6/7 是"当成一整行 / 单个词"，票数那种只有几个数字的小图用它
        /// 更稳 —— Auto 会在这点面积上乱猜，经常直接返回空页。
        /// 传 int 而不是 Tesseract 的枚举，是为了让没引用 Tesseract.dll 时也能编译。
        /// </summary>
        public static string RecognizePage(Bitmap bmp, int psm, out float confidence)
        {
            confidence = 0f;
            if (bmp == null) return null;

            object eng;
            lock (_lock) { eng = _engine; }
            if (eng == null) return null;

            byte[] png;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    // 全名限定：Tesseract 里也有一个 ImageFormat，不限定会撞名
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    png = ms.ToArray();
                }
            }
            catch { return null; }

#if TESSERACT
            return RecognizePng(eng, png, psm, out confidence);
#else
            return null;
#endif
        }

        private static string RecognizePng(object engineObj, byte[] png, out float confidence)
        {
            return RecognizePng(engineObj, png, 0, out confidence);
        }

        private static string RecognizePng(object engineObj, byte[] png, int psm, out float confidence)
        {
            confidence = 0f;
#if TESSERACT
            TesseractEngine engine = (TesseractEngine)engineObj;

            PageSegMode mode = engine.DefaultPageSegMode;
            if (psm == 6) mode = PageSegMode.SingleBlock;
            else if (psm == 7) mode = PageSegMode.SingleLine;
            else if (psm == 8) mode = PageSegMode.SingleWord;

            // 匿名方法里不能直接用 out 参数，绕一层数组。
            float[] conf = new float[1];

            lock (_recogLock)
            {
                Func<string> work = new Func<string>(delegate
                {
                    using (Pix pix = Pix.LoadFromMemory(png))
                    using (Page page = engine.Process(pix, mode))
                    {
                        conf[0] = page.GetMeanConfidence();
                        return page.GetText();
                    }
                });

                Task<string> task = Task.Run<string>(work);

                bool finished;
                try { finished = task.Wait(TimeoutMs); }
                catch (AggregateException) { finished = true; }   // 出错了也算"跑完了"

                if (!finished)
                {
                    // 超时：那个线程还卡在 native 里，引擎不能再用了。
                    Logger.On("OCR", "识别超时（>" + TimeoutMs + "ms），丢弃本次结果并重建引擎");
                    Quarantine(engine, task);
                    return null;
                }

                if (task.IsFaulted)
                {
                    string msg = task.Exception != null
                        ? task.Exception.GetBaseException().Message
                        : "未知错误";
                    Logger.Err("OCR", "识别出错，丢弃本次结果", task.Exception);
                    DropEngine(engine, "识别出错：" + msg);
                    return null;
                }

                confidence = conf[0];
                return task.Result;
            }
#else
            return null;
#endif
        }

#if TESSERACT
        /// <summary>
        /// 隔离一个卡住的引擎：把它从"当前引擎"上摘下来，等它自己跑完再释放。
        /// 直接 Dispose 会在 native 还在用的时候把内存拆掉，必然崩。
        /// </summary>
        private static void Quarantine(TesseractEngine engine, Task task)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_engine, engine))
                {
                    _engine = null;
                    _status = "上次识别超时，引擎已丢弃，将在下次自动重建";
                }
                _quarantine.Add(engine);
            }

            task.ContinueWith(new Action<Task>(delegate
            {
                lock (_lock) { _quarantine.Remove(engine); }
                try { engine.Dispose(); } catch { }
            }));
        }

        /// <summary>引擎真的坏了：扔掉，下次调用时重建。</summary>
        private static void DropEngine(TesseractEngine engine, string reason)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_engine, engine))
                {
                    _engine = null;
                    _status = reason;
                }
            }
            try { engine.Dispose(); } catch { }
        }
#endif
    }
}
