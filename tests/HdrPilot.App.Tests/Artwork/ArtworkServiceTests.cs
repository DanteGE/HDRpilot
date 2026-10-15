using System.Text.Json;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Bilderdienst (E89, G10.3) mit nachgebauten Launcher-Caches: lokale Bilder in höchstens der angefragten Breite, Ersatzbilder
/// statt leerer Flächen, verschmierte Kapseln, zugeschnittene Logos, Umgebungsfarben und helle HDR-Fassung einmal berechnet
/// und gemerkt, eigene Bilder dauerhaft, neue Cache-Dateien erscheinen von selbst.
/// </summary>
public sealed class ArtworkServiceTests : IDisposable
{
    private readonly ArtworkFixture _art = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _art.Dispose();

    [Fact]
    public async Task Steam_images_load_from_the_cache_at_most_at_the_requested_width()
    {
        _art.SteamFile("100", "library_hero.jpg", ArtworkFixture.Hero(640, 206));
        _art.SteamFile("100", "library_600x900.jpg", ArtworkFixture.Cover(300, 450));
        var service = _art.Service();
        var game = ArtworkFixture.Steam("100");
        game.IconCandidates = [_art.Save(Path.Combine(_art.Root, "steam-games", "100.png"), ArtworkFixture.Icon(64))];

        var small = (BitmapSource)(await service.GetImageAsync(game, ArtworkKind.Hero, 320, Ct))!;
        var large = (BitmapSource)(await service.GetImageAsync(game, ArtworkKind.Hero, 3000, Ct))!;
        var artwork = await service.GetArtworkAsync(game, 640, Ct);

        Assert.Equal(320, small.PixelWidth);
        Assert.True(small.IsFrozen);
        Assert.Equal(640, large.PixelWidth); // nie hochskaliert
        Assert.Same(small, service.GetCachedImage(game, ArtworkKind.Hero, 320));
        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Hero);
        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Cover);
        Assert.Equal("Lokal aus dem Steam-Cache · nichts heruntergeladen", artwork.Summary.Description);
        Assert.Equal("Steam-Cache", artwork.Hero!.SourceLabel);
        Assert.Same(artwork, service.GetCachedArtwork(game));
        Assert.False(Directory.Exists(Path.Combine(_art.ArtDirectory, "gen")), "für ein Spiel mit allen Bildern wird nichts erzeugt");
    }

    [Fact]
    public async Task Missing_images_are_generated_and_never_empty()
    {
        var service = _art.Service();
        var game = new GameConfig { Source = GameSource.Epic, SourceId = "ohne-bilder", Name = "Red Dead Redemption 2" };

        var artwork = await service.GetArtworkAsync(game, 1920, Ct);
        var header = await service.GetImageAsync(game, ArtworkKind.Header, 460, Ct);

        Assert.NotNull(artwork.Hero);
        Assert.NotNull(artwork.Cover);
        Assert.NotNull(artwork.Field);
        Assert.NotNull(artwork.Icon);
        Assert.NotNull(header);
        Assert.Null(artwork.Logo);
        Assert.True(artwork.Summary.LogoIsName);
        Assert.Equal(ArtworkOrigin.Generated, artwork.Summary.Hero);
        Assert.Equal(ArtworkOrigin.Generated, artwork.Summary.Icon); // Monogramm
        Assert.Equal((1920, 620), (artwork.Hero.PixelWidth, artwork.Hero.PixelHeight));
        Assert.Equal((400, 600), (artwork.Cover.PixelWidth, artwork.Cover.PixelHeight));
        Assert.Equal(256, artwork.Icon.PixelWidth);

        var gen = Path.Combine(_art.ArtDirectory, "gen");
        var stem = ArtworkFiles.Stem(game.Key);
        Assert.True(File.Exists(Path.Combine(gen, stem + "-hero.jpg")));
        Assert.True(File.Exists(Path.Combine(gen, stem + "-icon.png")));
        var cover = BitmapFrame.Create(new Uri(Path.Combine(gen, stem + "-cover.jpg")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal((600, 900), (cover.PixelWidth, cover.PixelHeight));
        Assert.Contains("kein lokales Titelbild", artwork.Summary.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task German_logo_wins_and_is_trimmed_to_its_visible_part()
    {
        _art.SteamFile("200", "library_hero.jpg", ArtworkFixture.Hero());
        _art.SteamFile("200", "logo.png", ArtworkFixture.Logo(400, 200, 0, 0, 400, 200));
        _art.SteamFile("200", "logo_german.png", ArtworkFixture.Logo(400, 200, 100, 50, 200, 60));
        var service = _art.Service();

        var artwork = await service.GetArtworkAsync(ArtworkFixture.Steam("200"), 1200, Ct);
        service.FlushIndex();

        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Logo);
        Assert.Equal((204, 64), (artwork.Logo!.PixelWidth, artwork.Logo.PixelHeight)); // 200×60 plus 2 px Rand
        var trimmed = Path.Combine(_art.ArtDirectory, "logo", ArtworkFiles.LogoFileName("Steam:200"));
        Assert.True(File.Exists(trimmed));
        Assert.Contains("\"logoTrim\":[98,48,204,64]", File.ReadAllText(Path.Combine(_art.ArtDirectory, "index.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logo_without_transparent_margin_is_used_as_is()
    {
        _art.SteamFile("201", "library_hero.jpg", ArtworkFixture.Hero());
        _art.SteamFile("201", "logo.png", ArtworkFixture.Logo(300, 100, 0, 0, 300, 100));
        var service = _art.Service();

        var artwork = await service.GetArtworkAsync(ArtworkFixture.Steam("201"), 1200, Ct);

        Assert.Equal(300, artwork.Logo!.PixelWidth);
        Assert.False(File.Exists(Path.Combine(_art.ArtDirectory, "logo", ArtworkFiles.LogoFileName("Steam:201"))));
    }

    [Fact]
    public async Task Smeared_capsule_is_replaced_by_a_crop_of_the_hero()
    {
        _art.SteamFile("300", "library_hero.jpg", ArtworkFixture.Hero(1200, 388));
        _art.SteamFile("300", "library_capsule.jpg", ArtworkFixture.SmearedCapsule());
        _art.SteamFile("301", "library_hero.jpg", ArtworkFixture.Hero(1200, 388));
        _art.SteamFile("301", "library_capsule.jpg", ArtworkFixture.Cover());
        var service = _art.Service();

        var smeared = await service.GetArtworkAsync(ArtworkFixture.Steam("300", "Blue Estate"), 1200, Ct);
        var normal = await service.GetArtworkAsync(ArtworkFixture.Steam("301"), 1200, Ct);
        var coverage = await service.GetCoverageAsync([ArtworkFixture.Steam("300"), ArtworkFixture.Steam("301")], Ct);

        Assert.True(smeared.Summary.CoverFromSmear);
        Assert.Equal(ArtworkOrigin.Generated, smeared.Summary.Cover);
        Assert.Equal(400, smeared.Cover!.PixelWidth);

        // Gegenprobe: ein echtes Motiv bleibt das Launcher-Cover.
        Assert.False(normal.Summary.CoverFromSmear);
        Assert.Equal(ArtworkOrigin.Launcher, normal.Summary.Cover);
        var row = Assert.Single(coverage.Rows);
        Assert.Equal(new ArtworkCount(Used: 1, Found: 2), row.Cover); // verschmiert: gefunden, nicht genutzt
        Assert.Equal(new ArtworkCount(2, 2), row.Hero);
    }

    [Fact]
    public async Task Ambient_colors_are_computed_once_and_read_back_from_the_index()
    {
        var hero = _art.SteamFile("400", "library_hero.jpg", ArtworkFixture.Hero());
        var stamps = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? Stamp(string path) => stamps.TryGetValue(path, out var s) ? s : File.Exists(path) ? "fest" : null;
        var game = ArtworkFixture.Steam("400");
        var firstService = _art.Service(stamp: Stamp);
        var first = await firstService.GetArtworkAsync(game, 640, Ct);
        firstService.FlushIndex();
        Assert.InRange(first.Ambient.LightX, 0.6, 0.8);

        // Datei unlesbar machen, Kennung gleich lassen: gelesen wird jetzt nur noch das Verzeichnis.
        File.WriteAllText(hero, "kein Bild mehr");
        var second = _art.Service(stamp: Stamp);
        var again = await second.GetArtworkAsync(game, 640, Ct);

        Assert.Equal(first.Ambient, again.Ambient);
        Assert.NotEqual(AmbientColors.FromName(game.Name), again.Ambient);
        Assert.NotNull(again.Hero); // unlesbares Titelbild → Ersatzbild statt Lücke

        // Gegenprobe: geänderte Kennung (Steam hat das Bild ersetzt) verwirft die gemerkten Farben.
        stamps[hero] = "neu";
        var third = await _art.Service(stamp: Stamp).GetArtworkAsync(game, 640, Ct);
        Assert.NotEqual(first.Ambient, third.Ambient);
    }

    [Fact]
    public async Task Bright_hdr_variant_is_computed_lazily_and_stored()
    {
        _art.SteamFile("500", "library_hero.jpg", ArtworkFixture.Solid(1280, 413, colorful: true));
        var service = _art.Service();
        var game = ArtworkFixture.Steam("500");
        var boostDir = Path.Combine(_art.ArtDirectory, "boost");

        await service.GetImageAsync(game, ArtworkKind.Hero, 1280, Ct);
        Assert.False(Directory.Exists(boostDir), "nicht für jedes geladene Titelbild");

        var artwork = await service.GetArtworkAsync(game, 1000, Ct);

        Assert.NotNull(artwork.HeroBoost);
        Assert.Equal(artwork.Hero!.PixelWidth, artwork.HeroBoost!.PixelWidth);
        Assert.True(ArtworkFixture.Brightness(artwork.HeroBoost.Image) > ArtworkFixture.Brightness(artwork.Hero.Image) + 10);
        Assert.Equal(ArtworkFiles.BoostFileName(game.Key, 1280), Path.GetFileName(Assert.Single(Directory.GetFiles(boostDir))));
    }

    [Fact]
    public async Task Custom_images_are_downscaled_persisted_and_win()
    {
        var source = _art.Save(Path.Combine(_art.Root, "eigen.png"), ArtworkFixture.Solid(4800, 900, colorful: true));
        _art.SteamFile("600", "library_hero.jpg", ArtworkFixture.Hero());
        var service = _art.Service();
        var game = ArtworkFixture.Steam("600");

        var heroToken = service.SaveCustomImage(game, ArtworkKind.Hero, source);
        var coverToken = service.SaveCustomImage(game, ArtworkKind.Cover, source);
        game.Artwork = new GameArtworkChoice { Hero = heroToken, Cover = coverToken };
        var artwork = await service.GetArtworkAsync(game, 3840, Ct);

        Assert.Equal(ArtworkOverrideKind.Custom, ArtworkOverride.KindOf(heroToken));
        var heroFile = Path.Combine(_art.ArtDirectory, "custom", ArtworkOverride.FileOf(heroToken)!);
        var stored = BitmapFrame.Create(new Uri(heroFile), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(3840, stored.PixelWidth); // höchstens 3840 breit
        var cover = BitmapFrame.Create(new Uri(Path.Combine(_art.ArtDirectory, "custom", ArtworkOverride.FileOf(coverToken)!)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal((600, 900), (cover.PixelWidth, cover.PixelHeight));
        Assert.Equal(ArtworkOrigin.Custom, artwork.Summary.Hero);
        Assert.Equal(ArtworkOrigin.Custom, artwork.Summary.Cover);
        Assert.Equal("Eigene Datei · nur auf diesem PC", artwork.Summary.Description);
        Assert.True(File.Exists(source), "die gewählte Datei bleibt unverändert");

        // Löschen nur eigener Dateien: eine fremde Datei im selben Ordner bleibt.
        var foreign = Path.Combine(_art.ArtDirectory, "custom", "urlaub.jpg");
        File.WriteAllText(foreign, "x");
        service.DeleteCustomImage(heroToken);
        service.DeleteCustomImage("custom:urlaub.jpg");
        Assert.False(File.Exists(heroFile));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void Custom_image_that_is_no_image_is_rejected()
    {
        var text = Path.Combine(_art.Root, "liesmich.png");
        File.WriteAllText(text, "kein Bild");

        Assert.Throws<InvalidDataException>(() => _art.Service().SaveCustomImage(ArtworkFixture.Steam("1"), ArtworkKind.Logo, text));
        Assert.Throws<ArgumentOutOfRangeException>(() => _art.Service().SaveCustomImage(ArtworkFixture.Steam("1"), ArtworkKind.Icon, text));
    }

    [Fact]
    public async Task Xbox_splash_is_offered_with_a_note_and_the_tile_becomes_the_cover()
    {
        var resources = Path.Combine(_art.Root, "XboxGames", "Hellraiser", "Content", "Resources");
        var tile = _art.Save(Path.Combine(resources, "Square480x480Logo.png"), ArtworkFixture.Solid(480, 480, colorful: true));
        _art.Save(Path.Combine(resources, "SplashScreen.png"), ArtworkFixture.Solid(620, 300, colorful: false));
        var game = new GameConfig { Source = GameSource.Xbox, SourceId = "Pkg_1", Name = "Clive Barker's Hellraiser: Revival", InstallDir = Path.GetDirectoryName(resources)!, IconCandidates = [tile] };
        var service = _art.Service();

        var artwork = await service.GetArtworkAsync(game, 1920, Ct);
        var candidates = await service.GetCandidatesAsync(game, ArtworkKind.Hero, Ct);

        Assert.Equal(ArtworkOrigin.Generated, artwork.Summary.Hero);
        Assert.Equal("Xbox-Paket: Kachelbild 480 px · Titelbild und Cover daraus erzeugt", artwork.Summary.Description);
        Assert.Equal(ArtworkOrigin.Launcher, artwork.Summary.Icon);
        Assert.Equal(["Automatisch", "SplashScreen.png (Xbox-Paket)", "Erzeugtes Bild"], candidates.Select(c => c.Label));
        Assert.True(candidates[0].IsCurrent);
        Assert.Equal("Schwarzweiß – vermutlich Engine-Logo", candidates[1].Note);
        Assert.Equal("alt:SplashScreen.png", candidates[1].Token);
        Assert.All(candidates, c => Assert.True(c.Preview is BitmapSource { PixelWidth: <= ArtworkService.PreviewSize }));

        // Wahl des Splash: das Titelbild kommt dann aus dem Paket, die Wahl ist markiert.
        game.Artwork = new GameArtworkChoice { Hero = candidates[1].Token };
        var chosen = await service.GetArtworkAsync(game, 1920, Ct);
        var again = await service.GetCandidatesAsync(game, ArtworkKind.Hero, Ct);
        Assert.Equal(ArtworkOrigin.Alternative, chosen.Summary.Hero);
        Assert.Equal(620, chosen.Hero!.PixelWidth);
        Assert.True(again[1].IsCurrent);
        Assert.False(again[0].IsCurrent);
    }

    [Fact]
    public async Task Coloured_splash_is_described_as_title_card()
    {
        var resources = Path.Combine(_art.Root, "XboxGames", "Wolkenhof", "Content", "Resources");
        _art.Save(Path.Combine(resources, "SplashScreen.png"), ArtworkFixture.Solid(620, 300, colorful: true));
        var game = new GameConfig { Source = GameSource.Xbox, SourceId = "Pkg_2", Name = "Wolkenhof", InstallDir = Path.GetDirectoryName(resources)! };

        var candidates = await _art.Service().GetCandidatesAsync(game, ArtworkKind.Hero, Ct);

        Assert.Equal("Titelkarte mit Schriftzug in der Mitte", candidates[1].Note);
        Assert.Empty(await _art.Service().GetCandidatesAsync(game, ArtworkKind.Icon, Ct));
    }

    [Fact]
    public async Task New_files_in_a_watched_cache_raise_changed_and_watching_ends_with_the_last_lease()
    {
        Directory.CreateDirectory(Path.Combine(_art.LibraryCache, "700"));
        var service = _art.Service(watch: true);
        var changed = new List<string?>();
        var signal = new SemaphoreSlim(0);
        service.Changed += (_, e) =>
        {
            lock (changed)
            {
                changed.Add(e.GameKey);
            }

            signal.Release();
        };
        var game = ArtworkFixture.Steam("700");
        Assert.False(service.IsWatching);

        var lease = service.Acquire();
        var before = await service.GetArtworkAsync(game, 640, Ct);
        Assert.True(service.IsWatching);
        Assert.Equal(ArtworkOrigin.Generated, before.Summary.Hero);

        _art.SteamFile("700", "library_hero.jpg", ArtworkFixture.Hero());
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(10), Ct), "Änderung gemeldet");
        lock (changed)
        {
            Assert.Contains("Steam:700", changed);
        }

        var after = await service.GetArtworkAsync(game, 640, Ct);
        Assert.Equal(ArtworkOrigin.Launcher, after.Summary.Hero);

        // Ohne Fenster: keine Beobachtung, kein Bildspeicher, keine Meldungen mehr.
        lease.Dispose();
        Assert.False(service.IsWatching);
        Assert.Equal(0, service.MemoryBytes);
        lock (changed)
        {
            changed.Clear();
        }

        while (signal.CurrentCount > 0)
        {
            signal.Wait(0, Ct);
        }

        _art.SteamFile("700", "logo.png", ArtworkFixture.Logo(100, 50, 10, 10, 50, 20));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(800), Ct));
        await WaitUntil(() => service.Workers == 0);
    }

    [Fact]
    public async Task Coverage_and_examples_use_real_games_of_the_library()
    {
        _art.SteamFile("800", "library_hero.jpg", ArtworkFixture.Hero(1200, 388));
        _art.SteamFile("800", "library_capsule.jpg", ArtworkFixture.SmearedCapsule());
        _art.SteamFile("801", "library_hero.jpg", ArtworkFixture.Hero());
        _art.SteamFile("801", "logo.png", ArtworkFixture.Logo(100, 50, 10, 10, 50, 20));
        var icon = _art.Save(Path.Combine(_art.Root, "epic", "icon.png"), ArtworkFixture.Icon(256));
        var games = new List<GameConfig>
        {
            ArtworkFixture.Steam("800", "Blue Estate"),
            ArtworkFixture.Steam("801", "Mit Logo"),
            new() { Source = GameSource.Epic, SourceId = "e1", Name = "Battlefield 6", IconCandidates = [icon], LastPlayed = DateTimeOffset.UtcNow },
            new() { Source = GameSource.Manual, SourceId = "m1", Name = "Ganz ohne Bild" },
        };

        var service = _art.Service();
        var coverage = await service.GetCoverageAsync(games, Ct);
        var examples = await service.GetFallbackExamplesAsync(games, Ct);

        var steam = coverage.Rows.Single(r => r.Source == GameSource.Steam);
        Assert.Equal(2, steam.Games);
        Assert.Equal(new ArtworkCount(1, 1), steam.Logo);
        var epic = coverage.Rows.Single(r => r.Source == GameSource.Epic);
        Assert.Equal(new ArtworkCount(0, 0), epic.Hero);
        Assert.Equal(new ArtworkCount(1, 1), epic.Icon);
        Assert.Equal(new ArtworkCount(0, 0), coverage.Rows.Single(r => r.Source == GameSource.Manual).Icon);

        Assert.Equal("Blue Estate", examples.Single(e => e.Kind == FallbackKind.CoverFromSmearedCapsule).GameName);
        var heroFromIcon = examples.Single(e => e.Kind == FallbackKind.HeroFromIcon);
        Assert.Equal("Battlefield 6", heroFromIcon.GameName);
        Assert.Equal("Epic Games · aus icon.png · 256 px", heroFromIcon.Detail);
        Assert.Equal("Ganz ohne Bild", examples.Single(e => e.Kind == FallbackKind.Monogram).GameName);
        Assert.Equal(examples.OrderBy(e => e.Kind), examples);
    }

    [Fact]
    public async Task Index_is_written_and_cleared_from_memory_when_the_last_window_closes()
    {
        _art.SteamFile("900", "library_hero.jpg", ArtworkFixture.Hero());
        var service = _art.Service();
        var lease = service.Acquire();
        await service.GetArtworkAsync(ArtworkFixture.Steam("900"), 640, Ct);
        Assert.True(service.MemoryBytes > 0);

        lease.Dispose();

        await WaitUntil(() => File.Exists(Path.Combine(_art.ArtDirectory, "index.json")));
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(_art.ArtDirectory, "index.json")));
        Assert.Equal(1, index.RootElement.GetProperty("version").GetInt32());
        Assert.True(index.RootElement.GetProperty("games").TryGetProperty("Steam:900", out var entry));
        Assert.Equal("#", entry.GetProperty("ambient").GetProperty("a1").GetString()![..1]);
        Assert.Equal(0, service.MemoryBytes);
        Assert.Null(service.GetCachedArtwork(ArtworkFixture.Steam("900")));
    }

    [Fact]
    public void Memory_cache_evicts_the_least_recently_used_images()
    {
        var cache = new ImageMemoryCache(budget: 3 * 100 * 100 * 4);
        var image = ArtworkFixture.Solid(100, 100, colorful: true);
        cache.Set("Steam:1|a", image);
        cache.Set("Steam:1|b", image);
        cache.Set("Steam:2|c", image);
        Assert.True(cache.TryGet("Steam:1|a", out _)); // a frisch benutzt

        cache.Set("Steam:2|d", image);

        Assert.False(cache.TryGet("Steam:1|b", out _));
        Assert.True(cache.TryGet("Steam:1|a", out _));
        Assert.Equal(3 * 100 * 100 * 4, cache.Bytes);
        cache.RemovePrefix("steam:2|");
        Assert.Equal(1, cache.Count);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25, Ct);
        }

        Assert.True(condition());
    }
}
