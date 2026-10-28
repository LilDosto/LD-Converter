using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using Donustur.Core.Utils;

namespace Donustur.Engine.Media;

public static class FfmpegInstaller
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
    /// FFmpeg kurulu değilse Windows winget veya doğrudan resmi GitHub kaynağından
    /// indirip kurar. Eşzamanlı çağrıları güvenle tek bir kuruluma yönlendirir.
    /// </summary>
    public static Task<string?> EnsureOrInstallFfmpegAsync(
        MediaEngine mediaEngine,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        // 1. Zaten mevcutsa anında döndür
        string? existing = mediaEngine.ResolveFfmpegPath();
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

            _activeInstallTask = Task.Run(() => PerformInstallAsync(mediaEngine, progress, token), token);
            return _activeInstallTask;
        }
    }

    private static async Task<string?> PerformInstallAsync(
        MediaEngine mediaEngine,
        IProgress<string>? progress,
        CancellationToken token)
    {
        progress?.Report("FFmpeg taranıyor...");

        // 1. Derin tarama
        string? found = mediaEngine.ResolveFfmpegPath();
        if (found != null)
        {
            progress?.Report($"FFmpeg bulundu: {found}");
            return found;
        }

        // 2. Yöntem 1: Windows Paket Yöneticisi (winget)
        string? wingetPath = ResolveWingetPath();
        if (wingetPath != null)
        {
            try
            {
                progress?.Report("FFmpeg, Windows Paket Yöneticisi (winget) ile indiriliyor...");
                string args = "install --id Gyan.FFmpeg.Essentials --accept-package-agreements --accept-source-agreements --silent";

                var result = await ProcessRunner.RunAsync(
                    wingetPath,
                    args,
                    outputProgress: progress,
                    token: token);

                if (result.ExitCode == 0)
                {
                    // Çevre değişkenlerini ve winget paket klasörünü yeniden tara
                    found = mediaEngine.ResolveFfmpegPath();
                    if (found != null)
                    {
                        progress?.Report($"✓ FFmpeg başarıyla kuruldu: {found}");
                        return found;
                    }
                }
            }
            catch
            {
                // Winget başarısız olursa doğrudan indirme yöntemine geç
            }
        }

        // 3. Yöntem 2: Doğrudan resmi GitHub sürümünden taşınabilir FFmpeg indirme
        try
        {
            progress?.Report("FFmpeg resmi sunucudan doğrudan indiriliyor...");
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string toolsDir = Path.Combine(localAppData, "LDConverter", "tools");
            Directory.CreateDirectory(toolsDir);

            string zipPath = Path.Combine(toolsDir, "ffmpeg_download.zip");
            string targetExePath = Path.Combine(toolsDir, "ffmpeg.exe");

            // GyanD / FFmpeg essentials build doğrudan indirme URL'si
            string downloadUrl = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";

            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LDConverter/1.3.0");

                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using (var contentStream = await response.Content.ReadAsStreamAsync(token))
                await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
                        totalRead += read;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double pct = (double)totalRead / totalBytes.Value * 100.0;
                            progress?.Report($"FFmpeg indiriliyor: %{pct:F0}");
                        }
                    }
                }
            }

            progress?.Report("FFmpeg paketi açılıyor...");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var ffmpegEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                if (ffmpegEntry != null)
                {
                    ffmpegEntry.ExtractToFile(targetExePath, overwrite: true);
                }
            }

            try { File.Delete(zipPath); } catch { }

            if (File.Exists(targetExePath))
            {
                mediaEngine.SetCustomPath(targetExePath);
                progress?.Report($"✓ FFmpeg başarıyla hazırlandı: {targetExePath}");
                return targetExePath;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"✕ FFmpeg indirme hatası: {ex.Message}");
        }

        return mediaEngine.ResolveFfmpegPath();
    }

    public static string? ResolveWingetPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultWinget = Path.Combine(localAppData, @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(defaultWinget))
        {
            return defaultWinget;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;
                string candidate = Path.Combine(segment.Trim(), "winget.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
