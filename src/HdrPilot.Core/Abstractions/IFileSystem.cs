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

    /// <summary>
    /// Direkte Unterordner ohne verknüpfte Ordner (Junction, symbolische Verknüpfung): für Suchen, die nie woandershin
    /// abbiegen dürfen, etwa über eine Verknüpfung auf eine Netzfreigabe (E91). Ordner von OneDrive und Co. (Platzhalter
    /// mit Analysepunkt, aber ohne Ziel) zählen als echte Ordner. Wirft nicht bei fehlendem Ordner. Standard:
    /// <see cref="EnumerateDirectories"/> (Attrappen kennen keine Verknüpfungen).
    /// </summary>
    IEnumerable<string> EnumerateDirectoriesWithoutLinks(string directory) => EnumerateDirectories(directory);

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
    /// nach Namen; verknüpften Ordnern (Junction, symbolische Verknüpfung) folgt es nicht. Für Prüfungen, die „nichts da“ von
    /// „nicht zu klären“ unterscheiden: Ist der Ordner selbst oder einer darunter (bis zur Tiefe) nicht lesbar, wirft die Folge
    /// <see cref="UnauthorizedAccessException"/> (oder <see cref="IOException"/>), nachdem sie alle lesbaren Treffer geliefert
    /// hat; wer beim ersten Treffer aufhört, merkt davon nichts. Wirft nicht bei fehlendem Ordner. Standard: die rekursive
    /// Folge von <see cref="EnumerateFiles"/>, nach Tiefe begrenzt.
    /// </summary>
    IEnumerable<string> EnumerateFilesUpTo(string directory, string searchPattern, int maxDepth) =>
        EnumerateFiles(directory, searchPattern, recursive: true)
            .Where(f => Path.GetRelativePath(directory, f).Count(c => c == Path.DirectorySeparatorChar) <= maxDepth);

    /// <summary>
    /// Gibt es die Datei? Für Prüfungen, die „nicht da“ von „nicht zu klären“ unterscheiden: Anders als
    /// <see cref="FileExists"/> wirft es (<see cref="UnauthorizedAccessException"/> oder <see cref="IOException"/>), wenn
    /// Windows die Auskunft verweigert (eine Datei in einem nicht lesbaren Ordner) oder Laufwerk oder Freigabe nicht
    /// antworten. Fehlt sie, ihr Ordner oder ihr Laufwerk, oder ist der Pfad ungültig: false. Standard: <see cref="FileExists"/>.
    /// </summary>
    bool FileExistsOrThrow(string path) => FileExists(path);

    /// <summary>
    /// Alle Dateien unter <paramref name="directory"/> samt Unterordnern, für Prüfungen, die nichts übersehen dürfen: Anders als
    /// <see cref="EnumerateFiles"/> wirft es (<see cref="UnauthorizedAccessException"/> oder <see cref="IOException"/>), wenn
    /// ein Ordner darunter nicht lesbar ist oder ein verknüpfter Ordner darin liegt, dem es nicht folgt. Fehlt der Ordner, ist
    /// die Folge leer. Standard: die rekursive Folge von <see cref="EnumerateFiles"/>.
    /// </summary>
    IEnumerable<string> EnumerateFilesCompletely(string directory, string searchPattern) =>
        EnumerateFiles(directory, searchPattern, recursive: true);

    /// <summary>
    /// Liest höchstens <paramref name="maxBytes"/> Bytes vom Anfang (oder mit <paramref name="fromEnd"/> vom Ende) einer Datei,
    /// die einem anderen Programm gehört: Geöffnet wird mit Freigabe zum Lesen, Schreiben und Löschen, damit es die Datei
    /// währenddessen schreiben, ersetzen oder löschen darf (Quiver, Velopack, E91). <see cref="SharedRead.Length"/> ist die
    /// Länge der ganzen Datei. Wirft wie <see cref="ReadAllBytes"/>. Standard: <see cref="ReadAllBytes"/>, gekürzt.
    /// </summary>
    SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false)
    {
        var all = ReadAllBytes(path);
        var count = Math.Min(all.Length, Math.Max(0, maxBytes));
        return new SharedRead(fromEnd ? all[(all.Length - count)..] : all[..count], all.Length);
    }
}

/// <summary>Ergebnis von <see cref="IFileSystem.ReadShared"/>: gelesene Bytes und Länge der ganzen Datei.</summary>
public readonly record struct SharedRead(byte[] Bytes, long Length)
{
    /// <summary>Die ganze Datei wurde gelesen.</summary>
    public bool IsComplete => Bytes.Length == Length;
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

    // Win32-Fehler, wie sie Windows beim Öffnen und Lesen von Ordnern und Dateien meldet (HRESULT 0x8007xxxx).
    private const int Win32Facility = unchecked((int)0x80070000);
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidDrive = 15;
    private const int ErrorInvalidName = 123;
    private const int ErrorBadPathName = 161;
    private const int ErrorDirectory = 267;

    public bool FileExists(string path) => File.Exists(path);

    public bool FileExistsOrThrow(string path) => AttributesOrNull(path) is { } attributes && (attributes & FileAttributes.Directory) == 0;

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

    public IEnumerable<string> EnumerateDirectoriesWithoutLinks(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return new FileSystemEnumerable<string>(directory, (ref FileSystemEntry entry) => entry.ToFullPath(), TopLevel)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => entry.IsDirectory && !IsLink(ref entry),
        };
    }

    /// <summary>Verknüpfter Ordner mit Ziel (Junction, symbolische Verknüpfung); Platzhalter von OneDrive und Co. nicht.</summary>
    private static bool IsLink(ref FileSystemEntry entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0 && new DirectoryInfo(entry.ToFullPath()).LinkTarget is not null;

    public IEnumerable<string> EnumerateFilesUpTo(string directory, string searchPattern, int maxDepth)
    {
        if (!Directory.Exists(directory))
        {
            // Fehlt er wirklich, oder verweigert Windows schon die Auskunft über ihn (ein nicht lesbarer Ordner darüber)? Dann
            // wirft es; eine Datei dieses Namens ist kein Ordner.
            _ = AttributesOrNull(directory);
            return [];
        }

        return FilesUpTo(directory, searchPattern, maxDepth);
    }

    /// <summary>Liefert die lesbaren Treffer und wirft danach, wenn ein Ordner nicht lesbar war (<see cref="EnumerateFilesUpTo"/>).</summary>
    private static IEnumerable<string> FilesUpTo(string directory, string searchPattern, int maxDepth)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = maxDepth > 0,
            MaxRecursionDepth = Math.Max(0, maxDepth),
            AttributesToSkip = 0,
        };
        using var files = new FilesUpToEnumerator(directory, searchPattern, options);
        while (files.MoveNext())
        {
            yield return files.Current;
        }

        if (files.Error != 0)
        {
            throw files.Error == ErrorAccessDenied
                ? new UnauthorizedAccessException("Nicht jeder Ordner unter " + directory + " ist lesbar.")
                : new IOException("Nicht jeder Ordner unter " + directory + " ist lesbar (Fehler " + files.Error + ").", Win32Facility | files.Error);
        }
    }

    /// <summary>
    /// Dateien bis zu einer Tiefe, ohne verknüpften Ordnern zu folgen. Einen Ordner, der sich nicht öffnen oder lesen lässt
    /// (Zugriff verweigert, Laufwerk antwortet nicht), überspringt es nicht still: Es merkt sich den Fehler und liest weiter.
    /// </summary>
    private sealed class FilesUpToEnumerator(string directory, string searchPattern, EnumerationOptions options)
        : FileSystemEnumerator<string>(directory, options)
    {
        /// <summary>Win32-Fehler des ersten Ordners, der sich nicht lesen ließ; 0: alle gelesen.</summary>
        public int Error { get; private set; }

        protected override bool ContinueOnError(int error)
        {
            // Inzwischen verschwunden (oder durch eine Datei ersetzt): Dort liegt nichts. Alles andere bleibt ungeklärt.
            if (error is not (ErrorFileNotFound or ErrorPathNotFound or ErrorDirectory) && Error == 0)
            {
                Error = error;
            }

            return true;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry) =>
            !entry.IsDirectory && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName);

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0;

        protected override string TransformEntry(ref FileSystemEntry entry) => entry.ToFullPath();
    }

    /// <summary>
    /// Attribute des Pfads; null, wenn er fehlt (auch sein Ordner oder Laufwerk) oder kein gültiger Pfad ist. Verweigert
    /// Windows die Auskunft (etwa ein nicht lesbarer Ordner darüber) oder antwortet das Laufwerk nicht, wirft es
    /// (<see cref="UnauthorizedAccessException"/>, <see cref="IOException"/>); <see cref="File.Exists"/> sagte dann nur „nein“.
    /// </summary>
    private static FileAttributes? AttributesOrNull(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or ArgumentException or NotSupportedException
            || (ex is IOException && (ex.HResult & unchecked((int)0xFFFF0000)) == Win32Facility
                && (ex.HResult & 0xFFFF) is ErrorInvalidDrive or ErrorInvalidName or ErrorBadPathName or ErrorDirectory))
        {
            return null;
        }
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

    public SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        var length = stream.Length;
        var count = (int)Math.Min(length, Math.Max(0, maxBytes));
        if (fromEnd && length > count)
        {
            stream.Seek(length - count, SeekOrigin.Begin);
        }

        var buffer = new byte[count];
        var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return new SharedRead(read == count ? buffer : buffer[..read], length);
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
