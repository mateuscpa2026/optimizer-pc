using OptimizerPC.Core.Security;
using OptimizerPC.Services.Cleaning;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Prova a segunda barreira da limpeza: a lista branca de locais recusa areas criticas do
/// Windows, pastas de programas, conteudo pessoal e raizes de unidade, mesmo que o catalogo
/// viesse a conter uma entrada incorreta.
/// </summary>
public sealed class CleanupSafetyTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SystemDrive = Path.GetPathRoot(Windows) ?? @"C:\";
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Temp = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedLocation_RecusaCaminhoVazio(string? path) =>
        Assert.False(CleanupSafety.IsAllowedLocation(path));

    [Fact]
    public void IsAllowedLocation_RecusaRaizDeUnidade()
    {
        Assert.False(CleanupSafety.IsAllowedLocation(SystemDrive));
        Assert.False(CleanupSafety.IsAllowedLocation(SystemDrive + "Windows"));
    }

    [Fact]
    public void IsAllowedLocation_ExigePeloMenosDoisNiveis()
    {
        Assert.False(CleanupSafety.IsAllowedLocation(Path.Combine(SystemDrive, "Temp")));
        Assert.False(CleanupSafety.IsAllowedLocation(Path.Combine(SystemDrive, "Windows")));

        // Dois niveis ja sao suficientes.
        Assert.True(CleanupSafety.IsAllowedLocation(Path.Combine(ProgramData, "OptimizerPC")));
    }

    [Fact]
    public void IsAllowedLocation_AceitaPastasDeCacheTemporariosELogs()
    {
        var accepted = new[]
        {
            Path.Combine(Windows, "Temp"),
            Path.Combine(Windows, "SoftwareDistribution", "Download"),
            Path.Combine(Windows, "Prefetch"),
            Path.Combine(Windows, "Logs", "CBS"),
            Path.Combine(Windows, "Minidump"),
            Path.Combine(Temp, "subpasta"),
            Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
            Path.Combine(LocalAppData, "Microsoft", "Windows", "INetCache"),
            Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"),
            Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data")
        };

        foreach (var path in accepted)
        {
            Assert.True(CleanupSafety.IsAllowedLocation(path), "Deveria ser permitido: " + path);
        }
    }

    [Theory]
    [InlineData("System32")]
    [InlineData("SysWOW64")]
    [InlineData("WinSxS")]
    [InlineData("Boot")]
    [InlineData("Fonts")]
    [InlineData("assembly")]
    [InlineData("Microsoft.NET")]
    [InlineData("servicing")]
    [InlineData("INF")]
    [InlineData("Speech")]
    [InlineData("SystemApps")]
    [InlineData("ImmersiveControlPanel")]
    public void IsAllowedLocation_RecusaSubpastasCriticasDoWindows(string subFolder)
    {
        var path = Path.Combine(Windows, subFolder, "arquivo.tmp");

        Assert.False(CleanupSafety.IsAllowedLocation(path), "Deveria ser recusado: " + path);
    }

    [Fact]
    public void IsAllowedLocation_RecusaRaizesProibidas()
    {
        var forbidden = new[]
        {
            Path.Combine(SystemDrive, "Boot", "resources"),
            Path.Combine(SystemDrive, "EFI", "Microsoft"),
            Path.Combine(SystemDrive, "Recovery", "WindowsRE"),
            Path.Combine(SystemDrive, "System Volume Information", "Chaves"),
            Path.Combine(SystemDrive, "$Recycle.Bin", "S-1-5-21"),
            Path.Combine(SystemDrive, "PerfLogs", "Admin")
        };

        foreach (var path in forbidden)
        {
            Assert.False(CleanupSafety.IsAllowedLocation(path), "Deveria ser recusado: " + path);
        }
    }

    [Fact]
    public void IsAllowedLocation_RecusaPastasDeProgramas()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            Assert.False(CleanupSafety.IsAllowedLocation(Path.Combine(root, "Aplicativo", "cache")));
        }
    }

    [Theory]
    [InlineData("Documents")]
    [InlineData("Desktop")]
    [InlineData("Pictures")]
    [InlineData("Videos")]
    [InlineData("Music")]
    [InlineData("Downloads")]
    [InlineData("OneDrive")]
    public void IsAllowedLocation_RecusaAreaPessoal(string relative)
    {
        var path = Path.Combine(Profile, relative, "cache");

        Assert.False(CleanupSafety.IsAllowedLocation(path), "Deveria ser recusado: " + path);
    }

    [Fact]
    public void IsAllowedLocation_AceitaProgramDataDeRelatoriosDeErro()
    {
        var path = Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive");

        Assert.True(CleanupSafety.IsAllowedLocation(path));
    }

    [Fact]
    public void IsAllowedLocation_NaoConfundePrefixoParecidoComRaizProibida()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programs) is false)
        {
            Assert.True(CleanupSafety.IsAllowedLocation(programs + " Extra\\cache"));
        }

        Assert.True(CleanupSafety.IsAllowedLocation(Path.Combine(Windows, "TempBackup", "cache")));
        Assert.True(CleanupSafety.IsAllowedLocation(Path.Combine(Windows, "Logs", "CBSBackup")));
    }

    [Fact]
    public void ForbiddenRoots_NaoIncluiProgramDataNemPastaDeTemporarios()
    {
        Assert.NotEmpty(CleanupSafety.ForbiddenRoots);

        foreach (var root in CleanupSafety.ForbiddenRoots)
        {
            Assert.False(ProtectedPaths.IsUnder(ProgramData, root), "ProgramData nao pode ser raiz proibida: " + root);
            Assert.False(ProtectedPaths.IsUnder(Temp, root), "A pasta de temporarios nao pode ser raiz proibida: " + root);
            Assert.True(Path.IsPathFullyQualified(root), "Raiz proibida deveria ser absoluta: " + root);
        }
    }
}
