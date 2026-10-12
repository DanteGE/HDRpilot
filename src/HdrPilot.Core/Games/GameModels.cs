using System.Text.Json.Serialization;

namespace HdrPilot.Core.Games;

[JsonConverter(typeof(JsonStringEnumConverter<GameSource>))]
public enum GameSource
{
    Steam,
    Epic,
    Xbox,
    Gog,
    Ubisoft,
    EA,
    BattleNet,
    Manual,
}

public static class GameKey
{
    /// <summary>Dauerhafter Schlüssel eines Spiels: Quelle und ID in der Quelle, nicht der Name.</summary>
    public static string Create(GameSource source, string sourceId) => source + ":" + sourceId;
}

/// <summary>Ein von einem Scanner gefundenes, installiertes Spiel.</summary>
public sealed record GameEntry
{
    public required GameSource Source { get; init; }

    /// <summary>ID in der Quelle, etwa Steam-AppID, Epic-AppName, Xbox-PackageFamilyName, GOG-gameID.</summary>
    public required string SourceId { get; init; }

    public required string Name { get; init; }

    /// <summary>Absoluter Installationsordner, über <see cref="Util.PathUtil.Normalize"/> vereinheitlicht.</summary>
    public required string InstallDir { get; init; }

    /// <summary>Bekannte EXE-Dateien als absolute Pfade (kann leer sein, wenn die Quelle keine nennt).</summary>
    public IReadOnlyList<string> Executables { get; init; } = [];

    /// <summary>Start-Befehl: URI (steam://…, com.epicgames.launcher://…, shell:AppsFolder\…, uplay://…) oder EXE-Pfad.</summary>
    public string? LaunchCommand { get; init; }

    /// <summary>Argumente, nur wenn <see cref="LaunchCommand"/> eine EXE ist.</summary>
    public string? LaunchArguments { get; init; }

    /// <summary>Arbeitsordner beim Start über die EXE.</summary>
    public string? LaunchWorkingDirectory { get; init; }

    /// <summary>Zuletzt gespielt laut Launcher (etwa Steam "LastPlayed"), sonst null. Für „zuletzt verwendet“.</summary>
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>
    /// Symbol-Hinweise des Launchers, beste Quelle zuerst (E86): absolute Pfade auf .ico, .exe oder Bilder, die beim
    /// Einlesen existierten (Steam-Symbol, goggame-ID.ico, Xbox-Logos …), höchstens <see cref="IconFiles.MaxCandidates"/>.
    /// Leer, wenn der Launcher nichts Besseres als die Spiel-EXE kennt.
    /// </summary>
    public IReadOnlyList<string> IconCandidates { get; init; } = [];

    [JsonIgnore]
    public string Key => GameKey.Create(Source, SourceId);
}

/// <summary>Nicht-fataler Fehler eines Scanners, etwa eine defekte Manifestdatei.</summary>
public sealed record ScanError(string Message, string? Path = null);

public sealed record ScanResult
{
    public required GameSource Source { get; init; }

    public required string ScannerName { get; init; }

    /// <summary>True, wenn Spuren des Launchers gefunden wurden. Ein fehlender Launcher ist kein Fehler.</summary>
    public bool LauncherDetected { get; init; }

    public IReadOnlyList<GameEntry> Games { get; init; } = [];

    public IReadOnlyList<ScanError> Errors { get; init; } = [];
}

public sealed record ScanOptions
{
    /// <summary>Vom Nutzer angegebene Suchordner (EA-Erkennung, Kandidaten-Vorschläge).</summary>
    public IReadOnlyList<string> SearchFolders { get; init; } = [];

    public static ScanOptions Default { get; } = new();
}

/// <summary>Ein Scanner je Quelle. Darf nicht werfen, wenn der Launcher fehlt: dann leere Liste ohne Fehler.</summary>
public interface IGameScanner
{
    GameSource Source { get; }

    string Name { get; }

    ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Vorschlag aus einem Suchordner: wird nie ungefragt übernommen.</summary>
public sealed record GameCandidate
{
    public required string Name { get; init; }

    public required string Folder { get; init; }

    public IReadOnlyList<string> Executables { get; init; } = [];
}
