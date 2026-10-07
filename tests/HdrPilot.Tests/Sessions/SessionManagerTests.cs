using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Sessions;

/// <summary>Sitzungslogik mit Attrappen für HDR-Steuerung, Prozessliste, Speicher und steuerbarer Uhr.</summary>
public sealed class SessionManagerTests
{
    private const string DirA = @"D:\Steam-Bibliothek\steamapps\common\Spiel A";
    private const string DirB = @"C:\XboxGames\Spiel B\Content";
    private const string ExeA = DirA + @"\bin\x64\SpielA.exe";
    private const string ExeB = DirB + @"\SpielB.exe";

    private readonly FakeClock _clock = new();
    private readonly FakeHdrController _hdr = FakeHdrController.WithPrimary();
    private readonly FakeSessionConfig _config = new();
    private readonly InMemorySessionStateStore _store = new();
    private readonly FakeLauncher _launcher = new();
    private readonly ListLogger<SessionManager> _log = new();
    private readonly ProcessScript _processes = new();
    private readonly List<SessionNotification> _notifications = [];
    private SessionManager? _manager;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SessionManager Manager
    {
        get
        {
            if (_manager is null)
            {
                _manager = Create();
                _manager.Notification += (_, n) => _notifications.Add(n);
            }

            return _manager;
        }
    }

    private SessionManager Create() => new(
        _hdr, _config, _store, _launcher, _clock,
        (name, _) => name.Contains("Crash", StringComparison.OrdinalIgnoreCase) || name.StartsWith("vc_redist", StringComparison.OrdinalIgnoreCase),
        [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"],
        _log);

    /// <summary>Ein Abfragezyklus, danach rückt die Uhr eine Sekunde vor.</summary>
    private async Task Poll(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await Manager.UpdateAsync(_processes.Next(), Ct);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private bool PrimaryOn => _hdr[FakeHdrController.Primary].HdrOn;

    /// <summary>Wie die App: erst initialisieren (erster Zyklus), dann starten.</summary>
    private async Task<LaunchOutcome> Launch(string key)
    {
        if (!Manager.IsInitialized)
        {
            await Poll();
        }

        return await Manager.LaunchAsync(key, Ct);
    }

    // ---------- Weg A: Start und Ende ----------

    [Fact]
    public async Task Session_start_of_enabled_game_switches_hdr_on()
    {
        _config.AddGame("1", DirA);
        await Poll();
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(PrimaryOn);
        var s = Assert.Single(Manager.Sessions);
        Assert.Equal("Steam:1", s.GameKey);
        Assert.Equal(SessionPhase.Running, s.Phase);
        Assert.Equal(SessionTrigger.ProcessDetected, s.Trigger);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.HdrEnabled);
    }

    [Fact]
    public async Task Hotkey_fallback_is_only_allowed_while_no_game_process_runs()
    {
        // E66: beim Erkennen des laufenden Spiels nie, vor dem Start über die App und beim Zurückstellen schon.
        _config.AddGame("1", DirA);
        await Poll();
        var pid = _processes.StartExe(ExeA);
        await Poll();
        Assert.True(PrimaryOn);
        Assert.Equal([false], _hdr.HotkeyAllowed);

        _processes.Stop(pid);
        await Poll(15);
        Assert.False(PrimaryOn);
        Assert.Equal([false, true], _hdr.HotkeyAllowed);

        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        Assert.True(PrimaryOn);
        Assert.Equal([false, true, true], _hdr.HotkeyAllowed);
    }

    [Fact]
    public async Task Hotkey_fallback_blocked_while_any_library_game_runs()
    {
        // Spiel 2 ist weder ausgewählt noch als installiert markiert, läuft aber: Zurückstellen ohne Notlösung.
        _config.AddGame("1", DirA);
        _config.AddGame("2", DirB, hdrAuto: false).IsInstalled = false;
        await Poll();
        var a = _processes.StartExe(ExeA);
        await Poll();
        _processes.StartExe(ExeB);
        await Poll();
        Assert.False(Manager.HotkeyFallbackAllowed);

        _processes.Stop(a);
        await Poll(15);

        Assert.False(PrimaryOn);
        Assert.Equal([false, false], _hdr.HotkeyAllowed);
        Assert.False(Manager.HotkeyFallbackAllowed);
    }

    [Fact]
    public async Task Hotkey_fallback_blocked_when_launched_game_runs_unrecognised()
    {
        // Start über die App, die echte EXE liegt außerhalb des Installationsordners: nach der Startfrist zurückstellen,
        // aber ohne Notlösung, denn das Spiel läuft womöglich.
        _config.Config.Settings.LaunchTimeoutSeconds = 20;
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        _processes.StartExe(@"E:\Woanders\EchtesSpiel.exe");
        await Poll(2);
        Assert.False(Manager.HotkeyFallbackAllowed);

        await Poll(25);

        Assert.False(PrimaryOn);
        Assert.Equal([true, false], _hdr.HotkeyAllowed);
    }

    [Fact]
    public async Task Hotkey_fallback_blocked_for_reapply_during_launch()
    {
        // Bildschirm kann beim Start über die App noch kein HDR; kommt er während des Starts, ohne Notlösung nachschalten.
        _hdr[FakeHdrController.Primary].Capable = false;
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        _hdr[FakeHdrController.Primary].Capable = true;

        await Manager.OnDisplaysChangedAsync(Ct);

        Assert.True(PrimaryOn);
        Assert.False(_hdr.HotkeyAllowed[^1]);
    }

    [Fact]
    public async Task Recovery_adopts_running_launched_game_that_is_neither_selected_nor_installed()
    {
        // HdrPilot stürzte ab, während ein über die App gestartetes Spiel lief (nicht ausgewählt, gerade nicht als
        // installiert markiert). Nach dem Neustart darf HDR nicht mitten im Spiel zurückgestellt werden.
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("2", DirB, hdrAuto: false).IsInstalled = false;
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:2"] });
        var pid = _processes.StartExe(ExeB);

        await Manager.InitializeAsync(_processes.Next(), Ct);

        Assert.True(PrimaryOn);
        Assert.Equal("Steam:2", Assert.Single(Manager.Sessions).GameKey);
        _processes.Stop(pid);
        await Poll(15);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Launch_waits_while_a_launcher_from_the_game_folder_is_open()
    {
        // Wie der REDlauncher von Cyberpunk, der auf die Anmeldung wartet: kein Spielprozess, aber ein Hilfsprogramm
        // aus dem Spielordner läuft. Die Startfrist läuft nicht ab, HDR bleibt an.
        _config.Config.Settings.LaunchTimeoutSeconds = 30;
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        var launcher = _processes.StartExe(DirB + @"\CrashLauncher.exe"); // Hilfsprogramm (Testregel: "Crash")
        await Poll(120);
        Assert.True(PrimaryOn);
        Assert.Equal(SessionPhase.Launching, Assert.Single(Manager.Sessions).Phase);
        Assert.True(_log.Contains(LogLevel.Information, "Startfrist wird verlängert"));

        _processes.StartExe(ExeB);
        await Poll();
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        _processes.Stop(launcher);
    }

    [Fact]
    public async Task Launch_times_out_after_the_launcher_closes()
    {
        _config.Config.Settings.LaunchTimeoutSeconds = 30;
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        var launcher = _processes.StartExe(DirB + @"\CrashLauncher.exe");
        await Poll(60);
        Assert.True(PrimaryOn);

        _processes.Stop(launcher); // Launcher geschlossen, ohne das Spiel zu starten
        await Poll(35);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
    }

    [Fact]
    public async Task Launch_waits_for_an_open_launcher_at_most_30_minutes()
    {
        _config.Config.Settings.LaunchTimeoutSeconds = 30;
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        _processes.StartExe(DirB + @"\CrashLauncher.exe");
        await Poll(29 * 60);
        Assert.True(PrimaryOn);

        await Poll(2 * 60);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
    }

    [Fact]
    public async Task Launch_request_while_the_game_runs_does_not_start_it_again()
    {
        // Doppelter Doppelklick auf die Verknüpfung, während das Spiel schon läuft (beobachtet mit Battlefield 6).
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        _processes.StartExe(ExeB);
        await Poll();

        var outcome = await Manager.LaunchAsync("Steam:2", Ct);

        Assert.True(outcome.Started);
        Assert.Single(_launcher.Launched);
        Assert.True(PrimaryOn);
        Assert.True(_log.Contains(LogLevel.Information, "übergangen"));
    }

    [Fact]
    public async Task Duplicate_launch_request_within_seconds_is_ignored()
    {
        _config.AddGame("2", DirB, hdrAuto: false);
        await Launch("Steam:2");
        await Manager.LaunchAsync("Steam:2", Ct);
        Assert.Single(_launcher.Launched);

        _clock.Advance(TimeSpan.FromSeconds(20)); // später: echter neuer Startversuch
        await Manager.LaunchAsync("Steam:2", Ct);
        Assert.Equal(2, _launcher.Launched.Count);
    }

    [Fact]
    public async Task Session_state_is_written_to_disk_before_switching()
    {
        _config.AddGame("1", DirA);
        SessionStateData? atSwitch = null;
        _hdr.BeforeSet = (_, enable) =>
        {
            if (enable)
            {
                atSwitch = _store.Data;
            }
        };

        _processes.StartExe(ExeA);
        await Poll();

        Assert.NotNull(atSwitch);
        var claim = Assert.Single(atSwitch!.Displays);
        Assert.Equal(FakeHdrController.Primary, claim.MonitorDevicePath);
        Assert.False(claim.WasHdrOn);
        Assert.Equal(["Steam:1"], claim.Games);
    }

    [Fact]
    public async Task Session_not_started_for_game_without_hdr_auto()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        _processes.StartExe(ExeA);
        await Poll(3);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public async Task Session_ignores_helper_processes_in_game_folder()
    {
        _config.AddGame("1", DirA);
        _processes.StartExe(DirA + @"\UnityCrashHandler64.exe");
        _processes.StartExe(DirA + @"\_CommonRedist\vc_redist.x64.exe");
        await Poll(2);

        Assert.Empty(Manager.Sessions);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Session_ends_only_after_grace_period()
    {
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        Assert.True(PrimaryOn);

        _processes.Stop(pid);
        await Poll(10); // Nachlaufzeit 10 s: nach 10 Zyklen (0..9 s) noch an
        Assert.True(PrimaryOn);
        Assert.Equal(SessionPhase.Ending, Assert.Single(Manager.Sessions).Phase);

        await Poll(); // 10 s nach dem ersten Zyklus ohne Prozess
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Null(_store.Data);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.HdrRestored);
    }

    [Fact]
    public async Task Session_survives_launcher_chain_and_self_restart_within_grace()
    {
        _config.AddGame("1", DirA);
        var launcherPid = _processes.StartExe(DirA + @"\Launcher.exe");
        await Poll();
        _processes.Stop(launcherPid);
        await Poll(4);
        var gamePid = _processes.StartExe(ExeA); // Spiel startet sich selbst neu
        await Poll(3);
        _processes.Stop(gamePid);
        await Poll(5);
        _processes.StartExe(ExeA);
        await Poll(20);

        Assert.True(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
        Assert.Equal(0, _hdr.SetCalls(enable: false));
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
    }

    [Fact]
    public async Task Session_grace_period_comes_from_config()
    {
        _config.Config.Settings.GracePeriodSeconds = 0;
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        await Poll();

        Assert.False(PrimaryOn);
    }

    // ---------- Mehrere Spiele ----------

    [Fact]
    public async Task Session_two_games_restore_only_after_last_one_ends()
    {
        _config.AddGame("1", DirA);
        _config.AddGame("2", DirB, source: Core.Games.GameSource.Xbox);
        var a = _processes.StartExe(ExeA);
        await Poll();
        var b = _processes.StartExe(ExeB);
        await Poll();
        Assert.Equal(2, Manager.Sessions.Count);

        _processes.Stop(a);
        await Poll(15);
        Assert.True(PrimaryOn);
        Assert.Single(Manager.Sessions);
        Assert.Equal(["Xbox:2"], _store.Data!.Displays[0].Games);

        _processes.Stop(b);
        await Poll(11);
        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: false));
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_second_game_keeps_original_state_of_first()
    {
        // HDR war vor Spiel A aus; Spiel B kommt dazu, als HDR schon an ist: gemerkt bleibt "aus".
        _config.AddGame("1", DirA);
        _config.AddGame("2", DirB);
        var a = _processes.StartExe(ExeA);
        await Poll();
        var b = _processes.StartExe(ExeB);
        await Poll();
        Assert.False(_store.Data!.Displays.Single().WasHdrOn);

        _processes.Stop(b);
        _processes.Stop(a);
        await Poll(12);
        Assert.False(PrimaryOn);
    }

    // ---------- Zustand vorher ----------

    [Fact]
    public async Task Session_hdr_already_on_stays_on()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        Assert.True(_store.Data!.Displays.Single().WasHdrOn);

        _processes.Stop(pid);
        await Poll(12);

        Assert.True(PrimaryOn);
        Assert.Empty(_hdr.Calls);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_restore_disabled_leaves_hdr_on()
    {
        _config.AddGame("1", DirA, restore: false);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        await Poll(12);

        Assert.True(PrimaryOn);
        Assert.Equal(0, _hdr.SetCalls(enable: false));
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_mixed_restore_settings_keep_hdr_on()
    {
        _config.AddGame("1", DirA, restore: true);
        _config.AddGame("2", DirB, restore: false);
        var a = _processes.StartExe(ExeA);
        var b = _processes.StartExe(ExeB);
        await Poll();
        _processes.Stop(a);
        _processes.Stop(b);
        await Poll(12);

        Assert.True(PrimaryOn);
    }

    // ---------- Bildschirm nicht HDR-fähig oder nicht angeschlossen ----------

    [Fact]
    public async Task Session_display_not_hdr_capable_switches_nothing_and_notifies_once()
    {
        _hdr[FakeHdrController.Primary].Capable = false;
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll(5);

        Assert.Empty(_hdr.Calls);
        Assert.Single(_notifications, n => n.Kind == NotificationKind.DisplayNotHdrCapable);
        Assert.Single(Manager.Sessions); // Spiel wird nie blockiert, Sitzung läuft
        Assert.Null(_store.Data);
        Assert.True(_log.Contains(LogLevel.Warning, "unterstützt derzeit kein HDR"));
    }

    [Fact]
    public async Task Session_display_not_connected_switches_nothing_and_notifies()
    {
        var game = _config.AddGame("1", DirA);
        game.Display = @"\\?\DISPLAY#GONE#1";
        _processes.StartExe(ExeA);
        await Poll(3);

        Assert.Empty(_hdr.Calls);
        Assert.Single(_notifications, n => n.Kind == NotificationKind.DisplayNotConnected);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Session_display_reconnected_during_game_is_switched_on_display_change()
    {
        _hdr[FakeHdrController.Primary].Connected = false;
        var game = _config.AddGame("1", DirA);
        game.Display = FakeHdrController.Primary;
        _processes.StartExe(ExeA);
        await Poll();
        Assert.Empty(_hdr.Calls);

        _hdr[FakeHdrController.Primary].Connected = true;
        await Manager.OnDisplaysChangedAsync(Ct);

        Assert.True(PrimaryOn);
        Assert.False(_store.Data!.Displays.Single().WasHdrOn);
    }

    [Fact]
    public async Task Session_display_change_does_not_override_manual_toggle()
    {
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll();
        _hdr[FakeHdrController.Primary].HdrOn = false; // Nutzer schaltet HDR im Spiel von Hand aus

        await Manager.OnDisplaysChangedAsync(Ct);

        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    // ---------- Bildschirmauswahl ----------

    [Fact]
    public async Task Session_uses_game_display_over_global_setting()
    {
        _hdr.Displays.Add(new FakeDisplay(FakeHdrController.Second, "Odyssey"));
        _config.Config.Settings.TargetDisplays = [FakeHdrController.Primary];
        var game = _config.AddGame("1", DirA);
        game.Display = FakeHdrController.Second;
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(_hdr[FakeHdrController.Second].HdrOn);
        Assert.False(PrimaryOn);
    }

    [Fact]
    public async Task Session_global_setting_with_two_displays_switches_both_and_skips_incapable()
    {
        _hdr.Displays.Add(new FakeDisplay(FakeHdrController.Second, "Büro") { Capable = false });
        _hdr.Displays.Add(new FakeDisplay(@"\\?\DISPLAY#THIRD", "Dritter"));
        _config.Config.Settings.TargetDisplays = [FakeHdrController.Primary, FakeHdrController.Second, @"\\?\DISPLAY#THIRD"];
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();

        Assert.True(PrimaryOn);
        Assert.True(_hdr[@"\\?\DISPLAY#THIRD"].HdrOn);
        Assert.Equal(2, _store.Data!.Displays.Count);

        _processes.Stop(pid);
        await Poll(12);
        Assert.False(PrimaryOn);
        Assert.False(_hdr[@"\\?\DISPLAY#THIRD"].HdrOn);
    }

    [Fact]
    public async Task Session_default_target_is_primary_display()
    {
        _hdr.Displays.Insert(0, new FakeDisplay(FakeHdrController.Second, "Zweiter"));
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(PrimaryOn);
        Assert.False(_hdr[FakeHdrController.Second].HdrOn);
    }

    // ---------- Wiederanlauf nach Absturz ----------

    [Fact]
    public async Task Session_recovery_restores_when_no_selected_game_runs()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true; // HdrPilot hatte eingeschaltet, dann Absturz
        _config.AddGame("1", DirA);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });

        await Poll();

        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);
        Assert.True(_log.Contains(LogLevel.Information, "Kein ausgewähltes Spiel läuft mehr"));
    }

    [Fact]
    public async Task Session_recovery_adopts_running_game_and_restores_after_it_ends()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });
        var pid = _processes.StartExe(ExeA);

        await Poll(3);
        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionTrigger.Recovery, s.Trigger);
        Assert.True(PrimaryOn);
        Assert.Empty(_hdr.Calls);

        _processes.Stop(pid);
        await Poll(12);
        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_recovery_adopts_other_selected_game_on_same_display()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        _config.AddGame("2", DirB);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });
        var b = _processes.StartExe(ExeB); // A ist beendet, B läuft noch

        await Poll(2);
        Assert.True(PrimaryOn);
        Assert.Equal(["Steam:2"], _store.Data!.Displays.Single().Games);
        Assert.False(_store.Data!.Displays.Single().WasHdrOn);

        _processes.Stop(b);
        await Poll(12);
        Assert.False(PrimaryOn); // ursprünglicher Zustand (aus), nicht der Zustand beim Neustart
    }

    [Fact]
    public async Task Session_recovery_with_hdr_on_before_does_not_switch_off()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = true, Games = ["Steam:1"] });

        await Poll();

        Assert.True(PrimaryOn);
        Assert.Empty(_hdr.Calls);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_recovery_with_disconnected_display_keeps_state_until_display_returns()
    {
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _hdr[FakeHdrController.Primary].Connected = false;
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });

        await Poll();
        Assert.NotNull(_store.Data);
        Assert.Empty(_store.Data!.Displays.Single().Games);

        _hdr[FakeHdrController.Primary].Connected = true;
        await Manager.OnDisplaysChangedAsync(Ct);

        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_recovery_survives_a_second_crash_before_restore()
    {
        // Neustart 1 übernimmt laufendes Spiel; erneuter Absturz; Neustart 2 nach Spielende stellt korrekt zurück.
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:1"] });
        var pid = _processes.StartExe(ExeA);
        await Poll();

        _manager = null; // zweiter Absturz: neue Instanz mit demselben Speicher
        _processes.Stop(pid);
        await Poll();

        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_failed_restore_is_retried_on_display_change()
    {
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        _hdr.NextResults.Enqueue(HdrSetStatus.Failed);
        await Poll(12);

        Assert.True(PrimaryOn);
        Assert.NotNull(_store.Data);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.SwitchFailed);

        await Manager.OnDisplaysChangedAsync(Ct);
        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Session_exception_from_hdr_controller_does_not_break_loop()
    {
        _config.AddGame("1", DirA);
        _hdr.ThrowOnSet = true;
        _processes.StartExe(ExeA);
        await Poll(2);

        Assert.Single(Manager.Sessions);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.SwitchFailed);
        Assert.True(_log.Contains(LogLevel.Warning, "Ausnahme beim Schalten"));
    }

    // ---------- Pause ----------

    [Fact]
    public async Task Session_paused_ignores_new_games_but_ends_running_ones()
    {
        _config.AddGame("1", DirA);
        _config.AddGame("2", DirB);
        var a = _processes.StartExe(ExeA);
        await Poll();
        Manager.Paused = true;
        _processes.StartExe(ExeB);
        await Poll();
        Assert.Single(Manager.Sessions);

        _processes.Stop(a);
        await Poll(12);
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
    }

    [Fact]
    public async Task Session_enabling_hdr_auto_while_game_runs_starts_session()
    {
        var game = _config.AddGame("1", DirA, hdrAuto: false);
        _processes.StartExe(ExeA);
        await Poll(2);
        Assert.False(PrimaryOn);

        game.HdrAuto = true;
        _config.Touch();
        await Poll();
        Assert.True(PrimaryOn);
    }

    // ---------- Weg B: Start über die App ----------

    [Fact]
    public async Task Session_launch_via_app_enables_hdr_before_starting_game()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        bool? hdrAtLaunch = null;
        _launcher.OnLaunch = _ => hdrAtLaunch = PrimaryOn;
        _launcher.Clock = () => _clock.UtcNow;

        var outcome = await Launch("Steam:1");

        Assert.True(outcome.Started);
        Assert.True(hdrAtLaunch);
        Assert.NotNull(outcome.HdrConfirmedAt);
        Assert.True(outcome.HdrConfirmedAt <= outcome.LaunchedAt);
        Assert.Equal(SessionPhase.Launching, Assert.Single(Manager.Sessions).Phase);

        _clock.Advance(TimeSpan.FromSeconds(5));
        _processes.StartExe(ExeA);
        await Poll();
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.True(_log.Contains(LogLevel.Information, "HDR-Schritt war abgeschlossen um"));
    }

    [Fact]
    public async Task Session_launch_waits_for_launcher_chain_then_ends_normally()
    {
        _config.AddGame("1", DirA);
        await Launch("Steam:1");
        await Poll(30); // Launcher braucht 30 s
        Assert.True(PrimaryOn);
        var pid = _processes.StartExe(ExeA);
        await Poll(5);
        _processes.Stop(pid);
        await Poll(11);

        Assert.False(PrimaryOn);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
    }

    [Fact]
    public async Task Session_launch_timeout_restores_hdr()
    {
        _config.Config.Settings.LaunchTimeoutSeconds = 20;
        _config.AddGame("1", DirA);
        await Launch("Steam:1");
        await Poll(19);
        Assert.True(PrimaryOn);
        await Poll(2);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
    }

    [Fact]
    public async Task Session_launch_failure_restores_hdr_and_notifies()
    {
        _config.AddGame("1", DirA);
        _launcher.Succeeds = false;

        var outcome = await Launch("Steam:1");

        Assert.False(outcome.Started);
        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.LaunchFailed);
    }

    [Fact]
    public async Task Session_launch_without_start_command_does_not_switch()
    {
        // Eigener Eintrag als Ordner mit mehreren EXEs: kein Start-Befehl, also HDR gar nicht erst anfassen.
        _config.AddGame("1", DirA).LaunchCommand = null;

        var outcome = await Launch("Steam:1");

        Assert.False(outcome.Started);
        Assert.Null(outcome.HdrConfirmedAt);
        Assert.Empty(_hdr.Calls);
        Assert.Empty(_launcher.Launched);
        Assert.Empty(Manager.Sessions);
        Assert.Null(_store.Data);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.LaunchFailed && n.Message.Contains("Spiel 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Session_launch_works_while_paused_and_on_incapable_display()
    {
        _config.AddGame("1", DirA);
        Manager.Paused = true;
        _hdr[FakeHdrController.Primary].Capable = false;

        var outcome = await Launch("Steam:1");

        Assert.True(outcome.Started); // Spiel wird nie blockiert
        Assert.Single(_launcher.Launched);
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.DisplayNotHdrCapable);
    }

    [Fact]
    public async Task Session_launch_before_initialize_is_rejected()
    {
        _config.AddGame("1", DirA);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Manager.LaunchAsync("Steam:1", Ct));
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public async Task Session_initialize_recovers_then_launch_keeps_new_state()
    {
        // Absturzrest vorhanden, App startet mit --run: erst zurückstellen, dann neu einschalten und gemerkt halten.
        _hdr[FakeHdrController.Primary].HdrOn = true;
        _config.AddGame("1", DirA);
        _store.Preset(new DisplayClaimState { MonitorDevicePath = FakeHdrController.Primary, FriendlyName = "MAG", WasHdrOn = false, Games = ["Steam:9"] });

        await Manager.InitializeAsync(_processes.Next(), Ct);
        Assert.False(PrimaryOn);
        Assert.Null(_store.Data);

        var outcome = await Manager.LaunchAsync("Steam:1", Ct);
        Assert.True(outcome.Started);
        Assert.True(PrimaryOn);
        await Poll(5);
        Assert.True(PrimaryOn);
        Assert.False(_store.Data!.Displays.Single().WasHdrOn);
    }

    // ---------- Nachprüfung Phase 3 (unabhängige Prüfer) ----------

    [Fact]
    public async Task Session_relaunch_via_app_during_grace_keeps_hdr_until_process()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        await Launch("Steam:1");
        var pid = _processes.StartExe(ExeA);
        await Poll(5);
        _processes.Stop(pid);
        await Poll(3); // Nachlaufzeit läuft (Ending)

        var outcome = await Manager.LaunchAsync("Steam:1", Ct);
        Assert.True(outcome.Started);
        Assert.Equal(SessionPhase.Launching, Assert.Single(Manager.Sessions).Phase);

        await Poll(15); // Steam braucht 15 s, alte Nachlaufzeit wäre längst abgelaufen
        Assert.True(PrimaryOn);
        Assert.Equal(0, _hdr.SetCalls(enable: false));
        _processes.StartExe(ExeA);
        await Poll();
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
    }

    [Fact]
    public async Task Session_relaunch_restarts_launch_timeout()
    {
        _config.Config.Settings.LaunchTimeoutSeconds = 120;
        _config.AddGame("1", DirA, hdrAuto: false);
        await Launch("Steam:1");
        await Poll(100);
        await Manager.LaunchAsync("Steam:1", Ct);
        await Poll(25);

        Assert.Equal(SessionPhase.Launching, Assert.Single(Manager.Sessions).Phase);
        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Session_relaunch_switches_hdr_on_again_after_manual_off()
    {
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll();
        _hdr[FakeHdrController.Primary].HdrOn = false; // Nutzer hat im Spiel von Hand ausgeschaltet

        await Manager.LaunchAsync("Steam:1", Ct);

        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Session_state_save_failure_is_reported_and_game_not_blocked()
    {
        var failing = new FailingStateStore();
        _manager = new SessionManager(_hdr, _config, failing, _launcher, _clock, (_, _) => false, [], _log);
        _manager.Notification += (_, n) => _notifications.Add(n);
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll();

        Assert.True(PrimaryOn); // das Spiel hat Vorrang
        Assert.Contains(_notifications, n => n.Kind == NotificationKind.StateNotSaved);
        Assert.True(failing.SaveAttempts >= 2); // ein erneuter Versuch
    }

    [Fact]
    public async Task Session_launch_of_not_installed_game_without_hdr_auto_is_matched()
    {
        var g = _config.AddGame("1", DirA, hdrAuto: false);
        g.IsInstalled = false;
        await Launch("Steam:1");
        _processes.StartExe(ExeA);
        await Poll();

        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        await Poll(130);
        Assert.True(PrimaryOn);
        Assert.Equal(0, _hdr.SetCalls(enable: false));
    }

    [Fact]
    public async Task Session_learns_game_exe_behind_starter_chain()
    {
        _config.AddGame("1", DirA);
        var starter = _processes.StartExe(DirA + @"\PlayA.exe");
        await Poll();
        _processes.Stop(starter);
        await Poll(4); // Lücke kürzer als die Nachlaufzeit
        _processes.StartExe(ExeA);
        await Poll(3);

        var learned = _config.Config.FindGame("Steam:1")!.DetectedExecutables;
        Assert.Contains(ExeA, learned, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(DirA + @"\PlayA.exe", learned, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FailingStateStore : ISessionStateStore
    {
        public int SaveAttempts { get; private set; }

        public SessionStateData? Load() => null;

        public void Save(SessionStateData data)
        {
            SaveAttempts++;
            throw new IOException("Datenträger voll");
        }

        public void Delete()
        {
        }
    }
    [Fact]
    public async Task Session_launch_unknown_game_does_nothing()
    {
        var outcome = await Launch("Steam:404");
        Assert.False(outcome.Started);
        Assert.Empty(_launcher.Launched);
        Assert.Empty(_hdr.Calls);
    }

    // ---------- Gemerkte EXEs und Zuordnung ----------

    [Fact]
    public async Task Session_learns_executables_on_first_run_only()
    {
        _config.AddGame("1", DirA);
        var a = _processes.StartExe(ExeA);
        var helper = _processes.StartExe(DirA + @"\bin\x64\SpielA_Helper.exe");
        await Poll();
        _processes.Stop(a);
        _processes.Stop(helper);
        await Poll(12);

        var learned = _config.Config.FindGame("Steam:1")!.DetectedExecutables;
        Assert.Equal(2, learned.Count);
        Assert.Contains(ExeA, learned, StringComparer.OrdinalIgnoreCase);

        _processes.StartExe(DirA + @"\other.exe");
        await Poll(2);
        Assert.Equal(2, _config.Config.FindGame("Steam:1")!.DetectedExecutables.Count);
    }

    [Fact]
    public async Task Session_selected_game_marked_not_installed_still_switches()
    {
        // Steam-Update: Spiel fehlte beim letzten Einlesen, ist aber ausgewählt und läuft.
        var game = _config.AddGame("1", DirA);
        game.IsInstalled = false;
        var other = _config.AddGame("2", DirB, hdrAuto: false);
        other.IsInstalled = false;
        _processes.StartExe(ExeA);
        _processes.StartExe(ExeB);
        await Poll();

        Assert.Equal("Steam:1", Assert.Single(Manager.Sessions).GameKey);
        Assert.True(PrimaryOn);
        Assert.DoesNotContain(_config.Recorded, r => r.Key == "Steam:2"); // nicht installiert und nicht ausgewählt: nicht beachtet
    }

    [Fact]
    public async Task Session_user_excluded_exe_does_not_count()
    {
        var game = _config.AddGame("1", DirA);
        game.ExcludedExecutables = ["Launcher.exe"];
        _processes.StartExe(DirA + @"\Launcher.exe");
        await Poll(3);

        Assert.Empty(Manager.Sessions);
    }

    [Fact]
    public async Task Session_protected_process_matched_by_learned_name()
    {
        var game = _config.AddGame("1", DirA);
        game.DetectedExecutables = [ExeA];
        _processes.Start("SpielA.exe", exePath: null); // Pfad nicht lesbar (geschützt)
        await Poll();

        Assert.Single(Manager.Sessions);
        Assert.True(PrimaryOn);
    }

    [Fact]
    public async Task Session_every_switch_is_logged_with_trigger_display_and_states()
    {
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        await Poll(12);

        var switches = _log.Entries.Where(e => e.Message.StartsWith("HDR-Schaltung", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, switches.Count);
        Assert.Contains("Auslöser: Spielstart erkannt", switches[0].Message, StringComparison.Ordinal);
        Assert.Contains("Bildschirm: MAG 341C OLED", switches[0].Message, StringComparison.Ordinal);
        Assert.Contains("vorher: aus | nachher: an | Ergebnis: Success", switches[0].Message, StringComparison.Ordinal);
        Assert.Contains("Auslöser: Spielende", switches[1].Message, StringComparison.Ordinal);
        Assert.Contains("vorher: an | nachher: aus", switches[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_state_changed_event_fires_on_start_and_end()
    {
        var count = 0;
        Manager.StateChanged += (_, _) => count++;
        _config.AddGame("1", DirA);
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        await Poll(12);

        Assert.True(count >= 2);
    }
}
