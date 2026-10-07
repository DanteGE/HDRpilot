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
