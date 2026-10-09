namespace HdrPilot.App.Setup;

/// <summary>Was ein Setup-Lauf gegenüber der vorhandenen Installation tut.</summary>
public enum SetupKind
{
    /// <summary>Noch nicht installiert.</summary>
    Install,

    /// <summary>Ältere Version installiert.</summary>
    Update,

    /// <summary>Gleiche Version installiert (Reparatur).</summary>
    Reinstall,

    /// <summary>Neuere Version installiert; nur auf ausdrücklichen Wunsch.</summary>
    Downgrade,
}

/// <summary>Stand vor dem Setup: was installiert ist und welche Optionen vorbelegt werden.</summary>
/// <param name="SetupVersion">Version dieser Setup-Datei.</param>
/// <param name="InstalledVersion">Version der installierten EXE; null, wenn keine da ist.</param>
/// <param name="Registered">Eintrag unter „Installierte Apps“ vorhanden (fehlt etwa bei einer von Hand kopierten EXE).</param>
/// <param name="AutostartEnabled">
/// Autostart-Wert startet eine HDRpilot.exe, die installierte oder eine andere Kopie (Vorbelegung bei Aktualisierung;
/// neu: an). „An“ biegt ihn auf die installierte EXE um, „aus“ entfernt ihn.
/// </param>
/// <param name="DesktopShortcutExists">Desktop-Verknüpfung „HDRpilot“ auf die installierte EXE vorhanden.</param>
/// <param name="AppRunning">HDRpilot läuft (wird vor dem Kopieren beendet und danach wieder gestartet).</param>
/// <param name="Elevated">
/// Der Setup läuft mit Adminrechten: er startet HDRpilot danach nicht (<see cref="SetupWarning.NotLaunchedElevated"/>),
/// die Fortschrittsliste führt den Schritt deshalb nicht.
/// </param>
public sealed record SetupInspection(
    Version SetupVersion,
    Version? InstalledVersion,
    bool Registered,
    string InstallDirectory,
    bool AutostartEnabled,
    bool DesktopShortcutExists,
    bool AppRunning,
    bool Elevated = false)
{
    public SetupKind Kind => InstalledVersion is null ? SetupKind.Install
        : InstalledVersion < SetupVersion ? SetupKind.Update
        : InstalledVersion == SetupVersion ? SetupKind.Reinstall
        : SetupKind.Downgrade;
}

/// <param name="Autostart">
/// Mit Windows starten (Run-Wert auf die installierte EXE). Sonst wird der Wert „HDRpilot“ entfernt, wenn er eine
/// HDRpilot.exe startet (auch eine andere Kopie: der Wertname gehört HDRpilot); Werte für andere Programme bleiben.
/// </param>
/// <param name="DesktopShortcut">Desktop-Verknüpfung „HDRpilot“ anlegen (sonst eine eigene entfernen).</param>
/// <param name="LaunchAfterInstall">
/// Danach starten (neu: mit Fenster; lief die App vorher, startet sie wieder im Infobereich). Nie, wenn der Setup mit
/// Adminrechten läuft (<see cref="SetupWarning.NotLaunchedElevated"/>).
/// </param>
/// <param name="AllowDowngrade">Ältere Version über eine neuere installieren.</param>
public sealed record InstallOptions(bool Autostart, bool DesktopShortcut, bool LaunchAfterInstall, bool AllowDowngrade = false);

public enum SetupStep
{
    CheckSetupFile,
    StopRunningApp,
    CopyFiles,
    Shortcuts,
    Autostart,
    Register,
    LaunchApp,
}

public enum UninstallStep
{
    StopRunningApp,
    RestoreHdr,
    RemoveFiles,
    RemoveShortcuts,
    RemoveAutostart,
    Unregister,
    RemoveData,
}

public enum SetupErrorKind
{
    /// <summary>Setup-Datei beschädigt oder unvollständig (Prüfsumme stimmt nicht).</summary>
    SetupFileDamaged,

    /// <summary>HDRpilot ließ sich nicht beenden (etwa hängt); der Nutzer soll es beenden und es erneut versuchen.</summary>
    AppStillRunning,

    /// <summary>Eine Datei im Programmordner ist gesperrt.</summary>
    FilesInUse,

    AccessDenied,

    DiskFull,

    /// <summary>Neuere Version installiert und kein ausdrücklicher Wunsch.</summary>
    DowngradeNotAllowed,

    /// <summary>Es läuft schon ein Setup oder Entfernen.</summary>
    AnotherSetupRunning,

    /// <summary>Beim Entfernen: nichts installiert.</summary>
    NotInstalled,

    Unexpected,
}

/// <summary>Hinweis zu einem Ergebnis, das trotzdem gelten kann (Erfolg bleibt Erfolg).</summary>
public enum SetupWarning
{
    /// <summary>Entfernen: der gemerkte HDR-Zustand ließ sich nicht wiederherstellen (session-state.json bleibt).</summary>
    HdrNotRestored,

    /// <summary>Setup lief mit Adminrechten: HDRpilot wurde bewusst nicht gestartet (es liefe sonst ebenso erhöht).</summary>
    NotLaunchedElevated,
}

/// <summary>Ergebnis von Installation oder Entfernen. Bei Fehlern ist der vorige Zustand wiederhergestellt.</summary>
/// <param name="Message">Technische Angabe zum Fehler (Protokoll; die Oberfläche nimmt ihre Texte aus den Ressourcen) oder null.</param>
/// <param name="FailedUninstallStep">Entfernen: Schritt des ersten Fehlers (das Entfernen macht danach weiter, wo es sicher ist).</param>
/// <param name="FailedSetupStep">Setup: Schritt, in dem der Fehler auftrat.</param>
/// <param name="Warnings">Hinweise neben dem Ergebnis (auch bei Erfolg), sonst null.</param>
/// <param name="FailedFile">Datei, die der Fehler betrifft (etwa eine gesperrte Datei beim Entfernen), sonst null.</param>
public sealed record SetupResult(
    bool Success,
    SetupErrorKind? Error,
    string? Message,
    Version? PreviousVersion,
    Version? NewVersion,
    bool AppStarted,
    UninstallStep? FailedUninstallStep = null,
    SetupStep? FailedSetupStep = null,
    IReadOnlyList<SetupWarning>? Warnings = null,
    string? FailedFile = null)
{
    public static SetupResult Failed(SetupErrorKind error, string message, Version? previous = null) => new(false, error, message, previous, null, false);

    public bool Has(SetupWarning warning) => Warnings?.Contains(warning) == true;
}

/// <summary>Installation und Aktualisierung (Logik ohne Oberfläche).</summary>
public interface ISetupEngine
{
    SetupLocations Locations { get; }

    SetupInspection Inspect();

    Task<SetupResult> InstallAsync(InstallOptions options, IProgress<SetupStep>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Stand vor dem Entfernen.</summary>
/// <param name="SteamGamesWithLaunchOption">Steam-Spiele, deren Startoption noch HDRpilot aufruft (Namen, sonst „Steam-App &lt;ID&gt;“ aus den Ressourcen).</param>
public sealed record UninstallInspection(
    string InstallDirectory,
    Version? InstalledVersion,
    IReadOnlyList<string> SteamGamesWithLaunchOption,
    string ConfigDirectory,
    string DataDirectory,
    bool AppRunning);

/// <param name="RemoveData">Auch Einstellungen, Spieleliste, Protokolle und HDR-Liste löschen (Standard: behalten).</param>
public sealed record UninstallOptions(bool RemoveData);

/// <summary>Entfernen (Logik ohne Oberfläche). Läuft aus einer Kopie außerhalb des Programmordners.</summary>
public interface IUninstallEngine
{
    SetupLocations Locations { get; }

    UninstallInspection Inspect();

    Task<SetupResult> UninstallAsync(UninstallOptions options, IProgress<UninstallStep>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Erzeugt die echten Engines (Windows-Registry, Shell-Verknüpfungen, Befehlsweg zur laufenden App). Implementiert in
/// <c>SetupEngine.cs</c> und <c>UninstallEngine.cs</c>; die Oberfläche kennt nur die Schnittstellen.
/// </summary>
public static partial class SetupEngines
{
    public static partial ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log);

    public static partial IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log);
}
