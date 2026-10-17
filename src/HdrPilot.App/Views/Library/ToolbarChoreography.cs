namespace HdrPilot.App.Views.Library;

/// <summary>Wie die Werkzeugleiste sich bewegt: Effektstufe „Voll“, „Reduziert“ oder sofort (Aus, minimiert, Spiel läuft).</summary>
public enum ToolbarMotionLevel
{
    Full,
    Reduced,
    Instant,
}

/// <summary>
/// Ziel von Lasche und Kapsel: Lage der Lasche über der Kapsel (<paramref name="TabTy"/>, relativ zur Oberkante der Kapsel),
/// Breite und Zahl der Einträge der Kapsel, ob sie ein ✕ hat und gefiltert ist (Rahmen, Hof, Lichtnaht).
/// </summary>
public readonly record struct TabTarget(double TabTy, double CapsuleWidth, int CapsuleItems, bool HasReset, bool Filtered);

/// <summary>Verschiebung eines Elements der Leiste beim Zeilenwechsel (FLIP): von der alten zur neuen Lage, geräteunabhängig.</summary>
public readonly record struct SlotMove(ToolbarSlot Slot, double Dx, double Dy, double Dw);

/// <summary>
/// Bewegungen der Werkzeugleiste nach der abgenommenen Vorschau (leiste.js: collapseVoll, expandVoll, collapseRed, expandRed,
/// finals, tabHover, morph, Sortier-Schritt), als Programme auf <see cref="MotionChannels"/>. Die Programme kennen keine
/// Elemente; das Fenster wendet die Kanäle an (nur Deckkraft und RenderTransform, dazu Breite und Höhe der einen
/// Lasche/Kapsel). Jedes Programm startet bei den aktuellen Werten: Ein zweites Umschalten läuft vom Augenblick aus weiter.
/// <list type="bullet">
/// <item>Einfalten, Voll (höchstens 740 ms): Die Elemente fließen als Welle zur Lasche (14 ms Versatz, je 140 ms), die Liste
/// steigt (380 ms ab 40 ms), die Lasche wird Kapsel (120–380), ihr Inhalt erscheint (ab 300), der Lichtstreif läuft
/// 340–640 zur Kapsel und verglüht bis 740, der Hof blitzt auf, gefiltert wächst die Lichtnaht (400 ms ab 340).</item>
/// <item>Ausfalten, Voll (höchstens 620 ms): Liste fällt (380), Naht zieht sich zurück, Streif von der Kapsel nach links,
/// Kapsel wird Lasche (60–260), die Elemente entfalten sich von der Lasche aus (bzw. nach Strg+F von der Suche aus).</item>
/// <item>Reduziert (höchstens 300 ms): kurze Überblendungen, die Liste gleitet 200 ms, Lasche und Kapsel nacheinander
/// überblendet, kein Versatz, kein Skalieren, kein Streif.</item>
/// <item>Sofort: Endzustand.</item>
/// </list>
/// </summary>
public sealed class ToolbarChoreography(MotionChannels channels)
{
    public const double TabWidth = 36;

    public const double TabHeight = 18;

    public const double CapsuleHeight = 24;

    /// <summary>Ruhewert des Hofs um die gefilterte Kapsel.</summary>
    public const double FilteredHalo = 0.35;

    /// <summary>Kapsel: so viele Einträge (mit Trennpunkten) haben eigene Kanäle.</summary>
    public const int CapsuleSlots = 12;

    public const string ListDy = "list:dy";
    public const string ToolbarOpacity = "tb:o";
    public const string SortWidth = "sortW";
    public const string SortLabelOpacity = "sortL:o";
    public const string SortLabelX = "sortL:x";
    public const string SearchKey = "key:o";
    public const string PillWidth = "pill:w";
    public const string PillHeight = "pill:h";
    public const string PillTy = "pill:ty";
    public const string PillTurn = "pill:r";
    public const string Divider = "cdv:o";
    public const string Hint = "hint:o";
    public const string Reset = "cx:o";
    public const string TabOpacity = "tabw:o";
    public const string Halo = "halo:o";
    public const string RestHalo = "halo2:o";
    public const string SeamScale = "seam:sx";
    public const string SeamOpacity = "seam:o";
    public const string StreakPosition = "streak:p";
    public const string StreakOpacity = "streak:o";
    public const string TabGhost = "ghost:tab";
    public const string ToolbarGhost = "ghost:bar";
    public const string LargeDy = "large:dy";

    /// <summary>Morph beim Wechsel zu zwei Zeilen (Vorschau MORPH.toTwo).</summary>
    private const double DropMs = 240;
    private const double DropStep = 12;
    private const double SlideAt = 80;
    private const double SlideMs = 200;
    private const double SlideStep = 12;
    private const double GrowAt = 96;
    private const double GrowMs = 220;

    /// <summary>Morph zurück zu einer Zeile (Vorschau MORPH.toOne).</summary>
    private const double ShrinkMs = 150;
    private const double BackSlideMs = 120;
    private const double BackSlideStep = 16;
    private const double RiseAt = 150;
    private const double RiseMs = 200;

    private static readonly ToolbarSlot[] Fields = [ToolbarSlot.Search, ToolbarSlot.Source, ToolbarSlot.Sort];
    private static readonly ToolbarSlot[] Filters = [ToolbarSlot.FilterAuto, ToolbarSlot.FilterNew, ToolbarSlot.FilterHdr];
    private static readonly ToolbarSlot[] RightGroup = [ToolbarSlot.View, ToolbarSlot.Rescan, ToolbarSlot.Seen, ToolbarSlot.Add];

    /// <summary>Vor einem reduzierten Übergang: Lasche bzw. Kapsel im jetzigen Zustand festhalten (Bild, das ausblendet).</summary>
    public event EventHandler? TabGhostRequested;

    /// <summary>Vor dem reduzierten Zeilenwechsel: die Leiste im jetzigen Zustand festhalten.</summary>
    public event EventHandler? ToolbarGhostRequested;

    public MotionChannels Channels => channels;

    /// <summary>Lichtstreif läuft von der Kapsel nach links (Ausfalten) statt zur Kapsel.</summary>
    public bool StreakReversed { get; private set; }

    public static string Item(ToolbarSlot slot, char part) => "w:" + (int)slot + ":" + part;

    public static string Morph(ToolbarSlot slot, char part) => "m:" + (int)slot + ":" + part;

    public static string CapsuleItem(int index, char part) => "ci:" + index + ":" + part;

    /// <summary>Endzustand ohne Bewegung (Start, Stufe „Aus“, Wechsel der Ansicht).</summary>
    public void Settle(bool collapsed, TabTarget target, double largeDy = 0)
    {
        foreach (var slot in Enum.GetValues<ToolbarSlot>())
        {
            channels.Set(Item(slot, 'o'), collapsed ? 0 : 1);
            channels.Set(Item(slot, 'x'), collapsed ? 16 : 0);
            channels.Set(Item(slot, 'y'), collapsed ? -6 : 0);
            channels.Set(Item(slot, 's'), collapsed ? 0.94 : 1);
        }

        channels.Set(ToolbarOpacity, 1);
        channels.Set(ListDy, 0);
        channels.Set(StreakOpacity, 0);
        channels.Set(Halo, 0);
        channels.Set(TabOpacity, 1);
        channels.Set(TabGhost, 0);
        channels.Set(ToolbarGhost, 0);
        channels.Set(LargeDy, largeDy);
        SettleTab(collapsed, target);
        var seam = collapsed && target.Filtered ? 1 : 0;
        channels.Set(SeamScale, seam);
        channels.Set(SeamOpacity, seam);
    }

    /// <summary>
    /// Einklappen. <paramref name="listDy"/>: wo die Liste gerade zu sehen ist, relativ zu ihrer neuen Lage (positiv: tiefer);
    /// <paramref name="order"/>: Elemente von links nach rechts (die Welle läuft zur Lasche).
    /// </summary>
    public void Collapse(ToolbarMotionLevel level, double listDy, IReadOnlyList<ToolbarSlot> order, TabTarget target, double largeDy = 0)
    {
        switch (level)
        {
            case ToolbarMotionLevel.Full:
                CollapseFull(listDy, order, target, largeDy);
                break;
            case ToolbarMotionLevel.Reduced:
                CollapseReduced(listDy, target, largeDy);
                break;
            default:
                Settle(collapsed: true, target, largeDy);
                break;
        }
    }

    /// <summary>Ausklappen; <paramref name="fromSearch"/> (Strg+F): die Elemente entfalten sich von der Suche aus.</summary>
    public void Expand(ToolbarMotionLevel level, double listDy, IReadOnlyList<ToolbarSlot> order, bool fromSearch, TabTarget target)
    {
        switch (level)
        {
            case ToolbarMotionLevel.Full:
                ExpandFull(listDy, order, fromSearch, target);
                break;
            case ToolbarMotionLevel.Reduced:
                ExpandReduced(listDy, target);
                break;
            default:
                Settle(collapsed: false, target);
                break;
        }
    }

    /// <summary>
    /// Lasche überfahren (nur ausgeklappt): Sie wächst nach links zu „Leiste ausblenden · Strg+Umschalt+F“ (160 ms) bzw.
    /// zurück. <paramref name="hoverWidth"/> ist ihre Breite mit dem Hinweis.
    /// </summary>
    public void Hover(bool on, double hoverWidth, ToolbarMotionLevel level)
    {
        var width = on ? hoverWidth : TabWidth;
        switch (level)
        {
            case ToolbarMotionLevel.Full:
                channels.Animate(PillWidth, width, 160, ToolbarEase.CubicOut);
                channels.Animate(Hint, on ? 1 : 0, on ? 110 : 60, ToolbarEase.CubicOut, delayMs: on ? 50 : 0);
                break;
            case ToolbarMotionLevel.Reduced:
                channels.Set(PillWidth, width);
                channels.Animate(Hint, on ? 1 : 0, 100, ToolbarEase.Linear);
                break;
            default:
                channels.Set(PillWidth, width);
                channels.Set(Hint, on ? 1 : 0);
                break;
        }
    }

    /// <summary>
    /// Inhalt der eingeklappten Kapsel geändert (Filter, Suche): Breite gleitet (160 ms), Hof und Lichtnaht folgen dem
    /// Filterzustand (Naht wächst 400 ms bzw. zieht sich 200 ms zurück; Reduziert 150 ms Überblenden).
    /// </summary>
    public void CapsuleChanged(ToolbarMotionLevel level, TabTarget target)
    {
        // Die Lasche wird gerade zur Kapsel (oder zurück): nur das Ziel der Breite nachführen, den Rest macht die Bewegung.
        if (channels.IsRunning(PillWidth) || channels.IsRunning(PillHeight) || channels.IsRunning(PillTy) || channels.IsRunning(PillTurn))
        {
            channels.Retarget(PillWidth, target.CapsuleWidth);
            return;
        }

        var wasWidth = channels.Get(PillWidth, target.CapsuleWidth);
        if (Math.Abs(wasWidth - target.CapsuleWidth) > 0.01 && level != ToolbarMotionLevel.Instant && Math.Abs(channels.Get(PillHeight) - CapsuleHeight) < 0.01)
        {
            channels.Animate(PillWidth, target.CapsuleWidth, 160, ToolbarEase.CubicOut);
        }
        else
        {
            channels.Set(PillWidth, target.CapsuleWidth);
        }

        channels.Set(PillHeight, CapsuleHeight);
        channels.Set(PillTy, 0);
        channels.Set(PillTurn, 180);
        channels.Set(Divider, 1);
        channels.Set(Hint, 0);
        for (var i = 0; i < CapsuleSlots; i++)
        {
            SetUnlessRunning(CapsuleItem(i, 'o'), 1);
            SetUnlessRunning(CapsuleItem(i, 'x'), 0);
        }

        SetUnlessRunning(Reset, target.HasReset ? 1 : 0);
        var rest = target.Filtered ? FilteredHalo : 0;
        if (!channels.IsRunning(RestHalo))
        {
            if (level == ToolbarMotionLevel.Instant)
            {
                channels.Set(RestHalo, rest);
            }
            else
            {
                channels.Animate(RestHalo, rest, 200);
            }
        }

        if (channels.IsRunning(SeamScale) || channels.IsRunning(SeamOpacity))
        {
            return;
        }

        var want = target.Filtered ? 1 : 0;
        switch (level)
        {
            case ToolbarMotionLevel.Full when Math.Abs(channels.Get(SeamScale) - want) > 0.001:
                if (want > 0)
                {
                    channels.Set(SeamOpacity, 1);
                    channels.Animate(SeamScale, 1, 400, ToolbarEase.Seam);
                }
                else
                {
                    channels.Animate(SeamScale, 0, 200, ToolbarEase.CubicOut);
                }

                break;
            case ToolbarMotionLevel.Reduced when Math.Abs(channels.Get(SeamOpacity) - want) > 0.001:
                channels.Set(SeamScale, 1);
                channels.Animate(SeamOpacity, want, 150, ToolbarEase.Linear);
                break;
            case ToolbarMotionLevel.Instant:
                channels.Set(SeamScale, want);
                channels.Set(SeamOpacity, want);
                break;
        }
    }

    private void SetUnlessRunning(string key, double value)
    {
        if (!channels.IsRunning(key))
        {
            channels.Set(key, value);
        }
    }

    /// <summary>
    /// Zeilenwechsel beim Ziehen, Stufe „Voll“ (Vorschau morph): Die Achsen laufen nacheinander, damit sich nichts kreuzt.
    /// 1 → 2: Erst fallen die Zeilenwechsler mit der Liste (je 12 ms versetzt), dann gleiten die Filter in Zeile 2 nach links
    /// und die Felder der Zeile 1 wachsen. 2 → 1: Erst geben die Felder nach und die Filter gleiten nach rechts, dann steigen
    /// alle Zeilenwechsler gemeinsam mit der Liste. ExponentialEase Out (Exponent 6), etwa 320 bzw. 350 ms.
    /// </summary>
    public void MorphRows(IReadOnlyList<SlotMove> moved, bool toTwo, double listDy)
    {
        var by = moved.ToDictionary(m => m.Slot);
        void Go(string key, double from, double durationMs, double delayMs = 0)
        {
            if (Math.Abs(from) < 0.01)
            {
                channels.Set(key, 0);
                return;
            }

            channels.Set(key, from);
            channels.Animate(key, 0, durationMs, ToolbarEase.ExpoOut6, delayMs);
        }

        foreach (var slot in RightGroup)
        {
            channels.Set(Morph(slot, 'x'), 0);
            channels.Set(Morph(slot, 'w'), 0);
        }

        foreach (var slot in Filters)
        {
            channels.Set(Morph(slot, 'w'), 0);
        }

        foreach (var slot in Fields)
        {
            channels.Set(Morph(slot, 'y'), 0);
        }

        if (toTwo)
        {
            var i = 0;
            foreach (var slot in Filters.Concat(RightGroup))
            {
                Go(Morph(slot, 'y'), by[slot].Dy, DropMs, DropStep * i++);
            }

            for (var j = 0; j < Filters.Length; j++)
            {
                Go(Morph(Filters[j], 'x'), by[Filters[j]].Dx, SlideMs, SlideAt + (SlideStep * j)); // nach links: „Automatik“ führt
            }

            foreach (var slot in Fields)
            {
                Go(Morph(slot, 'x'), by[slot].Dx, GrowMs, GrowAt);
                Go(Morph(slot, 'w'), by[slot].Dw, GrowMs, GrowAt);
            }

            Go(ListDy, listDy, DropMs);
        }
        else
        {
            foreach (var slot in Fields)
            {
                Go(Morph(slot, 'x'), by[slot].Dx, ShrinkMs);
                Go(Morph(slot, 'w'), by[slot].Dw, ShrinkMs);
            }

            for (var j = 0; j < Filters.Length; j++)
            {
                var slot = Filters[Filters.Length - 1 - j]; // nach rechts: „HDR“ führt
                Go(Morph(slot, 'x'), by[slot].Dx, BackSlideMs, BackSlideStep * j);
            }

            foreach (var slot in Filters.Concat(RightGroup))
            {
                Go(Morph(slot, 'y'), by[slot].Dy, RiseMs, RiseAt);
            }

            Go(ListDy, listDy, RiseMs, RiseAt);
        }
    }

    /// <summary>Zeilenwechsel, Stufe „Reduziert“: 150 ms Überblenden (die alte Leiste als Bild), der Inhalt springt.</summary>
    public void CrossfadeRows()
    {
        ToolbarGhostRequested?.Invoke(this, EventArgs.Empty);
        channels.Set(ToolbarGhost, 1);
        channels.Animate(ToolbarGhost, 0, 150, ToolbarEase.Linear);
        channels.Animate(ToolbarOpacity, 1, 150, ToolbarEase.Linear, from: 0);
        ClearMorph();
        channels.Set(ListDy, 0);
    }

    /// <summary>
    /// Sortier-Schritt beim Ziehen: Die Beschriftung weicht nach links aus (Breite 160 ms CubicEase Out, Deckkraft 90 ms,
    /// X 0 → −4) bzw. kommt zurück; nur die linke Gruppe rückt nach, die rechte hängt am Fensterrand. Reduziert: 150 ms
    /// Überblenden der Beschriftung, die Breite springt.
    /// </summary>
    public void SortStep(ToolbarMotionLevel level, bool toIcon, double fromWidth, double toWidth, IReadOnlyList<SlotMove> moved)
    {
        if (level == ToolbarMotionLevel.Full)
        {
            channels.Animate(SortWidth, toWidth, 160, ToolbarEase.CubicOut, from: fromWidth);
            SortLabel(toIcon, delayed: false);
            foreach (var move in moved.Where(m => !RightGroup.Contains(m.Slot)))
            {
                if (Math.Abs(move.Dx) > 0.5)
                {
                    channels.Set(Morph(move.Slot, 'x'), move.Dx);
                    channels.Animate(Morph(move.Slot, 'x'), 0, 160, ToolbarEase.CubicOut);
                }

                if (move.Slot != ToolbarSlot.Sort && Math.Abs(move.Dw) > 0.5)
                {
                    channels.Set(Morph(move.Slot, 'w'), move.Dw);
                    channels.Animate(Morph(move.Slot, 'w'), 0, 160, ToolbarEase.CubicOut);
                }
            }
        }
        else
        {
            channels.Set(SortWidth, toWidth);
            channels.Set(SortLabelX, toIcon ? -4 : 0);
            if (level == ToolbarMotionLevel.Reduced)
            {
                channels.Animate(SortLabelOpacity, toIcon ? 0 : 1, 150, ToolbarEase.Linear);
            }
            else
            {
                channels.Set(SortLabelOpacity, toIcon ? 0 : 1);
            }
        }
    }

    /// <summary>Beschriftung der Sortierung beim Zeilenwechsel (Voll): weicht wie beim Schritt bzw. kommt nach dem Wachsen der Felder.</summary>
    public void SortLabel(bool toIcon, bool delayed)
    {
        if (toIcon)
        {
            channels.Animate(SortLabelOpacity, 0, 90, ToolbarEase.CubicOut);
            channels.Animate(SortLabelX, -4, 160, ToolbarEase.CubicOut);
        }
        else
        {
            channels.Animate(SortLabelOpacity, 1, delayed ? 140 : 120, ToolbarEase.CubicOut, delayed ? GrowAt + 40 : 40);
            channels.Animate(SortLabelX, 0, 160, ToolbarEase.CubicOut, delayed ? GrowAt : 0);
        }
    }

    /// <summary>Sortierung und Tastenkappe ohne Bewegung in den Endzustand (keine Hysterese, nicht gezogen).</summary>
    public void SettleSort(double width, bool icon)
    {
        channels.Set(SortWidth, width);
        channels.Set(SortLabelOpacity, icon ? 0 : 1);
        channels.Set(SortLabelX, icon ? -4 : 0);
    }

    /// <summary>Tastenkappe „Strg+F“ ein- bzw. ausblenden (120 ms nur beim Ziehen, sonst sofort).</summary>
    public void SearchKeyTo(bool shown, bool animate)
    {
        if (animate)
        {
            channels.Animate(SearchKey, shown ? 1 : 0, 120, ToolbarEase.CubicOut);
        }
        else if (!channels.IsRunning(SearchKey))
        {
            channels.Set(SearchKey, shown ? 1 : 0);
        }
    }

    /// <summary>Verschiebungen des Zeilenwechsels löschen (Endzustand der Anordnung).</summary>
    public void ClearMorph()
    {
        foreach (var slot in Enum.GetValues<ToolbarSlot>())
        {
            channels.Set(Morph(slot, 'x'), 0);
            channels.Set(Morph(slot, 'y'), 0);
            channels.Set(Morph(slot, 'w'), 0);
        }
    }

    /// <summary>Lasche bzw. Kapsel ohne laufende Bewegung auf ihren Endwert (Vorschau finalsTab).</summary>
    public void SettleTab(bool collapsed, TabTarget target)
    {
        channels.Set(PillWidth, collapsed ? target.CapsuleWidth : TabWidth);
        channels.Set(PillHeight, collapsed ? CapsuleHeight : TabHeight);
        channels.Set(PillTy, collapsed ? 0 : target.TabTy);
        channels.Set(PillTurn, collapsed ? 180 : 0);
        channels.Set(Divider, collapsed ? 1 : 0);
        channels.Set(Hint, 0);
        for (var i = 0; i < CapsuleSlots; i++)
        {
            channels.Set(CapsuleItem(i, 'o'), collapsed ? 1 : 0);
            channels.Set(CapsuleItem(i, 'x'), collapsed ? 0 : -6);
        }

        channels.Set(Reset, collapsed && target.HasReset ? 1 : 0);
        channels.Set(RestHalo, collapsed && target.Filtered ? FilteredHalo : 0);
        channels.Set(Halo, 0);
    }

    /// <summary>Einfalten, Voll: alles endet spätestens nach 740 ms.</summary>
    private void CollapseFull(double listDy, IReadOnlyList<ToolbarSlot> order, TabTarget target, double largeDy)
    {
        channels.Set(ListDy, listDy);
        channels.Animate(ListDy, 0, 380, ToolbarEase.ExpoOut6, delayMs: 40);
        channels.Animate(LargeDy, largeDy, 380, ToolbarEase.ExpoOut6, delayMs: 40);
        channels.Set(ToolbarOpacity, 1);
        for (var i = 0; i < order.Count; i++)
        {
            var delay = i * 14.0;
            var slot = order[i];
            channels.Animate(Item(slot, 'o'), 0, 140, ToolbarEase.CubicIn, delay);
            channels.Animate(Item(slot, 'x'), 16, 140, ToolbarEase.CubicIn, delay);
            channels.Animate(Item(slot, 'y'), -6, 140, ToolbarEase.CubicIn, delay);
            channels.Animate(Item(slot, 's'), 0.94, 140, ToolbarEase.CubicIn, delay);
        }

        channels.Animate(Hint, 0, 80);

        // Lasche wird Kapsel: Breite ohne Überschwingen, nur Höhe und Lage federn (höchstens 0,4 px).
        channels.Animate(PillWidth, target.CapsuleWidth, 260, ToolbarEase.CubicOut, 120);
        channels.Animate(PillHeight, CapsuleHeight, 260, ToolbarEase.Spring, 120);
        channels.Animate(PillTy, 0, 260, ToolbarEase.Spring, 120);
        channels.Animate(PillTurn, 180, 220, ToolbarEase.CubicOut, 120);
        channels.Animate(Divider, 1, 120, delayMs: 260);
        for (var i = 0; i < Math.Min(CapsuleSlots, target.CapsuleItems * 2); i++)
        {
            channels.Set(CapsuleItem(i, 'o'), 0);
            channels.Set(CapsuleItem(i, 'x'), -6);
            channels.Animate(CapsuleItem(i, 'o'), 1, 120, ToolbarEase.CubicOut, 300 + (i * 15));
            channels.Animate(CapsuleItem(i, 'x'), 0, 150, ToolbarEase.CubicOut, 300 + (i * 15));
        }

        if (target.HasReset)
        {
            channels.Set(Reset, 0);
            channels.Animate(Reset, 1, 120, delayMs: 300 + (target.CapsuleItems * 30));
        }

        // „Zündung“: Lichtstreif läuft 340–640 auf der Kante zur Kapsel und verglüht bis 740; der Hof blitzt 460–740 auf
        // (Spitze, wenn der Streif ankommt), der große Schein 540–740.
        StreakReversed = false;
        channels.Set(StreakPosition, 0);
        channels.Animate(StreakPosition, 1, 300, ToolbarEase.SineInOut, 340);
        channels.Animate(StreakOpacity, 0, 400, ToolbarEase.Linear, 340, keys: [new(0, 0), new(0.3, 0.95), new(0.72, 0.9), new(1, 0)]);
        var rest = target.Filtered ? FilteredHalo : 0;
        channels.Animate(RestHalo, rest, 280, ToolbarEase.Linear, 460, keys: [new(0, null), new(0.55, 0.8), new(1, rest)]);
        channels.Animate(Halo, 0, 200, ToolbarEase.Linear, 540, keys: [new(0, 0), new(0.4, 1), new(1, 0)]);
        if (target.Filtered)
        {
            channels.Set(SeamOpacity, 1);
            channels.Animate(SeamScale, 1, 400, ToolbarEase.Seam, 340);
        }
    }

    /// <summary>Ausfalten, Voll (höchstens 620 ms).</summary>
    private void ExpandFull(double listDy, IReadOnlyList<ToolbarSlot> order, bool fromSearch, TabTarget target)
    {
        channels.Set(ListDy, listDy);
        channels.Animate(ListDy, 0, 380, ToolbarEase.ExpoOut6);
        channels.Animate(LargeDy, 0, 380, ToolbarEase.ExpoOut6);
        channels.Set(ToolbarOpacity, 1);

        // Die Naht zieht sich in die Kapsel zurück; ihr rechtes Ende bleibt an der schrumpfenden Kapsel.
        if (channels.Get(SeamScale) > 0)
        {
            channels.Animate(SeamScale, 0, 200, ToolbarEase.CubicOut);
        }
        else
        {
            channels.Set(SeamScale, 0);
        }

        StreakReversed = true;
        channels.Set(StreakPosition, 0);
        channels.Animate(StreakPosition, 1, 320, ToolbarEase.SineInOut);
        channels.Animate(StreakOpacity, 0, 320, ToolbarEase.Linear, keys: [new(0, 0), new(0.3, 0.9), new(1, 0)]);
        for (var i = 0; i < CapsuleSlots; i++)
        {
            channels.Animate(CapsuleItem(i, 'o'), 0, 90);
            channels.Animate(CapsuleItem(i, 'x'), -6, 120);
        }

        channels.Animate(Reset, 0, 80);
        channels.Animate(Divider, 0, 100);
        channels.Animate(PillWidth, TabWidth, 200, ToolbarEase.CubicOut, 60);
        channels.Animate(PillHeight, TabHeight, 200, ToolbarEase.Spring, 60);
        channels.Animate(PillTy, target.TabTy, 200, ToolbarEase.Spring, 60);
        channels.Animate(PillTurn, 0, 220, ToolbarEase.CubicOut, 60);
        channels.Animate(RestHalo, 0, 200);
        channels.Animate(Halo, 0, 120);
        var sequence = fromSearch ? order : order.Reverse().ToList();
        for (var i = 0; i < sequence.Count; i++)
        {
            var delay = 120 + (i * 16.0);
            var slot = sequence[i];
            channels.Animate(Item(slot, 'o'), 1, 180, ToolbarEase.CubicOut, delay);
            channels.Animate(Item(slot, 'x'), 0, 300, ToolbarEase.Spring, delay);
            channels.Animate(Item(slot, 'y'), 0, 300, ToolbarEase.Spring, delay);
            channels.Animate(Item(slot, 's'), 1, 300, ToolbarEase.Spring, delay);
        }
    }

    /// <summary>Einfalten, Reduziert (höchstens 300 ms): Lasche und Kapsel nacheinander überblendet (alt 0–90, neu 60–150).</summary>
    private void CollapseReduced(double listDy, TabTarget target, double largeDy)
    {
        channels.Set(ListDy, listDy);
        channels.Animate(ListDy, 0, 200, ToolbarEase.CubicOut);
        channels.Animate(LargeDy, largeDy, 200, ToolbarEase.CubicOut);
        foreach (var slot in Enum.GetValues<ToolbarSlot>())
        {
            channels.Animate(Item(slot, 'o'), 0, 120, ToolbarEase.CubicOut);
            channels.Animate(Item(slot, 'x'), 16, 1, delayMs: 120);
            channels.Animate(Item(slot, 'y'), -6, 1, delayMs: 120);
            channels.Animate(Item(slot, 's'), 0.94, 1, delayMs: 120);
        }

        GhostTab();
        SettleTab(collapsed: true, target);
        channels.Set(TabOpacity, 0);
        channels.Animate(TabOpacity, 1, 90, ToolbarEase.Linear, 60);
        if (target.Filtered)
        {
            channels.Set(SeamScale, 1);
            channels.Set(SeamOpacity, 0);
            channels.Animate(SeamOpacity, 1, 150, ToolbarEase.Linear);
        }

        channels.Set(StreakOpacity, 0);
    }

    /// <summary>Ausfalten, Reduziert: Die Leiste blendet erst ab 60 ms ein, wenn die Liste schon Platz gemacht hat.</summary>
    private void ExpandReduced(double listDy, TabTarget target)
    {
        channels.Set(ListDy, listDy);
        channels.Animate(ListDy, 0, 200, ToolbarEase.CubicOut);
        channels.Animate(LargeDy, 0, 200, ToolbarEase.CubicOut);
        foreach (var slot in Enum.GetValues<ToolbarSlot>())
        {
            channels.Set(Item(slot, 'x'), 0);
            channels.Set(Item(slot, 'y'), 0);
            channels.Set(Item(slot, 's'), 1);
            channels.Animate(Item(slot, 'o'), 1, 120, ToolbarEase.CubicOut, 60);
        }

        GhostTab();
        SettleTab(collapsed: false, target);
        channels.Set(TabOpacity, 0);
        channels.Animate(TabOpacity, 1, 90, ToolbarEase.Linear, 60);
        if (channels.Get(SeamOpacity) > 0)
        {
            channels.Animate(SeamOpacity, 0, 150, ToolbarEase.Linear);
        }
        else
        {
            channels.Set(SeamOpacity, 0);
        }

        channels.Set(StreakOpacity, 0);
    }

    private void GhostTab()
    {
        TabGhostRequested?.Invoke(this, EventArgs.Empty);
        channels.Set(TabGhost, 1);
        channels.Animate(TabGhost, 0, 90, ToolbarEase.Linear);
    }
}
