using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// HDR-Moment (E89, G10.6): Lichtstreifen, Blitz, Rand und bis zu 160 Partikel, die nur 2,5 s am Bildtakt hängen; bei
/// „Reduziert“ nur eine kurze Überblendung ohne Partikel, bei „Aus“ nichts. Uhr und Bildtakt sind hier Attrappen.
/// </summary>
public sealed class HdrMomentTests
{
    private static readonly MotionContext Shown = new(WindowVisible: true, Minimized: false, Active: true, GameRunning: false, PauseInBackground: true, PauseWhilePlaying: true);

    private static readonly HdrMomentScene Scene = new(1600, 420, 900, 160, 600, [Colors.Orange, Colors.DeepSkyBlue, Colors.White]);

    private static (HdrMomentPlayer Player, HdrMomentParts Parts, FakeFrameClock Clock, FakeEffectTime Time) Create()
    {
        var boost = new Border { Opacity = 0 };
        var parts = new HdrMomentParts(
            new Grid(),
            new Border { RenderTransform = new TranslateTransform() },
            new Border { RenderTransform = new ScaleTransform() },
            new Border(),
            new Border(),
            new ParticleLayer(),
            () => boost);
        var clock = new FakeFrameClock();
        var time = new FakeEffectTime();
        return (new HdrMomentPlayer(parts, clock, time, () => 17), parts, clock, time);
    }

    /// <summary>Längste Dauer aller Animationen eines Storyboards (Beginn plus Dauer).</summary>
    private static TimeSpan Longest(Storyboard board) => board.Children
        .Select(t => (t.BeginTime ?? TimeSpan.Zero) + (t.Duration.HasTimeSpan ? t.Duration.TimeSpan : t switch
        {
            DoubleAnimationUsingKeyFrames k => k.KeyFrames.Cast<DoubleKeyFrame>().Max(f => f.KeyTime.TimeSpan),
            _ => TimeSpan.MaxValue,
        }))
        .DefaultIfEmpty(TimeSpan.Zero)
        .Max();

    [Fact]
    public void Effect_hdr_moment_full_hooks_the_frame_clock_only_for_2_5_seconds()
    {
        WpfTestHost.Run(() =>
        {
            var (player, parts, clock, time) = Create();

            Assert.Equal(HdrMomentKind.Full, player.Play(EffectLevel.Full, Shown, Scene, transientBoost: true));
            Assert.True(player.IsHooked);
            Assert.Equal(1, clock.Subscribers);
            Assert.Equal(ParticleSystem.BurstCount, player.System!.Particles.Count); // Funkenregen am Spitzlicht, blendet ein

            var frames = 0;
            var peak = 0;
            for (var i = 0; i < 240 && player.IsHooked; i++)
            {
                time.Advance(TimeSpan.FromSeconds(1.0 / 60));
                clock.Tick();
                frames++;
                peak = Math.Max(peak, parts.Particles.Drawn);
            }

            Assert.False(player.IsHooked);
            Assert.Equal(0, clock.Subscribers); // danach kein Bildaufruf mehr
            Assert.InRange(frames, 140, 152); // 2,5 s bei 60 Bildern pro Sekunde
            Assert.InRange(peak, 1, EffectPolicy.MaxParticles);
            Assert.Equal(0, parts.Particles.Drawn);
            player.Dispose();
        });
    }

    [Fact]
    public void Effect_hdr_moment_storyboard_plays_at_60_fps_and_ends_within_2_5_seconds()
    {
        WpfTestHost.Run(() =>
        {
            var (player, _, _, _) = Create();
            player.Play(EffectLevel.Full, Shown, Scene, transientBoost: true);

            var board = Assert.IsType<Storyboard>(player.Board);
            Assert.Equal(EffectPolicy.OneShotFrameRate, Timeline.GetDesiredFrameRate(board));
            Assert.True(Longest(board) <= EffectPolicy.HdrMomentDuration, "längste Animation: " + Longest(board));
            Assert.Equal(TimeSpan.FromSeconds(1.6), HdrMomentPlayer.SweepDuration);
            Assert.Equal(TimeSpan.FromSeconds(1.8), HdrMomentPlayer.EdgeDuration);
            player.Dispose();
        });
    }

    [Fact]
    public void Effect_hdr_moment_reduced_fades_only_the_boost_without_particles_or_clock()
    {
        WpfTestHost.Run(() =>
        {
            var (player, parts, clock, _) = Create();

            Assert.Equal(HdrMomentKind.Reduced, player.Play(EffectLevel.Reduced, Shown, Scene, transientBoost: true));
            Assert.False(player.IsHooked);
            Assert.Equal(0, clock.Subscribers);
            Assert.Null(player.System);
            Assert.Equal(0, parts.Particles.Drawn);

            var board = Assert.IsType<Storyboard>(player.Board);
            var fade = Assert.IsType<DoubleAnimationUsingKeyFrames>(Assert.Single(board.Children));
            Assert.Same(parts.Boost(), Storyboard.GetTarget(fade));
            Assert.Equal(EffectPolicy.ReducedTransition, fade.KeyFrames[0].KeyTime.TimeSpan); // Einblenden höchstens 0,3 s
            player.Dispose();
        });
    }

    [Theory]
    [InlineData(EffectLevel.Full)]
    [InlineData(EffectLevel.Reduced)]
    public void Effect_hdr_moment_with_real_hdr_leaves_the_bright_layer_to_the_hero_stage(EffectLevel level)
    {
        WpfTestHost.Run(() =>
        {
            // Bei echtem HDR blendet der Titelbereich die helle Fassung ein und hält sie; nähme der Moment die Ebene, fiele sie
            // beim Entfernen seines Storyboards (Ende, Abbruch) zurück. Nur der Prüf-Moment blendet sie selbst kurz ein.
            var (player, parts, _, _) = Create();
            var boost = (DependencyObject)parts.Boost()!;
            player.Play(level, Shown, Scene, transientBoost: false);
            Assert.DoesNotContain(boost, player.Board!.Children.Select(Storyboard.GetTarget));

            player.Play(level, Shown, Scene, transientBoost: true);
            Assert.Contains(boost, player.Board!.Children.Select(Storyboard.GetTarget));
            player.Dispose();
        });
    }

    [Theory]
    [InlineData("Off", false, false)]
    [InlineData("Full", true, false)]
    [InlineData("Full", false, true)]
    public void Effect_hdr_moment_plays_nothing_when_off_minimized_or_while_playing(string level, bool minimized, bool playing)
    {
        WpfTestHost.Run(() =>
        {
            var (player, parts, clock, _) = Create();
            var context = Shown with { Minimized = minimized, GameRunning = playing };

            Assert.Equal(HdrMomentKind.None, player.Play(EffectPolicy.Parse(level), context, Scene, transientBoost: true));
            Assert.Null(player.Board);
            Assert.False(player.IsPlaying);
            Assert.Equal(0, clock.Subscribers);
            Assert.Equal(0, parts.Particles.Drawn);
            player.Dispose();
        });
    }

    [Fact]
    public void Effect_hdr_moment_stop_and_dispose_release_clock_storyboard_and_particles()
    {
        WpfTestHost.Run(() =>
        {
            var (player, parts, clock, time) = Create();
            player.Play(EffectLevel.Full, Shown, Scene, transientBoost: false);
            time.Advance(TimeSpan.FromSeconds(0.2));
            clock.Tick();

            player.Dispose();

            Assert.False(player.IsPlaying);
            Assert.Null(player.Board);
            Assert.Equal(0, clock.Subscribers);
            Assert.Equal(0, parts.Particles.Drawn);
            Assert.Equal(HdrMomentKind.None, player.Play(EffectLevel.Full, Shown, Scene, transientBoost: true)); // freigegeben
        });
    }

    [Fact]
    public void Effect_hdr_moment_freeze_is_deterministic_and_needs_no_frame_clock()
    {
        WpfTestHost.Run(() =>
        {
            var (player, parts, clock, _) = Create();

            player.Freeze(Scene, 0.35, transientBoost: false);
            var first = player.System!.Particles.Select(p => (p.X, p.Y, p.Color)).ToList();
            var drawn = parts.Particles.Drawn;
            player.Freeze(Scene, 0.35, transientBoost: false);
            var second = player.System!.Particles.Select(p => (p.X, p.Y, p.Color)).ToList();

            Assert.Equal(first, second);
            Assert.True(drawn > 0);
            Assert.False(player.IsHooked);
            Assert.Equal(0, clock.Subscribers);
            Assert.Contains(first, p => p.Color > 0); // alle Farben des Moments, nicht nur die erste
            player.Dispose();
        });
    }

    [Fact]
    public void Effect_particles_never_exceed_the_limit_and_finish_after_2_5_seconds()
    {
        foreach (var seed in new[] { 1, 7, 42, 4711, 90210 })
        {
            var system = new ParticleSystem(new Random(seed), 4);
            system.Start(3440, 900, 1700, 300);
            var steps = 0;
            while (system.Step(1.0 / 144))
            {
                Assert.True(system.Particles.Count <= EffectPolicy.MaxParticles, $"Startwert {seed}: {system.Particles.Count}");
                steps++;
            }

            Assert.True(system.Peak <= EffectPolicy.MaxParticles);
            Assert.True(system.Peak > ParticleSystem.BurstCount, "Lichtstreifen bringt weitere Funken");
            Assert.True(system.Finished);
            Assert.Empty(system.Particles);
            Assert.InRange(steps * (1.0 / 144), 2.45, 2.51);
        }
    }

    [Fact]
    public void Effect_particles_clamp_long_frames_and_stop_emitting_in_the_fade_out()
    {
        var system = new ParticleSystem(new Random(3), 2);
        system.Start(1600, 400, 800, 120);

        Assert.True(system.Step(1.0)); // ein hängendes Bild zählt höchstens 0,05 s
        Assert.Equal(0.05, system.Elapsed, 6);
        Assert.True(system.Step(double.NaN));
        Assert.Equal(0.05, system.Elapsed, 6);

        while (system.Elapsed < EffectPolicy.HdrMomentDuration.TotalSeconds - ParticleSystem.FadeOutSeconds + 0.01)
        {
            system.Step(0.02);
        }

        var count = system.Particles.Count;
        system.Step(0.02);
        Assert.True(system.Particles.Count <= count, "in der Ausblendung keine neuen Funken");
        Assert.InRange(system.FadeOut, 0, 1);
    }

    [Fact]
    public void Effect_particle_layer_draws_into_a_single_drawing_visual()
    {
        WpfTestHost.Run(() =>
        {
            var layer = new ParticleLayer();
            layer.SetColors([Colors.Red, Colors.Blue]);
            var system = new ParticleSystem(new Random(5), layer.ColorCount);
            system.Start(800, 300, 400, 100);
            system.Step(0.2);

            layer.Render(system.Particles, 1);

            Assert.Equal(1, VisualTreeHelper.GetChildrenCount(layer));
            Assert.IsType<DrawingVisual>(VisualTreeHelper.GetChild(layer, 0));
            Assert.InRange(layer.Drawn, 1, EffectPolicy.MaxParticles);
            Assert.False(layer.IsHitTestVisible);
            layer.Clear();
            Assert.Equal(0, layer.Drawn);
        });
    }

    /// <summary>Bildtakt zum Weiterschalten von Hand.</summary>
    private sealed class FakeFrameClock : IFrameClock
    {
        private EventHandler? _frame;

        public event EventHandler? Frame
        {
            add
            {
                _frame += value;
                Subscribers++;
            }

            remove
            {
                _frame -= value;
                Subscribers--;
            }
        }

        public int Subscribers { get; private set; }

        public void Tick() => _frame?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeEffectTime : IEffectTime
    {
        public TimeSpan Now { get; private set; } = TimeSpan.FromSeconds(100);

        public void Advance(TimeSpan by) => Now += by;
    }
}
