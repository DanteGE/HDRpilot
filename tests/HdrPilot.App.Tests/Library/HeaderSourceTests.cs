using System.ComponentModel;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kopf der Bibliothek folgt der Quelle (Wunsch des Nutzers 2026-10-22): Titel „Alle Spiele“, „Steam-Spiele“ …, Kennzahlen
/// nur der Auswahl („641 Spiele“, mit Filtern oder Suche „36 von 641 Spielen“), ausgeblendete nicht installierte Spiele
/// zählen nie, das Quellenmenü wählt wie das Feld „Quelle“ der Werkzeugleiste. Nur das ViewModel, ohne Fenster.
/// </summary>
public sealed class HeaderSourceTests
{
    /// <summary>
    /// Steam 5 (2 mit „HDR automatisch“, davon eins neu; 1 weiteres neu; 1 nicht installiert), GOG 3 (1 automatisch, 2 neu),
    /// Epic 1 (neu), eigenes Spiel 1.
    /// </summary>
    private static TestLibraryContext SampleContext() => new(
        Game(GameSource.Steam, "1", "Alan Wake 2", hdrAuto: true, isNew: true),
        Game(GameSource.Steam, "2", "Bernsteinhafen", hdrAuto: true, isNew: false),
        Game(GameSource.Steam, "3", "Celeste", isNew: true),
        Game(GameSource.Steam, "4", "Dredge", isNew: false),
        Game(GameSource.Steam, "5", "Elden Ring", isNew: false, installed: false),
        Game(GameSource.Gog, "6", "Frostpunk", hdrAuto: true, isNew: true),
        Game(GameSource.Gog, "7", "Gothic", isNew: true),
        Game(GameSource.Gog, "8", "Hades", isNew: false),
        Game(GameSource.Epic, "9", "Inside", isNew: true),
        Game(GameSource.Manual, "10", "Jusant", isNew: false));

    private static SourceFilterOption Option(LibraryViewModel vm, GameSource? source) => vm.SourceOptions.Single(o => o.Source == source);

    private static (string Title, string Count, int Auto, int New) Header(LibraryViewModel vm) =>
        (vm.HeaderTitle, vm.HeaderCountText, vm.HeaderHdrAutoCount, vm.HeaderNewCount);

    private static List<string?> Track(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    [Theory]
    [InlineData(null, "Alle Spiele")]
    [InlineData(GameSource.Steam, "Steam-Spiele")]
    [InlineData(GameSource.Gog, "GOG-Spiele")]
    [InlineData(GameSource.Epic, "Epic-Spiele")]
    [InlineData(GameSource.Xbox, "Xbox-Spiele")]
    [InlineData(GameSource.Ubisoft, "Ubisoft-Spiele")]
    [InlineData(GameSource.EA, "EA-Spiele")]
    [InlineData(GameSource.BattleNet, "Battle.net-Spiele")]
    [InlineData(GameSource.Quiver, "Quiver-Spiele")]
    [InlineData(GameSource.Manual, "Eigene Spiele")]
    public void Every_source_has_its_title_as_the_user_named_them(GameSource? source, string expected)
    {
        Assert.Equal(expected, LibraryText.SourceTitle(source));
        Assert.Equal(expected, new SourceFilterOption(source, 3).Title);
        Assert.Equal(expected + " (3)", new SourceFilterOption(source, 3).MenuName);
    }

    [Fact]
    public void Every_source_of_the_enum_has_a_title_of_its_own()
    {
        var titles = Enum.GetValues<GameSource>().Select(s => LibraryText.SourceTitle(s)).Append(LibraryText.SourceTitle(null)).ToList();
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
        Assert.All(titles, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal("SPIELEBIBLIOTHEK", LibraryStrings.Header_Overline);
    }

    [Fact]
    public void Title_and_stats_follow_the_selected_source() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        Assert.Equal(("Alle Spiele", "10 Spiele", 3, 5), Header(vm));
        Assert.Equal("Quelle: Alle Spiele", vm.HeaderSourceName);
        Assert.False(vm.IsHeaderFiltered);

        vm.SelectedSource = Option(vm, GameSource.Steam);
        Assert.Equal(("Steam-Spiele", "5 Spiele", 2, 2), Header(vm));
        Assert.Equal("Quelle: Steam-Spiele", vm.HeaderSourceName);
        Assert.False(vm.IsHeaderFiltered); // die Quelle ist der Titel, kein Filter für den Kopf

        vm.SelectedSource = Option(vm, GameSource.Gog);
        Assert.Equal(("GOG-Spiele", "3 Spiele", 1, 2), Header(vm));

        vm.SelectedSource = Option(vm, GameSource.Manual);
        Assert.Equal(("Eigene Spiele", "1 Spiel", 0, 0), Header(vm));
        Assert.Equal(LibraryText.Format(LibraryStrings.Stats_HdrAuto, 0), vm.HeaderHdrAutoText);

        vm.SelectedSource = null; // wie die Auswahlliste beim Neuaufbau: zurück zu „Alle“
        Assert.Equal(("Alle Spiele", "10 Spiele", 3, 5), Header(vm));
    });

    [Fact]
    public void Filters_and_search_count_the_visible_selection_relative_to_the_whole_source() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());
        vm.SelectedSource = Option(vm, GameSource.Steam);

        vm.OnlySelected = true;
        Assert.True(vm.IsHeaderFiltered);
        Assert.Equal(("Steam-Spiele", "2 von 5 Spielen", 2, 1), Header(vm));
        Assert.Equal((2, 5), (vm.HeaderShownCount, vm.HeaderSourceCount));

        vm.OnlySelected = false;
        vm.OnlyNew = true;
        Assert.Equal(("Steam-Spiele", "2 von 5 Spielen", 1, 2), Header(vm));

        vm.OnlyNew = false;
        vm.SearchText = "ce";
        vm.ApplySearch();
        Assert.Equal(("Steam-Spiele", "1 von 5 Spielen", 0, 1), Header(vm)); // nur „Celeste“

        vm.SearchText = "xqz";
        vm.ApplySearch();
        Assert.Equal(("Steam-Spiele", "0 von 5 Spielen", 0, 0), Header(vm));

        vm.ClearFilters(); // setzt auch die Quelle zurück
        Assert.Equal(("Alle Spiele", "10 Spiele", 3, 5), Header(vm));
        Assert.False(vm.IsHeaderFiltered);

        // Quelle mit genau einem Spiel: „… von 1 Spiel“ statt „von 1 Spielen“.
        vm.SelectedSource = Option(vm, GameSource.Epic);
        vm.OnlyNew = true;
        Assert.Equal("1 von 1 Spiel", vm.HeaderCountText);
        vm.OnlySelected = true;
        Assert.Equal("0 von 1 Spiel", vm.HeaderCountText);
    });

    [Fact]
    public void Header_count_text_for_all_cases()
    {
        Assert.Equal("641 Spiele", LibraryViewModel.HeaderCount(filtered: false, 641, 641));
        Assert.Equal("1 Spiel", LibraryViewModel.HeaderCount(filtered: false, 1, 1));
        Assert.Equal("36 von 641 Spielen", LibraryViewModel.HeaderCount(filtered: true, 36, 641));
        Assert.Equal("0 von 1 Spiel", LibraryViewModel.HeaderCount(filtered: true, 0, 1));
        Assert.Equal("0 von 0 Spielen", LibraryViewModel.HeaderCount(filtered: true, 0, 0));
    }

    [Fact]
    public void Hidden_uninstalled_games_never_count_in_title_stats_or_menu() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.Settings.HideUninstalledGames = true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.Equal(("Alle Spiele", "9 Spiele", 3, 5), Header(vm));
        Assert.Equal(4, Option(vm, GameSource.Steam).Count); // „Elden Ring“ ist nicht installiert
        Assert.Equal("Steam-Spiele (4)", Option(vm, GameSource.Steam).MenuName);

        vm.SelectedSource = Option(vm, GameSource.Steam);
        Assert.Equal(("Steam-Spiele", "4 Spiele", 2, 2), Header(vm));
        vm.OnlySelected = true;
        Assert.Equal("2 von 4 Spielen", vm.HeaderCountText);

        // Gegenprobe: eingeblendet zählt er wieder mit.
        context.ConfigService.Update(c => c.Settings.HideUninstalledGames = false);
        Assert.Equal("2 von 5 Spielen", vm.HeaderCountText);
        vm.OnlySelected = false;
        Assert.Equal("5 Spiele", vm.HeaderCountText);
    });

    [Fact]
    public void Source_menu_selects_like_the_toolbar_and_ticks_exactly_the_chosen_source() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());
        Assert.Equal([true, false, false, false, false], vm.SourceOptions.Select(o => o.IsSelected));
        Assert.Equal(LibraryStrings.SourceMenu_Selected, vm.SourceOptions[0].MenuStatus);
        var changes = Track(vm);

        var gog = Option(vm, GameSource.Gog);
        vm.SelectSourceCommand.Execute(gog);
        Assert.Same(gog, vm.SelectedSource); // dieselbe Wahl wie das Feld „Quelle“ (gebunden an SelectedSource)
        Assert.Equal([gog], vm.SourceOptions.Where(o => o.IsSelected));
        Assert.Equal(string.Empty, vm.SourceOptions[0].MenuStatus);
        Assert.Contains(nameof(LibraryViewModel.SelectedSource), changes);
        Assert.Contains(nameof(LibraryViewModel.HeaderTitle), changes);
        Assert.Contains(gog.Label, vm.ToolbarSummary, StringComparison.Ordinal); // Kapsel und Ansage nennen die Quelle

        // Eine Wahl über die Leiste setzt den Haken im Menü ebenso.
        vm.SelectedSource = Option(vm, GameSource.Steam);
        Assert.Equal([Option(vm, GameSource.Steam)], vm.SourceOptions.Where(o => o.IsSelected));

        // Fremde Einträge und anderes wählen nichts.
        vm.SelectSourceCommand.Execute(new SourceFilterOption(GameSource.Gog, 3));
        vm.SelectSourceCommand.Execute("Gog");
        Assert.Equal(GameSource.Steam, vm.SelectedSource!.Source);

        vm.ClearFilters();
        Assert.Equal([vm.SourceOptions[0]], vm.SourceOptions.Where(o => o.IsSelected));
        Assert.Equal("Alle Spiele", vm.HeaderTitle);
    });

    [Fact]
    public void Stats_follow_hdr_automatic_and_seen_live() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());
        vm.SelectedSource = Option(vm, GameSource.Gog);
        Assert.Equal(("GOG-Spiele", "3 Spiele", 1, 2), Header(vm));

        vm.Games.Single(g => g.Name == "Hades").HdrAuto = true; // Schalter in der Zeile
        Assert.Equal(2, vm.HeaderHdrAutoCount);
        Assert.Equal(LibraryText.Format(LibraryStrings.Stats_HdrAuto, 2), vm.HeaderHdrAutoText);

        vm.SelectedGame = vm.Games.Single(g => g.Name == "Gothic"); // selbst gewählt gilt als angesehen
        Assert.Equal(1, vm.HeaderNewCount);
        Assert.Equal(LibraryText.Format(LibraryStrings.Stats_New, 1), vm.HeaderNewText);

        // Die Zähler der Filterknöpfe zählen weiter alle Spiele.
        Assert.Equal(4, vm.HdrAutoCount);
    });

    [Fact]
    public void Title_falls_back_to_all_games_when_the_last_game_of_the_source_leaves() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedSource = Option(vm, GameSource.Epic);
        Assert.Equal("Epic-Spiele", vm.HeaderTitle);

        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.Source == GameSource.Epic));

        Assert.Null(vm.SelectedSource!.Source);
        Assert.Equal(("Alle Spiele", "9 Spiele", 3, 4), Header(vm));
        Assert.True(vm.SourceOptions[0].IsSelected);
    });
}
