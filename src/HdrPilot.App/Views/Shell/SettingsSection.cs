using System.Windows;
using System.Windows.Media;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Angaben für Bereichskarten (Hp.Shell.Section) und Navigationseinträge der Einstellungen: Unterzeile, Linien-Symbol und
/// die Markierung „gerade sichtbar“ der Navigation. Als angehängte Eigenschaften, damit jede HeaderedContentControl bzw.
/// Schaltfläche sie tragen kann. Bedienelemente rechts im Kopf einer Karte stehen in ihrem Tag.
/// </summary>
public static class SettingsSection
{
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(SettingsSection), new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(SettingsSection), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.RegisterAttached(
        "IsCurrent", typeof(bool), typeof(SettingsSection), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static string? GetDescription(DependencyObject element) => (string?)element.GetValue(DescriptionProperty);

    public static void SetDescription(DependencyObject element, string? value) => element.SetValue(DescriptionProperty, value);

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);

    public static bool GetIsCurrent(DependencyObject element) => (bool)element.GetValue(IsCurrentProperty);

    public static void SetIsCurrent(DependencyObject element, bool value) => element.SetValue(IsCurrentProperty, value);
}
