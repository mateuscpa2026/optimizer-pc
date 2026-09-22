using OptimizerPC.Core.Security;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Security;

/// <summary>
/// Prova a porta unica de autorizacao de exclusoes: caminho do sistema, conteudo
/// pessoal, raiz de unidade, links e arquivos fora da area permitida sao recusados.
/// </summary>
public sealed class SafePathValidatorTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Temp = ProtectedPaths.Normalize(Path.GetTempPath());
    private static readonly string SystemDrive = Path.GetPathRoot(Windows) ?? @"C:\";

    private readonly SafePathValidator _validator = new();

    private static SafeDeleteContext Context(params string[] roots) => new(roots);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateForDeletion_RecusaCaminhoInvalido(string? path)
    {
        var result = _validator.ValidateForDeletion(path!, Context(Temp));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.InvalidPath", result.ReasonKey);
    }

    [Theory]
    [InlineData("System32")]
    [InlineData("WinSxS")]
    [InlineData("config")]
    public void ValidateForDeletion_RecusaAreaCriticaDoWindows(string subFolder)
    {
        var path = Path.Combine(Windows, subFolder, "arquivo.tmp");
        var result = _validator.ValidateForDeletion(path, Context(Windows));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.ProtectedPath", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaArquivoCriticoMesmoComRaizPermitida()
    {
        var path = Path.Combine(Temp, "pagefile.sys");
        var result = _validator.ValidateForDeletion(path, Context(Temp));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.ProtectedPath", result.ReasonKey);
    }

    [Theory]
    [InlineData("OneDrive")]
    [InlineData("Saved Games")]
    [InlineData("Contacts")]
    [InlineData("Searches")]
    [InlineData("Links")]
    public void ValidateForDeletion_RecusaConteudoPessoal(string relative)
    {
        var path = Path.Combine(Profile, relative, "backup.zip");
        var result = _validator.ValidateForDeletion(path, Context(Profile));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.PersonalContent", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaDocumentoPessoalEmPastaTecnica()
    {
        var path = Path.Combine(Temp, "relatorio-final.docx");
        var result = _validator.ValidateForDeletion(path, Context(Temp));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.PersonalDocument", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_AceitaArquivoTecnicoEmPastaDeCache()
    {
        var path = Path.Combine(Temp, "cache-1234.tmp");
        var result = _validator.ValidateForDeletion(path, Context(Temp));

        Assert.True(result.IsAllowed);
        Assert.Equal("Security.Allowed", result.ReasonKey);
        Assert.Equal(path, result.ResolvedPath, ignoreCase: true);
    }

    [Fact]
    public void ValidateForDeletion_RecusaQuandoNaoHaRaizPermitida()
    {
        var result = _validator.ValidateForDeletion(Path.Combine(Temp, "cache.tmp"), Context());

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.NoAllowedRoots", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaCaminhoForaDaRaizPermitida()
    {
        var allowed = Path.Combine(Temp, "permitida");
        var other = Path.Combine(Temp, "outra", "cache.tmp");
        var result = _validator.ValidateForDeletion(other, Context(allowed));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.OutsideAllowedRoots", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaARaizDaUnidade()
    {
        var result = _validator.ValidateForDeletion(SystemDrive, Context(SystemDrive));

        // A raiz da unidade nunca e removivel. O motivo varia com o diretorio atual do
        // processo (a normalizacao de "C:" resolve para ele), mas a recusa e constante.
        Assert.False(result.IsAllowed);
        Assert.Contains(result.ReasonKey, new[] { "Security.Reason.DriveRoot", "Security.Reason.ProtectedPath" });
    }

    [Fact]
    public void ValidateForDeletion_RecusaArquivoQuandoSomentePastasSaoPermitidas()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Root, "arquivo.tmp");
        File.WriteAllText(file, "x");

        var result = _validator.ValidateForDeletion(file, new SafeDeleteContext(new[] { temp.Root }, AllowFiles: false));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.FilesNotAllowed", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaPastaQuandoSomenteArquivosSaoPermitidos()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Root, "subpasta");
        Directory.CreateDirectory(folder);

        var result = _validator.ValidateForDeletion(folder, new SafeDeleteContext(new[] { temp.Root }, AllowDirectories: false));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.DirectoriesNotAllowed", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_RecusaLinkDePasta()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Root, "alvo");
        var link = Path.Combine(temp.Root, "atalho");
        Directory.CreateDirectory(target);

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception)
        {
            // Criar link exige privilegio ou Modo de Desenvolvedor: sem ele o teste nao se aplica.
            return;
        }

        if (Directory.Exists(link) is false)
        {
            return;
        }

        var result = _validator.ValidateForDeletion(link, Context(temp.Root));

        Assert.False(result.IsAllowed);
        Assert.Equal("Security.Reason.ReparsePoint", result.ReasonKey);
    }

    [Fact]
    public void ValidateForDeletion_AceitaAreaDoUsuarioComEscapesExplicitos()
    {
        var path = Path.Combine(Profile, "OneDrive", "backup.zip");
        var result = _validator.ValidateForDeletion(
            path,
            new SafeDeleteContext(new[] { Profile }, AllowPersonalContent: true));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateForDeletion_AceitaAreaDoSistemaComEscapesExplicitos()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var path = Path.Combine(programData, "OptimizerPC-Testes", "arquivo.tmp");

        var result = _validator.ValidateForDeletion(
            path,
            new SafeDeleteContext(new[] { programData }, AllowProtectedSystemPaths: true));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateForDeletion_ExigeContexto()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.ValidateForDeletion(Path.Combine(Temp, "a.tmp"), null!));
    }

    [Fact]
    public void IsUsableCleanupRoot_AceitaPastaRealERecusaUnidade()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Root, "cache");
        Directory.CreateDirectory(folder);

        Assert.True(SafePathValidator.IsUsableCleanupRoot(folder));
        Assert.False(SafePathValidator.IsUsableCleanupRoot(SystemDrive));
        Assert.False(SafePathValidator.IsUsableCleanupRoot(Path.Combine(temp.Root, "inexistente")));
        Assert.False(SafePathValidator.IsUsableCleanupRoot("   "));
    }
}
