using System.Globalization;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>Ergebnis der Wahl der Start-EXE eines Quiver-Spiels.</summary>
/// <param name="Installed">Wie in Quiver: Ordner da, keine <c>install-incomplete.txt</c>, mindestens eine EXE, BAT oder CMD.</param>
/// <param name="Chosen">Die EXE, die „Mit HDR starten“ startet; null ohne eindeutige Wahl (E61).</param>
/// <param name="Executables">Bekannte EXE-Dateien (die gewählte allein, sonst alle Kandidaten ohne Hilfsprogramme).</param>
/// <param name="Note">Warum es keine Wahl gibt (für das Protokoll); null mit Wahl.</param>
internal sealed record QuiverExeChoice(bool Installed, string? Chosen, IReadOnlyList<string> Executables, string? Note)
{
    public static QuiverExeChoice NotInstalled(string note) => new(false, null, [], note);
}

/// <summary>
/// Wählt die Start-EXE eines Quiver-Spiels (E91), angelehnt an Quiver selbst, aber nur lesend und begrenzt:
/// <list type="number">
/// <item><c>selected_executable.txt</c> im Spielordner (die Wahl des Nutzers in Quiver), nur wenn sie auf eine vorhandene
/// Datei im Spielordner zeigt; eine gewählte BAT/CMD heißt: kein Start-Befehl (HDRpilot startet nur EXE-Dateien).</item>
/// <item>sonst die einzige EXE oben im Spielordner;</item>
/// <item>gibt es oben keine, die einzige bis <see cref="MaxDepth"/> Ebenen tief.</item>
/// </list>
/// Hilfsprogramme (<see cref="HelperExecutables"/>) zählen nicht mit. Bleiben mehrere, gibt es keinen Start-Befehl; die
/// Erkennung beim Spielen wirkt über den Spielordner trotzdem. Gesucht wird nur nach Namen, ohne verknüpften Ordnern zu
/// folgen, ohne Ordner von Wine/Proton (<c>.wine-prefix</c>, <c>.steam-compat-data</c>, <c>dosdevices</c>, <c>drive_c</c>) und
/// höchstens <see cref="MaxListedFiles"/> Dateien (bei „Black“ liegt ein Abbild mit 7,8 GB im Ordner; es wird nicht gelesen).
/// </summary>
internal static class QuiverExecutables
{
    /// <summary>Ebenen unter dem Spielordner (0: der Ordner selbst).</summary>
    public const int MaxDepth = 3;

    /// <summary>Höchstens so viele Dateien werden aufgelistet.</summary>
    public const int MaxListedFiles = 5000;

    /// <summary>Höchstens so viele EXE-Dateien kommen in die Liste des Spiels.</summary>
    public const int MaxExecutables = 16;

    public const string IncompleteMarker = "install-incomplete.txt";

    public const string SelectedExecutableFile = "selected_executable.txt";

    public const string LastPlayedFile = "LastPlayed.txt";

    /// <summary>Kurze Textdateien von Quiver; mehr wird nie gelesen.</summary>
    public const int MaxSmallFileBytes = 4096;

    private static readonly HashSet<string> CompatibilityFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wine-prefix", ".steam-compat-data", "dosdevices", "drive_c",
    };

    private static readonly string[] LastPlayedFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH.mm.ss", "yyyy-MM-dd'T'HH:mm:ss"];

    public static QuiverExeChoice Choose(IFileSystem fileSystem, string gameFolder, Func<string, bool> isHelper)
    {
        if (!fileSystem.DirectoryExists(gameFolder))
        {
            return QuiverExeChoice.NotInstalled("Spielordner fehlt");
        }

        if (fileSystem.FileExists(Path.Combine(gameFolder, IncompleteMarker)))
        {
            return QuiverExeChoice.NotInstalled("Installation unvollständig (" + IncompleteMarker + ")");
        }

        var files = ListFiles(fileSystem, gameFolder);
        var launchable = files.Where(f => HasExtension(f.Path, ".exe", ".bat", ".cmd")).ToList();
        if (launchable.Count == 0)
        {
            return QuiverExeChoice.NotInstalled("keine EXE, BAT oder CMD im Spielordner");
        }

        var exes = launchable.Where(f => HasExtension(f.Path, ".exe") && !isHelper(Path.GetFileName(f.Path)))
            .OrderBy(f => f.Depth)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var listed = exes.Select(f => f.Path).Take(MaxExecutables).ToList();

        switch (Selected(fileSystem, gameFolder))
        {
            case { } selected when HasExtension(selected, ".exe"):
                return new QuiverExeChoice(true, selected, [selected], null);
            case { } selected:
                return new QuiverExeChoice(true, null, listed, "in Quiver gewählt ist keine EXE: " + Path.GetFileName(selected));
        }

        var top = exes.Where(f => f.Depth == 0).ToList();
        var pool = top.Count > 0 ? top : exes;
        return pool.Count switch
        {
            1 => new QuiverExeChoice(true, pool[0].Path, [pool[0].Path], null),
            0 => new QuiverExeChoice(true, null, listed, "keine Spiel-EXE (nur Hilfsprogramme, BAT oder CMD)"),
            _ => new QuiverExeChoice(true, null, listed, $"{pool.Count} EXE-Dateien und keine Wahl in Quiver"),
        };
    }

    /// <summary>
    /// Zuletzt gespielt laut Quiver: <c>LastPlayed.txt</c> im Ordner der EXE (dort schreibt Quiver beim Start) oder im
    /// Spielordner, die spätere zählt. Ortszeit im Format „yyyy-MM-dd HH:mm:ss“; Unlesbares zählt nicht.
    /// </summary>
    public static DateTimeOffset? LastPlayed(IFileSystem fileSystem, IEnumerable<string?> folders)
    {
        DateTimeOffset? latest = null;
        foreach (var folder in folders.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ReadSmallText(fileSystem, Path.Combine(folder, LastPlayedFile)) is { } text
                && DateTime.TryParseExact(text, LastPlayedFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            {
                var value = new DateTimeOffset(local);
                latest = latest is null || value > latest ? value : latest;
            }
        }

        return latest;
    }

    /// <summary>
    /// Die Wahl aus selected_executable.txt: vorhandene Datei im Spielordner (relativ oder absolut angegeben); sonst null
    /// (fehlt, unlesbar, außerhalb des Spielordners, gelöscht).
    /// </summary>
    private static string? Selected(IFileSystem fileSystem, string gameFolder)
    {
        if (ReadSmallText(fileSystem, Path.Combine(gameFolder, SelectedExecutableFile)) is not { Length: > 0 } raw)
        {
            return null;
        }

        var candidate = raw.Trim('"').Replace('/', '\\');
        try
        {
            if (!Path.IsPathFullyQualified(candidate))
            {
                candidate = Path.Combine(gameFolder, candidate.TrimStart('\\'));
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        var full = PathUtil.Normalize(candidate);
        return PathUtil.IsUnder(full, gameFolder) && !PathUtil.PathEquals(full, gameFolder) && fileSystem.FileExists(full) ? full : null;
    }

    /// <summary>Kurze Textdatei geteilt lesen (UTF-8, Leerraum entfernt); fehlt, zu groß oder unlesbar: null.</summary>
    internal static string? ReadSmallText(IFileSystem fileSystem, string path)
    {
        try
        {
            if (!fileSystem.FileExists(path))
            {
                return null;
            }

            var read = fileSystem.ReadShared(path, MaxSmallFileBytes);
            return read.IsComplete ? new UTF8Encoding(false).GetString(read.Bytes).Trim('﻿', ' ', '\t', '\r', '\n') : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Dateien bis <see cref="MaxDepth"/> Ebenen samt Tiefe, ohne Ordner von Wine/Proton, höchstens <see cref="MaxListedFiles"/>.</summary>
    private static List<(string Path, int Depth)> ListFiles(IFileSystem fileSystem, string gameFolder)
    {
        try
        {
            return fileSystem.EnumerateFilesUpTo(gameFolder, "*", MaxDepth)
                .Take(MaxListedFiles)
                .Select(f => (Path: PathUtil.Normalize(f), Relative: Path.GetRelativePath(gameFolder, f)))
                .Where(f => !f.Relative.Split(Path.DirectorySeparatorChar).SkipLast(1).Any(CompatibilityFolders.Contains))
                .Select(f => (f.Path, f.Relative.Count(c => c == Path.DirectorySeparatorChar)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static bool HasExtension(string path, params string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
