namespace HdrPilot.App.Services;

/// <summary>
/// Notlösung für Spiele, die direkt aus Steam gestartet werden: Diese Startoption (Steam: Eigenschaften &gt; Allgemein
/// &gt; Startoptionen) lässt Steam das Spiel über HdrPilot starten. <c>%command%</c> ersetzt Steam durch den
/// eigentlichen Startbefehl des Spiels; HdrPilot schaltet HDR ein und startet ihn dann.
/// </summary>
public static class SteamLaunchOption
{
    /// <summary>Argumente nach dem Programmpfad von HdrPilot.</summary>
    public const string WrapArguments = "--wrap -- %command%";

    /// <summary>„"C:\…\HdrPilot.exe" --wrap -- %command%“: Pfad immer in Anführungszeichen (Leerzeichen in „Program Files“).</summary>
    public static string Build(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return "\"" + executablePath + "\" " + WrapArguments;
    }
}
