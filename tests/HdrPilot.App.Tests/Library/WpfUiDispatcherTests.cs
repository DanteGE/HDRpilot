using System.Windows.Threading;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>Der echte UI-Dispatcher der Bibliothek: Verzögerungen laufen genau einmal und lassen sich abbrechen.</summary>
public sealed class WpfUiDispatcherTests
{
    [Fact]
    public void Delay_runs_once_on_the_ui_thread_and_post_runs_later() => Sta.Run(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var uiThread = Environment.CurrentManagedThreadId;
        var runs = new List<int>();
        var posted = false;

        ui.Delay(TimeSpan.FromMilliseconds(20), () => runs.Add(Environment.CurrentManagedThreadId));
        ui.Post(() => posted = true);
        Assert.False(posted); // nie sofort, sondern mit niedriger Priorität

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(posted);
        Assert.Equal([uiThread], runs); // genau einmal, danach kein laufender Zeitgeber
    });

    [Fact]
    public void Disposed_delay_never_runs() => Sta.Run(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var runs = 0;

        var handle = ui.Delay(TimeSpan.FromMilliseconds(20), () => runs++);
        handle.Dispose();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(0, runs);
    });
}
