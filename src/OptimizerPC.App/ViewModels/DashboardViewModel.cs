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
/// Visao geral: indice de saude estimado, uso atual, espaco recuperavel e as
/// recomendacoes mais relevantes da ultima analise. Leitura apenas; a execucao
/// acontece nas telas de otimizacao e limpeza, sempre com confirmacao.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const int HistoryCapacity = 90;

    private readonly IDashboardService _dashboard;
    private readonly IOptimizationService _optimization;
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    public DashboardViewModel(
        IDashboardService dashboard,
        IOptimizationService optimization,
        IMonitoringService monitoring,
        ISettingsService settings,
        INavigationService navigation,
        IDialogService dialogs,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _dashboard = dashboard;
        _optimization = optimization;
        _monitoring = monitoring;
        _settings = settings;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public ObservableCollection<ScoreFactorItemViewModel> TopFactors { get; } = new();

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = new();

    public ObservableCollection<double> CpuHistory { get; } = new();

    public ObservableCollection<double> MemoryHistory { get; } = new();

    public ObservableCollection<double> DiskHistory { get; } = new();

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private string _scoreCategoryText = string.Empty;

    [ObservableProperty]
    private ScoreCategory _scoreCategory = ScoreCategory.Good;

    [ObservableProperty]
    private string _scoreLabel = string.Empty;

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private double _diskPercent;

    [ObservableProperty]
    private bool _isDiskActivityAvailable;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _freeSpaceText = string.Empty;

    [ObservableProperty]
    private double _freeSpacePercent;

    [ObservableProperty]
    private string _recoverableText = string.Empty;

    [ObservableProperty]
    private long _recoverableBytes;

    [ObservableProperty]
    private int _problemCount;

    [ObservableProperty]
    private int _recommendationCount;

    [ObservableProperty]
    private int _startupEnabledCount;

    [ObservableProperty]
    private string _temporaryText = string.Empty;

    [ObservableProperty]
    private string _recycleBinText = string.Empty;

    [ObservableProperty]
    private string _machineSummary = string.Empty;

    [ObservableProperty]
    private string _lastAnalysisText = string.Empty;

    [ObservableProperty]
    private bool _hasAnalysis;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _optimizeResultText = string.Empty;

    public string HealthDisclaimer => Localizer["Health.Disclaimer"];

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool HasProblems => ProblemCount > 0;

    public bool HasTopFactors => TopFactors.Count > 0;

    public string CpuText => Humanize.Percent(CpuPercent);

    public string MemoryPercentText => Humanize.Percent(MemoryPercent);

    public string DiskPercentText => IsDiskActivityAvailable ? Humanize.Percent(DiskPercent) : Localizer["Common.NotAvailable"];

    public string FreeSpacePercentText => Humanize.Percent(FreeSpacePercent);

    public string ProblemCountText => Localizer.Format("Dashboard.Problems.Count", ProblemCount);

    public string RecommendationCountText => Localizer.Format("Dashboard.Recommendations.Count", RecommendationCount);

    public string StartupCountText => Localizer.Format("Dashboard.Startup.Count", StartupEnabledCount);

    public string ScoreText => Score + " / 100";

    protected override async Task OnInitializeAsync()
    {
        _monitoring.SampleAvailable += OnSampleAvailable;

        _settings.SettingsChanged += OnSettingsChanged;

        await RefreshAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        IsMonitoring = _monitoring.IsRunning;
        await RefreshAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(HealthDisclaimer));
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(MemoryPercentText));
        OnPropertyChanged(nameof(DiskPercentText));
        OnPropertyChanged(nameof(FreeSpacePercentText));
        OnPropertyChanged(nameof(ProblemCountText));
        OnPropertyChanged(nameof(RecommendationCountText));
        OnPropertyChanged(nameof(StartupCountText));

        if (LastSummary is not null)
        {
            ApplySummary(LastSummary);
        }
    }

    protected override void OnDispose()
    {
        _monitoring.SampleAvailable -= OnSampleAvailable;
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    private DashboardSummary? LastSummary { get; set; }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private Task OpenDiagnosis() => _navigation.NavigateAsync(Screen.Diagnosis);

    [RelayCommand]
    private Task OpenCleaning() => _navigation.NavigateAsync(Screen.Cleaning);

    [RelayCommand]
    private Task OpenOptimization() => _navigation.NavigateAsync(Screen.Optimization);

    [RelayCommand]
    private Task OpenReports() => _navigation.NavigateAsync(Screen.Reports);

    /// <summary>
    /// Aplica somente as recomendacoes que dependem de uma acao segura e reversivel.
    /// O plano e sempre confirmado antes da execucao.
    /// </summary>
    [RelayCommand]
    private async Task OptimizeNowAsync()
    {
        var candidates = LastSummary?.Recommendations
            .Where(r => r.ActionId is not null && r.State == ActionState.Pending)
            .ToList() ?? new List<Recommendation>();

        if (candidates.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Dialog.Title.Info", "Dashboard.Optimize.Nothing").ConfigureAwait(true);
            return;
        }

        var estimated = candidates.Sum(r => r.EstimatedGainBytes);

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Dashboard.Optimize.Title",
                "Dashboard.Optimize.Message",
                "Dashboard.Optimize.Confirm",
                Localizer.Format("Dashboard.Optimize.Detail", candidates.Count, Humanize.Bytes(estimated)))
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        OptimizeResultText = string.Empty;

        OptimizationPlanResult? result = null;

        var completed = await RunAsync(
            async token =>
            {
                var plan = await _optimization.BuildPlanAsync(candidates, token).ConfigureAwait(true);
                result = await _optimization.ExecuteAsync(plan, null, token).ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
            },
            "Dashboard.Optimize.Running").ConfigureAwait(true);

        if (completed is false || result is null)
        {
            return;
        }

        OptimizeResultText = Localizer.Format(
            "Dashboard.Optimize.Result",
            result.SucceededCount,
            result.FailedCount,
            Humanize.Bytes(result.TotalBytesFreed));

        await _dialogs
            .ShowSuccessAsync(
                "Dashboard.Optimize.Title",
                "Dashboard.Optimize.Done",
                Localizer.Format("Dashboard.Optimize.Detail", result.SucceededCount, Humanize.Bytes(result.TotalBytesFreed)))
            .ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var (ok, summary) = await RunAsync<DashboardSummary?>(
            async token =>
            {
                var built = await _dashboard.BuildAsync(includeRecommendations: true, progress: null, token).ConfigureAwait(true);
                ApplySummary(built);
                return built;
            },
            "Dashboard.Loading").ConfigureAwait(true);

        if (ok is false || summary is null)
        {
            HasAnalysis = _settings.Current.LastDiagnosisUtc.HasValue;
            return;
        }

        HasAnalysis = true;
    }

    private void ApplySummary(DashboardSummary summary)
    {
        LastSummary = summary;

        Score = summary.Score.Total;
        ScoreCategory = summary.Score.Category;
        ScoreCategoryText = Localizer["Score.Category." + summary.Score.Category];
        ScoreLabel = Localizer["Dashboard.Score.Label"];

        TopFactors.Clear();
        foreach (var factor in summary.Score.NegativeFactors.Take(4))
        {
            TopFactors.Add(new ScoreFactorItemViewModel(factor, Localizer));
        }

        Recommendations.Clear();
        foreach (var recommendation in summary.Recommendations.Take(6))
        {
            Recommendations.Add(new RecommendationItemViewModel(recommendation, Localizer));
        }

        ProblemCount = summary.ProblemCount;
        RecommendationCount = summary.RecommendationCount;
        StartupEnabledCount = summary.StartupEnabledCount;
        RecoverableBytes = summary.TemporaryBytes + summary.RecycleBinBytes;
        RecoverableText = Humanize.Bytes(RecoverableBytes);
        TemporaryText = summary.TemporaryText;
        RecycleBinText = Humanize.Bytes(summary.RecycleBinBytes);
        ApplyFreeSpace(summary);

        MachineSummary = BuildMachineSummary(summary);
        LastAnalysisText = summary.LastDiagnosisUtc == default
            ? Localizer["Dashboard.LastAnalysis.None"]
            : Localizer.Format("Dashboard.LastAnalysis.At", Humanize.Date(summary.LastDiagnosisUtc.ToLocalTime()));

        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasTopFactors));
        OnPropertyChanged(nameof(ProblemCountText));
        OnPropertyChanged(nameof(RecommendationCountText));
        OnPropertyChanged(nameof(StartupCountText));
        OnPropertyChanged(nameof(ScoreText));
    }

    private void ApplyFreeSpace(DashboardSummary summary)
    {
        FreeSpaceText = summary.FreeSpaceText;
        FreeSpacePercent = summary.SystemDriveFreePercent;
        OnPropertyChanged(nameof(FreeSpacePercentText));
    }

    private string BuildMachineSummary(DashboardSummary summary)
    {
        var snapshot = summary.System;

        if (snapshot is null)
        {
            return string.Empty;
        }

        var parts = new List<string>
        {
            snapshot.Os.FullVersionText,
            string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? Localizer["Common.Cpu.Unknown"] : snapshot.Cpu.Name,
            Humanize.Bytes(snapshot.Memory.TotalBytes) + " RAM"
        };

        var gpu = snapshot.PrimaryGpu;
        if (gpu is not null && string.IsNullOrWhiteSpace(gpu.Name) is false)
        {
            parts.Add(gpu.Name);
        }

        return string.Join(" · ", parts);
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

        CpuPercent = Math.Round(sample.CpuPercent, 1);
        MemoryPercent = Math.Round(sample.MemoryPercent, 1);
        DiskPercent = sample.DiskActivityAvailable ? Math.Round(sample.DiskActivityPercent, 1) : 0;
        IsDiskActivityAvailable = sample.DiskActivityAvailable;
        MemoryText = Humanize.Bytes(sample.MemoryUsedBytes) + " / " + Humanize.Bytes(sample.MemoryTotalBytes);
        IsMonitoring = _monitoring.IsRunning;

        Append(CpuHistory, CpuPercent);
        Append(MemoryHistory, MemoryPercent);

        if (IsDiskActivityAvailable)
        {
            Append(DiskHistory, DiskPercent);
        }

        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(MemoryPercentText));
        OnPropertyChanged(nameof(DiskPercentText));
    }

    private static void Append(ObservableCollection<double> history, double value)
    {
        if (history.Count >= HistoryCapacity)
        {
            history.RemoveAt(0);
        }

        history.Add(value);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
        => _ = RefreshAsync();
}
