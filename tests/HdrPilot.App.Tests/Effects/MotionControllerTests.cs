using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// Steuerung der Dauer-Effekte eines Fensters (E89, G10.6, G10.9): Storyboards entstehen erst, wenn sie laufen dürfen,
/// zeigen höchstens 30 Bilder pro Sekunde, halten im Hintergrund an der Stelle an, werden minimiert, unsichtbar, beim
/// Spielen und beim Wechsel der Stufe entfernt und beim Schließen endgültig freigegeben.
/// </summary>
public sealed class MotionControllerTests
{
    private static (MotionController Motion, StaticAppearanceService Appearance, Border Target) Create(string? level = null, Action<AppearanceSettings>? settings = null)
    {
        var appearanceSettings = new AppearanceSettings { EffectLevel = level };
        settings?.Invoke(appearanceSettings);
        var appearance = new StaticAppearanceService(appearanceSettings);
        return (new MotionController(appearance), appearance, new Border());
    }

    private static Storyboard Spin(DependencyObject target)
    {
        var board = new Storyboard();
        var animation = new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        board.Children.Add(animation);
        return board;
    }

    private static void Open(MotionController motion) => motion.SetWindowState(visible: true, minimized: false, active: true);

    [Fact]
    public void Motion_effects_do_not_start_in_a_window_that_is_not_shown()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var effect = motion.Register("KenBurns", target, () => Spin(target));

            Assert.Equal(0, effect.Created);
            Assert.Null(effect.Board);
            Assert.False(motion.ContinuousAllowed);
            Assert.Equal(MotionPause.Hidden, motion.Pause);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_continuous_effect_runs_with_at_most_30_frames_per_second()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var effect = motion.Register("Rays", target, () => Spin(target));
            Open(motion);

            Assert.True(effect.IsRunning);
            Assert.False(effect.IsPaused);
            Assert.Equal(1, effect.Created);
            Assert.Equal(EffectPolicy.ContinuousFrameRate, Timeline.GetDesiredFrameRate(effect.Board!));
            Assert.Equal(30, EffectPolicy.ContinuousFrameRate);
            Assert.Equal(MotionPause.None, motion.Pause);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_reduced_level_never_creates_a_continuous_storyboard()
    {
        WpfTestHost.Run(() =>
        {
            var calls = 0;
            var (motion, _, target) = Create("Reduced");
            var effect = motion.Register("Bokeh", target, () =>
            {
                calls++;
                return Spin(target);
            });
            Open(motion);

            Assert.Equal(EffectLevel.Reduced, motion.Level);
            Assert.Equal(0, calls);
            Assert.Null(effect.Board);
            Assert.True(motion.OneShotAllowed); // Einmal-Effekte gibt es noch, kurz
            Assert.Equal(EffectPolicy.ReducedTransition, motion.Transition(TimeSpan.FromSeconds(0.9)));
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_off_level_allows_neither_continuous_nor_one_shot_effects()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create("Off");
            var effect = motion.Register("TitleSheen", target, () => Spin(target));
            Open(motion);

            Assert.Equal(0, effect.Created);
            Assert.False(motion.OneShotAllowed);
            Assert.Equal(TimeSpan.Zero, motion.Transition(TimeSpan.FromSeconds(0.9)));
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_inactive_window_pauses_in_place_and_resumes_without_a_new_storyboard()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var effect = motion.Register("KenBurns", target, () => Spin(target));
            Open(motion);
            var board = effect.Board;

            motion.SetWindowState(visible: true, minimized: false, active: false);
            Assert.True(effect.IsPaused);
            Assert.Same(board, effect.Board); // angehalten, nicht entfernt: kein Sprung beim Zurückkommen
            Assert.Equal(MotionPause.Background, motion.Pause);

            Open(motion);
            Assert.False(effect.IsPaused);
            Assert.True(effect.IsRunning);
            Assert.Equal(1, effect.Created);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_minimized_window_removes_the_storyboard_and_restarts_fresh()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var effect = motion.Register("Rays", target, () => Spin(target));
            Open(motion);

            motion.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Null(effect.Board);
            Assert.False(effect.IsRunning);
            Assert.Equal(MotionPause.Hidden, motion.Pause);
            Assert.False(motion.OneShotAllowed);

            Open(motion);
            Assert.True(effect.IsRunning);
            Assert.Equal(2, effect.Created);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_hidden_window_removes_every_effect()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var first = motion.Register("KenBurns", target, () => Spin(target));
            var second = motion.Register("Bokeh", target, () => Spin(target));
            Open(motion);

            motion.SetWindowState(visible: false, minimized: false, active: false);
            Assert.All(new[] { first, second }, e => Assert.Null(e.Board));
            Assert.Equal(MotionPause.Hidden, motion.Pause);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_running_game_removes_effects_unless_pausing_while_playing_is_off()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, appearance, target) = Create();
            var effect = motion.Register("SelectionRing", target, () => Spin(target));
            Open(motion);

            motion.SetGameRunning(true);
            Assert.Null(effect.Board);
            Assert.Equal(MotionPause.GameRunning, motion.Pause);
            Assert.False(motion.OneShotAllowed); // kein HDR-Moment über einem Spiel

            // Gegenprobe: Schalter „Beim Spielen anhalten“ aus, dann laufen sie weiter.
            appearance.Update(s => s.PauseWhilePlaying = false);
            Assert.True(effect.IsRunning);
            Assert.Equal(MotionPause.None, motion.Pause);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_inactive_window_keeps_running_when_pausing_in_background_is_off()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create(settings: s => s.PauseInBackground = false);
            var effect = motion.Register("CapsulePulse", target, () => Spin(target));
            motion.SetWindowState(visible: true, minimized: false, active: false);

            Assert.True(effect.IsRunning);
            Assert.False(effect.IsPaused);

            // Minimiert gilt trotzdem nie.
            motion.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Null(effect.Board);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_level_change_removes_effects_and_rebuilds_them_only_on_full()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, appearance, target) = Create();
            var effect = motion.Register("TitleSheen", target, () => Spin(target));
            Open(motion);
            var changed = 0;
            motion.Changed += (_, _) => changed++;

            appearance.Update(s => s.EffectLevel = "Reduced");
            Assert.Equal(EffectLevel.Reduced, motion.Level);
            Assert.Null(effect.Board);
            Assert.True(changed >= 1);

            appearance.Update(s => s.EffectLevel = "Full");
            Assert.True(effect.IsRunning);
            Assert.Equal(2, effect.Created);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_windows_animation_setting_reduces_full_effects()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, appearance, target) = Create();
            var effect = motion.Register("Bokeh", target, () => Spin(target));
            Open(motion);
            Assert.True(effect.IsRunning);

            appearance.WindowsAnimationsOn = false;
            appearance.RaiseChanged();
            Assert.Equal(EffectLevel.Reduced, motion.Level);
            Assert.Null(effect.Board);

            // Gegenprobe: „Windows-Animationseffekte beachten“ aus, dann wieder „Voll“.
            appearance.Update(s => s.RespectWindowsAnimations = false);
            Assert.Equal(EffectLevel.Full, motion.Level);
            Assert.True(effect.IsRunning);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_unwanted_effect_is_removed_and_starts_again_when_wanted()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var pulse = motion.Register("CapsulePulse", target, () => Spin(target), wanted: false);
            Open(motion);
            Assert.Equal(0, pulse.Created); // HDR aus: kein Puls

            pulse.SetWanted(true);
            Assert.True(pulse.IsRunning);
            pulse.SetWanted(false);
            Assert.Null(pulse.Board);
            Assert.False(pulse.IsRunning);
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_closing_the_window_removes_every_effect_and_unsubscribes()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, appearance, target) = Create();
            var window = new Window();
            motion.Attach(window);
            var effect = motion.Register("KenBurns", target, () => Spin(target));
            Open(motion);
            Assert.True(effect.IsRunning);

            window.Close();

            Assert.True(motion.IsDisposed);
            Assert.Null(effect.Board);
            Assert.Empty(motion.Effects);
            Assert.False(motion.OneShotAllowed);

            // Abgemeldet: Änderungen der Darstellung erreichen den Controller nicht mehr.
            appearance.Update(s => s.EffectLevel = "Reduced");
            Assert.Equal(EffectLevel.Full, motion.Level);
        });
    }

    [Fact]
    public void Motion_unregister_removes_the_storyboard_of_one_effect()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, target) = Create();
            var keep = motion.Register("Rays", target, () => Spin(target));
            var drop = motion.Register("RescanSpin", target, () => Spin(target));
            Open(motion);

            motion.Unregister(drop);

            Assert.Null(drop.Board);
            Assert.True(keep.IsRunning);
            Assert.Equal(["Rays"], motion.Effects.Select(e => e.Name));
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_transitions_jump_to_the_end_in_a_hidden_window_or_over_a_game()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, _) = Create();
            var full = TimeSpan.FromSeconds(0.9);

            Assert.Equal(TimeSpan.Zero, motion.Transition(full)); // nie angezeigt
            Open(motion);
            Assert.Equal(full, motion.Transition(full));
            motion.SetWindowState(visible: true, minimized: false, active: false);
            Assert.Equal(full, motion.Transition(full)); // im Hintergrund sichtbar: weich
            motion.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Equal(TimeSpan.Zero, motion.Transition(full));
            Open(motion);
            motion.SetGameRunning(true);
            Assert.Equal(TimeSpan.Zero, motion.Transition(full));
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_changed_reports_a_new_pause_reason_for_the_status_line()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, _) = Create();
            var changes = new List<MotionPause>();
            motion.Changed += (_, _) => changes.Add(motion.Pause);
            motion.SetWindowState(visible: true, minimized: true, active: false);
            changes.Clear();

            // Wieder sichtbar, aber inaktiv: Dauer-Effekte bleiben aus, nur der Grund ändert sich.
            motion.SetWindowState(visible: true, minimized: false, active: false);
            Assert.Equal([MotionPause.Background], changes);

            motion.SetWindowState(visible: true, minimized: false, active: false);
            Assert.Single(changes); // unverändert: keine Meldung
            motion.Dispose();
        });
    }

    [Fact]
    public void Motion_window_never_shown_reports_a_hidden_context()
    {
        WpfTestHost.Run(() =>
        {
            var (motion, _, _) = Create();
            var window = new Window();
            motion.Attach(window);

            Assert.False(motion.Context.WindowVisible);
            Assert.False(motion.ContinuousAllowed);
            Assert.False(motion.OneShotAllowed);
            window.Close();
        });
    }
}
