using System.Windows.Media;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>Farbrechnung der Darstellung (E89, G10.1): Kontrast nach WCAG, Mischungen, OKLab, Vorlagen.</summary>
public sealed class PaletteColorMathTests
{
    [Fact]
    public void Contrast_follows_wcag()
    {
        Assert.Equal(21, ColorMath.Contrast(Colors.Black, Colors.White), 3);
        Assert.Equal(1, ColorMath.Contrast(Colors.Orange, Colors.Orange), 6);

        // Gegenprobe aus dem Prototyp: dunkler Text (#0B0C12) auf der Atmosphäre von „Abendrot“ (#FF3D7F) etwa 5,8:1.
        Assert.InRange(ColorMath.Contrast(ColorMath.FromHex("#0B0C12"), ColorMath.FromHex("#FF3D7F")), 5.6, 6.0);
    }

    [Fact]
    public void Readable_reaches_the_target_or_gives_up_after_30_steps()
    {
        var accent = ColorMath.FromHex("#FF8A3D");
        var onWhite = ColorMath.Readable(accent, Colors.White, towardsWhite: false, target: 4.5);
        Assert.True(ColorMath.Contrast(onWhite, Colors.White) >= 4.5);
        Assert.NotEqual(accent, onWhite);

        // Schon lesbar: unverändert.
        Assert.Equal(accent, ColorMath.Readable(accent, ColorMath.FromHex("#14151D"), towardsWhite: true, target: 4.5));
    }

    [Fact]
    public void Mixing_ends_at_the_inputs()
    {
        var a = ColorMath.FromHex("#FF8A3D");
        var b = ColorMath.FromHex("#3B82F6");
        Assert.Equal(a, ColorMath.Mix(a, b, 0));
        Assert.Equal(b, ColorMath.Mix(a, b, 1));
        Assert.Equal(a, ColorMath.MixOklab(a, b, 0));
        Assert.Equal(b, ColorMath.MixOklab(a, b, 1));
    }

    [Theory]
    [InlineData("#FF8A3D")]
    [InlineData("#00E5FF")]
    [InlineData("#7C5CFF")]
    [InlineData("#101010")]
    public void Oklab_round_trip_keeps_the_color(string hex)
    {
        var color = ColorMath.FromHex(hex);
        var (l, a, b) = ColorMath.ToOklab(color);
        var back = ColorMath.FromOklab(l, a, b);
        Assert.InRange(Math.Abs(back.R - color.R) + Math.Abs(back.G - color.G) + Math.Abs(back.B - color.B), 0, 3);
    }

    [Fact]
    public void Min_chroma_saturates_pale_colors_but_keeps_gray()
    {
        var pale = ColorMath.FromHex("#5A6070");
        Assert.True(ColorMath.Chroma(ColorMath.MinChroma(pale, 0.08)) >= 0.075);
        Assert.Equal(ColorMath.FromHex("#808080"), ColorMath.MinChroma(ColorMath.FromHex("#808080"), 0.08));
    }

    [Fact]
    public void Hex_parsing_accepts_both_forms_and_rejects_garbage()
    {
        Assert.True(ColorMath.TryParseHex("#ff8a3d", out var c));
        Assert.Equal(Color.FromRgb(0xFF, 0x8A, 0x3D), c);
        Assert.True(ColorMath.TryParseHex("80FF8A3D", out var withAlpha));
        Assert.Equal(0x80, withAlpha.A);
        Assert.False(ColorMath.TryParseHex("#ff8a3", out _));
        Assert.False(ColorMath.TryParseHex("rot", out _));
        Assert.Equal("#FF8A3D", ColorMath.ToHex(c));
    }

    [Fact]
    public void Eight_presets_in_prototype_order_with_abendrot_as_default()
    {
        Assert.Equal(
            ["abendrot", "cyber", "neongruen", "royal", "gletscher", "synthwave", "glut", "polarlicht"],
            PalettePresets.All.Select(p => p.Id));
        Assert.Same(PalettePresets.Default, PalettePresets.Find("ABENDROT"));
        Assert.Null(PalettePresets.Find("custom"));
        Assert.Equal(PaletteMode.Duotone, PalettePresets.Default.DefaultMode);
    }
}
