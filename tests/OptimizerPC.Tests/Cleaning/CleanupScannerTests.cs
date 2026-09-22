using OptimizerPC.Core;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Cleaning;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Prova a varredura de limpeza: ela apenas le, mede o espaco potencialmente recuperavel
/// e respeita o catalogo (padroes, idade minima, alvos que exigem elevacao). Nada e
/// removido nesta etapa.
/// </summary>
public sealed class CleanupScannerTests
{
    private static readonly string Temp = Environment.ExpandEnvironmentVariables("%TEMP%");
    private static readonly string Windows = Environment.ExpandEnvironmentVariables("%SystemRoot%");
    private static readonly string LocalAppData = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%");

    private static CleanupScanner CreateScanner(InMemoryFileSystem files, bool elevated = false) =>
        new(files, new FakeElevationService { IsElevated = elevated }, new FakeLogger());

    [Fact]
    public async Task ScanAsync_ExcluiALixeiraQuandoSolicitado()
    {
        var scanner = CreateScanner(new InMemoryFileSystem());

        var targets = await scanner.ScanAsync(includeRecycleBin: false);

        Assert.Equal(CleanupTargetCatalog.Targets.Count - 1, targets.Count);
        Assert.DoesNotContain(targets, target => target.Id == CleanupTargetCatalog.RecycleBinId);
    }

    [Fact]
    public async Task ScanAsync_IncluiALixeiraSomenteComConfirmacao()
    {
        var scanner = CreateScanner(new InMemoryFileSystem());

        var targets = await scanner.ScanAsync(includeRecycleBin: true);

        var recycleBin = targets.Single(target => target.Id == CleanupTargetCatalog.RecycleBinId);
        Assert.Equal(CleanupCategory.RecycleBin, recycleBin.Category);
        Assert.True(recycleBin.RequiresConfirmation);
        Assert.Equal(RiskLevel.Medium, recycleBin.Risk);
        Assert.False(recycleBin.IsSelected);
        Assert.Empty(recycleBin.Paths);
        Assert.True(recycleBin.TotalBytes >= 0);
        Assert.True(recycleBin.FileCount >= 0);
    }

    [Fact]
    public async Task ScanAsync_MarcaIndisponivelQuandoNaoEncontraOsLocais()
    {
        var scanner = CreateScanner(new InMemoryFileSystem());

        var targets = await scanner.ScanAsync(includeRecycleBin: false);

        Assert.All(targets, target =>
        {
            Assert.False(target.IsAvailable);
            Assert.False(target.IsSelected);
            Assert.Equal("Cleanup.Reason.LocationMissing", target.UnavailableReasonKey);
        });
    }

    [Fact]
    public async Task ScanAsync_MedeArquivosDaPastaDeTemporarios()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path.Combine(Temp, "a.tmp"), 1024);
        files.AddFile(Path.Combine(Temp, "b.tmp"), 2048);
        files.AddFile(Path.Combine(Temp, "sub", "c.tmp"), 4096);

        var targets = await CreateScanner(files).ScanAsync(includeRecycleBin: false);

        var userTemp = targets.Single(target => target.Id == "user-temp");
        Assert.True(userTemp.IsAvailable);
        Assert.True(userTemp.IsSelected);
        Assert.Equal(7168, userTemp.TotalBytes);
        Assert.Equal(3, userTemp.FileCount);
        Assert.Equal(2, userTemp.DirectoryCount);
    }

    [Fact]
    public async Task ScanAsync_RespeitaOPadraoDeNomeNaContagem()
    {
        var files = new InMemoryFileSystem();
        var explorer = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
        files.AddFile(Path.Combine(explorer, "thumbcache_96.db"), 512);
        files.AddFile(Path.Combine(explorer, "iconcache_48.db"), 256);
        files.AddFile(Path.Combine(explorer, "outro.db"), 9999);

        var targets = await CreateScanner(files).ScanAsync(includeRecycleBin: false);

        var thumbnails = targets.Single(target => target.Id == "thumbnail-cache");
        Assert.Equal(768, thumbnails.TotalBytes);
        Assert.Equal(2, thumbnails.FileCount);
    }

    [Fact]
    public async Task ScanAsync_RespeitaAIdadeMinimaDosLogs()
    {
        var files = new InMemoryFileSystem();
        var cbs = Path.Combine(Windows, "Logs", "CBS");
        files.AddFile(Path.Combine(cbs, "antigo.log"), 100, DateTime.UtcNow.AddDays(-30));
        files.AddFile(Path.Combine(cbs, "recente.log"), 200, DateTime.UtcNow.AddDays(-1));

        var targets = await CreateScanner(files).ScanAsync(includeRecycleBin: false);

        var componentLogs = targets.Single(target => target.Id == "component-logs");
        Assert.Equal(100, componentLogs.TotalBytes);
        Assert.Equal(1, componentLogs.FileCount);
    }

    [Fact]
    public async Task ScanAsync_MarcaElevacaoNecessariaSemAdministrador()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path.Combine(Windows, "Prefetch", "ntfs.pf"), 1024);

        var targets = await CreateScanner(files, elevated: false).ScanAsync(includeRecycleBin: false);

        var prefetch = targets.Single(target => target.Id == "prefetch");
        Assert.False(prefetch.IsAvailable);
        Assert.False(prefetch.IsSelected);
        Assert.Equal("Cleanup.Reason.NeedsElevation", prefetch.UnavailableReasonKey);
        Assert.Equal(1024, prefetch.TotalBytes);
    }

    [Fact]
    public async Task ScanAsync_MantemAlvoDisponivelComAdministrador()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path.Combine(Windows, "Prefetch", "ntfs.pf"), 1024);

        var targets = await CreateScanner(files, elevated: true).ScanAsync(includeRecycleBin: false);

        var prefetch = targets.Single(target => target.Id == "prefetch");
        Assert.True(prefetch.IsAvailable);
        Assert.True(prefetch.IsSelected);
        Assert.Null(prefetch.UnavailableReasonKey);
    }

    [Fact]
    public async Task ScanAsync_FiltraPorCategoria()
    {
        var files = new InMemoryFileSystem();
        files.AddDirectory(Path.Combine(Temp, "cache"));

        var targets = await CreateScanner(files).ScanAsync(new[] { CleanupCategory.UserTemp }, includeRecycleBin: false);

        var single = Assert.Single(targets);
        Assert.Equal("user-temp", single.Id);
    }

    [Fact]
    public async Task ScanAsync_NaoRemoveNada()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path.Combine(Temp, "a.tmp"), 1024);
        files.AddFile(Path.Combine(Windows, "Prefetch", "ntfs.pf"), 2048);

        var targets = await CreateScanner(files, elevated: true).ScanAsync(includeRecycleBin: false);

        Assert.NotEmpty(targets);
        Assert.Empty(files.DeletedFiles);
        Assert.Empty(files.DeletedDirectories);
    }

    [Fact]
    public async Task ScanAsync_ReportaProgresso()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path.Combine(Temp, "a.tmp"), 1024);

        var progress = new RecordingProgress();
        var targets = await CreateScanner(files).ScanAsync(includeRecycleBin: false, progress: progress);

        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        Assert.Equal(targets.Count, reports[^1].TotalTargets);
        Assert.Equal(targets.Count, reports[^1].CompletedTargets);
        Assert.Contains(reports, report => report.CurrentTargetId == "user-temp" && report.CurrentPath.Length > 0);
    }

    [Fact]
    public async Task ScanAsync_RespeitaCancelamento()
    {
        var scanner = CreateScanner(new InMemoryFileSystem());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cancellationToken: source.Token));
    }

    private sealed class RecordingProgress : IProgress<CleanupProgress>
    {
        private readonly List<CleanupProgress> _reports = new();

        public IReadOnlyList<CleanupProgress> Reports
        {
            get
            {
                lock (_reports)
                {
                    return _reports.ToList();
                }
            }
        }

        public void Report(CleanupProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }
}
