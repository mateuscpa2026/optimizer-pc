using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
/// Armazenamento: volumes montados, saude dos discos, uso por categoria, arquivos
/// grandes e arquivos duplicados. Tudo nesta tela e somente leitura: nada e apagado
/// aqui. A remocao acontece na tela de Limpeza, sempre com confirmacao. Os caminhos
/// encontrados podem ser copiados para o usuario decidir o que fazer.
/// </summary>
public sealed partial class StorageViewModel : ViewModelBase
{
    private const long Megabyte = 1024L * 1024L;
    private const long Gigabyte = 1024L * Megabyte;

    private static readonly long[] LargeFileMinimums =
    {
        100 * Megabyte,
        500 * Megabyte,
        Gigabyte,
        5 * Gigabyte
    };

    private static readonly long[] DuplicateMinimums =
    {
        Megabyte,
        10 * Megabyte,
        50 * Megabyte,
        100 * Megabyte
    };

    private readonly IStorageAnalyzer _analyzer;
    private readonly IDriveHealthService _driveHealth;
    private readonly ILargeFileFinder _largeFileFinder;
    private readonly IDuplicateFinder _duplicateFinder;
    private readonly IElevationService _elevation;
    private readonly IDialogService _dialogs;

    public StorageViewModel(
        IStorageAnalyzer analyzer,
        IDriveHealthService driveHealth,
        ILargeFileFinder largeFileFinder,
        IDuplicateFinder duplicateFinder,
        IElevationService elevation,
        IDialogService dialogs,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _analyzer = analyzer;
        _driveHealth = driveHealth;
        _largeFileFinder = largeFileFinder;
        _duplicateFinder = duplicateFinder;
        _elevation = elevation;
        _dialogs = dialogs;

        _isElevated = elevation.IsElevated;

        LargeFileMinimumOptions = LargeFileMinimums.Select(bytes => new SizeOption(bytes)).ToList();
        DuplicateMinimumOptions = DuplicateMinimums.Select(bytes => new SizeOption(bytes)).ToList();

        _largeFileMinimum = LargeFileMinimumOptions[2];
        _duplicateMinimum = DuplicateMinimumOptions[1];
    }

    public ObservableCollection<VolumeItemViewModel> Volumes { get; } = new();

    public ObservableCollection<StorageDeviceItemViewModel> Devices { get; } = new();

    public ObservableCollection<StorageCategoryItemViewModel> Categories { get; } = new();

    public ObservableCollection<LargeFileItemViewModel> LargeFiles { get; } = new();

    public ObservableCollection<DuplicateGroupItemViewModel> DuplicateGroups { get; } = new();

    public ObservableCollection<RootOption> Roots { get; } = new();

    public IReadOnlyList<SizeOption> LargeFileMinimumOptions { get; }

    public IReadOnlyList<SizeOption> DuplicateMinimumOptions { get; }

    [ObservableProperty]
    private bool _hasVolumes;

    [ObservableProperty]
    private bool _hasDevices;

    [ObservableProperty]
    private bool _hasAnalysis;

    [ObservableProperty]
    private bool _hasLargeFiles;

    [ObservableProperty]
    private bool _hasDuplicates;

    [ObservableProperty]
    private bool _hasRoots;

    [ObservableProperty]
    private string _capacityText = string.Empty;

    [ObservableProperty]
    private string _usedSpaceText = string.Empty;

    [ObservableProperty]
    private string _freeSpaceText = string.Empty;

    [ObservableProperty]
    private double _freeSpacePercent;

    [ObservableProperty]
    private string _volumeCountText = string.Empty;

    [ObservableProperty]
    private string _deviceCountText = string.Empty;

    [ObservableProperty]
    private bool _hasVolumeList;

    [ObservableProperty]
    private VolumeItemViewModel? _selectedVolume;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _temporaryText = string.Empty;

    [ObservableProperty]
    private string _analysisAtText = string.Empty;

    [ObservableProperty]
    private bool _hasAnalysisNote;

    [ObservableProperty]
    private string _analysisNote = string.Empty;

    [ObservableProperty]
    private string _largeFileSummaryText = string.Empty;

    [ObservableProperty]
    private string _duplicateSummaryText = string.Empty;

    [ObservableProperty]
    private string _duplicateWastedText = string.Empty;

    [ObservableProperty]
    private SizeOption _largeFileMinimum;

    [ObservableProperty]
    private SizeOption _duplicateMinimum;

    public string LowSpaceNote => Localizer["Storage.Volume.LowSpace.Note"];

    public bool HasLowSpace => Volumes.Any(volume => volume.IsLowOnSpace);

    public string SelectedVolumeTitle => SelectedVolume?.Title ?? Localizer["Storage.Volume.Select.None"];

    public bool HasSelectedVolume => SelectedVolume is not null;

    public bool ShowElevationNote => IsElevated is false;

    public string ElevationNote => Localizer["Storage.Elevation.Note"];

    public bool CanRunMaintenance => SelectedVolume is { IsReady: true } && IsElevated && IsBusy is false;

    public string MaintenanceNote => Localizer["Storage.Maintenance.Note"];

    public string AnalysisTitle => Localizer["Storage.Analysis.Title"];

    public string AnalysisHint => Localizer["Storage.Analysis.Hint"];

    public string AnalysisEmptyText => Localizer["Storage.Analysis.Empty"];

    public string AnalysisUnavailableNote => Localizer["Storage.Analysis.Unavailable"];

    public string LargeFileTitle => Localizer["Storage.Large.Title"];

    public string LargeFileHint => Localizer["Storage.Large.Hint"];

    public string LargeFileEmptyText => Localizer["Storage.Large.Empty"];

    public string DuplicateTitle => Localizer["Storage.Duplicates.PanelTitle"];

    public string DuplicateHint => Localizer["Storage.Duplicates.Hint"];

    public string DuplicateEmptyText => Localizer["Storage.Duplicates.Empty"];

    public string RootsTitle => Localizer["Storage.Roots.Title"];

    public string RootsHint => Localizer["Storage.Roots.Hint"];

    public string VolumesTitle => Localizer["Storage.Volumes.Title"];

    public string DevicesTitle => Localizer["Storage.Devices.Title"];

    public string DevicesEmptyText => Localizer["Storage.Devices.Empty"];

    public string VolumesEmptyText => Localizer["Storage.Volumes.Empty"];

    public string StorageDisclaimer => Localizer["Storage.Disclaimer"];

    public string MaintenanceTitle => Localizer["Storage.Maintenance.Title"];

    public string MinimumSizeLabel => Localizer["Storage.Minimum.Label"];

    protected override async Task OnNavigatedToAsync() => await RefreshAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged()
    {
        RefreshDerived();
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        var progress = new Progress<StorageScanProgress>(item => SetProgress(
            item.PercentComplete,
            "Storage.Analysis.Progress",
            item.FilesProcessed));

        var (ok, result) = await RunAsync<StorageAnalysisResult>(
            token => _analyzer.AnalyzeAsync(progress, token),
            "Storage.Analysis.Preparing").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        ApplyAnalysis(result);
        SetStatus("Storage.Analysis.Done", Severity.Ok);
    }

    [RelayCommand]
    private async Task ScanLargeFilesAsync()
    {
        var roots = SelectedRoots();
        if (roots.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Storage.Roots.Title", "Storage.Roots.Empty").ConfigureAwait(true);
            return;
        }

        var minimum = LargeFileMinimum.Bytes;

        var progress = new Progress<StorageScanProgress>(item => SetProgress(
            item.PercentComplete,
            "Storage.Large.Progress",
            item.FilesProcessed,
            Humanize.Bytes(item.BytesProcessed)));

        var (ok, found) = await RunAsync<IReadOnlyList<LargeFileInfo>>(
            token => _largeFileFinder.FindAsync(roots, minimum, progress, token),
            "Storage.Large.Preparing").ConfigureAwait(true);

        if (ok is false || found is null)
        {
            return;
        }

        LargeFiles.Clear();
        foreach (var file in found)
        {
            LargeFiles.Add(new LargeFileItemViewModel(file, Localizer));
        }

        HasLargeFiles = LargeFiles.Count > 0;
        LargeFileSummaryText = Localizer.Format(
            "Storage.Large.Summary",
            LargeFiles.Count,
            Humanize.Bytes(found.Sum(file => file.SizeBytes)),
            Humanize.Bytes(minimum));

        SetStatus("Storage.Large.Done", Severity.Ok);
    }

    [RelayCommand]
    private async Task ScanDuplicatesAsync()
    {
        var roots = SelectedRoots();
        if (roots.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Storage.Roots.Title", "Storage.Roots.Empty").ConfigureAwait(true);
            return;
        }

        var minimum = DuplicateMinimum.Bytes;
        var lastReport = 0;

        var progress = new Progress<DuplicateScanProgress>(item =>
        {
            lastReport = item.FilesHashed;
            SetProgress(0, "Storage.Duplicates.Progress", item.FilesHashed, item.GroupsFound);
        });

        var (ok, result) = await RunAsync<DuplicateScanResult>(
            token => _duplicateFinder.FindAsync(roots, minimum, progress, token),
            "Storage.Duplicates.Preparing").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        DuplicateGroups.Clear();
        foreach (var group in result.Groups)
        {
            DuplicateGroups.Add(new DuplicateGroupItemViewModel(group, Localizer));
        }

        HasDuplicates = DuplicateGroups.Count > 0;
        DuplicateSummaryText = Localizer.Format(
            "Storage.Duplicates.Summary",
            result.Groups.Count,
            result.ScannedFiles,
            Humanize.Bytes(minimum));
        DuplicateWastedText = Localizer.Format("Storage.Duplicates.Wasted", Humanize.Bytes(result.TotalWastedBytes));

        if (result.WasCancelled)
        {
            SetStatus("Storage.Scan.Cancelled", Severity.Warning);
            return;
        }

        SetStatus("Storage.Duplicates.Done", Severity.Ok);
    }

    [RelayCommand]
    private async Task TrimAsync()
    {
        var volume = SelectedVolume;
        if (volume is null)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Storage.Maintenance.Title",
                "Storage.Trim.Confirm",
                "Storage.Trim.Run",
                volume.Title)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<VolumeMaintenanceResult>(
            token => _driveHealth.RunTrimAsync(volume.Model, token),
            "Storage.Trim.Running").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportMaintenanceAsync(result, "Storage.Trim.Done").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CheckDiskAsync()
    {
        var volume = SelectedVolume;
        if (volume is null)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Storage.Maintenance.Title",
                "Storage.Chkdsk.Confirm",
                "Storage.Chkdsk.Run",
                volume.Title)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<VolumeMaintenanceResult>(
            token => _driveHealth.RunChkdskScanAsync(volume.Model, token),
            "Storage.Chkdsk.Running").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        await ReportMaintenanceAsync(result, "Storage.Chkdsk.Done").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Clipboard.SetText(path);
            SetStatus("Storage.Path.Copied", Severity.Ok);
        }
        catch (Exception exception)
        {
            Logger.Warning("Storage", "Nao foi possivel copiar o caminho: " + exception.Message);
            await _dialogs.ShowWarningAsync("Storage.Path.Copy.Title", "Storage.Path.Copy.Failed", path).ConfigureAwait(true);
        }
    }

    private async Task ReportMaintenanceAsync(VolumeMaintenanceResult result, string successKey)
    {
        if (result.RequiresElevation)
        {
            SetStatus("Storage.Maintenance.Elevation", Severity.Warning);
            await _dialogs
                .ShowWarningAsync("Storage.Maintenance.Title", "Storage.Maintenance.Elevation", SelectedVolumeTitle)
                .ConfigureAwait(true);
            return;
        }

        if (result.Started is false)
        {
            var messageKey = string.IsNullOrWhiteSpace(result.MessageKey) ? "Storage.Maintenance.Unavailable" : result.MessageKey;
            SetStatus(messageKey, Severity.Warning);
            await _dialogs
                .ShowWarningAsync("Storage.Maintenance.Title", messageKey, Trim(result.Output))
                .ConfigureAwait(true);
            return;
        }

        SetStatus(successKey, Severity.Ok);

        await _dialogs
            .ShowSuccessAsync("Storage.Maintenance.Title", successKey, Trim(result.Output))
            .ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var (ok, data) = await RunAsync<(IReadOnlyList<VolumeInfo> Volumes, IReadOnlyList<StorageDeviceInfo> Devices)?>(
            async token =>
            {
                var volumes = await _analyzer.GetVolumesAsync(token).ConfigureAwait(true);
                var devices = await _analyzer.GetDevicesAsync(token).ConfigureAwait(true);
                return (volumes, devices);
            },
            "Storage.Loading").ConfigureAwait(true);

        if (ok is false || data is null)
        {
            return;
        }

        ApplyVolumes(data.Value.Volumes);
        ApplyDevices(data.Value.Devices);
        ApplyRoots(data.Value.Volumes);
    }

    private void ApplyVolumes(IReadOnlyList<VolumeInfo> volumes)
    {
        var previous = SelectedVolume?.DriveLetter;

        Volumes.Clear();
        foreach (var volume in volumes)
        {
            Volumes.Add(new VolumeItemViewModel(volume, Localizer));
        }

        HasVolumes = Volumes.Count > 0;
        HasVolumeList = HasVolumes;

        var ready = volumes.Where(volume => volume.IsReady).ToList();

        CapacityText = Humanize.Bytes(ready.Sum(volume => volume.TotalBytes));
        UsedSpaceText = Humanize.Bytes(ready.Sum(volume => volume.UsedBytes));
        FreeSpaceText = Humanize.Bytes(ready.Sum(volume => volume.FreeBytes));

        var total = ready.Sum(volume => volume.TotalBytes);
        var free = ready.Sum(volume => volume.FreeBytes);
        FreeSpacePercent = total <= 0 ? 0 : free * 100d / total;

        VolumeCountText = Localizer.Format("Storage.Volumes.Count", Volumes.Count);

        SelectedVolume = Volumes.FirstOrDefault(volume => string.Equals(volume.DriveLetter, previous, StringComparison.OrdinalIgnoreCase))
            ?? Volumes.FirstOrDefault(volume => volume.IsSystemDrive)
            ?? Volumes.FirstOrDefault();

        RefreshDerived();
    }

    private void ApplyDevices(IReadOnlyList<StorageDeviceInfo> devices)
    {
        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(new StorageDeviceItemViewModel(device, Localizer));
        }

        HasDevices = Devices.Count > 0;
        DeviceCountText = Localizer.Format("Storage.Devices.Count", Devices.Count);
    }

    private void ApplyRoots(IReadOnlyList<VolumeInfo> volumes)
    {
        var previous = Roots
            .Where(root => root.IsSelected)
            .Select(root => root.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = volumes
            .Where(volume => volume.IsReady && Directory.Exists(volume.RootPath))
            .Select(volume => volume.RootPath.TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Roots.Clear();
        foreach (var path in candidates)
        {
            var option = new RootOption(path)
            {
                IsSelected = previous.Count == 0 || previous.Contains(path)
            };

            Roots.Add(option);
        }

        HasRoots = Roots.Count > 0;
    }

    private void ApplyAnalysis(StorageAnalysisResult result)
    {
        Categories.Clear();

        var total = result.TotalBytes;
        foreach (var category in result.Categories.OrderByDescending(item => item.Bytes))
        {
            Categories.Add(new StorageCategoryItemViewModel(category, total, Localizer));
        }

        TemporaryText = Humanize.Bytes(result.TemporaryBytes);
        AnalysisAtText = Localizer.Format("Storage.Analysis.At", Humanize.Date(result.CompletedAtUtc.ToLocalTime()));
        AnalysisNote = BuildNote(result.Notes);
        HasAnalysisNote = AnalysisNote.Length > 0;
        HasAnalysis = Categories.Count > 0;

        RefreshDerived();
    }

    private string BuildNote(IReadOnlyList<string> keys)
    {
        var parts = keys
            .Where(key => string.IsNullOrWhiteSpace(key) is false)
            .Select(key => Localizer.TryGet(key, out var text) ? text : Localizer[key])
            .ToList();

        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    private IReadOnlyList<string> SelectedRoots() =>
        Roots.Where(root => root.IsSelected).Select(root => root.Path).ToList();

    private static string Trim(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length <= 1200 ? trimmed : trimmed.Substring(0, 1200) + "…";
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasLowSpace));
        OnPropertyChanged(nameof(LowSpaceNote));
        OnPropertyChanged(nameof(SelectedVolumeTitle));
        OnPropertyChanged(nameof(HasSelectedVolume));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(CanRunMaintenance));
        OnPropertyChanged(nameof(MaintenanceNote));
        OnPropertyChanged(nameof(MaintenanceTitle));
        OnPropertyChanged(nameof(AnalysisTitle));
        OnPropertyChanged(nameof(AnalysisHint));
        OnPropertyChanged(nameof(AnalysisEmptyText));
        OnPropertyChanged(nameof(AnalysisUnavailableNote));
        OnPropertyChanged(nameof(LargeFileTitle));
        OnPropertyChanged(nameof(LargeFileHint));
        OnPropertyChanged(nameof(LargeFileEmptyText));
        OnPropertyChanged(nameof(DuplicateTitle));
        OnPropertyChanged(nameof(DuplicateHint));
        OnPropertyChanged(nameof(DuplicateEmptyText));
        OnPropertyChanged(nameof(RootsTitle));
        OnPropertyChanged(nameof(RootsHint));
        OnPropertyChanged(nameof(VolumesTitle));
        OnPropertyChanged(nameof(VolumesEmptyText));
        OnPropertyChanged(nameof(DevicesTitle));
        OnPropertyChanged(nameof(DevicesEmptyText));
        OnPropertyChanged(nameof(StorageDisclaimer));
        OnPropertyChanged(nameof(MinimumSizeLabel));
    }

    partial void OnSelectedVolumeChanged(VolumeItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedVolumeTitle));
        OnPropertyChanged(nameof(HasSelectedVolume));
        OnPropertyChanged(nameof(CanRunMaintenance));
    }

    partial void OnIsElevatedChanged(bool value) => OnPropertyChanged(nameof(CanRunMaintenance));

    /// <summary>Tamanho minimo escolhido nas buscas de arquivos grandes e duplicados.</summary>
    public sealed class SizeOption
    {
        public SizeOption(long bytes) => Bytes = bytes;

        public long Bytes { get; }

        public string Text => Humanize.Bytes(Bytes);
    }

    /// <summary>Unidade incluida na varredura; o usuario escolhe o que analisar.</summary>
    public sealed class RootOption : ObservableObject
    {
        private bool _isSelected = true;

        public RootOption(string path) => Path = path;

        public string Path { get; }

        public string Title => Path;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
