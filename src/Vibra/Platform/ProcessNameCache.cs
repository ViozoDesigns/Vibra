using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vibra.Interop;

namespace Vibra.Platform
{
    /// <summary>
    /// Resolves which executable owns a window.
    ///
    /// Uses a process list snapshot (the same thing Task Manager and Discord game detection use) and
    /// never opens a handle to the game process, so anti-cheat has nothing to object to.
    /// </summary>
    internal sealed class ProcessNameCache
    {
        private const int SnapshotReuseMs = 250;
        private const int MaxCachedWindows = 512;

        private readonly Dictionary<(IntPtr, uint), string> byWindow = new Dictionary<(IntPtr, uint), string>();
        private Dictionary<uint, string> snapshot = new Dictionary<uint, string>();
        private int snapshotTakenAt = Environment.TickCount - SnapshotReuseMs * 2;

        public string Get(IntPtr hwnd, uint pid)
        {
            if (pid == 0)
                return null;

            var key = (hwnd, pid);
            if (byWindow.TryGetValue(key, out string exe))
                return exe;

            // A window we have not seen before. Its process may have started after the last
            // snapshot (or reused an old id), so take a fresh one unless we just did.
            if (unchecked(Environment.TickCount - snapshotTakenAt) > SnapshotReuseMs || !snapshot.ContainsKey(pid))
                TakeSnapshot();

            snapshot.TryGetValue(pid, out exe);
            if (byWindow.Count >= MaxCachedWindows)
                byWindow.Clear();
            byWindow[key] = exe;
            return exe;
        }

        private void TakeSnapshot()
        {
            var fresh = new Dictionary<uint, string>();
            IntPtr handle = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (handle == NativeMethods.INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                return;
            try
            {
                var entry = new NativeMethods.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>() };
                if (NativeMethods.Process32First(handle, ref entry))
                {
                    do
                    {
                        fresh[entry.th32ProcessID] = entry.szExeFile;
                    }
                    while (NativeMethods.Process32Next(handle, ref entry));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
            snapshot = fresh;
            snapshotTakenAt = Environment.TickCount;
        }
    }
}
