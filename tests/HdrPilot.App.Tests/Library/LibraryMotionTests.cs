using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Einblenden der Bibliothek (<see cref="LibraryMotion.FadeIn"/>, E89): Nach dem Ende hängt keine Animation mehr an Element
/// und Verschiebung, die vorübergehende Verschiebung ist wieder weg, eine eigene bleibt. Eine zweite Einblendung, die vor dem
/// Ende der ersten beginnt (schneller Spielwechsel), läuft zu Ende, auch wenn die erste inzwischen endet. Auf dem WPF-Testhost
/// in einem nie gezeigten Fenster, mit echten Uhren. Allein laufend: Die Effektstufe gilt für den ganzen Prozess, und die
/// Messung geht über die Zeit.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class LibraryMotionTests(ITestOutputHelper output)
{
    [Fact]
    public void A_finished_fade_in_leaves_nothing_running_and_restores_the_transform()
    {
        WithFullMotion(() =>
        {
            var (window, element) = Host();
            var own = new TranslateTransform(0, 5);
            var drawer = new Border { Width = 40, Height = 40, RenderTransform = own };
            ((Grid)window.Content).Children.Add(drawer);

            LibraryMotion.FadeIn(element, TimeSpan.Zero, offsetY: 14, durationMs: 150);
            LibraryMotion.FadeIn(drawer, TimeSpan.Zero, offsetX: 18, durationMs: 150);
            var temporary = Assert.IsType<TranslateTransform>(element.RenderTransform);
            Assert.True(element.HasAnimatedProperties);

            Pump(window, () => !element.HasAnimatedProperties && !temporary.HasAnimatedProperties && !drawer.HasAnimatedProperties && !own.HasAnimatedProperties);

            Assert.False(element.HasAnimatedProperties);
            Assert.False(temporary.HasAnimatedProperties);
            Assert.Equal(1, element.Opacity);
            Assert.Equal(DependencyProperty.UnsetValue, element.ReadLocalValue(UIElement.RenderTransformProperty)); // vorübergehende weg
            Assert.False(drawer.HasAnimatedProperties);
            Assert.False(own.HasAnimatedProperties);
            Assert.Same(own, drawer.RenderTransform); // eigene Verschiebung bleibt, mit ihrem Wert
            Assert.Equal(5, own.Y);
            Assert.Equal(0, own.X);
            window.Close();
        });
    }

    [Fact]
    public void A_second_fade_in_before_the_first_ends_runs_to_its_own_end()
    {
        WithFullMotion(() =>
        {
            // Mit echten Uhren entscheidet der Versuch nur, wenn die zweite Einblendung beginnt, solange die erste (400 ms)
            // sicher noch läuft, und der Messpunkt nach deren Ende, aber vor dem Ende der zweiten (900 ms ab ihrem Beginn)
            // liegt. Stand der Testhost-Thread länger (Speicherbereinigung, ausgelastete Maschine im vollen Lauf), trifft das
            // nicht zu, ohne dass die Einblendung etwas falsch macht: Dann wird mit neuem Fenster wiederholt, höchstens dreimal;
            // der letzte Versuch prüft immer.
            for (var attempt = 1; ; attempt++)
            {
                var (window, element) = Host();
                var clock = Stopwatch.StartNew();
                LibraryMotion.FadeIn(element, TimeSpan.Zero, offsetY: 14, durationMs: 400);
                var shift = Assert.IsType<TranslateTransform>(element.RenderTransform);
                Pump(window, () => clock.ElapsedMilliseconds >= 150);

                var second = clock.ElapsedMilliseconds;
                LibraryMotion.FadeIn(element, TimeSpan.Zero, offsetY: 14, durationMs: 900);
                var afterSecond = element.RenderTransform;

                // Die erste endet bei etwa 400 ms; danach muss die zweite noch laufen.
                Pump(window, () => clock.ElapsedMilliseconds >= 600);
                var at = clock.ElapsedMilliseconds;
                output.WriteLine($"Versuch {attempt}: zweite Einblendung bei {second} ms, Messpunkt bei {at} ms");
                if ((second >= 350 || at - second >= 800) && attempt < 3)
                {
                    window.Close();
                    continue; // nichts entschieden, weil der Thread stand: noch einmal
                }

                Assert.True(second < 350, $"Zweite Einblendung zu spät ({second} ms), die erste war schon fast vorbei; der Testlauf war zu langsam");
                Assert.Same(shift, afterSecond);
                Assert.True(at - second < 800, $"Messpunkt zu spät ({at} ms, zweite Einblendung bei {second} ms), der Testlauf war zu langsam");
                Assert.True(element.HasAnimatedProperties, $"Deckkraft läuft nicht mehr ({at} ms)");
                Assert.True(element.Opacity < 0.99, $"Deckkraft schon {element.Opacity} ({at} ms)");
                Assert.Same(shift, element.RenderTransform);
                Assert.True(shift.HasAnimatedProperties, $"Verschiebung läuft nicht mehr ({at} ms)");
                Assert.True(shift.Y > 0.5, $"Verschiebung schon {shift.Y} ({at} ms)");

                Pump(window, () => !element.HasAnimatedProperties && !shift.HasAnimatedProperties && element.ReadLocalValue(UIElement.RenderTransformProperty) == DependencyProperty.UnsetValue);
                Assert.False(element.HasAnimatedProperties);
                Assert.False(shift.HasAnimatedProperties);
                Assert.Equal(1, element.Opacity);
                Assert.Equal(DependencyProperty.UnsetValue, element.ReadLocalValue(UIElement.RenderTransformProperty));
                window.Close();
                break;
            }
        });
    }

    [Fact]
    public void Effect_level_off_starts_nothing()
    {
        WpfTestHost.Run(() =>
        {
            var previous = LibraryMotion.Instance.Level;
            try
            {
                LibraryMotion.Instance.SetLevel(EffectLevel.Off);
                var (window, element) = Host();
                LibraryMotion.FadeIn(element, TimeSpan.Zero);
                Assert.False(element.HasAnimatedProperties);
                Assert.Equal(DependencyProperty.UnsetValue, element.ReadLocalValue(UIElement.RenderTransformProperty));
                window.Close();
            }
            finally
            {
                LibraryMotion.Instance.SetLevel(previous);
            }
        });
    }

    /// <summary>Stufe „Voll“; ohne Hardware-Darstellung (keine Animationen) nur die Prüfung, dass nichts startet.</summary>
    private static void WithFullMotion(Action test)
    {
        WpfTestHost.Run(() =>
        {
            var previous = LibraryMotion.Instance.Level;
            try
            {
                LibraryMotion.Instance.SetLevel(EffectLevel.Full);
                if (!LibraryMotion.Enabled)
                {
                    var (window, element) = Host();
                    LibraryMotion.FadeIn(element, TimeSpan.Zero);
                    Assert.False(element.HasAnimatedProperties);
                    window.Close();
                    return;
                }

                test();
            }
            finally
            {
                LibraryMotion.Instance.SetLevel(previous);
            }
        });
    }

    /// <summary>Element in einem nie gezeigten, angeordneten Fenster (so laufen die Uhren auf dem Testhost).</summary>
    private static (Window Window, Border Element) Host()
    {
        var element = new Border { Width = 40, Height = 40 };
        var window = new Window { Content = new Grid { Children = { element } } };
        WpfTestHost.Layout(window, new Size(200, 200));
        return (window, element);
    }

    /// <summary>Pumpt den Dispatcher (Uhren, Abschluss), bis <paramref name="done"/> gilt, höchstens 3 s.</summary>
    private static void Pump(Window window, Func<bool> done)
    {
        var until = Stopwatch.StartNew();
        while (!done() && until.ElapsedMilliseconds < 3000)
        {
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(200, 200));
            Thread.Sleep(15);
        }
    }
}
