using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Config;

/// <summary>
/// Ablageorte. Konfiguration roamt mit dem Profil (%APPDATA%\HdrPilot), maschinenbezogene Daten
/// (gemerkter HDR-Zustand mit monitorDevicePath, Protokolle) liegen lokal (%LOCALAPPDATA%\HdrPilot).
/// </summary>
public sealed record AppPaths(string ConfigDirectory, string DataDirectory)
{
    public string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public string SessionStateFile => Path.Combine(DataDirectory, "session-state.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>HDR-Unterstützung laut PCGamingWiki je Spiel (Zwischenspeicher des Abgleichs).</summary>
    public string HdrSupportFile => Path.Combine(DataDirectory, "hdr-support.json");

    /// <summary>Vollständige HDR-Liste aus dem Abgleich mit Konto.</summary>
    public string HdrCatalogFile => Path.Combine(DataDirectory, "hdr-catalog.json");

    /// <summary>PCGamingWiki-Konto (Benutzername und Bot-Passwort), mit DPAPI für den Windows-Benutzer verschlüsselt.</summary>
    public string PcgwAccountFile => Path.Combine(DataDirectory, "pcgw-account.dat");

    /// <summary>Eigene Symbole der Spiele (Rechtsklick „Symbol ändern…“), je Spiel eine PNG mit 96 px.</summary>
    public string IconDirectory => Path.Combine(DataDirectory, "icons");

    /// <summary>Gemerkte Wahl der Symbolquelle je Spiel, damit ein Kaltstart keine Spielordner durchsucht (E86).</summary>
    public string IconCacheFile => Path.Combine(DataDirectory, "icon-cache.json");

    public static AppPaths Default(IAppEnvironment environment) => new(
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDRpilot"),
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDRpilot"));

    /// <summary>Alles unter einem Ordner (Tests, Kommandozeile mit --config-dir).</summary>
    public static AppPaths InDirectory(string directory) => new(directory, directory);
}
