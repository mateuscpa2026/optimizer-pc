using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Optimization;

/// <summary>
/// Calcula o índice de saúde do computador a partir dos dados coletados.
/// É uma estimativa interna do Optimizer PC, baseada nos fatores listados, e não
/// uma medição científica. Fatores sem dados disponíveis saem da conta e os pesos
/// restantes são renormalizados, de modo que a pontuação nunca seja inventada.
/// </summary>
public sealed class HealthScoreService : IHealthScoreService
{
    private static readonly ScoreCategory[] Categories =
    {
        ScoreCategory.Excellent,
        ScoreCategory.Good,
        ScoreCategory.Attention,
        ScoreCategory.Critical
    };

    private readonly ILocalizer _localizer;

    public HealthScoreService(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public IReadOnlyList<ScoreCategory> GetCategories() => Categories;

    public HealthScore Calculate(HealthScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builders = new List<FactorBuilder>(8);
        AddCpu(builders, input);
        AddMemory(builders, input);
        AddSystemDriveSpace(builders, input);
        AddDriveHealth(builders, input);
        AddStartup(builders, input);
        AddServices(builders, input);
        AddRecoverableSpace(builders, input);
        AddProcesses(builders, input);

        var effectiveWeight = builders.Sum(builder => builder.RawWeight);
        if (effectiveWeight <= 0)
        {
            return new HealthScore { Total = 0, Category = ScoreCategory.Critical };
        }

        var factors = builders
            .Select(builder => new ScoreFactor
            {
                Id = builder.Id,
                TitleKey = builder.TitleKey,
                DetailKey = builder.DetailKey,
                DetailValue = builder.DetailValue,
                Weight = builder.RawWeight / effectiveWeight,
                Score = Math.Clamp(Math.Round(builder.Score, 1), 0, 100),
                Severity = SeverityOf(builder.Score)
            })
            .ToList();

        var total = Math.Clamp(
            (int)Math.Round(factors.Sum(factor => factor.Score * factor.Weight), MidpointRounding.AwayFromZero),
            0,
            100);

        return new HealthScore
        {
            Total = total,
            Category = HealthScore.Classify(total),
            Factors = factors
        };
    }

    private static void AddCpu(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.CpuUsagePercent is not double cpu)
        {
            return;
        }

        var usage = Math.Clamp(cpu, 0, 100);

        builders.Add(new FactorBuilder(
            "cpu",
            "Score.Factor.Cpu.Title",
            "Score.Factor.Cpu.Detail",
            Humanize.Percent(usage, 1),
            0.12,
            100 - usage));
    }

    private static void AddMemory(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.Memory is not { TotalBytes: > 0 } memory)
        {
            return;
        }

        var used = Math.Clamp(memory.UsedPercent, 0, 100);

        builders.Add(new FactorBuilder(
            "memory",
            "Score.Factor.Memory.Title",
            "Score.Factor.Memory.Detail",
            Humanize.Bytes(memory.UsedBytes) + " / " + Humanize.Bytes(memory.TotalBytes) + " (" + Humanize.Percent(used) + ")",
            0.18,
            100 - used));
    }

    private static void AddSystemDriveSpace(List<FactorBuilder> builders, HealthScoreInput input)
    {
        var volume = input.Volumes.FirstOrDefault(v => v.IsSystemDrive) ?? input.Volumes.FirstOrDefault();
        if (volume is not { TotalBytes: > 0 })
        {
            return;
        }

        var free = Math.Clamp(volume.FreePercent, 0, 100);
        var score = free switch
        {
            >= 25 => 100,
            >= 15 => 85,
            >= 10 => 70,
            >= 5 => 45,
            _ => 20
        };

        builders.Add(new FactorBuilder(
            "disk-space",
            "Score.Factor.DiskSpace.Title",
            "Score.Factor.DiskSpace.Detail",
            Humanize.Bytes(volume.FreeBytes) + " / " + Humanize.Bytes(volume.TotalBytes),
            0.20,
            score));
    }

    private void AddDriveHealth(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.DriveHealth.Count == 0)
        {
            return;
        }

        var known = input.DriveHealth
            .Select(report => report.Status)
            .Where(status => status is DriveHealthStatus.Healthy or DriveHealthStatus.Warning or DriveHealthStatus.Failing)
            .ToList();

        var detail = _localizer.Format("Score.Factor.DriveHealth.DetailValue", known.Count, input.DriveHealth.Count);

        if (known.Count == 0)
        {
            // Sem dados SMART o estado é desconhecido: nem penaliza, nem aprova.
            builders.Add(new FactorBuilder(
                "drive-health",
                "Score.Factor.DriveHealth.Title",
                "Score.Factor.DriveHealth.NoData",
                detail,
                0.20,
                85));
            return;
        }

        var worst = known.Max();
        var score = worst switch
        {
            DriveHealthStatus.Healthy => 100,
            DriveHealthStatus.Warning => 60,
            _ => 0
        };

        builders.Add(new FactorBuilder(
            "drive-health",
            "Score.Factor.DriveHealth.Title",
            "Score.Factor.DriveHealth.Detail",
            detail,
            0.20,
            score));
    }

    private void AddStartup(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.StartupEntries.Count == 0)
        {
            return;
        }

        var enabled = input.StartupEntries.Where(entry => entry.IsEnabled).ToList();
        var high = enabled.Count(entry => entry.Impact == StartupImpact.High);
        var medium = enabled.Count(entry => entry.Impact == StartupImpact.Medium);
        var excess = Math.Max(0, enabled.Count - 8);

        var score = Math.Clamp(100 - (high * 12) - (medium * 5) - (excess * 2), 15, 100);

        builders.Add(new FactorBuilder(
            "startup",
            "Score.Factor.Startup.Title",
            "Score.Factor.Startup.Detail",
            _localizer.Format("Score.Factor.Startup.DetailValue", enabled.Count, input.StartupEntries.Count, high),
            0.10,
            score));
    }

    private void AddServices(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.Services.Count == 0)
        {
            return;
        }

        var thirdPartyAutomatic = input.Services.Count(service =>
            service.IsMicrosoft is false &&
            service.IsSystemCritical is false &&
            service.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed);

        var score = Math.Clamp(100 - (Math.Max(0, thirdPartyAutomatic - 6) * 4), 40, 100);

        builders.Add(new FactorBuilder(
            "services",
            "Score.Factor.Services.Title",
            "Score.Factor.Services.Detail",
            _localizer.Format("Score.Factor.Services.DetailValue", thirdPartyAutomatic),
            0.05,
            score));
    }

    private void AddRecoverableSpace(List<FactorBuilder> builders, HealthScoreInput input)
    {
        var bytes = Math.Max(0, input.RecoverableBytes);
        var gigabytes = bytes / 1024.0 / 1024.0 / 1024.0;

        var score = gigabytes switch
        {
            < 0.5 => 100,
            < 2 => 88,
            < 5 => 76,
            < 20 => 62,
            _ => 45
        };

        builders.Add(new FactorBuilder(
            "cleanup",
            "Score.Factor.Cleanup.Title",
            "Score.Factor.Cleanup.Detail",
            _localizer.Format("Score.Factor.Cleanup.DetailValue", Humanize.Bytes(bytes)),
            0.10,
            score));
    }

    private void AddProcesses(List<FactorBuilder> builders, HealthScoreInput input)
    {
        if (input.RunningProcessCount <= 0)
        {
            return;
        }

        var count = input.RunningProcessCount;
        var score = count switch
        {
            <= 100 => 100,
            <= 150 => 90,
            <= 200 => 78,
            <= 260 => 66,
            _ => 50
        };

        builders.Add(new FactorBuilder(
            "processes",
            "Score.Factor.Processes.Title",
            "Score.Factor.Processes.Detail",
            _localizer.Format("Score.Factor.Processes.DetailValue", count),
            0.05,
            score));
    }

    private static Severity SeverityOf(double score) => score switch
    {
        >= 85 => Severity.Ok,
        >= 70 => Severity.Info,
        >= 45 => Severity.Warning,
        _ => Severity.Critical
    };

    private sealed record FactorBuilder(
        string Id,
        string TitleKey,
        string DetailKey,
        string? DetailValue,
        double RawWeight,
        double Score);
}
