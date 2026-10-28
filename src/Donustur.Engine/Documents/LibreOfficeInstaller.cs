using Donustur.Engine.Media;

namespace Donustur.Engine.Documents;

public static class LibreOfficeInstaller
{
    private static readonly object LockObj = new();
    private static Task<string?>? _activeInstallTask;

    public static bool IsInstalling
    {
        get
        {
            lock (LockObj)
            {
                return _activeInstallTask != null && !_activeInstallTask.IsCompleted;
            }
        }
    }

    /// <summary>
    /// LibreOffice sistemde yoksa Windows winget üzerinden sessizce kurar.
    /// </summary>
    public static Task<string?> EnsureOrInstallLibreOfficeAsync(
        OfficeDocumentEngine officeEngine,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        string? existing = officeEngine.ResolveSofficePath();
        if (existing != null)
        {
            return Task.FromResult<string?>(existing);
        }

        lock (LockObj)
        {
            if (_activeInstallTask != null && !_activeInstallTask.IsCompleted)
            {
                return _activeInstallTask;
            }

            _activeInstallTask = Task.Run(async () =>
            {
                progress?.Report("LibreOffice taranıyor...");
                string? found = officeEngine.ResolveSofficePath();
                if (found != null) return found;

                string? wingetPath = FfmpegInstaller.ResolveWingetPath();
                if (wingetPath != null)
                {
                    try
                    {
                        progress?.Report("LibreOffice winget üzerinden indiriliyor ve kuruluyor...");
                        string args = "install --id TheDocumentFoundation.LibreOffice --accept-package-agreements --accept-source-agreements --silent";

                        var result = await ProcessRunner.RunAsync(wingetPath, args, outputProgress: progress, token: token);
                        if (result.ExitCode == 0)
                        {
                            found = officeEngine.ResolveSofficePath();
                            if (found != null)
                            {
                                progress?.Report($"✓ LibreOffice başarıyla kuruldu: {found}");
                                return found;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"✕ LibreOffice kurulum hatası: {ex.Message}");
                    }
                }

                return officeEngine.ResolveSofficePath();
            }, token);

            return _activeInstallTask;
        }
    }
}
