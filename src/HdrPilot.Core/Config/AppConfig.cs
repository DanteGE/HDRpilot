using System.Buffers;
using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Einträge aus <c>games</c> mit einer Quelle, die diese Version nicht kennt (etwa aus einer neueren Version, E91): Sie
    /// fehlen in <see cref="Games"/>, bleiben aber unverändert erhalten und werden beim Speichern wieder angehängt
    /// (<see cref="ConfigJson"/>). So macht eine neue Quelle die Datei nie „defekt“, und nichts geht verloren.
    /// </summary>
    [JsonIgnore]
    public List<JsonElement> UnknownSourceGames { get; set; } = [];

    public GameConfig? FindGame(string key) =>
        Games.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Schlüssel aller Spiele der Datei, auch der mit unbekannter Quelle (<c>&lt;source&gt;:&lt;sourceId&gt;</c>, so wie die
    /// Version sie bildet, die die Quelle kennt): Das Aufräumen der Bilder löscht so nie eigene Bilder oder Logos eines Spiels,
    /// das nur diese Version nicht kennt (E91). Einträge ohne Quelle oder ID als Text haben keinen Schlüssel.
    /// </summary>
    public IReadOnlyList<string> AllGameKeys() =>
        Games.Select(g => g.Key).Concat(UnknownSourceGames.Select(UnknownSourceKey).OfType<string>()).ToList();

    private static string? UnknownSourceKey(JsonElement game) =>
        game.ValueKind == JsonValueKind.Object
        && game.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String && source.GetString() is { Length: > 0 } name
        && game.TryGetProperty("sourceId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } sourceId
            ? name + ":" + sourceId
            : null;

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
    /// Oberfläche begrenzt die Höhe auf das Fenster, der gemerkte Anteil bleibt dabei unverändert. Raster und Regal merken ihre
    /// Höhe getrennt (<see cref="LibraryGridHeroRatio"/>, <see cref="LibraryShelfHeroRatio"/>).
    /// </summary>
    public double? LibraryHeroRatio { get; set; }

    /// <summary>
    /// Höhe des Titelbereichs im Raster der Spielebibliothek als Anteil der Fensterhöhe (0 bis 1), gemerkt erst nach dem
    /// Ziehgriff (Wunsch des Nutzers 2026-10-22: jede Ansicht merkt ihre eigene Höhe). Null: Standard des Rasters. Die
    /// Oberfläche begrenzt die Höhe so, dass eine ganze Kachelreihe zu sehen bleibt; der gemerkte Anteil bleibt dabei
    /// unverändert. Ältere Konfigurationen ohne das Feld gelten unverändert.
    /// </summary>
    public double? LibraryGridHeroRatio { get; set; }

    /// <summary>
    /// Höhe des Titelbereichs im Regal der Spielebibliothek als Anteil der Fensterhöhe (0 bis 1), gemerkt erst nach dem
    /// Ziehgriff (Wunsch des Nutzers 2026-10-22). Null: Standard des Regals. Die Oberfläche begrenzt die Höhe zwischen dem Platz
    /// für Automatik-Karte und großen Titel und der kleinsten Kachel; der gemerkte Anteil bleibt dabei unverändert.
    /// </summary>
    public double? LibraryShelfHeroRatio { get; set; }

    /// <summary>
    /// Gemerkte Ansicht der Spielebibliothek (E89): "List" (Standard), "Grid" (Raster) oder "Shelf" (Regal). Text statt
    /// Aufzählung wie bei <see cref="LibrarySortOrder"/>; Unbekanntes gilt als Liste.
    /// </summary>
    public string? LibraryView { get; set; }

    /// <summary>Vorschau in den Zeilen der Liste: "Image" (Standard, Bild) oder "Icon" (Symbol).</summary>
    public string? LibraryThumbnail { get; set; }

    /// <summary>
    /// Werkzeugleiste der Spielebibliothek eingeklappt (Kapsel auf der Bildkante statt Leiste, Wunsch des Nutzers 2026-10-17).
    /// Ein Zustand für alle drei Ansichten, gespeichert beim Umschalten, beim Öffnen ohne Animation hergestellt. Standard
    /// (auch für ältere Konfigurationen): ausgeklappt.
    /// </summary>
    public bool LibraryToolbarCollapsed { get; set; }

    /// <summary>
    /// Nicht installierte Spiele in der Bibliothek ausblenden (Wunsch des Nutzers 2026-10-18, E90). Ausgeblendete Spiele
    /// zählen nirgends mit (Kopf, Quellen, Kapsel, Leerzustand), „HDR automatisch“ ordnet sie weiter zu. Eingestellt in den
    /// Einstellungen (Karte „Bibliothek“), übernommen mit „Speichern“. Standard (auch für ältere Konfigurationen): aus.
    /// </summary>
    public bool HideUninstalledGames { get; set; }

    /// <summary>
    /// Gelernte Ordner des Quiver Launchers (E91, versteckt, ohne Oberfläche): Sieht die Überwachung
    /// <c>…\current\QuiverLauncher.exe</c> starten, merkt HDRpilot sich den Ordner darüber; das Einlesen prüft ihn dann
    /// zuerst. Null (Standard, auch für ältere Konfigurationen): keiner, das Feld fehlt dann in der Datei.
    /// </summary>
    public List<string>? QuiverFolders { get; set; }

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
    /// Cover-Hinweise des Launchers aus dem letzten Einlesen (siehe <see cref="GameEntry.CoverCandidates"/>, bisher nur
    /// Quiver, E91). Null, wenn es keine gibt: So bekommen die übrigen Spiele kein leeres Feld in der Datei.
    /// </summary>
    public List<string>? CoverCandidates { get; set; }

    /// <summary>Art der Installation laut Launcher, wenn sie abweicht (siehe <see cref="GameEntry.InstallKind"/>); sonst null.</summary>
    public string? InstallKind { get; set; }

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
        Converters = { new JsonStringEnumConverter(), new AppConfigConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>
/// Liest und schreibt <see cref="AppConfig"/> wie bisher, aber tolerant gegenüber Spielen mit unbekannter Quelle (E91):
/// Ein Eintrag in <c>games</c>, dessen <c>source</c> diese Version nicht als <see cref="GameSource"/> kennt (etwa „Quiver“
/// in 1.4.0 oder eine Quelle einer späteren Version), machte früher die ganze Datei ungültig; sie wurde als „defekt“
/// gesichert und die Bibliothek begann leer. Jetzt fehlt nur dieser Eintrag in <see cref="AppConfig.Games"/>; er wird
/// unverändert in <see cref="AppConfig.UnknownSourceGames"/> gehalten und beim Schreiben ans Ende von <c>games</c> gehängt.
/// Alles andere (auch andere Formfehler) bleibt wie bisher.
/// </summary>
internal sealed class AppConfigConverter : JsonConverter<AppConfig>
{
    private const string GamesProperty = "games";
    private const string SourceProperty = "source";

    /// <summary>Namen der bekannten Quellen (ohne Groß-/Kleinschreibung): der schnelle Weg ohne Probe-Deserialisierung.</summary>
    private static readonly HashSet<string> KnownSources = new(Enum.GetNames<GameSource>(), StringComparer.OrdinalIgnoreCase);

    private readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _inner = new();

    public override AppConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var inner = Inner(options);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root.Deserialize<AppConfig>(inner);
        }

        // Wie die Deserialisierung gilt bei doppeltem „games“ das letzte; gefiltert werden alle.
        List<JsonElement>? unknown = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(GamesProperty) && property.Value.ValueKind == JsonValueKind.Array)
            {
                unknown = property.Value.EnumerateArray().Where(g => IsUnknownSource(g, inner)).Select(g => g.Clone()).ToList();
            }
        }

        if (unknown is not { Count: > 0 })
        {
            return root.Deserialize<AppConfig>(inner);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals(GamesProperty) && property.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteStartArray();
                    foreach (var game in property.Value.EnumerateArray().Where(g => !IsUnknownSource(g, inner)))
                    {
                        game.WriteTo(writer);
                    }

                    writer.WriteEndArray();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        var config = JsonSerializer.Deserialize<AppConfig>(buffer.WrittenSpan, inner);
        if (config is not null)
        {
            config.UnknownSourceGames = unknown;
        }

        return config;
    }

    public override void Write(Utf8JsonWriter writer, AppConfig value, JsonSerializerOptions options)
    {
        var inner = Inner(options);
        if (value.UnknownSourceGames is not { Count: > 0 } unknown)
        {
            JsonSerializer.Serialize(writer, value, inner);
            return;
        }

        var element = JsonSerializer.SerializeToElement(value, inner);
        var wroteGames = false;
        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(GamesProperty))
            {
                property.WriteTo(writer);
                continue;
            }

            writer.WritePropertyName(property.Name);
            writer.WriteStartArray();
            foreach (var game in property.Value.EnumerateArray().Concat(unknown))
            {
                game.WriteTo(writer);
            }

            writer.WriteEndArray();
            wroteGames = true;
        }

        if (!wroteGames)
        {
            writer.WritePropertyName(GamesProperty);
            writer.WriteStartArray();
            foreach (var game in unknown)
            {
                game.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// True, wenn der Eintrag ein Objekt mit <c>source</c> ist, das keine bekannte Quelle ergibt: unbekannter Name, Zahl ohne
    /// Quelle, null. Entschieden wird wie beim Lesen selbst (mit denselben Optionen); ein bekannter Name braucht keine Probe.
    /// </summary>
    private static bool IsUnknownSource(JsonElement game, JsonSerializerOptions inner)
    {
        if (game.ValueKind != JsonValueKind.Object || !game.TryGetProperty(SourceProperty, out var source))
        {
            return false;
        }

        if (source.ValueKind == JsonValueKind.String && source.GetString() is { } name && KnownSources.Contains(name))
        {
            return false;
        }

        try
        {
            return !Enum.IsDefined(source.Deserialize<GameSource>(inner));
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>Dieselben Optionen ohne diesen Konverter (sonst riefe er sich selbst auf).</summary>
    private JsonSerializerOptions Inner(JsonSerializerOptions options) => _inner.GetValue(options, outer =>
    {
        var copy = new JsonSerializerOptions(outer);
        foreach (var converter in copy.Converters.OfType<AppConfigConverter>().ToList())
        {
            copy.Converters.Remove(converter);
        }

        return copy;
    });
}
