using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Farbebene der Spielfarben (E94, G15.3): löst dieselben Werte auf wie <see cref="ThemeResources.Build"/>, behält ihre
/// Pinsel, tauscht die Farbwerte einmal zu Beginn eines geänderten Wechsels, beruhigt sich über den Zeitgeber (hier von Hand),
/// meldet das Ende, hinterlässt nach Dispose nichts Animiertes und setzt eine unterbrochene Überblendung bei der gezeigten
/// Farbe fort.
/// </summary>
public sealed class GameAccentLayerTests
{
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(0.9);

    [Fact]
    public void Layer_resolves_the_same_values_as_the_engine_builder()
    {
        WpfTestHost.Run(() =>
        {
            var target = Palette("#D62828", "#F28C28");
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, target);
            var host = new Border();
            host.Resources.MergedDictionaries.Add(layer.Dictionary);
            var built = ThemeResources.Build(target);

            foreach (var key in GameAccentLayer.ColorKeys)
            {
                Assert.Equal((Color)built[key], (Color)host.FindResource(key));
            }

            foreach (var key in GameAccentLayer.BrushKeys)
            {
                Assert.Equal(ColorsOf((Brush)built[key]), ColorsOf((Brush)host.FindResource(key)));
                Assert.False(((Brush)host.FindResource(key)).IsFrozen);
            }

            Assert.Equal(14, GameAccentLayer.BrushKeys.Count);
            Assert.Equal(11, GameAccentLayer.ColorKeys.Count);
            Assert.Equal(19, GameAccentLayer.BrushKeys.Sum(k => ColorsOf((Brush)built[k]).Count));
        });
    }

    [Fact]
    public void Layer_keeps_its_brush_instances_across_transitions()
    {
        WpfTestHost.Run(() =>
        {
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, Palette("#D62828", "#F28C28"));
            layer.TimerHookForTest = (_, _) => { };
            var before = GameAccentLayer.BrushKeys.Select(k => layer.Dictionary[k]).ToList();

            layer.TransitionTo(Palette("#1E64DC", "#7C3AED"), Fade);
            layer.TransitionTo(Palette("#00A3A3", "#3B82F6"), Fade);
            layer.Settle();

            Assert.Equal(before, GameAccentLayer.BrushKeys.Select(k => layer.Dictionary[k]));
            Assert.Equal(0, layer.Stats.Replacements);
        });
    }

    [Fact]
    public void Color_part_is_swapped_once_at_the_start_of_a_changed_transition_and_never_when_unchanged()
    {
        WpfTestHost.Run(() =>
        {
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, Palette("#D62828", "#F28C28"));
            layer.TimerHookForTest = (_, _) => { };
            var blue = Palette("#1E64DC", "#7C3AED");

            layer.TransitionTo(blue, Fade);

            Assert.Equal(1, layer.Stats.ColorPartSwaps);
            Assert.Equal(blue.C1, (Color)layer.Dictionary.MergedDictionaries[0][ThemeKeys.ColorC1]); // sofort, nicht am Ende
            Assert.True(layer.Animating > 0);
            layer.Settle();
            layer.TransitionTo(blue, Fade);
            Assert.Equal(1, layer.Stats.ColorPartSwaps);
            Assert.Equal(1, layer.Stats.SkippedEqual);

        });
    }

    [Fact]
    public void Settle_runs_after_the_fade_plus_a_margin_and_leaves_targets_without_animation()
    {
        WpfTestHost.Run(() =>
        {
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, Palette("#D62828", "#F28C28"));
            TimeSpan? delay = null;
            Action? settle = null;
            layer.TimerHookForTest = (d, a) => (delay, settle) = (d, a);
            var blue = Palette("#1E64DC", "#7C3AED");

            layer.TransitionTo(blue, Fade);
            Assert.Equal(Fade + GameAccentLayer.SettleMargin, delay);
            Assert.True(layer.Animating > 0);
            settle!();

            Assert.Equal(0, layer.Animating);
            var accent = (LinearGradientBrush)layer.Dictionary[ThemeKeys.BrushAccent];
            Assert.Equal([blue.AccentFill1, blue.AccentFill2], accent.GradientStops.Select(s => s.Color));
            Assert.DoesNotContain(accent.GradientStops, s => s.HasAnimatedProperties);
            Assert.False(accent.IsFrozen);
        });
    }

    [Fact]
    public void On_settled_runs_at_the_end_or_at_once_when_nothing_changes()
    {
        WpfTestHost.Run(() =>
        {
            var red = Palette("#D62828", "#F28C28");
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, red);
            Action? settle = null;
            layer.TimerHookForTest = (_, a) => settle = a;
            var calls = new List<string>();

            layer.TransitionTo(red, Fade, () => calls.Add("gleich"));
            Assert.Equal(["gleich"], calls);

            layer.TransitionTo(Palette("#1E64DC", "#7C3AED"), Fade, () => calls.Add("blau"));
            layer.TransitionTo(red, Fade, () => calls.Add("rot"));
            Assert.Equal(["gleich"], calls);
            settle!();
            Assert.Equal(["gleich", "blau", "rot"], calls);

            layer.TransitionTo(Palette("#00A3A3", "#3B82F6"), TimeSpan.Zero, () => calls.Add("sofort"));
            Assert.Equal("sofort", calls[^1]);
        });
    }

    [Fact]
    public void Nothing_is_animated_after_dispose()
    {
        WpfTestHost.Run(() =>
        {
            var layer = new GameAccentLayer(WpfTestHost.Dispatcher, Palette("#D62828", "#F28C28"));
            layer.TimerHookForTest = (_, _) => { };
            layer.TransitionTo(Palette("#1E64DC", "#7C3AED"), Fade);

            layer.Dispose();

            Assert.Equal(0, layer.Animating);
            Assert.All(GameAccentLayer.BrushKeys, k => Assert.False(((Animatable)layer.Dictionary[k]).HasAnimatedProperties || Stops((Brush)layer.Dictionary[k]).Any(s => s.HasAnimatedProperties)));
            layer.TransitionTo(Palette("#00A3A3", "#3B82F6"), Fade); // nach Dispose ohne Wirkung
            Assert.Equal(0, layer.Animating);
        });
    }

    [Fact]
    public void Interrupted_fade_continues_from_the_color_on_screen()
    {
        WpfTestHost.Run(() =>
        {
            var red = Palette("#D62828", "#F28C28");
            using var layer = new GameAccentLayer(WpfTestHost.Dispatcher, red);
            layer.TimerHookForTest = (_, _) => { };
            var text = (SolidColorBrush)layer.Dictionary[ThemeKeys.BrushAccentText];
            var shown = text.Color;

            layer.TransitionTo(Palette("#1E64DC", "#7C3AED"), Fade);
            layer.TransitionTo(Palette("#00A3A3", "#3B82F6"), Fade);

            // Der zweite Wechsel springt nicht erst zum ersten Ziel: gezeigt bleibt, was gerade zu sehen war.
            Assert.Equal(shown, text.Color);
            Assert.True(text.HasAnimatedProperties);
            layer.Settle();
            Assert.Equal(layer.Target.AccentText, text.Color);
        });
    }

    internal static ThemePalette Palette(string accent, string atmosphere, bool dark = true)
    {
        var a = ColorMath.FromHex(accent);
        var m = ColorMath.FromHex(atmosphere);
        return PaletteEngine.Compute(new PaletteColors(a, m, PalettePresets.DerivedShine(m)), dark, true, PalettePresets.Default.Colors.Atmosphere);
    }

    internal static List<Color> ColorsOf(Brush brush) => brush switch
    {
        SolidColorBrush s => [s.Color],
        GradientBrush g => [.. g.GradientStops.Select(s => s.Color)],
        _ => [],
    };

    private static IEnumerable<GradientStop> Stops(Brush brush) => brush is GradientBrush g ? g.GradientStops : [];
}
