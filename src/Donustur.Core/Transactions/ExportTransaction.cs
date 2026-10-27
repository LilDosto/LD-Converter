using Donustur.Core.Utils;

namespace Donustur.Core.Transactions;

/// <summary>
/// Dönüştürme sırasında hedef klasörün yarım/bozuk dosyalarla kirlenmesini önleyen
/// atomik evreleme (staging) yöneticisi. Farklı disk sürücüleri (C:, D:, USB) arasında
/// klasör hiyerarşisini koruyarak güvenli aktarım yapar.
/// </summary>
public sealed class ExportTransaction : IDisposable
{
    private bool _disposed;
    public bool IsCommitted { get; private set; }

    public string StagingDirectory { get; }

    public ExportTransaction(string? baseDir = null)
    {
        string root = string.IsNullOrWhiteSpace(baseDir)
            ? Path.Combine(Path.GetTempPath(), "LDConverter")
            : baseDir;

        StagingDirectory = Path.Combine(root, "staging_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StagingDirectory);
    }

    /// <summary>
    /// Staging klasöründeki tüm dosya ve alt klasörleri yapısını koruyarak hedef klasöre taşır.
    /// </summary>
    public List<string> CommitTo(string destinationFolder, bool overwrite = false)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExportTransaction));

        if (!Directory.Exists(destinationFolder))
        {
            Directory.CreateDirectory(destinationFolder);
        }

        var finalPaths = new List<string>();

        // 1. Üst seviye dosyaları taşı
        var topFiles = Directory.GetFiles(StagingDirectory, "*", SearchOption.TopDirectoryOnly);
        foreach (var sourceFile in topFiles)
        {
            string fileName = Path.GetFileName(sourceFile);
            string dest = Path.Combine(destinationFolder, fileName);

            if (!overwrite && (File.Exists(dest) || Directory.Exists(dest)))
            {
                dest = SafePaths.GetUniquePath(destinationFolder, fileName);
            }

            File.Move(sourceFile, dest, overwrite: true);
            finalPaths.Add(dest);
        }

        // 2. Üst seviye klasörleri (örn. arşivden çıkarılan klasörler) alt yapısıyla birlikte taşı
        var topDirs = Directory.GetDirectories(StagingDirectory, "*", SearchOption.TopDirectoryOnly);
        foreach (var sourceDir in topDirs)
        {
            string dirName = Path.GetFileName(sourceDir);
            string destDir = Path.Combine(destinationFolder, dirName);

            if (!overwrite && (Directory.Exists(destDir) || File.Exists(destDir)))
            {
                destDir = SafePaths.GetUniquePath(destinationFolder, dirName);
            }

            MoveDirectoryCrossVolume(sourceDir, destDir);
            finalPaths.Add(destDir);
        }

        IsCommitted = true;
        return finalPaths;
    }

    private static void MoveDirectoryCrossVolume(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string targetFile = Path.Combine(destDir, relative);
            string? targetParent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }
            File.Move(file, targetFile, overwrite: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(StagingDirectory))
            {
                Directory.Delete(StagingDirectory, recursive: true);
            }
        }
        catch
        {
            // Arka plandaki kilitli dosyalar için sessizce geç
        }
    }
}
