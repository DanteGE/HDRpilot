using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kleine Symbol- und Bilddateien für Tests (E86), erzeugt in einem eigenen Temp-Ordner und beim Dispose gelöscht. Nie
/// Dateien aus Launcher-Zwischenspeichern oder Spielordnern.
/// </summary>
public sealed class IconFixtures : IDisposable
{
    public IconFixtures()
    {
        Directory = Path.Combine(Path.GetTempPath(), "hdrpilot-icon-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string PathOf(string name) => Path.Combine(Directory, name);

    /// <summary>Bild aus BGRA-Pixeln (eingefroren).</summary>
    public static BitmapSource Image(int width, int height, byte[] pixels)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>Buntes, deckendes Testbild mit Verlauf (kein Standardsymbol).</summary>
    public static BitmapSource Colorful(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)((x + y) % 2 == 0 ? 220 : 40);
                pixels[i + 3] = 255;
            }
        }

        return Image(width, height, pixels);
    }

    public string Png(string name, BitmapSource image)
    {
        var path = PathOf(name);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return path;
    }

    public string Jpg(string name, BitmapSource image)
    {
        var path = PathOf(name);
        using var stream = File.Create(path);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)));
        encoder.Save(stream);
        return path;
    }

    /// <summary>.ico mit PNG-Bildern (wie Steam und GOG sie für große Größen ablegen); Breite 256 steht als 0 im Verzeichnis.</summary>
    public string Ico(string name, params BitmapSource[] frames)
    {
        var images = frames.Select(f =>
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(f));
            encoder.Save(ms);
            return (Frame: f, Data: ms.ToArray());
        }).ToList();

        var path = PathOf(name);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        foreach (var (frame, data) in images)
        {
            writer.Write((byte)(frame.PixelWidth >= 256 ? 0 : frame.PixelWidth));
            writer.Write((byte)(frame.PixelHeight >= 256 ? 0 : frame.PixelHeight));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            writer.Write(data);
        }

        return path;
    }

    public string Bytes(string name, byte[] data)
    {
        var path = PathOf(name);
        File.WriteAllBytes(path, data);
        return path;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp-Ordner: bleibt notfalls liegen.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Kleines PE-Abbild (PE32+) nur mit Ressourcen: je Gruppe ein RT_GROUP_ICON mit den angegebenen Kantenlängen. Benannte
    /// Gruppen stehen wie bei Windows vor nummerierten. Reicht für <see cref="IconFileSizes.LargestInPe"/>, lädt nicht.
    /// </summary>
    public static byte[] PeWithIconGroups(params (string? Name, int[] Sizes)[] groups) => PeWithIconGroups(PeLayout.Plain, groups);

    /// <summary>Wie oben, mit dem Aufbau der Sektionen aus <paramref name="layout"/>.</summary>
    public static byte[] PeWithIconGroups(PeLayout layout, params (string? Name, int[] Sizes)[] groups)
    {
        var packed = layout == PeLayout.PackedDataBefore;
        var sectionRva = packed ? 0x2000u : 0x1000u;
        var sectionOffset = packed ? 0x400 : 0x200;
        var named = groups.Where(g => g.Name is not null).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
        var numbered = groups.Where(g => g.Name is null).ToList();
        var ordered = named.Concat(numbered).ToList();

        // Aufbau der Ressourcen: Wurzel (1 Typ) → Typ 14 (Namen) → je Name ein Sprachverzeichnis (1 Eintrag) → Datenverweise → Daten
        var rsrc = new List<byte>();
        void U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); rsrc.AddRange(b); }
        void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); rsrc.AddRange(b); }
        void Dir(int namedCount, int idCount) { U32(0); U32(0); U16(0); U16(0); U16(namedCount); U16(idCount); }

        var rootSize = 16 + 8;
        var typeSize = 16 + (8 * ordered.Count);
        var langSize = 16 + 8;
        var langStart = rootSize + typeSize;
        var dataEntryStart = langStart + (langSize * ordered.Count);
        var stringsStart = dataEntryStart + (16 * ordered.Count);
        var strings = new List<byte>();
        var nameOffsets = new List<int>();
        foreach (var g in named)
        {
            nameOffsets.Add(stringsStart + strings.Count);
            strings.AddRange(BitConverter.GetBytes((ushort)g.Name!.Length));
            strings.AddRange(System.Text.Encoding.Unicode.GetBytes(g.Name));
        }

        var groupStart = stringsStart + strings.Count + ((4 - (strings.Count % 4)) % 4);
        var groupData = ordered.Select(g =>
        {
            var d = new List<byte>();
            d.AddRange(BitConverter.GetBytes((ushort)0));
            d.AddRange(BitConverter.GetBytes((ushort)1));
            d.AddRange(BitConverter.GetBytes((ushort)g.Sizes.Length));
            foreach (var s in g.Sizes)
            {
                d.Add((byte)(s >= 256 ? 0 : s));
                d.Add((byte)(s >= 256 ? 0 : s));
                d.AddRange(new byte[] { 0, 0, 1, 0, 32, 0 });
                d.AddRange(BitConverter.GetBytes(1000));
                d.AddRange(BitConverter.GetBytes((ushort)1));
            }

            return d.ToArray();
        }).ToList();

        Dir(0, 1);
        U32(14);
        U32(0x8000_0000 | (uint)rootSize);
        Dir(named.Count, numbered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            U32(i < named.Count ? 0x8000_0000 | (uint)nameOffsets[i] : (uint)(i + 1));
            U32(0x8000_0000 | (uint)(langStart + (langSize * i)));
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            Dir(0, 1);
            U32(1031);
            U32((uint)(dataEntryStart + (16 * i)));
        }

        var dataOffset = groupStart;
        foreach (var data in groupData)
        {
            U32(sectionRva + (uint)dataOffset);
            U32((uint)data.Length);
            U32(0);
            U32(0);
            dataOffset += data.Length;
        }

        rsrc.AddRange(strings);
        while (rsrc.Count < groupStart)
        {
            rsrc.Add(0);
        }

        foreach (var data in groupData)
        {
            rsrc.AddRange(data);
        }

        // Kopf: DOS (0x40), "PE\0\0", COFF (20), Optional Header PE32+ (240), eine oder zwei Sektionen (je 40)
        var image = new byte[sectionOffset + rsrc.Count];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 0x40);
        var pe = 0x40;
        "PE\0\0"u8.CopyTo(image.AsSpan(pe));
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(pe + 4), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(pe + 6), (ushort)(packed ? 2 : 1));
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(pe + 20), 240);
        var optional = pe + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 108), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 112 + 16), sectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 112 + 20), (uint)rsrc.Count);
        var section = optional + 240;
        if (packed)
        {
            // Wie „pck“ in Godot-4-EXEs: VirtualSize 8, die Rohdaten (16 MB, hinter dem Dateiende) reichen über .rsrc hinaus.
            "pck"u8.CopyTo(image.AsSpan(section));
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 8), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 12), 0x1000);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 16), 0x0100_0000);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 20), 0x200);
            section += 40;
        }

        ".rsrc"u8.CopyTo(image.AsSpan(section));
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 8), layout == PeLayout.NoVirtualSize ? 0 : (uint)rsrc.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 12), sectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 16), (uint)rsrc.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 20), (uint)sectionOffset);
        rsrc.CopyTo(image, sectionOffset);
        return image;
    }
}

/// <summary>Aufbau der Sektionen im PE-Abbild von <see cref="IconFixtures.PeWithIconGroups(PeLayout, (string?, int[])[])"/>.</summary>
public enum PeLayout
{
    /// <summary>Nur .rsrc.</summary>
    Plain,

    /// <summary>Vor .rsrc eine Sektion wie „pck“ in Godot-4-EXEs: VirtualSize 8, SizeOfRawData reicht weit über .rsrc hinaus.</summary>
    PackedDataBefore,

    /// <summary>.rsrc ohne VirtualSize (manche Linker): Dann gilt SizeOfRawData.</summary>
    NoVirtualSize,
}

/// <summary>
/// 48-px-Symbole, nach den echten Fingerabdrücken der Standardsymbole gezeichnet (E86): ein Raster aus '.' (durchsichtig)
/// und '1'–'4' (Helligkeitsstufen der mittleren Farbe des Standardsymbols). Gefunden mit einer Koordinatensuche gegen
/// <see cref="IconFingerprint.DistanceTo"/>; die Bilder der echten Symbole sind nicht im Repository.
/// </summary>
public static class GenericIconArt
{
    private static readonly double[] Levels = [0.3, 0.8, 1.3, 1.8];

    /// <summary>EasyAntiCheat-Starter (Abstand 8), 12 × 12 Zellen.</summary>
    public static readonly (string Hash, (int R, int G, int B) Color, string[] Art) EasyAntiCheat = ("BFF8A9D4CC3574F3", (39, 105, 146),
    [
        "........12..",
        "...........2",
        ".1144411111.",
        "114442111111",
        "112441111111",
        "441111411111",
        "144114441111",
        "44144441111.",
        "..44444411..",
        "..24.44411..",
        ".14144411.1.",
        "......1.....",
    ]);

    /// <summary>Unreal Engine, schwarzes „U“ (Abstand 6), 16 × 16 Zellen.</summary>
    public static readonly (string Hash, (int R, int G, int B) Color, string[] Art) UnrealU = ("21704A055F2F2AF1", (97, 97, 97),
    [
        "1...44111144....",
        "....44111144....",
        "..111.11111.14..",
        "..111111111111..",
        "4411444111441144",
        "4411441111441144",
        "11114444441111.1",
        "1111444444111111",
        "11114444441111.1",
        "1111444444111111",
        "4411444.44441144",
        "4411444444441144",
        "..111.1.11111...",
        "..411111111111..",
        "2...44111144....",
        "....44111144....",
    ]);

    /// <summary>Godot 4 (Abstand 0), 16 × 16 Zellen.</summary>
    public static readonly (string Hash, (int R, int G, int B) Color, string[] Art) Godot4 = ("F9F095E9485D704F", (78, 118, 148),
    [
        "1.11111.111.1111",
        "1111111111111111",
        "11.1444.4444.111",
        "1111444444441111",
        "1144444..4.44411",
        "1144444444444411",
        "1144444..4.44411",
        "1144444444444411",
        "11444.444.444411",
        "1144444444444411",
        "1144444..4444411",
        "1144444444444411",
        "11.14.444444.111",
        "1111444444441111",
        "11111111111.1111",
        "1111111111111111",
    ]);

    /// <summary>48 × 48 BGRA-Pixel des Rasters.</summary>
    public static byte[] Render(string[] art, (int R, int G, int B) color)
    {
        var pixels = new byte[48 * 48 * 4];
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 48; x++)
            {
                var c = art[y * art.Length / 48][x * art.Length / 48];
                if (c == '.')
                {
                    continue;
                }

                var level = Levels[c - '1'];
                var i = ((y * 48) + x) * 4;
                pixels[i] = (byte)Math.Min(255, (int)(color.B * level));
                pixels[i + 1] = (byte)Math.Min(255, (int)(color.G * level));
                pixels[i + 2] = (byte)Math.Min(255, (int)(color.R * level));
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Dasselbe Raster mit vertauschten Helligkeitsstufen (gleiche Form, anderes Bild).</summary>
    public static string[] Inverted(string[] art) =>
        art.Select(row => new string(row.Select(c => c switch { '1' => '4', '2' => '3', '3' => '2', '4' => '1', _ => c }).ToArray())).ToArray();
}
