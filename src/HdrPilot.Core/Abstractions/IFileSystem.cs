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

    /// <summary>Schreibt erst in eine temporäre Datei im selben Ordner und ersetzt dann das Ziel.</summary>
    void WriteAllTextAtomic(string path, string contents);

    void DeleteFile(string path);

    /// <summary>Wurzeln aller bereiten, festen Laufwerke, etwa "C:\".</summary>
    IReadOnlyList<string> GetFixedDriveRoots();
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

            File.Move(temp, path, overwrite: true);
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
