using System.IO;
using System.Windows;
using System.Windows.Threading;
using Donustur.UI.Services;

namespace Donustur.UI.Windows;

public partial class ProgressToastWindow : Window
{
    private static readonly List<ProgressToastWindow> ActiveToasts = new();

    private string? _firstOutputFile;
    private string? _copiedPayload;
    private bool _isCompletedOrFailed;
    private readonly Action? _onCancelRequested;
    private readonly DispatcherTimer _autoCloseTimer;

    public ProgressToastWindow(string initialTitle, Action? onCancelRequested = null)
    {
        InitializeComponent();
        Icon = AppIconProvider.GetWindowIcon();
        TitleText.Text = initialTitle;
        _onCancelRequested = onCancelRequested;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
        _autoCloseTimer.Tick += (_, _) => Close();

        Loaded += (_, _) =>
        {
            if (!ActiveToasts.Contains(this))
            {
                ActiveToasts.Add(this);
            }
            RepositionAllToasts();
        };

        Closed += (_, _) =>
        {
            _autoCloseTimer.Stop();
            ActiveToasts.Remove(this);
            RepositionAllToasts();
        };
    }

    private static void RepositionAllToasts()
    {
        double workAreaRight = SystemParameters.WorkArea.Right;
        double workAreaBottom = SystemParameters.WorkArea.Bottom;

        for (int i = 0; i < ActiveToasts.Count; i++)
        {
            var toast = ActiveToasts[i];
            toast.Left = workAreaRight - toast.Width - 15;
            toast.Top = workAreaBottom - ((i + 1) * (toast.Height - 5)) - 10;
        }
    }

    private static readonly System.Windows.Media.SolidColorBrush CompletedGreenBrush;

    static ProgressToastWindow()
    {
        CompletedGreenBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
        CompletedGreenBrush.Freeze();
    }

    public void UpdateProgress(double percent, string? status = null)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        void Apply()
        {
            ProgressBar.Value = percent;
            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText.Text = status;
            }
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Apply);
        }
    }

    public void SetCompleted(IReadOnlyList<string> outputFiles, string? decodedText = null)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        void Apply()
        {
            _isCompletedOrFailed = true;
            ProgressBar.Value = 100;
            ProgressBar.Foreground = CompletedGreenBrush;
            TitleText.Text = "✓ Dönüştürme Tamamlandı";

            if (!string.IsNullOrWhiteSpace(decodedText))
            {
                _copiedPayload = decodedText;
                try { Clipboard.SetText(decodedText); } catch { }
                StatusText.Text = $"QR: {decodedText} (Panoya kopyalandı)";
                CopyTextButton.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = outputFiles.Count == 1
                    ? Path.GetFileName(outputFiles[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : $"{outputFiles.Count} öğe hazırlandı";
            }

            if (outputFiles.Count > 0)
            {
                _firstOutputFile = outputFiles[0];
                ActionPanel.Visibility = Visibility.Visible;
            }

            _autoCloseTimer.Start();
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Apply);
        }
    }

    public void SetFailed(string error)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        void Apply()
        {
            _isCompletedOrFailed = true;
            TitleText.Text = "✕ İşlem Tamamlanamadı";
            TitleText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Text = error;
            ProgressBar.Visibility = Visibility.Collapsed;
            _autoCloseTimer.Start();
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Apply);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_isCompletedOrFailed)
        {
            _onCancelRequested?.Invoke();
        }
        Close();
    }

    private void OnCopyTextClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_copiedPayload))
        {
            try
            {
                Clipboard.SetText(_copiedPayload);
                CopyTextButton.Content = "✓ Kopyalandı";
            }
            catch
            {
                // Pano meşgulse sessizce geç
            }
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_firstOutputFile))
        {
            DesktopGeometry.RevealInExplorer(_firstOutputFile);
        }
        Close();
    }
}
