using System;
using System.Collections.Generic;

namespace Vibra.Core
{
    /// <summary>A top-level window as seen by the decider.</summary>
    internal sealed class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public uint ProcessId { get; set; }
        /// <summary>Executable file name, e.g. "cs2.exe". May be null if it could not be resolved.</summary>
        public string ExeName { get; set; }
        public ScreenRect Bounds { get; set; }
        public bool IsForeground { get; set; }
    }

    /// <summary>A monitor as seen by the decider.</summary>
    internal sealed class MonitorArea
    {
        public MonitorArea(string key, ScreenRect bounds)
        {
            Key = key;
            Bounds = bounds;
        }

        public string Key { get; }
        public ScreenRect Bounds { get; }
    }

    /// <summary>
    /// Decides which window "owns" each monitor, i.e. what the user is actually looking at there.
    ///
    /// This is deliberately based on what is visible rather than on keyboard focus: a game keeps its
    /// monitor while you click around on another monitor (or in Vibra itself), and it loses it as
    /// soon as something else is really shown on top of it.
    /// </summary>
    internal static class ScreenDecider
    {
        /// <summary>A window that covers at least this share of a monitor owns it, focused or not.</summary>
        public const double CoverageThreshold = 0.5;

        /// <summary>A game window counts as hidden once a single window above covers this share of it.</summary>
        public const double HiddenThreshold = 0.5;

        /// <summary>
        /// Returns, for each monitor key, the window that owns it. Monitors that nothing owns
        /// (only desktop visible) are absent from the result.
        ///
        /// Walking windows from the top, the first one that is
        /// <list type="bullet">
        /// <item>the focused window and mostly on this monitor, or</item>
        /// <item>covering at least half of the monitor, or</item>
        /// <item>a game mostly on this monitor that isn't mostly hidden by windows above it</item>
        /// </list>
        /// owns the monitor. The last rule keeps windowed games (like a game's launcher/lobby)
        /// boosted while you use another monitor.
        /// </summary>
        /// <param name="monitors">All monitors.</param>
        /// <param name="windows">Candidate windows in z-order, topmost first. Callers filter out
        /// invisible, minimized, cloaked, click-through and shell windows beforehand.</param>
        /// <param name="isGame">Whether a window belongs to a game. Optional.</param>
        public static Dictionary<string, WindowInfo> FindOwners(IList<MonitorArea> monitors, IList<WindowInfo> windows,
            Func<WindowInfo, bool> isGame = null)
        {
            var owners = new Dictionary<string, WindowInfo>(StringComparer.OrdinalIgnoreCase);
            if (monitors.Count == 0 || windows.Count == 0)
                return owners;

            var homes = new MonitorArea[windows.Count];
            for (int i = 0; i < windows.Count; i++)
                homes[i] = HomeMonitor(monitors, windows[i].Bounds);

            var above = new List<ScreenRect>();
            foreach (var monitor in monitors)
            {
                above.Clear();
                for (int i = 0; i < windows.Count; i++)
                {
                    var window = windows[i];
                    ScreenRect visible = window.Bounds.Intersect(monitor.Bounds);
                    long overlap = visible.Area;
                    if (overlap == 0)
                        continue;

                    bool home = ReferenceEquals(homes[i], monitor);
                    bool focusedHere = window.IsForeground && home;
                    bool coversMonitor = overlap >= CoverageThreshold * monitor.Bounds.Area;
                    bool visibleGame = home && isGame != null && isGame(window) && !IsMostlyHidden(visible, above);
                    if (focusedHere || coversMonitor || visibleGame)
                    {
                        owners[monitor.Key] = window;
                        break;
                    }
                    above.Add(visible);
                }
            }

            return owners;
        }

        private static bool IsMostlyHidden(ScreenRect target, List<ScreenRect> above)
        {
            foreach (var rect in above)
            {
                if (rect.Intersect(target).Area >= HiddenThreshold * target.Area)
                    return true;
            }
            return false;
        }

        /// <summary>The monitor a window mostly sits on, or null if it is entirely off-screen.</summary>
        public static MonitorArea HomeMonitor(IList<MonitorArea> monitors, ScreenRect bounds)
        {
            MonitorArea best = null;
            long bestOverlap = 0;
            foreach (var monitor in monitors)
            {
                long overlap = bounds.Intersect(monitor.Bounds).Area;
                if (overlap > bestOverlap)
                {
                    best = monitor;
                    bestOverlap = overlap;
                }
            }
            return best;
        }
    }
}
