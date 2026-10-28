using Donustur.Core.Catalog;
using Donustur.Core.Models;
using Donustur.Core.Services;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using Donustur.Engine.Archives;
using Donustur.Engine.Documents;
using Donustur.Engine.Images;
using Donustur.Engine.Media;
using Donustur.Engine.Qr;

namespace Donustur.Engine;

public sealed class ConversionDispatcher
{
    private readonly ImageMagickEngine _imageEngine = new();
    private readonly OfficeDocumentEngine _officeEngine;
    private readonly PdfEngine _pdfEngine = new();
    private readonly MediaEngine _mediaEngine;
    private readonly ArchiveEngine _archiveEngine = new();
    private readonly QrEngine _qrEngine = new();
    private readonly HistoryStore? _historyStore;

    public ConversionDispatcher(AppPreferences preferences, HistoryStore? historyStore = null)
    {
        _officeEngine = new OfficeDocumentEngine(preferences.CustomLibreOfficePath, preferences.CustomPandocPath);
        _mediaEngine = new MediaEngine(preferences.CustomFfmpegPath);
        _historyStore = historyStore;
    }

    public async Task<ConversionResult> ExecuteTaskAsync(
        ConversionTask task,
        AppPreferences preferences,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        return await Task.Run(async () =>
        {
            task.Status = ConversionTaskStatus.Running;

            // Ayarlar ekranından değiştirilmiş olabilecek harici motor yollarını senkronize et
            _officeEngine.SetCustomPath(preferences.CustomLibreOfficePath);
            _officeEngine.SetCustomPandocPath(preferences.CustomPandocPath);
            _mediaEngine.SetCustomPath(preferences.CustomFfmpegPath);

            if (preferences.DefaultImageQuality is >= 10 and <= 100 && task.Options.ImageQuality == 85)
            {
                task.Options.ImageQuality = preferences.DefaultImageQuality;
            }

            string targetAction = task.TargetActionId.ToLowerInvariant().TrimStart('.');
            string destination = ResolveDestinationFolder(task, preferences);

            using var transaction = new ExportTransaction();

            try
            {
                var files = task.InputFiles;
                if (files.Count == 0)
                {
                    throw new InvalidOperationException("İşlenecek dosya bulunamadı.");
                }

                long totalInitialBytes = FileUtils.CalculateTotalBytes(files);
                IReadOnlyList<string> generatedStagedFiles;

                // 1. QR İşlemleri (Görsel ve PDF destekli)
                if (targetAction == "qr_read")
                {
                    var qrResults = new List<string>();
                    var decodedTexts = new List<string>();

                    for (int i = 0; i < files.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string f = files[i];
                        string? text = _qrEngine.ReadQrCode(f);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            decodedTexts.Add(text);
                            string txtPath = SafePaths.GetUniquePath(
                                transaction.StagingDirectory,
                                Path.GetFileNameWithoutExtension(f) + "_QR.txt");
                            await File.WriteAllTextAsync(txtPath, text, token);
                            qrResults.Add(txtPath);
                        }
                        progress?.Report((double)(i + 1) / files.Count * 100.0);
                    }

                    if (qrResults.Count == 0)
                    {
                        throw new InvalidOperationException("Seçilen dosyada okunabilir bir QR kod veya barkod bulunamadı.");
                    }

                    task.CurrentStatusMessage = string.Join(" | ", decodedTexts);
                    generatedStagedFiles = qrResults;
                }
                // 2. Arşiv Çıkarma
                else if (targetAction == "extract_folder")
                {
                    var extractedDirs = new List<string>();
                    foreach (var f in files)
                    {
                        string dir = await _archiveEngine.ExtractAsync(f, transaction, task.Options.ArchivePassword, progress, token);
                        extractedDirs.Add(dir);
                    }
                    generatedStagedFiles = extractedDirs;
                }
                // 3. ZIP Paketleme / Arşiv Dönüştürme
                else if (targetAction == "zip")
                {
                    string zip = await _archiveEngine.CreateZipAsync(files, transaction, null, progress, token);
                    generatedStagedFiles = new[] { zip };
                }
                // 4. 7Z Paketleme / Arşiv Dönüştürme
                else if (targetAction == "7z")
                {
                    string sevenZ = await _archiveEngine.Create7zAsync(files, transaction, null, progress, token);
                    generatedStagedFiles = new[] { sevenZ };
                }
                // 5. TAR.GZ Paketleme / Arşiv Dönüştürme
                else if (targetAction == "tar_gz")
                {
                    string tarGz = await _archiveEngine.CreateTarGzAsync(files, transaction, null, progress, token);
                    generatedStagedFiles = new[] { tarGz };
                }
                // 6. Yeni Klasöre Toplama
                else if (targetAction == "copy_folder")
                {
                    string folder = await _archiveEngine.CollectToFolderAsync(files, transaction, progress, token);
                    generatedStagedFiles = new[] { folder };
                }
                // 7. PDF veya Görsel Birleştirme
                else if (targetAction == "pdf_merge")
                {
                    if (files.All(FormatCatalog.IsImage))
                    {
                        generatedStagedFiles = await _imageEngine.ConvertImagesAsync(files, "pdf", transaction, task.Options, progress, token);
                    }
                    else
                    {
                        var mergeable = files.Where(f => FormatCatalog.IsPdf(f) || FormatCatalog.IsImage(f)).ToList();
                        if (mergeable.Count == 0)
                        {
                            throw new InvalidOperationException("PDF olarak birleştirmek için en az bir PDF veya görsel dosyası seçilmelidir.");
                        }
                        string merged = await _pdfEngine.MergePdfsAsync(mergeable, transaction, null, progress, token);
                        generatedStagedFiles = new[] { merged };
                    }
                }
                // 8. PDF Sayfa Ayırma
                else if (targetAction == "pdf_split")
                {
                    var splitFiles = new List<string>();
                    foreach (var f in files.Where(FormatCatalog.IsPdf))
                    {
                        var pages = await _pdfEngine.SplitPdfAsync(f, transaction, progress, token);
                        splitFiles.AddRange(pages);
                    }
                    generatedStagedFiles = splitFiles;
                }
                // 9. PDF'den Metin Çıkarma
                else if (targetAction == "pdf_to_txt")
                {
                    var txts = new List<string>();
                    foreach (var f in files)
                    {
                        string txt = await _pdfEngine.ExtractTextAsync(f, transaction, progress, token);
                        txts.Add(txt);
                    }
                    generatedStagedFiles = txts;
                }
                // 10. PDF'den Word (DOCX) Belgesine Dönüştürme
                else if (targetAction == "pdf_to_docx")
                {
                    var docxs = new List<string>();
                    foreach (var f in files)
                    {
                        string docx = await _pdfEngine.ConvertToDocxAsync(f, transaction, progress, token);
                        docxs.Add(docx);
                    }
                    generatedStagedFiles = docxs;
                }
                // 11. PDF'den Görsele (PNG veya JPG) Aktarma
                else if (targetAction is "pdf_to_images" or "pdf_to_jpg")
                {
                    string imgFormat = targetAction == "pdf_to_jpg" ? "jpg" : "png";
                    var imgs = new List<string>();
                    foreach (var f in files)
                    {
                        var extracted = await _pdfEngine.ExtractImagesAsync(f, transaction, progress, token, imgFormat);
                        imgs.AddRange(extracted);
                    }
                    generatedStagedFiles = imgs;
                }
                // 12. Görsel İşlemleri (PNG, JPG, WEBP, AVIF, ICO, Çoklu Görsel -> GIF/PDF vb.)
                else if (files.All(FormatCatalog.IsImage) &&
                         targetAction is "png" or "jpg" or "webp" or "avif" or "ico" or "bmp" or "tiff" or "gif" or "pdf" or "compress_image")
                {
                    generatedStagedFiles = await _imageEngine.ConvertImagesAsync(files, targetAction, transaction, task.Options, progress, token);
                }
                // 13. Video ve Ses Dönüşümleri
                else if (files.Any(FormatCatalog.IsVideo) || files.Any(FormatCatalog.IsAudio) ||
                         targetAction is "mp4" or "mp3" or "wav" or "aac" or "flac" or "ogg" or "mkv" or "webm" or "compress_video" or "compress_audio")
                {
                    generatedStagedFiles = await _mediaEngine.ConvertMediaAsync(files, targetAction, transaction, task.Options, progress, token);
                }
                // 14. Ofis Belgeleri (DOCX, PPTX, XLSX, ODT, TXT, MD, HTML vb.)
                else if (files.All(FormatCatalog.IsDocument) || targetAction is "docx" or "txt" or "html")
                {
                    generatedStagedFiles = await _officeEngine.ConvertDocumentsAsync(files, targetAction, transaction, progress, token);
                }
                // 15. Tekil Görsel Yedeği
                else if (targetAction is "png" or "jpg" or "webp" or "avif" or "ico" or "compress_image")
                {
                    generatedStagedFiles = await _imageEngine.ConvertImagesAsync(files, targetAction, transaction, task.Options, progress, token);
                }
                else
                {
                    // Genel ZIP arşivleme yedeği
                    string zip = await _archiveEngine.CreateZipAsync(files, transaction, null, progress, token);
                    generatedStagedFiles = new[] { zip };
                }

                // Dosyaları nihai hedef klasöre taşı (Commit)
                var finalCommittedPaths = transaction.CommitTo(destination, task.Options.OverwriteExisting);
                long totalFinalBytes = FileUtils.CalculateTotalBytes(finalCommittedPaths);

                task.Status = ConversionTaskStatus.Completed;
                task.CompletedAt = DateTime.Now;
                task.OutputFiles.AddRange(finalCommittedPaths);

                string sourceLabel = files.Count == 1
                    ? FormatCatalog.NormalizeExtension(files[0]).ToUpperInvariant()
                    : $"{files.Count} ÖĞE";
                string targetLabel = FormatFriendlyActionLabel(targetAction);

                _historyStore?.Add(new HistoryEntry
                {
                    Title = files.Count == 1
                        ? Path.GetFileName(files[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : $"{files.Count} öğe ({Path.GetFileName(files[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}...)",
                    FormatSummary = $"{sourceLabel} → {targetLabel}",
                    InputFiles = files.ToList(),
                    OutputFiles = finalCommittedPaths.ToList(),
                    OriginalSizeBytes = totalInitialBytes,
                    OutputSizeBytes = totalFinalBytes,
                    Timestamp = DateTime.Now,
                    Success = true
                });

                return new ConversionResult(true, finalCommittedPaths, null, task.Duration);
            }
            catch (OperationCanceledException)
            {
                task.Status = ConversionTaskStatus.Cancelled;
                task.CompletedAt = DateTime.Now;
                return new ConversionResult(false, Array.Empty<string>(), "İşlem kullanıcı tarafından iptal edildi.");
            }
            catch (Exception ex)
            {
                task.Status = ConversionTaskStatus.Failed;
                task.CompletedAt = DateTime.Now;
                task.ErrorMessage = ex.Message;

                _historyStore?.Add(new HistoryEntry
                {
                    Title = task.InputFiles.Count == 1
                        ? Path.GetFileName(task.InputFiles[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : $"{task.InputFiles.Count} dosya",
                    FormatSummary = FormatFriendlyActionLabel(targetAction),
                    InputFiles = task.InputFiles.ToList(),
                    OutputFiles = new List<string>(),
                    Timestamp = DateTime.Now,
                    Success = false,
                    ErrorMessage = ex.Message
                });

                return new ConversionResult(false, Array.Empty<string>(), ex.Message, task.Duration);
            }
        }, token);
    }

    private static string FormatFriendlyActionLabel(string action)
    {
        return action switch
        {
            "pdf_to_images" => "PNG",
            "pdf_to_jpg" => "JPG",
            "pdf_to_txt" => "TXT",
            "pdf_to_docx" => "DOCX",
            "pdf_merge" => "PDF BİRLEŞTİR",
            "pdf_split" => "PDF AYIR",
            "extract_folder" => "KLASÖRE ÇIKAR",
            "copy_folder" => "KLASÖRE TOPLA",
            "compress_image" or "compress_video" or "compress_audio" => "SIKIŞTIR",
            "qr_read" => "QR OKU",
            "tar_gz" => "TAR.GZ",
            _ => action.ToUpperInvariant()
        };
    }

    public static string ResolveDestinationFolder(ConversionTask task, AppPreferences preferences)
    {
        if (!string.IsNullOrWhiteSpace(task.Options.DestinationFolder) && Directory.Exists(task.Options.DestinationFolder))
        {
            return task.Options.DestinationFolder;
        }

        if (preferences.OutputDirectoryMode == "Desktop")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        if (preferences.OutputDirectoryMode == "Custom" &&
            !string.IsNullOrWhiteSpace(preferences.CustomOutputDirectory))
        {
            try
            {
                if (!Directory.Exists(preferences.CustomOutputDirectory))
                {
                    Directory.CreateDirectory(preferences.CustomOutputDirectory);
                }
                return preferences.CustomOutputDirectory;
            }
            catch
            {
                // Özel klasör oluşturulamazsa kaynak klasöre dön
            }
        }

        // SameAsSource (Hem dosya hem klasör girdilerini destekler)
        if (task.InputFiles.Count > 0)
        {
            string first = task.InputFiles[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(first) || Directory.Exists(first))
            {
                string? dir = Path.GetDirectoryName(first);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}
