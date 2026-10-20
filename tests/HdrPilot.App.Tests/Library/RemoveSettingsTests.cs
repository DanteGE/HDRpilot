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
        Assert.Equal([new ConfirmDetail("„HDR automatisch“ ist an"), new ConfirmDetail("2 gemerkte EXE\u2011Dateien")], request.Details);
        Assert.Equal(LibraryStrings.RemoveSettings_LossHeader, request.DetailsHeader);
        Assert.Equal("Laufwerk Q: ist nicht verbunden. Das Spiel ist vielleicht noch installiert.", request.Warning);
        Assert.Equal("Auch die 2 HDRpilot-Verknüpfungen löschen", request.OptionText);
        Assert.Equal(LibraryStrings.RemoveSettings_ShortcutHint, request.OptionHint);
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
        Assert.Equal([new ConfirmDetail(LibraryStrings.RemoveSettings_LossNothing)], request.Details);
    });

    [Fact]
    public void RemoveSettings_bulk_confirmation_names_every_game_and_all_missing_drives()
    {
        var outlast = Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, isNew: false, installed: false);
        var klippe = Game(GameSource.Steam, "9990140", "KLIPPE Demo", installed: false);
        var rolla = Game(GameSource.Steam, "3975830", "ROLLA", installed: false);

        var request = RemoveSettingsText.ForGames([outlast, klippe, rolla], [InstallTrace.MissingDrive("G:"), InstallTrace.None, InstallTrace.MissingDrive("F:")], shortcuts: 3);

        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_ConfirmTitleMany, 3), request.Title);
        Assert.Equal(
            [new ConfirmDetail("The Outlast Trials", "Laufwerk G: nicht verbunden · „HDR automatisch“ ist an"), new ConfirmDetail("KLIPPE Demo"), new ConfirmDetail("ROLLA", "Laufwerk F: nicht verbunden")],
            request.Details);
        Assert.Equal("Laufwerke F:, G: sind nicht verbunden. Spiele dort sind vielleicht noch installiert.", request.Warning);
        Assert.Equal("Auch die 3 HDRpilot-Verknüpfungen löschen", request.OptionText);
        Assert.Equal(LibraryStrings.RemoveSettings_ShortcutHint, request.OptionHint);
        Assert.True(request.IsDanger);
        Assert.Null(RemoveSettingsText.ForGames([klippe], [InstallTrace.None], shortcuts: 0).Warning);
        Assert.Equal(LibraryStrings.RemoveSettings_ShortcutHintOne, RemoveSettingsText.ForGames([klippe], [InstallTrace.None], shortcuts: 1).OptionHint);
    }

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

    [Fact]
    public void RemoveSettings_warns_when_an_exe_of_the_game_is_still_there() => Sta.Run(async () =>
    {
        // Prüfung (E90): Steam nicht gefunden oder ein Update ohne „vollständig installiert“ macht ein installiertes Spiel zu
        // „nicht installiert“; seine EXE ist dann noch da. Die Rückfrage sagt das, statt still alles zu entfernen.
        var context = SampleContext();
        context.Files.AddFile(@"C:\Program Files (x86)\Steam\steamapps\common\KlippeDemo\KlippeDemo.exe", "exe");
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Klippe);

        Assert.Equal(LibraryStrings.RemoveSettings_ProgramPresent, Assert.Single(dialogs.Requests).Warning);
        Assert.NotNull(context.Config.FindGame(Klippe)); // abgebrochen
    });

    [Fact]
    public void RemoveSettings_leftover_folder_without_an_exe_gets_no_warning() => Sta.Run(async () =>
    {
        // Prüfung (E90), echte Daten: Der Ordner von KLIPPE Demo hält nach dem Deinstallieren nur Spielstände, Einstellungen,
        // Protokolle und Absturzdaten (13 Dateien, keine EXE), die gemerkten EXEs sind weg, Steam führt es nicht mehr. Das ist
        // deinstalliert: keine Warnung „vielleicht noch installiert“.
        var context = SampleContext();
        const string Dir = @"C:\Program Files (x86)\Steam\steamapps\common\KlippeDemo";
        context.ConfigService.UpdateGame(Klippe, g => g.DetectedExecutables = [Dir + @"\Klippe\Binaries\Win64\KlippeDemo-Win64-Test.exe", Dir + @"\KlippeDemo.exe"]);
        foreach (var file in new[] { @"Engine\Saved\Config\Windows\Manifest.ini", @"Klippe\Saved\Config\Windows\Engine.ini", @"Klippe\Saved\Config\Windows\GameUserSettings.ini", @"Klippe\Saved\Logs\Klippe.log", @"Klippe\Saved\SaveGames\steam_autocloud.vdf", @"Klippe\.sentry-native\settings.dat" })
        {
            context.Files.AddFile(Path.Combine(Dir, file), "x");
        }

        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Klippe);

        var request = Assert.Single(dialogs.Requests);
        Assert.Null(request.Warning);
        Assert.Equal([new ConfirmDetail("2 gemerkte EXE\u2011Dateien")], request.Details);

        // Gegenprobe: Ist eine gemerkte EXE noch da, warnt sie.
        context.Files.AddFile(Dir + @"\Klippe\Binaries\Win64\KlippeDemo-Win64-Test.exe", "exe");
        await Remove(vm, Klippe);
        Assert.Equal(LibraryStrings.RemoveSettings_ProgramPresent, dialogs.Requests[^1].Warning);
    });

    [Fact]
    public void RemoveSettings_failed_save_after_the_change_deletes_nothing() => Sta.Run(async () =>
    {
        // Das Lambda nimmt das Spiel schon heraus, erst das Schreiben scheitert (Platte voll): Dann darf keine Datei fallen.
        var context = SampleContext();
        context.FakeShortcuts.Existing[Outlast] = [Shortcut("The Outlast Trials (HDR)")];
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        dialogs.OptionAnswers.Enqueue(true);
        dialogs.WhileConfirmOpen = () => context.Files.FailWriting(ConfigPath, new IOException("Datenträger voll"));
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);

        Assert.NotNull(context.Config.FindGame(Outlast));
        Assert.True(context.Files.FileExists(OwnIcon(Outlast)));
        Assert.Empty(context.FakeIcons.ForgottenGames);
        Assert.Empty(context.FakeShortcuts.Deleted);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Same(Item(vm, Outlast), vm.SelectedGame);
    });

    [Fact]
    public void RemoveSettings_of_the_last_game_of_a_source_selects_its_neighbour_in_all_games() => Sta.Run(async () =>
    {
        // Gefiltert nach „GOG“ mit nur diesem Spiel: Danach gibt es die Quelle nicht mehr, die Liste zeigt wieder alle, und
        // gewählt ist das Spiel, das in der Reihenfolge der Liste auf das entfernte folgt (nicht ein leerer Titelbereich).
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "553850", "HELLDIVERS 2", isNew: false),
            Game(GameSource.Gog, "1207658924", "Monkey Island 2", isNew: false, installed: false),
            Game(GameSource.Steam, "9990140", "KLIPPE Demo", isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false));
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Source == GameSource.Gog);
        vm.SelectedGame = Item(vm, "Gog:1207658924");
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;

        vm.RemoveSettingsCommand.Execute(null);
        await Sta.Finished(vm.RemoveSettingsCommand);

        Assert.Null(context.Config.FindGame("Gog:1207658924"));
        Assert.Null(vm.SelectedSource?.Source);
        Assert.Equal(3, vm.VisibleCount);
        Assert.Equal("KLIPPE Demo", vm.SelectedGame?.Name); // nach „Monkey Island 2“
        Assert.Same(vm.SelectedGame, vm.Details?.Item);
        Assert.Same(vm.SelectedGame, vm.Hero?.Item);
        Assert.Equal(1, follows); // das Fenster holt den Fokus einmal nach
    });

    [Fact]
    public void RemoveSettings_says_that_it_is_checking_while_the_drive_answers_slowly() => Sta.Run(async () =>
    {
        // Ein getrenntes Netzlaufwerk antwortet erst nach der Wartezeit: Bis zur Rückfrage steht in der Hinweiszeile, dass
        // geprüft wird; beim Öffnen der Rückfrage ist der Hinweis weg.
        var context = SampleContext();
        var slow = new HangingFileSystem(context.Files, @"Q:\");
        context.CleanupFiles = slow;
        var dialogs = new FakeDialogs();
        string? statusWhileAsking = "nicht gefragt";
        LibraryViewModel? asking = null;
        dialogs.WhileConfirmOpen = () => statusWhileAsking = asking!.StatusMessage;
        using var vm = new LibraryViewModel(context, dialogs);
        asking = vm;
        var shown = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.StatusMessage))
            {
                shown.Add(vm.StatusMessage);
            }
        };

        try
        {
            vm.SelectedGame = Item(vm, Outlast);
            vm.RemoveSettingsCommand.Execute(null);
            var checking = LibraryText.Format(LibraryStrings.RemoveSettings_Checking, "The Outlast Trials");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (vm.StatusMessage != checking && watch.Elapsed < UninstalledGameCleanup.CheckTimeout * 0.9)
            {
                await Task.Delay(20); // Zeitgeber laufen unter Last verspätet: warten, bis es so weit ist
            }

            Assert.InRange(watch.Elapsed, LibraryViewModel.CheckingDelay * 0.9, UninstalledGameCleanup.CheckTimeout);
            Assert.Equal(checking, vm.StatusMessage);
            Assert.Empty(dialogs.Requests);
            await Sta.Finished(vm.RemoveSettingsCommand);
        }
        finally
        {
            slow.Release();
        }

        Assert.Null(statusWhileAsking);
        Assert.Equal(LibraryStrings.RemoveSettings_Unchecked, Assert.Single(dialogs.Requests).Warning); // antwortet nicht: nicht zu klären
        Assert.Contains(LibraryText.Format(LibraryStrings.RemoveSettings_Checking, "The Outlast Trials"), shown);
    });

    [Fact]
    public void RemoveSettings_without_delay_shows_no_checking_hint() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        var shown = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.StatusMessage))
            {
                shown.Add(vm.StatusMessage);
            }
        };

        await Remove(vm, Klippe);

        Assert.Single(dialogs.Requests);
        Assert.Empty(shown); // schnell geprüft: kein Flackern in der Hinweiszeile
    });

    [Fact]
    public void RemoveSettings_reports_a_single_file_that_could_not_be_deleted_in_the_singular() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.Files.FailDeleting(OwnIcon(Outlast), new IOException("gesperrt"));
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);

        await Remove(vm, Outlast);

        Assert.Null(context.Config.FindGame(Outlast));
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_Done, "The Outlast Trials") + " " + LibraryStrings.RemoveSettings_FilesFailedOne, vm.StatusMessage);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Equal("Eine Datei ließ sich nicht löschen (siehe Protokoll).", LibraryText.FilesFailed(1));
        Assert.Equal("2 Dateien ließen sich nicht löschen (siehe Protokoll).", LibraryText.FilesFailed(2));
    });

    [Fact]
    public void RemoveSettings_losses_name_chosen_images_and_the_date_as_in_the_list()
    {
        // Eine Bildwahl ohne eigene Datei (anderes Launcher-Bild, erzeugtes) geht mit dem Eintrag verloren und wird genannt;
        // das Datum steht wie in Liste und Titelbereich.
        var game = Game(GameSource.Steam, "1304930", "The Outlast Trials", installed: false);
        game.Artwork = new GameArtworkChoice { Hero = "alt:library_hero_2.jpg", Logo = "gen" };
        var played = new DateTimeOffset(2026, 9, 12, 18, 15, 0, TimeSpan.Zero);
        game.LastPlayed = played;

        var request = RemoveSettingsText.ForGame(game, InstallTrace.None, 0);

        Assert.Equal(
            [new ConfirmDetail(LibraryText.Format(LibraryStrings.RemoveSettings_LossChosenMany, 2)), new ConfirmDetail(LibraryText.Format(LibraryStrings.RemoveSettings_LossLastPlayed, LibraryText.Format(LibraryStrings.When_Date, played.ToLocalTime())))],
            request.Details);
        game.Artwork = new GameArtworkChoice { Cover = "alt:cover.png" };
        game.LastPlayed = null;
        Assert.Equal([new ConfirmDetail(LibraryStrings.RemoveSettings_LossChosenOne)], RemoveSettingsText.ForGame(game, InstallTrace.None, 0).Details);
        Assert.False(LibraryCleanup.LossOf(game).IsEmpty);
    }

    [Fact]
    public void RemoveSettings_exe_texts_never_break_at_the_hyphen()
    {
        // Prüfung 2026-10-20: In der gedämpften Zeile unter einem Namen brach „EXE-|Dateien“ am Bindestrich um. Jeder Text
        // der Rückfragen mit „EXE-Datei“ nimmt den geschützten Bindestrich (U+2011, Segoe UI hat ihn).
        string[] texts =
        [
            LibraryStrings.RemoveSettings_LossDetectedOne, LibraryStrings.RemoveSettings_LossDetectedMany,
            LibraryStrings.RemoveSettings_LossExtraOne, LibraryStrings.RemoveSettings_LossExtraMany,
            LibraryStrings.RemoveSettings_LossExcludedOne, LibraryStrings.RemoveSettings_LossExcludedMany,
            LibraryStrings.RemoveSettings_ProgramPresent, LibraryStrings.RemoveAll_MarkProgramPresent,
        ];
        Assert.All(texts, t =>
        {
            Assert.Contains("EXE\u2011Datei", t, StringComparison.Ordinal);
            Assert.DoesNotContain("EXE-", t, StringComparison.Ordinal);
        });
        Assert.Equal("4 ausgeschlossene EXE\u2011Dateien", LibraryText.Format(LibraryStrings.RemoveSettings_LossExcludedMany, 4));
    }
}
