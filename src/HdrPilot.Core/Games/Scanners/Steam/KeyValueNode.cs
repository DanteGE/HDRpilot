namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>
/// Knoten einer Valve-KeyValues-Datei (Textformat, etwa libraryfolders.vdf oder appmanifest_*.acf):
/// entweder ein Schlüssel mit Zeichenkette oder ein Schlüssel mit Block aus weiteren Knoten.
/// Schlüssel dürfen mehrfach vorkommen; die Suche nimmt den ersten Treffer ohne Groß-/Kleinschreibung.
/// </summary>
internal sealed class KeyValueNode
{
    private static readonly IReadOnlyList<KeyValueNode> NoChildren = [];

    private KeyValueNode(string key, string? value, IReadOnlyList<KeyValueNode> children)
    {
        Key = key;
        Value = value;
        Children = children;
    }

    public string Key { get; }

    /// <summary>Zeichenkette des Knotens; null bei einem Block.</summary>
    public string? Value { get; }

    /// <summary>Kinder eines Blocks in Dateireihenfolge; leer bei einer Zeichenkette.</summary>
    public IReadOnlyList<KeyValueNode> Children { get; }

    public bool IsBlock => Value is null;

    public static KeyValueNode CreateString(string key, string value) => new(key, value, NoChildren);

    public static KeyValueNode CreateBlock(string key, IReadOnlyList<KeyValueNode> children) => new(key, null, children);

    /// <summary>Erstes Kind mit diesem Schlüssel (ohne Groß-/Kleinschreibung) oder null.</summary>
    public KeyValueNode? Child(string key)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Zeichenkette des ersten Kindes mit diesem Schlüssel; null, wenn es fehlt oder ein Block ist.</summary>
    public string? GetString(string key) => Child(key)?.Value;
}
