using OptimizerPC.Core.Security;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Segunda barreira da limpeza. Alem da validacao padrao de caminhos, recusa qualquer
/// pasta que seja area critica do Windows, diretorio de programas, area pessoal do
/// usuario ou raiz de unidade. Funciona como protecao independente do catalogo:
/// mesmo que uma entrada fosse adicionada por engano, ela nao seria limpa.
/// </summary>
internal static class CleanupSafety
{
    private static readonly string[] ForbiddenWindowsSubfolders =
    {
        "System32", "SysWOW64", "WinSxS", "Boot", "Fonts", "assembly",
        "Microsoft.NET", "servicing", "INF", "Speech", "SystemApps", "ImmersiveControlPanel"
    };

    private static readonly Lazy<string[]> LazyForbiddenRoots = new(BuildForbiddenRoots);

    internal static IReadOnlyList<string> ForbiddenRoots => LazyForbiddenRoots.Value;

    internal static bool IsAllowedLocation(string? path)
    {
        var normalized = ProtectedPaths.Normalize(path ?? string.Empty);
        if (normalized.Length == 0)
        {
            return false;
        }

        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var relative = normalized[root.Length..].Trim(Path.DirectorySeparatorChar);
        if (relative.Length == 0)
        {
            return false;
        }

        // Exige pelo menos dois niveis (ex.: C:\Windows\Temp, e nunca C:\Windows).
        if (relative.Split(Path.DirectorySeparatorChar).Length < 2)
        {
            return false;
        }

        if (ProtectedPaths.IsUserPersonalPath(normalized))
        {
            return false;
        }

        if (ForbiddenRoots.Any(forbidden => ProtectedPaths.IsUnder(normalized, forbidden)))
        {
            return false;
        }

        var windows = ProtectedPaths.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (windows.Length > 0 && ProtectedPaths.IsUnder(normalized, windows))
        {
            var underWindows = normalized[windows.Length..].Trim(Path.DirectorySeparatorChar);
            var firstSegment = underWindows.Split(Path.DirectorySeparatorChar)[0];
            if (ForbiddenWindowsSubfolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] BuildForbiddenRoots()
    {
        var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

        var roots = new List<string>
        {
            Path.Combine(drive, "Boot"),
            Path.Combine(drive, "EFI"),
            Path.Combine(drive, "Recovery"),
            Path.Combine(drive, "System Volume Information"),
            Path.Combine(drive, "$Recycle.Bin"),
            Path.Combine(drive, "PerfLogs")
        };

        // ProgramData nao entra na lista: os relatorios de erro do Windows ficam la e
        // sao um alvo legitimo de limpeza. Cada local e autorizado individualmente.
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            var value = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(value) is false)
            {
                roots.Add(value);
            }
        }

        return roots.Where(value => value.Length > 0).ToArray();
    }
}
