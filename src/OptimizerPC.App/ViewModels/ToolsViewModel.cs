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
/// Ferramentas nativas do Windows. As opcoes exibidas vem da lista branca do
/// aplicativo e os argumentos sao revalidados antes de cada execucao. Nenhum comando
/// roda sem confirmacao e a saida completa e mostrada apos o termino.
/// </summary>
public sealed partial class ToolsViewModel : ViewModelBase
{
    private readonly IWindowsToolsService _tools;
    private readonly IElevationService _elevation;
    private readonly IDialogService _dialogs;
    private readonly ToolLaunchState _launch;

    public ToolsViewModel(
        IWindowsToolsService tools,
        IElevationService elevation,
        IDialogService dialogs,
        ToolLaunchState launch,
        ILocalizer localizer,
        IAppLogger logger)
        : base(localizer, logger)
    {
        _tools = tools;
        _elevation = elevation;
        _dialogs = dialogs;
        _launch = launch;

        _isElevated = elevation.IsElevated;

        // IsBusy e uma propriedade manual da base: o botao depende dela.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanRun));
            }
        };
    }

    public ObservableCollection<WindowsToolItemViewModel> Tools { get; } = new();

    public ObservableCollection<string> Output { get; } = new();

    [ObservableProperty]
    private bool _hasTools;

    [ObservableProperty]
    private WindowsToolItemViewModel? _selectedTool;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _hasOutput;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private Severity _resultSeverity = Severity.Ok;

    public string Subtitle => Localizer["Tools.Subtitle"];

    public string OptionsTitle => Localizer["Tools.Options.Title"];

    public string OptionsEmptyText => Localizer["Tools.Options.Empty"];

    public string DetailsTitle => Localizer["Tools.Details.Title"];

    public string DetailsEmptyText => Localizer["Tools.Details.Select"];

    public string ArgumentsNote => Localizer["Tools.Arguments.Note"];

    public string OutputTitle => Localizer["Tools.Output.Title"];

    public string OutputEmptyText => Localizer["Tools.Output.Empty"];

    public string Disclaimer => Localizer["Tools.Disclaimer"];

    public string ElevationNote => Localizer["Tools.Elevation.Note"];

    public string RestartElevatedText => Localizer["Tools.Elevation.Restart"];

    public bool HasSelection => SelectedTool is not null;

    public bool ShowElevationNote => IsElevated is false;

    public bool CanRun =>
        SelectedTool is { } tool &&
        IsBusy is false &&
        (tool.RequiresElevation is false || IsElevated);

    public string ElevationHint => SelectedTool?.RequiresElevation is true && IsElevated is false
        ? Localizer["Tools.Elevation.Required"]
        : string.Empty;

    public bool HasElevationHint => ElevationHint.Length > 0;

    protected override async Task OnNavigatedToAsync() => await RefreshAsync().ConfigureAwait(true);

    protected override void OnLanguageChanged() => RefreshDerived();

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task RunAsync()
    {
        var tool = SelectedTool;
        if (tool is null || CanRun is false)
        {
            return;
        }

        var confirmed = await _dialogs
            .ConfirmActionAsync(
                "Tools.Run.Title",
                "Tools.Run.Confirm",
                "Tools.Run.Action",
                BuildConfirmDetail(tool),
                Views.DialogKind.Warning)
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        Output.Clear();
        Output.Add("> " + tool.Command + " " + tool.ArgumentsText);
        HasOutput = true;
        HasResult = false;

        var progress = new Progress<string>(line => Output.Add(line));

        var (ok, result) = await RunAsync<CommandResult>(
            token => _tools.RunAsync(tool.Id, null, progress, token),
            "Tools.Running").ConfigureAwait(true);

        if (ok is false || result is null)
        {
            SetStatus("Tools.Run.Failed", Severity.Critical);
            HasResult = true;
            ResultSeverity = Severity.Critical;
            ResultText = Localizer["Error.Unexpected"];
            return;
        }

        AppendResult(result);

        if (result.Started && result.ExitCode == 0)
        {
            SetStatus("Tools.Run.Completed", Severity.Ok);
            ResultSeverity = Severity.Ok;
            return;
        }

        var messageKey = string.IsNullOrWhiteSpace(result.MessageKey) ? "Tools.Run.Finished" : result.MessageKey;
        SetStatus(messageKey, Severity.Warning);
        ResultSeverity = Severity.Warning;
    }

    [RelayCommand]
    private void ClearOutput()
    {
        Output.Clear();
        HasOutput = false;
        HasResult = false;
        ResultText = string.Empty;
        ClearStatus();
    }

    [RelayCommand]
    private async Task CopyOutputAsync()
    {
        if (Output.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, Output));
            SetStatus("Tools.Output.Copied", Severity.Ok);
        }
        catch (Exception exception)
        {
            Logger.Warning("Tools", "Nao foi possivel copiar a saida: " + exception.Message);
            await _dialogs.ShowWarningAsync("Tools.Output.Title", "Tools.Output.Copy.Failed").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RestartElevatedAsync()
    {
        var confirmed = await _dialogs
            .ConfirmAsync("Tools.Elevation.Title", "Tools.Elevation.Confirm")
            .ConfigureAwait(true);

        if (confirmed is false)
        {
            return;
        }

        var started = await _elevation.RestartElevatedAsync("Tools.Elevation.Note").ConfigureAwait(true);
        if (started is false)
        {
            await _dialogs.ShowWarningAsync("Tools.Elevation.Title", "Tools.Elevation.Cancelled").ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        var (ok, descriptors) = await RunAsync<IReadOnlyList<WindowsToolDescriptor>>(
            token => Task.Run(() => _tools.GetTools(), token),
            "Tools.Loading").ConfigureAwait(true);

        if (ok is false || descriptors is null)
        {
            return;
        }

        var pending = _launch.Consume();
        var previous = SelectedTool?.Id ?? pending;

        Tools.Clear();
        foreach (var descriptor in descriptors)
        {
            Tools.Add(new WindowsToolItemViewModel(descriptor, Localizer));
        }

        HasTools = Tools.Count > 0;

        SelectedTool = Tools.FirstOrDefault(tool => string.Equals(tool.Id, previous, StringComparison.OrdinalIgnoreCase))
            ?? Tools.FirstOrDefault();

        IsElevated = _elevation.IsElevated;
        RefreshDerived();
    }

    private void AppendResult(CommandResult result)
    {
        HasResult = true;

        if (result.Started is false)
        {
            ResultText = Localizer[string.IsNullOrWhiteSpace(result.MessageKey) ? "Tools.Run.NotStarted" : result.MessageKey];
            AppendDetail(result.StandardError);
            return;
        }

        ResultText = Localizer.Format("Tools.Run.ExitCode", result.ExitCode);
        AppendDetail(result.StandardError);
    }

    private void AppendDetail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r', ' ', '\t');
            if (trimmed.Length > 0)
            {
                Output.Add(trimmed);
            }
        }

        HasOutput = true;
    }

    private string BuildConfirmDetail(WindowsToolItemViewModel tool)
    {
        var lines = new List<string>
        {
            tool.Command + " " + tool.ArgumentsText,
            tool.Description
        };

        if (tool.HasNote)
        {
            lines.Add(tool.Note!);
        }

        if (tool.RequiresElevation)
        {
            lines.Add(Localizer["Tools.Elevation.Required"]);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OptionsTitle));
        OnPropertyChanged(nameof(OptionsEmptyText));
        OnPropertyChanged(nameof(DetailsTitle));
        OnPropertyChanged(nameof(DetailsEmptyText));
        OnPropertyChanged(nameof(ArgumentsNote));
        OnPropertyChanged(nameof(OutputTitle));
        OnPropertyChanged(nameof(OutputEmptyText));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(RestartElevatedText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ElevationHint));
        OnPropertyChanged(nameof(HasElevationHint));
    }

    partial void OnSelectedToolChanged(WindowsToolItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ElevationHint));
        OnPropertyChanged(nameof(HasElevationHint));
    }

    partial void OnIsElevatedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowElevationNote));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ElevationHint));
        OnPropertyChanged(nameof(HasElevationHint));
    }
}
