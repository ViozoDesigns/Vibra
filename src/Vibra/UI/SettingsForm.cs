using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Vibra.App;
using Vibra.Core;
using Vibra.Platform;

namespace Vibra.UI
{
    /// <summary>
    /// Everything that isn't a game: desktop level per monitor, level for new games, shortcuts and
    /// startup. Changes apply immediately.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly VibranceEngine engine;
        private readonly SettingsStore store;
        private readonly Func<Hotkey, bool> isHotkeyAvailable;
        private readonly Updater updater;

        private readonly Panel content = new Panel { AutoScroll = true };
        private readonly DisplayMap displayMap = new DisplayMap();
        private readonly SliderRow displayRow = new SliderRow(removable: false);
        private readonly SliderRow newGamesRow = new SliderRow(removable: false);
        private readonly CheckBox autoAddCheck = new CheckBox();
        private readonly HotkeyBox increaseBox = new HotkeyBox();
        private readonly HotkeyBox decreaseBox = new HotkeyBox();
        private readonly HotkeyBox pauseBox = new HotkeyBox();
        private readonly Stepper stepStepper = new Stepper(1, 20);
        private readonly Label hotkeyError = new Label();
        private readonly CheckBox autostartCheck = new CheckBox();
        private readonly CheckBox autoUpdateCheck = new CheckBox();
        private readonly Label versionLabel = new Label();
        private readonly FlatButton checkUpdatesButton = new FlatButton();
        private readonly FlatButton closeButton = new FlatButton();
        private bool loading;

        public SettingsForm(VibranceEngine engine, SettingsStore store, Func<Hotkey, bool> isHotkeyAvailable, Updater updater)
        {
            this.engine = engine;
            this.store = store;
            this.isHotkeyAvailable = isHotkeyAvailable;
            this.updater = updater;
            var settings = store.Settings;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Vibra settings";
            Icon = AppIcon.Large;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(500), S(720));
            content.BackColor = Theme.Background;
            Controls.Add(content);

            // Displays
            displayMap.Height = S(150);
            displayMap.SelectedChanged += (s, e) => ShowSelectedDisplay();
            displayRow.Slider.ValueChanged += OnDisplayLevelChanged;
            displayRow.Slider.DragStarted += (s, e) => PreviewSelectedDisplay();
            displayRow.Slider.DragCompleted += (s, e) => engine.ClearPreview();

            // New games
            newGamesRow.Title = "New games";
            newGamesRow.Subtitle = "Level for games added automatically";
            newGamesRow.Slider.Value = settings.NewGameVibrance;
            newGamesRow.Slider.ValueChanged += (s, e) =>
            {
                store.Settings.NewGameVibrance = newGamesRow.Slider.Value;
                store.SaveSoon();
            };
            SetupCheck(autoAddCheck, "Add games automatically when they start");
            autoAddCheck.Checked = !settings.DisableAutoAdd;
            autoAddCheck.CheckedChanged += (s, e) =>
            {
                store.Settings.DisableAutoAdd = !autoAddCheck.Checked;
                store.SaveSoon();
            };

            // Shortcuts
            loading = true;
            increaseBox.Value = Parse(settings.HotkeyIncrease);
            decreaseBox.Value = Parse(settings.HotkeyDecrease);
            pauseBox.Value = Parse(settings.HotkeyPause);
            stepStepper.Value = settings.HotkeyStep;
            loading = false;
            increaseBox.ValueChanged += OnHotkeyChanged;
            decreaseBox.ValueChanged += OnHotkeyChanged;
            pauseBox.ValueChanged += OnHotkeyChanged;
            stepStepper.ValueChanged += (s, e) =>
            {
                store.Settings.HotkeyStep = stepStepper.Value;
                store.SaveSoon();
            };
            hotkeyError.ForeColor = Theme.Warning;
            hotkeyError.Font = Theme.Small;
            hotkeyError.AutoSize = false;

            // General
            SetupCheck(autostartCheck, "Start Vibra with Windows");
            autostartCheck.Checked = Autostart.IsEnabled;
            autostartCheck.CheckedChanged += OnAutostartChanged;

            // Updates
            SetupCheck(autoUpdateCheck, "Update automatically");
            autoUpdateCheck.Checked = !settings.DisableAutoUpdate;
            autoUpdateCheck.CheckedChanged += (s, e) =>
            {
                store.Settings.DisableAutoUpdate = !autoUpdateCheck.Checked;
                store.SaveSoon();
            };
            versionLabel.ForeColor = Theme.SubText;
            versionLabel.Font = Theme.Small;
            versionLabel.AutoSize = false;
            versionLabel.AutoEllipsis = true;
            versionLabel.TextAlign = ContentAlignment.MiddleLeft;
            checkUpdatesButton.Text = "Check now";
            checkUpdatesButton.Font = Theme.Body;
            checkUpdatesButton.Click += (s, e) => updater.CheckNow();
            updater.StatusChanged += OnUpdaterStatusChanged;
            OnUpdaterStatusChanged();

            closeButton.Text = "Done";
            closeButton.Click += (s, e) => Close();
            AcceptButton = closeButton;
            Controls.Add(closeButton);

            BuildContent();
            ResumeLayout(false);

            RefreshDisplays();
            engine.DisplaysChanged += OnDisplaysChanged;
        }

        private void RefreshDisplays()
        {
            displayMap.SetDisplays(engine.Displays, engine.DesktopPercent, engine.Supports);
            ShowSelectedDisplay();
        }

        private void OnDisplaysChanged()
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(RefreshDisplays));
        }

        private int S(int px) => Theme.Scale(this, px);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.UseDarkTitleBar(Handle);
            Theme.UseDarkScrollbars(content);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var area = Owner != null ? Screen.FromControl(Owner).WorkingArea : Screen.FromControl(this).WorkingArea;
            if (Height > area.Height * 0.92)
                Height = (int)(area.Height * 0.92);

            // Show() ignores CenterParent, so center over the main window by hand (kept on screen).
            if (Owner != null)
            {
                int x = Owner.Left + (Owner.Width - Width) / 2;
                int y = Owner.Top + (Owner.Height - Height) / 2;
                Location = new Point(
                    Math.Max(area.Left, Math.Min(x, area.Right - Width)),
                    Math.Max(area.Top, Math.Min(y, area.Bottom - Height)));
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.DisplaysChanged -= OnDisplaysChanged;
            updater.StatusChanged -= OnUpdaterStatusChanged;
            engine.ClearPreview();
            store.SaveSoon();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------ Layout

        private int contentY;

        private void BuildContent()
        {
            contentY = S(6);
            AddSection("DISPLAYS", "Desktop level: used on a monitor whenever no game is on it");
            AddFull(displayMap, S(8));
            AddFull(displayRow, S(22));

            AddSection("NEW GAMES", null);
            AddFull(newGamesRow, S(8));
            AddFull(autoAddCheck, S(22), S(26));

            AddSection("SHORTCUTS", "Work while you play. Click a box, then press the keys; Backspace turns it off.");
            AddLabeled("Increase vibrance", increaseBox);
            AddLabeled("Decrease vibrance", decreaseBox);
            AddLabeled("Pause / resume Vibra", pauseBox);
            AddLabeled("Step per press", stepStepper, S(110));
            AddFull(hotkeyError, S(14), S(18));

            AddSection("GENERAL", null);
            AddFull(autostartCheck, S(8), S(26));
            AddFull(autoUpdateCheck, S(8), S(26));
            content.Controls.Add(checkUpdatesButton);
            checkUpdatesButton.SetBounds(S(22), contentY, S(110), S(30));
            content.Controls.Add(versionLabel);
            versionLabel.SetBounds(S(22) + S(122), contentY, S(318), S(30));
            versionLabel.Tag = "rest";
            contentY += S(30) + S(16);
        }

        private void OnUpdaterStatusChanged()
        {
            versionLabel.Text = $"Version {Updater.CurrentVersionText} · {updater.Status}";
        }

        private void AddSection(string title, string description)
        {
            var header = new Label { Text = title, Font = Theme.Section, ForeColor = Theme.SubText, AutoSize = true };
            content.Controls.Add(header);
            header.Location = new Point(S(22), contentY);
            contentY += header.Height + S(4);
            if (description != null)
            {
                var text = new Label { Text = description, Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = false };
                content.Controls.Add(text);
                text.Tag = "full";
                text.SetBounds(S(22), contentY, S(440), S(32));
                contentY += text.Height + S(4);
            }
            else
            {
                contentY += S(4);
            }
        }

        private void AddFull(Control control, int spaceAfter, int? height = null)
        {
            control.Tag = "full";
            content.Controls.Add(control);
            control.SetBounds(S(22), contentY, S(440), height ?? control.Height);
            contentY += control.Height + spaceAfter;
        }

        private void AddLabeled(string text, Control control, int? width = null)
        {
            var label = new Label { Text = text, ForeColor = Theme.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            content.Controls.Add(label);
            content.Controls.Add(control);
            int rowHeight = S(34);
            label.SetBounds(S(22), contentY, S(190), rowHeight);
            control.SetBounds(S(222), contentY, width ?? S(240), rowHeight);
            contentY += rowHeight + S(8);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int buttonHeight = S(34);
            int pad = S(16);
            closeButton.SetBounds(ClientSize.Width - pad - S(100), ClientSize.Height - pad - buttonHeight, S(100), buttonHeight);
            content.SetBounds(0, S(8), ClientSize.Width, closeButton.Top - S(16));

            // Stretch full-width rows to the content width (minus a scrollbar when one is needed).
            int width = content.ClientSize.Width - S(44);
            foreach (Control control in content.Controls)
            {
                if ("full".Equals(control.Tag))
                    control.Width = Math.Max(S(200), width);
                else if ("rest".Equals(control.Tag))
                    control.Width = Math.Max(S(100), S(22) + width - control.Left);
            }
        }

        // ------------------------------------------------------------------ Displays

        private DisplayInfo SelectedDisplay => displayMap.Selected;

        private void ShowSelectedDisplay()
        {
            var display = SelectedDisplay;
            if (display == null)
            {
                displayRow.Title = "No monitors found";
                displayRow.Subtitle = string.Empty;
                displayRow.Slider.Enabled = false;
                return;
            }

            bool supported = engine.Supports(display);
            var parts = new[] { $"Display {display.Number}", $"{display.Bounds.Width}×{display.Bounds.Height}", display.IsPrimary ? "main" : null };
            displayRow.Title = display.Name;
            displayRow.Subtitle = supported ? string.Join(" · ", parts.Where(p => p != null)) : "Not driven by the NVIDIA GPU";
            displayRow.Slider.Enabled = supported;
            loading = true;
            displayRow.Slider.Value = engine.DesktopPercent(display);
            loading = false;
        }

        private void OnDisplayLevelChanged(object sender, EventArgs e)
        {
            var display = SelectedDisplay;
            if (loading || display == null)
                return;
            var profile = store.Settings.FindDisplay(display.Id);
            if (profile == null)
                return;
            profile.Vibrance = displayRow.Slider.Value;
            store.SaveSoon();
            displayMap.Invalidate();
            if (displayRow.Slider.IsDragging)
                PreviewSelectedDisplay();
            else
                engine.Evaluate();
        }

        private void PreviewSelectedDisplay()
        {
            var display = SelectedDisplay;
            if (display != null && engine.Supports(display))
                engine.SetPreview(display.GdiName, displayRow.Slider.Value);
        }

        // ------------------------------------------------------------------ Shortcuts

        private static Hotkey Parse(string text) => Hotkey.TryParse(text, out Hotkey hotkey) ? hotkey : Hotkey.None;

        private void OnHotkeyChanged(object sender, EventArgs e)
        {
            if (loading)
                return;
            var box = (HotkeyBox)sender;
            var boxes = new[] { increaseBox, decreaseBox, pauseBox };
            var previous = Parse(box == increaseBox ? store.Settings.HotkeyIncrease
                : box == decreaseBox ? store.Settings.HotkeyDecrease
                : store.Settings.HotkeyPause);

            string error = null;
            if (!box.Value.IsNone && boxes.Any(other => other != box && other.Value.Equals(box.Value)))
                error = $"{box.Value} is already used for another action.";
            else if (!box.Value.IsNone && !box.Value.Equals(previous) && !isHotkeyAvailable(box.Value))
                error = $"{box.Value} is already used by another app. Try a different one.";

            if (error != null)
            {
                hotkeyError.Text = error;
                loading = true;
                box.Value = previous;
                loading = false;
                return;
            }

            hotkeyError.Text = string.Empty;
            store.Settings.HotkeyIncrease = increaseBox.Value.ToString();
            store.Settings.HotkeyDecrease = decreaseBox.Value.ToString();
            store.Settings.HotkeyPause = pauseBox.Value.ToString();
            store.SaveSoon();
        }

        // ------------------------------------------------------------------ General

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
