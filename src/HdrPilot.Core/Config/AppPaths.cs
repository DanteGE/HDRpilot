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

    public static AppPaths Default(IAppEnvironment environment) => new(
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HdrPilot"),
        Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HdrPilot"));

    /// <summary>Alles unter einem Ordner (Tests, Kommandozeile mit --config-dir).</summary>
    public static AppPaths InDirectory(string directory) => new(directory, directory);
}
