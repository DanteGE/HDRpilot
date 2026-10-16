using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace HdrPilot.App.Views.Library;

/// <summary>Ein Element der Werkzeugleiste für die Rechnung von <see cref="ToolbarFlowPanel.Layout"/>.</summary>
/// <param name="Width">Natürliche Breite (so breit ist es mindestens).</param>
/// <param name="Grow">Anteil am Zuwachs, wenn eine Zeile gefüllt wird (0: feste Breite).</param>
/// <param name="SingleRowMaxWidth">So breit wird es höchstens, wenn alles in eine Zeile passt (bis zur natürlichen Breite: wächst nicht).</param>
/// <param name="EndGroup">Beginnt die rechte Gruppe: In einer Zeile stehen es und alle folgenden rechtsbündig.</param>
public readonly record struct FlowItem(double Width, double Grow = 0, double SingleRowMaxWidth = 0, bool EndGroup = false);

/// <summary>Ergebnis von <see cref="ToolbarFlowPanel.Layout"/>: Zeile, linke Kante und Breite je Element.</summary>
public sealed class FlowLayout(int rows, int[] row, double[] x, double[] width)
{
    /// <summary>Zahl der Zeilen (0 ohne Elemente).</summary>
    public int Rows { get; } = rows;

    /// <summary>Zeile je Element (ab 0; in der Reihenfolge der Elemente nie fallend).</summary>
    public IReadOnlyList<int> Row { get; } = row;

    /// <summary>Linke Kante je Element, ab dem linken Rand.</summary>
    public IReadOnlyList<double> X { get; } = x;

    /// <summary>Breite je Element, wie angeordnet.</summary>
    public IReadOnlyList<double> Width { get; } = width;
}

/// <summary>
/// Werkzeugleiste, die in gefüllte Zeilen umbricht (Wunsch des Nutzers 2026-10-16, „Blöcke, Zeilen gefüllt“). Passt alles in
/// natürlicher Breite nebeneinander, steht es in einer Zeile wie bisher: links die Elemente vor dem ersten mit
/// <see cref="EndGroupProperty"/>, rechtsbündig der Rest, dazwischen der freie Platz; wachsen dürfen dort nur Elemente bis
/// zu ihrer <see cref="SingleRowMaxWidthProperty"/>. Sonst so wenige Zeilen wie möglich, die Elemente in ihrer Reihenfolge
/// so verteilt, dass die breiteste Zeile so schmal wie möglich ist (bei Gleichstand die gleichmäßigste Verteilung), und
/// jede Zeile genau auf die volle Breite gestreckt: Den Zuwachs bekommen die Elemente nach <see cref="GrowProperty"/>, feste
/// Elemente (0) behalten ihre Breite (<see cref="BasisProperty"/> bzw. was ihr Inhalt will). Abstände zwischen Elementen und Zeilen sind überall gleich, Kanten liegen auf ganzen
/// Pixeln (bis auf die rechte bei krummer Breite). Elemente werden nur angeordnet, nie aus- oder umgehängt: Tab-Reihenfolge
/// wie sichtbar, kein Fokusverlust beim Umbrechen. Gerechnet wird nur, wenn WPF neu misst (Größe oder Inhalt geändert),
/// nie im Leerlauf.
/// </summary>
public sealed class ToolbarFlowPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(ToolbarFlowPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(ToolbarFlowPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Anteil eines Elements am Zuwachs, wenn eine Zeile gefüllt wird (0: feste Breite).</summary>
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(double), typeof(ToolbarFlowPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>
    /// Grund- und Mindestbreite eines Elements, auch wenn sein Inhalt weniger will; gemessen wird es in dieser Breite (die
    /// Suche wird durch einen langen Suchtext nicht breiter, die Leiste bricht beim Tippen nicht um). 0: was der Inhalt will.
    /// </summary>
    public static readonly DependencyProperty BasisProperty = DependencyProperty.RegisterAttached(
        "Basis", typeof(double), typeof(ToolbarFlowPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>So breit wird ein Element höchstens, wenn alles in eine Zeile passt (0: wächst dort nicht).</summary>
    public static readonly DependencyProperty SingleRowMaxWidthProperty = DependencyProperty.RegisterAttached(
        "SingleRowMaxWidth", typeof(double), typeof(ToolbarFlowPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>Beginnt die rechte Gruppe: In einer Zeile stehen dieses und alle folgenden Elemente rechtsbündig.</summary>
    public static readonly DependencyProperty EndGroupProperty = DependencyProperty.RegisterAttached(
        "EndGroup", typeof(bool), typeof(ToolbarFlowPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private readonly List<UIElement> _shown = [];
    private FlowItem[] _items = [];

    /// <summary>Abstand zwischen den Elementen einer Zeile.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Abstand zwischen den Zeilen.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>Zahl der Zeilen bei der letzten Anordnung (Tests, Prüfung).</summary>
    public int Rows { get; private set; }

    public static double GetGrow(UIElement element) => (double)element.GetValue(GrowProperty);

    public static void SetGrow(UIElement element, double value) => element.SetValue(GrowProperty, value);

    public static double GetBasis(UIElement element) => (double)element.GetValue(BasisProperty);

    public static void SetBasis(UIElement element, double value) => element.SetValue(BasisProperty, value);

    public static double GetSingleRowMaxWidth(UIElement element) => (double)element.GetValue(SingleRowMaxWidthProperty);

    public static void SetSingleRowMaxWidth(UIElement element, double value) => element.SetValue(SingleRowMaxWidthProperty, value);

    public static bool GetEndGroup(UIElement element) => (bool)element.GetValue(EndGroupProperty);

    public static void SetEndGroup(UIElement element, bool value) => element.SetValue(EndGroupProperty, value);

    /// <summary>
    /// Anordnung als reine Rechnung (ohne WPF prüfbar). Mit einer Breite: eine Zeile, wenn alles in natürlicher Breite passt
    /// (Wachstum nur bis <see cref="FlowItem.SingleRowMaxWidth"/>, der Rest vor <see cref="FlowItem.EndGroup"/> bzw. am
    /// Ende); sonst die kleinste Zahl Zeilen, ausgewogen geteilt, jede Zeile genau so breit wie
    /// <paramref name="available"/>. Ein Element, das allein breiter ist, bekommt eine eigene Zeile in voller Breite
    /// (abgeschnitten). Ohne Breite (0, negativ, NaN, unendlich: noch nicht angeordnet bzw. ohne Grenze gemessen) eine Zeile
    /// in natürlicher Breite. Gerechnet wird in ganzen Pixeln (natürliche Breiten aufgerundet, Abstand gerundet); den
    /// Bruchteil einer krummen Breite bekommt die rechte Kante.
    /// </summary>
    public static FlowLayout Layout(IReadOnlyList<FlowItem> items, double available, double gap)
    {
        ArgumentNullException.ThrowIfNull(items);
        var n = items.Count;
        var row = new int[n];
        var x = new double[n];
        var width = new double[n];
        if (n == 0)
        {
            return new FlowLayout(0, row, x, width);
        }

        var g = Math.Max(0, Math.Round(double.IsFinite(gap) ? gap : 0));
        var w = new double[n];
        for (var i = 0; i < n; i++)
        {
            w[i] = double.IsFinite(items[i].Width) && items[i].Width > 0 ? Math.Ceiling(items[i].Width - 0.01) : 0;
        }

        // Fast ganze Breiten zählen als ganz; der Rest (auch ein winziger negativer) geht an die rechte Kante.
        var bounded = double.IsFinite(available) && available > 0;
        var room = bounded ? Math.Floor(available + 0.01) : double.PositiveInfinity;
        var rest = bounded ? available - room : 0;
        var natural = w.Sum() + (g * (n - 1));
        if (!bounded || natural <= room)
        {
            var grown = SingleRow(items, w, bounded ? room - natural : 0, out var free);
            var end = Enumerable.Range(1, n - 1).FirstOrDefault(i => items[i].EndGroup);
            var slack = free + rest;
            if (end == 0 && slack < 0)
            {
                grown[^1] += slack;
            }

            var left = 0.0;
            for (var i = 0; i < n; i++)
            {
                left += i > 0 && i == end ? slack : 0;
                x[i] = left;
                width[i] = grown[i];
                left += grown[i] + g;
            }

            return new FlowLayout(1, row, x, width);
        }

        // Breiter als die Leiste: allein in einer Zeile, auf ihre Breite begrenzt.
        for (var i = 0; i < n; i++)
        {
            w[i] = Math.Min(w[i], room);
        }

        var starts = BalancedRows(w, g, room);
        for (var r = 0; r < starts.Length; r++)
        {
            var first = starts[r];
            var end = r + 1 < starts.Length ? starts[r + 1] : n;
            var weights = Enumerable.Range(first, end - first).Select(i => items[i].Grow).ToArray();
            if (weights.Sum() <= 0)
            {
                Array.Fill(weights, 1.0); // nur feste Elemente: ausnahmsweise gleichmäßig, die Zeile bleibt bündig
            }

            var add = Distribute(room - RowWidth(w, g, first, end), weights);
            add[^1] += rest;
            var left = 0.0;
            for (var i = first; i < end; i++)
            {
                row[i] = r;
                x[i] = left;
                width[i] = w[i] + add[i - first];
                left += w[i] + add[i - first] + g;
            }
        }

        return new FlowLayout(starts.Length, row, x, width);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Natürliche Breiten: ohne Grenze gemessen bzw. in der Grundbreite.
        _shown.Clear();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(default);
                continue;
            }

            var basis = GetBasis(child);
            child.Measure(new Size(basis > 0 ? basis : double.PositiveInfinity, availableSize.Height));
            _shown.Add(child);
        }

        _items = [.. _shown.Select(c =>
            new FlowItem(Math.Max(c.DesiredSize.Width, GetBasis(c)), Math.Max(0, GetGrow(c)), GetSingleRowMaxWidth(c), GetEndGroup(c)))];
        var layout = Layout(_items, availableSize.Width, Gap);

        // Erneut in der Breite messen, in der angeordnet wird (gestreckt bzw. abgeschnitten).
        for (var i = 0; i < _shown.Count; i++)
        {
            _shown[i].Measure(new Size(layout.Width[i], availableSize.Height));
        }

        Rows = layout.Rows;
        var heights = RowHeights(layout);
        var used = layout.Rows == 0 ? 0 : layout.X[^1] + layout.Width[^1];
        return new Size(used, heights.Sum() + (Math.Max(0, layout.Rows - 1) * RowGap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Natürliche Breiten aus dem Messen; hier wird nur angeordnet. Eine einzelne Zeile bekommt die ganze Höhe (etwa die
        // Ansichten im Rahmen ihres Segments).
        var layout = Layout(_items, finalSize.Width, Gap);
        var heights = RowHeights(layout);
        if (layout.Rows == 1)
        {
            heights[0] = Math.Max(heights[0], finalSize.Height);
        }

        var top = 0.0;
        for (var i = 0; i < _shown.Count; i++)
        {
            var r = layout.Row[i];
            if (i > 0 && r != layout.Row[i - 1])
            {
                top += heights[r - 1] + RowGap;
            }

            _shown[i].Arrange(new Rect(layout.X[i], top, layout.Width[i], heights[r]));
        }

        Rows = layout.Rows;
        return finalSize;
    }

    /// <summary>
    /// Höhe, die die Leiste in einer anderen Breite hätte (mit den zuletzt gemessenen Elementen; 0 vor dem ersten Messen).
    /// Etwa für das Regal: ob die Leiste in voller Breite unter die Automatik-Karte passt.
    /// </summary>
    public double HeightFor(double width)
    {
        if (_items.Length == 0)
        {
            return 0;
        }

        var row = _shown.Max(c => c.DesiredSize.Height);
        var rows = Layout(_items, width, Gap).Rows;
        return (rows * row) + (Math.Max(0, rows - 1) * RowGap);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        string.IsNullOrEmpty(AutomationProperties.GetAutomationId(this)) ? base.OnCreateAutomationPeer() : new ToolbarPeer(this);

    private double[] RowHeights(FlowLayout layout)
    {
        var heights = new double[layout.Rows];
        for (var i = 0; i < _shown.Count && i < layout.Row.Count; i++)
        {
            heights[layout.Row[i]] = Math.Max(heights[layout.Row[i]], _shown[i].DesiredSize.Height);
        }

        return heights;
    }

    private static double RowWidth(double[] w, double g, int first, int end)
    {
        var sum = 0.0;
        for (var i = first; i < end; i++)
        {
            sum += w[i];
        }

        return sum + (g * Math.Max(0, end - first - 1));
    }

    /// <summary>
    /// Eine Zeile: Elemente mit Anteil wachsen danach, jedes höchstens bis zu seiner
    /// <see cref="FlowItem.SingleRowMaxWidth"/> (wer sie erreicht, gibt seinen Anteil an die übrigen ab). Was übrig bleibt
    /// (<paramref name="free"/>), liegt vor der rechten Gruppe bzw. am Ende.
    /// </summary>
    private static double[] SingleRow(IReadOnlyList<FlowItem> items, double[] w, double room, out double free)
    {
        var n = w.Length;
        var grown = (double[])w.Clone();
        var cap = new double[n];
        var open = new List<int>();
        for (var i = 0; i < n; i++)
        {
            cap[i] = Math.Floor(items[i].SingleRowMaxWidth) - w[i];
            if (items[i].Grow > 0 && cap[i] > 0)
            {
                open.Add(i);
            }
        }

        var left = room;
        while (open.Count > 0 && left > 0)
        {
            var weight = open.Sum(i => items[i].Grow);
            var full = open.Where(i => left * items[i].Grow / weight >= cap[i]).ToList();
            if (full.Count == 0)
            {
                var add = Distribute(left, [.. open.Select(i => items[i].Grow)]);
                for (var k = 0; k < open.Count; k++)
                {
                    grown[open[k]] += add[k];
                }

                left = 0;
                break;
            }

            foreach (var i in full)
            {
                grown[i] += cap[i];
                left -= cap[i];
                open.Remove(i);
            }
        }

        free = left;
        return grown;
    }

    /// <summary>
    /// Kleinste Zahl Zeilen (der Reihe nach auffüllen), dann die Teilung mit der schmalsten breitesten Zeile und bei
    /// Gleichstand die mit den gleichmäßigsten Zeilen (kleinste Summe der Quadrate). Ergebnis: erstes Element je Zeile.
    /// </summary>
    private static int[] BalancedRows(double[] w, double g, double room)
    {
        var n = w.Length;
        var rows = MinimalRows(w, g, room);

        // widest[r, i]: schmalste breiteste Zeile für die ersten i Elemente in r Zeilen.
        var widest = Table(rows, n, double.PositiveInfinity);
        widest[0, 0] = 0;
        for (var r = 1; r <= rows; r++)
        {
            for (var i = r; i <= n; i++)
            {
                for (var j = r - 1; j < i; j++)
                {
                    widest[r, i] = Math.Min(widest[r, i], Math.Max(widest[r - 1, j], RowWidth(w, g, j, i)));
                }
            }
        }

        // Unter dieser Grenze die gleichmäßigste Teilung (Zeilen als ganze Pixel: exakter Vergleich).
        var limit = widest[rows, n];
        var cost = Table(rows, n, double.PositiveInfinity);
        var from = new int[rows + 1, n + 1];
        cost[0, 0] = 0;
        for (var r = 1; r <= rows; r++)
        {
            for (var i = r; i <= n; i++)
            {
                for (var j = r - 1; j < i; j++)
                {
                    var width = RowWidth(w, g, j, i);
                    var c = cost[r - 1, j] + (width * width);
                    if (width <= limit && c < cost[r, i])
                    {
                        cost[r, i] = c;
                        from[r, i] = j;
                    }
                }
            }
        }

        var starts = new int[rows];
        for (int r = rows, i = n; r > 0; r--)
        {
            starts[r - 1] = from[r, i];
            i = from[r, i];
        }

        return starts;
    }

    /// <summary>Kleinste Zahl Zeilen: der Reihe nach auffüllen, bis das nächste Element nicht mehr passt.</summary>
    private static int MinimalRows(double[] w, double g, double room)
    {
        var rows = 1;
        var line = w[0];
        for (var i = 1; i < w.Length; i++)
        {
            if (line + g + w[i] > room)
            {
                rows++;
                line = w[i];
            }
            else
            {
                line += g + w[i];
            }
        }

        return rows;
    }

    private static double[,] Table(int rows, int n, double value)
    {
        var table = new double[rows + 1, n + 1];
        for (var r = 0; r <= rows; r++)
        {
            for (var i = 0; i <= n; i++)
            {
                table[r, i] = value;
            }
        }

        return table;
    }

    /// <summary>
    /// Ganze Pixel nach Anteil verteilen: jeder bekommt seinen abgerundeten Anteil, die restlichen Pixel die größten
    /// Bruchteile (bei Gleichstand die vorderen). Die Summe ist genau <paramref name="extra"/> (ganzzahlig).
    /// </summary>
    private static double[] Distribute(double extra, double[] weights)
    {
        var count = weights.Length;
        var add = new double[count];
        var total = weights.Sum();
        if (count == 0 || total <= 0 || extra <= 0)
        {
            return add;
        }

        var fraction = new double[count];
        var given = 0.0;
        for (var k = 0; k < count; k++)
        {
            var exact = extra * weights[k] / total;
            add[k] = Math.Floor(exact);
            fraction[k] = weights[k] > 0 ? exact - add[k] : -1;
            given += add[k];
        }

        foreach (var k in Enumerable.Range(0, count).OrderByDescending(k => fraction[k]).ThenBy(k => k).Take((int)Math.Round(extra - given)))
        {
            add[k] += 1;
        }

        return add;
    }

    /// <summary>Werkzeugleiste im Automationsbaum (nur mit eigener Kennung): Prüfskripte und Bildschirmleser finden sie als Ganzes.</summary>
    private sealed class ToolbarPeer(ToolbarFlowPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ToolBar;

        protected override string GetClassNameCore() => nameof(ToolbarFlowPanel);
    }
}
