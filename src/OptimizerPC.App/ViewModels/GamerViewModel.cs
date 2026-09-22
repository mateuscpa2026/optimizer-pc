using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Modo Gamer. A sessao aplica apenas ajustes reversiveis e temporarios: modo de jogo do
/// Windows, plano de energia, efeitos visuais, prioridade do jogo, pausa de dois servicos
/// de indexacao e pausa das notificacoes do aplicativo. Nao existe overclock, ajuste de
/// BIOS ou de tensao: todos os ajustes podem ser desfeitos ao encerrar a sessao.
/// </summary>
public sealed partial class GamerViewModel : ViewModelBase
{
    private readonly IGameDetector _detector;
    private readonly IGameBoostService _boost;
    private readonly IProcessService _processes;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IElevationService _elevation;

    public GamerViewModel(
        IGameDetector detector,
        IGameBoostService boost,
        IProcessService processes,
        IDialogService dialogs,
        INavigationService navigation,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _detector = detector;
        _boost = boost;
        _processes = processes;
        _dialogs = dialogs;
        _navigation = navigation;
        _elevation = elevation;

        // IsBusy e uma propriedade manual da base: os botoes dependem dela.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                RefreshDerived();
            }
        };
    }

    public ObservableCollection<GameItemViewModel> Games { get; } = new();

    public ObservableCollection<ProcessItemViewModel> RunningGames { get; } = new();

    public ObservableCollection<FactItemViewModel> SessionResults { get; } = new();

    [ObservableProperty]
    private GameItemViewModel? _selectedGame;

    [ObservableProperty]
    private ProcessItemViewModel? _selectedProcess;

    [ObservableProperty]
    private bool _enableGameMode = true;

    [ObservableProperty]
    private bool _setHighPerformancePowerPlan = true;

    [ObservableProperty]
    private bool _pauseNotifications = true;

    [ObservableProperty]
    private bool _disableVisualEffects;

    [ObservableProperty]
    private bool _reduceBackgroundPriority;

    [ObservableProperty]
    private bool _stopTemporaryServices;

    [ObservableProperty]
    private bool _hasGames;

    [ObservableProperty]
    private bool _hasRunningGames;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    [ObservableProperty]
    private int _succeededCount;

    [ObservableProperty]
    private string _activeGameName = string.Empty;

    [ObservableProperty]
    private string _startedAtText = string.Empty;

    public bool IsElevated => _elevation.IsElevated;

    public bool ShowElevationNote => IsElevated is false && StopTemporaryServices;

    public string ElevationNote => Localizer["Gamer.Elevation.Note"];

    public string Disclaimer => Localizer["Gamer.Disclaimer"];

    public string GamesTitle => Localizer["Gamer.Games.Title"];

    public string GamesHint => Localizer["Gamer.Games.Hint"];

    public string GamesEmptyText => Localizer["Gamer.Games.Empty"];

    public string RunningTitle => Localizer["Gamer.Running.Title"];

    public string RunningHint => Localizer["Gamer.Running.Hint"];

    public string RunningEmptyText => Localizer["Gamer.Running.Empty"];

    public string OptionsTitle => Localizer["Gamer.Options.Title"];

    public string OptionsNote => Localizer["Gamer.Options.Note"];

    public string ResultsTitle => Localizer["Gamer.Results.Title"];

    public string ResultsEmptyText => Localizer["Gamer.Results.Empty"];

    public string SessionStateText => IsRunning ? Localizer["Gamer.Status.Active"] : Localizer["Gamer.Status.Idle"];

    public string SessionSummary => Localizer.Format("Gamer.Session.Summary", SucceededCount);

    public bool HasSession => IsRunning;

    public bool CanStart => IsRunning is false && IsBusy is false;

    public bool CanStop => IsRunning && IsBusy is false;

    protected override async Task OnNavigatedToAsync()
    {
        SyncSession();
        await RefreshAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        RefreshDerived();
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private Task OpenRestore() => _navigation.NavigateAsync(Screen.Restore);

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning)
        {
            await _dialogs.ShowInfoAsync("Gamer.Title", "Gamer.Error.AlreadyRunning").ConfigureAwait(true);
            return;
        }

        var session = new GameBoostSession
        {
            GameName = SelectedGame?.Name ?? SelectedProcess?.Description,
            ProcessId = SelectedProcess?.Id,
            EnableGameMode = EnableGameMode,
            SetHighPerformancePowerPlan = SetHighPerformancePowerPlan,
            PauseNotifications = PauseNotifications,
            DisableVisualEffects = DisableVisualEffects,
            ReduceBackgroundProcessPriority = ReduceBackgroundPriority,
            StopTemporaryServices = StopTemporaryServices
        };

        HasResult = false;
        SessionResults.Clear();

        var progress = new Progress<OptimizationProgress>(item =>
        {
            var percent = item.TotalActions <= 0 ? 0 : item.CompletedActions * 100d / item.TotalActions;
            SetProgress(percent, "Gamer.Start.Running", item.CompletedActions, item.TotalActions);
        });

        var (ok, result) = await RunAsync<GameBoostResult>(
            token => _boost.StartAsync(session, progress, token),
            "Gamer.Start.Preparing").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        ApplyResults(result);
        IsRunning = result.Success;
        ActiveGameName = session.GameName ?? Localizer["Gamer.NoGame"];
        StartedAtText = Localizer.Format("Gamer.Status.StartedAt", DateTime.Now.ToString("t"));

        SetStatus(IsRunning ? "Gamer.Status.Started" : "Gamer.Status.Failed", IsRunning ? Severity.Ok : Severity.Warning);
        RefreshDerived();
    }

    [RelayCommand]
    private Task StopRestore() => StopAsync(restoreEverything: true);

    [RelayCommand]
    private Task StopKeep() => StopAsync(restoreEverything: false);

    private async Task StopAsync(bool restoreEverything)
    {
        if (IsRunning is false)
        {
            await _dialogs.ShowInfoAsync("Gamer.Title", "Gamer.Error.NotRunning").ConfigureAwait(true);
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                restoreEverything ? "Gamer.Stop.Restore.Title" : "Gamer.Stop.Keep.Title",
                restoreEverything ? "Gamer.Stop.Restore.Confirm" : "Gamer.Stop.Keep.Confirm",
                restoreEverything ? "Gamer.Stop.Restore.ConfirmAction" : "Gamer.Stop.Keep.ConfirmAction")
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        SessionResults.Clear();

        var (ok, result) = await RunAsync<GameBoostResult>(
            token => _boost.StopAsync(restoreEverything, token),
            "Gamer.Stop.Running").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        ApplyResults(result);
        IsRunning = false;
        HasResult = true;
        ResultText = Localizer[restoreEverything ? "Gamer.Result.Restored" : "Gamer.Result.StoppedKept"];
        ResultDetail = string.Empty;
        ActiveGameName = string.Empty;
        StartedAtText = string.Empty;

        SetStatus("Gamer.Status.Stopped", Severity.Ok);
        RefreshDerived();
    }

    private async Task RefreshAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var (gamesOk, games) = await RunAsync<IReadOnlyList<InstalledGame>>(
            token => _detector.DetectAsync(token),
            "Gamer.Loading.Games").ConfigureAwait(true);

        if (gamesOk && games is not null)
        {
            Games.Clear();
            foreach (var game in games)
            {
                Games.Add(new GameItemViewModel(game, Localizer));
            }

            HasGames = Games.Count > 0;
        }

        var (runningOk, running) = await RunAsync<IReadOnlyList<ProcessInfoModel>?>(
            async token =>
            {
                var all = await _processes.GetProcessesAsync(token).ConfigureAwait(false);
                return _detector.FindRunningGames(all);
            },
            "Gamer.Loading.Running").ConfigureAwait(true);

        if (runningOk && running is not null)
        {
            RunningGames.Clear();
            foreach (var process in running)
            {
                RunningGames.Add(new ProcessItemViewModel(process, Localizer));
            }

            HasRunningGames = RunningGames.Count > 0;
        }

        RefreshDerived();
    }

    private void SyncSession()
    {
        var current = _boost.CurrentSession;

        IsRunning = current is not null;

        if (current is not null)
        {
            HasResult = false;
            SessionResults.Clear();
            ActiveGameName = current.GameName ?? Localizer["Gamer.NoGame"];

            EnableGameMode = current.EnableGameMode;
            SetHighPerformancePowerPlan = current.SetHighPerformancePowerPlan;
            PauseNotifications = current.PauseNotifications;
            DisableVisualEffects = current.DisableVisualEffects;
            ReduceBackgroundPriority = current.ReduceBackgroundProcessPriority;
            StopTemporaryServices = current.StopTemporaryServices;
        }

        RefreshDerived();
    }

    private void ApplyResults(GameBoostResult result)
    {
        SucceededCount = result.Results.Count(item => item.Success);

        foreach (var item in result.Results)
        {
            var value = Localizer[item.MessageKey];

            if (string.IsNullOrWhiteSpace(item.Detail) is false)
            {
                value = value + " · " + item.Detail;
            }

            SessionResults.Add(new FactItemViewModel(item.TitleKey, value, Localizer));
        }

        HasResult = true;
        ResultText = Localizer[result.MessageKey];
        ResultDetail = Localizer.Format("Gamer.Result.Summary", SucceededCount, result.Results.Count);
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(GamesTitle));
        OnPropertyChanged(nameof(GamesHint));
        OnPropertyChanged(nameof(GamesEmptyText));
        OnPropertyChanged(nameof(RunningTitle));
        OnPropertyChanged(nameof(RunningHint));
        OnPropertyChanged(nameof(RunningEmptyText));
        OnPropertyChanged(nameof(OptionsTitle));
        OnPropertyChanged(nameof(OptionsNote));
        OnPropertyChanged(nameof(ResultsTitle));
        OnPropertyChanged(nameof(ResultsEmptyText));
        OnPropertyChanged(nameof(SessionStateText));
        OnPropertyChanged(nameof(SessionSummary));
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnIsRunningChanged(bool value) => RefreshDerived();

    partial void OnSucceededCountChanged(int value) => OnPropertyChanged(nameof(SessionSummary));

    partial void OnStopTemporaryServicesChanged(bool value) => OnPropertyChanged(nameof(ShowElevationNote));
}
