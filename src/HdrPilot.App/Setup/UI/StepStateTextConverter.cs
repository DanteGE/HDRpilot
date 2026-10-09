using System.Globalization;
using System.Windows.Data;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Zustand eines Schritts als Text aus den Ressourcen („offen“, „läuft“, „erledigt“, „fehlgeschlagen“) für den
/// ItemStatus der Listenzeile: Bildschirmleser sagen sonst den Namen des Enum-Werts an.
/// </summary>
public sealed class StepStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StepState state ? SetupText.State(state) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
