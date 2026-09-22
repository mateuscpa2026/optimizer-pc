using System.Text.Json;
using OptimizerPC.Core;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Storage;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Storage;

public sealed class HistoryServiceTests
{
    [Fact]
    public async Task RecordAsync_GravaConsultaEFiltraEntradasLocais()
    {
        using var folder = new TempFolder();
        var harness = CreateHarness(folder);
        var oldEntry = Entry("History.Category.Cleanup", "Cleanup", new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), true);
        var recentEntry = Entry("History.Category.Optimization", "Optimize", new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc), false);

        var oldId = await harness.Service.RecordAsync(oldEntry);
        var recentId = await harness.Service.RecordAsync(recentEntry);
        var all = await harness.Service.QueryAsync();
        var cleanup = await harness.Service.QueryAsync(category: "History.Category.Cleanup");

        Assert.True(oldId > 0);
        Assert.True(recentId > oldId);
        Assert.Equal(oldId, oldEntry.Id);
        Assert.Equal(recentId, recentEntry.Id);
        Assert.Equal(new[] { recentId, oldId }, all.Select(entry => entry.Id));
        var stored = Assert.Single(cleanup);
        Assert.Equal(oldId, stored.Id);
        Assert.Equal("detalhe", stored.Details);
        Assert.Equal("restore-1", stored.RestoreRecordKey);
        Assert.Contains(harness.Logger.Entries, entry =>
            entry.Level == LogLevel.Info && entry.Category == "History" && entry.Message.Contains("Cleanup", StringComparison.Ordinal));
        Assert.Contains(harness.Logger.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Category == "History" && entry.Message.Contains("Optimize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClearAsync_RemoveTodasAsEntradasERegistraAAcao()
    {
        using var folder = new TempFolder();
        var harness = CreateHarness(folder);
        await harness.Service.RecordAsync(Entry("one", "one", DateTime.UtcNow, true));
        await harness.Service.RecordAsync(Entry("two", "two", DateTime.UtcNow.AddMinutes(1), true));

        var removed = await harness.Service.ClearAsync();

        Assert.Equal(2, removed);
        Assert.Empty(await harness.Service.QueryAsync());
        Assert.Contains(harness.Logger.Entries, entry =>
            entry.Level == LogLevel.Info && entry.Category == "History" && entry.Message.Contains("2 registros", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_GeraCsvComBomECamposEscapados()
    {
        using var folder = new TempFolder();
        var harness = CreateHarness(folder);
        var entry = Entry(
            "categoria;especial",
            "acao",
            new DateTime(2026, 5, 2, 13, 4, 5, DateTimeKind.Utc),
            true,
            description: "texto; \"citado\"");
        await harness.Service.RecordAsync(entry);
        var path = folder.Combine(Path.Combine("exports", "historico.csv"));

        var exported = await harness.Service.ExportAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        var content = await File.ReadAllTextAsync(path);

        Assert.Equal(path, exported);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.StartsWith("History.Column.Timestamp;History.Column.Category", content);
        Assert.Contains("\"categoria;especial\"", content);
        Assert.Contains("\"texto; \"\"citado\"\"\"", content);
        Assert.Contains("Common.Yes", content);
    }

    [Fact]
    public async Task ExportAsync_GeraJsonComPropriedadesDeRestauracao()
    {
        using var folder = new TempFolder();
        var harness = CreateHarness(folder);
        await harness.Service.RecordAsync(Entry("History.Category.Restore", "Restore", DateTime.UtcNow, true));
        var path = folder.Combine("historico.json");

        var exported = await harness.Service.ExportAsync(path);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var item = Assert.Single(json.RootElement.EnumerateArray());

        Assert.Equal(path, exported);
        Assert.Equal("History.Category.Restore", item.GetProperty("category").GetString());
        Assert.Equal("restore-1", item.GetProperty("restoreRecordKey").GetString());
        Assert.Equal("detalhe", item.GetProperty("details").GetString());
    }

    [Fact]
    public async Task ExportAsync_RecusaCaminhoVazio()
    {
        using var folder = new TempFolder();
        var harness = CreateHarness(folder);

        Assert.Null(await harness.Service.ExportAsync(" "));
    }

    private static Harness CreateHarness(TempFolder folder)
    {
        var logger = new FakeLogger();
        var database = new SqliteDatabase(folder.Combine("history.db"), logger);
        return new Harness(new HistoryService(new HistoryRepository(database), logger, new FakeLocalizer()), logger);
    }

    private static HistoryEntry Entry(
        string category,
        string action,
        DateTime timestamp,
        bool success,
        string description = "descricao") => new()
    {
        TimestampUtc = timestamp,
        Category = category,
        Action = action,
        Description = description,
        Result = success ? "Success" : "Failure",
        Success = success,
        BytesFreed = 2048,
        Details = "detalhe",
        RestoreRecordKey = "restore-1"
    };

    private sealed record Harness(HistoryService Service, FakeLogger Logger);
}
