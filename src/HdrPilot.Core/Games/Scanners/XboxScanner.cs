using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Xbox;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Spiele der Xbox-App und des Game Pass (Microsoft GDK, „modifizierbare“ Installation in einem eigenen Spieleordner).
/// <list type="number">
/// <item>Auf jedem festen Laufwerk die versteckte Datei <c>.GamingRoot</c> lesen; sie nennt den Spieleordner des
/// Laufwerks (am Zielrechner <c>C:\XboxGames</c> und <c>D:\Xbox Bibliothek</c>).</item>
/// <item>Je Unterordner <c>&lt;Spiel&gt;\Content\MicrosoftGame.config</c> lesen. Ordner ohne diese Datei (etwa
/// <c>GameSave</c>) sind keine Spiele.</item>
/// <item>Installationsordner ist <c>&lt;Spiel&gt;\Content</c>: Dorthin zeigt der Paketordner unter WindowsApps, dort liegen
/// gamelaunchhelper.exe und die Spiel-EXEs, und von dort liefen die Prozesse am Zielrechner (MuiCache, Compatibility Assistant).</item>
/// <item>Start über <c>shell:AppsFolder\&lt;PackageFamilyName&gt;!&lt;App-Id&gt;</c>. Die App-Id stammt aus
/// <c>appxmanifest.xml</c> (Applications/Application/@Id), sonst aus Executable/@Id.</item>
/// </list>
/// Liest nur; startet und verändert nichts.
/// </summary>
public sealed class XboxScanner : IGameScanner
{
    private const string ContentFolder = "Content";
    private const string ResourcesPri = "resources.pri";

    private readonly IFileSystem _fileSystem;
    private readonly IIndirectStringResolver _resolver;
    private readonly ILogger _logger;

    /// <summary>Vertrags-Konstruktor für <see cref="ScannerCatalog"/>. Registry und Umgebung braucht dieser Scanner nicht.</summary>
    public XboxScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
        : this(fileSystem, registry, environment, ShellIndirectStringResolver.Instance, logger)
    {
    }

    /// <summary>Wie oben, mit eigener Auflösung von <c>ms-resource:</c>-Namen (für Tests).</summary>
    public XboxScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, IIndirectStringResolver resolver, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(resolver);
        _fileSystem = fileSystem;
        _resolver = resolver;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Xbox;

    public string Name => "Xbox";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        var games = new List<GameEntry>();
        var errors = new List<ScanError>();
        var detected = false;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driveRoot in _fileSystem.GetFixedDriveRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var markerPath = Path.Combine(driveRoot, GamingRootFile.FileName);
            if (!_fileSystem.FileExists(markerPath))
            {
                continue;
            }

            detected = true;
            foreach (var library in ReadGamingRoot(driveRoot, markerPath, errors))
            {
                if (libraries.Add(library))
                {
                    ScanLibrary(library, games, errors, packages, cancellationToken);
                }
            }
        }

        return new ScanResult
        {
            Source = Source,
            ScannerName = Name,
            LauncherDetected = detected,
            Games = games,
            Errors = errors,
        };
    }

    private List<string> ReadGamingRoot(string driveRoot, string markerPath, List<ScanError> errors)
    {
        byte[] data;
        try
        {
            data = _fileSystem.ReadAllBytes(markerPath);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            errors.Add(new ScanError($"{GamingRootFile.FileName} konnte nicht gelesen werden: {ex.Message}", markerPath));
            return [];
        }

        var content = GamingRootFile.Parse(data);
        if (content.Error is not null)
        {
            errors.Add(new ScanError($"{GamingRootFile.FileName}: {content.Error}", markerPath));
        }
        else if (content.HeaderValue != content.Folders.Count)
        {
            _logger.LogDebug("Xbox: {Path} meldet {Header} im Kopf, enthält aber {Count} Ordnernamen.", markerPath, content.HeaderValue, content.Folders.Count);
        }

        var libraries = new List<string>();
        foreach (var folder in content.Folders)
        {
            var full = PathUtil.Normalize(Path.IsPathFullyQualified(folder) ? folder : Path.Combine(driveRoot, folder.TrimStart('\\', '/')));
            if (_fileSystem.DirectoryExists(full))
            {
                libraries.Add(full);
            }
            else
            {
                _logger.LogInformation("Xbox: Spieleordner {Folder} aus {Path} existiert nicht.", full, markerPath);
            }
        }

        return libraries;
    }

    private void ScanLibrary(string library, List<GameEntry> games, List<ScanError> errors, HashSet<string> packages, CancellationToken cancellationToken)
    {
        List<string> gameFolders;
        try
        {
            gameFolders = _fileSystem.EnumerateDirectories(library)
                .Select(PathUtil.Normalize)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            errors.Add(new ScanError($"Spieleordner konnte nicht gelesen werden: {ex.Message}", library));
            return;
        }

        foreach (var gameFolder in gameFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = ReadGame(gameFolder, errors);
                if (entry is null)
                {
                    continue;
                }

                if (!packages.Add(entry.SourceId))
                {
                    _logger.LogWarning("Xbox: Paket {Package} liegt mehrfach vor, {Folder} wird übersprungen.", entry.SourceId, gameFolder);
                    continue;
                }

                games.Add(entry);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new ScanError($"Spielordner konnte nicht ausgewertet werden: {ex.GetType().Name}: {ex.Message}", gameFolder));
            }
        }
    }

    private GameEntry? ReadGame(string gameFolder, List<ScanError> errors)
    {
        var contentDir = PathUtil.Normalize(Path.Combine(gameFolder, ContentFolder));
        var configPath = Path.Combine(contentDir, MicrosoftGameConfig.FileName);
        if (!_fileSystem.FileExists(configPath))
        {
            _logger.LogDebug("Xbox: {Folder} enthält keine {File}, kein Spiel.", gameFolder, MicrosoftGameConfig.FileName);
            return null;
        }

        MicrosoftGameConfig config;
        try
        {
            config = MicrosoftGameConfig.Parse(_fileSystem.ReadAllBytes(configPath));
        }
        catch (Exception ex) when (ex is InvalidDataException || IsReadFailure(ex))
        {
            errors.Add(new ScanError(
                ex is InvalidDataException ? ex.Message : $"{MicrosoftGameConfig.FileName} konnte nicht gelesen werden: {ex.Message}",
                configPath));
            return null;
        }

        var manifest = ReadManifest(contentDir, errors);
        var packageName = manifest?.IdentityName ?? config.IdentityName;
        var publisher = manifest?.IdentityPublisher ?? config.IdentityPublisher;
        if (!string.Equals(packageName, config.IdentityName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(publisher, config.IdentityPublisher, StringComparison.Ordinal))
        {
            _logger.LogWarning("Xbox: Identity in {Config} weicht von {Manifest} ab, es gilt das Paketmanifest.", MicrosoftGameConfig.FileName, AppxManifest.FileName);
        }

        if (!PackageIdentity.IsValidPackageName(packageName))
        {
            errors.Add(new ScanError($"Ungültiger Paketname \"{packageName}\" in Identity/@Name, Start-Befehl nicht bildbar.", configPath));
            return null;
        }

        var pcExecutables = config.Executables.Where(e => e.IsForPc).ToList();
        var executables = ResolveExecutables(contentDir, pcExecutables);
        if (pcExecutables.Count > 0 && executables.Count == 0)
        {
            _logger.LogInformation("Xbox: {Folder} übersprungen, keine der gelisteten EXE-Dateien existiert (unvollständige Installation).", gameFolder);
            return null;
        }

        var appId = manifest?.Applications.FirstOrDefault()?.Id ?? pcExecutables.FirstOrDefault(e => e.Id is not null)?.Id;
        if (appId is null)
        {
            errors.Add(new ScanError(
                "Keine App-Id gefunden (weder appxmanifest.xml Applications/Application/@Id noch Executable/@Id), Start-Befehl nicht bildbar.",
                configPath));
            return null;
        }

        if (!PackageIdentity.IsValidApplicationId(appId))
        {
            errors.Add(new ScanError($"Ungültige App-Id \"{appId}\", Start-Befehl nicht bildbar.", configPath));
            return null;
        }

        var familyName = PackageIdentity.FamilyName(packageName, publisher);
        return new GameEntry
        {
            Source = GameSource.Xbox,
            SourceId = familyName,
            Name = ChooseName(config, manifest, contentDir, packageName, gameFolder),
            InstallDir = contentDir,
            Executables = executables,
            LaunchCommand = $@"shell:AppsFolder\{familyName}!{appId}",
        };
    }

    private AppxManifest? ReadManifest(string contentDir, List<ScanError> errors)
    {
        var path = Path.Combine(contentDir, AppxManifest.FileName);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            return AppxManifest.Parse(_fileSystem.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is InvalidDataException || IsReadFailure(ex))
        {
            var reason = ex is InvalidDataException ? ex.Message : $"{AppxManifest.FileName} konnte nicht gelesen werden: {ex.Message}";
            errors.Add(new ScanError(reason + $" Es gelten die Angaben aus {MicrosoftGameConfig.FileName}.", path));
            return null;
        }
    }

    /// <summary>Executable/@Name ist relativ zum Content-Ordner (gemessen), oft mit Schrägstrichen. Nur existierende EXEs.</summary>
    private List<string> ResolveExecutables(string contentDir, IEnumerable<GameConfigExecutable> executables)
    {
        var result = new List<string>();
        foreach (var executable in executables)
        {
            var relative = executable.Name.Replace('/', '\\').Trim();
            if (relative.Length == 0 || Path.IsPathRooted(relative))
            {
                _logger.LogDebug("Xbox: Executable \"{Name}\" ist nicht relativ und wird ignoriert.", executable.Name);
                continue;
            }

            var full = PathUtil.Normalize(Path.Combine(contentDir, relative));
            if (!PathUtil.IsUnder(full, contentDir) || PathUtil.PathEquals(full, contentDir))
            {
                _logger.LogDebug("Xbox: Executable \"{Name}\" zeigt aus dem Content-Ordner heraus und wird ignoriert.", executable.Name);
                continue;
            }

            if (_fileSystem.FileExists(full) && !result.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(full);
            }
        }

        return result;
    }

    /// <summary>
    /// Anzeigename: zuerst ein direkter Text (ShellVisuals/@DefaultDisplayName, dann Properties/DisplayName des
    /// Paketmanifests), dann <c>ms-resource:</c>-Verweise über resources.pri, zuletzt der Ordnername.
    /// </summary>
    private string ChooseName(MicrosoftGameConfig config, AppxManifest? manifest, string contentDir, string packageName, string gameFolder)
    {
        string?[] sources = [config.DefaultDisplayName, manifest?.DisplayName];
        foreach (var source in sources)
        {
            if (!string.IsNullOrWhiteSpace(source) && !MsResource.IsReference(source))
            {
                return source.Trim();
            }
        }

        var pri = Path.Combine(contentDir, ResourcesPri);
        if (_fileSystem.FileExists(pri))
        {
            foreach (var source in sources)
            {
                if (source is null || !MsResource.IsReference(source))
                {
                    continue;
                }

                var resolved = _resolver.Resolve(MsResource.ToIndirectString(pri, source, packageName));
                if (!string.IsNullOrWhiteSpace(resolved) && !MsResource.IsReference(resolved))
                {
                    return resolved.Trim();
                }
            }
        }

        return Path.GetFileName(gameFolder);
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
