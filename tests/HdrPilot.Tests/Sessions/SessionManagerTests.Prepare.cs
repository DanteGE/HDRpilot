using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Sessions;

/// <summary>Vorbereitung für die Steam-Startoption (PrepareAsync) und „zuletzt verwendet“ für alle Spiele.</summary>
public sealed partial class SessionManagerTests
{
    /// <summary>Wie die App: erst initialisieren (erster Zyklus), dann vorbereiten.</summary>
    private async Task<PrepareOutcome> Prepare(string key)
    {
        if (!Manager.IsInitialized)
        {
            await Poll();
        }

        return await Manager.PrepareAsync(key, SessionTrigger.SteamLaunchOption, Ct);
    }

    private DateTimeOffset? LastPlayed(string key) => _config.Config.FindGame(key)!.LastPlayed;

    // ---------- Steam-Startoption: HDR vor dem Spielstart ----------

    [Fact]
    public async Task Prepare_switches_hdr_on_without_calling_the_launcher()
    {
        _config.AddGame("1", DirA, hdrAuto: false);

        var outcome = await Prepare("Steam:1");

        Assert.True(outcome.Prepared);
        Assert.Equal(_clock.UtcNow, outcome.HdrConfirmedAt);
        Assert.True(PrimaryOn);
        Assert.Empty(_launcher.Launched);
        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionTrigger.SteamLaunchOption, s.Trigger);
        Assert.Equal(SessionPhase.Launching, s.Phase);
        Assert.True(_log.Contains(LogLevel.Information, "Auslöser: Start über Steam-Startoption"));
        Assert.True(_log.Contains(LogLevel.Information, "Das Spiel wird jetzt gestartet"));
    }

    [Fact]
    public async Task Prepare_writes_state_before_switching()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        SessionStateData? atSwitch = null;
        _hdr.BeforeSet = (_, enable) =>
        {
            if (enable)
            {
                atSwitch = _store.Data;
            }
        };

        await Prepare("Steam:1");

        var claim = Assert.Single(atSwitch!.Displays);
        Assert.False(claim.WasHdrOn);
        Assert.Equal(["Steam:1"], claim.Games);
    }

    [Fact]
    public async Task Prepared_session_runs_with_the_game_and_restores_after_it()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        await Prepare("Steam:1");
        _clock.Advance(TimeSpan.FromSeconds(1.5)); // Nachlauf des Wrappers, dann startet das Spiel
        var pid = _processes.StartExe(ExeA);
        await Poll();
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.True(_log.Contains(LogLevel.Information, "HDR-Schritt war abgeschlossen um"));

        _processes.Stop(pid);
        await Poll(12);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Null(_store.Data);
    }

    [Fact]
    public async Task Prepare_counts_as_issued_start_for_the_hotkey_fallback()
    {
        // E66: Schalten vor dem Start (das Spiel läuft noch nicht) darf die Notlösung wie der Start über die App;
        // danach kann das Spiel unerkannt laufen, also nicht mehr (auch nicht beim Zurückstellen nach der Startfrist).
        _config.Config.Settings.LaunchTimeoutSeconds = 20;
        _config.AddGame("1", DirA, hdrAuto: false);

        await Prepare("Steam:1");
        Assert.Equal([true], _hdr.HotkeyAllowed);
        Assert.False(Manager.HotkeyFallbackAllowed);

        await Poll(25);

        Assert.False(PrimaryOn);
        Assert.Empty(Manager.Sessions);
        Assert.Equal([true, false], _hdr.HotkeyAllowed);
    }

    [Fact]
    public async Task Prepare_reapply_during_launch_does_not_use_the_hotkey_fallback()
    {
        _hdr[FakeHdrController.Primary].Capable = false;
        _config.AddGame("1", DirA, hdrAuto: false);
        await Prepare("Steam:1");
        _hdr[FakeHdrController.Primary].Capable = true;

        await Manager.OnDisplaysChangedAsync(Ct);

        Assert.True(PrimaryOn);
        Assert.False(_hdr.HotkeyAllowed[^1]);
    }

    [Fact]
    public async Task Prepare_unknown_game_switches_nothing()
    {
        var outcome = await Prepare("Steam:404");

        Assert.False(outcome.Prepared);
        Assert.Null(outcome.HdrConfirmedAt);
        Assert.Contains("Steam:404", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_hdr.Calls);
        Assert.Empty(Manager.Sessions);
        Assert.Null(_store.Data);
        Assert.DoesNotContain(_notifications, n => n.Kind == NotificationKind.LaunchFailed); // das Spiel startet ja trotzdem
    }

    [Fact]
    public async Task Prepare_works_while_paused_and_for_games_without_hdr_auto_or_start_command()
    {
        _config.AddGame("1", DirA, hdrAuto: false).LaunchCommand = null;
        Manager.Paused = true;

        var outcome = await Prepare("Steam:1");

        Assert.True(outcome.Prepared);
        Assert.True(PrimaryOn);
        Assert.Single(Manager.Sessions);
    }

    [Fact]
    public async Task Prepare_right_after_a_launch_via_the_app_is_a_duplicate_and_keeps_the_session()
    {
        // Start über HdrPilot (steam://rungameid/…), Steam ruft wenige Sekunden später die Startoption auf (E71).
        _config.AddGame("1", DirA, hdrAuto: false);
        await Launch("Steam:1");
        _clock.Advance(TimeSpan.FromSeconds(3));

        var outcome = await Manager.PrepareAsync("Steam:1", SessionTrigger.SteamLaunchOption, Ct);

        Assert.True(outcome.Prepared);
        Assert.Contains("wird bereits gestartet", outcome.Message, StringComparison.Ordinal);
        Assert.Single(_launcher.Launched);
        var s = Assert.Single(Manager.Sessions);
        Assert.Equal(SessionTrigger.LaunchedViaApp, s.Trigger);
        Assert.Equal(SessionPhase.Launching, s.Phase);
        Assert.Equal(1, _hdr.SetCalls(enable: true));
        Assert.True(_log.Contains(LogLevel.Information, "Sitzung besteht schon"));
    }

    [Fact]
    public async Task Prepare_while_the_game_runs_is_a_duplicate_and_turns_hdr_back_on()
    {
        _config.AddGame("1", DirA);
        _processes.StartExe(ExeA);
        await Poll();
        _hdr[FakeHdrController.Primary].HdrOn = false; // von Hand ausgeschaltet

        var outcome = await Manager.PrepareAsync("Steam:1", SessionTrigger.SteamLaunchOption, Ct);

        Assert.True(outcome.Prepared);
        Assert.Contains("läuft bereits", outcome.Message, StringComparison.Ordinal);
        Assert.True(PrimaryOn);
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
    }

    [Fact]
    public async Task Prepare_during_grace_period_waits_again_for_the_game()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        await Prepare("Steam:1");
        var pid = _processes.StartExe(ExeA);
        await Poll(5);
        _processes.Stop(pid);
        await Poll(3); // Nachlaufzeit läuft

        var outcome = await Manager.PrepareAsync("Steam:1", SessionTrigger.SteamLaunchOption, Ct);

        Assert.True(outcome.Prepared);
        Assert.Equal(SessionPhase.Launching, Assert.Single(Manager.Sessions).Phase);
        Assert.True(_log.Contains(LogLevel.Information, "Erneuter Start über Steam-Startoption"));
        await Poll(15); // die alte Nachlaufzeit wäre abgelaufen
        Assert.True(PrimaryOn);
        Assert.Equal(0, _hdr.SetCalls(enable: false));
    }

    [Fact]
    public async Task Prepare_before_initialize_is_rejected()
    {
        _config.AddGame("1", DirA);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Manager.PrepareAsync("Steam:1", SessionTrigger.SteamLaunchOption, Ct));
        Assert.Empty(_hdr.Calls);
    }

    // ---------- Zuletzt verwendet ----------

    [Fact]
    public async Task Last_played_is_recorded_for_any_matched_game_once_per_run()
    {
        _config.AddGame("1", DirA, hdrAuto: false); // installiert, ohne HDR-Sitzung
        await Poll();
        var firstRun = _clock.UtcNow;
        var pid = _processes.StartExe(ExeA);
        await Poll(3);

        Assert.Empty(Manager.Sessions);
        Assert.Equal(firstRun, LastPlayed("Steam:1"));
        Assert.Single(_config.LastPlayedRecords);

        _processes.Stop(pid);
        await Poll(4); // Neustart des Spiels innerhalb der Nachlaufzeit: derselbe Lauf
        pid = _processes.StartExe(ExeA);
        await Poll(3);
        Assert.Single(_config.LastPlayedRecords);

        _processes.Stop(pid);
        await Poll(12); // Lauf zu Ende
        var secondRun = _clock.UtcNow;
        _processes.StartExe(ExeA);
        await Poll();

        Assert.Equal(2, _config.LastPlayedRecords.Count);
        Assert.Equal(secondRun, LastPlayed("Steam:1"));
    }

    [Fact]
    public async Task Last_played_is_written_once_per_hdr_session_too()
    {
        _config.AddGame("1", DirA);
        var start = _clock.UtcNow;
        var pid = _processes.StartExe(ExeA);
        await Poll();
        _processes.Stop(pid);
        await Poll(12);

        Assert.Empty(Manager.Sessions);
        Assert.Equal(("Steam:1", start), Assert.Single(_config.LastPlayedRecords));
    }

    [Fact]
    public async Task Last_played_is_written_after_the_hdr_switch_so_it_never_delays_it()
    {
        _config.AddGame("1", DirA);
        int? recordsAtSwitch = null;
        _hdr.BeforeSet = (_, enable) => recordsAtSwitch ??= _config.LastPlayedRecords.Count;
        _processes.StartExe(ExeA);

        await Poll();

        Assert.Equal(0, recordsAtSwitch);
        Assert.Single(_config.LastPlayedRecords);
    }

    [Fact]
    public async Task Last_played_ignores_processes_of_games_outside_the_matcher()
    {
        _config.AddGame("2", DirB, hdrAuto: false).IsInstalled = false; // weder installiert noch ausgewählt
        _processes.StartExe(ExeB);
        await Poll(3);

        Assert.Empty(_config.LastPlayedRecords);
        Assert.Null(LastPlayed("Steam:2"));
    }

    [Fact]
    public async Task Last_played_is_recorded_for_each_of_two_games()
    {
        _config.AddGame("1", DirA, hdrAuto: false);
        _config.AddGame("2", DirB, hdrAuto: false);
        _processes.StartExe(ExeA);
        _processes.StartExe(ExeB);
        await Poll(3);

        Assert.Equal(["Steam:1", "Steam:2"], _config.LastPlayedRecords.Select(r => r.Key).Order());
    }
}
