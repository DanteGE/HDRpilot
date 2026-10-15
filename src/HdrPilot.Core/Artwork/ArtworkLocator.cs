using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners.Ubisoft;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Artwork;

/// <summary>Bildcache, aus dem ein lokales Bild stammt (nur gelesen).</summary>
public enum LauncherCache
{
    /// <summary>Steam <c>appcache\librarycache\&lt;appid&gt;</c>.</summary>
    Steam,

    /// <summary>GOG Galaxy <c>%ProgramData%\GOG.com\Galaxy\webcache\&lt;Benutzer&gt;\gog\&lt;ID&gt;</c>.</summary>
    Gog,

    /// <summary>Ubisoft Connect <c>cache\assets</c>.</summary>
    Ubisoft,

    /// <summary>Bilder im Paket eines Xbox-Spiels (<c>&lt;Spielordner&gt;\Resources</c>).</summary>
    Xbox,
}

/// <summary>Ein gefundenes Launcher-Bild.</summary>
/// <param name="Variant">Steam-Cover: "600x900" oder "capsule" (Kapsel, oft verschmiert); sonst null.</param>
public sealed record LocatedImage(ArtworkKind Kind, string Path, LauncherCache Cache, string? Variant = null)
{
    /// <summary>Dateiname ohne Ordner (Kennzeichen „alt:&lt;Datei&gt;“ der eigenen Bildwahl).</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Lokale Launcher-Bilder eines Spiels (E89, G10.3): je Bildart die bevorzugte Datei, dazu das kleine Steam-Gemeinschaftssymbol
/// (32 px, nur letzter Ersatz), das deckende Kachelbild eines Xbox-Pakets und weitere Bilder, die „Bild wählen…“ anbietet
/// (etwa SplashScreen.png eines Xbox-Pakets oder die nicht deutsche Fassung eines Steam-Bildes).
/// </summary>
public sealed record LocatedArtwork(
    LocatedImage? Hero,
    LocatedImage? Logo,
    LocatedImage? Cover,
    LocatedImage? Header,
    LocatedImage? Field,
    LocatedImage? CommunityIcon,
    LocatedImage? CacheIcon,
    LocatedImage? Tile,
    IReadOnlyList<LocatedImage> Alternatives)
{
    public static LocatedArtwork None { get; } = new(null, null, null, null, null, null, null, null, []);

    /// <summary>Bevorzugte Datei einer Bildart (Titelbild, Logo, Cover, Kopfbild, Farbfeld); sonst null.</summary>
    public LocatedImage? Get(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => Hero,
        ArtworkKind.Logo => Logo,
        ArtworkKind.Cover => Cover,
        ArtworkKind.Header => Header,
        ArtworkKind.Field => Field,
        _ => null,
    };

    /// <summary>Alle gefundenen Dateien (für die Kennung im Zwischenspeicher und die Beobachtung der Ordner).</summary>
    public IEnumerable<LocatedImage> All()
    {
        foreach (var image in new[] { Hero, Logo, Cover, Header, Field, CommunityIcon, CacheIcon, Tile })
        {
            if (image is not null)
            {
                yield return image;
            }
        }

        foreach (var alternative in Alternatives)
        {
            yield return alternative;
        }
    }

    /// <summary>
    /// Datei zum Kennzeichen „alt:&lt;Datei&gt;“ einer Bildart: eine der weiteren Dateien oder die bevorzugte selbst; null, wenn
    /// es sie (nicht mehr) gibt.
    /// </summary>
    public LocatedImage? Alternative(ArtworkKind kind, string fileName) =>
        Alternatives.FirstOrDefault(a => a.Kind == kind && string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        ?? (Get(kind) is { } primary && string.Equals(primary.FileName, fileName, StringComparison.OrdinalIgnoreCase) ? primary : null);
}

/// <summary>
/// Ordner der Launcher-Bildcaches, einmal je Dienst ermittelt. Steam wie der Steam-Scanner (HKCU SteamPath, Rückfall
/// HKLM 32 Bit InstallPath, dann der Standardordner), GOG Galaxy unter ProgramData, Ubisoft Connect aus
/// HKLM\SOFTWARE\Ubisoft\Launcher (32 Bit) InstallDir, sonst der Standardordner unter Programme (x86).
/// </summary>
public sealed record ArtworkRoots(string? SteamRoot, string? GogWebCache, string? UbisoftLauncher)
{
    public static ArtworkRoots None { get; } = new(null, null, null);

    /// <summary><c>&lt;Steam&gt;\appcache\librarycache</c>; null ohne Steam.</summary>
    public string? SteamLibraryCache => SteamRoot is null ? null : Path.Combine(SteamRoot, "appcache", "librarycache");

    /// <summary><c>&lt;Ubisoft Game Launcher&gt;\cache\assets</c>; null ohne Ubisoft Connect.</summary>
    public string? UbisoftAssets => UbisoftLauncher is null ? null : Path.Combine(UbisoftLauncher, "cache", "assets");

    /// <summary>Produkt-Cache von Ubisoft Connect (Zuordnung Spiel → Bilddateien).</summary>
    public string? UbisoftConfigurations => UbisoftLauncher is null ? null : Path.Combine(UbisoftLauncher, "cache", "configuration", "configurations");

    /// <summary>Ermittelt die Ordner; nur vorhandene Ordner zählen. Wirft nicht.</summary>
    public static ArtworkRoots Resolve(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        string? Existing(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                var path = PathUtil.Normalize(raw);
                return Path.IsPathFullyQualified(path) && fileSystem.DirectoryExists(path) ? path : null;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        string? Read(RegistryHiveKind hive, string key, string value, RegistryViewKind view = RegistryViewKind.Default)
        {
            try
            {
                using var k = registry.OpenKey(hive, key, view);
                return k?.GetString(value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogDebug("Bilder: Registry-Schlüssel {Key} nicht lesbar: {Message}", key, ex.Message);
                return null;
            }
        }

        var programFilesX86 = environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programData = environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var steam = Existing(Read(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath"))
            ?? Existing(Read(RegistryHiveKind.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryViewKind.Registry32))
            ?? (string.IsNullOrWhiteSpace(programFilesX86) ? null : Existing(Path.Combine(programFilesX86, "Steam")));
        var gog = string.IsNullOrWhiteSpace(programData) ? null : Existing(Path.Combine(programData, "GOG.com", "Galaxy", "webcache"));
        var ubisoft = Existing(Read(RegistryHiveKind.LocalMachine, @"SOFTWARE\Ubisoft\Launcher", "InstallDir", RegistryViewKind.Registry32))
            ?? (string.IsNullOrWhiteSpace(programFilesX86) ? null : Existing(Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher")));
        return new ArtworkRoots(steam, gog, ubisoft);
    }
}

/// <summary>
/// Findet die lokalen Launcher-Bilder eines Spiels (E89, G10.3), nur lesend, mit den am Zielrechner gemessenen Dateinamen
/// (Referenz: <c>docs/design/showcase-hero/prototyp/make-data.ps1</c>):
/// <list type="bullet">
/// <item>Steam: <c>appcache\librarycache\&lt;appid&gt;\</c> samt Unterordnern (Dateiname → erster Pfad), deutsche Fassungen
/// zuerst: Titelbild <c>library_hero</c>, Logo <c>logo</c>, Cover <c>library_600x900</c> vor der Kapsel
/// <c>library_capsule</c>, Kopfbild <c>header</c> vor <c>library_header</c>, Farbfeld <c>library_hero_blur</c>; das
/// Gemeinschaftssymbol <c>&lt;40 Hex&gt;.jpg</c> (32 px) nur als letzter Symbol-Ersatz.</item>
/// <item>GOG Galaxy: <c>webcache\&lt;Benutzer&gt;\gog\&lt;ID&gt;\</c>: Titelbild <c>*_glx_bg_top_padding_7.webp</c> (sonst
/// <c>*_glx_background*.webp</c>), Cover <c>*_glx_vertical_cover.webp</c>, Logo <c>*_glx_logo*.webp</c>, Symbol
/// <c>*_glx_square_icon_v2.webp</c>.</item>
/// <item>Ubisoft Connect: Produkt-Cache (Protobuf mit YAML) nennt <c>background_image</c> (Titelbild), <c>logo_image</c>,
/// <c>thumb_image</c> (Cover) und <c>splash_image</c> (Kopfbild), auch über <c>localizations.default</c>; die Dateien liegen
/// in <c>cache\assets</c>.</item>
/// <item>Xbox: <c>&lt;Spielordner&gt;\Resources\Square480x480Logo*.png</c> ist das deckende Kachelbild;
/// <c>SplashScreen*.png</c> nur als Wahlmöglichkeit (oft Engine-Logo oder Titelkarte).</item>
/// </list>
/// Epic, EA, Battle.net und manuelle Spiele haben keinen Bildcache (Symbol aus der EXE, Rest erzeugt).
/// </summary>
public sealed class ArtworkLocator
{
    /// <summary>Steam-Dateinamen je Bildart in Vorzugsreihenfolge (deutsche Fassungen zuerst).</summary>
    public static IReadOnlyDictionary<ArtworkKind, string[]> SteamNames { get; } = new Dictionary<ArtworkKind, string[]>
    {
        [ArtworkKind.Hero] = ["library_hero_german.jpg", "library_hero.jpg"],
        [ArtworkKind.Logo] = ["logo_german.png", "logo.png"],
        [ArtworkKind.Cover] = ["library_600x900_german.jpg", "library_600x900.jpg", "library_capsule_german.jpg", "library_capsule.jpg"],
        [ArtworkKind.Header] = ["header_german.jpg", "header.jpg", "library_header_german.jpg", "library_header.jpg"],
        [ArtworkKind.Field] = ["library_hero_blur_german.jpg", "library_hero_blur.jpg"],
    };

    /// <summary>Höchstens so viele Dateien je Steam-Ordner (Schutz vor fremden Riesenordnern).</summary>
    public const int MaxSteamFiles = 256;

    private readonly IFileSystem _fileSystem;
    private readonly ArtworkRoots _roots;
    private readonly ILogger _logger;
    private readonly Lazy<IReadOnlyList<string>> _gogUsers;
    private readonly object _ubisoftGate = new();
    private Dictionary<string, IReadOnlyDictionary<string, string>>? _ubisoft;

    public ArtworkLocator(IFileSystem fileSystem, ArtworkRoots roots, ILogger? logger = null)
    {
        _fileSystem = fileSystem;
        _roots = roots;
        _logger = logger ?? NullLogger.Instance;
        _gogUsers = new Lazy<IReadOnlyList<string>>(GogUserFolders);
    }

    public ArtworkRoots Roots => _roots;

    /// <summary>Gefundene Bilder eines Spiels; ohne Bildcache <see cref="LocatedArtwork.None"/>. Wirft nicht.</summary>
    public LocatedArtwork Locate(GameConfig game)
    {
        try
        {
            return game.Source switch
            {
                GameSource.Steam => Steam(game.SourceId),
                GameSource.Gog => Gog(game.SourceId),
                GameSource.Ubisoft => Ubisoft(game.SourceId),
                GameSource.Xbox => Xbox(game.InstallDir),
                _ => LocatedArtwork.None,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            _logger.LogDebug("Bilder von {Game} nicht lesbar: {Message}", game.Key, ex.Message);
            return LocatedArtwork.None;
        }
    }

    /// <summary>Vergisst den gelesenen Ubisoft-Produkt-Cache (nach einer Änderung im Cache-Ordner).</summary>
    public void Forget()
    {
        lock (_ubisoftGate)
        {
            _ubisoft = null;
        }
    }

    /// <summary>Ubisoft-Bilddateien (Dateiname, ohne Ordner) → Produkt-IDs, für die Beobachtung von <c>cache\assets</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> UbisoftAssetOwners()
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, files) in UbisoftConfigurations())
        {
            foreach (var file in files.Values)
            {
                if (!owners.TryGetValue(file, out var list))
                {
                    owners[file] = list = [];
                }

                list.Add(id);
            }
        }

        return owners.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    private LocatedArtwork Steam(string appId)
    {
        if (_roots.SteamLibraryCache is not { } cache || appId.Length == 0 || !appId.All(char.IsAsciiDigit))
        {
            return LocatedArtwork.None;
        }

        var dir = Path.Combine(cache, appId);
        if (!_fileSystem.DirectoryExists(dir))
        {
            return LocatedArtwork.None;
        }

        // Steam legt die Bilder in Unterordnern mit Prüfsummen-Namen ab: Dateiname → erster Pfad (wie make-data.ps1).
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? community = null;
        foreach (var file in _fileSystem.EnumerateFiles(dir, "*", recursive: true).Take(MaxSteamFiles))
        {
            var name = Path.GetFileName(file);
            files.TryAdd(name, file);
            if (community is null && IsCommunityIcon(name) && PathUtil.PathEquals(Path.GetDirectoryName(file) ?? string.Empty, dir))
            {
                community = file;
            }
        }

        var alternatives = new List<LocatedImage>();
        LocatedImage? Pick(ArtworkKind kind)
        {
            LocatedImage? chosen = null;
            foreach (var name in SteamNames[kind])
            {
                if (!files.TryGetValue(name, out var path))
                {
                    continue;
                }

                var variant = kind == ArtworkKind.Cover ? (name.StartsWith("library_600x900", StringComparison.OrdinalIgnoreCase) ? "600x900" : "capsule") : null;
                var image = new LocatedImage(kind, path, LauncherCache.Steam, variant);
                if (chosen is null)
                {
                    chosen = image;
                }
                else if (kind is not ArtworkKind.Field and not ArtworkKind.Header)
                {
                    alternatives.Add(image);
                }
            }

            return chosen;
        }

        var hero = Pick(ArtworkKind.Hero);
        var logo = Pick(ArtworkKind.Logo);
        var cover = Pick(ArtworkKind.Cover);
        var header = Pick(ArtworkKind.Header);
        var field = Pick(ArtworkKind.Field);
        var icon = community is null ? null : new LocatedImage(ArtworkKind.Icon, community, LauncherCache.Steam);
        return new LocatedArtwork(hero, logo, cover, header, field, icon, null, null, alternatives);
    }

    /// <summary>Gemeinschaftssymbol von Steam: 40 Hex-Zeichen und „.jpg“ direkt im appid-Ordner.</summary>
    public static bool IsCommunityIcon(string fileName) =>
        fileName.Length == 44
        && fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        && !fileName.AsSpan(0, 40).ContainsAnyExcept("0123456789abcdefABCDEF");

    private LocatedArtwork Gog(string id)
    {
        if (id.Length == 0 || !id.All(char.IsAsciiDigit))
        {
            return LocatedArtwork.None;
        }

        foreach (var user in _gogUsers.Value)
        {
            var dir = Path.Combine(user, id);
            if (!_fileSystem.DirectoryExists(dir))
            {
                continue;
            }

            LocatedImage? hero = null, background = null, cover = null, logo = null, icon = null;
            foreach (var file in _fileSystem.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith("_glx_bg_top_padding_7.webp", StringComparison.OrdinalIgnoreCase))
                {
                    hero ??= new LocatedImage(ArtworkKind.Hero, file, LauncherCache.Gog);
                }
                else if (name.Contains("_glx_background", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    background ??= new LocatedImage(ArtworkKind.Hero, file, LauncherCache.Gog);
                }
                else if (name.EndsWith("_glx_vertical_cover.webp", StringComparison.OrdinalIgnoreCase))
                {
                    cover ??= new LocatedImage(ArtworkKind.Cover, file, LauncherCache.Gog);
                }
                else if (name.Contains("_glx_logo", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    logo ??= new LocatedImage(ArtworkKind.Logo, file, LauncherCache.Gog);
                }
                else if (name.EndsWith("_glx_square_icon_v2.webp", StringComparison.OrdinalIgnoreCase))
                {
                    icon ??= new LocatedImage(ArtworkKind.Icon, file, LauncherCache.Gog);
                }
            }

            var alternatives = hero is not null && background is not null ? new List<LocatedImage> { background } : [];
            return new LocatedArtwork(hero ?? background, logo, cover, null, null, null, icon, null, alternatives);
        }

        return LocatedArtwork.None;
    }

    /// <summary>Ordner <c>webcache\&lt;Benutzer&gt;\gog</c> (Ordner ohne Unterordner gog, etwa „…-overlay“, fallen weg).</summary>
    private List<string> GogUserFolders()
    {
        if (_roots.GogWebCache is not { } webcache)
        {
            return [];
        }

        try
        {
            return _fileSystem.EnumerateDirectories(webcache)
                .Select(user => Path.Combine(user, "gog"))
                .Where(_fileSystem.DirectoryExists)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            _logger.LogDebug("Bilder: GOG-Galaxy-Cache {Dir} nicht lesbar: {Message}", webcache, ex.Message);
            return [];
        }
    }

    private LocatedArtwork Ubisoft(string id)
    {
        if (_roots.UbisoftAssets is not { } assets || !UbisoftConfigurations().TryGetValue(id, out var files))
        {
            return LocatedArtwork.None;
        }

        LocatedImage? Find(string key, ArtworkKind kind)
        {
            if (!files.TryGetValue(key, out var name))
            {
                return null;
            }

            var path = Path.Combine(assets, name);
            return _fileSystem.FileExists(path) ? new LocatedImage(kind, path, LauncherCache.Ubisoft) : null;
        }

        return new LocatedArtwork(
            Find("background_image", ArtworkKind.Hero),
            Find("logo_image", ArtworkKind.Logo),
            Find("thumb_image", ArtworkKind.Cover),
            Find("splash_image", ArtworkKind.Header),
            null,
            null,
            null,
            null,
            []);
    }

    /// <summary>
    /// Bilddateien je Produkt-ID aus dem Produkt-Cache (einmal gelesen): Schlüssel background_image, logo_image, thumb_image,
    /// splash_image; Werte ohne Bild-Endung sind Schlüssel in <c>localizations.default</c>.
    /// </summary>
    private Dictionary<string, IReadOnlyDictionary<string, string>> UbisoftConfigurations()
    {
        lock (_ubisoftGate)
        {
            if (_ubisoft is not null)
            {
                return _ubisoft;
            }

            var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (_roots.UbisoftConfigurations is { } path && _fileSystem.FileExists(path))
            {
                try
                {
                    var products = UbisoftConfigurationCache.Read(_fileSystem.ReadAllBytes(path), new AllIds(), out var error);
                    foreach (var (id, yaml) in products)
                    {
                        if (UbisoftImages(yaml) is { Count: > 0 } images)
                        {
                            result[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = images;
                        }
                    }

                    if (error is not null)
                    {
                        _logger.LogDebug("Bilder: Ubisoft-Produkt-Cache teilweise gelesen: {Error}", error);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
                {
                    _logger.LogDebug("Bilder: Ubisoft-Produkt-Cache nicht lesbar: {Message}", ex.Message);
                }
            }

            return _ubisoft = result;
        }
    }

    /// <summary>Bildschlüssel aus dem YAML eines Produkts; nur schlichte Dateinamen mit Bild-Endung.</summary>
    internal static IReadOnlyDictionary<string, string> UbisoftImages(string yaml)
    {
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        YamlNode document;
        try
        {
            document = UbisoftYaml.Parse(yaml);
        }
        catch (FormatException)
        {
            return images;
        }

        var root = document["root"];
        var defaults = document["localizations"]?["default"];
        foreach (var key in (string[])["background_image", "logo_image", "thumb_image", "splash_image"])
        {
            var value = root?[key]?.Text?.Trim().Trim('\'', '"');
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!IsImageFile(value) && defaults?[value]?.Text?.Trim().Trim('\'', '"') is { Length: > 0 } localized)
            {
                value = localized;
            }

            if (IsImageFile(value) && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                images[key] = value;
            }
        }

        return images;
    }

    private static bool IsImageFile(string name) =>
        name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private LocatedArtwork Xbox(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || !IconFiles.IsLocalPath(installDir))
        {
            return LocatedArtwork.None;
        }

        var resources = Path.Combine(PathUtil.Normalize(installDir), "Resources");
        if (!_fileSystem.DirectoryExists(resources))
        {
            return LocatedArtwork.None;
        }

        var files = _fileSystem.EnumerateFiles(resources, "*.png").ToList();
        string? Best(string stem) =>
            files.Where(f => IsVariantOf(Path.GetFileName(f), stem))
                .OrderBy(f => ScaleRank(Path.GetFileName(f), stem))
                .FirstOrDefault();

        var tile = Best("Square480x480Logo") is { } t ? new LocatedImage(ArtworkKind.Icon, t, LauncherCache.Xbox) : null;
        var splash = Best("SplashScreen") is { } s ? new LocatedImage(ArtworkKind.Hero, s, LauncherCache.Xbox) : null;
        return new LocatedArtwork(null, null, null, null, null, null, null, tile, splash is null ? [] : [splash]);
    }

    /// <summary>„Name.png“ oder „Name.scale-200.png“ (und weitere Qualifizierer des Pakets).</summary>
    private static bool IsVariantOf(string fileName, string stem) =>
        fileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
        && (fileName.Length == stem.Length + 4 || fileName[stem.Length] == '.')
        && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ohne Qualifizierer zuerst, dann die größte Skalierung.</summary>
    private static int ScaleRank(string fileName, string stem)
    {
        if (fileName.Length == stem.Length + 4)
        {
            return 0;
        }

        var marker = fileName.IndexOf(".scale-", StringComparison.OrdinalIgnoreCase);
        if (marker > 0 && int.TryParse(fileName.AsSpan(marker + 7, fileName.Length - marker - 11), out var scale))
        {
            return 1000 - Math.Clamp(scale, 0, 999);
        }

        return 2000;
    }

    /// <summary>Für <see cref="UbisoftConfigurationCache.Read"/>: alle Produkt-IDs.</summary>
    private sealed class AllIds : IReadOnlySet<uint>
    {
        public int Count => int.MaxValue;

        public bool Contains(uint item) => true;

        public bool IsProperSubsetOf(IEnumerable<uint> other) => false;

        public bool IsProperSupersetOf(IEnumerable<uint> other) => true;

        public bool IsSubsetOf(IEnumerable<uint> other) => false;

        public bool IsSupersetOf(IEnumerable<uint> other) => true;

        public bool Overlaps(IEnumerable<uint> other) => other.Any();

        public bool SetEquals(IEnumerable<uint> other) => false;

        public IEnumerator<uint> GetEnumerator() => Enumerable.Empty<uint>().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
