using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Common;
using HdrPilot.Core.Games.Scanners.Gog;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// GOG (Galaxy und Offline-Installer). Liest <c>HKLM\Software\GOG.com\Games\&lt;ID&gt;</c> über die 32-Bit-Sicht der
/// Registry (kein handgeschriebener WOW6432Node-Pfad). Zusatzinhalte (Wert <c>dependsOn</c> gesetzt oder
/// <c>rootGameId</c> der Spielinfo ≠ ID) werden übersprungen. Start direkt über die EXE aus der Registry; fehlt sie,
/// über die primäre playTask der <c>goggame-&lt;ID&gt;.info</c>. Bekannte EXE-Dateien zusätzlich aus den playTasks der
/// Kategorie "game" (das eigentliche Spiel hinter einem Starter). Liest nur, schreibt nichts.
/// </summary>
public sealed class GogScanner : IGameScanner
{
    internal const string GamesKeyPath = @"Software\GOG.com\Games";
    internal const string GalaxyClientKeyPath = @"Software\GOG.com\GalaxyClient";
    internal const string GalaxyClientPathsKeyPath = @"Software\GOG.com\GalaxyClient\paths";

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly ILogger _logger;

    public GogScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        _fileSystem = fileSystem;
        _registry = registry;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Gog;

    public string Name => "GOG Galaxy";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (galaxyInstalled, galaxyClientDir) = ReadGalaxyClient();
        using var gamesKey = _registry.OpenKey(RegistryHiveKind.LocalMachine, GamesKeyPath, RegistryViewKind.Registry32);
        if (gamesKey is null)
        {
            return Result(galaxyInstalled, [], []);
        }

        var games = new List<GameEntry>();
        var errors = new List<ScanError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyName in gamesKey.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = ReadEntry(gamesKey, keyName, galaxyClientDir, errors);
            if (entry is null)
            {
                continue;
            }

            if (!seen.Add(entry.SourceId))
            {
                _logger.LogInformation("GOG: doppelter Eintrag für {GameId} übersprungen ({Key}).", entry.SourceId, keyName);
                continue;
            }

            games.Add(entry);
        }

        return Result(true, games, errors);
    }

    private ScanResult Result(bool launcherDetected, IReadOnlyList<GameEntry> games, IReadOnlyList<ScanError> errors) => new()
    {
        Source = Source,
        ScannerName = Name,
        LauncherDetected = launcherDetected,
        Games = games,
        Errors = errors,
    };

    private (bool Installed, string? ClientDir) ReadGalaxyClient()
    {
        bool installed;
        using (var client = _registry.OpenKey(RegistryHiveKind.LocalMachine, GalaxyClientKeyPath, RegistryViewKind.Registry32))
        {
            installed = client is not null;
        }

        using var paths = _registry.OpenKey(RegistryHiveKind.LocalMachine, GalaxyClientPathsKeyPath, RegistryViewKind.Registry32);
        var dir = paths?.GetString("client");
        var normalized = string.IsNullOrWhiteSpace(dir) ? null : PathUtil.Normalize(dir);
        return (installed, normalized is not null && Path.IsPathFullyQualified(normalized) ? normalized : null);
    }

    private GameEntry? ReadEntry(IRegistryKey gamesKey, string keyName, string? galaxyClientDir, List<ScanError> errors)
    {
        var keyPath = gamesKey.Name + "\\" + keyName;
        GogRegistryEntry reg;
        using (var key = gamesKey.OpenSubKey(keyName))
        {
            if (key is null)
            {
                errors.Add(new ScanError("GOG-Registry-Eintrag nicht lesbar.", keyPath));
                return null;
            }

            reg = GogRegistryEntry.Read(key);
        }

        var id = string.IsNullOrWhiteSpace(reg.GameId) ? keyName.Trim() : reg.GameId.Trim();
        if (NamesOtherGame(reg.DependsOn, id))
        {
            _logger.LogInformation("GOG: {GameId} übersprungen, Zusatzinhalt zu {DependsOn}.", id, reg.DependsOn);
            return null;
        }

        if (string.IsNullOrWhiteSpace(reg.Path))
        {
            errors.Add(new ScanError($"GOG-Registry-Eintrag unvollständig: Wert {GogRegistryEntry.PathValue} fehlt.", keyPath));
            return null;
        }

        var installDir = InstallPaths.NormalizeInstallDir(reg.Path, out var problem);
        if (installDir is null)
        {
            errors.Add(new ScanError("GOG-Registry-Eintrag unbrauchbar: " + problem, keyPath));
            return null;
        }

        if (galaxyClientDir is not null && PathUtil.IsUnder(galaxyClientDir, installDir))
        {
            // Sonst gälte GOG Galaxy selbst als Spiel und jeder Galaxy-Prozess schaltete HDR.
            errors.Add(new ScanError($"GOG-Registry-Eintrag unbrauchbar: Installationsordner \"{installDir}\" enthält GOG Galaxy selbst.", keyPath));
            return null;
        }

        if (!_fileSystem.DirectoryExists(installDir))
        {
            _logger.LogInformation("GOG: {GameId} übersprungen, Spielordner fehlt: {InstallDir}.", id, installDir);
            return null;
        }

        var info = ReadInfo(installDir, id, errors);
        if (info?.RootGameId is { } rootId && NamesOtherGame(rootId, id))
        {
            _logger.LogInformation("GOG: {GameId} übersprungen, laut Spielinfo Zusatzinhalt zu {RootGameId}.", id, rootId);
            return null;
        }

        var name = !string.IsNullOrWhiteSpace(reg.GameName) ? reg.GameName.Trim() : info?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new ScanError($"GOG-Registry-Eintrag unvollständig: Wert {GogRegistryEntry.GameNameValue} fehlt.", keyPath));
            return null;
        }

        var launch = ResolveLaunch(reg, info, installDir);
        if (launch is null)
        {
            errors.Add(new ScanError(
                $"GOG-Spiel \"{name}\": keine vorhandene Start-EXE (Registry-Wert {GogRegistryEntry.ExeValue} und primäre playTask fehlen oder zeigen ins Leere).",
                keyPath));
            return null;
        }

        var executables = new List<string>();
        InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, launch.Value.Command));
        InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, reg.Exe));
        foreach (var task in info?.PlayTasks ?? [])
        {
            if (task.IsFileTask && (task.IsPrimary || task.IsGameCategory))
            {
                InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, task.Path));
            }
        }

        return new GameEntry
        {
            Source = GameSource.Gog,
            SourceId = id,
            Name = name,
            InstallDir = installDir,
            Executables = executables,
            LaunchCommand = launch.Value.Command,
            LaunchArguments = launch.Value.Arguments,
            LaunchWorkingDirectory = launch.Value.WorkingDirectory,
        };
    }

    /// <summary>Spielinfo aus dem Spielordner; fehlt sie, ist das kein Fehler (nicht jeder Installer legt sie an).</summary>
    private GogGameInfo? ReadInfo(string installDir, string id, List<ScanError> errors)
    {
        var path = installDir + "\\goggame-" + id + ".info";
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            return GogGameInfoReader.Parse(_fileSystem.ReadAllBytes(path));
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("GOG-Spielinfo defekt: " + ex.Message, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"GOG-Spielinfo nicht lesbar: {ex.GetType().Name}: {ex.Message}", path));
        }

        return null;
    }

    /// <summary>Start-EXE aus der Registry (exe, launchParam, workingDir); sonst die primäre playTask. Nur vorhandene Dateien.</summary>
    private (string Command, string? Arguments, string? WorkingDirectory)? ResolveLaunch(GogRegistryEntry reg, GogGameInfo? info, string installDir)
    {
        var exe = ResolveExisting(reg.Exe, installDir);
        if (exe is not null)
        {
            return (exe, NullIfEmpty(reg.LaunchParam), ResolveWorkingDirectory(reg.WorkingDir, installDir));
        }

        if (info?.PrimaryTask is { } task && ResolveExisting(task.Path, installDir) is { } taskExe)
        {
            return (taskExe, NullIfEmpty(task.Arguments), ResolveWorkingDirectory(task.WorkingDir, installDir));
        }

        return null;
    }

    private string? ResolveExisting(string? path, string installDir)
    {
        var full = ToAbsolute(path, installDir);
        return full is not null && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && _fileSystem.FileExists(full) ? full : null;
    }

    /// <summary>Arbeitsordner nur, wenn angegeben und vorhanden; sonst null (der Start nutzt dann den Ordner der EXE).</summary>
    private string? ResolveWorkingDirectory(string? path, string installDir)
    {
        var full = ToAbsolute(path, installDir);
        return full is not null && _fileSystem.DirectoryExists(full) ? full : null;
    }

    /// <summary>Absoluter, vereinheitlichter Pfad; relative Angaben (playTasks) gelten relativ zum Spielordner.</summary>
    private static string? ToAbsolute(string? path, string installDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidate = path.Trim().Trim('"').Replace('/', '\\');
        if (!Path.IsPathFullyQualified(candidate))
        {
            candidate = installDir + "\\" + candidate.TrimStart('\\');
        }

        return PathUtil.Normalize(candidate);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// True, wenn <paramref name="referencedId"/> (dependsOn bzw. rootGameId) ein anderes Spiel nennt. Leer, "0" (etwa
    /// als DWORD 0 geschrieben, keine GOG-ID) und die eigene ID machen einen Eintrag nicht zum Zusatzinhalt.
    /// </summary>
    private static bool NamesOtherGame(string? referencedId, string id)
    {
        var value = referencedId?.Trim();
        return !string.IsNullOrEmpty(value)
            && !string.Equals(value, "0", StringComparison.Ordinal)
            && !string.Equals(value, id, StringComparison.OrdinalIgnoreCase);
    }
}
