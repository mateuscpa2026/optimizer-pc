using System.Runtime.Versioning;

namespace OptimizerPC.Core.Security;

/// <summary>
/// Regra unica que decide se um arquivo pode ser enviado para a Lixeira.
/// A interface usa esta regra para habilitar (ou explicar) cada caixa de marcar e o
/// servico usa a mesma regra para autorizar a operacao, de modo que as duas pontas
/// nunca divergem. Areas do sistema e conteudo pessoal sao recusados por padrao.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RecycleBinGuard
{
    public static bool TryAuthorize(
        string? path,
        IReadOnlyList<string> allowedRoots,
        SafePathValidator validator,
        out string normalizedPath,
        out string reasonKey)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        normalizedPath = ProtectedPaths.Normalize(path ?? string.Empty);
        reasonKey = string.Empty;

        if (normalizedPath.Length == 0)
        {
            reasonKey = "Security.Reason.InvalidPath";
            return false;
        }

        var fileName = Path.GetFileName(normalizedPath);
        if (ProtectedPaths.ProtectedFileNamesList.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            reasonKey = "Security.Reason.ProtectedFile";
            return false;
        }

        // A raiz da unidade e recusada aqui: o validador renormaliza o caminho e,
        // para uma raiz sem barra final, ele seria resolvido para o diretorio atual.
        var volumeRoot = Path.GetPathRoot(normalizedPath);
        if (string.Equals(normalizedPath, volumeRoot?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            reasonKey = "Security.Reason.DriveRoot";
            return false;
        }

        var context = new SafeDeleteContext(
            allowedRoots,
            AllowFiles: true,
            AllowDirectories: false,
            AllowPersonalContent: false,
            AllowProtectedSystemPaths: false);

        var validation = validator.ValidateForDeletion(normalizedPath, context);
        if (validation.IsAllowed)
        {
            return true;
        }

        normalizedPath = validation.ResolvedPath.Length > 0 ? validation.ResolvedPath : normalizedPath;
        reasonKey = validation.ReasonKey;
        return false;
    }
}
