using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Bildlaufleiste im Spalt zwischen Liste bzw. Raster und Detailbereich (Wunsch des Nutzers 2026-10-22): Die Pille, die dort
/// wie eine Bildlaufleiste aussah, ist jetzt die Bildlaufleiste der aktiven Ansicht (<see cref="Source"/>). Ihre Länge ist
/// sichtbarer Teil durch Gesamthöhe (mindestens <see cref="MinThumbLength"/>), ihre Bahn reicht genau so weit wie der
/// sichtbare Bereich der Ansicht, und ohne etwas zu scrollen ist sie weg. Die Pille ziehen scrollt pixelgenau, ein Klick
/// über bzw. unter sie blättert eine Seite (weich nach Effektstufe, <see cref="PageDuration"/>), das Mausrad im Spalt
/// scrollt die Ansicht. Die eigenen Bildlaufleisten von Liste und Raster sind aus; Mausrad, Tastatur und UI-Automation
/// (ScrollPattern der Liste) gehen wie bisher direkt an die Ansicht.
/// <para>
/// Derselbe Spalt bleibt für die Maus der Trenner zum Detailbereich (<see cref="Splitter"/>): Beim Überfahren außerhalb der
/// Pille erscheint die feine Linie, waagerecht Ziehen ändert die Breite (über die Zieh-Ereignisse des Trenners, der wie
/// bisher rechnet, begrenzt und meldet), Esc bricht ab, Doppelklick meldet <see cref="ResetRequested"/>. Klick oder Ziehen
/// entscheidet die Bewegung: Erst ab der Zieh-Schwelle von Windows waagerecht wird gezogen; wer vorher senkrecht über die
/// Schwelle rutscht, blättert nicht. Die Pille hat Vorrang vor der Linie. Tastatur und UI-Automation der Breite bleiben beim
/// Trenner (er nimmt den Fokus, die Leiste nie).
/// </para>
/// </summary>
public sealed class GapScrollBar : ScrollBar
{
    /// <summary>Kürzeste Pille: so lang wie der frühere ruhende Griff des Trenners.</summary>
    public const double MinThumbLength = 36;

    /// <summary>Bildlauf einer Zeile (wie Raster und Mausrad).</summary>
    public const double LineStep = 48;

    /// <summary>Blättern um eine Seite auf der Stufe „Voll“ („Reduziert“ höchstens 0,3 s, „Aus“ sofort).</summary>
    public static readonly TimeSpan PageDuration = TimeSpan.FromSeconds(0.35);

    /// <summary>Liste oder Raster (ListBox), deren ScrollViewer die Leiste führt; null: keine (Regal).</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Control), typeof(GapScrollBar), new PropertyMetadata(null, (d, _) => ((GapScrollBar)d).Resolve()));

    /// <summary>Trenner zum Detailbereich: Ziehen im Spalt bewegt ihn über seine Zieh-Ereignisse.</summary>
    public static readonly DependencyProperty SplitterProperty = DependencyProperty.Register(
        nameof(Splitter), typeof(GridSplitter), typeof(GapScrollBar), new PropertyMetadata(null));

    /// <summary>Bildschirmfoto: feine Linie wie beim Überfahren, ohne Maus.</summary>
    public static readonly DependencyProperty ShowsLineProperty = DependencyProperty.Register(
        nameof(ShowsLine), typeof(bool), typeof(GapScrollBar), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey TrackMarginPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TrackMargin), typeof(Thickness), typeof(GapScrollBar), new PropertyMetadata(default(Thickness)));

    /// <summary>Abstand der Bahn oben und unten: die Bahn reicht genau so weit wie der sichtbare Bereich der Ansicht.</summary>
    public static readonly DependencyProperty TrackMarginProperty = TrackMarginPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsResizingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsResizing), typeof(bool), typeof(GapScrollBar), new PropertyMetadata(false));

    /// <summary>Im Spalt wird gerade die Breite gezogen (Linie in der Akzentfarbe).</summary>
    public static readonly DependencyProperty IsResizingProperty = IsResizingPropertyKey.DependencyProperty;

    /// <summary>Weiches Blättern (animiert), schreibt den Versatz der Ansicht, solange geblättert wird.</summary>
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        "AnimatedOffset", typeof(double), typeof(GapScrollBar), new PropertyMetadata(0.0, (d, e) => ((GapScrollBar)d).OnAnimatedOffset((double)e.NewValue)));

    private static readonly IEasingFunction PageEase = CreateEase();

    private readonly TranslateTransform _trackShift = new();
    private readonly MouseWheelEventHandler _sourceWheel;
    private readonly KeyEventHandler _sourceKey;
    private readonly MouseButtonEventHandler _sourceMouse;
    private readonly ScrollChangedEventHandler _sourceScroll;
    private readonly KeyEventHandler _escape;
    private ScrollViewer? _viewer;
    private Window? _keyWindow;
    private bool _syncing;
    private bool _pressed;
    private bool _resizing;
    private bool _clickCanceled;
    private bool _pagedByClick;
    private Point _pressPoint;
    private double _resizeOrigin;
    private double _pageTarget = double.NaN;
    private double _lastAnimated = double.NaN;
    private double _undoOffset = double.NaN;
    private int _pageToken;

    public GapScrollBar()
    {
        _sourceWheel = (_, _) => StopPaging();
        _sourceKey = (_, _) => StopPaging();
        _sourceMouse = (_, _) => StopPaging();
        _sourceScroll = OnSourceScrollChanged;
        _escape = OnWindowKey;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>Doppelklick in den Spalt (nicht auf die Pille): zurück zur Standardaufteilung.</summary>
    public event EventHandler? ResetRequested;

    public Control? Source
    {
        get => (Control?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public GridSplitter? Splitter
    {
        get => (GridSplitter?)GetValue(SplitterProperty);
        set => SetValue(SplitterProperty, value);
    }

    public bool ShowsLine
    {
        get => (bool)GetValue(ShowsLineProperty);
        set => SetValue(ShowsLineProperty, value);
    }

    public Thickness TrackMargin => (Thickness)GetValue(TrackMarginProperty);

    public bool IsResizing => (bool)GetValue(IsResizingProperty);

    /// <summary>Der ScrollViewer, den die Leiste gerade führt (Tests).</summary>
    public ScrollViewer? Viewer => _viewer;

    /// <summary>Ziel des laufenden weichen Blätterns (NaN: es wird nicht geblättert; Tests).</summary>
    public double PagingTarget => _pageTarget;

    /// <summary>
    /// Senkrechte Verschiebung der Bahn wie die des Inhalts, während die Werkzeugleiste ein- oder ausklappt (FLIP). Wie der
    /// Inhalt ist sie dabei unten am Ende des sichtbaren Bereichs geschnitten, oben frei.
    /// </summary>
    public void ShiftTrack(double dy)
    {
        _trackShift.Y = dy;
        if (Track is { Parent: UIElement host })
        {
            var moving = Math.Abs(dy) > 0.01;
            host.Clip = moving ? new RectangleGeometry(new Rect(-ActualWidth, -1000, 3 * ActualWidth, Math.Max(0, ActualHeight - TrackMargin.Bottom) + 1000)) : null;
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (Track is { } track)
        {
            // Kürzeste Pille: Track nimmt die halbe Höhe der Pfeilknöpfe einer Bildlaufleiste als kleinste Länge des Thumb.
            track.Resources[SystemParameters.VerticalScrollBarButtonHeightKey] = 2 * MinThumbLength;
            track.RenderTransform = _trackShift;
        }
    }

    /// <summary>
    /// Eine Seite (sichtbare Höhe) nach oben (negativ) oder unten blättern, weich nach Effektstufe. Folgt ein Klick, während
    /// noch geblättert wird, zählt die nächste Seite ab dem Ziel; Mausrad, Tasten, ein Klick in die Ansicht und das Ziehen der
    /// Pille beenden das Blättern sofort.
    /// </summary>
    public void PageBy(int direction)
    {
        if (_viewer is not { } viewer || direction == 0 || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        var from = double.IsNaN(_pageTarget) ? viewer.VerticalOffset : _pageTarget;
        ScrollSmoothly(Math.Clamp(from + (Math.Sign(direction) * viewer.ViewportHeight), 0, viewer.ScrollableHeight));
    }

    /// <summary>
    /// Druck mit der linken Taste in den Spalt außerhalb der Pille, in Koordinaten der Leiste (Maus; Tests ohne echte Maus).
    /// Der zweite Druck eines Doppelklicks setzt die Aufteilung zurück und nimmt das Blättern des ersten Klicks zurück.
    /// </summary>
    public void Press(Point position, int clickCount)
    {
        if (clickCount >= 2)
        {
            _pressed = false;
            if (_pagedByClick && !double.IsNaN(_undoOffset))
            {
                ScrollSmoothly(_undoOffset);
            }

            _pagedByClick = false;
            ResetRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _pagedByClick = false;
        _pressed = true;
        _resizing = false;
        _clickCanceled = false;
        _pressPoint = position;
    }

    /// <summary>
    /// Bewegung bei gedrückter Taste: ab der waagerechten Zieh-Schwelle wird die Breite gezogen (der Trenner folgt dem Zeiger
    /// ab seiner jetzigen Lage, wie bei seinem eigenen Thumb); rutscht der Zeiger vorher senkrecht über die Schwelle, war es
    /// kein Klick (es wird nicht geblättert).
    /// </summary>
    public void Drag(Point position)
    {
        if (!_pressed)
        {
            return;
        }

        var dx = position.X - _pressPoint.X;
        if (!_resizing)
        {
            if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance)
            {
                _clickCanceled |= Math.Abs(position.Y - _pressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
                return;
            }

            BeginResize();
        }

        Splitter?.RaiseEvent(new DragDeltaEventArgs(dx, 0));
    }

    /// <summary>Loslassen: Ziehen beenden (Breite merkt das Fenster) oder, war es ein Klick, zur Stelle hin blättern.</summary>
    public void Release(Point position)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        if (_resizing)
        {
            EndResize(canceled: false, position.X - _pressPoint.X);
        }
        else if (!_clickCanceled)
        {
            var before = _viewer?.VerticalOffset ?? double.NaN;
            _pagedByClick = PageToward(position.Y);
            _undoOffset = _pagedByClick ? before : double.NaN;
        }
    }

    /// <summary>Esc beim Ziehen: zurück zur Breite davor (wie beim Trenner), nichts gemerkt.</summary>
    public void CancelResize()
    {
        if (!_resizing)
        {
            return;
        }

        // Der Trenner rechnet ab seiner jetzigen Lage: um so viel zurück, wie sich der Spalt bewegt hat.
        Splitter?.RaiseEvent(new DragDeltaEventArgs(_resizeOrigin - VisualTreeHelper.GetOffset(this).X, 0));
        _pressed = false;
        EndResize(canceled: true, 0);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || IsOnThumb(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Press(e.GetPosition(this), e.ClickCount);
        if (_pressed)
        {
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressed)
        {
            Drag(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressed)
        {
            Release(e.GetPosition(this));
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <summary>Maus verloren (anderes Fenster, Alt+Tab): Ziehen endet wie beim Trenner mit der erreichten Breite, ein Klick blättert nicht.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        if (_resizing)
        {
            EndResize(canceled: true, 0);
        }
    }

    /// <summary>Mausrad im Spalt: scrollt die Ansicht wie über ihr selbst.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || _viewer is not { } viewer)
        {
            return;
        }

        StopPaging();
        viewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = viewer });
        e.Handled = true;
    }

    /// <summary>Der Hinweis zum Spalt (Breite, blättern) gilt nicht über der Pille und nicht beim Ziehen.</summary>
    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        base.OnToolTipOpening(e);
        if (_pressed || Track?.Thumb is { IsMouseOver: true })
        {
            e.Handled = true;
        }
    }

    /// <summary>Wert geändert durch die Pille (Ziehen) oder die UI-Automation (RangeValue): die Ansicht folgt sofort.</summary>
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (!_syncing && _viewer is { } viewer)
        {
            StopPaging();
            viewer.ScrollToVerticalOffset(newValue);
        }
    }

    private static IEasingFunction CreateEase()
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        ease.Freeze();
        return ease;
    }

    /// <summary>Wirksame Dauer des Blätterns: wie die übrigen kurzen Übergänge der Bibliothek nach Effektstufe.</summary>
    private static TimeSpan PageTransition() =>
        LibraryMotion.Enabled ? EffectPolicy.Transition(LibraryMotion.Instance.Level, PageDuration) : TimeSpan.Zero;

    /// <summary>Erster ScrollViewer in der Vorlage der Ansicht (Liste und Raster: ScrollHost); null, solange sie keine Vorlage hat.</summary>
    private static ScrollViewer? FindViewer(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is ScrollViewer viewer)
            {
                return viewer;
            }

            if (current is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        return null;
    }

    /// <summary>Kachel-Panel des Rasters als Inhalt des ScrollViewers (ItemsPresenter → Panel); null in der Liste.</summary>
    private static VirtualizingTilePanel? TilePanel(ScrollViewer viewer) =>
        viewer.Content is ItemsPresenter presenter && VisualTreeHelper.GetChildrenCount(presenter) > 0
            ? VisualTreeHelper.GetChild(presenter, 0) as VirtualizingTilePanel
            : null;

    /// <summary>Lage im Layout relativ zu einem Vorfahren, ohne RenderTransform (die Verschiebung beim Ein- und Ausklappen zählt nicht).</summary>
    private static Vector? LayoutOffset(DependencyObject element, DependencyObject ancestor)
    {
        var offset = default(Vector);
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return offset;
            }

            if (node is Visual visual)
            {
                offset += VisualTreeHelper.GetOffset(visual);
            }
        }

        return null;
    }

    /// <summary>Den ScrollViewer der Quelle suchen und anmelden (Quelle gewechselt, Vorlage inzwischen da).</summary>
    private void Resolve()
    {
        var viewer = Source is { } source ? FindViewer(source) : null;
        if (ReferenceEquals(viewer, _viewer))
        {
            return;
        }

        StopPaging();
        _pagedByClick = false;
        if (_viewer is { } old)
        {
            old.ScrollChanged -= _sourceScroll;
            old.PreviewMouseWheel -= _sourceWheel;
            old.PreviewKeyDown -= _sourceKey;
            old.PreviewMouseDown -= _sourceMouse;
        }

        _viewer = viewer;
        if (viewer is not null)
        {
            viewer.ScrollChanged += _sourceScroll;
            viewer.PreviewMouseWheel += _sourceWheel;
            viewer.PreviewKeyDown += _sourceKey;
            viewer.PreviewMouseDown += _sourceMouse;
        }

        Sync();
        UpdateTrackMargin();
    }

    /// <summary>
    /// Nach jedem Layout-Durchgang (nie im Leerlauf): Vorlage der Quelle inzwischen da? Bahn noch auf der Höhe ihres sichtbaren
    /// Bereichs (Hinweise, Titelbereich, Werkzeugleiste, Fenstergröße)? Ändert nur bei einer Abweichung etwas.
    /// </summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (Source is null || Visibility != Visibility.Visible)
        {
            return;
        }

        if (_viewer is null)
        {
            Resolve();
        }

        UpdateTrackMargin();
    }

    /// <summary>
    /// Bahn von der Oberkante des sichtbaren Bereichs bis zu seiner Unterkante; im Raster beginnt sie mit der ersten
    /// Kachelreihe (unter dem Innenabstand des Kachel-Panels), wie sie in der Liste mit der ersten Zeile beginnt.
    /// </summary>
    private void UpdateTrackMargin()
    {
        var margin = default(Thickness);
        if (_viewer is { } viewer && VisualTreeHelper.GetParent(this) is { } parent
            && LayoutOffset(viewer, parent) is { } source && LayoutOffset(this, parent) is { } self)
        {
            var inset = TilePanel(viewer) is { } panel ? panel.Padding.Top : 0;
            var top = source.Y + inset - self.Y;
            var bottom = (self.Y + ActualHeight) - (source.Y + viewer.ActualHeight);
            margin = new Thickness(0, Math.Max(0, top), 0, Math.Max(0, bottom));
        }

        if (TrackMargin != margin)
        {
            SetValue(TrackMarginPropertyKey, margin);
        }
    }

    private void OnSourceScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _viewer))
        {
            return; // ein ScrollViewer in einer Zeile oder Kachel
        }

        // Etwas anderes hat gescrollt (Auswahl sichtbar machen, Sortieren): Blättern beenden statt dagegen zu laufen.
        if (!double.IsNaN(_pageTarget) && Math.Abs(e.VerticalOffset - _lastAnimated) > 1.5)
        {
            StopPaging();
        }

        Sync();
    }

    /// <summary>Werte der Leiste aus der Ansicht: Bereich, sichtbarer Teil (Länge der Pille) und Lage.</summary>
    private void Sync()
    {
        _syncing = true;
        try
        {
            var viewer = _viewer;
            Minimum = 0;
            Maximum = viewer is null ? 0 : Math.Max(0, viewer.ScrollableHeight);
            ViewportSize = viewer is null ? 0 : Math.Max(0, viewer.ViewportHeight);
            LargeChange = Math.Max(1, ViewportSize);
            SmallChange = LineStep;
            Value = viewer?.VerticalOffset ?? 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Klick über bzw. unter die Pille blättert dorthin; auf ihrer Höhe (neben ihr) oder ohne Bildlauf nichts.</summary>
    private bool PageToward(double y)
    {
        if (_viewer is null || Maximum <= 0 || Track?.Thumb is not { ActualHeight: > 0 } thumb)
        {
            return false;
        }

        var top = thumb.TranslatePoint(default, this).Y;
        var direction = y < top ? -1 : y > top + thumb.ActualHeight ? 1 : 0;
        PageBy(direction);
        return direction != 0;
    }

    private void ScrollSmoothly(double target)
    {
        if (_viewer is not { } viewer)
        {
            return;
        }

        var duration = PageTransition();
        if (duration <= TimeSpan.Zero)
        {
            StopPaging();
            viewer.ScrollToVerticalOffset(target);
            return;
        }

        var from = double.IsNaN(_pageTarget) ? viewer.VerticalOffset : (double)GetValue(AnimatedOffsetProperty);
        var token = ++_pageToken;
        _pageTarget = target;
        _lastAnimated = from;
        var animation = new DoubleAnimation(from, target, duration) { EasingFunction = PageEase };
        animation.Completed += (_, _) =>
        {
            if (token == _pageToken)
            {
                StopPaging();
            }
        };
        BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnAnimatedOffset(double value)
    {
        // Nur während geblättert wird: Endet die Animation, fällt die Eigenschaft auf ihren Grundwert zurück.
        if (!double.IsNaN(_pageTarget) && _viewer is { } viewer)
        {
            _lastAnimated = value;
            viewer.ScrollToVerticalOffset(value);
        }
    }

    private void StopPaging()
    {
        if (double.IsNaN(_pageTarget))
        {
            return;
        }

        _pageTarget = double.NaN;
        _pageToken++;
        BeginAnimation(AnimatedOffsetProperty, null);
    }

    private bool IsOnThumb(DependencyObject? element)
    {
        for (var node = element; node is not null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
        {
            if (node is Thumb)
            {
                return true;
            }
        }

        return false;
    }

    private void BeginResize()
    {
        _resizing = true;
        _resizeOrigin = VisualTreeHelper.GetOffset(this).X;
        SetValue(IsResizingPropertyKey, true);
        _keyWindow = Window.GetWindow(this);
        _keyWindow?.AddHandler(Keyboard.PreviewKeyDownEvent, _escape, handledEventsToo: true);
        Splitter?.RaiseEvent(new DragStartedEventArgs(_pressPoint.X, _pressPoint.Y));
    }

    private void EndResize(bool canceled, double horizontalChange)
    {
        _resizing = false;
        SetValue(IsResizingPropertyKey, false);
        _keyWindow?.RemoveHandler(Keyboard.PreviewKeyDownEvent, _escape);
        _keyWindow = null;
        Splitter?.RaiseEvent(new DragCompletedEventArgs(horizontalChange, 0, canceled));
    }

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _resizing)
        {
            CancelResize();
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}
