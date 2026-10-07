using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
