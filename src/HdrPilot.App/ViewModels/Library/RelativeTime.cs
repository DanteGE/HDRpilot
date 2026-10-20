using HdrPilot.App.Resources;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// „Zuletzt gespielt“ für Liste, Titelbereich und Kontextmenü (Prototyp when()): heute/gestern mit Uhrzeit, bis 6 Tage
/// „vor N Tagen“, bis 34 Tage in Wochen, danach das Datum. Alle Texte aus LibraryStrings.resx; gerechnet wird in
/// Ortszeit nach Kalendertagen.
/// </summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is not { } t)
        {
            return LibraryStrings.When_Never;
        }

        var local = t.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var days = (int)Math.Round((today - local.Date).TotalDays);
        if (days <= 0)
        {
            return LibraryText.Format(LibraryStrings.When_Today, local);
        }

        if (days == 1)
        {
            return LibraryText.Format(LibraryStrings.When_Yesterday, local);
        }

        if (days < 7)
        {
            return LibraryText.Format(LibraryStrings.When_Days, days);
        }

        if (days < 35)
        {
            var weeks = (int)Math.Round(days / 7.0, MidpointRounding.AwayFromZero);
            return weeks == 1 ? LibraryStrings.When_OneWeek : LibraryText.Format(LibraryStrings.When_Weeks, weeks);
        }

        return LibraryText.Format(LibraryStrings.When_Date, local);
    }

    /// <summary>Kleine Überschrift im Titelbereich: neu, noch nicht gespielt oder zuletzt gespielt (Großschreibung macht die Ansicht).</summary>
    public static string Overline(bool isNew, DateTimeOffset? firstSeen, DateTimeOffset? lastPlayed, DateTimeOffset now)
    {
        if (isNew)
        {
            return LibraryText.Format(LibraryStrings.Hero_OverlineNew, Format(firstSeen, now));
        }

        return lastPlayed is null
            ? LibraryStrings.Hero_OverlineNeverPlayed
            : LibraryText.Format(LibraryStrings.Hero_OverlineLastPlayed, Format(lastPlayed, now));
    }
}
