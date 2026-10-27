using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App;

/// <summary>
/// Kopf und Detailbereich, Rückmeldungen des Nutzers vom 2026-10-22: Titel mit der gewählten Quelle und Quellenmenü
/// (SourceTitleButton, SourceMenu), Erklärungen zu „Windows Auto HDR“ zum Aufklappen (AutoHdrInfoToggle). Hier nur, was
/// Bildschirmfotos und Tests brauchen; Bedienung und Bewegung stecken in <see cref="MenuButton"/>, <see cref="CrossfadeText"/>
/// und <see cref="DisclosureToggle"/>.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Bildschirmfoto: Quellenmenü, wie es unter dem Titel aufginge (linksbündig mit dem Titel, <see cref="MenuButton.MenuGap"/>
    /// darunter). Ein Kontextmenü gehört in ein Popup; es wird deshalb ohne Eltern angeordnet und über das Fenster gezeichnet.
    /// </summary>
    public SnapshotOverlay? CreateSourceMenuPreview()
    {
        if (SourceTitleButton.ContextMenu is not { } menu)
        {
            return null;
        }

        menu.PlacementTarget = SourceTitleButton;
        SourceTitleButton.PreviewMenuOpen();
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Loaded); // Einträge aus SourceOptions erzeugen
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (dx, dy) = MenuButton.MenuOffset(MenuButton.MenuShadowMargin);
        var at = SourceTitleButton.TranslatePoint(new Point(0, SourceTitleButton.ActualHeight), Root);
        return new SnapshotOverlay(menu, new Point(Math.Round(Math.Max(0, at.X + dx)), Math.Round(at.Y + dy)));
    }

    /// <summary>Bildschirmfoto und Tests: Detailbereich so weit scrollen, dass der Abschnitt „BILD“ oben steht.</summary>
    public void ScrollDetailsToPicture()
    {
        DetailsPanel.UpdateLayout();
        if (PictureSection.IsVisible || PictureSection.ActualHeight > 0)
        {
            var top = PictureSection.TransformToAncestor(DetailsPanel).Transform(default).Y;
            DetailsPanel.ScrollToVerticalOffset(Math.Max(0, DetailsPanel.VerticalOffset + top - 16));
        }
    }
}
