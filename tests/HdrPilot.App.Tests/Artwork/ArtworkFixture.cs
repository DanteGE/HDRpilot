using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Nachgebaute Launcher-Caches und Datenordner in einem eigenen Temp-Ordner für die Tests der Spielbilder (E89): nur kleine,
/// im Test erzeugte Bilder, nie Dateien aus echten Launcher-Caches. Wird beim Dispose gelöscht.
/// </summary>
public sealed class ArtworkFixture : IDisposable
{
    private readonly List<ArtworkService> _services = [];

    public ArtworkFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "hdrpilot-art-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        SteamRoot = Path.Combine(Root, "Steam");
        GogWebCache = Path.Combine(Root, "GOG", "webcache");
        ArtDirectory = Path.Combine(Root, "data", "art");
        IconDirectory = Path.Combine(Root, "data", "icons");
        Directory.CreateDirectory(Path.Combine(SteamRoot, "appcache", "librarycache"));
    }

    public string Root { get; }

    public string SteamRoot { get; }

    public string GogWebCache { get; }

    public string ArtDirectory { get; }

    public string IconDirectory { get; }

    public ArtworkRoots Roots => new(SteamRoot, Directory.Exists(GogWebCache) ? GogWebCache : null, null);

    public string LibraryCache => Path.Combine(SteamRoot, "appcache", "librarycache");

    /// <summary>Bilderdienst auf diesen Ordnern; Ereignisse kommen sofort, ohne Beobachtung der Ordner (außer gewünscht).</summary>
    public ArtworkService Service(
        bool watch = false,
        Func<string, string?>? stamp = null,
        long memoryBudget = 64L * 1024 * 1024,
        TimeSpan? debounce = null,
        int maxParallel = 2,
        Func<bool>? gameRunning = null)
    {
        var service = new ArtworkService(new ArtworkServiceOptions
        {
            ArtDirectory = ArtDirectory,
            IconDirectory = IconDirectory,
            Roots = () => Roots,
            WatchCaches = watch,
            WatchDebounce = debounce ?? TimeSpan.FromMilliseconds(150),
            SaveDelay = TimeSpan.FromMilliseconds(50),
            FileStamp = stamp,
            MemoryBudget = memoryBudget,
            MaxParallel = maxParallel,
            GameRunning = gameRunning ?? (() => false),
        });
        _services.Add(service);
        return service;
    }

    /// <summary>Steam-Bild in einem Unterordner mit Prüfsummen-Namen, wie Steam es ablegt.</summary>
    public string SteamFile(string appId, string fileName, BitmapSource image)
    {
        var dir = Path.Combine(LibraryCache, appId, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return Save(Path.Combine(dir, fileName), image);
    }

    public string Save(string path, BitmapSource image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BitmapEncoder encoder = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(encoder is JpegBitmapEncoder ? new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0) : image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    public static GameConfig Steam(string appId, string name = "Testspiel") => new() { Source = GameSource.Steam, SourceId = appId, Name = name };

    /// <summary>Buntes Titelbild mit hellem Fleck bei etwa 70 % der Breite.</summary>
    public static BitmapSource Hero(int width = 640, int height = 206) => Image(width, height, (x, y) =>
    {
        var spot = Math.Abs(x - (width * 0.7)) < width * 0.05 && Math.Abs(y - (height * 0.4)) < height * 0.1;
        return spot ? ((byte)250, (byte)245, (byte)230, (byte)255) : ((byte)(20 + (x * 60 / width)), (byte)(40 + (y * 50 / height)), (byte)(150 + (x * 80 / width)), (byte)255);
    });

    /// <summary>Logo mit durchsichtigem Rand: deckend nur im Rechteck (left, top, w, h).</summary>
    public static BitmapSource Logo(int width, int height, int left, int top, int w, int h) => Image(width, height, (x, y) =>
        x >= left && x < left + w && y >= top && y < top + h ? ((byte)240, (byte)200, (byte)40, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

    /// <summary>Normales Cover mit Motiv (auch senkrecht Änderungen).</summary>
    public static BitmapSource Cover(int width = 300, int height = 450) => Image(width, height, (x, y) =>
        ((byte)((x * 7 + y * 13) % 256), (byte)((x * y / 7) % 256), (byte)((y * 3) % 256), (byte)255));

    /// <summary>Von Steam erzeugte Kapsel: oben und unten je Spalte konstant (senkrecht verschmiert), waagerecht bunt.</summary>
    public static BitmapSource SmearedCapsule(int width = 300, int height = 450) => Image(width, height, (x, y) =>
    {
        var s = (byte)(128 + (90 * Math.Sin(2 * Math.PI * x / width)));
        return y < height * 0.11 || y > height * 0.66
            ? (s, (byte)(s / 2), (byte)(255 - s), (byte)255)
            : ((byte)((x * 7 + y * 13) % 256), (byte)((x * y / 7) % 256), (byte)((y * 3) % 256), (byte)255);
    });

    /// <summary>Quadratisches Symbol mit durchsichtigen Ecken.</summary>
    public static BitmapSource Icon(int size = 256) => Image(size, size, (x, y) =>
    {
        var inside = Math.Pow(x - (size / 2.0), 2) + Math.Pow(y - (size / 2.0), 2) < Math.Pow(size * 0.45, 2);
        return inside ? ((byte)(30 + (x * 200 / size)), (byte)90, (byte)(220 - (y * 150 / size)), (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
    });

    /// <summary>Deckendes Bild (Xbox-Kachelbild) oder graues Splash.</summary>
    public static BitmapSource Solid(int width, int height, bool colorful) => Image(width, height, (x, y) => colorful
        ? ((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)120, (byte)255)
        : ((byte)((x + y) % 200), (byte)((x + y) % 200), (byte)((x + y) % 200), (byte)255));

    public static BitmapSource Image(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var o = ((y * width) + x) * 4;
                pixels[o] = b;
                pixels[o + 1] = g;
                pixels[o + 2] = r;
                pixels[o + 3] = a;
            }
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>Mittlere Helligkeit (0–255) eines Bildes.</summary>
    public static double Brightness(ImageSource image)
    {
        var bgra = ArtworkImaging.ToBgra((BitmapSource)image);
        double sum = 0;
        for (var i = 0; i < bgra.Pixels.Length; i += 4)
        {
            sum += (0.2126 * bgra.Pixels[i + 2]) + (0.7152 * bgra.Pixels[i + 1]) + (0.0722 * bgra.Pixels[i]);
        }

        return sum / (bgra.Pixels.Length / 4);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
