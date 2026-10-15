using System.Windows.Media;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Farbrechnung der Themen (E89, G10.1): Text auf Akzentflächen mindestens 4,5:1, Bedienelemente 3:1, Akzent- und gedämpfter
/// Text 4,5:1 auf Fläche und Hintergrund, für alle Vorlagen in hell und dunkel, Duoton und Tritonus, mit und ohne
/// automatische Textfarbe und für zufällige eigene Farben. Gegenproben zeigen, dass die Anpassung jeweils nötig war.
/// </summary>
public sealed class PaletteEngineTests
{
    public static TheoryData<string, bool, PaletteMode, bool> Combinations()
    {
        var data = new TheoryData<string, bool, PaletteMode, bool>();
        foreach (var preset in PalettePresets.All)
        {
            foreach (var dark in new[] { true, false })
            {
                foreach (var mode in new[] { PaletteMode.Duotone, PaletteMode.Tritone })
                {
                    foreach (var auto in new[] { true, false })
                    {
                        data.Add(preset.Id, dark, mode, auto);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Palette_every_preset_keeps_text_and_control_contrast(string id, bool dark, PaletteMode mode, bool auto)
    {
        var palette = PaletteEngine.Compute(Colors(id, mode), dark, auto);

        AssertReadable(palette, $"{id} {(dark ? "dunkel" : "hell")} {mode} auto={auto}");
    }

    [Fact]
    public void Palette_random_custom_colors_are_made_readable()
    {
        var random = new Random(4711);
        for (var i = 0; i < 300; i++)
        {
            var colors = new PaletteColors(RandomColor(random), RandomColor(random), RandomColor(random));
            foreach (var dark in new[] { true, false })
            {
                AssertReadable(PaletteEngine.Compute(colors, dark, autoTextOnAccent: i % 2 == 0), $"Zufall {i} {ColorMath.ToHex(colors.Accent)}/{ColorMath.ToHex(colors.Atmosphere)}/{ColorMath.ToHex(colors.Shine)}");
            }
        }
    }

    [Fact]
    public void Palette_auto_text_picks_dark_ink_on_bright_accents_and_white_on_dark_ones()
    {
        Assert.Equal(PaletteEngine.Ink, PaletteEngine.Compute(Colors("abendrot", PaletteMode.Duotone), true, true).OnAccent);
        var deep = new PaletteColors(ColorMath.FromHex("#3A1C71"), ColorMath.FromHex("#5B1E9E"), ColorMath.FromHex("#8E44AD"));
        Assert.Equal(System.Windows.Media.Colors.White, PaletteEngine.Compute(deep, true, true).OnAccent);
    }

    [Fact]
    public void Palette_without_auto_text_keeps_white_and_darkens_the_fill_instead()
    {
        var neon = Colors("neongruen", PaletteMode.Duotone);
        var manual = PaletteEngine.Compute(neon, dark: true, autoTextOnAccent: false);
        Assert.Equal(System.Windows.Media.Colors.White, manual.OnAccent);
        Assert.NotEqual(neon.Accent, manual.AccentFill1); // Fläche weicht aus, der Text bleibt weiß
        Assert.True(ColorMath.Contrast(System.Windows.Media.Colors.White, manual.AccentFill1) >= 4.5);
        Assert.True(ColorMath.RelativeLuminance(manual.AccentFill1) < ColorMath.RelativeLuminance(neon.Accent));

        // Gegenprobe: automatisch wählt dunkle Schrift, die Fläche bleibt das unveränderte Neongrün.
        var automatic = PaletteEngine.Compute(neon, dark: true, autoTextOnAccent: true);
        Assert.Equal(PaletteEngine.Ink, automatic.OnAccent);
        Assert.Equal(neon.Accent, automatic.AccentFill1);
        Assert.True(ColorMath.Contrast(System.Windows.Media.Colors.White, neon.Accent) < 4.5, "Gegenprobe: Weiß auf Neongrün wäre unlesbar");
    }

    [Fact]
    public void Palette_light_controls_are_darkened_to_reach_3_to_1_but_dark_ones_stay()
    {
        var abendrot = Colors("abendrot", PaletteMode.Duotone);
        var light = PaletteEngine.Compute(abendrot, dark: false, autoTextOnAccent: true);
        var dark = PaletteEngine.Compute(abendrot, dark: true, autoTextOnAccent: true);

        Assert.True(ColorMath.Contrast(abendrot.Accent, light.PanelReference) < 3, "Gegenprobe: Orange auf Weiß hat zu wenig Kontrast");
        Assert.NotEqual(abendrot.Accent, light.Control1);
        Assert.True(ColorMath.Contrast(light.Control1, light.PanelReference) >= 3);
        Assert.Equal(abendrot.Accent, dark.Control1); // auf Dunkel ausreichend: unverändert
    }

    [Fact]
    public void Palette_backgrounds_follow_the_prototype_formulas()
    {
        var abendrot = Colors("abendrot", PaletteMode.Duotone);
        var dark = PaletteEngine.Compute(abendrot, true, true);
        var light = PaletteEngine.Compute(abendrot, false, true);
        var night = Color.FromRgb(0x05, 0x06, 0x0A);

        // a1 = 82 % Nacht in der Atmosphäre; --bg dunkel = 34 % a1 in Nacht (OKLab), hell = 9 % Atmosphäre in #EDEFF4.
        Assert.Equal(ColorMath.Mix(abendrot.Atmosphere, night, 0.82), dark.Ambient1);
        Assert.Equal(ColorMath.MixOklab(night, dark.Ambient1, 0.34), dark.Background);
        Assert.Equal(ColorMath.MixOklab(Color.FromRgb(0xED, 0xEF, 0xF4), abendrot.Atmosphere, 0.09), light.Background);
        Assert.True(ColorMath.RelativeLuminance(dark.Background) < 0.02 && ColorMath.RelativeLuminance(light.Background) > 0.7);
    }

    [Theory]
    [InlineData(7.2, ContrastGrade.Aaa)]
    [InlineData(4.5, ContrastGrade.Aa)]
    [InlineData(4.49, ContrastGrade.AaLarge)]
    [InlineData(3.0, ContrastGrade.AaLarge)]
    [InlineData(2.9, ContrastGrade.TooLow)]
    public void Palette_contrast_grades_follow_wcag(double ratio, ContrastGrade grade) =>
        Assert.Equal(grade, PaletteEngine.Grade(ratio));

    [Fact]
    public void Palette_readable_on_all_backgrounds_mixes_until_every_contrast_holds()
    {
        var white = System.Windows.Media.Colors.White;
        var paper = ColorMath.FromHex("#EDEFF4");
        var orange = ColorMath.FromHex("#FF8A3D");
        var adjusted = PaletteEngine.ReadableOnAll(orange, [white, paper], towardsWhite: false, 4.5);

        Assert.True(PaletteEngine.MinContrast(adjusted, [white, paper]) >= 4.5);
        Assert.Equal(orange, PaletteEngine.ReadableOnAll(orange, [ColorMath.FromHex("#14151D")], towardsWhite: true, 4.5)); // schon lesbar
    }

    internal static PaletteColors Colors(string id, PaletteMode mode)
    {
        var preset = PalettePresets.Find(id)!;
        return mode == PaletteMode.Duotone ? preset.Colors with { Shine = PalettePresets.DerivedShine(preset.Colors.Atmosphere) } : preset.Colors;
    }

    private static void AssertReadable(ThemePalette p, string what)
    {
        Color[] surfaces = [p.PanelReference, p.Background];
        foreach (var fill in new[] { p.AccentFill1, p.AccentFill2, p.AccentFill3 })
        {
            Assert.True(ColorMath.Contrast(p.OnAccent, fill) >= 4.5, $"{what}: Text auf Akzent {ColorMath.ToHex(p.OnAccent)}/{ColorMath.ToHex(fill)} = {ColorMath.Contrast(p.OnAccent, fill):0.00}");
        }

        foreach (var (name, color, target) in new[]
        {
            ("Akzenttext", p.AccentText, 4.5), ("Zweittext", p.TextSecondary, 4.5), ("gedämpfter Text", p.TextMuted, 4.5),
            ("Erfolg", p.Success, 4.5), ("Warnung", p.Warning, 4.5), ("Fehler", p.Danger, 4.5), ("Text", p.Text, 4.5),
            ("Bedienelement 1", p.Control1, 3.0), ("Bedienelement 2", p.Control2, 3.0), ("Fokusring", p.FocusRing, 3.0), ("Auswahlring", p.SelectionRing, 3.0),
        })
        {
            Assert.True(PaletteEngine.MinContrast(color, surfaces) >= target, $"{what}: {name} {ColorMath.ToHex(color)} = {PaletteEngine.MinContrast(color, surfaces):0.00}");
        }
    }

    private static Color RandomColor(Random random) => Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
}
