using System.Windows;
using Microsoft.Win32;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.App.Services;

public interface IThemeService
{
    ThemeMode Current { get; }

    /// <summary>Tema realmente aplicado agora (o modo Sistema e resolvido para claro ou escuro).</summary>
    bool IsDark { get; }

    event EventHandler? ThemeChanged;

    void Apply(ThemeMode mode);
}

/// <summary>
/// Troca o dicionario de cores da aplicacao. Os controles usam DynamicResource,
/// entao basta substituir o primeiro dicionario mesclado.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string DarkPath = "pack://application:,,,/OptimizerPC;component/Themes/Dark.xaml";
    private const string LightPath = "pack://application:,,,/OptimizerPC;component/Themes/Light.xaml";

    private readonly IRegistryService _registry;
    private readonly IAppLogger _logger;
    private bool _listening;

    public ThemeService(IRegistryService registry, IAppLogger logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public ThemeMode Current { get; private set; } = ThemeMode.Dark;

    public bool IsDark { get; private set; } = true;

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeMode mode)
    {
        Current = mode;
        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.System => IsSystemUsingLightTheme(),
            _ => true
        };

        IsDark = dark is false;

        var application = Application.Current;
        if (application is not null)
        {
            var source = new Uri(dark ? DarkPath : LightPath, UriKind.Absolute);
            var dictionaries = application.Resources.MergedDictionaries;

            ResourceDictionary? previous = null;
            foreach (var dictionary in dictionaries)
            {
                var current = dictionary.Source?.OriginalString ?? string.Empty;
                if (current.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    previous = dictionary;
                    break;
                }
            }

            var replacement = new ResourceDictionary { Source = source };
            if (previous is null)
            {
                dictionaries.Insert(0, replacement);
            }
            else
            {
                var index = dictionaries.IndexOf(previous);
                dictionaries[index] = replacement;
            }
        }

        ListenToSystemChanges(mode == ThemeMode.System);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsSystemUsingLightTheme()
    {
        try
        {
            var value = _registry.GetValue(
                RegistryHiveKind.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme");

            return value?.IntValue == 1;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(ThemeService), "Nao foi possivel ler o tema do Windows; usando o tema escuro.", ex);
            return false;
        }
    }

    private void ListenToSystemChanges(bool enabled)
    {
        if (enabled == _listening)
        {
            return;
        }

        try
        {
            if (enabled)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            else
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }

            _listening = enabled;
        }
        catch (Exception ex)
        {
            _listening = false;
            _logger.Warning(nameof(ThemeService), "Nao foi possivel acompanhar o tema do Windows.", ex);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Current is not ThemeMode.System)
        {
            return;
        }

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.Dispatcher.BeginInvoke(new Action(() => Apply(ThemeMode.System)));
    }
}
