using System.Diagnostics.CodeAnalysis;
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
    /// <summary>
    /// Setup, wenn die eigene EXE ein Paket trägt (Schalter dann nach <see cref="SetupArguments"/>); sonst Entfernen,
    /// wenn die Befehlszeile es verlangt; sonst false (normaler Start, nichts geändert).
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
    /// Optionen ohne Fenster (<c>--quiet</c>): Schalter der Befehlszeile, sonst bisheriger Stand (neu: Autostart an,
    /// Desktop-Verknüpfung nur, wenn schon eine da ist); gestartet wird, außer mit <c>--no-launch</c>.
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
