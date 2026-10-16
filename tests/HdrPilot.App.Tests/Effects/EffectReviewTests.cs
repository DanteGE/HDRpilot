using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// Effekte nach der unabhängigen Prüfung (G10.10): Der HDR-Moment blendet die helle Fassung des gerade sichtbaren Bildplatzes
/// ein (auch wenn sie erst nach dem Öffnen kam), Partikel takten mit höchstens 60 Bildern pro Sekunde über eine Zeitleiste,
/// Dauer-Effekte laufen nur, wenn etwas sie zeigt, und die Stufe gilt auch für Einstellungen und Tray-Menü.
/// </summary>
public sealed class EffectReviewTests
{
    private static readonly MotionContext Shown = new(WindowVisible: true, Minimized: false, Active: true, GameRunning: false, PauseInBackground: true, PauseWhilePlaying: true);

    private static (MainWindow Window, LibraryViewModel Model, FakeArtworkService Art, TestLibraryContext Context) Open(Action<FakeArtworkService>? setup = null)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }

        var art = new FakeArtworkService();
        art.WithHero.Add("Steam:1091500");
        art.WithHero.Add("Epic:Crow");
        setup?.Invoke(art);
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false))
        {
            Artwork = art,
            Appearance = new StaticAppearanceService(new AppearanceSettings()),
        };
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        context.Dispatcher.RunDelayed();
        var window = new MainWindow();
        window.Attach(model);
        WpfTestHost.Layout(window, new Size(1600, 1000));
        window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
        return (window, model, art, context);
    }

    private static IEnumerable<DependencyObject> Targets(Storyboard board) => board.Children.Select(Storyboard.GetTarget);

    [Fact]
    public void Effect_hdr_moment_looks_up_the_bright_variant_when_it_plays_not_when_the_window_opens()
    {
        WpfTestHost.Run(() =>
        {
            // Teile wie im Fenster: geholt, bevor ein Bild geladen ist.
            var stage = new HeroStage();
            stage.Measure(new Size(1600, 300));
            stage.Arrange(new Rect(0, 0, 1600, 300));
            var parts = stage.MomentParts(new System.Windows.Controls.Border());
            using var player = new HdrMomentPlayer(parts, new TimelineFrameClock(), new StopwatchEffectTime());
            var scene = stage.MomentScene([Colors.Orange]);

            stage.Show(null, Artwork("A"), TimeSpan.Zero);
            Assert.Equal(HdrMomentKind.Full, player.Play(EffectLevel.Full, Shown, scene, transientBoost: true));
            var first = stage.CurrentBoostLayer();
            Assert.NotNull(first);
            Assert.Contains(first, Targets(player.Board!));

            // Nächstes Bild im anderen Platz: dessen helle Fassung, nicht mehr die des ersten.
            stage.Show(null, Artwork("B"), TimeSpan.Zero);
            Assert.Equal(HdrMomentKind.Full, player.Play(EffectLevel.Full, Shown, scene, transientBoost: true));
            var second = stage.CurrentBoostLayer();
            Assert.NotSame(first, second);
            Assert.Contains(second!, Targets(player.Board!));
            Assert.DoesNotContain(first, Targets(player.Board!));
        });
    }

    [Fact]
    public void Effect_test_moment_loads_the_bright_variant_first_and_fades_it()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, art, context) = Open();
            var stage = window.HeroStage;
            Assert.Null(stage.ShownBoost); // HDR aus: keine helle Fassung geladen

            // Prüfung --test-hdr-moment: erst die helle Fassung holen, dann spielen.
            var played = window.PlayTestHdrMomentAsync();
            Assert.True(played.IsCompleted);
            Assert.Equal(HdrMomentKind.Full, played.Result);
            Assert.NotNull(stage.ShownBoost);
            Assert.Contains(stage.CurrentBoostLayer()!, Targets(window.Moment!.Board!));
            Assert.Equal(7, window.Moment.Board!.Children.Count); // Streifen (2), Linie (2), Blitz, Rand und helle Fassung

            // Anderes Spiel: der Moment blendet dessen helle Fassung ein.
            model.SelectedGame = model.Games[1];
            context.Dispatcher.RunDelayed();
            Assert.Equal(HdrMomentKind.Full, window.PlayTestHdrMomentAsync().Result);
            Assert.Contains(stage.CurrentBoostLayer()!, Targets(window.Moment.Board!));

            // Je Spiel einmal geholt: vor dem Moment oder schon vorab (aktives Fenster, Auswahl zur Ruhe gekommen).
            Assert.Equal(["Steam:1091500", "Epic:Crow"], art.BoostRequests.Concat(art.PrefetchRequests));
            window.Close();
        });
    }

    [Fact]
    public void Effect_reduced_test_moment_fades_the_bright_variant()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _, context) = Open();
            context.Appearance.Update(s => s.EffectLevel = "Reduced");

            Assert.Equal(HdrMomentKind.Reduced, window.PlayTestHdrMomentAsync().Result);

            var fade = Assert.Single(window.Moment!.Board!.Children);
            Assert.Same(window.HeroStage.CurrentBoostLayer(), Storyboard.GetTarget(fade));
            window.Close();
        });
    }

    [Fact]
    public void Effect_selection_ring_runs_only_where_tiles_draw_it_and_the_sheen_only_over_a_name()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _, context) = Open(art => art.WithLogo.Add("Epic:Crow"));
            Effect(window, "SelectionRing").Let(e => Assert.False(e.IsRunning)); // Liste: kein Kachelring zu sehen
            Effect(window, "TitleSheen").Let(e => Assert.True(e.IsRunning)); // Name in der Titelschrift

            model.SetViewMode(LibraryViewMode.Grid, persist: false);
            Effect(window, "SelectionRing").Let(e => Assert.True(e.IsRunning));
            Assert.Equal(EffectPolicy.ContinuousFrameRate, Timeline.GetDesiredFrameRate(Effect(window, "SelectionRing").Board!));

            // Spiel mit Logo: der Glanz-Text ist ausgeblendet, also steht auch seine Animation.
            model.SelectedGame = model.Games[1];
            context.Dispatcher.RunDelayed();
            Assert.True(model.Hero!.HasLogo);
            Effect(window, "TitleSheen").Let(e => Assert.False(e.IsRunning));

            model.SetViewMode(LibraryViewMode.List, persist: false);
            Effect(window, "SelectionRing").Let(e => Assert.False(e.IsRunning));
            window.Close();
        });
    }

    [Fact]
    public void Effect_particles_are_clocked_by_a_60_fps_timeline_that_runs_only_while_subscribed() => Sta.Run(async () =>
    {
        // Eigener UI-Thread: Andere Tests auf dem gemeinsamen WPF-Thread liefen sonst in der Wartezeit dazwischen.
        var clock = new TimelineFrameClock();
        Assert.Equal(EffectPolicy.OneShotFrameRate, Timeline.GetDesiredFrameRate(clock.Timeline));
        Assert.Null(clock.Clock);

        var ticks = 0;
        EventHandler onFrame = (_, _) => ticks++;
        clock.Frame += onFrame;
        Assert.Equal(1, clock.Subscribers);
        Assert.NotNull(clock.Clock);

        // Ohne angezeigtes Fenster zeichnet WPF im Test nicht von selbst: Nur hier (nie in der App) hält ein Empfänger von
        // CompositionTarget.Rendering die Zeichenschleife am Laufen, damit die Uhr der Zeitleiste tickt.
        var rendered = 0;
        EventHandler pump = (_, _) => rendered++;
        CompositionTarget.Rendering += pump;
        try
        {
            await Task.Delay(500);
            Assert.True(rendered > 0, "Zeichenschleife lief nicht");
            Assert.InRange(ticks, 3, 40);

            clock.Frame -= onFrame;
            Assert.Equal(0, clock.Subscribers);
            Assert.Null(clock.Clock);
            var after = ticks;
            await Task.Delay(200);
            Assert.Equal(after, ticks); // abgemeldet: kein Takt mehr
        }
        finally
        {
            CompositionTarget.Rendering -= pump;
        }
    });

    [Fact]
    public void Effect_no_view_hooks_composition_target_rendering()
    {
        var app = SourceFiles.InRepository("src", "HdrPilot.App");
        var hits = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(f => File.ReadAllLines(f).Any(l => l.Contains("CompositionTarget.Rendering", StringComparison.Ordinal) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(hits); // sonst zeichnete WPF jedes Bild in der Bildwiederholrate des Bildschirms
    }

    [Theory]
    [InlineData(EffectLevel.Off, true, true, false)]
    [InlineData(EffectLevel.Reduced, true, true, true)]
    [InlineData(EffectLevel.Full, false, true, true)] // „Windows-Animationseffekte beachten“ aus: wirksam Voll
    [InlineData(EffectLevel.Full, true, false, false)] // ohne Hardware-Darstellung nie
    public void Effect_shell_motion_follows_the_effective_level(EffectLevel level, bool windowsAnimations, bool hardware, bool expected)
    {
        Assert.Equal(expected, ShellMotion.Allowed(level, windowsAnimations, hardware));
    }

    [Fact]
    public void Effect_shell_motion_without_appearance_follows_windows()
    {
        Assert.True(ShellMotion.Allowed(null, windowsAnimations: true, hardwareRendering: true));
        Assert.False(ShellMotion.Allowed(null, windowsAnimations: false, hardwareRendering: true));
    }

    [Fact]
    public void Effect_shell_motion_level_switches_settings_dialogs_and_tray_menu_off()
    {
        WpfTestHost.Run(() =>
        {
            var events = 0;
            void Handler(object? sender, PropertyChangedEventArgs e) => events++;
            ShellMotion.StaticPropertyChanged += Handler;
            try
            {
                ShellMotion.Level = EffectLevel.Off;
                Assert.False(ShellMotion.IsEnabled);
                Assert.False(HdrPilot.App.Infrastructure.Motion.Instance.IsEnabled); // Schalter und Karten in Controls.xaml
                ShellMotion.Level = EffectLevel.Reduced;
                Assert.Equal((RenderCapability.Tier >> 16) > 0, ShellMotion.IsEnabled);
                Assert.Equal(2, events);
            }
            finally
            {
                ShellMotion.Level = null;
                ShellMotion.StaticPropertyChanged -= Handler;
            }
        });
    }

    [Fact]
    public void Effect_library_motion_follows_the_level_not_only_windows()
    {
        WpfTestHost.Run(() =>
        {
            var motion = HdrPilot.App.Views.Library.LibraryMotion.Instance;
            var before = motion.Level;
            try
            {
                motion.SetLevel(EffectLevel.Off);
                Assert.False(motion.IsEnabled);
                motion.SetLevel(EffectLevel.Full);
                Assert.Equal(HdrPilot.App.Views.Library.LibraryMotion.HardwareRendering, motion.IsFull);
            }
            finally
            {
                motion.SetLevel(before);
            }
        });
    }

    private static ContinuousEffect Effect(MainWindow window, string name) => window.Motion!.Effects.Single(e => e.Name == name);

    /// <summary>Satz mit Titelbild und heller Fassung (kleine, erzeugte Bilder).</summary>
    private static Services.Artwork.GameArtwork Artwork(string key) => new(
        key,
        new Services.Artwork.ArtworkImage(FakeArtworkService.Solid(32, 9, Colors.DarkOrange), Core.Artwork.ArtworkKind.Hero, Core.Artwork.ArtworkOrigin.Launcher, 32, 9, "Steam"),
        new Services.Artwork.ArtworkImage(FakeArtworkService.Solid(32, 9, Colors.Moccasin), Core.Artwork.ArtworkKind.Hero, Core.Artwork.ArtworkOrigin.Launcher, 32, 9, "Steam"),
        null,
        null,
        null,
        null,
        Core.Artwork.AmbientColors.FromName(key),
        Services.Artwork.ArtworkSummary.Empty);
}

/// <summary>Kleine Hilfe für Prüfungen an einem Wert.</summary>
internal static class LetExtensions
{
    public static void Let<T>(this T value, Action<T> check) => check(value);
}
