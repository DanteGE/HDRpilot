using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Windows Auto HDR für Spiele mit <see cref="GameConfig.AutoHdr"/> (E88, nach dem Livetest G9.6): Windows wendet Auto HDR
/// unter 26H2 nur an, wenn der globale Schalter (Token <c>AutoHDREnable=1</c> im Wert
/// <see cref="AutoHdrRegistryLocation.GlobalValueName"/>) und Windows-HDR schon beim Start des Spielprozesses an sind; der
/// Wert je EXE wirkt nicht, ein späteres Einschalten erst beim nächsten Start. Deshalb setzt HDRpilot den globalen
/// Schalter direkt vor jedem Start, den es vorher kennt, und stellt danach den vorherigen Zustand her (R1/R2/R3).
/// <list type="bullet">
/// <item>Gesetzt nur bei angekündigten Starts („Mit HDR starten“, Verknüpfung, Steam-Startoption, Frühwarnung), nie beim
/// bloßen Erkennen eines Spiels und nie, solange kein solches Spiel startet oder läuft. Eine Ankündigung gilt, solange die
/// HDR-Sitzung auf den Spielprozess wartet, sonst bis zur Startfrist. Auch der neue Versuch nach einem Fehler setzt nur,
/// solange ein angekündigter Start noch aussteht: Läuft das Spiel schon, hat Windows den Wert gelesen, gesetzt hielte er
/// nur für andere Spiele.</item>
/// <item>Ende wie bei der Sitzungslogik: Nachlaufzeit, nach einem kurzen Lauf der Neustart-Puffer; ein Neustart in dieser
/// Zeit läuft einfach weiter. Mehrere Spiele teilen sich den Schalter: zurückgestellt wird nach dem letzten.</item>
/// <item>autohdr-global.json existiert nur, solange HDRpilot den Schalter hält (vor dem Schreiben angelegt): Nach einem
/// Absturz von HDRpilot stellt der nächste Start zurück, sobald kein solches Spiel mehr läuft. Beim Beenden von HDRpilot
/// wird immer zurückgestellt (das laufende Spiel hat den Wert schon gelesen). Lässt sich die Datei nicht löschen, hält
/// autohdr-global.done fest, dass sie erledigt ist (<see cref="AutoHdrGlobalDone"/>, nie zurückstellen), und das Löschen
/// beider Dateien wird wiederholt.</item>
/// <item>Wahl des Nutzers bleibt: Stand der Schalter schon auf 1, ändert HDRpilot nichts und stellt nichts zurück; hat der
/// Nutzer ihn in Windows geändert, bleibt seine Einstellung (R3, „extern geändert“); schaltet er im Tray um
/// (<see cref="SetByUser"/>), gibt HDRpilot den Schalter ab und setzt ihn für die laufenden Sitzungen nicht neu.</item>
/// <item>Jeder neue angekündigte Start liest den Schalter neu, auch während HDRpilot ihn hält: Hat der Nutzer ihn
/// inzwischen umgestellt (Tray oder Windows), setzt dieser Start ihn für sein Spiel wieder und stellt danach den Stand
/// her, den er vorfand.</item>
/// </list>
/// Alles im Speicher hängt nur an den Aufrufen der Sitzungslogik; ohne angekündigten Start kein Registry-Zugriff.
/// </summary>
internal sealed class AutoHdrGlobalSwitch
{
    /// <summary>Nach einem Fehler beim Lesen, Merken, Schreiben oder Zurückstellen so lange warten.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private const string Value = AutoHdrRegistryLocation.GlobalValueName;

    private readonly IGpuPreferenceStore _store;
    private readonly IFileSystem _fileSystem;
    private readonly string _path;
    private readonly string _donePath;
    private readonly IAppEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly SessionManagerOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _announced = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RunningGame> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Angekündigte Starts, deren HDR-Sitzung im letzten Zyklus noch auf den Spielprozess wartete.</summary>
    private readonly HashSet<string> _waitingSeen = new(StringComparer.OrdinalIgnoreCase);
    private AutoHdrGlobalMarker? _marker;
    private bool _markerFileExists;
    private bool _markerLoaded;
    private bool _recovered;
    private string? _inertReason;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    /// <summary>Kein sauberer REG_SZ: bis zum nächsten angekündigten Start nicht wieder lesen.</summary>
    private bool _unsupported;

    /// <summary>Schalter stand schon auf 1 (Wahl des Nutzers), solange Bedarf besteht: nicht bei jedem Zyklus neu lesen.</summary>
    private bool _alreadyOn;

    /// <summary>Der Nutzer hat im Tray umgeschaltet, während Sitzungen liefen: für sie nicht neu setzen (erst ein neuer Start).</summary>
    private bool _userChoice;

    /// <summary>Erledigte Dateien sind noch da (Merker gesperrt oder Kennzeichen nicht gelöscht): Löschen wiederholen.</summary>
    private bool _discardPending;
    private DateTimeOffset _discardRetryAfter;

    /// <summary>Kennzeichen für den erledigten Merker, der noch in der Datei steht; null, wenn keiner mehr darin steht.</summary>
    private AutoHdrGlobalDone? _abandoned;

    /// <summary><see cref="_abandoned"/> steht in autohdr-global.done.</summary>
    private bool _abandonedSaved;

    /// <param name="markerFile">autohdr-global.json.</param>
    /// <param name="doneFile">autohdr-global.done (Kennzeichen „erledigt“ für einen gesperrten Merker).</param>
    public AutoHdrGlobalSwitch(
        IGpuPreferenceStore store, IFileSystem fileSystem, string markerFile, string doneFile, IAppEnvironment environment, IClock clock, ILogger logger, SessionManagerOptions options)
    {
        _store = store;
        _fileSystem = fileSystem;
        _path = markerFile;
        _donePath = doneFile;
        _environment = environment;
        _clock = clock;
        _logger = logger;
        _options = options;
    }

    /// <summary>HDRpilot hält den globalen Schalter gerade.</summary>
    public bool IsHeld
    {
        get
        {
            lock (_gate)
            {
                return _marker is not null;
            }
        }
    }

    /// <param name="markerFileExists">Ergebnis der Prüfung beim Start (der Merker wird erst bei Bedarf gelesen).</param>
    /// <param name="doneFileExists">
    /// autohdr-global.done gefunden: Mit Merker entscheidet es beim Lesen des Merkers; ohne ist es ein Rest (der Merker ging
    /// erst nach dem Kennzeichen weg) und wird im ersten Zyklus gelöscht.
    /// </param>
    public void Start(bool markerFileExists, bool doneFileExists)
    {
        lock (_gate)
        {
            _markerFileExists = markerFileExists;
            _markerLoaded = false;
            if (doneFileExists && !markerFileExists)
            {
                _discardPending = true;
                _discardRetryAfter = DateTimeOffset.MinValue;
            }
        }
    }

    /// <summary>Start angekündigt: für ein Spiel mit der Option den Schalter jetzt setzen, vor Spielprozess und HDR-Schalten.</summary>
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

            // Ein neuer ausdrücklicher Start: Stand frisch lesen (der Nutzer kann den Schalter inzwischen geändert haben), auch
            // wenn HDRpilot ihn gerade hält. Eine Wahl im Tray galt den laufenden Sitzungen; dieser Start setzt für sein Spiel.
            _announced[current.Key] = now;
            _waitingSeen.Remove(current.Key);
            _alreadyOn = false;
            _unsupported = false;
            _userChoice = false;
            if (_marker is not null)
            {
                CheckHeld(now);
            }

            Evaluate(config, now, TriggerText(trigger), current.Name, "Start angekündigt");
        }
    }

    /// <summary>
    /// Der angekündigte Start ist gescheitert (Start-Befehl fehlgeschlagen): Ankündigung zurücknehmen und, wenn kein anderes
    /// Spiel den Schalter braucht, gleich zurückstellen statt erst mit der Startfrist.
    /// </summary>
    public void OnStartAbandoned(string gameKey, AppConfig config, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_announced.Remove(gameKey))
            {
                return;
            }

            _waitingSeen.Remove(gameKey);
            _logger.LogInformation("Auto HDR global: angekündigter Start von {Key} fehlgeschlagen.", gameKey);
            if (_inertReason is null)
            {
                Evaluate(config, now, "neuer Versuch nach einem Fehler", null, "Start fehlgeschlagen");
            }
        }
    }

    /// <summary>
    /// Erster Zyklus: Fand der Start einen Merker (HDRpilot endete nicht sauber), laufende Spiele mit der Option übernehmen
    /// (nie als kurz) und zurückstellen, sobald keines mehr läuft. Ohne Merker wird nichts übernommen.
    /// </summary>
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
            if (_inertReason is not null || _marker is null)
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

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, IReadOnlyCollection<string> waitingGameKeys, AppConfig config, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_discardPending && now >= _discardRetryAfter)
            {
                Tidy(now);
            }

            if (_running.Count == 0 && _announced.Count == 0 && _marker is null && (_markerLoaded || !_markerFileExists))
            {
                return; // Leerlauf: kein angekündigter Start, nichts gehalten
            }

            EnsureMarkerLoaded();
            if (_inertReason is not null)
            {
                return;
            }

            var running = new HashSet<string>(runningGameKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in running)
            {
                var announced = _announced.Remove(key); // der angekündigte Start ist da
                if (_running.TryGetValue(key, out var known))
                {
                    known.MissingSince = null; // Neustart in der Nachlaufzeit oder im Puffer: läuft einfach weiter
                    known.HoldUntil = null;
                    continue;
                }

                // Nur Starts, die HDRpilot angekündigt bekam: ein bloß erkanntes Spiel hat den Wert schon gelesen.
                if (announced && config.FindGame(key) is { AutoHdr: true })
                {
                    _running[key] = new RunningGame(now);
                }
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

            var waiting = new HashSet<string>(waitingGameKeys, StringComparer.OrdinalIgnoreCase);
            var launchTimeout = TimeSpan.FromSeconds(config.Settings.LaunchTimeoutSeconds);
            foreach (var (key, at) in _announced.ToList())
            {
                if (waiting.Contains(key))
                {
                    // Die HDR-Sitzung wartet noch (etwa auf ein Launcher-Fenster): die Ankündigung gilt weiter.
                    _announced[key] = now;
                }
                else if (_waitingSeen.Contains(key) || now - at >= launchTimeout)
                {
                    // Die HDR-Sitzung hat aufgegeben (Startfrist), oder ohne Sitzung ist die Startfrist um.
                    _announced.Remove(key);
                    _logger.LogInformation("Auto HDR global: angekündigter Start von {Key} ohne Spielprozess verfallen.", key);
                }
            }

            _waitingSeen.Clear();
            _waitingSeen.UnionWith(_announced.Keys.Where(waiting.Contains));

            Evaluate(config, now, "neuer Versuch nach einem Fehler", null, "Spielende");
        }
    }

    /// <summary>Option geändert oder Spiel entfernt: hat kein angekündigtes oder laufendes Spiel sie mehr, sofort zurück.</summary>
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

            Evaluate(config, now, "neuer Versuch nach einem Fehler", null, "„Windows Auto HDR“ ausgeschaltet oder Spiel entfernt");
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

            if (_discardPending)
            {
                Tidy(now); // letzter Versuch; ein gesperrter Merker bleibt mit seinem Kennzeichen als erledigt
            }

            _running.Clear();
            _announced.Clear();
            _waitingSeen.Clear();
        }
    }

    /// <summary>Liest den Schalter jetzt (für das Tray-Menü).</summary>
    public AutoHdrSwitchState Read()
    {
        lock (_gate)
        {
            try
            {
                return StateOf(_store.Read(Value));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Auto HDR global: {Value} ließ sich nicht lesen ({Error}).", Value, ex.Message);
                return AutoHdrSwitchState.Unknown;
            }
        }
    }

    /// <summary>
    /// Der Nutzer schaltet (Tray): nur das Token setzen. Hält HDRpilot den Schalter gerade, gilt das als Wahl des Nutzers:
    /// Merker weg, nach der Sitzung wird nichts zurückgestellt und für die laufenden Sitzungen nichts neu gesetzt (erst ein
    /// neuer angekündigter Start setzt ihn für sein Spiel und stellt danach diese Wahl wieder her).
    /// </summary>
    public AutoHdrSwitchState SetByUser(bool on)
    {
        lock (_gate)
        {
            EnsureMarkerLoaded();
            var wanted = on ? GpuPreferenceText.GlobalOn : GpuPreferenceText.GlobalOff;
            GpuPreferenceValue current;
            try
            {
                current = _store.Read(Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Auto HDR global: {Value} ließ sich nicht lesen ({Error}); nichts geändert.", Value, ex.Message);
                return AutoHdrSwitchState.Unknown;
            }

            var before = StateOf(current);
            if (before == AutoHdrSwitchState.Unknown)
            {
                _logger.LogInformation("Auto HDR global: {Value} ist kein sauberer REG_SZ ({Current}), HDRpilot fasst ihn nicht an.", Value, GpuPreferenceText.Describe(current));
                return AutoHdrSwitchState.Unknown;
            }

            var target = on ? AutoHdrSwitchState.On : AutoHdrSwitchState.Off;
            if (before != target)
            {
                var merge = GpuPreferenceText.SetToken(current, wanted);
                try
                {
                    _store.WriteString(Value, merge.Written!);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Auto HDR global: AutoHDREnable={Wanted} ließ sich nicht setzen ({Error}).", wanted, ex.Message);
                    return AutoHdrSwitchState.Unknown;
                }

                _logger.LogInformation(
                    "Auto HDR global: vom Nutzer {State} (vorher „{Before}“, jetzt „{After}“); gilt ab dem nächsten Spielstart.",
                    on ? "eingeschaltet" : "ausgeschaltet", GpuPreferenceText.Describe(current), merge.Written);
            }

            if (_marker is not null)
            {
                // Die Wahl des Nutzers gilt: HDRpilot gibt den Schalter ab, statt ihn nach der Sitzung zurückzustellen.
                _logger.LogInformation(
                    "Auto HDR global: Der Nutzer hat den Schalter selbst gestellt, während HDRpilot ihn hielt ({Games}); HDRpilot stellt nach dem Spiel nichts zurück.",
                    string.Join(", ", _marker.Games));
                DiscardMarker(_marker, _clock.UtcNow);
            }

            _userChoice = ActiveKeysUnchecked().Count > 0;
            _alreadyOn = false;
            return target;
        }
    }

    private void Evaluate(AppConfig config, DateTimeOffset now, string trigger, string? gameName, string releaseReason)
    {
        // Option ausgeschaltet oder Spiel entfernt: nicht mehr verfolgen (wieder eingeschaltet zählt erst der nächste Start).
        foreach (var key in ActiveKeysUnchecked().Where(k => config.FindGame(k) is not { AutoHdr: true }))
        {
            _announced.Remove(key);
            _running.Remove(key);
        }

        var active = ActiveKeys(config);
        if (active.Count == 0)
        {
            _alreadyOn = false;
            _userChoice = false;
            if (_marker is not null && now >= _retryAfter)
            {
                Release(releaseReason, now);
            }

            return;
        }

        if (_marker is not null || _alreadyOn || _userChoice || _unsupported || now < _retryAfter)
        {
            return;
        }

        // Setzen nur direkt vor einem angekündigten Start, auch beim neuen Versuch nach einem Fehler: Laufen die Spiele
        // schon (das Setzen davor scheiterte), hat Windows den Wert gelesen, gesetzt hielte er bis zu ihrem Ende nur für
        // andere Spiele. Laufende Spiele halten nur einen Schalter, den HDRpilot schon hält (oben).
        var announced = active.FirstOrDefault(_announced.ContainsKey);
        if (announced is null)
        {
            return;
        }

        Activate(active, now, trigger, gameName ?? config.FindGame(announced)?.Name ?? announced);
    }

    /// <summary>Angekündigte und laufende (auch in Nachlaufzeit oder Puffer) Spiele, die die Option noch haben.</summary>
    private List<string> ActiveKeys(AppConfig config) =>
        ActiveKeysUnchecked().Where(k => config.FindGame(k) is { AutoHdr: true }).ToList();

    private List<string> ActiveKeysUnchecked() =>
        _announced.Keys.Concat(_running.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void Activate(List<string> active, DateTimeOffset now, string trigger, string gameName)
    {
        GpuPreferenceValue current;
        try
        {
            current = _store.Read(Value);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            _logger.LogWarning("Auto HDR global: {Value} ließ sich nicht lesen ({Error}); neuer Versuch in {Seconds:0} s, falls der Start dann noch aussteht.", Value, ex.Message, RetryDelay.TotalSeconds);
            return;
        }

        var merge = GpuPreferenceText.SetToken(current, GpuPreferenceText.GlobalOn);
        if (merge.Kind == GpuPreferenceMergeKind.Unsupported)
        {
            _unsupported = true;
            _logger.LogInformation(
                "Auto HDR global: {Value} ist kein sauberer REG_SZ ({Current}), HDRpilot fasst ihn nicht an.", Value, GpuPreferenceText.Describe(current));
            return;
        }

        if (merge.Kind == GpuPreferenceMergeKind.Unchanged)
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
            _logger.LogWarning("Auto HDR global: Merker nicht gespeichert, der globale Schalter bleibt unverändert; neuer Versuch in {Seconds:0} s, falls der Start dann noch aussteht.", RetryDelay.TotalSeconds);
            return;
        }

        try
        {
            _store.WriteString(Value, marker.Written);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            DiscardMarker(marker, now);
            _logger.LogWarning("Auto HDR global: AutoHDREnable=1 ließ sich nicht setzen ({Error}); neuer Versuch in {Seconds:0} s, falls der Start dann noch aussteht.", ex.Message, RetryDelay.TotalSeconds);
            return;
        }

        marker.Status = AutoHdrEntryStatus.Applied;
        TrySaveMarker(marker); // scheitert das, klärt das Zurückstellen den Stand „Pending“
        _marker = marker;
        _logger.LogInformation(
            "Auto HDR global: AutoHDREnable=1 gesetzt für „{Game}“ ({Trigger}), vorher „{Before}“.",
            gameName, trigger, GpuPreferenceText.Describe(current));
    }

    /// <summary>Stellt zurück (R1/R2/R3) und löscht den Merker. Bei einem Registry-Fehler bleibt der Merker (neuer Versuch).</summary>
    private void Release(string reason, DateTimeOffset now)
    {
        var marker = _marker!;
        AutoHdrRestoreResult result;
        try
        {
            result = AutoHdrRestore.Apply(_store, marker);
        }
        catch (Exception ex)
        {
            _retryAfter = now + RetryDelay;
            _logger.LogWarning(
                "Auto HDR global: ließ sich nicht zurückstellen ({Error}); bleibt gemerkt, neuer Versuch in {Seconds:0} s oder beim nächsten Start.",
                ex.Message, RetryDelay.TotalSeconds);
            return;
        }

        _logger.LogInformation("{Message}", AutoHdrRestore.Describe(result, "Auto HDR global: " + Value, reason));
        DiscardMarker(marker, now);
    }

    /// <summary>
    /// Ein neuer Start, während HDRpilot den Schalter hält: Steht das Token nicht mehr auf 1 (oder ist der Wert weg oder
    /// nicht mehr sauber), hat jemand anders umgestellt (R3, etwa der Nutzer in Windows). Das ist seine Wahl: Merker weg;
    /// der neue Start setzt den Schalter wie jeder andere und merkt sich diesen Stand. Bei einem Lesefehler weiter halten.
    /// </summary>
    private void CheckHeld(DateTimeOffset now)
    {
        GpuPreferenceValue current;
        try
        {
            current = _store.Read(Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Auto HDR global: {Value} ließ sich nicht lesen ({Error}); HDRpilot hält den Schalter weiter.", Value, ex.Message);
            return;
        }

        if (StateOf(current) == AutoHdrSwitchState.On)
        {
            return; // unverändert oder nur andere Einträge geändert (R2): weiter halten
        }

        _logger.LogInformation(
            "Auto HDR global: {Value} extern geändert, während HDRpilot ihn hielt (jetzt „{Current}“); das bleibt die Wahl des Nutzers, der neue Start merkt sich diesen Stand.",
            Value, GpuPreferenceText.Describe(current));
        DiscardMarker(_marker!, now);
    }

    /// <summary>
    /// Merker entfernen, weil nichts mehr zurückzustellen ist (zurückgestellt, vom Nutzer übernommen oder nie geschrieben);
    /// HDRpilot hält den Schalter danach nicht mehr. Lässt sich die Datei nicht löschen (etwa vom Virenscanner gesperrt),
    /// hält autohdr-global.done fest, dass sie erledigt ist, damit der nächste Start oder das Entfernen sie nie als
    /// Absturzrest zurückstellen; das Löschen wird später wiederholt (<see cref="Tidy"/>).
    /// </summary>
    private void DiscardMarker(AutoHdrGlobalMarker marker, DateTimeOffset now)
    {
        _marker = null;
        _abandoned = AutoHdrGlobalDone.Of(marker);
        _abandonedSaved = false;
        Tidy(now);
    }

    /// <summary>
    /// Erledigte Dateien wegräumen: zuerst den Merker, dann das Kennzeichen (ohne Merker gilt es nichts mehr). Hält HDRpilot
    /// den Schalter inzwischen wieder, gehört der Merker zu diesem Setzen: dann nie löschen, nur das alte Kennzeichen. Bleibt
    /// der Merker gesperrt, wird das Kennzeichen gespeichert (falls noch nicht geschehen); neuer Versuch nach
    /// <see cref="RetryDelay"/> und beim Beenden. Eine Sperre auf dem Merker verhindert unter NTFS auch sein Ersetzen, deshalb
    /// steht das Kennzeichen in einer eigenen Datei.
    /// </summary>
    private void Tidy(DateTimeOffset now)
    {
        if (_marker is null && !TryDeleteMarker())
        {
            if (_abandoned is not null && !_abandonedSaved)
            {
                _abandonedSaved = TrySaveDone(_abandoned);
            }

            _discardPending = true;
            _discardRetryAfter = now + RetryDelay;
            return;
        }

        _abandoned = null; // der erledigte Merker ist weg (gelöscht oder von einem neuen Setzen ersetzt)
        _abandonedSaved = false;
        if (!TryDeleteDone())
        {
            _discardPending = true;
            _discardRetryAfter = now + RetryDelay;
            return;
        }

        _discardPending = false;
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

    private static AutoHdrSwitchState StateOf(GpuPreferenceValue value)
    {
        if (value.IsMissing)
        {
            return AutoHdrSwitchState.Off;
        }

        return GpuPreferenceText.TryParse(value, out var tokens)
            ? string.Equals(tokens.Get(GpuPreferenceText.AutoHdrKey), GpuPreferenceText.GlobalOn, StringComparison.Ordinal) ? AutoHdrSwitchState.On : AutoHdrSwitchState.Off
            : AutoHdrSwitchState.Unknown;
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
                _logger.LogWarning("Auto HDR global: {Path} ist nicht lesbar ({Problem}); Auto HDR bleibt in diesem Lauf aus.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt when load.Backup is null:
                _inertReason = "Merker des globalen Schalters defekt: " + load.Problem;
                _logger.LogWarning("Auto HDR global: {Path} ist defekt ({Problem}) und ließ sich nicht sichern; Auto HDR bleibt in diesem Lauf aus.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt:
                _markerFileExists = false;
                _logger.LogWarning(
                    "Auto HDR global: {Path} war defekt ({Problem}), gesichert als {Backup}. Den Schalter „Auto HDR“ bei Bedarf in den Windows-Einstellungen prüfen.",
                    _path, load.Problem, load.Backup);
                return;
        }

        var marker = load.Data!;
        if (!string.Equals(marker.RegistryKey, _store.KeyName, StringComparison.OrdinalIgnoreCase))
        {
            _inertReason = "Zustand gehört zu " + marker.RegistryKey;
            _logger.LogWarning("Auto HDR global: {Path} gehört zu {Key}, nicht zu {Current}; Auto HDR bleibt in diesem Lauf aus.", _path, marker.RegistryKey, _store.KeyName);
            return;
        }

        var done = IsMarkedDone(marker);
        if (done is null)
        {
            return; // Kennzeichen nicht lesbar: nichts anfassen
        }

        if (done.Value || marker.Status == AutoHdrEntryStatus.Released)
        {
            // Erledigt, nur die Datei blieb (etwa nach einer Wahl des Nutzers im Tray): nie zurückstellen, nur löschen.
            _logger.LogInformation("Auto HDR global: {Path} ist erledigt (nichts zurückzustellen) und wird gelöscht.", _path);
            _abandoned = AutoHdrGlobalDone.Of(marker);
            _abandonedSaved = done.Value;
            Tidy(_clock.UtcNow);
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

    private bool TryDeleteMarker()
    {
        try
        {
            _fileSystem.DeleteFile(_path);
            _markerFileExists = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR global: Merker {Path} ließ sich nicht löschen ({Error}); neuer Versuch später.", _path, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Steht in autohdr-global.done, dass genau dieser Merker erledigt ist? Null: Kennzeichen nicht lesbar oder defekt; dann
    /// bleibt Auto HDR in diesem Lauf aus und nichts wird angefasst, wie beim Entfernen über den Setup (ein defektes
    /// Kennzeichen könnte zu genau diesem Merker gehören, ein Zurückstellen nähme dann die Wahl des Nutzers zurück).
    /// </summary>
    private bool? IsMarkedDone(AutoHdrGlobalMarker marker)
    {
        var load = AutoHdrFiles.Load<AutoHdrGlobalDone>(_fileSystem, _donePath, d => d.IsValid(), clock: null);
        switch (load.State)
        {
            case AutoHdrFileState.Loaded:
                return load.Data!.Matches(marker); // passt es nicht, ist es ein Rest eines früheren Setzens
            case AutoHdrFileState.Missing:
                return false;
            default:
                _inertReason = "Kennzeichen des globalen Schalters nicht auswertbar: " + load.Problem;
                _logger.LogWarning("Auto HDR global: {Path} ließ sich nicht auswerten ({Problem}); Auto HDR bleibt in diesem Lauf aus.", _donePath, load.Problem);
                return null;
        }
    }

    private bool TrySaveDone(AutoHdrGlobalDone done)
    {
        try
        {
            AutoHdrFiles.Save(_fileSystem, _donePath, done);
            _logger.LogInformation("Auto HDR global: Merker {Path} gesperrt, in {Done} als erledigt vermerkt (wird nie zurückgestellt).", _path, _donePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR global: {Done} ließ sich nicht speichern ({Error}); neuer Versuch später.", _donePath, ex.Message);
            return false;
        }
    }

    private bool TryDeleteDone()
    {
        try
        {
            _fileSystem.DeleteFile(_donePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR global: {Done} ließ sich nicht löschen ({Error}); neuer Versuch später.", _donePath, ex.Message);
            return false;
        }
    }

    private static string TriggerText(SessionTrigger trigger) => trigger switch
    {
        SessionTrigger.LaunchedViaApp => "Start über HDRpilot",
        SessionTrigger.SteamLaunchOption => "Start über Steam-Startoption",
        SessionTrigger.LauncherSignal => "Launcher-Signal",
        SessionTrigger.ProcessDetected => "Spielstart erkannt",
        SessionTrigger.Recovery => "Wiederanlauf",
        _ => trigger.ToString(),
    };

    /// <param name="firstSeen">Erster erkannter Prozess; null bei übernommenen Läufen (Dauer unbekannt, nie kurz).</param>
    private sealed class RunningGame(DateTimeOffset? firstSeen)
    {
        public DateTimeOffset? FirstSeen { get; } = firstSeen;

        public DateTimeOffset? MissingSince { get; set; }

        public DateTimeOffset? HoldUntil { get; set; }
    }
}
