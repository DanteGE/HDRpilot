using System.Text;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Erzeugt appinfo.vdf im Binärformat von Steam (v27, v28, v29), wie es am Zielrechner liegt (gemessen: v29,
/// Kennung 29 44 56 07). Jeder Eintrag enthält wie im Original einen Block "appinfo" mit "appid", "common"
/// (name, type, oslist, Zahlen) sowie "extended" und "config", damit das Überspringen aller Werttypen geprüft wird.
/// </summary>
internal sealed class SteamAppInfoBuilder(uint magic = SteamAppInfoBuilder.V29)
{
    public const uint V27 = 0x07564427;
    public const uint V28 = 0x07564428;
    public const uint V29 = 0x07564429;

    private readonly List<(uint AppId, byte[] Kv)> _entries = [];
    private readonly List<string> _keys = [];

    /// <summary>Eintrag; <paramref name="clientIcon"/> und <paramref name="icon"/> wie common/clienticon und common/icon (SHA-1 in Hex, E86).</summary>
    public SteamAppInfoBuilder Add(uint appId, string? type, string? name, string? clientIcon = null, string? icon = null)
    {
        var kv = new MemoryStream();
        BeginObject(kv, "appinfo");
        Int32(kv, "appid", (int)appId);
        BeginObject(kv, "common");
        if (name is not null)
        {
            String(kv, "name", name);
        }

        if (type is not null)
        {
            String(kv, "type", type);
        }

        if (clientIcon is not null)
        {
            String(kv, "clienticon", clientIcon);
            String(kv, "clienttga", clientIcon + "tga");
        }

        if (icon is not null)
        {
            String(kv, "icon", icon);
        }

        String(kv, "oslist", "windows");
        UInt64(kv, "gameid", appId);
        Float(kv, "review_percentage", 0.93f);
        WideString(kv, "sortas", name ?? string.Empty);
        EndObject(kv);
        BeginObject(kv, "extended");
        String(kv, "developer", "Beispiel");
        Int64(kv, "listofdlc_count", 2);
        EndObject(kv);
        BeginObject(kv, "config");
        String(kv, "installdir", name ?? "x");
        BeginObject(kv, "launch");
        BeginObject(kv, "0");
        String(kv, "executable", "game.exe");
        String(kv, "type", "default");
        EndObject(kv);
        EndObject(kv);
        EndObject(kv);
        EndObject(kv); // appinfo
        kv.WriteByte(0x08); // Ende der Wurzel
        _entries.Add((appId, kv.ToArray()));
        return this;
    }

    public byte[] Build()
    {
        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        w.Write(magic);
        w.Write(1u); // Universum
        var offsetPosition = file.Position;
        if (magic == V29)
        {
            w.Write(0L); // Platzhalter für den Offset der Schlüsseltabelle
        }

        var headerSize = magic == V27 ? 40 : 60;
        foreach (var (appId, kv) in _entries)
        {
            w.Write(appId);
            w.Write((uint)(headerSize + kv.Length));
            w.Write(2u); // infoState
            w.Write(1_790_000_000u); // lastUpdated
            w.Write(0UL); // picsToken
            w.Write(new byte[20]); // SHA-1 Text
            w.Write(12345u); // changeNumber
            if (magic != V27)
            {
                w.Write(new byte[20]); // SHA-1 Binärdaten
            }

            w.Write(kv);
        }

        w.Write(0u); // Ende der Einträge
        if (magic == V29)
        {
            var tableOffset = file.Position;
            w.Write((uint)_keys.Count);
            foreach (var key in _keys)
            {
                w.Write(Encoding.UTF8.GetBytes(key));
                w.Write((byte)0);
            }

            file.Position = offsetPosition;
            w.Write(tableOffset);
        }

        w.Flush();
        return file.ToArray();
    }

    private void Key(Stream s, string key)
    {
        if (magic == V29)
        {
            var index = _keys.IndexOf(key);
            if (index < 0)
            {
                index = _keys.Count;
                _keys.Add(key);
            }

            s.Write(BitConverter.GetBytes(index));
        }
        else
        {
            s.Write(Encoding.UTF8.GetBytes(key));
            s.WriteByte(0);
        }
    }

    private void BeginObject(Stream s, string key)
    {
        s.WriteByte(0x00);
        Key(s, key);
    }

    private static void EndObject(Stream s) => s.WriteByte(0x08);

    private void String(Stream s, string key, string value)
    {
        s.WriteByte(0x01);
        Key(s, key);
        s.Write(Encoding.UTF8.GetBytes(value));
        s.WriteByte(0);
    }

    private void WideString(Stream s, string key, string value)
    {
        s.WriteByte(0x05);
        Key(s, key);
        s.Write(Encoding.Unicode.GetBytes(value));
        s.Write([0, 0]);
    }

    private void Int32(Stream s, string key, int value)
    {
        s.WriteByte(0x02);
        Key(s, key);
        s.Write(BitConverter.GetBytes(value));
    }

    private void Float(Stream s, string key, float value)
    {
        s.WriteByte(0x03);
        Key(s, key);
        s.Write(BitConverter.GetBytes(value));
    }

    private void UInt64(Stream s, string key, ulong value)
    {
        s.WriteByte(0x07);
        Key(s, key);
        s.Write(BitConverter.GetBytes(value));
    }

    private void Int64(Stream s, string key, long value)
    {
        s.WriteByte(0x0A);
        Key(s, key);
        s.Write(BitConverter.GetBytes(value));
    }
}
