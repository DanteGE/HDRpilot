using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kacheln in Raster und Regal (Wunsch des Nutzers 2026-10-22): Das Leuchten hinter der gewählten Kachel war unten
/// abgeschnitten, und beim Überfahren verschwand die erste Reihe zum Teil unter der Werkzeugleiste. Jetzt läuft das Leuchten
/// ganz in seiner Fläche aus und hat unten Platz (Raster und Regal); im Regal hat die Kachelreihe oben Platz zum Anheben,
/// im Raster wachsen Kacheln ohne Platz nach oben nach unten. Auf dem WPF-Testhost, nie angezeigt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class TileRoomTests
{
    private static (MainWindow Window, LibraryViewModel Model) Open(LibraryViewMode view, Size size)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }

        var context = new TestLibraryContext([.. Enumerable.Range(1, 60).Select(i => Game(GameSource.Steam, "g" + i, $"Spiel {i:00}", isNew: false))]);
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        var window = new MainWindow();
        window.Attach(model);
        model.SelectedGame = model.GamesView.Cast<GameItemViewModel>().First(); // die erste Kachel
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

    private static T Named<T>(MainWindow window, string name) => (T)window.FindName(name);

    private static List<ListBoxItem> Tiles(ListBox list) => WpfTestHost.Visuals(list).OfType<ListBoxItem>().Where(c => c.ActualHeight > 0).ToList();

    private static T Part<T>(ListBoxItem tile, string name)
        where T : FrameworkElement => WpfTestHost.Visuals(tile).OfType<T>().First(e => e.Name == name);

    /// <summary>
    /// Oberkante einer Kachel beim Überfahren, im sichtbaren Bereich ihrer Liste (wie die Vorlage sie bewegt): steigt sie, um
    /// 10 px und das halbe Wachstum; wächst sie nach unten, bleibt ihre Oberkante; dazu der Ring der gewählten Kachel.
    /// </summary>
    private static double HoverTop(ListBox list, ListBoxItem tile)
    {
        var top = tile.TransformToAncestor(list).Transform(default).Y;
        var root = Part<Grid>(tile, "TileRoot");
        var rises = VirtualizingTilePanel.GetRisesOnHover(tile);
        Assert.Equal(rises ? new Point(0.5, 0.5) : new Point(0.5, 0), root.RenderTransformOrigin); // die Vorlage folgt dem Panel
        var growth = (VirtualizingTilePanel.HoverScale - 1) * tile.ActualHeight;
        return rises
            ? top - VirtualizingTilePanel.HoverLift - (growth / 2) - (VirtualizingTilePanel.RingOutset * VirtualizingTilePanel.HoverScale)
            : top - (VirtualizingTilePanel.RingOutset * VirtualizingTilePanel.HoverScale);
    }

    [Theory]
    [InlineData(1745, 982)] // 1920×1080 bei 110 %
    [InlineData(1164, 727)] // 1280×800 bei 110 %
    [InlineData(3127, 1309)] // 3440×1440 bei 110 %
    public void Hovered_grid_tiles_never_vanish_under_the_toolbar_the_first_row_grows_downward(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, _) = Open(LibraryViewMode.Grid, size);
            var grid = Named<ListBox>(window, "GameGrid");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(grid));
            Assert.Equal(new Thickness(4, ShowcaseLayout.GridPaddingTop, 4, 32), panel.Padding);

            foreach (var offset in new[] { 0.0, 37, panel.TileHeight + panel.Gap - 3, 2 * (panel.TileHeight + panel.Gap) })
            {
                panel.SetVerticalOffset(offset);
                Arrange(window, size);
                var tiles = Tiles(grid);
                Assert.NotEmpty(tiles);
                foreach (var tile in tiles.Where(t => t.TransformToAncestor(grid).Transform(default).Y >= 0))
                {
                    // Ganz sichtbare Kacheln bleiben auch beim Überfahren ganz im sichtbaren Bereich (Ring eingeschlossen).
                    var hoverTop = HoverTop(grid, tile);
                    Assert.True(hoverTop >= -0.5, FormattableString.Invariant($"{width}×{height}, Bildlauf {offset:0}: überfahrene Kachel beginnt bei {hoverTop:0.#} über dem Raster"));
                }
            }

            // Ungescrollt: die erste Reihe wächst nach unten, die zweite steigt wie bisher.
            panel.SetVerticalOffset(0);
            Arrange(window, size);
            var first = (ListBoxItem)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (ListBoxItem)grid.ItemContainerGenerator.ContainerFromIndex(panel.Columns);
            Assert.False(VirtualizingTilePanel.GetRisesOnHover(first));
            Assert.True(VirtualizingTilePanel.GetRisesOnHover(second));
            window.Close();
        });
    }

    [Theory]
    [InlineData(1745, 982)]
    [InlineData(1000, 620)] // kleinstes Fenster
    [InlineData(3127, 1309)]
    public void Hovered_shelf_tiles_rise_fully_inside_the_row_which_starts_at_the_toolbar(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, _) = Open(LibraryViewMode.Shelf, size);
            var shelf = Named<ListBox>(window, "GameShelf");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(shelf));
            Assert.Equal(new Thickness(32, ShowcaseLayout.ShelfPaddingTop, 32, 22 + ShowcaseLayout.ShelfGlowRoom), panel.Padding);
            var bar = Named<FrameworkElement>(window, "ToolbarArea");
            Assert.Equal(bar.TransformToAncestor(window).Transform(new Point(0, bar.ActualHeight)).Y, shelf.TransformToAncestor(window).Transform(default).Y, 0.5);
            foreach (var tile in Tiles(shelf))
            {
                Assert.True(VirtualizingTilePanel.GetRisesOnHover(tile));
                var hoverTop = HoverTop(shelf, tile);
                Assert.True(hoverTop >= 0, FormattableString.Invariant($"{width}×{height}: überfahrene Kachel beginnt bei {hoverTop:0.#} über der Reihe"));
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData(LibraryViewMode.Grid)]
    [InlineData(LibraryViewMode.Shelf)]
    public void Glow_under_the_selected_tile_fades_out_completely_inside_its_list(LibraryViewMode view)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model) = Open(view, size);
            var list = Named<ListBox>(window, view == LibraryViewMode.Grid ? "GameGrid" : "GameShelf");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(list));
            if (view == LibraryViewMode.Grid)
            {
                // Raster: die letzte Kachel, ganz nach unten gescrollt (unter den anderen Reihen liegt das Leuchten unter den
                // Kacheln der nächsten Reihe; unter der letzten hat es den Innenabstand).
                model.SelectedGame = model.GamesView.Cast<GameItemViewModel>().Last();
                panel.SetVerticalOffset(panel.ExtentHeight);
                Arrange(window, size);
            }

            var tile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(model.SelectedGame);
            Assert.True(tile.IsSelected);
            var halo = Part<Border>(tile, "SelectedHalo");
            Assert.Equal(0.6, halo.Opacity, 3);

            // Die Fläche des Leuchtens liegt ganz in der Liste (unten Innenabstand dafür; im Raster für die letzte Reihe).
            var box = halo.TransformToAncestor(list).TransformBounds(new Rect(halo.RenderSize));
            var tileBox = tile.TransformToAncestor(list).TransformBounds(new Rect(tile.RenderSize));
            Assert.Equal(32, box.Bottom - tileBox.Bottom, 0.5);
            Assert.True(panel.Padding.Bottom >= box.Bottom - tileBox.Bottom - 0.5, $"Innenabstand unten {panel.Padding.Bottom}, Leuchten {box.Bottom - tileBox.Bottom:0.#} unter der Kachel");
            if (view == LibraryViewMode.Shelf)
            {
                Assert.True(box.Bottom <= list.ActualHeight + 0.5, $"Leuchten endet bei {box.Bottom:0.#}, Reihe bei {list.ActualHeight:0.#}");
                var statusTop = Named<FrameworkElement>(window, "StatusBar").TransformToAncestor(window).Transform(default).Y;
                Assert.True(list.TransformToAncestor(window).Transform(new Point(0, list.ActualHeight)).Y <= statusTop + 0.5, "Reihe endet über der Statuszeile");
            }

            // Gerendert: unter der Kachel läuft das Leuchten weich aus, ohne Kante (vorher brach es 16 px unter der Kachel mit
            // einem Sprung ab). Gemessen in der Deckkraft der Liste allein (durchsichtiger Grund).
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(list.ActualWidth), (int)Math.Ceiling(list.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(list);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            int Alpha(int x, int y) => pixels[(y * stride) + (x * 4) + 3];
            var x = (int)Math.Round(tileBox.Left + (tileBox.Width / 2));
            var from = (int)Math.Ceiling(tileBox.Bottom) + 5; // unter dem Ring der gewählten Kachel
            var to = Math.Min(bitmap.PixelHeight - 1, (int)Math.Floor(box.Bottom) + 4);
            Assert.True(Alpha(x, from) > 20, $"Leuchten direkt unter der Kachel: Deckkraft {Alpha(x, from)}");
            var steps = Enumerable.Range(from, to - from).Select(y => Math.Abs(Alpha(x, y + 1) - Alpha(x, y))).ToList();
            Assert.True(steps.Max() <= 8, $"Kante im Leuchten: Sprung {steps.Max()} bei {from + steps.IndexOf(steps.Max())} (Kachel endet {tileBox.Bottom:0}, Leuchten {box.Bottom:0})");
            Assert.True(Alpha(x, to) <= 2, $"Leuchten ausgelaufen: Deckkraft {Alpha(x, to)}");
            window.Close();
        });
    }
}
