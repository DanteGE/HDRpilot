using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HdrPilot.App.Services.Display;

namespace HdrPilot.App.Infrastructure;

/// <summary>Fenstergröße für ein Bildschirmfoto in Bildschirmpixeln, etwa 1920×1080.</summary>
public readonly record struct SnapshotSize(int Width, int Height)
{
    /// <summary>Größen der Sichtprüfung von Phase 10 (G10.5, G10.8).</summary>
    public static IReadOnlyList<SnapshotSize> Review { get; } = [new(1280, 800), new(1920, 1080), new(3440, 1440)];

    /// <summary>„1920x1080“ (auch mit „×“); Breite 320–7680, Höhe 240–4320.</summary>
    public static bool TryParse(string? text, out SnapshotSize size)
    {
        size = default;
        var parts = (text ?? string.Empty).Trim().Split('x', 'X', '×');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || w is < 320 or > 7680
            || h is < 240 or > 4320)
        {
            return false;
        }

        size = new SnapshotSize(w, h);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}

/// <summary>
/// Ein Zustand eines Fensters für Bildschirmfotos (E54, erweitert mit E89). <see cref="Window"/> und <see cref="Name"/>
/// ergeben den Dateinamen (<see cref="Id"/>, etwa „hauptfenster-raster“). <see cref="Prepare"/> bringt das angezeigte
/// Fenster in den Zustand (Ansicht wählen, HDR-Moment an einer Stelle anhalten …) und läuft nach dem Einschwingen.
/// </summary>
public sealed record SnapshotScene(string Window, string Name, Func<Window> Create, Func<Window, Task>? Prepare = null)
{
    /// <summary>Wartezeit nach dem Anzeigen (Daten, Bilder, Einblendungen), vor <see cref="Prepare"/>.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Was über dem Fenster liegt, aber nicht Teil davon sein kann, etwa ein Kontextmenü: Als echtes Popup ginge es auf dem
    /// sichtbaren Bildschirm auf und käme nicht ins Bild. Wird nach <see cref="Prepare"/> abgefragt und darübergezeichnet.
    /// </summary>
    public Func<Window, IReadOnlyList<SnapshotOverlay>>? Overlays { get; init; }

    /// <summary>
    /// False für Dialoge mit fester Größe (etwa „Bild wählen…“): Sie werden in ihrer eigenen Größe gerendert, nicht in den
    /// Größen des Plans, die sie nur leer auseinanderziehen würden, wie es der Nutzer nie sieht.
    /// </summary>
    public bool Resizable { get; init; } = true;

    public string Id => Window + "-" + Name;
}

/// <summary>Element, das über das Fenster gezeichnet wird, mit seiner linken oberen Ecke in geräteunabhängigen Pixeln des Fensters.</summary>
public sealed record SnapshotOverlay(FrameworkElement Element, Point Position);

/// <summary>
/// Was gerendert wird: Größen (leer: Startgröße des Fensters), Auswahl der Zustände (leer: alle; ein Eintrag passt auf
/// <see cref="SnapshotScene.Id"/>, <see cref="SnapshotScene.Window"/> oder <see cref="SnapshotScene.Name"/>), Themen
/// (true dunkel, false hell) und auf Wunsch feste Werte eines Bildschirms (null: echte Werte über DXGI).
/// </summary>
public sealed record SnapshotPlan(IReadOnlyList<SnapshotSize> Sizes, IReadOnlyList<string> Scenes, IReadOnlyList<bool> Themes, DisplayNits? Display = null)
{
    /// <summary>Wie bisher: alle Zustände in Startgröße, dunkel und hell.</summary>
    public static SnapshotPlan Default { get; } = new([], [], [true, false]);

    /// <summary>
    /// Aus den Schaltern <c>--snapshot-sizes</c> („1280x800,1920x1080“ oder „review“), <c>--snapshot-scenes</c> (Komma-Liste),
    /// <c>--snapshot-themes</c> („dunkel,hell“) und <c>--snapshot-display</c> („Name;Spitze;Vollbild;Minimum;Bit“). Fehlende
    /// Angaben: Standard. Wirft <see cref="FormatException"/>.
    /// </summary>
    public static SnapshotPlan Parse(string? sizes, string? scenes, string? themes, string? display = null)
    {
        var sizeList = new List<SnapshotSize>();
        foreach (var part in Split(sizes))
        {
            if (string.Equals(part, "review", StringComparison.OrdinalIgnoreCase))
            {
                sizeList.AddRange(SnapshotSize.Review);
            }
            else if (SnapshotSize.TryParse(part, out var size))
            {
                sizeList.Add(size);
            }
            else
            {
                throw new FormatException("Ungültige Größe für --snapshot-sizes: " + part);
            }
        }

        var themeList = new List<bool>();
        foreach (var part in Split(themes))
        {
            themeList.Add(part.ToLowerInvariant() switch
            {
                "dunkel" or "dark" => true,
                "hell" or "light" => false,
                _ => throw new FormatException("Ungültiges Thema für --snapshot-themes: " + part),
            });
        }

        return new SnapshotPlan(
            sizeList.Distinct().ToList(),
            Split(scenes).ToList(),
            themeList.Count == 0 ? Default.Themes : themeList.Distinct().ToList(),
            ParseDisplay(display));
    }

    /// <summary>„MAG 341C OLED;456;254;0;10“: Name, Spitze, Vollbild und Minimum in Nits (Punkt als Dezimalzeichen), Bit je Farbe.</summary>
    private static DisplayNits? ParseDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(';', StringSplitOptions.TrimEntries);
        static bool Nits(string s, out double value) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0;
        if (parts.Length != 5
            || parts[0].Length == 0
            || !Nits(parts[1], out var peak) || peak <= 0
            || !Nits(parts[2], out var full)
            || !Nits(parts[3], out var min)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var bits))
        {
            throw new FormatException("Ungültige Angabe für --snapshot-display: " + text);
        }

        return new DisplayNits("snapshot", parts[0], peak, full, min, bits, HdrActive: false, PeakMeasuredWithHdr: true);
    }

    public bool Includes(SnapshotScene scene) => Scenes.Count == 0 || Scenes.Any(s =>
        string.Equals(s, scene.Id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(s, scene.Window, StringComparison.OrdinalIgnoreCase)
        || string.Equals(s, scene.Name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Split(string? text) =>
        (text ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Entwicklungswerkzeug (<c>HdrPilot.exe --snapshot &lt;ordner&gt; --config-dir &lt;ordner&gt;</c>, E54): rendert Fenster
/// außerhalb des sichtbaren Bildschirms in PNG-Dateien, um das Aussehen hell und dunkel zu prüfen, ohne den Nutzer zu stören.
/// Mit <c>--snapshot-sizes</c>, <c>--snapshot-scenes</c> und <c>--snapshot-themes</c> (E89) je Zustand in festen Größen
/// (auch größer als der Bildschirm, etwa 3440×1440 bei ausgeschaltetem Monitor): Dateiname
/// <c>&lt;fenster&gt;-&lt;zustand&gt;[-&lt;B&gt;x&lt;H&gt;]-&lt;dunkel|hell&gt;.png</c>, Bildgröße genau B×H Pixel.
/// </summary>
public static class SnapshotRenderer
{
    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>Rendert alle Zustände des Plans; liefert die geschriebenen Dateien.</summary>
    public static async Task<IReadOnlyList<string>> RenderAsync(Application app, IReadOnlyList<SnapshotScene> scenes, SnapshotPlan plan, string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        try
        {
            foreach (var dark in plan.Themes)
            {
                ThemeManager.Override = dark;
                ThemeManager.Apply(app);
                foreach (var scene in scenes.Where(plan.Includes))
                {
                    IReadOnlyList<SnapshotSize?> sizes = plan.Sizes.Count == 0 || !scene.Resizable ? [null] : plan.Sizes.Select(s => (SnapshotSize?)s).ToList();
                    foreach (var size in sizes)
                    {
                        var path = Path.Combine(directory, FileName(scene, size, dark));
                        await RenderOneAsync(scene, size, path);
                        written.Add(path);
                    }
                }
            }
        }
        finally
        {
            ThemeManager.Override = null;
            ThemeManager.Apply(app);
        }

        return written;
    }

    /// <summary>Dateiname eines Zustands: „hauptfenster-raster-1920x1080-dunkel.png“ bzw. ohne Größe „hauptfenster-raster-hell.png“.</summary>
    public static string FileName(SnapshotScene scene, SnapshotSize? size, bool dark) =>
        scene.Id + (size is { } s ? "-" + s : string.Empty) + (dark ? "-dunkel" : "-hell") + ".png";

    private static async Task RenderOneAsync(SnapshotScene scene, SnapshotSize? size, string path)
    {
        var window = scene.Create();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        HwndSource? source = null;
        HwndSourceHook? hook = null;
        if (size is { } fixedSize)
        {
            // Feste Größe auch über dem Bildschirm (Vollbild 3440×1440 bei kleinerem oder ausgeschaltetem Monitor):
            // Höchstmaße des Fensters aufheben und Windows die Größe nicht auf den Bildschirm begrenzen lassen.
            window.SizeToContent = SizeToContent.Manual;
            window.MaxWidth = double.PositiveInfinity;
            window.MaxHeight = double.PositiveInfinity;
            var handle = new WindowInteropHelper(window).EnsureHandle();
            source = HwndSource.FromHwnd(handle);
            hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_GETMINMAXINFO)
                {
                    var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    info.MaxTrackSize.X = Math.Max(info.MaxTrackSize.X, fixedSize.Width + 64);
                    info.MaxTrackSize.Y = Math.Max(info.MaxTrackSize.Y, fixedSize.Height + 64);
                    Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                }

                return IntPtr.Zero;
            };
            source?.AddHook(hook);
        }

        try
        {
            window.Show();
            var dpi = VisualTreeHelper.GetDpi(window);
            if (size is { } s)
            {
                window.Width = s.Width / dpi.DpiScaleX;
                window.Height = s.Height / dpi.DpiScaleY;
            }

            // Layout, Daten und Bilder laden lassen, Einblend-Animationen abwarten.
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(scene.Settle);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (scene.Prepare is { } prepare)
            {
                await prepare(window);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }

            Save(window, path, size, scene.Overlays?.Invoke(window) ?? []);
        }
        finally
        {
            if (source is not null && hook is not null)
            {
                source.RemoveHook(hook);
            }

            window.Close();
        }
    }

    private static void Save(Window window, string path, SnapshotSize? size, IReadOnlyList<SnapshotOverlay> overlays)
    {
        var content = (FrameworkElement)window.Content;
        var root = (Visual)(VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) : content);
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = size?.Width ?? (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = size?.Height ?? (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        foreach (var overlay in overlays)
        {
            // Ohne Eltern angeordnet; über einen Pinsel mit festem Ausschnitt an seine Stelle gezeichnet (wie gerendert, ohne Versatz).
            var element = overlay.Element;
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var box = new Rect(element.DesiredSize);
            element.Arrange(box);
            element.UpdateLayout();
            var layer = new DrawingVisual();
            using (var dc = layer.RenderOpen())
            {
                var brush = new VisualBrush(element)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = box,
                };
                dc.DrawRectangle(brush, null, new Rect(overlay.Position, box.Size));
            }

            bitmap.Render(layer);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
}
