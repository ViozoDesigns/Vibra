using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly IconCache icons;
        private readonly Func<string> hotkeyHint;
        private readonly Action openSettings;
        private readonly Action requestLibraryScan;

        private readonly Label titleLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly GlyphButton settingsButton = new GlyphButton("");
        private readonly FlatButton pauseButton = new FlatButton();
        private readonly Label gamesHeader = new Label();
        private readonly FlatButton addButton = new FlatButton();
        private readonly GameGrid grid;
        private readonly GameEditor editor;
        private readonly Label hintLabel = new Label();
        private HashSet<GameProfile> shownGames = new HashSet<GameProfile>();

        public MainForm(VibranceEngine engine, SettingsStore store, IconCache icons, Func<string> hotkeyHint, Action openSettings, Action requestLibraryScan)
        {
            this.engine = engine;
            this.store = store;
            this.icons = icons;
            this.hotkeyHint = hotkeyHint;
            this.openSettings = openSettings;
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
            ClientSize = new Size(S(560), S(640));
            MinimumSize = SizeFromClientSize(new Size(S(420), S(460)));

            titleLabel.Text = "Vibra";
            titleLabel.Font = Theme.Title;
            titleLabel.AutoSize = true;

            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.ForeColor = Theme.SubText;

            settingsButton.Font = new Font("Segoe MDL2 Assets", 12f);
            settingsButton.BackColor = Theme.Background;
            settingsButton.Click += (s, e) => openSettings();
            new ToolTip().SetToolTip(settingsButton, "Settings");

            pauseButton.Click += (s, e) => engine.SetPaused(!engine.IsPaused);

            gamesHeader.Text = "GAMES";
            gamesHeader.Font = Theme.Section;
            gamesHeader.ForeColor = Theme.SubText;
            gamesHeader.AutoSize = true;

            addButton.Text = "+  Add game";
            addButton.Click += (s, e) => ShowAddMenu();

            grid = new GameGrid(icons)
            {
                EmptyText = "No games yet.\n\nStart a game and it's added automatically,\npick one from \"+ Add game\", or press your shortcut while playing.",
            };
            grid.SelectedChanged += (s, e) => editor.Game = grid.Selected;
            grid.ContextRequested += ShowTileMenu;
            grid.RemoveRequested += game => Defer(() => RemoveGame(game));

            editor = new GameEditor(icons);
            editor.VibranceChanged += OnEditorVibranceChanged;
            editor.RemoveClicked += (s, e) =>
            {
                var game = editor.Game;
                if (game != null)
                    Defer(() => RemoveGame(game));
            };

            hintLabel.AutoSize = false;
            hintLabel.AutoEllipsis = true;
            hintLabel.ForeColor = Theme.SubText;
            hintLabel.Font = Theme.Small;

            Controls.AddRange(new Control[] { titleLabel, statusLabel, settingsButton, pauseButton, gamesHeader, addButton, grid, editor, hintLabel });
            // Buttons always paint above the labels next to them.
            settingsButton.BringToFront();
            pauseButton.BringToFront();
            addButton.BringToFront();
            ResumeLayout(false);

            engine.StateChanged += OnEngineStateChanged;
            engine.GamesChanged += OnGamesChanged;
            engine.GameAdjusted += OnGameAdjusted;
            icons.Changed += OnIconsChanged;

            RebuildGames();
            RefreshStatus();
        }

        private int S(int px) => Theme.Scale(this, px);

        /// <summary>
        /// Runs after the current event finishes, so a control is never changed or removed while one
        /// of its own event handlers is still running.
        /// </summary>
        private void Defer(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
            else if (!IsDisposed)
                action();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.UseDarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height * 0.92)
                Height = (int)(area.Height * 0.92);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Closing the window keeps Vibra running in the tray.
                e.Cancel = true;
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
                icons.Changed -= OnIconsChanged;
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

            var pauseSize = new Size(S(92), S(32));
            int gear = S(32);
            titleLabel.Location = new Point(pad - S(2), y);
            pauseButton.Bounds = new Rectangle(pad + width - pauseSize.Width, y + S(6), pauseSize.Width, pauseSize.Height);
            settingsButton.Bounds = new Rectangle(pauseButton.Left - gear - S(6), y + S(6), gear, gear);
            y += titleLabel.Height + S(2);
            // Stop short of the gear/Pause buttons: the label would otherwise paint over their lower edge.
            statusLabel.Bounds = new Rectangle(pad, y, Math.Max(S(40), settingsButton.Left - S(12) - pad), S(20));
            y += statusLabel.Height + S(18);

            var addSize = new Size(S(124), S(32));
            addButton.Bounds = new Rectangle(pad + width - addSize.Width, y, addSize.Width, addSize.Height);
            gamesHeader.Location = new Point(pad, y + (addSize.Height - gamesHeader.Height) / 2);
            y += addSize.Height + S(10);

            int bottom = ClientSize.Height - S(12);
            hintLabel.Bounds = new Rectangle(pad, bottom - S(18), width, S(18));
            bottom = hintLabel.Top - S(10);
            editor.Bounds = new Rectangle(pad, bottom - editor.PreferredHeight, width, editor.PreferredHeight);
            bottom = editor.Top - S(12);

            grid.Bounds = new Rectangle(pad, y, width, Math.Max(S(80), bottom - y));
        }

        // ------------------------------------------------------------------ Games

        private List<GameProfile> SortedGames() =>
            store.Settings.Games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        private void RebuildGames()
        {
            var previous = grid.Selected;
            shownGames = new HashSet<GameProfile>(store.Settings.Games);
            grid.SetGames(SortedGames(), engine.IsLive);
            if (grid.Selected == null || !store.Settings.Games.Contains(grid.Selected))
                grid.Selected = PreferredSelection(previous);
            editor.Game = grid.Selected;
            RefreshStatus();
        }

        private GameProfile PreferredSelection(GameProfile previous)
        {
            var games = store.Settings.Games;
            if (previous != null && games.Contains(previous))
                return previous;
            return engine.LiveGames.Values.FirstOrDefault() ?? SortedGames().FirstOrDefault();
        }

        private void OnGamesChanged() => Defer(() =>
        {
            // A game that was just added (hotkey or auto-detect) is almost always the one on screen: show it.
            var added = store.Settings.Games.FirstOrDefault(g => !shownGames.Contains(g));
            RebuildGames();
            if (added != null)
                grid.Selected = added;
        });

        private void OnGameAdjusted(GameProfile game)
        {
            grid.RefreshTiles();
            if (ReferenceEquals(editor.Game, game))
                editor.RefreshValue();
        }

        private void OnIconsChanged()
        {
            grid.RefreshTiles();
            editor.Invalidate();
        }

        private void OnEditorVibranceChanged(object sender, EventArgs e)
        {
            // Only ever changes the monitor the game is actually on (nothing happens if it isn't on screen).
            store.SaveSoon();
            engine.Evaluate();
            grid.RefreshTiles();
        }

        private void RemoveGame(GameProfile game)
        {
            store.Settings.RemoveGame(game);
            store.SaveSoon();
            RebuildGames();
            engine.Evaluate();
        }

        private void ShowTileMenu(GameProfile game, Point location)
        {
            var menu = CreateMenu();
            AddItem(menu.Items, $"Remove {game.Name}", null, () => RemoveGame(game));
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(grid, location);
        }

        // ------------------------------------------------------------------ Add menu

        private ContextMenuStrip CreateMenu() => new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.Body,
        };

        private void ShowAddMenu()
        {
            var menu = CreateMenu();
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
                        AddGame(profile, app.Window);
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
                    AddItem(menu.Items, game.Name, game.Source, () => AddGame(game.ToProfile(settings.NewGameVibrance), IntPtr.Zero));
                if (installed.Count > inline)
                {
                    var more = new ToolStripMenuItem($"More installed games ({installed.Count - inline})");
                    foreach (var game in installed.Skip(inline))
                        AddItem(more.DropDownItems, game.Name, game.Source, () => AddGame(game.ToProfile(settings.NewGameVibrance), IntPtr.Zero));
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
                    }, app.Window));
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

        private void AddGame(GameProfile profile, IntPtr window)
        {
            if (profile?.Exe == null || store.Settings.FindGame(profile.Exe) != null)
                return;
            if (profile.OtherExes != null && profile.OtherExes.Count == 0)
                profile.OtherExes = null;
            if (!store.Settings.AddGame(profile))
            {
                // Already there (e.g. the exe picked was the game's own launcher): just show it.
                grid.Selected = store.Settings.FindGame(profile.Exe) ?? grid.Selected;
                return;
            }
            store.SaveSoon();
            if (window != IntPtr.Zero)
                icons.CaptureFromWindow(profile, window);
            RebuildGames();
            grid.Selected = profile;
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
                    IconPath = dialog.FileName,
                }, IntPtr.Zero);
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

        // ------------------------------------------------------------------ Status

        private void OnEngineStateChanged()
        {
            grid.RefreshTiles();
            editor.RefreshState(engine);
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            pauseButton.Text = engine.IsPaused ? "Resume" : "Pause";
            hintLabel.Text = hotkeyHint();
            statusLabel.Text = StatusText(engine, store.Settings, out bool warning);
            statusLabel.ForeColor = warning ? Theme.Warning
                : engine.LiveGames.Count > 0 && !engine.IsPaused ? Theme.Live
                : Theme.SubText;
            editor.RefreshState(engine);
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
    }

    /// <summary>Bottom panel for the selected game: icon, name, where it is, slider, remove.</summary>
    internal sealed class GameEditor : Control
    {
        private readonly IconCache icons;
        private readonly FlatButton removeButton = new FlatButton();
        private GameProfile game;
        private string subtitle = string.Empty;
        private bool live;
        private bool loading;

        public GameEditor(IconCache icons)
        {
            this.icons = icons;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;

            Slider = new VibranceSlider { BackColor = Theme.Surface };
            Slider.ValueChanged += (s, e) =>
            {
                Invalidate();
                if (loading || game == null)
                    return;
                game.Vibrance = Slider.Value;
                VibranceChanged?.Invoke(this, EventArgs.Empty);
            };
            Controls.Add(Slider);

            removeButton.Text = "Remove";
            removeButton.Font = Theme.Body;
            removeButton.BackColor = Theme.Surface;
            removeButton.Click += (s, e) => RemoveClicked?.Invoke(this, EventArgs.Empty);
            Controls.Add(removeButton);
        }

        public event EventHandler VibranceChanged;
        public event EventHandler RemoveClicked;

        public VibranceSlider Slider { get; }

        public int PreferredHeight => Theme.Scale(this, 112);

        public GameProfile Game
        {
            get => game;
            set
            {
                game = value;
                Slider.Enabled = removeButton.Enabled = game != null;
                RefreshValue();
                Invalidate();
            }
        }

        public void RefreshValue()
        {
            loading = true;
            Slider.Value = game?.Vibrance ?? VibranceScale.MinPercent;
            loading = false;
            Invalidate();
        }

        public void RefreshState(VibranceEngine engine)
        {
            var entry = engine.LiveGames.FirstOrDefault(kv => ReferenceEquals(kv.Value, game));
            live = game != null && entry.Value != null && !engine.IsPaused;
            if (game == null)
                subtitle = string.Empty;
            else if (live)
                subtitle = $"On screen · {engine.FindDisplay(entry.Key)?.Name ?? entry.Key}";
            else
                subtitle = $"Not on screen · changes apply when {game.Name} is";
            Invalidate();
        }

        private int S(int px) => Theme.Scale(this, px);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = S(16);
            var buttonSize = new Size(S(84), S(30));
            removeButton.Bounds = new Rectangle(Width - pad - buttonSize.Width, pad, buttonSize.Width, buttonSize.Height);
            int sliderTop = S(70);
            Slider.SetBounds(pad - S(6), sliderTop, Width - pad * 2 - S(52) + S(6), S(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), S(12)))
            using (var brush = new SolidBrush(Theme.Surface))
                g.FillPath(brush, path);

            int pad = S(16);
            if (game == null)
            {
                TextRenderer.DrawText(g, "Select a game to change its vibrance", Theme.Body, ClientRectangle, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int iconSize = S(40);
            var iconRect = new Rectangle(pad, pad, iconSize, iconSize);
            var icon = icons.Get(game);
            if (icon != null)
                g.DrawImage(icon, iconRect);
            else
                IconCache.DrawPlaceholder(g, iconRect, game.Name);

            int textLeft = iconRect.Right + S(12);
            int textWidth = Math.Max(1, removeButton.Left - S(8) - textLeft);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, game.Name, Theme.Value, new Rectangle(textLeft, pad - S(1), textWidth, S(22)), Theme.Text, flags);

            int subtitleLeft = textLeft;
            if (live)
            {
                int dot = S(7);
                using (var brush = new SolidBrush(Theme.Live))
                    g.FillEllipse(brush, textLeft, pad + S(28), dot, dot);
                subtitleLeft += dot + S(6);
            }
            TextRenderer.DrawText(g, subtitle, Theme.Small, new Rectangle(subtitleLeft, pad + S(22), Math.Max(1, textWidth - (subtitleLeft - textLeft)), S(18)),
                live ? Theme.Live : Theme.SubText, flags);

            var valueRect = new Rectangle(Slider.Right, Slider.Top, Width - pad - Slider.Right, Slider.Height);
            TextRenderer.DrawText(g, $"{Slider.Value}%", Theme.Value, valueRect, Theme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
