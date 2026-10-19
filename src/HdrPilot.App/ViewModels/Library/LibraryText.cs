using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Formatierte Texte der Spielebibliothek (alle Vorlagen aus LibraryStrings.resx).</summary>
public static class LibraryText
{
    public static string Format(string template, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);

    public static string Games(int count) =>
        count == 1 ? LibraryStrings.Count_GamesOne : Format(LibraryStrings.Count_GamesMany, count);

    /// <summary>„1 nicht installiertes Spiel“, „5 nicht installierte Spiele“ (ausgeblendet, E90).</summary>
    public static string HiddenGames(int count) =>
        count == 1 ? LibraryStrings.Count_HiddenOne : Format(LibraryStrings.Count_HiddenMany, count);

    /// <summary>„Eine Datei ließ sich nicht löschen …“, „3 Dateien ließen sich nicht löschen …“ (nach „Einstellungen entfernen…“, E90).</summary>
    public static string FilesFailed(int count) =>
        count == 1 ? LibraryStrings.RemoveSettings_FilesFailedOne : Format(LibraryStrings.RemoveSettings_FilesFailed, count);

    /// <summary>HDR laut PCGamingWiki in Worten (Titelbereich, Karte, Detailbereich); nie „SDR“.</summary>
    public static string HdrKindLabel(HdrSupportStatus status) => status switch
    {
        HdrSupportStatus.Native => LibraryStrings.HdrKind_Native,
        HdrSupportStatus.Limited => LibraryStrings.HdrKind_Limited,
        HdrSupportStatus.ModOnly => LibraryStrings.HdrKind_ModOnly,
        HdrSupportStatus.None => LibraryStrings.HdrKind_None,
        _ => LibraryStrings.HdrKind_Unknown,
    };

    /// <summary>HDR-Angabe als Kennzeichen in Großbuchstaben (Titelbereich, Detailbereich, Karte): „NATIVES HDR“ …</summary>
    public static string HdrTag(HdrSupportStatus status) => status switch
    {
        HdrSupportStatus.Native => LibraryStrings.HdrTag_Native,
        HdrSupportStatus.Limited => LibraryStrings.HdrTag_Limited,
        HdrSupportStatus.ModOnly => LibraryStrings.HdrTag_ModOnly,
        HdrSupportStatus.None => LibraryStrings.HdrTag_None,
        _ => LibraryStrings.HdrTag_Unknown,
    };

    /// <summary>Kurzform für die Spalte „HDR“ der Liste: „Nativ“, „Teilweise“, „Mod“; sonst leer (dann ein Strich).</summary>
    public static string HdrPill(HdrSupportStatus status) => status switch
    {
        HdrSupportStatus.Native => LibraryStrings.HdrPill_Native,
        HdrSupportStatus.Limited => LibraryStrings.HdrPill_Limited,
        HdrSupportStatus.ModOnly => LibraryStrings.HdrPill_ModOnly,
        _ => string.Empty,
    };

    /// <summary>
    /// Zwei Buchstaben aus dem Namen (Prototyp mono()): besitzanzeigendes „'s“ und Satzzeichen weg, Bindestriche zählen nicht;
    /// erster Buchstabe der ersten beiden Wörter, bei einem Wort dessen erste beiden. „Baldur's Gate 3“ → „BG“, „SWAT 3“ → „S3“.
    /// </summary>
    public static string Monogram(string? name)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(name ?? string.Empty, @"['’]s\b", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[™®©:’'""!?.,\-–—()\[\]]", " ");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetterOrDigit))
            .ToList();
        if (words.Count == 0)
        {
            return "?";
        }

        var first = words[0];
        var second = words.Count > 1 ? words[1][0].ToString() : first.Length > 1 ? first[1].ToString() : string.Empty;
        return (first[0] + second).ToUpper(CultureInfo.CurrentCulture);
    }

    public static string SourceName(GameSource source) => source switch
    {
        GameSource.Steam => LibraryStrings.Source_Steam,
        GameSource.Epic => LibraryStrings.Source_Epic,
        GameSource.Xbox => LibraryStrings.Source_Xbox,
        GameSource.Gog => LibraryStrings.Source_Gog,
        GameSource.Ubisoft => LibraryStrings.Source_Ubisoft,
        GameSource.EA => LibraryStrings.Source_EA,
        GameSource.BattleNet => LibraryStrings.Source_BattleNet,
        GameSource.Manual => LibraryStrings.Source_Manual,
        _ => source.ToString(),
    };
}
