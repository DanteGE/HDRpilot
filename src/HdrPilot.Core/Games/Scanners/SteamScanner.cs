using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Steam;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Findet installierte Steam-Spiele. Ablauf:
/// <list type="number">
/// <item>Steam-Ordner aus HKCU\Software\Valve\Steam, Wert SteamPath (gemessen: <c>c:/program files (x86)/steam</c>);
/// Rückfall HKLM\SOFTWARE\Valve\Steam (32-Bit-Sicht), Wert InstallPath, danach der Standardordner unter Programme (x86).</item>
/// <item>Bibliotheken aus <c>steamapps\libraryfolders.vdf</c>; die Standardbibliothek (Steam-Ordner) immer, auch wenn die
/// Datei fehlt oder defekt ist. Doppelte Bibliotheken (Groß-/Kleinschreibung, Schrägstriche) zählen einmal, fehlende
/// werden übersprungen.</item>
/// <item>Je Bibliothek <c>steamapps\appmanifest_&lt;AppID&gt;.acf</c> mit appid, name, installdir, StateFlags und LastPlayed
/// (Unix-Sekunden, für „zuletzt verwendet“; 0 heißt nie gespielt). Spielordner
/// ist <c>&lt;Bibliothek&gt;\steamapps\common\&lt;installdir&gt;</c>; fehlt er, entfällt der Eintrag.</item>
/// <item>Nur vollständig installierte Apps (StateFlags-Bit 4); ausstehende Updates (etwa 6 = 4 + 2) zählen als installiert.</item>
/// <item>Werkzeuge und Anwendungen filtert <see cref="SteamAppFilter"/> (feste Liste, App-Typ aus appcache\appinfo.vdf, Namensregeln).</item>
/// </list>
/// Steam nennt keine Spiel-EXE; <see cref="GameEntry.Executables"/> bleibt leer. Start: <c>steam://rungameid/&lt;AppID&gt;</c>.
/// Symbol-Hinweis: das offizielle Steam-Symbol (<see cref="SteamIconLocator"/>, E86).
/// </summary>
public sealed class SteamScanner : IGameScanner
{
    internal const string UserKeyPath = @"Software\Valve\Steam";
    internal const string MachineKeyPath = @"SOFTWARE\Valve\Steam";

    /// <summary>StateFlags-Bit „vollständig installiert“.</summary>
    internal const uint StateFullyInstalled = 4;

    /// <summary>StateFlags-Bit „wird deinstalliert“.</summary>
    internal const uint StateUninstalling = 2048;

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public SteamScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Steam;

    public string Name => "Steam";

    /// <summary>Start-Befehl einer Steam-App.</summary>
    public static string LaunchCommandFor(uint appId) => "steam://rungameid/" + appId.ToString(CultureInfo.InvariantCulture);

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var steamPath = FindSteamPath();
        if (steamPath is null)
        {
            _logger.LogDebug("Steam ist nicht installiert (kein SteamPath mit vorhandenem Ordner).");
            return new ScanResult { Source = Source, ScannerName = Name, LauncherDetected = false };
        }

        var errors = new List<ScanError>();
        var libraries = FindLibraries(steamPath, errors, cancellationToken);
        var manifests = new List<Manifest>();
        foreach (var library in libraries)
        {
            ReadManifests(library, manifests, errors, cancellationToken);
        }

        var installed = SelectInstalled(manifests, cancellationToken);
        var appInfo = installed.Count == 0 ? null : LoadAppInfo(steamPath, installed, errors, cancellationToken);

        var games = new List<GameEntry>(installed.Count);
        var icons = new SteamIconLocator(steamPath, _fileSystem, _registry, _environment, _logger);
        foreach (var manifest in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SteamAppInfo? info = null;
            appInfo?.TryGetValue(manifest.AppId, out info);
            var name = ResolveName(manifest, info);
            var reason = SteamAppFilter.GetNonGameReason(manifest.AppId, name, info?.Type);
            if (reason is not null)
            {
                _logger.LogInformation("Steam: {AppId} \"{Name}\" ist kein Spiel ({Reason}) und wird ausgefiltert.", manifest.AppId, name, reason);
                continue;
            }

            games.Add(new GameEntry
            {
                Source = GameSource.Steam,
                SourceId = manifest.AppId.ToString(CultureInfo.InvariantCulture),
                Name = name,
                InstallDir = manifest.GameDir,
                Executables = [],
                LaunchCommand = LaunchCommandFor(manifest.AppId),
                LastPlayed = manifest.LastPlayed,
                IconCandidates = icons.Find(manifest.AppId, info),
            });
        }

        return new ScanResult
        {
            Source = Source,
            ScannerName = Name,
            LauncherDetected = true,
            Games = games,
            Errors = errors,
        };
    }

    private string? FindSteamPath()
    {
        var candidates = new List<(string? Path, string Origin)>();
        using (var key = _registry.OpenKey(RegistryHiveKind.CurrentUser, UserKeyPath))
        {
            candidates.Add((key?.GetString("SteamPath"), @"HKCU\" + UserKeyPath + @"\SteamPath"));
        }

        using (var key = _registry.OpenKey(RegistryHiveKind.LocalMachine, MachineKeyPath, RegistryViewKind.Registry32))
        {
            candidates.Add((key?.GetString("InstallPath"), @"HKLM\" + MachineKeyPath + @"\InstallPath (32 Bit)"));
        }

        var programFilesX86 = PathUtil.Normalize(_environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (Path.IsPathFullyQualified(programFilesX86))
        {
            var defaultPath = Path.Combine(programFilesX86, "Steam");
            if (_fileSystem.FileExists(Path.Combine(defaultPath, "steam.exe")))
            {
                candidates.Add((defaultPath, "Standardordner"));
            }
        }

        foreach (var (raw, origin) in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = PathUtil.Normalize(raw);
            if (!Path.IsPathFullyQualified(path))
            {
                _logger.LogWarning("Steam: {Origin} ist kein absoluter Pfad: {Path}", origin, raw);
                continue;
            }

            if (_fileSystem.DirectoryExists(path))
            {
                _logger.LogDebug("Steam-Ordner aus {Origin}: {Path}", origin, path);
                return path;
            }

            _logger.LogDebug("Steam: {Origin} zeigt auf einen fehlenden Ordner: {Path}", origin, path);
        }

        return null;
    }

    private List<string> FindLibraries(string steamPath, List<ScanError> errors, CancellationToken cancellationToken)
    {
        // Die Standardbibliothek steht immer an erster Stelle; die erste Schreibweise aus libraryfolders.vdf ersetzt die aus
        // der Registry (dort gemessen klein und mit Schrägstrichen). Ohne Eintrag in der Datei bleibt die Registry-Schreibweise.
        var libraries = new List<string> { steamPath };
        var defaultSpellingFromFile = false;
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!_fileSystem.FileExists(vdfPath))
        {
            _logger.LogInformation("Steam: {Path} fehlt, nur die Standardbibliothek wird gelesen.", vdfPath);
            return libraries;
        }

        KeyValueNode root;
        try
        {
            root = ParseFile(vdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("Steam-Bibliotheksliste nicht lesbar, nur die Standardbibliothek wird gelesen: " + ex.Message, vdfPath));
            return libraries;
        }
        catch (KeyValuesException ex)
        {
            errors.Add(new ScanError("Steam-Bibliotheksliste defekt, nur die Standardbibliothek wird gelesen: " + ex.Message, vdfPath));
            return libraries;
        }

        var folders = root.Child("libraryfolders");
        if (folders is null || !folders.IsBlock)
        {
            errors.Add(new ScanError("Steam-Bibliotheksliste hat einen unerwarteten Aufbau (Abschnitt \"libraryfolders\" fehlt), nur die Standardbibliothek wird gelesen.", vdfPath));
            return libraries;
        }

        foreach (var entry in folders.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDigits(entry.Key))
            {
                continue; // ältere Dateien: TimeNextStatsReport, ContentStatsID
            }

            // Aktuelles Format: Block mit "path"; älteres Format: der Wert selbst ist der Pfad.
            var raw = entry.IsBlock ? entry.GetString("path") : entry.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                errors.Add(new ScanError($"Steam-Bibliothek \"{entry.Key}\" in der Bibliotheksliste hat keinen Pfad.", vdfPath));
                continue;
            }

            var path = PathUtil.Normalize(raw);
            if (!Path.IsPathFullyQualified(path))
            {
                errors.Add(new ScanError($"Steam-Bibliothek \"{entry.Key}\" hat keinen absoluten Pfad: {raw}", vdfPath));
                continue;
            }

            var existing = libraries.FindIndex(l => PathUtil.PathEquals(l, path));
            if (existing >= 0)
            {
                if (existing == 0 && !defaultSpellingFromFile)
                {
                    libraries[0] = path;
                    defaultSpellingFromFile = true;
                }

                continue;
            }

            if (!_fileSystem.DirectoryExists(path))
            {
                _logger.LogInformation("Steam: Bibliothek {Path} ist nicht vorhanden und wird übersprungen.", path);
                continue;
            }

            libraries.Add(path);
        }

        return libraries;
    }

    private void ReadManifests(string library, List<Manifest> manifests, List<ScanError> errors, CancellationToken cancellationToken)
    {
        var steamApps = Path.Combine(library, "steamapps");
        List<string> files;
        try
        {
            files = _fileSystem.EnumerateFiles(steamApps, "appmanifest_*.acf")
                .Where(f => f.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("Steam-Bibliothek nicht lesbar: " + ex.Message, steamApps));
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = ReadManifest(library, file, errors);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }
    }

    private Manifest? ReadManifest(string library, string file, List<ScanError> errors)
    {
        KeyValueNode root;
        try
        {
            root = ParseFile(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("Steam-Manifest nicht lesbar: " + ex.Message, file));
            return null;
        }
        catch (KeyValuesException ex)
        {
            errors.Add(new ScanError("Steam-Manifest defekt: " + ex.Message, file));
            return null;
        }

        var state = root.Child("AppState");
        if (state is null || !state.IsBlock)
        {
            errors.Add(new ScanError("Steam-Manifest defekt: Abschnitt \"AppState\" fehlt.", file));
            return null;
        }

        var appIdText = state.GetString("appid");
        if (!uint.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
        {
            errors.Add(new ScanError("Steam-Manifest unvollständig: \"appid\" fehlt oder ist keine Zahl.", file));
            return null;
        }

        var installDir = state.GetString("installdir")?.Trim();
        if (string.IsNullOrEmpty(installDir))
        {
            errors.Add(new ScanError($"Steam-Manifest unvollständig: \"installdir\" fehlt (AppID {appId}).", file));
            return null;
        }

        if (!IsPlainRelativeFolder(installDir))
        {
            errors.Add(new ScanError($"Steam-Manifest enthält einen ungültigen Ordnernamen in \"installdir\" (AppID {appId}): {installDir}", file));
            return null;
        }

        uint? stateFlags = null;
        var stateText = state.GetString("StateFlags");
        if (stateText is not null)
        {
            if (uint.TryParse(stateText, NumberStyles.None, CultureInfo.InvariantCulture, out var flags))
            {
                stateFlags = flags;
            }
            else
            {
                _logger.LogWarning("Steam: StateFlags \"{Value}\" in {File} ist keine Zahl und wird ignoriert.", stateText, file);
            }
        }

        var gameDir = PathUtil.Normalize(Path.Combine(library, "steamapps", "common", installDir));
        return new Manifest(appId, state.GetString("name"), installDir, gameDir, stateFlags, file, ParseLastPlayed(state.GetString("LastPlayed")));
    }

    /// <summary>"LastPlayed" in Unix-Sekunden; "0" (nie gespielt), fehlend oder ungültig: null.</summary>
    internal static DateTimeOffset? ParseLastPlayed(string? value)
    {
        if (!long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private List<Manifest> SelectInstalled(List<Manifest> manifests, CancellationToken cancellationToken)
    {
        var result = new List<Manifest>();
        var seenIds = new HashSet<uint>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest.StateFlags is { } flags)
            {
                if ((flags & StateFullyInstalled) == 0)
                {
                    _logger.LogInformation("Steam: {AppId} ist nicht vollständig installiert (StateFlags {Flags}) und wird übersprungen.", manifest.AppId, flags);
                    continue;
                }

                if ((flags & StateUninstalling) != 0)
                {
                    _logger.LogInformation("Steam: {AppId} wird gerade deinstalliert (StateFlags {Flags}) und wird übersprungen.", manifest.AppId, flags);
                    continue;
                }
            }

            if (!_fileSystem.DirectoryExists(manifest.GameDir))
            {
                _logger.LogInformation("Steam: Spielordner {Dir} von {AppId} fehlt, Eintrag wird übersprungen.", manifest.GameDir, manifest.AppId);
                continue;
            }

            if (!seenIds.Add(manifest.AppId))
            {
                _logger.LogInformation("Steam: AppID {AppId} steht in mehreren Bibliotheken; {File} wird übersprungen.", manifest.AppId, manifest.File);
                continue;
            }

            if (!seenDirs.Add(manifest.GameDir))
            {
                _logger.LogInformation("Steam: Ordner {Dir} gehört schon zu einer anderen App; {AppId} wird übersprungen.", manifest.GameDir, manifest.AppId);
                continue;
            }

            result.Add(manifest);
        }

        return result;
    }

    private IReadOnlyDictionary<uint, SteamAppInfo>? LoadAppInfo(
        string steamPath, List<Manifest> installed, List<ScanError> errors, CancellationToken cancellationToken)
    {
        var path = Path.Combine(steamPath, "appcache", "appinfo.vdf");
        if (!_fileSystem.FileExists(path))
        {
            _logger.LogInformation("Steam: {Path} fehlt; Werkzeuge werden nur über die feste Liste und Namensregeln erkannt.", path);
            return null;
        }

        try
        {
            var data = _fileSystem.ReadAllBytes(path);
            var info = SteamAppInfoReader.Read(data, installed.Select(m => m.AppId).ToHashSet(), cancellationToken);
            if (info is null)
            {
                _logger.LogWarning("Steam: {Path} hat eine unbekannte Formatversion; Werkzeuge werden nur über die feste Liste und Namensregeln erkannt.", path);
            }

            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("Steam-App-Cache nicht lesbar; Werkzeuge werden nur über die feste Liste und Namensregeln erkannt: " + ex.Message, path));
            return null;
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("Steam-App-Cache beschädigt; Werkzeuge werden nur über die feste Liste und Namensregeln erkannt: " + ex.Message, path));
            return null;
        }
    }

    private KeyValueNode ParseFile(string path)
    {
        var text = SteamTextDecoder.Decode(_fileSystem.ReadAllBytes(path), out var usedLatin1);
        if (usedLatin1)
        {
            _logger.LogWarning("Steam: {Path} ist kein gültiges UTF-8 und wird als Latin-1 gelesen.", path);
        }

        return KeyValuesParser.Parse(text);
    }

    private static string ResolveName(Manifest manifest, SteamAppInfo? info)
    {
        var name = manifest.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.Equals(name, "appid_" + manifest.AppId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        // Steam schreibt gelegentlich den Platzhalter "appid_<AppID>" (gemessen bei Wallpaper Engine).
        var cached = info?.Name?.Trim();
        if (!string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        return Path.GetFileName(manifest.InstallDirName.TrimEnd('\\', '/'));
    }

    private static bool IsDigits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    /// <summary>
    /// installdir muss ein Ordner unterhalb von steamapps\common sein. Absolute Pfade, Laufwerksangaben und ".." würden
    /// den Installationsordner (und damit später die Prozesszuordnung) auf fremde Ordner ausdehnen.
    /// </summary>
    private static bool IsPlainRelativeFolder(string value)
    {
        if (Path.IsPathRooted(value) || value.Contains(':', StringComparison.Ordinal) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        // Abschnitte nur aus Punkten oder Leerzeichen ("..", ". .") kürzt Windows weg; sie führen aus dem Ordner heraus.
        return value.Split('\\', '/').All(part => part.Length == 0 || part.Trim(' ', '.').Length > 0);
    }

    private sealed record Manifest(uint AppId, string? Name, string InstallDirName, string GameDir, uint? StateFlags, string File, DateTimeOffset? LastPlayed);
}
