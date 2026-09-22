using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Modo PC Fraco: avalia o hardware real da maquina e oferece apenas ajustes
/// reversiveis (efeitos visuais, plano de energia, limpeza segura). Nao existe
/// overclock, ajuste de BIOS ou qualquer promessa de ganho garantido: a tela mostra
/// o que foi identificado e o que cada ajuste faz.
/// </summary>
public sealed partial class LowEndViewModel : ViewModelBase
{
    private readonly ILowEndModeService _lowEnd;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IElevationService _elevation;

    public LowEndViewModel(
        ILowEndModeService lowEnd,
        ISystemInfoService systemInfo,
        IDialogService dialogs,
        INavigationService navigation,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _lowEnd = lowEnd;
        _systemInfo = systemInfo;
        _dialogs = dialogs;
        _navigation = navigation;
        _elevation = elevation;
    }

    public ObservableCollection<FactItemViewModel> Reasons { get; } = new();

    public ObservableCollection<FactItemViewModel> Suggestions { get; } = new();

    public ObservableCollection<OptimizationActionItemViewModel> Actions { get; } = new();

    [ObservableProperty]
    private bool _isLowEnd;

    [ObservableProperty]
    private bool _hasAssessment;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _coresText = string.Empty;

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    private string _machineSummary = string.Empty;

    [ObservableProperty]
    private string _verdictText = string.Empty;

    [ObservableProperty]
    private string _verdictDetail = string.Empty;

    [ObservableProperty]
    private bool _hasReasons;

    [ObservableProperty]
    private bool _hasSuggestions;

    [ObservableProperty]
    private bool _hasActions;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    [ObservableProperty]
    private bool _wasCancelled;

    [ObservableProperty]
    private int _selectedCount;

    public bool IsElevated => _elevation.IsElevated;

    public bool ShowElevationNote => IsElevated is false && Actions.Any(action => action.RequiresElevation);

    public string ElevationNote => Localizer["LowEnd.Elevation.Note"];

    public string Disclaimer => Localizer["LowEnd.Disclaimer"];

    public string ActionsTitle => Localizer["LowEnd.Actions.Title"];

    public string ActionsNote => Localizer["LowEnd.Actions.Note"];

    public string ReasonsTitle => Localizer["LowEnd.Reasons.Title"];

    public string SuggestionsTitle => Localizer["LowEnd.Suggestions.Title"];

    public string MemoryLabel => Localizer["LowEnd.Memory.Label"];

    public string CoresLabel => Localizer["LowEnd.Cores.Label"];

    public string StorageLabel => Localizer["LowEnd.Storage.Label"];

    public string VerdictTitle => Localizer["LowEnd.Verdict.Title"];

    public string SelectionText => Localizer.Format("LowEnd.Selection", SelectedCount, Actions.Count);

    public bool HasSelection => SelectedCount > 0;

    protected override async Task OnNavigatedToAsync() => await AssessAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged()
    {
        RefreshDerived();
    }

    [RelayCommand]
    private Task Reassess() => AssessAsync();

    [RelayCommand]
    private Task OpenOptimization() => _navigation.NavigateAsync(Screen.Optimization);

    [RelayCommand]
    private Task OpenRestore() => _navigation.NavigateAsync(Screen.Restore);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var action in Actions)
        {
            action.IsSelected = true;
        }

        UpdateSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var action in Actions)
        {
            action.IsSelected = false;
        }

        UpdateSelection();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var selected = Actions.Where(action => action.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync("LowEnd.Actions.Title", "LowEnd.Actions.Empty").ConfigureAwait(true);
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "LowEnd.Apply.Title",
                "LowEnd.Apply.Confirm",
                "LowEnd.Apply.ConfirmAction",
                Localizer.Format("LowEnd.Apply.Detail", selected.Count))
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        HasResult = false;
        var progress = new Progress<OptimizationProgress>(item =>
        {
            var percent = item.TotalActions <= 0 ? 0 : item.CompletedActions * 100d / item.TotalActions;
            SetProgress(percent, "LowEnd.Apply.Running", item.CompletedActions, item.TotalActions);
        });

        var (ok, result) = await RunAsync<OptimizationPlanResult>(
            token => _lowEnd.ApplyAsync(selected.Select(action => action.Model).ToList(), progress, token),
            "LowEnd.Apply.Preparing").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        foreach (var action in Actions)
        {
            action.Refresh();
        }

        WasCancelled = result.WasCancelled;
        HasResult = true;
        ResultText = WasCancelled ? Localizer["LowEnd.Result.Cancelled"] : Localizer["LowEnd.Result.Done"];
        ResultDetail = Localizer.Format(
            "LowEnd.Result.Detail",
            result.SucceededCount,
            result.FailedCount,
            Humanize.Bytes(result.TotalBytesFreed));

        SetStatus(WasCancelled ? "LowEnd.Status.Cancelled" : "LowEnd.Status.Done", WasCancelled ? Severity.Warning : Severity.Ok);

        await AssessAsync(keepResult: true).ConfigureAwait(true);
    }

    private async Task AssessAsync(bool keepResult = false)
    {
        var (ok, snapshot) = await RunAsync<SystemSnapshot>(
            token => _systemInfo.GetSnapshotAsync(token),
            "LowEnd.Loading").ConfigureAwait(true);

        if (ok is false || snapshot is null)
        {
            return;
        }

        var scoreInput = new HealthScoreInput { Memory = snapshot.Memory };
        var assessment = _lowEnd.Assess(snapshot, scoreInput);

        ApplyAssessment(assessment, snapshot);

        // A lista de ajustes le o estado atual do sistema, entao roda fora da interface.
        var (actionsOk, actions) = await RunAsync<IReadOnlyList<OptimizationAction>>(
            token => Task.Run(() => _lowEnd.BuildActions(assessment), token),
            "LowEnd.Loading.Actions").ConfigureAwait(true);

        if (actionsOk is false || actions is null)
        {
            return;
        }

        Actions.Clear();
        foreach (var action in actions)
        {
            var item = new OptimizationActionItemViewModel(action, Localizer);
            item.PropertyChanged += OnActionChanged;
            Actions.Add(item);
        }

        HasActions = Actions.Count > 0;
        UpdateSelection();
        RefreshDerived();

        if (keepResult is false)
        {
            HasResult = false;
            ResultText = string.Empty;
            ResultDetail = string.Empty;
        }
    }

    private void ApplyAssessment(LowEndAssessment assessment, SystemSnapshot snapshot)
    {
        IsLowEnd = assessment.IsLowEnd;
        HasAssessment = true;

        MemoryText = assessment.TotalMemoryMb > 0
            ? Humanize.Bytes(assessment.TotalMemoryMb * 1024L * 1024L)
            : Localizer["Common.NotAvailable"];

        CoresText = assessment.LogicalCores > 0
            ? assessment.LogicalCores + " " + Localizer["LowEnd.Cores.Unit"]
            : Localizer["Common.NotAvailable"];

        StorageText = snapshot.StorageDevices.Count == 0
            ? Localizer["Common.NotIdentified"]
            : assessment.HasSolidStateDrive
                ? Localizer["LowEnd.Storage.Solid"]
                : Localizer["LowEnd.Storage.Mechanical"];

        MachineSummary = string.Join(
            " · ",
            new[]
            {
                snapshot.Os.FullVersionText,
                string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? Localizer["Common.Cpu.Unknown"] : snapshot.Cpu.Name,
                MemoryText + " RAM"
            }.Where(part => string.IsNullOrWhiteSpace(part) is false));

        Reasons.Clear();
        foreach (var key in assessment.ReasonKeys)
        {
            Reasons.Add(new FactItemViewModel(key, null, Localizer));
        }

        Suggestions.Clear();
        foreach (var key in assessment.SuggestedActionKeys)
        {
            Suggestions.Add(new FactItemViewModel(key, null, Localizer));
        }

        HasReasons = Reasons.Count > 0;
        HasSuggestions = Suggestions.Count > 0;

        VerdictText = IsLowEnd ? Localizer["LowEnd.Verdict.LowEnd"] : Localizer["LowEnd.Verdict.Capable"];
        VerdictDetail = IsLowEnd ? Localizer["LowEnd.Verdict.LowEnd.Detail"] : Localizer["LowEnd.Verdict.Capable.Detail"];

        RefreshDerived();
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(OptimizationActionItemViewModel.IsSelected))
        {
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        SelectedCount = Actions.Count(action => action.IsSelected);
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(ActionsTitle));
        OnPropertyChanged(nameof(ActionsNote));
        OnPropertyChanged(nameof(ReasonsTitle));
        OnPropertyChanged(nameof(SuggestionsTitle));
        OnPropertyChanged(nameof(MemoryLabel));
        OnPropertyChanged(nameof(CoresLabel));
        OnPropertyChanged(nameof(StorageLabel));
        OnPropertyChanged(nameof(VerdictTitle));
        OnPropertyChanged(nameof(SelectionText));
    }
}
