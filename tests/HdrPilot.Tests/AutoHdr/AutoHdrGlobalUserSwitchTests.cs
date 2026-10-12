using HdrPilot.Core.AutoHdr;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Lesen und Umschalten des globalen Auto-HDR-Schalters durch den Nutzer (Grundlage für den Tray-Eintrag „Auto HDR“,
/// G9.9): nur das Token, andere Tokens bleiben; eine Änderung des Nutzers ist seine Wahl und wird nach einer Sitzung nicht
/// zurückgestellt und während ihr nicht überschrieben. Registry nur als Attrappe.
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
