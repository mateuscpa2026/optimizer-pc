using System.Globalization;
using System.Text.Json;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Localization;

/// <summary>
/// Textos da interface carregados de catalogos JSON embutidos no proprio executavel.
/// Nada e baixado da internet: o aplicativo funciona por completo sem conexao.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    private const string ResourcePrefix = "OptimizerPC.Services.Localization.loc.";

    private readonly IAppLogger _logger;
    private readonly Dictionary<AppLanguage, Dictionary<string, string>> _catalogs = new();
    private AppLanguage _current = AppLanguage.PtBr;

    public JsonLocalizer(IAppLogger logger)
    {
        _logger = logger;
        LoadCatalogs();
    }

    public AppLanguage Current => _current;

    public IReadOnlyList<AppLanguage> Available { get; } = new[] { AppLanguage.PtBr, AppLanguage.EnUs, AppLanguage.Es };

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
        if (_catalogs.TryGetValue(_current, out var catalog) && catalog.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_current == language)
        {
            return;
        }

        _current = language;
        _logger.Info("Localization", "Idioma da interface alterado para " + language + ".");
        LanguageChanged?.Invoke(this, language);
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (TryGet(key, out var value))
        {
            return value;
        }

        // Portugues como reserva: nenhuma tela deve ficar sem texto, mesmo em traducao incompleta.
        if (_catalogs.TryGetValue(AppLanguage.PtBr, out var fallback) && fallback.TryGetValue(key, out var portuguese))
        {
            return portuguese;
        }

        return key;
    }

    private void LoadCatalogs()
    {
        var assembly = typeof(JsonLocalizer).Assembly;

        foreach (var language in Available)
        {
            var resourceName = ResourcePrefix + Code(language) + ".json";

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    _logger.Warning("Localization", "Catalogo de textos ausente: " + resourceName);
                    continue;
                }

                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                _catalogs[language] = entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.Error("Localization", "Falha ao carregar o catalogo de textos " + resourceName + ".", ex);
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
