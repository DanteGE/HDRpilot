using System.IO;
using HdrPilot.App.Setup;

namespace HdrPilot.App.Services;

/// <summary>
/// Steht die HDRpilot-Startoption (<see cref="SteamLaunchOption"/>) bei einem Steam-Spiel schon in Steam (G9.7, Angebot
/// beim Einschalten von „Windows Auto HDR“)? Liest nur, mit demselben Leser wie die Warnung beim Entfernen
/// (<see cref="SteamLaunchOptionScanner"/>): <c>localconfig.vdf</c> aller Steam-Konten. Treffer ist eine Startoption mit
/// dem EXE-Namen von HDRpilot und <c>--wrap</c>. Fehlt Steam oder ist die Datei nicht lesbar, zählt das als „fehlt“; dann
/// bietet HDRpilot die Startoption an, was nichts kaputt macht.
/// </summary>
public static class SteamLaunchOptionStatus
{
    /// <param name="steamPath">Liefert den Steam-Ordner (App: HKCU\Software\Valve\Steam, Wert SteamPath; Tests: Testordner).</param>
    /// <param name="executablePath">Pfad der laufenden HDRpilot.exe; ihr Dateiname wird gesucht (sonst „HDRpilot.exe“).</param>
    public static bool IsSet(Func<string?> steamPath, string? executablePath, string appId)
    {
        ArgumentNullException.ThrowIfNull(steamPath);
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        var exeName = Path.GetFileName(executablePath);

        // Namen der Spiele braucht es hier nicht: keine config.json.
        var scanner = new SteamLaunchOptionScanner(steamPath, configFile: string.Empty, log: null, string.IsNullOrWhiteSpace(exeName) ? SetupLocations.ExeName : exeName);
        return scanner.FindAppIds().Contains(appId.Trim(), StringComparer.Ordinal);
    }
}
