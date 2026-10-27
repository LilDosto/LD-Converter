using Donustur.Core.Catalog;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using ImageMagick;
using UglyToad.PdfPig;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Donustur.Engine.Qr;

public sealed class QrEngine
{
    /// <summary>
    /// Görsel veya PDF dosyasından QR kod(ları) okur ve metnini döner.
    /// </summary>
    public string? ReadQrCode(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        if (FormatCatalog.IsPdf(filePath))
        {
            return ReadQrFromPdf(filePath);
        }

        try
        {
            using var image = new MagickImage(filePath);
            return DecodeFromMagickImage(image);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadQrFromPdf(string pdfPath)
    {
        try
        {
            var foundTexts = new List<string>();
            using var document = PdfDocument.Open(pdfPath);

            for (int i = 1; i <= document.NumberOfPages; i++)
            {
                var page = document.GetPage(i);
                foreach (var pdfImg in page.GetImages())
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
                            byte[] raw = pdfImg.RawBytes.ToArray();
                            if (raw.Length > 0)
                            {
                                mImg = new MagickImage(raw);
                            }
                        }

                        if (mImg != null)
                        {
                            using (mImg)
                            {
                                string? decoded = DecodeFromMagickImage(mImg);
                                if (!string.IsNullOrWhiteSpace(decoded) && !foundTexts.Contains(decoded))
                                {
                                    foundTexts.Add(decoded);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Okunamayan alt görselleri atla
                    }
                }
            }

            return foundTexts.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, foundTexts) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeFromMagickImage(MagickImage image)
    {
        image.AutoOrient();

        // Şeffaf arka planlı QR kodların doğru okunması için beyaz zemine oturt
        if (image.HasAlpha)
        {
            image.ColorAlpha(MagickColors.White);
        }

        using var pixels = image.GetPixels();
        byte[]? rgb = pixels.ToByteArray(PixelMapping.RGB);
        if (rgb == null) return null;

        var luminanceSource = new RGBLuminanceSource(
            rgb,
            (int)image.Width,
            (int)image.Height,
            RGBLuminanceSource.BitmapFormat.RGB24);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.CODE_128, BarcodeFormat.EAN_13 }
            }
        };

        var result = reader.Decode(luminanceSource);
        return result?.Text;
    }

    /// <summary>
    /// Verilen metinden yüksek çözünürlüklü bir QR kod PNG görseli üretir.
    /// </summary>
    public string GenerateQrCode(string text, ExportTransaction transaction, int size = 600)
    {
        string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, "Karekod_" + Guid.NewGuid().ToString("N")[..6] + ".png");

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = size,
                Height = size,
                Margin = 4,
                CharacterSet = "UTF-8"
            }
        };

        var pixelData = writer.Write(text);

        // PixelDataRenderer varsayılan olarak BGRA32 üretir (B, G, R, A)
        var readSettings = new MagickReadSettings
        {
            Width = (uint)pixelData.Width,
            Height = (uint)pixelData.Height,
            Format = MagickFormat.Bgra
        };

        using var image = new MagickImage(pixelData.Pixels, readSettings);
        image.Write(outPath, MagickFormat.Png);

        return outPath;
    }
}
