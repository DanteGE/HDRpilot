using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Inhalt eines Tooltips der Werkzeugleiste (Stil Lib.ToolTip, Vorschau .tip): Titel (13 SemiBold) mit Tastenkappe,
/// Erklärung (12, TextSecondary) und Fußzeile in Mono 11 mit Zahlen, auf Wunsch mit hervorgehobenem Anfang („Filter an“).
/// Leere Teile entfallen. Die Texte kommen aus der Oberfläche (resx) bzw. aus dem ViewModel (Zahlen).
/// </summary>
public sealed class ToolbarTip : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToolbarTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(nameof(Shortcut), typeof(string), typeof(ToolbarTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ToolbarTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FooterLeadProperty = DependencyProperty.Register(nameof(FooterLead), typeof(string), typeof(ToolbarTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(nameof(Footer), typeof(string), typeof(ToolbarTip), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Tastenkürzel hinter dem Titel („Alt+H“); leer: keine Kappe.</summary>
    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Hervorgehobener Anfang der Fußzeile (Akzentfarbe), etwa „Filter an“.</summary>
    public string FooterLead
    {
        get => (string)GetValue(FooterLeadProperty);
        set => SetValue(FooterLeadProperty, value);
    }

    public string Footer
    {
        get => (string)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}

/// <summary>
/// Lage der Tooltips der Werkzeugleiste: mittig unter dem Element, 8 px Abstand (Lib.ToolTip setzt <see cref="CenteredProperty"/>;
/// WPF legt den Tooltip unter das Element, beim Öffnen wird er mittig ausgerichtet; am Bildschirmrand verschiebt WPF ihn).
/// </summary>
public static class ToolbarTipPlacement
{
    /// <summary>Abstand zwischen Element und Tooltip.</summary>
    public const double Gap = 8;

    /// <summary>Rand des Tooltips für seinen Schatten (Lib.ToolTip: oben 4, unten 16, seitlich je 10).</summary>
    public const double ShadowTop = 4;

    public static readonly DependencyProperty CenteredProperty = DependencyProperty.RegisterAttached(
        "Centered", typeof(bool), typeof(ToolbarTipPlacement), new PropertyMetadata(false, OnCenteredChanged));

    public static bool GetCentered(DependencyObject element) => (bool)element.GetValue(CenteredProperty);

    public static void SetCentered(DependencyObject element, bool value) => element.SetValue(CenteredProperty, value);

    /// <summary>Waagerechter Versatz, der einen Tooltip dieser Breite mittig unter ein Element dieser Breite legt.</summary>
    public static double CenterOffset(double targetWidth, double tipWidth) => Math.Round((targetWidth - tipWidth) / 2);

    private static void OnCenteredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolTip tip)
        {
            tip.Opened -= OnOpened;
            if (e.NewValue is true)
            {
                tip.Opened += OnOpened;
            }
        }
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip { PlacementTarget: FrameworkElement target } tip)
        {
            tip.Placement = PlacementMode.Bottom;
            tip.VerticalOffset = Gap - ShadowTop;
            tip.HorizontalOffset = CenterOffset(target.ActualWidth, tip.ActualWidth);
        }
    }
}
