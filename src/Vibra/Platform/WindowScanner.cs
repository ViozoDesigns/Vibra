using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Vibra.App;
using Vibra.Core;
using Vibra.Interop;

namespace Vibra.Platform
{
    /// <summary>A running app with a window, offered when adding a game.</summary>
    internal sealed class RunningApp
    {
        public string Exe { get; set; }
        public string Title { get; set; }
        public IntPtr Window { get; set; }
    }

    /// <summary>
    /// Reads the list of top-level windows (read-only, no hooks into other processes) and filters
    /// it down to windows that actually cover content on screen.
    /// </summary>
    internal sealed class WindowScanner
    {
        private const int MinWindowSize = 64;

        // Transient shell UI that sits on top of a game without replacing it. Treating these as
        // "see-through" is what stops the colors flipping while you hold Alt+Tab, press the
        // Windows key or glance at a notification.
        private static readonly HashSet<string> SeeThroughClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Shell_TrayWnd",                        // taskbar
            "Shell_SecondaryTrayWnd",               // taskbar on other monitors
            "Progman",                              // desktop
            "WorkerW",                              // desktop / wallpaper
            "MultitaskingViewFrame",                // Alt+Tab (Windows 10)
            "XamlExplorerHostIslandWindow",         // Alt+Tab / Task View / Snap (Windows 11)
            "ForegroundStaging",                    // transient window used during Alt+Tab
            "TaskSwitcherWnd",                      // classic Alt+Tab
            "TaskSwitcherOverlayWnd",
            "Windows.UI.Core.CoreWindow",           // Start, Search, Action Center, notifications
            "NotifyIconOverflowWindow",             // tray overflow
            "TopLevelWindowForOverflowXamlIsland",  // tray overflow (Windows 11)
            "Shell_InputSwitchTopLevelWindow",      // language switcher
            "#32768",                               // menus
            "tooltips_class32",
            "SysShadow",
            "Xaml_WindowedPopupClass",
        };

        // Overlays and shell hosts that draw over a game rather than replacing it.
        private static readonly HashSet<string> SeeThroughProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShellExperienceHost.exe",
            "StartMenuExperienceHost.exe",
            "SearchHost.exe",
            "SearchApp.exe",
            "TextInputHost.exe",
            "NVIDIA Overlay.exe",
            "NVIDIA Share.exe",
            "GameBar.exe",
            "GameBarFTServer.exe",
        };

        // Never offered as games / never adjusted by hotkeys.
        private static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe",
            "ApplicationFrameHost.exe",
            "SystemSettings.exe",
            "Taskmgr.exe",
            "LockApp.exe",
            "dwm.exe",
            "csrss.exe",
            "winlogon.exe",
        };

        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private readonly ProcessNameCache processNames = new ProcessNameCache();
        private readonly StringBuilder classBuffer = new StringBuilder(256);
        private readonly NativeMethods.EnumWindowsProc enumProc;
        private List<WindowInfo> scanResults;
        private IntPtr scanForeground;

        public WindowScanner()
        {
            enumProc = OnEnumWindow;
        }

        /// <summary>Windows that can own a monitor, in z-order (topmost first).</summary>
        public List<WindowInfo> Scan()
        {
            scanForeground = NativeMethods.GetForegroundWindow();
            scanResults = new List<WindowInfo>(32);
            NativeMethods.EnumWindows(enumProc, IntPtr.Zero);
            var results = scanResults;
            scanResults = null;
            return results;
        }

        private bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                var info = Inspect(hwnd);
                if (info != null)
                    scanResults.Add(info);
            }
            catch (Exception ex)
            {
                // Never let an exception unwind through the native EnumWindows frame.
                Log.Error("Window inspection failed", ex);
            }
            return true;
        }

        private WindowInfo Inspect(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return null;

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return null;
            if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                return null;
            if ((exStyle & NativeMethods.WS_EX_LAYERED) != 0 && (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
                return null; // click-through overlay (FPS counters, crosshair apps, recording widgets)

            if (IsCloaked(hwnd))
                return null; // hidden by DWM: other virtual desktop, suspended UWP app

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                return null;
            var bounds = new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < MinWindowSize || bounds.Height < MinWindowSize)
                return null;

            if (SeeThroughClasses.Contains(GetClassName(hwnd)))
                return null;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == ownProcessId)
                return null; // Vibra's own windows never change colors, so you can tune while the game is visible

            string exe = processNames.Get(hwnd, pid);
            if (exe != null && SeeThroughProcesses.Contains(exe))
                return null;

            return new WindowInfo
            {
                Handle = hwnd,
                ProcessId = pid,
                ExeName = exe,
                Bounds = bounds,
                IsForeground = hwnd == scanForeground,
            };
        }

        /// <summary>The app behind the foreground window, or null if it is the shell, Vibra or an overlay.</summary>
        public RunningApp DescribeForeground()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == ownProcessId)
                return null;
            string exe = processNames.Get(hwnd, pid);
            if (!IsGameCandidate(exe))
                return null;
            return new RunningApp { Exe = exe, Title = GetTitle(hwnd), Window = hwnd };
        }

        /// <summary>Apps with a visible window, for the "Add game" menu.</summary>
        public List<RunningApp> ListRunningApps()
        {
            var apps = new List<RunningApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            NativeMethods.EnumWindowsProc callback = (hwnd, lParam) =>
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(hwnd) || IsCloaked(hwnd))
                        return true;
                    int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                        return true;
                    if (SeeThroughClasses.Contains(GetClassName(hwnd)))
                        return true;
                    string title = GetTitle(hwnd);
                    if (string.IsNullOrWhiteSpace(title))
                        return true;
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == 0 || pid == ownProcessId)
                        return true;
                    string exe = processNames.Get(hwnd, pid);
                    if (IsGameCandidate(exe) && seen.Add(exe))
                        apps.Add(new RunningApp { Exe = exe, Title = title.Trim(), Window = hwnd });
                }
                catch (Exception ex)
                {
                    Log.Error("Listing apps failed", ex);
                }
                return true;
            };
            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            apps.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
            return apps;
        }

        private static bool IsGameCandidate(string exe) =>
            !string.IsNullOrEmpty(exe) && !NotGames.Contains(exe) && !SeeThroughProcesses.Contains(exe);

        private static bool IsCloaked(IntPtr hwnd) =>
            NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

        private string GetClassName(IntPtr hwnd)
        {
            classBuffer.Clear();
            NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Capacity);
            return classBuffer.ToString();
        }

        private static string GetTitle(IntPtr hwnd)
        {
            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
                return string.Empty;
            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}
