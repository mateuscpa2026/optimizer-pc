using System.Globalization;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;

namespace OptimizerPC.Services.Scheduling;

/// <summary>
/// Agendamento da manutencao periodica pelo Agendador de Tarefas oficial do Windows.
/// A tarefa executa o proprio Otimizador PC com o parametro --manutencao: nenhum
/// script externo e criado. Os comandos passam pela lista branca do aplicativo, que
/// aceita apenas /create, /delete e /query com valores previamente validados.
/// </summary>
public sealed class MaintenanceScheduler : IMaintenanceScheduler
{
    private const string LogCategory = "Scheduling";

    private const string DefaultTaskName = "OptimizerPC-Manutencao";

    private const string MaintenanceArgument = "--maintenance";

    private const string HistoryCategoryKey = "History.Category.Maintenance";

    private readonly ICommandExecutionService _commands;
    private readonly IElevationService _elevation;
    private readonly ISettingsService _settings;
    private readonly IHistoryService _history;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public MaintenanceScheduler(
        ICommandExecutionService commands,
        IElevationService elevation,
        ISettingsService settings,
        IHistoryService history,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _commands = commands;
        _elevation = elevation;
        _settings = settings;
        _history = history;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<ScheduleResult> ApplyAsync(MaintenanceSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule.RunDiagnosis is false && schedule.RunSafeCleanup is false && schedule.GenerateReport is false)
        {
            return Failure("Schedule.Error.NothingSelected");
        }

        var taskName = SanitizeName(schedule.TaskName);
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return Failure("Schedule.Error.NoExecutable");
        }

        var action = "\"" + executable + "\" " + MaintenanceArgument;
        var arguments = new List<string> { "/create", "/tn", taskName, "/tr", action };

        switch (schedule.Frequency)
        {
            case MaintenanceFrequency.Daily:
                arguments.AddRange(new[] { "/sc", "DAILY" });
                break;

            case MaintenanceFrequency.Weekly:
                arguments.AddRange(new[] { "/sc", "WEEKLY", "/d", WeekdayArgument(schedule.DayOfWeek) });
                break;

            default:
                arguments.AddRange(new[] { "/sc", "MONTHLY", "/d", Math.Clamp(schedule.DayOfMonth, 1, 31).ToString(CultureInfo.InvariantCulture) });
                break;
        }

        if (TryReadTime(schedule.TimeOfDay, out var time) is false)
        {
            return Failure("Schedule.Error.InvalidTime");
        }

        arguments.AddRange(new[] { "/st", time });
        arguments.Add("/f");

        var result = await RunSchTasksAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (result.Success is false)
        {
            return result;
        }

        schedule.TaskName = taskName;
        schedule.IsEnabled = true;
        await SaveScheduleAsync(schedule, cancellationToken).ConfigureAwait(false);
        await RecordHistoryAsync(
            _localizer["Schedule.History.Applied"],
            _localizer.Format("Schedule.History.AppliedDetail", taskName, Describe(schedule)),
            success: true,
            cancellationToken).ConfigureAwait(false);

        _logger.Info(LogCategory, "Tarefa de manutencao agendada (" + taskName + ", " + Describe(schedule) + ").");

        return new ScheduleResult
        {
            Success = true,
            MessageKey = "Schedule.Message.Applied"
        };
    }

    public async Task<ScheduleResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var taskName = SanitizeName(_settings.Current.Schedule.TaskName);
        if (await ExistsAsync(taskName, cancellationToken).ConfigureAwait(false) is false)
        {
            _settings.Current.Schedule.IsEnabled = false;
            await SaveScheduleAsync(_settings.Current.Schedule, cancellationToken).ConfigureAwait(false);
            return new ScheduleResult { Success = true, MessageKey = "Schedule.Message.NotConfigured" };
        }

        var arguments = new[] { "/delete", "/tn", taskName, "/f" };
        var result = await RunSchTasksAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (result.Success is false)
        {
            return result;
        }

        _settings.Current.Schedule.IsEnabled = false;
        await SaveScheduleAsync(_settings.Current.Schedule, cancellationToken).ConfigureAwait(false);
        await RecordHistoryAsync(
            _localizer["Schedule.History.Removed"],
            _localizer.Format("Schedule.History.RemovedDetail", taskName),
            success: true,
            cancellationToken).ConfigureAwait(false);

        _logger.Info(LogCategory, "Tarefa de manutencao removida (" + taskName + ").");

        return new ScheduleResult
        {
            Success = true,
            MessageKey = "Schedule.Message.Removed"
        };
    }

    /// <summary>
    /// Devolve a configuracao salva com o estado real da tarefa no Windows: o campo
    /// IsEnabled reflete a existencia da tarefa no Agendador, nao apenas a intencao
    /// gravada nas configuracoes.
    /// </summary>
    public async Task<MaintenanceSchedule> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var current = _settings.Current.Schedule;
        var taskName = SanitizeName(current.TaskName);
        var exists = await ExistsAsync(taskName, cancellationToken).ConfigureAwait(false);

        return new MaintenanceSchedule
        {
            IsEnabled = exists,
            Frequency = current.Frequency,
            DayOfWeek = current.DayOfWeek,
            DayOfMonth = current.DayOfMonth,
            TimeOfDay = current.TimeOfDay,
            RunDiagnosis = current.RunDiagnosis,
            RunSafeCleanup = current.RunSafeCleanup,
            GenerateReport = current.GenerateReport,
            LastRunUtc = current.LastRunUtc,
            TaskName = taskName
        };
    }

    private async Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken)
    {
        var result = await RunSchTasksAsync(
            new[] { "/query", "/tn", taskName },
            cancellationToken).ConfigureAwait(false);

        return result.Success;
    }

    /// <summary>
    /// Executa o schtasks revalidando os argumentos na lista branca. Quando o Windows
    /// nega a operacao sem elevacao, o retorno indica que o usuario precisa reiniciar
    /// o aplicativo como administrador — nunca tentamos elevar silenciosamente.
    /// </summary>
    private async Task<ScheduleResult> RunSchTasksAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var command = CommandAllowList.All.First(candidate =>
            string.Equals(candidate.Executable, "schtasks.exe", StringComparison.OrdinalIgnoreCase));
        var executable = CommandAllowList.ResolveExecutablePath(command);

        if (CommandAllowList.TryResolve(executable, arguments, out _) is false)
        {
            _logger.Warning(LogCategory, "Agendamento recusado pela lista branca de comandos.");
            return Failure("Schedule.Error.NotAllowed");
        }

        CommandResult result;
        try
        {
            result = await _commands.RunAllowedAsync(executable, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure("Schedule.Error.Cancelled");
        }

        if (result.Started is false || result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            _logger.Warning(LogCategory, "schtasks retornou codigo " + result.ExitCode + ". " + Truncate(detail));

            if (_elevation.IsElevated is false)
            {
                return Failure("Schedule.Error.NeedElevation", requiresElevation: true);
            }

            return Failure("Schedule.Error.CommandFailed");
        }

        return new ScheduleResult { Success = true };
    }

    private async Task SaveScheduleAsync(MaintenanceSchedule schedule, CancellationToken cancellationToken)
    {
        try
        {
            _settings.Current.Schedule = schedule;
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel gravar o agendamento nas configuracoes.", exception);
        }
    }

    private async Task RecordHistoryAsync(string action, string description, bool success, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(
                new HistoryEntry
                {
                    Category = HistoryCategoryKey,
                    Action = action,
                    Description = description,
                    Result = _localizer[success ? "Schedule.History.Success" : "Schedule.History.Failure"],
                    Success = success
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning(LogCategory, "Nao foi possivel registrar o agendamento no historico.", exception);
        }
    }

    private static ScheduleResult Failure(string messageKey, bool requiresElevation = false) => new()
    {
        Success = false,
        MessageKey = messageKey,
        RequiresElevation = requiresElevation
    };

    /// <summary>
    /// Nome da tarefa restrito ao conjunto aceito pela lista branca. Caracteres
    /// inesperados sao removidos em vez de escapados.
    /// </summary>
    private static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultTaskName;
        }

        var clean = new string(value.Trim().Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-').ToArray());

        if (clean.Length == 0)
        {
            return DefaultTaskName;
        }

        return clean.Length > 64 ? clean[..64] : clean;
    }

    private static string WeekdayArgument(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN"
    };

    private static bool TryReadTime(string? value, out string time)
    {
        time = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var parsed) is false)
        {
            return false;
        }

        time = parsed.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        return true;
    }

    private string Describe(MaintenanceSchedule schedule)
    {
        var frequency = _localizer["Schedule.Frequency." + schedule.Frequency];
        if (schedule.Frequency == MaintenanceFrequency.Weekly)
        {
            frequency += " · " + _localizer["Schedule.Weekday." + schedule.DayOfWeek];
        }
        else if (schedule.Frequency == MaintenanceFrequency.Monthly)
        {
            frequency += " · " + _localizer.Format("Schedule.DayOfMonth", Math.Clamp(schedule.DayOfMonth, 1, 31));
        }

        return frequency + " · " + schedule.TimeOfDay;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 400 ? single : single[..400];
    }
}
