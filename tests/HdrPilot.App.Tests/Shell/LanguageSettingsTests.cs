using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Sprache in den Einstellungen (E95): Auswahl „Sprache · Language“ in „Start und Hinweise“, übernommen mit „Speichern“,
/// wirksam nach einem Neustart; „Jetzt neu starten“ speichert zuerst und startet dann neu.
/// </summary>
public sealed class LanguageSettingsTests
{
    private const string Exe = @"D:\Programme\HDRpilot\HDRpilot.exe";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();

    [Fact]
    public void Choice_offers_both_languages_named_in_their_own_language()
    {
        var (vm, _) = Create();

        Assert.Equal(["de", "en"], vm.Languages.Select(l => l.Value));
        Assert.Equal(["Deutsch", "English"], vm.Languages.Select(l => l.Label));
        using (TestCulture.Use("en-US"))
        {
            // Die Namen sind Eigenbezeichnungen und stehen in beiden Sprachen gleich.
            Assert.Equal("Deutsch", ShellStrings.LanguageNameGerman);
            Assert.Equal("English", ShellStrings.LanguageNameEnglish);
        }
    }

    [Fact]
    public void Stored_language_is_selected_and_matches_the_displayed_one()
    {
        var (vm, _) = Create(c => c.Settings.Language = "de");

        Assert.Equal("de", vm.Language);
        Assert.Equal("de", vm.DisplayedLanguage); // Tests laufen auf Deutsch
        Assert.False(vm.LanguageNeedsRestart);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Choosing_english_is_a_change_that_needs_a_restart_and_is_saved()
    {
        var (vm, ctx) = Create();

        vm.Language = "en";

        Assert.True(vm.HasChanges);
        Assert.True(vm.LanguageNeedsRestart);
        Assert.True(vm.Save());
        Assert.Equal("en", ctx.Config.Current.Settings.Language);
        Assert.Equal(0, ctx.RestartCalls); // „Speichern“ startet nie neu

        vm.Language = "de";
        Assert.False(vm.LanguageNeedsRestart);
    }

    [Fact]
    public void Unknown_values_are_ignored()
    {
        var (vm, _) = Create();

        vm.Language = "fr";
        vm.Language = string.Empty;

        Assert.Equal("de", vm.Language);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Saving_other_settings_keeps_an_old_config_without_language()
    {
        // Ältere config.json ohne Feld: Speichern anderer Einstellungen schreibt keine Sprache dazu.
        var (vm, ctx) = Create(c => c.Settings.Language = null);
        Assert.Equal("de", vm.Language);

        vm.GracePeriodSeconds = 25;
        Assert.True(vm.Save());

        Assert.Null(ctx.Config.Current.Settings.Language);
        Assert.DoesNotContain("\"language\"", _fs.ReadAllText(ShellTestFactory.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_stored_value_survives_saving_other_settings()
    {
        var (vm, ctx) = Create(c => c.Settings.Language = "fr");
        Assert.Equal("de", vm.Language);

        vm.GracePeriodSeconds = 25;
        Assert.True(vm.Save());

        Assert.Equal("fr", ctx.Config.Current.Settings.Language);
    }

    [Fact]
    public void Cancel_discards_the_choice()
    {
        var (vm, ctx) = Create();
        vm.Language = "en";

        vm.Cancel();

        Assert.Equal("de", ctx.Config.Current.Settings.Language);
    }

    [Fact]
    public async Task Restart_now_saves_everything_first_then_restarts()
    {
        var (vm, ctx) = Create();
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        vm.Language = "en";
        vm.GracePeriodSeconds = 25;

        vm.RestartNowCommand.Execute(null);
        await WaitFor(vm.RestartNowCommand);

        Assert.Equal(1, ctx.RestartCalls);
        Assert.Equal("en", ctx.LanguageAtRestart);
        Assert.Equal(25, ctx.Config.Current.Settings.GracePeriodSeconds);
        Assert.False(vm.HasChanges);
        Assert.False(vm.HasError);
        Assert.Equal(0, closed); // das Fenster schließt der Neustart, nicht das Speichern
    }

    [Fact]
    public async Task Restart_now_does_not_restart_when_saving_fails()
    {
        var (vm, ctx) = Create();
        vm.Language = "en";
        vm.HelperPatternsText = @"C:\Ordner\absturz.exe"; // ungültig: Muster mit Ordner

        vm.RestartNowCommand.Execute(null);
        await WaitFor(vm.RestartNowCommand);

        Assert.Equal(0, ctx.RestartCalls);
        Assert.True(vm.HasError);
        Assert.Equal("de", ctx.Config.Current.Settings.Language);
    }

    [Fact]
    public async Task Failed_restart_is_reported_and_the_choice_stays_saved()
    {
        var (vm, ctx) = Create();
        ctx.RestartResult = false;
        vm.Language = "en";

        vm.RestartNowCommand.Execute(null);
        await WaitFor(vm.RestartNowCommand);

        Assert.Equal(1, ctx.RestartCalls);
        Assert.Equal(ShellStrings.RestartFailed, vm.ErrorMessage);
        Assert.Equal("en", ctx.Config.Current.Settings.Language);
    }

    [Fact]
    public async Task Restart_now_is_locked_while_a_game_session_runs()
    {
        // Ein Neustart mitten in einer Sitzung überließe das Zurückstellen von HDR der neuen Instanz: gesperrt, mit Hinweis.
        var (vm, ctx) = Create();
        vm.Language = "en";
        Assert.False(vm.LanguageRestartBlocked);
        Assert.True(vm.RestartNowCommand.CanExecute(null));

        var changed = new List<string?>();
        var canExecuteChanged = 0;
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.RestartNowCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        ctx.SessionList.Add(new SessionInfo("Manual:testspiel", "Testspiel", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [7]));
        ctx.RaiseHdrStateChanged();

        Assert.True(vm.LanguageRestartBlocked);
        Assert.Contains(nameof(SettingsViewModel.LanguageRestartBlocked), changed);
        Assert.True(canExecuteChanged > 0);
        Assert.False(vm.RestartNowCommand.CanExecute(null));
        vm.RestartNowCommand.Execute(null);
        await WaitFor(vm.RestartNowCommand);
        Assert.Equal(0, ctx.RestartCalls);
        Assert.Equal("de", ctx.Config.Current.Settings.Language); // auch nichts gespeichert
        Assert.True(vm.HasChanges);

        // „Speichern“ geht weiter; nach dem Spiel ist der Neustart wieder frei.
        ctx.SessionList.Clear();
        ctx.RaiseHdrStateChanged();
        Assert.False(vm.LanguageRestartBlocked);
        Assert.True(vm.RestartNowCommand.CanExecute(null));
        vm.RestartNowCommand.Execute(null);
        await WaitFor(vm.RestartNowCommand);
        Assert.Equal(1, ctx.RestartCalls);
        Assert.Equal("en", ctx.LanguageAtRestart);
    }

    [Fact]
    public void Restart_now_is_locked_while_a_game_is_being_prepared()
    {
        var (vm, ctx) = Create();
        ctx.SessionList.Add(new SessionInfo("Steam:1", "Testspiel", SessionPhase.Launching, SessionTrigger.SteamLaunchOption, DateTimeOffset.UnixEpoch, [], []));
        vm.Language = "en";

        Assert.True(vm.LanguageRestartBlocked); // schon beim Öffnen der Einstellungen
        Assert.False(vm.RestartNowCommand.CanExecute(null));
    }

    [Fact]
    public void Settings_window_explains_why_restart_is_locked()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            var vm = new SettingsViewModel(context, new FakeFolderPicker());
            window.Attach(vm);
            vm.Language = "en";
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(1280, 900));

            var button = Find<Button>(window, "LanguageRestartButton");
            var hint = Find<TextBlock>(window, "LanguageRestartBlockedHint");
            Assert.True(button.IsEnabled);
            Assert.False(IsShown(hint));

            context.SessionList.Add(new SessionInfo("Manual:testspiel", "Testspiel", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [7]));
            context.RaiseHdrStateChanged();
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(1280, 900));
            Assert.False(button.IsEnabled);
            Assert.True(IsShown(hint));
            Assert.Equal(ShellStrings.LanguageRestartBlocked, hint.Text);

            context.SessionList.Clear();
            context.RaiseHdrStateChanged();
            WpfTestHost.DoEvents();
            Assert.True(button.IsEnabled);
            Assert.False(IsShown(hint));

            window.Close();
        });
    }

    [Fact]
    public void Settings_window_shows_the_choice_and_the_restart_hint_only_when_needed()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            var vm = new SettingsViewModel(context, new FakeFolderPicker());
            window.Attach(vm);
            WpfTestHost.Layout(window, new Size(1280, 900));

            var combo = Find<ComboBox>(window, "LanguageChoice");
            Assert.Equal(ShellStrings.LanguageLabel, AutomationProperties.GetName(combo));
            Assert.Equal(2, combo.Items.Count);
            Assert.Equal("de", combo.SelectedValue);

            var button = Find<Button>(window, "LanguageRestartButton");
            Assert.Equal(ShellStrings.LanguageRestartNow, AutomationProperties.GetName(button));
            Assert.False(IsShown(button));

            combo.SelectedValue = "en";
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(1280, 900));
            Assert.Equal("en", vm.Language);
            Assert.True(IsShown(button));
            Assert.True(IsShown(Find<TextBlock>(window, "LanguageRestartHint")));

            window.Close();
        });
    }

    [Fact]
    public void Settings_window_in_english_names_its_controls_in_english()
    {
        WpfTestHost.Run(() =>
        {
            var ui = CultureInfo.CurrentUICulture;
            var format = CultureInfo.CurrentCulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Settings.Language = "en");
                var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
                var window = new SettingsWindow();
                var vm = new SettingsViewModel(context, new FakeFolderPicker());
                window.Attach(vm);
                WpfTestHost.Layout(window, new Size(1280, 900));

                Assert.Equal("en", vm.DisplayedLanguage);
                Assert.False(vm.LanguageNeedsRestart);
                Assert.Equal("Save", AutomationProperties.GetName(Find<Button>(window, "SaveSettingsButton")));
                Assert.Equal("Cancel", AutomationProperties.GetName(Find<Button>(window, "CancelSettingsButton")));
                Assert.Equal("Language · Sprache", AutomationProperties.GetName(Find<ComboBox>(window, "LanguageChoice")));
                Assert.Equal("Start with Windows", AutomationProperties.GetName(Find<CheckBox>(window, "AutostartToggle")));
                Assert.Equal("1,000 ms", vm.PollIntervalText); // Formate wie en-US
                window.Close();
            }
            finally
            {
                CultureInfo.CurrentUICulture = ui;
                CultureInfo.CurrentCulture = format;
            }
        });
    }

    [Fact]
    public void Tray_menu_in_english()
    {
        WpfTestHost.Run(() =>
        {
            var ui = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                var model = new TrayMenuViewModel(new FakeTrayHost(), new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["A", "B"]), ex => throw ex);
                var menu = new HdrPilot.App.Views.Shell.TrayMenu { DataContext = model };
                WpfTestHost.DoEvents();
                var items = menu.Items.OfType<MenuItem>().ToDictionary(AutomationProperties.GetAutomationId);

                Assert.Equal("Turn HDR off now", items["TrayToggleHdr"].Header);
                Assert.Equal("Pause automation", AutomationProperties.GetName(items["TrayPause"]));
                Assert.Equal("Open game list", AutomationProperties.GetName(items["TrayOpen"]));
                Assert.Equal("Settings", AutomationProperties.GetName(items["TraySettings"]));
                Assert.Equal("Exit HDRpilot", AutomationProperties.GetName(items["TrayExit"]));
                Assert.Equal("HDR on", model.StatusTitle);
                Assert.Equal("2 games running", model.StatusDetail);
            }
            finally
            {
                CultureInfo.CurrentUICulture = ui;
            }
        });
    }

    private (SettingsViewModel Vm, FakeShellContext Context) Create(Action<AppConfig>? setup = null)
    {
        var config = ShellTestFactory.NewConfig(_fs, setup);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new AutostartService(_registry, Exe));
        return (new SettingsViewModel(ctx, new FakeFolderPicker()), ctx);
    }

    private static async Task WaitFor(HdrPilot.App.Infrastructure.AsyncRelayCommand command)
    {
        for (var i = 0; i < 200 && command.IsRunning; i++)
        {
            await Task.Delay(10);
        }

        Assert.False(command.IsRunning, "Befehl läuft noch");
    }

    private static T Find<T>(DependencyObject root, string automationId)
        where T : FrameworkElement =>
        WpfTestHost.Visuals(root).Concat(LogicalTree(root)).OfType<T>().First(e => AutomationProperties.GetAutomationId(e) == automationId);

    private static IEnumerable<DependencyObject> LogicalTree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var item in LogicalTree(child))
            {
                yield return item;
            }
        }
    }

    /// <summary>Sichtbar im Sinne der Bindung (das Fenster selbst wird im Test nie angezeigt).</summary>
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? e = element; e is not null; e = LogicalTreeHelper.GetParent(e) ?? System.Windows.Media.VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }
}
