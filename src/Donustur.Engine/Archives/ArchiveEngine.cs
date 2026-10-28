using System.IO.Compression;
using Donustur.Core.Catalog;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace Donustur.Engine.Archives;

public sealed class ArchiveEngine
{
    public static string? Resolve7ZipPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(localAppData, "LDConverter", "tools", "7z.exe"),
            Path.Combine(localAppData, "LDConverter", "tools", "7zr.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            @"C:\ProgramData\chocolatey\bin\7z.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // WinGet paket dizini
        try
        {
            string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                var matches = Directory.GetFiles(wingetPackages, "7z.exe", SearchOption.AllDirectories);
                if (matches.Length > 0 && File.Exists(matches[0]))
                {
                    return matches[0];
                }
            }
        }
        catch { }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;
                string c1 = Path.Combine(segment.Trim(), "7z.exe");
                if (File.Exists(c1)) return c1;
                string c2 = Path.Combine(segment.Trim(), "7zr.exe");
                if (File.Exists(c2)) return c2;
            }
        }

        return null;
    }

    public bool Is7ZipAvailable => Resolve7ZipPath() != null;

    /// <summary>
    /// Arşiv dosyasını klasöre çıkarır. ZIP, 7Z, TAR, GZ, TGZ, RAR destekler.
    /// </summary>
    public async Task<string> ExtractAsync(
        string archivePath,
        ExportTransaction transaction,
        string? password = null,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string baseName = GetCleanArchiveBaseName(archivePath);
        string extractDir = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + "_Cikarilan");
        Directory.CreateDirectory(extractDir);

        await Task.Run(() =>
        {
            ExtractToDirectoryInternal(archivePath, extractDir, password, progress, token);
        }, token);

        return extractDir;
    }

    private static void ExtractToDirectoryInternal(
        string archivePath,
        string extractDir,
        string? password,
        IProgress<double>? progress,
        CancellationToken token)
    {
        string lowerName = Path.GetFileName(archivePath).ToLowerInvariant();
        var readerOptions = new ReaderOptions { Password = password };

        // .tar.gz / .tgz / .gz akış tabanlı arşivler için ReaderFactory kullan
        if (lowerName.EndsWith(".tar.gz") || lowerName.EndsWith(".tgz") || lowerName.EndsWith(".gz"))
        {
            using var fileStream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(fileStream, readerOptions);
            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(extractDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
            progress?.Report(100.0);
            return;
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        int total = Math.Max(1, entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            entry.WriteToDirectory(extractDir, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });

            progress?.Report((double)(i + 1) / total * 100.0);
        }
    }

    /// <summary>
    /// Verilen dosyaları (veya mevcut arşivlerin içeriğini) ZIP arşivi olarak paketler.
    /// </summary>
    public async Task<string> CreateZipAsync(
        IReadOnlyList<string> files,
        ExportTransaction transaction,
        string? zipName = null,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        // Eğer girdilerin tamamı zaten arşivse (örn. .rar veya .7z -> .zip dönüşümü), önce açıp sonra ZIP yap
        if (files.Count > 0 && files.All(FormatCatalog.IsArchive))
        {
            return await ConvertArchivesToFormatAsync(files, "zip", transaction, progress, token);
        }

        string name = string.IsNullOrWhiteSpace(zipName)
            ? (files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".zip" : "Arsiv.zip")
            : zipName;

        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name += ".zip";
        }

        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, name);

        await Task.Run(() =>
        {
            using var zipStream = new FileStream(outputPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string file = files[i];

                if (File.Exists(file))
                {
                    string entryName = GetUniqueArchiveEntryName(Path.GetFileName(file), usedEntryNames);
                    archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
                else if (Directory.Exists(file))
                {
                    string dirName = Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    AddDirectoryToZip(archive, file, dirName, usedEntryNames);
                }

                progress?.Report((double)(i + 1) / files.Count * 100.0);
            }
        }, token);

        return outputPath;
    }

    /// <summary>
    /// Verilen dosyaları .tar.gz olarak paketler.
    /// </summary>
    public async Task<string> CreateTarGzAsync(
        IReadOnlyList<string> files,
        ExportTransaction transaction,
        string? archiveName = null,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        if (files.Count > 0 && files.All(FormatCatalog.IsArchive))
        {
            return await ConvertArchivesToFormatAsync(files, "tar_gz", transaction, progress, token);
        }

        string baseName = string.IsNullOrWhiteSpace(archiveName)
            ? (files.Count == 1 ? GetCleanArchiveBaseName(files[0]) : "Arsiv")
            : GetCleanArchiveBaseName(archiveName);

        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + ".tar.gz");

        await Task.Run(() =>
        {
            using var stream = File.OpenWrite(outputPath);
            var writerOptions = new WriterOptions(CompressionType.GZip) { LeaveStreamOpen = false };
            using var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, writerOptions);

            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string item = files[i];

                if (File.Exists(item))
                {
                    writer.Write(Path.GetFileName(item), item);
                }
                else if (Directory.Exists(item))
                {
                    string prefix = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    foreach (var subFile in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        string rel = Path.Combine(prefix, Path.GetRelativePath(item, subFile)).Replace('\\', '/');
                        writer.Write(rel, subFile);
                    }
                }

                progress?.Report((double)(i + 1) / files.Count * 100.0);
            }
        }, token);

        return outputPath;
    }

    /// <summary>
    /// Verilen dosyaları 7-Zip (.7z) olarak paketler. Sistemde 7z.exe varsa kullanır.
    /// </summary>
    public async Task<string> Create7zAsync(
        IReadOnlyList<string> files,
        ExportTransaction transaction,
        string? archiveName = null,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        if (files.Count > 0 && files.All(FormatCatalog.IsArchive))
        {
            return await ConvertArchivesToFormatAsync(files, "7z", transaction, progress, token);
        }

        string? sevenZipExe = Resolve7ZipPath();
        if (sevenZipExe == null)
        {
            sevenZipExe = await SevenZipInstaller.EnsureOrInstall7ZipAsync(token: token);
        }

        if (sevenZipExe == null || !File.Exists(sevenZipExe))
        {
            throw new FileNotFoundException(
                "7-Zip (7z.exe) bulunamadı ve otomatik indirilemedi. .7z sıkıştırması için lütfen internet bağlantınızı kontrol edin veya Ayarlar ekranından 7-Zip motorunu kurun.");
        }

        string baseName = string.IsNullOrWhiteSpace(archiveName)
            ? (files.Count == 1 ? GetCleanArchiveBaseName(files[0]) : "Arsiv")
            : GetCleanArchiveBaseName(archiveName);

        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + ".7z");
        string quotedFiles = string.Join(" ", files.Select(f => $"\"{f}\""));
        string args = $"a -t7z -mx=7 -y \"{outputPath}\" {quotedFiles}";

        progress?.Report(25.0);
        var result = await ProcessRunner.RunAsync(sevenZipExe, args, token: token);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"7-Zip arşivleme hatası ({result.ExitCode}): {result.StandardError}");
        }

        progress?.Report(100.0);
        return outputPath;
    }

    /// <summary>
    /// Mevcut bir arşivi (RAR, 7Z, ZIP, TAR.GZ) açıp hedef arşiv formatına (ZIP, 7Z, TAR.GZ) dönüştürür.
    /// </summary>
    private async Task<string> ConvertArchivesToFormatAsync(
        IReadOnlyList<string> archiveFiles,
        string targetFormat,
        ExportTransaction transaction,
        IProgress<double>? progress,
        CancellationToken token)
    {
        string tempExtractRoot = Path.Combine(Path.GetTempPath(), "Donustur", "repack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExtractRoot);

        try
        {
            for (int i = 0; i < archiveFiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string archivePath = archiveFiles[i];
                string subDir = archiveFiles.Count == 1
                    ? tempExtractRoot
                    : Path.Combine(tempExtractRoot, GetCleanArchiveBaseName(archivePath));
                Directory.CreateDirectory(subDir);

                await Task.Run(() =>
                {
                    ExtractToDirectoryInternal(archivePath, subDir, null, null, token);
                }, token);

                progress?.Report((double)(i + 1) / archiveFiles.Count * 50.0);
            }

            var extractedItems = Directory.GetFileSystemEntries(tempExtractRoot).ToList();
            if (extractedItems.Count == 0)
            {
                throw new InvalidOperationException("Kaynak arşiv boş olduğu için dönüştürülemedi.");
            }

            string baseName = archiveFiles.Count == 1
                ? GetCleanArchiveBaseName(archiveFiles[0])
                : "Donusturulen_Arsiv";

            return targetFormat switch
            {
                "7z" => await Create7zFromExtractedAsync(extractedItems, baseName, transaction, progress, token),
                "tar_gz" => await CreateTarGzFromExtractedAsync(extractedItems, baseName, transaction, progress, token),
                _ => await CreateZipFromExtractedAsync(extractedItems, baseName, transaction, progress, token)
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempExtractRoot))
                {
                    Directory.Delete(tempExtractRoot, recursive: true);
                }
            }
            catch
            {
                // Geçici klasör temizleme hatasını yoksay
            }
        }
    }

    private async Task<string> CreateZipFromExtractedAsync(
        IReadOnlyList<string> items,
        string baseName,
        ExportTransaction transaction,
        IProgress<double>? progress,
        CancellationToken token)
    {
        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + ".zip");
        await Task.Run(() =>
        {
            using var zipStream = new FileStream(outputPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string item = items[i];
                if (File.Exists(item))
                {
                    string entryName = GetUniqueArchiveEntryName(Path.GetFileName(item), usedNames);
                    archive.CreateEntryFromFile(item, entryName, CompressionLevel.Optimal);
                }
                else if (Directory.Exists(item))
                {
                    AddDirectoryToZip(archive, item, Path.GetFileName(item), usedNames);
                }
                progress?.Report(50.0 + ((double)(i + 1) / items.Count * 50.0));
            }
        }, token);
        return outputPath;
    }

    private async Task<string> CreateTarGzFromExtractedAsync(
        IReadOnlyList<string> items,
        string baseName,
        ExportTransaction transaction,
        IProgress<double>? progress,
        CancellationToken token)
    {
        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + ".tar.gz");
        await Task.Run(() =>
        {
            using var stream = File.OpenWrite(outputPath);
            var writerOptions = new WriterOptions(CompressionType.GZip) { LeaveStreamOpen = false };
            using var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, writerOptions);

            for (int i = 0; i < items.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string item = items[i];
                if (File.Exists(item))
                {
                    writer.Write(Path.GetFileName(item), item);
                }
                else if (Directory.Exists(item))
                {
                    string prefix = Path.GetFileName(item);
                    foreach (var subFile in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        string rel = Path.Combine(prefix, Path.GetRelativePath(item, subFile)).Replace('\\', '/');
                        writer.Write(rel, subFile);
                    }
                }
                progress?.Report(50.0 + ((double)(i + 1) / items.Count * 50.0));
            }
        }, token);
        return outputPath;
    }

    private async Task<string> Create7zFromExtractedAsync(
        IReadOnlyList<string> items,
        string baseName,
        ExportTransaction transaction,
        IProgress<double>? progress,
        CancellationToken token)
    {
        string? sevenZipExe = Resolve7ZipPath();
        if (sevenZipExe == null)
        {
            throw new FileNotFoundException("7-Zip (7z.exe) sistemde bulunamadı.");
        }

        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, baseName + ".7z");
        string quotedFiles = string.Join(" ", items.Select(f => $"\"{f}\""));
        string args = $"a -t7z -mx=7 -y \"{outputPath}\" {quotedFiles}";

        var result = await ProcessRunner.RunAsync(sevenZipExe, args, token: token);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"7-Zip arşivleme hatası ({result.ExitCode}): {result.StandardError}");
        }
        progress?.Report(100.0);
        return outputPath;
    }

    /// <summary>
    /// Seçili dosya ve klasörleri yeni bir klasör altında toplar.
    /// </summary>
    public async Task<string> CollectToFolderAsync(
        IReadOnlyList<string> files,
        ExportTransaction transaction,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string folderName = $"Toplanan_Dosyalar_{DateTime.Now:yyyyMMdd_HHmm}";
        string targetFolder = SafePaths.GetUniquePath(transaction.StagingDirectory, folderName);
        Directory.CreateDirectory(targetFolder);

        await Task.Run(() =>
        {
            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string item = files[i];
                if (File.Exists(item))
                {
                    string destFile = SafePaths.GetUniquePath(targetFolder, Path.GetFileName(item));
                    File.Copy(item, destFile, overwrite: true);
                }
                else if (Directory.Exists(item))
                {
                    string dirName = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string destSubDir = SafePaths.GetUniquePath(targetFolder, dirName);
                    CopyDirectoryRecursive(item, destSubDir, token);
                }

                progress?.Report((double)(i + 1) / files.Count * 100.0);
            }
        }, token);

        return targetFolder;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir, CancellationToken token)
    {
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, file);
            File.Copy(file, Path.Combine(destDir, rel), overwrite: true);
        }
    }

    private static string GetCleanArchiveBaseName(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^7];
        }
        return Path.GetFileNameWithoutExtension(name);
    }

    private static string GetUniqueArchiveEntryName(string desiredName, HashSet<string> usedNames)
    {
        if (usedNames.Add(desiredName)) return desiredName;

        string nameNoExt = Path.GetFileNameWithoutExtension(desiredName);
        string ext = Path.GetExtension(desiredName);
        int counter = 1;
        string candidate;
        do
        {
            candidate = $"{nameNoExt} ({counter}){ext}";
            counter++;
        } while (!usedNames.Add(candidate));

        return candidate;
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryPrefix, HashSet<string> usedNames)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string entryName = Path.Combine(entryPrefix, relative).Replace('\\', '/');
            if (usedNames.Add(entryName))
            {
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }
    }
}
