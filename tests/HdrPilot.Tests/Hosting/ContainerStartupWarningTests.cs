using System.Collections.Concurrent;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Platform;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Hosting;

/// <summary>
/// Warnung beim Start, wenn die Ablage im Container einer anderen App landet (E84): einmal je Start, nach der
/// Startzeile, im Hintergrund, nie ein Abbruch. Die Prüfung ist eine Attrappe; Ablage und Launcher-Ordner liegen in einem
/// Temp-Ordner, die Konfiguration enthält kein Spiel (es wird nie HDR geschaltet).
/// </summary>
public sealed class ContainerStartupWarningTests : IDisposable
{
    private const string Warning = "HDRpilot läuft im Container der App Claude_pzs8sxrjxfjjc: Windows leitet Einstellungen, Protokolle und Registry-Einträge (Autostart) in deren privaten Speicher um. Bitte HDRpilot über das Startmenü oder die Verknüpfung starten.";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.StartCheck-" + Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment _environment = new();
    private readonly CapturingLoggerProvider _capture = new();
    private readonly FakeContainerDetector _detector = new();

    public ContainerStartupWarningTests()
    {
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "Local");
        Directory.CreateDirectory(Path.Combine(_root, "Local"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AppPaths Paths => AppPaths.InDirectory(Path.Combine(_root, "Ablage"));

    [Fact]
    public async Task Redirected_data_folder_is_warned_about_once_after_the_start_line()
    {
        _detector.Package = "Claude_pzs8sxrjxfjjc";
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.StartAsync(Ct); // zweiter Aufruf startet nichts neu
            await host.ContainerCheck;
        }

        Assert.Equal([Paths.DataDirectory], _detector.Folders);
        var lines = _capture.Lines;
        var warning = Assert.Single(lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(Warning, warning.Message);
        var start = lines.FindIndex(l => l.Message.Contains(" startet. Konfiguration ", StringComparison.Ordinal));
        Assert.InRange(start, 0, lines.IndexOf(warning) - 1);
    }

    [Fact]
    public async Task Data_folder_outside_a_container_logs_nothing_extra()
    {
        await using (var host = CreateHost())
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        Assert.Single(_detector.Folders);
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

        Assert.Contains(_capture.Lines, l => l.Level == LogLevel.Information && l.Message == "Container-Prüfung für " + Paths.DataDirectory + ": " + _detector.Problem);
        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("im Container der App", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Default_check_of_a_folder_outside_AppData_writes_no_probe()
    {
        // Ohne Attrappe prüft der echte ContainerDetector; die Ablage liegt nicht unter dem (nachgebildeten) AppData.
        await using (var host = new AutomationHost(Paths, PhysicalFileSystem.Instance, new FakeRegistry(), _environment, SystemClock.Instance, [_capture]))
        {
            await host.StartAsync(Ct);
            await host.ContainerCheck;
        }

        Assert.DoesNotContain(_capture.Lines, l => l.Message.Contains("Container-Prüfung", StringComparison.Ordinal) || l.Message.Contains("im Container der App", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(Paths.DataDirectory, ContainerDetector.ProbePrefix + "*"));
    }

    private AutomationHost CreateHost() =>
        new(Paths, PhysicalFileSystem.Instance, new FakeRegistry(), _environment, SystemClock.Instance, [_capture], _detector);

    private sealed class FakeContainerDetector : IContainerDetector
    {
        private readonly ConcurrentQueue<string> _folders = new();

        public string? Package { get; set; }

        public string? Problem { get; set; }

        public Exception? Failure { get; set; }

        public IReadOnlyList<string> Folders => [.. _folders];

        public ContainerRedirection Detect(string folder)
        {
            _folders.Enqueue(folder);
            if (Failure is not null)
            {
                throw Failure;
            }

            return new ContainerRedirection(folder, Package, Problem);
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
