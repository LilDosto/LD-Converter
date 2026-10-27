namespace Donustur.Core.Models;

public enum ConversionTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Bir dönüştürme görevinin durum ve çıktı bilgilerini temsil eder.
/// </summary>
public sealed class ConversionTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<string> InputFiles { get; init; } = Array.Empty<string>();
    public string TargetActionId { get; init; } = string.Empty;
    public string TargetDisplayName { get; init; } = string.Empty;
    public ConversionOptions Options { get; init; } = new();
    public ConversionTaskStatus Status { get; set; } = ConversionTaskStatus.Queued;
    public double ProgressPercentage { get; set; }
    public string? CurrentStatusMessage { get; set; }
    public List<string> OutputFiles { get; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - CreatedAt : null;
}

public sealed record ConversionResult(
    bool Success,
    IReadOnlyList<string> OutputFiles,
    string? ErrorMessage = null,
    TimeSpan? Duration = null
);
