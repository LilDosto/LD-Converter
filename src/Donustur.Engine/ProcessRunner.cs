using System.Diagnostics;
using System.Text;

namespace Donustur.Engine;

public static class ProcessRunner
{
    public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Harici CLI araçlarını (FFmpeg, LibreOffice vb.) UI'ı dondurmadan,
    /// CancellationToken ile süreç ağacını sonlandırabilen güvenli çalıştırıcı.
    /// </summary>
    public static async Task<ProcessOutput> RunAsync(
        string exePath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? outputProgress = null,
        CancellationToken token = default)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Çalıştırılacak araç bulunamadı: {exePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exePath) ?? string.Empty,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        long lastReportTicks = 0;

        void ReportThrottled(string line)
        {
            if (outputProgress == null) return;
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref lastReportTicks) >= 60) // En fazla ~16 FPS raporlama (WPF Dispatcher koruması)
            {
                Interlocked.Exchange(ref lastReportTicks, now);
                outputProgress.Report(line);
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stdOut)
                {
                    stdOut.AppendLine(e.Data);
                }
                ReportThrottled(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stdErr)
                {
                    stdErr.AppendLine(e.Data);
                }
                ReportThrottled(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // CancellationToken ile işlem iptal edilirse süreci ve alt süreçlerini zorla sonlandır
        using (token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Süreç zaten kapanmış olabilir
            }
        }))
        {
            await process.WaitForExitAsync(token);
        }

        return new ProcessOutput(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
