using System.Text.Json;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.Configuration;

namespace OptimizerPC.Services.Restore;

/// <summary>Estado anterior de um valor de registro (usado ao alterar o modo de inicio de um servico).</summary>
internal sealed record RegistryValuePayload(
    string Hive,
    string SubKey,
    string Value,
    bool HadValue,
    string? PreviousString,
    int? PreviousInt);

/// <summary>Estado anterior do modo de inicio de um servico do Windows.</summary>
internal sealed record ServiceStartModePayload(
    string Hive,
    string SubKey,
    bool HadStart,
    int PreviousStart,
    bool HadDelayed,
    int PreviousDelayed);

/// <summary>Plano de energia ativo antes da troca.</summary>
internal sealed record PowerPlanPayload(string SchemeGuid, string SchemeName);

/// <summary>
/// Estado anterior de um valor de registro em qualquer formato suportado.
/// Usado em ajustes de preferencias do usuario (efeitos visuais, modo Gamer).
/// </summary>
internal sealed record RegistryBackupPayload(
    string Hive,
    string SubKey,
    string? Name,
    bool HadValue,
    string? Kind,
    string? StringValue,
    int? IntValue,
    string[]? MultiStringValue)
{
    internal RegistryValueData? ToValueData() => Kind is null
        ? null
        : new RegistryValueData(Hive, SubKey, Name, Kind, StringValue, IntValue, MultiStringValue);
}

/// <summary>Valor anterior de uma preferencia de aparencia do Windows.</summary>
internal sealed record VisualEffectValue(
    string SubKey,
    string Name,
    bool HadValue,
    string? Kind,
    string? StringValue,
    int? IntValue);

/// <summary>
/// Perfil de efeitos visuais do usuario antes do ajuste. Guarda cada valor lido para
/// que a restauracao devolva exatamente o que existia, inclusive valores ausentes.
/// </summary>
internal sealed record VisualEffectsPayload(
    bool OptimizedForPerformance,
    IReadOnlyList<VisualEffectValue> Values);

/// <summary>Serializa o estado anterior de um item de inicializacao para permitir a reversao.</summary>
internal sealed record StartupTogglePayload(
    string Hive,
    string SubKey,
    string Value,
    bool HadValue,
    string Previous,
    bool Applied);

/// <summary>
/// Ponto unico de serializacao dos pontos de reversao. Cada tipo de alteracao grava
/// apenas o estado anterior necessario para desfazer a operacao.
/// </summary>
internal static class RestorePayload
{
    public static string ForRegistryInt(string hive, string subKey, string valueName, int? previous) =>
        Serialize(new RegistryValuePayload(hive, subKey, valueName, previous.HasValue, null, previous));

    public static string ForServiceStartMode(string hive, string subKey, int? start, int? delayed) =>
        Serialize(new ServiceStartModePayload(hive, subKey, start.HasValue, start ?? 0, delayed.HasValue, delayed ?? 0));

    public static string ForPowerPlan(Guid scheme, string name) =>
        Serialize(new PowerPlanPayload(scheme.ToString("D"), name));

    public static string ForRegistryBackup(RegistryHiveKind hive, string subKey, string? name, RegistryValueData? previous) =>
        Serialize(new RegistryBackupPayload(
            hive.ToString(),
            subKey,
            name,
            previous is not null,
            previous?.Kind,
            previous?.StringValue,
            previous?.IntValue,
            previous?.MultiStringValue));

    public static string ForToggle(string hive, string subKey, string valueName, byte[]? previous, bool enabled) =>
        Serialize(new StartupTogglePayload(
            hive,
            subKey,
            valueName,
            previous is not null,
            previous is null ? string.Empty : Convert.ToBase64String(previous),
            enabled));

    public static string ForVisualEffects(bool optimizedForPerformance, IReadOnlyList<VisualEffectValue> values) =>
        Serialize(new VisualEffectsPayload(optimizedForPerformance, values));

    /// <summary>
    /// Verifica se um payload contem determinada propriedade. Os registros de reversao
    /// guardam o estado anterior em formatos diferentes e o despachante precisa
    /// reconhecer o formato antes de desserializar.
    /// </summary>
    public static bool HasProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(propertyName, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static RegistryValuePayload? ReadRegistryValue(string json) => Deserialize<RegistryValuePayload>(json);

    public static ServiceStartModePayload? ReadServiceStartMode(string json) => Deserialize<ServiceStartModePayload>(json);

    public static PowerPlanPayload? ReadPowerPlan(string json) => Deserialize<PowerPlanPayload>(json);

    public static RegistryBackupPayload? ReadRegistryBackup(string json) => Deserialize<RegistryBackupPayload>(json);

    public static StartupTogglePayload? ReadToggle(string json) => Deserialize<StartupTogglePayload>(json);

    public static VisualEffectsPayload? ReadVisualEffects(string json) => Deserialize<VisualEffectsPayload>(json);

    private static string Serialize<T>(T payload) =>
        JsonSerializer.Serialize(payload, AppJson.CreateOptions(indented: false));

    private static T? Deserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, AppJson.CreateOptions(indented: false));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
