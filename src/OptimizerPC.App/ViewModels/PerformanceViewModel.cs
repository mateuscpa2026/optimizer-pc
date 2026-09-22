using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.App.Views;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Desempenho em tempo real. As amostras vem do monitor local do proprio aplicativo:
/// uso de CPU, memoria, atividade de disco e rede, quantidade de processos e threads.
/// A tela tambem mostra o plano de energia ativo e o perfil de efeitos visuais, com
/// alteracao sempre confirmada e reversivel. Nada e enviado para fora do computador.
/// </summary>
public sealed partial class PerformanceViewModel : ViewModelBase
{
    private const int HistoryCapacity = 120;

    private static readonly int[] AllowedIntervals = { 500, 1000, 2000, 5000 };

    private readonly IMonitoringService _monitoring;
    private readonly IMetricsProvider _metrics;
    private readonly IPowerService _power;
    private readonly IVisualEffectsService _visualEffects;
    private readonly ISystemInfoService _system;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    private bool _applyingSettings;

    public PerformanceViewModel(
        IMonitoringService monitoring,
        IMetricsProvider metrics,
        IPowerService power,
        IVisualEffectsService visualEffects,
        ISystemInfoService system,
        ISettingsService settings,
        IDialogService dialogs,
        INavigationService navigation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _monitoring = monitoring;
        _metrics = metrics;
        _power = power;
        _visualEffects = visualEffects;
        _system = system;
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;

        IntervalOptions = AllowedIntervals
            .Select(value => new IntervalOption(value, localizer))
            .ToList();

        _selectedInterval = settings.Current.MonitoringIntervalOrDefault;
    }

    public ObservableCollection<double> CpuHistory { get; } = new();

    public ObservableCollection<double> MemoryHistory { get; } = new();

    public ObservableCollection<double> DiskHistory { get; } = new();

    public ObservableCollection<double> NetworkHistory { get; } = new();

    public ObservableCollection<PowerPlanItemViewModel> PowerPlans { get; } = new();

    public IReadOnlyList<IntervalOption> IntervalOptions { get; private set; }

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private bool _isDiskActivityAvailable;
    [ObservableProperty] private double _networkBytesPerSecond;
    [ObservableProperty] private bool _isNetworkActivityAvailable;
    [ObservableProperty] private string _memoryText = string.Empty;
    [ObservableProperty] private string _diskSpeedText = string.Empty;
    [ObservableProperty] private string _networkSpeedText = string.Empty;
    [ObservableProperty] private int _processCount;
    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private int _selectedInterval;
    [ObservableProperty] private string _updatedText = string.Empty;
    [ObservableProperty] private PowerPlanItemViewModel? _selectedPlan;
    [ObservableProperty] private bool _isEffectsOptimized;
    [ObservableProperty] private bool _effectsAvailable = true;
    [ObservableProperty] private string _machineSummary = string.Empty;
    [ObservableProperty] private string _memoryModulesText = string.Empty;
    [ObservableProperty] private string _gpuText = string.Empty;
    [ObservableProperty] private string _osText = string.Empty;
    [ObservableProperty] private string _uptimeText = string.Empty;
    [ObservableProperty] private bool _hasHardwareInfo;

    public bool HasPowerPlans => PowerPlans.Count > 0;

    public bool CanApplyPlan =>
        IsBusy is false && SelectedPlan is not null && SelectedPlan.IsActive is false;

    public string CpuText => Humanize.Percent(CpuPercent, 1);

    public string MemoryPercentText => Humanize.Percent(MemoryPercent, 1);

    public string DiskPercentText => IsDiskActivityAvailable
        ? Humanize.Percent(DiskPercent, 1)
        : Localizer["Common.NotAvailable"];

    public string NetworkText => IsNetworkActivityAvailable
        ? Humanize.Speed(NetworkBytesPerSecond)
        : Localizer["Common.NotAvailable"];

    public string ProcessCountText => ProcessCount.ToString("N0");

    public string ThreadCountText => ThreadCount.ToString("N0");

    public string ProcessCountLabel => Localizer["Performance.Processes.Label"];

    public string ThreadCountLabel => Localizer["Performance.Threads.Label"];

    public string IntervalLabel => Localizer["Performance.Interval.Label"];

    public string MonitoringHint => Localizer["Performance.Monitoring.Hint"];

    public string PrivacyNote => Localizer["Performance.Privacy.Note"];

    public string RealTimeNote => Localizer["Performance.RealTime.Note"];

    public string PowerTitle => Localizer["Performance.Power.Title"];

    public string PowerNote => Localizer["Performance.Power.Note"];

    public string PowerUnavailable => Localizer["Performance.Power.Unavailable"];

    public string EffectsTitle => Localizer["Performance.Effects.Title"];

    public string EffectsNote => Localizer["Performance.Effects.Note"];

    public string EffectsStateText => Localizer[
        IsEffectsOptimized ? "Performance.Effects.Performance" : "Performance.Effects.Appearance"];

    public string EffectsActionText => Localizer[
        IsEffectsOptimized ? "Performance.Effects.Restore" : "Performance.Effects.Optimize"];

    public string HardwareTitle => Localizer["Performance.Hardware.Title"];

    public string MemoryLabel => Localizer["Performance.Hardware.Memory"];

    public string GpuLabel => Localizer["Performance.Hardware.Gpu"];

    public string OsLabel => Localizer["Performance.Hardware.Os"];

    public string UptimeLabel => Localizer["Performance.Hardware.Uptime"];

    public string TemperatureNote => Localizer["Performance.Temperature.Note"];

    [RelayCommand]
    private void ToggleMonitoring()
    {
        if (_monitoring.IsRunning)
        {
            _monitoring.Stop();
            IsMonitoring = false;
            SetStatus("Performance.Status.Stopped", Severity.Info);
            return;
        }

        _monitoring.Interval = TimeSpan.FromMilliseconds(SelectedInterval);
        _monitoring.Start();
        IsMonitoring = _monitoring.IsRunning;
        SetStatus("Performance.Status.Running", Severity.Ok);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        CpuHistory.Clear();
        MemoryHistory.Clear();
        DiskHistory.Clear();
        NetworkHistory.Clear();
    }

    [RelayCommand]
    private async Task ApplyPlanAsync()
    {
        var plan = SelectedPlan;

        if (plan is null || IsBusy || plan.IsActive)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmAsync("Performance.Power.Confirm.Title", "Performance.Power.Confirm.Message", plan.Name, DialogKind.Question)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _power.SetActivePlanAsync(plan.Model, token),
            "Performance.Power.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync(
                    "Performance.Power.Error.Title",
                    string.IsNullOrEmpty(result.MessageKey) ? "Performance.Power.Unavailable" : result.MessageKey,
                    result.Detail)
                .ConfigureAwait(true);

            return;
        }

        SetStatus("Performance.Power.Status.Applied", Severity.Ok);
        await LoadPlansAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleVisualEffectsAsync()
    {
        if (IsBusy || EffectsAvailable is false)
        {
            return;
        }

        var optimize = IsEffectsOptimized is false;

        var confirmed = await _dialogs
            .ConfirmAsync(
                optimize ? "Performance.Effects.Confirm.Optimize.Title" : "Performance.Effects.Confirm.Restore.Title",
                optimize ? "Performance.Effects.Confirm.Optimize.Message" : "Performance.Effects.Confirm.Restore.Message",
                null,
                DialogKind.Question)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _visualEffects.ApplyAsync(optimize, token),
            "Performance.Effects.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync(
                    "Performance.Effects.Error.Title",
                    string.IsNullOrEmpty(result.MessageKey) ? "Performance.Effects.Unavailable" : result.MessageKey,
                    result.Detail)
                .ConfigureAwait(true);

            return;
        }

        SetStatus("Performance.Effects.Status.Applied", Severity.Ok);
        await LoadEffectsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task OpenRestoreAsync() => _navigation.NavigateAsync(Screen.Restore);

    protected override async Task OnInitializeAsync()
    {
        _monitoring.SampleAvailable += OnSampleAvailable;
        _settings.SettingsChanged += OnSettingsChanged;

        await RefreshAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        IsMonitoring = _monitoring.IsRunning;

        if (HasHardwareInfo is false)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    protected override void OnLanguageChanged()
    {
        IntervalOptions = AllowedIntervals
            .Select(value => new IntervalOption(value, Localizer))
            .ToList();

        OnPropertyChanged(nameof(IntervalOptions));
        RefreshDerived();
    }

    protected override void OnDispose()
    {
        _monitoring.SampleAvailable -= OnSampleAvailable;
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    partial void OnSelectedIntervalChanged(int value)
    {
        if (_applyingSettings)
        {
            return;
        }

        _monitoring.Interval = TimeSpan.FromMilliseconds(value);
        _ = PersistIntervalAsync(value);
    }

    partial void OnSelectedPlanChanged(PowerPlanItemViewModel? value)
        => OnPropertyChanged(nameof(CanApplyPlan));

    private async Task PersistIntervalAsync(int milliseconds)
    {
        try
        {
            _applyingSettings = true;
            _settings.Current.MonitoringIntervalMs = milliseconds;
            await _settings.SaveAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Performance", "Nao foi possivel salvar o intervalo de monitoramento.", exception);
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadHardwareAsync().ConfigureAwait(true);
        await LoadPlansAsync().ConfigureAwait(true);
        await LoadEffectsAsync().ConfigureAwait(true);

        IsMonitoring = _monitoring.IsRunning;

        if (IsMonitoring is false)
        {
            var (ok, sample) = await RunAsync<MetricSample?>(
                async token => _monitoring.LastSample ?? await _metrics.SampleAsync(token).ConfigureAwait(true),
                "Performance.Loading").ConfigureAwait(true);

            if (ok && sample is not null)
            {
                ApplySample(sample);
            }
        }
    }

    private async Task LoadHardwareAsync()
    {
        var (ok, snapshot) = await RunAsync<SystemSnapshot>(
            token => _system.GetSnapshotAsync(token),
            "Performance.Loading.Hardware").ConfigureAwait(true);

        if (ok is false || snapshot is null)
        {
            return;
        }

        HasHardwareInfo = true;

        OsText = snapshot.Os.FullVersionText;
        UptimeText = Humanize.Duration(snapshot.Os.Uptime, Localizer["Common.Duration.SubSecond"]);
        GpuText = BuildGpuText(snapshot);
        MemoryModulesText = BuildMemoryText(snapshot.Memory);
        MachineSummary = BuildMachineSummary(snapshot);
    }

    private async Task LoadPlansAsync()
    {
        var (ok, plans) = await RunAsync<IReadOnlyList<PowerPlanInfo>>(
            token => _power.GetPlansAsync(token),
            "Performance.Power.Loading").ConfigureAwait(true);

        if (ok is false || plans is null)
        {
            return;
        }

        PowerPlans.Clear();
        foreach (var plan in plans)
        {
            PowerPlans.Add(new PowerPlanItemViewModel(plan, Localizer));
        }

        SelectedPlan = PowerPlans.FirstOrDefault(plan => plan.IsActive);

        OnPropertyChanged(nameof(HasPowerPlans));
        OnPropertyChanged(nameof(CanApplyPlan));
    }

    private async Task LoadEffectsAsync()
    {
        try
        {
            IsEffectsOptimized = await _visualEffects
                .IsOptimizedForPerformanceAsync(CancellationToken.None)
                .ConfigureAwait(true);

            EffectsAvailable = true;
        }
        catch (Exception exception)
        {
            EffectsAvailable = false;
            Logger.Warning("Performance", "Nao foi possivel ler o perfil de efeitos visuais.", exception);
        }

        OnPropertyChanged(nameof(EffectsStateText));
        OnPropertyChanged(nameof(EffectsActionText));
    }

    private void OnSampleAvailable(object? sender, MetricSample sample)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess() is false)
        {
            dispatcher.BeginInvoke(new Action(() => OnSampleAvailable(sender, sample)));
            return;
        }

        ApplySample(sample);
    }

    private void ApplySample(MetricSample sample)
    {
        CpuPercent = Math.Round(sample.CpuPercent, 1);
        MemoryPercent = Math.Round(sample.MemoryPercent, 1);
        IsDiskActivityAvailable = sample.DiskActivityAvailable;
        DiskPercent = sample.DiskActivityAvailable ? Math.Round(sample.DiskActivityPercent, 1) : 0;
        IsNetworkActivityAvailable = sample.NetworkActivityAvailable;
        NetworkBytesPerSecond = sample.NetworkDownloadBytesPerSecond + sample.NetworkUploadBytesPerSecond;

        MemoryText = Humanize.Bytes(sample.MemoryUsedBytes) + " / " + Humanize.Bytes(sample.MemoryTotalBytes);
        DiskSpeedText = Humanize.Speed(sample.DiskReadBytesPerSecond) + " / " + Humanize.Speed(sample.DiskWriteBytesPerSecond);
        NetworkSpeedText = Humanize.Speed(sample.NetworkDownloadBytesPerSecond) + " / " + Humanize.Speed(sample.NetworkUploadBytesPerSecond);

        ProcessCount = sample.ProcessCount;
        ThreadCount = sample.ThreadCount;
        UpdatedText = Localizer.Format("Performance.Updated.At", Humanize.Date(sample.TimestampUtc.ToLocalTime()));
        IsMonitoring = _monitoring.IsRunning;

        Append(CpuHistory, CpuPercent);
        Append(MemoryHistory, MemoryPercent);

        if (IsDiskActivityAvailable)
        {
            Append(DiskHistory, DiskPercent);
        }

        if (IsNetworkActivityAvailable)
        {
            Append(NetworkHistory, NetworkBytesPerSecond);
        }

        RefreshLiveTexts();
    }

    private void ApplyIntervalFromSettings()
    {
        var value = _settings.Current.MonitoringIntervalOrDefault;
        if (value == SelectedInterval)
        {
            return;
        }

        _applyingSettings = true;
        try
        {
            SelectedInterval = value;
        }
        finally
        {
            _applyingSettings = false;
        }

        _monitoring.Interval = TimeSpan.FromMilliseconds(value);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        ApplyIntervalFromSettings();
        RefreshDerived();
    }

    private static void Append(ObservableCollection<double> history, double value)
    {
        if (history.Count >= HistoryCapacity)
        {
            history.RemoveAt(0);
        }

        history.Add(value);
    }

    private static string BuildGpuText(SystemSnapshot snapshot)
    {
        var names = snapshot.Gpus
            .Select(gpu => gpu.Name)
            .Where(name => string.IsNullOrWhiteSpace(name) is false)
            .ToList();

        return names.Count == 0 ? string.Empty : string.Join(" · ", names);
    }

    private static string BuildMemoryText(MemoryInfo memory)
    {
        if (memory.Modules.Count == 0)
        {
            return string.Empty;
        }

        var parts = memory.Modules
            .Where(module => module.CapacityBytes > 0)
            .Select(module => (string.IsNullOrWhiteSpace(module.Slot) ? string.Empty : module.Slot + ": ")
                + Humanize.Bytes(module.CapacityBytes)
                + (module.SpeedMhz > 0 ? " · " + module.SpeedMhz + " MHz" : string.Empty))
            .ToList();

        return string.Join("   ", parts);
    }

    private string BuildMachineSummary(SystemSnapshot snapshot)
    {
        var parts = new List<string>();

        parts.Add(string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? Localizer["Common.Cpu.Unknown"] : snapshot.Cpu.Name);

        if (snapshot.Cpu.PhysicalCores + snapshot.Cpu.LogicalProcessors > 0)
        {
            parts.Add(Humanize.Cores(snapshot.Cpu.PhysicalCores, snapshot.Cpu.LogicalProcessors, Localizer));
        }

        if (snapshot.Memory.TotalBytes > 0)
        {
            parts.Add(Humanize.Bytes(snapshot.Memory.TotalBytes) + " RAM");
        }

        return string.Join(" · ", parts);
    }

    private void RefreshLiveTexts()
    {
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(MemoryPercentText));
        OnPropertyChanged(nameof(DiskPercentText));
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(ProcessCountText));
        OnPropertyChanged(nameof(ThreadCountText));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasPowerPlans));
        OnPropertyChanged(nameof(CanApplyPlan));
        OnPropertyChanged(nameof(IntervalLabel));
        OnPropertyChanged(nameof(MonitoringHint));
        OnPropertyChanged(nameof(PrivacyNote));
        OnPropertyChanged(nameof(RealTimeNote));
        OnPropertyChanged(nameof(PowerTitle));
        OnPropertyChanged(nameof(PowerNote));
        OnPropertyChanged(nameof(PowerUnavailable));
        OnPropertyChanged(nameof(EffectsTitle));
        OnPropertyChanged(nameof(EffectsNote));
        OnPropertyChanged(nameof(EffectsStateText));
        OnPropertyChanged(nameof(EffectsActionText));
        OnPropertyChanged(nameof(HardwareTitle));
        OnPropertyChanged(nameof(MemoryLabel));
        OnPropertyChanged(nameof(GpuLabel));
        OnPropertyChanged(nameof(OsLabel));
        OnPropertyChanged(nameof(UptimeLabel));
        OnPropertyChanged(nameof(TemperatureNote));
        OnPropertyChanged(nameof(ProcessCountLabel));
        OnPropertyChanged(nameof(ThreadCountLabel));
        RefreshLiveTexts();
    }

    /// <summary>Intervalo de amostragem oferecido ao usuario.</summary>
    public sealed class IntervalOption
    {
        private readonly ILocalizer _localizer;

        public IntervalOption(int milliseconds, ILocalizer localizer)
        {
            Milliseconds = milliseconds;
            _localizer = localizer;
        }

        public int Milliseconds { get; }

        public string Text => _localizer.Format("Performance.Interval.Option", Milliseconds);
    }
}
