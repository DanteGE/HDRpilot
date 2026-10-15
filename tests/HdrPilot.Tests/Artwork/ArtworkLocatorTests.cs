using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.Scanners;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Lokale Launcher-Bilder (E89, G10.3) mit den am Zielrechner gemessenen Dateinamen; nur lesend, mit Attrappen statt echter
/// Launcher-Ordner.
/// </summary>
public sealed class ArtworkLocatorTests
{
    private const string Steam = @"C:\Program Files (x86)\Steam";
    private const string Cache = Steam + @"\appcache\librarycache";
    private const string WebCache = @"C:\ProgramData\GOG.com\Galaxy\webcache";
    private const string Ubisoft = @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher";

    private static readonly ArtworkRoots Roots = new(Steam, WebCache, Ubisoft);

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public void Steam_prefers_german_variants_in_hashed_subfolders()
    {
        _fs.AddFile(Cache + @"\9990110\aa\library_hero.jpg", "x")
            .AddFile(Cache + @"\9990110\bb\library_hero_german.jpg", "x")
            .AddFile(Cache + @"\9990110\cc\logo.png", "x")
            .AddFile(Cache + @"\9990110\dd\logo_german.png", "x")
            .AddFile(Cache + @"\9990110\ee\library_hero_blur_german.jpg", "x")
            .AddFile(Cache + @"\9990110\ff\library_header_german.jpg", "x");

        var located = Locator().Locate(Game(GameSource.Steam, "9990110"));

        Assert.Equal(Cache + @"\9990110\bb\library_hero_german.jpg", located.Hero?.Path);
        Assert.Equal(Cache + @"\9990110\dd\logo_german.png", located.Logo?.Path);
        Assert.Equal("library_hero_blur_german.jpg", located.Field?.FileName);
        Assert.Equal("library_header_german.jpg", located.Header?.FileName);
        Assert.All(located.All(), image => Assert.Equal(LauncherCache.Steam, image.Cache));

        // Die nicht deutschen Fassungen stehen unter „Bild wählen…“ zur Wahl, nicht als bevorzugte Datei.
        Assert.Contains(located.Alternatives, a => a.Kind == ArtworkKind.Hero && a.FileName == "library_hero.jpg");
        Assert.Contains(located.Alternatives, a => a.Kind == ArtworkKind.Logo && a.FileName == "logo.png");
        Assert.Equal("library_hero.jpg", located.Alternative(ArtworkKind.Hero, "library_hero.jpg")?.FileName);
        Assert.Equal(located.Hero, located.Alternative(ArtworkKind.Hero, "library_hero_german.jpg"));
        Assert.Null(located.Alternative(ArtworkKind.Hero, "unbekannt.jpg"));
    }

    [Fact]
    public void Steam_cover_600x900_wins_over_the_capsule_and_keeps_the_variant()
    {
        _fs.AddFile(Cache + @"\10\a\library_capsule.jpg", "x").AddFile(Cache + @"\10\b\library_600x900.jpg", "x");
        _fs.AddFile(Cache + @"\20\a\library_capsule_german.jpg", "x");

        var tall = Locator().Locate(Game(GameSource.Steam, "10"));
        var capsule = Locator().Locate(Game(GameSource.Steam, "20"));

        Assert.Equal("600x900", tall.Cover?.Variant);
        Assert.Contains(tall.Alternatives, a => a.Kind == ArtworkKind.Cover && a.Variant == "capsule");
        Assert.Equal("capsule", capsule.Cover?.Variant);
        Assert.Equal("library_capsule_german.jpg", capsule.Cover?.FileName);
    }

    [Fact]
    public void Steam_community_icon_only_directly_in_the_appid_folder()
    {
        const string hash = "9b1d3f5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b7d";
        _fs.AddFile(Cache + @"\30\" + hash + ".jpg", "x");
        _fs.AddFile(Cache + @"\40\sub\" + hash + ".jpg", "x");

        Assert.Equal(hash + ".jpg", Locator().Locate(Game(GameSource.Steam, "30")).CommunityIcon?.FileName);
        Assert.Null(Locator().Locate(Game(GameSource.Steam, "40")).CommunityIcon);
        Assert.True(ArtworkLocator.IsCommunityIcon(hash + ".jpg"));
        Assert.False(ArtworkLocator.IsCommunityIcon("logo.jpg"));
        Assert.False(ArtworkLocator.IsCommunityIcon(hash.Replace('d', 'x') + ".jpg"));
    }

    [Theory]
    [InlineData("9990110")]
    [InlineData("..\\9990110")]
    [InlineData("abc")]
    [InlineData("")]
    public void Steam_without_folder_or_with_odd_ids_finds_nothing(string id)
    {
        _fs.AddFile(Cache + @"\9990111\a\library_hero.jpg", "x");

        Assert.Same(LocatedArtwork.None, Locator().Locate(Game(GameSource.Steam, id)));
    }

    [Fact]
    public void Gog_uses_the_galaxy_webcache_names_and_offers_the_background()
    {
        var dir = WebCache + @"\12345678901234567\gog\1999999901\";
        _fs.AddDirectory(WebCache + @"\1999999901-overlay");
        _fs.AddFile(dir + "e94e_glx_bg_top_padding_7.webp", "x")
            .AddFile(dir + "aa11_glx_background_1920.webp", "x")
            .AddFile(dir + "69e5_glx_vertical_cover.webp", "x")
            .AddFile(dir + "9919_glx_square_icon_v2.webp", "x")
            .AddFile(dir + "77aa_glx_logo_2x.webp", "x");

        var located = Locator().Locate(Game(GameSource.Gog, "1999999901"));

        Assert.Equal("e94e_glx_bg_top_padding_7.webp", located.Hero?.FileName);
        Assert.Equal("69e5_glx_vertical_cover.webp", located.Cover?.FileName);
        Assert.Equal("77aa_glx_logo_2x.webp", located.Logo?.FileName);
        Assert.Equal("9919_glx_square_icon_v2.webp", located.CacheIcon?.FileName);
        Assert.Equal(LauncherCache.Gog, located.Hero?.Cache);
        Assert.Equal("aa11_glx_background_1920.webp", Assert.Single(located.Alternatives).FileName);

        // Gegenprobe: ohne padding_7 gilt der Hintergrund als Titelbild.
        var fs = new InMemoryFileSystem().AddFile(dir + "aa11_glx_background_1920.webp", "x");
        Assert.Equal("aa11_glx_background_1920.webp", new ArtworkLocator(fs, Roots).Locate(Game(GameSource.Gog, "1999999901")).Hero?.FileName);
    }

    [Fact]
    public void Ubisoft_maps_product_cache_images_including_localized_keys()
    {
        var yaml = "version: 2.0\nroot:\n  name: l1\n  background_image: BACKGROUND\n  logo_image: 1a2b.png\n  thumb_image: 3c4d.jpg\n  splash_image: 5e6f.jpg\n"
            + "localizations:\n  default:\n    l1: Avatar\n    BACKGROUND: d491.jpg\n";
        _fs.AddFile(Ubisoft + @"\cache\configuration\configurations", new UbisoftCacheBuilder().Add(4740, yaml).AddFixture(420).Build());
        _fs.AddFile(Ubisoft + @"\cache\assets\d491.jpg", "x").AddFile(Ubisoft + @"\cache\assets\1a2b.png", "x").AddFile(Ubisoft + @"\cache\assets\3c4d.jpg", "x");

        var locator = Locator();
        var located = locator.Locate(Game(GameSource.Ubisoft, "4740"));

        Assert.Equal("d491.jpg", located.Hero?.FileName);
        Assert.Equal("1a2b.png", located.Logo?.FileName);
        Assert.Equal("3c4d.jpg", located.Cover?.FileName);
        Assert.Null(located.Header); // splash_image nennt eine Datei, die im Cache fehlt
        Assert.Equal(["4740"], locator.UbisoftAssetOwners()["d491.jpg"]);
        Assert.Same(LocatedArtwork.None, locator.Locate(Game(GameSource.Ubisoft, "9999")));
    }

    [Fact]
    public void Ubisoft_ignores_values_that_are_not_plain_image_names()
    {
        var images = ArtworkLocator.UbisoftImages("root:\n  background_image: ..\\..\\secret.jpg\n  logo_image: logo.exe\n  thumb_image: ok.jpg\n");

        Assert.Equal(["thumb_image"], images.Keys);
    }

    [Fact]
    public void Xbox_tile_and_splash_alternative_prefer_unqualified_then_largest_scale()
    {
        const string resources = @"D:\XboxGames\Hellraiser\Content\Resources";
        _fs.AddFile(resources + @"\Square480x480Logo.scale-100.png", "x")
            .AddFile(resources + @"\Square480x480Logo.scale-200.png", "x")
            .AddFile(resources + @"\SplashScreen.png", "x")
            .AddFile(resources + @"\SplashScreenX.png", "x");

        var located = Locator().Locate(new GameConfig { Source = GameSource.Xbox, SourceId = "Pkg_1", Name = "Hellraiser", InstallDir = @"D:\XboxGames\Hellraiser\Content" });

        Assert.Equal("Square480x480Logo.scale-200.png", located.Tile?.FileName);
        var splash = Assert.Single(located.Alternatives);
        Assert.Equal(ArtworkKind.Hero, splash.Kind);
        Assert.Equal("SplashScreen.png", splash.FileName);
        Assert.Null(located.Hero); // Splash ist nie automatisch das Titelbild
    }

    [Theory]
    [InlineData(GameSource.Epic)]
    [InlineData(GameSource.EA)]
    [InlineData(GameSource.BattleNet)]
    [InlineData(GameSource.Manual)]
    public void Launchers_without_image_cache_find_nothing(GameSource source)
    {
        _fs.AddFile(Cache + @"\1\a\library_hero.jpg", "x");

        Assert.Same(LocatedArtwork.None, Locator().Locate(Game(source, "1")));
    }

    [Fact]
    public void Unreadable_cache_folder_is_treated_as_empty()
    {
        var failing = new ThrowingFileSystem(_fs.AddFile(Cache + @"\5\a\library_hero.jpg", "x"));

        Assert.Same(LocatedArtwork.None, new ArtworkLocator(failing, Roots).Locate(Game(GameSource.Steam, "5")));
    }

    [Fact]
    public void Roots_follow_registry_then_default_folders()
    {
        var fs = new InMemoryFileSystem()
            .AddDirectory(@"D:\Steam")
            .AddDirectory(@"C:\Program Files (x86)\Steam")
            .AddDirectory(WebCache)
            .AddDirectory(@"E:\Ubi\Launcher");
        var registry = new FakeRegistry()
            .Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "d:/steam")
            .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Ubisoft\Launcher", "InstallDir", "E:/Ubi/Launcher/", RegistryViewKind.Registry32);

        var roots = ArtworkRoots.Resolve(fs, registry, new FakeEnvironment());

        Assert.Equal(@"D:\Steam", roots.SteamRoot, ignoreCase: true);
        Assert.Equal(WebCache, roots.GogWebCache);
        Assert.Equal(@"E:\Ubi\Launcher", roots.UbisoftLauncher);
        Assert.Equal(@"D:\Steam\appcache\librarycache", roots.SteamLibraryCache, ignoreCase: true);

        // Gegenprobe: Registry zeigt auf einen fehlenden Ordner → Standardordner; ohne Ubisoft-Ordner → null.
        var fallback = ArtworkRoots.Resolve(
            new InMemoryFileSystem().AddDirectory(@"C:\Program Files (x86)\Steam"),
            new FakeRegistry().Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "x:/gone"),
            new FakeEnvironment());
        Assert.Equal(@"C:\Program Files (x86)\Steam", fallback.SteamRoot);
        Assert.Null(fallback.UbisoftLauncher);
        Assert.Null(fallback.GogWebCache);
    }

    private ArtworkLocator Locator() => new(_fs, Roots);

    private static GameConfig Game(GameSource source, string id) => new() { Source = source, SourceId = id, Name = "Spiel " + id };

    /// <summary>Dateisystem, dessen Aufzählung wie ein gesperrter Ordner wirft.</summary>
    private sealed class ThrowingFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            throw new UnauthorizedAccessException("gesperrt");

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            throw new UnauthorizedAccessException("gesperrt");

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }
}
