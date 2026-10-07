using System.Text;

namespace HdrPilot.Core.Games.Scanners.BattleNet;

/// <summary>Wire-Typen des Protobuf-Binärformats.</summary>
internal enum ProtoWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32 = 5,
}

/// <summary>
/// Minimaler Leser für das Protobuf-Binärformat (nur Wire-Ebene, ohne Schema und ohne Google.Protobuf).
/// Jeder Formatfehler (abgeschnittene Daten, zu lange Zahl, Länge über das Ende, Gruppen) wirft
/// <see cref="InvalidDataException"/>; nie wird über das Ende des Puffers gelesen.
/// </summary>
internal ref struct ProtoWireReader
{
    private const ulong MaxFieldNumber = (1UL << 29) - 1;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public ProtoWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Byte-Position des nächsten Lesevorgangs (für Fehlermeldungen).</summary>
    public readonly int Position => _position;

    public readonly bool End => _position >= _data.Length;

    public (int Field, ProtoWireType WireType) ReadTag()
    {
        var tag = ReadVarint();
        var field = tag >> 3;
        var wireType = tag & 7;
        if (field == 0 || field > MaxFieldNumber)
        {
            throw new InvalidDataException($"ungültige Feldnummer {field}");
        }

        if (wireType > (ulong)ProtoWireType.Fixed32)
        {
            throw new InvalidDataException($"unbekannter Wire-Typ {wireType} bei Feld {field}");
        }

        return ((int)field, (ProtoWireType)wireType);
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (_position >= _data.Length)
            {
                throw new InvalidDataException("Daten enden mitten in einer Zahl");
            }

            var b = _data[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("Zahl länger als 10 Byte");
    }

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        var length = ReadVarint();
        if (length > (ulong)(_data.Length - _position))
        {
            throw new InvalidDataException($"Längenangabe {length} reicht über das Ende der Daten");
        }

        var slice = _data.Slice(_position, (int)length);
        _position += (int)length;
        return slice;
    }

    /// <summary>Liest einen Text. Ungültiges UTF-8 wirft <see cref="InvalidDataException"/>.</summary>
    public string ReadString()
    {
        var bytes = ReadLengthDelimited();
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Text ist kein gültiges UTF-8", ex);
        }
    }

    /// <summary>Überspringt den Wert eines nicht benötigten Felds.</summary>
    public void Skip(ProtoWireType wireType)
    {
        switch (wireType)
        {
            case ProtoWireType.Varint:
                ReadVarint();
                break;
            case ProtoWireType.Fixed64:
                Advance(8);
                break;
            case ProtoWireType.LengthDelimited:
                ReadLengthDelimited();
                break;
            case ProtoWireType.Fixed32:
                Advance(4);
                break;
            default:
                throw new InvalidDataException($"Wire-Typ {(int)wireType} (Gruppe) wird nicht unterstützt");
        }
    }

    private void Advance(int count)
    {
        if (count > _data.Length - _position)
        {
            throw new InvalidDataException("Daten enden mitten in einem Wert");
        }

        _position += count;
    }
}
