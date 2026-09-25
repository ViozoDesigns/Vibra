using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Vibra.Core;

namespace Vibra.UI
{
    /// <summary>50-100% slider with a vibrance gradient fill.</summary>
    internal sealed class VibranceSlider : Control
    {
        private int value = VibranceScale.MinPercent;
        private bool dragging;
        private bool hovering;

        public VibranceSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public event EventHandler ValueChanged;
        public event EventHandler DragStarted;
        public event EventHandler DragCompleted;

        public int Value
        {
            get => value;
            set
            {
                int clamped = VibranceScale.Clamp(value);
                if (clamped == this.value)
                    return;
                this.value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsDragging => dragging;

        private int ThumbRadius => Theme.Scale(this, 8);

        private RectangleF TrackRect
        {
            get
            {
                float inset = ThumbRadius + 2;
                float height = Math.Max(4, Theme.Scale(this, 6));
                return new RectangleF(inset, (Height - height) / 2f, Math.Max(1, Width - 2 * inset), height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var track = TrackRect;
            using (var path = Theme.RoundedRect(track, track.Height / 2f))
            using (var brush = new SolidBrush(Theme.Track))
                g.FillPath(brush, path);

            float fraction = (value - VibranceScale.MinPercent) / (float)(VibranceScale.MaxPercent - VibranceScale.MinPercent);
            float x = track.X + track.Width * fraction;

            if (Enabled && x - track.X >= 1)
            {
                var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, x - track.X), track.Height);
                var gradientArea = new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height);
                using (var brush = new LinearGradientBrush(gradientArea, Theme.GradientStart, Theme.GradientEnd, LinearGradientMode.Horizontal))
                using (var path = Theme.RoundedRect(fill, track.Height / 2f))
                {
                    brush.InterpolationColors = Theme.VibranceBlend();
                    g.FillPath(brush, path);
                }
            }

            float radius = ThumbRadius * (dragging || hovering ? 1.12f : 1f);
            var thumb = new RectangleF(x - radius, Height / 2f - radius, radius * 2, radius * 2);
            using (var brush = new SolidBrush(Enabled ? Theme.Text : Theme.SubText))
                g.FillEllipse(brush, thumb);

            if (Focused && ShowFocusCues)
            {
                float grow = Theme.Scale(this, 3);
                using (var pen = new Pen(Color.FromArgb(160, Theme.GradientMid), Theme.Scale(this, 2)))
                    g.DrawEllipse(pen, RectangleF.Inflate(thumb, grow, grow));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left)
                return;
            Focus();
            dragging = true;
            Capture = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
            SetFromX(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
                SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            EndDrag();
        }

        private void EndDrag()
        {
            if (!dragging)
                return;
            dragging = false;
            Capture = false;
            Invalidate();
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovering = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovering = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value -= 1;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value += 1;
                    break;
                case Keys.PageDown:
                    Value -= 5;
                    break;
                case Keys.PageUp:
                    Value += 5;
                    break;
                case Keys.Home:
                    Value = VibranceScale.MinPercent;
                    break;
                case Keys.End:
                    Value = VibranceScale.MaxPercent;
                    break;
                default:
                    base.OnKeyDown(e);
                    return;
            }
            e.Handled = true;
        }

        private void SetFromX(int px)
        {
            var track = TrackRect;
            float fraction = Math.Max(0f, Math.Min(1f, (px - track.X) / track.Width));
            Value = VibranceScale.MinPercent + (int)Math.Round(fraction * (VibranceScale.MaxPercent - VibranceScale.MinPercent));
        }
    }

    /// <summary>Small borderless button that paints a single glyph.</summary>
    internal sealed class GlyphButton : Control
    {
        private bool hovering;

        public GlyphButton(string glyph)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Text = glyph;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (hovering)
            {
                using (var path = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Scale(this, 6)))
                using (var brush = new SolidBrush(Theme.SurfaceHover))
                    g.FillPath(brush, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, hovering ? Theme.Text : Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovering = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovering = false;
            Invalidate();
        }
    }

    /// <summary>A card with a title, subtitle, slider and percentage, optionally removable.</summary>
    internal sealed class SliderRow : Control
    {
        private readonly GlyphButton removeButton;
        private string title = string.Empty;
        private string subtitle = string.Empty;
        private bool live;

        public SliderRow(bool removable)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;

            Slider = new VibranceSlider { BackColor = Theme.Surface };
            Slider.ValueChanged += (s, e) => Invalidate();
            Slider.EnabledChanged += (s, e) => Invalidate();
            Controls.Add(Slider);

            if (removable)
            {
                removeButton = new GlyphButton("✕") { BackColor = Theme.Surface };
                removeButton.Click += (s, e) => RemoveClicked?.Invoke(this, EventArgs.Empty);
                Controls.Add(removeButton);
            }

            Height = Theme.Scale(this, 58);
        }

        public event EventHandler RemoveClicked;

        public VibranceSlider Slider { get; }

        public string Title
        {
            get => title;
            set
            {
                title = value ?? string.Empty;
                Invalidate();
            }
        }

        public string Subtitle
        {
            get => subtitle;
            set
            {
                subtitle = value ?? string.Empty;
                Invalidate();
            }
        }

        /// <summary>Shows a green dot: this game is on screen right now.</summary>
        public bool Live
        {
            get => live;
            set
            {
                if (live == value)
                    return;
                live = value;
                Invalidate();
            }
        }

        private int Pad => Theme.Scale(this, 14);
        private int ValueWidth => Theme.Scale(this, 46);
        private int ButtonSize => removeButton == null ? 0 : Theme.Scale(this, 26);
        private int TextWidth => Math.Max(Theme.Scale(this, 120), (int)(Width * 0.36));

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int sliderHeight = Theme.Scale(this, 26);
            int sliderLeft = Pad + TextWidth + Theme.Scale(this, 8);
            int right = Width - Pad - (ButtonSize > 0 ? ButtonSize + Theme.Scale(this, 4) : 0);
            int sliderRight = right - ValueWidth;
            Slider.SetBounds(sliderLeft, (Height - sliderHeight) / 2, Math.Max(Theme.Scale(this, 40), sliderRight - sliderLeft), sliderHeight);
            removeButton?.SetBounds(Width - Pad - ButtonSize + Theme.Scale(this, 4), (Height - ButtonSize) / 2, ButtonSize, ButtonSize);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            using (var path = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Scale(this, 10)))
            using (var brush = new SolidBrush(Theme.Surface))
                g.FillPath(brush, path);

            int x = Pad;
            int lineHeight = Height / 2;
            if (live)
            {
                int dot = Theme.Scale(this, 7);
                using (var brush = new SolidBrush(Theme.Live))
                    g.FillEllipse(brush, x, lineHeight - Theme.Scale(this, 3) - dot / 2 - Theme.Scale(this, 5), dot, dot);
                x += dot + Theme.Scale(this, 6);
            }

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int textRight = Pad + TextWidth;
            var titleRect = new Rectangle(x, lineHeight - Theme.Scale(this, 21), Math.Max(1, textRight - x), Theme.Scale(this, 20));
            var subtitleRect = new Rectangle(Pad, lineHeight + Theme.Scale(this, 1), Math.Max(1, TextWidth), Theme.Scale(this, 18));
            TextRenderer.DrawText(g, title, Theme.BodyStrong, titleRect, Enabled ? Theme.Text : Theme.SubText, flags | TextFormatFlags.Bottom);
            TextRenderer.DrawText(g, subtitle, Theme.Small, subtitleRect, live ? Theme.Live : Theme.SubText, flags | TextFormatFlags.Top);

            var valueRect = new Rectangle(Slider.Right, 0, ValueWidth, Height);
            TextRenderer.DrawText(g, Slider.Enabled ? $"{Slider.Value}%" : "–", Theme.Value, valueRect, Slider.Enabled ? Theme.Text : Theme.SubText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class FlatButton : Button
    {
        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.BodyStrong;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderColor = Theme.Border;
            FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            FlatAppearance.MouseDownBackColor = Theme.Border;
        }

        protected override bool ShowFocusCues => false;
    }
}
