using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games;

// PLATZHALTER für die Arbeitskopie phase3/session. Die echte Fassung kommt aus phase2/xbox-manual;
// beim Zusammenführen wird diese Datei durch sie ersetzt.
public static class ManualGames
{
    public static GameEntry Create(IFileSystem fileSystem, string path, string? name = null, string? id = null)
    {
        var p = PathUtil.Normalize(path);
        if (fileSystem.FileExists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(p)!;
            return new GameEntry
            {
                Source = GameSource.Manual,
                SourceId = id ?? Guid.NewGuid().ToString("N"),
                Name = name ?? Path.GetFileNameWithoutExtension(p),
                InstallDir = dir,
                Executables = [p],
                LaunchCommand = p,
                LaunchWorkingDirectory = dir,
            };
        }

        if (fileSystem.DirectoryExists(p))
        {
            var exes = fileSystem.EnumerateFiles(p, "*.exe").ToList();
            return new GameEntry
            {
                Source = GameSource.Manual,
                SourceId = id ?? Guid.NewGuid().ToString("N"),
                Name = name ?? Path.GetFileName(p),
                InstallDir = p,
                Executables = exes,
                LaunchCommand = exes.Count == 1 ? exes[0] : null,
                LaunchWorkingDirectory = exes.Count == 1 ? p : null,
            };
        }

        throw new ArgumentException("Pfad nicht gefunden: " + path, nameof(path));
    }
}
