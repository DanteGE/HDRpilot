using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Dateisystem im Speicher mit Windows-Semantik (Groß-/Kleinschreibung egal, Backslash).
/// Ordner entstehen implizit aus Dateipfaden. Beispieldateien lassen sich aus Fixtures\ einhängen.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _readFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _writeFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _deleteFailures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> FixedDriveRoots { get; } = [@"C:\"];

    /// <summary>Ordner der Beispieldateien im Testausgabeordner.</summary>
    public static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public InMemoryFileSystem AddFile(string path, string contents) => AddFile(path, new UTF8Encoding(false).GetBytes(contents));

    public InMemoryFileSystem AddFile(string path, byte[] contents)
    {
        var p = PathUtil.Normalize(path);
        _files[p] = contents;
        AddParents(p);
        return this;
    }

    /// <summary>Hängt eine Datei aus Fixtures\ unter einem virtuellen Pfad ein.</summary>
    public InMemoryFileSystem AddFixture(string fixtureRelativePath, string virtualPath) =>
        AddFile(virtualPath, File.ReadAllBytes(Path.Combine(FixturesRoot, fixtureRelativePath)));

    public InMemoryFileSystem AddDirectory(string path)
    {
        var p = PathUtil.Normalize(path);
        _directories.Add(p);
        AddParents(p);
        return this;
    }

    /// <summary>Lesen dieser Datei wirft die angegebene Ausnahme (z. B. gesperrte oder nicht lesbare Datei).</summary>
    public InMemoryFileSystem FailReading(string path, Exception exception)
    {
        _readFailures[PathUtil.Normalize(path)] = exception;
        return this;
    }

    /// <summary>Schreiben dieser Datei wirft die angegebene Ausnahme (Platte voll, gesperrt). Null hebt es auf.</summary>
    public InMemoryFileSystem FailWriting(string path, Exception? exception)
    {
        if (exception is null)
        {
            _writeFailures.Remove(PathUtil.Normalize(path));
        }
        else
        {
            _writeFailures[PathUtil.Normalize(path)] = exception;
        }

        return this;
    }

    /// <summary>
    /// Löschen dieser Datei wirft die angegebene Ausnahme (etwa vom Virenscanner gesperrt, ohne FILE_SHARE_DELETE geöffnet).
    /// Wie unter NTFS scheitert dann auch das atomare Ersetzen der vorhandenen Datei (MoveFileEx mit REPLACE_EXISTING braucht
    /// dasselbe Löschrecht); eine neue Datei an diesem Pfad lässt sich anlegen. Null hebt es auf.
    /// </summary>
    public InMemoryFileSystem FailDeleting(string path, Exception? exception)
    {
        if (exception is null)
        {
            _deleteFailures.Remove(PathUtil.Normalize(path));
        }
        else
        {
            _deleteFailures[PathUtil.Normalize(path)] = exception;
        }

        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(PathUtil.Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(PathUtil.Normalize(path));

    public string ReadAllText(string path)
    {
        var bytes = ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public byte[] ReadAllBytes(string path)
    {
        var p = PathUtil.Normalize(path);
        if (_readFailures.TryGetValue(p, out var failure))
        {
            throw failure;
        }

        return _files.TryGetValue(p, out var bytes)
            ? bytes
            : throw new FileNotFoundException("Datei nicht gefunden: " + path, path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false)
    {
        var dir = PathUtil.Normalize(directory);
        if (!_directories.Contains(dir))
        {
            return [];
        }

        var regex = WildcardToRegex(searchPattern);
        return _files.Keys
            .Where(f => IsChild(f, dir, recursive) && regex.IsMatch(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false)
    {
        var dir = PathUtil.Normalize(directory);
        if (!_directories.Contains(dir))
        {
            return [];
        }

        var regex = WildcardToRegex(searchPattern);
        return _directories
            .Where(d => IsChild(d, dir, recursive) && regex.IsMatch(Path.GetFileName(d)))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public void WriteAllTextAtomic(string path, string contents)
    {
        var p = PathUtil.Normalize(path);
        if (_writeFailures.TryGetValue(p, out var failure))
        {
            throw failure;
        }

        // Gesperrt gegen Löschen: Ersetzen der vorhandenen Datei scheitert genauso (siehe FailDeleting).
        if (_deleteFailures.TryGetValue(p, out var locked) && _files.ContainsKey(p))
        {
            throw locked;
        }

        AddFile(path, contents);
    }

    public void DeleteFile(string path)
    {
        var p = PathUtil.Normalize(path);
        if (_deleteFailures.TryGetValue(p, out var failure) && _files.ContainsKey(p))
        {
            throw failure;
        }

        _files.Remove(p);
    }

    public IReadOnlyList<string> GetFixedDriveRoots() => FixedDriveRoots;

    private static bool IsChild(string path, string dir, bool recursive)
    {
        if (!PathUtil.IsUnder(path, dir) || string.Equals(path, dir, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (recursive)
        {
            return true;
        }

        var parent = Path.GetDirectoryName(path);
        return parent is not null && string.Equals(PathUtil.Normalize(parent), dir, StringComparison.OrdinalIgnoreCase);
    }

    private void AddParents(string path)
    {
        var parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            _directories.Add(PathUtil.Normalize(parent));
            var next = Path.GetDirectoryName(parent);
            if (next == parent)
            {
                break;
            }

            parent = next;
        }
    }

    private static Regex WildcardToRegex(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
