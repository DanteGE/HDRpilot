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
/// daneben schließen wie bisher. Gilt für „···“ und die Symbol-Kachel im Detailbereich und für den Titel (Quellenmenü).
/// Das Menü ist ein echtes Popup ohne Treffertest (ein bewegter Zeiger wählt nichts), das Fenster wird nie gezeigt.
/// </summary>
public sealed class MenuToggleTests
{
    private static (MenuButton Button, ContextMenu Menu) Standalone()
    {
        var menu = new ContextMenu { IsHitTestVisible = false, Items = { new MenuItem { Header = "a" }, new MenuItem { Header = "b" } } };
        var button = new MenuButton { Content = "Menü", ContextMenu = menu };
        return (button, menu);
    }

    private static void Invoke(MenuButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        WpfTestHost.DoEvents();
    }

    private static ExpandCollapseState State(MenuButton button) =>
        ((IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.ExpandCollapse)).ExpandCollapseState;

    private static void Raise(UIElement target, RoutedEvent routedEvent) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent, Source = target });

    private static void Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = target,
        };
        target.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = Keyboard.KeyDownEvent;
            target.RaiseEvent(args);
        }

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

    private static void Close(ContextMenu menu)
    {
        menu.IsOpen = false;
        WpfTestHost.DoEvents();
    }

    [Fact]
    public void Invoking_the_button_opens_and_invoking_it_again_closes_the_menu()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                Invoke(button);
                Assert.True(menu.IsOpen);
                Assert.True(button.IsMenuOpen);
                Assert.Equal(ExpandCollapseState.Expanded, State(button));

                Invoke(button); // zweites Auslösen bei offenem Menü: zu, nicht neu auf
                Assert.False(menu.IsOpen);
                Assert.False(button.IsMenuOpen);
                Assert.Equal(ExpandCollapseState.Collapsed, State(button));

                Invoke(button); // danach öffnet es wieder
                Assert.True(menu.IsOpen);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Fact]
    public void Second_mouse_click_closes_the_menu_and_does_not_reopen_it()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                Invoke(button);
                Assert.True(menu.IsOpen);

                // Drücken auf dem Knopf bei offenem Menü; das Menü geht dabei zu (Klick daneben), der Klick kommt danach an.
                Raise(button, UIElement.PreviewMouseLeftButtonDownEvent);
                Close(menu);
                Invoke(button);
                Assert.False(menu.IsOpen);
                Assert.False(button.IsMenuOpen);
                Assert.Equal(ExpandCollapseState.Collapsed, State(button));

                // Nach dem Loslassen gilt die Sperre nicht mehr: der nächste Klick öffnet.
                Raise(button, UIElement.PreviewMouseLeftButtonUpEvent);
                WpfTestHost.DoEvents();
                Invoke(button);
                Assert.True(menu.IsOpen);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Fact]
    public void A_press_that_never_reached_the_button_does_not_swallow_a_later_click()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                // Ein neues Drücken bei geschlossenem Menü öffnet wie immer (keine Sperre ohne offenes Menü).
                Raise(button, UIElement.PreviewMouseLeftButtonDownEvent);
                Invoke(button);
                Assert.True(menu.IsOpen);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Fact]
    public void A_key_after_a_press_released_elsewhere_opens_the_menu_again()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                // Drücken bei offenem Menü schließt es; losgelassen wird außerhalb (kein Klick, kein Loslassen am Knopf).
                Invoke(button);
                Raise(button, UIElement.PreviewMouseLeftButtonDownEvent);
                Close(menu);

                // Eine Taste beginnt eine neue Bedienung: Eingabetaste bzw. Auslösen danach öffnet das Menü.
                Press(button, Key.LeftCtrl);
                Invoke(button);
                Assert.True(menu.IsOpen);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void Enter_or_space_closes_the_open_menu_while_no_entry_is_highlighted(Key key)
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                Invoke(button);
                Assert.True(menu.IsOpen);

                // Die Tastatur geht beim Öffnen ans Menü; ohne gewählten Eintrag wirkt die Taste wie ein zweiter Klick.
                Press(menu, key);
                Assert.False(menu.IsOpen);
                Assert.False(button.IsMenuOpen);

                // Auf dem Knopf selbst (Fokus noch dort) ebenso.
                Invoke(button);
                Assert.True(menu.IsOpen);
                Press(button, key);
                Assert.False(menu.IsOpen);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Fact]
    public void Enter_on_a_highlighted_entry_still_belongs_to_the_entry()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            var clicked = 0;
            var entry = (MenuItem)menu.Items[0];
            entry.Click += (_, _) => clicked++;
            try
            {
                Invoke(button);
                Assert.True(menu.IsOpen);
                entry.Focus();
                WpfTestHost.DoEvents();
                Assert.True(entry.IsHighlighted);
                Press(entry, Key.Enter);
                Assert.Equal(1, clicked);
            }
            finally
            {
                Close(menu);
            }
        });
    }

    [Fact]
    public void Escape_still_closes_and_the_next_click_opens()
    {
        WpfTestHost.Run(() =>
        {
            var (button, menu) = Standalone();
            try
            {
                Invoke(button);
                Press(menu, Key.Escape);
                Assert.False(menu.IsOpen);
                Assert.False(button.IsMenuOpen);
                Invoke(button);
                Assert.True(menu.IsOpen);
            }
            finally
            {
                Close(menu);
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
