using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Einstellungen entfernen…“ in der Bibliothek (E90, G11.1): nur bei nicht installierten Launcher-Spielen, die Rückfrage
/// nennt, was verloren geht, warnt bei fehlendem Laufwerk und bietet die HDRpilot-Verknüpfungen per Häkchen an. „Abbrechen“
/// lässt alles, wie es ist; „Entfernen“ nimmt genau dieses Spiel heraus und wählt den Nachbarn. Findet ein Einlesen das Spiel
/// während der Rückfrage wieder oder beginnt eine Sitzung, bleibt alles; während einer Sitzung ist es gesperrt.
/// </summary>
public sealed class RemoveSettingsTests
{
    private const string Outlast = "Steam:1304930";
    private const string Klippe = "Steam:9990140";
    private const string Helldivers = "Steam:553850";
    private const string Manual = "Manual:abc";
    private const string DeadIsland = "Epic:Crow";

    /// <summary>
    /// Nach Name: Dead Island 2, Eigenes Spiel (nicht installiert), HELLDIVERS 2, The Outlast Trials (nicht installiert, auf
    /// dem nicht verbundenen Laufwerk Q:, „HDR automatisch“ an, zwei gemerkte EXE-Dateien), KLIPPE Demo (nicht installiert,
    /// auf C:, ohne eigene Einstellungen, neu).
    /// </summary>
    private static TestLibraryContext SampleContext()
    {
        var outlast = Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, isNew: false, installed: false, installDir: @"Q:\SteamLibrary\steamapps\common\Nachtprobe");
        outlast.DetectedExecutables = [@"Q:\SteamLibrary\steamapps\common\Nachtprobe\TOTClient.exe", @"Q:\SteamLibrary\steamapps\common\Nachtprobe\Binaries\Win64\TOTClient-Win64-Shipping.exe"];
        var context = new TestLibraryContext(
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
            Game(GameSource.Manual, "abc", "Eigenes Spiel", hdrAuto: true, isNew: false, installed: false),
            Game(GameSource.Steam, "553850", "HELLDIVERS 2", hdrAuto: true, isNew: false),
            outlast,
            Game(GameSource.Steam, "9990140", "KLIPPE Demo", installed: false, installDir: @"C:\Program Files (x86)\Steam\steamapps\common\KlippeDemo"));
        context.Files.AddFile(OwnIcon(Outlast), "png");
        context.Files.AddFile(OwnIcon(Klippe), "png");
        context.Files.AddFile(OwnIcon(Helldivers), "png");
        return context;
    }

    private static string OwnIcon(string key) => Path.Combine(IconDirectory, CustomIcons.FileNameFor(key));

    private static GameItemViewModel Item(LibraryViewModel vm, string key) => vm.Games.Single(g => g.Key == key);

    private static GameShortcut Shortcut(string name) => new(@"C:\Users\Test\Desktop\" + name + ".lnk", Path.ChangeExtension(OwnIcon(Outlast), ".ico"));

    private static LibraryScanReport ScanFinding(params (string Id, string Name)[] steamGames) => new(
    [
        new ScannerReport(new ScanResult
        {
            Source = GameSource.Steam,
            ScannerName = "Steam",
            LauncherDetected = true,
            Games = [.. steamGames.Select(g => new GameEntry { Source = GameSource.Steam, SourceId = g.Id, Name = g.Name, InstallDir = @"C:\Spiele\" + g.Name })],
        }, TimeSpan.Zero),
    ]);

    private static async Task Remove(LibraryViewModel vm, string key)
    {
        vm.SelectedGame = Item(vm, key);
        vm.RemoveSettingsCommand.Execute(null);
        await Sta.Finished(vm.RemoveSettingsCommand);
    }

    [Fact]
    public void RemoveSettings_is_offered_only_for_not_installed_launcher_games() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        foreach (var (key, offered) in new[] { (Outlast, true), (Klippe, true), (Helldivers, false), (Manual, false), (DeadIsland, false) })
        {
            vm.SelectedGame = Item(vm, key);
            Assert.Equal(offered, vm.SelectedGame.IsRemovable);
            Assert.Equal(offered, vm.RemoveSettingsCommand.CanExecute(null));
            Assert.Equal(offered, vm.Details!.CanRemoveSettings);
            Assert.Equal(offered, vm.Details.RemoveSettingsCommand.CanExecute(null));
        }

        vm.SelectedGame = Item(vm, Outlast);
        Assert.Equal(LibraryStrings.Details_NotInstalledRemovable, vm.Details!.NotInstalledText);
        vm.SelectedGame = Item(vm, Manual);
        Assert.Equal(LibraryStrings.Details_NotInstalled, vm.Details!.NotInstalledText);
        Assert.True(vm.Details.RemoveGameCommand.CanExecute(null)); // eigene Spiele behalten „Aus der Bibliothek entfernen“
    });

    [Fact]
    public void RemoveSettings_confirmation_names_the_losses_warns_about_the_drive_and_offers_the_shortcuts() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.FakeShortcuts.Existing[Outlast] = [Shortcut("The Outlast Trials (HDR)"), Shortcut("Outlast")];
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_ConfirmTitle, "The Outlast Trials"), request.Title);
        Assert.Equal(["„HDR automatisch“ ist an", "2 gemerkte EXE-Dateien"], request.Details);
        Assert.Equal(LibraryStrings.RemoveSettings_LossHeader, request.DetailsHeader);
        Assert.Equal("Laufwerk Q: ist nicht verbunden. Das Spiel ist vielleicht noch installiert.", request.Warning);
        Assert.Equal("Auch die 2 HDRpilot-Verknüpfungen löschen", request.OptionText);
        Assert.Equal(LibraryStrings.RemoveSettings_Confirm, request.ConfirmText);
        Assert.True(request.IsDanger);
        Assert.Contains("ändert sich nichts", request.Message, StringComparison.Ordinal);
    });

    [Fact]
    public void RemoveSettings_without_drive_problem_and_shortcuts_has_no_warning_and_no_checkbox() => Sta.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(SampleContext(), dialogs);

        await Remove(vm, Klippe);

        var request = Assert.Single(dialogs.Requests);
        Assert.Null(request.Warning);
        Assert.Null(request.OptionText);
        Assert.Equal([LibraryStrings.RemoveSettings_LossNothing], request.Details);
    });

    [Fact]
    public void RemoveSettings_cancel_leaves_the_config_and_all_files_unchanged() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.FakeShortcuts.Existing[Outlast] = [Shortcut("The Outlast Trials (HDR)")];
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(false);
        dialogs.OptionAnswers.Enqueue(true); // Häkchen gesetzt, aber abgebrochen
        using var vm = new LibraryViewModel(context, dialogs);
        var before = context.Files.ReadAllBytes(ConfigPath);
        var updates = context.Updates;

        await Remove(vm, Outlast);

        Assert.Single(dialogs.Requests);
        Assert.Equal(before, context.Files.ReadAllBytes(ConfigPath));
        Assert.Equal(updates, context.Updates);
        Assert.True(context.Files.FileExists(OwnIcon(Outlast)));
        Assert.Empty(context.FakeIcons.ForgottenGames);
        Assert.Empty(context.FakeShortcuts.Deleted);
        Assert.Same(Item(vm, Outlast), vm.SelectedGame);
        Assert.Null(vm.StatusMessage);
    });

    [Fact]
    public void RemoveSettings_removes_exactly_this_game_and_selects_its_neighbour() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        var others = context.Config.Games.Where(g => g.Key != Outlast).Select(g => System.Text.Json.JsonSerializer.Serialize(g, ConfigJson.Options)).ToList();

        await Remove(vm, Outlast);

        Assert.Null(context.Config.FindGame(Outlast));
        Assert.Equal(others, context.Config.Games.Select(g => System.Text.Json.JsonSerializer.Serialize(g, ConfigJson.Options)));
        Assert.DoesNotContain(vm.Games, g => g.Key == Outlast);
        var klippe = Item(vm, Klippe);
        Assert.Same(klippe, vm.SelectedGame); // der Nachbar: das nächste Spiel der Liste
        Assert.Same(klippe, vm.Details?.Item);
        Assert.Same(klippe, vm.Hero?.Item);
        Assert.True(klippe.IsNew); // leise gewählt, nicht als angesehen markiert
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_Done, "The Outlast Trials"), vm.StatusMessage);
        Assert.Equal(StatusKind.Success, vm.StatusKind);
        context.Dispatcher.RunDelayed(); // ausstehendes „angesehen“ gespeichert (es gibt keins)
        Assert.True(context.Config.FindGame(Klippe)!.IsNew);
        Assert.False(context.Files.FileExists(OwnIcon(Outlast)));
        Assert.True(context.Files.FileExists(OwnIcon(Klippe)));
        Assert.True(context.Files.FileExists(OwnIcon(Helldivers)));
        Assert.Equal([Outlast], context.FakeIcons.ForgottenGames);
        Assert.Equal(4, vm.TotalCount);
    });

    [Fact]
    public void RemoveSettings_of_the_last_game_selects_the_previous_one() => Sta.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(SampleContext(), dialogs);

        await Remove(vm, Klippe);

        Assert.Same(Item(vm, Outlast), vm.SelectedGame);
    });

    [Fact]
    public void RemoveSettings_with_the_checkbox_deletes_the_shortcuts_and_without_keeps_them() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.FakeShortcuts.Existing[Outlast] = [Shortcut("The Outlast Trials (HDR)"), Shortcut("Outlast")];
        context.FakeShortcuts.Existing[Klippe] = [Shortcut("KLIPPE Demo (HDR)")];
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        dialogs.OptionAnswers.Enqueue(true);
        dialogs.Answers.Enqueue(true);
        dialogs.OptionAnswers.Enqueue(false);
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);
        Assert.Equal(2, context.FakeShortcuts.Deleted.Count);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_DoneShortcutsMany, "The Outlast Trials", 2), vm.StatusMessage);

        await Remove(vm, Klippe);
        Assert.Equal(2, context.FakeShortcuts.Deleted.Count); // ohne Häkchen bleiben sie
        Assert.Single(context.FakeShortcuts.Existing[Klippe]);
        Assert.Null(context.Config.FindGame(Klippe));
    });

    [Fact]
    public void RemoveSettings_game_found_again_while_confirming_keeps_everything() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        dialogs.WhileConfirmOpen = () => context.ConfigService.ApplyScan(ScanFinding(("1304930", "The Outlast Trials"), ("553850", "HELLDIVERS 2"), ("9990140", "KLIPPE Demo")), context.Files);
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);

        var outlast = context.Config.FindGame(Outlast)!;
        Assert.True(outlast.IsInstalled);
        Assert.True(outlast.HdrAuto);
        Assert.Equal(2, outlast.DetectedExecutables.Count);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_Kept, "The Outlast Trials"), vm.StatusMessage);
        Assert.True(context.Files.FileExists(OwnIcon(Outlast)));
        Assert.Empty(context.FakeIcons.ForgottenGames);
        Assert.Same(Item(vm, Outlast), vm.SelectedGame);
    });

    [Fact]
    public void RemoveSettings_is_blocked_while_a_session_runs_or_is_prepared() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = Item(vm, Outlast);
        Assert.True(vm.RemoveSettingsCommand.CanExecute(null));

        // Vorbereitet (Steam-Startoption, Start über HDRpilot): Phase „startet“.
        context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Launching, SessionTrigger.SteamLaunchOption, DateTimeOffset.UnixEpoch, [], []));
        context.RaiseHdrStateChanged();
        Assert.False(vm.RemoveSettingsCommand.CanExecute(null));
        Assert.False(vm.Details!.RemoveSettingsCommand.CanExecute(null));
        vm.Details.RemoveSettingsCommand.Execute(null);
        await Sta.Finished(vm.Details.RemoveSettingsCommand);
        Assert.Empty(dialogs.Requests);

        // Beginnt die Sitzung erst während der Rückfrage, bleibt das Spiel beim Schreiben.
        context.SessionList.Clear();
        context.RaiseHdrStateChanged();
        dialogs.Answers.Enqueue(true);
        dialogs.WhileConfirmOpen = () => context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [42]));
        await Remove(vm, Outlast);
        Assert.NotNull(context.Config.FindGame(Outlast));
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_Kept, "The Outlast Trials"), vm.StatusMessage);
        Assert.Empty(context.FakeIcons.ForgottenGames);
    });

    [Fact]
    public void RemoveSettings_failed_save_keeps_the_files() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        dialogs.WhileConfirmOpen = () => context.FailNextUpdate = new IOException("Datenträger voll");
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);

        Assert.NotNull(context.Config.FindGame(Outlast));
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.True(context.Files.FileExists(OwnIcon(Outlast)));
        Assert.Empty(context.FakeIcons.ForgottenGames);
    });

    [Fact]
    public void RemoveSettings_reinstalled_game_returns_as_new_with_hdr_auto_off() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        await Remove(vm, Outlast);

        context.ReportAfterRescan = ScanFinding(("1304930", "The Outlast Trials"), ("553850", "HELLDIVERS 2"));
        vm.RescanCommand.Execute(null);
        await Sta.Finished(vm.RescanCommand);

        var back = Item(vm, Outlast);
        Assert.True(back.IsNew);
        Assert.False(back.HdrAuto);
        Assert.True(back.IsInstalled);
        Assert.Empty(context.Config.FindGame(Outlast)!.DetectedExecutables);
    });
}
