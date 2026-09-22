using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>Linha de rotulo e valor usada nos resumos de deteccao.</summary>
public sealed class FactItemViewModel : ItemViewModelBase
{
    private readonly string _labelKey;

    public FactItemViewModel(string labelKey, string? value, ILocalizer localizer)
        : base(localizer)
    {
        _labelKey = labelKey;
        Value = value ?? string.Empty;
    }

    public string Label => Localizer[_labelKey];

    public string Value { get; }

    public bool HasValue => Value.Length > 0;
}
