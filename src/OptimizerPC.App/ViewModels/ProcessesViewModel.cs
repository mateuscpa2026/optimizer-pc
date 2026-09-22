using System.Collections.ObjectModel;
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
/// Processos em execucao. As metricas sao atualizadas periodicamente enquanto a tela
/// esta visivel. Encerrar um processo exige confirmacao e nunca e permitido para
/// processos criticos do Windows. A prioridade e ajustada dentro dos limites
/// suportados pelo sistema.
/// </summary>
public sealed partial class ProcessesViewModel : ViewModelBase
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(4);

    private readonly IProcessService _processes;
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;

    private readonly List<ProcessItemViewModel> _all = new();
    private CancellationTokenSource? _refreshLoop;

    public ProcessesViewModel(
        IProcessService processes,
        IDialogService dialogs,
        IElevationService elevation,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _processes = processes;
        _dialogs = dialogs;
        _elevation = elevation;
        PriorityOptions = BuildPriorityOptions(localizer);
    }

    public ObservableCollection<ProcessItemViewModel> Items { get; } = new();

    public IReadOnlyList<PriorityOption> PriorityOptions { get; private set; }

    [ObservableProperty] private ProcessItemViewModel? _selectedProcess;
    [ObservableProperty] private ProcessPriorityHint _selectedPriority = ProcessPriorityHint.Normal;
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private bool _busyOnly;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private int _processCount;
    [ObservableProperty] private string _totalMemoryText = string.Empty;
    [ObservableProperty] private string _totalCpuText = string.Empty;
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private DateTime? _updatedAt;

    public bool HasItems => Items.Count > 0;

    public bool IsFilteredEmpty => _all.Count > 0 && Items.Count == 0;

    public bool HasSelection => SelectedProcess is not null;

    public bool CanEndSelection => SelectedProcess?.CanEnd is true;

    public string ProcessCountText => Localizer.Format("Process.Summary.Count", ProcessCount);

    public string UpdatedText => UpdatedAt is null
        ? Localizer["Process.Summary.Never"]
        : Localizer.Format("Process.Summary.UpdatedAt", Humanize.Date(UpdatedAt.Value.ToLocalTime()));

    public string ElevationNote => Localizer["Process.Elevation.Note"];

    public bool ShowElevationNote => IsElevated is false && Items.Any(item => item.IsElevatedProcess);

    public string SafetyNote => Localizer["Process.Safety.Note"];

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        BusyOnly = false;
    }

    [RelayCommand]
    private async Task EndSelectedAsync()
    {
        var item = SelectedProcess;

        if (item is null || IsBusy || item.CanEnd is false)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmAsync("Process.End.Title", "Process.End.Message", item.Name, DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var processId = item.Id;
        var name = item.Name;

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _processes.EndProcessAsync(processId, token),
            "Process.End.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync("Process.End.Title", string.IsNullOrEmpty(result.MessageKey) ? "Process.End.Unavailable" : result.MessageKey, result.Detail)
                .ConfigureAwait(true);

            return;
        }

        SelectedProcess = null;
        SetStatusFormat("Process.End.Done", Severity.Ok, name);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ApplyPriorityAsync()
    {
        var item = SelectedProcess;

        if (item is null || IsBusy)
        {
            return;
        }

        var processId = item.Id;
        var priority = SelectedPriority;

        var confirmed = await _dialogs
            .ConfirmAsync("Process.Priority.Title", "Process.Priority.Message", item.Name, DialogKind.Question)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var (ok, result) = await RunAsync<ActionExecutionResult>(
            token => _processes.SetPriorityAsync(processId, priority, token),
            "Process.Priority.Working").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            return;
        }

        if (result.Success is false)
        {
            await _dialogs
                .ShowWarningAsync("Process.Priority.Title", string.IsNullOrEmpty(result.MessageKey) ? "Process.Priority.Unavailable" : result.MessageKey, result.Detail)
                .ConfigureAwait(true);

            return;
        }

        SetStatus("Process.Priority.Done", Severity.Ok);
    }

    protected override async Task OnInitializeAsync()
    {
        IsElevated = _elevation.IsElevated;
        await LoadAsync().ConfigureAwait(true);
    }

    protected override async Task OnNavigatedToAsync()
    {
        IsElevated = _elevation.IsElevated;
        await LoadAsync().ConfigureAwait(true);
        StartAutoRefresh();
    }

    protected override Task OnNavigatedFromAsync()
    {
        StopAutoRefresh();
        return Task.CompletedTask;
    }

    protected override void OnLanguageChanged()
    {
        PriorityOptions = BuildPriorityOptions(Localizer);

        foreach (var item in _all)
        {
            item.Refresh();
        }

        RefreshDerived();
    }

    protected override void OnDispose() => StopAutoRefresh();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnBusyOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedProcessChanged(ProcessItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEndSelection));
    }

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value)
        {
            StartAutoRefresh();
            return;
        }

        StopAutoRefresh();
    }

    private static IReadOnlyList<PriorityOption> BuildPriorityOptions(ILocalizer localizer) =>
    [
        new PriorityOption(ProcessPriorityHint.Normal, "Process.Priority.Normal", localizer),
        new PriorityOption(ProcessPriorityHint.BelowNormal, "Process.Priority.BelowNormal", localizer),
        new PriorityOption(ProcessPriorityHint.AboveNormal, "Process.Priority.AboveNormal", localizer),
        new PriorityOption(ProcessPriorityHint.High, "Process.Priority.High", localizer)
    ];

    private async Task LoadAsync()
    {
        var (ok, processes) = await RunAsync<IReadOnlyList<ProcessInfoModel>>(
            token => _processes.GetProcessesAsync(token),
            "Process.Loading").ConfigureAwait(true);

        if (ok is false || processes is null)
        {
            return;
        }

        Merge(processes);

        UpdatedAt = DateTime.UtcNow;
        ApplyFilter();
        RefreshSummary();
    }

    private void Merge(IReadOnlyList<ProcessInfoModel> processes)
    {
        var byId = _all.ToDictionary(item => item.Id);
        var alive = new HashSet<int>();

        foreach (var process in processes)
        {
            alive.Add(process.Id);

            if (byId.TryGetValue(process.Id, out var existing))
            {
                existing.Update(process);
                continue;
            }

            _all.Add(new ProcessItemViewModel(process, Localizer));
        }

        for (var index = _all.Count - 1; index >= 0; index--)
        {
            if (alive.Contains(_all[index].Id) is false)
            {
                _all.RemoveAt(index);
            }
        }
    }

    private void ApplyFilter()
    {
        var query = FilterText?.Trim() ?? string.Empty;
        var selectedId = SelectedProcess?.Id;

        var filtered = _all
            .Where(item => BusyOnly is false || item.HasCpuActivity || item.MemoryBytes > 64L * 1024 * 1024)
            .Where(item => query.Length == 0 ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Company.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.MemoryBytes)
            .ToList();

        for (var index = 0; index < filtered.Count; index++)
        {
            var item = filtered[index];

            if (index < Items.Count && ReferenceEquals(Items[index], item))
            {
                continue;
            }

            var existing = Items.IndexOf(item);
            if (existing >= 0)
            {
                Items.Move(existing, index);
                continue;
            }

            Items.Insert(index, item);
        }

        while (Items.Count > filtered.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        if (selectedId is not null && Items.FirstOrDefault(item => item.Id == selectedId) is ProcessItemViewModel match)
        {
            SelectedProcess = match;
        }
        else if (SelectedProcess is not null && Items.Contains(SelectedProcess) is false)
        {
            SelectedProcess = null;
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(ShowElevationNote));
    }

    private void RefreshSummary()
    {
        ProcessCount = Items.Count;
        TotalMemoryText = Humanize.Bytes(Items.Sum(item => item.MemoryBytes));
        TotalCpuText = Humanize.Percent(Items.Sum(item => item.CpuPercent), 1);

        OnPropertyChanged(nameof(ProcessCountText));
        OnPropertyChanged(nameof(UpdatedText));
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();

        if (AutoRefresh is false)
        {
            return;
        }

        _refreshLoop = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
        _ = LoopAsync(_refreshLoop.Token);
    }

    private void StopAutoRefresh()
    {
        var loop = _refreshLoop;
        _refreshLoop = null;

        try
        {
            loop?.Cancel();
            loop?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Já liberado.
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (token.IsCancellationRequested is false)
            {
                await Task.Delay(RefreshInterval, token).ConfigureAwait(true);

                while (IsBusy && token.IsCancellationRequested is false)
                {
                    await Task.Delay(250, token).ConfigureAwait(true);
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await LoadAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Tela desativada ou aplicativo encerrado.
        }
        catch (Exception exception)
        {
            Logger.Warning("Processes", "Falha na atualizacao periodica da lista de processos.", exception);
        }
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(ProcessCountText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(SafetyNote));
    }
}

/// <summary>Opcao de prioridade exibida na tela de processos.</summary>
public sealed class PriorityOption
{
    private readonly ILocalizer _localizer;

    public PriorityOption(ProcessPriorityHint hint, string titleKey, ILocalizer localizer)
    {
        Hint = hint;
        TitleKey = titleKey;
        _localizer = localizer;
    }

    public ProcessPriorityHint Hint { get; }

    public string TitleKey { get; }

    public string Text => _localizer[TitleKey];
}
