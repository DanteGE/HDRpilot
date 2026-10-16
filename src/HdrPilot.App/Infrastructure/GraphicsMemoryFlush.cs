using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Gibt den Grafikspeicher geschlossener Fenster frei (G10.9). WPF hält die Texturen eines geschlossenen Fensters samt
/// Grafikgerät, bis wieder ein Bild gezeichnet wird; ohne offenes Fenster geschieht das nie. Gemessen nach 80 gewählten
/// Spielen: rund 230 MB im Grafiktreiber, die erst beim nächsten Öffnen frei wurden (mit Software-Darstellung entstehen sie
/// gar nicht, ein GC erreicht sie nicht). Deshalb zeichnet ein 1×1-Hilfsfenster ganz unten in der Fensterreihenfolge (hinter
/// dem Desktop, nie aktiv, ohne Eintrag in Taskleiste und Alt+Tab) drei Bilder und schließt sich wieder; danach gibt WPF
/// Texturen und Gerät frei. Ein Fenster außerhalb aller Bildschirme reicht nicht, WPF zeichnet es nicht über die
/// Grafikkarte; ohne Pausen zwischen den Bildern auch nicht (gemessen). Kein Bildtakt: Jedes Bild entsteht, weil sich der
/// Inhalt ändert.
/// </summary>
public static partial class GraphicsMemoryFlush
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndBottom = new(1);

    /// <summary>Pause nach jedem geänderten Bild, damit der Darstellungs-Thread es wirklich zeichnet und zeigt.</summary>
    private static readonly TimeSpan FramePause = TimeSpan.FromMilliseconds(100);

    /// <summary>Pause nach dem Schließen, bevor gesammelt wird (der Darstellungs-Thread baut das Ziel ab).</summary>
    private static readonly TimeSpan ReleasePause = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Hilfsfenster zeigen, drei Bilder zeichnen lassen, schließen und dem Darstellungs-Thread kurz Zeit lassen, Texturen und
    /// Gerät abzubauen (UI-Thread, einmalig, zusammen rund 0,6 s).
    /// </summary>
    public static async Task RunAsync()
    {
        var parameters = new HwndSourceParameters("HDRpilot", 1, 1)
        {
            PositionX = 0,
            PositionY = 0,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExNoActivate | WsExToolWindow | WsExTransparent,
        };
        using (var source = new HwndSource(parameters))
        {
            var dot = new Border { Width = 1, Height = 1, Background = Brushes.Black };
            source.RootVisual = dot;
            SetWindowPos(source.Handle, HwndBottom, 0, 0, 1, 1, SwpNoActivate | SwpShowWindow);
            foreach (var brush in new[] { Brushes.DimGray, Brushes.Black, Brushes.DimGray })
            {
                dot.Background = brush; // geändert, damit wirklich ein neues Bild entsteht
                await Task.Delay(FramePause);
            }
        }

        await Task.Delay(ReleasePause);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
