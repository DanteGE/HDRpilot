using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Dekodierte Bilder im Speicher, begrenzt auf <see cref="Budget"/> Byte (Breite × Höhe × 4): Beim Überschreiten fallen die am
/// längsten nicht benutzten heraus (sie werden bei Bedarf von der Platte neu dekodiert). Auch Bildsätze für den Titelbereich
/// liegen hier, mit der Größe aller Bilder, die sie halten: So zählt jedes Bild, auf das der Speicher verweist, mindestens
/// einmal, und mehr als das Budget bleibt nie erreichbar. Threadsicher.
/// </summary>
public sealed class ImageMemoryCache(long budget)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _order = new();
    private long _bytes;

    public long Budget => budget;

    /// <summary>Belegter Speicher (geschätzt).</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public static long SizeOf(ImageSource? image) => image is BitmapSource bitmap ? Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4) : 1;

    public bool TryGet(string key, out ImageSource? image)
    {
        if (TryGetValue(key, out var value))
        {
            image = value as ImageSource;
            return true;
        }

        image = null;
        return false;
    }

    /// <summary>Beliebiger Eintrag (etwa ein Bildsatz) unter diesem Schlüssel; zählt als benutzt.</summary>
    public bool TryGetValue(string key, out object? value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    public void Set(string key, ImageSource? image) => Set(key, image, SizeOf(image));

    /// <summary>Eintrag mit eigener Größe (Bildsatz: Summe seiner Bilder), mindestens 1 Byte.</summary>
    public void Set(string key, object? value, long bytes)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var old))
            {
                _order.Remove(old);
                _bytes -= old.Value.Bytes;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value, Math.Max(1, bytes)));
            _order.AddFirst(node);
            _entries[key] = node;
            _bytes += node.Value.Bytes;
            while (_bytes > budget && _order.Last is { } last && last != node)
            {
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    /// <summary>Entfernt einen Eintrag (etwa den ältesten Bildsatz über der Höchstzahl).</summary>
    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var node))
            {
                _order.Remove(node);
                _bytes -= node.Value.Bytes;
            }
        }
    }

    /// <summary>Entfernt alle Einträge, deren Schlüssel so beginnt (etwa alle Bilder eines Spiels).</summary>
    public void RemovePrefix(string prefix)
    {
        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var node = _entries[key];
                _order.Remove(node);
                _entries.Remove(key);
                _bytes -= node.Value.Bytes;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _bytes = 0;
        }
    }

    private sealed record Entry(string Key, object? Value, long Bytes);
}
