using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Sessions;

/// <summary>
/// Neustart-Puffer nach kurzen Sitzungen und Warten auf eine Vollbild-Anwendung. Anlass (G4.5): Dead Island 2 hing
/// gleich nach dem automatischen Umschalten; der Nutzer beendete es und startete 96 s nach dem Zurückstellen neu.
/// </summary>
public sealed partial class SessionManagerTests
{
    private readonly FakeActivityProbe _activity = new();
    private bool _fullscreen;
    private int _fullscreenQueries;
    private SessionManagerOptions? _options;

    /// <summary>
    /// Optionen der Attrappe: Vollbild-Abfrage und Prozessaktivität aus dem Test. Die älteren Tests prüfen die eingestellte
    /// Nachlaufzeit und laufen ohne Neustart-Puffer; <see cref="UseRestartBuffer"/> schaltet ihn mit dem Wert für den
    /// Betrieb ein (vor dem ersten Zugriff auf <see cref="Manager"/>).
    /// </summary>
    private SessionManagerOptions Options => _options ??= FakeOptions(TimeSpan.Zero);

    private SessionManagerOptions FakeOptions(TimeSpan restartBuffer) => new()
    {
        RestartBuffer = restartBuffer,
        FullscreenAppRunning = () =>
        {
            _fullscreenQueries++;
            return _fullscreen;
        },
        ActivityProbe = _activity,
    };

    private void UseRestartBuffer()
    {
        Assert.Null(_manager);
        _options = FakeOptions(SessionManagerOptions.Default.RestartBuffer);
    }

    /// <summary>
    /// Spiel A läuft <paramref name="seconds"/> s (vom ersten Zyklus mit Prozess an), dann endet sein Prozess und ein
    /// Zyklus ohne Prozess läuft. Gibt den Zeitpunkt dieses Zyklus zurück (Beginn von Puffer oder Nachlaufzeit).
    /// </summary>
    private async Task<DateTimeOffset> RunGameFor(int seconds)
    {
        var pid = _processes.StartExe(ExeA);
        await Poll(seconds);
        _processes.Stop(pid);
        var stoppedAt = _clock.UtcNow;
        await Poll();
        return stoppedAt;
    }

    private int FullscreenWaitLogs() => _log.Entries.Count(e => e.Message.Contains("Zurückstellen wartet", StringComparison.Ordinal));

    [Fact]
    public void Operating_thresholds_are_the_agreed_ones()
    {
        var o = SessionManagerOptions.Default;

        Assert.Equal(TimeSpan.FromMinutes(5), o.ShortSessionLimit);
        Assert.Equal(TimeSpan.FromMinutes(3), o.RestartBuffer);
        Assert.Equal(TimeSpan.FromMinutes(30), o.MaxFullscreenHold);
        Assert.Equal(TimeSpan.FromSeconds(30), o.HangCheckInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), o.HangThreshold);
        Assert.Null(o.FullscreenAppRunning); // Betrieb: SHQueryUserNotificationState
        Assert.Null(o.ActivityProbe); // Betrieb: NtProcessActivityProbe
    }

    [Fact]
    public async Task Short_session_keeps_hdr_on_for_the_restart_buffer_then_restores()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(60);

        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionPhase.Ending, s.Phase);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(180), s.BufferUntil);
        Assert.False(s.HeldForFullscreen);
        Assert.True(_log.Contains(LogLevel.Information, "kurze Sitzung (60 s): Neustart-Puffer 180 s"));

        await Poll(179); // die Nachlaufzeit (10 s) ist längst vorbei, der Puffer läuft noch
        Assert.True(PrimaryOn);
        Assert.Equal(0, _hdr.SetCalls(enable: false));

        await Poll();
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Null(_store.Data);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.HdrRestored);
        Assert.True(_log.Contains(LogLevel.Information, "Auslöser: Spielende \"Spiel 1\" (Spielende nach Neustart-Puffer)"));
        Assert.Equal(1, _fullscreenQueries); // einmal beim Ablauf des Puffers
    }

    [Fact]
    public async Task Restart_buffer_from_the_configuration_overrides_the_default()
    {
        UseRestartBuffer();
        _config.Config.Settings.RestartBufferSeconds = 60;
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(30);

        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(60), Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(60);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Restart_buffer_zero_in_the_configuration_switches_it_off()
    {
        UseRestartBuffer();
        _config.Config.Settings.RestartBufferSeconds = 0;
        _config.AddGame("1", DirA);
        await Poll();

        await RunGameFor(30);

        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(10);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Restart_within_the_buffer_continues_the_session_and_keeps_hdr_on()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(20); // hängt kurz nach dem Umschalten, Nutzer beendet es
        await Poll(95);

        var pid = _processes.StartExe(ExeA); // 96 s nach dem Ende wieder gestartet
        await Poll();

        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionPhase.Running, s.Phase);
        Assert.Null(s.BufferUntil);
        Assert.True(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
        Assert.Equal(0, _hdr.SetCalls(enable: false));
        Assert.True(_log.Contains(LogLevel.Information, "läuft wieder, Neustart-Puffer abgebrochen"));

        // Jetzt läuft es lange (gezählt ab dem ersten Start): danach gilt wieder die eingestellte Nachlaufzeit.
        await Poll(10 * 60);
        _processes.Stop(pid);
        await Poll(10);
        Assert.True(PrimaryOn);
        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll();
        Assert.False(PrimaryOn);
        Assert.Equal(0, _fullscreenQueries);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public async Task Only_sessions_shorter_than_five_minutes_get_the_buffer(int seconds, bool buffered)
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(seconds);

        await Poll(10); // eingestellte Nachlaufzeit abgelaufen

        Assert.Equal(buffered, PrimaryOn);
        Assert.Equal(buffered ? 1 : 0, Manager.Sessions.Count);
    }

    [Fact]
    public async Task No_buffer_when_hdr_was_already_on_before_the_game()
    {
        UseRestartBuffer();
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(30);

        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(10);

        Assert.Empty(Manager.Sessions);
        Assert.True(PrimaryOn);
        Assert.Empty(_hdr.Calls);
        Assert.False(_log.Contains(LogLevel.Information, "Neustart-Puffer"));
    }

    [Fact]
    public async Task No_buffer_when_hdr_stays_on_after_the_game_anyway()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA, restore: false);
        await Poll();
        await RunGameFor(30);

        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(10);

        Assert.Empty(Manager.Sessions);
        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Longer_grace_period_wins_over_the_buffer()
    {
        UseRestartBuffer();
        _config.Config.Settings.GracePeriodSeconds = 300;
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(30);

        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(300), Assert.Single(Manager.Sessions).BufferUntil);
        Assert.True(_log.Contains(LogLevel.Information, "Neustart-Puffer 300 s"));
        await Poll(299);
        Assert.True(PrimaryOn);
        await Poll();
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Fullscreen_app_after_the_buffer_postpones_the_restore_until_it_closes()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(30);
        await Poll(179);
        Assert.Equal(0, _fullscreenQueries); // während des Puffers wird nicht gefragt

        _fullscreen = true; // ein anderes Spiel startet gerade im Vollbild
        await Poll();
        Assert.True(PrimaryOn);
        Assert.True(Assert.Single(Manager.Sessions).HeldForFullscreen);
        Assert.Equal(1, FullscreenWaitLogs());

        await Poll(600);
        Assert.True(PrimaryOn);
        Assert.Equal(601, _fullscreenQueries); // bei jedem Zyklus geprüft
        Assert.Equal(1, FullscreenWaitLogs()); // nur einmal protokolliert

        _fullscreen = false;
        await Poll();
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.True(_log.Contains(LogLevel.Information, "Vollbild-Anwendung nach 601 s beendet"));
    }

    [Fact]
    public async Task Waiting_for_a_fullscreen_app_is_capped_at_30_minutes()
    {
        UseRestartBuffer();
        _fullscreen = true;
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(30);
        await Poll(180); // Puffer abgelaufen, Warten beginnt
        Assert.True(Assert.Single(Manager.Sessions).HeldForFullscreen);

        await Poll((30 * 60) - 1);
        Assert.True(PrimaryOn);

        await Poll();
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.True(_log.Contains(LogLevel.Information, "Vollbild-Anwendung läuft seit 30 min"));
    }

    [Fact]
    public async Task Long_session_restores_after_the_grace_period_even_with_a_fullscreen_app()
    {
        UseRestartBuffer();
        _fullscreen = true;
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(6 * 60);

        await Poll(10);

        Assert.False(PrimaryOn);
        Assert.Equal(0, _fullscreenQueries);
    }

    [Fact]
    public async Task Buffer_end_and_waiting_are_published_without_an_event_per_second()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        var pid = _processes.StartExe(ExeA);
        await Poll(30);
        var changes = 0;
        Manager.StateChanged += (_, _) => changes++;

        _processes.Stop(pid);
        var stoppedAt = _clock.UtcNow;
        await Poll();
        Assert.Equal(1, changes);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(180), Assert.Single(Manager.Sessions).BufferUntil);

        _fullscreen = true;
        await Poll(179);
        Assert.Equal(1, changes); // den Countdown führt das Tray selbst nach

        await Poll();
        Assert.Equal(2, changes);
        var held = Assert.Single(Manager.Sessions);
        Assert.True(held.HeldForFullscreen);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(180), held.BufferUntil);
    }

    [Fact]
    public async Task Launch_via_the_app_during_the_buffer_waits_for_the_game_again()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA, hdrAuto: false);
        await Launch("Steam:1");
        var pid = _processes.StartExe(ExeA);
        await Poll(20);
        _processes.Stop(pid);
        await Poll(5);
        Assert.NotNull(Assert.Single(Manager.Sessions).BufferUntil);

        await Manager.LaunchAsync("Steam:1", Ct);

        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionPhase.Launching, s.Phase);
        Assert.Null(s.BufferUntil);
        Assert.False(s.HeldForFullscreen);
        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Failing_fullscreen_query_does_not_keep_hdr_on()
    {
        _options = new SessionManagerOptions
        {
            ActivityProbe = _activity,
            FullscreenAppRunning = () => throw new InvalidOperationException("Shell nicht erreichbar"),
        };
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(30);

        await Poll(180);

        Assert.False(PrimaryOn);
        Assert.True(_log.Contains(LogLevel.Warning, "Vollbild-Anwendung läuft“ fehlgeschlagen"));
    }
}
