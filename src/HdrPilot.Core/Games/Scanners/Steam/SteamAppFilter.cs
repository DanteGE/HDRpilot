using System.Text.RegularExpressions;

namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>
/// Erkennt Steam-Einträge ohne Spielcharakter (Werkzeuge, Laufzeitumgebungen, Anwendungen).
/// Reihenfolge der Regeln:
/// <list type="number">
/// <item>Pflegbare Liste <see cref="NonGameAppIds"/>: gilt immer.</item>
/// <item>App-Typ aus appinfo.vdf: Typen aus <see cref="NonGameTypes"/> fallen heraus; "Game", "Demo" und "Beta"
/// (Playtests) bleiben. Steht ein Typ im Cache, entscheidet er allein.</item>
/// <item>Nur ohne bekannten Typ (Cache fehlt, ist defekt oder kennt die App nicht): Namensregeln
/// <see cref="NonGameNamePattern"/>.</item>
/// </list>
/// Ein Spiel zu verstecken wäre schlimmer als ein Werkzeug zu zeigen; deshalb bleiben unbekannte Typen und
/// Einträge ohne Typ (etwa Playtests ohne öffentliche Daten) in der Liste.
/// </summary>
internal static partial class SteamAppFilter
{
    /// <summary>
    /// AppIDs ohne Spielcharakter, am Zielrechner gefunden (Typ laut appinfo.vdf in Klammern). Erweiterbar:
    /// eine Zeile je App. Greift auch, wenn appinfo.vdf fehlt.
    /// </summary>
    public static IReadOnlyDictionary<uint, string> NonGameAppIds { get; } = new Dictionary<uint, string>
    {
        [228980] = "Steamworks Common Redistributables (Tool)",
        [250820] = "SteamVR (Tool)",
        [431960] = "Wallpaper Engine (Application)",
        [993090] = "Lossless Scaling (Application)",
        [1366800] = "Crosshair X (Application)",
        [3477300] = "Scope X (Application)",
    };

    /// <summary>App-Typen aus appinfo.vdf (<c>common/type</c>), die kein Spiel sind. Vergleich ohne Groß-/Kleinschreibung.</summary>
    public static IReadOnlySet<string> NonGameTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Tool",
        "Application",
        "Config",
        "DLC",
        "Music",
        "Video",
        "Media",
        "Driver",
        "Hardware",
        "Guide",
        "Plugin",
        "Series",
        "Comic",
        "Franchise",
        "Advertising",
    };

    /// <summary>
    /// Namensregeln für Werkzeuge, deren Typ unbekannt ist: Redistributables, Dedicated Server, Steamworks SDK,
    /// Steam Linux Runtime, Proton und SteamVR.
    /// </summary>
    [GeneratedRegex(
        @"Redistributable|Dedicated Server|^Steamworks SDK|^Steam Linux Runtime|^SteamVR\b|^Proton (\d|Experimental|Hotfix|Next|BattlEye|EasyAntiCheat)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex NonGameNamePattern();

    /// <summary>Grund, warum der Eintrag kein Spiel ist, oder null für ein Spiel.</summary>
    public static string? GetNonGameReason(uint appId, string name, string? appInfoType)
    {
        if (NonGameAppIds.TryGetValue(appId, out var listed))
        {
            return "feste Liste: " + listed;
        }

        if (!string.IsNullOrWhiteSpace(appInfoType))
        {
            return NonGameTypes.Contains(appInfoType.Trim()) ? "App-Typ " + appInfoType.Trim() : null;
        }

        return NonGameNamePattern().IsMatch(name) ? "Namensregel" : null;
    }
}
