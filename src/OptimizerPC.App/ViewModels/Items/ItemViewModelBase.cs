using CommunityToolkit.Mvvm.ComponentModel;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.ViewModels.Items;

/// <summary>
/// Base dos itens exibidos nas listas. Os textos sao resolvidos sob demanda, o
/// que permite atualizar tudo de uma vez quando o idioma muda.
/// </summary>
public abstract class ItemViewModelBase : ObservableObject
{
    protected ItemViewModelBase(ILocalizer localizer) => Localizer = localizer;

    protected ILocalizer Localizer { get; }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
