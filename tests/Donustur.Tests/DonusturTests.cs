using Donustur.Core.Catalog;
using Donustur.Core.Math;
using Donustur.Core.Models;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using Donustur.Engine;
using Donustur.Engine.Archives;
using Donustur.Engine.Documents;
using Donustur.Engine.Images;
using Donustur.Engine.Qr;
using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Donustur.Tests;

public class WheelMathTests
{
    [Fact]
    public void DeadZone_ReturnsNegativeOne()
    {
        int segment = WheelMath.GetSegmentIndex(200, 200, 200, 200, 6);
        Assert.Equal(-1, segment);

        int farSegment = WheelMath.GetSegmentIndex(200, 200, 200, 400, 6);
        Assert.Equal(-1, farSegment);
    }

    [Fact]
    public void ValidAngle_ReturnsValidSegmentIndex()
    {
        int segmentRight = WheelMath.GetSegmentIndex(200, 200, 280, 200, 8);
        Assert.InRange(segmentRight, 0, 7);

        int segmentDown = WheelMath.GetSegmentIndex(200, 200, 200, 280, 8);
        Assert.InRange(segmentDown, 0, 7);
    }

    [Fact]
    public void PointOnCircle_CalculatesCorrectly()
    {
        var pt = WheelMath.GetPointOnCircle(100, 100, 50, 0);
        Assert.Equal(150, pt.X, 1);
        Assert.Equal(100, pt.Y, 1);
    }
}

public class FormatCatalogTests
{
    [Fact]
    public void DetectsImageType()
    {
        Assert.True(FormatCatalog.IsImage("photo.PNG"));
        Assert.True(FormatCatalog.IsImage("image.webp"));
        Assert.True(FormatCatalog.IsImage("graphic.avif"));
    }

    [Fact]
    public void DetectsPdfType()
    {
        Assert.True(FormatCatalog.IsPdf("document.PDF"));
    }

    [Fact]
    public void DetectsDocumentType()
    {
        Assert.True(FormatCatalog.IsDocument("report.docx"));
        Assert.True(FormatCatalog.IsDocument("notes.txt"));
    }

    [Fact]
    public void DetectsVideoType()
    {
        Assert.True(FormatCatalog.IsVideo("movie.mp4"));
        Assert.True(FormatCatalog.IsVideo("clip.mkv"));
    }

    [Fact]
    public void GetChoicesForSingleImage_ReturnsExpectedChoices()
    {
        var choices = FormatCatalog.GetChoicesForFiles(new[] { "test.png" });
        Assert.NotEmpty(choices);
        Assert.Contains(choices, c => c.Id == "webp");
        Assert.Contains(choices, c => c.Id == "jpg");
        Assert.Contains(choices, c => c.Id == "pdf");
    }

    [Fact]
    public void GetChoicesForMultipleImages_IncludesAnimatedGif()
    {
        var choices = FormatCatalog.GetChoicesForFiles(new[] { "1.png", "2.png" });
        Assert.Contains(choices, c => c.Id == "gif");
    }

    [Fact]
    public void GetChoicesForSinglePdf_ReturnsExpectedChoices()
    {
        var choices = FormatCatalog.GetChoicesForFiles(new[] { "sample.pdf" });
        Assert.NotEmpty(choices);
        Assert.Contains(choices, c => c.Id == "pdf_to_images");
        Assert.Contains(choices, c => c.Id == "pdf_to_txt");
        Assert.Contains(choices, c => c.Id == "pdf_to_docx");
    }

    [Fact]
    public void GetChoicesForMultiplePdfs_IncludesMerge()
    {
        var choices = FormatCatalog.GetChoicesForFiles(new[] { "1.pdf", "2.pdf" });
        Assert.Contains(choices, c => c.Id == "pdf_merge");
    }
}

public class SafePathsTests
{
    [Fact]
    public void GeneratesUniquePathWhenConflictExists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string file1 = Path.Combine(tempDir, "output.txt");
            File.WriteAllText(file1, "hello");

            string unique = SafePaths.GetUniquePath(tempDir, "output.txt");
            Assert.NotEqual(file1, unique);
            Assert.Contains("output (1).txt", unique);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class ExportTransactionTests
{
    [Fact]
    public void CommitTo_MovesStagedFilesToDestination()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "DonusturTxBase_" + Guid.NewGuid());
        string destDir = Path.Combine(Path.GetTempPath(), "DonusturTxDest_" + Guid.NewGuid());
        Directory.CreateDirectory(destDir);
        try
        {
            using (var tx = new ExportTransaction(baseDir))
            {
                string stagedFile = Path.Combine(tx.StagingDirectory, "result.txt");
                File.WriteAllText(stagedFile, "done");

                var committed = tx.CommitTo(destDir);
                Assert.Single(committed);
                Assert.True(File.Exists(committed[0]));
                Assert.True(tx.IsCommitted);
            }
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public void CommitTo_PreservesDirectoryHierarchy()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "DonusturTxDirBase_" + Guid.NewGuid());
        string destDir = Path.Combine(Path.GetTempPath(), "DonusturTxDirDest_" + Guid.NewGuid());
        Directory.CreateDirectory(destDir);
        try
        {
            using (var tx = new ExportTransaction(baseDir))
            {
                string nestedDir = Path.Combine(tx.StagingDirectory, "Pack_Cikarilan", "SubFolder");
                Directory.CreateDirectory(nestedDir);
                File.WriteAllText(Path.Combine(nestedDir, "inner.txt"), "nested content");

                var committed = tx.CommitTo(destDir);
                Assert.Single(committed);
                Assert.True(Directory.Exists(committed[0]));
                Assert.True(File.Exists(Path.Combine(committed[0], "SubFolder", "inner.txt")));
            }
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public void DisposeWithoutCommit_CleansUpStaging()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "DonusturTxRollback_" + Guid.NewGuid());
        string stagingDir;
        using (var tx = new ExportTransaction(baseDir))
        {
            stagingDir = tx.StagingDirectory;
            string stagedFile = Path.Combine(tx.StagingDirectory, "temporary.txt");
            File.WriteAllText(stagedFile, "discard me");
        }

        Assert.False(Directory.Exists(stagingDir));
    }
}

public class ImageEngineTests
{
    [Fact]
    public async Task ConvertsPngToWebpAndHandlesSameBaseNames()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturImgTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPng = Path.Combine(tempDir, "sample.png");
            string inputBmp = Path.Combine(tempDir, "sample.bmp");
            using (var image = new MagickImage(MagickColors.Crimson, 100, 100))
            {
                image.Write(inputPng);
                image.Write(inputBmp);
            }

            var engine = new ImageMagickEngine();
            using var tx = new ExportTransaction(tempDir);
            var options = new ConversionOptions { ImageQuality = 85 };

            var outputs = await engine.ConvertImagesAsync(
                new[] { inputPng, inputBmp },
                "webp",
                tx,
                options,
                null,
                CancellationToken.None);

            Assert.Equal(2, outputs.Count);
            Assert.NotEqual(outputs[0], outputs[1]);
            Assert.All(outputs, p => Assert.True(File.Exists(p) && new FileInfo(p).Length > 0));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class PdfAndDocxEngineTests
{
    private static string CreateSamplePdf(string dir, string fileName, string content)
    {
        string path = Path.Combine(dir, fileName);
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 14, XFontStyle.Regular);
        gfx.DrawString(content, font, XBrushes.Black, new XPoint(50, 100));
        doc.Save(path);
        return path;
    }

    [Fact]
    public async Task PdfToDocx_And_PdfToJpg_WorkThroughDispatcher()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturPdfDocxTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string pdfPath = CreateSamplePdf(tempDir, "rapor.pdf", "Merhaba Dunya PDF Test Icerigi");
            var prefs = new AppPreferences
            {
                OutputDirectoryMode = "Custom",
                CustomOutputDirectory = tempDir
            };
            var dispatcher = new ConversionDispatcher(prefs);

            // 1. pdf_to_docx
            var docxTask = new ConversionTask
            {
                InputFiles = new[] { pdfPath },
                TargetActionId = "pdf_to_docx",
                TargetDisplayName = "DOCX Yap"
            };
            var docxRes = await dispatcher.ExecuteTaskAsync(docxTask, prefs);
            Assert.True(docxRes.Success, docxRes.ErrorMessage);
            Assert.Single(docxRes.OutputFiles);
            Assert.EndsWith(".docx", docxRes.OutputFiles[0], StringComparison.OrdinalIgnoreCase);

            string extractedFromDocx = DocxHelper.ExtractTextFromDocx(docxRes.OutputFiles[0]);
            Assert.Contains("Merhaba", extractedFromDocx);

            // 2. pdf_to_jpg
            var jpgTask = new ConversionTask
            {
                InputFiles = new[] { pdfPath },
                TargetActionId = "pdf_to_jpg",
                TargetDisplayName = "Sayfaları JPG Yap"
            };
            var jpgRes = await dispatcher.ExecuteTaskAsync(jpgTask, prefs);
            Assert.True(jpgRes.Success, jpgRes.ErrorMessage);
            Assert.NotEmpty(jpgRes.OutputFiles);
            Assert.EndsWith(".jpg", jpgRes.OutputFiles[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task MergePdfAndImage_CreatesCombinedPdf()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturMergeMixTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string pdfPath = CreateSamplePdf(tempDir, "sayfa1.pdf", "Birinci Sayfa");
            string imgPath = Path.Combine(tempDir, "sayfa2.png");
            using (var img = new MagickImage(MagickColors.CornflowerBlue, 200, 200))
            {
                img.Write(imgPath);
            }

            var pdfEngine = new PdfEngine();
            using var tx = new ExportTransaction(tempDir);
            string merged = await pdfEngine.MergePdfsAsync(new[] { pdfPath, imgPath }, tx);

            Assert.True(File.Exists(merged));
            using var doc = UglyToad.PdfPig.PdfDocument.Open(merged);
            Assert.Equal(2, doc.NumberOfPages);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class ArchiveEngineTests
{
    [Fact]
    public async Task CreateAndExtractTarGz_PreservesFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturArchiveTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            string f1 = Path.Combine(tempDir, "a.txt");
            File.WriteAllText(f1, "icerik A");

            var engine = new ArchiveEngine();
            using var tx = new ExportTransaction(tempDir);

            string tarGz = await engine.CreateTarGzAsync(new[] { f1 }, tx);
            Assert.True(File.Exists(tarGz));
            Assert.EndsWith(".tar.gz", tarGz, StringComparison.OrdinalIgnoreCase);

            string extractedDir = await engine.ExtractAsync(tarGz, tx);
            Assert.True(Directory.Exists(extractedDir));
            Assert.True(File.Exists(Path.Combine(extractedDir, "a.txt")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class QrEngineTests
{
    [Fact]
    public void GenerateAndReadQrCode()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DonusturQrTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var qrEngine = new QrEngine();
            using var tx = new ExportTransaction(tempDir);
            string payload = "https://github.com/donustur";

            string qrFile = qrEngine.GenerateQrCode(payload, tx, 300);
            Assert.True(File.Exists(qrFile));

            string? decoded = qrEngine.ReadQrCode(qrFile);
            Assert.NotNull(decoded);
            Assert.Equal(payload, decoded);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}


public class PandocEngineTests
{
    [Fact]
    public void PandocEngine_ResolvesCustomPathWhenProvided()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var engine = new PandocEngine(tempFile);
            Assert.Equal(tempFile, engine.ResolvePandocPath());
            Assert.True(engine.IsAvailable);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public class FfmpegResolutionTests
{
    [Fact]
    public void WingetOrSystemFfmpeg_CanBeResolved()
    {
        var mediaEngine = new Donustur.Engine.Media.MediaEngine();
        string? resolved = mediaEngine.ResolveFfmpegPath();
        if (resolved != null)
        {
            Assert.True(File.Exists(resolved));
            Assert.EndsWith("ffmpeg.exe", resolved, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WingetPath_CanBeResolvedIfInstalled()
    {
        string? winget = Donustur.Engine.Media.FfmpegInstaller.ResolveWingetPath();
        if (winget != null)
        {
            Assert.True(File.Exists(winget));
            Assert.EndsWith("winget.exe", winget, StringComparison.OrdinalIgnoreCase);
        }
    }
}
