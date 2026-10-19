using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Nicht installierte Spiele ausblenden“ in der Bibliothek (E90, G11.1): Ausgeblendete Spiele zählen nirgends mit (Kopf,
/// Quellen, Filterzahlen, Kapsel), der Leerzustand sagt, dass alles ausgeblendet ist, ein gewähltes Spiel weicht seinem
/// Nachbarn, ein wiedergefundenes erscheint, und das Ausblenden ist kein Filter.
/// </summary>
public sealed class UninstalledLibraryTests
{
    private const string Outlast = "Steam:1304930";
    private const string Helldivers = "Steam:553850";

    /// <summary>
    /// Nach Name: Dead Island 2 (Epic, neu), Eigenes Spiel (nicht installiert), HELLDIVERS 2 („HDR automatisch“), The Outlast
    /// Trials (nicht installiert, „HDR automatisch“, HDR-fähig, neu), VEIN Demo (nicht installiert, neu).
    /// </summary>
    private static TestLibraryContext SampleContext()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Epic, "Crow", "Dead Island 2"),
            Game(GameSource.Manual, "abc", "Eigenes Spiel", isNew: false, installed: false),
            Game(GameSource.Steam, "553850", "HELLDIVERS 2", hdrAuto: true, isNew: false),
            Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, installed: false),
            Game(GameSource.Steam, "2082470", "VEIN Demo", installed: false));
        context.HdrSupport[Outlast] = HdrSupportStatus.Native;
        context.HdrSupport[Helldivers] = HdrSupportStatus.Native;
        return context;
    }

    private static void Hide(TestLibraryContext context, bool hide) => context.ConfigService.Update(c => c.Settings.HideUninstalledGames = hide);

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    [Fact]
    public void Uninstalled_hidden_games_count_nowhere() => Sta.Run(async () =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        await vm.RefreshHdrSupportAsync();
        Assert.Equal((5, 2, 3, 2), (vm.TotalCount, vm.HdrAutoCount, vm.NewCount, vm.HdrCapableCount));

        Hide(context, true);

        Assert.Equal(["Dead Island 2", "HELLDIVERS 2"], Visible(vm));
        Assert.Equal((2, 1, 1, 1), (vm.TotalCount, vm.HdrAutoCount, vm.NewCount, vm.HdrCapableCount));
        Assert.Equal(3, vm.HiddenCount);
        Assert.Equal(LibraryText.Games(2), vm.ResultText);
        Assert.Equal(LibraryText.Games(2), vm.TotalText);
        Assert.Equal(["Alle", "Steam (1)", "Epic Games (1)"], vm.SourceOptions.Select(o => o.AutomationName));
        Assert.Equal(2, vm.SourceOptions[0].Count);
        Assert.False(vm.IsFiltered); // Ausblenden ist kein Filter: keine Kapsel „Gefiltert“, keine Lichtnaht
        Assert.Equal(LibraryStrings.Toolbar_NoFilters, vm.ToolbarSummary);
        Assert.Equal(LibraryText.Format(LibraryStrings.Toolbar_ResetTipHidden, LibraryText.Games(2), "3 nicht installierte Spiele"), vm.ResetTipText);

        // Gefiltert zählt die Kapsel nur die gelisteten Spiele („1 von 2“).
        vm.OnlyNew = true;
        Assert.Equal(LibraryText.Format(LibraryStrings.Toolbar_Filtered, LibraryStrings.Filter_OnlyNew, 1, 2), vm.ToolbarSummary);
        vm.OnlyNew = false;

        Hide(context, false);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(0, vm.HiddenCount);
        Assert.Equal(LibraryText.Format(LibraryStrings.Toolbar_ResetTip, LibraryText.Games(5)), vm.ResetTipText);
        Assert.Equal(5, Visible(vm).Count);
    });

    [Fact]
    public void Uninstalled_hiding_the_selected_game_selects_its_neighbour() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var follows = 0;
        vm.SelectionFollowedFilter += (_, _) => follows++;
        vm.SelectedGame = vm.Games.Single(g => g.Key == Outlast);
        context.Dispatcher.RunDelayed(); // „angesehen“ gespeichert: Danach gleicht die Liste Änderungen von außen sofort ab

        Hide(context, true);

        // Danach VEIN Demo (auch ausgeblendet), also das vorige bleibende: HELLDIVERS 2.
        Assert.Equal("HELLDIVERS 2", vm.SelectedGame?.Name);
        Assert.Same(vm.SelectedGame, vm.Details?.Item);
        Assert.Equal(1, follows);

        // Wieder eingeblendet: die Zeilen sind zurück, die Auswahl bleibt.
        Hide(context, false);
        Assert.Equal(5, Visible(vm).Count);
        Assert.Equal("HELLDIVERS 2", vm.SelectedGame?.Name);
    });

    [Fact]
    public void Uninstalled_all_hidden_shows_why_the_list_is_empty() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1304930", "The Outlast Trials", installed: false),
            Game(GameSource.Steam, "2082470", "VEIN Demo", installed: false));
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Equal(LibraryStrings.List_EmptyTitle, vm.EmptyTitle);

        Hide(context, true);

        Assert.True(vm.IsLibraryEmpty);
        Assert.False(vm.IsFilterEmpty);
        Assert.Null(vm.SelectedGame);
        Assert.Equal(LibraryStrings.List_EmptyHiddenTitle, vm.EmptyTitle);
        Assert.Equal(LibraryText.Format(LibraryStrings.List_EmptyHiddenTextMany, 2), vm.EmptyText);

        context.ConfigService.Update(c => c.Games.RemoveAll(g => g.SourceId == "2082470"));
        Assert.Equal(LibraryStrings.List_EmptyHiddenTextOne, vm.EmptyText);

        context.ConfigService.Update(c => c.Games.Clear());
        Assert.Equal(LibraryStrings.List_EmptyTitle, vm.EmptyTitle);
        Assert.Equal(LibraryStrings.List_EmptyText, vm.EmptyText);
    });

    [Fact]
    public void Uninstalled_game_found_again_appears_while_hiding() => Sta.Run(() =>
    {
        var context = SampleContext();
        Hide(context, true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Equal(2, vm.TotalCount);

        context.ConfigService.Update(c => c.FindGame(Outlast)!.IsInstalled = true);

        Assert.Equal(["Dead Island 2", "HELLDIVERS 2", "The Outlast Trials"], Visible(vm));
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(2, vm.HiddenCount);
    });

    [Fact]
    public void Uninstalled_hidden_source_leaves_the_source_filter() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Source == GameSource.Manual);
        Assert.Equal(["Eigenes Spiel"], Visible(vm));

        Hide(context, true);

        Assert.DoesNotContain(vm.SourceOptions, o => o.Source == GameSource.Manual);
        Assert.Null(vm.SelectedSource?.Source);
        Assert.Equal(2, Visible(vm).Count);
    });

    [Fact]
    public void Uninstalled_hidden_game_cannot_be_selected_by_its_key() => Sta.Run(() =>
    {
        var context = SampleContext();
        Hide(context, true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games.Single(g => g.Key == Helldivers);

        vm.SelectGame(Outlast);

        Assert.Equal(Helldivers, vm.SelectedGame?.Key);
        Assert.False(vm.IsFiltered);
    });

    [Fact]
    public void Uninstalled_hidden_game_still_shows_as_running_in_the_status() => Sta.Run(() =>
    {
        // Ausgeblendet heißt nicht vergessen: Läuft ein ausgeblendetes Spiel (HDR automatisch wirkt weiter), nennt der Kopf es.
        var context = SampleContext();
        Hide(context, true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        context.SessionList.Add(new HdrPilot.Core.Sessions.SessionInfo(Outlast, "The Outlast Trials", HdrPilot.Core.Sessions.SessionPhase.Running, HdrPilot.Core.Sessions.SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [7]));
        context.RaiseHdrStateChanged();

        Assert.True(vm.IsGameRunning);
        Assert.Contains("The Outlast Trials", vm.ActiveSessionText, StringComparison.Ordinal);
        Assert.Equal(2, vm.TotalCount);
    });
}
