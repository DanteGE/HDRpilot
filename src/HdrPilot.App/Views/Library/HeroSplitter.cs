using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Ziehgriff am unteren Rand des Titelbereichs (Wunsch des Nutzers 2026-10-16, seit 2026-10-22 in Liste, Raster und Regal,
/// jede Ansicht mit ihrer eigenen Höhe): stellt dessen Höhe in geräteunabhängigen Pixeln ein. Mit der Maus ziehen (Esc bricht ab, wie beim Trenner der Spalten); Pfeiltasten oben/unten
/// in Schritten von <see cref="SmallStep"/>, Bild auf/ab in Schritten von <see cref="LargeStep"/>, Pos1/Ende kleinste bzw.
/// größte Höhe; Doppelklick und Eingabetaste setzen auf den Standard zurück. Für die UI-Automation ein Griff mit
/// Wertebereich (RangeValue), lesbar und setzbar ohne echte Eingaben; Auslösen (Invoke) setzt wie der Doppelklick zurück.
/// <para>
/// Bereich und Wert setzt nur das Fenster. Was der Nutzer tut, meldet der Griff: <see cref="Moved"/> bei jeder Änderung
/// (beim Ziehen laufend), <see cref="Committed"/>, wenn sie fertig ist und gemerkt werden soll (Ende des Ziehens, Taste,
/// UI-Automation; nie nach einem bloßen Klick), <see cref="Discarded"/>, wenn ein Ziehen ohne Ergebnis endet, und
/// <see cref="ResetRequested"/>.
/// </para>
/// </summary>
public sealed class HeroSplitter : Thumb
{
    /// <summary>Schritt der Pfeiltasten (wie beim Trenner zwischen Liste und Detailbereich).</summary>
    public const double SmallStep = 24;

    /// <summary>Schritt von Bild auf/ab.</summary>
    public const double LargeStep = 96;

    /// <summary>Abstand der Leuchtlinie vom linken Rand der Liste (und ohne Werkzeugleiste vom rechten).</summary>
    public const double LineInset = 18;

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(HeroSplitter), new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(HeroSplitter), new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(HeroSplitter), new PropertyMetadata(0.0, OnValueChanged, CoerceValue));

    /// <summary>
    /// Ränder der Leuchtlinie (beim Überfahren, Ziehen, mit Fokus): links <see cref="LineInset"/>, rechts setzt das Fenster
    /// sie 12 px vor Lasche bzw. Kapsel der Werkzeugleiste (Wunsch des Nutzers 2026-10-17).
    /// </summary>
    public static readonly DependencyProperty LineMarginProperty = DependencyProperty.Register(
        nameof(LineMargin), typeof(Thickness), typeof(HeroSplitter), new PropertyMetadata(new Thickness(LineInset, 0, LineInset, 0)));

    private double _dragStart;
    private bool _dragging;
    private bool _dragMoved;
    private bool _dragCanceled;

    public HeroSplitter()
    {
        DragStarted += OnDragStarted;
        DragDelta += OnDragDelta;
        DragCompleted += OnDragCompleted;
        MouseDoubleClick += OnDoubleClick;
    }

    /// <summary>Der Nutzer hat die Höhe geändert (<see cref="Value"/>); beim Ziehen bei jeder Bewegung.</summary>
    public event EventHandler? Moved;

    /// <summary>Eine Änderung ist fertig und soll gemerkt werden.</summary>
    public event EventHandler? Committed;

    /// <summary>
    /// Ein Ziehen hat die Höhe bewegt, endet aber ohne Änderung, die gemerkt wird: zurück an der Ausgangslage losgelassen oder
    /// abgebrochen (Esc, Maus verloren; der Griff steht dann wieder auf der Höhe vor dem Ziehen). Im Fenster gilt wieder die
    /// gemerkte Höhe, nicht der Anteil, den das Ziehen zuletzt ergab.
    /// </summary>
    public event EventHandler? Discarded;

    /// <summary>Doppelklick oder Eingabetaste: zurück zur Standardhöhe.</summary>
    public event EventHandler? ResetRequested;

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Höhe des Titelbereichs, auf <see cref="Minimum"/> bis <see cref="Maximum"/> begrenzt.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Thickness LineMargin
    {
        get => (Thickness)GetValue(LineMarginProperty);
        set => SetValue(LineMarginProperty, value);
    }

    /// <summary>
    /// Höhe auf Wunsch des Nutzers ändern (ganze Pixel, begrenzt) und melden; <paramref name="commit"/>: fertig, merken. Ohne
    /// Änderung (etwa am Rand des Bereichs) wird nichts gemeldet.
    /// </summary>
    private void MoveTo(double value, bool commit)
    {
        var before = Value;
        Value = Math.Round(value);
        if (Value == before)
        {
            return;
        }

        Moved?.Invoke(this, EventArgs.Empty);
        if (commit)
        {
            Committed?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                MoveTo(Value - SmallStep, commit: true);
                break;
            case Key.Down:
                MoveTo(Value + SmallStep, commit: true);
                break;
            case Key.PageUp:
                MoveTo(Value - LargeStep, commit: true);
                break;
            case Key.PageDown:
                MoveTo(Value + LargeStep, commit: true);
                break;
            case Key.Home:
                MoveTo(Minimum, commit: true);
                break;
            case Key.End:
                MoveTo(Maximum, commit: true);
                break;
            case Key.Enter:
                ResetRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Escape when _dragging:
                // Ziehen abbrechen wie beim Trenner der Spalten (GridSplitter): zurück zur Höhe davor, nichts gemerkt.
                _dragCanceled = true;
                MoveTo(_dragStart, commit: false);
                CancelDrag(); // endet in OnDragCompleted (ohne Maus, etwa im Test, erst mit dem Loslassen)
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HeroSplitterAutomationPeer(this);

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => d.CoerceValue(ValueProperty);

    private static object CoerceValue(DependencyObject d, object value)
    {
        var splitter = (HeroSplitter)d;
        var v = (double)value;
        return double.IsFinite(v) ? Math.Max(splitter.Minimum, Math.Min(splitter.Maximum, v)) : splitter.Minimum;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged) && UIElementAutomationPeer.FromElement((HeroSplitter)d) is HeroSplitterAutomationPeer peer)
        {
            peer.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
        }
    }

    /// <summary>
    /// Doppelklick: zurück zum Standard. Als erledigt markiert, erreicht der zweite Druck den Thumb nicht mehr (Control meldet
    /// den Doppelklick, bevor UIElement OnMouseLeftButtonDown aufruft): Er beginnt kein Ziehen, dessen kleine Bewegung die
    /// Standardhöhe gleich wieder als eigene Höhe merken könnte.
    /// </summary>
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _dragStart = Value;
        _dragging = true;
        _dragMoved = false;
        _dragCanceled = false;
    }

    /// <summary>
    /// Der Griff folgt dem Zeiger: Die Verschiebung zählt ab seiner aktuellen Lage (wie bei Thumb üblich), am Rand des
    /// Bereichs bleibt er stehen und holt den Zeiger wieder ab, sobald dieser zurückkommt.
    /// </summary>
    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var before = Value;
        MoveTo(Value + e.VerticalChange, commit: false);
        _dragMoved |= Value != before;
    }

    /// <summary>
    /// Ende des Ziehens: gemerkt wird nur eine echte Änderung. Abgebrochen (Esc, Maus verloren) geht der Griff zurück auf die
    /// Höhe vor dem Ziehen. Hat das Ziehen bewegt, ohne dass etwas gemerkt wird, gilt im Fenster wieder die gemerkte Höhe
    /// (<see cref="Discarded"/>).
    /// </summary>
    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_dragging)
        {
            return; // schon beendet
        }

        var moved = _dragMoved;
        var canceled = e.Canceled || _dragCanceled;
        _dragMoved = false;
        _dragCanceled = false;
        _dragging = false;
        if (canceled)
        {
            MoveTo(_dragStart, commit: false);
        }

        if (!canceled && moved && Value != _dragStart)
        {
            Committed?.Invoke(this, EventArgs.Empty);
        }
        else if (moved)
        {
            Discarded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Griff mit Wertebereich: Höhe in Pixeln lesen und setzen (gilt wie eine Taste, wird gemerkt). Auslösen (Invoke) wirkt
    /// wie Doppelklick und Eingabetaste: Standardhöhe.
    /// </summary>
    private sealed class HeroSplitterAutomationPeer(HeroSplitter owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider, IInvokeProvider
    {
        private HeroSplitter Splitter => (HeroSplitter)Owner;

        public double Value => Splitter.Value;

        public double Minimum => Splitter.Minimum;

        public double Maximum => Splitter.Maximum;

        public double SmallChange => SmallStep;

        public double LargeChange => LargeStep;

        public bool IsReadOnly => !Splitter.IsEnabled;

        public void SetValue(double value)
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            if (!double.IsFinite(value) || value < Splitter.Minimum || value > Splitter.Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Splitter.MoveTo(value, commit: true);
        }

        public void Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            Splitter.ResetRequested?.Invoke(Splitter, EventArgs.Empty);
        }

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.RangeValue or PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        internal void RaiseValueChanged(double oldValue, double newValue) =>
            RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override string GetClassNameCore() => nameof(HeroSplitter);
    }
}
