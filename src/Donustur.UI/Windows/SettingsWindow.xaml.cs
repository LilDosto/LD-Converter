using System.Windows;
using System.Windows.Controls;
using Donustur.Core.Services;
using Donustur.Engine.Archives;
using Donustur.Engine.Documents;
using Donustur.Engine.Media;
using Donustur.UI.Services;
using Microsoft.Win32;

namespace Donustur.UI.Windows;

public partial class SettingsWindow : Window
{
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LDConverter";

    private readonly PreferencesStore _preferencesStore;
    private string? _customPandocPath;

    public SettingsWindow(PreferencesStore preferencesStore)
    {
        InitializeComponent();
        _preferencesStore = preferencesStore;
        Icon = AppIconProvider.GetWindowIcon();

        DwmBackdrop.ApplyFluentBackdrop(this, DwmBackdrop.BackdropType.MainWindow, darkMode: true);

        Loaded += (_, _) => LoadValues();
    }

    private void LoadValues()
    {
        var prefs = _preferencesStore.Current;
        _customPandocPath = prefs.CustomPandocPath;

        EnableWheelCheck.IsChecked = prefs.Enabled;

        // Modifier
        foreach (ComboBoxItem item in ModifierCombo.Items)
        {
            if (string.Equals(item.Content.ToString(), prefs.DragModifier, StringComparison.OrdinalIgnoreCase))
            {
                ModifierCombo.SelectedItem = item;
                break;
            }
        }
        if (ModifierCombo.SelectedItem == null) ModifierCombo.SelectedIndex = 0;

        ScaleSlider.Value = Math.Clamp(prefs.WheelScale, 0.8, 1.4);
        ScaleValueLabel.Text = $"{ScaleSlider.Value:0.00}x";

        QualitySlider.Value = Math.Clamp(prefs.DefaultImageQuality, 20, 100);
        QualityValueLabel.Text = $"%{QualitySlider.Value:F0}";

        SoundCheck.IsChecked = prefs.SoundEffectsEnabled;
        MinimizeToTrayCheck.IsChecked = prefs.MinimizeToTray;
        AutoOpenCheck.IsChecked = prefs.AutoOpenOutputFolder;
        RunAtStartupCheck.IsChecked = IsStartupEnabled();

        // Output mode
        OutputModeCombo.SelectedIndex = prefs.OutputDirectoryMode switch
        {
            "Desktop" => 1,
            "Custom" => 2,
            _ => 0
        };

        CustomFolderText.Text = prefs.CustomOutputDirectory;
        CustomFolderPanel.Visibility = OutputModeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        // Harici Motor Kontrolleri (UI thread'i kilitlememesi için arka planda tara)
        LibreOfficeStatusText.Text = "ℹ Kontrol ediliyor...";
        FfmpegStatusText.Text = "ℹ Kontrol ediliyor...";
        PandocStatusText.Text = "ℹ Kontrol ediliyor...";
        NativeEnginesStatusText.Text = "✓ Magick.NET, PdfPig ve SharpCompress hazır";

        Task.Run(() =>
        {
            var officeEngine = new OfficeDocumentEngine(prefs.CustomLibreOfficePath, _customPandocPath);
            bool officeAvailable = officeEngine.IsAvailable;
            string? sofficePath = officeAvailable ? officeEngine.ResolveSofficePath() : null;

            var mediaEngine = new MediaEngine(prefs.CustomFfmpegPath);
            bool mediaAvailable = mediaEngine.IsAvailable;
            string? ffmpegPath = mediaAvailable ? mediaEngine.ResolveFfmpegPath() : null;

            var pandocEngine = new PandocEngine(_customPandocPath);
            bool pandocAvailable = pandocEngine.IsAvailable;
            string? pandocPath = pandocAvailable ? pandocEngine.ResolvePandocPath() : null;
            string? pdfEngine = pandocAvailable ? pandocEngine.ResolvePdfEngine() : null;

            string? sevenZip = ArchiveEngine.Resolve7ZipPath();

            Dispatcher.BeginInvoke(() =>
            {
                if (officeAvailable)
                {
                    LibreOfficeStatusText.Text = $"✓ Hazır ({sofficePath})";
                    LibreOfficeStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    LibreOfficeStatusText.Text = "ℹ Kurulu değil (TXT/DOCX/HTML/PDF yerel motoru aktif; PPTX/XLSX için LibreOffice seçilebilir)";
                    LibreOfficeStatusText.Foreground = System.Windows.Media.Brushes.Khaki;
                }

                if (mediaAvailable)
                {
                    FfmpegStatusText.Text = $"✓ Hazır ({ffmpegPath})";
                    FfmpegStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    InstallFfmpegBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    FfmpegStatusText.Text = "✕ Bulunamadı (Video & ses dönüşümleri için FFmpeg gereklidir)";
                    FfmpegStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
                    InstallFfmpegBtn.Visibility = Visibility.Visible;
                }

                if (pandocAvailable)
                {
                    string engineInfo = pdfEngine != null ? $" [PDF Motoru: {pdfEngine}]" : "";
                    PandocStatusText.Text = $"✓ Hazır ({pandocPath}){engineInfo}";
                    PandocStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    InstallPandocBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PandocStatusText.Text = "ℹ Kurulu değil (Markdown & LaTeX denklem dönüşümleri için kurabilirsiniz)";
                    PandocStatusText.Foreground = System.Windows.Media.Brushes.Khaki;
                    InstallPandocBtn.Visibility = Visibility.Visible;
                }

                NativeEnginesStatusText.Text = sevenZip != null
                    ? $"✓ Magick.NET, PdfPig, SharpCompress ve 7-Zip ({sevenZip}) hazır"
                    : "✓ Magick.NET, PdfPig ve SharpCompress hazır (ZIP, TAR.GZ, Görsel, PDF, DOCX, QR)";
            });
        });
    }

    private void OnScaleSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValueLabel != null)
        {
            ScaleValueLabel.Text = $"{e.NewValue:0.00}x";
        }
    }

    private void OnQualitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueLabel != null)
        {
            QualityValueLabel.Text = $"%{e.NewValue:F0}";
        }
    }

    private void OnOutputModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomFolderPanel != null)
        {
            CustomFolderPanel.Visibility = OutputModeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Dönüştürülen Dosyaların Kaydedileceği Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            CustomFolderText.Text = dlg.FolderName;
        }
    }

    private void OnBrowseLibreOfficeClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "soffice.exe|soffice.exe|Tüm Dosyalar (*.*)|*.*",
            Title = "LibreOffice soffice.exe dosyasını seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            var prefs = _preferencesStore.Current;
            prefs.CustomLibreOfficePath = dlg.FileName;
            _preferencesStore.Save(prefs);
            LoadValues();
        }
    }

    private void OnBrowseFfmpegClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ffmpeg.exe|ffmpeg.exe|Tüm Dosyalar (*.*)|*.*",
            Title = "FFmpeg ffmpeg.exe dosyasını seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            var prefs = _preferencesStore.Current;
            prefs.CustomFfmpegPath = dlg.FileName;
            _preferencesStore.Save(prefs);
            LoadValues();
        }
    }

    private async void OnInstallFfmpegClick(object sender, RoutedEventArgs e)
    {
        InstallFfmpegBtn.IsEnabled = false;
        FfmpegStatusText.Text = "📥 FFmpeg indiriliyor ve kuruluyor...";
        FfmpegStatusText.Foreground = System.Windows.Media.Brushes.Khaki;

        var progress = new Progress<string>(msg =>
        {
            Dispatcher.BeginInvoke(() => FfmpegStatusText.Text = msg);
        });

        var mediaEngine = new MediaEngine(_preferencesStore.Current.CustomFfmpegPath);
        string? result = await FfmpegInstaller.EnsureOrInstallFfmpegAsync(mediaEngine, progress);

        if (result != null)
        {
            var prefs = _preferencesStore.Current;
            prefs.CustomFfmpegPath = result;
            _preferencesStore.Save(prefs);
        }

        InstallFfmpegBtn.IsEnabled = true;
        LoadValues();
    }

    private void OnBrowsePandocClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "pandoc.exe|pandoc.exe|Tüm Dosyalar (*.*)|*.*",
            Title = "Pandoc pandoc.exe dosyasını seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            _customPandocPath = dlg.FileName;
            var prefs = _preferencesStore.Current;
            prefs.CustomPandocPath = dlg.FileName;
            _preferencesStore.Save(prefs);
            LoadValues();
        }
    }

    private async void OnInstallPandocClick(object sender, RoutedEventArgs e)
    {
        InstallPandocBtn.IsEnabled = false;
        PandocStatusText.Text = "📥 Pandoc indiriliyor ve kuruluyor...";
        PandocStatusText.Foreground = System.Windows.Media.Brushes.Khaki;

        var progress = new Progress<string>(msg =>
        {
            Dispatcher.BeginInvoke(() => PandocStatusText.Text = msg);
        });

        var pandocEngine = new PandocEngine(_customPandocPath);
        string? result = await PandocInstaller.EnsureOrInstallPandocAsync(pandocEngine, progress);

        if (result != null)
        {
            _customPandocPath = result;
            var prefs = _preferencesStore.Current;
            prefs.CustomPandocPath = result;
            _preferencesStore.Save(prefs);
        }

        InstallPandocBtn.IsEnabled = true;
        LoadValues();
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        SetStartupEnabled(RunAtStartupCheck.IsChecked == true);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? "";
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch
        {
            // İzin hatası vb.
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var prefs = _preferencesStore.Current;

        prefs.Enabled = EnableWheelCheck.IsChecked == true;
        prefs.DragModifier = (ModifierCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Shift";
        prefs.WheelScale = Math.Round(ScaleSlider.Value, 2);
        prefs.DefaultImageQuality = (int)Math.Round(QualitySlider.Value);
        prefs.SoundEffectsEnabled = SoundCheck.IsChecked == true;
        prefs.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        prefs.AutoOpenOutputFolder = AutoOpenCheck.IsChecked == true;
        prefs.RunAtStartup = RunAtStartupCheck.IsChecked == true;
        prefs.CustomPandocPath = _customPandocPath;

        prefs.OutputDirectoryMode = OutputModeCombo.SelectedIndex switch
        {
            1 => "Desktop",
            2 => "Custom",
            _ => "SameAsSource"
        };
        prefs.CustomOutputDirectory = CustomFolderText.Text.Trim();

        _preferencesStore.Save(prefs);
        Close();
    }
}
