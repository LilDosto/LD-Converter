using System.Text;
using Donustur.Core.Catalog;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using ImageMagick;
using ImageMagick.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Donustur.Engine.Documents;

public sealed class PdfEngine
{
    /// <summary>
    /// Çoklu PDF belgelerini (veya PDF + Görsel karışımını) tek bir PDF dosyasında birleştirir.
    /// </summary>
    public async Task<string> MergePdfsAsync(
        IReadOnlyList<string> inputFiles,
        ExportTransaction transaction,
        string? outputName = null,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string name = string.IsNullOrWhiteSpace(outputName) ? "Birlestirilmis_Belge.pdf" : outputName;
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name += ".pdf";
        }
        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, name);

        await Task.Run(() =>
        {
            using var outputDoc = new PdfSharpCore.Pdf.PdfDocument();

            for (int i = 0; i < inputFiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string input = inputFiles[i];

                if (FormatCatalog.IsPdf(input))
                {
                    using var inputDoc = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                    for (int pageIndex = 0; pageIndex < inputDoc.PageCount; pageIndex++)
                    {
                        outputDoc.AddPage(inputDoc.Pages[pageIndex]);
                    }
                }
                else if (FormatCatalog.IsImage(input))
                {
                    string tempImgPdf = Path.Combine(Path.GetTempPath(), $"donustur_img_{Guid.NewGuid():N}.pdf");
                    try
                    {
                        using (var img = new MagickImage(input))
                        {
                            img.AutoOrient();
                            img.Write(tempImgPdf, MagickFormat.Pdf);
                        }
                        using var imgPdfDoc = PdfReader.Open(tempImgPdf, PdfDocumentOpenMode.Import);
                        for (int pageIndex = 0; pageIndex < imgPdfDoc.PageCount; pageIndex++)
                        {
                            outputDoc.AddPage(imgPdfDoc.Pages[pageIndex]);
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempImgPdf))
                        {
                            try { File.Delete(tempImgPdf); } catch { }
                        }
                    }
                }

                progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
            }

            if (outputDoc.PageCount == 0)
            {
                throw new InvalidOperationException("Birleştirilecek geçerli bir PDF veya görsel sayfası bulunamadı.");
            }

            outputDoc.Save(outputPath);
        }, token);

        return outputPath;
    }

    /// <summary>
    /// Bir PDF belgesinin sayfalarını ayrı PDF dosyalarına böler.
    /// </summary>
    public async Task<IReadOnlyList<string>> SplitPdfAsync(
        string inputPdf,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        var outputPaths = new List<string>();
        string baseName = Path.GetFileNameWithoutExtension(inputPdf);

        await Task.Run(() =>
        {
            using var inputDoc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Import);
            int total = inputDoc.PageCount;

            for (int i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();

                using var singleDoc = new PdfSharpCore.Pdf.PdfDocument();
                singleDoc.AddPage(inputDoc.Pages[i]);

                string pageName = $"{baseName}_Sayfa_{i + 1:D2}.pdf";
                string pagePath = SafePaths.GetUniquePath(transaction.StagingDirectory, pageName);
                singleDoc.Save(pagePath);
                outputPaths.Add(pagePath);

                progress?.Report((double)(i + 1) / total * 100.0);
            }
        }, token);

        return outputPaths;
    }

    /// <summary>
    /// PDF belgesindeki tüm metinleri çıkarıp .txt dosyası olarak kaydeder.
    /// </summary>
    public async Task<string> ExtractTextAsync(
        string inputPdf,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string outName = Path.GetFileNameWithoutExtension(inputPdf) + ".txt";
        string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, outName);

        await Task.Run(() =>
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(inputPdf);
            var sb = new StringBuilder();
            int totalPages = document.NumberOfPages;

            for (int i = 1; i <= totalPages; i++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                string pageText = ExtractPageTextFormatted(page);

                sb.AppendLine($"--- Sayfa {i} ---");
                sb.AppendLine(pageText);
                sb.AppendLine();

                progress?.Report((double)i / totalPages * 100.0);
            }

            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        }, token);

        return outPath;
    }

    /// <summary>
    /// PDF belgesini Word (.docx) belgesine dönüştürür.
    /// Matematik denklemleri, karmaşık formüller, kesirler ve sembolleri bozmadan
    /// yüksek sadakatle koruyan akıllı formül motorunu öncelikli olarak kullanır.
    /// </summary>
    public async Task<string> ConvertToDocxAsync(
        string inputPdf,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string outName = Path.GetFileNameWithoutExtension(inputPdf) + ".docx";
        string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, outName);

        // 1. Akıllı Formül & Matematik Koruyucu Motor
        string? smartResult = await PdfMathEngine.TryConvertAsync(inputPdf, outPath, progress, token);
        if (smartResult != null && File.Exists(smartResult))
        {
            return smartResult;
        }

        // 2. Yedek yerel motor (PdfPig)
        await Task.Run(() =>
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(inputPdf);
            int totalPages = document.NumberOfPages;
            var pages = new List<string>(totalPages);

            for (int i = 1; i <= totalPages; i++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                pages.Add(ExtractPageTextFormatted(page));
                progress?.Report((double)i / totalPages * 90.0);
            }

            DocxHelper.CreateDocxFromPages(outPath, pages);
            progress?.Report(100.0);
        }, token);

        return outPath;
    }

    /// <summary>
    /// PDF içindeki görselleri veya sayfaları hedef görsel formatında (png/jpg) dışa aktarır.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractImagesAsync(
        string inputPdf,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default,
        string targetFormat = "png")
    {
        var outputs = new List<string>();
        string baseName = Path.GetFileNameWithoutExtension(inputPdf);
        bool isJpg = targetFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                     targetFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ||
                     targetFormat.Equals("pdf_to_jpg", StringComparison.OrdinalIgnoreCase);

        string ext = isJpg ? ".jpg" : ".png";
        MagickFormat magickFormat = isJpg ? MagickFormat.Jpg : MagickFormat.Png;

        await Task.Run(() =>
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(inputPdf);
            int totalPages = document.NumberOfPages;
            int imageIndex = 1;

            for (int i = 1; i <= totalPages; i++)
            {
                token.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                var images = page.GetImages().ToList();
                int extractedOnPage = 0;

                foreach (var pdfImg in images)
                {
                    try
                    {
                        MagickImage? mImg = null;
                        if (pdfImg.TryGetPng(out byte[]? pngBytes) && pngBytes != null && pngBytes.Length > 0)
                        {
                            mImg = new MagickImage(pngBytes);
                        }
                        else
                        {
                            byte[] rawBytes = pdfImg.RawBytes.ToArray();
                            if (rawBytes.Length > 0)
                            {
                                mImg = new MagickImage(rawBytes);
                            }
                        }

                        if (mImg != null)
                        {
                            using (mImg)
                            {
                                if (isJpg)
                                {
                                    if (mImg.HasAlpha)
                                    {
                                        mImg.ColorAlpha(MagickColors.White);
                                    }
                                    mImg.Quality = 90;
                                }

                                string imgName = $"{baseName}_Sayfa{i:D2}_Gorsel{imageIndex:D2}{ext}";
                                string imgPath = SafePaths.GetUniquePath(transaction.StagingDirectory, imgName);
                                mImg.Write(imgPath, magickFormat);
                                outputs.Add(imgPath);
                                imageIndex++;
                                extractedOnPage++;
                            }
                        }
                    }
                    catch
                    {
                        // Desteklenmeyen ham görsel akışını atla
                    }
                }

                // Eğer sayfada gömülü bitmap yoksa, sayfa metnini yüksek çözünürlüklü görsel sayfasına işle
                if (extractedOnPage == 0)
                {
                    string pageText = ExtractPageTextFormatted(page);
                    string pageImgName = $"{baseName}_Sayfa_{i:D2}{ext}";
                    string pageImgPath = SafePaths.GetUniquePath(transaction.StagingDirectory, pageImgName);
                    RenderTextPageToImage(pageText, i, totalPages, pageImgPath, magickFormat);
                    outputs.Add(pageImgPath);
                }

                progress?.Report((double)i / totalPages * 100.0);
            }
        }, token);

        return outputs;
    }

    private static string ExtractPageTextFormatted(UglyToad.PdfPig.Content.Page page)
    {
        try
        {
            string ordered = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(ordered))
            {
                return ordered;
            }
        }
        catch
        {
            // Yedek olarak standart page.Text kullan
        }
        return page.Text ?? string.Empty;
    }

    private static void RenderTextPageToImage(string pageText, int pageNumber, int totalPages, string outputPath, MagickFormat format)
    {
        using var canvas = new MagickImage(MagickColors.White, 1240, 1754);
        var drawables = new Drawables()
            .Font("Arial")
            .FontPointSize(18)
            .FillColor(MagickColors.Black);

        string[] rawLines = (pageText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        double y = 80;
        const int maxCharsPerLine = 95;

        foreach (var rawLine in rawLines)
        {
            if (y > 1660) break;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                y += 24;
                continue;
            }

            string remaining = rawLine;
            while (remaining.Length > 0 && y <= 1660)
            {
                string chunk = remaining.Length > maxCharsPerLine
                    ? remaining[..maxCharsPerLine]
                    : remaining;
                remaining = remaining.Length > maxCharsPerLine
                    ? remaining[maxCharsPerLine..]
                    : string.Empty;

                drawables.Text(70, y, chunk);
                y += 26;
            }
        }

        // Sayfa alt bilgisi
        drawables
            .FontPointSize(14)
            .FillColor(new MagickColor("#6B7280"))
            .Text(560, 1710, $"Sayfa {pageNumber} / {totalPages}");

        canvas.Draw(drawables);
        if (format == MagickFormat.Jpg)
        {
            canvas.Quality = 90;
        }
        canvas.Write(outputPath, format);
    }
}
