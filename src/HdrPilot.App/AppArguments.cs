namespace HdrPilot.App;

/// <summary>
/// Befehlszeile der App. Ein zweiter Aufruf reicht seine Befehle an die laufende Instanz weiter.
/// <list type="bullet">
/// <item><c>--run &lt;Quelle:ID&gt;</c> Start über HdrPilot (HDR vorher an), etwa aus einer Verknüpfung.</item>
/// <item><c>--show</c> Fenster öffnen; <c>--minimized</c> nur Tray (Autostart).</item>
/// <item><c>--toggle-hdr</c>, <c>--pause</c>, <c>--resume</c>, <c>--rescan</c>, <c>--exit</c> wie im Tray-Menü.</item>
/// <item><c>--config-dir &lt;ordner&gt;</c> eigene Ablage (Tests); eigene Instanz je Ordner.</item>
/// <item><c>--autohdr-test-root &lt;name&gt;</c> nur mit <c>--config-dir</c>: Auto HDR schreibt unter
/// HKCU\Software\HDRpilot.Tests\AutoHdr\&lt;name&gt;. Ohne den Schalter gilt dort ein aus der Ablage abgeleiteter Name; eine
/// eigene Ablage erreicht den echten Schlüssel nie (E88). Wird nie weitergereicht.</item>
/// <item><c>--wrap -- %command%</c> Steam-Startoption: erledigt <c>App</c> vor allem anderen über die rohe Befehlszeile
/// (siehe <see cref="HdrPilot.Core.Launch.SteamWrapper"/>); wird nie weitergereicht.</item>
/// <item><c>--prepare &lt;Quelle:ID&gt; --ready-event &lt;name&gt;</c> vom Wrapper an die laufende Instanz: HDR für einen Start
/// vorbereiten, den der Wrapper selbst ausführt, und danach das Ereignis setzen.</item>
/// <item>Nur für Entwicklung und Prüfskripte, nur mit <c>--config-dir</c> (E54, E89): <c>--snapshot &lt;ordner&gt;</c> mit
/// <c>--snapshot-sizes</c>, <c>--snapshot-scenes</c>, <c>--snapshot-themes</c> und <c>--snapshot-display</c> (siehe
/// <see cref="Infrastructure.SnapshotPlan"/>),
/// <c>--artwork-report &lt;datei&gt;</c>, <c>--settings-probe &lt;datei&gt;</c> (E90), <c>--test-hdr-moment</c> und
/// <c>--test-toolbar-search</c> (die letzten beiden werden weitergereicht).</item>
/// <item><c>--</c> beendet die Befehle: alles danach (der Spielbefehl) wird nie als Befehl von HdrPilot gelesen.</item>
/// </list>
/// </summary>
public sealed record AppArguments
{
    public string? RunGameKey { get; init; }

    /// <summary>Steam-Startoption: HDR für dieses Spiel vorbereiten, das Spiel startet der Wrapper.</summary>
    public string? PrepareGameKey { get; init; }

    /// <summary>Ereignis, das nach der Vorbereitung gesetzt wird (Name aus <see cref="HdrPilot.Core.Launch.PrepareSignal"/>).</summary>
    public string? ReadyEvent { get; init; }

    /// <summary>Aufruf als Steam-Startoption (<c>--wrap</c>); wird nie weitergereicht.</summary>
    public bool Wrap { get; init; }

    public bool Show { get; init; }

    public bool Minimized { get; init; }

    public bool ToggleHdr { get; init; }

    public bool? Pause { get; init; }

    public bool Rescan { get; init; }

    public bool Exit { get; init; }

    public bool OpenSettings { get; init; }

    public string? ConfigDir { get; init; }

    /// <summary>Name der Auto-HDR-Testwurzel (nur zusammen mit <see cref="ConfigDir"/> beachtet, E88).</summary>
    public string? AutoHdrTestRoot { get; init; }

    /// <summary>Entwicklung: Hauptfenster und Einstellungen hell und dunkel unsichtbar als PNG in diesen Ordner rendern, dann beenden.</summary>
    public string? SnapshotDir { get; init; }

    /// <summary>Entwicklung (E89): Größen der Bildschirmfotos, etwa „1280x800,1920x1080,3440x1440“ oder „review“.</summary>
    public string? SnapshotSizes { get; init; }

    /// <summary>Entwicklung (E89): Auswahl der Zustände, etwa „hauptfenster-raster,einstellungen“.</summary>
    public string? SnapshotScenes { get; init; }

    /// <summary>Entwicklung (E89): Themen der Bildschirmfotos, „dunkel“, „hell“ oder beide.</summary>
    public string? SnapshotThemes { get; init; }

    /// <summary>
    /// Entwicklung (E89): Werte eines Bildschirms für die Bildschirmfotos, „Name;Spitze;Vollbild;Minimum;Bit“, etwa
    /// „MAG 341C OLED;456;254;0;10“, wenn der Monitor aus ist und DXGI nichts meldet.
    /// </summary>
    public string? SnapshotDisplay { get; init; }

    /// <summary>
    /// Entwicklung (E89, G10.3): Abdeckung der Bibliothek mit lokalen Bildern als JSON in diese Datei schreiben, dann beenden.
    /// Nur mit eigener Ablage (--config-dir); liest die Launcher-Caches nur.
    /// </summary>
    public string? ArtworkReport { get; init; }

    /// <summary>
    /// Prüfung (E90, G11.3): die Konfiguration der eigenen Ablage wie die Einstellungen laden, einmal unverändert und einmal
    /// mit umgeschaltetem „Nicht installierte Spiele ausblenden“ speichern und einen Bericht als JSON in diese Datei schreiben,
    /// dann beenden. Nur mit eigener Ablage (--config-dir); ohne Automatik, ohne Einlesen, ohne Fenster.
    /// </summary>
    public string? SettingsProbe { get; init; }

    /// <summary>
    /// Prüfung (E89, G10.9): den HDR-Moment in der Bibliothek abspielen, ohne HDR zu schalten. Wirkt nur in einer Instanz
    /// mit eigener Ablage (--config-dir) und wird an sie weitergereicht.
    /// </summary>
    public bool TestHdrMoment { get; init; }

    /// <summary>
    /// Prüfung (G10.5): in der Bibliothek Strg+F ausführen wie gedrückt (schaltet die Werkzeugleiste um: eingeklappt klappt sie
    /// dauerhaft aus und die Suche bekommt den Fokus, ausgeklappt klappt sie ein), ohne Tastatureingabe. Wirkt nur in einer Instanz mit eigener Ablage (--config-dir) und
    /// wird an sie weitergereicht.
    /// </summary>
    public bool TestToolbarSearch { get; init; }

    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>True, wenn der Aufruf etwas tun soll außer die App zu starten.</summary>
    public bool HasCommand => RunGameKey is not null || PrepareGameKey is not null || ReadyEvent is not null
        || Show || ToggleHdr || Pause is not null || Rescan || Exit || OpenSettings || TestHdrMoment || TestToolbarSearch;

    /// <summary>
    /// Eine eigene Ablage, wie sie die Entwicklerschalter brauchen (<c>--snapshot</c>, <c>--artwork-report</c>,
    /// <c>--settings-probe</c>): <see cref="ConfigDir"/> ist gesetzt und weder der echte Konfigurationsordner noch der echte
    /// Datenordner (<paramref name="defaults"/>), auch nicht in anderer Schreibweise oder auf einem anderen Weg dorthin
    /// (<see cref="Core.Util.PathUtil.SameDirectory"/>: 8.3-Namen, <c>\\?\</c>, Junctions, subst). Sonst schrieben sie in die
    /// echte Konfiguration, womöglich neben der laufenden App.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(ConfigDir))]
    public bool HasOwnConfigDir(Core.Config.AppPaths defaults)
    {
        if (string.IsNullOrWhiteSpace(ConfigDir))
        {
            return false;
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(ConfigDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }

        return !Core.Util.PathUtil.SameDirectory(full, defaults.ConfigDirectory) && !Core.Util.PathUtil.SameDirectory(full, defaults.DataDirectory);
    }

    public static AppArguments Parse(IReadOnlyList<string> args)
    {
        var result = new AppArguments();
        var unknown = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                break; // dahinter steht der Spielbefehl der Steam-Startoption, nie ein Befehl von HdrPilot
            }

            switch (a.ToLowerInvariant())
            {
                case "--run" when i + 1 < args.Count:
                    result = result with { RunGameKey = args[++i] };
                    break;
                case "--prepare" when i + 1 < args.Count:
                    result = result with { PrepareGameKey = args[++i] };
                    break;
                case "--ready-event" when i + 1 < args.Count:
                    result = result with { ReadyEvent = args[++i] };
                    break;
                case "--wrap":
                    result = result with { Wrap = true };
                    break;
                case "--config-dir" when i + 1 < args.Count:
                    result = result with { ConfigDir = args[++i] };
                    break;
                case "--autohdr-test-root" when i + 1 < args.Count:
                    result = result with { AutoHdrTestRoot = args[++i] };
                    break;
                case "--snapshot" when i + 1 < args.Count:
                    result = result with { SnapshotDir = args[++i] };
                    break;
                case "--snapshot-sizes" when i + 1 < args.Count:
                    result = result with { SnapshotSizes = args[++i] };
                    break;
                case "--snapshot-scenes" when i + 1 < args.Count:
                    result = result with { SnapshotScenes = args[++i] };
                    break;
                case "--snapshot-themes" when i + 1 < args.Count:
                    result = result with { SnapshotThemes = args[++i] };
                    break;
                case "--snapshot-display" when i + 1 < args.Count:
                    result = result with { SnapshotDisplay = args[++i] };
                    break;
                case "--artwork-report" when i + 1 < args.Count:
                    result = result with { ArtworkReport = args[++i] };
                    break;
                case "--settings-probe" when i + 1 < args.Count:
                    result = result with { SettingsProbe = args[++i] };
                    break;
                case "--test-hdr-moment":
                    result = result with { TestHdrMoment = true };
                    break;
                case "--test-toolbar-search":
                    result = result with { TestToolbarSearch = true };
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

    /// <summary>
    /// Was ein zweiter Aufruf an die laufende Instanz weiterreicht: seine Befehle; ohne Befehl das Fenster öffnen;
    /// nur <c>--minimized</c> (Autostart, während HdrPilot schon läuft): nichts.
    /// </summary>
    public IReadOnlyList<string> ToRunningInstance() => HasCommand ? ToForwardedArgs() : Minimized ? [] : ["--show"];

    /// <summary>Befehle für die Weitergabe an die laufende Instanz (ohne --config-dir, --autohdr-test-root und --wrap).</summary>
    public IReadOnlyList<string> ToForwardedArgs()
    {
        var list = new List<string>();
        if (RunGameKey is not null)
        {
            list.Add("--run");
            list.Add(RunGameKey);
        }

        if (PrepareGameKey is not null)
        {
            list.Add("--prepare");
            list.Add(PrepareGameKey);
        }

        if (ReadyEvent is not null)
        {
            list.Add("--ready-event");
            list.Add(ReadyEvent);
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

        if (TestHdrMoment)
        {
            list.Add("--test-hdr-moment");
        }

        if (TestToolbarSearch)
        {
            list.Add("--test-toolbar-search");
        }

        return list;
    }
}
