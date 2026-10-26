using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace HdrPilot.App.Views.Library;

/// <summary>Neue Anordnung der Werkzeugleiste: vorher (null beim ersten Mal) und jetzt.</summary>
public sealed class ToolbarArrangedEventArgs(ToolbarArrangement? previous, ToolbarArrangement current) : EventArgs
{
    public ToolbarArrangement? Previous { get; } = previous;

    public ToolbarArrangement Current { get; } = current;

    /// <summary>Andere Zahl von Zeilen.</summary>
    public bool RowsChanged => Previous is { } p && p.Rows != Current.Rows;

    /// <summary>Die Sortierung zeigt jetzt nur ihr Symbol bzw. wieder ihre Beschriftung.</summary>
    public bool SortStepChanged => Previous is { } p && p.SortIconOnly != Current.SortIconOnly;
}

/// <summary>
/// Werkzeugleiste „Fließleiste mit Lichtnaht“ (abgenommene Vorschau, Wunsch des Nutzers 2026-10-17): ordnet ihre zehn
/// Elemente nach <see cref="ToolbarLayout.Arrange"/> an, und zwar im selben Durchgang, in dem sich die Breite ändert: Lagen
/// und Breiten folgen dem Fensterrand sofort, nichts hinkt nach. Nur solange der Nutzer zieht (<see cref="IsResizing"/>:
/// Fensterrand oder Trenner zum Detailbereich), gilt die Hysterese; bewegt werden dann nur die Schritte (Zeilenwechsel,
/// Sortier-Schritt), und zwar vom Fenster über <see cref="Arranged"/> (FLIP: die Elemente stehen schon an ihrem neuen Platz,
/// RenderTransform führt sie vom alten dorthin). Beim Maximieren, Einrasten, DPI-Wechsel und Start gilt sofort der Endzustand.
/// <para>
/// Die Leiste zeichnet auch über ihren Platz hinaus (kein Layout-Ausschnitt): Beim Einklappen meldet sie keine Höhe mehr
/// (<see cref="ReservesHeight"/>), die Liste steigt, und das Fenster schneidet sie über <see cref="UIElement.Clip"/> an der
/// Oberkante der Liste ab, solange die Elemente noch ausblenden. Elemente werden nur angeordnet, nie umgehängt: Die
/// Tab-Reihenfolge bleibt, der Fokus geht nicht verloren.
/// </para>
/// </summary>
public sealed class ToolbarPanel : Panel
{
    /// <summary>Platz eines Elements in der Leiste.</summary>
    public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached(
        "Slot", typeof(ToolbarSlot), typeof(ToolbarPanel), new FrameworkPropertyMetadata(ToolbarSlot.Search, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Die Sortierung zeigt nur ihr Symbol (schmaler Innenrand, keine Beschriftung).</summary>
    public static readonly DependencyProperty IconOnlyProperty = DependencyProperty.RegisterAttached(
        "IconOnly", typeof(bool), typeof(ToolbarPanel), new FrameworkPropertyMetadata(false));

    private readonly double[] _widthOffsets = new double[10];
    private ToolbarArrangement? _arrangement;
    private bool _reserves = true;
    private double _originFraction;

    public ToolbarPanel()
    {
        // Freie Fläche der Leiste reagiert auf Rechtsklick (Menü der Leiste), ohne etwas zu zeichnen.
        Background = Brushes.Transparent;

        // Ein- und ausgeklappt: UI-Automation erfährt, dass sich Elemente und Kinder geändert haben.
        IsVisibleChanged += (_, _) =>
        {
            if (UIElementAutomationPeer.FromElement(this) is { } peer)
            {
                peer.ResetChildrenCache();
                peer.InvalidatePeer();
            }
        };
    }

    /// <summary>Neue Anordnung (andere Breite oder Skalierung). Wird beim Messen ausgelöst, bevor die Elemente gemessen werden.</summary>
    public event EventHandler<ToolbarArrangedEventArgs>? Arranged;

    /// <summary>Anordnung, wie zuletzt gerechnet (null vor dem ersten Messen).</summary>
    public ToolbarArrangement? Arrangement => _arrangement;

    /// <summary>Zeilen, wie zuletzt angeordnet (Tests, Prüfung).</summary>
    public int Rows => _arrangement?.Rows ?? 1;

    /// <summary>Der Nutzer zieht gerade (Fensterrand, Trenner): Hysterese an, Schritte werden bewegt.</summary>
    public bool IsResizing { get; set; }

    /// <summary>
    /// Wo die Leiste im Pixelraster des Fensters beginnt: Bruchteil eines Gerätepixels (−0,5 … 0,5), gesetzt vom Fenster. Die
    /// rechte Kante liegt dann auf dem Gerätepixel, das der rechten Kante der Liste am nächsten ist.
    /// </summary>
    public double OriginFraction
    {
        get => _originFraction;
        set
        {
            if (Math.Abs(_originFraction - value) > 0.001)
            {
                _originFraction = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>Höhe im Layout melden (ausgeklappt); eingeklappt 0, die Liste rückt nach oben.</summary>
    public bool ReservesHeight
    {
        get => _reserves;
        set
        {
            if (_reserves != value)
            {
                _reserves = value;
                InvalidateMeasure();
            }
        }
    }

    public static ToolbarSlot GetSlot(UIElement element) => (ToolbarSlot)element.GetValue(SlotProperty);

    public static void SetSlot(UIElement element, ToolbarSlot value) => element.SetValue(SlotProperty, value);

    public static bool GetIconOnly(UIElement element) => (bool)element.GetValue(IconOnlyProperty);

    public static void SetIconOnly(UIElement element, bool value) => element.SetValue(IconOnlyProperty, value);

    /// <summary>Element auf einem Platz (null, wenn es keins gibt).</summary>
    public FrameworkElement? ItemAt(ToolbarSlot slot) =>
        InternalChildren.OfType<FrameworkElement>().FirstOrDefault(c => GetSlot(c) == slot);

    /// <summary>
    /// Breiten während einer Bewegung (Zeilenwechsel, Sortier-Schritt): je Platz die Abweichung von der Endbreite. Nur bei
    /// einer Änderung wird neu angeordnet; am Ende stehen alle wieder auf 0.
    /// </summary>
    public void SetWidthOffsets(ReadOnlySpan<double> offsets)
    {
        var changed = false;
        for (var i = 0; i < _widthOffsets.Length && i < offsets.Length; i++)
        {
            if (Math.Abs(_widthOffsets[i] - offsets[i]) > 0.001)
            {
                _widthOffsets[i] = offsets[i];
                changed = true;
            }
        }

        if (changed)
        {
            InvalidateArrange();
        }
    }

    /// <summary>
    /// Anordnung für eine Breite, während die Leiste nicht misst (etwa ihre ausgeklappte Höhe, solange sie eingeklappt ist): bei
    /// derselben Breite und Skalierung die zuletzt gerechnete (auch die beim Ziehen gemerkten zwei Zeilen; so misst sie beim
    /// Ausklappen wieder), sonst ohne Gedächtnis wie nach dem Ziehen.
    /// </summary>
    public ToolbarArrangement ArrangeFor(double width)
    {
        var scale = Scale();
        return _arrangement is { } last && last.Scale == scale && SameWidth(last, width)
            ? last
            : ToolbarLayout.Arrange(width, scale, originFraction: _originFraction);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        var scale = Scale();
        ToolbarMemory? memory = _arrangement is { } last && (IsResizing || SameWidth(last, width)) && last.Scale == scale
            ? last.Memory
            : null;
        var next = ToolbarLayout.Arrange(width, scale, memory, _originFraction);
        var previous = _arrangement;
        _arrangement = next;
        if (previous is null || !Same(previous, next))
        {
            Arranged?.Invoke(this, new ToolbarArrangedEventArgs(previous, next));
        }

        foreach (UIElement child in InternalChildren)
        {
            var slot = next[GetSlot(child)];
            child.Measure(new Size(Math.Max(0, slot.Width + _widthOffsets[(int)GetSlot(child)]), slot.Height));
        }

        return new Size(next.Width, _reserves ? next.Height : 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_arrangement is not { } arrangement)
        {
            return finalSize;
        }

        foreach (UIElement child in InternalChildren)
        {
            var index = (int)GetSlot(child);
            var slot = arrangement.Slots[index];
            child.Arrange(new Rect(slot.X, slot.Y, Math.Max(0, slot.Width + _widthOffsets[index]), slot.Height));
        }

        return finalSize;
    }

    /// <summary>Kein Layout-Ausschnitt: Die Leiste zeichnet weiter, während sie einklappt (das Fenster schneidet über Clip).</summary>
    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null;

    protected override AutomationPeer OnCreateAutomationPeer() =>
        string.IsNullOrEmpty(AutomationProperties.GetAutomationId(this)) ? base.OnCreateAutomationPeer() : new ToolbarPeer(this);

    /// <summary>Elemente innen auf ganzen Gerätepixeln (Rahmen der Felder und Knöpfe 1 px statt 1,1 px bei 110 %).</summary>
    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualAdded is FrameworkElement element)
        {
            element.UseLayoutRounding = true;
        }
    }

    /// <summary>Dieselbe Breite (bis auf einen halben Gerätepixel), für die zuletzt gerechnet wurde.</summary>
    private static bool SameWidth(ToolbarArrangement last, double width) =>
        Math.Abs(last.RequestedWidth - width) * last.Scale < 0.5;

    private static bool Same(ToolbarArrangement a, ToolbarArrangement b) =>
        a.Rows == b.Rows && a.Stage == b.Stage && a.Scale == b.Scale && a.Slots.SequenceEqual(b.Slots);

    private double Scale()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return dpi > 0 ? dpi : 1;
    }

    /// <summary>
    /// Werkzeugleiste im Automationsbaum (nur mit eigener Kennung): Prüfskripte und Bildschirmleser finden sie als Ganzes.
    /// Eingeklappt (Visibility.Collapsed) ist sie weder Steuer- noch Inhaltselement und hat keine Kinder: Bildschirmleser und
    /// die Steueransicht von UI-Automation sehen sie nicht mehr.
    /// </summary>
    private sealed class ToolbarPeer(ToolbarPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        private bool Shown => owner.Visibility == Visibility.Visible;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ToolBar;

        protected override string GetClassNameCore() => nameof(ToolbarPanel);

        protected override List<AutomationPeer>? GetChildrenCore() => Shown ? base.GetChildrenCore() : null;

        protected override bool IsControlElementCore() => Shown && base.IsControlElementCore();

        protected override bool IsContentElementCore() => Shown && base.IsContentElementCore();

        protected override bool IsOffscreenCore() => !Shown || base.IsOffscreenCore();
    }
}
