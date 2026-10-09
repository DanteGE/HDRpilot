using System.Globalization;
using System.IO;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Sessions;

namespace HdrPilot.App.Setup;

/// <summary>
/// Entfernen (E83). Läuft aus einer Kopie im Temp-Ordner, damit der Programmordner gelöscht werden kann. Ablauf: Sperre,
/// laufende App über den Befehlsweg beenden, gemerkten HDR-Zustand wiederherstellen, nur die eigenen Dateien löschen
/// (fremde Dateien und Ordner bleiben), Verknüpfungen auf die eigene EXE, eigenen Autostart-Wert und Eintrag entfernen;
/// Einstellungen und Daten nur auf ausdrücklichen Wunsch. Jeder Schritt macht nach Fehlern weiter, wo das sicher ist;
/// das Ergebnis nennt den ersten Fehler. Lässt sich die EXE nicht löschen, bleibt der Eintrag für einen neuen Versuch.
/// </summary>
public sealed class UninstallEngine : IUninstallEngine
{
    private readonly SetupLog _log;
    private readonly IRegistry _registry;
    private readonly IUninstallEntryStore _entries;
    private readonly ISetupAppControl _app;
    private readonly ISetupShortcuts _shortcuts;
    private readonly IHdrStateRestorer _hdr;
    private readonly ISteamLaunchOptionScanner _steam;
    private readonly ISetupFileOperations _files;
    private readonly Func<string, Version?> _versionReader;
    private readonly TimeSpan _stopTimeout;
    private readonly string _mutexName;

    public UninstallEngine(
        SetupLocations locations,
        SetupLog log,
        IRegistry registry,
        IUninstallEntryStore entries,
        ISetupAppControl app,
        ISetupShortcuts shortcuts,
        IHdrStateRestorer hdr,
        ISteamLaunchOptionScanner steam,
        ISetupFileOperations? files = null,
        Func<string, Version?>? versionReader = null,
        TimeSpan? stopTimeout = null,
        string? mutexName = null)
    {
        Locations = locations;
        _log = log;
        _registry = registry;
        _entries = entries;
        _app = app;
        _shortcuts = shortcuts;
        _hdr = hdr;
        _steam = steam;
        _files = files ?? SetupFileOperations.Instance;
        _versionReader = versionReader ?? SetupSupport.ReadFileVersion;
        _stopTimeout = stopTimeout ?? SetupEngine.DefaultStopTimeout;
        _mutexName = mutexName ?? SetupSupport.MutexNameFor(locations);
    }

    public SetupLocations Locations { get; }

    public UninstallInspection Inspect()
    {
        IReadOnlyList<string> steamGames;
        try
        {
            steamGames = _steam.FindGamesWithLaunchOption();
        }
        catch (Exception ex)
        {
            _log.Warn(SetupSupport.Describe("Steam-Startoptionen suchen", ex));
            steamGames = [];
        }

        bool running;
        try
        {
            running = _app.IsRunning();
        }
        catch (Exception ex)
        {
            _log.Warn(SetupSupport.Describe("Laufende App suchen", ex));
            running = false;
        }

        return new UninstallInspection(
            Locations.InstallDirectory,
            InstalledVersion(),
            steamGames,
            Locations.DataPaths.ConfigDirectory,
            Locations.DataPaths.DataDirectory,
            running);
    }

    public Task<SetupResult> UninstallAsync(UninstallOptions options, IProgress<UninstallStep>? progress = null, CancellationToken cancellationToken = default) =>
        // Eigener Thread: die Sperre (Mutex) wird auf demselben Thread freigegeben, der sie hält.
        Task.Factory.StartNew(() => Uninstall(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private SetupResult Uninstall(UninstallOptions options, IProgress<UninstallStep>? progress, CancellationToken cancellationToken)
    {
        _log.Info("Entfernen startet: " + Locations.InstallDirectory + ", Daten löschen " + options.RemoveData.ToString(CultureInfo.InvariantCulture) + ".");
        using var setupLock = SetupLock.TryAcquire(_mutexName);
        if (setupLock is null)
        {
            return Fail(SetupErrorKind.AnotherSetupRunning, "Sperre " + _mutexName + " belegt: ein anderes Setup oder Entfernen läuft.", null);
        }

        var version = InstalledVersion();
        var registered = ReadEntry() is not null;
        if (version is null && !registered)
        {
            return Fail(SetupErrorKind.NotInstalled, "Weder " + Locations.ExePath + " noch ein Eintrag unter „Installierte Apps“ vorhanden.", null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_app.IsRunning())
        {
            Report(progress, UninstallStep.StopRunningApp);
            if (!_app.Stop(_stopTimeout))
            {
                return Fail(SetupErrorKind.AppStillRunning, string.Create(CultureInfo.InvariantCulture, $"HDRpilot lief nach {_stopTimeout.TotalSeconds:0} s noch."), version);
            }

            _log.Info("HDRpilot ist beendet.");
        }

        var errors = new FirstError();
        RestoreHdr(progress, cancellationToken);

        Report(progress, UninstallStep.RemoveFiles);
        RemoveFiles(errors);

        Report(progress, UninstallStep.RemoveShortcuts);
        RemoveShortcuts(errors);

        Report(progress, UninstallStep.RemoveAutostart);
        Run(errors, "Autostart entfernen", () =>
        {
            if (SetupSupport.RemoveOwnAutostart(_registry, Locations))
            {
                _log.Info("Autostart entfernt.");
            }
        });

        if (File.Exists(Locations.ExePath))
        {
            _log.Warn("Die EXE ist noch da: der Eintrag unter „Installierte Apps“ bleibt für einen neuen Versuch.");
        }
        else
        {
            Report(progress, UninstallStep.Unregister);
            Run(errors, "Eintrag entfernen", () =>
            {
                _entries.Delete();
                _log.Info("Eintrag unter „Installierte Apps“ entfernt.");
            });
        }

        if (options.RemoveData)
        {
            Report(progress, UninstallStep.RemoveData);
            RemoveData(errors);
        }
        else
        {
            _log.Info("Einstellungen und Daten bleiben: " + Locations.DataPaths.ConfigDirectory + ", " + Locations.DataPaths.DataDirectory);
        }

        if (errors.Kind is { } kind)
        {
            return Fail(kind, errors.Detail!, version);
        }

        _log.Info("Entfernen fertig.");
        return new SetupResult(true, null, null, version, null, false);
    }

    private Version? InstalledVersion()
    {
        if (!File.Exists(Locations.ExePath))
        {
            return null;
        }

        try
        {
            return SetupSupport.Normalize(_versionReader(Locations.ExePath) ?? new Version(0, 0, 0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Version der installierten EXE nicht lesbar: " + ex.Message);
            return new Version(0, 0, 0);
        }
    }

    private UninstallEntry? ReadEntry()
    {
        try
        {
            return _entries.Read();
        }
        catch (Exception ex)
        {
            _log.Warn(SetupSupport.Describe("Eintrag lesen", ex));
            return null;
        }
    }

    /// <summary>
    /// Gemerkte Bildschirme (session-state.json) auf den Zustand vor dem Spiel zurückstellen, wo die Spiele es wollten;
    /// danach die Datei löschen. Scheitert ein Bildschirm, bleibt sie (die App prüft sie beim nächsten Start). Fehler hier
    /// verhindern das Entfernen nicht.
    /// </summary>
    private void RestoreHdr(IProgress<UninstallStep>? progress, CancellationToken cancellationToken)
    {
        var path = Locations.DataPaths.SessionStateFile;
        if (!File.Exists(path))
        {
            return;
        }

        Report(progress, UninstallStep.RestoreHdr);
        var store = new FileSessionStateStore(PhysicalFileSystem.Instance, path);
        var allRestored = true;
        foreach (var claim in store.Load()?.Displays ?? [])
        {
            if (!claim.Restore)
            {
                _log.Info("HDR für " + claim.FriendlyName + " bleibt (ein Spiel wollte nicht zurückstellen).");
                continue;
            }

            try
            {
                var result = _hdr.SetHdrAsync(claim.MonitorDevicePath, claim.WasHdrOn, cancellationToken).GetAwaiter().GetResult();
                _log.Info("HDR für " + claim.FriendlyName + " (" + claim.MonitorDevicePath + ") zurück auf " + (claim.WasHdrOn ? "an" : "aus") + ": " + result.Status + ", " + result.Message);
                allRestored &= result.IsSuccess;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error("HDR für " + claim.FriendlyName + " ließ sich nicht zurückstellen.", ex);
                allRestored = false;
            }
        }

        if (!allRestored)
        {
            _log.Warn("Gemerkter HDR-Zustand bleibt gespeichert: " + path);
            return;
        }

        try
        {
            store.Delete();
            _log.Info("Gemerkter HDR-Zustand gelöscht: " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Gemerkter HDR-Zustand nicht gelöscht: " + ex.Message);
        }
    }

    /// <summary>Nur die installierten Dateien (und ihre „.new“/„.old“-Reste), dann leere eigene Ordner.</summary>
    private void RemoveFiles(FirstError errors)
    {
        foreach (var relative in SetupLocations.InstalledFiles)
        {
            var target = Path.Combine(Locations.InstallDirectory, relative);
            foreach (var path in new[] { target, target + SetupFileTransaction.NewSuffix, target + SetupFileTransaction.OldSuffix })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    _files.Delete(path);
                    _log.Info("Gelöscht: " + path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(SetupSupport.Classify(ex, existingFile: true), SetupSupport.Describe("Löschen von " + path, ex));
                    _log.Error("Nicht gelöscht: " + path, ex);
                }
            }
        }

        var subfolders = SetupLocations.InstalledFiles
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(d => Path.Combine(Locations.InstallDirectory, d!));
        foreach (var folder in subfolders)
        {
            DeleteIfEmpty(folder);
        }

        DeleteIfEmpty(Locations.InstallDirectory);
    }

    /// <summary>
    /// Startmenü- und Desktop-Verknüpfung „HDRpilot“ sowie alle Spiele-Verknüpfungen der App (Startmenü\Programme\HDRpilot
    /// und Desktop), aber nur, wenn sie auf die eigene EXE zeigen. Fremde bleiben.
    /// </summary>
    private void RemoveShortcuts(FirstError errors)
    {
        var candidates = new List<string> { Locations.StartMenuShortcut, Locations.DesktopShortcut };
        foreach (var folder in new[] { Locations.GameShortcutsDirectory, Locations.DesktopDirectory })
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    candidates.AddRange(Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn("Ordner nicht lesbar: " + folder + " (" + ex.Message + ")");
            }
        }

        foreach (var shortcut in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_shortcuts.PointsTo(shortcut, Locations.ExePath))
            {
                continue;
            }

            Run(errors, "Verknüpfung " + shortcut + " entfernen", () =>
            {
                _files.Delete(shortcut);
                _log.Info("Verknüpfung entfernt: " + shortcut);
            });
        }

        DeleteIfEmpty(Locations.GameShortcutsDirectory);
    }

    /// <summary>Genau die beiden Ablageordner der App, nie ihre übergeordneten Ordner.</summary>
    private void RemoveData(FirstError errors)
    {
        var folders = new[] { Locations.DataPaths.ConfigDirectory, Locations.DataPaths.DataDirectory }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            if (string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder) ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                _log.Error("Laufwerkswurzel wird nie gelöscht: " + folder);
                continue;
            }

            Run(errors, "Ordner " + folder + " löschen", () =>
            {
                Directory.Delete(folder, recursive: true);
                _log.Info("Gelöscht: " + folder);
            });
        }
    }

    private void DeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
                _log.Info("Leerer Ordner entfernt: " + folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Etwa im Explorer geöffnet: der leere Ordner stört nicht.
            _log.Warn("Ordner bleibt: " + folder + " (" + ex.Message + ")");
        }
    }

    private void Run(FirstError errors, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errors.Add(SetupSupport.Classify(ex), SetupSupport.Describe(what, ex));
            _log.Error(what + " fehlgeschlagen.", ex);
        }
    }

    private SetupResult Fail(SetupErrorKind kind, string detail, Version? version)
    {
        _log.Error("Entfernen nicht abgeschlossen (" + kind + "): " + detail);
        return SetupResult.Failed(kind, detail, version);
    }

    private void Report(IProgress<UninstallStep>? progress, UninstallStep step)
    {
        _log.Info("Schritt: " + step);
        progress?.Report(step);
    }
}

public static partial class SetupEngines
{
    public static partial IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log)
    {
        // Prüflauf: Steam-Ordner im Testordner (nie das echte Steam), HDR nur protokollieren (nie den echten Bildschirm).
        var testRoot = SetupSupport.TestRootOf(locations);
        ISteamLaunchOptionScanner steam = testRoot is null
            ? new SteamLaunchOptionScanner(WindowsRegistry.Instance, locations.DataPaths.ConfigFile, log)
            : new SteamLaunchOptionScanner(() => Path.Combine(testRoot, "Steam"), locations.DataPaths.ConfigFile, log);
        IHdrStateRestorer hdr = testRoot is null ? new DisplayConfigHdrStateRestorer() : new LoggingHdrStateRestorer(log);
        return new UninstallEngine(
            locations,
            log,
            WindowsRegistry.Instance,
            new WindowsUninstallEntryStore(locations.UninstallKeyPath),
            new SetupAppControl(locations),
            ShellSetupShortcuts.Instance,
            hdr,
            steam);
    }
}
