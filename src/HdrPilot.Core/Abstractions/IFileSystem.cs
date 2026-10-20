using System.IO.Enumeration;

namespace HdrPilot.Core.Abstractions;

/// <summary>Dateizugriffe, damit Scanner und Konfiguration ohne echte Platte testbar sind.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Liefert vollständige Pfade. Wirft nicht bei fehlendem Ordner, sondern liefert eine leere Folge.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false);

    /// <summary>Liefert vollständige Pfade. Wirft nicht bei fehlendem Ordner, sondern liefert eine leere Folge.</summary>
    IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false);

    void CreateDirectory(string path);

    /// <summary>
    /// Schreibt erst in eine temporäre Datei im selben Ordner und ersetzt dann das Ziel. Hält ein anderer Prozess das Ziel kurz
    /// offen, wartet das Ersetzen (<see cref="FileReplace"/>); scheitert es, bleibt das Ziel unverändert, und es wirft.
    /// </summary>
    void WriteAllTextAtomic(string path, string contents);

    void DeleteFile(string path);

    /// <summary>Wurzeln aller bereiten, festen Laufwerke, etwa "C:\".</summary>
    IReadOnlyList<string> GetFixedDriveRoots();

    /// <summary>
    /// Dateien bis <paramref name="maxDepth"/> Ordnerebenen unter <paramref name="directory"/> (0: nur der Ordner selbst), nur
    /// nach Namen; verknüpften Ordnern (Junction, symbolische Verknüpfung) folgt es nicht, nicht lesbare überspringt es. Wirft
    /// nicht bei fehlendem Ordner. Standard: die rekursive Folge von <see cref="EnumerateFiles"/>, nach Tiefe begrenzt.
    /// </summary>
    IEnumerable<string> EnumerateFilesUpTo(string directory, string searchPattern, int maxDepth) =>
        EnumerateFiles(directory, searchPattern, recursive: true)
            .Where(f => Path.GetRelativePath(directory, f).Count(c => c == Path.DirectorySeparatorChar) <= maxDepth);

    /// <summary>
    /// Alle Dateien unter <paramref name="directory"/> samt Unterordnern, für Prüfungen, die nichts übersehen dürfen: Anders als
    /// <see cref="EnumerateFiles"/> wirft es (<see cref="UnauthorizedAccessException"/> oder <see cref="IOException"/>), wenn
    /// ein Ordner darunter nicht lesbar ist oder ein verknüpfter Ordner darin liegt, dem es nicht folgt. Fehlt der Ordner, ist
    /// die Folge leer. Standard: die rekursive Folge von <see cref="EnumerateFiles"/>.
    /// </summary>
    IEnumerable<string> EnumerateFilesCompletely(string directory, string searchPattern) =>
        EnumerateFiles(directory, searchPattern, recursive: true);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Instance { get; } = new();

    private static readonly EnumerationOptions TopLevel = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static readonly EnumerationOptions Recursive = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, searchPattern, recursive ? Recursive : TopLevel);
    }

    public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(directory, searchPattern, recursive ? Recursive : TopLevel);
    }

    public IEnumerable<string> EnumerateFilesUpTo(string directory, string searchPattern, int maxDepth)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = maxDepth > 0,
            MaxRecursionDepth = Math.Max(0, maxDepth),
            AttributesToSkip = 0,
        };
        return new FileSystemEnumerable<string>(directory, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        };
    }

    public IEnumerable<string> EnumerateFilesCompletely(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var options = new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = true, AttributesToSkip = 0 };
        return new FileSystemEnumerable<string>(directory, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName),

            // Ordner von OneDrive und Co. sind Platzhalter mit Analysepunkt, aber echte Ordner: Nur Verknüpfungen mit Ziel
            // (Junction, symbolische Verknüpfung) gelten als nicht verfolgt.
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReparsePoint) == 0 || new DirectoryInfo(entry.ToFullPath()).LinkTarget is null
                    ? true
                    : throw new IOException("Verknüpfter Ordner, nicht verfolgt: " + entry.ToFullPath()),
        };
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllTextAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Pfad ohne Ordner: " + path, nameof(path));
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // Ein Leser des Ziels (auch mit Freigabe zum Löschen) lässt das Ersetzen mit „Zugriff verweigert“ scheitern:
            // kurz warten und erneut ersetzen, die fertige temporäre Datei bleibt so lange liegen.
            FileReplace.Run(() => File.Move(temp, path, overwrite: true));
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IReadOnlyList<string> GetFixedDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (IOException)
            {
                // Laufwerk verschwand während der Abfrage.
            }
            catch (UnauthorizedAccessException)
            {
                // Laufwerk ohne Leserecht wird übersprungen.
            }
        }

        return roots;
    }
}
