using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.Scanners;

/// <summary>Hilfen für EaScannerTests und BattleNetScannerTests: Hüllen, die jeden Schreibzugriff mit einer Ausnahme quittieren.</summary>
internal static class EaBattleNetTestSupport
{
    public sealed class ReadOnlyFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            inner.EnumerateDirectories(directory, searchPattern, recursive);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();

        public void CreateDirectory(string path) => throw new InvalidOperationException("Scanner darf keinen Ordner anlegen: " + path);

        public void WriteAllTextAtomic(string path, string contents) => throw new InvalidOperationException("Scanner darf nicht schreiben: " + path);

        public void DeleteFile(string path) => throw new InvalidOperationException("Scanner darf nicht löschen: " + path);
    }

    /// <summary>Wie das innere Dateisystem, aber das Auflisten eines bestimmten Ordners wirft (gesperrt, währenddessen gelöscht).</summary>
    public sealed class FailingEnumerationFileSystem(IFileSystem inner, string failingFolder, Exception failure) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            string.Equals(directory.TrimEnd('\\'), failingFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? Fail()
                : inner.EnumerateDirectories(directory, searchPattern, recursive);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        private IEnumerable<string> Fail()
        {
            // Wie Directory.EnumerateDirectories: der Fehler kommt erst beim Durchlaufen.
            yield return failingFolder + @"\Erster";
            throw failure;
        }
    }

    public sealed class ReadOnlyRegistry(IRegistry inner) : IRegistry
    {
        public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default) =>
            inner.OpenKey(hive, subKeyPath, view) is { } key ? new ReadOnlyKey(key) : null;

        public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath) =>
            throw new InvalidOperationException("Scanner darf keinen Registry-Schlüssel anlegen: " + subKeyPath);
    }

    private sealed class ReadOnlyKey(IRegistryKey inner) : IRegistryKey
    {
        public string Name => inner.Name;

        public IReadOnlyList<string> GetSubKeyNames() => inner.GetSubKeyNames();

        public IReadOnlyList<string> GetValueNames() => inner.GetValueNames();

        public object? GetValue(string name) => inner.GetValue(name);

        public IRegistryKey? OpenSubKey(string name) => inner.OpenSubKey(name) is { } key ? new ReadOnlyKey(key) : null;

        public void SetValue(string name, string value) => throw new InvalidOperationException("Scanner darf keinen Registry-Wert schreiben: " + name);

        public void DeleteValue(string name) => throw new InvalidOperationException("Scanner darf keinen Registry-Wert löschen: " + name);

        public void Dispose() => inner.Dispose();
    }
}
