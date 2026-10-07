using HdrPilot.Core.Launchers;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Launchers;

/// <summary>Ein Signal je Startvorgang: Steam je (AppID, ActionID), Epic je Spiel innerhalb von 10 s.</summary>
public sealed class LauncherSignalDedupeTests
{
    private readonly FakeClock _clock = new();
    private readonly LauncherSignalDeduplicator _dedupe;

    public LauncherSignalDedupeTests() => _dedupe = new LauncherSignalDeduplicator(_clock);

    private static LauncherSignal Steam(long action, string appId = "9990130") => new(LauncherKind.Steam, "Steam:" + appId, action);

    private static LauncherSignal Epic(string appName = "Crow") => new(LauncherKind.Epic, "Epic:" + appName);

    [Fact]
    public void Steam_reports_each_action_once_however_many_lines_it_writes()
    {
        Assert.True(_dedupe.IsNew(Steam(5)));
        for (var i = 0; i < 17; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(_dedupe.IsNew(Steam(5)));
        }

        Assert.True(_dedupe.IsNew(Steam(6))); // nächster Start desselben Spiels
        Assert.True(_dedupe.IsNew(Steam(6, "1091500"))); // anderes Spiel, gleiche Nummer
    }

    [Fact]
    public void Steam_action_stays_known_while_lines_keep_coming()
    {
        Assert.True(_dedupe.IsNew(Steam(5)));
        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(100)); // jeweils kürzer als SteamIdle
            Assert.False(_dedupe.IsNew(Steam(5)));
        }
    }

    [Fact]
    public void Steam_action_is_reported_again_after_a_long_silence()
    {
        // Steam zählt nach einem Neustart wieder von vorn; nach langer Pause ist dieselbe Nummer ein neuer Start.
        Assert.True(_dedupe.IsNew(Steam(1)));
        _clock.Advance(LauncherSignalDeduplicator.SteamIdle - TimeSpan.FromSeconds(1));
        Assert.False(_dedupe.IsNew(Steam(1)));
        _clock.Advance(LauncherSignalDeduplicator.SteamIdle);
        Assert.True(_dedupe.IsNew(Steam(1)));
    }

    [Fact]
    public void Epic_reports_the_same_game_once_within_ten_seconds()
    {
        Assert.True(_dedupe.IsNew(Epic()));
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(_dedupe.IsNew(Epic())); // URI und Bibliothek für denselben Start
        Assert.True(_dedupe.IsNew(Epic("Sugar")));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_dedupe.IsNew(Epic())); // 10 s nach dem ersten: neuer Start
    }

    [Fact]
    public void Epic_window_does_not_slide()
    {
        Assert.True(_dedupe.IsNew(Epic()));
        _clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(_dedupe.IsNew(Epic()));
        _clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True(_dedupe.IsNew(Epic()));
    }

    [Fact]
    public void Epic_and_steam_keys_never_collide()
    {
        Assert.True(_dedupe.IsNew(Epic("9990130")));
        Assert.True(_dedupe.IsNew(Steam(5)));
    }
}
