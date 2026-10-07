using System.Net;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>Stand des Abgleichs für die Einstellungen.</summary>
/// <param name="CheckedGames">Spiele der Bibliothek mit einem Ergebnis (unter ihrem aktuellen Namen).</param>
/// <param name="AccountUser">Gespeichertes Konto („Name@BotName“) oder null.</param>
public sealed record HdrListStatus(
    DateTimeOffset? LastSuccessAt,
    PcgwErrorKind? LastError,
    int CheckedGames,
    int TotalGames,
    bool AccountMode,
    bool IsRunning,
    string? AccountUser);

/// <summary>Was Oberfläche und Start von der HDR-Liste brauchen (in Tests eine Attrappe).</summary>
public interface IHdrListService
{
    /// <summary>Abgleich begonnen oder beendet, Konto geändert. Kommt auf einem beliebigen Thread.</summary>
    event EventHandler? Changed;

    bool IsRunning { get; }

    HdrListStatus GetStatus();

    /// <summary>HDR-Unterstützung je Spielschlüssel (ohne „unbekannt“); leer, solange nichts abgeglichen ist.</summary>
    IReadOnlyDictionary<string, HdrSupportStatus> LoadStatuses();

    /// <summary>Abgleich auf einem eigenen Hintergrund-Thread; läuft schon einer, wird auf diesen gewartet.</summary>
    Task<HdrListRunResult> SyncNowAsync(bool force = true);

    /// <summary>
    /// Prüft das Konto (Anmeldung und Cargo-Recht) und speichert es nur bei Erfolg; danach lädt ein Abgleich die
    /// vollständige Liste. Leeres Passwort: das gespeicherte, wenn der Benutzername gleich ist. Ergebnis: null bei
    /// Erfolg, sonst die Fehlerart.
    /// </summary>
    Task<PcgwErrorKind?> SaveAccountAsync(string userName, string? password);

    /// <summary>Löscht Konto und vollständige Liste; die Ergebnisse je Spiel bleiben.</summary>
    void RemoveAccount();
}

/// <summary>
/// HDR-Liste der App: Abgleich beim Start (einmaliger Zeitgeber, verzögert, nur wenn AppSettings.HdrListSync an ist),
/// „Jetzt abgleichen“ und das optionale PCGamingWiki-Konto. Netzwerkzugriffe laufen nur in einem eigenen Thread mit
/// niedriger Priorität, der danach endet; der HttpClient lebt nur für einen Lauf. Dauerhaft läuft nichts.
/// </summary>
public sealed class HdrListService : IHdrListService, IDisposable
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly Func<AppConfig> _config;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly HdrSupportCache _cache;
    private readonly PcgwAccountStore _accounts;
    private readonly HdrListSync _sync;
    private readonly RequestPacer _pacer;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task<HdrListRunResult>? _running;
    private Timer? _startupTimer;
    private string? _accountUser;
    private bool _accountLoaded;
    private bool _disposed;

    public HdrListService(
        AppPaths paths,
        IFileSystem fileSystem,
        IClock clock,
        Func<AppConfig> config,
        ILoggerFactory? loggerFactory = null,
        Func<HttpMessageHandler>? handlerFactory = null,
        ISecretProtector? protector = null)
    {
        _paths = paths;
        _clock = clock;
        _config = config;
        _handlerFactory = handlerFactory ?? CreateHandler;
        _logger = loggerFactory?.CreateLogger("HdrList") ?? NullLogger.Instance;
        _cache = new HdrSupportCache(fileSystem, paths, _logger);
        _accounts = new PcgwAccountStore(fileSystem, paths.PcgwAccountFile, protector ?? DpapiProtector.Instance, _logger);
        _sync = new HdrListSync(_cache, clock, _logger);
        _pacer = new RequestPacer(clock, PcgwClient.PauseBetweenRequests);
    }

    public event EventHandler? Changed;

    /// <summary>„HdrPilot/1.0.0 (Windows desktop app; HDR list)“, wie es die Nutzungsregeln des Wikis erbitten.</summary>
    public static string UserAgent { get; } =
        "HdrPilot/" + (typeof(HdrListService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0") + " (Windows desktop app; HDR list)";

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running is { IsCompleted: false };
            }
        }
    }

    public AppPaths Paths => _paths;

    public HdrListStatus GetStatus()
    {
        var data = _cache.Load();
        var games = _config().Games;
        var checkedGames = games.Count(g => data.Games.TryGetValue(g.Key, out var e) && string.Equals(e.Name, g.Name, StringComparison.Ordinal));
        return new HdrListStatus(data.LastSuccessAt, data.LastError, checkedGames, games.Count, data.AccountMode, IsRunning, AccountUser());
    }

    public IReadOnlyDictionary<string, HdrSupportStatus> LoadStatuses() => _cache.LoadStatuses();

    /// <summary>Abgleich nach dem Start: einmal nach <paramref name="delay"/>, wenn die Einstellung dann noch an ist.</summary>
    public void ScheduleStartupSync(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _startupTimer?.Dispose();
            _startupTimer = new Timer(_ => OnStartupTimer(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public Task<HdrListRunResult> SyncNowAsync(bool force = true)
    {
        Task<HdrListRunResult> task;
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running)
            {
                return running;
            }

            if (_disposed)
            {
                return Task.FromResult(new HdrListRunResult(HdrListRunOutcome.Failed, null, 0, 0, 0, false));
            }

            task = RunOnWorker(token => Sync(force, token));
            _running = task;
        }

        RaiseChanged();
        _ = task.ContinueWith(_ => RaiseChanged(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    public async Task<PcgwErrorKind?> SaveAccountAsync(string userName, string? password)
    {
        userName = userName.Trim();
        if (!PcgwAccount.IsValidUserName(userName))
        {
            throw new ArgumentException("Benutzername im Format Name@BotName erwartet.", nameof(userName));
        }

        var result = await RunOnWorker(token => TestAndStore(userName, password, token)).ConfigureAwait(false);
        RaiseChanged();
        if (result is null)
        {
            _ = SyncNowAsync(force: true); // vollständige Liste gleich laden
        }

        return result;
    }

    public void RemoveAccount()
    {
        _accounts.Delete();
        _cache.DeleteCatalog();
        lock (_gate)
        {
            _accountUser = null;
            _accountLoaded = true;
        }

        _logger.LogInformation("PCGamingWiki-Konto entfernt.");
        RaiseChanged();
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

    private static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = true,
        CookieContainer = new CookieContainer(),
    };

    private void OnStartupTimer()
    {
        lock (_gate)
        {
            _startupTimer?.Dispose();
            _startupTimer = null;
        }

        try
        {
            if (_config().Settings.HdrListSync)
            {
                _ = SyncNowAsync(force: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HDR-Liste: Abgleich nach dem Start nicht begonnen.");
        }
    }

    private string? AccountUser()
    {
        lock (_gate)
        {
            if (_accountLoaded)
            {
                return _accountUser;
            }
        }

        var user = _accounts.Load(out _)?.UserName;
        lock (_gate)
        {
            _accountUser = user;
            _accountLoaded = true;
            return user;
        }
    }

    private PcgwClient CreateClient() => new(_handlerFactory(), _clock, UserAgent, RequestTimeout, _pacer);

    private HdrListRunResult Sync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            var games = _config().Games.Select(g => new HdrListGame(g.Key, g.Source, g.SourceId, g.Name, g.IsInstalled, g.LastPlayed)).ToList();
            var account = _accounts.Load(out var unreadable);
            if (unreadable)
            {
                // Konto eingerichtet, aber nicht lesbar: nicht stillschweigend ohne Konto (mit Spielnamen) abfragen.
                var data = _cache.Load();
                data.LastError = PcgwErrorKind.AccountUnreadable;
                _cache.Save(data);
                return new HdrListRunResult(HdrListRunOutcome.Failed, PcgwErrorKind.AccountUnreadable, 0, 0, 0, true);
            }

            return _sync.Run(games, account, CreateClient, force, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "HDR-Liste: Abgleich fehlgeschlagen.");
            return new HdrListRunResult(HdrListRunOutcome.Failed, null, 0, 0, 0, false);
        }
    }

    private PcgwErrorKind? TestAndStore(string userName, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            var stored = _accounts.Load(out _);
            password = stored is not null && string.Equals(stored.UserName, userName, StringComparison.Ordinal)
                ? stored.Password
                : throw new ArgumentException("Bot-Passwort fehlt.", nameof(password));
        }

        var account = new PcgwAccount(userName, password);
        using (var client = CreateClient())
        {
            try
            {
                HdrListSync.TestAccount(client, account, cancellationToken);
            }
            catch (PcgwException ex)
            {
                _logger.LogWarning("PCGamingWiki-Konto: Prüfung fehlgeschlagen ({Kind}{Code}).", ex.Kind, ex.Code is null ? string.Empty : ", " + ex.Code);
                return ex.Kind;
            }
        }

        try
        {
            _accounts.Save(account);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _logger.LogWarning("PCGamingWiki-Konto: nicht gespeichert ({Error}).", ex.Message);
            return PcgwErrorKind.AccountNotSaved;
        }

        lock (_gate)
        {
            _accountUser = userName;
            _accountLoaded = true;
        }

        _logger.LogInformation("PCGamingWiki-Konto gespeichert und geprüft.");
        return null;
    }

    /// <summary>Arbeit auf einem eigenen Hintergrund-Thread mit niedriger Priorität, immer nur eine zugleich.</summary>
    private Task<T> RunOnWorker<T>(Func<CancellationToken, T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _lifetime.Token;
        var thread = new Thread(() =>
        {
            try
            {
                _worker.Wait(token);
                try
                {
                    completion.TrySetResult(work(token));
                }
                finally
                {
                    _worker.Release();
                }
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "HdrPilot HDR-Liste",
        };
        thread.Start();
        return completion.Task;
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HDR-Liste: Fehler in einem Empfänger von Changed.");
        }
    }
}
