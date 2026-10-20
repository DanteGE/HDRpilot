using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Bild als BGRA-Puffer (8 Bit je Kanal, Zeilen ohne Lücken) für die Rechnungen in <see cref="PixelAnalysis"/>.</summary>
public sealed record BgraImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>Eingefrorenes Bild aus den Pixeln.</summary>
    public BitmapSource ToBitmap()
    {
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>
/// Bilddatei zu groß (über 32 MB, über 100 Megapixel oder verkleinert noch über 36 Megapixel): eigene Meldung „zu groß“.
/// Zählt wie jeder Lesefehler als „Bild nicht lesbar“ (<see cref="ArtworkImaging.IsDecodeFailure"/>).
/// </summary>
public sealed class ImageTooLargeException(string message) : IOException(message);

/// <summary>
/// Dekodieren und Kodieren der Spielbilder mit den Bilddecodern von Windows (WPF/WIC, E89). Gelesen wird immer geteilt (der
/// Launcher darf gleichzeitig schreiben), nie verändert. Bilder werden höchstens in der angefragten Breite dekodiert (nie
/// hochskaliert) und eingefroren. Die Größe steht vor dem Dekodieren fest (Kopf der Datei): Dateien über 32 MB oder
/// 100 Megapixel und Bilder, die auch verkleinert noch über 36 Megapixel hätten, gelten als zu groß
/// (<see cref="ImageTooLargeException"/>). Alle Methoden laufen auf beliebigen Threads; Fehler kommen als Ausnahme (siehe
/// <see cref="IsDecodeFailure"/>).
/// </summary>
public static class ArtworkImaging
{
    public const long MaxFileBytes = 32L * 1024 * 1024;

    /// <summary>Höchstens so viele Pixel hat ein dekodiertes Bild (Speicher).</summary>
    public const long MaxPixels = 6000L * 6000;

    /// <summary>Höchstens so viele Pixel darf eine Bilddatei haben; dekodiert wird sie verkleinert (Handyfotos mit 48 MP gehen).</summary>
    public const long MaxSourcePixels = 100_000_000;

    /// <summary>Breite und Höhe einer Bilddatei (Kopf, sonst der Decoder ohne Pixel); .ico und .exe: größtes Symbol.</summary>
    public static (int Width, int Height) Measure(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var size = Math.Max(0, IconFileSizes.LargestInExe(path));
            return (size, size);
        }

        if (ImageHeader.TryGetSize(path, out var width, out var height))
        {
            return (width, height);
        }

        using var stream = OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>
    /// Bild in höchstens <paramref name="maxWidth"/> Pixeln Breite (Seitenverhältnis bleibt, nie größer als das Original),
    /// eingefroren. .ico und .exe über Windows (größtes passendes Symbol, höchstens 256 px).
    /// </summary>
    public static BitmapSource Decode(string path, int maxWidth)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var (native, _) = Measure(path);
            var size = Math.Clamp(Math.Min(maxWidth, native > 0 ? native : 256), 16, 256);
            return GameIconService.ExtractIcon(path, size) as BitmapSource
                ?? throw new InvalidDataException("Kein Symbol in " + Path.GetFileName(path));
        }

        using var stream = OpenRead(path);
        var (width, _) = FrameSize(stream, maxWidth);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        if (maxWidth > 0 && maxWidth < width)
        {
            image.DecodePixelWidth = maxWidth;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Bild auf genau <paramref name="width"/> × <paramref name="height"/> gestreckt als BGRA (wie das Zeichnen auf eine kleine
    /// Leinwand im Prototyp): für Umgebungsfarben (48 px breit) und die Kapsel-Prüfung (60×90). Die Verkleinerung macht der
    /// Decoder (bei JPEG schon beim Dekodieren).
    /// </summary>
    public static BgraImage DecodeStretched(string path, int width, int height)
    {
        var extension = Path.GetExtension(path);
        BitmapSource source;
        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            source = Decode(path, Math.Max(width, height));
            source = Scale(source, width, height);
        }
        else
        {
            using var stream = OpenRead(path);
            _ = FrameSize(stream, width);
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.DecodePixelWidth = width;
            image.DecodePixelHeight = height;
            image.EndInit();
            image.Freeze();
            source = image;
            if (source.PixelWidth != width || source.PixelHeight != height)
            {
                source = Scale(source, width, height);
            }
        }

        return ToBgra(source);
    }

    /// <summary>Pixel eines Bildes als BGRA.</summary>
    public static BgraImage ToBgra(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return new BgraImage(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    /// <summary>Gestreckt auf genau diese Größe (flächengemittelt bzw. bilinear, <see cref="PixelAnalysis.Resample"/>), eingefroren.</summary>
    public static BitmapSource Scale(BitmapSource source, int width, int height)
    {
        var pixels = ToBgra(source);
        return new BgraImage(width, height, PixelAnalysis.Resample(pixels.Pixels, pixels.Width, pixels.Height, 0, 0, pixels.Width, pixels.Height, width, height)).ToBitmap();
    }

    /// <summary>Ausschnitt in Pixeln, eingefroren.</summary>
    public static BitmapSource Crop(BitmapSource source, PixelRect rect)
    {
        var cropped = new CroppedBitmap(source, new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>
    /// Füllt <paramref name="width"/> × <paramref name="height"/> wie CSS object-fit: cover (Ausschnitt um den Punkt
    /// <paramref name="focusX"/>/<paramref name="focusY"/> als Anteil 0–1), eingefroren.
    /// </summary>
    public static BitmapSource CoverFill(BitmapSource source, int width, int height, double focusX = 0.5, double focusY = 0.5)
    {
        var pixels = ToBgra(source);
        var scale = Math.Max(width / (double)pixels.Width, height / (double)pixels.Height);
        double sw = width / scale, sh = height / scale;
        var sx = (pixels.Width - sw) * Math.Clamp(focusX, 0, 1);
        var sy = (pixels.Height - sh) * Math.Clamp(focusY, 0, 1);
        return new BgraImage(width, height, PixelAnalysis.Resample(pixels.Pixels, pixels.Width, pixels.Height, sx, sy, sw, sh, width, height)).ToBitmap();
    }

    /// <summary>Kodiert als JPEG (Qualität 0–100) oder PNG in eine Datei, atomar über eine Temp-Datei im selben Ordner.</summary>
    public static void Save(BitmapSource image, string path, int jpegQuality = 90)
    {
        BitmapEncoder encoder = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
        var frame = encoder is JpegBitmapEncoder && image.Format != PixelFormats.Bgr24
            ? new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)
            : (BitmapSource)image;
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        WriteAtomic(path, buffer.ToArray());
    }

    /// <summary>Schreibt Bytes atomar (Temp-Datei im selben Ordner, dann ersetzen).</summary>
    public static void WriteAtomic(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new ArgumentException("Pfad ohne Ordner: " + path, nameof(path));
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Prüft Bytes (etwa einen Download) als Bild, ohne sie ganz zu dekodieren: Größe aus dem Kopf (höchstens
    /// <paramref name="maxPixels"/>), dann ein kleines Vorschaubild, damit auch die Pixeldaten lesbar sind.
    /// </summary>
    public static (int Width, int Height) Validate(byte[] data, long maxPixels)
    {
        if (data.LongLength > MaxFileBytes)
        {
            throw new ImageTooLargeException("Bild ist zu groß: " + data.LongLength + " Byte.");
        }

        using var stream = new MemoryStream(data, writable: false);
        var (width, height) = FrameSize(stream, 64);
        if ((long)width * height > maxPixels)
        {
            throw new ImageTooLargeException("Bild hat zu viele Pixel: " + width + " × " + height + ".");
        }

        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        image.DecodePixelWidth = Math.Min(64, width);
        image.EndInit();
        return (width, height);
    }

    /// <summary>
    /// Fehler, die „Bild nicht lesbar“ bedeuten (fehlender Decoder, kaputte oder zu große Datei, gesperrt, für den Decoder zu
    /// wenig Speicher).
    /// </summary>
    public static bool IsDecodeFailure(Exception ex) =>
        IconReader.IsDecodeFailure(ex) || ex is System.Runtime.InteropServices.COMException or FileFormatException or OutOfMemoryException;

    /// <summary>
    /// Bilddatei lesen: geteilt geöffnet (Lesen, Schreiben, Löschen) und nur so lange, bis der Inhalt im Speicher liegt; die
    /// Datei ist wieder zu, bevor dekodiert wird. Ein offener Leser lässt nämlich das Ersetzen der Datei durch Verschieben
    /// mit „Zugriff verweigert“ scheitern (so ersetzt etwa Quiver seine Bilder und Verknüpfungen, E91). Dateien über
    /// <see cref="MaxFileBytes"/> werden abgelehnt.
    /// </summary>
    public static Stream OpenRead(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (file.Length > MaxFileBytes)
        {
            throw new ImageTooLargeException("Bild ist zu groß: " + Path.GetFileName(path));
        }

        var buffer = new byte[file.Length];
        var read = file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
    }

    /// <summary>
    /// Größe des ersten Bildes aus dem Kopf, ohne Pixel zu dekodieren. Abgelehnt werden Dateien über
    /// <see cref="MaxSourcePixels"/> und Bilder, die in höchstens <paramref name="maxWidth"/> Breite (0: ganz) noch über
    /// <see cref="MaxPixels"/> hätten.
    /// </summary>
    private static (int Width, int Height) FrameSize(Stream stream, int maxWidth)
    {
        // Zuerst der Kopf der Datei ohne Decoder (PNG, JPEG, BMP, GIF, WebP): Riesige Bilder fallen heraus, bevor WIC sie
        // anfasst; erst dann die genaue Größe über den Decoder (ohne Pixel).
        var probe = new byte[(int)Math.Min(ImageHeader.ProbeLength, stream.Length - stream.Position)];
        stream.ReadExactly(probe);
        stream.Position = 0;
        if (ImageHeader.TryGetSize(probe, out var headerWidth, out var headerHeight))
        {
            CheckSize(headerWidth, headerHeight, maxWidth);
        }

        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Bild ohne Pixel.");
        }

        CheckSize(width, height, maxWidth);
        return (width, height);
    }

    private static void CheckSize(int width, int height, int maxWidth)
    {
        if ((long)width * height > MaxSourcePixels)
        {
            throw new ImageTooLargeException("Bild hat zu viele Pixel: " + width + " × " + height + ".");
        }

        var decodedWidth = maxWidth > 0 && maxWidth < width ? maxWidth : width;
        var decodedHeight = (long)Math.Ceiling(height * (decodedWidth / (double)width));
        if (decodedWidth * decodedHeight > MaxPixels)
        {
            throw new ImageTooLargeException("Bild hat auch verkleinert zu viele Pixel: " + width + " × " + height + ".");
        }
    }
}
