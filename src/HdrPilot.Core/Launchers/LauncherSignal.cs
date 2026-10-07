namespace HdrPilot.Core.Launchers;

/// <summary>Launcher, deren Protokolle die Frühwarnung liest.</summary>
public enum LauncherKind
{
    /// <summary>Epic Games Launcher: <c>%LOCALAPPDATA%\EpicGamesLauncher\Saved\Logs\EpicGamesLauncher.log</c>.</summary>
    Epic,

    /// <summary>Steam: <c>&lt;Steam-Ordner&gt;\logs\console_log.txt</c>.</summary>
    Steam,
}

/// <summary>
/// Ein Launcher hat einen Spielstart angekündigt. Enthält nur abgeleitete Werte (Schlüssel, Nummer des Startvorgangs),
/// nie die Protokollzeile: Zeilen desselben Starts enthalten Anmeldecodes und Benutzernamen.
/// </summary>
/// <param name="GameKey">Schlüssel wie in der Konfiguration, etwa „Epic:Crow“ oder „Steam:3008130“.</param>
/// <param name="ActionId">Steam: Nummer des Startvorgangs (<c>GameAction … ActionID</c>), sonst null.</param>
public sealed record LauncherSignal(LauncherKind Launcher, string GameKey, long? ActionId = null);

/// <summary>Protokolldatei eines installierten Launchers, die die Frühwarnung beobachtet.</summary>
public sealed record LauncherLogSource(LauncherKind Launcher, string Directory, string FileName)
{
    public string FullPath => Path.Combine(Directory, FileName);
}
