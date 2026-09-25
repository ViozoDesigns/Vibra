using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Vibra.App;

namespace Vibra
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, @"Local\Vibra.SingleInstance", out bool firstInstance))
            {
                if (!firstInstance)
                {
                    // Already running: bring the existing window up instead of starting twice.
                    try
                    {
                        using (var show = EventWaitHandle.OpenExisting(VibraApp.ShowEventName))
                            show.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                    }
                    return;
                }

                Log.Init();
                Log.Info($"Vibra {Application.ProductVersion} starting");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Error("Unhandled UI exception", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log.Error("Fatal exception", e.ExceptionObject as Exception);
                    VibraApp.Current?.EmergencyRestore();
                };

                // Needed so background work (library scan) can report back to the UI thread.
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                bool startHidden = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
                bool openSettings = args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));
                using (var app = new VibraApp(startHidden, openSettings))
                    Application.Run(app);

                Log.Info("Vibra exited");
            }
        }
    }
}
