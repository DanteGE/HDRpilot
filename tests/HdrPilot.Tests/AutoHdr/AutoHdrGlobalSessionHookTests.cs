using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Wie die Sitzungslogik Auto HDR informiert (E88): Ankündigung vor dem Schalten von HDR und vor dem Start-Befehl
/// (Start über die App, Steam-Startoption, Frühwarnung), Wiederanlauf genau einmal, jeder Zyklus mit allen zugeordneten
/// laufenden Spielen und den Starts, deren HDR-Sitzung noch wartet. Auto HDR als aufzeichnende Attrappe.
/// </summary>
public sealed class AutoHdrGlobalSessionHookTests
{
    private const string DirA = @"D:\Steam-Bibliothek\steamapps\common\Spiel A";
    private const string ExeA = DirA + @"\SpielA.exe";
    private const string DirB = @"D:\Games\Spiel B";
    private const string ExeB = DirB + @"\SpielB.exe";

    private readonly FakeClock _clock = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeSessionConfig _config = new();
    private readonly FakeLauncher _launcher = new();
    private readonly ProcessScript _processes = new();
    private readonly ListLogger<SessionManager> _log = new();
    private readonly List<string> _events = [];
    private readonly RecordingHooks _hooks;
    private readonly SessionManager _manager;

    public AutoHdrGlobalSessionHookTests()
    {
        _hooks = new RecordingHooks(_events);
        _hdr.BeforeSet = (_, on) => _events.Add(on ? "hdr an" : "hdr aus");
        _launcher.OnLaunch = g => _events.Add("start " + g.Key);
        _manager = new SessionManager(
            _hdr, _config, new InMemorySessionStateStore(), _launcher, _clock, (_, _) => false, [@"C:\Windows"], _log,
            new SessionManagerOptions
            {
                RestartBuffer = TimeSpan.Zero,
                FullscreenAppRunning = () => false,
                NotificationsSuppressed = () => false,
                ActivityProbe = new FakeActivityProbe(),
                AutoHdr = _hooks,
            });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Poll(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await _manager.UpdateAsync(_processes.Next(), Ct);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private GameConfig AutoHdrGame(string id, string dir, bool hdrAuto = false)
    {
        var game = _config.AddGame(id, dir, hdrAuto);
        game.AutoHdr = true;
        _config.Touch();
        return game;
    }

    [Fact]
    public async Task Launch_via_app_announces_before_hdr_is_switched_and_before_the_start_command()
    {
        AutoHdrGame("1", DirA);
        await Poll();
        _events.Clear();

        var outcome = await _manager.LaunchAsync("Steam:1", Ct);

        Assert.True(outcome.Started);
        Assert.Equal(["announce Steam:1 LaunchedViaApp", "hdr an", "start Steam:1"], _events);
    }

    [Theory]
    [InlineData(SessionTrigger.SteamLaunchOption)]
    [InlineData(SessionTrigger.LauncherSignal)]
    public async Task Prepare_announces_before_hdr_is_switched(SessionTrigger trigger)
    {
        AutoHdrGame("1", DirA, hdrAuto: true);
        await Poll();
        _events.Clear();

        var outcome = await _manager.PrepareAsync("Steam:1", trigger, Ct);

        Assert.True(outcome.Prepared);
        Assert.Equal(["announce Steam:1 " + trigger, "hdr an"], _events);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task Unknown_game_or_missing_launch_command_announces_nothing()
    {
        AutoHdrGame("1", DirA).LaunchCommand = null;
        await Poll();
        _events.Clear();

        Assert.False((await _manager.LaunchAsync("Steam:1", Ct)).Started);
        Assert.False((await _manager.LaunchAsync("Steam:999", Ct)).Started);
        Assert.False((await _manager.PrepareAsync("Steam:999", SessionTrigger.SteamLaunchOption, Ct)).Prepared);

        Assert.Empty(_events);
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public async Task Recovery_is_reported_exactly_once_with_the_running_games()
    {
        AutoHdrGame("1", DirA);
        _processes.StartExe(ExeA);

        await _manager.InitializeAsync(_processes.Next(), Ct);
        await Poll(3);

        var recovery = Assert.Single(_hooks.Recoveries);
        Assert.Equal(["Steam:1"], recovery);
        Assert.Equal(3, _hooks.Cycles.Count);
    }

    [Fact]
    public async Task First_cycle_without_initialize_reports_recovery_once_too()
    {
        await Poll(4);

        Assert.Single(_hooks.Recoveries);
        Assert.Equal(4, _hooks.Cycles.Count);
    }

    [Fact]
    public async Task Cycles_include_running_games_without_hdr_automation()
    {
        AutoHdrGame("1", DirA);
        var notInstalled = AutoHdrGame("2", DirB);
        notInstalled.IsInstalled = false; // mit „Windows Auto HDR“ trotzdem zugeordnet
        var plain = _config.AddGame("3", @"E:\Games\Spiel C", hdrAuto: false);
        plain.IsInstalled = false; // ohne beide Schalter und nicht installiert: nicht zugeordnet
        await Poll();
        _processes.StartExe(ExeA);
        _processes.StartExe(ExeB);
        _processes.StartExe(@"E:\Games\Spiel C\SpielC.exe");

        await Poll();

        var (keys, waiting) = _hooks.Cycles[^1];
        Assert.Equal(["Steam:1", "Steam:2"], keys.Order(StringComparer.Ordinal));
        Assert.Empty(waiting);
        Assert.Empty(_manager.Sessions); // keine HDR-Sitzung ohne „HDR automatisch“
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public async Task Cycles_name_the_starts_whose_hdr_session_still_waits_for_the_game()
    {
        AutoHdrGame("1", DirA);
        await Poll();
        await _manager.LaunchAsync("Steam:1", Ct);

        await Poll();
        Assert.Equal(["Steam:1"], _hooks.Cycles[^1].Waiting);
        Assert.Empty(_hooks.Cycles[^1].Keys);

        _processes.StartExe(ExeA); // Spiel erkannt: wartet nicht mehr, läuft
        await Poll();
        Assert.Empty(_hooks.Cycles[^1].Waiting);
        Assert.Equal(["Steam:1"], _hooks.Cycles[^1].Keys);
    }

    [Fact]
    public async Task Announce_for_auto_hdr_only_switches_no_hdr_and_needs_initialization()
    {
        AutoHdrGame("1", DirA);

        await _manager.AnnounceAutoHdrStartAsync("Steam:1", SessionTrigger.LauncherSignal, Ct);
        Assert.Empty(_events); // vor dem ersten Zyklus: nichts

        await Poll();
        _events.Clear();
        await _manager.AnnounceAutoHdrStartAsync("Steam:1", SessionTrigger.LauncherSignal, Ct);
        await _manager.AnnounceAutoHdrStartAsync("Steam:999", SessionTrigger.LauncherSignal, Ct);

        Assert.Equal(["announce Steam:1 LauncherSignal"], _events);
        Assert.Empty(_hdr.Calls);
        Assert.Empty(_manager.Sessions);
    }

    [Fact]
    public async Task Throwing_hooks_never_break_the_hdr_start()
    {
        AutoHdrGame("1", DirA);
        _hooks.Throw = true;
        await Poll();

        var outcome = await _manager.LaunchAsync("Steam:1", Ct);

        Assert.True(outcome.Started);
        Assert.True(_hdr[FakeHdrController.Primary].HdrOn);
        Assert.Single(_launcher.Launched);
        Assert.True(_log.Contains(LogLevel.Warning, "Auto HDR: Aufruf aus der Sitzungslogik fehlgeschlagen"));
    }

    private sealed class RecordingHooks(List<string> events) : IAutoHdrSessionHooks
    {
        public List<List<string>> Recoveries { get; } = [];

        public List<(List<string> Keys, List<string> Waiting)> Cycles { get; } = [];

        public bool Throw { get; set; }

        public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now)
        {
            ThrowIfWanted();
            events.Add("announce " + game.Key + " " + trigger);
        }

        public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
        {
            ThrowIfWanted();
            Recoveries.Add([.. runningGameKeys]);
        }

        public void OnCycle(IReadOnlyCollection<string> runningGameKeys, IReadOnlyCollection<string> waitingGameKeys, AppConfig config, DateTimeOffset now)
        {
            ThrowIfWanted();
            Cycles.Add(([.. runningGameKeys], [.. waitingGameKeys]));
        }

        private void ThrowIfWanted()
        {
            if (Throw)
            {
                throw new InvalidOperationException("Auto HDR kaputt");
            }
        }
    }
}
