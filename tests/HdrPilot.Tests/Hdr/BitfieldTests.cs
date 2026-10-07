using HdrPilot.Core.Hdr;

namespace HdrPilot.Tests.Hdr;

/// <summary>Deutung der Bitfelder, mit Rohwerten, die am echten System (Build 26200, MAG 341C OLED) gemessen wurden.</summary>
public sealed class BitfieldTests
{
    private static readonly RawDisplayPath Path = new(new DisplayTarget(0x1A2B3, 0, 4353), 0x1A2B3, 0, 0, IsPrimary: true);
    private static readonly RawTargetName Name = new("MAG 341C OLED", @"\\?\DISPLAY#MSI3DD0#test", 10);

    [Fact]
    public void Info2_measured_hdr_off_wcg_on()
    {
        // Gemessen: value=0xD7, activeColorMode=1 (WCG), 10 bpc. HDR-fähig, HDR aus.
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, @"\\.\DISPLAY1", new RawColorInfo2(0xD7, 0, 10, 1));
        Assert.True(d.HdrSupported);
        Assert.False(d.HdrEnabled);
        Assert.Equal(AdvancedColorMode.Wcg, d.ActiveColorMode);
        Assert.False(d.BlockedByPolicy);
        Assert.Equal(HdrApi.Modern, d.Api);
        Assert.Equal(10u, d.BitsPerColorChannel);
    }

    [Fact]
    public void Info2_measured_monitor_in_picture_in_picture_reports_no_hdr()
    {
        // Gemessen bei Bildschirm im Bild-in-Bild-Modus: value=0x45, activeColorMode=0, 8 bpc.
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(0x45, 0, 8, 0));
        Assert.False(d.HdrSupported);
        Assert.False(d.HdrEnabled);
        Assert.Equal(AdvancedColorMode.Sdr, d.ActiveColorMode);
        Assert.Null(d.GdiDeviceName);
    }

    [Fact]
    public void Info2_hdr_on()
    {
        // Bit 5 (highDynamicRangeUserEnabled) gesetzt, activeColorMode=2.
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(0xF7, 0, 10, 2));
        Assert.True(d.HdrSupported);
        Assert.True(d.HdrEnabled);
        Assert.Equal(AdvancedColorMode.Hdr, d.ActiveColorMode);
        Assert.True(DisplayConfigHdrController.IsInState(d, enable: true));
        Assert.False(DisplayConfigHdrController.IsInState(d, enable: false));
    }

    [Theory]
    [InlineData(1u << 4, true, false)]
    [InlineData(1u << 5, false, true)]
    [InlineData((1u << 4) | (1u << 5), true, true)]
    [InlineData(0xFFFFFFCFu, false, false)] // alle Bits außer 4 und 5
    public void Info2_uses_bit_4_and_5(uint value, bool supported, bool enabled)
    {
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(value, 0, 10, 0));
        Assert.Equal(supported, d.HdrSupported);
        Assert.Equal(enabled, d.HdrEnabled);
    }

    [Fact]
    public void Info2_policy_bit_3()
    {
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(1u << 3, 0, 8, 0));
        Assert.True(d.BlockedByPolicy);
    }

    [Fact]
    public void Info2_switched_on_but_not_active_is_not_in_state()
    {
        // Schalter an, aber Windows zeigt WCG (etwa Akku-Richtlinie): nicht als "HDR an" bestätigen.
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(0xF7, 0, 10, 1));
        Assert.True(d.HdrEnabled);
        Assert.False(DisplayConfigHdrController.IsInState(d, enable: true));
    }

    [Fact]
    public void Info2_unknown_color_mode_maps_to_null()
    {
        var d = DisplayConfigHdrController.FromInfo2(Path, Name, string.Empty, new RawColorInfo2(0x10, 0, 8, 7));
        Assert.Null(d.ActiveColorMode);
    }

    [Theory]
    [InlineData(0x7u, true, false)] // gemessen: WCG an, HDR aus (wideColorEnforced gesetzt)
    [InlineData(0x3u, true, true)]  // Advanced Color an ohne erzwungene Wide Color: HDR an
    [InlineData(0x1u, true, false)] // unterstützt, aus
    [InlineData(0x0u, false, false)]
    [InlineData(0x9u, false, false)] // advancedColorForceDisabled
    [InlineData(0xBu, false, true)]  // erzwungen deaktiviert, aber als an gemeldet: nicht schaltbar
    public void Legacy_bits(uint value, bool supported, bool enabled)
    {
        var d = DisplayConfigHdrController.FromLegacy(Path, Name, string.Empty, new RawColorInfo(value, 0, 10));
        Assert.Equal(supported, d.HdrSupported);
        Assert.Equal(enabled, d.HdrEnabled);
        Assert.Null(d.ActiveColorMode);
        Assert.Equal(HdrApi.Legacy, d.Api);
    }
}
