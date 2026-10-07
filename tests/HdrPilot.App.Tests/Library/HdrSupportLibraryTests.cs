using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>Kennzeichen, Filter „HDR-fähig“ und Nachladen der HDR-Liste in der Spielebibliothek.</summary>
public sealed class HdrSupportLibraryTests
{
    private static TestLibraryContext SampleContext()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "3008130", "Dying Light: The Beast", isNew: false),
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false),
            Game(GameSource.Xbox, "Palworld", "Palworld", isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
            Game(GameSource.Manual, "m1", "Eigenes Spiel", isNew: false));
        context.HdrSupport["Steam:3008130"] = HdrSupportStatus.Native;
        context.HdrSupport["Steam:1091500"] = HdrSupportStatus.Limited;
        context.HdrSupport["Xbox:Palworld"] = HdrSupportStatus.None;
        context.HdrSupport["Epic:Crow"] = HdrSupportStatus.ModOnly;
        return context;
    }

    private static List<string> Visible(LibraryViewModel vm) => vm.GamesView.Cast<GameItemViewModel>().Select(g => g.Name).ToList();

    private static GameItemViewModel Item(LibraryViewModel vm, string key) => vm.Games.Single(g => g.Key == key);

    [Fact]
    public void Statuses_are_loaded_when_the_window_opens_and_shown_as_badges() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.Equal(1, context.HdrSupportLoads);
        var native = Item(vm, "Steam:3008130");
        Assert.Equal(HdrSupportStatus.Native, native.HdrSupport);
        Assert.True(native.HasHdrBadge);
        Assert.Equal("HDR", native.HdrBadgeText);
        Assert.Equal(LibraryStrings.Badge_HdrTooltip, native.HdrBadgeTooltip);

        var limited = Item(vm, "Steam:1091500");
        Assert.True(limited.HasHdrBadge);
        Assert.Equal("HDR*", limited.HdrBadgeText);
        Assert.Equal("HDR eingeschränkt laut PCGamingWiki", limited.HdrBadgeTooltip);

        foreach (var key in new[] { "Xbox:Palworld", "Epic:Crow", "Manual:m1" })
        {
            var item = Item(vm, key);
            Assert.False(item.HasHdrBadge, key);
            Assert.Equal(string.Empty, item.HdrBadgeText);
            Assert.Equal(string.Empty, item.HdrBadgeTooltip); // nie null: AutomationProperties.Name verlangt Text
        }

        Assert.Equal(HdrSupportStatus.Unknown, Item(vm, "Manual:m1").HdrSupport);
    });

    [Fact]
    public void Hdr_capable_filter_keeps_native_and_limited_and_is_cleared_with_the_other_filters() => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.OnlyHdrCapable = true;

        Assert.Equal(["Cyberpunk 2077", "Dying Light: The Beast"], Visible(vm));
        Assert.True(vm.IsFiltered);
        Assert.Equal(LibraryText.Format(LibraryStrings.List_ResultFiltered, 2, 5), vm.ResultText);
        Assert.Contains(nameof(LibraryViewModel.IsFiltered), changed);

        vm.OnlyNew = true; // zusammen mit anderen Filtern: beide müssen passen
        Assert.Empty(Visible(vm));
        Assert.True(vm.IsFilterEmpty);

        vm.ClearFilters();
        Assert.False(vm.OnlyHdrCapable);
        Assert.False(vm.IsFiltered);
        Assert.Equal(5, Visible(vm).Count);
        Assert.Contains(nameof(LibraryViewModel.OnlyHdrCapable), changed);
    });

    [Fact]
    public void A_finished_sync_updates_badges_and_the_filtered_list() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs()) { OnlyHdrCapable = true };
        Assert.Equal(["Cyberpunk 2077", "Dying Light: The Beast"], Visible(vm));
        var cyberpunk = Item(vm, "Steam:1091500");
        var badgeChanges = 0;
        cyberpunk.PropertyChanged += (_, e) => badgeChanges += e.PropertyName == nameof(GameItemViewModel.HasHdrBadge) ? 1 : 0;

        context.PublishHdrSupport(("Steam:1091500", HdrSupportStatus.Native), ("Epic:Crow", HdrSupportStatus.Limited));

        Assert.Equal(["Cyberpunk 2077", "Dead Island 2"], Visible(vm));
        Assert.Equal(HdrSupportStatus.Native, cyberpunk.HdrSupport);
        Assert.Equal(HdrSupportStatus.Unknown, Item(vm, "Steam:3008130").HdrSupport); // nicht mehr im Zwischenspeicher
        Assert.Equal(1, badgeChanges);
    });

    [Fact]
    public void Games_added_later_get_their_status_from_the_loaded_list() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.HdrSupport["Gog:1207658924"] = HdrSupportStatus.Native;
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        context.ConfigService.Update(c => c.Games.Add(Game(GameSource.Gog, "1207658924", "Neues Spiel")));

        Assert.Equal(HdrSupportStatus.Native, Item(vm, "Gog:1207658924").HdrSupport);
    });

    [Fact]
    public void Unreadable_cache_leaves_the_list_without_badges() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.HdrSupportFailure = new IOException("gesperrt");

        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.All(vm.Games, g => Assert.Equal(HdrSupportStatus.Unknown, g.HdrSupport));
        Assert.Equal(5, Visible(vm).Count);
    });

    [Fact]
    public void Closing_the_window_unsubscribes_from_sync_results() => Sta.Run(() =>
    {
        var context = SampleContext();
        var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Equal(1, context.HdrSupportChangedSubscribers);

        vm.Dispose();

        Assert.Equal(0, context.HdrSupportChangedSubscribers);
        context.PublishHdrSupport(("Manual:m1", HdrSupportStatus.Native));
        Assert.Equal(1, context.HdrSupportLoads); // nach dem Schließen wird nichts mehr geladen
    });
}
