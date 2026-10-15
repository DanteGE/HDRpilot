using System.ComponentModel;
using System.Windows;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Dauer-Effekte der Einstellungen (Vorführkarte „Voll“, Zoom der Live-Vorschau) folgen den Regeln der Effekte (E89, G10.6):
/// nur bei „Voll“, sichtbarem, nicht minimiertem und (mit „Im Hintergrund anhalten“) aktivem Fenster; Schließen hält an.
/// </summary>
public sealed class SettingsMotionTests
{
    [Fact]
    public void Settings_motion_runs_only_while_the_window_is_visible_active_and_effects_are_full()
    {
        WpfTestHost.Run(() =>
        {
            var appearance = new StaticAppearanceService();
            var context = new MotionContext(WindowVisible: true, Minimized: false, Active: true, GameRunning: false, PauseInBackground: true, PauseWhilePlaying: true);
            var calls = new List<string>();
            var source = new Notifier();
            var window = new Window();
            var motion = new SettingsMotion(window, () => appearance.Current, source, () => calls.Add("start"), () => calls.Add("stop"), () => context);
            Assert.True(motion.IsRunning);

            context = context with { Active = false };
            motion.Update();
            Assert.False(motion.IsRunning);

            // „Im Hintergrund anhalten“ aus: auch inaktiv weiter, minimiert aber nie.
            context = context with { PauseInBackground = false };
            motion.Update();
            Assert.True(motion.IsRunning);
            context = context with { Minimized = true };
            motion.Update();
            Assert.False(motion.IsRunning);

            // Stufe „Reduziert“: keine Dauer-Effekte, auch bei aktivem Fenster (Darstellung meldet die Änderung).
            context = context with { Minimized = false, Active = true, PauseInBackground = true };
            motion.Update();
            Assert.True(motion.IsRunning);
            appearance.Update(s => s.EffectLevel = "Reduced");
            source.Raise();
            Assert.False(motion.IsRunning);
            Assert.True(motion.OneShotAllowed); // Einmal-Effekte bleiben bei „Reduziert“ erlaubt
            appearance.Update(s => s.EffectLevel = "Off");
            source.Raise();
            Assert.False(motion.OneShotAllowed);

            Assert.Equal(["start", "stop", "start", "stop", "start", "stop"], calls);
            motion.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void Settings_motion_stops_and_unsubscribes_when_disposed()
    {
        WpfTestHost.Run(() =>
        {
            var appearance = new StaticAppearanceService();
            var stops = 0;
            var source = new Notifier();
            var window = new Window();
            var context = new MotionContext(true, false, true, false, true, true);
            var motion = new SettingsMotion(window, () => appearance.Current, source, () => { }, () => stops++, () => context);
            Assert.True(motion.IsRunning);
            Assert.Equal(1, source.Subscribers);

            window.Close(); // Schließen beendet alles
            Assert.False(motion.IsRunning);
            Assert.Equal(1, stops);
            Assert.Equal(0, source.Subscribers);

            motion.Dispose(); // zweites Mal ohne Wirkung
            Assert.Equal(1, stops);
        });
    }

    [Fact]
    public void Settings_motion_never_starts_for_a_window_that_was_never_shown()
    {
        WpfTestHost.Run(() =>
        {
            var started = false;
            var window = new Window();
            using var motion = new SettingsMotion(window, () => new StaticAppearanceService(new AppearanceSettings()).Current, null, () => started = true, () => { });

            Assert.False(motion.IsRunning);
            Assert.False(started);
            Assert.False(motion.Context.WindowVisible);
            window.Close();
        });
    }

    [Fact]
    public void Settings_window_starts_no_continuous_effect_before_it_is_shown()
    {
        WpfTestHost.Run(() =>
        {
            var window = SettingsLayoutTests.CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1600, 1000));
            Assert.False(window.ContinuousEffectsRunning);
            window.Close();
        });
    }

    private sealed class Notifier : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Subscribers => PropertyChanged?.GetInvocationList().Length ?? 0;

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
