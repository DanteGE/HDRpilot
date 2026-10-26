using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HdrPilot.Core.Resources;

namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>
/// Angaben aus dem Steam-App-Cache für eine AppID: App-Typ (etwa "Game", "Tool", "Application"), Name und die Namen der
/// Symbole: <see cref="ClientIcon"/> ist die Datei <c>steam\games\&lt;clienticon&gt;.ico</c>, <see cref="Icon"/> das
/// 32-px-JPEG <c>appcache\librarycache\&lt;AppID&gt;\&lt;icon&gt;.jpg</c> (beides SHA-1 in Hex, E86).
/// </summary>
internal sealed record SteamAppInfo(string? Type, string? Name, string? ClientIcon = null, string? Icon = null);

/// <summary>
/// Liest aus <c>&lt;Steam&gt;\appcache\appinfo.vdf</c> (Binärformat) nur <c>appinfo/common/type</c>, <c>name</c>,
/// <c>clienticon</c> und <c>icon</c> der gewünschten AppIDs. Eigene Umsetzung nach der öffentlichen Formatbeschreibung
/// (SteamDB „SteamAppInfo“). Aufbau:
/// <list type="bullet">
/// <item>Kopf: UInt32 Kennung (0x07564427/28/29), UInt32 Universum; ab Version 29 zusätzlich Int64 Offset der Schlüsseltabelle.</item>
/// <item>Je App: UInt32 AppID (0 = Ende), UInt32 Größe des Rests, danach 40 (v27) bzw. 60 Byte (v28/29) Kopfdaten
/// (Status, Zeitstempel, Token, SHA-1, Änderungsnummer, ab v28 SHA-1 der Binärdaten) und binäre KeyValues.</item>
/// <item>Binäre KeyValues: Typbyte, Schlüssel (v29: Int32-Index in die Schlüsseltabelle, sonst nullterminiert), Wert.
/// 0x00 Block, 0x01 Zeichenkette, 0x02 Int32, 0x03 Float, 0x04 Zeiger, 0x05 UTF-16, 0x06 Farbe, 0x07 UInt64,
/// 0x0A Int64, 0x08/0x0B Blockende.</item>
/// <item>v29: Schlüsseltabelle am Offset: UInt32 Anzahl, danach nullterminierte UTF-8-Zeichenketten.</item>
/// </list>
/// </summary>
internal static class SteamAppInfoReader
{
    public const uint MagicV27 = 0x07564427;
    public const uint MagicV28 = 0x07564428;
    public const uint MagicV29 = 0x07564429;

    private const int MaxDepth = 64;

    /// <summary>
    /// Liefert die Angaben der gewünschten AppIDs, die im Cache stehen. Null bei unbekannter Formatversion.
    /// </summary>
    /// <exception cref="InvalidDataException">Die Datei ist abgeschnitten oder beschädigt.</exception>
    public static IReadOnlyDictionary<uint, SteamAppInfo>? Read(byte[] data, IReadOnlySet<uint> wanted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(wanted);
        var reader = new ByteReader(data, 0, data.Length);
        var magic = reader.ReadUInt32();
        if (magic is not (MagicV27 or MagicV28 or MagicV29))
        {
            return null;
        }

        reader.ReadUInt32(); // Universum
        var entriesEnd = data.Length;
        string[]? keyTable = null;
        if (magic == MagicV29)
        {
            var offset = reader.ReadInt64();
            if (offset < reader.Position || offset > data.Length - 4)
            {
                throw new InvalidDataException(ScanStrings.AppInfo_KeyTableOffset);
            }

            entriesEnd = (int)offset;
            keyTable = ReadKeyTable(data, (int)offset);
        }

        var entryHeaderSize = magic == MagicV27 ? 40 : 60;
        var result = new Dictionary<uint, SteamAppInfo>();
        var entries = new ByteReader(data, reader.Position, entriesEnd);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appId = entries.ReadUInt32();
            if (appId == 0)
            {
                break;
            }

            var size = entries.ReadUInt32();
            if (size < entryHeaderSize || size > (uint)(entriesEnd - entries.Position))
            {
                throw new InvalidDataException(ScanText.Format(ScanStrings.AppInfo_EntrySize, appId, size));
            }

            var entryEnd = entries.Position + (int)size;
            if (wanted.Contains(appId))
            {
                var kv = new ByteReader(data, entries.Position + entryHeaderSize, entryEnd);
                result[appId] = ReadEntry(kv, keyTable);
                if (result.Count == wanted.Count)
                {
                    break;
                }
            }

            entries.Position = entryEnd;
        }

        return result;
    }

    private static string[] ReadKeyTable(byte[] data, int offset)
    {
        var reader = new ByteReader(data, offset, data.Length);
        var count = reader.ReadUInt32();
        if (count > (uint)(data.Length - reader.Position))
        {
            throw new InvalidDataException(ScanStrings.AppInfo_KeyTableLength);
        }

        var keys = new string[count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = reader.ReadCString();
        }

        return keys;
    }

    private static SteamAppInfo ReadEntry(ByteReader reader, string[]? keyTable)
    {
        var common = new CommonValues();
        ReadObject(reader, keyTable, Section.Root, 0, common);
        return new SteamAppInfo(common.Type, common.Name, common.ClientIcon, common.Icon);
    }

    private enum Section
    {
        Root,
        AppInfo,
        Common,
        Other,
    }

    private static void ReadObject(ByteReader reader, string[]? keyTable, Section section, int depth, CommonValues common)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException(ScanStrings.AppInfo_TooDeep);
        }

        while (true)
        {
            var kind = reader.ReadByte();
            if (kind is 0x08 or 0x0B)
            {
                return;
            }

            var key = ReadKey(reader, keyTable);
            switch (kind)
            {
                case 0x00:
                    var child = (section, key) switch
                    {
                        (Section.Root, "appinfo") => Section.AppInfo,
                        (Section.AppInfo, "common") => Section.Common,
                        _ => Section.Other,
                    };
                    ReadObject(reader, keyTable, child, depth + 1, common);
                    break;
                case 0x01:
                    var value = reader.ReadCString();
                    if (section == Section.Common)
                    {
                        common.Set(key, value);
                    }

                    break;
                case 0x02 or 0x03 or 0x04 or 0x06:
                    reader.Skip(4);
                    break;
                case 0x07 or 0x0A:
                    reader.Skip(8);
                    break;
                case 0x05:
                    reader.SkipWideCString();
                    break;
                default:
                    throw new InvalidDataException(
                        ScanText.Format(ScanStrings.AppInfo_UnknownType, kind.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }
    }

    private static string ReadKey(ByteReader reader, string[]? keyTable)
    {
        if (keyTable is null)
        {
            return reader.ReadCString();
        }

        var index = reader.ReadInt32();
        if (index < 0 || index >= keyTable.Length)
        {
            throw new InvalidDataException(ScanText.Format(ScanStrings.AppInfo_KeyIndex, index));
        }

        return keyTable[index];
    }

    /// <summary>Gesuchte Zeichenketten aus appinfo/common.</summary>
    private sealed class CommonValues
    {
        public string? Type { get; private set; }

        public string? Name { get; private set; }

        public string? ClientIcon { get; private set; }

        public string? Icon { get; private set; }

        public void Set(string key, string value)
        {
            if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase))
            {
                Type = value;
            }
            else if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                Name = value;
            }
            else if (string.Equals(key, "clienticon", StringComparison.OrdinalIgnoreCase))
            {
                ClientIcon = value;
            }
            else if (string.Equals(key, "icon", StringComparison.OrdinalIgnoreCase))
            {
                Icon = value;
            }
        }
    }

    /// <summary>Liest mit Grenzprüfung; jede Überschreitung gilt als abgeschnittene Datei.</summary>
    private sealed class ByteReader
    {
        private readonly byte[] _data;
        private readonly int _start;
        private readonly int _end;

        public ByteReader(byte[] data, int start, int end)
        {
            _data = data;
            _start = start;
            _end = end;
            Position = _start;
        }

        public int Position { get; set; }

        public byte ReadByte()
        {
            Ensure(1);
            return _data[Position++];
        }

        public uint ReadUInt32()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position, 4));
            Position += 4;
            return value;
        }

        public int ReadInt32()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Position, 4));
            Position += 4;
            return value;
        }

        public long ReadInt64()
        {
            Ensure(8);
            var value = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(Position, 8));
            Position += 8;
            return value;
        }

        public void Skip(int count)
        {
            Ensure(count);
            Position += count;
        }

        public string ReadCString()
        {
            var span = _data.AsSpan(Position, Math.Max(0, _end - Position));
            var length = span.IndexOf((byte)0);
            if (length < 0)
            {
                throw Truncated();
            }

            var value = Encoding.UTF8.GetString(span[..length]);
            Position += length + 1;
            return value;
        }

        public void SkipWideCString()
        {
            while (true)
            {
                Ensure(2);
                var isEnd = _data[Position] == 0 && _data[Position + 1] == 0;
                Position += 2;
                if (isEnd)
                {
                    return;
                }
            }
        }

        private void Ensure(int count)
        {
            if (Position < _start || count < 0 || Position > _end - count)
            {
                throw Truncated();
            }
        }

        private static InvalidDataException Truncated() => new(ScanStrings.AppInfo_Truncated);
    }
}
