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
