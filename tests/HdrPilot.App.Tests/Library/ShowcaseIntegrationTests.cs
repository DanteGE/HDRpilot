using System.Windows.Threading;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Tests.Artwork;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
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
