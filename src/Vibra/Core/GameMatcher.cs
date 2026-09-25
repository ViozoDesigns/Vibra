using System;
using System.IO;

namespace Vibra.Core
{
    /// <summary>Matches a saved game's executable against the executable that owns a window.</summary>
    internal static class GameMatcher
    {
        // Many Unreal Engine games ship a small launcher "Game.exe" next to the real game process
        // "Game-Win64-Shipping.exe". Picking either one should work.
        private static readonly string[] ShippingSuffixes =
        {
            "-win64-shipping",
            "-wingdk-shipping",
            "-win64-test",
        };

        public static bool IsExactMatch(string savedExe, string windowExe) =>
            !string.IsNullOrEmpty(savedExe) &&
            !string.IsNullOrEmpty(windowExe) &&
            string.Equals(savedExe, windowExe, StringComparison.OrdinalIgnoreCase);

        public static bool Matches(string savedExe, string windowExe)
        {
            if (string.IsNullOrEmpty(savedExe) || string.IsNullOrEmpty(windowExe))
                return false;
            if (IsExactMatch(savedExe, windowExe))
                return true;

            string savedStem = Path.GetFileNameWithoutExtension(savedExe);
            string windowStem = Path.GetFileNameWithoutExtension(windowExe);
            foreach (string suffix in ShippingSuffixes)
            {
                if (string.Equals(windowStem, savedStem + suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>File name of a path, accepting both '\\' and '/' on every platform.</summary>
        public static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            path = path.Trim();
            int slash = path.LastIndexOfAny(new[] { '\\', '/' });
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        /// <summary>A readable default name for an executable, e.g. "cs2.exe" -> "cs2".</summary>
        public static string DisplayNameFor(string exe)
        {
            string stem = Path.GetFileNameWithoutExtension(exe ?? string.Empty);
            foreach (string suffix in ShippingSuffixes)
            {
                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return stem.Substring(0, stem.Length - suffix.Length);
            }
            return stem;
        }
    }
}
