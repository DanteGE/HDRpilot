using System.Globalization;
using System.IO;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Ermitteln je Spiel: was es lokal gibt, was gilt und was abgeleitet ist (index.json).</summary>
public sealed partial class ArtworkService
{
    /// <summary>Ab dieser Größe gilt ein deckendes Bild als Kachelbild (Bildfläche statt Symbol).</summary>
    private const int TileMinimum = 256;

    /// <summary>Ohne Titelbild wird die EXE nach einem größeren Symbol gefragt, wenn das beste kleiner ist.</summary>
    private const int ExeIconWanted = 128;

    /// <summary>
    /// Ein Cover aus dem Cache gilt unter diesem Verhältnis von Höhe zu Breite als (fast) quadratisch: Quiver legt die Kartenbilder
    /// von SteamGridDB meist quadratisch ab (480 × 480), ein Hochformat-Cover hat 1,5 (600 × 900).
    /// </summary>
    private const double SquareCoverRatio = 1.2;

    private readonly Lazy<IconReader> _iconReader = new(() => new IconReader());

    /// <summary>Wie eine Bildart entsteht.</summary>
    private enum Generator
    {
        /// <summary>Datei dekodieren (Launcher, eigene Datei, anderes lokales Bild, SteamGridDB, Symbol).</summary>
        None,
        Hero,
        CoverFromHero,
        CoverFromField,

        /// <summary>Cover im Hochformat aus einem (fast) quadratischen Bild des Launchers (Quiver): das Bild ganz, oben.</summary>
        CoverFromSquare,
        Header,
        Monogram,

        /// <summary>Farbfeld aus den gemerkten Pixeln.</summary>
        Field,
    }

    /// <summary>Was eine Bildart eines Spiels zeigt.</summary>
    private sealed record ArtSpec(ArtworkKind Kind, string? File, ArtworkOrigin Origin, string Label, Generator Generate);

    /// <summary>Ermittelter Stand eines Spiels (im Speicher, solange ein Fenster offen ist).</summary>
    private sealed class GameState
    {
        public required string Key { get; init; }

        public required string ConfigStamp { get; init; }

        public required string Signature { get; init; }

        public required GameConfig Game { get; init; }

        public required LocatedArtwork Located { get; init; }

        public required IndexedIcon Icon { get; init; }

        public required bool CoverSmeared { get; init; }

        /// <summary>Cover-Dateien des Launchers, die (fast) quadratisch sind (Quiver); als Cover gilt dann <see cref="Generator.CoverFromSquare"/>.</summary>
        public required IReadOnlySet<string> SquareCovers { get; init; }

        /// <summary>Das automatische Cover, wenn es (fast) quadratisch ist: Es steht auch im erzeugten Titelbild und gibt die Farben.</summary>
        public string? SquareArt => Located.Cover is { } cover && SquareCovers.Contains(cover.Path) ? cover.Path : null;

        public required ArtSpec Hero { get; init; }

        public required ArtSpec Logo { get; init; }

        public required ArtSpec Cover { get; init; }

        public required ArtSpec Header { get; init; }

        public required ArtSpec Field { get; init; }

        public required ArtSpec IconSpec { get; init; }

        /// <summary>Umgebungsfarben samt Farbfeld (nur beim vollständigen Ermitteln).</summary>
        public IndexedAmbient? Ambient { get; init; }

        public required ArtworkSummary Summary { get; init; }

        /// <summary>Vorschau einer anderen Wahl: nichts davon kommt in den Speicher oder in art\gen.</summary>
        public bool IsHypothetical { get; init; }

        public bool Full => Ambient is not null;

        public string Stem => ArtworkFiles.Stem(Key);

        public ArtSpec Spec(ArtworkKind kind) => kind switch
        {
            ArtworkKind.Hero => Hero,
            ArtworkKind.Logo => Logo,
            ArtworkKind.Cover => Cover,
            ArtworkKind.Header => Header,
            ArtworkKind.Field => Field,
            _ => IconSpec,
        };
    }

    /// <summary>
    /// Ermittelt ein Spiel (Platte, nur lesend in den Launcher-Caches): gefundene Bilder, Kennung, Symbol, Prüfung der
    /// Steam-Kapsel und die geltende Quelle je Bildart; mit <paramref name="full"/> auch Umgebungsfarben und Farbfeld.
    /// Ergebnisse landen in index.json und gelten, bis sich die Kennung ändert.
    /// </summary>
    private GameState Resolve(GameConfig game, bool full)
    {
        var stamp = ConfigStamp(game);
        if (_states.TryGetValue(game.Key, out var known) && known.ConfigStamp == stamp && (known.Full || !full))
        {
            return known;
        }

        lock (_gameLocks.GetOrAdd(game.Key, _ => new object()))
        {
            if (_states.TryGetValue(game.Key, out known) && known.ConfigStamp == stamp && (known.Full || !full))
            {
                return known;
            }

            var located = _locator.Value.Locate(game);
            var custom = CustomFiles(game);
            var customIcon = CustomIconPath(game);
            var sgdb = SteamGridDbFiles(game.Key);
            var extra = custom.Values.Concat(sgdb.Values).Append(customIcon).OfType<string>().ToList();
            var signature = ArtworkIndex.Signature(game, located, _stamp, extra, _options.HelperPatterns());
            ResetIfStale(game.Key, signature);
            RecordLocated(game.Key, located);

            var hero = HeroSpec(game, located, custom, sgdb);
            var icon = Entry(game.Key, e => e.Icon) ?? StoreIcon(game.Key, ChooseIcon(game, located, hero.File is not null, customIcon));
            var smeared = located.Cover is { Cache: LauncherCache.Steam } cover
                && (Entry(game.Key, e => e.CoverSmeared) ?? StoreSmear(game.Key, cover.Path));
            var squares = SquareCovers(located);
            var squareArt = located.Cover is { } automatic && squares.Contains(automatic.Path) ? automatic.Path : null;
            var ambient = full ? Entry(game.Key, e => e.Ambient) ?? StoreAmbient(game.Key, ComputeAmbient(game, hero, squareArt, icon)) : Entry(game.Key, e => e.Ambient);
            var state = Build(game, stamp, signature, located, custom, sgdb, icon, smeared, squares, ambient);
            _states[game.Key] = state;
            return state;
        }
    }

    /// <summary>
    /// Cover-Dateien aus dem Cache von Quiver (automatisch und zur Wahl), die (fast) quadratisch sind
    /// (<see cref="SquareCoverRatio"/>; gemessen am Kopf der Datei). Als Cover zugeschnitten fehlte ihnen links und rechts ein
    /// Drittel, etwa vom Schriftzug „BLACK“; sie werden deshalb ganz in ein erzeugtes Cover im Hochformat gesetzt.
    /// </summary>
    private HashSet<string> SquareCovers(LocatedArtwork located)
    {
        var squares = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var covers = located.Cover is { } automatic ? located.Alternatives.Prepend(automatic) : located.Alternatives;
        foreach (var image in covers.Where(i => i is { Kind: ArtworkKind.Cover, Cache: LauncherCache.Quiver }))
        {
            try
            {
                var (width, height) = ArtworkImaging.Measure(image.Path);
                if (width > 0 && height > 0 && height < width * SquareCoverRatio)
                {
                    squares.Add(image.Path);
                }
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                _logger.LogDebug("Bilder: Cover {Path} nicht lesbar: {Error}", image.Path, ex.GetType().Name);
            }
        }

        return squares;
    }

    /// <summary>
    /// Stand eines Spiels mit einer anderen eigenen Wahl (Vorschau in „Bild wählen…“): dieselben gefundenen Bilder, dasselbe
    /// Symbol und dieselbe Kapsel-Prüfung, aber ohne Verzeichnis und ohne Speicher; die Farben werden bei einem anderen
    /// Titelbild neu gelesen (nicht gemerkt).
    /// </summary>
    private GameState Hypothetical(GameState real, GameArtworkChoice? choice)
    {
        var game = Snapshot(real.Game);
        game.Artwork = choice;
        var custom = CustomFiles(game);
        var sgdb = SteamGridDbFiles(game.Key);
        var hero = HeroSpec(game, real.Located, custom, sgdb);
        var ambient = hero.File == real.Hero.File && real.Ambient is not null ? real.Ambient : ComputeAmbient(game, hero, real.SquareArt, real.Icon);
        return Build(game, ConfigStamp(game), real.Signature, real.Located, custom, sgdb, real.Icon, real.CoverSmeared, real.SquareCovers, ambient, hypothetical: true);
    }

    /// <summary>Geltende Quelle je Bildart aus den ermittelten Angaben (ohne Plattenzugriff).</summary>
    private GameState Build(
        GameConfig game,
        string stamp,
        string signature,
        LocatedArtwork located,
        Dictionary<ArtworkKind, string> custom,
        Dictionary<ArtworkKind, string> sgdb,
        IndexedIcon icon,
        bool smeared,
        IReadOnlySet<string> squareCovers,
        IndexedAmbient? ambient,
        bool hypothetical = false)
    {
        var hero = HeroSpec(game, located, custom, sgdb);
        var logo = LogoSpec(game, located, custom, sgdb);
        var cover = CoverSpec(game, located, custom, sgdb, hero, smeared);
        if (cover is { Generate: Generator.None, File: { } coverFile, Origin: ArtworkOrigin.Launcher or ArtworkOrigin.Alternative } && squareCovers.Contains(coverFile))
        {
            cover = cover with { Generate = Generator.CoverFromSquare };
        }
        var header = located.Header is { } h
            ? new ArtSpec(ArtworkKind.Header, h.Path, ArtworkOrigin.Launcher, CacheLabel(h.Cache), Generator.None)
            : hero.File is not null
                ? hero with { Kind = ArtworkKind.Header }
                : new ArtSpec(ArtworkKind.Header, null, ArtworkOrigin.Generated, ArtworkStrings.Source_Generated, Generator.Header);
        var field = hero.Origin == ArtworkOrigin.Launcher && located.Hero is { Cache: LauncherCache.Steam } && hero.File == located.Hero.Path && located.Field is { } blur
            ? new ArtSpec(ArtworkKind.Field, blur.Path, ArtworkOrigin.Launcher, CacheLabel(blur.Cache), Generator.None)
            : new ArtSpec(ArtworkKind.Field, null, hero.File is null ? ArtworkOrigin.Generated : hero.Origin, hero.Label, Generator.Field);
        var iconSpec = icon.Path is null
            ? new ArtSpec(ArtworkKind.Icon, null, ArtworkOrigin.Generated, ArtworkStrings.Source_Monogram, Generator.Monogram)
            : new ArtSpec(ArtworkKind.Icon, icon.Path, icon.Kind == "custom" ? ArtworkOrigin.Custom : ArtworkOrigin.Launcher, IconLabel(icon), Generator.None);
        return new GameState
        {
            Key = game.Key,
            ConfigStamp = stamp,
            Signature = signature,
            Game = game,
            Located = located,
            Icon = icon,
            CoverSmeared = smeared,
            SquareCovers = squareCovers,
            Hero = hero,
            Logo = logo,
            Cover = cover,
            Header = header,
            Field = field,
            IconSpec = iconSpec,
            Ambient = ambient,
            Summary = Summary(game, hero, logo, cover, iconSpec, icon, smeared, located.Cover is { } automatic && squareCovers.Contains(automatic.Path)),
            IsHypothetical = hypothetical,
        };
    }

    /// <summary>Eigene Bilddateien je Bildart, die es gibt (Kennzeichen „custom:“).</summary>
    private Dictionary<ArtworkKind, string> CustomFiles(GameConfig game)
    {
        var files = new Dictionary<ArtworkKind, string>();
        foreach (var (kind, token) in new[] { (ArtworkKind.Hero, game.Artwork?.Hero), (ArtworkKind.Logo, game.Artwork?.Logo), (ArtworkKind.Cover, game.Artwork?.Cover) })
        {
            if (ArtworkOverride.KindOf(token) == ArtworkOverrideKind.Custom && ArtworkOverride.FileOf(token) is { } file)
            {
                var path = Path.Combine(_options.ArtDirectory, ArtworkFiles.CustomFolder, file);
                if (_fileSystem.FileExists(path))
                {
                    files[kind] = path;
                }
                else
                {
                    _logger.LogDebug("Bilder: eigene Datei {File} von {Game} fehlt, es gilt das automatische Bild.", file, game.Key);
                }
            }
        }

        return files;
    }

    /// <summary>Eigenes Symbol („Symbol ändern…“), falls gesetzt und vorhanden.</summary>
    private string? CustomIconPath(GameConfig game)
    {
        if (_options.IconDirectory is not { } directory || !CustomIcons.IsValidFileName(game.CustomIcon))
        {
            return null;
        }

        var path = Path.Combine(directory, game.CustomIcon!);
        return _fileSystem.FileExists(path) ? path : null;
    }

    /// <summary>
    /// Gespeicherte Bilder von SteamGridDB je Bildart (nur vorhandene Dateien), nur solange SteamGridDB eingeschaltet ist:
    /// ausgeschaltet gelten wieder die lokalen bzw. erzeugten Bilder; die Dateien bleiben für ein erneutes Einschalten.
    /// </summary>
    private Dictionary<ArtworkKind, string> SteamGridDbFiles(string gameKey)
    {
        var files = new Dictionary<ArtworkKind, string>();
        if (!_options.SteamGridDbEnabled())
        {
            return files;
        }

        var stored = Entry(gameKey, e => e.SteamGridDb?.Files is { } f ? new Dictionary<string, string>(f) : null);
        foreach (var (name, file) in stored ?? [])
        {
            if (KindOfName(name) is { } kind && ArtworkOverride.IsPlainImageName(file))
            {
                var path = Path.Combine(_options.ArtDirectory, ArtworkFiles.SteamGridDbFolder, file);
                if (_fileSystem.FileExists(path))
                {
                    files[kind] = path;
                }
            }
        }

        return files;
    }

    private ArtSpec HeroSpec(GameConfig game, LocatedArtwork located, Dictionary<ArtworkKind, string> custom, Dictionary<ArtworkKind, string> sgdb)
    {
        var token = game.Artwork?.Hero;
        switch (ArtworkOverride.KindOf(token))
        {
            case ArtworkOverrideKind.Custom when custom.TryGetValue(ArtworkKind.Hero, out var file):
                return new ArtSpec(ArtworkKind.Hero, file, ArtworkOrigin.Custom, ArtworkStrings.Source_Custom, Generator.None);
            case ArtworkOverrideKind.Alternative when located.Alternative(ArtworkKind.Hero, ArtworkOverride.FileOf(token)!) is { } alternative:
                return new ArtSpec(ArtworkKind.Hero, alternative.Path, ArtworkOrigin.Alternative, CacheLabel(alternative.Cache), Generator.None);
            case ArtworkOverrideKind.Generated:
                return Generated(ArtworkKind.Hero, Generator.Hero);
        }

        if (located.Hero is { } hero)
        {
            return new ArtSpec(ArtworkKind.Hero, hero.Path, ArtworkOrigin.Launcher, CacheLabel(hero.Cache), Generator.None);
        }

        return sgdb.TryGetValue(ArtworkKind.Hero, out var fromSgdb)
            ? new ArtSpec(ArtworkKind.Hero, fromSgdb, ArtworkOrigin.SteamGridDb, ArtworkStrings.Source_SteamGridDb, Generator.None)
            : Generated(ArtworkKind.Hero, Generator.Hero);
    }

    private ArtSpec LogoSpec(GameConfig game, LocatedArtwork located, Dictionary<ArtworkKind, string> custom, Dictionary<ArtworkKind, string> sgdb)
    {
        var token = game.Artwork?.Logo;
        switch (ArtworkOverride.KindOf(token))
        {
            case ArtworkOverrideKind.Custom when custom.TryGetValue(ArtworkKind.Logo, out var file):
                return new ArtSpec(ArtworkKind.Logo, file, ArtworkOrigin.Custom, ArtworkStrings.Source_Custom, Generator.None);
            case ArtworkOverrideKind.Alternative when located.Alternative(ArtworkKind.Logo, ArtworkOverride.FileOf(token)!) is { } alternative:
                return new ArtSpec(ArtworkKind.Logo, alternative.Path, ArtworkOrigin.Alternative, CacheLabel(alternative.Cache), Generator.None);
            case ArtworkOverrideKind.Generated:
                return NoLogo();
        }

        if (located.Logo is { } logo)
        {
            return new ArtSpec(ArtworkKind.Logo, logo.Path, ArtworkOrigin.Launcher, CacheLabel(logo.Cache), Generator.None);
        }

        return sgdb.TryGetValue(ArtworkKind.Logo, out var fromSgdb)
            ? new ArtSpec(ArtworkKind.Logo, fromSgdb, ArtworkOrigin.SteamGridDb, ArtworkStrings.Source_SteamGridDb, Generator.None)
            : NoLogo();

        static ArtSpec NoLogo() => new(ArtworkKind.Logo, null, ArtworkOrigin.None, ArtworkStrings.Source_Name, Generator.None);
    }

    private ArtSpec CoverSpec(GameConfig game, LocatedArtwork located, Dictionary<ArtworkKind, string> custom, Dictionary<ArtworkKind, string> sgdb, ArtSpec hero, bool smeared)
    {
        var generated = Generated(ArtworkKind.Cover, hero.File is not null ? Generator.CoverFromHero : Generator.CoverFromField);
        var token = game.Artwork?.Cover;
        switch (ArtworkOverride.KindOf(token))
        {
            case ArtworkOverrideKind.Custom when custom.TryGetValue(ArtworkKind.Cover, out var file):
                return new ArtSpec(ArtworkKind.Cover, file, ArtworkOrigin.Custom, ArtworkStrings.Source_Custom, Generator.None);
            case ArtworkOverrideKind.Alternative when located.Alternative(ArtworkKind.Cover, ArtworkOverride.FileOf(token)!) is { } alternative:
                return new ArtSpec(ArtworkKind.Cover, alternative.Path, ArtworkOrigin.Alternative, CacheLabel(alternative.Cache), Generator.None);
            case ArtworkOverrideKind.Generated:
                return generated;
        }

        if (located.Cover is { } cover && !smeared)
        {
            return new ArtSpec(ArtworkKind.Cover, cover.Path, ArtworkOrigin.Launcher, CacheLabel(cover.Cache), Generator.None);
        }

        if (sgdb.TryGetValue(ArtworkKind.Cover, out var fromSgdb))
        {
            return new ArtSpec(ArtworkKind.Cover, fromSgdb, ArtworkOrigin.SteamGridDb, ArtworkStrings.Source_SteamGridDb, Generator.None);
        }

        return generated;
    }

    private static ArtSpec Generated(ArtworkKind kind, Generator generator) =>
        new(kind, null, ArtworkOrigin.Generated, ArtworkStrings.Source_Generated, generator);

    /// <summary>
    /// Symbol: eigenes Symbol, sonst das größte der Symbol-Hinweise des Launchers, des Symbols im Bildcache (GOG) und des
    /// Kachelbilds (Xbox); die EXE nur bei Epic, EA, Battle.net, Quiver und manuellen Spielen oder wenn ein Spiel ohne Titelbild nur
    /// ein kleines Symbol (unter 128 px) hat. Das 32-px-Gemeinschaftssymbol von Steam nur als letzter Ersatz; sonst Monogramm.
    /// Deckende Bilder ab 256 px gelten als Kachelbild.
    /// </summary>
    private IndexedIcon ChooseIcon(GameConfig game, LocatedArtwork located, bool hasHero, string? customIcon)
    {
        if (customIcon is not null)
        {
            return new IndexedIcon { Path = customIcon, Px = CustomIcons.StoredSize, Kind = "custom" };
        }

        IndexedIcon? best = null;
        void Consider(string path, string kind)
        {
            try
            {
                var (width, height) = ArtworkImaging.Measure(path);
                var px = Math.Min(width, height);
                if (px <= 0)
                {
                    return;
                }

                var opaque = kind is "image" or "tile" && px >= TileMinimum && IsOpaqueImage(path);
                var candidate = new IndexedIcon { Path = path, Px = px, Kind = opaque ? "tile" : kind == "tile" ? "image" : kind, Opaque = opaque };
                if (best is null || candidate.Px > best.Px)
                {
                    best = candidate;
                }
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                _logger.LogDebug("Bilder: Symbol {Path} nicht lesbar: {Error}", path, ex.GetType().Name);
            }
        }

        foreach (var candidate in game.IconCandidates)
        {
            if (!IconFiles.IsReadableCandidate(candidate) || !_fileSystem.FileExists(candidate))
            {
                continue;
            }

            var extension = Path.GetExtension(candidate).ToLowerInvariant();
            Consider(candidate, extension switch { ".ico" => "ico", ".exe" => "exe", _ => "image" });
        }

        if (located.CacheIcon is { } cacheIcon)
        {
            Consider(cacheIcon.Path, "cache");
        }

        if (located.Tile is { } tile)
        {
            Consider(tile.Path, "tile");
        }

        var needExe = game.Source is GameSource.Epic or GameSource.EA or GameSource.BattleNet or GameSource.Manual or GameSource.Quiver
            || (!hasHero && (best?.Px ?? 0) < ExeIconWanted);
        if (needExe && ExecutableIcon(game) is { } exe && exe.Px > (best?.Px ?? 0))
        {
            best = exe;
        }

        if (best is null && located.CommunityIcon is { } community)
        {
            best = new IndexedIcon { Path = community.Path, Px = 32, Kind = "cache" };
        }

        return best ?? new IndexedIcon { Kind = "monogram" };
    }

    /// <summary>Symbol der Haupt-EXE (Hilfsprogramme und Ausschlüsse übersprungen, ohne tiefere Suche); Standardsymbole zählen nicht.</summary>
    private IndexedIcon? ExecutableIcon(GameConfig game)
    {
        try
        {
            var resolver = new IconExecutableResolver(_fileSystem);
            var reader = _iconReader.Value;
            if (resolver.ResolveWithIcon(game, _options.HelperPatterns(), path => reader.Read(path), deepSearch: false) is { Icon.IsGeneric: false } exe)
            {
                return new IndexedIcon { Path = exe.Path, Px = Math.Max(exe.Icon.NativeSize, 32), Kind = "exe" };
            }
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: EXE-Symbol von {Game} nicht lesbar: {Error}", game.Key, ex.GetType().Name);
        }

        return null;
    }

    private bool IsOpaqueImage(string path)
    {
        try
        {
            return PixelAnalysis.IsOpaque(ArtworkImaging.DecodeStretched(path, 24, 24).Pixels);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            return false;
        }
    }

    /// <summary>Prüft ein Steam-Cover auf eine verschmierte Kapsel (60×90, siehe <see cref="SmearMetrics"/>) und merkt es sich.</summary>
    private bool StoreSmear(string gameKey, string coverPath)
    {
        bool smeared;
        string? metrics = null;
        try
        {
            var image = ArtworkImaging.DecodeStretched(coverPath, PixelAnalysis.SmearWidth, PixelAnalysis.SmearHeight);
            var m = PixelAnalysis.Smear(image.Pixels);
            smeared = m.IsSmeared;
            metrics = m.ToString();
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: Cover {Path} nicht lesbar: {Error}", coverPath, ex.GetType().Name);
            smeared = false;
        }

        WithEntry(gameKey, e =>
        {
            e.CoverSmeared = smeared;
            e.SmearMetrics = metrics;
        });
        return smeared;
    }

    /// <summary>
    /// Umgebungsfarben: aus dem Titelbild (Szene, Farbfeld 8×3), sonst aus einem quadratischen Cover des Launchers (Quiver,
    /// steht dann auch im erzeugten Titelbild und Cover), aus Symbol oder Kachelbild (Farbfeld 6×6), sonst aus dem Namen (fester
    /// Farbton je Name, Farbfeld als Verlauf).
    /// </summary>
    private IndexedAmbient ComputeAmbient(GameConfig game, ArtSpec hero, string? squareArt, IndexedIcon icon)
    {
        foreach (var (path, scene) in new[] { (hero.File, true), (squareArt, false), (icon.Path, false) })
        {
            if (path is null)
            {
                continue;
            }

            try
            {
                var (width, height) = ArtworkImaging.Measure(path);
                var small = ArtworkImaging.DecodeStretched(path, PixelAnalysis.AmbientWidth, PixelAnalysis.AmbientHeight(width, height));
                if (PixelAnalysis.Ambient(small.Pixels, small.Width, small.Height, scene) is { } result)
                {
                    return IndexedAmbient.From(result, scene);
                }
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                _logger.LogDebug("Bilder: Farben aus {Path} nicht lesbar: {Error}", path, ex.GetType().Name);
            }
        }

        return NameAmbient(game.Name);
    }

    /// <summary>Farben aus dem Namen samt Farbfeld als Verlauf 120° (a1 → a2 abgedunkelt → a3 abgedunkelt, wie nameField).</summary>
    internal static IndexedAmbient NameAmbient(string name)
    {
        var colors = AmbientColors.FromName(name);
        const int w = 8, h = 3;
        var stops = new[]
        {
            (Offset: 0.0, Color: PixelAnalysis.HexToRgb(colors.Deep)),
            (Offset: 0.55, Color: Mix(colors.Glow, 0.35)),
            (Offset: 1.0, Color: Mix(colors.Shine, 0.45)),
        };
        var pixels = new byte[w * h * 4];
        var angle = 120 * Math.PI / 180;
        double dx = Math.Sin(angle), dy = -Math.Cos(angle);
        var length = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var t = Math.Clamp(((((x + 0.5) - (w / 2.0)) * dx) + (((y + 0.5) - (h / 2.0)) * dy) + length) / (2 * length), 0, 1);
                var i = t <= 0.55 ? 0 : 1;
                var local = (t - stops[i].Offset) / (stops[i + 1].Offset - stops[i].Offset);
                var o = ((y * w) + x) * 4;
                pixels[o + 2] = (byte)Math.Round(stops[i].Color.R + ((stops[i + 1].Color.R - stops[i].Color.R) * local));
                pixels[o + 1] = (byte)Math.Round(stops[i].Color.G + ((stops[i + 1].Color.G - stops[i].Color.G) * local));
                pixels[o] = (byte)Math.Round(stops[i].Color.B + ((stops[i + 1].Color.B - stops[i].Color.B) * local));
                pixels[o + 3] = 255;
            }
        }

        return IndexedAmbient.From(new AmbientResult(colors, pixels, w, h), fromHero: false);

        static (int R, int G, int B) Mix(string hex, double towardsInk)
        {
            var (r, g, b) = PixelAnalysis.HexToRgb(hex);
            return ((int)Math.Round(r + ((5 - r) * towardsInk)), (int)Math.Round(g + ((6 - g) * towardsInk)), (int)Math.Round(b + ((10 - b) * towardsInk)));
        }
    }

    /// <summary>
    /// Zeile für den Detailbereich und Herkunft je Bildart (wie artLine im Prototyp). Ohne Titelbild im Cache: ein
    /// quadratisches Cover (Quiver) oder ein Cover aus dem Cache wird genannt; „kein Bildcache“ nur bei Launchern ohne Bildcache.
    /// </summary>
    private static ArtworkSummary Summary(GameConfig game, ArtSpec hero, ArtSpec logo, ArtSpec cover, ArtSpec iconSpec, IndexedIcon icon, bool smeared, bool squareArt)
    {
        var source = LibraryText.SourceName(game.Source);
        string description;
        if (hero.Origin == ArtworkOrigin.Custom)
        {
            description = ArtworkStrings.Summary_Custom;
        }
        else if (hero.Origin is ArtworkOrigin.Launcher or ArtworkOrigin.Alternative)
        {
            description = Format(ArtworkStrings.Summary_LocalCache, hero.Label);
        }
        else if (hero.Origin == ArtworkOrigin.SteamGridDb)
        {
            description = ArtworkStrings.Summary_SteamGridDb;
        }
        else if (ArtworkOverride.KindOf(game.Artwork?.Hero) == ArtworkOverrideKind.Generated)
        {
            description = ArtworkStrings.Summary_GeneratedChoice;
        }
        else if (game.Source == GameSource.Xbox && icon.Kind == "tile")
        {
            description = Format(ArtworkStrings.Summary_XboxTile, icon.Px);
        }
        else if (game.Source == GameSource.Steam && icon.Path is not null)
        {
            // Steam hat einen Bildcache, nur kein Titelbild für dieses Spiel (auch wenn das Symbol aus der EXE kommt).
            description = ArtworkStrings.Summary_SteamNoHero;
        }
        else if (squareArt)
        {
            description = Format(ArtworkStrings.Summary_SquareArt, ArtworkStrings.Source_QuiverCache);
        }
        else if (cover.Origin is ArtworkOrigin.Launcher or ArtworkOrigin.Alternative)
        {
            description = Format(icon.Path is null ? ArtworkStrings.Summary_CoverFromCacheNoIcon : ArtworkStrings.Summary_CoverFromCache, cover.Label);
        }
        else if (icon.Kind == "exe" && icon.Path is { } exe && game.Source is GameSource.Epic or GameSource.EA or GameSource.BattleNet or GameSource.Manual)
        {
            // Nur Launcher ohne eigenen Bildcache; Steam, GOG, Ubisoft, Xbox und Quiver haben einen, nur ohne Titelbild.
            description = Format(ArtworkStrings.Summary_ExeIcon, source, Path.GetFileName(exe), icon.Px);
        }
        else
        {
            description = Format(icon.Path is null ? ArtworkStrings.Summary_NoHeroFromName : ArtworkStrings.Summary_NoHeroFromIcon, source);
        }

        return new ArtworkSummary(
            hero.Origin,
            logo.Origin,
            cover.Origin,
            iconSpec.Origin,
            LogoIsName: logo.File is null,
            CoverFromSmear: smeared && cover.Generate == Generator.CoverFromHero,
            description);
    }

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string CacheLabel(LauncherCache cache) => cache switch
    {
        LauncherCache.Steam => ArtworkStrings.Source_SteamCache,
        LauncherCache.Gog => ArtworkStrings.Source_GogCache,
        LauncherCache.Ubisoft => ArtworkStrings.Source_UbisoftCache,
        LauncherCache.Quiver => ArtworkStrings.Source_QuiverCache,
        _ => ArtworkStrings.Source_XboxPackage,
    };

    private static string IconLabel(IndexedIcon icon) => icon.Kind switch
    {
        "custom" => ArtworkStrings.Source_CustomIcon,
        "ico" => Format(ArtworkStrings.Source_IconFile, icon.Px),
        "exe" => Format(ArtworkStrings.Source_Executable, Path.GetFileName(icon.Path), icon.Px),
        "tile" => Format(ArtworkStrings.Source_XboxTile, icon.Px),
        "cache" => Format(ArtworkStrings.Source_CacheIcon, icon.Px),
        _ => Format(ArtworkStrings.Source_ImageFile, icon.Px),
    };

    private static ArtworkKind? KindOfName(string name) => name switch
    {
        "hero" => ArtworkKind.Hero,
        "logo" => ArtworkKind.Logo,
        "cover" => ArtworkKind.Cover,
        _ => null,
    };

    private static string NameOf(ArtworkKind kind) => kind.ToString().ToLowerInvariant();

    // ---- index.json --------------------------------------------------------------------------------------------------

    /// <summary>Liest (beim ersten Mal von der Platte) und ändert den Eintrag eines Spiels unter der Sperre des Verzeichnisses.</summary>
    private T WithEntry<T>(string gameKey, Func<ArtworkIndexEntry, T> action)
    {
        lock (_indexGate)
        {
            var index = _index ??= _indexFile.Load();
            if (!index.Games.TryGetValue(gameKey, out var entry))
            {
                index.Games[gameKey] = entry = new ArtworkIndexEntry();
            }

            return action(entry);
        }
    }

    private void WithEntry(string gameKey, Action<ArtworkIndexEntry> action)
    {
        WithEntry(gameKey, e =>
        {
            action(e);
            return 0;
        });
        MarkDirty();
    }

    /// <summary>Wert aus dem Eintrag eines Spiels (ohne Änderung).</summary>
    private T Entry<T>(string gameKey, Func<ArtworkIndexEntry, T> read) => WithEntry(gameKey, read);

    private IndexedIcon StoreIcon(string gameKey, IndexedIcon icon)
    {
        WithEntry(gameKey, e => e.Icon = icon);
        return icon;
    }

    private IndexedAmbient StoreAmbient(string gameKey, IndexedAmbient ambient)
    {
        WithEntry(gameKey, e => e.Ambient = ambient);
        return ambient;
    }

    /// <summary>Speichern nach kurzer Pause (gesammelt), nur solange ein Fenster offen ist; sonst beim Ende des Berichts.</summary>
    private void MarkDirty()
    {
        lock (_indexGate)
        {
            _indexDirty = true;
            if (Volatile.Read(ref _leases) > 0 && !_disposed)
            {
                _saveTimer ??= new Timer(_ => FlushIndex(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Geänderte Kennung: abgeleitete Daten verwerfen und ihre Dateien löschen (SteamGridDB bleibt).</summary>
    private void ResetIfStale(string gameKey, string signature)
    {
        var stale = new List<string>();
        var changed = WithEntry(gameKey, e =>
        {
            if (e.Signature == signature)
            {
                return false;
            }

            stale.AddRange(DerivedFiles(e));
            e.ResetDerived(signature);
            return true;
        });
        if (!changed)
        {
            return;
        }

        MarkDirty();
        DeleteQuietly(stale);
    }

    /// <summary>
    /// Abgeleitete Dateien eines Eintrags (erzeugt, helle Fassungen, zugeschnittenes Logo) in ihren Ordnern unter art. Nur
    /// schlichte Dateinamen zählen, geprüft vor dem Zusammensetzen: Ein von Hand geändertes index.json mit absolutem Pfad oder
    /// „..“ kann so nie eine Datei außerhalb von art treffen.
    /// </summary>
    private List<string> DerivedFiles(ArtworkIndexEntry entry)
    {
        var files = new List<string>();
        void Add(string folder, string? name)
        {
            if (ArtworkOverride.IsPlainImageName(name))
            {
                files.Add(Path.Combine(_options.ArtDirectory, folder, name!));
            }
        }

        foreach (var name in entry.Generated?.Values ?? Enumerable.Empty<string>())
        {
            Add(ArtworkFiles.GeneratedFolder, name);
        }

        foreach (var name in entry.Boost?.Values ?? Enumerable.Empty<string>())
        {
            Add(ArtworkFiles.BoostFolder, name);
        }

        Add(ArtworkFiles.LogoFolder, entry.Logo);
        return files;
    }

    /// <summary>
    /// Räumt Bilder von Spielen auf, die nicht mehr in der Bibliothek stehen: ihre Einträge in index.json und alle Dateien mit
    /// ihrem Stamm (<see cref="ArtworkFiles.StemOf"/>) in art (erzeugt, Logos, helle Fassungen, eigene Bilder, SteamGridDB).
    /// Nur mit bekannter, nicht leerer Bibliothek: Eine unlesbare Konfiguration löscht nichts. Läuft nach dem Schließen des
    /// letzten Fensters im Hintergrund; liefert die Zahl gelöschter Dateien. Wirft nicht.
    /// </summary>
    public int PruneRemovedGames()
    {
        IReadOnlyCollection<string>? keys;
        try
        {
            keys = _options.LibraryKeys?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogDebug("Bilder: Bibliothek zum Aufräumen nicht lesbar: {Message}", ex.Message);
            return 0;
        }

        if (keys is not { Count: > 0 })
        {
            return 0;
        }

        var library = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var stems = new HashSet<string>(keys.Select(ArtworkFiles.Stem), StringComparer.OrdinalIgnoreCase);
        int removed;
        lock (_indexGate)
        {
            var index = _index ??= _indexFile.Load();
            var gone = index.Games.Keys.Where(k => !library.Contains(k)).ToList();
            foreach (var key in gone)
            {
                index.Games.Remove(key);
            }

            removed = gone.Count;
            _indexDirty |= removed > 0;
        }

        var stale = new List<string>();
        foreach (var folder in new[] { ArtworkFiles.GeneratedFolder, ArtworkFiles.LogoFolder, ArtworkFiles.BoostFolder, ArtworkFiles.CustomFolder, ArtworkFiles.SteamGridDbFolder })
        {
            var directory = Path.Combine(_options.ArtDirectory, folder);
            try
            {
                if (_fileSystem.DirectoryExists(directory))
                {
                    stale.AddRange(_fileSystem.EnumerateFiles(directory).Where(f => ArtworkFiles.StemOf(Path.GetFileName(f)) is { } stem && !stems.Contains(stem)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Bilder: Ordner {Folder} nicht aufgeräumt: {Message}", folder, ex.Message);
            }
        }

        DeleteQuietly(stale);
        if (removed > 0 || stale.Count > 0)
        {
            _logger.LogInformation("Bilder: {Games} entfernte Spiele aus dem Verzeichnis genommen, {Files} Dateien gelöscht.", removed, stale.Count);
        }

        return stale.Count;
    }

    private void DeleteQuietly(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                _fileSystem.DeleteFile(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Bilder: alte Datei {File} nicht gelöscht: {Message}", file, ex.Message);
            }
        }
    }

    /// <summary>Gefundene Bilder samt Größe ins Verzeichnis (Bericht, Abdeckung, „Bild wählen…“).</summary>
    private void RecordLocated(string gameKey, LocatedArtwork located)
    {
        if (Entry(gameKey, e => e.Located.Count > 0 || e.Alternatives is not null))
        {
            return;
        }

        var images = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
        void Add(string name, LocatedImage? image)
        {
            if (image is not null)
            {
                images[name] = Measured(image);
            }
        }

        Add("hero", located.Hero);
        Add("logo", located.Logo);
        Add("cover", located.Cover);
        Add("header", located.Header);
        Add("field", located.Field);
        Add("community", located.CommunityIcon);
        Add("cacheicon", located.CacheIcon);
        Add("tile", located.Tile);
        var alternatives = located.Alternatives.Select(Measured).ToList();
        WithEntry(gameKey, e =>
        {
            e.Located = images;
            e.Alternatives = alternatives;
        });
    }

    private static IndexedImage Measured(LocatedImage image)
    {
        int width = 0, height = 0;
        try
        {
            (width, height) = ArtworkImaging.Measure(image.Path);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            // Größe unbekannt: 0.
        }

        return new IndexedImage { Path = image.Path, Width = width, Height = height, Source = image.Cache, Variant = image.Variant };
    }
}
