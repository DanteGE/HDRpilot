using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

public sealed class GameDetailsViewModelTests
{
    private const string Dir = @"D:\GOG-Spiele\Cyberpunk 2077";

    private static GameConfig Cyberpunk()
    {
        var game = Game(GameSource.Gog, "1423049311", "Cyberpunk 2077", hdrAuto: true, isNew: false, installDir: Dir);
        game.KnownExecutables = [Dir + @"\REDprelauncher.exe", Dir + @"\bin\x64\Cyberpunk2077.exe"];
        game.DetectedExecutables = [Dir + @"\bin\x64\Cyberpunk2077.exe", Dir + @"\bin\x64\CrashTool.exe"];
        game.ExtraExecutables = [@"C:\Tools\ReShadeLauncher.exe"];
        game.ExcludedExecutables = [Dir + @"\bin\x64\CrashTool.exe", "Benchmark.exe"];
        return game;
    }

    private static (TestLibraryContext Context, LibraryViewModel Library, GameDetailsViewModel Details) Open(GameConfig game, FakeDialogs? dialogs = null)
    {
        var context = new TestLibraryContext(game);
        var library = new LibraryViewModel(context, dialogs ?? new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        return (context, library, library.Details!);
    }

    private static GameConfig Saved(TestLibraryContext context) => context.Config.Games.Single();

    [Fact]
    public void Executables_show_origin_exclusions_and_helpers() => Sta.Run(() =>
    {
        var (_, library, details) = Open(Cyberpunk());
        using var _l = library;

        var rows = details.Executables.Select(e => (e.FileName, e.OriginText, e.IsExcluded)).ToList();
        Assert.Equal(
            [
                ("Cyberpunk2077.exe", LibraryStrings.Exe_OriginDetected, false),
                ("CrashTool.exe", LibraryStrings.Exe_OriginExcluded, true),
                ("REDprelauncher.exe", LibraryStrings.Exe_OriginHelper, false),
                ("ReShadeLauncher.exe", LibraryStrings.Exe_OriginExtra, false),
                ("Benchmark.exe", LibraryStrings.Exe_OriginExcluded, true),
            ],
            rows);
        Assert.True(details.Executables.Single(e => e.FileName == "REDprelauncher.exe").IsInactive);
        Assert.True(details.HasExecutables);
    });

    [Fact]
    public void Removing_a_known_exe_excludes_it_and_removing_an_added_one_deletes_it() => Sta.Run(async () =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;

        details.SelectedExecutable = details.Executables.Single(e => e.FileName == "Cyberpunk2077.exe");
        Assert.True(details.RemoveExeCommand.CanExecute(null));
        details.RemoveExeCommand.Execute(null);
        await Task.Yield();

        var saved = Saved(context);
        Assert.Contains(Dir + @"\bin\x64\Cyberpunk2077.exe", saved.ExcludedExecutables);
        Assert.Contains(Dir + @"\bin\x64\Cyberpunk2077.exe", saved.KnownExecutables); // Launcher-Angabe bleibt erhalten
        Assert.True(details.Executables.Single(e => e.FileName == "Cyberpunk2077.exe").IsExcluded);
        Assert.Equal(StatusKind.Info, details.MessageKind);

        details.SelectedExecutable = details.Executables.Single(e => e.FileName == "ReShadeLauncher.exe");
        details.RemoveExeCommand.Execute(null);
        await Task.Yield();

        saved = Saved(context);
        Assert.Empty(saved.ExtraExecutables);
        Assert.DoesNotContain(@"C:\Tools\ReShadeLauncher.exe", saved.ExcludedExecutables);
        Assert.DoesNotContain(details.Executables, e => e.FileName == "ReShadeLauncher.exe");
    });

    [Fact]
    public void Adding_an_exe_puts_it_into_extra_and_lifts_an_exclusion() => Sta.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        dialogs.Executables.Enqueue(@"E:\Mods\Cyberpunk2077-Mod.exe");
        dialogs.Executables.Enqueue(Dir + @"\bin\x64\CrashTool.exe");
        var (context, library, details) = Open(Cyberpunk(), dialogs);
        using var _l = library;

        details.AddExeCommand.Execute(null);
        await Task.Yield();
        Assert.Contains(@"E:\Mods\Cyberpunk2077-Mod.exe", Saved(context).ExtraExecutables);
        Assert.Equal("Cyberpunk2077-Mod.exe", details.SelectedExecutable?.FileName);
        Assert.Equal(StatusKind.Success, details.MessageKind);

        details.AddExeCommand.Execute(null);
        await Task.Yield();
        var saved = Saved(context);
        Assert.DoesNotContain(Dir + @"\bin\x64\CrashTool.exe", saved.ExcludedExecutables);
        Assert.DoesNotContain(Dir + @"\bin\x64\CrashTool.exe", saved.ExtraExecutables); // war schon gemerkt
        Assert.False(details.Executables.Single(e => e.FileName == "CrashTool.exe").IsExcluded);

        // Abbruch in der Dateiauswahl ändert nichts.
        var updates = context.Updates;
        details.AddExeCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(updates, context.Updates);
    });

    [Fact]
    public void Excluded_exe_can_be_taken_back() => Sta.Run(async () =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;

        details.SelectedExecutable = details.Executables.Single(e => e.FileName == "Benchmark.exe");
        Assert.True(details.IsSelectedExecutableExcluded);
        Assert.False(details.RemoveExeCommand.CanExecute(null));
        Assert.True(details.IncludeExeCommand.CanExecute(null));

        details.IncludeExeCommand.Execute(null);
        await Task.Yield();

        Assert.Equal([Dir + @"\bin\x64\CrashTool.exe"], Saved(context).ExcludedExecutables);
        Assert.DoesNotContain(details.Executables, e => e.FileName == "Benchmark.exe");
    });

    [Fact]
    public void Relearn_forgets_detected_executables() => Sta.Run(async () =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;
        Assert.True(details.RelearnExeCommand.CanExecute(null));

        details.RelearnExeCommand.Execute(null);
        await Task.Yield();

        var saved = Saved(context);
        Assert.Empty(saved.DetectedExecutables);
        Assert.Equal(2, saved.KnownExecutables.Count);
        Assert.False(details.RelearnExeCommand.CanExecute(null));
        Assert.Equal(LibraryStrings.Exe_RelearnDone, details.Message);
    });

    [Fact]
    public void Display_choice_is_global_by_default_or_a_specific_display() => Sta.Run(() =>
    {
        var game = Cyberpunk();
        var context = new TestLibraryContext(game);
        context.Hdr.Displays.Add(new FakeDisplay(FakeHdrController.Second, "Odyssey G7") { Capable = false });
        using var library = new LibraryViewModel(context, new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;

        Assert.Equal(3, details.Displays.Count);
        Assert.True(details.SelectedDisplay!.IsGlobal);
        Assert.Equal(LibraryText.Format(LibraryStrings.Display_GlobalWith, "MAG 341C OLED"), details.Displays[0].Label);
        Assert.True(details.Displays[1].IsHdrCapable);
        Assert.Equal(LibraryText.Format(LibraryStrings.Display_LabelWithDetail, LibraryStrings.Display_Primary, LibraryStrings.Display_HdrCapable), details.Displays[1].Detail);
        Assert.Equal("MAG 341C OLED · Hauptbildschirm · HDR-fähig", details.Displays[1].ToString());
        Assert.False(details.Displays[2].IsHdrCapable);
        Assert.Equal(LibraryStrings.Display_NoHdr, details.Displays[2].Detail);

        details.SelectedDisplay = details.Displays.Single(d => d.DevicePath == FakeHdrController.Second);
        Assert.Equal(FakeHdrController.Second, Saved(context).Display);

        details.SelectedDisplay = details.Displays[0];
        Assert.Null(Saved(context).Display);
    });

    [Fact]
    public void Display_that_is_not_connected_stays_selected() => Sta.Run(() =>
    {
        var game = Cyberpunk();
        game.Display = @"\\?\DISPLAY#TV#weg";
        var (context, library, details) = Open(game);
        using var _l = library;

        var selected = details.SelectedDisplay!;
        Assert.Equal(@"\\?\DISPLAY#TV#weg", selected.DevicePath);
        Assert.False(selected.IsConnected);
        Assert.Equal(LibraryStrings.Display_NotConnected, selected.Label);
        Assert.Equal(@"\\?\DISPLAY#TV#weg", Saved(context).Display); // nur Anzeige, nichts überschrieben
    });

    [Fact]
    public void Restore_after_switch_writes_config() => Sta.Run(() =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;
        Assert.True(details.RestoreAfter);

        details.RestoreAfter = false;

        Assert.False(Saved(context).RestoreAfter);
        Assert.Equal(1, context.Updates);
    });

    [Fact]
    public void Launch_goes_through_the_app_and_shows_the_result() => Sta.Run(async () =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;

        details.LaunchCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(["Gog:1423049311"], context.Launched);
        Assert.Equal(StatusKind.Success, details.MessageKind);
        var hdrTime = context.NextLaunch.HdrConfirmedAt!.Value.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Equal(LibraryText.Format(LibraryStrings.Launch_Started, hdrTime), details.Message);

        context.NextLaunch = new LaunchOutcome(false, "Für \"Cyberpunk 2077\" ist kein Start-Befehl bekannt.", null, null);
        details.LaunchCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(StatusKind.Error, details.MessageKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.Launch_Failed, context.NextLaunch.Message), details.Message);
        Assert.False(details.IsLaunching);
    });

    [Fact]
    public void Launch_is_disabled_for_games_that_are_not_installed() => Sta.Run(() =>
    {
        var game = Cyberpunk();
        game.IsInstalled = false;
        var (_, library, details) = Open(game);
        using var _l = library;

        Assert.False(details.LaunchCommand.CanExecute(null));
    });

    [Fact]
    public void Shortcuts_report_path_or_error() => Sta.Run(() =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;

        details.CreateDesktopShortcutCommand.Execute(null);
        Assert.Equal(StatusKind.Success, details.MessageKind);
        Assert.Contains(@"C:\Links\Desktop\Cyberpunk 2077 (HDR).lnk", details.Message, StringComparison.Ordinal);

        details.CreateStartMenuShortcutCommand.Execute(null);
        Assert.Equal([("Desktop", "Gog:1423049311"), ("Startmenü", "Gog:1423049311")], context.FakeShortcuts.Created);

        context.FakeShortcuts.Failure = new UnauthorizedAccessException("Zugriff verweigert");
        details.CreateDesktopShortcutCommand.Execute(null);
        Assert.Equal(StatusKind.Error, details.MessageKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.Shortcut_Failed, "Zugriff verweigert"), details.Message);
    });

    [Fact]
    public void Manual_games_can_be_removed_after_confirmation() => Sta.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        var (context, library, details) = Open(Game(GameSource.Manual, "m1", "Mein Spiel", hdrAuto: true, isNew: false), dialogs);
        using var _l = library;
        Assert.True(details.IsManual);
        Assert.True(details.RemoveGameCommand.CanExecute(null));

        dialogs.Answers.Enqueue(false);
        details.RemoveGameCommand.Execute(null);
        await Task.Yield();
        Assert.Single(context.Config.Games);
        Assert.Contains("Mein Spiel", dialogs.Confirms[0].Message, StringComparison.Ordinal);

        dialogs.Answers.Enqueue(true);
        details.RemoveGameCommand.Execute(null);
        await Task.Yield();
        Assert.Empty(context.Config.Games);
        Assert.Null(library.SelectedGame);
        Assert.Equal(LibraryText.Format(LibraryStrings.Game_Removed, "Mein Spiel"), library.StatusMessage);
    });

    [Fact]
    public void Launcher_games_cannot_be_removed() => Sta.Run(() =>
    {
        var (_, library, details) = Open(Cyberpunk());
        using var _l = library;

        Assert.False(details.IsManual);
        Assert.False(details.RemoveGameCommand.CanExecute(null));
    });

    [Fact]
    public void Switch_in_details_and_row_are_the_same() => Sta.Run(() =>
    {
        var (context, library, details) = Open(Cyberpunk());
        using var _l = library;

        details.Item.HdrAuto = false;

        Assert.False(library.Games.Single().HdrAuto);
        Assert.False(Saved(context).HdrAuto);
    });
}
