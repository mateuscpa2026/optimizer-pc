using Microsoft.Win32;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Restore;

/// <summary>
/// Devolve um valor de registro ao estado anterior. Usado tanto pela sessao do Modo Gamer
/// quanto pela Central de Restauracao, para que exista uma unica forma de desfazer.
/// </summary>
internal static class RegistryValueWriter
{
    internal static bool Restore(
        IRegistryService registry,
        RegistryHiveKind hive,
        string subKey,
        string? name,
        RegistryValueData? previous)
    {
        try
        {
            if (previous is null)
            {
                // O valor nao existia antes: remove para devolver o estado original.
                if (registry.KeyExists(hive, subKey))
                {
                    registry.DeleteValue(hive, subKey, name);
                }

                return true;
            }

            if (registry.KeyExists(hive, subKey) is false)
            {
                return false;
            }

            Apply(registry, hive, subKey, name ?? previous.Name, previous);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Apply(
        IRegistryService registry,
        RegistryHiveKind hive,
        string subKey,
        string? name,
        RegistryValueData value)
    {
        switch (value.Kind)
        {
            case nameof(RegistryValueKind.DWord):
                registry.SetInt(hive, subKey, name ?? string.Empty, value.IntValue ?? 0);
                break;

            case nameof(RegistryValueKind.QWord):
                registry.SetInt(hive, subKey, name ?? string.Empty, value.IntValue ?? 0);
                break;

            case nameof(RegistryValueKind.ExpandString):
                registry.SetExpandString(hive, subKey, name ?? string.Empty, value.StringValue ?? string.Empty);
                break;

            case nameof(RegistryValueKind.MultiString):
                registry.SetMultiString(hive, subKey, name ?? string.Empty, value.MultiStringValue ?? Array.Empty<string>());
                break;

            case nameof(RegistryValueKind.Binary):
                registry.SetBinary(hive, subKey, name ?? string.Empty, Decode(value.StringValue));
                break;

            default:
                registry.SetString(hive, subKey, name ?? string.Empty, value.StringValue ?? string.Empty);
                break;
        }
    }

    /// <summary>Valores binarios sao lidos como base64 pelo servico de registro.</summary>
    private static byte[] Decode(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}
