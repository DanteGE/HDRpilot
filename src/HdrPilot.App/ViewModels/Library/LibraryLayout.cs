namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Aufteilung des Hauptfensters: Spieleliste links, Detailbereich rechts, dazwischen ein verschiebbarer Trenner.
/// Beide Spalten wachsen mit dem Fenster (Sternbreiten); gemerkt wird der Anteil des Detailbereichs.
/// </summary>
public static class LibraryLayout
{
    /// <summary>Darunter würden Zeilen der Spieleliste (Symbol, Name, Hinweise, Schalter) abgeschnitten.</summary>
    public const double ListMinWidth = 400;

    /// <summary>Darunter passen die beiden Verknüpfungs-Schaltflächen nicht mehr nebeneinander.</summary>
    public const double DetailsMinWidth = 340;

    /// <summary>Standard: wie bisher etwa 400 px Detailbereich bei der Startgröße des Fensters.</summary>
    public const double DefaultDetailsRatio = 0.35;

    /// <summary>Gespeicherte Anteile außerhalb dieses Bereichs gelten als verrutscht und werden begrenzt.</summary>
    public const double MinDetailsRatio = 0.15;

    public const double MaxDetailsRatio = 0.75;

    /// <summary>Gespeicherter Anteil, bereinigt: fehlend oder unsinnig ergibt den Standard, sonst begrenzt.</summary>
    public static double Normalize(double? ratio) =>
        ratio is { } r && double.IsFinite(r) && r > 0 && r < 1
            ? Math.Clamp(r, MinDetailsRatio, MaxDetailsRatio)
            : DefaultDetailsRatio;

    /// <summary>
    /// Anteil für die verfügbare Breite beider Spalten (ohne Trenner): so begrenzt, dass Liste und Detailbereich
    /// ihre Mindestbreite behalten. Reicht die Breite für beide nicht, werden die Mindestbreiten anteilig geteilt.
    /// </summary>
    public static double Clamp(double? ratio, double columnsWidth)
    {
        var r = Normalize(ratio);
        if (!double.IsFinite(columnsWidth) || columnsWidth <= 0)
        {
            return r;
        }

        if (columnsWidth <= ListMinWidth + DetailsMinWidth)
        {
            return DetailsMinWidth / (ListMinWidth + DetailsMinWidth);
        }

        return Math.Clamp(r, DetailsMinWidth / columnsWidth, (columnsWidth - ListMinWidth) / columnsWidth);
    }

    /// <summary>Anteil des Detailbereichs aus den tatsächlichen Spaltenbreiten (nach dem Ziehen am Trenner).</summary>
    public static double? RatioOf(double listWidth, double detailsWidth)
    {
        var total = listWidth + detailsWidth;
        return double.IsFinite(total) && total > 0 && listWidth >= 0 && detailsWidth >= 0 ? detailsWidth / total : null;
    }
}
