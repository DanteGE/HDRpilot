using HdrPilot.App.Infrastructure;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Speicher nach dem Schließen eines Fensters (G10.9): sofort ein GC, dann genau einmal, wenn der Bilddienst aufgeräumt hat
/// und die Ruhezeit vorbei ist, Grafikspeicher freigeben und gründlich sammeln; nicht, wenn inzwischen wieder ein Fenster
/// offen ist, und nur für das zuletzt geschlossene Fenster. Nichts läuft wiederholt.
/// </summary>
public sealed class UiMemoryReleaseTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private sealed class Harness
    {
        public List<bool> Collects { get; } = [];

        public List<(TimeSpan Delay, TaskCompletionSource Done)> Delays { get; } = [];

        public TaskCompletionSource Cleanup { get; set; } = NewSource();

        public int Flushes { get; private set; }

        public bool WindowOpen { get; set; }

        public UiMemoryRelease Create(TimeSpan? cleanupTimeout = null) => new(new UiMemoryReleaseOptions
        {
            AnyWindowOpen = () => WindowOpen,
            CleanupFinished = () => Cleanup.Task,
            FlushGraphics = () =>
            {
                lock (Collects)
                {
                    Flushes++;
                }

                return Task.CompletedTask;
            },
            Collect = thorough =>
            {
                lock (Collects)
                {
                    Collects.Add(thorough);
                }
            },
            Delay = delay =>
            {
                var done = NewSource();
                lock (Delays)
                {
                    Delays.Add((delay, done));
                }

                return done.Task;
            },
            Settle = TimeSpan.FromSeconds(2),
            CleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(10),
        });

        public void SettleAll()
        {
            lock (Delays)
            {
                foreach (var (_, done) in Delays)
                {
                    done.TrySetResult();
                }
            }
        }

        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task Second_pass_runs_once_after_the_image_cleanup_and_the_settle_time()
    {
        var h = new Harness();
        var release = h.Create();

        release.WindowClosed();
        Assert.Equal([false], h.Collects); // sofort wie bisher

        // Ruhezeit vorbei, der Bilddienst räumt noch auf: noch kein zweiter Durchgang.
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(h.Delays).Delay);
        h.SettleAll();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal([false], h.Collects);
        Assert.Equal(0, h.Flushes);

        // Aufgeräumt: Grafikspeicher freigeben und gründlich sammeln, genau einmal.
        h.Cleanup.SetResult();
        await release.Pending.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal([false, true], h.Collects);
        Assert.Equal(1, h.Flushes);
        Assert.Equal(1, release.Completed);
        Assert.Single(h.Delays); // kein weiterer Zeitgeber: nichts läuft wiederholt
    }

    [Fact]
    public async Task Second_pass_is_skipped_when_a_window_opened_meanwhile()
    {
        var h = new Harness();
        var release = h.Create();

        release.WindowClosed();
        h.WindowOpen = true;
        h.SettleAll();
        h.Cleanup.SetResult();
        await release.Pending.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal([false], h.Collects);
        Assert.Equal(0, h.Flushes);
        Assert.Equal(0, release.Completed);
    }

    [Fact]
    public async Task Only_the_last_closed_window_runs_the_second_pass()
    {
        var h = new Harness();
        var release = h.Create();

        release.WindowClosed();
        var first = release.Pending;
        release.WindowClosed();
        h.SettleAll();
        h.Cleanup.SetResult();
        await Task.WhenAll(first, release.Pending).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal([false, false, true], h.Collects);
        Assert.Equal(1, h.Flushes);
        Assert.Equal(1, release.Completed);
    }

    [Fact]
    public async Task Hanging_image_cleanup_does_not_block_the_second_pass_forever()
    {
        var h = new Harness();
        var release = h.Create(cleanupTimeout: TimeSpan.FromMilliseconds(50));

        release.WindowClosed();
        h.SettleAll();
        await release.Pending.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.False(h.Cleanup.Task.IsCompleted);
        Assert.Equal([false, true], h.Collects);
        Assert.Equal(1, h.Flushes);
    }
}
