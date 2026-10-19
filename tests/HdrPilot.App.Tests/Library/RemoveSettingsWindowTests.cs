using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Einstellungen entfernen…“ im Hauptfenster und die Rückfrage (E90, G11.1/G11.2), auf dem WPF-Testhost erzeugt und
/// angeordnet, nie angezeigt: Knopf ganz unten und Eintrag im Kontextmenü nur bei nicht installierten Launcher-Spielen, Entf
/// fragt erst, nach dem Entfernen folgt der Fokus dem Nachbarn; die Rückfrage zeigt Aufzählung, Warnung und Häkchen und
/// hat den Fokus auf „Abbrechen“. Läuft allein wie die übrigen Fenster-Prüfungen der Bibliothek.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class RemoveSettingsWindowTests
{
    private static readonly Size WindowSize = new(1745, 982);

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context, FakeDialogs Dialogs) Open()
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext(
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
            Game(GameSource.Manual, "abc", "Eigenes Spiel", isNew: false, installed: false),
            Game(GameSource.Steam, "553850", "HELLDIVERS 2", isNew: false),
            Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, isNew: false, installed: false, installDir: @"Q:\SteamLibrary\steamapps\common\Nachtprobe"),
            Game(GameSource.Steam, "2082470", "VEIN Demo", isNew: false, installed: false));
        var dialogs = new FakeDialogs();
        var model = new LibraryViewModel(context, dialogs);
        var window = new MainWindow();
        window.Attach(model);
        Arrange(window);
        return (window, model, context, dialogs);
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

    private static GameItemViewModel ByName(LibraryViewModel model, string name) => model.Games.Single(g => g.Name == name);

    /// <summary>Knöpfe ganz unten im Detailbereich nach Kennung (Visibility, wie sie das Fenster gerade zeigt).</summary>
    private static Button Bottom(MainWindow window, string automationId) =>
        WpfTestHost.Visuals((FrameworkElement)window.FindName("DetailsContent")).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == automationId);

    private static MenuItem MenuEntry(MainWindow window, LibraryViewModel model, string automationId)
    {
        var list = (ListBox)window.FindName("GameList");
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(model.SelectedGame));
        var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
        menu.PlacementTarget = row;
        WpfTestHost.DoEvents();
        return menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == automationId);
    }

    /// <summary>Wartet, bis der asynchrone Befehl fertig ist (Suche der Verknüpfungen und Prüfung des Laufwerks im Hintergrund).</summary>
    private static void WaitFor(AsyncRelayCommandProbe probe)
    {
        for (var i = 0; i < 500 && probe(); i++)
        {
            Thread.Sleep(10);
            WpfTestHost.DoEvents();
        }

        Assert.False(probe(), "Befehl läuft noch");
    }

    private delegate bool AsyncRelayCommandProbe();

    [Fact]
    public void RemoveSettings_button_and_menu_entry_appear_only_for_not_installed_launcher_games()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _, _) = Open();
            foreach (var (name, removable, manual) in new[] { ("The Outlast Trials", true, false), ("Eigenes Spiel", false, true), ("HELLDIVERS 2", false, false) })
            {
                model.SelectedGame = ByName(model, name);
                Arrange(window);
                Assert.Equal(removable ? Visibility.Visible : Visibility.Collapsed, Bottom(window, "RemoveSettingsButton").Visibility);
                Assert.Equal(manual ? Visibility.Visible : Visibility.Collapsed, Bottom(window, "RemoveGameButton").Visibility);
                var entry = MenuEntry(window, model, "ContextRemoveSettings");
                Assert.Equal(removable ? Visibility.Visible : Visibility.Collapsed, entry.Visibility);
                Assert.Same(model.RemoveSettingsCommand, entry.Command);
            }

            model.SelectedGame = ByName(model, "The Outlast Trials");
            Arrange(window);
            var button = Bottom(window, "RemoveSettingsButton");
            Assert.Same(model.Details!.RemoveSettingsCommand, button.Command);
            Assert.Equal(LibraryStrings.RemoveSettings_Button, AutomationProperties.GetName(button));
            Assert.Equal(LibraryStrings.Key_Delete, MenuEntry(window, model, "ContextRemoveSettings").InputGestureText);
            window.Close();
        });
    }

    [Fact]
    public void RemoveSettings_delete_key_asks_first_and_the_focus_follows_the_neighbour()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, context, dialogs) = Open();
            var list = (ListBox)window.FindName("GameList");
            var outlast = ByName(model, "The Outlast Trials");
            model.SelectedGame = outlast;
            Arrange(window);
            var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(outlast));
            FocusManager.SetFocusedElement(window, row);

            // Entf ohne Bestätigung: nichts geschieht.
            dialogs.Answers.Enqueue(false);
            Assert.True(window.HandleKey(Key.Delete, ModifierKeys.None, row));
            WaitFor(() => model.RemoveSettingsCommand.IsRunning);
            Assert.Single(dialogs.Requests);
            Assert.NotNull(context.Config.FindGame(outlast.Key));

            // Entf und „Entfernen“: Das Spiel ist weg, der Nachbar gewählt, und der Fokus ging mit seiner Zeile mit.
            dialogs.Answers.Enqueue(true);
            Assert.True(window.HandleKey(Key.Delete, ModifierKeys.None, row));
            WaitFor(() => model.RemoveSettingsCommand.IsRunning);
            Arrange(window);
            Assert.Null(context.Config.FindGame(outlast.Key));
            var vein = ByName(model, "VEIN Demo");
            Assert.Same(vein, model.SelectedGame);
            Assert.Same(vein, list.SelectedItem);
            Assert.Same(list.ItemContainerGenerator.ContainerFromItem(vein), FocusManager.GetFocusedElement(window));

            // Auf einem installierten Spiel tut Entf nichts.
            var helldivers = ByName(model, "HELLDIVERS 2");
            model.SelectedGame = helldivers;
            Arrange(window);
            Assert.False(window.HandleKey(Key.Delete, ModifierKeys.None, list.ItemContainerGenerator.ContainerFromItem(helldivers) as ListBoxItem));
            Assert.Equal(2, dialogs.Requests.Count);
            window.Close();
        });
    }

    [Fact]
    public void RemoveSettings_entry_fits_into_the_context_menu_preview_of_a_low_row()
    {
        // Bildschirmfoto des Kontextmenüs (E90): Das Menü eines tief stehenden nicht installierten Spiels wird so gesetzt,
        // dass auch sein letzter Eintrag „Einstellungen entfernen…“ ganz im Fenster liegt (gemessen mit übernommenen Bindungen).
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var context = new TestLibraryContext([.. Enumerable.Range(1, 30).Select(i => Game(GameSource.Steam, "g" + i, $"Spiel {i:00}", isNew: false, installed: i != 10))]);
            var model = new LibraryViewModel(context, new FakeDialogs());
            var window = new MainWindow();
            window.Attach(model);
            Arrange(window);
            model.SelectedGame = ByName(model, "Spiel 10");
            Arrange(window);

            var overlay = Assert.IsType<HdrPilot.App.Infrastructure.SnapshotOverlay>(window.CreateContextMenuPreview());
            var menu = Assert.IsType<ContextMenu>(overlay.Element);
            WpfTestHost.DoEvents();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var entry = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "ContextRemoveSettings");
            Assert.Equal(Visibility.Visible, entry.Visibility);
            var root = (FrameworkElement)window.FindName("Root");
            Assert.InRange(overlay.Position.Y + menu.DesiredSize.Height, 0, root.ActualHeight);
            window.Close();
        });
    }

    [Fact]
    public void RemoveSettings_dialog_shows_losses_warning_and_checkbox_with_the_focus_on_cancel()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var game = Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, installed: false, installDir: @"Q:\Spiele\Outlast");
            game.DetectedExecutables = [@"Q:\Spiele\Outlast\a.exe", @"Q:\Spiele\Outlast\b.exe"];
            var dialog = new ConfirmDialog(RemoveSettingsText.ForGame(game, "Q:", 2));
            WpfTestHost.Layout(dialog, new Size(ConfirmDialog.WideWidth, 900));

            Assert.Equal(ConfirmDialog.WideWidth, dialog.Width);
            var details = (ItemsControl)dialog.FindName("DetailsList");
            Assert.Equal(["„HDR automatisch“ ist an", "2 gemerkte EXE-Dateien"], details.Items.Cast<string>());
            Assert.Equal(Visibility.Visible, ((FrameworkElement)dialog.FindName("WarningArea")).Visibility);
            Assert.Equal("Laufwerk Q: ist nicht verbunden. Das Spiel ist vielleicht noch installiert.", ((TextBlock)dialog.FindName("WarningText")).Text);
            var option = (CheckBox)dialog.FindName("OptionBox");
            Assert.Equal(Visibility.Visible, option.Visibility);
            Assert.False(option.IsChecked);
            Assert.Equal("Auch die 2 HDRpilot-Verknüpfungen löschen", AutomationProperties.GetName(option));
            Assert.Same(dialog.FindName("CancelButton"), FocusManager.GetFocusedElement(dialog));
            Assert.Equal(new ConfirmAnswer(false, false), dialog.Answer); // nicht bestätigt: auch kein Häkchen
            dialog.Close();

            // Die einfache Rückfrage (etwa „Spiel entfernen?“) bleibt wie bisher: ohne Aufzählung, Warnung und Häkchen.
            var simple = new ConfirmDialog(LibraryStrings.RemoveGame_ConfirmTitle, "Text", LibraryStrings.RemoveGame_Confirm, isDanger: true);
            WpfTestHost.Layout(simple, new Size(480, 600));
            Assert.Equal(480, simple.Width);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)simple.FindName("DetailsArea")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)simple.FindName("WarningArea")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)simple.FindName("OptionBox")).Visibility);
            Assert.Same(simple.FindName("CancelButton"), FocusManager.GetFocusedElement(simple));
            simple.Close();
        });
    }
}
