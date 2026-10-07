using HdrPilot.Core.Abstractions;
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
    /// darunter oder darüber liegen, ebenso. Nicht lesbare Unterordner werden ausgelassen.
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

            foreach (var folder in SafeDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(folder)
                    || roots.Any(r => PathUtil.PathEquals(r, folder))
                    || known.Any(k => PathUtil.IsUnder(folder, k) || PathUtil.IsUnder(k, folder)))
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
