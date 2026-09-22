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
/// Programas que iniciam com o Windows. A tela somente le os itens registrados e
/// permite desativar ou reativar entradas conhecidas: a alteracao e reversivel e
/// fica registrada para restauracao. Nenhum item e removido do disco.
/// </summary>
public sealed partial class StartupViewModel : ViewModelBase
{
    private readonly IStartupService _startup;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IElevationService _elevation;

    private readonly List<StartupItemViewModel> _all = new();

    public StartupViewModel(
        IStartupService startup,
        IDialogService dialogs,
        INavigationService navigation,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _startup = startup;
        _dialogs = dialogs;
        _navigation = navigation;
        _elevation = elevation;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _enabledOnly;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _disabledCount;
    [ObservableProperty] private int _highImpactCount;
    [ObservableProperty] private bool _isElevated;

    public bool HasItems => Items.Count > 0;

    public bool IsFilteredEmpty => HasEntries && Items.Count == 0;

    public string EnabledCountText => Localizer.Format("Startup.Summary.Enabled", EnabledCount);

    public string DisabledCountText => Localizer.Format("Startup.Summary.Disabled", DisabledCount);

    public string HighImpactCountText => Localizer.Format("Startup.Summary.HighImpact", HighImpactCount);

    public string ElevationNote => Localizer["Startup.Elevation.Note"];

    public bool ShowElevationNote => IsElevated is false && Items.Any(item => item.RequiresElevation);

    public string SafetyNote => Localizer["Startup.Safety.Note"];

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        EnabledOnly = false;
    }

    [RelayCommand]
    private Task OpenRestoreAsync() => _navigation.NavigateAsync(Screen.Restore);

    [RelayCommand]
    private async Task ToggleAsync(StartupItemViewModel? item)
    {
        if (item is null || IsBusy || item.SupportsToggle is false)
        {
            return;
        }

        var enable = item.IsEnabled is false;
        var titleKey = enable ? "Startup.Enable.Title" : "Startup.Disable.Title";
        var messageKey = enable ? "Startup.Enable.Message" : "Startup.Disable.Message";

        var confirmed = await _dialogs
            .ConfirmAsync(titleKey, messageKey, item.Name, DialogKind.Question)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        StartupItemViewModel? target = item;

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _startup.SetEnabledAsync(target.Model, enable, token),
            enable ? "Startup.Enable.Working" : "Startup.Disable.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync("Startup.Error.Title", string.IsNullOrEmpty(result.MessageKey) ? "Startup.Error.Unavailable" : result.MessageKey, result.Detail)
                .ConfigureAwait(true);

            return;
        }

        target.IsEnabled = enable;
        SetStatus(enable ? "Startup.Status.Enabled" : "Startup.Status.Disabled", Severity.Ok);
        ApplyFilter();
        RefreshSummary();
    }

    protected override async Task OnInitializeAsync()
    {
        await LoadAsync().ConfigureAwait(true);
    }

    protected override Task OnNavigatedToAsync()
    {
        IsElevated = _elevation.IsElevated;
        return Task.CompletedTask;
    }

    protected override void OnLanguageChanged()
    {
        foreach (var item in _all)
        {
            item.Refresh();
        }

        RefreshDerived();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnEnabledOnlyChanged(bool value) => ApplyFilter();

    private async Task LoadAsync()
    {
        var (ok, entries) = await RunAsync<IReadOnlyList<StartupEntry>>(
            token => _startup.GetEntriesAsync(token),
            "Startup.Loading").ConfigureAwait(true);

        if (ok is false || entries is null)
        {
            return;
        }

        IsElevated = _elevation.IsElevated;

        _all.Clear();
        foreach (var entry in entries)
        {
            _all.Add(new StartupItemViewModel(entry, Localizer));
        }

        HasEntries = _all.Count > 0;
        ApplyFilter();
        RefreshSummary();

        SetStatus(
            HasEntries ? "Startup.Status.Ready" : "Startup.Status.Empty",
            HasEntries ? Severity.Ok : Severity.Info);
    }

    private void ApplyFilter()
    {
        var query = FilterText?.Trim() ?? string.Empty;

        Items.Clear();
        foreach (var item in _all)
        {
            if (EnabledOnly && item.IsEnabled is false)
            {
                continue;
            }

            if (query.Length > 0 &&
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) is false &&
                item.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            Items.Add(item);
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void RefreshSummary()
    {
        EnabledCount = _all.Count(item => item.IsEnabled);
        DisabledCount = _all.Count - EnabledCount;
        HighImpactCount = _all.Count(item => item.IsEnabled && item.Impact is StartupImpact.High);

        OnPropertyChanged(nameof(EnabledCountText));
        OnPropertyChanged(nameof(DisabledCountText));
        OnPropertyChanged(nameof(HighImpactCountText));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(EnabledCountText));
        OnPropertyChanged(nameof(DisabledCountText));
        OnPropertyChanged(nameof(HighImpactCountText));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(SafetyNote));
    }
}
