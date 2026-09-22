using System.IO;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Linha do historico de acoes executadas.</summary>
public sealed class HistoryItemViewModel : ItemViewModelBase
{
    public HistoryItemViewModel(HistoryEntry model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public HistoryEntry Model { get; }

    public long Id => Model.Id;

    public string TimestampText => Model.TimestampText;

    public string Category => Text(Model.Category);

    public string Action => Text(Model.Action);

    public string Description => Model.Description;

    public bool Success => Model.Success;

    public string StatusText => Localizer[Model.Success ? "History.Result.Success" : "History.Result.Failure"];

    public long BytesFreed => Model.BytesFreed;

    public bool HasBytesFreed => Model.BytesFreed > 0;

    public string BytesFreedText => Model.BytesFreedText;

    public string? Details => string.IsNullOrWhiteSpace(Model.Details) ? null : Model.Details;

    public bool HasDetails => Details is not null;

    public string? RestoreRecordKey => Model.RestoreRecordKey;

    public bool HasRestoreRecord => string.IsNullOrWhiteSpace(Model.RestoreRecordKey) is false;

    private string Text(string keyOrText) => string.IsNullOrWhiteSpace(keyOrText) ? string.Empty : Localizer[keyOrText];
}

/// <summary>Relatorio gerado e salvo localmente.</summary>
public sealed class ReportItemViewModel : ItemViewModelBase
{
    public ReportItemViewModel(ReportRecord model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public ReportRecord Model { get; }

    public long Id => Model.Id;

    public string Title => Model.Title;

    public ReportFormat Format => Model.Format;

    public string FormatText => Localizer["Report.Format." + Model.Format];

    public string FilePath => Model.FilePath;

    public string FileName => Path.GetFileName(Model.FilePath);

    public string CreatedText => Model.CreatedText;

    public long SizeBytes => Model.SizeBytes;

    public string SizeText => Model.SizeText;

    public string? Summary => string.IsNullOrWhiteSpace(Model.Summary) ? null : Model.Summary;

    public bool HasSummary => Summary is not null;
}

/// <summary>Registro de reversao criado antes de uma alteracao.</summary>
public sealed class RestoreRecordItemViewModel : ItemViewModelBase
{
    public RestoreRecordItemViewModel(RestoreRecord model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public RestoreRecord Model { get; }

    public long Id => Model.Id;

    public string Key => Model.Key;

    public RestoreRecordKind Kind => Model.Kind;

    public string KindText => Localizer["Restore.Kind." + Model.Kind];

    public string Title => string.IsNullOrWhiteSpace(Model.TitleKey) ? KindText : Text(Model.TitleKey);

    public string Description => Model.Description;

    public bool HasDescription => string.IsNullOrWhiteSpace(Model.Description) is false;

    public string CreatedText => Model.CreatedText;

    public UndoState UndoState => Model.UndoState;

    public string UndoStateText => Localizer["Restore.UndoState." + Model.UndoState];

    public bool CanRestore => Model.CanRestore;

    public string? SourceAction => string.IsNullOrWhiteSpace(Model.SourceAction) ? null : Model.SourceAction;

    public string? TargetPath => string.IsNullOrWhiteSpace(Model.TargetPath) ? null : Model.TargetPath;

    public bool HasTargetPath => TargetPath is not null;

    public string? RestoreMessage => string.IsNullOrWhiteSpace(Model.RestoreMessage) ? null : Model.RestoreMessage;

    public bool HasRestoreMessage => RestoreMessage is not null;

    private string Text(string keyOrText) => Localizer[keyOrText];
}

/// <summary>Ponto de restauracao do Windows.</summary>
public sealed class RestorePointItemViewModel : ItemViewModelBase
{
    public RestorePointItemViewModel(RestorePointInfo model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public RestorePointInfo Model { get; }

    public int SequenceNumber => Model.SequenceNumber;

    public string Title => string.IsNullOrWhiteSpace(Model.Description)
        ? Localizer["Restore.Point.NoDescription"]
        : Model.Description;

    public string CreatedText => Model.CreatedText;

    public string TypeText => string.IsNullOrWhiteSpace(Model.Type)
        ? Localizer["Common.NotAvailable"]
        : Model.Type;

    public bool HasType => string.IsNullOrWhiteSpace(Model.Type) is false;

    public bool IsSystemRestorePoint => Model.IsSystemRestorePoint;
}

/// <summary>Linha de log local exibida nas configuracoes.</summary>
public sealed class LogItemViewModel : ItemViewModelBase
{
    public LogItemViewModel(LogRecord model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public LogRecord Model { get; }

    public long Id => Model.Id;

    public string TimestampText => Model.TimestampText;

    public LogLevel Level => Model.Level;

    public string LevelText => Model.LevelText;

    public Severity Severity => Model.Level switch
    {
        LogLevel.Debug => Severity.Info,
        LogLevel.Info => Severity.Ok,
        LogLevel.Warning => Severity.Warning,
        _ => Severity.Critical
    };

    public string Category => Model.Category;

    public string Message => Model.Message;

    public string? Exception => string.IsNullOrWhiteSpace(Model.Exception) ? null : Model.Exception;

    public bool HasException => Exception is not null;
}

/// <summary>Tipo de relatorio disponivel para geracao.</summary>
public sealed class ReportFormatItemViewModel : ItemViewModelBase
{
    public ReportFormatItemViewModel(ReportFormat format, ILocalizer localizer)
        : base(localizer) => Format = format;

    public ReportFormat Format { get; }

    public string Title => Localizer["Report.Format." + Format];

    public string Description => Localizer["Report.Format." + Format + ".Detail"];
}
