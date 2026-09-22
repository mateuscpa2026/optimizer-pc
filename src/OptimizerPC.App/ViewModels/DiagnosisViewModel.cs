using System.Collections.ObjectModel;
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
/// Diagnóstico do computador: verificações somente leitura de sistema, armazenamento,
/// inicialização, serviços e segurança. O resultado alimenta o índice de saúde e as
/// recomendações, mas nada é alterado aqui: cada sugestão leva à tela responsável e
/// continua dependendo de confirmação.
/// </summary>
public sealed partial class DiagnosisViewModel : ViewModelBase
{
    private readonly IDiagnosticService _diagnostics;
    private readonly IDashboardService _dashboard;
    private readonly IElevationService _elevation;
    private readonly INavigationService _navigation;
    private readonly ToolLaunchState _toolLaunch;
    private readonly List<DiagnosticCheckItemViewModel> _checks = new();

    private bool _autoRunRequested;

    public DiagnosisViewModel(
        IDiagnosticService diagnostics,
        IDashboardService dashboard,
        IElevationService elevation,
        INavigationService navigation,
        ToolLaunchState toolLaunch,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _diagnostics = diagnostics;
        _dashboard = dashboard;
        _elevation = elevation;
        _navigation = navigation;
        _toolLaunch = toolLaunch;
    }

    public ObservableCollection<DiagnosticCategoryGroupViewModel> Groups { get; } = new();

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = new();

    [ObservableProperty]
    private bool _includeDriveHealth = true;

    [ObservableProperty]
    private bool _includeStartupAnalysis = true;

    [ObservableProperty]
    private bool _includeServiceAnalysis = true;

    [ObservableProperty]
    private bool _includeSecurityChecks = true;

    [ObservableProperty]
    private bool _includeDeepStorageScan;

    [ObservableProperty]
    private bool _problemsOnly;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _hasScore;

    [ObservableProperty]
    private bool _wasCancelled;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private ScoreCategory _scoreCategory = ScoreCategory.Good;

    [ObservableProperty]
    private int _problemCount;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _verifiedCount;

    [ObservableProperty]
    private int _unverifiedCount;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private DateTime? _completedAt;

    public bool HasGroups => Groups.Count > 0;

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool IsFilteredEmpty => HasResult && Groups.Count == 0;

    public bool ShowElevationNote => IsElevated is false && HasResult;

    public string ElevationNote => Localizer["Diagnose.Elevation.Note"];

    public string HealthDisclaimer => Localizer["Health.Disclaimer"];

    public string ScoreText => Score + " / 100";

    public string ScoreCategoryText => Localizer["Score.Category." + ScoreCategory];

    public string CriticalCountText => Localizer.Format("Diagnose.Summary.Critical", CriticalCount);

    public string WarningCountText => Localizer.Format("Diagnose.Summary.Warning", WarningCount);

    public string VerifiedCountText => Localizer.Format("Diagnose.Summary.Verified", VerifiedCount);

    public string UnverifiedCountText => Localizer.Format("Diagnose.Summary.Unverified", UnverifiedCount);

    public string DurationText => Duration == default
        ? string.Empty
        : Localizer.Format("Diagnose.Summary.Duration", Humanize.Duration(Duration, Localizer["Common.Duration.SubSecond"]));

    public string CompletedText => CompletedAt is null
        ? string.Empty
        : Localizer.Format("Diagnose.Summary.CompletedAt", Humanize.Date(CompletedAt.Value));

    /// <summary>
    /// Solicitado pela primeira execução para iniciar a análise assim que a tela abrir.
    /// </summary>
    public void RequestAutoRun() => _autoRunRequested = true;

    protected override async Task OnInitializeAsync()
    {
        IsElevated = _elevation.IsElevated;

        try
        {
            var last = await _diagnostics.GetLastResultAsync(LifetimeToken).ConfigureAwait(true);

            if (last is not null)
            {
                ApplyResult(last, null);
                SetStatus("Diagnose.Status.Restored", Severity.Info);
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento.
        }
        catch (Exception exception)
        {
            Logger.Warning("Diagnosis", "Nao foi possivel ler o ultimo diagnostico da sessao.", exception);
        }
    }

    protected override async Task OnNavigatedToAsync()
    {
        IsElevated = _elevation.IsElevated;

        if (_autoRunRequested is false)
        {
            return;
        }

        _autoRunRequested = false;
        await RunDiagnosisAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        IsElevated = _elevation.IsElevated;

        foreach (var check in _checks)
        {
            check.Refresh();
        }

        foreach (var group in Groups)
        {
            group.Refresh();

            foreach (var item in group.Items)
            {
                item.Refresh();
            }
        }

        foreach (var recommendation in Recommendations)
        {
            recommendation.Refresh();
        }
    }

    [RelayCommand]
    private Task OpenCleaning() => _navigation.NavigateAsync(Screen.Cleaning);

    [RelayCommand]
    private Task OpenOptimization() => _navigation.NavigateAsync(Screen.Optimization);

    /// <summary>
    /// Encaminha a ferramenta sugerida por uma verificação para a tela de ferramentas,
    /// que executa o comando somente após confirmação.
    /// </summary>
    [RelayCommand]
    private Task OpenToolAsync(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return Task.CompletedTask;
        }

        _toolLaunch.Request(toolId);
        return _navigation.NavigateAsync(Screen.Tools);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        ProblemsOnly = false;
    }

    [RelayCommand]
    private async Task RunDiagnosisAsync()
    {
        var options = new DiagnosticOptions
        {
            IncludeDriveHealth = IncludeDriveHealth,
            IncludeStartupAnalysis = IncludeStartupAnalysis,
            IncludeServiceAnalysis = IncludeServiceAnalysis,
            IncludeSecurityChecks = IncludeSecurityChecks,
            IncludeDeepStorageScan = IncludeDeepStorageScan
        };

        var progress = new Progress<DiagnosticProgress>(ApplyProgress);

        DiagnosisResult? result = null;
        DashboardSummary? summary = null;
        var cancelled = false;

        var completed = await RunAsync(
            async token =>
            {
                result = await _diagnostics.RunAsync(options, null, progress, token).ConfigureAwait(true);

                // O resumo do dashboard reaproveita a mesma coleta e devolve o indice
                // de saude e as recomendacoes referentes ao estado observado agora.
                summary = await _dashboard.BuildAsync(includeRecommendations: true, progress: null, token)
                    .ConfigureAwait(true);
            },
            "Diagnose.Running").ConfigureAwait(true);

        if (completed is false)
        {
            cancelled = result is null;

            if (cancelled)
            {
                SetStatus("Diagnose.Status.Cancelled", Severity.Info);
            }

            return;
        }

        ApplyResult(result!, summary);

        SetStatusFormat(
            ProblemCount > 0 ? "Diagnose.Status.Done" : "Diagnose.Status.Clean",
            ProblemCount > 0 ? Severity.Warning : Severity.Ok,
            ProblemCount);
    }

    partial void OnProblemsOnlyChanged(bool value) => ApplyFilter();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnScoreCategoryChanged(ScoreCategory value) => OnPropertyChanged(nameof(ScoreCategoryText));

    partial void OnIsElevatedChanged(bool value) => OnPropertyChanged(nameof(ShowElevationNote));

    partial void OnHasResultChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void ApplyProgress(DiagnosticProgress progress)
        => SetProgress(progress.PercentComplete, progress.CurrentCheckTitleKey);

    private void ApplyResult(DiagnosisResult result, DashboardSummary? summary)
    {
        _checks.Clear();

        foreach (var check in result.Checks)
        {
            _checks.Add(new DiagnosticCheckItemViewModel(check, Localizer));
        }

        if (summary is not null)
        {
            Score = summary.Score.Total;
            ScoreCategory = summary.Score.Category;
            HasScore = true;

            Recommendations.Clear();

            foreach (var recommendation in summary.Recommendations)
            {
                Recommendations.Add(new RecommendationItemViewModel(recommendation, Localizer));
            }
        }

        ProblemCount = result.ProblemCount;
        CriticalCount = result.CriticalCount;
        WarningCount = result.WarningCount;
        VerifiedCount = result.Checks.Count(check => check.Succeeded);
        UnverifiedCount = result.Checks.Count - VerifiedCount;
        Duration = result.Duration;
        CompletedAt = result.CompletedAtUtc.ToLocalTime();
        WasCancelled = result.WasCancelled;
        HasResult = true;

        ApplyFilter();

        OnPropertyChanged(nameof(HasRecommendations));
    }

    private void ApplyFilter()
    {
        var query = _checks.AsEnumerable();

        if (ProblemsOnly)
        {
            query = query.Where(item => item.HasProblem);
        }

        var text = FilterText.Trim();

        if (text.Length > 0)
        {
            query = query.Where(item => Matches(item, text));
        }

        var filtered = query.ToList();

        Groups.Clear();

        foreach (var category in DistinctCategories(filtered))
        {
            Groups.Add(new DiagnosticCategoryGroupViewModel(
                category,
                filtered.Where(item => string.Equals(item.Model.CategoryKey, category, StringComparison.Ordinal)).ToList(),
                Localizer));
        }

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsFilteredEmpty));
    }

    /// <summary>
    /// Mantem a ordem em que o servico emite as verificacoes: as categorias aparecem
    /// como foram avaliadas, sem uma ordem fixa escrita na interface.
    /// </summary>
    private static IEnumerable<string> DistinctCategories(List<DiagnosticCheckItemViewModel> items)
    {
        var seen = new List<string>();

        foreach (var item in items)
        {
            var category = item.Model.CategoryKey;

            if (seen.Contains(category, StringComparer.Ordinal))
            {
                continue;
            }

            seen.Add(category);
            yield return category;
        }
    }

    private static bool Matches(DiagnosticCheckItemViewModel item, string text)
        => Contains(item.Title, text)
            || Contains(item.CategoryText, text)
            || Contains(item.Description, text)
            || Contains(item.Detail, text);

    private static bool Contains(string? value, string text)
        => value is not null && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);
}
