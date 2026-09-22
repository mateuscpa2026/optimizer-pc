using OptimizerPC.Core.Formatting;

namespace OptimizerPC.Core.Models;

/// <summary>Verificacao individual do diagnostico do Windows.</summary>
public sealed class DiagnosticCheck
{
    public string Id { get; init; } = string.Empty;
    public string CategoryKey { get; init; } = string.Empty;
    public string TitleKey { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Severity Severity { get; init; } = Severity.Ok;
    public string AdviceKey { get; init; } = string.Empty;
    public bool Succeeded { get; init; } = true;
    public string? ErrorDetail { get; init; }
    public ElevationRequirement Elevation { get; init; } = ElevationRequirement.None;
    public string? RelatedToolId { get; init; }

    public bool HasProblem => Severity is Severity.Warning or Severity.Critical;
}

public sealed class DiagnosisResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DiagnosticCheck> Checks { get; init; } = new();
    public bool WasCancelled { get; set; }

    public int CriticalCount => Checks.Count(c => c.Severity == Severity.Critical);

    public int WarningCount => Checks.Count(c => c.Severity == Severity.Warning);

    public int ProblemCount => Checks.Count(c => c.HasProblem);

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}

/// <summary>Fator que compoe a pontuacao estimada de desempenho/saude.</summary>
public sealed class ScoreFactor
{
    public string Id { get; init; } = string.Empty;
    public string TitleKey { get; init; } = string.Empty;
    public string DetailKey { get; init; } = string.Empty;
    public string? DetailValue { get; init; }
    public double Weight { get; init; }
    public double Score { get; init; }
    public Severity Severity { get; init; } = Severity.Ok;

    public double PointsLost => Math.Max(0, (100 - Score) * Weight / 100.0);

    public string ScoreText => Score.ToString("0") + " / 100";
}

/// <summary>
/// Pontuacao de saude do computador. E uma estimativa interna do Optimizer PC,
/// calculada a partir dos fatores listados, e nao uma medicao cientifica.
/// </summary>
public sealed class HealthScore
{
    public int Total { get; init; }
    public ScoreCategory Category { get; init; }
    public IReadOnlyList<ScoreFactor> Factors { get; init; } = Array.Empty<ScoreFactor>();
    public DateTime ComputedAtUtc { get; init; } = DateTime.UtcNow;

    public string TotalText => Total + "/100";

    public IEnumerable<ScoreFactor> NegativeFactors => Factors.Where(f => f.Score < 85).OrderByDescending(f => f.PointsLost);

    public static ScoreCategory Classify(int total) => total switch
    {
        >= 85 => ScoreCategory.Excellent,
        >= 70 => ScoreCategory.Good,
        >= 50 => ScoreCategory.Attention,
        _ => ScoreCategory.Critical
    };

    public string DisclaimerKey => "Score.Disclaimer";
}

/// <summary>Progresso da execucao do diagnostico.</summary>
public readonly record struct DiagnosticProgress(
    string CurrentCheckId,
    string CurrentCheckTitleKey,
    int CompletedChecks,
    int TotalChecks)
{
    public double PercentComplete => TotalChecks <= 0 ? 0 : Math.Clamp(CompletedChecks * 100.0 / TotalChecks, 0, 100);
}

/// <summary>Resumo usado pelos cartoes do dashboard.</summary>
public sealed class DashboardSummary
{
    public HealthScore Score { get; set; } = new();
    public MetricSample Metrics { get; set; } = new();
    public SystemSnapshot? System { get; set; }
    public IReadOnlyList<Recommendation> Recommendations { get; set; } = Array.Empty<Recommendation>();
    public IReadOnlyList<CleanupTarget> CleanupTargets { get; set; } = Array.Empty<CleanupTarget>();
    public int ProblemCount { get; set; }
    public int RecommendationCount { get; set; }
    public int StartupEnabledCount { get; set; }
    public long TemporaryBytes { get; set; }
    public long RecycleBinBytes { get; set; }
    public long SystemDriveFreeBytes { get; set; }
    public double SystemDriveFreePercent { get; set; }
    public DateTime LastDiagnosisUtc { get; set; }
    public string? Temperature { get; set; }

    public string TemporaryText => Humanize.Bytes(TemporaryBytes);

    public string FreeSpaceText => Humanize.Bytes(SystemDriveFreeBytes);
}
