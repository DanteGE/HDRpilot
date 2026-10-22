using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Tiefenwirkung im Titelbereich (Wunsch des Nutzers 2026-10-22): Verlässt der Zeiger Titelbild oder Fenster, gleiten Titel,
/// Kennzeichen und Knöpfe gemeinsam mit den Bildebenen zurück (0,8 s, QuinticEase EaseOut), statt zu springen; kommt er zurück,
/// geht es vom aktuellen Wert aus weiter; ohne Dauer-Effekte steht alles sofort in der Mitte. Echte WPF-Animationen mit
/// echter Zeit (gepumpt wie in EffectReviewTests), aber nur mit großzügigen Grenzen: geprüft wird, dass nichts springt und
/// beide gleich weit sind, nicht die genaue Dauer.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class HeroPointerTests
{
    private static (MainWindow Window, LibraryViewModel Model) Open(string effects)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }

        var context = new TestLibraryContext([.. Enumerable.Range(1, 12).Select(i => Game(GameSource.Steam, "g" + i, $"Spiel {i:00}", isNew: false))])
        {
            Appearance = new HdrPilot.App.Theming.StaticAppearanceService(new AppearanceSettings { EffectLevel = effects }),
        };
        var model = new LibraryViewModel(context, new FakeDialogs());
        var window = new MainWindow();
        window.Attach(model);
        model.SelectedGame = model.Games[0];
        var size = new Size(1745, 982);
        WpfTestHost.Layout(window, size);
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, size);
        window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
        return (window, model);
    }

    /// <summary>Verschiebung von Titel, Kennzeichen und Knöpfen (HeroContent).</summary>
    private static TranslateTransform Content(MainWindow window) =>
        Assert.IsType<TranslateTransform>(((UIElement)window.FindName("HeroContent")).RenderTransform);

    /// <summary>Pumpt Nachrichten und Bildtakt, bis die Bedingung gilt (höchstens <paramref name="seconds"/>).</summary>
    private static bool PumpUntil(Func<bool> done, double seconds = 3)
    {
        EventHandler pump = (_, _) => { };
        CompositionTarget.Rendering += pump;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (!done())
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                WpfTestHost.DoEvents();
                Thread.Sleep(15);
            }

            return true;
        }
        finally
        {
            CompositionTarget.Rendering -= pump;
        }
    }

    /// <summary>Zeiger unten rechts im Titelbild, bis Bildebene und Inhalt dort stehen.</summary>
    private static (double Content, double Image) PointBottomRight(MainWindow window)
    {
        var hero = window.HeroStage;
        window.PointHero(new Point(hero.ActualWidth * 0.9, hero.ActualHeight * 0.9));
        Assert.True(PumpUntil(() => Content(window).X > 5.5 && hero.PointerShift.X < -11), $"Zeiger angekommen: Inhalt {Content(window).X:0.##}, Bild {hero.PointerShift.X:0.##}");
        return (Content(window).X, hero.PointerShift.X);
    }

    [Fact]
    public void Content_glides_back_together_with_the_image_layers_when_the_pointer_leaves_the_hero_and_continues_on_reentry()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _) = Open("Full");
            Assert.True(window.Motion!.ContinuousAllowed);
            var hero = window.HeroStage;
            var content = Content(window);
            Assert.Equal(TimeSpan.FromSeconds(0.8), MainWindow.PointerReturnDuration);
            var (x0, image0) = PointBottomRight(window);
            Thread.Sleep(40); // Drosselung (30 Hz) vorbei

            // Unter das Titelbild: Titel und Knöpfe springen nicht (vorher standen sie sofort in der Mitte), sie gleiten mit den
            // Bildebenen zurück.
            var started = DateTime.UtcNow;
            window.PointHero(new Point(hero.ActualWidth * 0.5, hero.ActualHeight + 40));
            Assert.Equal(x0, content.X, 0.5);
            Assert.Equal(image0, hero.PointerShift.X, 0.5);
            Assert.True(content.HasAnimatedProperties && hero.PointerShift.HasAnimatedProperties);

            // Beide auf demselben Weg: gleich weit zurück (gleiche Dauer und Kurve), solange sie gleiten.
            var samples = 0;
            Assert.True(PumpUntil(() =>
            {
                var a = content.X / x0;
                var b = hero.PointerShift.X / image0;
                if (a is > 0.02 and < 0.98 && b is > 0.02 and < 0.98)
                {
                    samples++;
                    Assert.True(Math.Abs(a - b) < 0.2, FormattableString.Invariant($"Inhalt bei {a:0.##}, Bildebene bei {b:0.##} des Wegs"));
                }

                return Math.Abs(content.X) < 0.01 && Math.Abs(hero.PointerShift.X) < 0.01 && Math.Abs(hero.BokehShift.Y) < 0.01;
            }), $"zurück in der Mitte: Inhalt {content.X:0.##}, Bild {hero.PointerShift.X:0.##}");
            Assert.True(samples > 0, "kein Bild, in dem beide noch gleiten");
            Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(0.4), "zu schnell in der Mitte: " + (DateTime.UtcNow - started));
            Assert.Equal(0, content.Y, 2);

            // Wieder hinein, mitten im Zurückgleiten: Es geht vom aktuellen Wert aus weiter, ohne Sprung.
            (x0, _) = PointBottomRight(window);
            Thread.Sleep(40);
            window.PointHero(new Point(hero.ActualWidth * 0.5, -30)); // über dem Titelbild hinaus
            Assert.True(PumpUntil(() => content.X < x0 * 0.7, 2), "gleitet zurück");
            var mid = content.X;
            Thread.Sleep(40);
            window.PointHero(new Point(hero.ActualWidth * 0.1, hero.ActualHeight * 0.5));
            Assert.Equal(mid, content.X, 0.75);
            Assert.True(PumpUntil(() => content.X < -5.5), "folgt dem Zeiger nach links: " + content.X);

            // Den Fenster verlassen: ebenso gleitend.
            var left = content.X;
            window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert.Equal(left, content.X, 0.5);
            Assert.True(content.HasAnimatedProperties);
            Assert.True(PumpUntil(() => Math.Abs(content.X) < 0.01 && Math.Abs(hero.PointerShift.X) < 0.01));
            window.Close();
        });
    }

    [Fact]
    public void Without_continuous_effects_everything_stands_in_the_middle_at_once_also_mid_glide()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _) = Open("Full");
            var hero = window.HeroStage;
            var content = Content(window);
            PointBottomRight(window);
            Thread.Sleep(40);

            // Mitten im Zurückgleiten minimiert (Dauer-Effekte aus): sofort in der Mitte, keine Animation mehr.
            window.PointHero(new Point(hero.ActualWidth * 0.5, hero.ActualHeight + 40));
            Assert.True(content.HasAnimatedProperties);
            window.Motion!.SetWindowState(visible: true, minimized: true, active: false);
            Assert.False(window.Motion.ContinuousAllowed);
            Assert.Equal(0, content.X);
            Assert.Equal(0, content.Y);
            Assert.Equal(0, hero.PointerShift.X);
            Assert.Equal(0, hero.BokehShift.Y);
            Assert.False(content.HasAnimatedProperties);
            Assert.False(hero.PointerShift.HasAnimatedProperties);

            // Ohne Dauer-Effekte folgt nichts dem Zeiger, und das Verlassen ändert nichts.
            window.PointHero(new Point(hero.ActualWidth * 0.9, hero.ActualHeight * 0.9));
            window.ResetPointer();
            Assert.Equal(0, content.X);
            Assert.False(content.HasAnimatedProperties);
            window.Close();

            // Stufe „Reduziert“: keine Tiefenwirkung.
            (window, _) = Open("Reduced");
            Assert.False(window.Motion!.ContinuousAllowed);
            window.PointHero(new Point(window.HeroStage.ActualWidth * 0.9, window.HeroStage.ActualHeight * 0.9));
            Assert.Equal(0, Content(window).X);
            Assert.False(window.HeroStage.PointerShift.HasAnimatedProperties);
            window.Close();
        });
    }
}
