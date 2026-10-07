using HdrPilot.App.Resources;
using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Shell;

/// <summary>Countdown des Neustart-Puffers im Tooltip: läuft nur, solange der Puffer läuft.</summary>
public sealed partial class TrayServiceTests
{
    private static readonly DateTimeOffset BufferStart = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Restart_buffer_countdown_is_kept_current_and_stops_with_the_buffer()
    {
        var clock = new TestClock(BufferStart);
        var host = new FakeTrayHost { State = new TrayState(true, true, false, ["Dead Island 2"], BufferStart + TimeSpan.FromSeconds(95)) };
        var surface = new FakeSurface();
        var service = CreateWithClock(host, surface, clock);

        WpfTestHost.Run(service.Initialize);
        Assert.Contains("HDR bleibt noch 1:35 an (Neustart-Puffer)", WpfTestHost.Run(() => surface.Text), StringComparison.Ordinal);
        Assert.True(WpfTestHost.Run(() => service.IsCountdownRunning));

        // Ohne neues Lesen des Zustands: der Timer führt nur den Text nach.
        var queries = host.Queries;
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(WaitForText(surface, "1:05"), "Countdown wurde nicht nachgeführt");
        Assert.Equal(queries, host.Queries);

        clock.Advance(TimeSpan.FromSeconds(70)); // Puffer vorbei: Timer endet von selbst
        Assert.True(WaitForText(surface, "0:00"), "Countdown endete nicht bei 0:00");
        Assert.True(SpinWait.SpinUntil(() => WpfTestHost.Run(() => !service.IsCountdownRunning), TimeSpan.FromSeconds(5)), "Timer läuft weiter");

        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Countdown_timer_runs_only_while_a_buffer_runs_and_stops_on_dispose()
    {
        var clock = new TestClock(BufferStart);
        var host = new FakeTrayHost { State = new TrayState(true, true, false, ["Hades II"]) };
        var surface = new FakeSurface();
        var service = CreateWithClock(host, surface, clock);
        WpfTestHost.Run(service.Initialize);
        Assert.False(WpfTestHost.Run(() => service.IsCountdownRunning)); // normaler Betrieb: kein zusätzlicher Timer

        // Warten auf eine Vollbild-Anwendung: eigener Text, kein Countdown.
        host.State = host.State with { RestartBufferUntil = BufferStart - TimeSpan.FromSeconds(1), RestartBufferHeld = true };
        WpfTestHost.Run(service.Refresh);
        Assert.True(WaitForText(surface, ShellStrings.TrayTooltipRestartBufferFullscreen), "Text für das Warten fehlt");
        Assert.False(WpfTestHost.Run(() => service.IsCountdownRunning));

        host.State = host.State with { RestartBufferUntil = BufferStart + TimeSpan.FromMinutes(3), RestartBufferHeld = false };
        WpfTestHost.Run(service.Refresh);
        Assert.True(SpinWait.SpinUntil(() => WpfTestHost.Run(() => service.IsCountdownRunning), TimeSpan.FromSeconds(5)), "Countdown startet nicht");

        WpfTestHost.Run(service.Dispose);
        Assert.False(WpfTestHost.Run(() => service.IsCountdownRunning));
    }

    private static TrayService CreateWithClock(ITrayHost host, FakeSurface surface, TestClock clock) => new(
        host,
        new NotificationGate(() => true, new SwitchableState { Current = UserNotificationState.AcceptsNotifications }),
        surfaceFactory: () => surface,
        iconSize: () => 16,
        clock: () => clock.Now);

    private static bool WaitForText(FakeSurface surface, string fragment) =>
        SpinWait.SpinUntil(() => WpfTestHost.Run(() => surface.Text.Contains(fragment, StringComparison.Ordinal)), TimeSpan.FromSeconds(5));

    private sealed class TestClock(DateTimeOffset start)
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = start;

        public DateTimeOffset Now
        {
            get
            {
                lock (_gate)
                {
                    return _now;
                }
            }
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate)
            {
                _now += by;
            }
        }
    }
}
