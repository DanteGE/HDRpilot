namespace HdrPilot.Cli;

/// <summary>Exit-Codes der Kommandozeile. Dokumentiert in README und `hdrpilot help`.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int DisplayNotFound = 3;
    public const int NotHdrCapable = 4;
    public const int SwitchFailed = 5;
}
