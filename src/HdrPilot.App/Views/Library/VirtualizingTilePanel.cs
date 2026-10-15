using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Views.Library;

/// <summary>Anordnung der Kacheln: Raster (senkrecht blättern) oder Regal (eine Reihe, waagerecht blättern).</summary>
public enum TileArrangement
{
    Grid,
    Shelf,
}

/// <summary>
/// Virtualisierte Kacheln für Raster und Regal (E89, G10.5). Im Raster folgen Spaltenzahl und Kachelgröße bei jeder
/// Breite neu <see cref="RasterLayout.Compute"/> (nie eine alte Kachelbreite, nie waagerechter Bildlauf); im Regal eine
/// Reihe nach <see cref="ShowcaseLayout.Shelf"/>. Es entstehen nur die sichtbaren Kacheln (plus eine Reihe bzw. vier
/// Kacheln Vorrat), Container werden wiederverwendet. Bildlauf pixelgenau über <see cref="IScrollInfo"/>; im Regal blättert
/// <see cref="PageBy"/> seitenweise (weich nach Effektstufe).
/// </summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ArrangementProperty = DependencyProperty.Register(
        nameof(Arrangement), typeof(TileArrangement), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(TileArrangement.Grid, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(RasterLayout.DefaultGap, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(new Thickness(4, 12, 4, 24), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Weicher Bildlauf des Regals (animierbar), schreibt den waagerechten Versatz.</summary>
    public static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedOffset), typeof(double), typeof(VirtualizingTilePanel),
        new PropertyMetadata(0.0, (d, e) => ((VirtualizingTilePanel)d).SetOffset((double)e.NewValue)));

    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private double _tileWidth;
    private double _tileHeight;
    private int _columns = 1;
    private int _count;

    /// <summary>Spalten, Kachelgröße oder sichtbarer Bereich haben sich geändert (Regal: „1–7 von 651“).</summary>
    public event EventHandler? LayoutUpdatedTiles;

    public TileArrangement Arrangement
    {
        get => (TileArrangement)GetValue(ArrangementProperty);
        set => SetValue(ArrangementProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public double AnimatedOffset
    {
        get => (double)GetValue(AnimatedOffsetProperty);
        set => SetValue(AnimatedOffsetProperty, value);
    }

    /// <summary>Spalten im Raster (Regal: so viele, wie ganz sichtbar sind).</summary>
    public int Columns => _columns;

    public double TileWidth => _tileWidth;

    public double TileHeight => _tileHeight;

    /// <summary>Breite, für die das Raster zuletzt berechnet wurde (ohne Innenabstand).</summary>
    public double LayoutWidth { get; private set; }

    /// <summary>Erste ganz oder teilweise sichtbare Kachel (Index in der Sicht).</summary>
    public int FirstVisibleIndex { get; private set; }

    /// <summary>Anzahl ganz sichtbarer Kacheln (Regal) bzw. sichtbarer Zeilen × Spalten (Raster).</summary>
    public int VisibleCount { get; private set; }

    public int ItemCount => _count;

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    private bool IsShelf => Arrangement == TileArrangement.Shelf;

    private double PitchX => _tileWidth + Gap;

    private double PitchY => _tileHeight + Gap;

    /// <summary>Regal: um so viele Seiten weiterblättern (weich, Dauer nach Effektstufe; 0 sofort).</summary>
    public void PageBy(int pages, TimeSpan duration)
    {
        if (!IsShelf || _count == 0)
        {
            return;
        }

        var step = Math.Max(1, VisibleCount);
        var first = Math.Clamp(FirstVisibleIndex + (pages * step), 0, Math.Max(0, _count - step));
        ScrollToIndex(first, duration);
    }

    /// <summary>Regal: Kachel an den Anfang holen (weich nach Effektstufe).</summary>
    public void ScrollToIndex(int index, TimeSpan duration)
    {
        if (!IsShelf)
        {
            BringIndexIntoViewPublic(index);
            return;
        }

        var target = Math.Clamp(index * PitchX, 0, Math.Max(0, _extent.Width - _viewport.Width));
        if (duration <= TimeSpan.Zero)
        {
            BeginAnimation(AnimatedOffsetProperty, null);
            SetOffset(target);
            return;
        }

        BeginAnimation(AnimatedOffsetProperty, new DoubleAnimation(_offset.X, target, duration) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 } });
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= _count || _tileWidth <= 0)
        {
            return;
        }

        if (IsShelf)
        {
            var left = index * PitchX;
            if (left < _offset.X)
            {
                SetOffset(left);
            }
            else if (left + _tileWidth > _offset.X + _viewport.Width - Padding.Right)
            {
                SetOffset(left + _tileWidth - _viewport.Width + Padding.Left + Padding.Right);
            }

            return;
        }

        var row = index / Math.Max(1, _columns);
        var top = Padding.Top + (row * PitchY);
        if (top - Padding.Top < _offset.Y)
        {
            SetVerticalOffset(top - Padding.Top);
        }
        else if (top + _tileHeight > _offset.Y + _viewport.Height - Padding.Bottom)
        {
            SetVerticalOffset(top + _tileHeight + Padding.Bottom - _viewport.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Der Zugriff auf die Kinder bereitet den Container-Erzeuger vor (VirtualizingPanel), erst dann gilt ItemContainerGenerator.
        _ = InternalChildren;
        var owner = ItemsControl.GetItemsOwner(this);
        _count = owner?.Items.Count ?? 0;

        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height;
        UpdateMetrics(width, height);

        var viewport = new Size(width, height);
        var extent = IsShelf
            ? new Size(Padding.Left + (_count * PitchX) - (_count > 0 ? Gap : 0) + Padding.Right, height)
            : new Size(width, Padding.Top + (Rows * PitchY) - (Rows > 0 ? Gap : 0) + Padding.Bottom);
        UpdateScrollInfo(viewport, extent);

        var (first, last) = RealizedRange();
        var tile = new Size(_tileWidth, _tileHeight);
        if (_count > 0 && last >= first)
        {
            CleanUp(first, last);
            Realize(first, last, tile);
        }
        else
        {
            CleanUp(0, -1);
        }

        UpdateVisible();
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return finalSize;
        }

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0)
            {
                continue;
            }

            child.Arrange(TileRect(index));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                SetOffsetsToStart();
                break;
        }

        base.OnItemsChanged(sender, args);
    }

    protected override void OnClearChildren()
    {
        base.OnClearChildren();
        SetOffsetsToStart();
    }

    /// <summary>Lage einer Kachel in Koordinaten des Panels (Versatz des Bildlaufs abgezogen).</summary>
    public Rect TileRect(int index)
    {
        if (IsShelf)
        {
            return new Rect(Padding.Left + (index * PitchX) - _offset.X, Padding.Top, _tileWidth, _tileHeight);
        }

        var columns = Math.Max(1, _columns);
        var row = index / columns;
        var column = index % columns;
        return new Rect(Padding.Left + (column * PitchX), Padding.Top + (row * PitchY) - _offset.Y, _tileWidth, _tileHeight);
    }

    private int Rows => _count == 0 ? 0 : (_count + Math.Max(1, _columns) - 1) / Math.Max(1, _columns);

    private void UpdateMetrics(double width, double height)
    {
        var columns = _columns;
        var tileWidth = _tileWidth;
        if (IsShelf)
        {
            var shelf = ShowcaseLayout.Shelf(width, height - Padding.Top - Padding.Bottom + 34, Gap);
            _tileWidth = shelf.TileWidth;
            _tileHeight = shelf.TileHeight;
            _columns = shelf.Visible;
            LayoutWidth = width;
        }
        else
        {
            LayoutWidth = Math.Max(0, width - Padding.Left - Padding.Right);
            var raster = RasterLayout.Compute(LayoutWidth, Gap);
            _tileWidth = raster.TileWidth;
            _tileHeight = raster.TileHeight;
            _columns = raster.Columns;
        }

        if (columns != _columns || Math.Abs(tileWidth - _tileWidth) > 0.01)
        {
            // Neue Spalten: die oberste sichtbare Zeile bleibt oben.
            LayoutUpdatedTiles?.Invoke(this, EventArgs.Empty);
        }
    }

    private (int First, int Last) RealizedRange()
    {
        if (_count == 0 || _tileWidth <= 0)
        {
            return (0, -1);
        }

        if (IsShelf)
        {
            var first = Math.Max(0, (int)Math.Floor((_offset.X - Padding.Left) / PitchX) - 4);
            var last = Math.Min(_count - 1, (int)Math.Ceiling((_offset.X + _viewport.Width) / PitchX) + 4);
            return (first, last);
        }

        var columns = Math.Max(1, _columns);
        var firstRow = Math.Max(0, (int)Math.Floor((_offset.Y - Padding.Top) / PitchY) - 1);
        var lastRow = (int)Math.Ceiling((_offset.Y + _viewport.Height) / PitchY) + 1;
        return (firstRow * columns, Math.Min(_count - 1, ((lastRow + 1) * columns) - 1));
    }

    private void Realize(int first, int last, Size tile)
    {
        var generator = ItemContainerGenerator;
        var position = generator.GeneratorPositionFromIndex(first);
        var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (var index = first; index <= last; index++, childIndex++)
            {
                if (generator.GenerateNext(out var newlyRealized) is not UIElement child)
                {
                    break;
                }

                if (newlyRealized || !ReferenceEquals(VisualTreeHelper.GetParent(child), this))
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(tile);
            }
        }
    }

    private void CleanUp(int first, int last)
    {
        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return;
        }

        var recycler = generator as IRecyclingItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last)
            {
                continue;
            }

            if (recycler is not null && VirtualizingPanel.GetVirtualizationMode(ItemsControl.GetItemsOwner(this)) == VirtualizationMode.Recycling)
            {
                recycler.Recycle(position, 1);
            }
            else
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(i, 1);
        }
    }

    private void UpdateVisible()
    {
        if (_count == 0 || _tileWidth <= 0)
        {
            FirstVisibleIndex = 0;
            VisibleCount = 0;
        }
        else if (IsShelf)
        {
            FirstVisibleIndex = Math.Clamp((int)Math.Round(_offset.X / PitchX), 0, _count - 1);
            VisibleCount = Math.Min(_columns, _count - FirstVisibleIndex);
        }
        else
        {
            var columns = Math.Max(1, _columns);
            var firstRow = Math.Max(0, (int)Math.Floor((_offset.Y - Padding.Top) / PitchY));
            FirstVisibleIndex = Math.Min(_count - 1, firstRow * columns);
            var rows = (int)Math.Ceiling(_viewport.Height / PitchY);
            VisibleCount = Math.Min(_count - FirstVisibleIndex, rows * columns);
        }

        LayoutUpdatedTiles?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        var changed = viewport != _viewport || extent != _extent;
        _viewport = viewport;
        _extent = extent;
        var maxX = Math.Max(0, _extent.Width - _viewport.Width);
        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        var clamped = new Point(Math.Clamp(_offset.X, 0, maxX), Math.Clamp(_offset.Y, 0, maxY));
        if (clamped != _offset)
        {
            _offset = clamped;
            changed = true;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private void SetOffsetsToStart()
    {
        _offset = default;
        BeginAnimation(AnimatedOffsetProperty, null);
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    private void SetOffset(double x)
    {
        if (IsShelf)
        {
            SetHorizontalOffset(x);
        }
        else
        {
            SetVerticalOffset(x);
        }
    }

    // ---------- IScrollInfo ----------

    public void LineUp() => SetVerticalOffset(_offset.Y - 48);

    public void LineDown() => SetVerticalOffset(_offset.Y + 48);

    public void LineLeft() => SetHorizontalOffset(_offset.X - 48);

    public void LineRight() => SetHorizontalOffset(_offset.X + 48);

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);

    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);

    public void MouseWheelUp()
    {
        if (IsShelf)
        {
            SetHorizontalOffset(_offset.X - (SystemParameters.WheelScrollLines * 16));
        }
        else
        {
            SetVerticalOffset(_offset.Y - (SystemParameters.WheelScrollLines * 16));
        }
    }

    public void MouseWheelDown()
    {
        if (IsShelf)
        {
            SetHorizontalOffset(_offset.X + (SystemParameters.WheelScrollLines * 16));
        }
        else
        {
            SetVerticalOffset(_offset.Y + (SystemParameters.WheelScrollLines * 16));
        }
    }

    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - (SystemParameters.WheelScrollLines * 16));

    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + (SystemParameters.WheelScrollLines * 16));

    public void SetHorizontalOffset(double offset)
    {
        var x = Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, Math.Max(0, _extent.Width - _viewport.Width));
        if (Math.Abs(x - _offset.X) < 0.01)
        {
            return;
        }

        _offset.X = x;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        var y = Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(y - _offset.Y) < 0.01)
        {
            return;
        }

        _offset.Y = y;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement element && ItemsControl.GetItemsOwner(this) is { } owner
            && (owner.ContainerFromElement(element) ?? element) is UIElement container
            && InternalChildren.IndexOf(container) is >= 0 and var child)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(child, 0));
            if (index >= 0)
            {
                BringIndexIntoView(index);
            }
        }

        return rectangle;
    }
}
