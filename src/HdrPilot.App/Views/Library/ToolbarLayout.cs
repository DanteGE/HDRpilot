namespace HdrPilot.App.Views.Library;

/// <summary>Plätze der Werkzeugleiste in der Reihenfolge der Oberfläche (und der Tab-Reihenfolge).</summary>
public enum ToolbarSlot
{
    Search,
    Source,
    Sort,
    FilterAuto,
    FilterNew,
    FilterHdr,
    View,
    Rescan,
    Seen,
    Add,
}

/// <summary>Stufe der Anpassung an die Breite (Tabelle „Wie die Leiste in schmalen Fenstern nachgibt“ der Vorschau).</summary>
public enum ToolbarStage
{
    /// <summary>Alles bequem, Suche 320, der Rest wird Freiraum zwischen linker und rechter Gruppe.</summary>
    Comfortable,

    /// <summary>Die Suche gibt nach (320 → 236), stufenlos.</summary>
    SearchYields,

    /// <summary>Suche, Quelle und Sortierung geben anteilig nach (68 : 52 : 36), stufenlos bis 168/136/160.</summary>
    FieldsYield,

    /// <summary>Einziger Schritt: Die Sortierung zeigt nur ihr Symbol (60), die Felder stehen am Minimum.</summary>
    SortIcon,

    /// <summary>Zwei Zeilen, Umbruch an der Gruppengrenze: oben Suche · Quelle · Sortierung gefüllt, unten Filter und Aktionen.</summary>
    TwoRows,

    /// <summary>Nur in der schmalsten Liste (kleinstes Fenster, breitester Detailbereich): die Aktionen in einer dritten Zeile.</summary>
    ThreeRows,
}

/// <summary>
/// Lage eines Elements der Leiste in geräteunabhängigen Pixeln; hoch <see cref="ToolbarLayout.ItemHeight"/> in ganzen
/// Gerätepixeln (bei 110 % 44 px, also 39,85).
/// </summary>
public readonly record struct ToolbarSlotRect(double X, double Y, double Width, double Height = ToolbarLayout.ItemHeight)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>
/// Gedächtnis der Hysterese beim Ziehen (Fensterrand oder Trenner zum Detailbereich): welcher Zustand zuletzt galt. Nur
/// solange gezogen wird, kehrt ein Schritt erst mit Abstand zur Schwelle zurück.
/// </summary>
public readonly record struct ToolbarMemory(bool MultipleRows, bool SortIcon, bool SearchKey);

/// <summary>Ergebnis von <see cref="ToolbarLayout.Arrange"/>: Zeilen, Stufe und die Lage jedes Elements.</summary>
public sealed class ToolbarArrangement
{
    internal ToolbarArrangement(double requestedWidth, double width, double scale, int rows, ToolbarStage stage, bool searchKey, ToolbarSlotRect[] slots, double height)
    {
        RequestedWidth = requestedWidth;
        Width = width;
        Scale = scale;
        Rows = rows;
        Stage = stage;
        SearchKey = searchKey;
        Slots = slots;
        Height = height;
    }

    /// <summary>Breite der Liste, für die gerechnet wurde.</summary>
    public double RequestedWidth { get; }

    /// <summary>Breite der Leiste auf ganzen Gerätepixeln: bis zu dem Pixel, der der rechten Kante der Liste am nächsten ist.</summary>
    public double Width { get; }

    /// <summary>Skalierung des Bildschirms (1 = 100 %), auf deren Gerätepixel die Kanten liegen.</summary>
    public double Scale { get; }

    public int Rows { get; }

    public ToolbarStage Stage { get; }

    /// <summary>
    /// Die Sortierung zeigt nur ihr Symbol: auf der Symbol-Stufe und in der schmalsten, mehrzeiligen Liste (unter 480).
    /// </summary>
    public bool SortIconOnly => this[ToolbarSlot.Sort].Width <= ToolbarLayout.SortIconWidth + 4; // Rundung auf Gerätepixel

    /// <summary>Die Tastenkappe „Strg+F“ in der Suche ist zu sehen.</summary>
    public bool SearchKey { get; }

    /// <summary>Höhe der Leiste im Layout: Zeilen à 40 und 8 dazwischen (40, 88, 136), unabhängig von der Skalierung.</summary>
    public double Height { get; }

    /// <summary>Lage je Platz (Index = <see cref="ToolbarSlot"/>).</summary>
    public IReadOnlyList<ToolbarSlotRect> Slots { get; }

    public ToolbarSlotRect this[ToolbarSlot slot] => Slots[(int)slot];

    /// <summary>Freiraum zwischen Filtern und Ansicht in einer Zeile (sonst zwischen Filtern und Ansicht der zweiten Zeile).</summary>
    public double GroupGap => this[ToolbarSlot.View].X - this[ToolbarSlot.FilterHdr].Right;

    public ToolbarMemory Memory => new(Rows > 1, Stage == ToolbarStage.SortIcon, SearchKey);
}

/// <summary>
/// Anordnung der Werkzeugleiste als reine Rechnung (abgenommene Vorschau „Fließleiste mit Lichtnaht“, leiste.js
/// layoutToolbar, Wunsch des Nutzers 2026-10-17). Einzeilig hat Vorrang: Erst gibt die Suche nach (320 → 236), dann Suche,
/// Quelle und Sortierung anteilig bis 168/136/160, dann zeigt die Sortierung nur ihr Symbol; erst unter einer Listenbreite
/// von 806 kommen zwei Zeilen, getrennt an der Gruppengrenze (oben die drei Felder, die Zeile füllend; unten die Filter links,
/// Ansicht · Neu einlesen · gesehen · hinzufügen rechts). Zwischen Filtern und Ansicht bleiben mindestens 24 (nur auf der
/// Symbol-Stufe 16). Beim Ziehen gilt Hysterese: Die Beschriftung kommt erst 24 über der Schwelle zurück, die eine Zeile
/// erst 32 darüber, die Tastenkappe „Strg+F“ weicht unter 204 Suchbreite und kommt ab 216 zurück.
/// <para>
/// Stufe und Zeilen folgen den Schwellen der Vorschau in geräteunabhängigen Pixeln, bei jeder Skalierung bei denselben
/// Breiten (1366×768 bei 110 %, Liste 807, ist einzeilig wie bei 100 %). Verteilt wird danach in Gerätepixeln der
/// Skalierung: Abstände round(8 · Skalierung), Knöpfe und Höhe round(40 · Skalierung), die Ansicht aufgerundet, die Felder
/// durch fortlaufendes Runden. So liegt jede Kante (auch bei 110 % mit 106 dpi und bei 125 %) auf einem ganzen Gerätepixel
/// der Leiste, die rechte Gruppe hängt bündig an der rechten Kante. Was das Runden an einer Schwelle kostet (höchstens zwei
/// Gerätepixel), trägt der Freiraum vor der Ansicht bzw. tragen die Felder.
/// </para>
/// </summary>
public static class ToolbarLayout
{
    public const double ItemHeight = 40;

    public const double Gap = 8;

    public const double FilterWidth = 40;

    public const double IconWidth = 40;

    /// <summary>Ansicht: drei Symbole à 38 und 4 Innenrand je Seite.</summary>
    public const double ViewWidth = 122;

    public const double SearchMax = 320;

    public const double SearchBase = 236;

    public const double SearchMin = 168;

    public const double SourceBase = 188;

    public const double SourceMin = 136;

    public const double SortBase = 196;

    public const double SortMin = 160;

    /// <summary>Sortierung nur als Symbol mit Pfeil.</summary>
    public const double SortIconWidth = 60;

    /// <summary>Kleinster Abstand zwischen Filtern und Ansicht (sonst verschmelzen die Gruppen zu einem Band).</summary>
    public const double GroupGap = 24;

    /// <summary>Auf der Symbol-Stufe darf der Abstand bis hierher sinken.</summary>
    public const double GroupGapIcon = 16;

    /// <summary>Hysterese beim Ziehen: Die Beschriftung der Sortierung kommt erst so weit über ihrer Schwelle zurück.</summary>
    public const double SortStepBack = 24;

    /// <summary>Hysterese beim Ziehen: Die eine Zeile kommt erst so weit über ihrer Schwelle zurück.</summary>
    public const double OneRowBack = 32;

    /// <summary>Unter dieser Suchbreite weicht die Tastenkappe „Strg+F“.</summary>
    public const double SearchKeyHide = 204;

    /// <summary>Beim Ziehen kommt die Tastenkappe erst ab dieser Suchbreite zurück.</summary>
    public const double SearchKeyShow = 216;

    /// <summary>Linke Gruppe ohne die Felder: drei Filter und fünf Abstände.</summary>
    private const double LeftFixed = (3 * FilterWidth) + (5 * Gap);

    /// <summary>Spielraum beim Vergleich mit den Schwellen (Rechenfehler, keine Pixel).</summary>
    private const double Epsilon = 1e-6;

    /// <summary>Rechte Gruppe: Ansicht, drei Symbolknöpfe, drei Abstände (266).</summary>
    public const double RightGroup = ViewWidth + (3 * IconWidth) + (3 * Gap);

    /// <summary>Ab dieser Listenbreite alles bequem (Suche 320): 1154.</summary>
    public const double ComfortableWidth = SearchMax + SourceBase + SortBase + LeftFixed + GroupGap + RightGroup;

    /// <summary>Ab dieser Listenbreite gibt nur die Suche nach: 1070.</summary>
    public const double SearchYieldWidth = SearchBase + SourceBase + SortBase + LeftFixed + GroupGap + RightGroup;

    /// <summary>Ab dieser Listenbreite zeigt die Sortierung ihre Beschriftung: 914.</summary>
    public const double SortStepWidth = SearchMin + SourceMin + SortMin + LeftFixed + GroupGap + RightGroup;

    /// <summary>Ab dieser Listenbreite einzeilig (Sortierung nur Symbol): 806.</summary>
    public const double OneRowWidth = SearchMin + SourceMin + SortIconWidth + LeftFixed + GroupGapIcon + RightGroup;

    /// <summary>Darunter passen Filter und Aktionen nicht mehr nebeneinander in die zweite Zeile: 410.</summary>
    public const double SecondRowWidth = (3 * FilterWidth) + (3 * Gap) + RightGroup;

    /// <summary>Höhe der Leiste bei dieser Zahl von Zeilen (100 %).</summary>
    public static double HeightFor(int rows) => rows <= 0 ? 0 : (rows * ItemHeight) + ((rows - 1) * Gap);

    /// <summary>
    /// Anordnung für eine Breite der Liste. <paramref name="memory"/> nur beim Ziehen (Hysterese, siehe oben), sonst null:
    /// Dann gilt sofort der Endzustand der Breite. Ohne gültige Breite (noch nicht angeordnet) die bequeme Zeile.
    /// <paramref name="originFraction"/>: Bruchteil eines Gerätepixels, um den die Leiste im Fenster neben dem Pixelraster
    /// beginnt (−0,5 … 0,5; das Fenster rückt sie auf den nächsten Pixel); die rechte Kante liegt dann auf dem Pixel, der der
    /// rechten Kante der Liste am nächsten ist.
    /// </summary>
    public static ToolbarArrangement Arrange(double width, double scale = 1, ToolbarMemory? memory = null, double originFraction = 0)
    {
        var s = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var w = double.IsFinite(width) && width > 0 ? width : ComfortableWidth;
        var f = double.IsFinite(originFraction) ? Math.Clamp(originFraction, -0.5, 0.5) : 0;
        var px = new Pixels(s);
        var right = Math.Round(f + (w * s), MidpointRounding.AwayFromZero);
        var g = px.Gap;
        var b = px.Round(FilterWidth);
        var view = px.Fixed(ViewWidth);
        var rightGroup = view + (3 * b) + (3 * g);
        var leftFixed = (3 * b) + (5 * g);

        // Stufe nach den Schwellen der Vorschau (geräteunabhängig, beim Ziehen mit Hysterese).
        var labeled = w >= SortStepWidth + (memory is { SortIcon: true } ? SortStepBack : 0) - Epsilon;
        var oneRow = labeled || w >= OneRowWidth + (memory is { MultipleRows: true } ? OneRowBack : 0) - Epsilon;

        var slots = new ToolbarSlotRect[10];
        ToolbarStage stage;
        int rows;
        double searchWidth;
        if (oneRow)
        {
            rows = 1;
            stage = !labeled ? ToolbarStage.SortIcon
                : w >= ComfortableWidth - Epsilon ? ToolbarStage.Comfortable
                : w >= SearchYieldWidth - Epsilon ? ToolbarStage.SearchYields
                : ToolbarStage.FieldsYield;
            double ws, wsrc, wsort, total;
            if (stage is ToolbarStage.SearchYields or ToolbarStage.FieldsYield)
            {
                // Die Felder geben nach und füllen genau den Platz (Gerätepixel) bis zum Gruppenabstand: erst die Suche bis 236,
                // dann alle drei anteilig.
                total = right - leftFixed - rightGroup - px.Size(GroupGap);
                var extra = (total / s) - (SearchBase + SourceBase + SortBase);
                (ws, wsrc, wsort) = extra >= 0 ? (Math.Min(SearchMax, SearchBase + extra), SourceBase, SortBase) : Yield(-extra);
                if (SearchBase + extra > SearchMax)
                {
                    total = px.Round(ws + wsrc + wsort);
                }
            }
            else
            {
                (ws, wsrc, wsort) = stage == ToolbarStage.Comfortable ? (SearchMax, SourceBase, SortBase) : (SearchMin, SourceMin, SortIconWidth);
                total = px.Round(ws + wsrc + wsort);
            }

            // Felder fortlaufend auf ganze Pixel, danach die Filter; die rechte Gruppe hängt an der rechten Kante.
            var sizes = px.Fill([ws, wsrc, wsort], Math.Max(3, total));
            var x = 0.0;
            for (var i = 0; i < 3; i++)
            {
                slots[i] = px.Rect(x, 0, sizes[i], b);
                x += sizes[i] + g;
            }

            for (var i = 0; i < 3; i++)
            {
                slots[(int)ToolbarSlot.FilterAuto + i] = px.Rect(x, 0, b, b);
                x += b + g;
            }

            RightGroupAt(slots, px, right, 0, b, g, view);
            searchWidth = slots[(int)ToolbarSlot.Search].Width;
        }
        else
        {
            // Zwei (oder drei) Zeilen: oben die Felder, die die Zeile genau füllen (aufgeteilt nach der Breite in geräteunabhängigen
            // Pixeln, verteilt auf die Gerätepixel der Zeile).
            var avail = w - (2 * Gap);
            double ws, wsrc, wsort;
            if (avail >= SearchBase + SourceBase + SortBase)
            {
                var extra = avail - (SearchBase + SourceBase + SortBase);
                (ws, wsrc, wsort) = (SearchBase + (extra * 4 / 8), SourceBase + (extra * 2 / 8), SortBase + (extra * 2 / 8));
                if (ws > SearchMax)
                {
                    var over = ws - SearchMax;
                    (ws, wsrc, wsort) = (SearchMax, wsrc + (over / 2), wsort + (over / 2));
                }
            }
            else if (avail >= SearchMin + SourceMin + SortMin)
            {
                (ws, wsrc, wsort) = Yield((SearchBase + SourceBase + SortBase) - avail);
            }
            else
            {
                // Schmalste Liste (unter 480): die Sortierung nur als Symbol, Suche und Quelle teilen sich den Rest.
                var rest = avail - SortIconWidth;
                (ws, wsrc, wsort) = (rest * SearchMin / (SearchMin + SourceMin), rest * SourceMin / (SearchMin + SourceMin), SortIconWidth);
            }

            var sizes = px.Fill([ws, wsrc, wsort], right - (2 * g));
            var x = 0.0;
            for (var i = 0; i < 3; i++)
            {
                slots[i] = px.Rect(x, 0, sizes[i], b);
                x += sizes[i] + g;
            }

            var pitch = b + g;
            for (var i = 0; i < 3; i++)
            {
                slots[(int)ToolbarSlot.FilterAuto + i] = px.Rect(i * pitch, pitch, b, b);
            }

            var three = w < SecondRowWidth - Epsilon;
            rows = three ? 3 : 2;
            stage = three ? ToolbarStage.ThreeRows : ToolbarStage.TwoRows;
            RightGroupAt(slots, px, right, three ? 2 * pitch : pitch, b, g, view);
            searchWidth = slots[(int)ToolbarSlot.Search].Width;
        }

        var key = memory is { SearchKey: false } ? searchWidth >= SearchKeyShow - 0.01 : searchWidth >= SearchKeyHide - 0.01;
        var height = HeightFor(rows);
        return new ToolbarArrangement(w, right / s, s, rows, stage, key, slots, height);
    }

    /// <summary>Suche, Quelle und Sortierung geben anteilig nach (68 : 52 : 36), um <paramref name="d"/> zusammen.</summary>
    private static (double Search, double Source, double Sort) Yield(double d)
    {
        var total = (SearchBase - SearchMin) + (SourceBase - SourceMin) + (SortBase - SortMin);
        var k = Math.Clamp(d, 0, total);
        return (SearchBase - (k * (SearchBase - SearchMin) / total), SourceBase - (k * (SourceBase - SourceMin) / total), SortBase - (k * (SortBase - SortMin) / total));
    }

    private static void RightGroupAt(ToolbarSlotRect[] slots, Pixels px, double right, double y, double b, double g, double view)
    {
        var x = right - b;
        foreach (var slot in new[] { ToolbarSlot.Add, ToolbarSlot.Seen, ToolbarSlot.Rescan })
        {
            slots[(int)slot] = px.Rect(x, y, b, b);
            x -= g + b;
        }

        slots[(int)ToolbarSlot.View] = px.Rect(x + b - view, y, view, b);
    }

    /// <summary>Rechnung in Gerätepixeln einer Skalierung.</summary>
    private readonly record struct Pixels(double Scale)
    {
        public double Gap => Math.Round(ToolbarLayout.Gap * Scale, MidpointRounding.AwayFromZero);

        /// <summary>Abstand in ganzen Gerätepixeln.</summary>
        public double Size(double dip) => Math.Round(dip * Scale, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Breite der Ansicht (122) in ganzen Gerätepixeln, aufgerundet (bei 125 % 153 statt 152,5): Sie füllt ihren Platz und
        /// wird nie kleiner als ihr Inhalt. Knöpfe und Höhe (40) werden gerundet (bei 110 % 44 statt 44,17).
        /// </summary>
        public double Fixed(double dip) => Math.Ceiling((dip * Scale) - 1e-6);

        public double Round(double dip) => Math.Round(dip * Scale, MidpointRounding.AwayFromZero);

        public ToolbarSlotRect Rect(double x, double y, double width, double height) => new(x / Scale, y / Scale, width / Scale, height / Scale);

        /// <summary>Breiten (geräteunabhängig) fortlaufend gerundet auf genau <paramref name="total"/> Gerätepixel.</summary>
        public double[] Fill(double[] dips, double total)
        {
            var sum = dips.Sum();
            var result = new double[dips.Length];
            var acc = 0.0;
            var placed = 0.0;
            for (var i = 0; i < dips.Length; i++)
            {
                acc += dips[i];
                var end = i == dips.Length - 1 ? total : Math.Round(acc / sum * total, MidpointRounding.AwayFromZero);
                result[i] = Math.Max(1, end - placed);
                placed = end;
            }

            return result;
        }
    }
}
