using System.Windows;
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
/// Höhe des Titelbereichs je Ansicht (Wunsch des Nutzers 2026-10-22): Der Ziehgriff gibt es auch in Raster und Regal, mittig
/// auf der Bildkante, mit demselben Verhalten wie in der Liste (Ziehen, Doppelklick bzw. Eingabetaste, Esc, Tasten,
/// UI-Automation). Jede Ansicht merkt ihre eigene Höhe (AppSettings.LibraryHeroRatio, LibraryGridHeroRatio,
/// LibraryShelfHeroRatio; ohne Eintrag ihr Standard). Das Raster behält eine ganze Kachelreihe, das Regal tauscht Titelbereich
/// gegen Kachelgröße bis zur kleinsten Kachel und behält Platz für Automatik-Karte und großen Titel. Auf dem WPF-Testhost,
/// nie angezeigt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class HeroPerViewTests
{
    private static GameConfig[] ManyGames(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Game(i % 3 == 0 ? GameSource.Epic : GameSource.Steam, "g" + i, $"Spiel {i:000}", hdrAuto: i % 4 == 0, isNew: false))];

    private static (MainWindow Window, LibraryViewModel Model) Open(TestLibraryContext context, LibraryViewMode view, Size size)
    {
        EnsureLibraryResources();
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        var window = new MainWindow();
        window.Attach(model);
        model.SelectedGame = model.Games[0];
        Arrange(window, size);
        return (window, model);
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

    private static double Top(MainWindow window, FrameworkElement element) => element.TransformToAncestor(Named<Visual>(window, "Root")).Transform(default).Y;

    private static double Bottom(MainWindow window, FrameworkElement element) => Top(window, element) + element.ActualHeight;

    private static double RootHeight(MainWindow window) => Named<FrameworkElement>(window, "Root").ActualHeight;

    private static IRangeValueProvider Range(HeroSplitter splitter) =>
        Assert.IsAssignableFrom<IRangeValueProvider>(UIElementAutomationPeer.CreatePeerForElement(splitter).GetPattern(PatternInterface.RangeValue));

    private static void Invoke(HeroSplitter splitter) =>
        Assert.IsAssignableFrom<IInvokeProvider>(UIElementAutomationPeer.CreatePeerForElement(splitter).GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Drag(HeroSplitter splitter, double delta)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
        splitter.RaiseEvent(new DragDeltaEventArgs(0, delta));
        splitter.RaiseEvent(new DragCompletedEventArgs(0, delta, false));
    }

    private static void Press(MainWindow window, UIElement target, Key key, Size size)
    {
        var source = new KeySource();
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyUpEvent });
        Arrange(window, size);
    }

    /// <summary>Raster: Werkzeugleiste und Kachel zusammen, wie angeordnet (so rechnet das Fenster den Bereich).</summary>
    private static double GridRow(MainWindow window) =>
        Named<FrameworkElement>(window, "ToolbarArea").ActualHeight + Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(Named<ListBox>(window, "GameGrid"))).TileHeight;

    /// <summary>Der Griff liegt mit seiner Mitte auf der Bildkante, so breit wie die Werkzeugleiste.</summary>
    private static void AssertGripOnEdge(MainWindow window, string what)
    {
        var splitter = Named<HeroSplitter>(window, "HeroSplitter");
        Assert.Equal(Visibility.Visible, splitter.Visibility);
        Assert.Equal(ShowcaseLayout.HeroSplitterHeight, splitter.ActualHeight, 0.5);
        var edge = Bottom(window, Named<FrameworkElement>(window, "Hero"));
        Assert.True(Math.Abs(Top(window, splitter) + (splitter.ActualHeight / 2) - edge) <= 0.5, $"{what}: Griff {Top(window, splitter):0.#}, Kante {edge:0.#}");
        Assert.Equal(window.HeroHeight, Range(splitter).Value);
    }

    [Fact]
    public void Grid_and_shelf_have_a_grip_and_each_view_remembers_its_own_height_also_after_reopening()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var context = new TestLibraryContext(ManyGames(80));
            var (window, model) = Open(context, LibraryViewMode.List, size);
            var root = RootHeight(window);
            var listDefault = window.HeroHeight;
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            AssertGripOnEdge(window, "Liste");

            // Raster: eigener Standard und Bereich (eine ganze Kachelreihe bleibt); gezogen folgt nur seine Höhe, sofort.
            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, size);
            var gridDefault = window.HeroHeight;
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, root, gridRow: GridRow(window)), gridDefault);
            AssertGripOnEdge(window, "Raster");
            var (gridMin, gridMax) = ShowcaseLayout.GridHeroRange(root, GridRow(window));
            Assert.Equal((gridMin, gridMax), (Range(splitter).Minimum, Range(splitter).Maximum));
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 60));
            Assert.Equal(gridDefault + 60, window.HeroHeight); // ohne Animation, sofort
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 60, false));
            Arrange(window, size);
            AssertGripOnEdge(window, "Raster nach dem Ziehen");
            Assert.Equal(Math.Round((gridDefault + 60) / root, 4), model.HeroRatioFor(LibraryViewMode.Grid));
            Assert.Null(model.HeroRatio);
            context.Dispatcher.RunDelayed();
            Assert.Equal(model.HeroRatioFor(LibraryViewMode.Grid), context.Config.Settings.LibraryGridHeroRatio);
            Assert.Null(context.Config.Settings.LibraryHeroRatio);
            Assert.Null(context.Config.Settings.LibraryShelfHeroRatio);

            // Regal: eigener Standard; per UI-Automation gesetzt (wie eine Taste: sofort gemerkt), nur dort.
            model.ViewMode = LibraryViewMode.Shelf;
            Arrange(window, size);
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, root), window.HeroHeight);
            AssertGripOnEdge(window, "Regal");
            var shelfRange = Range(splitter);
            Assert.Equal(ShowcaseLayout.ShelfHeroRange(root), (shelfRange.Minimum, shelfRange.Maximum));
            var shelfHeight = shelfRange.Minimum + 40;
            shelfRange.SetValue(shelfHeight);
            Arrange(window, size);
            Assert.Equal(shelfHeight, window.HeroHeight);
            AssertGripOnEdge(window, "Regal nach UI-Automation");
            context.Dispatcher.RunDelayed();
            Assert.Equal(Math.Round(shelfHeight / root, 4), context.Config.Settings.LibraryShelfHeroRatio);

            // Jede Ansicht mit ihrer eigenen Höhe.
            model.ViewMode = LibraryViewMode.List;
            Arrange(window, size);
            Assert.Equal(listDefault, window.HeroHeight);
            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, size);
            Assert.Equal(gridDefault + 60, window.HeroHeight);
            window.Close();
            var saved = context.Config.Settings;

            // Neu gestartet mit dieser Konfiguration: dieselben Höhen je Ansicht.
            context = new TestLibraryContext(ManyGames(80));
            context.ConfigService.Update(c =>
            {
                c.Settings.LibraryHeroRatio = saved.LibraryHeroRatio;
                c.Settings.LibraryGridHeroRatio = saved.LibraryGridHeroRatio;
                c.Settings.LibraryShelfHeroRatio = saved.LibraryShelfHeroRatio;
            });
            (window, model) = Open(context, LibraryViewMode.Shelf, size);
            splitter = Named<HeroSplitter>(window, "HeroSplitter");
            Assert.Equal(shelfHeight, window.HeroHeight);
            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, size);
            Assert.Equal(gridDefault + 60, window.HeroHeight);

            // Zurücksetzen (Invoke wie Doppelklick und Eingabetaste) gilt nur für die Ansicht, in der es geschieht.
            Invoke(splitter);
            Arrange(window, size);
            Assert.Equal(gridDefault, window.HeroHeight);
            Assert.Null(model.HeroRatioFor(LibraryViewMode.Grid));
            Assert.Equal(Math.Round(shelfHeight / root, 4), model.HeroRatioFor(LibraryViewMode.Shelf));
            window.Close();
            Assert.Null(context.Config.Settings.LibraryGridHeroRatio);
            Assert.Equal(Math.Round(shelfHeight / root, 4), context.Config.Settings.LibraryShelfHeroRatio);
            Assert.Null(context.Config.Settings.LibraryHeroRatio);
        });
    }

    [Theory]
    [InlineData(1745, 982, 1)] // 1920×1080 bei 110 %
    [InlineData(1164, 727, 2)] // 1280×800 bei 110 %: zweizeilige Leiste
    [InlineData(1000, 620, 2)] // kleinstes Fenster
    public void Grid_grip_keeps_one_whole_tile_row_and_moves_with_the_keyboard(double width, double height, int rows)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var context = new TestLibraryContext(ManyGames(80));
            var (window, model) = Open(context, LibraryViewMode.Grid, size);
            Assert.Equal(rows, window.ToolbarRows);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var grid = Named<ListBox>(window, "GameGrid");
            var start = window.HeroHeight;

            // Ende: der größte Titelbereich; die erste Kachelreihe bleibt ganz zu sehen, mit 17 px Luft über und unter der Leiste.
            Press(window, splitter, Key.End, size);
            Assert.Equal(Range(splitter).Maximum, window.HeroHeight);
            var tile = (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.True(window.HeroHeight == ShowcaseLayout.MinListHero || Bottom(window, tile) <= Bottom(window, grid) + 0.5, $"{width}×{height}: Kachel endet bei {Bottom(window, tile):0.#}, Raster bei {Bottom(window, grid):0.#}");
            var bar = Named<FrameworkElement>(window, "ToolbarArea");
            Assert.Equal(ShowcaseLayout.ToolbarGap, Top(window, bar) - Bottom(window, Named<FrameworkElement>(window, "Hero")), 0.5);
            Assert.Equal(ShowcaseLayout.ToolbarGap, Top(window, tile) - Bottom(window, bar), 0.5);
            AssertGripOnEdge(window, "Raster, größter Titelbereich");
            Press(window, splitter, Key.PageDown, size);
            Assert.Equal(Range(splitter).Maximum, window.HeroHeight); // darüber hinaus geschieht nichts

            // Pos1, Pfeiltasten und Bild ab wie in der Liste.
            Press(window, splitter, Key.Home, size);
            Assert.Equal(ShowcaseLayout.MinListHero, window.HeroHeight);
            Press(window, splitter, Key.Down, size);
            Assert.Equal(Math.Min(Range(splitter).Maximum, ShowcaseLayout.MinListHero + HeroSplitter.SmallStep), window.HeroHeight);
            AssertGripOnEdge(window, "Raster nach Tasten");
            context.Dispatcher.RunDelayed();
            Assert.Equal(Math.Round(window.HeroHeight / RootHeight(window), 4), context.Config.Settings.LibraryGridHeroRatio);

            // Eingabetaste: zurück zum Standard des Rasters, nichts mehr gemerkt.
            Press(window, splitter, Key.Enter, size);
            Assert.Equal(start, window.HeroHeight);
            Assert.Null(model.HeroRatioFor(LibraryViewMode.Grid));
            window.Close();
            Assert.Null(context.Config.Settings.LibraryGridHeroRatio);
        });
    }

    [Theory]
    [InlineData(1745, 982)] // 1920×1080 bei 110 %
    [InlineData(1280, 800)]
    [InlineData(3127, 1309)] // 3440×1440 bei 110 %
    public void Shelf_grip_trades_hero_height_against_tile_size_down_to_the_smallest_tile_and_keeps_card_and_title(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var context = new TestLibraryContext(ManyGames(20));
            var (window, _) = Open(context, LibraryViewMode.Shelf, size);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var shelf = Named<ListBox>(window, "GameShelf");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(shelf));
            var range = Range(splitter);
            var standardTile = panel.TileHeight;

            // Größter Titelbereich: die Kacheln genau so klein wie erlaubt (200 px), ganz über der Statuszeile.
            range.SetValue(range.Maximum);
            Arrange(window, size);
            Assert.Equal(range.Maximum, window.HeroHeight);
            Assert.Equal(ShowcaseLayout.MinShelfTile, panel.TileHeight);
            AssertTilesWhole(window, $"{width}×{height} größter Titelbereich");
            AssertGripOnEdge(window, "Regal, größter Titelbereich");

            // Ein Schritt kleiner: Die Kachel wächst um ebenso viel (bis 340), die Reihe bleibt unten bündig.
            Press(window, splitter, Key.Up, size);
            Assert.Equal(range.Maximum - HeroSplitter.SmallStep, window.HeroHeight);
            Assert.Equal(Math.Min(340, ShowcaseLayout.MinShelfTile + HeroSplitter.SmallStep), panel.TileHeight);

            // Kleinster Titelbereich: größte Kacheln; Werkzeugleiste unter der Automatik-Karte, Kennzeile, Name bzw. Logo,
            // Angaben und Knöpfe des großen Titels haben Platz (nichts ragt in den Kopf).
            Press(window, splitter, Key.Home, size);
            Assert.Equal(ShowcaseLayout.MinShelfHero, window.HeroHeight);
            Assert.True(panel.TileHeight >= standardTile, $"Kachel {panel.TileHeight}, Standard {standardTile}");
            AssertTilesWhole(window, $"{width}×{height} kleinster Titelbereich");
            var card = Named<FrameworkElement>(window, "AutomationCard");
            var bar = Named<FrameworkElement>(window, "ToolbarArea");
            Assert.True(Top(window, bar) >= Bottom(window, card), $"Leiste {Top(window, bar):0.#}, Karte bis {Bottom(window, card):0.#}");
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "LargeOverline").Visibility);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "LargeMeta").Visibility);
            var heroLarge = Named<FrameworkElement>(window, "HeroLarge");
            var header = Named<FrameworkElement>(window, "HeaderArea");
            var heroTop = Top(window, Named<FrameworkElement>(window, "HeroContent")) + VisualTreeHelper.GetOffset(heroLarge).Y;
            Assert.True(heroTop >= Bottom(window, header), $"Titel beginnt bei {heroTop:0.#}, Kopf endet bei {Bottom(window, header):0.#}");
            Assert.Equal(Top(window, bar) - 24, heroTop + heroLarge.ActualHeight, 0.5);
            AssertGripOnEdge(window, "Regal, kleinster Titelbereich");

            // Esc bricht ein Ziehen ab wie in der Liste: zurück zur Höhe davor, gemerkt bleibt die Höhe von Pos1.
            var before = window.HeroHeight;
            var delta = Math.Min(80, range.Maximum - before);
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, delta));
            Assert.Equal(before + delta, window.HeroHeight);
            Press(window, splitter, Key.Escape, size);
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, true)); // ohne echte Maus endet das Ziehen erst hier
            Arrange(window, size);
            Assert.Equal(before, window.HeroHeight);
            context.Dispatcher.RunDelayed();
            Assert.Equal(Math.Round(ShowcaseLayout.MinShelfHero / RootHeight(window), 4), context.Config.Settings.LibraryShelfHeroRatio);
            window.Close();
        });

        static void AssertTilesWhole(MainWindow window, string what)
        {
            var shelf = Named<ListBox>(window, "GameShelf");
            var statusTop = Top(window, Named<FrameworkElement>(window, "StatusBar"));
            Assert.True(Bottom(window, shelf) <= statusTop + 0.5, $"{what}: Reihe endet bei {Bottom(window, shelf):0.#}, Statuszeile bei {statusTop:0.#}");
            var tiles = WpfTestHost.Visuals(shelf).OfType<ListBoxItem>().Where(c => c.ActualHeight > 0).ToList();
            Assert.NotEmpty(tiles);
            foreach (var tile in tiles)
            {
                var top = tile.TransformToAncestor(shelf).Transform(default).Y;
                Assert.True(top >= 0 && top + tile.ActualHeight <= shelf.ActualHeight + 0.5, $"{what}: Kachel {top:0}–{top + tile.ActualHeight:0} in Reihe 0–{shelf.ActualHeight:0}");
            }
        }
    }

    /// <summary>Tastenereignisse brauchen eine Quelle; das Fenster wird in Tests nie angezeigt.</summary>
    private sealed class KeySource : PresentationSource
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
