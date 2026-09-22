using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.DependencyInjection;
using OptimizerPC.Services.Storage;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Configuracoes do aplicativo. Cada preferencia e gravada em arquivo local assim que
/// muda. O que depende do Windows (iniciar com o sistema, tarefa agendada) e aplicado
/// por mecanismos oficiais e informado com honestidade quando exige elevacao.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OptimizerPC";
    private const int LogViewerLimit = 300;

    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IMonitoringService _monitoring;
    private readonly IMaintenanceScheduler _scheduler;
    private readonly IRegistryService _registry;
    private readonly INotificationService _notifications;
    private readonly IElevationService _elevation;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly LogRepository _logs;
    private readonly IServiceProvider _provider;

    private bool _suppressPersist;
    private bool _isLoaded;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IMonitoringService monitoring,
        IMaintenanceScheduler scheduler,
        IRegistryService registry,
        INotificationService notifications,
        IElevationService elevation,
        IDialogService dialogs,
        IAppPaths paths,
        LogRepository logs,
        IServiceProvider provider,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _settings = settings;
        _theme = theme;
        _monitoring = monitoring;
        _scheduler = scheduler;
        _registry = registry;
        _notifications = notifications;
        _elevation = elevation;
        _dialogs = dialogs;
        _paths = paths;
        _logs = logs;
        _provider = provider;

        BuildChoices();
        LoadFromSettings();

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanAct));
            }
        };
    }

    /// <summary>Opcao de lista suspensa com valor tipado e texto ja traduzido.</summary>
    public sealed class Choice<T>
        where T : struct
    {
        public Choice(T value, string text)
        {
            Value = value;
            Text = text;
        }

        public T Value { get; }

        public string Text { get; }
    }

    public ObservableCollection<Choice<AppLanguage>> Languages { get; } = new();

    public ObservableCollection<Choice<ThemeMode>> Themes { get; } = new();

    public ObservableCollection<Choice<int>> Intervals { get; } = new();

    public ObservableCollection<Choice<int>> Retentions { get; } = new();

    public ObservableCollection<Choice<LogLevel>> LogLevels { get; } = new();

    public ObservableCollection<Choice<MaintenanceFrequency>> Frequencies { get; } = new();

    public ObservableCollection<Choice<DayOfWeek>> Weekdays { get; } = new();

    public ObservableCollection<LogItemViewModel> LogEntries { get; } = new();

    [ObservableProperty]
    private Choice<AppLanguage>? _selectedLanguage;

    [ObservableProperty]
    private Choice<ThemeMode>? _selectedTheme;

    [ObservableProperty]
    private Choice<int>? _selectedInterval;

    [ObservableProperty]
    private Choice<int>? _selectedRetention;

    [ObservableProperty]
    private Choice<LogLevel>? _selectedLogLevel;

    [ObservableProperty]
    private Choice<MaintenanceFrequency>? _selectedFrequency;

    [ObservableProperty]
    private Choice<DayOfWeek>? _selectedWeekday;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _reducedMotion;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _confirmBeforeActions;

    [ObservableProperty]
    private bool _createRestorePointBeforeChanges;

    [ObservableProperty]
    private bool _persistLogsInDatabase;

    [ObservableProperty]
    private bool _scheduleEnabled;

    [ObservableProperty]
    private string _scheduleTime = "12:00";

    [ObservableProperty]
    private bool _scheduleRunDiagnosis = true;

    [ObservableProperty]
    private bool _scheduleRunCleanup = true;

    [ObservableProperty]
    private bool _scheduleGenerateReport = true;

    [ObservableProperty]
    private string _scheduleStatusText = string.Empty;

    [ObservableProperty]
    private bool _autoOptimizationEnabled;

    [ObservableProperty]
    private bool _autoCleanTemporaryFiles = true;

    [ObservableProperty]
    private bool _autoCleanSafeCache = true;

    [ObservableProperty]
    private bool _autoOptimizeStartup;

    [ObservableProperty]
    private bool _autoAdjustPowerPlan;

    [ObservableProperty]
    private bool _autoFreeUpSpace = true;

    [ObservableProperty]
    private bool _autoEmptyRecycleBin;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _hasLogs;

    [ObservableProperty]
    private string _logsSummaryText = string.Empty;

    public string Subtitle => Localizer["Settings.Subtitle"];

    public string AppearanceTitle => Localizer["Settings.Appearance.Title"];

    public string LanguageLabel => Localizer["Settings.Language.Label"];

    public string LanguageNote => Localizer["Settings.Language.Note"];

    public string ThemeLabel => Localizer["Settings.Theme.Label"];

    public string ThemeNote => Localizer["Settings.Theme.Note"];

    public string MotionLabel => Localizer["Settings.Motion.Label"];

    public string MotionNote => Localizer["Settings.Motion.Note"];

    public string StartupTitle => Localizer["Settings.Startup.Title"];

    public string StartWithWindowsLabel => Localizer["Settings.StartWithWindows.Label"];

    public string StartWithWindowsNote => Localizer["Settings.StartWithWindows.Note"];

    public string MinimizeToTrayLabel => Localizer["Settings.MinimizeToTray.Label"];

    public string MinimizeToTrayNote => Localizer["Settings.MinimizeToTray.Note"];

    public string BehaviorTitle => Localizer["Settings.Behavior.Title"];

    public string ConfirmLabel => Localizer["Settings.Confirm.Label"];

    public string ConfirmNote => Localizer["Settings.Confirm.Note"];

    public string RestorePointLabel => Localizer["Settings.RestorePoint.Label"];

    public string RestorePointNote => Localizer["Settings.RestorePoint.Note"];

    public string NotificationsLabel => Localizer["Settings.Notifications.Label"];

    public string NotificationsNote => Localizer["Settings.Notifications.Note"];

    public string TestNotificationText => Localizer["Settings.Notifications.Test"];

    public string MonitoringTitle => Localizer["Settings.Monitoring.Title"];

    public string IntervalLabel => Localizer["Settings.Interval.Label"];

    public string IntervalNote => Localizer["Settings.Interval.Note"];

    public string ScheduleTitle => Localizer["Settings.Schedule.Title"];

    public string ScheduleEnableLabel => Localizer["Settings.Schedule.Enable"];

    public string ScheduleNote => Localizer["Settings.Schedule.Note"];

    public string FrequencyLabel => Localizer["Settings.Schedule.Frequency"];

    public string WeekdayLabel => Localizer["Settings.Schedule.Weekday"];

    public string TimeLabel => Localizer["Settings.Schedule.Time"];

    public string TimeNote => Localizer["Settings.Schedule.Time.Note"];

    public string ScheduleActionsLabel => Localizer["Settings.Schedule.Actions"];

    public string ScheduleRunDiagnosisLabel => Localizer["Settings.Schedule.RunDiagnosis"];

    public string ScheduleRunCleanupLabel => Localizer["Settings.Schedule.RunCleanup"];

    public string ScheduleGenerateReportLabel => Localizer["Settings.Schedule.GenerateReport"];

    public string ScheduleApplyText => Localizer["Settings.Schedule.Apply"];

    public string ScheduleRemoveText => Localizer["Settings.Schedule.Remove"];

    public string AutoTitle => Localizer["Settings.Auto.Title"];

    public string AutoEnableLabel => Localizer["Settings.Auto.Enable"];

    public string AutoNote => Localizer["Settings.Auto.Note"];

    public string AutoCleanTempLabel => Localizer["Settings.Auto.CleanTemp"];

    public string AutoCleanCacheLabel => Localizer["Settings.Auto.CleanCache"];

    public string AutoStartupLabel => Localizer["Settings.Auto.Startup"];

    public string AutoPowerLabel => Localizer["Settings.Auto.Power"];

    public string AutoSpaceLabel => Localizer["Settings.Auto.Space"];

    public string AutoRecycleLabel => Localizer["Settings.Auto.Recycle"];

    public string LogsTitle => Localizer["Settings.Logs.Title"];

    public string LogLevelLabel => Localizer["Settings.Logs.Level"];

    public string LogLevelNote => Localizer["Settings.Logs.Level.Note"];

    public string PersistLogsLabel => Localizer["Settings.Logs.Persist"];

    public string PersistLogsNote => Localizer["Settings.Logs.Persist.Note"];

    public string RetentionLabel => Localizer["Settings.Logs.Retention"];

    public string RetentionNote => Localizer["Settings.Logs.Retention.Note"];

    public string LogViewerTitle => Localizer["Settings.Logs.Viewer.Title"];

    public string LogViewerEmptyText => Localizer["Settings.Logs.Viewer.Empty"];

    public string LogViewerHint => Localizer["Settings.Logs.Viewer.Hint"];

    public string RefreshLogsText => Localizer["Settings.Logs.Viewer.Refresh"];

    public string ClearLogsText => Localizer["Settings.Logs.Viewer.Clear"];

    public string PathsTitle => Localizer["Settings.Paths.Title"];

    public string DataFolderLabel => Localizer["Settings.Paths.Data"];

    public string LogsFolderLabel => Localizer["Settings.Paths.Logs"];

    public string ReportsFolderLabel => Localizer["Settings.Paths.Reports"];

    public string CopyPathText => Localizer["Settings.Paths.Copy"];

    public string PrivacyTitle => Localizer["Settings.About.Privacy.Title"];

    public string PrivacyText => Localizer["Settings.About.Privacy"];

    public string AboutTitle => Localizer["Settings.About.Title"];

    public string ElevationTitle => Localizer["Settings.About.Elevation"];

    public string ElevationNote => Localizer["Settings.About.Elevation.Note"];

    public string RestartElevatedText => Localizer["Settings.About.Restart"];

    public string ResetText => Localizer["Settings.About.Reset"];

    public string VersionText { get; } = ResolveVersion();

    public string DataFolder => _paths.DataFolder;

    public string LogsFolder => _paths.LogsFolder;

    public string ReportsFolder => _paths.ReportsFolder;

    public string ElevationText => Localizer[IsElevated ? "Shell.Elevation.Admin" : "Shell.Elevation.Standard"];

    public bool CanAct => IsBusy is false;

    public bool ShowWeekday => SelectedFrequency?.Value is MaintenanceFrequency.Weekly;

    public bool ShowScheduleFields => ScheduleEnabled;

    public bool HasScheduleStatus => ScheduleStatusText.Length > 0;

    public string VersionLine => Localizer.Format("Settings.About.Version", VersionText);

    protected override async Task OnNavigatedToAsync()
    {
        LoadFromSettings();
        await LoadScheduleAsync().ConfigureAwait(true);
        await LoadLogsAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        BuildChoices();
        RefreshDerived();

        foreach (var entry in LogEntries)
        {
            entry.Refresh();
        }
    }

    [RelayCommand]
    private async Task ApplyScheduleAsync()
    {
        var schedule = new MaintenanceSchedule
        {
            IsEnabled = true,
            Frequency = SelectedFrequency?.Value ?? MaintenanceFrequency.Weekly,
            DayOfWeek = SelectedWeekday?.Value ?? DayOfWeek.Sunday,
            TimeOfDay = ScheduleTime.Trim(),
            RunDiagnosis = ScheduleRunDiagnosis,
            RunSafeCleanup = ScheduleRunCleanup,
            GenerateReport = ScheduleGenerateReport,
            TaskName = _settings.Current.Schedule.TaskName
        };

        var (ok, result) = await RunAsync<ScheduleResult>(
            token => _scheduler.ApplyAsync(schedule, token),
            "Settings.Schedule.Applying").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportScheduleResultAsync(result, "Settings.Schedule.Applied").ConfigureAwait(true);
        await LoadScheduleAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveScheduleAsync()
    {
        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Settings.Schedule.Title",
                "Settings.Schedule.Remove.Confirm",
                "Settings.Schedule.Remove",
                Localizer["Settings.Schedule.Remove.Detail"],
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ScheduleResult>(
            token => _scheduler.RemoveAsync(token),
            "Settings.Schedule.Removing").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportScheduleResultAsync(result, "Settings.Schedule.Removed").ConfigureAwait(true);
        await LoadScheduleAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshLogsAsync() => await LoadLogsAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        if (LogEntries.Count == 0)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Settings.Logs.Viewer.Clear.Title",
                "Settings.Logs.Viewer.Clear.Confirm",
                "Settings.Logs.Viewer.Clear.Action",
                Localizer.Format("Settings.Logs.Viewer.Clear.Detail", LogEntries.Count),
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, removed) = await RunAsync<int>(
            token => _logs.ClearAsync(token),
            "Settings.Logs.Viewer.Clearing").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        SetStatusFormat("Settings.Logs.Viewer.Cleared", Severity.Ok, removed);
        await LoadLogsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        if (LogEntries.Count == 0)
        {
            return;
        }

        var text = string.Join(
            Environment.NewLine,
            LogEntries.Select(entry => entry.TimestampText + " [" + entry.LevelText + "] " + entry.Category + " · " + entry.Message));

        await CopyTextAsync(text, "Settings.Logs.Viewer.Copied").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await CopyTextAsync(path, "Settings.Paths.Copied").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TestNotificationAsync()
    {
        _notifications.Publish(new AppNotification
        {
            TitleKey = "Settings.Notifications.Test.Title",
            MessageKey = "Settings.Notifications.Test.Message",
            Severity = NotificationSeverity.Success
        });

        SetStatus("Settings.Notifications.Test.Sent", Severity.Ok);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestartElevatedAsync()
    {
        var confirmed = await _dialogs
            .ConfirmAsync("Settings.About.Elevation", "Settings.About.Restart.Confirm")
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var started = await _elevation.RestartElevatedAsync("Settings.About.Restart.Reason").ConfigureAwait(true);
        if (started is false)
        {
            await _dialogs.ShowWarningAsync("Settings.About.Elevation", "Settings.About.Restart.Cancelled").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ResetDefaultsAsync()
    {
        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Settings.About.Reset.Title",
                "Settings.About.Reset.Confirm",
                "Settings.About.Reset.Action",
                Localizer["Settings.About.Reset.Detail"],
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, _) = await RunAsync<bool>(
            async token =>
            {
                ApplyDefaults();
                await _settings.SaveAsync(token).ConfigureAwait(true);
                return true;
            },
            "Settings.About.Resetting").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        SetStatus("Settings.About.Reset.Done", Severity.Ok);

        _theme.Apply(_settings.Current.Theme);
        Controls.Motion.Enabled = _settings.Current.ReducedMotion is false;
        _monitoring.Interval = TimeSpan.FromMilliseconds(_settings.Current.MonitoringIntervalOrDefault);
        _provider.ApplyLoggingPreferences();
    }

    private void ApplyDefaults()
    {
        var defaults = new AppSettings();
        var current = _settings.Current;

        current.Theme = defaults.Theme;
        current.StartWithWindows = defaults.StartWithWindows;
        current.MinimizeToTray = defaults.MinimizeToTray;
        current.NotificationsEnabled = defaults.NotificationsEnabled;
        current.ReducedMotion = defaults.ReducedMotion;
        current.MonitoringIntervalMs = defaults.MonitoringIntervalMs;
        current.ConfirmBeforeActions = defaults.ConfirmBeforeActions;
        current.CreateRestorePointBeforeChanges = defaults.CreateRestorePointBeforeChanges;
        current.ShowTemperatureWhenAvailable = defaults.ShowTemperatureWhenAvailable;
        current.MinimumLogLevel = defaults.MinimumLogLevel;
        current.PersistLogsInDatabase = defaults.PersistLogsInDatabase;
        current.LogRetentionDays = defaults.LogRetentionDays;
        current.AutoOptimizationEnabled = defaults.AutoOptimizationEnabled;
        current.AutoOptimization = defaults.AutoOptimization;

        TryWriteRunEntry(defaults.StartWithWindows);

        LoadFromSettings();
    }

    private async Task LoadScheduleAsync()
    {
        var (ok, schedule) = await RunAsync<MaintenanceSchedule>(
            token => _scheduler.GetCurrentAsync(token),
            "Settings.Schedule.Loading").ConfigureAwait(true);

        if (ok is false || schedule is null)
        {
            return;
        }

        _suppressPersist = true;
        ScheduleEnabled = schedule.IsEnabled;
        ScheduleTime = schedule.TimeOfDay;
        ScheduleRunDiagnosis = schedule.RunDiagnosis;
        ScheduleRunCleanup = schedule.RunSafeCleanup;
        ScheduleGenerateReport = schedule.GenerateReport;
        SelectChoice(Frequencies, choice => choice.Value == schedule.Frequency, value => SelectedFrequency = value);
        SelectChoice(Weekdays, choice => choice.Value == schedule.DayOfWeek, value => SelectedWeekday = value);
        _suppressPersist = false;

        ScheduleStatusText = schedule.IsEnabled
            ? Localizer.Format("Settings.Schedule.Status.Active", DescribeSchedule(schedule))
            : Localizer["Settings.Schedule.Status.Inactive"];

        RefreshDerived();
    }

    private async Task LoadLogsAsync()
    {
        var (ok, records) = await RunAsync<IReadOnlyList<LogRecord>>(
            token => _logs.QueryAsync(LogViewerLimit, null, token),
            "Settings.Logs.Viewer.Loading").ConfigureAwait(true);

        if (ok is false || records is null)
        {
            return;
        }

        LogEntries.Clear();
        foreach (var record in records)
        {
            LogEntries.Add(new LogItemViewModel(record, Localizer));
        }

        HasLogs = LogEntries.Count > 0;
        LogsSummaryText = HasLogs
            ? Localizer.Format("Settings.Logs.Viewer.Count", LogEntries.Count)
            : string.Empty;

        RefreshDerived();
    }

    private void LoadFromSettings()
    {
        var current = _settings.Current;

        _suppressPersist = true;

        SelectChoice(Languages, choice => choice.Value == current.Language, value => SelectedLanguage = value);
        SelectChoice(Themes, choice => choice.Value == current.Theme, value => SelectedTheme = value);
        SelectChoice(Intervals, choice => choice.Value == current.MonitoringIntervalOrDefault, value => SelectedInterval = value);
        SelectChoice(Retentions, choice => choice.Value == current.LogRetentionDays, value => SelectedRetention = value);
        SelectChoice(LogLevels, choice => choice.Value == current.MinimumLogLevel, value => SelectedLogLevel = value);

        StartWithWindows = ReadRunEntry();
        MinimizeToTray = current.MinimizeToTray;
        ReducedMotion = current.ReducedMotion;
        NotificationsEnabled = current.NotificationsEnabled;
        ConfirmBeforeActions = current.ConfirmBeforeActions;
        CreateRestorePointBeforeChanges = current.CreateRestorePointBeforeChanges;
        PersistLogsInDatabase = current.PersistLogsInDatabase;
        AutoOptimizationEnabled = current.AutoOptimizationEnabled;
        AutoCleanTemporaryFiles = current.AutoOptimization.CleanTemporaryFiles;
        AutoCleanSafeCache = current.AutoOptimization.CleanSafeCache;
        AutoOptimizeStartup = current.AutoOptimization.OptimizeStartup;
        AutoAdjustPowerPlan = current.AutoOptimization.AdjustPowerPlan;
        AutoFreeUpSpace = current.AutoOptimization.FreeUpSpace;
        AutoEmptyRecycleBin = current.AutoOptimization.EmptyRecycleBin;
        IsElevated = _elevation.IsElevated;

        _suppressPersist = false;
        _isLoaded = true;

        OnPropertyChanged(nameof(StartWithWindows));
        RefreshDerived();
    }

    private void BuildChoices()
    {
        SelectChoice(Languages, choice => choice.Value == Localizer.Current, value => SelectedLanguage = value);
        Languages.Clear();
        foreach (var language in Localizer.Available)
        {
            Languages.Add(new Choice<AppLanguage>(language, LanguageName(language)));
        }

        var theme = Themes.FirstOrDefault(choice => choice.Value == _theme.Current)?.Value ?? _theme.Current;
        Themes.Clear();
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        {
            Themes.Add(new Choice<ThemeMode>(mode, Localizer["Settings.Theme." + mode]));
        }

        Intervals.Clear();
        foreach (var milliseconds in new[] { 500, 1000, 2000, 5000 })
        {
            Intervals.Add(new Choice<int>(milliseconds, Localizer.Format("Settings.Interval.Option", milliseconds)));
        }

        Retentions.Clear();
        foreach (var days in new[] { 7, 14, 30, 60, 90 })
        {
            Retentions.Add(new Choice<int>(days, Localizer.Format("Settings.Retention.Option", days)));
        }

        LogLevels.Clear();
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error })
        {
            LogLevels.Add(new Choice<LogLevel>(level, Localizer["Settings.Logs.Level." + level]));
        }

        Frequencies.Clear();
        foreach (var frequency in new[] { MaintenanceFrequency.Daily, MaintenanceFrequency.Weekly, MaintenanceFrequency.Monthly })
        {
            Frequencies.Add(new Choice<MaintenanceFrequency>(frequency, Localizer["Schedule.Frequency." + frequency]));
        }

        Weekdays.Clear();
        foreach (var day in new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        })
        {
            Weekdays.Add(new Choice<DayOfWeek>(day, Localizer["Schedule.Weekday." + day]));
        }

        _suppressPersist = true;
        SelectChoice(Languages, choice => choice.Value == Localizer.Current, value => SelectedLanguage = value);
        SelectChoice(Themes, choice => choice.Value == theme, value => SelectedTheme = value);

        var settings = _settings.Current;
        SelectChoice(Intervals, choice => choice.Value == settings.MonitoringIntervalOrDefault, value => SelectedInterval = value);
        SelectChoice(Retentions, choice => choice.Value == settings.LogRetentionDays, value => SelectedRetention = value);
        SelectChoice(LogLevels, choice => choice.Value == settings.MinimumLogLevel, value => SelectedLogLevel = value);
        SelectChoice(Frequencies, choice => choice.Value == _settings.Current.Schedule.Frequency, value => SelectedFrequency = value);
        SelectChoice(Weekdays, choice => choice.Value == _settings.Current.Schedule.DayOfWeek, value => SelectedWeekday = value);
        _suppressPersist = false;
    }

    private static string LanguageName(AppLanguage language) => language switch
    {
        AppLanguage.EnUs => "English (US)",
        AppLanguage.Es => "Español",
        _ => "Português (Brasil)"
    };

    private static string ResolveVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : version.Major + "." + version.Minor;
    }

    private static void SelectChoice<T>(
        IEnumerable<Choice<T>> choices,
        Func<Choice<T>, bool> predicate,
        Action<Choice<T>> assign)
        where T : struct
    {
        var match = choices.FirstOrDefault(predicate) ?? choices.FirstOrDefault();
        if (match is not null)
        {
            assign(match);
        }
    }

    private bool ReadRunEntry()
    {
        try
        {
            var value = _registry.GetValue(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName);
            return string.IsNullOrWhiteSpace(value?.StringValue) is false;
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", "Nao foi possivel ler a inicializacao automatica: " + exception.Message);
            return false;
        }
    }

    private bool TryWriteRunEntry(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return false;
                }

                _registry.SetString(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName, "\"" + executable + "\"");
            }
            else
            {
                _registry.DeleteValue(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName);
            }

            return true;
        }
        catch (Exception exception)
        {
            Logger.Error("Settings", "Nao foi possivel alterar a inicializacao automatica.", exception);
            return false;
        }
    }

    private async Task ReportScheduleResultAsync(ScheduleResult result, string successKey)
    {
        var messageKey = string.IsNullOrWhiteSpace(result.MessageKey)
            ? (result.Success ? successKey : "Settings.Schedule.Failed")
            : result.MessageKey;

        if (result.Success)
        {
            SetStatus(messageKey, Severity.Ok);
            await _dialogs.ShowSuccessAsync("Settings.Schedule.Title", messageKey).ConfigureAwait(true);
            return;
        }

        SetStatus(messageKey, result.RequiresElevation ? Severity.Warning : Severity.Critical);

        await _dialogs
            .ShowWarningAsync(
                "Settings.Schedule.Title",
                messageKey,
                result.RequiresElevation ? Localizer["Settings.Schedule.Elevation"] : null)
            .ConfigureAwait(true);
    }

    private async Task CopyTextAsync(string text, string statusKey)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus(statusKey, Severity.Ok);
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", "Nao foi possivel copiar para a area de transferencia: " + exception.Message);
            await _dialogs.ShowWarningAsync("Settings.Paths.Title", "Settings.Paths.Copy.Failed").ConfigureAwait(true);
        }
    }

    private string DescribeSchedule(MaintenanceSchedule schedule)
    {
        var frequency = Localizer["Schedule.Frequency." + schedule.Frequency];

        if (schedule.Frequency is MaintenanceFrequency.Weekly)
        {
            frequency += " · " + Localizer["Schedule.Weekday." + schedule.DayOfWeek];
        }

        return frequency + " · " + schedule.TimeOfDay;
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AppearanceTitle));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageNote));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeNote));
        OnPropertyChanged(nameof(MotionLabel));
        OnPropertyChanged(nameof(MotionNote));
        OnPropertyChanged(nameof(StartupTitle));
        OnPropertyChanged(nameof(StartWithWindowsLabel));
        OnPropertyChanged(nameof(StartWithWindowsNote));
        OnPropertyChanged(nameof(MinimizeToTrayLabel));
        OnPropertyChanged(nameof(MinimizeToTrayNote));
        OnPropertyChanged(nameof(BehaviorTitle));
        OnPropertyChanged(nameof(ConfirmLabel));
        OnPropertyChanged(nameof(ConfirmNote));
        OnPropertyChanged(nameof(RestorePointLabel));
        OnPropertyChanged(nameof(RestorePointNote));
        OnPropertyChanged(nameof(NotificationsLabel));
        OnPropertyChanged(nameof(NotificationsNote));
        OnPropertyChanged(nameof(TestNotificationText));
        OnPropertyChanged(nameof(MonitoringTitle));
        OnPropertyChanged(nameof(IntervalLabel));
        OnPropertyChanged(nameof(IntervalNote));
        OnPropertyChanged(nameof(ScheduleTitle));
        OnPropertyChanged(nameof(ScheduleEnableLabel));
        OnPropertyChanged(nameof(ScheduleNote));
        OnPropertyChanged(nameof(FrequencyLabel));
        OnPropertyChanged(nameof(WeekdayLabel));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(TimeNote));
        OnPropertyChanged(nameof(ScheduleActionsLabel));
        OnPropertyChanged(nameof(ScheduleRunDiagnosisLabel));
        OnPropertyChanged(nameof(ScheduleRunCleanupLabel));
        OnPropertyChanged(nameof(ScheduleGenerateReportLabel));
        OnPropertyChanged(nameof(ScheduleApplyText));
        OnPropertyChanged(nameof(ScheduleRemoveText));
        OnPropertyChanged(nameof(AutoTitle));
        OnPropertyChanged(nameof(AutoEnableLabel));
        OnPropertyChanged(nameof(AutoNote));
        OnPropertyChanged(nameof(AutoCleanTempLabel));
        OnPropertyChanged(nameof(AutoCleanCacheLabel));
        OnPropertyChanged(nameof(AutoStartupLabel));
        OnPropertyChanged(nameof(AutoPowerLabel));
        OnPropertyChanged(nameof(AutoSpaceLabel));
        OnPropertyChanged(nameof(AutoRecycleLabel));
        OnPropertyChanged(nameof(LogsTitle));
        OnPropertyChanged(nameof(LogLevelLabel));
        OnPropertyChanged(nameof(LogLevelNote));
        OnPropertyChanged(nameof(PersistLogsLabel));
        OnPropertyChanged(nameof(PersistLogsNote));
        OnPropertyChanged(nameof(RetentionLabel));
        OnPropertyChanged(nameof(RetentionNote));
        OnPropertyChanged(nameof(LogViewerTitle));
        OnPropertyChanged(nameof(LogViewerEmptyText));
        OnPropertyChanged(nameof(LogViewerHint));
        OnPropertyChanged(nameof(RefreshLogsText));
        OnPropertyChanged(nameof(ClearLogsText));
        OnPropertyChanged(nameof(PathsTitle));
        OnPropertyChanged(nameof(DataFolderLabel));
        OnPropertyChanged(nameof(LogsFolderLabel));
        OnPropertyChanged(nameof(ReportsFolderLabel));
        OnPropertyChanged(nameof(CopyPathText));
        OnPropertyChanged(nameof(PrivacyTitle));
        OnPropertyChanged(nameof(PrivacyText));
        OnPropertyChanged(nameof(AboutTitle));
        OnPropertyChanged(nameof(ElevationTitle));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(RestartElevatedText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(VersionLine));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(ShowWeekday));
        OnPropertyChanged(nameof(ShowScheduleFields));
        OnPropertyChanged(nameof(HasScheduleStatus));
        OnPropertyChanged(nameof(LogsSummaryText));
    }

    private void Persist()
    {
        if (_suppressPersist || _isLoaded is false)
        {
            return;
        }

        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", "Nao foi possivel gravar as configuracoes: " + exception.Message);
            await _dialogs.ShowWarningAsync("Settings.Title", "Settings.Save.Failed").ConfigureAwait(true);
        }
    }

    partial void OnSelectedLanguageChanged(Choice<AppLanguage>? value)
    {
        if (value is null || value.Value == Localizer.Current)
        {
            return;
        }

        Localizer.SetLanguage(value.Value);
        _settings.Current.Language = value.Value;
        Persist();
    }

    partial void OnSelectedThemeChanged(Choice<ThemeMode>? value)
    {
        if (value is null || value.Value == _theme.Current)
        {
            return;
        }

        _theme.Apply(value.Value);
        _settings.Current.Theme = value.Value;
        Persist();
    }

    partial void OnSelectedIntervalChanged(Choice<int>? value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Current.MonitoringIntervalMs = value.Value;
        _monitoring.Interval = TimeSpan.FromMilliseconds(value.Value);
        Persist();
    }

    partial void OnSelectedRetentionChanged(Choice<int>? value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Current.LogRetentionDays = value.Value;
        _provider.ApplyLoggingPreferences();
        Persist();
    }

    partial void OnSelectedLogLevelChanged(Choice<LogLevel>? value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Current.MinimumLogLevel = value.Value;
        _provider.ApplyLoggingPreferences();
        Persist();
    }

    partial void OnSelectedFrequencyChanged(Choice<MaintenanceFrequency>? value) => OnPropertyChanged(nameof(ShowWeekday));

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressPersist || _isLoaded is false)
        {
            return;
        }

        if (TryWriteRunEntry(value) is false)
        {
            _suppressPersist = true;
            var actual = ReadRunEntry();
            StartWithWindows = actual;
            _suppressPersist = false;

            OnPropertyChanged(nameof(StartWithWindows));
            _ = _dialogs
                .ShowWarningAsync("Settings.Startup.Title", "Settings.StartWithWindows.Failed")
                .ConfigureAwait(true);
            return;
        }

        _settings.Current.StartWithWindows = value;
        Persist();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settings.Current.MinimizeToTray = value;
        Persist();
    }

    partial void OnReducedMotionChanged(bool value)
    {
        Controls.Motion.Enabled = value is false;
        _settings.Current.ReducedMotion = value;
        Persist();
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        _settings.Current.NotificationsEnabled = value;
        Persist();
    }

    partial void OnConfirmBeforeActionsChanged(bool value)
    {
        _settings.Current.ConfirmBeforeActions = value;
        Persist();
    }

    partial void OnCreateRestorePointBeforeChangesChanged(bool value)
    {
        _settings.Current.CreateRestorePointBeforeChanges = value;
        Persist();
    }

    partial void OnPersistLogsInDatabaseChanged(bool value)
    {
        _settings.Current.PersistLogsInDatabase = value;
        _provider.ApplyLoggingPreferences();
        Persist();
    }

    partial void OnScheduleEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowScheduleFields));

    partial void OnAutoOptimizationEnabledChanged(bool value)
    {
        _settings.Current.AutoOptimizationEnabled = value;
        Persist();
    }

    partial void OnAutoCleanTemporaryFilesChanged(bool value)
    {
        _settings.Current.AutoOptimization.CleanTemporaryFiles = value;
        Persist();
    }

    partial void OnAutoCleanSafeCacheChanged(bool value)
    {
        _settings.Current.AutoOptimization.CleanSafeCache = value;
        Persist();
    }

    partial void OnAutoOptimizeStartupChanged(bool value)
    {
        _settings.Current.AutoOptimization.OptimizeStartup = value;
        Persist();
    }

    partial void OnAutoAdjustPowerPlanChanged(bool value)
    {
        _settings.Current.AutoOptimization.AdjustPowerPlan = value;
        Persist();
    }

    partial void OnAutoFreeUpSpaceChanged(bool value)
    {
        _settings.Current.AutoOptimization.FreeUpSpace = value;
        Persist();
    }

    partial void OnAutoEmptyRecycleBinChanged(bool value)
    {
        _settings.Current.AutoOptimization.EmptyRecycleBin = value;
        Persist();
    }

    partial void OnScheduleStatusTextChanged(string value) => OnPropertyChanged(nameof(HasScheduleStatus));
}
