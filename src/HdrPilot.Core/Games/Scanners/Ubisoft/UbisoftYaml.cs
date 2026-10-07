using System.Globalization;
using System.Text;

namespace HdrPilot.Core.Games.Scanners.Ubisoft;

/// <summary>Knoten des YAML-Teilbaums: Zeichenkette, Zuordnung oder Liste.</summary>
internal abstract class YamlNode
{
    /// <summary>Kind einer Zuordnung oder null (auch wenn dieser Knoten keine Zuordnung ist).</summary>
    public YamlNode? this[string key] => this is YamlMapping map ? map.Get(key) : null;

    /// <summary>Text einer Zeichenkette, sonst null.</summary>
    public string? Text => (this as YamlScalar)?.Value;
}

internal sealed class YamlScalar(string value) : YamlNode
{
    public string Value { get; } = value;
}

internal sealed class YamlMapping : YamlNode
{
    private readonly List<KeyValuePair<string, YamlNode>> _entries = [];

    /// <summary>Einträge in Dateireihenfolge.</summary>
    public IReadOnlyList<KeyValuePair<string, YamlNode>> Entries => _entries;

    public YamlNode? Get(string key)
    {
        foreach (var (k, v) in _entries)
        {
            if (string.Equals(k, key, StringComparison.Ordinal))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Doppelte Schlüssel: der erste gilt.</summary>
    public void Add(string key, YamlNode value)
    {
        if (Get(key) is null)
        {
            _entries.Add(new KeyValuePair<string, YamlNode>(key, value));
        }
    }
}

internal sealed class YamlSequence : YamlNode
{
    public List<YamlNode> Items { get; } = [];
}

/// <summary>
/// Liest die Teilmenge von YAML, die Ubisoft Connect im Produkt-Cache verwendet: Einrückung mit Leerzeichen,
/// Zuordnungen, Listen (auch auf derselben Einrückung wie der Schlüssel), Zeichenketten ohne, mit einfachen und mit
/// doppelten Anführungszeichen, leere Flow-Werte ({} und []), Kommentare und Blocktexte (| und &gt;).
/// Unbekanntes wird übersprungen statt abzubrechen; nur zu tiefe Verschachtelung gilt als Fehler.
/// </summary>
internal static class UbisoftYaml
{
    private const int MaxDepth = 64;

    /// <exception cref="FormatException">Zu tief verschachtelt.</exception>
    public static YamlNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new Parser(Split(text));
        return parser.ParseDocument();
    }

    private static List<Line> Split(string text)
    {
        var lines = new List<Line>();
        var number = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            number++;
            var line = rawLine.TrimEnd('\r');
            var indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
            {
                indent++;
            }

            var content = line[indent..].TrimEnd();
            if (content.Length == 0 || content[0] == '#' || content is "---" or "..." || (indent == 0 && content[0] == '%'))
            {
                continue;
            }

            lines.Add(new Line(indent, content, number));
        }

        return lines;
    }

    private readonly record struct Line(int Indent, string Content, int Number);

    private sealed class Parser(List<Line> lines)
    {
        private int _pos;

        private Line? Current => _pos < lines.Count ? lines[_pos] : null;

        public YamlNode ParseDocument()
        {
            var result = Current is { } first ? ParseNode(first.Indent, 0) : new YamlMapping();

            // Reste mit geringerer Einrückung als die erste Zeile sind kein gültiges YAML; sie werden ignoriert.
            return result;
        }

        private YamlNode ParseNode(int indent, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new FormatException("YAML zu tief verschachtelt (Zeile " + lines[Math.Min(_pos, lines.Count - 1)].Number.ToString(CultureInfo.InvariantCulture) + ").");
            }

            return Current is { } line && IsSequenceItem(line.Content)
                ? ParseSequence(indent, depth)
                : ParseMapping(indent, depth);
        }

        private YamlMapping ParseMapping(int indent, int depth)
        {
            var map = new YamlMapping();
            while (Current is { } line && line.Indent >= indent)
            {
                if (line.Indent > indent)
                {
                    _pos++; // unerwartet tief eingerückt (etwa Fortsetzung eines mehrzeiligen Textes): überspringen
                    continue;
                }

                if (IsSequenceItem(line.Content))
                {
                    break;
                }

                _pos++;
                if (!TrySplitKey(line.Content, out var key, out var rest))
                {
                    continue; // keine Zuordnungszeile
                }

                map.Add(key, ParseValue(rest, indent, depth));
            }

            return map;
        }

        private YamlSequence ParseSequence(int indent, int depth)
        {
            var sequence = new YamlSequence();
            while (Current is { } line && line.Indent >= indent)
            {
                if (line.Indent > indent)
                {
                    _pos++;
                    continue;
                }

                if (!IsSequenceItem(line.Content))
                {
                    break;
                }

                var itemText = line.Content.Length == 1 ? string.Empty : line.Content[1..];
                var trimmed = itemText.TrimStart(' ');
                var itemIndent = indent + 1 + (itemText.Length - trimmed.Length);
                if (trimmed.Length == 0)
                {
                    _pos++;
                    sequence.Items.Add(Current is { } next && next.Indent > indent
                        ? ParseNode(next.Indent, depth + 1)
                        : new YamlScalar(string.Empty));
                }
                else if (IsSequenceItem(trimmed) || TrySplitKey(trimmed, out _, out _))
                {
                    // "- schlüssel: wert" beginnt eine Zuordnung, deren weitere Schlüssel auf itemIndent folgen.
                    lines[_pos] = new Line(itemIndent, trimmed, line.Number);
                    sequence.Items.Add(ParseNode(itemIndent, depth + 1));
                }
                else
                {
                    _pos++;
                    sequence.Items.Add(ParseInlineValue(trimmed));
                }
            }

            return sequence;
        }

        private YamlNode ParseValue(string rest, int parentIndent, int depth)
        {
            var value = rest.Trim();
            if (value.Length == 0 || value[0] == '#')
            {
                if (Current is { } next && next.Indent > parentIndent)
                {
                    return ParseNode(next.Indent, depth + 1);
                }

                if (Current is { } same && same.Indent == parentIndent && IsSequenceItem(same.Content))
                {
                    return ParseSequence(parentIndent, depth + 1);
                }

                return new YamlScalar(string.Empty);
            }

            if (value[0] is '|' or '>' && value.Length <= 3 && value.Skip(1).All(c => c is '-' or '+' || char.IsAsciiDigit(c)))
            {
                var parts = new List<string>();
                while (Current is { } block && block.Indent > parentIndent)
                {
                    parts.Add(block.Content);
                    _pos++;
                }

                return new YamlScalar(string.Join(value[0] == '|' ? "\n" : " ", parts));
            }

            return ParseInlineValue(value);
        }

        private static YamlNode ParseInlineValue(string value)
        {
            if (value == "{}")
            {
                return new YamlMapping();
            }

            if (value == "[]")
            {
                return new YamlSequence();
            }

            return new YamlScalar(ParseScalar(value));
        }

        private static bool IsSequenceItem(string content) => content == "-" || content.StartsWith("- ", StringComparison.Ordinal);
    }

    /// <summary>Trennt "schlüssel: wert". Schlüssel dürfen in Anführungszeichen stehen.</summary>
    private static bool TrySplitKey(string content, out string key, out string rest)
    {
        key = string.Empty;
        rest = string.Empty;
        int colon;
        if (content[0] is '"' or '\'')
        {
            var end = FindClosingQuote(content, 0);
            if (end < 0)
            {
                return false;
            }

            colon = end + 1;
            while (colon < content.Length && content[colon] == ' ')
            {
                colon++;
            }

            if (colon >= content.Length || content[colon] != ':' || (colon + 1 < content.Length && content[colon + 1] != ' '))
            {
                return false;
            }

            key = ParseScalar(content[..(end + 1)]);
        }
        else
        {
            colon = content.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
            {
                if (!content.EndsWith(':'))
                {
                    return false;
                }

                colon = content.Length - 1;
            }

            key = content[..colon].Trim();
            if (key.Length == 0)
            {
                return false;
            }
        }

        rest = colon + 1 < content.Length ? content[(colon + 1)..] : string.Empty;
        return true;
    }

    /// <summary>Wert einer Zeichenkette: Anführungszeichen entfernt, Escapes aufgelöst, Kommentar abgeschnitten.</summary>
    internal static string ParseScalar(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        if (value[0] == '\'')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < value.Length; i++)
            {
                if (value[i] == '\'')
                {
                    if (i + 1 < value.Length && value[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        continue;
                    }

                    return sb.ToString();
                }

                sb.Append(value[i]);
            }

            return sb.ToString(); // nicht geschlossen: Rest der Zeile
        }

        if (value[0] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c == '\\' && i + 1 < value.Length)
                {
                    i++;
                    switch (value[i])
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case '0':
                            sb.Append('\0');
                            break;
                        case 'x' when TryHex(value, i + 1, 2, out var x):
                            sb.Append((char)x);
                            i += 2;
                            break;
                        case 'u' when TryHex(value, i + 1, 4, out var u):
                            sb.Append((char)u);
                            i += 4;
                            break;
                        default:
                            sb.Append(value[i]); // \" \\ \/ und Unbekanntes
                            break;
                    }

                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }

    private static int FindClosingQuote(string content, int start)
    {
        var quote = content[start];
        for (var i = start + 1; i < content.Length; i++)
        {
            if (quote == '"' && content[i] == '\\')
            {
                i++;
                continue;
            }

            if (content[i] == quote)
            {
                if (quote == '\'' && i + 1 < content.Length && content[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                return i;
            }
        }

        return -1;
    }

    private static bool TryHex(string value, int start, int length, out int result)
    {
        result = 0;
        return start + length <= value.Length
            && int.TryParse(value.AsSpan(start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }
}
