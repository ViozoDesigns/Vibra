using System;
using System.Windows.Forms;
using Vibra.Interop;

namespace Vibra.Platform
{
    /// <summary>
    /// Invisible top-level window that receives system broadcasts (display mode changes, resume,
    /// unlock, shutdown) and global hotkeys. It must be top-level, not message-only, or Windows
    /// would not send it the broadcasts.
    /// </summary>
    internal sealed class MessageWindow : NativeWindow, IDisposable
    {
        public const int ShowRequestMessage = NativeMethods.WM_APP + 1;

        private readonly bool sessionNotifications;

        public MessageWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "Vibra.MessageWindow",
                ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
            });
            sessionNotifications = NativeMethods.WTSRegisterSessionNotification(Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        }

        public event Action DisplayChanged;
        public event Action SystemResumed;
        public event Action SessionEnding;
        public event Action ShowRequested;
        public event Action<int> HotkeyPressed;

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_DISPLAYCHANGE:
                    DisplayChanged?.Invoke();
                    break;

                case NativeMethods.WM_POWERBROADCAST:
                    int power = m.WParam.ToInt32();
                    if (power == NativeMethods.PBT_APMRESUMEAUTOMATIC || power == NativeMethods.PBT_APMRESUMESUSPEND)
                        SystemResumed?.Invoke();
                    break;

                case NativeMethods.WM_WTSSESSION_CHANGE:
                    int session = m.WParam.ToInt32();
                    if (session == NativeMethods.WTS_SESSION_UNLOCK || session == NativeMethods.WTS_CONSOLE_CONNECT)
                        SystemResumed?.Invoke();
                    break;

                case NativeMethods.WM_ENDSESSION:
                    if (m.WParam != IntPtr.Zero)
                        SessionEnding?.Invoke();
                    break;

                case NativeMethods.WM_HOTKEY:
                    HotkeyPressed?.Invoke(m.WParam.ToInt32());
                    break;

                case ShowRequestMessage:
                    ShowRequested?.Invoke();
                    break;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                if (sessionNotifications)
                    NativeMethods.WTSUnRegisterSessionNotification(Handle);
                DestroyHandle();
            }
        }
    }
}
