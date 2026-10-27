using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Entprellung der Spielfarben (E94, G15.3): einzelner Wechsel sofort, schnelle Folgen und gehaltene Tasten halten die
/// gezeigten Farben und wenden das letzte Spiel 300 ms nach dem letzten Wechsel an; ankommende Farben warten, solange gehalten
/// wird; früher und später Zeitgeber.
/// </summary>
public sealed class GameAccentSchedulerTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void A_single_change_applies_at_once()
    {
        var scheduler = new GameAccentScheduler();

        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Selected(Ms(1000), repeat: false));
        Assert.False(scheduler.IsHolding);
        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Selected(Ms(2000), repeat: false)); // lange danach: wieder sofort
    }

    [Fact]
    public void Bursts_of_33_ms_hold_and_apply_300_ms_after_the_last_change()
    {
        var scheduler = new GameAccentScheduler();
        var applied = scheduler.Selected(Ms(0), false) == GameAccentDecision.ApplyNow ? 1 : 0;
        var held = 0;
        for (var t = 33; t <= 33 * 19; t += 33)
        {
            var d = scheduler.Selected(Ms(t), false);
            Assert.Equal(GameAccentAction.Hold, d.Action);
            Assert.Equal(Ms(t + 300), d.DueAt);
            held++;
        }

        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Elapsed(Ms((33 * 19) + 300)));
        applied++;
        Assert.Equal((2, 19), (applied, held));
        Assert.False(scheduler.IsHolding);
    }

    [Fact]
    public void Colors_arriving_while_holding_wait()
    {
        var scheduler = new GameAccentScheduler();
        scheduler.Selected(Ms(0), false);
        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.ColorsArrived());

        scheduler.Selected(Ms(100), false);

        Assert.Equal(GameAccentDecision.None, scheduler.ColorsArrived());
    }

    [Fact]
    public void Held_arrow_key_gives_one_fade_at_the_start_and_one_at_the_end()
    {
        var scheduler = new GameAccentScheduler();
        var fades = 0;
        if (scheduler.Selected(Ms(0), repeat: false).Action == GameAccentAction.ApplyNow)
        {
            fades++;
        }

        // Windows: erste Wiederholung nach etwa 500 ms, dann alle 33 ms; losgelassen bei 2000 ms.
        TimeSpan due = default;
        for (var t = 500.0; t <= 2000; t += 33)
        {
            var d = scheduler.Selected(Ms(t), repeat: true);
            Assert.Equal(GameAccentAction.Hold, d.Action);
            due = d.DueAt;
        }

        Assert.Equal(GameAccentDecision.Hold(due), scheduler.Elapsed(due - Ms(1)));
        if (scheduler.Elapsed(due).Action == GameAccentAction.ApplyNow)
        {
            fades++;
        }

        Assert.Equal(2, fades);
    }

    [Fact]
    public void Repeat_flag_holds_at_once_even_after_a_long_pause()
    {
        var scheduler = new GameAccentScheduler();
        scheduler.Selected(Ms(0), false);

        var d = scheduler.Selected(Ms(5000), repeat: true);

        Assert.Equal(GameAccentAction.Hold, d.Action);
        Assert.True(scheduler.IsHolding);
    }

    [Fact]
    public void A_late_tick_applies_and_a_tick_without_holding_does_nothing()
    {
        var scheduler = new GameAccentScheduler();
        Assert.Equal(GameAccentDecision.None, scheduler.Elapsed(Ms(10)));
        scheduler.Selected(Ms(0), false);
        scheduler.Selected(Ms(50), false);

        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Elapsed(Ms(2000)));
        Assert.Equal(GameAccentDecision.None, scheduler.Elapsed(Ms(2100)));
    }

    [Fact]
    public void An_early_tick_holds_again_until_the_due_time()
    {
        var scheduler = new GameAccentScheduler();
        scheduler.Selected(Ms(0), false);
        scheduler.Selected(Ms(50), false); // fällig bei 350
        scheduler.Selected(Ms(200), false); // fällig bei 500

        var early = scheduler.Elapsed(Ms(350));

        Assert.Equal(GameAccentDecision.Hold(Ms(500)), early);
        Assert.True(scheduler.IsHolding);
        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Elapsed(Ms(500)));
    }

    [Fact]
    public void A_change_that_is_not_fast_cancels_holding()
    {
        var scheduler = new GameAccentScheduler();
        scheduler.Selected(Ms(0), false);
        scheduler.Selected(Ms(100), false);
        Assert.True(scheduler.IsHolding);

        Assert.Equal(GameAccentDecision.ApplyNow, scheduler.Selected(Ms(1000), false));
        Assert.False(scheduler.IsHolding);
        Assert.Equal(GameAccentDecision.None, scheduler.Elapsed(Ms(1000))); // der Zeitgeber hat nichts mehr zu tun
    }
}
