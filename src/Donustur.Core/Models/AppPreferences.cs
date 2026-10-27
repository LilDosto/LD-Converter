namespace Donustur.Core.Models;

public sealed class AppPreferences
{
    public bool Enabled { get; set; } = true;
    public string DragModifier { get; set; } = "Shift"; // Shift, Ctrl, Alt, Shift+Ctrl
    public double WheelScale { get; set; } = 1.0;
    public string Theme { get; set; } = "Dark"; // Dark, Light, System
    public string OutputDirectoryMode { get; set; } = "SameAsSource"; // SameAsSource, Desktop, Custom
    public string CustomOutputDirectory { get; set; } = string.Empty;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool RunAtStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public bool AutoOpenOutputFolder { get; set; } = false;
    public string? CustomFfmpegPath { get; set; }
    public string? CustomLibreOfficePath { get; set; }
    public int DefaultImageQuality { get; set; } = 85;
}
