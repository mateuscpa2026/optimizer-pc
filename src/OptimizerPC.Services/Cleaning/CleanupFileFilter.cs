using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Regras de selecao de arquivos de um local de limpeza (padrao de nome e idade minima).
/// Compartilhado entre a varredura e a limpeza para garantir que a interface mostre
/// exatamente o que sera removido.
/// </summary>
internal static class CleanupFileFilter
{
    internal static bool Matches(CleanupLocation location, FileEntry file)
    {
        if (string.IsNullOrEmpty(location.FilePattern) is false &&
            MatchesPattern(Path.GetFileName(file.Path), location.FilePattern) is false)
        {
            return false;
        }

        if (location.MinAgeDays > 0 && file.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-location.MinAgeDays))
        {
            return false;
        }

        return true;
    }

    /// <summary>Comparacao simples para os padroes usados no catalogo (com no maximo um "*").</summary>
    internal static bool MatchesPattern(string name, string pattern)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];

        return name.Length >= prefix.Length + suffix.Length
               && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
