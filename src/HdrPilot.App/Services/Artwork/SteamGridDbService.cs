using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Was SteamGridDB vom Bilderdienst braucht (umgesetzt von <see cref="ArtworkService"/>, in Tests eine Attrappe).</summary>
public interface ISteamGridDbArtwork
{
    /// <summary>Bildarten, die lokal fehlen und keine eigene Wahl haben (nur diese werden gefüllt).</summary>
    IReadOnlyList<ArtworkKind> MissingKinds(GameConfig game);

    DateTimeOffset? SteamGridDbChecked(GameConfig game, ArtworkKind kind);

    void MarkSteamGridDbChecked(GameConfig game, ArtworkKind kind, DateTimeOffset when);

    long? SteamGridDbGameId(GameConfig game);

    void SetSteamGridDbGameId(GameConfig game, long id);

    DateTimeOffset? SteamGridDbLastRun { get; set; }

    /// <summary>Bild speichern (art\sgdb) und die Änderung melden; InvalidDataException für kein Bild.</summary>
    void StoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when);

    void FlushIndex();
}

/// <summary>Einstellungen des SteamGridDB-Dienstes.</summary>
public sealed class SteamGridDbServiceOptions
{
    public required ISteamGridDbArtwork Artwork { get; init; }

    /// <summary>Aktuelle Konfiguration (Schalter <c>settings.artwork.steamGridDb</c>, Spieleliste).</summary>
    public required Func<AppConfig> Current { get; init; }

    /// <summary>Konfiguration ändern und speichern.</summary>
    public required Action<Action<AppConfig>> Update { get; init; }

    public IFileSystem FileSystem { get; init; } = PhysicalFileSystem.Instance;

    /// <summary>steamgriddb-key.dat im Datenordner.</summary>
    public required string KeyFile { get; init; }

    /// <summary>Verschlüsselung des Schlüssels (null: DPAPI mit eigenem Schlüsselanteil).</summary>
    public ISecretProtector? Protector { get; init; }

    public IClock Clock { get; init; } = SystemClock.Instance;

    /// <summary>True, solange ein Spiel läuft (dann startet kein Lauf von selbst).</summary>
    public Func<bool> GameRunning { get; init; } = () => false;

    /// <summary>Erzeugt den HTTP-Handler eines Laufs (in Tests eine Attrappe). Wird nur mit Schalter an und Schlüssel aufgerufen.</summary>
    public Func<HttpMessageHandler>? HandlerFactory { get; init; }

    public Action<Action> PostToUi { get; init; } = action => action();

    /// <summary>Wartezeit nach dem Start der App bis zum täglichen Lauf.</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>So lange wird eine Bildart ohne Treffer nicht erneut abgefragt.</summary>
    public TimeSpan NegativeCache { get; init; } = TimeSpan.FromDays(30);

    public int MaxRequests { get; init; } = SteamGridDbClient.MaxRequests;

    /// <summary>Abstand zwischen zwei Anfragen (in Tests kürzer).</summary>
    public TimeSpan? Pause { get; init; }

    public ILogger? Logger { get; init; }
}

/// <summary>
/// SteamGridDB (E89, G10.4): einzige Ausnahme von „nur lokal“. Standardmäßig aus; nur mit eigenem API-Schlüssel
/// (DPAPI-verschlüsselt in steamgriddb-key.dat, nie im Protokoll) und nur für Bilder, die lokal fehlen und keine eigene Wahl
/// haben. Ein Lauf (nach dem Einschalten oder Speichern des Schlüssels, „Fehlende Bilder jetzt laden“, höchstens einmal am
/// Tag 60 s nach dem Start einer App mit normaler Ablage und ohne laufendes Spiel) läuft auf einem eigenen Thread mit
/// niedriger Priorität, stellt höchstens 200 Anfragen mit mindestens 1 s Abstand und merkt sich Fehlanzeigen 30 Tage.
/// Ist es aus oder fehlt der Schlüssel, entsteht kein HttpClient und keine Anfrage; nur „Prüfen“ (Klick des Nutzers)
/// fragt mit dem eingegebenen Schlüssel einmal nach.
/// </summary>
public sealed class SteamGridDbService : ISteamGridDbService
{
    private readonly SteamGridDbServiceOptions _options;
    private readonly SteamGridDbKeyStore _keys;
    private readonly ILogger _logger;
    private readonly RequestPacer _pacer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task? _running;
    private Timer? _startupTimer;
    private SteamGridDbKeyState? _keyState;
    private DateTimeOffset? _lastRun;
    private int _lastRequests;
    private int _lastFilled;
    private string? _lastError;
    private (int Hero, int Logo, int Cover)? _missing;
    private bool _missingRequested;
    private bool _disposed;

    public SteamGridDbService(SteamGridDbServiceOptions options)
    {
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _keys = new SteamGridDbKeyStore(options.FileSystem, options.KeyFile, options.Protector ?? SteamGridDbKeyStore.CreateProtector(), _logger);
        _pacer = new RequestPacer(options.Clock, options.Pause ?? SteamGridDbClient.PauseBetweenRequests);
    }

    public event EventHandler? Changed;

    /// <summary>„HDRpilot/1.4.0 (Windows desktop app; artwork)“.</summary>
    public static string UserAgent { get; } =
        "HDRpilot/" + (typeof(SteamGridDbService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0") + " (Windows desktop app; artwork)";

    /// <summary>Anzahl der erzeugten HTTP-Handler (Nachweis: aus oder ohne Schlüssel 0).</summary>
    public int HandlersCreated { get; private set; }

    public SteamGridDbStatus Status
    {
        get
        {
            var enabled = _options.Current().Settings.Artwork?.SteamGridDb ?? false;
            var key = KeyState();
            (int Hero, int Logo, int Cover)? missing;
            bool start;
            lock (_gate)
            {
                missing = _missing;
                start = !_missingRequested && !_disposed;
                _missingRequested = true;
            }

            if (start)
            {
                // Erste Anzeige in den Einstellungen: fehlende Bilder im Hintergrund zählen (nur lokal, kein Netz).
                _ = RunOnWorker(CountMissing);
            }

            lock (_gate)
            {
                return new SteamGridDbStatus(
                    enabled,
                    key,
                    _running is { IsCompleted: false },
                    _lastRun ?? SafeLastRun(),
                    _lastRequests,
                    _lastFilled,
                    _lastError,
                    missing?.Hero ?? 0,
                    missing?.Logo ?? 0,
                    missing?.Cover ?? 0);
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        _options.Update(c => c.Settings.Artwork.SteamGridDb = enabled);
        _logger.LogInformation("SteamGridDB {State}.", enabled ? "eingeschaltet" : "ausgeschaltet");
        RaiseChanged();
        if (enabled && KeyState() == SteamGridDbKeyState.Stored)
        {
            _ = RunNowAsync();
        }
    }

    public Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!SteamGridDbKeyStore.IsPlausible(key))
        {
            return Task.FromResult(SteamGridDbCheckResult.Invalid);
        }

        return RunOnWorker(() =>
        {
            using var client = CreateClient(key.Trim());
            try
            {
                return client.CheckKey(cancellationToken) ? SteamGridDbCheckResult.Valid : SteamGridDbCheckResult.Invalid;
            }
            catch (SteamGridDbException ex)
            {
                _logger.LogInformation("SteamGridDB: Prüfung des Schlüssels fehlgeschlagen ({Kind}).", ex.Kind);
                return ex.Kind == SteamGridDbErrorKind.RateLimited ? SteamGridDbCheckResult.RateLimited : SteamGridDbCheckResult.NetworkError;
            }
        });
    }

    public void SaveKey(string key)
    {
        try
        {
            _keys.Save(key);
            lock (_gate)
            {
                _keyState = SteamGridDbKeyState.Stored;
                _lastError = null;
            }

            _logger.LogInformation("SteamGridDB: Schlüssel gespeichert.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning("SteamGridDB: Schlüssel nicht gespeichert ({Error}).", ex.GetType().Name);
            lock (_gate)
            {
                _lastError = ArtworkStrings.SteamGridDb_Error_KeyNotSaved;
            }

            RaiseChanged();
            return;
        }

        RaiseChanged();
        if (_options.Current().Settings.Artwork?.SteamGridDb == true)
        {
            _ = RunNowAsync();
        }
    }

    public void RemoveKey()
    {
        _keys.Delete();
        lock (_gate)
        {
            _keyState = SteamGridDbKeyState.Missing;
            _lastError = null;
        }

        _logger.LogInformation("SteamGridDB: Schlüssel entfernt.");
        RaiseChanged();
    }

    public Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_running is { IsCompleted: false } running)
            {
                return running;
            }

            task = RunOnWorker(() => Run(cancellationToken));
            _running = task;
        }

        RaiseChanged();
        _ = task.ContinueWith(_ => RaiseChanged(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    public void ScheduleStartup(bool allowed)
    {
        if (!allowed)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _startupTimer?.Dispose();
            _startupTimer = new Timer(_ => OnStartupTimer(), null, _options.StartupDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _startupTimer?.Dispose();
            _startupTimer = null;
        }

        _lifetime.Cancel();
    }

    private SteamGridDbKeyState KeyState()
    {
        lock (_gate)
        {
            if (_keyState is { } known)
            {
                return known;
            }
        }

        var key = _keys.Load(out var unreadable);
        var state = key is not null ? SteamGridDbKeyState.Stored : unreadable ? SteamGridDbKeyState.Unreadable : SteamGridDbKeyState.Missing;
        lock (_gate)
        {
            _keyState = state;
            return state;
        }
    }

    private DateTimeOffset? SafeLastRun()
    {
        try
        {
            return _options.Artwork.SteamGridDbLastRun;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OnStartupTimer()
    {
        lock (_gate)
        {
            _startupTimer?.Dispose();
            _startupTimer = null;
        }

        try
        {
            var enabled = _options.Current().Settings.Artwork?.SteamGridDb == true;
            if (!enabled || KeyState() != SteamGridDbKeyState.Stored)
            {
                return;
            }

            if (_options.GameRunning())
            {
                _logger.LogInformation("SteamGridDB: täglicher Lauf übersprungen, ein Spiel läuft.");
                lock (_gate)
                {
                    _lastError = ArtworkStrings.SteamGridDb_Error_GameRunning;
                }

                RaiseChanged();
                return;
            }

            if (SafeLastRun() is { } last && _options.Clock.UtcNow - last < TimeSpan.FromDays(1))
            {
                return;
            }

            _ = RunNowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamGridDB: Lauf nach dem Start nicht begonnen.");
        }
    }

    /// <summary>Ein Lauf: fehlende Bilder abfragen und laden (eigener Thread). Ohne Schalter oder Schlüssel: nichts.</summary>
    private void Run(CancellationToken cancellationToken)
    {
        var config = _options.Current();
        if (config.Settings.Artwork?.SteamGridDb != true)
        {
            return;
        }

        var key = _keys.Load(out var unreadable);
        if (key is null)
        {
            lock (_gate)
            {
                _keyState = unreadable ? SteamGridDbKeyState.Unreadable : SteamGridDbKeyState.Missing;
                _lastError = unreadable ? ArtworkStrings.SteamGridDb_Error_KeyUnreadable : null;
            }

            return;
        }

        var now = _options.Clock.UtcNow;
        var work = new List<(GameConfig Game, List<ArtworkKind> Kinds)>();
        foreach (var game in config.Games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kinds = _options.Artwork.MissingKinds(game)
                .Where(k => _options.Artwork.SteamGridDbChecked(game, k) is not { } checkedAt || now - checkedAt >= _options.NegativeCache)
                .ToList();
            if (kinds.Count > 0)
            {
                work.Add((game, kinds));
            }
        }

        var filled = 0;
        string? error = null;
        var requests = 0;
        if (work.Count > 0)
        {
            using var client = CreateClient(key);
            try
            {
                foreach (var (game, kinds) in work)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        filled += Fill(client, game, kinds, now, cancellationToken);
                    }
                    catch (SteamGridDbException ex) when (ex.Kind is SteamGridDbErrorKind.Blocked or SteamGridDbErrorKind.InvalidResponse or SteamGridDbErrorKind.HttpError)
                    {
                        // Nur dieses Spiel (etwa eine kaputte Suchantwort): merken und weiter.
                        _logger.LogDebug("SteamGridDB: {Game} übersprungen ({Error}).", game.Key, ex.Kind);
                        foreach (var kind in kinds)
                        {
                            _options.Artwork.MarkSteamGridDbChecked(game, kind, now);
                        }
                    }
                }
            }
            catch (SteamGridDbException ex)
            {
                _logger.LogWarning("SteamGridDB: Lauf beendet ({Kind}) nach {Requests} Anfragen.", ex.Kind, client.RequestCount);
                error = Message(ex.Kind);
            }
            finally
            {
                requests = client.RequestCount;
            }
        }

        _options.Artwork.SteamGridDbLastRun = now;
        _options.Artwork.FlushIndex();
        lock (_gate)
        {
            _lastRun = now;
            _lastRequests = requests;
            _lastFilled = filled;
            _lastError = error;
        }

        _logger.LogInformation("SteamGridDB: Lauf beendet, {Requests} Anfragen, {Filled} Bilder geladen, {Games} Spiele mit fehlenden Bildern.", requests, filled, work.Count);
        CountMissing();
    }

    /// <summary>Füllt die fehlenden Bildarten eines Spiels; Fehlanzeigen werden gemerkt. Abbruch des Laufs über Ausnahmen.</summary>
    private int Fill(SteamGridDbClient client, GameConfig game, List<ArtworkKind> kinds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var appId = game.Source == GameSource.Steam && game.SourceId.All(char.IsAsciiDigit) ? game.SourceId : null;
        long? gameId = null;
        if (appId is null)
        {
            gameId = _options.Artwork.SteamGridDbGameId(game) ?? client.SearchGameId(game.Name, cancellationToken);
            if (gameId is not { } id)
            {
                foreach (var kind in kinds)
                {
                    _options.Artwork.MarkSteamGridDbChecked(game, kind, now);
                }

                return 0;
            }

            _options.Artwork.SetSteamGridDbGameId(game, id);
        }

        var filled = 0;
        foreach (var kind in kinds)
        {
            var assetKind = kind switch
            {
                ArtworkKind.Hero => SteamGridDbAssetKind.Hero,
                ArtworkKind.Logo => SteamGridDbAssetKind.Logo,
                _ => SteamGridDbAssetKind.Grid,
            };
            try
            {
                if (client.FirstAsset(assetKind, appId, gameId, cancellationToken) is not { } asset)
                {
                    _options.Artwork.MarkSteamGridDbChecked(game, kind, now);
                    continue;
                }

                var (data, extension) = client.Download(asset, cancellationToken);
                _options.Artwork.StoreSteamGridDbImage(game, kind, data, extension, now);
                filled++;
            }
            catch (SteamGridDbException ex) when (ex.Kind is SteamGridDbErrorKind.Blocked or SteamGridDbErrorKind.InvalidResponse or SteamGridDbErrorKind.HttpError)
            {
                _logger.LogDebug("SteamGridDB: {Kind} für {Game} übersprungen ({Error}).", kind, game.Key, ex.Kind);
                _options.Artwork.MarkSteamGridDbChecked(game, kind, now);
            }
            catch (InvalidDataException)
            {
                _logger.LogDebug("SteamGridDB: {Kind} für {Game} ist kein lesbares Bild.", kind, game.Key);
                _options.Artwork.MarkSteamGridDbChecked(game, kind, now);
            }
        }

        return filled;
    }

    /// <summary>Zählt die lokal fehlenden Bilder (nur lokal, für die Anzeige in den Einstellungen).</summary>
    private void CountMissing()
    {
        int hero = 0, logo = 0, cover = 0;
        try
        {
            foreach (var game in _options.Current().Games)
            {
                if (_lifetime.IsCancellationRequested)
                {
                    return;
                }

                foreach (var kind in _options.Artwork.MissingKinds(game))
                {
                    if (kind == ArtworkKind.Hero)
                    {
                        hero++;
                    }
                    else if (kind == ArtworkKind.Logo)
                    {
                        logo++;
                    }
                    else
                    {
                        cover++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogDebug("SteamGridDB: fehlende Bilder nicht gezählt: {Message}", ex.Message);
            return;
        }

        lock (_gate)
        {
            _missing = (hero, logo, cover);
        }

        RaiseChanged();
    }

    private SteamGridDbClient CreateClient(string key)
    {
        HandlersCreated++;
        var handler = _options.HandlerFactory?.Invoke() ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
        };
        return new SteamGridDbClient(handler, _options.Clock, key, UserAgent, _options.MaxRequests, _pacer);
    }

    private static string Message(SteamGridDbErrorKind kind) => kind switch
    {
        SteamGridDbErrorKind.Unauthorized => ArtworkStrings.SteamGridDb_Error_Unauthorized,
        SteamGridDbErrorKind.RateLimited => ArtworkStrings.SteamGridDb_Error_RateLimited,
        SteamGridDbErrorKind.RequestLimit => ArtworkStrings.SteamGridDb_Error_RequestLimit,
        SteamGridDbErrorKind.Blocked => ArtworkStrings.SteamGridDb_Error_Blocked,
        SteamGridDbErrorKind.InvalidResponse => ArtworkStrings.SteamGridDb_Error_InvalidResponse,
        SteamGridDbErrorKind.HttpError => ArtworkStrings.SteamGridDb_Error_Http,
        _ => ArtworkStrings.SteamGridDb_Error_Network,
    };

    /// <summary>Arbeit auf einem eigenen Hintergrund-Thread mit niedriger Priorität (wie die HDR-Liste).</summary>
    private Task<T> RunOnWorker<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SteamGridDB: Fehler im Hintergrund.");
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "HDRpilot SteamGridDB",
        };
        thread.Start();
        return completion.Task;
    }

    private Task RunOnWorker(Action work) => RunOnWorker(() =>
    {
        work();
        return true;
    });

    private void RaiseChanged() => _options.PostToUi(() =>
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamGridDB: Fehler in einem Empfänger von Changed.");
        }
    });
}
