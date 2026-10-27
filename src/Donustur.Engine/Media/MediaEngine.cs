using Donustur.Core.Models;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;

namespace Donustur.Engine.Media;

public sealed class MediaEngine
{
    private string? _customPath;

    public MediaEngine(string? customPath = null)
    {
        _customPath = customPath;
    }

    public void SetCustomPath(string? customPath)
    {
        _customPath = customPath;
    }

    /// <summary>
    /// FFmpeg çalıştırılabilir dosya yolunu bulur.
    /// </summary>
    public string? ResolveFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(_customPath) && File.Exists(_customPath))
        {
            return _customPath;
        }

        string baseDir = AppContext.BaseDirectory;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidatePaths =
        {
            Path.Combine(baseDir, "tools", "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg.exe"),
            Path.Combine(localAppData, @"Microsoft\WinGet\Links\ffmpeg.exe"),
            Path.Combine(userProfile, @"scoop\shims\ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // WinGet paket klasörlerini dinamik tara
        string wingetPackages = Path.Combine(localAppData, @"Microsoft\WinGet\Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                foreach (var pkgDir in Directory.EnumerateDirectories(wingetPackages, "*FFmpeg*"))
                {
                    var found = Directory.EnumerateFiles(pkgDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null)
                        return found;
                }
            }
            catch
            {
                // Erişim hatasında devam et
            }
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;
                string combined = Path.Combine(segment.Trim(), "ffmpeg.exe");
                if (File.Exists(combined))
                    return combined;
            }
        }

        return null;
    }

    public bool IsAvailable => ResolveFfmpegPath() != null;

    /// <summary>
    /// Video veya ses dosyasını hedef formata dönüştürür.
    /// </summary>
    public async Task<IReadOnlyList<string>> ConvertMediaAsync(
        IReadOnlyList<string> inputFiles,
        string targetFormat,
        ExportTransaction transaction,
        ConversionOptions options,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        string? ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath == null)
        {
            throw new FileNotFoundException(
                "FFmpeg bulunamadı. Medya dönüştürme için FFmpeg gereklidir. Lütfen Ayarlar ekranından ffmpeg.exe yolunu seçin veya 'winget install Gyan.FFmpeg' komutuyla yükleyin.");
        }

        var outputFiles = new List<string>();
        string formatKey = targetFormat.ToLowerInvariant().TrimStart('.');

        for (int i = 0; i < inputFiles.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            string inputFile = inputFiles[i];
            string fileName = Path.GetFileNameWithoutExtension(inputFile);
            string outputExt = GetExtensionForFormat(formatKey, inputFile);
            string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, fileName + outputExt);

            string arguments = BuildArguments(inputFile, outputPath, formatKey, options);

            var result = await ProcessRunner.RunAsync(ffmpegPath, arguments, token: token);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg dönüştürme hatası ({result.ExitCode}): {result.StandardError}");
            }

            if (File.Exists(outputPath))
            {
                outputFiles.Add(outputPath);
            }

            progress?.Report((double)(i + 1) / inputFiles.Count * 100.0);
        }

        return outputFiles;
    }

    private static string GetExtensionForFormat(string formatKey, string inputFile)
    {
        return formatKey switch
        {
            "mp4" => ".mp4",
            "mkv" => ".mkv",
            "webm" => ".webm",
            "mp3" => ".mp3",
            "wav" => ".wav",
            "aac" => ".m4a",
            "flac" => ".flac",
            "ogg" => ".ogg",
            "gif" => ".gif",
            // Sıkıştırma işlemlerinde H.264/MP3 kodekleriyle tam uyumluluk için standart konteyner kullan
            "compress_video" => ".mp4",
            "compress_audio" => ".mp3",
            _ => "." + formatKey
        };
    }

    private static string BuildArguments(string input, string output, string formatKey, ConversionOptions options)
    {
        return formatKey switch
        {
            "mp4" => $"-y -i \"{input}\" -c:v libx264 -preset {options.VideoPreset} -crf {options.VideoCrf} -c:a aac -b:a {options.AudioBitrate} \"{output}\"",
            "mkv" => $"-y -i \"{input}\" -c:v libx264 -preset {options.VideoPreset} -crf {options.VideoCrf} -c:a aac \"{output}\"",
            "webm" => $"-y -i \"{input}\" -c:v libvpx-vp9 -b:v 0 -crf 30 -c:a libopus \"{output}\"",
            "mp3" => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {options.AudioBitrate} \"{output}\"",
            "wav" => $"-y -i \"{input}\" -vn -c:a pcm_s16le \"{output}\"",
            "aac" => $"-y -i \"{input}\" -vn -c:a aac -b:a {options.AudioBitrate} \"{output}\"",
            "flac" => $"-y -i \"{input}\" -vn -c:a flac \"{output}\"",
            "ogg" => $"-y -i \"{input}\" -vn -c:a libvorbis -q:a 5 \"{output}\"",
            "gif" => $"-y -i \"{input}\" -vf \"fps=15,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{output}\"",
            "compress_video" => $"-y -i \"{input}\" -c:v libx264 -crf 28 -preset fast -c:a aac -b:a 128k \"{output}\"",
            "compress_audio" => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a 96k \"{output}\"",
            _ => $"-y -i \"{input}\" \"{output}\""
        };
    }
}
