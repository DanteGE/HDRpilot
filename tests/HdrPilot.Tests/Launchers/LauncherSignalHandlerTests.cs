using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Launchers;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Launchers;

/// <summary>
/// Entscheidung über ein Launcher-Signal mit der echten Sitzungslogik und Attrappen (HDR, Prozesse, Speicher, Uhr):
/// nur Spiele mit „HDR automatisch“, nicht pausiert, ohne bestehende Sitzung; dann HDR vor dem Spielprozess.
/// </summary>
public sealed class LauncherSignalHandlerTests
{
    private const string DirSteam = @"D:\Steam-Bibliothek\steamapps\common\Aschenacht";
    private const string ExeSteam = DirSteam + @"\bin\x64\Game.exe";
    private const string DirEpic = @"D:\Anwendungen\Epic Games\DeadIsland2";
    private const string ExeEpic = DirEpic + @"\DeadIsland.exe";

    private static readonly LauncherSignal SteamSignal = new(LauncherKind.Steam, "Steam:9990130", 5);
    private static readonly LauncherSignal EpicSignal = new(LauncherKind.Epic, "Epic:Crow");

    private readonly FakeClock _clock = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeSessionConfig _config = new();
    private readonly InMemorySessionStateStore _store = new();
    private readonly FakeLauncher _launcher = new();
    private readonly ListLogger<SessionManager> _sessionLog = new();
    private readonly ListLogger<LauncherSignalHandler> _log = new();
    private readonly ProcessScript _processes = new();
    private readonly SessionManager _manager;
    private readonly CountingSessions _sessions;
    private readonly LauncherSignalHandler _handler;

    public LauncherSignalHandlerTests()
    {
        _manager = new SessionManager(_hdr, _config, _store, _launcher, _clock, (_, _) => false, [@"C:\Windows"], _sessionLog);
        _sessions = new CountingSessions(new SessionManagerSignalTarget(_manager));
        _handler = new LauncherSignalHandler(_config, _sessions, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private bool PrimaryOn => _hdr[FakeHdrController.Primary].HdrOn;

    private async Task Poll(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await _manager.UpdateAsync(_processes.Next(), Ct);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private GameConfig AddSteamGame(bool hdrAuto = true) => _config.AddGame("9990130", DirSteam, hdrAuto);

    private GameConfig AddEpicGame(bool hdrAuto = true)
    {
        var game = _config.AddGame("Crow", DirEpic, hdrAuto, GameSource.Epic);
        game.LaunchCommand = "com.epicgames.launcher://apps/a%3Ab%3ACrow?action=launch&silent=true";
        return game;
    }

    [Fact]
    public async Task Signal_for_an_automatic_game_switches_hdr_on_before_the_game_process()
    {
        AddSteamGame();
        await Poll();

        var result = await _handler.HandleAsync(SteamSignal, Ct);

        Assert.Equal(LauncherSignalResult.Prepared, result);
        Assert.Equal(1, _sessions.PrepareCalls);
        Assert.True(PrimaryOn);
        Assert.Empty(_launcher.Launched); // gestartet hat der Launcher selbst
        var session = Assert.Single(_manager.Sessions);
        Assert.Equal(SessionTrigger.LauncherSignal, session.Trigger);
        Assert.Equal(SessionPhase.Launching, session.Phase);
        Assert.True(_sessionLog.Contains(LogLevel.Information, "Auslöser Launcher-Signal (Epic/Steam)"));
        Assert.True(_log.Contains(LogLevel.Information, "Start von \"Spiel 9990130\" (Steam:9990130) angekündigt"));
    }

    [Fact]
    public async Task Epic_signal_prepares_the_epic_game()
    {
        AddEpicGame();
        await Poll();

        Assert.Equal(LauncherSignalResult.Prepared, await _handler.HandleAsync(EpicSignal, Ct));
        Assert.Equal("Epic:Crow", Assert.Single(_manager.Sessions).GameKey);
        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Prepared_session_continues_with_the_game_and_restores_after_it()
    {
        AddEpicGame();
        await Poll();
        await _handler.HandleAsync(EpicSignal, Ct);
        _clock.Advance(TimeSpan.FromSeconds(1.8)); // gemessen: Spielprozess 1,8 s nach dem Signal

        var pid = _processes.StartExe(ExeEpic);
        await Poll();
        var session = Assert.Single(_manager.Sessions);
        Assert.Equal(SessionPhase.Running, session.Phase);
        Assert.Equal(SessionTrigger.LauncherSignal, session.Trigger);
        Assert.Equal(1, _hdr.SetCalls(enable: true));

        _processes.Stop(pid);
        await Poll(12);
        Assert.False(PrimaryOn);
        Assert.Empty(_manager.Sessions);
    }

    [Fact]
    public async Task False_signal_is_undone_by_the_launch_timeout()
    {
        // Start abgebrochen (etwa Rückfrage von Steam verneint): Nach der Startfrist stellt HdrPilot zurück.
        _config.Config.Settings.LaunchTimeoutSeconds = 120;
        AddSteamGame();
        await Poll();
        await _handler.HandleAsync(SteamSignal, Ct);
        Assert.True(PrimaryOn);

        await Poll(119);
        Assert.True(PrimaryOn);
        await Poll(2);

        Assert.False(PrimaryOn);
        Assert.Empty(_manager.Sessions);
    }

    [Fact]
    public async Task Game_without_hdr_auto_is_left_alone()
    {
        AddSteamGame(hdrAuto: false);
        await Poll();

        Assert.Equal(LauncherSignalResult.NotAutomatic, await _handler.HandleAsync(SteamSignal, Ct));
        AssertNothingPrepared();
    }

    [Fact]
    public async Task Paused_automation_is_left_alone()
    {
        AddSteamGame();
        await Poll();
        _manager.Paused = true;

        Assert.Equal(LauncherSignalResult.Paused, await _handler.HandleAsync(SteamSignal, Ct));
        AssertNothingPrepared();
    }

    [Fact]
    public async Task Unknown_game_is_left_alone()
    {
        AddSteamGame();
        await Poll();

        Assert.Equal(LauncherSignalResult.UnknownGame, await _handler.HandleAsync(new LauncherSignal(LauncherKind.Steam, "Steam:431960", 1), Ct));
        AssertNothingPrepared();
    }

    [Fact]
    public async Task Disabled_setting_is_left_alone()
    {
        AddSteamGame();
        _config.Config.Settings.LauncherSignals = false;
        await Poll();

        Assert.Equal(LauncherSignalResult.Disabled, await _handler.HandleAsync(SteamSignal, Ct));
        AssertNothingPrepared();
    }

    [Fact]
    public async Task Signal_before_the_first_cycle_is_left_alone()
    {
        AddSteamGame();

        Assert.Equal(LauncherSignalResult.NotReady, await _handler.HandleAsync(SteamSignal, Ct));
        AssertNothingPrepared();
    }

    [Fact]
    public async Task Launch_via_the_app_keeps_its_session()
    {
        // HdrPilot startet über steam://rungameid/…; Steam protokolliert danach denselben Start.
        AddSteamGame();
        await Poll();
        await _manager.LaunchAsync("Steam:9990130", Ct);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(LauncherSignalResult.SessionExists, await _handler.HandleAsync(SteamSignal, Ct));
        Assert.Equal(0, _sessions.PrepareCalls);
        Assert.Equal(SessionTrigger.LaunchedViaApp, Assert.Single(_manager.Sessions).Trigger);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Running_game_keeps_its_session()
    {
        AddSteamGame();
        _processes.StartExe(ExeSteam);
        await Poll();
        Assert.Equal(SessionTrigger.ProcessDetected, Assert.Single(_manager.Sessions).Trigger);

        Assert.Equal(LauncherSignalResult.SessionExists, await _handler.HandleAsync(SteamSignal, Ct));
        Assert.Equal(0, _sessions.PrepareCalls);
    }

    [Fact]
    public async Task Errors_are_logged_without_the_signal_line_and_never_thrown()
    {
        AddSteamGame();
        await Poll();
        var failing = new LauncherSignalHandler(_config, new ThrowingSessions(), _log);

        Assert.Equal(LauncherSignalResult.Failed, await failing.HandleAsync(SteamSignal, Ct));
        Assert.True(_log.Contains(LogLevel.Warning, "Launcher-Signal für Steam:9990130 ließ sich nicht verarbeiten"));
    }

    [Fact]
    public void Setting_defaults_to_on_and_round_trips()
    {
        var empty = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{\"settings\":{}}", ConfigJson.Options)!;
        Assert.True(empty.Settings.LauncherSignals);
        Assert.True(new AppSettings().LauncherSignals);

        var off = new AppConfig();
        off.Settings.LauncherSignals = false;
        var json = System.Text.Json.JsonSerializer.Serialize(off, ConfigJson.Options);
        Assert.Contains("\"launcherSignals\": false", json, StringComparison.Ordinal);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options)!.Settings.LauncherSignals);
    }

    private void AssertNothingPrepared()
    {
        Assert.Equal(0, _sessions.PrepareCalls);
        Assert.Empty(_hdr.Calls);
        Assert.Empty(_manager.Sessions);
        Assert.False(PrimaryOn);
    }

    /// <summary>Zählt die Aufrufe von PrepareAsync und reicht sie an die echte Sitzungslogik weiter.</summary>
    private sealed class CountingSessions(ILauncherSignalSessions inner) : ILauncherSignalSessions
    {
        public int PrepareCalls { get; private set; }

        public bool IsInitialized => inner.IsInitialized;

        public bool Paused => inner.Paused;

        public IReadOnlyList<SessionInfo> Sessions => inner.Sessions;

        public Task<PrepareOutcome> PrepareAsync(string gameKey, SessionTrigger trigger, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return inner.PrepareAsync(gameKey, trigger, cancellationToken);
        }
    }

    private sealed class ThrowingSessions : ILauncherSignalSessions
    {
        public bool IsInitialized => true;

        public bool Paused => false;

        public IReadOnlyList<SessionInfo> Sessions => [];

        public Task<PrepareOutcome> PrepareAsync(string gameKey, SessionTrigger trigger, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Sitzungslogik kaputt");
    }
}
