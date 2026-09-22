using System.ComponentModel;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.App.Localization;

/// <summary>
/// Ponte observavel entre o localizador e o XAML. Permite trocar o idioma em tempo
/// de execucao: ao mudar a lingua, todas as ligacoes de texto sao atualizadas.
/// </summary>
public sealed class LocalizationProxy : INotifyPropertyChanged
{
    public static LocalizationProxy Current { get; } = new();

    private ILocalizer? _localizer;

    private LocalizationProxy()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] =>
        _localizer is null || string.IsNullOrEmpty(key) ? string.Empty : _localizer[key];

    public void Attach(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        if (_localizer is not null)
        {
            _localizer.LanguageChanged -= OnLanguageChanged;
        }

        _localizer = localizer;
        localizer.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged(this, localizer.Current);
    }

    private void OnLanguageChanged(object? sender, AppLanguage language) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
