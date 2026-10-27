using System.Globalization;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Config;

public sealed record ConfigLoadResult(AppConfig Config, bool CreatedNew, string? Warning, string? Text = null);

/// <summary>
/// Liest und schreibt config.json. Schreiben ist atomar (temporäre Datei, dann ersetzen). Eine neue Konfiguration bekommt
/// die Sprache von <paramref name="newConfigLanguage"/> (Standard: Anzeigesprache von Windows, <see cref="UiLanguage"/>, E95).
/// </summary>
public sealed class ConfigStore(IFileSystem fileSystem, string path, IClock clock, ILogger? logger = null, Func<string>? newConfigLanguage = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public string Path => path;

    public ConfigLoadResult Load()
    {
        if (!fileSystem.FileExists(path))
        {
            var created = new AppConfig();
            created.Settings.Language = (newConfigLanguage ?? UiLanguage.ForNewConfig)();
            return new ConfigLoadResult(created, CreatedNew: true, Warning: null);
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
        return new ConfigLoadResult(config, CreatedNew: false, warning, text);
    }

    /// <summary>Aktueller Dateiinhalt oder null, wenn die Datei fehlt oder nicht lesbar ist.</summary>
    public string? TryReadText()
    {
        try
        {
            return fileSystem.FileExists(path) ? fileSystem.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Schreibt atomar und liefert den geschriebenen Text (zum Erkennen fremder Änderungen).</summary>
    public string Save(AppConfig config)
    {
        config.Version = AppConfig.CurrentVersion;
        var text = JsonSerializer.Serialize(config, ConfigJson.Options);
        fileSystem.WriteAllTextAtomic(path, text);
        return text;
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
        s.Appearance ??= new AppearanceSettings();
        s.Artwork ??= new ArtworkSettings();
        if (s.QuiverFolders is { } quiverFolders)
        {
            // Nur vorhandene Einträge, jeder Ordner einmal; eine leere Liste bleibt leer (sie fehlte nur, wenn nie etwas gelernt wurde).
            s.QuiverFolders = quiverFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        config.UnknownSourceGames ??= [];
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
            g.IconCandidates ??= [];
            if (g.CoverCandidates is { } covers)
            {
                g.CoverCandidates = covers.Count(c => !string.IsNullOrWhiteSpace(c)) == 0 ? null : covers.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            }

            // Nur ein schlichter Dateiname im Symbolordner, nie ein Pfad (eine von Hand geänderte Datei darf nicht
            // auf fremde Dateien zeigen, „Symbol zurücksetzen“ löscht die genannte Datei).
            if (g.CustomIcon is not null && !Games.CustomIcons.IsValidFileName(g.CustomIcon))
            {
                g.CustomIcon = null;
            }

            // Eigene Bildwahl: nur gültige Kennzeichen mit schlichten Dateinamen (E89), sonst automatisch.
            g.Artwork = GameArtworkChoice.Normalize(g.Artwork);
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
