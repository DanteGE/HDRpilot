using HdrPilot.Core.Launchers;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Ende zu Ende über die echte Sitzungslogik und den echten Dienst (E88): Jeder Start, den HDRpilot vorher kennt, setzt den
/// globalen Auto-HDR-Schalter vor dem HDR-Schalten und vor dem Spielprozess (Starten/„Mit HDR starten“ und Verknüpfung über
/// LaunchAsync, Steam-Startoption über PrepareAsync, Frühwarnung von Steam/Epic); nach der Sitzung ist er zurück. Ein Start
/// ohne HDRpilot setzt nichts. HDR, Launcher, Prozesse und Registry sind Attrappen.
/// </summary>
public sealed class AutoHdrGlobalStartPathTests
{
    private readonly AutoHdrHarness _h = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeLauncher _launcher = new();
    private readonly ProcessScript _processes = new();
    private readonly List<string> _events = [];
    private readonly SessionManager _sessions;
    private readonly string _a;

    public AutoHdrGlobalStartPathTests()
    {
        _h.Store.SetString(Global, GlobalOff);
        _a = _h.AddGame("1", ExeA);
        _hdr.BeforeSet = (_, on) => _events.Add((on ? "hdr an" : "hdr aus") + " bei " + _h.Value(Global));
        _launcher.OnLaunch = g => _events.Add("start " + g.Key + " bei " + _h.Value(Global));
        _h.Service.Start();
        _sessions = _h.CreateSessions(_hdr, _launcher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Poll(int seconds = 1)
    {
        for (var i = 0; i < seconds; i++)
        {
            await _sessions.UpdateAsync(_processes.Next(), Ct);
            _h.Clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task Start_via_hdrpilot_sets_the_switch_before_hdr_and_before_the_start_command()
    {
        await Poll();

        var outcome = await _sessions.LaunchAsync(_a, Ct); // „Mit HDR starten“ und Verknüpfung (--run)

        Assert.True(outcome.Started);
        Assert.Equal(["hdr an bei " + GlobalOn, "start " + _a + " bei " + GlobalOn], _events);
    }

    [Fact]
    public async Task Steam_launch_option_sets_the_switch_before_the_game_starts()
    {
        await Poll();

        var outcome = await _sessions.PrepareAsync(_a, SessionTrigger.SteamLaunchOption, Ct); // Wrapper: danach startet Steam das Spiel

        Assert.True(outcome.Prepared);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(["hdr an bei " + GlobalOn], _events);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task Launcher_early_warning_sets_the_switch_also_without_hdr_automation()
    {
        await Poll();
        var handler = new LauncherSignalHandler(_h.Config, new SessionManagerSignalTarget(_sessions));

        var result = await handler.HandleAsync(new LauncherSignal(LauncherKind.Steam, _a, 5), Ct);

        Assert.Equal(LauncherSignalResult.AutoHdrOnly, result);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Empty(_hdr.Calls); // „HDR automatisch“ ist aus: HDR bleibt, wie es ist
    }

    [Fact]
    public async Task Game_started_without_hdrpilot_gets_no_auto_hdr()
    {
        await Poll();
        _processes.StartExe(ExeA);

        await Poll(30);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public async Task Whole_session_restores_the_switch_and_hdr_after_the_game()
    {
        _h.Config.Update(c => c.Settings.RestartBufferSeconds = 0);
        await Poll();
        await _sessions.LaunchAsync(_a, Ct);
        await Poll(3); // Startfrist läuft, das Spiel lädt
        var pid = _processes.StartExe(ExeA);
        await Poll(60);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.True(_hdr[FakeHdrController.Primary].HdrOn);

        _processes.Stop(pid);
        await Poll(9);
        Assert.Equal(GlobalOn, _h.Value(Global)); // Nachlaufzeit 10 s
        await Poll(3);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.False(_hdr[FakeHdrController.Primary].HdrOn);
        Assert.Equal(["set " + Global + "=" + GlobalOn, "set " + Global + "=" + GlobalOff], _h.GlobalOperations);
    }

    [Fact]
    public async Task Start_that_never_finds_its_game_restores_with_the_launch_timeout()
    {
        await Poll();
        await _sessions.LaunchAsync(_a, Ct);

        await Poll(110);
        Assert.Equal(GlobalOn, _h.Value(Global));
        await Poll(15); // Startfrist 120 s: Sitzung endet, der Schalter mit ihr

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Empty(_sessions.Sessions);
    }

    [Fact]
    public async Task Game_without_the_option_started_via_hdrpilot_leaves_the_switch_alone()
    {
        var plain = _h.AddGame("2", ExeB, autoHdr: false);
        await Poll();

        await _sessions.LaunchAsync(plain, Ct);
        await Poll(5);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(0, _h.Store.ReadsOf(Global));
        Assert.Single(_launcher.Launched); // Gegenprobe: gestartet wurde trotzdem, mit HDR
        Assert.True(_hdr[FakeHdrController.Primary].HdrOn);
    }
}
