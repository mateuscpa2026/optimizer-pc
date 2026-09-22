using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Diagnostics;

/// <summary>
/// Monta o resumo do dashboard reaproveitando a mesma coleta usada pelo diagnostico:
/// os dados do computador sao lidos uma unica vez por abertura de tela.
/// A varredura profunda de armazenamento nao faz parte do dashboard: ela e executada
/// sob demanda na tela de armazenamento, porque percorre o disco inteiro.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private readonly SystemDataCollector _collector;
    private readonly IHealthScoreService _healthScore;
    private readonly IRecommendationEngine _recommendations;
    private readonly IPowerService _power;
    private readonly IVisualEffectsService _visualEffects;
    private readonly ILowEndModeService _lowEnd;
    private readonly IGameBoostService _gameBoost;
    private readonly IDiagnosticService _diagnostics;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;

    public DashboardService(
        SystemDataCollector collector,
        IHealthScoreService healthScore,
        IRecommendationEngine recommendations,
        IPowerService power,
        IVisualEffectsService visualEffects,
        ILowEndModeService lowEnd,
        IGameBoostService gameBoost,
        IDiagnosticService diagnostics,
        IClock clock,
        IAppLogger logger)
    {
        _collector = collector;
        _healthScore = healthScore;
        _recommendations = recommendations;
        _power = power;
        _visualEffects = visualEffects;
        _lowEnd = lowEnd;
        _gameBoost = gameBoost;
        _diagnostics = diagnostics;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DashboardSummary> BuildAsync(
        bool includeRecommendations = true,
        IProgress<DiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new DiagnosticOptions();
        var data = await _collector.CollectAsync(options, null, cancellationToken).ConfigureAwait(false);

        var assessment = AssessLowEnd(data);
        var scoreInput = BuildScoreInput(data, assessment.IsLowEnd);
        var score = _healthScore.Calculate(scoreInput);

        var systemVolume = data.SystemVolume;
        var summary = new DashboardSummary
        {
            Score = score,
            Metrics = data.Metrics ?? new MetricSample(),
            System = data.Snapshot,
            CleanupTargets = data.CleanupTargets,
            ProblemCount = CountProblems(scoreInput),
            StartupEnabledCount = data.StartupEntries.Count(entry => entry.IsEnabled),
            TemporaryBytes = data.CleanupTargets
                .Where(target => target.Category != CleanupCategory.RecycleBin)
                .Sum(target => target.TotalBytes),
            RecycleBinBytes = data.RecycleBin?.SizeBytes ?? 0,
            SystemDriveFreeBytes = systemVolume?.FreeBytes ?? 0,
            SystemDriveFreePercent = systemVolume?.FreePercent ?? 0,
            LastDiagnosisUtc = await ReadLastDiagnosisUtcAsync(cancellationToken).ConfigureAwait(false),
            Temperature = ReadTemperature(data)
        };

        if (includeRecommendations)
        {
            var recommendations = await BuildRecommendationsAsync(data, scoreInput, cancellationToken)
                .ConfigureAwait(false);

            summary.RecommendationCount = recommendations.Count;
            summary.Recommendations = recommendations;
        }

        _logger.Info(
            "Dashboard",
            "Dashboard atualizado: indice " + score.Total + "/100, " + summary.RecommendationCount + " recomendacao(oes).");

        progress?.Report(new DiagnosticProgress("dashboard.done", "Dashboard.Progress.Done", 1, 1));

        return summary;
    }

    private LowEndAssessment AssessLowEnd(SystemData data)
    {
        if (data.Snapshot is not { } snapshot)
        {
            return new LowEndAssessment();
        }

        return _lowEnd.Assess(snapshot, BuildScoreInput(data, isLowEnd: false));
    }

    private async Task<IReadOnlyList<Recommendation>> BuildRecommendationsAsync(
        SystemData data,
        HealthScoreInput scoreInput,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PowerPlanInfo> plans;
        try
        {
            plans = await _power.GetPlansAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning("Dashboard", "Planos de energia indisponiveis para as recomendacoes.", exception);
            plans = Array.Empty<PowerPlanInfo>();
        }

        var visualEffectsOptimized = await _visualEffects.IsOptimizedForPerformanceAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = new RecommendationContext
        {
            Snapshot = data.Snapshot,
            ScoreInput = scoreInput,
            CleanupTargets = data.CleanupTargets,
            StorageUsage = data.StorageUsage,
            StartupEntries = data.StartupEntries,
            Services = data.Services,
            PowerPlans = plans,
            VisualEffectsOptimized = visualEffectsOptimized,
            GamerModeEnabled = _gameBoost.CurrentSession is not null
        };

        var recommendations = _recommendations.Build(context);
        _logger.Info("Dashboard", recommendations.Count + " recomendacao(oes) geradas a partir dos dados atuais.");

        return recommendations;
    }

    /// <summary>
    /// Momento do ultimo diagnostico completo. Quando nenhum diagnostico foi executado
    /// na sessao, vale o instante da coleta atual: o dashboard nunca exibe uma data
    /// de diagnostico que nao aconteceu.
    /// </summary>
    private async Task<DateTime> ReadLastDiagnosisUtcAsync(CancellationToken cancellationToken)
    {
        try
        {
            var last = await _diagnostics.GetLastResultAsync(cancellationToken).ConfigureAwait(false);
            return last?.CompletedAtUtc ?? _clock.UtcNow;
        }
        catch (Exception exception)
        {
            _logger.Warning("Dashboard", "Nao foi possivel ler o ultimo diagnostico.", exception);
            return _clock.UtcNow;
        }
    }

    private static HealthScoreInput BuildScoreInput(SystemData data, bool isLowEnd) => new()
    {
        CpuUsagePercent = data.Metrics?.CpuPercent,
        Memory = data.Snapshot?.Memory,
        Volumes = data.Volumes,
        DriveHealth = data.DriveHealth,
        StorageUsage = data.StorageUsage,
        StartupEntries = data.StartupEntries,
        Services = data.Services,
        RecoverableBytes = data.RecoverableBytes,
        RunningProcessCount = data.Processes.Count,
        IsLowEndHardware = isLowEnd
    };

    /// <summary>
    /// Conta os pontos de atencao reais observados agora: volume quase cheio, memoria em
    /// uso critico, disco em falha, reinicializacao pendente. A contagem e explicita para
    /// o numero exibido no dashboard ter origem verificavel.
    /// </summary>
    private static int CountProblems(HealthScoreInput input)
    {
        var problems = 0;

        problems += input.Volumes.Count(volume => volume.IsReady && volume.FreePercent < 12);
        problems += input.DriveHealth.Count(report => report.Status is DriveHealthStatus.Failing or DriveHealthStatus.Warning);
        problems += input.Memory is { TotalBytes: > 0 } memory && memory.UsedPercent >= 90 ? 1 : 0;
        problems += input.CpuUsagePercent is double cpu && cpu >= 85 ? 1 : 0;

        return problems;
    }

    private static string? ReadTemperature(SystemData data)
    {
        var temperatures = data.DriveHealth
            .Where(report => report.TemperatureCelsius.HasValue)
            .Select(report => report.TemperatureCelsius!.Value)
            .ToList();

        return temperatures.Count == 0 ? null : temperatures.Max() + " °C";
    }
}
