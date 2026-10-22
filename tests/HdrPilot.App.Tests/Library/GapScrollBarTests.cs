using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bildlaufleiste im Spalt zwischen Liste bzw. Raster und Detailbereich (Wunsch des Nutzers 2026-10-22): Die Pille ist die
/// Bildlaufleiste der aktiven Ansicht (Länge nach sichtbarem Anteil, Bahn genau auf der Höhe des sichtbaren Bereichs, ohne
/// Bildlauf weg), Ziehen der Pille scrollt, ein Klick darüber bzw. darunter blättert, das Mausrad im Spalt scrollt; der Spalt
/// bleibt der Trenner (waagerecht ziehen, Esc, Doppelklick, Tastatur am Trenner). Die eigenen Bildlaufleisten von Liste und
/// Raster sind aus, Mausrad, Tastatur und UI-Automation der Liste gehen wie bisher. Läuft allein: Die Prüfungen stellen die
/// Effektstufe (sie gilt für die ganze Bibliothek).
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class GapScrollBarTests(ITestOutputHelper output)
{
    private static readonly Size Standard = new(1600, 1000);

    private static GameConfig[] ManyGames(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Game(i % 3 == 0 ? GameSource.Epic : GameSource.Steam, "g" + i, $"Spiel {i:000}", hdrAuto: i % 4 == 0, isNew: false))];

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context) Open(int games, LibraryViewMode view = LibraryViewMode.List, string effects = "Off", Size? size = null)
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext(ManyGames(games))
        {
            Appearance = new HdrPilot.App.Theming.StaticAppearanceService(new AppearanceSettings { EffectLevel = effects }),
        };
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        var window = new MainWindow();
        window.Attach(model);
        WpfTestHost.Layout(window, size ?? Standard);
        window.FitDetailsColumn(); // wie beim Laden
        Arrange(window, size);
        return (window, model, context);
    }

    /// <summary>
    /// Neu anordnen wie nach einer Eingabe (ohne die Aufteilung neu einzupassen, das tut das Fenster nur beim Laden und beim
    /// Wechsel der Ansicht); die Bahn der Leiste folgt nach dem Layout-Durchgang (ein weiterer Durchgang).
    /// </summary>
    private static void Arrange(MainWindow window, Size? size = null)
    {
        var s = size ?? Standard;
        WpfTestHost.Layout(window, s);
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, s);
        WpfTestHost.Layout(window, s);
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

    private static GapScrollBar Bar(MainWindow window) => Named<GapScrollBar>(window, "LibraryScroll");

    private static ScrollViewer Host(ListBox list) => Assert.IsType<ScrollViewer>(list.Template.FindName("ScrollHost", list));

    private static Rect Box(MainWindow window, FrameworkElement element) =>
        element.TransformToAncestor(Named<Visual>(window, "Root")).TransformBounds(new Rect(element.RenderSize));

    private static Thumb Pill(GapScrollBar bar) => Assert.IsAssignableFrom<Thumb>(bar.Track.Thumb);

    /// <summary>Das getroffene Element liegt in <paramref name="ancestor"/> (oder ist es).</summary>
    private static bool Inside(DependencyObject? hit, DependencyObject ancestor)
    {
        for (var node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Oberkante der Pille in Koordinaten der Leiste.</summary>
    private static double PillTop(GapScrollBar bar) => Pill(bar).TranslatePoint(default, bar).Y;

    /// <summary>Klick (Drücken und Loslassen an derselben Stelle) in Koordinaten der Leiste, danach anordnen.</summary>
    private static void Click(MainWindow window, GapScrollBar bar, Point at)
    {
        bar.Press(at, 1);
        bar.Release(at);
        Arrange(window);
    }

    private static void Key(UIElement target, Key key, RoutedEvent routed)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, key) { RoutedEvent = routed };
        target.RaiseEvent(args);
    }

    [Fact]
    public void Pill_is_the_scrollbar_of_the_list_with_its_track_on_the_visible_rows_and_the_tiny_scrollbar_is_gone()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200);
            var bar = Bar(window);
            var list = Named<ListBox>(window, "GameList");
            var host = Host(list);

            // Die Leiste führt den ScrollViewer der Liste; dessen eigene Leiste ist aus (scrollen kann er weiter).
            Assert.Same(host, bar.Viewer);
            Assert.Equal(ScrollBarVisibility.Hidden, host.VerticalScrollBarVisibility);
            Assert.Equal(Visibility.Collapsed, host.ComputedVerticalScrollBarVisibility);
            Assert.True(host.ScrollableHeight > 1000, "Liste scrollt: " + host.ScrollableHeight);
            Assert.Equal(host.ScrollableHeight, bar.Maximum, 1.0);
            Assert.Equal(host.ViewportHeight, bar.ViewportSize, 1.0);

            // Spalt wie der Trenner; die Bahn genau so hoch wie die sichtbaren Zeilen (unter dem Kopf der Liste).
            var splitter = Box(window, Named<FrameworkElement>(window, "LibrarySplitter"));
            var gap = Box(window, bar);
            Assert.Equal(splitter, gap);
            var track = Box(window, bar.Track);
            var rows = Box(window, host);
            Assert.Equal(rows.Top, track.Top, 0.5);
            Assert.Equal(rows.Bottom, track.Bottom, 0.5);
            Assert.Equal(Visibility.Visible, bar.Track.Visibility);

            // Pille mittig im Spalt, Länge nach sichtbarem Anteil, mindestens 36 px (200 Zeilen: der kleinste Wert).
            var pill = Box(window, Pill(bar));
            Assert.Equal(gap.Left + (gap.Width / 2), pill.Left + (pill.Width / 2), 0.5);
            var expected = Math.Max(GapScrollBar.MinThumbLength, track.Height * host.ViewportHeight / host.ExtentHeight);
            Assert.Equal(expected, pill.Height, 1.0);
            Assert.Equal(track.Top, pill.Top, 1.0);

            // Treffer: Die Pille hat Vorrang vor der Linie (Zeiger Pfeil), daneben und darüber ist der Spalt der Trenner
            // (Doppelpfeil); den Trenner selbst erreicht die Maus nicht mehr, er zeigt in Ruhe nichts (kein Griff, der wie
            // eine Bildlaufleiste aussieht), mit Tastaturfokus die Linie.
            // (Ohne angezeigtes Fenster ist nichts IsVisible, deshalb der Treffer im visuellen Baum statt InputHitTest.)
            static DependencyObject? Hit(Visual root, Point at) => VisualTreeHelper.HitTest(root, at)?.VisualHit;
            var middle = PillTop(bar) + (Pill(bar).ActualHeight / 2);
            Assert.True(Inside(Hit(bar, new Point(bar.ActualWidth / 2, middle)), Pill(bar)), "Pille hat Vorrang");
            Assert.False(Inside(Hit(bar, new Point(2, middle)), Pill(bar)), "neben der Pille: Spalt");
            Assert.NotNull(Hit(bar, new Point(2, middle)));
            Assert.NotNull(Hit(bar, new Point(bar.ActualWidth / 2, 4)));
            Assert.False(Inside(Hit(bar, new Point(bar.ActualWidth / 2, 4)), Pill(bar)));
            Assert.Equal(Cursors.Arrow, Pill(bar).Cursor);
            Assert.Equal(Cursors.SizeWE, bar.Cursor);
            var keyboardSplitter = Named<GridSplitter>(window, "LibrarySplitter");
            Assert.All(WpfTestHost.Visuals(keyboardSplitter).OfType<Border>(), b => Assert.Equal(0, b.Opacity));
            Assert.Null(keyboardSplitter.Template.FindName("Rest", keyboardSplitter));

            // Gescrollt: Pille anteilig in der Bahn, Wert wie der Versatz.
            host.ScrollToVerticalOffset(host.ScrollableHeight / 2);
            Arrange(window);
            Assert.Equal(host.VerticalOffset, bar.Value, 1.0);
            pill = Box(window, Pill(bar));
            Assert.Equal(track.Top + ((track.Height - pill.Height) * host.VerticalOffset / host.ScrollableHeight), pill.Top, 1.0);
            host.ScrollToBottom();
            Arrange(window);
            Assert.Equal(track.Bottom, Box(window, Pill(bar)).Bottom, 1.0);
            window.Close();
        });
    }

    [Fact]
    public void Pill_follows_the_grid_from_its_first_tile_row_and_the_shelf_has_none()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(200, LibraryViewMode.Grid);
            var bar = Bar(window);
            var grid = Named<ListBox>(window, "GameGrid");
            var host = Host(grid);
            Assert.Same(host, bar.Viewer);
            Assert.Equal(ScrollBarVisibility.Hidden, host.VerticalScrollBarVisibility);
            Assert.Equal(Visibility.Collapsed, host.ComputedVerticalScrollBarVisibility);
            Assert.True(host.ScrollableHeight > 1000, "Raster scrollt: " + host.ScrollableHeight);
            Assert.Equal(host.ScrollableHeight, bar.Maximum, 1.0);

            // Bahn ab der ersten Kachelreihe (Innenabstand des Kachel-Panels) bis zum unteren Rand des Rasters.
            var panel = Assert.IsType<VirtualizingTilePanel>(VisualTreeHelper.GetChild(Assert.IsType<ItemsPresenter>(host.Content), 0));
            var view = Box(window, host);
            var track = Box(window, bar.Track);
            Assert.Equal(view.Top + panel.Padding.Top, track.Top, 0.5);
            Assert.Equal(view.Bottom, track.Bottom, 0.5);
            var first = Assert.IsType<ListBoxItem>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(Box(window, first).Top, track.Top, 0.5);

            // Ziehen der Pille scrollt das Raster anteilig.
            var pill = Pill(bar);
            var room = bar.Track.ActualHeight - pill.ActualHeight;
            pill.RaiseEvent(new DragStartedEventArgs(0, 0));
            pill.RaiseEvent(new DragDeltaEventArgs(0, 60));
            pill.RaiseEvent(new DragCompletedEventArgs(0, 60, false));
            Arrange(window);
            Assert.Equal(60 * host.ScrollableHeight / room, host.VerticalOffset, 1.0);
            Assert.Equal(host.VerticalOffset, bar.Value, 1.0);

            // Regal: kein Spalt, keine Leiste.
            model.ViewMode = LibraryViewMode.Shelf;
            Arrange(window);
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.Null(bar.Viewer);
            Assert.Equal(0, bar.Maximum);

            // Zurück zur Liste: wieder deren ScrollViewer.
            model.ViewMode = LibraryViewMode.List;
            Arrange(window);
            Assert.Equal(Visibility.Visible, bar.Visibility);
            Assert.Same(Host(Named<ListBox>(window, "GameList")), bar.Viewer);
            window.Close();
        });
    }

    [Fact]
    public void Dragging_the_pill_scrolls_the_list_in_proportion()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var pill = Pill(bar);
            var room = bar.Track.ActualHeight - pill.ActualHeight;

            pill.RaiseEvent(new DragStartedEventArgs(0, 10));
            pill.RaiseEvent(new DragDeltaEventArgs(0, 40));
            Arrange(window);
            Assert.Equal(40 * host.ScrollableHeight / room, host.VerticalOffset, 1.0);
            pill.RaiseEvent(new DragCompletedEventArgs(0, 40, false));
            Assert.Equal(host.VerticalOffset, bar.Value, 1.0);
            window.Close();
        });
    }

    [Fact]
    public void Click_above_or_below_the_pill_pages_toward_the_pointer_and_beside_it_nothing()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var center = bar.ActualWidth / 2;
            var bottom = bar.ActualHeight - bar.TrackMargin.Bottom - 4;
            var page = host.ViewportHeight;

            Click(window, bar, new Point(center, bottom));
            Assert.Equal(page, host.VerticalOffset, 1.0);
            Click(window, bar, new Point(center, bottom));
            Assert.Equal(2 * page, host.VerticalOffset, 1.0);

            // Über der Pille: eine Seite zurück. Auf ihrer Höhe neben ihr: nichts.
            Click(window, bar, new Point(center, bar.TrackMargin.Top + 2));
            Assert.Equal(page, host.VerticalOffset, 1.0);
            Click(window, bar, new Point(2, PillTop(bar) + (Pill(bar).ActualHeight / 2)));
            Assert.Equal(page, host.VerticalOffset, 1.0);
            Assert.Null(model.DetailsRatio); // ein Klick ändert die Breite nie
            window.Close();
        });
    }

    [Fact]
    public void Small_jitter_is_still_a_click_and_a_vertical_slip_neither_pages_nor_resizes()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var details = Named<ColumnDefinition>(window, "DetailsColumn");
            var width = details.ActualWidth;
            var at = new Point(bar.ActualWidth / 2, bar.ActualHeight - bar.TrackMargin.Bottom - 4);

            // Unter der Zieh-Schwelle von Windows (waagerecht und senkrecht): ein Klick.
            bar.Press(at, 1);
            bar.Drag(at + new Vector(SystemParameters.MinimumHorizontalDragDistance - 1, SystemParameters.MinimumVerticalDragDistance - 1));
            Assert.False(bar.IsResizing);
            bar.Release(at);
            Arrange(window);
            Assert.Equal(host.ViewportHeight, host.VerticalOffset, 1.0);
            Assert.Equal(width, details.ActualWidth, 0.5);

            // Senkrecht abgerutscht: kein Klick, kein Ziehen.
            bar.Press(at, 1);
            bar.Drag(at + new Vector(0, SystemParameters.MinimumVerticalDragDistance + 6));
            bar.Release(at + new Vector(0, SystemParameters.MinimumVerticalDragDistance + 6));
            Arrange(window);
            Assert.Equal(host.ViewportHeight, host.VerticalOffset, 1.0);
            Assert.Equal(width, details.ActualWidth, 0.5);
            Assert.Null(model.DetailsRatio);
            window.Close();
        });
    }

    [Fact]
    public void Dragging_the_gap_sideways_resizes_the_details_and_remembers_the_split()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var details = Named<ColumnDefinition>(window, "DetailsColumn");
            var before = details.ActualWidth;
            var at = new Point(bar.ActualWidth / 2, bar.ActualHeight - bar.TrackMargin.Bottom - 4);

            bar.Press(at, 1);
            bar.Drag(at + new Vector(-30, 0));
            Assert.True(bar.IsResizing);
            Assert.True(window.IsToolbarResizing, "Trenner meldet das Ziehen wie bisher (Hysterese der Werkzeugleiste)");
            Arrange(window);
            Assert.Equal(before + 30, details.ActualWidth, 1.0);

            // Der Spalt ist mitgewandert: weiter ab seiner neuen Lage, wie beim Thumb des Trenners.
            bar.Drag(at + new Vector(-30, 2));
            Arrange(window);
            Assert.Equal(before + 60, details.ActualWidth, 1.0);
            bar.Release(at + new Vector(-30, 2));
            Arrange(window);
            Assert.False(bar.IsResizing);
            Assert.False(window.IsToolbarResizing);
            Assert.Equal(before + 60, details.ActualWidth, 1.0);
            Assert.Equal(0, host.VerticalOffset); // Ziehen blättert nicht
            Assert.NotNull(model.DetailsRatio);
            context.Dispatcher.RunDelayed();
            Assert.NotNull(context.Config.Settings.LibraryPaneRatio);
            window.Close();
        });
    }

    [Fact]
    public void Escape_cancels_a_gap_drag_and_restores_the_width()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context) = Open(200);
            var bar = Bar(window);
            var details = Named<ColumnDefinition>(window, "DetailsColumn");
            var before = details.ActualWidth;
            var at = new Point(bar.ActualWidth / 2, 60);

            bar.Press(at, 1);
            bar.Drag(at + new Vector(-50, 0));
            Arrange(window);
            Assert.Equal(before + 50, details.ActualWidth, 1.0);

            // Esc geht an das Fenster (die Leiste hat nie den Fokus): zurück zur Breite davor, nichts gemerkt.
            Key(window, System.Windows.Input.Key.Escape, Keyboard.PreviewKeyDownEvent);
            Arrange(window);
            Assert.False(bar.IsResizing);
            Assert.Equal(before, details.ActualWidth, 0.5);
            bar.Drag(at + new Vector(-80, 0)); // nach dem Abbruch bewegt nichts mehr
            bar.Release(at + new Vector(-80, 0));
            Arrange(window);
            Assert.Equal(before, details.ActualWidth, 0.5);
            Assert.Null(model.DetailsRatio);
            context.Dispatcher.RunDelayed();
            Assert.Null(context.Config.Settings.LibraryPaneRatio);
            window.Close();
        });
    }

    [Fact]
    public void Double_click_in_the_gap_resets_the_split_and_takes_back_the_page_of_its_first_click()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var details = Named<ColumnDefinition>(window, "DetailsColumn");
            var standard = details.ActualWidth;
            var top = new Point(bar.ActualWidth / 2, 60);

            bar.Press(top, 1);
            bar.Drag(top + new Vector(-40, 0));
            bar.Release(top + new Vector(-40, 0));
            Arrange(window);
            Assert.NotNull(model.DetailsRatio);

            // Erster Klick des Doppelklicks blättert, der zweite nimmt das zurück und setzt die Aufteilung zurück.
            var at = new Point(bar.ActualWidth / 2, bar.ActualHeight - bar.TrackMargin.Bottom - 4);
            bar.Press(at, 1);
            bar.Release(at);
            Arrange(window);
            Assert.Equal(host.ViewportHeight, host.VerticalOffset, 1.0);
            bar.Press(at, 2);
            Arrange(window);
            Assert.Equal(0, host.VerticalOffset, 1.0);
            Assert.Null(model.DetailsRatio);
            Assert.Equal(standard, details.ActualWidth, 1.0);
            window.Close();
        });
    }

    [Fact]
    public void Wheel_in_the_gap_and_on_the_list_keyboard_and_ui_automation_still_scroll()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200);
            var bar = Bar(window);
            var list = Named<ListBox>(window, "GameList");
            var host = Host(list);

            // Mausrad im Spalt und über der Liste.
            bar.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            Arrange(window);
            var afterGap = host.VerticalOffset;
            Assert.True(afterGap > 0, "Rad im Spalt scrollt die Liste");
            var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(list.Items.IndexOf(list.SelectedItem)) ?? WpfTestHost.Visuals(list).OfType<ListBoxItem>().First());
            row.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            Arrange(window);
            Assert.True(host.VerticalOffset > afterGap, "Rad über der Liste scrollt weiter");
            Assert.Equal(host.VerticalOffset, bar.Value, 1.0);

            // UI-Automation: ScrollPattern der Liste wie bisher.
            var scroll = Assert.IsAssignableFrom<IScrollProvider>(UIElementAutomationPeer.CreatePeerForElement(list).GetPattern(PatternInterface.Scroll));
            Assert.True(scroll.VerticallyScrollable);
            scroll.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 50);
            Arrange(window);
            Assert.Equal(host.ScrollableHeight / 2, host.VerticalOffset, 1.0);
            Assert.Equal(50, scroll.VerticalScrollPercent, 0.5);

            // Tastatur: Pos1 und Ende auf einer Zeile.
            var current = WpfTestHost.Visuals(list).OfType<ListBoxItem>().First();
            Key(current, System.Windows.Input.Key.Home, Keyboard.KeyDownEvent);
            Arrange(window);
            Assert.Equal(0, host.VerticalOffset, 1.0);
            current = WpfTestHost.Visuals(list).OfType<ListBoxItem>().First();
            Key(current, System.Windows.Input.Key.End, Keyboard.KeyDownEvent);
            Arrange(window);
            Assert.Equal(host.ScrollableHeight, host.VerticalOffset, 1.0);
            Assert.Equal(host.ScrollableHeight, bar.Value, 1.0);
            window.Close();
        });
    }

    [Fact]
    public void Gap_scrollbar_is_named_for_ui_automation_sets_its_value_and_never_takes_the_focus()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            Assert.Equal("LibraryScrollBar", AutomationProperties.GetAutomationId(bar));
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.ScrollGap_Name, AutomationProperties.GetName(bar));
            Assert.False(bar.Focusable);
            Assert.False(bar.IsTabStop);
            Assert.False(Pill(bar).Focusable);

            var peer = UIElementAutomationPeer.CreatePeerForElement(bar);
            Assert.Equal(AutomationControlType.ScrollBar, peer.GetAutomationControlType());
            var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer.GetPattern(PatternInterface.RangeValue));
            range.SetValue(500);
            Arrange(window);
            Assert.Equal(500, host.VerticalOffset, 1.0);

            // Der Trenner bleibt für Tastatur und UI-Automation der Breite zuständig, in der Tab-Reihenfolge wie bisher.
            var splitter = Named<GridSplitter>(window, "LibrarySplitter");
            Assert.True(splitter.Focusable && splitter.IsTabStop);
            window.Close();
        });
    }

    [Fact]
    public void Without_anything_to_scroll_the_pill_is_gone_but_the_gap_still_resizes()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(4);
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            Assert.Equal(0, host.ScrollableHeight);
            Assert.Equal(0, bar.Maximum);
            Assert.NotEqual(Visibility.Visible, bar.Track.Visibility);
            Assert.Equal(Visibility.Visible, bar.Visibility);

            var details = Named<ColumnDefinition>(window, "DetailsColumn");
            var before = details.ActualWidth;
            var at = new Point(bar.ActualWidth / 2, 200);
            Click(window, bar, at); // nichts zu blättern
            Assert.Equal(0, host.VerticalOffset);
            bar.Press(at, 1);
            bar.Drag(at + new Vector(-24, 0));
            bar.Release(at + new Vector(-24, 0));
            Arrange(window);
            Assert.Equal(before + 24, details.ActualWidth, 1.0);
            Assert.NotNull(model.DetailsRatio);
            window.Close();
        });
    }

    [Fact]
    public void Paging_glides_by_effect_level_and_input_on_the_list_stops_it()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200, effects: "Full");
            var bar = Bar(window);
            var host = Host(Named<ListBox>(window, "GameList"));
            var page = host.ViewportHeight;

            bar.PageBy(1);
            output.WriteLine(LibraryMotion.Enabled ? "Blättern weich (Grafikkarte zeichnet)" : "Blättern sofort (ohne Grafikkarte)");
            if (LibraryMotion.Enabled)
            {
                // Weich: Ziel gesetzt, ein zweiter Klick zählt ab dem Ziel; das Rad über der Liste beendet das Blättern.
                Assert.Equal(page, bar.PagingTarget, 1.0);
                bar.PageBy(1);
                Assert.Equal(2 * page, bar.PagingTarget, 1.0);
                host.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                Assert.True(double.IsNaN(bar.PagingTarget));
            }
            else
            {
                Arrange(window);
                Assert.Equal(page, host.VerticalOffset, 1.0);
            }

            window.Close();
        });
    }

    [Fact]
    public void Pill_glides_with_the_list_while_the_toolbar_folds()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(200, effects: "Full");
            var bar = Bar(window);
            window.Motion!.SetWindowState(visible: true, minimized: false, active: true);
            window.FreezeToolbarMotion(collapse: true, 120);
            var content = Assert.IsType<TranslateTransform>(Named<FrameworkElement>(window, "ListCardContent").RenderTransform);
            var shift = Assert.IsType<TranslateTransform>(bar.Track.RenderTransform);
            if (LibraryMotion.HardwareRendering)
            {
                Assert.NotEqual(0, content.Y); // mitten im Einklappen
                Assert.NotNull(VisualTreeHelper.GetParent(bar.Track) is UIElement host ? host.Clip : null); // unten geschnitten wie die Liste
            }

            Assert.Equal(content.Y, shift.Y, 3);
            window.Close();
        });
    }

    /// <summary>Tastenereignisse brauchen eine Quelle; das Fenster wird in Tests nie angezeigt.</summary>
    private sealed class TestPresentationSource : PresentationSource
    {
        private Visual? _root;

        public override Visual? RootVisual
        {
            get => _root;
            set => _root = value;
        }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
