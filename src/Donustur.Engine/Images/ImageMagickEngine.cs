using ImageMagick;
using Donustur.Core.Models;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;

namespace Donustur.Engine.Images;

public sealed class ImageMagickEngine
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "webp", "avif", "bmp", "tiff", "tif", "gif", "ico", "svg", "heic", "heif"
    };

    public bool CanHandle(string extension)
    {
        return SupportedImageExtensions.Contains(extension.TrimStart('.'));
    }

    /// <summary>
    /// Tek veya çoklu görselleri hedef formata dönüştürür.
    /// </summary>
    public async Task<IReadOnlyList<string>> ConvertImagesAsync(
        IReadOnlyList<string> inputFiles,
        string targetFormat,
        ExportTransaction transaction,
        ConversionOptions options,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        var outputPaths = new List<string>();
        string formatKey = targetFormat.ToLowerInvariant().TrimStart('.');

        // Çoklu görsellerden tek PDF oluşturma
        if (formatKey == "pdf")
        {
            string pdfName = inputFiles.Count == 1
                ? Path.GetFileNameWithoutExtension(inputFiles[0]) + ".pdf"
                : "Birlestirilmis_Gorseller.pdf";

            string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, pdfName);

            await Task.Run(() =>
            {
                using var collection = new MagickImageCollection();
                for (int i = 0; i < inputFiles.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var image = new MagickImage(inputFiles[i]);
                    image.AutoOrient();
                    collection.Add(image);
                    progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
                }
                collection.Write(outPath, MagickFormat.Pdf);
            }, token);

            outputPaths.Add(outPath);
            return outputPaths;
        }

        // Çoklu görsellerden animasyonlu GIF oluşturma
        if (formatKey == "gif" && inputFiles.Count > 1)
        {
            string gifName = "Animasyon.gif";
            string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, gifName);

            await Task.Run(() =>
            {
                using var collection = new MagickImageCollection();
                for (int i = 0; i < inputFiles.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var img = new MagickImage(inputFiles[i]);
                    img.AutoOrient();
                    img.AnimationDelay = 40; // 400ms
                    collection.Add(img);
                    progress?.Report((double)(i + 1) / inputFiles.Count * 90.0);
                }
                collection.Optimize();
                collection.Write(outPath, MagickFormat.Gif);
                progress?.Report(100.0);
            }, token);

            outputPaths.Add(outPath);
            return outputPaths;
        }

        // Birebir görsel dönüşümleri
        MagickFormat magickFormat = MapFormat(formatKey);

        for (int i = 0; i < inputFiles.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            string inputPath = inputFiles[i];
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
            string extensionWithDot = formatKey == "compress_image"
                ? Path.GetExtension(inputPath)
                : "." + formatKey;

            string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, fileNameWithoutExt + extensionWithDot);

            await Task.Run(() =>
            {
                using var image = new MagickImage(inputPath);
                image.AutoOrient();

                if (options.StripMetadata)
                {
                    image.Strip();
                }

                if (options.ResizeWidth.HasValue || options.ResizeHeight.HasValue)
                {
                    var geo = new MagickGeometry((uint)(options.ResizeWidth ?? 0), (uint)(options.ResizeHeight ?? 0))
                    {
                        IgnoreAspectRatio = false
                    };
                    image.Resize(geo);
                }

                if (formatKey == "compress_image")
                {
                    image.Quality = (uint)System.Math.Clamp(options.ImageQuality, 10, 95);
                    image.Strip();
                    image.Write(outPath);
                }
                else if (magickFormat == MagickFormat.Ico)
                {
                    // Oranı koruyarak 256x256 şeffaf tuvale ortala
                    image.Resize(new MagickGeometry(256, 256) { IgnoreAspectRatio = false });
                    image.BackgroundColor = MagickColors.Transparent;
                    image.Extent(256, 256, Gravity.Center);
                    image.Write(outPath, MagickFormat.Ico);
                }
                else
                {
                    if (magickFormat is MagickFormat.Jpg or MagickFormat.Jpeg)
                    {
                        // Şeffaf PNG/WebP görseller JPG'ye çevrilirken siyah arka plan oluşmasını önle
                        if (image.HasAlpha)
                        {
                            image.ColorAlpha(MagickColors.White);
                        }
                    }

                    if (magickFormat is MagickFormat.Jpg or MagickFormat.Jpeg or MagickFormat.WebP or MagickFormat.Avif)
                    {
                        image.Quality = (uint)System.Math.Clamp(options.ImageQuality, 10, 100);
                    }
                    image.Write(outPath, magickFormat);
                }
            }, token);

            outputPaths.Add(outPath);
            progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
        }

        return outputPaths;
    }

    private static MagickFormat MapFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "png" => MagickFormat.Png,
            "jpg" or "jpeg" => MagickFormat.Jpg,
            "webp" => MagickFormat.WebP,
            "avif" => MagickFormat.Avif,
            "bmp" => MagickFormat.Bmp,
            "tiff" or "tif" => MagickFormat.Tiff,
            "ico" => MagickFormat.Ico,
            "gif" => MagickFormat.Gif,
            "pdf" => MagickFormat.Pdf,
            _ => MagickFormat.Png
        };
    }
}
