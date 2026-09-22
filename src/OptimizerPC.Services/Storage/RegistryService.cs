using System.Globalization;
using Microsoft.Win32;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Storage;

/// <summary>
/// Acesso ao registro do Windows. As escritas sao usadas apenas em chaves conhecidas
/// (inicializacao de programas e preferencias visuais) e sempre com backup previo.
/// </summary>
public sealed class RegistryService : IRegistryService
{
    public bool KeyExists(RegistryHiveKind hive, string subKey)
    {
        try
        {
            using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: false);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public RegistryValueData? GetValue(RegistryHiveKind hive, string subKey, string? name)
    {
        try
        {
            using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: false);
            if (key is null)
            {
                return null;
            }

            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                return null;
            }

            var kind = key.GetValueKind(name);

            return kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => new RegistryValueData(
                    hive.ToString(), subKey, name, kind.ToString(),
                    value as string, null, null),

                RegistryValueKind.DWord or RegistryValueKind.QWord => new RegistryValueData(
                    hive.ToString(), subKey, name, kind.ToString(),
                    null, Convert.ToInt32(value, CultureInfo.InvariantCulture), null),

                RegistryValueKind.MultiString => new RegistryValueData(
                    hive.ToString(), subKey, name, kind.ToString(),
                    null, null, value as string[]),

                RegistryValueKind.Binary => new RegistryValueData(
                    hive.ToString(), subKey, name, kind.ToString(),
                    Convert.ToBase64String((byte[])value), null, null),

                _ => new RegistryValueData(hive.ToString(), subKey, name, kind.ToString(), value.ToString(), null, null)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryHiveKind hive, string subKey)
    {
        try
        {
            using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: false);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey)
    {
        try
        {
            using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: false);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public string[]? GetMultiString(RegistryHiveKind hive, string subKey, string name)
    {
        try
        {
            using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as string[];
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SetString(RegistryHiveKind hive, string subKey, string name, string value) =>
        Write(hive, subKey, name, RegistryValueKind.String, value);

    public void SetExpandString(RegistryHiveKind hive, string subKey, string name, string value) =>
        Write(hive, subKey, name, RegistryValueKind.ExpandString, value);

    public void SetInt(RegistryHiveKind hive, string subKey, string name, int value) =>
        Write(hive, subKey, name, RegistryValueKind.DWord, value);

    public void SetBinary(RegistryHiveKind hive, string subKey, string name, byte[] value) =>
        Write(hive, subKey, name, RegistryValueKind.Binary, value);

    public void SetMultiString(RegistryHiveKind hive, string subKey, string name, string[] value) =>
        Write(hive, subKey, name, RegistryValueKind.MultiString, value);

    public void DeleteValue(RegistryHiveKind hive, string subKey, string? name)
    {
        using var key = OpenBase(hive)?.OpenSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException("A chave de registro informada nao esta acessivel: " + subKey);

        if (name is null)
        {
            return;
        }

        key.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void Write(RegistryHiveKind hive, string subKey, string name, RegistryValueKind kind, object value)
    {
        using var key = OpenBase(hive)?.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException("Nao foi possivel abrir a chave de registro para escrita: " + subKey);

        key.SetValue(name, value, kind);
    }

    private static RegistryKey? OpenBase(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.CurrentUser => Registry.CurrentUser,
        RegistryHiveKind.LocalMachine => Registry.LocalMachine,
        RegistryHiveKind.ClassesRoot => Registry.ClassesRoot,
        RegistryHiveKind.Users => Registry.Users,
        _ => null
    };
}
