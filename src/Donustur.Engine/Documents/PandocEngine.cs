using System.Diagnostics;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;

namespace Donustur.Engine.Documents;

public sealed class PandocEngine
{
    private string? _customPath;

    public PandocEngine(string? customPath = null)
    {
        _customPath = customPath;
    }

    public void SetCustomPath(string? customPath)
    {
        _customPath = customPath;
    }

    public string? ResolvePandocPath()
    {
        if (!string.IsNullOrWhiteSpace(_customPath) && File.Exists(_customPath))
        {
            return _customPath;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidatePaths =
        {
            Path.Combine(progFiles, @"Pandoc\pandoc.exe"),
            Path.Combine(progFilesX86, @"Pandoc\pandoc.exe"),
            Path.Combine(localAppData, @"Pandoc\pandoc.exe"),
            Path.Combine(localAppData, @"Programs\Pandoc\pandoc.exe"),
            Path.Combine(localAppData, @"Microsoft\WinGet\Links\pandoc.exe")
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // WinGet paket klasörünü dinamik tara
        string wingetPackages = Path.Combine(localAppData, @"Microsoft\WinGet\Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                foreach (var pkgDir in Directory.EnumerateDirectories(wingetPackages))
                {
                    if (Path.GetFileName(pkgDir).Contains("pandoc", StringComparison.OrdinalIgnoreCase))
                    {
                        string direct = Path.Combine(pkgDir, "pandoc.exe");
                        if (File.Exists(direct)) return direct;

                        string inBin = Path.Combine(pkgDir, "bin", "pandoc.exe");
                        if (File.Exists(inBin)) return inBin;
                    }
                }
            }
            catch
            {
                // Erişim hatasında devam et
            }
        }

        // PATH çevre değişkenlerini Process, User ve Machine seviyelerinde kontrol et
        var pathTargets = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine };
        foreach (var target in pathTargets)
        {
            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH", target);
                if (pathEnv != null)
                {
                    foreach (var segment in pathEnv.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrWhiteSpace(segment)) continue;
                        string combined = Path.Combine(segment.Trim(), "pandoc.exe");
                        if (File.Exists(combined))
                            return combined;
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
        }

        return null;
    }

    /// <summary>
    /// Sistemde xelatex, pdflatex veya alternatif bir pdf-engine motoru olup olmadığını tespit eder.
    /// TinyTeX, MiKTeX ve sistem PATH yollarını öncelikli olarak tarar.
    /// </summary>
    public string? ResolvePdfEngine()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] specificPaths =
        {
            Path.Combine(appData, @"TinyTeX\bin\windows\xelatex.exe"),
            Path.Combine(progFiles, @"MiKTeX\miktex\bin\x64\xelatex.exe"),
            Path.Combine(progFilesX86, @"MiKTeX\miktex\bin\xelatex.exe"),
            Path.Combine(localAppData, @"Programs\MiKTeX\miktex\bin\x64\xelatex.exe"),
            Path.Combine(appData, @"TinyTeX\bin\windows\pdflatex.exe")
        };

        foreach (var p in specificPaths)
        {
            if (File.Exists(p)) return p;
        }

        string[] candidates = { "xelatex", "pdflatex", "lualatex", "typst", "weasyprint", "wkhtmltopdf" };
        foreach (var eng in candidates)
        {
            string? foundInPath = FindExecutableInPath(eng + ".exe");
            if (foundInPath != null)
            {
                return foundInPath;
            }
        }
        return null;
    }

    private static string? FindExecutableInPath(string exeName)
    {
        var pathTargets = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine };
        foreach (var target in pathTargets)
        {
            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH", target);
                if (pathEnv != null)
                {
                    foreach (var segment in pathEnv.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrWhiteSpace(segment)) continue;
                        string combined = Path.Combine(segment.Trim(), exeName);
                        if (File.Exists(combined))
                            return combined;
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
        }
        return null;
    }

    public bool IsAvailable => ResolvePandocPath() != null;

    /// <summary>
    /// Pandoc kullanarak Markdown veya ofis belgesini yüksek sadakatle dönüştürür.
    /// Matematik denklemleri ($...$, $$...$$) için xelatex ve standart LaTeX desteği sağlar.
    /// </summary>
    public async Task<string?> ConvertAsync(
        string inputFile,
        string targetFormat,
        ExportTransaction transaction,
        CancellationToken token = default)
    {
        string? pandocPath = ResolvePandocPath();
        if (pandocPath == null) return null;

        string formatKey = targetFormat.ToLowerInvariant().TrimStart('.');
        string baseName = Path.GetFileNameWithoutExtension(inputFile);
        string outputPath = SafePaths.GetUniquePath(transaction.StagingDirectory, $"{baseName}.{formatKey}");

        string arguments;
        string? pdfEngine = ResolvePdfEngine();

        if (formatKey == "pdf")
        {
            // Kullanıcının paylaştığı kusursuz parametreler:
            // pandoc "belge.md" -o "belge.pdf" --pdf-engine=xelatex -V mainfont="Arial" -V geometry:margin=2.5cm
            string engineParam = pdfEngine != null ? $"--pdf-engine=\"{pdfEngine}\"" : "";
            arguments = $"\"{inputFile}\" -o \"{outputPath}\" {engineParam} -V mainfont=\"Arial\" -V geometry:margin=2.5cm".Trim();
        }
        else
        {
            arguments = $"\"{inputFile}\" -o \"{outputPath}\"";
        }

        var result = await ProcessRunner.RunAsync(pandocPath, arguments, token: token);
        if (result.ExitCode == 0 && File.Exists(outputPath))
        {
            return outputPath;
        }

        // Eğer xelatex ile denendi ve başarısız olduysa, varsayılan pandoc parametresiyle tekrar dene
        if (formatKey == "pdf" && !string.IsNullOrEmpty(pdfEngine))
        {
            string fallbackArgs = $"\"{inputFile}\" -o \"{outputPath}\" -V geometry:margin=2.5cm";
            var fallbackResult = await ProcessRunner.RunAsync(pandocPath, fallbackArgs, token: token);
            if (fallbackResult.ExitCode == 0 && File.Exists(outputPath))
            {
                return outputPath;
            }
        }

        return null;
    }
}
