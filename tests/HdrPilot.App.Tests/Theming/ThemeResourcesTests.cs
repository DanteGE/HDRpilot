using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Erzeugte Farb-Ressourcen (E89, G10.1): jedes Token der Oberfläche kommt aus der Palette, mit dem richtigen Typ, eingefroren;
/// der weiche Farbwechsel animiert nur geänderte Pinsel und endet ohne laufende Uhren.
/// </summary>
public sealed class ThemeResourcesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Theme_generated_dictionary_has_every_key_with_the_right_type_frozen(bool dark)
    {
        WpfTestHost.Run(() =>
        {
            var dictionary = ThemeResources.Build(PaletteEngine.Compute(PaletteEngineTests.Colors("cyber", PaletteMode.Tritone), dark, true));
            foreach (var key in ThemeKeys.All)
            {
                Assert.True(dictionary.Contains(key), "fehlt: " + key);
                var value = dictionary[key];
                if (ThemeKeys.IsColor(key))
                {
                    Assert.IsType<Color>(value);
                }
                else if (ThemeKeys.IsFont(key))
                {
                    Assert.IsType<FontFamily>(value);
                }
                else
                {
                    var brush = Assert.IsAssignableFrom<Brush>(value);
                    Assert.True(brush.IsFrozen, "nicht eingefroren: " + key);
                }
            }

            Assert.False(ThemeResources.HasRunningTransitions(dictionary));
        });
    }

    [Fact]
    public void Theme_resources_derive_every_accent_from_the_palette()
    {
        WpfTestHost.Run(() =>
        {
            var cyber = PaletteEngine.Compute(PaletteEngineTests.Colors("cyber", PaletteMode.Tritone), true, true);
            var d = ThemeResources.Build(cyber);

            Assert.Equal(cyber.C1, (Color)d[ThemeKeys.ColorC1]);
            Assert.Equal(cyber.C3, (Color)d[ThemeKeys.ColorAccent3]); // bisheriger Schlüssel folgt mit
            Assert.Equal(cyber.OnAccent, ((SolidColorBrush)d[ThemeKeys.BrushTextOnAccent]).Color);
            Assert.Equal(cyber.AccentText, ((SolidColorBrush)d[ThemeKeys.BrushAccentSolid]).Color);
            var accent = (LinearGradientBrush)d[ThemeKeys.BrushAccent];
            Assert.Equal([cyber.AccentFill1, cyber.AccentFill2], accent.GradientStops.Select(s => s.Color));
            var hdrOn = (LinearGradientBrush)d[ThemeKeys.BrushHdrOn];
            Assert.Equal([cyber.AccentFill1, cyber.AccentFill2, cyber.AccentFill3], hdrOn.GradientStops.Select(s => s.Color));
            Assert.Equal([cyber.Control1, cyber.Control2], ((LinearGradientBrush)d[ThemeKeys.BrushToggleOn]).GradientStops.Select(s => s.Color));

            // Weiße Schrift auf Neongrün: die Flächen weichen aus, der Verlauf nimmt die Flächen, nicht die Rohfarben.
            var neon = PaletteEngine.Compute(PaletteEngineTests.Colors("neongruen", PaletteMode.Duotone), true, autoTextOnAccent: false);
            var n = ThemeResources.Build(neon);
            Assert.NotEqual(neon.C1, neon.AccentFill1);
            Assert.Equal([neon.AccentFill1, neon.AccentFill2], ((LinearGradientBrush)n[ThemeKeys.BrushAccent]).GradientStops.Select(s => s.Color));

            // Gegenprobe: eine andere Palette ergibt einen anderen Akzent, das Wörterbuch ist nicht fest.
            var glut = ThemeResources.Build(PaletteEngine.Compute(PaletteEngineTests.Colors("glut", PaletteMode.Duotone), true, true));
            Assert.NotEqual((Color)d[ThemeKeys.ColorC1], (Color)glut[ThemeKeys.ColorC1]);
        });
    }

    [Fact]
    public void Theme_light_and_dark_differ_in_surfaces_and_text()
    {
        WpfTestHost.Run(() =>
        {
            var colors = PaletteEngineTests.Colors("abendrot", PaletteMode.Duotone);
            var dark = ThemeResources.Build(PaletteEngine.Compute(colors, true, true));
            var light = ThemeResources.Build(PaletteEngine.Compute(colors, false, true));

            Assert.True(ColorMath.RelativeLuminance(((SolidColorBrush)dark[ThemeKeys.BrushText]).Color) > 0.8);
            Assert.True(ColorMath.RelativeLuminance(((SolidColorBrush)light[ThemeKeys.BrushText]).Color) < 0.02);
            Assert.True(ColorMath.RelativeLuminance((Color)dark[ThemeKeys.ColorBackground]) < 0.02);
            Assert.True(ColorMath.RelativeLuminance((Color)light[ThemeKeys.ColorBackground]) > 0.7);
            Assert.NotEqual(((SolidColorBrush)dark[ThemeKeys.BrushSourceEpic]).Color, ((SolidColorBrush)light[ThemeKeys.BrushSourceEpic]).Color);
        });
    }

    [Fact]
    public void Theme_transition_animates_changed_brushes_only_and_keeps_colors_immediate()
    {
        WpfTestHost.Run(() =>
        {
            var from = PaletteEngine.Compute(PaletteEngineTests.Colors("abendrot", PaletteMode.Duotone), true, true);
            var to = PaletteEngine.Compute(PaletteEngineTests.Colors("cyber", PaletteMode.Tritone), true, true);
            var transition = ThemeResources.BuildTransition(from, to, TimeSpan.FromSeconds(0.9));

            var accent = (LinearGradientBrush)transition[ThemeKeys.BrushAccent];
            Assert.False(accent.IsFrozen);
            Assert.True(accent.GradientStops.Any(s => s.HasAnimatedProperties), "Akzentverlauf wird nicht übergeblendet");
            Assert.True(((SolidColorBrush)transition[ThemeKeys.BrushAccentText]).HasAnimatedProperties);
            Assert.True(ThemeResources.HasRunningTransitions(transition));

            // Unverändert (Linien, Text): eingefroren wie im Endzustand; Farben (Color) sofort im Ziel.
            Assert.True(((Brush)transition[ThemeKeys.BrushLine]).IsFrozen);
            Assert.Equal(to.C1, (Color)transition[ThemeKeys.ColorC1]);

            // Gegenprobe: ohne Dauer (Effekte aus) gibt es keine Überblendung.
            Assert.False(ThemeResources.HasRunningTransitions(ThemeResources.BuildTransition(from, to, TimeSpan.Zero)));
        });
    }

    [Fact]
    public void Theme_accent_layer_keys_cover_every_key_that_follows_the_accent()
    {
        WpfTestHost.Run(() =>
        {
            // Gleiche Flächen (Schema-Atmosphäre), andere Akzentfarben: was sich dann ändert, muss in der Farbebene stehen
            // oder ausdrücklich beim Schema bleiben (E94).
            var surface = PalettePresets.Default.Colors.Atmosphere;
            foreach (var dark in new[] { true, false })
            {
                var a = ThemeResources.Build(PaletteEngine.Compute(new PaletteColors(ColorMath.FromHex("#D62828"), ColorMath.FromHex("#F28C28"), ColorMath.FromHex("#FFD400")), dark, true, surface));
                var b = ThemeResources.Build(PaletteEngine.Compute(new PaletteColors(ColorMath.FromHex("#1E64DC"), ColorMath.FromHex("#7C3AED"), ColorMath.FromHex("#9CC9F2")), dark, true, surface));
                var known = GameAccentLayer.BrushKeys.Concat(GameAccentLayer.ColorKeys).Concat(GameAccentLayer.ExcludedKeys).Cast<string>().ToHashSet(StringComparer.Ordinal);

                var changed = ThemeKeys.All.Where(k => Describe(a[k]) != Describe(b[k])).ToList();

                Assert.NotEmpty(changed);
                Assert.DoesNotContain(changed, k => !known.Contains(k));
                Assert.Empty(GameAccentLayer.ExcludedKeys.Intersect(GameAccentLayer.BrushKeys.Concat(GameAccentLayer.ColorKeys)));
            }
        });
    }

    [Fact]
    public void Theme_accent_entries_write_exactly_the_25_layer_keys()
    {
        WpfTestHost.Run(() =>
        {
            var palette = PaletteEngine.Compute(PaletteEngineTests.Colors("cyber", PaletteMode.Tritone), true, true);
            var d = new ResourceDictionary();

            ThemeResources.AddAccentEntries(d, palette);

            var expected = GameAccentLayer.BrushKeys.Concat(GameAccentLayer.ColorKeys).Cast<string>().OrderBy(k => k, StringComparer.Ordinal);
            Assert.Equal(expected, d.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(25, d.Count);
            var full = ThemeResources.Build(palette);
            Assert.All(d.Keys.Cast<string>(), k => Assert.Equal(Describe(full[k]), Describe(d[k])));
            Assert.All(d.Values.OfType<Freezable>(), f => Assert.True(f.IsFrozen));
        });
    }

    private static string Describe(object? value) => value switch
    {
        Color c => ColorMath.ToHex(c) + c.A.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
        SolidColorBrush s => "solid " + Describe(s.Color),
        GradientBrush g => g.GetType().Name + " " + string.Join(",", g.GradientStops.Select(x => Describe(x.Color) + "@" + x.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        _ => value?.ToString() ?? "null",
    };
}
