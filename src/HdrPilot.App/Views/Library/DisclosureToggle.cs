using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// „Mehr erfahren“ / „Weniger“ (Rückmeldung des Nutzers 2026-10-22): klappt Erklärungen auf, die sonst zu viel Platz
/// nähmen, etwa die Hinweise zu „Windows Auto HDR“ im Detailbereich. Ein Umschaltknopf, der seine Beschriftung tauscht
/// (<see cref="CollapsedText"/>, <see cref="ExpandedText"/>); Klick, Leer- und Eingabetaste schalten. Was er aufklappt
/// (<see cref="Reveals"/>), blendet beim Aufklappen nach der Effektstufe ein (Voll 220 ms mit 6 px Weg, Reduziert nur kurz,
/// Aus sofort). Für UI-Automation ein Knopf mit Aus-/Einklappen (ExpandCollapse) und Umschalten.
/// </summary>
public sealed class DisclosureToggle : ToggleButton
{
    public static readonly DependencyProperty CollapsedTextProperty = DependencyProperty.Register(
        nameof(CollapsedText), typeof(string), typeof(DisclosureToggle), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ExpandedTextProperty = DependencyProperty.Register(
        nameof(ExpandedText), typeof(string), typeof(DisclosureToggle), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RevealsProperty = DependencyProperty.Register(
        nameof(Reveals), typeof(UIElement), typeof(DisclosureToggle), new PropertyMetadata(null));

    /// <summary>Beschriftung zugeklappt („Mehr erfahren“).</summary>
    public string CollapsedText
    {
        get => (string)GetValue(CollapsedTextProperty);
        set => SetValue(CollapsedTextProperty, value);
    }

    /// <summary>Beschriftung aufgeklappt („Weniger“).</summary>
    public string ExpandedText
    {
        get => (string)GetValue(ExpandedTextProperty);
        set => SetValue(ExpandedTextProperty, value);
    }

    /// <summary>Bereich, der beim Aufklappen einblendet (seine Sichtbarkeit bindet er selbst an denselben Zustand).</summary>
    public UIElement? Reveals
    {
        get => (UIElement?)GetValue(RevealsProperty);
        set => SetValue(RevealsProperty, value);
    }

    /// <summary>Die Beschriftung, die gerade zu sehen ist.</summary>
    public string ShownText => IsChecked == true ? ExpandedText : CollapsedText;

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        if (Reveals is { } target && IsLoaded)
        {
            LibraryMotion.FadeIn(target, TimeSpan.Zero, offsetY: -6, durationMs: 220);
        }

        RaiseExpandCollapse(false, true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        RaiseExpandCollapse(true, false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DisclosureTogglePeer(this);

    private void RaiseExpandCollapse(bool oldExpanded, bool newExpanded)
    {
        if (UIElementAutomationPeer.FromElement(this) is DisclosureTogglePeer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>Knopf mit Aus-/Einklappen (aufgeklappt = angehakt) und weiter mit Umschalten wie jeder ToggleButton.</summary>
    private sealed class DisclosureTogglePeer(DisclosureToggle owner) : ToggleButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand()
        {
            ThrowIfDisabled();
            owner.SetCurrentValue(IsCheckedProperty, true);
        }

        public void Collapse()
        {
            ThrowIfDisabled();
            owner.SetCurrentValue(IsCheckedProperty, false);
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        protected override string GetClassNameCore() => nameof(DisclosureToggle);

        private void ThrowIfDisabled()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }
        }
    }
}
