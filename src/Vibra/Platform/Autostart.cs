using System;
using System.Windows.Forms;
using Microsoft.Win32;
using Vibra.App;

namespace Vibra.Platform
{
    /// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Vibra";

        private static string Command => $"\"{Application.ExecutablePath}\" --minimized";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                        return key?.GetValue(ValueName) is string;
                }
                catch (Exception ex)
                {
                    Log.Error("Could not read autostart setting", ex);
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(ValueName, Command);
                else
                    key.DeleteValue(ValueName, false);
            }
        }

        /// <summary>Keeps the Run entry pointing at this exe if the user moved it.</summary>
        public static void RepairPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, Command);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not update autostart path", ex);
            }
        }
    }
}
