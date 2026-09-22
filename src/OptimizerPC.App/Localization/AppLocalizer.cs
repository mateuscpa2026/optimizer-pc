using System.Globalization;
using System.Text.Json;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.Localization;

namespace OptimizerPC.App.Localization;

/// <summary>
/// Textos da camada visual. Consulta primeiro o catalogo do aplicativo e delega
/// ao catalogo dos servicos quando a chave nao existe aqui.
/// </summary>
public sealed class AppLocalizer : ILocalizer
{
    private const string ResourcePrefix = "OptimizerPC.App.Localization.loc.";

    private readonly JsonLocalizer _services;
    private readonly IAppLogger _logger;
    private readonly Dictionary<AppLanguage, Dictionary<string, string>> _catalogs = new();

    public AppLocalizer(JsonLocalizer services, IAppLogger logger)
    {
        _services = services;
        _logger = logger;
        LoadCatalogs();
        _services.LanguageChanged += OnInnerLanguageChanged;
    }

    public AppLanguage Current => _services.Current;

    public IReadOnlyList<AppLanguage> Available => _services.Available;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public string this[string key] => Resolve(key);

    public string Format(string key, params object[] args)
    {
        var template = Resolve(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public bool TryGet(string key, out string value)
    {
        if (_catalogs.TryGetValue(Current, out var catalog) && catalog.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        return _services.TryGet(key, out value);
    }

    public void SetLanguage(AppLanguage language) => _services.SetLanguage(language);

    private void OnInnerLanguageChanged(object? sender, AppLanguage language) =>
        LanguageChanged?.Invoke(this, language);

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_catalogs.TryGetValue(Current, out var catalog) && catalog.TryGetValue(key, out var value))
        {
            return value;
        }

        // Portugues como reserva antes de consultar os servicos.
        if (_catalogs.TryGetValue(AppLanguage.PtBr, out var fallback) && fallback.TryGetValue(key, out var portuguese))
        {
            return portuguese;
        }

        return _services[key];
    }

    private void LoadCatalogs()
    {
        var assembly = typeof(AppLocalizer).Assembly;

        foreach (var language in Available)
        {
            var resourceName = ResourcePrefix + Code(language) + ".json";

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    _logger.Warning("Localization", "Catalogo de interface ausente: " + resourceName);
                    continue;
                }

                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                _catalogs[language] = entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.Error("Localization", "Falha ao carregar o catalogo de interface " + resourceName + ".", ex);
            }
        }
    }

    private static string Code(AppLanguage language) => language switch
    {
        AppLanguage.EnUs => "en-US",
        AppLanguage.Es => "es",
        _ => "pt-BR"
    };
}
