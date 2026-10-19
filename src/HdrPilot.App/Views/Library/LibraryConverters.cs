using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Wert → Visibility: true, nicht leerer Text, Zahl ungleich 0 und jedes andere Objekt gelten als sichtbar; null und
/// false als ausgeblendet. Mit Parameter „invert“ umgekehrt.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            _ => true,
        };
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visibility → bool: nur <see cref="Visibility.Visible"/> ist true. Etwa: Eine Liste ist nur dann ein Halt für die
/// Tabulatortaste, wenn ihre Bildlaufleiste zu sehen ist (die Rückfrage von „Einstellungen entfernen…“, E90).
/// </summary>
public sealed class IsVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Zahl skalieren und begrenzen, etwa die Schriftgröße eines erzeugten Covers nach der Kachelbreite. Parameter
/// „Faktor;Minimum;Maximum“ (invariante Schreibweise), zum Beispiel „0.105;9;22“.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var number = value is double d && double.IsFinite(d) ? d : 0;
        var parts = (parameter as string ?? "1").Split(';');
        double Part(int i, double fallback) => parts.Length > i && double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : fallback;
        var result = number * Part(0, 1);
        return Math.Clamp(result, Part(1, double.MinValue), Part(2, double.MaxValue));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
