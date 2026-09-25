using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Vibra.Core;
using Vibra.Platform;

namespace Vibra.UI
{
    /// <summary>Monitors drawn in their physical arrangement (like Windows display settings); click to select.</summary>
    internal sealed class DisplayMap : Control
    {
        private List<DisplayInfo> displays = new List<DisplayInfo>();
        private Func<DisplayInfo, int> levelOf = d => VibranceScale.MinPercent;
        private Func<DisplayInfo, bool> supported = d => true;
        private DisplayInfo selected;
        private DisplayInfo hovered;

        public DisplayMap()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        public event EventHandler SelectedChanged;

        public DisplayInfo Selected
        {
            get => selected;
            set
            {
                if (ReferenceEquals(selected, value))
                    return;
                selected = value;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetDisplays(IEnumerable<DisplayInfo> list, Func<DisplayInfo, int> level, Func<DisplayInfo, bool> isSupported)
        {
            displays = list.ToList();
            levelOf = level;
            supported = isSupported;
            Invalidate();
            if (selected == null || !displays.Contains(selected))
                Selected = displays.FirstOrDefault(d => supported(d)) ?? displays.FirstOrDefault();
        }

        private Dictionary<DisplayInfo, RectangleF> ComputeLayout()
        {
            var result = new Dictionary<DisplayInfo, RectangleF>();
            if (displays.Count == 0)
                return result;

            int left = displays.Min(d => d.Bounds.Left), top = displays.Min(d => d.Bounds.Top);
            int right = displays.Max(d => d.Bounds.Right), bottom = displays.Max(d => d.Bounds.Bottom);
            float margin = Theme.Scale(this, 6);
            float scale = Math.Min((Width - margin * 2) / Math.Max(1f, right - left), (Height - margin * 2) / Math.Max(1f, bottom - top));
            float offsetX = (Width - (right - left) * scale) / 2f;
            float offsetY = (Height - (bottom - top) * scale) / 2f;
            float gap = Theme.Scale(this, 3);

            foreach (var d in displays)
            {
                result[d] = new RectangleF(
                    offsetX + (d.Bounds.Left - left) * scale + gap,
                    offsetY + (d.Bounds.Top - top) * scale + gap,
                    d.Bounds.Width * scale - gap * 2,
                    d.Bounds.Height * scale - gap * 2);
            }
            return result;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var pair in ComputeLayout())
            {
                var display = pair.Key;
                var rect = pair.Value;
                bool isSupported = supported(display);
                bool isSelected = ReferenceEquals(display, selected);
                bool isHovered = ReferenceEquals(display, hovered);

                using (var path = Theme.RoundedRect(rect, Theme.Scale(this, 8)))
                {
                    using (var brush = new SolidBrush(isSelected || isHovered ? Theme.SurfaceHover : Theme.Surface))
                        g.FillPath(brush, path);

                    if (isSelected)
                    {
                        using (var gradient = new LinearGradientBrush(rect, Theme.GradientStart, Theme.GradientEnd, LinearGradientMode.ForwardDiagonal) { InterpolationColors = Theme.VibranceBlend() })
                        using (var pen = new Pen(gradient, Theme.Scale(this, 2)))
                            g.DrawPath(pen, path);
                    }
                    else
                    {
                        using (var pen = new Pen(Theme.Border, 1))
                            g.DrawPath(pen, path);
                    }
                }

                // Screen area with a vibrance-tinted glow that grows with the level.
                var screen = RectangleF.Inflate(rect, -Theme.Scale(this, 6), -Theme.Scale(this, 6));
                if (isSupported && screen.Width > 4 && screen.Height > 4)
                {
                    int level = levelOf(display);
                    int alpha = 20 + (level - VibranceScale.MinPercent) * 2;
                    using (var path = Theme.RoundedRect(screen, Theme.Scale(this, 4)))
                    using (var glow = new LinearGradientBrush(screen, Color.FromArgb(alpha, Theme.GradientStart), Color.FromArgb(alpha, Theme.GradientEnd), LinearGradientMode.ForwardDiagonal))
                        g.FillPath(glow, path);
                }

                DrawLabels(g, rect, display.Number > 0 ? display.Number.ToString() : "?",
                    isSupported ? $"{levelOf(display)}%" : "n/a", isSupported);
            }
        }

        /// <summary>Monitor number above its level, centred as one block so they never overlap.</summary>
        private void DrawLabels(Graphics g, RectangleF rect, string number, string caption, bool isSupported)
        {
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var numberFont = rect.Height >= Theme.Scale(this, 70) ? Theme.Title : Theme.Value;
            Size numberSize = TextRenderer.MeasureText(g, number, numberFont, Size.Empty, flags);
            Size captionSize = TextRenderer.MeasureText(g, caption, Theme.Small, Size.Empty, flags);
            int gap = Theme.Scale(this, 2);
            var bounds = Rectangle.Round(rect);

            if (numberSize.Height + gap + captionSize.Height > bounds.Height - Theme.Scale(this, 8))
            {
                // Too flat for two lines: "2 · 70%" on one line.
                TextRenderer.DrawText(g, $"{number} · {caption}", Theme.Small, bounds, isSupported ? Theme.Text : Theme.SubText,
                    flags | TextFormatFlags.VerticalCenter);
                return;
            }

            int top = bounds.Y + (bounds.Height - numberSize.Height - gap - captionSize.Height) / 2;
            TextRenderer.DrawText(g, number, numberFont, new Rectangle(bounds.X, top, bounds.Width, numberSize.Height),
                isSupported ? Theme.Text : Theme.SubText, flags);
            TextRenderer.DrawText(g, caption, Theme.Small, new Rectangle(bounds.X, top + numberSize.Height + gap, bounds.Width, captionSize.Height),
                Theme.SubText, flags);
        }

        private DisplayInfo HitTest(Point point) =>
            ComputeLayout().FirstOrDefault(pair => pair.Value.Contains(point)).Key;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            if (!ReferenceEquals(hit, hovered))
            {
                hovered = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = null;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = HitTest(e.Location);
            if (hit != null)
                Selected = hit;
        }
    }

    /// <summary>Click, then press a key combination. Backspace/Delete clears, Esc keeps the old one.</summary>
    internal sealed class HotkeyBox : Control
    {
        private Hotkey value;
        private string hint;

        public HotkeyBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
        }

        public event EventHandler ValueChanged;

        public Hotkey Value
        {
            get => value;
            set
            {
                if (this.value.Equals(value))
                    return;
                this.value = value;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, Theme.Scale(this, 6)))
            {
                using (var brush = new SolidBrush(Focused ? Theme.SurfaceHover : Theme.Surface))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Focused ? Theme.GradientMid : Theme.Border, Focused ? Theme.Scale(this, 2) : 1))
                    g.DrawPath(pen, path);
            }

            string text = Focused ? hint ?? "Press a shortcut…" : value.ToString();
            Color color = Focused || value.IsNone ? Theme.SubText : Theme.Text;
            var textRect = Rectangle.Inflate(ClientRectangle, -Theme.Scale(this, 10), 0);
            TextRenderer.DrawText(g, text, Theme.Body, textRect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            hint = null;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused)
                return base.ProcessCmdKey(ref msg, keyData);

            Keys key = keyData & Keys.KeyCode;
            Keys modifiers = keyData & Keys.Modifiers;

            if (key == Keys.Tab && modifiers == Keys.None)
                return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return true;
            if (modifiers == Keys.None && (key == Keys.Back || key == Keys.Delete))
            {
                Value = Hotkey.None;
                Parent?.SelectNextControl(this, true, true, true, true);
                return true;
            }
            if (modifiers == Keys.None && key == Keys.Escape)
            {
                Parent?.SelectNextControl(this, true, true, true, true);
                return true;
            }

            int vk = (int)key;
            if (!KeyNames.IsSupported(vk))
            {
                ShowHint("That key can't be used");
                return true;
            }

            int mods = ((modifiers & Keys.Control) != 0 ? Hotkey.Control : 0) |
                       ((modifiers & Keys.Alt) != 0 ? Hotkey.Alt : 0) |
                       ((modifiers & Keys.Shift) != 0 ? Hotkey.Shift : 0);
            var candidate = new Hotkey(mods, vk);
            if (!candidate.IsAllowed)
            {
                ShowHint("Add Ctrl, Alt or Shift");
                return true;
            }

            Value = candidate;
            Parent?.SelectNextControl(this, true, true, true, true);
            return true;
        }

        private void ShowHint(string text)
        {
            hint = text;
            Invalidate();
        }
    }

    /// <summary>[−] 5% [+] number picker.</summary>
    internal sealed class Stepper : Control
    {
        private readonly GlyphButton minus = new GlyphButton("−");
        private readonly GlyphButton plus = new GlyphButton("+");
        private int value;

        public Stepper(int minimum, int maximum)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Minimum = minimum;
            Maximum = maximum;
            BackColor = Theme.Background;
            minus.BackColor = plus.BackColor = Theme.Surface;
            minus.Click += (s, e) => Value -= 1;
            plus.Click += (s, e) => Value += 1;
            Controls.Add(minus);
            Controls.Add(plus);
        }

        public event EventHandler ValueChanged;

        public int Minimum { get; }
        public int Maximum { get; }
        public string Suffix { get; set; } = "%";

        public int Value
        {
            get => value;
            set
            {
                int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (clamped == this.value)
                    return;
                this.value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int size = Height - Theme.Scale(this, 4);
            minus.SetBounds(Theme.Scale(this, 2), Theme.Scale(this, 2), size, size);
            plus.SetBounds(Width - size - Theme.Scale(this, 2), Theme.Scale(this, 2), size, size);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Scale(this, 6)))
            using (var brush = new SolidBrush(Theme.Surface))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, $"{value}{Suffix}", Theme.BodyStrong, ClientRectangle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
