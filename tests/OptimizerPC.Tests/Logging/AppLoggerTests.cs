using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Logging;
using Xunit;

namespace OptimizerPC.Tests.Logging;

public sealed class AppLoggerTests
{
    [Fact]
    public void Log_RespeitaONivelMinimoEIncluiExcecao()
    {
        var sink = new RecordingSink();
        using var logger = new AppLogger(LogLevel.Warning, sink);
        var api = (IAppLogger)logger;

        api.Debug("Test", "debug");
        api.Info("Test", "info");
        api.Warning("Test", "warning");
        api.Error("Test", "error", new InvalidOperationException("falhou"));

        Assert.Equal(2, sink.Records.Count);
        Assert.Equal(LogLevel.Warning, sink.Records[0].Level);
        Assert.Equal("warning", sink.Records[0].Message);
        Assert.Equal(LogLevel.Error, sink.Records[1].Level);
        Assert.Contains("falhou", sink.Records[1].Exception);
    }

    [Fact]
    public void Log_FalhaEmUmDestinoNaoImpedeOsDemais()
    {
        var good = new RecordingSink();
        using var logger = new AppLogger(LogLevel.Debug, new ThrowingSink(), good);
        var api = (IAppLogger)logger;

        api.Info("Test", "continua");

        var record = Assert.Single(good.Records);
        Assert.Equal("continua", record.Message);
    }

    [Fact]
    public void AddRemoveEDispose_ControlamOsDestinosAtivos()
    {
        var first = new RecordingSink();
        var second = new RecordingSink();
        using var logger = new AppLogger(LogLevel.Info, first);
        var api = (IAppLogger)logger;

        logger.AddSink(second);
        api.Info("Test", "primeiro");
        logger.RemoveSink(first);
        api.Info("Test", "segundo");
        logger.Dispose();
        api.Info("Test", "terceiro");

        Assert.Equal(new[] { "primeiro" }, first.Records.Select(record => record.Message));
        Assert.Equal(new[] { "primeiro", "segundo" }, second.Records.Select(record => record.Message));
    }

    [Fact]
    public void MinimumLevel_PodeSerAlteradoEmTempoDeExecucao()
    {
        var sink = new RecordingSink();
        using var logger = new AppLogger(LogLevel.Error, sink);
        var api = (IAppLogger)logger;

        api.Warning("Test", "ignorado");
        logger.MinimumLevel = LogLevel.Debug;
        api.Debug("Test", "aceito");

        var record = Assert.Single(sink.Records);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("aceito", record.Message);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<LogRecord> Records { get; } = new();

        public void Write(LogRecord record) => Records.Add(record);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(LogRecord record) => throw new InvalidOperationException("sink indisponivel");
    }
}
