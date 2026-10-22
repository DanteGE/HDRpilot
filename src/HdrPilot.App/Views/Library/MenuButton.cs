using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Knopf, der sein Kontextmenü als Auswahlmenü darunter öffnet (Titel im Kopf der Bibliothek → Quellenmenü, Wunsch des
/// Nutzers 2026-10-22). Öffnen per Klick, Eingabe- oder Leertaste, Alt+Pfeil nach unten oder F4 (auch Rechtsklick und
/// Umschalt+F10 wie jedes Kontextmenü), linksbündig mit dem Knopf. Mit der Tastatur geöffnet steht der Fokus auf dem
/// gewählten Eintrag (angehakt, sonst dem ersten), mit der Maus auf keinem. <see cref="IsMenuOpen"/> folgt dem Menü (Pfeil
/// der Vorlage dreht sich). Für UI-Automation ein Knopf mit Aus-/Einklappen (ExpandCollapse) und Auslösen.
/// </summary>
public sealed class MenuButton : Button
{
    private static readonly DependencyPropertyKey IsMenuOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMenuOpen), typeof(bool), typeof(MenuButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMenuOpenProperty = IsMenuOpenPropertyKey.DependencyProperty;

    /// <summary>Abstand des Menüs unter dem Knopf.</summary>
    public const double MenuGap = 4;

    /// <summary>Außenrand der Vorlage von Lib.ContextMenu (Platz für den Schatten), um den das Menü versetzt wird.</summary>
    public static readonly Thickness MenuShadowMargin = new(8, 4, 12, 18);

    private bool _focusOnOpen;

    public bool IsMenuOpen => (bool)GetValue(IsMenuOpenProperty);

    /// <summary>Menü öffnen; <paramref name="byKeyboard"/>: Fokus auf den gewählten Eintrag.</summary>
    public void OpenMenu(bool byKeyboard)
    {
        if (ContextMenu is not { } menu || menu.IsOpen)
        {
            return;
        }

        Prepare(menu);
        _focusOnOpen = byKeyboard;
        menu.IsOpen = true;
    }

    public void CloseMenu()
    {
        if (ContextMenu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }
    }

    /// <summary>Bildschirmfotos: Knopf zeigen, als wäre sein Menü offen (Fläche, gedrehter Pfeil), ohne Popup.</summary>
    public void PreviewMenuOpen() => SetValue(IsMenuOpenPropertyKey, true);

    /// <summary>
    /// Lage des Menüs: Die Menüfläche beginnt bündig mit dem Knopf, <see cref="MenuGap"/> darunter (Lib.ContextMenu hat
    /// außen 8 px links und 4 px oben für den Schatten).
    /// </summary>
    public static (double Horizontal, double Vertical) MenuOffset(Thickness menuMargin) => (-menuMargin.Left, MenuGap - menuMargin.Top);

    protected override void OnClick()
    {
        base.OnClick();
        OpenMenu(InputManager.Current.MostRecentInputDevice is KeyboardDevice);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!e.Handled && ((key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) || (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)))
        {
            OpenMenu(byKeyboard: true);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        if (ContextMenu is { } menu)
        {
            Prepare(menu);
            _focusOnOpen = e.CursorLeft < 0 && e.CursorTop < 0; // Umschalt+F10 bzw. Menütaste
        }
    }

    /// <summary>Eintrag, der beim Öffnen per Tastatur den Fokus bekommt: der angehakte, sonst der erste bedienbare.</summary>
    public static MenuItem? EntryToFocus(ItemsControl menu)
    {
        var items = menu.Items.Cast<object>()
            .Select(i => i as MenuItem ?? menu.ItemContainerGenerator.ContainerFromItem(i) as MenuItem)
            .OfType<MenuItem>()
            .ToList();
        return items.FirstOrDefault(i => i.IsChecked && i.IsEnabled) ?? items.FirstOrDefault(i => i.IsEnabled);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MenuButtonPeer(this);

    /// <summary>Das Kontextmenü gesetzt oder getauscht: auf sein Öffnen und Schließen hören (auch nach Rechtsklick).</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != ContextMenuProperty)
        {
            return;
        }

        if (e.OldValue is ContextMenu old)
        {
            old.Opened -= OnMenuOpened;
            old.Closed -= OnMenuClosed;
        }

        if (e.NewValue is ContextMenu menu)
        {
            menu.Opened += OnMenuOpened;
            menu.Closed += OnMenuClosed;
        }
    }

    private void Prepare(ContextMenu menu)
    {
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Bottom;
        (menu.HorizontalOffset, menu.VerticalOffset) = MenuOffset(MenuShadowMargin);
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        SetValue(IsMenuOpenPropertyKey, true);
        RaiseExpandCollapse(false, true);
        if (_focusOnOpen && sender is ContextMenu menu)
        {
            _focusOnOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => EntryToFocus(menu)?.Focus());
        }
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        SetValue(IsMenuOpenPropertyKey, false);
        RaiseExpandCollapse(true, false);
    }

    private void RaiseExpandCollapse(bool oldOpen, bool newOpen)
    {
        if (UIElementAutomationPeer.FromElement(this) is MenuButtonPeer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>Knopf mit Auslösen (öffnet das Menü) und Aus-/Einklappen (offen = aufgeklappt).</summary>
    private sealed class MenuButtonPeer(MenuButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            owner.OpenMenu(byKeyboard: true);
        }

        public void Collapse() => owner.CloseMenu();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        protected override string GetClassNameCore() => nameof(MenuButton);
    }
}
