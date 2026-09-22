using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>Etapas da primeira execucao.</summary>
public enum FirstRunStep
{
    Welcome = 0,
    Detection = 1,
    Diagnosis = 2,
    Result = 3
}

/// <summary>
/// Primeira execucao: apresenta o aplicativo, detecta o computador e executa um
/// diagnostico inicial. Tudo e somente leitura e nada e alterado no sistema; ao
/// final o usuario decide se abre a tela de diagnostico.
/// </summary>
public sealed partial class FirstRunViewModel : ViewModelBase
{
    private const int StepCount = 4;

    private readonly IDashboardService _dashboard;
    private readonly IDiagnosticService _diagnostics;
    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;

    private bool _isClosing;

    public FirstRunViewModel(
        IDashboardService dashboard,
        IDiagnosticService diagnostics,
        ISettingsService settings,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _dashboard = dashboard;
        _diagnostics = diagnostics;
        _settings = settings;
        _elevation = elevation;

        PropertyChanged += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanLeaveWelcome));
                OnPropertyChanged(nameof(CanAdvance));
            }
        };
    }

    /// <summary>Informa se, ao fechar, o aplicativo deve abrir a tela de diagnostico.</summary>
    public event EventHandler<bool>? Finished;

    public ObservableCollection<FactItemViewModel> Facts { get; } = new();

    [ObservableProperty] private FirstRunStep _step = FirstRunStep.Welcome;
    [ObservableProperty] private bool _isDetecting;
    [ObservableProperty] private bool _hasDetected;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private int _score;
    [ObservableProperty] private ScoreCategory _scoreCategory = ScoreCategory.Good;
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _verifiedCount;
    [ObservableProperty] private int _recommendationCount;
    [ObservableProperty] private int _startupCount;
    [ObservableProperty] private string _recoverableText = string.Empty;

    public bool IsWelcomeStep => Step is FirstRunStep.Welcome;

    public bool IsDetectionStep => Step is FirstRunStep.Detection;

    public bool IsDiagnosisStep => Step is FirstRunStep.Diagnosis;

    public bool IsResultStep => Step is FirstRunStep.Result;

    public bool CanLeaveWelcome => IsBusy is false;

    public bool CanAdvance => IsBusy is false && IsDetecting is false;

    public bool HasFacts => Facts.Count > 0;

    public string StepText => Localizer.Format("FirstRun.Step", (int)Step + 1, StepCount);

    public double StepPercent => ((int)Step + 1) * 100d / StepCount;

    public string ScoreText => Score + " / 100";

    public string ScoreCategoryText => Localizer["Score.Category." + ScoreCategory];

    public string ProblemCountText => ProblemCount.ToString("0");

    public string CriticalCountText => Localizer.Format("FirstRun.Result.Critical", CriticalCount);

    public string WarningCountText => Localizer.Format("FirstRun.Result.Warning", WarningCount);

    public string VerifiedCountText => Localizer.Format("FirstRun.Result.Verified", VerifiedCount);

    public string RecommendationCountText => RecommendationCount.ToString("0");

    public string StartupCountText => StartupCount.ToString("0");

    public string HealthDisclaimer => Localizer["Health.Disclaimer"];

    /// <summary>Primeiro passo: sai da apresentacao e inicia a deteccao real do computador.</summary>
    [RelayCommand]
    private async Task BeginAsync()
    {
        Step = FirstRunStep.Detection;
        await DetectAsync().ConfigureAwait(true);
    }

    /// <summary>Avanca conforme a etapa atual. A deteccao nunca e refeita sem necessidade.</summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step is FirstRunStep.Detection)
        {
            Step = FirstRunStep.Diagnosis;

            if (HasResult is false)
            {
                await DiagnoseAsync().ConfigureAwait(true);
            }

            return;
        }

        if (Step is FirstRunStep.Diagnosis)
        {
            Step = FirstRunStep.Result;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (IsBusy)
        {
            return;
        }

        Step = Step switch
        {
            FirstRunStep.Result => FirstRunStep.Diagnosis,
            FirstRunStep.Diagnosis => FirstRunStep.Detection,
            FirstRunStep.Detection => FirstRunStep.Welcome,
            _ => FirstRunStep.Welcome
        };
    }

    /// <summary>Fecha a apresentacao sem executar o diagnostico inicial.</summary>
    [RelayCommand]
    private Task SkipAsync() => CloseAsync(startDiagnosis: false);

    /// <summary>Abre a tela de diagnostico ao entrar no aplicativo.</summary>
    [RelayCommand]
    private Task StartDiagnosisAsync() => CloseAsync(startDiagnosis: true);

    /// <summary>Abre o aplicativo na tela inicial.</summary>
    [RelayCommand]
    private Task ExploreAsync() => CloseAsync(startDiagnosis: false);

    /// <summary>Encerra a janela pelo botao de fechar da barra de titulo.</summary>
    public async Task CloseAsync(bool startDiagnosis)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;

        try
        {
            if (_settings.Current.HasCompletedFirstRun is false)
            {
                _settings.Current.HasCompletedFirstRun = true;
                await _settings.SaveAsync(LifetimeToken).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning("FirstRun", "Nao foi possivel registrar a conclusao da primeira execucao.", exception);
        }

        Finished?.Invoke(this, startDiagnosis);
    }

    protected override void OnLanguageChanged()
    {
        foreach (var fact in Facts)
        {
            fact.Refresh();
        }
    }

    partial void OnStepChanged(FirstRunStep value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsDetectionStep));
        OnPropertyChanged(nameof(IsDiagnosisStep));
        OnPropertyChanged(nameof(IsResultStep));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(StepPercent));
    }

    partial void OnIsDetectingChanged(bool value) => OnPropertyChanged(nameof(CanAdvance));

    partial void OnScoreCategoryChanged(ScoreCategory value) => OnPropertyChanged(nameof(ScoreCategoryText));

    private async Task DetectAsync()
    {
        IsDetecting = true;
        var progress = new Progress<DiagnosticProgress>(item => SetProgress(item.PercentComplete, item.CurrentCheckTitleKey));

        var (ok, summary) = await RunAsync<DashboardSummary?>(
            async token =>
            {
                var built = await _dashboard.BuildAsync(includeRecommendations: true, progress, token)
                    .ConfigureAwait(true);

                ApplyDetection(built);
                return built;
            },
            "FirstRun.Detecting").ConfigureAwait(true);

        IsDetecting = false;

        if (ok is false || summary is null)
        {
            return;
        }

        HasDetected = true;
        SetStatus("FirstRun.Detected", Severity.Ok);
    }

    private async Task DiagnoseAsync()
    {
        var progress = new Progress<DiagnosticProgress>(item => SetProgress(item.PercentComplete, item.CurrentCheckTitleKey));

        var (ok, result) = await RunAsync<DiagnosisResult>(
            token => _diagnostics.RunAsync(new DiagnosticOptions(), null, progress, token),
            "Diagnose.Running").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        ProblemCount = result.ProblemCount;
        CriticalCount = result.CriticalCount;
        WarningCount = result.WarningCount;
        VerifiedCount = result.Checks.Count(check => check.Succeeded);
        HasResult = true;

        SetStatusFormat(
            ProblemCount > 0 ? "Diagnose.Status.Done" : "Diagnose.Status.Clean",
            ProblemCount > 0 ? Severity.Warning : Severity.Ok,
            ProblemCount);
    }

    private void ApplyDetection(DashboardSummary summary)
    {
        Facts.Clear();

        var snapshot = summary.System;

        if (snapshot is not null)
        {
            AddFact("FirstRun.Fact.Machine", Compose(snapshot.Motherboard.SystemManufacturer, snapshot.Motherboard.SystemModel, snapshot.Os.ComputerName));
            AddFact("FirstRun.Fact.Windows", snapshot.Os.FullVersionText + " · " + snapshot.Os.Architecture);
            AddFact("FirstRun.Fact.Cpu", ComposeCpu(snapshot));
            AddFact("FirstRun.Fact.Memory", ComposeMemory(snapshot));
            AddFact("FirstRun.Fact.Gpu", snapshot.PrimaryGpu?.Name);
            AddFact("FirstRun.Fact.Storage", ComposeStorage(snapshot));
            AddFact("FirstRun.Fact.SystemDrive", ComposeSystemVolume(snapshot));
        }

        AddFact("FirstRun.Fact.Startup", Localizer.Format("FirstRun.Fact.StartupValue", summary.StartupEnabledCount));
        AddFact("FirstRun.Fact.Recoverable", Humanize.Bytes(summary.TemporaryBytes + summary.RecycleBinBytes));
        AddFact("FirstRun.Fact.Privileges", Localizer[_elevation.IsElevated ? "FirstRun.Fact.Privileges.Admin" : "FirstRun.Fact.Privileges.Standard"]);

        Score = summary.Score.Total;
        ScoreCategory = summary.Score.Category;
        RecommendationCount = summary.RecommendationCount;
        StartupCount = summary.StartupEnabledCount;
        RecoverableText = Humanize.Bytes(summary.TemporaryBytes + summary.RecycleBinBytes);

        OnPropertyChanged(nameof(HasFacts));
    }

    private void AddFact(string labelKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Facts.Add(new FactItemViewModel(labelKey, value, Localizer));
    }

    private string ComposeCpu(SystemSnapshot snapshot)
    {
        var name = string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? Localizer["Common.Cpu.Unknown"] : snapshot.Cpu.Name;
        return name + " · " + Localizer.Format("FirstRun.Fact.CpuValue", snapshot.Cpu.PhysicalCores, snapshot.Cpu.LogicalProcessors);
    }

    private string ComposeMemory(SystemSnapshot snapshot)
    {
        var modules = snapshot.Memory.Modules.Count;
        return modules == 0
            ? Humanize.Bytes(snapshot.Memory.TotalBytes)
            : Localizer.Format("FirstRun.Fact.MemoryValue", Humanize.Bytes(snapshot.Memory.TotalBytes), modules);
    }

    private string ComposeStorage(SystemSnapshot snapshot)
    {
        if (snapshot.StorageDevices.Count == 0)
        {
            return string.Empty;
        }

        var parts = snapshot.StorageDevices
            .Take(3)
            .Select(device => device.Model
                + " (" + Humanize.MediaType(device.MediaType, Localizer)
                + " " + Humanize.BusType(device.BusType, Localizer)
                + ", " + Humanize.Bytes(device.SizeBytes) + ")");

        return string.Join(" · ", parts);
    }

    private string ComposeSystemVolume(SystemSnapshot snapshot)
    {
        var volume = snapshot.SystemVolume;

        if (volume is null || volume.IsReady is false)
        {
            return string.Empty;
        }

        return Localizer.Format(
            "FirstRun.Fact.SystemDriveValue",
            volume.DriveLetter,
            Humanize.Bytes(volume.FreeBytes),
            Humanize.Bytes(volume.TotalBytes),
            Humanize.Percent(volume.FreePercent));
    }

    private static string? Compose(string first, string second, string fallback)
    {
        var parts = new[] { first, second }.Where(item => string.IsNullOrWhiteSpace(item) is false).ToList();
        return parts.Count == 0 ? fallback : string.Join(" ", parts);
    }
}
