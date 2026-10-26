using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Hauptfenster „Showcase Hero“ auf dem WPF-Testhost (E89, G10.5), erzeugt und angeordnet, nie angezeigt: drei Ansichten in
/// der Reihenfolge Liste, Raster, Regal; nur die aktive Ansicht hat Daten; das Raster verteilt gleich große Kacheln über
/// die ganze Breite und erzeugt nur sichtbare; der Titelbereich der Liste ist ein Drittel hoch und per Ziehgriff einstellbar
/// (Ziehen, UI-Automation, gemerkt, kein Einklappen beim Scrollen); Tastenkürzel, Kontextmenü, Nit-Anzeige und keine
/// Bindungsfehler. Läuft allein, weil die Prüfung auf Bindungsfehler prozessweit mithört (siehe
/// <see cref="BindingErrorRecorder"/>).
/// </summary>
[Collection(ShellSerialCollection.Name)]
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
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, rootHeight), window.HeroHeight);
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

            // Liste: Titelbereich ein Drittel hoch (einstellbar), Details als Spalte mit Trenner.
            model.ViewMode = LibraryViewMode.List;
            Arrange(window, new Size(1600, 1000));
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.List, rootHeight), window.HeroHeight);
            Assert.Equal(Math.Round(rootHeight / 3), window.HeroHeight);
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
    public void Snapshot_draws_the_context_menu_of_the_selected_row_without_opening_a_popup()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(6)));
            model.SelectedGame = model.Games[1];
            Arrange(window, new Size(1600, 1000));

            var overlay = window.CreateContextMenuPreview();

            Assert.NotNull(overlay);
            var menu = Assert.IsType<ContextMenu>(overlay.Element);
            WpfTestHost.DoEvents();
            Assert.False(menu.IsOpen); // nie ein Popup auf dem echten Bildschirm
            Assert.Same(model, menu.DataContext); // wirkt wie beim Rechtsklick auf das gewählte Spiel
            Assert.Contains(menu.Items.OfType<MenuItem>(), m => AutomationProperties.GetAutomationId(m) == "ContextLaunch");
            var root = Named<FrameworkElement>(window, "Root");
            Assert.InRange(overlay.Position.X, 0, root.ActualWidth - menu.DesiredSize.Width);
            Assert.InRange(overlay.Position.Y, 0, root.ActualHeight - menu.DesiredSize.Height);
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

    [Theory]
    [InlineData(1000, 620, 1)] // kleinstes Fenster: die Leiste ist im Regal immer einzeilig
    [InlineData(1178, 633, 1)] // Startgröße auf 1920×1080 bei 150 %
    [InlineData(1164, 727, 1)] // 1280×800 bei 110 % Skalierung
    [InlineData(1280, 720, 1)]
    [InlineData(1280, 800, 1)]
    [InlineData(1366, 768, 1)]
    [InlineData(1600, 1000, 1)]
    [InlineData(1745, 982, 1)] // 1920×1080 bei 110 %
    [InlineData(1920, 1080, 1)]
    [InlineData(3127, 1309, 1)] // 3440×1440 bei 110 %
    [InlineData(3440, 1440, 1)]
    public void Shelf_tiles_stay_whole_above_the_status_bar_at_every_window_height(double width, double height, int rows)
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(12)), LibraryViewMode.Shelf, new Size(width, height));
            model.SelectedGame = model.Games[0];
            Arrange(window, new Size(width, height));
            Assert.Equal(rows, window.ToolbarRows);

            var shelf = Named<ListBox>(window, "GameShelf");
            var statusTop = Named<FrameworkElement>(window, "StatusBar").TransformToAncestor(window).Transform(default).Y;
            var shelfBottom = shelf.TransformToAncestor(window).Transform(new Point(0, shelf.ActualHeight)).Y;
            var tiles = Containers(shelf).Where(c => c.ActualHeight > 0).ToList();
            Assert.NotEmpty(tiles);
            foreach (var tile in tiles)
            {
                var top = tile.TransformToAncestor(shelf).Transform(default).Y;
                Assert.True(top >= 0 && top + tile.ActualHeight <= shelf.ActualHeight + 0.5,
                    $"{width}×{height}: Kachel {top:0}–{top + tile.ActualHeight:0} in Reihe 0–{shelf.ActualHeight:0}, Titelbereich {window.HeroHeight:0}");
                Assert.InRange(tile.ActualHeight / tile.ActualWidth, 1.48, 1.52); // 2:3, Breite auf ganze Pixel gerundet
            }

            Assert.True(shelfBottom <= statusTop + 0.5, $"{width}×{height}: Reihe endet bei {shelfBottom:0}, Statuszeile beginnt bei {statusTop:0}");
            Assert.True(window.HeroHeight >= ShowcaseLayout.MinShelfHero, $"{width}×{height}: Titelbereich {window.HeroHeight:0}");

            // Kennzeile, Logo bzw. Name, Angaben und Knöpfe ragen nicht in den Kopf (unter 92 px); nach ihrer Anordnung, ohne
            // die Verschiebung des Einblendens (läuft nach der Wahl des Spiels noch).
            var heroLarge = Named<FrameworkElement>(window, "HeroLarge");
            var header = Named<FrameworkElement>(window, "HeaderArea");
            var heroTop = Named<FrameworkElement>(window, "HeroContent").TransformToAncestor(window).Transform(default).Y + VisualTreeHelper.GetOffset(heroLarge).Y;
            var headerBottom = header.TransformToAncestor(window).Transform(new Point(0, header.ActualHeight)).Y;
            Assert.True(heroTop >= headerBottom, $"{width}×{height}: Titelinhalt beginnt bei {heroTop:0}, Kopf endet bei {headerBottom:0}");

            // Die Werkzeugleiste (im Regal bei jeder Größe einzeilig) liegt unter der Automatik-Karte, nie darauf; die Knöpfe
            // des Titelbereichs bleiben 24 px über ihr.
            var card = Named<FrameworkElement>(window, "AutomationCard");
            var cardBounds = card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));
            var toolbar = Named<FrameworkElement>(window, "ToolbarArea");
            var bar = toolbar.TransformToAncestor(window).TransformBounds(new Rect(toolbar.RenderSize));
            Assert.True(bar.Top >= cardBounds.Bottom || bar.Right <= cardBounds.Left - 16 + 0.5, $"{width}×{height}: Werkzeugleiste {bar}, Karte {cardBounds}");
            Assert.Equal(bar.Top - 24, heroTop + heroLarge.ActualHeight, 0.5);

            // Unter der Leiste die Bildkante (17 px, die Lasche liegt mittig darauf), 12 px darunter die Kacheln; die Reihe
            // selbst beginnt schon an der Leiste (Platz zum Anheben).
            var edge = Named<FrameworkElement>(window, "Hero").TransformToAncestor(window).Transform(new Point(0, window.HeroHeight)).Y;
            Assert.Equal(ShowcaseLayout.ToolbarGap, edge - bar.Bottom, 0.5);
            Assert.Equal(bar.Bottom, shelf.TransformToAncestor(window).Transform(default).Y, 0.5);
            Assert.All(tiles, tile => Assert.Equal(edge + 12, tile.TransformToAncestor(window).Transform(default).Y, 0.5));
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620)] // kleinstes Fenster
    [InlineData(1164, 727)] // 1280×800 bei 110 %: hier stand „HDRpilot Quiver-Prüfs“, rechts abgeschnitten
    [InlineData(1280, 800)]
    [InlineData(1745, 982)] // 1920×1080 bei 110 %
    [InlineData(1920, 1080)]
    [InlineData(3127, 1309)] // 3440×1440 bei 110 %
    [InlineData(3440, 1440)]
    public void Shelf_title_wraps_left_of_the_card_into_at_most_two_lines_and_then_ends_with_an_ellipsis(double width, double height)
    {
        // Prüfung 2026-10-20: Im Regal brach der Name erst bei 820 px um; daneben steht die Symbol-Kachel, und rechts beginnt die
        // Automatik-Karte. In schmalen Fenstern wurde ein langer Name deshalb ohne „…“ abgeschnitten.
        WpfTestHost.Run(() =>
        {
            const string Long = "HDRpilot Quiver-Prüfspiel";
            const string VeryLong = "Ein sehr langer Spielname, der auch in zwei Zeilen nicht passt: Die Rückkehr des Prüfspiels";
            GameConfig[] games = [Game(GameSource.Quiver, "q1", Long, isNew: false), Game(GameSource.Steam, "s1", VeryLong, isNew: false), Game(GameSource.Steam, "s2", "Black", isNew: false), .. ManyGames(4)];
            var size = new Size(width, height);
            var (window, model, _) = Open(new TestLibraryContext(games), LibraryViewMode.Shelf, size);
            var content = Named<FrameworkElement>(window, "HeroContent");
            var heroLarge = Named<FrameworkElement>(window, "HeroLarge");
            var title = Named<FrameworkElement>(window, "LargeTitle");
            var card = Named<FrameworkElement>(window, "AutomationCard");

            foreach (var (key, name) in new[] { ("Quiver:q1", Long), ("Steam:s1", VeryLong), ("Steam:s2", "Black") })
            {
                model.SelectedGame = model.Games.First(g => g.Key == key);
                Arrange(window, size);
                window.FitCompactHero();
                Arrange(window, size);
                Assert.True(model.Hero!.ShowName && model.Hero.ShowIconTile, "Name mit Symbol-Kachel (ohne Logo, erzeugtes Titelbild)");
                var text = WpfTestHost.Visuals(title).OfType<TextBlock>().First(t => t.Name == "LargeName");
                Assert.Equal(name, text.Text);

                // Ganz zu sehen: links der Automatik-Karte, innerhalb des Titelbereichs (nicht vom Layout abgeschnitten).
                var visible = Named<FrameworkElement>(window, "Root").ActualWidth - content.Margin.Left - content.Margin.Right;
                var bounds = text.TransformToAncestor(content).TransformBounds(new Rect(text.RenderSize));
                var right = text.TransformToAncestor(window).TransformBounds(new Rect(text.RenderSize)).Right;
                var cardLeft = card.TransformToAncestor(window).Transform(default).X;
                Assert.True(bounds.Right <= visible + 0.5, $"{width}×{height} „{name}“: endet bei {bounds.Right:0}, sichtbar {visible:0} px (Umbruchbreite {window.LargeNameWidth:0})");
                Assert.True(right <= cardLeft, $"{width}×{height} „{name}“: endet bei {right:0}, Karte beginnt bei {cardLeft:0}");
                Assert.True(title.DesiredSize.Width <= visible + 0.5, $"{width}×{height} „{name}“: Titel will {title.DesiredSize.Width:0} px, sichtbar {visible:0} px");

                // Höchstens zwei Zeilen (je 70 px), über Angaben und Knöpfen, nie darauf.
                var lines = (int)Math.Round(text.ActualHeight / 70);
                Assert.InRange(lines, 1, 2);
                var below = Named<FrameworkElement>(window, "LargeMeta") is { Visibility: Visibility.Visible } meta ? meta : Named<FrameworkElement>(window, "LargeActions");
                var textBottom = text.TransformToAncestor(heroLarge).TransformBounds(new Rect(text.RenderSize)).Bottom;
                Assert.True(textBottom <= VisualTreeHelper.GetOffset(below).Y + 0.5, $"{width}×{height} „{name}“: Name endet bei {textBottom:0}, darunter beginnt {below.Name} bei {VisualTreeHelper.GetOffset(below).Y:0}");

                // Was nicht in zwei Zeilen passt, endet mit „…“ (der ganze Name bräuchte mehr Zeilen); ein kurzer bleibt einzeilig.
                var full = new FormattedText(name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, 1.0)
                {
                    MaxTextWidth = window.LargeNameWidth,
                    LineHeight = text.LineHeight,
                };
                var needed = (int)Math.Round(full.Height / text.LineHeight);
                Assert.Equal(Math.Min(needed, 2), lines);
                Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                if (name == VeryLong)
                {
                    Assert.True(needed > 2, $"{width}×{height}: der lange Name bräuchte {needed} Zeilen");
                }
                else if (name == "Black")
                {
                    Assert.Equal(1, lines);
                }
            }

            window.Close();
        });
    }

    [Fact]
    public void Compact_hero_offers_launch_while_there_is_room_and_never_cuts_the_name()
    {
        WpfTestHost.Run(() =>
        {
            GameConfig[] games = [Game(GameSource.Xbox, "x1", "Clive Barker's Hellraiser: Revival", isNew: false), .. ManyGames(5)];
            var (window, model, _) = Open(new TestLibraryContext(games));
            model.SelectedGame = model.Games.First(g => g.Key == "Xbox:x1");
            var compact = Named<FrameworkElement>(window, "HeroCompact");
            var actions = Named<FrameworkElement>(window, "CompactActions");
            var title = Named<FrameworkElement>(window, "CompactTitle");
            void Fit(Size size)
            {
                Arrange(window, size);
                window.FitCompactHero();
                Arrange(window, size);
            }

            // 1920×1080 bei 110 %: Starten und „Beim Start …“ rechts im Titelbereich wie im Prototyp, mit demselben Befehl.
            Fit(new Size(1745, 982));
            Assert.Equal(Visibility.Visible, actions.Visibility);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "CompactLaunchChip").Visibility);
            Assert.Same(model.Details!.LaunchCommand, Named<Button>(window, "CompactLaunchButton").Command);
            var right = actions.TransformToAncestor(compact).Transform(new Point(actions.ActualWidth, 0)).X;
            Assert.True(right <= compact.ActualWidth + 0.5, $"Knöpfe enden bei {right:0}, Titelbereich {compact.ActualWidth:0} breit");

            // Kleinstes Fenster: Knöpfe weichen, der lange Name bricht um statt abgeschnitten zu werden (Gegenprobe zum Stand vorher).
            Fit(new Size(1000, 620));
            var content = Named<FrameworkElement>(window, "HeroContent");
            var visible = Named<FrameworkElement>(window, "Root").ActualWidth - content.Margin.Left - content.Margin.Right;
            Assert.Equal(Visibility.Collapsed, actions.Visibility);
            Assert.True(window.CompactNameWidth < MainWindow.MaxCompactName, "Umbruchbreite " + window.CompactNameWidth);
            Assert.True(compact.ActualWidth <= visible + 0.5, $"Titelbereich {compact.ActualWidth:0} px breit, sichtbar {visible:0} px (Titel {title.ActualWidth:0} px)");
            window.Close();
        });
    }

    /// <summary>Oberkante im Inhalt des Fensters (Root, ohne den 1-px-Rahmen), wie die Rechnungen von ShowcaseLayout.</summary>
    private static double Top(MainWindow window, FrameworkElement element) => element.TransformToAncestor(Named<Visual>(window, "Root")).Transform(default).Y;

    private static double Bottom(MainWindow window, FrameworkElement element) => Top(window, element) + element.ActualHeight;

    private static IRangeValueProvider HeroRange(HeroSplitter splitter) =>
        Assert.IsAssignableFrom<IRangeValueProvider>(UIElementAutomationPeer.CreatePeerForElement(splitter).GetPattern(PatternInterface.RangeValue));

    /// <summary>Zeilen der Liste, die ganz zu sehen sind.</summary>
    private static int WholeRows(MainWindow window)
    {
        var list = Named<ListBox>(window, "GameList");
        return Containers(list).Count(row =>
        {
            var top = row.TransformToAncestor(list).Transform(default).Y;
            return row.ActualHeight > 0 && top >= -0.5 && top + row.ActualHeight <= list.ActualHeight + 0.5;
        });
    }

    [Theory]
    [InlineData(1164, 727, 2)] // 1280×800 bei 110 % (Werkzeugleiste zweizeilig)
    [InlineData(1280, 800, 1)] // Sortierung nur als Symbol
    [InlineData(1745, 982, 1)] // 1920×1080 bei 110 %
    [InlineData(1920, 1080, 1)]
    [InlineData(3127, 1309, 1)] // 3440×1440 bei 110 %
    [InlineData(3440, 1440, 1)]
    public void List_hero_is_a_third_high_by_default_with_its_handle_between_hero_and_toolbar(double width, double height, int rows)
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(width, height);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(80)), size: size);
            model.SelectedGame = model.Games[0];
            Arrange(window, size);
            var rootHeight = Named<FrameworkElement>(window, "Root").ActualHeight;
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");

            // Werkzeugleiste: so viele Zeilen, wie die Breite der Liste verlangt (je 40 px, 8 px dazwischen); darunter bleiben
            // Kopf der Liste, fünf ganze Zeilen und Statuszeile (H − 342 − Höhe der Leiste).
            var toolbarHeight = Named<FrameworkElement>(window, "ToolbarArea").ActualHeight;
            Assert.Equal(rows, window.ToolbarRows);
            Assert.Equal((rows * 40) + ((rows - 1) * 8), toolbarHeight, 0.5);

            // Standard: ein Drittel der Fensterhöhe (Wunsch des Nutzers; vorher höchstens 240 px).
            Assert.Equal(Math.Round(rootHeight / 3), window.HeroHeight);
            Assert.Equal(window.HeroHeight, Named<FrameworkElement>(window, "Hero").ActualHeight);

            // Griff: sichtbar, bedienbar, für Bildschirmleser benannt, mit Wertebereich.
            Assert.Equal(Visibility.Visible, splitter.Visibility);
            Assert.Equal("HeroSplitter", AutomationProperties.GetAutomationId(splitter));
            Assert.Equal(LibraryStrings.HeroSplitter_Name, AutomationProperties.GetName(splitter));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(splitter)));
            Assert.True(splitter.Focusable && splitter.IsTabStop);
            Assert.Equal(Cursors.SizeNS, splitter.Cursor);
            var range = HeroRange(splitter);
            Assert.Equal(window.HeroHeight, range.Value);
            Assert.Equal(ShowcaseLayout.MinListHero, range.Minimum);
            Assert.Equal(rootHeight - 342 - toolbarHeight, range.Maximum);
            Assert.Equal(AutomationControlType.Thumb, UIElementAutomationPeer.CreatePeerForElement(splitter).GetAutomationControlType());

            // Lage: am unteren Rand des Titelbilds unter dessen Inhalt, darunter die Werkzeugleiste, dann die Liste; nur über
            // der Spalte der Liste (nicht unter dem Detailbereich).
            var handleTop = Top(window, splitter);
            var handleBottom = Bottom(window, splitter);
            Assert.Equal(window.HeroHeight - ShowcaseLayout.HeroSplitterOverlap, handleTop, 0.5);
            Assert.Equal(ShowcaseLayout.HeroSplitterHeight, splitter.ActualHeight, 0.5);
            // Inhalt nach seiner Anordnung, ohne die Verschiebung des Einblendens (läuft nach der Wahl des Spiels noch).
            var compact = Named<FrameworkElement>(window, "HeroCompact");
            var compactBottom = Top(window, Named<FrameworkElement>(window, "HeroContent")) + VisualTreeHelper.GetOffset(compact).Y + compact.ActualHeight;
            Assert.True(compactBottom <= handleTop + 0.5, $"Inhalt des Titelbereichs (bis {compactBottom:0}) ragt in den Griff ({handleTop:0})");
            var toolbarTop = Top(window, Named<FrameworkElement>(window, "ToolbarArea"));
            Assert.True(toolbarTop >= handleBottom - 0.5, $"Werkzeugleiste ({toolbarTop:0}) unter dem Griff ({handleBottom:0})");
            Assert.True(Top(window, Named<FrameworkElement>(window, "ListCard")) >= handleBottom + 40, "Liste unter der Werkzeugleiste");

            // Werkzeugleiste: oben (zur Unterkante des Titelbilds) so viel Luft wie unten (zur Liste), Wunsch des Nutzers; der
            // sichtbare Griff liegt in dieser Luft.
            var toolbar = Named<FrameworkElement>(window, "ToolbarArea");
            var heroStage = Named<FrameworkElement>(window, "Hero");
            var gapAbove = toolbarTop - (Top(window, heroStage) + heroStage.ActualHeight);
            var gapBelow = Top(window, Named<FrameworkElement>(window, "ListCard")) - (toolbarTop + toolbar.ActualHeight);
            Assert.Equal(gapBelow, gapAbove, 0.5);
            Assert.Equal(17, gapAbove, 0.5); // die Lasche liegt mittig auf der Bildkante, die Leiste 8 px unter ihr

            // Der Griff liegt genau auf der Grenze zwischen Titelbild und Liste (Wunsch des Nutzers): seine Mitte auf der
            // Unterkante des Titelbilds.
            Assert.Equal(Top(window, heroStage) + heroStage.ActualHeight, handleTop + (splitter.ActualHeight / 2), 0.5);
            var details = Named<FrameworkElement>(window, "DetailsArea");
            var handleRight = splitter.TransformToAncestor(window).Transform(new Point(splitter.ActualWidth, 0)).X;
            Assert.True(handleRight <= details.TransformToAncestor(window).Transform(default).X - 20, "Griff endet vor dem Detailbereich");

            // Größter Titelbereich: mindestens fünf ganze Zeilen bleiben, auch unter einer umgebrochenen Werkzeugleiste.
            range.SetValue(range.Maximum);
            Arrange(window, size);
            Assert.Equal(rows, window.ToolbarRows);
            Assert.Equal(rootHeight - 342 - toolbarHeight, window.HeroHeight);
            Assert.True(WholeRows(window) >= ShowcaseLayout.MinListRows, $"{width}×{height}: {WholeRows(window)} ganze Zeilen");
            window.Close();
        });
    }

    [Fact]
    public void List_hero_follows_the_handle_live_and_is_remembered_only_after_a_real_drag()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var context = new TestLibraryContext(ManyGames(40));
            var (window, model, _) = Open(context, size: size);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var rootHeight = Named<FrameworkElement>(window, "Root").ActualHeight;
            var start = window.HeroHeight;
            var updates = context.Updates;
            var delays = context.Dispatcher.PendingDelays;

            // Ziehen: Der Titelbereich folgt sofort (ohne Animation), der Griff mit ihm; gemerkt wird noch nichts.
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 30));
            Assert.Equal(start + 30, window.HeroHeight);
            Arrange(window, size);
            Assert.Equal(start + 30 - ShowcaseLayout.HeroSplitterOverlap, Top(window, splitter), 0.5);
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 24.6)); // ab der neuen Lage des Griffs, ganze Pixel
            Assert.Equal(start + 55, window.HeroHeight);
            Assert.Null(model.HeroRatio);
            Assert.Equal(delays, context.Dispatcher.PendingDelays);

            // Ende des Ziehens: Anteil an der Fensterhöhe gemerkt, einmal gespeichert.
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Assert.Equal(Math.Round((start + 55) / rootHeight, 4), model.HeroRatio);
            context.Dispatcher.RunDelayed();
            Assert.Equal(model.HeroRatio, context.Config.Settings.LibraryHeroRatio);
            Assert.Equal(updates + 1, context.Updates);

            // Bloßer Klick auf den Griff: nichts gemerkt, nichts geschrieben.
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Assert.Equal(0, context.Dispatcher.PendingDelays);
            Assert.Equal(updates + 1, context.Updates);

            // Über den Rand hinaus: Der Griff bleibt am größten Wert stehen.
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 5000));
            Assert.Equal(rootHeight - 382, window.HeroHeight);
            splitter.RaiseEvent(new DragDeltaEventArgs(0, -9000));
            Assert.Equal(ShowcaseLayout.MinListHero, window.HeroHeight);
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            context.Dispatcher.RunDelayed();
            Assert.Equal(Math.Round(ShowcaseLayout.MinListHero / rootHeight, 4), context.Config.Settings.LibraryHeroRatio);

            // Doppelklick: zurück zum Standard, nichts mehr gemerkt.
            splitter.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            Arrange(window, size);
            Assert.Null(model.HeroRatio);
            Assert.Equal(Math.Round(rootHeight / 3), window.HeroHeight);
            Assert.Equal(window.HeroHeight, splitter.Value);
            context.Dispatcher.RunDelayed();
            Assert.Null(context.Config.Settings.LibraryHeroRatio);
            window.Close();
        });
    }

    [Fact]
    public void List_hero_height_is_set_by_ui_automation_restored_after_reopening_and_kept_across_views_and_window_sizes()
    {
        WpfTestHost.Run(() =>
        {
            var large = new Size(3127, 1309);
            var context = new TestLibraryContext(ManyGames(40));
            var (window, model, _) = Open(context, size: large);
            var range = HeroRange(Named<HeroSplitter>(window, "HeroSplitter"));

            // UI-Automation ohne echte Eingaben: setzen wie eine Taste, sofort angewandt und gemerkt.
            range.SetValue(600);
            Assert.Equal(600, window.HeroHeight);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.SetValue(range.Maximum + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => range.SetValue(ShowcaseLayout.MinListHero - 1));
            Assert.Equal(600, window.HeroHeight);
            var rootHeight = Named<FrameworkElement>(window, "Root").ActualHeight;
            window.Close(); // speichert beim Schließen
            var stored = context.Config.Settings.LibraryHeroRatio;
            Assert.Equal(Math.Round(600 / rootHeight, 4), stored);

            // Neu gestartet mit dieser Konfiguration: dieselbe Höhe.
            context = new TestLibraryContext(ManyGames(40));
            context.ConfigService.Update(c => c.Settings.LibraryHeroRatio = stored);
            (window, model, _) = Open(context, size: large);
            Assert.Equal(stored, model.HeroRatio);
            Assert.Equal(600, window.HeroHeight);

            // Raster und Regal mit ihrer eigenen Höhe (hier ihr Standard; die Höhe der Liste gilt dort nicht); die Werkzeugleiste
            // im Raster mit so viel Luft unter dem Titelbild wie darunter bis zur ersten Kachelreihe (17 px). Zurück in der Liste
            // wieder die gemerkte Höhe.
            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, large);
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, rootHeight), window.HeroHeight);
            Assert.Equal(Visibility.Visible, Named<HeroSplitter>(window, "HeroSplitter").Visibility);
            var gridToolbar = Named<FrameworkElement>(window, "ToolbarArea");
            var gridToolbarTop = Top(window, gridToolbar);
            Assert.Equal(window.HeroHeight + 17, gridToolbarTop, 0.5);
            var grid = Named<ListBox>(window, "GameGrid");
            var tile = (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.Equal(17, Top(window, tile) - (gridToolbarTop + gridToolbar.ActualHeight), 0.5);
            model.ViewMode = LibraryViewMode.Shelf;
            Arrange(window, large);
            Assert.Equal(ShowcaseLayout.HeroHeight(LibraryViewMode.Shelf, rootHeight), window.HeroHeight);
            Assert.Equal(Visibility.Visible, Named<HeroSplitter>(window, "HeroSplitter").Visibility);
            Assert.Null(model.HeroRatioFor(LibraryViewMode.Grid));
            Assert.Null(model.HeroRatioFor(LibraryViewMode.Shelf));
            model.ViewMode = LibraryViewMode.List;
            Arrange(window, large);
            Assert.Equal(600, window.HeroHeight);

            // Kleineres Fenster: begrenzt (fünf Zeilen unter der hier zweizeiligen Werkzeugleiste gehen vor), der gemerkte
            // Anteil bleibt; groß wieder 600 px.
            Arrange(window, new Size(1164, 727));
            var smallRoot = Named<FrameworkElement>(window, "Root").ActualHeight;
            Assert.Equal(2, window.ToolbarRows);
            Assert.Equal(smallRoot - 430, window.HeroHeight);
            Assert.Equal(smallRoot - 430, HeroRange(Named<HeroSplitter>(window, "HeroSplitter")).Maximum);
            Arrange(window, large);
            Assert.Equal(600, window.HeroHeight);
            Assert.Equal(stored, model.HeroRatio);
            context.Dispatcher.RunDelayed();
            Assert.Equal(stored, context.Config.Settings.LibraryHeroRatio);

            // Auslösen per UI-Automation wie Doppelklick und Eingabetaste: Standardhöhe, nichts mehr gemerkt.
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            Assert.IsAssignableFrom<IInvokeProvider>(UIElementAutomationPeer.CreatePeerForElement(splitter).GetPattern(PatternInterface.Invoke)).Invoke();
            Arrange(window, large);
            Assert.Null(model.HeroRatio);
            Assert.Equal(Math.Round(rootHeight / 3), window.HeroHeight);
            window.Close();
            Assert.Null(context.Config.Settings.LibraryHeroRatio);
        });
    }

    [Fact]
    public void List_hero_keeps_its_height_while_the_list_scrolls()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(200)), size: size); // erstes Spiel gewählt, ohne Einblenden
            Assert.Same(model.Games[0], model.SelectedGame);

            // Wie beim Anzeigen: Was das Fenster beim Laden an der Liste anmeldet, hört beim Scrollen mit.
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Arrange(window, size);
            var before = window.HeroHeight;
            var scroll = Assert.IsType<ScrollViewer>(Named<ListBox>(window, "GameList").Template.FindName("ScrollHost", Named<ListBox>(window, "GameList")));

            // Früher klappte der Titelbereich ab 60 px Scrollweg auf 120 px zusammen (Wunsch des Nutzers: „Größe bleibt“).
            window.ScrollListBy(30);
            Arrange(window, size);
            WpfTestHost.DoEvents();
            Arrange(window, size);
            Assert.True(scroll.VerticalOffset > 600, "gescrollt: " + scroll.VerticalOffset);
            Assert.Equal(before, window.HeroHeight);
            // Inhalt des Titelbereichs weder ausgeblendet noch gesperrt (die Deckkraft setzt nur das Einblenden beim Öffnen).
            var compact = Named<FrameworkElement>(window, "HeroCompact");
            Assert.Equal(BaseValueSource.Default, DependencyPropertyHelper.GetValueSource(compact, UIElement.OpacityProperty).BaseValueSource);
            Assert.True(compact.IsHitTestVisible);
            Assert.True(Named<FrameworkElement>(window, "CompactActions").IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public void List_hero_drag_back_to_the_start_and_the_second_press_of_a_double_click_remember_nothing()
    {
        WpfTestHost.Run(() =>
        {
            var size = new Size(1745, 982);
            var context = new TestLibraryContext(ManyGames(40));
            var (window, model, _) = Open(context, size: size);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var start = window.HeroHeight;
            var delays = context.Dispatcher.PendingDelays;

            // Prüfung 2026-10-16: hin und zurück gezogen und an der Ausgangslage losgelassen. Nichts gemerkt; der Standard
            // bliebe sonst nicht Standard, sondern stünde fest auf dem Anteil 0,3333.
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 30));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, -30));
            Assert.Equal(start, window.HeroHeight);
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Arrange(window, size);
            Assert.Null(model.HeroRatio);
            Assert.Equal(delays, context.Dispatcher.PendingDelays);
            Assert.Equal(start, window.HeroHeight);

            // Eine eigene Höhe, dann Doppelklick. Sein zweiter Druck (wie vom Eingabesystem: MouseDown, daraus der linke Knopf
            // mit ClickCount 2) setzt zurück und beginnt kein Ziehen, dessen kleine Bewegung das Zurücksetzen gleich wieder
            // überschreiben könnte: Control meldet den Doppelklick, bevor der Thumb den Druck sieht; erledigt erreicht er ihn nie.
            HeroRange(splitter).SetValue(start + 96);
            context.Dispatcher.RunDelayed();
            Assert.NotNull(context.Config.Settings.LibraryHeroRatio);
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(nonPublic: true)!.Invoke(press, [2]);
            splitter.RaiseEvent(press);
            Assert.True(press.Handled);
            Assert.False(splitter.IsDragging);
            Arrange(window, size);
            Assert.Null(model.HeroRatio);
            Assert.Equal(start, window.HeroHeight);
            context.Dispatcher.RunDelayed();
            Assert.Null(context.Config.Settings.LibraryHeroRatio);

            // Gegenprobe: Ein einfacher Druck beginnt das Ziehen (sonst prüfte das oben nichts).
            var single = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(nonPublic: true)!.Invoke(single, [1]);
            splitter.RaiseEvent(single);
            Assert.True(splitter.IsDragging);
            splitter.CancelDrag(); // ohne echte Maus endet das Ziehen nicht von selbst
            Assert.Null(model.HeroRatio);
            window.Close();
        });
    }

    [Fact]
    public void List_hero_drag_without_result_keeps_the_remembered_height_for_larger_windows()
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Gemerkt ist 0,6, im kleinen Fenster begrenzt. Griff hin und her bewegt, an derselben Stelle
            // losgelassen: Danach galt im Fenster der Anteil der begrenzten Höhe, beim Vergrößern blieb der Titelbereich bis zum
            // Neustart kleiner als 0,6 · H.
            var small = new Size(1164, 727);
            var large = new Size(3127, 1309);
            var context = new TestLibraryContext(ManyGames(40));
            context.ConfigService.Update(c => c.Settings.LibraryHeroRatio = 0.6);
            var (window, model, _) = Open(context, size: small);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var smallRoot = Named<FrameworkElement>(window, "Root").ActualHeight;
            Assert.Equal(2, window.ToolbarRows);
            Assert.Equal(smallRoot - 430, window.HeroHeight); // fünf Zeilen unter der zweizeiligen Werkzeugleiste

            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, -40));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 40));
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Arrange(window, small);
            Assert.Equal(smallRoot - 430, window.HeroHeight);
            Assert.Equal(0.6, model.HeroRatio);

            Arrange(window, large);
            var largeRoot = Named<FrameworkElement>(window, "Root").ActualHeight;
            Assert.Equal(Math.Round(0.6 * largeRoot), window.HeroHeight);
            context.Dispatcher.RunDelayed();
            Assert.Equal(0.6, context.Config.Settings.LibraryHeroRatio);
            window.Close();
        });
    }

    [Fact]
    public void List_hero_light_follows_the_height_after_resetting_the_handle()
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Nach dem Zurücksetzen (Doppelklick, Eingabetaste) blieb das Spitzlicht auf der Höhe des
            // kleinen Titelbereichs, bis Spiel oder Farben wechselten.
            var size = new Size(1745, 982);
            var (window, _, _) = Open(new TestLibraryContext(ManyGames(40)), size: size);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var range = HeroRange(splitter);
            range.SetValue(range.Minimum);
            Arrange(window, size);
            var low = window.HeroStage.Light.Y;

            Assert.IsAssignableFrom<IInvokeProvider>(UIElementAutomationPeer.CreatePeerForElement(splitter).GetPattern(PatternInterface.Invoke)).Invoke();
            Arrange(window, size);
            var afterReset = window.HeroStage.Light.Y;

            // Vergleich: neu angeordnet bei gleicher Höhe (Fensterbreite ein Pixel anders), das Licht liegt dort, wo es hingehört.
            Arrange(window, new Size(size.Width + 1, size.Height));
            var expected = window.HeroStage.Light.Y;
            Assert.True(expected > low + 1, $"Licht im hohen Titelbereich {expected:0}, im niedrigen {low:0}");
            Assert.Equal(expected, afterReset, 0.5);

            // Ebenso nach dem Ziehen und mit Tasten: Das Licht folgt der Höhe.
            splitter.RaiseEvent(new DragStartedEventArgs(0, 10));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, -120));
            splitter.RaiseEvent(new DragCompletedEventArgs(0, -120, false));
            Arrange(window, new Size(size.Width + 1, size.Height));
            var dragged = window.HeroStage.Light.Y;
            Arrange(window, size);
            Assert.Equal(window.HeroStage.Light.Y, dragged, 0.5);
            window.Close();
        });
    }

    [Fact]
    public void List_hero_range_keeps_five_whole_rows_below_the_notices()
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Die Hinweise (Fehler beim Einlesen, Vorschläge) liegen zwischen Werkzeugleiste und Liste. Beim
            // größten Titelbereich blieben mit ihnen nur etwa drei ganze Zeilen, aufgeklappt keine.
            var size = new Size(1745, 982);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(80)), size: size);
            var splitter = Named<HeroSplitter>(window, "HeroSplitter");
            var root = Named<FrameworkElement>(window, "Root").ActualHeight;
            HeroRange(splitter).SetValue(root - 382);
            Arrange(window, size);
            var chosen = model.HeroRatio;
            Assert.True(WholeRows(window) >= ShowcaseLayout.MinListRows);

            foreach (var notices in new[] { 60.0, 190.0 }) // zugeklappt, aufgeklappt
            {
                var notice = new Border { Height = notices - 10, Margin = new Thickness(0, 10, 0, 0) };
                Named<StackPanel>(window, "NoticeArea").Children.Add(notice);
                Arrange(window, size);
                Assert.Equal(root - 382 - notices, window.HeroHeight);
                Assert.Equal(root - 382 - notices, HeroRange(splitter).Maximum);
                Assert.True(WholeRows(window) >= ShowcaseLayout.MinListRows, $"mit {notices} px Hinweisen: {WholeRows(window)} ganze Zeilen");
                Named<StackPanel>(window, "NoticeArea").Children.Remove(notice);
                Arrange(window, size);
                Assert.Equal(root - 382, window.HeroHeight); // gewählte Höhe wieder, ohne dass sie neu gemerkt wurde
                Assert.Equal(chosen, model.HeroRatio);
            }

            window.Close();
        });
    }

    [Fact]
    public void List_hero_at_its_smallest_still_shows_the_whole_icon_tile_of_a_game_without_logo()
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Bei 180 px blieben unter dem Kopf 66 px, die Symbol-Kachel (72 px) eines Spiels ohne Logo war
            // unten abgeschnitten.
            var size = new Size(1745, 982);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(40)), size: size);
            Assert.True(model.Hero?.ShowIconTile, "Spiel ohne Titelbild und Logo: Symbol-Kachel vor dem Namen");
            var range = HeroRange(Named<HeroSplitter>(window, "HeroSplitter"));
            range.SetValue(range.Minimum);
            Arrange(window, size);
            Assert.Equal(ShowcaseLayout.MinListHero, window.HeroHeight);

            var title = Named<FrameworkElement>(window, "CompactTitle");
            var tile = WpfTestHost.Visuals(title).OfType<ContentControl>().Single(c => c.Width == 72 && c.Visibility == Visibility.Visible && c.ActualHeight > 0);

            // Lage nach der Anordnung, ohne die Verschiebungen des Einblendens (laufen nach der Wahl des Spiels noch).
            var root = Named<Visual>(window, "Root");
            var top = 0.0;
            for (DependencyObject v = tile; !ReferenceEquals(v, root); v = VisualTreeHelper.GetParent(v))
            {
                top += VisualTreeHelper.GetOffset((Visual)v).Y;
            }

            Assert.True(top >= 98 - 0.5, $"Kachel beginnt bei {top:0.0}, unter dem Kopf erst bei 98");
            Assert.True(top + tile.ActualHeight <= window.HeroHeight - 16 + 0.5, $"Kachel endet bei {top + tile.ActualHeight:0.0}, Titelbereich {window.HeroHeight}");
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620, 2)] // kleinstes Fenster
    [InlineData(1178, 620, 2)] // Startgröße auf 1920×1080 bei 150 % und auf 1280×720
    [InlineData(1164, 727, 2)] // 1280×800 bei 110 %
    [InlineData(1745, 982, 1)] // 1920×1080 bei 110 %
    public void Grid_shows_its_first_tile_row_whole_with_as_much_air_above_the_toolbar_as_below(double width, double height, int rows)
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Mit 22 px Luft über der Werkzeugleiste (Wunsch des Nutzers: oben so viel wie unten) war in
            // niedrigen Fenstern mit zweizeiliger Leiste die erste Kachelreihe unten abgeschnitten (vorher passte sie). Der
            // Titelbereich gibt so weit nach, wie nötig; die Luft bleibt.
            var size = new Size(width, height);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(80)), LibraryViewMode.Grid, size);
            model.SelectedGame = model.Games[0];
            Arrange(window, size);
            Assert.Equal(rows, window.ToolbarRows);

            var grid = Named<ListBox>(window, "GameGrid");
            var toolbar = Named<FrameworkElement>(window, "ToolbarArea");
            var heroStage = Named<FrameworkElement>(window, "Hero");
            var tile = (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var gridBottom = Bottom(window, grid);
            // Passt die Reihe in sehr niedrigen Fenstern nicht mehr ganz, ist der Titelbereich am kleinsten, das Raster scrollt.
            Assert.True(window.HeroHeight == ShowcaseLayout.MinListHero || Bottom(window, tile) <= gridBottom + 0.5, $"{width}×{height}: Kachel endet bei {Bottom(window, tile):0}, Raster bei {gridBottom:0}");
            Assert.True(Top(window, tile) >= Top(window, grid) - 0.5);
            var above = Top(window, toolbar) - Bottom(window, heroStage);
            var below = Top(window, tile) - Bottom(window, toolbar);
            Assert.Equal(ShowcaseLayout.ToolbarGap, above, 0.5);
            Assert.Equal(above, below, 0.5);
            Assert.True(window.HeroHeight >= ShowcaseLayout.MinListHero);
            Assert.True(window.HeroHeight == ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, Named<FrameworkElement>(window, "Root").ActualHeight) || Bottom(window, tile) >= gridBottom - 1, "Titelbereich nicht kleiner als nötig");
            window.Close();
        });
    }

    [Fact]
    public void Grid_hero_follows_the_tile_size_after_switching_views_and_moving_the_column_splitter()
    {
        WpfTestHost.Run(() =>
        {
            // Die Kachelgröße steht erst nach dem Anordnen fest: beim ersten Wechsel ins Raster und wenn der Trenner die Spalte
            // der Liste breiter oder schmaler macht, ohne dass sich das Fenster ändert.
            var size = new Size(1178, 620);
            var (window, model, _) = Open(new TestLibraryContext(ManyGames(80)), size: size);
            Arrange(window, size);
            Assert.Equal(2, window.ToolbarRows); // schon in der Liste zweizeilig

            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window, size);
            AssertFirstRow(window, "nach dem Wechsel");

            foreach (var ratio in new[] { LibraryLayout.MinDetailsRatio, 0.5, 0.35 })
            {
                model.SetDetailsRatio(ratio);
                Arrange(window, size);
                AssertFirstRow(window, "Detailbereich " + ratio);
            }

            window.Close();
        });

        static void AssertFirstRow(MainWindow window, string step)
        {
            var grid = Named<ListBox>(window, "GameGrid");
            var panel = Assert.IsType<VirtualizingTilePanel>(MainWindow.FindTilePanel(grid));
            var tile = (FrameworkElement)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var root = Named<FrameworkElement>(window, "Root").ActualHeight;
            var expected = ShowcaseLayout.HeroHeight(LibraryViewMode.Grid, root, gridRow: Named<FrameworkElement>(window, "ToolbarArea").ActualHeight + panel.TileHeight);
            Assert.True(expected == window.HeroHeight, $"{step}: Titelbereich {window.HeroHeight}, erwartet {expected} (Kachel {panel.TileHeight})");
            Assert.True(window.HeroHeight == ShowcaseLayout.MinListHero || Bottom(window, tile) <= Bottom(window, grid) + 0.5, $"{step}: Kachel endet bei {Bottom(window, tile):0}, Raster bei {Bottom(window, grid):0}");
        }
    }

    [Fact]
    public void Hero_bottom_edge_shows_no_line_of_bright_image_on_a_fractional_pixel()
    {
        WpfTestHost.Run(() =>
        {
            // Prüfung 2026-10-16: Fällt die Unterkante des Titelbilds nicht auf ganze Bildschirmpixel (242 px bei 110 % sind
            // 266,2), rastete der dunkle Verlauf auf die Zeile darüber ein, das größere Bild füllte die angeschnittene Zeile:
            // eine 1 px hohe helle Linie genau am Ziehgriff.
            var stage = new HeroStage { Width = 400, Height = 242, VerticalAlignment = VerticalAlignment.Top };
            var host = new Grid { Width = 400, Height = 262, Background = Brushes.Black, Children = { stage } };
            host.Measure(new Size(400, 262));
            host.Arrange(new Rect(0, 0, 400, 262));
            var white = FakeArtworkService.Solid(32, 9, Colors.White);
            stage.Show(null, new GameArtwork("A", new ArtworkImage(white, ArtworkKind.Hero, ArtworkOrigin.Launcher, 32, 9, "Steam"), null, null, null, null, null, AmbientColors.FromName("A"), ArtworkSummary.Empty), TimeSpan.Zero);
            host.UpdateLayout();

            const double scale = 1.1;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(400 * scale), (int)Math.Ceiling(262 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            double Brightness(int row) => Enumerable.Range(20, 360).Average(x => (double)pixels[(row * stride) + (x * 4) + 1]);

            var cut = (int)Math.Floor(242 * scale); // angeschnittene Zeile 266
            Assert.True(Brightness(cut) <= Brightness(cut - 1) + 2, $"Zeile {cut}: {Brightness(cut):0}, darüber {Brightness(cut - 1):0}");
        });
    }

    [Fact]
    public void Folder_path_is_shortened_in_the_middle_when_its_column_is_narrower_than_the_text()
    {
        WpfTestHost.Run(() =>
        {
            // Wie eine EXE-Zeile im schmalen Detailbereich (Schublade im Regal): Ordner | Herkunft rechts
            const string folder = @"…\FunkTag\FunkTagProjekt\Binaries\Win64";
            var text = new MiddleEllipsisText { FullText = folder, FontSize = 11 };
            var origin = new TextBlock { Text = "beim Spielen erkannt", FontSize = 10.5, Margin = new Thickness(10, 0, 0, 0) };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(origin, 1);
            row.Children.Add(new StackPanel { Children = { text } });
            row.Children.Add(origin);

            for (var i = 0; i < 3; i++)
            {
                WpfTestHost.Layout(row, new Size(260, 40));
                WpfTestHost.DoEvents();
            }

            var column = row.ColumnDefinitions[0].ActualWidth;
            var dpi = VisualTreeHelper.GetDpi(text).PixelsPerDip;
            var shown = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
            Assert.NotEqual(folder, text.Text);
            Assert.Contains(PathEllipsis.Ellipsis, text.Text, StringComparison.Ordinal);
            Assert.EndsWith("Win64", text.Text, StringComparison.Ordinal); // das Ende bleibt lesbar, nichts unter der Herkunft
            Assert.True(shown <= column + 0.5, $"{text.Text}: {shown:0.0} px in {column:0.0} px");
            Assert.Equal(folder, text.ToolTip);
        });
    }

    [Fact]
    public void Frozen_hdr_moment_for_snapshots_shows_on_in_capsule_and_button_without_switching()
    {
        WpfTestHost.Run(() =>
        {
            var context = new TestLibraryContext(ManyGames(3));
            var (window, model, _) = Open(context);
            model.SelectedGame = model.Games[0];
            Assert.Equal(LibraryStrings.Hdr_TurnOn, model.HdrToggleText);

            // Bildschirmfotos des HDR-Moments (--snapshot): Kapsel und Knopf zeigen einheitlich „an“, geschaltet wird nichts.
            window.FreezeHdrMoment(0.35);
            WpfTestHost.DoEvents();

            Assert.False(model.IsHdrOn);
            Assert.True(model.ShowsHdrOn);
            Assert.Equal(LibraryStrings.Nits_HdrOn, model.HdrShortText);
            Assert.Equal(LibraryStrings.Hdr_TurnOff, model.HdrToggleText);
            Assert.Equal(LibraryStrings.Hdr_TurnOff, AutomationProperties.GetName(Named<Button>(window, "ToggleHdrButton")));
            Assert.Equal(LibraryStrings.Status_ActiveHdrOn, model.AutomationStateText); // Statuszeile passt zur Kapsel
            Assert.Equal(0, context.ToggleHdrCalls);
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
}
