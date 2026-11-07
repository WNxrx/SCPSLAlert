using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 探测 SCP:SL 游戏进程是否在运行（只看进程列表，不碰游戏本体）。
    /// 只在“没运行 → 运行”和“运行 → 没运行”这两种状态跳变时各触发一次提示。
    /// </summary>
    internal class GameWatcher : IDisposable
    {
        private readonly Timer _timer;
        private bool _running;
        private bool _first = true;
        private bool _disposed;

        /// <summary>游戏启动了。</summary>
        public event Action Started;

        /// <summary>游戏退出了。</summary>
        public event Action Exited;

        public bool IsRunning
        {
            get { return _running; }
        }

        public GameWatcher(int intervalMs)
        {
            if (intervalMs < 500) intervalMs = 500;
            _timer = new Timer();
            _timer.Interval = intervalMs;
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            // 第一次只记录当前状态、不弹提示：如果启动本工具时游戏本来就在跑，
            // 那不算“检测到游戏启动”，免得一上来就弹一条莫名其妙的提示。
            _running = Detect();
            _first = false;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool now = Detect();
            if (now == _running) return;

            _running = now;
            if (_first) return;

            if (now)
            {
                if (Started != null) Started();
            }
            else
            {
                if (Exited != null) Exited();
            }
        }

        /// <summary>游戏进程名包含 SCPSL（但要排掉本工具自己 SCPSLAlert.exe）。</summary>
        private static bool Detect()
        {
            Process[] all = null;
            try
            {
                all = Process.GetProcesses();
                for (int i = 0; i < all.Length; i++)
                {
                    string name;
                    try { name = all[i].ProcessName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.StartsWith("SCPSLAlert", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.IndexOf("SCPSL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("SCP Secret Laboratory", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch
            {
                // 拿不到进程列表时当作“没运行”
            }
            finally
            {
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        try { all[i].Dispose(); } catch { }
                    }
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
    }
}
