using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Config;

/// <summary>Inhalt von %APPDATA%\HdrPilot\config.json.</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public AppSettings Settings { get; set; } = new();

    public List<GameConfig> Games { get; set; } = [];

    public GameConfig? FindGame(string key) =>
        Games.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tiefe Kopie über JSON, damit Leser einen unveränderlichen Stand behalten.</summary>
    public AppConfig Clone() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, ConfigJson.Options), ConfigJson.Options)!;
}

public sealed class AppSettings
{
    /// <summary>monitorDevicePath der zu schaltenden Bildschirme. Leer: Hauptbildschirm.</summary>
    public List<string> TargetDisplays { get; set; } = [];

    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Nachlaufzeit: so lange muss kein Spielprozess mehr laufen, bevor zurückgestellt wird.</summary>
    public int GracePeriodSeconds { get; set; } = 10;

    /// <summary>Start über die App: so lange wird auf den ersten Spielprozess gewartet.</summary>
    public int LaunchTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Frühwarnung: Spielstarts aus Epic Games Launcher und Steam an deren Protokollen erkennen und HDR schon vor dem
    /// Spielprozess einschalten (nur Spiele mit „HDR automatisch“). Standard an.
    /// </summary>
    public bool LauncherSignals { get; set; } = true;

    /// <summary>
    /// Neustart-Puffer in Sekunden: Endet ein Spiel kurz nach dem Start (Hänger, Absturz), bleibt HDR so lange an, damit
    /// ein Neustart HDR schon vorfindet. Null: Standard (180 s), 0: aus.
    /// </summary>
    public int? RestartBufferSeconds { get; set; }

    public bool StartWithWindows { get; set; }

    public bool Notifications { get; set; } = true;

    /// <summary>Notlösung Win+Alt+B. Standard aus.</summary>
    public bool HotkeyFallback { get; set; }

    /// <summary>HDR-Liste von PCGamingWiki bei jedem Start abgleichen (verzögert, höchstens einmal pro Stunde, E73).</summary>
    public bool HdrListSync { get; set; } = true;

    public List<string> SearchFolders { get; set; } = [];

    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>Pflegbare Ausschlussliste für Hilfsprozesse (Glob-Muster). Null: Standardliste.</summary>
    public List<string>? HelperExecutablePatterns { get; set; }

    /// <summary>
    /// Sortierung der Spielebibliothek: "LastPlayed" (zuletzt gespielt zuerst) oder null für den Standard (Name).
    /// Bewusst Text statt Aufzählung: ein unbekannter Wert (etwa aus einer neueren Version) macht die Datei nicht
    /// unlesbar, sondern gilt als Standard.
    /// </summary>
    public string? LibrarySortOrder { get; set; }

    /// <summary>
    /// Anteil des Detailbereichs an der Breite beider Spalten der Spielebibliothek (0 bis 1). Null: Standard.
    /// Die Oberfläche begrenzt ihn beim Öffnen auf die aktuelle Fenstergröße.
    /// </summary>
    public double? LibraryDetailsRatio { get; set; }
}

/// <summary>Einstellungen je Spiel. Hängt an Quelle und ID, nicht am Namen.</summary>
public sealed class GameConfig
{
    public GameSource Source { get; set; }

    public string SourceId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string InstallDir { get; set; } = string.Empty;

    /// <summary>EXEs, die der Scanner nennt.</summary>
    public List<string> KnownExecutables { get; set; } = [];

    /// <summary>Beim ersten erkannten Lauf gemerkte EXEs (vollständige Pfade).</summary>
    public List<string> DetectedExecutables { get; set; } = [];

    /// <summary>Vom Nutzer entfernte EXEs (Dateiname oder Pfad): zählen für dieses Spiel nicht als Spielprozess.</summary>
    public List<string> ExcludedExecutables { get; set; } = [];

    /// <summary>Vom Nutzer ergänzte EXEs (Dateiname oder Pfad), auch außerhalb des Installationsordners.</summary>
    public List<string> ExtraExecutables { get; set; } = [];

    public string? LaunchCommand { get; set; }

    public string? LaunchArguments { get; set; }

    public string? LaunchWorkingDirectory { get; set; }

    /// <summary>Schalter „HDR automatisch“. Neue Spiele: aus.</summary>
    public bool HdrAuto { get; set; }

    /// <summary>monitorDevicePath oder null für die globale Einstellung.</summary>
    public string? Display { get; set; }

    /// <summary>Nach dem Spiel den gemerkten Zustand wiederherstellen.</summary>
    public bool RestoreAfter { get; set; } = true;

    /// <summary>Beim letzten Einlesen neu gefunden und noch nicht angesehen.</summary>
    public bool IsNew { get; set; } = true;

    public bool IsInstalled { get; set; } = true;

    public DateTimeOffset? FirstSeen { get; set; }

    public DateTimeOffset? LastSeen { get; set; }

    public DateTimeOffset? LastPlayed { get; set; }

    [JsonIgnore]
    public string Key => GameKey.Create(Source, SourceId);

    [JsonIgnore]
    public bool IsManual => Source == GameSource.Manual;
}

public static class ConfigJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
