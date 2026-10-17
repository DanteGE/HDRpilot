using HdrPilot.App.Views.Library;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Werkzeugleiste, die in gefüllte Zeilen umbricht (Wunsch des Nutzers 2026-10-16), als reine Rechnung
/// (<see cref="ToolbarFlowPanel.Layout"/>): so wenige Zeilen wie möglich, ausgewogen und in der Reihenfolge geteilt, jede
/// Zeile genau so breit wie die Leiste, feste Elemente unverändert; in einer Zeile das bisherige Bild (Gruppen links und
/// rechts, nur die Suche wächst, begrenzt).
/// </summary>
public sealed class ToolbarFlowLayoutTests
{
    private const double Gap = 8;

    /// <summary>
    /// Die Elemente der Bibliothek mit ihren natürlichen Breiten (gemessen, ganze Pixel): Suche, Quelle, Sortierung, drei
    /// Chips, Ansicht (Beginn der rechten Gruppe), Neu einlesen, gesehen (fest), Spiel hinzufügen.
    /// </summary>
    private static readonly FlowItem[] Toolbar =
    [
        new(236, 4, 320), new(188, 2), new(196, 2), new(133, 2), new(109, 2), new(133, 2),
        new(224, 1, EndGroup: true), new(40), new(40), new(143, 1),
    ];

    private static double Natural(IReadOnlyList<FlowItem> items) => items.Sum(i => Math.Ceiling(i.Width - 0.01)) + (Gap * (items.Count - 1));

    private static IEnumerable<int> InRow(FlowLayout layout, int row) => Enumerable.Range(0, layout.Row.Count).Where(i => layout.Row[i] == row);

    /// <summary>Breite einer Zeile in natürlichen Breiten (ganze Pixel wie die Rechnung).</summary>
    private static double RowWidth(IReadOnlyList<FlowItem> items, int first, int end) =>
        Enumerable.Range(first, end - first).Sum(i => Math.Ceiling(items[i].Width - 0.01)) + (Gap * (end - first - 1));

    /// <summary>Alle Teilungen in zusammenhängende Zeilen (Erstes Element je Zeile), zum Vergleich mit der Rechnung.</summary>
    private static IEnumerable<int[]> Splits(int n, int rows)
    {
        if (rows == 1)
        {
            yield return [0];
            yield break;
        }

        foreach (var rest in Splits(n, rows - 1))
        {
            for (var start = rest[^1] + 1; start < n; start++)
            {
                yield return [.. rest, start];
            }
        }
    }

    private static double Widest(IReadOnlyList<FlowItem> items, int[] starts) =>
        Enumerable.Range(0, starts.Length).Max(r => RowWidth(items, starts[r], r + 1 < starts.Length ? starts[r + 1] : items.Count));

    [Fact]
    public void Everything_that_fits_stays_in_one_row_with_the_groups_left_and_right()
    {
        var available = Natural(Toolbar) + 40;
        var layout = ToolbarFlowPanel.Layout(Toolbar, available, Gap);

        Assert.Equal(1, layout.Rows);
        Assert.All(layout.Row, r => Assert.Equal(0, r));

        // Die Suche wächst bis zu ihrer Grenze (hier um den ganzen Rest von 40 px), sonst bleibt alles natürlich breit.
        Assert.Equal(276, layout.Width[0]);
        for (var i = 1; i < Toolbar.Length; i++)
        {
            Assert.Equal(Math.Ceiling(Toolbar[i].Width - 0.01), layout.Width[i]);
        }

        // Links bündig, rechts bündig: Die rechte Gruppe (ab der Ansicht) endet am rechten Rand.
        Assert.Equal(0, layout.X[0]);
        Assert.Equal(available, layout.X[^1] + layout.Width[^1], 6);
    }

    [Fact]
    public void One_row_at_3440_px_grows_only_the_search_up_to_its_maximum_and_leaves_the_rest_between_the_groups()
    {
        var available = 2846.5; // Liste bei 3440×1440
        var layout = ToolbarFlowPanel.Layout(Toolbar, available, Gap);

        Assert.Equal(1, layout.Rows);
        Assert.Equal(320, layout.Width[0]); // nie absurd breit
        Assert.Equal(new[] { 188.0, 196, 133, 109, 133, 224, 40, 40, 143 }, layout.Width.Skip(1));

        // Linke Gruppe mit 8 px Abstand ab 0, rechte Gruppe mit 8 px Abstand bis zum rechten Rand, dazwischen der Rest.
        for (var i = 1; i < 6; i++)
        {
            Assert.Equal(Gap, layout.X[i] - (layout.X[i - 1] + layout.Width[i - 1]), 6);
        }

        for (var i = 7; i < Toolbar.Length; i++)
        {
            Assert.Equal(Gap, layout.X[i] - (layout.X[i - 1] + layout.Width[i - 1]), 6);
        }

        Assert.Equal(available, layout.X[^1] + layout.Width[^1], 6);
        Assert.True(layout.X[6] - (layout.X[5] + layout.Width[5]) > 1000, "freier Platz zwischen den Gruppen");
    }

    [Theory]
    [InlineData(1436)] // Liste bei 1920×1080
    [InlineData(1142)] // 1600×1000
    [InlineData(1249)] // 1745×982
    [InlineData(852)] // 1280×800
    [InlineData(734)] // 1164×727
    [InlineData(632)] // 1280×800, Detailbereich am breitesten
    [InlineData(572)] // kleinstes Fenster
    [InlineData(400)] // schmalste Liste
    public void Narrow_toolbars_use_the_fewest_rows_balanced_in_order_and_filled_flush(double available)
    {
        var layout = ToolbarFlowPanel.Layout(Toolbar, available, Gap);
        var n = Toolbar.Length;

        // Wenigste Zeilen: keine Teilung in eine Zeile weniger passt.
        Assert.True(layout.Rows > 1);
        Assert.DoesNotContain(Splits(n, layout.Rows - 1), s => Widest(Toolbar, s) <= available);

        // Reihenfolge bleibt: Zeilen steigen, innerhalb einer Zeile von links nach rechts.
        for (var i = 1; i < n; i++)
        {
            Assert.True(layout.Row[i] == layout.Row[i - 1] ? layout.X[i] > layout.X[i - 1] : layout.Row[i] == layout.Row[i - 1] + 1);
        }

        // Ausgewogen: Keine andere Teilung in so viele Zeilen hat eine schmalere breiteste Zeile.
        var starts = Enumerable.Range(0, layout.Rows).Select(r => InRow(layout, r).First()).ToArray();
        var best = Splits(n, layout.Rows).Min(s => Widest(Toolbar, s));
        Assert.Equal(best, Widest(Toolbar, starts));

        // Jede Zeile genau so breit wie die Leiste, Abstände 8, nichts schmaler als natürlich, feste Elemente unverändert.
        for (var r = 0; r < layout.Rows; r++)
        {
            var row = InRow(layout, r).ToList();
            Assert.Equal(0, layout.X[row[0]]);
            Assert.Equal(available, layout.X[row[^1]] + layout.Width[row[^1]], 6);
            for (var k = 1; k < row.Count; k++)
            {
                Assert.Equal(Gap, layout.X[row[k]] - (layout.X[row[k - 1]] + layout.Width[row[k - 1]]), 6);
            }
        }

        for (var i = 0; i < n; i++)
        {
            Assert.True(layout.Width[i] >= Math.Min(Toolbar[i].Width, available), $"Element {i}: {layout.Width[i]}");
        }

        Assert.Equal(40, layout.Width[7]);
        Assert.Equal(40, layout.Width[8]);
    }

    [Fact]
    public void The_two_row_split_matches_the_mockup_and_the_search_grows_most()
    {
        // 1366×768 bei 110 % (Liste 807 px): oben Suche, Quelle, Sortierung, Automatik; unten Neu, HDR-fähig, Ansicht, Aktionen.
        var layout = ToolbarFlowPanel.Layout(Toolbar, 807, Gap);

        Assert.Equal(2, layout.Rows);
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1 }, layout.Row);
        var grown = Enumerable.Range(0, 4).Select(i => layout.Width[i] - Toolbar[i].Width).ToList();
        Assert.True(grown[0] > grown[1] && grown[1] > 0, string.Join(", ", grown));
        Assert.Equal(grown[1], grown[2], 0);
        Assert.Equal(grown[1], grown[3], 0);

        // Ansicht wächst mäßig (Anteil 1 statt 2), feste Symbol-Schaltflächen gar nicht.
        Assert.True(layout.Width[6] - 224 < layout.Width[4] - 109);
    }

    [Fact]
    public void An_item_wider_than_the_toolbar_gets_its_own_row_at_full_width()
    {
        FlowItem[] items = [new(120, 1), new(900, 1), new(80, 1)];
        var layout = ToolbarFlowPanel.Layout(items, 500, Gap);

        Assert.Equal(3, layout.Rows);
        Assert.Equal(new[] { 0, 1, 2 }, layout.Row);
        Assert.All(layout.Width, w => Assert.Equal(500, w));
        Assert.All(layout.X, x => Assert.Equal(0, x));

        var single = ToolbarFlowPanel.Layout([new FlowItem(900, 1)], 500, Gap);
        Assert.Equal(1, single.Rows);
        Assert.Equal(500, single.Width[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-20)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Without_a_width_everything_stays_in_one_row_at_its_natural_width(double available)
    {
        var layout = ToolbarFlowPanel.Layout(Toolbar, available, Gap);

        Assert.Equal(1, layout.Rows);
        for (var i = 0; i < Toolbar.Length; i++)
        {
            Assert.Equal(Math.Ceiling(Toolbar[i].Width - 0.01), layout.Width[i]);
        }

        Assert.Equal(Natural(Toolbar), layout.X[^1] + layout.Width[^1]);
        Assert.Equal(0, ToolbarFlowPanel.Layout([], 500, Gap).Rows);
    }

    [Fact]
    public void A_row_of_fixed_items_only_still_ends_flush()
    {
        FlowItem[] items = [new(300, 1), new(260, 1), new(40), new(40)];
        var layout = ToolbarFlowPanel.Layout(items, 320, Gap);

        Assert.Equal(3, layout.Rows);
        var last = InRow(layout, 2).ToList();
        Assert.Equal(new[] { 2, 3 }, last);
        Assert.Equal(320, layout.X[3] + layout.Width[3], 6);
        Assert.Equal(layout.Width[2], layout.Width[3], 0);
    }

    [Fact]
    public void Fractional_widths_end_exactly_at_the_right_edge_on_whole_pixels()
    {
        var layout = ToolbarFlowPanel.Layout(Toolbar, 852.4, Gap);

        for (var r = 0; r < layout.Rows; r++)
        {
            var row = InRow(layout, r).ToList();
            Assert.Equal(852.4, layout.X[row[^1]] + layout.Width[row[^1]], 9);
            Assert.All(row, i => Assert.Equal(Math.Round(layout.X[i]), layout.X[i])); // linke Kanten auf ganzen Pixeln
        }
    }

    [Fact]
    public void Random_toolbars_keep_every_rule()
    {
        var random = new Random(4711);
        for (var run = 0; run < 400; run++)
        {
            var items = Enumerable.Range(0, random.Next(1, 9))
                .Select(_ => new FlowItem(20 + (random.NextDouble() * 260), random.Next(0, 4), random.Next(0, 2) == 0 ? 0 : 400, random.Next(0, 6) == 0))
                .ToArray();
            var available = 60 + (random.NextDouble() * 900);
            var layout = ToolbarFlowPanel.Layout(items, available, Gap);
            var fits = Natural(items) <= Math.Floor(available + 0.01);

            Assert.True(!fits || layout.Rows == 1, $"Lauf {run}: passt in eine Zeile, aber {layout.Rows} Zeilen");
            for (var r = 0; r < layout.Rows; r++)
            {
                var row = InRow(layout, r).ToList();
                Assert.NotEmpty(row);
                Assert.Equal(0, layout.X[row[0]]);
                var right = layout.X[row[^1]] + layout.Width[row[^1]];
                Assert.True(right <= available + 1e-6, $"Lauf {run}: Zeile {r} endet bei {right}, Leiste {available}");
                if (!fits)
                {
                    Assert.Equal(available, right, 6); // umgebrochen: jede Zeile bündig
                }
            }

            if (!fits && layout.Rows > 1)
            {
                Assert.DoesNotContain(Splits(items.Length, layout.Rows - 1), s => Widest(items, s) <= Math.Floor(available + 0.01));
            }
        }
    }
}
