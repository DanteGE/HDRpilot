using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Hilfen des WPF-Testhosts: Eine gestellte Bildschirmskalierung (<see cref="WpfTestHost.SetRootDpi"/>) gilt nur für ihr
/// Fenster; später erzeugte, nie gezeigte Fenster und Elemente behalten die Skalierung, die ein neues Element vorher hatte.
/// WPF führt die Skalierungen in einer Tabelle für den ganzen Prozess. Ohne die Vorbelegung im Testhost scheitert der Test,
/// wenn noch kein Fenster gezeigt und keine Skalierung gestellt wurde (etwa allein gestartet); danach ist die Tabelle belegt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class WpfTestHostTests
{
    [Fact]
    public void A_root_dpi_set_on_one_window_leaves_later_windows_at_the_standard_dpi()
    {
        WpfTestHost.Run(() =>
        {
            var standard = VisualTreeHelper.GetDpi(new Border());
            foreach (var step in new[] { 0.5, 0.75 })
            {
                var scale = standard.DpiScaleX + step;
                var (window, content) = Host();
                WpfTestHost.SetRootDpi(window, new DpiScale(scale, scale));
                WpfTestHost.Layout(window, new Size(200, 200));
                Assert.Equal(scale, VisualTreeHelper.GetDpi(window).DpiScaleX);
                Assert.Equal(scale, VisualTreeHelper.GetDpi(content).DpiScaleX);
                window.Close();

                var (later, laterContent) = Host();
                Assert.Equal(standard, VisualTreeHelper.GetDpi(later));
                Assert.Equal(standard, VisualTreeHelper.GetDpi(laterContent));
                Assert.Equal(standard, VisualTreeHelper.GetDpi(new Border()));
                later.Close();
            }
        });
    }

    /// <summary>Nie gezeigtes, angeordnetes Fenster mit einem Element.</summary>
    private static (Window Window, Border Content) Host()
    {
        var content = new Border { Width = 40, Height = 40 };
        var window = new Window { Content = content };
        WpfTestHost.Layout(window, new Size(200, 200));
        return (window, content);
    }
}
