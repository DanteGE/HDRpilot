using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.App.Setup;

/// <summary>
/// Installation und Aktualisierung (E83). Ablauf: Sperre, Versionsvergleich, Setup-Datei prüfen, laufende App über den
/// Befehlsweg beenden, Dateien als „.new“ ablegen und prüfen, dann ersetzen (bisherige als „.old“), Verknüpfungen,
/// Autostart, Eintrag unter „Installierte Apps“, „.old“ löschen, App starten. Scheitert ein Schritt nach dem Kopieren,
/// wird alles bisher Geänderte zurückgenommen: die vorige Version bleibt lauffähig und startet wieder, wenn sie lief.
/// Einstellungen und Daten (<see cref="SetupLocations.DataPaths"/>) fasst der Setup nie an.
/// </summary>
public sealed class SetupEngine : ISetupEngine
{
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(20);

    private readonly SetupBundle _bundle;
    private readonly SetupLog _log;
    private readonly IRegistry _registry;
    private readonly IUninstallEntryStore _entries;
    private readonly ISetupAppControl _app;
    private readonly ISetupShortcuts _shortcuts;
    private readonly ISetupFileOperations _files;
    private readonly Version _setupVersion;
    private readonly Func<string, Version?> _versionReader;
    private readonly TimeSpan _stopTimeout;
    private readonly string _mutexName;

    /// <param name="setupVersion">Version dieser Setup-Datei (Standard: Version dieser Assembly).</param>
    /// <param name="versionReader">Version einer installierten EXE (Standard: Dateiversion).</param>
    public SetupEngine(
        SetupLocations locations,
        SetupBundle bundle,
        SetupLog log,
        IRegistry registry,
        IUninstallEntryStore entries,
        ISetupAppControl app,
        ISetupShortcuts shortcuts,
        ISetupFileOperations? files = null,
        Version? setupVersion = null,
        Func<string, Version?>? versionReader = null,
        TimeSpan? stopTimeout = null,
        string? mutexName = null)
    {
        Locations = locations;
        _bundle = bundle;
        _log = log;
        _registry = registry;
        _entries = entries;
        _app = app;
        _shortcuts = shortcuts;
        _files = files ?? SetupFileOperations.Instance;
        _setupVersion = SetupSupport.Normalize(setupVersion ?? typeof(SetupEngine).Assembly.GetName().Version ?? new Version(0, 0, 0));
        _versionReader = versionReader ?? SetupSupport.ReadFileVersion;
        _stopTimeout = stopTimeout ?? DefaultStopTimeout;
        _mutexName = mutexName ?? SetupSupport.MutexNameFor(locations);
    }

    public SetupLocations Locations { get; }

    public SetupInspection Inspect()
    {
        var installed = InstalledVersion();
        return new SetupInspection(
            _setupVersion,
            installed,
            Guard("Eintrag lesen", () => _entries.Read() is not null),
            Locations.InstallDirectory,
            installed is null || Guard("Autostart lesen", () => SetupSupport.AutostartPointsToExe(_registry, Locations)),
            Guard("Desktop-Verknüpfung lesen", () => _shortcuts.PointsTo(Locations.DesktopShortcut, Locations.ExePath)),
            Guard("Laufende App suchen", _app.IsRunning));
    }

    public Task<SetupResult> InstallAsync(InstallOptions options, IProgress<SetupStep>? progress = null, CancellationToken cancellationToken = default) =>
        // Eigener Thread: die Sperre (Mutex) wird auf demselben Thread freigegeben, der sie hält.
        Task.Factory.StartNew(() => Install(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private SetupResult Install(InstallOptions options, IProgress<SetupStep>? progress, CancellationToken cancellationToken)
    {
        _log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Setup {_setupVersion.ToString(3)} startet: Ziel {Locations.InstallDirectory}, Autostart {options.Autostart}, Desktop-Verknüpfung {options.DesktopShortcut}, danach starten {options.LaunchAfterInstall}, ältere Version erlaubt {options.AllowDowngrade}."));
        using var setupLock = SetupLock.TryAcquire(_mutexName);
        if (setupLock is null)
        {
            return Fail(SetupErrorKind.AnotherSetupRunning, "Sperre " + _mutexName + " belegt: ein anderes Setup oder Entfernen läuft.", null);
        }

        var inspection = Inspect();
        var previous = inspection.InstalledVersion;
        _log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Stand: {inspection.Kind}, installiert {previous?.ToString(3) ?? "nichts"}, eingetragen {inspection.Registered}, läuft {inspection.AppRunning}."));
        if (inspection.Kind == SetupKind.Downgrade && !options.AllowDowngrade)
        {
            return Fail(SetupErrorKind.DowngradeNotAllowed, "Installiert ist " + previous!.ToString(3) + ", der Setup enthält " + _setupVersion.ToString(3) + ".", previous);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Report(progress, SetupStep.CheckSetupFile);
        if (!CheckBundle(out var payload, out var problem))
        {
            return Fail(SetupErrorKind.SetupFileDamaged, problem, previous);
        }

        // Was hier beendet wird, startet nach einem Fehler wieder: meist die installierte EXE, sonst eine portable Kopie
        // aus einem anderen Ordner (gleiche Einzelinstanz-Kennung).
        string? stoppedExe = null;
        if (_app.IsRunning())
        {
            Report(progress, SetupStep.StopRunningApp);
            stoppedExe = Guard("Laufende Instanz zuordnen", () => _app.InstanceExePath()) ?? Locations.ExePath;
            if (!_app.Stop(_stopTimeout))
            {
                return Fail(SetupErrorKind.AppStillRunning, string.Create(CultureInfo.InvariantCulture, $"HDRpilot lief nach {_stopTimeout.TotalSeconds:0} s noch."), previous);
            }

            _log.Info("HDRpilot ist beendet: " + stoppedExe);
        }

        var wasRunning = stoppedExe is not null;
        var newFolders = MissingFolders();
        Report(progress, SetupStep.CopyFiles);
        var transaction = new SetupFileTransaction(_files, _log);
        try
        {
            CopyFiles(transaction, payload);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            RemoveIfEmpty(newFolders);
            var kind = ex is InvalidDataException ? SetupErrorKind.SetupFileDamaged : SetupSupport.Classify(ex, transaction.FailedOnExistingFile);
            return FailAfterStop(kind, SetupSupport.Describe("Dateien kopieren (" + (transaction.CurrentFile ?? Locations.InstallDirectory) + ")", ex), previous, stoppedExe);
        }

        PreviousState? before = null;
        var step = SetupStep.Shortcuts;
        try
        {
            before = CaptureState();
            Report(progress, step);
            WriteShortcuts(options.DesktopShortcut);
            step = SetupStep.Autostart;
            Report(progress, step);
            ApplyAutostart(options.Autostart);
            step = SetupStep.Register;
            Report(progress, step);
            Register();
        }
        catch (Exception ex)
        {
            if (before is not null)
            {
                RestoreState(before);
            }

            transaction.Rollback();
            RemoveIfEmpty(newFolders);

            // Verknüpfungen und Registry liegen außerhalb des Programmordners: „kein Schreibzugriff auf den Programmordner“
            // oder „Datei in Benutzung“ wäre hier die falsche Meldung.
            return FailAfterStop(SetupSupport.ClassifyOutsideProgramFolder(ex), SetupSupport.Describe(step.ToString(), ex), previous, stoppedExe);
        }

        transaction.Complete();
        var started = false;
        if (options.LaunchAfterInstall)
        {
            Report(progress, SetupStep.LaunchApp);
            started = Launch(Locations.ExePath, inspection.Kind != SetupKind.Install && wasRunning ? "--minimized" : "--show");
        }

        _log.Info("Setup fertig: HDRpilot " + _setupVersion.ToString(3) + " ist installiert" + (started ? " und gestartet." : "."));
        return new SetupResult(true, null, null, previous, _setupVersion, started);
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
            _log.Warn("Version der installierten EXE nicht lesbar, gilt als älter: " + ex.Message);
            return new Version(0, 0, 0);
        }
    }

    private bool CheckBundle(out IReadOnlyList<string> payload, out string problem)
    {
        payload = [];
        problem = string.Empty;
        try
        {
            if (!_bundle.VerifyExe())
            {
                problem = "Prüfsumme der EXE in " + _bundle.FilePath + " stimmt nicht.";
                return false;
            }

            payload = _bundle.PayloadEntries();
            _log.Info("Setup-Datei geprüft: EXE " + _bundle.ExeLength.ToString(CultureInfo.InvariantCulture) + " Bytes, Beigaben " + string.Join(", ", payload) + ".");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problem = SetupSupport.Describe("Setup-Datei lesen", ex);
            return false;
        }
    }

    /// <summary>EXE und Beigaben ablegen, die EXE gegen die Prüfsumme im Abschluss prüfen, dann alle ersetzen.</summary>
    private void CopyFiles(SetupFileTransaction transaction, IReadOnlyList<string> payload)
    {
        _files.CreateDirectory(Locations.InstallDirectory);
        transaction.Stage(Locations.ExePath, _bundle.CopyExeTo);
        if (!HashEquals(Locations.ExePath + SetupFileTransaction.NewSuffix, _bundle.ExeSha256))
        {
            throw new InvalidDataException("Die abgelegte EXE weicht von der Prüfsumme im Setup ab.");
        }

        foreach (var name in SetupBundle.PayloadEntryNames)
        {
            if (!payload.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue; // fehlt im Paket: eine vorhandene Datei bleibt, wie sie ist
            }

            var target = Path.Combine(Locations.InstallDirectory, name.Replace('/', '\\'));
            _files.CreateDirectory(Path.GetDirectoryName(target)!);
            transaction.Stage(target, stream =>
            {
                if (!_bundle.CopyPayloadEntryTo(name, stream))
                {
                    throw new InvalidDataException("Eintrag fehlt im Paket: " + name);
                }
            });
        }

        transaction.CommitAll();
    }

    /// <summary>Ordner der installierten Dateien (samt übergeordneten), die es vor dem Kopieren noch nicht gibt; tiefste zuerst.</summary>
    private List<string> MissingFolders()
    {
        var missing = new List<string>();
        foreach (var file in SetupLocations.InstalledFiles)
        {
            for (var folder = Path.GetDirectoryName(Path.Combine(Locations.InstallDirectory, file));
                 !string.IsNullOrEmpty(folder) && !Directory.Exists(folder);
                 folder = Path.GetDirectoryName(folder))
            {
                if (!missing.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    missing.Add(folder);
                }
            }
        }

        return [.. missing.OrderByDescending(f => f.Length)];
    }

    /// <summary>Nach dem Rückfall: selbst angelegte Ordner wieder entfernen, sofern leer (vorhandene bleiben immer).</summary>
    private void RemoveIfEmpty(IReadOnlyList<string> createdFolders)
    {
        foreach (var folder in createdFolders)
        {
            SetupSupport.DeleteIfEmpty(folder, _log);
        }
    }

    private static bool HashEquals(string path, IReadOnlyList<byte> expected)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream).AsSpan().SequenceEqual(expected.ToArray());
    }

    /// <summary>Startmenü immer; Desktop nach Wunsch, sonst nur eine eigene Desktop-Verknüpfung entfernen.</summary>
    private void WriteShortcuts(bool desktop)
    {
        var exe = Locations.ExePath;
        _files.CreateDirectory(Locations.ProgramsDirectory);
        _shortcuts.Write(Locations.StartMenuShortcut, exe, Locations.ExtraLaunchArguments, Locations.InstallDirectory, SetupSupport.ProductName);
        _log.Info("Startmenü-Verknüpfung: " + Locations.StartMenuShortcut);
        if (desktop)
        {
            _files.CreateDirectory(Locations.DesktopDirectory);
            _shortcuts.Write(Locations.DesktopShortcut, exe, Locations.ExtraLaunchArguments, Locations.InstallDirectory, SetupSupport.ProductName);
            _log.Info("Desktop-Verknüpfung: " + Locations.DesktopShortcut);
        }
        else if (_shortcuts.PointsTo(Locations.DesktopShortcut, exe))
        {
            _files.Delete(Locations.DesktopShortcut);
            _log.Info("Desktop-Verknüpfung entfernt: " + Locations.DesktopShortcut);
        }
    }

    private void ApplyAutostart(bool enabled)
    {
        if (enabled)
        {
            SetupSupport.EnableAutostart(_registry, Locations);
            _log.Info("Autostart eingetragen.");
        }
        else if (SetupSupport.RemoveOwnAutostart(_registry, Locations))
        {
            _log.Info("Autostart entfernt.");
        }
        else if (SetupSupport.ReadAutostart(_registry, Locations) is { } foreign)
        {
            _log.Info("Fremder Autostart-Wert bleibt unberührt: " + foreign);
        }
    }

    private void Register()
    {
        var bytes = SetupLocations.InstalledFiles
            .Select(f => new FileInfo(Path.Combine(Locations.InstallDirectory, f)))
            .Where(f => f.Exists)
            .Sum(f => f.Length);
        var entry = UninstallEntry.For(Locations, _setupVersion, bytes, DateTime.Now);
        _entries.Write(entry);
        _log.Info("Eingetragen unter „Installierte Apps“: " + entry.DisplayVersion + ", " + entry.EstimatedSizeKb.ToString(CultureInfo.InvariantCulture) + " KB.");
    }

    private PreviousState CaptureState() => new(
        File.Exists(Locations.StartMenuShortcut) ? File.ReadAllBytes(Locations.StartMenuShortcut) : null,
        File.Exists(Locations.DesktopShortcut) ? File.ReadAllBytes(Locations.DesktopShortcut) : null,
        SetupSupport.ReadAutostart(_registry, Locations),
        _entries.Read());

    /// <summary>Verknüpfungen, Autostart und Eintrag auf den Stand vor dem Setup zurücksetzen. Wirft nicht.</summary>
    private void RestoreState(PreviousState state)
    {
        Try("Startmenü-Verknüpfung zurücksetzen", () => RestoreFile(Locations.StartMenuShortcut, state.StartMenuShortcut));
        Try("Desktop-Verknüpfung zurücksetzen", () => RestoreFile(Locations.DesktopShortcut, state.DesktopShortcut));
        Try("Autostart zurücksetzen", () => SetupSupport.RestoreAutostart(_registry, Locations, state.Autostart));
        Try("Eintrag zurücksetzen", () =>
        {
            if (state.Entry is null)
            {
                _entries.Delete();
            }
            else
            {
                _entries.Write(state.Entry);
            }
        });
    }

    private void RestoreFile(string path, byte[]? content)
    {
        if (content is null)
        {
            _files.Delete(path);
        }
        else
        {
            File.WriteAllBytes(path, content);
        }
    }

    private bool Launch(string exePath, string mode)
    {
        var arguments = Locations.ExtraLaunchArguments.Length == 0 ? mode : mode + " " + Locations.ExtraLaunchArguments;
        try
        {
            var started = _app.Start(exePath, arguments);
            _log.Info((started ? "Gestartet: " : "Nicht gestartet: ") + exePath + " " + arguments);
            return started;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("HDRpilot ließ sich nicht starten.", ex);
            return false;
        }
    }

    /// <summary>
    /// Fehler nach dem Beenden der App: die beendete (unveränderte) Version wieder im Tray starten. Das ist die EXE, aus der
    /// sie lief (auch eine portable Kopie); gibt es die nicht mehr, die bisher installierte.
    /// </summary>
    private SetupResult FailAfterStop(SetupErrorKind kind, string detail, Version? previous, string? stoppedExe)
    {
        var result = Fail(kind, detail, previous);
        if (stoppedExe is null)
        {
            return result;
        }

        var exe = File.Exists(stoppedExe) ? stoppedExe : Locations.ExePath;
        if (!File.Exists(exe))
        {
            _log.Warn("Die beendete Version lässt sich nicht wieder starten: " + stoppedExe + " fehlt.");
            return result;
        }

        _log.Info("Die beendete Version wird wieder gestartet: " + exe);
        return result with { AppStarted = Launch(exe, "--minimized") };
    }

    private SetupResult Fail(SetupErrorKind kind, string detail, Version? previous)
    {
        _log.Error("Setup nicht abgeschlossen (" + kind + "): " + detail);
        return SetupResult.Failed(kind, detail, previous);
    }

    private void Report(IProgress<SetupStep>? progress, SetupStep step)
    {
        _log.Info("Schritt: " + step);
        progress?.Report(step);
    }

    /// <summary>Liest einen Zustand; scheitert das, gilt der Standardwert (false, null) und der Fehler steht im Protokoll.</summary>
    private T? Guard<T>(string what, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _log.Warn(SetupSupport.Describe(what, ex));
            return default;
        }
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
            _log.Info(what + ": erledigt.");
        }
        catch (Exception ex)
        {
            _log.Error(what + " fehlgeschlagen.", ex);
        }
    }

    private sealed record PreviousState(byte[]? StartMenuShortcut, byte[]? DesktopShortcut, string? Autostart, UninstallEntry? Entry);
}

public static partial class SetupEngines
{
    public static partial ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log) => new SetupEngine(
        locations,
        bundle,
        log,
        WindowsRegistry.Instance,
        new WindowsUninstallEntryStore(locations.UninstallKeyPath),
        new SetupAppControl(locations),
        ShellSetupShortcuts.Instance);
}
