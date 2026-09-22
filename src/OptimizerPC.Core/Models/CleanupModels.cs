using OptimizerPC.Core.Formatting;

namespace OptimizerPC.Core.Models;

/// <summary>Local de limpeza detectado, com volume e risco estimados antes da execucao.</summary>
public sealed class CleanupTarget
{
    public string Id { get; init; } = string.Empty;
    public CleanupCategory Category { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    public long TotalBytes { get; set; }
    public long FileCount { get; set; }
    public long DirectoryCount { get; set; }
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
    public ElevationRequirement Elevation { get; init; } = ElevationRequirement.None;
    public bool RequiresConfirmation { get; init; }
    public bool IsAvailable { get; set; } = true;
    public string? UnavailableReasonKey { get; set; }
    public bool IsSelected { get; set; }

    public string SizeText => Humanize.Bytes(TotalBytes);
}

public sealed class CleanupFailure
{
    public string Path { get; init; } = string.Empty;
    public string ReasonKey { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>Resultado da limpeza de uma categoria.</summary>
public sealed class CleanupCategoryResult
{
    public string TargetId { get; init; } = string.Empty;
    public CleanupCategory Category { get; init; }
    public long DeletedFiles { get; set; }
    public long DeletedBytes { get; set; }
    public long SkippedFiles { get; set; }
    public bool Succeeded { get; set; }
    public string MessageKey { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public List<CleanupFailure> Failures { get; init; } = new();

    public string DeletedText => Humanize.Bytes(DeletedBytes);
}

public sealed class CleanupSummary
{
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public List<CleanupCategoryResult> Results { get; init; } = new();
    public bool WasCancelled { get; set; }
    public bool IsDryRun { get; init; }

    public long TotalDeletedBytes => Results.Sum(r => r.DeletedBytes);

    public long TotalDeletedFiles => Results.Sum(r => r.DeletedFiles);

    public long TotalSkippedFiles => Results.Sum(r => r.SkippedFiles);

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    public string TotalDeletedText => Humanize.Bytes(TotalDeletedBytes);
}

public sealed class RecycleBinInfo
{
    public long ItemCount { get; init; }
    public long SizeBytes { get; init; }

    public string SizeText => Humanize.Bytes(SizeBytes);
}

/// <summary>Progresso da varredura ou da limpeza de uma categoria.</summary>
public readonly record struct CleanupProgress(
    string CurrentTargetId,
    string CurrentPath,
    long ProcessedBytes,
    long TotalBytes,
    int CompletedTargets,
    int TotalTargets)
{
    public double PercentComplete => TotalBytes <= 0
        ? (TotalTargets <= 0 ? 0 : CompletedTargets * 100.0 / TotalTargets)
        : Math.Clamp(ProcessedBytes * 100.0 / TotalBytes, 0, 100);
}
