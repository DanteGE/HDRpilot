using System.Globalization;
using System.Text.RegularExpressions;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Kleiner Leser für C-Header (Windows SDK): Strukturen, Enums, #defines. Berechnet das x64-Layout
/// (MSVC-Regeln: natürliche Ausrichtung, Unions, Bitfelder gleichen Typs teilen sich eine Einheit),
/// damit P/Invoke-Strukturen automatisch gegen den Header geprüft werden können.
/// </summary>
public sealed partial class CHeaderLayout
{
    private static readonly Dictionary<string, (int Size, int Align)> Primitives = new(StringComparer.Ordinal)
    {
        ["UINT32"] = (4, 4), ["UINT"] = (4, 4), ["DWORD"] = (4, 4), ["ULONG"] = (4, 4), ["LONG"] = (4, 4),
        ["INT"] = (4, 4), ["INT32"] = (4, 4), ["BOOL"] = (4, 4), ["int"] = (4, 4), ["unsigned"] = (4, 4),
        ["UINT16"] = (2, 2), ["USHORT"] = (2, 2), ["WORD"] = (2, 2), ["WCHAR"] = (2, 2), ["SHORT"] = (2, 2),
        ["UINT64"] = (8, 8), ["ULONGLONG"] = (8, 8), ["LONGLONG"] = (8, 8), ["ULONG_PTR"] = (8, 8),
        ["BYTE"] = (1, 1), ["UCHAR"] = (1, 1), ["CHAR"] = (1, 1),
        // Aus anderen Headern (winnt.h, windef.h, guiddef.h):
        ["LUID"] = (8, 4), ["POINTL"] = (8, 4), ["RECTL"] = (16, 4), ["GUID"] = (16, 4),
    };

    private readonly Dictionary<string, List<string>> _structBodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Member>> _structCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _duplicates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _enumTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _constants = new(StringComparer.Ordinal);

    private CHeaderLayout()
    {
    }

    public static CHeaderLayout Parse(string headerText)
    {
        var layout = new CHeaderLayout();
        headerText = headerText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var withoutComments = BlockComment().Replace(headerText, " ");
        withoutComments = LineComment().Replace(withoutComments, string.Empty);

        foreach (Match m in Define().Matches(withoutComments))
        {
            if (TryParseNumber(m.Groups[2].Value, out var value))
            {
                layout._constants[m.Groups[1].Value] = value;
            }
        }

        var code = string.Join('\n', withoutComments.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        var tokens = Tokenize(code);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != "typedef" || i + 1 >= tokens.Count)
            {
                continue;
            }

            var kind = tokens[i + 1];
            if (kind is not ("struct" or "enum" or "union"))
            {
                continue;
            }

            var j = i + 2;
            if (j < tokens.Count && tokens[j] != "{")
            {
                j++; // Tag-Name, etwa "_DISPLAYCONFIG_SET_HDR_STATE"
            }

            if (j >= tokens.Count || tokens[j] != "{")
            {
                continue; // Vorwärtsdeklaration
            }

            var close = FindClose(tokens, j);
            var name = close + 1 < tokens.Count ? tokens[close + 1] : string.Empty;
            if (name is "," or ";" or "*" || string.IsNullOrEmpty(name))
            {
                continue;
            }

            var body = tokens.GetRange(j + 1, close - j - 1);
            if (kind == "enum")
            {
                layout._enumTypes.Add(name);
                layout.ParseEnum(body);
            }
            else if (!layout._structBodies.TryAdd(name, body))
            {
                layout._duplicates.Add(name);
            }

            i = close;
        }

        return layout;
    }

    public bool HasStruct(string name) => _structBodies.ContainsKey(name);

    /// <summary>True, wenn der Name mehrfach definiert ist (etwa in #if/#else-Zweigen); dann ist der Abgleich nicht eindeutig.</summary>
    public bool IsDefinedMoreThanOnce(string name) => _duplicates.Contains(name);

    public long Constant(string name) =>
        _constants.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException("Konstante nicht im Header: " + name);

    /// <summary>Oberste Ebene einer Struktur: Slots mit Offset, Größe und den Namen, die dort liegen (bei Unions mehrere).</summary>
    public StructLayoutInfo Layout(string structName)
    {
        var members = Members(structName) ?? throw new KeyNotFoundException("Struktur nicht im Header: " + structName);
        var slots = new List<Slot>();
        var (size, align) = LayoutMembers(members, isUnion: false, slots);
        return new StructLayoutInfo(structName, size, align, slots);
    }

    /// <summary>Bitposition eines Bitfelds innerhalb seiner Einheit, gesucht in allen Ebenen der Struktur.</summary>
    public int BitPosition(string structName, string bitFieldName)
    {
        var members = Members(structName) ?? throw new KeyNotFoundException("Struktur nicht im Header: " + structName);
        return FindBit(members, bitFieldName) ?? throw new KeyNotFoundException($"Bitfeld {bitFieldName} nicht in {structName}");
    }

    private List<Member>? Members(string structName)
    {
        if (_structCache.TryGetValue(structName, out var cached))
        {
            return cached;
        }

        if (!_structBodies.TryGetValue(structName, out var body))
        {
            return null;
        }

        var pos = 0;
        var members = ParseMembers(body, ref pos);
        _structCache[structName] = members;
        return members;
    }

    private static int? FindBit(List<Member> members, string name)
    {
        var bit = 0;
        string? unitType = null;
        foreach (var m in members)
        {
            if (m.Children is not null)
            {
                bit = 0;
                unitType = null;
                var found = FindBit(m.Children, name);
                if (found is not null)
                {
                    return found;
                }

                continue;
            }

            if (m.BitWidth is { } width)
            {
                if (unitType != m.Type)
                {
                    bit = 0;
                    unitType = m.Type;
                }

                if (m.Name == name)
                {
                    return bit;
                }

                bit += width;
            }
            else
            {
                bit = 0;
                unitType = null;
            }
        }

        return null;
    }

    private (int Size, int Align) LayoutMembers(List<Member> members, bool isUnion, List<Slot>? slots)
    {
        var offset = 0;
        var maxAlign = 1;
        var maxSize = 0;
        string? bitUnitType = null;
        var bitUsed = 0;
        var bitUnitSize = 0;

        foreach (var m in members)
        {
            int size;
            int align;
            if (m.BitWidth is { } width && !isUnion)
            {
                var (typeSize, typeAlign) = TypeLayout(m.Type);
                if (bitUnitType == m.Type && bitUsed + width <= typeSize * 8)
                {
                    bitUsed += width;
                    continue;
                }

                offset = AlignUp(offset, typeAlign);
                slots?.Add(new Slot(offset, typeSize, [m.Name], IsUnion: false));
                offset += typeSize;
                maxAlign = Math.Max(maxAlign, typeAlign);
                bitUnitType = m.Type;
                bitUsed = width;
                bitUnitSize = typeSize;
                continue;
            }

            bitUnitType = null;
            bitUsed = 0;

            if (m.Children is not null)
            {
                (size, align) = LayoutMembers(m.Children, m.IsUnion, slots: null);
            }
            else
            {
                (size, align) = TypeLayout(m.Type);
                size *= m.ArrayLength;
            }

            maxAlign = Math.Max(maxAlign, align);
            if (isUnion)
            {
                maxSize = Math.Max(maxSize, size);
                continue;
            }

            offset = AlignUp(offset, align);
            if (slots is not null)
            {
                var names = m.Children is not null && m.IsUnion
                    ? m.Children.Select(c => c.Name).Where(n => !n.StartsWith("DUMMY", StringComparison.Ordinal)).ToList()
                    : [m.Name];
                slots.Add(new Slot(offset, size, names, m.Children is not null && m.IsUnion));
            }

            offset += size;
        }

        _ = bitUnitSize;
        var total = isUnion ? maxSize : offset;
        return (AlignUp(total, maxAlign), maxAlign);
    }

    private (int Size, int Align) TypeLayout(string type)
    {
        if (Primitives.TryGetValue(type, out var p))
        {
            return p;
        }

        if (_enumTypes.Contains(type))
        {
            return (4, 4);
        }

        if (Members(type) is { } members)
        {
            return LayoutMembers(members, isUnion: false, slots: null);
        }

        throw new KeyNotFoundException("Unbekannter Typ im Header: " + type);
    }

    private List<Member> ParseMembers(List<string> tokens, ref int pos)
    {
        var members = new List<Member>();
        while (pos < tokens.Count && tokens[pos] != "}")
        {
            if (tokens[pos] is "union" or "struct" && pos + 1 < tokens.Count && tokens[pos + 1] == "{")
            {
                var isUnion = tokens[pos] == "union";
                pos += 2;
                var children = ParseMembers(tokens, ref pos);
                pos++; // '}'
                var name = tokens[pos] == ";" ? "DUMMY" : tokens[pos];
                while (tokens[pos] != ";")
                {
                    pos++;
                }

                pos++;
                members.Add(new Member(isUnion ? "union" : "struct", name, 1, null, children, isUnion));
                continue;
            }

            var declaration = new List<string>();
            while (tokens[pos] != ";")
            {
                declaration.Add(tokens[pos]);
                pos++;
            }

            pos++;
            members.Add(ParseDeclaration(declaration));
        }

        return members;
    }

    private Member ParseDeclaration(List<string> d)
    {
        var colon = d.IndexOf(":");
        int? bits = null;
        if (colon >= 0)
        {
            bits = int.Parse(d[colon + 1], CultureInfo.InvariantCulture);
            d = d.GetRange(0, colon);
        }

        var arrayLength = 1;
        var bracket = d.IndexOf("[");
        if (bracket >= 0)
        {
            var lengthToken = d[bracket + 1];
            arrayLength = TryParseNumber(lengthToken, out var n) ? (int)n : (int)Constant(lengthToken);
            d = d.GetRange(0, bracket);
        }

        var name = d[^1];
        var type = d[^2];
        return new Member(type, name, arrayLength, bits, null, false);
    }

    private void ParseEnum(List<string> body)
    {
        long next = 0;
        var i = 0;
        while (i < body.Count)
        {
            var name = body[i++];
            long value = next;
            if (i < body.Count && body[i] == "=")
            {
                i++;
                var expr = new List<string>();
                while (i < body.Count && body[i] != ",")
                {
                    expr.Add(body[i++]);
                }

                var text = string.Concat(expr);
                if (TryParseNumber(text, out var parsed))
                {
                    value = parsed;
                }
                else if (_constants.TryGetValue(text, out var alias))
                {
                    value = alias;
                }
            }

            _constants[name] = value;
            next = value + 1;
            if (i < body.Count && body[i] == ",")
            {
                i++;
            }
        }
    }

    private static int FindClose(List<string> tokens, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i] == "{")
            {
                depth++;
            }
            else if (tokens[i] == "}")
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        throw new FormatException("Unbalancierte Klammern im Header.");
    }

    private static List<string> Tokenize(string code) =>
        TokenPattern().Matches(code).Select(m => m.Value).ToList();

    private static bool TryParseNumber(string text, out long value)
    {
        text = text.Trim().TrimEnd('L', 'l', 'U', 'u');
        var negative = text.StartsWith('-');
        if (negative)
        {
            text = text[1..].Trim();
        }

        bool ok;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            ok = long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        else
        {
            ok = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        if (ok && negative)
        {
            value = -value;
        }

        return ok;
    }

    private static int AlignUp(int value, int align) => (value + align - 1) / align * align;

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"^[ \t]*#define[ \t]+(\w+)[ \t]+\(?([-]?\s*(?:0x[0-9A-Fa-f]+|\d+)[uUlL]*)\)?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Define();

    [GeneratedRegex(@"0x[0-9A-Fa-f]+[uUlL]*|\d+[uUlL]*|\w+|[{}\[\];:,=*\-()]")]
    private static partial Regex TokenPattern();

    private sealed record Member(string Type, string Name, int ArrayLength, int? BitWidth, List<Member>? Children, bool IsUnion);

    public sealed record Slot(int Offset, int Size, IReadOnlyList<string> Names, bool IsUnion);

    public sealed record StructLayoutInfo(string Name, int Size, int Align, IReadOnlyList<Slot> Slots);
}
