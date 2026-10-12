using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Weg (a) je Spiel (E88): Für jede Ziel-EXE eines Spiels mit „Windows Auto HDR“ steht in UserGpuPreferences das Token
/// <c>AutoHDREnable=2097</c>, solange die Option an ist (Auto HDR wirkt erst beim nächsten Spielstart, der Wert muss also
/// vorher stehen). Was HDRpilot schreibt, steht vorher in autohdr-state.json (Original, geschriebener Text), damit
/// Ausschalten, Entfernen des Spiels und Rücknahme genau den Originalzustand herstellen (R1/R2), aber nie etwas, das
/// inzwischen jemand anders geändert hat (R3). Werte, die kein sauberer REG_SZ sind, fasst HDRpilot nie an.
/// <para>
/// Abgleich beim Start (vollständig), bei Konfigurationsänderungen nur, wenn sich die Ziele geändert haben oder ein Fehler
/// nachzuholen ist (sonst kein Registry-Zugriff), und für ein einzelnes Spiel vor einem angekündigten Start. Gehört der
/// gemerkte Zustand zu einem anderen Schlüssel oder ist er nicht lesbar, bleibt der Weg für diesen Lauf aus.
/// </para>
/// </summary>
internal sealed class AutoHdrRegistryEngine
{
    private readonly IGpuPreferenceStore _store;
    private readonly IFileSystem _fileSystem;
    private readonly string _path;
    private readonly IAppEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, AutoHdrTargetState> _targetStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsupportedLogged = new(StringComparer.OrdinalIgnoreCase);
    private List<AutoHdrStateEntry> _entries = [];
    private bool _fileExists;
    private string? _inertReason;

    /// <summary>Signatur der Ziele beim letzten vollständigen Abgleich; null: nächster Abgleich vollständig.</summary>
    private string? _signature;

    /// <summary>Fehler beim Schreiben, Zurücknehmen oder Speichern: nächster Abgleich holt es nach.</summary>
    private bool _retryPending;
    private bool _savePending;

    /// <summary>Während einer Rücknahme: Ergebnis je Wertname (der letzte Versuch zählt).</summary>
    private Dictionary<string, AutoHdrRestoreTally>? _tally;
    private volatile IReadOnlyDictionary<string, AutoHdrGameStatus> _status = new Dictionary<string, AutoHdrGameStatus>();

    public AutoHdrRegistryEngine(IGpuPreferenceStore store, IFileSystem fileSystem, string stateFile, IAppEnvironment environment, IClock clock, ILogger logger)
    {
        _store = store;
        _fileSystem = fileSystem;
        _path = stateFile;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Grund, warum der Weg in diesem Lauf aus ist (Zustand eines anderen Schlüssels, nicht lesbar); sonst null.</summary>
    public string? InertReason
    {
        get
        {
            lock (_gate)
            {
                return _inertReason;
            }
        }
    }

    /// <summary>Gemerkte Werte (für Tests und Diagnose).</summary>
    public int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Lädt den gemerkten Zustand (nur wenn die Datei existiert) und gleicht vollständig ab. True: Stand geändert.</summary>
    public bool Start(AppConfig config, bool stateFileExists)
    {
        lock (_gate)
        {
            Load(stateFileExists);
            return ReconcileCore(config, force: true, onlyGameKey: null);
        }
    }

    /// <summary>
    /// Abgleich nach einer Konfigurationsänderung (ohne Registry-Zugriff, wenn die Ziele gleich blieben und nichts nachzuholen
    /// ist) oder nur für ein Spiel vor seinem Start (<paramref name="onlyGameKey"/>: fehlende Werte eintragen, Fehler erneut
    /// versuchen, nichts zurücknehmen). True: Stand geändert.
    /// </summary>
    public bool Reconcile(AppConfig config, string? onlyGameKey = null)
    {
        lock (_gate)
        {
            return ReconcileCore(config, force: false, onlyGameKey);
        }
    }

    /// <summary>
    /// Rücknahme: alle gemerkten Werte zurücknehmen, gewünscht oder nicht (R1/R2/R3), danach vollständig abgleichen.
    /// True: Stand geändert.
    /// </summary>
    public bool RestoreAll(AppConfig config, string reason)
    {
        lock (_gate)
        {
            if (_inertReason is not null)
            {
                return false;
            }

            var dirty = false;
            var retry = false;
            foreach (var entry in _entries.ToList())
            {
                retry |= !RestoreEntry(entry, reason, ref dirty);
            }

            SaveIfNeeded(dirty);
            _retryPending = retry || _savePending;
            _signature = null;
            return ReconcileCore(config, force: true, onlyGameKey: null);
        }
    }

    public AutoHdrGameStatus GetStatus(string gameKey) =>
        _status.TryGetValue(gameKey, out var status) ? status : AutoHdrGameStatus.None;

    /// <summary>Zählt ab jetzt das Ergebnis jedes Zurücknehmens (für „Auto-HDR-Einträge zurücknehmen“).</summary>
    public void BeginTally()
    {
        lock (_gate)
        {
            _tally = new Dictionary<string, AutoHdrRestoreTally>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Beendet das Zählen: zurückgenommen, stehen gelassen (extern geändert), fehlgeschlagen.</summary>
    public (int Restored, int LeftAsIs, int Failed) EndTally()
    {
        lock (_gate)
        {
            var tally = _tally ?? [];
            _tally = null;
            return (
                tally.Values.Count(t => t == AutoHdrRestoreTally.Restored),
                tally.Values.Count(t => t == AutoHdrRestoreTally.LeftAsIs),
                tally.Values.Count(t => t == AutoHdrRestoreTally.Failed));
        }
    }

    private void Load(bool stateFileExists)
    {
        _entries = [];
        _fileExists = false;
        _inertReason = null;
        _signature = null;
        if (!stateFileExists)
        {
            return;
        }

        var load = AutoHdrFiles.Load<AutoHdrStateData>(_fileSystem, _path, d => d.IsValid(), _clock);
        switch (load.State)
        {
            case AutoHdrFileState.Missing:
                return;

            case AutoHdrFileState.Unreadable:
                _fileExists = true;
                _inertReason = "Zustand nicht lesbar: " + load.Problem;
                _logger.LogWarning("Auto HDR: {Path} ist nicht lesbar ({Problem}). Werte je Spiel bleiben in diesem Lauf unverändert.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt when load.Backup is null:
                _fileExists = true;
                _inertReason = "Zustand defekt: " + load.Problem;
                _logger.LogWarning("Auto HDR: {Path} ist defekt ({Problem}) und ließ sich nicht sichern. Werte je Spiel bleiben in diesem Lauf unverändert.", _path, load.Problem);
                return;

            case AutoHdrFileState.Corrupt:
                _logger.LogWarning(
                    "Auto HDR: {Path} war defekt ({Problem}), gesichert als {Backup}. Früher eingetragene Werte lassen sich nicht automatisch zurücknehmen (in den Windows-Grafikeinstellungen prüfen).",
                    _path, load.Problem, load.Backup);
                return;
        }

        var data = load.Data!;
        _fileExists = true;
        if (!string.Equals(data.RegistryKey, _store.KeyName, StringComparison.OrdinalIgnoreCase))
        {
            _inertReason = "Zustand gehört zu " + data.RegistryKey;
            _logger.LogWarning(
                "Auto HDR: {Path} gehört zu {Key}, nicht zu {Current}. Werte je Spiel bleiben in diesem Lauf unverändert.", _path, data.RegistryKey, _store.KeyName);
            return;
        }

        _entries = data.Entries;
        var builds = _entries.Select(e => e.WindowsBuild).Where(b => b != _environment.WindowsBuild).Distinct().ToList();
        if (builds.Count > 0)
        {
            _logger.LogInformation(
                "Auto HDR: Windows-Build seit dem Eintragen geändert ({Old} → {New}); Format ungeprüft.", string.Join(", ", builds), _environment.WindowsBuild);
        }
    }

    private bool ReconcileCore(AppConfig config, bool force, string? onlyGameKey)
    {
        var desired = AutoHdrTargets.Desired(config);
        if (_inertReason is not null)
        {
            _targetStates.Clear();
            foreach (var target in desired.TargetOrder)
            {
                _targetStates[target] = AutoHdrTargetState.Failed;
            }

            return Publish(desired);
        }

        var full = onlyGameKey is null;
        if (full && !force && !_retryPending && string.Equals(desired.Signature, _signature, StringComparison.Ordinal))
        {
            return Publish(desired); // Ziele unverändert: kein Registry-Zugriff
        }

        if (!full && !desired.ByGame.ContainsKey(onlyGameKey!))
        {
            return false;
        }

        var dirty = false;
        var retry = false;
        if (full)
        {
            foreach (var entry in _entries.Where(e => !desired.Targets.ContainsKey(e.ValueName)).ToList())
            {
                retry |= !RestoreEntry(entry, ReasonFor(entry, config), ref dirty);
            }
        }

        foreach (var target in desired.TargetOrder)
        {
            var games = desired.Targets[target];
            if (!full && !games.Contains(onlyGameKey!, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var entry = Find(target);
            if (entry is not null && !entry.Games.SequenceEqual(games, StringComparer.OrdinalIgnoreCase))
            {
                entry.Games = [.. games];
                dirty = true;
            }

            var state = entry is null ? Apply(target, games, config, ref dirty) : Check(entry, config, ref dirty);
            _targetStates[target] = state;
            retry |= state == AutoHdrTargetState.Failed;
        }

        SaveIfNeeded(dirty);
        if (full)
        {
            _signature = desired.Signature;
            _retryPending = retry || _savePending;
            foreach (var stale in _targetStates.Keys.Where(k => !desired.Targets.ContainsKey(k)).ToList())
            {
                _targetStates.Remove(stale);
            }
        }
        else
        {
            _retryPending |= retry || _savePending;
        }

        return Publish(desired);
    }

    /// <summary>Ziel ohne gemerkten Eintrag: lesen, Token setzen (Zustand zuerst auf die Platte, dann schreiben).</summary>
    private AutoHdrTargetState Apply(string target, IReadOnlyList<string> games, AppConfig config, ref bool dirty)
    {
        GpuPreferenceValue current;
        try
        {
            current = _store.Read(target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Auto HDR: {Exe} ließ sich nicht lesen ({Error}); neuer Versuch beim nächsten Abgleich.", target, ex.Message);
            return AutoHdrTargetState.Failed;
        }

        var merge = GpuPreferenceText.MergeOn(current, GpuPreferenceText.PerGameOn);
        switch (merge.Kind)
        {
            case GpuPreferenceMergeKind.Unsupported:
                if (_unsupportedLogged.Add(target))
                {
                    _logger.LogInformation(
                        "Auto HDR: {Exe} ist kein sauberer REG_SZ ({Value}), nicht angefasst.", target, GpuPreferenceText.Describe(current));
                }

                return AutoHdrTargetState.Unsupported;

            case GpuPreferenceMergeKind.AlreadyOn:
                _logger.LogDebug("Auto HDR: {Exe} steht schon auf AutoHDREnable=2097 („{Value}“), nichts einzutragen.", target, current.Text);
                return AutoHdrTargetState.AlreadyOn;
        }

        var entry = new AutoHdrStateEntry
        {
            ValueName = target,
            Games = [.. games],
            Status = AutoHdrEntryStatus.Pending,
            Original = AutoHdrOriginalValue.From(current),
            OriginalToken = merge.OriginalToken,
            Written = merge.Written!,
            WindowsBuild = _environment.WindowsBuild,
            WrittenAt = _clock.UtcNow,
        };
        _entries.Add(entry);
        if (!TrySave())
        {
            // Ohne gemerkten Originalzustand wird nie geschrieben.
            _entries.Remove(entry);
            _logger.LogWarning("Auto HDR: Zustand nicht gespeichert, {Exe} wird nicht eingetragen; neuer Versuch beim nächsten Abgleich.", target);
            return AutoHdrTargetState.Failed;
        }

        dirty = false; // alles bis hier ist gespeichert
        return WriteNow(entry, config, current, ref dirty);
    }

    private AutoHdrTargetState WriteNow(AutoHdrStateEntry entry, AppConfig config, GpuPreferenceValue before, ref bool dirty)
    {
        try
        {
            _store.WriteString(entry.ValueName, entry.Written);
        }
        catch (Exception ex)
        {
            _entries.Remove(entry);
            dirty = true;
            _logger.LogWarning("Auto HDR: {Exe} ließ sich nicht eintragen ({Error}); neuer Versuch beim nächsten Abgleich.", entry.ValueName, ex.Message);
            return AutoHdrTargetState.Failed;
        }

        entry.Status = AutoHdrEntryStatus.Applied;
        dirty = true;
        _logger.LogInformation(
            "Auto HDR: „{Game}“ – {Exe} eingetragen (AutoHDREnable=2097), vorher „{Before}“.",
            GameNames(entry, config), entry.ValueName, GpuPreferenceText.Describe(before));
        return AutoHdrTargetState.Applied;
    }

    /// <summary>Ziel mit gemerktem Eintrag: steht er noch? Ein unterbrochenes Schreiben (Pending) wird hier fertig.</summary>
    private AutoHdrTargetState Check(AutoHdrStateEntry entry, AppConfig config, ref bool dirty)
    {
        if (entry.Status == AutoHdrEntryStatus.ExternallyChanged)
        {
            return AutoHdrTargetState.ExternallyChanged; // wird nie von selbst neu geschrieben
        }

        GpuPreferenceValue current;
        try
        {
            current = _store.Read(entry.ValueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Auto HDR: {Exe} ließ sich nicht lesen ({Error}); neuer Versuch beim nächsten Abgleich.", entry.ValueName, ex.Message);
            return AutoHdrTargetState.Failed;
        }

        if (entry.Status == AutoHdrEntryStatus.Pending && GpuPreferenceText.SameValue(current, entry.Original.ToValue()))
        {
            // Absturz zwischen Merken und Schreiben: jetzt schreiben.
            return WriteNow(entry, config, current, ref dirty);
        }

        if (GpuPreferenceText.TryParse(current, out var tokens)
            && (string.Equals(current.Text, entry.Written, StringComparison.Ordinal)
                || string.Equals(tokens.Get(GpuPreferenceText.AutoHdrKey), GpuPreferenceText.PerGameOn, StringComparison.Ordinal)))
        {
            // Steht noch (Windows ergänzt etwa AppStatus=…: bleibt so, R2 nimmt später nur das Token zurück).
            if (entry.Status == AutoHdrEntryStatus.Pending)
            {
                entry.Status = AutoHdrEntryStatus.Applied;
                dirty = true;
            }

            return AutoHdrTargetState.Applied;
        }

        entry.Status = AutoHdrEntryStatus.ExternallyChanged;
        dirty = true;
        _logger.LogInformation(
            "Auto HDR: {Exe} extern geändert (jetzt „{Value}“), HDRpilot lässt den Wert stehen. Zum erneuten Eintragen „Windows Auto HDR“ aus- und wieder einschalten.",
            entry.ValueName, GpuPreferenceText.Describe(current));
        return AutoHdrTargetState.ExternallyChanged;
    }

    /// <summary>Nimmt einen gemerkten Wert zurück und vergisst ihn. False: Registry-Fehler, der Eintrag bleibt.</summary>
    private bool RestoreEntry(AutoHdrStateEntry entry, string reason, ref bool dirty)
    {
        if (entry.Status == AutoHdrEntryStatus.ExternallyChanged)
        {
            _entries.Remove(entry);
            dirty = true;
            Count(entry.ValueName, AutoHdrRestoreTally.LeftAsIs);
            _logger.LogInformation("Auto HDR: {Exe} war extern geändert und bleibt, wie er ist ({Reason}).", entry.ValueName, reason);
            return true;
        }

        AutoHdrRestoreResult result;
        try
        {
            result = AutoHdrRestore.Apply(_store, entry.ValueName, entry.Original, entry.OriginalToken, entry.Written, GpuPreferenceText.PerGameOn);
        }
        catch (Exception ex)
        {
            Count(entry.ValueName, AutoHdrRestoreTally.Failed);
            _logger.LogWarning(
                "Auto HDR: {Exe} ließ sich nicht zurücknehmen ({Error}); bleibt gemerkt, neuer Versuch beim nächsten Abgleich.", entry.ValueName, ex.Message);
            return false;
        }

        _entries.Remove(entry);
        dirty = true;
        Count(entry.ValueName, result.Outcome == AutoHdrRestoreOutcome.LeftAsIs ? AutoHdrRestoreTally.LeftAsIs : AutoHdrRestoreTally.Restored);
        _logger.LogInformation("{Message}", AutoHdrRestore.Describe(result, "Auto HDR: " + entry.ValueName, reason));
        return true;
    }

    private static string ReasonFor(AutoHdrStateEntry entry, AppConfig config)
    {
        var games = entry.Games.Select(config.FindGame).Where(g => g is not null).ToList();
        if (games.Count == 0)
        {
            return "Spiel entfernt";
        }

        return games.All(g => !g!.AutoHdr) ? "Windows Auto HDR ausgeschaltet" : "EXE ist kein Ziel mehr";
    }

    private void Count(string valueName, AutoHdrRestoreTally outcome)
    {
        if (_tally is not null)
        {
            _tally[valueName] = outcome;
        }
    }

    private AutoHdrStateEntry? Find(string valueName) =>
        _entries.FirstOrDefault(e => string.Equals(e.ValueName, valueName, StringComparison.OrdinalIgnoreCase));

    private static string GameNames(AutoHdrStateEntry entry, AppConfig config) =>
        string.Join("“, „", entry.Games.Select(k => config.FindGame(k)?.Name ?? k));

    private void SaveIfNeeded(bool dirty)
    {
        if (dirty || _savePending)
        {
            _savePending = !TrySave();
        }
    }

    /// <summary>Schreibt den Zustand atomar; ohne Einträge wird die Datei gelöscht. False bei Fehlern (protokolliert).</summary>
    private bool TrySave()
    {
        try
        {
            if (_entries.Count == 0)
            {
                if (_fileExists)
                {
                    _fileSystem.DeleteFile(_path);
                    _fileExists = false;
                }

                return true;
            }

            AutoHdrFiles.Save(_fileSystem, _path, new AutoHdrStateData { RegistryKey = _store.KeyName, Entries = _entries });
            _fileExists = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto HDR: Zustand {Path} ließ sich nicht speichern ({Error}).", _path, ex.Message);
            return false;
        }
    }

    /// <summary>Stand je Spiel mit Option an veröffentlichen. True, wenn er sich geändert hat.</summary>
    private bool Publish(AutoHdrDesired desired)
    {
        var map = new Dictionary<string, AutoHdrGameStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, targets) in desired.ByGame)
        {
            var list = new List<AutoHdrTargetStatus>(targets.Count);
            foreach (var target in targets)
            {
                if (_targetStates.TryGetValue(target, out var state))
                {
                    list.Add(new AutoHdrTargetStatus(target, state));
                }
            }

            map[key] = list.Count == 0 ? AutoHdrGameStatus.None : new AutoHdrGameStatus(list);
        }

        var previous = _status;
        _status = map;
        return previous.Count != map.Count
            || map.Any(p => !previous.TryGetValue(p.Key, out var old) || !old.Targets.SequenceEqual(p.Value.Targets));
    }
}

/// <summary>Ergebnis eines Werts bei der Rücknahme.</summary>
internal enum AutoHdrRestoreTally
{
    Restored,
    LeftAsIs,
    Failed,
}
