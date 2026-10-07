using HdrPilot.Core.HdrSupport;

namespace HdrPilot.Tests.HdrSupport;

public sealed class PcgwWikitextTests
{
    [Theory]
    [InlineData("true", HdrSupportStatus.Native)]
    [InlineData("always on", HdrSupportStatus.Native)]
    [InlineData("limited", HdrSupportStatus.Limited)]
    [InlineData("hackable", HdrSupportStatus.ModOnly)]
    [InlineData("false", HdrSupportStatus.None)]
    [InlineData("unknown", HdrSupportStatus.Unknown)]
    [InlineData("n/a", HdrSupportStatus.Unknown)]
    [InlineData("", HdrSupportStatus.Unknown)]
    [InlineData(" TRUE ", HdrSupportStatus.Native)]
    [InlineData("vielleicht", HdrSupportStatus.Unknown)]
    public void Hdr_values_map_to_statuses(string value, HdrSupportStatus expected)
    {
        Assert.Equal(expected, HdrSupportValues.Parse(value));
        Assert.Equal(expected, PcgwWikitext.Parse("Spiel", "{{Video\n|hdr = " + value + "\n|hdr notes = true\n}}").Status);
    }

    [Fact]
    public void Only_native_and_limited_count_as_hdr_capable()
    {
        Assert.True(HdrSupportValues.IsHdrCapable(HdrSupportStatus.Native));
        Assert.True(HdrSupportValues.IsHdrCapable(HdrSupportStatus.Limited));
        Assert.False(HdrSupportValues.IsHdrCapable(HdrSupportStatus.ModOnly));
        Assert.False(HdrSupportValues.IsHdrCapable(HdrSupportStatus.None));
        Assert.False(HdrSupportValues.IsHdrCapable(HdrSupportStatus.Unknown));
    }

    [Fact]
    public void Reads_ids_with_comments_side_ids_and_spacing_like_the_recorded_page()
    {
        const string text = "{{Infobox game\n|steam appid  = 1091500\n|steam appid side  = 1495710, 2060310\n"
            + "|gogcom id    = 2093619782 <!-- the main store \"package\" depot, but not the game -->\n"
            + "|gogcom id side = 1373464609 <!-- the main game depot --> , 1423049311\n}}\n"
            + "{{Video\n|hdr                        = limited\n|hdr notes                  = Has options for scRGB or PQ HDR10, true\n}}";

        var page = PcgwWikitext.Parse("Cyberpunk 2077", text);

        Assert.Equal("Cyberpunk 2077", page.Title);
        Assert.Equal(HdrSupportStatus.Limited, page.Status);
        Assert.Equal(["1091500", "1495710", "2060310"], page.SteamAppIds);
        Assert.Equal(["2093619782", "1373464609", "1423049311"], page.GogIds);
        Assert.True(page.HasSteamAppId("1091500"));
        Assert.False(page.HasSteamAppId("109150"));
    }

    [Fact]
    public void Several_ids_in_one_field_and_values_ending_the_template()
    {
        var page = PcgwWikitext.Parse("X", "{{Infobox game\n|steam appid = 3008130,  3008140 ; 3008150\n}}\n{{Video\n|hdr=true}}");

        Assert.Equal(["3008130", "3008140", "3008150"], page.SteamAppIds);
        Assert.Equal(HdrSupportStatus.Native, page.Status);
    }

    [Fact]
    public void Commented_out_values_and_notes_do_not_count()
    {
        var page = PcgwWikitext.Parse("X", "{{Video\n<!--|hdr = true-->\n|hdr notes = true\n|hdr = false\n}}\n<!-- |steam appid = 1 ");

        Assert.Equal(HdrSupportStatus.None, page.Status);
        Assert.Empty(page.SteamAppIds);

        // Kommentare mitten im Wert: Ziffern darin sind keine IDs, der Wert dahinter zählt.
        var inline = PcgwWikitext.Parse("X", "|steam appid = 123 <!-- nicht 456 -->\n|hdr = <!-- früher true --> limited\n");
        Assert.Equal(["123"], inline.SteamAppIds);
        Assert.Equal(HdrSupportStatus.Limited, inline.Status);
    }

    [Fact]
    public void Missing_fields_or_text_mean_unknown()
    {
        Assert.Equal(HdrSupportStatus.Unknown, PcgwWikitext.Parse("X", null).Status);
        Assert.Equal(HdrSupportStatus.Unknown, PcgwWikitext.Parse("X", "Begriffsklärung: [[Doom (1993)]], [[Doom (2016)]]").Status);
        Assert.Empty(PcgwWikitext.Parse("X", "|steam appid = \n").SteamAppIds);
    }
}
