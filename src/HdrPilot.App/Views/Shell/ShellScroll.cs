using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Mausrad in verschachtelten Bildlaufbereichen (Liste oder Eingabefeld in einer scrollenden Seite): Kann der innere
/// Bereich in Drehrichtung nicht weiter, geht das Rad an die Seite weiter. Ohne das schluckt WPF das Rad über jeder
/// Liste, auch wenn sie gar nicht scrollen kann, und die Einstellungen ließen sich dort nicht blättern.
/// </summary>
public static class ShellScroll
{
    public static readonly DependencyProperty ChainWheelProperty = DependencyProperty.RegisterAttached(
        "ChainWheel", typeof(bool), typeof(ShellScroll), new PropertyMetadata(false, OnChainWheelChanged));

    public static bool GetChainWheel(DependencyObject element) => (bool)element.GetValue(ChainWheelProperty);

    public static void SetChainWheel(DependencyObject element, bool value) => element.SetValue(ChainWheelProperty, value);

    /// <summary>True, wenn <paramref name="viewer"/> in Drehrichtung (<paramref name="delta"/> &gt; 0 = nach oben) noch scrollen kann.</summary>
    public static bool CanScroll(ScrollViewer? viewer, int delta)
    {
        if (viewer is null || viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return delta > 0 ? viewer.VerticalOffset > 0.5 : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;
    }

    private static void OnChainWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element || CanScroll(FindScrollViewer(element), e.Delta))
        {
            return;
        }

        // Innen ist Schluss: Ereignis hier beenden und beim Elternelement neu auslösen (erreicht die Seite).
        if (VisualTreeHelper.GetParent(element) is UIElement parent)
        {
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = element,
            });
        }
    }

    /// <summary>Der erste ScrollViewer in der Vorlage des Elements (Liste: Vorlagen-Viewer, Eingabefeld: PART_ContentHost).</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is ScrollViewer viewer && !ReferenceEquals(current, root))
            {
                return viewer;
            }

            if (current is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        return null;
    }
}
