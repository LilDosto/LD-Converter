using System.Runtime.InteropServices;
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
    private readonly DispatcherTimer _pollTimer;

    private bool _isDragging;
    private bool _suppressedForCurrentDrag;
    private POINT _dragStartPos;
    private WheelWindow? _activeWheel;

    public GlobalDragController(PreferencesStore preferencesStore, Action<ToolChoice, IReadOnlyList<string>> onDropped)
    {
        _preferencesStore = preferencesStore;
        _onDropped = onDropped;

        // 30 ms (~33 FPS) sorgulama aralığı: CPU kullanımı %0.01 seviyesindedir.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_preferencesStore.Current.Enabled)
        {
            if (_activeWheel != null)
            {
                _activeWheel.CancelAndClose();
                _activeWheel = null;
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
            _activeWheel.CancelAndClose();
            _activeWheel = null;
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
                    _activeWheel.ExecuteSelectionAndClose();
                    _activeWheel = null;
                }
            }
            return;
        }

        GetCursorPos(out POINT currentPoint);

        if (!_isDragging)
        {
            _dragStartPos = currentPoint;
            _isDragging = true;
            _suppressedForCurrentDrag = IsOwnWindowForeground();
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
            OpenWheel(currentPoint);
        }
        else if (_activeWheel != null)
        {
            // Eğer çark açıldıktan 350ms sonra hâlâ geçerli bir dosya sürüklemesi (OLE FileDrop)
            // algılanmadıysa (örneğin kullanıcı metin seçiyorsa), çarkı sessizce kapat.
            if (!_activeWheel.HasValidFileDrag &&
                (DateTime.UtcNow - _activeWheel.OpenedAtUtc).TotalMilliseconds > 350)
            {
                _activeWheel.CancelAndClose();
                _activeWheel = null;
                _suppressedForCurrentDrag = true;
                return;
            }

            _activeWheel.UpdateMousePosition(new Point(currentPoint.X, currentPoint.Y));
        }
    }

    private static bool IsOwnWindowForeground()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || Application.Current == null) return false;

            foreach (Window win in Application.Current.Windows)
            {
                if (win is WheelWindow) continue;
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero && hwnd == fg)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Hata durumunda engelleme
        }
        return false;
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
        var prefs = _preferencesStore.Current;
        _activeWheel = new WheelWindow(_onDropped, prefs.SoundEffectsEnabled, prefs.WheelScale);

        // Çarkı farenin tam merkezine konumlandır
        DesktopGeometry.PlaceCentered(_activeWheel, screenPoint.X, screenPoint.Y);
        _activeWheel.Show();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _activeWheel?.CancelAndClose();
        _activeWheel = null;
    }
}
