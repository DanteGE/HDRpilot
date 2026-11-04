namespace HdrPilot.Cli;

/// <summary>Exit-Codes der Kommandozeile. Dokumentiert im Handbuch (docs/manual.md, docs/handbuch.md) und in `hdrpilot help`.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int DisplayNotFound = 3;
    public const int NotHdrCapable = 4;
    public const int SwitchFailed = 5;
    public const int GameNotFound = 6;
}
