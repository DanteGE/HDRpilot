using HdrPilot.App.Views.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Anordnung der Werkzeugleiste als reine Rechnung (<see cref="ToolbarLayout.Arrange"/>, abgenommene Vorschau leiste.js,
/// Wunsch des Nutzers 2026-10-17): Leiter der Stufen (Suche gibt nach, Felder geben anteilig nach, Sortierung nur als Symbol,
/// erst unter 806 zwei Zeilen), Hysterese nur beim Ziehen, Zeilen an der Gruppengrenze, gefüllte erste Zeile, Kanten auf
/// ganzen Gerätepixeln bei 100, 110, 125 und 150 %.
/// </summary>
public sealed class ToolbarLayoutTests
{
    private static readonly double[] Scales = [1, 1.1, 1.25, 1.5];

    private static ToolbarSlotRect Slot(ToolbarArrangement a, ToolbarSlot slot) => a[slot];

    [Fact]
    public void Thresholds_follow_the_preview_with_a_group_gap_of_24()
    {
        // Vorschau: 1138/1054/898 des Dokuments + 16 (Gruppenabstand 24 statt 8), zwei Zeilen unter 806 (798 + 8).
        Assert.Equal(1154, ToolbarLayout.ComfortableWidth);
        Assert.Equal(1070, ToolbarLayout.SearchYieldWidth);
        Assert.Equal(914, ToolbarLayout.SortStepWidth);
        Assert.Equal(806, ToolbarLayout.OneRowWidth);
        Assert.Equal(266, ToolbarLayout.RightGroup);
        Assert.Equal(410, ToolbarLayout.SecondRowWidth);
        Assert.Equal(40, ToolbarLayout.HeightFor(1));
        Assert.Equal(88, ToolbarLayout.HeightFor(2));
        Assert.Equal(136, ToolbarLayout.HeightFor(3));
    }

    [Theory]
    [InlineData(2641, ToolbarStage.Comfortable, 320, 188, 196)] // 3440×1440
    [InlineData(1434, ToolbarStage.Comfortable, 320, 188, 196)] // 1920×1080
    [InlineData(1154, ToolbarStage.Comfortable, 320, 188, 196)]
    [InlineData(1153, ToolbarStage.SearchYields, 319, 188, 196)]
    [InlineData(1142, ToolbarStage.SearchYields, 308, 188, 196)] // Startfenster 1600×1000 DIP
    [InlineData(1070, ToolbarStage.SearchYields, 236, 188, 196)]
    [InlineData(1018, ToolbarStage.FieldsYield, 213, 171, 184)] // d = 52: 68 : 52 : 36
    [InlineData(914, ToolbarStage.FieldsYield, 168, 136, 160)]
    [InlineData(913, ToolbarStage.SortIcon, 168, 136, 60)] // einziger Schritt
    [InlineData(850, ToolbarStage.SortIcon, 168, 136, 60)] // 1280×800
    [InlineData(806, ToolbarStage.SortIcon, 168, 136, 60)]
    public void Single_row_ladder_gives_way_in_steps(double width, ToolbarStage stage, double search, double source, double sort)
    {
        var a = ToolbarLayout.Arrange(width);
        Assert.Equal(1, a.Rows);
        Assert.Equal(stage, a.Stage);
        Assert.Equal(search, Slot(a, ToolbarSlot.Search).Width, 0);
        Assert.Equal(source, Slot(a, ToolbarSlot.Source).Width, 0);
        Assert.Equal(sort, Slot(a, ToolbarSlot.Sort).Width, 0);
        Assert.Equal(stage == ToolbarStage.SortIcon, a.SortIconOnly);
        Assert.Equal(40, ToolbarLayout.HeightFor(1));
        Assert.Equal(40, a.Height);

        // Linke Gruppe ab 0 mit 8 px, die rechte bündig an der rechten Kante, dazwischen der Freiraum (mindestens 24, auf der
        // Symbol-Stufe 16).
        var order = Enum.GetValues<ToolbarSlot>();
        for (var i = 1; i < order.Length; i++)
        {
            if (order[i] == ToolbarSlot.View)
            {
                continue;
            }

            Assert.Equal(8, Slot(a, order[i]).X - Slot(a, order[i - 1]).Right, 3);
        }

        Assert.Equal(0, Slot(a, ToolbarSlot.Search).X);
        Assert.Equal(width, Slot(a, ToolbarSlot.Add).Right, 3);
        Assert.True(a.GroupGap >= (stage == ToolbarStage.SortIcon ? 16 : 24) - 0.001, $"L {width}: Gruppenabstand {a.GroupGap}");
        Assert.Equal(122, Slot(a, ToolbarSlot.View).Width);
        Assert.All([ToolbarSlot.FilterAuto, ToolbarSlot.FilterNew, ToolbarSlot.FilterHdr, ToolbarSlot.Rescan, ToolbarSlot.Seen, ToolbarSlot.Add], s => Assert.Equal(40, Slot(a, s).Width));
    }

    [Fact]
    public void Single_row_never_gets_wider_while_the_window_gets_narrower_and_keeps_the_group_gap()
    {
        ToolbarArrangement? previous = null;
        for (var width = 2000.0; width >= ToolbarLayout.OneRowWidth; width -= 1)
        {
            var a = ToolbarLayout.Arrange(width);
            Assert.Equal(1, a.Rows);
            Assert.True(a.GroupGap >= (a.Stage == ToolbarStage.SortIcon ? 16 : 24) - 0.001, $"L {width}: {a.GroupGap}");
            if (previous is not null)
            {
                // Je Feld höchstens das eine Pixel des fortlaufenden Rundens, zusammen nie breiter.
                foreach (var slot in Enum.GetValues<ToolbarSlot>())
                {
                    Assert.True(Slot(a, slot).Width <= Slot(previous, slot).Width + 1.001, $"L {width}: {slot} wächst");
                }

                Assert.True(Fields(a) <= Fields(previous) + 0.001, $"L {width}: Felder zusammen breiter");
            }

            previous = a;
        }
    }

    [Theory]
    [InlineData(805, false)]
    [InlineData(734, false)] // 1280×800 bei 110 %
    [InlineData(570, false)] // kleinstes Fenster 1000×620
    [InlineData(480, false)]
    [InlineData(479, true)] // schmalste Liste: Sortierung nur als Symbol
    public void Below_806_two_rows_split_at_the_group_border_with_a_filled_first_row(double width, bool sortIcon)
    {
        var a = ToolbarLayout.Arrange(width);
        Assert.Equal(2, a.Rows);
        Assert.Equal(ToolbarStage.TwoRows, a.Stage);
        Assert.Equal(88, a.Height);
        Assert.Equal(sortIcon, a.SortIconOnly);

        // Zeile 1: Suche · Quelle · Sortierung, bündig an beiden Kanten (die Zeile gefüllt), 8 dazwischen.
        Assert.Equal(0, Slot(a, ToolbarSlot.Search).X);
        Assert.Equal(8, Slot(a, ToolbarSlot.Source).X - Slot(a, ToolbarSlot.Search).Right, 3);
        Assert.Equal(8, Slot(a, ToolbarSlot.Sort).X - Slot(a, ToolbarSlot.Source).Right, 3);
        Assert.Equal(width, Slot(a, ToolbarSlot.Sort).Right, 3);
        Assert.All(new[] { ToolbarSlot.Search, ToolbarSlot.Source, ToolbarSlot.Sort }, s => Assert.Equal(0, Slot(a, s).Y));
        Assert.True(Slot(a, ToolbarSlot.Search).Width <= ToolbarLayout.SearchMax + 0.001);

        // Zeile 2: Filter links (0, 48, 96), Ansicht · Neu einlesen · gesehen · hinzufügen rechts bündig.
        Assert.Equal([0, 48, 96], new[] { ToolbarSlot.FilterAuto, ToolbarSlot.FilterNew, ToolbarSlot.FilterHdr }.Select(s => Slot(a, s).X));
        Assert.All(new[] { ToolbarSlot.FilterAuto, ToolbarSlot.FilterNew, ToolbarSlot.FilterHdr, ToolbarSlot.View, ToolbarSlot.Rescan, ToolbarSlot.Seen, ToolbarSlot.Add }, s => Assert.Equal(48, Slot(a, s).Y));
        Assert.Equal(width, Slot(a, ToolbarSlot.Add).Right, 3);
        Assert.Equal(width - 266, Slot(a, ToolbarSlot.View).X, 3);
    }

    [Fact]
    public void Two_rows_give_the_first_row_to_search_first_up_to_320()
    {
        // 805: 789 zu verteilen; Suche bis 320, der Rest gleich an Quelle und Sortierung (Vorschau).
        var wide = ToolbarLayout.Arrange(805);
        Assert.Equal(320, Slot(wide, ToolbarSlot.Search).Width, 0);
        Assert.InRange(Slot(wide, ToolbarSlot.Sort).Width - 8 - Slot(wide, ToolbarSlot.Source).Width, -1, 1);
        Assert.Equal(789, Slot(wide, ToolbarSlot.Search).Width + Slot(wide, ToolbarSlot.Source).Width + Slot(wide, ToolbarSlot.Sort).Width);

        // 570: 554 zu verteilen, alle drei unter ihrer Grundbreite im Verhältnis 68 : 52 : 36.
        var narrow = ToolbarLayout.Arrange(570);
        Assert.Equal(207, Slot(narrow, ToolbarSlot.Search).Width, 0);
        Assert.Equal(166, Slot(narrow, ToolbarSlot.Source).Width, 0);
        Assert.Equal(181, Slot(narrow, ToolbarSlot.Sort).Width, 0);
    }

    [Fact]
    public void Narrowest_list_puts_the_actions_into_a_third_row()
    {
        // Kleinstes Fenster mit breitestem Detailbereich (Liste 400): Filter und Aktionen passen nicht nebeneinander.
        var a = ToolbarLayout.Arrange(400);
        Assert.Equal(3, a.Rows);
        Assert.Equal(ToolbarStage.ThreeRows, a.Stage);
        Assert.Equal(136, a.Height);
        Assert.Equal(48, Slot(a, ToolbarSlot.FilterHdr).Y);
        Assert.All(new[] { ToolbarSlot.View, ToolbarSlot.Rescan, ToolbarSlot.Seen, ToolbarSlot.Add }, s => Assert.Equal(96, Slot(a, s).Y));
        Assert.Equal(400, Slot(a, ToolbarSlot.Add).Right, 3);
        Assert.Equal(400, Slot(a, ToolbarSlot.Sort).Right, 3);
        Assert.Equal(2, ToolbarLayout.Arrange(410).Rows);
    }

    [Fact]
    public void Hysteresis_applies_only_while_dragging()
    {
        // Ohne Ziehen gilt sofort die Stufe der Breite.
        Assert.Equal(1, ToolbarLayout.Arrange(820).Rows);
        Assert.False(ToolbarLayout.Arrange(930).SortIconOnly);

        // Gezogen aus zwei Zeilen: erst ab 806 + 32 wieder eine (Vorschau TH.oneBack).
        var two = ToolbarLayout.Arrange(800).Memory;
        Assert.True(two.MultipleRows);
        Assert.Equal(2, ToolbarLayout.Arrange(820, memory: two).Rows);
        Assert.Equal(2, ToolbarLayout.Arrange(837, memory: two).Rows);
        Assert.Equal(1, ToolbarLayout.Arrange(838, memory: two).Rows);

        // Gezogen aus der Symbol-Stufe: die Beschriftung erst ab 914 + 24.
        var icon = ToolbarLayout.Arrange(900).Memory;
        Assert.True(icon.SortIcon);
        Assert.True(ToolbarLayout.Arrange(930, memory: icon).SortIconOnly);
        Assert.True(ToolbarLayout.Arrange(937, memory: icon).SortIconOnly);
        Assert.False(ToolbarLayout.Arrange(938, memory: icon).SortIconOnly);

        // In die andere Richtung ohne Verzögerung: die Schwelle selbst.
        var labeled = ToolbarLayout.Arrange(950).Memory;
        Assert.True(ToolbarLayout.Arrange(913, memory: labeled).SortIconOnly);
        Assert.Equal(2, ToolbarLayout.Arrange(805, memory: ToolbarLayout.Arrange(810).Memory).Rows);
    }

    [Fact]
    public void Search_key_hides_below_204_and_while_dragging_returns_only_at_216()
    {
        // Suchbreite 236 - d·68/156: 204 bei L ≈ 996,6; 216 bei L ≈ 1024,1.
        Assert.True(ToolbarLayout.Arrange(1000).SearchKey);
        Assert.False(ToolbarLayout.Arrange(990).SearchKey);
        var hidden = ToolbarLayout.Arrange(990).Memory;
        Assert.False(ToolbarLayout.Arrange(1010, memory: hidden).SearchKey);
        Assert.True(ToolbarLayout.Arrange(1030, memory: hidden).SearchKey);
        Assert.False(ToolbarLayout.Arrange(850).SearchKey); // Symbol-Stufe: Suche 168
        Assert.True(ToolbarLayout.Arrange(1434).SearchKey);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Every_edge_lies_on_a_whole_device_pixel_with_gaps_of_round_8_times_scale(double scale)
    {
        var gap = Math.Round(8 * scale, MidpointRounding.AwayFromZero);
        for (var width = 400.0; width <= 2700; width += 3.7)
        {
            var a = ToolbarLayout.Arrange(width, scale);
            var right = Math.Round(width * scale, MidpointRounding.AwayFromZero);
            Assert.Equal(right / scale, a.Width, 6);
            foreach (var slot in Enum.GetValues<ToolbarSlot>())
            {
                var r = Slot(a, slot);
                Assert.True(IsWhole(r.X * scale) && IsWhole(r.Right * scale) && IsWhole(r.Y * scale), $"{scale}: L {width} {slot} {r}");
                Assert.True(r.X >= -0.001 && r.Right <= a.Width + 0.001, $"{scale}: L {width} {slot} ragt hinaus");
            }

            // Nachbarn in einer Zeile mit genau round(8 · Skalierung) Gerätepixeln (bis auf den Freiraum vor der Ansicht).
            foreach (var row in Enum.GetValues<ToolbarSlot>().GroupBy(s => Slot(a, s).Y))
            {
                var items = row.OrderBy(s => Slot(a, s).X).ToList();
                for (var i = 1; i < items.Count; i++)
                {
                    var d = (Slot(a, items[i]).X - Slot(a, items[i - 1]).Right) * scale;
                    if (items[i] == ToolbarSlot.View)
                    {
                        Assert.True(d >= gap - 0.001, $"{scale}: L {width} Freiraum {d}");
                        continue;
                    }

                    Assert.Equal(gap, d, 3);
                }
            }

            Assert.Equal(right, Slot(a, ToolbarSlot.Add).Right * scale, 3); // bündig rechts
            if (a.Rows > 1)
            {
                Assert.Equal(right, Slot(a, ToolbarSlot.Sort).Right * scale, 3);
                Assert.Equal(Math.Ceiling(40 * scale - 1e-6) + gap, Slot(a, ToolbarSlot.FilterAuto).Y * scale, 3);
            }
        }
    }

    [Fact]
    public void Without_a_width_the_comfortable_row_is_used()
    {
        foreach (var width in new[] { 0, double.NaN, double.PositiveInfinity, -5 })
        {
            var a = ToolbarLayout.Arrange(width);
            Assert.Equal(1, a.Rows);
            Assert.Equal(ToolbarStage.Comfortable, a.Stage);
            Assert.Equal(ToolbarLayout.ComfortableWidth, a.Width);
        }

        Assert.Equal(1, ToolbarLayout.Arrange(1200, double.NaN).Scale);
    }

    private static bool IsWhole(double value) => Math.Abs(value - Math.Round(value)) < 1e-6;

    private static double Fields(ToolbarArrangement a) =>
        Slot(a, ToolbarSlot.Search).Width + Slot(a, ToolbarSlot.Source).Width + Slot(a, ToolbarSlot.Sort).Width;
}
