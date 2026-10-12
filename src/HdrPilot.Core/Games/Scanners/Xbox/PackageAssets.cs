using System.Globalization;
using System.Text.RegularExpressions;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>
/// Löst einen Logo-Namen aus <c>MicrosoftGame.config</c> oder <c>appxmanifest.xml</c> zu einer Datei auf (E86). Pakete
/// legen Logos oft nur mit Zusatz ab, etwa <c>Square44x44Logo.targetsize-48.png</c> oder <c>StoreLogo.scale-200.png</c>
/// (gemessen bei 61 von 77 Store-Paketen am Zielrechner). Gesucht wird erst der genaue Name, dann
/// <c>&lt;Name&gt;.*&lt;Endung&gt;</c> im selben Ordner; dort gewinnt die größte Variante in Pixeln (<see cref="Pixels"/>).
/// Kontrast-Varianten (<c>contrast-black</c>, <c>contrast-white</c>) zählen nicht.
/// </summary>
internal static partial class PackageAssets
{
    /// <summary>
    /// Grundgröße für <c>scale-</c>, wenn der Name keine nennt: <c>targetsize-</c>-Varianten hat nur das kleine App-Symbol
    /// (Square44x44Logo, in Paketen oft „SmallLogo“ genannt). Gibt es nur <c>scale-</c>-Varianten, ändert die Grundgröße
    /// die Reihenfolge nicht.
    /// </summary>
    private const int DefaultBaseSize = 44;

    /// <summary>Vorhandene Datei unter <paramref name="packageDir"/> oder null (nie außerhalb des Paketordners).</summary>
    public static string? Resolve(IFileSystem fileSystem, string packageDir, string relative)
    {
        var rel = relative.Replace('/', '\\').Trim();
        if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Contains(':', StringComparison.Ordinal) || rel.IndexOfAny(['*', '?']) >= 0)
        {
            return null;
        }

        var full = PathUtil.Normalize(Path.Combine(packageDir, rel));
        if (!PathUtil.IsUnder(full, packageDir) || PathUtil.PathEquals(full, packageDir))
        {
            return null;
        }

        if (fileSystem.FileExists(full))
        {
            return full;
        }

        var dir = Path.GetDirectoryName(full);
        var stem = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        if (dir is null || stem.Length == 0 || extension.Length == 0)
        {
            return null;
        }

        string? best = null;
        var bestScore = -1;
        foreach (var file in fileSystem.EnumerateFiles(dir, stem + ".*" + extension).Take(64))
        {
            var name = Path.GetFileName(file);
            if (name.Length <= stem.Length + extension.Length + 1)
            {
                continue;
            }

            var qualifier = name[(stem.Length + 1)..^extension.Length];
            if (qualifier.Contains("contrast-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = Pixels(stem, qualifier);
            if (score > bestScore || (score == bestScore && best is not null && name.Length < Path.GetFileName(best).Length))
            {
                best = file;
                bestScore = score;
            }
        }

        return best is null ? null : PathUtil.Normalize(best);
    }

    /// <summary>
    /// Kantenlänge einer Variante in Pixeln, damit <c>scale-</c> (Prozent) und <c>targetsize-</c> (Pixel) vergleichbar sind:
    /// <c>targetsize-48</c> sind 48 px, <c>scale-100</c> ist die Grundgröße aus dem Namen (Square150x150Logo: 150 px, sonst
    /// <see cref="DefaultBaseSize"/>), <c>scale-200</c> das Doppelte. 0, wenn der Zusatz keine Größe nennt.
    /// </summary>
    internal static int Pixels(string stem, string qualifier)
    {
        var match = SizeInName().Match(stem);
        var baseSize = match.Success && int.TryParse(match.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var named)
            ? named
            : DefaultBaseSize;
        var best = 0;
        foreach (var part in qualifier.Split('_', '.'))
        {
            if (Number(part, "targetsize-") is { } pixels)
            {
                best = Math.Max(best, pixels);
            }
            else if (Number(part, "scale-") is { } percent)
            {
                best = Math.Max(best, (int)Math.Min(int.MaxValue, (long)baseSize * percent / 100));
            }
        }

        return best;
    }

    private static int? Number(string part, string prefix) =>
        part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(part[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    [GeneratedRegex(@"(?<size>\d{1,4})x\d{1,4}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SizeInName();
}
