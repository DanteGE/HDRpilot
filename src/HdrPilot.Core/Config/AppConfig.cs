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
    /// <summary>Obergrenze für <see cref="RestartBufferSeconds"/>: 30:00 (größere Werte gelten als 30:00).</summary>
    public const int RestartBufferMaxSeconds = 1800;

    /// <summary>monitorDevicePath der zu schaltenden Bildschirme. Leer: Hauptbildschirm.</summary>
    public List<string> TargetDisplays { get; set; } = [];

    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Nachlaufzeit: so lange muss kein Spielprozess mehr laufen, bevor zurückgestellt wird.</summary>
    public int GracePeriodSeconds { get; set; } = 10;

    /// <summary>Start über die App: so lange wird auf den ersten Spielprozess gewartet.</summary>
    public int LaunchTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Frühwarnung: Spielstarts aus Epic Games Launcher und Steam an deren Protokollen erkennen und HDR schon vor dem
    /// Spielprozess einschalten (nur Spiele mit „HDR automatisch“); für Spiele mit „Windows Auto HDR“ auch den globalen
    /// Auto-HDR-Schalter (E88). Standard an.
    /// </summary>
    public bool LauncherSignals { get; set; } = true;

    /// <summary>
    /// Neustart-Puffer in Sekunden: Endet ein Spiel kurz nach dem Start (Hänger, Absturz), bleibt HDR so lange an, damit
    /// ein Neustart HDR schon vorfindet. Null: Standard (60 s), 0: aus, höchstens <see cref="RestartBufferMaxSeconds"/>.
    /// In den Einstellungen mit Minuten und Sekunden einstellbar (E85), gilt ab dem nächsten Spielende.
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
    /// Anteil des Detailbereichs im alten Design der Spielebibliothek (vor E89). Wird nicht mehr gelesen: Im neuen Design hat
    /// der Detailbereich eine andere Standardbreite, eine alte Aufteilung verschöbe Raster und Kopfzeile
    /// (<see cref="LibraryPaneRatio"/>). Bleibt nur erhalten, damit ältere Versionen ihre Aufteilung behalten.
    /// </summary>
    public double? LibraryDetailsRatio { get; set; }

    /// <summary>
    /// Anteil des Detailbereichs an der Breite beider Spalten der Spielebibliothek im Design „Showcase Hero“ (0 bis 1),
    /// gemerkt erst, wenn der Trenner gezogen wurde. Null: Standardbreite des Prototyps. Die Oberfläche begrenzt ihn beim
    /// Öffnen auf die aktuelle Fenstergröße.
    /// </summary>
    public double? LibraryPaneRatio { get; set; }

    /// <summary>
    /// Höhe des Titelbereichs in der Liste der Spielebibliothek als Anteil der Fensterhöhe (0 bis 1), gemerkt erst, wenn der
    /// Ziehgriff gezogen oder per Tastatur verstellt wurde (Wunsch des Nutzers 2026-10-16). Null: Standard (ein Drittel). Die
    /// Oberfläche begrenzt die Höhe auf das Fenster, der gemerkte Anteil bleibt dabei unverändert.
    /// </summary>
    public double? LibraryHeroRatio { get; set; }

    /// <summary>
    /// Gemerkte Ansicht der Spielebibliothek (E89): "List" (Standard), "Grid" (Raster) oder "Shelf" (Regal). Text statt
    /// Aufzählung wie bei <see cref="LibrarySortOrder"/>; Unbekanntes gilt als Liste.
    /// </summary>
    public string? LibraryView { get; set; }

    /// <summary>Vorschau in den Zeilen der Liste: "Image" (Standard, Bild) oder "Icon" (Symbol).</summary>
    public string? LibraryThumbnail { get; set; }

    /// <summary>Darstellung: Farbschema, Thema, Effekte (E89). Ältere Konfigurationen: Standardwerte.</summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Spielbilder: SteamGridDB (Standard aus) (E89).</summary>
    public ArtworkSettings Artwork { get; set; } = new();
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

    /// <summary>
    /// Schalter „Windows Auto HDR“ (E88): Windows rechnet das SDR-Bild des Spiels in HDR um. Unabhängig von
    /// <see cref="HdrAuto"/>; HDRpilot schaltet dafür den globalen Auto-HDR-Schalter von Windows direkt vor jedem Start
    /// ein, den es vorher kennt (Starten, Verknüpfung, Steam-Startoption, Frühwarnung), und danach zurück.
    /// Neue und ältere Einträge: aus.
    /// </summary>
    public bool AutoHdr { get; set; }

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

    /// <summary>
    /// Symbol-Hinweise des Launchers aus dem letzten Einlesen (siehe <see cref="GameEntry.IconCandidates"/>). Gespeichert,
    /// damit die Bibliothek auch vor dem Ende des Einlesens beim Start das richtige Symbol zeigt; nur vorhandene
    /// Dateien, meist eine je Spiel (E86).
    /// </summary>
    public List<string> IconCandidates { get; set; } = [];

    /// <summary>
    /// Eigenes Symbol (Rechtsklick „Symbol ändern…“): Dateiname einer PNG im Ordner icons des Datenordners, hat Vorrang
    /// vor allen automatischen Quellen. Null: automatisch. Fehlt die Datei (etwa auf einem anderen PC), gilt automatisch.
    /// </summary>
    public string? CustomIcon { get; set; }

    /// <summary>
    /// Eigene Bildwahl („Bild wählen…“, E89) je Bildart; null: alles automatisch (lokale Launcher-Bilder, sonst erzeugt).
    /// </summary>
    public GameArtworkChoice? Artwork { get; set; }

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
