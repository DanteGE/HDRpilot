namespace HdrPilot.Core.Util;

public static class PathUtil
{
    /// <summary>
    /// Vereinheitlicht einen Windows-Pfad: Schrägstriche zu Backslashes, doppelte Trenner entfernt,
    /// kein abschließender Trenner (außer bei Laufwerkswurzeln wie "C:\"). Wirft nicht.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var p = path.Trim().Trim('"').Replace('/', '\\');
        var isUnc = p.StartsWith(@"\\", StringComparison.Ordinal);
        while (p.Contains(@"\\", StringComparison.Ordinal))
        {
            p = p.Replace(@"\\", @"\", StringComparison.Ordinal);
        }

        if (isUnc)
        {
            p = @"\" + p;
        }

        try
        {
            if (Path.IsPathFullyQualified(p))
            {
                p = Path.GetFullPath(p);
            }
        }
        catch (ArgumentException)
        {
            // Ungültige Zeichen: Pfad unverändert weiterreichen.
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        if (p.Length > 3 && p.EndsWith('\\'))
        {
            p = p.TrimEnd('\\');
        }

        if (p.Length == 2 && p[1] == ':')
        {
            p += "\\";
        }

        return p;
    }

    /// <summary>True, wenn <paramref name="path"/> gleich <paramref name="directory"/> ist oder darunter liegt. Ohne Groß-/Kleinschreibung.</summary>
    public static bool IsUnder(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var p = Normalize(path);
        var d = Normalize(directory);
        if (p.Length < d.Length)
        {
            return false;
        }

        if (p.Length == d.Length)
        {
            return string.Equals(p, d, StringComparison.OrdinalIgnoreCase);
        }

        if (!p.StartsWith(d, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return d.EndsWith('\\') || p[d.Length] == '\\';
    }

    public static bool PathEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
}
