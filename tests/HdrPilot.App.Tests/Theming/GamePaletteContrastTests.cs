using System.Windows.Media;
using HdrPilot.App.Theming;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Kontrast der Spielpaletten (E94, G15.2) als Eigenschaftstest über mehr als 1000 erzeugte Paletten: für jede Palette, die
/// <see cref="GamePalette.Map"/> liefert, gegen die Flächen aller acht Vorlagen und den möglichen Grundton der Bibliothek,
/// hell und dunkel, mit und ohne automatische Textfarbe: Text auf Akzent und Akzenttext mindestens 4,5:1, Bedienelemente 3:1,
/// automatische Textfarbe immer die dunkle Schrift auf unveränderten Flächen, Bänder, Atmosphäre und Glanz ohne graue Mitte,
/// Gelb und Gold im hellen Thema bei Bernstein. Map läuft einmal je Rohsatz und Modus.
/// </summary>
public sealed class GamePaletteContrastTests
{
    [Fact]
    public void Raw_accents_over_hue_lightness_and_chroma_keep_every_guarantee()
    {
        var raws = new List<GameColors>();
        for (var hi = 0; hi < 36; hi++)
        {
            foreach (var l in new[] { 0.25, 0.37, 0.48, 0.6, 0.72, 0.84, 0.95 })
            {
                foreach (var c in new[] { 0.03, 0.10, 0.17, 0.25 })
                {
                    var h = hi * 10.0;
                    raws.Add(GamePaletteTests.Raw(ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h)).Hex));
                }
            }
        }

        AssertAll(raws);
    }

    [Fact]
    public void Random_raw_sets_with_candidates_keep_every_guarantee()
    {
        var random = new Random(4711);
        var raws = new List<GameColors>();
        for (var i = 0; i < 300; i++)
        {
            GameSwatch[] Swatches() => [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => new GameSwatch(GamePaletteTests.RandomHex(random), GameColorSource.Cover))];
            raws.Add(new GameColors(GamePaletteTests.RandomHex(random), GameColorSource.Cover, 1, Swatches(), Swatches()));
        }

        AssertAll(raws);
    }

    [Fact]
    public void Yellow_and_gold_in_the_light_theme_become_amber()
    {
        var raws = new List<GameColors>();
        for (var h = 80; h < 115; h += 2)
        {
            foreach (var l in new[] { 0.6, 0.75, 0.9 })
            {
                raws.Add(GamePaletteTests.Raw(ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, 0.18, h), h)).Hex));
            }
        }

        var checkedAmber = AssertAll(raws);
        Assert.True(checkedAmber > 0);
    }

    /// <summary>Prüft jede Zeile der Garantien; Ergebnis: Zahl der Bernstein-Prüfungen.</summary>
    private static int AssertAll(IReadOnlyList<GameColors> raws)
    {
        var failures = new List<string>();
        var amber = 0;
        foreach (var raw in raws)
        {
            foreach (var mode in new[] { PaletteMode.Duotone, PaletteMode.Tritone })
            {
                if (GamePalette.Map(raw, mode) is not { } accents)
                {
                    continue;
                }

                CheckMapping(raw, mode, accents, failures);
                foreach (var preset in PalettePresets.All)
                {
                    foreach (var dark in new[] { true, false })
                    {
                        foreach (var auto in new[] { true, false })
                        {
                            var state = GamePaletteTests.State(preset.Id, mode, auto);
                            var p = GamePalette.Compute(accents, state, dark);
                            amber += CheckPalette(raw, accents, p, dark, auto, failures);
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)) + $"{Environment.NewLine}({failures.Count} Fehler)");
        return amber;
    }

    private static void CheckMapping(GameColors raw, PaletteMode mode, PaletteColors accents, List<string> failures)
    {
        var source = ColorScience.ToOklab(Rgba8.Parse(raw.Accent!));
        var accent = GamePaletteTests.Lab(accents.Accent);
        var atmosphere = GamePaletteTests.Lab(accents.Atmosphere);
        var y = ColorMath.RelativeLuminance(accents.Accent);
        var floor = Math.Min(FitRole.Accent.CMin, HighestChromaInBand(source.H)) - 0.003;
        if (y < FitRole.Accent.YMin(source.H) || y > FitRole.Accent.YMax || accent.C < floor
            || (source.C > 0.02 && ColorScience.HueDistance(source.H, accent.H) > 2))
        {
            failures.Add($"Band {raw.Accent} → {ColorMath.ToHex(accents.Accent)}");
        }

        var d = ColorScience.HueDistance(accent.H, atmosphere.H);
        if (d is < 30 or > 120 || ColorScience.Mud(GamePaletteTests.Rgba(accents.Accent), GamePaletteTests.Rgba(accents.Atmosphere)) < 0.70)
        {
            failures.Add($"Atmosphäre {raw.Accent} → {ColorMath.ToHex(accents.Atmosphere)} ({d:0.0}°)");
        }

        if (mode == PaletteMode.Tritone && accents.Shine != PalettePresets.DerivedShine(accents.Atmosphere))
        {
            var shine = GamePaletteTests.Rgba(accents.Shine);
            if (ColorScience.Mud(GamePaletteTests.Rgba(accents.Atmosphere), shine) < 0.70 || ColorScience.Mud(GamePaletteTests.Rgba(accents.Accent), shine) < 0.70
                || ColorScience.HueDistance(accent.H, GamePaletteTests.Lab(accents.Shine).H) > 150)
            {
                failures.Add($"Glanz {raw.Accent} → {ColorMath.ToHex(accents.Shine)}");
            }
        }
    }

    private static int CheckPalette(GameColors raw, PaletteColors accents, ThemePalette p, bool dark, bool auto, List<string> failures)
    {
        var what = $"{raw.Accent} → {ColorMath.ToHex(accents.Accent)}/{ColorMath.ToHex(accents.Atmosphere)} {(dark ? "dunkel" : "hell")} auto={auto}";
        // Flächen des Schemas im Zustand (je Vorlage ein Durchlauf) und der mögliche Grundton der Bibliothek.
        var own = new[] { p.PanelReference, p.Background, p.BackgroundTop, p.BackgroundBottom }.Concat(GamePalette.LibraryBackgroundBounds(dark)).ToArray();
        if (p.OnAccentContrast < 4.5)
        {
            failures.Add($"Text auf Akzent {p.OnAccentContrast:0.00}: {what}");
        }

        if (PaletteEngine.MinContrast(p.AccentText, own) < 4.5)
        {
            failures.Add($"Akzenttext {PaletteEngine.MinContrast(p.AccentText, own):0.00}: {what}");
        }

        foreach (var (name, color) in new[] { ("Bedienelement 1", p.Control1), ("Bedienelement 2", p.Control2), ("Fokus", p.FocusRing), ("Auswahl", p.SelectionRing) })
        {
            if (PaletteEngine.MinContrast(color, own) < 3.0)
            {
                failures.Add($"{name} {PaletteEngine.MinContrast(color, own):0.00}: {what}");
            }
        }

        if (auto && (p.OnAccent != PaletteEngine.Ink || p.AccentFill1 != p.C1 || p.AccentFill2 != p.C2 || p.AccentFill3 != p.C3))
        {
            failures.Add($"automatische Textfarbe: {what}");
        }

        var hue = GamePaletteTests.Lab(accents.Accent).H;
        if (!dark && hue is >= 80 and < 115)
        {
            foreach (var color in new[] { p.AccentText, p.Control1 })
            {
                if (color != accents.Accent && GamePaletteTests.Lab(color).H is < 70 or > 80)
                {
                    failures.Add($"Bernstein {ColorMath.ToHex(color)} ({GamePaletteTests.Lab(color).H:0}°): {what}");
                }
            }

            return 1;
        }

        return 0;
    }

    private static double HighestChromaInBand(double h)
    {
        var yMin = FitRole.Accent.YMin(h);
        var max = 0.0;
        for (var l = 0.30; l <= 0.995; l += 0.005)
        {
            var rgb = ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, FitRole.Accent.CMax, h), h));
            var y = ColorScience.Luminance(rgb);
            if (y >= yMin && y <= FitRole.Accent.YMax)
            {
                max = Math.Max(max, ColorScience.ToOklab(rgb).C);
            }
        }

        return max;
    }
}
