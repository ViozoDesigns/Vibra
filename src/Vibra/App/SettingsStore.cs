using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Vibra.Core;

namespace Vibra.App
{
    /// <summary>Loads and saves <see cref="AppSettings"/>; saves are batched so slider drags don't hit the disk.</summary>
    internal sealed class SettingsStore : IDisposable
    {
        private readonly string path;
        private readonly Timer saveTimer = new Timer { Interval = 400 };

        private SettingsStore(string path, AppSettings settings)
        {
            this.path = path;
            Settings = settings;
            saveTimer.Tick += (s, e) => Save();
        }

        public AppSettings Settings { get; }

        public static SettingsStore Load()
        {
            string path = AppPaths.SettingsFile;
            AppSettings settings = null;
            try
            {
                if (File.Exists(path))
                    settings = SettingsSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Error("Could not read settings, starting fresh", ex);
                TryBackupBrokenFile(path);
            }

            if (settings == null)
            {
                settings = new AppSettings();
                settings.Normalize();
            }
            return new SettingsStore(path, settings);
        }

        public void SaveSoon()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void Save()
        {
            saveTimer.Stop();
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, SettingsSerializer.ToJson(Settings), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings", ex);
            }
        }

        private static void TryBackupBrokenFile(string path)
        {
            try
            {
                File.Copy(path, path + ".broken", true);
            }
            catch
            {
                // Nothing more we can do.
            }
        }

        public void Dispose()
        {
            if (saveTimer.Enabled)
                Save();
            saveTimer.Dispose();
        }
    }
}
