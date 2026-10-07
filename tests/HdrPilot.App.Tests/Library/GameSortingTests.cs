using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

public sealed class GameSortingTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 20, 0, 0, TimeSpan.FromHours(2));

    private static GameConfig Played(GameConfig game, int daysAgo)
    {
        game.LastPlayed = Day.AddDays(-daysAgo);
        return game;
    }

    /// <summary>Alphabetisch: Alan Wake 2, Baldur's Gate 3, Cyberpunk 2077, Dead Island 2, Elden Ring.</summary>
    private static TestLibraryContext SampleContext() => new(
        Played(Game(GameSource.Steam, "1", "Elden Ring", hdrAuto: true, isNew: false), daysAgo: 3),
        Game(GameSource.Epic, "2", "Alan Wake 2", isNew: false),
        Played(Game(GameSource.Gog, "3", "Cyberpunk 2077", hdrAuto: true, isNew: false), daysAgo: 0),
        Game(GameSource.Steam, "4", "Dead Island 2"),
        Played(Game(GameSource.Steam, "5", "Baldur's Gate 3", isNew: false), daysAgo: 10));

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    [Fact]
    public void Options_are_name_and_last_played_with_name_as_default() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());

        Assert.Equal([LibraryStrings.Sort_ByName, LibraryStrings.Sort_ByLastPlayed], vm.SortOptions.Select(o => o.ToString()));
        Assert.Equal(GameSortOrder.Name, vm.SortOrder);
        Assert.Same(vm.SortOptions[0], vm.SelectedSortOption);
        Assert.Equal(["Alan Wake 2", "Baldur's Gate 3", "Cyberpunk 2077", "Dead Island 2", "Elden Ring"], Visible(vm));
    });

    [Fact]
    public void Last_played_puts_recent_games_first_and_never_played_ones_after_them_by_name() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedSortOption = vm.SortOptions.Single(o => o.Order == GameSortOrder.LastPlayed);

        Assert.Equal(["Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Alan Wake 2", "Dead Island 2"], Visible(vm));
        Assert.Equal(GameSortOrder.LastPlayed, vm.SortOrder);
        Assert.Equal("LastPlayed", context.Config.Settings.LibrarySortOrder);

        vm.SortOrder = GameSortOrder.Name;
        Assert.Equal(["Alan Wake 2", "Baldur's Gate 3", "Cyberpunk 2077", "Dead Island 2", "Elden Ring"], Visible(vm));
        Assert.Null(context.Config.Settings.LibrarySortOrder); // Standard wird nicht gespeichert
    });

    [Fact]
    public void Sorting_works_together_with_search_and_filters() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SortOrder = GameSortOrder.LastPlayed;

        vm.SelectedSource = vm.SourceOptions.Single(o => o.Source == GameSource.Steam);
        Assert.Equal(["Elden Ring", "Baldur's Gate 3", "Dead Island 2"], Visible(vm));

        vm.SearchText = "2";
        vm.ApplySearch();
        Assert.Equal(["Dead Island 2"], Visible(vm));

        vm.ClearFilters();
        vm.OnlySelected = true;
        Assert.Equal(["Cyberpunk 2077", "Elden Ring"], Visible(vm));
        Assert.Equal(GameSortOrder.LastPlayed, vm.SortOrder); // Filter zurücksetzen lässt die Sortierung stehen
    });

    [Fact]
    public void Sort_order_is_restored_from_the_configuration() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.Settings.LibrarySortOrder = "lastplayed");
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.Equal(GameSortOrder.LastPlayed, vm.SortOrder);
        Assert.Equal("Cyberpunk 2077", Visible(vm)[0]);
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Quelle")]
    [InlineData("1")]
    [InlineData("Name, LastPlayed")]
    public void Unknown_or_missing_sort_order_means_name(string? stored)
    {
        Assert.Equal(GameSortOrder.Name, GameSorting.Parse(stored));
    }

    [Fact]
    public void Config_values_round_trip()
    {
        foreach (var order in Enum.GetValues<GameSortOrder>())
        {
            Assert.Equal(order, GameSorting.Parse(GameSorting.ToConfig(order)));
        }

        Assert.Null(GameSorting.ToConfig(GameSortOrder.Name));
        Assert.Equal(GameSortOrder.LastPlayed, GameSorting.Parse(" LASTPLAYED "));
    }

    [Fact]
    public void A_finished_game_moves_to_the_top_when_sorted_by_last_played() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SortOrder = GameSortOrder.LastPlayed;
        var selected = vm.Games.Single(g => g.Name == "Elden Ring");
        vm.SelectedGame = selected;

        // Wie die Sitzungslogik nach dem Spielen (RecordLastPlayed) oder das Einlesen aus Steam.
        context.ConfigService.Update(c => c.FindGame("Epic:2")!.LastPlayed = Day.AddHours(3));

        Assert.Equal(["Alan Wake 2", "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Dead Island 2"], Visible(vm));
        Assert.Same(selected, vm.SelectedGame); // Auswahl bleibt

        // Gleichzeitig ein neues Spiel und ein geändertes Datum: beides kommt an.
        context.ConfigService.Update(c =>
        {
            c.Games.Add(Played(Game(GameSource.Xbox, "6", "Forza Horizon 5"), daysAgo: 1));
            c.FindGame("Steam:4")!.LastPlayed = Day.AddHours(5);
        });
        Assert.Equal(["Dead Island 2", "Alan Wake 2", "Cyberpunk 2077", "Forza Horizon 5", "Elden Ring", "Baldur's Gate 3"], Visible(vm));
    });

    [Fact]
    public void Name_sort_inserts_new_games_alphabetically() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        context.ConfigService.Update(c => c.Games.Add(Played(Game(GameSource.Xbox, "6", "Bloodborne"), daysAgo: 0)));

        Assert.Equal(["Alan Wake 2", "Baldur's Gate 3", "Bloodborne", "Cyberpunk 2077", "Dead Island 2", "Elden Ring"], Visible(vm));
    });

    [Fact]
    public void Games_without_last_played_keep_name_order_and_ties_are_stable() => Sta.Run(() =>
    {
        var same = Day.AddDays(-2);
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "b", "Beta", isNew: false),
            Game(GameSource.Steam, "a", "Alpha", isNew: false),
            Game(GameSource.Epic, "z", "Zeta", isNew: false),
            Game(GameSource.Gog, "y", "Ypsilon", isNew: false));
        context.ConfigService.Update(c =>
        {
            c.FindGame("Epic:z")!.LastPlayed = same;
            c.FindGame("Gog:y")!.LastPlayed = same;
        });
        using var vm = new LibraryViewModel(context, new FakeDialogs()) { SortOrder = GameSortOrder.LastPlayed };

        Assert.Equal(["Ypsilon", "Zeta", "Alpha", "Beta"], Visible(vm));
    });

    [Fact]
    public void Failed_save_of_the_sort_order_keeps_the_choice_and_reports() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        context.FailNextUpdate = new IOException("Datenträger voll");

        vm.SortOrder = GameSortOrder.LastPlayed;

        Assert.Equal(GameSortOrder.LastPlayed, vm.SortOrder);
        Assert.Equal("Cyberpunk 2077", Visible(vm)[0]);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Null(context.Config.Settings.LibrarySortOrder);
    });
}
