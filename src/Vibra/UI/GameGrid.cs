using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Vibra.Core;

namespace Vibra.UI
{
    /// <summary>Scrollable grid of game tiles (icon, name, level, on-screen dot). One tile is selected.</summary>
    internal sealed class GameGrid : Panel
    {
        private readonly IconCache icons;
        private List<GameProfile> games = new List<GameProfile>();
        private Func<GameProfile, bool> isLive = g => false;
        private GameProfile selected;
        private GameProfile hovered;

        public GameGrid(IconCache icons)
        {
            this.icons = icons;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AutoScroll = true;
            BackColor = Theme.Background;
        }

        public event EventHandler SelectedChanged;
        public event Action<GameProfile, Point> ContextRequested;
        public event Action<GameProfile> RemoveRequested;

        public string EmptyText { get; set; } = string.Empty;

        public GameProfile Selected
        {
            get => selected;
            set
            {
                if (ReferenceEquals(selected, value))
                    return;
                selected = value;
                ScrollToSelected();
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetGames(IEnumerable<GameProfile> list, Func<GameProfile, bool> liveCheck)
        {
            games = list.ToList();
            isLive = liveCheck ?? (g => false);
            if (!games.Contains(hovered))
                hovered = null;
            UpdateScrollSize();
            Invalidate();
            if (selected != null && !games.Contains(selected))
                Selected = games.FirstOrDefault();
        }

        public void RefreshTiles() => Invalidate();

        // ------------------------------------------------------------------ Layout

        private int Gap => Theme.Scale(this, 10);
        private int TileHeight => Theme.Scale(this, 128);
        private int MinTileWidth => Theme.Scale(this, 112);

        private int Columns => Math.Max(1, (ClientSize.Width + Gap) / (MinTileWidth + Gap));

        private int TileWidth => Math.Max(MinTileWidth / 2, (ClientSize.Width - (Columns - 1) * Gap) / Columns);

        private Rectangle TileBounds(int index)
        {
            int column = index % Columns;
            int row = index / Columns;
            return new Rectangle(column * (TileWidth + Gap), row * (TileHeight + Gap) + AutoScrollPosition.Y, TileWidth, TileHeight);
        }

        private void UpdateScrollSize()
        {
            int rows = (games.Count + Columns - 1) / Columns;
            AutoScrollMinSize = new Size(0, rows == 0 ? 0 : rows * (TileHeight + Gap) - Gap);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateScrollSize();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.UseDarkScrollbars(this);
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < games.Count; i++)
            {
                if (TileBounds(i).Contains(point))
                    return i;
            }
            return -1;
        }

        private void ScrollToSelected()
        {
            int index = games.IndexOf(selected);
            if (index < 0 || !IsHandleCreated)
                return;
            var bounds = TileBounds(index);
            if (bounds.Top < 0)
                AutoScrollPosition = new Point(0, bounds.Top - AutoScrollPosition.Y);
            else if (bounds.Bottom > ClientSize.Height)
                AutoScrollPosition = new Point(0, bounds.Bottom - AutoScrollPosition.Y - ClientSize.Height);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        // ------------------------------------------------------------------ Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (games.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Theme.Body, ClientRectangle, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            for (int i = 0; i < games.Count; i++)
            {
                var bounds = TileBounds(i);
                if (bounds.Bottom < e.ClipRectangle.Top || bounds.Top > e.ClipRectangle.Bottom)
                    continue;
                DrawTile(g, games[i], bounds);
            }
        }

        private void DrawTile(Graphics g, GameProfile game, Rectangle bounds)
        {
            bool isSelected = ReferenceEquals(game, selected);
            bool isHovered = ReferenceEquals(game, hovered);
            bool live = isLive(game);
            float radius = Theme.Scale(this, 12);

            var rect = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1);
            using (var path = Theme.RoundedRect(rect, radius))
            {
                using (var brush = new SolidBrush(isSelected || isHovered ? Theme.SurfaceHover : Theme.Surface))
                    g.FillPath(brush, path);
                if (isSelected)
                {
                    using (var brush = new LinearGradientBrush(rect, Theme.GradientStart, Theme.GradientEnd, LinearGradientMode.ForwardDiagonal) { InterpolationColors = Theme.VibranceBlend() })
                    using (var pen = new Pen(brush, Theme.Scale(this, 2)))
                        g.DrawPath(pen, path);
                }
            }

            int iconSize = Theme.Scale(this, 48);
            var iconRect = new Rectangle(bounds.X + (bounds.Width - iconSize) / 2, bounds.Y + Theme.Scale(this, 14), iconSize, iconSize);
            var icon = icons.Get(game);
            if (icon != null)
                g.DrawImage(icon, iconRect);
            else
                IconCache.DrawPlaceholder(g, iconRect, game.Name);

            if (live)
            {
                int dot = Theme.Scale(this, 9);
                using (var ring = new SolidBrush(isSelected || isHovered ? Theme.SurfaceHover : Theme.Surface))
                    g.FillEllipse(ring, bounds.Right - Theme.Scale(this, 20) - 2, bounds.Y + Theme.Scale(this, 10) - 2, dot + 4, dot + 4);
                using (var brush = new SolidBrush(Theme.Live))
                    g.FillEllipse(brush, bounds.Right - Theme.Scale(this, 20), bounds.Y + Theme.Scale(this, 10), dot, dot);
            }

            int pad = Theme.Scale(this, 8);
            var nameRect = new Rectangle(bounds.X + pad, iconRect.Bottom + Theme.Scale(this, 8), bounds.Width - pad * 2, Theme.Scale(this, 34));
            TextRenderer.DrawText(g, game.Name, Theme.BodyStrong, nameRect, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var valueRect = new Rectangle(bounds.X, bounds.Bottom - Theme.Scale(this, 24), bounds.Width, Theme.Scale(this, 18));
            TextRenderer.DrawText(g, $"{game.Vibrance}%", Theme.Small, valueRect, live ? Theme.Live : Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // ------------------------------------------------------------------ Input

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            var game = index >= 0 ? games[index] : null;
            Cursor = game != null ? Cursors.Hand : Cursors.Default;
            if (!ReferenceEquals(game, hovered))
            {
                hovered = game;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != null)
            {
                hovered = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = HitTest(e.Location);
            if (index < 0)
                return;
            Selected = games[index];
            if (e.Button == MouseButtons.Right)
                ContextRequested?.Invoke(games[index], e.Location);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (games.Count == 0)
                return;
            int index = Math.Max(0, games.IndexOf(selected));
            switch (e.KeyCode)
            {
                case Keys.Left: index--; break;
                case Keys.Right: index++; break;
                case Keys.Up: index -= Columns; break;
                case Keys.Down: index += Columns; break;
                case Keys.Delete:
                    if (selected != null)
                        RemoveRequested?.Invoke(selected);
                    e.Handled = true;
                    return;
                default:
                    return;
            }
            Selected = games[Math.Max(0, Math.Min(games.Count - 1, index))];
            e.Handled = true;
        }
    }
}
