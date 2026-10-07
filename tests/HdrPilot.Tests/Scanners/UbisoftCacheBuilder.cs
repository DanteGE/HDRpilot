using System.Text;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Erzeugt den Produkt-Cache von Ubisoft Connect (cache\configuration\configurations) wie am Zielrechner gemessen:
/// Protobuf-Folge von Feld 1 (Länge), darin Feld 1 Produkt-ID (Varint), Feld 2 Version (Varint), Feld 3 YAML (UTF-8).
/// Der gemessene Anfang der echten Datei lautet 0A C3 0C 08 65 10 2E 1A BC 0C "version: 2.0".
/// </summary>
internal sealed class UbisoftCacheBuilder
{
    private readonly MemoryStream _data = new();

    /// <summary>Hängt ein Produkt mit dem YAML aus Fixtures\Ubisoft\&lt;id&gt;.yml an.</summary>
    public UbisoftCacheBuilder AddFixture(uint id) =>
        Add(id, File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Ubisoft", id + ".yml"), Encoding.UTF8));

    public UbisoftCacheBuilder Add(uint id, string yaml, uint version = 0)
    {
        var inner = new MemoryStream();
        WriteTag(inner, 1, 0);
        WriteVarint(inner, id);
        WriteTag(inner, 2, 0);
        WriteVarint(inner, version == 0 ? id : version);
        var bytes = Encoding.UTF8.GetBytes(yaml);
        WriteTag(inner, 3, 2);
        WriteVarint(inner, (ulong)bytes.Length);
        inner.Write(bytes);

        WriteTag(_data, 1, 2);
        WriteVarint(_data, (ulong)inner.Length);
        inner.Position = 0;
        inner.CopyTo(_data);
        return this;
    }

    /// <summary>Fremdes Feld auf oberster Ebene (muss übersprungen werden).</summary>
    public UbisoftCacheBuilder AddUnknownField()
    {
        WriteTag(_data, 7, 0);
        WriteVarint(_data, 300);
        WriteTag(_data, 8, 5);
        _data.Write([1, 2, 3, 4]);
        WriteTag(_data, 9, 1);
        _data.Write(new byte[8]);
        return this;
    }

    public byte[] Build() => _data.ToArray();

    private static void WriteTag(Stream s, int field, int wireType) => WriteVarint(s, (ulong)((field << 3) | wireType));

    private static void WriteVarint(Stream s, ulong value)
    {
        while (value >= 0x80)
        {
            s.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        s.WriteByte((byte)value);
    }
}
