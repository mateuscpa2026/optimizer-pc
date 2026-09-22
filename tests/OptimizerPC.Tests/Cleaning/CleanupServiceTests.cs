using OptimizerPC.Core;
using OptimizerPC.Core.Models;
using OptimizerPC.Core.Security;
using OptimizerPC.Services.Cleaning;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Cleaning;

/// <summary>
/// Prova que a execucao da limpeza so remove o que o catalogo interno autoriza:
/// os caminhos informados pela interface sao ignorados, arquivos criticos ficam
/// preservados, falhas pontuais nao interrompem o ciclo e cada categoria gera
/// registro no historico. Nada toca o disco: o sistema de arquivos e em memoria.
/// </summary>
public sealed class CleanupServiceTests
{
    private static readonly string Temp = ProtectedPaths.Normalize(Path.GetTempPath());

    private static readonly string Windows = ProtectedPaths.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    private static readonly string LocalAppData = ProtectedPaths.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static readonly string ExplorerCache = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");

    private sealed record Harness(
        CleanupService Service,
        InMemoryFileSystem Files,
        FakeHistoryService History,
        FakeSettingsService Settings,
        FakeLogger Logger);

    private static Harness CreateHarness(bool elevated = false)
    {
        var files = new InMemoryFileSystem();
        var history = new FakeHistoryService();
        var settings = new FakeSettingsService();
        var logger = new FakeLogger();

        var service = new CleanupService(
            files,
            new SafePathValidator(),
            new FakeElevationService { IsElevated = elevated },
            history,
            settings,
            new FakeLocalizer(),
            logger);

        return new Harness(service, files, history, settings, logger);
    }

    private static CleanupTarget Target(
        string id,
        IReadOnlyList<string>? paths = null,
        long totalBytes = 0,
        bool selected = true,
        bool available = true) => new()
        {
            Id = id,
            Category = CleanupCategory.UserTemp,
            TitleKey = "Cleanup.Target.Teste.Title",
            DescriptionKey = "Cleanup.Target.Teste.Description",
            Paths = paths ?? Array.Empty<string>(),
            TotalBytes = totalBytes,
            IsSelected = selected,
            IsAvailable = available
        };

    private static string TempFile(string relative) => Path.Combine(Temp, relative);

    private static string WindowsTempFile(string name) => Path.Combine(Windows, "Temp", name);

    [Fact]
    public async Task CleanAsync_RemoveSomenteOsArquivosDoAlvoERegistraOEspacoLiberado()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("optimizerpc-a.tmp"), 1024);
        harness.Files.AddFile(TempFile("optimizerpc-b.tmp"), 2048);
        harness.Files.AddFile(TempFile(Path.Combine("subpasta", "optimizerpc-c.tmp")), 4096);

        var reports = new RecordingProgress();
        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp", totalBytes: 7168) }, reports);

        var result = Assert.Single(summary.Results);
        Assert.Equal("user-temp", result.TargetId);
        Assert.Equal(3, result.DeletedFiles);
        Assert.Equal(7168, result.DeletedBytes);
        Assert.Equal(0, result.SkippedFiles);
        Assert.True(result.Succeeded);
        Assert.Equal("Cleanup.Result.Cleaned", result.MessageKey);
        Assert.Empty(result.Failures);
        Assert.False(summary.WasCancelled);
        Assert.Equal(7168, summary.TotalDeletedBytes);

        Assert.Equal(3, harness.Files.DeletedFiles.Count);
        Assert.Contains(TempFile("optimizerpc-a.tmp"), harness.Files.DeletedFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(TempFile(Path.Combine("subpasta", "optimizerpc-c.tmp")), harness.Files.DeletedFiles, StringComparer.OrdinalIgnoreCase);

        // A subpasta vazia sai; a propria pasta de temporarios permanece.
        Assert.Contains(Path.Combine(Temp, "subpasta"), harness.Files.DeletedDirectories, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Temp, harness.Files.DeletedDirectories, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, reports.Items.Count);
        Assert.All(reports.Items, item => Assert.Equal("user-temp", item.CurrentTargetId));
        Assert.All(reports.Items, item => Assert.Equal(1, item.TotalTargets));
        Assert.Equal(7168, reports.Items[^1].ProcessedBytes);
    }

    [Fact]
    public async Task CleanAsync_IgnoraOsCaminhosInformadosPelaInterface()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("cache-real.tmp"), 512);
        harness.Files.AddFile(Path.Combine(Windows, "System32", "kernel32.dll"), 4096);

        var summary = await harness.Service.CleanAsync(new[]
        {
            Target("user-temp", paths: new[] { Path.Combine(Windows, "System32") })
        });

        var deleted = Assert.Single(harness.Files.DeletedFiles);
        Assert.Equal(TempFile("cache-real.tmp"), deleted, ignoreCase: true);
        Assert.Equal(512, summary.TotalDeletedBytes);
        Assert.True(harness.Files.FileExists(Path.Combine(Windows, "System32", "kernel32.dll")));
    }

    [Fact]
    public async Task CleanAsync_PreservaArquivoCriticoDentroDaAreaPermitida()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("pagefile.sys"), 8192);
        harness.Files.AddFile(TempFile("cache-ok.tmp"), 1024);

        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp") });

        var result = Assert.Single(summary.Results);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(1024, result.DeletedBytes);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("Cleanup.Result.Partial", result.MessageKey);
        Assert.True(result.Succeeded);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Security.Reason.ProtectedFile", failure.ReasonKey);
        Assert.True(harness.Files.FileExists(TempFile("pagefile.sys")));
    }

    [Fact]
    public async Task CleanAsync_RespeitaOPadraoDeNomeDoCatalogo()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(Path.Combine(ExplorerCache, "thumbcache_128.db"), 512);
        harness.Files.AddFile(Path.Combine(ExplorerCache, "iconcache_256.db"), 256);
        harness.Files.AddFile(Path.Combine(ExplorerCache, "outro-arquivo.bin"), 4096);

        var summary = await harness.Service.CleanAsync(new[] { Target("thumbnail-cache") });

        Assert.Equal(2, summary.TotalDeletedFiles);
        Assert.Equal(768, summary.TotalDeletedBytes);
        Assert.True(harness.Files.FileExists(Path.Combine(ExplorerCache, "outro-arquivo.bin")));
    }

    [Fact]
    public async Task CleanAsync_ContinuaOCicloQuandoUmArquivoNaoPodeSerRemovido()
    {
        var harness = CreateHarness();
        var blocked = TempFile("preso.tmp");
        harness.Files.AddFile(blocked, 2048);
        harness.Files.AddFile(TempFile("ok.tmp"), 1024);
        harness.Files.FailingPaths.Add(blocked);

        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp") });

        var result = Assert.Single(summary.Results);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(1024, result.DeletedBytes);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("Cleanup.Result.Partial", result.MessageKey);
        Assert.True(result.Succeeded);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Cleanup.Reason.DeleteFailed", failure.ReasonKey);
        Assert.Equal(blocked, failure.Path, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(failure.Detail));
        Assert.True(harness.Files.FileExists(blocked));
    }

    [Fact]
    public async Task CleanAsync_MarcaComoBloqueadoQuandoNadaPodeSerRemovido()
    {
        var harness = CreateHarness();
        var blocked = TempFile("preso.tmp");
        harness.Files.AddFile(blocked, 2048);
        harness.Files.FailingPaths.Add(blocked);

        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp") });

        var result = Assert.Single(summary.Results);
        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal("Cleanup.Result.Blocked", result.MessageKey);
        Assert.False(result.Succeeded);
        Assert.Equal(0, summary.TotalDeletedBytes);

        // Sem espaço liberado nao ha o que comemorar no historico de configuracao.
        Assert.Equal(0, harness.Settings.SaveCount);
        Assert.Null(harness.Settings.Current.LastCleanupUtc);
    }

    [Fact]
    public async Task CleanAsync_IgnoraAlvosNaoSelecionadosOuIndisponiveis()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("cache.tmp"), 1024);

        var summary = await harness.Service.CleanAsync(new[]
        {
            Target("user-temp"),
            Target("windows-temp", selected: false),
            Target("prefetch", available: false)
        });

        var result = Assert.Single(summary.Results);
        Assert.Equal("user-temp", result.TargetId);
        Assert.Equal(1, summary.TotalDeletedFiles);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Info && entry.Message.Contains("2 alvo(s) nao selecionado(s) ou indisponivel(is) foram ignorados."));
        Assert.Single(harness.History.Entries);
    }

    [Fact]
    public async Task CleanAsync_ExigeElevacaoParaLimparAreaDoSistema()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(WindowsTempFile("arquivo.tmp"), 4096);

        var summary = await harness.Service.CleanAsync(new[] { Target("windows-temp") });

        var result = Assert.Single(summary.Results);
        Assert.False(result.Succeeded);
        Assert.Equal("Cleanup.Reason.NeedsElevation", result.MessageKey);
        Assert.Empty(harness.Files.DeletedFiles);
        Assert.True(harness.Files.FileExists(WindowsTempFile("arquivo.tmp")));
    }

    [Fact]
    public async Task CleanAsync_RemoveAreaDoSistemaComAdministrador()
    {
        var harness = CreateHarness(elevated: true);
        harness.Files.AddFile(WindowsTempFile("arquivo.tmp"), 4096);

        var summary = await harness.Service.CleanAsync(new[] { Target("windows-temp") });

        var result = Assert.Single(summary.Results);
        Assert.True(result.Succeeded);
        Assert.Equal("Cleanup.Result.Cleaned", result.MessageKey);
        Assert.Equal(4096, summary.TotalDeletedBytes);
        Assert.Single(harness.Files.DeletedFiles);
        Assert.False(harness.Files.FileExists(WindowsTempFile("arquivo.tmp")));
    }

    [Fact]
    public async Task CleanAsync_RegistraHistoricoEDataDaUltimaLimpeza()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("cache.tmp"), 2048);

        await harness.Service.CleanAsync(new[] { Target("user-temp") });

        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal("History.Category.Cleanup", entry.Category);
        Assert.Equal("Cleanup.Target.Teste.Title", entry.Action);
        Assert.Equal("Cleanup.Result.Cleaned", entry.Result);
        Assert.True(entry.Success);
        Assert.Equal(2048, entry.BytesFreed);
        Assert.StartsWith("Cleanup.History.Description", entry.Description);
        Assert.Null(entry.Details);

        Assert.Equal(1, harness.Settings.SaveCount);
        Assert.NotNull(harness.Settings.Current.LastCleanupUtc);
    }

    [Fact]
    public async Task CleanAsync_RegistraNoHistoricoOFatoDeNadaTerSidoRemovido()
    {
        var harness = CreateHarness();
        harness.Files.AddDirectory(Temp);

        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp") });

        var result = Assert.Single(summary.Results);
        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal("Cleanup.Result.NothingToDo", result.MessageKey);
        Assert.True(result.Succeeded);

        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(0, entry.BytesFreed);
        Assert.Equal(0, harness.Settings.SaveCount);
    }

    [Fact]
    public async Task CleanAsync_RespeitaCancelamentoAntesDeQualquerExclusao()
    {
        var harness = CreateHarness();
        harness.Files.AddFile(TempFile("cache.tmp"), 1024);

        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var summary = await harness.Service.CleanAsync(new[] { Target("user-temp") }, cancellationToken: source.Token);

        Assert.True(summary.WasCancelled);
        Assert.Empty(summary.Results);
        Assert.Empty(harness.Files.DeletedFiles);
        Assert.Empty(harness.History.Entries);
        Assert.Equal(0, harness.Settings.SaveCount);
        Assert.Contains(harness.Logger.Entries, entry => entry.Message.Contains("Limpeza cancelada pelo usuario."));
    }

    [Fact]
    public async Task CleanAsync_SemAlvosNaoGravaHistoricoNemConfiguracoes()
    {
        var harness = CreateHarness();

        var summary = await harness.Service.CleanAsync(Array.Empty<CleanupTarget>());

        Assert.Empty(summary.Results);
        Assert.Equal(0, summary.TotalDeletedBytes);
        Assert.False(summary.WasCancelled);
        Assert.Empty(harness.History.Entries);
        Assert.Equal(0, harness.Settings.SaveCount);
    }

    [Fact]
    public async Task CleanAsync_ExigeAListaDeAlvos()
    {
        var harness = CreateHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Service.CleanAsync(null!));
    }

    /// <summary>Relatorio de progresso que coleta de forma sincrona, sem depender do contexto de sincronizacao.</summary>
    private sealed class RecordingProgress : IProgress<CleanupProgress>
    {
        private readonly List<CleanupProgress> _items = new();

        public IReadOnlyList<CleanupProgress> Items
        {
            get
            {
                lock (_items)
                {
                    return _items.ToList();
                }
            }
        }

        public void Report(CleanupProgress value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }
}
