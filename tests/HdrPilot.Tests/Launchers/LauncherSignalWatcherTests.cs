using System.Collections.Concurrent;
using System.Text;
using HdrPilot.Core.Launchers;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Launchers;

/// <summary>Watcher mit Temp-Ordnern in der Rolle der Launcher-Protokolle (echtes Format aus Fixtures\Launchers).</summary>
public sealed class LauncherSignalWatcherTests : IDisposable
{
    private const string SteamLaunch = "[2026-09-23 12:47:32] GameAction [AppID 9990130, ActionID 6] : LaunchApp changed task to CheckShaderDepotManifest with \"\"\r\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly LauncherLogSource _epic;
    private readonly LauncherLogSource _steam;
    private readonly FakeClock _clock = new();
    private readonly ListLogger<LauncherSignalWatcher> _log = new();
    private readonly ConcurrentQueue<LauncherSignal> _signals = new();
    private LauncherSignalWatcher? _watcher;

    public LauncherSignalWatcherTests()
    {
        _epic = new LauncherLogSource(LauncherKind.Epic, Path.Combine(_root, "Epic", "Logs"), LauncherLogLocator.EpicLogFileName);
        _steam = new LauncherLogSource(LauncherKind.Steam, Path.Combine(_root, "Steam", "logs"), LauncherLogLocator.SteamLogFileName);
        Directory.CreateDirectory(_epic.Directory);
        Directory.CreateDirectory(_steam.Directory);
        File.WriteAllBytes(_epic.FullPath, []);
        File.WriteAllBytes(_steam.FullPath, []);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Fixture(string name) => Path.Combine(InMemoryFileSystem.FixturesRoot, "Launchers", name);

    private static void Append(LauncherLogSource source, string text) => Append(source, Encoding.UTF8.GetBytes(text));

    private static void Append(LauncherLogSource source, byte[] bytes)
    {
        using var stream = new FileStream(source.FullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(bytes);
    }

    private static void AppendFixture(LauncherLogSource source, string fixture) => Append(source, File.ReadAllBytes(Fixture(fixture)));

    /// <summary>Ohne eigenen Zeitgeber (wie in der Automatik): Tests rufen CheckNow oder Tick selbst auf.</summary>
    private LauncherSignalWatcher Start(TimeSpan? ownTickInterval = null, Action<LauncherSignal>? onSignal = null, params LauncherLogSource[] sources)
    {
        _watcher = new LauncherSignalWatcher(sources.Length == 0 ? [_epic, _steam] : sources, _clock, onSignal ?? _signals.Enqueue, _log, ownTickInterval);
        _watcher.Start();
        return _watcher;
    }

    private async Task<LauncherSignal> NextSignalAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (_signals.TryDequeue(out var signal))
            {
                return signal;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Kein Launcher-Signal innerhalb von " + timeout);
    }

    /// <summary>Reihenfolge zwischen den Launchern hängt davon ab, ob ein Dateiereignis zuerst prüft.</summary>
    private List<LauncherSignal> SortedSignals() => _signals.OrderBy(s => s.Launcher).ThenBy(s => s.ActionId).ToList();

    [Fact]
    public void Launch_lines_appended_after_start_are_reported_once_per_launch()
    {
        var watcher = Start();
        AppendFixture(_steam, "console_log.txt");
        AppendFixture(_epic, "EpicGamesLauncher.log");

        watcher.CheckNow();

        Assert.Equal(
            [
                new LauncherSignal(LauncherKind.Epic, "Epic:Crow"), // URI und Bibliothek innerhalb von 10 s: ein Start
                new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 5),
                new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 6),
            ],
            SortedSignals());
    }

    [Fact]
    public void Lines_written_before_start_are_never_reported()
    {
        AppendFixture(_steam, "console_log.txt");
        AppendFixture(_epic, "EpicGamesLauncher.log");
        var watcher = Start();

        watcher.CheckNow(verifyPaths: true);
        Append(_steam, "[2026-09-23 12:53:00] STARTUP SlowJSMethodCall took 1.0ms\r\n");
        watcher.CheckNow();

        Assert.Empty(_signals);
    }

    [Fact]
    public void Steam_log_renamed_at_steam_start_is_followed_into_the_new_file()
    {
        AppendFixture(_steam, "console_log.txt");
        var watcher = Start();
        File.Move(_steam.FullPath, Path.Combine(_steam.Directory, "console_log.previous.txt"));
        Append(_steam, "\r\n\r\n[2026-09-23 14:43:00] Startup - updater built Oct  1 2026\r\n"
            + "[2026-09-23 14:43:05] GameAction [AppID 1091500, ActionID 1] : LaunchApp changed task to ProcessingInstallScript with \"\"\r\n");

        watcher.CheckNow(verifyPaths: true); // im Betrieb löst das Dateiereignis „umbenannt“ diese Prüfung aus

        Assert.Equal([new LauncherSignal(LauncherKind.Steam, "Steam:1091500", 1)], _signals);
    }

    [Fact]
    public void Epic_log_rotated_to_a_backup_is_followed_into_the_new_file()
    {
        // Epic legt beim Start eine Sicherung an; der Start über eine Verknüpfung steht dann in den ersten Zeilen.
        AppendFixture(_epic, "EpicGamesLauncher.log");
        var watcher = Start();
        File.Move(_epic.FullPath, Path.Combine(_epic.Directory, "EpicGamesLauncher-backup-2026.09.23-11.51.44.log"));
        AppendFixture(_epic, "EpicGamesLauncher.log");

        watcher.CheckNow(verifyPaths: true);

        Assert.Equal([new LauncherSignal(LauncherKind.Epic, "Epic:Crow")], _signals);
    }

    [Fact]
    public async Task File_events_trigger_a_check_without_any_tick()
    {
        Start();

        Append(_epic, "[2026.09.23-10.55.40:042][824]LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received a:b:Crow library \r\n");

        Assert.Equal("Epic:Crow", (await NextSignalAsync(TimeSpan.FromSeconds(10))).GameKey);
    }

    [Fact]
    public async Task Rename_event_follows_the_new_file_without_any_tick()
    {
        Start();
        File.Move(_steam.FullPath, Path.Combine(_steam.Directory, "console_log.previous.txt"));
        Append(_steam, SteamLaunch);

        Assert.Equal(new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 6), await NextSignalAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Tick_checks_the_open_file_every_time_and_the_path_only_every_thirtieth_time()
    {
        // Protokollordner fehlt beim Start: kein FileSystemWatcher, nur der Takt (wie bei verlorenen Dateiereignissen).
        var late = new LauncherLogSource(LauncherKind.Steam, Path.Combine(_root, "SpaeterSteam", "logs"), LauncherLogLocator.SteamLogFileName);
        var watcher = Start(null, null, late);
        Assert.True(_log.Contains(LogLevel.Warning, "nur Prüfung im Sekundentakt"));
        Directory.CreateDirectory(late.Directory);
        Append(late, SteamLaunch);

        for (var tick = 1; tick < LauncherSignalWatcher.VerifyMissingEveryTicks; tick++)
        {
            watcher.Tick();
        }

        Assert.Empty(_signals); // fehlende Datei: Pfad nur jeden fünften Takt
        watcher.Tick();
        Assert.Equal([new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 6)], _signals);

        // Offen: jeder Takt sieht Angehängtes über das Handle.
        Append(late, SteamLaunch.Replace("ActionID 6", "ActionID 7", StringComparison.Ordinal));
        watcher.Tick();
        Assert.Equal(2, _signals.Count);

        // Ersetzt ohne Dateiereignis: erst die Pfadprüfung im 30. Takt findet die neue Datei.
        File.Move(late.FullPath, late.FullPath + ".previous");
        Append(late, SteamLaunch.Replace("ActionID 6", "ActionID 8", StringComparison.Ordinal));
        for (var tick = LauncherSignalWatcher.VerifyMissingEveryTicks + 2; tick < LauncherSignalWatcher.VerifyEveryTicks; tick++)
        {
            watcher.Tick();
        }

        Assert.Equal(2, _signals.Count);
        watcher.Tick();
        Assert.Equal(8, _signals.Last().ActionId);
    }

    [Fact]
    public async Task Own_tick_interval_works_without_file_events()
    {
        var late = new LauncherLogSource(LauncherKind.Steam, Path.Combine(_root, "SpaeterSteam", "logs"), LauncherLogLocator.SteamLogFileName);
        Start(TimeSpan.FromMilliseconds(50), null, late);
        Directory.CreateDirectory(late.Directory);
        Append(late, SteamLaunch);

        Assert.Equal(new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 6), await NextSignalAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Nothing_is_reported_after_dispose_and_the_files_are_released()
    {
        var watcher = Start();
        watcher.Dispose();
        AppendFixture(_steam, "console_log.txt");

        watcher.CheckNow(verifyPaths: true);
        watcher.Tick();

        Assert.Empty(_signals);
        using (new FileStream(_steam.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // exklusiv öffnen geht nur ohne offenes Handle des Watchers
        }
    }

    [Fact]
    public void Failing_receiver_does_not_stop_the_watcher()
    {
        var calls = 0;
        var watcher = Start(onSignal: s =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Empfänger kaputt");
        });
        Append(_steam, "[2026-09-23 12:44:30] GameAction [AppID 1, ActionID 1] : LaunchApp changed task to A with \"\"\r\n");
        watcher.CheckNow();
        Append(_steam, "[2026-09-23 12:44:31] GameAction [AppID 2, ActionID 2] : LaunchApp changed task to A with \"\"\r\n");
        watcher.CheckNow();

        Assert.Equal(2, calls);
        Assert.True(_log.Contains(LogLevel.Warning, "ließ sich nicht weitergeben"));
    }

    [Fact]
    public void Raw_log_lines_never_reach_the_log()
    {
        // Die Zeilen eines Starts enthalten Anmeldecode und Benutzerdaten (hier anonymisiert als REDACTED/User).
        var watcher = Start(onSignal: s => throw new InvalidOperationException("erzwingt eine Warnung mit Schlüssel"));
        AppendFixture(_epic, "EpicGamesLauncher.log");
        AppendFixture(_steam, "console_log.txt");
        watcher.CheckNow();

        Assert.NotEmpty(_log.Entries);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("AUTH", StringComparison.OrdinalIgnoreCase)
            || e.Message.Contains("REDACTED", StringComparison.Ordinal)
            || e.Message.Contains("epicuser", StringComparison.OrdinalIgnoreCase)
            || e.Message.Contains("GameAction", StringComparison.Ordinal)
            || e.Message.Contains("0123456789abcdef", StringComparison.Ordinal));
    }
}
