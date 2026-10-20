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
/// <item>sonst der einzige Kandidat oben im Spielordner: eine EXE, ein Startskript (BAT/CMD) zählt wie bei Quiver mit;</item>
/// <item>gibt es oben keinen, der einzige bis <see cref="MaxDepth"/> Ebenen tief.</item>
/// </list>
/// Hilfsprogramme (<see cref="HelperExecutables"/>) zählen nicht mit. Ist der einzige Kandidat ein Skript oder bleiben
/// mehrere, gibt es keinen Start-Befehl: Quiver startet dann das Skript bzw. fragt, und HDRpilot startet nie eine andere
/// Datei als Quiver. Die Erkennung beim Spielen wirkt über den Spielordner trotzdem.
/// <para>Gesucht wird Ebene für Ebene nur nach Namen, ohne verknüpften Ordnern zu folgen; Ordner von Wine/Proton
/// (<c>.wine-prefix</c>, <c>.steam-compat-data</c>, <c>dosdevices</c>, <c>drive_c</c>) betritt die Suche gar nicht. Für
/// „installiert“ sucht sie wie Quiver auch tiefer (bis <see cref="MaxInstallDepth"/>), aber nur, solange bis
/// <see cref="MaxDepth"/> nichts liegt. Gezählt werden nur EXE, BAT und CMD (höchstens <see cref="MaxCandidates"/>), andere
/// Dateien kosten nichts (bei „Black“ liegt ein Abbild mit 7,8 GB im Ordner; es wird nie geöffnet). Höchstens
/// <see cref="MaxFolders"/> Ordner werden aufgelistet; endet die Suche dort, gilt das Spiel als installiert ohne Start-Befehl,
/// nie als „nicht installiert“.</para>
/// </summary>
internal static class QuiverExecutables
{
    /// <summary>Ebenen unter dem Spielordner (0: der Ordner selbst), in denen die Start-EXE gesucht wird.</summary>
    public const int MaxDepth = 3;

    /// <summary>Bis zu dieser Ebene sucht die Prüfung „installiert“ (Quiver selbst sucht ohne Grenze).</summary>
    public const int MaxInstallDepth = 16;

    /// <summary>Höchstens so viele Ordner werden je Spiel aufgelistet.</summary>
    public const int MaxFolders = 3000;

    /// <summary>Höchstens so viele Kandidaten (EXE, BAT, CMD) werden je Spiel gesammelt.</summary>
    public const int MaxCandidates = 256;

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

    /// <summary>Frühester glaubwürdiger Wert in <c>LastPlayed.txt</c> (Quiver gibt es erst seit 2025).</summary>
    private static readonly DateTimeOffset EarliestLastPlayed = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Kandidaten der Suche samt Ebene; <see cref="Truncated"/>: an einer Grenze beendet, die Liste kann fehlen.</summary>
    private sealed record Listing(List<(string Path, int Depth)> Candidates, bool Truncated);

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

        // Die Wahl in Quiver zuerst: Sie gilt, wie viele Dateien der Ordner auch hat.
        var selected = Selected(fileSystem, gameFolder);
        if (selected is not null && HasExtension(selected, ".exe"))
        {
            return new QuiverExeChoice(true, selected, [selected], null);
        }

        var listing = ListCandidates(fileSystem, gameFolder);
        bool IsGameExe((string Path, int Depth) c) => HasExtension(c.Path, ".exe") && !isHelper(Path.GetFileName(c.Path));
        bool IsScript((string Path, int Depth) c) => HasExtension(c.Path, ".bat", ".cmd");
        var shallow = listing.Candidates.Where(c => c.Depth <= MaxDepth).ToList();
        var listed = (shallow.Count > 0 ? shallow : listing.Candidates).Where(IsGameExe)
            .OrderBy(c => c.Depth)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Path)
            .Take(MaxExecutables)
            .ToList();

        if (selected is not null)
        {
            return new QuiverExeChoice(true, null, listed, "in Quiver gewählt ist keine EXE: " + Path.GetFileName(selected));
        }

        if (listing.Candidates.Count == 0)
        {
            return listing.Truncated
                ? new QuiverExeChoice(true, null, [], $"Suche nach {MaxFolders} Ordnern beendet, keine EXE gefunden")
                : QuiverExeChoice.NotInstalled("keine EXE, BAT oder CMD im Spielordner");
        }

        if (shallow.Count == 0)
        {
            return new QuiverExeChoice(true, null, listed, $"EXE erst tiefer als {MaxDepth} Ebenen");
        }

        // Wie Quiver: oben zählt jede EXE und jedes Skript; nur wenn oben keiner liegt, die Ebenen darunter.
        var top = shallow.Where(c => c.Depth == 0 && (IsGameExe(c) || IsScript(c))).ToList();
        if (top.Count == 0 && listing.Truncated)
        {
            return new QuiverExeChoice(true, null, listed, $"Suche nach {MaxFolders} Ordnern beendet, Wahl nicht eindeutig");
        }

        var pool = top.Count > 0 ? top : shallow.Where(c => IsGameExe(c) || IsScript(c)).ToList();
        return pool switch
        {
            [var only] when IsGameExe(only) => new QuiverExeChoice(true, only.Path, [only.Path], null),
            [var only] => new QuiverExeChoice(true, null, listed, "Quiver startet " + Path.GetFileName(only.Path) + ", HDRpilot startet keine Skripte"),
            [] => new QuiverExeChoice(true, null, listed, "keine Spiel-EXE (nur Hilfsprogramme)"),
            _ => new QuiverExeChoice(true, null, listed, $"{pool.Count} Kandidaten und keine Wahl in Quiver"),
        };
    }

    /// <summary>
    /// Zuletzt gespielt laut Quiver: <c>LastPlayed.txt</c> im Ordner der EXE (dort schreibt Quiver beim Start) oder im
    /// Spielordner, die spätere zählt. Ortszeit im Format „yyyy-MM-dd HH:mm:ss“. Unlesbares zählt nicht, ebenso Werte vor
    /// 2000 oder mehr als einen Tag nach <paramref name="now"/>: Quiver schreibt im Kalender der Kultur, unter Thai etwa das
    /// Jahr 2569, das sonst für immer „zuletzt gespielt“ bliebe.
    /// </summary>
    public static DateTimeOffset? LastPlayed(IFileSystem fileSystem, IEnumerable<string?> folders, DateTimeOffset? now = null)
    {
        var latestPlausible = (now ?? DateTimeOffset.Now).AddDays(1);
        DateTimeOffset? latest = null;
        foreach (var folder in folders.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ReadSmallText(fileSystem, Path.Combine(folder, LastPlayedFile)) is { } text
                && DateTime.TryParseExact(text, LastPlayedFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            {
                var value = new DateTimeOffset(local);
                if (value < EarliestLastPlayed || value > latestPlausible)
                {
                    continue;
                }

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

    /// <summary>
    /// EXE, BAT und CMD samt Ebene, Ebene für Ebene (siehe Klassenbeschreibung): bis <see cref="MaxDepth"/> alle, danach nur
    /// bis zur ersten Ebene mit einem Fund. Verknüpfte Ordner und Ordner von Wine/Proton werden nie betreten, nicht lesbare
    /// übersprungen.
    /// </summary>
    private static Listing ListCandidates(IFileSystem fileSystem, string gameFolder)
    {
        var candidates = new List<(string Path, int Depth)>();
        var level = new List<string> { gameFolder };
        var listed = 0;
        for (var depth = 0; depth <= MaxInstallDepth && level.Count > 0; depth++)
        {
            if (depth > MaxDepth && candidates.Count > 0)
            {
                break;
            }

            var next = new List<string>();
            foreach (var folder in level)
            {
                if (++listed > MaxFolders)
                {
                    return new Listing(candidates, Truncated: true);
                }

                foreach (var file in Files(fileSystem, folder))
                {
                    if (HasExtension(file, ".exe", ".bat", ".cmd"))
                    {
                        candidates.Add((PathUtil.Normalize(file), depth));
                        if (candidates.Count >= MaxCandidates)
                        {
                            return new Listing(candidates, Truncated: true);
                        }
                    }
                }

                if (depth < MaxInstallDepth)
                {
                    next.AddRange(Subfolders(fileSystem, folder).Where(d => !CompatibilityFolders.Contains(Path.GetFileName(d))));
                }
            }

            level = next;
        }

        return new Listing(candidates, Truncated: false);
    }

    private static List<string> Files(IFileSystem fileSystem, string folder)
    {
        try
        {
            return fileSystem.EnumerateFiles(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static List<string> Subfolders(IFileSystem fileSystem, string folder)
    {
        try
        {
            return fileSystem.EnumerateDirectoriesWithoutLinks(folder).Select(PathUtil.Normalize).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static bool HasExtension(string path, params string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
