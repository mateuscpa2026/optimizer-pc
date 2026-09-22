using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Gaming;

/// <summary>
/// Deteccao de jogos instalados. A leitura usa apenas os manifestos que os proprios
/// lancadores mantem no computador (Steam, Epic Games e GOG Galaxy). Nenhum dado sai
/// da maquina e nenhum diretorio e varrido sem ser um local conhecido de lancador.
/// </summary>
public sealed class GameDetector : IGameDetector
{
    private const string LogCategory = "Gaming";
    private const string SteamKey = @"Software\Valve\Steam";
    private const string SteamKey32 = @"SOFTWARE\WOW6432Node\Valve\Steam";
    private const string GogKey = @"SOFTWARE\GOG.com\Games";
    private const string GogKey32 = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    private static readonly Regex QuotedToken = new("\"([^\"]*)\"", RegexOptions.Compiled);

    private readonly IRegistryService _registry;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();

    private IReadOnlyList<InstalledGame> _games = Array.Empty<InstalledGame>();

    public GameDetector(IRegistryService registry, IAppLogger logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task<IReadOnlyList<InstalledGame>> DetectAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Detect(cancellationToken), cancellationToken);

    public IReadOnlyList<ProcessInfoModel> FindRunningGames(IReadOnlyList<ProcessInfoModel> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        IReadOnlyList<InstalledGame> games;
        lock (_sync)
        {
            games = _games;
        }

        if (games.Count == 0)
        {
            return Array.Empty<ProcessInfoModel>();
        }

        var matches = new List<ProcessInfoModel>();

        foreach (var process in processes)
        {
            if (IsGameProcess(process, games))
            {
                matches.Add(process);
            }
        }

        return matches;
    }

    private IReadOnlyList<InstalledGame> Detect(CancellationToken cancellationToken)
    {
        var games = new List<InstalledGame>();

        DetectSteam(games, cancellationToken);
        DetectEpic(games, cancellationToken);
        DetectGog(games, cancellationToken);

        var ordered = games
            .GroupBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        lock (_sync)
        {
            _games = ordered;
        }

        _logger.Info(LogCategory, "Jogos instalados identificados: " + ordered.Length + ".");

        if (ordered.Length == 0)
        {
            _logger.Info(LogCategory, "Nenhum manifesto de Steam, Epic Games ou GOG Galaxy foi encontrado neste computador.");
        }

        return ordered;
    }

    private void DetectSteam(List<InstalledGame> games, CancellationToken cancellationToken)
    {
        try
        {
            var steamRoot = ReadSteamRoot();
            if (steamRoot is null)
            {
                return;
            }

            var libraries = new List<string> { Path.Combine(steamRoot, "steamapps") };

            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            foreach (var library in ReadSteamLibraries(libraryFile))
            {
                var apps = Path.Combine(library, "steamapps");
                if (Directory.Exists(apps) && libraries.Contains(apps, StringComparer.OrdinalIgnoreCase) is false)
                {
                    libraries.Add(apps);
                }
            }

            foreach (var library in libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(library) is false)
                {
                    continue;
                }

                foreach (var manifest in SafeEnumerate(library, "appmanifest_*.acf"))
                {
                    var game = ReadSteamManifest(manifest, library);
                    if (game is not null)
                    {
                        games.Add(game);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel ler a biblioteca da Steam.", ex);
        }
    }

    private string? ReadSteamRoot()
    {
        foreach (var (hive, key) in new[]
                 {
                     (RegistryHiveKind.CurrentUser, SteamKey),
                     (RegistryHiveKind.LocalMachine, SteamKey32),
                     (RegistryHiveKind.LocalMachine, SteamKey)
                 })
        {
            foreach (var name in new[] { "SteamPath", "InstallPath" })
            {
                var value = _registry.GetValue(hive, key, name);
                var path = value?.StringValue;
                if (string.IsNullOrWhiteSpace(path) is false && Directory.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Le os caminhos de biblioteca registrados pela Steam. O arquivo muda de formato
    /// entre versoes, entao cada valor entre aspas que pareca um caminho absoluto e aceito.
    /// </summary>
    private static IEnumerable<string> ReadSteamLibraries(string libraryFile)
    {
        if (File.Exists(libraryFile) is false)
        {
            yield break;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(libraryFile);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            if (line.Contains("\"path\"", StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            var match = QuotedToken.Matches(line);
            for (var index = 1; index < match.Count; index++)
            {
                var candidate = match[index].Groups[1].Value.Replace("\\\\", "\\");
                if (IsAbsolutePath(candidate))
                {
                    yield return candidate;
                    break;
                }
            }
        }
    }

    private InstalledGame? ReadSteamManifest(string manifestPath, string appsFolder)
    {
        var text = ReadAllText(manifestPath);
        if (text is null)
        {
            return null;
        }

        var name = ReadSteamValue(text, "name");
        var installDir = ReadSteamValue(text, "installdir");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var installPath = Path.Combine(appsFolder, "common", installDir);
        var size = long.TryParse(ReadSteamValue(text, "SizeOnDisk"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0L;

        return new InstalledGame
        {
            Name = name,
            InstallPath = installPath,
            ExecutablePath = Directory.Exists(installPath) ? FindMainExecutable(installPath, name) : null,
            SizeBytes = size,
            Launcher = GameLauncherKind.Steam
        };
    }

    private static string ReadSteamValue(string text, string key)
    {
        var marker = "\"" + key + "\"";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var matches = QuotedToken.Matches(text, index);
        return matches.Count >= 2 ? matches[1].Groups[1].Value : string.Empty;
    }

    private void DetectEpic(List<InstalledGame> games, CancellationToken cancellationToken)
    {
        try
        {
            var manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "UnrealEngineLauncher",
                "LauncherInstalled.dat");

            var text = ReadAllText(manifestPath);
            if (text is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("InstallationList", out var list) is false ||
                list.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in list.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var installPath = ReadJsonString(item, "InstallLocation");
                if (string.IsNullOrWhiteSpace(installPath) || Directory.Exists(installPath) is false)
                {
                    continue;
                }

                var appName = ReadJsonString(item, "AppName");
                var name = string.IsNullOrWhiteSpace(appName) ? Path.GetFileName(installPath) : appName;

                games.Add(new InstalledGame
                {
                    Name = name,
                    InstallPath = installPath,
                    ExecutablePath = FindMainExecutable(installPath, name),
                    SizeBytes = 0,
                    Launcher = GameLauncherKind.Epic
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.Warning(LogCategory, "O manifesto da Epic Games nao pode ser interpretado.", ex);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategory, "Nao foi possivel ler a biblioteca da Epic Games.", ex);
        }
    }

    private void DetectGog(List<InstalledGame> games, CancellationToken cancellationToken)
    {
        foreach (var (hive, key) in new[]
                 {
                     (RegistryHiveKind.LocalMachine, GogKey),
                     (RegistryHiveKind.LocalMachine, GogKey32)
                 })
        {
            try
            {
                if (_registry.KeyExists(hive, key) is false)
                {
                    continue;
                }

                foreach (var subKey in _registry.GetSubKeyNames(hive, key))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var path = Path.Combine(key, subKey);
                    var installPath = _registry.GetValue(hive, path, "path")?.StringValue;
                    if (string.IsNullOrWhiteSpace(installPath) || Directory.Exists(installPath) is false)
                    {
                        continue;
                    }

                    var name = _registry.GetValue(hive, path, "gameName")?.StringValue;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = Path.GetFileName(installPath.TrimEnd('\\', '/'));
                    }

                    var executable = _registry.GetValue(hive, path, "exe")?.StringValue;

                    games.Add(new InstalledGame
                    {
                        Name = name,
                        InstallPath = installPath,
                        ExecutablePath = string.IsNullOrWhiteSpace(executable) ? FindMainExecutable(installPath, name) : executable,
                        SizeBytes = 0,
                        Launcher = GameLauncherKind.GOG
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategory, "Nao foi possivel ler a biblioteca do GOG Galaxy.", ex);
            }
        }
    }

    /// <summary>
    /// Procura o executavel principal apenas na raiz da pasta do jogo: preferindo um nome
    /// parecido com o do jogo e, na duvida, o maior arquivo executavel encontrado.
    /// </summary>
    private static string? FindMainExecutable(string installPath, string gameName)
    {
        try
        {
            var candidates = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly);
            if (candidates.Length == 0)
            {
                return null;
            }

            var normalized = Normalize(gameName);
            foreach (var candidate in candidates)
            {
                var file = Normalize(Path.GetFileNameWithoutExtension(candidate));
                if (normalized.Length > 0 && (file.StartsWith(normalized, StringComparison.Ordinal) || normalized.StartsWith(file, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }

            return candidates
                .OrderByDescending(candidate => new FileInfo(candidate).Length)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool IsGameProcess(ProcessInfoModel process, IReadOnlyList<InstalledGame> games)
    {
        if (string.IsNullOrEmpty(process.FilePath))
        {
            return false;
        }

        foreach (var game in games)
        {
            if (game.ExecutablePath is not null &&
                string.Equals(game.ExecutablePath, process.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsUnder(process.FilePath, game.InstallPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedRoot = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbsolutePath(string value) =>
        value.Length >= 3 && value[1] == ':' && (value[2] == '\\' || value[2] == '/');

    private static string? ReadJsonString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> SafeEnumerate(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static string? ReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
