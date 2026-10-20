using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.Scanners;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Bilderdienst nach der unabhängigen Prüfung (G10.10): eigenes Symbol erneuert auch die Bilder, große Fotos werden
/// verkleinert statt abgelehnt, Größen vor dem Dekodieren geprüft, SteamGridDB-Bilder nur eingeschaltet, Aufräumen nach
/// entfernten Spielen, ein von Hand geändertes index.json löscht nichts außerhalb von art, Kennung samt Ausschlussliste,
/// Herkunftszeile bei Steam ohne Titelbild und der Produkt-Cache von Ubisoft wird beobachtet.
/// </summary>
public sealed class ArtworkReviewTests : IDisposable
{
    private readonly ArtworkFixture _art = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _art.Dispose();

    [Fact]
    public async Task Changed_own_icon_under_the_same_name_shows_after_forget()
    {
        // „Symbol ändern…“ legt die Datei beim erneuten Ändern unter demselben Namen ab: die Angaben des Spiels bleiben gleich.
        var game = new GameConfig { Source = GameSource.Epic, SourceId = "Crow", Name = "Dead Island 2" };
        game.CustomIcon = CustomIcons.FileNameFor(game.Key);
        var iconFile = Path.Combine(_art.IconDirectory, game.CustomIcon);
        _art.Save(iconFile, Uniform(96, 20));
        var service = _art.Service();
        var changed = new List<string?>();
        service.Changed += (_, e) => changed.Add(e.GameKey);

        var before = await service.GetArtworkAsync(game, 1280, Ct);
        Assert.InRange(ArtworkFixture.Brightness(before.Icon!.Image), 0, 40);

        File.SetLastWriteTimeUtc(_art.Save(iconFile, Uniform(96, 230)), DateTime.UtcNow.AddMinutes(1));
        var stale = await service.GetArtworkAsync(game, 1280, Ct);
        Assert.Same(before, stale); // ohne Vergessen: das alte Bild (so blieb es bis zum Schließen)

        service.Forget(game.Key);
        var after = await service.GetArtworkAsync(game, 1280, Ct);

        Assert.Equal(["Epic:Crow"], changed);
        Assert.InRange(ArtworkFixture.Brightness(after.Icon!.Image), 200, 255);
        Assert.Equal(ArtworkOrigin.Custom, after.Summary.Icon);
    }

    [Fact]
    public void Large_photos_are_downscaled_and_only_oversized_files_are_too_large()
    {
        // 7000 × 6000 (42 MP, über der alten Grenze von 36 MP): wird beim Dekodieren verkleinert und übernommen.
        var photo = Path.Combine(_art.Root, "foto.jpg");
        SaveGray(photo, 7000, 6000);
        var service = _art.Service();
        var game = ArtworkFixture.Steam("10");

        var token = service.SaveCustomImage(game, ArtworkKind.Hero, photo);

        var stored = BitmapFrame.Create(new Uri(Path.Combine(_art.ArtDirectory, "custom", ArtworkOverride.FileOf(token)!)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(ArtworkService.CustomHeroWidth, stored.PixelWidth);
        Assert.InRange(stored.PixelHeight, 3290, 3292);

        // Gegenprobe: 20000 × 20000 laut Kopf (400 MP) – abgelehnt, bevor ein Pixel dekodiert ist, mit eigener Meldung.
        var huge = Path.Combine(_art.Root, "riesig.bmp");
        File.WriteAllBytes(huge, BmpHeader(20000, 20000));
        Assert.Throws<ImageTooLargeException>(() => service.SaveCustomImage(game, ArtworkKind.Hero, huge));
        Assert.Throws<ImageTooLargeException>(() => ArtworkImaging.Decode(huge, 320));

        // Kein Bild bleibt „kein lesbares Bild“.
        var text = Path.Combine(_art.Root, "liesmich.png");
        File.WriteAllText(text, "kein Bild");
        var error = Assert.Throws<InvalidDataException>(() => service.SaveCustomImage(game, ArtworkKind.Hero, text));
        Assert.Equal(ArtworkStrings.Error_NotAnImage, error.Message);
    }

    [Fact]
    public void Steamgriddb_download_is_measured_before_it_is_decoded()
    {
        var service = _art.Service();
        var game = ArtworkFixture.Steam("11");

        var error = Assert.Throws<InvalidDataException>(() =>
            service.StoreSteamGridDbImage(game, ArtworkKind.Hero, BmpHeader(20000, 20000), ".png", DateTimeOffset.UtcNow));

        Assert.IsType<ImageTooLargeException>(error.InnerException); // aus dem Kopf, ohne die Pixel zu dekodieren
        Assert.False(Directory.Exists(Path.Combine(_art.ArtDirectory, "sgdb")));
    }

    [Fact]
    public async Task Steamgriddb_images_count_only_while_steamgriddb_is_on()
    {
        var enabled = true;
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = _art.ArtDirectory,
            Roots = () => _art.Roots,
            WatchCaches = false,
            SteamGridDbEnabled = () => enabled,
        });
        using var dispose = service;
        var game = new GameConfig { Source = GameSource.Gog, SourceId = "77", Name = "Ohne lokale Bilder" };
        var changed = new List<string?>();
        service.Changed += (_, e) => changed.Add(e.GameKey);
        service.StoreSteamGridDbImage(game, ArtworkKind.Hero, Encode(ArtworkFixture.Hero(800, 258)), ".jpg", DateTimeOffset.UtcNow);
        Assert.Equal(ArtworkOrigin.SteamGridDb, (await service.GetArtworkAsync(game, 800, Ct)).Summary.Hero);

        enabled = false;
        changed.Clear();
        service.SteamGridDbSwitched(false);

        Assert.Equal(["Gog:77"], changed); // die Bibliothek lädt das Spiel neu
        var off = await service.GetArtworkAsync(game, 800, Ct);
        Assert.Equal(ArtworkOrigin.Generated, off.Summary.Hero);
        Assert.Single(Directory.GetFiles(Path.Combine(_art.ArtDirectory, "sgdb"))); // für ein erneutes Einschalten

        enabled = true;
        service.SteamGridDbSwitched(true);
        Assert.Equal(ArtworkOrigin.SteamGridDb, (await service.GetArtworkAsync(game, 800, Ct)).Summary.Hero);
    }

    [Fact]
    public void Images_of_games_removed_from_the_library_are_cleaned_up_after_closing()
    {
        IReadOnlyCollection<string> library = ["Steam:1", "Steam:2"];
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = _art.ArtDirectory,
            Roots = () => _art.Roots,
            WatchCaches = false,
            LibraryKeys = () => library,
        });
        using var dispose = service;
        string File(string folder, string name)
        {
            var path = Path.Combine(_art.ArtDirectory, folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, "x");
            return path;
        }

        string[] Files(string key) =>
        [
            File("gen", ArtworkFiles.GeneratedFileName(key, ArtworkKind.Hero)),
            File("logo", ArtworkFiles.LogoFileName(key)),
            File("boost", ArtworkFiles.BoostFileName(key, 1920)),
            File("custom", ArtworkFiles.CustomFileName(key, ArtworkKind.Cover)),
            File("sgdb", ArtworkFiles.SteamGridDbFileName(key, ArtworkKind.Logo, ".png")),
        ];

        var kept = Files("Steam:1");
        var removed = Files("Steam:3");
        var foreign = File("custom", "urlaub.jpg");
        System.IO.File.WriteAllText(Path.Combine(_art.ArtDirectory, "index.json"), "{\"version\":1,\"games\":{\"Steam:1\":{\"signature\":\"a\"},\"Steam:3\":{\"signature\":\"b\"}}}");

        Assert.Equal(5, service.PruneRemovedGames());
        service.FlushIndex();

        Assert.All(kept, f => Assert.True(System.IO.File.Exists(f), f));
        Assert.All(removed, f => Assert.False(System.IO.File.Exists(f), f));
        Assert.True(System.IO.File.Exists(foreign), "fremde Dateien bleiben");
        var index = System.IO.File.ReadAllText(Path.Combine(_art.ArtDirectory, "index.json"));
        Assert.Contains("Steam:1", index, StringComparison.Ordinal);
        Assert.DoesNotContain("Steam:3", index, StringComparison.Ordinal);

        // Gegenprobe: Ist die Bibliothek leer (etwa eine unlesbare Konfiguration), wird nichts gelöscht.
        library = [];
        var orphan = Files("Steam:4");
        Assert.Equal(0, service.PruneRemovedGames());
        Assert.All(orphan, f => Assert.True(System.IO.File.Exists(f), f));
    }

    [Fact]
    public async Task Edited_index_never_deletes_files_outside_the_art_folder()
    {
        var outside = Path.Combine(_art.Root, "wichtig", "urlaub.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "x");
        var json = "{\"version\":1,\"games\":{\"Steam:20\":{\"signature\":\"alt\",\"generated\":{\"hero\":" + System.Text.Json.JsonSerializer.Serialize(outside)
            + "},\"boost\":{\"1920\":\"..\\\\..\\\\wichtig\\\\urlaub.jpg\"},\"logo\":" + System.Text.Json.JsonSerializer.Serialize(outside) + "}}}";
        Directory.CreateDirectory(_art.ArtDirectory);
        File.WriteAllText(Path.Combine(_art.ArtDirectory, "index.json"), json);

        // Geänderte Kennung: abgeleitete Dateien werden verworfen, aber nur schlichte Namen in ihren Ordnern unter art.
        await _art.Service().GetArtworkAsync(ArtworkFixture.Steam("20"), 640, Ct);

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task Steam_game_with_an_exe_icon_says_steam_has_no_title_image()
    {
        var installDir = Path.Combine(_art.Root, "SteamGame");
        Directory.CreateDirectory(installDir);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), Path.Combine(installDir, "Spiel.exe"));
        var game = new GameConfig { Source = GameSource.Steam, SourceId = "2001", Name = "Everything is Crab", InstallDir = installDir };

        var artwork = await _art.Service().GetArtworkAsync(game, 1280, Ct);

        Assert.Equal(ArtworkOrigin.Generated, artwork.Summary.Hero);
        Assert.Equal(ArtworkStrings.Summary_SteamNoHero, artwork.Summary.Description); // nicht „Steam: kein Bildcache“
        Assert.Contains("Spiel.exe", artwork.Icon!.SourceLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_helper_list_counts_in_the_identity_of_a_game()
    {
        _art.SteamFile("30", "library_hero.jpg", ArtworkFixture.Hero());
        IReadOnlyList<string>? helpers = null;
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = _art.ArtDirectory,
            Roots = () => _art.Roots,
            WatchCaches = false,
            HelperPatterns = () => helpers,
        });
        using var dispose = service;
        var game = ArtworkFixture.Steam("30");
        await service.GetArtworkAsync(game, 640, Ct);
        service.FlushIndex();
        var signature = SignatureOf("Steam:30");
        Assert.NotNull(service.GetCachedArtwork(game));

        helpers = ["*launcher*.exe"];

        Assert.Null(service.GetCachedArtwork(game)); // anderes Symbol aus der EXE möglich: neu ermitteln
        await service.GetArtworkAsync(game, 640, Ct);
        service.FlushIndex();
        Assert.NotEqual(signature, SignatureOf("Steam:30"));

        // Gegenprobe: Mit der Standardliste (null) bleibt die Kennung wie ohne Liste.
        var located = LocatedArtwork.None;
        Assert.Equal(ArtworkIndex.Signature(game, located, _ => "x"), ArtworkIndex.Signature(game, located, _ => "x", helperPatterns: null));
        Assert.NotEqual(ArtworkIndex.Signature(game, located, _ => "x"), ArtworkIndex.Signature(game, located, _ => "x", helperPatterns: ["*.exe"]));
    }

    [Fact]
    public async Task New_ubisoft_game_in_the_product_cache_shows_its_images_without_restart()
    {
        var launcher = Path.Combine(_art.Root, "Ubisoft Game Launcher");
        var configurations = Path.Combine(launcher, "cache", "configuration", "configurations");
        var assets = Path.Combine(launcher, "cache", "assets");
        Directory.CreateDirectory(Path.GetDirectoryName(configurations)!);
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(configurations, new UbisoftCacheBuilder().Add(101, "root:\n  name: Anderes Spiel\n").Build());
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = _art.ArtDirectory,
            Roots = () => new ArtworkRoots(_art.SteamRoot, null, launcher),
            WatchCaches = true,
            WatchDebounce = TimeSpan.FromMilliseconds(150),
        });
        using var dispose = service;
        var signal = new SemaphoreSlim(0);
        var changed = new List<string?>();
        service.Changed += (_, e) =>
        {
            lock (changed)
            {
                changed.Add(e.GameKey);
            }

            signal.Release();
        };
        var game = new GameConfig { Source = GameSource.Ubisoft, SourceId = "4740", Name = "Neu installiert" };
        using var lease = service.Acquire();
        Assert.Equal(ArtworkOrigin.Generated, (await service.GetArtworkAsync(game, 640, Ct)).Summary.Hero);

        // Ubisoft Connect installiert das Spiel: neues Titelbild in cache\assets, der Produkt-Cache nennt es.
        _art.Save(Path.Combine(assets, "d491.jpg"), ArtworkFixture.Hero());
        File.WriteAllBytes(configurations, new UbisoftCacheBuilder().Add(101, "root:\n  name: Anderes Spiel\n").Add(4740, "root:\n  background_image: d491.jpg\n").Build());

        for (var i = 0; i < 40; i++)
        {
            lock (changed)
            {
                if (changed.Contains("Ubisoft:4740"))
                {
                    break;
                }
            }

            await signal.WaitAsync(TimeSpan.FromMilliseconds(250), Ct);
        }

        lock (changed)
        {
            Assert.Contains("Ubisoft:4740", changed);
        }

        var after = await service.GetArtworkAsync(game, 640, Ct);
        Assert.Equal(ArtworkOrigin.Launcher, after.Summary.Hero);
    }

    private string? SignatureOf(string key)
    {
        using var index = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_art.ArtDirectory, "index.json")));
        return index.RootElement.GetProperty("games").GetProperty(key).GetProperty("signature").GetString();
    }

    /// <summary>Einfarbiges, deckendes Bild in dieser Helligkeit.</summary>
    private static BitmapSource Uniform(int size, byte value) => ArtworkFixture.Image(size, size, (_, _) => (value, value, value, (byte)255));

    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Großes Graustufen-JPEG (wenig Speicher beim Erzeugen, klein auf der Platte).</summary>
    private static void SaveGray(string path, int width, int height)
    {
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Array.Fill(pixels, (byte)(40 + (y * 160 / height)), y * width, width);
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        var encoder = new JpegBitmapEncoder { QualityLevel = 60 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Nur der Kopf einer BMP-Datei (24 Bit) mit dieser Größe, ohne Pixel: so groß wirkt sie nur laut Kopf.</summary>
    private static byte[] BmpHeader(int width, int height)
    {
        var data = new byte[54 + 64];
        var stride = ((width * 3) + 3) / 4 * 4;
        var size = 54L + ((long)stride * height);
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes((int)Math.Min(int.MaxValue, size)).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(width).CopyTo(data, 18);
        BitConverter.GetBytes(height).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        return data;
    }
}
