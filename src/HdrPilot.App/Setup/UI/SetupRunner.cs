using System.Globalization;
using System.IO;
using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Processes;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Abläufe von Setup und Entfernen (E83), mit Fenster oder still (<c>--quiet</c>). Ergebnis ist immer ein Exit-Code
/// aus <see cref="SetupExitCodes"/>; Einzelheiten stehen im Setup-Protokoll. Die Kopie zum Entfernen schreibt am Ende
/// „ERGEBNIS: &lt;Code&gt;“ dorthin und lässt ihren Temp-Ordner löschen.
/// </summary>
public static class SetupRunner
{
    public static async Task<int> RunAsync(Application app, SetupMode mode)
    {
        // Zwischen zwei Fenstern (Rückfrage, Fortschritt) darf WPF nicht von selbst beenden; den Code setzt App.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var args = mode.Arguments;
        SetupLocations locations;
        SetupLog log;
        try
        {
            locations = SetupMode.LocationsFor(args);
            log = SetupLog.For(locations, args.TestRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SetupExitCodes.BadArguments; // Testordner unbrauchbar
        }

        log.Info(string.Create(CultureInfo.InvariantCulture, $"{mode.Kind} startet (PID {Environment.ProcessId}, {Environment.ProcessPath}), still: {args.Quiet}, Prüflauf: {locations.IsTest}"));
        app.DispatcherUnhandledException += (_, e) =>
        {
            log.Error("Unbehandelter Fehler in der Oberfläche", e.Exception);
            e.Handled = true;
        };

        if (args.Unknown.Count > 0)
        {
            // Still: lieber nichts tun als etwas anderes als verlangt. Die Kopie zum Entfernen bekommt ihre Befehlszeile
            // von uns und räumt in jedem Fall auf.
            log.Warn("Unbekannte Argumente: " + string.Join(' ', args.Unknown));
            if (args.Quiet && mode.Kind != SetupModeKind.UninstallContinue)
            {
                log.Warn("Nichts geändert, Exit-Code " + SetupExitCodes.BadArguments.ToString(CultureInfo.InvariantCulture));
                return SetupExitCodes.BadArguments;
            }
        }

        if (mode.Kind == SetupModeKind.UninstallContinue)
        {
            return await RunUninstallContinueAsync(app, args, locations, log);
        }

        try
        {
            return mode.Kind == SetupModeKind.Setup
                ? await RunSetupAsync(app, mode.Bundle!, args, locations, log)
                : await RunUninstallAsync(app, args, locations, log);
        }
        catch (Exception ex)
        {
            log.Error("Abgebrochen", ex);
            if (!args.Quiet)
            {
                ShowUnexpected(mode.Kind == SetupModeKind.Setup ? SetupStrings.SetupWindowTitle : SetupStrings.UninstallWindowTitle, ex, log);
            }

            return SetupExitCodes.Failed;
        }
    }

    /// <summary>Setup ohne Fenster: Optionen nach <see cref="SetupMode.QuietOptions"/>, ältere Version nur mit <c>--allow-downgrade</c>.</summary>
    public static async Task<int> RunQuietSetupAsync(ISetupEngine engine, SetupInspection inspection, SetupArguments args, SetupLog log)
    {
        var options = SetupMode.QuietOptions(args, inspection);
        if (inspection.Kind == SetupKind.Downgrade && !options.AllowDowngrade)
        {
            log.Warn(string.Create(CultureInfo.InvariantCulture, $"Neuere Version {inspection.InstalledVersion} installiert, ohne --allow-downgrade nichts geändert."));
            return SetupExitCodes.DowngradeRefused;
        }

        log.Info("Optionen: " + options);
        var result = await engine.InstallAsync(options);
        LogResult(log, result);
        return SetupMode.ExitCodeFor(result);
    }

    /// <summary>Entfernen ohne Fenster in der Kopie: Ergebnis ins Protokoll, Exit-Code zurück.</summary>
    public static async Task<int> RunQuietUninstallAsync(IUninstallEngine engine, bool removeData, SetupLog log)
    {
        var result = await engine.UninstallAsync(new UninstallOptions(removeData));
        LogResult(log, result);
        return SetupMode.ExitCodeFor(result);
    }

    private static async Task<int> RunSetupAsync(Application app, SetupBundle bundle, SetupArguments args, SetupLocations locations, SetupLog log)
    {
        var engine = SetupEngines.CreateSetup(locations, bundle, log);
        var inspection = engine.Inspect();
        log.Info("Stand: " + inspection.Kind + " " + inspection);
        if (args.Quiet)
        {
            return await RunQuietSetupAsync(engine, inspection, args, log);
        }

        ThemeManager.Initialize(app);
        var model = new SetupViewModel(engine, inspection, args, log);
        var window = new SetupWindow();
        window.Attach(model);
        return await ShowAsync(window, () => model.ExitCode);
    }

    /// <summary>Installierte EXE: Rückfrage (oder still sofort), dann die Kopie im Temp-Ordner starten und enden.</summary>
    private static async Task<int> RunUninstallAsync(Application app, SetupArguments args, SetupLocations locations, SetupLog log)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der eigenen EXE unbekannt.");
        Task HandOff(bool removeData)
        {
            var continueArgs = (args with { RemoveData = removeData }).ToContinueArguments(Environment.ProcessId);
            return Task.Run(() => UninstallHandoff.Start(self, Path.GetTempPath(), continueArgs, log));
        }

        if (args.Quiet)
        {
            await HandOff(args.RemoveData);
            return SetupExitCodes.Ok;
        }

        ThemeManager.Initialize(app);
        var engine = SetupEngines.CreateUninstall(locations, log);
        var inspection = engine.Inspect();
        log.Info("Stand: " + inspection);
        var model = new UninstallViewModel(inspection, args, HandOff, log);
        var window = new UninstallWindow();
        window.Attach(model);
        return await ShowAsync(window, () => model.ExitCode);
    }

    /// <summary>
    /// Kopie im Temp-Ordner: warten, bis die installierte EXE beendet ist, entfernen (mit Fenster oder still), Ergebnis
    /// ins Protokoll, Temp-Ordner nach dem Ende löschen lassen. Läuft auch bei Fehlern bis zum Aufräumen.
    /// </summary>
    private static async Task<int> RunUninstallContinueAsync(Application app, SetupArguments args, SetupLocations locations, SetupLog log)
    {
        var code = SetupExitCodes.Failed;
        try
        {
            if (args.WaitPid is { } pid)
            {
                await WaitForInstalledExeAsync(pid, log);
            }

            var engine = SetupEngines.CreateUninstall(locations, log);
            if (args.Quiet)
            {
                code = await RunQuietUninstallAsync(engine, args.RemoveData, log);
            }
            else
            {
                ThemeManager.Initialize(app);
                var model = new UninstallProgressViewModel(engine, args.RemoveData, locations.DataPaths.ConfigDirectory, log);
                var window = new UninstallProgressWindow();
                window.Attach(model);
                var closed = ShowAsync(window, () => model.ExitCode);
                await model.RunAsync();
                code = await closed;
            }
        }
        catch (Exception ex)
        {
            log.Error("Entfernen abgebrochen", ex);
            if (!args.Quiet)
            {
                ShowUnexpected(SetupStrings.UninstallWindowTitle, ex, log);
            }

            code = SetupExitCodes.Failed;
        }
        finally
        {
            // Das Protokoll liegt nicht im Temp-Ordner der Kopie; „ERGEBNIS“ ist seine letzte Zeile (Prüfskripte).
            UninstallHandoff.ScheduleSelfRemoval(Environment.ProcessPath, Path.GetTempPath(), log);
            log.Info("ERGEBNIS: " + code.ToString(CultureInfo.InvariantCulture));
        }

        return code;
    }

    /// <summary>Wartet ohne Handle über die Prozessliste (alle 200 ms, höchstens 30 s) auf das Ende der installierten EXE.</summary>
    private static async Task WaitForInstalledExeAsync(int pid, SetupLog log)
    {
        using var snapshot = new NtProcessSnapshotProvider();
        bool IsRunning(int id)
        {
            try
            {
                return snapshot.GetProcesses().Any(p => p.ProcessId == id);
            }
            catch (Exception ex)
            {
                // Ohne Prozessliste nicht ewig warten; scheitert das Entfernen an der laufenden EXE, meldet es das.
                log.Warn("Prozessliste nicht lesbar: " + ex.Message);
                return false;
            }
        }

        if (!await UninstallHandoff.WaitUntilGoneAsync(pid, IsRunning, UninstallHandoff.WaitInterval, UninstallHandoff.WaitTimeout))
        {
            log.Warn(string.Create(CultureInfo.InvariantCulture, $"Prozess {pid} lief nach {UninstallHandoff.WaitTimeout.TotalSeconds:0} s noch; Entfernen versucht es trotzdem."));
        }
    }

    private static Task<int> ShowAsync(Window window, Func<int> exitCode)
    {
        var closed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(exitCode());
        window.Show();
        window.Activate();
        return closed.Task;
    }

    private static void ShowUnexpected(string title, Exception ex, SetupLog log) =>
        MessageBox.Show(SetupText.Error(SetupErrorKind.Unexpected, ex.Message, log.FilePath), title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Ergebnis einer Installation oder eines Entfernens ins Setup-Protokoll.</summary>
    public static void LogResult(SetupLog log, SetupResult result)
    {
        if (result.Success)
        {
            log.Info(string.Create(CultureInfo.InvariantCulture, $"Erfolg: {result.PreviousVersion} -> {result.NewVersion}, gestartet: {result.AppStarted}"));
        }
        else
        {
            log.Error(string.Create(CultureInfo.InvariantCulture, $"Fehler {result.Error}: {result.Message}"));
        }
    }
}
