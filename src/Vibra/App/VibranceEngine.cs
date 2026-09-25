using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Vibra.Backends;
using Vibra.Core;
using Vibra.Interop;
using Vibra.Platform;

namespace Vibra.App
{
    /// <summary>
    /// Keeps every monitor at the right vibrance: the game's level while that game is what you see
    /// on the monitor, the monitor's desktop level otherwise.
    ///
    /// Switching is event driven (Windows tells us the instant focus, minimise state or window
    /// placement changes), so it happens together with the alt-tab rather than after a polling delay.
    /// A one-second watchdog catches the rare cases events miss and repairs driver resets.
    /// </summary>
    internal sealed class VibranceEngine : IDisposable
    {
        private const int WatchdogIntervalMs = 1000;
        private const int VerifyEveryTicks = 2;
        private const int SettleDelayMs = 150;
        private const int VerifyAfterDisplayChangeMs = 1500;
        private const int RetryFailedAfterMs = 2000;
        private const int MaxOverrideStrikes = 3;

        private readonly IColorBackend backend;
        private readonly SettingsStore store;
        private readonly WindowScanner scanner = new WindowScanner();
        private readonly Dictionary<string, int> applied = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> failedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> overrideStrikes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<IntPtr> ownerWindows = new HashSet<IntPtr>();
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly NativeMethods.WinEventDelegate winEventProc;
        private readonly Timer watchdog = new Timer { Interval = WatchdogIntervalMs };
        private readonly Timer settleTimer = new Timer { Interval = SettleDelayMs };
        private readonly Timer verifySoonTimer = new Timer { Interval = VerifyAfterDisplayChangeMs };

        private List<DisplayInfo> displays = new List<DisplayInfo>();
        private List<MonitorArea> monitorAreas = new List<MonitorArea>();
        private Dictionary<string, GameProfile> live = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<GameProfile, IntPtr> liveWindows = new Dictionary<GameProfile, IntPtr>();
        private GameCatalog catalog = GameCatalog.Empty;
        private string previewDisplay;
        private int previewPercent;
        private int ticks;
        private bool evaluating;
        private bool disposed;

        public VibranceEngine(IColorBackend backend, SettingsStore store)
        {
            this.backend = backend;
            this.store = store;
            winEventProc = OnWinEvent;
            watchdog.Tick += OnWatchdogTick;
            settleTimer.Tick += (s, e) =>
            {
                settleTimer.Stop();
                Evaluate();
            };
            verifySoonTimer.Tick += (s, e) =>
            {
                verifySoonTimer.Stop();
                Verify();
            };
        }

        /// <summary>Which game is live on which display, or paused/unavailable state, changed.</summary>
        public event Action StateChanged;
        /// <summary>A game was added by a hotkey.</summary>
        public event Action GamesChanged;
        /// <summary>A game's vibrance was changed by a hotkey.</summary>
        public event Action<GameProfile> GameAdjusted;
        /// <summary>Monitors were connected, disconnected or rearranged.</summary>
        public event Action DisplaysChanged;

        public bool IsBackendAvailable => backend.IsAvailable;
        public string BackendStatus => backend.Status;
        public bool IsPaused => store.Settings.Paused;
        public IReadOnlyList<DisplayInfo> Displays => displays;

        public bool Supports(DisplayInfo display) => backend.Supports(display.GdiName);

        /// <summary>Games currently on screen, keyed by display GDI name.</summary>
        public IReadOnlyDictionary<string, GameProfile> LiveGames => live;

        public bool IsLive(GameProfile game) => live.Values.Contains(game);

        /// <summary>The window of a game that is on screen right now, or zero.</summary>
        public IntPtr WindowOf(GameProfile game) =>
            game != null && liveWindows.TryGetValue(game, out IntPtr hwnd) ? hwnd : IntPtr.Zero;

        public GameCatalog Catalog => catalog;

        /// <summary>Installs the result of a library scan.</summary>
        public void SetCatalog(GameCatalog value)
        {
            catalog = value ?? GameCatalog.Empty;
            Evaluate();
        }

        public void Start()
        {
            RefreshDisplays();
            AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);
            AddHook(NativeMethods.EVENT_SYSTEM_MOVESIZEEND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND);
            AddHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND);
            AddHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY);
            AddHook(NativeMethods.EVENT_OBJECT_HIDE, NativeMethods.EVENT_OBJECT_HIDE);
            AddHook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED);
            watchdog.Start();
            Evaluate();
            Log.Info($"Engine started: backend={backend.Status}, displays={string.Join(", ", displays.Select(d => $"{d.GdiName} '{d.Name}' supported={Supports(d)}"))}");
        }

        public void SetPaused(bool paused)
        {
            if (store.Settings.Paused == paused)
                return;
            store.Settings.Paused = paused;
            store.SaveSoon();
            if (paused)
                RestoreDesktop();
            else
                applied.Clear();
            Evaluate();
            StateChanged?.Invoke();
        }

        /// <summary>Temporarily show <paramref name="percent"/> on a display, e.g. while dragging a slider.</summary>
        public void SetPreview(string gdiName, int percent)
        {
            previewDisplay = gdiName;
            previewPercent = percent;
            Evaluate();
        }

        public void ClearPreview()
        {
            if (previewDisplay == null)
                return;
            previewDisplay = null;
            Evaluate();
        }

        /// <summary>Recompute what each monitor should show and apply any differences.</summary>
        public void Evaluate()
        {
            if (disposed || evaluating)
                return;
            evaluating = true;
            try
            {
                EvaluateCore();
            }
            catch (Exception ex)
            {
                Log.Error("Evaluate failed", ex);
            }
            finally
            {
                evaluating = false;
            }
        }

        private void EvaluateCore()
        {
            var nowLive = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
            if (!backend.IsAvailable || displays.Count == 0)
            {
                SetLive(nowLive);
                return;
            }

            bool paused = store.Settings.Paused;
            var gameExes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool IsGameWindow(WindowInfo w)
            {
                if (paused || string.IsNullOrEmpty(w.ExeName))
                    return false;
                if (!gameExes.TryGetValue(w.ExeName, out bool isGame))
                    gameExes[w.ExeName] = isGame = store.Settings.FindGame(w.ExeName) != null || CanAutoAdd(w.ExeName);
                return isGame;
            }

            var owners = ScreenDecider.FindOwners(monitorAreas, scanner.Scan(), IsGameWindow);
            ownerWindows.Clear();
            foreach (var owner in owners.Values)
                ownerWindows.Add(owner.Handle);
            var nowLiveWindows = new Dictionary<GameProfile, IntPtr>();

            int gameCount = store.Settings.Games.Count;
            foreach (var display in displays)
            {
                if (!backend.Supports(display.GdiName))
                    continue;

                GameProfile game = null;
                if (!paused && owners.TryGetValue(display.GdiName, out WindowInfo owner))
                {
                    game = store.Settings.FindGame(owner.ExeName) ?? TryAutoAdd(owner.ExeName);
                    if (game != null)
                        nowLiveWindows[game] = owner.Handle;
                }

                int target = game?.Vibrance ?? DesktopPercent(display);
                if (previewDisplay != null && string.Equals(previewDisplay, display.GdiName, StringComparison.OrdinalIgnoreCase))
                    target = previewPercent;

                if (game != null)
                    nowLive[display.GdiName] = game;

                Apply(display.GdiName, target, force: false);
            }

            liveWindows = nowLiveWindows;
            SetLive(nowLive);
            if (store.Settings.Games.Count != gameCount)
                GamesChanged?.Invoke();
        }

        /// <summary>A recognised game just took over a monitor for the first time: add it automatically.</summary>
        private bool CanAutoAdd(string exe)
        {
            var settings = store.Settings;
            return !settings.DisableAutoAdd && !string.IsNullOrEmpty(exe) && !settings.IsIgnored(exe) && catalog.Identify(exe) != null;
        }

        private GameProfile TryAutoAdd(string exe)
        {
            if (!CanAutoAdd(exe))
                return null;

            var settings = store.Settings;
            DetectedGame detected = catalog.Identify(exe);

            var profile = detected.ToProfile(settings.NewGameVibrance);
            profile.Exe = exe; // the executable actually running is the one to match first
            profile.OtherExes = detected.Exes.Where(e => !string.Equals(e, exe, StringComparison.OrdinalIgnoreCase)).ToList();
            if (profile.OtherExes.Count == 0)
                profile.OtherExes = null;
            settings.Games.Add(profile);
            store.SaveSoon();
            Log.Info($"Auto-added {detected.Name} ({exe}, {detected.Source}) at {profile.Vibrance}%");
            return profile;
        }

        private void Apply(string gdiName, int percent, bool force)
        {
            if (!force && applied.TryGetValue(gdiName, out int current) && current == percent)
                return;
            if (!force && failedAt.TryGetValue(gdiName, out int when) && unchecked(Environment.TickCount - when) < RetryFailedAfterMs)
                return;

            if (backend.TrySetVibrance(gdiName, percent))
            {
                if (!force)
                    overrideStrikes.Remove(gdiName);
                applied[gdiName] = percent;
                failedAt.Remove(gdiName);
            }
            else
            {
                applied.Remove(gdiName);
                if (!failedAt.ContainsKey(gdiName))
                    Log.Warn($"Could not set vibrance {percent}% on {gdiName}");
                failedAt[gdiName] = Environment.TickCount;
            }
        }

        /// <summary>Re-apply if the driver changed vibrance behind our back (mode switch, sleep, HDR toggle).</summary>
        private void Verify()
        {
            if (!backend.IsAvailable || store.Settings.Paused)
                return;
            foreach (var entry in applied.ToList())
            {
                int? actual = backend.TryGetVibrance(entry.Key);
                if (!actual.HasValue || actual.Value == entry.Value)
                {
                    overrideStrikes.Remove(entry.Key);
                    continue;
                }

                // Re-apply a few times; if the driver keeps overriding (e.g. some HDR modes), stop
                // fighting it until the target or the display setup changes.
                overrideStrikes.TryGetValue(entry.Key, out int strikes);
                if (strikes >= MaxOverrideStrikes)
                    continue;
                overrideStrikes[entry.Key] = ++strikes;
                if (strikes == MaxOverrideStrikes)
                    Log.Warn($"Driver keeps changing vibrance on {entry.Key} ({actual}% instead of {entry.Value}%); giving up until something changes");
                else
                    Log.Info($"Vibrance on {entry.Key} was reset to {actual}% by the driver, restoring {entry.Value}%");
                Apply(entry.Key, entry.Value, force: true);
            }
        }

        /// <summary>Put every monitor back to its desktop level (pause, exit, shutdown).</summary>
        public void RestoreDesktop()
        {
            if (!backend.IsAvailable)
                return;
            foreach (var display in displays)
            {
                if (backend.Supports(display.GdiName))
                    Apply(display.GdiName, DesktopPercent(display), force: true);
            }
        }

        /// <summary>Hotkey handler: nudge the focused game's vibrance, adding it as a game if needed.</summary>
        public GameProfile AdjustForegroundApp(int delta)
        {
            RunningApp app = scanner.DescribeForeground();
            if (app == null)
                return null;

            var settings = store.Settings;
            GameProfile game = settings.FindGame(app.Exe);
            bool added = false;
            if (game == null)
            {
                string gdi = DisplayEnumerator.GdiNameForWindow(NativeMethods.GetForegroundWindow());
                int start = gdi != null && applied.TryGetValue(gdi, out int current) ? current : VibranceScale.MinPercent;
                string name = catalog.Identify(app.Exe)?.Name ?? CleanTitle(app.Title, app.Exe);
                game = new GameProfile { Exe = app.Exe, Name = name, Vibrance = start };
                settings.AddGame(game);
                added = true;
                Log.Info($"Added {app.Exe} via hotkey");
            }

            game.Vibrance = VibranceScale.Clamp(game.Vibrance + delta);
            store.SaveSoon();
            Evaluate();
            if (added)
                GamesChanged?.Invoke();
            GameAdjusted?.Invoke(game);
            return game;
        }

        public List<RunningApp> ListRunningApps() => scanner.ListRunningApps();

        public string DisplayForWindow(IntPtr hwnd) => DisplayEnumerator.GdiNameForWindow(hwnd);

        public DisplayInfo FindDisplay(string gdiName) =>
            displays.FirstOrDefault(d => string.Equals(d.GdiName, gdiName, StringComparison.OrdinalIgnoreCase));

        public int DesktopPercent(DisplayInfo display) =>
            store.Settings.FindDisplay(display.Id)?.Vibrance ?? VibranceScale.MinPercent;

        public void OnDisplayConfigurationChanged()
        {
            backend.Refresh();
            RefreshDisplays();
            applied.Clear();
            failedAt.Clear();
            overrideStrikes.Clear();
            Evaluate();
            // The driver may re-apply its own value once the mode switch settles; check again shortly.
            verifySoonTimer.Stop();
            verifySoonTimer.Start();
        }

        public void OnSystemResumed() => OnDisplayConfigurationChanged();

        private void RefreshDisplays()
        {
            var fresh = DisplayEnumerator.GetDisplays();
            bool changed = fresh.Count != displays.Count || fresh.Where((d, i) => !d.SameAs(displays[i])).Any();
            displays = fresh;
            monitorAreas = displays.Select(d => new MonitorArea(d.GdiName, d.Bounds)).ToList();

            foreach (var display in displays)
            {
                if (!backend.Supports(display.GdiName))
                    continue;
                var profile = store.Settings.FindDisplay(display.Id);
                if (profile == null)
                {
                    // First time we see this monitor: keep whatever it is set to today (e.g. 70% from
                    // NVIDIA Control Panel) as its desktop level.
                    int current = backend.TryGetVibrance(display.GdiName) ?? VibranceScale.MinPercent;
                    store.Settings.Displays.Add(new DisplayProfile { Id = display.Id, Name = display.Name, Vibrance = current });
                    store.SaveSoon();
                    changed = true;
                    Log.Info($"New display {display.Name} ({display.GdiName}), desktop vibrance {current}%");
                }
                else if (profile.Name != display.Name)
                {
                    profile.Name = display.Name;
                    store.SaveSoon();
                }
            }

            if (changed)
                DisplaysChanged?.Invoke();
        }

        private void SetLive(Dictionary<string, GameProfile> nowLive)
        {
            bool same = nowLive.Count == live.Count &&
                        nowLive.All(kv => live.TryGetValue(kv.Key, out var g) && ReferenceEquals(g, kv.Value));
            if (same)
                return;
            live = nowLive;
            Log.Info(live.Count == 0
                ? "On screen: no games"
                : "On screen: " + string.Join(", ", live.Select(kv => $"{kv.Value.Name} ({kv.Value.Vibrance}%) on {kv.Key} '{FindDisplay(kv.Key)?.Name}'")));
            StateChanged?.Invoke();
        }

        private void AddHook(uint eventMin, uint eventMax)
        {
            IntPtr hook = NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook == IntPtr.Zero)
                Log.Warn($"SetWinEventHook failed for 0x{eventMin:X}-0x{eventMax:X}");
            else
                hooks.Add(hook);
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
                    return;

                switch (eventType)
                {
                    case NativeMethods.EVENT_OBJECT_DESTROY:
                    case NativeMethods.EVENT_OBJECT_HIDE:
                        // Thousands of these fire for menus, tooltips and child controls; only a
                        // window that currently owns a monitor matters.
                        if (!ownerWindows.Contains(hwnd))
                            return;
                        break;
                    case NativeMethods.EVENT_OBJECT_CLOAKED:
                    case NativeMethods.EVENT_OBJECT_UNCLOAKED:
                        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                            return;
                        break;
                }

                // Apply immediately, then once more after things settle (window animations,
                // exclusive-fullscreen games minimising a moment after losing focus).
                Evaluate();
                settleTimer.Stop();
                settleTimer.Start();
            }
            catch (Exception ex)
            {
                // Never let an exception unwind into the native event dispatch.
                Log.Error("WinEvent handler failed", ex);
            }
        }

        private void OnWatchdogTick(object sender, EventArgs e)
        {
            ticks++;
            Evaluate();
            if (ticks % VerifyEveryTicks == 0)
            {
                try
                {
                    Verify();
                }
                catch (Exception ex)
                {
                    Log.Error("Verify failed", ex);
                }
            }
        }

        private static string CleanTitle(string title, string exe)
        {
            title = (title ?? string.Empty).Trim();
            if (title.Length == 0 || title.Length > 60)
                return GameMatcher.DisplayNameFor(exe);
            return title;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            foreach (var hook in hooks)
                NativeMethods.UnhookWinEvent(hook);
            hooks.Clear();
            watchdog.Dispose();
            settleTimer.Dispose();
            verifySoonTimer.Dispose();
            try
            {
                RestoreDesktop();
            }
            catch (Exception ex)
            {
                Log.Error("Restore on exit failed", ex);
            }
            disposed = true;
        }
    }
}
