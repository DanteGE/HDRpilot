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
/// Zahl der Anfragen und gefüllten Bilder, letzter Fehler, und wie viele Bilder lokal fehlen.
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
}

/// <summary>
/// SteamGridDB (E89, G10.4): einzige Ausnahme von „nur lokal“. Standardmäßig aus, nur mit eigenem API-Schlüssel
/// (DPAPI-verschlüsselt in steamgriddb-key.dat), füllt nur Bilder, die lokal fehlen; lokale und eigene Bilder bleiben
/// unangetastet. Ist es aus oder fehlt der Schlüssel, gibt es keinen einzigen Netzwerkzugriff. Ereignisse auf dem UI-Thread.
/// Änderungen an dieser Schnittstelle nur hinzufügend und mit Standardumsetzung.
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
