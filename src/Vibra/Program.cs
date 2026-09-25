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
            bool HasArg(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

            Log.Init();
            Mutex instance = RunningInstance.Acquire(forceTakeOver: HasArg("--updated"));
            if (instance == null)
                return;

            try
            {
                Log.Info($"Vibra {Application.ProductVersion} starting");
                RunningInstance.Register();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Error("Unhandled UI exception", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log.Error("Fatal exception", e.ExceptionObject as Exception);
                    VibraApp.Current?.EmergencyRestore();
                };

                // Needed so background work (library scan, updates) can report back to the UI thread.
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                using (var app = new VibraApp(startHidden: HasArg("--minimized"), openSettings: HasArg("--settings"), justUpdated: HasArg("--updated")))
                    Application.Run(app);

                Log.Info("Vibra exited");
            }
            finally
            {
                RunningInstance.Unregister();
                instance.ReleaseMutex();
                instance.Dispose();
            }
        }
    }
}
