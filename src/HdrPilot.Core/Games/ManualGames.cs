using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games;

/// <summary>Legt manuelle Einträge an: eine EXE oder ein Ordner, Name frei wählbar.</summary>
public static class ManualGames
{
    /// <summary>
    /// Erzeugt einen manuellen Eintrag.
    /// <list type="bullet">
    /// <item>EXE: Installationsordner ist ihr Ordner, Start über die EXE mit diesem Arbeitsordner.</item>
    /// <item>Ordner: EXE-Dateien der obersten Ebene ohne Hilfsprogramme; Start-Befehl nur, wenn es genau eine gibt.</item>
    /// </list>
    /// Name standardmäßig Ordner- bzw. Dateiname ohne Endung, ID standardmäßig eine neue GUID (Format N).
    /// </summary>
    /// <exception cref="ArgumentException">Pfad fehlt, ist relativ, existiert nicht, ist keine EXE oder eine Laufwerkswurzel.</exception>
    public static GameEntry Create(IFileSystem fileSystem, string path, string? name = null, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Es wurde kein Pfad angegeben.", nameof(path));
        }

        var full = PathUtil.Normalize(path);
        if (!Path.IsPathFullyQualified(full))
        {
            throw new ArgumentException($"Der Pfad muss vollständig sein (mit Laufwerk), nicht relativ: \"{path}\".", nameof(path));
        }

        var sourceId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();

        if (fileSystem.FileExists(full))
        {
            if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Nur EXE-Dateien oder Ordner können hinzugefügt werden: \"{full}\".", nameof(path));
            }

            var folder = PathUtil.Normalize(Path.GetDirectoryName(full) ?? string.Empty);
            if (IsDriveRoot(folder))
            {
                throw new ArgumentException(DriveRootMessage(path), nameof(path));
            }

            return new GameEntry
            {
                Source = GameSource.Manual,
                SourceId = sourceId,
                Name = ChooseName(name, Path.GetFileNameWithoutExtension(full)),
                InstallDir = folder,
                Executables = [full],
                LaunchCommand = full,
                LaunchWorkingDirectory = folder,
            };
        }

        if (fileSystem.DirectoryExists(full))
        {
            if (IsDriveRoot(full))
            {
                throw new ArgumentException(DriveRootMessage(path), nameof(path));
            }

            var executables = fileSystem.EnumerateFiles(full, "*.exe")
                .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !HelperExecutables.IsHelper(f))
                .Select(PathUtil.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var single = executables.Count == 1 ? executables[0] : null;
            return new GameEntry
            {
                Source = GameSource.Manual,
                SourceId = sourceId,
                Name = ChooseName(name, Path.GetFileName(full)),
                InstallDir = full,
                Executables = executables,
                LaunchCommand = single,
                LaunchWorkingDirectory = single is null ? null : full,
            };
        }

        throw new ArgumentException($"Der Pfad existiert nicht: \"{full}\".", nameof(path));
    }

    private static string ChooseName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    /// <summary>
    /// Eine Laufwerkswurzel (oder Freigabewurzel) als Installationsordner hieße: jeder Prozess darauf gilt als Spiel.
    /// </summary>
    private static bool IsDriveRoot(string folder) =>
        string.IsNullOrEmpty(folder) || string.Equals(PathUtil.Normalize(Path.GetPathRoot(folder) ?? string.Empty), folder, StringComparison.OrdinalIgnoreCase);

    private static string DriveRootMessage(string path) =>
        $"Ein ganzes Laufwerk kann kein Spielordner sein, sonst gälte jeder Prozess darauf als Spiel: \"{path}\". " +
        "Bitte die EXE in einen eigenen Ordner legen oder den Spielordner wählen.";
}
