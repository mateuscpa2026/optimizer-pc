using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.App.Services;
using OptimizerPC.App.ViewModels.Items;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Historico local das acoes executadas. O filtro e montado a partir das categorias
/// realmente gravadas; nada e enviado para fora do computador.
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private static readonly int[] Limits = { 100, 200, 500, 1000 };

    private readonly IHistoryService _history;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    public HistoryViewModel(
        IHistoryService history,
        INavigationService navigation,
        IDialogService dialogs,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _history = history;
        _navigation = navigation;
        _dialogs = dialogs;

        foreach (var limit in Limits)
        {
            LimitsOptions.Add(limit);
        }

        _selectedLimit = LimitsOptions[1];

        // IsBusy e manual na base: a lista de filtros depende dela.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanAct));
            }
        };
    }

    /// <summary>Opcao do filtro de categoria; chave nula representa "todas".</summary>
    public sealed class CategoryOption
    {
        public CategoryOption(string? key, string text)
        {
            Key = key;
            Text = text;
        }

        public string? Key { get; }

        public string Text { get; }
    }

    public ObservableCollection<HistoryItemViewModel> Entries { get; } = new();

    public ObservableCollection<CategoryOption> Categories { get; } = new();

    public ObservableCollection<int> LimitsOptions { get; } = new();

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private int _selectedLimit;

    [ObservableProperty]
    private HistoryItemViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _hasEntries;

    [ObservableProperty]
    private bool _isCategoriesLoaded;

    public string Subtitle => Localizer["History.Subtitle"];

    public string FilterLabel => Localizer["History.Filter.Label"];

    public string CategoryLabel => Localizer["History.Category.Label"];

    public string LimitLabel => Localizer["History.Limit.Label"];

    public string EntriesTitle => Localizer["History.Entries.Title"];

    public string EntriesEmptyText => Localizer["History.Entries.Empty"];

    public string EntriesHint => Localizer["History.Entries.Hint"];

    public string DetailsTitle => Localizer["History.Details.Title"];

    public string DetailsEmptyText => Localizer["History.Details.Empty"];

    public string ExportText => Localizer["History.Export"];

    public string ClearText => Localizer["History.Clear.Button"];

    public string CopyDetailsText => Localizer["History.Details.Copy"];

    public string RestoreScreenText => Localizer["History.Details.Restore"];

    public string Disclaimer => Localizer["History.Disclaimer"];

    public bool CanAct => IsBusy is false;

    public bool HasSelection => SelectedEntry is not null;

    public bool CanOpenRestore => SelectedEntry?.HasRestoreRecord is true;

    public string EntriesSummary => Entries.Count == 0
        ? string.Empty
        : Localizer.Format("History.Summary", Entries.Count, Entries.Count(entry => entry.Success is false));

    protected override async Task OnNavigatedToAsync() => await RefreshAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged()
    {
        RebuildCategories();
        RefreshDerived();

        foreach (var entry in Entries)
        {
            entry.Refresh();
        }
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task ApplyFilterAsync() => await RefreshAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Entries.Count == 0)
        {
            await _dialogs.ShowInfoAsync("History.Export.Title", "History.Export.Empty").ConfigureAwait(true);
            return;
        }

        var suggested = "optimizerpc-historico-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
        var path = _dialogs.AskSaveFile(suggested, Localizer["History.Export.Filter"], "History.Export.Title");

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var (ok, saved) = await RunAsync<string?>(
            token => _history.ExportAsync(path, token),
            "History.Export.Running").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(saved))
        {
            SetStatus("History.Export.Failed", Severity.Warning);
            await _dialogs.ShowWarningAsync("History.Export.Title", "History.Export.Failed").ConfigureAwait(true);
            return;
        }

        SetStatus("History.Export.Done", Severity.Ok);
        await _dialogs.ShowSuccessAsync("History.Export.Title", "History.Export.Done", saved).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "History.Clear.Title",
                "History.Clear.Confirm",
                "History.Clear.Action",
                Localizer.Format("History.Clear.Detail", Entries.Count),
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, removed) = await RunAsync<int>(
            token => _history.ClearAsync(token),
            "History.Clear.Running").ConfigureAwait(true);

        if (ok is false)
        {
            return;
        }

        SetStatusFormat("History.Clear.Done", Severity.Ok, removed);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        var lines = new List<string>
        {
            entry.TimestampText + " · " + entry.Category,
            entry.Action,
            entry.Description,
            entry.StatusText
        };

        if (entry.HasDetails)
        {
            lines.Add(entry.Details!);
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            SetStatus("History.Details.Copied", Severity.Ok);
        }
        catch (Exception exception)
        {
            Logger.Warning("History", "Nao foi possivel copiar o registro: " + exception.Message);
            await _dialogs.ShowWarningAsync("History.Details.Title", "History.Details.CopyFailed").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenRestoreAsync()
    {
        if (await _navigation.NavigateAsync(Screen.Restore).ConfigureAwait(true) is false)
        {
            ReportError("Error.Navigation");
        }
    }

    private async Task RefreshAsync()
    {
        var category = SelectedCategory?.Key;

        var (ok, entries) = await RunAsync<IReadOnlyList<HistoryEntry>>(
            token => _history.QueryAsync(SelectedLimit, category, token),
            "History.Loading").ConfigureAwait(true);

        if (ok is false || entries is null)
        {
            return;
        }

        var previous = SelectedEntry?.Id;

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new HistoryItemViewModel(entry, Localizer));
        }

        HasEntries = Entries.Count > 0;
        SelectedEntry = Entries.FirstOrDefault(entry => entry.Id == previous) ?? Entries.FirstOrDefault();

        RebuildCategories();
        RefreshDerived();
    }

    /// <summary>
    /// Categorias exibidas: as que existem no banco agora. Assim nao aparece filtro vazio.
    /// </summary>
    private void RebuildCategories()
    {
        var previous = SelectedCategory?.Key;

        Categories.Clear();
        Categories.Add(new CategoryOption(null, Localizer["History.Category.All"]));

        foreach (var category in Entries
            .Select(entry => entry.Model.Category)
            .Where(value => string.IsNullOrWhiteSpace(value) is false)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            Categories.Add(new CategoryOption(category, Localizer[category]));
        }

        IsCategoriesLoaded = Categories.Count > 1;

        SelectedCategory = Categories.FirstOrDefault(option =>
            string.Equals(option.Key, previous, StringComparison.OrdinalIgnoreCase)) ?? Categories[0];
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(FilterLabel));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(LimitLabel));
        OnPropertyChanged(nameof(EntriesTitle));
        OnPropertyChanged(nameof(EntriesEmptyText));
        OnPropertyChanged(nameof(EntriesHint));
        OnPropertyChanged(nameof(DetailsTitle));
        OnPropertyChanged(nameof(DetailsEmptyText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(CopyDetailsText));
        OnPropertyChanged(nameof(RestoreScreenText));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanOpenRestore));
        OnPropertyChanged(nameof(EntriesSummary));
    }

    partial void OnSelectedEntryChanged(HistoryItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanOpenRestore));
    }
}
