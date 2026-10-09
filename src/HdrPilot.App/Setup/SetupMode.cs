using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using HdrPilot.App.Setup.UI;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.App.Setup;

/// <summary>Als was die EXE läuft, wenn nicht als normale App (E83).</summary>
public enum SetupModeKind
{
    /// <summary>Setup-Datei: gültiger Abschluss an der eigenen EXE.</summary>
    Setup,

    /// <summary>Installierte EXE mit <c>--uninstall</c>: Rückfrage, dann übernimmt eine Kopie im Temp-Ordner.</summary>
    Uninstall,

    /// <summary>Kopie im Temp-Ordner (<c>--uninstall-continue</c>): entfernt die Installation.</summary>
    UninstallContinue,
}

/// <summary>
/// Weiche beim Start (E83): Setup-Datei, Entfernen oder normale App. Das Erkennen kostet nur das Lesen der letzten
/// 64 Bytes der eigenen EXE. Setup und Entfernen nehmen nie die Einzelinstanz der App, starten weder Automatik noch
/// Tray und fassen die Konfiguration nicht an.
/// </summary>
public sealed record SetupMode(SetupModeKind Kind, SetupArguments Arguments, SetupBundle? Bundle)
{
    private const string TestRootSwitch = "--test-root";

    /// <summary>
    /// Setup, wenn die eigene EXE ein Paket trägt (Schalter dann nach <see cref="SetupArguments"/>; solche zum Entfernen
    /// lehnt der Lauf ab, siehe <see cref="RefusedArguments"/>); sonst Entfernen, wenn die Befehlszeile es verlangt;
    /// sonst false (normaler Start, nichts geändert).
    /// </summary>
    public static bool TryDetect(string? processPath, IReadOnlyList<string> args, [NotNullWhen(true)] out SetupMode? mode)
    {
        // Hinter "--" steht der Spielbefehl der Steam-Startoption: nie als eigene Schalter lesen.
        var own = args.TakeWhile(a => a != "--").ToList();
        if (SetupBundle.TryOpen(processPath) is { } bundle)
        {
            mode = new SetupMode(SetupModeKind.Setup, SetupArguments.Parse(own), bundle);
            return true;
        }

        var parsed = SetupArguments.Parse(own);
        if (parsed.IsUninstall)
        {
            mode = new SetupMode(parsed.UninstallContinue ? SetupModeKind.UninstallContinue : SetupModeKind.Uninstall, parsed, null);
            return true;
        }

        mode = null;
        return false;
    }

    /// <summary>Führt Setup oder Entfernen aus (mit Fenster oder still) und liefert den Exit-Code (<see cref="SetupExitCodes"/>).</summary>
    public static Task<int> RunAsync(Application app, SetupMode mode) => SetupRunner.RunAsync(app, mode);

    /// <summary>Normale Orte, im Prüflauf (<c>--test-root</c>) alles unter dem Testordner.</summary>
    public static SetupLocations LocationsFor(SetupArguments args) =>
        args.TestRoot is { } root ? SetupLocations.ForTestRoot(root) : SetupLocations.Default(SystemEnvironment.Instance);

    /// <summary>
    /// Schalter, wegen derer der Lauf nichts ändert (Exit-Code <see cref="SetupExitCodes.BadArguments"/>, mit Fenster nur
    /// eine Fehlermeldung); leer: weiter. Lieber nichts tun als etwas anderes als verlangt:
    /// <list type="bullet">
    /// <item><c>--test-root</c> ohne Ordner (Windows PowerShell 5.1 lässt leere Argumente weg) oder mit einem Schalter
    /// als Ordner: sonst träfe der Lauf die echte Installation oder liefe anders als verlangt. In jeder Art.</item>
    /// <item>Setup-Datei mit Schaltern zum Entfernen: sie installierte sonst, statt zu entfernen.</item>
    /// <item>Ohne Fenster jeder unbekannte Schalter; ebenso in der Kopie zum Entfernen, deren Befehlszeile von der
    /// installierten EXE kommt.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> RefusedArguments(SetupModeKind kind, SetupArguments args)
    {
        var refused = new List<string>();
        refused.AddRange(args.Quiet || kind == SetupModeKind.UninstallContinue
            ? args.Unknown
            : args.Unknown.Where(a => a.StartsWith(TestRootSwitch, StringComparison.OrdinalIgnoreCase)));

        if (args.TestRoot is { } root && root.StartsWith("--", StringComparison.Ordinal))
        {
            refused.Add(TestRootSwitch);
            refused.Add(root);
        }

        if (kind == SetupModeKind.Setup)
        {
            if (args.Uninstall)
            {
                refused.Add("--uninstall");
            }

            if (args.UninstallContinue)
            {
                refused.Add("--uninstall-continue");
            }

            if (args.RemoveData)
            {
                refused.Add("--remove-data");
            }

            if (args.WaitPid is { } pid)
            {
                refused.Add("--wait-pid");
                refused.Add(pid.ToString(CultureInfo.InvariantCulture));
            }
        }

        return refused;
    }

    /// <summary>
    /// Optionen ohne Fenster (<c>--quiet</c>): Schalter der Befehlszeile, sonst bisheriger Stand (neu: Autostart an,
    /// Desktop-Verknüpfung nur, wenn schon eine da ist); gestartet wird, außer mit <c>--no-launch</c> (und nie, wenn der
    /// Setup mit Adminrechten läuft; das Ergebnis bleibt dann Ok).
    /// </summary>
    public static InstallOptions QuietOptions(SetupArguments args, SetupInspection inspection) => new(
        args.Autostart ?? (inspection.Kind == SetupKind.Install || inspection.AutostartEnabled),
        args.DesktopShortcut ?? inspection.DesktopShortcutExists,
        !args.NoLaunch,
        args.AllowDowngrade);

    /// <summary>Exit-Code zu einem Ergebnis von Installation oder Entfernen.</summary>
    public static int ExitCodeFor(SetupResult result) => result.Success
        ? SetupExitCodes.Ok
        : result.Error switch
        {
            SetupErrorKind.DowngradeNotAllowed => SetupExitCodes.DowngradeRefused,
            SetupErrorKind.AppStillRunning => SetupExitCodes.AppStillRunning,
            SetupErrorKind.NotInstalled => SetupExitCodes.NotInstalled,
            _ => SetupExitCodes.Failed,
        };
}
