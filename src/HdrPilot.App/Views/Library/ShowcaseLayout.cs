using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>Maße der Kachelreihe im Regal.</summary>
/// <param name="TileWidth">Breite einer Kachel (2:3 zur Höhe).</param>
/// <param name="TileHeight">Höhe einer Kachel.</param>
/// <param name="Visible">Wie viele Kacheln ganz in die Breite passen (mindestens 2), für Blättern und „1–7 von 651“.</param>
public readonly record struct ShelfMetrics(double TileWidth, double TileHeight, double Gap, int Visible)
{
    public double Pitch => TileWidth + Gap;
}

/// <summary>Welche Spalten der Liste Platz haben (die Spalte „Spiel“ behält immer mindestens <see cref="ShowcaseLayout.MinNameColumn"/>).</summary>
public readonly record struct ListColumns(bool Source, bool LastPlayed, bool Hdr);

/// <summary>
/// Maße des Hauptfensters im Stil „Showcase Hero“ (Prototyp v4, app.css), als reine Funktionen: Höhe des Titelbereichs je
/// Ansicht, Lage der Werkzeugleiste, Breite des Detailbereichs, Kachelreihe im Regal, Spalten der Liste, Lage des
/// Spitzlichts im HDR-Moment. Alle Werte in geräteunabhängigen Pixeln.
/// </summary>
public static class ShowcaseLayout
{
    /// <summary>Außenabstand links und rechts (--pad).</summary>
    public const double Pad = 32;

    /// <summary>Oberkante der Kopfzeile (unter der 32-px-Titelleiste).</summary>
    public const double HeaderTop = 40;

    public const double HeaderHeight = 52;

    /// <summary>Oberkante des Detailbereichs und der Automatik-Karte.</summary>
    public const double PaneTop = 104;

    /// <summary>Abstand des Detailbereichs vom unteren Rand (Statuszeile darunter).</summary>
    public const double PaneBottom = 40;

    /// <summary>Abstand des Inhalts vom unteren Rand.</summary>
    public const double BodyBottom = 38;

    /// <summary>Abstand zwischen Werkzeugleiste und Inhalt (Werkzeugleiste 40 px + 10 px Luft).</summary>
    public const double ToolbarToBody = 50;

    /// <summary>Lücke zwischen Liste/Werkzeugleiste und Detailbereich.</summary>
    public const double PaneGap = 24;

    /// <summary>Höhe des eingeklappten Titelbereichs, wenn die Liste gescrollt ist.</summary>
    public const double CollapsedListHero = 120;

    /// <summary>Unterhalb dieser Fensterbreite: Kennzahlen im Kopf aus, Bildschirm-Kapsel kompakt, Ansicht-Text in der Statuszeile aus.</summary>
    public const double CompactWidth = 1366;

    /// <summary>Unterhalb dieser Fensterbreite: Tastenhinweise in der Statuszeile aus.</summary>
    public const double KeyHintsWidth = 1500;

    /// <summary>Mindestbreite der Spalte „Spiel“ in der Liste.</summary>
    public const double MinNameColumn = 180;

    /// <summary>
    /// Breitester Detailbereich in Liste und Raster (E89): Der Prototyp hat 340–396 px; mit dem Trenner darf er breiter werden,
    /// aber nie so breit, dass er bei 3440 px oder einer alten gespeicherten Aufteilung die Hälfte des Fensters füllt.
    /// </summary>
    public const double MaxPaneWidth = 560;

    /// <summary>Kleinste Kachel im Regal (Prototyp: clamp(h − 34; 200; 340)).</summary>
    public const double MinShelfTile = 200;

    /// <summary>Kachel im Regal, wenn selbst der kleinste Titelbereich keine 200 px übrig lässt (Fensterhöhe unter 712 px).</summary>
    public const double TinyShelfTile = 100;

    /// <summary>
    /// Kleinster Titelbereich im Regal, wenn das Fenster für 500 px samt Kachelreihe zu niedrig ist: Die Werkzeugleiste
    /// (50 px über dem unteren Rand) bleibt unter der Automatik-Karte (unterer Rand bei etwa 384 px).
    /// </summary>
    public const double MinShelfHero = 440;

    /// <summary>Unterer Innenrand des großen Titelbereichs (Regal) unter den Knöpfen.</summary>
    public const double LargeHeroBottom = 74;

    /// <summary>Kennzeile, Angaben und Knöpfe des großen Titelbereichs samt Abständen (ohne Logo bzw. Name).</summary>
    public const double LargeHeroChrome = 150;

    /// <summary>Höchste Höhe von Logo bzw. Name im großen Titelbereich (Viewbox des Prototyps).</summary>
    public const double LargeTitleHeight = 190;

    /// <summary>
    /// Höhe des Titelbereichs: Liste clamp(H·0,22; 180; 240), beim Scrollen 120; Raster clamp(H·0,24; 196; 260); Regal
    /// clamp(H·0,62; 500; 660). Im Regal darf er bei sehr hohen Fenstern (über 1122 px, etwa 3440×1440) mitwachsen, damit
    /// unter der Kachelreihe keine leere Fläche bleibt: Obergrenze dort H − 462. In niedrigen Fenstern (etwa 1280×800 bei
    /// 110 % Skalierung, 727 px) gibt er nach, bis die kleinste Kachel ganz über der Statuszeile steht (die Reihe beginnt am
    /// unteren Rand des Titelbilds und endet <see cref="BodyBottom"/> über dem Fensterrand), höchstens bis 440 px.
    /// </summary>
    public static double HeroHeight(LibraryViewMode view, double windowHeight, bool listCollapsed)
    {
        var h = double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;
        var value = view switch
        {
            LibraryViewMode.Shelf => Math.Min(
                Math.Clamp(h * 0.62, 500, Math.Max(660, h - 462)),
                Math.Max(MinShelfHero, h - BodyBottom - ShelfPadding - MinShelfTile)),
            LibraryViewMode.Grid => Math.Clamp(h * 0.24, 196, 260),
            _ => listCollapsed ? CollapsedListHero : Math.Clamp(h * 0.22, 180, 240),
        };
        return Math.Round(value);
    }

    /// <summary>
    /// Platz für Logo bzw. Name im großen Titelbereich (Regal) zwischen Kopf und Knöpfen: Titelbereich minus unterer
    /// Innenrand, Kopf samt 16 px Luft und <see cref="LargeHeroChrome"/>. Ab 190 px gilt die volle Größe.
    /// </summary>
    public static double LargeTitleRoom(double heroHeight) =>
        heroHeight - LargeHeroBottom - (HeaderTop + HeaderHeight + 16) - LargeHeroChrome;

    /// <summary>Oberkante der Werkzeugleiste: im Regal über dem unteren Rand des Titelbilds, sonst direkt darunter.</summary>
    public static double ToolbarTop(LibraryViewMode view, double heroHeight) => view == LibraryViewMode.Shelf ? heroHeight - 50 : heroHeight - 2;

    /// <summary>Breite des Detailbereichs ohne gemerkte Aufteilung: clamp(23 % der Fensterbreite; 340; 396).</summary>
    public static double PaneWidth(double windowWidth) => Math.Round(Math.Clamp((double.IsFinite(windowWidth) ? windowWidth : 1600) * 0.23, 340, 396));

    /// <summary>Innenabstand der Kachelreihe oben und unten zusammen (12 + 22).</summary>
    public const double ShelfPadding = 34;

    /// <summary>
    /// Kachelreihe im Regal: Höhe clamp(h − 34; 200; 340), Breite 2/3 davon, sichtbare Kacheln nach der Breite. Ist die
    /// Reihe niedriger als die kleinste Kachel (nur in sehr niedrigen Fenstern), wird die Kachel kleiner statt abgeschnitten,
    /// höchstens bis 100 px.
    /// </summary>
    public static ShelfMetrics Shelf(double width, double height, double gap = 18)
    {
        var available = (double.IsFinite(height) ? height : 0) - ShelfPadding;
        var tileHeight = Math.Round(available >= MinShelfTile ? Math.Min(available, 340) : Math.Max(available, TinyShelfTile));
        var tileWidth = Math.Round(tileHeight * 2 / 3);
        var usable = (double.IsFinite(width) ? width : 0) - (2 * Pad);
        var visible = Math.Max(2, (int)Math.Floor((usable + gap) / (tileWidth + gap)));
        return new ShelfMetrics(tileWidth, tileHeight, gap, visible);
    }

    /// <summary>
    /// Spalten der Liste (Prototyp: Vorschau 64 | Spiel | Quelle 124 | Zuletzt gespielt 140 | HDR 96 | HDR automatisch 118,
    /// Abstände 14, Innenabstand 28). Wird es eng, fallen zuerst „Quelle“, dann „HDR“, dann „Zuletzt gespielt“ weg; „Spiel“
    /// und „HDR automatisch“ bleiben immer.
    /// </summary>
    public static ListColumns ListColumnsFor(double listWidth, double thumbnailWidth = 64)
    {
        var width = double.IsFinite(listWidth) ? listWidth : 0;
        var name = width - (thumbnailWidth + 118 + (2 * 14) + 28);
        bool source = true, hdr = true, last = true;
        name -= 124 + 14 + 96 + 14 + 140 + 14;
        if (name < MinNameColumn)
        {
            source = false;
            name += 124 + 14;
        }

        if (name < MinNameColumn)
        {
            hdr = false;
            name += 96 + 14;
        }

        if (name < MinNameColumn)
        {
            last = false;
        }

        return new ListColumns(source, last, hdr);
    }

    /// <summary>
    /// Spitzlicht des HDR-Moments im Titelbereich (Prototyp placeLight): waagerecht auf 30–70 % und links vom Detailbereich
    /// bzw. der Automatik-Karte geklemmt, senkrecht unter der Kopfzeile (mindestens 112 px) und über dem unteren Rand.
    /// </summary>
    /// <param name="lightX">Gemessene Lage 0–1 (AmbientColors.LightX).</param>
    /// <param name="lightY">Gemessene Lage 0–1 (AmbientColors.LightY).</param>
    /// <param name="blockerLeft">Linke Kante des Detailbereichs bzw. der Karte im Titelbereich (px); NaN ohne.</param>
    public static (double X, double Y) LightPosition(double heroWidth, double heroHeight, double lightX, double lightY, double blockerLeft)
    {
        var w = heroWidth > 0 ? heroWidth : 1600;
        var h = heroHeight > 0 ? heroHeight : 300;
        var right = double.IsFinite(blockerLeft) ? Math.Min(0.7, (blockerLeft - 60) / w) : 0.7;
        var x = Math.Clamp(double.IsFinite(lightX) ? lightX : 0.62, 0.3, Math.Max(0.3, right));
        var yMin = Math.Max(112, h * 0.3);
        var yMax = Math.Max(yMin, Math.Min(h - 26, h * 0.78));
        var y = Math.Clamp((double.IsFinite(lightY) ? lightY : 0.45) * h, yMin, yMax);
        return (x * w, y);
    }
}
