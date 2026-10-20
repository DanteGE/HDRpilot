using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Resources;

namespace HdrPilot.App.Views.Library;

/// <summary>Symbol eines Filters: was es in der Liste zeigt (Wunsch des Nutzers, eindeutig ohne Text).</summary>
public enum FilterGlyphKind
{
    /// <summary>Mini-Schalter wie in der Spalte „HDR AUTOMATISCH“.</summary>
    Switch,

    /// <summary>Plakette „NEU“.</summary>
    New,

    /// <summary>Plakette „HDR“.</summary>
    Hdr,
}

/// <summary>
/// Symbol der Filterknöpfe und der Kapsel (Vorschau .msw, .plq): Mini-Schalter (Spur 18×10, Kontur 1,6, Knopf 6) bzw.
/// Plakette 26×13 (Radius 4, 8,5 Bold; 22 war zu eng, das „R“ berührte die Kontur). Aus: Kontur in
/// <see cref="Stroke"/>; an (<see cref="OnAmount"/> 1): gefüllt im Akzentverlauf, die Schrift in <see cref="OnForeground"/>,
/// der Knopf des Schalters gleitet nach rechts (<see cref="KnobPosition"/>) und wird weiß. Gezeichnet in OnRender (eine
/// Fläche, kein Layout); beide Werte lassen sich animieren.
/// </summary>
public sealed class FilterGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FilterGlyphKind), typeof(FilterGlyph), new FrameworkPropertyMetadata(FilterGlyphKind.Switch, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OnAmountProperty = DependencyProperty.Register(
        nameof(OnAmount), typeof(double), typeof(FilterGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KnobPositionProperty = DependencyProperty.Register(
        nameof(KnobPosition), typeof(double), typeof(FilterGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FilterGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(FilterGlyph), new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OnForegroundProperty = DependencyProperty.Register(
        nameof(OnForeground), typeof(Brush), typeof(FilterGlyph), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KnobOnProperty = DependencyProperty.Register(
        nameof(KnobOn), typeof(Brush), typeof(FilterGlyph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface PlaqueFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public FilterGlyph()
    {
        SnapsToDevicePixels = true;
    }

    public FilterGlyphKind Kind
    {
        get => (FilterGlyphKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>0 aus (Kontur), 1 an (gefüllt); dazwischen überblendet.</summary>
    public double OnAmount
    {
        get => (double)GetValue(OnAmountProperty);
        set => SetValue(OnAmountProperty, value);
    }

    /// <summary>Knopf des Schalters: 0 links, 1 rechts (8 px weiter).</summary>
    public double KnobPosition
    {
        get => (double)GetValue(KnobPositionProperty);
        set => SetValue(KnobPositionProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush OnForeground
    {
        get => (Brush)GetValue(OnForegroundProperty);
        set => SetValue(OnForegroundProperty, value);
    }

    public Brush KnobOn
    {
        get => (Brush)GetValue(KnobOnProperty);
        set => SetValue(KnobOnProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Kind == FilterGlyphKind.Switch ? new Size(18, 10) : new Size(26, 13);

    protected override void OnRender(DrawingContext dc)
    {
        var on = Math.Clamp(OnAmount, 0, 1);
        if (Kind == FilterGlyphKind.Switch)
        {
            // Spur: Kontur 1,6 innen, an im Akzentverlauf; Knopf 6 px, gleitet 8 px nach rechts.
            var track = new Rect(0, 0, 18, 10);
            if (on < 1)
            {
                dc.PushOpacity(1 - on);
                dc.DrawRoundedRectangle(null, new Pen(Stroke, 1.6), Inset(track, 0.8), 4.2, 4.2);
                dc.Pop();
            }

            if (on > 0)
            {
                dc.PushOpacity(on);
                dc.DrawRoundedRectangle(Fill, null, track, 5, 5);
                dc.Pop();
            }

            var x = 2 + (8 * Math.Clamp(KnobPosition, -0.2, 1.2));
            var knob = new Rect(x, 2, 6, 6);
            dc.DrawEllipse(Stroke, null, Center(knob), 3, 3);
            if (on > 0)
            {
                dc.PushOpacity(on);
                dc.DrawEllipse(KnobOn, null, Center(knob), 3, 3);
                dc.Pop();
            }

            return;
        }

        // Plakette: aus als Kontur 1 px innen, an gefüllt; Schrift 8,5 Bold mittig.
        var plaque = new Rect(0, 0, 26, 13);
        if (on < 1)
        {
            dc.PushOpacity(1 - on);
            dc.DrawRoundedRectangle(null, new Pen(Stroke, 1), Inset(plaque, 0.5), 3.5, 3.5);
            dc.DrawText(Text(Stroke), TextOrigin());
            dc.Pop();
        }

        if (on > 0)
        {
            dc.PushOpacity(on);
            dc.DrawRoundedRectangle(Fill, null, plaque, 4, 4);
            dc.DrawText(Text(OnForeground), TextOrigin());
            dc.Pop();
        }
    }

    private FormattedText Text(Brush brush) => new(
        Kind == FilterGlyphKind.New ? LibraryStrings.Badge_New : LibraryStrings.Badge_Hdr,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        PlaqueFace,
        8.5,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Point TextOrigin()
    {
        var text = Text(Brushes.Black);
        return new Point(Math.Round((26 - text.WidthIncludingTrailingWhitespace) / 2 + 0.2, 1), Math.Round((13 - text.Height) / 2, 1));
    }

    private static Rect Inset(Rect r, double by) => new(r.X + by, r.Y + by, Math.Max(0, r.Width - (2 * by)), Math.Max(0, r.Height - (2 * by)));

    private static Point Center(Rect r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));
}
