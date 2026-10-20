using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bibliothek „Showcase Hero“ ohne Fenster (E89): Ansicht und Vorschau gemerkt, Schublade nur im Regal, Titelbereich
/// folgt der Auswahl, Nit-Werte des Zielbildschirms (Skala bis zur Spitze), HDR-Moment nur beim Einschalten,
/// „Installationsordner öffnen“ und „Bild wählen…“.
/// </summary>
public sealed class ShowcaseViewModelTests
{
    private static TestLibraryContext SampleContext() => new(
        Game(GameSource.Steam, "1091500", "Cyberpunk 2077", hdrAuto: true, isNew: false),
        Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
        Game(GameSource.Gog, "1", "Baldur's Gate 3", isNew: false));

    /// <summary>Befehl ausführen und seine (mit den Attrappen sofort fertigen) Fortsetzungen abwarten.</summary>
    private static async Task Run(System.Windows.Input.ICommand command, object? parameter)
    {
        command.Execute(parameter);
        for (var i = 0; i < 5; i++)
        {
            await Task.Yield();
        }
    }

    private static DisplayNits Nits(double peak = 456, double fullFrame = 285, double min = 0.04) =>
        new(FakeHdrController.Primary, "MSI MAG 341CQP", peak, fullFrame, min, 10, HdrActive: true, PeakMeasuredWithHdr: true);

    [Fact]
    public void View_mode_defaults_to_list_and_the_choice_is_remembered() => Sta.Run(() =>
    {
        var context = SampleContext();
        using (var vm = new LibraryViewModel(context, new FakeDialogs()))
        {
            Assert.Equal(LibraryViewMode.List, vm.ViewMode);
            Assert.True(vm.IsListView);

            vm.IsGridView = true;
            Assert.Equal(LibraryViewMode.Grid, vm.ViewMode);
            Assert.Equal("Grid", context.Config.Settings.LibraryView);

            vm.ShowViewCommand.Execute(LibraryViewMode.Shelf);
            Assert.True(vm.IsShelfView);
            Assert.Equal("Shelf", context.Config.Settings.LibraryView);
        }

        using (var reopened = new LibraryViewModel(context, new FakeDialogs()))
        {
            Assert.Equal(LibraryViewMode.Shelf, reopened.ViewMode);
            reopened.ViewMode = LibraryViewMode.List;
            Assert.Null(context.Config.Settings.LibraryView); // Standard: nichts gespeichert
        }
    });

    [Fact]
    public void View_mode_for_screenshots_is_not_written() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var updates = context.Updates;

        vm.SetViewMode(LibraryViewMode.Shelf, persist: false);

        Assert.True(vm.IsShelfView);
        Assert.Equal(updates, context.Updates);
        Assert.Null(context.Config.Settings.LibraryView);
    });

    [Fact]
    public void Thumbnail_choice_of_the_list_is_remembered() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.True(vm.IsThumbnailImage);

        vm.IsThumbnailIcon = true;

        Assert.Equal(LibraryThumbnail.Icon, vm.Thumbnail);
        Assert.Equal("Icon", context.Config.Settings.LibraryThumbnail);
        vm.IsThumbnailImage = true;
        Assert.Null(context.Config.Settings.LibraryThumbnail);
    });

    [Fact]
    public void Details_drawer_opens_only_on_the_shelf_and_closes_on_view_change() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        vm.IsDetailsDrawerOpen = true;
        Assert.False(vm.IsDetailsDrawerOpen); // Liste: Details stehen ohnehin daneben

        vm.ViewMode = LibraryViewMode.Shelf;
        vm.ToggleDetailsDrawerCommand.Execute(null);
        Assert.True(vm.IsDetailsDrawerOpen);

        vm.ViewMode = LibraryViewMode.Grid;
        Assert.False(vm.IsDetailsDrawerOpen);
    });

    [Fact]
    public void Hero_follows_the_selection_and_falls_back_to_the_generated_image() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var artwork = new FakeArtworkService();
        artwork.WithHero.Add("Steam:1091500");
        context.Artwork = artwork;
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:1091500");
        await Task.Yield();
        Assert.NotNull(vm.Hero);
        Assert.Equal("Cyberpunk 2077", vm.Hero!.Name);
        Assert.Equal(ArtworkOrigin.Launcher, vm.Hero.Artwork?.Hero?.Origin);
        Assert.False(vm.Hero.IsGenerated);

        context.Dispatcher.RunDelayed();
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Gog:1");
        context.Dispatcher.RunDelayed(); // schnelles Blättern: Titelbild kurz verzögert
        await Task.Yield();
        Assert.Equal("Baldur's Gate 3", vm.Hero!.Name);
        Assert.True(vm.Hero.IsGenerated);
        Assert.Equal("BG", vm.Hero.Monogram);
    });

    [Fact]
    public void Nits_come_from_the_target_display_and_the_scale_ends_at_the_peak() => Sta.Run(() =>
    {
        var context = SampleContext();
        var luminance = new FakeLuminanceService();
        context.Luminance = luminance;
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.False(vm.Nits.HasValues);
        Assert.Equal(LibraryStrings.Nits_Unknown, vm.Nits.PeakText);

        luminance.Set(Nits(peak: 456, fullFrame: 285));

        Assert.True(vm.Nits.HasValues);
        Assert.Equal(456, vm.Nits.ScaleMaximum);
        Assert.NotEqual(1000, vm.Nits.ScaleMaximum); // nie eine feste Skala bis 1000 (Anmerkung 3)
        Assert.Contains("456", vm.Nits.PeakText, StringComparison.Ordinal);
        Assert.Contains("285", vm.Nits.FullFrameText, StringComparison.Ordinal);
        Assert.Contains("456", vm.Nits.ScaleEndText, StringComparison.Ordinal);
        Assert.Equal(285.0 / 456, vm.Nits.FullFrameFraction, 3);
        Assert.Contains("10", vm.Nits.DisplayName, StringComparison.Ordinal);
        Assert.Equal(vm.Nits.DisplayName.ToUpperInvariant(), vm.Nits.DisplayName);

        luminance.Set(Nits(peak: 1015, fullFrame: 600));
        Assert.Equal(1015, vm.Nits.ScaleMaximum);
    });

    [Fact]
    public void Hdr_moment_is_requested_only_when_hdr_turns_on() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var moments = 0;
        vm.HdrMomentRequested += (_, _) => moments++;

        context.RaiseHdrStateChanged();
        Assert.Equal(0, moments);

        context.Hdr[FakeHdrController.Primary].HdrOn = true;
        context.RaiseHdrStateChanged();
        Assert.Equal(1, moments);
        Assert.True(vm.ShowsHdrOn);
        Assert.Equal(LibraryStrings.Nits_HdrOn, vm.HdrShortText);

        context.RaiseHdrStateChanged(); // bleibt an: kein zweiter Moment
        Assert.Equal(1, moments);

        context.Hdr[FakeHdrController.Primary].HdrOn = false;
        context.RaiseHdrStateChanged();
        Assert.Equal(1, moments);
        Assert.Equal(LibraryStrings.Nits_HdrOff, vm.HdrShortText);
    });

    [Fact]
    public void Install_folder_opens_only_when_it_exists() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.Files.AddDirectory(@"D:\Games\Crow");
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");
        vm.OpenInstallFolderCommand.Execute(null);
        Assert.Equal([@"D:\Games\Crow"], context.OpenedFolders);

        vm.SelectedGame = vm.Games.Single(g => g.Key == "Gog:1");
        vm.OpenInstallFolderCommand.Execute(null);
        Assert.Single(context.OpenedFolders);
        Assert.Equal(LibraryText.Format(LibraryStrings.Folder_NotFound, "Baldur's Gate 3"), vm.StatusMessage);
    });

    [Fact]
    public void Choose_artwork_opens_the_picker_for_the_selected_game_and_kind() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.Artwork = new FakeArtworkService();
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");

        await Run(vm.ChooseArtworkCommand, "Cover");

        var picker = Assert.Single(dialogs.Pickers);
        Assert.Same(vm.SelectedGame, picker.Item);
        Assert.True(picker.IsCover);
        Assert.Contains("Dead Island 2", picker.Subtitle, StringComparison.Ordinal);
        Assert.Null(context.Config.FindGame("Epic:Crow")!.Artwork); // abgebrochen: nichts geschrieben
    });

    [Fact]
    public void Picker_offers_automatic_and_marks_the_current_choice() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame("Steam:1091500")!.Artwork = new GameArtworkChoice { Hero = "alt:hero_2.jpg" });
        var artwork = new FakeArtworkService();
        artwork.Candidates[ArtworkKind.Hero] =
        [
            new ArtworkCandidate("alt:hero_1.jpg", ArtworkKind.Hero, ArtworkOrigin.Alternative, "Steam 1", null, null, 3840, 1240, IsCurrent: false),
            new ArtworkCandidate("alt:hero_2.jpg", ArtworkKind.Hero, ArtworkOrigin.Alternative, "Steam 2", null, null, 1920, 620, IsCurrent: true),
        ];
        context.Artwork = artwork;
        var dialogs = new FakeDialogs { OnArtworkPicker = _ => false };
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:1091500");
        await Run(vm.ChooseArtworkCommand, null);
        var picker = Assert.Single(dialogs.Pickers);
        await picker.LoadAsync();

        Assert.Equal(3, picker.Candidates.Count);
        Assert.Null(picker.Candidates[0].Token); // „Automatisch“ zuerst
        Assert.Equal(LibraryStrings.Picker_Automatic, picker.Candidates[0].Label);
        Assert.Equal("alt:hero_2.jpg", picker.Selected!.Token);
        Assert.True(picker.Selected.IsSelected);
        Assert.False(string.IsNullOrEmpty(picker.Candidates[1].SizeText));

        picker.ResetCommand.Execute(null);
        Assert.Null(picker.Selected!.Token);
    });

    [Fact]
    public void Picker_apply_writes_the_choice_and_deletes_a_replaced_own_image() => Sta.Run(async () =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame("Gog:1")!.Artwork = new GameArtworkChoice { Hero = "custom:alt.png" });
        var artwork = new FakeArtworkService();
        artwork.Candidates[ArtworkKind.Hero] = [new ArtworkCandidate("alt:hero_1.jpg", ArtworkKind.Hero, ArtworkOrigin.Alternative, "GOG", null, null, 1920, 620, IsCurrent: false)];
        context.Artwork = artwork;
        ArtworkPickerViewModel? shown = null;
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Gog:1");

        // Der Dialog wählt erst nach dem Laden; hier vorab: Laden abwarten, dann wählen und übernehmen.
        dialogs.OnArtworkPicker = p =>
        {
            shown = p;
            p.LoadAsync().GetAwaiter().GetResult();
            p.Selected = p.Candidates.Single(c => c.Token == "alt:hero_1.jpg");
            return true;
        };
        await Run(vm.ChooseArtworkCommand, "Hero");

        Assert.NotNull(shown);
        Assert.Equal("alt:hero_1.jpg", context.Config.FindGame("Gog:1")!.Artwork!.Hero);
        Assert.Equal(["custom:alt.png"], artwork.Deleted);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_Applied, "Baldur's Gate 3"), vm.StatusMessage);
    });

    [Fact]
    public void Picker_own_file_is_saved_as_custom_image_and_non_images_are_rejected() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var artwork = new FakeArtworkService();
        context.Artwork = artwork;
        var dialogs = new FakeDialogs();
        dialogs.ArtworkFiles.Enqueue(@"C:\Gibt\Es\Nicht\bild.png");
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Epic:Crow");
        await Run(vm.ChooseArtworkCommand, "Logo");
        var picker = Assert.Single(dialogs.Pickers);
        await picker.LoadAsync();

        picker.PickFileCommand.Execute(null);
        for (var i = 0; i < 300 && !picker.HasMessage; i++)
        {
            await Task.Delay(10);
        }

        Assert.True(picker.HasMessage);
        Assert.Equal(LibraryStrings.Picker_NotAnImage, picker.Message);
        Assert.DoesNotContain(picker.Candidates, c => c.PendingFile is not null);
        Assert.Empty(artwork.Saved);
    });

    [Fact]
    public void Picker_own_file_chosen_while_the_list_still_loads_stays_chosen() => Sta.Run(async () =>
    {
        // Prüfung 2026-10-20 (phase10-ui): Die Liste lädt im Hintergrund; war die eigene Datei schneller, ersetzte die fertige
        // Liste sie bisher stillschweigend. Sie bleibt jetzt in der Liste und gewählt.
        using var art = new HdrPilot.App.Tests.Artwork.ArtworkFixture();
        var file = art.Save(Path.Combine(art.Root, "eigenes-titelbild.png"), HdrPilot.App.Tests.Artwork.ArtworkFixture.Cover(60, 90));
        var context = SampleContext();
        var artwork = new FakeArtworkService { PendingCandidates = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        artwork.Candidates[ArtworkKind.Hero] = [new ArtworkCandidate("alt:hero_1.jpg", ArtworkKind.Hero, ArtworkOrigin.Alternative, "Steam 1", null, null, 3840, 1240, IsCurrent: false)];
        context.Artwork = artwork;
        var dialogs = new FakeDialogs { OnArtworkPicker = _ => false };
        dialogs.ArtworkFiles.Enqueue(file);
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single(g => g.Key == "Steam:1091500");
        await Run(vm.ChooseArtworkCommand, null); // lädt die Liste (wartet auf PendingCandidates)
        var picker = Assert.Single(dialogs.Pickers);

        picker.PickFileCommand.Execute(null);
        for (var i = 0; i < 300 && !picker.Candidates.Any(c => c.PendingFile is not null); i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(file, picker.Selected?.PendingFile);
        artwork.PendingCandidates.SetResult();
        for (var i = 0; i < 300 && !picker.Candidates.Any(c => c.Token == "alt:hero_1.jpg"); i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(["Automatisch", "alt:hero_1.jpg", file], picker.Candidates.Select(c => c.PendingFile ?? c.Token ?? "Automatisch"));
        Assert.Equal(file, picker.Selected?.PendingFile);
        Assert.True(picker.Selected!.IsSelected);
        Assert.False(picker.IsLoading);
    });
}
