using System;
using System.Globalization;
using System.Text;

namespace SCPSLAlert
{
    /// <summary>
    /// 时间输入的解析（原来在 CalibratePanel 里，现在合并进战术面板的「校准」页）。
    ///
    /// 用户习惯直接照游戏 HUD 打 <c>11.22</c>（11 分 22 秒），也常常打
    /// <c>6:10</c> / <c>370</c> / <c>1:02:03</c>。这里统一处理，
    /// 并且把中文输入法下的全角符号（１１．２２ / 6：10）也换算成半角。
    /// 解析不出来一律返回 false，绝不抛异常。
    /// </summary>
    internal static class TimeInput
    {
        /// <summary>"回合已进行"专用：支持 11.22 = 11 分 22 秒。</summary>
        public static bool TryMinutes(string s, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = Normalize(s);

            if (s.IndexOf(':') >= 0) return TrySeconds(s, out seconds);

            int dot = s.IndexOf('.');
            if (dot < 0)
            {
                // 没有小数点：填的就是秒数
                return TrySeconds(s, out seconds);
            }

            string a = s.Substring(0, dot).Trim();
            string b = s.Substring(dot + 1).Trim();
            double mins, secs;

            if (a.Length == 0) a = "0";
            if (b.Length == 0) b = "0";
            if (!double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out mins)) return false;
            if (!double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out secs)) return false;

            if (b.Length <= 2 && secs < 60)
            {
                seconds = mins * 60.0 + secs;   // 11.22 → 11 分 22 秒
                return true;
            }

            seconds = mins + secs / 100.0;      // 11.225 → 11.225 分钟
            seconds *= 60.0;
            return true;
        }

        /// <summary>数字或时间都能解析：200 / 3:20（分:秒）/ 1:02:03。</summary>
        public static bool TrySeconds(string s, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = Normalize(s);
            if (s.Length == 0) return false;

            double plain;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out plain))
            {
                seconds = plain;
                return true;
            }

            string[] parts = s.Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out v)) return false;
                total = total * 60 + v;
            }
            seconds = total;
            return true;
        }

        /// <summary>中文输入法的全角数字 / 冒号 / 句点换成半角，并去掉空格。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\u3000') continue;
                if (c >= '０' && c <= '９') c = (char)('0' + (c - '０'));
                else if (c == '：') c = ':';
                else if (c == '．' || c == '。' || c == '，' || c == '、') c = '.';
                b.Append(c);
            }
            return b.ToString();
        }
    }
}
