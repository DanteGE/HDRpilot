using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Beobachtet die Bildcaches der Launcher (E89, neue Bilder erscheinen von selbst): je Ordner ein
/// <see cref="FileSystemWatcher"/> mit Unterordnern auf Dateinamen, Ordnernamen und Schreibzeit. Änderungen werden
/// <see cref="Debounce"/> lang gesammelt und dann gemeinsam gemeldet; ein übergelaufener Puffer oder ein Fehler meldet
/// „alles“ (null). Lebt nur, solange ein Fenster Bilder zeigt; Dispose beendet Beobachtung und Zeitgeber.
/// </summary>
public sealed class ArtworkWatcher : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(750);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Action<IReadOnlyCollection<string>?> _changed;
    private readonly TimeSpan _debounce;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private bool _all;
    private bool _disposed;

    /// <param name="folders">Zu beobachtende Ordner (fehlende werden übersprungen).</param>
    /// <param name="changed">Gesammelte Pfade nach der Wartezeit; null: alles neu prüfen. Kommt auf einem Hintergrund-Thread.</param>
    public ArtworkWatcher(IEnumerable<string> folders, Action<IReadOnlyCollection<string>?> changed, TimeSpan? debounce = null, ILogger? logger = null)
    {
        _changed = changed;
        _debounce = debounce ?? DefaultDebounce;
        _logger = logger ?? NullLogger.Instance;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += OnEvent;
                watcher.Changed += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
            {
                _logger.LogDebug("Bilder: Ordner {Folder} wird nicht beobachtet: {Message}", folder, ex.Message);
            }
        }
    }

    /// <summary>Anzahl der aktiven Beobachter (Prüfung: im Leerlauf 0).</summary>
    public int Count => _watchers.Count;

    /// <summary>Meldet einen Pfad wie ein Ereignis des Dateisystems (Tests).</summary>
    public void Notify(string? path)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (path is null)
            {
                _all = true;
            }
            else
            {
                _pending.Add(path);
            }

            _timer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
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
            _timer?.Dispose();
            _timer = null;
            _pending.Clear();
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnEvent;
            watcher.Changed -= OnEvent;
            watcher.Deleted -= OnEvent;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Notify(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Notify(e.OldFullPath);
        Notify(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _logger.LogDebug("Bilder: Beobachtung meldet einen Fehler ({Error}), alle Spiele werden neu geprüft.", e.GetException().GetType().Name);
        Notify(null);
    }

    private void Flush()
    {
        IReadOnlyCollection<string>? paths;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            paths = _all ? null : _pending.ToList();
            _all = false;
            _pending.Clear();
        }

        try
        {
            _changed(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bilder: Fehler beim Verarbeiten geänderter Bilddateien.");
        }
    }
}
