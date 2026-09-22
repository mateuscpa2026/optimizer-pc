using System.Collections.ObjectModel;
using System.IO;
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
/// Backup e restauracao. Cada alteracao relevante registra um ponto de reversao antes
/// de ser aplicada; aqui o usuario revisa esses registros, desfaz o que quiser e cria
/// pontos de restauracao do Windows. Nada e alterado sem confirmacao.
/// </summary>
public sealed partial class RestoreViewModel : ViewModelBase
{
    private readonly IRestoreService _restore;
    private readonly IRestorePointManager _points;
    private readonly IStorageAnalyzer _analyzer;
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;

    public RestoreViewModel(
        IRestoreService restore,
        IRestorePointManager points,
        IStorageAnalyzer analyzer,
        IDialogService dialogs,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _restore = restore;
        _points = points;
        _analyzer = analyzer;
        _dialogs = dialogs;
        _elevation = elevation;

        _isElevated = elevation.IsElevated;
        _pointDescription = DefaultPointDescription();

        // IsBusy e uma propriedade manual da base: os botoes dependem dela.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanAct));
            }
        };
    }

    public ObservableCollection<RestoreRecordItemViewModel> Records { get; } = new();

    public ObservableCollection<RestorePointItemViewModel> Points { get; } = new();

    [ObservableProperty]
    private bool _hasRecords;

    [ObservableProperty]
    private RestoreRecordItemViewModel? _selectedRecord;

    [ObservableProperty]
    private bool _hasPoints;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _pointDescription;

    [ObservableProperty]
    private bool _isProtectionEnabled;

    [ObservableProperty]
    private bool _isProtectionSupported = true;

    [ObservableProperty]
    private string _protectionStatusText = string.Empty;

    [ObservableProperty]
    private string _recordsSummaryText = string.Empty;

    public string Subtitle => Localizer["Restore.Subtitle"];

    public string RecordsTitle => Localizer["Restore.Records.Title"];

    public string RecordsEmptyText => Localizer["Restore.Records.Empty"];

    public string RecordsHint => Localizer["Restore.Records.Hint"];

    public string PointsTitle => Localizer["Restore.Points.Title"];

    public string PointsEmptyText => Localizer["Restore.Points.Empty"];

    public string PointsNote => Localizer["Restore.Points.Note"];

    public string PointDescriptionLabel => Localizer["Restore.Point.Description"];

    public string PointDescriptionNote => Localizer["Restore.Point.Description.Note"];

    public string ElevationNote => Localizer["Restore.Elevation.Note"];

    public string Disclaimer => Localizer["Restore.Disclaimer"];

    public string SelectedRecordTitle => SelectedRecord?.Title ?? Localizer["Restore.Record.Select.None"];

    public bool HasSelectedRecord => SelectedRecord?.CanRestore is true;

    public bool ShowElevationNote => IsElevated is false;

    public bool CanAct => IsBusy is false;

    public bool CanCreatePoint => IsBusy is false && PointDescription.Trim().Length > 0;

    protected override async Task OnNavigatedToAsync() => await RefreshAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged() => RefreshDerived();

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task UndoAsync(RestoreRecordItemViewModel? item)
    {
        var record = item ?? SelectedRecord;
        if (record is null || record.CanRestore is false)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Restore.Undo.Title",
                "Restore.Undo.Confirm",
                "Restore.Undo.Action",
                record.Title + Environment.NewLine + record.CreatedText)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, restored) = await RunAsync<bool>(
            token => _restore.UndoAsync(record.Model, token),
            "Restore.Undo.Running").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        if (restored)
        {
            SetStatus("Restore.Undo.Done", Severity.Ok);
            await _dialogs.ShowSuccessAsync("Restore.Undo.Title", "Restore.Undo.Done", record.Title).ConfigureAwait(true);
        }
        else
        {
            SetStatus("Restore.Undo.Failed", Severity.Warning);
            await _dialogs.ShowWarningAsync("Restore.Undo.Title", "Restore.Undo.Failed", record.Title).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreatePointAsync()
    {
        var description = PointDescription.Trim();
        if (description.Length == 0)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Restore.Point.Create.Title",
                "Restore.Point.Create.Confirm",
                "Restore.Point.Create.Action",
                description)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<RestorePointCreationResult>(
            token => _points.CreateAsync(description, token),
            "Restore.Point.Creating").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        var messageKey = string.IsNullOrWhiteSpace(result.MessageKey) ? "Restore.Point.Failed" : result.MessageKey;

        if (result.Success)
        {
            SetStatus(messageKey, Severity.Ok);
            await _dialogs.ShowSuccessAsync("Restore.Point.Create.Title", messageKey, description).ConfigureAwait(true);
        }
        else if (result.RequiresElevation)
        {
            SetStatus(messageKey, Severity.Warning);
            await _dialogs.ShowWarningAsync("Restore.Point.Create.Title", messageKey, Localizer["Restore.Elevation.Note"]).ConfigureAwait(true);
        }
        else
        {
            SetStatus(messageKey, Severity.Warning);
            await _dialogs.ShowWarningAsync("Restore.Point.Create.Title", messageKey).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EnableProtectionAsync()
    {
        var (ok, volumes) = await RunAsync<IReadOnlyList<VolumeInfo>>(
            token => _analyzer.GetVolumesAsync(token),
            "Restore.Point.Loading").ConfigureAwait(true);

        var systemVolume = ok ? volumes?.FirstOrDefault(volume => volume.IsSystemDrive) : null;
        if (systemVolume is null)
        {
            SetStatus("Restore.Point.NoSystemVolume", Severity.Warning);
            return;
        }

        var (opened, result) = await RunAsync<RestorePointCreationResult>(
            token => _points.EnableProtectionAsync(systemVolume, token),
            "Restore.Point.Enabling").ConfigureAwait(true);

        if (opened is false || result is null)
        {
            return;
        }

        var messageKey = string.IsNullOrWhiteSpace(result.MessageKey) ? "Restore.Point.OpenFailed" : result.MessageKey;
        SetStatus(messageKey, result.Success ? Severity.Ok : Severity.Warning);

        await _dialogs
            .ShowInfoAsync("Restore.Protection.Title", messageKey, systemVolume.RootPath)
            .ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenProtectionAsync()
    {
        var opened = await _points.OpenSystemProtectionAsync().ConfigureAwait(true);
        if (opened is false)
        {
            await _dialogs.ShowWarningAsync("Restore.Protection.Title", "Restore.Point.OpenFailed").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenWizardAsync()
    {
        var opened = await _points.OpenSystemRestoreAsync().ConfigureAwait(true);
        if (opened is false)
        {
            await _dialogs.ShowWarningAsync("Restore.Protection.Title", "Restore.Point.OpenFailed").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Records.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Restore.Export.Title", "Restore.Export.Empty").ConfigureAwait(true);
            return;
        }

        var suggested = "optimizerpc-restauracao-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
        var path = _dialogs.AskSaveFile(
            suggested,
            Localizer["Restore.Export.Filter"],
            "Restore.Export.Title");

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ReportFormat.Csv,
            ".html" or ".htm" => ReportFormat.Html,
            _ => ReportFormat.Json
        };

        var records = Records.Select(item => item.Model).ToList();

        var (ok, saved) = await RunAsync<string>(
            async token =>
            {
                await _restore.ExportAsync(records, path, format, token).ConfigureAwait(true);
                return path;
            },
            "Restore.Export.Running").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        SetStatus("Restore.Export.Done", Severity.Ok);
        await _dialogs.ShowSuccessAsync("Restore.Export.Title", "Restore.Export.Done", saved).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestartElevatedAsync()
    {
        var confirmed = await _dialogs
            .ConfirmAsync("Restore.Elevation.Title", "Restore.Elevation.Confirm")
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var started = await _elevation.RestartElevatedAsync("Restore.Elevation.Note").ConfigureAwait(true);
        if (started is false)
        {
            await _dialogs.ShowWarningAsync("Restore.Elevation.Title", "Restore.Elevation.Cancelled").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (Records.Count == 0)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Restore.Clear.Title",
                "Restore.Clear.Confirm",
                "Restore.Clear.Action",
                Localizer.Format("Restore.Clear.Detail", Records.Count),
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, removed) = await RunAsync<int>(
            token => _restore.ClearHistoryAsync(token),
            "Restore.Clear.Running").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        SetStatusFormat("Restore.Clear.Done", Severity.Ok, removed);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var (ok, data) = await RunAsync<(IReadOnlyList<RestoreRecord> Records, SystemRestoreStatus Status, IReadOnlyList<RestorePointInfo> Points)?>(
            async token =>
            {
                var records = await _restore.GetRecordsAsync(token).ConfigureAwait(true);
                var status = await _points.GetStatusAsync(token).ConfigureAwait(true);
                var points = await _points.ListAsync(token).ConfigureAwait(true);
                return (records, status, points);
            },
            "Restore.Loading").ConfigureAwait(true);

        if (ok is false || data is null)
        {
            return;
        }

        ApplyRecords(data.Value.Records);
        ApplyStatus(data.Value.Status);
        ApplyPoints(data.Value.Points);

        IsElevated = _elevation.IsElevated;
        RefreshDerived();
    }

    private void ApplyRecords(IReadOnlyList<RestoreRecord> records)
    {
        var previous = SelectedRecord?.Key;

        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(new RestoreRecordItemViewModel(record, Localizer));
        }

        HasRecords = Records.Count > 0;

        var available = records.Count(record => record.CanRestore);
        RecordsSummaryText = Localizer.Format("Restore.Records.Summary", Records.Count, available);

        SelectedRecord = Records.FirstOrDefault(record => string.Equals(record.Key, previous, StringComparison.Ordinal))
            ?? Records.FirstOrDefault(record => record.CanRestore);
    }

    private void ApplyStatus(SystemRestoreStatus status)
    {
        IsProtectionSupported = status.IsSupported;
        IsProtectionEnabled = status.IsEnabled;

        ProtectionStatusText = string.IsNullOrWhiteSpace(status.Message)
            ? Localizer[status.IsEnabled ? "Restore.Protection.On" : "Restore.Protection.Off"]
            : status.Message;
    }

    private void ApplyPoints(IReadOnlyList<RestorePointInfo> points)
    {
        Points.Clear();
        foreach (var point in points)
        {
            Points.Add(new RestorePointItemViewModel(point, Localizer));
        }

        HasPoints = Points.Count > 0;
    }

    private string DefaultPointDescription() =>
        Localizer.Format("Restore.Point.DefaultDescription", Humanize.Date(DateTime.Now));

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(RecordsTitle));
        OnPropertyChanged(nameof(RecordsEmptyText));
        OnPropertyChanged(nameof(RecordsHint));
        OnPropertyChanged(nameof(PointsTitle));
        OnPropertyChanged(nameof(PointsEmptyText));
        OnPropertyChanged(nameof(PointsNote));
        OnPropertyChanged(nameof(PointDescriptionLabel));
        OnPropertyChanged(nameof(PointDescriptionNote));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(SelectedRecordTitle));
        OnPropertyChanged(nameof(HasSelectedRecord));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanCreatePoint));
    }

    partial void OnSelectedRecordChanged(RestoreRecordItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedRecordTitle));
        OnPropertyChanged(nameof(HasSelectedRecord));
    }

    partial void OnIsElevatedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(CanAct));
    }

    partial void OnPointDescriptionChanged(string value) => OnPropertyChanged(nameof(CanCreatePoint));
}
