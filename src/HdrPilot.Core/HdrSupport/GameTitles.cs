using System.Text;

namespace HdrPilot.Core.HdrSupport;

/// <summary>
/// Spielnamen für den Abgleich mit PCGamingWiki: bereinigen (ohne ™ ® ©, einheitliche Anführungszeichen, Striche und
/// Leerzeichen), vergleichen (zusätzlich ohne Groß-/Kleinschreibung) und wenige sichere Titelvarianten bilden.
/// </summary>
public static class GameTitles
{
    /// <summary>Höchstlänge eines Wiki-Titels in Byte (UTF-8), wie MediaWiki sie verlangt.</summary>
    public const int MaxTitleBytes = 255;

    /// <summary>Bereinigter Name für Anfragen; Groß-/Kleinschreibung bleibt (Wiki-Titel unterscheiden sie).</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            char? mapped = c switch
            {
                '™' or '®' or '©' or '℠' => null, // ™ ® © ℠
                '‘' or '’' or '‚' or '‛' or '´' or '`' or '′' => '\'', // ‘ ’ ‚ ‛ ´ ` ′
                '“' or '”' or '„' or '‟' or '″' or '«' or '»' => '"', // “ ” „ ‟ ″ « »
                '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-', // Bindestriche, Gedankenstriche, Minus
                _ when char.IsWhiteSpace(c) || char.IsControl(c) => ' ',
                _ => c,
            };
            if (mapped is not { } m)
            {
                continue;
            }

            if (m == ' ')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(m);
        }

        return sb.ToString();
    }

    /// <summary>Vergleichsschlüssel: bereinigt, kompatibel zerlegt (NFKC, etwa „Ⅱ“ = „II“) und kleingeschrieben.</summary>
    public static string Normalize(string? name)
    {
        var clean = Clean(name);
        return clean.Length == 0 ? clean : Clean(clean.Normalize(NormalizationForm.FormKC)).ToLowerInvariant();
    }

    /// <summary>
    /// Grober Vergleichsschlüssel: nur Buchstaben und Ziffern des Vergleichsschlüssels („Avatar: Frontiers of
    /// Pandora“ = „Avatar Frontiers of Pandora™“). Nur als Gegenprobe für einen Suchtreffer bzw. eindeutigen
    /// Listeneintrag, nie als alleinige Grundlage einer Anfrage.
    /// </summary>
    public static string LooseKey(string? name)
    {
        var normalized = Normalize(name);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Gleicher Titel bis auf Satzzeichen, Leerzeichen, Symbole und Groß-/Kleinschreibung.</summary>
    public static bool LooselyEqual(string? a, string? b)
    {
        var key = LooseKey(a);
        return key.Length > 0 && string.Equals(key, LooseKey(b), StringComparison.Ordinal);
    }

    /// <summary>
    /// Titel, unter denen das Spiel im Wiki stehen kann, in dieser Reihenfolge: der bereinigte Name und, wenn er
    /// „ - “ enthält, dieselbe Stelle mit Doppelpunkt („Dying Light - The Beast“ → „Dying Light: The Beast“).
    /// Weitergehende Umformungen sind nicht sicher; dafür gibt es Weiterleitungen im Wiki und die Suche mit Gegenprobe.
    /// </summary>
    public static IReadOnlyList<string> Variants(string? name)
    {
        var clean = Clean(name);
        var result = new List<string>(2);
        Add(clean);
        var dash = clean.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            Add(string.Concat(clean.AsSpan(0, dash), ": ", clean.AsSpan(dash + 3)));
        }

        return result;

        void Add(string title)
        {
            if (IsValidTitle(title) && !result.Contains(title, StringComparer.Ordinal))
            {
                result.Add(title);
            }
        }
    }

    /// <summary>
    /// Zulässiger Wiki-Titel: nicht leer, höchstens 255 Byte, ohne # &lt; &gt; [ ] | { } und ohne %-Kodierung.
    /// „|“ trennt in der Anfrage die Titel und darf deshalb nie vorkommen.
    /// </summary>
    public static bool IsValidTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || Encoding.UTF8.GetByteCount(title) > MaxTitleBytes || title.StartsWith(':'))
        {
            return false;
        }

        for (var i = 0; i < title.Length; i++)
        {
            var c = title[i];
            if (c is '#' or '<' or '>' or '[' or ']' or '|' or '{' or '}' || char.IsControl(c))
            {
                return false;
            }

            if (c == '%' && i + 2 < title.Length && char.IsAsciiHexDigit(title[i + 1]) && char.IsAsciiHexDigit(title[i + 2]))
            {
                return false;
            }
        }

        return true;
    }
}
