using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Globaler Auto-HDR-Schalter um Spielstarts, die HDRpilot vorher kennt (E88, nach dem Livetest G9.6): AutoHDREnable=1 im
/// Wert DirectXUserGlobalSettings direkt bei der Ankündigung (vor Spielprozess und HDR-Schalten), zurück nach der Sitzung
/// (Nachlaufzeit, Neustart-Puffer, mehrere Spiele teilen sich den Schalter), nach einem Absturz beim nächsten Start; nie für
/// bloß erkannte Spiele, nie ohne Spiel; eine Änderung durch den Nutzer bleibt. Registry nur als Attrappe.
/// </summary>
public sealed class AutoHdrGlobalSwitchTests
{
    private readonly AutoHdrHarness _h = new();
    private string _a = string.Empty;

    private DateTimeOffset Now => _h.Clock.UtcNow;

    /// <summary>Spiel A mit „Windows Auto HDR“, globaler Wert wie beim Nutzer; Dienst gestartet, Wiederanlauf ohne Spiel.</summary>
    private void Setup(string? global = GlobalOff, bool start = true)
    {
        if (global is not null)
        {
            _h.Store.SetString(Global, global);
        }

        _a = _h.AddGame("1", ExeA);
        if (start)
        {
            _h.Service.Start();
            _h.Service.OnRecovery([], _h.Current, Now);
        }
    }

    /// <summary>Ein Zyklus mit den laufenden Spielen, danach eine Sekunde später.</summary>
    private void Cycle(params string[] running) => CycleWaiting([], running);

    private void CycleWaiting(string[] waiting, params string[] running)
    {
        _h.Service.OnCycle(running, waiting, _h.Current, Now);
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
    public void Game_started_without_hdrpilot_never_touches_the_switch()
    {
        Setup();
        var other = _h.AddGame("2", ExeB, autoHdr: false, hdrAuto: true);

        Cycles(30, _a); // mit „Windows Auto HDR“, aber nicht über HDRpilot gestartet: hat den Wert schon gelesen
        Cycles(30);
        Cycles(10, other);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(0, _h.Store.ReadsOf(Global));
        Assert.Null(_h.Marker);
        Assert.False(_h.Service.HoldsGlobalSwitch);
    }

    [Fact]
    public void Game_started_without_hdrpilot_does_not_hold_the_switch_of_an_announced_one()
    {
        Setup();
        var b = _h.AddGame("2", ExeB); // auch mit „Windows Auto HDR“, aber direkt im Launcher gestartet

        _h.Announce(_a);
        Cycles(400, _a, b);
        Cycles(9, b); // A endet, B läuft weiter
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2, b);

        Assert.Equal(GlobalOff, _h.Value(Global)); // B hielt den Schalter nicht: Windows las ihn bei seinem Start schon
        Assert.Equal(1, _h.SetOnCount);
    }

    [Fact]
    public void Announced_start_sets_the_token_at_once_and_the_end_restores_after_the_grace_period()
    {
        Setup();

        _h.Announce(_a, SessionTrigger.LaunchedViaApp);

        Assert.Equal(GlobalOn, _h.Value(Global)); // vor dem Spielprozess
        Assert.Equal(["set " + Global + "=" + GlobalOn], _h.GlobalOperations);
        Assert.Equal(AutoHdrEntryStatus.Applied, _h.Marker!.Status);
        Assert.Equal(GlobalOff, _h.Marker.Original.Text);
        Assert.Equal("0", _h.Marker.OriginalToken);
        Assert.Equal([_a], _h.Marker.Games);
        Assert.True(_h.Service.HoldsGlobalSwitch);
        Assert.True(_h.Logged("AutoHDREnable=1 gesetzt für „Spiel 1“ (Start über HDRpilot), vorher „" + GlobalOff + "“"));

        Cycles(6 * 60, _a); // langer Lauf: danach nur die Nachlaufzeit
        Cycles(9);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
        Assert.False(_h.Service.HoldsGlobalSwitch);
        Assert.True(_h.Logged("zurückgestellt auf „" + GlobalOff + "“ (Spielende)"));
    }

    [Fact]
    public void Announced_game_without_the_option_changes_nothing()
    {
        Setup();
        var plain = _h.AddGame("2", ExeB, autoHdr: false, hdrAuto: true);

        _h.Announce(plain, SessionTrigger.SteamLaunchOption);
        Cycles(30, plain);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(0, _h.Store.ReadsOf(Global));

        _h.Announce(_a, SessionTrigger.SteamLaunchOption); // Gegenprobe: mit der Option
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.True(_h.Logged("(Start über Steam-Startoption)"));
    }

    [Fact]
    public void Short_run_keeps_the_token_for_the_restart_buffer_and_a_restart_continues()
    {
        Setup();
        _h.Announce(_a);

        Cycles(30, _a); // kurzer Lauf
        Cycles(40);
        Assert.Equal(GlobalOn, _h.Value(Global)); // nach 40 s im Puffer von 60 s

        Cycles(5, _a); // Neustart im Puffer (etwa vom Spiel selbst): läuft einfach weiter
        Cycles(30);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(31);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Equal(1, _h.SetOnCount);
    }

    [Fact]
    public void Restart_buffer_zero_means_only_the_grace_period()
    {
        Setup();
        _h.Config.Update(c => c.Settings.RestartBufferSeconds = 0);
        _h.Announce(_a);

        Cycles(30, _a);
        Cycles(9);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2);

        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Announcement_without_a_process_expires_after_the_launch_timeout()
    {
        Setup();
        _h.Announce(_a, SessionTrigger.LauncherSignal);

        Cycles(119);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(2);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.True(_h.Logged("ohne Spielprozess verfallen"));
    }

    [Fact]
    public void Announcement_lives_while_the_hdr_session_waits_and_ends_with_it()
    {
        Setup();
        _h.Announce(_a);

        for (var i = 0; i < 300; i++)
        {
            CycleWaiting([_a]); // Sitzung wartet länger als die Startfrist (Launcher-Fenster aus dem Spielordner)
        }

        Assert.Equal(GlobalOn, _h.Value(Global));

        Cycle(); // die Sitzung gab auf: sofort zurück, nicht erst eine weitere Startfrist später
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Process_found_after_a_long_wait_keeps_the_token_until_the_game_ends()
    {
        Setup();
        _h.Announce(_a);
        for (var i = 0; i < 200; i++)
        {
            CycleWaiting([_a]);
        }

        Cycles(400, _a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(11);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Equal(1, _h.SetOnCount);
    }

    [Fact]
    public void Switch_already_on_by_the_user_is_left_alone_and_never_turned_off()
    {
        Setup(global: GlobalOn);

        _h.Announce(_a);
        Cycles(5, _a);
        Cycles(100);

        Assert.Empty(_h.GlobalOperations);
        Assert.Null(_h.Marker);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(1, _h.LogCount("Auto HDR global ist schon an"));
        Assert.Equal(1, _h.Store.ReadsOf(Global)); // nicht bei jedem Zyklus neu gelesen
    }

    [Fact]
    public void Each_new_start_reads_the_switch_again()
    {
        Setup(global: GlobalOn);
        _h.Announce(_a);
        Cycles(5, _a);
        Cycles(15);

        _h.Store.SetString(Global, GlobalOff); // der Nutzer schaltete Auto HDR in Windows aus
        _h.Announce(_a);

        Assert.Equal(GlobalOn, _h.Value(Global)); // der neue Start über HDRpilot will Auto HDR
        Assert.Equal(GlobalOff, _h.Marker!.Original.Text);
    }

    [Fact]
    public void Missing_global_value_is_created_and_deleted_again()
    {
        Setup(global: null);

        _h.Announce(_a);
        Assert.Equal("AutoHDREnable=1;", _h.Value(Global));
        Cycles(400, _a);
        Cycles(11);

        Assert.False(_h.Store.Values.ContainsKey(Global));
        Assert.Equal("delete " + Global, _h.GlobalOperations[^1]);
    }

    [Fact]
    public void Unclean_global_value_is_never_touched()
    {
        Setup(global: null);
        _h.Store.Set(Global, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "0"));

        _h.Announce(_a);
        Cycles(5, _a);

        Assert.Empty(_h.GlobalOperations);
        Assert.Null(_h.Marker);
        Assert.Equal(1, _h.LogCount("ist kein sauberer REG_SZ"));
    }

    [Fact]
    public void Users_change_in_windows_while_held_is_kept()
    {
        Setup();
        _h.Announce(_a);
        Cycles(400, _a);
        _h.Store.SetString(Global, "AutoHDREnable=0;SwapEffectUpgradeEnable=0;"); // Nutzer schaltet in Windows um

        Cycles(11);

        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=0;", _h.Value(Global));
        Assert.Single(_h.GlobalOperations); // nur das Setzen, kein Zurückstellen
        Assert.Null(_h.Marker);
        Assert.True(_h.Logged("extern geändert"));
    }

    [Fact]
    public void Other_tokens_changed_while_held_only_the_own_token_goes_back()
    {
        Setup();
        _h.Announce(_a);
        Cycles(400, _a);
        _h.Store.SetString(Global, "AutoHDREnable=1;SwapEffectUpgradeEnable=0;"); // anderes geändert, Auto HDR noch an

        Cycles(11);

        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=0;", _h.Value(Global));
        Assert.True(_h.Logged("nur AutoHDREnable zurückgestellt"));
    }

    [Fact]
    public void Two_overlapping_games_share_the_switch_until_the_last_ends()
    {
        Setup();
        var b = _h.AddGame("2", ExeB);

        _h.Announce(_a);
        Cycles(400, _a);
        _h.Announce(b, SessionTrigger.SteamLaunchOption);
        Cycles(100, _a, b);
        Cycles(400, b); // A endet, B läuft weiter (danach lange genug für die Nachlaufzeit von A)
        Assert.Equal(GlobalOn, _h.Value(Global));
        Cycles(11);

        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Equal(1, _h.SetOnCount);
        Assert.Single(_h.GlobalOperations, o => o.EndsWith(GlobalOff, StringComparison.Ordinal));
    }

    [Fact]
    public void Switching_the_option_off_or_removing_the_game_releases_at_once()
    {
        Setup();
        _h.Announce(_a);
        Cycles(10, _a);

        _h.SetAutoHdr(_a, false);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.True(_h.Logged("(„Windows Auto HDR“ ausgeschaltet oder Spiel entfernt)"));

        _h.SetAutoHdr(_a, true); // wieder an: wirkt erst beim nächsten angekündigten Start
        Cycles(10, _a);
        Assert.Equal(GlobalOff, _h.Value(Global));

        _h.Announce(_a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        _h.RemoveGame(_a);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void Shutdown_releases()
    {
        Setup();
        _h.Announce(_a);
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
        _h.Announce(_a);
        Cycles(10, _a);
        Assert.NotNull(_h.Marker); // HDRpilot stürzt hier ab: kein Shutdown

        var service = _h.Restart();
        service.Start();
        Assert.Equal(GlobalOn, _h.Value(Global)); // erst der Wiederanlauf im ersten Zyklus stellt zurück
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
        _h.Announce(_a);
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
    public void Normal_start_adopts_no_running_game()
    {
        Setup(start: false);

        _h.Service.Start();
        _h.Service.OnRecovery([_a], _h.Current, Now); // lief schon vor HDRpilot, kein Merker
        Cycles(30, _a);

        Assert.Empty(_h.GlobalOperations);
        Assert.Equal(0, _h.Store.ReadsOf(Global));
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
    public void Marker_for_another_key_keeps_auto_hdr_off_for_this_run()
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
        _h.Announce(_a);
        Cycles(10, _a);

        Assert.Empty(_h.GlobalOperations);
        Assert.NotNull(_h.Marker);
        Assert.True(_h.Logged("gehört zu " + AutoHdrRegistryLocation.Real.KeyName, LogLevel.Warning));
    }

    [Fact]
    public void Marker_save_failure_writes_nothing_and_retries_later()
    {
        Setup();
        _h.Fs.FailWriting(_h.Paths.AutoHdrGlobalFile, new IOException("Platte voll"));

        _h.Announce(_a);
        Assert.Empty(_h.GlobalOperations);

        _h.Fs.FailWriting(_h.Paths.AutoHdrGlobalFile, null);
        CycleWaiting([_a]);
        Assert.Empty(_h.GlobalOperations); // 30 s Pause nach dem Fehler
        for (var i = 0; i < 30; i++)
        {
            CycleWaiting([_a]);
        }

        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(AutoHdrEntryStatus.Applied, _h.Marker!.Status);
    }

    [Fact]
    public void Registry_write_failure_removes_the_marker_and_retries_later()
    {
        Setup();
        _h.Store.FailWrites = new UnauthorizedAccessException("verweigert");

        _h.Announce(_a);
        Assert.Null(_h.Marker);
        Assert.Equal(GlobalOff, _h.Value(Global));

        _h.Store.FailWrites = null;
        for (var i = 0; i < 31; i++)
        {
            CycleWaiting([_a]);
        }

        Assert.Equal(GlobalOn, _h.Value(Global));
    }

    [Fact]
    public void Failed_restore_keeps_the_marker_for_the_next_try()
    {
        Setup();
        _h.Announce(_a);
        Cycles(400, _a);
        _h.Store.FailWrites = new UnauthorizedAccessException("verweigert");

        Cycles(11);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.NotNull(_h.Marker);

        _h.Store.FailWrites = null;
        Cycles(31);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
    }
}
