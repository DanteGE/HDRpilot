namespace HdrPilot.App;

/// <summary>
/// Befehlszeile der App. Ein zweiter Aufruf reicht seine Befehle an die laufende Instanz weiter.
/// <list type="bullet">
/// <item><c>--run &lt;Quelle:ID&gt;</c> Start über HdrPilot (HDR vorher an), etwa aus einer Verknüpfung.</item>
/// <item><c>--show</c> Fenster öffnen; <c>--minimized</c> nur Tray (Autostart).</item>
/// <item><c>--toggle-hdr</c>, <c>--pause</c>, <c>--resume</c>, <c>--rescan</c>, <c>--exit</c> wie im Tray-Menü.</item>
/// <item><c>--config-dir &lt;ordner&gt;</c> eigene Ablage (Tests); eigene Instanz je Ordner.</item>
/// </list>
/// </summary>
public sealed record AppArguments
{
    public string? RunGameKey { get; init; }

    public bool Show { get; init; }

    public bool Minimized { get; init; }

    public bool ToggleHdr { get; init; }

    public bool? Pause { get; init; }

    public bool Rescan { get; init; }

    public bool Exit { get; init; }

    public bool OpenSettings { get; init; }

    public string? ConfigDir { get; init; }

    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>True, wenn der Aufruf etwas tun soll außer die App zu starten.</summary>
    public bool HasCommand => RunGameKey is not null || Show || ToggleHdr || Pause is not null || Rescan || Exit || OpenSettings;

    public static AppArguments Parse(IReadOnlyList<string> args)
    {
        var result = new AppArguments();
        var unknown = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--run" when i + 1 < args.Count:
                    result = result with { RunGameKey = args[++i] };
                    break;
                case "--config-dir" when i + 1 < args.Count:
                    result = result with { ConfigDir = args[++i] };
                    break;
                case "--show":
                    result = result with { Show = true };
                    break;
                case "--settings":
                    result = result with { OpenSettings = true };
                    break;
                case "--minimized":
                    result = result with { Minimized = true };
                    break;
                case "--toggle-hdr":
                    result = result with { ToggleHdr = true };
                    break;
                case "--pause":
                    result = result with { Pause = true };
                    break;
                case "--resume":
                    result = result with { Pause = false };
                    break;
                case "--rescan":
                    result = result with { Rescan = true };
                    break;
                case "--exit":
                    result = result with { Exit = true };
                    break;
                default:
                    unknown.Add(a);
                    break;
            }
        }

        return result with { Unknown = unknown };
    }

    /// <summary>Befehle für die Weitergabe an die laufende Instanz (ohne --config-dir).</summary>
    public IReadOnlyList<string> ToForwardedArgs()
    {
        var list = new List<string>();
        if (RunGameKey is not null)
        {
            list.Add("--run");
            list.Add(RunGameKey);
        }

        if (Show)
        {
            list.Add("--show");
        }

        if (OpenSettings)
        {
            list.Add("--settings");
        }

        if (ToggleHdr)
        {
            list.Add("--toggle-hdr");
        }

        if (Pause is { } p)
        {
            list.Add(p ? "--pause" : "--resume");
        }

        if (Rescan)
        {
            list.Add("--rescan");
        }

        if (Exit)
        {
            list.Add("--exit");
        }

        return list;
    }
}
