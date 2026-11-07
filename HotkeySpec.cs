using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace SCPSLAlert
{
    /// <summary>
    /// 快捷键描述，例如 "Ctrl+Alt+F8"。
    /// 支持 Ctrl / Alt / Shift / Win 修饰键，主键可以是 A-Z、0-9、F1-F12 和一批常用键。
    /// </summary>
    internal class HotkeySpec
    {
        public int Vk;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public bool Win;

        public bool IsValid
        {
            get { return Vk != 0; }
        }

        /// <summary>注册全局热键用的修饰符位。</summary>
        public int RegisterModifiers
        {
            get
            {
                int m = 0;
                if (Alt) m |= NativeMethods.MOD_ALT;
                if (Ctrl) m |= NativeMethods.MOD_CONTROL;
                if (Shift) m |= NativeMethods.MOD_SHIFT;
                if (Win) m |= 0x0008; // MOD_WIN
                return m | NativeMethods.MOD_NOREPEAT;
            }
        }

        /// <summary>在游戏里按的时候，Win 键基本用不上，这里只用于显示。</summary>
        public override string ToString()
        {
            if (!IsValid) return "（未设置）";
            string s = "";
            if (Ctrl) s += "Ctrl+";
            if (Alt) s += "Alt+";
            if (Shift) s += "Shift+";
            if (Win) s += "Win+";
            return s + KeyName(Vk);
        }

        /// <summary>
        /// 把钩子收到的一次按键，格式化成和 config 里一样的文本，
        /// 方便直接拿去查表。这样“读取配置”和“识别按键”用的是同一套写法。
        /// </summary>
        public static string Format(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            if (vk == 0) return null;
            string s = "";
            if (ctrl) s += "Ctrl+";
            if (alt) s += "Alt+";
            if (shift) s += "Shift+";
            if (win) s += "Win+";
            // KeyName 里 F1-F12 / 字母数字 / 具名键都会还原成能再 Parse 回来的写法
            return s + KeyName(vk);
        }

        public static HotkeySpec Parse(string text)
        {
            HotkeySpec h = new HotkeySpec();
            if (string.IsNullOrEmpty(text)) return h;

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;

                if (string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "Control", StringComparison.OrdinalIgnoreCase)) { h.Ctrl = true; continue; }
                if (string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase)) { h.Alt = true; continue; }
                if (string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase)) { h.Shift = true; continue; }
                if (string.Equals(p, "Win", StringComparison.OrdinalIgnoreCase)) { h.Win = true; continue; }

                int vk = KeyCode(p);
                if (vk != 0) h.Vk = vk;
            }
            return h;
        }

        private static readonly Dictionary<string, int> Named = BuildNamed();

        private static Dictionary<string, int> BuildNamed()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d["Space"] = 0x20;
            d["Tab"] = 0x09;
            d["Enter"] = 0x0D;
            d["Return"] = 0x0D;
            d["Esc"] = 0x1B;
            d["Escape"] = 0x1B;
            d["Backspace"] = 0x08;
            d["Insert"] = 0x2D;
            d["Delete"] = 0x2E;
            d["Home"] = 0x24;
            d["End"] = 0x23;
            d["PageUp"] = 0x21;
            d["PageDown"] = 0x22;
            d["Up"] = 0x26;
            d["Down"] = 0x28;
            d["Left"] = 0x25;
            d["Right"] = 0x27;
            d["`"] = 0xC0;
            d["-"] = 0xBD;
            d["="] = 0xBB;
            d["["] = 0xDB;
            d["]"] = 0xDD;
            d["\\"] = 0xDC;
            d[";"] = 0xBA;
            d["'"] = 0xDE;
            d[","] = 0xBC;
            d["."] = 0xBE;
            d["/"] = 0xBF;

            // 小键盘 —— 之前按小键盘会显示成 0x6D 这种看不懂的东西
            d["Num0"] = 0x60; d["Num1"] = 0x61; d["Num2"] = 0x62; d["Num3"] = 0x63;
            d["Num4"] = 0x64; d["Num5"] = 0x65; d["Num6"] = 0x66; d["Num7"] = 0x67;
            d["Num8"] = 0x68; d["Num9"] = 0x69;
            d["Num*"] = 0x6A;
            d["Num+"] = 0x6B;
            d["Num-"] = 0x6D;
            d["Num."] = 0x6E;
            d["Num/"] = 0x6F;

            d["CapsLock"] = 0x14;
            d["NumLock"] = 0x90;
            d["ScrollLock"] = 0x91;
            d["Pause"] = 0x13;
            d["PrintScreen"] = 0x2C;
            d["Apps"] = 0x5D;
            return d;
        }

        /// <summary>反向查表：虚拟键码 -> 界面上好看的写法（同码取先登记的那个）。</summary>
        private static readonly Dictionary<int, string> NamedByVk = BuildReverse();

        private static Dictionary<int, string> BuildReverse()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[0x20] = "Space"; d[0x09] = "Tab"; d[0x0D] = "Enter"; d[0x1B] = "Esc";
            d[0x08] = "Backspace"; d[0x2D] = "Insert"; d[0x2E] = "Delete";
            d[0x24] = "Home"; d[0x23] = "End"; d[0x21] = "PageUp"; d[0x22] = "PageDown";
            d[0x26] = "Up"; d[0x28] = "Down"; d[0x25] = "Left"; d[0x27] = "Right";
            d[0xC0] = "`"; d[0xBD] = "-"; d[0xBB] = "="; d[0xDB] = "["; d[0xDD] = "]";
            d[0xDC] = "\\"; d[0xBA] = ";"; d[0xDE] = "'"; d[0xBC] = ",";
            d[0xBE] = "."; d[0xBF] = "/";
            d[0x60] = "Num0"; d[0x61] = "Num1"; d[0x62] = "Num2"; d[0x63] = "Num3";
            d[0x64] = "Num4"; d[0x65] = "Num5"; d[0x66] = "Num6"; d[0x67] = "Num7";
            d[0x68] = "Num8"; d[0x69] = "Num9";
            d[0x6A] = "Num*"; d[0x6B] = "Num+"; d[0x6D] = "Num-"; d[0x6E] = "Num.";
            d[0x6F] = "Num/";
            d[0x14] = "CapsLock"; d[0x90] = "NumLock"; d[0x91] = "ScrollLock";
            d[0x13] = "Pause"; d[0x2C] = "PrintScreen"; d[0x5D] = "Apps";
            return d;
        }

        private static int KeyCode(string p)
        {
            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
            }
            if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f'))
            {
                int n;
                if (int.TryParse(p.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                    && n >= 1 && n <= 12)
                    return 0x70 + (n - 1);
            }
            int v;
            if (Named.TryGetValue(p, out v)) return v;
            return 0;
        }

        private static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
            string s;
            if (NamedByVk.TryGetValue(vk, out s)) return s;
            return "0x" + vk.ToString("X2");
        }
    }
}
