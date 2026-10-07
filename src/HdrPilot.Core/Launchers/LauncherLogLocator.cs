using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Launchers;

/// <summary>
/// Findet die Protokolle installierter Launcher. Ein Launcher zählt als installiert, wenn sein Protokollordner existiert;
/// die Datei selbst darf fehlen (sie entsteht beim nächsten Start des Launchers).
/// <list type="bullet">
/// <item>Epic: <c>%LOCALAPPDATA%\EpicGamesLauncher\Saved\Logs\EpicGamesLauncher.log</c> (UTF-8 mit BOM; beim Start des
/// Launchers wird die alte Datei zu <c>EpicGamesLauncher-backup-&lt;Zeit&gt;.log</c>).</item>
/// <item>Steam: <c>&lt;SteamPath&gt;\logs\console_log.txt</c>, SteamPath aus HKCU\Software\Valve\Steam (gemessen:
/// <c>c:/program files (x86)/steam</c>), sonst HKLM (32 Bit) InstallPath. Steam benennt die Datei beim Start mitunter
/// in <c>console_log.previous.txt</c> um.</item>
/// </list>
/// </summary>
public static class LauncherLogLocator
{
    public const string EpicLogFileName = "EpicGamesLauncher.log";
    public const string SteamLogFileName = "console_log.txt";

    internal const string SteamUserKeyPath = @"Software\Valve\Steam";
    internal const string SteamMachineKeyPath = @"SOFTWARE\Valve\Steam";

    public static IReadOnlyList<LauncherLogSource> Find(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment)
    {
        var sources = new List<LauncherLogSource>(2);
        var localAppData = PathUtil.Normalize(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (Path.IsPathFullyQualified(localAppData))
        {
            var epicLogs = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Logs");
            if (fileSystem.DirectoryExists(epicLogs))
            {
                sources.Add(new LauncherLogSource(LauncherKind.Epic, epicLogs, EpicLogFileName));
            }
        }

        foreach (var steam in SteamDirectories(registry))
        {
            var steamLogs = Path.Combine(steam, "logs");
            if (fileSystem.DirectoryExists(steamLogs))
            {
                sources.Add(new LauncherLogSource(LauncherKind.Steam, steamLogs, SteamLogFileName));
                break;
            }
        }

        return sources;
    }

    private static IEnumerable<string> SteamDirectories(IRegistry registry)
    {
        string? user;
        using (var key = registry.OpenKey(RegistryHiveKind.CurrentUser, SteamUserKeyPath))
        {
            user = key?.GetString("SteamPath");
        }

        string? machine;
        using (var key = registry.OpenKey(RegistryHiveKind.LocalMachine, SteamMachineKeyPath, RegistryViewKind.Registry32))
        {
            machine = key?.GetString("InstallPath");
        }

        foreach (var raw in new[] { user, machine })
        {
            var path = string.IsNullOrWhiteSpace(raw) ? string.Empty : PathUtil.Normalize(raw);
            if (Path.IsPathFullyQualified(path))
            {
                yield return path;
            }
        }
    }
}
