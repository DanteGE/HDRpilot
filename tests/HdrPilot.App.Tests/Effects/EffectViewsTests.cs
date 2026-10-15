using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// Effekte im Hauptfenster (E89, G10.6): alle Dauer-Effekte hängen am <see cref="MotionController"/>, laufen nur im
/// sichtbaren, aktiven Fenster mit 30 Bildern pro Sekunde und sind nach dem Schließen weg; die Bibliothek benutzt weder
/// Weichzeichner noch Schatten- oder Mischeffekte (alles mit Verläufen gezeichnet).
/// </summary>
public sealed partial class EffectViewsTests
{
    private static readonly string[] ContinuousEffects = ["KenBurns", "Rays", "Bokeh", "CapsulePulse", "RescanSpin", "SelectionRing", "TitleSheen"];

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context) Open(string? level = null)
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", hdrAuto: true, isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: true))
        {
            Appearance = new StaticAppearanceService(new AppearanceSettings { EffectLevel = level }),
        };
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        var window = new MainWindow();
        window.Attach(model);
        WpfTestHost.Layout(window, new Size(1600, 1000));
        return (window, model, context);
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    [Fact]
    public void Effect_main_window_runs_continuous_effects_only_while_shown_and_releases_them_on_close()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context) = Open();
            var motion = window.Motion!;
            foreach (var name in ContinuousEffects)
            {
                Assert.Contains(motion.Effects, e => e.Name == name);
            }

            // Nie angezeigt: nichts läuft, kein Storyboard entstanden.
            Assert.All(motion.Effects, e => Assert.Null(e.Board));

            // Sichtbar und aktiv: die gewünschten laufen, jedes mit höchstens 30 Bildern pro Sekunde.
            motion.SetWindowState(visible: true, minimized: false, active: true);
            var running = motion.Effects.Where(e => e.IsRunning).ToList();
            Assert.Contains(running, e => e.Name == "KenBurns");
            Assert.Contains(running, e => e.Name == "SelectionRing");
            Assert.DoesNotContain(running, e => e.Name == "RescanSpin"); // nur beim Einlesen
            Assert.All(running, e => Assert.Equal(EffectPolicy.ContinuousFrameRate, Timeline.GetDesiredFrameRate(e.Board!)));
            Assert.False(string.IsNullOrWhiteSpace(model.EffectsText));

            // Minimiert: alles entfernt.
            motion.SetWindowState(visible: true, minimized: true, active: false);
            Assert.All(motion.Effects, e => Assert.Null(e.Board));

            motion.SetWindowState(visible: true, minimized: false, active: true);
            var moment = window.Moment!;
            window.PlayTestHdrMoment();
            Assert.True(moment.IsPlaying);

            window.Close();
            Assert.True(motion.IsDisposed);
            Assert.Empty(motion.Effects);
            Assert.False(moment.IsPlaying);
            Assert.False(moment.IsHooked);
            Assert.True(context.Disposed);
        });
    }

    [Fact]
    public void Effect_opening_fade_keeps_the_own_transforms_of_drawer_and_hero()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var view in new[] { LibraryViewMode.Shelf, LibraryViewMode.List })
            {
                var (window, model, _) = Open();
                model.SetViewMode(view, persist: false);
                WpfTestHost.Layout(window, new Size(1600, 1000));
                var details = (FrameworkElement)window.FindName("DetailsArea");
                var hero = (FrameworkElement)window.FindName("HeroContent");
                var drawerShift = details.RenderTransform;
                var heroShift = hero.RenderTransform;

                // Wie beim Öffnen des Fensters: Bereiche blenden nacheinander ein (Stufe „Voll“).
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Same(heroShift, hero.RenderTransform);
                Assert.Same(drawerShift, details.RenderTransform);

                model.IsDetailsDrawerOpen = view == LibraryViewMode.Shelf;
                WpfTestHost.Layout(window, new Size(1600, 1000));
                Assert.IsType<System.Windows.Media.TranslateTransform>(details.RenderTransform);
                window.Close();
            }
        });
    }

    [Fact]
    public void Effect_main_window_reduced_level_plays_only_the_short_moment()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open("Reduced");
            var motion = window.Motion!;
            motion.SetWindowState(visible: true, minimized: false, active: true);

            Assert.All(motion.Effects, e => Assert.Equal(0, e.Created));
            Assert.Equal(HdrMomentKind.Reduced, window.PlayTestHdrMoment());
            Assert.False(window.Moment!.IsHooked);
            Assert.False(LibraryMotionIsFull());
            window.Close();
        });
    }

    [Fact]
    public void Effect_main_window_off_level_plays_no_moment_and_no_storyboards()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open("Off");
            var motion = window.Motion!;
            motion.SetWindowState(visible: true, minimized: false, active: true);

            Assert.All(motion.Effects, e => Assert.Equal(0, e.Created));
            Assert.Equal(HdrMomentKind.None, window.PlayTestHdrMoment());
            Assert.Equal(EffectLevel.Off, model.EffectLevel);
            window.Close();
        });
    }

    [Fact]
    public void Effect_views_use_no_blur_shadow_shader_or_blend_effects()
    {
        var files = ViewFiles().ToList();
        Assert.True(files.Count >= 15, "Dateien der Bibliothek: " + files.Count);
        foreach (var file in files)
        {
            var code = WithoutComments(File.ReadAllText(file), file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
            var hit = Forbidden().Match(code);
            Assert.False(hit.Success, $"{Path.GetFileName(file)}: {hit.Value}");

            // Eigenes Zeichnen je Bild nur über den Bildtakt der Effekte (meldet sich nach 2,5 s ab), keine Zeitgeber.
            if (!file.EndsWith("FrameClock.cs", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("CompositionTarget.Rendering", code, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
        }

        // Gegenprobe: die Prüfung fände einen Schatten.
        Assert.Matches(Forbidden(), "<Border.Effect><DropShadowEffect BlurRadius=\"8\" /></Border.Effect>");
        Assert.DoesNotMatch(Forbidden(), WithoutComments("<!-- kein DropShadowEffect -->", xaml: true));
    }

    private static bool LibraryMotionIsFull() => HdrPilot.App.Views.Library.LibraryMotion.Instance.IsFull;

    private static IEnumerable<string> ViewFiles()
    {
        var app = SourceFiles.InRepository("src", "HdrPilot.App");
        yield return Path.Combine(app, "MainWindow.xaml");
        yield return Path.Combine(app, "MainWindow.xaml.cs");
        yield return Path.Combine(app, "Themes", "Library.xaml");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(app, "Views", "Library"), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal)))
        {
            yield return file;
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(app, "Infrastructure", "Effects"), "*.cs"))
        {
            yield return file;
        }
    }

    private static string WithoutComments(string code, bool xaml) => xaml
        ? XamlComment().Replace(code, string.Empty)
        : CodeComment().Replace(code, string.Empty);

    [GeneratedRegex(@"\b(BlurEffect|DropShadowEffect|ShaderEffect|BitmapEffect\w*|BlendMode|RenderOptions\.SetEdgeMode)\b|\.Effect>|\sEffect=""|\.Effect\s*=")]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XamlComment();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CodeComment();
}
