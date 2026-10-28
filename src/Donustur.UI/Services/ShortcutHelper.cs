using System.IO;

namespace Donustur.UI.Services;

public static class ShortcutHelper
{
    private const string AppShortcutName = "LD Converter.lnk";

    public static string GetDesktopShortcutPath()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktopPath, AppShortcutName);
    }

    public static bool HasDesktopShortcut()
    {
        return File.Exists(GetDesktopShortcutPath());
    }

    public static bool CreateDesktopShortcut()
    {
        try
        {
            string shortcutPath = GetDesktopShortcutPath();
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "LD Converter - Hızlı ve Güvenli Dosya Dönüştürücü";
            shortcut.IconLocation = $"{exePath},0";
            shortcut.Save();

            return File.Exists(shortcutPath);
        }
        catch
        {
            return false;
        }
    }
}
