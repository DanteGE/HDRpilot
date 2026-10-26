using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.Artwork;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Spielfarben im Bilderdienst (E94, G15.7) mit nachgebauten Launcher-Caches und erzeugten Bildern: einmal je Bildstand
/// berechnet und aus index.json zurückgelesen, nur echte Dateien als Quelle (nie erzeugte Bilder oder der Name), unlesbare
/// Dateien und Fehler werden nicht gemerkt, ein neuer Bildstand während der Rechnung wird nie überschrieben, und Laden der
/// Bilder, Kacheln oder die Vorschau in „Bild wählen…“ rechnen nie.
/// </summary>
public sealed class ArtworkServiceGameColorsTests : IDisposable
{
    private readonly ArtworkFixture _art = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string IndexPath => Path.Combine(_art.ArtDirectory, "index.json");

    public void Dispose() => _art.Dispose();

    [Fact]
    public async Task Colors_are_computed_once_and_read_back_from_the_index()
    {
        RedLogoNightCover("10");
        var game = ArtworkFixture.Steam("10");
        var first = _art.Service();

        var colors = await first.GetGameColorsAsync(game, Ct);
        var again = await first.GetGameColorsAsync(game, Ct);
        first.FlushIndex();

        Assert.Equal(GameColorSource.Logo, colors.AccentSource);
        Assert.True(colors.IsUsable);
        Assert.Same(colors, again);
        Assert.Equal(1, first.GameColorsComputed);
        Assert.Contains("\"colors\":{\"v\":1,", File.ReadAllText(IndexPath), StringComparison.Ordinal);

        var second = _art.Service();
        var fromIndex = await second.GetGameColorsAsync(game, Ct);
        Assert.Equal(colors, fromIndex);
        Assert.Equal(0, second.GameColorsComputed);
    }

    [Fact]
    public async Task Peek_knows_the_colors_only_after_the_first_request_and_only_for_the_same_state()
    {
        RedLogoNightCover("11");
        var game = ArtworkFixture.Steam("11");
        var service = _art.Service();

        Assert.False(service.TryPeek(game, out var none));
        Assert.Equal(GameColors.None, none);
        var colors = await service.GetGameColorsAsync(game, Ct);

        Assert.True(service.TryPeek(game, out var peeked));
        Assert.Equal(colors, peeked);
        var changed = ArtworkFixture.Steam("11");
        changed.Artwork = new GameArtworkChoice { Logo = ArtworkOverride.Generated };
        Assert.False(service.TryPeek(changed, out _));
    }

    [Fact]
    public async Task A_custom_logo_changes_the_signature_and_recomputes()
    {
        RedLogoNightCover("12");
        var service = _art.Service();
        var game = ArtworkFixture.Steam("12");
        var red = await service.GetGameColorsAsync(game, Ct);

        var blue = _art.Save(Path.Combine(_art.Root, "blau.png"), Bitmap(GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#1E64DC") })));
        game.Artwork = new GameArtworkChoice { Logo = service.SaveCustomImage(game, ArtworkKind.Logo, blue) };
        var custom = await service.GetGameColorsAsync(game, Ct);

        Assert.Equal(2, service.GameColorsComputed);
        Assert.InRange(Hue(red.Accent!), 20, 35);
        Assert.InRange(Hue(custom.Accent!), 255, 268);
        Assert.Equal(GameColorSource.Logo, custom.AccentSource);
    }

    [Fact]
    public async Task Generated_cover_and_hero_are_never_a_source()
    {
        // Keine Datei: Titelbild, Cover und Kopfbild wären erzeugt (Farbfeld aus dem Namen); gelesen wird davon nichts.
        var service = _art.Service();
        var game = ArtworkFixture.Steam("13", "Testspiel Bunt");

        var colors = await service.GetGameColorsAsync(game, Ct);

        Assert.Null(colors.Accent);
        Assert.Equal(0, colors.Confidence);
        Assert.False(Directory.Exists(Path.Combine(_art.ArtDirectory, "gen")), "für die Farben entsteht kein erzeugtes Bild");
    }

    [Fact]
    public async Task A_name_instead_of_the_logo_is_no_source()
    {
        RedLogo("14");
        _art.SteamFile("14", "library_600x900.jpg", Bitmap(GeneratedArt.Cover(CoverScene.Gray)));
        var service = _art.Service();
        var game = ArtworkFixture.Steam("14");
        game.Artwork = new GameArtworkChoice { Logo = ArtworkOverride.Generated };

        var colors = await service.GetGameColorsAsync(game, Ct);

        Assert.Null(colors.Accent);
        Assert.NotEqual(GameColorSource.Logo, colors.AccentSource);
    }

    [Fact]
    public async Task Square_cover_of_quiver_is_read_from_its_file_not_from_the_generated_composite()
    {
        var square = _art.Save(Path.Combine(_art.Root, "quiver", "rot.png"), Bitmap(GeneratedArt.Solid(256, 256, GeneratedArt.C("#C62828"))));
        var game = new GameConfig { Source = GameSource.Quiver, SourceId = "rot", Name = "Testspiel Rot", CoverCandidates = [square] };
        var service = _art.Service();

        var colors = await service.GetGameColorsAsync(game, Ct);

        Assert.Equal(GameColorSource.Cover, colors.AccentSource);
        Assert.InRange(Hue(colors.Accent!), 20, 35);
        Assert.False(Directory.Exists(Path.Combine(_art.ArtDirectory, "gen")), "das erzeugte Cover wird nie gelesen");
    }

    [Fact]
    public async Task Unreadable_real_cover_is_absent_and_the_result_is_not_remembered()
    {
        var cover = _art.SteamFile("15", "library_600x900.jpg", Bitmap(GeneratedArt.Cover(CoverScene.TealSciFi)));
        _art.SteamFile("15", "library_hero.jpg", Bitmap(GeneratedArt.Hero(HeroScene.Sunset)));
        File.WriteAllText(cover, "gerade gesperrt oder kaputt");
        var service = _art.Service();
        var game = ArtworkFixture.Steam("15");

        var first = await service.GetGameColorsAsync(game, Ct);
        var second = await service.GetGameColorsAsync(game, Ct);
        service.FlushIndex();

        Assert.Equal(GameColorSource.Hero, first.AccentSource); // ohne Cover gilt das Titelbild
        Assert.Equal(first, second);
        Assert.Equal(2, service.GameColorsComputed);
        Assert.False(service.TryPeek(game, out _));
        Assert.True(!File.Exists(IndexPath) || !File.ReadAllText(IndexPath).Contains("\"colors\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_new_art_state_during_the_computation_is_never_overwritten()
    {
        RedLogoNightCover("16");
        var service = _art.Service(maxParallel: 2);
        var game = ArtworkFixture.Steam("16");
        var changed = ArtworkFixture.Steam("16");
        changed.Artwork = new GameArtworkChoice { Logo = ArtworkOverride.Generated };
        var raced = false;
        service.GameColorsComputedHook = _ =>
        {
            if (!raced)
            {
                // Auf dem anderen Bilder-Thread: neuer Bildstand (Name statt Logo) mitten in der Rechnung.
                raced = true;
                Assert.True(service.GetArtworkAsync(changed, 640, Ct).Wait(TimeSpan.FromSeconds(30), Ct));
            }
        };

        var colors = await service.GetGameColorsAsync(game, Ct);
        service.FlushIndex();

        Assert.True(raced);
        Assert.Equal(GameColorSource.Logo, colors.AccentSource); // die Antwort selbst gilt für den angefragten Stand
        Assert.DoesNotContain("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);
        Assert.False(service.TryPeek(changed, out _));
    }

    [Fact]
    public async Task An_unexpected_error_gives_none_and_leaves_the_artwork_alone()
    {
        RedLogoNightCover("17");
        var service = _art.Service();
        var game = ArtworkFixture.Steam("17");
        service.GameColorsComputedHook = _ => throw new InvalidOperationException("Testfehler");

        var colors = await service.GetGameColorsAsync(game, Ct);
        var artwork = await service.GetArtworkAsync(game, 640, Ct);
        service.FlushIndex();

        Assert.Equal(GameColors.None, colors);
        Assert.False(service.TryPeek(game, out _));
        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Logo);
        Assert.NotNull(artwork.Logo);
        Assert.DoesNotContain("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loading_images_and_artwork_never_computes_colors()
    {
        RedLogoNightCover("18");
        _art.SteamFile("18", "library_hero.jpg", Bitmap(GeneratedArt.Hero(HeroScene.BlueSky)));
        var service = _art.Service();
        var game = ArtworkFixture.Steam("18");

        await service.GetArtworkAsync(game, 1280, Ct);
        foreach (var kind in Enum.GetValues<ArtworkKind>())
        {
            await service.GetImageAsync(game, kind, 320, Ct);
        }

        service.FlushIndex();
        Assert.Equal(0, service.GameColorsComputed);
        Assert.False(service.TryPeek(game, out _));
        Assert.DoesNotContain("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_old_index_without_colors_computes_them_on_the_first_request()
    {
        RedLogoNightCover("19");
        var game = ArtworkFixture.Steam("19");
        var old = _art.Service();
        await old.GetArtworkAsync(game, 640, Ct);
        old.FlushIndex();
        Assert.DoesNotContain("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);

        var service = _art.Service();
        var colors = await service.GetGameColorsAsync(game, Ct);
        service.FlushIndex();

        Assert.Equal(1, service.GameColorsComputed);
        Assert.True(colors.IsUsable);
        Assert.Contains("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hypothetical_preview_of_the_picker_never_computes_or_stores()
    {
        RedLogoNightCover("20");
        _art.SteamFile("20", "logo_german.png", Bitmap(GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#1E64DC") })));
        var service = _art.Service();
        var game = ArtworkFixture.Steam("20");

        foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover })
        {
            await service.GetCandidatesAsync(game, kind, Ct);
        }

        service.FlushIndex();
        Assert.Equal(0, service.GameColorsComputed);
        Assert.DoesNotContain("\"colors\"", File.ReadAllText(IndexPath), StringComparison.Ordinal);
    }

    private void RedLogo(string appId) =>
        _art.SteamFile(appId, "logo.png", Bitmap(GeneratedArt.Logo(new LogoSpec { Fill = GeneratedArt.C("#D62828"), Outline = 2 })));

    private void RedLogoNightCover(string appId)
    {
        RedLogo(appId);
        _art.SteamFile(appId, "library_600x900.jpg", Bitmap(GeneratedArt.Cover(CoverScene.NightPortrait)));
    }

    private static double Hue(string hex) => ColorScience.ToOklab(Rgba8.Parse(hex)).H;

    private static BitmapSource Bitmap(ArtImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
