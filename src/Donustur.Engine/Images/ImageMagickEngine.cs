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
    /// Çoklu dosya dönüşümlerinde yarış koşulsuz yol ayırma ve CPU çekirdeklerine göre paralel işleme uygular.
    /// </summary>
    public async Task<IReadOnlyList<string>> ConvertImagesAsync(
        IReadOnlyList<string> inputFiles,
        string targetFormat,
        ExportTransaction transaction,
        ConversionOptions options,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        if (inputFiles.Count == 0)
        {
            return Array.Empty<string>();
        }

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

            return new[] { outPath };
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

            return new[] { outPath };
        }

        // Birebir görsel dönüşümleri
        MagickFormat magickFormat = MapFormat(formatKey);
        string[] outputPaths = new string[inputFiles.Count];
        int completedCount = 0;

        // YARIŞ DURUMU (RACE CONDITION) ÖNLEME:
        // Aynı temel ada sahip dosyalar (örn: resim.png ve resim.bmp -> resim.webp) paralel iş parçacıklarında
        // aynı anda SafePaths.GetUniquePath çağırdığında çakışmayı önlemek için yollar önceden belirlenir.
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobItems = new (string inputPath, string outPath, int idx)[inputFiles.Count];

        for (int i = 0; i < inputFiles.Count; i++)
        {
            string inputPath = inputFiles[i];
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
            string extensionWithDot = formatKey == "compress_image"
                ? Path.GetExtension(inputPath)
                : "." + formatKey;

            string candidate = SafePaths.GetUniquePath(transaction.StagingDirectory, fileNameWithoutExt + extensionWithDot);
            int counter = 1;
            while (reservedPaths.Contains(candidate))
            {
                candidate = SafePaths.GetUniquePath(transaction.StagingDirectory, $"{fileNameWithoutExt} ({counter}){extensionWithDot}");
                counter++;
            }
            reservedPaths.Add(candidate);
            jobItems[i] = (inputPath, candidate, i);
        }

        // Paralel işleme yapılandırması: CPU çekirdek sayısına göre dinamik ölçekleme
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8)
        };

        await Parallel.ForEachAsync(jobItems, parallelOptions, (item, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            string inputPath = item.inputPath;
            string outPath = item.outPath;
            int idx = item.idx;

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

            outputPaths[idx] = outPath;
            int currentDone = Interlocked.Increment(ref completedCount);
            progress?.Report((double)currentDone / inputFiles.Count * 100.0);

            return ValueTask.CompletedTask;
        });

        return outputPaths.Where(p => !string.IsNullOrEmpty(p)).ToList();
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
