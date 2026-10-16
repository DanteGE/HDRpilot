using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Laden der Bilder: Dateien dekodieren, Ersatzbilder erzeugen, Logos zuschneiden, helle HDR-Fassungen.</summary>
public sealed partial class ArtworkService
{
    /// <summary>Die helle HDR-Fassung wird höchstens in dieser Breite berechnet (Speicher, Zeit).</summary>
    public const int MaxBoostWidth = 2560;

    /// <summary>Breite des Covers im Satz für den Titelbereich (Detailbereich, Regal: höchstens zweifach dargestellt).</summary>
    public const int ArtworkCoverWidth = 400;

    /// <summary>Lädt eine Bildart (Arbeits-Thread); fehlt die Datei oder ist sie unlesbar, gilt das Ersatzbild.</summary>
    private ImageSource? LoadImage(GameConfig game, ArtworkKind kind, int width)
    {
        var state = Resolve(game, full: false);
        var spec = state.Spec(kind);
        if (spec.Generate != Generator.None)
        {
            state = Resolve(game, full: true);
            spec = state.Spec(kind);
        }

        return LoadSpec(state, spec, width) ?? LoadFallback(game, kind, width);
    }

    /// <summary>
    /// Satz für den Titelbereich: Titelbild, Farbfeld, Logo, Cover, Symbol und Farben, ohne helle Fassung (die gibt es nur auf
    /// Anfrage, <see cref="GetHeroBoostAsync"/>). Der Satz zählt zum Speicherbudget (<see cref="StoreSet"/>).
    /// </summary>
    private GameArtwork LoadArtwork(GameConfig game, int heroWidth)
    {
        var state = Resolve(game, full: true);
        var hero = LoadSpec(state, state.Hero, heroWidth) ?? LoadFallback(game, ArtworkKind.Hero, heroWidth);
        var field = LoadSpec(state, state.Field, NormalizeWidth(ArtworkKind.Field, 64)) ?? LoadFallback(game, ArtworkKind.Field, 64);
        var logo = LoadSpec(state, state.Logo, Math.Clamp(heroWidth / 3, 256, 1280));
        var cover = LoadSpec(state, state.Cover, ArtworkCoverWidth) ?? LoadFallback(game, ArtworkKind.Cover, ArtworkCoverWidth);
        var icon = LoadSpec(state, state.IconSpec, 256) ?? LoadFallback(game, ArtworkKind.Icon, 256);
        var artwork = new GameArtwork(
            game.Key,
            Wrap(hero, state.Hero),
            null,
            Wrap(field, state.Field),
            Wrap(logo, state.Logo),
            Wrap(cover, state.Cover),
            Wrap(icon, state.IconSpec),
            (state.Ambient ?? NameAmbient(game.Name)).ToColors(),
            state.Summary);
        StoreSet(game.Key, new CachedArtwork(state.ConfigStamp, heroWidth, artwork));
        return artwork;
    }

    /// <summary>
    /// Helle Fassung des Titelbilds in der Breite des Satzes (Arbeits-Thread); null, wenn sie sich nicht berechnen lässt oder
    /// gerade ein Spiel läuft (dann rechnet HDRpilot nichts, auch keine Anfrage, die schon vorher wartete).
    /// </summary>
    private ArtworkImage? LoadHeroBoost(GameConfig game, int heroWidth)
    {
        if (_options.GameRunning())
        {
            _logger.LogDebug("Bilder: helle Fassung von {Game} nicht berechnet, ein Spiel läuft.", game.Key);
            return null;
        }

        var state = Resolve(game, full: true);
        var key = BoostKey(state, heroWidth);
        if (_memory.TryGetValue(key, out var known) && known is ArtworkImage cached)
        {
            return cached;
        }

        try
        {
            if (Wrap(LoadBoost(state, heroWidth), state.Hero) is not { } boost)
            {
                return null;
            }

            _memory.Set(key, boost, ImageMemoryCache.SizeOf(boost.Image));
            return boost;
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: helle Fassung von {Game} nicht berechnet: {Error}", game.Key, ex.GetType().Name);
            return null;
        }
    }

    private static string BoostKey(GameState state, int width) => ImageKey(state, ArtworkKind.Hero, width) + "|boost";

    private static ArtworkImage? Wrap(ImageSource? image, ArtSpec spec) => image is BitmapSource bitmap
        ? new ArtworkImage(bitmap, spec.Kind, spec.Origin, bitmap.PixelWidth, bitmap.PixelHeight, spec.Label)
        : null;

    /// <summary>Bild nach seiner Quelle; null, wenn es keins gibt (Logo) oder die Datei unlesbar ist.</summary>
    private ImageSource? LoadSpec(GameState state, ArtSpec spec, int width)
    {
        var key = ImageKey(state, spec.Kind, width);
        if (_memory.TryGet(key, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            image = spec.Generate switch
            {
                Generator.None when spec.File is null => null,
                Generator.None when spec.Kind == ArtworkKind.Logo => TrimmedLogo(state, spec.File!, width),
                Generator.None => ArtworkImaging.Decode(spec.File!, width),
                Generator.Field => FieldImage(state).ToBitmap(),
                _ => ArtworkImaging.Decode(EnsureGenerated(state, spec.Generate), width),
            };
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: {Kind} von {Game} nicht lesbar ({File}): {Error}", spec.Kind, state.Key, spec.File ?? spec.Generate.ToString(), ex.GetType().Name);
            if (spec.Kind == ArtworkKind.Logo || spec.Generate != Generator.None)
            {
                return null;
            }
        }

        if (image is not null || spec.Kind == ArtworkKind.Logo)
        {
            _memory.Set(key, image);
        }

        return image;
    }

    /// <summary>Ersatzbild einer Bildart, wenn die eigentliche Quelle unlesbar war (Titelbild, Cover, Kopfbild, Farbfeld, Symbol).</summary>
    private ImageSource? LoadFallback(GameConfig game, ArtworkKind kind, int width)
    {
        if (kind == ArtworkKind.Logo)
        {
            return null;
        }

        var state = Resolve(game, full: true);
        var generator = kind switch
        {
            ArtworkKind.Hero => Generator.Hero,
            ArtworkKind.Cover => Generator.CoverFromField,
            ArtworkKind.Header => Generator.Header,
            ArtworkKind.Field => Generator.Field,
            _ => Generator.Monogram,
        };
        try
        {
            return generator == Generator.Field
                ? FieldImage(state).ToBitmap()
                : ArtworkImaging.Decode(EnsureGenerated(state, generator), width);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogWarning("Bilder: Ersatzbild {Kind} von {Game} nicht erzeugt: {Error}", kind, game.Key, ex.Message);
            return null;
        }
    }

    /// <summary>Farbfeld des Spiels als kleines Bild (8×3 aus dem Titelbild, 6×6 aus dem Symbol, sonst aus dem Namen).</summary>
    private static BgraImage FieldImage(GameState state)
    {
        var ambient = state.Ambient ?? NameAmbient(state.Game.Name);
        if (ambient.FieldPixels() is not { } pixels)
        {
            ambient = NameAmbient(state.Game.Name);
            pixels = ambient.FieldPixels()!;
        }

        return new BgraImage(ambient.FieldWidth, ambient.FieldHeight, pixels);
    }

    /// <summary>
    /// Pfad eines erzeugten Bildes in art\gen; fehlt es, wird es jetzt gezeichnet und gespeichert (einmal je Kennung des
    /// Spiels). Zeichnen braucht einen STA-Thread (die Arbeits-Threads sind es; sonst ein eigener).
    /// </summary>
    private string EnsureGenerated(GameState state, Generator generator)
    {
        var kind = generator switch
        {
            Generator.Hero => ArtworkKind.Hero,
            Generator.CoverFromHero or Generator.CoverFromField => ArtworkKind.Cover,
            Generator.Header => ArtworkKind.Header,
            Generator.Monogram => ArtworkKind.Icon,
            _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, "Kein erzeugtes Bild."),
        };
        var name = NameOf(kind) + (generator == Generator.CoverFromHero ? "-hero" : string.Empty);
        var file = ArtworkFiles.GeneratedFileName(state.Key, kind);
        var path = Path.Combine(_options.ArtDirectory, ArtworkFiles.GeneratedFolder, file);
        lock (_gameLocks.GetOrAdd(state.Key, _ => new object()))
        {
            if (Entry(state.Key, e => e.Generated is { } g && g.TryGetValue(name, out var known) && known == file) && _fileSystem.FileExists(path))
            {
                return path;
            }

            var image = Sta.Run(() => Render(state, generator));
            ArtworkImaging.Save(image, path, 90);
            WithEntry(state.Key, e =>
            {
                e.Generated ??= new Dictionary<string, string>(StringComparer.Ordinal);
                e.Generated.Remove(NameOf(kind));
                e.Generated.Remove(NameOf(kind) + "-hero");
                e.Generated[name] = file;
            });
            _logger.LogDebug("Bilder: {Kind} von {Game} erzeugt ({Generator}).", kind, state.Key, generator);
            return path;
        }
    }

    private BitmapSource Render(GameState state, Generator generator)
    {
        var name = state.Game.Name;
        var field = FieldImage(state);
        BitmapSource? Icon(int size) => state.Icon.Path is { } p ? TryDecode(p, size) : null;
        var isTile = state.Icon.Kind == "tile";
        switch (generator)
        {
            case Generator.Hero:
                return ArtworkRenderer.Hero(name, field, isTile ? Icon(ArtworkRenderer.HeroHeight) : null);
            case Generator.CoverFromHero when state.Hero.File is { } heroFile && TryDecode(heroFile, 3840) is { } hero:
                var lightX = state.Ambient is { FromHero: true } ambient ? ambient.LightX : 0.5;
                var logo = state.Logo.File is { } logoFile ? (state.IsHypothetical ? TrimInMemory(logoFile, 480) : TrimmedLogo(state, logoFile, 480) as BitmapSource) : null;
                return ArtworkRenderer.CoverFromHero(hero, lightX, logo, name);
            case Generator.CoverFromHero:
            case Generator.CoverFromField:
                return ArtworkRenderer.CoverFromField(name, field, Icon(256), state.Icon.Px, isTile, PixelAnalysis.Monogram(name));
            case Generator.Header:
                return ArtworkRenderer.Header(field, Icon(256), state.Icon.Px, isTile, PixelAnalysis.Monogram(name));
            default:
                return ArtworkRenderer.Monogram(PixelAnalysis.Monogram(name), AmbientColors.FromName(name));
        }
    }

    private BitmapSource? TryDecode(string path, int width)
    {
        try
        {
            return ArtworkImaging.Decode(path, width);
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: {Path} nicht lesbar: {Error}", path, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Logo auf den sichtbaren Teil zugeschnitten (Deckkraft über 8, 2 px Rand), einmal berechnet und als PNG in art\logo
    /// gespeichert; ist nichts abzuschneiden, gilt die Datei selbst.
    /// </summary>
    private ImageSource? TrimmedLogo(GameState state, string file, int width)
    {
        var trimmedFile = ArtworkFiles.LogoFileName(state.Key);
        var trimmedPath = Path.Combine(_options.ArtDirectory, ArtworkFiles.LogoFolder, trimmedFile);
        var (known, trim) = Entry(state.Key, e => (e.Logo, e.LogoTrim));
        if (known == trimmedFile && _fileSystem.FileExists(trimmedPath))
        {
            return ArtworkImaging.Decode(trimmedPath, width);
        }

        if (known is null && trim is { Length: 4 })
        {
            return ArtworkImaging.Decode(file, width);
        }

        var full = ArtworkImaging.ToBgra(ArtworkImaging.Decode(file, 2048));
        var bounds = PixelAnalysis.OpaqueBounds(full.Pixels, full.Width, full.Height);
        if (bounds is not { } rect || (rect.Width == full.Width && rect.Height == full.Height))
        {
            WithEntry(state.Key, e =>
            {
                e.Logo = null;
                e.LogoTrim = [0, 0, full.Width, full.Height];
            });
            return ArtworkImaging.Decode(file, width);
        }

        var cropped = ArtworkImaging.Crop(full.ToBitmap(), rect);
        ArtworkImaging.Save(cropped, trimmedPath);
        WithEntry(state.Key, e =>
        {
            e.Logo = trimmedFile;
            e.LogoTrim = [rect.X, rect.Y, rect.Width, rect.Height];
        });
        return width < cropped.PixelWidth ? ArtworkImaging.Decode(trimmedPath, width) : cropped;
    }

    /// <summary>Logo nur im Speicher zuschneiden (Vorschau einer anderen Wahl), in höchstens dieser Breite.</summary>
    private static BitmapSource TrimInMemory(string file, int width)
    {
        var full = ArtworkImaging.ToBgra(ArtworkImaging.Decode(file, 2048));
        var bitmap = full.ToBitmap();
        if (PixelAnalysis.OpaqueBounds(full.Pixels, full.Width, full.Height) is { } rect && (rect.Width != full.Width || rect.Height != full.Height))
        {
            bitmap = ArtworkImaging.Crop(bitmap, rect);
        }

        return width < bitmap.PixelWidth
            ? ArtworkImaging.Scale(bitmap, width, Math.Max(1, (int)Math.Round(bitmap.PixelHeight * (width / (double)bitmap.PixelWidth))))
            : bitmap;
    }

    /// <summary>
    /// Helle „HDR“-Fassung des Titelbilds (<see cref="PixelAnalysis.Boost"/>) in Stufenbreite bis 2560 px, einmal berechnet und
    /// als JPEG in art\boost gespeichert (nur für Titelbilder, die mit HDR an oder im HDR-Moment zu sehen sind, nie für die
    /// ganze Bibliothek). Die Dateien sind auf <see cref="ArtworkServiceOptions.BoostBudget"/> begrenzt; die am längsten nicht
    /// benutzten fallen heraus.
    /// </summary>
    private ImageSource LoadBoost(GameState state, int width)
    {
        var heroPath = state.Hero.File ?? EnsureGenerated(state, Generator.Hero);
        var (nativeWidth, _) = ArtworkImaging.Measure(heroPath);
        var target = Math.Min(Math.Min(ArtworkFiles.WidthStep(width), MaxBoostWidth), nativeWidth > 0 ? nativeWidth : MaxBoostWidth);
        var file = ArtworkFiles.BoostFileName(state.Key, target);
        var path = Path.Combine(_options.ArtDirectory, ArtworkFiles.BoostFolder, file);
        ImageSource result;
        var widthKey = target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Entry(state.Key, e => e.Boost is { } b && b.TryGetValue(widthKey, out var f) && f == file) && File.Exists(path))
        {
            Touch(path);
            result = ArtworkImaging.Decode(path, Math.Min(width, target));
        }
        else
        {
            var pixels = ArtworkImaging.ToBgra(ArtworkImaging.Decode(heroPath, target));
            PixelAnalysis.Boost(pixels.Pixels);
            var boosted = pixels.ToBitmap();
            ArtworkImaging.Save(boosted, path, 90);
            WithEntry(state.Key, e =>
            {
                e.Boost ??= new Dictionary<string, string>(StringComparer.Ordinal);
                e.Boost[widthKey] = file;
            });
            PruneBoost();
            result = width < boosted.PixelWidth ? ArtworkImaging.Decode(path, width) : boosted;
        }

        return result;
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reihenfolge der Aufräumung bleibt dann die alte.
        }
    }

    /// <summary>Hält die hellen Fassungen unter der Grenze: die am längsten nicht benutzten zuerst löschen.</summary>
    private void PruneBoost()
    {
        try
        {
            var directory = new DirectoryInfo(Path.Combine(_options.ArtDirectory, ArtworkFiles.BoostFolder));
            if (!directory.Exists)
            {
                return;
            }

            var files = directory.GetFiles("*.jpg").OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (total <= _options.BoostBudget)
                {
                    break;
                }

                total -= file.Length;
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Bilder: helle Fassungen nicht aufgeräumt: {Message}", ex.Message);
        }
    }

    /// <summary>Führt Arbeit auf einem STA-Thread aus (der aktuelle, wenn er STA ist, sonst ein eigener, der danach endet).</summary>
    internal static class Sta
    {
        public static T Run<T>(Func<T> work)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                return work();
            }

            T result = default!;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
                }
            })
            {
                IsBackground = true,
                Name = "HDRpilot Bilder (STA)",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }

            return result;
        }
    }
}
