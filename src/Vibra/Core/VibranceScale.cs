using System;

namespace Vibra.Core
{
    /// <summary>
    /// Converts between the percentage shown to users and the driver's raw level.
    /// Uses the same scale as NVIDIA Control Panel: 50% is the neutral default and 100% is the
    /// strongest boost.
    /// </summary>
    internal static class VibranceScale
    {
        public const int MinPercent = 50;
        public const int MaxPercent = 100;
        public const int DefaultGamePercent = 80;

        public static int Clamp(int percent) => Math.Max(MinPercent, Math.Min(MaxPercent, percent));

        public static int ToLevel(int percent, int minLevel, int maxLevel)
        {
            if (maxLevel <= minLevel)
                return minLevel;

            double fraction = (Clamp(percent) - MinPercent) / (double)(MaxPercent - MinPercent);
            return minLevel + (int)Math.Round(fraction * (maxLevel - minLevel), MidpointRounding.AwayFromZero);
        }

        public static int ToPercent(int level, int minLevel, int maxLevel)
        {
            if (maxLevel <= minLevel)
                return MinPercent;

            level = Math.Max(minLevel, Math.Min(maxLevel, level));
            double fraction = (level - minLevel) / (double)(maxLevel - minLevel);
            return MinPercent + (int)Math.Round(fraction * (MaxPercent - MinPercent), MidpointRounding.AwayFromZero);
        }
    }
}
