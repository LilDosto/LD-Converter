using System.Diagnostics;
using System.Text;

namespace Donustur.Engine;

public static class ProcessRunner
{
    public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Harici CLI araçlarını (FFmpeg, LibreOffice, Pandoc, Python vb.) UI'ı dondurmadan,
    /// zaman aşımı korumalı ve CancellationToken ile süreç ağacını güvenli sonlandırabilen çalıştırıcı.
    /// </summary>
    public static async Task<ProcessOutput> RunAsync(
        string exePath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? outputProgress = null,
        TimeSpan? timeout = null,
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
            // En fazla ~16 FPS raporlama (WPF Dispatcher kuyruğunun şişmesini önleme)
            if (now - Interlocked.Read(ref lastReportTicks) >= 60)
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

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            linkedCts.CancelAfter(timeout.Value);
        }

        var effectiveToken = linkedCts.Token;

        try
        {
            using (effectiveToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // Alt süreçleriyle (process tree) birlikte derhal zorla kapat (Zombi süreç önleme)
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Süreç zaten kapanmış olabilir
                }
            }))
            {
                await process.WaitForExitAsync(effectiveToken);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Kullanıcı tarafından iptal edildi
            throw;
        }
        catch (OperationCanceledException) when (timeout.HasValue && linkedCts.IsCancellationRequested)
        {
            throw new TimeoutException($"CLI işlemi zaman aşımına uğradı ({timeout.Value.TotalSeconds:F0} sn): {Path.GetFileName(exePath)}");
        }

        // KRİTİK DÜZELTME: .NET'te WaitForExitAsync, asenkron BeginOutput/ErrorReadLine akışlarının
        // tamponlarını boşaltmasını beklemez. Standart WaitForExit() çağrılarak tüm logların
        // eksiksiz StringBuilder'a aktarılması garanti altına alınır (Yarış durumu & log kesilmesi önleme).
        try
        {
            process.WaitForExit();
        }
        catch
        {
            // İşlem tamamlandıktan sonra oluşabilecek istisnaları yut
        }

        return new ProcessOutput(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
