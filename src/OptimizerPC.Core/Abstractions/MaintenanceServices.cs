using OptimizerPC.Core.Models;

namespace OptimizerPC.Core.Abstractions;

/// <summary>
/// Varredura de locais de limpeza sem remover nada.
/// </summary>
public interface ICleanupScanner
{
    Task<IReadOnlyList<CleanupTarget>> ScanAsync(
        IReadOnlyList<CleanupCategory>? categories = null,
        bool includeRecycleBin = true,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<RecycleBinInfo> GetRecycleBinInfoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Execução da limpeza com validação de segurança.
/// </summary>
public interface ICleanupService
{
    Task<CleanupSummary> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Esvazia a Lixeira. Operação sempre confirmada pelo usuário.
    /// </summary>
    Task<ActionExecutionResult> EmptyRecycleBinAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Diagnóstico completo do sistema.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>
    /// Executa o diagnóstico. Quando <paramref name="cleanupTargets"/> é informado,
    /// a varredura de limpeza não é repetida: o chamador já varreu e reaproveita o resultado.
    /// </summary>
    Task<DiagnosisResult> RunAsync(
        DiagnosticOptions options,
        IReadOnlyList<CleanupTarget>? cleanupTargets = null,
        IProgress<DiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DiagnosisResult?> GetLastResultAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opções do diagnóstico.
/// </summary>
public sealed class DiagnosticOptions
{
    public bool IncludeDeepStorageScan { get; init; }

    public bool IncludeDriveHealth { get; init; } = true;

    public bool IncludeStartupAnalysis { get; init; } = true;

    public bool IncludeServiceAnalysis { get; init; } = true;

    public bool IncludeSecurityChecks { get; init; } = true;
}

/// <summary>
/// Cálculo do índice de saúde do PC.
/// </summary>
public interface IHealthScoreService
{
    HealthScore Calculate(HealthScoreInput input);

    IReadOnlyList<ScoreCategory> GetCategories();
}

/// <summary>
/// Entrada do cálculo do índice de saúde.
/// </summary>
public sealed class HealthScoreInput
{
    public double? CpuUsagePercent { get; init; }

    public MemoryInfo? Memory { get; init; }

    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = Array.Empty<VolumeInfo>();

    public IReadOnlyList<DeviceHealthReport> DriveHealth { get; init; } = Array.Empty<DeviceHealthReport>();

    public IReadOnlyList<StorageCategoryUsage> StorageUsage { get; init; } = Array.Empty<StorageCategoryUsage>();

    public IReadOnlyList<StartupEntry> StartupEntries { get; init; } = Array.Empty<StartupEntry>();

    public IReadOnlyList<WindowsServiceInfo> Services { get; init; } = Array.Empty<WindowsServiceInfo>();

    public long RecoverableBytes { get; init; }

    public int RunningProcessCount { get; init; }

    public bool IsLowEndHardware { get; init; }
}

/// <summary>
/// Efeitos visuais do Windows. Altera somente preferencias do usuario atual e
/// registra cada valor anterior para permitir a restauracao.
/// </summary>
public interface IVisualEffectsService
{
    Task<bool> IsOptimizedForPerformanceAsync(CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> ApplyAsync(bool optimizeForPerformance, CancellationToken cancellationToken = default);
}

/// <summary>
/// Monta o resumo exibido no dashboard a partir das fontes locais.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummary> BuildAsync(
        bool includeRecommendations = true,
        IProgress<DiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Motor de recomendações. Somente leitura: nunca executa nada.
/// </summary>
public interface IRecommendationEngine
{
    IReadOnlyList<Recommendation> Build(RecommendationContext context);
}

/// <summary>
/// Contexto usado para gerar recomendações.
/// </summary>
public sealed class RecommendationContext
{
    public SystemSnapshot? Snapshot { get; init; }

    public HealthScoreInput ScoreInput { get; init; } = new();

    public IReadOnlyList<CleanupTarget> CleanupTargets { get; init; } = Array.Empty<CleanupTarget>();

    public IReadOnlyList<StorageCategoryUsage> StorageUsage { get; init; } = Array.Empty<StorageCategoryUsage>();

    public IReadOnlyList<StartupEntry> StartupEntries { get; init; } = Array.Empty<StartupEntry>();

    public IReadOnlyList<WindowsServiceInfo> Services { get; init; } = Array.Empty<WindowsServiceInfo>();

    public IReadOnlyList<PowerPlanInfo> PowerPlans { get; init; } = Array.Empty<PowerPlanInfo>();

    public bool VisualEffectsOptimized { get; init; }

    public bool GamerModeEnabled { get; init; }
}

/// <summary>
/// Execução de planos de otimização com registro e restauração.
/// </summary>
public interface IOptimizationService
{
    Task<OptimizationPlan> BuildPlanAsync(
        IReadOnlyList<Recommendation> recommendations,
        CancellationToken cancellationToken = default);

    Task<OptimizationPlanResult> ExecuteAsync(
        OptimizationPlan plan,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> UndoAsync(
        OptimizationAction action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Progresso da execução de um plano.
/// </summary>
public readonly record struct OptimizationProgress(int CompletedActions, int TotalActions, string CurrentActionKey, string CurrentActionTitle);

/// <summary>
/// Modo PC Fraco: ajustes reversíveis para máquinas modestas.
/// </summary>
public interface ILowEndModeService
{
    LowEndAssessment Assess(SystemSnapshot snapshot, HealthScoreInput scoreInput);

    /// <summary>
    /// Ajustes disponíveis com o estado atual e o motivo de estarem bloqueados, quando for o caso.
    /// </summary>
    IReadOnlyList<OptimizationAction> BuildActions(LowEndAssessment assessment);

    Task<OptimizationPlanResult> ApplyAsync(
        IReadOnlyList<OptimizationAction> actions,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado da avaliação de hardware modesto.
/// </summary>
public sealed class LowEndAssessment
{
    public bool IsLowEnd { get; init; }

    public int TotalMemoryMb { get; init; }

    public int LogicalCores { get; init; }

    public bool HasSolidStateDrive { get; init; }

    public IReadOnlyList<string> ReasonKeys { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SuggestedActionKeys { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Modo Gamer: sessão de foco temporária, sem overclock.
/// </summary>
public interface IGameBoostService
{
    Task<GameBoostResult> StartAsync(
        GameBoostSession session,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GameBoostResult> StopAsync(bool restoreEverything, CancellationToken cancellationToken = default);

    GameBoostSession? CurrentSession { get; }
}

/// <summary>
/// Sessão do Modo Gamer.
/// </summary>
public sealed class GameBoostSession
{
    public string? GameName { get; init; }

    public int? ProcessId { get; init; }

    public bool SetHighPerformancePowerPlan { get; init; } = true;

    public bool PauseNotifications { get; init; } = true;

    public bool ReduceBackgroundProcessPriority { get; init; }

    public bool DisableVisualEffects { get; init; }

    public bool StopTemporaryServices { get; init; }

    public bool EnableGameMode { get; init; } = true;
}

/// <summary>
/// Resultado de uma operação do Modo Gamer.
/// </summary>
public sealed class GameBoostResult
{
    public bool Success { get; init; }

    public IReadOnlyList<ActionExecutionResult> Results { get; init; } = Array.Empty<ActionExecutionResult>();

    public string MessageKey { get; init; } = string.Empty;
}

/// <summary>
/// Detecção de jogos instalados (somente leitura).
/// </summary>
public interface IGameDetector
{
    Task<IReadOnlyList<InstalledGame>> DetectAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<ProcessInfoModel> FindRunningGames(IReadOnlyList<ProcessInfoModel> processes);
}

/// <summary>
/// Registro e restauração de alterações.
/// </summary>
public interface IRestoreService
{
    Task<IReadOnlyList<RestoreRecord>> GetRecordsAsync(CancellationToken cancellationToken = default);

    Task<bool> UndoAsync(RestoreRecord record, CancellationToken cancellationToken = default);

    Task<int> RegisterAsync(RestoreRecord record, CancellationToken cancellationToken = default);

    Task ExportAsync(IReadOnlyList<RestoreRecord> records, string filePath, ReportFormat format, CancellationToken cancellationToken = default);

    Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pontos de restauração do Windows.
/// </summary>
public interface IRestorePointManager
{
    Task<SystemRestoreStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<RestorePointCreationResult> CreateAsync(string description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os pontos existentes. O Windows não expõe essa lista sem WMI: quando a
    /// leitura não for possível, o retorno é vazio e a interface orienta o usuário
    /// a abrir a Restauração do Sistema.
    /// </summary>
    Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Habilita a proteção do sistema. A ativação é feita pelas ferramentas oficiais
    /// do Windows: o retorno indica que a ação precisa ser concluída pelo usuário.
    /// </summary>
    Task<RestorePointCreationResult> EnableProtectionAsync(VolumeInfo systemVolume, CancellationToken cancellationToken = default);

    /// <summary>Abre a janela oficial "Proteção do Sistema" do Windows.</summary>
    Task<bool> OpenSystemProtectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Abre o assistente oficial de Restauração do Sistema do Windows.</summary>
    Task<bool> OpenSystemRestoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado da criação de ponto de restauração.
/// </summary>
public sealed class RestorePointCreationResult
{
    public bool Success { get; init; }

    public string MessageKey { get; init; } = string.Empty;

    public bool RequiresElevation { get; init; }

    public int? SequenceNumber { get; init; }
}

/// <summary>
/// Geração de relatórios.
/// </summary>
public interface IReportService
{
    Task<ReportResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken = default);

    IReadOnlyList<ReportRecord> GetRecentReports(int limit = 20);

    Task<IReadOnlyList<ReportRecord>> GetHistoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Agendamento de manutenção periódica.
/// </summary>
public interface IMaintenanceScheduler
{
    Task<ScheduleResult> ApplyAsync(MaintenanceSchedule schedule, CancellationToken cancellationToken = default);

    Task<ScheduleResult> RemoveAsync(CancellationToken cancellationToken = default);

    Task<MaintenanceSchedule> GetCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado do agendamento.
/// </summary>
public sealed class ScheduleResult
{
    public bool Success { get; init; }

    public string MessageKey { get; init; } = string.Empty;

    public bool RequiresElevation { get; init; }
}
