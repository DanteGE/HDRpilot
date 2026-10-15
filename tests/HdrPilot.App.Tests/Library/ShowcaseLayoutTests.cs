using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Anordnung „Showcase Hero“ als reine Rechnungen (E89, G10.5): Raster mit gleich großen Kacheln über die ganze Breite
/// (auch 3440 px), Höhen des Titelbereichs je Ansicht, Spalten der Liste, Reihenfolge und Speicherung der Ansichten,
/// relative Zeitangaben.
/// </summary>
public sealed class ShowcaseLayoutTests
{
    /// <summary>Breite des Rasters bei einer Fensterbreite: Ränder 2 × 32, Trenner 24, Detailbereich 23 % (340–396), Innenabstand 8.</summary>
    private static double GridWidth(double window) => window - 64 - 24 - ShowcaseLayout.PaneWidth(window) - 8;

    [Theory]
    [InlineData(1000)]
    [InlineData(1280)]
    [InlineData(1366)]
    [InlineData(1600)]
    [InlineData(1920)]
    [InlineData(2560)]
    [InlineData(3440)]
    [InlineData(5120)]
    public void Raster_layout_fills_the_width_with_uniform_tiles_at_every_window_size(double window)
    {
        var width = GridWidth(window);
        var m = RasterLayout.Compute(width);

        Assert.InRange(m.TileWidth, RasterLayout.DefaultMinTile, RasterLayout.DefaultMaxTile);
        Assert.Equal(m.TileWidth * 1.5, m.TileHeight); // 2:3, ohne Rundung
        Assert.Equal(0, m.TileWidth % 2); // gerade ganze Pixel
        Assert.True(m.UsedWidth <= width, $"{window}: {m.UsedWidth} > {width}");

        // Keine Lücke rechts: übrig bleibt nur das Abrunden (höchstens 2 px je Spalte), nie Platz für eine weitere Kachel.
        Assert.True(width - m.UsedWidth < 2 * m.Columns, $"{window}: Rest {width - m.UsedWidth:0.0} bei {m.Columns} Spalten");
    }

    [Fact]
    public void Raster_layout_uses_more_columns_on_wider_windows_instead_of_bigger_tiles()
    {
        var at1600 = RasterLayout.Compute(GridWidth(1600));
        var at3440 = RasterLayout.Compute(GridWidth(3440));

        Assert.True(at3440.Columns >= 2 * at1600.Columns, $"{at1600.Columns} → {at3440.Columns}");
        Assert.InRange(at3440.TileWidth, 150, 220);
        Assert.Equal(17, at3440.Columns); // 3440 × 1440: 17 Kacheln je Reihe
    }

    [Fact]
    public void Raster_layout_holds_for_random_widths()
    {
        var random = new Random(4711);
        for (var i = 0; i < 2000; i++)
        {
            var width = 482 + (random.NextDouble() * 6000);
            var m = RasterLayout.Compute(width);
            Assert.InRange(m.TileWidth, 148, 220); // ganz knapp über der Mindestbreite rundet es auf gerade Pixel ab
            Assert.True(m.UsedWidth <= width + 1e-9);
            Assert.True(width - m.UsedWidth < 2 * m.Columns + 1e-9, $"{width:0.0}: Rest {width - m.UsedWidth:0.0}");
            Assert.True(m.Columns >= 3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(200)]
    public void Raster_layout_never_overflows_narrow_or_invalid_widths(double width)
    {
        var m = RasterLayout.Compute(width);

        Assert.True(m.Columns >= 2);
        Assert.True(m.TileWidth >= 0);
        Assert.True(m.UsedWidth <= Math.Max(0, double.IsFinite(width) ? width : 0) + ((m.Columns - 1) * m.Gap) + 1e-9 || m.TileWidth == 0);
        Assert.Equal(0, m.Rows(0));
        Assert.Equal(1, m.Rows(1));
    }

    [Theory]
    [InlineData(1000, 220)]
    [InlineData(800, 180)]
    [InlineData(1440, 240)]
    public void Showcase_list_hero_is_slim_and_collapses_to_120(double height, double expected)
    {
        Assert.Equal(expected, ShowcaseLayout.HeroHeight(LibraryViewMode.List, height, listCollapsed: false));
        Assert.Equal(ShowcaseLayout.CollapsedListHero, ShowcaseLayout.HeroHeight(LibraryViewMode.List, height, listCollapsed: true));
    }

    [Fact]
    public void Showcase_shelf_hero_is_big_and_grid_hero_medium()
    {
        Assert.Equal(620, ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 1000, false));
        Assert.Equal(500, ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 620, false));
        Assert.Equal(240, ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, 1000, false));
        Assert.Equal(196, ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, 700, false));

        // 3440 × 1440: das Regal behält unten Platz für die Kachelreihe (Abweichung vom Prototyp: höchstens H − 462).
        var shelf = ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 1400, false);
        Assert.True(1400 - shelf >= 462, "Platz für die Reihe: " + (1400 - shelf));
    }

    [Theory]
    [InlineData(1142, true, true, true)]
    [InlineData(820, true, true, true)]
    [InlineData(819, false, true, true)]
    [InlineData(682, false, true, true)]
    [InlineData(681, false, false, true)]
    [InlineData(572, false, false, true)]
    [InlineData(571, false, false, false)]
    public void Showcase_list_columns_drop_source_then_hdr_then_last_played(double listWidth, bool source, bool hdr, bool last)
    {
        var columns = ShowcaseLayout.ListColumnsFor(listWidth);

        Assert.Equal(new ListColumns(source, last, hdr), columns);
    }

    [Fact]
    public void Showcase_pane_and_shelf_follow_the_window()
    {
        Assert.Equal(340, ShowcaseLayout.PaneWidth(1000));
        Assert.Equal(368, ShowcaseLayout.PaneWidth(1600));
        Assert.Equal(396, ShowcaseLayout.PaneWidth(3440));

        var shelf = ShowcaseLayout.Shelf(3440, 300);
        Assert.Equal(266, shelf.TileHeight);
        Assert.Equal(177, shelf.TileWidth);
        Assert.True(shelf.Visible >= 15, "Kacheln im Regal: " + shelf.Visible);
    }

    [Fact]
    public void View_modes_are_ordered_list_grid_shelf_and_stored_robustly()
    {
        Assert.Equal([LibraryViewMode.List, LibraryViewMode.Grid, LibraryViewMode.Shelf], LibraryViewModes.Ordered);
        Assert.Equal(LibraryViewMode.List, LibraryViewModes.Parse(null));
        Assert.Equal(LibraryViewMode.Grid, LibraryViewModes.Parse("grid"));
        Assert.Equal(LibraryViewMode.Shelf, LibraryViewModes.Parse(" Shelf "));
        Assert.Equal(LibraryViewMode.List, LibraryViewModes.Parse("Wand"));
        Assert.Equal(LibraryViewMode.List, LibraryViewModes.Parse("2")); // keine Zahlen als Ansicht
        Assert.Null(LibraryViewModes.ToConfig(LibraryViewMode.List)); // Standard wird nicht gespeichert
        Assert.Equal("Shelf", LibraryViewModes.ToConfig(LibraryViewMode.Shelf));
        Assert.Equal(LibraryThumbnail.Icon, LibraryViewModes.ParseThumbnail("icon"));
        Assert.Equal(LibraryThumbnail.Image, LibraryViewModes.ParseThumbnail("Bild"));
        Assert.Null(LibraryViewModes.ToConfig(LibraryThumbnail.Image));
    }

    [Fact]
    public void Relative_time_reads_like_the_prototype()
    {
        var now = new DateTimeOffset(new DateTime(2026, 10, 16, 20, 0, 0, DateTimeKind.Local));
        DateTimeOffset At(int daysAgo, int hour = 21, int minute = 15) =>
            new(new DateTime(2026, 10, 16, hour, minute, 0, DateTimeKind.Local).AddDays(-daysAgo));

        Assert.Equal(LibraryStrings.When_Never, RelativeTime.Format(null, now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Today, At(0, 18, 5).ToLocalTime()), RelativeTime.Format(At(0, 18, 5), now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Yesterday, At(1).ToLocalTime()), RelativeTime.Format(At(1), now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Days, 3), RelativeTime.Format(At(3), now));
        Assert.Equal(LibraryStrings.When_OneWeek, RelativeTime.Format(At(7), now));
        Assert.Equal(LibraryStrings.When_OneWeek, RelativeTime.Format(At(10), now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Weeks, 2), RelativeTime.Format(At(11), now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Weeks, 5), RelativeTime.Format(At(34), now));
        Assert.Equal(LibraryText.Format(LibraryStrings.When_Date, At(35).ToLocalTime()), RelativeTime.Format(At(35), now));
        Assert.Contains("2026", RelativeTime.Format(At(35), now), StringComparison.Ordinal);

        // Im Titelbereich in Großbuchstaben (HeroViewModel), hier die Bausteine.
        Assert.Equal(LibraryStrings.Hero_OverlineNeverPlayed, RelativeTime.Overline(false, null, null, now));
        Assert.Equal(LibraryText.Format(LibraryStrings.Hero_OverlineNew, LibraryText.Format(LibraryStrings.When_Days, 3)), RelativeTime.Overline(true, At(3), null, now));
    }

    [Theory]
    [InlineData("Baldur's Gate 3", "BG")]
    [InlineData("SWAT 3", "S3")]
    [InlineData("Hades", "HA")]
    [InlineData("Half-Life: Alyx", "HL")] // Bindestrich trennt Wörter wie im Prototyp
    public void Monogram_takes_two_letters_like_the_prototype(string name, string expected) =>
        Assert.Equal(expected, LibraryText.Monogram(name));

    [Fact]
    public void Path_ellipsis_keeps_drive_and_end_of_long_paths()
    {
        const string path = @"D:\Steam-Bibliothek\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";
        double Measure(string s) => s.Length * 7.0;

        var shortened = PathEllipsis.Shorten(path, 300, Measure);

        Assert.True(Measure(shortened) <= 300, shortened);
        Assert.StartsWith(@"D:\", shortened, StringComparison.Ordinal);
        Assert.EndsWith("Cyberpunk2077.exe", shortened, StringComparison.Ordinal);
        Assert.Contains("…", shortened, StringComparison.Ordinal);
        Assert.Equal(path, PathEllipsis.Shorten(path, 10000, Measure));
    }
}
