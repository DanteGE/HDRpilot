using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Views.Library;
using static HdrPilot.App.Views.Library.ToolbarChoreography;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bewegungen der Werkzeugleiste nach der abgenommenen Vorschau (leiste.js) je Effektstufe, abgetastet über die Zeit mit
/// gestellter Uhr: Einfalten „Voll“ endet nach 740 ms (Welle zur Lasche, Liste steigt, Lasche wird Kapsel, Zündung, Naht),
/// Ausfalten nach 620 ms, „Reduziert“ nach 300 ms ohne Versatz und ohne Streif, „Sofort“ ohne Bewegung; Unterbrechen läuft
/// vom Augenblick aus weiter; danach ist der Bildtakt abgemeldet. Dazu der Morph beim Zeilenwechsel und die Lasche beim
/// Überfahren.
/// </summary>
public sealed class ToolbarMotionTests
{
    private static readonly TabTarget Filtered = new(TabTy: -4, CapsuleWidth: 240, CapsuleItems: 4, HasReset: true, Filtered: true);
    private static readonly TabTarget Plain = new(TabTy: -4, CapsuleWidth: 150, CapsuleItems: 1, HasReset: false, Filtered: false);
    private static readonly ToolbarSlot[] Order = Enum.GetValues<ToolbarSlot>();

    private static (ToolbarChoreography Choreo, MotionChannels Channels, TestFrameClock Clock, TestTime Time) Create(bool collapsed, TabTarget target)
    {
        var clock = new TestFrameClock();
        var time = new TestTime();
        var channels = new MotionChannels(clock, time);
        var choreo = new ToolbarChoreography(channels);
        choreo.Settle(collapsed, target);
        return (choreo, channels, clock, time);
    }

    /// <summary>Zeit vorstellen und einen Takt geben (wie der Bildtakt).</summary>
    private static void At(TestTime time, MotionChannels channels, double ms)
    {
        time.Now = TimeSpan.FromMilliseconds(ms);
        channels.Tick();
    }

    [Fact]
    public void Full_collapse_flows_to_the_tab_lifts_the_list_and_ignites_within_740_ms()
    {
        var (choreo, ch, clock, time) = Create(collapsed: false, Filtered);
        choreo.Collapse(ToolbarMotionLevel.Full, listDy: 38, Order, Filtered);
        Assert.Equal(1, clock.Subscribers); // ein Bildtakt, solange etwas läuft

        // 0 ms: Endlayout steht, die Liste wird um +38 verschoben gezeigt; alles noch sichtbar.
        At(time, ch, 0);
        Assert.Equal(38, ch.Get(ListDy), 3);
        Assert.Equal(1, ch.Get(Item(ToolbarSlot.Search, 'o')), 3);
        Assert.Equal(TabWidth, ch.Get(PillWidth), 3);

        // Die Liste steigt erst ab 40 ms, dann schnell (ExpoOut).
        At(time, ch, 40);
        Assert.Equal(38, ch.Get(ListDy), 3);

        // Welle: Suche zuerst (0–140 ms, CubicEase In), „+“ als letztes (ab 126 ms); X +16, Y −6, Skalierung 0,94.
        At(time, ch, 70);
        var searchMid = ch.Get(Item(ToolbarSlot.Search, 'o'));
        Assert.InRange(searchMid, 0.8, 0.95); // CubicEase In: erst langsam
        Assert.Equal(1, ch.Get(Item(ToolbarSlot.Add, 'o')), 3);
        At(time, ch, 140);
        Assert.Equal(0, ch.Get(Item(ToolbarSlot.Search, 'o')), 3);
        Assert.Equal(16, ch.Get(Item(ToolbarSlot.Search, 'x')), 3);
        Assert.Equal(-6, ch.Get(Item(ToolbarSlot.Search, 'y')), 3);
        Assert.Equal(0.94, ch.Get(Item(ToolbarSlot.Search, 's')), 3);
        Assert.True(ch.Get(ListDy) < 10, "ExpoOut: nach 100 ms ist die Liste fast oben: " + ch.Get(ListDy));
        At(time, ch, 266);
        Assert.All(Order, s => Assert.Equal(0, ch.Get(Item(s, 'o')), 3));

        // Lasche wird Kapsel (120–380): Höhe 18 → 24, Lage → 0, Pfeil dreht 180°, Breite wächst ohne Überschwingen.
        At(time, ch, 250);
        Assert.InRange(ch.Get(PillWidth), TabWidth, Filtered.CapsuleWidth);
        Assert.InRange(ch.Get(PillTurn), 90, 180);
        At(time, ch, 380);
        Assert.Equal(Filtered.CapsuleWidth, ch.Get(PillWidth), 3);
        Assert.Equal(CapsuleHeight, ch.Get(PillHeight), 3);
        Assert.Equal(0, ch.Get(PillTy), 3);
        Assert.Equal(180, ch.Get(PillTurn), 3);

        // Inhalt der Kapsel ab 300 ms, je 15 ms versetzt; ✕ nach den Einträgen.
        Assert.True(ch.Get(CapsuleItem(0, 'o')) > 0.9);
        Assert.Equal(1, ch.Get(Divider), 3);

        // Zündung: Streif 340–640 zur Kapsel, Spitze um 460 ms, verglüht bis 740; Naht wächst 400 ms ab 340.
        At(time, ch, 340);
        Assert.Equal(0, ch.Get(StreakOpacity), 3);
        Assert.False(choreo.StreakReversed);
        At(time, ch, 460);
        Assert.InRange(ch.Get(StreakOpacity), 0.9, 0.96);
        Assert.InRange(ch.Get(StreakPosition), 0.3, 0.7);
        Assert.True(ch.Get(SeamScale) > 0.5, "Naht wächst schnell (KeySpline): " + ch.Get(SeamScale));
        Assert.Equal(1, ch.Get(SeamOpacity), 3);
        At(time, ch, 614);
        Assert.True(ch.Get(RestHalo) > 0.7, "Hof leuchtet auf, wenn der Streif ankommt: " + ch.Get(RestHalo));
        Assert.True(ch.Get(Halo) > 0.8, "großer Schein: " + ch.Get(Halo));

        // 740 ms: Endzustand, keine Bewegung, kein Bildtakt mehr.
        At(time, ch, 740);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
        Assert.False(ch.IsHooked);
        Assert.Equal(0, ch.Get(StreakOpacity), 3);
        Assert.Equal(0, ch.Get(Halo), 3);
        Assert.Equal(FilteredHalo, ch.Get(RestHalo), 3);
        Assert.Equal(1, ch.Get(SeamScale), 3);
        Assert.Equal(1, ch.Get(Reset), 3);
        Assert.Equal(0, ch.Get(ListDy), 3);
    }

    [Fact]
    public void Full_expand_unfolds_from_the_tab_or_from_the_search_within_620_ms()
    {
        foreach (var fromSearch in new[] { false, true })
        {
            var (choreo, ch, clock, time) = Create(collapsed: true, Filtered);
            choreo.Expand(ToolbarMotionLevel.Full, listDy: -38, Order, fromSearch, Filtered);
            At(time, ch, 0);
            Assert.Equal(-38, ch.Get(ListDy), 3);
            Assert.True(choreo.StreakReversed); // Streif von der Kapsel nach links

            // Kapsel wird Lasche (60–260), die Naht zieht sich zurück (200 ms).
            At(time, ch, 200);
            Assert.Equal(0, ch.Get(SeamScale), 3);
            At(time, ch, 260);
            Assert.Equal(TabWidth, ch.Get(PillWidth), 3);
            Assert.Equal(Filtered.TabTy, ch.Get(PillTy), 3);
            Assert.Equal(0, ch.Get(Divider), 3);

            // Elemente ab 120 ms, je 16 ms versetzt; von der Lasche aus („+“ zuerst) bzw. nach Strg+F von der Suche aus.
            At(time, ch, 150);
            var first = fromSearch ? ToolbarSlot.Search : ToolbarSlot.Add;
            var last = fromSearch ? ToolbarSlot.Add : ToolbarSlot.Search;
            Assert.True(ch.Get(Item(first, 'o')) > 0.3, $"{first} erscheint zuerst: {ch.Get(Item(first, 'o'))}");
            Assert.Equal(0, ch.Get(Item(last, 'o')), 3);
            At(time, ch, 620);
            Assert.Equal(0, ch.Running);
            Assert.Equal(0, clock.Subscribers);
            Assert.All(Order, s =>
            {
                Assert.Equal(1, ch.Get(Item(s, 'o')), 3);
                Assert.Equal(0, ch.Get(Item(s, 'x')), 3);
                Assert.Equal(1, ch.Get(Item(s, 's')), 3);
            });
        }
    }

    [Fact]
    public void Reduced_fades_within_300_ms_without_offsets_scale_turn_or_streak()
    {
        var (choreo, ch, clock, time) = Create(collapsed: false, Filtered);
        var ghosts = 0;
        choreo.TabGhostRequested += (_, _) => ghosts++;
        choreo.Collapse(ToolbarMotionLevel.Reduced, listDy: 38, Order, Filtered);
        Assert.Equal(1, ghosts); // Lasche als Bild, das ausblendet (alt 0–90, neu 60–150)

        // Kapsel steht sofort in ihrer Form (keine Federn, keine Drehung), blendet aber erst ab 60 ms ein.
        At(time, ch, 0);
        Assert.Equal(Filtered.CapsuleWidth, ch.Get(PillWidth), 3);
        Assert.Equal(180, ch.Get(PillTurn), 3);
        Assert.Equal(0, ch.Get(TabOpacity), 3);
        Assert.Equal(1, ch.Get(TabGhost), 3);
        At(time, ch, 60);
        Assert.Equal(0, ch.Get(Item(ToolbarSlot.Search, 'x')), 3); // kein Versatz während des Ausblendens
        Assert.InRange(ch.Get(Item(ToolbarSlot.Search, 'o')), 0.05, 0.5);
        Assert.InRange(ch.Get(TabGhost), 0.2, 0.5);
        At(time, ch, 150);
        Assert.Equal(1, ch.Get(TabOpacity), 3);
        Assert.Equal(0, ch.Get(TabGhost), 3);
        Assert.Equal(1, ch.Get(SeamOpacity), 3);
        Assert.Equal(0, ch.Get(StreakOpacity), 3);
        At(time, ch, 300);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
        Assert.Equal(0, ch.Get(ListDy), 3);

        // Zurück, ebenso höchstens 300 ms: die Leiste erst ab 60 ms, wenn die Liste Platz gemacht hat.
        time.Now = TimeSpan.Zero;
        choreo.Expand(ToolbarMotionLevel.Reduced, listDy: -38, Order, fromSearch: false, Filtered);
        At(time, ch, 50);
        Assert.Equal(0, ch.Get(Item(ToolbarSlot.Search, 'o')), 3);
        Assert.Equal(1, ch.Get(Item(ToolbarSlot.Search, 's')), 3);
        At(time, ch, 300);
        Assert.Equal(0, ch.Running);
        Assert.Equal(1, ch.Get(Item(ToolbarSlot.Search, 'o')), 3);
        Assert.Equal(0, ch.Get(SeamOpacity), 3);
    }

    [Fact]
    public void Instant_level_sets_the_end_state_without_a_frame_clock()
    {
        var (choreo, ch, clock, _) = Create(collapsed: false, Plain);
        choreo.Collapse(ToolbarMotionLevel.Instant, listDy: 38, Order, Plain);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
        Assert.Equal(Plain.CapsuleWidth, ch.Get(PillWidth));
        Assert.Equal(0, ch.Get(ListDy));
        Assert.Equal(0, ch.Get(SeamOpacity)); // ungefiltert: keine Naht
        Assert.All(Order, s => Assert.Equal(0, ch.Get(Item(s, 'o'))));
        choreo.Expand(ToolbarMotionLevel.Instant, listDy: -38, Order, fromSearch: true, Plain);
        Assert.Equal(TabWidth, ch.Get(PillWidth));
        Assert.Equal(Plain.TabTy, ch.Get(PillTy));
        Assert.All(Order, s => Assert.Equal(1, ch.Get(Item(s, 'o'))));
    }

    [Fact]
    public void Toggling_again_continues_from_the_current_values()
    {
        var (choreo, ch, clock, time) = Create(collapsed: false, Filtered);
        choreo.Collapse(ToolbarMotionLevel.Full, listDy: 38, Order, Filtered);
        At(time, ch, 250);
        var width = ch.Get(PillWidth);
        var search = ch.Get(Item(ToolbarSlot.Search, 'o'));
        var add = ch.Get(Item(ToolbarSlot.Add, 'o'));
        var list = ch.Get(ListDy);

        // Mitten im Einfalten wieder ausklappen: Die Liste fällt von dort, wo sie zu sehen ist, alles läuft vom Augenblick aus.
        choreo.Expand(ToolbarMotionLevel.Full, listDy: list - 38, Order, fromSearch: false, Filtered);
        At(time, ch, 250);
        Assert.Equal(width, ch.Get(PillWidth), 3);
        Assert.Equal(search, ch.Get(Item(ToolbarSlot.Search, 'o')), 3);
        Assert.Equal(add, ch.Get(Item(ToolbarSlot.Add, 'o')), 3);
        Assert.Equal(list - 38, ch.Get(ListDy), 3);
        At(time, ch, 260);
        Assert.InRange(Math.Abs(ch.Get(PillWidth) - width), 0, 20); // kein Sprung
        At(time, ch, 250 + 620);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
        Assert.Equal(TabWidth, ch.Get(PillWidth), 3);
        Assert.All(Order, s => Assert.Equal(1, ch.Get(Item(s, 'o')), 3));
    }

    [Fact]
    public void Finish_jumps_to_the_end_and_unhooks_for_a_minimized_window()
    {
        var (choreo, ch, clock, time) = Create(collapsed: false, Filtered);
        choreo.Collapse(ToolbarMotionLevel.Full, listDy: 38, Order, Filtered);
        At(time, ch, 100);
        var finished = 0;
        ch.Finished += (_, _) => finished++;
        ch.Finish();
        Assert.Equal(1, finished);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
        Assert.Equal(Filtered.CapsuleWidth, ch.Get(PillWidth));
        Assert.Equal(1, ch.Get(SeamScale));
        Assert.Equal(0, ch.Get(ListDy));
    }

    [Fact]
    public void Row_change_morphs_axis_after_axis_in_about_320_and_350_ms()
    {
        // 1 → 2: Filter und rechte Gruppe fallen um 48 (je 12 ms versetzt), dann gleiten die Filter nach links, die Felder wachsen.
        var (choreo, ch, clock, time) = Create(collapsed: false, Plain);
        var moved = Order.Select(s => s switch
        {
            ToolbarSlot.Search or ToolbarSlot.Source or ToolbarSlot.Sort => new SlotMove(s, 0, 0, -120),
            ToolbarSlot.FilterAuto or ToolbarSlot.FilterNew or ToolbarSlot.FilterHdr => new SlotMove(s, 420, -48, 0),
            _ => new SlotMove(s, 0, -48, 0),
        }).ToList();
        choreo.MorphRows(moved, toTwo: true, listDy: -48);
        At(time, ch, 0);
        Assert.Equal(-48, ch.Get(Morph(ToolbarSlot.FilterAuto, 'y')), 3);
        Assert.Equal(420, ch.Get(Morph(ToolbarSlot.FilterAuto, 'x')), 3);
        Assert.Equal(-120, ch.Get(Morph(ToolbarSlot.Search, 'w')), 3);
        Assert.Equal(-48, ch.Get(ListDy), 3);
        At(time, ch, 80);
        Assert.Equal(420, ch.Get(Morph(ToolbarSlot.FilterAuto, 'x')), 3); // erst fallen, dann gleiten
        Assert.True(ch.Get(Morph(ToolbarSlot.FilterAuto, 'y')) > -10, "fast unten: " + ch.Get(Morph(ToolbarSlot.FilterAuto, 'y')));
        Assert.True(ch.Get(Morph(ToolbarSlot.Add, 'y')) < ch.Get(Morph(ToolbarSlot.FilterAuto, 'y')), "die rechte Gruppe folgt versetzt");
        Assert.Equal(-120, ch.Get(Morph(ToolbarSlot.Search, 'w')), 3); // die Felder wachsen erst ab 96 ms
        At(time, ch, 320);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);

        // 2 → 1: erst geben die Felder nach (150 ms), dann steigen alle Zeilenwechsler mit der Liste (ab 150 ms).
        time.Now = TimeSpan.Zero;
        var back = moved.Select(m => new SlotMove(m.Slot, -m.Dx, -m.Dy, -m.Dw)).ToList();
        choreo.MorphRows(back, toTwo: false, listDy: 48);
        At(time, ch, 140);
        Assert.Equal(48, ch.Get(Morph(ToolbarSlot.Add, 'y')), 3);
        Assert.Equal(48, ch.Get(ListDy), 3);
        Assert.True(Math.Abs(ch.Get(Morph(ToolbarSlot.Search, 'w'))) < 5, "Felder fast fertig");
        At(time, ch, 350);
        Assert.Equal(0, ch.Running);
        Assert.All(Order, s => Assert.Equal(0, ch.Get(Morph(s, 'y')), 3));
    }

    [Fact]
    public void Tab_hover_grows_to_the_hint_in_160_ms_and_back()
    {
        var (choreo, ch, clock, time) = Create(collapsed: false, Plain);
        choreo.Hover(on: true, hoverWidth: 210, ToolbarMotionLevel.Full);
        At(time, ch, 40);
        Assert.Equal(0, ch.Get(Hint), 3); // Hinweis erst nach 50 ms
        At(time, ch, 160);
        Assert.Equal(210, ch.Get(PillWidth), 3);
        Assert.Equal(1, ch.Get(Hint), 3);
        Assert.Equal(0, ch.Running);
        choreo.Hover(on: false, hoverWidth: 210, ToolbarMotionLevel.Reduced);
        Assert.Equal(TabWidth, ch.Get(PillWidth)); // Reduziert: Breite springt, Hinweis blendet 100 ms
        At(time, ch, 260);
        Assert.Equal(0, ch.Get(Hint), 3);
        Assert.Equal(0, clock.Subscribers);
    }

    [Fact]
    public void Capsule_change_slides_its_width_and_grows_the_seam_when_filtered()
    {
        var (choreo, ch, _, time) = Create(collapsed: true, Plain);
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Filtered);
        At(time, ch, 80);
        Assert.InRange(ch.Get(PillWidth), Plain.CapsuleWidth + 1, Filtered.CapsuleWidth - 1);
        At(time, ch, 400);
        Assert.Equal(Filtered.CapsuleWidth, ch.Get(PillWidth), 3);
        Assert.Equal(1, ch.Get(SeamScale), 3);
        Assert.Equal(FilteredHalo, ch.Get(RestHalo), 3);
        Assert.Equal(1, ch.Get(Reset), 3);
        choreo.CapsuleChanged(ToolbarMotionLevel.Instant, Plain);
        Assert.Equal(Plain.CapsuleWidth, ch.Get(PillWidth));
        Assert.Equal(0, ch.Get(SeamScale));
        Assert.Equal(0, ch.Get(Reset));
    }

    [Fact]
    public void Capsule_change_retracts_the_seam_with_cubic_in_and_restarts_a_running_width_glide()
    {
        // Filter weg (Vorschau refresh): Die Naht zieht sich 200 ms mit CubicEase In zurück (bei 100 ms noch 7/8), Hof und ✕
        // folgen.
        var (choreo, ch, clock, time) = Create(collapsed: true, Filtered);
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Plain);
        At(time, ch, 100);
        Assert.Equal(1 - ToolbarEase.CubicIn(0.5), ch.Get(SeamScale), 3);
        Assert.Equal(0, ch.Get(Reset), 3);
        At(time, ch, 200);
        Assert.Equal(0, ch.Get(SeamScale), 3);
        Assert.Equal(0, ch.Get(RestHalo), 3);
        Assert.Equal(Plain.CapsuleWidth, ch.Get(PillWidth), 3);

        // Ändert sich der Inhalt, während die Breite noch gleitet, gleitet sie vom jetzigen Wert neu (160 ms ab jetzt).
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Filtered);
        At(time, ch, 260);
        var mid = ch.Get(PillWidth);
        Assert.InRange(mid, Plain.CapsuleWidth + 1, Filtered.CapsuleWidth - 1);
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Filtered with { CapsuleWidth = 300 });
        At(time, ch, 360);
        Assert.InRange(ch.Get(PillWidth), mid + 1, 299); // ein bloß neues Ziel wäre jetzt (360 ms) schon angekommen
        At(time, ch, 420);
        Assert.Equal(300, ch.Get(PillWidth), 3);
        At(time, ch, 800);
        Assert.Equal(0, clock.Subscribers);
    }

    [Fact]
    public void Unchanged_capsule_starts_no_motion_and_no_frame_clock()
    {
        // Etwa nach dem Sortieren: gleiche Kapsel, keine Bewegung (vorher lief der Hof 200 ms von 0,35 nach 0,35).
        var (choreo, ch, clock, _) = Create(collapsed: true, Filtered);
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Filtered);
        choreo.CapsuleChanged(ToolbarMotionLevel.Reduced, Filtered);
        Assert.Equal(0, ch.Running);
        Assert.Equal(0, clock.Subscribers);
    }

    [Fact]
    public void Filters_cleared_while_the_seam_still_grows_retract_seam_halo_and_reset()
    {
        // Mitten im Einklappen (die Naht wächst 340–740, der Hof blitzt) werden die Filter aufgehoben: Am Ende leuchtet nichts.
        var (choreo, ch, clock, time) = Create(collapsed: false, Filtered);
        choreo.Collapse(ToolbarMotionLevel.Full, listDy: 38, Order, Filtered);
        At(time, ch, 500);
        Assert.InRange(ch.Get(SeamScale), 0.05, 0.99);
        choreo.CapsuleChanged(ToolbarMotionLevel.Full, Plain);
        At(time, ch, 1000);
        Assert.Equal(0, ch.Get(SeamScale), 3);
        Assert.Equal(0, ch.Get(RestHalo), 3);
        Assert.Equal(0, ch.Get(Reset), 3);
        Assert.Equal(Plain.CapsuleWidth, ch.Get(PillWidth), 3);
        Assert.Equal(0, clock.Subscribers);
    }

    [Fact]
    public void Eases_match_wpf_and_the_preview()
    {
        Assert.Equal(0, ToolbarEase.ExpoOut6(0), 6);
        Assert.Equal(1, ToolbarEase.ExpoOut6(1), 6);
        Assert.Equal(0.875, ToolbarEase.CubicOut(0.5), 6);
        Assert.Equal(0.125, ToolbarEase.CubicIn(0.5), 6);
        Assert.Equal(0.5, ToolbarEase.SineInOut(0.5), 6);
        Assert.Equal(1, ToolbarEase.Spring(1), 6);
        Assert.True(ToolbarEase.Spring(0.75) > 1, "die Feder schwingt leicht über");
        Assert.True(Enumerable.Range(0, 101).Max(i => ToolbarEase.Spring(i / 100.0)) < 1.07, "höchstens 6 % (bei 4–6 px Weg unter 0,4 px)");
        Assert.Equal(1, ToolbarEase.Seam(1), 6);
        Assert.True(ToolbarEase.Seam(0.25) > 0.6, "KeySpline der Naht: schnell, dann sanft");
    }

    private sealed class TestFrameClock : IFrameClock
    {
        private EventHandler? _frame;

        public event EventHandler? Frame
        {
            add
            {
                _frame += value;
                Subscribers++;
            }

            remove
            {
                _frame -= value;
                Subscribers--;
            }
        }

        public int Subscribers { get; private set; }
    }

    private sealed class TestTime : IEffectTime
    {
        public TimeSpan Now { get; set; }
    }
}
