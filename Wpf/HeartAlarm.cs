using System;
using System.IO;
using System.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 角色死亡时的一声监护仪长音。程序内生成 WAV，不依赖外部音频文件。
    /// </summary>
    internal static class HeartAlarm
    {
        private static readonly object Gate = new object();
        private static SoundPlayer _player;
        private static MemoryStream _stream;

        public static void Play(int volume)
        {
            if (volume <= 0) return;
            if (volume > 100) volume = 100;
            lock (Gate)
            {
                try
                {
                    Stop();
                    _stream = BuildWav(volume);
                    _player = new SoundPlayer(_stream);
                    _player.Load();
                    _player.Play();
                }
                catch
                {
                    try { Stop(); } catch { }
                }
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                try { if (_player != null) _player.Stop(); } catch { }
                try { if (_player != null) _player.Dispose(); } catch { }
                try { if (_stream != null) _stream.Dispose(); } catch { }
                _player = null;
                _stream = null;
            }
        }

        private static MemoryStream BuildWav(int volume)
        {
            const int rate = 44100;
            const double seconds = 1.45;
            const double freq = 960.0;

            int count = (int)(rate * seconds);
            int dataSize = count * 2;
            MemoryStream ms = new MemoryStream(44 + dataSize);
            BinaryWriter w = new BinaryWriter(ms);

            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataSize);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(dataSize);

            double amp = 0.16 * (volume / 100.0);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double env = 1.0;
                if (t < 0.025) env = t / 0.025;
                else if (t > seconds - 0.35) env = Math.Max(0, (seconds - t) / 0.35);

                double v = Math.Sin(2 * Math.PI * freq * t) * 0.85
                    + Math.Sin(2 * Math.PI * freq * 2 * t) * 0.10;
                short s = (short)(v * amp * env * 32767);
                w.Write(s);
            }

            w.Flush();
            ms.Position = 0;
            return ms;
        }
    }
}
