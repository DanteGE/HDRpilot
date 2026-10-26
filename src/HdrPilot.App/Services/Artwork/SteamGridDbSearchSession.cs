using System.Windows.Threading;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Was eine Suche vom Dienst bekommt (<see cref="SteamGridDbService.OpenSearch"/>, Testdaten-Lieferant).</summary>
internal sealed class SteamGridDbSearchSetup
{
    public required string GameName { get; init; }

    /// <summary>Steam-AppID (nur Ziffern) oder null.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>Gemerkte SteamGridDB-ID eines Spiels ohne Steam-AppID oder null.</summary>
    public long? RememberedId { get; init; }

    /// <summary>Erzeugt den Client (mit Handler, Schlüssel und gemeinsamem Abstand) bei der ersten Anfrage.</summary>
    public required Func<SteamGridDbBudget, SteamGridDbClient> CreateClient { get; init; }

    public SteamGridDbSearchLimits Limits { get; init; } = SteamGridDbSearchLimits.Default;

    public IClock Clock { get; init; } = SystemClock.Instance;

    public required ILogger Logger { get; init; }

    public Action<string>? TestLog { get; init; }

    /// <summary>Ende des Dienstes (App beendet): bricht die Suche ab.</summary>
    public CancellationToken Lifetime { get; init; }

    /// <summary>Die Schnittstelle hat den Schlüssel abgelehnt.</summary>
    public Action? Unauthorized { get; init; }

    /// <summary>Die Schnittstelle hat erfolgreich geantwortet.</summary>
    public Action? Answered { get; init; }

    /// <summary>Die Suche ist beendet: Verbrauch und Zeit der letzten Anfrage.</summary>
    public Action<SteamGridDbSearchSession, SteamGridDbSessionUsage, DateTimeOffset?>? Closed { get; init; }
}

/// <summary>
/// Eine Suche in „Bild wählen…“ (E93). Der Client entsteht erst bei der ersten Anfrage. Anfragen an die Schnittstelle laufen
/// nacheinander (eine Sperre je Sitzung) auf je einem eigenen Hintergrund-Thread mit niedriger Priorität; Vorschauen in einer
/// Warteschlange auf höchstens <see cref="SteamGridDbSearchLimits.MaxParallelPreviews"/> Threads, die nach kurzer Ruhe enden.
/// Nach HTTP 429 ruht die Sitzung eine Minute ohne Netz. Dispose bricht ab, leert die Warteschlange und gibt den Client
/// sofort frei (hängende Übertragungen enden mit). Protokolliert werden nur Fehlerarten, Zahlen und Rechnernamen, nie
/// Schlüssel, Suchbegriffe, Spiel- oder Autorennamen und Adressen.
/// </summary>
internal sealed class SteamGridDbSearchSession : ISteamGridDbSearchSession
{
    private const string ApiThreadName = "HDRpilot SteamGridDB";
    private const string PreviewThreadName = "HDRpilot SteamGridDB Vorschau";

    private readonly SteamGridDbSearchSetup _setup;
    private readonly SteamGridDbBudget _budget;
    private readonly CancellationTokenSource _cancel = new();
    private readonly CancellationTokenRegistration _lifetime;
    private readonly SemaphoreSlim _api = new(1, 1);
    private readonly object _gate = new();
    private readonly LinkedList<PreviewJob> _queue = new();
    private readonly HashSet<string> _hostsLogged = new(StringComparer.OrdinalIgnoreCase);
    private SteamGridDbClient? _client;
    private Task<SteamGridDbResolveResult>? _resolve;
    private DateTimeOffset? _blockedUntil;
    private DateTimeOffset? _lastRequest;
    private int _fullDownloads;
    private int _workers;
    private int _idle;
    private bool _disposed;

    public SteamGridDbSearchSession(SteamGridDbSearchSetup setup)
    {
        _setup = setup;
        _budget = new SteamGridDbBudget(setup.Limits.MaxApiRequests, setup.Limits.MaxImageRequests, setup.Limits.MaxPreviewBytes);
        InitialMatch = setup.SteamAppId is { } appId && SteamGridDbTarget.IsSteamAppId(appId)
            ? new SteamGridDbMatch(SteamGridDbMatchKind.SteamAppId, SteamGridDbTarget.Steam(appId), setup.GameName, null, false)
            : setup.RememberedId is > 0 and var id
                ? new SteamGridDbMatch(SteamGridDbMatchKind.Remembered, SteamGridDbTarget.Game(id), setup.GameName, null, false)
                : null;
        _lifetime = setup.Lifetime.Register(static state => ((SteamGridDbSearchSession)state!).Dispose(), this);
    }

    public string GameName => _setup.GameName;

    public bool IsSteamGame => InitialMatch?.Kind == SteamGridDbMatchKind.SteamAppId;

    public SteamGridDbMatch? InitialMatch { get; }

    public SteamGridDbSessionUsage Usage
    {
        get
        {
            lock (_gate)
            {
                return new SteamGridDbSessionUsage(_budget.ApiRequests, _budget.ImageRequests, _budget.PreviewBytes, _fullDownloads);
            }
        }
    }

    public DateTimeOffset? BlockedUntil
    {
        get
        {
            lock (_gate)
            {
                return _blockedUntil is { } until && until > _setup.Clock.UtcNow ? until : null;
            }
        }
    }

    /// <summary>Lebende Vorschau-Threads (Tests).</summary>
    internal int ActiveWorkers
    {
        get
        {
            lock (_gate)
            {
                return _workers;
            }
        }
    }

    public Task<SteamGridDbResolveResult> ResolveAsync(CancellationToken cancellationToken)
    {
        if (InitialMatch is { } known)
        {
            return Task.FromResult(new SteamGridDbResolveResult(known, [], false));
        }

        Task<SteamGridDbResolveResult> shared;
        lock (_gate)
        {
            if (_resolve is null || _resolve.IsFaulted || _resolve.IsCanceled)
            {
                // Eine gemeinsame Auflösung auf dem Token der Sitzung: zwei Reiter zugleich fragen nur einmal.
                _resolve = Api((client, token) => ResolveByName(client, token, steamUnknown: false), CancellationToken.None);
            }

            shared = _resolve;
        }

        return shared.WaitAsync(cancellationToken);
    }

    public Task<SteamGridDbResolveResult> ResolveByNameAsync(CancellationToken cancellationToken) =>
        Api((client, token) => ResolveByName(client, token, steamUnknown: IsSteamGame), cancellationToken);

    public Task<SteamGridDbGameHit?> DescribeAsync(long gameId, CancellationToken cancellationToken) =>
        Api((client, token) => client.GameById(gameId, token), cancellationToken);

    public Task<IReadOnlyList<SteamGridDbGameHit>> SearchGamesAsync(string term, CancellationToken cancellationToken) =>
        Api((client, token) => client.SearchGames(term, token), cancellationToken);

    public Task<SteamGridDbAssetPage> GetAssetsAsync(SteamGridDbMatch match, ArtworkKind kind, int page, CancellationToken cancellationToken)
    {
        var assetKind = kind switch
        {
            ArtworkKind.Hero => SteamGridDbAssetKind.Hero,
            ArtworkKind.Logo => SteamGridDbAssetKind.Logo,
            ArtworkKind.Cover => SteamGridDbAssetKind.Grid,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "SteamGridDB hat Titelbilder, Logos und Cover."),
        };
        return Api((client, token) => client.Assets(assetKind, match.Target, page, token), cancellationToken);
    }

    public Task<SteamGridDbPreview> LoadPreviewAsync(SteamGridDbAsset asset, int decodeWidth, CancellationToken queueToken)
    {
        var job = new PreviewJob(asset, Math.Max(1, decodeWidth));
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromCanceled<SteamGridDbPreview>(new CancellationToken(true));
            }

            if (queueToken.IsCancellationRequested)
            {
                return Task.FromCanceled<SteamGridDbPreview>(queueToken);
            }

            job.Node = _queue.AddLast(job);
            if (_idle > 0)
            {
                Monitor.Pulse(_gate);
            }
            else if (_workers < _setup.Limits.MaxParallelPreviews)
            {
                StartWorker();
            }
        }

        // Solange sie wartet, nimmt das Token der Warteschlange sie wieder heraus.
        job.Registration = queueToken.Register(() => Unqueue(job));
        return job.Completion.Task;
    }

    public Task<SteamGridDbImageData> DownloadAsync(SteamGridDbAsset asset, ArtworkKind kind, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_fullDownloads >= _setup.Limits.MaxFullDownloads)
            {
                return Task.FromException<SteamGridDbImageData>(new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Höchstzahl voller Bilder erreicht."));
            }

            _fullDownloads++;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, cancellationToken);
        return RunWorker(
            () =>
            {
                ThrowIfBlocked();
                var client = Client();
                var (data, extension) = client.Download(asset, SteamGridDbClient.MaxPickedImageBytes, paced: false, linked.Token);
                Touch();
                LogHost(asset.Url);

                // Was das Speichern ablehnte (über 100 MP oder in der Speicherbreite zu groß), scheitert schon hier, im Dialog.
                var (width, height) = ArtworkImaging.Validate(data, ArtworkImaging.MaxSourcePixels, ArtworkService.CustomSaveWidth(kind));
                return new SteamGridDbImageData(data, extension, width, height);
            },
            linked);
    }

    public void Dispose()
    {
        PreviewJob[] waiting;
        SteamGridDbClient? client;
        int workers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            waiting = [.. _queue];
            _queue.Clear();
            client = _client;
            _client = null;
            workers = _workers;
            Monitor.PulseAll(_gate);
        }

        // Erst abbrechen, dann den Client freigeben: laufende Übertragungen enden über ihr Token und die freigegebene Antwort.
        _cancel.Cancel();
        _lifetime.Unregister();
        foreach (var job in waiting)
        {
            job.Registration.Dispose();
            job.Completion.TrySetCanceled();
        }

        client?.Dispose();
        var usage = Usage;
        DateTimeOffset? last;
        lock (_gate)
        {
            last = _lastRequest;
        }

        _setup.Closed?.Invoke(this, usage, last);
        if (workers == 0)
        {
            _setup.TestLog?.Invoke("Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0");
        }
    }

    private static SteamGridDbResolveResult Resolve(IReadOnlyList<SteamGridDbGameHit> hits, string name, bool steamUnknown)
    {
        var wanted = SteamGridDbClient.NormalizeName(name);
        var exact = wanted.Length == 0 ? [] : hits.Where(h => SteamGridDbClient.NormalizeName(h.Name) == wanted).ToList();
        if (exact.Count == 0)
        {
            return new SteamGridDbResolveResult(null, hits, steamUnknown);
        }

        var first = exact[0];
        var match = new SteamGridDbMatch(SteamGridDbMatchKind.ExactName, SteamGridDbTarget.Game(first.Id), first.Name, first.ReleaseYear, first.Verified, Ambiguous: exact.Count > 1);
        return new SteamGridDbResolveResult(match, hits, steamUnknown);
    }

    private SteamGridDbResolveResult ResolveByName(SteamGridDbClient client, CancellationToken cancellationToken, bool steamUnknown) =>
        Resolve(client.SearchGames(GameName, cancellationToken), GameName, steamUnknown);

    /// <summary>Eine Anfrage an die Schnittstelle: nacheinander je Sitzung, auf einem eigenen Thread.</summary>
    private Task<T> Api<T>(Func<SteamGridDbClient, CancellationToken, T> work, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, cancellationToken);
        return RunWorker(
            () =>
            {
                _api.Wait(linked.Token);
                try
                {
                    ThrowIfBlocked();
                    var client = Client();
                    try
                    {
                        var result = work(client, linked.Token);
                        _setup.Answered?.Invoke();
                        return result;
                    }
                    finally
                    {
                        Touch();
                    }
                }
                finally
                {
                    _api.Release();
                }
            },
            linked);
    }

    /// <summary>
    /// Arbeit auf einem neuen Hintergrund-Thread mit niedriger Priorität (wie <c>RunOnWorker</c> des Dienstes, aber
    /// Fehler von SteamGridDB und unlesbare Bilder sind Alltag: Protokoll auf Stufe Information).
    /// </summary>
    private Task<T> RunWorker<T>(Func<T> work, CancellationTokenSource linked)
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
            catch (Exception ex) when (ex is ObjectDisposedException or System.IO.IOException && _cancel.IsCancellationRequested)
            {
                // Spät gemeldet, nachdem die Sitzung den Client freigab: zählt als Abbruch.
                completion.TrySetCanceled(_cancel.Token);
            }
            catch (SteamGridDbException ex)
            {
                Note(ex);
                completion.TrySetException(ex);
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex) || ex is System.IO.InvalidDataException)
            {
                _setup.Logger.LogInformation("SteamGridDB-Suche: Bild nicht lesbar ({Error}).", ex.GetType().Name);
                completion.TrySetException(ex);
            }
            catch (Exception ex)
            {
                _setup.Logger.LogError(ex, "SteamGridDB-Suche: Fehler im Hintergrund.");
                completion.TrySetException(ex);
            }
            finally
            {
                linked.Dispose();
                Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = ApiThreadName,
        };
        thread.Start();
        return completion.Task;
    }

    /// <summary>Fehler merken: 429 sperrt die Sitzung eine Minute, 401/403 der Schnittstelle meldet der Dienst.</summary>
    private void Note(SteamGridDbException ex)
    {
        _setup.Logger.LogInformation("SteamGridDB-Suche: {Kind}", ex.Kind);
        if (ex.Kind == SteamGridDbErrorKind.RateLimited && ex.Data["blocked"] is not true)
        {
            lock (_gate)
            {
                _blockedUntil = _setup.Clock.UtcNow + _setup.Limits.RateLimitPause;
            }
        }
        else if (ex.Kind == SteamGridDbErrorKind.Unauthorized)
        {
            _setup.Unauthorized?.Invoke();
        }
    }

    private void ThrowIfBlocked()
    {
        lock (_gate)
        {
            if (_blockedUntil is { } until && until > _setup.Clock.UtcNow)
            {
                // Ohne Anfrage abgelehnt: verlängert die Pause nicht.
                var blocked = new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Pause nach zu vielen Anfragen.");
                blocked.Data["blocked"] = true;
                throw blocked;
            }
        }
    }

    private void Touch()
    {
        lock (_gate)
        {
            _lastRequest = _setup.Clock.UtcNow;
        }
    }

    /// <summary>Nur der Rechnername einer Bildadresse, einmal je Rechner (für den Livetest), nie der Pfad.</summary>
    private void LogHost(string? url)
    {
        if (!_setup.Logger.IsEnabled(LogLevel.Debug) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        bool first;
        lock (_gate)
        {
            first = _hostsLogged.Add(uri.Host);
        }

        if (first)
        {
            _setup.Logger.LogDebug("SteamGridDB-Suche: Bilder von {Host}.", uri.Host);
        }
    }

    private SteamGridDbClient Client()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new OperationCanceledException(_cancel.Token);
            }

            return _client ??= _setup.CreateClient(_budget);
        }
    }

    private void Unqueue(PreviewJob job)
    {
        lock (_gate)
        {
            if (job.Node?.List is null)
            {
                return; // läuft schon oder ist fertig
            }

            _queue.Remove(job.Node);
            job.Node = null;
        }

        job.Completion.TrySetCanceled();
    }

    /// <summary>Unter <see cref="_gate"/> aufrufen.</summary>
    private void StartWorker()
    {
        _workers++;
        var thread = new Thread(PreviewLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = PreviewThreadName,
        };
        thread.Start();
    }

    private void PreviewLoop()
    {
        try
        {
            while (true)
            {
                PreviewJob job;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_disposed)
                    {
                        _idle++;
                        var woken = Monitor.Wait(_gate, _setup.Limits.PreviewIdleExit);
                        _idle--;
                        if (!woken && _queue.Count == 0)
                        {
                            return;
                        }
                    }

                    if (_disposed || _queue.First is not { } node)
                    {
                        return;
                    }

                    job = node.Value;
                    _queue.RemoveFirst();
                    job.Node = null;
                }

                job.Registration.Dispose();
                Load(job);
            }
        }
        finally
        {
            bool last;
            lock (_gate)
            {
                _workers--;
                last = _workers == 0 && _disposed;
            }

            Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
            if (last)
            {
                _setup.TestLog?.Invoke("Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0");
            }
        }
    }

    /// <summary>Lädt und dekodiert eine Vorschau; einmal begonnen, beendet sie nur das Token der Sitzung.</summary>
    private void Load(PreviewJob job)
    {
        try
        {
            ThrowIfBlocked();
            var client = Client();
            var bytes = client.DownloadPreview(job.Asset, _cancel.Token);
            Touch();
            LogHost(job.Asset.ThumbUrl);
            var image = ArtworkImaging.Decode(bytes, job.DecodeWidth);
            job.Completion.TrySetResult(new SteamGridDbPreview(image, (long)image.PixelWidth * image.PixelHeight * 4));
        }
        catch (OperationCanceledException)
        {
            job.Completion.TrySetCanceled();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or System.IO.IOException && _cancel.IsCancellationRequested)
        {
            job.Completion.TrySetCanceled();
        }
        catch (SteamGridDbException ex)
        {
            if (ex.Data["blocked"] is not true && ex.Kind != SteamGridDbErrorKind.RequestLimit)
            {
                Touch();
            }

            Note(ex);
            job.Completion.TrySetException(ex);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex) || ex is System.IO.InvalidDataException or NotSupportedException or ArgumentException)
        {
            _setup.Logger.LogInformation("SteamGridDB-Suche: Vorschau nicht lesbar ({Error}).", ex.GetType().Name);
            job.Completion.TrySetException(ex);
        }
    }

    /// <summary>Eine Vorschau in der Warteschlange.</summary>
    private sealed class PreviewJob(SteamGridDbAsset asset, int decodeWidth)
    {
        public SteamGridDbAsset Asset { get; } = asset;

        public int DecodeWidth { get; } = decodeWidth;

        public TaskCompletionSource<SteamGridDbPreview> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LinkedListNode<PreviewJob>? Node { get; set; }

        public CancellationTokenRegistration Registration { get; set; }
    }
}
