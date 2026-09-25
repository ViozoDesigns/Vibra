using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Vibra.App;

namespace Vibra.Platform
{
    /// <summary>Gets app icons from exe/ico files or from a running app's window.</summary>
    internal static class IconExtractor
    {
        private const int WM_GETICON = 0x007F;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const int GCLP_HICON = -14;
        private const uint SMTO_BLOCK = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
            IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        /// <summary>The icon stored in an exe or ico file at roughly <paramref name="size"/> pixels, or null.</summary>
        public static Bitmap FromFile(string path, int size)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint count = PrivateExtractIcons(path, 0, size, size, handles, ids, 1, 0);
                if (count > 0 && count != uint.MaxValue && handles[0] != IntPtr.Zero)
                {
                    try
                    {
                        using (var icon = Icon.FromHandle(handles[0]))
                            return icon.ToBitmap();
                    }
                    finally
                    {
                        DestroyIcon(handles[0]);
                    }
                }

                using (var icon = Icon.ExtractAssociatedIcon(path))
                    return icon?.ToBitmap();
            }
            catch (Exception ex)
            {
                Log.Error($"Could not read icon from {path}", ex);
                return null;
            }
        }

        /// <summary>
        /// The icon a window shows in the taskbar, or null. Uses the same request the taskbar and
        /// Alt+Tab make, with a short timeout so a busy game can never stall Vibra.
        /// </summary>
        public static Bitmap FromWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return null;
            try
            {
                IntPtr handle = IntPtr.Zero;
                foreach (int which in new[] { ICON_BIG, ICON_SMALL2 })
                {
                    if (SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(which), IntPtr.Zero, SMTO_BLOCK | SMTO_ABORTIFHUNG, 100, out handle) != IntPtr.Zero && handle != IntPtr.Zero)
                        break;
                    handle = IntPtr.Zero;
                }
                if (handle == IntPtr.Zero)
                    handle = GetClassLongPtr(hwnd, GCLP_HICON);
                if (handle == IntPtr.Zero)
                    return null;

                // The icon belongs to the window's owner: copy it, never destroy it.
                using (var icon = Icon.FromHandle(handle))
                    return icon.ToBitmap();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read window icon", ex);
                return null;
            }
        }
    }
}
