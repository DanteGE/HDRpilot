using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Auswahl beim Filtern im Hauptfenster (Entscheidung des Nutzers 2026-10-19, G10.5), auf dem WPF-Testhost erzeugt und
/// angeordnet, nie angezeigt: Die Liste zeigt die Auswahl des ViewModels, ein zurückgekehrtes Spiel wird in den sichtbaren
/// Bereich geholt, und der Fokus geht nie verloren. Lag er auf der verschwundenen Zeile bzw. Kachel, folgt er der Auswahl
/// (ohne Treffer auf „Filter zurücksetzen“); in Suche und Leiste bleibt er. Das Testfenster ist nie aktiv: Geprüft wird
/// der Fokus, den es sich merkt und beim Aktivieren wiederherstellt (im aktiven Fenster ist das der Tastaturfokus). Läuft
/// allein wie die übrigen Fenster-Prüfungen der Bibliothek.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class SelectionFollowsFilterWindowTests
{
    private static readonly Size WindowSize = new(1745, 982);

    /// <summary>„Spiel 001“ bis „Spiel 060“; neu ist jedes fünfte (005, 010, …), „Spiel 001“ ist gewählt.</summary>
    private static (MainWindow Window, LibraryViewModel Model) Open(LibraryViewMode view = LibraryViewMode.List)
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext([.. Enumerable.Range(1, 60).Select(i => Game(i % 3 == 0 ? GameSource.Epic : GameSource.Steam, "g" + i, $"Spiel {i:000}", isNew: i % 5 == 0))]);
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games[0];
        var window = new MainWindow();
        window.Attach(model);
        Arrange(window);
        return (window, model);
    }

    private static void Arrange(MainWindow window)
    {
        WpfTestHost.Layout(window, WindowSize);
        window.FitDetailsColumn();
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, WindowSize);
        WpfTestHost.DoEvents();
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

    private static ListBoxItem Row(ListBox list, GameItemViewModel item) => Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(item));

    private static GameItemViewModel ByName(LibraryViewModel model, string name) => model.Games.Single(g => g.Name == name);

    [Fact]
    public void Focus_on_the_hidden_row_follows_the_selection_to_the_first_match_and_back()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model) = Open();
            var list = Named<ListBox>(window, "GameList");
            var first = ByName(model, "Spiel 001");
            FocusManager.SetFocusedElement(window, Row(list, first));

            // Alt+N: nur neue Spiele, „Spiel 001“ fällt heraus. Titelbereich, Details und Liste zeigen den ersten Treffer,
            // und der Fokus ist auf seiner Zeile statt auf der verschwundenen.
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Arrange(window);
            var match = ByName(model, "Spiel 005");
            Assert.Same(match, model.SelectedGame);
            Assert.Same(match, model.Hero?.Item);
            Assert.Same(match, list.SelectedItem);
            Assert.Same(Row(list, match), FocusManager.GetFocusedElement(window));

            // Alt+N wieder: „Spiel 001“ kehrt zurück, der Fokus mit ihm.
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Arrange(window);
            Assert.Same(first, model.SelectedGame);
            Assert.Same(first, list.SelectedItem);
            Assert.Same(Row(list, first), FocusManager.GetFocusedElement(window));
            window.Close();
        });
    }

    [Fact]
    public void Focus_stays_in_the_search_and_on_the_filter_button_and_the_returning_game_is_scrolled_into_view()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model) = Open();
            var list = Named<ListBox>(window, "GameList");
            var search = Named<TextBox>(window, "SearchBox");
            var filterNew = Named<FilterToggle>(window, "FilterNewButton");

            // Ein Spiel weit unten, nicht neu: noch keine Zeile dafür (virtualisiert).
            var far = ByName(model, "Spiel 048");
            model.SelectedGame = far;
            Arrange(window);
            Assert.Null(list.ItemContainerGenerator.ContainerFromItem(far));

            // Suche mit dem Fokus in der Suche: Die Auswahl springt auf den ersten Treffer, der Fokus bleibt.
            FocusManager.SetFocusedElement(window, search);
            model.SearchText = "Spiel 01";
            model.ApplySearch();
            Arrange(window);
            Assert.Same(ByName(model, "Spiel 001"), model.SelectedGame); // „01“ steckt auch in „001“
            Assert.Same(search, FocusManager.GetFocusedElement(window));

            // Filter „Neu“ per Knopf, der Fokus auf dem Knopf: Er bleibt dort.
            model.SearchText = string.Empty;
            model.ApplySearch();
            Arrange(window);
            Assert.Same(far, model.SelectedGame);
            FocusManager.SetFocusedElement(window, filterNew);
            filterNew.IsChecked = true;
            Arrange(window);
            Assert.True(model.OnlyNew);
            Assert.Same(ByName(model, "Spiel 005"), model.SelectedGame);
            Assert.Same(filterNew, FocusManager.GetFocusedElement(window));

            // Filter aus: „Spiel 048“ kehrt zurück und steht sichtbar in der Liste.
            filterNew.IsChecked = false;
            Arrange(window);
            Assert.Same(far, model.SelectedGame);
            var row = Row(list, far);
            var top = row.TranslatePoint(default, list).Y;
            Assert.InRange(top, 0, list.ActualHeight - row.ActualHeight);
            Assert.Same(filterNew, FocusManager.GetFocusedElement(window));
            window.Close();
        });
    }

    [Fact]
    public void Without_a_match_the_focus_goes_to_reset_filters_and_returns_with_the_game()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model) = Open();
            var list = Named<ListBox>(window, "GameList");
            var reset = Named<Button>(window, "ResetFiltersButton");
            var first = ByName(model, "Spiel 001");
            FocusManager.SetFocusedElement(window, Row(list, first));

            // Alt+H (HDR-fähig): Keins der Spiele ist es, kein Spiel gewählt, der Hinweis ist da und hat den Fokus.
            Assert.True(window.HandleKey(Key.H, ModifierKeys.Alt, null));
            Arrange(window);
            Assert.Null(model.SelectedGame);
            Assert.Null(list.SelectedItem);
            Assert.False(model.HasDetails);
            Assert.False(model.HasHero);
            Assert.Equal(Visibility.Visible, Named<FrameworkElement>(window, "NoMatch").Visibility);
            Assert.Same(reset, FocusManager.GetFocusedElement(window));

            // „Filter zurücksetzen“: „Spiel 001“ ist zurück, der Fokus auf seiner Zeile.
            reset.Command.Execute(null);
            Arrange(window);
            Assert.Same(first, model.SelectedGame);
            Assert.Equal(Visibility.Collapsed, Named<FrameworkElement>(window, "NoMatch").Visibility);
            Assert.Same(Row(list, first), FocusManager.GetFocusedElement(window));
            window.Close();
        });
    }

    [Fact]
    public void In_the_grid_the_focus_follows_from_the_hidden_tile_to_the_first_match()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model) = Open(LibraryViewMode.Grid);
            var grid = Named<ListBox>(window, "GameGrid");
            var first = ByName(model, "Spiel 001");
            Assert.Same(first, grid.SelectedItem);
            FocusManager.SetFocusedElement(window, Row(grid, first));

            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Arrange(window);
            var match = ByName(model, "Spiel 005");
            Assert.Same(match, model.SelectedGame);
            Assert.Same(match, grid.SelectedItem);
            Assert.Same(Row(grid, match), FocusManager.GetFocusedElement(window));

            // Wechsel der Ansicht: Auswahl bleibt, „Spiel 001“ kehrt in der Liste zurück.
            model.ViewMode = LibraryViewMode.List;
            Arrange(window);
            var list = Named<ListBox>(window, "GameList");
            Assert.Same(match, list.SelectedItem);
            Assert.True(window.HandleKey(Key.N, ModifierKeys.Alt, null));
            Arrange(window);
            Assert.Same(first, model.SelectedGame);
            Assert.Same(first, list.SelectedItem);
            window.Close();
        });
    }

    [Fact]
    public void In_the_shelf_the_automation_card_hides_without_a_selected_game()
    {
        // Regal, Suche ohne Treffer: Ohne Spiel stünden auf der Karte ein leerer Titel und Schalter, die umschalten, aber nichts
        // speichern. Sie verschwindet und kehrt mit dem Spiel zurück; in Liste und Raster gibt es sie nie.
        WpfTestHost.Run(() =>
        {
            var (window, model) = Open(LibraryViewMode.Shelf);
            var card = Named<FrameworkElement>(window, "AutomationCard");
            Assert.Equal(Visibility.Visible, card.Visibility);

            model.SearchText = "xqz";
            model.ApplySearch();
            Arrange(window);
            Assert.Null(model.SelectedGame);
            Assert.Equal(Visibility.Collapsed, card.Visibility);

            model.ClearFiltersCommand.Execute(null);
            Arrange(window);
            Assert.Same(ByName(model, "Spiel 001"), model.SelectedGame);
            Assert.Equal(Visibility.Visible, card.Visibility);

            model.ViewMode = LibraryViewMode.Grid;
            Arrange(window);
            Assert.Equal(Visibility.Collapsed, card.Visibility);
            model.SearchText = "xqz";
            model.ApplySearch();
            model.ViewMode = LibraryViewMode.Shelf;
            Arrange(window);
            Assert.Equal(Visibility.Collapsed, card.Visibility); // auch beim Wechsel ins Regal ohne Spiel
            window.Close();
        });
    }
}
