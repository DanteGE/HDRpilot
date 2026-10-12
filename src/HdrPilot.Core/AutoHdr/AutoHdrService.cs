using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Windows Auto HDR je Spiel (E88), getrennt von „HDR automatisch“: Für Spiele mit <see cref="GameConfig.AutoHdr"/> setzt
/// HDRpilot den globalen Auto-HDR-Schalter von Windows direkt vor jedem Start, den es vorher kennt, und stellt ihn nach
/// der Sitzung zurück (<see cref="AutoHdrGlobalSwitch"/>). Werte je Spiel-EXE schreibt HDRpilot nicht: Sie wirken unter
/// 26H2 nicht (Livetest G9.6). Dazu Lesen und Umschalten des Schalters durch den Nutzer (Tray-Eintrag „Auto HDR“).
/// <para>
/// Keine öffentliche Methode wirft; Fehler stehen im Protokoll (Kategorie „AutoHdr“). Vor <see cref="Start"/> tun
/// Konfigurationsänderungen und die Aufrufe der Sitzungslogik nichts.
/// </para>
/// </summary>
public sealed class AutoHdrService : IAutoHdrService, IAutoHdrSessionHooks
{
    private readonly ConfigService _config;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly AutoHdrGlobalSwitch _switch;
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
        _switch = new AutoHdrGlobalSwitch(
            store, fileSystem, paths.AutoHdrGlobalFile, environment, clock, _logger, sessionOptions ?? SessionManagerOptions.Default);
    }

    public AutoHdrRegistryLocation Location { get; }

    public bool HoldsGlobalSwitch => _switch.IsHeld;

    /// <summary>
    /// Beim Start der Automatik, vor dem ersten Zyklus: nur prüfen, ob ein Merker eines abgebrochenen Laufs da ist (eine
    /// Dateiprüfung, kein Registry-Zugriff); zurückgestellt wird im ersten Zyklus (<see cref="OnRecovery"/>). Wirft nie.
    /// </summary>
    public void Start()
    {
        lock (_startGate)
        {
            if (_started)
            {
                return;
            }

            try
            {
                _switch.Start(_fileSystem.FileExists(_paths.AutoHdrGlobalFile));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto HDR: Start fehlgeschlagen.");
            }
            finally
            {
                _started = true;
            }
        }
    }

    /// <summary>
    /// Konfiguration geändert: Hat kein angekündigtes oder laufendes Spiel die Option mehr (ausgeschaltet, Spiel entfernt),
    /// sofort zurückstellen. Ohne gehaltenen Schalter kein Registry-Zugriff. Wirft nie.
    /// </summary>
    public void OnConfigChanged()
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _switch.OnConfigChanged(_config.Current, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Abgleich nach Konfigurationsänderung fehlgeschlagen.");
        }
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
            _switch.Shutdown(_clock.UtcNow);
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

    public AutoHdrSwitchState ReadGlobalSwitch()
    {
        try
        {
            return _switch.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto HDR: globaler Schalter nicht lesbar.");
            return AutoHdrSwitchState.Unknown;
        }
    }

    public AutoHdrSwitchState SetGlobalSwitchByUser(bool on)
    {
        try
        {
            return _switch.SetByUser(on);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Umschalten durch den Nutzer fehlgeschlagen.");
            return AutoHdrSwitchState.Unknown;
        }
    }

    public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _switch.OnStartAnnounced(game, _config.Current, trigger, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Vorbereitung des Starts von {Key} fehlgeschlagen.", game.Key);
        }
    }

    public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _switch.OnRecovery(runningGameKeys, config, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Wiederanlauf fehlgeschlagen.");
        }
    }

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, IReadOnlyCollection<string> waitingGameKeys, AppConfig config, DateTimeOffset now)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            _switch.OnCycle(runningGameKeys, waitingGameKeys, config, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto HDR: Zyklus fehlgeschlagen.");
        }
    }
}
