namespace Donustur.Core.Models;

/// <summary>
/// Çark üzerinde veya arayüzde gösterilen dönüştürme/işlem seçeneği.
/// </summary>
public sealed record ToolChoice(
    string Id,
    string DisplayName,
    string Category,
    string IconKey,
    string Description,
    bool RequiresOptions = false
);
