using HdrPilot.App.Services;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Shell;

public sealed class DisplayEventsServiceTests
{
    [Fact]
    public void Burst_of_events_is_reported_once_after_the_quiet_period()
    {
        var (service, source, timer, raised) = Create();
        service.Start();

        source.Raise("WM_DISPLAYCHANGE");
        source.Raise("WM_DISPLAYCHANGE");
        source.Raise("Resume");

        Assert.Equal(3, timer.Restarts);
        Assert.Empty(raised);

        timer.Fire();
        Assert.Single(raised);

        // Ohne neue Ereignisse kein weiteres Melden.
        timer.Fire();
        Assert.Single(raised);
    }

    [Fact]
    public void Each_quiet_period_reports_separately()
    {
        var (service, source, timer, raised) = Create();
        service.Start();

        source.Raise("WM_DISPLAYCHANGE");
        timer.Fire();
        source.Raise("Resume");
        timer.Fire();

        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void Nothing_is_attached_before_start()
    {
        var (_, source, timer, raised) = Create();

        source.Raise("WM_DISPLAYCHANGE");

        Assert.False(source.Attached);
        Assert.Equal(0, timer.Restarts);
        Assert.Empty(raised);
    }

    [Fact]
    public void Dispose_detaches_the_static_system_events_and_stops_the_timer()
    {
        var (service, source, timer, raised) = Create();
        service.Start();
        source.Raise("WM_DISPLAYCHANGE");

        service.Dispose();
        source.RaiseEvenIfDetached("WM_DISPLAYCHANGE");
        timer.Fire();

        Assert.False(source.Attached);
        Assert.True(timer.Disposed);
        Assert.Empty(raised);
        service.Dispose();
    }

    [Fact]
    public void Events_are_marshalled_through_the_ui_poster()
    {
        var posted = new List<Action>();
        var source = new FakeDisplayEventSource();
        var timer = new ManualDebounceTimer();
        using var service = new DisplayEventsService(source, timer, posted.Add);
        service.Start();

        source.Raise("WM_DISPLAYCHANGE");

        Assert.Single(posted);
        Assert.Equal(0, timer.Restarts);
        posted[0]();
        Assert.Equal(1, timer.Restarts);
    }

    [Fact]
    public void Coalesced_events_are_logged()
    {
        var logger = new ListLogger<DisplayEventsService>();
        var source = new FakeDisplayEventSource();
        var timer = new ManualDebounceTimer();
        using var service = new DisplayEventsService(source, timer, a => a(), logger);
        service.Start();

        source.Raise("WM_DISPLAYCHANGE");
        source.Raise("WM_DISPLAYCHANGE");
        timer.Fire();

        Assert.True(logger.Contains(LogLevel.Information, "2 Ereignisse"));
    }

    [Fact]
    public async Task Dispatcher_timer_waits_for_the_last_event()
    {
        var elapsed = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = TimeSpan.FromMilliseconds(150);
        var timer = WpfTestHost.Run(() =>
        {
            var t = new DispatcherDebounceTimer(WpfTestHost.Dispatcher, delay);
            t.Elapsed += (_, _) => elapsed.TrySetResult(DateTime.UtcNow);
            return t;
        });

        DateTime lastRestart = default;
        for (var i = 0; i < 4; i++)
        {
            WpfTestHost.Run(() => timer.Restart());
            lastRestart = DateTime.UtcNow;
            await Task.Delay(60, TestContext.Current.CancellationToken);
        }

        var fired = await elapsed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(fired - lastRestart >= delay - TimeSpan.FromMilliseconds(20), $"zu früh: {(fired - lastRestart).TotalMilliseconds} ms");
        WpfTestHost.Run(timer.Dispose);
    }

    [Fact]
    public void Debounce_delay_is_one_and_a_half_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(1.5), DisplayEventsService.DebounceDelay);

    private static (DisplayEventsService Service, FakeDisplayEventSource Source, ManualDebounceTimer Timer, List<EventArgs> Raised) Create()
    {
        var source = new FakeDisplayEventSource();
        var timer = new ManualDebounceTimer();
        var service = new DisplayEventsService(source, timer, a => a());
        var raised = new List<EventArgs>();
        service.DisplaysChanged += (_, e) => raised.Add(e);
        return (service, source, timer, raised);
    }

    private sealed class FakeDisplayEventSource : IDisplayEventSource
    {
        private Action<string>? _handler;
        private Action<string>? _lastHandler;

        public bool Attached => _handler is not null;

        public void Attach(Action<string> onEvent)
        {
            _handler = onEvent;
            _lastHandler = onEvent;
        }

        public void Detach() => _handler = null;

        public void Raise(string reason) => _handler?.Invoke(reason);

        /// <summary>Simuliert ein Ereignis, das noch unterwegs war, als abgemeldet wurde.</summary>
        public void RaiseEvenIfDetached(string reason) => _lastHandler?.Invoke(reason);
    }

    private sealed class ManualDebounceTimer : IDebounceTimer
    {
        public event EventHandler? Elapsed;

        public int Restarts { get; private set; }

        public bool Running { get; private set; }

        public bool Disposed { get; private set; }

        public void Restart()
        {
            Restarts++;
            Running = true;
        }

        public void Stop() => Running = false;

        public void Fire()
        {
            Running = false;
            Elapsed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Disposed = true;
    }
}
