using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Vibra.App
{
    /// <summary>
    /// Only one Vibra runs at a time. Starting the same exe again shows the running window;
    /// starting a different version (a new download, or after an update) replaces the running one.
    /// </summary>
    internal static class RunningInstance
    {
        public const string ShowEventName = @"Local\Vibra.ShowWindow";
        public const string QuitEventName = @"Local\Vibra.Quit";
        private const string MutexName = @"Local\Vibra.SingleInstance";

        private static string InfoPath => Path.Combine(AppPaths.DataDirectory, "instance.txt");

        private static string Identity => Application.ExecutablePath + "|" + Application.ProductVersion;

        /// <summary>
        /// Takes the single-instance lock, replacing an older/different running copy if needed.
        /// Returns null if this process should exit (the running copy was asked to show itself).
        /// </summary>
        public static Mutex Acquire(bool forceTakeOver)
        {
            var mutex = new Mutex(false, MutexName);
            if (TryWait(mutex, 0))
                return mutex;

            if (!forceTakeOver && IsSameAsRunning())
            {
                Signal(ShowEventName);
                mutex.Dispose();
                return null;
            }

            Log.Info("A different Vibra is running; taking over from it");
            Signal(QuitEventName);
            if (TryWait(mutex, 5000))
                return mutex;

            // Versions from before this existed don't listen for the quit signal.
            KillOtherCopies();
            if (TryWait(mutex, 5000))
                return mutex;

            mutex.Dispose();
            MessageBox.Show("Another copy of Vibra is still running.\n\nRight-click the Vibra icon in the tray, choose Exit, then start Vibra again.",
                "Vibra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        public static void Register()
        {
            try
            {
                File.WriteAllText(InfoPath, Identity);
            }
            catch (Exception ex)
            {
                Log.Error("Could not record running instance", ex);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (File.Exists(InfoPath) && string.Equals(File.ReadAllText(InfoPath).Trim(), Identity, StringComparison.OrdinalIgnoreCase))
                    File.Delete(InfoPath);
            }
            catch
            {
                // Harmless: the next start overwrites it.
            }
        }

        private static bool IsSameAsRunning()
        {
            try
            {
                return File.Exists(InfoPath) && string.Equals(File.ReadAllText(InfoPath).Trim(), Identity, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryWait(Mutex mutex, int milliseconds)
        {
            try
            {
                return mutex.WaitOne(milliseconds);
            }
            catch (AbandonedMutexException)
            {
                return true; // previous owner exited without releasing it; it's ours now
            }
        }

        private static void Signal(string name)
        {
            try
            {
                using (var handle = EventWaitHandle.OpenExisting(name))
                    handle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void KillOtherCopies()
        {
            using (var self = Process.GetCurrentProcess())
            {
                foreach (var process in Process.GetProcessesByName(self.ProcessName))
                {
                    using (process)
                    {
                        if (process.Id == self.Id)
                            continue;
                        try
                        {
                            Log.Info($"Closing old Vibra (pid {process.Id})");
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Could not close old Vibra", ex);
                        }
                    }
                }
            }
        }
    }
}
