using HdrPilot.App.Wrapper;
using HdrPilot.Core.Launch;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Wrapper;

/// <summary>Laufende Instanz: Vorbereitung ausführen und dem wartenden Wrapper immer antworten.</summary>
public sealed class PrepareRequestHandlerTests
{
    private readonly ListLogger<PrepareRequestHandlerTests> _log = new();

    [Fact]
    public async Task Prepared_game_answers_ready()
    {
        using var waiter = new PrepareWaiter();
        string? asked = null;

        var outcome = await PrepareRequestHandler.HandleAsync("Steam:1091500", waiter.Name, key =>
        {
            asked = key;
            return Task.FromResult(new PrepareOutcome(true, "bereit", DateTimeOffset.UnixEpoch));
        }, _log);

        Assert.Equal("Steam:1091500", asked);
        Assert.True(outcome!.Prepared);
        Assert.Equal(PrepareWaitResult.Ready, waiter.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(_log.Contains(LogLevel.Information, "Steam-Startoption angefordert: Steam:1091500"));
        Assert.True(_log.Contains(LogLevel.Information, "HDR vorbereitet"));
    }

    [Fact]
    public async Task Unknown_game_answers_skip()
    {
        using var waiter = new PrepareWaiter();

        await PrepareRequestHandler.HandleAsync(
            "Steam:404", waiter.Name, _ => Task.FromResult(new PrepareOutcome(false, "Spiel Steam:404 ist nicht bekannt.", null)), _log);

        Assert.Equal(PrepareWaitResult.Skipped, waiter.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(_log.Contains(LogLevel.Information, "nichts vorbereitet"));
    }

    [Fact]
    public async Task Failing_preparation_still_answers_skip_and_does_not_throw()
    {
        using var waiter = new PrepareWaiter();

        var outcome = await PrepareRequestHandler.HandleAsync(
            "Steam:1", waiter.Name, _ => throw new InvalidOperationException("InitializeAsync fehlt"), _log);

        Assert.Null(outcome);
        Assert.Equal(PrepareWaitResult.Skipped, waiter.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(_log.Contains(LogLevel.Error, "fehlgeschlagen"));
    }

    [Fact]
    public async Task Without_ready_event_only_prepares()
    {
        var called = false;

        await PrepareRequestHandler.HandleAsync("Steam:1", null, _ =>
        {
            called = true;
            return Task.FromResult(new PrepareOutcome(true, "bereit", null));
        }, _log);

        Assert.True(called);
        Assert.False(_log.Contains(LogLevel.Warning, "Antwort"));
    }

    [Fact]
    public void Foreign_event_name_is_refused_with_a_warning()
    {
        PrepareRequestHandler.Answer(@"Local\Irgendwas", prepared: true, _log);

        Assert.True(_log.Contains(LogLevel.Warning, "Antwort an die Steam-Startoption nicht möglich"));
    }
}
