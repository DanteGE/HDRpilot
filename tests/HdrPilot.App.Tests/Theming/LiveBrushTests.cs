using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Bleibende Pinsel der Spielfarben (E94, G15.3): Überblendung von der gezeigten Farbe aus, dieselbe Instanz über Wechsel
/// hinweg, nach dem Beruhigen Grundwerte gleich Zielen und keine Animation, unpassende oder eingefrorene Pinsel werden ersetzt,
/// Dauer 0 ohne Uhr und je Verlaufsstopp nichts zu tun, wenn das Ziel gleich bleibt (eine laufende Überblendung überlebt).
/// </summary>
public sealed class LiveBrushTests
{
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(0.9);

    [Fact]
    public void Retarget_starts_a_fade_from_the_color_on_screen()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var red = ColorMath.FromHex("#D62828");
            var live = (SolidColorBrush)set.Track(null, Frozen(new SolidColorBrush(red)), TimeSpan.Zero);

            var result = set.Track(live, Frozen(new SolidColorBrush(ColorMath.FromHex("#2EC4B6"))), Fade);

            Assert.Same(live, result);
            Assert.True(live.HasAnimatedProperties);
            Assert.Equal(red, live.Color); // kein From: die Überblendung beginnt bei der gezeigten Farbe
            Assert.Equal(1, set.Stats.Animations);
            Assert.Equal(1, set.Animating);
            set.Settle();
        });
    }

    [Fact]
    public void The_same_instance_is_kept_across_retargets()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var live = set.Track(null, Gradient("#D62828", "#F28C28"), TimeSpan.Zero);

            Assert.Same(live, set.Track(live, Gradient("#1E64DC", "#7C3AED"), Fade));
            Assert.Same(live, set.Track(live, Gradient("#00A3A3", "#3B82F6"), Fade));
            Assert.Equal(0, set.Stats.Replacements);
            Assert.False(live.IsFrozen);
            set.Settle();
        });
    }

    [Fact]
    public void Settle_sets_base_values_to_the_targets_and_removes_every_animation()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var live = (LinearGradientBrush)set.Track(null, Gradient("#D62828", "#F28C28"), TimeSpan.Zero);
            set.Track(live, Gradient("#1E64DC", "#7C3AED"), Fade);
            Assert.True(live.GradientStops.All(s => s.HasAnimatedProperties));

            set.Settle();

            Assert.Equal([ColorMath.FromHex("#1E64DC"), ColorMath.FromHex("#7C3AED")], live.GradientStops.Select(s => s.Color));
            Assert.DoesNotContain(live.GradientStops, s => s.HasAnimatedProperties);
            Assert.False(live.IsFrozen);
            Assert.Equal(0, set.Animating);
        });
    }

    [Fact]
    public void Incompatible_or_frozen_brushes_are_replaced_and_counted()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var frozen = Frozen(new SolidColorBrush(Colors.Red));
            var twoStops = (LinearGradientBrush)LiveBrush.Unfrozen(Gradient("#D62828", "#F28C28"));
            var threeStops = Gradient("#D62828", "#F28C28", "#FFD400");

            Assert.False(LiveBrush.Compatible(frozen, frozen));
            Assert.False(LiveBrush.Compatible(twoStops, threeStops));
            Assert.False(LiveBrush.Compatible(twoStops, Frozen(new SolidColorBrush(Colors.Red))));
            Assert.True(LiveBrush.Compatible(twoStops, Gradient("#000000", "#FFFFFF")));

            Assert.NotSame(frozen, set.Track(frozen, Frozen(new SolidColorBrush(Colors.Blue)), Fade));
            var replaced = set.Track(twoStops, threeStops, Fade);
            Assert.NotSame(twoStops, replaced);
            Assert.False(replaced.IsFrozen);
            Assert.Equal(2, set.Stats.Replacements);
            Assert.False(replaced.HasAnimatedProperties); // ersetzt wird sofort
        });
    }

    [Fact]
    public void Zero_duration_sets_the_value_without_a_clock()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var live = (SolidColorBrush)set.Track(null, Frozen(new SolidColorBrush(Colors.Red)), TimeSpan.Zero);
            set.Track(live, Frozen(new SolidColorBrush(Colors.Blue)), Fade);

            set.Track(live, Frozen(new SolidColorBrush(Colors.Lime)), TimeSpan.Zero);

            Assert.False(live.HasAnimatedProperties);
            Assert.Equal(Colors.Lime, live.Color);
            Assert.Equal(0, set.Animating);
        });
    }

    [Fact]
    public void Unchanged_target_per_stop_leaves_a_running_fade_alone()
    {
        WpfTestHost.Run(() =>
        {
            var set = new LiveBrushSet();
            var live = (LinearGradientBrush)set.Track(null, Gradient("#D62828", "#F28C28", "#FFD400"), TimeSpan.Zero);
            set.Track(live, Gradient("#1E64DC", "#F28C28", "#FFD400"), Fade); // nur Stopp 0 ändert sich
            var retargets = set.Stats.Retargets;

            // Wie ApplyAmbient im selben Durchlauf: dieselben Ziele, sofort – nichts zu tun, die Überblendung läuft weiter.
            set.Track(live, Gradient("#1E64DC", "#F28C28", "#FFD400"), TimeSpan.Zero);

            Assert.Equal(1, retargets);
            Assert.Equal(retargets, set.Stats.Retargets);
            Assert.True(live.GradientStops[0].HasAnimatedProperties);
            Assert.False(live.GradientStops[1].HasAnimatedProperties);
            set.Settle();
        });
    }

    private static LinearGradientBrush Gradient(params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.32), EndPoint = new Point(1, 0.68) };
        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(ColorMath.FromHex(colors[i]), i / (double)Math.Max(1, colors.Length - 1)));
        }

        return Frozen(brush);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
