using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Spielbilder (E89). Gerüst: liest nur den Steam-Bildcache (<c>appcache\librarycache\&lt;appid&gt;</c>, rekursiv, nur
/// lesend; deutsche Varianten zuerst) und dekodiert in der angefragten Breite. Andere Launcher, Ersatzbilder,
/// Umgebungsfarben, helle HDR-Fassung, Zwischenspeicher auf der Platte, Beobachtung der Cache-Ordner und eigene Bilder
/// folgen mit G10.3.
/// </summary>
public sealed class ArtworkService : IArtworkService
{
    private static readonly Dictionary<ArtworkKind, string[]> SteamNames = new()
    {
        [ArtworkKind.Hero] = ["library_hero_german.jpg", "library_hero.jpg"],
        [ArtworkKind.Logo] = ["logo_german.png", "logo.png"],
        [ArtworkKind.Cover] = ["library_600x900_german.jpg", "library_600x900.jpg", "library_capsule_german.jpg", "library_capsule.jpg"],
        [ArtworkKind.Header] = ["header_german.jpg", "header.jpg", "library_header_german.jpg", "library_header.jpg"],
        [ArtworkKind.Field] = ["library_hero_blur_german.jpg", "library_hero_blur.jpg"],
    };

    private readonly IFileSystem _fileSystem;
    private readonly Lazy<string?> _steamRoot;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GameArtwork> _artwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _steamFiles = new(StringComparer.OrdinalIgnoreCase);
    private int _leases;

    /// <param name="steamRoot">Steam-Ordner (etwa aus HKCU\Software\Valve\Steam); null ohne Steam.</param>
    public ArtworkService(IFileSystem fileSystem, Func<string?> steamRoot, ILogger? logger = null)
    {
        _fileSystem = fileSystem;
        _steamRoot = new Lazy<string?>(steamRoot);
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler<ArtworkChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) =>
        _images.TryGetValue(ImageKey(game, kind, decodeWidth), out var image) ? image : null;

    public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default)
    {
        var key = ImageKey(game, kind, decodeWidth);
        if (_images.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached);
        }

        return Task.Run(() => _images.GetOrAdd(key, _ => Load(game, kind, decodeWidth)), cancellationToken);
    }

    public GameArtwork? GetCachedArtwork(GameConfig game) => _artwork.TryGetValue(game.Key, out var artwork) ? artwork : null;

    public async Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        if (_artwork.TryGetValue(game.Key, out var cached))
        {
            return cached;
        }

        var hero = await GetImageAsync(game, ArtworkKind.Hero, heroDecodeWidth, cancellationToken);
        var field = await GetImageAsync(game, ArtworkKind.Field, 64, cancellationToken);
        var logo = await GetImageAsync(game, ArtworkKind.Logo, Math.Max(256, heroDecodeWidth / 3), cancellationToken);
        var cover = await GetImageAsync(game, ArtworkKind.Cover, 600, cancellationToken);
        var origin = hero is null ? ArtworkOrigin.None : ArtworkOrigin.Launcher;
        var summary = new ArtworkSummary(
            origin,
            logo is null ? ArtworkOrigin.None : ArtworkOrigin.Launcher,
            cover is null ? ArtworkOrigin.None : ArtworkOrigin.Launcher,
            ArtworkOrigin.None,
            LogoIsName: logo is null,
            CoverFromSmear: false,
            hero is null && cover is null ? ArtworkStrings.Summary_NoLocalImages : ArtworkStrings.Summary_LocalSteam);
        var result = new GameArtwork(
            game.Key,
            Wrap(hero, ArtworkKind.Hero),
            HeroBoost: null,
            Wrap(field, ArtworkKind.Field),
            Wrap(logo, ArtworkKind.Logo),
            Wrap(cover, ArtworkKind.Cover),
            Icon: null,
            AmbientColors.FromName(game.Name),
            summary);
        return _artwork.GetOrAdd(game.Key, result);
    }

    public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);

    public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath) =>
        throw new InvalidDataException("Eigene Bilder folgen mit G10.3.");

    public void DeleteCustomImage(string? token)
    {
    }

    public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var rows = new List<ArtworkCoverageRow>();
        foreach (var group in games.GroupBy(g => g.Source).OrderBy(g => g.Key))
        {
            int Count(ArtworkKind kind) => group.Count(g => Locate(g, kind) is not null);
            var hero = Count(ArtworkKind.Hero);
            var logo = Count(ArtworkKind.Logo);
            var cover = Count(ArtworkKind.Cover);
            rows.Add(new ArtworkCoverageRow(group.Key, group.Count(), new(hero, hero), new(logo, logo), new(cover, cover), default));
        }

        return new ArtworkCoverage(rows);
    }, cancellationToken);

    public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FallbackExample>>([]);

    public IDisposable Acquire()
    {
        Interlocked.Increment(ref _leases);
        return new Lease(this);
    }

    public void ClearCache()
    {
        _images.Clear();
        _artwork.Clear();
        _steamFiles.Clear();
    }

    public void Dispose() => ClearCache();

    private static string ImageKey(GameConfig game, ArtworkKind kind, int width) => game.Key + "|" + kind + "|" + width;

    private static ArtworkImage? Wrap(ImageSource? image, ArtworkKind kind) => image is BitmapSource bitmap
        ? new ArtworkImage(bitmap, kind, ArtworkOrigin.Launcher, bitmap.PixelWidth, bitmap.PixelHeight, ArtworkStrings.Source_SteamCache)
        : null;

    private ImageSource? Load(GameConfig game, ArtworkKind kind, int width)
    {
        if (Locate(game, kind) is not { } path)
        {
            return null;
        }

        try
        {
            return Decode(path, width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug("Bild nicht lesbar: {Path} ({Error})", path, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Datei einer Bildart im Steam-Cache (deutsche Variante zuerst); null für andere Quellen oder ohne Datei.</summary>
    private string? Locate(GameConfig game, ArtworkKind kind)
    {
        if (game.Source != GameSource.Steam || !SteamNames.TryGetValue(kind, out var names) || _steamRoot.Value is not { } root)
        {
            return null;
        }

        var files = _steamFiles.GetOrAdd(game.SourceId, id =>
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (id.All(char.IsAsciiDigit))
            {
                foreach (var file in _fileSystem.EnumerateFiles(Path.Combine(root, "appcache", "librarycache", id), "*", recursive: true))
                {
                    byName.TryAdd(Path.GetFileName(file), file);
                }
            }

            return byName;
        });
        return names.Select(n => files.TryGetValue(n, out var path) ? path : null).FirstOrDefault(p => p is not null);
    }

    /// <summary>Dekodiert höchstens in der Originalbreite (nie hochskaliert), liest geteilt (Steam darf schreiben).</summary>
    private static BitmapSource Decode(string path, int width)
    {
        int sourceWidth;
        using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            sourceWidth = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        if (width > 0 && width < sourceWidth)
        {
            image.DecodePixelWidth = width;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed class Lease(ArtworkService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && Interlocked.Decrement(ref owner._leases) == 0)
            {
                owner.ClearCache();
            }
        }
    }
}
