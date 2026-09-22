using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Cleaning;

/// <summary>
/// Monta a lista de alvos de limpeza a partir de identificadores do catalogo interno.
/// A otimizacao automatica e o modo PC Fraco usam este ponto para limpar somente alvos
/// conhecidos: nenhum caminho vindo da interface e aceito.
/// </summary>
internal static class CleanupTargetSelection
{
    internal static IReadOnlyList<CleanupTarget> FromIds(params string[] ids)
    {
        var targets = new List<CleanupTarget>(ids.Length);

        foreach (var id in ids)
        {
            var descriptor = CleanupTargetCatalog.Find(id);
            if (descriptor is null)
            {
                continue;
            }

            targets.Add(new CleanupTarget
            {
                Id = descriptor.Id,
                Category = descriptor.Category,
                TitleKey = descriptor.TitleKey,
                DescriptionKey = descriptor.DescriptionKey,
                Risk = descriptor.Risk,
                Elevation = descriptor.Elevation,
                RequiresConfirmation = descriptor.RequiresConfirmation,
                IsSelected = true
            });
        }

        return targets;
    }
}
