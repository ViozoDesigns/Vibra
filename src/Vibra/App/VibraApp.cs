using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Vibra.Backends;
using Vibra.Core;
using Vibra.Interop;
using Vibra.Platform;
using Vibra.UI;

namespace Vibra.App
{
    /// <summary>Owns the tray icon, the engine and the windows for the lifetime of the process.</summary>
    internal sealed class VibraApp : ApplicationContext
    {
        private const int HotkeyIncreaseId = 1;
        private const int HotkeyDecreaseId = 2;
        private const int HotkeyPauseId = 3;
        private const int HotkeyTestId = 99;
        private const int LibraryScanDelayMs = 4000;

        private readonly SettingsStore store;
        private readonly NvidiaBackend backend;
        private readonly VibranceEngine engine;
        private readonly IconCache icons;
        private readonly MessageWindow messages;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private readonly Updater updater;
        private readonly UndoHistory history = new UndoHistory();
        private readonly EventWaitHandle showEvent;
        private readonly RegisteredWaitHandle showWait;
        private readonly EventWaitHandle quitEvent;
        private readonly RegisteredWaitHandle quitWait;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer scanDelay = new System.Windows.Forms.Timer { Interval = LibraryScanDelayMs };
        private readonly List<int> registeredHotkeys = new List<int>();
        private readonly List<string> failedHotkeys = new List<string>();
        private MainForm mainForm;
        private SettingsForm settingsForm;
        private bool scanRunning;
        private bool shuttingDown;

        public VibraApp(bool startHidden, bool openSettings = false, bool justUpdated = false)
        {
            Current = this;
            ui = SynchronizationContext.Current;

            store = SettingsStore.Load();
            backend = new NvidiaBackend();
            engine = new VibranceEngine(backend, store);
            icons = new IconCache(exe => engine.Catalog.IconCandidatesFor(exe));
            icons.IconPathFound += (game, path) =>
            {
                if (string.IsNullOrEmpty(game.IconPath))
                {
                    game.IconPath = path;
                    store.SaveSoon();
                }
            };

            messages = new MessageWindow();
            messages.DisplayChanged += engine.OnDisplayConfigurationChanged;
            messages.SystemResumed += engine.OnSystemResumed;
            messages.SessionEnding += OnSessionEnding;
            messages.HotkeyPressed += OnHotkey;
            messages.ShowRequested += ShowMainWindow;
            messages.QuitRequested += Shutdown;

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

            engine.StateChanged += OnEngineStateChanged;
            history.Applied += OnHistoryApplied;
            engine.Start();
            RegisterHotkeys();
            UpdateTray();

            // A second launch of the same exe asks this instance to show itself; a different
            // version (new download or update) asks it to quit so it can take over.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RunningInstance.ShowEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
                NativeMethods.PostMessage(messages.Handle, MessageWindow.ShowRequestMessage, IntPtr.Zero, IntPtr.Zero), null, -1, false);
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RunningInstance.QuitEventName);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (state, timedOut) =>
                NativeMethods.PostMessage(messages.Handle, MessageWindow.QuitRequestMessage, IntPtr.Zero, IntPtr.Zero), null, -1, false);

            // Updates install only while no game is on screen, so a restart never blinks the colors mid-game.
            updater = new Updater(store, () => engine.LiveGames.Count == 0 && settingsForm == null, RestartForUpdate);
            updater.Start();

            Autostart.RepairPath();

            scanDelay.Tick += (s, e) =>
            {
                scanDelay.Stop();
                StartLibraryScan();
            };
            scanDelay.Start();

            if (!startHidden)
                ShowMainWindow();
            if (openSettings)
                OpenSettings();
            if (justUpdated)
                tray.ShowBalloonTip(4000, $"Vibra updated to {Updater.CurrentVersionText}", "Your games and settings are unchanged.", ToolTipIcon.None);
        }

        public static VibraApp Current { get; private set; }

        private void OnEngineStateChanged()
        {
            UpdateTray();
            updater?.TryInstall();
            // Games found some other way than a library scan get their icon from their window.
            foreach (var game in engine.LiveGames.Values.Distinct())
                icons.CaptureFromWindow(game, engine.WindowOf(game));
        }

        /// <summary>An undo/redo changed the settings: save and apply them.</summary>
        private void OnHistoryApplied(string description, bool undone)
        {
            store.SaveSoon();
            engine.Evaluate();
            if (settingsForm == null)
                RegisterHotkeys(); // shortcuts may have changed back
            UpdateTray();
            mainForm?.RefreshStatus();
        }

        // ------------------------------------------------------------------ Tray

        private ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Body };
            menu.Items.Add(new ToolStripMenuItem("Open Vibra", null, (s, e) => ShowMainWindow()) { Font = Theme.BodyStrong });
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, (s, e) => OpenSettings()));
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

        // ------------------------------------------------------------------ Windows

        private void ShowMainWindow()
        {
            if (shuttingDown)
                return;
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm(engine, store, icons, history, HotkeyHint, OpenSettings, StartLibraryScan);
                mainForm.HiddenToTray += OnHiddenToTray;
            }
            if (!mainForm.Visible)
                mainForm.Show();
            if (mainForm.WindowState == FormWindowState.Minimized)
                mainForm.WindowState = FormWindowState.Normal;
            mainForm.Activate();
            mainForm.BringToFront();
        }

        private void OpenSettings()
        {
            if (shuttingDown)
                return;
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Activate();
                return;
            }

            // Global shortcuts would swallow the keys the user presses to set new ones.
            UnregisterHotkeys();
            settingsForm = new SettingsForm(engine, store, IsHotkeyAvailable, updater, history);
            settingsForm.FormClosed += (s, e) =>
            {
                settingsForm = null;
                store.Save();
                RegisterHotkeys();
                UpdateTray();
                if (mainForm != null && !mainForm.IsDisposed)
                    mainForm.RefreshStatus();
            };
            var owner = mainForm != null && !mainForm.IsDisposed && mainForm.Visible ? mainForm : null;
            if (owner != null)
            {
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.Show(owner);
            }
            else
            {
                settingsForm.StartPosition = FormStartPosition.CenterScreen;
                settingsForm.Show();
            }
        }

        private void OnHiddenToTray()
        {
            if (store.Settings.TrayHintShown)
                return;
            store.Settings.TrayHintShown = true;
            store.SaveSoon();
            tray.ShowBalloonTip(4000, "Vibra keeps running", "It switches colors automatically from the tray. Right-click the icon for settings, pause or exit.", ToolTipIcon.None);
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
                    if (catalog == null || shuttingDown)
                        return;
                    engine.SetCatalog(catalog);
                    icons.RetryMissing(); // install folders are known now, so exe icons can be read
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

        private static Hotkey Parse(string text) => Hotkey.TryParse(text, out Hotkey hotkey) ? hotkey : Hotkey.None;

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            failedHotkeys.Clear();
            var settings = store.Settings;
            Register(HotkeyIncreaseId, Parse(settings.HotkeyIncrease));
            Register(HotkeyDecreaseId, Parse(settings.HotkeyDecrease));
            Register(HotkeyPauseId, Parse(settings.HotkeyPause));
        }

        private void Register(int id, Hotkey hotkey)
        {
            if (hotkey.IsNone)
                return;
            if (NativeMethods.RegisterHotKey(messages.Handle, id, (uint)hotkey.Modifiers, (uint)hotkey.Key))
            {
                registeredHotkeys.Add(id);
            }
            else
            {
                failedHotkeys.Add(hotkey.ToString());
                Log.Warn($"Could not register shortcut {hotkey}; another app is using it");
            }
        }

        private void UnregisterHotkeys()
        {
            foreach (int id in registeredHotkeys)
                NativeMethods.UnregisterHotKey(messages.Handle, id);
            registeredHotkeys.Clear();
        }

        /// <summary>True if no other app has claimed this shortcut.</summary>
        private bool IsHotkeyAvailable(Hotkey hotkey)
        {
            if (!NativeMethods.RegisterHotKey(messages.Handle, HotkeyTestId, (uint)hotkey.Modifiers, (uint)hotkey.Key))
                return false;
            NativeMethods.UnregisterHotKey(messages.Handle, HotkeyTestId);
            return true;
        }

        private string HotkeyHint()
        {
            var settings = store.Settings;
            var increase = Parse(settings.HotkeyIncrease);
            var decrease = Parse(settings.HotkeyDecrease);
            var pause = Parse(settings.HotkeyPause);
            if (failedHotkeys.Count > 0)
                return $"{string.Join(", ", failedHotkeys)} is used by another app · change it in Settings";

            var parts = new List<string>();
            if (!increase.IsNone || !decrease.IsNone)
                parts.Add($"In game: {string.Join(" / ", new[] { increase, decrease }.Where(h => !h.IsNone))} to tune");
            if (!pause.IsNone)
                parts.Add($"{pause} to pause");
            return parts.Count == 0 ? "Shortcuts are off · set them in Settings" : string.Join(" · ", parts);
        }

        private void OnHotkey(int id)
        {
            int step = store.Settings.HotkeyStep;
            switch (id)
            {
                case HotkeyIncreaseId:
                    engine.AdjustForegroundApp(step);
                    break;
                case HotkeyDecreaseId:
                    engine.AdjustForegroundApp(-step);
                    break;
                case HotkeyPauseId:
                    engine.SetPaused(!engine.IsPaused);
                    break;
            }
        }

        // ------------------------------------------------------------------ Shutdown

        /// <summary>The new exe is in place: start it and leave (it waits for this process to exit).</summary>
        private void RestartForUpdate()
        {
            bool windowOpen = mainForm != null && !mainForm.IsDisposed && mainForm.Visible;
            try
            {
                Process.Start(Application.ExecutablePath, windowOpen ? "--updated" : "--updated --minimized");
            }
            catch (Exception ex)
            {
                Log.Error("Could not start the updated Vibra", ex);
                return;
            }
            Shutdown();
        }

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
            settingsForm?.Dispose();
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
                UnregisterHotkeys();
                showWait.Unregister(null);
                showEvent.Dispose();
                quitWait.Unregister(null);
                quitEvent.Dispose();
                updater.Dispose();
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
