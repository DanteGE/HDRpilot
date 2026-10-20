using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Auswahl beim Filtern (Entscheidung des Nutzers 2026-10-19, G10.5, ersetzt die Regel aus Phase 4, nach der Neufiltern die
/// Auswahl nie verschob): Fällt das gewählte Spiel durch Suche oder Filter aus der Liste, zeigen Titelbereich und Details
/// leise den ersten Treffer (nicht als angesehen markiert, nichts gespeichert), ohne Treffer kein Spiel; steht das vorherige
/// Spiel wieder in der Liste, kehrt es zurück. Mit Suche, Quelle, den drei Filtern der Leiste, Sortierung, Wechsel der
/// Ansicht und Zurücksetzen (✕ der Kapsel, „Filter zurücksetzen“). Den Fokus prüft
/// <see cref="SelectionFollowsFilterWindowTests"/>.
/// </summary>
public sealed class SelectionFollowsFilterTests
{
    private const string Cyberpunk = "Steam:1091500";
    private const string RedDead = "Steam:1174180";
    private const string Pokemon = "Steam:123";
    private const string DeadIsland = "Epic:Crow";

    /// <summary>
    /// Nach Name: Cyberpunk 2077, Dead Island 2, Eigenes Spiel, Pokémon Arena, Red Dead Redemption 2. Neu sind Red Dead,
    /// Pokémon und Dead Island, „HDR automatisch“ haben Cyberpunk und das eigene Spiel, HDR-fähig sind Red Dead und Dead Island.
    /// Zuletzt gespielt: Pokémon, dann Red Dead, die übrigen nie.
    /// </summary>
    private static TestLibraryContext SampleContext()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", hdrAuto: true, isNew: false),
            Game(GameSource.Steam, "1174180", "Red Dead Redemption 2"),
            Game(GameSource.Steam, "123", "Pokémon Arena"),
            Game(GameSource.Epic, "Crow", "Dead Island 2"),
            Game(GameSource.Manual, "abc", "Eigenes Spiel", hdrAuto: true, isNew: false, installed: false));
        context.ConfigService.Update(c =>
        {
            c.FindGame(Pokemon)!.LastPlayed = new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero);
            c.FindGame(RedDead)!.LastPlayed = new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);
        });
        context.HdrSupport[RedDead] = HdrSupportStatus.Native;
        context.HdrSupport[DeadIsland] = HdrSupportStatus.Limited;
        return context;
    }

    private static GameItemViewModel Item(LibraryViewModel vm, string key) => vm.Games.Single(g => g.Key == key);

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    private static SourceFilterOption Source(LibraryViewModel vm, GameSource? source) => vm.SourceOptions.Single(o => o.Source == source);

    private static void Search(LibraryViewModel vm, string text)
    {
        vm.SearchText = text;
        vm.ApplySearch();
    }

    /// <summary>Liste, Titelbereich und Details zeigen dasselbe Spiel (null: keins).</summary>
    private static void AssertShown(LibraryViewModel vm, GameItemViewModel? item)
    {
        Assert.Same(item, vm.SelectedGame);
        Assert.Same(item, vm.Details?.Item);
        Assert.Same(item, vm.Hero?.Item);
        Assert.Equal(item is not null, vm.HasDetails);
        Assert.Equal(item is not null, vm.HasHero);
    }

    [Fact]
    public void Selection_stays_while_it_is_still_listed() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        var deadIsland = Item(vm, DeadIsland);
        vm.SelectedGame = deadIsland;
        var details = vm.Details;
        var hero = vm.Hero;

        // Suche, Quelle, Filter, Sortierung und Ansicht, nach denen Dead Island immer noch in der Liste steht.
        Search(vm, "dead");
        Assert.Equal(["Dead Island 2", "Red Dead Redemption 2"], Visible(vm));
        vm.SortOrder = GameSortOrder.LastPlayed;
        Assert.Equal(["Red Dead Redemption 2", "Dead Island 2"], Visible(vm));
        vm.ToggleFilter(ToolbarFilter.HdrCapable);
        vm.SelectedSource = Source(vm, GameSource.Epic);
        vm.SetViewMode(LibraryViewMode.Grid, persist: false);
        Assert.Equal(["Dead Island 2"], Visible(vm));

        // Nicht neu gewählt: dieselben Details und derselbe Titelbereich, keine Meldung ans Fenster.
        AssertShown(vm, deadIsland);
        Assert.Same(details, vm.Details);
        Assert.Same(hero, vm.Hero);
        Assert.Equal(0, follows);
    });

    [Fact]
    public void Hidden_selection_moves_quietly_to_the_first_match() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        vm.SelectedGame = Item(vm, Cyberpunk);
        context.Dispatcher.RunDelayed();
        var updates = context.Updates;

        vm.SearchText = "dead";
        context.Dispatcher.RunDelayed(); // entprellt

        // Erster Treffer in der Reihenfolge der Liste: Titelbereich und Details folgen, das Fenster wird benachrichtigt.
        var deadIsland = Item(vm, DeadIsland);
        AssertShown(vm, deadIsland);
        Assert.Equal(1, follows);

        // Leise: nicht als angesehen markiert, auch nicht verzögert, und nichts gespeichert.
        context.Dispatcher.RunDelayed();
        Assert.True(deadIsland.IsNew);
        Assert.Equal(3, vm.NewCount);
        Assert.True(context.Config.FindGame(DeadIsland)!.IsNew);
        Assert.Equal(updates, context.Updates);
    });

    [Fact]
    public void Previous_selection_returns_when_it_is_listed_again() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        var cyberpunk = Item(vm, Cyberpunk);
        vm.SelectedGame = cyberpunk;
        context.Dispatcher.RunDelayed();
        var updates = context.Updates;

        Search(vm, "dead");
        AssertShown(vm, Item(vm, DeadIsland));

        // Ein anderer Treffer bleibt, solange Cyberpunk fehlt …
        Search(vm, "red dead");
        AssertShown(vm, Item(vm, RedDead));
        Assert.Equal(2, follows);

        // … und Cyberpunk kehrt zurück, sobald es wieder in der Liste steht, auch noch gefiltert.
        Search(vm, "2");
        Assert.Equal(["Cyberpunk 2077", "Dead Island 2", "Red Dead Redemption 2"], Visible(vm));
        AssertShown(vm, cyberpunk);
        Assert.Equal(3, follows);

        // Danach bleibt es (kein verdrängtes Spiel mehr), auch ohne Filter.
        Search(vm, string.Empty);
        AssertShown(vm, cyberpunk);
        Assert.Equal(3, follows);
        Assert.True(Item(vm, RedDead).IsNew);
        Assert.Equal(updates, context.Updates);
    });

    [Fact]
    public void A_game_chosen_while_filtered_replaces_the_hidden_one() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = Item(vm, Cyberpunk);
        Search(vm, "dead");

        // Selbst gewählt (Klick, Pfeiltaste): gilt als angesehen, und Cyberpunk kehrt nicht mehr zurück.
        var redDead = Item(vm, RedDead);
        vm.SelectedGame = redDead;
        Assert.False(redDead.IsNew);
        vm.ClearFilters();
        AssertShown(vm, redDead);

        // Nacheinander zwei Treffer gezeigt: Zurück kommt das selbst gewählte Spiel, nicht der erste Treffer.
        Search(vm, "pokemon");
        AssertShown(vm, Item(vm, Pokemon));
        Search(vm, "island");
        AssertShown(vm, Item(vm, DeadIsland));
        Search(vm, string.Empty);
        AssertShown(vm, redDead);

        // Ebenso zählt eine Wahl ohne Merken als angesehen (Bildschirmfotos, Prüfungen).
        vm.ToggleFilter(ToolbarFilter.HdrAuto);
        AssertShown(vm, Item(vm, Cyberpunk));
        var own = Item(vm, "Manual:abc");
        vm.SelectQuietly(own);
        vm.ToggleFilter(ToolbarFilter.HdrAuto);
        AssertShown(vm, own);
    });

    [Fact]
    public void Without_a_match_no_game_is_selected_and_the_notice_shows() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        var cyberpunk = Item(vm, Cyberpunk);
        vm.SelectedGame = cyberpunk;
        context.Dispatcher.RunDelayed();
        var updates = context.Updates;

        Search(vm, "gibt es nicht");
        Assert.Empty(Visible(vm));
        AssertShown(vm, null);
        Assert.True(vm.IsFilterEmpty);
        Assert.Equal("Keine Spiele für diese Auswahl", LibraryStrings.List_NoMatchTitle);
        Assert.Equal(1, follows);

        // Noch ein Filter ohne Treffer: nichts ändert sich.
        vm.ToggleFilter(ToolbarFilter.New);
        AssertShown(vm, null);
        Assert.Equal(1, follows);

        // Wieder Treffer, aber ohne Cyberpunk: der erste, leise.
        Search(vm, "pokemon");
        var pokemon = Item(vm, Pokemon);
        AssertShown(vm, pokemon);
        Assert.True(pokemon.IsNew);

        // Filter aufgehoben: Cyberpunk ist wieder da.
        vm.ClearFilters();
        AssertShown(vm, cyberpunk);
        Assert.False(vm.IsFilterEmpty);
        Assert.Equal(3, follows);
        context.Dispatcher.RunDelayed();
        Assert.Equal(updates, context.Updates);
        Assert.True(context.Config.FindGame(Pokemon)!.IsNew);
    });

    [Fact]
    public void Without_a_match_the_details_text_is_neutral_and_follows_the_filter() => Sta.Run(() =>
    {
        // Prüfung 2026-10-20: Ohne Treffer bat der Detailbereich „Wähle ein Spiel“, obwohl die Liste nichts zum Wählen zeigt.
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal(LibraryStrings.Details_EmptyTitle, vm.DetailsEmptyTitle);
        Assert.Equal(LibraryStrings.Details_EmptyText, vm.DetailsEmptyText);

        Search(vm, "gibt es nicht");
        Assert.True(vm.IsFilterEmpty);
        Assert.Equal("Kein Spiel gewählt", vm.DetailsEmptyTitle);
        Assert.Equal(LibraryStrings.Details_NoMatchText, vm.DetailsEmptyText);
        Assert.Contains(nameof(LibraryViewModel.DetailsEmptyTitle), raised);
        Assert.Contains(nameof(LibraryViewModel.DetailsEmptyText), raised);

        raised.Clear();
        vm.ClearFilters();
        Assert.Equal(LibraryStrings.Details_EmptyTitle, vm.DetailsEmptyTitle);
        Assert.Equal(LibraryStrings.Details_EmptyText, vm.DetailsEmptyText);
        Assert.Contains(nameof(LibraryViewModel.DetailsEmptyTitle), raised);

        // Leere Bibliothek (Gegenprüfung 2026-10-20): kein Filter ohne Treffer, aber ebenso nichts zu wählen. Der Detailbereich
        // sagt das mit eigenem Text („Noch kein Spiel in der Liste“), nicht „Wähle ein Spiel“ und nicht „wieder“ wie ohne Treffer.
        var emptyContext = new TestLibraryContext();
        using var empty = new LibraryViewModel(emptyContext, new FakeDialogs());
        Assert.True(empty.IsLibraryEmpty);
        Assert.False(empty.IsFilterEmpty);
        Assert.Equal("Noch kein Spiel in der Liste", empty.DetailsEmptyTitle);
        Assert.Equal(LibraryStrings.Details_EmptyLibraryText, empty.DetailsEmptyText);
        Assert.DoesNotContain("Wähle", empty.DetailsEmptyTitle + empty.DetailsEmptyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wieder", empty.DetailsEmptyText, StringComparison.Ordinal);

        // Kommt ein Spiel dazu, gilt wieder „Wähle ein Spiel“ (bzw. es ist gewählt), und beide Texte melden sich.
        var emptyRaised = new List<string?>();
        empty.PropertyChanged += (_, e) => emptyRaised.Add(e.PropertyName);
        emptyContext.ConfigService.Update(c => c.Games.Add(Game(GameSource.Steam, "1", "Hades")));
        Assert.False(empty.IsLibraryEmpty);
        Assert.Equal(LibraryStrings.Details_EmptyTitle, empty.DetailsEmptyTitle);
        Assert.Equal(LibraryStrings.Details_EmptyText, empty.DetailsEmptyText);
        Assert.Contains(nameof(LibraryViewModel.DetailsEmptyTitle), emptyRaised);
        Assert.Contains(nameof(LibraryViewModel.DetailsEmptyText), emptyRaised);
    });

    [Fact]
    public void Source_filter_and_the_three_toolbar_filters_move_and_restore_the_selection() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var cyberpunk = Item(vm, Cyberpunk);
        var deadIsland = Item(vm, DeadIsland);
        vm.SelectedGame = cyberpunk;
        context.Dispatcher.RunDelayed();
        var updates = context.Updates;

        // Quelle
        vm.SelectedSource = Source(vm, GameSource.Epic);
        AssertShown(vm, deadIsland);
        vm.SelectedSource = Source(vm, null);
        AssertShown(vm, cyberpunk);

        // „Neu“ (Alt+N, Knopf): Cyberpunk ist nicht neu, der erste neue nach Name ist Dead Island.
        vm.ToggleFilter(ToolbarFilter.New);
        AssertShown(vm, deadIsland);
        vm.ToggleFilter(ToolbarFilter.New);
        AssertShown(vm, cyberpunk);

        // „HDR-fähig“ (Menü der Leiste, Name als Text)
        vm.ToggleFilterCommand.Execute("HdrCapable");
        AssertShown(vm, deadIsland);
        vm.ToggleFilterCommand.Execute(ToolbarFilter.HdrCapable);
        AssertShown(vm, cyberpunk);
        Assert.Equal(updates, context.Updates);

        // „HDR automatisch“ (Alt+A): Pokémon hat es nicht, der erste mit Automatik ist Cyberpunk.
        var pokemon = Item(vm, Pokemon);
        vm.SelectedGame = pokemon;
        vm.OnlySelected = true;
        AssertShown(vm, cyberpunk);
        vm.OnlySelected = false;
        AssertShown(vm, pokemon);

        // Kombiniert: Quelle und Filter, bis nichts mehr passt, dann alles zurück.
        vm.SelectedSource = Source(vm, GameSource.Steam);
        vm.ToggleFilter(ToolbarFilter.HdrCapable);
        AssertShown(vm, Item(vm, RedDead));
        vm.ToggleFilter(ToolbarFilter.HdrAuto);
        AssertShown(vm, null);
        vm.ClearFilters();
        AssertShown(vm, pokemon);
        Assert.True(deadIsland.IsNew);
        Assert.True(Item(vm, RedDead).IsNew);
    });

    [Fact]
    public void First_match_follows_the_sort_order_and_sorting_view_switch_and_reset_keep_the_rule() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        var cyberpunk = Item(vm, Cyberpunk);
        var pokemon = Item(vm, Pokemon);
        vm.SortOrder = GameSortOrder.LastPlayed;
        vm.SelectedGame = cyberpunk;

        // Erster Treffer nach „Zuletzt gespielt“ (Pokémon), nicht nach Name (Dead Island).
        vm.ToggleFilter(ToolbarFilter.New);
        Assert.Equal(["Pokémon Arena", "Red Dead Redemption 2", "Dead Island 2"], Visible(vm));
        AssertShown(vm, pokemon);
        Assert.Equal(1, follows);

        // Sortierung und Ansicht ändern nichts an der Auswahl, auch nicht am verdrängten Spiel.
        vm.SortOrder = GameSortOrder.Name;
        Assert.Equal(["Dead Island 2", "Pokémon Arena", "Red Dead Redemption 2"], Visible(vm));
        AssertShown(vm, pokemon);
        vm.SetViewMode(LibraryViewMode.Grid, persist: false);
        AssertShown(vm, pokemon);
        vm.ViewMode = LibraryViewMode.Shelf;
        AssertShown(vm, pokemon);
        Assert.Equal(1, follows);

        // Zurücksetzen (✕ der Kapsel und „Filter zurücksetzen“ teilen sich den Befehl): Cyberpunk kehrt zurück.
        vm.ClearFiltersCommand.Execute(null);
        AssertShown(vm, cyberpunk);
        Assert.Equal(2, follows);
        Assert.True(pokemon.IsNew);
        Assert.Equal(LibraryViewMode.Shelf, vm.ViewMode);
    });

    [Fact]
    public void New_matching_game_after_no_match_is_shown_and_a_removed_hidden_game_does_not_return() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        vm.SelectedGame = Item(vm, Cyberpunk);
        Search(vm, "baldur");
        AssertShown(vm, null);

        // Etwa das Einlesen im Hintergrund findet ein passendes Spiel: Es erscheint auch im Titelbereich, leise.
        context.ConfigService.Update(c => c.Games.Add(Game(GameSource.Gog, "1", "Baldur's Gate 3")));
        var baldur = Item(vm, "Gog:1");
        Assert.Equal(["Baldur's Gate 3"], Visible(vm));
        AssertShown(vm, baldur);
        Assert.True(baldur.IsNew);
        Assert.Equal(2, follows);

        // Das verdrängte Spiel verschwindet aus der Bibliothek: Es kommt nicht zurück, auch ohne Filter.
        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.Key == Cyberpunk));
        vm.ClearFilters();
        AssertShown(vm, baldur);
        Assert.Equal(2, follows);

        // Ein entferntes gewähltes Spiel: Sein Nachbar ist gewählt (E90, vorher blieb der Titelbereich leer), leise.
        var deadIsland = Item(vm, DeadIsland);
        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.Key == "Gog:1"));
        AssertShown(vm, deadIsland);
        Assert.True(deadIsland.IsNew);
        Assert.Equal(3, follows);
    });
}
