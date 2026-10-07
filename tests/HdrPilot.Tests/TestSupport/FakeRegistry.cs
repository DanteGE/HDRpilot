using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Registry-Attrappe. Bildet die 64-Bit-Sicht (Default/Registry64) und die 32-Bit-Sicht (Registry32) getrennt ab.
/// Wie unter Windows x64 landet HKLM\SOFTWARE\WOW6432Node\X in der Default-Sicht in derselben Ablage wie
/// HKLM\SOFTWARE\X in der 32-Bit-Sicht. HKCU ist nicht umgeleitet.
/// </summary>
public sealed class FakeRegistry : IRegistry
{
    private const string Wow = @"SOFTWARE\WOW6432Node\";
    private readonly Dictionary<string, Node> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deniedKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Setzt einen Wert und legt fehlende Schlüssel an.</summary>
    public FakeRegistry Set(RegistryHiveKind hive, string keyPath, string valueName, object value, RegistryViewKind view = RegistryViewKind.Default)
    {
        var node = Ensure(hive, keyPath, view);
        node.Values[valueName] = value;
        return this;
    }

    public FakeRegistry AddKey(RegistryHiveKind hive, string keyPath, RegistryViewKind view = RegistryViewKind.Default)
    {
        Ensure(hive, keyPath, view);
        return this;
    }

    /// <summary>Öffnen dieses Schlüssels liefert null, als fehlten die Rechte.</summary>
    public FakeRegistry DenyAccess(RegistryHiveKind hive, string keyPath, RegistryViewKind view = RegistryViewKind.Default)
    {
        var (store, path) = Resolve(hive, keyPath, view);
        _deniedKeys.Add(store + "|" + path);
        return this;
    }

    public object? Get(RegistryHiveKind hive, string keyPath, string valueName, RegistryViewKind view = RegistryViewKind.Default)
    {
        var node = Find(hive, keyPath, view);
        return node is not null && node.Values.TryGetValue(valueName, out var v) ? v : null;
    }

    public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default)
    {
        var (store, path) = Resolve(hive, subKeyPath, view);
        if (IsDenied(store, path))
        {
            return null;
        }

        var node = Find(hive, subKeyPath, view);
        return node is null ? null : new Key(this, store, path, node);
    }

    public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath)
    {
        var (store, path) = Resolve(hive, subKeyPath, RegistryViewKind.Default);
        return new Key(this, store, path, Ensure(hive, subKeyPath, RegistryViewKind.Default));
    }

    private bool IsDenied(string store, string path) => _deniedKeys.Contains(store + "|" + path.TrimEnd('\\'));

    private static (string Store, string Path) Resolve(RegistryHiveKind hive, string keyPath, RegistryViewKind view)
    {
        var path = keyPath.Trim('\\');
        if (hive == RegistryHiveKind.CurrentUser)
        {
            return ("HKCU", path);
        }

        if (view == RegistryViewKind.Registry32)
        {
            return ("HKLM32", path);
        }

        if (path.StartsWith(Wow, StringComparison.OrdinalIgnoreCase))
        {
            return ("HKLM32", @"SOFTWARE\" + path[Wow.Length..]);
        }

        return ("HKLM64", path);
    }

    private Node? Find(RegistryHiveKind hive, string keyPath, RegistryViewKind view)
    {
        var (store, path) = Resolve(hive, keyPath, view);
        if (!_roots.TryGetValue(store, out var node))
        {
            return null;
        }

        foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Children.TryGetValue(part, out node!))
            {
                return null;
            }
        }

        return node;
    }

    private Node Ensure(RegistryHiveKind hive, string keyPath, RegistryViewKind view)
    {
        var (store, path) = Resolve(hive, keyPath, view);
        if (!_roots.TryGetValue(store, out var node))
        {
            node = new Node(store);
            _roots[store] = node;
        }

        foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Children.TryGetValue(part, out var child))
            {
                child = new Node(part);
                node.Children[part] = child;
            }

            node = child;
        }

        return node;
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Key(FakeRegistry owner, string store, string path, Node node) : IRegistryKey
    {
        public string Name => (store.StartsWith("HKCU", StringComparison.Ordinal) ? "HKEY_CURRENT_USER\\" : "HKEY_LOCAL_MACHINE\\") + path;

        public IReadOnlyList<string> GetSubKeyNames() => node.Children.Values.Select(c => c.Name).ToList();

        public IReadOnlyList<string> GetValueNames() => node.Values.Keys.ToList();

        public object? GetValue(string name) => node.Values.TryGetValue(name, out var v) ? v : null;

        public IRegistryKey? OpenSubKey(string name)
        {
            var childPath = path + "\\" + name.Trim('\\');
            if (owner.IsDenied(store, childPath))
            {
                return null;
            }

            var current = node;
            foreach (var part in name.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!current.Children.TryGetValue(part, out current!))
                {
                    return null;
                }
            }

            return new Key(owner, store, childPath, current);
        }

        public void SetValue(string name, string value) => node.Values[name] = value;

        public void DeleteValue(string name) => node.Values.Remove(name);

        public void Dispose()
        {
        }
    }
}
