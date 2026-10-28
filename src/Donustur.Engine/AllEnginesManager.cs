using Donustur.Engine.Archives;
using Donustur.Engine.Documents;
using Donustur.Engine.Media;

namespace Donustur.Engine;

public sealed record EngineStatusReport(
    bool FfmpegAvailable,
    string? FfmpegPath,
    bool SevenZipAvailable,
    string? SevenZipPath,
    bool PandocAvailable,
    string? PandocPath,
    bool LibreOfficeAvailable,
    string? LibreOfficePath,
    bool XelatexAvailable = false,
    string? XelatexPath = null
)
{
    public bool AllInstalled => FfmpegAvailable && SevenZipAvailable && PandocAvailable && LibreOfficeAvailable && XelatexAvailable;
    public int InstalledCount => (FfmpegAvailable ? 1 : 0) + (SevenZipAvailable ? 1 : 0) + (PandocAvailable ? 1 : 0) + (LibreOfficeAvailable ? 1 : 0) + (XelatexAvailable ? 1 : 0);
    public int TotalCount => 5;
}

public static class AllEnginesManager
{
    private static readonly object LockObj = new();
    private static Task<EngineStatusReport>? _activeScanTask;

    public static EngineStatusReport GetCurrentStatus(
        MediaEngine? mediaEngine = null,
        ArchiveEngine? archiveEngine = null,
        PandocEngine? pandocEngine = null,
        OfficeDocumentEngine? officeEngine = null)
    {
        string? ffmpegPath = mediaEngine?.ResolveFfmpegPath() ?? (new MediaEngine()).ResolveFfmpegPath();
        string? sevenZipPath = ArchiveEngine.Resolve7ZipPath();
        string? pandocPath = pandocEngine?.ResolvePandocPath() ?? (new PandocEngine()).ResolvePandocPath();
        string? libreOfficePath = officeEngine?.ResolveSofficePath() ?? (new OfficeDocumentEngine()).ResolveSofficePath();
        string? xelatexPath = pandocEngine?.ResolvePdfEngine() ?? (new PandocEngine()).ResolvePdfEngine();

        return new EngineStatusReport(
            FfmpegAvailable: ffmpegPath != null,
            FfmpegPath: ffmpegPath,
            SevenZipAvailable: sevenZipPath != null,
            SevenZipPath: sevenZipPath,
            PandocAvailable: pandocPath != null,
            PandocPath: pandocPath,
            LibreOfficeAvailable: libreOfficePath != null,
            LibreOfficePath: libreOfficePath,
            XelatexAvailable: xelatexPath != null,
            XelatexPath: xelatexPath
        );
    }

    /// <summary>
    /// Eksik olan tüm dönüştürme motorlarını arka planda tarar ve indirir/kurar.
    /// FFmpeg, 7-Zip, Pandoc, XeLaTeX (TinyTeX) ve LibreOffice kontrol edilir.
    /// </summary>
    public static Task<EngineStatusReport> EnsureAllEnginesAsync(
        MediaEngine mediaEngine,
        ArchiveEngine archiveEngine,
        PandocEngine pandocEngine,
        OfficeDocumentEngine officeEngine,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        lock (LockObj)
        {
            if (_activeScanTask != null && !_activeScanTask.IsCompleted)
            {
                return _activeScanTask;
            }

            _activeScanTask = Task.Run(async () =>
            {
                progress?.Report("Dönüştürme motorları taranıyor...");
                var initialStatus = GetCurrentStatus(mediaEngine, archiveEngine, pandocEngine, officeEngine);

                if (initialStatus.AllInstalled)
                {
                    progress?.Report("✓ Tüm dönüştürme motorları hazır!");
                    return initialStatus;
                }

                // 1. 7-Zip Kontrolü (Hafif ve ultra hızlı indirme: ~600 KB)
                if (!initialStatus.SevenZipAvailable)
                {
                    try
                    {
                        progress?.Report("7-Zip motoru hazırlanıyor...");
                        await SevenZipInstaller.EnsureOrInstall7ZipAsync(progress, token);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"7-Zip hazırlama atlandı: {ex.Message}");
                    }
                }

                // 2. FFmpeg Kontrolü (Video & Ses dönüştürme motoru)
                if (!initialStatus.FfmpegAvailable)
                {
                    try
                    {
                        progress?.Report("FFmpeg motoru indiriliyor...");
                        await FfmpegInstaller.EnsureOrInstallFfmpegAsync(mediaEngine, progress, token);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"FFmpeg indirme atlandı: {ex.Message}");
                    }
                }

                // 3. Pandoc Kontrolü (Gelişmiş Belge & Denklem motoru)
                if (!initialStatus.PandocAvailable)
                {
                    try
                    {
                        progress?.Report("Pandoc belge motoru kontrol ediliyor...");
                        await PandocInstaller.EnsureOrInstallPandocAsync(pandocEngine, progress, token);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Pandoc kontrolü atlandı: {ex.Message}");
                    }
                }

                // 4. XeLaTeX / TinyTeX Kontrolü (Gelişmiş Matematik Formülü ve PDF Derleme Motoru)
                if (!initialStatus.XelatexAvailable)
                {
                    try
                    {
                        progress?.Report("XeLaTeX matematik motoru kontrol ediliyor...");
                        await TinyTexInstaller.EnsureOrInstallXelatexAsync(pandocEngine, progress, token);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"XeLaTeX kontrolü atlandı: {ex.Message}");
                    }
                }

                // 5. LibreOffice Kontrolü (Büyük ofis belgeleri motoru)
                if (!initialStatus.LibreOfficeAvailable)
                {
                    try
                    {
                        progress?.Report("LibreOffice motoru kontrol ediliyor...");
                        await LibreOfficeInstaller.EnsureOrInstallLibreOfficeAsync(officeEngine, progress, token);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"LibreOffice kontrolü atlandı: {ex.Message}");
                    }
                }

                var finalStatus = GetCurrentStatus(mediaEngine, archiveEngine, pandocEngine, officeEngine);
                progress?.Report($"✓ Motor taraması tamamlandı ({finalStatus.InstalledCount}/{finalStatus.TotalCount} motor hazır)");
                return finalStatus;
            }, token);

            return _activeScanTask;
        }
    }
}
