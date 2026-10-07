using System.Globalization;
using System.Text;

namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>Die Datei entspricht nicht dem Valve-KeyValues-Format. Die Meldung nennt die Zeile.</summary>
internal sealed class KeyValuesException : Exception
{
    public KeyValuesException(string message, int line)
        : base(message + " (Zeile " + line.ToString(CultureInfo.InvariantCulture) + ")")
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// Liest das Textformat von Valve-KeyValues (KeyValues 1), wie es Steam für libraryfolders.vdf und
/// appmanifest_*.acf schreibt. Eigene Umsetzung nach der öffentlichen Formatbeschreibung:
/// <list type="bullet">
/// <item>Zeichenketten in Anführungszeichen mit den Escapes \\, \", \n, \t; unbekannte Escapes bleiben unverändert.</item>
/// <item>Tokens ohne Anführungszeichen enden an Leerraum, Anführungszeichen oder geschweiften Klammern.</item>
/// <item>Kommentare mit // bis zum Zeilenende.</item>
/// <item>Bedingungen wie [$WIN32] nach einem Schlüssel oder Wert werden überlesen (nicht ausgewertet).</item>
/// </list>
/// Abgeschnittene oder kaputte Dateien und Binärmüll führen zu einer <see cref="KeyValuesException"/>.
/// </summary>
internal static class KeyValuesParser
{
    /// <summary>Tiefer verschachtelte Dateien gelten als defekt (Schutz vor Stapelüberlauf).</summary>
    public const int MaxDepth = 64;

    /// <summary>Liefert einen namenlosen Wurzelblock mit allen Schlüsseln der obersten Ebene.</summary>
    public static KeyValueNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var children = ParseBlockContents(reader, depth: 0, openedAtLine: 0);
        return KeyValueNode.CreateBlock(string.Empty, children);
    }

    private static List<KeyValueNode> ParseBlockContents(Reader reader, int depth, int openedAtLine)
    {
        if (depth > MaxDepth)
        {
            throw new KeyValuesException("Zu tief verschachtelt", reader.Line);
        }

        var isRoot = depth == 0;
        var children = new List<KeyValueNode>();
        while (true)
        {
            var token = reader.Next();
            switch (token.Kind)
            {
                case TokenKind.End when isRoot:
                    return children;
                case TokenKind.End:
                    throw new KeyValuesException($"Block ab Zeile {openedAtLine} wird nicht geschlossen (Datei abgeschnitten?)", reader.Line);
                case TokenKind.CloseBrace when isRoot:
                    throw new KeyValuesException("Unerwartete schließende Klammer", token.Line);
                case TokenKind.CloseBrace:
                    return children;
                case TokenKind.OpenBrace:
                    throw new KeyValuesException("Block ohne Schlüssel", token.Line);
                case TokenKind.Condition:
                    continue;
            }

            var key = token.Text;
            var value = reader.Next();
            while (value.Kind == TokenKind.Condition)
            {
                value = reader.Next();
            }

            switch (value.Kind)
            {
                case TokenKind.String:
                    children.Add(KeyValueNode.CreateString(key, value.Text));
                    break;
                case TokenKind.OpenBrace:
                    children.Add(KeyValueNode.CreateBlock(key, ParseBlockContents(reader, depth + 1, value.Line)));
                    break;
                case TokenKind.End:
                    throw new KeyValuesException($"Wert für \"{key}\" fehlt (Datei abgeschnitten?)", reader.Line);
                default:
                    throw new KeyValuesException($"Wert für \"{key}\" fehlt", value.Line);
            }

            reader.SkipConditions();
        }
    }

    private enum TokenKind
    {
        End,
        String,
        OpenBrace,
        CloseBrace,
        Condition,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Line);

    private sealed class Reader(string text)
    {
        private int _pos;

        public int Line { get; private set; } = 1;

        public void SkipConditions()
        {
            var save = _pos;
            var saveLine = Line;
            while (true)
            {
                var token = Next();
                if (token.Kind == TokenKind.Condition)
                {
                    save = _pos;
                    saveLine = Line;
                    continue;
                }

                _pos = save;
                Line = saveLine;
                return;
            }
        }

        public Token Next()
        {
            SkipWhitespaceAndComments();
            if (_pos >= text.Length)
            {
                return new Token(TokenKind.End, string.Empty, Line);
            }

            var c = text[_pos];
            switch (c)
            {
                case '{':
                    _pos++;
                    return new Token(TokenKind.OpenBrace, "{", Line);
                case '}':
                    _pos++;
                    return new Token(TokenKind.CloseBrace, "}", Line);
                case '"':
                    return ReadQuoted();
                case '[':
                    return ReadCondition();
                default:
                    return ReadUnquoted();
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '\n')
                {
                    Line++;
                    _pos++;
                }
                else if (c == '\uFEFF' || (char.IsWhiteSpace(c) && c != '\0'))
                {
                    _pos++;
                }
                else if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private Token ReadQuoted()
        {
            var startLine = Line;
            _pos++; // öffnendes Anführungszeichen
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                if (c == '"')
                {
                    return new Token(TokenKind.String, sb.ToString(), startLine);
                }

                if (c == '\n')
                {
                    Line++;
                }

                if (c == '\\' && _pos < text.Length)
                {
                    var e = text[_pos];
                    switch (e)
                    {
                        case '\\':
                            sb.Append('\\');
                            _pos++;
                            continue;
                        case '"':
                            sb.Append('"');
                            _pos++;
                            continue;
                        case 'n':
                            sb.Append('\n');
                            _pos++;
                            continue;
                        case 't':
                            sb.Append('\t');
                            _pos++;
                            continue;
                    }

                    // Unbekannter Escape (etwa ein einfacher Backslash in "D:\Spielordner"): unverändert übernehmen.
                    sb.Append('\\');
                    continue;
                }

                if (c == '\0')
                {
                    throw new KeyValuesException("Nullzeichen in Zeichenkette (Binärdaten oder falsche Kodierung?)", Line);
                }

                sb.Append(c);
            }

            throw new KeyValuesException($"Zeichenkette ab Zeile {startLine} wird nicht geschlossen (Datei abgeschnitten?)", Line);
        }

        private Token ReadCondition()
        {
            var startLine = Line;
            var start = _pos;
            while (_pos < text.Length && text[_pos] != ']' && text[_pos] != '\n')
            {
                _pos++;
            }

            if (_pos >= text.Length || text[_pos] != ']')
            {
                throw new KeyValuesException("Bedingung in eckigen Klammern wird nicht geschlossen", startLine);
            }

            _pos++;
            return new Token(TokenKind.Condition, text[start.._pos], startLine);
        }

        private Token ReadUnquoted()
        {
            var start = _pos;
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (char.IsWhiteSpace(c) || c == '"' || c == '{' || c == '}')
                {
                    break;
                }

                if (char.IsControl(c))
                {
                    throw new KeyValuesException(
                        "Ungültiges Steuerzeichen 0x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture) + " (Binärdaten oder falsche Kodierung?)",
                        Line);
                }

                _pos++;
            }

            return new Token(TokenKind.String, text[start.._pos], Line);
        }
    }
}
