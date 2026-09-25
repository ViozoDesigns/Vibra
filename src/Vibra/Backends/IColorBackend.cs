namespace Vibra.Backends
{
    /// <summary>
    /// A way of changing saturation for a whole monitor in the GPU's display hardware.
    /// Everything is addressed by GDI device name ("\\.\DISPLAY1").
    /// </summary>
    internal interface IColorBackend
    {
        /// <summary>True when the driver was found and initialised.</summary>
        bool IsAvailable { get; }

        /// <summary>Human-readable state, e.g. why the backend is unavailable.</summary>
        string Status { get; }

        /// <summary>Re-enumerate outputs. Call after display configuration changes.</summary>
        void Refresh();

        bool Supports(string gdiDeviceName);

        /// <summary>Sets vibrance in percent (50-100). Returns false if the driver refused.</summary>
        bool TrySetVibrance(string gdiDeviceName, int percent);

        /// <summary>Reads the vibrance currently active in the driver, in percent.</summary>
        int? TryGetVibrance(string gdiDeviceName);
    }
}
