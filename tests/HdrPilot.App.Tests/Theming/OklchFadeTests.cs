using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Theming;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Überblendung im OKLCH-Farbraum (E94, G15.3): vier Schlüsselbilder auf dem kürzeren Bogen zu den Zeiten von CubicEase
/// EaseInOut, ohne „From“ (Start bei der Farbe auf dem Bildschirm), keine graue Mitte zwischen den Schlüsselbildern, graues Ende
/// mit dem Farbton des anderen, Alpha linear.
/// </summary>
public sealed class OklchFadeTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.9);

    [Fact]
    public void Fade_has_four_key_frames_at_the_times_of_cubic_ease_in_out()
    {
        var fade = OklchFade.Create(ColorMath.FromHex("#FF5050"), ColorMath.FromHex("#2EC4B6"), Duration)!;

        Assert.Equal(4, fade.KeyFrames.Count);
        Assert.Equal([0.397, 0.5, 0.603, 1.0], fade.KeyFrames.Cast<ColorKeyFrame>().Select(k => Math.Round(k.KeyTime.TimeSpan.TotalSeconds / Duration.TotalSeconds, 3)));
        Assert.Equal(EasingMode.EaseIn, Assert.IsType<CubicEase>(Assert.IsType<EasingColorKeyFrame>(fade.KeyFrames[0]).EasingFunction).EasingMode);
        Assert.IsType<LinearColorKeyFrame>(fade.KeyFrames[1]);
        Assert.IsType<LinearColorKeyFrame>(fade.KeyFrames[2]);
        Assert.Equal(EasingMode.EaseOut, Assert.IsType<CubicEase>(Assert.IsType<EasingColorKeyFrame>(fade.KeyFrames[3]).EasingFunction).EasingMode);
        Assert.Equal(ColorMath.FromHex("#2EC4B6"), fade.KeyFrames[3].Value);
        Assert.Equal(FillBehavior.HoldEnd, fade.FillBehavior);
        Assert.Equal(EffectPolicy.OneShotFrameRate, Timeline.GetDesiredFrameRate(fade));
        Assert.True(fade.IsFrozen);

        // CubicEase InOut erreicht 25 % bei 0,397: das erste Stück (EaseIn) ist dort genau.
        Assert.Equal(0.25, new CubicEase { EasingMode = EasingMode.EaseInOut }.Ease(0.397), 2);
    }

    [Fact]
    public void Fade_starts_from_the_color_on_screen_and_is_skipped_when_nothing_changes()
    {
        var from = ColorMath.FromHex("#FF71CE");
        var to = ColorMath.FromHex("#3DDC84");
        var fade = OklchFade.Create(from, to, Duration)!;

        // Kein From: der erste Abschnitt läuft vom Grundwert (der gezeigten Farbe) zum ersten Schlüsselbild.
        Assert.Equal(from, fade.KeyFrames[0].InterpolateValue(from, 0));
        var quarter = ColorScience.Lerp(GamePaletteTests.Rgba(from), GamePaletteTests.Rgba(to), 0.25);
        Assert.Equal(Color.FromArgb(quarter.A, quarter.R, quarter.G, quarter.B), fade.KeyFrames[0].Value);
        Assert.Null(OklchFade.Create(from, from, Duration));
        Assert.Null(OklchFade.Create(from, to, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("#FF5050", "#2EC4B6")]
    [InlineData("#FF71CE", "#3DDC84")]
    [InlineData("#FF8A3D", "#3B82F6")]
    public void Fade_keeps_chroma_between_key_frames(string fromHex, string toHex)
    {
        var from = ColorMath.FromHex(fromHex);
        var to = ColorMath.FromHex(toHex);
        var fade = OklchFade.Create(from, to, Duration)!;
        var floor = 0.6 * Math.Min(ColorMath.Chroma(from), ColorMath.Chroma(to));

        var previous = from;
        foreach (var frame in fade.KeyFrames.Cast<ColorKeyFrame>())
        {
            for (var p = 0.0; p <= 1.0001; p += 0.1)
            {
                var color = frame.InterpolateValue(previous, p);
                Assert.True(ColorMath.Chroma(color) >= floor, $"{fromHex}→{toHex}: {ColorMath.ToHex(color)} C {ColorMath.Chroma(color):0.000} < {floor:0.000}");
            }

            previous = frame.Value;
        }

        // Gegenprobe: die direkte Überblendung von WPF (scRGB) fällt in der Mitte darunter.
        var direct = new LinearColorKeyFrame(to, KeyTime.FromPercent(1)).InterpolateValue(from, 0.5);
        Assert.True(ColorMath.Chroma(direct) < ColorMath.Chroma(fade.KeyFrames[1].Value));
    }

    [Fact]
    public void Fade_from_gray_keeps_the_hue_of_the_other_end()
    {
        var blue = ColorMath.FromHex("#3B82F6");
        var fade = OklchFade.Create(ColorMath.FromHex("#808080"), blue, Duration)!;

        foreach (var frame in fade.KeyFrames.Cast<ColorKeyFrame>())
        {
            Assert.True(ColorScience.HueDistance(GamePaletteTests.Lab(frame.Value).H, GamePaletteTests.Lab(blue).H) <= 5, ColorMath.ToHex(frame.Value));
        }
    }

    [Fact]
    public void Fade_mixes_alpha_linearly()
    {
        var fade = OklchFade.Create(Color.FromArgb(0, 0xD6, 0x28, 0x28), Color.FromArgb(255, 0xD6, 0x28, 0x28), Duration)!;

        var alphas = fade.KeyFrames.Cast<ColorKeyFrame>().Select(k => (int)k.Value.A).ToList();
        Assert.InRange(alphas[0], 62, 66);
        Assert.InRange(alphas[1], 126, 130);
        Assert.InRange(alphas[2], 189, 193);
        Assert.Equal(255, alphas[3]);
    }
}
