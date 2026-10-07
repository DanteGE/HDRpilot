using System.Diagnostics;
using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Native;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Resources;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Sessions;

/// <summary>
/// Sitzungslogik: erkennt Spielstart und -ende, merkt sich vor dem Einschalten den HDR-Zustand jedes betroffenen
/// Bildschirms (sofort auf der Platte), schaltet erst nach dem Ende des letzten Spiels zurück und stellt nach einem
/// Absturz beim nächsten Start den gemerkten Zustand wieder her. Alle Abläufe sind serialisiert. Nach kurzen Sitzungen
/// gilt ein Neustart-Puffer; scheint ein laufendes Spiel unsichtbar zu hängen, kommt ein Hinweis.
/// </summary>
public sealed class SessionManager
{
    private readonly IHdrController _hdr;
    private readonly ISessionConfigSource _config;
    private readonly ISessionStateStore _store;
    private readonly IGameLauncher _launcher;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly Func<string, IReadOnlyList<string>?, bool> _isHelper;
    private readonly IReadOnlyList<string> _forbiddenRoots;
    private readonly SessionManagerOptions _options;
    private readonly Func<bool> _fullscreenAppRunning;
    private readonly IProcessActivityProbe _activity;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>So lange wartet ein Start über die App höchstens, solange ein Launcher-Fenster aus dem Spielordner offen ist.</summary>
    private static readonly TimeSpan MaxLauncherWait = TimeSpan.FromMinutes(30);

    /// <summary>Ein zweiter Start desselben Spiels so kurz danach gilt als doppelt angeklickt.</summary>
    private static readonly TimeSpan DuplicateLaunchWindow = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, ActiveSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DisplayClaim> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MatchedProcess> _pidMatches = [];
    private readonly Dictionary<string, LearnState> _learning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Laufende Läufe je Spiel für „zuletzt verwendet“ (Wert: seit wann kein Prozess mehr, sonst null).</summary>
    private readonly Dictionary<string, DateTimeOffset?> _playing = new(StringComparer.OrdinalIgnoreCase);
    private GameMatcher? _matcher;

    /// <summary>Alle Spiele der Bibliothek, nur für die Sperre der Tastenkürzel-Notlösung (E66).</summary>
    private GameMatcher? _anyGameMatcher;
    private long _matcherRevision = -1;
    private bool _recovered;

    /// <summary>Spiele aus dem gemerkten Zustand eines abgebrochenen Laufs: werden beim Wiederanlauf mit zugeordnet.</summary>
    private HashSet<string> _recoveryKeys = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<SessionInfo> _publicSessions = [];
    private volatile bool _paused;
    private volatile bool _hotkeyFallbackAllowed = true;

    /// <summary>Zeitpunkt der letzten Prüfung auf hängende Spiele; null, solange keine Sitzung läuft.</summary>
    private DateTimeOffset? _lastHangCheck;

    /// <param name="isHelper">Prüft einen EXE-Namen gegen die Ausschlussliste (Muster aus der Konfiguration, null = Standard).</param>
    /// <param name="forbiddenRoots">Ordner, die nie als Installationsordner gelten (z. B. Windows, Programme).</param>
    /// <param name="options">Neustart-Puffer und Hinweis „Spiel hängt“ (null = Werte für den Betrieb).</param>
    public SessionManager(
        IHdrController hdr,
        ISessionConfigSource config,
        ISessionStateStore store,
        IGameLauncher launcher,
        IClock clock,
        Func<string, IReadOnlyList<string>?, bool> isHelper,
        IReadOnlyList<string>? forbiddenRoots = null,
        ILogger? logger = null,
        SessionManagerOptions? options = null)
    {
        _hdr = hdr;
        _config = config;
        _store = store;
        _launcher = launcher;
        _clock = clock;
        _isHelper = isHelper;
        _forbiddenRoots = forbiddenRoots ?? [];
        _logger = logger ?? NullLogger.Instance;
        _options = options ?? SessionManagerOptions.Default;
        _fullscreenAppRunning = _options.FullscreenAppRunning ?? ShellNative.IsFullscreenAppRunning;
        _activity = _options.ActivityProbe ?? new NtProcessActivityProbe(_logger);
    }

    /// <summary>Meldung für einen Tray-Hinweis.</summary>
    public event EventHandler<SessionNotification>? Notification;

    /// <summary>Sitzungen oder gemerkte Zustände haben sich geändert.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Automatik pausiert: neue Spielstarts schalten nichts. Laufende Sitzungen enden normal.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused != value)
            {
                _paused = value;
                _logger.LogInformation("Automatik {State}.", value ? "pausiert" : "fortgesetzt");
            }
        }
    }

    public IReadOnlyList<SessionInfo> Sessions => _publicSessions;

    /// <summary>
    /// Ob die Tastenkürzel-Notlösung gerade benutzt werden darf (Stand des letzten Zyklus): nicht, solange ein Spiel der
    /// Bibliothek läuft oder ein über HdrPilot gestartetes Spiel noch nicht erkannt ist (E66). Für das Umschalten von Hand.
    /// </summary>
    public bool HotkeyFallbackAllowed => _hotkeyFallbackAllowed;

    public bool IsInitialized => _recovered;

    /// <summary>
    /// Erster Schritt nach dem Start von HdrPilot: liest den gemerkten Zustand und stellt ihn wieder her, wenn kein
    /// ausgewähltes Spiel mehr läuft; laufende Spiele werden übernommen. Muss vor <see cref="LaunchAsync"/> laufen;
    /// <see cref="UpdateAsync"/> ruft es beim ersten Zyklus selbst auf.
    /// </summary>
    public async Task InitializeAsync(ProcessChanges snapshot, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recovered)
            {
                return;
            }

            var revision = _config.Revision; // erst die Revision, dann den Stand lesen (sonst Wettlauf mit Update)
            var cfg = _config.Current;
            PrepareRecovery();
            RefreshMatches(snapshot, cfg, revision);
            _recovered = true;
            await RecoverCoreAsync(RunningGames(), cfg, cancellationToken).ConfigureAwait(false);
            FinishRecovery();
        }
        finally
        {
            var changed = PublishState();
            _lock.Release();
            RaiseStateChanged(changed);
        }
    }

    /// <summary>Ein Abfragezyklus der Überwachung (Weg A, Nachlaufzeit, Wiederanlauf beim ersten Aufruf).</summary>
    public async Task UpdateAsync(ProcessChanges changes, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.UtcNow;
            var revision = _config.Revision;
            var cfg = _config.Current;
            if (!_recovered)
            {
                PrepareRecovery();
            }

            RefreshMatches(changes, cfg, revision);
            var running = RunningGames();

            if (!_recovered)
            {
                _recovered = true;
                await RecoverCoreAsync(running, cfg, cancellationToken).ConfigureAwait(false);
                FinishRecovery();
            }

            Learn(running, cfg, now);

            foreach (var (key, processes) in running)
            {
                if (_sessions.TryGetValue(key, out var session))
                {
                    Touch(session, processes, now);
                    continue;
                }

                var game = cfg.FindGame(key);
                if (game is null || !game.HdrAuto || Paused)
                {
                    continue;
                }

                await StartSessionAsync(game, SessionTrigger.ProcessDetected, processes, cfg, startRequested: false, cancellationToken).ConfigureAwait(false);
            }

            var grace = TimeSpan.FromSeconds(cfg.Settings.GracePeriodSeconds);
            var launchTimeout = TimeSpan.FromSeconds(cfg.Settings.LaunchTimeoutSeconds);
            foreach (var session in _sessions.Values.ToList())
            {
                if (running.ContainsKey(session.Key))
                {
                    continue;
                }

                if (session.Phase == SessionPhase.Launching)
                {
                    if (session.LaunchCommandIssued && now - session.StartedAt < MaxLauncherWait
                        && _matcher!.FindInInstallDir(session.Key, _pidMatches.Values.Select(m => m.Process)) is { } launcherWindow)
                    {
                        // Ein Programm aus dem Spielordner läuft noch (etwa der REDlauncher, der auf die Anmeldung wartet):
                        // die Startfrist beginnt neu, HDR bleibt an.
                        if (!session.WaitingForLauncherLogged)
                        {
                            session.WaitingForLauncherLogged = true;
                            _logger.LogInformation(
                                "\"{Game}\": {Exe} aus dem Spielordner läuft noch, Startfrist wird verlängert (höchstens {Max} min).",
                                session.Name, launcherWindow.ExeName, MaxLauncherWait.TotalMinutes);
                        }

                        session.LaunchRequestedAt = now;
                        continue;
                    }

                    if (now - (session.LaunchRequestedAt ?? session.StartedAt) >= launchTimeout)
                    {
                        // Das gestartete Spiel kann unerkannt laufen (EXE außerhalb des Installationsordners): keine Notlösung.
                        await EndSessionAsync(
                            session, $"kein Spielprozess innerhalb von {launchTimeout.TotalSeconds:0} s nach dem Start", cancellationToken,
                            gameMayRun: session.LaunchCommandIssued).ConfigureAwait(false);
                    }

                    continue;
                }

                if (session.MissingSince is null)
                {
                    session.MissingSince = now;
                    session.Phase = SessionPhase.Ending;
                    BeginEnding(session, grace, RestartBufferFor(cfg), now);
                }

                if (session.BufferUntil is { } bufferEnd)
                {
                    if (now < bufferEnd || HoldForFullscreen(session, now))
                    {
                        continue;
                    }

                    await EndSessionAsync(session, "Spielende nach Neustart-Puffer", cancellationToken).ConfigureAwait(false);
                }
                else if (now - session.MissingSince >= grace)
                {
                    await EndSessionAsync(session, "Spielende", cancellationToken).ConfigureAwait(false);
                }
            }

            // Erst nach den Schaltvorgängen: das Speichern der Konfiguration verzögert nie das Einschalten.
            RecordPlayed(running, cfg, now);
            CheckForHangs(now);
        }
        finally
        {
            var changed = PublishState();
            _lock.Release();
            RaiseStateChanged(changed);
        }
    }

    /// <summary>
    /// Weg B: HDR einschalten, Bestätigung abwarten, dann das Spiel über den Start-Befehl der Quelle starten.
    /// Gilt auch bei pausierter Automatik und für Spiele ohne „HDR automatisch“ (ausdrücklicher Wunsch).
    /// </summary>
    public async Task<LaunchOutcome> LaunchAsync(string gameKey, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_recovered)
            {
                // Sonst hielte der erste Zyklus den eben gemerkten Zustand für einen Absturzrest und stellte zurück.
                throw new InvalidOperationException("InitializeAsync muss vor LaunchAsync aufgerufen werden.");
            }

            var cfg = _config.Current;
            var game = cfg.FindGame(gameKey);
            if (game is null)
            {
                return new LaunchOutcome(false, Format(CoreStrings.LaunchUnknownGame, gameKey), null, null);
            }

            if (string.IsNullOrWhiteSpace(game.LaunchCommand))
            {
                // Ohne Start-Befehl gar nicht erst schalten (sonst HDR kurz an und gleich wieder aus).
                var noCommand = Format(CoreStrings.LaunchNoCommand, game.Name);
                _logger.LogWarning("Start über die App abgelehnt, HDR bleibt unverändert: {Message}", noCommand);
                Notify(NotificationKind.LaunchFailed, CoreStrings.NotifyLaunchFailedTitle, noCommand);
                return new LaunchOutcome(false, noCommand, null, null);
            }

            var (session, duplicate) = await BeginStartAsync(game, SessionTrigger.LaunchedViaApp, cfg, cancellationToken).ConfigureAwait(false);
            if (duplicate is not null)
            {
                _logger.LogInformation("Start über die App für \"{Game}\" übergangen: {Reason}", game.Name, duplicate);
                return new LaunchOutcome(true, duplicate, session.HdrConfirmedAt, null);
            }

            session.HdrConfirmedAt = _clock.UtcNow;
            _logger.LogInformation(
                "Start über die App: HDR-Schritt für \"{Game}\" abgeschlossen um {Time}. Starte das Spiel.", game.Name, Stamp(session.HdrConfirmedAt.Value));

            var launchedAt = _clock.UtcNow;
            var result = _launcher.Launch(game);
            if (result.Success)
            {
                session.LaunchCommandIssued = true;
                _logger.LogInformation("Start-Befehl ausgeführt um {Time}: {Message}", Stamp(launchedAt), result.Message);
            }
            else
            {
                _logger.LogWarning("Start fehlgeschlagen um {Time}: {Message}", Stamp(launchedAt), result.Message);
                Notify(NotificationKind.LaunchFailed, CoreStrings.NotifyLaunchFailedTitle, result.Message);
                if (session.Phase == SessionPhase.Launching)
                {
                    await EndSessionAsync(session, "Start fehlgeschlagen", cancellationToken).ConfigureAwait(false);
                }
            }

            return new LaunchOutcome(result.Success, result.Message, session.HdrConfirmedAt, launchedAt);
        }
        finally
        {
            var changed = PublishState();
            _lock.Release();
            RaiseStateChanged(changed);
        }
    }

    /// <summary>
    /// HDR vor einem Spielstart einschalten, den ein anderer ausführt: die Steam-Startoption (<c>HdrPilot.exe --wrap</c>)
    /// startet das Spiel gleich danach selbst. Wie <see cref="LaunchAsync"/> (bestehende Sitzung, doppelte Anforderung E71,
    /// Zustand vor dem Schalten gespeichert, HDR an und bestätigt), aber ohne Start-Befehl. Danach wartet die Sitzung auf
    /// den Spielprozess und der Start gilt als ausgeführt (Notlösung gesperrt, E66). Gilt auch bei pausierter Automatik
    /// und für Spiele ohne „HDR automatisch“ (ausdrücklicher Wunsch). Unbekanntes Spiel: nichts schalten.
    /// </summary>
    /// <param name="trigger">Auslöser für Sitzung und Protokoll, etwa <see cref="SessionTrigger.SteamLaunchOption"/>.</param>
    public async Task<PrepareOutcome> PrepareAsync(string gameKey, SessionTrigger trigger, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_recovered)
            {
                // Sonst hielte der erste Zyklus den eben gemerkten Zustand für einen Absturzrest und stellte zurück.
                throw new InvalidOperationException("InitializeAsync muss vor PrepareAsync aufgerufen werden.");
            }

            var cfg = _config.Current;
            var game = cfg.FindGame(gameKey);
            if (game is null)
            {
                var unknown = Format(CoreStrings.LaunchUnknownGame, gameKey);
                _logger.LogInformation("{Trigger}: {Message} HDR bleibt unverändert, das Spiel startet ohne Vorbereitung.", TriggerText(trigger), unknown);
                return new PrepareOutcome(false, unknown, null);
            }

            var (session, duplicate) = await BeginStartAsync(game, trigger, cfg, cancellationToken).ConfigureAwait(false);
            if (duplicate is not null)
            {
                _logger.LogInformation(
                    "{Trigger} für \"{Game}\": Sitzung besteht schon ({Reason}), HDR ist an.", TriggerText(trigger), game.Name, duplicate);
                return new PrepareOutcome(true, duplicate, session.HdrConfirmedAt ?? _clock.UtcNow);
            }

            session.HdrConfirmedAt = _clock.UtcNow;

            // Das Spiel startet gleich danach: ab jetzt kann es laufen, auch wenn sein Prozess noch nicht erkannt ist.
            session.LaunchCommandIssued = true;
            _logger.LogInformation(
                "{Trigger}: HDR-Schritt für \"{Game}\" abgeschlossen um {Time}. Das Spiel wird jetzt gestartet.",
                TriggerText(trigger), game.Name, Stamp(session.HdrConfirmedAt.Value));
            return new PrepareOutcome(true, Format(CoreStrings.PrepareReady, game.Name), session.HdrConfirmedAt);
        }
        finally
        {
            var changed = PublishState();
            _lock.Release();
            RaiseStateChanged(changed);
        }
    }

    /// <summary>Windows meldet geänderte Bildschirme (WM_DISPLAYCHANGE): ausstehende Schritte nachholen.</summary>
    public async Task OnDisplaysChangedAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Bildschirmänderung gemeldet, prüfe ausstehende Schaltvorgänge.");
            foreach (var claim in _claims.Values.Where(c => c.Games.Count == 0).ToList())
            {
                await ReleaseAsync(claim, "Bildschirmänderung (nachgeholtes Zurückstellen)", null, cancellationToken).ConfigureAwait(false);
            }

            foreach (var session in _sessions.Values.ToList())
            {
                await ReapplyAsync(session, cancellationToken).ConfigureAwait(false);
            }

            Persist();
        }
        finally
        {
            var changed = PublishState();
            _lock.Release();
            RaiseStateChanged(changed);
        }
    }

    /// <summary>
    /// Gemeinsamer Teil von Start über die App und Vorbereitung: neue Sitzung anlegen (Zustand vor dem Schalten gespeichert,
    /// HDR an und bestätigt) oder eine bestehende wieder auf „wartet auf Spielprozess“ stellen und HDR sicher einschalten.
    /// Läuft das Spiel schon oder wurde es gerade erst gestartet, ist die Anforderung doppelt (E71): dann nur HDR sicher an,
    /// die Sitzung bleibt, wie sie ist, und der Grund kommt zurück.
    /// </summary>
    private async Task<(ActiveSession Session, string? Duplicate)> BeginStartAsync(
        GameConfig game, SessionTrigger trigger, AppConfig cfg, CancellationToken cancellationToken)
    {
        var processes = RunningGames().GetValueOrDefault(game.Key) ?? [];
        if (!_sessions.TryGetValue(game.Key, out var session))
        {
            session = await StartSessionAsync(game, trigger, processes, cfg, startRequested: true, cancellationToken).ConfigureAwait(false);
            return (session, null);
        }

        // Läuft das Spiel schon oder wurde es gerade erst gestartet (etwa doppelter Doppelklick auf die Verknüpfung),
        // keinen zweiten Start-Befehl schicken: bei direkt gestarteten EXEs gäbe das eine zweite Instanz (E71).
        var duplicate = processes.Count > 0
            ? Format(CoreStrings.LaunchAlreadyRunning, game.Name)
            : session.Phase == SessionPhase.Launching && session.LaunchCommandIssued
              && _clock.UtcNow - (session.LaunchRequestedAt ?? session.StartedAt) < DuplicateLaunchWindow
                ? Format(CoreStrings.LaunchAlreadyStarting, game.Name)
                : null;
        if (duplicate is not null)
        {
            await EnsureOnAsync(session, trigger, cancellationToken).ConfigureAwait(false);
            return (session, duplicate);
        }

        if (processes.Count == 0)
        {
            // Erneuter Start (etwa während der Nachlaufzeit oder nach einem Neustart des Spiels):
            // Sitzung wieder auf "wartet auf Spielprozess" stellen, Startfrist neu beginnen.
            session.Phase = SessionPhase.Launching;
            ClearEnding(session);
            session.LaunchRequestedAt = _clock.UtcNow;
            _logger.LogInformation("Erneuter {Trigger} für \"{Game}\": Sitzung wartet wieder auf den Spielprozess.", TriggerText(trigger), game.Name);
        }

        await ReapplyAsync(session, cancellationToken).ConfigureAwait(false);
        await EnsureOnAsync(session, trigger, cancellationToken).ConfigureAwait(false);
        return (session, null);
    }

    /// <param name="startRequested">Start über die App oder Vorbereitung: ohne laufenden Prozess wartet die Sitzung auf das Spiel.</param>
    private async Task<ActiveSession> StartSessionAsync(
        GameConfig game, SessionTrigger trigger, IReadOnlyList<ProcessInfo> processes, AppConfig cfg, bool startRequested, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var session = new ActiveSession(game.Key, game.Name, trigger, now, game.RestoreAfter)
        {
            Phase = startRequested && processes.Count == 0 ? SessionPhase.Launching : SessionPhase.Running,
        };
        session.ProcessIds = processes.Select(p => p.ProcessId).ToList();
        if (session.Phase == SessionPhase.Running)
        {
            session.FirstProcessAt = now;
        }
        else
        {
            session.LaunchRequestedAt = now;
        }

        _sessions[game.Key] = session;

        // Spiele mit Sitzung müssen zugeordnet werden, auch wenn sie nicht installiert und nicht ausgewählt sind
        // (Start über die App): Zuordnung im nächsten Zyklus neu aufbauen.
        _matcherRevision = -1;
        var first = processes.FirstOrDefault();
        _logger.LogInformation(
            "Sitzung beginnt um {Time}: \"{Game}\" ({Key}), Auslöser {Trigger}{Process}.",
            Stamp(now), game.Name, game.Key, TriggerText(trigger),
            first is null ? string.Empty : $", Prozess {first.ExeName} (PID {first.ProcessId})");

        var displays = SafeGetDisplays();
        var targets = ResolveTargets(game, cfg.Settings, displays);
        if (targets.Count == 0)
        {
            _logger.LogWarning("Kein aktiver Bildschirm gefunden, HDR wird für \"{Game}\" nicht geschaltet.", game.Name);
            Notify(NotificationKind.DisplayNotConnected, CoreStrings.NotifyNoDisplayTitle, Format(CoreStrings.NotifyNoDisplayMessage, game.Name));
        }

        foreach (var target in targets)
        {
            await ClaimAsync(session, target, displays, cancellationToken).ConfigureAwait(false);
        }

        Persist();
        return session;
    }

    private async Task ClaimAsync(ActiveSession session, string devicePath, IReadOnlyList<DisplayInfo> displays, CancellationToken cancellationToken)
    {
        var info = displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (_claims.TryGetValue(devicePath, out var claim))
        {
            claim.Games.Add(session.Key);
            claim.Restore &= session.RestoreAfter;
            AddDisplay(session, devicePath);
            Persist();
            if (info is null || !info.HdrSupported)
            {
                claim.Applied = false;
                return;
            }

            if (!DisplayConfigHdrController.IsInState(info, enable: true))
            {
                var result = await SwitchAsync(devicePath, info.FriendlyName, true, TriggerText(session.Trigger), session, cancellationToken).ConfigureAwait(false);
                claim.Applied = result.IsSuccess;
            }
            else
            {
                claim.Applied = true;
            }

            return;
        }

        if (info is null)
        {
            _logger.LogWarning("Bildschirm {Display} ist nicht angeschlossen. HDR für \"{Game}\" wird nicht geschaltet.", devicePath, session.Name);
            Notify(NotificationKind.DisplayNotConnected, CoreStrings.NotifyNotConnectedTitle, Format(CoreStrings.NotifyNotConnectedMessage, session.Name));
            session.PendingDisplays.Add(devicePath);
            return;
        }

        if (!info.HdrSupported)
        {
            _logger.LogWarning("Bildschirm \"{Display}\" unterstützt derzeit kein HDR. HDR für \"{Game}\" wird nicht geschaltet.", info.FriendlyName, session.Name);
            Notify(NotificationKind.DisplayNotHdrCapable, CoreStrings.NotifyNotCapableTitle, Format(CoreStrings.NotifyNotCapableMessage, session.Name, info.FriendlyName));
            session.PendingDisplays.Add(devicePath);
            return;
        }

        claim = new DisplayClaim(devicePath, info.FriendlyName, info.HdrEnabled, session.RestoreAfter);
        claim.Games.Add(session.Key);
        _claims[devicePath] = claim;
        AddDisplay(session, devicePath);

        // Zuerst den gemerkten Zustand auf die Platte, dann schalten. Scheitert das Speichern (etwa kurz gesperrt),
        // einmal kurz erneut versuchen; scheitert es wieder, trotzdem schalten (das Spiel hat Vorrang), aber melden.
        if (!Persist(out var saveError))
        {
            await _clock.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            if (!Persist(out saveError))
            {
                _logger.LogError("Gemerkter HDR-Zustand für \"{Display}\" ließ sich nicht speichern: {Error}", info.FriendlyName, saveError);
                Notify(NotificationKind.StateNotSaved, CoreStrings.NotifyStateNotSavedTitle, Format(CoreStrings.NotifyStateNotSavedMessage, saveError));
            }
        }

        if (DisplayConfigHdrController.IsInState(info, enable: true))
        {
            claim.Applied = true;
            _logger.LogInformation("HDR auf \"{Display}\" war schon an und bleibt nach \"{Game}\" an.", info.FriendlyName, session.Name);
            return;
        }

        var switched = await SwitchAsync(devicePath, info.FriendlyName, true, TriggerText(session.Trigger), session, cancellationToken).ConfigureAwait(false);
        claim.Applied = switched.IsSuccess;
    }

    /// <summary>Start über die App oder Vorbereitung bei bestehender Sitzung: HDR auf ihren Bildschirmen sicher einschalten.</summary>
    private async Task EnsureOnAsync(ActiveSession session, SessionTrigger trigger, CancellationToken cancellationToken)
    {
        var displays = SafeGetDisplays();
        foreach (var path in session.Displays)
        {
            var info = displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, path, StringComparison.OrdinalIgnoreCase));
            if (info is { HdrSupported: true } && !DisplayConfigHdrController.IsInState(info, enable: true))
            {
                var result = await SwitchAsync(path, info.FriendlyName, true, TriggerText(trigger), session, cancellationToken).ConfigureAwait(false);
                if (_claims.TryGetValue(path, out var claim))
                {
                    claim.Applied = result.IsSuccess;
                }
            }
        }
    }

    /// <summary>Holt für eine Sitzung ausstehende Bildschirme und nicht angewendete Zustände nach.</summary>
    private async Task ReapplyAsync(ActiveSession session, CancellationToken cancellationToken)
    {
        var displays = SafeGetDisplays();
        foreach (var pending in session.PendingDisplays.ToList())
        {
            var info = displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, pending, StringComparison.OrdinalIgnoreCase));
            if (info is { HdrSupported: true })
            {
                session.PendingDisplays.Remove(pending);
                await ClaimAsync(session, pending, displays, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var path in session.Displays)
        {
            if (!_claims.TryGetValue(path, out var claim) || claim.Applied)
            {
                continue;
            }

            var info = displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, path, StringComparison.OrdinalIgnoreCase));
            if (info is { HdrSupported: true })
            {
                if (DisplayConfigHdrController.IsInState(info, enable: true))
                {
                    claim.Applied = true;
                    continue;
                }

                var result = await SwitchAsync(path, info.FriendlyName, true, "Bildschirm wieder verfügbar", session, cancellationToken).ConfigureAwait(false);
                claim.Applied = result.IsSuccess;
            }
        }
    }

    /// <param name="gameMayRun">Das Spiel kann noch unerkannt laufen: Zurückstellen ohne Tastenkürzel-Notlösung.</param>
    private async Task EndSessionAsync(ActiveSession session, string reason, CancellationToken cancellationToken, bool gameMayRun = false)
    {
        _sessions.Remove(session.Key);
        _logger.LogInformation("Sitzung endet um {Time}: \"{Game}\" ({Reason}).", Stamp(_clock.UtcNow), session.Name, reason);
        foreach (var path in session.Displays)
        {
            if (_claims.TryGetValue(path, out var claim))
            {
                claim.Games.Remove(session.Key);
                if (claim.Games.Count == 0)
                {
                    await ReleaseAsync(claim, $"Spielende \"{session.Name}\" ({reason})", session, cancellationToken, gameMayRun).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation(
                        "HDR auf \"{Display}\" bleibt an: noch {Count} weitere(s) Spiel(e) aktiv.", claim.FriendlyName, claim.Games.Count);
                }
            }
        }

        Persist(); // „zuletzt verwendet“ schreibt RecordPlayed schon beim Erkennen des Spiels
    }

    private async Task ReleaseAsync(DisplayClaim claim, string trigger, ActiveSession? session, CancellationToken cancellationToken, bool gameMayRun = false)
    {
        if (!claim.Restore)
        {
            _logger.LogInformation("\"{Display}\": Zurückstellen ist für das Spiel ausgeschaltet, HDR bleibt wie es ist.", claim.FriendlyName);
            _claims.Remove(claim.DevicePath);
            return;
        }

        if (claim.WasHdrOn)
        {
            _logger.LogInformation("\"{Display}\": HDR war vor dem Spiel an und bleibt an.", claim.FriendlyName);
            _claims.Remove(claim.DevicePath);
            return;
        }

        var result = await SwitchAsync(claim.DevicePath, claim.FriendlyName, false, trigger, session, cancellationToken, gameMayRun).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _claims.Remove(claim.DevicePath);
        }
        else
        {
            claim.Applied = false;
            _logger.LogWarning(
                "\"{Display}\": Zurückstellen gerade nicht möglich ({Status}). Neuer Versuch bei der nächsten Bildschirmänderung oder beim nächsten Start.",
                claim.FriendlyName, result.Status);
        }
    }

    private async Task RecoverCoreAsync(Dictionary<string, List<ProcessInfo>> running, AppConfig cfg, CancellationToken cancellationToken)
    {
        var data = _store.Load();
        if (data is null || data.Displays.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Gemerkter HDR-Zustand für {Count} Bildschirm(e) gefunden (letzter Lauf endete nicht sauber). Prüfe laufende Spiele.", data.Displays.Count);
        var displays = SafeGetDisplays();
        foreach (var state in data.Displays)
        {
            var claim = new DisplayClaim(state.MonitorDevicePath, state.FriendlyName, state.WasHdrOn, state.Restore) { Applied = true };
            _claims[state.MonitorDevicePath] = claim;

            foreach (var (key, processes) in running)
            {
                var game = cfg.FindGame(key);
                if (game is null)
                {
                    continue;
                }

                var wanted = state.Games.Contains(key, StringComparer.OrdinalIgnoreCase)
                    || (game.HdrAuto && ResolveTargets(game, cfg.Settings, displays).Contains(state.MonitorDevicePath, StringComparer.OrdinalIgnoreCase));
                if (!wanted)
                {
                    continue;
                }

                if (!_sessions.TryGetValue(key, out var session))
                {
                    session = new ActiveSession(key, game.Name, SessionTrigger.Recovery, _clock.UtcNow, game.RestoreAfter)
                    {
                        Phase = SessionPhase.Running,
                        FirstProcessAt = _clock.UtcNow,
                        ProcessIds = processes.Select(p => p.ProcessId).ToList(),
                    };
                    _sessions[key] = session;
                    _matcherRevision = -1;
                    _logger.LogInformation("\"{Game}\" läuft noch: Sitzung wird übernommen.", game.Name);
                }

                AddDisplay(session, state.MonitorDevicePath);
                claim.Games.Add(key);
                claim.Restore &= game.RestoreAfter;
            }

            if (claim.Games.Count == 0)
            {
                _logger.LogInformation("Kein ausgewähltes Spiel läuft mehr: stelle den gemerkten Zustand von \"{Display}\" wieder her.", claim.FriendlyName);
                await ReleaseAsync(claim, "Wiederanlauf nach Absturz", null, cancellationToken).ConfigureAwait(false);
            }
        }

        Persist();
    }

    private void Touch(ActiveSession session, List<ProcessInfo> processes, DateTimeOffset now)
    {
        session.ProcessIds = processes.Select(p => p.ProcessId).ToList();
        if (session.Phase == SessionPhase.Launching)
        {
            session.Phase = SessionPhase.Running;
            session.FirstProcessAt = now;
            var first = processes[0];
            _logger.LogInformation(
                "Spielprozess erkannt um {Time}: {Exe} (PID {Pid}). HDR-Schritt war abgeschlossen um {HdrTime}.",
                Stamp(now), first.ExeName, first.ProcessId, session.HdrConfirmedAt is { } t ? Stamp(t) : "unbekannt");
        }
        else if (session.Phase == SessionPhase.Ending)
        {
            session.Phase = SessionPhase.Running;
            _logger.LogInformation(
                "\"{Game}\" läuft wieder, {Wait} abgebrochen.", session.Name, session.BufferUntil is null ? "Nachlaufzeit" : "Neustart-Puffer");
            ClearEnding(session);
        }
    }

    /// <summary>
    /// Kein Spielprozess mehr: normalerweise beginnt die Nachlaufzeit. War die Sitzung kurz und hat HdrPilot HDR für sie
    /// eingeschaltet (vorher aus, Zurückstellen an), gilt stattdessen der längere Neustart-Puffer. Anlass: Dead Island 2
    /// hing gleich nach dem Umschalten, der Nutzer beendete es und startete 96 s nach dem Zurückstellen neu. Startet das
    /// Spiel im Puffer wieder, läuft die Sitzung einfach weiter (<see cref="Touch"/>) und HDR bleibt an.
    /// </summary>
    /// <summary>Neustart-Puffer aus der Konfiguration (restartBufferSeconds, 0 = aus), sonst der Standard aus den Optionen.</summary>
    private TimeSpan RestartBufferFor(AppConfig cfg) =>
        cfg.Settings.RestartBufferSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 1800)) : _options.RestartBuffer;

    private void BeginEnding(ActiveSession session, TimeSpan grace, TimeSpan restartBuffer, DateTimeOffset now)
    {
        var ran = now - (session.FirstProcessAt ?? now);
        var shortSession = restartBuffer > TimeSpan.Zero
            && session.FirstProcessAt is not null
            && ran < _options.ShortSessionLimit
            && session.Displays.Any(path => _claims.TryGetValue(path, out var claim) && !claim.WasHdrOn && claim.Restore);
        if (!shortSession)
        {
            _logger.LogInformation("Kein Prozess von \"{Game}\" mehr. Nachlaufzeit {Grace} s beginnt.", session.Name, grace.TotalSeconds);
            return;
        }

        var wait = grace > restartBuffer ? grace : restartBuffer;
        session.BufferUntil = now + wait;
        _logger.LogInformation(
            "Kein Prozess von \"{Game}\" mehr, kurze Sitzung ({Ran:0} s): Neustart-Puffer {Buffer:0} s, HDR bleibt so lange an.",
            session.Name, ran.TotalSeconds, wait.TotalSeconds);
    }

    /// <summary>
    /// Neustart-Puffer abgelaufen. Läuft eine Vollbild-Anwendung im Vordergrund (etwa ein anderes Spiel, das gerade
    /// startet), wartet das Zurückstellen, bis sie weg ist (geprüft bei jedem Zyklus), höchstens
    /// <see cref="SessionManagerOptions.MaxFullscreenHold"/> lang. True: weiter warten.
    /// </summary>
    private bool HoldForFullscreen(ActiveSession session, DateTimeOffset now)
    {
        if (!IsFullscreenAppRunning())
        {
            if (session.FullscreenHoldSince is { } heldSince)
            {
                _logger.LogInformation(
                    "\"{Game}\": Vollbild-Anwendung nach {Seconds:0} s beendet, HDR wird zurückgestellt.", session.Name, (now - heldSince).TotalSeconds);
            }

            return false;
        }

        if (session.FullscreenHoldSince is not { } since)
        {
            session.FullscreenHoldSince = now;
            _logger.LogInformation(
                "\"{Game}\": Neustart-Puffer abgelaufen, aber eine Vollbild-Anwendung läuft. Zurückstellen wartet, bis sie beendet ist (höchstens {Max} min).",
                session.Name, _options.MaxFullscreenHold.TotalMinutes);
            return true;
        }

        if (now - since < _options.MaxFullscreenHold)
        {
            return true;
        }

        _logger.LogInformation(
            "\"{Game}\": Vollbild-Anwendung läuft seit {Max} min, HDR wird trotzdem zurückgestellt.", session.Name, _options.MaxFullscreenHold.TotalMinutes);
        return false;
    }

    private bool IsFullscreenAppRunning()
    {
        try
        {
            return _fullscreenAppRunning();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abfrage „Vollbild-Anwendung läuft“ fehlgeschlagen.");
            return false;
        }
    }

    /// <summary>Nachlaufzeit, Neustart-Puffer und Warten auf eine Vollbild-Anwendung beenden (das Spiel ist wieder da).</summary>
    private static void ClearEnding(ActiveSession session)
    {
        session.MissingSince = null;
        session.BufferUntil = null;
        session.FullscreenHoldSince = null;
    }

    /// <summary>
    /// Hinweis „Spiel hängt im Hintergrund“. Anlass: Palworld lief nach dem Schließen unsichtbar weiter (kein Fenster,
    /// keine CPU), HdrPilot ließ HDR richtigerweise an, der Nutzer wusste aber nicht warum. Nur solange eine Sitzung läuft,
    /// alle <see cref="SessionManagerOptions.HangCheckInterval"/>: eine Abfrage der CPU-Zeiten aller Prozesse (ohne Handle),
    /// Fenster nur für Sitzungen ohne CPU-Zuwachs. Hatten alle Prozesse einer Sitzung mindestens
    /// <see cref="SessionManagerOptions.HangThreshold"/> lang kein sichtbares Fenster und keine zusätzliche CPU-Zeit, kommt
    /// ein Hinweis, höchstens einmal je Sitzung. Nichts wird beendet oder zurückgestellt. Minimierte Fenster zählen als
    /// sichtbar, angehaltene Xbox-Spiele lösen also keinen Hinweis aus.
    /// </summary>
    private void CheckForHangs(DateTimeOffset now)
    {
        if (_sessions.Count == 0 && _lastHangCheck is null)
        {
            return; // Leerlauf ohne Spiel: nichts zu tun, nichts anzulegen
        }

        try
        {
            var running = _sessions.Values.Where(s => s.Phase == SessionPhase.Running && s.ProcessIds.Count > 0).ToList();
            if (running.Count == 0)
            {
                if (_lastHangCheck is not null)
                {
                    // Keine Sitzung läuft mehr: nichts mehr prüfen und den Abfragepuffer freigeben.
                    _lastHangCheck = null;
                    foreach (var s in _sessions.Values)
                    {
                        s.ResetActivity();
                    }

                    _activity.ReleaseBuffers();
                }

                return;
            }

            if (_lastHangCheck is { } last && now - last < _options.HangCheckInterval)
            {
                return;
            }

            var previousCheck = _lastHangCheck;
            _lastHangCheck = now;
            foreach (var s in _sessions.Values.Where(s => s.Phase != SessionPhase.Running))
            {
                s.ResetActivity();
            }

            var cpu = _activity.SampleCpu(running.SelectMany(s => s.ProcessIds).ToHashSet());
            var cpuIdle = new List<ActiveSession>();
            foreach (var session in running)
            {
                // Ruhig heißt: jeder Prozess war schon bei der letzten Prüfung da (gleiche Startzeit) und hat seitdem
                // keine CPU-Zeit verbraucht. Neue, verschwundene oder nicht lesbare Prozesse zählen als aktiv.
                var idle = previousCheck is not null;
                var samples = new Dictionary<int, ProcessCpuSample>(session.ProcessIds.Count);
                foreach (var pid in session.ProcessIds)
                {
                    if (!cpu.TryGetValue(pid, out var sample))
                    {
                        idle = false;
                        continue;
                    }

                    samples[pid] = sample;
                    if (!session.CpuSamples.TryGetValue(pid, out var before) || before.CreateTime != sample.CreateTime || before.CpuTime != sample.CpuTime)
                    {
                        idle = false;
                    }
                }

                session.CpuSamples = samples;
                if (idle)
                {
                    cpuIdle.Add(session);
                }
            }

            // Fenster nur, wenn ein Spiel keine CPU-Zeit verbraucht hat (im Normalfall gar nicht).
            var withWindow = cpuIdle.Count == 0
                ? null
                : _activity.WithVisibleWindow(cpuIdle.SelectMany(s => s.ProcessIds).ToHashSet());
            foreach (var session in running)
            {
                var idle = withWindow is not null && cpuIdle.Contains(session) && !session.ProcessIds.Any(withWindow.Contains);
                UpdateHangState(session, idle, now);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prüfung auf hängende Spiele fehlgeschlagen.");
        }
    }

    /// <summary>
    /// Ruhig seit der ersten Prüfung, bei der alles ruhig war (Fenster sind nur Stichproben): der Hinweis kommt erst,
    /// wenn das über mindestens <see cref="SessionManagerOptions.HangThreshold"/> beobachtet wurde.
    /// </summary>
    private void UpdateHangState(ActiveSession session, bool idle, DateTimeOffset now)
    {
        if (!idle)
        {
            if (session.IdleSince is not null && session.HangHintShown)
            {
                _logger.LogInformation("\"{Game}\" ist wieder aktiv (Fenster oder CPU-Zeit).", session.Name);
            }

            session.IdleSince = null;
            return;
        }

        session.IdleSince ??= now;
        var idleFor = now - session.IdleSince.Value;
        if (session.HangHintShown || idleFor < _options.HangThreshold)
        {
            return;
        }

        // Während einer Vollbild-Anwendung zeigt das Tray keine Hinweise (nur Protokoll): dann bei einer späteren
        // Prüfung nachholen, sonst ginge der einzige Hinweis der Sitzung verloren.
        if (IsFullscreenAppRunning())
        {
            _logger.LogDebug("\"{Game}\" scheint zu hängen; der Hinweis wartet, bis keine Vollbild-Anwendung mehr läuft.", session.Name);
            return;
        }

        session.HangHintShown = true;
        _logger.LogInformation(
            "\"{Game}\" scheint im Hintergrund zu hängen: seit {Seconds:0} s kein sichtbares Fenster und keine CPU-Zeit (PID {Pids}). HDR bleibt an bis zum Spielende, Hinweis angezeigt.",
            session.Name, idleFor.TotalSeconds, string.Join(", ", session.ProcessIds));
        Notify(NotificationKind.GameSeemsHung, CoreStrings.NotifyGameSeemsHungTitle, Format(CoreStrings.NotifyGameSeemsHungMessage, session.Name));
    }

    private void Learn(Dictionary<string, List<ProcessInfo>> running, AppConfig cfg, DateTimeOffset now)
    {
        // Der "erste erkannte Lauf" endet wie eine Sitzung erst nach der Nachlaufzeit. So werden bei Starter-Ketten
        // (Starter beendet sich, kurz danach startet das Spiel) auch die eigentlichen Spiel-EXEs gemerkt.
        var grace = TimeSpan.FromSeconds(cfg.Settings.GracePeriodSeconds);
        foreach (var (key, state) in _learning.ToList())
        {
            if (running.ContainsKey(key))
            {
                state.MissingSince = null;
            }
            else if (state.MissingSince is null)
            {
                state.MissingSince = now;
            }
            else if (now - state.MissingSince >= grace)
            {
                _learning.Remove(key);
            }
        }

        foreach (var (key, processes) in running)
        {
            if (!_learning.TryGetValue(key, out var state))
            {
                var game = cfg.FindGame(key);
                if (game is null || game.DetectedExecutables.Count > 0)
                {
                    continue;
                }

                state = new LearnState();
                _learning[key] = state;
            }

            var seen = state.Executables;

            var added = false;
            foreach (var p in processes)
            {
                if (p.ExePath is not null && seen.Add(PathUtil.Normalize(p.ExePath)))
                {
                    added = true;
                }
            }

            if (added)
            {
                _config.RecordDetectedExecutables(key, seen.ToList());
                _logger.LogInformation("Spiel-EXEs für {Key} gemerkt: {Exes}", key, string.Join(", ", seen));
            }
        }
    }

    /// <summary>
    /// „Zuletzt verwendet“ für jedes zugeordnete Spiel der Bibliothek, nicht nur für Spiele mit HDR-Sitzung: einmal je Lauf,
    /// beim ersten Erkennen. Ein Lauf endet wie eine Sitzung erst nach der Nachlaufzeit ohne Prozess; Starter-Ketten und
    /// Neustarts des Spiels schreiben also nicht erneut (die Konfiguration wird selten geschrieben).
    /// </summary>
    private void RecordPlayed(Dictionary<string, List<ProcessInfo>> running, AppConfig cfg, DateTimeOffset now)
    {
        var grace = TimeSpan.FromSeconds(cfg.Settings.GracePeriodSeconds);
        foreach (var (key, missingSince) in _playing.ToList())
        {
            if (running.ContainsKey(key))
            {
                _playing[key] = null;
            }
            else if (missingSince is null)
            {
                _playing[key] = now;
            }
            else if (now - missingSince >= grace)
            {
                _playing.Remove(key);
            }
        }

        foreach (var key in running.Keys)
        {
            if (_playing.ContainsKey(key) || cfg.FindGame(key) is null)
            {
                continue;
            }

            _playing[key] = null;
            _config.RecordLastPlayed(key, now);
            _logger.LogDebug("Zuletzt verwendet für {Key}: {Time}.", key, Stamp(now));
        }
    }

    private void RefreshMatches(ProcessChanges changes, AppConfig cfg, long revision)
    {
        if (_matcher is null || revision != _matcherRevision)
        {
            var patterns = cfg.Settings.HelperExecutablePatterns;
            // Ausgewählte Spiele zählen auch, wenn sie beim letzten Einlesen fehlten (etwa mitten in einem Steam-Update),
            // ebenso Spiele aus dem gemerkten Zustand eines abgebrochenen Laufs (sonst stellte der Wiederanlauf mitten
            // im Spiel zurück).
            _matcher = new GameMatcher(
                cfg.Games.Where(g => g.IsInstalled || g.HdrAuto || _sessions.ContainsKey(g.Key) || _recoveryKeys.Contains(g.Key)),
                name => _isHelper(name, patterns),
                _forbiddenRoots);
            _anyGameMatcher = new GameMatcher(cfg.Games, name => _isHelper(name, patterns), _forbiddenRoots);
            _matcherRevision = revision;
            _pidMatches.Clear();
            foreach (var p in changes.Current)
            {
                _pidMatches[p.ProcessId] = MatchProcess(p);
            }

            return;
        }

        foreach (var p in changes.Exited)
        {
            _pidMatches.Remove(p.ProcessId);
        }

        foreach (var p in changes.Started)
        {
            _pidMatches[p.ProcessId] = MatchProcess(p);
        }

        if (_pidMatches.Count != changes.Current.Count)
        {
            // Abgleich, falls Zyklen fehlten: fehlende ergänzen, veraltete entfernen.
            var current = changes.Current.ToDictionary(p => p.ProcessId);
            foreach (var pid in _pidMatches.Keys.Where(k => !current.ContainsKey(k)).ToList())
            {
                _pidMatches.Remove(pid);
            }

            foreach (var p in changes.Current.Where(p => !_pidMatches.ContainsKey(p.ProcessId)))
            {
                _pidMatches[p.ProcessId] = MatchProcess(p);
            }
        }
    }

    private MatchedProcess MatchProcess(ProcessInfo p)
    {
        var key = _matcher!.Match(p)?.Key;
        return new MatchedProcess(p, key, key is not null || _anyGameMatcher!.Match(p) is not null);
    }

    /// <summary>Spiele aus dem gemerkten Zustand vor dem ersten Zuordnen laden, damit der Wiederanlauf sie übernimmt.</summary>
    private void PrepareRecovery()
    {
        var keys = _store.Load()?.Displays.SelectMany(d => d.Games) ?? [];
        _recoveryKeys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (_recoveryKeys.Count > 0)
        {
            _matcherRevision = -1;
        }
    }

    private void FinishRecovery()
    {
        if (_recoveryKeys.Count > 0)
        {
            _recoveryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _matcherRevision = -1; // übernommene Spiele bleiben über ihre Sitzung in der Zuordnung
        }
    }

    /// <summary>
    /// Die Tastenkürzel-Notlösung nur, solange kein Spiel läuft (E66): künstliche Tastendrücke an ein laufendes Spiel
    /// sind genau das, wogegen Anti-Cheat-Systeme vorgehen. Als laufend gilt jeder Prozess eines Spiels der Bibliothek
    /// (ausgewählt oder nicht) und jedes über HdrPilot gestartete Spiel, dessen Prozess noch nicht erkannt ist.
    /// Erlaubt bleibt sie vor einem Start über die App (das Spiel läuft noch nicht) und beim Zurückstellen nach dem
    /// letzten Spiel.
    /// </summary>
    private bool ComputeHotkeyFallbackAllowed() =>
        !_pidMatches.Values.Any(m => m.IsAnyGame)
        && !_sessions.Values.Any(s => s.Phase == SessionPhase.Launching && s.LaunchCommandIssued);

    private Dictionary<string, List<ProcessInfo>> RunningGames()
    {
        var result = new Dictionary<string, List<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _pidMatches.Values)
        {
            if (m.GameKey is null)
            {
                continue;
            }

            if (!result.TryGetValue(m.GameKey, out var list))
            {
                list = [];
                result[m.GameKey] = list;
            }

            list.Add(m.Process);
        }

        return result;
    }

    private static List<string> ResolveTargets(GameConfig game, AppSettings settings, IReadOnlyList<DisplayInfo> displays)
    {
        if (!string.IsNullOrWhiteSpace(game.Display))
        {
            return [game.Display];
        }

        if (settings.TargetDisplays.Count > 0)
        {
            return settings.TargetDisplays.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        return primary is null ? [] : [primary.MonitorDevicePath];
    }

    private async Task<HdrSetResult> SwitchAsync(
        string devicePath, string displayName, bool enable, string trigger, ActiveSession? session, CancellationToken cancellationToken,
        bool gameMayRun = false)
    {
        var sw = Stopwatch.StartNew();
        HdrSetResult result;
        try
        {
            // Beim Launcher-Signal startet das Spiel ohnehin 1-2 s später, unabhängig von HdrPilot: keine Notlösung (E66).
            var launcherStartsGame = enable && session?.Trigger == SessionTrigger.LauncherSignal;
            var allowHotkey = !gameMayRun && !launcherStartsGame && ComputeHotkeyFallbackAllowed();
            result = await _hdr.SetHdrAsync(devicePath, enable, allowHotkey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new HdrSetResult
            {
                Status = HdrSetStatus.Failed,
                MonitorDevicePath = devicePath,
                FriendlyName = displayName,
                Message = Format(CoreStrings.HdrSwitchException, ex.Message),
            };
        }

        var level = result.IsSuccess ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(
            level,
            "HDR-Schaltung um {Time} | Auslöser: {Trigger} | Spiel: {Game} | Bildschirm: {Display} | vorher: {Before} | nachher: {After} | Ergebnis: {Status} | {Message} | {Ms} ms",
            Stamp(_clock.UtcNow), trigger, session is null ? "-" : $"{session.Name} ({session.Key})", displayName,
            OnOff(result.Before), OnOff(result.After), result.Status, result.Message, sw.ElapsedMilliseconds);

        var game = session?.Name ?? "HDRpilot";
        switch (result.Status)
        {
            case HdrSetStatus.Success when enable:
                Notify(NotificationKind.HdrEnabled, CoreStrings.NotifyHdrEnabledTitle, Format(CoreStrings.NotifyHdrEnabledMessage, game, displayName));
                break;
            case HdrSetStatus.Success:
                Notify(NotificationKind.HdrRestored, CoreStrings.NotifyHdrRestoredTitle, Format(CoreStrings.NotifyHdrRestoredMessage, displayName));
                break;
            case HdrSetStatus.NotHdrCapable:
                Notify(NotificationKind.DisplayNotHdrCapable, CoreStrings.NotifyNotCapableTitle, result.Message);
                break;
            case HdrSetStatus.NotConnected:
                Notify(NotificationKind.DisplayNotConnected, CoreStrings.NotifyNotConnectedTitle, result.Message);
                break;
            case HdrSetStatus.Failed:
                Notify(NotificationKind.SwitchFailed, CoreStrings.NotifySwitchFailedTitle, result.Message);
                break;
        }

        return result;
    }

    private IReadOnlyList<DisplayInfo> SafeGetDisplays()
    {
        try
        {
            return _hdr.GetDisplays();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bildschirme ließen sich nicht abfragen.");
            return [];
        }
    }

    private void Persist() => Persist(out _);

    /// <summary>Schreibt den gemerkten Zustand sofort. False mit Fehlertext, wenn das Speichern scheiterte.</summary>
    private bool Persist(out string? error)
    {
        error = null;
        try
        {
            if (_claims.Count == 0)
            {
                _store.Delete();
                return true;
            }

            var now = _clock.UtcNow;
            _store.Save(new SessionStateData
            {
                Displays = _claims.Values.Select(c => new DisplayClaimState
                {
                    MonitorDevicePath = c.DevicePath,
                    FriendlyName = c.FriendlyName,
                    WasHdrOn = c.WasHdrOn,
                    Restore = c.Restore,
                    Games = [.. c.Games],
                    SavedAt = now,
                }).ToList(),
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Gemerkter HDR-Zustand ließ sich nicht speichern.");
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Veröffentlicht den Stand unter Sperre; das Ereignis wird erst nach Freigabe der Sperre ausgelöst.</summary>
    private bool PublishState()
    {
        var snapshot = _sessions.Values
            .Select(s => new SessionInfo(
                s.Key, s.Name, s.Phase, s.Trigger, s.StartedAt, [.. s.Displays], [.. s.ProcessIds], s.BufferUntil, s.FullscreenHoldSince is not null))
            .OrderBy(s => s.StartedAt)
            .ToList();
        var previous = _publicSessions;
        _publicSessions = snapshot;
        _hotkeyFallbackAllowed = ComputeHotkeyFallbackAllowed();
        return !previous.SequenceEqual(snapshot, SessionInfoComparer.Instance);
    }

    private void RaiseStateChanged(bool changed)
    {
        if (!changed)
        {
            return;
        }

        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler in einem StateChanged-Empfänger.");
        }
    }

    private static void AddDisplay(ActiveSession session, string devicePath)
    {
        if (!session.Displays.Contains(devicePath, StringComparer.OrdinalIgnoreCase))
        {
            session.Displays.Add(devicePath);
        }
    }

    private void Notify(NotificationKind kind, string title, string message)
    {
        try
        {
            Notification?.Invoke(this, new SessionNotification(kind, title, message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Benachrichtigung fehlgeschlagen.");
        }
    }

    private static string TriggerText(SessionTrigger trigger) => trigger switch
    {
        SessionTrigger.ProcessDetected => "Spielstart erkannt",
        SessionTrigger.LaunchedViaApp => "Start über die App",
        SessionTrigger.Recovery => "Wiederanlauf",
        SessionTrigger.SteamLaunchOption => "Start über Steam-Startoption",
        SessionTrigger.LauncherSignal => "Launcher-Signal (Epic/Steam)",
        _ => trigger.ToString(),
    };

    private static string OnOff(bool? value) => value switch
    {
        true => "an",
        false => "aus",
        null => "unbekannt",
    };

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string Stamp(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <param name="GameKey">Zugeordnetes Spiel (installiert, ausgewählt oder mit Sitzung), sonst null.</param>
    /// <param name="IsAnyGame">Gehört zu irgendeinem Spiel der Bibliothek (nur für die Sperre der Notlösung).</param>
    private sealed record MatchedProcess(ProcessInfo Process, string? GameKey, bool IsAnyGame);

    private sealed class LearnState
    {
        public HashSet<string> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset? MissingSince { get; set; }
    }

    private sealed class ActiveSession(string key, string name, SessionTrigger trigger, DateTimeOffset startedAt, bool restoreAfter)
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        public SessionTrigger Trigger { get; } = trigger;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public bool RestoreAfter { get; } = restoreAfter;

        public SessionPhase Phase { get; set; }

        public DateTimeOffset? MissingSince { get; set; }

        /// <summary>Ende des Neustart-Puffers nach einer kurzen Sitzung (gilt statt der Nachlaufzeit), sonst null.</summary>
        public DateTimeOffset? BufferUntil { get; set; }

        /// <summary>Seit wann das Zurückstellen nach dem Neustart-Puffer auf das Ende einer Vollbild-Anwendung wartet.</summary>
        public DateTimeOffset? FullscreenHoldSince { get; set; }

        /// <summary>CPU-Zeit je Prozess bei der letzten Prüfung auf Hänger.</summary>
        public Dictionary<int, ProcessCpuSample> CpuSamples { get; set; } = [];

        /// <summary>Seit wann alle Prozesse ohne sichtbares Fenster und ohne CPU-Zeit sind (null: aktiv oder unbekannt).</summary>
        public DateTimeOffset? IdleSince { get; set; }

        /// <summary>Hinweis „Spiel hängt“ schon gezeigt (höchstens einmal je Sitzung).</summary>
        public bool HangHintShown { get; set; }

        /// <summary>Zeitpunkt des (letzten) Starts über die App; ab hier zählt die Startfrist.</summary>
        public DateTimeOffset? LaunchRequestedAt { get; set; }

        public DateTimeOffset? HdrConfirmedAt { get; set; }

        /// <summary>Der Start-Befehl wurde ausgeführt: das Spiel kann laufen, auch wenn sein Prozess noch nicht erkannt ist.</summary>
        public bool LaunchCommandIssued { get; set; }

        public bool WaitingForLauncherLogged { get; set; }

        public DateTimeOffset? FirstProcessAt { get; set; }

        public List<int> ProcessIds { get; set; } = [];

        public List<string> Displays { get; } = [];

        public List<string> PendingDisplays { get; } = [];

        /// <summary>Beobachtung für den Hinweis „Spiel hängt“ verwerfen (Sitzung läuft gerade nicht).</summary>
        public void ResetActivity()
        {
            CpuSamples.Clear();
            IdleSince = null;
        }
    }

    private sealed class DisplayClaim(string devicePath, string friendlyName, bool wasHdrOn, bool restore)
    {
        public string DevicePath { get; } = devicePath;

        public string FriendlyName { get; } = friendlyName;

        public bool WasHdrOn { get; } = wasHdrOn;

        public bool Restore { get; set; } = restore;

        public bool Applied { get; set; }

        public HashSet<string> Games { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SessionInfoComparer : IEqualityComparer<SessionInfo>
    {
        public static SessionInfoComparer Instance { get; } = new();

        public bool Equals(SessionInfo? x, SessionInfo? y) =>
            x is not null && y is not null && x.GameKey == y.GameKey && x.Phase == y.Phase
            && x.Displays.SequenceEqual(y.Displays) && x.ProcessIds.SequenceEqual(y.ProcessIds)
            && x.BufferUntil == y.BufferUntil && x.HeldForFullscreen == y.HeldForFullscreen;

        public int GetHashCode(SessionInfo obj) => obj.GameKey.GetHashCode(StringComparison.Ordinal);
    }
}
