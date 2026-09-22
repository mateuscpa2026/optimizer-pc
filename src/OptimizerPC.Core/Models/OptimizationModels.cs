namespace OptimizerPC.Core.Models;

/// <summary>
/// Recomendacao gerada pela Smart Optimization. Sempre apresenta motivo, impacto
/// estimado e risco, e nunca e executada sem aprovacao do usuario.
/// </summary>
public sealed class Recommendation
{
    public string Id { get; init; } = string.Empty;
    public RecommendationKind Kind { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public string ReasonKey { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public ImpactLevel Impact { get; init; } = ImpactLevel.Medium;
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
    public long EstimatedGainBytes { get; init; }
    public ElevationRequirement Elevation { get; init; } = ElevationRequirement.None;
    public string? ActionId { get; init; }
    public ActionState State { get; set; } = ActionState.Pending;

    public bool CanExecute => string.IsNullOrEmpty(ActionId) is false && State == ActionState.Pending;
}

/// <summary>Acao concreta que pode ser executada pelo plano de otimizacao.</summary>
public sealed class OptimizationAction
{
    public string Id { get; init; } = string.Empty;
    public OptimizationActionKind Kind { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public ImpactLevel Impact { get; init; } = ImpactLevel.Medium;
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
    public ElevationRequirement Elevation { get; init; } = ElevationRequirement.None;
    public long EstimatedBytes { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool CreatesRestoreRecord { get; init; } = true;
    public string? Parameter { get; init; }
    public string? TargetId { get; init; }
    public ActionState State { get; set; } = ActionState.Pending;
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Motivo pelo qual a acao nao pode ser aplicada agora (por exemplo, o ajuste
    /// ja esta ativo). Quando preenchido, a acao fica indisponivel com explicacao.
    /// </summary>
    public string? BlockedReasonKey { get; init; }

    public bool IsBlocked => string.IsNullOrEmpty(BlockedReasonKey) is false;
}

public sealed class OptimizationPlan
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public List<OptimizationAction> Actions { get; init; } = new();
    public string Origin { get; init; } = "SmartOptimization";
    public bool CreateRestorePoint { get; init; } = true;

    public long TotalEstimatedBytes => Actions.Sum(a => a.EstimatedBytes);

    public IEnumerable<OptimizationAction> SelectedActions => Actions.Where(a => a.IsSelected);
}

/// <summary>Resultado da execucao de uma acao individual.</summary>
public sealed class ActionExecutionResult
{
    public string ActionId { get; init; } = string.Empty;
    public OptimizationActionKind Kind { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string MessageKey { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public long BytesFreed { get; set; }
    public TimeSpan Duration { get; set; }
    public long? RestoreRecordId { get; set; }
}

public sealed class OptimizationPlanResult
{
    public string PlanId { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public List<ActionExecutionResult> Results { get; init; } = new();
    public bool WasCancelled { get; set; }
    public bool RestorePointCreated { get; set; }
    public string? RestorePointMessage { get; set; }

    public long TotalBytesFreed => Results.Sum(r => r.BytesFreed);

    public int SucceededCount => Results.Count(r => r.Success);

    public int FailedCount => Results.Count(r => r.Success is false && r.Skipped is false);

    public int SkippedCount => Results.Count(r => r.Skipped);

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
