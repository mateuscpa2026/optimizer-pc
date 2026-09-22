using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizerPC.Core;
using OptimizerPC.Services.Localization;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Localization;

/// <summary>
/// Os textos da interface vivem em dois pares de catalogos: um para a camada de servicos
/// e outro para a interface WPF. Nenhuma tela pode ficar sem texto em nenhum idioma.
/// </summary>
public sealed class LocalizationCoverageTests
{
    private const string ServicesFolder = "src/OptimizerPC.Services/Localization";
    private const string AppFolder = "src/OptimizerPC.App/Localization";

    private static readonly string[] Languages = { "pt-BR", "en-US", "es" };

    /// <summary>Chaves que existem nas duas camadas e precisam manter o mesmo texto.</summary>
    private static readonly string[] SharedKeysToCompare = { "App.Title", "App.Slogan", "Common.Yes", "Common.No", "Common.Ok", "Common.Cancel", "Common.Close", "Common.Save", "Common.Apply", "Common.Refresh", "Common.Loading", "Common.None" };

    /// <summary>Formas sem acento de palavras que, em portugues, sempre levam acento.</summary>
    private static readonly string[] UnaccentedPortuguese =
    {
        "nao", "sao", "voce", "tambem", "apos", "ate", "configuracoes", "configuracao", "historico",
        "historicos", "relatorio", "relatorios", "memoria", "usuario", "inicializacao", "otimizacao",
        "diagnostico", "analise", "referencia", "numero", "ultimo", "proximo", "disponivel", "critico",
        "automatico", "versao", "versoes", "funcao", "funcoes", "acao", "acoes", "opcao", "opcoes",
        "informacao", "selecao", "permissao", "decisao", "conexao", "atencao", "area", "tres", "saude",
        "espaco", "inicio", "padrao", "graficos", "estatisticas", "midia", "seguranca", "alteracao",
        "instalacao", "aplicacao", "operacao", "elevacao", "avaliacao", "conteudo", "sugestoes",
        "necessario", "rapido", "possivel", "facil", "util", "unico", "publico", "especifico", "minimo",
        "maximo", "basico", "metricas", "logico", "nivel", "temporarios", "privilegio", "sugestao",
        "visualizacao", "personalizacao", "restauracao", "confirmacao", "exibicao", "duracao", "precisao",
        "utilizacao", "execucao", "gravacao"
    };

    /// <summary>Formas sem acento de palavras que, em espanhol, sempre levam acento.</summary>
    private static readonly string[] UnaccentedSpanish =
    {
        "configuracion", "informacion", "analisis", "tambien", "rapido", "seleccion", "funcion", "opcion",
        "version", "atencion", "accion", "numero", "diagnostico", "optimizacion", "estadisticas",
        "automatico", "critico", "aplicacion", "instalacion", "liberacion", "actualizacion", "descripcion",
        "sesion", "revision", "decision", "conexion", "verificacion", "proteccion", "energia", "maquina",
        "ademas", "despues", "aqui", "estan", "util", "facil", "unico", "publico", "minimo", "maximo",
        "basico", "metricas", "logico", "operacion", "duracion"
    };

    [Theory]
    [InlineData(ServicesFolder)]
    [InlineData(AppFolder)]
    public void Catalogos_MantemOMesmoConjuntoDeChavesNosTresIdiomas(string folder)
    {
        var portuguese = Load(folder, "pt-BR");
        var problems = new List<string>();

        foreach (var language in Languages)
        {
            var catalog = Load(folder, language);

            problems.AddRange(portuguese.Keys
                .Except(catalog.Keys, StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => language + " sem texto para " + key));

            problems.AddRange(catalog.Keys
                .Except(portuguese.Keys, StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => language + " com chave inexistente em pt-BR: " + key));
        }

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(ServicesFolder)]
    [InlineData(AppFolder)]
    public void Catalogos_NaoDeixamTextoVazioNemChaveSemTraducao(string folder)
    {
        var problems = new List<string>();

        foreach (var language in Languages)
        {
            foreach (var (key, value) in Load(folder, language))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add(language + " com texto vazio em " + key);
                }
                else if (string.Equals(value, key, StringComparison.Ordinal))
                {
                    problems.Add(language + " exibe a propria chave em " + key);
                }
            }
        }

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(ServicesFolder)]
    [InlineData(AppFolder)]
    public void Catalogos_UsamOsMesmosEspacosDeFormatacaoEmTodosOsIdiomas(string folder)
    {
        var portuguese = Load(folder, "pt-BR");
        var problems = new List<string>();

        foreach (var (key, template) in portuguese)
        {
            var expected = Placeholders(template);

            foreach (var language in Languages)
            {
                var actual = Placeholders(Load(folder, language)[key]);
                if (expected.SequenceEqual(actual, StringComparer.Ordinal) is false)
                {
                    problems.Add(key + " (" + language + "): esperado [" + string.Join(", ", expected) + "] e encontrado [" + string.Join(", ", actual) + "]");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(ServicesFolder)]
    [InlineData(AppFolder)]
    public void TextosEmPortuguesEEspanhol_UsamAcentuacaoCorreta(string folder)
    {
        var problems = new List<string>();

        CollectUnaccented(folder, "pt-BR", UnaccentedPortuguese, problems);
        CollectUnaccented(folder, "es", UnaccentedSpanish, problems);

        Assert.Empty(problems);
    }

    [Fact]
    public void Camadas_ConcordamNosTextosQueAparecemNasDuasTelas()
    {
        var services = Load(ServicesFolder, "pt-BR");
        var app = Load(AppFolder, "pt-BR");
        var problems = new List<string>();

        foreach (var key in SharedKeysToCompare)
        {
            Assert.True(services.ContainsKey(key), "Chave ausente nos servicos: " + key);
            Assert.True(app.ContainsKey(key), "Chave ausente na interface: " + key);

            foreach (var language in Languages)
            {
                var fromServices = Load(ServicesFolder, language)[key];
                var fromApp = Load(AppFolder, language)[key];

                if (string.Equals(fromServices, fromApp, StringComparison.Ordinal) is false)
                {
                    problems.Add(key + " (" + language + "): servicos=\"" + fromServices + "\" interface=\"" + fromApp + "\"");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void CatalogosDosServicos_EstaoEmbutidosNoExecutavel()
    {
        var expected = Load(ServicesFolder, "pt-BR");
        var localizer = new JsonLocalizer(new FakeLogger());
        var missing = new List<string>();

        Assert.Equal(3, localizer.Available.Count);

        foreach (var language in new[] { AppLanguage.PtBr, AppLanguage.EnUs, AppLanguage.Es })
        {
            localizer.SetLanguage(language);

            foreach (var key in expected.Keys)
            {
                if (localizer.TryGet(key, out _) is false)
                {
                    missing.Add(language + " -> " + key);
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void CatalogosDaInterface_EstaoEmbutidosNoAplicativo()
    {
        var assemblyPath = ApplicationAssemblyPath();

        // A interface so existe depois da compilacao da solucao inteira.
        if (assemblyPath is null)
        {
            return;
        }

        var assembly = Assembly.LoadFrom(assemblyPath);
        var names = assembly.GetManifestResourceNames();
        var expected = Load(AppFolder, "pt-BR");

        foreach (var language in Languages)
        {
            var name = "OptimizerPC.App.Localization.loc." + language + ".json";

            Assert.Contains(name, names);

            using var stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);

            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream!);
            Assert.NotNull(entries);
            Assert.Equal(expected.Count, entries!.Count);
        }
    }

    [Fact]
    public void Localizador_TrocaDeIdiomaEExibeOsTextosDoCatalogo()
    {
        var portuguese = Load(ServicesFolder, "pt-BR");
        var english = Load(ServicesFolder, "en-US");
        var key = portuguese.Keys.First(candidate => string.Equals(portuguese[candidate], english[candidate], StringComparison.Ordinal) is false);
        var localizer = new JsonLocalizer(new FakeLogger());

        Assert.Equal(AppLanguage.PtBr, localizer.Current);
        Assert.Equal(portuguese[key], localizer[key]);

        localizer.SetLanguage(AppLanguage.EnUs);

        Assert.Equal(AppLanguage.EnUs, localizer.Current);
        Assert.Equal(english[key], localizer[key]);
        Assert.True(localizer.TryGet(key, out var value));
        Assert.False(string.IsNullOrWhiteSpace(value));

        // Sem texto no idioma atual a interface mostra a propria chave, nunca uma tela em branco.
        Assert.False(localizer.TryGet("Texto.Ausente.NosCatalogos", out _));
        Assert.Equal("Texto.Ausente.NosCatalogos", localizer["Texto.Ausente.NosCatalogos"]);
    }

    private static void CollectUnaccented(string folder, string language, string[] words, List<string> problems)
    {
        foreach (var (key, value) in Load(folder, language))
        {
            foreach (var word in words)
            {
                if (ContainsWord(value, word))
                {
                    problems.Add(language + " " + key + " usa \"" + word + "\": " + value);
                    break;
                }
            }
        }
    }

    private static bool ContainsWord(string value, string word) =>
        Regex.IsMatch(value, "(?<![\\p{L}])" + word + "(?![\\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, "\\{[0-9]+\\}")
            .Select(match => match.Value)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> Load(string folder, string language)
    {
        var path = Path.Combine(RepositoryRoot(), folder.Replace('/', Path.DirectorySeparatorChar), "loc." + language + ".json");
        var content = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(content)
               ?? throw new InvalidOperationException("Catalogo invalido: " + path);
    }

    /// <summary>
    /// Caminho do executavel da interface, ou null quando ele ainda nao foi compilado ou
    /// e mais antigo que os catalogos, caso em que nao ha o que verificar.
    /// </summary>
    private static string? ApplicationAssemblyPath()
    {
        var project = Path.Combine(RepositoryRoot(), "src", "OptimizerPC.App");
        var newestSource = Languages
            .Select(language => Path.Combine(project, "Localization", "loc." + language + ".json"))
            .Append(Path.Combine(project, "OptimizerPC.App.csproj"))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(project, "bin", configuration, "net8.0-windows", "OptimizerPC.dll");

            if (File.Exists(candidate) && File.GetLastWriteTimeUtc(candidate) >= newestSource)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptimizerPC.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Raiz do repositorio nao encontrada a partir de " + AppContext.BaseDirectory);
    }
}
