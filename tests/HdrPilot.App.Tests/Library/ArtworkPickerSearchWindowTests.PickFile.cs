using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Shell;
using HdrPilot.Core.Artwork;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>Kachel „Eigene Datei…“: beide Zeilen ganz lesbar, auch in der schmalen Cover-Kachel (130 DIP) und auf Englisch.</summary>
public sealed partial class ArtworkPickerSearchWindowTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void The_custom_file_tile_shows_both_lines_untrimmed_with_every_format_in_every_kind(string culture)
    {
        WpfTestHost.Run(() =>
        {
            using var language = TestCulture.Use(culture);
            foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover })
            {
                var (dialog, picker, _) = Open(kind);
                Load(picker);
                Arrange(dialog, double.PositiveInfinity);

                var hint = Find<TextBlock>(dialog, "PickFileHint");
                Assert.Equal(LibraryStrings.Picker_PickFileHint, hint.Text);
                foreach (var format in new[] { "PNG", "JPG", "WebP", "BMP" })
                {
                    Assert.Contains(format, hint.Text, StringComparison.Ordinal);
                }

                // Beide Zeilen der Kachel ohne Auslassung: Der Text braucht höchstens die Breite, die er bekommt.
                var lines = Descendants(Find<Button>(dialog, "PickFileButton")).OfType<TextBlock>().Where(t => t.TextTrimming != TextTrimming.None).ToList();
                Assert.Equal(2, lines.Count);
                Assert.Contains(hint, lines);
                foreach (var line in lines)
                {
                    var dpi = VisualTreeHelper.GetDpi(line).PixelsPerDip;
                    var needed = new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface(line.FontFamily, line.FontStyle, line.FontWeight, line.FontStretch), line.FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
                    Assert.True(needed <= line.ActualWidth + 0.01, FormattableString.Invariant($"{culture} {kind}: „{line.Text}“ braucht {needed:0.0} DIP, hat {line.ActualWidth:0.0}"));
                }

                dialog.Close();
            }
        });
    }
}
