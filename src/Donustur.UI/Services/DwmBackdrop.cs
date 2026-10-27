using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Donustur.UI.Services;

public static class DwmBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public enum BackdropType
    {
        Auto = 0,
        None = 1,
        MainWindow = 2, // Mica
        TransientWindow = 3, // Acrylic
        TabbedWindow = 4 // Mica Alt
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyFluentBackdrop(Window window, BackdropType backdrop = BackdropType.TransientWindow, bool darkMode = true)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) =>
            {
                IntPtr h = new WindowInteropHelper(window).Handle;
                ApplyInternal(h, backdrop, darkMode);
            };
            return;
        }

        ApplyInternal(hwnd, backdrop, darkMode);
    }

    private static void ApplyInternal(IntPtr hwnd, BackdropType backdrop, bool darkMode)
    {
        try
        {
            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            int backdropValue = (int)backdrop;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropValue, sizeof(int));
        }
        catch
        {
            // Windows 10 veya eski sürümlerde sessizce devam et
        }
    }
}
