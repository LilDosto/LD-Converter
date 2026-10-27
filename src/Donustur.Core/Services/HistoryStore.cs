using System.Text.Json;

namespace Donustur.Core.Services;

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string FormatSummary { get; set; } = string.Empty;
    public List<string> InputFiles { get; set; } = new();
    public List<string> OutputFiles { get; set; } = new();
    public long OriginalSizeBytes { get; set; }
    public long OutputSizeBytes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public event Action? Changed;

    public HistoryStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(localData, "LDConverter");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "history.json");
        }

        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            _entries.Clear();
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions);
                    if (list != null)
                    {
                        _entries.AddRange(list);
                    }
                }
            }
            catch
            {
                // Silently fallback on empty history
            }
        }
    }

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Insert(0, entry);
            // En fazla son 100 işlemi sakla
            if (_entries.Count > 100)
            {
                _entries.RemoveRange(100, _entries.Count - 100);
            }
            SaveInternal();
        }
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _entries.RemoveAll(e => e.Id == id) > 0;
            if (removed)
            {
                SaveInternal();
            }
        }
        if (removed)
        {
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            SaveInternal();
        }
        Changed?.Invoke();
    }

    private void SaveInternal()
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_entries, JsonOptions);
            string tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            // Silently handle IO errors
        }
    }
}
