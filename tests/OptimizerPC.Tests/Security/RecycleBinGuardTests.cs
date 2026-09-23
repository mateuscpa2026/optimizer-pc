using OptimizerPC.Core.Security;
using OptimizerPC.Services.System;
using Xunit;

namespace OptimizerPC.Tests.Security;

/// <summary>
/// Prova a regra que autoriza o envio de arquivos para a Lixeira a partir da tela
/// Armazenamento. Areas do sistema, pastas pessoais, documentos, raizes de unidade
/// e pastas inteiras sao recusados, sempre com um motivo exibivel na interface.
/// </summary>
public sealed class RecycleBinGuardTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Temp = ProtectedPaths.Normalize(Path.GetTempPath());
    private static readonly string SystemDrive = Path.GetPathRoot(Windows) ?? @"C:\";

    private readonly SafePathValidator _validator = new();

    private (bool Allowed, string ReasonKey) Authorize(string? path, params string[] roots)
    {
        var allowed = RecycleBinGuard.TryAuthorize(path, roots, _validator, out _, out var reasonKey);
        return (allowed, allowed ? "Security.Allowed" : reasonKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryAuthorize_RecusaCaminhoInvalido(string? path)
    {
        var (allowed, reason) = Authorize(path, Temp);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.InvalidPath", reason);
    }

    [Theory]
    [InlineData("pagefile.sys")]
    [InlineData("swapfile.sys")]
    [InlineData("hiberfil.sys")]
    [InlineData("bootmgr")]
    [InlineData("ntuser.dat")]
    [InlineData("desktop.ini")]
    public void TryAuthorize_RecusaArquivoCriticoMesmoDentroDaRaizPermitida(string fileName)
    {
        var (allowed, reason) = Authorize(Path.Combine(Temp, fileName), Temp);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.ProtectedFile", reason);
    }

    [Theory]
    [InlineData("System32")]
    [InlineData("WinSxS")]
    [InlineData("Fonts")]
    public void TryAuthorize_RecusaAreaDoWindows(string subFolder)
    {
        var path = Path.Combine(Windows, subFolder, "arquivo.tmp");
        var (allowed, reason) = Authorize(path, Windows);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.ProtectedPath", reason);
    }

    // Documents, Desktop, Pictures, Videos e Music tambem sao raizes protegidas
    // (SpecialFolder), entao o validador as recusa como ProtectedPath antes de chegar
    // a checagem de conteudo pessoal. O motivo exibido difere, mas a recusa e a mesma.
    [Theory]
    [InlineData("Documents")]
    [InlineData("Desktop")]
    [InlineData("Pictures")]
    [InlineData("Videos")]
    [InlineData("Music")]
    public void TryAuthorize_RecusaPastaPessoalProtegida(string relative)
    {
        var path = Path.Combine(Profile, relative, "arquivo.tmp");
        var (allowed, reason) = Authorize(path, Profile);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.ProtectedPath", reason);
    }

    // Pastas pessoais que nao estao na lista de raizes protegidas caem na checagem de
    // conteudo pessoal do validador.
    [Theory]
    [InlineData("Downloads")]
    [InlineData("OneDrive")]
    [InlineData("Saved Games")]
    [InlineData("Contacts")]
    [InlineData("Searches")]
    [InlineData("Links")]
    public void TryAuthorize_RecusaConteudoPessoal(string relative)
    {
        var path = Path.Combine(Profile, relative, "arquivo.tmp");
        var (allowed, reason) = Authorize(path, Profile);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.PersonalContent", reason);
    }

    [Theory]
    [InlineData("relatorio.pdf")]
    [InlineData("foto.jpg")]
    [InlineData("backup.zip")]
    [InlineData("planilha.xlsx")]
    [InlineData("video.mp4")]
    public void TryAuthorize_RecusaDocumentoPessoalEmPastaTecnica(string fileName)
    {
        var (allowed, reason) = Authorize(Path.Combine(Temp, fileName), Temp);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.PersonalDocument", reason);
    }

    [Fact]
    public void TryAuthorize_RecusaCaminhoForaDaRaizPermitida()
    {
        var path = Path.Combine(SystemDrive, "OptimizerPC-Teste", "arquivo.tmp");
        var (allowed, reason) = Authorize(path, Temp);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.OutsideAllowedRoots", reason);
    }

    [Fact]
    public void TryAuthorize_RecusaQuandoNaoHaRaizPermitida()
    {
        var (allowed, reason) = Authorize(Path.Combine(Temp, "arquivo.tmp"));

        Assert.False(allowed);
        Assert.Equal("Security.Reason.NoAllowedRoots", reason);
    }

    [Fact]
    public void TryAuthorize_RecusaRaizDaUnidade()
    {
        var (allowed, reason) = Authorize(SystemDrive, SystemDrive);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.DriveRoot", reason);
    }

    [Fact]
    public void TryAuthorize_RecusaPastaInteira()
    {
        var (allowed, reason) = Authorize(Temp, Temp);

        Assert.False(allowed);
        Assert.Equal("Security.Reason.DirectoriesNotAllowed", reason);
    }

    [Fact]
    public void TryAuthorize_AceitaArquivoTecnicoDentroDaRaizPermitida()
    {
        var path = Path.Combine(Temp, "optimizer-pc-teste-9f3a.tmp");
        var allowed = RecycleBinGuard.TryAuthorize(path, new[] { Temp }, _validator, out var normalized, out var reason);

        Assert.True(allowed);
        Assert.Empty(reason);
        Assert.Equal(ProtectedPaths.Normalize(path), normalized, ignoreCase: true);
    }

    [Fact]
    public void TryAuthorize_DevolveCaminhoNormalizadoAoRecusar()
    {
        var path = Temp + Path.DirectorySeparatorChar;
        var allowed = RecycleBinGuard.TryAuthorize(path, new[] { Temp }, _validator, out var normalized, out _);

        Assert.False(allowed);
        Assert.Equal(Temp, normalized, ignoreCase: true);
    }

    [Theory]
    [InlineData("arquivo.tmp")]
    [InlineData("pagefile.sys")]
    [InlineData("relatorio.pdf")]
    public void TryAuthorize_ServicoEInterfaceUsamAMesmaRegra(string fileName)
    {
        var path = Path.Combine(Temp, fileName);

        var fromGuard = RecycleBinGuard.TryAuthorize(path, new[] { Temp }, _validator, out _, out var guardReason);
        var fromService = RecycleBinMover.TryAuthorize(path, new[] { Temp }, _validator, out _, out var serviceReason);

        Assert.Equal(fromGuard, fromService);
        Assert.Equal(guardReason, serviceReason);
    }
}
