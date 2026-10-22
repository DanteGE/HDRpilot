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

    /// <summary>
    /// Werkzeugleiste in einer Zeile. Erst unter einer Listenbreite von 806 bricht sie in zwei Zeilen um (ToolbarLayout,
    /// Wunsch des Nutzers 2026-10-17), je Zeile 40 px und 8 px dazwischen; was davon abhängt, nimmt ihre ausgeklappte Höhe.
    /// </summary>
    public const double ToolbarHeight = 40;

    /// <summary>
    /// Luft zwischen Bildkante (Unterkante des Titelbilds) und Werkzeugleiste: Die Lasche „Leiste ausblenden“ liegt mittig auf
    /// der Bildkante, die Leiste <see cref="TabGap"/> unter ihr (Wunsch des Nutzers 2026-10-22; vorher 10 px, die Lasche saß
    /// dann −16 … +2 um die Kante). In Liste und Raster liegt die Leiste darunter, mit genauso viel Luft bis zum Inhalt (Wunsch
    /// des Nutzers 2026-10-16: oben so viel wie unten, bei jeder Zahl von Zeilen): 17 px über der Leiste, 17 px bis zum Rahmen
    /// der Liste bzw. bis zur ersten Kachelreihe. Im Regal liegt die Leiste darüber, ihre letzte Zeile endet 17 px über der
    /// Bildkante.
    /// </summary>
    public const double ToolbarGap = (TabHeight / 2) + TabGap;

    /// <summary>Eingeklappt in Liste und Raster: Luft zwischen der Kapsel auf der Bildkante und dem Inhalt darunter.</summary>
    public const double CollapsedGap = 10;

    /// <summary>
    /// Ziehgriff zwischen Titelbereich und Inhalt (HeroSplitter, in allen drei Ansichten): liegt mit seiner Mitte genau auf
    /// der Grenze, der Unterkante des Titelbilds (Wunsch des Nutzers 2026-10-16), und reicht je 10 px darüber (in den unteren
    /// Innenrand des Titelbereichs, 16 px frei von Bedienelementen; im Regal bis 7 px unter die Werkzeugleiste) und darunter
    /// (in Liste und Raster bis 7 px vor die Werkzeugleiste, im Regal bis 2 px vor die Kachelreihe).
    /// </summary>
    public const double HeroSplitterHeight = 20;

    /// <summary>So weit ragt der Ziehgriff in den Titelbereich: so weit wie darunter, damit seine Mitte auf der Grenze liegt.</summary>
    public const double HeroSplitterOverlap = HeroSplitterHeight / 2;

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

    /// <summary>
    /// Kachel im Regal, wenn selbst der kleinste Titelbereich keine 200 px übrig lässt (Fensterhöhe unter 723 px): höchstens
    /// so viel kleiner. Im kleinsten Fenster (620 px hoch) bleiben 97 px; vorher 100, seit die Werkzeugleiste des Regals 7 px
    /// höher steht (die Lasche liegt mittig auf der Bildkante, <see cref="MinShelfHero"/>).
    /// </summary>
    public const double TinyShelfTile = 96;

    /// <summary>
    /// Kleinster Titelbereich im Regal (auch beim Ziehen am Griff): Die Werkzeugleiste (57 px über dem unteren Rand, oberste
    /// Zeile ab 390 px) bleibt unter der Automatik-Karte, Logo bzw. Name behalten über den Knöpfen 108 px. Vorher 440 mit der
    /// Leiste 50 px über dem Rand; seit die Lasche mittig auf der Bildkante liegt (Wunsch des Nutzers 2026-10-22), steht die
    /// Leiste 7 px höher. Mit umgebrochener Werkzeugleiste um deren zusätzliche Zeilen mehr (<see cref="ToolbarExtra"/>),
    /// siehe <see cref="ToolbarTop"/>.
    /// </summary>
    public const double MinShelfHero = 447;

    /// <summary>
    /// Unterer Innenrand des großen Titelbereichs (Regal) unter den Knöpfen, mit einzeiliger Werkzeugleiste: 24 px Luft über
    /// der Leiste, die Leiste und ihre Luft bis zur Bildkante (<see cref="ToolbarGap"/>); mit umgebrochener Leiste um deren
    /// zusätzliche Zeilen mehr (<see cref="LargeHeroInset"/>).
    /// </summary>
    public const double LargeHeroBottom = 24 + ToolbarHeight + ToolbarGap;

    /// <summary>Kennzeile, Angaben und Knöpfe des großen Titelbereichs samt Abständen (ohne Logo bzw. Name).</summary>
    public const double LargeHeroChrome = 150;

    /// <summary>Höchste Höhe von Logo bzw. Name im großen Titelbereich (Viewbox des Prototyps).</summary>
    public const double LargeTitleHeight = 190;

    /// <summary>
    /// Höhe des Titelbereichs je Ansicht, in ganzen Pixeln: mit gemerktem Anteil an der Fensterhöhe (Ziehgriff, jede Ansicht
    /// ihren eigenen, Wunsch des Nutzers 2026-10-16 und 2026-10-22) dieser, sonst der Standard der Ansicht, begrenzt auf
    /// <see cref="HeroRange"/>. Liste: Standard ein Drittel der Fensterhöhe, beim Scrollen unverändert
    /// (<see cref="ListHeroHeight"/>). Raster: Standard clamp(H·0,24; 196; 260), in niedrigen Fenstern weniger
    /// (<see cref="GridHeroHeight"/>). Regal: Standard clamp(H·0,62; 500; 660); bei sehr hohen Fenstern (über 1122 px, etwa
    /// 3440×1440) wächst er auf H − 462 mit, damit unter der größten Kachelreihe (340 px) samt Seitenangabe kein leeres Band
    /// bleibt (bei H·0,62 blieben im Vollbild bis 135 px frei); in niedrigen Fenstern (etwa 1280×800 bei 110 % Skalierung,
    /// 727 px) gibt er nach, bis die kleinste Kachel ganz über der Statuszeile steht (<see cref="ShelfHeroRange"/>). Im Regal
    /// ist die Werkzeugleiste bei jeder erlaubten Fenstergröße einzeilig (Listenbreite mindestens 934,
    /// <see cref="ToolbarLayout"/>); wäre sie höher, wüchse sie nach oben in das Titelbild und der kleinste Titelbereich um
    /// ihre zusätzlichen Zeilen (<see cref="ToolbarExtra"/>). Ein- und Ausklappen der Leiste ändert den Titelbereich nie:
    /// Gerechnet wird immer mit der ausgeklappten Höhe.
    /// </summary>
    /// <param name="ratio">Gemerkte Höhe des Titelbereichs dieser Ansicht als Anteil der Fensterhöhe (null oder unsinnig: Standard).</param>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Liste bzw. Raster (Fehler beim Einlesen, Vorschläge; 0: keine).</param>
    /// <param name="gridRow">Raster: Werkzeugleiste und Kachel zusammen, wie angeordnet (0: Kacheln noch nicht gemessen).</param>
    /// <param name="toolbarHeight">Höhe der ausgeklappten Werkzeugleiste (eine Zeile: <see cref="ToolbarHeight"/>); auch eingeklappt, damit der Titelbereich beim Umschalten nicht springt.</param>
    public static double HeroHeight(
        LibraryViewMode view,
        double windowHeight,
        double? ratio = null,
        double notices = 0,
        double gridRow = 0,
        double toolbarHeight = ToolbarHeight)
    {
        var h = Height(windowHeight);
        return view switch
        {
            LibraryViewMode.Shelf => ShelfHeroHeight(h, ratio, toolbarHeight),
            LibraryViewMode.Grid => GridHeroHeight(h, gridRow, notices, ratio),
            _ => ListHeroHeight(h, ratio, notices, toolbarHeight),
        };
    }

    /// <summary>
    /// Bereich des Ziehgriffs je Ansicht (kleinste und größte Höhe des Titelbereichs): <see cref="ListHeroRange"/>,
    /// <see cref="GridHeroRange"/> bzw. <see cref="ShelfHeroRange"/>.
    /// </summary>
    public static (double Min, double Max) HeroRange(
        LibraryViewMode view,
        double windowHeight,
        double notices = 0,
        double gridRow = 0,
        double toolbarHeight = ToolbarHeight) => view switch
        {
            LibraryViewMode.Shelf => ShelfHeroRange(windowHeight, toolbarHeight),
            LibraryViewMode.Grid => GridHeroRange(windowHeight, gridRow, notices),
            _ => ListHeroRange(windowHeight, notices, toolbarHeight),
        };

    /// <summary>
    /// Titelbereich des Rasters: gemerkter Anteil oder Standard clamp(H·0,24; 196; 260), begrenzt auf
    /// <see cref="GridHeroRange"/>. In niedrigen Fenstern gibt er nach, bis die erste Kachelreihe ganz über der Statuszeile
    /// steht, höchstens bis <see cref="MinListHero"/>: etwa bei 1000×620 und 1178×620 (Startgröße auf 1920×1080 bei 150 % und
    /// auf 1280×720) mit umgebrochener Werkzeugleiste.
    /// </summary>
    /// <param name="gridRow">Werkzeugleiste (in ihrer tatsächlichen Höhe) und Kachel zusammen, wie angeordnet (0: Kacheln noch nicht gemessen).</param>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Raster (0: keine).</param>
    /// <param name="ratio">Gemerkte Höhe als Anteil der Fensterhöhe (null oder unsinnig: Standard).</param>
    public static double GridHeroHeight(double windowHeight, double gridRow, double notices = 0, double? ratio = null)
    {
        var h = Height(windowHeight);
        var (min, max) = GridHeroRange(h, gridRow, notices);
        var value = NormalizeHeroRatio(ratio) is { } r ? r * h : Math.Clamp(h * 0.24, 196, 260);
        return Math.Clamp(Math.Round(value), min, max);
    }

    /// <summary>
    /// Kleinste und größte Höhe des Titelbereichs im Raster: mindestens <see cref="MinListHero"/>; höchstens so hoch, dass die
    /// erste Kachelreihe ganz über der Statuszeile steht (darüber die Werkzeugleiste mit gleicher Luft oben und unten, die
    /// Hinweise und der Innenabstand des Rasters; Wunsch des Nutzers 2026-10-22: eine ganze Reihe bleibt immer zu sehen).
    /// Solange die Kacheln nicht gemessen sind, gibt es keine obere Grenze.
    /// </summary>
    /// <param name="gridRow">Werkzeugleiste (in ihrer tatsächlichen Höhe) und Kachel zusammen, wie angeordnet (0: Kacheln noch nicht gemessen).</param>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Raster (0: keine).</param>
    public static (double Min, double Max) GridHeroRange(double windowHeight, double gridRow, double notices = 0)
    {
        var h = Height(windowHeight);
        if (!double.IsFinite(gridRow) || gridRow <= 0)
        {
            return (MinListHero, double.PositiveInfinity);
        }

        // Über der Leiste so viel Luft wie unter ihr bis zur ersten Kachelreihe (ToolbarTop), dann die Kachel bis zum unteren
        // Rand des Rasters.
        var below = (2 * ToolbarGap) + gridRow + Notices(notices) + PaneBottom;
        return (MinListHero, Math.Max(MinListHero, Math.Floor(h - below)));
    }

    /// <summary>
    /// Titelbereich des Regals: gemerkter Anteil oder der Standard (siehe <see cref="HeroHeight"/>), begrenzt auf
    /// <see cref="ShelfHeroRange"/>.
    /// </summary>
    /// <param name="ratio">Gemerkte Höhe als Anteil der Fensterhöhe (null oder unsinnig: Standard).</param>
    /// <param name="toolbarHeight">Höhe der ausgeklappten Werkzeugleiste (im Regal immer eine Zeile).</param>
    public static double ShelfHeroHeight(double windowHeight, double? ratio, double toolbarHeight = ToolbarHeight)
    {
        var h = Height(windowHeight);
        var (min, max) = ShelfHeroRange(h, toolbarHeight);
        var value = NormalizeHeroRatio(ratio) is { } r ? r * h : Math.Max(Math.Clamp(h * 0.62, 500, 660), h - 462);
        return Math.Clamp(Math.Round(value), min, max);
    }

    /// <summary>
    /// Kleinste und größte Höhe des Titelbereichs im Regal (Wunsch des Nutzers 2026-10-22: Titelbereich gegen Kachelgröße):
    /// mindestens <see cref="MinShelfHero"/> (die Werkzeugleiste bleibt unter der Automatik-Karte, der große Titel behält
    /// Platz); höchstens so hoch, dass die kleinste Kachel (<see cref="MinShelfTile"/>) ganz über der Statuszeile steht
    /// (H − 274: die Kachelreihe hat 12 px über und 22 px unter der Kachel, darunter 40 px bis zum Fensterrand). In niedrigen
    /// Fenstern (Inhalt unter 721 px hoch) bleibt nur das Minimum, die Kacheln werden dann kleiner (höchstens bis
    /// <see cref="TinyShelfTile"/>).
    /// </summary>
    /// <param name="toolbarHeight">Höhe der ausgeklappten Werkzeugleiste (im Regal immer eine Zeile).</param>
    public static (double Min, double Max) ShelfHeroRange(double windowHeight, double toolbarHeight = ToolbarHeight)
    {
        var h = Height(windowHeight);
        var min = MinShelfHero + ToolbarExtra(toolbarHeight);
        return (min, Math.Max(min, Math.Floor(h - ShelfRowChrome - MinShelfTile)));
    }

    /// <summary>
    /// Titelbereich der Liste (Wunsch des Nutzers 2026-10-16): gemerkter Anteil an der Fensterhöhe (Standard
    /// <see cref="DefaultListHeroRatio"/>, etwa 267 px bei 800, 360 bei 1080 und 480 bei 1440 px), in ganzen Pixeln und auf
    /// <see cref="ListHeroRange"/> begrenzt. Der Anteil bleibt gemerkt, wie er ist: In einem niedrigeren Fenster (oder mit
    /// Hinweisen über der Liste bzw. mehr Zeilen der Werkzeugleiste) gilt die Grenze, sonst wieder die gewählte Höhe.
    /// </summary>
    /// <param name="notices">Hinweise zwischen Werkzeugleiste und Liste (0: keine), siehe <see cref="ListHeroRange"/>.</param>
    /// <param name="toolbarHeight">Höhe der Werkzeugleiste, wie angeordnet (eine Zeile: <see cref="ToolbarHeight"/>).</param>
    public static double ListHeroHeight(double windowHeight, double? ratio, double notices = 0, double toolbarHeight = ToolbarHeight)
    {
        var h = Height(windowHeight);
        var (min, max) = ListHeroRange(h, notices, toolbarHeight);
        return Math.Clamp(Math.Round(NormalizeListHeroRatio(ratio) * h), min, max);
    }

    /// <summary>
    /// Kleinste und größte Höhe des Titelbereichs der Liste: mindestens <see cref="MinListHero"/>; höchstens so hoch, dass
    /// darunter die Werkzeugleiste in ihrer tatsächlichen Höhe (eine bis drei Zeilen, in der schmalsten Liste mehr) mit ihrer
    /// Luft oben und unten (je <see cref="ToolbarGap"/>), die Hinweise (Fehler beim Einlesen, Vorschläge, auch aufgeklappt),
    /// Kopf der Liste, <see cref="MinListRows"/> ganze Zeilen und die Statuszeile Platz behalten. Ohne Hinweise sind das
    /// H − 342 − Höhe der Leiste: H − 382 mit einer Zeile, H − 430 mit zwei (etwa 297 px bei 727 und 552 bei 982 px
    /// Fensterhöhe), H − 478 mit drei.
    /// </summary>
    /// <param name="notices">Höhe der Hinweise über der Liste, wie angeordnet (0: keine).</param>
    /// <param name="toolbarHeight">Höhe der Werkzeugleiste, wie angeordnet (eine Zeile: <see cref="ToolbarHeight"/>).</param>
    public static (double Min, double Max) ListHeroRange(double windowHeight, double notices = 0, double toolbarHeight = ToolbarHeight)
    {
        var h = Height(windowHeight);
        var below = ToolbarTop(LibraryViewMode.List, 0) + ToolbarHeight + ToolbarExtra(toolbarHeight) + Notices(notices) + ToolbarGap
            + ListHeaderHeight + (MinListRows * ListRowHeight) + PaneBottom;
        return (MinListHero, Math.Max(MinListHero, Math.Floor(h - below)));
    }

    /// <summary>Fensterhöhe für die Rechnungen: fehlend oder unsinnig (noch nicht angeordnet) 1000.</summary>
    private static double Height(double windowHeight) => double.IsFinite(windowHeight) && windowHeight > 0 ? windowHeight : 1000;

    private static double Notices(double notices) => double.IsFinite(notices) && notices > 0 ? notices : 0;

    /// <summary>
    /// Wie viel höher die Werkzeugleiste ist als eine Zeile (umgebrochen: 48 px je weitere Zeile); 0 für eine Zeile und für
    /// unsinnige Werte (noch nicht angeordnet).
    /// </summary>
    public static double ToolbarExtra(double toolbarHeight) =>
        double.IsFinite(toolbarHeight) && toolbarHeight > ToolbarHeight ? toolbarHeight - ToolbarHeight : 0;

    /// <summary>
    /// Regal mit eingeklappter Werkzeugleiste: Knöpfe und Logo sinken (unterer Innenrand 81 → 49), die Kachelreihe bleibt.
    /// Die Knöpfe enden dann auf der Oberkante der Kapsel, die Lichtnaht läuft 12 px unter ihnen (bei den 40 des Entwurfs
    /// wären es 4 gewesen, die Naht hätte die Knöpfe unterstrichen).
    /// </summary>
    public const double LargeHeroSink = 32;

    /// <summary>Abstand zwischen Lasche und Werkzeugleiste: so viel wie zwischen den Knöpfen (Wunsch des Nutzers 2026-10-17).</summary>
    public const double TabGap = 8;

    /// <summary>Lasche der ausgeklappten Leiste (sichtbar 36 × 18), mittig auf der Bildkante.</summary>
    public const double TabHeight = 18;

    /// <summary>Kapsel der eingeklappten Leiste: 24 hoch, in Liste und Raster mittig auf der Bildkante.</summary>
    public const double CapsuleHeight = 24;

    /// <summary>
    /// Eingeklappt in Liste und Raster: Inhalt so weit unter der Bildkante, dass er <see cref="CollapsedGap"/> unter der Kapsel
    /// beginnt (Rahmen der Liste bzw. Oberkante des Rasters; die erste Kachelreihe folgt nach dem Innenabstand des Rasters).
    /// </summary>
    public const double CollapsedBodyOffset = (CapsuleHeight / 2) + CollapsedGap;

    /// <summary>
    /// Innenabstand des Rasters über der ersten Kachelreihe (VirtualizingTilePanel, Padding oben in MainWindow.xaml): Platz
    /// für den Ring der gewählten Kachel. Der Rest der Luft unter der Werkzeugleiste (<see cref="BodyGap"/>) liegt über dem
    /// Raster, damit gescrollte Kacheln 10 px unter der Leiste enden. Kacheln ohne Platz nach oben (die erste Reihe, nach dem
    /// Scrollen die oberste) wachsen beim Überfahren nach unten (VirtualizingTilePanel.RisesOnHover, Wunsch des Nutzers
    /// 2026-10-22: sie verschwanden sonst halb unter der Leiste).
    /// </summary>
    public const double GridPaddingTop = 7;

    /// <summary>
    /// Innenabstand der Kachelreihe im Regal über der Kachel: Sie beginnt 12 px unter der Bildkante, ihre Liste schon an der
    /// Unterkante der Werkzeugleiste (<see cref="ToolbarGap"/> darüber). So hebt sich eine Kachel beim Überfahren ganz
    /// (−10 px, 3,5 % größer, Ring), ohne oben abgeschnitten zu werden (Wunsch des Nutzers 2026-10-22).
    /// </summary>
    public const double ShelfPaddingTop = ToolbarGap + 12;

    /// <summary>
    /// So weit reicht die Kachelreihe des Regals unter den unteren Rand der Spalten (bis an die Statuszeile): Platz für das
    /// Leuchten unter der gewählten Kachel (32 px unter ihr), das vorher 16 px unter der Kachel hart abbrach (Wunsch des
    /// Nutzers 2026-10-22).
    /// </summary>
    public const double ShelfGlowRoom = 10;

    /// <summary>
    /// Luft zwischen Werkzeugleiste und Inhalt (Liste, Raster, Kachelreihe), wie sie über dem Inhalt im Layout liegt: Liste
    /// <see cref="ToolbarGap"/> bis zu ihrem Rahmen; Raster bis zu seiner Oberkante, die erste Kachelreihe folgt nach
    /// <see cref="GridPaddingTop"/> (zusammen wieder <see cref="ToolbarGap"/>); im Regal keine, seine Kachelreihe beginnt an
    /// der Unterkante der Leiste und hat die Luft als Innenabstand (<see cref="ShelfPaddingTop"/>).
    /// </summary>
    public static double BodyGap(LibraryViewMode view) => view switch
    {
        LibraryViewMode.Shelf => 0,
        LibraryViewMode.Grid => ToolbarGap - GridPaddingTop,
        _ => ToolbarGap,
    };

    /// <summary>
    /// Oberkante des Inhalts unter der Werkzeugleiste (Rahmen der Liste, Oberkante des Rasters bzw. der Kachelreihe, ohne
    /// Hinweise): ausgeklappt <see cref="BodyGap"/> unter der Leiste, eingeklappt in Liste und Raster
    /// <see cref="CollapsedBodyOffset"/> unter der Bildkante (10 px unter der Kapsel). Im Regal bleibt die Kachelreihe, wo sie
    /// ist (die Kapsel steht in der früheren Leistenzeile).
    /// </summary>
    /// <param name="toolbarHeight">Höhe der ausgeklappten Werkzeugleiste.</param>
    public static double BodyTop(LibraryViewMode view, double heroHeight, double toolbarHeight, bool collapsed) =>
        collapsed && view != LibraryViewMode.Shelf
            ? heroHeight + CollapsedBodyOffset
            : ToolbarTop(view, heroHeight, toolbarHeight) + toolbarHeight + BodyGap(view);

    /// <summary>
    /// Oberkante der Kapsel (eingeklappt): in Liste und Raster mittig auf der Bildkante wie die Lasche (das Ein- und
    /// Ausklappen ändert dort nur die Größe), im Regal mittig in der früheren Zeile der Werkzeugleiste (unter der
    /// Automatik-Karte, rechts).
    /// </summary>
    public static double CapsuleTop(LibraryViewMode view, double heroHeight, double toolbarHeight) =>
        view == LibraryViewMode.Shelf
            ? ToolbarTop(view, heroHeight, toolbarHeight) + Math.Round((ToolbarHeight - CapsuleHeight) / 2)
            : heroHeight - (CapsuleHeight / 2);

    /// <summary>
    /// Oberkante der Lasche (ausgeklappt): in allen Ansichten mittig auf der Bildkante, auf der auch Ziehgriff und Lichtnaht
    /// liegen (Wunsch des Nutzers 2026-10-22; vorher Liste −16 … +2, Raster −4 … +14 um die Kante). Die Werkzeugleiste liegt
    /// <see cref="TabGap"/> unter ihr (Liste, Raster) bzw. über ihr (Regal).
    /// </summary>
    public static double TabTop(double heroHeight) => heroHeight - (TabHeight / 2);

    /// <summary>Gemerkter Anteil der Liste, bereinigt: fehlend oder unsinnig (nicht zwischen 0 und 1) ergibt den Standard.</summary>
    public static double NormalizeListHeroRatio(double? ratio) => NormalizeHeroRatio(ratio) ?? DefaultListHeroRatio;

    /// <summary>Gemerkter Anteil einer Ansicht, bereinigt: fehlend oder unsinnig (nicht zwischen 0 und 1) ergibt null, den Standard der Ansicht.</summary>
    public static double? NormalizeHeroRatio(double? ratio) =>
        ratio is { } r && double.IsFinite(r) && r > 0 && r < 1 ? r : null;

    /// <summary>Anteil einer Höhe des Titelbereichs an der Fensterhöhe (zum Merken nach dem Ziehen); null ohne gültige Höhe.</summary>
    public static double? HeroRatio(double heroHeight, double windowHeight) =>
        double.IsFinite(heroHeight) && double.IsFinite(windowHeight) && heroHeight > 0 && windowHeight > 0 ? heroHeight / windowHeight : null;

    /// <summary>
    /// Unterer Innenrand des großen Titelbereichs (Regal) unter den Knöpfen: <see cref="LargeHeroBottom"/>, mit umgebrochener
    /// Werkzeugleiste um ihre zusätzlichen Zeilen mehr. So bleiben zwischen Knöpfen und Leiste immer 24 px.
    /// </summary>
    public static double LargeHeroInset(double toolbarHeight = ToolbarHeight) => LargeHeroBottom + ToolbarExtra(toolbarHeight);

    /// <summary>
    /// Platz für Logo bzw. Name im großen Titelbereich (Regal) zwischen Kopf und Knöpfen: Titelbereich minus unterer
    /// Innenrand (<see cref="LargeHeroInset"/>), Kopf samt 16 px Luft und <see cref="LargeHeroChrome"/>. Ab 190 px gilt die
    /// volle Größe.
    /// </summary>
    public static double LargeTitleRoom(double heroHeight, double toolbarHeight = ToolbarHeight) =>
        heroHeight - LargeHeroInset(toolbarHeight) - (HeaderTop + HeaderHeight + 16) - LargeHeroChrome;

    /// <summary>
    /// Oberkante der Werkzeugleiste: im Regal über dem unteren Rand des Titelbilds, ihre letzte Zeile endet
    /// <see cref="ToolbarGap"/> darüber (umgebrochen wächst sie nach oben, die Kachelreihe bleibt). In Liste und Raster
    /// <see cref="ToolbarGap"/> unter der Unterkante des Titelbilds, mit derselben Luft darunter bis zum Rahmen der Liste bzw.
    /// zur ersten Kachelreihe (Wunsch des Nutzers 2026-10-16: oben so viel wie unten, bei jeder Zahl von Zeilen). Die Lasche
    /// liegt in allen Ansichten mittig auf der Bildkante, <see cref="TabGap"/> vor der Leiste.
    /// </summary>
    /// <param name="toolbarHeight">Höhe der Werkzeugleiste, wie angeordnet (eine Zeile: <see cref="ToolbarHeight"/>).</param>
    public static double ToolbarTop(LibraryViewMode view, double heroHeight, double toolbarHeight = ToolbarHeight) =>
        view == LibraryViewMode.Shelf
            ? heroHeight - ToolbarGap - ToolbarHeight - ToolbarExtra(toolbarHeight)
            : heroHeight + ToolbarGap;

    /// <summary>Breite des Detailbereichs ohne gemerkte Aufteilung: clamp(23 % der Fensterbreite; 340; 396).</summary>
    public static double PaneWidth(double windowWidth) => Math.Round(Math.Clamp((double.IsFinite(windowWidth) ? windowWidth : 1600) * 0.23, 340, 396));

    /// <summary>Innenabstand der Kachelreihe oben und unten zusammen (12 + 22), gemessen ab der Bildkante bzw. bis zum unteren Rand der Spalten.</summary>
    public const double ShelfPadding = 34;

    /// <summary>
    /// Kachelreihe des Regals unter der Bildkante ohne die Kachel: <see cref="ShelfPadding"/> und der Abstand der Spalten vom
    /// unteren Rand (<see cref="PaneBottom"/>). Die Kachel ist so hoch wie die Fensterhöhe minus Titelbereich minus dies.
    /// </summary>
    public const double ShelfRowChrome = ShelfPadding + PaneBottom;

    /// <summary>
    /// Kachelreihe im Regal: Höhe clamp(h − 34; 200; 340), Breite 2/3 davon, sichtbare Kacheln nach der Breite. Ist die
    /// Reihe niedriger als die kleinste Kachel (nur in sehr niedrigen Fenstern), wird die Kachel kleiner statt abgeschnitten,
    /// höchstens bis <see cref="TinyShelfTile"/>.
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
