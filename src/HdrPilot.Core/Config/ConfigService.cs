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

    // Aufzeichnungen der Sitzungslogik, deren Speichern scheiterte (Datei gesperrt): je Spiel, beim nächsten Speichern nachgeholt,
    // spätestens beim einmaligen Versuch nach PendingRetryDelay oder beim Beenden (FlushPending).
    private readonly Dictionary<string, List<string>> _pendingExecutables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _pendingLastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private bool _retryScheduled;
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

    /// <summary>
    /// So lange nach einer Aufzeichnung der Sitzungslogik, deren Speichern scheiterte (die Datei war gesperrt, etwa von einem
    /// Sicherungs- oder Synchronisierungsprogramm), wird einmal erneut gespeichert (<see cref="FlushPending"/>). Scheitert
    /// auch das, holt sie das nächste Speichern oder das Beenden nach.
    /// </summary>
    public static TimeSpan PendingRetryDelay { get; } = TimeSpan.FromSeconds(30);

    public AppConfig Current => Volatile.Read(ref _current);

    public long Revision => Interlocked.Read(ref _revision);

    public string? LoadWarning { get; }

    /// <summary>
    /// Ändert die Konfiguration unter Sperre, speichert sofort und meldet die Änderung. Hat ein anderer Prozess
    /// (etwa die Kommandozeile) die Datei inzwischen geändert, wird sie vorher neu gelesen, damit nichts verloren geht.
    /// Aufzeichnungen der Sitzungslogik, deren Speichern zuvor scheiterte, kommen dabei mit hinein.
    /// </summary>
    public void Update(Action<AppConfig> mutate)
    {
        lock (_gate)
        {
            WithFileLock(() =>
            {
                var readText = _lastText;
                AppConfig next;
                try
                {
                    var baseConfig = ReloadBaseIfChangedOnDisk() ?? _current;
                    next = baseConfig.Clone();
                    ApplyPending(next);
                    mutate(next);
                    ConfigStore.Normalize(next);
                    if (!TrySave(next, out var error))
                    {
                        // Nicht übernehmen: Speicher und Platte bleiben gleich, der Aufrufer erfährt den Fehler.
                        throw new ConfigSaveException(_store.Path, error);
                    }
                }
                catch
                {
                    // Eine eben gelesene fremde Änderung gilt weiter als neu: Sonst überginge das nächste Neulesen sie, und
                    // das nächste Speichern schriebe den alten Stand darüber.
                    _lastText = readText;
                    throw;
                }

                _pendingExecutables.Clear();
                _pendingLastPlayed.Clear();
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
        try
        {
            RecordDetectedExecutablesCore(gameKey, executables);
        }
        catch (ConfigSaveException ex)
        {
            lock (_gate)
            {
                if (!_pendingExecutables.TryGetValue(gameKey, out var pending))
                {
                    _pendingExecutables[gameKey] = pending = [];
                }

                var added = executables.Where(e => !pending.Contains(e, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                pending.AddRange(added);
            }

            _logger.LogWarning("Gemerkte Spiel-EXEs nicht gespeichert, beim nächsten Speichern nachgeholt: {Error}", ex.Message);
            SchedulePendingRetry();
        }
    }

    private void RecordDetectedExecutablesCore(string gameKey, IReadOnlyCollection<string> executables)
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
        try
        {
            RecordLastPlayedCore(gameKey, when);
        }
        catch (ConfigSaveException ex)
        {
            lock (_gate)
            {
                if (!_pendingLastPlayed.TryGetValue(gameKey, out var pending) || pending < when)
                {
                    _pendingLastPlayed[gameKey] = when;
                }
            }

            _logger.LogWarning("Zuletzt gespielt nicht gespeichert, beim nächsten Speichern nachgeholt: {Error}", ex.Message);
            SchedulePendingRetry();
        }
    }

    private void RecordLastPlayedCore(string gameKey, DateTimeOffset when)
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

    /// <summary>
    /// Speichert jetzt, was die Sitzungslogik aufzeichnete, aber nicht speichern konnte (beim Beenden und einmal nach
    /// <see cref="PendingRetryDelay"/>); ohne Offenes schreibt es nichts. Scheitert das Speichern wieder, wirft es nicht: Das
    /// Offene bleibt für das nächste Speichern. True, wenn nichts mehr offen ist.
    /// </summary>
    public bool FlushPending()
    {
        lock (_gate)
        {
            if (_pendingExecutables.Count == 0 && _pendingLastPlayed.Count == 0)
            {
                return true;
            }
        }

        try
        {
            Update(_ => { }); // nimmt das Offene mit (ApplyPending)
            _logger.LogInformation("Aufzeichnungen der Sitzungslogik nachträglich gespeichert.");
            return true;
        }
        catch (ConfigSaveException ex)
        {
            _logger.LogWarning("Aufzeichnungen der Sitzungslogik weiter nicht gespeichert, beim nächsten Speichern nachgeholt: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>Ein Versuch nach <see cref="PendingRetryDelay"/>, solange keiner wartet (nach einer gescheiterten Aufzeichnung).</summary>
    private void SchedulePendingRetry()
    {
        lock (_gate)
        {
            if (_retryScheduled)
            {
                return;
            }

            _retryScheduled = true;
        }

        _ = RetryPendingAsync();
    }

    private async Task RetryPendingAsync()
    {
        try
        {
            try
            {
                await _clock.Delay(PendingRetryDelay).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _retryScheduled = false;
                }
            }

            _ = FlushPending();
        }
        catch (Exception ex)
        {
            // Läuft unbeobachtet im Hintergrund: Ein unerwarteter Fehler darf nicht verloren gehen, die App aber nicht stören.
            _logger.LogWarning(ex, "Erneutes Speichern der Aufzeichnungen der Sitzungslogik gescheitert.");
        }
    }

    /// <summary>
    /// Holt Aufzeichnungen nach, deren Speichern scheiterte (unter <c>_gate</c>): gemerkte EXEs kommen dazu, „zuletzt
    /// gespielt“ nur, wenn es neuer ist als der Eintrag. Ein inzwischen entferntes Spiel bleibt entfernt.
    /// </summary>
    private void ApplyPending(AppConfig config)
    {
        foreach (var (key, executables) in _pendingExecutables)
        {
            if (config.FindGame(key) is { } game)
            {
                game.DetectedExecutables.AddRange(executables.Where(e => !game.DetectedExecutables.Contains(e, StringComparer.OrdinalIgnoreCase)));
            }
        }

        foreach (var (key, when) in _pendingLastPlayed)
        {
            if (config.FindGame(key) is { } game && (game.LastPlayed is null || game.LastPlayed < when))
            {
                game.LastPlayed = when;
            }
        }
    }

    private void TrySave(AppConfig config) => TrySave(config, out _);

    private bool TrySave(AppConfig config, out string error)
    {
        try
        {
            _lastText = _store.Save(config);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Konfiguration ließ sich nicht speichern ({Path}).", _store.Path);
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>Die Konfiguration ließ sich nicht speichern (Platte voll, Datei gesperrt). Die Änderung wurde nicht übernommen.</summary>
public sealed class ConfigSaveException(string path, string reason)
    : IOException(string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.CoreStrings.ConfigSaveFailed, path, reason))
{
    public string ConfigPath { get; } = path;

    public string Reason { get; } = reason;
}