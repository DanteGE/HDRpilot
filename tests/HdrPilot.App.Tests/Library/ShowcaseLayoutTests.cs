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
    [InlineData(460)]
    [InlineData(470)]
    [InlineData(481)]
    public void Raster_layout_never_stretches_tiles_beyond_the_maximum_on_narrow_widths(double width)
    {
        // Zwei Spalten wären hier über 220 px breit: lieber eine dritte, etwas schmalere Spalte.
        var m = RasterLayout.Compute(width);

        Assert.Equal(3, m.Columns);
        Assert.True(m.TileWidth <= RasterLayout.DefaultMaxTile, "Kachel " + m.TileWidth);
        Assert.True(m.UsedWidth <= width);
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
    [InlineData(800, 267)]
    [InlineData(1080, 360)]
    [InlineData(1440, 480)]
    [InlineData(727, 242)] // 1280×800 bei 110 %
    [InlineData(982, 327)] // 1920×1080 bei 110 %
    [InlineData(1309, 436)] // 3440×1440 bei 110 %
    [InlineData(620, 204)] // kleinste Fensterhöhe: durch fünf ganze Zeilen begrenzt (620 − 416)
    public void Showcase_list_hero_defaults_to_a_third_of_the_window_height(double height, double expected)
    {
        // Wunsch des Nutzers: Standard etwa ein Drittel (vorher clamp(H·0,22; 180; 240), bei 1440 px nur 240 px).
        Assert.Equal(expected, ShowcaseLayout.HeroHeight(LibraryViewMode.List, height));
        Assert.Equal(expected, ShowcaseLayout.HeroHeight(LibraryViewMode.List, height, listRatio: null));
        Assert.Equal(expected, ShowcaseLayout.ListHeroHeight(height, null));
        Assert.InRange(expected / height, 0.32, 0.345);
    }

    [Theory]
    [InlineData(620)]
    [InlineData(727)]
    [InlineData(800)]
    [InlineData(982)]
    [InlineData(1080)]
    [InlineData(1309)]
    [InlineData(1440)]
    [InlineData(2160)]
    public void Showcase_list_hero_range_keeps_a_readable_hero_and_five_list_rows(double height)
    {
        var (min, max) = ShowcaseLayout.ListHeroRange(height);

        // Kleinster Titelbereich: Unter dem Kopf (98 px) bleiben über dem unteren Innenrand 72 px, so hoch wie die
        // Symbol-Kachel eines Spiels ohne Logo (mit 180 px waren es 66, die Kachel war unten abgeschnitten); Logo, Angaben
        // und der 50 px hohe Knopf brauchen weniger.
        Assert.Equal(ShowcaseLayout.MinListHero, min);
        Assert.True(min - 98 - 16 >= 72, "Inhalt des Titelbereichs " + (min - 98 - 16));

        // Größter: Ziehgriff, Werkzeugleiste (auch zweizeilig), Kopf der Liste, fünf ganze Zeilen und die Statuszeile
        // bleiben (H − 416).
        Assert.Equal(height - 416, max);
        var listTop = ShowcaseLayout.ToolbarTop(LibraryViewMode.List, max) + ShowcaseLayout.StackedToolbarHeight + 10;
        var rows = height - ShowcaseLayout.PaneBottom - listTop - ShowcaseLayout.ListHeaderHeight;
        Assert.True(rows >= ShowcaseLayout.MinListRows * ShowcaseLayout.ListRowHeight, $"H {height}: {rows} px für Zeilen");

        // Gemerkte Anteile gelten nur innerhalb des Bereichs.
        Assert.Equal(max, ShowcaseLayout.ListHeroHeight(height, 0.99));
        Assert.Equal(min, ShowcaseLayout.ListHeroHeight(height, 0.01));
        Assert.InRange(ShowcaseLayout.ListHeroHeight(height, null), min, max);
    }

    [Theory]
    [InlineData(727, 60)] // Fehler beim Einlesen (zugeklappt)
    [InlineData(982, 128)] // Fehler und Vorschläge
    [InlineData(982, 372)] // beide aufgeklappt
    [InlineData(1309, 250)]
    public void Showcase_list_hero_range_keeps_five_rows_below_the_notices(double height, double notices)
    {
        // Prüfung 2026-10-16: Die Hinweise liegen zwischen Werkzeugleiste und Liste; ohne sie in der Rechnung blieben beim
        // größten Titelbereich mit Hinweisen nur etwa drei ganze Zeilen, aufgeklappt keine.
        var (min, max) = ShowcaseLayout.ListHeroRange(height, notices);
        Assert.Equal(ShowcaseLayout.MinListHero, min);
        Assert.Equal(Math.Max(min, height - 416 - notices), max);
        var listTop = ShowcaseLayout.ToolbarTop(LibraryViewMode.List, max) + ShowcaseLayout.StackedToolbarHeight + notices + 10;
        var rows = height - ShowcaseLayout.PaneBottom - listTop - ShowcaseLayout.ListHeaderHeight;
        Assert.True(max == min || rows >= ShowcaseLayout.MinListRows * ShowcaseLayout.ListRowHeight, $"H {height}: {rows} px für Zeilen");

        // Der gemerkte Anteil bleibt, die Höhe wird nur begrenzt; ohne Hinweise gilt wieder die gewählte Höhe.
        Assert.Equal(max, ShowcaseLayout.ListHeroHeight(height, 0.99, notices));
        Assert.Equal(max, ShowcaseLayout.HeroHeight(LibraryViewMode.List, height, 0.99, notices));
        Assert.Equal(height - 416, ShowcaseLayout.ListHeroHeight(height, 0.99));

        // Unsinnige Höhen der Hinweise zählen nicht.
        Assert.Equal(ShowcaseLayout.ListHeroRange(height), ShowcaseLayout.ListHeroRange(height, double.NaN));
        Assert.Equal(ShowcaseLayout.ListHeroRange(height), ShowcaseLayout.ListHeroRange(height, -40));
    }

    [Theory]
    [InlineData(618, 88 + 255, 191)] // 1000×620 (Rahmen 2 px): zweizeilige Leiste, Kachel 170 × 255
    [InlineData(618, 88 + 252, 194)] // 1178×620 (Startgröße auf 1920×1080 bei 150 %): Kachel 168 × 252
    [InlineData(618, 40 + 264, 196)] // 1400×620: einzeilige Leiste, die Reihe passt ohnehin
    [InlineData(725, 88 + 246, 196)] // 1164×727 (1280×800 bei 110 %)
    [InlineData(980, 40 + 243, 235)] // 1745×982 (1920×1080 bei 110 %): wie bisher clamp(H·0,24; 196; 260)
    [InlineData(618, 88 + 330, ShowcaseLayout.MinListHero)] // nie kleiner als der kleinste Titelbereich
    [InlineData(618, 0, 196)] // Kacheln noch nicht gemessen
    public void Showcase_grid_hero_gives_way_until_the_first_tile_row_is_whole(double height, double gridRow, double expected)
    {
        // Prüfung 2026-10-16: Mit 22 px Luft über der Werkzeugleiste (Wunsch des Nutzers: oben so viel wie unten) war bei
        // 1000×620 und 1178×620 die erste Kachelreihe unten abgeschnitten. Der Titelbereich gibt so weit nach, wie nötig.
        var hero = ShowcaseLayout.GridHeroHeight(height, gridRow);
        Assert.Equal(expected, hero);
        Assert.Equal(expected, ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, height, gridRow: gridRow));
        var standard = Math.Round(Math.Clamp(height * 0.24, 196, 260));
        Assert.Equal(standard, ShowcaseLayout.GridHeroHeight(height, 0));

        // Erste Kachel: unter der Leiste (Luft oben wie unten) und dem Innenabstand; endet über der Statuszeile.
        var tileBottom = ShowcaseLayout.ToolbarTop(LibraryViewMode.Grid, hero) + gridRow + ShowcaseLayout.ToolbarGap + ShowcaseLayout.GridPaddingTop;
        Assert.True(hero == ShowcaseLayout.MinListHero || gridRow == 0 || tileBottom <= height - ShowcaseLayout.PaneBottom, $"H {height}: Kachel endet bei {tileBottom}");
        Assert.True(hero == standard || tileBottom >= height - ShowcaseLayout.PaneBottom - 1, $"H {height}: Titelbereich {hero} kleiner als nötig");

        // Hinweise über dem Raster schieben die Reihe tiefer: der Titelbereich gibt mehr nach (höchstens bis zum Minimum).
        if (gridRow > 0)
        {
            var withNotices = Math.Max(ShowcaseLayout.MinListHero, Math.Min(expected, height - 84 - gridRow - 60 - ShowcaseLayout.PaneBottom));
            Assert.Equal(withNotices, ShowcaseLayout.GridHeroHeight(height, gridRow, 60));
        }
    }

    [Fact]
    public void Showcase_list_hero_range_never_inverts_in_tiny_windows()
    {
        // Unter der kleinsten Fensterhöhe (Testfenster, Fehler im Layout): nie kleiner als das Minimum, nie Ausnahme.
        foreach (var height in new[] { 0.0, -5, double.NaN, 300, 541 })
        {
            var (min, max) = ShowcaseLayout.ListHeroRange(height);
            Assert.True(max >= min, $"H {height}: {min}–{max}");
            Assert.InRange(ShowcaseLayout.ListHeroHeight(height, 0.5), min, max);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-0.2)]
    [InlineData(1.0)]
    [InlineData(7.5)]
    public void Stored_list_hero_ratio_that_makes_no_sense_means_the_default(double? stored)
    {
        Assert.Equal(ShowcaseLayout.DefaultListHeroRatio, ShowcaseLayout.NormalizeListHeroRatio(stored), 9);
        Assert.Equal(ShowcaseLayout.ListHeroHeight(982, null), ShowcaseLayout.ListHeroHeight(982, stored));
    }

    [Fact]
    public void Stored_list_hero_ratio_round_trips_and_survives_a_smaller_window()
    {
        // 3440×1440 bei 110 % (1309 px): Titelbereich auf 800 px gezogen und gemerkt (wie im ViewModel auf vier Stellen).
        var ratio = Math.Round(ShowcaseLayout.ListHeroRatio(800, 1309)!.Value, 4);
        Assert.Equal(0.6112, ratio);
        Assert.Equal(800, ShowcaseLayout.ListHeroHeight(1309, ratio));

        // Kleinere Fenster: Fünf Zeilen gehen vor, die Höhe wird begrenzt; wo Platz ist, gilt der Anteil. Der Anteil selbst
        // bleibt gemerkt, im großen Fenster gilt wieder die gewählte Höhe.
        Assert.Equal(727 - 416, ShowcaseLayout.ListHeroHeight(727, ratio));
        Assert.Equal(800 - 416, ShowcaseLayout.ListHeroHeight(800, ratio));
        Assert.Equal(660, ShowcaseLayout.ListHeroHeight(1080, ratio));
        Assert.Equal(880, ShowcaseLayout.ListHeroHeight(1440, ratio));
        Assert.Equal(800, ShowcaseLayout.ListHeroHeight(1309, ratio));

        // Jede ganze Höhe im Bereich übersteht das Merken bei 1440 px ohne Abweichung.
        var (min, max) = ShowcaseLayout.ListHeroRange(1440);
        for (var h = min; h <= max; h++)
        {
            var stored = Math.Round(ShowcaseLayout.ListHeroRatio(h, 1440)!.Value, 4);
            Assert.Equal(h, ShowcaseLayout.ListHeroHeight(1440, stored));
        }

        Assert.Null(ShowcaseLayout.ListHeroRatio(300, 0));
        Assert.Null(ShowcaseLayout.ListHeroRatio(double.NaN, 900));
    }

    [Fact]
    public void Showcase_toolbar_has_the_same_gap_above_as_below_in_list_and_grid()
    {
        // Wunsch des Nutzers 2026-10-16: zwischen Unterkante des Titelbilds und Werkzeugleiste so viel Luft wie zwischen
        // Werkzeugleiste (40 px) und Inhalt darunter: in der Liste bis zu deren Rahmen, im Raster bis zur ersten Kachelreihe.
        Assert.Equal(10, ShowcaseLayout.ToolbarGap);
        Assert.Equal(ShowcaseLayout.ToolbarToBody - 40, ShowcaseLayout.ToolbarGap);
        Assert.Equal(410, ShowcaseLayout.ToolbarTop(LibraryViewMode.List, 400));
        Assert.Equal(422, ShowcaseLayout.ToolbarTop(LibraryViewMode.Grid, 400));
        Assert.Equal(ShowcaseLayout.ToolbarGap + ShowcaseLayout.GridPaddingTop, ShowcaseLayout.ToolbarTop(LibraryViewMode.Grid, 400) - 400);

        // Ziehgriff der Liste: mit seiner Mitte auf der Unterkante des Titelbilds (Wunsch des Nutzers), bis zur Leiste; oben
        // bleibt er im unteren Innenrand des Titelbereichs (16 px, dessen Inhalt endet dort).
        Assert.Equal(400 - ShowcaseLayout.HeroSplitterOverlap + ShowcaseLayout.HeroSplitterHeight, ShowcaseLayout.ToolbarTop(LibraryViewMode.List, 400));
        Assert.Equal(ShowcaseLayout.HeroSplitterHeight / 2, ShowcaseLayout.HeroSplitterOverlap);
        Assert.True(ShowcaseLayout.HeroSplitterOverlap <= 16);

        // Regal wie bisher.
        Assert.Equal(570, ShowcaseLayout.ToolbarTop(LibraryViewMode.Shelf, 620));
    }

    [Fact]
    public void Showcase_shelf_hero_is_big_and_grid_hero_medium()
    {
        Assert.Equal(620, ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 1000));
        Assert.Equal(500, ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 800)); // wie der Prototyp bei 1280×800
        Assert.Equal(240, ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, 1000));
        Assert.Equal(196, ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, 700));

        // 3440 × 1440: das Regal behält unten Platz für die Kachelreihe (Abweichung vom Prototyp: höchstens H − 462).
        var shelf = ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, 1400);
        Assert.True(1400 - shelf >= 462, "Platz für die Reihe: " + (1400 - shelf));

        // … und füllt ihn: unter der größten Reihe (340 + 34) und der Seitenangabe bleibt kein leeres Band (Sichtprüfung
        // 3440×1440: bei H·0,62 waren es 135 px). Gegenprobe: bei 1000 px bleibt es beim Prototyp (620).
        foreach (var height in new[] { 1309.0, 1400, 1440 })
        {
            var row = height - ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, height) - ShowcaseLayout.BodyBottom;
            Assert.InRange(row - ShowcaseLayout.ShelfPadding - 340, 0, 50);
        }
    }

    [Theory]
    [InlineData(620)] // kleinste Fensterhöhe
    [InlineData(700)]
    [InlineData(727)] // 1280×800 bei 110 % Skalierung
    [InlineData(768)]
    [InlineData(800)]
    [InlineData(982)]
    [InlineData(1309)]
    [InlineData(1440)]
    public void Shelf_hero_leaves_room_for_whole_tiles_at_every_window_height(double height)
    {
        // Gegenprobe zum Prototyp: clamp(H·0,62; 500; 660) ließe bei 727 px nur 189 px für die Reihe (Kachel 200 + 34).
        var hero = ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, height);
        var row = height - hero - ShowcaseLayout.BodyBottom;
        var tile = ShowcaseLayout.Shelf(1600, row).TileHeight;

        Assert.True(tile + ShowcaseLayout.ShelfPadding <= row, $"H {height}: Titelbereich {hero}, Reihe {row}, Kachel {tile}");
        Assert.True(tile >= ShowcaseLayout.TinyShelfTile && (tile >= ShowcaseLayout.MinShelfTile || height < 712), $"H {height}: Kachel {tile}");
        Assert.True(hero >= ShowcaseLayout.MinShelfHero, $"H {height}: Titelbereich {hero}");
        Assert.True(hero >= 500 || row - ShowcaseLayout.ShelfPadding <= ShowcaseLayout.MinShelfTile + 1, $"H {height}: Titelbereich {hero} kleiner als nötig");
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
    public void Showcase_list_columns_share_the_width_of_very_wide_lists()
    {
        // Vollbild 3440 px: Liste etwa 2700 px breit. Ohne Verteilung wäre „Spiel“ über 2000 px breit, mit einer leeren
        // Fläche zwischen Name und Quelle; die Spalten dahinter wachsen gleichmäßig, „Spiel“ bleibt bei 640 px.
        var wide = ShowcaseLayout.ListColumnsFor(2700);
        var extra = wide.SourceWidth - ShowcaseLayout.SourceColumn;
        var name = 2700 - (64 + 118 + 28 + 28) - (3 * 14) - wide.SourceWidth - wide.LastPlayedWidth - wide.HdrWidth;

        Assert.True(extra > 400, "Zuschlag " + extra);
        Assert.Equal(extra, wide.LastPlayedWidth - ShowcaseLayout.LastPlayedColumn);
        Assert.Equal(extra, wide.HdrWidth - ShowcaseLayout.HdrColumn);
        Assert.InRange(name, ShowcaseLayout.MaxNameColumn, ShowcaseLayout.MaxNameColumn + 3);

        // Gegenprobe: bei üblichen Breiten (1920 px, Liste etwa 1240 px) bleiben die Spalten wie im Prototyp.
        Assert.Equal(new ListColumns(true, true, true), ShowcaseLayout.ListColumnsFor(1240));
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
