using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Pixel-Rechnungen der Spielbilder (E89, G10.3) mit erzeugten BGRA-Bildern: verschmierte Steam-Kapseln, Umgebungsfarben mit
/// Mindest-Chroma, Spitzlicht, Farbfeld, Zuschneiden der Logos, helle HDR-Fassung, Monogramm, Umrechnen der Größe.
/// </summary>
public sealed class ArtworkPixelTests
{
    [Fact]
    public void Smeared_capsule_is_detected_by_vertical_smear_at_top_and_bottom()
    {
        // Wie eine von Steam erzeugte Kapsel: oben und unten je Spalte konstant (senkrecht verschmiert), waagerecht bunt.
        var image = Image(60, 90, (x, y) =>
        {
            var stripe = (byte)(128 + (90 * Math.Sin(2 * Math.PI * x / 60)) + (x % 2 * 4));
            return y is < 10 or > 60 ? (stripe, (byte)(stripe / 2), (byte)(255 - stripe)) : ((byte)((x * y) % 255), (byte)(y * 2), (byte)(x * 4));
        });

        var metrics = PixelAnalysis.Smear(image);

        Assert.True(metrics.BottomVertical < 1.4, metrics.ToString());
        Assert.True(metrics.IsSmeared, metrics.ToString());
    }

    [Fact]
    public void Flat_or_detailed_covers_are_not_smeared()
    {
        // Gegenproben: schwarze Fläche (keine Farbe, keine Streifen) und echtes Motiv (auch senkrecht Änderung).
        var black = PixelAnalysis.Smear(Image(60, 90, (_, _) => (0, 0, 0)));
        var detailed = PixelAnalysis.Smear(Image(60, 90, (x, y) => ((byte)((x * 13 + y * 29) % 256), (byte)((x * y) % 256), (byte)((y * 31) % 256))));

        Assert.False(black.IsSmeared, black.ToString());
        Assert.False(detailed.IsSmeared, detailed.ToString());

        // Oben verschmiert, unten aber eine einfarbige Fläche (Schriftband): unten zu wenig Streuung, keine erzeugte Kapsel.
        var plainBottom = PixelAnalysis.Smear(Image(60, 90, (x, y) =>
        {
            var stripe = (byte)(128 + (90 * Math.Sin(2 * Math.PI * x / 60)));
            return y < 10 ? (stripe, (byte)(stripe / 2), (byte)(255 - stripe)) : y > 60 ? ((byte)12, (byte)14, (byte)20) : ((byte)(x * 4), (byte)(y * 2), (byte)90);
        }));
        Assert.True(plainBottom.BottomDeviation < 10 && plainBottom.TopDeviation > 3, plainBottom.ToString());
        Assert.False(plainBottom.IsSmeared, plainBottom.ToString());
        Assert.Throws<ArgumentException>(() => PixelAnalysis.Smear(new byte[4 * 10 * 10], 10, 10));
    }

    [Fact]
    public void Ambient_colors_follow_the_dominant_hue_and_pick_a_different_shine()
    {
        // Großteils kräftiges Blau, ein Streifen Orange: a2 blau, a3 orange (anderer Farbton), a1 dunkel.
        var image = Image(48, 16, (x, _) => x < 36 ? ((byte)30, (byte)80, (byte)220) : ((byte)240, (byte)140, (byte)20));

        var result = PixelAnalysis.Ambient(image, 48, 16, isScene: true)!;
        var (h2, s2, _) = Hsl(result.Colors.Glow);
        var (h3, _, l3) = Hsl(result.Colors.Shine);
        var (_, _, l1) = Hsl(result.Colors.Deep);

        Assert.InRange(h2, 215, 235);
        Assert.InRange(h3, 25, 45);
        Assert.True(s2 >= 0.45);
        Assert.InRange(l3, 0.67, 0.83);
        Assert.InRange(l1, 0.13, 0.17);
        Assert.Equal(AmbientDullness.None, result.Colors.Dullness);
    }

    [Fact]
    public void Gray_images_are_marked_and_pale_images_get_minimum_chroma()
    {
        var gray = PixelAnalysis.Ambient(Image(48, 16, (x, y) => ((byte)(100 + x), (byte)(100 + x), (byte)(100 + x))), 48, 16, true)!;
        Assert.Equal(AmbientDullness.Gray, gray.Colors.Dullness);

        // Blass (Sättigung um 0,15): Mindest-Chroma 0,08 für a2 und a3, 0,05 für a1 (OKLCH).
        var pale = PixelAnalysis.Ambient(Image(48, 16, (_, _) => ((byte)140, (byte)128, (byte)118)), 48, 16, true)!;
        Assert.Equal(AmbientDullness.Pale, pale.Colors.Dullness);
        Assert.True(PixelAnalysis.Chroma(pale.Colors.Glow) >= 0.079, pale.Colors.Glow);
        Assert.True(PixelAnalysis.Chroma(pale.Colors.Deep) >= 0.049, pale.Colors.Deep);

        // Gegenprobe: ohne Mindest-Chroma wäre der dunkle Grundton a1 fast grau.
        var raw = PixelAnalysis.HslToHex(PixelAnalysis.RgbToHsl(140, 128, 118).H, 0.2, 0.15);
        Assert.True(PixelAnalysis.Chroma(raw) < 0.045, raw);
        Assert.NotEqual(raw, pale.Colors.Deep);
    }

    [Fact]
    public void Highlight_is_searched_only_between_22_and_86_percent_of_the_width()
    {
        // Hellster Fleck ganz links (außerhalb) und ein etwas dunklerer bei 70 %: gefunden wird der bei 70 %.
        var image = Image(48, 16, (x, y) => x <= 4 && y is >= 6 and <= 9 ? ((byte)255, (byte)255, (byte)255)
            : x is >= 32 and <= 35 && y is >= 3 and <= 5 ? ((byte)230, (byte)230, (byte)230)
            : ((byte)20, (byte)30, (byte)60));

        var colors = PixelAnalysis.Ambient(image, 48, 16, isScene: true)!.Colors;

        Assert.InRange(colors.LightX, 0.66, 0.75);
        Assert.InRange(colors.LightY, 0.18, 0.32);
        Assert.InRange(colors.Highlight, 0.85, 0.92);

        // Kein Titelbild (Symbol): feste Mitte, feste Stärke.
        var icon = PixelAnalysis.Ambient(image, 48, 16, isScene: false)!.Colors;
        Assert.Equal(0.62, icon.LightX);
        Assert.Equal(0.55, icon.Highlight);
    }

    [Fact]
    public void Transparent_images_give_no_colors()
    {
        Assert.Null(PixelAnalysis.Ambient(new byte[48 * 48 * 4], 48, 48, isScene: false));
        Assert.Null(PixelAnalysis.Ambient(new byte[3], 48, 48, isScene: false));
    }

    [Fact]
    public void Field_is_small_opaque_and_toned_down()
    {
        var bright = Image(48, 16, (_, _) => (250, 250, 250));
        var scene = PixelAnalysis.Ambient(bright, 48, 16, isScene: true)!;
        var icon = PixelAnalysis.Ambient(Image(48, 48, (x, y) => ((byte)(x * 5), 40, (byte)(y * 5))), 48, 48, isScene: false)!;

        Assert.Equal((8, 3), (scene.FieldWidth, scene.FieldHeight));
        Assert.Equal((6, 6), (icon.FieldWidth, icon.FieldHeight));
        Assert.Equal(8 * 3 * 4, scene.Field.Length);
        for (var i = 0; i < scene.Field.Length; i += 4)
        {
            Assert.Equal(255, scene.Field[i + 3]);
            var (_, _, l) = PixelAnalysis.RgbToHsl(scene.Field[i + 2], scene.Field[i + 1], scene.Field[i]);
            Assert.True(l <= 0.705, "Helligkeit höchstens 0,7: " + l);
        }
    }

    [Fact]
    public void Field_keeps_the_base_color_where_the_image_is_transparent()
    {
        var transparent = new byte[48 * 48 * 4];
        var field = PixelAnalysis.Field(transparent, 48, 48, 6, 6, "#204080", 1.3);

        var (r, g, b) = (field[2], field[1], field[0]);
        Assert.True(b > r && b > g, $"Grundfarbe bleibt sichtbar: {r},{g},{b}");
    }

    [Fact]
    public void Logo_bounds_trim_transparent_margins_with_padding()
    {
        var logo = Image(100, 50, (_, _) => (0, 0, 0), (x, y) => x is >= 20 and < 70 && y is >= 10 and < 30 ? (byte)255 : (byte)0);

        Assert.Equal(new PixelRect(18, 8, 54, 24), PixelAnalysis.OpaqueBounds(logo, 100, 50));

        // Gegenproben: fast durchsichtige Pixel (Deckkraft 8) zählen nicht; ganz durchsichtig: kein Zuschnitt.
        var faint = Image(100, 50, (_, _) => (0, 0, 0), (x, _) => x < 10 ? (byte)8 : (byte)0);
        Assert.Null(PixelAnalysis.OpaqueBounds(faint, 100, 50));
        var edge = Image(10, 10, (_, _) => (0, 0, 0), (x, y) => x == 0 && y == 0 ? (byte)200 : (byte)0);
        Assert.Equal(new PixelRect(0, 0, 3, 3), PixelAnalysis.OpaqueBounds(edge, 10, 10));
    }

    [Fact]
    public void Boost_brightens_saturates_and_keeps_alpha()
    {
        var pixels = new byte[] { 60, 90, 140, 200, 128, 128, 128, 255 };
        var (h, s, l) = PixelAnalysis.RgbToHsl(140, 90, 60);

        PixelAnalysis.Boost(pixels);

        var (h2, s2, l2) = PixelAnalysis.RgbToHsl(pixels[2], pixels[1], pixels[0]);
        Assert.True(l2 > l, "heller");
        Assert.True(s2 > s, "satter");
        Assert.InRange(h2, h - 4, h + 4);
        Assert.Equal(200, pixels[3]);
        Assert.Equal(pixels[4], pixels[5]); // Grau bleibt grau
        Assert.Equal(pixels[5], pixels[6]);
        Assert.True(pixels[4] > 128);

        // Gegenprobe: mit den neutralen Werten 1/1/1 ändert sich nichts.
        var neutral = new byte[] { 60, 90, 140, 200 };
        PixelAnalysis.Boost(neutral, 1, 1, 1);
        Assert.Equal(new byte[] { 60, 90, 140, 200 }, neutral);
    }

    [Theory]
    [InlineData("Baldur's Gate 3", "BG")]
    [InlineData("SWAT 3 - Tactical Game of The Year Edition", "S3")]
    [InlineData("Clive Barker's Hellraiser: Revival", "CB")]
    [InlineData("Assassin’s Creed", "AC")]
    [InlineData("Prodeus", "PR")]
    [InlineData("Battlefield™ 6", "B6")]
    [InlineData("- - -", "?")]
    [InlineData("", "?")]
    [InlineData("öde Ödnis", "ÖÖ")]
    public void Monogram_follows_the_prototype(string name, string expected) => Assert.Equal(expected, PixelAnalysis.Monogram(name));

    [Fact]
    public void Min_chroma_keeps_hue_and_leaves_gray_alone()
    {
        Assert.Equal("#808080", PixelAnalysis.MinChroma("#808080", 0.08));
        Assert.Equal("#FF3D7F", PixelAnalysis.MinChroma("#FF3D7F", 0.08)); // schon kräftig

        var boosted = PixelAnalysis.MinChroma("#6E6A66", 0.08);
        Assert.True(PixelAnalysis.Chroma(boosted) >= 0.079);
        var (l0, _, _) = PixelAnalysis.ToOklab("#6E6A66");
        var (l1, _, _) = PixelAnalysis.ToOklab(boosted);
        Assert.InRange(l1, l0 - 0.02, l0 + 0.02);
    }

    [Fact]
    public void Resample_hits_the_exact_size_and_averages_with_alpha()
    {
        // 4×2: links rot deckend, rechts grün durchsichtig → auf 2×1: links rot, rechts durchsichtig ohne dunklen Rand.
        var pixels = Image(4, 2, (x, _) => x < 2 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)255, (byte)0), (x, _) => x < 2 ? (byte)255 : (byte)0);

        var small = PixelAnalysis.Resample(pixels, 4, 2, 0, 0, 4, 2, 2, 1);

        Assert.Equal(2 * 1 * 4, small.Length);
        Assert.Equal((255, 0, 0, 255), (small[2], small[1], small[0], small[3]));
        Assert.Equal(0, small[7]);

        // Halb deckend gemischt: Farbe bleibt rot statt zu Schwarz zu verblassen.
        var mixed = PixelAnalysis.Resample(pixels, 4, 2, 1, 0, 2, 2, 1, 1);
        Assert.Equal(255, mixed[2]);
        Assert.InRange(mixed[3], 126, 129);

        var big = PixelAnalysis.Resample(pixels, 4, 2, 0, 0, 4, 2, 9, 5);
        Assert.Equal(9 * 5 * 4, big.Length);
    }

    private static byte[] Image(int width, int height, Func<int, int, (byte R, byte G, byte B)> color, Func<int, int, byte>? alpha = null)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = color(x, y);
                var o = ((y * width) + x) * 4;
                pixels[o] = b;
                pixels[o + 1] = g;
                pixels[o + 2] = r;
                pixels[o + 3] = alpha?.Invoke(x, y) ?? 255;
            }
        }

        return pixels;
    }

    private static (double H, double S, double L) Hsl(string hex)
    {
        var (r, g, b) = PixelAnalysis.HexToRgb(hex);
        return PixelAnalysis.RgbToHsl(r, g, b);
    }
}
