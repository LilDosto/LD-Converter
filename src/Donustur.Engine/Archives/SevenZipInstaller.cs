using System.Net.Http;
using Donustur.Engine.Media;

namespace Donustur.Engine.Archives;

public static class SevenZipInstaller
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
    /// 7-Zip sistemde yoksa resmi 7-Zip standalone motorunu (7zr.exe) veya winget paketini
    /// otomatik olarak %LOCALAPPDATA%\LDConverter\tools\7z.exe altına indirir ve hazırlar.
    /// </summary>
    public static Task<string?> EnsureOrInstall7ZipAsync(
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        string? existing = ArchiveEngine.Resolve7ZipPath();
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
                progress?.Report("7-Zip taranıyor...");
                string? found = ArchiveEngine.Resolve7ZipPath();
                if (found != null) return found;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string toolsDir = Path.Combine(localAppData, "LDConverter", "tools");
                Directory.CreateDirectory(toolsDir);
                string targetExe = Path.Combine(toolsDir, "7z.exe");

                // 1. Doğrudan resmi hızlı indirme (7-Zip Standalone Konsol Motoru ~600 KB)
                try
                {
                    progress?.Report("7-Zip konsol motoru indiriliyor...");
                    string downloadUrl = "https://www.7-zip.org/a/7zr.exe";

                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("LDConverter/1.9.0");

                    byte[] data = await client.GetByteArrayAsync(downloadUrl, token);
                    if (data.Length > 10000)
                    {
                        await File.WriteAllBytesAsync(targetExe, data, token);
                        if (File.Exists(targetExe))
                        {
                            progress?.Report($"✓ 7-Zip motoru hazırlandı: {targetExe}");
                            return targetExe;
                        }
                    }
                }
                catch
                {
                    // Doğrudan indirme başarısızsa winget'i dene
                }

                // 2. Winget alternatifi
                string? wingetPath = FfmpegInstaller.ResolveWingetPath();
                if (wingetPath != null)
                {
                    try
                    {
                        progress?.Report("7-Zip winget üzerinden kuruluyor...");
                        string args = "install --id 7zip.7zip --accept-package-agreements --accept-source-agreements --silent";
                        var result = await ProcessRunner.RunAsync(wingetPath, args, outputProgress: progress, token: token);
                        if (result.ExitCode == 0)
                        {
                            found = ArchiveEngine.Resolve7ZipPath();
                            if (found != null)
                            {
                                progress?.Report($"✓ 7-Zip başarıyla kuruldu: {found}");
                                return found;
                            }
                        }
                    }
                    catch
                    {
                        // Sessizce geç
                    }
                }

                return ArchiveEngine.Resolve7ZipPath();
            }, token);

            return _activeInstallTask;
        }
    }
}
