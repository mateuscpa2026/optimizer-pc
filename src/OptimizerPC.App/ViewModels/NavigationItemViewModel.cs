using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Item da navegacao lateral. O titulo vem do catalogo de idioma e e revalidado
/// quando o idioma muda.
/// </summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly Action<Screen> _select;
    private bool _isSelected;
    private int _badge;

    public NavigationItemViewModel(NavigationEntry entry, ILocalizer localizer, Action<Screen> select)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _select = select ?? throw new ArgumentNullException(nameof(select));
    }

    public NavigationEntry Entry { get; }

    public Screen Screen => Entry.Screen;

    public string IconKey => Entry.IconKey;

    public string Title => _localizer[Entry.TitleKey];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Contador exibido ao lado do item (usado quando ha pendencias).</summary>
    public int Badge
    {
        get => _badge;
        set
        {
            if (SetProperty(ref _badge, value))
            {
                OnPropertyChanged(nameof(HasBadge));
            }
        }
    }

    public bool HasBadge => _badge > 0;

    [RelayCommand]
    private void Select() => _select(Screen);

    public void RefreshTexts() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// Agrupamento visual da navegacao lateral.
/// </summary>
public sealed class NavigationSectionViewModel
{
    private readonly ILocalizer _localizer;

    public NavigationSectionViewModel(string titleKey, IReadOnlyList<NavigationItemViewModel> items, ILocalizer localizer)
    {
        TitleKey = titleKey;
        Items = items;
        _localizer = localizer;
    }

    public string TitleKey { get; }

    public IReadOnlyList<NavigationItemViewModel> Items { get; }

    public string Title => _localizer[TitleKey];
}
