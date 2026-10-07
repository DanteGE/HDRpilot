using System.Diagnostics;
using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Resources;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Sessions;

/// <summary>
/// Sitzungslogik: erkennt Spielstart und -ende, merkt sich vor dem Einschalten den HDR-Zustand jedes betroffenen
/// Bildschirms (sofort auf der Platte), schaltet erst nach dem Ende des letzten Spiels zurück und stellt nach einem
/// Absturz beim nächsten Start den gemerkten Zustand wieder her. Alle Abläufe sind serialisiert.
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
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly Dictionary<string, ActiveSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DisplayClaim> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MatchedProcess> _pidMatches = [];
    private readonly Dictionary<string, LearnState> _learning = new(StringComparer.OrdinalIgnoreCase);
    private GameMatcher? _matcher;
    private long _matcherRevision = -1;
    private bool _recovered;
    private volatile IReadOnlyList<SessionInfo> _publicSessions = [];
    private volatile bool _paused;

    /// <param name="isHelper">Prüft einen EXE-Namen gegen die Ausschlussliste (Muster aus der Konfiguration, null = Standard).</param>
    /// <param name="forbiddenRoots">Ordner, die nie als Installationsordner gelten (z. B. Windows, Programme).</param>
    public SessionManager(
        IHdrController hdr,
        ISessionConfigSource config,
        ISessionStateStore store,
        IGameLauncher launcher,
        IClock clock,
        Func<string, IReadOnlyList<string>?, bool> isHelper,
        IReadOnlyList<string>? forbiddenRoots = null,
        ILogger? logger = null)
    {
        _hdr = hdr;
        _config = config;
        _store = store;
        _launcher = launcher;
        _clock = clock;
        _isHelper = isHelper;
        _forbiddenRoots = forbiddenRoots ?? [];
        _logger = logger ?? NullLogger.Instance;
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
            RefreshMatches(snapshot, cfg, revision);
            _recovered = true;
            await RecoverCoreAsync(RunningGames(), cfg, cancellationToken).ConfigureAwait(false);
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
            RefreshMatches(changes, cfg, revision);
            var running = RunningGames();

            if (!_recovered)
            {
                _recovered = true;
                await RecoverCoreAsync(running, cfg, cancellationToken).ConfigureAwait(false);
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

                await StartSessionAsync(game, SessionTrigger.ProcessDetected, processes, cfg, cancellationToken).ConfigureAwait(false);
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
                    if (now - (session.LaunchRequestedAt ?? session.StartedAt) >= launchTimeout)
                    {
                        await EndSessionAsync(session, $"kein Spielprozess innerhalb von {launchTimeout.TotalSeconds:0} s nach dem Start", cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (session.MissingSince is null)
                {
                    session.MissingSince = now;
                    session.Phase = SessionPhase.Ending;
                    _logger.LogInformation(
                        "Kein Prozess von \"{Game}\" mehr. Nachlaufzeit {Grace} s beginnt.", session.Name, grace.TotalSeconds);
                }

                if (now - session.MissingSince >= grace)
                {
                    await EndSessionAsync(session, "Spielende", cancellationToken).ConfigureAwait(false);
                }
            }
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

            var processes = RunningGames().GetValueOrDefault(game.Key) ?? [];
            if (!_sessions.TryGetValue(game.Key, out var session))
            {
                session = await StartSessionAsync(game, SessionTrigger.LaunchedViaApp, processes, cfg, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (processes.Count == 0)
                {
                    // Erneuter Start (etwa während der Nachlaufzeit oder nach einem Neustart des Spiels):
                    // Sitzung wieder auf "wartet auf Spielprozess" stellen, Startfrist neu beginnen.
                    session.Phase = SessionPhase.Launching;
                    session.MissingSince = null;
                    session.LaunchRequestedAt = _clock.UtcNow;
                    _logger.LogInformation("Erneuter Start über die App für \"{Game}\": Sitzung wartet wieder auf den Spielprozess.", game.Name);
                }

                await ReapplyAsync(session, cancellationToken).ConfigureAwait(false);
                await EnsureOnAsync(session, cancellationToken).ConfigureAwait(false);
            }

            session.HdrConfirmedAt = _clock.UtcNow;
            _logger.LogInformation(
                "Start über die App: HDR-Schritt für \"{Game}\" abgeschlossen um {Time}. Starte das Spiel.", game.Name, Stamp(session.HdrConfirmedAt.Value));

            var launchedAt = _clock.UtcNow;
            var result = _launcher.Launch(game);
            if (result.Success)
            {
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

    private async Task<ActiveSession> StartSessionAsync(
        GameConfig game, SessionTrigger trigger, IReadOnlyList<ProcessInfo> processes, AppConfig cfg, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var session = new ActiveSession(game.Key, game.Name, trigger, now, game.RestoreAfter)
        {
            Phase = trigger == SessionTrigger.LaunchedViaApp && processes.Count == 0 ? SessionPhase.Launching : SessionPhase.Running,
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

    /// <summary>Start über die App bei bestehender Sitzung: HDR auf ihren Bildschirmen sicher einschalten.</summary>
    private async Task EnsureOnAsync(ActiveSession session, CancellationToken cancellationToken)
    {
        var displays = SafeGetDisplays();
        foreach (var path in session.Displays)
        {
            var info = displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, path, StringComparison.OrdinalIgnoreCase));
            if (info is { HdrSupported: true } && !DisplayConfigHdrController.IsInState(info, enable: true))
            {
                var result = await SwitchAsync(path, info.FriendlyName, true, TriggerText(SessionTrigger.LaunchedViaApp), session, cancellationToken).ConfigureAwait(false);
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

    private async Task EndSessionAsync(ActiveSession session, string reason, CancellationToken cancellationToken)
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
                    await ReleaseAsync(claim, $"Spielende \"{session.Name}\" ({reason})", session, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation(
                        "HDR auf \"{Display}\" bleibt an: noch {Count} weitere(s) Spiel(e) aktiv.", claim.FriendlyName, claim.Games.Count);
                }
            }
        }

        Persist();
        if (session.FirstProcessAt is not null)
        {
            _config.RecordLastPlayed(session.Key, _clock.UtcNow);
        }
    }

    private async Task ReleaseAsync(DisplayClaim claim, string trigger, ActiveSession? session, CancellationToken cancellationToken)
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

        var result = await SwitchAsync(claim.DevicePath, claim.FriendlyName, false, trigger, session, cancellationToken).ConfigureAwait(false);
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
            session.MissingSince = null;
            _logger.LogInformation("\"{Game}\" läuft wieder, Nachlaufzeit abgebrochen.", session.Name);
        }
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

    private void RefreshMatches(ProcessChanges changes, AppConfig cfg, long revision)
    {
        if (_matcher is null || revision != _matcherRevision)
        {
            var patterns = cfg.Settings.HelperExecutablePatterns;
            // Ausgewählte Spiele zählen auch, wenn sie beim letzten Einlesen fehlten (etwa mitten in einem Steam-Update).
            _matcher = new GameMatcher(
                cfg.Games.Where(g => g.IsInstalled || g.HdrAuto || _sessions.ContainsKey(g.Key)),
                name => _isHelper(name, patterns),
                _forbiddenRoots);
            _matcherRevision = revision;
            _pidMatches.Clear();
            foreach (var p in changes.Current)
            {
                _pidMatches[p.ProcessId] = new MatchedProcess(p, _matcher.Match(p)?.Key);
            }

            return;
        }

        foreach (var p in changes.Exited)
        {
            _pidMatches.Remove(p.ProcessId);
        }

        foreach (var p in changes.Started)
        {
            _pidMatches[p.ProcessId] = new MatchedProcess(p, _matcher.Match(p)?.Key);
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
                _pidMatches[p.ProcessId] = new MatchedProcess(p, _matcher.Match(p)?.Key);
            }
        }
    }

    /// <summary>
    /// Die Tastenkürzel-Notlösung nur, solange kein Prozess eines Spiels läuft (E66): künstliche Tastendrücke an ein
    /// laufendes Spiel sind genau das, wogegen Anti-Cheat-Systeme vorgehen. Erlaubt bleibt sie vor einem Start über die
    /// App (das Spiel läuft noch nicht) und beim Zurückstellen nach dem letzten Spiel.
    /// </summary>
    private bool HotkeyFallbackAllowed => !_pidMatches.Values.Any(m => m.GameKey is not null);

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
        string devicePath, string displayName, bool enable, string trigger, ActiveSession? session, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        HdrSetResult result;
        try
        {
            result = await _hdr.SetHdrAsync(devicePath, enable, HotkeyFallbackAllowed, cancellationToken).ConfigureAwait(false);
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

        var game = session?.Name ?? "HdrPilot";
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
            .Select(s => new SessionInfo(s.Key, s.Name, s.Phase, s.Trigger, s.StartedAt, [.. s.Displays], [.. s.ProcessIds]))
            .OrderBy(s => s.StartedAt)
            .ToList();
        var previous = _publicSessions;
        _publicSessions = snapshot;
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

    private sealed record MatchedProcess(ProcessInfo Process, string? GameKey);

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

        /// <summary>Zeitpunkt des (letzten) Starts über die App; ab hier zählt die Startfrist.</summary>
        public DateTimeOffset? LaunchRequestedAt { get; set; }

        public DateTimeOffset? HdrConfirmedAt { get; set; }

        public DateTimeOffset? FirstProcessAt { get; set; }

        public List<int> ProcessIds { get; set; } = [];

        public List<string> Displays { get; } = [];

        public List<string> PendingDisplays { get; } = [];
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
            && x.Displays.SequenceEqual(y.Displays) && x.ProcessIds.SequenceEqual(y.ProcessIds);

        public int GetHashCode(SessionInfo obj) => obj.GameKey.GetHashCode(StringComparison.Ordinal);
    }
}
