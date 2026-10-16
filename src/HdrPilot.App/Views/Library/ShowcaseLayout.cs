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

/// <summary>
/// Welche Spalten der Liste Platz haben (die Spalte „Spiel“ behält immer mindestens <see cref="ShowcaseLayout.MinNameColumn"/>)
/// und wie breit „Quelle“, „Zuletzt gespielt“ und „HDR“ sind (breiter nur in sehr breiten Listen, siehe
/// <see cref="ShowcaseLayout.MaxNameColumn"/>).
/// </summary>
public readonly record struct ListColumns(
    bool Source,
    bool LastPlayed,
    bool Hdr,
    double SourceWidth = ShowcaseLayout.SourceColumn,
    double LastPlayedWidth = ShowcaseLayout.LastPlayedColumn,
    double HdrWidth = ShowcaseLayout.HdrColumn);

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

    /// <summary>
    /// Kleinster Titelbereich der Liste (und des Rasters in niedrigen Fenstern): Unter dem Kopf (bis 98 px) bleiben über dem
    /// unteren Innenrand (16 px) 72 px, so hoch wie die Symbol-Kachel vor dem Namen eines Spiels ohne Logo (fest 72 × 72);
    /// Logo bzw. Name, Kennzeile, Angaben und der 50 px hohe Knopf „Starten“ brauchen weniger. Mit 180 px (66 px Platz) war
    /// der untere Rand der Kachel abgeschnitten.
    /// </summary>
    public const double MinListHero = 98 + 72 + 16;

    /// <summary>Titelbereich der Liste ohne gemerkte Höhe: ein Drittel der Fensterhöhe (Wunsch des Nutzers 2026-10-16).</summary>
    public const double DefaultListHeroRatio = 1.0 / 3;

    /// <summary>So viele Zeilen der Liste bleiben unter dem größten Titelbereich mindestens ganz zu sehen.</summary>
    public const int MinListRows = 5;

    /// <summary>Zeile der Liste (Prototyp .row, Lib.GameRow).</summary>
    public const double ListRowHeight = 46;

    /// <summary>Kopf der Liste mit den Spaltennamen samt Rahmen oben und unten (36 + 2).</summary>
    public const double ListHeaderHeight = 38;

    /// <summary>Werkzeugleiste in zwei Zeilen (Stufe 7 in schmalen Fenstern): Ansicht und Aktionen über den Filtern (40 + 8 + 40).</summary>
    public const double StackedToolbarHeight = 88;

    /// <summary>Werkzeugleiste in einer Zeile.</summary>
    public const double ToolbarHeight = 40;

    /// <summary>
    /// Luft zwischen Werkzeugleiste und Inhalt darunter (<see cref="ToolbarToBody"/> ohne die 40 px der Leiste). Genauso viel
    /// Luft bleibt in Liste und Raster über der Leiste bis zur Unterkante des Titelbilds (Wunsch des Nutzers 2026-10-16: oben
    /// so viel wie unten).
    /// </summary>
    public const double ToolbarGap = ToolbarToBody - ToolbarHeight;

    /// <summary>
    /// Ziehgriff zwischen Titelbereich und Liste (HeroSplitter): liegt mit seiner Mitte genau auf der Grenze, der Unterkante
    /// des Titelbilds (Wunsch des Nutzers 2026-10-16), und reicht je <see cref="ToolbarGap"/> darüber (in den unteren
    /// Innenrand des Titelbereichs, 16 px frei von Bedienelementen) und darunter bis zur Werkzeugleiste.
    /// </summary>
    public const double HeroSplitterHeight = 2 * ToolbarGap;

    /// <summary>So weit ragt der Ziehgriff in den Titelbereich: so weit wie darunter, damit seine Mitte auf der Grenze liegt.</summary>
    public const double HeroSplitterOverlap = ToolbarGap;

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
    /// Höhe des Titelbereichs: Liste nach <see cref="ListHeroHeight"/> (einstellbar, Standard ein Drittel der Fensterhöhe,
    /// beim Scrollen unverändert); Raster nach <see cref="GridHeroHeight"/> (clamp(H·0,24; 196; 260), in niedrigen Fenstern
    /// weniger); Regal clamp(H·0,62; 500; 660). Im Regal wächst er bei sehr hohen Fenstern (über 1122 px, etwa 3440×1440) auf
    /// H − 462 mit: Die größte Kachelreihe (340 px) samt Seitenangabe liegt dann unten an, ohne leeres Band darunter (bei
    /// H·0,62 blieben im Vollbild bis 135 px frei). In niedrigen Fenstern (etwa 1280×800 bei 110 % Skalierung, 727 px) gibt
    /// er nach, bis die kleinste Kachel ganz über der Statuszeile steht (die Reihe beginnt am unteren Rand des Titelbilds und
    /// endet <see cref="BodyBottom"/> über dem Fensterrand), höchstens bis 440 px.
    /// </summary>
    /// <param name="listRatio">Gemerkte Höhe des Titelbereichs der Liste als Anteil der Fensterhöhe (null: Standard).</param>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Liste bzw. Raster (Fehler beim Einlesen, Vorschläge; 0: keine).</param>
    /// <param name="gridRow">Raster: Werkzeugleiste und Kachel zusammen, wie angeordnet (0: Kacheln noch nicht gemessen).</param>
    public static double HeroHeight(LibraryViewMode view, double windowHeight, double? listRatio = null, double notices = 0, double gridRow = 0)
    {
        var h = double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;
        var value = view switch
        {
            LibraryViewMode.Shelf => Math.Min(
                Math.Max(Math.Clamp(h * 0.62, 500, 660), h - 462),
                Math.Max(MinShelfHero, h - BodyBottom - ShelfPadding - MinShelfTile)),
            LibraryViewMode.Grid => GridHeroHeight(h, gridRow, notices),
            _ => ListHeroHeight(h, listRatio, notices),
        };
        return Math.Round(value);
    }

    /// <summary>
    /// Titelbereich des Rasters: clamp(H·0,24; 196; 260). In niedrigen Fenstern gibt er nach, bis die erste Kachelreihe ganz
    /// über der Statuszeile steht (darüber die Werkzeugleiste mit gleicher Luft oben und unten, die Hinweise und der
    /// Innenabstand des Rasters), höchstens bis <see cref="MinListHero"/>: etwa bei 1000×620 und 1178×620 (Startgröße auf
    /// 1920×1080 bei 150 % und auf 1280×720) mit zweizeiliger Werkzeugleiste.
    /// </summary>
    /// <param name="gridRow">Werkzeugleiste (ein- oder zweizeilig) und Kachel zusammen, wie angeordnet (0: Kacheln noch nicht gemessen).</param>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Raster (0: keine).</param>
    public static double GridHeroHeight(double windowHeight, double gridRow, double notices = 0)
    {
        var h = double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;
        var hero = Math.Clamp(h * 0.24, 196, 260);
        if (double.IsFinite(gridRow) && gridRow > 0)
        {
            // Über der Leiste so viel Luft wie unter ihr bis zur ersten Kachelreihe (ToolbarTop), dann die Kachel bis zum
            // unteren Rand des Rasters.
            var below = (2 * (ToolbarGap + GridPaddingTop)) + gridRow + Notices(notices) + PaneBottom;
            hero = Math.Min(hero, Math.Max(MinListHero, Math.Floor(h - below)));
        }

        return Math.Round(hero);
    }

    /// <summary>
    /// Titelbereich der Liste (Wunsch des Nutzers 2026-10-16): gemerkter Anteil an der Fensterhöhe (Standard
    /// <see cref="DefaultListHeroRatio"/>, etwa 267 px bei 800, 360 bei 1080 und 480 bei 1440 px), in ganzen Pixeln und auf
    /// <see cref="ListHeroRange"/> begrenzt. Der Anteil bleibt gemerkt, wie er ist: In einem niedrigeren Fenster (oder mit
    /// Hinweisen über der Liste) gilt die Grenze, in einem höheren wieder die gewählte Höhe.
    /// </summary>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Liste (0: keine), siehe <see cref="ListHeroRange"/>.</param>
    public static double ListHeroHeight(double windowHeight, double? ratio, double notices = 0)
    {
        var h = double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;
        var (min, max) = ListHeroRange(h, notices);
        return Math.Clamp(Math.Round(NormalizeListHeroRatio(ratio) * h), min, max);
    }

    /// <summary>
    /// Kleinste und größte Höhe des Titelbereichs der Liste: mindestens <see cref="MinListHero"/>; höchstens so hoch, dass
    /// darunter Ziehgriff, Werkzeugleiste (auch zweizeilig, <see cref="StackedToolbarHeight"/>), die Hinweise (Fehler beim
    /// Einlesen, Vorschläge, auch aufgeklappt), Kopf der Liste, <see cref="MinListRows"/> ganze Zeilen und die Statuszeile
    /// Platz behalten. Ohne Hinweise sind das H − 416, etwa 311 px bei 727, 566 bei 982 und 893 bei 1309 px Fensterhöhe;
    /// mit einzeiliger Werkzeugleiste bleiben sechs Zeilen.
    /// </summary>
    /// <param name="notices">Höhe der Hinweise über der Liste, wie angeordnet (0: keine).</param>
    public static (double Min, double Max) ListHeroRange(double windowHeight, double notices = 0)
    {
        var h = double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;
        var below = ToolbarTop(LibraryViewMode.List, 0) + StackedToolbarHeight + Notices(notices) + ToolbarGap + ListHeaderHeight
            + (MinListRows * ListRowHeight) + PaneBottom;
        return (MinListHero, Math.Max(MinListHero, Math.Floor(h - below)));
    }

    private static double Notices(double notices) => double.IsFinite(notices) && notices > 0 ? notices : 0;

    /// <summary>Gemerkter Anteil, bereinigt: fehlend oder unsinnig (nicht zwischen 0 und 1) ergibt den Standard.</summary>
    public static double NormalizeListHeroRatio(double? ratio) =>
        ratio is { } r && double.IsFinite(r) && r > 0 && r < 1 ? r : DefaultListHeroRatio;

    /// <summary>Anteil einer Höhe des Titelbereichs an der Fensterhöhe (zum Merken nach dem Ziehen); null ohne gültige Höhe.</summary>
    public static double? ListHeroRatio(double heroHeight, double windowHeight) =>
        double.IsFinite(heroHeight) && double.IsFinite(windowHeight) && heroHeight > 0 && windowHeight > 0 ? heroHeight / windowHeight : null;

    /// <summary>
    /// Platz für Logo bzw. Name im großen Titelbereich (Regal) zwischen Kopf und Knöpfen: Titelbereich minus unterer
    /// Innenrand, Kopf samt 16 px Luft und <see cref="LargeHeroChrome"/>. Ab 190 px gilt die volle Größe.
    /// </summary>
    public static double LargeTitleRoom(double heroHeight) =>
        heroHeight - LargeHeroBottom - (HeaderTop + HeaderHeight + 16) - LargeHeroChrome;

    /// <summary>
    /// Innenabstand des Rasters über der ersten Kachelreihe (VirtualizingTilePanel, Padding oben in MainWindow.xaml): Platz
    /// für das Anheben und Leuchten der gewählten Kachel.
    /// </summary>
    public const double GridPaddingTop = 12;

    /// <summary>
    /// Oberkante der Werkzeugleiste: im Regal über dem unteren Rand des Titelbilds. In Liste und Raster unter der Unterkante
    /// des Titelbilds mit derselben Luft wie darunter bis zum Inhalt (Wunsch des Nutzers 2026-10-16: oben so viel wie
    /// unten): in der Liste <see cref="ToolbarGap"/> wie bis zum Rahmen der Liste (der Ziehgriff liegt auf der Unterkante
    /// des Titelbilds), im Raster dazu <see cref="GridPaddingTop"/> wie bis zur ersten Kachelreihe.
    /// </summary>
    public static double ToolbarTop(LibraryViewMode view, double heroHeight) => view switch
    {
        LibraryViewMode.Shelf => heroHeight - 50,
        LibraryViewMode.Grid => heroHeight + ToolbarGap + GridPaddingTop,
        _ => heroHeight + ToolbarGap,
    };

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

    /// <summary>Spalte „Quelle“ der Liste (Prototyp).</summary>
    public const double SourceColumn = 124;

    /// <summary>Spalte „Zuletzt gespielt“ der Liste (Prototyp).</summary>
    public const double LastPlayedColumn = 140;

    /// <summary>Spalte „HDR“ der Liste (Prototyp).</summary>
    public const double HdrColumn = 96;

    /// <summary>
    /// Breiteste Spalte „Spiel“: In sehr breiten Listen (Vollbild 3440 px) bekommen „Quelle“, „Zuletzt gespielt“ und „HDR“
    /// den Platz darüber zu gleichen Teilen, statt dass zwischen Name und Quelle eine leere Fläche entsteht.
    /// </summary>
    public const double MaxNameColumn = 640;

    /// <summary>
    /// Spalten der Liste (Prototyp: Vorschau 64 | Spiel | Quelle 124 | Zuletzt gespielt 140 | HDR 96 | HDR automatisch 118,
    /// Abstände 14, Innenabstand 28). Wird es eng, fallen zuerst „Quelle“, dann „HDR“, dann „Zuletzt gespielt“ weg; „Spiel“
    /// und „HDR automatisch“ bleiben immer. Wäre „Spiel“ breiter als <see cref="MaxNameColumn"/>, wachsen die übrigen
    /// sichtbaren Spalten gleichmäßig mit (ganze Pixel).
    /// </summary>
    public static ListColumns ListColumnsFor(double listWidth, double thumbnailWidth = 64)
    {
        var width = double.IsFinite(listWidth) ? listWidth : 0;
        var name = width - (thumbnailWidth + 118 + (2 * 14) + 28);
        bool source = true, hdr = true, last = true;
        name -= SourceColumn + 14 + HdrColumn + 14 + LastPlayedColumn + 14;
        if (name < MinNameColumn)
        {
            source = false;
            name += SourceColumn + 14;
        }

        if (name < MinNameColumn)
        {
            hdr = false;
            name += HdrColumn + 14;
        }

        if (name < MinNameColumn)
        {
            last = false;
        }

        var shown = (source ? 1 : 0) + (last ? 1 : 0) + (hdr ? 1 : 0);
        var extra = shown > 0 && name > MaxNameColumn ? Math.Floor((name - MaxNameColumn) / shown) : 0;
        return new ListColumns(source, last, hdr, SourceColumn + extra, LastPlayedColumn + extra, HdrColumn + extra);
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
