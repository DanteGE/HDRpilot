using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

public sealed class LibraryViewModelTests
{
    private static TestLibraryContext SampleContext() => new(
        Game(GameSource.Steam, "1091500", "Cyberpunk 2077", hdrAuto: true, isNew: false),
        Game(GameSource.Steam, "1174180", "Red Dead Redemption 2"),
        Game(GameSource.Steam, "123", "Pokémon Arena"),
        Game(GameSource.Epic, "Crow", "Dead Island 2"),
        Game(GameSource.Manual, "abc", "Eigenes Spiel", hdrAuto: true, isNew: false, installed: false));

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    [Fact]
    public void Lists_games_sorted_with_counts_and_source_options() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        Assert.Equal(["Cyberpunk 2077", "Dead Island 2", "Eigenes Spiel", "Pokémon Arena", "Red Dead Redemption 2"], Visible(vm));
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(2, vm.HdrAutoCount);
        Assert.Equal(3, vm.NewCount);
        Assert.Equal(LibraryText.Games(5), vm.ResultText);
        Assert.Equal(["Alle", "Steam (3)", "Epic Games (1)", "Manuell (1)"], vm.SourceOptions.Select(o => o.AutomationName));
        Assert.Equal(5, vm.SourceOptions[0].Count);
        var manual = vm.Games.Single(g => g.IsManual);
        Assert.False(manual.IsInstalled);
    });

    [Fact]
    public void Search_is_debounced_and_ignores_case_and_accents() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SearchText = "POKEMON";
        Assert.Equal(5, Visible(vm).Count); // noch entprellt
        Assert.Equal(1, context.Dispatcher.PendingDelays);
        context.Dispatcher.RunDelayed();
        Assert.Equal(["Pokémon Arena"], Visible(vm));
        Assert.True(vm.IsFiltered);
        Assert.Equal(LibraryText.Format(LibraryStrings.List_ResultFiltered, 1, 5), vm.ResultText);

        vm.SearchText = "dead 2";
        context.Dispatcher.RunDelayed();
        Assert.Equal(["Dead Island 2", "Red Dead Redemption 2"], Visible(vm));

        vm.SearchText = "epic games";
        context.Dispatcher.RunDelayed();
        Assert.Equal(["Dead Island 2"], Visible(vm));

        vm.SearchText = "gibt es nicht";
        vm.ApplySearch();
        Assert.Empty(Visible(vm));
        Assert.True(vm.IsFilterEmpty);
    });

    [Fact]
    public void Source_filter_and_toggles_combine_and_reset() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        vm.SelectedSource = vm.SourceOptions.Single(o => o.Source == GameSource.Steam);
        Assert.Equal(["Cyberpunk 2077", "Pokémon Arena", "Red Dead Redemption 2"], Visible(vm));

        vm.OnlyNew = true;
        Assert.Equal(["Pokémon Arena", "Red Dead Redemption 2"], Visible(vm));

        vm.OnlyNew = false;
        vm.OnlySelected = true;
        Assert.Equal(["Cyberpunk 2077"], Visible(vm));

        vm.ClearFilters();
        Assert.Equal(5, Visible(vm).Count);
        Assert.False(vm.IsFiltered);
        Assert.Null(vm.SelectedSource!.Source);
    });

    [Fact]
    public void Hdr_auto_switch_writes_config_and_clears_new() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var rdr = vm.Games.Single(g => g.Key == "Steam:1174180");

        rdr.HdrAuto = true;

        var saved = context.Config.FindGame("Steam:1174180")!;
        Assert.True(saved.HdrAuto);
        Assert.False(saved.IsNew);
        Assert.False(rdr.IsNew);
        Assert.Equal(3, vm.HdrAutoCount);
        Assert.Equal(2, vm.NewCount);

        rdr.HdrAuto = false;
        Assert.False(context.Config.FindGame("Steam:1174180")!.HdrAuto);
        Assert.Equal(2, vm.HdrAutoCount);
    });

    [Fact]
    public void Selecting_a_game_marks_it_seen_and_shows_details() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var pokemon = vm.Games.Single(g => g.Name == "Pokémon Arena");

        vm.SelectedGame = pokemon;

        Assert.False(pokemon.IsNew);
        Assert.NotNull(vm.Details);
        Assert.Same(pokemon, vm.Details!.Item);
        Assert.Equal(2, vm.NewCount);

        context.Dispatcher.RunDelayed();
        Assert.False(context.Config.FindGame("Steam:123")!.IsNew);
    });

    [Fact]
    public void Browsing_new_games_saves_seen_marks_in_one_write() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var updates = context.Updates;

        // Mit den Pfeiltasten durch drei neue Spiele: keine Schreibvorgänge je Zeile.
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:1174180");
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:123");
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");
        Assert.Equal(updates, context.Updates);
        Assert.Equal(0, vm.NewCount);
        Assert.Equal(1, context.Dispatcher.PendingDelays);

        // Ein Abgleich mit einer Änderung von außen (etwa Einlesen oder Automatik) holt die Markierung nicht zurück.
        context.ConfigService.Update(c => c.FindGame("Steam:1091500")!.LastPlayed = DateTimeOffset.UnixEpoch);
        vm.SyncFromConfig();
        Assert.All(vm.Games.Where(g => !g.IsManual && g.Key != "Steam:1091500"), g => Assert.False(g.IsNew));

        context.Dispatcher.RunDelayed();
        Assert.Equal(updates + 1, context.Updates);
        Assert.DoesNotContain(context.Config.Games, g => g.IsNew);
        Assert.Equal(DateTimeOffset.UnixEpoch, vm.Games.Single(g => g.Key == "Steam:1091500").Config.LastPlayed); // danach abgeglichen
    });

    [Fact]
    public void Pending_seen_marks_are_saved_before_other_changes_and_on_close() => Sta.Run(() =>
    {
        var context = SampleContext();
        var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:123");
        vm.Games.Single(g => g.Key == "Steam:1091500").HdrAuto = false; // anderer Schreibvorgang: Markierung zuerst
        Assert.False(context.Config.FindGame("Steam:123")!.IsNew);
        Assert.Equal(0, context.Dispatcher.PendingDelays);

        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");
        vm.Dispose();
        Assert.False(context.Config.FindGame("Epic:Crow")!.IsNew);
    });

    [Fact]
    public void Refiltering_never_moves_the_selection_or_marks_games_seen() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var cyberpunk = vm.Games.Single(g => g.Key == "Steam:1091500");
        var pokemon = vm.Games.Single(g => g.Key == "Steam:123");

        // Wie eine mit dem aktuellen Element verbundene Liste: Neufiltern setzt das aktuelle Element auf den ersten Treffer.
        vm.GamesView.CurrentChanged += (_, _) => vm.SelectedGame = vm.GamesView.CurrentItem as GameItemViewModel;
        vm.GamesView.MoveCurrentTo(cyberpunk);
        Assert.Same(cyberpunk, vm.SelectedGame);

        vm.SearchText = "pokemon";
        context.Dispatcher.RunDelayed();

        Assert.Same(pokemon, vm.GamesView.CurrentItem); // die Sicht ist weitergerückt …
        Assert.Same(cyberpunk, vm.SelectedGame);         // … die Auswahl nicht
        Assert.Same(cyberpunk, vm.Details?.Item);
        Assert.True(pokemon.IsNew);
        Assert.True(context.Config.FindGame("Steam:123")!.IsNew);
    });

    [Fact]
    public void Main_window_list_does_not_follow_the_current_item_of_the_view()
    {
        // Eine ListCollectionView, die nicht die Standardsicht ist, würde die ListBox sonst mit dem aktuellen Element
        // verbinden: Jede Suche wählte dann den ersten Treffer aus und markierte ihn als angesehen.
        var xaml = System.Xml.Linq.XDocument.Load(SourceFiles.MainWindowXaml);
        var list = xaml.Descendants().Single(e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GameList");

        Assert.Equal("{Binding GamesView}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("False", (string?)list.Attribute("IsSynchronizedWithCurrentItem"));
    }

    [Fact]
    public void Source_option_names_follow_the_count()
    {
        var option = new SourceFilterOption(GameSource.Steam, 3);
        var changed = new List<string?>();
        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        option.Count = 4;

        Assert.Equal("Steam (4)", option.AutomationName);
        Assert.Contains(nameof(SourceFilterOption.AutomationName), changed);
        Assert.Equal(LibraryStrings.Filter_All, new SourceFilterOption(null, 9).AutomationName);
    }

    [Fact]
    public void Mark_all_seen_clears_every_new_flag() => Sta.Run(async () =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.True(vm.MarkAllSeenCommand.CanExecute(null));

        vm.MarkAllSeenCommand.Execute(null);
        await Task.Yield();

        Assert.DoesNotContain(context.Config.Games, g => g.IsNew);
        Assert.All(vm.Games, g => Assert.False(g.IsNew));
        Assert.Equal(0, vm.NewCount);
        Assert.False(vm.MarkAllSeenCommand.CanExecute(null));
        Assert.Equal(StatusKind.Success, vm.StatusKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.MarkAllSeen_Done, LibraryText.Games(3)), vm.StatusMessage);
    });

    [Fact]
    public void Outside_config_changes_add_update_and_remove_rows() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");
        context.Dispatcher.RunDelayed(); // „angesehen“ gespeichert

        // Etwa das Einlesen im Hintergrund: ein Spiel neu, eines weg, eines umbenannt.
        context.ConfigService.Update(c =>
        {
            c.Games.Add(Game(GameSource.Gog, "1", "Baldur's Gate 3"));
            c.Games.RemoveAll(g => g.Key == "Steam:123");
            c.FindGame("Steam:1174180")!.Name = "Red Dead Redemption II";
        });

        Assert.Equal(["Baldur's Gate 3", "Cyberpunk 2077", "Dead Island 2", "Eigenes Spiel", "Red Dead Redemption II"], Visible(vm));
        Assert.Equal("Epic:Crow", vm.SelectedGame?.Key);
        Assert.Contains(vm.SourceOptions, o => o.Source == GameSource.Gog);

        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.Key == "Epic:Crow"));
        Assert.Null(vm.SelectedGame);
        Assert.Null(vm.Details);
        Assert.DoesNotContain(vm.SourceOptions, o => o.Source == GameSource.Epic);
    });

    [Fact]
    public void Failed_save_restores_the_switch_and_reports() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var rdr = vm.Games.Single(g => g.Key == "Steam:1174180");
        context.FailNextUpdate = new IOException("Datenträger voll");

        rdr.HdrAuto = true;

        Assert.False(rdr.HdrAuto);
        Assert.False(context.Config.FindGame("Steam:1174180")!.HdrAuto);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Contains("Datenträger voll", vm.StatusMessage, StringComparison.Ordinal);
    });

    [Fact]
    public void Hdr_status_shows_target_display_and_reacts_to_changes() => Sta.Run(async () =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.False(vm.IsHdrOn);
        Assert.True(vm.CanToggleHdr);
        Assert.Equal("HDR aus · MAG 341C OLED", vm.HdrStatusText);
        Assert.Equal(LibraryStrings.Hdr_TurnOn, vm.HdrToggleText);

        context.Hdr[FakeHdrController.Primary].HdrOn = true;
        context.SessionList.Add(new SessionInfo("Steam:1091500", "Cyberpunk 2077", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UtcNow, [FakeHdrController.Primary], [42]));
        context.RaiseHdrStateChanged();

        Assert.True(vm.IsHdrOn);
        Assert.Equal("HDR an · MAG 341C OLED", vm.HdrStatusText);
        Assert.Equal(LibraryStrings.Hdr_TurnOff, vm.HdrToggleText);
        Assert.True(vm.Games.Single(g => g.Key == "Steam:1091500").IsRunning);
        Assert.Equal(LibraryText.Format(LibraryStrings.Hdr_ActiveSessions, "Cyberpunk 2077"), vm.ActiveSessionText);

        vm.ToggleHdrCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, context.ToggleHdrCalls);

        context.Hdr[FakeHdrController.Primary].Capable = false;
        context.Hdr[FakeHdrController.Primary].HdrOn = false;
        context.RaiseHdrStateChanged();
        Assert.False(vm.CanToggleHdr);
        Assert.False(vm.ToggleHdrCommand.CanExecute(null));
        Assert.Equal("Kein HDR möglich · MAG 341C OLED", vm.HdrStatusText);
    });

    [Fact]
    public void Changed_target_displays_update_status_and_global_option() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.Hdr.Displays.Add(new FakeDisplay(FakeHdrController.Second, "Odyssey G7"));
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:1091500");
        Assert.Equal("HDR aus · MAG 341C OLED", vm.HdrStatusText);

        // Etwa im Einstellungsfenster: anderer Zielbildschirm, ohne dass sich der HDR-Zustand ändert.
        context.ConfigService.Update(c => c.Settings.TargetDisplays = [FakeHdrController.Second]);

        Assert.Equal("HDR aus · Odyssey G7", vm.HdrStatusText);
        Assert.Equal(LibraryText.Format(LibraryStrings.Display_GlobalWith, "Odyssey G7"), vm.Details!.Displays[0].Label);
        Assert.True(vm.Details.SelectedDisplay!.IsGlobal);
    });

    [Fact]
    public void Pause_and_settings_go_to_the_app() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.IsPaused = true;
        Assert.True(context.IsPaused);
        Assert.True(vm.IsPaused);
        vm.IsPaused = false;
        Assert.False(context.IsPaused);

        vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, context.SettingsShown);
    });

    [Fact]
    public void Rescan_shows_scanner_errors_and_new_games() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.Files.AddDirectory(@"D:\Games\new");
        context.ReportAfterRescan = new LibraryScanReport(
        [
            new ScannerReport(
                new ScanResult
                {
                    Source = GameSource.Steam,
                    ScannerName = "Steam",
                    LauncherDetected = true,
                    Games =
                    [
                        new GameEntry { Source = GameSource.Steam, SourceId = "1091500", Name = "Cyberpunk 2077", InstallDir = @"D:\Games\1091500" },
                        new GameEntry { Source = GameSource.Steam, SourceId = "999", Name = "Neues Spiel", InstallDir = @"D:\Games\new" },
                    ],
                    Errors = [new ScanError("appmanifest_5.acf ist defekt", @"C:\Steam\steamapps\appmanifest_5.acf")],
                },
                TimeSpan.FromMilliseconds(5)),
            new ScannerReport(new ScanResult { Source = GameSource.Epic, ScannerName = "Epic", Errors = [new ScanError("Manifest unlesbar")] }, TimeSpan.Zero),
        ]);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.False(vm.HasScanErrors);

        vm.RescanCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(1, context.RescanCalls);
        Assert.False(vm.IsScanning);
        Assert.True(vm.HasScanErrors);
        Assert.Equal(LibraryText.Format(LibraryStrings.ScanErrors_HeaderMany, 2), vm.ScanErrorsHeader);
        Assert.Equal(["Steam: appmanifest_5.acf ist defekt", "Epic Games: Manifest unlesbar"], vm.ScanErrors.Select(e => e.ToString()));
        var fresh = vm.Games.Single(g => g.Key == "Steam:999");
        Assert.True(fresh.IsNew);
        Assert.False(fresh.HdrAuto);
    });

    [Fact]
    public void Closing_unsubscribes_and_releases_the_context() => Sta.Run(() =>
    {
        var context = SampleContext();
        var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Equal(1, context.ConfigChangedSubscribers);
        Assert.Equal(1, context.HdrStateChangedSubscribers);
        Assert.Equal(1, context.ScanStateChangedSubscribers);
        vm.SearchText = "x";
        Assert.Equal(1, context.Dispatcher.PendingDelays);

        vm.Dispose();

        Assert.Equal(0, context.ConfigChangedSubscribers);
        Assert.Equal(0, context.HdrStateChangedSubscribers);
        Assert.Equal(0, context.ScanStateChangedSubscribers);
        Assert.Equal(0, context.Dispatcher.PendingDelays); // kein Zeitgeber hält das Fenster fest
        Assert.True(context.Disposed);
    });

    [Fact]
    public void Rows_ask_for_icons_only_when_shown() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Empty(context.FakeIcons.Requested);

        _ = vm.Games[0].Icon;
        _ = vm.Games[0].Icon;

        Assert.Equal([vm.Games[0].Key], context.FakeIcons.Requested);
    });

    [Fact]
    public void Candidates_from_search_folders_are_only_added_on_request() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Bekannt", installDir: @"E:\Spiele\Bekannt"));
        context.Files
            .AddFile(@"E:\Spiele\Bekannt\bekannt.exe", "x")
            .AddFile(@"E:\Spiele\SWAT 4\Content\System\Swat4.exe", "x")
            .AddFile(@"E:\Spiele\Hades\Hades.exe", "x")
            .AddDirectory(@"E:\Spiele\Leer");
        context.ConfigService.Update(c => c.Settings.SearchFolders.Add(@"E:\Spiele"));
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        await vm.RefreshCandidatesAsync();

        Assert.True(vm.HasCandidates);
        Assert.Equal(["Hades", "SWAT 4"], vm.Candidates.Select(c => c.Name));
        Assert.Single(context.Config.Games); // nichts ungefragt übernommen

        vm.AddCandidateCommand.Execute(vm.Candidates.Single(c => c.Name == "SWAT 4"));
        await Task.Yield();

        var added = context.Config.Games.Single(g => g.IsManual);
        Assert.Equal("SWAT 4", added.Name);
        Assert.Equal(@"E:\Spiele\SWAT 4", added.InstallDir);
        Assert.True(added.HdrAuto);
        Assert.False(added.IsNew);
        Assert.Contains(@"E:\Spiele\SWAT 4\Content\System\Swat4.exe", added.KnownExecutables);
        Assert.Equal(@"E:\Spiele\SWAT 4\Content\System\Swat4.exe", added.LaunchCommand);
        Assert.Equal(added.Key, vm.SelectedGame?.Key);
        Assert.Equal(["Hades"], vm.Candidates.Select(c => c.Name));
    });

    [Fact]
    public void Candidate_in_a_very_wide_folder_needs_confirmation() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext();
        context.Env.Folders[Environment.SpecialFolder.ApplicationData] = @"C:\Users\Test\AppData\Roaming";
        context.Files.AddFile(@"C:\Users\Test\AppData\Tool\tool.exe", "x");
        context.ConfigService.Update(c => c.Settings.SearchFolders.Add(@"C:\Users\Test"));
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        await vm.RefreshCandidatesAsync();
        var appData = vm.Candidates.Single(c => c.Name == "AppData");

        dialogs.Answers.Enqueue(false);
        vm.AddCandidateCommand.Execute(appData);
        await Task.Yield();
        Assert.Equal(LibraryStrings.WideFolder_Title, Assert.Single(dialogs.Confirms).Title);
        Assert.Empty(context.Config.Games);
        Assert.Contains(appData, vm.Candidates); // bleibt als Vorschlag stehen

        dialogs.Answers.Enqueue(true);
        vm.AddCandidateCommand.Execute(appData);
        await Task.Yield();
        Assert.Equal(@"C:\Users\Test\AppData", Assert.Single(context.Config.Games).InstallDir);
    });

    [Fact]
    public void Candidate_whose_folder_is_gone_reports_instead_of_adding() => Sta.Run(async () =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var games = context.Config.Games.Count;

        vm.AddCandidateCommand.Execute(new CandidateItemViewModel(new GameCandidate { Name = "Weg", Folder = @"E:\Spiele\Weg", Executables = [@"E:\Spiele\Weg\Weg.exe"] }));
        await Task.Yield();

        Assert.Equal(games, context.Config.Games.Count);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Equal(LibraryStrings.AddGame_ErrorNotFound, vm.StatusMessage);
    });

    [Fact]
    public void Add_game_dialog_adds_a_manual_game_with_hdr_auto_on() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.Files.AddFile(@"D:\Indie\Celeste\Celeste.exe", "x");
        var dialogs = new FakeDialogs
        {
            OnAddGame = dialog =>
            {
                dialog.Path = @"D:\Indie\Celeste\Celeste.exe";
                dialog.Name = "Celeste (Steamdeck-Kopie)";
                return dialog.TryConfirm();
            },
        };
        using var vm = new LibraryViewModel(context, dialogs);
        vm.OnlyNew = true;

        vm.AddGameCommand.Execute(null);
        await Task.Yield();

        var added = context.Config.Games.Single(g => g.Name == "Celeste (Steamdeck-Kopie)");
        Assert.Equal(GameSource.Manual, added.Source);
        Assert.True(added.HdrAuto);
        Assert.False(added.IsNew);
        Assert.Equal(@"D:\Indie\Celeste", added.InstallDir);
        Assert.Equal(@"D:\Indie\Celeste\Celeste.exe", added.LaunchCommand);
        Assert.Equal(added.Key, vm.SelectedGame?.Key);
        Assert.False(vm.OnlyNew); // Filter zurückgesetzt, damit das neue Spiel sichtbar ist
        Assert.Contains(vm.GamesView.Cast<GameItemViewModel>(), g => g.Key == added.Key);
    });
}
