using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Donustur.Core.Catalog;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Donustur.Engine.Documents;

public sealed class OfficeDocumentEngine
{
    private string? _customPath;
    private readonly PandocEngine _pandocEngine = new();

    public OfficeDocumentEngine(string? customPath = null, string? customPandocPath = null)
    {
        _customPath = customPath;
        _pandocEngine.SetCustomPath(customPandocPath);
    }

    public void SetCustomPath(string? customPath)
    {
        _customPath = customPath;
    }

    public void SetCustomPandocPath(string? customPandocPath)
    {
        _pandocEngine.SetCustomPath(customPandocPath);
    }

    public PandocEngine Pandoc => _pandocEngine;

    /// <summary>
    /// LibreOffice soffice.exe yolunu bulur.
    /// </summary>
    public string? ResolveSofficePath()
    {
        if (!string.IsNullOrWhiteSpace(_customPath) && File.Exists(_customPath))
        {
            return _customPath;
        }

        string[] standardLocations =
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };

        foreach (var loc in standardLocations)
        {
            if (File.Exists(loc))
                return loc;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var path in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string combined = Path.Combine(path.Trim(), "soffice.exe");
                if (File.Exists(combined))
                    return combined;
            }
        }

        return null;
    }

    public bool IsAvailable => ResolveSofficePath() != null;

    /// <summary>
    /// Ofis ve metin belgelerini (DOCX, PPTX, XLSX, ODT, TXT, MD, HTML vb.) PDF veya diğer formatlara dönüştürür.
    /// LibreOffice kuruluysa tam sadakatli motoru kullanır; kurulu değilse yerel OpenXML/PDF/HTML motoruna başvurur.
    /// </summary>
    public async Task<IReadOnlyList<string>> ConvertDocumentsAsync(
        IReadOnlyList<string> inputFiles,
        string targetFormat,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        var outputFiles = new List<string>();
        string formatKey = targetFormat.ToLowerInvariant().TrimStart('.');
        string? sofficePath = ResolveSofficePath();

        for (int i = 0; i < inputFiles.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            string inputFile = inputFiles[i];
            string sourceExt = FormatCatalog.NormalizeExtension(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);

            // Kaynak ve hedef uzantı aynıysa doğrudan kopyala
            if (string.Equals(sourceExt, formatKey, StringComparison.OrdinalIgnoreCase))
            {
                string copyDest = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + "." + formatKey);
                File.Copy(inputFile, copyDest, overwrite: true);
                outputFiles.Add(copyDest);
                progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
                continue;
            }

            string? generatedPath = null;

            // 1. Öncelik: Markdown belgelerinde veya denklem içeren metinlerde Pandoc (XeLaTeX & LaTeX math desteği)
            if (sourceExt is "md" or "markdown" && _pandocEngine.IsAvailable)
            {
                generatedPath = await _pandocEngine.ConvertAsync(inputFile, formatKey, transaction, token);
            }

            // 2. Öncelik: LibreOffice kuruluysa izole profil ile çalıştır
            if ((generatedPath == null || !File.Exists(generatedPath)) && sofficePath != null)
            {
                generatedPath = await TryConvertWithLibreOfficeAsync(sofficePath, inputFile, formatKey, transaction, token);
            }

            // 3. Pandoc diğer formatlarda da alternatif olarak denenebilir
            if ((generatedPath == null || !File.Exists(generatedPath)) && _pandocEngine.IsAvailable)
            {
                generatedPath = await _pandocEngine.ConvertAsync(inputFile, formatKey, transaction, token);
            }

            // 4. Yedek: Yerel dönüştürücü (TXT, MD, HTML, DOCX <-> PDF, DOCX, TXT, HTML)
            if (generatedPath == null || !File.Exists(generatedPath))
            {
                if (CanConvertNatively(sourceExt, formatKey))
                {
                    generatedPath = await Task.Run(() => ConvertNatively(inputFile, sourceExt, formatKey, transaction), token);
                }
                else if (sofficePath == null && !_pandocEngine.IsAvailable)
                {
                    throw new FileNotFoundException(
                        $"'{sourceExt.ToUpperInvariant()} → {formatKey.ToUpperInvariant()}' dönüşümü için LibreOffice veya Pandoc gereklidir. " +
                        "Lütfen LibreOffice ya da Pandoc yükleyin veya Ayarlar ekranından soffice.exe / pandoc.exe yolunu belirtin.");
                }
                else
                {
                    throw new InvalidOperationException($"Belge dönüştürülemedi: {Path.GetFileName(inputFile)}");
                }
            }

            outputFiles.Add(generatedPath);
            progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
        }

        return outputFiles;
    }

    private static async Task<string?> TryConvertWithLibreOfficeAsync(
        string sofficePath,
        string inputFile,
        string formatKey,
        ExportTransaction transaction,
        CancellationToken token)
    {
        string tempProfileDir = Path.Combine(Path.GetTempPath(), "Donustur", "lo_profile_" + Guid.NewGuid().ToString("N"));
        string tempOutDir = Path.Combine(transaction.StagingDirectory, "lo_work_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(tempProfileDir);
        Directory.CreateDirectory(tempOutDir);

        try
        {
            string profileUri = new Uri(tempProfileDir).AbsoluteUri;
            string filterArg = formatKey == "txt" ? "txt:Text (encoded):UTF8" : formatKey;
            string args = $"-env:UserInstallation={profileUri} --headless --norestore --convert-to \"{filterArg}\" --outdir \"{tempOutDir}\" \"{inputFile}\"";

            var result = await ProcessRunner.RunAsync(sofficePath, args, token: token);
            if (result.ExitCode != 0)
            {
                return null;
            }

            var generated = Directory.GetFiles(tempOutDir);
            if (generated.Length == 0)
            {
                return null;
            }

            string desiredFileName = Path.GetFileNameWithoutExtension(inputFile) + "." + formatKey;
            string finalStagedPath = SafePaths.GetUniquePath(transaction.StagingDirectory, desiredFileName);
            File.Move(generated[0], finalStagedPath, overwrite: true);
            return finalStagedPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempOutDir)) Directory.Delete(tempOutDir, true); } catch { }
            try { if (Directory.Exists(tempProfileDir)) Directory.Delete(tempProfileDir, true); } catch { }
        }
    }

    private static bool CanConvertNatively(string sourceExt, string targetFormat)
    {
        bool supportedSource = sourceExt is "txt" or "md" or "html" or "docx";
        bool supportedTarget = targetFormat is "txt" or "html" or "docx" or "pdf";
        return supportedSource && supportedTarget;
    }

    private static string ConvertNatively(string inputFile, string sourceExt, string targetFormat, ExportTransaction transaction)
    {
        string baseName = Path.GetFileNameWithoutExtension(inputFile);
        string outPath = SafePaths.GetUniquePath(transaction.StagingDirectory, $"{baseName}.{targetFormat}");

        string plainText = ReadDocumentPlainText(inputFile, sourceExt);

        switch (targetFormat)
        {
            case "txt":
                File.WriteAllText(outPath, plainText, Encoding.UTF8);
                break;

            case "docx":
                DocxHelper.CreateDocxFromPages(outPath, new[] { plainText });
                break;

            case "html":
                string html = BuildHtmlFromText(baseName, plainText);
                File.WriteAllText(outPath, html, Encoding.UTF8);
                break;

            case "pdf":
                RenderTextToPdf(plainText, outPath);
                break;

            default:
                throw new NotSupportedException($"Desteklenmeyen hedef belge formatı: {targetFormat}");
        }

        return outPath;
    }

    private static string ReadDocumentPlainText(string inputFile, string sourceExt)
    {
        if (sourceExt == "docx")
        {
            return DocxHelper.ExtractTextFromDocx(inputFile);
        }

        string raw = File.ReadAllText(inputFile, Encoding.UTF8);
        if (sourceExt == "html")
        {
            string withoutScripts = Regex.Replace(raw, @"<(script|style)[^>]*?>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string withBreaks = Regex.Replace(withoutScripts, @"<(br|p|div|li|tr|h[1-6])[^>]*?>", "\n", RegexOptions.IgnoreCase);
            string stripped = Regex.Replace(withBreaks, @"<[^>]+>", "");
            return WebUtility.HtmlDecode(stripped).Trim();
        }

        return raw;
    }

    private static string BuildHtmlFromText(string title, string text)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"tr\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("  <style>body{font-family:'Segoe UI',system-ui,sans-serif;line-height:1.6;max-width:800px;margin:40px auto;padding:0 20px;color:#1f2937;background:#ffffff;}p{margin:0 0 12px 0;white-space:pre-wrap;}code{background:#f3f4f6;padding:2px 6px;border-radius:4px;font-family:monospace;}</style>");
        sb.AppendLine("  <script>window.MathJax = { tex: { inlineMath: [['$', '$'], ['\\\\(', '\\\\)']] } };</script>");
        sb.AppendLine("  <script id=\"MathJax-script\" async src=\"https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js\"></script>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            sb.AppendLine($"  <p>{WebUtility.HtmlEncode(line)}</p>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void RenderTextToPdf(string text, string outputPath)
    {
        using var document = new PdfDocument();
        var font = new XFont("Arial", 11, XFontStyle.Regular);

        string[] rawLines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var wrappedLines = new List<string>();
        const int maxChars = 90;

        foreach (var raw in rawLines)
        {
            if (string.IsNullOrEmpty(raw))
            {
                wrappedLines.Add(string.Empty);
                continue;
            }

            string rem = raw;
            while (rem.Length > 0)
            {
                int len = Math.Min(maxChars, rem.Length);
                wrappedLines.Add(rem[..len]);
                rem = rem[len..];
            }
        }

        if (wrappedLines.Count == 0)
        {
            wrappedLines.Add(string.Empty);
        }

        const double margin = 45;
        const double lineHeight = 16;
        int lineIndex = 0;

        while (lineIndex < wrappedLines.Count)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            double y = margin;
            double maxY = page.Height.Point - margin;

            while (lineIndex < wrappedLines.Count && y + lineHeight <= maxY)
            {
                string line = wrappedLines[lineIndex++];
                if (!string.IsNullOrEmpty(line))
                {
                    gfx.DrawString(line, font, XBrushes.Black, new XPoint(margin, y + 11));
                }
                y += lineHeight;
            }
        }

        document.Save(outputPath);
    }
}
