using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>
/// Ein gelesenes Symbol (E86): Bild in Zielgröße (eingefroren), größte Kantenlänge in der Datei (0: unbekannt) und ob es
/// ein bekanntes Standardsymbol ist.
/// </summary>
public sealed record IconImage(ImageSource Image, int NativeSize, bool IsGeneric)
{
    /// <summary>Ab dieser Größe in der Datei gilt ein Symbol als scharf; kleinere werden für 48 px hochgerechnet.</summary>
    public const int SharpSize = 48;

    /// <summary>Gut: kein Standardsymbol und in der Datei mindestens <see cref="SharpSize"/> px groß.</summary>
    public bool IsGood => !IsGeneric && NativeSize >= SharpSize;
}

/// <summary>
/// Liest Symbole aus Dateien. EXE und .ico über Windows (<see cref="GameIconService.ExtractIcon"/>, dieselbe Größe wie
/// bei der Messung der Fingerabdrücke), Bilder (.png, .jpg, .webp …) über die Bilddecoder von Windows (WPF/WIC) mit
/// Zielbreite, eingepasst in ein Quadrat. Die echte Größe kommt aus dem Verzeichnis der .ico bzw. der ersten
/// Symbolgruppe der EXE (die, die Windows für Index 0 nimmt). Fehlende Decoder (WebP, HEIF auf anderen PCs) und
/// unlesbare Dateien gelten als „kein Symbol“ und landen nur im Debug-Protokoll.
/// </summary>
public sealed class IconReader(GenericIconDetector? detector = null, ILogger? logger = null)
{
    /// <summary>Größere Bilddateien werden nicht dekodiert (Speicher).</summary>
    public const long MaxImageBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Bilder mit mehr Pixeln werden nicht dekodiert: PNG, WebP und BMP dekodiert Windows erst ganz und verkleinert dann
    /// (36 Megapixel sind kurzzeitig etwa 144 MB).
    /// </summary>
    public const long MaxImagePixels = 6000L * 6000;

    private readonly GenericIconDetector _detector = detector ?? GenericIconDetector.Default;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Symbol in <paramref name="size"/> px oder null, wenn die Datei keines hergibt. Wirft nicht.</summary>
    public IconImage? Read(string path, int size = GameIconService.IconSize)
    {
        try
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                if (GameIconService.ExtractIcon(path, size) is not BitmapSource icon)
                {
                    return null;
                }

                return new IconImage(icon, NativeSize(path, extension), _detector.IsGeneric(icon));
            }

            if (IconFiles.IsImage(path))
            {
                var (image, nativeSize) = DecodeFitted(path, size);
                return new IconImage(image, nativeSize, _detector.IsGeneric(image));
            }
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            _logger.LogDebug("Symbol aus {Path} nicht lesbar: {Error}: {Message}", path, ex.GetType().Name, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Bild mit den Decodern von Windows lesen und in ein Quadrat mit <paramref name="size"/> px einpassen (Seitenverhältnis
    /// bleibt, Rest durchsichtig). Liefert auch die größte Kantenlänge der Datei. Wirft bei fehlendem Decoder oder
    /// kaputter Datei (NotSupportedException, FileFormatException …).
    /// </summary>
    public static (BitmapSource Image, int NativeSize) DecodeFitted(string path, int size)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxImageBytes)
        {
            throw new InvalidDataException("Bild ist zu groß: " + stream.Length + " Byte.");
        }

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Bild ohne Pixel.");
        }

        if ((long)width * height > MaxImagePixels)
        {
            throw new InvalidDataException("Bild hat zu viele Pixel: " + width + " × " + height + ".");
        }

        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (width >= height)
        {
            bitmap.DecodePixelWidth = size;
        }
        else
        {
            bitmap.DecodePixelHeight = size;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        return (Square(bitmap, size), Math.Max(width, height));
    }

    /// <summary>Mittig auf ein durchsichtiges Quadrat setzen (BGRA, eingefroren).</summary>
    internal static BitmapSource Square(BitmapSource image, int size)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = Math.Min(size, converted.PixelWidth), height = Math.Min(size, converted.PixelHeight);
        var source = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(source, converted.PixelWidth * 4, 0);
        var target = new byte[size * size * 4];
        int left = (size - width) / 2, top = (size - height) / 2;
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(source, y * converted.PixelWidth * 4, target, (((top + y) * size) + left) * 4, width * 4);
        }

        var result = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, target, size * 4);
        result.Freeze();
        return result;
    }

    /// <summary>Echte Größe des Symbols in einer .ico oder EXE; 0, wenn sie sich nicht lesen lässt (gilt dann als unscharf).</summary>
    private int NativeSize(string path, string extension)
    {
        try
        {
            var size = extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ? IconFileSizes.LargestInIco(path) : IconFileSizes.LargestInExe(path);
            return Math.Max(0, size);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            _logger.LogDebug("Symbolgröße in {Path} nicht lesbar: {Message}", path, ex.Message);
            return 0;
        }
    }

    internal static bool IsDecodeFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException
            or InvalidOperationException or ExternalException or InvalidDataException or OverflowException or System.Security.SecurityException;
}

/// <summary>
/// Echte Größe von Symbolen in Dateien, ohne sie zu laden: Verzeichnis einer .ico und erste Symbolgruppe (RT_GROUP_ICON)
/// einer EXE, gelesen mit wenigen Dateizugriffen statt der ganzen (oft Hunderte MB großen) Datei. Breite 0 heißt 256.
/// </summary>
public static class IconFileSizes
{
    private const int GroupIcon = 14;
    private const int MaxEntries = 256;

    /// <summary>Größte Kantenlänge in einer .ico; -1, wenn unlesbar oder keine .ico.</summary>
    public static int LargestInIco(string path)
    {
        using var stream = OpenRead(path);
        var header = new byte[6 + (16 * MaxEntries)];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return LargestInIco(header.AsSpan(0, read));
    }

    /// <summary>Größte Kantenlänge laut ICONDIR (Kopf 6 Byte, Einträge je 16 Byte); -1 bei ungültigem Kopf.</summary>
    public static int LargestInIco(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 1)
        {
            return -1;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var largest = -1;
        for (var i = 0; i < count && i < MaxEntries && 6 + (i * 16) + 16 <= data.Length; i++)
        {
            int width = data[6 + (i * 16)], height = data[6 + (i * 16) + 1];
            largest = Math.Max(largest, Math.Max(width == 0 ? 256 : width, height == 0 ? 256 : height));
        }

        return largest;
    }

    /// <summary>Größte Kantenlänge der ersten Symbolgruppe einer EXE/DLL; -1, wenn unlesbar oder ohne Symbol.</summary>
    public static int LargestInExe(string path)
    {
        using var stream = OpenRead(path);
        return LargestInPe(stream);
    }

    /// <summary>
    /// Liest aus einem PE-Abbild (32 oder 64 Bit) die erste RT_GROUP_ICON in Verzeichnisreihenfolge (benannte vor
    /// nummerierten Einträgen, wie EnumResourceNames sie liefert und ExtractIcon für Index 0 nimmt), davon die erste
    /// Sprache, und darin den größten Eintrag (GRPICONDIR, Einträge je 14 Byte). -1, wenn es keine gibt.
    /// </summary>
    public static int LargestInPe(Stream stream)
    {
        var dos = ReadAt(stream, 0, 64);
        if (dos is null || dos[0] != 'M' || dos[1] != 'Z')
        {
            return -1;
        }

        var pe = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(0x3C));
        var coff = pe > 0 ? ReadAt(stream, pe, 24) : null;
        if (coff is null || coff[0] != 'P' || coff[1] != 'E' || coff[2] != 0 || coff[3] != 0)
        {
            return -1;
        }

        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(coff.AsSpan(6)), optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff.AsSpan(20));
        var optional = ReadAt(stream, pe + 24, optionalSize);
        if (optional is null || optionalSize < 2 || sectionCount is 0 or > 96)
        {
            return -1;
        }

        var directories = BinaryPrimitives.ReadUInt16LittleEndian(optional) switch
        {
            0x10B => 96, // PE32
            0x20B => 112, // PE32+
            _ => -1,
        };
        if (directories < 0 || directories + 24 > optionalSize || BinaryPrimitives.ReadUInt32LittleEndian(optional.AsSpan(directories - 4)) < 3)
        {
            return -1;
        }

        var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(optional.AsSpan(directories + 16));
        var sections = ReadAt(stream, pe + 24 + optionalSize, sectionCount * 40);
        if (resourceRva == 0 || sections is null)
        {
            return -1;
        }

        // Wie der Loader von Windows: Eine Sektion umfasst VirtualSize Byte, nur ohne VirtualSize (manche Linker) SizeOfRawData.
        // Nie die größere von beiden: Godot 4 packt die Spieldaten in eine Sektion „pck“ mit VirtualSize 8, deren SizeOfRawData
        // weit über .rsrc hinausreicht (gemessen: Buckshot Roulette, Overlooting, s.p.l.i.t, Spin Hero).
        long ToOffset(uint rva)
        {
            for (var i = 0; i < sectionCount; i++)
            {
                var s = sections.AsSpan(i * 40);
                uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]), address = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
                uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(s[16..]), raw = BinaryPrimitives.ReadUInt32LittleEndian(s[20..]);
                if (rva >= address && rva < (long)address + (virtualSize != 0 ? virtualSize : rawSize))
                {
                    return raw + (long)(rva - address);
                }
            }

            return -1;
        }

        var root = ToOffset(resourceRva);
        if (root < 0)
        {
            return -1;
        }

        // Typ RT_GROUP_ICON → erster Name → erste Sprache → Datenverweis.
        var names = Subdirectory(stream, root, root, GroupIcon);
        var languages = names < 0 ? -1 : Subdirectory(stream, root, names, null);
        var dataEntry = languages < 0 ? -1 : FirstEntryData(stream, root, languages);
        var entry = dataEntry < 0 ? null : ReadAt(stream, dataEntry, 8);
        if (entry is null)
        {
            return -1;
        }

        var groupOffset = ToOffset(BinaryPrimitives.ReadUInt32LittleEndian(entry));
        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(4)), 6 + (14 * MaxEntries));
        var group = groupOffset < 0 || length < 6 ? null : ReadAt(stream, groupOffset, length);
        if (group is null || BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(2)) != 1)
        {
            return -1;
        }

        var largest = -1;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
        for (var i = 0; i < count && 6 + (i * 14) + 14 <= group.Length; i++)
        {
            int width = group[6 + (i * 14)], height = group[6 + (i * 14) + 1];
            largest = Math.Max(largest, Math.Max(width == 0 ? 256 : width, height == 0 ? 256 : height));
        }

        return largest;
    }

    /// <summary>
    /// Unterverzeichnis eines Eintrags im Ressourcenverzeichnis an <paramref name="directory"/>: mit
    /// <paramref name="id"/> der nummerierte Eintrag, sonst der erste (benannte zuerst). -1, wenn es keins gibt.
    /// </summary>
    private static long Subdirectory(Stream stream, long root, long directory, int? id)
    {
        var header = ReadAt(stream, directory, 16);
        if (header is null)
        {
            return -1;
        }

        var count = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14)), MaxEntries * 16);
        var entries = count == 0 ? null : ReadAt(stream, directory + 16, count * 8);
        for (var i = 0; entries is not null && i < count; i++)
        {
            var name = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 8));
            var target = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan((i * 8) + 4));
            if (id is null || ((name & 0x8000_0000) == 0 && name == id))
            {
                return (target & 0x8000_0000) != 0 ? root + (target & 0x7FFF_FFFF) : -1;
            }
        }

        return -1;
    }

    /// <summary>Datenverweis (IMAGE_RESOURCE_DATA_ENTRY) des ersten Eintrags eines Sprachverzeichnisses; -1 sonst.</summary>
    private static long FirstEntryData(Stream stream, long root, long directory)
    {
        var header = ReadAt(stream, directory, 24);
        if (header is null || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14)) == 0)
        {
            return -1;
        }

        var target = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
        return (target & 0x8000_0000) != 0 ? -1 : root + target;
    }

    private static byte[]? ReadAt(Stream stream, long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > stream.Length)
        {
            return null;
        }

        stream.Position = offset;
        var buffer = new byte[length];
        return stream.ReadAtLeast(buffer, length, throwOnEndOfStream: false) == length ? buffer : null;
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
}
