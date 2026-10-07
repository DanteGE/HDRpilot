using System.Globalization;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Config;

public sealed record ConfigLoadResult(AppConfig Config, bool CreatedNew, string? Warning);

/// <summary>Liest und schreibt config.json. Schreiben ist atomar (temporäre Datei, dann ersetzen).</summary>
public sealed class ConfigStore(IFileSystem fileSystem, string path, IClock clock, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public string Path => path;

    public ConfigLoadResult Load()
    {
        if (!fileSystem.FileExists(path))
        {
            return new ConfigLoadResult(new AppConfig(), CreatedNew: true, Warning: null);
        }

        string text;
        try
        {
            text = fileSystem.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht lesbar (gesperrt): Standard verwenden, Datei aber nicht überschreiben lassen wir den Aufrufer entscheiden.
            _logger.LogError(ex, "Konfiguration {Path} nicht lesbar.", path);
            return new ConfigLoadResult(new AppConfig(), CreatedNew: false, Warning: "Konfiguration nicht lesbar: " + ex.Message);
        }

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            var backup = Backup(text, "defekt");
            _logger.LogError(ex, "Konfiguration {Path} ist defekt, gesichert als {Backup}. Starte mit Standardwerten.", path, backup);
            return new ConfigLoadResult(new AppConfig(), CreatedNew: false, Warning: $"Konfiguration war defekt und wurde als {backup} gesichert.");
        }

        if (config is null)
        {
            return new ConfigLoadResult(new AppConfig(), CreatedNew: false, Warning: "Konfiguration war leer.");
        }

        string? warning = null;
        if (config.Version > AppConfig.CurrentVersion)
        {
            var backup = Backup(text, "v" + config.Version.ToString(CultureInfo.InvariantCulture));
            warning = $"Konfiguration stammt aus einer neueren Version ({config.Version}); Kopie unter {backup}.";
            _logger.LogWarning("{Warning}", warning);
        }

        Normalize(config);
        return new ConfigLoadResult(config, CreatedNew: false, warning);
    }

    public void Save(AppConfig config)
    {
        config.Version = AppConfig.CurrentVersion;
        fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(config, ConfigJson.Options));
    }

    /// <summary>Bereinigt Nullwerte und doppelte Spiele (nach Schlüssel), begrenzt Zahlenwerte.</summary>
    internal static void Normalize(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        var s = config.Settings;
        s.TargetDisplays ??= [];
        s.SearchFolders ??= [];
        s.PollIntervalMs = Math.Clamp(s.PollIntervalMs, 250, 10_000);
        s.GracePeriodSeconds = Math.Clamp(s.GracePeriodSeconds, 0, 600);
        s.LaunchTimeoutSeconds = Math.Clamp(s.LaunchTimeoutSeconds, 10, 1800);

        config.Games ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        config.Games = config.Games
            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.SourceId) && seen.Add(g.Key))
            .ToList();
        foreach (var g in config.Games)
        {
            g.Name ??= g.SourceId;
            g.InstallDir ??= string.Empty;
            g.KnownExecutables ??= [];
            g.DetectedExecutables ??= [];
            g.ExcludedExecutables ??= [];
            g.ExtraExecutables ??= [];
        }
    }

    private string Backup(string text, string tag)
    {
        var backupPath = path + "." + tag + "-" + clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
        try
        {
            fileSystem.WriteAllTextAtomic(backupPath, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Sicherung {Backup} fehlgeschlagen.", backupPath);
        }

        return backupPath;
    }
}
