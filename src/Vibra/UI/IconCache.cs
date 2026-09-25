using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Vibra.App;
using Vibra.Core;
using Vibra.Platform;

namespace Vibra.UI
{
    /// <summary>
    /// Game icons, kept as PNGs in %APPDATA%\Vibra\icons so they're available even when the game
    /// isn't running. Sources, in order: the game's exe on disk, then its window while it's on screen.
    /// </summary>
    internal sealed class IconCache
    {
        private const int SourceSize = 64;

        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, string> installedPathForExe;

        public IconCache(Func<string, string> installedPathForExe)
        {
            this.installedPathForExe = installedPathForExe;
        }

        /// <summary>An icon was found for a game that had none.</summary>
        public event Action Changed;

        private static string Directory
        {
            get
            {
                string dir = Path.Combine(AppPaths.DataDirectory, "icons");
                System.IO.Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>The game's icon, or null if none is known yet (callers draw a placeholder).</summary>
        public Image Get(GameProfile game)
        {
            string key = KeyFor(game);
            if (key == null)
                return null;
            if (images.TryGetValue(key, out Image image))
                return image;
            if (missing.Contains(key))
                return null;

            image = LoadFromDisk(key) ?? FromFiles(game);
            if (image == null)
            {
                missing.Add(key);
                return null;
            }
            images[key] = image;
            return image;
        }

        public bool Has(GameProfile game) => Get(game) != null;

        /// <summary>Grabs the icon from a game's window if we don't have one yet.</summary>
        public void CaptureFromWindow(GameProfile game, IntPtr hwnd)
        {
            string key = KeyFor(game);
            if (key == null || Get(game) != null)
                return;
            var bitmap = IconExtractor.FromWindow(hwnd);
            if (bitmap == null)
                return;
            Store(key, bitmap);
            Changed?.Invoke();
        }

        /// <summary>Retry games without icons, e.g. after a library scan found their install folders.</summary>
        public void RetryMissing()
        {
            if (missing.Count == 0)
                return;
            missing.Clear();
            Changed?.Invoke();
        }

        private Image FromFiles(GameProfile game)
        {
            string key = KeyFor(game);
            var candidates = new List<string> { game.IconPath };
            candidates.AddRange(game.AllExes.Select(installedPathForExe));
            foreach (string path in candidates.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var bitmap = IconExtractor.FromFile(path, SourceSize);
                if (bitmap != null)
                    return Store(key, bitmap);
            }
            return null;
        }

        private Image Store(string key, Bitmap bitmap)
        {
            Image trimmed = Trim(bitmap);
            images[key] = trimmed;
            missing.Remove(key);
            try
            {
                trimmed.Save(Path.Combine(Directory, key + ".png"), ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Log.Error("Could not cache icon", ex);
            }
            return trimmed;
        }

        private static Image LoadFromDisk(string key)
        {
            string path = Path.Combine(Directory, key + ".png");
            if (!File.Exists(path))
                return null;
            try
            {
                // Load into memory so the file isn't kept locked.
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var loaded = Image.FromStream(stream))
                    return new Bitmap(loaded);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not load cached icon {path}", ex);
                return null;
            }
        }

        /// <summary>Crops transparent margins so small icons don't look lost in their tile.</summary>
        private static Image Trim(Bitmap source)
        {
            int left = source.Width, top = source.Height, right = -1, bottom = -1;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (source.GetPixel(x, y).A <= 8)
                        continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (right < left || bottom < top)
                return source;

            int size = Math.Max(right - left + 1, bottom - top + 1);
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source,
                    new Rectangle((size - (right - left + 1)) / 2, (size - (bottom - top + 1)) / 2, right - left + 1, bottom - top + 1),
                    new Rectangle(left, top, right - left + 1, bottom - top + 1),
                    GraphicsUnit.Pixel);
            }
            source.Dispose();
            return result;
        }

        private static string KeyFor(GameProfile game)
        {
            if (game == null || string.IsNullOrEmpty(game.Exe))
                return null;
            var invalid = Path.GetInvalidFileNameChars();
            return new string(game.Exe.ToLowerInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        /// <summary>Colored rounded square with the game's initials, used until a real icon is known.</summary>
        public static void DrawPlaceholder(Graphics g, RectangleF bounds, string name)
        {
            string initials = Initials(name);
            int hue = Math.Abs((name ?? string.Empty).Aggregate(17, (h, c) => h * 31 + c)) % 360;
            using (var path = Theme.RoundedRect(bounds, bounds.Width * 0.22f))
            using (var brush = new LinearGradientBrush(bounds, FromHue(hue, 0.55, 0.62), FromHue((hue + 40) % 360, 0.65, 0.45), LinearGradientMode.ForwardDiagonal))
                g.FillPath(brush, path);
            using (var font = new Font("Segoe UI Semibold", Math.Max(6f, bounds.Height * 0.3f), GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var brush = new SolidBrush(Color.White))
                g.DrawString(initials, font, brush, bounds, format);
        }

        private static string Initials(string name)
        {
            var words = (name ?? "?").Split(new[] { ' ', '-', '_', ':', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0]))
                .ToList();
            if (words.Count == 0)
                return "?";
            if (words.Count == 1)
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        private static Color FromHue(int hue, double saturation, double lightness)
        {
            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
            double m = lightness - c / 2;
            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = c; g = x; }
            else if (hue < 120) { r = x; g = c; }
            else if (hue < 180) { g = c; b = x; }
            else if (hue < 240) { g = x; b = c; }
            else if (hue < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
