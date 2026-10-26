using System.Windows.Media;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Spielpalette (E94, G15.2): Bänder und Farbton, abgeleitete und gedrehte Atmosphäre ohne graue Mitte, Glanz im Tritonus,
/// Duoton wie die Vorlagen, graue Spiele genau das Schema, ruhiges Schema, Bernstein statt Oliv im hellen Thema und der
/// mögliche Grundton der Bibliothek als zusätzliche Fläche.
/// </summary>
public sealed class GamePaletteTests
{
    [Fact]
    public void Map_keeps_bands_and_hue()
    {
        foreach (var hex in new[] { "#0B1F4D", "#7A1020", "#1E5631", "#0F5257", "#F7C6D9", "#D62828", "#00F0FF", "#E8B500" })
        {
            var source = Lab(ColorMath.FromHex(hex));
            var mapped = GamePalette.Map(Raw(hex), PaletteMode.Duotone)!.Value;
            var y = ColorMath.RelativeLuminance(mapped.Accent);
            Assert.InRange(y, FitRole.Accent.YMin(source.H) - 1e-9, FitRole.Accent.YMax + 1e-9);
            Assert.True(ColorScience.HueDistance(source.H, Lab(mapped.Accent).H) <= 2, hex);
            Assert.True(Lab(mapped.Accent).C >= 0.115, hex);
            Assert.Equal(255, mapped.Accent.A);
        }
    }

    [Fact]
    public void Derived_atmosphere_of_a_warm_accent_lies_40_degrees_before()
    {
        var mapped = GamePalette.Map(Raw("#D62828"), PaletteMode.Duotone)!.Value;

        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), -42, -38);
    }

    [Fact]
    public void Derived_atmosphere_of_a_cool_accent_lies_40_degrees_behind()
    {
        var mapped = GamePalette.Map(Raw("#1E64DC"), PaletteMode.Duotone)!.Value;

        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), 38, 42);
    }

    [Fact]
    public void Close_candidate_is_spread_to_32_degrees()
    {
        var mapped = GamePalette.Map(Raw("#D62828", atmospheres: ["#F07040"]), PaletteMode.Duotone)!.Value;

        Assert.InRange(Arc(mapped.Accent, mapped.Atmosphere), 30, 34);
    }

    [Fact]
    public void Atmosphere_never_has_a_gray_middle_and_the_derived_one_steps_back_to_32_degrees()
    {
        var mapped = GamePalette.Map(Raw("#D62828", atmospheres: ["#469E00", "#B556D4"]), PaletteMode.Duotone)!.Value;
        Assert.True(ColorScience.Mud(Rgba(mapped.Accent), Rgba(mapped.Atmosphere)) >= GameColorMapping.MinMud);
        Assert.InRange(Math.Abs(Arc(mapped.Accent, mapped.Atmosphere)), 60, 80); // Grün (graue Mitte) übersprungen

        var accent = Rgba(mapped.Accent);
        var stepped = GameColorMapping.DeriveAtmosphere(accent, minMud: 1.5);
        Assert.InRange(Math.Abs(ColorScience.SignedHueArc(ColorScience.ToOklab(accent).H, ColorScience.ToOklab(stepped).H)), 30, 34);
    }

    [Fact]
    public void Tritone_uses_a_fitting_shine_candidate()
    {
        var mapped = GamePalette.Map(Raw("#D62828", shines: ["#E080FF"]), PaletteMode.Tritone)!.Value;

        Assert.Equal(ColorScience.Fit(Rgba8.Parse("#E080FF"), FitRole.Shine), Rgba(mapped.Shine));
    }

    [Fact]
    public void Tritone_without_candidates_uses_the_arc()
    {
        var mapped = GamePalette.Map(Raw("#D62828"), PaletteMode.Tritone)!.Value;

        Assert.InRange(Arc(mapped.Atmosphere, mapped.Shine), -53, -47);
        Assert.NotEqual(PalettePresets.DerivedShine(mapped.Atmosphere), mapped.Shine);
    }

    [Fact]
    public void Tritone_falls_back_to_the_derived_shine()
    {
        var mapped = GamePalette.Map(Raw("#D62828", atmospheres: ["#7C3AED"], shines: ["#FFD400"]), PaletteMode.Tritone)!.Value;

        Assert.Equal(PalettePresets.DerivedShine(mapped.Atmosphere), mapped.Shine);
    }

    [Fact]
    public void Tritone_shine_keeps_color_towards_the_accent_and_stays_within_150_degrees()
    {
        var random = new Random(11);
        for (var i = 0; i < 200; i++)
        {
            var raw = new GameColors(RandomHex(random), GameColorSource.Cover, 1, [new GameSwatch(RandomHex(random), GameColorSource.Cover)], [new GameSwatch(RandomHex(random), GameColorSource.Hero)]);
            var mapped = GamePalette.Map(raw, PaletteMode.Tritone)!.Value;
            if (mapped.Shine == PalettePresets.DerivedShine(mapped.Atmosphere))
            {
                continue;
            }

            Assert.True(ColorScience.Mud(Rgba(mapped.Accent), Rgba(mapped.Shine)) >= 0.7, raw.ToString());
            Assert.True(ColorScience.Mud(Rgba(mapped.Atmosphere), Rgba(mapped.Shine)) >= 0.7, raw.ToString());
            Assert.True(ColorScience.HueDistance(Lab(mapped.Accent).H, Lab(mapped.Shine).H) <= 150, raw.ToString());
        }
    }

    [Fact]
    public void Duotone_shine_is_derived_like_the_presets()
    {
        var mapped = GamePalette.Map(Raw("#D62828", shines: ["#E080FF"]), PaletteMode.Duotone)!.Value;

        Assert.Equal(PalettePresets.DerivedShine(mapped.Atmosphere), mapped.Shine);
    }

    [Fact]
    public void Missing_or_gray_colors_map_to_null()
    {
        Assert.Null(GamePalette.Map(null, PaletteMode.Duotone));
        Assert.Null(GamePalette.Map(GameColors.None, PaletteMode.Tritone));
        Assert.Null(GamePalette.Map(new GameColors(null, GameColorSource.None, 0.31, [], []), PaletteMode.Duotone));
        Assert.Null(GamePalette.Map(Raw("#D62828") with { Confidence = 0.4 }, PaletteMode.Duotone));
    }

    [Fact]
    public void Glow_hint_reorders_the_atmosphere_candidates()
    {
        var raw = Raw("#D62828", atmospheres: ["#F28C28", "#7C3AED"]);

        Assert.InRange(Lab(GamePalette.Map(raw, PaletteMode.Duotone)!.Value.Atmosphere).H, 50, 70);
        Assert.InRange(Lab(GamePalette.Map(raw, PaletteMode.Duotone, ColorMath.FromHex("#6A40E0"))!.Value.Atmosphere).H, 280, 305);
    }

    [Fact]
    public void Calm_scheme_caps_chroma_and_keeps_the_hue()
    {
        var royal = PalettePresets.Find("royal")!.Colors;
        var calm = GamePalette.Calm(royal);

        foreach (var (source, quiet) in new[] { (royal.Accent, calm.Accent), (royal.Atmosphere, calm.Atmosphere), (royal.Shine, calm.Shine) })
        {
            Assert.True(ColorMath.Chroma(quiet) <= 0.035, ColorMath.ToHex(quiet));
            Assert.True(ColorScience.HueDistance(Lab(source).H, Lab(quiet).H) <= 12);
            Assert.True(Math.Abs(Lab(source).L - Lab(quiet).L) < 0.01);
        }
    }

    [Fact]
    public void Scheme_is_exactly_the_app_level_palette()
    {
        foreach (var preset in PalettePresets.All)
        {
            foreach (var dark in new[] { true, false })
            {
                var state = State(preset.Id, preset.DefaultMode);
                Assert.Equal(PaletteEngine.Compute(state.Palette, dark, state.AutoTextOnAccent), GamePalette.Scheme(state, dark));
            }
        }
    }

    [Fact]
    public void Light_theme_darkens_gold_to_amber_not_olive()
    {
        var state = State("abendrot", PaletteMode.Duotone);
        var gold = GamePalette.Map(Raw("#E8B500"), PaletteMode.Duotone)!.Value;

        var light = GamePalette.Compute(gold, state, dark: false);
        var engine = PaletteEngine.Compute(gold, dark: false, state.AutoTextOnAccent, state.Palette.Atmosphere, GamePalette.LibraryBackgroundBounds(false));
        Color[] surfaces = [light.PanelReference, light.Background, light.BackgroundTop, light.BackgroundBottom, .. GamePalette.LibraryBackgroundBounds(false)];

        foreach (var color in new[] { light.AccentText, light.Control1, light.FocusRing, light.SelectionRing })
        {
            Assert.InRange(Lab(color).H, 70, 80);
        }

        Assert.True(PaletteEngine.MinContrast(light.AccentText, surfaces) >= 4.6);
        Assert.True(PaletteEngine.MinContrast(light.Control1, surfaces) >= 3.0);
        Assert.InRange(Lab(engine.AccentText).H, 85, 110); // Gegenprobe: die Engine allein dunkelt zu Oliv ab
        Assert.Equal(GamePalette.Compute(gold, state, dark: true), PaletteEngine.Compute(gold, true, state.AutoTextOnAccent, state.Palette.Atmosphere, GamePalette.LibraryBackgroundBounds(true)));
    }

    [Fact]
    public void Library_bounds_are_the_extreme_of_every_sampled_background()
    {
        var dark = GamePalette.LibraryBackgroundBounds(dark: true).Single();
        var light = GamePalette.LibraryBackgroundBounds(dark: false).Single();

        var darkSamples = GamePalette.SampledBackgrounds(dark: true).ToList();
        var lightSamples = GamePalette.SampledBackgrounds(dark: false).ToList();
        Assert.All(darkSamples, b => Assert.True(ColorMath.RelativeLuminance(b) <= ColorMath.RelativeLuminance(dark) + 1e-9, ColorMath.ToHex(b)));
        Assert.All(lightSamples, b => Assert.True(ColorMath.RelativeLuminance(b) >= ColorMath.RelativeLuminance(light) - 1e-9, ColorMath.ToHex(b)));
        Assert.Contains(dark, darkSamples);
        Assert.Contains(light, lightSamples);
    }

    [Fact]
    public void Library_bounds_cover_the_formulas_of_name_and_art_colors()
    {
        // Ändert sich FromName oder eine Grenze von PixelAnalysis.Ambient, schlägt dieser Test fehl (dann die Abtastung anpassen).
        var dark = ColorMath.RelativeLuminance(GamePalette.LibraryBackgroundBounds(true)[0]);
        var light = ColorMath.RelativeLuminance(GamePalette.LibraryBackgroundBounds(false)[0]);
        var scheme = PalettePresets.Default.Colors;
        for (var i = 0; i < 360; i++)
        {
            var name = AmbientColors.FromName("Spiel " + i);
            Assert.True(ColorMath.RelativeLuminance(AmbientPalette.Resolve(name, scheme, true, true).Background) <= dark + 1e-9);
            Assert.True(ColorMath.RelativeLuminance(AmbientPalette.Resolve(name, scheme, true, false).Background) >= light - 1e-9);
        }

        // Grenzen der Bildfarben (PixelAnalysis.Ambient): a1 Sättigung 0,2–0,75 bei Helligkeit 0,15, a2 0,45–0,95 bei 0,46–0,6.
        var image = Enumerable.Range(0, 36).SelectMany(h => new[] { 0.2, 0.75 }.SelectMany(s1 => new[] { 0.46, 0.6 }.Select(l2 =>
            new AmbientColors(PixelAnalysis.HslToHex(h * 10, s1, 0.15), PixelAnalysis.HslToHex(h * 10, 0.95, l2), "#FFFFFF", 0.5, 0.5, 0.5, AmbientDullness.Gray))));
        foreach (var colors in image)
        {
            Assert.True(ColorMath.RelativeLuminance(AmbientPalette.Resolve(colors, scheme, true, true).Background) <= dark + 1e-9);
            Assert.True(ColorMath.RelativeLuminance(AmbientPalette.Resolve(colors, scheme, true, false).Background) >= light - 1e-9);
        }
    }

    internal static AppearanceState State(string preset, PaletteMode mode, bool auto = true) => AppearanceService.Compute(
        new AppearanceSettings { Preset = preset, PaletteMode = mode.ToString(), AutoTextOnAccent = auto }, isDark: true, windowsAnimationsOn: true, hardwareRendering: true, lightTaskbar: false);

    internal static GameColors Raw(string accent, string[]? atmospheres = null, string[]? shines = null) => new(
        accent,
        GameColorSource.Logo,
        1,
        [.. (atmospheres ?? []).Select(a => new GameSwatch(a, GameColorSource.Cover))],
        [.. (shines ?? []).Select(s => new GameSwatch(s, GameColorSource.Cover))]);

    internal static string RandomHex(Random random)
    {
        double l = 0.3 + (random.NextDouble() * 0.65), h = random.NextDouble() * 360, c = 0.04 + (random.NextDouble() * 0.2);
        return ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h)).Hex;
    }

    internal static Rgba8 Rgba(Color c) => new(c.R, c.G, c.B, c.A);

    internal static Oklab Lab(Color c) => ColorScience.ToOklab(Rgba(c));

    private static double Arc(Color from, Color to) => ColorScience.SignedHueArc(Lab(from).H, Lab(to).H);
}
