using OptimizerPC.Core;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Local declarado pelo aplicativo como origem de limpeza. O modelo aceita variaveis
/// de ambiente e, no maximo, um segmento curinga (usado nos perfis de navegador).
/// </summary>
internal sealed record CleanupLocation(
    string Template,
    bool IsSingleFile = false,
    string? FilePattern = null,
    int MinAgeDays = 0);

/// <summary>
/// Descricao de um alvo de limpeza. O catalogo e fixo e vive no codigo: a limpeza
/// resolve o alvo pelo identificador e ignora qualquer caminho informado pela interface.
/// Uma lista adulterada nao consegue, portanto, apontar a exclusao para fora das areas
/// previstas neste arquivo.
/// </summary>
internal sealed record CleanupTargetDescriptor(
    string Id,
    CleanupCategory Category,
    string TitleKey,
    string DescriptionKey,
    IReadOnlyList<CleanupLocation> Locations,
    RiskLevel Risk = RiskLevel.Low,
    ElevationRequirement Elevation = ElevationRequirement.None,
    bool RequiresConfirmation = false,
    bool SelectedByDefault = true,
    bool IsRecycleBin = false);

/// <summary>
/// Lista branca de locais de limpeza. Cada entrada aponta apenas para pastas de cache,
/// temporarios, relatorios de erro e logs de manutencao do proprio Windows.
/// Nunca aponta para pastas de documentos, Areas de Trabalho, unidades inteiras
/// ou areas criticas do sistema.
/// </summary>
internal static class CleanupTargetCatalog
{
    internal const string RecycleBinId = "recycle-bin";

    private static readonly Lazy<IReadOnlyList<CleanupTargetDescriptor>> LazyTargets = new(Build);

    internal static IReadOnlyList<CleanupTargetDescriptor> Targets => LazyTargets.Value;

    internal static CleanupTargetDescriptor? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Targets.FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<CleanupTargetDescriptor> Build()
    {
        const string windows = "%SystemRoot%";

        return new[]
        {
            new CleanupTargetDescriptor(
                "user-temp",
                CleanupCategory.UserTemp,
                "Cleanup.Target.UserTemp.Title",
                "Cleanup.Target.UserTemp.Description",
                new[] { new CleanupLocation("%TEMP%") }),

            new CleanupTargetDescriptor(
                "windows-temp",
                CleanupCategory.WindowsTemp,
                "Cleanup.Target.WindowsTemp.Title",
                "Cleanup.Target.WindowsTemp.Description",
                new[] { new CleanupLocation(windows + "\\Temp") },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "thumbnail-cache",
                CleanupCategory.ThumbnailCache,
                "Cleanup.Target.Thumbnails.Title",
                "Cleanup.Target.Thumbnails.Description",
                new[]
                {
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer", FilePattern: "thumbcache_*.db"),
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer", FilePattern: "iconcache_*.db")
                }),

            new CleanupTargetDescriptor(
                RecycleBinId,
                CleanupCategory.RecycleBin,
                "Cleanup.Target.RecycleBin.Title",
                "Cleanup.Target.RecycleBin.Description",
                Array.Empty<CleanupLocation>(),
                Risk: RiskLevel.Medium,
                RequiresConfirmation: true,
                SelectedByDefault: false,
                IsRecycleBin: true),

            new CleanupTargetDescriptor(
                "error-reports",
                CleanupCategory.WindowsErrorReports,
                "Cleanup.Target.ErrorReports.Title",
                "Cleanup.Target.ErrorReports.Description",
                new[]
                {
                    new CleanupLocation("%ProgramData%\\Microsoft\\Windows\\WER\\ReportQueue"),
                    new CleanupLocation("%ProgramData%\\Microsoft\\Windows\\WER\\ReportArchive"),
                    new CleanupLocation("%ProgramData%\\Microsoft\\Windows\\WER\\Temp"),
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\WER")
                },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "delivery-optimization",
                CleanupCategory.DeliveryOptimization,
                "Cleanup.Target.DeliveryOptimization.Title",
                "Cleanup.Target.DeliveryOptimization.Description",
                new[]
                {
                    new CleanupLocation(windows + "\\ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache")
                },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "windows-update-cache",
                CleanupCategory.WindowsUpdateCache,
                "Cleanup.Target.WindowsUpdateCache.Title",
                "Cleanup.Target.WindowsUpdateCache.Description",
                new[] { new CleanupLocation(windows + "\\SoftwareDistribution\\Download") },
                Risk: RiskLevel.Medium,
                Elevation: ElevationRequirement.Required,
                RequiresConfirmation: true,
                SelectedByDefault: false),

            new CleanupTargetDescriptor(
                "prefetch",
                CleanupCategory.PrefetchData,
                "Cleanup.Target.Prefetch.Title",
                "Cleanup.Target.Prefetch.Description",
                new[] { new CleanupLocation(windows + "\\Prefetch", FilePattern: "*.pf") },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "crash-dumps",
                CleanupCategory.CrashDumps,
                "Cleanup.Target.CrashDumps.Title",
                "Cleanup.Target.CrashDumps.Description",
                new[]
                {
                    new CleanupLocation("%LOCALAPPDATA%\\CrashDumps"),
                    new CleanupLocation(windows + "\\Minidump"),
                    new CleanupLocation(windows + "\\LiveKernelReports"),
                    new CleanupLocation(windows + "\\MEMORY.DMP", IsSingleFile: true)
                },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "component-logs",
                CleanupCategory.InstallerResidue,
                "Cleanup.Target.ComponentLogs.Title",
                "Cleanup.Target.ComponentLogs.Description",
                new[]
                {
                    new CleanupLocation(windows + "\\Logs\\CBS", MinAgeDays: 7),
                    new CleanupLocation(windows + "\\Logs\\DISM", MinAgeDays: 7),
                    new CleanupLocation("%LOCALAPPDATA%\\Downloaded Installations")
                },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "maintenance-logs",
                CleanupCategory.OldLogs,
                "Cleanup.Target.MaintenanceLogs.Title",
                "Cleanup.Target.MaintenanceLogs.Description",
                new[]
                {
                    new CleanupLocation(windows + "\\Logs\\WindowsUpdate", MinAgeDays: 7),
                    new CleanupLocation(windows + "\\Logs\\waasmedic", MinAgeDays: 7),
                    new CleanupLocation(windows + "\\Logs\\NetSetup", MinAgeDays: 7)
                },
                Elevation: ElevationRequirement.Required),

            new CleanupTargetDescriptor(
                "browser-caches",
                CleanupCategory.BrowserCaches,
                "Cleanup.Target.BrowserCaches.Title",
                "Cleanup.Target.BrowserCaches.Description",
                new[]
                {
                    new CleanupLocation("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache\\Cache_Data"),
                    new CleanupLocation("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache"),
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Cache\\Cache_Data"),
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Code Cache"),
                    new CleanupLocation("%LOCALAPPDATA%\\BraveSoftware\\Brave-Browser\\User Data\\*\\Cache\\Cache_Data"),
                    new CleanupLocation("%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2")
                }),

            new CleanupTargetDescriptor(
                "application-caches",
                CleanupCategory.ApplicationCaches,
                "Cleanup.Target.ApplicationCaches.Title",
                "Cleanup.Target.ApplicationCaches.Description",
                new[]
                {
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache"),
                    new CleanupLocation("%LOCALAPPDATA%\\Microsoft\\Windows\\Caches")
                }),

            new CleanupTargetDescriptor(
                "font-cache",
                CleanupCategory.FontCache,
                "Cleanup.Target.FontCache.Title",
                "Cleanup.Target.FontCache.Description",
                new[]
                {
                    new CleanupLocation(windows + "\\ServiceProfiles\\LocalService\\AppData\\Local\\FontCache")
                },
                Elevation: ElevationRequirement.Required)
        };
    }
}
