using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Texte der Bibliothek auf Englisch (E95): Einzahl und Mehrzahl, „vor N Tagen“, Datum und Uhrzeit im englischen Format,
/// Quellen und HDR-Angaben. Die Vorlagen kommen aus LibraryStrings.en.resx, gerechnet wird wie auf Deutsch.
/// </summary>
public sealed class EnglishTextTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 10, 16, 20, 0, 0, DateTimeKind.Local));

    private static DateTimeOffset At(int daysAgo, int hour = 21, int minute = 15) =>
        new(new DateTime(2026, 10, 16, hour, minute, 0, DateTimeKind.Local).AddDays(-daysAgo));

    private static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    [Fact]
    public void Counts_use_singular_and_plural()
    {
        using var _ = TestCulture.Use("en-US");

        Assert.Equal("1 game", LibraryText.Games(1));
        Assert.Equal("2 games", LibraryText.Games(2));
        Assert.Equal("0 games", LibraryText.Games(0));
        Assert.Equal("1234 games", LibraryText.Games(1234)); // {0} ohne Tausendertrennung wie auf Deutsch
        Assert.Equal("1 uninstalled game", LibraryText.HiddenGames(1));
        Assert.Equal("5 uninstalled games", LibraryText.HiddenGames(5));
        Assert.Equal("One file couldn't be deleted (see log).", LibraryText.FilesFailed(1));
        Assert.Equal("3 files couldn't be deleted (see log).", LibraryText.FilesFailed(3));
    }

    [Fact]
    public void Relative_time_reads_naturally_in_english()
    {
        using var _ = TestCulture.Use("en-US");

        Assert.Equal("never played", RelativeTime.Format(null, Now));
        // Uhrzeit als „t“ der Kultur (je nach ICU mit schmalem Leerzeichen vor PM), deshalb hier mit derselben Formatierung.
        Assert.Equal("today, " + Time(At(0, 18, 5)), RelativeTime.Format(At(0, 18, 5), Now));
        Assert.Contains("PM", RelativeTime.Format(At(0, 18, 5), Now), StringComparison.Ordinal);
        Assert.Equal("yesterday, " + Time(At(1)), RelativeTime.Format(At(1), Now));
        Assert.Equal("3 days ago", RelativeTime.Format(At(3), Now));
        Assert.Equal("1 week ago", RelativeTime.Format(At(7), Now));
        Assert.Equal("2 weeks ago", RelativeTime.Format(At(11), Now));
        Assert.Equal("5 weeks ago", RelativeTime.Format(At(34), Now));
        Assert.Equal("Sep 11, 2026", RelativeTime.Format(At(35), Now));
        Assert.Equal("Not played yet", RelativeTime.Overline(false, null, null, Now));
        Assert.Equal("New in the library · 3 days ago", RelativeTime.Overline(true, At(3), null, Now));
        Assert.Equal("Last played · yesterday, " + Time(At(1)), RelativeTime.Overline(false, null, At(1), Now));
    }

    [Fact]
    public void Time_follows_the_regional_format_of_the_english_variant()
    {
        using var _ = TestCulture.Use("en-GB");

        Assert.Equal("today, 18:05", RelativeTime.Format(At(0, 18, 5), Now)); // 24 Stunden wie in Großbritannien
        Assert.EndsWith(" 11, 2026", RelativeTime.Format(At(35), Now), StringComparison.Ordinal); // Muster der Vorlage
    }

    [Fact]
    public void German_stays_as_before()
    {
        Assert.Equal("heute, 18:05", RelativeTime.Format(At(0, 18, 5), Now));
        Assert.Equal("vor 3 Tagen", RelativeTime.Format(At(3), Now));
        Assert.Equal("1 Spiel", LibraryText.Games(1));
        Assert.Equal("2 Spiele", LibraryText.Games(2));
    }

    [Fact]
    public void Sources_and_hdr_labels_are_english()
    {
        using var _ = TestCulture.Use("en-US");

        Assert.Equal("All games", LibraryText.SourceTitle(null));
        Assert.Equal("Steam games", LibraryText.SourceTitle(GameSource.Steam));
        Assert.Equal("My games", LibraryText.SourceTitle(GameSource.Manual));
        Assert.Equal("Manual", LibraryText.SourceName(GameSource.Manual));
        Assert.Equal("NATIVE HDR", LibraryText.HdrTag(HdrSupportStatus.Native));
        Assert.Equal("LIMITED", LibraryText.HdrPill(HdrSupportStatus.Limited));
        Assert.Equal("HDR on launch", LibraryStrings.Details_HdrAuto);
        Assert.Equal("Windows Auto HDR", LibraryStrings.Details_AutoHdr);
        Assert.Equal("Ctrl+F", LibraryStrings.Key_CtrlF);
        Assert.Equal("Shift+F10", LibraryStrings.Key_ShiftF10);
        Assert.Equal("Del", LibraryStrings.Key_Delete);
    }
}
