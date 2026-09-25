using System;
using System.Runtime.InteropServices;
using System.Text;
using Vibra.App;

namespace Vibra.Platform
{
    /// <summary>
    /// Full path of a running process's exe without opening the process: asks the kernel's process
    /// list by id (the same information Task Manager's details view shows). Nothing touches the game,
    /// so anti-cheat has nothing to see.
    /// </summary>
    internal static class ProcessPaths
    {
        private const int SystemProcessIdInformation = 88;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESS_ID_INFORMATION
        {
            public IntPtr ProcessId;
            public UNICODE_STRING ImageName;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int systemInformationClass, ref SYSTEM_PROCESS_ID_INFORMATION information,
            int informationLength, out int returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        /// <summary>"C:\Games\Foo\foo.exe" for a process id, or null.</summary>
        public static string ImagePath(uint processId)
        {
            if (processId == 0)
                return null;
            try
            {
                string ntPath = QueryNtPath(processId, 1024);
                return ntPath == null ? null : ToDosPath(ntPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not look up the exe of process {processId}", ex);
                return null;
            }
        }

        private static string QueryNtPath(uint processId, int bufferBytes)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(bufferBytes);
                try
                {
                    var info = new SYSTEM_PROCESS_ID_INFORMATION
                    {
                        ProcessId = new IntPtr(processId),
                        ImageName = new UNICODE_STRING { Length = 0, MaximumLength = (ushort)bufferBytes, Buffer = buffer },
                    };
                    int status = NtQuerySystemInformation(SystemProcessIdInformation, ref info, Marshal.SizeOf<SYSTEM_PROCESS_ID_INFORMATION>(), out _);
                    if (status == 0)
                        return info.ImageName.Length == 0 ? null : Marshal.PtrToStringUni(buffer, info.ImageName.Length / 2);
                    if (status != StatusInfoLengthMismatch || info.ImageName.MaximumLength <= bufferBytes)
                        return null;
                    bufferBytes = info.ImageName.MaximumLength;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return null;
        }

        /// <summary>"\Device\HarddiskVolume3\Games\foo.exe" -> "C:\Games\foo.exe".</summary>
        private static string ToDosPath(string ntPath)
        {
            var target = new StringBuilder(260);
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string drive = letter + ":";
                target.Clear();
                if (QueryDosDevice(drive, target, target.Capacity) == 0)
                    continue;
                string device = target.ToString();
                if (ntPath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                    return drive + ntPath.Substring(device.Length);
            }
            return null;
        }
    }
}
