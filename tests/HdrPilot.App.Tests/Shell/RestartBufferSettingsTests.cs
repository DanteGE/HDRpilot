using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Einstellung „Neustart-Puffer“ (E85, Wunsch des Nutzers nach dem Helldivers-2-Test): Minuten und Sekunden, 0:00 = aus,
/// höchstens 30:00, Standard 1:00; gespeichert als restartBufferSeconds und ab dem nächsten Spielende wirksam.
/// </summary>
public sealed class RestartBufferSettingsTests
{
    private const string GameDir = @"D:\Steam-Bibliothek\steamapps\common\Sternenwacht 2";
    private const string GameExe = GameDir + @"\bin\sternenwacht2.exe";

    private readonly InMemoryFileSystem _fs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void RestartBuffer_not_set_in_the_config_loads_as_one_minute()
    {
        var (vm, ctx) = Create();

        Assert.Null(ctx.Config.Current.Settings.RestartBufferSeconds);
        Assert.Equal(60, SettingsViewModel.RestartBufferDefaultSeconds);
        Assert.Equal(1, vm.RestartBufferMinutes);
        Assert.Equal(0, vm.RestartBufferSeconds);
        Assert.Equal(60, vm.RestartBufferTotalSeconds);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitMinutesSeconds, 1, 0), vm.RestartBufferText);
        Assert.Equal("1:00 min", vm.RestartBufferText);
        Assert.False(vm.HasChanges);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(45, 0, 45)]
    [InlineData(125, 2, 5)]
    [InlineData(1800, 30, 0)]
    public void RestartBuffer_from_the_config_loads_as_minutes_and_seconds(int stored, int minutes, int seconds)
    {
        var (vm, _) = Create(c => c.Settings.RestartBufferSeconds = stored);

        Assert.Equal(minutes, vm.RestartBufferMinutes);
        Assert.Equal(seconds, vm.RestartBufferSeconds);
        Assert.Equal(stored, vm.RestartBufferTotalSeconds);
        Assert.False(vm.HasChanges);
    }

    [Theory]
    [InlineData(1801, 30, 0)]
    [InlineData(99_999, 30, 0)]
    [InlineData(-5, 0, 0)]
    public void RestartBuffer_out_of_range_in_the_config_is_clamped_on_load(int stored, int minutes, int seconds)
    {
        // Von Hand bearbeitete config.json: wie die Sitzungslogik höchstens 30:00, negative Werte gelten als aus.
        var (vm, _) = Create(c => c.Settings.RestartBufferSeconds = stored);

        Assert.Equal(minutes, vm.RestartBufferMinutes);
        Assert.Equal(seconds, vm.RestartBufferSeconds);
    }

    [Fact]
    public void RestartBuffer_minutes_and_seconds_are_saved_as_seconds_and_survive_a_restart()
    {
        var (vm, ctx) = Create();

        vm.RestartBufferMinutes = 2;
        vm.RestartBufferSeconds = 5;
        Assert.Equal(125, vm.RestartBufferTotalSeconds);
        Assert.Equal("2:05 min", vm.RestartBufferText);
        Assert.True(vm.HasChanges);
        Assert.True(vm.Save());

        Assert.False(vm.HasChanges);
        Assert.Equal(125, ctx.Config.Current.Settings.RestartBufferSeconds); // das liest die Sitzungslogik je Spielende
        Assert.Contains("\"restartBufferSeconds\": 125", _fs.ReadAllText(ShellTestFactory.ConfigPath), StringComparison.Ordinal);
        var (again, _) = Create();
        Assert.Equal(2, again.RestartBufferMinutes);
        Assert.Equal(5, again.RestartBufferSeconds);
    }

    [Fact]
    public void RestartBuffer_zero_is_saved_as_off()
    {
        var (vm, ctx) = Create();

        vm.RestartBufferMinutes = 0;
        Assert.Equal(ShellStrings.RestartBufferOff, vm.RestartBufferText);
        Assert.Equal("aus", vm.RestartBufferText);
        Assert.True(vm.Save());

        Assert.Equal(0, ctx.Config.Current.Settings.RestartBufferSeconds);
        Assert.Equal(0, ShellTestFactory.NewConfig(_fs).Current.Settings.RestartBufferSeconds);
    }

    [Fact]
    public void RestartBuffer_not_set_stays_unset_when_other_settings_are_saved()
    {
        // Ohne Eingriff bleibt restartBufferSeconds ungeschrieben: ein späterer neuer Standard gilt dann auch (G7.8).
        var (vm, ctx) = Create();

        vm.GracePeriodSeconds = 20;
        vm.RestartBufferMinutes = 3;
        vm.RestartBufferMinutes = 1; // geändert und zurück: wieder der Standard
        Assert.True(vm.Save());

        Assert.Null(ctx.Config.Current.Settings.RestartBufferSeconds);
        Assert.DoesNotContain("restartBufferSeconds", _fs.ReadAllText(ShellTestFactory.ConfigPath), StringComparison.Ordinal);
        Assert.Equal(20, ctx.Config.Current.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void RestartBuffer_stored_explicitly_stays_explicit_even_at_the_default()
    {
        var (vm, ctx) = Create(c => c.Settings.RestartBufferSeconds = 60);

        vm.GracePeriodSeconds = 20;
        Assert.True(vm.Save());

        Assert.Equal(60, ctx.Config.Current.Settings.RestartBufferSeconds);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(31, 30)]
    [InlineData(999, 30)]
    public void RestartBuffer_minutes_are_limited_to_0_to_30(int input, int expected)
    {
        var (vm, _) = Create(c => c.Settings.RestartBufferSeconds = 0);

        vm.RestartBufferMinutes = input;

        Assert.Equal(expected, vm.RestartBufferMinutes);
        Assert.Equal(0, vm.RestartBufferSeconds);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(60, 59)]
    [InlineData(500, 59)]
    public void RestartBuffer_seconds_are_limited_to_0_to_59(int input, int expected)
    {
        var (vm, _) = Create(c => c.Settings.RestartBufferSeconds = 120);

        vm.RestartBufferSeconds = input;

        Assert.Equal(2, vm.RestartBufferMinutes); // ein Überlauf der Sekunden zählt nicht als Minute
        Assert.Equal(expected, vm.RestartBufferSeconds);
    }

    [Fact]
    public void RestartBuffer_never_exceeds_30_minutes_and_resets_the_seconds_slider()
    {
        var (vm, _) = Create(c => c.Settings.RestartBufferSeconds = 45);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RestartBufferMinutes = 30; // 30:45 gibt es nicht: 30:00
        Assert.Equal((30, 0, 1800), (vm.RestartBufferMinutes, vm.RestartBufferSeconds, vm.RestartBufferTotalSeconds));
        Assert.Contains(nameof(SettingsViewModel.RestartBufferSeconds), changed);

        // 30 min + 5 s: bleibt 30:00. Der Sekunden-Regler steht schon auf 5 und muss zurück auf 0, obwohl sich der
        // Wert nicht ändert; deshalb wird er trotzdem gemeldet.
        changed.Clear();
        vm.RestartBufferSeconds = 5;
        Assert.Equal((30, 0, 1800), (vm.RestartBufferMinutes, vm.RestartBufferSeconds, vm.RestartBufferTotalSeconds));
        Assert.Contains(nameof(SettingsViewModel.RestartBufferSeconds), changed);
        Assert.DoesNotContain(nameof(SettingsViewModel.HasChanges), changed);

        vm.RestartBufferMinutes = 29;
        vm.RestartBufferSeconds = 59;
        Assert.Equal(1799, vm.RestartBufferTotalSeconds);
    }

    [Fact]
    public void RestartBuffer_change_marks_the_settings_as_changed_and_reverting_clears_it()
    {
        var (vm, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RestartBufferSeconds = 30;
        Assert.True(vm.HasChanges);
        Assert.Equal(ShellStrings.UnsavedChanges, vm.ChangeStatus);
        Assert.Contains(nameof(SettingsViewModel.RestartBufferText), changed);
        Assert.Contains(nameof(SettingsViewModel.RestartBufferTotalSeconds), changed);

        vm.RestartBufferSeconds = 0;
        Assert.False(vm.HasChanges);

        vm.RestartBufferMinutes = 0;
        Assert.True(vm.HasChanges);
        vm.RestartBufferMinutes = 1;
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void RestartBuffer_cancel_discards_the_change()
    {
        var (vm, ctx) = Create(c => c.Settings.RestartBufferSeconds = 90);
        var before = _fs.ReadAllText(ShellTestFactory.ConfigPath);

        vm.RestartBufferMinutes = 10;
        vm.CancelCommand.Execute(null);

        Assert.Equal(before, _fs.ReadAllText(ShellTestFactory.ConfigPath));
        Assert.Equal(90, ctx.Config.Current.Settings.RestartBufferSeconds);
        Assert.Empty(ctx.SavedCalls);
    }

    [Fact]
    public async Task RestartBuffer_saved_in_the_settings_applies_to_the_next_ending_session_without_restart()
    {
        // Wie in der App teilen sich Einstellungen und Sitzungslogik denselben ConfigService (keine Kopie, kein Neustart).
        var config = ShellTestFactory.NewConfig(_fs, c => c.Games.Add(new GameConfig
        {
            Source = GameSource.Steam, SourceId = "9990110", Name = "STERNENWACHT 2", InstallDir = GameDir, HdrAuto = true,
        }));
        var clock = new FakeClock();
        var hdr = FakeHdrController.WithPrimary();
        var processes = new ProcessScript();
        var sessions = new SessionManager(
            hdr, config, new InMemorySessionStateStore(), new FakeLauncher(), clock, (_, _) => false, [], null,
            new SessionManagerOptions { FullscreenAppRunning = () => false, NotificationsSuppressed = () => false, ActivityProbe = new FakeActivityProbe() });

        async Task Poll(int times = 1)
        {
            for (var i = 0; i < times; i++)
            {
                await sessions.UpdateAsync(processes.Next(), Ct);
                clock.Advance(TimeSpan.FromSeconds(1));
            }
        }

        // Kurz gespielt (wie im Helldivers-2-Test 77 s), dann beendet: Beginn des Puffers.
        async Task<DateTimeOffset> PlayBriefly()
        {
            var pid = processes.StartExe(GameExe);
            await Poll(77);
            processes.Stop(pid);
            var stoppedAt = clock.UtcNow;
            await Poll();
            return stoppedAt;
        }

        void SaveBuffer(int minutes, int seconds)
        {
            using var vm = new SettingsViewModel(new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config)), new FakeFolderPicker());
            vm.RestartBufferMinutes = minutes;
            vm.RestartBufferSeconds = seconds;
            Assert.True(vm.Save());
        }

        await Poll();
        var first = await PlayBriefly();
        Assert.Equal(first + TimeSpan.FromMinutes(1), Assert.Single(sessions.Sessions).BufferUntil); // Standard 1:00
        await Poll(60);
        Assert.Empty(sessions.Sessions);
        Assert.False(hdr[FakeHdrController.Primary].HdrOn);

        SaveBuffer(0, 45);
        var second = await PlayBriefly();
        Assert.Equal(second + TimeSpan.FromSeconds(45), Assert.Single(sessions.Sessions).BufferUntil);
        await Poll(44);
        Assert.True(hdr[FakeHdrController.Primary].HdrOn);
        await Poll();
        Assert.False(hdr[FakeHdrController.Primary].HdrOn);

        SaveBuffer(0, 0);
        await PlayBriefly();
        Assert.Null(Assert.Single(sessions.Sessions).BufferUntil); // aus: nur die Nachlaufzeit (10 s)
        await Poll(10);
        Assert.Empty(sessions.Sessions);
        Assert.False(hdr[FakeHdrController.Primary].HdrOn);
    }

    [Fact]
    public void RestartBuffer_sliders_have_names_limits_and_snap_back_at_30_minutes()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            var model = new SettingsViewModel(ctx, new FakeFolderPicker());
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(1080, 1400));
            WpfTestHost.DoEvents();

            var sliders = Logical(window).OfType<Slider>().ToDictionary(AutomationProperties.GetAutomationId);
            var minutes = sliders["RestartBufferMinutes"];
            var seconds = sliders["RestartBufferSeconds"];
            Assert.Equal(ShellStrings.RestartBufferMinutesName, AutomationProperties.GetName(minutes));
            Assert.Equal(ShellStrings.RestartBufferSecondsName, AutomationProperties.GetName(seconds));
            Assert.Equal(ShellStrings.RestartBufferHint, AutomationProperties.GetHelpText(minutes));
            Assert.Equal((0d, (double)SettingsViewModel.RestartBufferMinutesMax), (minutes.Minimum, minutes.Maximum));
            Assert.Equal((0d, (double)SettingsViewModel.RestartBufferSecondsMax), (seconds.Minimum, seconds.Maximum));
            Assert.Equal((1d, 0d), (minutes.Value, seconds.Value));

            // Neustart-Puffer steht direkt nach der Nachlaufzeit (vor der Startfrist).
            var order = Logical(window).OfType<Slider>().Select(AutomationProperties.GetAutomationId).ToList();
            Assert.Equal(["PollInterval", "GracePeriod", "RestartBufferMinutes", "RestartBufferSeconds", "LaunchTimeout"], order);
            var labels = Logical(window).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(ShellStrings.RestartBufferLabel, labels);
            Assert.Contains(ShellStrings.RestartBufferMinutesLabel, labels);
            Assert.Contains(ShellStrings.RestartBufferSecondsLabel, labels);
            Assert.Contains(ShellStrings.RestartBufferHint, labels);

            seconds.Value = 45;
            minutes.Value = 0;
            Assert.Equal(45, model.RestartBufferTotalSeconds);
            Assert.Contains("0:45 min", Logical(window).OfType<TextBlock>().Select(t => t.Text));

            // 30:45 und 30:05 gibt es nicht: der Sekunden-Regler springt auf 0 zurück.
            minutes.Value = 30;
            WpfTestHost.DoEvents();
            Assert.Equal(0, seconds.Value);
            seconds.Value = 5;
            WpfTestHost.DoEvents();
            Assert.Equal(0, seconds.Value);
            Assert.Equal(1800, model.RestartBufferTotalSeconds);
            window.Close();
        });
    }

    private (SettingsViewModel Vm, FakeShellContext Context) Create(Action<AppConfig>? setup = null)
    {
        var config = ShellTestFactory.NewConfig(_fs, setup);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        return (new SettingsViewModel(ctx, new FakeFolderPicker()), ctx);
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in Logical(child))
            {
                yield return descendant;
            }
        }
    }
}
