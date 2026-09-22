using System.Collections.ObjectModel;
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
/// Relatorios locais. O usuario escolhe o formato e o que entra no documento; o
/// arquivo e gravado na pasta de relatorios do aplicativo e fica listado abaixo.
/// Nada e enviado para fora do computador.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private const int HistoryLimit = 200;

    private readonly IReportService _reportsService;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly IDialogService _dialogs;

    public ReportsViewModel(
        IReportService reportsService,
        ISettingsService settings,
        IAppPaths paths,
        IDialogService dialogs,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _reportsService = reportsService;
        _settings = settings;
        _paths = paths;
        _dialogs = dialogs;

        foreach (var format in SupportedFormats)
        {
            Formats.Add(new ReportFormatItemViewModel(format, localizer));
        }

        _selectedFormat = Formats[0];
        _reportTitle = DefaultTitle();

        // IsBusy e manual na base: o botao de gerar depende dela.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanGenerate));
            }
        };
    }

    private static IReadOnlyList<ReportFormat> SupportedFormats { get; } = new[]
    {
        ReportFormat.Html,
        ReportFormat.Pdf,
        ReportFormat.Txt,
        ReportFormat.Json,
        ReportFormat.Csv
    };

    public ObservableCollection<ReportFormatItemViewModel> Formats { get; } = new();

    public ObservableCollection<ReportItemViewModel> Reports { get; } = new();

    [ObservableProperty]
    private ReportFormatItemViewModel? _selectedFormat;

    [ObservableProperty]
    private string _reportTitle;

    [ObservableProperty]
    private bool _includeSystemInfo = true;

    [ObservableProperty]
    private bool _includeDiagnosis = true;

    [ObservableProperty]
    private bool _includeRecommendations = true;

    [ObservableProperty]
    private bool _includeHistory = true;

    [ObservableProperty]
    private bool _includeCleanupSummary = true;

    [ObservableProperty]
    private bool _hasReports;

    [ObservableProperty]
    private ReportItemViewModel? _selectedReport;

    public string Subtitle => Localizer["Report.Subtitle"];

    public string GenerateTitle => Localizer["Report.Generate.Title"];

    public string FormatLabel => Localizer["Report.Format.Label"];

    public string TitleLabel => Localizer["Report.Title.Label"];

    public string TitleNote => Localizer["Report.Title.Note"];

    public string FolderLabel => Localizer["Report.Folder.Label"];

    public string FolderNote => Localizer["Report.Folder.Note"];

    public string SectionsLabel => Localizer["Report.Sections.Label"];

    public string SectionSystemInfoText => Localizer["Report.Section.SystemInfo"];

    public string SectionDiagnosisText => Localizer["Report.Section.Diagnosis"];

    public string SectionRecommendationsText => Localizer["Report.Section.Recommendations"];

    public string SectionHistoryText => Localizer["Report.Section.History"];

    public string SectionCleanupText => Localizer["Report.Section.CleanupSummary"];

    public string RecentTitle => Localizer["Report.Recent.Title"];

    public string RecentEmptyText => Localizer["Report.Recent.Empty"];

    public string RecentHint => Localizer["Report.Recent.Hint"];

    public string DetailsTitle => Localizer["Report.Details.Title"];

    public string DetailsEmptyText => Localizer["Report.Details.Empty"];

    public string CopyPathText => Localizer["Report.CopyPath"];

    public string GenerateText => Localizer["Report.Generate"];

    public string Disclaimer => Localizer["Report.Disclaimer"];

    /// <summary>Pasta real usada pelo servico: a configurada ou a pasta padrao do aplicativo.</summary>
    public string OutputFolder => string.IsNullOrWhiteSpace(_settings.Current.ReportsFolder)
        ? _paths.ReportsFolder
        : _settings.Current.ReportsFolder;

    public bool CanGenerate => IsBusy is false && ReportTitle.Trim().Length > 0;

    public bool HasSelectedReport => SelectedReport is not null;

    public string ReportsSummary => Reports.Count == 0
        ? string.Empty
        : Localizer.Format("Report.Recent.Count", Reports.Count);

    protected override async Task OnNavigatedToAsync() => await RefreshAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged() => RefreshDerived();

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task GenerateAsync()
    {
        var title = ReportTitle.Trim();
        if (title.Length == 0)
        {
            return;
        }

        var request = new ReportRequest
        {
            Format = SelectedFormat?.Format ?? ReportFormat.Html,
            Title = title,
            IncludeSystemInfo = IncludeSystemInfo,
            IncludeDiagnosis = IncludeDiagnosis,
            IncludeRecommendations = IncludeRecommendations,
            IncludeHistory = IncludeHistory,
            IncludeCleanupSummary = IncludeCleanupSummary
        };

        var (ok, result) = await RunAsync<ReportResult>(
            token => _reportsService.GenerateAsync(request, token),
            "Report.Generating").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            SetStatus("Report.Failed", Severity.Warning);
            await _dialogs
                .ShowWarningAsync("Report.Generate.Title", "Report.Failed", result.Message)
                .ConfigureAwait(true);
            return;
        }

        SetStatus("Report.Generated", Severity.Ok);
        await _dialogs
            .ShowSuccessAsync("Report.Generate.Title", "Report.Generated", result.FilePath)
            .ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);

        SelectedReport = Reports.FirstOrDefault(report =>
            string.Equals(report.FilePath, result.FilePath, StringComparison.OrdinalIgnoreCase)) ?? SelectedReport;
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? SelectedReport?.FilePath : path;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Clipboard.SetText(target);
            SetStatus("Report.Path.Copied", Severity.Ok);
        }
        catch (Exception exception)
        {
            Logger.Warning("Reports", "Nao foi possivel copiar o caminho: " + exception.Message);
            await _dialogs
                .ShowWarningAsync("Report.Path.Copy.Title", "Report.Path.Copy.Failed", target)
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void UseDefaultTitle() => ReportTitle = DefaultTitle();

    [RelayCommand]
    private void DeselectReport() => SelectedReport = null;

    private async Task RefreshAsync()
    {
        var (ok, records) = await RunAsync<IReadOnlyList<ReportRecord>>(
            token => _reportsService.GetHistoryAsync(token),
            "Report.Loading").ConfigureAwait(true);

        if (ok is false || records is null)
        {
            return;
        }

        var previous = SelectedReport?.Id;

        Reports.Clear();
        foreach (var record in records.Take(HistoryLimit))
        {
            Reports.Add(new ReportItemViewModel(record, Localizer));
        }

        HasReports = Reports.Count > 0;
        SelectedReport = Reports.FirstOrDefault(report => report.Id == previous) ?? Reports.FirstOrDefault();

        OnPropertyChanged(nameof(OutputFolder));
        OnPropertyChanged(nameof(ReportsSummary));
        RefreshDerived();
    }

    private string DefaultTitle() =>
        Localizer.Format("Report.DefaultTitle", Humanize.Date(DateTime.Now));

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(GenerateTitle));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(TitleLabel));
        OnPropertyChanged(nameof(TitleNote));
        OnPropertyChanged(nameof(FolderLabel));
        OnPropertyChanged(nameof(FolderNote));
        OnPropertyChanged(nameof(SectionsLabel));
        OnPropertyChanged(nameof(SectionSystemInfoText));
        OnPropertyChanged(nameof(SectionDiagnosisText));
        OnPropertyChanged(nameof(SectionRecommendationsText));
        OnPropertyChanged(nameof(SectionHistoryText));
        OnPropertyChanged(nameof(SectionCleanupText));
        OnPropertyChanged(nameof(RecentTitle));
        OnPropertyChanged(nameof(RecentEmptyText));
        OnPropertyChanged(nameof(RecentHint));
        OnPropertyChanged(nameof(DetailsTitle));
        OnPropertyChanged(nameof(DetailsEmptyText));
        OnPropertyChanged(nameof(CopyPathText));
        OnPropertyChanged(nameof(GenerateText));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(HasSelectedReport));
        OnPropertyChanged(nameof(ReportsSummary));

        foreach (var format in Formats)
        {
            format.Refresh();
        }

        foreach (var report in Reports)
        {
            report.Refresh();
        }
    }

    partial void OnReportTitleChanged(string value) => OnPropertyChanged(nameof(CanGenerate));

    partial void OnSelectedReportChanged(ReportItemViewModel? value) => OnPropertyChanged(nameof(HasSelectedReport));
}
