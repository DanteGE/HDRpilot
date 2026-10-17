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
/// Kanten, 8 px zwischen den Knöpfen, Luft 10/10 bzw. 22/22, Lasche 8 px über der Leiste, Kapsel 10 px über der Liste,
/// Griff auf der Kante), Ein- und Ausklappen (Lasche, Tasten, Menü, gemerkt, ohne Bewegung beim Öffnen), Bewegung je
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
    [InlineData(1164, 727)] // 1280×800 bei 110 %
    [InlineData(1242, 698)] // 1366×768 bei 110 %
    [InlineData(1280, 800)]
    [InlineData(1366, 768)]
    [InlineData(1600, 1000)] // Startfenster
    [InlineData(1745, 982)] // 1920×1080 bei 110 %
    [InlineData(1920, 1080)]
    [InlineData(3127, 1309)] // 3440×1440 bei 110 %
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

            // Luft über der Leiste (zur Bildkante) gleich der darunter (zur Liste): 10/10; der Griff mittig auf der Kante.
            var above = bar.Top - edge;
            var below = card.Top - bar.Bottom;
            Assert.Equal(10, above, 0.5);
            Assert.Equal(10, below, 0.5);
            var grip = Box(window, Named<FrameworkElement>(window, "HeroSplitter"));
            Assert.Equal(edge, grip.Top + (grip.Height / 2), 0.5);

            // Lasche rechts, ihre Unterkante 8 px über der Leiste (so viel wie zwischen den Knöpfen, Wunsch des Nutzers).
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            Assert.Equal(36, tab.Width, 0.5);
            Assert.Equal(18, tab.Height, 0.5);
            Assert.Equal(card.Right, tab.Right, 0.6);
            Assert.Equal(8, bar.Top - tab.Bottom, 0.5);

            // Inhalt des Titelbereichs 22 px über der Bildkante (Platz für Lasche und Kapsel).
            var compact = Named<FrameworkElement>(window, "HeroCompact");
            Assert.Equal(22, compact.Margin.Bottom);
            output.WriteLine(FormattableString.Invariant(
                $"Liste {width}x{height}: L {card.Width:0.##}, Skalierung {scale:0.###}, Zeilen {window.ToolbarRows}, Stufe {arrangement.Stage}, Suche {boxes[ToolbarSlot.Search].Width:0.##}, Quelle {boxes[ToolbarSlot.Source].Width:0.##}, Sortierung {boxes[ToolbarSlot.Sort].Width:0.##}, Abstände {string.Join('/', gaps.Select(g => g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Distinct())}, Luft {above:0.##}/{below:0.##}, Lasche über Leiste {bar.Top - tab.Bottom:0.##}, Griffmitte − Kante {grip.Top + (grip.Height / 2) - edge:0.##}, Kante rechts {card.Right - boxes[ToolbarSlot.Add].Right:0.##}"));
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
            Assert.Equal(edge - 12, capsule.Top, 0.5); // mittig auf der Kante
            Assert.Equal(card.Right, capsule.Right, 0.6); // rechtsbündig
            Assert.Equal(10, card.Top - capsule.Bottom, 0.5); // 10 px darunter die Liste
            Assert.Equal(cardTop - card.Top, ToolbarLayout.HeightFor(rows) - 2, 0.5); // Gewinn: 38 bzw. 86
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
    public void Grid_keeps_22_px_air_above_and_below_expanded_and_under_the_capsule(double width, double height)
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
            Assert.Equal(22, bar.Top - edge, 0.5);
            Assert.Equal(22, tile.Top - bar.Bottom, 0.5);
            var tab = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            Assert.Equal(8, bar.Top - tab.Bottom, 0.5); // −4 … +14 um die Kante

            model.IsToolbarCollapsed = true;
            Arrange(window, size);
            Assert.Equal(hero, window.HeroHeight);
            var capsule = Box(window, Named<FrameworkElement>(window, "ToolbarToggle"));
            tile = Box(window, (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(edge - 12, capsule.Top, 0.5);
            Assert.Equal(22, tile.Top - capsule.Bottom, 0.5);
            output.WriteLine(FormattableString.Invariant($"Raster {width}x{height}: Luft {bar.Top - edge:0.##}/22, Lasche {tab.Top - edge:0.##} … {tab.Bottom - edge:0.##}, eingeklappt Kachel {tile.Top - capsule.Bottom:0.##} unter der Kapsel"));
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
            Assert.Equal(2, tab.Top - bar.Bottom, 0.5); // Lasche in der Fuge zwischen Leiste und Kachelreihe
            Assert.Equal(2, shelfTop + ShowcaseLayout.GridPaddingTop - tab.Bottom, 0.5);
            Assert.Equal(bar.Right, tab.Right, 0.6);

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
            Assert.Equal(0, ((TranslateTransform)((TransformGroup)search.RenderTransform).Children[1]).X); // kein Versatz
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
            Assert.Equal(0, ((TranslateTransform)((TransformGroup)filter.RenderTransform).Children[1]).Y);

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
