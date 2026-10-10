using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>
/// Löst einen Logo-Namen aus <c>MicrosoftGame.config</c> oder <c>appxmanifest.xml</c> zu einer Datei auf (E86). Pakete
/// legen Logos oft nur mit Zusatz ab, etwa <c>Square44x44Logo.targetsize-48.png</c> oder <c>StoreLogo.scale-200.png</c>
/// (gemessen bei 61 von 77 Store-Paketen am Zielrechner). Gesucht wird erst der genaue Name, dann
/// <c>&lt;Name&gt;.*&lt;Endung&gt;</c> im selben Ordner; dort gewinnt der größte Zahlenwert hinter <c>scale-</c> bzw.
/// <c>targetsize-</c>. Kontrast-Varianten (<c>contrast-black</c>, <c>contrast-white</c>) zählen nicht.
/// </summary>
internal static class PackageAssets
{
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

            var score = Score(qualifier);
            if (score > bestScore || (score == bestScore && best is not null && name.Length < Path.GetFileName(best).Length))
            {
                best = file;
                bestScore = score;
            }
        }

        return best is null ? null : PathUtil.Normalize(best);
    }

    /// <summary>Größter Zahlenwert hinter „scale-“ oder „targetsize-“ im Zusatz (0, wenn keiner).</summary>
    internal static int Score(string qualifier)
    {
        var best = 0;
        foreach (var part in qualifier.Split('_', '.'))
        {
            foreach (var prefix in new[] { "scale-", "targetsize-" })
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(part[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    best = Math.Max(best, value);
                }
            }
        }

        return best;
    }
}
