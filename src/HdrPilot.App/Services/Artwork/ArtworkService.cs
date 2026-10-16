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

    /// <summary>Schalter „SteamGridDB“: geladene Bilder von dort gelten nur, solange er an ist (Standard: aus).</summary>
    public Func<bool> SteamGridDbEnabled { get; init; } = () => false;

    /// <summary>Schlüssel der Spiele in der Bibliothek, zum Aufräumen nach dem Schließen; null: nie aufräumen.</summary>
    public Func<IReadOnlyCollection<string>>? LibraryKeys { get; init; }

    /// <summary>Läuft gerade ein Spiel? Dann wird keine helle Fassung berechnet (Standard: nie).</summary>
    public Func<bool> GameRunning { get; init; } = () => false;

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
/// zuerst, Vorab-Laden (<see cref="PrefetchHeroBoostAsync"/>) erst, wenn sonst nichts ansteht; die Threads enden, sobald
/// nichts mehr ansteht. Bilder sind eingefroren und höchstens in der angefragten Breite dekodiert; der Speicher ist begrenzt
/// (<see cref="ImageMemoryCache"/>). Speicher, Beobachtung der Cache-Ordner und Zeitgeber gibt es nur, solange ein Fenster
/// eine Freigabe hält (<see cref="Acquire"/>); danach läuft nichts mehr (<see cref="WhenReleasedAsync"/>).
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
    private readonly LinkedList<string> _setOrder = new();
    private readonly object _setGate = new();
    private readonly Dictionary<string, WorkItem> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<WorkItem> _queue = new();
    private readonly Stack<WorkItem> _lowQueue = new();
    private readonly object _queueGate = new();
    private readonly object _indexGate = new();
    private readonly object _leaseGate = new();
    private ArtworkIndexData? _index;
    private Timer? _saveTimer;
    private bool _indexDirty;
    private ArtworkWatcher? _watcher;
    private TaskCompletionSource? _workersIdle;
    private Task _released = Task.CompletedTask;
    private int _workers;
    private int _generation;
    private int _leases;
    private bool _disposed;

    /// <summary>Höchstens so viele Bildsätze für den Titelbereich bleiben im Speicher (gewähltes Spiel und die zuletzt gewählten).</summary>
    public const int MaxCachedSets = 7;

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
        TryGetSet(game.Key) is { } cached && cached.ConfigStamp == ConfigStamp(game) ? cached.Artwork : null;

    public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot(game);
        var width = NormalizeWidth(ArtworkKind.Hero, heroDecodeWidth);
        if (TryGetSet(snapshot.Key) is { } cached && cached.ConfigStamp == ConfigStamp(snapshot) && cached.HeroWidth == width)
        {
            return Task.FromResult(cached.Artwork);
        }

        return Typed(
            Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|artwork|" + width, () => Guarded(snapshot, () => LoadArtwork(snapshot, width), () => null), cancellationToken),
            value => value as GameArtwork ?? EmptyArtwork(snapshot));
    }

    public Task<ArtworkImage?> GetHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
        HeroBoostAsync(game, heroDecodeWidth, low: false, cancellationToken);

    public Task<ArtworkImage?> PrefetchHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
        HeroBoostAsync(game, heroDecodeWidth, low: true, cancellationToken);

    /// <summary>
    /// Fertig, sobald nach dem Ende der letzten Freigabe aufgeräumt ist (siehe <see cref="ReleaseLease"/>); solange eine
    /// Freigabe besteht, die letzte Aufräumarbeit (meist längst fertig).
    /// </summary>
    public Task WhenReleasedAsync() => Volatile.Read(ref _released);

    private Task<ArtworkImage?> HeroBoostAsync(GameConfig game, int heroDecodeWidth, bool low, CancellationToken cancellationToken)
    {
        var width = NormalizeWidth(ArtworkKind.Hero, heroDecodeWidth);
        if (_states.TryGetValue(game.Key, out var state) && state.ConfigStamp == ConfigStamp(game) && _memory.TryGetValue(BoostKey(state, width), out var cached) && cached is ArtworkImage known)
        {
            return Task.FromResult<ArtworkImage?>(known);
        }

        var snapshot = Snapshot(game);
        return Typed(
            Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|boost|" + width, () => Guarded(snapshot, () => LoadHeroBoost(snapshot, width), () => null), cancellationToken, low),
            value => value as ArtworkImage);
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
            foreach (var queue in new[] { _queue, _lowQueue })
            {
                while (queue.Count > 0)
                {
                    queue.Pop().Completion.TrySetResult(null);
                }
            }

            _inflight.Clear();
        }

        ClearMemory();
    }

    /// <summary>Bilder, Bildsätze und ermittelte Angaben im Speicher vergessen (die Warteschlange bleibt).</summary>
    private void ClearMemory()
    {
        _memory.Clear();
        lock (_setGate)
        {
            _setOrder.Clear();
        }

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
    /// Kurze Kennung der Angaben eines Spiels, die die Bilder bestimmen (ohne Plattenzugriff, für den Speicher im Fenster),
    /// samt Ausschlussliste der Hilfsprogramme (sie bestimmt das Symbol aus der EXE). Änderungen in den Launcher-Caches meldet
    /// die Beobachtung der Ordner.
    /// </summary>
    private string ConfigStamp(GameConfig game) => string.Join(
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
        string.Join(';', game.ExcludedExecutables),
        string.Join(';', _options.HelperPatterns() ?? []));

    /// <summary>Breiten unter 16 px und über 3840 px gibt es nicht; Farbfeld und Symbol haben eigene Grenzen.</summary>
    private static int NormalizeWidth(ArtworkKind kind, int width) => kind switch
    {
        ArtworkKind.Icon => Math.Clamp(width <= 0 ? 256 : width, 16, 256),
        ArtworkKind.Field => Math.Clamp(width <= 0 ? 64 : width, 8, 256),
        _ => Math.Clamp(width <= 0 ? 1920 : width, 16, 3840),
    };

    private static string ImageKey(GameState state, ArtworkKind kind, int width) => state.Key + "|" + state.Signature + "|" + kind + "|" + width;

    /// <summary>
    /// Stellt Arbeit oben auf den Stapel (zuletzt angefragt, zuerst geladen); <paramref name="low"/>: auf den Stapel für
    /// Vorab-Laden, der erst drankommt, wenn der normale leer ist. Gleiche Anfragen teilen sich eine Arbeit; sie entfällt nur,
    /// wenn alle Anfragenden abgebrochen haben (etwa weggescrollte Zeilen). Eine normale Anfrage zieht vorab Angefragtes vor.
    /// </summary>
    private Task<object?> Enqueue(string key, Func<object?> work, CancellationToken cancellationToken, bool low = false)
    {
        WorkItem item;
        lock (_queueGate)
        {
            if (_inflight.TryGetValue(key, out var existing))
            {
                item = existing;
                item.Waiters.Add(cancellationToken);

                // Jetzt gebraucht: oben auf den normalen Stapel; der Eintrag auf dem anderen wird übersprungen.
                if (!low && item.Low && !item.Taken)
                {
                    item.Low = false;
                    _queue.Push(item);
                }
            }
            else
            {
                item = new WorkItem(key, work, _generation) { Low = low };
                item.Waiters.Add(cancellationToken);
                _inflight[key] = item;
                (low ? _lowQueue : _queue).Push(item);
                if (_workers < Math.Max(1, _options.MaxParallel))
                {
                    var first = _workers == 0;
                    _workers++;
                    StartWorker(first);
                }
            }
        }

        return cancellationToken.CanBeCanceled ? item.Completion.Task.WaitAsync(cancellationToken) : item.Completion.Task;
    }

    /// <summary>Nächste Arbeit (unter der Sperre der Warteschlange): erst der normale Stapel, dann das Vorab-Laden.</summary>
    private bool TryTake([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkItem? item)
    {
        while (_queue.Count > 0 || _lowQueue.Count > 0)
        {
            var next = _queue.Count > 0 ? _queue.Pop() : _lowQueue.Pop();
            if (!next.Taken)
            {
                next.Taken = true;
                item = next;
                return true;
            }
        }

        item = null;
        return false;
    }

    /// <summary>Fertig, sobald kein Arbeits-Thread mehr läuft (sofort, wenn keiner läuft).</summary>
    private Task WhenWorkersIdle()
    {
        lock (_queueGate)
        {
            if (_workers == 0)
            {
                return Task.CompletedTask;
            }

            _workersIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _workersIdle.Task;
        }
    }

    /// <summary>Arbeits-Thread (STA, weil erzeugte Bilder mit WPF gezeichnet werden); endet, sobald nichts mehr ansteht.</summary>
    private void StartWorker(bool first)
    {
        var thread = new Thread(() => Work(first))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "HDRpilot Bilder",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <param name="first">Erster Arbeits-Thread nach einer Pause: meldet den Beginn (Stufe Debug, phase10-gpu.ps1).</param>
    private void Work(bool first)
    {
        if (first)
        {
            _logger.LogDebug("Bilder: Laden beginnt.");
        }

        TaskCompletionSource? idle = null;
        var last = false;
        try
        {
            while (true)
            {
                WorkItem item;
                lock (_queueGate)
                {
                    if (!TryTake(out var next))
                    {
                        _workers--;
                        if (_workers == 0)
                        {
                            last = true;
                            idle = _workersIdle;
                            _workersIdle = null;
                        }

                        return;
                    }

                    item = next;
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
            if (last)
            {
                _logger.LogDebug("Bilder: nichts mehr in Arbeit.");
            }

            idle?.TrySetResult();
        }
    }

    private void StartWatching()
    {
        if (!_options.WatchCaches || _watcher is not null)
        {
            return;
        }

        var roots = _roots.Value;
        var folders = new[] { roots.SteamLibraryCache, roots.GogWebCache, roots.UbisoftAssets, UbisoftConfigurationFolder(roots) }.OfType<string>().ToList();
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

    /// <summary>Ordner des Produkt-Caches von Ubisoft Connect (<c>cache\configuration</c>); null ohne Ubisoft Connect.</summary>
    private static string? UbisoftConfigurationFolder(ArtworkRoots roots) =>
        roots.UbisoftConfigurations is { } file ? Path.GetDirectoryName(file) : null;

    /// <summary>Neue oder geänderte Dateien in einem Bildcache: betroffene Spiele vergessen und melden.</summary>
    private void OnCachesChanged(IReadOnlyCollection<string>? paths)
    {
        if (paths is null)
        {
            _locator.Value.Forget();
            _states.Clear();
            _memory.Clear();
            Raise(null);
            return;
        }

        var roots = _roots.Value;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ubisoft = null;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Produkt-Cache von Ubisoft geändert (etwa ein neu installiertes Spiel): Zuordnung der Bilddateien neu lesen, bevor
        // neue Dateien in cache\assets zugeordnet werden, und alle schon ermittelten Ubisoft-Spiele neu prüfen.
        if (UbisoftConfigurationFolder(roots) is { } configuration && paths.Any(p => Core.Util.PathUtil.IsUnder(p, configuration)))
        {
            _locator.Value.Forget();
            keys.UnionWith(_states.Values.Where(s => s.Game.Source == Core.Games.GameSource.Ubisoft).Select(s => s.Key));
        }

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
            ForgetInMemory(key);
            Raise(key);
        }
    }

    /// <summary>
    /// Alles zu einem Spiel im Speicher vergessen und melden: „Symbol ändern…“ legt die Datei beim erneuten Ändern unter
    /// demselben Namen ab, die Angaben des Spiels bleiben also gleich; erst so werden Symbol und daraus erzeugte Bilder neu
    /// gelesen.
    /// </summary>
    public void Forget(string gameKey)
    {
        ForgetInMemory(gameKey);
        Raise(gameKey);
    }

    /// <summary>Alles im Speicher zu einem Spiel vergessen (nächste Anfrage prüft neu); Bildsatz samt heller Fassung inbegriffen.</summary>
    private void ForgetInMemory(string gameKey)
    {
        _states.TryRemove(gameKey, out _);
        _memory.RemovePrefix(gameKey + "|");
    }

    private static string SetKey(string gameKey) => gameKey + "|set";

    /// <summary>Gemerkter Bildsatz eines Spiels (zählt als zuletzt benutzt); null, wenn er nicht mehr im Speicher ist.</summary>
    private CachedArtwork? TryGetSet(string gameKey)
    {
        var key = SetKey(gameKey);
        if (!_memory.TryGetValue(key, out var value) || value is not CachedArtwork cached)
        {
            return null;
        }

        lock (_setGate)
        {
            if (_setOrder.Find(key) is { } node)
            {
                _setOrder.Remove(node);
                _setOrder.AddFirst(node);
            }
        }

        return cached;
    }

    /// <summary>
    /// Bildsatz merken: im Bildspeicher mit der Größe aller Bilder, die er hält (zählt zum Budget), und höchstens
    /// <see cref="MaxCachedSets"/> Sätze (das gewählte Spiel und die zuletzt gewählten); der älteste fällt heraus.
    /// </summary>
    private void StoreSet(string gameKey, CachedArtwork cached)
    {
        var key = SetKey(gameKey);
        var a = cached.Artwork;
        var bytes = new[] { a.Hero, a.Field, a.Logo, a.Cover, a.Icon }
            .OfType<ArtworkImage>()
            .Select(i => i.Image)
            .Distinct()
            .Sum(ImageMemoryCache.SizeOf);
        _memory.Set(key, cached, bytes);
        lock (_setGate)
        {
            if (_setOrder.Find(key) is { } node)
            {
                _setOrder.Remove(node);
            }

            _setOrder.AddFirst(key);
            while (_setOrder.Count > MaxCachedSets && _setOrder.Last is { } oldest)
            {
                _setOrder.RemoveLast();
                _memory.Remove(oldest.Value);
            }
        }
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

        // Fenster zu: Bilder freigeben, Bilder entfernter Spiele aufräumen, Verzeichnis im Hintergrund schreiben, kein
        // Zeitgeber bleibt.
        ClearCache();
        var cleanup = Task.Run(async () =>
        {
            // Laufende Arbeit (höchstens MaxParallel Bilder) zu Ende kommen lassen: Was sie danach noch in den Speicher legt,
            // gehört keinem Fenster mehr und bliebe sonst bis zum nächsten Öffnen liegen.
            await WhenWorkersIdle().ConfigureAwait(false);
            lock (_leaseGate)
            {
                if (_leases == 0)
                {
                    ClearMemory();
                }
            }

            PruneRemovedGames();
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
        Volatile.Write(ref _released, cleanup);
    }

    private sealed class WorkItem(string key, Func<object?> work, int generation)
    {
        public string Key => key;

        public Func<object?> Work => work;

        public int Generation => generation;

        /// <summary>Nur vorab angefragt (auf dem Stapel für Vorab-Laden; unter der Sperre der Warteschlange).</summary>
        public bool Low { get; set; }

        /// <summary>Schon von einem Arbeits-Thread genommen (unter der Sperre der Warteschlange).</summary>
        public bool Taken { get; set; }

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
