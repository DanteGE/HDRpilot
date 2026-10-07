using HdrPilot.Core.Launch;

namespace HdrPilot.Tests.Launch;

/// <summary>Rückmeldung der laufenden Instanz an den Wrapper über benannte Ereignisse (echte Ereignisse, eindeutige Namen).</summary>
public sealed class PrepareSignalTests
{
    [Fact]
    public void New_names_are_unique_and_valid()
    {
        var a = PrepareSignal.NewName();
        var b = PrepareSignal.NewName();

        Assert.NotEqual(a, b);
        Assert.True(PrepareSignal.IsValidName(a));
        Assert.StartsWith(@"Local\HdrPilot.Prepared.", a, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Local\Irgendwas")]
    [InlineData(@"Global\HdrPilot.Prepared.0123456789abcdef0123456789abcdef")]
    [InlineData(@"Local\HdrPilot.Prepared.0123456789ABCDEF0123456789ABCDEF")]
    [InlineData(@"Local\HdrPilot.Prepared.0123456789abcdef0123456789abcdef.Skip")]
    [InlineData(@"Local\HdrPilot.Prepared.0123456789abcdef")]
    public void Foreign_or_malformed_names_are_rejected(string? name)
    {
        Assert.False(PrepareSignal.IsValidName(name));
    }

    [Fact]
    public void Prepared_sets_the_ready_event()
    {
        using var waiter = new PrepareWaiter();

        Assert.True(PrepareSignal.TrySignal(waiter.Name, prepared: true));
        Assert.Equal(PrepareWaitResult.Ready, waiter.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Not_prepared_sets_the_skip_event()
    {
        using var waiter = new PrepareWaiter();

        Assert.True(PrepareSignal.TrySignal(waiter.Name, prepared: false));
        Assert.Equal(PrepareWaitResult.Skipped, waiter.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Without_answer_the_wait_times_out()
    {
        using var waiter = new PrepareWaiter();

        Assert.Equal(PrepareWaitResult.TimedOut, waiter.Wait(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Signal_for_a_wrapper_that_is_gone_fails_quietly()
    {
        string name;
        using (var waiter = new PrepareWaiter())
        {
            name = waiter.Name;
        }

        Assert.False(PrepareSignal.TrySignal(name, prepared: true));
    }

    [Fact]
    public void Foreign_existing_event_is_never_set()
    {
        var foreign = @"Local\HdrPilot.Tests.Fremd." + Guid.NewGuid().ToString("N");
        using var handle = new EventWaitHandle(false, EventResetMode.ManualReset, foreign);

        Assert.False(PrepareSignal.TrySignal(foreign, prepared: true));
        Assert.False(handle.WaitOne(0));
    }
}
