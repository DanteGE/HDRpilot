using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Hauptfenster „Showcase Hero“ auf dem WPF-Testhost (E89, G10.5), erzeugt und angeordnet, nie angezeigt: drei Ansichten in
/// der Reihenfolge Liste, Raster, Regal; nur die aktive Ansicht hat Daten; das Raster verteilt gleich große Kacheln über
/// die ganze Breite und erzeugt nur sichtbare; Tastenkürzel, Kontextmenü, Nit-Anzeige und keine Bindungsfehler.
/// </summary>
public sealed class ShowcaseViewsTests
{
    private static GameConfig[] ManyGames(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Game(i % 3 == 0 ? GameSource.Epic : GameSource.Steam, "g" + i, $"Spiel {i:000}", hdrAuto: i % 4 == 0, isNew: false))];

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context) Open(TestLibraryContext context, LibraryViewMode? view = null, Size? size = null)
    {
        EnsureLibraryResources();
        if (view is { } v)
        {
            context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(v));
        }

        var model = new LibraryViewModel(context, new FakeDialogs());
        var window = new MainWindow();
        window.Attach(model);
        Arrange(window, size ?? new Size(1600, 1000));
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

    private static List<ListBoxItem> Containers(ListBox list) => WpfTestHost.Visuals(list).OfType<ListBoxItem>().ToList();

    [Fact]
    public void Views_are_offered_in_the_order_list_grid_shelf_and_switch_with_ctrl_digits()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context) = Open(new TestLibraryContext(ManyGames(12)));

            var buttons = new[] { "ViewListButton", "ViewGridButton", "ViewShelfButton" }.Select(n => Named<RadioButton>(window, n)).ToList();
            var x = buttons.Select(b => b.TransformToAncestor(window).Transform(default).X).ToList();
            Assert.True(x[0] < x[1] && x[1] < x[2], "Reihenfolge Liste, Raster, Regal: " + string.Join(", ", x));
            Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
            Assert.Equal(LibraryViewMode.List, window.AppliedView); // Standard: Liste
            Assert.True(buttons[0].IsChecked);

            Assert.True(window.HandleKey(Key.D2, ModifierKeys.Control, null));
            Assert.Equal(LibraryViewMode.Grid, window.AppliedView);
            Assert.True(buttons[1].IsChecked);
            Assert.True(window.HandleKey(Key.NumPad3, ModifierKeys.Control, null));
            Assert.Equal(LibraryViewMode.Shelf, window.AppliedView);
            Assert.True(window.HandleKey(Key.D1, ModifierKeys.Control, null));
            Assert.Equal(LibraryViewMode.List, window.AppliedView);
            Assert.False(window.HandleKey(Key.D2, ModifierKeys.None, null)); // ohne Strg nichts
            Assert.Null(context.Config.Settings.LibraryView); // zuletzt Liste: Standard, nichts gespeichert
            Assert.Equal(LibraryViewMode.List, model.ViewMode);
            window.Close();
        });
    }

    [Fact]
    public void Only_the_active_view_holds_the_games_and_the_selection_survives_switching()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(30)));
            var list = Named<ListBox>(window, "GameList");
            var grid = Named<ListBox>(window, "GameGrid");
            var shelf = Named<ListBox>(window, "GameShelf");
            var selected = model.Games[7];
            model.SelectedGame = selected;

            foreach (var (view, active) in new[] { (LibraryViewMode.Grid, grid), (LibraryViewMode.Shelf, shelf), (LibraryViewMode.List, list) })
            {
                model.ViewMode = view;
                Arrange(window, new Size(1600, 1000));
                foreach (var box in new[] { list, grid, shelf })
                {
                    Assert.Equal(ReferenceEquals(box, active) ? model.GamesView : null, box.ItemsSource);
                    Assert.False(box.IsSynchronizedWithCurrentItem);
                }

                Assert.Same(selected, model.SelectedGame);
                Assert.Same(selected, active.SelectedItem);
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    public void Grid_realizes_only_visible_tiles_of_one_size_across_the_full_width(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(new TestLibraryContext(ManyGames(400)), LibraryViewMode.Grid, new Size(width, height));
            var grid = Named<ListBox>(window, "GameGrid");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(grid));
            var expected = RasterLayout.Compute(panel.LayoutWidth);

            Assert.Equal(expected.Columns, panel.Columns);
            Assert.Equal(expected.TileWidth, panel.TileWidth);
            Assert.InRange(panel.TileWidth, RasterLayout.DefaultMinTile, RasterLayout.DefaultMaxTile);
            var tiles = Containers(grid);
            Assert.InRange(tiles.Count, panel.Columns, 200); // virtualisiert: nie alle 400
            Assert.All(tiles, t => Assert.Equal(panel.TileWidth, t.ActualWidth, 1));
            Assert.All(tiles, t => Assert.Equal(panel.TileHeight, t.ActualHeight, 1));

            // Rechts bleibt nur das Abrunden übrig.
            var used = (panel.Columns * panel.TileWidth) + ((panel.Columns - 1) * expected.Gap);
            Assert.True(panel.LayoutWidth - used < 2 * panel.Columns, $"Rest {panel.LayoutWidth - used:0.0}");
            if (width >= 3440)
            {
                Assert.True(panel.Columns >= 15, "3440 px: " + panel.Columns + " Spalten");
            }

            window.Close();
        });
    }

    [Fact]
    public void Shelf_shows_the_big_hero_the_automation_card_and_the_details_as_a_drawer()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(40)), LibraryViewMode.Shelf);
            model.SelectedGame = model.Games[0];
            Arrange(window, new Size(1600, 1000));

            var rootHeight = Named<FrameworkElement>(window, "Root").ActualHeight;
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, rootHeight, false), window.HeroHeight);
            Assert.True(window.HeroHeight >= 500);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "AutomationCard").Visibility);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "HeroLarge").Visibility);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "LibrarySplitter").Visibility);
            Assert.True(window.HandleKey(Key.I, ModifierKeys.None, null));
            Assert.True(model.IsDetailsDrawerOpen);
            Assert.True(window.HandleKey(Key.Escape, ModifierKeys.None, null));
            Assert.False(model.IsDetailsDrawerOpen);

            var shelf = Named<ListBox>(window, "GameShelf");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(shelf));
            Assert.True(panel.VisibleCount >= 5, "Kacheln im Regal: " + panel.VisibleCount);
            Assert.InRange(Containers(shelf).Count, panel.VisibleCount, panel.VisibleCount + 10);

            // Liste: schmaler Titelbereich, Details als Spalte mit Trenner.
            model.ViewMode = LibraryViewMode.List;
            Arrange(window, new Size(1600, 1000));
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.List, rootHeight, false), window.HeroHeight);
            Assert.InRange(window.HeroHeight, 180, 240);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "AutomationCard").Visibility);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "LibrarySplitter").Visibility);
            window.Close();
        });
    }

    [Fact]
    public void Enter_launches_space_toggles_hdr_auto_and_arrows_move_in_the_grid()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context) = Open(new TestLibraryContext(ManyGames(40)));
            model.SelectedGame = model.Games[0];
            Arrange(window, new Size(1600, 1000));
            var row = Assert.IsType<ListBoxItem>(Named<ListBox>(window, "GameList").ItemContainerGenerator.ContainerFromItem(model.Games[0]));

            var before = model.Games[0].HdrAuto;
            Assert.True(window.HandleKey(Key.Space, ModifierKeys.None, row));
            Assert.Equal(!before, model.Games[0].HdrAuto);
            Assert.True(window.HandleKey(Key.Enter, ModifierKeys.None, row));
            WpfTestHost.DoEvents();
            Assert.Equal([model.Games[0].Key], context.Launched);

            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, new Size(1600, 1000));
            var grid = Named<ListBox>(window, "GameGrid");
            var columns = MainWindow.FindTilePanel(grid)!.Columns;
            var tile = Assert.IsType<ListBoxItem>(grid.ItemContainerGenerator.ContainerFromIndex(grid.SelectedIndex));
            var start = grid.SelectedIndex;
            Assert.True(window.HandleKey(Key.Down, ModifierKeys.None, tile));
            Assert.Equal(start + columns, grid.SelectedIndex);
            Assert.Same(model.SelectedGame, grid.SelectedItem);
            Assert.True(window.HandleKey(Key.Right, ModifierKeys.None, tile));
            Assert.Equal(start + columns + 1, grid.SelectedIndex);
            window.Close();
        });
    }

    [Fact]
    public void Context_menu_of_rows_and_tiles_offers_choose_artwork_and_change_icon()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(6)));
            model.SelectedGame = model.Games[0];
            Arrange(window, new Size(1600, 1000));

            foreach (var name in new[] { "GameList", "GameGrid" })
            {
                model.ViewMode = name == "GameList" ? LibraryViewMode.List : LibraryViewMode.Grid;
                Arrange(window, new Size(1600, 1000));
                var box = Named<ListBox>(window, name);
                var item = Containers(box)[0];
                var menu = Assert.IsType<ContextMenu>(item.ContextMenu);
                menu.PlacementTarget = item;
                WpfTestHost.DoEvents();
                var ids = menu.Items.OfType<MenuItem>().ToDictionary(AutomationProperties.GetAutomationId);
                Assert.Same(model.ChooseArtworkCommand, ids["ChooseArtworkMenuItem"].Command);
                Assert.Same(model.ChangeIconCommand, ids["ChangeIconMenuItem"].Command);
                Assert.Same(model.OpenInstallFolderCommand, ids["ContextOpenFolder"].Command);
                Assert.All(ids.Values, m => Assert.False(string.IsNullOrWhiteSpace(m.Header as string)));
            }

            window.Close();
        });
    }

    [Fact]
    public void Nits_capsule_shows_real_values_and_the_scale_ends_at_the_peak()
    {
        WpfTestHost.Run(() =>
        {
            var context = new TestLibraryContext(ManyGames(3));
            var luminance = new FakeLuminanceService();
            luminance.Set(new DisplayNits(FakeHdrController.Primary, "MSI MAG 341CQP", 456, 285, 0.04, 10, HdrActive: false, PeakMeasuredWithHdr: true));
            context.Luminance = luminance;
            var (window, model, _) = Open(context);

            var scale = Assert.IsType<NitsScaleBar>(window.FindName("NitsScale"));
            Assert.Equal(456, scale.Maximum);
            Assert.Equal(456, scale.Value);
            Assert.Equal(285, scale.FullFrame);
            Assert.Equal(model.Nits.PeakText, Named<TextBlock>(window, "PeakValue").Text);
            Assert.Equal(model.Nits.FullFrameText, Named<TextBlock>(window, "FullFrameValue").Text);
            Assert.Equal(model.Nits.MinimumText, Named<TextBlock>(window, "MinimumValue").Text);
            Assert.Contains("456", AutomationProperties.GetName(Named<TextBlock>(window, "PeakValue")), StringComparison.Ordinal);

            luminance.Set(new DisplayNits(FakeHdrController.Primary, "MSI MAG 341CQP", 1015, 600, 0.01, 10, HdrActive: true, PeakMeasuredWithHdr: true));
            WpfTestHost.DoEvents();
            Assert.Equal(1015, scale.Maximum);
            window.Close();
        });
    }

    [Fact]
    public void Main_window_has_no_binding_errors_in_any_view()
    {
        var errors = WpfTestHost.Run(() =>
        {
            using var recorder = new BindingErrorRecorder();
            var context = new TestLibraryContext(ManyGames(24)) { Artwork = new FakeArtworkService() };
            context.HdrSupport["Steam:g1"] = Core.HdrSupport.HdrSupportStatus.Native;
            var (window, model, _) = Open(context);
            model.SelectedGame = model.Games[1];
            foreach (var view in LibraryViewModes.Ordered)
            {
                model.ViewMode = view;
                Arrange(window, new Size(1600, 1000));
                model.IsDetailsDrawerOpen = view == LibraryViewMode.Shelf;
                Arrange(window, new Size(1600, 1000));
            }

            model.IsThumbnailIcon = true;
            model.ViewMode = LibraryViewMode.List;
            Arrange(window, new Size(1600, 1000));
            window.Close();
            WpfTestHost.DoEvents();
            return recorder.Messages;
        });

        Assert.Empty(errors);
    }

    /// <summary>Hört in der Ablaufverfolgung der Bindungen mit (wie ShellBindingTests).</summary>
    private sealed class BindingErrorRecorder : TraceListener
    {
        private readonly SourceLevels _previousLevel;
        private readonly List<string> _messages = [];

        public BindingErrorRecorder()
        {
            PresentationTraceSources.Refresh();
            var source = PresentationTraceSources.DataBindingSource;
            _previousLevel = source.Switch.Level;
            source.Switch.Level = SourceLevels.Warning;
            source.Listeners.Add(this);
        }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                lock (_messages)
                {
                    _messages.Add(message);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            var source = PresentationTraceSources.DataBindingSource;
            source.Listeners.Remove(this);
            source.Switch.Level = _previousLevel;
            base.Dispose(disposing);
        }
    }
}
