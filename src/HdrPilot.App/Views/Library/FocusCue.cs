using System.Windows;
using System.Windows.Input;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Fokusring nur bei Bedienung mit der Tastatur (Rückmeldung des Nutzers 2026-10-22, wie :focus-visible im Browser): Ein
/// Knopf, der den Tastaturfokus durch einen Mausklick bekommt, zeigt keinen Ring, sonst sähe ein ausgeschalteter Filter
/// nach dem Klick weiter „gewählt“ aus. Kommt der Fokus über die Tastatur (Tab, Pfeile, ein Kürzel), oder drückt der Nutzer
/// auf dem Element eine Taste (etwa die Leertaste), erscheint er; ein Klick nimmt ihn wieder weg. Ist in Windows
/// „Zugriffstasten unterstreichen“ an (<see cref="SystemParameters.KeyboardCues"/>), zeigt WPF Fokus immer an, dann auch
/// hier. Vorlagen fragen <see cref="IsVisibleProperty"/> statt IsKeyboardFocused ab; eingeschaltet wird es je Stil über
/// <see cref="EnabledProperty"/>.
/// </summary>
public static class FocusCue
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusCue), new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyPropertyKey IsVisiblePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsVisible", typeof(bool), typeof(FocusCue), new PropertyMetadata(false));

    /// <summary>Fokusring zeigen: Tastaturfokus, der nicht von einem Klick kommt.</summary>
    public static readonly DependencyProperty IsVisibleProperty = IsVisiblePropertyKey.DependencyProperty;

    /// <summary>Zuletzt mit Maus, Stift oder Finger gedrückt (bis der Fokus geht oder eine Taste kommt).</summary>
    private static readonly DependencyProperty PointerPressedProperty = DependencyProperty.RegisterAttached(
        "PointerPressed", typeof(bool), typeof(FocusCue), new PropertyMetadata(false));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetIsVisible(DependencyObject element) => (bool)element.GetValue(IsVisibleProperty);

    /// <summary>
    /// Entscheidung beim Eintreffen des Fokus: Ring, wenn Windows Fokus immer zeigt, oder wenn er nicht von einem Klick kommt
    /// und die letzte Eingabe von der Tastatur war (Tab, Pfeile; auch ein Fokus, den das Programm nach einer Taste setzt).
    /// </summary>
    public static bool ShowsOnFocus(bool keyboardCues, bool pointerPressed, InputDevice? mostRecentInput) =>
        keyboardCues || (!pointerPressed && mostRecentInput is KeyboardDevice);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.GotKeyboardFocus -= OnGotKeyboardFocus;
        element.LostKeyboardFocus -= OnLostKeyboardFocus;
        element.PreviewMouseDown -= OnPointerDown;
        element.PreviewStylusDown -= OnPointerDown;
        element.PreviewTouchDown -= OnPointerDown;
        element.PreviewKeyDown -= OnKeyDown;
        if ((bool)e.NewValue)
        {
            element.GotKeyboardFocus += OnGotKeyboardFocus;
            element.LostKeyboardFocus += OnLostKeyboardFocus;
            element.PreviewMouseDown += OnPointerDown;
            element.PreviewStylusDown += OnPointerDown;
            element.PreviewTouchDown += OnPointerDown;
            element.PreviewKeyDown += OnKeyDown;
        }
        else
        {
            element.ClearValue(IsVisiblePropertyKey);
            element.ClearValue(PointerPressedProperty);
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is UIElement element && ReferenceEquals(e.NewFocus, element))
        {
            var pointer = (bool)element.GetValue(PointerPressedProperty);
            element.SetValue(IsVisiblePropertyKey, ShowsOnFocus(SystemParameters.KeyboardCues, pointer, InputManager.Current.MostRecentInputDevice));
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is UIElement element && ReferenceEquals(e.OldFocus, element))
        {
            element.SetValue(IsVisiblePropertyKey, false);
            element.ClearValue(PointerPressedProperty);
        }
    }

    private static void OnPointerDown(object? sender, RoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.SetValue(PointerPressedProperty, true);
            element.SetValue(IsVisiblePropertyKey, SystemParameters.KeyboardCues && element.IsKeyboardFocused);
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Tasten gehen an das Element mit dem Fokus; nur eigene zählen (nicht die eines fokussierten Kindes), und nur Umschalt,
        // Strg, Alt oder Windows-Taste allein sind noch keine Bedienung des Elements.
        if (sender is not UIElement element || !ReferenceEquals(e.OriginalSource, element) || IsModifier(e.Key == Key.System ? e.SystemKey : e.Key))
        {
            return;
        }

        element.ClearValue(PointerPressedProperty);
        element.SetValue(IsVisiblePropertyKey, true);
    }

    private static bool IsModifier(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
