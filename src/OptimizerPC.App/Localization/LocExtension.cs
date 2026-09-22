using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace OptimizerPC.App.Localization;

/// <summary>
/// Uso em XAML: Text="{loc:Loc Nav.Dashboard}". Retorna uma ligacao viva ao
/// <see cref="LocalizationProxy"/>, portanto o texto acompanha a troca de idioma.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Mode = BindingMode.OneWay,
            Source = LocalizationProxy.Current,
            Path = new PropertyPath("[" + Key + "]")
        };

        return binding.ProvideValue(serviceProvider);
    }
}
