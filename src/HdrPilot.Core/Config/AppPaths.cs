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

    /// <summary>
    /// Gemerkter globaler Auto-HDR-Schalter samt Originalzustand, solange HDRpilot ihn für einen Spielstart hält (E88).
    /// Maschinenbezogen wie der gemerkte HDR-Zustand.
    /// </summary>
    public string AutoHdrGlobalFile => Path.Combine(DataDirectory, "autohdr-global.json");

    /// <summary>
    /// Kennzeichen „erledigt“ für einen <see cref="AutoHdrGlobalFile"/>, der sich nicht löschen ließ (E88): eigene Datei, weil
    /// eine Sperre auf dem Merker unter NTFS auch sein Ersetzen verhindert. Existiert nur bis zum Löschen des Merkers.
    /// </summary>
    public string AutoHdrGlobalDoneFile => Path.Combine(DataDirectory, "autohdr-global.done");

    /// <summary>
    /// Spielbilder (E89): Verzeichnis index.json (Herkunft, Umgebungsfarben, Zuschnitte je Spiel) und daraus abgeleitete
    /// Bilder (erzeugte Ersatzbilder, zugeschnittene Logos, helle HDR-Fassungen, eigene Bilder, SteamGridDB).
    /// Maschinenbezogen wie die Symbole; gelesen wird aus den Launcher-Caches nur.
    /// </summary>
    public string ArtDirectory => Path.Combine(DataDirectory, "art");

    /// <summary>Verzeichnis der Spielbilder (siehe <see cref="ArtDirectory"/>).</summary>
    public string ArtIndexFile => Path.Combine(ArtDirectory, "index.json");

    /// <summary>Eigene Bilder („Bild wählen…“), je Spiel und Bildart eine Datei.</summary>
    public string CustomArtDirectory => Path.Combine(ArtDirectory, "custom");

    /// <summary>SteamGridDB-API-Schlüssel des Nutzers, mit DPAPI für den Windows-Benutzer verschlüsselt (nur wenn eingetragen).</summary>
    public string SteamGridDbKeyFile => Path.Combine(DataDirectory, "steamgriddb-key.dat");

    /// <summary>
    /// Zuletzt bei eingeschaltetem HDR gemessene Helligkeit je Bildschirm (DXGI, E89): Maximum aller Nit-Skalen, auch
    /// wenn HDR gerade aus ist.
    /// </summary>
    public string DisplayLuminanceFile => Path.Combine(DataDirectory, "display-luminance.json");

    public static AppPaths Default(IAppEnvironment environment) => new(
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDRpilot"),
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDRpilot"));

    /// <summary>Alles unter einem Ordner (Tests, Kommandozeile mit --config-dir).</summary>
    public static AppPaths InDirectory(string directory) => new(directory, directory);
}
