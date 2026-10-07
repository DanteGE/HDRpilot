namespace HdrPilot.Core.Sessions;

/// <summary>Wodurch eine Sitzung begann.</summary>
public enum SessionTrigger
{
    /// <summary>Weg A: Spielprozess von der Überwachung erkannt.</summary>
    ProcessDetected,

    /// <summary>Weg B: Start über die App (HDR vor dem Spielstart).</summary>
    LaunchedViaApp,

    /// <summary>Nach einem Neustart von HdrPilot lief das Spiel noch.</summary>
    Recovery,

    /// <summary>Steam-Startoption (<c>HdrPilot.exe --wrap -- %command%</c>): HDR vor dem Spielstart, das Spiel startet der Wrapper.</summary>
    SteamLaunchOption,
}

public enum SessionPhase
{
    /// <summary>Weg B: HDR ist an, der Spielprozess ist noch nicht erschienen.</summary>
    Launching,

    Running,

    /// <summary>Kein Spielprozess mehr, Nachlaufzeit läuft.</summary>
    Ending,
}

public enum NotificationKind
{
    HdrEnabled,
    HdrRestored,
    DisplayNotHdrCapable,
    DisplayNotConnected,
    SwitchFailed,
    LaunchFailed,

    /// <summary>Der gemerkte HDR-Zustand ließ sich nicht auf die Platte schreiben.</summary>
    StateNotSaved,
}

/// <summary>Kurze Meldung für den Tray-Hinweis.</summary>
public sealed record SessionNotification(NotificationKind Kind, string Title, string Message);

/// <summary>Öffentliche Sicht auf eine laufende Sitzung (für Oberfläche und Diagnose).</summary>
public sealed record SessionInfo(
    string GameKey,
    string GameName,
    SessionPhase Phase,
    SessionTrigger Trigger,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> Displays,
    IReadOnlyList<int> ProcessIds);

public sealed record LaunchOutcome(
    bool Started,
    string Message,
    DateTimeOffset? HdrConfirmedAt,
    DateTimeOffset? LaunchedAt);

/// <summary>Ergebnis der Vorbereitung für einen Start, den ein anderer ausführt (etwa die Steam-Startoption).</summary>
/// <param name="Prepared">HDR-Schritt erledigt, die Sitzung wartet auf das Spiel. False: nichts geschaltet (etwa unbekanntes Spiel).</param>
public sealed record PrepareOutcome(
    bool Prepared,
    string Message,
    DateTimeOffset? HdrConfirmedAt);
