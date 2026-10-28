using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Donustur.Core.Models;
using Donustur.Core.Services;
using Donustur.UI.Windows;

namespace Donustur.UI.Services;

public sealed class GlobalDragController : IDisposable
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // ALT
    private const int VK_ESCAPE = 0x1B;

    private readonly PreferencesStore _preferencesStore;
    private readonly Action<ToolChoice, IReadOnlyList<string>> _onDropped;
    private readonly System.Threading.Timer _backgroundTimer;

    private bool _isDragging;
    private bool _suppressedForCurrentDrag;
    private POINT _dragStartPos;
    private WheelWindow? _activeWheel;
    private IntPtr _activeWheelHwnd = IntPtr.Zero;
    private int _isDispatchingPos;
    private bool _isDisposed;

    public GlobalDragController(PreferencesStore preferencesStore, Action<ToolChoice, IReadOnlyList<string>> onDropped)
    {
        _preferencesStore = preferencesStore;
        _onDropped = onDropped;

        // UI iş parçacığını asla meşgul etmeyen, arka plan iş parçacığı zamanlayıcısı (25ms ~ 40Hz)
        _backgroundTimer = new System.Threading.Timer(OnBackgroundTick, null, 25, 25);
    }

    private void OnBackgroundTick(object? state)
    {
        if (_isDisposed) return;

        if (!_preferencesStore.Current.Enabled)
        {
            if (_activeWheel != null)
            {
                DispatchToUi(() =>
                {
                    _activeWheel?.CancelAndClose();
                    _activeWheel = null;
                    _activeWheelHwnd = IntPtr.Zero;
                });
            }
            _isDragging = false;
            _suppressedForCurrentDrag = false;
            return;
        }

        bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool rightDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        bool escapeDown = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;

        // Sağ tık veya ESC ile çarkı iptal et
        if (_activeWheel != null && (rightDown || escapeDown))
        {
            DispatchToUi(() =>
            {
                _activeWheel?.CancelAndClose();
                _activeWheel = null;
                _activeWheelHwnd = IntPtr.Zero;
            });
            _suppressedForCurrentDrag = true;
            return;
        }

        if (!leftDown)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _suppressedForCurrentDrag = false;
                if (_activeWheel != null)
                {
                    DispatchToUi(() =>
                    {
                        if (_activeWheel != null)
                        {
                            _activeWheel.ExecuteSelectionAndClose();
                            _activeWheel = null;
                            _activeWheelHwnd = IntPtr.Zero;
                        }
                    });
                }
            }
            return;
        }

        GetCursorPos(out POINT currentPoint);

        if (!_isDragging)
        {
            _dragStartPos = currentPoint;
            _isDragging = true;
            _suppressedForCurrentDrag = IsOwnWindowForeground(_activeWheelHwnd);
            return;
        }

        if (_suppressedForCurrentDrag)
        {
            return;
        }

        // Fare en az 8 piksel hareket etmişse sürükleme kabul edilir
        int deltaX = Math.Abs(currentPoint.X - _dragStartPos.X);
        int deltaY = Math.Abs(currentPoint.Y - _dragStartPos.Y);
        if (deltaX + deltaY < 8) return;

        bool modifierPressed = CheckModifierPressed(_preferencesStore.Current.DragModifier);

        if (modifierPressed && _activeWheel == null)
        {
            DispatchToUi(() => OpenWheel(currentPoint));
        }
        else if (_activeWheel != null)
        {
            var wheel = _activeWheel;
            // Eğer çark açıldıktan 350ms sonra hâlâ geçerli bir dosya sürüklemesi (OLE FileDrop)
            // algılanmadıysa (örneğin kullanıcı metin seçiyorsa), çarkı sessizce kapat.
            if (!wheel.HasValidFileDrag && (DateTime.UtcNow - wheel.OpenedAtUtc).TotalMilliseconds > 350)
            {
                DispatchToUi(() =>
                {
                    wheel.CancelAndClose();
                    if (_activeWheel == wheel)
                    {
                        _activeWheel = null;
                        _activeWheelHwnd = IntPtr.Zero;
                    }
                });
                _suppressedForCurrentDrag = true;
                return;
            }

            // Fare konumu güncellemesini Dispatcher kuyruğunu şişirmeden (rate-limit) gönder
            if (Interlocked.CompareExchange(ref _isDispatchingPos, 1, 0) == 0)
            {
                DispatchToUi(() =>
                {
                    try
                    {
                        wheel.UpdateMousePosition(new Point(currentPoint.X, currentPoint.Y));
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isDispatchingPos, 0);
                    }
                });
            }
        }
    }

    private static bool IsOwnWindowForeground(IntPtr activeWheelHwnd)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (activeWheelHwnd != IntPtr.Zero && fg == activeWheelHwnd) return false;

            GetWindowThreadProcessId(fg, out uint procId);
            return procId == (uint)Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckModifierPressed(string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "ctrl" or "control" => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            "alt" => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            "shift+ctrl" or "ctrl+shift" =>
                ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) &&
                ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0),
            _ => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 // Varsayılan: Shift
        };
    }

    private void OpenWheel(POINT screenPoint)
    {
        if (_activeWheel != null) return;

        var prefs = _preferencesStore.Current;
        var wheel = new WheelWindow(_onDropped, prefs.SoundEffectsEnabled, prefs.WheelScale);
        _activeWheel = wheel;

        // Çarkı farenin tam merkezine konumlandır
        DesktopGeometry.PlaceCentered(wheel, screenPoint.X, screenPoint.Y);
        wheel.Show();

        try
        {
            _activeWheelHwnd = new WindowInteropHelper(wheel).Handle;
        }
        catch
        {
            _activeWheelHwnd = IntPtr.Zero;
        }
    }

    private static void DispatchToUi(Action action)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return;

        if (app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _backgroundTimer.Dispose();

        DispatchToUi(() =>
        {
            _activeWheel?.CancelAndClose();
            _activeWheel = null;
            _activeWheelHwnd = IntPtr.Zero;
        });
    }
}
