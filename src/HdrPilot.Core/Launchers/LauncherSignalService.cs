using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Launchers;

/// <summary>
/// Hält die Frühwarnung für die Automatik: startet den <see cref="LauncherSignalWatcher"/> für die installierten Launcher,
/// wenn die Einstellung <see cref="AppSettings.LauncherSignals"/> an ist, und folgt ihr bei jeder Konfigurationsänderung.
/// Signale werden nacheinander an den <see cref="LauncherSignalHandler"/> gegeben, ohne die Dateiprüfung aufzuhalten.
/// </summary>
public sealed class LauncherSignalService : IDisposable
{
    private readonly ISessionConfigSource _config;
    private readonly LauncherSignalHandler _handler;
    private readonly Func<IReadOnlyList<LauncherLogSource>> _locate;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private LauncherSignalWatcher? _watcher;
    private Task _pending = Task.CompletedTask;
    private bool _started;
    private bool _disposed;
    private bool _noLauncherLogged;

    /// <param name="locate">Liefert die Protokolle der installierten Launcher (siehe <see cref="LauncherLogLocator"/>).</param>
    public LauncherSignalService(
        ISessionConfigSource config,
        LauncherSignalHandler handler,
        Func<IReadOnlyList<LauncherLogSource>> locate,
        IClock clock,
        ILogger? logger = null)
    {
        _config = config;
        _handler = handler;
        _locate = locate;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Zusammenbau für die Automatik: Protokolle über <see cref="LauncherLogLocator"/>, Signale an
    /// <see cref="SessionManager.PrepareAsync"/> mit dem Auslöser <see cref="SessionTrigger.LauncherSignal"/>.
    /// </summary>
    public static LauncherSignalService Create(
        ISessionConfigSource config,
        SessionManager sessions,
        IFileSystem fileSystem,
        IRegistry registry,
        IAppEnvironment environment,
        IClock clock,
        ILogger? logger = null) =>
        new(
            config,
            new LauncherSignalHandler(config, new SessionManagerSignalTarget(sessions), logger),
            () => LauncherLogLocator.Find(fileSystem, registry, environment),
            clock,
            logger);

    /// <summary>Protokolle, die gerade beobachtet werden (leer: Frühwarnung aus oder kein Launcher installiert).</summary>
    public IReadOnlyList<LauncherLogSource> ActiveSources
    {
        get
        {
            lock (_gate)
            {
                return _watcher?.Sources ?? [];
            }
        }
    }

    /// <summary>Nach dem ersten Zyklus der Sitzungslogik aufrufen (vorher lehnt sie Vorbereitungen ab).</summary>
    public void Start()
    {
        lock (_gate)
        {
            _started = true;
        }

        ApplySettings();
    }

    /// <summary>Nach einer Konfigurationsänderung: Frühwarnung ein- oder ausschalten.</summary>
    public void ApplySettings()
    {
        LauncherSignalWatcher? stopped = null;
        lock (_gate)
        {
            if (!_started || _disposed)
            {
                return;
            }

            var enabled = _config.Current.Settings.LauncherSignals;
            if (enabled && _watcher is null)
            {
                var sources = Locate();
                if (sources.Count == 0)
                {
                    if (!_noLauncherLogged)
                    {
                        _noLauncherLogged = true;
                        _logger.LogInformation("Frühwarnung aus Launcher-Protokollen: weder Epic Games Launcher noch Steam gefunden.");
                    }

                    return;
                }

                var watcher = new LauncherSignalWatcher(sources, _clock, OnSignal, _logger);
                try
                {
                    watcher.Start();
                }
                catch (Exception ex)
                {
                    // Die Frühwarnung ist eine Zugabe: Start der Automatik und Speichern der Konfiguration gehen vor.
                    watcher.Dispose();
                    _logger.LogWarning(ex, "Frühwarnung aus Launcher-Protokollen ließ sich nicht starten.");
                    return;
                }

                _watcher = watcher;
                _logger.LogInformation("Frühwarnung aus Launcher-Protokollen aktiv: {Launchers}.", string.Join(", ", sources.Select(s => s.Launcher)));
            }
            else if (!enabled && _watcher is not null)
            {
                stopped = _watcher;
                _watcher = null;
                _logger.LogInformation("Frühwarnung aus Launcher-Protokollen ausgeschaltet.");
            }
        }

        stopped?.Dispose();
    }

    /// <summary>
    /// Sicherheitsnetz im Takt der Prozessüberwachung (siehe <see cref="LauncherSignalWatcher.Tick"/>): kein eigener
    /// Zeitgeber, also kein zusätzliches Aufwachen. Wirft nicht.
    /// </summary>
    public void Tick() => CurrentWatcher()?.Tick();

    /// <summary>Prüft die Protokolle sofort, statt auf Dateiereignis oder Takt zu warten (für Tests).</summary>
    internal void CheckNow(bool verifyPaths = false) => CurrentWatcher()?.CheckNow(verifyPaths);

    /// <summary>Für Tests: Dateiereignisse abschalten, damit nur noch <see cref="Tick"/> prüft.</summary>
    internal void SuspendFileEvents() => CurrentWatcher()?.SuspendFileEvents();

    /// <summary>Wartet, bis alle bisher gemeldeten Signale verarbeitet sind (für Tests und Diagnose).</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _pending;
        }
    }

    public void Dispose()
    {
        LauncherSignalWatcher? watcher;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watcher = _watcher;
            _watcher = null;
        }

        watcher?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }

    private LauncherSignalWatcher? CurrentWatcher()
    {
        lock (_gate)
        {
            return _watcher;
        }
    }

    private IReadOnlyList<LauncherLogSource> Locate()
    {
        try
        {
            return _locate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            _logger.LogWarning("Launcher-Protokolle ließen sich nicht suchen: {Error}", ex.Message);
            return [];
        }
    }

    private void OnSignal(LauncherSignal signal)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Nacheinander abarbeiten (Signale sind selten); die Dateiprüfung wartet nicht auf das Umschalten.
            var token = _cts.Token;
            _pending = _pending
                .ContinueWith(_ => _handler.HandleAsync(signal, token), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)
                .Unwrap();
        }
    }
}
