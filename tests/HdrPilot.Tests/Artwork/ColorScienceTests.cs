using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Farbrechnung der Spielfarben (E94, G15.1): OKLab hin und zurück, Leuchtdichte nach WCAG, Begrenzung auf sRGB bei festem
/// Farbton, Einpassen in die Wertebereiche (Band, Mindest-Chroma, Aufhellen dunkler Markenfarben), „graue Mitte“ und Mischen
/// in OKLCH.
/// </summary>
public sealed class ColorScienceTests
{
    [Fact]
    public void Oklab_round_trip_is_exact_to_one_step()
    {
        for (var r = 0; r < 256; r += 15)
        {
            for (var g = 0; g < 256; g += 15)
            {
                for (var b = 0; b < 256; b += 15)
                {
                    var c = new Rgba8((byte)r, (byte)g, (byte)b);
                    var back = ColorScience.ToRgba8(ColorScience.ToOklab(c));
                    Assert.True(Math.Abs(back.R - r) <= 1 && Math.Abs(back.G - g) <= 1 && Math.Abs(back.B - b) <= 1, $"{c} → {back}");
                }
            }
        }
    }

    [Fact]
    public void Luminance_of_reference_colours_follows_wcag()
    {
        Assert.Equal(0, ColorScience.Luminance(new Rgba8(0, 0, 0)), 6);
        Assert.Equal(1, ColorScience.Luminance(new Rgba8(255, 255, 255)), 6);
        Assert.Equal(0.2126, ColorScience.Luminance(new Rgba8(255, 0, 0)), 4);
        Assert.Equal(0.7152, ColorScience.Luminance(new Rgba8(0, 255, 0)), 4);
        Assert.Equal(0.0722, ColorScience.Luminance(new Rgba8(0, 0, 255)), 4);
        Assert.Equal(0.2159, ColorScience.Luminance(new Rgba8(128, 128, 128)), 3);
        Assert.Equal(21, ColorScience.Contrast(new Rgba8(0, 0, 0), new Rgba8(255, 255, 255)), 3);
    }

    [Fact]
    public void Gamut_clip_keeps_lightness_and_hue()
    {
        for (var h = 0; h < 360; h += 15)
        {
            foreach (var l in new[] { 0.3, 0.5, 0.7, 0.9 })
            {
                var c = ColorScience.GamutClip(l, 0.4, h);
                Assert.True(ColorScience.InGamut(l, c, h));
                Assert.False(ColorScience.InGamut(l, c + 0.01, h), $"L {l} h {h}: {c} ist nicht die größte Chroma");
                var rgb = ColorScience.ToRgba8(ColorScience.FromLch(l, c, h));
                var lab = ColorScience.ToOklab(rgb);
                Assert.True(Math.Abs(lab.L - l) < 0.01);
                Assert.True(c < 0.03 || ColorScience.HueDistance(lab.H, h) <= 2, $"L {l} h {h}: {lab.H}");
            }
        }

        Assert.Equal(0, ColorScience.GamutClip(1, 0.2, 40));
        Assert.Equal(0.05, ColorScience.GamutClip(0.6, 0.05, 40));
    }

    [Fact]
    public void Fit_stays_in_the_band_for_every_hue_lightness_chroma_and_role()
    {
        var failures = new List<string>();
        foreach (var role in new[] { FitRole.Accent, FitRole.Atmosphere, FitRole.Shine })
        {
            for (var hi = 0; hi < 36; hi++)
            {
                foreach (var l in new[] { 0.25, 0.37, 0.48, 0.6, 0.72, 0.84, 0.95 })
                {
                    foreach (var c in new[] { 0.03, 0.1, 0.17, 0.25 })
                    {
                        var h = hi * 10.0;
                        var source = ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h));
                        var src = ColorScience.ToOklab(source);
                        var fit = ColorScience.Fit(source, role);
                        var lab = ColorScience.ToOklab(fit);
                        var y = ColorScience.Luminance(fit);
                        var yMin = role.YMin(src.H);
                        var floor = Math.Min(role.CMin, HighestChromaInBand(role, src.H)) - 0.003;
                        var drift = ColorScience.HueDistance(src.H, lab.H);
                        if (y < yMin || y > role.YMax || lab.C < floor || (src.C > 0.02 && drift > 2) || fit.A != 255)
                        {
                            failures.Add($"{role.Name} {source} → {fit}: Y {y:0.000}, C {lab.C:0.000}, Drift {drift:0.0}");
                        }
                    }
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Fit_lifts_dark_colours_with_the_lift_boost_and_raises_pale_ones_to_the_chroma_floor()
    {
        // Dunkles Navy (Y unter dem Band): Ziel-Chroma mindestens 0,15 statt der eigenen 0,09.
        var navy = ColorScience.ToOklab(ColorScience.Fit(Rgba8.Parse("#0B1F4D"), FitRole.Accent));
        Assert.True(navy.C >= 0.145, navy.C.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Helles Pastell (im Band, Chroma 0,05): auf die Mindest-Chroma 0,12 angehoben.
        var pale = ColorScience.ToOklab(ColorScience.Fit(Rgba8.Parse("#CCE0FF"), FitRole.Accent));
        Assert.InRange(pale.C, 0.115, 0.13);

        // Glanz hat keinen Aufhellbonus: Mindest-Chroma 0,08 genügt.
        var shine = ColorScience.ToOklab(ColorScience.Fit(Rgba8.Parse("#1E2A40"), FitRole.Shine));
        Assert.InRange(shine.C, 0.077, 0.1);

        // Kräftige Farben im Band bleiben unverändert.
        Assert.Equal(Rgba8.Parse("#00F0FF"), ColorScience.Fit(Rgba8.Parse("#00F0FF"), FitRole.Accent));
        Assert.Equal(Rgba8.Parse("#E8B500"), ColorScience.Fit(Rgba8.Parse("#E8B500"), FitRole.Accent));
    }

    [Theory]
    [InlineData("#0B1F4D")]
    [InlineData("#7A1020")]
    [InlineData("#1E5631")]
    [InlineData("#0F5257")]
    [InlineData("#F7C6D9")]
    public void Fit_examples_keep_the_hue_and_gain_chroma(string hex)
    {
        var source = ColorScience.ToOklab(Rgba8.Parse(hex));
        var fit = ColorScience.ToOklab(ColorScience.Fit(Rgba8.Parse(hex), FitRole.Accent));

        Assert.True(ColorScience.HueDistance(source.H, fit.H) <= 2);
        Assert.True(fit.C >= 0.115, $"{hex}: C {fit.C:0.000}");
        if (hex == "#0F5257")
        {
            // Dunkles Türkis wird kräftig, aber nicht neon.
            Assert.True(fit.L < 0.75, fit.L.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Mud_measures_the_gray_middle_of_srgb_gradients()
    {
        // Gegenfarben mischen sich in sRGB zu Grau, benachbarte Farbtöne nicht.
        Assert.True(ColorScience.Mud(Rgba8.Parse("#FF5050"), Rgba8.Parse("#2EC4B6")) < 0.3);
        Assert.True(ColorScience.Mud(Rgba8.Parse("#3B82F6"), Rgba8.Parse("#2EC4B6")) >= 0.7);
        Assert.True(ColorScience.Mud(Rgba8.Parse("#FF8A3D"), Rgba8.Parse("#FF3D7F")) >= 0.7);
        Assert.Equal(1, ColorScience.Mud(Rgba8.Parse("#D62828"), Rgba8.Parse("#D62828")), 2);
        Assert.Equal(1, ColorScience.Mud(Rgba8.Parse("#808080"), Rgba8.Parse("#202020")));
    }

    [Fact]
    public void Lerp_takes_the_shorter_arc_keeps_the_hue_of_a_gray_end_and_mixes_alpha_linearly()
    {
        // Rot (≈ 29°) nach Magenta (≈ 328°): kürzerer Bogen über 0°, nicht über Grün.
        var red = Rgba8.Parse("#FF3030");
        var magenta = Rgba8.Parse("#E040E0");
        var mid = ColorScience.ToOklab(ColorScience.Lerp(red, magenta, 0.5));
        var expected = ColorScience.ToOklab(red).H + (ColorScience.SignedHueArc(ColorScience.ToOklab(red).H, ColorScience.ToOklab(magenta).H) / 2);
        Assert.True(ColorScience.HueDistance(mid.H, expected) <= 3, mid.H.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(mid.C > 0.15);

        // Genau 180° Abstand: positive Richtung.
        Assert.Equal(90, ColorScience.SignedHueArc(0, 90));
        Assert.Equal(180, ColorScience.SignedHueArc(10, 190));
        Assert.Equal(-90, ColorScience.SignedHueArc(90, 0));

        // Graues Ende: durchgehend der Farbton des anderen.
        var blue = Rgba8.Parse("#3B82F6");
        var toGray = ColorScience.ToOklab(ColorScience.Lerp(blue, Rgba8.Parse("#808080"), 0.5));
        Assert.True(ColorScience.HueDistance(toGray.H, ColorScience.ToOklab(blue).H) <= 3);

        // Alpha linear, Enden exakt.
        var a = ColorScience.Lerp(new Rgba8(255, 0, 0, 0), new Rgba8(255, 0, 0, 255), 0.25);
        Assert.InRange(a.A, 63, 65);
        Assert.Equal(red, ColorScience.Lerp(red, magenta, 0));
        Assert.Equal(magenta, ColorScience.Lerp(red, magenta, 1));
    }

    private static double HighestChromaInBand(FitRole role, double h)
    {
        var yMin = role.YMin(h);
        var max = 0.0;
        for (var l = 0.30; l <= 0.995; l += 0.005)
        {
            var rgb = ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, role.CMax, h), h));
            var y = ColorScience.Luminance(rgb);
            if (y >= yMin && y <= role.YMax)
            {
                max = Math.Max(max, ColorScience.ToOklab(rgb).C);
            }
        }

        return max;
    }
}
