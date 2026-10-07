using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games.Scanners.Common;

/// <summary>Gemeinsame Pfadregeln der Scanner für Installationsordner und EXE-Dateien.</summary>
internal static class InstallPaths
{
    /// <summary>
    /// Vereinheitlicht einen Installationsordner aus Launcher-Daten. Liefert null samt Begründung, wenn der Pfad leer,
    /// relativ oder eine Laufwerkswurzel ist: Später gilt jeder Prozess unter dem Ordner als Spiel, eine Wurzel wie
    /// "D:\" würde also jedes Programm auf dem Laufwerk erfassen.
    /// </summary>
    public static string? NormalizeInstallDir(string? raw, out string? problem)
    {
        var normalized = PathUtil.Normalize(raw ?? string.Empty);
        if (normalized.Length == 0)
        {
            problem = "Installationsordner ist leer.";
            return null;
        }

        if (!Path.IsPathFullyQualified(normalized))
        {
            problem = $"Installationsordner \"{normalized}\" ist kein absoluter Pfad.";
            return null;
        }

        var root = Path.GetPathRoot(normalized);
        if (root is not null && PathUtil.PathEquals(root, normalized))
        {
            problem = $"Installationsordner \"{normalized}\" ist eine Laufwerkswurzel.";
            return null;
        }

        problem = null;
        return normalized;
    }

    /// <summary>
    /// Löst einen von der Quelle genannten EXE-Pfad auf (relativ zum Installationsordner oder absolut, Schrägstriche
    /// erlaubt). Liefert nur eine vorhandene .exe-Datei unter dem Installationsordner, sonst null. Es wird nicht gesucht.
    /// </summary>
    public static string? ResolveExecutable(IFileSystem fileSystem, string installDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidate = path.Trim().Trim('"').Replace('/', '\\');
        if (!Path.IsPathFullyQualified(candidate))
        {
            candidate = installDir + "\\" + candidate.TrimStart('\\');
        }

        var full = PathUtil.Normalize(candidate);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !PathUtil.IsUnder(full, installDir))
        {
            return null;
        }

        return fileSystem.FileExists(full) ? full : null;
    }

    /// <summary>Fügt einen Pfad hinzu, wenn er nicht null ist und noch fehlt (ohne Groß-/Kleinschreibung).</summary>
    public static void AddDistinct(List<string> paths, string? path)
    {
        if (path is not null && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(path);
        }
    }
}
