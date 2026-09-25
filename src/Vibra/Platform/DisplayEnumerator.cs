using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vibra.App;
using Vibra.Core;
using Vibra.Interop;

namespace Vibra.Platform
{
    internal sealed class DisplayInfo
    {
        /// <summary>GDI device name, e.g. "\\.\DISPLAY1". Changes between reboots; use for runtime calls only.</summary>
        public string GdiName { get; set; }
        /// <summary>Stable identifier for the physical monitor (device path), used to key settings.</summary>
        public string Id { get; set; }
        /// <summary>Monitor model name, e.g. "LG ULTRAGEAR".</summary>
        public string Name { get; set; }
        public ScreenRect Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public int Number { get; set; }

        public bool SameAs(DisplayInfo other) =>
            other != null &&
            string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GdiName, other.GdiName, StringComparison.OrdinalIgnoreCase) &&
            Bounds.Equals(other.Bounds) &&
            IsPrimary == other.IsPrimary;
    }

    internal static class DisplayEnumerator
    {
        public static List<DisplayInfo> GetDisplays()
        {
            var targets = QueryMonitorNames();
            var displays = new List<DisplayInfo>();

            NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                    return true;

                targets.TryGetValue(info.szDevice, out var target);
                int number = ParseDisplayNumber(info.szDevice);
                displays.Add(new DisplayInfo
                {
                    GdiName = info.szDevice,
                    Id = string.IsNullOrEmpty(target.Path) ? info.szDevice : target.Path,
                    Name = string.IsNullOrWhiteSpace(target.Name) ? $"Display {number}" : target.Name.Trim(),
                    Bounds = new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                    IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    Number = number,
                });
                return true;
            };

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            return displays
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Bounds.Left)
                .ThenBy(d => d.Bounds.Top)
                .ToList();
        }

        /// <summary>The GDI name of the monitor a window is mostly on.</summary>
        public static string GdiNameForWindow(IntPtr hwnd)
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return null;
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
        }

        private static int ParseDisplayNumber(string gdiName)
        {
            int i = gdiName.Length;
            while (i > 0 && char.IsDigit(gdiName[i - 1]))
                i--;
            return int.TryParse(gdiName.Substring(i), out int n) ? n : 0;
        }

        private struct MonitorTarget
        {
            public string Name;
            public string Path;
        }

        /// <summary>Maps GDI names to monitor model names and device paths via the DisplayConfig API.</summary>
        private static Dictionary<string, MonitorTarget> QueryMonitorNames()
        {
            var result = new Dictionary<string, MonitorTarget>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                    return result;

                var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
                if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                    return result;

                for (int i = 0; i < pathCount; i++)
                {
                    var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    source.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                    source.header.adapterId = paths[i].sourceInfo.adapterId;
                    source.header.id = paths[i].sourceInfo.id;
                    if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrEmpty(source.viewGdiDeviceName))
                        continue;

                    var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) != 0)
                        continue;

                    if (!result.ContainsKey(source.viewGdiDeviceName))
                    {
                        result[source.viewGdiDeviceName] = new MonitorTarget
                        {
                            Name = target.monitorFriendlyDeviceName,
                            Path = target.monitorDevicePath,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not query monitor names", ex);
            }
            return result;
        }
    }
}
