using System.Globalization;
using System.IO;
using HdrPilot.App.Resources;
using HdrPilot.Core.Games;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Ergebnis der Prüfung des Ausschlusslisten-Editors (ein Muster je Zeile).</summary>
public sealed record HelperPatternParseResult(IReadOnlyList<string> Patterns, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>Prüft und vereinheitlicht die pflegbare Ausschlussliste für Hilfsprozesse.</summary>
public static class HelperPatternList
{
    public const int MaxPatterns = 500;
    public const int MaxPatternLength = 128;

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars().Where(c => c is not '*' and not '?' and not '\\' and not '/').ToArray();

    /// <summary>Text der Standardliste (ein Muster je Zeile).</summary>
    public static string DefaultText { get; } = ToText(HelperExecutables.DefaultPatterns);

    public static string ToText(IEnumerable<string> patterns) => string.Join(Environment.NewLine, patterns);

    /// <summary>
    /// Zerlegt den Editor-Text: Leerzeilen und Leerraum fallen weg, Doppelte (ohne Groß-/Kleinschreibung) ebenso.
    /// Muster mit Ordnertrennern, unzulässigen Zeichen oder Überlänge sind Fehler (erster Fehler wird gemeldet).
    /// </summary>
    public static HelperPatternParseResult Parse(string? text)
    {
        var patterns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var pattern = lines[i].Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            var line = (i + 1).ToString(CultureInfo.CurrentCulture);
            if (pattern.Contains('\\', StringComparison.Ordinal) || pattern.Contains('/', StringComparison.Ordinal))
            {
                return Fail(patterns, string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternFolder, line, pattern));
            }

            if (pattern.IndexOfAny(InvalidChars) >= 0)
            {
                return Fail(patterns, string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternInvalidChars, line, pattern));
            }

            if (pattern.Length > MaxPatternLength)
            {
                return Fail(patterns, string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternTooLong, line, MaxPatternLength));
            }

            if (seen.Add(pattern))
            {
                patterns.Add(pattern);
            }
        }

        return patterns.Count > MaxPatterns
            ? Fail(patterns, string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternTooMany, MaxPatterns))
            : new HelperPatternParseResult(patterns, null);
    }

    /// <summary>True, wenn die Liste dieselben Muster wie die Standardliste enthält (Reihenfolge und Schreibweise egal).</summary>
    public static bool IsDefault(IReadOnlyCollection<string> patterns) =>
        patterns.Count == HelperExecutables.DefaultPatterns.Count
        && new HashSet<string>(patterns, StringComparer.OrdinalIgnoreCase).SetEquals(HelperExecutables.DefaultPatterns);

    /// <summary>Wert für die Konfiguration: null steht für die Standardliste (wächst mit künftigen Versionen mit).</summary>
    public static List<string>? ToConfigValue(IReadOnlyList<string> patterns) => IsDefault(patterns) ? null : [.. patterns];

    private static HelperPatternParseResult Fail(List<string> patterns, string error) => new(patterns, error);
}
