using HdrPilot.App.Services.Artwork;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Helle Fassung des Titelbilds nur im Ruhezustand (G10.9, „keine Last, solange ein Spiel läuft“): Sie wird nie berechnet,
/// solange das Fenster im Hintergrund, minimiert oder geschlossen ist oder ein Spiel läuft oder startet; bei aktivem Fenster
/// wird sie nach kurzer Ruhe der Auswahl vorab geladen (niedrige Priorität), und der HDR-Moment wartet höchstens kurz auf sie.
/// </summary>
public sealed class ShowcaseBoostTests
{
    private const string Key = "Steam:1091500";

    private static (TestLibraryContext Context, FakeArtworkService Art) Context()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false));
        var art = new FakeArtworkService();
        art.WithHero.Add(Key);
        art.WithHero.Add("Epic:Crow");
        context.Artwork = art;
        return (context, art);
    }

    private static void SetHdr(TestLibraryContext context, bool on)
    {
        context.Hdr.Displays[0].HdrOn = on;
        context.RaiseHdrStateChanged();
    }

    private static SessionInfo Running(string key, string name) =>
        new(key, name, SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UtcNow, [FakeHdrController.Primary], [42]);

    [Fact]
    public void Bright_variant_is_never_computed_while_the_window_is_in_the_background_minimized_or_closed() => Sta.Run(() =>
    {
        var (context, art) = Context();
        var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SelectedGame = vm.Games[0];
        context.Dispatcher.RunDelayed();

        // Fenster im Hintergrund oder minimiert (nicht aktiv): HDR an rechnet nichts, auch vorab nicht.
        SetHdr(context, on: true);
        context.Dispatcher.RunDelayed();
        Assert.False(vm.BoostAllowed);
        Assert.Empty(art.BoostRequests);
        Assert.Empty(art.PrefetchRequests);
        Assert.Null(vm.Hero!.Artwork!.HeroBoost);

        // Aktiv: jetzt wird sie gebraucht und geholt.
        vm.SetWindowActive(true);
        Assert.Equal([Key], art.BoostRequests);
        Assert.NotNull(vm.Hero.Artwork!.HeroBoost);

        // Geschlossen: nichts mehr, auch nicht nach HDR aus und wieder an.
        vm.Dispose();
        SetHdr(context, on: false);
        SetHdr(context, on: true);
        context.Dispatcher.RunDelayed();
        Assert.Single(art.BoostRequests);
        Assert.Empty(art.PrefetchRequests);
    });

    [Fact]
    public void Bright_variant_is_never_computed_while_a_game_runs() => Sta.Run(() =>
    {
        var (context, art) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SetWindowActive(true);
        vm.SelectedGame = vm.Games[0];

        // Ein Spiel läuft (von der Automatik erkannt) und HDR geht an: nichts wird berechnet, auch kein Vorab-Laden.
        context.SessionList.Add(Running(Key, "Cyberpunk 2077"));
        SetHdr(context, on: true);
        context.Dispatcher.RunDelayed();
        Assert.True(vm.IsGameRunning);
        Assert.False(vm.BoostAllowed);
        Assert.Empty(art.BoostRequests);
        Assert.Empty(art.PrefetchRequests);

        // Spiel zu Ende, HDR wieder aus: nach kurzer Ruhe vorab laden.
        context.SessionList.Clear();
        SetHdr(context, on: false);
        context.Dispatcher.RunDelayed();
        Assert.Equal([Key], art.PrefetchRequests);
    });

    [Fact]
    public void Bright_variant_is_not_computed_while_a_launch_from_the_library_runs() => Sta.Run(async () =>
    {
        var (context, art) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SetWindowActive(true);
        vm.SelectedGame = vm.Games[0];

        // Start über die Bibliothek: Solange er läuft (HDR geht dabei an, die Sitzung ist noch nicht gemeldet), wird
        // nichts berechnet, auch das wartende Vorab-Laden nicht.
        context.PendingLaunch = new TaskCompletionSource<LaunchOutcome>();
        vm.Details!.LaunchCommand.Execute(null);
        SetHdr(context, on: true);
        context.Dispatcher.RunDelayed();
        Assert.False(vm.BoostAllowed);
        Assert.Empty(art.BoostRequests);
        Assert.Empty(art.PrefetchRequests);

        // Start gescheitert (kein Spiel läuft): HDR ist an, also wird sie jetzt geholt.
        context.PendingLaunch.SetResult(new LaunchOutcome(false, "kein Start-Befehl", null, null));
        await Sta.Finished(vm.Details!.LaunchCommand);
        Assert.True(vm.BoostAllowed);
        Assert.Equal([Key], art.BoostRequests);
    });

    [Fact]
    public void Bright_variant_is_prefetched_only_after_the_selection_settles_in_an_active_window() => Sta.Run(() =>
    {
        var (context, art) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SetWindowActive(true);
        vm.SelectedGame = vm.Games[0];

        // Gleich nach dem Wechsel nichts (die Auswahl kann weiterwandern), nach der Ruhe vorab mit niedriger Priorität.
        Assert.Empty(art.PrefetchRequests);
        context.Dispatcher.RunDelayed();
        Assert.Equal([Key], art.PrefetchRequests);
        Assert.Empty(art.BoostRequests);
        Assert.NotNull(vm.Hero!.Artwork!.HeroBoost);

        // HDR geht an: sie ist schon da, keine neue Anfrage.
        SetHdr(context, on: true);
        Assert.Empty(art.BoostRequests);

        // Fenster geht in den Hintergrund, bevor die Ruhe vorbei ist: das Vorab-Laden entfällt.
        SetHdr(context, on: false);
        vm.SelectedGame = vm.Games[1];
        vm.SetWindowActive(false);
        context.Dispatcher.RunDelayed(); // lädt bei schnellem Wechsel erst hier das Titelbild
        context.Dispatcher.RunDelayed();
        Assert.Equal("Epic:Crow", vm.Hero!.Item.Key);
        Assert.Equal([Key], art.PrefetchRequests);

        // Wieder aktiv: nach der Ruhe vorab.
        vm.SetWindowActive(true);
        context.Dispatcher.RunDelayed();
        Assert.Equal([Key, "Epic:Crow"], art.PrefetchRequests);
    });

    [Fact]
    public void Hdr_moment_waits_briefly_for_a_bright_variant_that_is_still_loading() => Sta.Run(async () =>
    {
        var (context, art) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SetWindowActive(true);
        vm.SelectedGame = vm.Games[0];
        art.PendingBoost = new TaskCompletionSource<ArtworkImage?>();
        SetHdr(context, on: true);
        Assert.Equal([Key], art.BoostRequests);

        // Lädt noch: der Moment wartet, höchstens bis die kurze Frist abläuft.
        var wait = vm.WaitForHeroBoostAsync(LibraryViewModel.MomentBoostWait);
        await Task.Yield();
        Assert.False(wait.IsCompleted);
        context.Dispatcher.RunDelayed();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.Hero!.Artwork!.HeroBoost);

        // Kommt sie innerhalb der Frist, ist das Warten sofort vorbei und sie steht im Titelbereich.
        var second = vm.WaitForHeroBoostAsync(LibraryViewModel.MomentBoostWait);
        Assert.False(second.IsCompleted);
        art.PendingBoost.SetResult(new ArtworkImage(FakeArtworkService.Solid(32, 9, System.Windows.Media.Colors.Moccasin), ArtworkKind.Hero, ArtworkOrigin.Launcher, 3840, 1240, "Steam"));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(vm.Hero.Artwork!.HeroBoost);

        // Nichts lädt: sofort fertig.
        Assert.True(vm.WaitForHeroBoostAsync(LibraryViewModel.MomentBoostWait).IsCompleted);
    });
}
