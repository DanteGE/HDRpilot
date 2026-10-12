using HdrPilot.Core.AutoHdr;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Lesen und Umschalten des globalen Auto-HDR-Schalters durch den Nutzer (Grundlage für den Tray-Eintrag „Auto HDR“,
/// G9.9): nur das Token, andere Tokens bleiben; eine Änderung des Nutzers ist seine Wahl und wird nach einer Sitzung nicht
/// zurückgestellt und während ihr nicht überschrieben; erst ein neuer Start über HDRpilot liest den Schalter neu und stellt
/// danach diese Wahl wieder her. Registry und Dateisystem nur als Attrappe.
/// </summary>
public sealed class AutoHdrGlobalUserSwitchTests
{
    private readonly AutoHdrHarness _h = new();
    private string _a = string.Empty;

    private void Setup(string? global = GlobalOff)
    {
        if (global is not null)
        {
            _h.Store.SetString(Global, global);
        }

        _a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Service.OnRecovery([], _h.Current, _h.Clock.UtcNow);
    }

    private void Cycles(int seconds, params string[] running)
    {
        for (var i = 0; i < seconds; i++)
        {
            _h.Service.OnCycle(running, [], _h.Current, _h.Clock.UtcNow);
            _h.Clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Theory]
    [InlineData(GlobalOff, AutoHdrSwitchState.Off)]
    [InlineData(GlobalOn, AutoHdrSwitchState.On)]
    [InlineData("SwapEffectUpgradeEnable=1;", AutoHdrSwitchState.Off)]
    [InlineData(null, AutoHdrSwitchState.Off)]
    [InlineData("AutoHDREnable=2;", AutoHdrSwitchState.Off)]
    [InlineData("0", AutoHdrSwitchState.Unknown)]
    public void Read_reports_the_current_state_without_writing(string? global, AutoHdrSwitchState expected)
    {
        Setup(global);

        Assert.Equal(expected, _h.Service.ReadGlobalSwitch());
        Assert.Empty(_h.Store.Operations);
    }

    [Fact]
    public void Unreadable_value_reads_as_unknown()
    {
        Setup();
        _h.Store.FailReads = new UnauthorizedAccessException("verweigert");

        Assert.Equal(AutoHdrSwitchState.Unknown, _h.Service.ReadGlobalSwitch());
        Assert.Equal(AutoHdrSwitchState.Unknown, _h.Service.SetGlobalSwitchByUser(on: true));
        Assert.Empty(_h.Store.Operations);
    }

    [Fact]
    public void User_switch_sets_only_the_token_and_keeps_the_other_tokens()
    {
        Setup();

        Assert.Equal(AutoHdrSwitchState.On, _h.Service.SetGlobalSwitchByUser(on: true));
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(AutoHdrSwitchState.On, _h.Service.ReadGlobalSwitch());

        Assert.Equal(AutoHdrSwitchState.Off, _h.Service.SetGlobalSwitchByUser(on: false));
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker); // die Wahl des Nutzers merkt HDRpilot nicht zum Zurückstellen
        Assert.True(_h.Logged("vom Nutzer eingeschaltet"));

        // Gegenprobe: schon im gewünschten Stand: nichts geschrieben.
        var writes = _h.Store.Operations.Count;
        Assert.Equal(AutoHdrSwitchState.Off, _h.Service.SetGlobalSwitchByUser(on: false));
        Assert.Equal(writes, _h.Store.Operations.Count);
    }

    [Fact]
    public void Missing_value_is_created_when_switched_on_and_stays_missing_when_switched_off()
    {
        Setup(global: null);

        Assert.Equal(AutoHdrSwitchState.Off, _h.Service.SetGlobalSwitchByUser(on: false));
        Assert.Empty(_h.Store.Operations);

        Assert.Equal(AutoHdrSwitchState.On, _h.Service.SetGlobalSwitchByUser(on: true));
        Assert.Equal("AutoHDREnable=1;", _h.Value(Global));
    }

    [Fact]
    public void Unclean_value_is_not_switched()
    {
        Setup(global: null);
        _h.Store.Set(Global, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1"));

        Assert.Equal(AutoHdrSwitchState.Unknown, _h.Service.SetGlobalSwitchByUser(on: true));
        Assert.Empty(_h.Store.Operations);
    }

    [Fact]
    public void Switched_on_by_the_user_while_held_stays_on_after_the_session()
    {
        Setup();
        _h.Announce(_a);
        Cycles(30, _a);
        Assert.True(_h.Service.HoldsGlobalSwitch);

        Assert.Equal(AutoHdrSwitchState.On, _h.Service.SetGlobalSwitchByUser(on: true)); // im Tray bestätigt: soll an bleiben

        Assert.False(_h.Service.HoldsGlobalSwitch);
        Assert.Null(_h.Marker);
        Cycles(120);
        Assert.Equal(GlobalOn, _h.Value(Global)); // nach dem Spiel nicht zurückgestellt
        Assert.Equal(1, _h.SetOnCount);
        Assert.True(_h.Logged("HDRpilot stellt nach dem Spiel nichts zurück"));
    }

    [Fact]
    public void Switched_off_by_the_user_while_held_is_not_set_again_during_the_session()
    {
        Setup();
        _h.Announce(_a);
        Cycles(30, _a);

        Assert.Equal(AutoHdrSwitchState.Off, _h.Service.SetGlobalSwitchByUser(on: false));
        Cycles(30, _a);
        Assert.Equal(GlobalOff, _h.Value(Global)); // während der laufenden Sitzung nicht wieder an
        Cycles(120);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);

        // Gegenprobe: ein neuer Start über HDRpilot setzt den Schalter wieder für sich (die Option des Spiels ist an).
        _h.Announce(_a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(GlobalOff, _h.Marker!.Original.Text);
    }

    [Fact]
    public void Switched_off_by_the_user_while_held_a_new_start_sets_it_for_its_game_and_restores_the_choice()
    {
        Setup();
        var b = _h.AddGame("2", ExeB);
        _h.Announce(_a);
        Cycles(30, _a);
        Assert.Equal(AutoHdrSwitchState.Off, _h.Service.SetGlobalSwitchByUser(on: false));
        Cycles(10, _a);
        Assert.Equal(GlobalOff, _h.Value(Global)); // für die laufende Sitzung nicht wieder an

        // Ein neuer Start über HDRpilot liest den Schalter neu und setzt ihn für sein Spiel (die Option ist an) …
        _h.Announce(b);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(GlobalOff, _h.Marker!.Original.Text);
        Cycles(400, _a, b);

        // … und stellt danach die Wahl des Nutzers wieder her.
        Cycles(15);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
    }

    [Fact]
    public void Changed_in_windows_while_held_is_the_users_choice_and_a_new_start_keeps_it_afterwards()
    {
        const string Users = "AutoHDREnable=0;SwapEffectUpgradeEnable=0;";
        Setup();
        var b = _h.AddGame("2", ExeB);
        _h.Announce(_a);
        Cycles(30, _a);
        _h.Store.SetString(Global, Users); // der Nutzer schaltet in Windows Auto HDR und die Fensteroptimierung aus

        _h.Announce(b);

        // Neu gelesen: B bekommt Auto HDR, gemerkt wird der Stand des Nutzers, nicht mehr der von vor A.
        Assert.Equal("AutoHDREnable=1;SwapEffectUpgradeEnable=0;", _h.Value(Global));
        Assert.True(_h.Service.HoldsGlobalSwitch);
        Assert.Equal(Users, _h.Marker!.Original.Text);
        Assert.True(_h.Logged("extern geändert, während HDRpilot ihn hielt"));
        Cycles(400, _a, b);
        Cycles(15);
        Assert.Equal(Users, _h.Value(Global));
        Assert.Null(_h.Marker);
    }

    [Fact]
    public void Other_tokens_changed_while_held_keep_the_hold_at_a_new_start()
    {
        Setup();
        var b = _h.AddGame("2", ExeB);
        _h.Announce(_a);
        Cycles(30, _a);
        _h.Store.SetString(Global, "AutoHDREnable=1;SwapEffectUpgradeEnable=0;"); // Auto HDR noch an, anderes geändert (R2)
        var writes = _h.GlobalOperations.Count;

        _h.Announce(b);

        Assert.Equal(writes, _h.GlobalOperations.Count); // nichts geschrieben, weiter gehalten
        Assert.Equal(GlobalOff, _h.Marker!.Original.Text);
        Cycles(400, _a, b);
        Cycles(15);
        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=0;", _h.Value(Global)); // nur das Token zurück
    }

    [Fact]
    public void Users_choice_with_a_locked_marker_is_never_restored_after_hdrpilot_ends()
    {
        Setup();
        _h.Announce(_a);
        Cycles(30, _a);
        var held = _h.Marker!;
        _h.LockMarker(true); // ein Virenscanner sperrt die Datei: Löschen und Ersetzen scheitern

        Assert.Equal(AutoHdrSwitchState.On, _h.Service.SetGlobalSwitchByUser(on: true)); // soll an bleiben

        Assert.False(_h.Service.HoldsGlobalSwitch);
        Assert.Equal(AutoHdrEntryStatus.Applied, _h.Marker!.Status); // der Merker selbst ist unverändert …
        Assert.Equal(held.SetAt, _h.Done!.SetAt); // … das Kennzeichen „erledigt“ steht daneben
        Assert.Equal(held.Written, _h.Done.Written);

        // Die Sperre hält länger als HDRpilot; der nächste Start sieht den Merker, stellt aber nichts zurück.
        Cycles(10, _a);
        _h.Service.Shutdown();
        var service = _h.Restart();
        service.Start();
        service.OnRecovery([], _h.Current, _h.Clock.UtcNow);
        Cycles(31);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(1, _h.SetOnCount);
        Assert.True(_h.Logged("ist erledigt"));
        Assert.NotNull(_h.Marker);

        // Gelöscht werden beide, sobald es wieder geht.
        _h.LockMarker(false);
        Cycles(31);
        Assert.Null(_h.Marker);
        Assert.Null(_h.Done);
        Assert.Equal(GlobalOn, _h.Value(Global));
    }

    [Fact]
    public void Locked_marker_is_deleted_later_with_its_done_mark()
    {
        Setup();
        _h.Announce(_a);
        Cycles(30, _a);
        _h.LockMarker(true);
        _h.Service.SetGlobalSwitchByUser(on: false);
        Assert.NotNull(_h.Done);
        Cycles(31); // Spiel zu Ende; der Löschversuch nach 30 s scheitert noch
        Assert.NotNull(_h.Marker);
        Assert.NotNull(_h.Done);

        _h.LockMarker(false);
        Cycles(31);

        Assert.Null(_h.Marker);
        Assert.Null(_h.Done);
        Assert.Equal(GlobalOff, _h.Value(Global));
    }

    [Fact]
    public void New_start_while_the_marker_is_locked_sets_nothing_and_a_later_one_holds_with_its_own_marker()
    {
        var (first, second) = LockedMarkerThenNewStart();

        // Der neue Merker hat denselben Text, aber einen anderen Zeitpunkt: das alte Kennzeichen gilt ihm nicht.
        Assert.Equal(first.Written, second.Written);
        Assert.NotEqual(first.SetAt, second.SetAt);
        Assert.Equal(first.SetAt, _h.Done!.SetAt);

        // Der fällige Löschversuch löscht nur das alte Kennzeichen, nie den Merker des neuen Setzens.
        Cycles(5, _a);
        Assert.True(_h.Service.HoldsGlobalSwitch);
        Assert.Equal(second.SetAt, _h.Marker!.SetAt);
        Assert.Equal(AutoHdrEntryStatus.Applied, _h.Marker.Status);
        Assert.Null(_h.Done);

        Cycles(400, _a); // länger als ein kurzer Lauf: danach nur die Nachlaufzeit
        Cycles(15);
        Assert.Equal(GlobalOff, _h.Value(Global));
        Assert.Null(_h.Marker);
    }

    [Fact]
    public void Old_done_mark_does_not_protect_a_new_marker_after_a_crash()
    {
        LockedMarkerThenNewStart();
        Assert.NotNull(_h.Done); // HDRpilot stürzt ab, bevor das alte Kennzeichen gelöscht ist

        var service = _h.Restart();
        service.Start();
        service.OnRecovery([], _h.Current, _h.Clock.UtcNow);

        Assert.Equal(GlobalOff, _h.Value(Global)); // der neue Merker wird zurückgestellt
        Assert.Null(_h.Marker);
        Assert.Null(_h.Done);
        Assert.True(_h.Logged("(Wiederanlauf nach Absturz)"));
    }

    /// <summary>
    /// Der Nutzer schaltet aus, während der Merker gesperrt ist (Kennzeichen „erledigt“ daneben); ein neuer Start scheitert an
    /// der Sperre (nie ohne Merker gesetzt); nach der Sperre setzt der nächste Start mit eigenem Merker, bevor der
    /// Löschversuch fällig ist. Liefert den alten und den neuen Merker.
    /// </summary>
    private (AutoHdrGlobalMarker First, AutoHdrGlobalMarker Second) LockedMarkerThenNewStart()
    {
        Setup();
        _h.Announce(_a);
        Cycles(30, _a);
        var first = _h.Marker!;
        _h.LockMarker(true);
        _h.Service.SetGlobalSwitchByUser(on: false);
        Cycles(10); // Spiel zu Ende

        _h.Announce(_a);
        Assert.Equal(GlobalOff, _h.Value(Global)); // neuer Merker nicht zu speichern: nichts gesetzt
        Assert.False(_h.Service.HoldsGlobalSwitch);
        Assert.Equal(first.SetAt, _h.Marker!.SetAt);

        _h.LockMarker(false);
        _h.Clock.Advance(AutoHdrGlobalSwitch.RetryDelay);
        _h.Announce(_a);
        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.True(_h.Service.HoldsGlobalSwitch);
        return (first, _h.Marker!);
    }

    [Fact]
    public void Switched_on_by_the_user_before_a_start_is_never_turned_off()
    {
        Setup();
        Assert.Equal(AutoHdrSwitchState.On, _h.Service.SetGlobalSwitchByUser(on: true));
        var writes = _h.Store.Operations.Count;

        _h.Announce(_a);
        Cycles(30, _a);
        Cycles(120);

        Assert.Equal(GlobalOn, _h.Value(Global));
        Assert.Equal(writes, _h.Store.Operations.Count);
        Assert.Null(_h.Marker);
    }
}
