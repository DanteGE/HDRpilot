using System.IO;
using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Setup;

/// <summary>
/// Wohin der Setup installiert (E83). Normal: für den angemeldeten Benutzer (Programmordner unter
/// %LOCALAPPDATA%\Programs, Startmenü und Desktop des Benutzers, HKCU). Prüfläufe: alles unter einem Testordner
/// (<c>--test-root</c>) und ein eigener Registry-Zweig; dann berührt nichts die echte Installation.
/// </summary>
public sealed record SetupLocations(
    string InstallDirectory,
    string ProgramsDirectory,
    string DesktopDirectory,
    string RegistryRoot,
    AppPaths DataPaths,
    string? ConfigDirectoryArgument)
{
    public const string ExeName = "HDRpilot.exe";
    public const string ShortcutName = "HDRpilot.lnk";
    public const string UninstallKeyName = "HDRpilot";

    /// <summary>Dateien, die der Setup installiert und beim Entfernen löscht (relativ zum Programmordner).</summary>
    public static readonly IReadOnlyList<string> InstalledFiles = [ExeName, "README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", @"cli\hdrpilot.exe"];

    /// <summary>Normale Registry-Wurzel; darunter „Uninstall“ und „Run“.</summary>
    public const string WindowsRegistryRoot = @"Software\Microsoft\Windows\CurrentVersion";

    public bool IsTest => ConfigDirectoryArgument is not null;

    public string ExePath => Path.Combine(InstallDirectory, ExeName);

    public string StartMenuShortcut => Path.Combine(ProgramsDirectory, ShortcutName);

    public string DesktopShortcut => Path.Combine(DesktopDirectory, ShortcutName);

    /// <summary>Ordner der Spiele-Verknüpfungen, die die App selbst anlegt (Startmenü\Programme\HDRpilot).</summary>
    public string GameShortcutsDirectory => Path.Combine(ProgramsDirectory, Services.ShortcutService.StartMenuFolderName);

    public string UninstallKeyPath => RegistryRoot + @"\Uninstall\" + UninstallKeyName;

    public string RunKeyPath => RegistryRoot + @"\Run";

    /// <summary>Kennung der Einzelinstanz, die zu dieser Installation gehört (normale oder Test-Ablage).</summary>
    public string InstanceId => Infrastructure.SingleInstance.IdFor(ConfigDirectoryArgument);

    /// <summary>
    /// Wo die App dieser Installation Auto HDR schreibt (E88): normal der echte Schlüssel von Windows, im Prüflauf die
    /// Testwurzel ihrer Ablage (dieselbe, die die App mit <c>--config-dir</c> ohne <c>--autohdr-test-root</c> benutzt).
    /// </summary>
    public AutoHdrRegistryLocation AutoHdrLocation => ConfigDirectoryArgument is null
        ? AutoHdrRegistryLocation.Real
        : AutoHdrRegistryLocation.ForConfigDirectory(ConfigDirectoryArgument);

    /// <summary>Zusätzliche Argumente beim Start der installierten App (nur im Prüflauf: die Test-Ablage).</summary>
    public string ExtraLaunchArguments => ConfigDirectoryArgument is null ? string.Empty : "--config-dir \"" + ConfigDirectoryArgument + "\"";

    public static SetupLocations Default(IAppEnvironment environment) => new(
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HDRpilot"),
        environment.GetFolderPath(Environment.SpecialFolder.Programs),
        environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        WindowsRegistryRoot,
        AppPaths.Default(environment),
        null);

    /// <summary>Prüflauf: alles unter <paramref name="root"/>, Registry unter HKCU\Software\HDRpilot.Tests\Setup\&lt;Hash&gt;.</summary>
    public static SetupLocations ForTestRoot(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
        var config = Path.Combine(full, "Config");
        return new SetupLocations(
            Path.Combine(full, "Programs", "HDRpilot"),
            Path.Combine(full, "StartMenu", "Programs"),
            Path.Combine(full, "Desktop"),
            @"Software\HDRpilot.Tests\Setup\" + hash,
            AppPaths.InDirectory(config),
            config);
    }
}
