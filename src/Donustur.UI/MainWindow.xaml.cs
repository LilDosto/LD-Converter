using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Donustur.Core.Catalog;
using Donustur.Core.Models;
using Donustur.Core.Services;
using Donustur.Core.Transactions;
using Donustur.Core.Utils;
using Donustur.Engine;
using Donustur.Engine.Media;
using Donustur.Engine.Qr;
using Donustur.UI.Services;
using Donustur.UI.Windows;
using Microsoft.Win32;

namespace Donustur.UI;

public partial class MainWindow : Window
{
    private readonly PreferencesStore _preferencesStore;
    private readonly HistoryStore _historyStore;
    private readonly ConversionDispatcher _dispatcher;
    private readonly Action? _onExitRequested;

    private List<string> _selectedFiles = new();
    private ToolChoice? _selectedTool;
    private CancellationTokenSource? _activeCts;

    public MainWindow(
        PreferencesStore preferencesStore,
        HistoryStore historyStore,
        ConversionDispatcher dispatcher,
        Action? onExitRequested = null)
    {
        InitializeComponent();
        _preferencesStore = preferencesStore;
        _historyStore = historyStore;
        _dispatcher = dispatcher;
        _onExitRequested = onExitRequested;

        var appIcon = AppIconProvider.GetWindowIcon();
        Icon = appIcon;
        HeaderLogoImage.Source = appIcon;
        DwmBackdrop.ApplyFluentBackdrop(this, DwmBackdrop.BackdropType.MainWindow, darkMode: true);

        UpdateWheelStatusDisplay();
        _preferencesStore.Changed += _ => Dispatcher.Invoke(UpdateWheelStatusDisplay);
        _historyStore.Changed += () => Dispatcher.Invoke(RenderHistory);

        Loaded += (_, _) =>
        {
            RenderHistory();
            CheckAndEnsureAllEnginesOnStartup();
        };
        Closing += OnWindowClosing;
    }

    private void UpdateWheelStatusDisplay()
    {
        bool enabled = _preferencesStore.Current.Enabled;
        string mod = _preferencesStore.Current.DragModifier;
        WheelStatusLed.Fill = enabled
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
            : new SolidColorBrush(Color.FromRgb(156, 163, 175));
        WheelStatusLabel.Text = enabled ? $"Çark: {mod} (Aktif)" : "Çark: Kapalı";
    }

    private void CheckAndEnsureAllEnginesOnStartup()
    {
        _ = Task.Run(async () =>
        {
            var status = AllEnginesManager.GetCurrentStatus(
                _dispatcher.Media,
                _dispatcher.Archive,
                _dispatcher.Pandoc,
                _dispatcher.Office);

            if (status.AllInstalled)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    FooterStatusLabel.Text = "✓ Tüm dönüştürme motorları (FFmpeg, 7-Zip, Pandoc, LibreOffice) hazır.";
                });
                return;
            }

            var missing = new List<string>();
            if (!status.FfmpegAvailable) missing.Add("FFmpeg");
            if (!status.SevenZipAvailable) missing.Add("7-Zip");
            if (!status.PandocAvailable) missing.Add("Pandoc");
            if (!status.LibreOfficeAvailable) missing.Add("LibreOffice");

            _ = Dispatcher.BeginInvoke(() =>
            {
                FooterStatusLabel.Text = $"ℹ Eksik motorlar taranıyor ({string.Join(", ", missing)})...";
            });

            var progress = new Progress<string>(msg =>
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    FooterStatusLabel.Text = $"📥 {msg}";
                });
            });

            var finalStatus = await AllEnginesManager.EnsureAllEnginesAsync(
                _dispatcher.Media,
                _dispatcher.Archive,
                _dispatcher.Pandoc,
                _dispatcher.Office,
                progress);

            _ = Dispatcher.BeginInvoke(() =>
            {
                if (finalStatus.FfmpegAvailable && string.IsNullOrWhiteSpace(_preferencesStore.Current.CustomFfmpegPath))
                {
                    var prefs = _preferencesStore.Current;
                    prefs.CustomFfmpegPath = finalStatus.FfmpegPath;
                    _preferencesStore.Save(prefs);
                }

                if (finalStatus.AllInstalled)
                {
                    FooterStatusLabel.Text = "✓ Tüm motorlar hazır (FFmpeg, 7-Zip, Pandoc, LibreOffice)!";
                }
                else
                {
                    FooterStatusLabel.Text = $"✓ Motor kontrolü tamamlandı ({finalStatus.InstalledCount}/{finalStatus.TotalCount} motor hazır).";
                }
            });
        });
    }

    private void OnToggleWheelClick(object sender, RoutedEventArgs e)
    {
        var prefs = _preferencesStore.Current;
        prefs.Enabled = !prefs.Enabled;
        _preferencesStore.Save(prefs);
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_preferencesStore) { Owner = this };
        win.ShowDialog();
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (ConvertView == null || HistoryView == null || ToolsView == null) return;

        ConvertView.Visibility = TabConvert.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = TabHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ToolsView.Visibility = TabTools.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (TabHistory.IsChecked == true)
        {
            RenderHistory();
        }
    }

    #region Drag & Drop & File Selection In Main Window

    private void OnMainDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241));
            DropZoneBorder.Background = new SolidColorBrush(Color.FromArgb(25, 99, 102, 241));
        }
        e.Handled = true;
    }

    private void OnMainDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMainDragLeave(object sender, DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(19, 23, 34));
    }

    private void OnMainDrop(object sender, DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(19, 23, 34));

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (_selectedFiles.Count > 0)
                {
                    AddFilesToSelection(files);
                }
                else
                {
                    SetSelectedFiles(files);
                }
            }
        }
    }

    private void OnSelectFilesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Dönüştürülecek Dosyaları Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            SetSelectedFiles(dlg.FileNames);
        }
    }

    private void OnSelectFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Multiselect = true,
            Title = "Arşivlenecek veya İşlenecek Klasörü Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FolderNames.Length > 0)
        {
            SetSelectedFiles(dlg.FolderNames);
        }
    }

    private void OnAddMoreFilesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Listeye Eklenecek Dosyaları Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            AddFilesToSelection(dlg.FileNames);
        }
    }

    public void AddFilesToSelection(IEnumerable<string> newFiles)
    {
        var merged = _selectedFiles
            .Concat(newFiles)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SetSelectedFiles(merged);
    }

    public void SetSelectedFiles(IReadOnlyList<string> files)
    {
        _selectedFiles = files
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_selectedFiles.Count == 0)
        {
            OnClearSelectionClick(this, new RoutedEventArgs());
            return;
        }

        if (TabConvert.IsChecked != true)
        {
            TabConvert.IsChecked = true;
        }

        EmptyPrompt.Visibility = Visibility.Collapsed;
        SelectedFilesPanel.Visibility = Visibility.Visible;

        FilesCountLabel.Text = _selectedFiles.Count == 1
            ? Path.GetFileName(_selectedFiles[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : $"{_selectedFiles.Count} öğe seçildi";

        FilesSubLabel.Text = "Boyut hesaplanıyor...";

        RenderSelectedFileItems();
        RebuildFormatChips();

        // Toplam boyutu arka planda hesaplayarak UI'ı anında akıcı tut
        var filesToMeasure = _selectedFiles.ToList();
        Task.Run(() =>
        {
            long totalBytes = FileUtils.CalculateTotalBytes(filesToMeasure);
            Dispatcher.BeginInvoke(() =>
            {
                if (_selectedFiles.Count == filesToMeasure.Count)
                {
                    FilesSubLabel.Text = $"Toplam Boyut: {FileUtils.FormatBytes(totalBytes)}";
                }
            });
        });
    }

    private void RenderSelectedFileItems()
    {
        SelectedFileItemsStack.Children.Clear();
        int maxVisible = 25;
        var displayItems = _selectedFiles.Take(maxVisible).ToList();

        foreach (var filePath in displayItems)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var dock = new DockPanel();

            var removeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Padding = new Thickness(6, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Bu öğeyi listeden çıkar"
            };
            string capturedPath = filePath;
            removeBtn.Click += (_, _) =>
            {
                _selectedFiles.RemoveAll(p => string.Equals(p, capturedPath, StringComparison.OrdinalIgnoreCase));
                SetSelectedFiles(_selectedFiles);
            };
            DockPanel.SetDock(removeBtn, Dock.Right);
            dock.Children.Add(removeBtn);

            bool isDir = Directory.Exists(filePath);
            string sizeStr;
            try
            {
                if (!isDir && File.Exists(filePath))
                {
                    sizeStr = FileUtils.FormatBytes(new FileInfo(filePath).Length);
                }
                else
                {
                    sizeStr = "Klasör";
                }
            }
            catch
            {
                sizeStr = "-";
            }

            var sizeText = new TextBlock
            {
                Text = sizeStr,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0)
            };
            DockPanel.SetDock(sizeText, Dock.Right);
            dock.Children.Add(sizeText);

            string displayName = Path.GetFileName(filePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var nameText = new TextBlock
            {
                Text = (isDir ? "📁 " : "📄 ") + displayName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = filePath
            };
            dock.Children.Add(nameText);

            row.Child = dock;
            SelectedFileItemsStack.Children.Add(row);
        }

        if (_selectedFiles.Count > maxVisible)
        {
            var moreBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 99, 102, 241)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            moreBadge.Child = new TextBlock
            {
                Text = $"+ {_selectedFiles.Count - maxVisible} dosya daha seçili",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 180, 252))
            };
            SelectedFileItemsStack.Children.Add(moreBadge);
        }
    }

    private void RebuildFormatChips()
    {
        string? previousToolId = _selectedTool?.Id;
        FormatChipsPanel.Children.Clear();
        var choices = FormatCatalog.GetChoicesForFiles(_selectedFiles);

        RadioButton? targetRadio = null;
        foreach (var choice in choices)
        {
            var radio = new RadioButton
            {
                Content = choice.DisplayName,
                Tag = choice,
                ToolTip = choice.Description,
                Style = (Style)FindResource("ChipButton")
            };

            radio.Checked += (s, _) =>
            {
                if (s is RadioButton rb && rb.Tag is ToolChoice tc)
                {
                    _selectedTool = tc;
                    SelectedToolDescriptionLabel.Text = tc.Description;
                }
            };

            FormatChipsPanel.Children.Add(radio);
            if (targetRadio == null || string.Equals(choice.Id, previousToolId, StringComparison.OrdinalIgnoreCase))
            {
                targetRadio = radio;
            }
        }

        if (targetRadio != null)
        {
            targetRadio.IsChecked = true;
        }
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selectedFiles.Clear();
        _selectedTool = null;
        SelectedFileItemsStack.Children.Clear();
        SelectedFilesPanel.Visibility = Visibility.Collapsed;
        EmptyPrompt.Visibility = Visibility.Visible;
    }

    private void OnCancelConvertClick(object sender, RoutedEventArgs e)
    {
        _activeCts?.Cancel();
    }

    private async void OnStartConvertClick(object sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count == 0 || _selectedTool == null) return;

        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();

        StartConvertButton.IsEnabled = false;
        ProgressGrid.Visibility = Visibility.Visible;
        MainProgressBar.Value = 0;
        CurrentTaskStatusLabel.Text = $"{_selectedTool.DisplayName} işleniyor...";

        var progress = new Progress<double>(val =>
        {
            MainProgressBar.Value = val;
            CurrentTaskStatusLabel.Text = $"{_selectedTool.DisplayName} işleniyor (%{val:F0})...";
        });

        var task = new ConversionTask
        {
            InputFiles = _selectedFiles.ToList(),
            TargetActionId = _selectedTool.Id,
            TargetDisplayName = _selectedTool.DisplayName,
            Options = new ConversionOptions
            {
                ImageQuality = _preferencesStore.Current.DefaultImageQuality
            }
        };

        var result = await _dispatcher.ExecuteTaskAsync(task, _preferencesStore.Current, progress, _activeCts.Token);

        ProgressGrid.Visibility = Visibility.Collapsed;
        StartConvertButton.IsEnabled = true;

        if (result.Success)
        {
            if (task.TargetActionId == "qr_read" && !string.IsNullOrWhiteSpace(task.CurrentStatusMessage))
            {
                try { Clipboard.SetText(task.CurrentStatusMessage); } catch { }
                FooterStatusLabel.Text = $"✓ QR Okundu (Panoya kopyalandı): {task.CurrentStatusMessage}";
                MessageBox.Show(
                    $"Okunan QR Kod İçeriği (Panoya kopyalandı):\n\n{task.CurrentStatusMessage}",
                    "QR Kod Sonucu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                FooterStatusLabel.Text = $"✓ {_selectedTool.DisplayName} işlemi tamamlandı ({result.OutputFiles.Count} öğe hazırlandı).";
            }

            if (_preferencesStore.Current.SoundEffectsEnabled)
            {
                SoundFeedback.PlaySuccess();
            }

            if (_preferencesStore.Current.AutoOpenOutputFolder && result.OutputFiles.Count > 0)
            {
                DesktopGeometry.RevealInExplorer(result.OutputFiles[0]);
            }

            OnClearSelectionClick(sender, e);
        }
        else if (task.Status == ConversionTaskStatus.Cancelled)
        {
            FooterStatusLabel.Text = "ℹ İşlem iptal edildi.";
        }
        else
        {
            FooterStatusLabel.Text = $"✕ Hata: {result.ErrorMessage}";
            MessageBox.Show(result.ErrorMessage ?? "Dönüştürme başarısız oldu.", "LD Converter Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Kitaplık / Geçmiş

    private void RenderHistory()
    {
        HistoryItemsList.Children.Clear();
        var entries = _historyStore.Entries;

        if (entries.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Henüz dönüştürülmüş dosya yok. İlk dosyanızı sürükleyip dönüştürün!",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            HistoryItemsList.Children.Add(emptyText);
            return;
        }

        foreach (var entry in entries)
        {
            var card = CreateHistoryCard(entry);
            HistoryItemsList.Children.Add(card);
        }
    }

    private Border CreateHistoryCard(HistoryEntry entry)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 24, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(37, 45, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoStack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        var badge = new Border
        {
            Background = entry.Success
                ? new SolidColorBrush(Color.FromRgb(99, 102, 241))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = entry.FormatSummary,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        titleRow.Children.Add(badge);

        titleRow.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        infoStack.Children.Add(titleRow);

        string timeText = $"{entry.Timestamp:dd.MM.yyyy HH:mm}";
        string detailLine;
        if (!entry.Success && !string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            detailLine = $"{timeText} · Hata: {entry.ErrorMessage}";
        }
        else if (entry.OriginalSizeBytes > 0 && entry.OutputSizeBytes > 0)
        {
            detailLine = $"{timeText} · {FileUtils.FormatBytes(entry.OriginalSizeBytes)} → {FileUtils.FormatBytes(entry.OutputSizeBytes)} ({FileUtils.FormatPercentChange(entry.OriginalSizeBytes, entry.OutputSizeBytes)})";
        }
        else
        {
            detailLine = timeText;
        }

        infoStack.Children.Add(new TextBlock
        {
            Text = detailLine,
            FontSize = 11,
            Foreground = entry.Success
                ? new SolidColorBrush(Color.FromRgb(156, 163, 175))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        grid.Children.Add(infoStack);
        Grid.SetColumn(infoStack, 0);

        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        string? firstOut = entry.OutputFiles.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstOut))
        {
            var openFolderBtn = new Button
            {
                Content = "Klasörde Göster",
                Background = new SolidColorBrush(Color.FromRgb(38, 44, 61)),
                Foreground = Brushes.White,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            string targetPath = firstOut;
            openFolderBtn.Click += (_, _) =>
            {
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    DesktopGeometry.RevealInExplorer(targetPath);
                }
                else
                {
                    MessageBox.Show("Hedef dosya veya klasör artık mevcut değil.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            btnStack.Children.Add(openFolderBtn);
        }

        var deleteItemBtn = new Button
        {
            Content = "✕",
            ToolTip = "Kaydı Sil",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        string entryId = entry.Id;
        deleteItemBtn.Click += (_, _) => _historyStore.Remove(entryId);
        btnStack.Children.Add(deleteItemBtn);

        grid.Children.Add(btnStack);
        Grid.SetColumn(btnStack, 1);

        border.Child = grid;
        return border;
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        _historyStore.Clear();
    }

    #endregion

    #region Hızlı Araçlar

    private void OnQrInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnToolGenerateQrClick(sender, new RoutedEventArgs());
        }
    }

    private void OnToolGenerateQrClick(object sender, RoutedEventArgs e)
    {
        string text = QrInputText.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("Lütfen QR koda dönüştürülecek bir metin veya bağlantı girin.", "QR Kod Üretici", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var tx = new ExportTransaction();
            var qrEngine = new QrEngine();
            qrEngine.GenerateQrCode(text, tx, 600);

            var dummyTask = new ConversionTask { TargetActionId = "qr_generate" };
            string targetDir = ConversionDispatcher.ResolveDestinationFolder(dummyTask, _preferencesStore.Current);
            var committed = tx.CommitTo(targetDir);

            if (committed.Count > 0)
            {
                long outSize = FileUtils.CalculateTotalBytes(committed);
                _historyStore.Add(new HistoryEntry
                {
                    Title = text.Length > 36 ? text[..36] + "..." : text,
                    FormatSummary = "METİN → QR PNG",
                    OutputFiles = committed,
                    OutputSizeBytes = outSize,
                    Timestamp = DateTime.Now,
                    Success = true
                });

                if (_preferencesStore.Current.SoundEffectsEnabled)
                {
                    SoundFeedback.PlaySuccess();
                }

                FooterStatusLabel.Text = $"✓ QR Kod oluşturuldu: {Path.GetFileName(committed[0])}";
                DesktopGeometry.RevealInExplorer(committed[0]);
            }
        }
        catch (Exception ex)
        {
            FooterStatusLabel.Text = $"✕ QR oluşturma hatası: {ex.Message}";
            MessageBox.Show(ex.Message, "QR Kod Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnToolReadQrClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Görsel ve PDF Dosyaları|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff;*.pdf|Tüm Dosyalar (*.*)|*.*",
            Title = "QR Kodu Okunacak Görsel veya PDF Dosyasını Seçin"
        };

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName))
            return;

        try
        {
            var qrEngine = new QrEngine();
            string? decoded = qrEngine.ReadQrCode(dlg.FileName);

            if (!string.IsNullOrWhiteSpace(decoded))
            {
                DecodedQrResultBox.Text = decoded;
                DecodedQrResultBox.Visibility = Visibility.Visible;
                CopyDecodedQrButton.Visibility = Visibility.Visible;
                CopyDecodedQrButton.Content = "Metni Kopyala";

                try { Clipboard.SetText(decoded); } catch { }
                if (_preferencesStore.Current.SoundEffectsEnabled)
                {
                    SoundFeedback.PlaySuccess();
                }
                FooterStatusLabel.Text = $"✓ QR Kod okundu ve panoya kopyalandı: {decoded}";
            }
            else
            {
                FooterStatusLabel.Text = "✕ Seçilen dosyada okunabilir bir QR kod bulunamadı.";
                MessageBox.Show("Seçilen dosyada okunabilir bir QR kod veya barkod bulunamadı.", "QR Okuyucu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            FooterStatusLabel.Text = $"✕ QR okuma hatası: {ex.Message}";
            MessageBox.Show(ex.Message, "QR Okuma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyDecodedQrClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(DecodedQrResultBox.Text))
        {
            try
            {
                Clipboard.SetText(DecodedQrResultBox.Text);
                CopyDecodedQrButton.Content = "✓ Kopyalandı";
            }
            catch
            {
                // Pano meşgul
            }
        }
    }

    private async void OnToolMergePdfsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "PDF ve Görsel Dosyaları|*.pdf;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tiff|PDF Belgeleri (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Birleştirilecek PDF veya Görsel Dosyalarını Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            await RunQuickToolTaskAsync(dlg.FileNames, "pdf_merge", "PDF Birleştirme");
        }
    }

    private async void OnToolQuickZipClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "ZIP Formatında Sıkıştırılacak Dosyaları Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            await RunQuickToolTaskAsync(dlg.FileNames, "zip", "Hızlı ZIP");
        }
    }

    private async void OnToolQuickTarGzClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "TAR.GZ Formatında Sıkıştırılacak Dosyaları Seçin"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            await RunQuickToolTaskAsync(dlg.FileNames, "tar_gz", "Hızlı TAR.GZ");
        }
    }

    private async Task RunQuickToolTaskAsync(IReadOnlyList<string> files, string actionId, string displayName)
    {
        FooterStatusLabel.Text = $"{displayName} işlemi yürütülüyor...";
        var task = new ConversionTask
        {
            InputFiles = files,
            TargetActionId = actionId,
            TargetDisplayName = displayName,
            Options = new ConversionOptions
            {
                ImageQuality = _preferencesStore.Current.DefaultImageQuality
            }
        };

        var result = await _dispatcher.ExecuteTaskAsync(task, _preferencesStore.Current);
        if (result.Success && result.OutputFiles.Count > 0)
        {
            if (_preferencesStore.Current.SoundEffectsEnabled)
            {
                SoundFeedback.PlaySuccess();
            }
            FooterStatusLabel.Text = $"✓ {displayName} tamamlandı: {Path.GetFileName(result.OutputFiles[0])}";
            DesktopGeometry.RevealInExplorer(result.OutputFiles[0]);
        }
        else
        {
            FooterStatusLabel.Text = $"✕ {displayName} hatası: {result.ErrorMessage}";
            MessageBox.Show(result.ErrorMessage ?? "İşlem başarısız oldu.", "Hızlı Araç Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_preferencesStore.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _activeCts?.Cancel();
        if (_onExitRequested != null)
        {
            _onExitRequested.Invoke();
        }
        else
        {
            Application.Current?.Shutdown();
        }
    }
}
