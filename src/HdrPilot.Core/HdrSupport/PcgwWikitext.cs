using System.Text.RegularExpressions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>Was HdrPilot von einer Wiki-Seite braucht: HDR-Angabe und die IDs der Läden (für die Gegenprobe).</summary>
public sealed record PcgwPage(string Title, HdrSupportStatus Status, IReadOnlyList<string> SteamAppIds, IReadOnlyList<string> GogIds)
{
    public bool HasSteamAppId(string appId) => SteamAppIds.Contains(appId, StringComparer.Ordinal);

    public bool HasGogId(string id) => GogIds.Contains(id, StringComparer.Ordinal);
}

/// <summary>
/// Liest die Felder „hdr“ (Vorlage „Video settings“), „steam appid“ und „gogcom id“ (jeweils auch „… side“) aus dem
/// Wikitext einer PCGamingWiki-Seite. Kommentare (&lt;!-- --&gt;) zählen nicht; mehrere IDs sind durch Kommas getrennt.
/// </summary>
public static partial class PcgwWikitext
{
    public static PcgwPage Parse(string title, string? wikitext)
    {
        if (string.IsNullOrEmpty(wikitext))
        {
            return new PcgwPage(title, HdrSupportStatus.Unknown, [], []);
        }

        var text = Comment().Replace(wikitext, string.Empty);
        string? hdr = null;
        var steam = new List<string>();
        var gog = new List<string>();
        foreach (Match m in Field().Matches(text))
        {
            var key = m.Groups["key"].Value.ToLowerInvariant();
            var value = m.Groups["value"].Value;
            if (key == "hdr")
            {
                hdr ??= FirstValue(value);
            }
            else if (key.StartsWith("steam", StringComparison.Ordinal))
            {
                AddIds(steam, value);
            }
            else
            {
                AddIds(gog, value);
            }
        }

        return new PcgwPage(title, HdrSupportValues.Parse(hdr), steam, gog);
    }

    /// <summary>Nur Ziffernfolgen zählen als ID („2093619782 , 1423049311“).</summary>
    public static IReadOnlyList<string> ParseIds(string? value)
    {
        var ids = new List<string>();
        AddIds(ids, value);
        return ids;
    }

    private static void AddIds(List<string> ids, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (Match m in Digits().Matches(value))
        {
            if (!ids.Contains(m.Value, StringComparer.Ordinal))
            {
                ids.Add(m.Value);
            }
        }
    }

    /// <summary>Wert bis zum Ende der Zeile bzw. bis zur nächsten Vorlagengrenze („true}}“).</summary>
    private static string FirstValue(string value)
    {
        var end = value.AsSpan().IndexOfAny('|', '}', '{');
        return (end >= 0 ? value[..end] : value).Trim();
    }

    [GeneratedRegex(@"<!--.*?(?:-->|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(
        @"^[ \t]*\|[ \t]*(?<key>hdr|steam[ \t]+appid(?:[ \t]+side)?|gogcom[ \t]+id(?:[ \t]+side)?)[ \t]*=(?<value>[^\r\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Field();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();
}
