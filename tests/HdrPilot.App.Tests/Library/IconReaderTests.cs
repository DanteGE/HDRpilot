using System.Text;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Symbole aus Dateien lesen (E86): echte Größe aus dem Verzeichnis der .ico bzw. der ersten Symbolgruppe der EXE, Bilder über
/// die Windows-Decoder, Fehler (kaputte Datei, fehlender Decoder, riesiges Bild) als „kein Symbol“.
/// </summary>
public sealed class IconReaderTests : IDisposable
{
    private readonly IconFixtures _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Icon_quality_rule_needs_48_px_and_no_generic_icon()
    {
        var image = IconFixtures.Colorful(48, 48);

        Assert.True(new IconImage(image, 48, IsGeneric: false).IsGood);
        Assert.True(new IconImage(image, 256, IsGeneric: false).IsGood);
        Assert.False(new IconImage(image, 47, IsGeneric: false).IsGood);
        Assert.False(new IconImage(image, 32, IsGeneric: false).IsGood);
        Assert.False(new IconImage(image, 0, IsGeneric: false).IsGood); // Größe unbekannt
        Assert.False(new IconImage(image, 256, IsGeneric: true).IsGood);
    }

    [Fact]
    public void Icon_size_is_read_from_the_ico_directory()
    {
        var sharp = _files.Ico("scharf.ico", IconFixtures.Colorful(16, 16), IconFixtures.Colorful(32, 32), IconFixtures.Colorful(256, 256));
        var small = _files.Ico("klein.ico", IconFixtures.Colorful(16, 16), IconFixtures.Colorful(32, 32));

        Assert.Equal(256, IconFileSizes.LargestInIco(sharp));
        Assert.Equal(32, IconFileSizes.LargestInIco(small));
        Assert.Equal(-1, IconFileSizes.LargestInIco(new byte[] { 0, 0, 2, 0, 1, 0 })); // Cursor, keine .ico
        Assert.Equal(-1, IconFileSizes.LargestInIco(new byte[] { 0, 0 }));
    }

    [Fact]
    public void Icon_size_is_read_from_the_first_exe_icon_group()
    {
        using var single = new MemoryStream(IconFixtures.PeWithIconGroups((null, [16, 32, 48, 256])));
        using var small = new MemoryStream(IconFixtures.PeWithIconGroups((null, [16, 32])));

        // Windows nimmt für Index 0 die erste Gruppe in Verzeichnisreihenfolge: benannte vor nummerierten.
        using var named = new MemoryStream(IconFixtures.PeWithIconGroups((null, [256]), ("MAINICON", [16, 32])));

        Assert.Equal(256, IconFileSizes.LargestInPe(single));
        Assert.Equal(32, IconFileSizes.LargestInPe(small));
        Assert.Equal(32, IconFileSizes.LargestInPe(named));
        Assert.Equal(-1, IconFileSizes.LargestInPe(new MemoryStream(Encoding.ASCII.GetBytes("MZ das ist keine EXE"))));
        Assert.Equal(-1, IconFileSizes.LargestInPe(new MemoryStream(IconFixtures.PeWithIconGroups())));
    }

    [Fact]
    public void Icon_size_of_godot_exes_with_packed_data_counts_sections_by_virtual_size()
    {
        // Godot 4 legt die Spieldaten in eine Sektion „pck“ (VirtualSize 8) vor .rsrc, ihre Rohdaten reichen über .rsrc hinaus.
        using var godot = new MemoryStream(IconFixtures.PeWithIconGroups(PeLayout.PackedDataBefore, (null, [16, 32, 256])));

        // Ohne VirtualSize gilt SizeOfRawData.
        using var noVirtualSize = new MemoryStream(IconFixtures.PeWithIconGroups(PeLayout.NoVirtualSize, (null, [16, 32, 256])));

        Assert.Equal(256, IconFileSizes.LargestInPe(godot));
        Assert.Equal(256, IconFileSizes.LargestInPe(noVirtualSize));
    }

    [Fact]
    public void Icon_size_of_a_truncated_exe_is_unknown()
    {
        var data = IconFixtures.PeWithIconGroups((null, [256]));

        Assert.Equal(-1, IconFileSizes.LargestInPe(new MemoryStream(data[..(data.Length - 20)])));
        Assert.Equal(-1, IconFileSizes.LargestInPe(new MemoryStream(data[..300])));
    }

    [Fact]
    public void Icon_reader_reads_ico_png_and_jpg_with_their_native_size()
    {
        var reader = new IconReader();

        var ico = reader.Read(_files.Ico("a.ico", IconFixtures.Colorful(32, 32), IconFixtures.Colorful(256, 256)));
        var smallIco = reader.Read(_files.Ico("b.ico", IconFixtures.Colorful(32, 32)));
        var png = reader.Read(_files.Png("c.png", IconFixtures.Colorful(300, 150)));
        var jpg = reader.Read(_files.Jpg("d.jpg", IconFixtures.Colorful(32, 32)));

        Assert.NotNull(ico);
        Assert.Equal(256, ico.NativeSize);
        Assert.True(ico.IsGood);
        Assert.Equal(32, smallIco!.NativeSize);
        Assert.False(smallIco.IsGood);
        Assert.Equal(300, png!.NativeSize);
        Assert.Equal(32, jpg!.NativeSize); // wie das librarycache-Bild von Steam: 32 px, gilt als unscharf
        foreach (var icon in new[] { ico, smallIco, png, jpg })
        {
            var image = Assert.IsAssignableFrom<BitmapSource>(icon.Image);
            Assert.Equal(48, image.PixelWidth);
            Assert.Equal(48, image.PixelHeight);
            Assert.True(image.IsFrozen);
            Assert.False(icon.IsGeneric);
        }
    }

    [Fact]
    public void Icon_reader_fits_wide_images_into_a_transparent_square()
    {
        var (image, native) = IconReader.DecodeFitted(_files.Png("breit.png", IconFixtures.Colorful(200, 100)), 48);
        var pixels = IconFingerprint.NormalizedPixels(image)!;

        Assert.Equal(200, native);
        Assert.Equal(0, pixels[3]); // oben links durchsichtig (Rand)
        Assert.Equal(255, pixels[((24 * 48) + 24) * 4 + 3]); // Mitte deckend
    }

    [Fact]
    public void Icon_reader_reads_real_exe_icons()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        var icon = new IconReader().Read(explorer);

        Assert.NotNull(icon);
        Assert.True(icon.NativeSize >= 48, "explorer.exe hat Symbole ab 48 px: " + icon.NativeSize);
        Assert.Equal(IconFileSizes.LargestInExe(explorer), icon.NativeSize);
        Assert.True(icon.IsGood);
    }

    [Theory]
    [InlineData("kaputt.png")]
    [InlineData("kaputt.webp")]
    [InlineData("kaputt.jpg")]
    [InlineData("kaputt.ico")]
    [InlineData("kaputt.exe")]
    public void Icon_reader_turns_broken_files_and_missing_codecs_into_no_icon(string name)
    {
        var path = _files.Bytes(name, Encoding.ASCII.GetBytes("Das ist kein Bild und kein Symbol."));

        Assert.Null(new IconReader().Read(path));
        Assert.Null(new IconReader().Read(_files.PathOf("fehlt-" + name)));
    }

    [Fact]
    public void Icon_reader_refuses_images_with_too_many_pixels_before_decoding()
    {
        // Gültige PNG mit 7000 × 7000 Pixeln (49 MP, 1 Bit grau, wenige KB): abgelehnt, bevor Windows Speicher dafür anlegt.
        var path = _files.Bytes("riesig.png", BlackPng(7000, 7000));

        Assert.Null(new IconReader().Read(path));
        Assert.Throws<InvalidDataException>(() => IconReader.DecodeFitted(path, 48));
        Assert.NotNull(IconReader.DecodeFitted(_files.Bytes("normal.png", BlackPng(600, 400)), 48).Image);
    }

    /// <summary>Schwarze PNG (1 Bit grau) mit gültigen Prüfsummen; die Bilddaten sind gepackt nur wenige KB groß.</summary>
    private static byte[] BlackPng(int width, int height)
    {
        var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // Bittiefe
        header[9] = 0; // grau
        Chunk(output, "IHDR", header);

        var packed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + ((width + 7) / 8)]; // Filterbyte 0, dann Nullen
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        Chunk(output, "IDAT", packed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(typed);
        var crc = Crc32(typed);
        output.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB8_8320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
