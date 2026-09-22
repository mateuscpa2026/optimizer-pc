using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Interop;
using OptimizerPC.Services.Restore;

namespace OptimizerPC.Services.Optimization;

/// <summary>
/// Efeitos visuais do Windows. Ajusta somente preferencias do usuario atual
/// (HKEY_CURRENT_USER) e nunca o preset geral do sistema: os codigos do valor
/// VisualFXSetting mudam entre versoes do Windows e um valor errado alteraria a
/// aparencia inteira do computador. Cada valor anterior e registrado antes da
/// alteracao para permitir a restauracao exata.
/// </summary>
public sealed class VisualEffectsService : IVisualEffectsService
{
    private const string LogCategory = "VisualEffects";
    private const string HistoryCategory = "History.Category.Optimization";

    private static readonly VisualEffectSetting[] Settings =
    {
        new(@"Control Panel\Desktop", "MenuShowDelay", IsText: true, Performance: "0", Appearance: "400"),
        new(@"Control Panel\Desktop", "DragFullWindows", IsText: true, Performance: "0", Appearance: "1"),
        new(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", IsText: true, Performance: "0", Appearance: "1"),
        new(AdvancedSubKey, "TaskbarAnimations", IsText: false, Performance: "0", Appearance: "1"),
        new(AdvancedSubKey, "ListviewAlphaSelect", IsText: false, Performance: "0", Appearance: "1"),
        new(AdvancedSubKey, "ListviewShadow", IsText: false, Performance: "0", Appearance: "1")
    };

    private const string AdvancedSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private readonly IRegistryService _registry;
    private readonly IRestoreService _restore;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public VisualEffectsService(
        IRegistryService registry,
        IRestoreService restore,
        IHistoryService history,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _registry = registry;
        _restore = restore;
        _history = history;
        _localizer = localizer;
        _logger = logger;
    }

    public Task<bool> IsOptimizedForPerformanceAsync(CancellationToken cancellationToken = default) =>
        Task.Run(IsOptimizedForPerformance, cancellationToken);

    public async Task<ActionExecutionResult> ApplyAsync(bool optimizeForPerformance, CancellationToken cancellationToken = default)
    {
        var kind = optimizeForPerformance
            ? OptimizationActionKind.DisableVisualEffects
            : OptimizationActionKind.EnableVisualEffects;
        var titleKey = optimizeForPerformance ? "Visual.Action.Optimize" : "Visual.Action.Restore";
        var messageKey = optimizeForPerformance ? "Visual.Result.Optimized" : "Visual.Result.Restored";
        var actionId = kind.ToString();

        var previous = Capture();
        var restoreRecordId = await RegisterRestoreRecordAsync(kind, titleKey, previous, cancellationToken).ConfigureAwait(false);

        try
        {
            Write(optimizeForPerformance);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao alterar os efeitos visuais.", ex);

            return new ActionExecutionResult
            {
                ActionId = actionId,
                Kind = kind,
                TitleKey = titleKey,
                Success = false,
                MessageKey = "Visual.Error.ApplyFailed",
                Detail = ex.Message
            };
        }

        NativeDesktop.NotifySettingChange("VisualEffects");
        _logger.Info(LogCategory, optimizeForPerformance
            ? "Efeitos visuais ajustados para desempenho."
            : "Efeitos visuais restaurados para aparencia.");

        await RegisterHistoryAsync(titleKey, messageKey, restoreRecordId, cancellationToken).ConfigureAwait(false);

        return new ActionExecutionResult
        {
            ActionId = actionId,
            Kind = kind,
            TitleKey = titleKey,
            Success = true,
            MessageKey = messageKey,
            RestoreRecordId = restoreRecordId
        };
    }

    private bool IsOptimizedForPerformance()
    {
        // Avalia os efeitos individuais: e o que o ajuste realmente altera.
        return ReadInt("TaskbarAnimations") == 0
               && ReadInt("ListviewShadow") == 0
               && ReadInt("ListviewAlphaSelect") == 0
               && IsOff("MinAnimate")
               && IsOff("DragFullWindows");
    }

    private bool IsOff(string name)
    {
        var setting = SettingOf(name);
        var value = _registry.GetValue(RegistryHiveKind.CurrentUser, setting.SubKey, name);
        if (value is null)
        {
            return false;
        }

        var text = value.IntValue?.ToString() ?? value.StringValue;
        return string.Equals(text, "0", StringComparison.Ordinal);
    }

    private int? ReadInt(string name)
    {
        var setting = SettingOf(name);
        var value = _registry.GetValue(RegistryHiveKind.CurrentUser, setting.SubKey, name);
        if (value is null)
        {
            return null;
        }

        if (value.IntValue.HasValue)
        {
            return value.IntValue;
        }

        return int.TryParse(value.StringValue, out var parsed) ? parsed : null;
    }

    private IReadOnlyList<VisualEffectValue> Capture()
    {
        var values = new List<VisualEffectValue>(Settings.Length);

        foreach (var setting in Settings)
        {
            var current = _registry.GetValue(RegistryHiveKind.CurrentUser, setting.SubKey, setting.Name);
            values.Add(new VisualEffectValue(
                setting.SubKey,
                setting.Name,
                current is not null,
                current?.Kind,
                current?.StringValue,
                current?.IntValue));
        }

        return values;
    }

    private void Write(bool optimizeForPerformance)
    {
        foreach (var setting in Settings)
        {
            var value = optimizeForPerformance ? setting.Performance : setting.Appearance;

            if (setting.IsText)
            {
                _registry.SetString(RegistryHiveKind.CurrentUser, setting.SubKey, setting.Name, value);
                continue;
            }

            _registry.SetInt(RegistryHiveKind.CurrentUser, setting.SubKey, setting.Name, int.Parse(value));
        }
    }

    private async Task<long?> RegisterRestoreRecordAsync(
        OptimizationActionKind kind,
        string titleKey,
        IReadOnlyList<VisualEffectValue> previous,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = RestorePayload.ForVisualEffects(kind == OptimizationActionKind.DisableVisualEffects, previous);

            return await _restore.RegisterAsync(
                new RestoreRecord
                {
                    Kind = RestoreRecordKind.VisualEffectsProfile,
                    TitleKey = "Restore.Kind.VisualEffectsProfile",
                    Description = _localizer[titleKey],
                    SourceAction = kind.ToString(),
                    PayloadJson = payload
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sem o ponto de reversao a alteracao continua valida: o ajuste e reversivel
            // pelos proprios controles do Windows e o usuario e informado na tela.
            _logger.Warning(LogCategory, "Nao foi possivel registrar o ponto de reversao dos efeitos visuais.", ex);
            return null;
        }
    }

    private async Task RegisterHistoryAsync(string titleKey, string messageKey, long? restoreRecordId, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategory,
                    Action = _localizer[titleKey],
                    Description = _localizer[messageKey],
                    Result = _localizer[messageKey],
                    Success = true,
                    RestoreRecordKey = restoreRecordId?.ToString()
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a alteracao dos efeitos visuais no historico.", ex);
        }
    }

    private static VisualEffectSetting SettingOf(string name) =>
        Settings.First(setting => string.Equals(setting.Name, name, StringComparison.Ordinal));

    /// <summary>Valor aplicado a um efeito em cada modo.</summary>
    private sealed record VisualEffectSetting(string SubKey, string Name, bool IsText, string Performance, string Appearance);
}
