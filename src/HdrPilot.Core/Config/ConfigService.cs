using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Config;

/// <summary>Was die Sitzungslogik von der Konfiguration braucht (für Tests austauschbar).</summary>
public interface ISessionConfigSource
{
    /// <summary>Unveränderlicher Stand. Nicht verändern; Änderungen nur über die Methoden.</summary>
    AppConfig Current { get; }

    /// <summary>Zählt bei jeder Änderung hoch (für Zwischenspeicher der Prozesszuordnung).</summary>
    long Revision { get; }

    void RecordDetectedExecutables(string gameKey, IReadOnlyCollection<string> executables);

    void RecordLastPlayed(string gameKey, DateTimeOffset when);
}

/// <summary>
/// Hält die Konfiguration im Speicher, veröffentlicht nach jeder Änderung einen neuen, unveränderlichen
/// Stand und schreibt sofort atomar auf die Platte.
/// </summary>
public sealed class ConfigService : ISessionConfigSource
{
    private readonly ConfigStore _store;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private AppConfig _current;
    private long _revision;

    public ConfigService(ConfigStore store, IClock clock, ILogger? logger = null)
    {
        _store = store;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
        var result = store.Load();
        LoadWarning = result.Warning;
        _current = result.Config;
        if (result.CreatedNew)
        {
            TrySave(_current);
        }
    }

    public event EventHandler? Changed;

    public AppConfig Current => Volatile.Read(ref _current);

    public long Revision => Interlocked.Read(ref _revision);

    public string? LoadWarning { get; }

    /// <summary>Ändert die Konfiguration unter Sperre, speichert sofort und meldet die Änderung.</summary>
    public void Update(Action<AppConfig> mutate)
    {
        lock (_gate)
        {
            var next = _current.Clone();
            mutate(next);
            ConfigStore.Normalize(next);
            TrySave(next);
            Volatile.Write(ref _current, next);
            Interlocked.Increment(ref _revision);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateGame(string gameKey, Action<GameConfig> mutate) => Update(c =>
    {
        var game = c.FindGame(gameKey) ?? throw new KeyNotFoundException("Spiel nicht in der Konfiguration: " + gameKey);
        mutate(game);
    });

    public MergeSummary ApplyScan(LibraryScanReport report, IFileSystem fileSystem)
    {
        MergeSummary summary = default!;
        Update(c => summary = LibraryMerger.Apply(c, report, fileSystem, _clock.UtcNow));
        return summary;
    }

    public void RecordDetectedExecutables(string gameKey, IReadOnlyCollection<string> executables)
    {
        if (Current.FindGame(gameKey) is not { } game || executables.All(e => game.DetectedExecutables.Contains(e, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        Update(c =>
        {
            if (c.FindGame(gameKey) is { } g)
            {
                foreach (var exe in executables)
                {
                    if (!g.DetectedExecutables.Contains(exe, StringComparer.OrdinalIgnoreCase))
                    {
                        g.DetectedExecutables.Add(exe);
                    }
                }
            }
        });
    }

    public void RecordLastPlayed(string gameKey, DateTimeOffset when)
    {
        if (Current.FindGame(gameKey) is null)
        {
            return;
        }

        Update(c =>
        {
            if (c.FindGame(gameKey) is { } g)
            {
                g.LastPlayed = when;
            }
        });
    }

    private void TrySave(AppConfig config)
    {
        try
        {
            _store.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Konfiguration ließ sich nicht speichern ({Path}).", _store.Path);
        }
    }
}
