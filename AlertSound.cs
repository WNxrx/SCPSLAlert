using System;
using System.Collections.Generic;
using System.Media;

namespace SCPSLAlert
{
    /// <summary>
    /// 提示音。
    ///
    /// 用 Windows 自带的系统音效（SystemSounds），不依赖任何外部 .wav 文件 ——
    /// 这样程序仍然是“单个 exe + 一个 config.ini”，丢到哪都能跑。
    ///
    /// 每个提示可以单独选一种，也可以整个关掉。
    /// </summary>
    internal static class AlertSound
    {
        /// <summary>可选音效的名字，顺序和 AlertConfig.SoundKind 对应。</summary>
        public static readonly string[] Names =
        {
            "不响",
            "提示（Asterisk）",
            "感叹（Exclamation）",
            "警告（Hand）",
            "疑问（Question）",
            "默认（Beep）"
        };

        private static readonly DateTime[] _lastPlay = new DateTime[64];

        /// <summary>外部总开关（设置里的“启用提示音”）。</summary>
        public static bool Master = true;

        /// <summary>
        /// 按类型响一声。
        /// cooldownKey 用提示下标即可 —— 同一条提示在冷却时间内不会连续响，
        /// 免得日志里连刷几行的时候响成一片。
        /// </summary>
        public static void Play(int kind, int cooldownKey, int cooldownSeconds)
        {
            if (!Master) return;
            if (kind <= 0) return;

            // 冷却：同一条提示短时间内不重复响
            if (cooldownKey >= 0 && cooldownKey < _lastPlay.Length)
            {
                DateTime now = DateTime.Now;
                double gap = (now - _lastPlay[cooldownKey]).TotalSeconds;
                if (cooldownSeconds > 0 && gap < cooldownSeconds) return;
                _lastPlay[cooldownKey] = now;
            }

            try
            {
                switch (kind)
                {
                    case 1: SystemSounds.Asterisk.Play(); break;
                    case 2: SystemSounds.Exclamation.Play(); break;
                    case 3: SystemSounds.Hand.Play(); break;
                    case 4: SystemSounds.Question.Play(); break;
                    case 5: SystemSounds.Beep.Play(); break;
                }
            }
            catch
            {
                // 有些系统把声音服务关了，响不了就算了，绝不能因此崩掉
            }
        }

        /// <summary>换新一局时把冷却状态清掉。</summary>
        public static void Reset()
        {
            for (int i = 0; i < _lastPlay.Length; i++) _lastPlay[i] = DateTime.MinValue;
        }
    }
}
