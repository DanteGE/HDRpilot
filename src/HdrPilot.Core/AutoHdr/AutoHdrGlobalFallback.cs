using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Weg (b), Notlösung (E88, Einstellung <see cref="AppSettings.AutoHdrGlobalFallback"/>, Standard aus): Solange ein Spiel
/// mit „Windows Auto HDR“ läuft oder sein Start angekündigt ist, setzt HDRpilot im globalen Wert
/// <see cref="AutoHdrRegistryLocation.GlobalValueName"/> das Token <c>AutoHDREnable=1</c> und stellt danach den vorherigen
/// Zustand her (R1/R2/R3 wie je Spiel). Nie, solange kein solches Spiel läuft.
/// <list type="bullet">
/// <item>Angekündigte Starts („Mit HDR starten“, Verknüpfung, Steam-Startoption, Frühwarnung) setzen den Schalter vor dem
/// Spielprozess; sonst beim Erkennen des Prozesses. Eine Ankündigung verfällt nach der Startfrist ohne Prozess.</item>
/// <item>Ende wie bei der Sitzungslogik: Nachlaufzeit, nach einem kurzen Lauf der Neustart-Puffer; ein Neustart in dieser
/// Zeit läuft einfach weiter. Bei pausierter Automatik zählen neu erkannte Spiele nicht.</item>
/// <item>autohdr-global.json existiert nur, solange HDRpilot den Schalter hält (vor dem Schreiben angelegt): Nach einem
/// Absturz von HDRpilot stellt der nächste Start zurück, sobald kein solches Spiel mehr läuft. Beim Beenden von HDRpilot
/// wird immer zurückgestellt (das laufende Spiel hat den Wert schon gelesen).</item>
/// <item>Steht der Schalter schon auf 1 (vom Nutzer), ändert HDRpilot nichts und stellt nichts zurück.</item>
/// </list>
/// Alles im Speicher hängt nur an den Aufrufen der Sitzungslogik; ohne Spiel mit der Option kein Registry-Zugriff.
/// </summary>
internal sealed class AutoHdrGlobalFallback
{
    /// <summary>Nach einem Fehler beim Lesen, Merken, Schreiben oder Zurückstellen so lange warten.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private const string Value = AutoHdrRegistryLocation.GlobalValueName;

    private readonly IGpuPreferenceStore _store;
    private readonly IFileSystem _fileSystem;
    private readonly string _path;
    private readonly IAppEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly SessionManagerOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _announced = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RunningGame> _running = new(StringComparer.OrdinalIgnoreCase);
    private AutoHdrGlobalMarker? _marker;
    private bool _markerFileExists;
    private bool _markerLoaded;
    private bool _recovered;
    private string? _inertReason;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    /// <summary>Kein sauberer REG_SZ: in diesem Lauf nie anfassen.</summary>
    private bool _unsupported;

    /// <summary>Schalter stand schon auf 1, solange Bedarf besteht: nicht bei jedem Zyklus neu lesen.</summary>
    private bool _alreadyOn;

    /// <summary>Während einer Rücknahme: Ergebnis des Zurückstellens.</summary>
    private bool _tallying;
    private AutoHdrRestoreTally? _tally;

    public AutoHdrGlobalFallback(
        IGpuPreferenceStore store, IFileSystem fileSystem, string markerFile, IAppEnvironment environment, IClock clock, ILogger logger, SessionManagerOptions options)
    {
        _store = store;
        _fileSystem = fileSystem;
        _path = markerFile;
        _environment = environment;
        _clock = clock;
        _logger = logger;
        _options = options;
    }

    /// <summary>HDRpilot hält den globalen Schalter gerade.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _marker is not null;
            }
        }
    }

    /// <summary>Grund, warum die Notlösung in diesem Lauf aus ist (Merker eines anderen Schlüssels, nicht lesbar); sonst null.</summary>
    public string? InertReason
    {
        get
        {
            lock (_gate)
            {
                EnsureMarkerLoaded();
                return _inertReason;
            }
        }
    }

    /// <param name="markerFileExists">Ergebnis der Prüfung beim Start (der Merker wird erst bei Bedarf gelesen).</param>
    public void Start(bool markerFileExists)
    {
        lock (_gate)
        {
            _markerFileExists = markerFileExists;
            _markerLoaded = false;
        }
    }

    public void OnStartAnnounced(GameConfig game, AppConfig config, SessionTrigger trigger, DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureMarkerLoaded();
            var current = config.FindGame(game.Key);
            if (_inertReason is not null || current is not { AutoHdr: true })
            {
                return;
            }

            _announced[current.Key] = now;
            Evaluate(config, now, TriggerText(trigger), current.Name, "Start angekündigt");
        }
    }

    /// <summary>Erster Zyklus: gemerkten Schalter übernehmen, laufende Spiele mit der Option übernehmen (nie als kurz).</summary>
    public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_recovered)
            {
                return;
            }

            _recovered = true;
            EnsureMarkerLoaded();
            if (_inertReason is not null)
            {
                return;
            }

            foreach (var key in runningGameKeys)
            {
                if (config.FindGame(key) is { AutoHdr: true } && !_running.ContainsKey(key))
                {
                    _running[key] = new RunningGame(null);
                }
            }

            Evaluate(config, now, "Wiederanlauf", null, "Wiederanlauf nach Absturz");
        }
    }

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (runningGameKeys.Count == 0 && _running.Count == 0 && _announced.Count == 0 && _marker is null && (_markerLoaded || !_markerFileExists))
            {
                return; // Leerlauf: nichts zu tun
            }

            EnsureMarkerLoaded();
            if (_inertReason is not null)
            {
                return;
            }

            var running = new HashSet<string>(runningGameKeys, StringComparer.OrdinalIgnoreCase);
            string? detected = null;
            foreach (var key in running)
            {
                if (_running.TryGetValue(key, out var known))
                {
                    known.MissingSince = null; // Neustart in der Nachlaufzeit oder im Puffer: läuft einfach weiter
                    known.HoldUntil = null;
                    continue;
                }

                var game = config.FindGame(key);
                if (game is not { AutoHdr: true })
                {
                    continue;
                }

                var announced = _announced.Remove(key);
                if (paused && !announced)
                {
                    continue; // pausiert: neu erkannte Spiele zählen nicht
                }

                _running[key] = new RunningGame(now);
                detected ??= game.Name;
            }

            foreach (var (key, game) in _running.ToList())
            {
                if (running.Contains(key))
                {
                    continue;
                }

                if (game.MissingSince is null)
                {
                    game.MissingSince = now;
                    game.HoldUntil = now + HoldFor(game, config, now);
                }
                else if (now >= game.HoldUntil)
                {
                    _running.Remove(key);
                }
            }

            var launchTimeout = TimeSpan.FromSeconds(config.Settings.LaunchTimeoutSeconds);
            foreach (var (key, at) in _announced.ToList())
            {
                if (now - at >= launchTimeout)
                {
                    _announced.Remove(key);
                    _logger.LogDebug("Auto HDR global: angekündigter Start von {Key} ohne Spielprozess verfallen.", key);
                }
            }

            Evaluate(config, now, "Spielstart erkannt", detected, "Spielende");
        }
    }

    /// <summary>Einstellung oder Option geändert: sofort setzen oder zurückstellen.</summary>
    public void OnConfigChanged(AppConfig config, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_running.Count == 0 && _announced.Count == 0 && _marker is null && (_markerLoaded || !_markerFileExists))
            {
                return;
            }

            EnsureMarkerLoaded();
            if (_inertReason is not null)
            {
                return;
            }

            var reason = config.Settings.AutoHdrGlobalFallback ? "kein Spiel mit „Windows Auto HDR“ mehr aktiv" : "Notlösung ausgeschaltet";
            Evaluate(config, now, "Einstellung geändert", null, reason);
        }
    }

    /// <summary>Zählt ab jetzt das Ergebnis des Zurückstellens (für „Auto-HDR-Einträge zurücknehmen“; der letzte Versuch zählt).</summary>
    public void BeginTally()
    {
        lock (_gate)
        {
            _tallying = true;
            _tally = null;
        }
    }

    /// <summary>Beendet das Zählen: zurückgestellt, stehen gelassen (extern geändert), fehlgeschlagen (je 0 oder 1).</summary>
    public (int Restored, int LeftAsIs, int Failed) EndTally()
    {
        lock (_gate)
        {
            _tallying = false;
            return _tally switch
            {
                AutoHdrRestoreTally.Restored => (1, 0, 0),
                AutoHdrRestoreTally.LeftAsIs => (0, 1, 0),
                AutoHdrRestoreTally.Failed => (0, 0, 1),
                _ => (0, 0, 0),
            };
        }
    }

    /// <summary>„Auto-HDR-Einträge zurücknehmen“: Schalter zurückstellen, falls er noch gehalten wird.</summary>
    public void ReleaseForRollback(DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureMarkerLoaded();
            if (_inertReason is not null || _marker is null)
            {
                return;
            }

            _alreadyOn = false;
            Release("Auto-HDR-Einträge zurückgenommen", now);
        }
    }

    /// <summary>HDRpilot endet: Schalter zurückstellen (danach verfolgt niemand mehr das Spielende).</summary>
    public void Shutdown(DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureMarkerLoaded();
            if (_inertReason is null && _marker is not null)
            {
                Release("HDRpilot wird beendet", now);
            }

            _running.Clear();
            _announced.Clear();
        }
    }

    private void Evaluate(AppConfig config, DateTimeOffset now, string trigger, string? gameName, string releaseReason)
    {
        var active = ActiveKeys(config);
        if (!config.Settings.AutoHdrGlobalFallback || active.Count == 0)
        {
            _alreadyOn = false;
            if (_marker is not null && now >= _retryAfter)
            {
                Release(releaseReason, now);
            }

            return;
        }

        if (_marker is not null || _alreadyOn || _unsupported || now < _retryAfter)
        {
            return;
        }

        Activate(active, config, now, trigger, gameName ?? config.FindGame(active[0])?.Name ?? active[0]);
    }

    /// <summary>Angekündigte und laufende (auch in Nachlaufzeit oder Puffer) Spiele, die die Option noch haben.</summary>
    private List<string> ActiveKeys(AppConfig config) =>
        _announced.Keys.Concat(_running.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => config.FindGame(k) is { AutoHdr: true })
            .ToList();

    private void Activate(List<string> active, AppConfig config, DateTimeOffset now, string trigger, string gameName)
    {
        GpuPreferenceValue current;
        try
        {
            current = _store.Read(Value);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            _logger.LogWarning("Auto HDR global: {Value} ließ sich nicht lesen ({Error}); neuer Versuch in {Seconds:0} s.", Value, ex.Message, RetryDelay.TotalSeconds);
            return;
        }

        var merge = GpuPreferenceText.MergeOn(current, GpuPreferenceText.GlobalOn);
        if (merge.Kind == GpuPreferenceMergeKind.Unsupported)
        {
            _unsupported = true;
            _logger.LogInformation(
                "Auto HDR global: {Value} ist kein sauberer REG_SZ ({Current}), HDRpilot fasst ihn nicht an.", Value, GpuPreferenceText.Describe(current));
            return;
        }

        if (merge.Kind == GpuPreferenceMergeKind.AlreadyOn)
        {
            _alreadyOn = true;
            _logger.LogInformation(
                "Auto HDR global ist schon an („{Current}“): HDRpilot ändert für „{Game}“ nichts und stellt nichts zurück.", current.Text, gameName);
            return;
        }

        var marker = new AutoHdrGlobalMarker
        {
            RegistryKey = _store.KeyName,
            Status = AutoHdrEntryStatus.Pending,
            Original = AutoHdrOriginalValue.From(current),
            OriginalToken = merge.OriginalToken,
            Written = merge.Written!,
            Games = active,
            WindowsBuild = _environment.WindowsBuild,
            SetAt = now,
        };
        if (!TrySaveMarker(marker))
        {
            _retryAfter = now + RetryDelay;
            _logger.LogWarning("Auto HDR global: Merker nicht gespeichert, der globale Schalter bleibt unverändert; neuer Versuch in {Seconds:0} s.", RetryDelay.TotalSeconds);
            return;
        }

        try
        {
            _store.WriteString(Value, marker.Written);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            TryDeleteMarker();
            _logger.LogWarning("Auto HDR global: AutoHDREnable=1 ließ sich nicht setzen ({Error}); neuer Versuch in {Seconds:0} s.", ex.Message, RetryDelay.TotalSeconds);
            return;
        }

        marker.Status = AutoHdrEntryStatus.Applied;
        TrySaveMarker(marker); // scheitert das, klärt der nächste Start den Stand „Pending“
        _marker = marker;
        _logger.LogInformation(
            "Auto HDR global (Notlösung): AutoHDREnable=1 gesetzt für „{Game}“ ({Trigger}), vorher „{Before}“.",
            gameName, trigger, GpuPreferenceText.Describe(current));
    }

    /// <summary>Stellt zurück (R1/R2/R3) und löscht den Merker. Null: Registry-Fehler, der Merker bleibt.</summary>
    private AutoHdrRestoreOutcome? Release(string reason, DateTimeOffset now)
    {
        var marker = _marker!;
        AutoHdrRestoreResult result;
        try
        {
            result = AutoHdrRestore.Apply(_store, Value, marker.Original, marker.OriginalToken, marker.Written, GpuPreferenceText.GlobalOn);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            Count(AutoHdrRestoreTally.Failed);
            _logger.LogWarning(
                "Auto HDR global: ließ sich nicht zurückstellen ({Error}); bleibt gemerkt, neuer Versuch in {Seconds:0} s oder beim nächsten Start.",
                ex.Message, RetryDelay.TotalSeconds);
            return null;
        }

        Count(result.Outcome == AutoHdrRestoreOutcome.LeftAsIs ? AutoHdrRestoreTally.LeftAsIs : AutoHdrRestoreTally.Restored);

        switch (result)
        {
            case { Outcome: AutoHdrRestoreOutcome.Restored, Rule: GpuPreferenceRestoreRule.Exact }:
                _logger.LogInformation(
                    "Auto HDR global: zurückgestellt auf „{Original}“ ({Reason}).", GpuPreferenceText.Describe(marker.Original.ToValue()), reason);
                break;
            default:
                _logger.LogInformation("{Message}", AutoHdrRestore.Describe(result, "Auto HDR global: " + Value, reason));
                break;
        }

        TryDeleteMarker();
        _marker = null;
        return result.Outcome;
    }

    /// <summary>
    /// Ruhezeit nach dem Ende wie <c>SessionManager.BeginEnding</c>: nach einem kurzen Lauf der Neustart-Puffer (wenn er
    /// länger als die Nachlaufzeit ist), sonst die Nachlaufzeit. Übernommene Läufe (ohne Beginn) sind nie kurz.
    /// </summary>
    private TimeSpan HoldFor(RunningGame game, AppConfig config, DateTimeOffset now)
    {
        var grace = TimeSpan.FromSeconds(config.Settings.GracePeriodSeconds);
        var buffer = config.Settings.RestartBufferSeconds is { } seconds
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, AppSettings.RestartBufferMaxSeconds))
            : _options.RestartBuffer;
        var shortRun = buffer > grace && game.FirstSeen is { } first && now - first < _options.ShortSessionLimit;
        return shortRun ? buffer : grace;
    }

    private void Count(AutoHdrRestoreTally outcome)
    {
        if (_tallying)
        {
            _tally = outcome;
        }
    }

    private void EnsureMarkerLoaded()
    {
        if (_markerLoaded)
        {
            return;
        }

        _markerLoaded = true;
        if (!_markerFileExists)
        {
            return;
        }

        var load = AutoHdrFiles.Load<AutoHdrGlobalMarker>(_fileSystem, _path, m => m.IsValid(), _clock);
        switch (load.State)
        {
            case AutoHdrFileState.Missing:
                _markerFileExists = false;
                return;

            case AutoHdrFileState.Unreadable:
                _inertReason = "Merker des globalen Schalters nicht lesbar: " + load.Problem;
                _logger.LogWarning("Auto HDR global: {Path} ist nicht lesbar ({Problem}); die Notlösung bleibt in diesem Lauf aus.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt when load.Backup is null:
                _inertReason = "Merker des globalen Schalters defekt: " + load.Problem;
                _logger.LogWarning("Auto HDR global: {Path} ist defekt ({Problem}) und ließ sich nicht sichern; die Notlösung bleibt in diesem Lauf aus.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt:
                _markerFileExists = false;
                _logger.LogWarning(
                    "Auto HDR global: {Path} war defekt ({Problem}), gesichert als {Backup}. Den globalen Auto-HDR-Schalter bei Bedarf in den Windows-Einstellungen prüfen.",
                    _path, load.Problem, load.Backup);
                return;
        }

        var marker = load.Data!;
        if (!string.Equals(marker.RegistryKey, _store.KeyName, StringComparison.OrdinalIgnoreCase))
        {
            _inertReason = "Zustand gehört zu " + marker.RegistryKey;
            _logger.LogWarning("Auto HDR global: {Path} gehört zu {Key}, nicht zu {Current}; die Notlösung bleibt in diesem Lauf aus.", _path, marker.RegistryKey, _store.KeyName);
            return;
        }

        _marker = marker;
        _logger.LogInformation(
            "Auto HDR global: gemerkter Schalter gefunden (vorher „{Original}“, gesetzt {SetAt:u} für {Games}); der letzte Lauf endete nicht sauber.",
            GpuPreferenceText.Describe(marker.Original.ToValue()), marker.SetAt, string.Join(", ", marker.Games));
        if (marker.WindowsBuild != _environment.WindowsBuild)
        {
            _logger.LogInformation("Auto HDR global: Windows-Build seit dem Setzen geändert ({Old} → {New}); Format ungeprüft.", marker.WindowsBuild, _environment.WindowsBuild);
        }
    }

    private bool TrySaveMarker(AutoHdrGlobalMarker marker)
    {
        try
        {
            AutoHdrFiles.Save(_fileSystem, _path, marker);
            _markerFileExists = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR global: Merker {Path} ließ sich nicht speichern ({Error}).", _path, ex.Message);
            return false;
        }
    }

    private void TryDeleteMarker()
    {
        try
        {
            _fileSystem.DeleteFile(_path);
            _markerFileExists = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR global: Merker {Path} ließ sich nicht löschen ({Error}); der nächste Start prüft ihn.", _path, ex.Message);
        }
    }

    private static string TriggerText(SessionTrigger trigger) => trigger switch
    {
        SessionTrigger.LaunchedViaApp => "Start über die App",
        SessionTrigger.SteamLaunchOption => "Start über Steam-Startoption",
        SessionTrigger.LauncherSignal => "Launcher-Signal",
        SessionTrigger.ProcessDetected => "Spielstart erkannt",
        SessionTrigger.Recovery => "Wiederanlauf",
        _ => trigger.ToString(),
    };

    /// <param name="FirstSeen">Erster erkannter Prozess; null bei übernommenen Läufen (Dauer unbekannt, nie kurz).</param>
    private sealed class RunningGame(DateTimeOffset? firstSeen)
    {
        public DateTimeOffset? FirstSeen { get; } = firstSeen;

        public DateTimeOffset? MissingSince { get; set; }

        public DateTimeOffset? HoldUntil { get; set; }
    }
}
