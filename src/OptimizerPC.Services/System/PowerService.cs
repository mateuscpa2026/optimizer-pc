using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;
using OptimizerPC.Services.Restore;

namespace OptimizerPC.Services.System;

/// <summary>
/// Planos de energia do Windows via PowrProf. Apenas o plano ativo e alterado:
/// nenhum limite de energia, tensao, frequencia ou configuracao de firmware e tocado.
/// A troca registra um ponto de reversao com o plano anterior.
/// </summary>
public sealed class PowerService : IPowerService
{
    private static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid PowerSaverScheme = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private readonly IRestoreService _restore;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public PowerService(IRestoreService restore, ILocalizer localizer, IAppLogger logger)
    {
        _restore = restore;
        _localizer = localizer;
        _logger = logger;
    }

    public Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<PowerPlanInfo>>(ReadPlans, cancellationToken);

    public Task<PowerPlanInfo?> GetActivePlanAsync(CancellationToken cancellationToken = default)
        => Task.Run<PowerPlanInfo?>(() => ReadPlans().FirstOrDefault(p => p.IsActive), cancellationToken);

    public async Task<ActionExecutionResult> SetActivePlanAsync(PowerPlanInfo plan, CancellationToken cancellationToken = default)
    {
        var current = ReadPlans().FirstOrDefault(p => p.IsActive);

        if (current is not null && current.SchemeGuid == plan.SchemeGuid)
        {
            return new ActionExecutionResult
            {
                ActionId = plan.SchemeGuid.ToString("D"),
                Kind = OptimizationActionKind.SetPowerPlan,
                TitleKey = "Power.Action.SetActive",
                Success = false,
                Skipped = true,
                MessageKey = "Power.Result.AlreadyActive",
                Detail = plan.Name
            };
        }

        if (NativePower.SetActiveScheme(plan.SchemeGuid) is false)
        {
            _logger.Warning("Power", "Nao foi possivel ativar o plano de energia " + plan.Name + ".", null);

            return new ActionExecutionResult
            {
                ActionId = plan.SchemeGuid.ToString("D"),
                Kind = OptimizationActionKind.SetPowerPlan,
                TitleKey = "Power.Action.SetActive",
                Success = false,
                MessageKey = "Power.Error.ApplyFailed",
                Detail = plan.Name
            };
        }

        long? recordId = null;
        if (current is not null)
        {
            var payload = RestorePayload.ForPowerPlan(current.SchemeGuid, current.Name);
            recordId = await _restore.RegisterAsync(
                new RestoreRecord
                {
                    Kind = RestoreRecordKind.PowerPlan,
                    TitleKey = "Restore.Kind.PowerPlan",
                    Description = current.Name,
                    SourceAction = nameof(OptimizationActionKind.SetPowerPlan),
                    PayloadJson = payload
                },
                cancellationToken).ConfigureAwait(false);
        }

        _logger.Info("Power", "Plano de energia ativo alterado para " + plan.Name + ".");

        return new ActionExecutionResult
        {
            ActionId = plan.SchemeGuid.ToString("D"),
            Kind = OptimizationActionKind.SetPowerPlan,
            TitleKey = "Power.Action.SetActive",
            Success = true,
            MessageKey = "Power.Result.Activated",
            Detail = plan.Name,
            RestoreRecordId = recordId
        };
    }

    private IReadOnlyList<PowerPlanInfo> ReadPlans()
    {
        var active = NativePower.GetActiveScheme();
        var seen = new HashSet<Guid>();

        var plans = new List<PowerPlanInfo>();
        foreach (var scheme in NativePower.Enumerate())
        {
            if (seen.Add(scheme.Guid) is false)
            {
                // O Windows pode repetir o mesmo GUID na enumeracao.
                continue;
            }

            var kind = Classify(scheme.Guid);
            plans.Add(new PowerPlanInfo
            {
                SchemeGuid = scheme.Guid,
                Name = string.IsNullOrWhiteSpace(scheme.Name) ? _localizer["Power.Plan.Unnamed"] : scheme.Name,
                Kind = kind,
                IsWellKnown = kind != PowerPlanKind.Custom,
                IsActive = active == scheme.Guid
            });
        }

        return plans
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Kind == PowerPlanKind.Custom)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static PowerPlanKind Classify(Guid guid)
    {
        if (guid == BalancedScheme)
        {
            return PowerPlanKind.Balanced;
        }

        if (guid == HighPerformanceScheme)
        {
            return PowerPlanKind.HighPerformance;
        }

        if (guid == PowerSaverScheme)
        {
            return PowerPlanKind.PowerSaver;
        }

        return PowerPlanKind.Custom;
    }
}
