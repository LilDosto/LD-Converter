using System.Diagnostics;
using Donustur.Engine.Media;

namespace Donustur.Engine.Documents;

public static class PandocInstaller
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
    /// Pandoc kurulu değilse Windows winget üzerinden sessizce kurar.
    /// </summary>
    public static Task<string?> EnsureOrInstallPandocAsync(
        PandocEngine pandocEngine,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        string? existing = pandocEngine.ResolvePandocPath();
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
                progress?.Report("Pandoc aranıyor...");
                string? found = pandocEngine.ResolvePandocPath();
                if (found != null) return found;

                string? wingetPath = FfmpegInstaller.ResolveWingetPath();
                if (wingetPath != null)
                {
                    try
                    {
                        progress?.Report("Pandoc, Windows Paket Yöneticisi (winget) ile indiriliyor...");
                        string args = "install --id JohnMacFarlane.Pandoc --accept-package-agreements --accept-source-agreements --silent";

                        var result = await ProcessRunner.RunAsync(wingetPath, args, outputProgress: progress, token: token);
                        if (result.ExitCode == 0)
                        {
                            found = pandocEngine.ResolvePandocPath();
                            if (found != null)
                            {
                                progress?.Report($"✓ Pandoc başarıyla kuruldu: {found}");
                                return found;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"✕ Pandoc kurulum hatası: {ex.Message}");
                    }
                }

                return pandocEngine.ResolvePandocPath();
            }, token);

            return _activeInstallTask;
        }
    }
}
