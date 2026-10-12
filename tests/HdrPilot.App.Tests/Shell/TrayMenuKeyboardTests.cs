using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Pfeiltasten im geöffneten Tray-Menü (G8.1): Das echte <see cref="TrayMenu"/> wird kurz geöffnet (eigenes Popup des
/// Testprozesses) und per Tastendruck-Ereignissen bedient, wie sie das Eingabesystem an das fokussierte Element schickt.
/// Allein, weil der Tastaturfokus für den ganzen Testprozess gilt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class TrayMenuKeyboardTests
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Einträge, die die Pfeiltasten erreichen, von oben nach unten (ohne Versionszeile und Trenner).</summary>
    private static readonly string[] Navigable = ["TrayToggleHdr", "TrayPause", "TrayOpen", "TraySettings", "TrayLogs", "TrayExit"];

    [Fact]
    public void AppVersion_line_never_gets_the_focus_when_arrowing_through_the_open_tray_menu()
    {
        WpfTestHost.Run(() =>
        {
            // Unsichtbares Hilfsfenster des Testthreads mit dem Win32-Fokus dieses Threads, wie in der App das Hilfsfenster
            // des Tray-Dienstes (hier ohne Vordergrund): Ohne Fokus im Thread nimmt das nicht aktivierbare Menü-Popup
            // keinen Tastaturfokus an.
            var holder = new Border { Focusable = true };
            using var owner = new HwndSource(new HwndSourceParameters("HdrPilot.Tests.TrayMenuOwner")
            {
                WindowStyle = WS_POPUP,
                ExtendedWindowStyle = WS_EX_TOOLWINDOW,
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
            })
            {
                RootVisual = holder,
            };
            Assert.True(holder.Focus(), "Hilfsfenster bekommt den Fokus nicht");

            // Pfeil nach unten vom Start: „HDR jetzt …“, dann alle Einträge der Reihe nach und am Ende wieder nach oben,
            // an der Versionszeile vorbei
            var down = ArrowThroughOpenMenu([.. Enumerable.Repeat(Key.Down, Navigable.Length + 1)]);
            Assert.Equal("TrayToggleHdr", down[0]);
            string[] downOrder = [.. Navigable, Navigable[0]];
            Assert.Equal(downOrder, down);
            Assert.DoesNotContain("TrayVersion", down);

            // Pfeil nach oben vom Start: „HDRpilot beenden“, dann rückwärts; von „HDR jetzt …“ geht es nicht auf die
            // Versionszeile, sondern wieder ans Ende
            var up = ArrowThroughOpenMenu([.. Enumerable.Repeat(Key.Up, Navigable.Length + 1)]);
            Assert.Equal("TrayExit", up[0]);
            string[] upOrder = [.. Enumerable.Reverse(Navigable), Navigable[^1]];
            Assert.Equal(upOrder, up);
            Assert.DoesNotContain("TrayVersion", up);

            // Die Folge aus phase6-traymenu.ps1, Schritt 7: Pfeil nach unten trifft „HDR jetzt …“, Pfeil nach oben springt
            // von dort an der Versionszeile vorbei zu „HDRpilot beenden“ (dort beendet Eingabe die Testinstanz)
            string[] gateOrder = ["TrayToggleHdr", "TrayExit"];
            Assert.Equal(gateOrder, ArrowThroughOpenMenu(Key.Down, Key.Up));
        });
    }

    /// <summary>Öffnet ein neues Tray-Menü, drückt die Tasten der Reihe nach und liefert nach jedem Druck die AutomationId des fokussierten Elements.</summary>
    private static List<string> ArrowThroughOpenMenu(params Key[] keys)
    {
        var model = new TrayMenuViewModel(new FakeTrayHost(), TrayState.Initial, ex => throw ex);

        // Wie in der App (TrayService): am Mauszeiger. Ohne Treffertest, damit ein bewegter Zeiger keinen Eintrag wählt.
        var menu = new TrayMenu { DataContext = model, Placement = PlacementMode.MousePoint, IsHitTestVisible = false };
        var version = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "TrayVersion");
        var focused = new List<string>();
        menu.IsOpen = true;
        try
        {
            WpfTestHost.DoEvents();
            Assert.True(menu.IsOpen, "Menü nicht geöffnet");
            Assert.Same(menu, Keyboard.FocusedElement);

            for (var i = 0; i < keys.Length; i++)
            {
                Press(keys[i]);
                focused.Add(Keyboard.FocusedElement is DependencyObject element ? AutomationProperties.GetAutomationId(element) : "(kein Fokus)");
                var pressed = string.Join(", ", keys[..(i + 1)]);
                Assert.False(version.IsKeyboardFocusWithin, $"Versionszeile hat nach {pressed} den Tastaturfokus");
                Assert.True(menu.IsOpen, $"Menü nach {pressed} geschlossen");
            }
        }
        finally
        {
            menu.IsOpen = false;
            WpfTestHost.DoEvents();
            menu.DataContext = null;
        }

        return focused;
    }

    /// <summary>Taste wie vom Eingabesystem: PreviewKeyDown (tunnelnd), dann KeyDown mit denselben Daten an das fokussierte Element.</summary>
    private static void Press(Key key)
    {
        var target = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
        args.RoutedEvent = Keyboard.KeyDownEvent;
        target.RaiseEvent(args);
        WpfTestHost.DoEvents();
    }
}
