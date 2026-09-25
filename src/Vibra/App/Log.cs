using System;
using System.IO;

namespace Vibra.App
{
    internal static class AppPaths
    {
        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vibra");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
        public static string LogFile => Path.Combine(DataDirectory, "vibra.log");
    }

    /// <summary>Tiny append-only log in %APPDATA%\Vibra\vibra.log, for troubleshooting.</summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();
        private static string path;

        public static void Init()
        {
            try
            {
                path = AppPaths.LogFile;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                    info.Delete();
            }
            catch
            {
                path = null;
            }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

        private static void Write(string level, string message)
        {
            if (path == null)
                return;
            try
            {
                lock (Gate)
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
