using System.Collections.Concurrent;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Platform;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Hosting;

/// <summary>
/// Warnung beim Start, wenn HDRpilot im Container einer anderen App läuft (E84): einmal je Start, nach der Startzeile, im
/// Hintergrund, nie ein Abbruch, unabhängig vom Ort der Ablage. Die Prüfung ist meist eine Attrappe; Ablage und
/// Launcher-Ordner liegen in einem Temp-Ordner, die Konfiguration enthält kein Spiel (es wird nie HDR geschaltet).
/// </summary>
public sealed class ContainerStartupWarningTests : IDisposable
{
    private const string Package = "Claude_pzs8sxrjxfjjc";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.StartCheck-" + Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment _environment = new();
    private readonly CapturingLoggerProvider _capture = new();
    private readonly FakeContainerDetector _detector = new();

    public ContainerStartupWarningTests()
    {
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = Local;
        Directory.CreateDirectory(Local);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Local => Path.Combine(_root, "Local");

    private AppPaths Paths => AppPaths.InDirectory(Path.Combine(_root, "Ablage"));

    [Fact]
    public async Task Container_is_warned_about_once_after_the_start_line()
    {
        _detector.Package = Package;
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.StartAsync(Ct); // zweiter Aufruf startet nichts neu
            await host.ContainerCheck;
        }

        Assert.Equal(1, _detector.Calls);
        var lines = _capture.Lines;
        var warning = Assert.Single(lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(WarningFor(Package), warning.Message);
        var start = lines.FindIndex(l => l.Message.Contains(" startet. Konfiguration ", StringComparison.Ordinal));
        Assert.InRange(start, 0, lines.IndexOf(warning) - 1);
    }

    [Fact]
    public async Task Outside_a_container_nothing_extra_is_logged()
    {
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        Assert.Equal(1, _detector.Calls);
        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("Container-Prüfung", StringComparison.Ordinal) || l.Message.Contains("im Container der App", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failing_or_inconclusive_check_never_stops_the_start()
    {
        _detector.Failure = new InvalidOperationException("Probe kaputt");
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
            Assert.True(host.Watcher.IsRunning);
        }

        Assert.Contains(_capture.Lines, l => l.Level == LogLevel.Information && l.Message == "Container-Prüfung fehlgeschlagen." && l.Exception is InvalidOperationException);
        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));

        _detector.Failure = null;
        _detector.Problem = "Probe schreiben oder suchen: IOException: Datenträger voll";
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        Assert.Contains(_capture.Lines, l => l.Level == LogLevel.Information && l.Message == "Container-Prüfung mit Probe in " + FakeContainerDetector.Folder + ": " + _detector.Problem);
        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Slow_check_holds_up_neither_the_start_nor_the_watcher()
    {
        // Die Probe kann dauern (Virenscanner, viele Apps unter Packages): Start und Überwachung laufen, solange sie hängt.
        using var release = new ManualResetEventSlim();
        _detector.Package = Package;
        _detector.Gate = release;
        await using (var host = CreateHost())
        {
            try
            {
                await Task.Run(() => host.StartAsync(Ct), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
                await _detector.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
                Assert.True(host.Watcher.IsRunning);
                Assert.False(host.ContainerCheck.IsCompleted); // hängt noch in der Probe
                Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));
            }
            finally
            {
                release.Set();
            }

            await host.ContainerCheck;
        }

        Assert.Equal(1, _detector.Calls);
        Assert.Single(_capture.Lines, l => l.Message == WarningFor(Package));
    }

    [Fact]
    public async Task Default_check_probes_local_app_data_and_leaves_nothing_behind()
    {
        // Ohne Attrappe prüft der echte ContainerDetector im (nachgebildeten) %LOCALAPPDATA%, nicht in der Ablage.
        await using (var host = new AutomationHost(Paths, PhysicalFileSystem.Instance, new FakeRegistry(), _environment, SystemClock.Instance, [_capture]))
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("Container-Prüfung", StringComparison.Ordinal) || l.Message.Contains("im Container der App", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Local));
        Assert.Empty(Directory.EnumerateFiles(Paths.DataDirectory, ContainerDetector.ProbePrefix + "*"));
    }

    [Fact]
    public async Task Real_check_warns_in_a_container_even_when_the_data_folder_exists_outside_it()
    {
        // Gegen das echte Profil. Die Ablage besteht schon und liegt im Temp-Ordner, den Windows nie umleitet: wie ein
        // schon vorhandenes %LOCALAPPDATA%\HDRpilot ohne Kopie im Container. Ob dieser Testlauf im Container einer App
        // läuft, sagt eine eigene Probe direkt in %LOCALAPPDATA%; die Warnung muss genau dann kommen.
        Directory.CreateDirectory(Paths.DataDirectory);
        File.WriteAllText(Path.Combine(Paths.DataDirectory, "vorhanden.txt"), "echt");
        var container = ContainerOf(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var dataFolderRedirected = ContainerOf(Paths.DataDirectory);

        await using (var host = new AutomationHost(
            Paths, PhysicalFileSystem.Instance, new FakeRegistry(), _environment, SystemClock.Instance, [_capture], new ContainerDetector(SystemEnvironment.Instance)))
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(container is null
            ? "Dieser Testlauf läuft in keinem Container"
            : "Dieser Testlauf läuft im Container der App " + container + "; eine Probe in der Ablage " + Paths.DataDirectory + " wird "
                + (dataFolderRedirected is null ? "nicht" : "auch") + " umgeleitet");
        var warnings = _capture.Lines.Where(l => l.Message.Contains("im Container der App", StringComparison.Ordinal)).Select(l => l.Message).ToList();
        if (container is null)
        {
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Null(dataFolderRedirected); // Vorbedingung: eine Probe in der Ablage sähe unverdächtig aus
            Assert.Equal(WarningFor(container), Assert.Single(warnings));
        }

        Assert.DoesNotContain(_capture.Lines, l => l.Message.StartsWith("Container-Prüfung", StringComparison.Ordinal));
        Assert.Equal("echt", File.ReadAllText(Path.Combine(Paths.DataDirectory, "vorhanden.txt")));
        Assert.Empty(Directory.EnumerateFiles(Paths.DataDirectory, "*Probe*"));
    }

    private static string WarningFor(string package) =>
        "HDRpilot läuft im Container der App " + package + ": Windows leitet Registry-Einträge wie den Autostart und je nach Ordner auch Einstellungen und Protokolle in deren privaten Speicher um. Bitte HDRpilot über das Startmenü oder die Verknüpfung starten.";

    /// <summary>
    /// Eigene Probe in <paramref name="folder"/> (unter dem echten %LOCALAPPDATA%): App, in deren
    /// <c>Packages\&lt;App&gt;\LocalCache\Local</c> sie am gleichen relativen Pfad auftaucht, sonst null. Immer gelöscht.
    /// </summary>
    private static string? ContainerOf(string folder)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var probe = Path.Combine(folder, "HDRpilot.Tests.Probe-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(probe, []);
        try
        {
            var packages = Path.Combine(local, "Packages");
            var relative = Path.GetRelativePath(local, probe);
            return Directory.Exists(packages)
                ? Directory.EnumerateDirectories(packages).Where(p => File.Exists(Path.Combine(p, "LocalCache", "Local", relative))).Select(Path.GetFileName).FirstOrDefault()
                : null;
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private AutomationHost CreateHost() =>
        new(Paths, PhysicalFileSystem.Instance, new FakeRegistry(), _environment, SystemClock.Instance, [_capture], _detector);

    private sealed class FakeContainerDetector : IContainerDetector
    {
        public const string Folder = @"C:\Users\Test\AppData\Local";
        private int _calls;

        public string? Package { get; set; }

        public string? Problem { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>Hält die Prüfung an, bis es gesetzt ist (langsame Probe); null: sofort fertig.</summary>
        public ManualResetEventSlim? Gate { get; set; }

        /// <summary>Erfüllt, sobald die Prüfung begonnen hat.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public ContainerRedirection Detect()
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            Gate?.Wait();
            if (Failure is not null)
            {
                throw Failure;
            }

            return new ContainerRedirection(Folder, Package, Problem);
        }
    }

    /// <summary>Sammelt alle Protokollzeilen der Automatik (alle Stufen, mit Ausnahme).</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _lines = new();

        public List<(LogLevel Level, string Message, Exception? Exception)> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }
}
