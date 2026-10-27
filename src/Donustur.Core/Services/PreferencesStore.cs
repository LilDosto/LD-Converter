using System.Text.Json;
using Donustur.Core.Models;

namespace Donustur.Core.Services;

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private AppPreferences _current;

    public AppPreferences Current => _current;

    public event Action<AppPreferences>? Changed;

    public PreferencesStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "LDConverter");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "preferences.json");
        }

        _current = Load();
    }

    private AppPreferences Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Fallback to defaults on error
        }

        var defaultPrefs = new AppPreferences();
        Save(defaultPrefs);
        return defaultPrefs;
    }

    public void Save(AppPreferences preferences)
    {
        try
        {
            _current = preferences;
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(preferences, JsonOptions);
            string tempFile = _filePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _filePath, overwrite: true);

            Changed?.Invoke(_current);
        }
        catch
        {
            // Handle error silently
        }
    }
}
