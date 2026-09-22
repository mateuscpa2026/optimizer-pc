using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Local de limpeza com tamanho estimado e selecao do usuario.</summary>
public sealed class CleanupTargetItemViewModel : ItemViewModelBase
{
    public CleanupTargetItemViewModel(CleanupTarget model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public CleanupTarget Model { get; }

    public string Id => Model.Id;

    public CleanupCategory Category => Model.Category;

    public string Title => Localizer[Model.TitleKey];

    public string Description => Localizer[Model.DescriptionKey];

    public long TotalBytes => Model.TotalBytes;

    public string SizeText => Model.SizeText;

    public long FileCount => Model.FileCount;

    public string FilesText => Localizer.Format("Cleanup.Items.Count", Model.FileCount, Model.DirectoryCount);

    public RiskLevel Risk => Model.Risk;

    public string RiskText => Localizer["Risk." + Model.Risk];

    public bool RequiresElevation => Model.Elevation is ElevationRequirement.Required;

    public bool RequiresConfirmation => Model.RequiresConfirmation;

    public bool IsAvailable => Model.IsAvailable;

    public string? UnavailableReason => string.IsNullOrWhiteSpace(Model.UnavailableReasonKey)
        ? null
        : Localizer[Model.UnavailableReasonKey!];

    public bool HasUnavailableReason => UnavailableReason is not null;

    public IReadOnlyList<string> Paths => Model.Paths;

    public string PathsText => Model.Paths.Count == 0 ? string.Empty : string.Join(Environment.NewLine, Model.Paths);

    public bool IsSelected
    {
        get => Model.IsSelected && Model.IsAvailable;
        set
        {
            if (Model.IsSelected == value)
            {
                return;
            }

            Model.IsSelected = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>Resultado da limpeza de uma categoria.</summary>
public sealed class CleanupResultItemViewModel : ItemViewModelBase
{
    public CleanupResultItemViewModel(CleanupCategoryResult model, string titleKey, ILocalizer localizer)
        : base(localizer)
    {
        Model = model;
        TitleKey = titleKey;
    }

    public CleanupCategoryResult Model { get; }

    public string TitleKey { get; }

    public string Title => Localizer[TitleKey];

    public string Category => Model.Category.ToString();

    public bool Succeeded => Model.Succeeded;

    public string StatusText => Model.Succeeded ? Localizer["Cleanup.Result.Cleaned"] : Localizer["Cleanup.Result.Partial"];

    public long DeletedBytes => Model.DeletedBytes;

    public string DeletedText => Model.DeletedText;

    public string FilesText => Localizer.Format("Cleanup.Result.Files", Model.DeletedFiles, Model.SkippedFiles);

    public string Message => Localizer[Model.MessageKey];

    public bool HasMessage => string.IsNullOrWhiteSpace(Model.MessageKey) is false;

    public string DurationText => Humanize.Duration(Model.Duration, Localizer["Common.Duration.SubSecond"]);
}
