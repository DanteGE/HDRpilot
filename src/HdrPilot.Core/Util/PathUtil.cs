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

    /// <summary>
    /// True, wenn beide Pfade denselben Ordner meinen, auch in Schreibweisen, die <see cref="PathEquals"/> nicht erkennt: Präfix
    /// <c>\\?\</c>, kurze 8.3-Namen, Junctions und symbolische Verknüpfungen, mit subst verbundene Laufwerke. Gibt es beide,
    /// entscheidet ihre Kennung (Datenträger und Dateiindex, nur lesend geöffnet); sonst der Vergleich der Pfade. Wirft nicht.
    /// </summary>
    public static bool SameDirectory(string a, string b)
    {
        if (PathEquals(WithoutLongPathPrefix(a), WithoutLongPathPrefix(b)))
        {
            return true;
        }

        try
        {
            return Launchers.FileIdentity.OfPath(a) is { } first && Launchers.FileIdentity.OfPath(b) is { } second && first == second;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary><c>\\?\C:\…</c> als <c>C:\…</c>, <c>\\?\UNC\server\…</c> als <c>\\server\…</c>; sonst unverändert.</summary>
    private static string WithoutLongPathPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
