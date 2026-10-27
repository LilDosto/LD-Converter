namespace Donustur.Core.Models;

/// <summary>
/// Dönüştürme işlemleri için yapılandırma seçenekleri.
/// </summary>
public sealed class ConversionOptions
{
    public int ImageQuality { get; set; } = 85;
    public int? ResizeWidth { get; set; }
    public int? ResizeHeight { get; set; }
    public bool StripMetadata { get; set; } = false;
    public int PdfDpi { get; set; } = 200;
    public string? ArchivePassword { get; set; }
    public string VideoPreset { get; set; } = "faster";
    public int VideoCrf { get; set; } = 23;
    public string AudioBitrate { get; set; } = "192k";
    public string? DestinationFolder { get; set; }
    public bool OverwriteExisting { get; set; } = false;

    public static ConversionOptions Default => new();
}
