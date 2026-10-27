using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Donustur.Core.Catalog;
using Donustur.Core.Math;
using Donustur.Core.Models;
using Donustur.UI.Services;

namespace Donustur.UI.Windows;

public partial class WheelWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly Action<ToolChoice, IReadOnlyList<string>> _onDropped;
    private readonly bool _soundEnabled;
    private readonly double _targetScale;
    private IReadOnlyList<string> _activeFiles = Array.Empty<string>();
    private bool _isClosing;
    private bool _selectionExecuted;

    public DateTime OpenedAtUtc { get; } = DateTime.UtcNow;
    public bool HasValidFileDrag { get; private set; }

    public WheelWindow(Action<ToolChoice, IReadOnlyList<string>> onDropped, bool soundEnabled = true, double scale = 1.0)
    {
        InitializeComponent();
        _onDropped = onDropped;
        _soundEnabled = soundEnabled;
        _targetScale = Math.Clamp(scale, 0.75, 1.5);

        // Ölçek büyütüldüğünde çarkın pencere sınırlarından kırpılmasını önle
        double baseSize = 390.0;
        Width = Math.Ceiling(baseSize * _targetScale);
        Height = Math.Ceiling(baseSize * _targetScale);

        WheelScale.ScaleX = _targetScale;
        WheelScale.ScaleY = _targetScale;

        // Varsayılan seçeneklerle başlat
        RadialControl.Choices = FormatCatalog.GetChoicesForFiles(Array.Empty<string>());

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        Loaded += (_, _) => AnimateOpen();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Pencerenin odağı çalmamasını ve OLE sürüklemeyi bozmamasını sağla
        var handle = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
    }

    private void AnimateOpen()
    {
        var scaleAnim = new DoubleAnimation(_targetScale * 0.7, _targetScale, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(100));

        WheelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        WheelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        RootGrid.BeginAnimation(OpacityProperty, fadeAnim);
    }

    public void AnimateClose(Action? onComplete = null)
    {
        if (_isClosing) return;
        _isClosing = true;

        var scaleAnim = new DoubleAnimation(_targetScale, _targetScale * 0.8, TimeSpan.FromMilliseconds(90));
        var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(90));

        fadeAnim.Completed += (_, _) =>
        {
            try
            {
                Close();
            }
            catch
            {
                // Pencere zaten kapanmış olabilir
            }
            onComplete?.Invoke();
        };

        WheelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        WheelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        RootGrid.BeginAnimation(OpacityProperty, fadeAnim);
    }

    public void CancelAndClose()
    {
        _selectionExecuted = true;
        RadialControl.HoveredIndex = -1;
        AnimateClose();
    }

    public void UpdateMousePosition(Point screenPoint)
    {
        if (!IsLoaded || _isClosing) return;
        try
        {
            if (PresentationSource.FromVisual(this) == null) return;
            var clientPos = PointFromScreen(screenPoint);
            UpdateSegmentFromPoint(clientPos);
        }
        catch
        {
            // Pencere kapanırken oluşan koordinat dönüşüm hatasını yoksay
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            HasValidFileDrag = true;
            e.Effects = DragDropEffects.Copy;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _activeFiles = files;
                RadialControl.FileCount = files.Length;
                RadialControl.Choices = FormatCatalog.GetChoicesForFiles(files);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            HasValidFileDrag = true;
            e.Effects = DragDropEffects.Copy;
            Point pos = e.GetPosition(this);
            UpdateSegmentFromPoint(pos);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (!_isClosing && !_selectionExecuted)
        {
            RadialControl.HoveredIndex = -1;
        }
    }

    private void UpdateSegmentFromPoint(Point pos)
    {
        if (_isClosing || _selectionExecuted) return;

        double centerX = ActualWidth / 2.0;
        double centerY = ActualHeight / 2.0;
        if (centerX <= 0 || centerY <= 0)
        {
            centerX = Width / 2.0;
            centerY = Height / 2.0;
        }

        int total = RadialControl.Choices?.Count ?? 0;
        int newIndex = WheelMath.GetSegmentIndex(
            centerX, centerY,
            pos.X, pos.Y,
            total,
            RadialControl.InnerRadius * _targetScale,
            RadialControl.OuterRadius * _targetScale);

        if (newIndex != RadialControl.HoveredIndex)
        {
            RadialControl.HoveredIndex = newIndex;
            if (newIndex >= 0 && _soundEnabled)
            {
                SoundFeedback.PlayTick();
            }
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            HasValidFileDrag = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _activeFiles = files;
            }
        }

        Point dropPos = e.GetPosition(this);
        UpdateSegmentFromPoint(dropPos);
        ExecuteSelectionAndClose();
        e.Handled = true;
    }

    public void ExecuteSelectionAndClose()
    {
        if (_selectionExecuted) return;
        _selectionExecuted = true;

        int index = RadialControl.HoveredIndex;
        var choices = RadialControl.Choices;
        var files = _activeFiles;

        ToolChoice? selected = (index >= 0 && choices != null && index < choices.Count)
            ? choices[index]
            : null;

        AnimateClose(() =>
        {
            if (selected != null && files.Count > 0)
            {
                _onDropped(selected, files);
            }
        });
    }
}
