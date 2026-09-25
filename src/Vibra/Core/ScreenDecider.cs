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
    /// monitor while you click around on another monitor, and it loses it as soon as something else
    /// is really shown on top of it.
    /// </summary>
    internal static class ScreenDecider
    {
        /// <summary>A window that covers at least this share of a monitor owns it, focused or not.</summary>
        public const double CoverageThreshold = 0.5;

        /// <summary>
        /// Returns, for each monitor key, the window that owns it. Monitors that nothing owns
        /// (only desktop visible) are absent from the result.
        /// </summary>
        /// <param name="monitors">All monitors.</param>
        /// <param name="windows">Candidate windows in z-order, topmost first. Callers filter out
        /// invisible, minimized, cloaked, click-through and shell windows beforehand.</param>
        public static Dictionary<string, WindowInfo> FindOwners(IList<MonitorArea> monitors, IList<WindowInfo> windows)
        {
            var owners = new Dictionary<string, WindowInfo>(StringComparer.OrdinalIgnoreCase);
            if (monitors.Count == 0)
                return owners;

            foreach (var window in windows)
            {
                if (owners.Count == monitors.Count)
                    break;

                MonitorArea home = HomeMonitor(monitors, window.Bounds);
                if (home == null)
                    continue;

                foreach (var monitor in monitors)
                {
                    if (owners.ContainsKey(monitor.Key))
                        continue;

                    long overlap = window.Bounds.Intersect(monitor.Bounds).Area;
                    if (overlap == 0)
                        continue;

                    bool focusedHere = window.IsForeground && ReferenceEquals(home, monitor);
                    bool coversMonitor = overlap >= CoverageThreshold * monitor.Bounds.Area;
                    if (focusedHere || coversMonitor)
                        owners[monitor.Key] = window;
                }
            }

            return owners;
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
