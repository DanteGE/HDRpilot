using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Quiver;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games;

/// <summary>
/// Schlägt Spiele aus vom Nutzer angegebenen Suchordnern vor. Jeder direkte Unterordner eines Suchordners ist ein
/// Kandidat, wenn er bis Tiefe <see cref="MaxDepth"/> mindestens eine EXE enthält, die kein Hilfsprogramm ist.
/// Nur Vorschläge: Übernommen wird ein Kandidat erst, wenn der Nutzer ihn bestätigt (dann über <see cref="ManualGames"/>).
/// </summary>
public sealed class SearchFolderCandidateFinder(IFileSystem fileSystem)
{
    /// <summary>
    /// Suchtiefe ab dem Kandidatenordner: 0 ist der Ordner selbst, 3 erreicht etwa
    /// <c>&lt;Spiel&gt;\&lt;Projekt&gt;\Binaries\Win64\*.exe</c> (Unreal Engine).
    /// </summary>
    public const int MaxDepth = 3;

    /// <summary>
    /// Liefert die Kandidaten aller Suchordner in Eingabereihenfolge, je Suchordner nach Name sortiert.
    /// Fehlende Suchordner werden still übersprungen; Ordner, die einem bekannten Installationsordner entsprechen,
    /// darunter oder darüber liegen, ebenso. Nicht lesbare Unterordner werden ausgelassen. Ist der Suchordner eine
    /// Laufwerkswurzel, sind Windows-Systemordner (<c>$RECYCLE.BIN</c> u. ä., <c>System Volume Information</c>) keine Kandidaten.
    /// Der Ordner des Quiver Launchers und alles darin ist nie ein Kandidat (E91): Quiver selbst ist kein Spiel, seine Spiele
    /// liest der Quiver-Scanner.
    /// </summary>
    public IReadOnlyList<GameCandidate> FindCandidates(
        IReadOnlyList<string> searchFolders, IEnumerable<string> knownInstallDirs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchFolders);
        ArgumentNullException.ThrowIfNull(knownInstallDirs);

        var known = knownInstallDirs
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(PathUtil.Normalize)
            .Where(Path.IsPathFullyQualified)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var roots = searchFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(PathUtil.Normalize)
            .Where(Path.IsPathFullyQualified)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<GameCandidate>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileSystem.DirectoryExists(root))
            {
                continue;
            }

            if (InsideQuiver(root))
            {
                continue;
            }

            var isDriveRoot = PathUtil.PathEquals(Path.GetPathRoot(root) ?? string.Empty, root);
            foreach (var folder in SafeDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(folder)
                    || (isDriveRoot && IsWindowsSystemFolder(Path.GetFileName(folder)))
                    || roots.Any(r => PathUtil.PathEquals(r, folder))
                    || known.Any(k => PathUtil.IsUnder(folder, k) || PathUtil.IsUnder(k, folder))
                    || QuiverRoot.Detect(fileSystem, folder, requireLibrary: false) != QuiverLayout.None)
                {
                    continue;
                }

                var executables = FindExecutables(folder, cancellationToken);
                if (executables.Count > 0)
                {
                    candidates.Add(new GameCandidate
                    {
                        Name = Path.GetFileName(folder),
                        Folder = folder,
                        Executables = executables,
                    });
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Breitensuche bis <see cref="MaxDepth"/>: oberste Ebene zuerst. Innerhalb einer Ebene stehen EXEs vorn, die wie
    /// der Ordner heißen (etwa „Swat4.exe“ in „SWAT 4“), dann ähnlich benannte, danach alle übrigen nach Pfad sortiert.
    /// </summary>
    private List<string> FindExecutables(string folder, CancellationToken cancellationToken)
    {
        var folderKey = Key(Path.GetFileName(folder));
        var result = new List<string>();
        var level = new List<string> { folder };
        for (var depth = 0; depth <= MaxDepth && level.Count > 0; depth++)
        {
            var found = new List<string>();
            var next = new List<string>();
            foreach (var dir in level)
            {
                cancellationToken.ThrowIfCancellationRequested();
                found.AddRange(SafeFiles(dir)
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !HelperExecutables.IsHelper(f)));
                if (depth < MaxDepth)
                {
                    next.AddRange(SafeDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase));
                }
            }

            result.AddRange(found
                .OrderBy(f => Resemblance(f, folderKey))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase));
            level = next;
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// True, wenn der Suchordner ein Quiver-Ordner ist oder darin liegt (etwa dessen Apps-Ordner): geprüft am Kennzeichen von
    /// Quiver im Suchordner und bis zu drei Ordnern darüber.
    /// </summary>
    private bool InsideQuiver(string folder)
    {
        var current = folder;
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(current); i++)
        {
            if (QuiverRoot.Detect(fileSystem, current, requireLibrary: false) != QuiverLayout.None)
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            current = parent is null || PathUtil.PathEquals(parent, current) ? null : parent;
        }

        return false;
    }

    /// <summary>
    /// Ordner, die Windows in Laufwerkswurzeln anlegt: Papierkorb (<c>$RECYCLE.BIN</c>, enthält gelöschte Spielordner
    /// samt EXEs), Update-Reste (<c>$WINDOWS.~BT</c>, <c>$WinREAgent</c> …) und <c>System Volume Information</c>.
    /// </summary>
    private static bool IsWindowsSystemFolder(string name) =>
        name.StartsWith('$') || string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase);

    /// <summary>0: heißt wie der Ordner, 1: Name beginnt mit dem Ordnernamen oder umgekehrt, 2: sonst.</summary>
    private static int Resemblance(string exePath, string folderKey)
    {
        var stem = Key(Path.GetFileNameWithoutExtension(exePath));
        if (folderKey.Length < 3 || stem.Length < 3)
        {
            return 2;
        }

        if (stem == folderKey)
        {
            return 0;
        }

        return stem.StartsWith(folderKey, StringComparison.Ordinal) || folderKey.StartsWith(stem, StringComparison.Ordinal) ? 1 : 2;
    }

    /// <summary>Nur Buchstaben und Ziffern, klein: „SWAT 4“ und „Swat4“ werden beide zu „swat4“.</summary>
    private static string Key(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private List<string> SafeFiles(string directory)
    {
        try
        {
            return fileSystem.EnumerateFiles(directory, "*.exe").Select(PathUtil.Normalize).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private List<string> SafeDirectories(string directory)
    {
        try
        {
            return fileSystem.EnumerateDirectories(directory).Select(PathUtil.Normalize).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
