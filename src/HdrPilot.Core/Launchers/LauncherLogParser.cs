using System.Globalization;
using System.Text.RegularExpressions;
using HdrPilot.Core.Games;

namespace HdrPilot.Core.Launchers;

/// <summary>
/// Erkennt angekündigte Spielstarts in Protokollzeilen der Launcher. Formate am Zielrechner geprüft (2026-10-07):
/// <list type="bullet">
/// <item>Epic, Start aus der Bibliothek: <c>LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received
/// &lt;Namespace&gt;:&lt;Katalog&gt;:&lt;AppName&gt; library</c>, gemessen 1,8 s vor dem Spielprozess.</item>
/// <item>Epic, Start über Verknüpfung oder URI (auch der Start über HdrPilot): <c>LogUriHandler: Started processing URI
/// [… com.epicgames.launcher://apps/&lt;Namespace&gt;%3A&lt;Katalog&gt;%3A&lt;AppName&gt;?action=launch&amp;silent=true …]</c>.</item>
/// <item>Steam (<c>console_log.txt</c>): <c>GameAction [AppID &lt;ID&gt;, ActionID &lt;n&gt;] : LaunchApp changed task to …</c>;
/// die erste Zeile eines Startvorgangs steht 1 bis 2 s vor dem Spielprozess, länger, wenn Steam nachfragt.</item>
/// </list>
/// Der Schlüssel ist „Epic:&lt;AppName&gt;“ (wie <see cref="Games.Scanners.EpicScanner"/>) bzw. „Steam:&lt;AppID&gt;“.
/// Zeilen desselben Starts enthalten Anmeldecodes und Benutzernamen: Ergebnis sind nur Schlüssel und Nummer des
/// Startvorgangs, die Zeile selbst wird nirgends weitergegeben oder protokolliert.
/// </summary>
public static partial class LauncherLogParser
{
    private const int MaxAppNameLength = 128;

    /// <summary>Byte-Vorauswahl: Nur Zeilen mit einem dieser Marker werden überhaupt dekodiert und geprüft.</summary>
    private static ReadOnlySpan<byte> EpicLibraryMarker => "LaunchWithOptions: Received "u8;

    private static ReadOnlySpan<byte> EpicUriMarker => "LogUriHandler: Started processing URI"u8;

    private static ReadOnlySpan<byte> SteamMarker => "GameAction [AppID "u8;

    /// <summary>Billige Vorprüfung auf Byte-Ebene (ohne Dekodieren), ob die Zeile ein Startsignal sein kann.</summary>
    public static bool IsCandidate(LauncherKind launcher, ReadOnlySpan<byte> line) => launcher switch
    {
        LauncherKind.Epic => line.IndexOf(EpicLibraryMarker) >= 0 || line.IndexOf(EpicUriMarker) >= 0,
        LauncherKind.Steam => line.IndexOf(SteamMarker) >= 0,
        _ => false,
    };

    /// <summary>Angekündigter Spielstart in dieser Zeile, sonst null.</summary>
    public static LauncherSignal? Parse(LauncherKind launcher, string line) => launcher switch
    {
        LauncherKind.Epic => ParseEpic(line),
        LauncherKind.Steam => ParseSteam(line),
        _ => null,
    };

    private static LauncherSignal? ParseEpic(string line)
    {
        var library = EpicLibraryRegex().Match(line);
        if (library.Success)
        {
            return EpicSignal(library.Groups["id"].Value);
        }

        var uri = EpicUriRegex().Match(line);
        if (uri.Success && IsLaunchAction(uri.Groups["query"].Value))
        {
            // Doppelpunkte sind in der URI als %3A kodiert.
            return EpicSignal(Uri.UnescapeDataString(uri.Groups["path"].Value));
        }

        return null;
    }

    private static LauncherSignal? ParseSteam(string line)
    {
        var match = SteamRegex().Match(line);
        if (!match.Success
            || !uint.TryParse(match.Groups["app"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            || appId == 0
            || !long.TryParse(match.Groups["action"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var actionId))
        {
            return null;
        }

        return new LauncherSignal(LauncherKind.Steam, GameKey.Create(GameSource.Steam, appId.ToString(CultureInfo.InvariantCulture)), actionId);
    }

    /// <summary>„Namespace:Katalog:AppName“ oder nur „AppName“: der letzte Teil ist der AppName.</summary>
    private static LauncherSignal? EpicSignal(string id)
    {
        var appName = id[(id.LastIndexOf(':') + 1)..];
        return IsValidAppName(appName) ? new LauncherSignal(LauncherKind.Epic, GameKey.Create(GameSource.Epic, appName)) : null;
    }

    /// <summary>Nur <c>action=launch</c> startet ein Spiel (nicht etwa install oder verify).</summary>
    private static bool IsLaunchAction(string query)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, "action=launch", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Epic-AppNames sind kurze Bezeichner aus Buchstaben, Ziffern, Punkt, Unter- und Bindestrich.</summary>
    private static bool IsValidAppName(string appName)
    {
        if (appName.Length is 0 or > MaxAppNameLength || !char.IsAsciiLetterOrDigit(appName[0]))
        {
            return false;
        }

        foreach (var c in appName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"LaunchWithOptions: Received (?<id>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex EpicLibraryRegex();

    [GeneratedRegex(@"LogUriHandler: Started processing URI \[.*?com\.epicgames\.launcher://apps/(?<path>[^?\s""\]]+)\?(?<query>[^\s""\]]*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EpicUriRegex();

    [GeneratedRegex(@"GameAction \[AppID (?<app>[0-9]{1,10}), ActionID (?<action>[0-9]{1,18})\] : LaunchApp\b", RegexOptions.CultureInvariant)]
    private static partial Regex SteamRegex();
}
