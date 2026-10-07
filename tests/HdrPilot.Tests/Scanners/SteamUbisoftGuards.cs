using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.Scanners;

/// <summary>Wächter für den Vertrag „Scanner lesen nur“: zählen Schreibversuche bzw. werfen dabei.</summary>
internal static class SteamUbisoftGuards
{
    /// <summary>Reicht Lesezugriffe durch und zählt jeden Schreibversuch.</summary>
    internal sealed class WriteSpyFileSystem(IFileSystem inner) : IFileSystem
    {
        public int Writes { get; private set; }

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            inner.EnumerateDirectories(directory, searchPattern, recursive);

        public void CreateDirectory(string path) => Writes++;

        public void WriteAllTextAtomic(string path, string contents) => Writes++;

        public void DeleteFile(string path) => Writes++;

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }

    /// <summary>Registry, die jeden Schreibversuch mit einer Ausnahme beantwortet.</summary>
    internal sealed class ReadOnlyRegistry(IRegistry inner) : IRegistry
    {
        public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default)
        {
            var key = inner.OpenKey(hive, subKeyPath, view);
            return key is null ? null : new ReadOnlyKey(key);
        }

        public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath) =>
            throw new InvalidOperationException("Scanner dürfen die Registry nicht schreiben: " + subKeyPath);

        private sealed class ReadOnlyKey(IRegistryKey inner) : IRegistryKey
        {
            public string Name => inner.Name;

            public IReadOnlyList<string> GetSubKeyNames() => inner.GetSubKeyNames();

            public IReadOnlyList<string> GetValueNames() => inner.GetValueNames();

            public object? GetValue(string name) => inner.GetValue(name);

            public IRegistryKey? OpenSubKey(string name)
            {
                var sub = inner.OpenSubKey(name);
                return sub is null ? null : new ReadOnlyKey(sub);
            }

            public void SetValue(string name, string value) =>
                throw new InvalidOperationException("Scanner dürfen die Registry nicht schreiben: " + inner.Name);

            public void DeleteValue(string name) =>
                throw new InvalidOperationException("Scanner dürfen die Registry nicht schreiben: " + inner.Name);

            public void Dispose() => inner.Dispose();
        }
    }
}
