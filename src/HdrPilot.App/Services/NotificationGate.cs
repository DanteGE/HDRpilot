using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>QUERY_USER_NOTIFICATION_STATE aus shellapi.h.</summary>
public enum UserNotificationState
{
    /// <summary>Abfrage fehlgeschlagen.</summary>
    Unknown = 0,

    /// <summary>QUNS_NOT_PRESENT: Bildschirmschoner, gesperrt oder inaktive Benutzersitzung.</summary>
    NotPresent = 1,

    /// <summary>QUNS_BUSY: Vollbild-Anwendung im Vordergrund (auch randlose Vollbild-Spiele).</summary>
    Busy = 2,

    /// <summary>QUNS_RUNNING_D3D_FULL_SCREEN: exklusives Direct3D-Vollbild.</summary>
    RunningDirect3DFullScreen = 3,

    /// <summary>QUNS_PRESENTATION_MODE: Präsentationsmodus.</summary>
    PresentationMode = 4,

    /// <summary>QUNS_ACCEPTS_NOTIFICATIONS: frei.</summary>
    AcceptsNotifications = 5,

    /// <summary>QUNS_QUIET_TIME: erste Stunde nach der Anmeldung (Windows regelt das selbst).</summary>
    QuietTime = 6,

    /// <summary>QUNS_APP: eine Vollbild-App aus dem Store läuft.</summary>
    App = 7,
}

/// <summary>Fragt ab, ob der Nutzer gerade gestört werden darf (für Tests austauschbar).</summary>
public interface IUserNotificationStateProvider
{
    UserNotificationState Query();
}

/// <summary>SHQueryUserNotificationState: kostet nur einen Aufruf, keine Abfrage im Hintergrund.</summary>
public sealed partial class ShellUserNotificationState : IUserNotificationStateProvider
{
    public UserNotificationState Query()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 && Enum.IsDefined((UserNotificationState)state)
                ? (UserNotificationState)state
                : UserNotificationState.Unknown;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return UserNotificationState.Unknown;
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}

public enum NotificationDecision
{
    Show,

    /// <summary>Hinweise sind in den Einstellungen aus.</summary>
    SuppressedDisabled,

    /// <summary>Vollbild-Spiel, Präsentation oder gesperrter Bildschirm: nur protokolliert.</summary>
    SuppressedBusy,
}

/// <summary>
/// Entscheidet vor jeder Meldung, ob sie angezeigt werden darf. Kein Hinweis darf ein Spiel im Vollbild stören:
/// Bei Vollbild, Präsentation oder gesperrtem Bildschirm wird die Meldung nur protokolliert.
/// </summary>
public sealed class NotificationGate(Func<bool> enabled, IUserNotificationStateProvider state, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>True für Zustände, in denen keine Meldung erscheinen darf.</summary>
    public static bool IsBusy(UserNotificationState state) => state is UserNotificationState.Busy
        or UserNotificationState.RunningDirect3DFullScreen
        or UserNotificationState.PresentationMode
        or UserNotificationState.NotPresent
        or UserNotificationState.App;

    public NotificationDecision Evaluate(string title, string message)
    {
        if (!enabled())
        {
            _logger.LogInformation("Hinweis nicht angezeigt (Hinweise aus): {Title} | {Message}", title, message);
            return NotificationDecision.SuppressedDisabled;
        }

        var current = state.Query();
        if (IsBusy(current))
        {
            _logger.LogInformation("Hinweis nicht angezeigt (Vollbild/Präsentation, Zustand {State}): {Title} | {Message}", current, title, message);
            return NotificationDecision.SuppressedBusy;
        }

        return NotificationDecision.Show;
    }
}
