using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// Messbarkeit der Effekte (G10.9): Der <see cref="MotionController"/> nennt, wie viele Dauer-Effekte noch Uhren haben, die
/// WPF taktet (angehalten und minimiert: 0, phase10-gpu.ps1 prüft das am Protokoll), und meldet Änderungen der Lage des
/// Fensters (nur ein aktives Fenster rechnet die helle Fassung). Eine spät kommende helle Fassung blendet über, wenn ihre
/// Ebene gerade zu sehen ist.
/// </summary>
public sealed class MotionClockTests
{
    private static Storyboard Spin(DependencyObject target)
    {
        var board = new Storyboard();
        var animation = new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        board.Children.Add(animation);
        return board;
    }

    private static ArtworkImage Image(Color color) =>
        new(FakeArtworkService.Solid(32, 9, color), ArtworkKind.Hero, ArtworkOrigin.Launcher, 32, 9, "Steam");

    private static GameArtwork Artwork(string key, ArtworkImage hero) =>
        new(key, hero, null, null, null, null, null, AmbientColors.FromName(key), ArtworkSummary.Empty);

    private static bool IsAnimated(UIElement element) => element.HasAnimatedProperties;

    [Fact]
    public void Motion_live_clocks_count_running_effects_and_none_while_paused_or_minimized()
    {
        WpfTestHost.Run(() =>
        {
            var motion = new MotionController(new StaticAppearanceService(new AppearanceSettings()));
            var first = new Border();
            var second = new Border();
            motion.Register("KenBurns", first, () => Spin(first));
            motion.Register("Rays", second, () => Spin(second));
            Assert.Equal(0, motion.LiveClocks); // nie gezeigt: kein Storyboard

            // Uhren beginnen erst mit dem nächsten Takt der Zeitsteuerung; hier rechnet ein Sprung an den Anfang sie sofort.
            motion.SetWindowState(visible: true, minimized: false, active: true);
            motion.Effects[0].Board!.SeekAlignedToLastTick(first, TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            motion.Effects[1].Board!.SeekAlignedToLastTick(second, TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            Assert.Equal(2, motion.LiveClocks);

            // Im Hintergrund angehalten: Werte gehalten, aber keine Uhr mehr (ein nur angehaltenes Storyboard taktete weiter).
            motion.SetWindowState(visible: true, minimized: false, active: false);
            Assert.All(motion.Effects, e => Assert.True(e.IsPaused));
            Assert.Equal(0, motion.LiveClocks);

            motion.SetWindowState(visible: true, minimized: false, active: true);
            Assert.Equal(2, motion.LiveClocks);

            motion.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Equal(0, motion.LiveClocks);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_reports_window_changes_with_the_active_state()
    {
        WpfTestHost.Run(() =>
        {
            var motion = new MotionController(new StaticAppearanceService(new AppearanceSettings()));
            var seen = new List<bool>();
            motion.WindowChanged += (_, _) => seen.Add(motion.WindowActive);

            motion.SetWindowState(visible: true, minimized: false, active: true);
            motion.SetWindowState(visible: true, minimized: false, active: true); // unverändert: keine Meldung
            motion.SetWindowState(visible: true, minimized: false, active: false);
            motion.SetWindowState(visible: true, minimized: true, active: false);
            motion.SetWindowState(visible: true, minimized: false, active: true);

            Assert.Equal([true, false, false, true], seen);
            motion.Dispose();
            motion.SetWindowState(visible: false, minimized: false, active: false);
            Assert.Equal(4, seen.Count); // geschlossen: keine Meldung mehr
        });
    }

    [Fact]
    public void Effect_late_bright_variant_fades_in_while_its_layer_shows_and_appears_at_once_otherwise()
    {
        WpfTestHost.Run(() =>
        {
            var stage = new HeroStage();
            stage.Measure(new Size(1600, 300));
            stage.Arrange(new Rect(0, 0, 1600, 300));

            // HDR aus (Ebene nicht zu sehen): Die vorab geladene Fassung steht sofort da, ohne Uhr.
            var plain = Artwork("A", Image(Colors.DarkOrange));
            stage.Show(null, plain, TimeSpan.Zero);
            var early = Image(Colors.Moccasin);
            stage.Show(null, plain with { HeroBoost = early }, HeroStage.CrossfadeDuration);
            Assert.Same(early.Image, stage.ShownBoost);
            Assert.False(IsAnimated(stage.CurrentBoostImageLayer));
            Assert.Equal(1, stage.CurrentBoostImageLayer.Opacity);

            // HDR an, anderes Spiel ohne Fassung (warmes Licht), sie kommt nach: Sie blendet über, statt aufzuspringen.
            stage.SetHdr(on: true, TimeSpan.Zero);
            var other = Artwork("B", Image(Colors.SteelBlue));
            stage.Show(null, other, TimeSpan.Zero);
            Assert.Null(stage.ShownBoost);
            Assert.NotNull(stage.CurrentBoostLayer());
            var late = Image(Colors.LightSteelBlue);
            stage.Show(null, other with { HeroBoost = late }, HeroStage.CrossfadeDuration);
            Assert.Same(late.Image, stage.ShownBoost);
            Assert.True(IsAnimated(stage.CurrentBoostImageLayer));

            // Stufe „Aus“ (kein Übergang): auch bei HDR an sofort.
            var third = Artwork("C", Image(Colors.DarkRed));
            stage.Show(null, third, TimeSpan.Zero);
            stage.Show(null, third with { HeroBoost = Image(Colors.IndianRed) }, TimeSpan.Zero);
            Assert.False(IsAnimated(stage.CurrentBoostImageLayer));
            Assert.Equal(1, stage.CurrentBoostImageLayer.Opacity);
        });
    }
}
