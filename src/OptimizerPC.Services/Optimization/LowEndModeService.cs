using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Optimization;

/// <summary>
/// Modo PC Fraco. Avalia o hardware com os dados ja coletados e propoe apenas ajustes
/// reversiveis: efeitos visuais, plano de energia de alto desempenho e limpeza segura.
/// Nenhum ajuste e aplicado sem passar pelo servico de otimizacao, que registra o
/// ponto de reversao antes de alterar qualquer coisa.
/// </summary>
public sealed class LowEndModeService : ILowEndModeService
{
    /// <summary>Limite de memoria para considerar o computador modesto.</summary>
    private const long MemoryThresholdBytes = 5L * 1024 * 1024 * 1024;

    private const int MinimumComfortableCores = 4;

    private readonly IOptimizationService _optimization;
    private readonly IVisualEffectsService _visualEffects;
    private readonly IElevationService _elevation;
    private readonly IAppLogger _logger;

    public LowEndModeService(
        IOptimizationService optimization,
        IVisualEffectsService visualEffects,
        IElevationService elevation,
        IAppLogger logger)
    {
        _optimization = optimization;
        _visualEffects = visualEffects;
        _elevation = elevation;
        _logger = logger;
    }

    public LowEndAssessment Assess(SystemSnapshot snapshot, HealthScoreInput scoreInput)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(scoreInput);

        var memoryBytes = snapshot.Memory.TotalBytes > 0
            ? snapshot.Memory.TotalBytes
            : scoreInput.Memory?.TotalBytes ?? 0;

        var totalMemoryMb = (int)Math.Min(int.MaxValue, memoryBytes / (1024 * 1024));

        var cores = snapshot.Cpu.LogicalProcessors > 0
            ? snapshot.Cpu.LogicalProcessors
            : Environment.ProcessorCount;

        var hasSolidState = HasSolidStateDrive(snapshot);
        var storageKnown = snapshot.StorageDevices.Count > 0;

        var reasons = new List<string>();

        if (memoryBytes > 0 && memoryBytes <= MemoryThresholdBytes)
        {
            reasons.Add("LowEnd.Reason.LowMemory");
        }

        if (cores > 0 && cores < MinimumComfortableCores)
        {
            reasons.Add("LowEnd.Reason.FewCores");
        }

        // Disco mecanico so entra como motivo quando a leitura do dispositivo foi
        // concluida: sem dados de armazenamento o aplicativo nao faz suposicoes.
        if (storageKnown && hasSolidState is false)
        {
            reasons.Add("LowEnd.Reason.MechanicalDisk");
        }

        var suggestions = new List<string>();

        if (hasSolidState is false)
        {
            suggestions.Add("LowEnd.Suggestion.DisableVisualEffects");
        }

        suggestions.Add("LowEnd.Suggestion.HighPerformancePlan");
        suggestions.Add("LowEnd.Suggestion.CleanTemporaryFiles");

        var assessment = new LowEndAssessment
        {
            IsLowEnd = reasons.Count > 0,
            TotalMemoryMb = totalMemoryMb,
            LogicalCores = cores,
            HasSolidStateDrive = hasSolidState,
            ReasonKeys = reasons,
            SuggestedActionKeys = suggestions
        };

        _logger.Info(
            "LowEndMode",
            "Avaliacao de hardware modesto: "
            + (assessment.IsLowEnd ? "sim" : "nao")
            + " (" + totalMemoryMb + " MB, " + cores + " threads, "
            + (storageKnown ? hasSolidState ? "SSD" : "disco mecanico" : "armazenamento nao identificado") + ").");

        return assessment;
    }

    public IReadOnlyList<OptimizationAction> BuildActions(LowEndAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        // O estado atual decide o que faz sentido oferecer: o ajuste que ja esta aplicado
        // aparece bloqueado com o motivo, em vez de prometer uma mudanca que nao vai ocorrer.
        var visualEffectsOptimized = false;
        try
        {
            visualEffectsOptimized = _visualEffects.IsOptimizedForPerformanceAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warning("LowEndMode", "Nao foi possivel ler o estado dos efeitos visuais.", ex);
        }

        var actions = new List<OptimizationAction>(4)
        {
            new()
            {
                Id = "lowend.visual-effects",
                Kind = OptimizationActionKind.DisableVisualEffects,
                TitleKey = "Recommendation.VisualEffects.Title",
                DescriptionKey = "Recommendation.VisualEffects.Description",
                Impact = ImpactLevel.Medium,
                Risk = RiskLevel.Low,
                Elevation = ElevationRequirement.None,
                EstimatedBytes = 0,
                RequiresConfirmation = false,
                BlockedReasonKey = visualEffectsOptimized ? "LowEnd.Blocked.AlreadyOptimized" : null,
                IsSelected = visualEffectsOptimized is false
            },

            new()
            {
                Id = "lowend.power-plan",
                Kind = OptimizationActionKind.SetPowerPlan,
                TitleKey = "Recommendation.PowerPlan.Title",
                DescriptionKey = "Recommendation.PowerPlan.Description",
                Impact = ImpactLevel.Medium,
                Risk = RiskLevel.Low,
                Elevation = ElevationRequirement.None,
                EstimatedBytes = 0,
                RequiresConfirmation = false,
                IsSelected = true
            },

            new()
            {
                Id = "lowend.temp-files",
                Kind = OptimizationActionKind.CleanTemporaryFiles,
                TitleKey = "Recommendation.TemporaryFiles.Title",
                DescriptionKey = "Recommendation.TemporaryFiles.Description",
                Impact = ImpactLevel.Medium,
                Risk = RiskLevel.Low,
                Elevation = ElevationRequirement.Recommended,
                EstimatedBytes = 0,
                RequiresConfirmation = false,
                IsSelected = true
            },

            new()
            {
                Id = "lowend.recycle-bin",
                Kind = OptimizationActionKind.EmptyRecycleBin,
                TitleKey = "Recommendation.RecycleBin.Title",
                DescriptionKey = "Recommendation.RecycleBin.Description",
                Impact = ImpactLevel.Medium,
                Risk = RiskLevel.Medium,
                Elevation = ElevationRequirement.None,
                EstimatedBytes = 0,
                RequiresConfirmation = true,
                IsSelected = false
            }
        };

        if (assessment.IsLowEnd is false && assessment.HasSolidStateDrive is false)
        {
            _logger.Info("LowEndMode", "Modo PC Fraco aberto sem hardware modesto identificado: sugestoes mantidas como opcionais.");
        }

        if (_elevation.IsElevated is false)
        {
            _logger.Info("LowEndMode", "Sessao sem privilegios administrativos: a limpeza de arquivos do sistema sera parcial.");
        }

        return actions;
    }

    public async Task<OptimizationPlanResult> ApplyAsync(
        IReadOnlyList<OptimizationAction> actions,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var plan = new OptimizationPlan
        {
            Actions = actions.ToList(),
            Origin = "LowEndMode",
            CreateRestorePoint = true
        };

        _logger.Info("LowEndMode", "Aplicando " + actions.Count + " ajuste(s) do Modo PC Fraco.");

        return await _optimization.ExecuteAsync(plan, progress, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasSolidStateDrive(SystemSnapshot snapshot) =>
        snapshot.StorageDevices.Any(device =>
            device.MediaType is StorageMediaType.Ssd or StorageMediaType.ScmOrNvme
            || device.BusType is StorageBusType.Nvme
            || device.RotationRateRpm == 0);
}
