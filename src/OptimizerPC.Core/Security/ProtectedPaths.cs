using System.Runtime.Versioning;

namespace OptimizerPC.Core.Security;

/// <summary>
/// Lista de protecao com diretorios e arquivos que o Optimizer PC nunca remove,
/// move ou modifica. A validacao e baseada em caminhos normalizados e completos.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProtectedPaths
{
    private static readonly Lazy<string[]> LazyProtectedRoots = new(BuildProtectedRoots);

    private static readonly string[] ProtectedFileNames =
    {
        "bootmgr", "bootnxt", "ntldr", "bcd", "boot.ini", "pagefile.sys", "swapfile.sys",
        "hiberfil.sys", "ntuser.dat", "ntuser.ini", "ntuser.dat.log", "desktop.ini",
        "kernel32.dll", "ntdll.dll", "kernelbase.dll"
    };

    private static readonly string[] ProtectedExtensions =
    {
        ".sys", ".efi", ".drv", ".ocx", ".msc", ".cpl", ".dll", ".exe"
    };

    private static readonly string[] ProtectedUserRelativeRoots =
    {
        "Documents", "Desktop", "Pictures", "Videos", "Music", "Downloads", "Favorites",
        "OneDrive", "Saved Games", "Contacts", "Searches", "Links"
    };

    /// <summary>Raizes que jamais podem ser alvo de exclusao.</summary>
    public static IReadOnlyList<string> ProtectedRoots => LazyProtectedRoots.Value;

    /// <summary>Nomes de arquivo individuais protegidos.</summary>
    public static IReadOnlyList<string> ProtectedFileNamesList => ProtectedFileNames;

    /// <summary>
    /// Verifica se o caminho esta dentro de uma area protegida do sistema ou
    /// se e um arquivo critico individual.
    /// </summary>
    public static bool IsProtected(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var full = Normalize(path);
        if (full.Length == 0)
        {
            return true;
        }

        foreach (var root in ProtectedRoots)
        {
            if (IsUnder(full, root))
            {
                return true;
            }
        }

        var fileName = Path.GetFileName(full);
        return ProtectedFileNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Retorna verdadeiro quando o caminho pertence a uma pasta pessoal do usuario
    /// (documentos, imagens, videos, etc.). Essas pastas nao sao limpas automaticamente.
    /// </summary>
    public static bool IsUserPersonalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Normalize(path);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            return false;
        }

        foreach (var relative in ProtectedUserRelativeRoots)
        {
            var candidate = Normalize(Path.Combine(profile, relative));
            if (candidate.Length > 0 && IsUnderOrEqual(full, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Extensoes que o aplicativo considera de midia/documento pessoal.</summary>
    public static bool IsPersonalDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return ProtectedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) is false
               && PersonalDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] PersonalDocumentExtensions =
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".odt", ".ods", ".txt",
        ".rtf", ".csv", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".raw",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".zip", ".rar", ".7z", ".psd", ".ai", ".svg", ".epub", ".mobi"
    };

    /// <summary>Retorna verdadeiro quando <paramref name="candidate"/> esta dentro de <paramref name="root"/>.</summary>
    public static bool IsUnder(string candidate, string root)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedRoot = Normalize(root);
        if (normalizedCandidate.Length == 0 || normalizedRoot.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderOrEqual(string candidate, string root) => IsUnder(candidate, root);

    /// <summary>
    /// Normaliza o caminho (caminho absoluto + separadores padronizados) sem exigir
    /// que o arquivo exista e sem resolver links, para evitar bypass por reparse point.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var trimmed = path.Trim().Trim('"');
            var full = Path.GetFullPath(trimmed);
            full = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return full.TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string[] BuildProtectedRoots()
    {
        var roots = new List<string>();

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var candidate = value;
            if (Path.IsPathFullyQualified(candidate) is false)
            {
                candidate = Path.Combine(GetSystemDrive(), candidate.TrimStart('\\', '/'));
            }

            var normalized = Normalize(candidate);
            if (normalized.Length > 0 && roots.Contains(normalized, StringComparer.OrdinalIgnoreCase) is false)
            {
                roots.Add(normalized);
            }
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Path.Combine(GetSystemDrive(), "Windows"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "System32"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "SysWOW64"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "WinSxS"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "System32", "drivers"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "System32", "config"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "Boot"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "Fonts"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "assembly"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "Microsoft.NET"));
        Add(Path.Combine(GetSystemDrive(), "Windows", "servicing"));
        Add(Path.Combine(GetSystemDrive(), "Boot"));
        Add(Path.Combine(GetSystemDrive(), "EFI"));
        Add(Path.Combine(GetSystemDrive(), "Recovery"));
        Add(Path.Combine(GetSystemDrive(), "System Volume Information"));
        Add(Path.Combine(GetSystemDrive(), "$Recycle.Bin"));
        Add(Path.Combine(GetSystemDrive(), "PerfLogs"));

        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyVideos,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.Favorites
                 })
        {
            Add(Environment.GetFolderPath(folder));
        }

        return roots.ToArray();
    }

    private static string GetSystemDrive() =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
}
