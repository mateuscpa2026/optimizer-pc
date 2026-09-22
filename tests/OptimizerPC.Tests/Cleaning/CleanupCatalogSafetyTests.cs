using OptimizerPC.Core;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Cleaning;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Auditoria do catalogo de limpeza. O catalogo vive no codigo e e a unica fonte de
/// caminhos aceita pela limpeza: estas verificacoes provam que nenhuma entrada aponta
/// para arquivos do sistema, pastas de programas, conteudo pessoal ou para o proprio
/// banco de dados do aplicativo.
/// </summary>
public sealed class CleanupCatalogSafetyTests
{
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string[] KnownRoots = { "%SystemRoot%", "%TEMP%", "%LOCALAPPDATA%", "%ProgramData%" };

    private static readonly string[] PersonalFolderNames =
    {
        "Documents", "Desktop", "Pictures", "Videos", "Music", "Downloads", "OneDrive", "Favorites", "Saved Games"
    };

    /// <summary>Troca o segmento curinga dos perfis de navegador por um nome fixo.</summary>
    private static string Materialize(string template) =>
        Environment.ExpandEnvironmentVariables(template).Replace("*", "perfil-de-teste");

    private static string SampleFileName(string pattern) => pattern.Replace("*", "abc1234");

    [Fact]
    public void Catalogo_NaoTemIdentificadoresDuplicados()
    {
        var ids = CleanupTargetCatalog.Targets.Select(target => target.Id).ToList();

        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalogo_TemChavesDeLocalizacaoEmTodosOsAlvos()
    {
        foreach (var target in CleanupTargetCatalog.Targets)
        {
            Assert.StartsWith("Cleanup.Target.", target.TitleKey, StringComparison.Ordinal);
            Assert.EndsWith(".Title", target.TitleKey, StringComparison.Ordinal);
            Assert.EndsWith(".Description", target.DescriptionKey, StringComparison.Ordinal);
        }

        var titles = CleanupTargetCatalog.Targets.Select(target => target.TitleKey).ToList();
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogo_ApenasALixeiraFicaSemLocais()
    {
        foreach (var target in CleanupTargetCatalog.Targets)
        {
            if (target.IsRecycleBin)
            {
                Assert.Empty(target.Locations);
                continue;
            }

            Assert.NotEmpty(target.Locations);
        }
    }

    [Fact]
    public void Catalogo_TemExatamenteUmAlvoDeLixeira()
    {
        var recycleBins = CleanupTargetCatalog.Targets.Where(target => target.IsRecycleBin).ToList();

        var recycleBin = Assert.Single(recycleBins);
        Assert.Equal(CleanupTargetCatalog.RecycleBinId, recycleBin.Id);
        Assert.Equal(CleanupCategory.RecycleBin, recycleBin.Category);
        Assert.True(recycleBin.RequiresConfirmation);
        Assert.False(recycleBin.SelectedByDefault);
        Assert.NotNull(CleanupTargetCatalog.Find(CleanupTargetCatalog.RecycleBinId));
    }

    [Fact]
    public void Catalogo_NenhumLocalApontaParaAreaCritica()
    {
        foreach (var location in AllLocations())
        {
            var path = Materialize(location.Template);

            Assert.True(
                CleanupSafety.IsAllowedLocation(path),
                "Local recusado pela barreira de seguranca: " + location.Template);
        }
    }

    [Fact]
    public void Catalogo_NaoApontaParaPastasPessoais()
    {
        foreach (var location in AllLocations())
        {
            var segments = Materialize(location.Template)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                Assert.DoesNotContain(segment, PersonalFolderNames, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Catalogo_UsaSomenteVariaveisDeAmbienteReconhecidas()
    {
        foreach (var location in AllLocations())
        {
            var root = location.Template.Split(Path.DirectorySeparatorChar)[0];

            Assert.Contains(root, KnownRoots);
            Assert.NotEqual(root, Environment.ExpandEnvironmentVariables(root));
        }
    }

    [Fact]
    public void Catalogo_NaoRemoveArquivosPessoais()
    {
        foreach (var location in AllLocations())
        {
            string? name = location.IsSingleFile
                ? Path.GetFileName(location.Template)
                : location.FilePattern is null ? null : SampleFileName(location.FilePattern);

            if (name is null)
            {
                continue;
            }

            Assert.False(ProtectedPaths.IsPersonalDocument(name), "Arquivo pessoal no catalogo: " + name);
        }
    }

    [Fact]
    public void Catalogo_NaoRemoveExecutaveisDoAplicativo()
    {
        var dataFolder = Path.Combine(LocalAppData, "OptimizerPC");

        foreach (var location in AllLocations())
        {
            Assert.False(
                ProtectedPaths.IsUnder(Materialize(location.Template), dataFolder),
                "A limpeza nao pode remover os dados do proprio aplicativo: " + location.Template);
        }
    }

    [Fact]
    public void Catalogo_NaoUsaCaracteresDeComandoNemAspas()
    {
        foreach (var location in AllLocations())
        {
            foreach (var forbidden in new[] { '"', '&', '|', ';', '>', '<', '`' })
            {
                Assert.DoesNotContain(forbidden, location.Template);
            }
        }
    }

    [Fact]
    public void Catalogo_NaoUsaRiscoMaximoNemLimpezaAutomaticaDeRisco()
    {
        foreach (var target in CleanupTargetCatalog.Targets)
        {
            Assert.True(target.Risk is RiskLevel.Low or RiskLevel.Medium, "Risco excessivo em " + target.Id);

            if (target.RequiresConfirmation)
            {
                Assert.NotEqual(RiskLevel.Low, target.Risk);
                Assert.False(target.SelectedByDefault, "Alvo de risco nao pode vir marcado: " + target.Id);
            }
        }
    }

    [Fact]
    public void Catalogo_ExigeElevacaoApenasNasAreasDoSistema()
    {
        foreach (var target in CleanupTargetCatalog.Targets)
        {
            if (target.IsRecycleBin)
            {
                continue;
            }

            var underWindows = target.Locations.All(location =>
                location.Template.StartsWith("%SystemRoot%", StringComparison.OrdinalIgnoreCase) ||
                location.Template.StartsWith("%ProgramData%", StringComparison.OrdinalIgnoreCase));

            if (underWindows)
            {
                Assert.Equal(ElevationRequirement.Required, target.Elevation);
            }
        }

        Assert.Equal(ElevationRequirement.None, CleanupTargetCatalog.Find("user-temp")!.Elevation);
        Assert.Equal(ElevationRequirement.None, CleanupTargetCatalog.Find("browser-caches")!.Elevation);
        Assert.Equal(ElevationRequirement.Required, CleanupTargetCatalog.Find("prefetch")!.Elevation);
        Assert.Equal(ElevationRequirement.Required, CleanupTargetCatalog.Find("font-cache")!.Elevation);
    }

    [Fact]
    public void Catalogo_IdadeMinimaApenasNosLogs()
    {
        foreach (var location in AllLocations())
        {
            var isLog = location.Template.Contains("\\Logs\\", StringComparison.OrdinalIgnoreCase);

            if (location.MinAgeDays > 0)
            {
                Assert.True(isLog, "Idade minima fora de logs de manutencao: " + location.Template);
            }

            if (isLog)
            {
                Assert.True(location.MinAgeDays > 0, "Log de manutencao sem idade minima: " + location.Template);
            }
        }
    }

    [Fact]
    public void Catalogo_NenhumLocalApontaParaUnidadeInteira()
    {
        var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        foreach (var location in AllLocations())
        {
            var path = ProtectedPaths.Normalize(Materialize(location.Template));

            Assert.True(path.Length > drive.TrimEnd(Path.DirectorySeparatorChar).Length);
            Assert.True(ProtectedPaths.IsUnder(path, drive));
        }
    }

    [Fact]
    public void Find_EncontraIgnorandoCaixaERecusaIdentificadorVazio()
    {
        var target = CleanupTargetCatalog.Find("USER-TEMP");

        Assert.NotNull(target);
        Assert.Equal("user-temp", target!.Id);
        Assert.Null(CleanupTargetCatalog.Find(null!));
        Assert.Null(CleanupTargetCatalog.Find("   "));
        Assert.Null(CleanupTargetCatalog.Find("alvo-inexistente"));
    }

    [Fact]
    public void Catalogo_TodosOsAlvosTemCategoriaDistintaDoRecycleBinForaDaLixeira()
    {
        foreach (var target in CleanupTargetCatalog.Targets.Where(target => target.IsRecycleBin is false))
        {
            Assert.NotEqual(CleanupCategory.RecycleBin, target.Category);
        }

        Assert.Equal(
            CleanupTargetCatalog.Targets.Count,
            CleanupTargetCatalog.Targets.Select(target => target.Category).Distinct().Count());
    }

    private static IEnumerable<CleanupLocation> AllLocations() =>
        CleanupTargetCatalog.Targets.SelectMany(target => target.Locations);
}
