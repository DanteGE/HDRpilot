using System.Windows;
using System.Windows.Controls;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Name mit Kennzeichen in einer Zeile (Spieleliste, „AUTO HDR“): Die Kennzeichen (alle Kinder ab dem zweiten) stehen
/// direkt hinter dem Namen (erstes Kind), nicht am rechten Rand. Wird es eng, kürzt sich nur der Name; die Kennzeichen
/// bleiben ganz sichtbar. Ausgeblendete Kennzeichen belegen keinen Platz.
/// </summary>
public sealed class NameWithBadgesPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        if (children.Count == 0)
        {
            return default;
        }

        var badges = 0.0;
        var height = 0.0;
        for (var i = 1; i < children.Count; i++)
        {
            children[i].Measure(new Size(double.PositiveInfinity, availableSize.Height));
            badges += children[i].DesiredSize.Width;
            height = Math.Max(height, children[i].DesiredSize.Height);
        }

        var name = children[0];
        name.Measure(new Size(Math.Max(0, availableSize.Width - badges), availableSize.Height));
        var width = name.DesiredSize.Width + badges;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), Math.Max(height, name.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        if (children.Count == 0)
        {
            return finalSize;
        }

        var badges = 0.0;
        for (var i = 1; i < children.Count; i++)
        {
            badges += children[i].DesiredSize.Width;
        }

        var nameWidth = Math.Min(children[0].DesiredSize.Width, Math.Max(0, finalSize.Width - badges));
        children[0].Arrange(new Rect(0, 0, nameWidth, finalSize.Height));
        var x = nameWidth;
        for (var i = 1; i < children.Count; i++)
        {
            var width = children[i].DesiredSize.Width;
            children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
