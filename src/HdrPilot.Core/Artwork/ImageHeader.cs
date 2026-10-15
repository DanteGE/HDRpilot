using System.Buffers.Binary;

namespace HdrPilot.Core.Artwork;

/// <summary>
/// Breite und Höhe eines Bildes aus dem Dateikopf, ohne die Pixel zu dekodieren (E89): PNG, JPEG, GIF, BMP, WebP (VP8, VP8L,
/// VP8X) und ICO (größtes Bild im Verzeichnis). So lassen sich Symbol-Kandidaten und Launcher-Bilder vergleichen, ohne
/// Hunderte Bilder zu laden.
/// </summary>
public static class ImageHeader
{
    /// <summary>So viele Bytes vom Anfang genügen fast immer (JPEG mit großem EXIF-Vorschaubild: siehe <see cref="TryGetSize"/>).</summary>
    public const int ProbeLength = 64 * 1024;

    /// <summary>Größe aus den ersten Bytes einer Datei; false, wenn das Format unbekannt oder der Kopf unvollständig ist.</summary>
    public static bool TryGetSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
            height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
        }
        else if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return Jpeg(data, out width, out height);
        }
        else if (data.Length >= 10 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        }
        else if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..]));
        }
        else if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return WebP(data, out width, out height);
        }
        else if (data.Length >= 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            for (var i = 0; i < count && 6 + (i * 16) + 16 <= data.Length; i++)
            {
                int w = data[6 + (i * 16)], h = data[6 + (i * 16) + 1];
                w = w == 0 ? 256 : w;
                h = h == 0 ? 256 : h;
                if (w * h > width * height)
                {
                    width = w;
                    height = h;
                }
            }
        }

        return width > 0 && height > 0 && width <= 65535 && height <= 65535;
    }

    /// <summary>Liest den Kopf einer Datei (höchstens <see cref="ProbeLength"/> Bytes, geteilt geöffnet) und liefert ihre Größe.</summary>
    public static bool TryGetSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            var buffer = new byte[(int)Math.Min(ProbeLength, stream.Length)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return TryGetSize(buffer.AsSpan(0, read), out width, out height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool Jpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        var pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            var marker = data[pos + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF)
            {
                pos += marker == 0xFF ? 1 : 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 2)..]);
            var isFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame && pos + 9 <= data.Length)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 7)..]);
                return width > 0 && height > 0;
            }

            if (marker == 0xDA || length < 2)
            {
                return false;
            }

            pos += 2 + length;
        }

        return false;
    }

    private static bool WebP(ReadOnlySpan<byte> b, out int width, out int height)
    {
        width = height = 0;
        var format = b.Slice(12, 4);
        if (format.SequenceEqual("VP8X"u8))
        {
            width = 1 + (b[24] | (b[25] << 8) | (b[26] << 16));
            height = 1 + (b[27] | (b[28] << 8) | (b[29] << 16));
        }
        else if (format.SequenceEqual("VP8 "u8))
        {
            width = (b[26] | (b[27] << 8)) & 0x3FFF;
            height = (b[28] | (b[29] << 8)) & 0x3FFF;
        }
        else if (format.SequenceEqual("VP8L"u8) && b.Length >= 25)
        {
            width = 1 + (((b[22] & 0x3F) << 8) | b[21]);
            height = 1 + (((b[24] & 0xF) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6));
        }

        return width > 0 && height > 0;
    }
}
