using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Launchers;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Launchers;

/// <summary>
/// Zusammenbau der Frühwarnung wie in der Automatik: Launcher-Ordner über Registry und Umgebung (Attrappen, die auf
/// Temp-Ordner zeigen), Einstellung an/aus im laufenden Betrieb, Signale bis zur Sitzungslogik.
/// </summary>
public sealed class LauncherSignalServiceTests : IDisposable
{
    private const string SteamLaunch = "[2026-09-23 12:44:30] GameAction [AppID 9990130, ActionID 5] : LaunchApp changed task to CheckShaderDepotManifest with \"\"\r\n";
    private const string EpicLaunch = "[2026.09.23-10.55.40:042][824]LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received a:b:Crow library \r\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly string _epicLog;
    private readonly string _steamLog;
    private readonly FakeEnvironment _environment = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeClock _clock = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeSessionConfig _config = new();
    private readonly ListLogger<LauncherSignalService> _log = new();
    private readonly SessionManager _manager;
    private LauncherSignalService? _service;

    public LauncherSignalServiceTests()
    {
        var local = Path.Combine(_root, "Local");
        var steam = Path.Combine(_root, "Program Files (x86)", "Steam");
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = local;
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", steam.Replace('\\', '/').ToLowerInvariant());
        _epicLog = Path.Combine(local, "EpicGamesLauncher", "Saved", "Logs", "EpicGamesLauncher.log");
        _steamLog = Path.Combine(steam, "logs", "console_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_epicLog)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_steamLog)!);
        File.WriteAllText(_steamLog, "\r\n\r\n" + SteamLaunch.Replace("ActionID 5", "ActionID 1", StringComparison.Ordinal)); // alter Start: nie nachspielen

        _config.AddGame("9990130", @"D:\Steam-Bibliothek\steamapps\common\Aschenacht");
        _manager = new SessionManager(_hdr, _config, new InMemorySessionStateStore(), new FakeLauncher(), _clock, (_, _) => false, [], new ListLogger<SessionManager>());
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LauncherSignalService CreateService() =>
        _service = LauncherSignalService.Create(_config, _manager, PhysicalFileSystem.Instance, _registry, _environment, _clock, _log);

    private async Task<LauncherSignalService> StartedService()
    {
        await _manager.UpdateAsync(new ProcessScript().Next(), Ct); // erster Zyklus wie in der Automatik
        var service = CreateService();
        service.Start();
        return service;
    }

    private static void Append(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private async Task CheckAsync(LauncherSignalService service)
    {
        service.CheckNow(verifyPaths: true); // im Betrieb: Dateiereignis oder Takt
        await service.WhenIdleAsync();
    }

    [Fact]
    public async Task Finds_both_launchers_and_switches_hdr_on_a_steam_launch_line()
    {
        var service = await StartedService();
        Assert.Equal([LauncherKind.Epic, LauncherKind.Steam], service.ActiveSources.Select(s => s.Launcher));
        Assert.Equal(_steamLog, service.ActiveSources[1].FullPath, ignoreCase: true);
        await CheckAsync(service);
        Assert.Empty(_manager.Sessions); // alter Start in der Datei

        Append(_steamLog, SteamLaunch);
        await CheckAsync(service);

        var session = Assert.Single(_manager.Sessions);
        Assert.Equal(("Steam:9990130", SessionTrigger.LauncherSignal, SessionPhase.Launching), (session.GameKey, session.Trigger, session.Phase));
        Assert.True(_hdr[FakeHdrController.Primary].HdrOn);
        Assert.True(_log.Contains(LogLevel.Information, "Frühwarnung aus Launcher-Protokollen aktiv: Epic, Steam"));
    }

    [Fact]
    public async Task Tick_of_the_process_poll_reads_appended_launch_lines()
    {
        var service = await StartedService();

        Append(_steamLog, SteamLaunch);
        service.Tick(); // so ruft die Automatik es in jedem Zyklus der Prozessüberwachung auf
        await service.WhenIdleAsync();

        Assert.Equal(SessionTrigger.LauncherSignal, Assert.Single(_manager.Sessions).Trigger);
    }

    [Fact]
    public async Task Epic_launch_line_reaches_the_session_logic()
    {
        _config.AddGame("Crow", @"D:\Anwendungen\Epic Games\DeadIsland2", source: GameSource.Epic);
        var service = await StartedService();

        Append(_epicLog, "\uFEFFLog file open, 09/23/26 11:56:42\r\n" + EpicLaunch);
        await CheckAsync(service);

        Assert.Equal("Epic:Crow", Assert.Single(_manager.Sessions).GameKey);
    }

    [Fact]
    public async Task Setting_off_watches_nothing_and_can_be_switched_on_live()
    {
        _config.Config.Settings.LauncherSignals = false;
        var service = await StartedService();
        Assert.Empty(service.ActiveSources);

        Append(_steamLog, SteamLaunch); // während aus: wird auch später nicht nachgespielt
        _config.Config.Settings.LauncherSignals = true;
        service.ApplySettings();
        Assert.Equal(2, service.ActiveSources.Count);
        await CheckAsync(service);
        Assert.Empty(_manager.Sessions);

        Append(_steamLog, SteamLaunch.Replace("ActionID 5", "ActionID 6", StringComparison.Ordinal));
        await CheckAsync(service);
        Assert.Single(_manager.Sessions);
    }

    [Fact]
    public async Task Switching_the_setting_off_stops_watching()
    {
        var service = await StartedService();
        _config.Config.Settings.LauncherSignals = false;
        service.ApplySettings();

        Assert.Empty(service.ActiveSources);
        Append(_steamLog, SteamLaunch);
        await CheckAsync(service);
        Assert.Empty(_manager.Sessions);
        Assert.True(_log.Contains(LogLevel.Information, "Frühwarnung aus Launcher-Protokollen ausgeschaltet"));
    }

    [Fact]
    public async Task Missing_launchers_start_nothing_and_are_logged_once()
    {
        Directory.Delete(_root, recursive: true);
        var service = await StartedService();
        service.ApplySettings();

        Assert.Empty(service.ActiveSources);
        Assert.Single(_log.Entries, e => e.Message.Contains("weder Epic Games Launcher noch Steam", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failing_watcher_start_never_breaks_the_automation()
    {
        // Die Frühwarnung ist eine Zugabe: Start der Automatik und Speichern der Konfiguration dürfen nicht scheitern.
        await _manager.UpdateAsync(new ProcessScript().Next(), Ct);
        var broken = new LauncherLogSource(LauncherKind.Steam, Path.GetDirectoryName(_steamLog)!, "console\0log.txt");
        _service = new LauncherSignalService(
            _config, new LauncherSignalHandler(_config, new SessionManagerSignalTarget(_manager)), () => [broken], _clock, _log);

        _service.Start();
        _service.ApplySettings();

        Assert.Empty(_service.ActiveSources);
        Assert.True(_log.Contains(LogLevel.Warning, "ließ sich nicht starten"));
    }

    [Fact]
    public void Nothing_runs_before_start()
    {
        var service = CreateService();
        service.ApplySettings(); // Konfigurationsänderung vor dem ersten Zyklus

        Assert.Empty(service.ActiveSources);
    }

    [Fact]
    public async Task Nothing_runs_after_dispose()
    {
        var service = await StartedService();
        service.Dispose();
        service.ApplySettings();

        Assert.Empty(service.ActiveSources);
        Append(_steamLog, SteamLaunch);
        service.CheckNow();
        Assert.Empty(_manager.Sessions);
    }

    [Fact]
    public void Locator_finds_steam_from_hklm_when_hkcu_is_missing()
    {
        var registry = new FakeRegistry().Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", Path.Combine(_root, "Program Files (x86)", "Steam"), RegistryViewKind.Registry32);
        var environment = new FakeEnvironment();
        environment.Folders[Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "nicht-da");

        var sources = LauncherLogLocator.Find(PhysicalFileSystem.Instance, registry, environment);

        Assert.Equal(new LauncherLogSource(LauncherKind.Steam, Path.GetDirectoryName(_steamLog)!, "console_log.txt"), Assert.Single(sources));
    }

    [Fact]
    public void Locator_skips_launchers_without_log_folder()
    {
        var fs = new InMemoryFileSystem().AddDirectory(@"C:\Users\Test\AppData\Local\EpicGamesLauncher\Saved\Logs");
        var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/program files (x86)/steam");

        var sources = LauncherLogLocator.Find(fs, registry, new FakeEnvironment());

        Assert.Equal([new LauncherLogSource(LauncherKind.Epic, @"C:\Users\Test\AppData\Local\EpicGamesLauncher\Saved\Logs", "EpicGamesLauncher.log")], sources);
        fs.AddDirectory(@"C:\Program Files (x86)\Steam\logs");
        Assert.Equal(@"C:\Program Files (x86)\Steam\logs\console_log.txt", LauncherLogLocator.Find(fs, registry, new FakeEnvironment())[1].FullPath, ignoreCase: true);
        Assert.Empty(LauncherLogLocator.Find(new InMemoryFileSystem(), new FakeRegistry(), new FakeEnvironment()));
    }
}
