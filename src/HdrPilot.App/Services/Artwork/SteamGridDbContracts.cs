using System.Windows.Media;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Zustand des gespeicherten SteamGridDB-Schlüssels.</summary>
public enum SteamGridDbKeyState
{
    Missing,
    Stored,

    /// <summary>Gespeichert, aber nicht entschlüsselbar (anderer Windows-Benutzer, Profil kopiert).</summary>
    Unreadable,
}

/// <summary>Ergebnis von „Prüfen“ (eine Anfrage an SteamGridDB, nur auf Klick des Nutzers).</summary>
public enum SteamGridDbCheckResult
{
    Valid,
    Invalid,
    NetworkError,
    RateLimited,
}

/// <summary>
/// Stand für die Einstellungen (klare Anzeige des Netzwerkzugriffs): an/aus, Schlüssel, läuft gerade, letzter Lauf mit
/// Zahl der Anfragen und gefüllten Bilder, letzter Fehler, wie viele Bilder lokal fehlen, und die letzte Suche in
/// „Bild wählen…“ (E93).
/// </summary>
public sealed record SteamGridDbStatus(
    bool Enabled,
    SteamGridDbKeyState Key,
    bool IsRunning,
    DateTimeOffset? LastRun,
    int LastRequests,
    int LastFilled,
    string? LastError,
    int MissingHero,
    int MissingLogo,
    int MissingCover)
{
    public static SteamGridDbStatus Off { get; } = new(false, SteamGridDbKeyState.Missing, false, null, 0, 0, null, 0, 0, 0);

    /// <summary>Zeit der letzten Anfrage der zuletzt beendeten Suche in „Bild wählen…“ (null: keine).</summary>
    public DateTimeOffset? LastSearch { get; init; }

    /// <summary>Anfragen (Schnittstelle und Bilder) dieser Suche.</summary>
    public int LastSearchRequests { get; init; }

    /// <summary>
    /// Nur gesetzt, wenn eine Suche „Schlüssel abgelehnt“ bekam; eine spätere erfolgreiche Anfrage, Speichern und Entfernen
    /// des Schlüssels löschen es.
    /// </summary>
    public string? LastSearchError { get; init; }
}

/// <summary>Grenzen einer Suche in „Bild wählen…“ je geöffnetem Dialog (E93); Tests verkleinern sie.</summary>
public sealed record SteamGridDbSearchLimits
{
    public static SteamGridDbSearchLimits Default { get; } = new();

    public int MaxApiRequests { get; init; } = 40;

    public int MaxImageRequests { get; init; } = 800;

    public long MaxPreviewBytes { get; init; } = 128L * 1024 * 1024;

    public int MaxFullDownloads { get; init; } = 6;

    public int MaxParallelPreviews { get; init; } = 3;

    /// <summary>Pause nach HTTP 429 (ohne automatische Wiederholung).</summary>
    public TimeSpan RateLimitPause { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>So lange wartet ein Vorschau-Thread ohne Arbeit, bevor er endet.</summary>
    public TimeSpan PreviewIdleExit { get; init; } = TimeSpan.FromSeconds(2);

    public SteamGridDbTimeouts Timeouts { get; init; } = SteamGridDbTimeouts.Default;
}

/// <summary>Wie das Spiel bei SteamGridDB zugeordnet ist.</summary>
public enum SteamGridDbMatchKind
{
    /// <summary>Über die Steam-AppID aus der Bibliothek.</summary>
    SteamAppId,

    /// <summary>Über die früher gewählte und gemerkte SteamGridDB-ID.</summary>
    Remembered,

    /// <summary>Über einen Treffer mit gleichem Namen.</summary>
    ExactName,

    /// <summary>Vom Nutzer aus der Trefferliste gewählt.</summary>
    Chosen,
}

/// <summary>Zuordnung eines Spiels: Ziel der Bilderabfragen, Anzeigename und Jahr, „verifiziert“ und „mehrdeutig“.</summary>
public sealed record SteamGridDbMatch(SteamGridDbMatchKind Kind, SteamGridDbTarget Target, string DisplayName, int? Year, bool Verified, bool Ambiguous = false);

/// <summary>Ergebnis der Namenssuche: Zuordnung (null ohne Treffer mit gleichem Namen) und bis zu 20 Treffer.</summary>
public sealed record SteamGridDbResolveResult(SteamGridDbMatch? Match, IReadOnlyList<SteamGridDbGameHit> Hits, bool SteamAppUnknown);

/// <summary>Eingefrorene, klein dekodierte Vorschau und ihr Speicherbedarf (Breite × Höhe × 4).</summary>
public sealed record SteamGridDbPreview(ImageSource Image, long DecodedBytes);

/// <summary>Volles, geprüftes Bild für „Übernehmen“.</summary>
public sealed record SteamGridDbImageData(byte[] Data, string Extension, int Width, int Height);

/// <summary>Verbrauch einer Suche.</summary>
public sealed record SteamGridDbSessionUsage(int ApiRequests, int ImageRequests, long PreviewBytes, int FullDownloads);

/// <summary>
/// Suche eines Dialogs „Bild wählen…“ bei SteamGridDB (E93). Erst die erste Anfrage erzeugt einen HTTP-Handler; jede
/// Anfrage folgt einem Klick des Nutzers. Anfragen an die Schnittstelle laufen nacheinander auf eigenen Hintergrund-Threads
/// mit dem gemeinsamen Abstand des Dienstes, Vorschauen auf höchstens drei Threads ohne Abstand. Dispose bricht alles sofort
/// ab und gibt den Client frei.
/// </summary>
public interface ISteamGridDbSearchSession : IDisposable
{
    string GameName { get; }

    bool IsSteamGame { get; }

    /// <summary>Zuordnung ohne Netz (Steam-AppID oder gemerkte ID, Anzeige = Name in der Bibliothek); sonst null.</summary>
    SteamGridDbMatch? InitialMatch { get; }

    SteamGridDbSessionUsage Usage { get; }

    /// <summary>Nach HTTP 429: bis dahin keine Anfrage.</summary>
    DateTimeOffset? BlockedUntil { get; }

    /// <summary>
    /// Namenssuche für ein Spiel ohne bekanntes Ziel: eine gemeinsame laufende Aufgabe (Token der Sitzung); eine gescheiterte
    /// oder abgebrochene wird nicht behalten. Das Token des Aufrufers beendet nur dessen Warten.
    /// </summary>
    Task<SteamGridDbResolveResult> ResolveAsync(CancellationToken cancellationToken);

    /// <summary>Eine Namenssuche nach einer Seite „Spiel unbekannt“ (unbekannte Steam-App oder veraltete gemerkte ID).</summary>
    Task<SteamGridDbResolveResult> ResolveByNameAsync(CancellationToken cancellationToken);

    /// <summary>Name und Jahr einer gemerkten Zuordnung (games/id); null bei 404 (veraltete ID).</summary>
    Task<SteamGridDbGameHit?> DescribeAsync(long gameId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SteamGridDbGameHit>> SearchGamesAsync(string term, CancellationToken cancellationToken);

    Task<SteamGridDbAssetPage> GetAssetsAsync(SteamGridDbMatch match, ArtworkKind kind, int page, CancellationToken cancellationToken);

    /// <summary>
    /// Vorschau in die Warteschlange. <paramref name="queueToken"/> nimmt sie heraus, solange sie wartet; eine laufende
    /// endet (nur das Token der Sitzung bricht eine Übertragung ab) und liefert ihr Ergebnis.
    /// </summary>
    Task<SteamGridDbPreview> LoadPreviewAsync(SteamGridDbAsset asset, int decodeWidth, CancellationToken queueToken);

    /// <summary>Volles Bild für „Übernehmen“ (höchstens 32 MB, ohne Abstand, geprüft wie beim Speichern).</summary>
    Task<SteamGridDbImageData> DownloadAsync(SteamGridDbAsset asset, ArtworkKind kind, CancellationToken cancellationToken);
}

/// <summary>
/// SteamGridDB (E89, G10.4, E93): einzige Ausnahme von „nur lokal“. Nur mit eigenem API-Schlüssel (DPAPI-verschlüsselt in
/// steamgriddb-key.dat). Der Schalter (Standard: aus) regelt nur das automatische Füllen der Bilder, die lokal fehlen;
/// lokale und eigene Bilder bleiben unangetastet. Mit Schlüssel sucht „Bild wählen…“ auf Klick auch bei ausgeschaltetem
/// Schalter (<see cref="OpenSearch"/>). Ohne Schlüssel gibt es keinen einzigen Netzwerkzugriff. Ereignisse auf dem
/// UI-Thread. Änderungen an dieser Schnittstelle nur hinzufügend und mit Standardumsetzung.
/// </summary>
public interface ISteamGridDbService : IDisposable
{
    event EventHandler? Changed;

    SteamGridDbStatus Status { get; }

    /// <summary>Schalter in den Einstellungen; schreibt <c>settings.artwork.steamGridDb</c>.</summary>
    void SetEnabled(bool enabled);

    /// <summary>„Prüfen“: eine Anfrage mit diesem Schlüssel (Netzwerk, nur auf Klick des Nutzers).</summary>
    Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Schlüssel verschlüsselt speichern (nie ins Protokoll).</summary>
    void SaveKey(string key);

    void RemoveKey();

    /// <summary>Fehlende Bilder jetzt laden (nichts, wenn aus oder ohne Schlüssel).</summary>
    Task RunNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Beim Start der App: späteren Lauf einplanen (verzögert, höchstens einmal am Tag), nur wenn <paramref name="allowed"/>
    /// (normale Ablage; mit --config-dir nie von selbst) und eingeschaltet mit Schlüssel.
    /// </summary>
    void ScheduleStartup(bool allowed);

    /// <summary>Zustand des Schlüssels ohne Nebenwirkungen (kein Zählen fehlender Bilder, kein Netz). Standard: fehlt.</summary>
    SteamGridDbKeyState KeyState => SteamGridDbKeyState.Missing;

    /// <summary>
    /// Suche für „Bild wählen…“ (E93): null ohne lesbaren Schlüssel. Erzeugt keinen HTTP-Handler; das tut erst die erste
    /// Anfrage. Gilt auch bei ausgeschaltetem automatischem Laden. Standard: null.
    /// </summary>
    ISteamGridDbSearchSession? OpenSearch(GameConfig game) => null;

    /// <summary>
    /// Zuordnung, die ohne Schlüssel und ohne Netz feststeht (Steam-AppID oder gemerkte ID, Anzeige = Name in der
    /// Bibliothek), für die Spielwahl vor dem ersten Klick; die Sitzung (<see cref="OpenSearch"/>) entsteht erst mit ihm.
    /// Sonst null. Standard: null.
    /// </summary>
    SteamGridDbMatch? PeekMatch(GameConfig game) => null;

    /// <summary>
    /// Eine vom Nutzer gewählte und übernommene Zuordnung merken (nur Spiele ohne Steam-AppID; Steam: nichts). Standard:
    /// nichts.
    /// </summary>
    void RememberMatch(GameConfig game, long steamGridDbGameId)
    {
    }
}

/// <summary>SteamGridDB nicht verfügbar (Tests, Gerüst): immer aus, nie ein Netzwerkzugriff.</summary>
public sealed class NullSteamGridDbService : ISteamGridDbService
{
    public static NullSteamGridDbService Instance { get; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public SteamGridDbStatus Status => SteamGridDbStatus.Off;

    public void SetEnabled(bool enabled)
    {
    }

    public Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(SteamGridDbCheckResult.NetworkError);

    public void SaveKey(string key)
    {
    }

    public void RemoveKey()
    {
    }

    public Task RunNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void ScheduleStartup(bool allowed)
    {
    }

    public void Dispose()
    {
    }
}
