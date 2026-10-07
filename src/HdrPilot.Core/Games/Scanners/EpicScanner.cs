using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Common;
using HdrPilot.Core.Games.Scanners.Epic;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Epic Games Launcher. Liest die Manifeste <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item</c> (JSON).
/// Fehlt der Ordner, nennt <c>HKCU\Software\Epic Games\EOS</c>, Wert <c>ModSdkMetadataDir</c>, den Manifest-Ordner.
/// Übersprungen werden unvollständige Installationen, Zusatzinhalte (AppName ≠ MainGameAppName) und Einträge ohne
/// Spielcharakter (Engines, Plugins, nicht ausführbar). Start über die Launcher-URI, damit Anmeldung und
/// Cloud-Speicher wie beim Start aus dem Launcher laufen. Liest nur, schreibt nichts.
/// </summary>
public sealed class EpicScanner : IGameScanner
{
    internal const string EosKeyPath = @"Software\Epic Games\EOS";
    internal const string MetadataDirValueName = "ModSdkMetadataDir";

    /// <summary>AppCategories von Einträgen ohne Spielcharakter (Unreal Engine, Engine-Plugins).</summary>
    private static readonly string[] NonGameCategories = ["engines", "plugins"];

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public EpicScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Epic;

    public string Name => "Epic Games";

    /// <summary>Start-URI des Launchers, Doppelpunkte als %3A kodiert.</summary>
    internal static string BuildLaunchUri(string catalogNamespace, string catalogItemId, string appName) =>
        "com.epicgames.launcher://apps/"
        + Uri.EscapeDataString(catalogNamespace) + "%3A"
        + Uri.EscapeDataString(catalogItemId) + "%3A"
        + Uri.EscapeDataString(appName)
        + "?action=launch&silent=true";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (manifestDir, launcherDetected) = FindManifestDirectory();
        if (manifestDir is null)
        {
            return Result(launcherDetected, [], []);
        }

        var games = new List<GameEntry>();
        var errors = new List<ScanError>();
        List<string> files;
        try
        {
            // Nur die oberste Ebene: der Unterordner "Pending" enthält Manifeste laufender Installationen.
            files = _fileSystem.EnumerateFiles(manifestDir, "*.item")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"Epic-Manifestordner nicht lesbar: {ex.GetType().Name}: {ex.Message}", manifestDir));
            return Result(true, games, errors);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = ReadEntry(file, errors);
            if (entry is null)
            {
                continue;
            }

            if (!seen.Add(entry.SourceId))
            {
                _logger.LogInformation("Epic: doppeltes Manifest für {AppName} übersprungen ({File}).", entry.SourceId, file);
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

    /// <summary>Standardordner unter ProgramData, sonst der Ordner aus der Registry. LauncherDetected auch ohne Ordner, wenn die Registry ihn nennt.</summary>
    private (string? Directory, bool LauncherDetected) FindManifestDirectory()
    {
        var programData = _environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            var standard = PathUtil.Normalize(Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests"));
            if (_fileSystem.DirectoryExists(standard))
            {
                return (standard, true);
            }
        }

        string? configured;
        using (var key = _registry.OpenKey(RegistryHiveKind.CurrentUser, EosKeyPath))
        {
            configured = key?.GetString(MetadataDirValueName);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return (null, false);
        }

        // Gemessen: 'C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests' mit Schrägstrichen.
        var fromRegistry = PathUtil.Normalize(_environment.ExpandEnvironmentVariables(configured));
        if (Path.IsPathFullyQualified(fromRegistry) && _fileSystem.DirectoryExists(fromRegistry))
        {
            _logger.LogInformation("Epic: Manifest-Ordner über {Value} gefunden: {Directory}.", MetadataDirValueName, fromRegistry);
            return (fromRegistry, true);
        }

        _logger.LogInformation("Epic: {Value} zeigt auf einen fehlenden Ordner: {Directory}.", MetadataDirValueName, fromRegistry);
        return (null, true);
    }

    private GameEntry? ReadEntry(string file, List<ScanError> errors)
    {
        EpicManifest manifest;
        try
        {
            manifest = EpicManifestReader.Parse(_fileSystem.ReadAllBytes(file));
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("Epic-Manifest defekt: " + ex.Message, file));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"Epic-Manifest nicht lesbar: {ex.GetType().Name}: {ex.Message}", file));
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.AppName))
        {
            errors.Add(new ScanError("Epic-Manifest unvollständig: Pflichtfeld AppName fehlt.", file));
            return null;
        }

        var appName = manifest.AppName.Trim();
        if (manifest.IsIncompleteInstall)
        {
            _logger.LogInformation("Epic: {AppName} übersprungen, Installation unvollständig.", appName);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MainGameAppName)
            && !string.Equals(manifest.MainGameAppName.Trim(), appName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Epic: {AppName} übersprungen, Zusatzinhalt zu {MainGame}.", appName, manifest.MainGameAppName);
            return null;
        }

        if (!IsGame(manifest))
        {
            _logger.LogInformation("Epic: {AppName} übersprungen, kein Spiel (Kategorien {Categories}).", appName, string.Join(",", manifest.AppCategories));
            return null;
        }

        var missing = new List<string>();
        AddIfMissing(missing, "DisplayName", manifest.DisplayName);
        AddIfMissing(missing, "InstallLocation", manifest.InstallLocation);
        AddIfMissing(missing, "CatalogNamespace", manifest.CatalogNamespace);
        AddIfMissing(missing, "CatalogItemId", manifest.CatalogItemId);
        if (missing.Count > 0)
        {
            errors.Add(new ScanError($"Epic-Manifest unvollständig: Pflichtfeld(er) {string.Join(", ", missing)} fehlen.", file));
            return null;
        }

        var name = manifest.DisplayName!.Trim();
        var installDir = InstallPaths.NormalizeInstallDir(manifest.InstallLocation, out var problem);
        if (installDir is null)
        {
            errors.Add(new ScanError($"Epic-Manifest für \"{name}\" unbrauchbar: {problem}", file));
            return null;
        }

        if (!_fileSystem.DirectoryExists(installDir))
        {
            _logger.LogInformation("Epic: {Name} übersprungen, Spielordner fehlt: {InstallDir}.", name, installDir);
            return null;
        }

        var executables = new List<string>();
        InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, manifest.LaunchExecutable));
        InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, manifest.MainWindowProcessName));
        foreach (var process in manifest.ProcessNames)
        {
            InstallPaths.AddDistinct(executables, InstallPaths.ResolveExecutable(_fileSystem, installDir, process));
        }

        return new GameEntry
        {
            Source = GameSource.Epic,
            SourceId = appName,
            Name = name,
            InstallDir = installDir,
            Executables = executables,
            LaunchCommand = BuildLaunchUri(manifest.CatalogNamespace!.Trim(), manifest.CatalogItemId!.Trim(), appName),
        };
    }

    /// <summary>
    /// Spielcharakter: ausführbar (bIsExecutable nicht false) und Kategorie "games" ohne "engines"/"plugins".
    /// Manifeste ganz ohne AppCategories (ältere Formate) gelten als Spiel.
    /// </summary>
    private static bool IsGame(EpicManifest manifest)
    {
        if (manifest.IsExecutable == false)
        {
            return false;
        }

        if (manifest.AppCategories.Count == 0)
        {
            return true;
        }

        return !manifest.AppCategories.Any(c => NonGameCategories.Contains(c.Trim(), StringComparer.OrdinalIgnoreCase))
            && manifest.AppCategories.Any(c => string.Equals(c.Trim(), "games", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddIfMissing(List<string> missing, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(field);
        }
    }
}
