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
}
