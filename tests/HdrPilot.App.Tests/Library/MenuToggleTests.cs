using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Menüknöpfe schließen ihr offenes Menü wieder (Wunsch des Nutzers 2026-10-22): ein zweiter Klick, die Eingabe- oder
/// Leertaste und Auslösen per UI-Automation bei offenem Menü schließen es, statt es neu zu öffnen; Esc und ein Klick
/// daneben schließen wie bisher, der Fokus bleibt auf dem Knopf. Gilt für „···“ und die Symbol-Kachel im Detailbereich und
/// für den Titel (Quellenmenü).
/// <para>
/// In einem echten Testfenster (Entscheidung des Nutzers 2026-10-28): klein, oben links auf der Arbeitsfläche, angezeigt,
/// aber nicht aktiviert und ohne Taskleiste. Erst mit einer PresentationSource geht das Menü wirklich auf (sonst setzt WPF
/// IsOpen ohne Opened/Closed zurück), hält die Maus wie in der App und bekommt den Tastaturfokus des Testthreads, auch ohne
/// Vordergrund (das Fenster wird nie aktiviert). Echte Eingaben gibt es nicht: Ein Klick kommt über Auslösen (dasselbe
/// OnClick wie beim Loslassen), Drücken, Loslassen und Tasten als dieselben Routed Events in der Reihenfolge, in der das
/// Eingabesystem sie schickt. Die Menüs haben keinen Treffertest (ein Zeiger über dem Menü wählt nichts). Läuft allein: Maus
/// und Tastaturfokus gelten für den ganzen Testprozess.
/// </para>
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class MenuToggleTests
{
    private static MenuButton Standalone(bool menuTakesFocus = true) => new()
    {
        Content = "Menü",
        Width = 80,
        Height = 28,
        ContextMenu = new ContextMenu { Focusable = menuTakesFocus, Items = { new MenuItem { Header = "a" }, new MenuItem { Header = "b" } } },
    };

    private static AutomationPeer Peer(MenuButton button) => UIElementAutomationPeer.CreatePeerForElement(button);

    private static ExpandCollapseState State(MenuButton button) =>
        ((IExpandCollapseProvider)Peer(button).GetPattern(PatternInterface.ExpandCollapse)).ExpandCollapseState;

    /// <summary>Auslösen per UI-Automation: wie ein Klick (ButtonBase.OnClick), nach dem nächsten Durchlauf der Eingaben.</summary>
    private static void Invoke(MenuButton button)
    {
        ((IInvokeProvider)Peer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        WpfTestHost.DoEvents();
    }

    /// <summary>
    /// Linke Maustaste auf dem Knopf drücken, wie es das Eingabesystem meldet: Hält ein offenes Menü die Maus, bekommt es das
    /// Drücken zuerst als PreviewMouseDownOutsideCapturedElement (es geht zu und gibt die Maus frei), dann geht es mit demselben
    /// Zeitstempel als PreviewMouseDown und MouseDown an das Element darunter, den Knopf.
    /// </summary>
    private static void PressOnButton(MenuButton button)
    {
        var timestamp = Environment.TickCount;
        if (Mouse.Captured is UIElement captured)
        {
            captured.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownOutsideCapturedElementEvent });
        }

        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
    }

    /// <summary>
    /// Drücken außerhalb des offenen Menüs, aber nicht auf dem Knopf (etwa in der Liste): Nur das Menü, das die Maus hält,
    /// bekommt es gemeldet. Es liegt vor dem nächsten Drücken auf dem Knopf (älterer Zeitstempel).
    /// </summary>
    private static void PressElsewhere()
    {
        var captured = Assert.IsAssignableFrom<UIElement>(Mouse.Captured);
        captured.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount - 100, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownOutsideCapturedElementEvent });
    }

    private static void ReleaseOnButton(MenuButton button)
    {
        var timestamp = Environment.TickCount;
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        WpfTestHost.DoEvents();
    }

    /// <summary>
    /// Taste wie vom Eingabesystem an das Element mit dem Tastaturfokus: PreviewKeyDown (tunnelnd), unerledigt KeyDown, dann
    /// KeyUp an das Element, das danach den Fokus hat. Ohne Durchlauf der Warteschlange: „gleich danach“ bleibt gleich danach.
    /// </summary>
    private static void Type(Key key)
    {
        var target = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(down);
        if (!down.Handled)
        {
            down.RoutedEvent = Keyboard.KeyDownEvent;
            target.RaiseEvent(down);
        }

        var focused = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        var up = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(focused)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent };
        focused.RaiseEvent(up);
        if (!up.Handled)
        {
            up.RoutedEvent = Keyboard.KeyUpEvent;
            focused.RaiseEvent(up);
        }
    }

    private static List<MenuItem> Entries(ContextMenu menu) =>
        [.. menu.Items.Cast<object>().Select(i => i as MenuItem ?? menu.ItemContainerGenerator.ContainerFromItem(i) as MenuItem).OfType<MenuItem>()];

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    /// <summary>Das echte Testfenster mit einem Menüknopf (siehe Klasse); räumt Menü und Fenster in jedem Fall auf.</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window _window;

        public Host(MenuButton button, object? dataContext = null)
        {
            Button = button;
            Menu = button.ContextMenu!;
            Menu.IsHitTestVisible = false;
            Menu.Opened += (_, _) => Opened++;
            var area = SystemParameters.WorkArea;
            _window = new Window
            {
                Title = "HdrPilot.Tests.MenuToggleHost",
                Content = button,
                DataContext = dataContext,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                SizeToContent = SizeToContent.WidthAndHeight,
                Left = area.Left,
                Top = area.Top,
            };
            try
            {
                _window.Show();
                WpfTestHost.DoEvents();
                Assert.NotNull(PresentationSource.FromVisual(button));
                Assert.True(button.Focus(), "Knopf bekommt den Tastaturfokus nicht");
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        public MenuButton Button { get; }

        public ContextMenu Menu { get; }

        /// <summary>Wie oft das Menü aufging (ein zweites Opened hieße: wieder geöffnet, das Flackern von früher).</summary>
        public int Opened { get; private set; }

        /// <summary>
        /// Bis der Knopf den Zustand des Menüs meldet: Geht das Menü zu, ist sein IsOpen sofort aus; Closed und damit
        /// IsMenuOpen und Aus-/Einklappen folgen, wenn Windows es ausgeblendet hat (Zeitgeber des Popups).
        /// </summary>
        public void Settle()
        {
            WaitForTheButton();
            Assert.Equal(Menu.IsOpen, Button.IsMenuOpen);
        }

        private void WaitForTheButton()
        {
            WpfTestHost.DoEvents();
            var until = Environment.TickCount64 + 3000;
            while (Button.IsMenuOpen != Menu.IsOpen && Environment.TickCount64 < until)
            {
                Thread.Sleep(10);
                WpfTestHost.DoEvents();
            }
        }

        /// <summary>Geöffnet wie mit der Maus: Das Menü hält die Maus und den Tastaturfokus, kein Eintrag ist gewählt.</summary>
        public void AssertOpenByPointer(int opened)
        {
            Assert.True(Menu.IsOpen);
            Assert.True(Button.IsMenuOpen);
            Assert.Equal(ExpandCollapseState.Expanded, State(Button));
            Assert.Equal(opened, Opened);
            Assert.Same(Menu, Mouse.Captured);
            Assert.Same(Menu, Keyboard.FocusedElement);
            Assert.DoesNotContain(Entries(Menu), i => i.IsHighlighted);
        }

        /// <summary>
        /// Zu, ohne neu aufzugehen; ist es ausgeblendet, meldet Aus-/Einklappen „zugeklappt“ und der Fokus steht auf dem Knopf
        /// (schließt das Programm das Menü, gibt WPF ihn erst mit Closed zurück).
        /// </summary>
        public void AssertClosed(int opened)
        {
            Assert.False(Menu.IsOpen);
            Assert.Equal(opened, Opened);
            Settle();
            Assert.False(Button.IsMenuOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, State(Button));
            Assert.Equal(opened, Opened);
            Assert.True(Button.IsKeyboardFocused, $"Fokus auf {Keyboard.FocusedElement?.GetType().Name ?? "nichts"} statt auf dem Knopf");
        }

        public void Dispose()
        {
            try
            {
                Menu.IsOpen = false;
                WaitForTheButton();
            }
            finally
            {
                _window.Close();
                WpfTestHost.DoEvents();
            }
        }
    }

    [Fact]
    public void Invoking_the_button_opens_and_invoking_it_again_closes_the_menu()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);

            Invoke(host.Button); // zweites Auslösen bei offenem Menü: zu, nicht neu auf
            host.AssertClosed(opened: 1);

            Invoke(host.Button); // danach öffnet es wieder
            host.AssertOpenByPointer(opened: 2);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Second_mouse_click_closes_the_menu_and_does_not_reopen_it(bool releasedAfterTheMenuIsGone)
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);

            // Drücken auf dem Knopf bei offenem Menü: Das Menü geht dabei zu, der Klick (beim Loslassen) kommt danach an,
            // schnell (Windows blendet das Menü noch aus) oder erst, wenn es weg ist.
            PressOnButton(host.Button);
            Assert.False(host.Menu.IsOpen);
            if (releasedAfterTheMenuIsGone)
            {
                host.Settle();
            }

            Invoke(host.Button);
            host.AssertClosed(opened: 1);

            // Nach dem Loslassen gilt die Sperre nicht mehr: der nächste Klick öffnet.
            ReleaseOnButton(host.Button);
            PressOnButton(host.Button);
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 2);
        });
    }

    [Fact]
    public void A_click_while_the_menu_is_closed_opens_it()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            PressOnButton(host.Button);
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);
        });
    }

    [Fact]
    public void A_key_after_a_press_released_elsewhere_opens_the_menu_again()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());

            // Drücken bei offenem Menü schließt es; losgelassen wird außerhalb (kein Klick, kein Loslassen am Knopf).
            Invoke(host.Button);
            PressOnButton(host.Button);
            host.Settle();
            Assert.False(host.Menu.IsOpen);

            // Eine Taste beginnt eine neue Bedienung: Auslösen danach öffnet das Menü.
            Type(Key.LeftCtrl);
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 2);
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void Enter_or_space_closes_the_open_menu_while_no_entry_is_highlighted(Key key)
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);

            // Die Tastatur geht beim Öffnen ans Menü; ohne gewählten Eintrag wirkt die Taste wie ein zweiter Klick.
            Type(key);
            host.AssertClosed(opened: 1);

            // Noch einmal: Auf dem Knopf öffnet dieselbe Taste wieder, die nächste im Menü schließt wieder.
            Type(key);
            host.AssertOpenByPointer(opened: 2);
            Type(key);
            host.AssertClosed(opened: 2);
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void Enter_or_space_on_the_button_closes_a_menu_that_left_the_focus_there(Key key)
    {
        WpfTestHost.Run(() =>
        {
            // Ein Menü, das den Fokus nicht nimmt: Die Taste kommt beim Knopf an und schließt wie ein zweiter Klick (die
            // Leertaste fängt dabei nicht die Maus, das schlösse das Menü und das Loslassen öffnete es wieder).
            using var host = new Host(Standalone(menuTakesFocus: false));
            Invoke(host.Button);
            Assert.True(host.Menu.IsOpen);
            Assert.True(host.Button.IsKeyboardFocused);

            Type(key);
            host.AssertClosed(opened: 1);
        });
    }

    [Fact]
    public void Enter_on_a_highlighted_entry_still_belongs_to_the_entry()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            var clicked = 0;
            var entry = (MenuItem)host.Menu.Items[0];
            entry.Click += (_, _) => clicked++;

            // F4 öffnet mit der Tastatur: Der Fokus geht auf den ersten Eintrag, Eingabe löst ihn aus und schließt.
            Type(Key.F4);
            WpfTestHost.DoEvents();
            Assert.True(host.Menu.IsOpen);
            Assert.True(entry.IsKeyboardFocused);
            Assert.True(entry.IsHighlighted);
            Type(Key.Enter);
            Assert.False(host.Menu.IsOpen);
            WpfTestHost.DoEvents(); // MenuItem meldet Click erst nach dem Zeichnen
            Assert.Equal(1, clicked);
            host.AssertClosed(opened: 1);
        });
    }

    [Fact]
    public void Escape_still_closes_and_the_next_click_opens()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);
            Type(Key.Escape);
            host.AssertClosed(opened: 1);
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 2);
        });
    }

    /// <summary>
    /// Ein Klick daneben schließt das Menü wie bisher; der nächste Klick auf den Knopf öffnet es wieder, auch gleich danach,
    /// solange Windows das Menü noch ausblendet.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_click_elsewhere_closes_the_menu_and_the_next_click_on_the_button_opens_it(bool rightAway)
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);

            PressElsewhere();
            Assert.False(host.Menu.IsOpen);
            if (!rightAway)
            {
                host.AssertClosed(opened: 1);
            }

            PressOnButton(host.Button);
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 2);
        });
    }

    /// <summary>
    /// Gleich nach dem Schließen, solange Windows das Menü noch ausblendet (Closed steht aus, IsMenuOpen ist noch an): Ein
    /// Klick, die Eingabetaste auf dem Knopf oder Auslösen öffnen es wieder, statt ein schon geschlossenes Menü zu „schließen“.
    /// </summary>
    [Theory]
    [InlineData("Klick")]
    [InlineData("Eingabetaste")]
    [InlineData("Auslösen")]
    public void Right_after_escape_a_click_a_key_or_invoking_opens_the_menu_again(string how)
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            Invoke(host.Button);
            host.AssertOpenByPointer(opened: 1);
            Type(Key.Escape);
            Assert.False(host.Menu.IsOpen);
            Assert.True(host.Button.IsKeyboardFocused);

            switch (how)
            {
                case "Klick":
                    PressOnButton(host.Button);
                    Invoke(host.Button);
                    break;
                case "Eingabetaste":
                    Type(Key.Enter);
                    break;
                default:
                    Invoke(host.Button);
                    break;
            }

            Assert.True(host.Menu.IsOpen, $"{how} gleich nach Esc öffnet das Menü nicht");
            host.Settle();
            host.AssertOpenByPointer(opened: 2);
            Type(Key.Escape);
            host.AssertClosed(opened: 2);
        });
    }

    [Fact]
    public void Expand_and_collapse_open_and_close_the_menu_and_report_its_state()
    {
        WpfTestHost.Run(() =>
        {
            using var host = new Host(Standalone());
            var expand = (IExpandCollapseProvider)Peer(host.Button).GetPattern(PatternInterface.ExpandCollapse);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);

            // Aufklappen öffnet wie die Tastatur: Fokus auf dem ersten Eintrag.
            expand.Expand();
            WpfTestHost.DoEvents();
            Assert.True(host.Menu.IsOpen);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            Assert.True(((MenuItem)host.Menu.Items[0]).IsKeyboardFocused);

            expand.Collapse();
            host.AssertClosed(opened: 1);
        });
    }

    /// <summary>
    /// Die echten Menüs der drei Knöpfe (aus dem Hauptfenster, dort je Verwendung eine Instanz) an einem Knopf mit derselben
    /// Vorlage: Klick öffnet, ein zweiter Klick schließt ohne Wiederöffnen, Eingabetaste ohne gewählten Eintrag und Esc
    /// schließen, der Fokus bleibt auf dem Knopf.
    /// </summary>
    [Theory]
    [InlineData("ProfileMenuButton", "GameContextMenu")]
    [InlineData("DetailIconButton", "GameIconMenu")]
    [InlineData("SourceTitleButton", "SourceMenu")]
    public void The_real_menus_of_the_detail_pane_and_the_title_toggle_the_same_way(string name, string menuKey)
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Dredge", isNew: false), Game(GameSource.Gog, "2", "Gothic", isNew: false));
            var model = new LibraryViewModel(context, new FakeDialogs());
            model.SelectedGame = model.Games.First();
            var window = new MainWindow();
            try
            {
                window.Attach(model);
                WpfTestHost.Layout(window, new Size(1600, 1000));
                var real = Assert.IsType<MenuButton>(window.FindName(name));
                var menu = Assert.IsType<ContextMenu>(window.FindResource(menuKey));
                Assert.NotSame(real.ContextMenu, menu);
                Assert.Equal(menuKey, AutomationProperties.GetAutomationId(menu));
                var button = new MenuButton
                {
                    Style = real.Style,
                    Width = real.Width,
                    Height = real.Height,
                    Padding = real.Padding,
                    Tag = real.Tag,
                    Content = real.Content is UIElement ? "···" : real.Content,
                    ContentTemplate = real.ContentTemplate,
                    ContextMenu = menu,
                };

                using var host = new Host(button, model);
                Invoke(button);
                host.AssertOpenByPointer(opened: 1);
                Assert.Same(model, menu.DataContext);
                Assert.Contains(Entries(menu), i => i.IsEnabled && i.IsVisible);

                PressOnButton(button);
                Invoke(button);
                host.AssertClosed(opened: 1);
                ReleaseOnButton(button);

                Invoke(button);
                host.AssertOpenByPointer(opened: 2);
                Type(Key.Enter);
                host.AssertClosed(opened: 2);

                Invoke(button);
                host.AssertOpenByPointer(opened: 3);
                Type(Key.Escape);
                host.AssertClosed(opened: 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Detail_pane_and_title_menus_are_menu_buttons_with_their_menus()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Dredge", isNew: false));
            var model = new LibraryViewModel(context, new FakeDialogs());
            model.SelectedGame = model.Games.Single();
            var window = new MainWindow();
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(1600, 1000));
            WpfTestHost.DoEvents();

            foreach (var (name, menuId) in new[] { ("ProfileMenuButton", "GameContextMenu"), ("DetailIconButton", "GameIconMenu"), ("SourceTitleButton", "SourceMenu") })
            {
                var button = Assert.IsType<MenuButton>(window.FindName(name));
                Assert.Equal(menuId, AutomationProperties.GetAutomationId(button.ContextMenu!));
                var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                Assert.NotNull(peer.GetPattern(PatternInterface.ExpandCollapse));
                Assert.NotNull(peer.GetPattern(PatternInterface.Invoke));
            }

            window.Close();
        });
    }
}
