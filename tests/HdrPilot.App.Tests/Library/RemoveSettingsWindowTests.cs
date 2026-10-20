using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
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

            // Auf einem selbst hinzugefügten Spiel fragt Entf, ob es aus der Bibliothek soll; abgelehnt bleibt es.
            var own = ByName(model, "Eigenes Spiel");
            model.SelectedGame = own;
            Arrange(window);
            var asked = dialogs.Confirms.Count;
            dialogs.Answers.Enqueue(false);
            Assert.True(window.HandleKey(Key.Delete, ModifierKeys.None, list.ItemContainerGenerator.ContainerFromItem(own) as ListBoxItem));
            WaitFor(() => model.Details!.RemoveGameCommand.IsRunning);
            Assert.Equal(asked + 1, dialogs.Confirms.Count);
            Assert.Equal(LibraryStrings.RemoveGame_ConfirmTitle, dialogs.Confirms[^1].Title);
            Assert.NotNull(context.Config.FindGame(own.Key));

            // Auf einem installierten Spiel tut Entf nichts.
            var helldivers = ByName(model, "HELLDIVERS 2");
            model.SelectedGame = helldivers;
            Arrange(window);
            Assert.False(window.HandleKey(Key.Delete, ModifierKeys.None, list.ItemContainerGenerator.ContainerFromItem(helldivers) as ListBoxItem));
            Assert.Equal(2, dialogs.Requests.Count);
            Assert.Equal(asked + 1, dialogs.Confirms.Count);
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
            var dialog = new ConfirmDialog(RemoveSettingsText.ForGame(game, InstallTrace.MissingDrive("Q:"), 2));
            WpfTestHost.Layout(dialog, new Size(ConfirmDialog.WideWidth, 900));

            Assert.Equal(ConfirmDialog.WideWidth, dialog.Width);
            var details = (ItemsControl)dialog.FindName("DetailsList");
            Assert.Equal(["„HDR automatisch“ ist an", "2 gemerkte EXE\u2011Dateien"], details.Items.Cast<ConfirmDetail>().Select(d => d.Text));
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

    /// <summary>Ein Spiel, bei dem alles verloren ginge, was die Rückfrage nennen kann (elf Punkte), mit langem Namen.</summary>
    private static GameConfig EverythingGame()
    {
        var game = Game(GameSource.Steam, "990002001", "Aaa Antivirus Survivors 2003 Professional Demo: The Ultimate Collector's Edition with Extra Long Subtitle Remastered", hdrAuto: true, installed: false, installDir: @"Q:\SteamLibrary\steamapps\common\Antivirus");
        game.AutoHdr = true;
        game.Display = @"\\?\DISPLAY#TEST#1";
        game.RestoreAfter = false;
        game.DetectedExecutables = [@"Q:\a\x.exe", @"Q:\a\y.exe", @"Q:\a\z.exe"];
        game.ExtraExecutables = [@"Q:\a\e1.exe", @"Q:\a\e2.exe"];
        game.ExcludedExecutables = ["helper.exe"];
        game.CustomIcon = HdrPilot.Core.Games.CustomIcons.FileNameFor(game.Key);
        game.Artwork = new GameArtworkChoice { Hero = "custom:Steam_990002001-hero.jpg", Cover = "custom:Steam_990002001-cover.jpg", Logo = "alt:logo2.png" };
        game.LastPlayed = new DateTimeOffset(2026, 9, 12, 18, 15, 0, TimeSpan.Zero);
        return game;
    }

    /// <summary>18 nicht installierte Spiele mit langen Namen auf einem nicht verbundenen Laufwerk, jedes dritte mit „HDR automatisch“.</summary>
    private static ConfirmRequest EighteenGames()
    {
        var games = Enumerable.Range(1, 18).Select(i =>
        {
            var game = Game(GameSource.Steam, "99000" + i, $"Zusatzspiel Nummer {i:00} mit einem recht langen Namen zum Umbrechen", hdrAuto: i % 3 == 0, installed: false, installDir: @"R:\Games\Z" + i);
            game.DetectedExecutables = [@"R:\Games\Z" + i + @"\Spiel.exe"];
            return game;
        }).ToList();
        return RemoveSettingsText.ForGames(games, [.. games.Select(_ => InstallTrace.MissingDrive("R:"))], 3);
    }

    private static ScrollViewer DetailsScroll(ConfirmDialog dialog) => (ScrollViewer)dialog.FindName("DetailsScroll");

    /// <summary>Ordnet die Rückfrage in dieser Höhe an (wie das Fenster, das nicht höher als die Arbeitsfläche wird) und lässt die Bildlaufleiste nachziehen.</summary>
    private static FrameworkElement LayoutDialog(ConfirmDialog dialog, double height)
    {
        WpfTestHost.Layout(dialog, new Size(dialog.Width, height));
        dialog.UpdateLayout();
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(dialog, new Size(dialog.Width, height));
        WpfTestHost.DoEvents();
        return (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(dialog, 0);
    }

    [Fact]
    public void RemoveSettings_dialog_shows_every_loss_of_one_game_without_cutting_or_scrolling()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var dialog = new ConfirmDialog(RemoveSettingsText.ForGame(EverythingGame(), InstallTrace.MissingDrive("Q:"), 2));
            LayoutDialog(dialog, double.PositiveInfinity);

            var details = (ItemsControl)dialog.FindName("DetailsList");
            Assert.Equal(11, details.Items.Count);
            Assert.Contains(details.Items.Cast<ConfirmDetail>(), d => d.Text.StartsWith("Zuletzt gespielt am ", StringComparison.Ordinal));
            var scroll = DetailsScroll(dialog);
            Assert.True(scroll.ScrollableHeight < 0.5, "gescrollt: " + scroll.ScrollableHeight);
            Assert.False(scroll.Focusable || scroll.IsTabStop); // nichts zu scrollen: kein zusätzlicher Halt für die Tabulatortaste
            dialog.Close();
        });
    }

    [Fact]
    public void RemoveSettings_long_bulk_list_scrolls_by_keyboard_and_keeps_room_for_the_scroll_bar()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var dialog = new ConfirmDialog(EighteenGames());
            LayoutDialog(dialog, double.PositiveInfinity);

            var scroll = DetailsScroll(dialog);
            var list = (ItemsControl)dialog.FindName("DetailsList");
            Assert.Equal(ConfirmDialog.DetailsMaxHeight, scroll.ActualHeight, 1);
            Assert.True(scroll.ScrollableHeight > 100, "scrollt nicht: " + scroll.ScrollableHeight);
            Assert.True(scroll.Focusable && scroll.IsTabStop, $"per Tastatur nicht erreichbar: Focusable={scroll.Focusable} IsTabStop={scroll.IsTabStop}");
            Assert.True(ConfirmDialog.SwallowsInDetails(Key.Enter)); // Enter in der Liste löst nicht „Entfernen“ aus
            Assert.False(ConfirmDialog.SwallowsInDetails(Key.Down)); // die Pfeiltasten scrollen
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)dialog.FindName("DetailsFocus")).Visibility); // Fokusrahmen erst mit dem Fokus

            // Rechts bleibt Platz für die Bildlaufleiste, die im Fluent-Stil der App über dem Inhalt liegt (dort reicht der
            // sichtbare Bereich bis zum Rand): Der Text endet mindestens 14 px vor dem Rand des sichtbaren Bereichs.
            Assert.True(scroll.ViewportWidth - list.ActualWidth >= 14, "Abstand zur Bildlaufleiste: " + (scroll.ViewportWidth - list.ActualWidth));

            // Punkte des Sammel-Entfernens: der Name, darunter gedämpft, was verloren geht.
            var third = list.Items.Cast<ConfirmDetail>().ElementAt(2);
            Assert.Equal("Zusatzspiel Nummer 03 mit einem recht langen Namen zum Umbrechen", third.Text);
            Assert.Equal("Laufwerk R: nicht verbunden · „HDR automatisch“ ist an · 1 gemerkte EXE\u2011Datei", third.Note);
            dialog.Close();
        });
    }

    [Fact]
    public void RemoveSettings_enter_in_the_scrolling_list_does_not_confirm()
    {
        // E90: Wer die lange Liste mit der Tastatur liest, löst mit Enter nicht „Entfernen“ aus. Geprüft wird die Verdrahtung im
        // Fenster (KeyDown der Aufzählung), nicht nur die Regel: Die Taste ist erledigt, bevor sie die Standardschaltfläche
        // erreicht, auch wenn sie von einem Punkt der Liste aufsteigt; die Rückfrage bleibt unbeantwortet.
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var dialog = new ConfirmDialog(EighteenGames());
            LayoutDialog(dialog, double.PositiveInfinity);
            var scroll = DetailsScroll(dialog);
            Assert.True(scroll.Focusable && scroll.IsTabStop);
            KeyEventArgs Press(UIElement target, Key key)
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource(), Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent };
                target.RaiseEvent(args);
                return args;
            }

            Assert.True(Press(scroll, Key.Enter).Handled);
            var line = WpfTestHost.Visuals(scroll).OfType<TextBlock>().First(t => t.Text.StartsWith("Zusatzspiel", StringComparison.Ordinal));
            Assert.True(Press(line, Key.Enter).Handled);
            Assert.Null(dialog.DialogResult);
            Assert.Equal(new ConfirmAnswer(false, false), dialog.Answer);

            // Außerhalb der Liste gehört Enter weiter der Standardschaltfläche „Entfernen“ (nicht verschluckt).
            Assert.False(Press((UIElement)dialog.FindName("MessageText"), Key.Enter).Handled);
            Assert.True(((Button)dialog.FindName("ConfirmButton")).IsDefault);
            dialog.Close();
        });
    }

    /// <summary>Tastenereignisse brauchen eine Quelle; die Rückfrage wird in Tests nie angezeigt.</summary>
    private sealed class KeySource : PresentationSource
    {
        private System.Windows.Media.Visual? _root;

        public override System.Windows.Media.Visual? RootVisual
        {
            get => _root;
            set => _root = value;
        }

        public override bool IsDisposed => false;

        protected override System.Windows.Media.CompositionTarget? GetCompositionTargetCore() => null;
    }

    [Fact]
    public void RemoveSettings_dialog_never_grows_beyond_the_work_area_and_keeps_its_buttons_visible()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            foreach (var request in new[] { RemoveSettingsText.ForGame(EverythingGame(), InstallTrace.MissingDrive("Q:"), 2), EighteenGames() })
            {
                var dialog = new ConfirmDialog(request);
                Assert.True(dialog.MaxHeight <= SystemParameters.WorkArea.Height, "höher als die Arbeitsfläche: " + dialog.MaxHeight);

                // Etwa 1280×800 bei 125 % ohne Taskleiste: Die Aufzählung gibt nach, die Knöpfe stehen ganz im Fenster.
                const double Height = 560;
                var root = LayoutDialog(dialog, Height);
                foreach (var name in new[] { "ConfirmButton", "CancelButton" })
                {
                    var button = (FrameworkElement)dialog.FindName(name);
                    var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), root).Y;
                    Assert.InRange(bottom, button.ActualHeight, Height);
                }

                var scroll = DetailsScroll(dialog);
                Assert.True(scroll.ScrollableHeight > 0 && scroll.Focusable && scroll.IsTabStop, "Aufzählung gibt nicht nach");
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RemoveSettings_dialog_tells_screen_readers_the_message_the_warning_and_the_losses()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var game = Game(GameSource.Steam, "1304930", "The Outlast Trials", hdrAuto: true, installed: false);
            var request = RemoveSettingsText.ForGame(game, new InstallTrace(InstallSign.Program), 0);
            var dialog = new ConfirmDialog(request);

            var help = AutomationProperties.GetHelpText(dialog);
            Assert.Equal(request.Message + " " + LibraryStrings.RemoveSettings_ProgramPresent + " " + LibraryStrings.RemoveSettings_LossHeader + ": „HDR automatisch“ ist an", help);
            Assert.Equal(help, AutomationProperties.GetHelpText((DependencyObject)dialog.FindName("CancelButton"))); // hier liegt der Fokus
            Assert.Equal(LibraryStrings.RemoveSettings_LossHeader, AutomationProperties.GetName(DetailsScroll(dialog)));
            dialog.Close();
        });
    }

    [Fact]
    public void RemoveUninstalled_button_in_the_settings_explains_itself_to_screen_readers()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new HdrPilot.Tests.TestSupport.InMemoryFileSystem());
            var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new HdrPilot.App.Views.Shell.SettingsWindow();
            window.Attach(new HdrPilot.App.ViewModels.Shell.SettingsViewModel(context, new FakeFolderPicker()));
            WpfTestHost.Layout(window, new Size(1280, 860));

            var button = WpfTestHost.Visuals(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "RemoveUninstalledButton");
            Assert.Equal(ShellStrings.RemoveUninstalledHint, AutomationProperties.GetHelpText(button));
            window.Close();
        });
    }
}
