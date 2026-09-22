using System.Runtime.Versioning;

namespace OptimizerPC.Core.Security;

/// <summary>Resultado da validacao de um caminho antes de qualquer operacao destrutiva.</summary>
public sealed record PathValidationResult(bool IsAllowed, string ReasonKey, string ResolvedPath)
{
    public static PathValidationResult Allow(string path) => new(true, "Security.Allowed", path);

    public static PathValidationResult Deny(string path, string reasonKey) => new(false, reasonKey, path);
}

/// <summary>Contexto que define onde uma exclusao e permitida.</summary>
public sealed record SafeDeleteContext(
    IReadOnlyList<string> AllowedRoots,
    bool AllowFiles = true,
    bool AllowDirectories = true,
    bool AllowPersonalContent = false,
    bool AllowProtectedSystemPaths = false);

/// <summary>
/// Porta unica para autorizar exclusoes. Nenhum servico de limpeza remove arquivos
/// sem passar por aqui: o caminho precisa estar dentro de uma raiz permitida e fora
/// de qualquer area protegida do sistema ou de conteudo pessoal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SafePathValidator
{
    public PathValidationResult ValidateForDeletion(string path, SafeDeleteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var normalized = ProtectedPaths.Normalize(path);
        if (normalized.Length == 0)
        {
            return PathValidationResult.Deny(path ?? string.Empty, "Security.Reason.InvalidPath");
        }

        if (context.AllowProtectedSystemPaths is false && ProtectedPaths.IsProtected(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.ProtectedPath");
        }

        if (context.AllowPersonalContent is false && ProtectedPaths.IsUserPersonalPath(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.PersonalContent");
        }

        if (context.AllowPersonalContent is false && ProtectedPaths.IsPersonalDocument(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.PersonalDocument");
        }

        if (context.AllowedRoots.Count == 0)
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.NoAllowedRoots");
        }

        var insideAllowedRoot = context.AllowedRoots.Any(root => ProtectedPaths.IsUnder(normalized, root));
        if (insideAllowedRoot is false)
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.OutsideAllowedRoots");
        }

        if (IsRootItself(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.DriveRoot");
        }

        if (context.AllowFiles is false && File.Exists(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.FilesNotAllowed");
        }

        if (context.AllowDirectories is false && Directory.Exists(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.DirectoriesNotAllowed");
        }

        if (IsReparsePoint(normalized))
        {
            return PathValidationResult.Deny(normalized, "Security.Reason.ReparsePoint");
        }

        return PathValidationResult.Allow(normalized);
    }

    /// <summary>Confirma que a raiz informada e valida para uso como origem de limpeza.</summary>
    public static bool IsUsableCleanupRoot(string root)
    {
        var normalized = ProtectedPaths.Normalize(root);
        if (normalized.Length == 0 || IsRootItself(normalized))
        {
            return false;
        }

        return Directory.Exists(normalized);
    }

    private static bool IsRootItself(string normalized)
    {
        var root = Path.GetPathRoot(normalized);
        return string.Equals(normalized, root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
