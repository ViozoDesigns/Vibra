using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Vibra.Interop;

namespace Vibra.UI
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(15, 16, 20);
        public static readonly Color Surface = Color.FromArgb(25, 27, 33);
        public static readonly Color SurfaceHover = Color.FromArgb(35, 38, 46);
        public static readonly Color Border = Color.FromArgb(46, 49, 58);
        public static readonly Color Text = Color.FromArgb(236, 238, 242);
        public static readonly Color SubText = Color.FromArgb(140, 146, 160);
        public static readonly Color Track = Color.FromArgb(46, 50, 60);
        public static readonly Color Live = Color.FromArgb(52, 211, 120);
        public static readonly Color Warning = Color.FromArgb(255, 180, 60);

        public static readonly Color GradientStart = Color.FromArgb(0, 190, 255);
        public static readonly Color GradientMid = Color.FromArgb(140, 90, 255);
        public static readonly Color GradientEnd = Color.FromArgb(255, 60, 150);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyStrong = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 8.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 16f);
        public static readonly Font Value = new Font("Segoe UI Semibold", 10.5f);

        public static int Scale(Control control, int pixels) => (int)Math.Round(pixels * control.DeviceDpi / 96.0);

        public static ColorBlend VibranceBlend() => new ColorBlend
        {
            Colors = new[] { GradientStart, GradientMid, GradientEnd },
            Positions = new[] { 0f, 0.5f, 1f },
        };

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref on, sizeof(int));
        }

        public static void UseDarkScrollbars(Control control)
        {
            try
            {
                NativeMethods.SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows: keep the default look.
            }
        }
    }

    internal static class AppIcon
    {
        private static Icon large;
        private static Icon small;

        public static Icon Large => large ?? (large = Load(SystemInformation.IconSize));
        public static Icon Small => small ?? (small = Load(SystemInformation.SmallIconSize));

        private static Icon Load(Size size)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vibra.ico"))
            {
                if (stream != null)
                    return new Icon(stream, size);
            }
            return SystemIcons.Application;
        }
    }

    /// <summary>Dark styling for context menus (tray menu, "Add game" menu).</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool isShortcut = e.Item is ToolStripMenuItem item && !string.IsNullOrEmpty(item.ShortcutKeyDisplayString) && e.Text == item.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled || isShortcut ? Theme.SubText : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using (var pen = new Pen(Theme.GradientMid, Math.Max(2f, r.Height / 8f)))
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
                });
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.SurfaceHover;
            public override Color MenuItemSelected => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
            public override Color MenuItemPressedGradientBegin => Theme.SurfaceHover;
            public override Color MenuItemPressedGradientEnd => Theme.SurfaceHover;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
            public override Color CheckBackground => Theme.Surface;
            public override Color CheckSelectedBackground => Theme.SurfaceHover;
            public override Color CheckPressedBackground => Theme.SurfaceHover;
        }
    }
}
