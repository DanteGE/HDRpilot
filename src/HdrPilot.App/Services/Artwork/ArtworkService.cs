using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Einstellungen des Bilderdienstes (Ordner, Grenzen, Attrappen für Tests).</summary>
public sealed class ArtworkServiceOptions
{
    /// <summary>Ordner <c>art</c> im Datenordner (Verzeichnis, erzeugte und eigene Bilder).</summary>
    public required string ArtDirectory { get; init; }

    /// <summary>Ordner der eigenen Symbole („Symbol ändern…“, E86); null: eigene Symbole werden nicht gezeigt.</summary>
    public string? IconDirectory { get; init; }

    public IFileSystem FileSystem { get; init; } = PhysicalFileSystem.Instance;

    /// <summary>Ordner der Launcher-Bildcaches; wird beim ersten Bedarf einmal ermittelt.</summary>
    public required Func<ArtworkRoots> Roots { get; init; }

    /// <summary>Ausschlussliste für die EXE-Wahl (null: Standardliste).</summary>
    public Func<IReadOnlyList<string>?> HelperPatterns { get; init; } = () => null;

    /// <summary>Führt eine Aktion auf dem UI-Thread aus (Ereignisse); Standard: sofort auf dem aufrufenden Thread.</summary>
    public Action<Action> PostToUi { get; init; } = action => action();

    /// <summary>Größe und Änderungszeit einer Datei als Text (null: fehlt); Standard: echte Datei.</summary>
    public Func<string, string?>? FileStamp { get; init; }

    /// <summary>Beobachtung der Cache-Ordner, solange ein Fenster Bilder zeigt.</summary>
    public bool WatchCaches { get; init; } = true;

    public TimeSpan WatchDebounce { get; init; } = ArtworkWatcher.DefaultDebounce;

    /// <summary>Verzögerung beim Schreiben von index.json (Änderungen werden gesammelt).</summary>
    public TimeSpan SaveDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Höchstens so viele Bilder werden gleichzeitig dekodiert oder erzeugt.</summary>
    public int MaxParallel { get; init; } = 2;

    /// <summary>Obergrenze der dekodierten Bilder im Speicher (Byte).</summary>
    public long MemoryBudget { get; init; } = 192L * 1024 * 1024;

    /// <summary>Obergrenze der hellen HDR-Fassungen auf der Platte (Byte); die ältesten fallen heraus.</summary>
    public long BoostBudget { get; init; } = 300L * 1024 * 1024;

    public ILogger? Logger { get; init; }
}

/// <summary>
/// Spielbilder aus lokalen Quellen (E89, G10.3). Je Spiel wird einmal ermittelt und in <c>art\index.json</c> gemerkt, was es
/// gibt (<see cref="ArtworkLocator"/>), welches Symbol gilt, ob ein Steam-Cover verschmiert ist, die Umgebungsfarben samt
/// Farbfeld und der Zuschnitt des Logos; erzeugte Ersatzbilder, zugeschnittene Logos und helle HDR-Fassungen liegen als
/// Dateien in <c>art</c>. Die Launcher-Caches werden nur gelesen.
/// <para>
/// Reihenfolge je Bildart: eigene Wahl („Bild wählen…“: eigene Datei, anderes lokales Bild oder „Erzeugt“), Launcher-Bild,
/// SteamGridDB (nur wenn eingeschaltet und das Bild lokal fehlt), sonst ein erzeugtes Bild. Titelbild, Cover, Kopfbild,
/// Farbfeld und Symbol sind nie leer; ohne Logo steht der Name in der Titelschrift (Logo null).
/// </para>
/// <para>
/// Geladen wird auf höchstens <see cref="ArtworkServiceOptions.MaxParallel"/> eigenen STA-Threads, zuletzt angefragte
/// zuerst; sie enden, sobald nichts mehr ansteht. Bilder sind eingefroren und höchstens in der angefragten Breite dekodiert;
/// der Speicher ist begrenzt (<see cref="ImageMemoryCache"/>). Speicher, Beobachtung der Cache-Ordner und Zeitgeber gibt es
/// nur, solange ein Fenster eine Freigabe hält (<see cref="Acquire"/>); danach läuft nichts mehr.
/// </para>
/// </summary>
public sealed partial class ArtworkService : IArtworkService, ISteamGridDbArtwork
{
    private readonly ArtworkServiceOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly Func<string, string?> _stamp;
    private readonly Lazy<ArtworkRoots> _roots;
    private readonly Lazy<ArtworkLocator> _locator;
    private readonly ArtworkIndex _indexFile;
    private readonly ImageMemoryCache _memory;
    private readonly ConcurrentDictionary<string, GameState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _gameLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedArtwork> _artwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkItem> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<WorkItem> _queue = new();
    private readonly object _queueGate = new();
    private readonly object _indexGate = new();
    private readonly object _leaseGate = new();
    private ArtworkIndexData? _index;
    private Timer? _saveTimer;
    private bool _indexDirty;
    private ArtworkWatcher? _watcher;
    private int _workers;
    private int _generation;
    private int _leases;
    private bool _disposed;

    public ArtworkService(ArtworkServiceOptions options)
    {
        _options = options;
        _fileSystem = options.FileSystem;
        _logger = options.Logger ?? NullLogger.Instance;
        _stamp = options.FileStamp ?? RealFileStamp;
        _roots = new Lazy<ArtworkRoots>(() =>
        {
            try
            {
                return options.Roots();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Bilder: Launcher-Ordner nicht ermittelt ({Error}).", ex.GetType().Name);
                return ArtworkRoots.None;
            }
        });
        _locator = new Lazy<ArtworkLocator>(() => new ArtworkLocator(_fileSystem, _roots.Value, _logger));
        _indexFile = new ArtworkIndex(_fileSystem, Path.Combine(options.ArtDirectory, "index.json"), _logger);
        _memory = new ImageMemoryCache(options.MemoryBudget);
    }

    public event EventHandler<ArtworkChangedEventArgs>? Changed;

    /// <summary>Beobachtung der Cache-Ordner aktiv (nur mit Freigabe).</summary>
    public bool IsWatching => _watcher is not null;

    /// <summary>Anzahl der beobachteten Ordner (Prüfung: ohne Freigabe 0).</summary>
    public int WatchedFolders => _watcher?.Count ?? 0;

    /// <summary>Dekodierte Bilder im Speicher (Byte, geschätzt).</summary>
    public long MemoryBytes => _memory.Bytes;

    /// <summary>Gehaltene Freigaben.</summary>
    public int Leases => Volatile.Read(ref _leases);

    /// <summary>Laufende Arbeits-Threads (im Leerlauf 0).</summary>
    public int Workers => Volatile.Read(ref _workers);

    public ArtworkRoots Roots => _roots.Value;

    public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth)
    {
        if (!_states.TryGetValue(game.Key, out var state) || state.ConfigStamp != ConfigStamp(game))
        {
            return null;
        }

        return _memory.TryGet(ImageKey(state, kind, NormalizeWidth(kind, decodeWidth)), out var image) ? image : null;
    }

    public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default)
    {
        if (GetCachedImage(game, kind, decodeWidth) is { } cached)
        {
            return Task.FromResult<ImageSource?>(cached);
        }

        var snapshot = Snapshot(game);
        var width = NormalizeWidth(kind, decodeWidth);
        return Typed<ImageSource?>(
            Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|" + kind + "|" + width, () => Guarded(snapshot, () => LoadImage(snapshot, kind, width), () => LoadFallback(snapshot, kind, width)), cancellationToken),
            value => value as ImageSource);
    }

    public GameArtwork? GetCachedArtwork(GameConfig game) =>
        _artwork.TryGetValue(game.Key, out var cached) && cached.ConfigStamp == ConfigStamp(game) ? cached.Artwork : null;

    public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot(game);
        var width = NormalizeWidth(ArtworkKind.Hero, heroDecodeWidth);
        if (_artwork.TryGetValue(snapshot.Key, out var cached) && cached.ConfigStamp == ConfigStamp(snapshot) && cached.HeroWidth == width)
        {
            return Task.FromResult(cached.Artwork);
        }

        return Typed(
            Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|artwork|" + width, () => Guarded(snapshot, () => LoadArtwork(snapshot, width), () => null), cancellationToken),
            value => value as GameArtwork ?? EmptyArtwork(snapshot));
    }

    /// <summary>Satz ohne Bilder (Fenster inzwischen geschlossen oder unerwarteter Fehler): Farben aus dem Namen.</summary>
    private static GameArtwork EmptyArtwork(GameConfig game) =>
        new(game.Key, null, null, null, null, null, null, AmbientColors.FromName(game.Name), ArtworkSummary.Empty);

    private static async Task<T> Typed<T>(Task<object?> task, Func<object?, T> convert) => convert(await task.ConfigureAwait(false));

    /// <summary>
    /// Unerwartete Fehler (nicht nur unlesbare Bilder) landen im Protokoll statt bei der Oberfläche; dann gilt das Ersatzbild.
    /// </summary>
    private object? Guarded(GameConfig game, Func<object?> work, Func<object?> fallback)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Bilder: Fehler beim Laden von {Game}.", game.Key);
            try
            {
                return fallback();
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                _logger.LogError(inner, "Bilder: auch das Ersatzbild von {Game} scheiterte.", game.Key);
                return null;
            }
        }
    }

    public IDisposable Acquire()
    {
        lock (_leaseGate)
        {
            if (_disposed)
            {
                return NoLease.Instance;
            }

            if (++_leases == 1)
            {
                StartWatching();
            }
        }

        return new Lease(this);
    }

    public void ClearCache()
    {
        lock (_queueGate)
        {
            _generation++;
            while (_queue.Count > 0)
            {
                _queue.Pop().Completion.TrySetResult(null);
            }

            _inflight.Clear();
        }

        _memory.Clear();
        _artwork.Clear();
        _states.Clear();
    }

    public void Dispose()
    {
        lock (_leaseGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopWatching();
        }

        ClearCache();
        FlushIndex();
        lock (_indexGate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }

    /// <summary>Schreibt anstehende Änderungen an index.json sofort (Bericht, Ende einer Freigabe). Wirft nicht.</summary>
    public void FlushIndex()
    {
        lock (_indexGate)
        {
            if (!_indexDirty || _index is null)
            {
                return;
            }

            try
            {
                _indexFile.Save(_index);
                _indexDirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.LogWarning("Bilder: Verzeichnis nicht gespeichert ({Message}).", ex.Message);
            }
        }
    }

    private static string RealFileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>Kopie der Angaben eines Spiels, die der Hintergrund liest (die Konfiguration darf sich derweil ändern).</summary>
    private static GameConfig Snapshot(GameConfig game) => new()
    {
        Source = game.Source,
        SourceId = game.SourceId,
        Name = game.Name,
        InstallDir = game.InstallDir,
        KnownExecutables = [.. game.KnownExecutables],
        DetectedExecutables = [.. game.DetectedExecutables],
        ExcludedExecutables = [.. game.ExcludedExecutables],
        ExtraExecutables = [.. game.ExtraExecutables],
        IconCandidates = [.. game.IconCandidates],
        CustomIcon = game.CustomIcon,
        Artwork = game.Artwork is null ? null : new GameArtworkChoice { Hero = game.Artwork.Hero, Logo = game.Artwork.Logo, Cover = game.Artwork.Cover },
        IsInstalled = game.IsInstalled,
        LastPlayed = game.LastPlayed,
    };

    /// <summary>
    /// Kurze Kennung der Angaben eines Spiels, die die Bilder bestimmen (ohne Plattenzugriff, für den Speicher im Fenster).
    /// Änderungen in den Launcher-Caches meldet die Beobachtung der Ordner.
    /// </summary>
    private static string ConfigStamp(GameConfig game) => string.Join(
        '|',
        game.Name,
        game.Artwork?.Hero,
        game.Artwork?.Logo,
        game.Artwork?.Cover,
        game.CustomIcon,
        game.InstallDir,
        string.Join(';', game.IconCandidates),
        string.Join(';', game.DetectedExecutables),
        string.Join(';', game.KnownExecutables),
        string.Join(';', game.ExcludedExecutables));

    /// <summary>Breiten unter 16 px und über 3840 px gibt es nicht; Farbfeld und Symbol haben eigene Grenzen.</summary>
    private static int NormalizeWidth(ArtworkKind kind, int width) => kind switch
    {
        ArtworkKind.Icon => Math.Clamp(width <= 0 ? 256 : width, 16, 256),
        ArtworkKind.Field => Math.Clamp(width <= 0 ? 64 : width, 8, 256),
        _ => Math.Clamp(width <= 0 ? 1920 : width, 16, 3840),
    };

    private static string ImageKey(GameState state, ArtworkKind kind, int width) => state.Key + "|" + state.Signature + "|" + kind + "|" + width;

    /// <summary>
    /// Stellt Arbeit oben auf den Stapel (zuletzt angefragt, zuerst geladen). Gleiche Anfragen teilen sich eine Arbeit; sie
    /// entfällt nur, wenn alle Anfragenden abgebrochen haben (etwa weggescrollte Zeilen).
    /// </summary>
    private Task<object?> Enqueue(string key, Func<object?> work, CancellationToken cancellationToken)
    {
        WorkItem item;
        lock (_queueGate)
        {
            if (_inflight.TryGetValue(key, out var existing))
            {
                item = existing;
                item.Waiters.Add(cancellationToken);
            }
            else
            {
                item = new WorkItem(key, work, _generation);
                item.Waiters.Add(cancellationToken);
                _inflight[key] = item;
                _queue.Push(item);
                if (_workers < Math.Max(1, _options.MaxParallel))
                {
                    _workers++;
                    StartWorker();
                }
            }
        }

        return cancellationToken.CanBeCanceled ? item.Completion.Task.WaitAsync(cancellationToken) : item.Completion.Task;
    }

    /// <summary>Arbeits-Thread (STA, weil erzeugte Bilder mit WPF gezeichnet werden); endet, sobald nichts mehr ansteht.</summary>
    private void StartWorker()
    {
        var thread = new Thread(Work)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "HDRpilot Bilder",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void Work()
    {
        try
        {
            while (true)
            {
                WorkItem item;
                lock (_queueGate)
                {
                    if (_queue.Count == 0)
                    {
                        _workers--;
                        return;
                    }

                    item = _queue.Pop();
                }

                try
                {
                    bool canceled;
                    lock (_queueGate)
                    {
                        canceled = item.Waiters.All(t => t.IsCancellationRequested);
                    }

                    if (canceled)
                    {
                        item.Completion.TrySetCanceled();
                    }
                    else if (item.Generation != Volatile.Read(ref _generation))
                    {
                        item.Completion.TrySetResult(null);
                    }
                    else
                    {
                        item.Completion.TrySetResult(item.Work());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bilder: Laden fehlgeschlagen ({Key}).", item.Key);
                    item.Completion.TrySetException(ex);
                }
                finally
                {
                    lock (_queueGate)
                    {
                        if (_inflight.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
                        {
                            _inflight.Remove(item.Key);
                        }
                    }
                }
            }
        }
        finally
        {
            // Der Thread endet: seinen Dispatcher (falls WPF einen angelegt hat) ordentlich beenden.
            Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
        }
    }

    private void StartWatching()
    {
        if (!_options.WatchCaches || _watcher is not null)
        {
            return;
        }

        var roots = _roots.Value;
        var folders = new[] { roots.SteamLibraryCache, roots.GogWebCache, roots.UbisoftAssets }.OfType<string>().ToList();
        if (folders.Count == 0)
        {
            return;
        }

        _watcher = new ArtworkWatcher(folders, OnCachesChanged, _options.WatchDebounce, _logger);
        _logger.LogDebug("Bilder: {Count} Cache-Ordner werden beobachtet.", _watcher.Count);
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Neue oder geänderte Dateien in einem Bildcache: betroffene Spiele vergessen und melden.</summary>
    private void OnCachesChanged(IReadOnlyCollection<string>? paths)
    {
        if (paths is null)
        {
            _locator.Value.Forget();
            _states.Clear();
            _artwork.Clear();
            _memory.Clear();
            Raise(null);
            return;
        }

        var roots = _roots.Value;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ubisoft = null;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (roots.UbisoftAssets is { } assets && Core.Util.PathUtil.IsUnder(path, assets))
            {
                ubisoft ??= _locator.Value.UbisoftAssetOwners();
            }

            keys.UnionWith(ArtworkWatchMap.GameKeysFor(path, roots, ubisoft));
        }

        foreach (var key in keys)
        {
            Forget(key);
            Raise(key);
        }
    }

    /// <summary>Alles im Speicher zu einem Spiel vergessen (nächste Anfrage prüft neu).</summary>
    private void Forget(string gameKey)
    {
        _states.TryRemove(gameKey, out _);
        _artwork.TryRemove(gameKey, out _);
        _memory.RemovePrefix(gameKey + "|");
    }

    private void Raise(string? gameKey)
    {
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        _options.PostToUi(() =>
        {
            try
            {
                Changed?.Invoke(this, new ArtworkChangedEventArgs(gameKey));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bilder: Fehler in einem Empfänger von Changed.");
            }
        });
    }

    private void ReleaseLease()
    {
        lock (_leaseGate)
        {
            if (_leases == 0 || --_leases > 0)
            {
                return;
            }

            StopWatching();
        }

        // Fenster zu: Bilder freigeben, Verzeichnis im Hintergrund schreiben, kein Zeitgeber bleibt.
        ClearCache();
        _ = Task.Run(() =>
        {
            FlushIndex();
            lock (_indexGate)
            {
                if (Volatile.Read(ref _leases) == 0)
                {
                    _saveTimer?.Dispose();
                    _saveTimer = null;
                    _index = null;
                }
            }
        });
    }

    private sealed class WorkItem(string key, Func<object?> work, int generation)
    {
        public string Key => key;

        public Func<object?> Work => work;

        public int Generation => generation;

        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Abbruch-Merkmale aller Anfragenden (unter der Sperre der Warteschlange).</summary>
        public List<CancellationToken> Waiters { get; } = [];
    }

    private sealed record CachedArtwork(string ConfigStamp, int HeroWidth, GameArtwork Artwork);

    private sealed class Lease(ArtworkService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseLease();
            }
        }
    }
}
