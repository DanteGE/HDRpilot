using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Einpassen der Spielfarben (E94, G15.2) ohne WPF: Akzent im Band, Atmosphäre aus Kandidaten oder abgeleitet (±40°, auf 32°
/// gedreht, keine graue Mitte), Glanz im Tritonus (Kandidat, Bogen, sonst abgeleitet), Leuchtfarbe des Titelbilds als Hinweis,
/// ruhiges Schema. Die Kontrastrechnung prüfen die Tests der App (GamePaletteContrastTests).
/// </summary>
public sealed class GameColorMappingTests
{
    [Fact]
    public void Mapped_colors_stay_in_their_bands_and_keep_the_hue()
    {
        var failures = new List<string>();
        foreach (var raw in RawAccents())
        {
            var source = ColorScience.ToOklab(Rgba8.Parse(raw.Accent!));
            var mapped = GameColorMapping.Map(raw, tritone: true)!.Value;
            var accent = ColorScience.ToOklab(mapped.Accent);
            var y = ColorScience.Luminance(mapped.Accent);
            var yAtmosphere = ColorScience.Luminance(mapped.Atmosphere);
            if (y < FitRole.Accent.YMin(source.H) || y > FitRole.Accent.YMax
                || (source.C > 0.02 && ColorScience.HueDistance(source.H, accent.H) > 2)
                || yAtmosphere < FitRole.Atmosphere.YMin(ColorScience.ToOklab(mapped.Atmosphere).H) - 0.02 || yAtmosphere > FitRole.Atmosphere.YMax)
            {
                failures.Add($"{raw.Accent} → {mapped}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Warm_accent_without_candidates_derives_the_atmosphere_40_degrees_before()
    {
        var mapped = GameColorMapping.Map(Raw("#D62828"), tritone: false)!.Value;

        Assert.True(mapped.AtmosphereDerived);
        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), -42, -38);
        Assert.True(GameColorMapping.Warm(Hue(mapped.Accent)));
    }

    [Fact]
    public void Cool_accent_without_candidates_derives_the_atmosphere_40_degrees_behind()
    {
        var mapped = GameColorMapping.Map(Raw("#1E64DC"), tritone: false)!.Value;

        Assert.True(mapped.AtmosphereDerived);
        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), 38, 42);
        Assert.False(GameColorMapping.Warm(Hue(mapped.Accent)));
        Assert.True(GameColorMapping.Warm(335) && GameColorMapping.Warm(0) && GameColorMapping.Warm(109.9));
        Assert.False(GameColorMapping.Warm(110) || GameColorMapping.Warm(329.9));
    }

    [Fact]
    public void Candidate_too_close_to_the_accent_is_turned_to_32_degrees_on_its_own_side()
    {
        var raw = Raw("#D62828", atmospheres: ["#F07040"]);
        var fitted = ColorScience.Fit(Rgba8.Parse("#F07040"), FitRole.Atmosphere);
        var mapped = GameColorMapping.Map(raw, tritone: false)!.Value;

        Assert.InRange(Math.Abs(Arc(mapped.Accent, fitted)), 12, 30); // vorher zu nah
        Assert.False(mapped.AtmosphereDerived);
        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), 30, 34);
    }

    [Fact]
    public void Candidates_with_a_gray_middle_are_skipped_and_the_derived_one_steps_closer_until_32_degrees()
    {
        // Grün zu Rot (110°) ergibt in sRGB eine graue Mitte; Violett (70°) nicht.
        var mapped = GameColorMapping.Map(Raw("#D62828", atmospheres: ["#469E00", "#B556D4"]), tritone: false)!.Value;
        Assert.Equal(ColorScience.Fit(Rgba8.Parse("#B556D4"), FitRole.Atmosphere), mapped.Atmosphere);
        Assert.True(ColorScience.Mud(mapped.Accent, mapped.Atmosphere) >= GameColorMapping.MinMud);

        // Abgeleitet: bei 40° genügt die Mitte; verlangt man mehr, geht es in Schritten bis 32° heran.
        var accent = mapped.Accent;
        Assert.InRange(Math.Abs(Arc(accent, GameColorMapping.DeriveAtmosphere(accent))), 38, 42);
        Assert.InRange(Math.Abs(Arc(accent, GameColorMapping.DeriveAtmosphere(accent, minMud: 1.5))), 30, 34);
    }

    [Fact]
    public void Tritone_takes_a_fitting_shine_candidate()
    {
        var mapped = GameColorMapping.Map(Raw("#D62828", shines: ["#FF80C0", "#E080FF"]), tritone: true)!.Value;

        // Rosa liegt zu nah an der Atmosphäre (Magenta), Violett passt.
        Assert.Equal(ColorScience.Fit(Rgba8.Parse("#E080FF"), FitRole.Shine), mapped.Shine);
        AssertShine(mapped);
    }

    [Fact]
    public void Tritone_without_candidates_puts_the_shine_on_an_arc_50_degrees_behind_the_atmosphere()
    {
        var mapped = GameColorMapping.Map(Raw("#D62828"), tritone: true)!.Value;

        Assert.NotNull(mapped.Shine);
        Assert.InRange(Arc(mapped.Atmosphere, mapped.Shine!.Value), -53, -47); // gleiche Richtung wie Akzent → Atmosphäre
        AssertShine(mapped);
    }

    [Fact]
    public void Tritone_falls_back_to_the_derived_shine_when_neither_candidate_nor_arc_fits()
    {
        // Violette Atmosphäre zu Rot: Gelb liegt zu weit von der Atmosphäre, der Bogen (Blau) ergibt mit Rot eine graue Mitte.
        var mapped = GameColorMapping.Map(Raw("#D62828", atmospheres: ["#7C3AED"], shines: ["#FFD400"]), tritone: true)!.Value;

        Assert.False(mapped.AtmosphereDerived);
        Assert.Null(mapped.Shine);
        Assert.Null(GameColorMapping.Map(Raw("#D62828"), tritone: false)!.Value.Shine); // Duoton: immer abgeleitet
    }

    [Fact]
    public void Random_raw_sets_keep_atmosphere_and_shine_rules()
    {
        var random = new Random(4711);
        var failures = new List<string>();
        for (var i = 0; i < 300; i++)
        {
            var raw = new GameColors(RandomHex(random), GameColorSource.Cover, 1, Swatches(random), Swatches(random));
            var mapped = GameColorMapping.Map(raw, tritone: true)!.Value;
            var d = ColorScience.HueDistance(Hue(mapped.Accent), Hue(mapped.Atmosphere));
            if (d is < 30 or > 120 || ColorScience.Mud(mapped.Accent, mapped.Atmosphere) < GameColorMapping.MinMud)
            {
                failures.Add($"Atmosphäre {raw} → {mapped}");
            }

            if (mapped.Shine is { } shine && (ColorScience.Mud(mapped.Atmosphere, shine) < 0.7 || ColorScience.Mud(mapped.Accent, shine) < 0.7
                || ColorScience.HueDistance(Hue(mapped.Accent), Hue(shine)) > 150))
            {
                failures.Add($"Glanz {raw} → {mapped}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Unusable_colors_map_to_null()
    {
        Assert.Null(GameColorMapping.Map(null, tritone: false));
        Assert.Null(GameColorMapping.Map(GameColors.None, tritone: true));
        Assert.Null(GameColorMapping.Map(Raw("#D62828") with { Confidence = 0.49 }, tritone: false));
        Assert.Null(GameColorMapping.Map(Raw("#D62828") with { Accent = "rot" }, tritone: false));
    }

    [Fact]
    public void Glow_hint_puts_candidates_near_the_hero_glow_first()
    {
        var raw = Raw("#D62828", atmospheres: ["#F28C28", "#7C3AED"]);

        var plain = GameColorMapping.Map(raw, tritone: false)!.Value;
        var violet = GameColorMapping.Map(raw, tritone: false, Rgba8.Parse("#6A40E0"))!.Value;
        var far = GameColorMapping.Map(raw, tritone: false, Rgba8.Parse("#20C060"))!.Value;

        Assert.InRange(Hue(plain.Atmosphere), 50, 70);
        Assert.InRange(Hue(violet.Atmosphere), 280, 305);
        Assert.Equal(plain, far); // kein Kandidat in 45° Nähe: Reihenfolge bleibt
    }

    [Fact]
    public void Calm_caps_chroma_at_003_and_keeps_lightness_and_hue()
    {
        foreach (var hex in new[] { "#FF3D7F", "#00E5FF", "#C6FF3D", "#8B5CF6", "#FFD166" })
        {
            var source = ColorScience.ToOklab(Rgba8.Parse(hex));
            var calm = ColorScience.ToOklab(GameColorMapping.Calm(Rgba8.Parse(hex)));
            Assert.True(calm.C <= 0.035, $"{hex}: {calm.C}");
            Assert.True(Math.Abs(calm.L - source.L) < 0.01);
            Assert.True(ColorScience.HueDistance(calm.H, source.H) <= 12, $"{hex}: {calm.H} statt {source.H}");
        }

        Assert.Equal(Rgba8.Parse("#808080"), GameColorMapping.Calm(Rgba8.Parse("#808080")));
        Assert.Equal(64, GameColorMapping.Calm(new Rgba8(255, 0, 0, 64)).A);
    }

    private static void AssertShine(MappedGameColors mapped)
    {
        var shine = mapped.Shine!.Value;
        Assert.InRange(ColorScience.HueDistance(Hue(mapped.Accent), Hue(shine)), 25, 150);
        Assert.InRange(ColorScience.HueDistance(Hue(mapped.Atmosphere), Hue(shine)), 25, 90);
        Assert.True(ColorScience.Mud(mapped.Atmosphere, shine) >= 0.7);
        Assert.True(ColorScience.Mud(mapped.Accent, shine) >= 0.7);
    }

    private static IEnumerable<GameColors> RawAccents()
    {
        for (var hi = 0; hi < 36; hi++)
        {
            foreach (var l in new[] { 0.25, 0.37, 0.48, 0.6, 0.72, 0.84, 0.95 })
            {
                foreach (var c in new[] { 0.03, 0.1, 0.17, 0.25 })
                {
                    var h = hi * 10.0;
                    yield return Raw(ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h)).Hex);
                }
            }
        }
    }

    internal static GameColors Raw(string accent, string[]? atmospheres = null, string[]? shines = null) => new(
        accent,
        GameColorSource.Logo,
        1,
        [.. (atmospheres ?? []).Select(a => new GameSwatch(a, GameColorSource.Cover))],
        [.. (shines ?? []).Select(s => new GameSwatch(s, GameColorSource.Cover))]);

    private static string RandomHex(Random random)
    {
        double l = 0.3 + (random.NextDouble() * 0.65), h = random.NextDouble() * 360, c = 0.04 + (random.NextDouble() * 0.2);
        return ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h)).Hex;
    }

    private static GameSwatch[] Swatches(Random random) =>
        [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => new GameSwatch(RandomHex(random), GameColorSource.Cover))];

    private static double Hue(Rgba8 c) => ColorScience.ToOklab(c).H;

    private static double Arc(Rgba8 from, Rgba8 to) => ColorScience.SignedHueArc(Hue(from), Hue(to));
}
