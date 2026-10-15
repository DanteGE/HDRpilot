using System.Windows;
using System.Windows.Controls;

namespace HdrPilot.App.Views.Shell;

/// <summary>Spalten eines Bereichsrasters bei einer Breite: Anzahl, Breite je Spalte, Abstand und linker Rand der zentrierten Fläche.</summary>
public readonly record struct SettingsGridColumns(int Count, double Width, double Gap, double Left)
{
    /// <summary>Genutzte Breite aller Spalten samt Abständen.</summary>
    public double UsedWidth => (Count * Width) + ((Count - 1) * Gap);

    /// <summary>Linke Kante einer Spalte.</summary>
    public double ColumnLeft(int column) => Left + (column * (Width + Gap));
}

/// <summary>
/// Aufteilung der Einstellungen in Spalten (Anmerkung 4 des Nutzers, G10.8): so viele Spalten, wie bei der Mindestbreite
/// nebeneinander passen (höchstens <see cref="MaxColumns"/>), jede höchstens <see cref="MaxColumnWidth"/> breit; was übrig
/// bleibt, verteilt sich als Rand links und rechts. Im Vollbild wird so nichts in die Länge gezogen. Reine Funktion.
/// </summary>
public static class SettingsGridLayout
{
    /// <summary>Mindestbreite einer Spalte: darunter rutscht der Bereich in eine Spalte weniger.</summary>
    public const double MinColumnWidth = 520;

    /// <summary>Höchstbreite eines Bereichs.</summary>
    public const double MaxColumnWidth = 760;

    public const double Gap = 24;

    public const int MaxColumns = 3;

    public static SettingsGridColumns Compute(double width) => Compute(width, MinColumnWidth, MaxColumnWidth, Gap, MaxColumns);

    public static SettingsGridColumns Compute(double width, double minColumn, double maxColumn, double gap, int maxColumns)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return new SettingsGridColumns(1, maxColumn, gap, 0);
        }

        var count = (int)Math.Floor((width + gap) / (minColumn + gap));
        count = Math.Clamp(count, 1, Math.Max(1, maxColumns));
        var columnWidth = Math.Min(maxColumn, (width - ((count - 1) * gap)) / count);
        columnWidth = Math.Max(0, Math.Floor(columnWidth));
        var used = (count * columnWidth) + ((count - 1) * gap);
        return new SettingsGridColumns(count, columnWidth, gap, Math.Max(0, Math.Floor((width - used) / 2)));
    }
}

/// <summary>
/// Raster der Einstellungsbereiche: Spalten nach <see cref="SettingsGridLayout"/>, Bereiche in ihrer Reihenfolge jeweils in
/// die gerade kürzeste Spalte (Mauerwerk), damit keine Lücken unter kurzen Bereichen entstehen. Jeder Bereich bekommt genau
/// die Spaltenbreite, nie mehr als die Höchstbreite.
/// </summary>
public sealed class SettingsGridPanel : Panel
{
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(SettingsGridPanel), new FrameworkPropertyMetadata(SettingsGridLayout.MaxColumns, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private readonly List<(UIElement Child, int Column, double Top)> _placements = [];
    private SettingsGridColumns _columns = new(1, SettingsGridLayout.MaxColumnWidth, SettingsGridLayout.Gap, 0);

    /// <summary>Höchstzahl der Spalten in diesem Raster (etwa 2 für Gruppen mit wenigen Bereichen).</summary>
    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Spalten der letzten Anordnung (Prüfung, Navigation).</summary>
    public SettingsGridColumns Columns => _columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        _columns = SettingsGridLayout.Compute(
            availableSize.Width, SettingsGridLayout.MinColumnWidth, SettingsGridLayout.MaxColumnWidth, SettingsGridLayout.Gap, MaxColumns);
        var heights = new double[_columns.Count];
        _placements.Clear();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(new Size(_columns.Width, double.PositiveInfinity));
            var column = Shortest(heights);
            var top = heights[column] > 0 ? heights[column] + _columns.Gap : 0;
            _placements.Add((child, column, top));
            heights[column] = top + child.DesiredSize.Height;
        }

        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : _columns.UsedWidth;
        return new Size(width, heights.Length == 0 ? 0 : heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Andere Breite beim Anordnen als beim Messen (etwa ohne vorheriges Messen in Tests): Spalten neu rechnen.
        if (Math.Abs(SettingsGridLayout.Compute(finalSize.Width, SettingsGridLayout.MinColumnWidth, SettingsGridLayout.MaxColumnWidth, SettingsGridLayout.Gap, MaxColumns).Width - _columns.Width) > 0.5)
        {
            MeasureOverride(finalSize);
        }

        foreach (var (child, column, top) in _placements)
        {
            child.Arrange(new Rect(_columns.ColumnLeft(column), top, _columns.Width, child.DesiredSize.Height));
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        return finalSize;
    }

    private static int Shortest(double[] heights)
    {
        var best = 0;
        for (var i = 1; i < heights.Length; i++)
        {
            if (heights[i] < heights[best] - 0.5)
            {
                best = i;
            }
        }

        return best;
    }
}
