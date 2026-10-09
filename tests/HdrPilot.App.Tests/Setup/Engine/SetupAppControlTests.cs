using System.Collections.Concurrent;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Setup;
using HdrPilot.Core.Processes;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>
/// Echte App-Steuerung ohne Prozess-Handle: erkennt laufende Prozesse über ihren EXE-Pfad aus der Prozessliste und die
/// Einzelinstanz über ihre Sperre. Startet und beendet keinen Prozess (Einzelinstanz-Kennungen sind erfunden und gehören
/// einer Testinstanz in diesem Prozess, EXE-Pfade sind erfunden oder gehören laufenden Windows-Prozessen).
/// </summary>
public sealed class SetupAppControlTests
{
    private static string UnusedInstance() => "setup-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Running_process_is_found_by_its_exe_path_without_a_handle()
    {
        // svchost.exe läuft unter Windows immer (mehrfach); der eigene Prozess zählt nie.
        var svchost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "svchost.exe");

        Assert.True(new SetupAppControl(UnusedInstance(), svchost.ToUpperInvariant()).IsRunning());
    }

    [Fact]
    public void Nothing_running_from_the_exe_counts_as_stopped_without_sending_anything()
    {
        var control = new SetupAppControl(UnusedInstance(), Path.Combine(Path.GetTempPath(), "HDRpilot.Tests." + Guid.NewGuid().ToString("N"), "HDRpilot.exe"));

        Assert.False(control.IsRunning());
        Assert.True(control.Stop(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Own_process_never_counts_as_the_installed_app_but_any_other_process_from_the_exe_does()
    {
        const string Exe = @"C:\Users\U\AppData\Local\Programs\HDRpilot\HDRpilot.exe";
        var own = new ProcessInfo(Environment.ProcessId, "HDRpilot.exe", Exe);
        var other = new ProcessInfo(Environment.ProcessId + 1, "HDRpilot.exe", Exe.ToLowerInvariant());
        var portable = new ProcessInfo(Environment.ProcessId + 2, "HDRpilot.exe", @"D:\Portable\HDRpilot.exe");
        var unreadable = new ProcessInfo(Environment.ProcessId + 3, "HDRpilot.exe", null);

        Assert.False(new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(own, portable, unreadable)).IsRunning());
        Assert.True(new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(own, other)).IsRunning());
    }

    [Fact]
    public void Process_that_keeps_running_without_an_instance_is_reported_after_the_timeout()
    {
        const string Exe = @"C:\Users\U\AppData\Local\Programs\HDRpilot\HDRpilot.exe";
        var hung = new ProcessInfo(Environment.ProcessId + 1, "HDRpilot.exe", Exe);
        var control = new SetupAppControl(UnusedInstance(), Exe, () => new FixedSnapshot(hung));

        Assert.False(control.Stop(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void Running_instance_gets_exit_through_the_command_path_and_then_counts_as_stopped()
    {
        var id = UnusedInstance();
        using var app = new TestInstance(id, exitOnRequest: true);
        var control = new SetupAppControl(id, NotInstalledExe(), () => new FixedSnapshot());

        Assert.True(control.IsRunning()); // nur die Einzelinstanz, kein Prozess aus der EXE
        Assert.True(control.Stop(TimeSpan.FromSeconds(5)));

        Assert.Equal(["--exit"], Assert.Single(app.Received));
        Assert.False(control.IsRunning());
    }

    [Fact]
    public void Instance_that_ignores_exit_is_reported_after_the_timeout_and_never_ended_otherwise()
    {
        var id = UnusedInstance();
        using var app = new TestInstance(id, exitOnRequest: false);
        var control = new SetupAppControl(id, NotInstalledExe(), () => new FixedSnapshot());

        Assert.False(control.Stop(TimeSpan.FromMilliseconds(500)));

        Assert.Equal(["--exit"], Assert.Single(app.Received));
        Assert.True(control.IsRunning());
    }

    [Fact]
    public void Exe_of_the_running_instance_comes_from_its_window_and_the_process_list()
    {
        const string Portable = @"D:\Portable\HDRpilot.exe";
        var id = UnusedInstance();
        using var app = new TestInstance(id, exitOnRequest: false);
        var processes = new FixedSnapshot(
            new ProcessInfo(Environment.ProcessId + 1, "HDRpilot.exe", @"C:\Andere\HDRpilot.exe"),
            new ProcessInfo(Environment.ProcessId, "HDRpilot.exe", Portable));

        Assert.Equal(Environment.ProcessId, SingleInstance.OwnerProcessId(id));
        Assert.Equal(Portable, new SetupAppControl(id, NotInstalledExe(), () => processes).InstanceExePath());
        Assert.Null(new SetupAppControl(UnusedInstance(), NotInstalledExe(), () => processes).InstanceExePath());
    }

    private static string NotInstalledExe() => Path.Combine(Path.GetTempPath(), "HDRpilot.Tests." + Guid.NewGuid().ToString("N"), "HDRpilot.exe");

    private sealed class FixedSnapshot(params ProcessInfo[] processes) : IProcessSnapshotProvider
    {
        public IReadOnlyList<ProcessInfo> GetProcesses() => processes;
    }

    /// <summary>
    /// Echte Einzelinstanz mit Nachrichtenfenster auf einem eigenen STA-Thread, wie die laufende App (eigene Kennung, nie
    /// die echte). Merkt sich die empfangenen Befehle; mit <c>exitOnRequest</c> endet sie bei „--exit“ wie die App: erst
    /// nach der Antwort, Sperre und Fenster weg.
    /// </summary>
    private sealed class TestInstance : IDisposable
    {
        private readonly ConcurrentQueue<string[]> _received = new();
        private readonly ManualResetEventSlim _ready = new();
        private readonly Thread _thread;
        private Dispatcher? _dispatcher;
        private Exception? _startError;

        public TestInstance(string id, bool exitOnRequest)
        {
            _thread = new Thread(() =>
            {
                SingleInstance? instance = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    instance = SingleInstance.TryAcquire(id) ?? throw new InvalidOperationException("Kennung belegt: " + id);
                    instance.Listen(args =>
                    {
                        _received.Enqueue([.. args]);
                        if (exitOnRequest && args.Contains("--exit"))
                        {
                            dispatcher.InvokeAsync(dispatcher.InvokeShutdown); // erst nach der Antwort
                        }
                    });
                    _dispatcher = dispatcher;
                }
                catch (Exception ex)
                {
                    _startError = ex;
                    instance?.Dispose();
                    _ready.Set();
                    return;
                }

                _ready.Set();
                Dispatcher.Run();
                instance.Dispose(); // auf dem Thread, der die Sperre hält
            })
            {
                IsBackground = true,
                Name = "Einzelinstanz (Test)",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)), "Testinstanz startet nicht.");
            Assert.Null(_startError);
        }

        public IReadOnlyList<string[]> Received => [.. _received];

        public void Dispose()
        {
            if (_thread.IsAlive)
            {
                _dispatcher?.InvokeShutdown();
            }

            _thread.Join(TimeSpan.FromSeconds(10));
            _ready.Dispose();
        }
    }

    [Fact]
    public void Default_setup_version_is_the_file_version_of_the_app()
    {
        var app = typeof(SetupEngine).Assembly;

        Assert.Equal(SetupSupport.Normalize(app.GetName().Version!), SetupSupport.ReadFileVersion(app.Location));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\HDRpilot\\HDRpilot.exe\" --minimized", true)]
    [InlineData("\"c:\\apps\\hdrpilot\\HDRPILOT.EXE\"", true)]
    [InlineData("C:\\Apps\\HDRpilot\\HDRpilot.exe --minimized", true)]
    [InlineData("\"D:\\Other\\HDRpilot.exe\" --minimized", false)]
    [InlineData("\"C:\\Apps\\HDRpilot\\HDRpilot.exe.old\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Autostart_command_points_to_the_exe_only_for_the_same_path(string? command, bool expected) =>
        Assert.Equal(expected, SetupSupport.PointsTo(command, @"C:\Apps\HDRpilot\HDRpilot.exe"));
}
