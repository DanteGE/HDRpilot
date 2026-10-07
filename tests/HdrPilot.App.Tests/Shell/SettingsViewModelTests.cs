using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Shell;

public sealed class SettingsViewModelTests
{
    private const string Exe = @"C:\Apps\HdrPilot\HdrPilot.exe";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeFolderPicker _picker = new();

    [Fact]
    public void Defaults_of_a_new_configuration()
    {
        var (vm, _) = Create();

        Assert.Equal(1000, vm.PollIntervalMs);
        Assert.Equal(10, vm.GracePeriodSeconds);
        Assert.Equal(120, vm.LaunchTimeoutSeconds);
        Assert.False(vm.StartWithWindows);
        Assert.True(vm.Notifications);
        Assert.False(vm.HotkeyFallback);
        Assert.Empty(vm.SearchFolders);
        Assert.Equal(LogLevel.Information, vm.LogLevel);
        Assert.True(vm.HelperPatternsAreDefault);
        Assert.Equal(HelperPatternList.DefaultText, vm.HelperPatternsText);
        Assert.False(vm.HasChanges);
        Assert.False(vm.HasError);

        // Standard: keiner gewählt = Hauptbildschirm; Liste mit Kennzeichen, Hauptbildschirm zuerst.
        Assert.Equal(2, vm.Displays.Count);
        Assert.All(vm.Displays, d => Assert.False(d.IsSelected));
        Assert.True(vm.Displays[0].IsPrimary);
        Assert.True(vm.Displays[0].HdrSupported);
        Assert.False(vm.Displays[1].HdrSupported);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.DisplaysPrimaryFallback, "MAG 341C OLED"), vm.DisplaysSummary);
    }

    [Fact]
    public void Loads_the_stored_settings()
    {
        var (vm, _) = Create(c =>
        {
            var s = c.Settings;
            s.TargetDisplays = [FakeHdrController.Second];
            s.PollIntervalMs = 750;
            s.GracePeriodSeconds = 30;
            s.LaunchTimeoutSeconds = 240;
            s.Notifications = false;
            s.HotkeyFallback = true;
            s.SearchFolders = [@"D:\Spielordner"];
            s.LogLevel = LogLevel.Debug;
            s.HelperExecutablePatterns = ["Foo*", "BarHelper"];
        });

        Assert.Equal(750, vm.PollIntervalMs);
        Assert.Equal(30, vm.GracePeriodSeconds);
        Assert.Equal(240, vm.LaunchTimeoutSeconds);
        Assert.False(vm.Notifications);
        Assert.True(vm.HotkeyFallback);
        Assert.Equal([@"D:\Spielordner"], vm.SearchFolders);
        Assert.Equal(LogLevel.Debug, vm.LogLevel);
        Assert.Equal("Foo*" + Environment.NewLine + "BarHelper", vm.HelperPatternsText);
        Assert.False(vm.HelperPatternsAreDefault);
        Assert.True(vm.Displays.Single(d => d.Path == FakeHdrController.Second).IsSelected);
        Assert.False(vm.Displays.Single(d => d.Path == FakeHdrController.Primary).IsSelected);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.DisplaysSelected, 1, 2), vm.DisplaysSummary);
    }

    [Fact]
    public void Save_writes_everything_and_survives_a_restart()
    {
        var (vm, ctx) = Create();
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.Displays.Single(d => d.Path == FakeHdrController.Primary).IsSelected = true;
        vm.PollIntervalMs = 500;
        vm.GracePeriodSeconds = 45;
        vm.LaunchTimeoutSeconds = 300;
        vm.Notifications = false;
        vm.HotkeyFallback = true;
        vm.LogLevel = LogLevel.Warning;
        _picker.Results.Enqueue(@"E:\DRM-frei\");
        vm.AddSearchFolderCommand.Execute(null);
        vm.HelperPatternsText = HelperPatternList.DefaultText + Environment.NewLine + "MeinTool*";
        Assert.True(vm.HasChanges);

        Assert.True(vm.Save());

        Assert.Equal(1, closed);
        Assert.False(vm.HasChanges);
        Assert.Equal([true], ctx.SavedCalls);

        // Neustart: neuer ConfigService über dieselbe Ablage.
        var reloaded = ShellTestFactory.NewConfig(_fs).Current.Settings;
        Assert.Equal([FakeHdrController.Primary], reloaded.TargetDisplays);
        Assert.Equal(500, reloaded.PollIntervalMs);
        Assert.Equal(45, reloaded.GracePeriodSeconds);
        Assert.Equal(300, reloaded.LaunchTimeoutSeconds);
        Assert.False(reloaded.Notifications);
        Assert.True(reloaded.HotkeyFallback);
        Assert.Equal(LogLevel.Warning, reloaded.LogLevel);
        Assert.Equal([@"E:\DRM-frei"], reloaded.SearchFolders);
        Assert.NotNull(reloaded.HelperExecutablePatterns);
        Assert.Equal(HelperExecutables.DefaultPatterns.Count + 1, reloaded.HelperExecutablePatterns!.Count);
        Assert.Equal("MeinTool*", reloaded.HelperExecutablePatterns[^1]);
    }

    [Fact]
    public void Cancel_discards_all_changes()
    {
        var (vm, ctx) = Create(c => c.Settings.GracePeriodSeconds = 20);
        var before = _fs.ReadAllText(ShellTestFactory.ConfigPath);
        var revision = ctx.Config.Revision;
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.GracePeriodSeconds = 90;
        vm.StartWithWindows = true;
        vm.Displays[0].IsSelected = true;
        vm.HelperPatternsText = "NurDas";
        vm.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(before, _fs.ReadAllText(ShellTestFactory.ConfigPath));
        Assert.Equal(revision, ctx.Config.Revision);
        Assert.Equal(20, ctx.Config.Current.Settings.GracePeriodSeconds);
        Assert.Null(_registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.Empty(ctx.SavedCalls);
    }

    [Theory]
    [InlineData(10, 250)]
    [InlineData(249, 250)]
    [InlineData(5000, 5000)]
    [InlineData(99_999, 5000)]
    public void Poll_interval_is_limited(int input, int expected)
    {
        var (vm, _) = Create();
        vm.PollIntervalMs = input;
        Assert.Equal(expected, vm.PollIntervalMs);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(121, 120)]
    public void Grace_period_is_limited(int input, int expected)
    {
        var (vm, _) = Create();
        vm.GracePeriodSeconds = input;
        Assert.Equal(expected, vm.GracePeriodSeconds);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(601, 600)]
    public void Launch_timeout_is_limited(int input, int expected)
    {
        var (vm, _) = Create();
        vm.LaunchTimeoutSeconds = input;
        Assert.Equal(expected, vm.LaunchTimeoutSeconds);
    }

    [Fact]
    public void Out_of_range_values_from_the_config_are_clamped_on_load()
    {
        var (vm, _) = Create(c =>
        {
            c.Settings.PollIntervalMs = 9000;
            c.Settings.GracePeriodSeconds = 500;
            c.Settings.LaunchTimeoutSeconds = 15;
            c.Settings.LogLevel = LogLevel.Critical;
        });

        Assert.Equal(SettingsViewModel.PollIntervalMax, vm.PollIntervalMs);
        Assert.Equal(SettingsViewModel.GracePeriodMax, vm.GracePeriodSeconds);
        Assert.Equal(SettingsViewModel.LaunchTimeoutMin, vm.LaunchTimeoutSeconds);
        Assert.Equal(LogLevel.Error, vm.LogLevel);
    }

    [Fact]
    public void Unchanged_default_helper_list_is_stored_as_null_even_when_reordered()
    {
        var (vm, ctx) = Create();

        vm.HelperPatternsText = string.Join("\n", HelperExecutables.DefaultPatterns.Reverse().Select(p => "  " + p.ToUpperInvariant() + "  "));
        Assert.True(vm.HelperPatternsAreDefault);
        Assert.True(vm.Save());

        Assert.Null(ctx.Config.Current.Settings.HelperExecutablePatterns);
    }

    [Fact]
    public void Reset_restores_the_default_list()
    {
        var (vm, ctx) = Create(c => c.Settings.HelperExecutablePatterns = ["Nur*Eins"]);
        Assert.False(vm.HelperPatternsAreDefault);
        Assert.Equal(ShellStrings.HelperPatternsCustom, vm.HelperPatternsKind);

        vm.ResetHelperPatternsCommand.Execute(null);

        Assert.True(vm.HelperPatternsAreDefault);
        Assert.Equal(ShellStrings.HelperPatternsDefault, vm.HelperPatternsKind);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternsCount, HelperExecutables.DefaultPatterns.Count), vm.HelperPatternCountText);
        Assert.True(vm.Save());
        Assert.Null(ctx.Config.Current.Settings.HelperExecutablePatterns);
    }

    [Fact]
    public void Blank_lines_and_duplicates_are_dropped()
    {
        var (vm, ctx) = Create();

        vm.HelperPatternsText = "\r\n  Foo*  \r\n\r\nfoo*\nBar?\n";
        Assert.True(vm.Save());

        Assert.Equal(["Foo*", "Bar?"], ctx.Config.Current.Settings.HelperExecutablePatterns);
    }

    [Theory]
    [InlineData(@"Spiele\Crash*", "HelperPatternFolder")]
    [InlineData("C:/Tools/x.exe", "HelperPatternFolder")]
    [InlineData("a|b", "HelperPatternInvalidChars")]
    [InlineData("<Launcher>", "HelperPatternInvalidChars")]
    public void Invalid_patterns_block_saving(string pattern, string resourceKey)
    {
        var (vm, ctx) = Create();
        var revision = ctx.Config.Revision;

        vm.HelperPatternsText = "Ok*\n" + pattern;

        Assert.True(vm.HasError);
        var template = ShellStrings.ResourceManager.GetString(resourceKey, CultureInfo.CurrentUICulture)!;
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, template, 2, pattern), vm.ErrorMessage);
        Assert.False(vm.Save());
        Assert.Equal(revision, ctx.Config.Revision);
    }

    [Fact]
    public void Too_long_and_too_many_patterns_are_rejected()
    {
        var tooLong = HelperPatternList.Parse(new string('x', HelperPatternList.MaxPatternLength + 1));
        var tooMany = HelperPatternList.Parse(string.Join("\n", Enumerable.Range(0, HelperPatternList.MaxPatterns + 1).Select(i => "p" + i.ToString(CultureInfo.InvariantCulture))));
        var exactlyMax = HelperPatternList.Parse(string.Join("\n", Enumerable.Range(0, HelperPatternList.MaxPatterns).Select(i => "p" + i.ToString(CultureInfo.InvariantCulture))));

        Assert.False(tooLong.IsValid);
        Assert.False(tooMany.IsValid);
        Assert.True(exactlyMax.IsValid);
    }

    [Fact]
    public void Fixing_an_invalid_pattern_clears_the_error()
    {
        var (vm, _) = Create();

        vm.HelperPatternsText = @"a\b";
        Assert.True(vm.HasError);
        vm.HelperPatternsText = "ab";

        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Search_folders_are_normalized_without_duplicates()
    {
        var (vm, _) = Create(c => c.Settings.SearchFolders = [@"D:\Spielordner"]);

        Assert.True(vm.TryAddSearchFolder(@"E:\Games\"));
        Assert.False(vm.TryAddSearchFolder(@"d:\SPIELORDNER"));
        Assert.Equal(@"D:\Spielordner", vm.SelectedSearchFolder);
        Assert.False(vm.TryAddSearchFolder(@"relativ\ordner"));
        Assert.False(vm.TryAddSearchFolder("   "));

        Assert.Equal([@"D:\Spielordner", @"E:\Games"], vm.SearchFolders);
        Assert.True(vm.HasSearchFolders);
    }

    [Fact]
    public void Add_uses_the_folder_picker_and_ignores_cancel()
    {
        var (vm, _) = Create(c => c.Settings.SearchFolders = [@"D:\Spielordner"]);

        vm.AddSearchFolderCommand.Execute(null);
        Assert.Equal([@"D:\Spielordner"], _picker.Requests);
        Assert.Single(vm.SearchFolders);
        Assert.False(vm.HasChanges);

        _picker.Results.Enqueue(@"F:\Mehr Spiele");
        vm.AddSearchFolderCommand.Execute(null);
        Assert.Equal(@"F:\Mehr Spiele", vm.SelectedSearchFolder);
        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void Remove_deletes_the_selection_and_selects_the_neighbour()
    {
        var (vm, ctx) = Create(c => c.Settings.SearchFolders = [@"C:\A", @"C:\B", @"C:\C"]);
        Assert.False(vm.RemoveSearchFolderCommand.CanExecute(null));

        vm.SelectedSearchFolder = @"C:\B";
        Assert.True(vm.RemoveSearchFolderCommand.CanExecute(null));
        vm.RemoveSearchFolderCommand.Execute(null);
        Assert.Equal([@"C:\A", @"C:\C"], vm.SearchFolders);
        Assert.Equal(@"C:\C", vm.SelectedSearchFolder);

        vm.RemoveSearchFolderCommand.Execute(null);
        vm.RemoveSearchFolderCommand.Execute(null);
        Assert.Empty(vm.SearchFolders);
        Assert.Null(vm.SelectedSearchFolder);
        Assert.False(vm.RemoveSearchFolderCommand.CanExecute(null));

        Assert.True(vm.Save());
        Assert.Empty(ctx.Config.Current.Settings.SearchFolders);
        Assert.Equal([true], ctx.SavedCalls);
    }

    [Fact]
    public void Autostart_is_written_to_the_run_key_on_save_only()
    {
        var (vm, ctx) = Create();

        vm.StartWithWindows = true;
        Assert.Null(_registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.True(vm.Save());

        Assert.Equal("\"" + Exe + "\" --minimized", _registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.True(ctx.Config.Current.Settings.StartWithWindows);
        Assert.Equal([false], ctx.SavedCalls);

        var (again, _) = Create();
        Assert.True(again.StartWithWindows);
        again.StartWithWindows = false;
        Assert.True(again.Save());
        Assert.Null(_registry.Get(RegistryHiveKind.CurrentUser, AutostartService.RunKeyPath, "HdrPilot"));
        Assert.False(ShellTestFactory.NewConfig(_fs).Current.Settings.StartWithWindows);
    }

    [Fact]
    public void Autostart_failure_keeps_the_window_open_and_saves_nothing()
    {
        var config = ShellTestFactory.NewConfig(_fs);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new FailingAutostart(new UnauthorizedAccessException("verweigert")));
        using var vm = new SettingsViewModel(ctx, _picker);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        var revision = config.Revision;

        vm.StartWithWindows = true;
        vm.GracePeriodSeconds = 60;

        Assert.False(vm.Save());
        Assert.Equal(0, closed);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.AutostartFailed, "verweigert"), vm.ErrorMessage);
        Assert.Equal(revision, config.Revision);
    }

    [Fact]
    public void Editing_valid_patterns_keeps_an_autostart_error_visible()
    {
        var config = ShellTestFactory.NewConfig(_fs);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new FailingAutostart(new UnauthorizedAccessException("verweigert")));
        using var vm = new SettingsViewModel(ctx, _picker);
        vm.StartWithWindows = true;
        Assert.False(vm.Save());
        var autostartError = vm.ErrorMessage;
        Assert.NotNull(autostartError);

        vm.HelperPatternsText = "Gültig*";
        Assert.Equal(autostartError, vm.ErrorMessage);

        // Eigene Prüfmeldung ersetzt sie und verschwindet nach der Korrektur wieder.
        vm.HelperPatternsText = @"a\b";
        Assert.NotEqual(autostartError, vm.ErrorMessage);
        vm.HelperPatternsText = "ab";
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Disconnected_target_display_stays_visible_and_selected()
    {
        const string gone = @"\\?\DISPLAY#DEL41A8#abc";
        var (vm, ctx) = Create(c => c.Settings.TargetDisplays = [gone]);

        var option = vm.Displays.Single(d => d.Path == gone);
        Assert.True(option.IsSelected);
        Assert.False(option.IsConnected);
        Assert.Equal("DEL41A8", option.Name);

        Assert.True(vm.Save());
        Assert.Equal([gone], ctx.Config.Current.Settings.TargetDisplays);
    }

    [Fact]
    public void Display_change_refreshes_badges_and_keeps_the_selection()
    {
        var (vm, ctx) = Create(c => c.Settings.TargetDisplays = [FakeHdrController.Primary]);
        var primary = vm.Displays.Single(d => d.Path == FakeHdrController.Primary);
        Assert.False(primary.HdrEnabled);

        ctx.Hdr[FakeHdrController.Primary].HdrOn = true;
        ctx.Hdr[FakeHdrController.Second].Connected = false;
        ctx.Hdr.Displays.Add(new FakeDisplay(@"\\?\DISPLAY#LGD0001#tv", "LG OLED TV") { Capable = true });
        ctx.RaiseHdrStateChanged();

        Assert.True(primary.HdrEnabled);
        Assert.True(primary.IsSelected);
        Assert.False(vm.Displays.Single(d => d.Path == FakeHdrController.Second).IsConnected);
        var tv = vm.Displays.Single(d => d.Path == @"\\?\DISPLAY#LGD0001#tv");
        Assert.False(tv.IsSelected);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Failing_display_query_shows_an_empty_list_and_keeps_the_stored_targets()
    {
        const string stored = @"\\?\DISPLAY#DEL41A8#abc";
        var config = ShellTestFactory.NewConfig(_fs, c => c.Settings.TargetDisplays = [stored]);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new AutostartService(_registry, Exe))
        {
            DisplayFailure = new InvalidOperationException("QueryDisplayConfig fehlgeschlagen"),
        };
        using var vm = new SettingsViewModel(ctx, _picker);

        // Nur der gespeicherte (jetzt „nicht verbundene“) Bildschirm bleibt sichtbar und gewählt.
        var option = Assert.Single(vm.Displays);
        Assert.Equal(stored, option.Path);
        Assert.False(option.IsConnected);

        // Abfrage klappt wieder (Bildschirm angesteckt): Liste füllt sich, Auswahl bleibt.
        ctx.DisplayFailure = null;
        ctx.RaiseHdrStateChanged();
        Assert.Equal(3, vm.Displays.Count);
        Assert.True(vm.Save());
        Assert.Equal([stored], config.Current.Settings.TargetDisplays);
    }

    [Fact]
    public void No_displays_at_all_reports_the_empty_state()
    {
        var config = ShellTestFactory.NewConfig(_fs);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new AutostartService(_registry, Exe))
        {
            DisplayFailure = new InvalidOperationException("kein Grafiktreiber"),
        };
        using var vm = new SettingsViewModel(ctx, _picker);

        Assert.False(vm.HasDisplays);
        Assert.Equal(ShellStrings.DisplaysPrimaryFallbackUnknown, vm.DisplaysSummary);
        Assert.True(vm.Save());
        Assert.Empty(config.Current.Settings.TargetDisplays);
    }

    [Fact]
    public void Events_after_dispose_are_ignored()
    {
        var (vm, ctx) = Create();
        vm.Dispose();
        ctx.Hdr.Displays.Add(new FakeDisplay(@"\\?\DISPLAY#LGD0001#tv", "LG OLED TV") { Capable = true });

        vm.Displays[0].IsSelected = true;
        ctx.RaiseHdrStateChanged();

        Assert.Equal(2, vm.Displays.Count);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Dispose_unsubscribes_from_app_events()
    {
        var (vm, ctx) = Create();
        Assert.Equal(1, ctx.Subscribers);

        vm.Dispose();

        Assert.Equal(0, ctx.Subscribers);
    }

    [Fact]
    public void Reverting_an_edit_clears_the_change_marker()
    {
        var (vm, _) = Create();

        vm.GracePeriodSeconds = 11;
        Assert.True(vm.HasChanges);
        Assert.Equal(ShellStrings.UnsavedChanges, vm.ChangeStatus);
        vm.GracePeriodSeconds = 10;

        Assert.False(vm.HasChanges);
        Assert.Equal(ShellStrings.NoChanges, vm.ChangeStatus);
    }

    [Fact]
    public void Open_log_folder_failure_is_shown()
    {
        var (vm, ctx) = Create();
        ctx.OpenLogFolderFailure = new System.ComponentModel.Win32Exception(2, "nicht gefunden");

        vm.OpenLogFolderCommand.Execute(null);

        Assert.Equal(1, ctx.OpenLogFolderCalls);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.OpenLogFolderFailed, "nicht gefunden"), vm.ErrorMessage);
    }

    [Fact]
    public void Formatted_values_use_resources()
    {
        var (vm, _) = Create();

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitMilliseconds, 1000), vm.PollIntervalText);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitSeconds, 10), vm.GracePeriodText);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitSeconds, 120), vm.LaunchTimeoutText);
        Assert.Equal(5, vm.LogLevels.Count);
        Assert.Equal(LogLevel.Trace, vm.LogLevels[0].Value);
        Assert.Equal(LogLevel.Error, vm.LogLevels[^1].Value);
    }

    private (SettingsViewModel Vm, FakeShellContext Context) Create(Action<AppConfig>? setup = null)
    {
        var config = ShellTestFactory.NewConfig(_fs, setup);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new AutostartService(_registry, Exe));
        return (new SettingsViewModel(ctx, _picker), ctx);
    }
}
