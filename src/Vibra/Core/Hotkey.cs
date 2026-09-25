using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibra.Core
{
    /// <summary>A global keyboard shortcut: modifier flags (as RegisterHotKey expects) plus a virtual-key code.</summary>
    internal readonly struct Hotkey : IEquatable<Hotkey>
    {
        public const int Alt = 0x1;
        public const int Control = 0x2;
        public const int Shift = 0x4;

        public Hotkey(int modifiers, int key)
        {
            Modifiers = modifiers & (Alt | Control | Shift);
            Key = key;
        }

        public static Hotkey None => default;

        public int Modifiers { get; }
        public int Key { get; }
        public bool IsNone => Key == 0;

        /// <summary>Plain keys are only allowed for F1-F24; everything else needs Ctrl, Alt or Shift.</summary>
        public bool IsAllowed => IsNone || Modifiers != 0 || KeyNames.IsFunctionKey(Key);

        public override string ToString()
        {
            if (IsNone)
                return "None";
            var parts = new List<string>();
            if ((Modifiers & Control) != 0)
                parts.Add("Ctrl");
            if ((Modifiers & Alt) != 0)
                parts.Add("Alt");
            if ((Modifiers & Shift) != 0)
                parts.Add("Shift");
            parts.Add(KeyNames.NameOf(Key) ?? $"0x{Key:X2}");
            return string.Join("+", parts);
        }

        /// <summary>Parses "Ctrl+Alt+PgUp"; empty or "None" gives <see cref="None"/>.</summary>
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = None;
            if (text == null)
                return false;
            text = text.Trim();
            if (text.Length == 0 || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] parts = text.Split('+').Select(p => p.Trim()).ToArray();
            if (parts.Any(p => p.Length == 0))
                return false;

            int modifiers = 0;
            foreach (string part in parts.Take(parts.Length - 1))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= Control;
                        break;
                    case "alt":
                        modifiers |= Alt;
                        break;
                    case "shift":
                        modifiers |= Shift;
                        break;
                    default:
                        return false;
                }
            }

            int key = KeyNames.KeyOf(parts[parts.Length - 1]);
            if (key == 0)
                return false;
            hotkey = new Hotkey(modifiers, key);
            return true;
        }

        public bool Equals(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object obj) => obj is Hotkey other && Equals(other);
        public override int GetHashCode() => (Modifiers << 16) ^ Key;
    }

    /// <summary>Names for the virtual-key codes Vibra accepts as shortcut keys.</summary>
    internal static class KeyNames
    {
        private static readonly Dictionary<int, string> Names = BuildNames();
        private static readonly Dictionary<string, int> Keys = BuildKeys();

        public static bool IsFunctionKey(int vk) => vk >= 0x70 && vk <= 0x87;

        public static bool IsSupported(int vk) => Names.ContainsKey(vk);

        public static string NameOf(int vk) => Names.TryGetValue(vk, out string name) ? name : null;

        public static int KeyOf(string name) => name != null && Keys.TryGetValue(name.Trim(), out int vk) ? vk : 0;

        private static Dictionary<int, string> BuildNames()
        {
            var names = new Dictionary<int, string>
            {
                [0x08] = "Backspace",
                [0x13] = "Pause",
                [0x20] = "Space",
                [0x21] = "PgUp",
                [0x22] = "PgDn",
                [0x23] = "End",
                [0x24] = "Home",
                [0x25] = "Left",
                [0x26] = "Up",
                [0x27] = "Right",
                [0x28] = "Down",
                [0x2D] = "Insert",
                [0x2E] = "Delete",
                [0x6A] = "NumMultiply",
                [0x6B] = "NumPlus",
                [0x6D] = "NumMinus",
                [0x6E] = "NumDecimal",
                [0x6F] = "NumDivide",
                [0x91] = "ScrollLock",
                [0xBA] = ";",
                [0xBB] = "=",
                [0xBC] = ",",
                [0xBD] = "-",
                [0xBE] = ".",
                [0xBF] = "/",
                [0xC0] = "`",
                [0xDB] = "[",
                [0xDC] = "\\",
                [0xDD] = "]",
                [0xDE] = "'",
            };
            for (int c = 'A'; c <= 'Z'; c++)
                names[c] = ((char)c).ToString();
            for (int d = 0; d <= 9; d++)
            {
                names[0x30 + d] = d.ToString();
                names[0x60 + d] = "Num" + d;
            }
            for (int f = 1; f <= 24; f++)
                names[0x6F + f] = "F" + f;
            return names;
        }

        private static Dictionary<string, int> BuildKeys()
        {
            var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Names)
                keys[pair.Value] = pair.Key;
            keys["PageUp"] = 0x21;
            keys["PageDown"] = 0x22;
            keys["Del"] = 0x2E;
            keys["Ins"] = 0x2D;
            return keys;
        }
    }
}
