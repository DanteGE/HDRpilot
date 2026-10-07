using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using HdrPilot.App.Resources;
using Drawing = System.Drawing;

namespace HdrPilot.App.Services;

/// <summary>Was das Tray-Symbol zeigt: HDR-Zustand der Zielbildschirme, Pause und laufende Spiele.</summary>
public sealed record TrayState(bool HdrOn, bool HdrSupported, bool Paused, IReadOnlyList<string> ActiveGames)
{
    public static TrayState Initial { get; } = new(HdrOn: false, HdrSupported: true, Paused: false, ActiveGames: []);
}

/// <summary>Schlüssel eines gezeichneten Symbols. Gleicher Schlüssel: Symbol wird nicht neu gezeichnet.</summary>
public readonly record struct TrayIconSpec(bool HdrOn, bool Paused);

/// <summary>Reine Funktionen: Zustand → Symbol und Tooltip (testbar ohne Tray).</summary>
public static class TrayPresentation
{
    /// <summary>Höchstlänge des Tooltips (NOTIFYICONDATA.szTip, NotifyIcon.Text).</summary>
    public const int MaxTooltipLength = 127;

    public static TrayIconSpec IconFor(TrayState state) => new(state.HdrOn, state.Paused);

    /// <summary>Zeile 1 HDR-Zustand, Zeile 2 laufende Spiele (falls vorhanden), Zeile 3 Automatik. Höchstens 127 Zeichen.</summary>
    public static string TooltipFor(TrayState state)
    {
        var head = state.HdrOn
            ? ShellStrings.TrayTooltipHdrOn
            : state.HdrSupported ? ShellStrings.TrayTooltipHdrOff : ShellStrings.TrayTooltipNoHdr;
        var tail = state.Paused ? ShellStrings.TrayTooltipPaused : ShellStrings.TrayTooltipActive;
        var games = state.ActiveGames.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
        if (games.Count == 0)
        {
            return Fit(head + "\n" + tail, MaxTooltipLength);
        }

        var gamesLine = string.Format(
            CultureInfo.CurrentCulture,
            games.Count == 1 ? ShellStrings.TrayTooltipGame : ShellStrings.TrayTooltipGames,
            string.Join(ShellStrings.ListSeparator, games));
        var budget = MaxTooltipLength - head.Length - tail.Length - 2;
        return Fit(head + "\n" + Fit(gamesLine, Math.Max(budget, 1)) + "\n" + tail, MaxTooltipLength);
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
/// Zeichnet das Tray-Symbol zur Laufzeit in der Größe, die Windows für kleine Symbole bei der System-DPI erwartet:
/// HDR an = Akzentverlauf, HDR aus = gedämpft; darauf „H“ (klein) bzw. „HDR“ (ab 32 px), pausiert = Pause-Balken.
/// </summary>
public static partial class TrayIconRenderer
{
    private const int SM_CXSMICON = 49;

    // Farben wie Hp.Brush.HdrOn / Hp.Brush.HdrOff (GDI+ kennt keine WPF-Ressourcen). Auf heller und dunkler
    // Taskleiste gleichermaßen gut sichtbar.
    private static readonly Drawing.Color OnStart = Drawing.Color.FromArgb(255, 0xFF, 0xC2, 0x4D);
    private static readonly Drawing.Color OnMiddle = Drawing.Color.FromArgb(255, 0xFF, 0x5A, 0x5F);
    private static readonly Drawing.Color OnEnd = Drawing.Color.FromArgb(255, 0xD6, 0x28, 0x62);
    private static readonly Drawing.Color OffFill = Drawing.Color.FromArgb(255, 0x4B, 0x50, 0x68);
    private static readonly Drawing.Color OffEdge = Drawing.Color.FromArgb(255, 0x6C, 0x72, 0x8C);
    private static readonly Drawing.Color OnGlyph = Drawing.Color.White;
    private static readonly Drawing.Color OffGlyph = Drawing.Color.FromArgb(255, 0xD8, 0xDB, 0xE6);

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

    /// <summary>Zeichnet das Symbol als 32-bit-ARGB-Bitmap. Der Aufrufer gibt sie frei.</summary>
    public static Drawing.Bitmap Render(TrayIconSpec spec, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        var bitmap = new Drawing.Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Drawing.Color.Transparent);

        var s = (float)size;
        var body = new Drawing.RectangleF(0.5f, 0.5f, s - 1f, s - 1f);
        using (var path = RoundedRectangle(body, s * 0.27f))
        {
            if (spec.HdrOn)
            {
                using var brush = new LinearGradientBrush(new Drawing.PointF(0, 0), new Drawing.PointF(s, s), OnStart, OnEnd)
                {
                    InterpolationColors = new ColorBlend { Colors = [OnStart, OnMiddle, OnEnd], Positions = [0f, 0.55f, 1f] },
                };
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new Drawing.SolidBrush(OffFill);
                g.FillPath(brush, path);
                using var pen = new Drawing.Pen(OffEdge, Math.Max(1f, s / 24f));
                g.DrawPath(pen, path);
            }
        }

        var glyph = spec.HdrOn ? OnGlyph : OffGlyph;
        if (spec.Paused)
        {
            DrawPauseBars(g, size, glyph);
        }
        else if (size >= 32)
        {
            DrawHdrText(g, size, glyph);
        }
        else
        {
            DrawLetterH(g, size, glyph);
        }

        return bitmap;
    }

    /// <summary>Erzeugt das Symbol mit eigenem HICON. Freigabe über <see cref="TrayIcon.Dispose"/> (DestroyIcon).</summary>
    public static TrayIcon CreateIcon(TrayIconSpec spec, int size)
    {
        using var bitmap = Render(spec, size);
        var handle = bitmap.GetHicon();
        return new TrayIcon(spec, size, handle);
    }

    /// <summary>„H“ aus Rechtecken, am Pixelraster ausgerichtet: bei 16 px gestochen scharf.</summary>
    private static void DrawLetterH(Drawing.Graphics g, int size, Drawing.Color color)
    {
        var stroke = Math.Max(2, (int)Math.Round(size * 0.13));
        var height = (int)Math.Round(size * 0.56);
        var width = (int)Math.Round(size * 0.5);
        if ((size - width) % 2 != 0)
        {
            width++;
        }

        if ((size - height) % 2 != 0)
        {
            height++;
        }

        var x = (size - width) / 2;
        var y = (size - height) / 2;
        var bar = Math.Max(2, (int)Math.Round(size * 0.12));
        var barY = y + ((height - bar) / 2);
        using var brush = new Drawing.SolidBrush(color);
        var oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(brush, x, y, stroke, height);
        g.FillRectangle(brush, x + width - stroke, y, stroke, height);
        g.FillRectangle(brush, x + stroke, barY, width - (2 * stroke), bar);
        g.SmoothingMode = oldMode;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static void DrawPauseBars(Drawing.Graphics g, int size, Drawing.Color color)
    {
        var barWidth = Math.Max(2, (int)Math.Round(size * 0.14));
        var gap = Math.Max(2, (int)Math.Round(size * 0.13));
        var height = (int)Math.Round(size * 0.5);
        if ((size - height) % 2 != 0)
        {
            height++;
        }

        var total = (2 * barWidth) + gap;
        var x = (size - total) / 2;
        var y = (size - height) / 2;
        using var brush = new Drawing.SolidBrush(color);
        var oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(brush, x, y, barWidth, height);
        g.FillRectangle(brush, x + barWidth + gap, y, barWidth, height);
        g.SmoothingMode = oldMode;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static void DrawHdrText(Drawing.Graphics g, int size, Drawing.Color color)
    {
        using var family = ResolveFamily();
        // Winding: variable Schriften (Bahnschrift) haben überlappende Konturen, Alternate stanzte dort Löcher.
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddString("HDR", family, (int)Drawing.FontStyle.Bold, size * 0.5f, Drawing.PointF.Empty, Drawing.StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var maxWidth = size * 0.8f;
        var maxHeight = size * 0.44f;
        var scale = Math.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);
        using var matrix = new Matrix();
        matrix.Translate((size - (bounds.Width * scale)) / 2f, (size - (bounds.Height * scale)) / 2f);
        matrix.Scale(scale, scale);
        matrix.Translate(-bounds.X, -bounds.Y);
        path.Transform(matrix);
        using var brush = new Drawing.SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static Drawing.FontFamily ResolveFamily()
    {
        foreach (var name in new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift", "Segoe UI" })
        {
            try
            {
                return new Drawing.FontFamily(name);
            }
            catch (ArgumentException)
            {
                // Schrift fehlt: nächste versuchen.
            }
        }

        return new Drawing.FontFamily(GenericFontFamilies.SansSerif);
    }

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
