using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Configuration;
using OptimizerPC.Services.Interop;
using OptimizerPC.Services.Storage;

namespace OptimizerPC.Services.Restore;

/// <summary>
/// Central de reversao. Cada alteracao feita pelo aplicativo registra antes o estado
/// anterior e esta classe devolve o computador exatamente a esse estado. A reversao
/// percorre somente as chaves e valores gravados no proprio registro de reversao:
/// nenhum caminho ou valor e inventado na hora de desfazer.
/// </summary>
public sealed class RestoreService : IRestoreService
{
    private const string LogCategory = "Restore";
    private const string HistoryCategory = "History.Category.Restore";
    private const int MaxRecords = 200;
    private const char CsvSeparator = ';';

    private readonly RestoreRepository _repository;
    private readonly IRegistryService _registry;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public RestoreService(
        RestoreRepository repository,
        IRegistryService registry,
        IHistoryService history,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _repository = repository;
        _registry = registry;
        _history = history;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RestoreRecord>> GetRecordsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.ListAsync(MaxRecords, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao ler os pontos de reversao.", ex);
            return Array.Empty<RestoreRecord>();
        }
    }

    public async Task<int> RegisterAsync(RestoreRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            var id = await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
            _logger.Info(LogCategory, "Ponto de reversao registrado (" + record.TitleKey + " · " + record.Key + ").");
            return (int)id;
        }
        catch (Exception ex)
        {
            // A alteracao ja foi aplicada pelo chamador: falhar aqui nao deve derrubar a
            // operacao, mas a ausencia do ponto de reversao precisa ficar registrada.
            _logger.Error(LogCategory, "Nao foi possivel registrar o ponto de reversao de " + record.TitleKey + ".", ex);
            return 0;
        }
    }

    public async Task<bool> UndoAsync(RestoreRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.CanRestore is false)
        {
            _logger.Warning(LogCategory, "Reversao ignorada: o registro " + record.Key + " esta em estado " + record.UndoState + ".");
            return false;
        }

        var (success, message) = await Task.Run(() => Revert(record), cancellationToken).ConfigureAwait(false);

        record.UndoState = success ? UndoState.Restored : UndoState.Failed;
        record.RestoreMessage = message;

        try
        {
            await _repository.UpdateStateAsync(record.Key, record.UndoState, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao gravar o resultado da reversao.", ex);
        }

        await RegisterHistoryAsync(record, success, message, cancellationToken).ConfigureAwait(false);

        _logger.Log(
            success ? LogLevel.Info : LogLevel.Warning,
            LogCategory,
            (success ? "Reversao concluida: " : "Falha na reversao: ") + record.TitleKey + " (" + record.Key + ").");

        return success;
    }

    public async Task ExportAsync(
        IReadOnlyList<RestoreRecord> records,
        string filePath,
        ReportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Caminho de exportacao nao informado.", nameof(filePath));
        }

        if (format == ReportFormat.Pdf)
        {
            throw new NotSupportedException("A exportacao de pontos de reversao nao oferece PDF.");
        }

        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) is false)
        {
            Directory.CreateDirectory(folder);
        }

        var content = format switch
        {
            ReportFormat.Json => BuildJson(records),
            ReportFormat.Csv => BuildCsv(records),
            ReportFormat.Html => BuildHtml(records),
            _ => BuildText(records)
        };

        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
            .ConfigureAwait(false);

        _logger.Info(LogCategory, "Pontos de reversao exportados a pedido do usuario.");
    }

    public async Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(LogCategory, "Historico de reversao limpo (" + removed + " registro(s)).");
            return removed;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory, "Falha ao limpar o historico de reversao.", ex);
            return 0;
        }
    }

    private (bool Success, string Message) Revert(RestoreRecord record)
    {
        switch (record.Kind)
        {
            case RestoreRecordKind.RegistryValue:
                return RevertRegistryValue(record);

            case RestoreRecordKind.StartupEntryState:
                return RevertStartupEntry(record);

            case RestoreRecordKind.VisualEffectsProfile:
                return RevertVisualEffects(record);

            case RestoreRecordKind.PowerPlan:
                return RevertPowerPlan(record);

            default:
                // Backup de arquivo e ponto de restauracao do sistema nao sao desfeitos
                // por aqui: cada um tem seu proprio caminho oficial.
                return (false, _localizer["Restore.Error.NotReversible"]);
        }
    }

    private (bool Success, string Message) RevertRegistryValue(RestoreRecord record)
    {
        // O estado anterior de um servico tem formato proprio; os demais ajustes de
        // registro compartilham o mesmo formato de backup de valor.
        if (RestorePayload.HasProperty(record.PayloadJson, "hadStart"))
        {
            var service = RestorePayload.ReadServiceStartMode(record.PayloadJson);
            return service is null ? InvalidPayload() : RevertServiceStartMode(service);
        }

        if (RestorePayload.HasProperty(record.PayloadJson, "kind"))
        {
            var backup = RestorePayload.ReadRegistryBackup(record.PayloadJson);
            return backup is null ? InvalidPayload() : RevertRegistryBackup(backup);
        }

        var legacy = RestorePayload.ReadRegistryValue(record.PayloadJson);
        return legacy is null ? InvalidPayload() : RevertRegistryIntValue(legacy);
    }

    private (bool Success, string Message) RevertRegistryBackup(RegistryBackupPayload payload)
    {
        var hive = ParseHive(payload.Hive);
        if (hive is null)
        {
            return InvalidPayload();
        }

        // Valor ausente no estado anterior significa remover o valor criado.
        var previous = payload.ToValueData();
        var applied = RegistryValueWriter.Restore(_registry, hive.Value, payload.SubKey, payload.Name, previous);

        return applied
            ? (true, _localizer["Restore.Success.RegistryValue"])
            : (false, _localizer["Restore.Error.WriteFailed"]);
    }

    private (bool Success, string Message) RevertRegistryIntValue(RegistryValuePayload payload)
    {
        var hive = ParseHive(payload.Hive);
        if (hive is null)
        {
            return InvalidPayload();
        }

        var previous = payload.HadValue
            ? new RegistryValueData(
                payload.Hive,
                payload.SubKey,
                payload.Value,
                payload.PreviousInt.HasValue ? nameof(RegistryValueKind.DWord) : nameof(RegistryValueKind.String),
                payload.PreviousString,
                payload.PreviousInt,
                null)
            : null;

        var applied = RegistryValueWriter.Restore(_registry, hive.Value, payload.SubKey, payload.Value, previous);

        return applied
            ? (true, _localizer["Restore.Success.RegistryValue"])
            : (false, _localizer["Restore.Error.WriteFailed"]);
    }

    private (bool Success, string Message) RevertServiceStartMode(ServiceStartModePayload payload)
    {
        if (payload.HadStart is false)
        {
            return (false, _localizer["Restore.Error.PreviousUnknown"]);
        }

        var serviceName = NameFromSubKey(payload.SubKey);
        if (serviceName.Length is 0)
        {
            return InvalidPayload();
        }

        if (NativeProcess.IsCurrentProcessElevated() is false)
        {
            return (false, _localizer["Restore.Error.NeedsElevation"]);
        }

        using var manager = NativeServices.OpenSCManager(null, null, NativeServices.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            return (false, _localizer["Restore.Error.Denied"]);
        }

        using var handle = NativeServices.OpenService(manager, serviceName, NativeServices.SERVICE_CHANGE_CONFIG);
        if (handle.IsInvalid)
        {
            return (false, _localizer["Restore.Error.Denied"]);
        }

        var applied = NativeServices.ChangeServiceConfig(
            handle,
            NativeServices.SERVICE_NO_CHANGE,
            (uint)payload.PreviousStart,
            NativeServices.SERVICE_NO_CHANGE,
            null,
            null,
            IntPtr.Zero,
            null,
            null,
            null,
            null);

        if (applied is false)
        {
            return (false, _localizer["Restore.Error.WriteFailed"]);
        }

        if (payload.HadDelayed)
        {
            TrySetDelayedStart(handle, payload.PreviousDelayed == 1);
        }

        return (true, _localizer["Restore.Success.ServiceStartMode"]);
    }

    private (bool Success, string Message) RevertStartupEntry(RestoreRecord record)
    {
        var payload = RestorePayload.ReadToggle(record.PayloadJson);
        if (payload is null)
        {
            return InvalidPayload();
        }

        var hive = ParseHive(payload.Hive);
        if (hive is null)
        {
            return InvalidPayload();
        }

        try
        {
            if (payload.HadValue)
            {
                _registry.SetBinary(hive.Value, payload.SubKey, payload.Value, Convert.FromBase64String(payload.Previous));
            }
            else if (_registry.KeyExists(hive.Value, payload.SubKey))
            {
                // O Windows nao tinha registro de aprovacao para este item: remover o
                // valor devolve o comportamento original.
                _registry.DeleteValue(hive.Value, payload.SubKey, payload.Value);
            }

            return (true, _localizer["Restore.Success.StartupEntry"]);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel restaurar o item de inicializacao.", ex);
            return (false, _localizer["Restore.Error.Denied"]);
        }
    }

    private (bool Success, string Message) RevertVisualEffects(RestoreRecord record)
    {
        var payload = RestorePayload.ReadVisualEffects(record.PayloadJson);
        if (payload is null || payload.Values.Count is 0)
        {
            return InvalidPayload();
        }

        var restored = 0;
        foreach (var value in payload.Values)
        {
            var previous = value.HadValue
                ? new RegistryValueData(
                    RegistryHiveKind.CurrentUser.ToString(),
                    value.SubKey,
                    value.Name,
                    value.Kind ?? nameof(RegistryValueKind.String),
                    value.StringValue,
                    value.IntValue,
                    null)
                : null;

            if (RegistryValueWriter.Restore(_registry, RegistryHiveKind.CurrentUser, value.SubKey, value.Name, previous))
            {
                restored++;
            }
        }

        NativeDesktop.NotifySettingChange("VisualEffects");
        _logger.Info(LogCategory, "Efeitos visuais restaurados: " + restored + " de " + payload.Values.Count + " valores.");

        return restored == payload.Values.Count
            ? (true, _localizer["Restore.Success.VisualEffects"])
            : (false, _localizer.Format("Restore.Error.Partial", restored, payload.Values.Count));
    }

    private (bool Success, string Message) RevertPowerPlan(RestoreRecord record)
    {
        var payload = RestorePayload.ReadPowerPlan(record.PayloadJson);
        if (payload is null || Guid.TryParse(payload.SchemeGuid, out var scheme) is false)
        {
            return InvalidPayload();
        }

        if (NativePower.SetActiveScheme(scheme) is false)
        {
            // O plano pode ter sido removido do computador depois do ajuste.
            return (false, _localizer.Format("Restore.Error.PlanUnavailable", payload.SchemeName));
        }

        return (true, _localizer.Format("Restore.Success.PowerPlan", payload.SchemeName));
    }

    private async Task RegisterHistoryAsync(RestoreRecord record, bool success, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategory,
                    Action = _localizer["History.Restore.Title"],
                    Description = string.IsNullOrWhiteSpace(record.Description) ? record.TitleKey : record.Description,
                    Result = _localizer[success ? "History.Restore.Success" : "History.Restore.Failed"],
                    Success = success,
                    Details = message
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar a reversao no historico.", ex);
        }
    }

    private (bool Success, string Message) InvalidPayload() =>
        (false, _localizer["Restore.Error.PayloadInvalid"]);

    private static RegistryHiveKind? ParseHive(string hive) =>
        Enum.TryParse<RegistryHiveKind>(hive, ignoreCase: true, out var parsed) ? parsed : null;

    private static string NameFromSubKey(string subKey)
    {
        var index = subKey.LastIndexOf('\\');
        return index >= 0 && index + 1 < subKey.Length ? subKey[(index + 1)..] : string.Empty;
    }

    private static void TrySetDelayedStart(SafeWaitHandle handle, bool delayed)
    {
        var info = new NativeServices.SERVICE_DELAYED_AUTO_START_INFO { IsDelayedAutoStart = delayed };
        var size = Marshal.SizeOf<NativeServices.SERVICE_DELAYED_AUTO_START_INFO>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            NativeServices.ChangeServiceConfig2(handle, NativeServices.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, pointer);
        }
        catch (Exception)
        {
            // A marcacao de inicio atrasado e complementar: o modo principal ja voltou.
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private string BuildText(IReadOnlyList<RestoreRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_localizer["Restore.Export.Title"]);
        builder.AppendLine();

        foreach (var record in records)
        {
            builder.AppendLine(_localizer[record.TitleKey] + " · " + record.CreatedText);
            if (string.IsNullOrWhiteSpace(record.Description) is false)
            {
                builder.AppendLine(_localizer["Restore.Column.Description"] + ": " + record.Description);
            }

            if (string.IsNullOrWhiteSpace(record.TargetPath) is false)
            {
                builder.AppendLine(_localizer["Restore.Column.Target"] + ": " + record.TargetPath);
            }

            builder.AppendLine(_localizer["Restore.Column.State"] + ": " + _localizer["Restore.UndoState." + record.UndoState]);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private string BuildCsv(IReadOnlyList<RestoreRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(
            CsvSeparator,
            _localizer["Restore.Column.Created"],
            _localizer["Restore.Column.Kind"],
            _localizer["Restore.Column.Description"],
            _localizer["Restore.Column.Target"],
            _localizer["Restore.Column.State"]));

        foreach (var record in records)
        {
            builder.AppendLine(string.Join(
                CsvSeparator,
                Escape(record.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Escape(_localizer[record.TitleKey]),
                Escape(record.Description),
                Escape(record.TargetPath ?? string.Empty),
                Escape(_localizer["Restore.UndoState." + record.UndoState])));
        }

        return builder.ToString();
    }

    private string BuildHtml(IReadOnlyList<RestoreRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"" + Humanize.LanguageCode(_localizer.Current) + "\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>" + EscapeHtml(_localizer["Restore.Export.Title"]) + "</title></head><body>");
        builder.AppendLine("<h1>" + EscapeHtml(_localizer["Restore.Export.Title"]) + "</h1>");
        builder.AppendLine("<table border=\"1\" cellspacing=\"0\" cellpadding=\"6\">");
        builder.AppendLine("<tr><th>" + EscapeHtml(_localizer["Restore.Column.Created"]) + "</th><th>"
            + EscapeHtml(_localizer["Restore.Column.Kind"]) + "</th><th>"
            + EscapeHtml(_localizer["Restore.Column.Description"]) + "</th><th>"
            + EscapeHtml(_localizer["Restore.Column.Target"]) + "</th><th>"
            + EscapeHtml(_localizer["Restore.Column.State"]) + "</th></tr>");

        foreach (var record in records)
        {
            builder.AppendLine("<tr><td>" + EscapeHtml(record.CreatedText) + "</td><td>"
                + EscapeHtml(_localizer[record.TitleKey]) + "</td><td>"
                + EscapeHtml(record.Description) + "</td><td>"
                + EscapeHtml(record.TargetPath ?? string.Empty) + "</td><td>"
                + EscapeHtml(_localizer["Restore.UndoState." + record.UndoState]) + "</td></tr>");
        }

        builder.AppendLine("</table></body></html>");
        return builder.ToString();
    }

    private static string BuildJson(IReadOnlyList<RestoreRecord> records)
    {
        var options = AppJson.CreateOptions(indented: true, skipReadOnlyProperties: false);
        var projection = records.Select(record => new
        {
            record.Key,
            record.Kind,
            record.TitleKey,
            record.Description,
            record.CreatedAtUtc,
            record.UndoState,
            record.SourceAction,
            record.TargetPath,
            record.RestoredAtUtc,
            record.RestoreMessage
        });

        return JsonSerializer.Serialize(projection, options);
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { CsvSeparator, '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static string EscapeHtml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
