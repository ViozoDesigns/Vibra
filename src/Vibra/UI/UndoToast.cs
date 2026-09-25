using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Vibra.UI
{
    /// <summary>"Added 23 games · Undo" bar at the bottom of the main window; hides itself after a few seconds.</summary>
    internal sealed class UndoToast : Control
    {
        private const int VisibleMs = 8000;

        private readonly FlatButton actionButton = new FlatButton();
        private readonly Timer hideTimer = new Timer { Interval = VisibleMs };
        private string message = string.Empty;

        public UndoToast()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;

            actionButton.Font = Theme.BodyStrong;
            actionButton.BackColor = Theme.SurfaceHover;
            actionButton.ForeColor = Theme.Text;
            actionButton.Click += (s, e) => ActionClicked?.Invoke(this, EventArgs.Empty);
            Controls.Add(actionButton);

            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                Visible = false;
            };
        }

        public event EventHandler ActionClicked;

        /// <summary>True when the button redoes (after an undo) rather than undoes.</summary>
        public bool OffersRedo { get; private set; }

        public void Show(string text, bool offerRedo, bool showAction = true)
        {
            message = text ?? string.Empty;
            OffersRedo = offerRedo;
            actionButton.Text = offerRedo ? "Redo" : "Undo";
            actionButton.Visible = showAction;
            Visible = true;
            BringToFront();
            PerformLayout();
            Invalidate();
            hideTimer.Stop();
            hideTimer.Start();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = Theme.Scale(this, 3);
            int width = Theme.Scale(this, 76);
            actionButton.SetBounds(Width - width - pad, pad, width, Math.Max(1, Height - pad * 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Scale(this, 8)))
            using (var brush = new SolidBrush(Theme.Surface))
                g.FillPath(brush, path);

            int right = actionButton.Visible ? actionButton.Left - Theme.Scale(this, 8) : Width - Theme.Scale(this, 12);
            var textRect = new Rectangle(Theme.Scale(this, 12), 0, Math.Max(1, right - Theme.Scale(this, 12)), Height);
            TextRenderer.DrawText(g, message, Theme.Body, textRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                hideTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
