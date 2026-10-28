using System.Globalization;
using System.Windows.Threading;
using HdrPilot.App.Tests.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests;

/// <summary>
/// Sprache über den Start hinaus (E95): Der asynchrone Start setzt die Sprache vor seinem ersten await; danach erzeugte
/// Fenster (Einstellungen aus der Bibliothek, aus dem Tray oder über einen zweiten Aufruf) müssen sie noch haben. Wie in
/// der App läuft der Start in einer Operation eines eigenen UI-Threads (Application.Run ruft OnStartup so auf), eine
/// spätere Operation steht für alles, was danach kommt. Allein laufend: <see cref="UiLanguage.Apply"/> stellt auch die
/// Vorgaben aller Threads des Prozesses um (danach wieder Deutsch).
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class AppStartupLanguageTests
{
    [Fact]
    public async Task Language_set_before_the_first_await_stays_on_the_ui_thread_after_the_start()
    {
        var (inStart, later) = await RunStartAsync(keep: true);

        Assert.Equal("en-US", inStart);
        Assert.Equal("en-US", later);
    }

    [Fact]
    public async Task Without_setting_it_again_the_ui_thread_falls_back_after_the_start()
    {
        // Gegenprobe zum Verhalten von WPF: Im weiterlaufenden Start ist die Sprache noch da, jede spätere Operation des
        // UI-Threads bekommt aber die Kultur von vor dem Start zurück (hier die der Tests, de-DE).
        var (inStart, later) = await RunStartAsync(keep: false);

        Assert.Equal("en-US", inStart);
        Assert.Equal("de-DE", later);
    }

    /// <summary>
    /// Ein async-void-Start wie App.StartAsync: Sprache Englisch vor dem ersten await, dann weiter auf dem UI-Thread.
    /// Liefert die Textkultur im weiterlaufenden Start und in einer späteren Operation.
    /// </summary>
    private static async Task<(string InStart, string Later)> RunStartAsync(bool keep)
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "UI-Thread (Startsprache)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var dispatcher = await ready.Task;
        try
        {
            string? chosen = null;
            var continued = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            async void StartAsync()
            {
                UiLanguage.Apply(chosen = UiLanguage.English);
                await Task.Yield();
                continued.SetResult(CultureInfo.CurrentUICulture.Name);
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (keep)
                {
                    App.StartKeepingLanguage(StartAsync, () => chosen);
                }
                else
                {
                    StartAsync();
                }
            });
            var inStart = await continued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var later = await dispatcher.InvokeAsync(() => CultureInfo.CurrentUICulture.Name);
            return (inStart, later);
        }
        finally
        {
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(10));
            CultureInfo.DefaultThreadCurrentCulture = TestCulture.German;
            CultureInfo.DefaultThreadCurrentUICulture = TestCulture.German;
        }
    }
}
