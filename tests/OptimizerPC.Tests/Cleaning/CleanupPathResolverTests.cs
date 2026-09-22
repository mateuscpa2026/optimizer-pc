using OptimizerPC.Core;
using OptimizerPC.Services.Cleaning;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Prova a resolucao dos modelos do catalogo em caminhos reais. Somente leitura: a
/// resolucao descarta o que a barreira de seguranca recusa, ignora caminhos inexistentes,
/// expande o segmento curinga dos perfis e nao devolve duplicatas.
/// </summary>
public sealed class CleanupPathResolverTests
{
    private static readonly string Temp = Environment.ExpandEnvironmentVariables("%TEMP%");
    private static readonly string LocalAppData = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%");
    private static readonly string Windows = Environment.ExpandEnvironmentVariables("%SystemRoot%");
    private static readonly string Profile = Environment.ExpandEnvironmentVariables("%USERPROFILE%");

    private static CleanupTargetDescriptor Descriptor(params CleanupLocation[] locations) =>
        new("teste", CleanupCategory.UserTemp, "Cleanup.Target.Test.Title", "Cleanup.Target.Test.Description", locations);

    private static string? First(CleanupTargetDescriptor descriptor, InMemoryFileSystem files) =>
        CleanupPathResolver.Resolve(files, descriptor).FirstOrDefault()?.Path;

    [Fact]
    public void Resolve_IgnoraCaminhoInexistente()
    {
        var files = new InMemoryFileSystem();
        var resolved = CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation("%TEMP%")));

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_ExpandeVariavelDeAmbiente()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(Path.Combine(Temp, "cache"));

        var resolved = CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation("%TEMP%\\cache")));

        var location = Assert.Single(resolved);
        Assert.Equal(Path.Combine(Temp, "cache"), location.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_DescartaLocalRecusadoPelaBarreiraDeSeguranca()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(Path.Combine(Windows, "System32", "config"));
        files.AddDirectory(Path.Combine(Windows, "WinSxS", "Manifests"));
        files.AddDirectory(Path.Combine(Profile, "Documents"));

        var descriptor = Descriptor(
            new CleanupLocation("%SystemRoot%\\System32\\config"),
            new CleanupLocation("%SystemRoot%\\WinSxS\\Manifests"),
            new CleanupLocation("%USERPROFILE%\\Documents"));

        Assert.Empty(CleanupPathResolver.Resolve(files, descriptor));
    }

    [Fact]
    public void Resolve_DescartaCaminhoRasoOuRaizDeUnidade()
    {
        var files = new InMemoryFileSystem();
        var drive = Path.GetPathRoot(Windows) ?? @"C:\";

        var root = Path.Combine(drive, "Temp");
        files.AddDirectory(root);
        files.AddDirectory(Path.Combine(drive, "Windows"));

        Assert.Empty(CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation(root))));
        Assert.Empty(CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation(Path.Combine(drive, "Windows")))));
    }

    [Fact]
    public void Resolve_AceitaPastaTecnicaExistente()
    {
        var files = new InMemoryFileSystem();
        var folder = Path.Combine(Windows, "Logs", "CBS");
        files.AddDirectory(folder);

        Assert.Equal(folder, First(Descriptor(new CleanupLocation("%SystemRoot%\\Logs\\CBS")), files), ignoreCase: true);
    }

    [Fact]
    public void Resolve_ArquivoUnicoExigeQueOArquivoExista()
    {
        var files = new InMemoryFileSystem();

        Assert.Empty(CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation("%SystemRoot%\\MEMORY.DMP", IsSingleFile: true))));

        files.AddFile(Path.Combine(Windows, "MEMORY.DMP"), 4096);

        var resolved = Assert.Single(CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation("%SystemRoot%\\MEMORY.DMP", IsSingleFile: true))));
        Assert.Equal(Path.Combine(Windows, "MEMORY.DMP"), resolved.Path, ignoreCase: true);
    }

    [Fact]
    public void Resolve_ExpandeCuringaDosPerfisDeNavegador()
    {
        var files = new InMemoryFileSystem();
        var browser = Path.Combine(LocalAppData, "Google", "Chrome", "User Data");

        files.AddDirectory(Path.Combine(browser, "Default", "Cache", "Cache_Data"));
        files.AddDirectory(Path.Combine(browser, "Profile 2", "Cache", "Cache_Data"));

        var resolved = CleanupPathResolver.Resolve(
            files,
            Descriptor(new CleanupLocation("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache\\Cache_Data")));

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, item => item.Path.EndsWith("\\Default\\Cache\\Cache_Data", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved, item => item.Path.EndsWith("\\Profile 2\\Cache\\Cache_Data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_CuringaSemPerfisNaoDevolveNada()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(Path.Combine(LocalAppData, "Google", "Chrome", "User Data"));

        var resolved = CleanupPathResolver.Resolve(
            files,
            Descriptor(new CleanupLocation("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache\\Cache_Data")));

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_CuringaUsaApenasFilhosDiretos()
    {
        var files = new InMemoryFileSystem();
        var browser = Path.Combine(LocalAppData, "Google", "Chrome", "User Data");

        files.AddDirectory(Path.Combine(browser, "Default", "Cache", "Cache_Data"));
        files.AddDirectory(Path.Combine(browser, "Default", "Nested", "Cache", "Cache_Data"));

        var resolved = CleanupPathResolver.Resolve(
            files,
            Descriptor(new CleanupLocation("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache\\Cache_Data")));

        var single = Assert.Single(resolved);
        Assert.EndsWith("\\Default\\Cache\\Cache_Data", single.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_VariavelNaoResolvidaNaoGeraCaminho()
    {
        var files = new InMemoryFileSystem();
        var template = "%OPTIMIZERPC-VARIAVEL-INEXISTENTE%\\cache";

        files.AddDirectory(Environment.ExpandEnvironmentVariables(template));

        Assert.Empty(CleanupPathResolver.Resolve(files, Descriptor(new CleanupLocation(template))));
    }

    [Fact]
    public void Resolve_NaoDuplicaCaminhosRepetidos()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(Path.Combine(Temp, "cache"));

        var descriptor = Descriptor(
            new CleanupLocation("%TEMP%\\cache"),
            new CleanupLocation("%TEMP%\\cache"),
            new CleanupLocation("%TEMP%\\CACHE"));

        var resolved = CleanupPathResolver.Resolve(files, descriptor);

        Assert.Single(resolved);
    }

    [Fact]
    public void Resolve_MantemAsDuasRegrasDeNomeDoMesmoLocal()
    {
        var files = new InMemoryFileSystem();
        var folder = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
        files.AddDirectory(folder);

        var descriptor = Descriptor(
            new CleanupLocation(folder, FilePattern: "thumbcache_*.db"),
            new CleanupLocation(folder, FilePattern: "iconcache_*.db"));

        var resolved = CleanupPathResolver.Resolve(files, descriptor);

        // Sem as duas regras o cache de icones nunca seria limpo.
        Assert.Equal(2, resolved.Count);
        Assert.Equal(new[] { "thumbcache_*.db", "iconcache_*.db" }, resolved.Select(item => item.Location.FilePattern));
        Assert.All(resolved, item => Assert.Equal(folder, item.Path, ignoreCase: true));
    }

    [Fact]
    public void Resolve_DeduplicaRegrasIdenticas()
    {
        var files = new InMemoryFileSystem();
        var folder = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
        files.AddDirectory(folder);

        var descriptor = Descriptor(
            new CleanupLocation(folder, FilePattern: "thumbcache_*.db"),
            new CleanupLocation(folder, FilePattern: "THUMBCACHE_*.DB"));

        Assert.Single(CleanupPathResolver.Resolve(files, descriptor));
    }

    [Fact]
    public void Resolve_LimitaAExpansaoDoCuringa()
    {
        var files = new InMemoryFileSystem();
        var browser = Path.Combine(LocalAppData, "Mozilla", "Firefox", "Profiles");

        for (var index = 0; index < 260; index++)
        {
            files.AddDirectory(Path.Combine(browser, "perfil" + index.ToString("D3"), "cache2"));
        }

        var resolved = CleanupPathResolver.Resolve(
            files,
            Descriptor(new CleanupLocation("%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2")));

        Assert.Equal(256, resolved.Count);
    }
}
