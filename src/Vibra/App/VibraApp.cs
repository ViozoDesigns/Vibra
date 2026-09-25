using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Vibra.Backends;
using Vibra.Core;
using Vibra.Interop;
using Vibra.Platform;
using Vibra.UI;

namespace Vibra.App
{
    /// <summary>Owns the tray icon, the engine and the settings window for the lifetime of the process.</summary>
    internal sealed class VibraApp : ApplicationContext
    {
        public const string ShowEventName = @"Local\Vibra.ShowWindow";

        private const int HotkeyUp = 1;
        private const int HotkeyDown = 2;
        private const int LibraryScanDelayMs = 4000;

        private readonly SettingsStore store;
        private readonly NvidiaBackend backend;
        private readonly VibranceEngine engine;
        private readonly MessageWindow messages;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private readonly EventWaitHandle showEvent;
        private readonly RegisteredWaitHandle showWait;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer scanDelay = new System.Windows.Forms.Timer { Interval = LibraryScanDelayMs };
        private MainForm mainForm;
        private bool hotkeysRegistered;
        private bool scanRunning;
        private bool shuttingDown;

        public VibraApp(bool startHidden)
        {
            Current = this;
            ui = SynchronizationContext.Current;

            store = SettingsStore.Load();
            backend = new NvidiaBackend();
            engine = new VibranceEngine(backend, store);

            messages = new MessageWindow();
            messages.DisplayChanged += engine.OnDisplayConfigurationChanged;
            messages.SystemResumed += engine.OnSystemResumed;
            messages.SessionEnding += OnSessionEnding;
            messages.HotkeyPressed += OnHotkey;
            messages.ShowRequested += ShowMainWindow;

            pauseItem = new ToolStripMenuItem("Pause", null, (s, e) => engine.SetPaused(!engine.IsPaused));
            tray = new NotifyIcon
            {
                Icon = AppIcon.Small,
                Text = "Vibra",
                Visible = true,
                ContextMenuStrip = CreateTrayMenu(),
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ShowMainWindow();
            };

            engine.StateChanged += UpdateTray;
            engine.Start();
            RegisterHotkeys();
            UpdateTray();

            // A second launch (e.g. double-clicking the exe again) asks this instance to show itself.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
                NativeMethods.PostMessage(messages.Handle, MessageWindow.ShowRequestMessage, IntPtr.Zero, IntPtr.Zero), null, -1, false);

            Autostart.RepairPath();

            scanDelay.Tick += (s, e) =>
            {
                scanDelay.Stop();
                StartLibraryScan();
            };
            scanDelay.Start();

            if (!startHidden)
                ShowMainWindow();
        }

        public static VibraApp Current { get; private set; }

        // ------------------------------------------------------------------ Tray

        private ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Body };
            var open = new ToolStripMenuItem("Open Vibra", null, (s, e) => ShowMainWindow()) { Font = Theme.BodyStrong };
            menu.Items.Add(open);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => Shutdown()));
            return menu;
        }

        private void UpdateTray()
        {
            pauseItem.Checked = engine.IsPaused;
            string text = "Vibra · " + UI.MainForm.StatusText(engine, store.Settings, out _);
            tray.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text;
        }

        // ------------------------------------------------------------------ Window

        private void ShowMainWindow()
        {
            if (shuttingDown)
                return;
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm(engine, store, () => hotkeysRegistered, StartLibraryScan);
                mainForm.HiddenToTray += OnHiddenToTray;
            }
            if (!mainForm.Visible)
                mainForm.Show();
            if (mainForm.WindowState == FormWindowState.Minimized)
                mainForm.WindowState = FormWindowState.Normal;
            mainForm.Activate();
            mainForm.BringToFront();
        }

        private void OnHiddenToTray()
        {
            if (store.Settings.TrayHintShown)
                return;
            store.Settings.TrayHintShown = true;
            store.SaveSoon();
            tray.ShowBalloonTip(4000, "Vibra keeps running", "It switches colors automatically from the tray. Right-click the icon to pause or exit.", ToolTipIcon.None);
        }

        // ------------------------------------------------------------------ Library scan

        private void StartLibraryScan()
        {
            if (scanRunning)
                return;
            scanRunning = true;
            var thread = new Thread(() =>
            {
                GameCatalog catalog;
                try
                {
                    catalog = new GameCatalog(LibraryScanner.Scan());
                }
                catch (Exception ex)
                {
                    Log.Error("Library scan failed", ex);
                    catalog = null;
                }
                ui.Post(_ =>
                {
                    scanRunning = false;
                    if (catalog != null && !shuttingDown)
                        engine.SetCatalog(catalog);
                }, null);
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Vibra library scan",
            };
            thread.Start();
        }

        // ------------------------------------------------------------------ Hotkeys

        private void RegisterHotkeys()
        {
            if (store.Settings.DisableHotkeys)
                return;
            bool up = NativeMethods.RegisterHotKey(messages.Handle, HotkeyUp, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_PRIOR);
            bool down = NativeMethods.RegisterHotKey(messages.Handle, HotkeyDown, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_NEXT);
            hotkeysRegistered = up && down;
            if (!hotkeysRegistered)
                Log.Warn("Could not register Ctrl+Alt+PgUp/PgDn; another app is using them");
        }

        private void OnHotkey(int id)
        {
            int step = store.Settings.HotkeyStep;
            if (id == HotkeyUp)
                engine.AdjustForegroundApp(step);
            else if (id == HotkeyDown)
                engine.AdjustForegroundApp(-step);
        }

        // ------------------------------------------------------------------ Shutdown

        private void OnSessionEnding()
        {
            // Windows is logging off or shutting down: leave the desktop colors as the user set them.
            engine.RestoreDesktop();
            store.Save();
        }

        /// <summary>Last-ditch restore when the process is crashing.</summary>
        public void EmergencyRestore()
        {
            try
            {
                engine.RestoreDesktop();
            }
            catch
            {
                // Nothing else we can do; the next start restores desktop levels anyway.
            }
        }

        private void Shutdown()
        {
            if (shuttingDown)
                return;
            shuttingDown = true;
            tray.Visible = false;
            if (mainForm != null && !mainForm.IsDisposed)
            {
                mainForm.HiddenToTray -= OnHiddenToTray;
                mainForm.Dispose();
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuttingDown = true;
                if (hotkeysRegistered)
                {
                    NativeMethods.UnregisterHotKey(messages.Handle, HotkeyUp);
                    NativeMethods.UnregisterHotKey(messages.Handle, HotkeyDown);
                }
                showWait.Unregister(null);
                showEvent.Dispose();
                scanDelay.Dispose();
                engine.Dispose(); // restores desktop vibrance
                store.Dispose();
                tray.Dispose();
                messages.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
