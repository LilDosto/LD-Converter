using System.Diagnostics;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;

namespace Donustur.Engine.Documents;

/// <summary>
/// PDF belgelerindeki matematiksel denklemleri, karmaşık formülleri (kesirler, türevler,
/// indisler, alt parantezler ve semboller) görsel ve yapısal olarak koruyarak
/// Word (.docx) formatına aktaran akıllı formül koruma motoru.
/// </summary>
public static class PdfMathEngine
{
    public static string? ResolveEngineExePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] candidates =
        {
            Path.Combine(baseDir, "tools", "ld_pdf_math_engine.exe"),
            Path.Combine(baseDir, "ld_pdf_math_engine.exe"),
            Path.Combine(localAppData, "LDConverter", "tools", "ld_pdf_math_engine.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "ld_pdf_math_engine.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "ld_pdf_math_engine.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public static bool IsAvailable => ResolveEngineExePath() != null;

    /// <summary>
    /// PDF belgesini matematik formüllerini bozmadan DOCX formatına dönüştürmeyi dener.
    /// Başarılı olursa oluşturulan .docx dosya yolunu döner, aksi halde null döner.
    /// </summary>
    public static async Task<string?> TryConvertAsync(
        string inputPdf,
        string targetDocxPath,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        try
        {
            progress?.Report(15.0);

            // 1. Bağımsız yüksek sadakatli formül motoru (ld_pdf_math_engine.exe)
            string? engineExe = ResolveEngineExePath();
            if (engineExe != null)
            {
                string args = $"\"{inputPdf}\" \"{targetDocxPath}\"";
                var result = await ProcessRunner.RunAsync(engineExe, args, timeout: TimeSpan.FromMinutes(5), token: token);
                if (result.ExitCode == 0 && File.Exists(targetDocxPath) && new FileInfo(targetDocxPath).Length > 0)
                {
                    progress?.Report(100.0);
                    return targetDocxPath;
                }

                // Tanılama amacıyla hata logunu kaydet
                Debug.WriteLine($"[PdfMathEngine] ld_pdf_math_engine exit code: {result.ExitCode}, stderr: {result.StandardError}");
            }

            // 2. Python ve script alternatifi
            string baseDir = AppContext.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "tools", "cli_math_engine.py");
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "cli_math_engine.py");
            }
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "cli_math_engine.py");
            }

            if (File.Exists(scriptPath))
            {
                string? pythonPath = ResolvePythonPath();
                if (pythonPath != null)
                {
                    string pyArgs = $"\"{scriptPath}\" \"{inputPdf}\" \"{targetDocxPath}\"";
                    var pyResult = await ProcessRunner.RunAsync(pythonPath, pyArgs, timeout: TimeSpan.FromMinutes(5), token: token);
                    if (pyResult.ExitCode == 0 && File.Exists(targetDocxPath) && new FileInfo(targetDocxPath).Length > 0)
                    {
                        progress?.Report(100.0);
                        return targetDocxPath;
                    }
                    Debug.WriteLine($"[PdfMathEngine] Python script exit code: {pyResult.ExitCode}, stderr: {pyResult.StandardError}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PdfMathEngine] Exception: {ex.Message}");
        }

        return null;
    }

    private static string? ResolvePythonPath()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;
                string candidate = Path.Combine(segment.Trim(), "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
