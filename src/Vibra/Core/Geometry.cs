using System;

namespace Vibra.Core
{
    /// <summary>An axis-aligned rectangle in physical screen pixels (right/bottom exclusive).</summary>
    internal readonly struct ScreenRect : IEquatable<ScreenRect>
    {
        public ScreenRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        /// <summary>Area in pixels; zero for empty or inverted rectangles.</summary>
        public long Area => Width <= 0 || Height <= 0 ? 0 : (long)Width * Height;

        public ScreenRect Intersect(ScreenRect other) => new ScreenRect(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));

        public bool Equals(ScreenRect other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object obj) => obj is ScreenRect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Left;
                hash = (hash * 397) ^ Top;
                hash = (hash * 397) ^ Right;
                hash = (hash * 397) ^ Bottom;
                return hash;
            }
        }

        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }
}
