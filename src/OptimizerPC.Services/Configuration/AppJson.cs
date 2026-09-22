using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OptimizerPC.Services.Configuration;

/// <summary>
/// Opcoes JSON compartilhadas pelo aplicativo. Propriedades calculadas (somente leitura)
/// nao sao gravadas: os arquivos ficam enxutos e podem ser relidos sem ambiguidade.
/// </summary>
internal static class AppJson
{
    /// <summary>
    /// Opcoes compartilhadas. Projecoes anonimas (exportacoes) sao integralmente somente leitura:
    /// nelas o filtro precisa ser desligado para nao gerar objetos vazios.
    /// </summary>
    internal static JsonSerializerOptions CreateOptions(bool indented, bool skipReadOnlyProperties = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        if (skipReadOnlyProperties)
        {
            options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { SkipReadOnlyProperties }
            };
        }

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static void SkipReadOnlyProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.Set is null)
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }
}
