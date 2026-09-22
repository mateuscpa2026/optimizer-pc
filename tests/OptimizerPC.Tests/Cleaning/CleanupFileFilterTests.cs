using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.Cleaning;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Prova as regras que decidem o que aparece na varredura e o que e removido: padrao de
/// nome com no maximo um curinga e idade minima em dias. As duas etapas usam o mesmo
/// filtro, entao a interface mostra exatamente o que sera apagado.
/// </summary>
public sealed class CleanupFileFilterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "OptimizerPC-Tests");

    private static FileEntry File(string name, DateTime? lastWriteUtc = null) =>
        new(Path.Combine(Root, name), 1024, lastWriteUtc ?? DateTime.UtcNow.AddDays(-30));

    [Fact]
    public void Matches_SemPadraoESemIdadeAceitaQualquerArquivo()
    {
        var location = new CleanupLocation("%TEMP%");

        Assert.True(CleanupFileFilter.Matches(location, File("qualquer.tmp")));
        Assert.True(CleanupFileFilter.Matches(location, File("sem-extensao")));
    }

    [Fact]
    public void Matches_ComPadraoAceitaSomenteONomeCorrespondente()
    {
        var location = new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer", FilePattern: "thumbcache_*.db");

        Assert.True(CleanupFileFilter.Matches(location, File("thumbcache_128.db")));
        Assert.True(CleanupFileFilter.Matches(location, File("THUMBCACHE_256.DB")));
        Assert.False(CleanupFileFilter.Matches(location, File("iconcache_128.db")));
        Assert.False(CleanupFileFilter.Matches(location, File("thumbcache_128.dbt")));
    }

    [Fact]
    public void Matches_RespeitaIdadeMinimaEmDias()
    {
        var location = new CleanupLocation("%SystemRoot%\\Logs\\CBS", MinAgeDays: 7);

        Assert.True(CleanupFileFilter.Matches(location, File("antigo.log", DateTime.UtcNow.AddDays(-30))));
        Assert.True(CleanupFileFilter.Matches(location, File("limite.log", DateTime.UtcNow.AddDays(-8))));
        Assert.False(CleanupFileFilter.Matches(location, File("recente.log", DateTime.UtcNow.AddDays(-1))));
        Assert.False(CleanupFileFilter.Matches(location, File("de-hoje.log", DateTime.UtcNow)));
    }

    [Fact]
    public void Matches_IdadeZeroAceitaArquivoRecemCriado()
    {
        var location = new CleanupLocation("%TEMP%");

        Assert.True(CleanupFileFilter.Matches(location, File("novo.tmp", DateTime.UtcNow)));
    }

    [Fact]
    public void Matches_CombinaPadraoEIdadeMinima()
    {
        var location = new CleanupLocation("%SystemRoot%\\Logs\\CBS", FilePattern: "*.log", MinAgeDays: 7);

        Assert.True(CleanupFileFilter.Matches(location, File("antigo.log", DateTime.UtcNow.AddDays(-10))));
        Assert.False(CleanupFileFilter.Matches(location, File("recente.log", DateTime.UtcNow.AddDays(-2))));
        Assert.False(CleanupFileFilter.Matches(location, File("antigo.txt", DateTime.UtcNow.AddDays(-10))));
    }

    [Fact]
    public void MatchesPattern_SemCuringaComparaIgnorandoCaixa()
    {
        Assert.True(CleanupFileFilter.MatchesPattern("arquivo.pf", "arquivo.pf"));
        Assert.True(CleanupFileFilter.MatchesPattern("ARQUIVO.PF", "arquivo.pf"));
        Assert.False(CleanupFileFilter.MatchesPattern("outro.pf", "arquivo.pf"));
        Assert.False(CleanupFileFilter.MatchesPattern("arquivo.pfx", "arquivo.pf"));
    }

    [Fact]
    public void MatchesPattern_ComCuringaExigePrefixoESufixo()
    {
        Assert.True(CleanupFileFilter.MatchesPattern("thumbcache_128.db", "thumbcache_*.db"));
        Assert.True(CleanupFileFilter.MatchesPattern("thumbcache_.db", "thumbcache_*.db"));
        Assert.False(CleanupFileFilter.MatchesPattern("iconcache_128.db", "thumbcache_*.db"));
        Assert.False(CleanupFileFilter.MatchesPattern("thumbcache_128.dbx", "thumbcache_*.db"));
    }

    [Fact]
    public void MatchesPattern_ExigeTamanhoMinimoParaNaoSobreporPrefixoESufixo()
    {
        // "thumbcache.db" (12) e menor que o prefixo + sufixo (15): nao pode casar.
        Assert.False(CleanupFileFilter.MatchesPattern("thumbcache.db", "thumbcache_*.db"));
        Assert.False(CleanupFileFilter.MatchesPattern("pf", "*.pf"));
        Assert.True(CleanupFileFilter.MatchesPattern("a.pf", "*.pf"));
    }

    [Fact]
    public void MatchesPattern_CuringaSobreTodoONome()
    {
        Assert.True(CleanupFileFilter.MatchesPattern("qualquer-coisa.tmp", "*"));
        Assert.True(CleanupFileFilter.MatchesPattern(string.Empty, "*"));
    }
}
