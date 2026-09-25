using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Vibra.App;
using Vibra.Core;
using Vibra.Platform;

namespace Vibra.UI
{
    internal sealed class MainForm : Form
    {
        private readonly VibranceEngine engine;
        private readonly SettingsStore store;
        private readonly Func<bool> hotkeysActive;
        private readonly Action requestLibraryScan;

        private readonly Label titleLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly FlatButton pauseButton = new FlatButton();
        private readonly Label gamesHeader = new Label();
        private readonly FlatButton addButton = new FlatButton();
        private readonly StackPanel gamesList = new StackPanel { AutoScroll = true };
        private readonly Label emptyLabel = new Label();
        private readonly Label defaultsHeader = new Label();
        private readonly StackPanel defaultsList = new StackPanel();
        private readonly CheckBox autostartCheck = new CheckBox();
        private readonly CheckBox autoAddCheck = new CheckBox();
        private readonly Label hotkeyLabel = new Label();

        private readonly Dictionary<GameProfile, SliderRow> gameRows = new Dictionary<GameProfile, SliderRow>();
        private SliderRow newGameRow;

        public MainForm(VibranceEngine engine, SettingsStore store, Func<bool> hotkeysActive, Action requestLibraryScan)
        {
            this.engine = engine;
            this.store = store;
            this.hotkeysActive = hotkeysActive;
            this.requestLibraryScan = requestLibraryScan;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Vibra";
            Icon = AppIcon.Large;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            ClientSize = new Size(S(540), S(660));
            MinimumSize = SizeFromClientSize(new Size(S(460), S(520)));

            titleLabel.Text = "Vibra";
            titleLabel.Font = Theme.Title;
            titleLabel.AutoSize = true;

            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.ForeColor = Theme.SubText;

            pauseButton.Click += (s, e) => engine.SetPaused(!engine.IsPaused);

            SetupSectionLabel(gamesHeader, "GAMES");
            addButton.Text = "+  Add game";
            addButton.Click += (s, e) => ShowAddMenu();

            emptyLabel.Text = "No games yet.\nStart a game and it will be added automatically, pick one from \"+ Add game\",\nor press Ctrl+Alt+PgUp while playing.";
            emptyLabel.ForeColor = Theme.SubText;
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.Height = S(90);

            SetupSectionLabel(defaultsHeader, "DEFAULTS");

            SetupCheck(autostartCheck, "Start with Windows");
            autostartCheck.Checked = Autostart.IsEnabled;
            autostartCheck.CheckedChanged += OnAutostartChanged;

            SetupCheck(autoAddCheck, "Auto-add games when they start");
            autoAddCheck.Checked = !store.Settings.DisableAutoAdd;
            autoAddCheck.CheckedChanged += (s, e) =>
            {
                store.Settings.DisableAutoAdd = !autoAddCheck.Checked;
                store.SaveSoon();
            };

            hotkeyLabel.AutoSize = true;
            hotkeyLabel.ForeColor = Theme.SubText;
            hotkeyLabel.Font = Theme.Small;

            Controls.AddRange(new Control[]
            {
                titleLabel, statusLabel, pauseButton, gamesHeader, addButton, gamesList,
                defaultsHeader, defaultsList, autoAddCheck, autostartCheck, hotkeyLabel,
            });
            ResumeLayout(false);

            engine.StateChanged += OnEngineStateChanged;
            engine.GamesChanged += OnGamesChanged;
            engine.GameAdjusted += OnGameAdjusted;
            engine.DisplaysChanged += OnDisplaysChanged;

            RebuildGames();
            RebuildDefaults();
            RefreshStatus();
        }

        private int S(int px) => Theme.Scale(this, px);

        /// <summary>
        /// Runs after the current event finishes. Rows are rebuilt this way so a control is never
        /// disposed while one of its own event handlers is still on the stack.
        /// </summary>
        private void Defer(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
            else if (!IsDisposed)
                action();
        }

        private void OnGamesChanged() => Defer(RebuildGames);

        private void OnDisplaysChanged() => Defer(RebuildDefaults);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.UseDarkTitleBar(Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Closing the window keeps Vibra running in the tray.
                e.Cancel = true;
                engine.ClearPreview();
                Hide();
                HiddenToTray?.Invoke();
                return;
            }
            base.OnFormClosing(e);
        }

        public event Action HiddenToTray;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                engine.StateChanged -= OnEngineStateChanged;
                engine.GamesChanged -= OnGamesChanged;
                engine.GameAdjusted -= OnGameAdjusted;
                engine.DisplaysChanged -= OnDisplaysChanged;
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ Layout

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (ClientSize.Width == 0 || ClientSize.Height == 0)
                return;

            int pad = S(22);
            int width = ClientSize.Width - pad * 2;
            int y = S(16);

            var pauseSize = new Size(S(96), S(32));
            titleLabel.Location = new Point(pad - S(2), y);
            pauseButton.Bounds = new Rectangle(pad + width - pauseSize.Width, y + S(6), pauseSize.Width, pauseSize.Height);
            y += titleLabel.Height + S(2);
            statusLabel.Bounds = new Rectangle(pad, y, width - pauseSize.Width - S(12), S(20));
            y += statusLabel.Height + S(20);

            var addSize = new Size(S(124), S(32));
            addButton.Bounds = new Rectangle(pad + width - addSize.Width, y, addSize.Width, addSize.Height);
            gamesHeader.Location = new Point(pad, y + (addSize.Height - gamesHeader.Height) / 2);
            y += addSize.Height + S(10);

            // Bottom-up: footer, then defaults, then the games list takes what is left.
            int bottom = ClientSize.Height - S(16);
            hotkeyLabel.Location = new Point(pad, bottom - hotkeyLabel.Height);
            bottom = hotkeyLabel.Top - S(10);
            autostartCheck.Bounds = new Rectangle(pad, bottom - S(24), width / 2, S(24));
            autoAddCheck.Bounds = new Rectangle(pad + width / 2, bottom - S(24), width / 2, S(24));
            bottom = autostartCheck.Top - S(14);

            int defaultsHeight = defaultsList.ContentHeight;
            defaultsList.Bounds = new Rectangle(pad, bottom - defaultsHeight, width, defaultsHeight);
            bottom = defaultsList.Top - S(10);
            defaultsHeader.Location = new Point(pad, bottom - defaultsHeader.Height);
            bottom = defaultsHeader.Top - S(18);

            gamesList.Bounds = new Rectangle(pad, y, width, Math.Max(S(60), bottom - y));
        }

        // ------------------------------------------------------------------ Games

        private void RebuildGames()
        {
            gamesList.SuspendLayout();
            foreach (var row in gameRows.Values)
                row.Dispose();
            gameRows.Clear();
            gamesList.Controls.Clear();

            var games = store.Settings.Games;
            if (games.Count == 0)
            {
                gamesList.Controls.Add(emptyLabel);
            }
            else
            {
                foreach (var game in games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = CreateGameRow(game);
                    gameRows[game] = row;
                    gamesList.Controls.Add(row);
                }
            }
            gamesList.ResumeLayout(true);
            UpdateLiveIndicators();
            RefreshStatus();
        }

        private SliderRow CreateGameRow(GameProfile game)
        {
            var row = new SliderRow(removable: true) { Title = game.Name };
            row.Slider.Value = game.Vibrance;
            row.Slider.ValueChanged += (s, e) =>
            {
                game.Vibrance = row.Slider.Value;
                store.SaveSoon();
                if (row.Slider.IsDragging && !engine.IsLive(game))
                    PreviewHere(game.Vibrance);
                else
                    engine.Evaluate();
            };
            row.Slider.DragStarted += (s, e) =>
            {
                if (!engine.IsLive(game))
                    PreviewHere(row.Slider.Value);
            };
            row.Slider.DragCompleted += (s, e) => engine.ClearPreview();
            row.RemoveClicked += (s, e) => Defer(() =>
            {
                store.Settings.RemoveGame(game);
                store.SaveSoon();
                RebuildGames();
                engine.Evaluate();
            });
            return row;
        }

        /// <summary>Show a value on the monitor this window is on, so you can judge it without the game running.</summary>
        private void PreviewHere(int percent)
        {
            string gdi = engine.DisplayForWindow(Handle);
            if (gdi != null)
                engine.SetPreview(gdi, percent);
        }

        private void OnGameAdjusted(GameProfile game)
        {
            if (gameRows.TryGetValue(game, out var row))
                row.Slider.Value = game.Vibrance;
        }

        private void UpdateLiveIndicators()
        {
            var live = engine.LiveGames;
            foreach (var pair in gameRows)
            {
                var entry = live.FirstOrDefault(kv => ReferenceEquals(kv.Value, pair.Key));
                bool isLive = entry.Value != null;
                pair.Value.Live = isLive;
                pair.Value.Subtitle = isLive
                    ? $"On screen · {engine.FindDisplay(entry.Key)?.Name ?? entry.Key}"
                    : pair.Key.Exe;
            }
        }

        // ------------------------------------------------------------------ Add menu

        private void ShowAddMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false,
                Font = Theme.Body,
            };

            var settings = store.Settings;
            var catalog = engine.Catalog;
            var running = engine.ListRunningApps().Where(a => settings.FindGame(a.Exe) == null).ToList();
            var runningGames = running.Where(a => catalog.Identify(a.Exe) != null).ToList();
            var runningOther = running.Except(runningGames).ToList();
            var installed = catalog.Installed.Where(g => g.Exes.All(e => settings.FindGame(e) == null)).ToList();

            if (runningGames.Count > 0)
            {
                AddHeader(menu, "Running now");
                foreach (var app in runningGames)
                {
                    var detected = catalog.Identify(app.Exe);
                    AddItem(menu.Items, detected.Name, app.Exe, () =>
                    {
                        var profile = detected.ToProfile(settings.NewGameVibrance);
                        profile.Exe = app.Exe;
                        profile.OtherExes = detected.Exes.Where(e => !string.Equals(e, app.Exe, StringComparison.OrdinalIgnoreCase)).ToList();
                        AddGame(profile);
                    });
                }
            }

            if (installed.Count > 0)
            {
                if (menu.Items.Count > 0)
                    menu.Items.Add(new ToolStripSeparator());
                AddHeader(menu, "Installed");
                const int inline = 12;
                foreach (var game in installed.Take(inline))
                    AddItem(menu.Items, game.Name, game.Source, () => AddGame(game.ToProfile(settings.NewGameVibrance)));
                if (installed.Count > inline)
                {
                    var more = new ToolStripMenuItem($"More installed games ({installed.Count - inline})");
                    foreach (var game in installed.Skip(inline))
                        AddItem(more.DropDownItems, game.Name, game.Source, () => AddGame(game.ToProfile(settings.NewGameVibrance)));
                    StyleDropDown(more);
                    menu.Items.Add(more);
                }
                AddItem(menu.Items, $"Add all {installed.Count} installed games", null, () =>
                {
                    foreach (var game in installed)
                        settings.AddGame(game.ToProfile(settings.NewGameVibrance));
                    store.SaveSoon();
                    RebuildGames();
                    engine.Evaluate();
                });
            }

            if (menu.Items.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            if (runningOther.Count > 0)
            {
                var other = new ToolStripMenuItem("Other running apps");
                foreach (var app in runningOther)
                {
                    string title = app.Title.Length > 48 ? app.Title.Substring(0, 47) + "…" : app.Title;
                    AddItem(other.DropDownItems, title, app.Exe, () => AddGame(new GameProfile
                    {
                        Exe = app.Exe,
                        Name = app.Title.Length > 40 ? GameMatcher.DisplayNameFor(app.Exe) : app.Title,
                        Vibrance = settings.NewGameVibrance,
                    }));
                }
                StyleDropDown(other);
                menu.Items.Add(other);
            }

            AddItem(menu.Items, "Browse for a game .exe…", null, BrowseForGame);
            AddItem(menu.Items, "Rescan installed games", null, () => requestLibraryScan());

            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(addButton, new Point(0, addButton.Height + S(4)));
        }

        private void AddHeader(ContextMenuStrip menu, string text)
        {
            menu.Items.Add(new ToolStripMenuItem(text.ToUpperInvariant()) { Enabled = false, Font = Theme.Section });
        }

        private void AddItem(ToolStripItemCollection items, string text, string detail, Action onClick)
        {
            var item = new ToolStripMenuItem(text.Replace("&", "&&"));
            if (!string.IsNullOrEmpty(detail))
                item.ShortcutKeyDisplayString = detail;
            // Let the menu close before acting (some actions open a dialog).
            item.Click += (s, e) => Defer(onClick);
            items.Add(item);
        }

        private static void StyleDropDown(ToolStripMenuItem item)
        {
            item.DropDown.Renderer = new DarkMenuRenderer();
            if (item.DropDown is ToolStripDropDownMenu dropDown)
                dropDown.ShowImageMargin = false;
        }

        private void AddGame(GameProfile profile)
        {
            if (profile?.Exe == null || store.Settings.FindGame(profile.Exe) != null)
                return;
            if (profile.OtherExes != null && profile.OtherExes.Count == 0)
                profile.OtherExes = null;
            store.Settings.AddGame(profile);
            store.SaveSoon();
            RebuildGames();
            engine.Evaluate();
        }

        private void BrowseForGame()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Choose the game's .exe",
                Filter = "Programs (*.exe)|*.exe",
                CheckFileExists = true,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string exe = Path.GetFileName(dialog.FileName);
                var detected = engine.Catalog.Identify(exe);
                string name = detected?.Name ?? ReadProductName(dialog.FileName) ?? GameMatcher.DisplayNameFor(exe);
                AddGame(new GameProfile
                {
                    Exe = exe,
                    OtherExes = detected?.Exes.Where(e => !string.Equals(e, exe, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Name = name,
                    Vibrance = store.Settings.NewGameVibrance,
                });
            }
        }

        private static string ReadProductName(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                string name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : info.FileDescription;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ Defaults (desktop per monitor + new games)

        private void RebuildDefaults()
        {
            defaultsList.SuspendLayout();
            foreach (Control control in defaultsList.Controls.Cast<Control>().ToList())
                control.Dispose();
            defaultsList.Controls.Clear();

            foreach (var display in engine.Displays)
                defaultsList.Controls.Add(CreateDisplayRow(display));

            newGameRow = new SliderRow(removable: false)
            {
                Title = "New games",
                Subtitle = "Level for games added automatically",
            };
            newGameRow.Slider.Value = store.Settings.NewGameVibrance;
            newGameRow.Slider.ValueChanged += (s, e) =>
            {
                store.Settings.NewGameVibrance = newGameRow.Slider.Value;
                store.SaveSoon();
                if (newGameRow.Slider.IsDragging)
                    PreviewHere(newGameRow.Slider.Value);
            };
            newGameRow.Slider.DragStarted += (s, e) => PreviewHere(newGameRow.Slider.Value);
            newGameRow.Slider.DragCompleted += (s, e) => engine.ClearPreview();
            defaultsList.Controls.Add(newGameRow);

            defaultsList.ResumeLayout(true);
            PerformLayout();
        }

        private SliderRow CreateDisplayRow(DisplayInfo display)
        {
            bool supported = engine.Supports(display);
            var parts = new List<string> { "Desktop", $"{display.Bounds.Width}×{display.Bounds.Height}" };
            if (display.IsPrimary)
                parts.Add("main");
            var row = new SliderRow(removable: false)
            {
                Title = display.Name,
                Subtitle = supported ? string.Join(" · ", parts) : "Not driven by the NVIDIA GPU",
            };
            row.Slider.Value = engine.DesktopPercent(display);
            row.Slider.Enabled = supported;
            row.Slider.ValueChanged += (s, e) =>
            {
                var profile = store.Settings.FindDisplay(display.Id);
                if (profile == null)
                    return;
                profile.Vibrance = row.Slider.Value;
                store.SaveSoon();
                if (row.Slider.IsDragging)
                    engine.SetPreview(display.GdiName, row.Slider.Value);
                else
                    engine.Evaluate();
            };
            row.Slider.DragStarted += (s, e) => engine.SetPreview(display.GdiName, row.Slider.Value);
            row.Slider.DragCompleted += (s, e) => engine.ClearPreview();
            return row;
        }

        // ------------------------------------------------------------------ Status

        private void OnEngineStateChanged()
        {
            UpdateLiveIndicators();
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            pauseButton.Text = engine.IsPaused ? "Resume" : "Pause";
            hotkeyLabel.Text = hotkeysActive()
                ? "In game: Ctrl+Alt+PgUp / PgDn to tune (adds the game if new)"
                : "In-game hotkeys unavailable (Ctrl+Alt+PgUp/PgDn is taken by another app)";

            statusLabel.ForeColor = Theme.SubText;
            statusLabel.Text = StatusText(engine, store.Settings, out bool warning);
            if (warning)
                statusLabel.ForeColor = Theme.Warning;
            else if (engine.LiveGames.Count > 0 && !engine.IsPaused)
                statusLabel.ForeColor = Theme.Live;
        }

        public static string StatusText(VibranceEngine engine, AppSettings settings, out bool warning)
        {
            warning = false;
            if (!engine.IsBackendAvailable)
            {
                warning = true;
                return engine.BackendStatus;
            }
            if (engine.IsPaused)
                return "Paused · desktop colors restored";
            if (engine.LiveGames.Count > 0)
            {
                var first = engine.LiveGames.First();
                string where = engine.FindDisplay(first.Key)?.Name ?? first.Key;
                string more = engine.LiveGames.Count > 1 ? $" (+{engine.LiveGames.Count - 1} more)" : string.Empty;
                return $"● {first.Value.Name} at {first.Value.Vibrance}% on {where}{more}";
            }
            int count = settings.Games.Count;
            return count == 0 ? "Ready · waiting for a game" : $"Ready · {count} game{(count == 1 ? "" : "s")}";
        }

        // ------------------------------------------------------------------ Helpers

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            try
            {
                Autostart.Set(autostartCheck.Checked);
            }
            catch (Exception ex)
            {
                Log.Error("Could not change autostart", ex);
                MessageBox.Show(this, "Windows did not allow changing the startup setting.", "Vibra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                autostartCheck.CheckedChanged -= OnAutostartChanged;
                autostartCheck.Checked = Autostart.IsEnabled;
                autostartCheck.CheckedChanged += OnAutostartChanged;
            }
        }

        private static void SetupSectionLabel(Label label, string text)
        {
            label.Text = text;
            label.Font = Theme.Section;
            label.ForeColor = Theme.SubText;
            label.AutoSize = true;
        }

        private static void SetupCheck(CheckBox check, string text)
        {
            check.Text = text;
            check.ForeColor = Theme.Text;
            check.FlatStyle = FlatStyle.Flat;
            check.FlatAppearance.BorderColor = Theme.Border;
            check.FlatAppearance.CheckedBackColor = Theme.GradientMid;
            check.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            check.Cursor = Cursors.Hand;
        }
    }
}
