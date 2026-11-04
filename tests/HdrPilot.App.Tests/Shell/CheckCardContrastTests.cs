using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Tests.Theming;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Auswahl-Kästchen als Karte (Hp.Shell.CheckCard: „Auch Einstellungen, Spieleliste und Protokolle löschen“
/// beim Entfernen von HDRpilot, HDRpilot-Verknüpfungen in der Rückfrage „Einstellungen entfernen…“, ältere Version
/// installieren, Bildschirme): Der Rand des leeren Kästchens hält mindestens 3:1 zur Karte dahinter (WCAG 1.4.11), in Ruhe
/// und beim Überfahren, in Ruhe auch zur Fläche im Kästchen, für jede Vorlage in hell und dunkel. Prüfung 2026-11-04: in
/// LineStrong nur etwa 1,5:1. Gewählt deckt der Akzent das Kästchen, sein Rand entfällt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class CheckCardContrastTests
{
    public static TheoryData<string, bool> Palettes()
    {
        var data = new TheoryData<string, bool>();
        foreach (var preset in PalettePresets.All)
        {
            data.Add(preset.Id, true);
            data.Add(preset.Id, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Unticked_box_border_keeps_3_to_1_against_the_card_at_rest_and_on_hover(string preset, bool dark)
    {
        WpfTestHost.Run(() =>
        {
            var palette = PaletteEngine.Compute(PaletteEngineTests.Colors(preset, PalettePresets.Find(preset)!.DefaultMode), dark, autoTextOnAccent: true);
            var tokens = ThemeResources.Build(palette);
            var card = new CheckBox { Style = (Style)Application.Current.FindResource("Hp.Shell.CheckCard"), Content = "Auch Einstellungen, Spieleliste und Protokolle löschen" };
            card.Resources.MergedDictionaries.Add(tokens);
            WpfTestHost.Layout(card, new Size(480, 80));
            var box = (Border)card.Template.FindName("Box", card);
            var body = (Border)card.Template.FindName("Body", card);
            Color Token(string key) => ((SolidColorBrush)tokens[key]).Color;

            var rest = ((SolidColorBrush)box.BorderBrush).Color;
            var hover = Token(HoverKey(card));
            Assert.Equal(Token(ThemeKeys.BrushSurface1), ((SolidColorBrush)body.Background).Color);
            Assert.Equal(Token(ThemeKeys.BrushSurface2), ((SolidColorBrush)box.Background).Color);

            // Hinter der Karte: Fenstergrund (Mitte, oben, unten) oder die Kartenfläche der Einstellungen.
            var what = $"{preset} {(dark ? "dunkel" : "hell")}";
            foreach (var ground in new[] { palette.Background, palette.BackgroundTop, palette.BackgroundBottom, palette.PanelReference })
            {
                var surface = Over(Token(ThemeKeys.BrushSurface1), ground);
                var fill = Over(Token(ThemeKeys.BrushSurface2), surface);
                var hovered = Over(Token(ThemeKeys.BrushSurface2), ground);
                AssertContrast(Over(rest, surface), surface, $"{what}: Rand zur Karte auf {ColorMath.ToHex(ground)}");
                AssertContrast(Over(rest, surface), fill, $"{what}: Rand zur Fläche im Kästchen auf {ColorMath.ToHex(ground)}");
                AssertContrast(Over(hover, hovered), hovered, $"{what}: Rand beim Überfahren auf {ColorMath.ToHex(ground)}");

                // Gegenprobe: der bisherige Rand (LineStrong) läge darunter.
                Assert.True(ColorMath.Contrast(Over(Token(ThemeKeys.BrushLineStrong), surface), surface) < 3, what + ": LineStrong erreichte 3:1");
            }

            // Gewählt: Akzent über dem ganzen Kästchen, kein Rand darunter (sonst ein grauer Saum an den Rundungen).
            card.IsChecked = true;
            WpfTestHost.Layout(card, new Size(480, 80));
            Assert.Equal(1.0, ((UIElement)card.Template.FindName("Fill", card)).Opacity);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)box.BorderBrush).Color);
        });
    }

    /// <summary>Schlüssel des Rands beim Überfahren (Setter der Vorlage; die Maus lässt sich im Test nicht darüber legen).</summary>
    private static string HoverKey(CheckBox card)
    {
        var trigger = card.Template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
        var setter = trigger.Setters.OfType<Setter>().Single(s => s.TargetName == "Box" && s.Property == Border.BorderBrushProperty);
        return (string)Assert.IsType<DynamicResourceExtension>(setter.Value).ResourceKey;
    }

    private static void AssertContrast(Color a, Color b, string what)
    {
        var ratio = ColorMath.Contrast(a, b);
        Assert.True(ratio >= 3.0, FormattableString.Invariant($"{what}: {ratio:0.00}:1 ({ColorMath.ToHex(a)} zu {ColorMath.ToHex(b)})"));
    }

    /// <summary>Farbe <paramref name="top"/> (mit Deckkraft) über der deckenden Farbe <paramref name="under"/>.</summary>
    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Channel(byte t, byte u) => (byte)Math.Round((t * a) + (u * (1 - a)));
        return Color.FromRgb(Channel(top.R, under.R), Channel(top.G, under.G), Channel(top.B, under.B));
    }
}
