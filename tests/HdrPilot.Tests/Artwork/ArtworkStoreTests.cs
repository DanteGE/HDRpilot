using System.Buffers.Binary;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Zwischenspeicher der Spielbilder (E89): Verzeichnis index.json mit Kennung je Spiel, Dateinamen im Ordner art, Größe aus dem
/// Dateikopf und die Zuordnung geänderter Cache-Dateien zu Spielen (neue Bilder erscheinen von selbst).
/// </summary>
public sealed class ArtworkStoreTests
{
    private const string IndexPath = @"C:\Data\art\index.json";

    [Fact]
    public void Index_round_trips_derived_data()
    {
        var fs = new InMemoryFileSystem();
        var index = new ArtworkIndex(fs, IndexPath);
        var data = new ArtworkIndexData { SteamGridDbLastRun = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero) };
        var entry = new ArtworkIndexEntry
        {
            Signature = "ABC",
            Icon = new IndexedIcon { Path = @"C:\x.ico", Px = 256, Kind = "ico" },
            CoverSmeared = true,
            Logo = "Steam_1_00000000.png",
            LogoTrim = [1, 2, 3, 4],
            Ambient = new IndexedAmbient { A1 = "#101010", A2 = "#FF0000", A3 = "#FFFF00", LightX = 0.5, Field = "112233" + new string('0', 6 * 23), FieldWidth = 8, FieldHeight = 3, FromHero = true },
            SteamGridDb = new IndexedSteamGridDb { GameId = 42 },
        };
        entry.Located["hero"] = new IndexedImage { Path = @"C:\h.jpg", Width = 3840, Height = 1240, Source = LauncherCache.Steam };
        data.Games["Steam:1"] = entry;

        index.Save(data);
        var loaded = index.Load();

        var back = loaded.Games["steam:1"]; // Schlüssel ohne Groß-/Kleinschreibung
        Assert.Equal("ABC", back.Signature);
        Assert.Equal(256, back.Icon?.Px);
        Assert.True(back.CoverSmeared);
        Assert.Equal([1, 2, 3, 4], back.LogoTrim!);
        Assert.Equal(3840, back.Located["hero"].Width);
        Assert.Equal(42, back.SteamGridDb?.GameId);
        Assert.Equal(data.SteamGridDbLastRun, loaded.SteamGridDbLastRun);
        var field = back.Ambient!.FieldPixels()!;
        Assert.Equal(8 * 3 * 4, field.Length);
        Assert.Equal((0x11, 0x22, 0x33, 255), (field[2], field[1], field[0], field[3]));
    }

    [Theory]
    [InlineData("{ kaputt")]
    [InlineData("{\"version\": 99, \"games\": {\"Steam:1\": {\"signature\": \"X\"}}}")]
    [InlineData("null")]
    public void Unreadable_or_foreign_index_starts_empty(string content)
    {
        var fs = new InMemoryFileSystem().AddFile(IndexPath, content);

        Assert.Empty(new ArtworkIndex(fs, IndexPath).Load().Games);
    }

    [Fact]
    public void Wrong_field_size_gives_no_pixels()
    {
        Assert.Null(new IndexedAmbient { Field = "112233", FieldWidth = 8, FieldHeight = 3 }.FieldPixels());
        Assert.Null(new IndexedAmbient { Field = null, FieldWidth = 8, FieldHeight = 3 }.FieldPixels());
    }

    [Fact]
    public void Signature_changes_with_files_name_and_choice_but_not_otherwise()
    {
        var stamps = new Dictionary<string, string> { [@"C:\h.jpg"] = "100:1" };
        var located = LocatedArtwork.None with { Hero = new LocatedImage(ArtworkKind.Hero, @"C:\h.jpg", LauncherCache.Steam) };
        var game = new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "Spiel" };
        string Sig(GameConfig g, LocatedArtwork l) => ArtworkIndex.Signature(g, l, p => stamps.GetValueOrDefault(p));

        var first = Sig(game, located);
        Assert.Equal(first, Sig(game, located));

        stamps[@"C:\h.jpg"] = "120:2"; // Steam hat das Bild ersetzt
        var replaced = Sig(game, located);
        Assert.NotEqual(first, replaced);

        var renamed = new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "Spiel (Neu)" };
        Assert.NotEqual(replaced, Sig(renamed, located));

        var chosen = new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "Spiel", Artwork = new GameArtworkChoice { Cover = "gen" } };
        Assert.NotEqual(replaced, Sig(chosen, located));
        Assert.NotEqual(replaced, Sig(game, LocatedArtwork.None));
    }

    [Fact]
    public void Reset_keeps_steamgriddb_state()
    {
        var entry = new ArtworkIndexEntry { Signature = "A", CoverSmeared = true, Logo = "x.png", SteamGridDb = new IndexedSteamGridDb { GameId = 7 } };
        entry.Generated = new Dictionary<string, string> { ["hero"] = "h.jpg" };

        entry.ResetDerived("B");

        Assert.Equal("B", entry.Signature);
        Assert.Null(entry.CoverSmeared);
        Assert.Null(entry.Logo);
        Assert.Null(entry.Generated);
        Assert.Equal(7, entry.SteamGridDb?.GameId);
    }

    [Fact]
    public void File_names_are_stable_readable_and_own_custom_files_are_recognized()
    {
        var custom = ArtworkFiles.CustomFileName("Steam:1091500", ArtworkKind.Hero);

        Assert.Matches(@"^Steam_1091500_[0-9A-F]{8}-hero\.jpg$", custom);
        Assert.Equal(custom, ArtworkFiles.CustomFileName("Steam:1091500", ArtworkKind.Hero));
        Assert.EndsWith("-logo.png", ArtworkFiles.CustomFileName("Steam:1091500", ArtworkKind.Logo));
        Assert.True(ArtworkFiles.IsOwnCustomFile(custom));
        Assert.True(ArtworkOverride.IsValid(ArtworkOverride.Custom(custom)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtworkFiles.CustomFileName("Steam:1", ArtworkKind.Icon));

        // Gegenproben: fremde Namen gelten nicht als eigene Dateien (werden nie gelöscht).
        Assert.False(ArtworkFiles.IsOwnCustomFile("urlaub.jpg"));
        Assert.False(ArtworkFiles.IsOwnCustomFile("Steam_1_ZZZZZZZZ-hero.jpg"));
        Assert.False(ArtworkFiles.IsOwnCustomFile(@"..\Steam_1_0123ABCD-hero.jpg"));
        Assert.False(ArtworkFiles.IsOwnCustomFile(null));

        Assert.Equal(ArtworkFiles.Stem("Steam:1") + "-1920.jpg", ArtworkFiles.BoostFileName("Steam:1", 1920));
        Assert.EndsWith("-cover.webp", ArtworkFiles.SteamGridDbFileName("Gog:1", ArtworkKind.Cover, ".WEBP"));
        Assert.Equal(1920, ArtworkFiles.WidthStep(1700));
        Assert.Equal(2560, ArtworkFiles.WidthStep(1921));
        Assert.Equal(3840, ArtworkFiles.WidthStep(9000));
    }

    [Fact]
    public void Header_sizes_without_decoding()
    {
        var png = new byte[32];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 3840);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 1240);
        Assert.True(ImageHeader.TryGetSize(png, out var w, out var h));
        Assert.Equal((3840, 1240), (w, h));

        // JPEG: APP0, dann SOF0 mit Höhe 900 und Breite 600.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x03, 0x84, 0x02, 0x58, 0x03];
        Assert.True(ImageHeader.TryGetSize(jpeg, out w, out h));
        Assert.Equal((600, 900), (w, h));

        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp);
        "WEBPVP8X"u8.CopyTo(webp.AsSpan(8));
        webp[24] = 0x7F; // Breite − 1 = 1919 (0x077F)
        webp[25] = 0x07;
        webp[27] = 0x6B; // Höhe − 1 = 619 (0x026B)
        webp[28] = 0x02;
        Assert.True(ImageHeader.TryGetSize(webp, out w, out h));
        Assert.Equal((1920, 620), (w, h));

        // ICO: größtes Bild, Breite 0 heißt 256.
        byte[] ico = [0, 0, 1, 0, 2, 0, 32, 32, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.True(ImageHeader.TryGetSize(ico, out w, out h));
        Assert.Equal((256, 256), (w, h));

        // Gegenproben: kein Bild, abgeschnittener Kopf.
        Assert.False(ImageHeader.TryGetSize("<html>"u8, out _, out _));
        Assert.False(ImageHeader.TryGetSize(png.AsSpan(0, 10), out _, out _));
        Assert.False(ImageHeader.TryGetSize(jpeg.AsSpan(0, 8), out _, out _));
    }

    [Fact]
    public void Changed_cache_files_map_to_their_games()
    {
        var roots = new ArtworkRoots(@"C:\Steam", @"C:\ProgramData\GOG.com\Galaxy\webcache", @"C:\Ubi");
        var owners = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["d491.jpg"] = ["4740", "5000"] };

        Assert.Equal(["Steam:553850"], ArtworkWatchMap.GameKeysFor(@"C:\Steam\appcache\librarycache\553850\abc\library_hero.jpg", roots));
        Assert.Equal(["Steam:553850"], ArtworkWatchMap.GameKeysFor(@"c:/steam/appcache/librarycache/553850", roots));
        Assert.Equal(["Gog:1999999901"], ArtworkWatchMap.GameKeysFor(@"C:\ProgramData\GOG.com\Galaxy\webcache\4698\gog\1999999901\x_glx_logo.webp", roots));
        Assert.Equal(["Ubisoft:4740", "Ubisoft:5000"], ArtworkWatchMap.GameKeysFor(@"C:\Ubi\cache\assets\D491.jpg", roots, owners));

        // Gegenproben: Dateien ohne Spiel, fremde Ordner und das Overlay von GOG Galaxy.
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"C:\Steam\appcache\librarycache\assetcache.vdf", roots));
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"C:\Steam\appcache\librarycache\temp\x.jpg", roots));
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"C:\ProgramData\GOG.com\Galaxy\webcache\1999999901-overlay\a.webp", roots));
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"C:\Ubi\cache\assets\unbekannt.jpg", roots, owners));
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"D:\Bilder\library_hero.jpg", roots));
        Assert.Empty(ArtworkWatchMap.GameKeysFor(@"C:\Steam\appcache\librarycache\553850\x.jpg", ArtworkRoots.None));
    }
}
