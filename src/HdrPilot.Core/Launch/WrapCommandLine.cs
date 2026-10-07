using System.Globalization;
using System.Text;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Launch;

/// <summary>Aufruf als Steam-Startoption, aus der rohen Befehlszeile gelesen.</summary>
public sealed record WrapRequest
{
    /// <summary>Die rohe Befehlszeile (GetCommandLineW), für das Protokoll.</summary>
    public required string RawCommandLine { get; init; }

    /// <summary>Eigene Ablage (nur Tests): <c>--config-dir</c> vor <c>--wrap</c>.</summary>
    public string? ConfigDir { get; init; }

    /// <summary>Der Spielbefehl: alles nach dem ersten alleinstehenden <c>--</c>, unverändert (nur führende Leerzeichen entfernt).</summary>
    public required string GameCommand { get; init; }

    /// <summary>False, wenn <c>--</c> fehlte und der Spielbefehl ersatzweise alles nach <c>--wrap</c> ist.</summary>
    public bool SeparatorFound { get; init; }
}

/// <summary>Spiel-EXE und die unveränderten Argumente dahinter.</summary>
public sealed record GameCommand(string ExePath, string Arguments);

/// <summary>
/// Befehlszeile der Steam-Startoption <c>"…\HdrPilot.exe" --wrap -- %command%</c>. Steam setzt für %command% den
/// Spielbefehl ein (EXE-Pfad mit oder ohne Anführungszeichen, dahinter die Argumente von Steam und Nutzer). Gelesen wird
/// die rohe Befehlszeile, nicht args[]: so kommen die Argumente des Spiels Zeichen für Zeichen beim Spiel an.
/// </summary>
public static class WrapCommandLine
{
    public const string WrapSwitch = "--wrap";

    public const string Separator = "--";

    private const string ConfigDirSwitch = "--config-dir";

    private static readonly string[] StartableExtensions = [".exe", ".bat", ".cmd"];

    /// <summary>
    /// Erkennt den Aufruf als Steam-Startoption: ein alleinstehendes <c>--wrap</c> (vor einem <c>--</c>). Spielbefehl ist
    /// alles nach dem ersten alleinstehenden <c>--</c> dahinter; fehlt es (Startoption ohne <c>--</c> eingetragen), ersatzweise
    /// alles nach <c>--wrap</c>, damit das Spiel trotzdem startet. Sonst null (normaler Start der App).
    /// </summary>
    public static WrapRequest? TryParse(string rawCommandLine)
    {
        if (string.IsNullOrEmpty(rawCommandLine))
        {
            return null;
        }

        string? configDir = null;
        var wrapEnd = -1;
        var tokens = Tokenize(rawCommandLine).Skip(1).ToList(); // erstes Element: HdrPilot.exe selbst
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Value == Separator)
            {
                if (wrapEnd < 0)
                {
                    return null; // "--" vor "--wrap": kein Aufruf als Startoption
                }

                return new WrapRequest
                {
                    RawCommandLine = rawCommandLine,
                    ConfigDir = configDir,
                    GameCommand = rawCommandLine[token.End..].TrimStart(' ', '\t'),
                    SeparatorFound = true,
                };
            }

            if (wrapEnd >= 0)
            {
                continue; // zwischen --wrap und -- zählt nichts
            }

            if (string.Equals(token.Value, WrapSwitch, StringComparison.OrdinalIgnoreCase))
            {
                wrapEnd = token.End;
            }
            else if (string.Equals(token.Value, ConfigDirSwitch, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
            {
                configDir = tokens[++i].Value;
            }
        }

        if (wrapEnd < 0)
        {
            return null;
        }

        return new WrapRequest
        {
            RawCommandLine = rawCommandLine,
            ConfigDir = configDir,
            GameCommand = rawCommandLine[wrapEnd..].TrimStart(' ', '\t'),
            SeparatorFound = false,
        };
    }

    /// <summary>
    /// Trennt den Spielbefehl in EXE und Argumente. In Anführungszeichen: der Inhalt ist die EXE. Ohne Anführungszeichen
    /// (Pfad kann Leerzeichen enthalten): die EXE endet auf .exe, .bat oder .cmd (Groß-/Kleinschreibung egal) vor einem
    /// Leerzeichen oder dem Ende; passen mehrere Stellen, gewinnt die erste vorhandene Datei, sonst die erste Stelle.
    /// Die Argumente dahinter bleiben unverändert. Null, wenn der Befehl leer ist.
    /// </summary>
    public static GameCommand? SplitGameCommand(string command, Func<string, bool>? fileExists = null)
    {
        var s = (command ?? string.Empty).TrimStart(' ', '\t');
        if (s.Length == 0)
        {
            return null;
        }

        if (s[0] == '"')
        {
            var close = s.IndexOf('"', 1);
            var exe = close < 0 ? s[1..].TrimEnd() : s[1..close];
            var rest = close < 0 ? string.Empty : s[(close + 1)..].TrimStart(' ', '\t');
            return exe.Length == 0 ? null : new GameCommand(exe, rest);
        }

        var candidates = new List<int>();
        foreach (var extension in StartableExtensions)
        {
            var from = 0;
            while ((from = s.IndexOf(extension, from, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = from + extension.Length;
                if (end == s.Length || s[end] is ' ' or '\t')
                {
                    candidates.Add(end);
                }

                from = end;
            }
        }

        int exeEnd;
        if (candidates.Count == 0)
        {
            // Keine bekannte Endung: wie CreateProcess bis zum ersten Leerzeichen.
            exeEnd = s.IndexOfAny([' ', '\t']);
            if (exeEnd < 0)
            {
                exeEnd = s.Length;
            }
        }
        else
        {
            candidates.Sort();
            exeEnd = candidates.FirstOrDefault(end => fileExists?.Invoke(s[..end]) == true, candidates[0]);
        }

        return new GameCommand(s[..exeEnd], s[exeEnd..].TrimStart(' ', '\t'));
    }

    /// <summary>
    /// Arbeitsordner des Spiels: der geerbte (Steam setzt ihn auf den Spielordner), außer er ist leer, liegt unter
    /// system32 oder ist der Ordner von HdrPilot; dann der Ordner der Spiel-EXE.
    /// </summary>
    public static string ChooseWorkingDirectory(string? currentDirectory, string ownFolder, string exePath)
    {
        var current = currentDirectory ?? string.Empty;
        var unusable = string.IsNullOrWhiteSpace(current)
            || current.Contains("system32", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(ownFolder) && PathUtil.PathEquals(current, ownFolder));
        if (!unusable)
        {
            return current;
        }

        string? gameFolder = null;
        try
        {
            gameFolder = Path.GetDirectoryName(exePath);
        }
        catch (ArgumentException)
        {
            // Ungültiger Pfad: beim geerbten Ordner bleiben.
        }

        return string.IsNullOrEmpty(gameFolder) ? current : gameFolder;
    }

    /// <summary>Schlüssel des Steam-Spiels aus SteamAppId (Rückfall SteamGameId), etwa "Steam:1091500"; sonst null.</summary>
    public static string? SteamGameKey(Func<string, string?> getEnvironmentVariable, out string? rawAppId)
    {
        foreach (var name in new[] { "SteamAppId", "SteamGameId" })
        {
            var value = getEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            rawAppId = value;
            return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0
                ? "Steam:" + appId.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        rawAppId = null;
        return null;
    }

    /// <summary>
    /// Zerlegt eine Windows-Befehlszeile wie CommandLineToArgvW (Anführungszeichen, Backslash-Regeln) und merkt sich, wo
    /// jedes Element in der rohen Zeile endet. Das erste Element (Programmpfad) folgt der einfacheren argv[0]-Regel.
    /// </summary>
    private static IEnumerable<Token> Tokenize(string s)
    {
        var i = 0;
        SkipBlanks(s, ref i);
        if (i >= s.Length)
        {
            yield break;
        }

        if (s[i] == '"')
        {
            var close = s.IndexOf('"', i + 1);
            var end = close < 0 ? s.Length : close + 1;
            yield return new Token(s[(i + 1)..(close < 0 ? s.Length : close)], end);
            i = end;
        }
        else
        {
            var start = i;
            while (i < s.Length && s[i] is not (' ' or '\t'))
            {
                i++;
            }

            yield return new Token(s[start..i], i);
        }

        while (true)
        {
            SkipBlanks(s, ref i);
            if (i >= s.Length)
            {
                yield break;
            }

            var value = new StringBuilder();
            var inQuotes = false;
            while (i < s.Length)
            {
                var c = s[i];
                if (!inQuotes && c is ' ' or '\t')
                {
                    break;
                }

                if (c == '\\')
                {
                    var j = i;
                    while (j < s.Length && s[j] == '\\')
                    {
                        j++;
                    }

                    var count = j - i;
                    if (j < s.Length && s[j] == '"')
                    {
                        value.Append('\\', count / 2);
                        if (count % 2 == 1)
                        {
                            value.Append('"');
                            j++;
                        }
                    }
                    else
                    {
                        value.Append('\\', count);
                    }

                    i = j;
                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        value.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                value.Append(c);
                i++;
            }

            yield return new Token(value.ToString(), i);
        }
    }

    private static void SkipBlanks(string s, ref int i)
    {
        while (i < s.Length && s[i] is ' ' or '\t')
        {
            i++;
        }
    }

    private readonly record struct Token(string Value, int End);
}
