using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Launchers;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Frühwarnung und Auto HDR (E88): Ein Spiel mit „Windows Auto HDR“, aber ohne „HDR automatisch“ wird nur angekündigt (die
/// Notlösung kann vor dem Spielprozess setzen), HDR bleibt unverändert; Spiele mit „HDR automatisch“ laufen wie bisher über
/// PrepareAsync. Abgeschaltete Frühwarnung, unbekannte Spiele und pausierte Automatik kündigen nichts an.
/// </summary>
public sealed class AutoHdrGlobalLauncherSignalTests
{
    private const string Dir = @"D:\Steam-Bibliothek\steamapps\common\Spiel";
    private static readonly LauncherSignal Signal = new(LauncherKind.Steam, "Steam:1", 5);

    private readonly FakeClock _clock = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeSessionConfig _config = new();
    private readonly ProcessScript _processes = new();
    private readonly ListLogger<LauncherSignalHandler> _log = new();
    private readonly List<string> _announced = [];
    private readonly SessionManager _manager;
    private readonly LauncherSignalHandler _handler;

    public AutoHdrGlobalLauncherSignalTests()
    {
        _config.Config.Settings.RestartBufferSeconds = 0;
        _manager = new SessionManager(
            _hdr, _config, new InMemorySessionStateStore(), new FakeLauncher(), _clock, (_, _) => false, [@"C:\Windows"], null,
            new SessionManagerOptions
            {
                FullscreenAppRunning = () => false,
                NotificationsSuppressed = () => false,
                ActivityProbe = new FakeActivityProbe(),
                AutoHdr = new AnnounceRecorder(_announced),
            });
        _handler = new LauncherSignalHandler(_config, new SessionManagerSignalTarget(_manager), _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GameConfig Game(bool hdrAuto, bool autoHdr)
    {
        var game = _config.AddGame("1", Dir, hdrAuto);
        game.AutoHdr = autoHdr;
        _config.Touch();
        return game;
    }

    private Task Poll() => _manager.UpdateAsync(_processes.Next(), Ct);

    [Fact]
    public async Task Auto_hdr_only_game_is_announced_without_switching_hdr()
    {
        Game(hdrAuto: false, autoHdr: true);
        await Poll();

        var result = await _handler.HandleAsync(Signal, Ct);

        Assert.Equal(LauncherSignalResult.AutoHdrOnly, result);
        Assert.Equal(["Steam:1 LauncherSignal"], _announced);
        Assert.Empty(_hdr.Calls);
        Assert.Empty(_manager.Sessions);
        Assert.True(_log.Contains(Microsoft.Extensions.Logging.LogLevel.Information, "nur Windows Auto HDR wird vorbereitet"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Automatic_game_still_takes_prepare_and_is_announced_there(bool autoHdr)
    {
        Game(hdrAuto: true, autoHdr);
        await Poll();

        var result = await _handler.HandleAsync(Signal, Ct);

        Assert.Equal(LauncherSignalResult.Prepared, result);
        Assert.Equal(["Steam:1 LauncherSignal"], _announced); // über BeginStartAsync, der Dienst prüft die Option selbst
        Assert.True(_hdr[FakeHdrController.Primary].HdrOn);
    }

    [Fact]
    public async Task Paused_disabled_unknown_or_not_ready_announce_nothing()
    {
        Game(hdrAuto: false, autoHdr: true);

        Assert.Equal(LauncherSignalResult.NotAutomatic, await _handler.HandleAsync(Signal, Ct)); // vor dem ersten Zyklus

        await Poll();
        _manager.Paused = true;
        Assert.Equal(LauncherSignalResult.NotAutomatic, await _handler.HandleAsync(Signal, Ct));
        _manager.Paused = false;

        Assert.Equal(LauncherSignalResult.UnknownGame, await _handler.HandleAsync(new LauncherSignal(LauncherKind.Epic, "Epic:Fremd"), Ct));

        _config.Config.Settings.LauncherSignals = false;
        Assert.Equal(LauncherSignalResult.Disabled, await _handler.HandleAsync(Signal, Ct));

        Assert.Empty(_announced);
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public async Task Game_without_either_option_stays_not_automatic()
    {
        Game(hdrAuto: false, autoHdr: false);
        await Poll();

        Assert.Equal(LauncherSignalResult.NotAutomatic, await _handler.HandleAsync(Signal, Ct));
        Assert.Empty(_announced);
    }

    private sealed class AnnounceRecorder(List<string> announced) : IAutoHdrSessionHooks
    {
        public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now) => announced.Add(game.Key + " " + trigger);

        public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
        {
        }

        public void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now)
        {
        }
    }
}
