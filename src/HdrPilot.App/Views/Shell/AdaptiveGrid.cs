using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Gleich breite Spalten, deren Anzahl der Breite folgt (Farbvorlagen 8 → 4, Farbfelder 3 → 1, Beispiele 4 → 2 …):
/// gewählt wird die größte erlaubte Anzahl aus <see cref="Columns"/>, bei der jede Spalte mindestens
/// <see cref="MinItemWidth"/> breit ist. Zeilen sind so hoch wie ihr höchstes Element. Nichts wird abgeschnitten: Reicht
/// die Breite für keine Stufe, gilt die kleinste.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(string), typeof(AdaptiveGrid), new FrameworkPropertyMetadata("3,1", FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private int _columns = 1;
    private double _itemWidth;
    private double[] _rowHeights = [];

    /// <summary>Erlaubte Spaltenzahlen, absteigend oder beliebig, durch Komma getrennt (etwa „8,4,2“).</summary>
    public string Columns
    {
        get => (string)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Abstand zwischen Zeilen; ohne Angabe wie <see cref="Gap"/>.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>Spaltenzahl der letzten Anordnung (Prüfung).</summary>
    public int ActualColumns => _columns;

    /// <summary>Größte erlaubte Spaltenzahl, bei der jede Spalte mindestens <paramref name="minItemWidth"/> breit ist.</summary>
    public static int ChooseColumns(double width, IReadOnlyList<int> allowed, double minItemWidth, double gap, int items)
    {
        var ordered = allowed.Where(c => c > 0).Distinct().OrderByDescending(c => c).ToList();
        if (ordered.Count == 0)
        {
            return 1;
        }

        foreach (var count in ordered)
        {
            if (!double.IsFinite(width) || (width - ((count - 1) * gap)) / count >= minItemWidth)
            {
                return count;
            }
        }

        return ordered[^1];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var allowed = ParseColumns(Columns);
        _columns = ChooseColumns(availableSize.Width, allowed, MinItemWidth, Gap, children.Count);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : (_columns * MinItemWidth) + ((_columns - 1) * Gap);
        _itemWidth = Math.Max(0, (width - ((_columns - 1) * Gap)) / _columns);
        var rows = (int)Math.Ceiling(children.Count / (double)_columns);
        _rowHeights = new double[rows];
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(_itemWidth, double.PositiveInfinity));
            var row = i / _columns;
            _rowHeights[row] = Math.Max(_rowHeights[row], children[i].DesiredSize.Height);
        }

        var rowGap = double.IsNaN(RowGap) ? Gap : RowGap;
        return new Size(width, _rowHeights.Sum() + (Math.Max(0, rows - 1) * rowGap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var itemWidth = Math.Max(0, (finalSize.Width - ((_columns - 1) * Gap)) / _columns);
        var rowGap = double.IsNaN(RowGap) ? Gap : RowGap;
        var top = 0.0;
        for (var row = 0; row < _rowHeights.Length; row++)
        {
            for (var col = 0; col < _columns; col++)
            {
                var index = (row * _columns) + col;
                if (index >= children.Count)
                {
                    break;
                }

                children[index].Arrange(new Rect(col * (itemWidth + Gap), top, itemWidth, _rowHeights[row]));
            }

            top += _rowHeights[row] + rowGap;
        }

        return finalSize;
    }

    private static List<int> ParseColumns(string? text) =>
        (text ?? "1").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0)
            .DefaultIfEmpty(1)
            .ToList();
}

/// <summary>Abgerundeter Zuschnitt für Bildflächen (Vorschau, Beispiele): Clip folgt der Größe des Elements.</summary>
public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedClip), new PropertyMetadata(0.0, OnRadiusChanged));

    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;
        if ((double)e.NewValue > 0)
        {
            element.SizeChanged += OnSizeChanged;
            Apply(element);
        }
        else
        {
            element.Clip = null;
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        var radius = GetRadius(element);
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}

/// <summary>Breite (ActualWidth) mindestens Parameter → Visible, sonst Collapsed (Tabelle wird schmal nur Zahlen).</summary>
public sealed class WidthAtLeastToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var limit = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
        return value is double w && w >= limit ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
