using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>
/// Fenstersymbol in den Palettenfarben (E89, G10.2): dieselbe Zeichnung wie das Tray-Symbol in allen Größen, die Windows
/// für Titelleiste, Taskleiste, Alt+Tab und Explorer wählt, als Symboldatei im Speicher. WPF nimmt daraus je Verwendung das
/// passende Einzelbild (BitmapFrame eines IconBitmapDecoder).
/// </summary>
public static class WindowIconRenderer
{
    /// <summary>Größen der Einzelbilder in Pixeln.</summary>
    public static IReadOnlyList<int> Sizes { get; } = [16, 20, 24, 32, 48, 64, 128, 256];

    /// <summary>Symboldatei (ICO mit PNG-Einzelbildern) für den Zustand.</summary>
    public static byte[] RenderIco(TrayIconSpec spec)
    {
        var frames = Sizes.Select(size => (Size: size, Png: RenderPng(spec, size))).ToList();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // ICONDIR: reserviert, Typ 1 (Symbol), Anzahl; danach je Bild ein ICONDIRENTRY mit 16 Byte.
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);
            var offset = 6 + (16 * frames.Count);
            foreach (var (size, png) in frames)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }

            foreach (var (_, png) in frames)
            {
                writer.Write(png);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Fenstersymbol als eingefrorenes Bild mit allen Größen (für <see cref="System.Windows.Window.Icon"/>).</summary>
    public static BitmapFrame Render(TrayIconSpec spec)
    {
        using var stream = new MemoryStream(RenderIco(spec));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        if (frame.CanFreeze)
        {
            frame.Freeze();
        }

        return frame;
    }

    private static byte[] RenderPng(TrayIconSpec spec, int size)
    {
        using var bitmap = TrayIconRenderer.Render(spec, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

/// <summary>
/// Gemeinsames Fenstersymbol aller Fenster mit eigenem Rahmen (<see cref="Infrastructure.ChromeWindow"/>): Farben aus der
/// Darstellung, Zustand (HDR an/aus, pausiert, Spiel läuft) aus dem Tray. Gezeichnet wird erst, wenn ein Fenster das Symbol
/// braucht, und nur einmal je Zustand; ohne offenes Fenster kostet ein Zustandswechsel nichts. Solange die App keine
/// Darstellung angemeldet hat (Setup), gilt das feste Programmsymbol.
/// </summary>
public sealed class WindowIconSource
{
    private ImageSource? _icon;
    private TrayIconSpec _iconSpec;

    /// <summary>Das Symbol der App.</summary>
    public static WindowIconSource Shared { get; } = new();

    /// <summary>Zustand oder Farben haben sich geändert (auf dem Thread des Aufrufers, in der App der UI-Thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Protokoll für Zeichenfehler (Standard: keins).</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>True, sobald die Darstellung Farben gesetzt hat; vorher zeigen Fenster das feste Programmsymbol.</summary>
    public bool IsActive { get; private set; }

    public TrayIconSpec Spec { get; private set; } = new(HdrOn: false, Paused: false);

    /// <summary>Angemeldete Fenster (Prüfung: geschlossene Fenster melden sich ab).</summary>
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Wie oft gezeichnet wurde (Prüfung: nie ohne Bedarf, nie zweimal für denselben Zustand).</summary>
    public int RenderCount { get; private set; }

    /// <summary>Aktuelles Symbol; null ohne Darstellung oder wenn das Zeichnen scheiterte (dann bleibt das feste Programmsymbol).</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!IsActive)
            {
                return null;
            }

            if (_icon is null || _iconSpec != Spec)
            {
                var spec = Spec;
                try
                {
                    _icon = WindowIconRenderer.Render(spec);
                    RenderCount++;
                }
                catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException or IOException or NotSupportedException)
                {
                    Logger.LogWarning(ex, "Fenstersymbol ließ sich nicht zeichnen; das feste Programmsymbol bleibt.");
                    _icon = null;
                }

                _iconSpec = spec;
            }

            return _icon;
        }
    }

    /// <summary>Farben aus der Darstellung und Thema der Taskleiste.</summary>
    public void SetAppearance(IconPalette palette, bool lightTaskbar)
    {
        var activated = !IsActive;
        IsActive = true;
        Update(Spec with { Palette = palette, LightTaskbar = lightTaskbar }, activated);
    }

    /// <summary>Zustand aus dem Tray.</summary>
    public void SetState(bool hdrOn, bool paused, bool gameRunning) =>
        Update(Spec with { HdrOn = hdrOn, Paused = paused, GameRunning = gameRunning }, force: false);

    private void Update(TrayIconSpec next, bool force)
    {
        if (next == Spec && !force)
        {
            return;
        }

        Spec = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
