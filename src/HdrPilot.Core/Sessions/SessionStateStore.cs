using System.Text.Json;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Sessions;

/// <summary>Gemerkter Zustand eines Bildschirms, solange HdrPilot ihn für Spiele geschaltet hat.</summary>
public sealed class DisplayClaimState
{
    public string MonitorDevicePath { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>HDR-Zustand vor dem ersten Spiel. Wird beim Ende des letzten Spiels wiederhergestellt.</summary>
    public bool WasHdrOn { get; set; }

    /// <summary>False, wenn mindestens ein beteiligtes Spiel „nach dem Spiel zurückstellen“ ausgeschaltet hat.</summary>
    public bool Restore { get; set; } = true;

    /// <summary>Schlüssel der Spiele, die diesen Bildschirm gerade brauchen.</summary>
    public List<string> Games { get; set; } = [];

    public DateTimeOffset SavedAt { get; set; }
}

public sealed class SessionStateData
{
    public int Version { get; set; } = 1;

    public List<DisplayClaimState> Displays { get; set; } = [];
}

public interface ISessionStateStore
{
    SessionStateData? Load();

    void Save(SessionStateData data);

    void Delete();
}

/// <summary>session-state.json, atomar geschrieben. Wird sofort nach jeder Änderung geschrieben.</summary>
public sealed class FileSessionStateStore(IFileSystem fileSystem, string path, ILogger? logger = null) : ISessionStateStore
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public SessionStateData? Load()
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<SessionStateData>(fileSystem.ReadAllText(path), ConfigJson.Options);
            data?.Displays.RemoveAll(d => d is null || string.IsNullOrWhiteSpace(d.MonitorDevicePath));
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Gemerkter HDR-Zustand {Path} nicht lesbar.", path);
            return null;
        }
    }

    public void Save(SessionStateData data) =>
        fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(data, ConfigJson.Options));

    public void Delete() => fileSystem.DeleteFile(path);
}
