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
    private bool _userAway;
    private int _suppressedQueries;
    private SessionManagerOptions? _options;

    /// <summary>
    /// Optionen der Attrappe: Vollbild-Abfrage, Hinweis-Zustand der Shell (Vollbild oder Nutzer nicht da) und
    /// Prozessaktivität aus dem Test. Die älteren Tests prüfen die eingestellte Nachlaufzeit und laufen ohne
    /// Neustart-Puffer; <see cref="UseRestartBuffer"/> schaltet ihn mit dem Wert für den Betrieb ein (vor dem ersten
    /// Zugriff auf <see cref="Manager"/>).
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
        NotificationsSuppressed = () =>
        {
            _suppressedQueries++;
            return _fullscreen || _userAway;
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
        Assert.Equal(TimeSpan.FromMinutes(1), o.RestartBuffer); // E85, vorher 3 Minuten
        Assert.Equal(TimeSpan.FromMinutes(30), o.MaxFullscreenHold);
        Assert.Equal(TimeSpan.FromSeconds(30), o.HangCheckInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), o.HangThreshold);
        Assert.Null(o.FullscreenAppRunning); // Betrieb: SHQueryUserNotificationState
        Assert.Null(o.NotificationsSuppressed); // Betrieb: SHQueryUserNotificationState (auch gesperrt, Bildschirmschoner)
        Assert.Null(o.ActivityProbe); // Betrieb: NtProcessActivityProbe
    }

    [Fact]
    public async Task RestartBuffer_default_of_one_minute_keeps_hdr_on_after_a_short_session_then_restores()
    {
        // restartBufferSeconds nicht gesetzt: Standard 1:00 (E85; nach dem Helldivers-2-Test war 3:00 zu lang).
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        Assert.Null(_config.Config.Settings.RestartBufferSeconds);

        var stoppedAt = await RunGameFor(77);

        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionPhase.Ending, s.Phase);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(60), s.BufferUntil);
        Assert.False(s.HeldForFullscreen);
        Assert.True(_log.Contains(LogLevel.Information, "kurze Sitzung (77 s): Neustart-Puffer 60 s"));

        await Poll(59); // die Nachlaufzeit (10 s) ist längst vorbei, der Puffer läuft noch
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
    public async Task RestartBuffer_from_the_configuration_overrides_the_default()
    {
        UseRestartBuffer();
        _config.Config.Settings.RestartBufferSeconds = 125; // 2:05 in den Einstellungen
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(30);

        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(125), Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(124);
        Assert.True(PrimaryOn);
        await Poll();
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task RestartBuffer_zero_in_the_configuration_switches_it_off()
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
    public async Task RestartBuffer_changed_in_the_settings_applies_to_the_next_ending_session_without_restart()
    {
        // Die Einstellungen schreiben über den ConfigService; die Sitzungslogik liest den Puffer bei jedem Spielende neu.
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();

        var first = await RunGameFor(30);
        Assert.Equal(first + TimeSpan.FromSeconds(60), Assert.Single(Manager.Sessions).BufferUntil); // Standard
        await Poll(60);
        Assert.Empty(Manager.Sessions);

        _config.Config.Settings.RestartBufferSeconds = 45; // gespeichert als 0:45
        var second = await RunGameFor(30);
        Assert.Equal(second + TimeSpan.FromSeconds(45), Assert.Single(Manager.Sessions).BufferUntil);
        Assert.True(_log.Contains(LogLevel.Information, "kurze Sitzung (30 s): Neustart-Puffer 45 s"));
        await Poll(45);
        Assert.Empty(Manager.Sessions);
        Assert.False(PrimaryOn);

        _config.Config.Settings.RestartBufferSeconds = 0; // gespeichert als 0:00 = aus
        await RunGameFor(30);
        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(10);
        Assert.Empty(Manager.Sessions);
        Assert.Equal(3, _hdr.SetCalls(enable: true));
        Assert.Equal(3, _hdr.SetCalls(enable: false));
    }

    [Fact]
    public async Task RestartBuffer_change_during_a_running_buffer_keeps_its_end()
    {
        // Ein schon laufender Puffer behält sein Ende; die neue Einstellung gilt ab dem nächsten Spielende.
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        var stoppedAt = await RunGameFor(30);
        await Poll(10);

        _config.Config.Settings.RestartBufferSeconds = 0;
        await Poll(10);

        Assert.True(PrimaryOn);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(60), Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(39);
        Assert.True(PrimaryOn);
        await Poll();
        Assert.False(PrimaryOn);
    }

    [Theory]
    [InlineData(1800, 1800)]
    [InlineData(1801, 1800)]
    [InlineData(5000, 1800)]
    [InlineData(-5, 0)]
    public async Task RestartBuffer_outside_0_to_30_minutes_in_the_configuration_is_limited(int configured, int expected)
    {
        // config.json von Hand bearbeitet: höchstens 30:00, negative Werte gelten als aus.
        UseRestartBuffer();
        _config.Config.Settings.RestartBufferSeconds = configured;
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(30);

        var expectedEnd = expected == 0 ? (DateTimeOffset?)null : stoppedAt + TimeSpan.FromSeconds(expected);
        Assert.Equal(expectedEnd, Assert.Single(Manager.Sessions).BufferUntil);
    }

    [Fact]
    public async Task Restart_within_the_buffer_continues_the_session_and_keeps_hdr_on()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(20); // hängt kurz nach dem Umschalten, Nutzer beendet es
        await Poll(44);

        var pid = _processes.StartExe(ExeA); // 45 s nach dem Ende wieder gestartet (Dead Island 2 brauchte 96 s, E85)
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

    [Fact]
    public async Task Restart_within_the_buffer_switches_hdr_on_again_after_a_manual_switch_off()
    {
        // Im Puffer schaltet der Nutzer HDR von Hand aus („HDR jetzt ausschalten“ ändert keine Sitzung) und startet das
        // Spiel dann neu: wie jeder andere Start schaltet das HDR wieder ein (E41), sonst liefe der ganze Neustart in SDR.
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(60);
        await Poll(30);
        _hdr[FakeHdrController.Primary].HdrOn = false;

        _processes.StartExe(ExeA);
        await Poll();

        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.True(PrimaryOn);
        Assert.Equal(2, _hdr.SetCalls(enable: true));
        Assert.Equal([false, false], _hdr.HotkeyAllowed); // das Spiel läuft: nie die Notlösung
    }

    [Fact]
    public async Task Restart_within_the_buffer_while_paused_leaves_a_manual_switch_off_alone()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(60);
        Manager.Paused = true;
        _hdr[FakeHdrController.Primary].HdrOn = false;

        _processes.StartExe(ExeA);
        await Poll();

        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Restart_within_the_buffer_of_a_game_without_hdr_auto_leaves_a_manual_switch_off_alone()
    {
        // Über „Mit HDR starten“ gestartet, ohne „HDR automatisch“: ein erkannter Neustart schaltet bei diesem Spiel nie
        // etwas, auch im Puffer nicht.
        UseRestartBuffer();
        _config.AddGame("1", DirA, hdrAuto: false);
        await Launch("Steam:1");
        var pid = _processes.StartExe(ExeA);
        await Poll(60);
        _processes.Stop(pid);
        await Poll(30);
        Assert.NotNull(Assert.Single(Manager.Sessions).BufferUntil);
        _hdr[FakeHdrController.Primary].HdrOn = false;

        _processes.StartExe(ExeA);
        await Poll();

        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Short_process_gap_in_a_short_session_leaves_a_manual_switch_off_alone()
    {
        // Im Spiel von Hand auf SDR gestellt, dann startet sich das Spiel für eine Einstellung selbst neu (Prozess 2 s
        // weg): das ist kein neuer Start, HDR bleibt aus. Erst eine Lücke länger als die Nachlaufzeit zählt als Neustart.
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        var pid = _processes.StartExe(ExeA);
        await Poll(120);
        _hdr[FakeHdrController.Primary].HdrOn = false;

        _processes.Stop(pid);
        await Poll(2);
        Assert.NotNull(Assert.Single(Manager.Sessions).BufferUntil);
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(_log.Contains(LogLevel.Information, "läuft wieder, Neustart-Puffer abgebrochen"));
        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Process_back_within_the_grace_period_leaves_a_manual_switch_off_alone()
    {
        // Nur der Neustart im Puffer gilt als neuer Start. Kommt nach einer langen Sitzung innerhalb der Nachlaufzeit
        // wieder ein Prozess (etwa Starter-Kette), bleibt eine Umschaltung von Hand wie bisher unangetastet.
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(6 * 60);
        _hdr[FakeHdrController.Primary].HdrOn = false;

        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(_log.Contains(LogLevel.Information, "läuft wieder, Nachlaufzeit abgebrochen"));
        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Adopted_session_never_counts_as_short()
    {
        // HDRpilot wird während eines langen Spiels neu gestartet (etwa neue Version eingespielt). Wie lange das Spiel
        // schon läuft, ist unbekannt: endet es kurz danach, gilt die Nachlaufzeit, nicht der Neustart-Puffer.
        UseRestartBuffer();
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });
        var pid = _processes.StartExe(ExeA);
        await Poll(60);
        Assert.Equal(SessionTrigger.Recovery, Assert.Single(Manager.Sessions).Trigger);

        _processes.Stop(pid);
        await Poll();
        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        await Poll(10);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
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

    [Theory]
    [InlineData(5, 10)] // 0:05 bei der Standard-Nachlaufzeit
    [InlineData(10, 10)] // gleich lang
    [InlineData(null, 120)] // Standard 1:00 bei der längsten Nachlaufzeit der Einstellungen
    public async Task RestartBuffer_not_longer_than_the_grace_period_works_like_off_even_with_a_fullscreen_app(int? buffer, int grace)
    {
        // Mit dem Regler ist ein Puffer bis zur Nachlaufzeit leicht einzustellen (E85). Er wirkt wie 0:00: nur die
        // Nachlaufzeit, kein Warten auf eine Vollbild-Anwendung. Vorher galt die Nachlaufzeit dann als Puffer, und schon
        // 0:05 hielt HDR bei einer Vollbild-Anwendung bis zu 30 Minuten an.
        UseRestartBuffer();
        _fullscreen = true;
        _config.Config.Settings.RestartBufferSeconds = buffer;
        _config.Config.Settings.GracePeriodSeconds = grace;
        _config.AddGame("1", DirA);
        await Poll();

        await RunGameFor(30);

        Assert.Null(Assert.Single(Manager.Sessions).BufferUntil);
        Assert.True(_log.Contains(LogLevel.Information, $"Nachlaufzeit {grace} s beginnt"));
        await Poll(grace - 1);
        Assert.True(PrimaryOn);
        await Poll();
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Equal(0, _fullscreenQueries);
        Assert.False(_log.Contains(LogLevel.Information, "Neustart-Puffer"));
    }

    [Fact]
    public async Task RestartBuffer_one_second_longer_than_the_grace_period_applies_with_the_fullscreen_wait()
    {
        UseRestartBuffer();
        _fullscreen = true;
        _config.Config.Settings.RestartBufferSeconds = 11;
        _config.AddGame("1", DirA);
        await Poll();

        var stoppedAt = await RunGameFor(30);

        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(11), Assert.Single(Manager.Sessions).BufferUntil);
        Assert.True(_log.Contains(LogLevel.Information, "kurze Sitzung (30 s): Neustart-Puffer 11 s"));
        await Poll(11);
        Assert.True(PrimaryOn);
        Assert.True(Assert.Single(Manager.Sessions).HeldForFullscreen);
        Assert.Equal(1, FullscreenWaitLogs());
    }

    [Fact]
    public async Task Fullscreen_app_after_the_buffer_postpones_the_restore_until_it_closes()
    {
        UseRestartBuffer();
        _config.AddGame("1", DirA);
        await Poll();
        await RunGameFor(30);
        await Poll(59);
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
        await Poll(60); // Puffer abgelaufen, Warten beginnt
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
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(60), Assert.Single(Manager.Sessions).BufferUntil);

        _fullscreen = true;
        await Poll(59);
        Assert.Equal(1, changes); // den Countdown führt das Tray selbst nach

        await Poll();
        Assert.Equal(2, changes);
        var held = Assert.Single(Manager.Sessions);
        Assert.True(held.HeldForFullscreen);
        Assert.Equal(stoppedAt + TimeSpan.FromSeconds(60), held.BufferUntil);
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

        await Poll(60);

        Assert.False(PrimaryOn);
        Assert.True(_log.Contains(LogLevel.Warning, "Vollbild-Anwendung läuft“ fehlgeschlagen"));
    }
}
