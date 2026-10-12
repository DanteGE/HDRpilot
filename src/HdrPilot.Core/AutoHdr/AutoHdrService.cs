using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Windows Auto HDR je Spiel (E88), getrennt von „HDR automatisch“: Für Spiele mit <see cref="GameConfig.AutoHdr"/> trägt
/// HDRpilot je Spiel-EXE <c>AutoHDREnable=2097</c> in den Schlüssel UserGpuPreferences ein und nimmt es beim Ausschalten
/// wieder zurück (<see cref="AutoHdrRegistryEngine"/>); die Notlösung <see cref="AppSettings.AutoHdrGlobalFallback"/> setzt
/// zusätzlich den globalen Schalter, solange ein solches Spiel läuft (<see cref="AutoHdrGlobalFallback"/>). Abgleich beim
/// Start und bei jeder Konfigurationsänderung, Rücknahme auf Wunsch.
/// <para>
/// Keine öffentliche Methode wirft; Fehler stehen im Protokoll (Kategorie „AutoHdr“). Vor <see cref="Start"/> tun
/// Konfigurationsänderungen und die Aufrufe der Sitzungslogik nichts. <see cref="Changed"/> kommt nie unter einer Sperre.
/// </para>
/// </summary>
public sealed class AutoHdrService : IAutoHdrService, IAutoHdrSessionHooks
{
    private readonly ConfigService _config;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly AutoHdrRegistryEngine _engine;
    private readonly AutoHdrGlobalFallback _fallback;
    private readonly object _startGate = new();
    private volatile bool _started;

    /// <param name="sessionOptions">Neustart-Puffer und Grenze „kurzer Lauf“ wie in der Sitzungslogik (null: Standard).</param>
    public AutoHdrService(
        ConfigService config,
        IGpuPreferenceStore store,
        AutoHdrRegistryLocation location,
        IFileSystem fileSystem,
        AppPaths paths,
        IAppEnvironment environment,
        IClock clock,
        ILogger? logger = null,
        SessionManagerOptions? sessionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(clock);
        _config = config;
        _fileSystem = fileSystem;
        _paths = paths;
        _clock = clock;
        Location = location;
        _logger = logger ?? NullLogger.Instance;
        _engine = new AutoHdrRegistryEngine(store, fileSystem, paths.AutoHdrStateFile, environment, clock, _logger);
        _fallback = new AutoHdrGlobalFallback(
            store, fileSystem, paths.AutoHdrGlobalFile, environment, clock, _logger, sessionOptions ?? SessionManagerOptions.Default);
    }

    public AutoHdrRegistryLocation Location { get; }

    public event EventHandler? Changed;

    /// <summary>HDRpilot hält gerade den globalen Schalter der Notlösung (für Tests und Diagnose).</summary>
    public bool GlobalFallbackActive => _fallback.IsActive;

    /// <summary>
    /// Beim Start der Automatik, vor dem ersten Zyklus: gemerkten Zustand laden und einmal vollständig abgleichen. Ohne
    /// Spiel mit der Option und ohne Zustandsdateien nur zwei Dateiprüfungen, kein Registry-Zugriff. Wirft nie.
    /// </summary>
    public void Start()
    {
        var changed = false;
        lock (_startGate)
        {
            if (_started)
            {
                return;
            }

            try
            {
                var stateExists = _fileSystem.FileExists(_paths.AutoHdrStateFile);
                _fallback.Start(_fileSystem.FileExists(_paths.AutoHdrGlobalFile));
                changed = _engine.Start(_config.Current, stateExists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto HDR: Abgleich beim Start fehlgeschlagen.");
            }
            finally
            {
                _started = true;
            }
        }

        RaiseChanged(changed);
    }

    /// <summary>
    /// Konfiguration geändert (Option, gelernte EXEs, entferntes Spiel, Notlösung): abgleichen. Ohne geänderte Ziele kein
    /// Registry-Zugriff. Kann im Zyklus der Sitzungslogik kommen (gelernte EXEs). Wirft nie.
    /// </summary>
    public void OnConfigChanged()
    {
        if (!_started)
        {
            return;
        }

        var changed = false;
        try
        {
            var cfg = _config.Current;
            changed = _engine.Reconcile(cfg);
            _fallback.OnConfigChanged(cfg, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Abgleich nach Konfigurationsänderung fehlgeschlagen.");
        }

        RaiseChanged(changed);
    }

    /// <summary>Beim Beenden der Automatik (nach dem letzten Zyklus): globalen Schalter zurückstellen. Wirft nie.</summary>
    public void Shutdown()
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _fallback.Shutdown(_clock.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Zurückstellen beim Beenden fehlgeschlagen.");
        }
        finally
        {
            _started = false;
        }
    }

    public AutoHdrGameStatus GetStatus(string gameKey)
    {
        try
        {
            return _engine.GetStatus(gameKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Auto HDR: Stand für {Key} nicht verfügbar.", gameKey);
            return AutoHdrGameStatus.None;
        }
    }

    /// <summary>
    /// „Auto-HDR-Einträge zurücknehmen“ im Hintergrund: schaltet die Option bei allen Spielen aus (gespeichert), nimmt jeden
    /// gemerkten Wert zurück (auch die, die dabei schon der Abgleich zurücknahm, zählen mit) und stellt den globalen
    /// Schalter zurück. Lässt sich die Konfiguration nicht speichern, wird nichts zurückgenommen. Wirft nie; ein Abbruch
    /// wird nicht beachtet (die Rücknahme läuft immer zu Ende).
    /// </summary>
    public Task<AutoHdrRollbackResult> RollbackAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Rollback, CancellationToken.None);

    public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        var changed = false;
        try
        {
            var cfg = _config.Current;
            if (cfg.FindGame(game.Key) is { AutoHdr: true })
            {
                changed = _engine.Reconcile(cfg, game.Key); // fehlende Werte vor dem Spielprozess eintragen
            }

            _fallback.OnStartAnnounced(game, cfg, trigger, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Vorbereitung des Starts von {Key} fehlgeschlagen.", game.Key);
        }

        RaiseChanged(changed);
    }

    public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _fallback.OnRecovery(runningGameKeys, config, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Wiederanlauf fehlgeschlagen.");
        }
    }

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _fallback.OnCycle(runningGameKeys, config, paused, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Zyklus der Notlösung fehlgeschlagen.");
        }
    }

    private AutoHdrRollbackResult Rollback()
    {
        try
        {
            Start(); // falls die Automatik noch nicht lief: gemerkten Zustand erst laden
            if ((_engine.InertReason ?? _fallback.InertReason) is { } inert)
            {
                _logger.LogWarning("Auto HDR: Rücknahme nicht möglich: {Reason}.", inert);
                return new AutoHdrRollbackResult(0, 0, 0, 0, inert);
            }

            // Das Ausschalten in der Konfiguration löst schon einen Abgleich aus, der zurücknimmt: mitzählen.
            var switchedOff = 0;
            _engine.BeginTally();
            _fallback.BeginTally();
            try
            {
                if (_config.Current.Games.Any(g => g.AutoHdr))
                {
                    _config.Update(c =>
                    {
                        switchedOff = 0;
                        foreach (var game in c.Games.Where(g => g.AutoHdr))
                        {
                            game.AutoHdr = false;
                            switchedOff++;
                        }
                    });
                }
            }
            catch (ConfigSaveException ex)
            {
                _engine.EndTally();
                _fallback.EndTally();
                _logger.LogWarning("Auto HDR: Rücknahme abgebrochen, Konfiguration nicht gespeichert: {Error}", ex.Reason);
                return new AutoHdrRollbackResult(0, 0, 0, 0, ex.Message);
            }

            var changed = _engine.RestoreAll(_config.Current, "Auto-HDR-Einträge zurückgenommen");
            _fallback.ReleaseForRollback(_clock.UtcNow);
            var (restored, leftAsIs, failed) = _engine.EndTally();
            var global = _fallback.EndTally();
            var result = new AutoHdrRollbackResult(
                switchedOff, restored + global.Restored, leftAsIs + global.LeftAsIs, failed + global.Failed);
            _logger.LogInformation(
                "Auto HDR: Einträge zurückgenommen auf Wunsch: bei {Games} Spielen ausgeschaltet, {Restored} zurückgenommen, {Left} extern geändert (bleiben), {Failed} fehlgeschlagen.",
                result.GamesSwitchedOff, result.Restored, result.LeftAsIs, result.Failed);
            RaiseChanged(changed || result.Restored + result.LeftAsIs > 0);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Rücknahme fehlgeschlagen.");
            return new AutoHdrRollbackResult(0, 0, 0, 0, ex.Message);
        }
    }

    private void RaiseChanged(bool changed)
    {
        if (!changed)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto HDR: Fehler in einem Changed-Empfänger.");
        }
    }
}
