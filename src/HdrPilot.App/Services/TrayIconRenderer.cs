using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using HdrPilot.App.Resources;
using HdrPilot.App.Theming;
using HdrPilot.Core.Sessions;
using Drawing = System.Drawing;
using WpfColor = System.Windows.Media.Color;

namespace HdrPilot.App.Services;

/// <summary>Was das Tray-Symbol zeigt: HDR-Zustand der Zielbildschirme, Pause und laufende Spiele.</summary>
/// <param name="RestartBufferUntil">Ende des Neustart-Puffers, wenn HDR nur noch deshalb an bleibt (sonst null).</param>
/// <param name="RestartBufferHeld">Puffer abgelaufen, das Zurückstellen wartet auf das Ende einer Vollbild-Anwendung.</param>
public sealed record TrayState(
    bool HdrOn,
    bool HdrSupported,
    bool Paused,
    IReadOnlyList<string> ActiveGames,
    DateTimeOffset? RestartBufferUntil = null,
    bool RestartBufferHeld = false)
{
    public static TrayState Initial { get; } = new(HdrOn: false, HdrSupported: true, Paused: false, ActiveGames: []);
}

/// <summary>
/// Farben der Symbole aus dem Farbschema (E89, G10.2): Verlauf der Kachel „HDR an“ (Akzentflächen, Text darauf mindestens
/// 4,5:1) und die beiden Linienfarben der Glyphe „HDR aus“ (Akzent → Atmosphäre, je Taskleiste auf 3:1 gebracht).
/// Unabhängig vom Thema der Fenster: Die Symbole stehen auf der Taskleiste.
/// </summary>
public readonly record struct IconPalette(WpfColor Fill1, WpfColor Fill2, WpfColor Fill3, WpfColor OnAccent, WpfColor Accent, WpfColor Atmosphere)
{
    /// <summary>Auslieferungszustand: Vorlage „Abendrot“ im Duoton.</summary>
    public static IconPalette Default { get; } = From(PaletteEngine.Compute(
        PalettePresets.Default.Colors with { Shine = PalettePresets.DerivedShine(PalettePresets.Default.Colors.Atmosphere) }, dark: true, autoTextOnAccent: true));

    public static IconPalette From(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new(palette.AccentFill1, palette.AccentFill2, palette.AccentFill3, palette.OnAccent, palette.C1, palette.C2);
    }
}

/// <summary>
/// Schlüssel eines gezeichneten Symbols. Gleicher Schlüssel: Symbol wird nicht neu gezeichnet. Neu gezeichnet wird also
/// nur bei Wechsel von HDR, Pause oder laufendem Spiel, des Farbschemas oder des Taskleisten-Themas, nie nach Zeit.
/// </summary>
/// <param name="GameRunning">Ein Spiel läuft mit HDR (grünes Abzeichen); bei Pause gilt das Pause-Abzeichen.</param>
/// <param name="Palette">Farben aus dem Farbschema; null: <see cref="IconPalette.Default"/>.</param>
/// <param name="LightTaskbar">Helle Taskleiste (Windows-Option „Standard-Windows-Modus“ hell): Linienfarben dafür.</param>
public readonly record struct TrayIconSpec(bool HdrOn, bool Paused, bool GameRunning = false, IconPalette? Palette = null, bool LightTaskbar = false)
{
    /// <summary>Wirksame Farben.</summary>
    public IconPalette Colors => Palette ?? IconPalette.Default;
}

/// <summary>Reine Funktionen: Zustand → Symbol und Tooltip (testbar ohne Tray).</summary>
public static class TrayPresentation
{
    /// <summary>Höchstlänge des Tooltips (NOTIFYICONDATA.szTip, NotifyIcon.Text).</summary>
    public const int MaxTooltipLength = 127;

    public static TrayIconSpec IconFor(TrayState state) =>
        new(state.HdrOn, state.Paused, GameRunning: state.HdrOn && state.ActiveGames.Any(g => !string.IsNullOrWhiteSpace(g)));

    /// <inheritdoc cref="TooltipFor(TrayState, DateTimeOffset)"/>
    public static string TooltipFor(TrayState state) => TooltipFor(state, DateTimeOffset.UtcNow);

    /// <summary>
    /// Zeile 1 HDR-Zustand, dann laufende Spiele (falls vorhanden), dann der Neustart-Puffer (falls er läuft), zuletzt
    /// Automatik. Höchstens 127 Zeichen; gekürzt wird nur die Spielezeile.
    /// </summary>
    /// <param name="now">Für den Countdown des Neustart-Puffers.</param>
    public static string TooltipFor(TrayState state, DateTimeOffset now)
    {
        var head = state.HdrOn
            ? ShellStrings.TrayTooltipHdrOn
            : state.HdrSupported ? ShellStrings.TrayTooltipHdrOff : ShellStrings.TrayTooltipNoHdr;
        var tail = state.Paused ? ShellStrings.TrayTooltipPaused : ShellStrings.TrayTooltipActive;
        var buffer = RestartBufferLine(state, now);
        var games = state.ActiveGames.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
        var lines = new List<string> { head };
        if (games.Count > 0)
        {
            var gamesLine = string.Format(
                CultureInfo.CurrentCulture,
                games.Count == 1 ? ShellStrings.TrayTooltipGame : ShellStrings.TrayTooltipGames,
                string.Join(ShellStrings.ListSeparator, games));
            var lineBreaks = buffer is null ? 2 : 3;
            var budget = MaxTooltipLength - head.Length - tail.Length - (buffer?.Length ?? 0) - lineBreaks;
            lines.Add(Fit(gamesLine, Math.Max(budget, 1)));
        }

        if (buffer is not null)
        {
            lines.Add(buffer);
        }

        lines.Add(tail);
        return Fit(string.Join("\n", lines), MaxTooltipLength);
    }

    /// <summary>
    /// Zeile zum Neustart-Puffer: „HDR bleibt noch m:ss an“ bzw. „wartet auf eine Vollbild-Anwendung“. Null, wenn kein
    /// Puffer läuft oder HDR gar nicht an ist.
    /// </summary>
    public static string? RestartBufferLine(TrayState state, DateTimeOffset now)
    {
        if (!state.HdrOn)
        {
            return null;
        }

        if (state.RestartBufferHeld)
        {
            return ShellStrings.TrayTooltipRestartBufferFullscreen;
        }

        if (state.RestartBufferUntil is not { } until)
        {
            return null;
        }

        // Aufgerundet: der Countdown zeigt 0:00 erst, wenn der Puffer wirklich zu Ende ist.
        var seconds = (int)Math.Max(0, Math.Ceiling((until - now).TotalSeconds));
        return string.Format(CultureInfo.CurrentCulture, ShellStrings.TrayTooltipRestartBuffer, seconds / 60, seconds % 60);
    }

    /// <summary>True, solange der Tooltip einen laufenden Countdown zeigt (dann führt ihn das Tray jede Sekunde nach).</summary>
    public static bool HasCountdown(TrayState state, DateTimeOffset now) =>
        state.HdrOn && !state.RestartBufferHeld && state.RestartBufferUntil is { } until && until > now;

    /// <summary>
    /// Neustart-Puffer für das Tray aus den Sitzungen, nur wenn kein Spiel mehr läuft oder startet (sonst bleibt HDR
    /// ohnehin an): spätestes Pufferende und ob eine Sitzung auf das Ende einer Vollbild-Anwendung wartet.
    /// </summary>
    public static (DateTimeOffset? Until, bool Held) RestartBufferOf(IReadOnlyList<SessionInfo> sessions)
    {
        if (sessions.Count == 0 || sessions.Any(s => s.Phase != SessionPhase.Ending))
        {
            return (null, false);
        }

        return (sessions.Max(s => s.BufferUntil), sessions.Any(s => s.HeldForFullscreen));
    }

    /// <summary>Kürzt auf <paramref name="max"/> Zeichen mit Auslassungszeichen.</summary>
    public static string Fit(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var ellipsis = ShellStrings.Ellipsis;
        return max <= ellipsis.Length ? text[..max] : text[..(max - ellipsis.Length)].TrimEnd() + ellipsis;
    }
}

/// <summary>
/// Zeichnet Tray- und Fenstersymbol (E89, G10.2): die Sonne über dem Horizont aus dem Prototyp auf einer Kachel von 24
/// Einheiten. „HDR aus“ (der häufigste Zustand) ist die Linie im Verlauf Akzent → Atmosphäre ohne Kachel, auf mindestens
/// 3:1 gegen die Taskleiste gebracht; „HDR an“ ist die gefüllte Kachel im Verlauf Akzent → Atmosphäre → Glanz mit der
/// Glyphe in der Textfarbe auf Akzent; „Automatik pausiert“ bekommt ein gelbes Pause-Abzeichen (ohne HDR: graue Linie),
/// „Spiel läuft“ ein grünes Abzeichen. Unter 24 px gilt die vereinfachte Glyphe mit dickeren, am Pixelraster
/// ausgerichteten Strichen. Größen 8 bis 256 px.
/// </summary>
public static partial class TrayIconRenderer
{
    /// <summary>Kleinste und größte Kantenlänge.</summary>
    public const int MinSize = 8;

    public const int MaxSize = 256;

    private const int SM_CXSMICON = 49;

    /// <summary>Hintergrund der Taskleiste (Windows 11): dunkel und hell.</summary>
    public static readonly WpfColor DarkTaskbar = WpfColor.FromRgb(0x1C, 0x1D, 0x24);

    public static readonly WpfColor LightTaskbar = WpfColor.FromRgb(0xF3, 0xF4, 0xF8);

    private static readonly Drawing.Color PauseBadge = Drawing.Color.FromArgb(255, 0xFF, 0xB0, 0x20);
    private static readonly Drawing.Color PauseBars = Drawing.Color.FromArgb(255, 0x1A, 0x12, 0x06);
    private static readonly Drawing.Color RunBadge = Drawing.Color.FromArgb(255, 0x2B, 0xD6, 0x7B);
    private static readonly Drawing.Color RunMark = Drawing.Color.FromArgb(255, 0x06, 0x21, 0x0F);
    private static readonly WpfColor PausedGlyphOnDark = WpfColor.FromRgb(0xA4, 0xA9, 0xBC);
    private static readonly WpfColor PausedGlyphOnLight = WpfColor.FromRgb(0x5A, 0x60, 0x76);

    /// <summary>Kantenlänge kleiner Symbole bei der System-DPI (16 px bei 100 %, 20 bei 125 %, 24 bei 150 %).</summary>
    public static int SystemSmallIconSize()
    {
        try
        {
            var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
            return size is >= 16 and <= 256 ? size : 16;
        }
        catch (EntryPointNotFoundException)
        {
            return 16;
        }
    }

    /// <summary>Hintergrund der Taskleiste, für die das Symbol gezeichnet wird.</summary>
    public static WpfColor TaskbarColor(TrayIconSpec spec) => spec.LightTaskbar ? LightTaskbar : DarkTaskbar;

    /// <summary>Linienfarben der Glyphe „HDR aus“: Akzent und Atmosphäre, je auf mindestens 3:1 gegen die Taskleiste.</summary>
    public static (WpfColor Start, WpfColor End) OffGlyphColors(TrayIconSpec spec)
    {
        var bar = TaskbarColor(spec);
        var lighten = !spec.LightTaskbar;
        return (
            PaletteEngine.ReadableOnAll(spec.Colors.Accent, [bar], lighten, PaletteEngine.ControlContrast),
            PaletteEngine.ReadableOnAll(spec.Colors.Atmosphere, [bar], lighten, PaletteEngine.ControlContrast));
    }

    /// <summary>Linienfarbe der Glyphe bei pausierter Automatik ohne HDR (grau, mindestens 3:1 gegen die Taskleiste).</summary>
    public static WpfColor PausedGlyphColor(TrayIconSpec spec) => spec.LightTaskbar ? PausedGlyphOnLight : PausedGlyphOnDark;

    /// <summary>Zeichnet das Symbol als 32-bit-ARGB-Bitmap. Der Aufrufer gibt sie frei.</summary>
    public static Drawing.Bitmap Render(TrayIconSpec spec, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);
        var bitmap = new Drawing.Bitmap(size, size, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Drawing.Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Halbe Pixel: Pixel i deckt [i, i+1] ab wie in WPF und SVG; ganzzahlige Striche liegen so genau im Raster.
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Drawing.Color.Transparent);
            var glyph = new Glyph(size);
            if (spec.HdrOn)
            {
                DrawOnTile(g, glyph, spec.Colors);
            }
            else if (spec.Paused)
            {
                DrawLineGlyph(g, glyph, ToDrawing(PausedGlyphColor(spec)), ToDrawing(PausedGlyphColor(spec)));
            }
            else
            {
                var (start, end) = OffGlyphColors(spec);
                DrawLineGlyph(g, glyph, ToDrawing(start), ToDrawing(end));
            }

            if (spec.Paused)
            {
                DrawPauseBadge(g, glyph, ToDrawing(TaskbarColor(spec)));
            }
            else if (spec.HdrOn && spec.GameRunning)
            {
                DrawRunBadge(g, glyph, ToDrawing(TaskbarColor(spec)));
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Erzeugt das Symbol mit eigenem HICON. Freigabe über <see cref="TrayIcon.Dispose"/> (DestroyIcon).</summary>
    public static TrayIcon CreateIcon(TrayIconSpec spec, int size)
    {
        using var bitmap = Render(spec, size);
        var handle = bitmap.GetHicon();
        return new TrayIcon(spec, size, handle);
    }

    /// <summary>Gefüllte Kachel „HDR an“: Verlauf c1 → c2 → c3, halbe Sonne gefüllt, Horizont und Strahlen in der Schrift auf Akzent.</summary>
    private static void DrawOnTile(Drawing.Graphics g, Glyph glyph, IconPalette colors)
    {
        var s = glyph.Scale;
        var tile = glyph.Small
            ? new Drawing.RectangleF(0.5f * s, 0.5f * s, 23f * s, 23f * s)
            : new Drawing.RectangleF(1f * s, 1f * s, 22f * s, 22f * s);
        using (var path = RoundedRectangle(tile, (glyph.Small ? 6f : 6.5f) * s))
        using (var brush = new LinearGradientBrush(new Drawing.PointF(0, 0), new Drawing.PointF(glyph.Size, glyph.Size), ToDrawing(colors.Fill1), ToDrawing(colors.Fill3)))
        {
            brush.InterpolationColors = new ColorBlend
            {
                Colors = [ToDrawing(colors.Fill1), ToDrawing(colors.Fill2), ToDrawing(colors.Fill3)],
                Positions = [0f, 0.55f, 1f],
            };
            g.FillPath(brush, path);
        }

        var on = ToDrawing(colors.OnAccent);
        var width = glyph.Stroke(glyph.Small ? 2.8f : 2.2f);
        var horizon = glyph.SnapY(glyph.Small ? 17f : 16.5f, width);
        var radius = (glyph.Small ? 5.5f : 5f) * s;
        var center = glyph.SnapX(12f, width);
        using (var sun = new GraphicsPath())
        {
            sun.AddArc(center - radius, horizon - radius, radius * 2, radius * 2, 180, 180);
            sun.CloseFigure();
            using var fill = new Drawing.SolidBrush(on);
            g.FillPath(fill, sun);
        }

        using var ink = new Drawing.SolidBrush(on);
        using var pen = RoundPen(ink, width);
        if (glyph.Small)
        {
            g.DrawLine(pen, 3.5f * s, horizon, 20.5f * s, horizon);
            g.DrawLine(pen, center, 5f * s, center, 8.5f * s);
        }
        else
        {
            g.DrawLine(pen, 4f * s, horizon, 20f * s, horizon);
            g.DrawLine(pen, center, 6f * s, center, 8.4f * s);
            g.DrawLine(pen, 6.2f * s, 9f * s, 7.8f * s, 10.6f * s);
            g.DrawLine(pen, 17.8f * s, 9f * s, 16.2f * s, 10.6f * s);
        }
    }

    /// <summary>Linien-Glyphe ohne Kachel: Horizont, Sonnenbogen und Strahlen, Strich im Verlauf von links oben nach rechts unten.</summary>
    private static void DrawLineGlyph(Drawing.Graphics g, Glyph glyph, Drawing.Color start, Drawing.Color end)
    {
        var s = glyph.Scale;
        var width = glyph.Stroke(glyph.Small ? 3f : 2.4f);
        using var gradient = new LinearGradientBrush(new Drawing.PointF(0, 0), new Drawing.PointF(glyph.Size, glyph.Size), start, end);
        using var pen = RoundPen(gradient, width);
        var horizon = glyph.SnapY(glyph.Small ? 17.5f : 16.5f, width);
        var radius = (glyph.Small ? 6f : 5f) * s;
        var center = glyph.SnapX(12f, width);
        if (glyph.Small)
        {
            g.DrawLine(pen, 2.5f * s, horizon, 21.5f * s, horizon);
            g.DrawArc(pen, center - radius, horizon - radius, radius * 2, radius * 2, 180, 180);
            g.DrawLine(pen, center, 4.5f * s, center, 8.1f * s);
        }
        else
        {
            g.DrawLine(pen, 3.5f * s, horizon, 20.5f * s, horizon);
            g.DrawArc(pen, center - radius, horizon - radius, radius * 2, radius * 2, 180, 180);
            g.DrawLine(pen, center, 5.5f * s, center, 8.1f * s);
            g.DrawLine(pen, 6f * s, 8.8f * s, 7.8f * s, 10.6f * s);
            g.DrawLine(pen, 18f * s, 8.8f * s, 16.2f * s, 10.6f * s);
        }
    }

    /// <summary>Gelbes Pause-Abzeichen rechts unten mit Rand in der Taskleistenfarbe (hebt es von der Glyphe ab).</summary>
    private static void DrawPauseBadge(Drawing.Graphics g, Glyph glyph, Drawing.Color edge)
    {
        var s = glyph.Scale;
        var box = glyph.Small
            ? new Drawing.RectangleF(14.5f * s, 14.5f * s, 9.5f * s, 9.5f * s)
            : new Drawing.RectangleF(15.5f * s, 15.5f * s, 8f * s, 8f * s);
        using (var path = RoundedRectangle(box, (glyph.Small ? 2.6f : 2.4f) * s))
        {
            using var fill = new Drawing.SolidBrush(PauseBadge);
            g.FillPath(fill, path);
            using var border = new Drawing.Pen(edge, Math.Max(1f, 1.2f * s));
            g.DrawPath(border, path);
        }

        var bar = Math.Max(1f, (float)Math.Round((glyph.Small ? 1.6f : 1.3f) * s));
        using var pen = new Drawing.Pen(PauseBars, bar);
        var top = box.Top + (box.Height * 0.27f);
        var bottom = box.Bottom - (box.Height * 0.27f);
        var gap = box.Width * 0.14f;
        var middle = box.Left + (box.Width / 2f);
        g.DrawLine(pen, middle - gap, top, middle - gap, bottom);
        g.DrawLine(pen, middle + gap, top, middle + gap, bottom);
    }

    /// <summary>Grünes Abzeichen „Spiel läuft“ rechts unten; ab 24 px mit Startdreieck.</summary>
    private static void DrawRunBadge(Drawing.Graphics g, Glyph glyph, Drawing.Color edge)
    {
        var s = glyph.Scale;
        var (cx, cy, r) = glyph.Small ? (19f * s, 19f * s, 5f * s) : (19.5f * s, 19.5f * s, 4.3f * s);
        using (var fill = new Drawing.SolidBrush(RunBadge))
        {
            g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
        }

        using (var border = new Drawing.Pen(edge, Math.Max(1f, 1.2f * s)))
        {
            g.DrawEllipse(border, cx - r, cy - r, r * 2, r * 2);
        }

        if (!glyph.Small)
        {
            using var mark = new Drawing.SolidBrush(RunMark);
            g.FillPolygon(mark, [new Drawing.PointF(18.4f * s, 17.6f * s), new Drawing.PointF(21.4f * s, 19.5f * s), new Drawing.PointF(18.4f * s, 21.4f * s)]);
        }
    }

    private static Drawing.Pen RoundPen(Drawing.Brush brush, float width) => new(brush, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };

    private static Drawing.Color ToDrawing(WpfColor c) => Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    private static GraphicsPath RoundedRectangle(Drawing.RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    /// <summary>
    /// Maßstab der 24er-Kachel. Unter 24 px gilt die kleine Glyphe; ihre Striche werden auf ganze Pixel gerundet und so
    /// gelegt, dass sie genau Pixelreihen füllen (bei 16 px gestochen scharf statt zweier halb deckender Reihen).
    /// </summary>
    private readonly struct Glyph(int size)
    {
        public int Size { get; } = size;

        public float Scale { get; } = size / 24f;

        public bool Small { get; } = size < 24;

        /// <summary>Strichbreite in Pixeln; klein auf ganze Pixel gerundet (mindestens 1).</summary>
        public float Stroke(float units)
        {
            var px = units * Scale;
            return Small ? Math.Max(1f, MathF.Round(px)) : px;
        }

        /// <summary>Mitte einer waagrechten Linie: gerade Breite auf eine Pixelgrenze, ungerade auf eine Pixelmitte.</summary>
        public float SnapY(float units, float width) => Small ? Snap(units * Scale, width) : units * Scale;

        public float SnapX(float units, float width) => Small ? Snap(units * Scale, width) : units * Scale;

        private static float Snap(float center, float width) =>
            ((int)MathF.Round(width) % 2 == 0) ? MathF.Round(center) : MathF.Floor(center) + 0.5f;
    }
}

/// <summary>Gezeichnetes Tray-Symbol mit eigenem HICON. Dispose ruft DestroyIcon (kein GDI-/USER-Leck).</summary>
public sealed partial class TrayIcon : IDisposable
{
    private IntPtr _handle;

    internal TrayIcon(TrayIconSpec spec, int size, IntPtr handle)
    {
        Spec = spec;
        Size = size;
        _handle = handle;
        Icon = Drawing.Icon.FromHandle(handle);
    }

    public TrayIconSpec Spec { get; }

    public int Size { get; }

    /// <summary>Hülle ohne eigenen Besitz des Handles (Icon.FromHandle); gültig bis <see cref="Dispose"/>.</summary>
    public Drawing.Icon Icon { get; }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Icon.Dispose();
        DestroyIcon(handle);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
