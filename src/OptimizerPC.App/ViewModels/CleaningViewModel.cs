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
/// Limpeza de arquivos temporarios: a varredura apenas mede os locais conhecidos e
/// nada e excluido sem selecao e confirmacao do usuario. Arquivos pessoais nunca
/// entram na lista: o catalogo cobre somente pastas temporarias do Windows e do
/// usuario.
/// </summary>
public sealed partial class CleaningViewModel : ViewModelBase
{
    private readonly ICleanupScanner _scanner;
    private readonly ICleanupService _cleanup;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;

    public CleaningViewModel(
        ICleanupScanner scanner,
        ICleanupService cleanup,
        IDialogService dialogs,
        INavigationService navigation,
        ISettingsService settings,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _scanner = scanner;
        _cleanup = cleanup;
        _dialogs = dialogs;
        _navigation = navigation;
        _settings = settings;
        _elevation = elevation;
    }

    public ObservableCollection<CleanupTargetItemViewModel> Targets { get; } = new();

    public ObservableCollection<CleanupResultItemViewModel> Results { get; } = new();

    [ObservableProperty] private bool _hasScan;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _wasCancelled;
    [ObservableProperty] private bool _isRecycleBinBusy;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _recycleBinText = string.Empty;
    [ObservableProperty] private bool _hasRecycleBinInfo;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _summaryDetailText = string.Empty;

    public bool IsElevated => _elevation.IsElevated;

    public bool HasTargets => Targets.Count > 0;

    public bool HasSelection => SelectedCount > 0;

    public bool ShowElevationNote => IsElevated is false && Targets.Any(target => target.IsSelected && target.RequiresElevation);

    public string ElevationNote => Localizer["Cleanup.Elevation.Note"];

    public string SelectionText => Localizer.Format("Cleanup.Selection", SelectedCount, Targets.Count);

    public string EstimateText => Localizer.Format(
        "Cleanup.Selection.Estimate",
        Humanize.Bytes(Targets.Where(target => target.IsSelected).Sum(target => target.TotalBytes)));

    public string RecycleBinTitle => Localizer["Cleanup.RecycleBin.Title"];

    public string RecycleBinSubtitle => Localizer["Cleanup.RecycleBin.Subtitle"];

    public string EmptyRecycleBinAction => Localizer["Cleanup.RecycleBin.Action"];

    public string SafetyNote => Localizer["Cleanup.Safety.Note"];

    public string RestoreNote => Localizer["Cleanup.Safety.Restore"];

    [RelayCommand]
    private Task ScanAsync() => RunScanAsync();

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Targets.Where(target => target.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Cleanup.NothingSelected.Title", "Cleanup.NothingSelected.Message").ConfigureAwait(true);
            return;
        }

        var bytes = selected.Sum(target => target.TotalBytes);

        if (_settings.Current.ConfirmBeforeActions || selected.Any(target => target.RequiresConfirmation))
        {
            var detail = Localizer.Format("Cleanup.Confirm.Detail", selected.Count, Humanize.Bytes(bytes));
            var confirmed = await _dialogs
                .ConfirmAsync("Cleanup.Confirm.Title", "Cleanup.Confirm.Message", detail, DialogKind.Warning)
                .ConfigureAwait(true);

            if (confirmed is false)
            {
                return;
            }
        }

        var progress = new Progress<CleanupProgress>(item => SetProgress(
            item.PercentComplete,
            "Cleanup.Cleaning",
            item.CompletedTargets,
            item.TotalTargets));

        var (ok, summary) = await RunAsync<CleanupSummary>(
            token => _cleanup.CleanAsync(selected.Select(target => target.Model).ToList(), progress, token),
            "Cleanup.Cleaning.Preparing").ConfigureAwait(true);

        if (ok is false || summary is null)
        {
            return;
        }

        ApplySummary(summary);
        await LoadRecycleBinAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var target in Targets)
        {
            target.IsSelected = true;
        }

        RefreshSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var target in Targets)
        {
            target.IsSelected = false;
        }

        RefreshSelection();
    }

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        if (IsRecycleBinBusy)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmAsync("Cleanup.RecycleBin.Confirm.Title", "Cleanup.RecycleBin.Confirm.Message", RecycleBinText, DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        try
        {
            IsRecycleBinBusy = true;

            var (ok, result) = await RunAsync<ActionExecutionResult>(
                token => _cleanup.EmptyRecycleBinAsync(token),
                "Cleanup.RecycleBin.Working").ConfigureAwait(true);

            if (ok is false || result is null)
            {
                return;
            }

            if (result.Success is false)
            {
                await _dialogs
                    .ShowWarningAsync("Cleanup.RecycleBin.Title", string.IsNullOrEmpty(result.MessageKey) ? "Cleanup.Error.RecycleBinFailed" : result.MessageKey, result.Detail)
                    .ConfigureAwait(true);

                return;
            }

            SetStatus(string.IsNullOrEmpty(result.MessageKey) ? "Cleanup.Result.RecycleBinEmptied" : result.MessageKey, Severity.Ok);
            await LoadRecycleBinAsync().ConfigureAwait(true);
        }
        finally
        {
            IsRecycleBinBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenStorageAsync() => _navigation.NavigateAsync(Screen.Storage);

    [RelayCommand]
    private Task OpenHistoryAsync() => _navigation.NavigateAsync(Screen.History);

    protected override async Task OnInitializeAsync()
    {
        await LoadRecycleBinAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        if (HasScan is false && HasResults is false)
        {
            await RunScanAsync().ConfigureAwait(true);
            return;
        }

        await LoadRecycleBinAsync().ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        foreach (var target in Targets)
        {
            target.Refresh();
        }

        foreach (var result in Results)
        {
            result.Refresh();
        }

        RefreshDerived();
    }

    private async Task RunScanAsync()
    {
        var progress = new Progress<CleanupProgress>(item => SetProgress(
            item.PercentComplete,
            "Cleanup.Scanning",
            item.CompletedTargets,
            item.TotalTargets));

        var (ok, scanned) = await RunAsync<IReadOnlyList<CleanupTarget>>(
            token => _scanner.ScanAsync(categories: null, includeRecycleBin: false, progress: progress, cancellationToken: token),
            "Cleanup.Scanning.Preparing").ConfigureAwait(true);

        if (ok is false || scanned is null)
        {
            return;
        }

        Targets.Clear();
        foreach (var target in scanned)
        {
            var item = new CleanupTargetItemViewModel(target, Localizer);
            item.PropertyChanged += OnTargetPropertyChanged;
            Targets.Add(item);
        }

        Results.Clear();
        HasResults = false;
        HasScan = Targets.Count > 0;
        WasCancelled = false;
        SummaryText = string.Empty;
        SummaryDetailText = string.Empty;

        RefreshSelection();
        RefreshDerived();

        SetStatus(
            HasScan ? "Cleanup.Status.Ready" : "Cleanup.Status.NothingFound",
            HasScan ? Severity.Ok : Severity.Info);

        await LoadRecycleBinAsync().ConfigureAwait(true);
    }

    private async Task LoadRecycleBinAsync()
    {
        try
        {
            var info = await _scanner.GetRecycleBinInfoAsync(LifetimeToken).ConfigureAwait(true);

            HasRecycleBinInfo = info.ItemCount > 0;
            RecycleBinText = Localizer.Format("Cleanup.RecycleBin.Info", info.ItemCount, Humanize.Bytes(info.SizeBytes));
        }
        catch (OperationCanceledException)
        {
            // Encerramento.
        }
        catch (Exception exception)
        {
            HasRecycleBinInfo = false;
            Logger.Warning("Cleaning", "Nao foi possivel consultar a Lixeira.", exception);
        }
    }

    private void ApplySummary(CleanupSummary summary)
    {
        WasCancelled = summary.WasCancelled;
        HasResults = summary.Results.Count > 0;

        Results.Clear();
        foreach (var result in summary.Results)
        {
            var titleKey = Targets.FirstOrDefault(target => target.Id == result.TargetId)?.Model.TitleKey
                ?? "Cleanup.Target.RecycleBin.Title";

            Results.Add(new CleanupResultItemViewModel(result, titleKey, Localizer));
        }

        SummaryText = Localizer.Format("Cleanup.Result.Summary", summary.TotalDeletedFiles, Humanize.Bytes(summary.TotalDeletedBytes));

        var elapsed = Humanize.Duration(summary.Duration, Localizer["Common.Duration.SubSecond"]);

        SummaryDetailText = summary.TotalSkippedFiles > 0
            ? Localizer.Format("Cleanup.Result.SummaryDetail", summary.TotalSkippedFiles, elapsed)
            : Localizer.Format("Cleanup.Result.Duration", elapsed);

        SetStatus(
            WasCancelled ? "Cleanup.Status.Cancelled" : "Cleanup.Status.Done",
            WasCancelled ? Severity.Warning : Severity.Ok);

        RefreshDerived();
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CleanupTargetItemViewModel.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        SelectedCount = Targets.Count(target => target.IsSelected);
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(RecycleBinTitle));
        OnPropertyChanged(nameof(RecycleBinSubtitle));
        OnPropertyChanged(nameof(EmptyRecycleBinAction));
        OnPropertyChanged(nameof(SafetyNote));
        OnPropertyChanged(nameof(RestoreNote));
    }
}
