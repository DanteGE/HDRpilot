using System.Globalization;
using Microsoft.Win32;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Art eines Werts im Schlüssel UserGpuPreferences, wie Windows ihn speichert.</summary>
public enum GpuPreferenceValueKind
{
    /// <summary>Wert fehlt (oder der Schlüssel fehlt).</summary>
    Missing,

    /// <summary>REG_SZ: die einzige Art, die HDRpilot liest, schreibt und zurückstellt.</summary>
    String,

    ExpandString,

    MultiString,

    DWord,

    QWord,

    Binary,

    /// <summary>Jede andere Art (etwa REG_NONE).</summary>
    Other,
}

/// <summary>
/// Ein Wert im Schlüssel UserGpuPreferences. <see cref="Text"/> ist bei REG_SZ der Inhalt ohne das abschließende NUL
/// (eingebettete NUL bleiben erhalten), bei REG_EXPAND_SZ der nicht aufgelöste Inhalt, bei Zahlen die Zahl (nur für das
/// Protokoll); sonst null.
/// </summary>
public sealed record GpuPreferenceValue(GpuPreferenceValueKind Kind, string? Text)
{
    public static GpuPreferenceValue Missing { get; } = new(GpuPreferenceValueKind.Missing, null);

    public bool IsMissing => Kind == GpuPreferenceValueKind.Missing;

    public static GpuPreferenceValue FromString(string text) => new(GpuPreferenceValueKind.String, text);
}

/// <summary>
/// Der Schlüssel UserGpuPreferences unter HKCU (oder eine Testwurzel, siehe <see cref="AutoHdrRegistryLocation"/>): Werte
/// lesen, REG_SZ schreiben, Werte löschen. Sonst nichts, damit HDRpilot dort nichts anderes anfassen kann. In Tests eine
/// Attrappe im Speicher.
/// </summary>
public interface IGpuPreferenceStore
{
    /// <summary>Vollständiger Name des Schlüssels für Protokoll und Zustandsdateien.</summary>
    string KeyName { get; }

    /// <summary>Liest einen Wert samt Art. Fehlt Wert oder Schlüssel: <see cref="GpuPreferenceValue.Missing"/>. Wirft bei fehlenden Rechten.</summary>
    GpuPreferenceValue Read(string valueName);

    /// <summary>Schreibt einen REG_SZ (legt den Schlüssel bei Bedarf an). Wirft bei Fehlern.</summary>
    void WriteString(string valueName, string value);

    /// <summary>Löscht einen Wert; ein fehlender Wert ist kein Fehler. Wirft bei anderen Fehlern.</summary>
    void Delete(string valueName);
}

/// <summary>Echte Registry unter HKCU, immer am Ort aus <see cref="AutoHdrRegistryLocation"/>.</summary>
public sealed class RegistryGpuPreferenceStore(AutoHdrRegistryLocation location) : IGpuPreferenceStore
{
    public AutoHdrRegistryLocation Location => location;

    public string KeyName => location.KeyName;

    public GpuPreferenceValue Read(string valueName)
    {
        ArgumentException.ThrowIfNullOrEmpty(valueName);
        using var key = Registry.CurrentUser.OpenSubKey(location.KeyPath, writable: false);
        var raw = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key is null || raw is null)
        {
            return GpuPreferenceValue.Missing;
        }

        RegistryValueKind kind;
        try
        {
            kind = key.GetValueKind(valueName);
        }
        catch (IOException)
        {
            return GpuPreferenceValue.Missing; // gerade gelöscht
        }

        return kind switch
        {
            RegistryValueKind.String => new GpuPreferenceValue(GpuPreferenceValueKind.String, raw as string),
            RegistryValueKind.ExpandString => new GpuPreferenceValue(GpuPreferenceValueKind.ExpandString, raw as string),
            RegistryValueKind.MultiString => new GpuPreferenceValue(GpuPreferenceValueKind.MultiString, null),
            RegistryValueKind.DWord => new GpuPreferenceValue(GpuPreferenceValueKind.DWord, Convert.ToString(raw, CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new GpuPreferenceValue(GpuPreferenceValueKind.QWord, Convert.ToString(raw, CultureInfo.InvariantCulture)),
            RegistryValueKind.Binary => new GpuPreferenceValue(GpuPreferenceValueKind.Binary, null),
            _ => new GpuPreferenceValue(GpuPreferenceValueKind.Other, null),
        };
    }

    public void WriteString(string valueName, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(valueName);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("REG_SZ mit eingebettetem NUL wird nie geschrieben.", nameof(value));
        }

        using var key = Registry.CurrentUser.CreateSubKey(location.KeyPath, writable: true)
            ?? throw new IOException("Registry-Schlüssel ließ sich nicht anlegen: " + location.KeyName);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        ArgumentException.ThrowIfNullOrEmpty(valueName);
        using var key = Registry.CurrentUser.OpenSubKey(location.KeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
