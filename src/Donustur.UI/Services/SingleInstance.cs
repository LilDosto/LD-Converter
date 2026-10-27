using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Donustur.UI.Services;

public sealed class SingleInstance : IDisposable
{
    private const string PipeName = "LDConverter_App_Pipe";
    private const string MutexName = @"Local\LDConverter_SingleInstance_Mutex_v1";
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _pipeCts;

    public bool IsPrimaryInstance => _ownsMutex;

    public event Action? ShowRequestReceived;
    public event Action<IReadOnlyList<string>>? FilesReceived;

    public SingleInstance()
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            // Önceki süreç beklenmedik şekilde kapandıysa kilidi devral
            _ownsMutex = true;
        }
    }

    public void StartListening()
    {
        if (!_ownsMutex) return;

        _pipeCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_pipeCts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync(_pipeCts.Token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? msg = await reader.ReadLineAsync(_pipeCts.Token);
                    if (string.IsNullOrWhiteSpace(msg)) continue;

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (msg.StartsWith("FILES|", StringComparison.Ordinal))
                        {
                            var files = msg["FILES|".Length..]
                                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                                .Where(p => File.Exists(p) || Directory.Exists(p))
                                .ToList();

                            if (files.Count > 0)
                            {
                                FilesReceived?.Invoke(files);
                            }
                        }
                        ShowRequestReceived?.Invoke();
                    });
                }
                catch
                {
                    // Bağlantı sıfırlanmasında devam et
                }
            }
        });
    }

    public static void NotifyAndFocusPrimaryInstance(string[]? args = null)
    {
        try
        {
            // Mevcut örneğin penceresine odak izni ver ve öne getir
            using var currentProc = Process.GetCurrentProcess();
            var otherProcs = Process.GetProcessesByName(currentProc.ProcessName)
                .Where(p => p.Id != currentProc.Id);

            foreach (var proc in otherProcs)
            {
                try
                {
                    AllowSetForegroundWindow(proc.Id);
                    IntPtr hWnd = proc.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }
                        SetForegroundWindow(hWnd);
                    }
                }
                catch
                {
                    // Erişim hatasını yoksay
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Süreç tarama hatasını yoksay
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client, Encoding.UTF8);

            if (args != null && args.Length > 0)
            {
                writer.WriteLine("FILES|" + string.Join("|", args));
            }
            else
            {
                writer.WriteLine("SHOW");
            }
            writer.Flush();
        }
        catch
        {
            // Birincil örnek yanıt vermediyse sessizce geç
        }
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Zaten serbest bırakılmış olabilir
            }
        }
        _mutex.Dispose();
    }
}
