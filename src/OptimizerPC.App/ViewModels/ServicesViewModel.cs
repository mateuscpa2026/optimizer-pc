using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.App.Views;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Servicos do Windows. A tela lista o que esta instalado, permite iniciar, parar e
/// alterar o modo de inicio dos servicos comuns e mantem bloqueados, com o motivo
/// visivel, os servicos de seguranca e os essenciais ao funcionamento do sistema.
/// Toda alteracao e confirmada antes e registrada para restauracao.
/// </summary>
public sealed partial class ServicesViewModel : ViewModelBase
{
    private static readonly ServiceStartMode[] AllowedModes =
    {
        ServiceStartMode.Automatic,
        ServiceStartMode.AutomaticDelayed,
        ServiceStartMode.Manual,
        ServiceStartMode.Disabled
    };

    private readonly IServiceManager _services;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IElevationService _elevation;

    private readonly List<ServiceItemViewModel> _all = new();

    public ServicesViewModel(
        IServiceManager services,
        IDialogService dialogs,
        INavigationService navigation,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _services = services;
        _dialogs = dialogs;
        _navigation = navigation;
        _elevation = elevation;

        StartModeOptions = AllowedModes
            .Select(mode => new StartModeOption(mode, localizer))
            .ToList();
    }

    public ObservableCollection<ServiceItemViewModel> Items { get; } = new();

    public IReadOnlyList<StartModeOption> StartModeOptions { get; private set; }

    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private ServiceItemViewModel? _selectedService;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private bool _thirdPartyOnly;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _stoppedCount;
    [ObservableProperty] private int _protectedCount;
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private ServiceStartMode _selectedStartMode = ServiceStartMode.Automatic;

    public bool HasItems => Items.Count > 0;

    public bool IsFilteredEmpty => HasEntries && Items.Count == 0;

    public bool HasSelection => SelectedService is not null;

    public bool CanStart => SelectedService is { CanStart: true } && IsBusy is false;

    public bool CanStop => SelectedService is { CanStop: true, IsRunning: true } && IsBusy is false;

    /// <summary>Somente servicos comuns e nao criticos aceitam mudanca de modo de inicio.</summary>
    public bool CanChangeStartMode => SelectedService is { IsSystemCritical: false } && IsBusy is false;

    public bool CanApplyStartMode =>
        CanChangeStartMode && SelectedService is not null && SelectedService.StartMode != SelectedStartMode;

    public string RunningCountText => Localizer.Format("Services.Summary.Running", RunningCount);

    public string StoppedCountText => Localizer.Format("Services.Summary.Stopped", StoppedCount);

    public string ProtectedCountText => Localizer.Format("Services.Summary.Protected", ProtectedCount);

    public string ElevationNote => Localizer["Services.Elevation.Note"];

    public bool ShowElevationNote => IsElevated is false;

    public string SelectionTitle => SelectedService?.DisplayName ?? Localizer["Services.Selection.None"];

    public string SelectionPath => SelectedService?.ExecutablePath ?? string.Empty;

    public bool SelectionHasPath => SelectedService?.HasPath == true;

    public string SafetyNote => Localizer["Services.Safety.Note"];

    public string ChangeModeNote => Localizer["Services.StartMode.Note"];

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        RunningOnly = false;
        ThirdPartyOnly = false;
    }

    [RelayCommand]
    private Task OpenRestoreAsync() => _navigation.NavigateAsync(Screen.Restore);

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        var service = SelectedService;

        if (service is null || IsBusy || service.CanStart is false)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmAsync("Services.Start.Title", "Services.Start.Message", service.DisplayName, DialogKind.Question)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _services.StartAsync(service.Model, token),
            "Services.Start.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportAsync(result, service, "Services.Status.Started").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        var service = SelectedService;

        if (service is null || IsBusy || service.CanStop is false || service.IsRunning is false)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmAsync("Services.Stop.Title", "Services.Stop.Message", service.DisplayName, DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _services.StopAsync(service.Model, token),
            "Services.Stop.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportAsync(result, service, "Services.Status.Stopped").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ApplyStartModeAsync()
    {
        var service = SelectedService;

        if (service is null || IsBusy || CanApplyStartMode is false)
        {
            return;
        }

        var mode = SelectedStartMode;

        var confirmed = await _dialogs
            .ConfirmAsync(
                "Services.StartMode.Title",
                "Services.StartMode.Message",
                Localizer.Format("Services.StartMode.Detail", service.DisplayName, Localizer["Services.StartMode." + mode]),
                DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _services.SetStartModeAsync(service.Model, mode, token),
            "Services.StartMode.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportAsync(result, service, "Services.Status.StartModeChanged").ConfigureAwait(true);
    }

    protected override async Task OnInitializeAsync()
    {
        await LoadAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        IsElevated = _elevation.IsElevated;

        if (HasEntries is false)
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    protected override void OnLanguageChanged()
    {
        foreach (var item in _all)
        {
            item.Refresh();
        }

        StartModeOptions = AllowedModes
            .Select(mode => new StartModeOption(mode, Localizer))
            .ToList();

        OnPropertyChanged(nameof(StartModeOptions));
        RefreshDerived();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnRunningOnlyChanged(bool value) => ApplyFilter();

    partial void OnThirdPartyOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedServiceChanged(ServiceItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedStartMode = value.StartMode;
        }

        RefreshDerived();
    }

    partial void OnSelectedStartModeChanged(ServiceStartMode value)
        => OnPropertyChanged(nameof(CanApplyStartMode));

    private async Task LoadAsync()
    {
        var (ok, services) = await RunAsync<IReadOnlyList<WindowsServiceInfo>>(
            token => _services.GetServicesAsync(token),
            "Services.Loading").ConfigureAwait(true);

        if (ok is false || services is null)
        {
            return;
        }

        IsElevated = _elevation.IsElevated;

        var selectedName = SelectedService?.Name;

        _all.Clear();
        foreach (var service in services)
        {
            _all.Add(new ServiceItemViewModel(service, Localizer));
        }

        HasEntries = _all.Count > 0;
        ApplyFilter();

        if (selectedName is not null)
        {
            SelectedService = Items.FirstOrDefault(item =>
                string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        }

        RefreshSummary();

        SetStatus(
            HasEntries ? "Services.Status.Ready" : "Services.Status.Empty",
            HasEntries ? Severity.Ok : Severity.Info);
    }

    private void ApplyFilter()
    {
        var query = FilterText?.Trim() ?? string.Empty;

        Items.Clear();
        foreach (var item in _all)
        {
            if (RunningOnly && item.IsRunning is false)
            {
                continue;
            }

            if (ThirdPartyOnly && item.IsMicrosoft)
            {
                continue;
            }

            if (query.Length > 0 &&
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) is false &&
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            Items.Add(item);
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
    }

    private void RefreshSummary()
    {
        RunningCount = _all.Count(item => item.IsRunning);
        StoppedCount = _all.Count - RunningCount;
        ProtectedCount = _all.Count(item => item.IsSystemCritical);

        OnPropertyChanged(nameof(RunningCountText));
        OnPropertyChanged(nameof(StoppedCountText));
        OnPropertyChanged(nameof(ProtectedCountText));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanChangeStartMode));
        OnPropertyChanged(nameof(CanApplyStartMode));
        OnPropertyChanged(nameof(RunningCountText));
        OnPropertyChanged(nameof(StoppedCountText));
        OnPropertyChanged(nameof(ProtectedCountText));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionPath));
        OnPropertyChanged(nameof(SelectionHasPath));
        OnPropertyChanged(nameof(SafetyNote));
        OnPropertyChanged(nameof(ChangeModeNote));
    }

    /// <summary>
    /// Aplica o estado relatado pelo servico: quando a alteracao falha, a tela mostra
    /// o motivo real informado pela camada de servicos em vez de presumir sucesso.
    /// </summary>
    private async Task ReportAsync(ActionExecutionResult result, ServiceItemViewModel service, string successKey)
    {
        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync(
                    "Services.Error.Title",
                    string.IsNullOrEmpty(result.MessageKey) ? "Services.Error.Unavailable" : result.MessageKey,
                    result.Detail)
                .ConfigureAwait(true);

            return;
        }

        SetStatus(successKey, Severity.Ok);
        await LoadAsync().ConfigureAwait(true);

        SelectedService = Items.FirstOrDefault(item =>
            string.Equals(item.Name, service.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Modo de inicio oferecido na lista. Apenas modos seguros sao expostos.</summary>
    public sealed class StartModeOption
    {
        private readonly ILocalizer _localizer;

        public StartModeOption(ServiceStartMode mode, ILocalizer localizer)
        {
            Mode = mode;
            _localizer = localizer;
        }

        public ServiceStartMode Mode { get; }

        public string TitleKey => "Services.StartMode." + Mode;

        public string Text => _localizer[TitleKey];
    }
}
