using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Games.Scanners.Gog;

/// <summary>
/// Werte eines Schlüssels <c>HKLM\Software\GOG.com\Games\&lt;ID&gt;</c> (32-Bit-Sicht). Gemessen: GOG-Galaxy-Installationen
/// schreiben die Namen in camelCase (<c>gameName</c>, <c>path</c>, <c>exe</c>), ältere Offline-Installer in Großbuchstaben
/// (<c>GAMENAME</c>, <c>PATH</c>, <c>EXE</c>). Registry-Wertnamen unterscheiden keine Groß-/Kleinschreibung, beide
/// Varianten werden also über dieselben Namen gelesen.
/// </summary>
internal sealed record GogRegistryEntry
{
    internal const string GameIdValue = "gameID";
    internal const string GameNameValue = "gameName";
    internal const string PathValue = "path";
    internal const string ExeValue = "exe";
    internal const string WorkingDirValue = "workingDir";
    internal const string LaunchParamValue = "launchParam";
    internal const string DependsOnValue = "dependsOn";

    public string? GameId { get; init; }

    public string? GameName { get; init; }

    /// <summary>Installationsordner.</summary>
    public string? Path { get; init; }

    /// <summary>Absoluter Pfad der Start-EXE; fehlt bei Zusatzinhalten.</summary>
    public string? Exe { get; init; }

    public string? WorkingDir { get; init; }

    public string? LaunchParam { get; init; }

    /// <summary>ID des Hauptspiels bei Zusatzinhalten (DLC), sonst leer.</summary>
    public string? DependsOn { get; init; }

    public static GogRegistryEntry Read(IRegistryKey key) => new()
    {
        GameId = key.GetString(GameIdValue),
        GameName = key.GetString(GameNameValue),
        Path = key.GetString(PathValue),
        Exe = key.GetString(ExeValue),
        WorkingDir = key.GetString(WorkingDirValue),
        LaunchParam = key.GetString(LaunchParamValue),
        DependsOn = key.GetString(DependsOnValue),
    };
}
