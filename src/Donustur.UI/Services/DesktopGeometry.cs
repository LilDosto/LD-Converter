using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Donustur.UI.Services;

public static class DesktopGeometry
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    public static double GetDpiScaleAtPoint(int screenX, int screenY)
    {
        try
        {
            IntPtr hMonitor = MonitorFromPoint(new POINT { X = screenX, Y = screenY }, MONITOR_DEFAULTTONEAREST);
            if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Eski sistemlerde varsayılan ölçeğe dön
        }
        return 1.0;
    }

    /// <summary>
    /// Ekran piksel koordinatını (Physical Pixels) pencerenin DPI ölçeğine uygun
    /// WPF Device Independent Pixel (DIP) koordinatına çevirir.
    /// </summary>
    public static Point ScreenPixelsToDip(Visual visual, int screenX, int screenY)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            var matrix = source.CompositionTarget.TransformFromDevice;
            return matrix.Transform(new Point(screenX, screenY));
        }

        double scale = GetDpiScaleAtPoint(screenX, screenY);
        return new Point(screenX / scale, screenY / scale);
    }

    /// <summary>
    /// Çarkı farenin koordinatına ortalar ve çoklu monitör sınırlarının dışına taşmasını önler.
    /// </summary>
    public static void PlaceCentered(Window window, int screenPixelX, int screenPixelY)
    {
        double dpiScale = GetDpiScaleAtPoint(screenPixelX, screenPixelY);
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null && source.CompositionTarget.TransformToDevice.M11 > 0)
        {
            dpiScale = source.CompositionTarget.TransformToDevice.M11;
        }

        double dipX = screenPixelX / (dpiScale > 0 ? dpiScale : 1.0);
        double dipY = screenPixelY / (dpiScale > 0 ? dpiScale : 1.0);

        double targetLeft = dipX - (window.Width / 2.0);
        double targetTop = dipY - (window.Height / 2.0);

        // Çoklu monitör sanal ekran sınırlarını kontrol et (negatif koordinatları destekler)
        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxRight = minLeft + SystemParameters.VirtualScreenWidth;
        double maxBottom = minTop + SystemParameters.VirtualScreenHeight;

        if (targetLeft < minLeft) targetLeft = minLeft;
        if (targetTop < minTop) targetTop = minTop;
        if (targetLeft + window.Width > maxRight) targetLeft = maxRight - window.Width;
        if (targetTop + window.Height > maxBottom) targetTop = maxBottom - window.Height;

        window.Left = targetLeft;
        window.Top = targetTop;
    }

    /// <summary>
    /// Dosya veya klasörü Windows Gezgini'nde güvenli şekilde seçerek açar.
    /// </summary>
    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Gezgin başlatılamazsa sessizce geç
        }
    }
}
