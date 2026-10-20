using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Tray-Eintrag „Auto HDR“ (G9.9): zeigt den globalen Auto-HDR-Schalter von Windows, gelesen beim Öffnen des Menüs, und
/// schaltet ihn als Wahl des Nutzers (nur das Token, gilt ab dem nächsten Spielstart). Registry nur als Attrappe: entweder
/// der Host direkt oder der echte <see cref="AutoHdrService"/> über einem Schlüssel im Speicher, nie die echte Registry.
/// </summary>
public sealed class AutoHdrTrayTests
{
    private const string GlobalOff = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;";
    private const string GlobalOn = "AutoHDREnable=1;SwapEffectUpgradeEnable=1;";

    [Fact]
    public void AutoHdrTray_entry_sits_right_below_hdr_now_and_shows_the_state_read_when_the_menu_opens()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var state in new[] { AutoHdrSwitchState.On, AutoHdrSwitchState.Off })
            {
                var host = new FakeTrayHost { AutoHdr = state };
                var menu = new TrayMenu { DataContext = new TrayMenuViewModel(host, TrayState.Initial, ex => throw ex) };
                WpfTestHost.DoEvents();

                var items = menu.Items.OfType<Control>().Select(AutomationProperties.GetAutomationId).ToList();
                Assert.Equal(items.IndexOf("TrayToggleHdr") + 1, items.IndexOf("TrayAutoHdr"));
                var entry = Entry(menu);
                Assert.True(entry.IsCheckable);
                Assert.True(entry.IsEnabled);
                Assert.Equal(state == AutoHdrSwitchState.On, entry.IsChecked);
                Assert.Equal(ShellStrings.TrayMenuAutoHdr, entry.Header);
                Assert.Equal(ShellStrings.TrayMenuAutoHdr, AutomationProperties.GetName(entry));

                // UI Automation (phase6-traymenu.ps1): Umschalt-Muster mit dem Stand von Windows
                var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(entry).GetPattern(PatternInterface.Toggle);
                Assert.Equal(state == AutoHdrSwitchState.On ? ToggleState.On : ToggleState.Off, toggle.ToggleState);

                // Gelesen genau einmal, beim Öffnen; geschrieben nichts
                Assert.Equal(1, host.AutoHdrReads);
                Assert.Empty(host.Calls);
                menu.DataContext = null;
            }
        });
    }

    [Fact]
    public void AutoHdrTray_toggling_switches_windows_through_the_host_and_shows_the_new_state()
    {
        WpfTestHost.Run(() =>
        {
            var host = new FakeTrayHost { AutoHdr = AutoHdrSwitchState.Off };
            var model = new TrayMenuViewModel(host, TrayState.Initial, ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.DoEvents();
            var entry = Entry(menu);
            var changed = new List<string?>();
            model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            // Wie ein Klick: das Umschalt-Muster des Menüeintrags (WPF setzt IsChecked, die Bindung schreibt zurück)
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(entry).GetPattern(PatternInterface.Toggle)).Toggle();
            WpfTestHost.DoEvents();
            Assert.Equal(["autohdr:True"], host.Calls);
            Assert.True(model.IsAutoHdrOn);
            Assert.True(entry.IsChecked);
            Assert.Contains(nameof(TrayMenuViewModel.IsAutoHdrOn), changed);

            model.IsAutoHdrOn = false;
            Assert.Equal(["autohdr:True", "autohdr:False"], host.Calls);
            Assert.False(model.IsAutoHdrOn);

            // Gegenprobe: der Stand, den es schon hat, schreibt nichts
            model.IsAutoHdrOn = false;
            Assert.Equal(2, host.Calls.Count);

            // Andere Einträge schalten Auto HDR nie mit
            model.ToggleHdrCommand.Execute(null);
            model.IsPaused = true;
            Assert.Equal(["autohdr:True", "autohdr:False", "toggle", "pause:True"], host.Calls);
            Assert.Equal(1, host.AutoHdrReads);
            menu.DataContext = null;
        });
    }

    [Fact]
    public void AutoHdrTray_unreadable_switch_disables_the_entry_explains_why_and_writes_nothing()
    {
        WpfTestHost.Run(() =>
        {
            var host = new FakeTrayHost { AutoHdr = AutoHdrSwitchState.Unknown };
            var model = new TrayMenuViewModel(host, TrayState.Initial, ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.DoEvents();
            var entry = Entry(menu);

            Assert.False(model.CanToggleAutoHdr);
            Assert.False(entry.IsEnabled);
            Assert.False(entry.IsChecked);
            Assert.Equal(ShellStrings.TrayMenuAutoHdrUnknown, entry.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(entry));

            model.IsAutoHdrOn = true;
            Assert.Empty(host.Calls);
            Assert.False(model.IsAutoHdrOn);

            // Gegenprobe: lesbar ist der Eintrag bedienbar, mit dem Hinweis „gilt ab dem nächsten Spielstart“
            var readable = new TrayMenu { DataContext = new TrayMenuViewModel(new FakeTrayHost(), TrayState.Initial, ex => throw ex) };
            WpfTestHost.DoEvents();
            Assert.True(Entry(readable).IsEnabled);
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHint, Entry(readable).ToolTip);
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHint, AutomationProperties.GetHelpText(Entry(readable)));
            menu.DataContext = null;
            readable.DataContext = null;
        });
    }

    [Fact]
    public void AutoHdrTray_held_by_hdrpilot_for_a_game_says_it_goes_off_again_afterwards()
    {
        WpfTestHost.Run(() =>
        {
            var host = new FakeTrayHost { AutoHdr = AutoHdrSwitchState.On, AutoHdrSwitchHeld = true };
            var model = new TrayMenuViewModel(host, TrayState.Initial, ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.DoEvents();
            var entry = Entry(menu);

            // Das Häkchen zeigt, was Windows hat; der Hinweis sagt, dass es nur für das Spiel gilt.
            Assert.True(entry.IsChecked);
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHeld, entry.ToolTip);
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHeld, AutomationProperties.GetHelpText(entry));

            // Umgeschaltet ist es die Wahl des Nutzers: wieder der normale Hinweis.
            model.IsAutoHdrOn = false;
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHint, model.AutoHdrHint);
            menu.DataContext = null;

            // Gegenproben: an ohne Halten (Wahl des Nutzers) und aus (gehalten wird nur ein eingeschalteter Schalter).
            Assert.Equal(ShellStrings.TrayMenuAutoHdrHint, new TrayMenuViewModel(new FakeTrayHost { AutoHdr = AutoHdrSwitchState.On }, TrayState.Initial, ex => throw ex).AutoHdrHint);
            Assert.Equal(
                ShellStrings.TrayMenuAutoHdrHint,
                new TrayMenuViewModel(new FakeTrayHost { AutoHdr = AutoHdrSwitchState.Off, AutoHdrSwitchHeld = true }, TrayState.Initial, ex => throw ex).AutoHdrHint);
        });

        Assert.Contains("danach wieder aus", ShellStrings.TrayMenuAutoHdrHeld, StringComparison.Ordinal);
        Assert.Contains("ab dem nächsten Spielstart", ShellStrings.TrayMenuAutoHdrHeld, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoHdrTray_icon_is_the_one_of_the_list_badge_and_no_other_entry_uses_it()
    {
        WpfTestHost.Run(() =>
        {
            var menu = new TrayMenu { DataContext = new TrayMenuViewModel(new FakeTrayHost(), TrayState.Initial, ex => throw ex) };
            WpfTestHost.DoEvents();
            // Symbole sind Linien-Symbole (E89) oder Glyphen; „Auto HDR“ behält die Glyphe des Listen-Kennzeichens.
            var icons = menu.Items.OfType<MenuItem>().ToDictionary(AutomationProperties.GetAutomationId, i => i.Icon switch
            {
                TextBlock t => t.Text,
                HdrPilot.App.Views.Shell.LineIcon l => "linie:" + l.Data,
                _ => "ohne",
            });

            Assert.Equal(AppGlyphs.AutoHdr, icons["TrayAutoHdr"]);
            Assert.Single(icons.Values, v => v == AppGlyphs.AutoHdr);
            Assert.NotEqual(icons["TrayToggleHdr"], icons["TrayAutoHdr"]); // nicht die Sonne von „HDR jetzt …“
            menu.DataContext = null;
        });
    }

    [Fact]
    public void AutoHdrTray_hint_says_it_applies_from_the_next_game_start_also_without_hdrpilot()
    {
        Assert.Contains("ab dem nächsten Spielstart", ShellStrings.TrayMenuAutoHdrHint, StringComparison.Ordinal);
        Assert.Contains("auch ohne HDRpilot", ShellStrings.TrayMenuAutoHdrHint, StringComparison.Ordinal);
        Assert.Equal("Auto HDR", ShellStrings.TrayMenuAutoHdr);
        Assert.Contains("nicht", ShellStrings.TrayMenuAutoHdrUnknown, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoHdrTray_failures_are_reported_not_thrown_and_leave_the_entry_disabled()
    {
        var errors = new List<Exception>();
        var throwing = new TrayMenuViewModel(new ThrowingTrayHost(), TrayState.Initial, errors.Add);
        Assert.Equal("Auto HDR lesen", Assert.Single(errors).Message);
        Assert.False(throwing.CanToggleAutoHdr);
        throwing.IsAutoHdrOn = true; // gesperrt: kein zweiter Versuch
        Assert.Single(errors);

        var host = new FakeTrayHost { AutoHdr = AutoHdrSwitchState.Off, AutoHdrSetFailure = new UnauthorizedAccessException("verweigert") };
        var model = new TrayMenuViewModel(host, TrayState.Initial, errors.Add);
        model.IsAutoHdrOn = true;
        Assert.Equal(2, errors.Count);
        Assert.IsType<UnauthorizedAccessException>(errors[1]);
        Assert.False(model.IsAutoHdrOn);
        Assert.False(model.CanToggleAutoHdr);
        Assert.Equal(ShellStrings.TrayMenuAutoHdrUnknown, model.AutoHdrHint);
    }

    [Fact]
    public void AutoHdrTray_with_the_service_switching_off_during_a_session_stays_off_and_keeps_the_other_tokens()
    {
        var rig = new ServiceRig(GlobalOff);
        rig.Announce();
        rig.Cycles(30, running: true);
        Assert.Equal(GlobalOn, rig.Value);
        Assert.True(rig.Service.HoldsGlobalSwitch);

        var model = rig.OpenMenu();
        Assert.True(model.IsAutoHdrOn); // zeigt, was Windows gerade hat: HDRpilot hält den Schalter für das Spiel
        Assert.Equal(ShellStrings.TrayMenuAutoHdrHeld, model.AutoHdrHint); // … und sagt, dass er danach wieder aus ist
        model.IsAutoHdrOn = false;

        Assert.Equal(GlobalOff, rig.Value); // nur das Token, SwapEffectUpgradeEnable bleibt
        Assert.False(rig.Service.HoldsGlobalSwitch);
        rig.Cycles(30, running: true);
        Assert.Equal(GlobalOff, rig.Value); // während der Sitzung nicht wieder an
        rig.Cycles(180, running: false);
        Assert.Equal(GlobalOff, rig.Value);
        Assert.False(rig.Fs.FileExists(rig.Paths.AutoHdrGlobalFile));
        Assert.False(rig.OpenMenu().IsAutoHdrOn);
    }

    [Fact]
    public void AutoHdrTray_with_the_service_switching_on_during_a_session_is_kept_after_it()
    {
        var rig = new ServiceRig(GlobalOff);
        rig.Announce();
        rig.Cycles(30, running: true);

        // Im Tray aus und wieder an: die Wahl des Nutzers, nach dem Spiel nicht zurückgestellt
        var model = rig.OpenMenu();
        model.IsAutoHdrOn = false;
        rig.OpenMenu().IsAutoHdrOn = true;
        Assert.Equal(GlobalOn, rig.Value);

        rig.Cycles(30, running: true);
        rig.Cycles(180, running: false);
        Assert.Equal(GlobalOn, rig.Value);
        Assert.True(rig.OpenMenu().IsAutoHdrOn);
        Assert.Equal(ShellStrings.TrayMenuAutoHdrHint, rig.OpenMenu().AutoHdrHint); // die Wahl des Nutzers, nicht gehalten

        // Gegenprobe ohne Tray: dieselbe Sitzung stellt am Ende zurück
        var plain = new ServiceRig(GlobalOff);
        plain.Announce();
        plain.Cycles(30, running: true);
        plain.Cycles(180, running: false);
        Assert.Equal(GlobalOff, plain.Value);
    }

    [Fact]
    public void AutoHdrTray_with_the_service_switched_on_before_a_start_is_never_turned_off()
    {
        var rig = new ServiceRig(global: null); // Wert fehlt wie auf einem frischen System
        var model = rig.OpenMenu();
        Assert.False(model.IsAutoHdrOn);

        model.IsAutoHdrOn = true;
        Assert.Equal("AutoHDREnable=1;", rig.Value);
        var writes = rig.Store.Operations.Count;

        rig.Announce();
        rig.Cycles(30, running: true);
        rig.Cycles(180, running: false);
        Assert.Equal("AutoHDREnable=1;", rig.Value);
        Assert.Equal(writes, rig.Store.Operations.Count);
        Assert.False(rig.Fs.FileExists(rig.Paths.AutoHdrGlobalFile));
    }

    private static MenuItem Entry(TrayMenu menu) =>
        menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "TrayAutoHdr");

    /// <summary>
    /// Echter <see cref="AutoHdrService"/> über einem Schlüssel im Speicher, dazu ein Tray-Host, der wie der Adapter der App
    /// (AppServices.Shell.cs) nur durchreicht. Ein Spiel mit „Windows Auto HDR“; Sitzungsende nach kurzer Laufzeit mit
    /// Neustart-Puffer.
    /// </summary>
    private sealed class ServiceRig
    {
        public ServiceRig(string? global)
        {
            if (global is not null)
            {
                Store.SetString(AutoHdrRegistryLocation.GlobalValueName, global);
            }

            Config = new ConfigService(new ConfigStore(Fs, Paths.ConfigFile, Clock), Clock);
            var game = new GameConfig
            {
                Source = GameSource.Steam,
                SourceId = "1",
                Name = "Spiel 1",
                InstallDir = @"D:\Games\Spiel 1",
                KnownExecutables = [@"D:\Games\Spiel 1\Spiel1.exe"],
                AutoHdr = true,
                LaunchCommand = "steam://rungameid/1",
            };
            Config.Update(c =>
            {
                c.Settings.GracePeriodSeconds = 10;
                c.Games.Add(game);
            });
            Key = game.Key;
            Service = new AutoHdrService(
                Config, Store, AutoHdrRegistryLocation.ForTest("Tray"), Fs, Paths, new FakeEnvironment { WindowsBuild = 26300 }, Clock, new ListLogger<AutoHdrService>());
            Config.Changed += (_, _) => Service.OnConfigChanged();
            Service.Start();
            Service.OnRecovery([], Config.Current, Clock.UtcNow);
        }

        public InMemoryFileSystem Fs { get; } = new();

        public FakeClock Clock { get; } = new();

        public FakeGpuPreferenceStore Store { get; } = new();

        public AppPaths Paths { get; } = AppPaths.InDirectory(@"C:\Test\HDRpilot-Tray");

        public ConfigService Config { get; }

        public AutoHdrService Service { get; }

        public string Key { get; }

        public string? Value => Store.GetString(AutoHdrRegistryLocation.GlobalValueName);

        /// <summary>Menü öffnen: ein neues Modell liest den Schalter, wie beim Rechtsklick auf das Symbol.</summary>
        public TrayMenuViewModel OpenMenu() => new(new PassThroughHost(Service), TrayState.Initial, ex => throw ex);

        public void Announce() => Service.OnStartAnnounced(Config.Current.FindGame(Key)!, SessionTrigger.LaunchedViaApp, Clock.UtcNow);

        public void Cycles(int seconds, bool running)
        {
            for (var i = 0; i < seconds; i++)
            {
                Service.OnCycle(running ? [Key] : [], [], Config.Current, Clock.UtcNow);
                Clock.Advance(TimeSpan.FromSeconds(1));
            }
        }
    }

    private sealed class PassThroughHost(IAutoHdrService autoHdr) : ITrayHost
    {
        public Dispatcher Dispatcher => WpfTestHost.Dispatcher;

        public TrayState QueryTrayState() => TrayState.Initial;

        public Task ToggleHdrNowAsync() => Task.CompletedTask;

        public AutoHdrSwitchState ReadAutoHdrSwitch() => autoHdr.ReadGlobalSwitch();

        public bool AutoHdrSwitchHeld => autoHdr.HoldsGlobalSwitch;

        public AutoHdrSwitchState SetAutoHdrSwitch(bool on) => autoHdr.SetGlobalSwitchByUser(on);

        public void SetPaused(bool paused)
        {
        }

        public void ShowMainWindow()
        {
        }

        public void ShowSettingsWindow()
        {
        }

        public void OpenLogFolder()
        {
        }

        public Task ExitAsync() => Task.CompletedTask;
    }
}
