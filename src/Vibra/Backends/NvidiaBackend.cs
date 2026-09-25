using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Vibra.App;
using Vibra.Core;
using Vibra.Interop;

namespace Vibra.Backends
{
    /// <summary>
    /// NVIDIA Digital Vibrance through NVAPI. The effect is applied by the display engine while the
    /// image is sent to the monitor, so it costs no GPU time or FPS, and nothing is loaded into games.
    /// </summary>
    internal sealed class NvidiaBackend : IColorBackend
    {
        // Function ids from the public NVAPI SDK (nvapi_interface.h).
        private const uint IdInitialize = 0x0150E828;
        private const uint IdEnumNvidiaDisplayHandle = 0x9ABDD40D;
        private const uint IdGetAssociatedNvidiaDisplayName = 0x22A78B05;
        private const uint IdGetDvcInfo = 0x4085DE45;
        private const uint IdSetDvcLevel = 0x172409B4;

        private const int NvapiOk = 0;
        private const int ShortStringMax = 64;
        private const int MaxDisplays = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct NV_DISPLAY_DVC_INFO
        {
            public uint version;
            public int currentLevel;
            public int minLevel;
            public int maxLevel;
        }

        // MAKE_NVAPI_VERSION(NV_DISPLAY_DVC_INFO, 1)
        private static readonly uint DvcInfoVersion = (uint)Marshal.SizeOf<NV_DISPLAY_DVC_INFO>() | (1u << 16);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryInterfaceFn(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumNvidiaDisplayHandleFn(int thisEnum, out IntPtr displayHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetAssociatedNvidiaDisplayNameFn(IntPtr displayHandle, [Out] byte[] name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDvcInfoFn(IntPtr displayHandle, uint outputId, ref NV_DISPLAY_DVC_INFO info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDvcLevelFn(IntPtr displayHandle, uint outputId, int level);

        private readonly Dictionary<string, IntPtr> handles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Min, int Max)> ranges = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
        private EnumNvidiaDisplayHandleFn enumDisplayHandle;
        private GetAssociatedNvidiaDisplayNameFn getDisplayName;
        private GetDvcInfoFn getDvcInfo;
        private SetDvcLevelFn setDvcLevel;

        public NvidiaBackend()
        {
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Status = "Could not load the NVIDIA driver interface.";
                Log.Error("NVAPI initialisation failed", ex);
            }
        }

        public bool IsAvailable { get; private set; }
        public string Status { get; private set; } = "Not initialised";

        private void Initialize()
        {
            string dll = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";

            // Only ever load the driver's copy from System32, never a DLL placed next to the exe.
            IntPtr module = NativeMethods.LoadLibraryEx(dll, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (module == IntPtr.Zero)
            {
                Status = "No NVIDIA driver found. Vibra currently needs an NVIDIA GPU.";
                Log.Info($"{dll} not found (error {Marshal.GetLastWin32Error()})");
                return;
            }

            IntPtr queryPtr = NativeMethods.GetProcAddress(module, "nvapi_QueryInterface");
            if (queryPtr == IntPtr.Zero)
            {
                Status = "The installed NVIDIA driver is not supported.";
                Log.Warn("nvapi_QueryInterface export missing");
                return;
            }

            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(queryPtr);
            var initialize = Resolve<InitializeFn>(query, IdInitialize);
            enumDisplayHandle = Resolve<EnumNvidiaDisplayHandleFn>(query, IdEnumNvidiaDisplayHandle);
            getDisplayName = Resolve<GetAssociatedNvidiaDisplayNameFn>(query, IdGetAssociatedNvidiaDisplayName);
            getDvcInfo = Resolve<GetDvcInfoFn>(query, IdGetDvcInfo);
            setDvcLevel = Resolve<SetDvcLevelFn>(query, IdSetDvcLevel);

            if (initialize == null || enumDisplayHandle == null || getDisplayName == null || getDvcInfo == null || setDvcLevel == null)
            {
                Status = "The installed NVIDIA driver does not expose digital vibrance.";
                Log.Warn("One or more NVAPI functions could not be resolved");
                return;
            }

            int status = initialize();
            if (status != NvapiOk)
            {
                Status = $"NVIDIA driver refused to initialise (code {status}).";
                Log.Warn($"NvAPI_Initialize returned {status}");
                return;
            }

            IsAvailable = true;
            Status = "NVIDIA";
            Refresh();
        }

        private static T Resolve<T>(QueryInterfaceFn query, uint id) where T : class
        {
            IntPtr ptr = query(id);
            return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        public void Refresh()
        {
            if (!IsAvailable)
                return;

            handles.Clear();
            ranges.Clear();
            var nameBuffer = new byte[ShortStringMax];
            for (int i = 0; i < MaxDisplays; i++)
            {
                if (enumDisplayHandle(i, out IntPtr handle) != NvapiOk)
                    break;

                Array.Clear(nameBuffer, 0, nameBuffer.Length);
                if (getDisplayName(handle, nameBuffer) != NvapiOk)
                    continue;

                int length = Array.IndexOf(nameBuffer, (byte)0);
                string name = Encoding.ASCII.GetString(nameBuffer, 0, length < 0 ? nameBuffer.Length : length);
                if (name.Length > 0)
                    handles[name] = handle;
            }
        }

        public bool Supports(string gdiDeviceName) =>
            IsAvailable && gdiDeviceName != null && handles.ContainsKey(gdiDeviceName);

        public bool TrySetVibrance(string gdiDeviceName, int percent)
        {
            // Handles can go stale after mode switches; re-enumerate once and retry.
            return TrySetOnce(gdiDeviceName, percent) || (RefreshAndCheck(gdiDeviceName) && TrySetOnce(gdiDeviceName, percent));
        }

        public int? TryGetVibrance(string gdiDeviceName)
        {
            if (TryReadInfo(gdiDeviceName, out NV_DISPLAY_DVC_INFO info) ||
                (RefreshAndCheck(gdiDeviceName) && TryReadInfo(gdiDeviceName, out info)))
            {
                return VibranceScale.ToPercent(info.currentLevel, info.minLevel, info.maxLevel);
            }
            return null;
        }

        private bool TrySetOnce(string gdiDeviceName, int percent)
        {
            if (!IsAvailable || gdiDeviceName == null || !handles.TryGetValue(gdiDeviceName, out IntPtr handle))
                return false;

            // Fast path for alt-tab: once the range is known, a switch is a single driver call.
            if (!ranges.TryGetValue(gdiDeviceName, out var range))
            {
                if (!TryReadInfo(gdiDeviceName, out NV_DISPLAY_DVC_INFO info))
                    return false;
                range = (info.minLevel, info.maxLevel);
            }

            int level = VibranceScale.ToLevel(percent, range.Min, range.Max);
            return setDvcLevel(handle, 0, level) == NvapiOk;
        }

        private bool TryReadInfo(string gdiDeviceName, out NV_DISPLAY_DVC_INFO info)
        {
            info = new NV_DISPLAY_DVC_INFO { version = DvcInfoVersion };
            if (!IsAvailable || gdiDeviceName == null || !handles.TryGetValue(gdiDeviceName, out IntPtr handle))
                return false;
            if (getDvcInfo(handle, 0, ref info) != NvapiOk || info.maxLevel <= info.minLevel)
                return false;
            ranges[gdiDeviceName] = (info.minLevel, info.maxLevel);
            return true;
        }

        private bool RefreshAndCheck(string gdiDeviceName)
        {
            if (!IsAvailable)
                return false;
            Refresh();
            return Supports(gdiDeviceName);
        }
    }
}
