using System.Runtime.InteropServices;
using HdrPilot.App.Services;
using HdrPilot.App.Theming;
using Drawing = System.Drawing;
using WpfColor = System.Windows.Media.Color;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Tray-Symbol im Showcase-Stil (E89, G10.2): Sonne über dem Horizont; „HDR aus“ als Linie im Verlauf Akzent → Atmosphäre
/// ohne Kachel, „HDR an“ als gefüllte Kachel im Verlauf der Palette, Pause- und Spiel-Abzeichen, Farben je Taskleiste auf
/// mindestens 3:1. Mit Gegenproben (andere Palette, andere Taskleiste, ohne Abzeichen).
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed partial class TrayIconRendererTests
{
    public static TheoryData<int> AllSizes => [16, 20, 24, 32, 48, 64, 128, 256];

    [Theory]
    [MemberData(nameof(AllSizes))]
    public void TrayIcon_renders_every_size_from_16_to_256_with_transparent_corners(int size)
    {
        foreach (var spec in new[] { new TrayIconSpec(false, false), new TrayIconSpec(true, false), new TrayIconSpec(true, true), new TrayIconSpec(true, false, GameRunning: true) })
        {
            using var icon = TrayIconRenderer.Render(spec, size);
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
            Assert.True(icon.GetPixel(0, 0).A < 40, $"{spec}: Ecke links oben deckend bei {size} px");
            Assert.True(icon.GetPixel(size - 1, 0).A < 40, $"{spec}: Ecke rechts oben deckend bei {size} px");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => TrayIconRenderer.Render(new TrayIconSpec(false, false), TrayIconRenderer.MaxSize + 1));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void TrayIcon_hdr_on_is_a_filled_gradient_tile_and_off_a_line_without_tile(int size)
    {
        using var on = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: true, Paused: false), size);
        using var off = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: false, Paused: false), size);

        // Fläche links oben im Inneren (weder Strahl noch Sonne): an = Kachel in Akzentfarbe, aus = durchsichtig.
        var probe = new Drawing.Point(size * 3 / 16, size * 5 / 16);
        var tile = on.GetPixel(probe.X, probe.Y);
        var empty = off.GetPixel(probe.X, probe.Y);
        Assert.True(tile.A > 240, $"an: Kachel fehlt {tile}");
        Assert.True(tile.R > tile.B + 40, $"an: Abendrot ist warm {tile}");
        Assert.True(empty.A < 20, $"aus: Kachel gezeichnet {empty}");

        // Aus: der Horizont ist im Verlauf Akzent → Atmosphäre gezeichnet (deckend und farbig, nicht grau).
        var horizon = Enumerable.Range(0, size).Select(y => off.GetPixel(size / 2, y)).Where(c => c.A > 200).ToList();
        Assert.NotEmpty(horizon);
        Assert.Contains(horizon, c => Saturation(c) > 0.4);
    }

    [Fact]
    public void TrayIcon_colors_follow_the_palette()
    {
        var abendrot = IconPalette.From(PaletteEngine.Compute(Preset("abendrot"), dark: true, autoTextOnAccent: true));
        var cyber = IconPalette.From(PaletteEngine.Compute(Preset("cyber"), dark: true, autoTextOnAccent: true));
        using var warm = TrayIconRenderer.Render(new TrayIconSpec(true, false, Palette: abendrot), 32);
        using var cool = TrayIconRenderer.Render(new TrayIconSpec(true, false, Palette: cyber), 32);

        var w = warm.GetPixel(6, 9);
        var c = cool.GetPixel(6, 9);
        Assert.True(w.R > w.B + 60, $"Abendrot: {w}");
        Assert.True(c.B > c.R + 60, $"Cyber: {c}");

        // Gegenprobe: ohne Palette gilt der Auslieferungszustand (Abendrot).
        using var standard = TrayIconRenderer.Render(new TrayIconSpec(true, false), 32);
        Assert.Equal(w, standard.GetPixel(6, 9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayIcon_off_glyph_reaches_3_to_1_on_the_taskbar_for_every_preset(bool lightTaskbar)
    {
        var bar = lightTaskbar ? TrayIconRenderer.LightTaskbar : TrayIconRenderer.DarkTaskbar;
        foreach (var preset in PalettePresets.All)
        {
            foreach (var mode in new[] { PaletteMode.Duotone, PaletteMode.Tritone })
            {
                var colors = mode == PaletteMode.Duotone ? preset.Colors with { Shine = PalettePresets.DerivedShine(preset.Colors.Atmosphere) } : preset.Colors;
                var spec = new TrayIconSpec(false, false, Palette: IconPalette.From(PaletteEngine.Compute(colors, true, true)), LightTaskbar: lightTaskbar);
                var (start, end) = TrayIconRenderer.OffGlyphColors(spec);
                Assert.True(ColorMath.Contrast(start, bar) >= 3, $"{preset.Id}: Akzent {ColorMath.ToHex(start)} auf {ColorMath.ToHex(bar)}");
                Assert.True(ColorMath.Contrast(end, bar) >= 3, $"{preset.Id}: Atmosphäre {ColorMath.ToHex(end)} auf {ColorMath.ToHex(bar)}");
                Assert.True(ColorMath.Contrast(TrayIconRenderer.PausedGlyphColor(spec), bar) >= 3, "Pausiert-Grau");
            }
        }

        // Gegenprobe: ohne Anpassung hätte „Gletscher“ (#9BE7FF) auf der hellen Taskleiste zu wenig Kontrast.
        Assert.True(ColorMath.Contrast(ColorMath.FromHex("#9BE7FF"), TrayIconRenderer.LightTaskbar) < 3);
    }

    [Fact]
    public void TrayIcon_glyph_on_the_tile_reaches_4_5_to_1_for_every_preset()
    {
        foreach (var preset in PalettePresets.All)
        {
            foreach (var auto in new[] { true, false })
            {
                var p = IconPalette.From(PaletteEngine.Compute(preset.Colors, true, auto));
                foreach (var fill in new[] { p.Fill1, p.Fill2, p.Fill3 })
                {
                    Assert.True(ColorMath.Contrast(p.OnAccent, fill) >= 4.5, $"{preset.Id} (auto {auto}): {ColorMath.ToHex(p.OnAccent)} auf {ColorMath.ToHex(fill)}");
                }
            }
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void TrayIcon_paused_shows_an_amber_badge_and_running_a_green_one(int size)
    {
        using var plain = TrayIconRenderer.Render(new TrayIconSpec(true, false), size);
        using var paused = TrayIconRenderer.Render(new TrayIconSpec(true, true), size);
        using var pausedOff = TrayIconRenderer.Render(new TrayIconSpec(false, true), size);
        using var running = TrayIconRenderer.Render(new TrayIconSpec(true, false, GameRunning: true), size);
        using var runningPaused = TrayIconRenderer.Render(new TrayIconSpec(true, true, GameRunning: true), size);

        bool Amber(Drawing.Bitmap b) => Region(b, size).Any(c => c.A > 200 && c.R > 220 && c.G is > 140 and < 200 && c.B < 80);
        bool Green(Drawing.Bitmap b) => Region(b, size).Any(c => c.A > 200 && c.G > 180 && c.R < 100 && c.B < 160);

        Assert.True(Amber(paused), "Pause-Abzeichen fehlt (HDR an)");
        Assert.True(Amber(pausedOff), "Pause-Abzeichen fehlt (HDR aus)");
        Assert.True(Green(running), "Spiel-Abzeichen fehlt");
        Assert.True(Amber(runningPaused) && !Green(runningPaused), "Pause geht vor „Spiel läuft“");

        // Gegenprobe: ohne Pause und ohne Spiel kein Abzeichen.
        Assert.False(Amber(plain) || Green(plain), "Abzeichen ohne Grund");
    }

    [Fact]
    public void TrayIcon_light_taskbar_changes_the_line_colors_but_not_the_tile()
    {
        var palette = IconPalette.From(PaletteEngine.Compute(Preset("gletscher"), true, true));
        using var offDark = TrayIconRenderer.Render(new TrayIconSpec(false, false, Palette: palette), 32);
        using var offLight = TrayIconRenderer.Render(new TrayIconSpec(false, false, Palette: palette, LightTaskbar: true), 32);
        using var onDark = TrayIconRenderer.Render(new TrayIconSpec(true, false, Palette: palette), 32);
        using var onLight = TrayIconRenderer.Render(new TrayIconSpec(true, false, Palette: palette, LightTaskbar: true), 32);

        var horizon = HorizonRow(offDark);
        Assert.NotEqual(offDark.GetPixel(10, horizon), offLight.GetPixel(10, horizon));
        Assert.True(Luma(offLight.GetPixel(10, horizon)) < Luma(offDark.GetPixel(10, horizon)), "helle Taskleiste: Linie dunkler");
        Assert.Equal(onDark.GetPixel(6, 9), onLight.GetPixel(6, 9));
    }

    [Fact]
    public void TrayIcon_small_strokes_are_pixel_snapped_at_16_px()
    {
        using var off = TrayIconRenderer.Render(new TrayIconSpec(false, false), 16);

        // Horizont bei 16 px: genau zwei volle Pixelreihen, darüber und darunter nichts (keine halb deckenden Kanten).
        var rows = Enumerable.Range(0, 16).Where(y => off.GetPixel(2, y).A > 0).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, y => Assert.Equal(255, off.GetPixel(2, y).A));
        Assert.Equal(rows[0] + 1, rows[1]);
    }

    [Fact]
    public void TrayIcon_spec_equality_drives_redraws()
    {
        var palette = IconPalette.From(PaletteEngine.Compute(Preset("cyber"), true, true));
        Assert.Equal(new TrayIconSpec(true, false), new TrayIconSpec(true, false));
        Assert.Equal(new TrayIconSpec(true, false, Palette: palette), new TrayIconSpec(true, false, Palette: palette));

        // Gegenproben: jede sichtbare Änderung ist eine andere Spezifikation (neu zeichnen).
        Assert.NotEqual(new TrayIconSpec(true, false), new TrayIconSpec(true, false, Palette: palette));
        Assert.NotEqual(new TrayIconSpec(true, false), new TrayIconSpec(true, false, LightTaskbar: true));
        Assert.NotEqual(new TrayIconSpec(true, false), new TrayIconSpec(true, false, GameRunning: true));
        Assert.Equal(IconPalette.Default, new TrayIconSpec(true, false).Colors);
    }

    [Fact]
    public void System_small_icon_size_is_plausible()
    {
        var size = TrayIconRenderer.SystemSmallIconSize();

        Assert.InRange(size, 16, 256);
    }

    [Fact]
    public void Creating_and_disposing_icons_does_not_leak_gdi_or_user_handles()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        // Aufwärmen (GDI+ legt beim ersten Aufruf eigene Objekte an).
        TrayIconRenderer.CreateIcon(new TrayIconSpec(true, false), 32).Dispose();
        var gdiBefore = GetGuiResources(process.Handle, 0);
        var userBefore = GetGuiResources(process.Handle, 1);

        for (var i = 0; i < 300; i++)
        {
            using var icon = TrayIconRenderer.CreateIcon(new TrayIconSpec(i % 2 == 0, i % 3 == 0, GameRunning: i % 5 == 0), 16 + (i % 3 * 8));
            Assert.NotEqual(IntPtr.Zero, icon.Icon.Handle);
        }

        var gdiAfter = GetGuiResources(process.Handle, 0);
        var userAfter = GetGuiResources(process.Handle, 1);
        // Mit Vorzeichen rechnen: gibt ein Finalizer eines früheren Tests währenddessen ein Objekt frei, sinkt die Zahl
        // (uint liefe sonst über und meldete fälschlich ein Leck).
        Assert.True((int)gdiAfter - (int)gdiBefore < 20, $"GDI-Objekte {gdiBefore} -> {gdiAfter}");
        Assert.True((int)userAfter - (int)userBefore < 20, $"USER-Objekte {userBefore} -> {userAfter}");
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var icon = TrayIconRenderer.CreateIcon(new TrayIconSpec(false, true), 16);

        icon.Dispose();
        icon.Dispose();
    }

    internal static PaletteColors Preset(string id)
    {
        var p = PalettePresets.Find(id)!;
        return p.DefaultMode == PaletteMode.Duotone ? p.Colors with { Shine = PalettePresets.DerivedShine(p.Colors.Atmosphere) } : p.Colors;
    }

    /// <summary>Bildpunkte im Viertel rechts unten (dort sitzen die Abzeichen).</summary>
    private static IEnumerable<Drawing.Color> Region(Drawing.Bitmap bitmap, int size)
    {
        for (var y = size * 5 / 8; y < size; y++)
        {
            for (var x = size * 5 / 8; x < size; x++)
            {
                yield return bitmap.GetPixel(x, y);
            }
        }
    }

    private static int HorizonRow(Drawing.Bitmap bitmap) =>
        Enumerable.Range(bitmap.Height / 2, bitmap.Height / 2).First(y => bitmap.GetPixel(10, y).A > 200);

    private static double Luma(Drawing.Color c) => ColorMath.RelativeLuminance(WpfColor.FromRgb(c.R, c.G, c.B));

    private static double Saturation(Drawing.Color c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
        var min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
        return max <= 0 ? 0 : (max - min) / max;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
