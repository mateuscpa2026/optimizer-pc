using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;
using OptimizerPC.Services.Optimization;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Optimization;

public sealed class HealthScoreServiceTests
{
    private readonly HealthScoreService _service = new(new FakeLocalizer());

    [Fact]
    public void Calculate_RecusaEntradaNula()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Calculate(null!));
    }

    [Fact]
    public void Calculate_ComDadosMinimosRenormalizaOsPesosDisponiveis()
    {
        var score = _service.Calculate(new HealthScoreInput { CpuUsagePercent = 60 });

        Assert.Equal(67, score.Total);
        Assert.Equal(ScoreCategory.Attention, score.Category);
        Assert.Equal(new[] { "cpu", "cleanup" }, score.Factors.Select(factor => factor.Id));
        Assert.Equal(0.12 / 0.22, score.Factors[0].Weight, 8);
        Assert.Equal(0.10 / 0.22, score.Factors[1].Weight, 8);
        Assert.Equal(40, score.Factors[0].Score);
        Assert.Equal(100, score.Factors[1].Score);
    }

    [Fact]
    public void Calculate_UsaODiscoDoSistemaMesmoQuandoNaoEOPimeiroDaLista()
    {
        var score = _service.Calculate(new HealthScoreInput
        {
            Volumes = new[]
            {
                Volume(totalGigabytes: 200, freeGigabytes: 100, isSystemDrive: false),
                Volume(totalGigabytes: 100, freeGigabytes: 10, isSystemDrive: true)
            }
        });

        var disk = Assert.Single(score.Factors, factor => factor.Id == "disk-space");
        Assert.Equal(70, disk.Score);
        Assert.Equal(Severity.Info, disk.Severity);
        Assert.Contains("10", disk.DetailValue);
    }

    [Fact]
    public void Calculate_SemSmartNaoInventaSaudeDoDisco()
    {
        var score = _service.Calculate(new HealthScoreInput
        {
            DriveHealth = new[]
            {
                new DeviceHealthReport { Status = DriveHealthStatus.Unknown },
                new DeviceHealthReport { Status = DriveHealthStatus.Unavailable }
            }
        });

        var drive = Assert.Single(score.Factors, factor => factor.Id == "drive-health");
        Assert.Equal(85, drive.Score);
        Assert.Equal(Severity.Ok, drive.Severity);
        Assert.Equal("Score.Factor.DriveHealth.NoData", drive.DetailKey);
        Assert.Equal("Score.Factor.DriveHealth.DetailValue(0, 2)", drive.DetailValue);
    }

    [Fact]
    public void Calculate_AplicaOPiorEstadoConhecidoDoDisco()
    {
        var score = _service.Calculate(new HealthScoreInput
        {
            DriveHealth = new[]
            {
                new DeviceHealthReport { Status = DriveHealthStatus.Healthy },
                new DeviceHealthReport { Status = DriveHealthStatus.Warning },
                new DeviceHealthReport { Status = DriveHealthStatus.Failing }
            }
        });

        var drive = Assert.Single(score.Factors, factor => factor.Id == "drive-health");
        Assert.Equal(0, drive.Score);
        Assert.Equal(Severity.Critical, drive.Severity);
        Assert.Equal("Score.Factor.DriveHealth.Detail", drive.DetailKey);
        Assert.Equal("Score.Factor.DriveHealth.DetailValue(3, 3)", drive.DetailValue);
    }

    [Fact]
    public void Calculate_PenalizaInicializacaoServicosLimpezaEProcessosPelosLimitesDefinidos()
    {
        var score = _service.Calculate(new HealthScoreInput
        {
            StartupEntries = new[]
            {
                Startup(enabled: true, impact: StartupImpact.High),
                Startup(enabled: true, impact: StartupImpact.High),
                Startup(enabled: true, impact: StartupImpact.Medium),
                Startup(enabled: false, impact: StartupImpact.High)
            },
            Services = Enumerable.Range(0, 8)
                .Select(_ => new WindowsServiceInfo
                {
                    IsMicrosoft = false,
                    IsSystemCritical = false,
                    StartMode = ServiceStartMode.Automatic
                })
                .ToArray(),
            RecoverableBytes = 20L * 1024 * 1024 * 1024,
            RunningProcessCount = 261
        });

        Assert.Equal(4, score.Factors.Count);
        Assert.Equal(71, Factor(score, "startup").Score);
        Assert.Equal(92, Factor(score, "services").Score);
        Assert.Equal(45, Factor(score, "cleanup").Score);
        Assert.Equal(50, Factor(score, "processes").Score);
        Assert.Equal(ScoreCategory.Attention, score.Category);
    }

    [Fact]
    public void Calculate_LimitaEntradasForaDaFaixa()
    {
        var score = _service.Calculate(new HealthScoreInput
        {
            CpuUsagePercent = -10,
            Memory = new MemoryInfo { TotalBytes = 100, AvailableBytes = 200 },
            Volumes = new[] { new VolumeInfo { TotalBytes = 100, FreeBytes = 200, IsSystemDrive = true } },
            RecoverableBytes = -1,
            RunningProcessCount = -1
        });

        Assert.Equal(100, Factor(score, "cpu").Score);
        Assert.Equal(100, Factor(score, "memory").Score);
        Assert.Equal(100, Factor(score, "disk-space").Score);
        Assert.Equal(100, Factor(score, "cleanup").Score);
        Assert.DoesNotContain(score.Factors, factor => factor.Id == "processes");
        Assert.Equal(100, score.Total);
    }

    [Theory]
    [InlineData(100, ScoreCategory.Excellent)]
    [InlineData(85, ScoreCategory.Excellent)]
    [InlineData(84, ScoreCategory.Good)]
    [InlineData(70, ScoreCategory.Good)]
    [InlineData(69, ScoreCategory.Attention)]
    [InlineData(50, ScoreCategory.Attention)]
    [InlineData(49, ScoreCategory.Critical)]
    public void Classify_RespeitaOsLimitesPublicados(int total, ScoreCategory expected)
    {
        Assert.Equal(expected, HealthScore.Classify(total));
    }

    [Fact]
    public void GetCategories_RetornaCategoriasDaMelhorParaAPior()
    {
        Assert.Equal(
            new[] { ScoreCategory.Excellent, ScoreCategory.Good, ScoreCategory.Attention, ScoreCategory.Critical },
            _service.GetCategories());
    }

    private static ScoreFactor Factor(HealthScore score, string id) =>
        Assert.Single(score.Factors, factor => factor.Id == id);

    private static VolumeInfo Volume(long totalGigabytes, long freeGigabytes, bool isSystemDrive) => new()
    {
        TotalBytes = totalGigabytes * 1024 * 1024 * 1024,
        FreeBytes = freeGigabytes * 1024 * 1024 * 1024,
        IsSystemDrive = isSystemDrive
    };

    private static StartupEntry Startup(bool enabled, StartupImpact impact) => new()
    {
        IsEnabled = enabled,
        Impact = impact
    };
}
