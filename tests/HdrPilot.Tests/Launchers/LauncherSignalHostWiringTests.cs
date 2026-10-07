using System.Collections.Concurrent;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Launchers;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Launchers;

/// <summary>
/// Einbau der Frühwarnung in die Automatik (<see cref="AutomationHost"/>): Start nach dem ersten Zyklus, Takt der
/// Prozessüberwachung, Einstellung im laufenden Betrieb, Freigabe beim Beenden. Ablage, Launcher-Ordner und Registry
/// zeigen auf Temp-Ordner; die Konfiguration enthält kein Spiel, also wird nie HDR geschaltet.
/// </summary>
public sealed class LauncherSignalHostWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly string _steamLog;
    private readonly FakeEnvironment _environment = new();
    private readonly FakeRegistry _registry = new();
    private readonly CapturingLoggerProvider _capture = new();

    public LauncherSignalHostWiringTests()
    {
        var local = Path.Combine(_root, "Local");
        var steam = Path.Combine(_root, "Steam");
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = local;
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", steam.Replace('\\', '/'));
        Directory.CreateDirectory(Path.Combine(local, "EpicGamesLauncher", "Saved", "Logs"));
        _steamLog = Path.Combine(steam, "logs", "console_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_steamLog)!);
        File.WriteAllText(_steamLog, "\r\n\r\n[2026-10-07 18:00:00] Startup\r\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AutomationHost CreateHost() =>
        new(AppPaths.InDirectory(Path.Combine(_root, "Ablage")), PhysicalFileSystem.Instance, _registry, _environment, SystemClock.Instance, [_capture]);

    [Fact]
    public async Task Host_watches_only_after_start_follows_the_setting_and_releases_the_logs()
    {
        await using (var host = CreateHost())
        {
            Assert.Empty(host.LauncherSignals.ActiveSources); // vor dem ersten Zyklus nichts

            await host.StartAsync(Ct);
            Assert.Equal([LauncherKind.Epic, LauncherKind.Steam], host.LauncherSignals.ActiveSources.Select(s => s.Launcher));

            host.Config.Update(c => c.Settings.LauncherSignals = false);
            Assert.Empty(host.LauncherSignals.ActiveSources);
            host.Config.Update(c => c.Settings.LauncherSignals = true);
            Assert.Equal(2, host.LauncherSignals.ActiveSources.Count);
        }

        using (new FileStream(_steamLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // exklusiv öffnen geht nur, wenn die Automatik das Protokoll losgelassen hat
        }
    }

    [Fact]
    public async Task Poll_cycle_ticks_the_launcher_signals_without_file_events()
    {
        await using var host = CreateHost();
        host.Config.Update(c => c.Settings.PollIntervalMs = 250);
        await host.StartAsync(Ct);
        host.LauncherSignals.SuspendFileEvents();

        using (var writer = new FileStream(_steamLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            writer.Write(Encoding.UTF8.GetBytes("[2026-10-07 18:00:05] GameAction [AppID 999999, ActionID 1] : LaunchApp changed task to ProcessingInstallScript with \"\"\r\n"));
        }

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until && !_capture.Contains("Launcher-Signal von Steam für Steam:999999: nicht in der Bibliothek"))
        {
            await Task.Delay(50, Ct);
        }

        Assert.True(_capture.Contains("Launcher-Signal von Steam für Steam:999999: nicht in der Bibliothek"), "Signal kam nicht über den Takt an");
        Assert.True(_capture.Contains("Frühwarnung aus Launcher-Protokollen aktiv: Epic, Steam"));
    }

    /// <summary>Sammelt alle Protokollzeilen der Automatik (alle Stufen).</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public bool Contains(string fragment) => _lines.Any(l => l.Contains(fragment, StringComparison.Ordinal));

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}
