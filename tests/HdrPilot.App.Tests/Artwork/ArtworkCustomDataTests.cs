using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Eigene Bilder aus Bytes (E93, G14.2): ein von SteamGridDB übernommenes Bild wird ohne Temp-Datei genauso gespeichert wie
/// eine eigene Datei, Bytes werden nach denselben Regeln dekodiert, und eine geänderte Zuordnung vergisst die Bilder der
/// alten. Nur erzeugte Bilder.
/// </summary>
public sealed class ArtworkCustomDataTests : IDisposable
{
    private readonly ArtworkFixture _art = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _art.Dispose();

    [Fact]
    public async Task Decoding_bytes_follows_the_file_rules()
    {
        var png = Encode(ArtworkFixture.Solid(800, 400, colorful: true), png: true);

        var small = ArtworkImaging.Decode(png, 200);
        var full = ArtworkImaging.Decode(png, 4000);
        var elsewhere = await Task.Run(() => ArtworkImaging.Decode(png, 100), Ct);

        Assert.Equal((200, 100), (small.PixelWidth, small.PixelHeight));
        Assert.True(small.IsFrozen);
        Assert.Equal(800, full.PixelWidth); // nie hochskaliert
        Assert.True(elsewhere.IsFrozen);
        Assert.Equal(100, elsewhere.PixelWidth);

        // Über 32 MB, über 100 Megapixel laut Kopf, oder kein Bild: wie bei Dateien.
        Assert.Throws<ImageTooLargeException>(() => ArtworkImaging.Decode(new byte[ArtworkImaging.MaxFileBytes + 1], 100));
        Assert.Throws<ImageTooLargeException>(() => ArtworkImaging.Decode(TestPng.Gray(20000, 20000, headerOnly: true), 100));
        var garbage = Record.Exception(() => ArtworkImaging.Decode("kein Bild"u8.ToArray(), 100));
        Assert.NotNull(garbage);
        Assert.True(ArtworkImaging.IsDecodeFailure(garbage));
    }

    [Fact]
    public void Validate_with_the_save_width_rejects_what_the_save_would_refuse()
    {
        // 600 × 61000 hat weniger als 100 MP, wäre aber in der Speicherbreite (Cover 1800, also ganz) über 36 MP.
        var tall = TestPng.Gray(600, 61000);

        Assert.Equal((600, 61000), ArtworkImaging.Validate(tall, ArtworkImaging.MaxSourcePixels));
        Assert.Throws<ImageTooLargeException>(() => ArtworkImaging.Validate(tall, ArtworkImaging.MaxSourcePixels, ArtworkService.CustomSaveWidth(ArtworkKind.Cover)));
        Assert.Equal(3840, ArtworkService.CustomSaveWidth(ArtworkKind.Hero));
        Assert.Equal(1280, ArtworkService.CustomSaveWidth(ArtworkKind.Logo));
        Assert.Equal(ArtworkRenderer.CoverWidth * 3, ArtworkService.CustomSaveWidth(ArtworkKind.Cover));
    }

    [Theory]
    [InlineData(ArtworkKind.Cover)]
    [InlineData(ArtworkKind.Hero)]
    [InlineData(ArtworkKind.Logo)]
    public void Saving_custom_image_data_matches_saving_a_file(ArtworkKind kind)
    {
        var image = kind switch
        {
            ArtworkKind.Cover => ArtworkFixture.Cover(342, 482),
            ArtworkKind.Hero => ArtworkFixture.Hero(1920, 620),
            _ => ArtworkFixture.Logo(800, 310, 40, 60, 700, 180),
        };
        var data = Encode(image, png: true);
        var source = Path.Combine(_art.Root, "quelle.png");
        File.WriteAllBytes(source, data);
        var service = _art.Service();
        var game = ArtworkFixture.Steam("700", "Nebelfahrt");

        var fromData = service.SaveCustomImageData(game, kind, data);
        var dataFile = Path.Combine(_art.ArtDirectory, "custom", ArtworkOverride.FileOf(fromData)!);
        var dataBytes = File.ReadAllBytes(dataFile);
        var fromFile = service.SaveCustomImage(game, kind, source);
        var fileBytes = File.ReadAllBytes(dataFile);

        Assert.Equal(fromFile, fromData);
        Assert.Equal(ArtworkOverrideKind.Custom, ArtworkOverride.KindOf(fromData));
        Assert.Equal(fileBytes, dataBytes);
        var stored = BitmapFrame.Create(new MemoryStream(dataBytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        switch (kind)
        {
            case ArtworkKind.Cover:
                Assert.Equal((600, 900), (stored.PixelWidth, stored.PixelHeight)); // auf 2:3 zugeschnitten
                break;
            case ArtworkKind.Hero:
                Assert.Equal((1920, 620), (stored.PixelWidth, stored.PixelHeight));
                break;
            default:
                Assert.EndsWith(".png", dataFile, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, ArtworkImaging.ToBgra(stored).Pixels[3]); // Ecke bleibt durchsichtig
                break;
        }

        Assert.Throws<InvalidDataException>(() => service.SaveCustomImageData(game, kind, "kein Bild"u8.ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SaveCustomImageData(game, ArtworkKind.Icon, data));
    }

    [Fact]
    public void Default_save_custom_image_data_is_not_supported()
    {
        IArtworkService none = NullArtworkService.Instance;

        Assert.Throws<NotSupportedException>(() => none.SaveCustomImageData(ArtworkFixture.Steam("1"), ArtworkKind.Cover, [1, 2, 3]));
    }

    [Fact]
    public void Replacing_the_steamgriddb_game_drops_old_images_and_misses()
    {
        var service = _art.Service();
        var game = new HdrPilot.Core.Config.GameConfig { Source = HdrPilot.Core.Games.GameSource.Gog, SourceId = "1207658924", Name = "Kupferwald" };
        var when = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        service.SetSteamGridDbGameId(game, 9101);
        service.StoreSteamGridDbImage(game, ArtworkKind.Hero, Encode(ArtworkFixture.Hero(), png: true), ".png", when);
        service.MarkSteamGridDbChecked(game, ArtworkKind.Logo, when);
        var file = Path.Combine(_art.ArtDirectory, ArtworkFiles.SteamGridDbFolder, ArtworkFiles.SteamGridDbFileName(game.Key, ArtworkKind.Hero, ".png"));
        Assert.True(File.Exists(file));
        var changed = new List<string?>();
        service.Changed += (_, e) => changed.Add(e.GameKey);

        service.ReplaceSteamGridDbGame(game, 9101); // gleiche ID: nichts
        Assert.True(File.Exists(file));
        Assert.Equal(when, service.SteamGridDbChecked(game, ArtworkKind.Logo));
        Assert.Empty(changed);

        service.ReplaceSteamGridDbGame(game, 9102);

        Assert.False(File.Exists(file));
        Assert.Equal(9102, service.SteamGridDbGameId(game));
        Assert.Null(service.SteamGridDbChecked(game, ArtworkKind.Logo));
        Assert.Null(service.SteamGridDbChecked(game, ArtworkKind.Hero));
        Assert.Contains(game.Key, changed);
    }

    [Fact]
    public void Images_and_ids_of_an_old_match_are_refused_under_the_index_lock()
    {
        var service = _art.Service();
        var game = new HdrPilot.Core.Config.GameConfig { Source = HdrPilot.Core.Games.GameSource.Gog, SourceId = "1207658925", Name = "Kupferwald" };
        var when = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var png = Encode(ArtworkFixture.Hero(), png: true);
        var file = Path.Combine(_art.ArtDirectory, ArtworkFiles.SteamGridDbFolder, ArtworkFiles.SteamGridDbFileName(game.Key, ArtworkKind.Hero, ".png"));

        // Noch keine ID: Ein Lauf setzt die seiner Namenssuche, eine schon gesetzte (vom Nutzer gewählte) nie.
        Assert.True(service.TrySetSteamGridDbGameId(game, expected: null, 9101));
        Assert.False(service.TrySetSteamGridDbGameId(game, expected: null, 9200));
        Assert.Equal(9101, service.SteamGridDbGameId(game));

        // Ein Bild für eine andere als die geltende ID wird weder geschrieben noch eingetragen.
        Assert.False(service.TryStoreSteamGridDbImage(game, ArtworkKind.Hero, png, ".png", when, expectedGameId: 9102));
        Assert.False(File.Exists(file));
        Assert.Null(service.SteamGridDbChecked(game, ArtworkKind.Hero));

        Assert.True(service.TryStoreSteamGridDbImage(game, ArtworkKind.Hero, png, ".png", when, expectedGameId: 9101));
        Assert.True(File.Exists(file));
        Assert.Equal(when, service.SteamGridDbChecked(game, ArtworkKind.Hero));
        Assert.True(service.TrySetSteamGridDbGameId(game, expected: 9101, 9102));
        Assert.Equal(9102, service.SteamGridDbGameId(game));
    }

    private static byte[] Encode(BitmapSource image, bool png)
    {
        BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(png ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
