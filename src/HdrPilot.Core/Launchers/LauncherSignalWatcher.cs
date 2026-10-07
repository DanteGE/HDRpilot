using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Launchers;

/// <summary>
/// Frühwarnung: liest die Protokolle von Epic Games Launcher und Steam mit und meldet angekündigte Spielstarts, bevor
/// der Spielprozess existiert. Beginnt am Ende der Dateien (kein Nachspielen alter Starts).
/// <list type="bullet">
/// <item>Ein FileSystemWatcher je Protokollordner löst sofort eine Prüfung aus (Schreiben, Umbenennen, Neuanlage).</item>
/// <item>Sicherheitsnetz ist <see cref="Tick"/>, im Takt der Prozessüberwachung aufgerufen (kein eigener Zeitgeber, also
/// kein zusätzliches Aufwachen): je Protokoll eine Längenabfrage über das offene Handle; nur bei einer Änderung wird
/// gelesen. Alle <see cref="VerifyEveryTicks"/> Takte (ohne offene Datei alle <see cref="VerifyMissingEveryTicks"/>) wird
/// zusätzlich über den Pfad geprüft, ob der Launcher die Datei ersetzt hat, falls ein Dateiereignis verloren ging.</item>
/// </list>
/// Kein Zugriff auf Prozesse der Launcher. Die Rückmeldung bekommt nur abgeleitete Werte (<see cref="LauncherSignal"/>),
/// nie Zeilen.
/// </summary>
public sealed class LauncherSignalWatcher : IDisposable
{
    /// <summary>Takte zwischen zwei Prüfungen über den Pfad, solange die Datei offen ist (bei 1 s Takt: 30 s).</summary>
    public const int VerifyEveryTicks = 30;

    /// <summary>Takte zwischen zwei Prüfungen über den Pfad, solange die Datei fehlt.</summary>
    public const int VerifyMissingEveryTicks = 5;

    private readonly List<Tail> _tails;
    private readonly LauncherSignalDeduplicator _dedupe;
    private readonly Action<LauncherSignal> _onSignal;
    private readonly ILogger _logger;
    private readonly TimeSpan? _ownInterval;
    private readonly object _gate = new();
    private Timer? _timer;
    private long _ticks;
    private bool _started;
    private bool _disposed;

    /// <param name="onSignal">Wird für jeden neuen Start einmal aufgerufen (nacheinander, nie gleichzeitig).</param>
    /// <param name="ownTickInterval">
    /// Null (Automatik): Der Aufrufer ruft <see cref="Tick"/> selbst auf. Sonst ein eigener Zeitgeber in diesem Abstand
    /// (Messwerkzeuge, Tests).
    /// </param>
    public LauncherSignalWatcher(
        IEnumerable<LauncherLogSource> sources, IClock clock, Action<LauncherSignal> onSignal, ILogger? logger = null, TimeSpan? ownTickInterval = null)
    {
        _tails = sources.Select(s => new Tail(s)).ToList();
        _dedupe = new LauncherSignalDeduplicator(clock);
        _onSignal = onSignal;
        _logger = logger ?? NullLogger.Instance;
        _ownInterval = ownTickInterval;
    }

    public IReadOnlyList<LauncherLogSource> Sources => _tails.Select(t => t.Source).ToList();

    /// <summary>Öffnet jedes Protokoll an seinem Ende und startet die Dateiereignisse.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            foreach (var tail in _tails)
            {
                try
                {
                    tail.Log.StartAtEnd();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Der nächste Takt versucht es über den Pfad erneut.
                    _logger.LogWarning("Protokoll von {Launcher} ließ sich nicht öffnen: {Error}", tail.Source.Launcher, ex.Message);
                }

                tail.Events = CreateFileWatcher(tail);
            }

            if (_ownInterval is { } interval)
            {
                _timer = new Timer(_ => Tick(), null, interval, interval);
            }
        }
    }

    /// <summary>Sicherheitsnetz, etwa einmal pro Sekunde aufzurufen. Wirft nicht.</summary>
    public void Tick()
    {
        var tick = Interlocked.Increment(ref _ticks);
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            foreach (var tail in _tails)
            {
                var every = tail.Log.IsOpen ? VerifyEveryTicks : VerifyMissingEveryTicks;
                Check(tail, verifyPath: tail.Log.VerifyPending || tick % every == 0);
            }
        }
    }

    /// <summary>Prüft alle Protokolle sofort; mit <paramref name="verifyPaths"/> auch, ob eine Datei ersetzt wurde.</summary>
    public void CheckNow(bool verifyPaths = false)
    {
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            foreach (var tail in _tails)
            {
                Check(tail, verifyPaths || tail.Log.VerifyPending);
            }
        }
    }

    /// <summary>Für Tests: Dateiereignisse abschalten, damit nur noch <see cref="Tick"/> prüft.</summary>
    internal void SuspendFileEvents()
    {
        foreach (var tail in _tails)
        {
            if (tail.Events is { } events)
            {
                events.EnableRaisingEvents = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            // Wartet auf eine laufende Prüfung; danach prüft niemand mehr, und die Dateien werden losgelassen.
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var tail in _tails)
            {
                tail.Log.Dispose();
            }
        }

        foreach (var tail in _tails)
        {
            tail.Events?.Dispose();
        }

        _timer?.Dispose();
    }

    /// <summary>Nur unter <see cref="_gate"/> aufrufen.</summary>
    private void Check(Tail tail, bool verifyPath)
    {
        try
        {
            if (!verifyPath && !tail.Log.MayHaveNewData())
            {
                return;
            }

            foreach (var line in tail.Log.ReadNewLines(tail.Filter, verifyPath))
            {
                if (LauncherLogParser.Parse(tail.Source.Launcher, line) is { } signal && _dedupe.IsNew(signal))
                {
                    Raise(signal);
                }
            }

            tail.ErrorLogged = false;
        }
        catch (Exception ex)
        {
            // Nie die Prozessüberwachung aufhalten, in deren Takt geprüft wird; nur einmal je Störung melden.
            if (!tail.ErrorLogged)
            {
                tail.ErrorLogged = true;
                _logger.LogWarning("Protokoll von {Launcher} ließ sich nicht lesen: {Error}: {Message}", tail.Source.Launcher, ex.GetType().Name, ex.Message);
            }
        }
    }

    private void Raise(LauncherSignal signal)
    {
        try
        {
            _onSignal(signal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Launcher-Signal für {Key} ließ sich nicht weitergeben.", signal.GameKey);
        }
    }

    private FileSystemWatcher? CreateFileWatcher(Tail tail)
    {
        try
        {
            var events = new FileSystemWatcher(tail.Source.Directory, tail.Source.FileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            events.Changed += (_, _) => OnFileEvent(tail, verifyPath: false);
            events.Created += (_, _) => OnFileEvent(tail, verifyPath: true);
            events.Deleted += (_, _) => OnFileEvent(tail, verifyPath: true);
            events.Renamed += (_, _) => OnFileEvent(tail, verifyPath: true);
            events.Error += (_, _) => OnFileEvent(tail, verifyPath: true);
            events.EnableRaisingEvents = true;
            return events;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Keine Dateiereignisse für das Protokoll von {Launcher}, nur Prüfung im Sekundentakt: {Error}", tail.Source.Launcher, ex.Message);
            return null;
        }
    }

    /// <param name="verifyPath">Datei angelegt, umbenannt oder gelöscht: über den Pfad prüfen, ob sie ersetzt wurde.</param>
    private void OnFileEvent(Tail tail, bool verifyPath)
    {
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            Check(tail, verifyPath || tail.Log.VerifyPending);
        }
    }

    private sealed class Tail(LauncherLogSource source)
    {
        public LauncherLogSource Source { get; } = source;

        public LogTail Log { get; } = new(source.FullPath);

        public LogLineFilter Filter { get; } = line => LauncherLogParser.IsCandidate(source.Launcher, line);

        public FileSystemWatcher? Events { get; set; }

        public bool ErrorLogged { get; set; }
    }
}
