using System.Runtime.InteropServices;
using HdrPilot.App.Services;
using Drawing = System.Drawing;

namespace HdrPilot.App.Tests.Shell;

public sealed partial class TrayIconRendererTests
{
    public static TheoryData<int> Sizes => [16, 20, 24, 32, 48];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Hdr_on_is_warm_gradient_and_off_is_muted(int size)
    {
        using var on = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: true, Paused: false), size);
        using var off = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: false, Paused: false), size);

        Assert.Equal(size, on.Width);
        Assert.Equal(size, on.Height);

        // Fläche neben dem Buchstaben (links unten im Inneren): an = warm (Rot überwiegt), aus = kühles Grau.
        var probe = new Drawing.Point(size / 5, size * 4 / 5);
        var warm = on.GetPixel(probe.X, probe.Y);
        var muted = off.GetPixel(probe.X, probe.Y);
        Assert.True(warm.R > warm.B + 40, $"an: {warm}");
        Assert.True(muted.B >= muted.R, $"aus: {muted}");
        Assert.True(Saturation(warm) > Saturation(muted) + 0.3, $"an {Saturation(warm):0.00}, aus {Saturation(muted):0.00}");

        // Ecken bleiben durchsichtig (abgerundet).
        Assert.True(on.GetPixel(0, 0).A < 128);
        Assert.True(off.GetPixel(size - 1, size - 1).A < 128);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void Small_icon_shows_letter_h_and_paused_shows_bars(int size)
    {
        using var normal = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: true, Paused: false), size);
        using var paused = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: true, Paused: true), size);

        // Mitte: beim „H“ liegt dort der Querbalken (weiß), bei den Pause-Balken die Lücke (Hintergrund).
        var center = size / 2;
        Assert.True(IsWhite(normal.GetPixel(center, center)) || IsWhite(normal.GetPixel(center, center - 1)), "Querbalken des H fehlt");
        Assert.False(IsWhite(paused.GetPixel(center, center)), "Pause-Balken: Mitte muss frei sein");

        // Pause-Balken links und rechts der Mitte sind weiß.
        var row = center;
        Assert.Contains(Enumerable.Range(0, center), x => IsWhite(paused.GetPixel(x, row)));
        Assert.Contains(Enumerable.Range(center + 1, size - center - 1), x => IsWhite(paused.GetPixel(x, row)));
    }

    [Fact]
    public void Large_icon_spells_hdr()
    {
        using var large = TrayIconRenderer.Render(new TrayIconSpec(HdrOn: true, Paused: false), 32);

        // „HDR“ belegt die Breite: weiße Pixel links, in der Mitte und rechts der Mittelzeile.
        var whiteColumns = Enumerable.Range(0, 32).Where(x => Enumerable.Range(10, 12).Any(y => IsWhite(large.GetPixel(x, y)))).ToList();
        Assert.True(whiteColumns.Min() <= 8 && whiteColumns.Max() >= 23, $"Spalten {whiteColumns.Min()}..{whiteColumns.Max()}");
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
            using var icon = TrayIconRenderer.CreateIcon(new TrayIconSpec(i % 2 == 0, i % 3 == 0), 16 + (i % 3 * 8));
            Assert.NotEqual(IntPtr.Zero, icon.Icon.Handle);
        }

        var gdiAfter = GetGuiResources(process.Handle, 0);
        var userAfter = GetGuiResources(process.Handle, 1);
        Assert.True(gdiAfter - gdiBefore < 20, $"GDI-Objekte {gdiBefore} -> {gdiAfter}");
        Assert.True(userAfter - userBefore < 20, $"USER-Objekte {userBefore} -> {userAfter}");
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var icon = TrayIconRenderer.CreateIcon(new TrayIconSpec(false, true), 16);

        icon.Dispose();
        icon.Dispose();
    }

    private static bool IsWhite(Drawing.Color c) => c.A > 200 && c.R > 225 && c.G > 225 && c.B > 225;

    private static double Saturation(Drawing.Color c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
        var min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
        return max <= 0 ? 0 : (max - min) / max;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
