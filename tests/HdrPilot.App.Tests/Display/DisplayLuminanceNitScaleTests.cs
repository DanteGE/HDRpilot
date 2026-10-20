using System.Globalization;
using HdrPilot.App.Services.Display;

namespace HdrPilot.App.Tests.Display;

/// <summary>Nit-Skala (Anmerkung 3 des Nutzers, G10.7): Maximum ist die HDR-Spitze des Bildschirms, nie ein fester Wert.</summary>
public sealed class DisplayLuminanceNitScaleTests
{
    private static readonly DisplayNits Mag341C = new(@"\\?\DISPLAY#MSI3CB1#x", "MAG 341C OLED", 456, 254, 0.0, 10, HdrActive: false, PeakMeasuredWithHdr: true);

    [Fact]
    public void Scale_maximum_is_the_hdr_peak_not_1000()
    {
        Assert.Equal(456, NitScale.Maximum(Mag341C));
        Assert.Equal(1, NitScale.Fraction(456, Mag341C));
        Assert.Equal(254 / 456.0, NitScale.Fraction(254, Mag341C), 6);

        // Gegenprobe: mit einer festen Skala bis 1000 läge die Spitze nur bei 0,456.
        Assert.NotEqual(456 / 1000.0, NitScale.Fraction(456, Mag341C));
        Assert.Equal(1, NitScale.Fraction(1000, Mag341C));
    }

    [Fact]
    public void Values_are_formatted_like_the_prototype()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("456", NitScale.Format(456, de));
        Assert.Equal("254", NitScale.Format(253.6, de));
        Assert.Equal("0,0000", NitScale.Format(0, de));
        Assert.Equal("0,0500", NitScale.Format(0.05, de));
    }

    [Fact]
    public void Unknown_peak_gives_an_empty_scale()
    {
        var unknown = Mag341C with { PeakNits = 0 };
        Assert.Equal(0, NitScale.Maximum(unknown));
        Assert.Equal(0, NitScale.Fraction(254, unknown));
        Assert.Null(NullDisplayLuminanceService.Instance.Primary);
    }
}
