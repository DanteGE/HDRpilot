using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Notlösung „Auto HDR global nur während des Spiels“ (E88): setzt AutoHDREnable=1 im globalen Wert, solange ein Spiel mit
/// „Windows Auto HDR“ läuft oder sein Start angekündigt ist, und stellt danach zurück (Nachlaufzeit, Neustart-Puffer,
/// Absturz von HDRpilot). Nie ohne Einstellung, nie ohne laufendes Spiel. Registry nur als Attrappe.
/// </summary>
public sealed class AutoHdrGlobalFallbackTests
{
    private readonly AutoHdrHarness _h = new();
    private string _a = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => _h.Clock.UtcNow;

    /// <summary>Spiel A mit „Windows Auto HDR“, Notlösung an, globaler Wert wie beim Nutzer; Dienst gestartet, erster Zyklus.</summary>
    private void Setup(bool fallback = true, string? global = GlobalOff, bool start = true)
    {
        if (global is not null)
        {
            _h.Store.SetString(Global, global);
        }

        _h.Store.SetString(ExeA, "AutoHDREnable=2097;"); // je Spiel schon an: nur der globale Wert interessiert hier
        _a = _h.AddGame("1", ExeA);
        _h.SetFallback(fallback);
        if (start)
        {
            _h.Service.Start();
            _h.Service.OnRecovery([], _h.Current, Now);
        }
    }

    /// <summary>Ein Zyklus mit den laufenden Spielen, danach eine Sekunde später.</summary>
    private void Cycle(params string[] running) => Cycle(false, running);

    private void Cycle(bool paused, params string[] running)
    {
        _h.Service.OnCycle(running, _h.Current, paused, Now);
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
    }

    private void Cycles(int seconds, params string[] running)
    {
        for (var i = 0; i < seconds; i++)
        {
            Cycle(running);
        }
    }

    [Fact]
    public void Setting_off_never_writes_even_while_the_game_runs()
    {
        Setup(fallback: false);

        Cycles(30, _a);
        Cycles(30);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
    }

    [Fact]
    public void Never_active_while_no_such_game_runs()
    {
        Setup();
        var other = _h.AddGame("2", ExeB, autoHdr: false, hdrAuto: true);

        Cycles(10);
        Cycles(10, other); // läuft, aber ohne „Windows Auto HDR“

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(0, _h.Store.ReadsOf(Global));
    }

    [Fact]
    public void Detected_game_sets_the_token_and_its_end_restores_after_the_grace_period()
    {
        Setup();
        _h.Clock.Advance(TimeSpan.FromMinutes(10));

        Cycle(_a);

        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(["set " + Global + "=" + GlobalOn], _h.GlobalOperations);
        Assert.Equal(AutoHdrEntryStatus.Applied, _h.Marker!.Status);
        Assert.Equal(GlobalOff, _h.Marker.Original.Text);
        Assert.True(_h.Service.GlobalFallbackActive);
        Assert.True(_h.Logged("AutoHDREnable=1 gesetzt für „Spiel 1“ (Spielstart erkannt), vorher „" + GlobalOff + "“"));

        Cycles(6 * 60, _a); // langer Lauf: danach nur die Nachlaufzeit
        Cycles(9);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.False(_h.Service.GlobalFallbackActive);
        Assert.True(_h.Logged("zurückgestellt auf „" + GlobalOff + "“ (Spielende)"));
    }

    [Fact]
    public void Short_run_keeps_the_token_for_the_restart_buffer_and_a_restart_continues()
    {
        Setup();

        Cycles(30, _a); // kurzer Lauf
        Cycles(40);
        Assert.Equal(GlobalOn, _h.Value(Global)); // nach 40 s im Puffer von 60 s

        Cycles(5, _a); // Neustart im Puffer: läuft einfach weiter
        Cycles(30);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(31);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Single(_h.GlobalOperations, o => o.Contains(GlobalOn, StringComparison.Ordinal));
    }

    [Fact]
    public void Restart_buffer_zero_means_only_the_grace_period()
    {
        Setup();
        _h.Config.Update(c => c.Settings.RestartBufferSeconds = 0);

        Cycles(30, _a);
        Cycles(11);

        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Announced_start_sets_the_token_before_the_process_and_expires_after_the_launch_timeout()
    {
        Setup();

        _h.Service.OnStartAnnounced(_h.Current.FindGame(_a)!, SessionTrigger.SteamLaunchOption, Now);

        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.True(_h.Logged("(Start über Steam-Startoption)"));

        Cycles(119);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Announced_start_followed_by_the_process_keeps_the_token_until_the_game_ends()
    {
        Setup();
        _h.Service.OnStartAnnounced(_h.Current.FindGame(_a)!, SessionTrigger.LaunchedViaApp, Now);
        Cycles(100);

        Cycles(400, _a); // Spiel läuft länger als die Startfrist
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(11);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Single(_h.GlobalOperations, o => o.Contains(GlobalOn, StringComparison.Ordinal));
    }

    [Fact]
    public void Global_already_on_is_left_alone_and_never_restored()
    {
        Setup(global: GlobalOn);

        Cycles(5, _a);
        Cycles(100);

        Assert.Empty(_h.GlobalOperations);
        Assert.Null(_h.Marker);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(1, _h.LogCount("Auto HDR global ist schon an"));
        Assert.Equal(1, _h.Store.ReadsOf(Global)); // nicht bei jedem Zyklus neu gelesen
    }

    [Fact]
    public void Missing_global_value_is_created_and_deleted_again()
    {
        Setup(global: null);

        Cycles(400, _a);
        Assert.Equal("AutoHDREnable=1;", _h.Value(Global));
        Cycles(11);

        Assert.False(_h.Store.Values.ContainsKey(Global));
    }

    [Fact]
    public void Unclean_global_value_is_never_touched()
    {
        Setup(global: null);
        _h.Store.Set(Global, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "0"));

        Cycles(5, _a);

        Assert.Empty(_h.GlobalOperations);
        Assert.True(_h.Logged("ist kein sauberer REG_SZ"));
    }

    [Fact]
    public void External_change_while_active_is_left_as_it_is()
    {
        Setup();
        Cycles(400, _a);
        _h.Store.SetString(Global, "AutoHDREnable=0;SwapEffectUpgradeEnable=0;"); // Nutzer schaltet in Windows um

        Cycles(11);

        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=0;", _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.True(_h.Logged("extern geändert"));
    }

    [Fact]
    public void Two_overlapping_games_keep_it_until_the_last_ends()
    {
        Setup();
        _h.Store.SetString(ExeB, "AutoHDREnable=2097;");
        var b = _h.AddGame("2", ExeB);

        Cycles(400, _a);
        Cycles(100, _a, b);
        Cycles(400, b); // A endet, B läuft weiter (danach lange genug für die Nachlaufzeit)
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(11);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Single(_h.GlobalOperations, o => o.Contains(GlobalOn, StringComparison.Ordinal));
    }

    [Fact]
    public void Switching_the_setting_off_releases_immediately()
    {
        Setup();
        Cycles(10, _a);

        _h.SetFallback(false);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.True(_h.Logged("(Notlösung ausgeschaltet)"));
        Cycles(10, _a);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Removed_game_or_switched_off_option_counts_as_inactive()
    {
        Setup();
        Cycles(10, _a);

        _h.RemoveGame(_a);
        Assert.Equal(GlobalOff, _h.Value(Global));

        // Gegenprobe mit der Option: wieder an, dann nur „Windows Auto HDR“ aus.
        _a = _h.AddGame("1", ExeA);
        Cycles(10, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        _h.SetAutoHdr(_a, false);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Shutdown_releases()
    {
        Setup();
        Cycles(10, _a);

        _h.Service.Shutdown();

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.True(_h.Logged("(HDRpilot wird beendet)"));
    }

    [Fact]
    public void Crash_recovery_restores_when_the_game_is_gone()
    {
        Setup();
        Cycles(10, _a);
        Assert.NotNull(_h.Marker); // HDRpilot stürzt hier ab: kein Shutdown

        var service = _h.Restart();
        service.Start();
        Assert.Equal(GlobalOn, _h.Value(Global));
        service.OnRecovery([], _h.Current, Now);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.True(_h.Logged("der letzte Lauf endete nicht sauber"));
        Assert.True(_h.Logged("(Wiederanlauf nach Absturz)"));
    }

    [Fact]
    public void Crash_recovery_adopts_a_running_game_and_restores_after_its_grace_period()
    {
        Setup();
        Cycles(10, _a);
        var writes = _h.GlobalOperations.Count;

        var service = _h.Restart();
        service.Start();
        service.OnRecovery([_a], _h.Current, Now);
        Cycles(3, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(writes, _h.GlobalOperations.Count); // übernommen, nicht neu geschrieben

        Cycles(11); // übernommene Läufe sind nie kurz: nur die Nachlaufzeit
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Pending_marker_without_a_write_is_dropped_without_touching()
    {
        Setup(start: false);
        _h.WriteMarker(new AutoHdrGlobalMarker
        {
            RegistryKey = _h.Store.KeyName,
            Status = AutoHdrEntryStatus.Pending,
            Original = new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = GlobalOff },
            OriginalToken = "0",
            Written = GlobalOn,
            Games = [_a],
            WindowsBuild = 26300,
        });

        _h.Service.Start();
        _h.Service.OnRecovery([], _h.Current, Now);

        Assert.Empty(_h.GlobalOperations);
        Assert.Null(_h.Marker);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Marker_for_another_key_keeps_the_fallback_off()
    {
        Setup(start: false);
        _h.WriteMarker(new AutoHdrGlobalMarker
        {
            RegistryKey = AutoHdrRegistryLocation.Real.KeyName,
            Status = AutoHdrEntryStatus.Applied,
            Original = new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = GlobalOff },
            Written = GlobalOn,
        });

        _h.Service.Start();
        _h.Service.OnRecovery([], _h.Current, Now);
        Cycles(10, _a);

        Assert.Empty(_h.GlobalOperations);
        Assert.NotNull(_h.Marker);
        Assert.True(_h.Logged("gehört zu " + AutoHdrRegistryLocation.Real.KeyName, LogLevel.Warning));
    }

    [Fact]
    public void Marker_save_failure_writes_nothing_and_backs_off()
    {
        Setup();
        _h.Fs.FailWriting(_h.Paths.AutoHdrGlobalFile, new IOException("Platte voll"));

        Cycle(_a);
        Assert.Empty(_h.GlobalOperations);

        _h.Fs.FailWriting(_h.Paths.AutoHdrGlobalFile, null);
        Cycles(28, _a);
        Assert.Empty(_h.GlobalOperations); // 30 s Pause nach dem Fehler
        Cycles(2, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
    }

    [Fact]
    public void Registry_write_failure_removes_the_marker_and_retries_later()
    {
        Setup();
        _h.Store.FailWrites = new UnauthorizedAccessException("verweigert");

        Cycle(_a);
        Assert.Null(_h.Marker);
        Assert.Equal(GlobalOff, _h.Value(Global));

        _h.Store.FailWrites = null;
        Cycles(31, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
    }

    [Fact]
    public void Paused_automation_ignores_new_detections_but_tracked_games_run_out_normally()
    {
        Setup();

        Cycle(paused: true, _a);
        Assert.Empty(_h.GlobalOperations);

        Cycles(400, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycle(paused: true, _a); // schon erfasst: bleibt
        Assert.Equal(GlobalOn, _h.Value(Global));
        for (var i = 0; i < 11; i++)
        {
            Cycle(paused: true);
        }

        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public async Task Rollback_releases_the_global_token_and_counts_it()
    {
        Setup();
        Cycles(10, _a);

        var result = await _h.Service.RollbackAllAsync(Ct);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.Equal(1, result.GamesSwitchedOff);
        Assert.Equal(1, result.Restored);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA)); // war schon vorher an: bleibt
    }

    [Fact]
    public async Task Launch_via_app_sets_the_token_before_hdr_is_switched_and_before_the_game_starts()
    {
        Setup();
        var hdr = FakeHdrController.WithPrimary();
        var launcher = new FakeLauncher();
        var atSwitch = new List<string?>();
        var atLaunch = new List<string?>();
        hdr.BeforeSet = (_, _) => atSwitch.Add(_h.Value(Global));
        launcher.OnLaunch = _ => atLaunch.Add(_h.Value(Global));
        var sessions = _h.CreateSessions(hdr, launcher);
        await sessions.InitializeAsync(new ProcessScript().Next(), Ct);

        var outcome = await sessions.LaunchAsync(_a, Ct);

        Assert.True(outcome.Started);
        Assert.Equal([GlobalOn], atSwitch);
        Assert.Equal([GlobalOn], atLaunch);
    }

    [Fact]
    public async Task Detected_game_without_hdr_automation_sets_the_token_through_the_session_cycle()
    {
        Setup(start: true);
        var processes = new ProcessScript();
        var sessions = _h.CreateSessions(FakeHdrController.WithPrimary(), new FakeLauncher());
        await sessions.UpdateAsync(processes.Next(), Ct);

        processes.StartExe(ExeA);
        await sessions.UpdateAsync(processes.Next(), Ct);

        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Empty(sessions.Sessions); // ohne „HDR automatisch“ keine HDR-Sitzung
    }
}
