using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Höhe und Lage von Dialogen, die mit ihrem Inhalt wachsen („Bild wählen…“, E93): nie höher als die Arbeitsfläche des
/// Bildschirms, auf dem das Besitzerfenster steht (nicht nur des Hauptbildschirms), und beim Wachsen um die Mitte, ohne die
/// Arbeitsfläche zu verlassen. Die Rechnungen sind reine Funktionen (Tests); nur <see cref="WorkAreaFor"/> fragt Windows
/// (user32, ohne Adminrechte).
/// </summary>
public static partial class DialogPlacement
{
    /// <summary>Abstand zum Rand der Arbeitsfläche (oben und unten je die Hälfte).</summary>
    public const double Margin = 24;

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>Arbeitsfläche (ohne Taskleiste) des Bildschirms von <paramref name="owner"/> in DIP; sonst die des Hauptbildschirms.</summary>
    public static Rect WorkAreaFor(Window? owner)
    {
        if (owner is not null && new WindowInteropHelper(owner).Handle is var handle && handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) && PresentationSource.FromVisual(owner)?.CompositionTarget is { } target)
            {
                var toDip = target.TransformFromDevice;
                var topLeft = toDip.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = toDip.Transform(new Point(info.Work.Right, info.Work.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }

        return SystemParameters.WorkArea;
    }

    /// <summary>Höchste Höhe des Dialogs: die Arbeitsfläche abzüglich <see cref="Margin"/>, nie unter <paramref name="minHeight"/>.</summary>
    public static double MaxHeightFor(Rect work, double minHeight) => Math.Max(minHeight, work.Height - Margin);

    /// <summary>
    /// Neue Oberkante beim Wachsen von <paramref name="oldHeight"/> auf <paramref name="newHeight"/>: um die Mitte, dann in die
    /// Arbeitsfläche geschoben (12 DIP Abstand oben und unten).
    /// </summary>
    public static double GrowTop(Rect work, double top, double oldHeight, double newHeight)
    {
        var centred = top - ((newHeight - oldHeight) / 2);
        var highest = work.Top + (Margin / 2);
        var lowest = work.Bottom - (Margin / 2) - newHeight;
        return Math.Max(highest, Math.Min(centred, lowest));
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
