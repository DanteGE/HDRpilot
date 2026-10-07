using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.Scanners;

/// <summary>Dateisystem-Hülle für Scanner-Tests: Lesen geht durch, jeder Schreibversuch lässt den Test scheitern.</summary>
internal sealed class ReadOnlyFileSystem(IFileSystem inner) : IFileSystem
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

    public void CreateDirectory(string path) => throw new InvalidOperationException("Scanner dürfen keine Ordner anlegen: " + path);

    public void WriteAllTextAtomic(string path, string contents) => throw new InvalidOperationException("Scanner dürfen nicht schreiben: " + path);

    public void DeleteFile(string path) => throw new InvalidOperationException("Scanner dürfen nicht löschen: " + path);
}

/// <summary>Registry-Hülle für Scanner-Tests: protokolliert jedes Öffnen, jeder Schreibversuch lässt den Test scheitern.</summary>
internal sealed class ReadOnlyRegistry(IRegistry inner) : IRegistry
{
    public List<(RegistryHiveKind Hive, string Path, RegistryViewKind View)> Opened { get; } = [];

    public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default)
    {
        Opened.Add((hive, subKeyPath, view));
        var key = inner.OpenKey(hive, subKeyPath, view);
        return key is null ? null : new Key(key);
    }

    public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath) =>
        throw new InvalidOperationException("Scanner dürfen keine Registry-Schlüssel anlegen: " + subKeyPath);

    private sealed class Key(IRegistryKey inner) : IRegistryKey
    {
        public string Name => inner.Name;

        public IReadOnlyList<string> GetSubKeyNames() => inner.GetSubKeyNames();

        public IReadOnlyList<string> GetValueNames() => inner.GetValueNames();

        public object? GetValue(string name) => inner.GetValue(name);

        public IRegistryKey? OpenSubKey(string name)
        {
            var sub = inner.OpenSubKey(name);
            return sub is null ? null : new Key(sub);
        }

        public void SetValue(string name, string value) => throw new InvalidOperationException("Scanner dürfen nicht in die Registry schreiben: " + name);

        public void DeleteValue(string name) => throw new InvalidOperationException("Scanner dürfen keine Registry-Werte löschen: " + name);

        public void Dispose() => inner.Dispose();
    }
}
