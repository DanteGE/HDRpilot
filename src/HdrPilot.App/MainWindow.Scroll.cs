using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App;

/// <summary>
/// Bildlaufleiste im Spalt zwischen Liste bzw. Raster und Detailbereich (<see cref="Views.Library.GapScrollBar"/>, Wunsch
/// des Nutzers 2026-10-22): welche Ansicht sie führt, der Doppelklick in den Spalt und der Zustand für Bildschirmfotos.
/// </summary>
public partial class MainWindow
{
    /// <summary>Bildschirmfoto: Spalt wie beim Überfahren (feine Linie des Trenners), ohne Maus.</summary>
    public void PreviewGapHover() => LibraryScroll.ShowsLine = true;

    /// <summary>Bildschirmfoto: in der aktiven Ansicht eine Seite weiterblättern wie per Klick unter die Pille.</summary>
    public void PageLibraryBy(int pages) => LibraryScroll.PageBy(pages);

    /// <summary>Die Leiste führt die Ansicht, die gerade scrollt: Liste oder Raster; das Regal hat keinen Spalt.</summary>
    private void ApplyLibraryScroll(LibraryViewMode view) =>
        LibraryScroll.Source = view switch
        {
            LibraryViewMode.List => GameList,
            LibraryViewMode.Grid => GameGrid,
            _ => null,
        };

    /// <summary>Doppelklick in den Spalt: wie bisher am Trenner zurück zur Standardaufteilung (nichts mehr gemerkt).</summary>
    private void OnLibraryScrollReset(object? sender, EventArgs e)
    {
        _viewModel?.SetDetailsRatio(null);
        FitDetailsColumn();
        ApplyLayout(animate: false);
    }
}
