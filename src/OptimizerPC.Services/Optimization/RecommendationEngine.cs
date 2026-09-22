using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Optimization;

/// <summary>
/// Motor de recomendações da Smart Optimization. Somente leitura: analisa os dados
/// já coletados e propõe ações com motivo, impacto estimado e risco. Ações que
/// dependem de uma escolha do usuário (qual item de inicialização desativar, qual
/// processo encerrar) chegam sem execução automática e apontam para a tela
/// correspondente.
/// </summary>
public sealed class RecommendationEngine : IRecommendationEngine
{
    private const long HundredMegabytes = 100L * 1024 * 1024;
    private const long QuarterGigabyte = 300L * 1024 * 1024;
    private const long HalfGigabyte = 500L * 1024 * 1024;
    private const long Gigabyte = 1024L * 1024 * 1024;

    private readonly ILocalizer _localizer;

    public RecommendationEngine(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public IReadOnlyList<Recommendation> Build(RecommendationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var recommendations = new List<Recommendation>(12);
        AddCleanupRules(recommendations, context);
        AddMemoryAndCpuRules(recommendations, context);
        AddStartupRule(recommendations, context);
        AddServiceRule(recommendations, context);
        AddPowerPlanRule(recommendations, context);
        AddVisualEffectsRule(recommendations, context);
        AddDiskSpaceRule(recommendations, context);
        AddDriveHealthRule(recommendations, context);
        AddLargeFolderRule(recommendations, context);

        return recommendations
            .OrderByDescending(item => item.Impact)
            .ThenByDescending(item => item.EstimatedGainBytes)
            .ToArray();
    }

    private static void AddCleanupRules(List<Recommendation> recommendations, RecommendationContext context)
    {
        var targets = context.CleanupTargets.Where(target => target.IsAvailable).ToList();

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.UserTemp, CleanupCategory.WindowsTemp),
            QuarterGigabyte,
            "Recommendation.TemporaryFiles",
            RecommendationKind.CleanTemporaryFiles,
            ImpactLevel.Medium,
            RiskLevel.Low,
            nameof(OptimizationActionKind.CleanTemporaryFiles));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.BrowserCaches),
            QuarterGigabyte,
            "Recommendation.BrowserCache",
            RecommendationKind.CleanTemporaryFiles,
            ImpactLevel.Medium,
            RiskLevel.Low,
            nameof(OptimizationActionKind.CleanBrowserCache));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.ThumbnailCache),
            HundredMegabytes,
            "Recommendation.Thumbnails",
            RecommendationKind.CleanTemporaryFiles,
            ImpactLevel.Low,
            RiskLevel.Low,
            nameof(OptimizationActionKind.CleanThumbnails));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.WindowsErrorReports),
            50L * 1024 * 1024,
            "Recommendation.ErrorReports",
            RecommendationKind.FreeUpSpace,
            ImpactLevel.Low,
            RiskLevel.Low,
            nameof(OptimizationActionKind.CleanErrorReports));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.WindowsUpdateCache),
            Gigabyte,
            "Recommendation.UpdateCache",
            RecommendationKind.FreeUpSpace,
            ImpactLevel.Medium,
            RiskLevel.Medium,
            nameof(OptimizationActionKind.CleanUpdateCache));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.OldLogs, CleanupCategory.InstallerResidue),
            200L * 1024 * 1024,
            "Recommendation.OldLogs",
            RecommendationKind.FreeUpSpace,
            ImpactLevel.Low,
            RiskLevel.Low,
            nameof(OptimizationActionKind.CleanOldLogs));

        AddCleanup(
            recommendations,
            SumOf(targets, CleanupCategory.RecycleBin),
            HalfGigabyte,
            "Recommendation.RecycleBin",
            RecommendationKind.EmptyRecycleBin,
            ImpactLevel.Medium,
            RiskLevel.Medium,
            nameof(OptimizationActionKind.EmptyRecycleBin));
    }

    private static void AddCleanup(
        List<Recommendation> recommendations,
        long bytes,
        long minimum,
        string keyPrefix,
        RecommendationKind kind,
        ImpactLevel impact,
        RiskLevel risk,
        string actionName)
    {
        if (bytes < minimum)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec." + actionName,
            Kind = kind,
            TitleKey = keyPrefix + ".Title",
            DescriptionKey = keyPrefix + ".Description",
            ReasonKey = keyPrefix + ".Reason",
            Detail = Humanize.Bytes(bytes),
            Impact = bytes >= 5 * Gigabyte ? ImpactLevel.High : impact,
            Risk = risk,
            EstimatedGainBytes = bytes,
            ActionId = actionName
        });
    }

    private static long SumOf(IReadOnlyList<CleanupTarget> targets, params CleanupCategory[] categories)
        => targets.Where(target => categories.Contains(target.Category)).Sum(target => target.TotalBytes);

    private void AddMemoryAndCpuRules(List<Recommendation> recommendations, RecommendationContext context)
    {
        if (context.ScoreInput.Memory is { TotalBytes: > 0 } memory && memory.UsedPercent >= 85)
        {
            recommendations.Add(new Recommendation
            {
                Id = "rec.high-memory",
                Kind = RecommendationKind.HighMemoryProcess,
                TitleKey = "Recommendation.HighMemory.Title",
                DescriptionKey = "Recommendation.HighMemory.Description",
                ReasonKey = "Recommendation.HighMemory.Reason",
                Detail = Humanize.Bytes(memory.UsedBytes) + " / " + Humanize.Bytes(memory.TotalBytes) + " (" + Humanize.Percent(memory.UsedPercent) + ")",
                Impact = ImpactLevel.High,
                Risk = RiskLevel.Low
            });
        }

        if (context.ScoreInput.CpuUsagePercent is double cpu && cpu >= 80)
        {
            recommendations.Add(new Recommendation
            {
                Id = "rec.high-cpu",
                Kind = RecommendationKind.HighCpuProcess,
                TitleKey = "Recommendation.HighCpu.Title",
                DescriptionKey = "Recommendation.HighCpu.Description",
                ReasonKey = "Recommendation.HighCpu.Reason",
                Detail = Humanize.Percent(cpu, 1),
                Impact = ImpactLevel.High,
                Risk = RiskLevel.Low
            });
        }
    }

    private static void AddStartupRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var heavy = context.StartupEntries
            .Where(entry => entry.IsEnabled && entry.Impact == StartupImpact.High)
            .ToList();

        if (heavy.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", heavy.Take(5).Select(entry => entry.Name));
        if (heavy.Count > 5)
        {
            names += " (+" + (heavy.Count - 5) + ")";
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.startup",
            Kind = RecommendationKind.ReduceStartupPrograms,
            TitleKey = "Recommendation.Startup.Title",
            DescriptionKey = "Recommendation.Startup.Description",
            ReasonKey = "Recommendation.Startup.Reason",
            Detail = names,
            Impact = heavy.Count >= 2 ? ImpactLevel.High : ImpactLevel.Medium,
            Risk = RiskLevel.Low
        });
    }

    private void AddServiceRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var thirdParty = context.Services.Count(service =>
            service.IsMicrosoft is false &&
            service.IsSystemCritical is false &&
            service.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed);

        if (thirdParty <= 8)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.services",
            Kind = RecommendationKind.ReviewBackgroundServices,
            TitleKey = "Recommendation.Services.Title",
            DescriptionKey = "Recommendation.Services.Description",
            ReasonKey = "Recommendation.Services.Reason",
            Detail = _localizer.Format("Recommendation.Services.DetailValue", thirdParty),
            Impact = ImpactLevel.Medium,
            Risk = RiskLevel.Medium
        });
    }

    private static void AddPowerPlanRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var active = context.PowerPlans.FirstOrDefault(plan => plan.IsActive);
        if (active is null)
        {
            return;
        }

        var highPerformance = context.PowerPlans.FirstOrDefault(plan => plan.Kind == PowerPlanKind.HighPerformance);
        if (highPerformance is null || active.Kind != PowerPlanKind.PowerSaver)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.power-plan",
            Kind = RecommendationKind.ChangePowerPlan,
            TitleKey = "Recommendation.PowerPlan.Title",
            DescriptionKey = "Recommendation.PowerPlan.Description",
            ReasonKey = "Recommendation.PowerPlan.Reason",
            Detail = highPerformance.Name,
            Impact = ImpactLevel.Medium,
            Risk = RiskLevel.Low,
            ActionId = nameof(OptimizationActionKind.SetPowerPlan)
        });
    }

    private static void AddVisualEffectsRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        if (context.VisualEffectsOptimized || context.ScoreInput.IsLowEndHardware is false)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.visual-effects",
            Kind = RecommendationKind.DisableVisualEffects,
            TitleKey = "Recommendation.VisualEffects.Title",
            DescriptionKey = "Recommendation.VisualEffects.Description",
            ReasonKey = "Recommendation.VisualEffects.Reason",
            Impact = ImpactLevel.Medium,
            Risk = RiskLevel.Low,
            ActionId = nameof(OptimizationActionKind.DisableVisualEffects)
        });
    }

    private void AddDiskSpaceRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var volume = context.ScoreInput.Volumes.FirstOrDefault(v => v.IsSystemDrive)
                     ?? context.ScoreInput.Volumes.FirstOrDefault();

        if (volume is not { TotalBytes: > 0 } || volume.FreePercent >= 10)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.low-disk",
            Kind = RecommendationKind.LowDiskSpace,
            TitleKey = "Recommendation.LowDiskSpace.Title",
            DescriptionKey = "Recommendation.LowDiskSpace.Description",
            ReasonKey = "Recommendation.LowDiskSpace.Reason",
            Detail = Humanize.Bytes(volume.FreeBytes) + " / " + Humanize.Bytes(volume.TotalBytes),
            Impact = ImpactLevel.High,
            Risk = RiskLevel.Low
        });

        var isSolidState = IsSystemDriveSolidState(context);
        recommendations.Add(new Recommendation
        {
            Id = "rec.trim",
            Kind = RecommendationKind.DefragmentOrTrim,
            TitleKey = isSolidState ? "Recommendation.Trim.Title" : "Recommendation.Defrag.Title",
            DescriptionKey = isSolidState ? "Recommendation.Trim.Description" : "Recommendation.Defrag.Description",
            ReasonKey = "Recommendation.Trim.Reason",
            Impact = ImpactLevel.Medium,
            Risk = RiskLevel.Medium,
            ActionId = isSolidState ? nameof(OptimizationActionKind.TrimVolume) : null
        });
    }

    private static bool IsSystemDriveSolidState(RecommendationContext context)
    {
        var devices = context.Snapshot?.StorageDevices;
        if (devices is null || devices.Count == 0)
        {
            return false;
        }

        return devices.Any(device => device.MediaType is StorageMediaType.Ssd or StorageMediaType.ScmOrNvme);
    }

    private static void AddDriveHealthRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var affected = context.ScoreInput.DriveHealth
            .Where(report => report.Status is DriveHealthStatus.Warning or DriveHealthStatus.Failing)
            .ToList();

        if (affected.Count == 0)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.disk-integrity",
            Kind = RecommendationKind.CheckDiskIntegrity,
            TitleKey = "Recommendation.DiskIntegrity.Title",
            DescriptionKey = "Recommendation.DiskIntegrity.Description",
            ReasonKey = "Recommendation.DiskIntegrity.Reason",
            Detail = string.Join(", ", affected.Select(report => report.Model ?? report.DeviceName)),
            Impact = ImpactLevel.High,
            Risk = RiskLevel.Low
        });
    }

    private static void AddLargeFolderRule(List<Recommendation> recommendations, RecommendationContext context)
    {
        var downloads = context.StorageUsage.FirstOrDefault(usage => usage.Category == StorageCategory.Downloads);
        if (downloads is null || downloads.Bytes < 5 * Gigabyte)
        {
            return;
        }

        recommendations.Add(new Recommendation
        {
            Id = "rec.downloads",
            Kind = RecommendationKind.FreeUpSpace,
            TitleKey = "Recommendation.Downloads.Title",
            DescriptionKey = "Recommendation.Downloads.Description",
            ReasonKey = "Recommendation.Downloads.Reason",
            Detail = Humanize.Bytes(downloads.Bytes),
            Impact = ImpactLevel.Medium,
            Risk = RiskLevel.Medium
        });
    }
}
