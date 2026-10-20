using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HdrPilot.App.Views.Shell;

/// <summary>True → Collapsed, False → Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Abzeichen „Kein HDR“: nur bei verbundenem Bildschirm ohne HDR (Werte: HdrSupported, IsConnected).</summary>
public sealed class NoHdrBadgeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is false && values[1] is true ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Null oder leerer Text → Collapsed, sonst Visible (Parameter „invert“ kehrt es um).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string s && s.Length == 0);
        if (string.Equals(parameter as string, "invert", StringComparison.Ordinal))
        {
            empty = !empty;
        }

        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Schlüssel einer Farb-Ressource (etwa „Hp.Brush.Source.Steam“) → Pinsel. Wechselt das Thema, liest die Seite ihre
/// Bindungen neu (Darstellung meldet die Änderung), dann gilt der neue Pinsel.
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key) is Brush brush ? brush : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Anteil 0–1 → Breite in Pixeln (Parameter: volle Breite, Standard 40), etwa für Balken der Abdeckung.</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var full = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 40;
        return value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) * full : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Text in Großbuchstaben (Überzeilen wie „VORLAGEN“, CSS text-transform: uppercase).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string)?.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
