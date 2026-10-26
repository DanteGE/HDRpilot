using System.Globalization;
using System.Text;
using HdrPilot.Core.Resources;

namespace HdrPilot.Core.Games.Scanners.Ubisoft;

/// <summary>
/// Liest den Produkt-Cache von Ubisoft Connect (<c>&lt;Launcher&gt;\cache\configuration\configurations</c>).
/// Aufbau (gemessen): Protobuf-Folge von Feld 1 (Länge), darin Feld 1 = Produkt-ID (Varint), Feld 2 = Version (Varint),
/// Feld 3 = YAML-Text (UTF-8). Eigene Umsetzung des Protobuf-Wire-Formats (nur Varint, 64 Bit, Länge, 32 Bit).
/// </summary>
internal static class UbisoftConfigurationCache
{
    /// <summary>
    /// Liefert die YAML-Texte der gewünschten Produkt-IDs. Ist die Datei ab einer Stelle beschädigt, bleiben die bis
    /// dahin gelesenen Einträge erhalten und <paramref name="error"/> beschreibt den Schaden.
    /// </summary>
    public static IReadOnlyDictionary<uint, string> Read(byte[] data, IReadOnlySet<uint> wanted, out string? error)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(wanted);
        var result = new Dictionary<uint, string>();
        error = null;
        var pos = 0;
        while (pos < data.Length)
        {
            var at = pos;
            if (!TryReadVarint(data, ref pos, data.Length, out var tag) || (tag >> 3) == 0)
            {
                error = Describe(ScanStrings.Ubisoft_InvalidFieldHeader, at);
                break;
            }

            var field = tag >> 3;
            var wireType = (int)(tag & 7);
            if (wireType != 2)
            {
                if (!TrySkip(data, ref pos, data.Length, wireType))
                {
                    error = Describe(ScanStrings.Ubisoft_InvalidField, at);
                    break;
                }

                continue;
            }

            if (!TryReadLength(data, ref pos, data.Length, out var length))
            {
                error = Describe(ScanStrings.Ubisoft_FieldLength, at);
                break;
            }

            var end = pos + length;
            if (field == 1 && !TryReadProduct(data, pos, end, wanted, result))
            {
                error = Describe(ScanStrings.Ubisoft_ProductDamaged, at);
                break;
            }

            pos = end;
        }

        return result;
    }

    private static bool TryReadProduct(byte[] data, int pos, int end, IReadOnlySet<uint> wanted, Dictionary<uint, string> result)
    {
        ulong? id = null;
        var yamlStart = -1;
        var yamlLength = 0;
        while (pos < end)
        {
            if (!TryReadVarint(data, ref pos, end, out var tag) || (tag >> 3) == 0)
            {
                return false;
            }

            var field = tag >> 3;
            var wireType = (int)(tag & 7);
            if (field == 1 && wireType == 0)
            {
                if (!TryReadVarint(data, ref pos, end, out var value))
                {
                    return false;
                }

                id = value;
            }
            else if (field == 3 && wireType == 2)
            {
                if (!TryReadLength(data, ref pos, end, out yamlLength))
                {
                    return false;
                }

                yamlStart = pos;
                pos += yamlLength;
            }
            else if (!TrySkip(data, ref pos, end, wireType))
            {
                return false;
            }
        }

        if (id is { } productId and <= uint.MaxValue && yamlStart >= 0 && wanted.Contains((uint)productId))
        {
            result[(uint)productId] = Encoding.UTF8.GetString(data, yamlStart, yamlLength);
        }

        return true;
    }

    private static bool TrySkip(byte[] data, ref int pos, int end, int wireType)
    {
        switch (wireType)
        {
            case 0:
                return TryReadVarint(data, ref pos, end, out _);
            case 1:
                return Advance(ref pos, end, 8);
            case 2:
                if (!TryReadLength(data, ref pos, end, out var length))
                {
                    return false;
                }

                pos += length;
                return true;
            case 5:
                return Advance(ref pos, end, 4);
            default:
                return false; // Gruppen (3, 4) und unbekannte Typen kommen im Cache nicht vor
        }
    }

    private static bool Advance(ref int pos, int end, int count)
    {
        if (count > end - pos)
        {
            return false;
        }

        pos += count;
        return true;
    }

    private static bool TryReadLength(byte[] data, ref int pos, int end, out int length)
    {
        length = 0;
        if (!TryReadVarint(data, ref pos, end, out var value) || value > (ulong)(end - pos))
        {
            return false;
        }

        length = (int)value;
        return true;
    }

    private static bool TryReadVarint(byte[] data, ref int pos, int end, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (pos >= end)
            {
                return false;
            }

            var b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(string what, int offset) =>
        ScanText.Format(ScanStrings.Ubisoft_AtByte, what, offset.ToString(CultureInfo.InvariantCulture));
}
