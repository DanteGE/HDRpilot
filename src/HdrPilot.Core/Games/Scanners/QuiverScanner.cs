using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Common;
using HdrPilot.Core.Games.Scanners.Quiver;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Quiver Launcher (E91, portabel, MIT; github.com/tgeorgiadis/quiver-launcher). Den Ordner findet
/// <see cref="QuiverRootFinder"/>. Je Ordner liest der Scanner <c>settings.json</c> (eigener Apps-Ordner <c>AppsPath</c>) und
/// <c>apps.json</c>; jedes Spiel liegt in <c>&lt;Apps&gt;\&lt;folderName&gt;</c> oder in seinem <c>installPath</c>. Schlüssel ist
/// <c>Quiver:&lt;folderName&gt;</c> (Trenner, „..“ und ungültige Zeichen abgelehnt). Installiert ist ein Spiel wie in Quiver:
/// Ordner da, keine <c>install-incomplete.txt</c>, mindestens eine EXE, BAT oder CMD; die Start-EXE wählt
/// <see cref="QuiverExecutables"/>. Per Windows Installer installierte Spiele (<c>windows-installer.json</c>) starten über
/// die dort verknüpfte EXE; ihr Ordner gilt als Installationsordner. Gestartet wird wie in Quiver direkt über die EXE mit
/// ihrem Ordner als Arbeitsordner, ohne Argumente (nicht über <c>QuiverLauncher.exe --run</c>). Cover aus den Zwischenspeichern
/// von Quiver (<see cref="QuiverArtwork"/>). Kommt ein Spiel in zwei Quiver-Ordnern vor (etwa während des Umzugs von 2.4.x
/// auf 3.x, den Quiver selbst beschreibt), gilt der Ordner im Aufbau ab 3.0, dann der zuletzt gespielte, dann der zuerst
/// gefundene (gelernte zuerst). Liest nur, Dateien geteilt geöffnet, schreibt nichts, kein Netz. Details: docs\scanner\quiver.md.
/// </summary>
public sealed class QuiverScanner : IGameScanner
{
    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public QuiverScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Quiver;

    public string Name => "Quiver Launcher";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = new QuiverRootFinder(_fileSystem, _registry, _environment, _logger).Find(options, cancellationToken);
        var found = new List<(GameEntry Game, QuiverRootHit Root)>();
        var errors = new List<ScanError>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            found.AddRange(ReadRoot(root, errors, cancellationToken).Select(game => (game, root)));
        }

        // Doppelt (zwei Quiver-Ordner mit demselben folderName): Aufbau ab 3.0 vor der alten Struktur, dann der zuletzt
        // gespielte, dann der zuerst gefundene. Die Reihenfolge der Spiele bleibt die des ersten Fundes.
        var games = new List<GameEntry>();
        foreach (var group in found.GroupBy(f => f.Game.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            var best = group
                .OrderByDescending(f => f.Root.Layout == QuiverLayout.Velopack)
                .ThenByDescending(f => f.Game.LastPlayed ?? DateTimeOffset.MinValue)
                .First();
            foreach (var other in group.Where(f => !ReferenceEquals(f.Game, best.Game)))
            {
                _logger.LogInformation("Quiver: {Folder} kommt auch in {Root} vor; es gilt der Eintrag aus {Chosen}.", other.Game.SourceId, other.Root.Folder, best.Root.Folder);
            }

            games.Add(best.Game);
        }

        return new ScanResult
        {
            Source = Source,
            ScannerName = Name,
            LauncherDetected = roots.Count > 0,
            Games = games,
            Errors = errors,
        };
    }

    private List<GameEntry> ReadRoot(QuiverRootHit root, List<ScanError> errors, CancellationToken cancellationToken)
    {
        var result = new List<GameEntry>();
        var appsFolder = AppsFolder(root.Folder, errors);
        var appsJson = Path.Combine(root.Folder, QuiverRoot.AppsJson);
        if (!_fileSystem.FileExists(appsJson))
        {
            _logger.LogInformation("Quiver: {Root} hat noch keine Bibliothek (apps.json fehlt).", root.Folder);
            return result;
        }

        var problems = new List<(int Index, string Problem)>();
        IReadOnlyList<QuiverApp> apps;
        try
        {
            apps = QuiverLibrary.ReadApps(ReadJson(appsJson), problems);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("Quiver-Bibliothek defekt: " + ex.Message, appsJson));
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"Quiver-Bibliothek nicht lesbar: {ex.GetType().Name}: {ex.Message}", appsJson));
            return result;
        }

        foreach (var (index, problem) in problems)
        {
            errors.Add(new ScanError($"Quiver-Eintrag {index + 1} unbrauchbar: {problem}", appsJson));
        }

        var folderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadApp(root, appsFolder, app, appsJson, errors) is not { } game)
            {
                continue;
            }

            if (!folderNames.Add(game.SourceId))
            {
                _logger.LogInformation("Quiver: folderName {Folder} steht in {AppsJson} doppelt; es gilt der erste Eintrag.", game.SourceId, appsJson);
                continue;
            }

            result.Add(game);
        }

        return result;
    }

    /// <summary>
    /// Apps-Ordner: <c>AppsPath</c> aus settings.json, wenn absolut, auch eine Laufwerkswurzel (Quiver nimmt den Wert
    /// unverändert, die Spiele liegen dann in <c>G:\&lt;folderName&gt;</c>; ein Spielordner darunter ist nie selbst eine
    /// Wurzel, weil <c>folderName</c> nie leer ist); sonst <c>&lt;Quiver&gt;\Apps</c>. Eine defekte settings.json oder ein
    /// relativer <c>AppsPath</c> wird gemeldet, es gilt dann der Standardordner (so liegen die Spiele fast immer).
    /// </summary>
    private string AppsFolder(string root, List<ScanError> errors)
    {
        var standard = Path.Combine(root, QuiverRoot.DefaultAppsFolder);
        var settings = Path.Combine(root, QuiverRoot.SettingsJson);
        if (!_fileSystem.FileExists(settings))
        {
            return standard;
        }

        string? appsPath;
        try
        {
            appsPath = QuiverLibrary.ReadAppsPath(ReadJson(settings));
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError("Quiver-Einstellungen defekt, es gilt der Ordner Apps: " + ex.Message, settings));
            return standard;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"Quiver-Einstellungen nicht lesbar, es gilt der Ordner Apps: {ex.GetType().Name}: {ex.Message}", settings));
            return standard;
        }

        if (appsPath is null)
        {
            return standard;
        }

        var folder = PathUtil.Normalize(appsPath);
        if (folder.Length == 0 || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(folder))
        {
            errors.Add(new ScanError($"Quiver-Einstellung AppsPath unbrauchbar, es gilt der Ordner Apps: \"{folder}\" ist kein gültiger absoluter Pfad.", settings));
            return standard;
        }

        return folder;
    }

    private GameEntry? ReadApp(QuiverRootHit root, string appsFolder, QuiverApp app, string appsJson, List<ScanError> errors)
    {
        var folderName = app.FolderName;
        if (ValidateFolderName(folderName) is { } invalid)
        {
            errors.Add(new ScanError($"Quiver-Eintrag \"{app.DisplayName}\" abgelehnt: {invalid}", appsJson));
            return null;
        }

        string gameFolder;
        if (!string.IsNullOrWhiteSpace(app.InstallPath))
        {
            if (InstallPaths.NormalizeInstallDir(app.InstallPath, out var problem) is not { } custom)
            {
                errors.Add(new ScanError($"Quiver-Eintrag \"{app.DisplayName}\": installPath unbrauchbar: {problem}", appsJson));
                return null;
            }

            gameFolder = custom;
        }
        else
        {
            gameFolder = PathUtil.Normalize(Path.Combine(appsFolder, folderName!));
        }

        var receipt = Path.Combine(gameFolder, QuiverLibrary.InstallerReceiptFile);
        if (_fileSystem.FileExists(receipt))
        {
            return ReadInstalled(root, app, folderName!, gameFolder, receipt, errors);
        }

        var choice = QuiverExecutables.Choose(_fileSystem, gameFolder, name => HelperExecutables.IsHelper(name));
        if (!choice.Installed)
        {
            _logger.LogInformation("Quiver: {Folder} übersprungen ({Reason}): {GameFolder}.", folderName, choice.Note, gameFolder);
            return null;
        }

        if (choice.Chosen is null)
        {
            _logger.LogInformation("Quiver: {Folder} ohne Start-Befehl ({Reason}); die Erkennung über den Spielordner wirkt.", folderName, choice.Note);
        }

        var exeFolder = choice.Chosen is null ? null : Path.GetDirectoryName(choice.Chosen);
        return new GameEntry
        {
            Source = GameSource.Quiver,
            SourceId = folderName!,
            Name = app.DisplayName,
            InstallDir = gameFolder,
            Executables = choice.Executables,
            LaunchCommand = choice.Chosen,
            LaunchWorkingDirectory = exeFolder,
            LastPlayed = QuiverExecutables.LastPlayed(_fileSystem, [exeFolder, gameFolder]),
            CoverCandidates = QuiverArtwork.CoverCandidates(_fileSystem, root.Folder, folderName!, app.IconUrl),
        };
    }

    /// <summary>
    /// Per Windows Installer installiert: Das Programm liegt in seinem eigenen Ordner, Quiver hält im Spielordner nur die
    /// Verknüpfung. Nur „Linked“ mit einer vorhandenen EXE (absoluter Pfad) gilt als installiert; ihr Ordner wird zum
    /// Installationsordner (Laufwerkswurzeln und relative Pfade abgelehnt).
    /// </summary>
    private GameEntry? ReadInstalled(QuiverRootHit root, QuiverApp app, string folderName, string metadataFolder, string receipt, List<ScanError> errors)
    {
        QuiverInstallerReceipt link;
        try
        {
            link = QuiverLibrary.ReadInstallerReceipt(ReadJson(receipt));
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ScanError($"Quiver-Eintrag \"{app.DisplayName}\": Verknüpfung des Windows Installers defekt: {ex.Message}", receipt));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add(new ScanError($"Quiver-Eintrag \"{app.DisplayName}\": Verknüpfung des Windows Installers nicht lesbar: {ex.GetType().Name}: {ex.Message}", receipt));
            return null;
        }

        var exe = string.IsNullOrWhiteSpace(link.ExecutablePath) ? null : PathUtil.Normalize(link.ExecutablePath);
        if (exe is null || !Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !_fileSystem.FileExists(exe))
        {
            _logger.LogInformation("Quiver: {Folder} übersprungen (Windows Installer, Phase {Phase}, keine vorhandene EXE verknüpft).", folderName, link.Phase);
            return null;
        }

        if (InstallPaths.NormalizeInstallDir(Path.GetDirectoryName(exe), out var problem) is not { } installDir)
        {
            errors.Add(new ScanError($"Quiver-Eintrag \"{app.DisplayName}\": verknüpfte EXE unbrauchbar: {problem}", receipt));
            return null;
        }

        return new GameEntry
        {
            Source = GameSource.Quiver,
            SourceId = folderName,
            Name = app.DisplayName,
            InstallDir = installDir,
            Executables = [exe],
            LaunchCommand = exe,
            LaunchWorkingDirectory = installDir,
            LastPlayed = QuiverExecutables.LastPlayed(_fileSystem, [metadataFolder]),
            CoverCandidates = QuiverArtwork.CoverCandidates(_fileSystem, root.Folder, folderName, app.IconUrl),
            InstallKind = InstallKinds.WindowsInstaller,
        };
    }

    /// <summary>
    /// Null, wenn <paramref name="folderName"/> als Schlüssel und Ordnername taugt; sonst die Begründung: leer, genau „.“,
    /// „..“ (auch als Teil des Namens), Trenner, Laufwerksangabe oder ein im Dateinamen ungültiges Zeichen. So kann ein
    /// Eintrag nie aus dem Apps-Ordner hinauszeigen.
    /// </summary>
    internal static string? ValidateFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "folderName fehlt.";
        }

        if (folderName == ".")
        {
            return "folderName ist „.“.";
        }

        if (folderName.Contains("..", StringComparison.Ordinal))
        {
            return $"folderName \"{folderName}\" enthält „..“.";
        }

        if (folderName.IndexOfAny(['\\', '/', ':']) >= 0 || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"folderName \"{folderName}\" enthält einen Pfadtrenner oder ein ungültiges Zeichen.";
        }

        return folderName.EndsWith('.') || folderName.EndsWith(' ') ? $"folderName \"{folderName}\" endet mit Punkt oder Leerzeichen." : null;
    }

    /// <summary>JSON-Datei von Quiver geteilt lesen; größer als <see cref="QuiverLibrary.MaxJsonBytes"/>: defekt.</summary>
    private byte[] ReadJson(string path)
    {
        var read = _fileSystem.ReadShared(path, QuiverLibrary.MaxJsonBytes);
        return read.IsComplete ? read.Bytes : throw new InvalidDataException($"Datei größer als {QuiverLibrary.MaxJsonBytes / (1024 * 1024)} MB.");
    }
}
