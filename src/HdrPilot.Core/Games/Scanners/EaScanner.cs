using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Ea;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// EA app (früher Origin). Die Bibliotheksdatei der EA app ist verschlüsselt und an die Hardware gebunden; sie wird
/// nicht gelesen. Stattdessen gilt jeder Ordner mit <c>__Installer\installerdata.xml</c> als EA-Spiel. Gesucht wird in
/// Uninstall-Einträgen des EA-Installers, unter HKLM\SOFTWARE\EA Games, in den Standardordnern (EA Games, Origin Games,
/// Download-Ordner des alten Origin-Clients) und in den Suchordnern des Nutzers. Details: docs\scanner\ea.md.
/// </summary>
public sealed class EaScanner : IGameScanner
{
    /// <summary>Wie tief unter einem Suchordner nach Spielordnern gesucht wird (0 = der Suchordner selbst).</summary>
    public const int SearchFolderDepth = 3;

    internal const string InstallerDataRelativePath = @"__Installer\installerdata.xml";

    private const string SteamLibraryMarker = @"\steamapps\common\";

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public EaScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.EA;

    public string Name => "EA app";

    /// <summary>
    /// Start über das Protokoll der EA app. Genau diese Form ruft ein EA-Spiel selbst auf, wenn man seine EXE direkt
    /// startet (am Zielrechner im Protokoll EALaunchHelper.log gesehen); die contentID wird dabei als offerIds übergeben.
    /// </summary>
    public static string CreateLaunchCommand(string contentId) =>
        "origin2://game/launch/?offerIds=" + Uri.EscapeDataString(contentId) + "&authCode=&cmdParams=";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<ScanError>();
        var games = new List<GameEntry>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in FindGameFolders(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = ReadGame(folder, errors);
            if (entry is null)
            {
                continue;
            }

            if (!seenIds.Add(entry.SourceId))
            {
                _logger.LogInformation("EA: contentID {Id} kommt mehrfach vor, {Folder} wird übersprungen.", entry.SourceId, folder);
                continue;
            }

            games.Add(entry);
        }

        return new ScanResult
        {
            Source = Source,
            ScannerName = Name,
            LauncherDetected = games.Count > 0 || errors.Count > 0 || IsEaAppInstalled(),
            Games = games,
            Errors = errors,
        };
    }

    private bool IsEaAppInstalled()
    {
        foreach (var view in new[] { RegistryViewKind.Default, RegistryViewKind.Registry32 })
        {
            using var key = _registry.OpenKey(RegistryHiveKind.LocalMachine, @"SOFTWARE\Electronic Arts\EA Desktop", view);
            if (key is not null)
            {
                return true;
            }
        }

        var programData = _environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return !string.IsNullOrWhiteSpace(programData) && _fileSystem.DirectoryExists(Path.Combine(programData, "EA Desktop"));
    }

    /// <summary>Alle Ordner mit installerdata.xml, jeder nur einmal, in der Reihenfolge der Quellen.</summary>
    private List<string> FindGameFolders(ScanOptions options, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string candidate)
        {
            var folder = PathUtil.Normalize(candidate);
            if (folder.Length == 0 || !IsFullyQualified(folder) || !seen.Add(folder))
            {
                return;
            }

            if ((folder + "\\").Contains(SteamLibraryMarker, StringComparison.OrdinalIgnoreCase))
            {
                // Spiele in einer Steam-Bibliothek meldet der Steam-Scanner; sonst stünde ein Spiel doppelt in der Liste.
                _logger.LogDebug("EA: {Folder} liegt in einer Steam-Bibliothek und wird übersprungen.", folder);
                return;
            }

            if (_fileSystem.FileExists(Path.Combine(folder, InstallerDataRelativePath)))
            {
                result.Add(folder);
            }
        }

        foreach (var location in EaInstallLocations.FromUninstallEntries(_registry, cancellationToken))
        {
            Consider(location);
        }

        foreach (var location in EaInstallLocations.FromEaGamesKeys(_registry, cancellationToken))
        {
            Consider(location);
        }

        foreach (var root in DefaultRoots())
        {
            foreach (var sub in EnumerateDirectoriesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Consider(sub);
            }
        }

        foreach (var searchFolder in options.SearchFolders)
        {
            var root = PathUtil.Normalize(searchFolder);
            if (root.Length > 0 && IsFullyQualified(root))
            {
                SearchBelow(root, 0, Consider, cancellationToken);
            }
        }

        return result;
    }

    private IEnumerable<string> DefaultRoots()
    {
        // Standardordner der EA app (gemessen: C:\Program Files\EA Games) und des alten Origin-Clients.
        var programFiles = _environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "EA Games");
        }

        var programFilesX86 = _environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Origin Games");
        }

        if (EaInstallLocations.FromOriginSettings(_fileSystem, _environment, _logger) is { } originDownloadDir)
        {
            var normalized = PathUtil.Normalize(originDownloadDir);
            if (normalized.Length > 0 && IsFullyQualified(normalized))
            {
                yield return normalized;
            }
        }
    }

    private void SearchBelow(string folder, int depth, Action<string> consider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_fileSystem.FileExists(Path.Combine(folder, InstallerDataRelativePath)))
        {
            consider(folder);
            return; // In einem Spielordner nicht weiter absteigen.
        }

        if (depth >= SearchFolderDepth)
        {
            return;
        }

        foreach (var sub in EnumerateDirectoriesSafe(folder))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('$') || string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SearchBelow(PathUtil.Normalize(sub), depth + 1, consider, cancellationToken);
        }
    }

    /// <summary>Unterordner; ein unlesbarer oder währenddessen verschwundener Ordner ergibt eine leere Liste statt einer Ausnahme.</summary>
    private List<string> EnumerateDirectoriesSafe(string folder)
    {
        try
        {
            return _fileSystem.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("EA: Ordner {Folder} nicht lesbar: {Message}", folder, ex.Message);
            return [];
        }
    }

    private GameEntry? ReadGame(string installDir, List<ScanError> errors)
    {
        var path = Path.Combine(installDir, InstallerDataRelativePath);
        EaInstallerData data;
        try
        {
            data = EaInstallerDataParser.Parse(_fileSystem.ReadAllBytes(path));
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("installerdata.xml ist defekt (" + ex.Message + ").", path));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("installerdata.xml ist nicht lesbar (" + ex.Message + ").", path));
            return null;
        }

        if (data.ContentIds.Count == 0)
        {
            errors.Add(new ScanError("installerdata.xml enthält keine contentID; das Spiel wird übersprungen.", path));
            return null;
        }

        var executables = new List<string>();
        foreach (var filePath in data.LauncherFilePaths)
        {
            if (ResolveLauncher(filePath, installDir) is { } exe && !executables.Contains(exe, StringComparer.OrdinalIgnoreCase))
            {
                executables.Add(exe);
            }
        }

        if (data.LauncherFilePaths.Count > 0 && executables.Count == 0)
        {
            // Die Datei nennt Start-EXEs, aber keine davon liegt im Ordner: Installation unvollständig oder Rest einer Deinstallation.
            _logger.LogInformation("EA: {Folder} ohne die in installerdata.xml genannten EXE-Dateien, wird übersprungen.", installDir);
            return null;
        }

        if (ResolveUninstallIcon(data.UninstallPath, installDir) is { } icon && !executables.Contains(icon, StringComparer.OrdinalIgnoreCase))
        {
            executables.Add(icon);
        }

        var contentId = data.ContentIds[0];
        return new GameEntry
        {
            Source = Source,
            SourceId = contentId,
            Name = data.Title ?? FolderName(installDir) ?? contentId,
            InstallDir = installDir,
            Executables = executables,
            LaunchCommand = CreateLaunchCommand(contentId),
        };
    }

    /// <summary>
    /// Löst einen filePath aus runtime/launcher auf. Mit Makro zuerst über den Registry-Wert, sonst relativ zum Spielordner.
    /// Liefert nur vorhandene EXE-Dateien unterhalb des Spielordners.
    /// </summary>
    private string? ResolveLauncher(string filePath, string installDir)
    {
        var candidates = new List<string>();
        if (EaRegistryMacro.TryParse(filePath, out var macro))
        {
            if (ReadRegistryString(macro.Hive, macro.KeyPath, macro.ValueName) is { } baseDir && IsFullyQualified(PathUtil.Normalize(baseDir)))
            {
                candidates.Add(Combine(baseDir, macro.Rest));
            }

            candidates.Add(Combine(installDir, macro.Rest));
        }
        else if (!filePath.StartsWith('['))
        {
            var normalized = PathUtil.Normalize(filePath);
            candidates.Add(IsFullyQualified(normalized) ? normalized : Combine(installDir, filePath));
        }

        return candidates.FirstOrDefault(c => IsExistingExeUnder(c, installDir));
    }

    /// <summary>DisplayIcon des in uninstall/path genannten Uninstall-Eintrags, wenn es eine vorhandene EXE im Spielordner ist.</summary>
    private string? ResolveUninstallIcon(string? uninstallPath, string installDir)
    {
        if (!EaRegistryMacro.TryParse(uninstallPath, out var macro))
        {
            return null;
        }

        var icon = ReadRegistryString(macro.Hive, macro.KeyPath, "DisplayIcon");
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var exe = PathUtil.Normalize(StripIconIndex(icon));
        return IsExistingExeUnder(exe, installDir) ? exe : null;
    }

    /// <summary>Liest einen Wert erst in der 64-Bit-, dann in der 32-Bit-Sicht (der EA-Installer schreibt teils nur nach WOW6432Node).</summary>
    private string? ReadRegistryString(RegistryHiveKind hive, string keyPath, string valueName)
    {
        foreach (var view in new[] { RegistryViewKind.Default, RegistryViewKind.Registry32 })
        {
            using var key = _registry.OpenKey(hive, keyPath, view);
            var value = key?.GetString(valueName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private bool IsExistingExeUnder(string path, string installDir) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && PathUtil.IsUnder(path, installDir)
        && !PathUtil.PathEquals(path, installDir)
        && _fileSystem.FileExists(path);

    /// <summary>Entfernt Anführungszeichen und einen Symbolindex wie ",0" aus einem DisplayIcon-Wert.</summary>
    internal static string StripIconIndex(string icon)
    {
        var t = icon.Trim();
        if (t.StartsWith('"'))
        {
            var end = t.IndexOf('"', 1);
            return end > 0 ? t[1..end] : t.Trim('"');
        }

        var comma = t.LastIndexOf(',');
        return comma > 0 && int.TryParse(t[(comma + 1)..], out _) ? t[..comma] : t;
    }

    private static string? FolderName(string folder)
    {
        var name = Path.GetFileName(folder);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string Combine(string baseDir, string relative) =>
        PathUtil.Normalize(baseDir.Trim().Trim('"').TrimEnd('\\', '/') + "\\" + relative.Trim().TrimStart('\\', '/'));

    private static bool IsFullyQualified(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
