using System.IO;
using System.Windows;
using Donustur.Core.Models;
using Donustur.Core.Services;
using Donustur.Engine;
using Donustur.UI.Services;
using Donustur.UI.Windows;

namespace Donustur.UI;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private PreferencesStore? _preferencesStore;
    private HistoryStore? _historyStore;
    private ConversionDispatcher? _dispatcher;
    private GlobalDragController? _dragController;
    private TrayHost? _trayHost;
    private MainWindow? _mainWindow;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Aynı anda iki kez açılmasını senkron olarak engelle
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsPrimaryInstance)
        {
            SingleInstance.NotifyAndFocusPrimaryInstance(e.Args);
            _singleInstance.Dispose();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n{args.Exception.Message}",
                "LD Converter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        _singleInstance.StartListening();
        _singleInstance.ShowRequestReceived += BringMainWindowToFront;
        _singleInstance.FilesReceived += files =>
        {
            _mainWindow?.SetSelectedFiles(files);
        };

        // Servisleri başlat
        _preferencesStore = new PreferencesStore();
        _historyStore = new HistoryStore();
        _dispatcher = new ConversionDispatcher(_preferencesStore.Current, _historyStore);

        // Global sürükleme ve çark kontrolcüsü
        _dragController = new GlobalDragController(_preferencesStore, HandleWheelDroppedFiles);

        // Sistem Tepsisi (Tray)
        _trayHost = new TrayHost(
            _preferencesStore,
            openMainWindow: BringMainWindowToFront,
            openSettings: OpenSettingsWindow,
            exitApp: ExitApplication);

        _preferencesStore.Changed += _ => _trayHost.UpdateStatus();

        // Ana Pencereyi Oluştur
        _mainWindow = new MainWindow(
            _preferencesStore,
            _historyStore,
            _dispatcher,
            onExitRequested: ExitApplication);

        // Komut satırı argümanları (Dosya veya klasör ile açılmışsa)
        if (e.Args.Length > 0)
        {
            var validPaths = e.Args.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (validPaths.Count > 0)
            {
                _mainWindow.SetSelectedFiles(validPaths);
            }
        }

        _mainWindow.Show();
    }

    private void BringMainWindowToFront()
    {
        if (_mainWindow == null) return;

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Topmost = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void OpenSettingsWindow()
    {
        if (_preferencesStore == null) return;
        var win = new SettingsWindow(_preferencesStore);
        if (_mainWindow != null && _mainWindow.IsVisible)
        {
            win.Owner = _mainWindow;
        }
        win.ShowDialog();
    }

    private async void HandleWheelDroppedFiles(ToolChoice choice, IReadOnlyList<string> files)
    {
        if (_dispatcher == null || _preferencesStore == null || files.Count == 0) return;

        using var cts = new CancellationTokenSource();

        // Kayan bildirim penceresi oluştur
        var toast = new ProgressToastWindow(
            $"{choice.DisplayName} Dönüştürülüyor...",
            onCancelRequested: () => cts.Cancel());
        toast.Show();

        var progress = new Progress<double>(val =>
        {
            toast.UpdateProgress(val, $"{choice.DisplayName} işleniyor (%{val:F0})...");
        });

        var task = new ConversionTask
        {
            InputFiles = files,
            TargetActionId = choice.Id,
            TargetDisplayName = choice.DisplayName,
            Options = new ConversionOptions
            {
                ImageQuality = _preferencesStore.Current.DefaultImageQuality
            }
        };

        var result = await _dispatcher.ExecuteTaskAsync(task, _preferencesStore.Current, progress, cts.Token);

        if (result.Success)
        {
            string? decodedQr = choice.Id == "qr_read" ? task.CurrentStatusMessage : null;
            toast.SetCompleted(result.OutputFiles, decodedQr);

            if (_preferencesStore.Current.SoundEffectsEnabled)
            {
                SoundFeedback.PlaySuccess();
            }

            if (_preferencesStore.Current.AutoOpenOutputFolder && result.OutputFiles.Count > 0)
            {
                DesktopGeometry.RevealInExplorer(result.OutputFiles[0]);
            }
        }
        else
        {
            toast.SetFailed(result.ErrorMessage ?? "İşlem başarısız oldu.");
        }
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        _dragController?.Dispose();
        _dragController = null;

        _trayHost?.Dispose();
        _trayHost = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        Shutdown();
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dragController?.Dispose();
        _trayHost?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
