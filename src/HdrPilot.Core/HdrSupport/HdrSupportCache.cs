using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>Ergebnis des Abgleichs für ein Spiel der Bibliothek.</summary>
public sealed class HdrSupportEntry
{
    public HdrSupportStatus Status { get; set; }

    /// <summary>Gefundene Wiki-Seite; null, wenn PCGamingWiki das Spiel nicht kennt (oder nicht in der Liste führt).</summary>
    public string? PcgwTitle { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>Name des Spiels beim Abgleich. Ändert er sich (Umbenennung im Launcher), wird neu geprüft.</summary>
    public string? Name { get; set; }

    /// <summary>Ohne Konto: Seite unter dem Namen nicht gefunden, die Suche steht noch aus.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SearchPending { get; set; }
}

/// <summary>Inhalt von %LOCALAPPDATA%\HdrPilot\hdr-support.json.</summary>
public sealed class HdrSupportData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Beginn des letzten Abgleichs (auch eines gescheiterten): Grundlage für „höchstens einmal pro Stunde“.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Ende des letzten erfolgreichen Abgleichs („Stand“).</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Fehler des letzten Abgleichs; null, wenn er geklappt hat.</summary>
    public PcgwErrorKind? LastError { get; set; }

    /// <summary>Letzter erfolgreicher Abgleich lief mit Konto (vollständige Liste).</summary>
    public bool AccountMode { get; set; }

    /// <summary>Ergebnis je Spielschlüssel („Steam:1091500“).</summary>
    public Dictionary<string, HdrSupportEntry> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Inhalt von %LOCALAPPDATA%\HdrPilot\hdr-catalog.json: vollständige Liste HDR-fähiger Spiele (nur mit Konto).</summary>
public sealed class HdrCatalogData
{
    public int Version { get; set; } = 1;

    public DateTimeOffset FetchedAt { get; set; }

    public List<HdrCatalogEntry> Entries { get; set; } = [];
}

/// <summary>
/// Zwischenspeicher der HDR-Liste: hdr-support.json (Ergebnis je Spiel, klein, liest die Spielebibliothek) und
/// hdr-catalog.json (vollständige Liste aus dem Konto-Abgleich, nur für den Abgleich selbst). Beide werden atomar
/// geschrieben; eine defekte oder unlesbare Datei gilt als leer und wird beim nächsten Abgleich ersetzt.
/// </summary>
public sealed class HdrSupportCache(IFileSystem fileSystem, AppPaths paths, ILogger? logger = null)
{
    private static readonly JsonSerializerOptions CompactOptions = new(ConfigJson.Options) { WriteIndented = false };
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public HdrSupportData Load()
    {
        var data = Read<HdrSupportData>(paths.HdrSupportFile) ?? new HdrSupportData();
        var games = new Dictionary<string, HdrSupportEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, entry) in data.Games ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key) && entry is not null)
            {
                games[key] = entry;
            }
        }

        data.Games = games;
        return data;
    }

    public void Save(HdrSupportData data)
    {
        data.Version = HdrSupportData.CurrentVersion;
        fileSystem.WriteAllTextAtomic(paths.HdrSupportFile, JsonSerializer.Serialize(data, ConfigJson.Options));
    }

    /// <summary>Nur das Ergebnis je Spiel (für die Spielebibliothek).</summary>
    public IReadOnlyDictionary<string, HdrSupportStatus> LoadStatuses() =>
        Load().Games.Where(g => g.Value.Status != HdrSupportStatus.Unknown)
            .ToDictionary(g => g.Key, g => g.Value.Status, StringComparer.OrdinalIgnoreCase);

    public HdrCatalogData? LoadCatalog()
    {
        var catalog = Read<HdrCatalogData>(paths.HdrCatalogFile);
        catalog?.Entries.RemoveAll(e => e is null || string.IsNullOrWhiteSpace(e.Page));
        return catalog;
    }

    /// <summary>Ohne Einrückung: die Liste kann einige tausend Einträge haben.</summary>
    public void SaveCatalog(HdrCatalogData catalog) =>
        fileSystem.WriteAllTextAtomic(paths.HdrCatalogFile, JsonSerializer.Serialize(catalog, CompactOptions));

    public void DeleteCatalog() => fileSystem.DeleteFile(paths.HdrCatalogFile);

    private T? Read<T>(string path)
        where T : class
    {
        try
        {
            return fileSystem.FileExists(path) ? JsonSerializer.Deserialize<T>(fileSystem.ReadAllText(path), ConfigJson.Options) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning("HDR-Liste: {Path} nicht lesbar ({Error}), gilt als leer.", path, ex.Message);
            return null;
        }
    }
}
