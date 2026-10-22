using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Werkzeugleiste im Hauptfenster (abgenommene Vorschau „Fließleiste mit Lichtnaht“, Wunsch des Nutzers 2026-10-17), auf
/// dem WPF-Testhost erzeugt und angeordnet, nie angezeigt: Geometrie bei den Fenstergrößen der Abnahme (Zeilen, bündige
/// Kanten, 8 px zwischen den Knöpfen, Luft 17/17, Lasche mittig auf der Bildkante und 8 px über der Leiste, Kapsel 10 px
/// über der Liste, Griff auf der Kante), Ein- und Ausklappen (Lasche, Tasten, Menü, gemerkt, ohne Bewegung beim Öffnen), Bewegung je
/// Effektstufe mit gestellter Uhr, Filter als Symbolknöpfe mit Zahl für Bildschirmleser, Automation-Peer mit ExpandCollapse.
/// Läuft allein: Die Prüfungen stellen die Uhr der Leiste und die Effektstufe.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class ToolbarWindowTests(ITestOutputHelper output)
{
    private static readonly ToolbarSlot[] Slots = Enum.GetValues<ToolbarSlot>();

    private static GameConfig[] ManyGames(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Game(i % 3 == 0 ? GameSource.Epic : GameSource.Steam, "g" + i, $"Spiel {i:000}", hdrAuto: i % 4 == 0, isNew: i % 5 == 0))];

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context) Open(Size size, LibraryViewMode view = LibraryViewMode.List, bool collapsed = false, string effects = "Full")
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext(ManyGames(60))
        {
            Appearance = new HdrPilot.App.Theming.StaticAppearanceService(new AppearanceSettings { EffectLevel = effects }),
        };
        context.ConfigService.Update(c =>
        {
            c.Settings.LibraryView = LibraryViewModes.ToConfig(view);
            c.Settings.LibraryToolbarCollapsed = collapsed;
        });
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        var window = new MainWindow();
        window.Attach(model);
        Arrange(window, size);
        return (window, model, context);
    }

    private static void Arrange(MainWindow window, Size size)
    {
        WpfTestHost.Layout(window, size);
        window.FitDetailsColumn();
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, size);
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    private static T Named<T>(MainWindow window, string name) => (T)window.FindName(name);

    private static Rect Box(MainWindow window, FrameworkElement element) =>
        element.TransformToAncestor(Named<Visual>(window, "Root")).TransformBounds(new Rect(element.RenderSize));

    private static FrameworkElement Item(MainWindow window, ToolbarSlot slot) => Named<ToolbarPanel>(window, "ToolbarArea").ItemAt(slot)!;

    /// <summary>Wie gezeichnet im Fenster (mit RenderTransform), in geräteunabhängigen Pixeln ab dem Ursprung des Fensters.</summary>
    private static Rect WindowBox(MainWindow window, FrameworkElement element) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));

    /// <summary>Die zehn Elemente der Leiste zusammen, wie gezeichnet (relativ zu Root wie <see cref="Box"/>).</summary>
    private static Rect ItemsBox(MainWindow window) =>
        Slots.Select(s => Box(window, Item(window, s))).Aggregate(Rect.Union);

    /// <summary>Alle vier Kanten liegen auf ganzen Gerätepixeln des Fensters.</summary>
    private static void AssertWholePixels(MainWindow window, FrameworkElement element, double scale, string what)
    {
        var r = WindowBox(window, element);
        foreach (var edge in new[] { r.Left, r.Top, r.Right, r.Bottom })
        {
            Assert.True(Math.Abs((edge * scale) - Math.Round(edge * scale)) < 0.02, FormattableString.Invariant($"{what}: Kante {edge * scale:0.###} px nicht auf einem ganzen Gerätepixel ({r})"));
        }
    }

    /// <summary>Ein Gerätepixel: Spielraum, wo Leiste, Lasche oder Kapsel (auf ganzen Pixeln) an etwas grenzen, das es nicht ist.</summary>
    private static double OnePixel(MainWindow window) => 1 / Scale(window) + 0.01;

    /// <summary>Zeit und Bildtakt der Leiste von Hand (Werte über die Zeit abtasten), Fenster als sichtbar und aktiv.</summary>
    private static (TestClock Clock, TestTime Time) HandClock(MainWindow window)
    {
        var clock = new TestClock();
        var time = new TestTime();
        window.ToolbarMotion.UseTime(clock, time);
        window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
        return (clock, time);
    }

    private static void At(MainWindow window, TestTime time, double ms)
    {
        time.Now = TimeSpan.FromMilliseconds(ms);
        window.ToolbarMotion.Tick();
    }

    private static double Scale(MainWindow window) => Named<ToolbarPanel>(window, "ToolbarArea").Arrangement!.Scale;

    [Theory]
    [InlineData(1000, 620)] // kleinstes Fenster
    [InlineData(1164, 727)] // 1280×800 geteilt durch 1,1 (Abnahme)
    [InlineData(1159, 725)] // 1280×800 bei 110 % (106 dpi)
    [InlineData(1242, 698)] // 1366×768 geteilt durch 1,1 (Abnahme)
    [InlineData(1237, 696)] // 1366×768 bei 110 % (106 dpi): Liste 807, einzeilig wie bei 100 %
    [InlineData(1280, 800)]
    [InlineData(1366, 768)]
    [InlineData(1600, 1000)] // Startfenster
    [InlineData(1745, 982)] // 1920×1080 geteilt durch 1,1 (Abnahme)
    [InlineData(1739, 978)] // 1920×1080 bei 110 % (106 dpi)
    [InlineData(1920, 1080)]
    [InlineData(3127, 1309)] // 3440×1440 geteilt durch 1,1 (Abnahme)
    [InlineData(3116, 1304)] // 3440×1440 bei 110 % (106 dpi)
    [InlineData(3440, 1440)]
    public void List_toolbar_geometry_is_flush_with_even_gaps_and_air_and_the_tab_8_px_above(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, _, _) = Open(size);
            var toolbar = Named<ToolbarPanel>(window, "ToolbarArea");
            var arrangement = toolbar.Arrangement!;
            var scale = arrangement.Scale;
            var card = Box(window, Named<FrameworkElement>(window, "ListCard"));
            var bar = Box(window, toolbar);
            var hero = Box(window, Named<FrameworkElement>(window, "Hero"));
            var edge = hero.Bottom;

            // Zeilen nach der Leiter: einzeilig ab einer Listenbreite von 806, sonst zwei (in der schmalsten Liste drei).
            var expectedRows = card.Width >= ToolbarLayout.OneRowWidth - 0.5 ? 1 : card.Width >= ToolbarLayout.SecondRowWidth ? 2 : 3;
            Assert.Equal(expectedRows, window.ToolbarRows);
            Assert.Equal(ToolbarLayout.HeightFor(expectedRows), bar.Height, 0.5);

            // Jedes Element 40 hoch; je Zeile links und rechts bündig mit der Liste, dazwischen round(8 · Skalierung) Gerätepixel.
            var boxes = Slots.ToDictionary(s => s, s => Box(window, Item(window, s)));
            Assert.All(boxes.Values, b => Assert.Equal(40, b.Height, 0.5));
            var gaps = new List<double>();
            foreach (var row in Slots.GroupBy(s => Math.Round(boxes[s].Top)))
            {
                var items = row.OrderBy(s => boxes[s].Left).ToList();
                Assert.Equal(card.Left, boxes[items[0]].Left, 0.5);
                if (items.Count > 1 || expectedRows == 1)
                {
                    Assert.Equal(card.Right, boxes[items[^1]].Right, 0.6);
                }

                for (var i = 1; i < items.Count; i++)
                {
                    var gap = boxes[items[i]].Left - boxes[items[i - 1]].Right;
                    if (items[i] == ToolbarSlot.View)
                    {
                        Assert.True(gap >= (arrangement.SortIconOnly ? 16 : 24) - 0.6, $"{width}×{height}: Freiraum vor der Ansicht {gap:0.0}");
                        continue;
                    }

                    Assert.Equal(Math.Round(8 * scale, MidpointRounding.AwayFromZero), gap * scale, 1);
                    gaps.Add(gap);
                }
            }

            // Luft über der Leiste (zur Bildkante) gleich der darunter (zur Liste): 17/17; der Griff mittig auf der Kante.
            var above = bar.Top - edge;
            var below = card.Top - bar.Bottom;
            Assert.Equal(17, above, 0.5);
            Assert.Equal(17, below, 0.5);
            var grip = Box(window, Named<FrameworkElement>(window, "HeroSplitter"));
            Assert.Equal(edge, grip.Top + (grip.Height / 2), 0.5);

            // Lasche rechts, mittig auf der Bildkante (Wunsch des Nutzers 2026-10-22: höchstens einen halben Gerätepixel
            // daneben), ihre Unterkante 8 px über der Leiste (so viel wie zwischen den Knöpfen): round(8 · Skalierung)
            // Gerätepixel über den Knöpfen, auf einen Pixel genau, rechts bündig mit ihnen.
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var knobs = ItemsBox(window);
            Assert.Equal(36, tab.Width, 0.5);
            Assert.Equal(Math.Round(18 * scale) / scale, tab.Height, 3);
            Assert.Equal(card.Right, tab.Right, 0.6);
            Assert.Equal(knobs.Right, tab.Right, 3);
            Assert.True(Math.Abs((tab.Top + (tab.Height / 2) - edge) * scale) <= 0.5 + 1e-6, FormattableString.Invariant($"{width}×{height}: Mitte der Lasche {(tab.Top + (tab.Height / 2) - edge) * scale:0.###} px neben der Kante"));
            Assert.Equal(Math.Round(8 * scale, MidpointRounding.AwayFromZero) / scale, knobs.Top - tab.Bottom, OnePixel(window));

            // Kanten auf ganzen Gerätepixeln des Fensters (nicht nur der Leiste): Knöpfe, Felder und Lasche.
            Assert.All(Slots, s => AssertWholePixels(window, Item(window, s), scale, s.ToString()));
            AssertWholePixels(window, Named<FrameworkElement>(window, "ToolbarToggle"), scale, "Lasche");

            // Inhalt des Titelbereichs 22 px über der Bildkante (Platz für Lasche und Kapsel).
            var compact = Named<FrameworkElement>(window, "HeroCompact");
            Assert.Equal(22, compact.Margin.Bottom);
            output.WriteLine(FormattableString.Invariant(
                $"Liste {width}x{height}: L {card.Width:0.##}, Skalierung {scale:0.###}, Zeilen {window.ToolbarRows}, Stufe {arrangement.Stage}, Suche {boxes[ToolbarSlot.Search].Width:0.##}, Quelle {boxes[ToolbarSlot.Source].Width:0.##}, Sortierung {boxes[ToolbarSlot.Sort].Width:0.##}, Abstände {string.Join('/', gaps.Select(g => g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Distinct())}, Luft {above:0.##}/{below:0.##}, Lasche über Knöpfen {knobs.Top - tab.Bottom:0.##} ({(knobs.Top - tab.Bottom) * scale:0.##} px), Griffmitte − Kante {grip.Top + (grip.Height / 2) - edge:0.##}, Kante rechts {card.Right - boxes[ToolbarSlot.Add].Right:0.##}, Ausgleich {window.ToolbarPixelSnap.X * scale:0.##}/{window.ToolbarPixelSnap.Y * scale:0.##} px"));
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620)]
    [InlineData(1164, 727)]
    [InlineData(1745, 982)]
    [InlineData(3440, 1440)]
    public void Collapsed_capsule_sits_on_the_edge_10_px_above_the_list_and_the_hero_never_jumps(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, model, _) = Open(size);
            var heroExpanded = window.HeroHeight;
            var rows = window.ToolbarRows;
            var range = Named<HeroSplitter>(window, "HeroSplitter").Maximum;
            var cardTop = Box(window, Named<FrameworkElement>(window, "ListCard")).Top;

            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            Assert.True(window.IsToolbarCollapsed);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "ToolbarArea").Visibility); // ohne Bewegung (nie angezeigt)

            // Titelbereich und Bereich des Griffs rechnen weiter mit der ausgeklappten Leiste: nichts springt.
            Assert.Equal(heroExpanded, window.HeroHeight);
            Assert.Equal(range, Named<HeroSplitter>(window, "HeroSplitter").Maximum);
            Assert.Equal(rows, window.ToolbarRows);

            var edge = Box(window, Named<FrameworkElement>(window, "Hero")).Bottom;
            var capsule = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var card = Box(window, Named<FrameworkElement>(window, "ListCard"));
            Assert.Equal(24, capsule.Height, 0.5);
            Assert.Equal(edge - 12, capsule.Top, OnePixel(window)); // mittig auf der Kante, auf ganzen Gerätepixeln
            Assert.True(Math.Abs((capsule.Top + (capsule.Height / 2) - edge) * Scale(window)) <= 0.5 + 1e-6, "Kapsel mittig auf der Kante (höchstens ein halber Gerätepixel)");
            Assert.Equal(card.Right, capsule.Right, 0.6); // rechtsbündig
            AssertWholePixels(window, Named<FrameworkElement>(window, "ToolbarToggle"), Scale(window), "Kapsel");

            // 10 px darunter die Liste; die Kapsel liegt auf ganzen Gerätepixeln, Bildkante und Liste nicht: ein Pixel Spielraum.
            Assert.Equal(10, card.Top - capsule.Bottom, OnePixel(window));
            Assert.Equal(cardTop - card.Top, ToolbarLayout.HeightFor(rows) + 12, 0.5); // Gewinn: 52 bzw. 100
            var grip = Box(window, Named<FrameworkElement>(window, "HeroSplitter"));
            Assert.Equal(edge, grip.Top + (grip.Height / 2), 0.5);
            Assert.True(capsule.Left >= grip.Left + (grip.Width / 2) + 32 - 0.5, "Kapsel höchstens bis 32 px vor die Mitte des Griffs");
            Assert.Equal(card.Top, Box(window, Named<FrameworkElement>(window, "LibrarySplitter")).Top, 0.5); // Trenner beginnt an der Liste

            output.WriteLine(FormattableString.Invariant($"Liste eingeklappt {width}x{height}: Kapsel {capsule.Top - edge:0.##} … {capsule.Bottom - edge:0.##} um die Kante, Luft bis zur Liste {card.Top - capsule.Bottom:0.##}, Liste {cardTop - card.Top:0.##} höher, Titelbereich {heroExpanded} → {window.HeroHeight}"));
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620)]
    [InlineData(1280, 800)]
    [InlineData(1745, 982)]
    [InlineData(3440, 1440)]
    public void Grid_keeps_17_px_air_above_and_below_expanded_and_under_the_capsule(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, model, _) = Open(size, LibraryViewMode.Grid);
            var grid = Named<ListBox>(window, "GameGrid");
            var hero = window.HeroHeight;
            var edge = Box(window, Named<FrameworkElement>(window, "Hero")).Bottom;
            var bar = Box(window, Named<FrameworkElement>(window, "ToolbarArea"));
            var tile = Box(window, (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(17, bar.Top - edge, 0.5);
            Assert.Equal(17, tile.Top - bar.Bottom, 0.5);
            Assert.Equal(10, Box(window, grid).Top - bar.Bottom, 0.5); // gescrollte Kacheln enden 10 px unter der Leiste
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var scale = Scale(window);
            Assert.True(Math.Abs((tab.Top + (tab.Height / 2) - edge) * scale) <= 0.5 + 1e-6, "Lasche mittig auf der Kante (vorher −4 … +14)");
            Assert.Equal(Math.Round(8 * scale, MidpointRounding.AwayFromZero) / scale, ItemsBox(window).Top - tab.Bottom, OnePixel(window));
            Assert.Equal(8, bar.Top - tab.Bottom, OnePixel(window));

            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            Assert.Equal(hero, window.HeroHeight);
            var capsule = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            tile = Box(window, (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.True(Math.Abs((capsule.Top + (capsule.Height / 2) - edge) * scale) <= 0.5 + 1e-6, "Kapsel mittig auf der Kante: das Verwandeln ändert nur die Größe");
            Assert.Equal(17, tile.Top - capsule.Bottom, OnePixel(window));
            output.WriteLine(FormattableString.Invariant($"Raster {width}x{height}: Luft {bar.Top - edge:0.##}/{tile.Top - bar.Bottom:0.##}, Lasche {tab.Top - edge:0.##} … {tab.Bottom - edge:0.##}, eingeklappt Kachel {tile.Top - capsule.Bottom:0.##} unter der Kapsel"));
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620)]
    [InlineData(1164, 727)]
    [InlineData(1366, 768)]
    [InlineData(1745, 982)]
    [InlineData(3440, 1440)]
    public void Shelf_toolbar_is_one_row_and_the_capsule_stays_16_px_below_the_card(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, model, _) = Open(size, LibraryViewMode.Shelf);
            Assert.Equal(1, window.ToolbarRows);
            var bar = Box(window, Named<FrameworkElement>(window, "ToolbarArea"));
            var edge = Box(window, Named<FrameworkElement>(window, "Hero")).Bottom;
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var shelfTop = Box(window, Named<FrameworkElement>(window, "GameShelf")).Top;
            var shelf = Named<ListBox>(window, "GameShelf");
            var firstTile = Box(window, (FrameworkElement)shelf.ItemContainerGenerator.ContainerFromIndex(0));
            var scale = Scale(window);

            // Lasche in der Fuge zwischen Leiste und Kachelreihe, mittig auf der Bildkante (Wunsch des Nutzers 2026-10-22):
            // round(8 · Skalierung) Gerätepixel unter den Knöpfen (auf einen Pixel genau), 3 px über den Kacheln.
            Assert.True(Math.Abs((tab.Top + (tab.Height / 2) - edge) * scale) <= 0.5 + 1e-6, "Lasche mittig auf der Kante");
            Assert.Equal(Math.Round(8 * scale, MidpointRounding.AwayFromZero) / scale, tab.Top - ItemsBox(window).Bottom, OnePixel(window));
            Assert.Equal(8, tab.Top - bar.Bottom, OnePixel(window));
            Assert.Equal(3, firstTile.Top - tab.Bottom, OnePixel(window));
            Assert.Equal(bar.Bottom, shelfTop, 0.5); // die Reihe beginnt an der Leiste: Platz zum Anheben der Kacheln
            Assert.Equal(bar.Right, tab.Right, 0.6);
            Assert.Equal(ItemsBox(window).Right, tab.Right, 3);

            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            var capsule = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var card = Box(window, Named<FrameworkElement>(window, "AutomationCard"));
            Assert.Equal(bar.Top + 8, capsule.Top, 0.5); // mittig in der früheren Leistenzeile
            Assert.Equal(bar.Right, capsule.Right, 0.6);
            Assert.True(capsule.Top >= card.Bottom + 16 - 0.5, $"{width}×{height}: Kapsel {capsule.Top:0.#}, Karte bis {card.Bottom:0.#}");
            Assert.Equal(shelfTop, Box(window, Named<FrameworkElement>(window, "GameShelf")).Top, 0.5); // die Kachelreihe bleibt
            Assert.Equal(edge, Box(window, Named<FrameworkElement>(window, "Hero")).Bottom, 0.5);
            Assert.Equal(ShowcaseLayout.LargeHeroSink, ((TranslateTransform)Named<FrameworkElement>(window, "HeroLargeSink").RenderTransform).Y);
            output.WriteLine(FormattableString.Invariant($"Regal {width}x{height}: Lasche {tab.Top - bar.Bottom:0.##} unter der Leiste, Kapsel {capsule.Top - card.Bottom:0.##} unter der Karte"));
            window.Close();
        });
    }

    /// <summary>Bildkante (Unterkante des Titelbilds) in geräteunabhängigen Pixeln des Fensters.</summary>
    private static double Edge(MainWindow window) => WindowBox(window, Named<FrameworkElement>(window, "Hero")).Bottom;

    /// <summary>
    /// Lasche bzw. Kapsel liegt mittig auf der Bildkante: ihre Mitte höchstens einen halben Gerätepixel neben der Kante, ihre
    /// Kanten auf ganzen Gerätepixeln.
    /// </summary>
    private static double AssertCentredOnEdge(MainWindow window, double scale, string what)
    {
        var toggle = Named<FrameworkElement>(window, "ToolbarToggle");
        var box = WindowBox(window, toggle);
        var offset = ((box.Top + (box.Height / 2)) - Edge(window)) * scale;
        Assert.True(Math.Abs(offset) <= 0.5 + 1e-6, FormattableString.Invariant($"{what}: Mitte {offset:0.###} Gerätepixel neben der Bildkante (Lasche {box.Top * scale:0.##}–{box.Bottom * scale:0.##}, Kante {Edge(window) * scale:0.##})"));
        AssertWholePixels(window, toggle, scale, what);
        return offset;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void Tab_and_capsule_sit_centred_on_the_edge_within_half_a_device_pixel_at_every_scale_height_and_view(double scale)
    {
        // Wunsch des Nutzers 2026-10-22: Die Lasche „Leiste ausblenden“ saß in der Liste −16 … +2 um die Bildkante, im Raster
        // −4 … +14; Lichtnaht und Griff trafen sie unten. Jetzt liegt ihre Mitte auf der Kante: ausgeklappt, beim Überfahren,
        // nach Ziehen am Griff, nach dem Wechsel der Ansicht und anderen Fenstergrößen; eingeklappt ebenso die Kapsel (Liste,
        // Raster; im Regal steht sie in der früheren Leistenzeile).
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1600, 1000));
            VisualTreeHelper.SetRootDpi(window, new DpiScale(scale, scale));
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var worst = 0.0;
            var checks = 0;
            foreach (var size in new[] { new Size(1280, 800), new Size(1745, 982), new Size(3127, 1309) })
            {
                foreach (var view in LibraryViewModes.Ordered)
                {
                    model.ViewMode = view;
                    Arrange(window, size);
                    Assert.Equal(scale, Scale(window));
                    var what = FormattableString.Invariant($"{scale:0.##} · {size.Width}×{size.Height}, {view}");
                    worst = Math.Max(worst, Math.Abs(AssertCentredOnEdge(window, scale, what + " ausgeklappt")));
                    window.PreviewTabHover();
                    worst = Math.Max(worst, Math.Abs(AssertCentredOnEdge(window, scale, what + " Lasche überfahren")));

                    // Am Griff gezogen (ganze geräteunabhängige Pixel, bei 125 und 175 % keine ganzen Gerätepixel).
                    foreach (var delta in new[] { 37.4, -61.2 })
                    {
                        splitter.RaiseEvent(new DragStartedEventArgs(0, 5));
                        splitter.RaiseEvent(new DragDeltaEventArgs(0, delta));
                        splitter.RaiseEvent(new DragCompletedEventArgs(0, delta, false));
                        Arrange(window, size);
                        worst = Math.Max(worst, Math.Abs(AssertCentredOnEdge(window, scale, FormattableString.Invariant($"{what} nach Ziehen um {delta}, Titelbereich {window.HeroHeight}"))));
                        checks++;
                    }

                    model.IsToolbarCollapsed = true;
                    Arrange(window, size);
                    if (view != LibraryViewMode.Shelf)
                    {
                        worst = Math.Max(worst, Math.Abs(AssertCentredOnEdge(window, scale, what + " eingeklappt (Kapsel)")));
                    }

                    model.IsToolbarCollapsed = false;
                    Arrange(window, size);
                    AssertCentredOnEdge(window, scale, what + " wieder ausgeklappt");
                    var tab = WindowBox(window, Named<FrameworkElement>(window, "ToolbarToggle"));
                    var knobs = Slots.Select(s => WindowBox(window, Item(window, s))).Aggregate(Rect.Union);
                    var gap = view == LibraryViewMode.Shelf ? tab.Top - knobs.Bottom : knobs.Top - tab.Bottom;
                    Assert.True(Math.Abs((gap * scale) - Math.Round(8 * scale, MidpointRounding.AwayFromZero)) <= 1 + 1e-6, FormattableString.Invariant($"{what}: Lasche {gap * scale:0.##} Gerätepixel vor der Leiste"));
                }
            }

            output.WriteLine(FormattableString.Invariant($"{scale * 100:0} %: {checks} Höhen nach Ziehen, größte Abweichung der Mitte {worst:0.###} Gerätepixel"));
            model.ViewMode = LibraryViewMode.List;
            window.Close();
        });
    }

    [Theory]
    [InlineData(LibraryViewMode.List)]
    [InlineData(LibraryViewMode.Grid)]
    public void Tab_turns_into_the_capsule_and_back_by_size_only_centred_on_the_edge(LibraryViewMode view)
    {
        // Wunsch des Nutzers 2026-10-22: Lasche und Kapsel liegen beide mittig auf der Bildkante, das Verwandeln (Voll, 740 bzw.
        // 620 ms) ändert nur die Größe; die Mitte bleibt in jedem Bild auf der Kante.
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, view);
            var (_, time) = HandClock(window);
            var scale = Scale(window);
            var toggle = Named<FrameworkElement>(window, "ToolbarToggle");
            var heights = new HashSet<double>();
            foreach (var collapse in new[] { true, false })
            {
                model.IsToolbarCollapsed = collapse;
                Arrange(window, size);
                var start = time.Now.TotalMilliseconds;
                for (var t = 0; t <= 800; t += 20)
                {
                    At(window, time, start + t);
                    AssertCentredOnEdge(window, scale, FormattableString.Invariant($"{view}, {(collapse ? "einklappen" : "ausklappen")} bei {t} ms"));
                    heights.Add(Math.Round(toggle.Height * scale));
                }
            }

            Assert.Contains(Math.Round(18 * scale), heights);
            Assert.Contains(Math.Round(24 * scale), heights);
            Assert.True(heights.Count > 3, "Höhe gleitet: " + string.Join(", ", heights));
            window.Close();
        });
    }

    [Fact]
    public void Toggle_by_tab_keys_and_menu_is_remembered_and_restored_without_motion()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1600, 1000);
            var (window, model, context) = Open(size);
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            Assert.True(toggle.IsExpanded);
            Assert.False(context.Config.Settings.LibraryToolbarCollapsed);

            // Lasche (Klick, Eingabetaste, Leertaste und UI-Automation melden dasselbe): eingeklappt und sofort gespeichert.
            UIElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.ExpandCollapse).Should<IExpandCollapseProvider>().Collapse();
            Assert.True(model.IsToolbarCollapsed);
            Assert.False(toggle.IsExpanded);
            Assert.True(context.Config.Settings.LibraryToolbarCollapsed);

            // Strg+Umschalt+F und Strg+F1 schalten um.
            Assert.True(window.HandleKey(Key.F, ModifierKeys.Control | ModifierKeys.Shift, null));
            Assert.False(model.IsToolbarCollapsed);
            Assert.True(window.HandleKey(Key.F1, ModifierKeys.Control, null));
            Assert.True(model.IsToolbarCollapsed);

            // Strg+F klappt dauerhaft aus (gemerkt) und geht in die Suche.
            Assert.True(window.HandleKey(Key.F, ModifierKeys.Control, null));
            Assert.False(model.IsToolbarCollapsed);
            Assert.False(context.Config.Settings.LibraryToolbarCollapsed);
            Assert.True(window.HandleKey(Key.F, ModifierKeys.Control, null)); // ausgeklappt bleibt es so
            Assert.False(model.IsToolbarCollapsed);

            // Menü der Leiste: erster Eintrag blendet aus bzw. ein, Filter mit Haken, Zahl und Kürzel, alles zurücksetzen.
            var menu = Assert.IsType<ContextMenu>(toggle.ContextMenu);
            menu.PlacementTarget = toggle;
            WpfTestHost.DoEvents();
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["ToolbarMenuToggle", "ToolbarMenuAuto", "ToolbarMenuNew", "ToolbarMenuHdr", "ToolbarMenuReset"], items.Select(AutomationProperties.GetAutomationId));
            Assert.Equal(LibraryStrings.Toolbar_Hide, model.ToolbarToggleText);
            items[0].Command.Execute(null);
            Assert.True(model.IsToolbarCollapsed);
            Assert.Equal(LibraryStrings.Toolbar_Show, model.ToolbarToggleText);
            Assert.IsType<ContextMenu>(Named<ToolbarPanel>(window, "ToolbarArea").ContextMenu); // auch auf freier Fläche der Leiste
            window.Close();

            // Neu geöffnet: eingeklappt, ohne Bewegung, ohne Bildtakt.
            var reopened = new MainWindow();
            var model2 = new LibraryViewModel(context, new FakeDialogs());
            reopened.Attach(model2);
            Arrange(reopened, size);
            Assert.True(reopened.IsToolbarCollapsed);
            Assert.Equal(0, reopened.ToolbarMotion.Running);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(reopened, "ToolbarArea").Visibility);
            Assert.False(Named<ToolbarToggle>(reopened, "ToolbarToggle").IsExpanded);
            reopened.Close();
        });
    }

    [Fact]
    public void Alt_shortcuts_switch_the_filters_also_when_collapsed_and_the_capsule_shows_them()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1745, 982), collapsed: true);
            Assert.True(window.HandleKey(Key.A, ModifierKeys.Alt, null));
            Assert.True(model.OnlySelected);
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Assert.True(model.OnlyNew);
            Assert.True(window.HandleKey(Key.H, ModifierKeys.Alt, null));
            Assert.True(model.OnlyHdrCapable);
            Assert.True(window.HandleKey(Key.H, ModifierKeys.Alt, null));
            Assert.False(model.OnlyHdrCapable);
            Assert.True(model.IsToolbarCollapsed); // die Leiste bleibt eingeklappt

            // Kapsel: gefiltert mit ✕ (eigener Knopf), Zusammenfassung für Bildschirmleser.
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            Assert.True(toggle.IsFiltered);
            Assert.Contains(LibraryStrings.Filter_OnlySelected, model.ToolbarSummary, StringComparison.Ordinal);
            Assert.Contains(LibraryStrings.Filter_OnlyNew, model.ToolbarSummary, StringComparison.Ordinal);
            Assert.Equal(model.ToolbarSummary, AutomationProperties.GetItemStatus(toggle));
            var reset = Named<Button>(window, "ToolbarResetButton");
            Assert.Equal(Visibility.Visible, reset.Visibility);
            Assert.Equal(LibraryStrings.Toolbar_ResetName, AutomationProperties.GetName(reset));
            Assert.True(reset.Focusable);
            reset.Command.Execute(null);
            Assert.False(model.IsFiltered);
            Assert.Equal(LibraryStrings.Toolbar_NoFilters, AutomationProperties.GetItemStatus(toggle));
            Assert.False(toggle.IsFiltered);
            window.Close();
        });
    }

    [Fact]
    public void Toggle_peer_offers_expand_collapse_name_status_and_help_and_the_collapsed_toolbar_leaves_the_tree()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1745, 982));
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);
            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
            Assert.Equal(LibraryStrings.Toolbar_ToggleName, peer.GetName());
            Assert.Equal("ToolbarToggle", peer.GetAutomationId());
            Assert.Equal(LibraryStrings.Toolbar_NoFilters, peer.GetItemStatus());
            Assert.Equal(LibraryStrings.Toolbar_ToggleHelp, peer.GetHelpText());
            Assert.Equal(LibraryStrings.Key_CtrlShiftF, peer.GetAcceleratorKey());
            Assert.True(toggle.Focusable && KeyboardNavigation.GetIsTabStop(toggle));
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            expand.Expand(); // schon ausgeklappt: nichts
            Assert.False(model.IsToolbarCollapsed);

            var root = UIElementAutomationPeer.CreatePeerForElement(window);
            Assert.Contains(Descendants(root), p => p.GetAutomationId() == "LibraryToolbar");
            expand.Collapse();
            Arrange(window, new Size(1745, 982));
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            root.ResetChildrenCache();
            var hidden = Descendants(root).Single(p => p.GetAutomationId() == "LibraryToolbar");
            Assert.False(hidden.IsControlElement()); // nicht in der Steueransicht (Bildschirmleser)
            Assert.False(hidden.IsContentElement());
            Assert.True(hidden.IsOffscreen());
            Assert.Null(hidden.GetChildren());
            Assert.DoesNotContain(Descendants(root), p => p.GetAutomationId() == "SearchBox");
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(Named<FrameworkElement>(window, "ToolbarArea")));

            // Ansage für Bildschirmleser beim Umschalten (UI-Automation: Notification) – ohne Fehler auch ohne Zuhörer.
            model.ToggleFilter(ToolbarFilter.New);
            Assert.StartsWith("Gefiltert: ", peer.GetItemStatus(), StringComparison.Ordinal);
            expand.Expand();
            Assert.False(model.IsToolbarCollapsed);
            window.Close();
        });

        static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
        {
            foreach (var child in peer.GetChildren() ?? [])
            {
                yield return child;
                foreach (var inner in Descendants(child))
                {
                    yield return inner;
                }
            }
        }
    }

    [Fact]
    public void Filters_are_icon_toggles_with_counts_for_screen_readers_and_one_second_tooltips()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1745, 982));
            foreach (var (id, kind, status, key) in new[]
            {
                ("OnlySelectedToggle", FilterGlyphKind.Switch, (Func<string>)(() => model.OnlySelectedStatus), LibraryStrings.Key_AltA),
                ("OnlyNewToggle", FilterGlyphKind.New, () => model.OnlyNewStatus, LibraryStrings.Key_AltN),
                ("OnlyHdrCapableToggle", FilterGlyphKind.Hdr, () => model.OnlyHdrCapableStatus, LibraryStrings.Key_AltH),
            })
            {
                var filter = Named<ToolbarPanel>(window, "ToolbarArea").Children.OfType<FilterToggle>().Single(f => AutomationProperties.GetAutomationId(f) == id);
                Assert.Equal(kind, filter.Kind);
                Assert.Equal(40, filter.ActualWidth, 0.5);
                Assert.Equal(40, filter.ActualHeight, 0.5);
                var peer = UIElementAutomationPeer.CreatePeerForElement(filter);
                Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
                Assert.Equal(status(), peer.GetItemStatus());
                Assert.EndsWith(" Spiele", peer.GetItemStatus(), StringComparison.Ordinal);
                Assert.Equal(key, peer.GetAcceleratorKey());
                Assert.False(string.IsNullOrWhiteSpace(peer.GetHelpText()));
                Assert.StartsWith("Nur ", peer.GetName(), StringComparison.Ordinal); // ganzer Satz
                Assert.Equal(1000, ToolTipService.GetInitialShowDelay(filter));
                Assert.Equal(400, ToolTipService.GetBetweenShowDelay(filter));
                var tip = Assert.IsType<ToolTip>(filter.ToolTip);
                var content = Assert.IsType<ToolbarTip>(tip.Content);
                Assert.Equal(key, content.Shortcut);
            }

            // Zahl in der Plakette und im ItemStatus folgt dem Filter; an: „Filter an“ hervorgehoben in der Fußzeile.
            var newFilter = Named<FilterToggle>(window, "FilterNewButton");
            Assert.Equal(model.NewCount, newFilter.Count);
            Assert.Equal(LibraryText.Games(model.NewCount), model.OnlyNewStatus);
            Assert.Equal(string.Empty, model.OnlyNewTipLead);
            UIElementAutomationPeer.CreatePeerForElement(newFilter).GetPattern(PatternInterface.Toggle).Should<IToggleProvider>().Toggle();
            Assert.True(model.OnlyNew);
            Assert.Equal(LibraryStrings.Tip_FilterOnLead, model.OnlyNewTipLead);
            Assert.Contains(" von ", model.OnlyNewTip, StringComparison.Ordinal);

            // Ansicht und „Spiel hinzufügen“ nur als Symbol (122 und 40), mit Tooltip.
            Assert.Equal(122, Named<FrameworkElement>(window, "ViewSegment").ActualWidth, 0.6);
            Assert.Equal(40, Named<FrameworkElement>(window, "AddGameButton").ActualWidth, 0.6);
            Assert.Equal(1000, ToolTipService.GetInitialShowDelay(Named<FrameworkElement>(window, "ViewGridButton")));
            window.Close();
        });
    }

    [Fact]
    public void Full_collapse_moves_only_opacity_and_transforms_and_ends_after_740_ms()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size);
            var (clock, time) = HandClock(window);
            var search = Item(window, ToolbarSlot.Search);
            var add = Item(window, ToolbarSlot.Add);
            var content = (TranslateTransform)Named<FrameworkElement>(window, "ListCardContent").RenderTransform;
            var cardTop = Box(window, Named<FrameworkElement>(window, "ListCard")).Top;

            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            At(window, time, 0);
            Assert.Equal(1, clock.Subscribers);
            var newTop = Box(window, Named<FrameworkElement>(window, "ListCard")).Top;
            Assert.Equal(cardTop - newTop, content.Y, 0.5); // Endlayout sofort, die Liste sichtbar noch an ihrem alten Platz
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "ToolbarArea").Visibility);
            Assert.NotNull(Named<FrameworkElement>(window, "BodyArea").Clip); // unten geschnitten, solange sie gleitet

            var toggle = Named<FrameworkElement>(window, "ToolbarToggle");
            At(window, time, 70);
            Assert.InRange(search.Opacity, 0.8, 0.95);
            Assert.Equal(1, add.Opacity, 3);
            var group = (TransformGroup)search.RenderTransform;
            Assert.True(((TranslateTransform)group.Children[1]).X > 0);
            Assert.Equal(36, toggle.Width, 0.5); // die Lasche wird erst ab 120 ms zur Kapsel
            Assert.Equal(18, toggle.Height, 0.5);
            At(window, time, 300);
            Assert.Equal(0, search.Opacity, 3);
            Assert.Equal(0, add.Opacity, 3);
            Assert.True(content.Y < 1, "Liste oben: " + content.Y);
            var growing = toggle.Width;

            At(window, time, 740);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers); // keine Uhr nach dem Übergang
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "ToolbarArea").Visibility);
            Assert.Null(Named<FrameworkElement>(window, "BodyArea").Clip);
            Assert.Equal(0, content.Y);
            Assert.Equal(24, toggle.Height, 0.5);
            Assert.InRange(growing, 37, toggle.Width - 1); // Breite gleitet, springt nicht
            window.Close();
        });
    }

    [Fact]
    public void Reduced_fades_within_300_ms_and_off_minimized_or_hidden_is_instant()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, effects: "Reduced");
            var (clock, time) = HandClock(window);
            var search = Item(window, ToolbarSlot.Search);
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            At(window, time, 60);
            Assert.InRange(search.Opacity, 0.05, 0.5);
            Assert.Equal(window.ToolbarPixelSnap.X, ((TranslateTransform)((TransformGroup)search.RenderTransform).Children[1]).X, 6); // kein Versatz (nur der Ausgleich auf ganze Pixel)
            At(window, time, 300);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers);

            // Reduziert minimiert mitten im Ausklappen: Die Stufe bleibt, nur das Fenster ändert sich; die Leiste steht sofort.
            model.IsToolbarCollapsed = false;
            Arrange(window, size);
            At(window, time, 360);
            Assert.True(window.ToolbarMotion.Running > 0);
            window.Motion!.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers);
            Assert.Equal(1, search.Opacity, 3);
            window.Close();

            // Aus: Endzustand sofort.
            (window, model, _) = Open(size, effects: "Off");
            HandClock(window);
            model.IsToolbarCollapsed = true;
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "ToolbarArea").Visibility);
            window.Close();

            // Minimiert mitten in der Bewegung: springt ans Ende, danach kein Bildtakt.
            (window, model, _) = Open(size);
            (clock, time) = HandClock(window);
            model.IsToolbarCollapsed = true;
            At(window, time, 100);
            Assert.True(window.ToolbarMotion.Running > 0);
            window.Motion!.SetWindowState(visible: true, minimized: true, active: false);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "ToolbarArea").Visibility);
            window.Close();
        });
    }

    [Fact]
    public void Expanding_again_mid_collapse_continues_from_where_it_is()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size);
            var (clock, time) = HandClock(window);
            var content = (TranslateTransform)Named<FrameworkElement>(window, "ListCardContent").RenderTransform;
            var toggle = Named<FrameworkElement>(window, "ToolbarToggle");
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            At(window, time, 250);
            var visibleTop = Box(window, Named<FrameworkElement>(window, "ListCard")).Top + content.Y;
            var width = toggle.Width;
            var search = Item(window, ToolbarSlot.Search).Opacity;

            model.IsToolbarCollapsed = false;
            Arrange(window, size);
            At(window, time, 250);
            Assert.Equal(visibleTop, Box(window, Named<FrameworkElement>(window, "ListCard")).Top + content.Y, 0.5); // kein Sprung der Liste
            Assert.Equal(width, toggle.Width, 0.5);
            Assert.Equal(search, Item(window, ToolbarSlot.Search).Opacity, 3);
            At(window, time, 250 + 620);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers);
            Assert.All(Slots, s => Assert.Equal(1, Item(window, s).Opacity, 3));
            Assert.Equal(36, toggle.Width, 0.5);
            window.Close();
        });
    }

    [Fact]
    public void Dragging_animates_only_the_row_change_and_otherwise_follows_the_edge()
    {
        WpfTestHost.Run(() =>
        {
            var wide = new Size(1366, 900); // eine Zeile
            var narrow = new Size(1164, 900); // zwei Zeilen
            var (window, _, _) = Open(wide);
            var (clock, time) = HandClock(window);
            Assert.Equal(1, window.ToolbarRows);

            // Ohne Ziehen (Maximieren, Einrasten, Start): sofort der Endzustand, keine Bewegung.
            Arrange(window, narrow);
            Assert.Equal(2, window.ToolbarRows);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Arrange(window, wide);
            Assert.Equal(1, window.ToolbarRows);

            // Beim Ziehen am Fensterrand: der Zeilenwechsel als Morph (Filter fallen, Liste mit), etwa 320 ms.
            window.SetToolbarResizing(true);
            Arrange(window, narrow);
            Assert.Equal(2, window.ToolbarRows);
            Assert.True(window.ToolbarMotion.Running > 0);
            At(window, time, 0);
            var filter = Item(window, ToolbarSlot.FilterAuto);
            Assert.True(((TranslateTransform)((TransformGroup)filter.RenderTransform).Children[1]).Y < -40, "Filter zeigt noch die alte Zeile");
            At(window, time, 400);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(window.ToolbarPixelSnap.Y, ((TranslateTransform)((TransformGroup)filter.RenderTransform).Children[1]).Y, 6);

            // Hysterese: etwas breiter bleibt es zweizeilig, erst 32 über der Schwelle wieder eine Zeile.
            var list = Named<FrameworkElement>(window, "ListCard");
            Arrange(window, new Size(1164 + (ToolbarLayout.OneRowWidth + 10 - list.ActualWidth), 900));
            Assert.Equal(2, window.ToolbarRows);
            window.SetToolbarResizing(false);
            Arrange(window, wide);
            Assert.Equal(1, window.ToolbarRows);
            Assert.Equal(0, clock.Subscribers);
            window.Close();
        });
    }

    [Fact]
    public void Only_dragging_an_edge_counts_as_resizing_not_moving_or_restoring_a_maximized_window()
    {
        WpfTestHost.Run(() =>
        {
            const int enterSizeMove = 0x0231, sizing = 0x0214, exitSizeMove = 0x0232;
            var wide = new Size(1366, 900);
            var narrow = new Size(1164, 900);
            var (window, _, _) = Open(wide);
            HandClock(window);

            // Schleife ohne WM_SIZING: Verschieben an der Titelleiste, dabei wird ein maximiertes Fenster wiederhergestellt (oder
            // der Bildschirm wechselt): sofort der Endzustand, kein Morph, keine Hysterese.
            window.OnSizeMoveMessage(enterSizeMove);
            Assert.False(window.IsToolbarResizing);
            Arrange(window, narrow);
            Assert.Equal(2, window.ToolbarRows);
            Assert.Equal(0, window.ToolbarMotion.Running);
            window.OnSizeMoveMessage(exitSizeMove);
            Arrange(window, wide);
            Assert.Equal(1, window.ToolbarRows);

            // Zug an der Kante (WM_SIZING): jetzt wird der Zeilenwechsel bewegt; WM_EXITSIZEMOVE beendet das Ziehen.
            window.OnSizeMoveMessage(enterSizeMove);
            window.OnSizeMoveMessage(sizing);
            Assert.True(window.IsToolbarResizing);
            Arrange(window, narrow);
            Assert.True(window.ToolbarMotion.Running > 0);
            window.OnSizeMoveMessage(exitSizeMove);
            Assert.False(window.IsToolbarResizing);
            window.Close();
        });
    }

    [Fact]
    public void Two_rows_kept_while_dragging_keep_their_height_when_collapsed_and_the_list_does_not_jump_when_expanding()
    {
        WpfTestHost.Run(() =>
        {
            // Zweizeilig, dann am Rand in die Hysterese gezogen (Liste 816: zwei Zeilen bleiben) und losgelassen.
            var (window, model, _) = Open(new Size(1164, 900));
            var (_, time) = HandClock(window);
            var list = Named<FrameworkElement>(window, "ListCard");
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var band = new Size(1164 + (ToolbarLayout.OneRowWidth + 10 - list.ActualWidth), 900);
            window.SetToolbarResizing(true);
            Arrange(window, band);
            window.SetToolbarResizing(false);
            At(window, time, 1000);
            Assert.Equal(2, window.ToolbarRows);
            var range = splitter.Maximum;
            var hero = window.HeroHeight;
            var cardTop = Box(window, list).Top;

            // Eingeklappt rechnet alles weiter mit den zwei Zeilen: Bereich des Griffs und Titelbereich bleiben, die Liste
            // steigt um 100 (88 + 12: ausgeklappt 17 über und unter der Leiste, eingeklappt 10 unter der Kapsel).
            model.IsToolbarCollapsed = true;
            Arrange(window, band);
            At(window, time, 2000);
            Assert.Equal(range, splitter.Maximum);
            Assert.Equal(hero, window.HeroHeight);
            Assert.Equal(ToolbarLayout.HeightFor(2) + 12, cardTop - Box(window, list).Top, 0.5);
            var collapsedTop = Box(window, list).Top;

            // Ausklappen: Im ersten Bild steht die Liste, wo sie eingeklappt stand (kein Sprung um 48), dann wieder zwei Zeilen.
            var content = (TranslateTransform)Named<FrameworkElement>(window, "ListCardContent").RenderTransform;
            model.IsToolbarCollapsed = false;
            Arrange(window, band);
            At(window, time, 2000);
            Assert.Equal(collapsedTop, Box(window, list).Top + content.Y, 0.5);
            At(window, time, 3000);
            Assert.Equal(2, window.ToolbarRows);
            Assert.Equal(cardTop, Box(window, list).Top, 0.5);
            Assert.Equal(range, splitter.Maximum);
            window.Close();
        });
    }

    [Fact]
    public void Focus_never_gets_lost_when_the_toolbar_or_the_reset_button_goes_away_and_ctrl_f_focuses_the_search()
    {
        WpfTestHost.Run(() =>
        {
            // Das Testfenster ist nie aktiv: Geprüft wird der Fokus, den es sich merkt und beim Aktivieren wiederherstellt (im
            // aktiven Fenster ist das der Tastaturfokus).
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size);
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            var reset = Named<Button>(window, "ToolbarResetButton");
            var search = Named<TextBox>(window, "SearchBox");

            // Fokus in der Leiste beim Einklappen: auf die Kapsel.
            FocusManager.SetFocusedElement(window, search);
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            Assert.Same(toggle, FocusManager.GetFocusedElement(window));

            // ✕ mit Fokus verschwindet (Eingabetaste: Filter zurückgesetzt): Fokus auf die Kapsel, nicht ans Fenster.
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Assert.Equal(Visibility.Visible, reset.Visibility);
            Assert.True(reset.Focusable);
            FocusManager.SetFocusedElement(window, reset);
            reset.Command.Execute(null);
            Assert.Equal(Visibility.Collapsed, reset.Visibility);
            Assert.Same(toggle, FocusManager.GetFocusedElement(window));

            // ✕ mit Fokus, dann Strg+Umschalt+F (ausklappen, das ✕ nimmt keinen Fokus mehr): ebenso.
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            FocusManager.SetFocusedElement(window, reset);
            Assert.True(window.HandleKey(Key.F, ModifierKeys.Control | ModifierKeys.Shift, null));
            Assert.False(model.IsToolbarCollapsed);
            Assert.False(reset.Focusable);
            Assert.Same(toggle, FocusManager.GetFocusedElement(window));

            // Strg+F bei eingeklappter Leiste: dauerhaft ausgeklappt, die Suche hat den Fokus.
            model.IsToolbarCollapsed = true;
            Assert.True(window.HandleKey(Key.F, ModifierKeys.Control, null));
            Assert.False(model.IsToolbarCollapsed);
            Assert.Same(search, FocusManager.GetFocusedElement(window));
            window.Close();
        });
    }

    [Fact]
    public void Reset_button_is_only_there_when_collapsed_and_filtered()
    {
        WpfTestHost.Run(() =>
        {
            // Unsichtbar heißt Collapsed: nicht in der Steueransicht von UI-Automation (Bildschirmleser), nicht auslösbar
            // (phase10-ui.ps1 prüft das am echten Fenster), nicht treffbar und kein Tab-Halt.
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size);
            var reset = Named<Button>(window, "ToolbarResetButton");

            // Ausgeklappt und gefiltert: kein unsichtbarer Knopf.
            model.ToggleFilter(ToolbarFilter.New);
            Arrange(window, size);
            Assert.Equal(Visibility.Collapsed, reset.Visibility);
            Assert.False(reset.Focusable || reset.IsHitTestVisible);

            // Eingeklappt und gefiltert: da, Tab-Halt.
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            Assert.Equal(Visibility.Visible, reset.Visibility);
            Assert.True(reset.Focusable && KeyboardNavigation.GetIsTabStop(reset));

            // Eingeklappt ohne Filter und wieder ausgeklappt: weg.
            model.ClearFiltersCommand.Execute(null);
            Assert.Equal(Visibility.Collapsed, reset.Visibility);
            model.ToggleFilter(ToolbarFilter.New);
            model.IsToolbarCollapsed = false;
            Arrange(window, size);
            Assert.Equal(Visibility.Collapsed, reset.Visibility);
            window.Close();
        });
    }

    [Fact]
    public void High_contrast_draws_tab_capsule_and_reset_in_system_colors()
    {
        WpfTestHost.Run(() =>
        {
            // Dunkles Thema (helle Schrift) mit hohem Kontrast: Die Fläche wird Systemfarbe, also auch alles darauf.
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, collapsed: true);
            model.SearchText = "Spiel";
            model.ApplySearch();
            model.ToggleFilter(ToolbarFilter.New);
            window.SetToolbarHighContrast(true);
            Arrange(window, size);
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            var reset = Named<Button>(window, "ToolbarResetButton");
            var back = SystemColors.ControlColor;
            var texts = WpfTestHost.Visuals(toggle).Concat(WpfTestHost.Visuals(reset)).OfType<TextBlock>().ToList();
            Assert.True(texts.Count >= 6, "Hinweis, Tastenkappe, Suchbegriff, Zahl und ✕");
            foreach (var text in texts)
            {
                foreach (var brush in new[] { text.Foreground }.Concat(text.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Foreground)))
                {
                    var color = Assert.IsType<SolidColorBrush>(brush).Color;
                    Assert.True(Contrast(color, back) >= 3, $"„{text.Text}“: {color} auf {back}, Kontrast {Contrast(color, back):0.0}");
                }
            }

            Assert.All(WpfTestHost.Visuals(toggle).OfType<FilterGlyph>(), g => Assert.Equal(SystemColors.HighlightColor, Assert.IsType<SolidColorBrush>(g.Fill).Color));

            // Aus: wieder die Farben des Themas (helle Schrift, die auf der Systemfläche nicht lesbar wäre).
            window.SetToolbarHighContrast(false);
            Assert.Contains(texts, t => Contrast(Assert.IsType<SolidColorBrush>(t.Foreground).Color, back) < 3);
            window.Close();
        });

        static double Contrast(Color a, Color b)
        {
            static double Luminance(Color c)
            {
                static double Channel(byte v)
                {
                    var x = v / 255.0;
                    return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
                }

                return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
            }

            var (l1, l2) = (Luminance(a), Luminance(b));
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }
    }

    [Fact]
    public void Sorting_while_collapsed_starts_no_motion_and_keeps_the_capsule_content()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, collapsed: true);
            var (clock, time) = HandClock(window);
            model.ToggleFilter(ToolbarFilter.New);
            At(window, time, 2000);
            Assert.Equal(0, window.ToolbarMotion.Running);
            var toggle = Named<ToolbarToggle>(window, "ToolbarToggle");
            var items = toggle.CapsuleItems;

            // Sortieren meldet die Zusammenfassung neu, ändert aber nichts an der Kapsel: kein Neubau, keine Bewegung, kein Takt.
            model.SelectedSortOption = model.SortOptions.First(o => o != model.SelectedSortOption);
            Assert.Equal(0, window.ToolbarMotion.Running);
            Assert.Equal(0, clock.Subscribers);
            Assert.Equal(items, toggle.CapsuleItems);
            window.Close();
        });
    }

    [Fact]
    public void Reduced_reset_button_fades_in_with_the_capsule()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, effects: "Reduced");
            var (_, time) = HandClock(window);
            model.ToggleFilter(ToolbarFilter.New);
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            var reset = Named<Button>(window, "ToolbarResetButton");

            // Die Kapsel blendet 60–150 ms ein, das ✕ mit ihr (Vorschau: #cx liegt in #tabw), nicht schon bei 0 ms.
            At(window, time, 30);
            Assert.Equal(0, reset.Opacity, 3);
            At(window, time, 105);
            Assert.InRange(reset.Opacity, 0.3, 0.7);
            At(window, time, 300);
            Assert.Equal(1, reset.Opacity, 3);
            window.Close();
        });
    }

    [Fact]
    public void Reduced_reset_button_fades_out_with_the_capsule()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, collapsed: true, effects: "Reduced");
            var (_, time) = HandClock(window);
            model.ToggleFilter(ToolbarFilter.New);
            At(window, time, 1000);
            var reset = Named<Button>(window, "ToolbarResetButton");
            var ghost = Named<Image>(window, "TabGhost");
            Assert.Equal(1, reset.Opacity, 3);
            model.IsToolbarCollapsed = false;
            Arrange(window, size);

            // Die Kapsel blendet als Bild 0–90 ms aus, das ✕ mit ihr (es ist nicht im Bild), nicht schon bei 0 ms weg.
            At(window, time, 1000);
            Assert.Equal(Visibility.Visible, reset.Visibility);
            Assert.Equal(1, reset.Opacity, 3);
            At(window, time, 1045);
            Assert.InRange(reset.Opacity, 0.3, 0.7);
            Assert.Equal(ghost.Opacity, reset.Opacity, 3);
            Assert.False(reset.IsHitTestVisible); // ausgeklappt nicht mehr zu bedienen, auch während es ausblendet
            Assert.False(reset.Focusable);
            At(window, time, 1300);
            Assert.Equal(0, reset.Opacity, 3);
            Assert.Equal(Visibility.Collapsed, reset.Visibility);
            window.Close();
        });
    }

    [Fact]
    public void Reduced_ghost_shows_the_element_even_far_inside_its_parent()
    {
        WpfTestHost.Run(() =>
        {
            // Wie die Lasche im Canvas (1223/207): Das Bild zeigt das Element, nicht den leeren Platz an seinem Ursprung.
            var element = new Border { Width = 40, Height = 20, Background = Brushes.White };
            var canvas = new Canvas { Width = 1400, Height = 300 };
            Canvas.SetLeft(element, 1223);
            Canvas.SetTop(element, 207);
            canvas.Children.Add(element);
            WpfTestHost.Layout(canvas, new Size(1400, 300));
            var dpi = new DpiScale(106.0 / 96, 106.0 / 96);
            var (bitmap, bounds) = ElementBitmap.Render(element, dpi)!.Value;
            Assert.Equal(new Rect(0, 0, 40, 20), bounds);
            Assert.Equal(45, bitmap.PixelWidth);
            Assert.Equal(23, bitmap.PixelHeight);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var center = (((bitmap.PixelHeight / 2) * bitmap.PixelWidth) + (bitmap.PixelWidth / 2)) * 4;
            Assert.Equal(255, pixels[center + 3]); // deckend in der Mitte
            Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[(i * 4) + 3] == 255) > pixels.Length / 4 * 0.8, "Bild leer");
            Assert.Null(ElementBitmap.Render(new Border(), dpi));
        });
    }

    [Fact]
    public void Unfiltered_capsule_keeps_11_px_between_search_and_its_key()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(new Size(1745, 982), collapsed: true);
            var row = Assert.IsAssignableFrom<Panel>(Named<ToolbarToggle>(window, "ToolbarToggle").CapsuleItems[0]);
            var text = row.Children.OfType<TextBlock>().Last(); // „Suchen“
            var key = row.Children.OfType<Border>().Single(); // „Strg+F“
            var gap = key.TranslatePoint(default, row).X - text.TranslatePoint(new Point(text.ActualWidth, 0), row).X;
            Assert.Equal(MainWindow.CapsuleKeyGap, gap, 0.5); // Vorschau: Abstand 5 und margin-left 6
            window.Close();
        });
    }

    [Theory]
    [InlineData(LibraryViewMode.List)]
    [InlineData(LibraryViewMode.Grid)]
    [InlineData(LibraryViewMode.Shelf)]
    public void Grip_line_ends_12_px_before_the_tab_or_the_capsule(LibraryViewMode view)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(size, view);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var line = Assert.IsAssignableFrom<FrameworkElement>(splitter.Template.FindName("Line", splitter));
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            Assert.Equal(tab.Left - MainWindow.GripLineGap, Box(window, line).Right, OnePixel(window));
            Assert.Equal(Box(window, splitter).Left + HeroSplitter.LineInset, Box(window, line).Left, 0.5);
            var listArea = Box(window, Named<FrameworkElement>(window, "ListArea"));
            var side = view == LibraryViewMode.Shelf ? ShowcaseLayout.Pad : 0;
            Assert.Equal(listArea.Left + side, Box(window, splitter).Left, 0.5); // Griff so breit wie die Leiste (im Regal ohne Rand)
            Assert.Equal(listArea.Right - side, Box(window, splitter).Right, 0.5);

            model.ToggleFilter(ToolbarFilter.New);
            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            var capsule = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            var right = view == LibraryViewMode.Shelf
                ? Box(window, splitter).Right - HeroSplitter.LineInset // die Kapsel steht im Regal in der Leistenzeile, nicht auf der Linie
                : capsule.Left - MainWindow.GripLineGap;
            Assert.Equal(right, Box(window, line).Right, OnePixel(window));
            window.Close();
        });
    }

    private sealed class TestClock : IFrameClock
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

/// <summary>Kurzform für Muster eines Automation-Peers in diesen Prüfungen.</summary>
internal static class PatternExtensions
{
    public static T Should<T>(this object? pattern)
        where T : class => Assert.IsAssignableFrom<T>(pattern);
}
