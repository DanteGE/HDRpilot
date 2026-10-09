using System.Globalization;
using System.IO;
using System.Windows;
using HdrPilot.App.Resources;

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

        return await RunAsync(new WpfSetupHost(app), mode, locations, log);
    }

    /// <summary>
    /// Ablauf ohne WPF-Anwendung: Fenster, Prozesse und Engines kommen von <paramref name="host"/>. Passt die
    /// Befehlszeile nicht (<see cref="SetupMode.RefusedArguments"/>), ändert der Lauf nichts und liefert
    /// <see cref="SetupExitCodes.BadArguments"/>; die Kopie zum Entfernen räumt auch dann ihren Temp-Ordner weg.
    /// </summary>
    public static async Task<int> RunAsync(ISetupHost host, SetupMode mode, SetupLocations locations, SetupLog log)
    {
        var args = mode.Arguments;
        if (args.Unknown.Count > 0)
        {
            log.Warn("Unbekannte Argumente: " + string.Join(' ', args.Unknown));
        }

        var refused = SetupMode.RefusedArguments(mode.Kind, args);
        if (mode.Kind == SetupModeKind.UninstallContinue)
        {
            return await RunUninstallContinueAsync(host, args, refused, locations, log);
        }

        if (refused.Count > 0)
        {
            return Refuse(host, mode.Kind, args, refused, log);
        }

        try
        {
            return mode.Kind == SetupModeKind.Setup
                ? await RunSetupAsync(host, mode.Bundle!, args, locations, log)
                : await RunUninstallAsync(host, args, locations, log);
        }
        catch (Exception ex)
        {
            log.Error("Abgebrochen", ex);
            if (!args.Quiet)
            {
                host.ShowError(TitleFor(mode.Kind), SetupText.Error(SetupErrorKind.Unexpected, ex.Message, log.FilePath));
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

    /// <summary>
    /// Befehlszeile der Kopie zum Entfernen: die Wahl aus dem Fenster (auch Daten löschen), die eigene Prozess-ID zum
    /// Warten und der Testordner als vollständiger Pfad (die Kopie läuft in ihrem Temp-Ordner; ein relativer Pfad zeigte
    /// dort auf einen anderen Ordner).
    /// </summary>
    public static IReadOnlyList<string> ContinueArguments(SetupArguments args, bool removeData, int processId) =>
        (args with { RemoveData = removeData, TestRoot = args.TestRoot is null ? null : Path.GetFullPath(args.TestRoot) }).ToContinueArguments(processId);

    private static async Task<int> RunSetupAsync(ISetupHost host, SetupBundle bundle, SetupArguments args, SetupLocations locations, SetupLog log)
    {
        var engine = host.CreateSetup(locations, bundle, log);
        var inspection = engine.Inspect();
        log.Info("Stand: " + inspection.Kind + " " + inspection);
        return args.Quiet
            ? await RunQuietSetupAsync(engine, inspection, args, log)
            : await host.ShowSetupAsync(engine, inspection, args, log);
    }

    /// <summary>Installierte EXE: Rückfrage (oder still sofort), dann die Kopie im Temp-Ordner starten und enden.</summary>
    private static async Task<int> RunUninstallAsync(ISetupHost host, SetupArguments args, SetupLocations locations, SetupLog log)
    {
        Task HandOff(bool removeData) => host.HandOffAsync(ContinueArguments(args, removeData, host.ProcessId), log);

        if (args.Quiet)
        {
            await HandOff(args.RemoveData);
            return SetupExitCodes.Ok;
        }

        var engine = host.CreateUninstall(locations, log);
        var inspection = engine.Inspect();
        log.Info("Stand: " + inspection);
        return await host.ShowUninstallAsync(inspection, args, HandOff, log);
    }

    /// <summary>
    /// Kopie im Temp-Ordner: warten, bis die installierte EXE beendet ist, entfernen (mit Fenster oder still), Ergebnis
    /// ins Protokoll, Temp-Ordner nach dem Ende löschen lassen. Läuft auch bei Fehlern und abgelehnter Befehlszeile bis
    /// zum Aufräumen.
    /// </summary>
    private static async Task<int> RunUninstallContinueAsync(ISetupHost host, SetupArguments args, IReadOnlyList<string> refused, SetupLocations locations, SetupLog log)
    {
        var code = SetupExitCodes.Failed;
        try
        {
            if (refused.Count > 0)
            {
                // Die Befehlszeile kommt von der installierten EXE; passt sie nicht, wird nichts entfernt.
                code = Refuse(host, SetupModeKind.UninstallContinue, args, refused, log);
            }
            else
            {
                if (args.WaitPid is { } pid)
                {
                    await host.WaitUntilGoneAsync(pid, log);
                }

                var engine = host.CreateUninstall(locations, log);
                code = args.Quiet
                    ? await RunQuietUninstallAsync(engine, args.RemoveData, log)
                    : await host.ShowUninstallProgressAsync(engine, args.RemoveData, locations.DataPaths.ConfigDirectory, log);
            }
        }
        catch (Exception ex)
        {
            log.Error("Entfernen abgebrochen", ex);
            if (!args.Quiet)
            {
                // Bis hierher kann schon etwas entfernt sein: „die bisherige Version ist unverändert“ wäre falsch.
                host.ShowError(SetupStrings.UninstallWindowTitle, SetupText.UninstallError(SetupErrorKind.Unexpected, ex.Message, log.FilePath));
            }

            code = SetupExitCodes.Failed;
        }
        finally
        {
            // Das Protokoll liegt nicht im Temp-Ordner der Kopie; „ERGEBNIS“ ist seine letzte Zeile (Prüfskripte).
            host.ScheduleSelfRemoval(log);
            log.Info("ERGEBNIS: " + code.ToString(CultureInfo.InvariantCulture));
        }

        return code;
    }

    /// <summary>Abgelehnte Befehlszeile: nichts ändern, im Protokoll begründen, mit Fenster eine Fehlermeldung.</summary>
    private static int Refuse(ISetupHost host, SetupModeKind kind, SetupArguments args, IReadOnlyList<string> refused, SetupLog log)
    {
        var detail = string.Join(' ', refused);
        log.Warn("Befehlszeile abgelehnt (" + detail + "), nichts geändert, Exit-Code " + SetupExitCodes.BadArguments.ToString(CultureInfo.InvariantCulture));
        if (!args.Quiet)
        {
            host.ShowError(TitleFor(kind), SetupText.RefusedArguments(detail, log.FilePath));
        }

        return SetupExitCodes.BadArguments;
    }

    private static string TitleFor(SetupModeKind kind) => kind == SetupModeKind.Setup ? SetupStrings.SetupWindowTitle : SetupStrings.UninstallWindowTitle;

    /// <summary>Ergebnis einer Installation oder eines Entfernens ins Setup-Protokoll (mit Schritt, Datei und Hinweisen).</summary>
    public static void LogResult(SetupLog log, SetupResult result)
    {
        var warnings = result.Warnings is { Count: > 0 } list ? ", Hinweise: " + string.Join(", ", list) : string.Empty;
        if (result.Success)
        {
            log.Info(string.Create(CultureInfo.InvariantCulture, $"Erfolg: {result.PreviousVersion} -> {result.NewVersion}, gestartet: {result.AppStarted}{warnings}"));
        }
        else
        {
            var step = (object?)result.FailedSetupStep ?? result.FailedUninstallStep;
            var where = (step is null ? string.Empty : " im Schritt " + step) + (result.FailedFile is null ? string.Empty : " (" + result.FailedFile + ")");
            log.Error(string.Create(CultureInfo.InvariantCulture, $"Fehler {result.Error}{where}: {result.Message}{warnings}"));
        }
    }
}
