using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Launchers;

/// <summary>Was die Frühwarnung von der Sitzungslogik braucht (für Tests austauschbar).</summary>
public interface ILauncherSignalSessions
{
    bool IsInitialized { get; }

    bool Paused { get; }

    IReadOnlyList<SessionInfo> Sessions { get; }

    Task<PrepareOutcome> PrepareAsync(string gameKey, SessionTrigger trigger, CancellationToken cancellationToken = default);
}

/// <summary><see cref="SessionManager"/> als Ziel der Frühwarnung.</summary>
public sealed class SessionManagerSignalTarget(SessionManager sessions) : ILauncherSignalSessions
{
    public bool IsInitialized => sessions.IsInitialized;

    public bool Paused => sessions.Paused;

    public IReadOnlyList<SessionInfo> Sessions => sessions.Sessions;

    public Task<PrepareOutcome> PrepareAsync(string gameKey, SessionTrigger trigger, CancellationToken cancellationToken = default) =>
        sessions.PrepareAsync(gameKey, trigger, cancellationToken);
}

/// <summary>Was aus einem Launcher-Signal wurde.</summary>
public enum LauncherSignalResult
{
    /// <summary>HDR ist an, die Sitzung wartet auf den Spielprozess.</summary>
    Prepared,

    /// <summary>Frühwarnung in der Konfiguration ausgeschaltet.</summary>
    Disabled,

    /// <summary>Spiel nicht in der Bibliothek.</summary>
    UnknownGame,

    /// <summary>Spiel ohne „HDR automatisch“.</summary>
    NotAutomatic,

    /// <summary>Automatik pausiert.</summary>
    Paused,

    /// <summary>Sitzungslogik noch nicht initialisiert (erster Zyklus nach dem Start steht aus).</summary>
    NotReady,

    /// <summary>Das Spiel läuft schon (Sitzung mit erkanntem Spielprozess).</summary>
    SessionExists,

    /// <summary>Die Sitzungslogik hat nichts vorbereitet.</summary>
    NotPrepared,

    /// <summary>Fehler bei der Verarbeitung (protokolliert).</summary>
    Failed,
}

/// <summary>
/// Entscheidet, ob ein angekündigter Spielstart HDR schon vor dem Spielprozess einschaltet: nur für Spiele der
/// Bibliothek mit „HDR automatisch“, nicht bei pausierter Automatik und nicht, solange das Spiel läuft (dann bleiben
/// HDR-Änderungen von Hand unangetastet). Dann übernimmt <see cref="SessionManager.PrepareAsync"/> (HDR an und
/// bestätigt, Sitzung wartet auf den Spielprozess; startet das Spiel doch nicht, stellt die Startfrist zurück). Eine
/// endende Sitzung (Nachlaufzeit, Neustart-Puffer) oder eine, die noch auf ihr Spiel wartet, wird dabei wieder auf
/// „wartet auf Spielprozess“ gestellt; ein doppeltes Signal kurz nach einem Start ändert nichts (E71).
/// </summary>
public sealed class LauncherSignalHandler(ISessionConfigSource config, ILauncherSignalSessions sessions, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Wirft nicht; Fehler werden protokolliert.</summary>
    public async Task<LauncherSignalResult> HandleAsync(LauncherSignal signal, CancellationToken cancellationToken = default)
    {
        try
        {
            var cfg = config.Current;
            if (!cfg.Settings.LauncherSignals)
            {
                return LauncherSignalResult.Disabled;
            }

            var game = cfg.FindGame(signal.GameKey);
            if (game is null)
            {
                _logger.LogDebug("Launcher-Signal von {Launcher} für {Key}: nicht in der Bibliothek.", signal.Launcher, signal.GameKey);
                return LauncherSignalResult.UnknownGame;
            }

            if (!game.HdrAuto)
            {
                _logger.LogDebug("Launcher-Signal von {Launcher} für \"{Game}\": „HDR automatisch“ ist aus.", signal.Launcher, game.Name);
                return LauncherSignalResult.NotAutomatic;
            }

            if (sessions.Paused)
            {
                _logger.LogDebug("Launcher-Signal von {Launcher} für \"{Game}\": Automatik pausiert.", signal.Launcher, game.Name);
                return LauncherSignalResult.Paused;
            }

            if (!sessions.IsInitialized)
            {
                return LauncherSignalResult.NotReady;
            }

            // Nur ein laufendes Spiel bleibt unangetastet. Endet seine Sitzung gerade (Nachlaufzeit, Neustart-Puffer) oder
            // wartet sie noch, stellt PrepareAsync sie wieder auf den Start ein; sonst liefe die Frist während des neuen
            // Starts ab und HDR ginge mitten im Start aus und wieder an.
            if (sessions.Sessions.Any(s => string.Equals(s.GameKey, game.Key, StringComparison.OrdinalIgnoreCase) && s.Phase == SessionPhase.Running))
            {
                _logger.LogDebug("Launcher-Signal von {Launcher} für \"{Game}\": das Spiel läuft schon.", signal.Launcher, game.Name);
                return LauncherSignalResult.SessionExists;
            }

            _logger.LogInformation(
                "Launcher-Signal von {Launcher}: Start von \"{Game}\" ({Key}) angekündigt, HDR wird vor dem Spielprozess eingeschaltet.",
                signal.Launcher, game.Name, game.Key);
            var outcome = await sessions.PrepareAsync(game.Key, SessionTrigger.LauncherSignal, cancellationToken).ConfigureAwait(false);
            return outcome.Prepared ? LauncherSignalResult.Prepared : LauncherSignalResult.NotPrepared;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LauncherSignalResult.Failed; // beim Beenden von HdrPilot
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Launcher-Signal für {Key} ließ sich nicht verarbeiten.", signal.GameKey);
            return LauncherSignalResult.Failed;
        }
    }
}
