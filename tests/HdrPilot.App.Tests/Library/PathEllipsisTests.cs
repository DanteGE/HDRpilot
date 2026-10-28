using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kürzen in der Mitte (Detailbereich: Startbefehl und Ordner der EXE-Dateien). Startbefehle ohne „\“ sind Adressen der
/// Launcher; früher blieb von ihnen nur „…“, weil die ganze Adresse als ein einziger erster Teil galt. Alle Adressen hier
/// sind erfunden.
/// </summary>
public sealed class PathEllipsisTests
{
    private const string Epic = "com.epicgames.launcher://apps/0a1b2c3d4e5f60718293a4b5c6d7e8f9%3Af9e8d7c6b5a4938271605f4e3d2c1b0a%3AAetherReach?action=launch&silent=true";

    /// <summary>Wie eine Schrift mit fester Breite: 7 je Zeichen.</summary>
    private static double Measure(string s) => s.Length * 7.0;

    [Fact]
    public void Launcher_address_keeps_scheme_first_part_and_end_when_there_is_room()
    {
        var shortened = PathEllipsis.Shorten(Epic, 560, Measure);

        Assert.True(Measure(shortened) <= 560, shortened);
        Assert.StartsWith("com.epicgames.launcher://apps/…", shortened, StringComparison.Ordinal);
        Assert.EndsWith("AetherReach?action=launch&silent=true", shortened, StringComparison.Ordinal);
        Assert.True(shortened.Length >= 79, shortened); // nutzt den Platz: höchstens ein Zeichen frei
    }

    [Fact]
    public void Narrow_launcher_address_keeps_its_end_instead_of_only_the_ellipsis()
    {
        // 300 = 42 Zeichen: „com.epicgames.launcher://apps/…“ bräuchte mehr als die Hälfte, das Ende hat Vorrang.
        var shortened = PathEllipsis.Shorten(Epic, 300, Measure);

        Assert.True(Measure(shortened) <= 300, shortened);
        Assert.StartsWith("…", shortened, StringComparison.Ordinal);
        Assert.EndsWith("AetherReach?action=launch&silent=true", shortened, StringComparison.Ordinal);
        Assert.Equal(42, shortened.Length);
    }

    [Fact]
    public void Whole_parts_of_an_address_go_first_like_folders()
    {
        Assert.Equal("uplay://launch/…/0", PathEllipsis.Shorten("uplay://launch/9990101/0", 18 * 7, Measure));

        const string ea = "origin2://game/launch/?offerIds=Origin.OFR.50.0009990&authCode=&cmdParams=";
        var shortened = PathEllipsis.Shorten(ea, 60 * 7, Measure);
        Assert.True(Measure(shortened) <= 60 * 7, shortened);
        Assert.StartsWith("origin2://game/…", shortened, StringComparison.Ordinal);
        Assert.EndsWith("&authCode=&cmdParams=", shortened, StringComparison.Ordinal);
        Assert.DoesNotContain("launch", shortened, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Epic)]
    [InlineData("battlenet://AetherReachPruefspielMitLangemNamen")]
    [InlineData("uplay://launch/9990101/0")]
    [InlineData("steam://rungameid/9990101")]
    [InlineData("Startbefehl-ohne-Trenner-und-recht-lang-0123456789")]
    [InlineData(@"D:\Programmordner\Spiele\Aether Reach\Binaries\Win64\AetherReach-Win64-Shipping.exe")]
    public void Every_width_shows_more_than_the_ellipsis_and_fits(string command)
    {
        for (var width = 70.0; width <= 900; width += 7)
        {
            var shortened = PathEllipsis.Shorten(command, width, Measure);
            Assert.True(Measure(shortened) <= width, $"{width}: {shortened}");
            Assert.True(shortened.Replace(PathEllipsis.Ellipsis, string.Empty, StringComparison.Ordinal).Length >= 4, $"{width}: {shortened}");

            // Das Ende bleibt lesbar: Was nach der Auslassung steht, ist das Ende des Befehls (mindestens „/0“).
            var end = shortened[(shortened.LastIndexOf(PathEllipsis.Ellipsis, StringComparison.Ordinal) + 1)..];
            Assert.True(end.Length >= 2 && command.EndsWith(end, StringComparison.Ordinal), $"{width}: {shortened}");
        }
    }

    [Fact]
    public void Text_without_separators_keeps_its_end()
    {
        Assert.Equal("…cht-lang-0123456789", PathEllipsis.Shorten("Startbefehl-ohne-Trenner-und-recht-lang-0123456789", 20 * 7, Measure));
    }

    [Fact]
    public void Paths_keep_their_behaviour_drive_and_end_then_only_the_end()
    {
        const string path = @"D:\Programmordner\Spiele\Aether Reach\Binaries\Win64\AetherReach.exe";
        Assert.Equal(@"D:\…\Win64\AetherReach.exe", PathEllipsis.Shorten(path, 26 * 7, Measure));
        Assert.Equal(@"D:\…\AetherReach.exe", PathEllipsis.Shorten(path, 22 * 7, Measure));

        // Passt nicht einmal „D:\…\Datei“: wie bisher nur das Ende, ohne Laufwerk.
        Assert.Equal("…ach.exe", PathEllipsis.Shorten(path, 8 * 7, Measure));
    }

    [Fact]
    public void Launch_command_line_in_the_details_shows_the_end_of_an_address_in_its_column()
    {
        WpfTestHost.Run(() =>
        {
            // Wie die Zeile „Startbefehl“ im Detailbereich: Symbol | Befehl in Cascadia Mono 11.
            var text = new MiddleEllipsisText { FullText = Epic, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var row = new DockPanel { Width = 300 };
            var glyph = new TextBlock { Text = "x", Width = 17 };
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            row.Children.Add(text);

            for (var i = 0; i < 3; i++)
            {
                WpfTestHost.Layout(row, new Size(300, 40));
                WpfTestHost.DoEvents();
            }

            var dpi = VisualTreeHelper.GetDpi(text).PixelsPerDip;
            var shown = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
            Assert.NotEqual(PathEllipsis.Ellipsis, text.Text);
            Assert.Contains(PathEllipsis.Ellipsis, text.Text, StringComparison.Ordinal);
            Assert.EndsWith("action=launch&silent=true", text.Text, StringComparison.Ordinal);
            Assert.True(shown <= text.ActualWidth + 0.5, $"{text.Text}: {shown:0.0} in {text.ActualWidth:0.0}");
            Assert.Equal(Epic, text.ToolTip);
        });
    }
}
