namespace HdrPilot.App.Views.Library;

/// <summary>Spalten und Kachelgröße des Rasters für eine Breite (alle Werte in geräteunabhängigen Pixeln).</summary>
/// <param name="Columns">Anzahl der Spalten (mindestens 2).</param>
/// <param name="TileWidth">Breite einer Kachel.</param>
/// <param name="TileHeight">Höhe einer Kachel (Hochformat 2:3, auf ganze Pixel gerundet).</param>
/// <param name="Gap">Abstand zwischen den Kacheln.</param>
public readonly record struct RasterMetrics(int Columns, double TileWidth, double TileHeight, double Gap)
{
    /// <summary>Höhe einer Zeile samt Abstand darunter.</summary>
    public double RowPitch => TileHeight + Gap;

    /// <summary>Breite, die die Kacheln einer Zeile samt Abständen belegen.</summary>
    public double UsedWidth => (Columns * TileWidth) + ((Columns - 1) * Gap);

    /// <summary>Zeilen für so viele Spiele.</summary>
    public int Rows(int count) => count <= 0 ? 0 : (count + Columns - 1) / Columns;
}

/// <summary>
/// Raster der Bibliothek (Anmerkung 1 des Nutzers, G10.5): Spaltenzahl und Kachelgröße folgen bei jeder Fensterbreite der
/// Breite, auch im Vollbild bei 3440×1440. Wird bei jeder Breitenänderung neu berechnet und übernimmt nie eine frühere
/// Kachelbreite (das war der Fehler im Prototyp v4). Invarianten: die Kacheln passen nebeneinander in die Breite, jede
/// Kachel ist höchstens <c>maxTile</c> und mindestens <c>minTile</c> breit (schmaler nur, wenn die Breite unter
/// 3 · <c>minTile</c> + 2 · <c>gap</c> liegt, im Fenster also nie) und genau im Hochformat 2:3; es gibt nie einen
/// waagerechten Bildlauf.
/// </summary>
public static class RasterLayout
{
    public const double DefaultGap = 16;
    public const double DefaultMinTile = 150;
    public const double DefaultMaxTile = 220;

    /// <summary>Hochformat der Kacheln (Höhe / Breite).</summary>
    public const double AspectRatio = 1.5;

    public static RasterMetrics Compute(double availableWidth, double gap = DefaultGap, double minTile = DefaultMinTile, double maxTile = DefaultMaxTile)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            availableWidth = 0;
        }

        gap = Math.Max(0, gap);
        minTile = Math.Max(1, minTile);
        maxTile = Math.Max(minTile, maxTile);

        var columns = Math.Max(2, (int)Math.Floor((availableWidth + gap) / (minTile + gap)));
        var tile = TileWidth(availableWidth, columns, gap);
        while (tile > maxTile)
        {
            columns++;
            tile = TileWidth(availableWidth, columns, gap);
        }

        // Auf gerade ganze Pixel abrunden: keine halben Pixel an den Kanten, die Summe bleibt sicher in der Breite, und die
        // Höhe ist genau das 1,5-Fache (2:3 ohne Rundung).
        tile = Math.Max(0, 2 * Math.Floor(tile / 2));
        return new RasterMetrics(columns, tile, tile * AspectRatio, gap);
    }

    private static double TileWidth(double width, int columns, double gap) => (width - ((columns - 1) * gap)) / columns;
}
