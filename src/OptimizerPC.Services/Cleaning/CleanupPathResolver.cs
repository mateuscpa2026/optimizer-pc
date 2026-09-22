using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Cleaning;

/// <summary>Local do catalogo resolvido no disco e confirmado como existente.</summary>
internal sealed record ResolvedCleanupLocation(CleanupLocation Location, string Path);

/// <summary>
/// Transforma os modelos do catalogo em caminhos reais. Somente leitura: verifica
/// existencia, expande variaveis de ambiente e o segmento curinga dos perfis de
/// navegador, e descarta qualquer caminho recusado pela barreira de seguranca.
/// </summary>
internal static class CleanupPathResolver
{
    private const int MaxWildcardExpansions = 256;

    internal static IReadOnlyList<ResolvedCleanupLocation> Resolve(IFileSystemService files, CleanupTargetDescriptor descriptor)
    {
        var resolved = new List<ResolvedCleanupLocation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in descriptor.Locations)
        {
            foreach (var path in ExpandTemplate(files, location.Template))
            {
                if (CleanupSafety.IsAllowedLocation(path) is false)
                {
                    continue;
                }

                var exists = location.IsSingleFile ? files.FileExists(path) : files.DirectoryExists(path);
                if (exists is false || seen.Add(RuleKey(path, location)) is false)
                {
                    continue;
                }

                resolved.Add(new ResolvedCleanupLocation(location, path));
            }
        }

        return resolved;
    }

    // O mesmo local pode carregar mais de uma regra de nome (cache de miniaturas usa
    // thumbcache_*.db e iconcache_*.db): a deduplicacao precisa considerar a regra,
    // senao a segunda nunca seria aplicada.
    private static string RuleKey(string path, CleanupLocation location) =>
        path + "|" + location.FilePattern + "|" + location.MinAgeDays + "|" + location.IsSingleFile;

    /// <summary>Converte o modelo (com variaveis e eventual curinga) em caminhos existentes.</summary>
    private static IEnumerable<string> ExpandTemplate(IFileSystemService files, string template)
    {
        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(template);
        }
        catch (Exception)
        {
            yield break;
        }

        // Uma variavel nao resolvida deixaria o caminho invalido.
        if (expanded.Length == 0 || expanded.Contains('%'))
        {
            yield break;
        }

        if (expanded.Contains('*') is false)
        {
            yield return expanded;
            yield break;
        }

        var root = Path.GetPathRoot(expanded);
        if (string.IsNullOrEmpty(root))
        {
            yield break;
        }

        var segments = expanded[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { root.TrimEnd(Path.DirectorySeparatorChar) };

        foreach (var segment in segments)
        {
            var next = new List<string>();
            foreach (var prefix in current)
            {
                if (segment == "*")
                {
                    if (files.DirectoryExists(prefix) is false)
                    {
                        continue;
                    }

                    next.AddRange(files.EnumerateDirectories(prefix).Take(MaxWildcardExpansions));
                }
                else
                {
                    next.Add(Path.Combine(prefix, segment));
                }
            }

            current = next;
            if (current.Count == 0)
            {
                break;
            }
        }

        foreach (var path in current.Take(MaxWildcardExpansions))
        {
            yield return path;
        }
    }
}
