using HdrPilot.Core.AutoHdr;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Schlüssel UserGpuPreferences im Speicher (E88): nie die echte Registry. Wertnamen ohne Groß-/Kleinschreibung wie in der
/// Registry. Schreib- und Löschvorgänge werden mitgeschrieben, Fehler lassen sich vorgeben.
/// </summary>
public sealed class FakeGpuPreferenceStore : IGpuPreferenceStore
{
    private readonly Dictionary<string, GpuPreferenceValue> _values = new(StringComparer.OrdinalIgnoreCase);

    public string KeyName { get; init; } = AutoHdrRegistryLocation.ForTest("Fake").KeyName;

    /// <summary>Jeder Schreib- und Löschvorgang in Reihenfolge, etwa „set DirectXUserGlobalSettings=AutoHDREnable=1;“ oder „delete …“.</summary>
    public List<string> Operations { get; } = [];

    public int Reads { get; private set; }

    /// <summary>Wertnamen in der Reihenfolge der Lesevorgänge.</summary>
    public List<string> ReadNames { get; } = [];

    /// <summary>Wenn gesetzt, scheitert jeder Schreib- und Löschvorgang damit (nichts wird geändert).</summary>
    public Exception? FailWrites { get; set; }

    /// <summary>Wenn gesetzt, scheitert jedes Lesen damit.</summary>
    public Exception? FailReads { get; set; }

    /// <summary>Alle Werte (Name → Wert), etwa für Vergleiche vorher/nachher.</summary>
    public IReadOnlyDictionary<string, GpuPreferenceValue> Values => _values;

    /// <summary>Legt einen REG_SZ an, ohne ihn als Vorgang mitzuschreiben (Ausgangslage eines Tests).</summary>
    public FakeGpuPreferenceStore SetString(string name, string text) => Set(name, GpuPreferenceValue.FromString(text));

    /// <summary>Legt einen Wert beliebiger Art an (etwa DWORD, REG_EXPAND_SZ), ohne ihn mitzuschreiben.</summary>
    public FakeGpuPreferenceStore Set(string name, GpuPreferenceValue value)
    {
        if (value.IsMissing)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = value;
        }

        return this;
    }

    /// <summary>Wie oft dieser Wert gelesen wurde (ohne Groß-/Kleinschreibung).</summary>
    public int ReadsOf(string name) => ReadNames.Count(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Text eines REG_SZ oder null, wenn der Wert fehlt oder kein REG_SZ ist.</summary>
    public string? GetString(string name) =>
        _values.TryGetValue(name, out var v) && v.Kind == GpuPreferenceValueKind.String ? v.Text : null;

    public GpuPreferenceValue Read(string valueName)
    {
        Reads++;
        ReadNames.Add(valueName);
        if (FailReads is { } failure)
        {
            throw failure;
        }

        return _values.TryGetValue(valueName, out var v) ? v : GpuPreferenceValue.Missing;
    }

    public void WriteString(string valueName, string value)
    {
        if (FailWrites is { } failure)
        {
            throw failure;
        }

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("REG_SZ mit eingebettetem NUL wird nie geschrieben.", nameof(value));
        }

        Operations.Add("set " + valueName + "=" + value);
        _values[valueName] = GpuPreferenceValue.FromString(value);
    }

    public void Delete(string valueName)
    {
        if (FailWrites is { } failure)
        {
            throw failure;
        }

        Operations.Add("delete " + valueName);
        _values.Remove(valueName);
    }
}
