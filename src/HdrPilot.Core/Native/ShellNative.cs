using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// shellapi.h (SDK 10.0.26100.0): QUERY_USER_NOTIFICATION_STATE. Fragt nur den Zustand der Shell ab, öffnet keinen Prozess.
#pragma warning disable IDE1006

internal static partial class ShellNative
{
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;
    public const int QUNS_APP = 7;

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int pquns);

    /// <summary>True, wenn eine Vollbild-Anwendung (Spiel, Präsentation) im Vordergrund läuft.</summary>
    public static bool IsFullscreenAppRunning() =>
        SHQueryUserNotificationState(out var state) == 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE or QUNS_APP;
}
#pragma warning restore IDE1006
