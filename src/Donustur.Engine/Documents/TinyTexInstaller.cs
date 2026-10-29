using System.Net.Http;
using Donustur.Engine.Archives;
using Donustur.Engine.Media;

namespace Donustur.Engine.Documents;

public static class TinyTexInstaller
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
    /// Sistemde xelatex motoru yoksa TinyTeX (resmi bağımsız xelatex dağıtımı) paketini
    /// indirip %APPDATA%\TinyTeX altına otomatik olarak kurar.
    /// Böylece Pandoc XeLaTeX denklem motoru tam sadakatle çalışır.
    /// </summary>
    public static Task<string?> EnsureOrInstallXelatexAsync(
        PandocEngine pandocEngine,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        string? existing = pandocEngine.ResolvePdfEngine();
        if (existing != null && existing.Contains("xelatex", StringComparison.OrdinalIgnoreCase))
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
                progress?.Report("XeLaTeX denklem motoru kontrol ediliyor...");
                string? found = pandocEngine.ResolvePdfEngine();
                if (found != null && found.Contains("xelatex", StringComparison.OrdinalIgnoreCase))
                {
                    return found;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string targetDir = Path.Combine(appData, "TinyTeX");
                string xelatexExe = Path.Combine(targetDir, "bin", "windows", "xelatex.exe");

                if (File.Exists(xelatexExe))
                {
                    return xelatexExe;
                }

                // 1. 7-Zip motorunu hazırla
                string? sevenZip = await SevenZipInstaller.EnsureOrInstall7ZipAsync(progress, token);
                if (sevenZip == null || !File.Exists(sevenZip))
                {
                    progress?.Report("XeLaTeX kurulumu için 7-Zip gereklidir.");
                    return null;
                }

                // 2. TinyTeX-1 resmi paketini indir (~70 MB)
                string tempExe = Path.Combine(Path.GetTempPath(), $"TinyTeX_{Guid.NewGuid():N}.exe");
                try
                {
                    progress?.Report("XeLaTeX matematik motoru (TinyTeX) indiriliyor...");
                    string downloadUrl = "https://github.com/rstudio/tinytex-releases/releases/download/v2026.10/TinyTeX-1-windows-v2026.10.exe";

                    using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("LDConverter/2.0.0");
                        using var resp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                        resp.EnsureSuccessStatusCode();

                        await using var fs = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None);
                        await resp.Content.CopyToAsync(fs, token);
                    }

                    if (File.Exists(tempExe))
                    {
                        progress?.Report("XeLaTeX motoru açılıyor...");
                        string args = $"x \"{tempExe}\" -o\"{appData}\" -y";
                        var runRes = await ProcessRunner.RunAsync(sevenZip, args, outputProgress: progress, token: token);
                        if (runRes.ExitCode == 0 && File.Exists(xelatexExe))
                        {
                            progress?.Report($"✓ XeLaTeX motoru başarıyla kuruldu: {xelatexExe}");
                            return xelatexExe;
                        }
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"XeLaTeX kurulumu atlandı: {ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                }

                return pandocEngine.ResolvePdfEngine();
            }, token);

            return _activeInstallTask;
        }
    }
}
