using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games;

/// <summary>
/// Gemeinsame Regeln für Symbol-Hinweise der Scanner (<see cref="GameEntry.IconCandidates"/>, E86): welche Dateiarten
/// in Frage kommen, wie Registry- und Verknüpfungswerte („DisplayIcon“, „IconFile“) zu lesen sind und dass nur
/// vorhandene Dateien eingetragen werden. Nur lesend.
/// </summary>
public static class IconFiles
{
    /// <summary>Höchstens so viele Hinweise je Spiel (hält config.json klein; meist ist es einer).</summary>
    public const int MaxCandidates = 4;

    /// <summary>Bilder, die die App mit den Windows-Bilddecodern (WPF/WIC) liest. WebP nur, wenn der Decoder installiert ist.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    /// <summary>True für Bilder (siehe <see cref="ImageExtensions"/>).</summary>
    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path ?? string.Empty), StringComparer.OrdinalIgnoreCase);

    /// <summary>True für .ico, .exe und Bilder: Dateien, aus denen die App ein Symbol lesen kann.</summary>
    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || IsImage(path!);
    }

    /// <summary>
    /// True für einen absoluten Pfad mit Laufwerksbuchstaben („C:\…“). UNC-Pfade (\\Server\…) und Gerätepfade (\\?\…) zählen
    /// nicht: Ein Symbol-Hinweis führt nie zu einem Zugriff auf einen fremden Rechner, auch wenn eine von Hand geänderte
    /// config.json dorthin zeigt.
    /// </summary>
    public static bool IsLocalPath(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    /// <summary>True für eine Symboldatei, die die App lesen darf: lokal (<see cref="IsLocalPath"/>) und unterstützt.</summary>
    public static bool IsReadableCandidate(string? path) => IsLocalPath(path) && IsSupported(path!);

    /// <summary>
    /// Pfad aus einem Symbolverweis wie „DisplayIcon“ (Uninstall) oder „IconFile“ (.url): Anführungszeichen und ein
    /// angehängter Index („,0“, „,-101“) fallen weg, Schrägstriche werden zu Backslashes (Ubisoft schreibt
    /// <c>C:/Program Files (x86)/…</c>). Null, wenn leer oder nicht absolut.
    /// </summary>
    public static string? ParseIconLocation(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            value = end > 0 ? value[1..end] : value.Trim('"');
        }
        else
        {
            var comma = value.LastIndexOf(',');
            if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                value = value[..comma];
            }
        }

        var path = PathUtil.Normalize(value);
        return path.Length > 0 && Path.IsPathFullyQualified(path) ? path : null;
    }

    /// <summary>
    /// Trägt eine vorhandene, lesbare Symboldatei ein (ohne Doppelte, höchstens <see cref="MaxCandidates"/>).
    /// True, wenn sie dazukam. Fehler beim Prüfen gelten als „fehlt“.
    /// </summary>
    public static bool AddExisting(List<string> candidates, IFileSystem fileSystem, string? path)
    {
        if (path is null || candidates.Count >= MaxCandidates)
        {
            return false;
        }

        var normalized = PathUtil.Normalize(path);
        if (!IsReadableCandidate(normalized) || candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (!fileSystem.FileExists(normalized))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }

        candidates.Add(normalized);
        return true;
    }
}
