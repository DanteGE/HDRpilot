using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Shell;

public sealed class TrayPresentationTests
{
    [Fact]
    public void Hdr_on_shows_accent_icon_and_on_tooltip()
    {
        var state = new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: []);

        Assert.Equal(new TrayIconSpec(HdrOn: true, Paused: false), TrayPresentation.IconFor(state));
        Assert.Equal(ShellStrings.TrayTooltipHdrOn + "\n" + ShellStrings.TrayTooltipActive, TrayPresentation.TooltipFor(state));
    }

    [Fact]
    public void Hdr_off_shows_muted_icon_and_off_tooltip()
    {
        var state = new TrayState(HdrOn: false, HdrSupported: true, Paused: false, ActiveGames: []);

        Assert.Equal(new TrayIconSpec(HdrOn: false, Paused: false), TrayPresentation.IconFor(state));
        Assert.StartsWith(ShellStrings.TrayTooltipHdrOff + "\n", TrayPresentation.TooltipFor(state), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Paused_keeps_hdr_state_and_adds_pause(bool hdrOn)
    {
        var state = new TrayState(hdrOn, HdrSupported: true, Paused: true, ActiveGames: []);

        Assert.Equal(new TrayIconSpec(hdrOn, Paused: true), TrayPresentation.IconFor(state));
        var tooltip = TrayPresentation.TooltipFor(state);
        Assert.EndsWith("\n" + ShellStrings.TrayTooltipPaused, tooltip, StringComparison.Ordinal);
        Assert.StartsWith(hdrOn ? ShellStrings.TrayTooltipHdrOn : ShellStrings.TrayTooltipHdrOff, tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_hdr_capable_display_the_tooltip_says_so()
    {
        var state = new TrayState(HdrOn: false, HdrSupported: false, Paused: false, ActiveGames: []);

        Assert.StartsWith(ShellStrings.TrayTooltipNoHdr, TrayPresentation.TooltipFor(state), StringComparison.Ordinal);
    }

    [Fact]
    public void Running_games_are_listed_between_state_and_automation()
    {
        var one = TrayPresentation.TooltipFor(new TrayState(true, true, false, ["Cyberpunk 2077"]));
        var two = TrayPresentation.TooltipFor(new TrayState(true, true, false, ["Cyberpunk 2077", "Hades II"]));

        Assert.Equal(
            ShellStrings.TrayTooltipHdrOn + "\n" + string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayTooltipGame, "Cyberpunk 2077") + "\n" + ShellStrings.TrayTooltipActive,
            one);
        Assert.Contains(string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayTooltipGames, "Cyberpunk 2077" + ShellStrings.ListSeparator + "Hades II"), two, StringComparison.Ordinal);
    }

    [Fact]
    public void Tooltip_never_exceeds_127_characters_and_keeps_first_and_last_line()
    {
        var games = Enumerable.Range(1, 12).Select(i => "Ein sehr langer Spielname Nummer " + i.ToString(CultureInfo.InvariantCulture)).ToList();
        var state = new TrayState(HdrOn: true, HdrSupported: true, Paused: true, ActiveGames: games);

        var tooltip = TrayPresentation.TooltipFor(state);

        Assert.True(tooltip.Length <= TrayPresentation.MaxTooltipLength, $"Länge {tooltip.Length}");
        Assert.StartsWith(ShellStrings.TrayTooltipHdrOn + "\n", tooltip, StringComparison.Ordinal);
        Assert.EndsWith("\n" + ShellStrings.TrayTooltipPaused, tooltip, StringComparison.Ordinal);
        Assert.Contains(ShellStrings.Ellipsis, tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_game_names_are_ignored()
    {
        var tooltip = TrayPresentation.TooltipFor(new TrayState(false, true, false, ["", "  "]));

        Assert.Equal(ShellStrings.TrayTooltipHdrOff + "\n" + ShellStrings.TrayTooltipActive, tooltip);
    }

    [Theory]
    [InlineData("kurz", 10, "kurz")]
    [InlineData("genau zehn", 10, "genau zehn")]
    [InlineData("deutlich zu lang", 10, "deutlich…")]
    public void Fit_truncates_with_ellipsis(string text, int max, string expected)
    {
        var result = TrayPresentation.Fit(text, max);

        Assert.Equal(expected.Replace("…", ShellStrings.Ellipsis, StringComparison.Ordinal), result);
        Assert.True(result.Length <= max);
    }
}
