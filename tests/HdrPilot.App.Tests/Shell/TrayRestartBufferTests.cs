using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.Core.Sessions;

namespace HdrPilot.App.Tests.Shell;

/// <summary>Tooltip „HDR bleibt noch m:ss an (Neustart-Puffer)“ und seine Herleitung aus den Sitzungen.</summary>
public sealed class TrayRestartBufferTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    private static TrayState Buffered(TimeSpan left, bool held = false, bool hdrOn = true) =>
        new(hdrOn, HdrSupported: true, Paused: false, ActiveGames: ["Dead Island 2"], RestartBufferUntil: Now + left, RestartBufferHeld: held);

    private static string Countdown(int minutes, int seconds) =>
        string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayTooltipRestartBuffer, minutes, seconds);

    [Fact]
    public void Countdown_line_sits_between_games_and_automation()
    {
        var tooltip = TrayPresentation.TooltipFor(Buffered(TimeSpan.FromSeconds(155)), Now);

        Assert.Equal(
            ShellStrings.TrayTooltipHdrOn + "\n"
            + string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayTooltipGame, "Dead Island 2") + "\n"
            + "HDR bleibt noch 2:35 an (Neustart-Puffer)" + "\n"
            + ShellStrings.TrayTooltipActive,
            tooltip);
    }

    [Theory]
    [InlineData(180.0, 3, 0)]
    [InlineData(179.2, 3, 0)] // aufgerundet: 0:00 erst am Ende
    [InlineData(61.0, 1, 1)]
    [InlineData(9.0, 0, 9)]
    [InlineData(0.4, 0, 1)]
    [InlineData(0.0, 0, 0)]
    [InlineData(-5.0, 0, 0)]
    public void Countdown_shows_minutes_and_seconds(double secondsLeft, int minutes, int seconds)
    {
        var line = TrayPresentation.RestartBufferLine(Buffered(TimeSpan.FromSeconds(secondsLeft)), Now);

        Assert.Equal(Countdown(minutes, seconds), line);
    }

    [Fact]
    public void Waiting_for_a_fullscreen_app_has_its_own_line_without_countdown()
    {
        var state = Buffered(TimeSpan.FromSeconds(-30), held: true);

        Assert.Equal(ShellStrings.TrayTooltipRestartBufferFullscreen, TrayPresentation.RestartBufferLine(state, Now));
        Assert.Contains(ShellStrings.TrayTooltipRestartBufferFullscreen, TrayPresentation.TooltipFor(state, Now), StringComparison.Ordinal);
        Assert.False(TrayPresentation.HasCountdown(state, Now));
    }

    [Fact]
    public void No_buffer_line_without_buffer_or_when_hdr_is_off()
    {
        Assert.Null(TrayPresentation.RestartBufferLine(new TrayState(true, true, false, ["Hades II"]), Now));
        Assert.Null(TrayPresentation.RestartBufferLine(Buffered(TimeSpan.FromSeconds(90), hdrOn: false), Now)); // von Hand ausgeschaltet
        Assert.False(TrayPresentation.HasCountdown(Buffered(TimeSpan.FromSeconds(90), hdrOn: false), Now));
    }

    [Fact]
    public void Countdown_runs_only_until_the_buffer_ends()
    {
        Assert.True(TrayPresentation.HasCountdown(Buffered(TimeSpan.FromSeconds(1)), Now));
        Assert.False(TrayPresentation.HasCountdown(Buffered(TimeSpan.Zero), Now));
    }

    [Fact]
    public void Long_game_names_are_shortened_but_the_buffer_line_stays_whole()
    {
        var games = Enumerable.Range(1, 8).Select(i => "Ein sehr langer Spielname Nummer " + i.ToString(CultureInfo.InvariantCulture)).ToList();
        var state = new TrayState(true, true, true, games, Now + TimeSpan.FromSeconds(100), false);

        var tooltip = TrayPresentation.TooltipFor(state, Now);

        Assert.True(tooltip.Length <= TrayPresentation.MaxTooltipLength, $"Länge {tooltip.Length}");
        Assert.Contains("\n" + Countdown(1, 40) + "\n", tooltip, StringComparison.Ordinal);
        Assert.EndsWith("\n" + ShellStrings.TrayTooltipPaused, tooltip, StringComparison.Ordinal);
        Assert.Contains(ShellStrings.Ellipsis, tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Buffer_is_taken_from_sessions_only_when_no_game_runs_any_more()
    {
        var ends = Now + TimeSpan.FromSeconds(120);
        var buffered = Session("Steam:1", SessionPhase.Ending, ends);
        var grace = Session("Steam:2", SessionPhase.Ending, null);
        var running = Session("Steam:3", SessionPhase.Running, null);
        var later = Session("Steam:4", SessionPhase.Ending, ends + TimeSpan.FromSeconds(30));
        var held = Session("Steam:5", SessionPhase.Ending, Now - TimeSpan.FromSeconds(5)) with { HeldForFullscreen = true };

        Assert.Equal(((DateTimeOffset?)ends, false), TrayPresentation.RestartBufferOf([buffered]));
        Assert.Equal(((DateTimeOffset?)ends, false), TrayPresentation.RestartBufferOf([buffered, grace]));
        Assert.Equal(((DateTimeOffset?)(ends + TimeSpan.FromSeconds(30)), false), TrayPresentation.RestartBufferOf([buffered, later]));
        Assert.Equal(((DateTimeOffset?)null, false), TrayPresentation.RestartBufferOf([buffered, running])); // HDR bleibt wegen Spiel 3 an
        Assert.Equal(((DateTimeOffset?)null, false), TrayPresentation.RestartBufferOf([grace]));
        Assert.Equal(((DateTimeOffset?)null, false), TrayPresentation.RestartBufferOf([]));
        Assert.True(TrayPresentation.RestartBufferOf([held]).Held);
    }

    private static SessionInfo Session(string key, SessionPhase phase, DateTimeOffset? bufferUntil) =>
        new(key, key, phase, SessionTrigger.ProcessDetected, Now, [], [], bufferUntil);
}
