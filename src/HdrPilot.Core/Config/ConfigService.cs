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
    private readonly Mutex _fileLock;
    private AppConfig _current;
    private long _revision;
    private string? _lastText;

    public ConfigService(ConfigStore store, IClock clock, ILogger? logger = null)
    {
        _store = store;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;

        // Prozessübergreifende Sperre je Konfigurationsdatei (App und Kommandozeile schreiben dieselbe Datei).
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(store.Path).ToUpperInvariant())))[..16];
        _fileLock = new Mutex(false, @"Local\HdrPilot.Config." + id);

        var result = store.Load();
        LoadWarning = result.Warning;
        _current = result.Config;
        _lastText = result.Text;
        if (result.CreatedNew)
        {
            WithFileLock(() => TrySave(_current));
        }
    }

    public event EventHandler? Changed;

    public AppConfig Current => Volatile.Read(ref _current);

    public long Revision => Interlocked.Read(ref _revision);

    public string? LoadWarning { get; }

    /// <summary>
    /// Ändert die Konfiguration unter Sperre, speichert sofort und meldet die Änderung. Hat ein anderer Prozess
    /// (etwa die Kommandozeile) die Datei inzwischen geändert, wird sie vorher neu gelesen, damit nichts verloren geht.
    /// </summary>
    public void Update(Action<AppConfig> mutate)
    {
        lock (_gate)
        {
            WithFileLock(() =>
            {
                var baseConfig = ReloadBaseIfChangedOnDisk() ?? _current;
                var next = baseConfig.Clone();
                mutate(next);
                ConfigStore.Normalize(next);
                TrySave(next);
                Volatile.Write(ref _current, next);
                Interlocked.Increment(ref _revision);
            });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Liest die Datei neu, wenn ein anderer Prozess sie geändert hat. True, wenn sich etwas geändert hat.</summary>
    public bool ReloadIfChangedOnDisk()
    {
        var changed = false;
        lock (_gate)
        {
            WithFileLock(() =>
            {
                var reloaded = ReloadBaseIfChangedOnDisk();
                if (reloaded is not null)
                {
                    Volatile.Write(ref _current, reloaded);
                    Interlocked.Increment(ref _revision);
                    changed = true;
                }
            });
        }

        if (changed)
        {
            _logger.LogInformation("Konfiguration wurde außerhalb geändert und neu geladen.");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>Neuer Stand von der Platte, falls die Datei sich seit dem letzten Lesen/Schreiben geändert hat und gültig ist.</summary>
    private AppConfig? ReloadBaseIfChangedOnDisk()
    {
        var text = _store.TryReadText();
        if (text is null || text == _lastText)
        {
            return null;
        }

        try
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(text, ConfigJson.Options);
            if (config is null || config.Version > AppConfig.CurrentVersion)
            {
                return null;
            }

            ConfigStore.Normalize(config);
            _lastText = text;
            return config;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // halb geschrieben oder defekt: eigenen Stand behalten
        }
    }

    private void WithFileLock(Action action)
    {
        var acquired = false;
        try
        {
            try
            {
                acquired = _fileLock.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (AbandonedMutexException)
            {
                acquired = true; // anderer Prozess abgestürzt: Sperre gehört jetzt uns
            }

            action();
        }
        finally
        {
            if (acquired)
            {
                _fileLock.ReleaseMutex();
            }
        }
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
            _lastText = _store.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Konfiguration ließ sich nicht speichern ({Path}).", _store.Path);
        }
    }
}
