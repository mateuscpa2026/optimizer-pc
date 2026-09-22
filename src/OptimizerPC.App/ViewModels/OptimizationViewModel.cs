using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// Smart Optimization: monta um plano a partir das recomendacoes da analise local,
/// executa somente o que o usuario marcar e registra cada alteracao para reversao.
/// Nada e aplicado sem confirmacao.
/// </summary>
public sealed partial class OptimizationViewModel : ViewModelBase
{
    private readonly IOptimizationService _optimization;
    private readonly IDashboardService _dashboard;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IVisualEffectsService _visualEffects;
    private readonly IElevationService _elevation;

    public OptimizationViewModel(
        IOptimizationService optimization,
        IDashboardService dashboard,
        IDialogService dialogs,
        INavigationService navigation,
        ISettingsService settings,
        IVisualEffectsService visualEffects,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _optimization = optimization;
        _dashboard = dashboard;
        _dialogs = dialogs;
        _navigation = navigation;
        _settings = settings;
        _visualEffects = visualEffects;
        _elevation = elevation;
    }

    public ObservableCollection<OptimizationActionItemViewModel> Actions { get; } = new();

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = new();

    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _wasCancelled;
    [ObservableProperty] private bool _visualEffectsOptimized;
    [ObservableProperty] private bool _isVisualEffectsBusy;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _resultDetailText = string.Empty;
    [ObservableProperty] private string _restorePointNote = string.Empty;
    [ObservableProperty] private int _selectedCount;

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool HasActions => Actions.Count > 0;

    public bool IsElevated => _elevation.IsElevated;

    /// <summary>Alguma acao selecionada exige elevacao e a execucao nao esta elevada.</summary>
    public bool ShowElevationNote => IsElevated is false && Actions.Any(a => a.RequiresElevation);

    public string ElevationNote => Localizer["Optimize.Elevation.Note"];

    public string SelectionText => Localizer.Format("Optimize.Selection", SelectedCount, Actions.Count);

    public string EstimateText => Localizer.Format(
        "Optimize.Selection.Estimate",
        Humanize.Bytes(Actions.Where(a => a.IsSelected).Sum(a => a.Model.EstimatedBytes)));

    public bool HasSelection => SelectedCount > 0;

    public string RestorePointPolicy => _settings.Current.CreateRestorePointBeforeChanges
        ? Localizer["Optimize.RestorePoint.Enabled"]
        : Localizer["Optimize.RestorePoint.Disabled"];

    public string VisualEffectsText => VisualEffectsOptimized
        ? Localizer["Optimize.VisualEffects.Optimized"]
        : Localizer["Optimize.VisualEffects.Default"];

    public string VisualEffectsAction => VisualEffectsOptimized
        ? Localizer["Optimize.VisualEffects.RestoreAction"]
        : Localizer["Optimize.VisualEffects.ApplyAction"];

    public string ResultHeader => WasCancelled ? Localizer["Optimize.Result.Cancelled"] : Localizer["Optimize.Result.Done"];

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Actions.Where(action => action.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Optimize.Empty.Title", "Optimize.Empty.Message").ConfigureAwait(true);
            return;
        }

        var detail = Localizer.Format(
            "Optimize.Confirm.Detail",
            selected.Count,
            Humanize.Bytes(selected.Sum(a => a.Model.EstimatedBytes)));

        if (_settings.Current.ConfirmBeforeActions)
        {
            var confirmed = await _dialogs
                .ConfirmAsync("Optimize.Confirm.Title", "Optimize.Confirm.Message", detail, DialogKind.Question)
                .ConfigureAwait(true);

            if (confirmed is false)
            {
                return;
            }
        }

        var plan = new OptimizationPlan
        {
            Actions = selected.Select(action => action.Model).ToList(),
            Origin = "SmartOptimization",
            CreateRestorePoint = _settings.Current.CreateRestorePointBeforeChanges
        };

        var progress = new Progress<OptimizationProgress>(item =>
        {
            var percent = item.TotalActions <= 0 ? 0 : item.CompletedActions * 100d / item.TotalActions;
            SetProgress(percent, "Optimize.Running", item.CurrentActionTitle);
        });

        var (ok, result) = await RunAsync<OptimizationPlanResult>(
            token => _optimization.ExecuteAsync(plan, progress, token),
            "Optimize.Running.Preparing").ConfigureAwait(true);

        foreach (var action in Actions)
        {
            action.Refresh();
        }

        RefreshSelection();

        if (ok is false || result is null)
        {
            return;
        }

        ApplyResult(result);
        await ReloadVisualEffectsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var action in Actions)
        {
            action.IsSelected = true;
        }

        RefreshSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var action in Actions)
        {
            action.IsSelected = false;
        }

        RefreshSelection();
    }

    [RelayCommand]
    private async Task ToggleVisualEffectsAsync()
    {
        if (IsVisualEffectsBusy)
        {
            return;
        }

        var target = VisualEffectsOptimized is false;

        try
        {
            IsVisualEffectsBusy = true;

            var (ok, result) = await RunAsync<ActionExecutionResult>(
                token => _visualEffects.ApplyAsync(target, token),
                "Optimize.VisualEffects.Applying").ConfigureAwait(true);

            if (ok is false || result is null)
            {
                return;
            }

            if (result.Success is false)
            {
                await _dialogs
                    .ShowWarningAsync("Optimize.VisualEffects.Title", string.IsNullOrEmpty(result.MessageKey) ? "Optimize.VisualEffects.Unavailable" : result.MessageKey, result.Detail)
                    .ConfigureAwait(true);

                return;
            }

            SetStatus(result.Success ? "Optimize.VisualEffects.Done" : "Optimize.VisualEffects.Unavailable", Severity.Ok);
            await ReloadVisualEffectsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsVisualEffectsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenRestoreAsync() => _navigation.NavigateAsync(Screen.Restore);

    [RelayCommand]
    private Task OpenDiagnosisAsync() => _navigation.NavigateAsync(Screen.Diagnosis);

    protected override async Task OnInitializeAsync()
    {
        await LoadAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        // Estados podem ter mudado em outras telas (diagnostico, limpeza).
        if (HasPlan is false && HasResult is false)
        {
            await LoadAsync().ConfigureAwait(true);
            return;
        }

        await ReloadVisualEffectsAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        foreach (var action in Actions)
        {
            action.Refresh();
        }

        foreach (var recommendation in Recommendations)
        {
            recommendation.Refresh();
        }

        RefreshDerived();
    }

    private async Task LoadAsync()
    {
        var (ok, summary) = await RunAsync<DashboardSummary>(
            token => _dashboard.BuildAsync(includeRecommendations: true, progress: null, token),
            "Optimize.Building").ConfigureAwait(true);

        if (ok is false || summary is null)
        {
            return;
        }

        Recommendations.Clear();
        foreach (var recommendation in summary.Recommendations)
        {
            Recommendations.Add(new RecommendationItemViewModel(recommendation, Localizer));
        }

        var plan = await _optimization.BuildPlanAsync(summary.Recommendations, LifetimeToken).ConfigureAwait(true);

        Actions.Clear();
        foreach (var action in plan.Actions)
        {
            var item = new OptimizationActionItemViewModel(action, Localizer);
            item.PropertyChanged += OnActionPropertyChanged;
            Actions.Add(item);
        }

        HasPlan = Actions.Count > 0;
        HasResult = false;

        RefreshSelection();
        RefreshDerived();

        SetStatus(
            HasPlan ? "Optimize.Status.Ready" : "Optimize.Status.NothingToDo",
            HasPlan ? Severity.Ok : Severity.Info);

        await ReloadVisualEffectsAsync().ConfigureAwait(true);
    }

    private async Task ReloadVisualEffectsAsync()
    {
        try
        {
            VisualEffectsOptimized = await _visualEffects.IsOptimizedForPerformanceAsync(LifetimeToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Optimization", "Nao foi possivel ler o perfil de efeitos visuais.", exception);
        }
    }

    private void ApplyResult(OptimizationPlanResult result)
    {
        WasCancelled = result.WasCancelled;
        HasResult = true;

        ResultText = Localizer.Format(
            "Optimize.Result.Summary",
            result.SucceededCount,
            result.FailedCount,
            result.SkippedCount,
            Humanize.Bytes(result.TotalBytesFreed));

        ResultDetailText = Localizer.Format(
            "Optimize.Result.Duration",
            Humanize.Duration(result.Duration, Localizer["Common.Duration.SubSecond"]));

        RestorePointNote = string.IsNullOrWhiteSpace(result.RestorePointMessage)
            ? RestorePointPolicy
            : Localizer[result.RestorePointMessage!];

        SetStatus(WasCancelled ? "Optimize.Status.Cancelled" : "Optimize.Status.Done", WasCancelled ? Severity.Warning : Severity.Ok);
        RefreshDerived();
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(OptimizationActionItemViewModel.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        SelectedCount = Actions.Count(action => action.IsSelected);
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasActions));
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(RestorePointPolicy));
        OnPropertyChanged(nameof(VisualEffectsText));
        OnPropertyChanged(nameof(VisualEffectsAction));
        OnPropertyChanged(nameof(ResultHeader));
    }
}
