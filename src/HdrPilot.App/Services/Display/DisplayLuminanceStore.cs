using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Display;

/// <summary>Zuletzt bei eingeschaltetem HDR gemessene Werte eines Bildschirms.</summary>
public sealed class StoredLuminance
{
    public double PeakNits { get; set; }

    public double FullFrameNits { get; set; }

    public double MinNits { get; set; }

    public int Bits { get; set; }

    public DateTimeOffset ReadAt { get; set; }
}

/// <summary>Inhalt von display-luminance.json.</summary>
public sealed class StoredLuminanceData
{
    public int Version { get; set; } = DisplayLuminanceStore.CurrentVersion;

    /// <summary>Werte je monitorDevicePath.</summary>
    public Dictionary<string, StoredLuminance> Displays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// <c>%LOCALAPPDATA%\HDRpilot\display-luminance.json</c> (E89): die Helligkeit jedes Bildschirms, zuletzt bei eingeschaltetem
/// HDR gemessen (Spitze, Vollbild, Minimum, Farbtiefe). Daraus kommt das Maximum aller Nit-Skalen, auch wenn HDR gerade aus
/// ist. Eine unlesbare Datei gilt als leer; geschrieben wird atomar und nur, wenn sich Werte ändern.
/// </summary>
public sealed class DisplayLuminanceStore(IFileSystem fileSystem, string path, ILogger? logger = null)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private StoredLuminanceData? _data;

    public string Path => path;

    /// <summary>Gemerkter Stand eines Bildschirms; null ohne Messung mit HDR.</summary>
    public StoredLuminance? Get(string monitorDevicePath)
    {
        lock (_gate)
        {
            return Load().Displays.TryGetValue(monitorDevicePath, out var stored) ? stored : null;
        }
    }

    /// <summary>Merkt eine Messung mit eingeschaltetem HDR; true, wenn sich etwas geändert hat (dann ist gespeichert).</summary>
    public bool Remember(string monitorDevicePath, double peak, double fullFrame, double min, int bits, DateTimeOffset readAt)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Displays.TryGetValue(monitorDevicePath, out var known)
                && known.PeakNits == peak && known.FullFrameNits == fullFrame && known.MinNits == min && known.Bits == bits)
            {
                return false;
            }

            data.Displays[monitorDevicePath] = new StoredLuminance { PeakNits = peak, FullFrameNits = fullFrame, MinNits = min, Bits = bits, ReadAt = readAt };
            try
            {
                fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(data, Options));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Helligkeit: {Path} nicht gespeichert ({Message}).", path, ex.Message);
            }

            return true;
        }
    }

    private StoredLuminanceData Load()
    {
        if (_data is not null)
        {
            return _data;
        }

        try
        {
            if (fileSystem.FileExists(path)
                && JsonSerializer.Deserialize<StoredLuminanceData>(fileSystem.ReadAllText(path), Options) is { Version: CurrentVersion } data)
            {
                data.Displays = new Dictionary<string, StoredLuminance>(data.Displays ?? [], StringComparer.OrdinalIgnoreCase);
                return _data = data;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning("Helligkeit: {Path} nicht lesbar ({Error}), wird neu angelegt.", path, ex.GetType().Name);
        }

        return _data = new StoredLuminanceData();
    }
}
