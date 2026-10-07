using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// winuser.h (SDK 10.0.26100.0): Hauptfenster aufzählen und ihren Besitzer lesen. Liest nur Fensterdaten aus win32k,
// öffnet keinen Prozess, schickt keine Nachricht an fremde Fenster und hängt sich nirgends ein (kein Hook).
#pragma warning disable IDE1006

internal static unsafe partial class WindowNative
{
    /// <summary>Ruft den Rückruf für jedes Hauptfenster auf (Rückgabe 0 bricht ab).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    /// <summary>WS_VISIBLE des Fensters und seiner Eltern; auch minimierte Fenster gelten als sichtbar.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    /// <summary>Thread und Prozess, die das Fenster erzeugt haben (ohne Handle auf den Prozess).</summary>
    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
#pragma warning restore IDE1006
