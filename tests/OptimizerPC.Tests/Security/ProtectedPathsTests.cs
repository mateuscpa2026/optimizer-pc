using OptimizerPC.Core.Security;
using Xunit;

namespace OptimizerPC.Tests.Security;

/// <summary>
/// Prova que areas criticas do sistema e pastas pessoais do usuario nunca sao
/// tratadas como removiveis, independentemente de quem informou o caminho.
/// </summary>
public sealed class ProtectedPathsTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SystemDrive = Path.GetPathRoot(Windows) ?? @"C:\";
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Temp = Path.GetTempPath();

    public static IEnumerable<object[]> AreasCriticas()
    {
        yield return new object[] { Windows };
        yield return new object[] { Path.Combine(Windows, "System32") };
        yield return new object[] { Path.Combine(Windows, "System32", "config", "SAM") };
        yield return new object[] { Path.Combine(Windows, "System32", "drivers", "ntfs.sys") };
        yield return new object[] { Path.Combine(Windows, "SysWOW64") };
        yield return new object[] { Path.Combine(Windows, "WinSxS", "Manifests") };
        yield return new object[] { Path.Combine(Windows, "Fonts", "arial.ttf") };
        yield return new object[] { Path.Combine(Windows, "Boot") };
        yield return new object[] { Path.Combine(SystemDrive, "Boot", "bcd") };
        yield return new object[] { Path.Combine(SystemDrive, "EFI", "Microsoft", "Boot") };
        yield return new object[] { Path.Combine(SystemDrive, "Recovery", "WindowsRE") };
        yield return new object[] { Path.Combine(SystemDrive, "System Volume Information") };
        yield return new object[] { Path.Combine(SystemDrive, "$Recycle.Bin", "S-1-5-21") };
        yield return new object[] { Path.Combine(SystemDrive, "PerfLogs", "Admin") };
    }

    public static IEnumerable<object[]> PastasDeProgramas()
    {
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        yield return new object[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
    }

    [Fact]
    public void IsProtected_RecusaCaminhoVazio()
    {
        Assert.True(ProtectedPaths.IsProtected(null));
        Assert.True(ProtectedPaths.IsProtected(string.Empty));
        Assert.True(ProtectedPaths.IsProtected("   "));
    }

    [Theory]
    [MemberData(nameof(AreasCriticas))]
    public void IsProtected_RecusaAreasCriticasDoWindows(string path) =>
        Assert.True(ProtectedPaths.IsProtected(path), "Deveria estar protegido: " + path);

    [Theory]
    [MemberData(nameof(PastasDeProgramas))]
    public void IsProtected_RecusaPastasDeProgramasEPessoais(string path) =>
        Assert.True(ProtectedPaths.IsProtected(path), "Deveria estar protegido: " + path);

    [Theory]
    [InlineData("pagefile.sys")]
    [InlineData("swapfile.sys")]
    [InlineData("hiberfil.sys")]
    [InlineData("bootmgr")]
    [InlineData("ntldr")]
    [InlineData("bcd")]
    [InlineData("boot.ini")]
    [InlineData("ntuser.dat")]
    [InlineData("kernel32.dll")]
    [InlineData("ntdll.dll")]
    public void IsProtected_RecusaArquivoCriticoPeloNome(string fileName)
    {
        var inTemp = Path.Combine(Temp, fileName);

        Assert.True(ProtectedPaths.IsProtected(inTemp), "Deveria estar protegido: " + inTemp);
        Assert.True(ProtectedPaths.IsProtected(fileName), "Deveria estar protegido pelo nome: " + fileName);
    }

    [Fact]
    public void IsProtected_AceitaArquivoTemporarioComum()
    {
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(Temp, "optimizerpc-teste.tmp")));
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(Temp, "cache", "dados.bin")));
    }

    [Fact]
    public void ProtectedFileNamesList_ContemOsArquivosDeInicializacao()
    {
        Assert.Contains("pagefile.sys", ProtectedPaths.ProtectedFileNamesList, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hiberfil.sys", ProtectedPaths.ProtectedFileNamesList, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("bcd", ProtectedPaths.ProtectedFileNamesList, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtectedRoots_EstaPreenchidaComCaminhosAbsolutos()
    {
        Assert.NotEmpty(ProtectedPaths.ProtectedRoots);

        foreach (var root in ProtectedPaths.ProtectedRoots)
        {
            Assert.True(Path.IsPathFullyQualified(root), "Raiz nao absoluta: " + root);
        }
    }

    [Fact]
    public void ProtectedRoots_NaoIncluiAPastaDeTemporarios()
    {
        var normalizedTemp = ProtectedPaths.Normalize(Temp);

        Assert.DoesNotContain(ProtectedPaths.ProtectedRoots, root => ProtectedPaths.IsUnder(normalizedTemp, root));
    }

    public static IEnumerable<object[]> PastasPessoais()
    {
        yield return new object[] { "Documents" };
        yield return new object[] { "Desktop" };
        yield return new object[] { "Pictures" };
        yield return new object[] { "Videos" };
        yield return new object[] { "Music" };
        yield return new object[] { "Downloads" };
        yield return new object[] { "OneDrive" };
        yield return new object[] { "Saved Games" };
        yield return new object[] { "Contacts" };
        yield return new object[] { "Searches" };
        yield return new object[] { "Links" };
        yield return new object[] { "Favorites" };
    }

    [Theory]
    [MemberData(nameof(PastasPessoais))]
    public void IsUserPersonalPath_ReconhecePastasDoUsuario(string relative)
    {
        var path = Path.Combine(Profile, relative, "arquivo.txt");

        Assert.True(ProtectedPaths.IsUserPersonalPath(path), "Deveria ser pasta pessoal: " + path);
    }

    [Fact]
    public void IsUserPersonalPath_RecusaCaminhoVazio() =>
        Assert.False(ProtectedPaths.IsUserPersonalPath(null));

    [Fact]
    public void IsUserPersonalPath_RecusaPastaDeCache()
    {
        Assert.False(ProtectedPaths.IsUserPersonalPath(Path.Combine(Temp, "cache.tmp")));
        Assert.False(ProtectedPaths.IsUserPersonalPath(Path.Combine(Windows, "Temp")));
    }

    [Theory]
    [InlineData("relatorio.docx")]
    [InlineData("planilha.xlsx")]
    [InlineData("foto.jpg")]
    [InlineData("video.mp4")]
    [InlineData("musica.mp3")]
    [InlineData("backup.zip")]
    [InlineData("contrato.pdf")]
    public void IsPersonalDocument_ReconheceArquivoPessoal(string fileName) =>
        Assert.True(ProtectedPaths.IsPersonalDocument(Path.Combine(Temp, fileName)));

    [Theory]
    [InlineData("programa.exe")]
    [InlineData("biblioteca.dll")]
    [InlineData("driver.sys")]
    [InlineData("painel.cpl")]
    [InlineData("console.msc")]
    [InlineData("sem-extensao")]
    [InlineData("arquivo.tmp")]
    public void IsPersonalDocument_RecusaExecutaveisExtensoesDesconhecidas(string fileName) =>
        Assert.False(ProtectedPaths.IsPersonalDocument(Path.Combine(Temp, fileName)));

    [Fact]
    public void Normalize_RemoveSeparadorFinalEEspacos()
    {
        var expected = ProtectedPaths.Normalize(Windows);

        Assert.Equal(expected, ProtectedPaths.Normalize(Windows + Path.DirectorySeparatorChar));
        Assert.Equal(expected, ProtectedPaths.Normalize("  \"" + Windows + "\"  "));
        Assert.Equal(expected, ProtectedPaths.Normalize(Windows.Replace('\\', '/')));
    }

    [Fact]
    public void Normalize_RecusaEntradaVazia()
    {
        Assert.Equal(string.Empty, ProtectedPaths.Normalize(null!));
        Assert.Equal(string.Empty, ProtectedPaths.Normalize("   "));
    }

    [Fact]
    public void IsUnder_ComparaIgnorandoCaixaENaoConfundePrefixos()
    {
        var windows = ProtectedPaths.Normalize(Windows);

        Assert.True(ProtectedPaths.IsUnder(Path.Combine(Windows, "Temp", "a.tmp"), windows));
        Assert.True(ProtectedPaths.IsUnder(windows, windows));
        Assert.True(ProtectedPaths.IsUnder(Windows.ToUpperInvariant(), windows));
        Assert.False(ProtectedPaths.IsUnder(windows + "OutraPasta", windows));
        Assert.False(ProtectedPaths.IsUnder(string.Empty, windows));
    }

    [Fact]
    public void IsUnder_ComparaCaminhosReaisSemResolverLinks()
    {
        var parent = Path.Combine(Temp, "pai");
        var child = Path.Combine(parent, "filho", "arquivo.tmp");

        Assert.True(ProtectedPaths.IsUnder(child, parent));
        Assert.False(ProtectedPaths.IsUnder(parent, child));
    }
}
