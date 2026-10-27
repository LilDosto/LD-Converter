using System.Windows.Forms;
using Donustur.Core.Services;

namespace Donustur.UI.Services;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PreferencesStore _preferencesStore;
    private readonly Action _openMainWindow;
    private readonly Action _openSettings;
    private readonly Action _exitApp;
    private readonly ToolStripMenuItem _toggleWheelItem;

    public TrayHost(
        PreferencesStore preferencesStore,
        Action openMainWindow,
        Action openSettings,
        Action exitApp)
    {
        _preferencesStore = preferencesStore;
        _openMainWindow = openMainWindow;
        _openSettings = openSettings;
        _exitApp = exitApp;

        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("LD Converter'ı Aç");
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (_, _) => _openMainWindow();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _toggleWheelItem = new ToolStripMenuItem($"Sürükleme Çarkı ({_preferencesStore.Current.DragModifier})");
        _toggleWheelItem.Checked = _preferencesStore.Current.Enabled;
        _toggleWheelItem.Click += (_, _) =>
        {
            var p = _preferencesStore.Current;
            p.Enabled = !p.Enabled;
            _preferencesStore.Save(p);
            _toggleWheelItem.Checked = p.Enabled;
        };
        contextMenu.Items.Add(_toggleWheelItem);

        var settingsItem = new ToolStripMenuItem("Ayarlar...");
        settingsItem.Click += (_, _) => _openSettings();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Çıkış");
        exitItem.Click += (_, _) => _exitApp();
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIconProvider.GetTrayIcon(),
            Text = "LD Converter - Hızlı Dosya Dönüştürücü",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _openMainWindow();
    }

    public void ShowToast(string title, string message)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        }
        catch
        {
            // Sessizce geç
        }
    }

    public void UpdateStatus()
    {
        _toggleWheelItem.Checked = _preferencesStore.Current.Enabled;
        _toggleWheelItem.Text = $"Sürükleme Çarkı ({_preferencesStore.Current.DragModifier})";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
