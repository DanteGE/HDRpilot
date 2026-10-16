using System.Windows.Threading;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Tests.Artwork;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Zusammenspiel der Teile nach dem Zusammenführen (G10.10): die Bibliothek mit den echten Diensten statt Attrappen –
/// Helligkeit (DisplayLuminanceService, nur der DXGI-Leser ist eine Attrappe) in der Bildschirm-Kapsel und Spielbilder
/// (ArtworkService über erzeugte Testbilder) im Titelbereich.
/// </summary>
public sealed class ShowcaseIntegrationTests
{
    private const string Gdi = @"\\.\DISPLAY1";

    [Fact]
    public void Integration_nits_capsule_keeps_the_remembered_hdr_peak_of_the_real_luminance_service() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Testspiel", isNew: false));
        var reader = new SwitchableReader { Values = [new DisplayLuminance(Gdi, 456, 254, 0, 10, HdrActive: true)] };
        var hdrOn = true;
        var ui = Dispatcher.CurrentDispatcher;
        var fs = new InMemoryFileSystem();
        using var service = new DisplayLuminanceService(new DisplayLuminanceServiceOptions
        {
            Reader = reader,
            Displays = () => [Display(hdrOn)],
            Store = new DisplayLuminanceStore(fs, @"C:\Data\display-luminance.json"),
            PostToUi = action => ui.BeginInvoke(action),
        });
        context.Luminance = service;
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.False(vm.Nits.HasValues); // ohne Fenster-Freigabe wird nichts gelesen

        using (service.Acquire())
        {
            await Settled(service);
            Assert.Equal(456, vm.Nits.ScaleMaximum);
            Assert.Equal(254.0 / 456, vm.Nits.FullFrameFraction, 3);
            Assert.Contains("456", vm.Nits.ScaleEndText, StringComparison.Ordinal);

            // HDR aus: DXGI meldet SDR-Werte, die Skala endet weiter an der gemerkten HDR-Spitze (nie fest bei 1000).
            hdrOn = false;
            reader.Values = [new DisplayLuminance(Gdi, 270, 270, 0.5, 8, HdrActive: false)];
            service.Invalidate();
            await Settled(service);
            Assert.Equal(2, service.ReadCount);
            Assert.Equal(456, vm.Nits.ScaleMaximum);
            Assert.Contains("456", vm.Nits.PeakText, StringComparison.Ordinal);
            Assert.NotEqual(1000, vm.Nits.ScaleMaximum);
        }

        // Gegenprobe: ohne gemerkte Spitze (neue Ablage) zeigt die Kapsel den aktuellen SDR-Wert als Skalenende.
        using var fresh = new DisplayLuminanceService(new DisplayLuminanceServiceOptions
        {
            Reader = reader,
            Displays = () => [Display(hdrOn)],
            Store = new DisplayLuminanceStore(new InMemoryFileSystem(), @"C:\Data\display-luminance.json"),
            PostToUi = action => ui.BeginInvoke(action),
        });
        context.Luminance = fresh;
        using var other = new LibraryViewModel(context, new FakeDialogs());
        using (fresh.Acquire())
        {
            await Settled(fresh);
            Assert.Equal(270, other.Nits.ScaleMaximum);
            Assert.False(other.Nits.PeakMeasuredWithHdr);
        }
    });

    [Fact]
    public void Integration_hero_shows_the_real_launcher_image_and_falls_back_to_a_generated_one_with_the_icon() => Sta.Run(async () =>
    {
        using var art = new ArtworkFixture();
        art.SteamFile("100", "library_hero.jpg", ArtworkFixture.Hero());
        var icon = art.Save(Path.Combine(art.Root, "icons", "kein-titelbild.png"), ArtworkFixture.Icon(256));
        var withHero = Game(GameSource.Steam, "100", "Mit Titelbild", isNew: false);
        var withoutHero = Game(GameSource.Steam, "200", "Baldur's Gate 3", isNew: false);
        withoutHero.IconCandidates = [icon];
        var context = new TestLibraryContext(withHero, withoutHero);
        var service = art.Service();
        context.Artwork = service;
        using var lease = service.Acquire();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        vm.SelectedGame = vm.Games.Single(g => g.Key == withHero.Key);
        context.Dispatcher.RunDelayed();
        var hero = await HeroFor(vm, withHero.Key);
        Assert.Equal(ArtworkOrigin.Launcher, hero.Artwork!.Hero!.Origin);
        Assert.False(hero.IsGenerated);
        Assert.NotNull(hero.Artwork.HeroBoost); // helle HDR-Fassung für den HDR-Moment

        vm.SelectedGame = vm.Games.Single(g => g.Key == withoutHero.Key);
        context.Dispatcher.RunDelayed();
        hero = await HeroFor(vm, withoutHero.Key);
        Assert.Equal(ArtworkOrigin.Generated, hero.Artwork!.Hero!.Origin);
        Assert.True(hero.IsGenerated);
        Assert.True(hero.ShowIconTile); // Symbol links vom Namen: das erzeugte Titelbild enthält keins
        Assert.Same(hero.Artwork.Icon!.Image, hero.IconImage);
        Assert.Equal(256, hero.Artwork.Icon.PixelWidth);
    });

    [Fact]
    public void Integration_a_second_own_image_replaces_the_first_under_the_same_name() => Sta.Run(async () =>
    {
        // Der Bilddienst legt eigene Bilder je Spiel und Art immer unter demselben Namen ab (gleiches Kennzeichen).
        using var art = new ArtworkFixture();
        var (context, service, game, token) = WithOwnHero(art);
        var second = art.Save(Path.Combine(art.Root, "eigene", "zweites.png"), ArtworkFixture.Solid(1000, 400, colorful: true));
        using var lease = service.Acquire();
        using var vm = new LibraryViewModel(context, Dialogs(second, out var shown));
        var picker = await OpenPicker(vm, context, game.Key, shown);

        await PickOwnFile(picker, second);
        Assert.True(await picker.ApplyAsync());

        Assert.Equal(token, context.Config.FindGame(game.Key)!.Artwork!.Hero);
        Assert.True(File.Exists(CustomFile(art, token)));
        var artwork = await service.GetArtworkAsync(context.Config.FindGame(game.Key)!, 3840, TestContext.Current.CancellationToken);
        Assert.Equal(ArtworkOrigin.Custom, artwork.Summary.Hero);
        Assert.Equal(1000, artwork.Hero!.PixelWidth); // das neue Bild, nicht das erste (800 px)
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_Applied, game.Name), vm.StatusMessage);
    });

    [Fact]
    public void Integration_failed_write_keeps_the_own_image_the_config_still_uses() => Sta.Run(async () =>
    {
        using var art = new ArtworkFixture();
        var (context, service, game, token) = WithOwnHero(art);
        var second = art.Save(Path.Combine(art.Root, "eigene", "zweites.png"), ArtworkFixture.Solid(1000, 400, colorful: true));
        using var lease = service.Acquire();
        using var vm = new LibraryViewModel(context, Dialogs(second, out var shown));
        var picker = await OpenPicker(vm, context, game.Key, shown);
        await PickOwnFile(picker, second);
        picker.Kind = ArtworkKind.Logo; // zweite Änderung, damit geschrieben werden muss
        await Loaded(picker);
        picker.Selected = picker.Candidates.Single(c => c.Token == ArtworkOverride.Generated);
        Assert.Equal(ArtworkOverride.Generated, picker.Selected?.Token);
        context.FailNextUpdate = new IOException("Platte voll");

        Assert.False(await picker.ApplyAsync());

        // Die Konfiguration zeigt weiter auf die eigene Datei: sie darf beim Zurücknehmen nicht gelöscht werden.
        Assert.Equal(token, context.Config.FindGame(game.Key)!.Artwork!.Hero);
        Assert.True(File.Exists(CustomFile(art, token)));
    });

    [Fact]
    public void Integration_picker_ignores_a_choice_from_the_previous_tab_while_the_next_one_loads() => Sta.Run(async () =>
    {
        using var art = new ArtworkFixture();
        var (context, service, game, token) = WithOwnHero(art);
        using var lease = service.Acquire();
        using var vm = new LibraryViewModel(context, Dialogs(art.Save(Path.Combine(art.Root, "eigene", "x.png"), ArtworkFixture.Solid(64, 64, colorful: true)), out var shown));
        var picker = await OpenPicker(vm, context, game.Key, shown);
        var heroGenerated = picker.Candidates.Single(c => c.Token == ArtworkOverride.Generated);

        picker.Kind = ArtworkKind.Logo;
        Assert.DoesNotContain(heroGenerated, picker.Candidates); // die Titelbild-Liste ist weg, solange das Logo lädt
        picker.Selected = heroGenerated; // Klick in die alte Liste: gilt nicht für das Logo
        await Loaded(picker);

        Assert.Null(picker.Selected?.Token); // Logo bleibt automatisch
        Assert.False(await picker.ApplyAsync()); // nichts geändert, nichts geschrieben
        Assert.Equal(token, context.Config.FindGame(game.Key)!.Artwork!.Hero);
        Assert.Null(context.Config.FindGame(game.Key)!.Artwork!.Logo);
        Assert.Equal(0, context.Updates);
    });

    /// <summary>Spiel mit Steam-Titelbild und schon gewähltem eigenen Titelbild (800 px breit) über den echten Bilderdienst.</summary>
    private static (TestLibraryContext Context, ArtworkService Service, GameConfig Game, string Token) WithOwnHero(ArtworkFixture art)
    {
        art.SteamFile("100", "library_hero.jpg", ArtworkFixture.Hero());
        art.SteamFile("100", "logo.png", ArtworkFixture.Logo(320, 120, 40, 20, 200, 60));
        var first = art.Save(Path.Combine(art.Root, "eigene", "erstes.png"), ArtworkFixture.Solid(800, 300, colorful: true));
        var game = Game(GameSource.Steam, "100", "Mit Titelbild", isNew: false);
        var context = new TestLibraryContext(game);
        var ui = Dispatcher.CurrentDispatcher;
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = art.ArtDirectory,
            IconDirectory = art.IconDirectory,
            Roots = () => art.Roots,
            PostToUi = action => ui.BeginInvoke(action),
        });
        context.Artwork = service;
        var token = service.SaveCustomImage(game, ArtworkKind.Hero, first);
        context.ConfigService.Update(c => c.FindGame(game.Key)!.Artwork = new GameArtworkChoice { Hero = token });
        return (context, service, game, token);
    }

    private static FakeDialogs Dialogs(string file, out List<ArtworkPickerViewModel> shown)
    {
        var dialogs = new FakeDialogs();
        dialogs.ArtworkFiles.Enqueue(file);
        var list = new List<ArtworkPickerViewModel>();
        dialogs.OnArtworkPicker = p =>
        {
            list.Add(p);
            return false; // der Test übernimmt selbst (ein echter Dialog wartet auf den Nutzer)
        };
        shown = list;
        return dialogs;
    }

    private static async Task<ArtworkPickerViewModel> OpenPicker(LibraryViewModel vm, TestLibraryContext context, string key, List<ArtworkPickerViewModel> shown)
    {
        vm.SelectedGame = vm.Games.Single(g => g.Key == key);
        context.Dispatcher.RunDelayed();
        vm.ChooseArtworkCommand.Execute("Hero");
        var picker = Assert.Single(shown);
        await picker.LoadAsync();
        return picker;
    }

    private static async Task PickOwnFile(ArtworkPickerViewModel picker, string file)
    {
        picker.PickFileCommand.Execute(null);
        for (var i = 0; i < 200 && picker.Selected?.PendingFile != file; i++)
        {
            await Task.Delay(25);
        }

        Assert.Equal(file, picker.Selected?.PendingFile);
    }

    /// <summary>Warten, bis der Reiter seine Möglichkeiten geladen hat.</summary>
    private static async Task Loaded(ArtworkPickerViewModel picker)
    {
        for (var i = 0; i < 200 && (picker.IsLoading || picker.Candidates.Count == 0); i++)
        {
            await Task.Delay(25);
        }

        Assert.False(picker.IsLoading);
    }

    private static string CustomFile(ArtworkFixture art, string token) => Path.Combine(art.ArtDirectory, "custom", ArtworkOverride.FileOf(token)!);

    private static DisplayInfo Display(bool hdrOn) => new()
    {
        MonitorDevicePath = FakeHdrController.Primary,
        FriendlyName = "MAG 341C OLED",
        GdiDeviceName = Gdi,
        IsPrimary = true,
        HdrSupported = true,
        HdrEnabled = hdrOn,
        Api = HdrApi.Modern,
    };

    /// <summary>Lesen im Hintergrund abwarten, dann die Meldung auf dem UI-Thread des Tests laufen lassen.</summary>
    private static async Task Settled(DisplayLuminanceService service)
    {
        await service.Idle;
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task<HeroViewModel> HeroFor(LibraryViewModel vm, string key)
    {
        for (var i = 0; i < 200; i++)
        {
            if (vm.Hero is { Artwork: not null } hero && hero.Key == key)
            {
                return hero;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Titelbild von " + key + " nicht geladen.");
    }

    private sealed class SwitchableReader : IDisplayLuminanceReader
    {
        public IReadOnlyList<DisplayLuminance> Values { get; set; } = [];

        public IReadOnlyList<DisplayLuminance> ReadAll() => Values;
    }
}
