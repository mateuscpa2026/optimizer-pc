using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Fator que compoe o indice de saude do PC.</summary>
public sealed class ScoreFactorItemViewModel : ItemViewModelBase
{
    public ScoreFactorItemViewModel(ScoreFactor model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public ScoreFactor Model { get; }

    public string Title => Localizer[Model.TitleKey];

    public string Detail => string.IsNullOrWhiteSpace(Model.DetailValue) ? Localizer[Model.DetailKey] : Model.DetailValue!;

    public Severity Severity => Model.Severity;

    public string SeverityText => Localizer["Severity." + Model.Severity];

    public string WeightText => Humanize.Percent(Model.Weight * 100d);

    public string ScoreText => Model.ScoreText;

    public bool HasLoss => Model.PointsLost >= 0.5;

    public string LossText => HasLoss
        ? Localizer.Format("Score.PointsLost", Math.Round(Model.PointsLost))
        : Localizer["Score.NoLoss"];
}

/// <summary>Verificacao individual do diagnostico.</summary>
public sealed class DiagnosticCheckItemViewModel : ItemViewModelBase
{
    public DiagnosticCheckItemViewModel(DiagnosticCheck model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public DiagnosticCheck Model { get; }

    public string Title => Localizer[Model.TitleKey];

    public string Description => Localizer[Model.DescriptionKey];

    public string CategoryText => Localizer[Model.CategoryKey];

    public Severity Severity => Model.Severity;

    public string StatusText => Model.Succeeded is false
        ? Localizer["Diagnose.Status.Failed"]
        : Localizer["Severity." + Model.Severity];

    public bool HasProblem => Model.HasProblem;

    public string? Detail => string.IsNullOrWhiteSpace(Model.Detail) ? null : Model.Detail;

    public bool HasDetail => Detail is not null;

    public string Advice => Localizer[Model.AdviceKey];

    public bool HasAdvice => Model.HasProblem && string.IsNullOrWhiteSpace(Model.AdviceKey) is false;

    public string? ErrorDetail => string.IsNullOrWhiteSpace(Model.ErrorDetail) ? null : Model.ErrorDetail;

    public bool HasErrorDetail => ErrorDetail is not null;

    public bool RequiresElevation => Model.Elevation is ElevationRequirement.Required;

    public string? RelatedToolId => Model.RelatedToolId;

    public bool CanOpenTool => string.IsNullOrWhiteSpace(Model.RelatedToolId) is false;

    /// <summary>Ha algo a revelar: detalhe, erro, conselho ou ferramenta relacionada.</summary>
    public bool HasDetails => HasDetail || HasErrorDetail || HasAdvice || CanOpenTool;

    private bool _isExpanded;

    /// <summary>Detalhe tecnico e conselho visiveis somente quando o usuario pede.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>Agrupamento das verificacoes por categoria.</summary>
public sealed class DiagnosticCategoryGroupViewModel : ItemViewModelBase
{
    private readonly string _titleKey;

    public DiagnosticCategoryGroupViewModel(
        string titleKey,
        IReadOnlyList<DiagnosticCheckItemViewModel> items,
        ILocalizer localizer)
        : base(localizer)
    {
        _titleKey = titleKey;
        Items = items;
    }

    public string Title => Localizer[_titleKey];

    public IReadOnlyList<DiagnosticCheckItemViewModel> Items { get; }

    public bool HasProblems => Items.Any(item => item.HasProblem);

    public string CountText => Localizer.Format("Diagnose.Group.Count", Items.Count);
}

/// <summary>Recomendacao produzida pela analise local. Nada e executado por aqui.</summary>
public sealed class RecommendationItemViewModel : ItemViewModelBase
{
    public RecommendationItemViewModel(Recommendation model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public Recommendation Model { get; }

    public string Title => Localizer[Model.TitleKey];

    public string Description => Localizer[Model.DescriptionKey];

    public string Reason => Localizer[Model.ReasonKey];

    public string? Detail => string.IsNullOrWhiteSpace(Model.Detail) ? null : Model.Detail;

    public bool HasDetail => Detail is not null;

    public ImpactLevel Impact => Model.Impact;

    public string ImpactText => Localizer["Impact." + Model.Impact];

    public RiskLevel Risk => Model.Risk;

    public string RiskText => Localizer["Risk." + Model.Risk];

    public bool HasGain => Model.EstimatedGainBytes > 0;

    public string GainText => HasGain ? Humanize.Bytes(Model.EstimatedGainBytes) : string.Empty;

    public bool RequiresElevation => Model.Elevation is ElevationRequirement.Required;

    public bool CanExecute => Model.CanExecute;

    public string? ActionId => Model.ActionId;

    public ActionState State => Model.State;

    public string StateText => Localizer["Action.State." + Model.State];
}

/// <summary>Acao concreta de otimizacao, com selecao e estado de execucao.</summary>
public sealed class OptimizationActionItemViewModel : ItemViewModelBase
{
    public OptimizationActionItemViewModel(OptimizationAction model, ILocalizer localizer)
        : base(localizer) => Model = model;

    public OptimizationAction Model { get; }

    public string Id => Model.Id;

    public OptimizationActionKind Kind => Model.Kind;

    public string Title => Localizer[Model.TitleKey];

    public string Description => Localizer[Model.DescriptionKey];

    public ImpactLevel Impact => Model.Impact;

    public string ImpactText => Localizer["Impact." + Model.Impact];

    public RiskLevel Risk => Model.Risk;

    public string RiskText => Localizer["Risk." + Model.Risk];

    public bool RequiresElevation => Model.Elevation is ElevationRequirement.Required;

    public bool CreatesRestoreRecord => Model.CreatesRestoreRecord;

    public bool HasEstimate => Model.EstimatedBytes > 0;

    public string EstimateText => HasEstimate ? Humanize.Bytes(Model.EstimatedBytes) : string.Empty;

    public bool IsBlocked => Model.IsBlocked;

    public string? BlockedReason => Model.IsBlocked && string.IsNullOrWhiteSpace(Model.BlockedReasonKey) is false
        ? Localizer[Model.BlockedReasonKey!]
        : null;

    public ActionState State => Model.State;

    public string StateText => Localizer["Action.State." + Model.State];

    public bool IsSettled => Model.State is ActionState.Succeeded or ActionState.Skipped or ActionState.Failed or ActionState.Ignored;

    public bool IsSelected
    {
        get => Model.IsSelected && Model.IsBlocked is false;
        set
        {
            if (Model.IsBlocked || Model.IsSelected == value)
            {
                return;
            }

            Model.IsSelected = value;
            OnPropertyChanged();
        }
    }
}
