using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Donustur.UI.Services;

/// <summary>
/// Sistem tepsisi (Tray), görev çubuğu ve tüm uygulama pencereleri için
/// yüksek çözünürlüklü "LD Converter" simgesini üretir.
/// </summary>
public static class AppIconProvider
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon? _cachedTrayIcon;
    private static ImageSource? _cachedWindowIcon;

    public static Icon GetTrayIcon()
    {
        if (_cachedTrayIcon != null) return _cachedTrayIcon;

        using var bmp = RenderLogoBitmap(64);
        IntPtr hIcon = bmp.GetHicon();
        _cachedTrayIcon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return _cachedTrayIcon;
    }

    public static ImageSource GetWindowIcon()
    {
        if (_cachedWindowIcon != null) return _cachedWindowIcon;

        using var bmp = RenderLogoBitmap(128);
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            _cachedWindowIcon = source;
            return source;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Bitmap RenderLogoBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(System.Drawing.Color.Transparent);

        float pad = size * 0.04f;
        var rect = new RectangleF(pad, pad, size - (pad * 2), size - (pad * 2));
        using var path = CreateRoundedRect(rect, size * 0.24f);
        using var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            rect,
            System.Drawing.Color.FromArgb(255, 79, 70, 229), // Deep Indigo
            System.Drawing.Color.FromArgb(255, 6, 182, 212), // Vibrant Cyan
            45f);
        g.FillPath(bgBrush, path);

        // İç parlaklık çerçevesi
        using var borderPen = new System.Drawing.Pen(
            System.Drawing.Color.FromArgb(95, 255, 255, 255),
            Math.Max(1.0f, size * 0.02f));
        g.DrawPath(borderPen, path);

        // Dış dönüşüm yayları
        float ringPad = size * 0.16f;
        var ringRect = new RectangleF(ringPad, ringPad, size - (ringPad * 2), size - (ringPad * 2));
        using var ringPen = new System.Drawing.Pen(
            System.Drawing.Color.FromArgb(235, 255, 255, 255),
            Math.Max(1.5f, size * 0.055f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };

        g.DrawArc(ringPen, ringRect, 195, 140);
        g.DrawArc(ringPen, ringRect, 15, 140);

        // Merkez "LD" monogramı
        float fontSize = size * 0.30f;
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var textRect = new RectangleF(0, size * 0.01f, size, size);
        g.DrawString("LD", font, System.Drawing.Brushes.White, textRect, sf);

        return bmp;
    }

    private static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
